namespace AzureResourceLister.Dto;

public class SubscriptionCostRow
{
    public string SubscriptionName { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = string.Empty;
    public decimal ActualCost { get; set; }
    public decimal PaygCost { get; set; }
    public decimal Savings => PaygCost - ActualCost;
}
