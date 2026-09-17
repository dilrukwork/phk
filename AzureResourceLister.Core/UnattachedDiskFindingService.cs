using AzureResourceLister.Data;
using AzureResourceLister.Dto;
using Microsoft.EntityFrameworkCore;

namespace AzureResourceLister;

/// <summary>
/// Finds managed disks that are currently unattached (not assigned to any VM).
/// Azure charges for unattached disks at the same rate as attached ones, so they
/// represent pure waste unless intentionally retained (e.g. snapshot staging).
///
/// Disk state comes from the live Azure ARM API — this cannot be determined from
/// the billing sheet alone. Monthly cost is cross-referenced from ResourceCost.
/// </summary>
public class UnattachedDiskFindingService
{
    private readonly AzureResourceService          _resourceService;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public UnattachedDiskFindingService(
        AzureResourceService resourceService,
        IDbContextFactory<AppDbContext> dbFactory)
    {
        _resourceService = resourceService;
        _dbFactory       = dbFactory;
    }

    /// <summary>
    /// Fetches all managed disks from Azure across every tracked subscription,
    /// filters to Unattached state, then enriches with the monthly cost from the
    /// given billing period.
    /// </summary>
    public async Task<List<UnattachedDiskRow>> GetUnattachedDisksAsync(int year, int month)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var subscriptions = await db.Subscriptions
            .Include(s => s.Environment)
            .ToListAsync();

        if (subscriptions.Count == 0) return [];

        var startDate = new DateTime(year, month, 1);
        var endDate   = startDate.AddMonths(1);

        // ── Monthly cost per ARM resource ID from billing data ─────────────────
        var costByArmId = await db.ResourceCosts
            .Where(rc => rc.BillingPeriodStart >= startDate && rc.BillingPeriodStart < endDate)
            .GroupBy(rc => rc.AzureResourceId.ToLower())
            .Select(g => new { ArmId = g.Key, Cost = g.Sum(rc => rc.Cost) })
            .ToDictionaryAsync(x => x.ArmId, x => x.Cost);

        // ── Owner lookup by ARM resource ID ────────────────────────────────────
        var ownerByArmId = await (
            from r  in db.Resources
            join ro in db.ResourceOwners on r.Id equals ro.ResourceId
            join o  in db.BusinessOwners on ro.OwnerId equals o.Id
            where o.OwnerName.ToUpper() != "UNASSIGNED"
            select new { ArmId = r.AzureResourceId.ToLower(), o.OwnerName }
        ).Distinct().ToListAsync();

        var ownerLookup = ownerByArmId
            .GroupBy(x => x.ArmId)
            .ToDictionary(g => g.Key, g =>
                string.Join(", ", g.Select(x => x.OwnerName).Distinct().OrderBy(x => x)));

        // ── Query Azure for live disk state ────────────────────────────────────
        var results = new List<UnattachedDiskRow>();

        foreach (var sub in subscriptions)
        {
            List<AzureDisk> disks;
            try
            {
                disks = await _resourceService.GetDisksAsync(sub.AzureSubscriptionId);
            }
            catch (Exception ex)
            {
                // Log and skip this subscription rather than aborting the whole run
                throw new InvalidOperationException(
                    $"Failed to retrieve disks for subscription '{sub.SubscriptionName}': {ex.Message}", ex);
            }

            foreach (var disk in disks)
            {
                if (!disk.DiskState.Equals("Unattached", StringComparison.OrdinalIgnoreCase))
                    continue;

                var armIdKey = disk.Id.ToLowerInvariant();

                results.Add(new UnattachedDiskRow
                {
                    ResourceName     = disk.Name,
                    ResourceGroup    = disk.ResourceGroup,
                    SubscriptionName = sub.SubscriptionName,
                    EnvironmentName  = sub.Environment?.EnvironmentName ?? string.Empty,
                    OwnerName        = ownerLookup.GetValueOrDefault(armIdKey, string.Empty),
                    DiskSizeGb       = disk.DiskSizeGb,
                    SkuName          = disk.SkuName,
                    MonthlyCost      = costByArmId.GetValueOrDefault(armIdKey, 0m),
                });
            }
        }

        return results.OrderByDescending(r => r.MonthlyCost).ToList();
    }

    public async Task<List<int>> GetAvailableYearsAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.ResourceCosts
            .Select(rc => rc.BillingPeriodStart.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync();
    }
}
