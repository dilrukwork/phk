using AzureResourceLister.Data;
using AzureResourceLister.Dto;
using Microsoft.EntityFrameworkCore;

namespace AzureResourceLister;

/// <summary>
/// The Savings Plan calculator. Given a candidate hourly $ commitment, replays Azure's
/// real allocation algorithm hour by hour across the selected month: each hour, eligible
/// resources scheduled to be running compete for the shared commitment, highest discount
/// percentage first, until the commitment is exhausted (unused commitment does not roll
/// over). At $0 commitment this naturally produces the "no Savings Plan" baseline, since
/// no resource ever receives any budget — MonthlyCost, CostWithP1Y, and CostWithP3Y all
/// come out equal. Raising the commitment and re-running shows the resulting saving, so
/// you can try different amounts to find the optimum before locking in a 1-year or 3-year
/// term.
///
/// Only active resources (IsActive) not flagged ExcludeFromSavingsPlan are ever considered
/// — a resource can be permanently excluded from the calculator (e.g. known to be
/// decommissioned soon) via SetSavingsPlanExclusionAsync, independent of any specific run.
///
/// Uses HoursOfOperation (24x7 / 5x12, derived from the dxcAutoShutdownSchedule tag)
/// rather than real historical usage to decide which hours a resource is "running" —
/// confirmed sufficient for this estimate since actual per-hour usage isn't available,
/// only monthly aggregates.
///
/// Region matching is handled externally — the price sheet is pre-filtered to AUE/AUSE
/// rates and the correct OfferId before import, so the matching key here is simply
/// (MeterCategory + MeterSubCategory + MeterName).
/// </summary>
public class SavingsPlanAdminService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    private const string Unassigned = "UNASSIGNED";

    public SavingsPlanAdminService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    // ── Hourly commitment simulator ─────────────────────────────────────────

    /// <summary>
    /// Runs the hour-by-hour Savings Plan allocation for the selected month, once against
    /// P1Y rates and once against P3Y rates, using the given hourly commitment for both.
    /// Subscription is required — a Savings Plan commitment is scoped per subscription.
    /// </summary>
    public async Task<List<SavingsPlanSimulationRow>> RunHourlyCommitmentSimulationAsync(
        int year, int month,
        string subscriptionFilter,
        string? meterCategoryFilter,
        decimal hourlyCommitment)
    {
        var inputs = await LoadSimulationInputsAsync(year, month, subscriptionFilter, meterCategoryFilter);
        if (inputs.Count == 0) return [];

        var costWithP1Y = RunHourlySimulation(inputs, r => r.P1YHourlyRate, hourlyCommitment, year, month);
        var costWithP3Y = RunHourlySimulation(inputs, r => r.P3YHourlyRate, hourlyCommitment, year, month);

        return inputs
            .Select(r => new SavingsPlanSimulationRow
            {
                ResourceId       = r.ResourceId,
                ResourceName     = r.ResourceName,
                ResourceGroup    = r.ResourceGroup,
                SubscriptionName = r.SubscriptionName,
                MeterCategory    = r.MeterCategory,
                MeterSubCategory = r.MeterSubCategory,
                MeterName        = r.MeterName,
                HoursOfOperation = r.HoursOfOperation,
                ApplicationNames = r.ApplicationNames,
                MonthlyCost      = r.MonthlyCost,
                // Resources with no rate for a given term (rateSelector returned null) never
                // entered the competing pool for that term's pass, so fall back to the full
                // PAYG baseline — "no plan available" means no benefit, not a $0 cost.
                CostWithP1Y      = costWithP1Y.GetValueOrDefault(r.Key, r.MonthlyCost),
                CostWithP3Y      = costWithP3Y.GetValueOrDefault(r.Key, r.MonthlyCost),
            })
            .OrderByDescending(r => r.MonthlyCost)
            .ToList();
    }

    /// <summary>
    /// Replays the hourly allocation algorithm for one term (P1Y or P3Y) across every hour
    /// of the month. Each hour: filter to resources scheduled to be running, rank by
    /// discount percentage vs PAYG (highest first), then walk the ranked list spending the
    /// commitment — full coverage while budget allows, a blended partial-hour rate for the
    /// resource that exhausts the remaining budget, full PAYG for everything after that.
    /// The commitment resets fresh every hour; unused budget does not carry over.
    /// </summary>
    private static Dictionary<string, decimal> RunHourlySimulation(
        List<SimResource> resources,
        Func<SimResource, decimal?> rateSelector,
        decimal hourlyCommitment,
        int year, int month)
    {
        var costByKey = new Dictionary<string, decimal>();

        var eligible = resources
            .Where(r => rateSelector(r).HasValue)
            .Select(r => new { Resource = r, Rate = rateSelector(r)!.Value })
            .ToList();

        if (eligible.Count == 0) return costByKey;

        var startDate = new DateTime(year, month, 1);
        var endDate   = startDate.AddMonths(1);

        for (var dt = startDate; dt < endDate; dt = dt.AddHours(1))
        {
            var running = eligible
                .Where(x => IsRunningThisHour(x.Resource.HoursOfOperation, dt))
                .Select(x => new
                {
                    x.Resource,
                    x.Rate,
                    Discount = x.Resource.PayGHourlyRate > 0
                        ? (x.Resource.PayGHourlyRate - x.Rate) / x.Resource.PayGHourlyRate
                        : 0m,
                })
                .OrderByDescending(x => x.Discount)
                .ToList();

            var budget = hourlyCommitment;

            foreach (var item in running)
            {
                decimal cost;

                if (budget >= item.Rate)
                {
                    cost = item.Rate;
                    budget -= item.Rate;
                }
                else if (budget > 0)
                {
                    // Partial-hour blend: the portion of the hour the remaining budget covers
                    // gets the SP rate, the rest of that same hour is billed at PAYG.
                    var remaining = item.Rate - budget;
                    cost = budget + remaining * (item.Resource.PayGHourlyRate / item.Rate);
                    budget = 0;
                }
                else
                {
                    cost = item.Resource.PayGHourlyRate;
                }

                costByKey[item.Resource.Key] = costByKey.GetValueOrDefault(item.Resource.Key) + cost;
            }
        }

        return costByKey;
    }

    /// <summary>Matches the console app's IsRunning check: 24x7 always true; 5x12 only Mon-Fri 7am-7pm.</summary>
    private static bool IsRunningThisHour(string hoursOfOperation, DateTime dt)
    {
        if (hoursOfOperation == TagExtractor.HoursOfOperation5x12)
        {
            return dt.DayOfWeek is >= DayOfWeek.Monday and <= DayOfWeek.Friday
                && dt.Hour >= 7 && dt.Hour < 19;
        }

        // Default: 24x7
        return true;
    }

    // ── Permanent exclusion management ───────────────────────────────────────

    /// <summary>Flags or unflags a resource for permanent exclusion from Savings Plan calculations.</summary>
    public async Task SetSavingsPlanExclusionAsync(int resourceId, bool excluded)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var resource = await db.Resources.FindAsync(resourceId);
        if (resource is null) return;

        resource.ExcludeFromSavingsPlan = excluded;
        await db.SaveChangesAsync();
    }

    /// <summary>Lists every resource currently excluded, for the "Excluded Resources" management panel.</summary>
    public async Task<List<ExcludedResourceRow>> GetExcludedResourcesAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        return await (
            from r in db.Resources
            join s in db.Subscriptions on r.SubscriptionId equals s.Id
            where r.ExcludeFromSavingsPlan
            orderby r.ResourceName
            select new ExcludedResourceRow
            {
                ResourceId       = r.Id,
                ResourceName     = r.ResourceName,
                ResourceGroup    = r.ResourceGroup,
                SubscriptionName = s.SubscriptionName,
            }
        ).ToListAsync();
    }

    // ── Shared data loading ──────────────────────────────────────────────────

    private sealed record SimResource(
        int ResourceId,
        string ResourceName,
        string ResourceGroup,
        string SubscriptionName,
        string MeterCategory,
        string MeterSubCategory,
        string MeterName,
        string HoursOfOperation,
        string ApplicationNames,
        decimal PayGHourlyRate,
        decimal MonthlyCost,
        decimal? P1YHourlyRate,
        decimal? P3YHourlyRate)
    {
        public string Key => $"{ResourceId}";
    }

    /// <summary>
    /// Loads every eligible resource for the given period/filters with everything needed
    /// for the hourly simulation: schedule-based PAYG baseline, weighted-average PAYG
    /// hourly rate, normalised P1Y/P3Y hourly rates, and linked application names.
    /// Excludes inactive resources and anything flagged ExcludeFromSavingsPlan.
    /// </summary>
    private async Task<List<SimResource>> LoadSimulationInputsAsync(
        int year, int month, string? subscriptionFilter, string? meterCategoryFilter)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var startDate = new DateTime(year, month, 1);
        var endDate   = startDate.AddMonths(1);

        // ── Eligible meter keys from price sheet ────────────────────────────
        var priceSheetKeys = await db.PriceSheetEntries
            .Select(p => new { p.MeterCategory, p.MeterSubCategory, p.MeterName })
            .Distinct()
            .ToListAsync();

        if (priceSheetKeys.Count == 0) return [];

        var allMeters = await db.Meters.ToListAsync();

        var eligibleMeterIds = allMeters
            .Where(m => priceSheetKeys.Any(k =>
                string.Equals(k.MeterCategory,    m.MeterCategory,    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(k.MeterSubCategory, m.MeterSubCategory, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(k.MeterName,        m.MeterName,        StringComparison.OrdinalIgnoreCase)))
            .Select(m => m.Id)
            .ToList();

        if (eligibleMeterIds.Count == 0) return [];

        // ── Normalised P1Y / P3Y hourly rates per meter key ─────────────────
        // Price sheet UnitOfMeasure can be "100 Hours" rather than "1 Hour" — normalise so
        // the rate is always $ per single hour, matching UnitPrice's per-hour PAYG rate.
        var rateEntries = await db.PriceSheetEntries
            .Where(p => p.Term == "P1Y" || p.Term == "P3Y")
            .ToListAsync();

        var rateLookup = rateEntries
            .GroupBy(p => (
                Category:    p.MeterCategory.Trim().ToUpperInvariant(),
                SubCategory: p.MeterSubCategory.Trim().ToUpperInvariant(),
                Name:        p.MeterName.Trim().ToUpperInvariant()))
            .ToDictionary(
                g => g.Key,
                g => (
                    P1Y: g.Where(p => p.Term == "P1Y")
                          .Select(p => (decimal?)NormaliseUnitPrice(p.UnitPrice, p.UnitOfMeasure))
                          .FirstOrDefault(),
                    P3Y: g.Where(p => p.Term == "P3Y")
                          .Select(p => (decimal?)NormaliseUnitPrice(p.UnitPrice, p.UnitOfMeasure))
                          .FirstOrDefault()
                ));

        // ── Billing data for the PAYG baseline (weighted-average UnitPrice) ──
        // Only active, non-excluded resources are ever considered.
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

        if (!string.IsNullOrWhiteSpace(subscriptionFilter))
            query = query.Where(x => x.SubscriptionName == subscriptionFilter);

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
        var results = new List<SimResource>();

        foreach (var x in rawData)
        {
            var avgUnitPrice = x.TotalBilledQuantity > 0
                ? x.TotalQuantityTimesUnitPrice / x.TotalBilledQuantity
                : 0m;

            var expectedHours = GetExpectedOperatingHours(x.HoursOfOperation, year, month);
            var monthlyCost   = expectedHours * avgUnitPrice;

            var key = (
                Category:    x.MeterCategory.Trim().ToUpperInvariant(),
                SubCategory: x.MeterSubCategory.Trim().ToUpperInvariant(),
                Name:        x.MeterName.Trim().ToUpperInvariant());

            rateLookup.TryGetValue(key, out var rates);

            results.Add(new SimResource(
                x.Id, x.ResourceName, x.ResourceGroup, x.SubscriptionName,
                x.MeterCategory, x.MeterSubCategory, x.MeterName,
                x.HoursOfOperation, appLookup.GetValueOrDefault(x.Id, string.Empty),
                avgUnitPrice, monthlyCost,
                rates.P1Y, rates.P3Y));
        }

        return results;
    }

    // ── Schedule / rate helpers ──────────────────────────────────────────────

    /// <summary>
    /// Expected operating hours for a resource's schedule classification in the given month.
    /// 24x7 → every hour of the calendar month. 5x12 → 12 hours (7am-7pm) on each weekday.
    /// </summary>
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

    /// <summary>
    /// Normalises a price sheet rate to $ per single hour. UnitOfMeasure is typically
    /// "1 Hour" or "100 Hours" — divide UnitPrice by the leading number to get the true
    /// per-hour rate. Falls back to the raw UnitPrice if no leading number is found.
    /// </summary>
    private static decimal NormaliseUnitPrice(decimal unitPrice, string unitOfMeasure)
    {
        var match = System.Text.RegularExpressions.Regex.Match(unitOfMeasure ?? string.Empty, @"(\d+(\.\d+)?)");

        if (match.Success &&
            decimal.TryParse(match.Value, System.Globalization.CultureInfo.InvariantCulture, out var multiplier) &&
            multiplier > 0)
        {
            return unitPrice / multiplier;
        }

        return unitPrice;
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
        return await db.PriceSheetEntries
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
