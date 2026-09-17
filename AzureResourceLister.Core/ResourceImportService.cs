using AzureResourceLister.Data;
using AzureResourceLister.Dto;
using AzureResourceLister.Models;
using Microsoft.EntityFrameworkCore;

namespace AzureResourceLister;

/// <summary>
/// Imports resources from Azure. For each resource, resolves its Application and Owner by:
///   1. Checking the resource's own tag
///   2. Falling back to its Resource Group's tag if the resource doesn't have one
///   3. Looking up the raw value in RawValueMapping (the same table Applications/Owners pages write to)
///   4. Falling back to "Unassigned" if no mapping exists yet
///
/// Nothing here requires human review — resolution is fully deterministic. Anything that falls
/// back to Unassigned is just counted; the fix is curating that value on the Applications/Owners
/// page, then re-running this refresh, not reviewing it here.
/// </summary>
public class ResourceImportService
{
    private readonly AzureResourceService _resourceService;
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    private const string Unassigned = "UNASSIGNED";
    private const int SaveBatchSize = 200;

    public const string AllSubscriptions = "__ALL__";

    public ResourceImportService(AzureResourceService resourceService, IDbContextFactory<AppDbContext> dbFactory)
    {
        _resourceService = resourceService;
        _dbFactory = dbFactory;
    }

    public async Task<ResourceImportSummary> ImportAsync(string azureSubscriptionId)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var targetSubscriptions = await ResolveTargetSubscriptionsAsync(db, azureSubscriptionId);
        if (targetSubscriptions.Count == 0)
            throw new InvalidOperationException(
                "No subscriptions found in the Subscription table. Add at least one before refreshing.");

        var appMappings = await db.RawValueMappings
            .Where(m => m.EntityType == "Application")
            .ToDictionaryAsync(m => m.RawValue.ToUpperInvariant(), m => m.CanonicalName);

        var ownerMappings = await db.RawValueMappings
            .Where(m => m.EntityType == "BusinessOwner")
            .ToDictionaryAsync(m => m.RawValue.ToUpperInvariant(), m => m.CanonicalName);

        var appIds   = await db.Applications.ToDictionaryAsync(a => a.ApplicationName.ToUpperInvariant(), a => a.Id);
        var ownerIds = await db.BusinessOwners.ToDictionaryAsync(o => o.OwnerName.ToUpperInvariant(), o => o.Id);

        if (!appIds.TryGetValue(Unassigned, out var unassignedAppId))
            throw new InvalidOperationException("'Unassigned' Application not found — this shouldn't normally happen, it's seeded at startup.");

        if (!ownerIds.TryGetValue(Unassigned, out var unassignedOwnerId))
            throw new InvalidOperationException("'Unassigned' BusinessOwner not found — this shouldn't normally happen, it's seeded at startup.");

        var summary = new ResourceImportSummary();
        int processedSinceLastSave = 0;

