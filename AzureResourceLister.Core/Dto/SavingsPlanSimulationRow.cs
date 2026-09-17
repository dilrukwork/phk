namespace AzureResourceLister.Dto;

/// <summary>
/// One resource's result from the hourly-commitment Savings Plan simulation.
/// MonthlyCost is the schedule-based enterprise PAYG baseline (same figure shown on the
/// eligibility table). CostWithP1Y/CostWithP3Y are what that resource would actually cost
/// after the hour-by-hour commitment is allocated across all competing eligible resources —
/// so unlike a simple "apply the discount to everything" calculation, a resource can still
/// end up paying full PAYG for some or all of its hours if the commitment was exhausted by
/// higher-discount resources in those hours.
/// </summary>
public class SavingsPlanSimulationRow
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

    /// <summary>Schedule-based enterprise PAYG baseline for the month.</summary>
    public decimal MonthlyCost { get; set; }

    public decimal CostWithP1Y { get; set; }
    public decimal CostWithP3Y { get; set; }

    public decimal SavingP1Y => MonthlyCost - CostWithP1Y;
    public decimal SavingP3Y => MonthlyCost - CostWithP3Y;

    public decimal SavingP1YPercent => MonthlyCost > 0 ? SavingP1Y / MonthlyCost * 100m : 0m;
    public decimal SavingP3YPercent => MonthlyCost > 0 ? SavingP3Y / MonthlyCost * 100m : 0m;
}
