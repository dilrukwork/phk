using System;

namespace AzureResourceLister.Models;

public class ResourceCost
{
    public int Id { get; set; }

    /// <summary>
    /// Raw ARM resource ID from the billing sheet (CSV: ResourceId).
    /// Kept as-is for auditability and re-matching.
    /// </summary>
    public string AzureResourceId { get; set; } = string.Empty;

    /// <summary>
    /// FK to Resource.Id — null if the ARM resource ID could not be matched
    /// (those rows are also written to ResourceCostException).
    /// </summary>
    public int? ResourceDbId { get; set; }

    /// <summary>FK to Meter.Id.</summary>
    public int MeterId { get; set; }

    public decimal  Quantity           { get; set; }
    public decimal  Cost               { get; set; }
    public decimal  EffectivePrice     { get; set; }
    public decimal  UnitPrice          { get; set; }
    public decimal  PAYGPrice          { get; set; }
    public string   OfferId            { get; set; } = string.Empty;
    public string   PricingModel       { get; set; } = string.Empty;
    public DateTime BillingPeriodStart { get; set; }

    // ── Navigation ────────────────────────────────────────────────────────────
    public Resource? Resource { get; set; }
    public Meter     Meter    { get; set; } = null!;
}
