using System.Collections.Generic;

namespace AzureResourceLister.Models;

/// <summary>
/// Reference table of unique meter definitions sourced from the billing sheet.
/// Uniqueness is determined by the combination of MeterCategory + MeterSubCategory + MeterName + UnitOfMeasure.
/// </summary>
public class Meter
{
    public int Id { get; set; }

    public string MeterCategory    { get; set; } = string.Empty;
    public string MeterSubCategory { get; set; } = string.Empty;
    public string MeterName        { get; set; } = string.Empty;
    public string UnitOfMeasure    { get; set; } = string.Empty;

    // ── Navigation ────────────────────────────────────────────────────────────
    public ICollection<ResourceCost> ResourceCosts { get; set; } = [];
}
