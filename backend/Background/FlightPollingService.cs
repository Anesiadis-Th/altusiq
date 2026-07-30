using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using AltusIQ.Api.Health;
using AltusIQ.Api.Hubs;
using AltusIQ.Api.Models;
using AltusIQ.Api.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace AltusIQ.Api.Background;

public class FlightPollingService : BackgroundService
{
    private const int RequiredStateFields = 12;

    private readonly IOpenSkyAuthService _authService;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<FlightPollingService> _logger;
    private readonly IHubContext<FlightHub> _hubContext;
    private readonly FlightIngestionService _ingestionService;
    private readonly LiveSnapshotStore _snapshotStore;
    private readonly PollHeartbeat _heartbeat;
    private readonly IngestionSettings _settings;

    public FlightPollingService(
        IOpenSkyAuthService authService,
        HttpClient httpClient,
        IConfiguration config,
        ILogger<FlightPollingService> logger,
        IHubContext<FlightHub> hubContext,
        FlightIngestionService ingestionService,
        LiveSnapshotStore snapshotStore,
        PollHeartbeat heartbeat,
        IOptions<IngestionSettings> settings)
    {
        _authService = authService;
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
        _hubContext = hubContext;
        _ingestionService = ingestionService;
        _snapshotStore = snapshotStore;
        _heartbeat = heartbeat;
        _settings = settings.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = _settings.PollIntervalSeconds;

        _logger.LogInformation(
            "Flight polling service started. Interval: {Interval}s",
            intervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Error polling OpenSky. Will retry in {Interval}s",
                    intervalSeconds);
            }

            await Task.Delay(
                TimeSpan.FromSeconds(intervalSeconds), stoppingToken);
        }

        _logger.LogInformation("Flight polling service stopped");
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        var token = await _authService.GetTokenAsync(cancellationToken);

        var baseUrl = _config["OpenSky:ApiBaseUrl"]
            ?? "https://opensky-network.org";

        var response = await SendStatesRequestAsync(
            baseUrl, token, cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _logger.LogWarning(
                "OpenSky returned 401, refreshing token and retrying once");
            response.Dispose();
            _authService.InvalidateToken();
            token = await _authService.GetTokenAsync(cancellationToken);
            response = await SendStatesRequestAsync(
                baseUrl, token, cancellationToken);
        }

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var aircraft = ParseStates(json);

        _logger.LogInformation(
            "Received {Count} aircraft from OpenSky", aircraft.Count);

        _snapshotStore.Update(aircraft);
        _heartbeat.RecordSuccess();

        await _hubContext.Clients.All.SendAsync(
            "ReceiveFlightData", aircraft, cancellationToken);

        try
        {
            await _ingestionService.ProcessAsync(aircraft, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Flight ingestion failed");
        }
    }

    private async Task<HttpResponseMessage> SendStatesRequestAsync(
        string baseUrl, string token, CancellationToken cancellationToken)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{baseUrl}/api/states/all");

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(
            request, cancellationToken);

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

    private List<Aircraft> ParseStates(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("states", out var states)
            || states.ValueKind == JsonValueKind.Null)
            return [];

        var result = new List<Aircraft>();
        var skipped = 0;

        foreach (var state in states.EnumerateArray())
        {
            var aircraft = TryParseState(state);

            if (aircraft is null)
                skipped++;
            else
                result.Add(aircraft);
        }

        if (skipped > 0)
            _logger.LogWarning(
                "Skipped {Skipped} unusable state rows, kept {Kept}",
                skipped, result.Count);

        return result;
    }

    private static Aircraft? TryParseState(JsonElement state)
    {
        if (state.ValueKind != JsonValueKind.Array
            || state.GetArrayLength() < RequiredStateFields)
            return null;

        var icao24 = ReadString(state[0]);

        if (string.IsNullOrWhiteSpace(icao24))
            return null;

        if (state[4].ValueKind != JsonValueKind.Number
            || state[5].ValueKind != JsonValueKind.Number
            || state[6].ValueKind != JsonValueKind.Number)
            return null;

        return new Aircraft
        {
            Icao24             = icao24,
            Callsign           = ReadString(state[1])?.Trim(),
            OriginCountry      = ReadString(state[2]),
            Longitude          = state[5].GetDouble(),
            Latitude           = state[6].GetDouble(),
            BarometricAltitude = ReadDouble(state[7]),
            OnGround           = state[8].ValueKind == JsonValueKind.True,
            Velocity           = ReadDouble(state[9]),
            Heading            = ReadDouble(state[10]),
            VerticalRate       = ReadDouble(state[11]),
            LastContact        = state[4].GetInt64()
        };
    }

    private static string? ReadString(JsonElement element) =>
        element.ValueKind == JsonValueKind.String ? element.GetString() : null;

    private static double? ReadDouble(JsonElement element) =>
        element.ValueKind == JsonValueKind.Number ? element.GetDouble() : null;
}