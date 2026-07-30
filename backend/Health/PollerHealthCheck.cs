using AltusIQ.Api.Models;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace AltusIQ.Api.Health;

public class PollerHealthCheck : IHealthCheck
{
    private const int StaleIntervalMultiplier = 3;

    private readonly PollHeartbeat _heartbeat;
    private readonly IngestionSettings _settings;

    public PollerHealthCheck(
        PollHeartbeat heartbeat,
        IOptions<IngestionSettings> settings)
    {
        _heartbeat = heartbeat;
        _settings = settings.Value;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var intervalSeconds = Math.Max(_settings.PollIntervalSeconds, 1);
        var thresholdSeconds = intervalSeconds * StaleIntervalMultiplier;
        var ageSeconds = Math.Round(
            (DateTimeOffset.UtcNow - _heartbeat.LastSuccessUtc).TotalSeconds);

        var data = new Dictionary<string, object>
        {
            ["last_successful_poll"] = _heartbeat.HasPolled
                ? _heartbeat.LastSuccessUtc.ToString("O")
                : "never",
            ["seconds_since"] = ageSeconds,
            ["threshold_seconds"] = thresholdSeconds,
            ["poll_interval_seconds"] = intervalSeconds,
            ["successful_polls"] = _heartbeat.SuccessCount,
            ["started_at"] = _heartbeat.StartedAtUtc.ToString("O")
        };

        if (ageSeconds <= thresholdSeconds)
        {
            return Task.FromResult(HealthCheckResult.Healthy(
                _heartbeat.HasPolled
                    ? $"Last successful OpenSky poll {ageSeconds}s ago"
                    : $"Starting up, no poll completed yet ({ageSeconds}s since start)",
                data));
        }

        return Task.FromResult(HealthCheckResult.Unhealthy(
            _heartbeat.HasPolled
                ? $"No successful OpenSky poll for {ageSeconds}s, over the {thresholdSeconds}s threshold"
                : $"No successful OpenSky poll since startup {ageSeconds}s ago",
            data: data));
    }
}
