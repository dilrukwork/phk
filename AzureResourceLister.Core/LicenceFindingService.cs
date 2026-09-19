using AzureResourceLister.Data;
using AzureResourceLister.Dto;
using Microsoft.EntityFrameworkCore;

namespace AzureResourceLister;

/// <summary>
/// Queries existing billing data for Azure Hybrid Benefit saving opportunities.
/// All queries are restricted to Prod environments (any environment whose name
/// contains "prod", case-insensitive) and a specific billing period.
/// </summary>
public class LicenceFindingService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public LicenceFindingService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // ── Windows Hybrid Benefit ────────────────────────────────────────────────

    /// <summary>
    /// Returns Windows VMs billed at the Windows compute rate, indicating Azure
    /// Hybrid Benefit is NOT applied. Signal: MeterSubCategory contains "Windows"
    /// under the Virtual Machines category — if HB were active, the same VM would
    /// bill at the Linux rate and the SubCategory would not include "Windows".
    /// Prod environments only. Excludes $0 rows.
    /// </summary>
    public async Task<List<LicenceFindingRow>> GetWindowsHybridBenefitOpportunitiesAsync(int year, int month)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var startDate = new DateTime(year, month, 1);
        var endDate   = startDate.AddMonths(1);

        var rawData = await (
            from rc in db.ResourceCosts
            join m  in db.Meters             on rc.MeterId       equals m.Id
            join r  in db.Resources          on rc.ResourceDbId  equals r.Id
            join s  in db.Subscriptions      on r.SubscriptionId equals s.Id
            join e  in db.AzureEnvironments  on s.EnvironmentId  equals e.Id
            where m.MeterCategory == "Virtual Machines"
               && m.MeterSubCategory.ToLower().Contains("windows")
               && e.EnvironmentName.ToLower().Contains("prod")
               && rc.BillingPeriodStart >= startDate
               && rc.BillingPeriodStart <  endDate
            group rc by new
            {
                r.Id,
                r.ResourceName,
                r.ResourceGroup,
                s.SubscriptionName,
                e.EnvironmentName,
                m.MeterSubCategory,
            }
            into g
            where g.Sum(rc => rc.Cost) > 0
            select new
            {
                g.Key.Id,
                g.Key.ResourceName,
                g.Key.ResourceGroup,
                g.Key.SubscriptionName,
                g.Key.EnvironmentName,
                LicenceType  = g.Key.MeterSubCategory,
                MonthlyCost  = g.Sum(rc => rc.Cost),
            }
        ).ToListAsync();

        return await EnrichWithOwners(db, rawData
            .Select(x => new LicenceFindingRow
            {
                ResourceName     = x.ResourceName,
                ResourceGroup    = x.ResourceGroup,
                SubscriptionName = x.SubscriptionName,
                EnvironmentName  = x.EnvironmentName,
                LicenceType      = x.LicenceType,
                MonthlyCost      = x.MonthlyCost,
            })
            .OrderByDescending(r => r.MonthlyCost)
            .ToList(),
            rawData.Select(x => (x.Id, x.ResourceName)).ToList(),
            db);
    }

    // ── SQL Server Hybrid Benefit ─────────────────────────────────────────────

    /// <summary>
    /// Returns VMs paying full PAYG SQL Server Enterprise or Standard licence,
    /// indicating SQL Azure Hybrid Benefit is NOT applied. Developer Edition and
    /// Express Edition are excluded — they are free and HB doesn't apply.
    /// Prod environments only. Excludes $0 rows.
    /// </summary>
    public async Task<List<LicenceFindingRow>> GetSqlHybridBenefitOpportunitiesAsync(int year, int month)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var startDate = new DateTime(year, month, 1);
        var endDate   = startDate.AddMonths(1);

        var rawData = await (
            from rc in db.ResourceCosts
            join m  in db.Meters             on rc.MeterId       equals m.Id
            join r  in db.Resources          on rc.ResourceDbId  equals r.Id
            join s  in db.Subscriptions      on r.SubscriptionId equals s.Id
            join e  in db.AzureEnvironments  on s.EnvironmentId  equals e.Id
            where (m.MeterCategory == "Virtual Machines Licenses"
                || m.MeterCategory == "Virtual Machine Licenses")
               && (m.MeterSubCategory == "SQL Server Enterprise"
                || m.MeterSubCategory == "SQL Server Standard")
               && e.EnvironmentName.ToLower().Contains("prod")
               && rc.BillingPeriodStart >= startDate
               && rc.BillingPeriodStart <  endDate
            group rc by new
            {
                r.Id,
                r.ResourceName,
                r.ResourceGroup,
                s.SubscriptionName,
                e.EnvironmentName,
                m.MeterSubCategory,
            }
            into g
            where g.Sum(rc => rc.Cost) > 0
            select new
            {
                g.Key.Id,
                g.Key.ResourceName,
                g.Key.ResourceGroup,
                g.Key.SubscriptionName,
                g.Key.EnvironmentName,
                LicenceType = g.Key.MeterSubCategory,
                MonthlyCost = g.Sum(rc => rc.Cost),
            }
        ).ToListAsync();

        return await EnrichWithOwners(db, rawData
            .Select(x => new LicenceFindingRow
            {
                ResourceName     = x.ResourceName,
                ResourceGroup    = x.ResourceGroup,
                SubscriptionName = x.SubscriptionName,
                EnvironmentName  = x.EnvironmentName,
                LicenceType      = x.LicenceType,
                MonthlyCost      = x.MonthlyCost,
            })
            .OrderByDescending(r => r.MonthlyCost)
            .ToList(),
            rawData.Select(x => (x.Id, x.ResourceName)).ToList(),
            db);
    }

    // ── Filter options ────────────────────────────────────────────────────────

    /// <summary>Returns distinct billing years available, for the period picker.</summary>
    public async Task<List<int>> GetAvailableYearsAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.ResourceCosts
            .Select(rc => rc.BillingPeriodStart.Year)
            .Distinct()
            .OrderByDescending(y => y)
            .ToListAsync();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<List<LicenceFindingRow>> EnrichWithOwners(
        AppDbContext db,
        List<LicenceFindingRow> rows,
        List<(int Id, string ResourceName)> resourceIdMap,
        AppDbContext _)
    {
        if (rows.Count == 0) return rows;

        var resourceIds = resourceIdMap.Select(x => x.Id).ToList();

        var ownerData = await (
            from ro in db.ResourceOwners
            join o  in db.BusinessOwners on ro.OwnerId equals o.Id
            where resourceIds.Contains(ro.ResourceId)
               && o.OwnerName.ToUpper() != "UNASSIGNED"
            select new { ro.ResourceId, o.OwnerName }
        ).Distinct().ToListAsync();

        var ownerByResource = ownerData
            .GroupBy(x => x.ResourceId)
            .ToDictionary(
                g => g.Key,
                g => string.Join(", ", g.Select(x => x.OwnerName).Distinct().OrderBy(x => x)));

        // Map ResourceId → OwnerName via ResourceName (the rows don't carry Id)
        var idByName = resourceIdMap
            .GroupBy(x => x.ResourceName)
            .ToDictionary(g => g.Key, g => g.First().Id);

        foreach (var row in rows)
        {
            if (idByName.TryGetValue(row.ResourceName, out var rid) &&
                ownerByResource.TryGetValue(rid, out var owner))
            {
                row.OwnerName = owner;
            }
        }

        return rows;
    }
}
