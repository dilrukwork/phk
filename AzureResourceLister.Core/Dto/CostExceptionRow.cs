namespace AzureResourceLister.Dto;

/// <summary>Read-model for the Cost Exceptions admin grid.</summary>
public class CostExceptionRow
{
    public int Id { get; set; }
    public string AzureResourceId { get; set; } = string.Empty;
    public string MeterCategory { get; set; } = string.Empty;
    public string MeterSubCategory { get; set; } = string.Empty;
    public string MeterName { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal Cost { get; set; }
    public DateTime BillingPeriodStart { get; set; }
    public string SourceFile { get; set; } = string.Empty;
    public DateTime LoggedAt { get; set; }
}
