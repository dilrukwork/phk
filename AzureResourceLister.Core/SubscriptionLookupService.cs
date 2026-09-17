using AzureResourceLister.Data;
using AzureResourceLister.Dto;
using Microsoft.EntityFrameworkCore;

namespace AzureResourceLister;

/// <summary>
/// Simple read-only lookup over the Subscription table. Shared by any admin page that
/// needs a "which subscription(s)?" picker (Applications, and later Owners/Resources).
/// </summary>
public class SubscriptionLookupService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public SubscriptionLookupService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<List<SubscriptionOption>> GetAllAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        return await db.Subscriptions
            .OrderBy(s => s.SubscriptionName)
            .Select(s => new SubscriptionOption
            {
                AzureSubscriptionId = s.AzureSubscriptionId,
                SubscriptionName    = s.SubscriptionName,
            })
            .ToListAsync();
    }
}
