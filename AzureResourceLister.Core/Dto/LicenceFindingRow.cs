namespace AzureResourceLister.Dto;

public class LicenceFindingRow
{
    public string ResourceName     { get; set; } = string.Empty;
    public string ResourceGroup    { get; set; } = string.Empty;
    public string SubscriptionName { get; set; } = string.Empty;
    public string EnvironmentName  { get; set; } = string.Empty;
    public string OwnerName        { get; set; } = string.Empty;
    public string LicenceType      { get; set; } = string.Empty;
    public decimal MonthlyCost     { get; set; }
}
