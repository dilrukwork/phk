namespace AzureResourceLister.Models;

/// <summary>
/// One row from a dedicated Reserved Instance price sheet — a separate file and a separate
/// table from Savings Plan's PriceSheetEntry, deliberately. UnitPrice here means something
/// different (the total price for the whole term, not a per-UnitOfMeasure rate like Savings
/// Plan rows), and mixing the two into one table risks corrupting SavingsPlanAdminService's
/// rate lookup for any meter priced under both mechanisms.
///
/// Monthly cost is UnitPrice / 12 (P1Y) or UnitPrice / 36 (P3Y) — a flat amortisation, not
/// hours-based. A reservation is billed regardless of whether the resource actually runs,
/// so usage/HoursOfOperation deliberately plays no part in this calculation.
///
/// The composite (MeterCategory, MeterSubCategory, MeterName) is used to join to the Meter
/// table and from there to ResourceCost, identifying which resources are eligible — same
/// pattern as PriceSheetEntry.
///
/// The import is a full replace each time — the price sheet is an annual exercise and small
/// enough that a truncate-and-reload is cleaner than incremental upserts.
/// </summary>
public class ReservedInstancePriceEntry
{
    public int Id { get; set; }

    /// <summary>Azure MeterId GUID from the price sheet. Stored for reference/future use.</summary>
    public string AzureMeterId { get; set; } = string.Empty;

    // ── Meter identification (used as join key to Meter table) ────────────────
    public string MeterCategory    { get; set; } = string.Empty;
    public string MeterSubCategory { get; set; } = string.Empty;
    public string MeterName        { get; set; } = string.Empty;
    public string MeterRegion      { get; set; } = string.Empty;

    /// <summary>"P1Y" or "P3Y"</summary>
    public string Term { get; set; } = string.Empty;

    /// <summary>
    /// Total price for the whole term — NOT a per-hour or per-UnitOfMeasure rate, even
    /// though UnitOfMeasure is typically "1 Hour". Divide by 12 (P1Y) or 36 (P3Y) to get
    /// the flat monthly amortised cost.
    /// </summary>
    public decimal UnitPrice { get; set; }

    public string UnitOfMeasure { get; set; } = string.Empty;

    public string OfferId { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;

    public DateTime  EffectiveStartDate { get; set; }
    public DateTime? EffectiveEndDate   { get; set; }
}
