namespace AzureResourceLister.Dto;

public class SharedAppImportSummary
{
    public int ApplicationsCreated { get; set; }
    public int ResourcesAffected { get; set; }
    public int ResourceAppLinksCreated { get; set; }
    public int ExceptionsLogged { get; set; }
}
