namespace AzureResourceLister.Dto;

public class ApplicationCostRow
{
    public string ApplicationName { get; set; } = string.Empty;
    public string OwnerName       { get; set; } = string.Empty;
    public string CostCentre      { get; set; } = string.Empty;

    /// <summary>Allocated cost per environment — key is EnvironmentName.</summary>
    public Dictionary<string, decimal> CostByEnvironment { get; set; } = [];

    public decimal TotalCost => CostByEnvironment.Values.Sum();
}
