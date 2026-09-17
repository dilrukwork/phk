namespace AzureResourceLister.Dto;

public class ResourceCostRow
{
    public string ResourceName     { get; set; } = string.Empty;
    public string SubscriptionName { get; set; } = string.Empty;
    public string OwnerName        { get; set; } = string.Empty;
    public string CostCentre       { get; set; } = string.Empty;

    /// <summary>Distinct meter categories for this resource, comma-joined. Usually one value (e.g. "Virtual Machines").</summary>
    public string MeterCategory { get; set; } = string.Empty;

    /// <summary>Total cost per environment — key is EnvironmentName.</summary>
    public Dictionary<string, decimal> CostByEnvironment { get; set; } = [];

    public decimal TotalCost => CostByEnvironment.Values.Sum();
}
