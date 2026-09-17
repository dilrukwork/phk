using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AzureResourceLister;

public class AzureResource
{
    public string Id            { get; set; } = string.Empty;
    public string Name          { get; set; } = string.Empty;
    public string Type          { get; set; } = string.Empty;
    public string Location      { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;

    /// <summary>Raw tags as returned by the ARM API (key casing preserved).</summary>
    public Dictionary<string, string> Tags { get; set; } = [];
}

public class AzureResourceGroup
{
    public string Name { get; set; } = string.Empty;
    public Dictionary<string, string> Tags { get; set; } = [];
}

/// <summary>
/// Calls Azure Resource Manager. All methods take an explicit subscription ID rather than
/// reading a single one from config — the app can hold many subscriptions in the Subscription
/// table, and callers (e.g. the Applications import pipeline) decide which one(s) to target.
/// </summary>
public class AzureResourceService
{
    private readonly AzureTokenService _tokenService;
    private readonly HttpClient _httpClient;

    private const string ApiVersion = "2021-04-01";

    public AzureResourceService(AzureTokenService tokenService)
    {
        _tokenService = tokenService;
        _httpClient = new HttpClient { BaseAddress = new Uri("https://management.azure.com/") };
    }

    /// <summary>
    /// Lists all resources in the given subscription with pagination.
    /// GET https://management.azure.com/subscriptions/{id}/resources?api-version=2021-04-01&$expand=tags
    /// </summary>
    public async Task<List<AzureResource>> GetResourcesAsync(string subscriptionId)
    {
        var token = await _tokenService.GetAccessTokenAsync();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resources = new List<AzureResource>();
        var nextUrl = $"subscriptions/{subscriptionId}/resources?api-version={ApiVersion}&$expand=tags";

        while (nextUrl is not null)
        {
            var response = await _httpClient.GetAsync(nextUrl);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Resources API failed ({response.StatusCode}): {err}");
            }

            var page = JsonSerializer.Deserialize<ResourceListResponse>(await response.Content.ReadAsStringAsync())
                ?? throw new InvalidOperationException("Failed to deserialize resource list.");

            foreach (var r in page.Value)
            {
                if (string.IsNullOrWhiteSpace(r.Id)) continue;
                resources.Add(new AzureResource
                {
                    Id            = r.Id,
                    Name          = r.Name          ?? string.Empty,
                    Type          = r.Type          ?? string.Empty,
                    Location      = r.Location      ?? string.Empty,
                    ResourceGroup = ExtractResourceGroup(r.Id),
                    Tags          = r.Tags          ?? [],
                });
            }

            nextUrl = page.NextLink;
        }

        return resources;
    }

    /// <summary>
    /// Lists all resource groups in the given subscription with their tags.
    /// Returns a dictionary keyed by resource group name (lower-cased) for fast lookup.
    /// GET https://management.azure.com/subscriptions/{id}/resourcegroups?api-version=2021-04-01
    /// </summary>
    public async Task<Dictionary<string, AzureResourceGroup>> GetResourceGroupsAsync(string subscriptionId)
    {
        var token = await _tokenService.GetAccessTokenAsync();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var result  = new Dictionary<string, AzureResourceGroup>(StringComparer.OrdinalIgnoreCase);
        var nextUrl = $"subscriptions/{subscriptionId}/resourcegroups?api-version={ApiVersion}";

        while (nextUrl is not null)
        {
            var response = await _httpClient.GetAsync(nextUrl);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Resource Groups API failed ({response.StatusCode}): {err}");
            }

            var page = JsonSerializer.Deserialize<ResourceGroupListResponse>(await response.Content.ReadAsStringAsync())
                ?? throw new InvalidOperationException("Failed to deserialize resource group list.");

            foreach (var rg in page.Value)
            {
                if (string.IsNullOrWhiteSpace(rg.Name)) continue;
                result[rg.Name] = new AzureResourceGroup
                {
                    Name = rg.Name,
                    Tags = rg.Tags ?? [],
                };
            }

            nextUrl = page.NextLink;
        }

