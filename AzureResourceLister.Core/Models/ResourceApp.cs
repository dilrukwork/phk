namespace AzureResourceLister.Models;

/// <summary>
/// Links a Resource to an Application. A dedicated resource has exactly one row at
/// Weight = 1.0; a shared resource (e.g. a SQL server hosting multiple apps' databases)
/// has one row per consuming application, with Weight representing its share for
/// future cost apportionment.
/// </summary>
public class ResourceApp
{
    public int Id { get; set; }

    public int ResourceId { get; set; }
    public int ApplicationId { get; set; }

    /// <summary>Share of this resource attributable to this application (0.0–1.0). 1.0 for a dedicated resource.</summary>
    public decimal Weight { get; set; } = 1.0m;

    /// <summary>"TagSync" (derived from Azure/cost-sheet tags) or "SharedAppImport" (from the external shared-app registry CSV).
    /// Each sync path only ever replaces its own rows, so the two can coexist on the same resource without one wiping the other.</summary>
    public string Source { get; set; } = "TagSync";

    // ── Navigation ────────────────────────────────────────────────────────────
    public Resource Resource { get; set; } = null!;
    public Application Application { get; set; } = null!;
}
