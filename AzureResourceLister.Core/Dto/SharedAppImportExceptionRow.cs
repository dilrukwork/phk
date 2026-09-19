namespace AzureResourceLister.Dto;

/// <summary>Read-model for the Shared App Import Exceptions grid — editable for resync.</summary>
public class SharedAppImportExceptionRow
{
    public int Id { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
    public string OwnerName { get; set; } = string.Empty;
    public string AzureResourceName { get; set; } = string.Empty;
    public string SubscriptionName { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTime LoggedAt { get; set; }
}
