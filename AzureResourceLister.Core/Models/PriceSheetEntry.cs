namespace AzureResourceLister.Models;

/// <summary>
/// One row from the Azure price sheet for a Savings Plan term.
/// Only SavingsPlan rows are imported (Consumption rows are excluded — the enterprise
/// PAYG rate comes from ResourceCost.EffectivePrice, not the price sheet).
///
/// The composite (MeterCategory, MeterSubCategory, MeterName) is used to join to the
/// Meter table and from there to ResourceCost, identifying which resources are eligible.
///
/// The import is a full replace each time — the price sheet is annual and small enough
/// that a truncate-and-reload is cleaner than incremental upserts.
/// </summary>
public class PriceSheetEntry
{
    public int Id { get; set; }

    /// <summary>Azure MeterId GUID from the price sheet. Stored for reference/future use.</summary>
    public string AzureMeterId { get; set; } = string.Empty;

    // ── Meter identification (used as join key to Meter table) ────────────────
    public string MeterCategory    { get; set; } = string.Empty;
    public string MeterSubCategory { get; set; } = string.Empty;
    public string MeterName        { get; set; } = string.Empty;
    public string MeterRegion      { get; set; } = string.Empty;

    // ── Savings Plan details ──────────────────────────────────────────────────
    /// <summary>"P1Y" or "P3Y"</summary>
    public string Term { get; set; } = string.Empty;

    /// <summary>Price per UnitOfMeasure (e.g. per 100 hours). Divide by UOM multiplier for per-unit rate.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>e.g. "100 Hours", "1 Hour". Used to normalise the rate against billing quantities.</summary>
    public string UnitOfMeasure { get; set; } = string.Empty;

    public string OfferId  { get; set; } = string.Empty;
    public string Product  { get; set; } = string.Empty;

    public DateTime  EffectiveStartDate { get; set; }
    public DateTime? EffectiveEndDate   { get; set; }
}
