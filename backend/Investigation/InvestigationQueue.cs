using System.Threading.Channels;

namespace AltusIQ.Api.Investigation;

/// <summary>
/// The hand-off between the poll path and the investigator. The detector
/// writes an id and moves on; InvestigationService reads at its own pace.
/// </summary>
public interface IInvestigationQueue
{
    void Enqueue(Guid eventId);
    ChannelReader<Guid> Reader { get; }
}

public sealed class InvestigationQueue : IInvestigationQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(Guid eventId) => _channel.Writer.TryWrite(eventId);

    public ChannelReader<Guid> Reader => _channel.Reader;
}
