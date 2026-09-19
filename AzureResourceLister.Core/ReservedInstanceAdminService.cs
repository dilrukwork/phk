using AzureResourceLister.Data;
using AzureResourceLister.Dto;
using Microsoft.EntityFrameworkCore;

namespace AzureResourceLister;

/// <summary>
/// Compares on-demand cost against Reserved Instance pricing for eligible resources in a
/// selected subscription and billing period. One row per resource — no shared commitment
/// budget to allocate, so unlike Savings Plan there's no hour-by-hour simulation here, just
/// a direct calculation per resource.
///
/// Deliberately NOT hour-based on the reserved side: a reservation is billed for the whole
/// term regardless of whether the resource actually runs, so Reserved1YMonthly/3YMonthly are
/// a flat UnitPrice / 12 or / 36 — HoursOfOperation plays no part. OnDemandMonthly, by
/// contrast, uses the same schedule-aware calculation as Savings Plan (avgUnitPrice ×
/// expected operating hours), so a 5x12 resource's on-demand figure is already lower than a
/// 24x7 resource's on the same meter. That asymmetry is intentional — it's what lets this
/// page show that a Reserved Instance may not be worthwhile for a resource that isn't
/// running most of the time.
///
/// Reads from a dedicated Reserved Instance price sheet (a separate file and a separate
/// table, ReservedInstancePriceEntry) — no dependency on Savings Plan's PriceSheetEntry or
/// SavingsPlanAdminService, by design, so the two features can't interfere with each other.
///
/// Only active, non-excluded resources are considered — the same IsActive /
/// ExcludeFromSavingsPlan flags Savings Plan uses. Subscription is required.
/// </summary>
public class ReservedInstanceAdminService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    private const string Unassigned = "UNASSIGNED";

    public ReservedInstanceAdminService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Returns one row per eligible resource comparing on-demand cost against 1-year and
    /// 3-year Reserved Instance pricing for the selected month.
    /// </summary>
    public async Task<List<ReservedInstanceSimulationRow>> GetComparisonAsync(
        int year, int month, string subscriptionFilter, string? meterCategoryFilter = null)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var startDate = new DateTime(year, month, 1);
        var endDate   = startDate.AddMonths(1);

        // ── Eligible meter keys from the Reserved Instance price sheet ─────────
        var priceSheetKeys = await db.ReservedInstancePriceEntries
            .Select(p => new { p.MeterCategory, p.MeterSubCategory, p.MeterName })
            .Distinct()
            .ToListAsync();

        if (priceSheetKeys.Count == 0) return [];

        var allMeters = await db.Meters.ToListAsync();

        var eligibleMeterIds = allMeters
            .Where(m => priceSheetKeys.Any(k =>
                string.Equals(k.MeterCategory, m.MeterCategory, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    NormaliseVmSubCategory(k.MeterCategory, k.MeterSubCategory),
                    NormaliseVmSubCategory(m.MeterCategory, m.MeterSubCategory),
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(k.MeterName, m.MeterName, StringComparison.OrdinalIgnoreCase)))
            .Select(m => m.Id)
            .ToList();

        if (eligibleMeterIds.Count == 0) return [];

        // ── Reserved monthly rates per meter key — flat UnitPrice / TermMonths, no hours ──
        var rateEntries = await db.ReservedInstancePriceEntries
            .Where(p => p.Term == "P1Y" || p.Term == "P3Y")
            .ToListAsync();

        var rateLookup = rateEntries
            .GroupBy(p => (
                Category:    p.MeterCategory.Trim().ToUpperInvariant(),
                SubCategory: NormaliseVmSubCategory(p.MeterCategory, p.MeterSubCategory).ToUpperInvariant(),
                Name:        p.MeterName.Trim().ToUpperInvariant()))
            .ToDictionary(
                g => g.Key,
                g => (
                    P1Y: g.Where(p => p.Term == "P1Y").Select(p => (decimal?)(p.UnitPrice / 12m)).FirstOrDefault(),
                    P3Y: g.Where(p => p.Term == "P3Y").Select(p => (decimal?)(p.UnitPrice / 36m)).FirstOrDefault()
                ));

        // ── Billing data for the on-demand baseline (weighted-average UnitPrice) ──
        var query =
            from rc in db.ResourceCosts
            join m  in db.Meters        on rc.MeterId        equals m.Id
            join r  in db.Resources     on rc.ResourceDbId   equals r.Id
            join s  in db.Subscriptions on r.SubscriptionId  equals s.Id
            where eligibleMeterIds.Contains(rc.MeterId)
               && rc.BillingPeriodStart >= startDate
               && rc.BillingPeriodStart <  endDate
               && r.IsActive
               && !r.ExcludeFromSavingsPlan
               && s.SubscriptionName == subscriptionFilter
            group new { rc, m } by new
            {
                r.Id,
                r.ResourceName,
                r.ResourceGroup,
                r.HoursOfOperation,
                s.SubscriptionName,
                m.MeterCategory,
                m.MeterSubCategory,
                m.MeterName,
            }
            into g
            select new
            {
                g.Key.Id,
                g.Key.ResourceName,
                g.Key.ResourceGroup,
                g.Key.HoursOfOperation,
                g.Key.SubscriptionName,
                g.Key.MeterCategory,
                g.Key.MeterSubCategory,
                g.Key.MeterName,
                TotalBilledQuantity        = g.Sum(x => x.rc.Quantity),
                TotalQuantityTimesUnitPrice = g.Sum(x => x.rc.Quantity * x.rc.UnitPrice),
            };

        if (!string.IsNullOrWhiteSpace(meterCategoryFilter))
            query = query.Where(x => x.MeterCategory == meterCategoryFilter);

        var rawData = await query.ToListAsync();
        if (rawData.Count == 0) return [];

        // ── Application names per resource ──────────────────────────────────
        var resourceIds = rawData.Select(x => x.Id).Distinct().ToList();

        var appData = await (
            from ra in db.ResourceApps
            join a in db.Applications on ra.ApplicationId equals a.Id
            where resourceIds.Contains(ra.ResourceId) && a.ApplicationName.ToUpper() != Unassigned
            select new { ra.ResourceId, a.ApplicationName }
        ).Distinct().ToListAsync();

        var appLookup = appData
            .GroupBy(x => x.ResourceId)
            .ToDictionary(
                g => g.Key,
                g => string.Join(", ", g.Select(x => x.ApplicationName).Distinct().OrderBy(x => x)));

        // ── Assemble results ─────────────────────────────────────────────────
        var results = new List<ReservedInstanceSimulationRow>();

        foreach (var x in rawData)
        {
            var avgUnitPrice = x.TotalBilledQuantity > 0
                ? x.TotalQuantityTimesUnitPrice / x.TotalBilledQuantity
                : 0m;

            // On-demand: schedule-aware, exactly like Savings Plan's MonthlyCost
            var expectedHours   = GetExpectedOperatingHours(x.HoursOfOperation, year, month);
            var onDemandMonthly = expectedHours * avgUnitPrice;

            var key = (
                Category:    x.MeterCategory.Trim().ToUpperInvariant(),
                SubCategory: NormaliseVmSubCategory(x.MeterCategory, x.MeterSubCategory).ToUpperInvariant(),
                Name:        x.MeterName.Trim().ToUpperInvariant());

            rateLookup.TryGetValue(key, out var rates);

            results.Add(new ReservedInstanceSimulationRow
            {
                ResourceId        = x.Id,
                ResourceName      = x.ResourceName,
                ResourceGroup     = x.ResourceGroup,
                SubscriptionName  = x.SubscriptionName,
                MeterCategory     = x.MeterCategory,
                MeterSubCategory  = x.MeterSubCategory,
                MeterName         = x.MeterName,
                HoursOfOperation  = x.HoursOfOperation,
                ApplicationNames  = appLookup.GetValueOrDefault(x.Id, string.Empty),
                OnDemandMonthly   = onDemandMonthly,
                Reserved1YMonthly = rates.P1Y,
                Reserved3YMonthly = rates.P3Y,
            });
        }

        return results.OrderBy(r => r.ResourceName).ToList();
    }

    // ── VM subcategory normalisation ─────────────────────────────────────────

    /// <summary>
    /// Reconciles two systematic differences between the Reserved Instance price sheet and
    /// the on-demand Meter table for standard VM series:
    ///
    ///   1. Most Reserved Instance rows redundantly repeat the category name inside the
    ///      subcategory text — "Virtual Machines Dsv5 Series" instead of "Dsv5 Series". The
    ///      on-demand Meter table never does this (category and subcategory are separate
    ///      columns there), so an exact match fails for almost every standard VM family.
    ///
    ///   2. On-demand billing splits Windows VMs into a separate meter ("Dsv5 Series Windows")
    ///      because the PAYG rate bundles the OS licence into the compute price. A Reserved
    ///      Instance reserves hardware capacity only — the OS licence is unrelated and
    ///      continues billing separately regardless of the reservation — so Reserved
    ///      Instance pricing has no Windows variant at all. A Windows on-demand meter's
    ///      correct reservation is the OS-agnostic base family.
    /// </summary>
