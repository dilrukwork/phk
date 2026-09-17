namespace AzureResourceLister.Dto;

/// <summary>Outcome of a Resources "Refresh from Azure" run.</summary>
public class ResourceImportSummary
{
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int UnresolvedApplicationCount { get; set; }
    public int UnresolvedOwnerCount { get; set; }
}
