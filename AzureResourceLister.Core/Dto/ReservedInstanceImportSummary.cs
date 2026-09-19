namespace AzureResourceLister.Dto;

public class ReservedInstanceImportSummary
{
    public int EntriesImported { get; set; }

    /// <summary>Rows that weren't PriceType="ReservedInstance" — a safety net, since the
    /// file is expected to already be pre-filtered to Reserved Instance rows only.</summary>
    public int RowsSkipped { get; set; }

    public int DistinctMeters { get; set; }
}
