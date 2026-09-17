using AzureResourceLister.Data;
using AzureResourceLister.Dto;
using AzureResourceLister.Models;
using Microsoft.EntityFrameworkCore;

namespace AzureResourceLister;

/// <summary>
/// Simple CRUD over the Application table for the admin grid.
/// Deliberately kept separate from ApplicationImportService — this service only
/// ever touches rows that already exist; it never talks to Azure or the AI.
/// </summary>
public class ApplicationAdminService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    private const string Unassigned = "UNASSIGNED";

    public ApplicationAdminService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<ApplicationRow>> GetAllAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        return await db.Applications
            .OrderBy(a => a.ApplicationName)
            .Select(a => new ApplicationRow
            {
                Id              = a.Id,
                ApplicationName = a.ApplicationName,
                Budget          = a.Budget,
                ResourceCount   = db.ResourceApps.Count(ra => ra.ApplicationId == a.Id),
            })
            .ToListAsync();
    }

    public async Task UpdateAsync(int id, string applicationName, decimal budget)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var app = await db.Applications.FindAsync(id)
            ?? throw new InvalidOperationException("Application not found.");

        var normalised = applicationName.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalised))
            throw new InvalidOperationException("Application name cannot be blank.");

        app.ApplicationName = normalised;
        app.Budget = budget;

        await db.SaveChangesAsync();
    }

    /// <summary>Deletes an application. Refuses if resources still reference it, or if it's the Unassigned placeholder.</summary>
    public async Task<(bool Success, string? Error)> DeleteAsync(int id)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var app = await db.Applications.FindAsync(id);
        if (app is null)
            return (false, "Application not found.");

        if (app.ApplicationName.Equals(Unassigned, StringComparison.OrdinalIgnoreCase))
            return (false, "The 'Unassigned' placeholder cannot be deleted.");

        var resourceCount = await db.ResourceApps.CountAsync(ra => ra.ApplicationId == id);
        if (resourceCount > 0)
            return (false, $"Cannot delete — {resourceCount} resource(s) still reference this application.");

        db.Applications.Remove(app);
        await db.SaveChangesAsync();
        return (true, null);
    }
}
