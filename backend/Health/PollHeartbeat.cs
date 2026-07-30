namespace AltusIQ.Api.Health;

public class PollHeartbeat
{
    private long _lastSuccessTicks;
    private long _successCount;

    public PollHeartbeat()
    {
        var now = DateTimeOffset.UtcNow;
        StartedAtUtc = now;
        _lastSuccessTicks = now.UtcTicks;
    }

    public DateTimeOffset StartedAtUtc { get; }

    public DateTimeOffset LastSuccessUtc =>
        new(Interlocked.Read(ref _lastSuccessTicks), TimeSpan.Zero);

    public long SuccessCount => Interlocked.Read(ref _successCount);

    public bool HasPolled => SuccessCount > 0;

    public void RecordSuccess()
    {
        Interlocked.Exchange(
            ref _lastSuccessTicks, DateTimeOffset.UtcNow.UtcTicks);
        Interlocked.Increment(ref _successCount);
    }
}
