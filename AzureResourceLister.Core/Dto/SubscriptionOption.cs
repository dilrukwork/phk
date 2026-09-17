namespace AzureResourceLister.Dto;

/// <summary>One entry in a subscription picker dropdown.</summary>
public class SubscriptionOption
{
    public string AzureSubscriptionId { get; set; } = string.Empty;
    public string SubscriptionName { get; set; } = string.Empty;
}