        return result;
    }

    /// <summary>
    /// Lists all managed disks in the given subscription including their state (Attached/Unattached).
    /// GET https://management.azure.com/subscriptions/{id}/providers/Microsoft.Compute/disks?api-version=2023-04-02
    /// </summary>
    public async Task<List<AzureDisk>> GetDisksAsync(string subscriptionId)
    {
        var token = await _tokenService.GetAccessTokenAsync();
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var disks   = new List<AzureDisk>();
        var nextUrl = $"subscriptions/{subscriptionId}/providers/Microsoft.Compute/disks?api-version=2023-04-02";

        while (nextUrl is not null)
        {
            var response = await _httpClient.GetAsync(nextUrl);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync();
                throw new InvalidOperationException($"Disks API failed ({response.StatusCode}): {err}");
            }

            var page = JsonSerializer.Deserialize<DiskListResponse>(await response.Content.ReadAsStringAsync())
                ?? throw new InvalidOperationException("Failed to deserialize disk list.");

            foreach (var d in page.Value)
            {
                if (string.IsNullOrWhiteSpace(d.Id)) continue;
                disks.Add(new AzureDisk
                {
                    Id            = d.Id,
                    Name          = d.Name          ?? string.Empty,
                    ResourceGroup = ExtractResourceGroup(d.Id),
                    Location      = d.Location      ?? string.Empty,
                    DiskState     = d.Properties?.DiskState  ?? string.Empty,
                    DiskSizeGb    = d.Properties?.DiskSizeGB ?? 0,
                    SkuName       = d.Sku?.Name               ?? string.Empty,
                    Tags          = d.Tags                    ?? [],
                });
            }

            nextUrl = page.NextLink;
        }

        return disks;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string ExtractResourceGroup(string resourceId)
    {
        var parts = resourceId.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length - 1; i++)
            if (parts[i].Equals("resourceGroups", StringComparison.OrdinalIgnoreCase))
                return parts[i + 1];
        return string.Empty;
    }

    // ── JSON models ───────────────────────────────────────────────────────────

    private sealed class ResourceListResponse
    {
        [JsonPropertyName("value")]    public List<ResourceJson> Value    { get; set; } = [];
        [JsonPropertyName("nextLink")] public string?            NextLink { get; set; }
    }

    private sealed class ResourceJson
    {
        [JsonPropertyName("id")]       public string? Id       { get; set; }
        [JsonPropertyName("name")]     public string? Name     { get; set; }
        [JsonPropertyName("type")]     public string? Type     { get; set; }
        [JsonPropertyName("location")] public string? Location { get; set; }
        [JsonPropertyName("tags")]     public Dictionary<string, string>? Tags { get; set; }
    }

    private sealed class ResourceGroupListResponse
    {
        [JsonPropertyName("value")]    public List<ResourceGroupJson> Value    { get; set; } = [];
        [JsonPropertyName("nextLink")] public string?                 NextLink { get; set; }
    }

    private sealed class ResourceGroupJson
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("tags")] public Dictionary<string, string>? Tags { get; set; }
    }

    private sealed class DiskListResponse
    {
        [JsonPropertyName("value")]    public List<DiskJson> Value    { get; set; } = [];
        [JsonPropertyName("nextLink")] public string?        NextLink { get; set; }
    }

    private sealed class DiskJson
    {
        [JsonPropertyName("id")]         public string?                    Id         { get; set; }
        [JsonPropertyName("name")]       public string?                    Name       { get; set; }
        [JsonPropertyName("location")]   public string?                    Location   { get; set; }
        [JsonPropertyName("sku")]        public DiskSkuJson?               Sku        { get; set; }
        [JsonPropertyName("properties")] public DiskPropsJson?             Properties { get; set; }
        [JsonPropertyName("tags")]       public Dictionary<string, string>? Tags      { get; set; }
    }

    private sealed class DiskSkuJson
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    private sealed class DiskPropsJson
    {
        [JsonPropertyName("diskState")]  public string? DiskState  { get; set; }
        [JsonPropertyName("diskSizeGB")] public int?    DiskSizeGB { get; set; }
    }
}

// ── Public disk model ─────────────────────────────────────────────────────────

public class AzureDisk
{
    public string Id            { get; set; } = string.Empty;
    public string Name          { get; set; } = string.Empty;
    public string ResourceGroup { get; set; } = string.Empty;
    public string Location      { get; set; } = string.Empty;

    /// <summary>"Unattached", "Attached", "Reserved", "Frozen" etc.</summary>
    public string DiskState  { get; set; } = string.Empty;
    public int    DiskSizeGb { get; set; }

    /// <summary>"Premium_LRS", "Standard_LRS", "UltraSSD_LRS" etc.</summary>
    public string SkuName { get; set; } = string.Empty;

    public Dictionary<string, string> Tags { get; set; } = [];
}
