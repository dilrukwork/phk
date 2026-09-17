namespace AzureResourceLister.Dto;

/// <summary>Read-model for the Applications admin grid — includes a computed resource count.</summary>
public class ApplicationRow
{
    public int Id { get; set; }
    public string ApplicationName { get; set; } = string.Empty;
    public decimal Budget { get; set; }
    public int ResourceCount { get; set; }
}
