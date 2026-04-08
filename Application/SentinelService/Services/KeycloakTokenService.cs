using System.Text.Json;

namespace SentinelService.Services;

public sealed class KeycloakTokenService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;

    public KeycloakTokenService(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
    }

    public async Task<string> GetAccessTokenAsync()
    {
        var client = _httpClientFactory.CreateClient("keycloak");

        var parameters = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = _configuration["Keycloak:MicroserviceClientId"]!,
            ["client_secret"] = _configuration["Keycloak:ClientSecret"]!
        };

        var response = await client.PostAsync(
            _configuration["Keycloak:TokenEndpoint"],
            new FormUrlEncodedContent(parameters));

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("access_token").GetString()!;
    }
}