private static string NormaliseVmSubCategory(string category, string subCategory)
{
    // Trim the subCategory.
    var normalised = (subCategory ?? string.Empty).Trim();

    // Remove a prefix equal to the category plus a space, if present.
    var categoryPrefix = (category ?? string.Empty).Trim() + " ";
    if (normalised.StartsWith(categoryPrefix, StringComparison.OrdinalIgnoreCase))
        normalised = normalised.Substring(categoryPrefix.Length);

    // Optionally, remove any trailing " Windows" (if applicable).
    const string windowsSuffix = " Windows";
    if (normalised.EndsWith(windowsSuffix, StringComparison.OrdinalIgnoreCase))
        normalised = normalised[..^windowsSuffix.Length];

    // Remove hyphens and extra spaces.
    normalised = normalised.Replace("-", " ").Replace("  ", " ");

    // Convert to lower case for suffix removal.
    string lowerNormalised = normalised.ToLowerInvariant();

    // Remove common suffixes like "series", "series linux", or "linux".
    if (lowerNormalised.EndsWith(" series", StringComparison.Ordinal))
    {
        normalised = normalised[..^(" series".Length)];
    }
    else if (lowerNormalised.EndsWith(" series linux", StringComparison.Ordinal))
    {
        normalised = normalised[..^(" series linux".Length)];
    }
    else if (lowerNormalised.EndsWith(" linux", StringComparison.Ordinal))
    {
        normalised = normalised[..^(" linux".Length)];
    }

    return normalised.Trim();
}

    // ── Schedule helper ──────────────────────────────────────────────────────
    // Mirrors SavingsPlanAdminService's GetExpectedOperatingHours/CountWeekdays exactly,
    // duplicated deliberately so this feature has zero dependency on that file/class.

    private static int GetExpectedOperatingHours(string hoursOfOperation, int year, int month)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);

        if (hoursOfOperation == TagExtractor.HoursOfOperation5x12)
        {
            var weekdays = CountWeekdays(year, month, daysInMonth);
            return weekdays * 12; // 7am - 7pm
        }

        // Default: 24x7
        return daysInMonth * 24;
    }

    private static int CountWeekdays(int year, int month, int daysInMonth)
    {
        var weekdays = 0;
        for (var day = 1; day <= daysInMonth; day++)
        {
            var dow = new DateTime(year, month, day).DayOfWeek;
            if (dow != DayOfWeek.Saturday && dow != DayOfWeek.Sunday)
                weekdays++;
        }
        return weekdays;
    }

    // ── Filter options ────────────────────────────────────────────────────────

    public async Task<List<string>> GetSubscriptionsAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Subscriptions
            .OrderBy(s => s.SubscriptionName)
            .Select(s => s.SubscriptionName)
            .ToListAsync();
    }

    public async Task<List<string>> GetMeterCategoriesAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.ReservedInstancePriceEntries
            .Select(p => p.MeterCategory)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
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
