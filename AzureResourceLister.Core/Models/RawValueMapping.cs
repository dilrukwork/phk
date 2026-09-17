using System;

namespace AzureResourceLister.Models;

/// <summary>
/// Remembers a human-curated decision: this raw Azure tag value means this canonical name.
/// Checked by exact match (case-insensitive) on every refresh, so previously-decided values
/// never need to be reviewed again — only genuinely new raw values get shown for review.
/// </summary>
public class RawValueMapping
{
    public int Id { get; set; }

    /// <summary>"Application" today; the same pattern will extend to "BusinessOwner" later.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>The raw tag value exactly as seen in Azure (trimmed, original casing kept for reference).</summary>
    public string RawValue { get; set; } = string.Empty;

    /// <summary>The human-specified correct canonical name.</summary>
    public string CanonicalName { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
