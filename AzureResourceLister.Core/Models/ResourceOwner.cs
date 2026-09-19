namespace AzureResourceLister.Models;

/// <summary>
/// Links a Resource to a BusinessOwner. Currently always a single row per resource in
/// practice, but modelled as many-to-many for consistency with ResourceApp and to avoid
/// another schema change if multi-owner resources ever become a real scenario.
/// </summary>
public class ResourceOwner
{
    public int Id { get; set; }

    public int ResourceId { get; set; }
    public int OwnerId { get; set; }

    /// <summary>"TagSync" or "SharedAppImport" — same meaning as ResourceApp.Source.</summary>
    public string Source { get; set; } = "TagSync";

    // ── Navigation ────────────────────────────────────────────────────────────
    public Resource Resource { get; set; } = null!;
    public BusinessOwner Owner { get; set; } = null!;
}
