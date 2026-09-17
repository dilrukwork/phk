using AzureResourceLister.Data;
using AzureResourceLister.Dto;
using AzureResourceLister.Models;
using Microsoft.EntityFrameworkCore;

namespace AzureResourceLister;

/// <summary>
/// Manages the SharedAppImportException backlog. The only editable field is AzureResourceId
/// (AzureResourceName column) — since that's the sole match key, it's the only thing that
/// could be wrong. Resync tries the corrected ID and promotes the row on success.
/// </summary>
public class SharedAppExceptionAdminService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    private const string SharedAppImportSource = "SharedAppImport";

    public SharedAppExceptionAdminService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<PagedResult<SharedAppImportExceptionRow>> GetPagedAsync(int pageNumber, int pageSize, string? search = null)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var query = db.SharedAppImportExceptions.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(ex =>
                ex.ApplicationName.Contains(term) ||
                ex.AzureResourceName.Contains(term));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(ex => ex.LoggedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(ex => new SharedAppImportExceptionRow
            {
                Id                = ex.Id,
                ApplicationName   = ex.ApplicationName,
                OwnerName         = ex.OwnerName,
                AzureResourceName = ex.AzureResourceName, // stores the full ARM ID
                SubscriptionName  = ex.SubscriptionName,
                ResourceGroup     = ex.ResourceGroup,
                Reason            = ex.Reason,
                LoggedAt          = ex.LoggedAt,
            })
            .ToListAsync();

        return new PagedResult<SharedAppImportExceptionRow>
        {
            Items      = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize   = pageSize,
        };
    }

    /// <summary>
    /// Saves the corrected AzureResourceId and tries to resolve it.
    /// On success, links the application and removes the exception.
    /// On failure, the edit is still saved.
    /// </summary>
    public async Task<(bool Resolved, string? Error)> ResyncAsync(int exceptionId, string editedAzureResourceId)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var exception = await db.SharedAppImportExceptions.FindAsync(exceptionId);
        if (exception is null)
            return (false, "Exception row not found — it may have already been resolved.");

        exception.AzureResourceName = editedAzureResourceId.Trim();

        var resource = await FindResourceAsync(db, exception.AzureResourceName);
        if (resource is null)
        {
            await db.SaveChangesAsync();
            return (false, "Saved, but no matching resource was found for this Azure Resource ID.");
        }

        var applicationId = await GetOrCreateAsync<Application>(db,
            a => a.ApplicationName.ToUpper() == exception.ApplicationName.Trim().ToUpperInvariant(),
            () => new Application { ApplicationName = exception.ApplicationName.Trim().ToUpperInvariant(), Budget = 0m });

        await LinkApplicationToResourceAsync(db, resource.Id, applicationId);

        db.SharedAppImportExceptions.Remove(exception);
        await db.SaveChangesAsync();

        return (true, null);
    }

    /// <summary>Re-checks every exception's stored AzureResourceId against the Resource table.</summary>
    public async Task<int> ResyncAllAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var exceptions = await db.SharedAppImportExceptions.ToListAsync();
        int resolved = 0;

        foreach (var exception in exceptions)
        {
            var resource = await FindResourceAsync(db, exception.AzureResourceName);
            if (resource is null) continue;

            var applicationId = await GetOrCreateAsync<Application>(db,
                a => a.ApplicationName.ToUpper() == exception.ApplicationName.Trim().ToUpperInvariant(),
                () => new Application { ApplicationName = exception.ApplicationName.Trim().ToUpperInvariant(), Budget = 0m });

            await LinkApplicationToResourceAsync(db, resource.Id, applicationId);

            db.SharedAppImportExceptions.Remove(exception);
            resolved++;
        }

        await db.SaveChangesAsync();
        return resolved;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<Resource?> FindResourceAsync(AppDbContext db, string azureResourceId)
    {
        var key = azureResourceId.Trim().ToLowerInvariant();
        return await db.Resources
            .Where(r => r.AzureResourceId.ToLower() == key)
            .OrderByDescending(r => r.IsActive)
            .FirstOrDefaultAsync();
    }

    private static async Task<int> GetOrCreateAsync<T>(
        AppDbContext db,
        System.Linq.Expressions.Expression<Func<T, bool>> predicate,
        Func<T> factory) where T : class
    {
        var existing = await db.Set<T>().FirstOrDefaultAsync(predicate);
        if (existing is not null)
            return (int)existing.GetType().GetProperty("Id")!.GetValue(existing)!;

        var entity = factory();
        db.Set<T>().Add(entity);
        await db.SaveChangesAsync();
        return (int)entity.GetType().GetProperty("Id")!.GetValue(entity)!;
    }

    private static async Task LinkApplicationToResourceAsync(AppDbContext db, int resourceId, int applicationId)
    {
        var existingLinks = await db.ResourceApps
            .Where(ra => ra.ResourceId == resourceId && ra.Source == SharedAppImportSource)
            .ToListAsync();

        var applicationIds = existingLinks.Select(ra => ra.ApplicationId).Distinct().ToHashSet();
        applicationIds.Add(applicationId);

        var weight = Math.Round(1m / applicationIds.Count, 4);

        db.ResourceApps.RemoveRange(existingLinks);
        foreach (var appId in applicationIds)
        {
            db.ResourceApps.Add(new ResourceApp
            {
                ResourceId    = resourceId,
                ApplicationId = appId,
                Weight        = weight,
                Source        = SharedAppImportSource,
            });
        }
    }
}
