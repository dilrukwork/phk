namespace AzureResourceLister.Dto;

public class UnattachedDiskRow
{
    public string  ResourceName     { get; set; } = string.Empty;
    public string  ResourceGroup    { get; set; } = string.Empty;
    public string  SubscriptionName { get; set; } = string.Empty;
    public string  EnvironmentName  { get; set; } = string.Empty;
    public string  OwnerName        { get; set; } = string.Empty;
    public int     DiskSizeGb       { get; set; }
    public string  SkuName          { get; set; } = string.Empty;

    /// <summary>Monthly cost from billing data. Zero if the disk has no ResourceCost rows
    /// (e.g. was created after the last billing import).</summary>
    public decimal MonthlyCost { get; set; }
}
