using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AzureResourceLister.Llm;

/// <summary>
/// Calls a locally-running Ollama instance (https://ollama.com) via its REST API
/// on localhost. No data leaves the machine — this is the right choice when
/// processing sensitive business data like application or team names.
///
/// Prerequisites:
///   1. Install Ollama (https://ollama.com/download)
///   2. Pull a model, e.g.:  ollama pull llama3.1:8b
///   3. Ollama runs a local server automatically (default: http://localhost:11434)
/// </summary>
public class OllamaLlmProvider : ILlmProvider
{
    private readonly string _model;
    private readonly HttpClient _httpClient;

    public string Description => $"Ollama (local, model: {_model}) — fully offline, nothing leaves this machine.";

    public OllamaLlmProvider(string endpoint, string model)
    {
        _model = model;
        _httpClient = new HttpClient { BaseAddress = new Uri(endpoint.TrimEnd('/') + "/") };
    }

    public async Task<string> CompleteAsync(string prompt)
    {
        var requestBody = new
        {
            model = _model,
            prompt,
            stream = false,
            format = "json",   // forces the model to emit valid JSON where supported
            options = new
            {
                temperature = 0.1,    // low — consistent grouping decisions, not creative variation
                num_predict = 4096,   // cap output length, matching the other providers
                num_ctx = 16384,      // context = input + output combined; generous headroom as the app list grows
            },
        };

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.PostAsJsonAsync("api/generate", requestBody);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(
                $"Could not reach Ollama at {_httpClient.BaseAddress}. Is Ollama running? " +
                $"Start it with 'ollama serve' or open the Ollama desktop app. ({ex.Message})", ex);
        }

        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(
                $"Ollama call failed ({response.StatusCode}): {responseBody}\n" +
                $"If this mentions a missing model, run: ollama pull {_model}");

        var parsed = System.Text.Json.JsonSerializer.Deserialize<OllamaGenerateResponse>(
            responseBody, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to deserialize Ollama response.");

        return parsed.Response;
    }

    private sealed class OllamaGenerateResponse
    {
        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;
    }
}
