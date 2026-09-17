namespace AzureResourceLister.Dto;

/// <summary>
/// A resource that is eligible for a Savings Plan — it is currently billed
/// at the enterprise PAYG (OnDemand) rate AND its meter has entries in the
/// imported price sheet for at least one Savings Plan term.
/// </summary>
public class SavingsPlanEligibleRow
{
    public string ResourceName     { get; set; } = string.Empty;
    public string ResourceGroup    { get; set; } = string.Empty;
    public string SubscriptionName { get; set; } = string.Empty;

    /// <summary>MeterCategory — used as the "Type" filter (e.g. "Virtual Machines", "App Service").</summary>
    public string MeterCategory    { get; set; } = string.Empty;
    public string MeterSubCategory { get; set; } = string.Empty;
    public string MeterName        { get; set; } = string.Empty;

    /// <summary>"24x7" or "5x12" — the schedule classification used to derive MonthlyCost.</summary>
    public string HoursOfOperation { get; set; } = string.Empty;

    /// <summary>Sum of Cost for the selected billing period (enterprise PAYG rate × quantity).</summary>
    public decimal MonthlyCost { get; set; }

    public bool HasP1Y { get; set; }
    public bool HasP3Y { get; set; }
}
