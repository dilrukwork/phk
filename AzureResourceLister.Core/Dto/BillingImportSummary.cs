namespace AzureResourceLister.Dto;

/// <summary>Outcome of a Meters-only or full Resource Costs import run.</summary>
public class BillingImportSummary
{
    public int MetersInserted { get; set; }
    public int MetersAlreadyExisted { get; set; }
    public int CostsInserted { get; set; }
    public int OtherSubscriptionRowsSkipped { get; set; }
    public int ExceptionsLogged { get; set; }

    /// <summary>Resources reconstructed directly from cost-sheet rows because they no longer
    /// exist in Azure (and so weren't found by the last Resources refresh).</summary>
    public int ResourcesCreatedFromCostSheet { get; set; }
}
