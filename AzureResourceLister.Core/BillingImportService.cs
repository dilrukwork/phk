using System.Globalization;
using System.Text.Json;
using AzureResourceLister.Data;
using AzureResourceLister.Dto;
using AzureResourceLister.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AzureResourceLister;

/// <summary>
/// Imports Meter reference data and ResourceCost rows from the Azure billing sheet CSV.
///
/// Checks each row's SubscriptionId column against every subscription in the Subscription
/// table — anything matching one of ours gets processed; rows belonging to subscriptions we
/// don't track are skipped.
///
/// If a row's resource isn't found (most commonly: it's since been deleted in Azure, and so
/// the last Resources refresh never picked it up), it's reconstructed directly from the cost
/// sheet's own ResourceName/ResourceGroup/Tags columns rather than logged as an exception —
/// Resource.Source = "CostSheet" distinguishes these from normally-synced resources, and
/// IsActive is set to false (this assumes Resources is always refreshed before Resource Costs,
/// which is the established workflow order).
/// </summary>
public class BillingImportService
{
    private readonly IDbContextFactory<AppDbContext> _dbFactory;
    private readonly ILogger<BillingImportService> _logger;

    private const string Unassigned = "UNASSIGNED";

    public BillingImportService(IDbContextFactory<AppDbContext> dbFactory, ILogger<BillingImportService> logger)
    {
        _dbFactory = dbFactory;
        _logger    = logger;
    }

    // ── Public entry points ───────────────────────────────────────────────────

    /// <summary>Syncs Meter reference data only (insert new, skip existing).</summary>
    public async Task<BillingImportSummary> SyncMetersAsync(string billingSheetPath)
    {
        ValidatePath(billingSheetPath);
        var rows = ParseCsv(billingSheetPath);

        using var db = await _dbFactory.CreateDbContextAsync();
        var (inserted, alreadyExisted) = await SeedMetersAsync(db, rows);

        return new BillingImportSummary { MetersInserted = inserted, MetersAlreadyExisted = alreadyExisted };
    }

    /// <summary>
    /// Seeds meters, then inserts ResourceCost rows. Rows belonging to a subscription we
    /// don't track are skipped; rows whose resource isn't found are reconstructed from the
    /// cost sheet itself rather than logged as exceptions, where possible.
    /// </summary>
    public async Task<BillingImportSummary> SyncResourceCostsAsync(string billingSheetPath)
    {
        ValidatePath(billingSheetPath);
        var rows     = ParseCsv(billingSheetPath);
        var fileName = Path.GetFileName(billingSheetPath);

        using var db = await _dbFactory.CreateDbContextAsync();

        var (metersInserted, metersAlreadyExisted) = await SeedMetersAsync(db, rows);
        var meterLookup = await db.Meters
            .ToDictionaryAsync(
                m => new MeterKey(
                    m.MeterCategory.Trim(),
                    m.MeterSubCategory.Trim(),
                    m.MeterName.Trim(),
                    m.UnitOfMeasure.Trim()),
                m => m.Id,
                MeterKeyComparer.Instance);

        var summary = await InsertResourceCostsAsync(db, rows, meterLookup, fileName, _logger);
        summary.MetersInserted       = metersInserted;
        summary.MetersAlreadyExisted = metersAlreadyExisted;

        return summary;
    }

    // ── Step 1: Meters ────────────────────────────────────────────────────────

