namespace AzureResourceLister.Dto;

/// <summary>Read-model for the Owners admin grid — includes a computed resource count.</summary>
public class OwnerRow
{
    public int Id { get; set; }
    public string OwnerName { get; set; } = string.Empty;
    public int ResourceCount { get; set; }
}
