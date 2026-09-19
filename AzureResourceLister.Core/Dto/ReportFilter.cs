namespace AzureResourceLister.Dto;

public class ReportFilter
{
    public int Year  { get; set; } = DateTime.Now.Year;
    public int Month { get; set; } = DateTime.Now.Month;

    public string? OwnerName       { get; set; }
    public string? CostCentre      { get; set; }

    // Resources page only
    public string? SubscriptionName { get; set; }
    public string? EnvironmentName  { get; set; }
    public string? ApplicationName  { get; set; } // also used when navigating from Applications page
}