    private static async Task<(int Inserted, int AlreadyExisted)> SeedMetersAsync(AppDbContext db, List<BillingRow> rows)
    {
        // Use a case-insensitive, trim-normalised comparer — SQL Server's default CI collation
        // treats "Bandwidth" == "bandwidth" but C# record equality doesn't, causing the dictionary
        // lookup to miss existing rows and then fail on insert with a duplicate key error.
        var existing = await db.Meters
            .ToDictionaryAsync(
                m => new MeterKey(
                    m.MeterCategory.Trim(),
                    m.MeterSubCategory.Trim(),
                    m.MeterName.Trim(),
                    m.UnitOfMeasure.Trim()),
                m => m.Id,
                MeterKeyComparer.Instance);

        var incoming = rows
            .Select(r => new MeterKey(
                r.MeterCategory.Trim(),
                r.MeterSubCategory.Trim(),
                r.MeterName.Trim(),
                r.UnitOfMeasure.Trim()))
            .Distinct(MeterKeyComparer.Instance)
            .ToList();

        int inserted = 0;
        foreach (var key in incoming)
        {
            if (existing.ContainsKey(key)) continue;

            try
            {
                var entity = new Meter
                {
                    MeterCategory    = key.Category,
                    MeterSubCategory = key.SubCategory,
                    MeterName        = key.Name,
                    UnitOfMeasure    = key.Unit,
                };
                db.Meters.Add(entity);
                await db.SaveChangesAsync();

                existing[key] = entity.Id;
                inserted++;
            }
            catch (DbUpdateException ex) when (
                ex.InnerException?.Message.Contains("duplicate key", StringComparison.OrdinalIgnoreCase) == true ||
                ex.InnerException?.Message.Contains("Cannot insert duplicate", StringComparison.OrdinalIgnoreCase) == true)
            {
                // The DB has a case/whitespace variant the dictionary missed.
                // Clear the change tracker, load the real row and add it to the lookup so
                // subsequent cost rows can still find this meter's Id.
                db.ChangeTracker.Clear();
                var all = await db.Meters.ToListAsync();
                var real = all.FirstOrDefault(m =>
                    string.Equals(m.MeterCategory.Trim(),    key.Category,    StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(m.MeterSubCategory.Trim(), key.SubCategory, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(m.MeterName.Trim(),        key.Name,        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(m.UnitOfMeasure.Trim(),    key.Unit,        StringComparison.OrdinalIgnoreCase));
                if (real is not null)
                    existing[key] = real.Id;
            }
        }

        return (inserted, incoming.Count - inserted);
    }

    // ── Step 2: ResourceCosts ─────────────────────────────────────────────────

    private static async Task<BillingImportSummary> InsertResourceCostsAsync(
        AppDbContext db,
        List<BillingRow> rows,
        Dictionary<MeterKey, int> meterLookup,
        string sourceFileName,
        ILogger<BillingImportService> logger)
    {
        // Every subscription we currently track, mapped to its DB Id — used both to decide
        // whether a row is ours, and (for cost-sheet-reconstructed resources) which
        // Subscription row to attach the new Resource to.
        var subscriptionIds = await db.Subscriptions
            .ToDictionaryAsync(s => s.AzureSubscriptionId.ToUpperInvariant(), s => s.Id);

        // Resource lookup: AzureResourceId.lower → Resource entity. Includes both active and
        // deleted resources — a resource can appear on a billing sheet after deletion.
        // Where both an active and a deleted row exist for the same id, prefer the active one.
        // Holds full entities (not just Ids) so newly-created resources can be referenced via
        // navigation before they have a real Id — EF Core's change tracker wires up the FK
        // automatically once SaveChanges assigns one.
        var resourceLookup = (await db.Resources.ToListAsync())
            .GroupBy(r => r.AzureResourceId.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.IsActive).First());

        // For resolving Application/Owner on cost-sheet-reconstructed resources
        var appMappings = await db.RawValueMappings
            .Where(m => m.EntityType == "Application")
            .ToDictionaryAsync(m => m.RawValue.ToUpperInvariant(), m => m.CanonicalName);
        var ownerMappings = await db.RawValueMappings
            .Where(m => m.EntityType == "BusinessOwner")
            .ToDictionaryAsync(m => m.RawValue.ToUpperInvariant(), m => m.CanonicalName);
        var appIds   = await db.Applications.ToDictionaryAsync(a => a.ApplicationName.ToUpperInvariant(), a => a.Id);
        var ownerIds = await db.BusinessOwners.ToDictionaryAsync(o => o.OwnerName.ToUpperInvariant(), o => o.Id);
        var unassignedAppId   = appIds.GetValueOrDefault(Unassigned);
        var unassignedOwnerId = ownerIds.GetValueOrDefault(Unassigned);

        var summary = new BillingImportSummary();
        int processedSinceLastSave = 0;

        foreach (var row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.AzureSubscriptionId) ||
                !subscriptionIds.TryGetValue(row.AzureSubscriptionId.ToUpperInvariant(), out var dbSubscriptionId))
            {
                summary.OtherSubscriptionRowsSkipped++;
                continue;
            }

            var meterKey = new MeterKey(
                row.MeterCategory.Trim(),
                row.MeterSubCategory.Trim(),
                row.MeterName.Trim(),
                row.UnitOfMeasure.Trim());
            var meterId  = meterLookup[meterKey];
            var resourceKey = row.AzureResourceId.ToLowerInvariant();

            if (resourceLookup.TryGetValue(resourceKey, out var resource))
            {
                db.ResourceCosts.Add(new ResourceCost
                {
                    AzureResourceId    = row.AzureResourceId,
                    Resource           = resource,
                    MeterId            = meterId,
                    Quantity           = row.Quantity,
                    Cost               = row.Cost,
                    EffectivePrice     = row.EffectivePrice,
                    UnitPrice          = row.UnitPrice,
                    PAYGPrice          = row.PAYGPrice,
                    OfferId            = row.OfferId,
                    PricingModel       = row.PricingModel,
                    BillingPeriodStart = row.BillingPeriodStart,
                });
                summary.CostsInserted++;
            }
            else
            {
                // Not found — most likely deleted in Azure since the last Resources refresh.
                // Reconstruct it from this row's own ResourceName/ResourceGroup/Tags rather
                // than logging an exception.
                var rawTags   = ParseTagsColumn(row.RawTags);
                var costCentre = (TagExtractor.TryGetRawValue(rawTags, "CostCentre") ?? string.Empty).Trim();
                if (costCentre.Length > 30) costCentre = costCentre[..30];

                var hoursOfOperation = TagExtractor.ClassifyHoursOfOperation(rawTags);

                var applicationId = ResolveCanonicalId(rawTags, "ApplicationName",  appMappings,   appIds,   unassignedAppId);
                var ownerId        = ResolveCanonicalId(rawTags, "ApplicationOwner", ownerMappings, ownerIds, unassignedOwnerId);

                var newResource = new Resource
                {
                    AzureResourceId  = row.AzureResourceId,
                    ResourceName     = row.ResourceName,
                    ResourceGroup    = row.ResourceGroup,
                    CostCentre       = costCentre,
                    HoursOfOperation = hoursOfOperation,
                    SubscriptionId   = dbSubscriptionId,
                    IsActive         = false,
                    Source           = "CostSheet",
                };
                newResource.ResourceApps.Add(new ResourceApp { ApplicationId = applicationId, Weight = 1.0m, Source = "TagSync" });
                newResource.ResourceOwners.Add(new ResourceOwner { OwnerId = ownerId, Source = "TagSync" });

                db.Resources.Add(newResource);
                resourceLookup[resourceKey] = newResource; // later rows for the same resource in this run reuse it

                db.ResourceCosts.Add(new ResourceCost
                {
                    AzureResourceId    = row.AzureResourceId,
                    Resource           = newResource,
                    MeterId            = meterId,
                    Quantity           = row.Quantity,
                    Cost               = row.Cost,
                    EffectivePrice     = row.EffectivePrice,
                    UnitPrice          = row.UnitPrice,
                    PAYGPrice          = row.PAYGPrice,
                    OfferId            = row.OfferId,
                    PricingModel       = row.PricingModel,
                    BillingPeriodStart = row.BillingPeriodStart,
                });

                summary.ResourcesCreatedFromCostSheet++;
                summary.CostsInserted++;
            }

            processedSinceLastSave++;
            if (processedSinceLastSave >= 500)
            {
                try
                {
                    await db.SaveChangesAsync();
                    processedSinceLastSave = 0;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Batch save failed at ~row {Row} of {Total} in {File}. " +
                        "Costs inserted so far: {Inserted}. Exceptions logged: {Exceptions}. " +
                        "Inner exception: {Inner}",
                        summary.CostsInserted + summary.ExceptionsLogged,
                        rows.Count,
                        sourceFileName,
                        summary.CostsInserted,
                        summary.ExceptionsLogged,
                        ex.InnerException?.Message ?? "none");
                    throw;
                }
            }
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Final save failed for {File}. " +
                "Costs inserted: {Inserted}. Exceptions logged: {Exceptions}. " +
                "Inner exception: {Inner}",
                sourceFileName,
                summary.CostsInserted,
                summary.ExceptionsLogged,
                ex.InnerException?.Message ?? "none");
            throw;
        }

        logger.LogInformation(
            "Billing import complete for {File}: {Inserted} cost rows, " +
            "{Exceptions} exceptions, {SkippedSubs} rows skipped (other subscriptions), " +
            "{CreatedResources} resources reconstructed from cost sheet.",
            sourceFileName,
            summary.CostsInserted,
            summary.ExceptionsLogged,
            summary.OtherSubscriptionRowsSkipped,
            summary.ResourcesCreatedFromCostSheet);

        return summary;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses the cost sheet's Tags column. Azure's export typically gives the *inner*
    /// content of a JSON object without the surrounding braces (e.g. "Key": "Value","Key2": "Value2"),
    /// so this adds them back if they're missing before deserializing. Malformed/empty input
    /// just yields no tags rather than failing the whole row.
    /// </summary>
    private static Dictionary<string, string> ParseTagsColumn(string rawTagsValue)
    {
        if (string.IsNullOrWhiteSpace(rawTagsValue))
            return [];

        var trimmed = rawTagsValue.Trim();
        var json    = trimmed.StartsWith('{') ? trimmed : "{" + trimmed + "}";

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static int ResolveCanonicalId(
        Dictionary<string, string> tags,
        string tagKey,
        Dictionary<string, string> mappings,
        Dictionary<string, int> idsByName,
        int fallbackId)
    {
        var rawValue = TagExtractor.TryGetRawValue(tags, tagKey);
        if (rawValue is not null &&
            mappings.TryGetValue(rawValue.ToUpperInvariant(), out var canonical) &&
            idsByName.TryGetValue(canonical.ToUpperInvariant(), out var id))
        {
            return id;
        }

        return fallbackId;
    }

    private static void ValidatePath(string billingSheetPath)
    {
        if (string.IsNullOrWhiteSpace(billingSheetPath))
            throw new InvalidOperationException(
                "BillingSheetPath is not configured. Set it in appsettings.json.");

        if (!File.Exists(billingSheetPath))
            throw new FileNotFoundException($"Billing sheet not found: {billingSheetPath}");
    }

    // ── CSV parsing ───────────────────────────────────────────────────────────

    private static List<BillingRow> ParseCsv(string path)
    {
        var rows = new List<BillingRow>();

        // Read the whole file and tokenize as one continuous stream — a newline only ends
        // a row when we're outside a quoted field. Splitting into lines first (e.g. via
        // File.ReadAllLines) breaks the moment any field — Description, AdditionalInfo,
        // Tags — contains a literal embedded line break inside its quotes, silently
        // corrupting that row and every column after it for that line.
        var allRows = ParseCsvRows(File.ReadAllText(path));

        if (allRows.Count < 2)
            throw new InvalidOperationException("Billing sheet is empty or has no data rows.");

        var headers = allRows[0];
        var idx = headers
            .Select((h, i) => (h.Trim().ToLowerInvariant(), i))
            .ToDictionary(t => t.Item1, t => t.i);

        int Col(string name) => idx.TryGetValue(name.ToLowerInvariant(), out var i) ? i : -1;

        int iResourceId         = Col("ResourceId");
        int iResourceName       = Col("ResourceName");
        int iResourceGroup      = Col("ResourceGroup");
        int iSubscriptionId     = Col("SubscriptionId");
        int iTags               = Col("Tags");
        int iBillingPeriodStart = Col("BillingPeriodStartDate");
        int iMeterCategory      = Col("MeterCategory");
        int iMeterSubCategory   = Col("MeterSubCategory");
        int iMeterName          = Col("MeterName");
        int iUnitOfMeasure      = Col("UnitOfMeasure");
        int iQuantity           = Col("Quantity");
        int iCost               = Col("Cost");
        int iEffectivePrice     = Col("EffectivePrice");
        int iUnitPrice          = Col("UnitPrice");
        int iPayGPrice          = Col("PayGPrice");
        int iOfferId            = Col("OfferId");
        int iPricingModel       = Col("PricingModel");
        int iMeterRegion        = Col("MeterRegion");

        for (int rowNum = 1; rowNum < allRows.Count; rowNum++)
        {
            var cols = allRows[rowNum];
            if (cols.Count == 1 && string.IsNullOrWhiteSpace(cols[0])) continue; // blank trailing row

            var resourceId = Get(cols, iResourceId);
            if (string.IsNullOrWhiteSpace(resourceId)) continue;

            var billingStartRaw = Get(cols, iBillingPeriodStart);
            if (!DateTime.TryParse(billingStartRaw, CultureInfo.InvariantCulture, DateTimeStyles.None, out var billingStart))
                continue;

            rows.Add(new BillingRow
            {
                AzureResourceId     = resourceId,
                ResourceName        = Get(cols, iResourceName),
                ResourceGroup       = Get(cols, iResourceGroup),
                AzureSubscriptionId = Get(cols, iSubscriptionId),
                RawTags             = Get(cols, iTags),
                BillingPeriodStart  = billingStart,
                MeterCategory       = Get(cols, iMeterCategory),
                MeterSubCategory    = Get(cols, iMeterSubCategory),
                MeterName           = Get(cols, iMeterName),
                UnitOfMeasure       = Get(cols, iUnitOfMeasure),
                Quantity            = ParseDecimal(cols, iQuantity),
                Cost                = ParseDecimal(cols, iCost),
                EffectivePrice      = ParseDecimal(cols, iEffectivePrice),
                UnitPrice           = ParseDecimal(cols, iUnitPrice),
                PAYGPrice           = ParseDecimal(cols, iPayGPrice),
                OfferId             = Get(cols, iOfferId),
                PricingModel        = Get(cols, iPricingModel),
                MeterRegion         = Get(cols, iMeterRegion),
            });
        }

        return rows;
    }

    /// <summary>
    /// Tokenizes the entire file content into rows of fields. Unlike splitting by line first,
    /// this treats a newline as a row separator only when it occurs outside a quoted field —
    /// a literal embedded line break inside quotes (e.g. a multi-line Description) is kept as
    /// part of that field's content instead of incorrectly starting a new row.
    /// </summary>
    private static List<List<string>> ParseCsvRows(string text)
    {
        var rows = new List<List<string>>();
        var currentRow = new List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        int i = 0;
        int n = text.Length;

        while (i < n)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"' && i + 1 < n && text[i + 1] == '"') { current.Append('"'); i += 2; continue; }
                if (c == '"') { inQuotes = false; i++; continue; }
                current.Append(c); // includes literal \r or \n while inside quotes
                i++;
                continue;
            }

            if (c == '"') { inQuotes = true; i++; continue; }

            if (c == ',')
            {
                currentRow.Add(current.ToString());
                current.Clear();
                i++;
                continue;
            }

            if (c == '\r' || c == '\n')
            {
                currentRow.Add(current.ToString());
                current.Clear();
                rows.Add(currentRow);
                currentRow = [];
                i++;
                if (c == '\r' && i < n && text[i] == '\n') i++; // swallow the \n of a \r\n pair
                continue;
            }

            current.Append(c);
            i++;
        }

        // flush a trailing row if the file doesn't end with a newline
        if (current.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(current.ToString());
            rows.Add(currentRow);
        }

        return rows;
    }

    private static string Get(List<string> cols, int idx)
        => idx >= 0 && idx < cols.Count ? cols[idx].Trim() : string.Empty;

    private static decimal ParseDecimal(List<string> cols, int idx)
        => idx >= 0 && idx < cols.Count &&
           decimal.TryParse(cols[idx].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var val)
            ? val : 0m;

    // ── Inner types ───────────────────────────────────────────────────────────

    private sealed class BillingRow
    {
        public string   AzureResourceId     { get; set; } = string.Empty;
        public string   ResourceName        { get; set; } = string.Empty;
        public string   ResourceGroup       { get; set; } = string.Empty;
        public string   AzureSubscriptionId { get; set; } = string.Empty;
        public string   RawTags             { get; set; } = string.Empty;
        public DateTime BillingPeriodStart  { get; set; }
        public string   MeterCategory       { get; set; } = string.Empty;
        public string   MeterSubCategory    { get; set; } = string.Empty;
        public string   MeterName           { get; set; } = string.Empty;
        public string   UnitOfMeasure       { get; set; } = string.Empty;
        public decimal  Quantity            { get; set; }
        public decimal  Cost                { get; set; }
        public decimal  EffectivePrice      { get; set; }
        public decimal  UnitPrice           { get; set; }
        public decimal  PAYGPrice           { get; set; }
        public string   OfferId             { get; set; } = string.Empty;
        public string   PricingModel        { get; set; } = string.Empty;
        public string   MeterRegion         { get; set; } = string.Empty;
    }

    /// <summary>
    /// Case-insensitive, trim-normalised equality comparer for MeterKey.
    /// SQL Server's default CI collation treats "Bandwidth" == "bandwidth",
    /// but C# record equality doesn't — this comparer aligns the two.
    /// </summary>
    private sealed class MeterKeyComparer : IEqualityComparer<MeterKey>
    {
        public static readonly MeterKeyComparer Instance = new();

        public bool Equals(MeterKey? x, MeterKey? y)
        {
            if (x is null && y is null) return true;
            if (x is null || y is null) return false;
            return string.Equals(x.Category.Trim(),    y.Category.Trim(),    StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.SubCategory.Trim(), y.SubCategory.Trim(), StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Name.Trim(),        y.Name.Trim(),        StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.Unit.Trim(),        y.Unit.Trim(),        StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(MeterKey obj) => HashCode.Combine(
            obj.Category.Trim().ToUpperInvariant(),
            obj.SubCategory.Trim().ToUpperInvariant(),
            obj.Name.Trim().ToUpperInvariant(),
            obj.Unit.Trim().ToUpperInvariant());
    }

    private sealed record MeterKey(string Category, string SubCategory, string Name, string Unit);
}
