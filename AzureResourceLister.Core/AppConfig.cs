using Microsoft.Extensions.Configuration;

namespace AzureResourceLister;

/// <summary>
/// Application settings. In the web app, ASP.NET Core's configuration system already
/// merges appsettings.json + environment variables, so this class no longer builds its
/// own ConfigurationBuilder — it just reads from whatever IConfiguration is injected.
/// </summary>
public class AppConfig
{
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>How many days back to query the Activity Log for deleted resources (1–90, default 10).</summary>
    public int DeletedResourceLookbackDays { get; set; } = 10;

    /// <summary>Which LLM backend to use for AI data cleansing: "Ollama" (local, default) or "Anthropic" (cloud).</summary>
    public string AiProvider { get; set; } = "Ollama";
    public string OllamaEndpoint { get; set; } = "http://localhost:11434/";
    public string OllamaModel { get; set; } = "llama3.1:8b";

    public string AnthropicApiKey { get; set; } = string.Empty;
    public string AnthropicModel { get; set; } = "claude-sonnet-4-6";

    /// <summary>Your Azure AI Foundry resource endpoint, e.g. "https://your-resource.services.ai.azure.com/openai/v1".</summary>
    public string AzureFoundryEndpoint { get; set; } = string.Empty;
    public string AzureFoundryApiKey { get; set; } = string.Empty;
    public string AzureFoundryDeploymentName { get; set; } = string.Empty;

    /// <summary>Full path to the Azure billing sheet CSV.</summary>
    public string BillingSheetPath { get; set; } = string.Empty;

    /// <summary>Full path to the shared-app registry CSV.</summary>
    public string SharedAppRegistryPath { get; set; } = string.Empty;

    /// <summary>Full path to the Azure price sheet CSV (annual, used for Savings Plan analysis).</summary>
    public string PriceSheetPath { get; set; } = string.Empty;

    /// <summary>Full path to a dedicated Reserved Instance price sheet CSV (annual, separate
    /// file from PriceSheetPath — used for Reserved Instance analysis).</summary>
    public string ReservedInstancePriceSheetPath { get; set; } = string.Empty;

    public static AppConfig FromConfiguration(IConfiguration configuration)
    {
        var lookbackDays = int.TryParse(configuration["DeletedResourceLookbackDays"], out var days) && days is >= 1 and <= 90
            ? days
            : 10;

        return new AppConfig
        {
            TenantId       = configuration["AzureAd:TenantId"]       ?? string.Empty,
            ClientId       = configuration["AzureAd:ClientId"]       ?? string.Empty,
            ClientSecret   = configuration["AzureAd:ClientSecret"]   ?? string.Empty,

            DeletedResourceLookbackDays = lookbackDays,

            AiProvider     = configuration["AiProvider"]     ?? "Ollama",
            OllamaEndpoint = configuration["OllamaEndpoint"] ?? "http://localhost:11434/",
            OllamaModel    = configuration["OllamaModel"]    ?? "llama3.1:8b",

            AnthropicApiKey = configuration["AnthropicApiKey"] ?? string.Empty,
            AnthropicModel  = configuration["AnthropicModel"]  ?? "claude-sonnet-4-6",

            AzureFoundryEndpoint       = configuration["AzureFoundryEndpoint"]       ?? string.Empty,
            AzureFoundryApiKey         = configuration["AzureFoundryApiKey"]         ?? string.Empty,
            AzureFoundryDeploymentName = configuration["AzureFoundryDeploymentName"] ?? string.Empty,

            BillingSheetPath      = configuration["BillingSheetPath"]      ?? string.Empty,
            SharedAppRegistryPath = configuration["SharedAppRegistryPath"] ?? string.Empty,
            PriceSheetPath        = configuration["PriceSheetPath"]        ?? string.Empty,
            ReservedInstancePriceSheetPath = configuration["ReservedInstancePriceSheetPath"] ?? string.Empty,
        };
    }
}
