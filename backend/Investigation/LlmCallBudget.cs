using AltusIQ.Api.Models;
using Microsoft.Extensions.Options;

namespace AltusIQ.Api.Investigation;

/// <summary>
/// Counts model calls per UTC day across every provider. Free tiers are the
/// whole cost model here, so the count is the budget, not a metric.
/// </summary>
public sealed class LlmCallBudget
{
    private readonly LlmSettings _settings;
    private readonly TimeProvider _clock;
    private readonly object _gate = new();

    private DateOnly _day;
    private int _calls;
    private DateTimeOffset? _lastCallAt;

    public LlmCallBudget(IOptions<LlmSettings> settings, TimeProvider clock)
    {
        _settings = settings.Value;
        _clock = clock;
        _day = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
    }

    public int CallsToday
    {
        get { lock (_gate) { RollDay(); return _calls; } }
    }

    /// <summary>Reserves one call if the day's budget allows it.</summary>
    public bool TryReserve() => TryReserve(out _);

    /// <summary>
    /// Reserves one call and returns how long to wait before making it. The wait is
    /// measured from the previous call: the first call has none, and consecutive
    /// calls are MinSecondsBetweenCalls apart.
    /// </summary>
    public bool TryReserve(out TimeSpan wait)
    {
        lock (_gate)
        {
            wait = TimeSpan.Zero;
            RollDay();
            if (_calls >= _settings.MaxCallsPerDay)
                return false;

            var now = _clock.GetUtcNow();
            if (_lastCallAt is { } last && _settings.MinSecondsBetweenCalls > 0)
            {
                var next = last.AddSeconds(_settings.MinSecondsBetweenCalls);
                if (next > now)
                    wait = next - now;
            }

            _calls++;
            // Record when the call will actually be made, which is after the wait.
            _lastCallAt = now + wait;
            return true;
        }
    }

    /// <summary>How long to wait before the next call honours MinSecondsBetweenCalls.</summary>
    public TimeSpan TimeUntilNextAllowed()
    {
        lock (_gate)
        {
            if (_lastCallAt is null || _settings.MinSecondsBetweenCalls <= 0)
                return TimeSpan.Zero;

            var next = _lastCallAt.Value.AddSeconds(_settings.MinSecondsBetweenCalls);
            var wait = next - _clock.GetUtcNow();
            return wait > TimeSpan.Zero ? wait : TimeSpan.Zero;
        }
    }

    private void RollDay()
    {
        var today = DateOnly.FromDateTime(_clock.GetUtcNow().UtcDateTime);
        if (today == _day)
            return;

        _day = today;
        _calls = 0;
    }
}
