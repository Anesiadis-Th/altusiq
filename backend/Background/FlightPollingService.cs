using System.Net.Http.Headers;
using System.Text.Json;
using AltusIQ.Api.Hubs;
using AltusIQ.Api.Models;
using AltusIQ.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace AltusIQ.Api.Background;

public class FlightPollingService : BackgroundService
{
    private readonly IOpenSkyAuthService _authService;
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _config;
    private readonly ILogger<FlightPollingService> _logger;
    private readonly IHubContext<FlightHub> _hubContext;

    public FlightPollingService(
        IOpenSkyAuthService authService,
        HttpClient httpClient,
        IConfiguration config,
        ILogger<FlightPollingService> logger,
        IHubContext<FlightHub> hubContext)
    {
        _authService = authService;
        _httpClient = httpClient;
        _config = config;
        _logger = logger;
        _hubContext = hubContext;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = _config.GetValue<int>(
            "OpenSky:PollingIntervalSeconds", 10);

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

        var request = new HttpRequestMessage(
            HttpMethod.Get,
            $"{baseUrl}/api/states/all");

        request.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", token);

        var response = await _httpClient.SendAsync(
            request, cancellationToken);

        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var aircraft = ParseStates(json);

        _logger.LogInformation(
            "Received {Count} aircraft from OpenSky", aircraft.Count);

        await _hubContext.Clients.All.SendAsync(
            "ReceiveFlightData", aircraft, cancellationToken);
    }

    private static List<Aircraft> ParseStates(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (!root.TryGetProperty("states", out var states)
            || states.ValueKind == JsonValueKind.Null)
            return [];

        var result = new List<Aircraft>();

        foreach (var state in states.EnumerateArray())
        {
            if (state[5].ValueKind == JsonValueKind.Null ||
                state[6].ValueKind == JsonValueKind.Null)
                continue;

            result.Add(new Aircraft
            {
                Icao24             = state[0].GetString() ?? string.Empty,
                Callsign           = state[1].GetString()?.Trim(),
                OriginCountry      = state[2].GetString(),
                Longitude          = state[5].GetDouble(),
                Latitude           = state[6].GetDouble(),
                BarometricAltitude = state[7].ValueKind != JsonValueKind.Null
                    ? state[7].GetDouble() : null,
                OnGround           = state[8].GetBoolean(),
                Velocity           = state[9].ValueKind != JsonValueKind.Null
                    ? state[9].GetDouble() : null,
                Heading            = state[10].ValueKind != JsonValueKind.Null
                    ? state[10].GetDouble() : null,
                VerticalRate       = state[11].ValueKind != JsonValueKind.Null
                    ? state[11].GetDouble() : null,
                LastContact        = state[4].GetInt64()
            });
        }

        return result;
    }
}