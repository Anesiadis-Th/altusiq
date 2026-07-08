using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace AltusIQ.Api.Services;

public class OpenSkyFlightsClient : IOpenSkyFlightsClient
{
    private readonly HttpClient _httpClient;
    private readonly IOpenSkyAuthService _authService;
    private readonly IConfiguration _config;
    private readonly ILogger<OpenSkyFlightsClient> _logger;

    public OpenSkyFlightsClient(
        HttpClient httpClient,
        IOpenSkyAuthService authService,
        IConfiguration config,
        ILogger<OpenSkyFlightsClient> logger)
    {
        _httpClient = httpClient;
        _authService = authService;
        _config = config;
        _logger = logger;
    }

    public async Task<IReadOnlyList<OpenSkyFlightInfo>> GetAllFlightsAsync(
        long beginUnix,
        long endUnix,
        CancellationToken cancellationToken = default)
    {
        var token = await _authService.GetTokenAsync(cancellationToken);

        var baseUrl = _config["OpenSky:ApiBaseUrl"]
            ?? "https://opensky-network.org";

        var response = await SendFlightsRequestAsync(
            baseUrl, token, beginUnix, endUnix, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning(
                "OpenSky returned 401, refreshing token and retrying once");
            response.Dispose();
            _authService.InvalidateToken();
            token = await _authService.GetTokenAsync(cancellationToken);
            response = await SendFlightsRequestAsync(
                baseUrl, token, beginUnix, endUnix, cancellationToken);
        }

        if (response.StatusCode == HttpStatusCode.NotFound)
            return [];

        response.EnsureSuccessStatusCode();

        var flights = await response.Content
            .ReadFromJsonAsync<List<OpenSkyFlight>>(cancellationToken)
            ?? [];

        return flights
            .Where(f => f.Icao24 is not null)
            .Select(f => new OpenSkyFlightInfo(
                f.Icao24!,
                f.FirstSeen,
                f.LastSeen,
                f.EstDepartureAirport,
                f.EstArrivalAirport))
            .ToList();
    }

    private async Task<HttpResponseMessage> SendFlightsRequestAsync(
        string baseUrl,
        string token,
        long beginUnix,
        long endUnix,
        CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{baseUrl}/api/flights/all?begin={beginUnix}&end={endUnix}");

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(request, cancellationToken);

        if (response.Headers.TryGetValues(
                "X-Rate-Limit-Remaining", out var rateLimitValues))
        {
            _logger.LogInformation(
                "OpenSky rate limit remaining: {Remaining}",
                string.Join(", ", rateLimitValues));
        }
        else
        {
            _logger.LogInformation(
                "OpenSky response did not include X-Rate-Limit-Remaining header");
        }

        return response;
    }

    private sealed record OpenSkyFlight(
        [property: JsonPropertyName("icao24")] string? Icao24,
        [property: JsonPropertyName("firstSeen")] long FirstSeen,
        [property: JsonPropertyName("lastSeen")] long LastSeen,
        [property: JsonPropertyName("estDepartureAirport")] string? EstDepartureAirport,
        [property: JsonPropertyName("estArrivalAirport")] string? EstArrivalAirport);
}