        foreach (var subscription in targetSubscriptions)
        {
            var resources      = await _resourceService.GetResourcesAsync(subscription.AzureSubscriptionId);
            var resourceGroups = await _resourceService.GetResourceGroupsAsync(subscription.AzureSubscriptionId);

            var existing = await db.Resources
                .Where(r => r.SubscriptionId == subscription.Id)
                .Include(r => r.ResourceApps)
                .Include(r => r.ResourceOwners)
                .ToDictionaryAsync(r => r.AzureResourceId.ToLowerInvariant(), r => r);

            foreach (var ar in resources)
            {
                var rawAppValue    = ResolveRawValue(ar, "ApplicationName",  resourceGroups);
                var rawOwnerValue  = ResolveRawValue(ar, "ApplicationOwner", resourceGroups);
                var rawCostCentre  = ResolveRawValue(ar, "CostCentre",       resourceGroups) ?? string.Empty;
                // Truncate to the column's max length of 30 characters
                if (rawCostCentre.Length > 30) rawCostCentre = rawCostCentre[..30];

                // Auto-shutdown schedule lives on the resource itself — no resource-group fallback needed
                var hoursOfOperation = TagExtractor.ClassifyHoursOfOperation(ar.Tags);

                var applicationId = unassignedAppId;
                if (rawAppValue is not null &&
                    appMappings.TryGetValue(rawAppValue.ToUpperInvariant(), out var appCanonical) &&
                    appIds.TryGetValue(appCanonical.ToUpperInvariant(), out var aid))
                {
                    applicationId = aid;
                }
                else
                {
                    summary.UnresolvedApplicationCount++;
                }

                var ownerId = unassignedOwnerId;
                if (rawOwnerValue is not null &&
                    ownerMappings.TryGetValue(rawOwnerValue.ToUpperInvariant(), out var ownerCanonical) &&
                    ownerIds.TryGetValue(ownerCanonical.ToUpperInvariant(), out var oid))
                {
                    ownerId = oid;
                }
                else
                {
                    summary.UnresolvedOwnerCount++;
                }

                var key = ar.Id.ToLowerInvariant();

                if (existing.TryGetValue(key, out var resourceRow))
                {
                    resourceRow.ResourceName      = ar.Name;
                    resourceRow.ResourceGroup     = ar.ResourceGroup;
                    resourceRow.CostCentre        = rawCostCentre;
                    resourceRow.HoursOfOperation  = hoursOfOperation;
                    resourceRow.IsActive          = true;

                    // Replace only this resource's TagSync-sourced rows — any SharedAppImport
                    // rows from the external shared-app registry are left alone.
                    db.ResourceApps.RemoveRange(resourceRow.ResourceApps.Where(ra => ra.Source == "TagSync"));
                    db.ResourceOwners.RemoveRange(resourceRow.ResourceOwners.Where(ro => ro.Source == "TagSync"));

                    db.ResourceApps.Add(new ResourceApp { ResourceId = resourceRow.Id, ApplicationId = applicationId, Weight = 1.0m, Source = "TagSync" });
                    db.ResourceOwners.Add(new ResourceOwner { ResourceId = resourceRow.Id, OwnerId = ownerId, Source = "TagSync" });

                    summary.Updated++;
                }
                else
                {
                    var newResource = new Resource
                    {
                        AzureResourceId  = ar.Id,
                        ResourceName     = ar.Name,
                        ResourceGroup    = ar.ResourceGroup,
                        CostCentre       = rawCostCentre,
                        HoursOfOperation = hoursOfOperation,
                        SubscriptionId   = subscription.Id,
                        IsActive         = true,
                    };
                    newResource.ResourceApps.Add(new ResourceApp { ApplicationId = applicationId, Weight = 1.0m, Source = "TagSync" });
                    newResource.ResourceOwners.Add(new ResourceOwner { OwnerId = ownerId, Source = "TagSync" });

                    db.Resources.Add(newResource);
                    summary.Inserted++;
                }

                processedSinceLastSave++;
                if (processedSinceLastSave >= SaveBatchSize)
                {
                    await db.SaveChangesAsync();
                    processedSinceLastSave = 0;
                }
            }
        }

        await db.SaveChangesAsync();
        return summary;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string? ResolveRawValue(
        AzureResource resource,
        string tagKey,
        Dictionary<string, AzureResourceGroup> resourceGroups)
    {
        var ownValue = TagExtractor.TryGetRawValue(resource.Tags, tagKey);
        if (ownValue is not null) return ownValue;

        if (resourceGroups.TryGetValue(resource.ResourceGroup, out var rg))
            return TagExtractor.TryGetRawValue(rg.Tags, tagKey);

        return null;
    }

    private async Task<List<Subscription>> ResolveTargetSubscriptionsAsync(AppDbContext db, string azureSubscriptionId)
    {
        if (string.IsNullOrWhiteSpace(azureSubscriptionId) || azureSubscriptionId == AllSubscriptions)
            return await db.Subscriptions.ToListAsync();

        return await db.Subscriptions
            .Where(s => s.AzureSubscriptionId == azureSubscriptionId)
            .ToListAsync();
    }
}
