namespace AzureResourceLister.Dto;

/// <summary>A resource currently flagged out of Savings Plan calculations.</summary>
public class ExcludedResourceRow
{
    public int    ResourceId       { get; set; }
    public string ResourceName     { get; set; } = string.Empty;
    public string ResourceGroup    { get; set; } = string.Empty;
    public string SubscriptionName { get; set; } = string.Empty;
}
