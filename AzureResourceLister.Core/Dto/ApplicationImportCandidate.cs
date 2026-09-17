namespace AzureResourceLister.Dto;

/// <summary>
/// One AI-proposed, human-editable row in the pre-import review window.
/// Represents a cluster of near-duplicate raw tag values the AI grouped together,
/// along with a suggested canonical name.
/// </summary>
public class ApplicationImportCandidate
{
    /// <summary>The AI-suggested canonical name. Editable by the reviewer before import.</summary>
    public string ProposedName { get; set; } = string.Empty;

    /// <summary>The raw tag values that were grouped into this candidate, for transparency.</summary>
    public List<string> SourceRawValues { get; set; } = [];

    /// <summary>If the AI matched this candidate to an existing Application row, its Id.</summary>
    public int? MatchesExistingApplicationId { get; set; }

    /// <summary>If matched, the existing application's exact name (for display).</summary>
    public string? MatchesExistingName { get; set; }

    /// <summary>Whether this candidate should be imported. Defaults to true only for brand-new names.</summary>
    public bool Include { get; set; } = true;
}
