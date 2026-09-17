namespace AzureResourceLister.Dto;

/// <summary>Read-model for the Resources admin grid.</summary>
public class ResourceRow
{
    public int Id { get; set; }
    public string AzureResourceId { get; set; } = string.Empty;
    public string ResourceName { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public string SubscriptionName { get; set; } = string.Empty;

    /// <summary>Comma-joined — always one name today, future-proofed for shared resources.</summary>
    public string ApplicationName { get; set; } = string.Empty;

    /// <summary>Comma-joined — always one name today, future-proofed for multi-owner resources.</summary>
    public string OwnerName { get; set; } = string.Empty;

    public bool IsActive { get; set; }
    public string Source { get; set; } = string.Empty;
    public string CostCentre { get; set; } = string.Empty;
    public string HoursOfOperation { get; set; } = string.Empty;
}
