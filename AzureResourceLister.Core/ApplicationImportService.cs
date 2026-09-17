using AzureResourceLister.Data;
using AzureResourceLister.Dto;
using AzureResourceLister.Models;
using Microsoft.EntityFrameworkCore;

namespace AzureResourceLister;

/// <summary>
/// Imports ApplicationName tag values from Azure, using a persistent human-curated
/// mapping (RawValueMapping) to remember the canonical name for each raw value.
/// Only genuinely new raw values are surfaced for review — anything previously
/// decided is resolved automatically, every time.
/// </summary>
public class ApplicationImportService
{
    private readonly AzureResourceService _resourceService;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    private const string Unassigned = "UNASSIGNED";
    private const string EntityType = "Application";

    /// <summary>Sentinel value the UI dropdown uses to mean "every subscription in the table".</summary>
    public const string AllSubscriptions = "__ALL__";

    public ApplicationImportService(
        AzureResourceService resourceService,
        IDbContextFactory<AppDbContext> dbFactory)
    {
        _resourceService = resourceService;
        _dbFactory = dbFactory;
    }

    /// <summary>
    /// Fetches live Azure data for the given subscription (or every subscription, if
    /// <paramref name="azureSubscriptionId"/> is null/blank/<see cref="AllSubscriptions"/>),
    /// extracts unique ApplicationName tag values, and checks each one against
    /// RawValueMapping. Only genuinely new raw values come back for review.
    /// </summary>
    public async Task<RawValueRefreshResult> PreviewImportAsync(string azureSubscriptionId)
    {
        var targetSubscriptionIds = await ResolveTargetSubscriptionsAsync(azureSubscriptionId);

        if (targetSubscriptionIds.Count == 0)
            throw new InvalidOperationException(
                "No subscriptions found in the Subscription table. Add at least one before refreshing.");

        var rawValues = new List<string>();
        foreach (var subId in targetSubscriptionIds)
        {
            var resources      = await _resourceService.GetResourcesAsync(subId);
            var resourceGroups = await _resourceService.GetResourceGroupsAsync(subId);

            rawValues.AddRange(TagExtractor.ExtractValuesForKey(resources, "ApplicationName"));
            rawValues.AddRange(TagExtractor.ExtractValuesForKey(resourceGroups.Values.Select(rg => rg.Tags), "ApplicationName"));
        }

        var distinctRawValues = rawValues
            .Select(v => v.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        using var db = await _dbFactory.CreateDbContextAsync();

        var existingMappings = await db.RawValueMappings
            .Where(m => m.EntityType == EntityType)
            .ToDictionaryAsync(m => m.RawValue.ToUpperInvariant(), m => m.CanonicalName);

        var result = new RawValueRefreshResult();

        foreach (var rawValue in distinctRawValues)
        {
            var key = rawValue.ToUpperInvariant();
            if (existingMappings.TryGetValue(key, out var canonicalName))
            {
                result.AlreadyMappedCanonicalNames.Add(canonicalName);
            }
            else
            {
                result.NewItems.Add(new RawValueReviewItem
                {
                    RawValue      = rawValue,
                    CanonicalName = rawValue.ToUpperInvariant(), // sensible starting point — edit as needed
                    Include       = true,
                });
            }
        }

        result.AlreadyMappedCanonicalNames = result.AlreadyMappedCanonicalNames
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return result;
    }

    /// <summary>
    /// Saves the human-reviewed mappings for any new raw values, and ensures every
    /// resolved canonical name — new or previously known — exists as an Application row.
    /// The latter makes this self-healing: if an Application row was deleted but its
    /// mapping wasn't, the next refresh quietly re-creates it.
    /// </summary>
    public async Task<int> ImportApprovedAsync(RawValueRefreshResult result)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var existingApps = await db.Applications
            .ToDictionaryAsync(a => a.ApplicationName.ToUpperInvariant(), a => a.Id);

        var existingMappings = await db.RawValueMappings
            .Where(m => m.EntityType == EntityType)
            .ToDictionaryAsync(m => m.RawValue.ToUpperInvariant(), m => m);

        int created = 0;

        // 1. Save mappings + ensure Application rows for newly reviewed items
        foreach (var item in result.NewItems.Where(i => i.Include))
        {
            var canonicalName = item.CanonicalName.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(canonicalName)) continue;

            var rawKey = item.RawValue.Trim().ToUpperInvariant();
            if (!existingMappings.ContainsKey(rawKey))
            {
                db.RawValueMappings.Add(new RawValueMapping
                {
                    EntityType    = EntityType,
                    RawValue      = item.RawValue.Trim(),
                    CanonicalName = canonicalName,
                });
            }

            if (!existingApps.ContainsKey(canonicalName))
            {
                db.Applications.Add(new Application { ApplicationName = canonicalName, Budget = 0m });
                existingApps[canonicalName] = -1; // guard against duplicate inserts within this batch
                created++;
            }
        }

        // 2. Self-heal: ensure Application rows exist for every already-known canonical name too
        foreach (var canonicalName in result.AlreadyMappedCanonicalNames)
        {
            var key = canonicalName.Trim().ToUpperInvariant();
            if (string.IsNullOrWhiteSpace(key) || existingApps.ContainsKey(key)) continue;

            db.Applications.Add(new Application { ApplicationName = key, Budget = 0m });
            existingApps[key] = -1;
            created++;
        }

        await db.SaveChangesAsync();
        return created;
    }

    private async Task<List<string>> ResolveTargetSubscriptionsAsync(string azureSubscriptionId)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        if (string.IsNullOrWhiteSpace(azureSubscriptionId) || azureSubscriptionId == AllSubscriptions)
            return await db.Subscriptions.Select(s => s.AzureSubscriptionId).ToListAsync();

        return await db.Subscriptions
            .Where(s => s.AzureSubscriptionId == azureSubscriptionId)
            .Select(s => s.AzureSubscriptionId)
            .ToListAsync();
    }
}
