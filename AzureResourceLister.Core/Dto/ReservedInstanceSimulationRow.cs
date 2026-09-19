namespace AzureResourceLister.Dto;

/// <summary>
/// One resource's on-demand vs Reserved Instance cost comparison.
///
/// OnDemandMonthly uses the same schedule-aware calculation as Savings Plan
/// (avgUnitPrice × expected operating hours) — a 5x12 resource shows a lower on-demand
/// figure than a 24x7 resource running the same meter.
///
/// Reserved1YMonthly/Reserved3YMonthly are flat: term price ÷ 12 or ÷ 36, deliberately
/// ignoring HoursOfOperation — a reservation is billed whether the resource runs or not.
/// Nullable because a meter may only have a price row for one of the two terms.
///
/// This asymmetry is intentional: it's what lets the page show that a 5x12 resource may
/// not benefit from a reservation, since its on-demand cost is already low relative to a
/// flat, always-on reservation charge.
/// </summary>
public class ReservedInstanceSimulationRow
{
    public int    ResourceId       { get; set; }
    public string ResourceName     { get; set; } = string.Empty;
    public string ResourceGroup    { get; set; } = string.Empty;
    public string SubscriptionName { get; set; } = string.Empty;
    public string MeterCategory    { get; set; } = string.Empty;
    public string MeterSubCategory { get; set; } = string.Empty;
    public string MeterName        { get; set; } = string.Empty;
    public string HoursOfOperation { get; set; } = string.Empty;

    /// <summary>Distinct application names using this resource, comma-joined. Empty if none linked.</summary>
    public string ApplicationNames { get; set; } = string.Empty;

    public decimal  OnDemandMonthly   { get; set; }
    public decimal? Reserved1YMonthly { get; set; }
    public decimal? Reserved3YMonthly { get; set; }

    public decimal? Saving1Y => Reserved1YMonthly.HasValue ? OnDemandMonthly - Reserved1YMonthly.Value : null;
    public decimal? Saving3Y => Reserved3YMonthly.HasValue ? OnDemandMonthly - Reserved3YMonthly.Value : null;

    public decimal? Saving1YPercent => Saving1Y.HasValue && OnDemandMonthly > 0 ? Saving1Y.Value / OnDemandMonthly * 100m : null;
    public decimal? Saving3YPercent => Saving3Y.HasValue && OnDemandMonthly > 0 ? Saving3Y.Value / OnDemandMonthly * 100m : null;
}
