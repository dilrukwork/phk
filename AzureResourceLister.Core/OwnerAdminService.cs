using AzureResourceLister.Data;
using AzureResourceLister.Dto;
using AzureResourceLister.Models;
using Microsoft.EntityFrameworkCore;

namespace AzureResourceLister;

/// <summary>
/// Simple CRUD over the BusinessOwner table for the admin grid. Mirrors
/// ApplicationAdminService — only ever touches rows that already exist.
/// </summary>
public class OwnerAdminService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    private const string Unassigned = "UNASSIGNED";

    public OwnerAdminService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<OwnerRow>> GetAllAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        return await db.BusinessOwners
            .OrderBy(o => o.OwnerName)
            .Select(o => new OwnerRow
            {
                Id            = o.Id,
                OwnerName     = o.OwnerName,
                ResourceCount = db.ResourceOwners.Count(ro => ro.OwnerId == o.Id),
            })
            .ToListAsync();
    }

    public async Task UpdateAsync(int id, string ownerName)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var owner = await db.BusinessOwners.FindAsync(id)
            ?? throw new InvalidOperationException("Owner not found.");

        var normalised = ownerName.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(normalised))
            throw new InvalidOperationException("Owner name cannot be blank.");

        owner.OwnerName = normalised;
        await db.SaveChangesAsync();
    }

    /// <summary>Deletes an owner. Refuses if resources still reference it, or if it's the Unassigned placeholder.</summary>
    public async Task<(bool Success, string? Error)> DeleteAsync(int id)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var owner = await db.BusinessOwners.FindAsync(id);
        if (owner is null)
            return (false, "Owner not found.");

        if (owner.OwnerName.Equals(Unassigned, StringComparison.OrdinalIgnoreCase))
            return (false, "The 'Unassigned' placeholder cannot be deleted.");

        var resourceCount = await db.ResourceOwners.CountAsync(ro => ro.OwnerId == id);
        if (resourceCount > 0)
            return (false, $"Cannot delete — {resourceCount} resource(s) still reference this owner.");

        db.BusinessOwners.Remove(owner);
        await db.SaveChangesAsync();
        return (true, null);
    }
}
