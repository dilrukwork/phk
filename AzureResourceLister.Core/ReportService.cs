using AzureResourceLister.Data;
using AzureResourceLister.Dto;
using Microsoft.EntityFrameworkCore;

namespace AzureResourceLister;

public class ReportService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    private const string Unassigned = "UNASSIGNED";

    public ReportService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // ── Filter options ─────────────────────────────────────────────────────────

    public async Task<List<string>> GetCategoriesAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Meters
            .Select(m => m.MeterCategory)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
    }

    public async Task<ReportFilterOptions> GetFilterOptionsAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        return new ReportFilterOptions
        {
            Years = await db.ResourceCosts
                .Select(rc => rc.BillingPeriodStart.Year)
                .Distinct()
                .OrderByDescending(y => y)
                .ToListAsync(),

            Owners = await db.BusinessOwners
                .Where(o => o.OwnerName.ToUpper() != Unassigned)
                .OrderBy(o => o.OwnerName)
                .Select(o => o.OwnerName)
                .ToListAsync(),

            CostCentres = await db.Resources
                .Where(r => r.CostCentre != string.Empty)
                .Select(r => r.CostCentre)
                .Distinct()
                .OrderBy(c => c)
                .ToListAsync(),

            Subscriptions = await db.Subscriptions
                .OrderBy(s => s.SubscriptionName)
                .Select(s => s.SubscriptionName)
                .ToListAsync(),

            Environments = await db.AzureEnvironments
                .OrderBy(e => e.EnvironmentName)
                .Select(e => e.EnvironmentName)
                .ToListAsync(),
        };
    }

    // ── Applications report ────────────────────────────────────────────────────

    /// <summary>
    /// Returns allocated cost per application per environment for the given month.
    /// All joining and weight multiplication happens in C# — no complex LINQ-to-SQL
    /// translation of weights or large IN clauses on ResourceApp.Id.
    /// </summary>
    public async Task<CostReport<ApplicationCostRow>> GetApplicationCostsAsync(ReportFilter filter)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var environments = await db.AzureEnvironments
            .OrderBy(e => e.EnvironmentName)
            .Select(e => e.EnvironmentName)
            .ToListAsync();

        var startDate = new DateTime(filter.Year, filter.Month, 1);
        var endDate   = startDate.AddMonths(1);

        // ── Step 1: load ALL ResourceApp rows and compute valid (resource, app) allocations ──

        var allMappings = await db.ResourceApps
            .Select(ra => new { ra.ResourceId, ra.ApplicationId, ra.Weight })
            .ToListAsync();

        // A resource is "shared" if it appears more than once in ResourceApp
        var sharedResourceIds = allMappings
            .GroupBy(ra => ra.ResourceId)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet();

        // Valid allocations (keyed by (ResourceId, ApplicationId) → Weight):
        //   Dedicated resource (appears once): full cost, Weight = 1.0
        //   Shared resource (appears more than once): only fractional rows (Weight < 1.0)
        var validAllocations = allMappings
            .Where(ra =>
                   !sharedResourceIds.Contains(ra.ResourceId)
                || (sharedResourceIds.Contains(ra.ResourceId) && ra.Weight < 1.0m))
            .GroupBy(ra => (ra.ResourceId, ra.ApplicationId))
            .ToDictionary(g => g.Key, g => g.First().Weight);

        // ── Step 2: get application names (exclude UNASSIGNED) ────────────────

        var allAppIds = validAllocations.Keys.Select(k => k.ApplicationId).Distinct().ToList();
        var appNames  = await db.Applications
            .Where(a => allAppIds.Contains(a.Id) && a.ApplicationName.ToUpper() != Unassigned)
            .ToDictionaryAsync(a => a.Id, a => a.ApplicationName);

        // Only keep allocations for known (non-unassigned) applications
        var validAppIds      = appNames.Keys.ToHashSet();
        var relevantResourceIds = validAllocations.Keys
            .Where(k => validAppIds.Contains(k.ApplicationId))
            .Select(k => k.ResourceId)
            .Distinct().ToList();

        // ── Step 3: precompute optional filters ───────────────────────────────

        HashSet<int>? ownerResourceIds = null;
        if (!string.IsNullOrWhiteSpace(filter.OwnerName))
        {
            var ownerKey = filter.OwnerName.Trim().ToUpperInvariant();
            ownerResourceIds = (await db.ResourceOwners
                .Where(ro => ro.Owner.OwnerName.ToUpper() == ownerKey)
                .Select(ro => ro.ResourceId)
                .Distinct()
                .ToListAsync()).ToHashSet();
        }

        HashSet<int>? ccResourceIds = null;
        if (!string.IsNullOrWhiteSpace(filter.CostCentre))
        {
            ccResourceIds = (await db.Resources
                .Where(r => r.CostCentre == filter.CostCentre)
                .Select(r => r.Id)
                .ToListAsync()).ToHashSet();
        }

        // ── Step 4: fetch raw costs for relevant resources ────────────────────
        // Simple IN on ResourceId (a real FK column) — no ResourceApp.Id in this query at all.
        // Aggregated per resource so weight multiplication is a single multiply per (resource, env).

        var rawCostData = await (
            from r  in db.Resources
            join rc in db.ResourceCosts     on (int?)r.Id     equals rc.ResourceDbId
            join s  in db.Subscriptions     on r.SubscriptionId equals s.Id
            join e  in db.AzureEnvironments on s.EnvironmentId  equals e.Id
            where relevantResourceIds.Contains(r.Id)
               && rc.BillingPeriodStart >= startDate
               && rc.BillingPeriodStart < endDate
            select new { ResourceId = r.Id, r.CostCentre, e.EnvironmentName, rc.Cost }
        ).ToListAsync();

        // Sum cost per (ResourceId, Environment) in C#
        var costByResource = rawCostData
            .GroupBy(x => x.ResourceId)
            .ToDictionary(
                g => g.Key,
                g => (
                    TotalCost:       g.Sum(x => x.Cost),
                    CostCentre:      g.First().CostCentre,
                    EnvironmentName: g.First().EnvironmentName
                ));

        // Group valid allocations by ResourceId for O(1) lookup inside the loop
        var allocationsByResource = validAllocations
            .GroupBy(kvp => kvp.Key.ResourceId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(kvp => (AppId: kvp.Key.ApplicationId, Weight: kvp.Value)).ToList());

        // ── Step 5: distribute costs to applications using weights (pure C#) ──

        var flatData = new List<(int AppId, string AppName, string CostCentre, string EnvName, decimal AllocatedCost)>();

        foreach (var kvp in costByResource)
        {
            var resourceId = kvp.Key;
            var (totalCost, costCentre, envName) = kvp.Value;

            if (ownerResourceIds is not null && !ownerResourceIds.Contains(resourceId)) continue;
            if (ccResourceIds    is not null && !ccResourceIds.Contains(resourceId))    continue;

            if (!allocationsByResource.TryGetValue(resourceId, out var allocations)) continue;

            foreach (var (appId, weight) in allocations)
            {
                if (!appNames.TryGetValue(appId, out var appName)) continue;
                flatData.Add((appId, appName, costCentre, envName, totalCost * weight));
            }
        }

        if (flatData.Count == 0)
            return new CostReport<ApplicationCostRow> { Environments = environments };

        // ── Step 6: owners per application ────────────────────────────────────

        var appIds = flatData.Select(x => x.AppId).Distinct().ToList();

        var ownerData = await (
            from ro in db.ResourceOwners
            join ra in db.ResourceApps   on ro.ResourceId equals ra.ResourceId
            join o  in db.BusinessOwners on ro.OwnerId    equals o.Id
            where appIds.Contains(ra.ApplicationId) && o.OwnerName.ToUpper() != Unassigned
            select new { ra.ApplicationId, o.OwnerName }
        ).Distinct().ToListAsync();

        var ownerLookup = ownerData
            .GroupBy(x => x.ApplicationId)
            .ToDictionary(
                g => g.Key,
                g => string.Join(", ", g.Select(x => x.OwnerName).Distinct().OrderBy(x => x)));

        // ── Step 7: pivot ──────────────────────────────────────────────────────

        var rows = flatData
            .GroupBy(x => (x.AppId, x.AppName))
            .Select(g => new ApplicationCostRow
            {
                ApplicationName = g.Key.AppName,
                OwnerName       = ownerLookup.GetValueOrDefault(g.Key.AppId, string.Empty),
                CostCentre      = string.Join(", ",
                    g.Select(x => x.CostCentre).Where(c => !string.IsNullOrEmpty(c)).Distinct().OrderBy(c => c)),
                CostByEnvironment = environments.ToDictionary(
                    env => env,
                    env => g.Where(x => x.EnvName == env).Sum(x => x.AllocatedCost)),
            })
            .OrderBy(r => r.ApplicationName)
            .ToList();

        var activeEnvironments = environments
            .Where(env => rows.Any(r => r.CostByEnvironment.GetValueOrDefault(env) > 0))
            .ToList();

        return new CostReport<ApplicationCostRow>
        {
            Environments = activeEnvironments,
            Rows         = rows,
            GrandTotal   = rows.Sum(r => r.TotalCost),
        };
    }

    // ── Resources report ───────────────────────────────────────────────────────

    /// <summary>
    /// Returns resource cost per environment for the given month.
    /// Without application filter: full resource cost (total billing cost).
    /// With application filter (drill-down from Applications page): allocated cost —
    /// the application's weighted share of each resource, using the same allocation
    /// logic as GetApplicationCostsAsync.
    /// </summary>
    public async Task<CostReport<ResourceCostRow>> GetResourceCostsAsync(ReportFilter filter)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var startDate = new DateTime(filter.Year, filter.Month, 1);
        var endDate   = startDate.AddMonths(1);

        // ── Environments ───────────────────────────────────────────────────────

        var envQuery = db.AzureEnvironments.AsQueryable();
        if (!string.IsNullOrWhiteSpace(filter.EnvironmentName))
            envQuery = envQuery.Where(e => e.EnvironmentName == filter.EnvironmentName);

        var environments = await envQuery
            .OrderBy(e => e.EnvironmentName)
            .Select(e => e.EnvironmentName)
            .ToListAsync();

        // ── Allocation weights (only needed when filtering by application) ──────

        Dictionary<int, decimal>? weightByResourceId = null;

        if (!string.IsNullOrWhiteSpace(filter.ApplicationName))
        {
            var appKey = filter.ApplicationName.Trim().ToUpperInvariant();

            // Load all ResourceApp rows to apply the same shared/dedicated logic
            var allMappings = await db.ResourceApps
                .Select(ra => new { ra.ResourceId, ra.ApplicationId, ra.Weight })
                .ToListAsync();

            var sharedResourceIds = allMappings
                .GroupBy(ra => ra.ResourceId)
                .Where(g => g.Count() > 1)
                .Select(g => g.Key)
                .ToHashSet();

            // Get this application's Id
            var appId = await db.Applications
                .Where(a => a.ApplicationName.ToUpper() == appKey)
                .Select(a => a.Id)
                .FirstOrDefaultAsync();

            if (appId != 0)
            {
                // Find all resources this application uses, with their allocated weights
                weightByResourceId = allMappings
                    .Where(ra => ra.ApplicationId == appId
                              && (!sharedResourceIds.Contains(ra.ResourceId)
                                  || (sharedResourceIds.Contains(ra.ResourceId) && ra.Weight < 1.0m)))
                    .GroupBy(ra => ra.ResourceId)
                    .ToDictionary(g => g.Key, g => g.First().Weight);
            }
        }

        // ── Precompute other filtered resource ID sets ─────────────────────────

        HashSet<int>? ownerResourceIds = null;
        if (!string.IsNullOrWhiteSpace(filter.OwnerName))
        {
            var ownerKey = filter.OwnerName.Trim().ToUpperInvariant();
            ownerResourceIds = (await db.ResourceOwners
                .Where(ro => ro.Owner.OwnerName.ToUpper() == ownerKey)
                .Select(ro => ro.ResourceId)
                .Distinct()
                .ToListAsync()).ToHashSet();
        }

        // ── Main cost query ────────────────────────────────────────────────────

        var query =
            from r  in db.Resources
            join rc in db.ResourceCosts     on (int?)r.Id      equals rc.ResourceDbId
            join m  in db.Meters            on rc.MeterId      equals m.Id
            join s  in db.Subscriptions     on r.SubscriptionId equals s.Id
            join e  in db.AzureEnvironments on s.EnvironmentId  equals e.Id
            where rc.BillingPeriodStart >= startDate
               && rc.BillingPeriodStart < endDate
            select new
            {
                r.Id,
                r.ResourceName,
                r.CostCentre,
                s.SubscriptionName,
                EnvironmentName = e.EnvironmentName,
                rc.Cost,
                m.MeterCategory,
            };

        if (!string.IsNullOrWhiteSpace(filter.EnvironmentName))
            query = query.Where(x => x.EnvironmentName == filter.EnvironmentName);

        if (!string.IsNullOrWhiteSpace(filter.SubscriptionName))
            query = query.Where(x => x.SubscriptionName == filter.SubscriptionName);

        if (!string.IsNullOrWhiteSpace(filter.CostCentre))
            query = query.Where(x => x.CostCentre == filter.CostCentre);

        if (weightByResourceId is not null)
        {
            // Filter to only this application's resources
            var appResourceIds = weightByResourceId.Keys.ToList();
            query = query.Where(x => appResourceIds.Contains(x.Id));
        }

        if (ownerResourceIds is not null)
            query = query.Where(x => ownerResourceIds.Contains(x.Id));

        var rawData = await query.ToListAsync();

        if (rawData.Count == 0)
            return new CostReport<ResourceCostRow> { Environments = environments };

        // ── Owners per resource ────────────────────────────────────────────────

        var resourceIds = rawData.Select(x => x.Id).Distinct().ToList();

        var ownerData = await (
            from ro in db.ResourceOwners
            join o  in db.BusinessOwners on ro.OwnerId equals o.Id
            where resourceIds.Contains(ro.ResourceId) && o.OwnerName.ToUpper() != Unassigned
            select new { ro.ResourceId, o.OwnerName }
        ).Distinct().ToListAsync();

        var ownerLookup = ownerData
            .GroupBy(x => x.ResourceId)
            .ToDictionary(
                g => g.Key,
                g => string.Join(", ", g.Select(x => x.OwnerName).Distinct().OrderBy(x => x)));

        // ── Pivot — apply weight in C# if filtering by application ─────────────

        var rows = rawData
            .GroupBy(x => new { x.Id, x.ResourceName, x.SubscriptionName, x.CostCentre })
            .Select(g =>
            {
                var weight = weightByResourceId?.GetValueOrDefault(g.Key.Id, 1.0m) ?? 1.0m;

                return new ResourceCostRow
                {
                    ResourceName     = g.Key.ResourceName,
                    SubscriptionName = g.Key.SubscriptionName,
                    OwnerName        = ownerLookup.GetValueOrDefault(g.Key.Id, string.Empty),
                    CostCentre       = g.Key.CostCentre,
                    MeterCategory    = string.Join(", ", g.Select(x => x.MeterCategory)
                                           .Distinct().OrderBy(x => x)),
                    CostByEnvironment = environments.ToDictionary(
                        env => env,
                        env => g.Where(x => x.EnvironmentName == env).Sum(x => x.Cost) * weight),
                };
            })
            .OrderByDescending(r => r.TotalCost)
            .ToList();

        var activeEnvironments = string.IsNullOrWhiteSpace(filter.EnvironmentName)
            ? environments.Where(env => rows.Any(r => r.CostByEnvironment.GetValueOrDefault(env) > 0)).ToList()
            : environments;

        return new CostReport<ResourceCostRow>
        {
            Environments = activeEnvironments,
            Rows         = rows,
            GrandTotal   = rows.Sum(r => r.TotalCost),
        };
    }

    // ── Subscriptions report ───────────────────────────────────────────────────

    public async Task<List<SubscriptionCostRow>> GetSubscriptionCostsAsync(int year, int month, string? category = null)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var startDate = new DateTime(year, month, 1);
        var endDate   = startDate.AddMonths(1);

        var query =
            from s in db.Subscriptions
            join env in db.AzureEnvironments on s.EnvironmentId equals env.Id
            join r in db.Resources on s.Id equals r.SubscriptionId
            join rc in db.ResourceCosts on r.Id equals rc.ResourceDbId
            join m in db.Meters on rc.MeterId equals m.Id
            where rc.BillingPeriodStart >= startDate
               && rc.BillingPeriodStart < endDate
            select new
            {
                s.SubscriptionName,
                env.EnvironmentName,
                m.MeterCategory,
                rc.Cost,
            };

        if (!string.IsNullOrWhiteSpace(category))
        {
            query = query.Where(x => x.MeterCategory == category);
        }

        var raw = await query.ToListAsync();

        var rows = raw
            .GroupBy(x => new { x.SubscriptionName, x.EnvironmentName })
            .Select(g => new SubscriptionCostRow
            {
                SubscriptionName = g.Key.SubscriptionName,
                EnvironmentName  = g.Key.EnvironmentName,
                TotalCost        = g.Sum(x => x.Cost),
            })
            .OrderByDescending(r => r.TotalCost)
            .ThenBy(r => r.SubscriptionName)
            .ToList();

        return rows;
    }
}
