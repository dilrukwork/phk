namespace AzureResourceLister.Dto;

/// <summary>One genuinely-new raw tag value, awaiting a human-specified canonical name.</summary>
public class RawValueReviewItem
{
    /// <summary>The raw tag value exactly as seen in Azure. Not editable — for reference only.</summary>
    public string RawValue { get; set; } = string.Empty;

    /// <summary>The correct name, typed by the reviewer. Defaults to the raw value, normalised.</summary>
    public string CanonicalName { get; set; } = string.Empty;

    /// <summary>Whether this should be imported at all.</summary>
    public bool Include { get; set; } = true;
}
