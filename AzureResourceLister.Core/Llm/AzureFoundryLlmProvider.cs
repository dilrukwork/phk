using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AzureResourceLister.Llm;

/// <summary>
/// Calls a model deployed in your own Azure AI Foundry / Microsoft Foundry resource,
/// via the OpenAI-compatible v1 chat completions endpoint
/// (https://&lt;resource&gt;.services.ai.azure.com/openai/v1).
///
/// Data stays within your own Azure tenant/subscription — Microsoft acts as data
/// processor for serverless deployments; prompts/outputs aren't shared with the
/// underlying model provider or used for training.
///
/// Auth: a static API key, sent as a standard Bearer token (matches what the
/// OpenAI SDK does when you pass api_key instead of a DefaultAzureCredential
/// token provider).
/// </summary>
public class AzureFoundryLlmProvider : ILlmProvider
{
    private readonly string _apiKey;
    private readonly string _deploymentName;
    private readonly HttpClient _httpClient;

    public string Description => $"Azure AI Foundry (deployment: {_deploymentName}) — data stays in your Azure tenant.";

    public AzureFoundryLlmProvider(string endpoint, string apiKey, string deploymentName)
    {
        _apiKey = apiKey;
        _deploymentName = deploymentName;
        _httpClient = new HttpClient { BaseAddress = new Uri(endpoint.TrimEnd('/') + "/") };
    }

    public async Task<string> CompleteAsync(string prompt)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            throw new InvalidOperationException(
                "AzureFoundryApiKey is not configured. Set it in appsettings.json.");

        if (string.IsNullOrWhiteSpace(_deploymentName))
            throw new InvalidOperationException(
                "AzureFoundryDeploymentName is not configured. Set it in appsettings.json.");

        const int maxAttempts = 4;
        var delay = TimeSpan.FromSeconds(2);

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var requestBody = new
            {
                model = _deploymentName,
                messages = new[] { new { role = "user", content = prompt } },
                temperature = 0.1,   // low — we want consistent grouping decisions, not creative variation
                max_tokens = 4096,   // the model's actual cap — avoids truncating a large JSON response mid-way
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "chat/completions")
            {
                Content = JsonContent.Create(requestBody)
            };
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _apiKey);

            var response = await _httpClient.SendAsync(request);

            if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < maxAttempts)
            {
                // Honour Retry-After if the service gave us one, otherwise back off and try again
                var wait = response.Headers.RetryAfter?.Delta ?? delay;
                await Task.Delay(wait);
                delay *= 2;
                continue;
            }

            var responseBody = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(
                    $"Azure AI Foundry call failed ({response.StatusCode}) after {attempt} attempt(s): {responseBody}");

            var parsed = System.Text.Json.JsonSerializer.Deserialize<ChatCompletionResponse>(
                responseBody, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?? throw new InvalidOperationException("Failed to deserialize Azure AI Foundry response.");

            return parsed.Choices.FirstOrDefault()?.Message.Content ?? string.Empty;
        }

        throw new InvalidOperationException("Azure AI Foundry call failed: exhausted retries after repeated rate limiting.");
    }

    private sealed class ChatCompletionResponse
    {
        [JsonPropertyName("choices")]
        public List<Choice> Choices { get; set; } = [];
    }

    private sealed class Choice
    {
        [JsonPropertyName("message")]
        public ChatMessage Message { get; set; } = new();
    }

    private sealed class ChatMessage
    {
        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;
    }
}
