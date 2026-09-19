namespace AzureResourceLister.Dto;

public class ReportFilterOptions
{
    public List<int>    Years         { get; set; } = [];
    public List<string> Owners        { get; set; } = [];
    public List<string> CostCentres   { get; set; } = [];
    public List<string> Subscriptions { get; set; } = [];
    public List<string> Environments  { get; set; } = [];
}
