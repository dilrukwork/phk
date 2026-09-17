using System;
using System.Collections.Generic;
using System.Linq;

namespace AzureResourceLister;

/// <summary>
/// Extracts canonical tag values (ApplicationName, ApplicationOwner, CostCentre,
/// AutoShutdownSchedule) from Azure resource/resource-group tags.
///
/// Each canonical key has a priority-ordered list of raw tag key variants it will
/// accept — see TagDefinitions below. For a single resource (TryGetRawValue), variants
/// are tried strictly in that order: the first variant present with a genuinely
/// non-blank value wins, regardless of what order Azure happens to return the tags
/// dictionary in. If a higher-priority variant exists but is blank, matching falls
/// through to the next variant exactly as if the higher-priority one weren't there.
///
/// Every value returned is cleaned first: leading/trailing whitespace is trimmed, and
/// a single layer of wrapping quotes is stripped (a common artifact of tag values
/// pasted from Excel or set via automation/scripts).
/// </summary>
public static class TagExtractor
{
    // Each group: canonical output name → raw key variants, in priority order (all
    // lowercase — matched against tag keys after trim + ToLowerInvariant).
    private static readonly (string Canonical, string[] Variants)[] TagDefinitions =
    [
        ("ApplicationName", [
            "applicationname",
            "application name",
            "application-name",
            "application_name",
        ]),
        ("ApplicationOwner", [
            "applicationowner",
            "application owner",
            "application-owner",
            "application_owner",
        ]),
        ("CostCentre", [
            "costcentre",
            "costcenter",
            "cost centre",
            "cost center",
        ]),
        ("AutoShutdownSchedule", [
            "dxcautoshutdownschedule",
            "autoshutdownschedule",
            "auto shutdown schedule",
        ]),
    ];

    /// <summary>Resource runs continuously — no recognised auto-shutdown schedule applies.</summary>
    public const string HoursOfOperation24x7 = "24x7";

    /// <summary>Resource runs 7am-7pm Monday-Friday only, per an enabled auto-shutdown schedule.</summary>
    public const string HoursOfOperation5x12 = "5x12";

    public record TagValueRow(string CanonicalKey, string Value);

