namespace AzureResourceLister.Dto;

/// <summary>Read-model for the Meters admin grid.</summary>
public class MeterRow
{
    public int Id { get; set; }
    public string MeterCategory { get; set; } = string.Empty;
    public string MeterSubCategory { get; set; } = string.Empty;
    public string MeterName { get; set; } = string.Empty;
    public string UnitOfMeasure { get; set; } = string.Empty;
}
