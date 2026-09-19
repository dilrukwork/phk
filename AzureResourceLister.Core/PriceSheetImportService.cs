using System.Globalization;
using AzureResourceLister.Data;
using AzureResourceLister.Dto;
using AzureResourceLister.Models;
using Microsoft.EntityFrameworkCore;

namespace AzureResourceLister;

/// <summary>
/// Imports the Azure price sheet CSV, keeping only Savings Plan rows (PriceType = "SavingsPlan").
/// Consumption rows are excluded — the enterprise PAYG rate comes from ResourceCost.EffectivePrice.
///
/// Import strategy: REPLACE — the table is truncated and reloaded on each import.
/// The price sheet is an annual exercise; a full replace is simpler and safer than incremental updates.
/// </summary>
public class PriceSheetImportService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;

    public PriceSheetImportService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<PriceSheetImportSummary> ImportAsync(string priceSheetPath)
    {
        if (string.IsNullOrWhiteSpace(priceSheetPath))
            throw new InvalidOperationException("PriceSheetPath is not configured. Set it in appsettings.json.");
        if (!File.Exists(priceSheetPath))
            throw new FileNotFoundException($"Price sheet not found: {priceSheetPath}");

        var allRows = CsvParser.ParseRows(await File.ReadAllTextAsync(priceSheetPath));
        if (allRows.Count < 2)
            throw new InvalidOperationException("Price sheet is empty or has no data rows.");

        var headerIndex = CsvParser.BuildHeaderIndex(allRows[0]);

        var summary = new PriceSheetImportSummary();
        var entries = new List<PriceSheetEntry>();

        for (int i = 1; i < allRows.Count; i++)
        {
            var cols = allRows[i];
            if (cols.Count == 1 && string.IsNullOrWhiteSpace(cols[0])) continue;

            var priceType = CsvParser.Get(cols, headerIndex, "PriceType").Trim();

            // Skip Consumption rows — only import Savings Plan rates
            if (!priceType.Equals("SavingsPlan", StringComparison.OrdinalIgnoreCase))
            {
                summary.ConsumptionSkipped++;
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

            entries.Add(new PriceSheetEntry
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
        await db.PriceSheetEntries.ExecuteDeleteAsync();
        db.PriceSheetEntries.AddRange(entries);
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
        return await db.PriceSheetEntries.CountAsync();
    }
}
