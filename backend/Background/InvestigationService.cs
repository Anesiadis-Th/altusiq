using AltusIQ.Api.Investigation;
using AltusIQ.Api.Models;

namespace AltusIQ.Api.Background;

/// <summary>
/// Drains the investigation queue one event at a time. Lives entirely off the
/// poll path: the detector enqueues an id and the poll moves on, so a slow or
/// failing provider can never delay a broadcast.
///
/// At startup it runs RecoverAsync before reading the queue. The detector state
/// and the queue are held in memory, so after a restart nothing else would close
/// events left open or investigate events left pending.
/// </summary>
public class InvestigationService : BackgroundService
{
    private readonly IInvestigationQueue _queue;
    private readonly IAnomalyEventStore _store;
    private readonly InvestigationAgent _agent;
    private readonly IInvestigationToolSource _tools;
    private readonly ILogger<InvestigationService> _logger;

    // Events created before this time belong to an earlier process. Events this
    // process confirms are created later, so RecoverAsync never touches them.
    private readonly DateTime _startedAt;

    public InvestigationService(
        IInvestigationQueue queue,
        IAnomalyEventStore store,
        InvestigationAgent agent,
        IInvestigationToolSource tools,
        TimeProvider clock,
        ILogger<InvestigationService> logger)
    {
        _queue = queue;
        _store = store;
        _agent = agent;
        _tools = tools;
        _logger = logger;
        _startedAt = clock.GetUtcNow().UtcDateTime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Investigation service started, concurrency 1");

        try
        {
            await RecoverAsync(stoppingToken);

            await foreach (var id in _queue.Reader.ReadAllAsync(stoppingToken))
                await ProcessAsync(id, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        _logger.LogInformation("Investigation service stopped");
    }

    /// <summary>
    /// Closes events left open by an earlier process and re-queues every event still
    /// pending investigation. Returns the number queued. Errors are logged, not
    /// thrown, so an unavailable database at startup does not stop the consumer.
    /// </summary>
    public async Task<int> RecoverAsync(CancellationToken ct)
    {
        IReadOnlyList<AnomalyEvent> unfinished;
        try
        {
            unfinished = await _store.GetUnfinishedAsync(_startedAt, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Startup sweep could not read unfinished events; skipping it");
            return 0;
        }

        var closed = 0;
        var queued = 0;

        foreach (var evt in unfinished)
        {
            if (evt.Status == AnomalyEventStatus.Open)
            {
                // ClosedAt is the last fix on the code, the same rule the detector
                // uses for an aircraft that goes unseen.
                try
                {
                    await _store.CloseOrphanAsync(evt.Id, evt.LastSeenAt, ct);
                    closed++;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "Startup sweep could not close orphaned event {EventId}", evt.Id);
                }
            }

            if (evt.InvestigationStatus == InvestigationStatus.Pending)
            {
                _queue.Enqueue(evt.Id);
                queued++;
            }
        }

        if (unfinished.Count > 0)
            _logger.LogInformation(
                "Startup sweep: closed {Closed} orphaned event(s), re-queued {Queued} pending investigation(s)",
                closed, queued);

        return queued;
    }

    /// <summary>One queued id, start to finish. Never throws past a log line.</summary>
    public async Task ProcessAsync(Guid id, CancellationToken ct)
    {
        try
        {
            var evt = await _store.GetAsync(id, ct);
            if (evt is null)
            {
                _logger.LogWarning("Queued event {EventId} not found, skipping", id);
                return;
            }

            if (evt.InvestigationStatus != InvestigationStatus.Pending)
                return;

            var result = await _agent.InvestigateAsync(evt, _tools, ct);
            await _store.SaveInvestigationAsync(id, result, ct);

            _logger.LogInformation(
                "Investigation for {EventId}: {Status} via {Provider}/{Model}, grounded={Grounded}, {Calls} model call(s), tools {Tools}",
                id, result.Status, result.Provider ?? "none", result.Model ?? "none", result.Grounded,
                result.ModelCalls, string.Join(",", result.ToolsCalled));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Investigation for {EventId} failed", id);
        }
    }
}
