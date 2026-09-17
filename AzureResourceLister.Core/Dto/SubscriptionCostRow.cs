namespace AzureResourceLister.Dto;

public class SubscriptionCostRow
{
    public string SubscriptionName { get; set; } = string.Empty;
    public string EnvironmentName { get; set; } = string.Empty;
    public decimal TotalCost { get; set; }
}
