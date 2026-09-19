using AzureResourceLister.Data;
using AzureResourceLister.Dto;
using Microsoft.EntityFrameworkCore;

namespace AzureResourceLister;

/// <summary>
/// Paginated read access to the Meter table. No edit — meters are purely Azure
/// billing-sheet-derived reference data, same reasoning as Resources.
/// </summary>
public class MeterAdminService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public MeterAdminService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<PagedResult<MeterRow>> GetPagedAsync(int pageNumber, int pageSize, string? search = null)
    {
        using var db = await _dbFactory.CreateDbContextAsync();

        var query = db.Meters.AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(m =>
                m.MeterCategory.Contains(term) ||
                m.MeterSubCategory.Contains(term) ||
                m.MeterName.Contains(term));
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(m => m.MeterCategory).ThenBy(m => m.MeterName)
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new MeterRow
            {
                Id               = m.Id,
                MeterCategory    = m.MeterCategory,
                MeterSubCategory = m.MeterSubCategory,
                MeterName        = m.MeterName,
                UnitOfMeasure    = m.UnitOfMeasure,
            })
            .ToListAsync();

        return new PagedResult<MeterRow>
        {
            Items      = items,
            TotalCount = totalCount,
            PageNumber = pageNumber,
            PageSize   = pageSize,
        };
    }
}
