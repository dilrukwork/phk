namespace AzureResourceLister.Dto;

/// <summary>
/// Result of checking freshly-extracted raw tag values against the RawValueMapping table.
/// Shared shape for both Application and BusinessOwner imports — only the EntityType used
/// internally by the import service differs.
/// </summary>
public class RawValueRefreshResult
{
    /// <summary>Raw values with no existing mapping — need a human to specify the canonical name.</summary>
    public List<RawValueReviewItem> NewItems { get; set; } = [];

    /// <summary>Canonical names already resolved from a previous mapping — no review needed.</summary>
    public List<string> AlreadyMappedCanonicalNames { get; set; } = [];
}
