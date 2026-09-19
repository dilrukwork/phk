using System;

namespace AzureResourceLister.Models;

/// <summary>
/// Records billing sheet rows where the AzureResourceId could not be matched
/// to any row in the Resource table. Allows manual review and re-processing.
/// </summary>
public class ResourceCostException
{
    public int Id { get; set; }

    /// <summary>The unmatched ARM resource ID from the billing sheet.</summary>
    public string AzureResourceId { get; set; } = string.Empty;

    /// <summary>Source billing sheet file name for traceability.</summary>
    public string SourceFile { get; set; } = string.Empty;

    /// <summary>FK to Meter — populated since meters are seeded before cost import.</summary>
    public int? MeterId { get; set; }

    public decimal  Quantity           { get; set; }
    public decimal  Cost               { get; set; }
    public decimal  EffectivePrice     { get; set; }
    public decimal  UnitPrice          { get; set; }
    public decimal  PAYGPrice          { get; set; }
    public string   MeterCategory      { get; set; } = string.Empty;
    public string   MeterSubCategory   { get; set; } = string.Empty;
    public string   MeterName          { get; set; } = string.Empty;
    public string   UnitOfMeasure      { get; set; } = string.Empty;
    public string   OfferId            { get; set; } = string.Empty;
    public string   PricingModel       { get; set; } = string.Empty;
    public DateTime BillingPeriodStart { get; set; }

    /// <summary>UTC timestamp when this exception was logged.</summary>
    public DateTime LoggedAt { get; set; } = DateTime.UtcNow;

    // ── Navigation ────────────────────────────────────────────────────────────
    public Meter? Meter { get; set; }
}