    /// <summary>
    /// Returns one row per unique (canonical key, value) pair found across all resources,
    /// sorted by canonical key then value. Collects from ANY matching variant (not
    /// priority-limited) — this is a discovery/review view, so a resource with
    /// inconsistent tags (e.g. both "ApplicationName" and "Application Name" set to
    /// different values) surfaces both rather than silently picking one.
    /// </summary>
    public static List<TagValueRow> ExtractUniqueValues(IEnumerable<AzureResource> resources)
    {
        var buckets = TagDefinitions.ToDictionary(
            d => d.Canonical,
            _ => new HashSet<string>(StringComparer.Ordinal));

        foreach (var resource in resources)
        {
            foreach (var (rawKey, rawValue) in resource.Tags)
            {
                var cleaned = CleanTagValue(rawValue);
                if (string.IsNullOrEmpty(cleaned)) continue;

                var normKey = rawKey.Trim().ToLowerInvariant();

                foreach (var (canonical, variants) in TagDefinitions)
                {
                    if (variants.Contains(normKey))
                    {
                        buckets[canonical].Add(cleaned);
                        break;
                    }
                }
            }
        }

        return buckets
            .SelectMany(kvp => kvp.Value.Select(v => new TagValueRow(kvp.Key, v)))
            .OrderBy(r => r.CanonicalKey)
            .ThenBy(r => r.Value, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Convenience: returns only the values for a single canonical key.
    /// </summary>
    public static List<string> ExtractValuesForKey(IEnumerable<AzureResource> resources, string canonicalKey)
        => ExtractUniqueValues(resources)
            .Where(r => r.CanonicalKey.Equals(canonicalKey, StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Value)
            .ToList();

    /// <summary>
    /// Extracts unique values for a canonical key from a collection of raw tag dictionaries
    /// (e.g. resource group tags). Used to supplement resource-level tag extraction.
    /// </summary>
    public static List<string> ExtractValuesForKey(
        IEnumerable<Dictionary<string, string>> tagCollections,
        string canonicalKey)
    {
        var definition = TagDefinitions
            .FirstOrDefault(d => d.Canonical.Equals(canonicalKey, StringComparison.OrdinalIgnoreCase));

        if (definition == default) return [];

        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var tags in tagCollections)
        {
            foreach (var (rawKey, rawValue) in tags)
            {
                var cleaned = CleanTagValue(rawValue);
                if (string.IsNullOrEmpty(cleaned)) continue;
                if (definition.Variants.Contains(rawKey.Trim().ToLowerInvariant()))
                    values.Add(cleaned);
            }
        }

        return [.. values.OrderBy(v => v, StringComparer.OrdinalIgnoreCase)];
    }

    /// <summary>
    /// Returns the raw tag value for the given canonical key on a single set of tags, or
    /// null if none of its variants are present with a usable value.
    ///
    /// Variants are tried strictly in priority order (see TagDefinitions) — the first
    /// variant present with a non-blank value wins. If a higher-priority variant exists
    /// on the resource but its value is blank, that's treated the same as the variant
    /// being absent, and matching falls through to the next variant in the list.
    ///
    /// Used for per-resource resolution (e.g. resolving one specific resource's
    /// Application/Owner/CostCentre, with a resource-group fallback layered on top by
    /// the caller — see ResourceImportService.ResolveRawValue).
    /// </summary>
    public static string? TryGetRawValue(Dictionary<string, string> tags, string canonicalKey)
    {
        var definition = TagDefinitions
            .FirstOrDefault(d => d.Canonical.Equals(canonicalKey, StringComparison.OrdinalIgnoreCase));

        if (definition == default) return null;

        // Normalise the resource's tag keys once (trim + lowercase) so each variant
        // lookup below is a simple dictionary hit, independent of Azure's original key
        // casing/spacing and independent of the tags dictionary's enumeration order.
        var normalisedTags = new Dictionary<string, string>();
        foreach (var (rawKey, rawValue) in tags)
        {
            var key = rawKey.Trim().ToLowerInvariant();
            if (!normalisedTags.ContainsKey(key))
                normalisedTags[key] = rawValue;
        }

        foreach (var variant in definition.Variants)
        {
            if (normalisedTags.TryGetValue(variant, out var rawValue))
            {
                var cleaned = CleanTagValue(rawValue);
                if (!string.IsNullOrEmpty(cleaned))
                    return cleaned;
                // Present but blank — treat as absent and keep trying lower-priority variants.
            }
        }

        return null;
    }

    /// <summary>
    /// Derives HoursOfOperation ("24x7" or "5x12") from the dxcAutoShutdownSchedule tag.
    /// The tag value is a compound string (e.g. "Enabled;00 19 * * 1-4 11.8h;00 19 * * 5 59h"),
    /// so matching is StartsWith("Enabled"), not an exact match. Anything else — Disabled,
    /// a missing tag, or an unrecognised value — defaults to 24x7.
    /// </summary>
    public static string ClassifyHoursOfOperation(Dictionary<string, string> tags)
    {
        var raw = TryGetRawValue(tags, "AutoShutdownSchedule");

        return raw is not null && raw.StartsWith("Enabled", StringComparison.OrdinalIgnoreCase)
            ? HoursOfOperation5x12
            : HoursOfOperation24x7;
    }

    /// <summary>
    /// Cleans a raw tag value: trims leading/trailing whitespace, then strips a single
    /// layer of wrapping quotes if the whole value is quote-wrapped (a common artifact
    /// of values pasted from Excel or set via automation/scripts). Returns empty string
    /// for a value that's blank after cleaning.
    /// </summary>
    private static string CleanTagValue(string value)
    {
        var trimmed = value.Trim();

        if (trimmed.Length >= 2 &&
            ((trimmed[0] == '"' && trimmed[^1] == '"') ||
             (trimmed[0] == '\'' && trimmed[^1] == '\'')))
        {
            trimmed = trimmed[1..^1].Trim();
        }

        return trimmed;
    }
}
