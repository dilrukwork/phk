namespace AzureResourceLister.Dto;

public class PriceSheetImportSummary
{
    public int EntriesImported    { get; set; }
    public int ConsumptionSkipped { get; set; }   // Consumption rows filtered out
    public int DistinctMeters     { get; set; }   // unique meter combinations
}
