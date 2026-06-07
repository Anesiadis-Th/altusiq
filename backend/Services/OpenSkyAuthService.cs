using System.Net.Http.Headers;

namespace AltusIQ.Api.Services;


public class OpenSkyAuthService : IOpenSkyAuthService
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<OpenSkyAuthService> _logger;


    private string? _cachedToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public OpenSkyAuthService(
        HttpClient httpClient,
        IConfiguration config,
        ILogger<OpenSkyAuthService> logger)
    {
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
    }

    public async Task<string> GetTokenAsync(
        CancellationToken cancellationToken = default)
    {

        if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiry)
            return _cachedToken;


        await _tokenLock.WaitAsync(cancellationToken);
        try
        {

            if (_cachedToken is not null && DateTime.UtcNow < _tokenExpiry)
                return _cachedToken;

            _logger.LogInformation("Fetching new OpenSky OAuth2 token");

            var clientId = _config["OpenSky:ClientId"]
                ?? throw new InvalidOperationException(
                    "OpenSky:ClientId is not configured");

            var clientSecret = _config["OpenSky:ClientSecret"]
                ?? throw new InvalidOperationException(
                    "OpenSky:ClientSecret is not configured");

            var tokenUrl = _config["OpenSky:TokenUrl"]
                ?? throw new InvalidOperationException(
                    "OpenSky:TokenUrl is not configured");


            var formData = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", clientId),
                new KeyValuePair<string, string>("client_secret", clientSecret)
            });

            var response = await _httpClient.PostAsync(
                tokenUrl, formData, cancellationToken);

            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadFromJsonAsync<TokenResponse>(
                cancellationToken: cancellationToken)
                ?? throw new InvalidOperationException(
                    "OpenSky token response was empty");

            _cachedToken = json.AccessToken
            ?? throw new InvalidOperationException(
             "OpenSky token response did not contain an access_token field");
            _tokenExpiry = DateTime.UtcNow
                .AddSeconds(json.ExpiresIn - 60);

            _logger.LogInformation(
                "OpenSky token acquired, expires in {Seconds}s",
                json.ExpiresIn - 60);

            return _cachedToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

  private sealed record TokenResponse(
    [property: System.Text.Json.Serialization.JsonPropertyName("access_token")]
    string AccessToken,
    [property: System.Text.Json.Serialization.JsonPropertyName("expires_in")]
    int ExpiresIn);
}