using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace AzureResourceLister;

public class AzureTokenService
{
    private readonly AppConfig _config;
    private readonly HttpClient _httpClient;

    // Simple in-memory cache to avoid re-fetching on every call
    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    public AzureTokenService(AppConfig config)
    {
        _config = config;
        _httpClient = new HttpClient();
    }

    /// <summary>
    /// Retrieves an OAuth2 access token using the client credentials flow
    /// against the Microsoft Identity Platform.
    /// Endpoint: POST https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token
    /// </summary>
    public async Task<string> GetAccessTokenAsync()
    {
        // Return cached token if still valid (with 60s buffer)
        if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiry.AddSeconds(-60))
            return _cachedToken;

        var tokenEndpoint = $"https://login.microsoftonline.com/{_config.TenantId}/oauth2/v2.0/token";

        var formData = new Dictionary<string, string>
        {
            ["grant_type"]    = "client_credentials",
            ["client_id"]     = _config.ClientId,
            ["client_secret"] = _config.ClientSecret,
            ["scope"]         = "https://management.azure.com/.default",
        };

        var response = await _httpClient.PostAsync(tokenEndpoint, new FormUrlEncodedContent(formData));

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Token request failed ({response.StatusCode}): {errorBody}");
        }

        var tokenResponse = await response.Content.ReadFromJsonAsync<TokenResponse>()
            ?? throw new InvalidOperationException("Failed to deserialize token response.");

        _cachedToken = tokenResponse.AccessToken;
        _tokenExpiry = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn);

        return _cachedToken;
    }

    private sealed class TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
