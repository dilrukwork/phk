using AzureResourceLister.Data;
using AzureResourceLister.Dto;
using AzureResourceLister.Models;
using Microsoft.EntityFrameworkCore;

namespace AzureResourceLister;

/// <summary>
/// Manages the ResourceCostException backlog: rows from the billing sheet whose
/// AzureResourceId didn't match anything in the Resource table at import time.
/// Lets you correct the ID (e.g. it was missing when the cost sheet was imported,
/// or had a typo) and resync it — on success, the row becomes a real ResourceCost
/// and is removed from the exception list.
/// </summary>
public class CostExceptionAdminService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public CostExceptionAdminService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<PagedResult<CostExceptionRow>> GetPagedAsync(int pageNumber, int pageSize, string? search = null)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var query = db.ResourceCostExceptions.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(ex => ex.AzureResourceId.Contains(term));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderByDescending(ex => ex.LoggedAt)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(ex => new CostExceptionRow
            {
                Id                  = ex.Id,
                AzureResourceId     = ex.AzureResourceId,
                MeterCategory       = ex.MeterCategory,
                MeterSubCategory    = ex.MeterSubCategory,
                MeterName           = ex.MeterName,
                Quantity            = ex.Quantity,
                Cost                = ex.Cost,
                BillingPeriodStart  = ex.BillingPeriodStart,
                SourceFile          = ex.SourceFile,
                LoggedAt            = ex.LoggedAt,
            })
            .ToListAsync();

        return new PagedResult<CostExceptionRow>
        {
            Items      = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize   = pageSize,
        };
    }

    /// <summary>
    /// Saves the (possibly edited) AzureResourceId, then tries to match it against the
    /// Resource table. On success, creates the ResourceCost row and deletes the exception.
    /// On failure, the edit is still saved so it isn't lost, but the row remains an exception.
    /// </summary>
    public async Task<(bool Resolved, string? Error)> ResyncAsync(int exceptionId, string editedAzureResourceId)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var exception = await db.ResourceCostExceptions.FindAsync(exceptionId);
        if (exception is null)
            return (false, "Exception row not found — it may have already been resolved.");

        exception.AzureResourceId = editedAzureResourceId.Trim();

        var resourceId = await FindMatchingResourceIdAsync(db, exception.AzureResourceId);
        if (resourceId is null)
        {
            await db.SaveChangesAsync(); // keep the edit even though it still didn't match
            return (false, "Saved, but no matching resource was found for this ID.");
        }

        PromoteToResourceCost(db, exception, resourceId.Value);
        await db.SaveChangesAsync();

        return (true, null);
    }

    /// <summary>
    /// Re-checks every current exception's stored AzureResourceId against the Resource
    /// table as it stands now — useful after a later Resources refresh has brought in
    /// resources that didn't exist yet when the cost sheet was first imported.
    /// </summary>
    public async Task<int> ResyncAllAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var exceptions = await db.ResourceCostExceptions.ToListAsync();
        int resolved = 0;

        foreach (var exception in exceptions)
        {
            var resourceId = await FindMatchingResourceIdAsync(db, exception.AzureResourceId);
            if (resourceId is null) continue;

            try
            {
                PromoteToResourceCost(db, exception, resourceId.Value);
                resolved++;
            }
            catch (InvalidOperationException)
            {
                // Row has no MeterId — skip it rather than aborting the whole batch
            }
        }

        await db.SaveChangesAsync();
        return resolved;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<int?> FindMatchingResourceIdAsync(AppDbContext db, string azureResourceId)
    {
        var key = azureResourceId.Trim().ToLowerInvariant();

        // Prefer an active resource if both an active and a deleted row share the same id
        var match = await db.Resources
            .Where(r => r.AzureResourceId.ToLower() == key)
            .OrderByDescending(r => r.IsActive)
            .FirstOrDefaultAsync();

        return match?.Id;
    }

    private static void PromoteToResourceCost(AppDbContext db, ResourceCostException exception, int resourceId)
    {
        if (exception.MeterId is null)
            throw new InvalidOperationException(
                $"Exception row {exception.Id} has no MeterId — cannot promote to ResourceCost without a valid meter reference.");

        db.ResourceCosts.Add(new ResourceCost
        {
            AzureResourceId    = exception.AzureResourceId,
            ResourceDbId       = resourceId,
            MeterId            = exception.MeterId.Value,
            Quantity           = exception.Quantity,
            Cost               = exception.Cost,
            EffectivePrice     = exception.EffectivePrice,
            UnitPrice          = exception.UnitPrice,
            PAYGPrice          = exception.PAYGPrice,
            OfferId            = exception.OfferId,
            PricingModel       = exception.PricingModel,
            BillingPeriodStart = exception.BillingPeriodStart,
        });

        db.ResourceCostExceptions.Remove(exception);
    }
}
