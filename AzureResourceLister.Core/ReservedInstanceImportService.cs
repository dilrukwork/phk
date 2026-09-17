using System.Globalization;
using AzureResourceLister.Data;
using AzureResourceLister.Dto;
using AzureResourceLister.Models;
using Microsoft.EntityFrameworkCore;

namespace AzureResourceLister;

/// <summary>
/// Imports a dedicated Reserved Instance price sheet CSV — a separate file from the Savings
/// Plan price sheet (Config.ReservedInstancePriceSheetPath vs Config.PriceSheetPath), kept
/// in its own table (ReservedInstancePriceEntry) so the two features never share data and
/// can't interfere with each other.
///
/// The file is expected to already contain only Reserved Instance rows, but rows are still
/// checked for PriceType = "ReservedInstance" as a safety net — anything else is skipped
/// and counted, in case the file wasn't pre-filtered as expected.
///
/// Import strategy: REPLACE — the table is truncated and reloaded on each import, same as
/// Savings Plan's price sheet (an annual exercise; full replace is simpler than upserts).
/// </summary>
public class ReservedInstanceImportService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public ReservedInstanceImportService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<ReservedInstanceImportSummary> ImportAsync(string priceSheetPath)
    {
        if (string.IsNullOrWhiteSpace(priceSheetPath))
            throw new InvalidOperationException("ReservedInstancePriceSheetPath is not configured. Set it in appsettings.json.");
        if (!File.Exists(priceSheetPath))
            throw new FileNotFoundException($"Reserved Instance price sheet not found: {priceSheetPath}");

        var allRows = CsvParser.ParseRows(await File.ReadAllTextAsync(priceSheetPath));
        if (allRows.Count < 2)
            throw new InvalidOperationException("Price sheet is empty or has no data rows.");

        var headerIndex = CsvParser.BuildHeaderIndex(allRows[0]);

        var summary = new ReservedInstanceImportSummary();
        var entries = new List<ReservedInstancePriceEntry>();

        for (int i = 1; i < allRows.Count; i++)
        {
            var cols = allRows[i];
            if (cols.Count == 1 && string.IsNullOrWhiteSpace(cols[0])) continue;

            var priceType = CsvParser.Get(cols, headerIndex, "PriceType").Trim();

            // Safety net — the file is expected to already be pre-filtered to ReservedInstance rows
            if (!priceType.Equals("ReservedInstance", StringComparison.OrdinalIgnoreCase))
            {
                summary.RowsSkipped++;
                continue;
            }

            var startRaw = CsvParser.Get(cols, headerIndex, "EffectiveStartDate");
            if (!DateTime.TryParse(startRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var startDate))
                continue;

            var endRaw = CsvParser.Get(cols, headerIndex, "EffectiveEndDate");
            DateTime? endDate = DateTime.TryParse(endRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var ed)
                ? ed : null;

            var unitPriceRaw = CsvParser.Get(cols, headerIndex, "UnitPrice");
            if (!decimal.TryParse(unitPriceRaw, NumberStyles.Any, CultureInfo.InvariantCulture, out var unitPrice))
                continue;

            entries.Add(new ReservedInstancePriceEntry
            {
                AzureMeterId       = CsvParser.Get(cols, headerIndex, "MeterId").Trim(),
                MeterCategory      = CsvParser.Get(cols, headerIndex, "MeterCategory").Trim(),
                MeterSubCategory   = CsvParser.Get(cols, headerIndex, "MeterSubCategory").Trim(),
                MeterName          = CsvParser.Get(cols, headerIndex, "MeterName").Trim(),
                MeterRegion        = CsvParser.Get(cols, headerIndex, "MeterRegion").Trim(),
                Term               = CsvParser.Get(cols, headerIndex, "Term").Trim(),
                UnitPrice          = unitPrice,
                UnitOfMeasure      = CsvParser.Get(cols, headerIndex, "UnitOfMeasure").Trim(),
                OfferId            = CsvParser.Get(cols, headerIndex, "OfferId").Trim(),
                Product            = CsvParser.Get(cols, headerIndex, "Product").Trim(),
                EffectiveStartDate = startDate,
                EffectiveEndDate   = endDate,
            });

            summary.EntriesImported++;
        }

        using var db = await _dbFactory.CreateDbContextAsync();

        // Replace: truncate and reload
        await db.ReservedInstancePriceEntries.ExecuteDeleteAsync();
        db.ReservedInstancePriceEntries.AddRange(entries);
        await db.SaveChangesAsync();

        summary.DistinctMeters = entries
            .Select(e => (e.MeterCategory, e.MeterSubCategory, e.MeterName))
            .Distinct()
            .Count();

        return summary;
    }

    public async Task<int> GetEntryCountAsync()
    {
        using var db = await _dbFactory.CreateDbContextAsync();
        return await db.ReservedInstancePriceEntries.CountAsync();
    }
}
