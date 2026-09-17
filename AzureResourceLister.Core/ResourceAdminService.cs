using AzureResourceLister.Data;
using AzureResourceLister.Dto;
using Microsoft.EntityFrameworkCore;

namespace AzureResourceLister;

/// <summary>
/// Paginated, searchable read access to the Resource table. No edit/delete here —
/// Resources are Azure-driven; corrections happen at the Application/Owner level
/// followed by a re-refresh, not by overriding individual resource rows.
/// </summary>
public class ResourceAdminService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ResourceAdminService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<string>> GetCategoriesAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Meters
            .Select(m => m.MeterCategory)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync();
    }

    public async Task<List<string>> GetSourcesAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.Resources
            .Select(r => r.Source)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct()
            .OrderBy(s => s)
            .ToListAsync();
    }

    public async Task<PagedResult<ResourceRow>> GetPagedAsync(
        int pageNumber,
        int pageSize,
        string? search = null,
        string? categoryFilter = null,
        string? sourceFilter = null,
        string? subscriptionFilter = null)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var query = db.Resources.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(r =>
                r.ResourceName.Contains(term) ||
                r.AzureResourceId.Contains(term) ||
                r.ResourceGroup.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(subscriptionFilter) && subscriptionFilter != ResourceImportService.AllSubscriptions)
        {
            query = query.Where(r => r.Subscription.AzureSubscriptionId == subscriptionFilter);
        }

        if (!string.IsNullOrWhiteSpace(sourceFilter))
        {
            query = query.Where(r => r.Source == sourceFilter);
        }

        if (!string.IsNullOrWhiteSpace(categoryFilter))
        {
            var matchingResourceIds = db.ResourceCosts
                .Where(rc => rc.ResourceDbId.HasValue && rc.Meter.MeterCategory == categoryFilter)
                .Select(rc => rc.ResourceDbId!.Value)
                .Distinct();

            query = query.Where(r => matchingResourceIds.Contains(r.Id));
        }

        var totalCount = await query.CountAsync();

        // Project collection navigations as lists first (well-supported translation),
        // then join them into display strings in plain C# after materializing the page —
        // string.Join directly over a navigation collection doesn't reliably translate to SQL.
        var rawItems = await query
            .OrderBy(r => r.ResourceName)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new
            {
                r.Id,
                r.AzureResourceId,
                r.ResourceName,
                r.ResourceGroup,
                SubscriptionName = r.Subscription.SubscriptionName,
                ApplicationNames = r.ResourceApps.Select(ra => ra.Application.ApplicationName).ToList(),
                OwnerNames       = r.ResourceOwners.Select(ro => ro.Owner.OwnerName).ToList(),
                r.IsActive,
                r.Source,
                r.CostCentre,
                r.HoursOfOperation,
            })
            .ToListAsync();

        var items = rawItems.Select(x => new ResourceRow
        {
            Id               = x.Id,
            AzureResourceId  = x.AzureResourceId,
            ResourceName     = x.ResourceName,
            ResourceGroup    = x.ResourceGroup,
            SubscriptionName = x.SubscriptionName,
            ApplicationName  = string.Join(", ", x.ApplicationNames),
            OwnerName        = string.Join(", ", x.OwnerNames),
            IsActive         = x.IsActive,
            Source           = x.Source,
            CostCentre       = x.CostCentre,
            HoursOfOperation = x.HoursOfOperation,
        }).ToList();

        return new PagedResult<ResourceRow>
        {
            Items      = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize   = pageSize,
        };
    }

    public async Task UpdateHoursOfOperationAsync(int resourceId, string hoursOfOperation)
    {
        if (string.IsNullOrWhiteSpace(hoursOfOperation))
            throw new ArgumentException("Hours of operation cannot be empty.", nameof(hoursOfOperation));

        using var db = await _dbFactory.CreateDbContextAsync();
        var resource = await db.Resources.FindAsync(resourceId);
        if (resource is null)
            throw new InvalidOperationException($"Resource with Id {resourceId} not found.");

        resource.HoursOfOperation = hoursOfOperation;
        await db.SaveChangesAsync();
    }
}
