using AzureResourceLister.Data;
using AzureResourceLister.Dto;
using AzureResourceLister.Models;
using Microsoft.EntityFrameworkCore;

namespace AzureResourceLister;

/// <summary>
/// Imports the shared-app registry CSV (ApplicationName, AzureResourceId) and links
/// each application to the shared resources it uses, splitting each resource's cost
/// equally among every application that references it.
/// Owner column is no longer imported — resource ownership comes from Azure tag sync only.
/// </summary>
public class SharedAppImportService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private const string SharedAppImportSource = "SharedAppImport";

    public SharedAppImportService(IDbContextFactory<AppDbContext> dbFactory)
    {
        _dbFactory = dbFactory;
    }

    public async Task<SharedAppImportSummary> ImportAsync(string csvPath)
    {
        if (string.IsNullOrWhiteSpace(csvPath))
            throw new InvalidOperationException("No file path provided.");
        if (!File.Exists(csvPath))
            throw new FileNotFoundException($"File not found: {csvPath}");

        var sourceFile = Path.GetFileName(csvPath);
        var allRows = CsvParser.ParseRows(await File.ReadAllTextAsync(csvPath));

        if (allRows.Count < 2)
            throw new InvalidOperationException("File is empty or has no data rows.");

        var headerIndex = CsvParser.BuildHeaderIndex(allRows[0]);
        var rows = new List<SharedAppRow>();

        for (int i = 1; i < allRows.Count; i++)
        {
            var cols = allRows[i];
            if (cols.Count == 1 && string.IsNullOrWhiteSpace(cols[0])) continue;

            var appName = CsvParser.Get(cols, headerIndex, "ApplicationName");
            if (string.IsNullOrWhiteSpace(appName)) continue;

            var azureResourceId = CsvParser.Get(cols, headerIndex, "AzureResourceId");
            if (string.IsNullOrWhiteSpace(azureResourceId)) continue;

            rows.Add(new SharedAppRow
            {
                ApplicationName = appName,
                AzureResourceId = azureResourceId,
            });
        }

        using var db = await _dbFactory.CreateDbContextAsync();
        var summary = new SharedAppImportSummary();

        var resourceLookup = (await db.Resources.ToListAsync())
            .GroupBy(r => r.AzureResourceId.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.IsActive).First().Id);

        var appIds = await db.Applications.ToDictionaryAsync(a => a.ApplicationName.ToUpperInvariant(), a => a.Id);

        var resolved = new List<(int ResourceId, int ApplicationId)>();

        foreach (var row in rows)
        {
            if (!resourceLookup.TryGetValue(row.AzureResourceId.Trim().ToLowerInvariant(), out var resourceId))
            {
                await LogExceptionAsync(db, row, "Resource not found — AzureResourceId does not match any record in the Resource table.", sourceFile);
                summary.ExceptionsLogged++;
                continue;
            }

            var applicationId = await GetOrCreateApplicationAsync(db, appIds, row.ApplicationName, summary);
            resolved.Add((resourceId, applicationId));
        }

        await db.SaveChangesAsync();

        foreach (var resourceGroup in resolved.GroupBy(r => r.ResourceId))
        {
            var resourceId     = resourceGroup.Key;
            var distinctAppIds = resourceGroup.Select(r => r.ApplicationId).Distinct().ToList();
            var weight         = Math.Round(1m / distinctAppIds.Count, 4);

            var existingApps = await db.ResourceApps
                .Where(ra => ra.ResourceId == resourceId && ra.Source == SharedAppImportSource)
                .ToListAsync();
            db.ResourceApps.RemoveRange(existingApps);

            foreach (var applicationId in distinctAppIds)
            {
                db.ResourceApps.Add(new ResourceApp
                {
                    ResourceId    = resourceId,
                    ApplicationId = applicationId,
                    Weight        = weight,
                    Source        = SharedAppImportSource,
                });
                summary.ResourceAppLinksCreated++;
            }

            summary.ResourcesAffected++;
        }

        await db.SaveChangesAsync();
        return summary;
    }

    private static async Task<int> GetOrCreateApplicationAsync(
        AppDbContext db, Dictionary<string, int> appIds, string applicationName, SharedAppImportSummary summary)
    {
        var key = applicationName.Trim().ToUpperInvariant();
        if (appIds.TryGetValue(key, out var existingId)) return existingId;

        var entity = new Application { ApplicationName = key, Budget = 0m };
        db.Applications.Add(entity);
        await db.SaveChangesAsync();

        appIds[key] = entity.Id;
        summary.ApplicationsCreated++;
        return entity.Id;
    }

    private static async Task LogExceptionAsync(AppDbContext db, SharedAppRow row, string reason, string sourceFile)
    {
        db.SharedAppImportExceptions.Add(new SharedAppImportException
        {
            ApplicationName   = row.ApplicationName,
            AzureResourceName = row.AzureResourceId,
            Reason            = reason,
            SourceFile        = sourceFile,
            LoggedAt          = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
    }

    private sealed class SharedAppRow
    {
        public string ApplicationName { get; set; } = string.Empty;
        public string AzureResourceId { get; set; } = string.Empty;
    }
}
