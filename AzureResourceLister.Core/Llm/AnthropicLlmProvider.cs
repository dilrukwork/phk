using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace AzureResourceLister.Llm;

/// <summary>
/// Calls the Anthropic Claude API over the internet (https://api.anthropic.com).
/// Only used when AiProvider = "Anthropic" in appsettings.json. For fully local/offline
/// processing, use OllamaLlmProvider instead — no data leaves the machine with that option.
/// </summary>
public class AnthropicLlmProvider : ILlmProvider
{
    private readonly string _apiKey;
    private readonly string _model;
    private readonly HttpClient _httpClient;

    public string Description => $"Anthropic API (model: {_model}) — data is sent over the internet to Anthropic.";

    public AnthropicLlmProvider(string apiKey, string model)
    {
        _apiKey = apiKey;
        _model = model;
        _httpClient = new HttpClient { BaseAddress = new Uri("https://api.anthropic.com/") };
    }

    public async Task<string> CompleteAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException(
                "AnthropicApiKey is not configured. Set it in appsettings.json or the ANTHROPIC_API_KEY environment variable.");

        var requestBody = new
        {
            model = _model,
            max_tokens = 8000,
            temperature = 0.1,   // low — consistent grouping decisions, not creative variation
            messages = new[] { new { role = "user", content = prompt } }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/messages")
        {
            Content = JsonContent.Create(requestBody)
        };
        request.Headers.Add("x-api-key", _apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        var response = await _httpClient.SendAsync(request);
        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Claude API call failed ({response.StatusCode}): {responseBody}");

        var parsed = JsonSerializer.Deserialize<ClaudeMessageResponse>(responseBody, JsonOpts)
            ?? throw new InvalidOperationException("Failed to deserialize Claude API response.");

        return string.Concat(parsed.Content.Where(c => c.Type == "text").Select(c => c.Text));
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    private sealed class ClaudeMessageResponse
    {
        [JsonPropertyName("content")]
        public List<ClaudeContentBlock> Content { get; set; } = [];
    }

    private sealed class ClaudeContentBlock
    {
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
        [JsonPropertyName("text")] public string Text { get; set; } = string.Empty;
    }
}
