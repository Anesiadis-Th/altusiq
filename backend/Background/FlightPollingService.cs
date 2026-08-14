using System.Net;
using System.Net.Http.Headers;
using AltusIQ.Api.Health;
using AltusIQ.Api.Hubs;
using AltusIQ.Api.Models;
using AltusIQ.Api.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace AltusIQ.Api.Background;

public class FlightPollingService : BackgroundService
{
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
        var parsed = OpenSkyStateParser.Parse(json);
        var aircraft = parsed.Aircraft;

        if (parsed.Skipped > 0)
            _logger.LogWarning(
                "Skipped {Skipped} unusable state rows, kept {Kept}",
                parsed.Skipped, aircraft.Count);

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

}