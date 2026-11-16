using AltusIQ.Api.Investigation;
using AltusIQ.Api.Models;

namespace AltusIQ.Api.Background;

/// <summary>
/// Drains the investigation queue one event at a time. Lives entirely off the
/// poll path: the detector enqueues an id and the poll moves on, so a slow or
/// failing provider can never delay a broadcast.
/// </summary>
public class InvestigationService : BackgroundService
{
    private readonly IInvestigationQueue _queue;
    private readonly IAnomalyEventStore _store;
    private readonly InvestigationAgent _agent;
    private readonly IInvestigationToolSource _tools;
    private readonly ILogger<InvestigationService> _logger;

    public InvestigationService(
        IInvestigationQueue queue,
        IAnomalyEventStore store,
        InvestigationAgent agent,
        IInvestigationToolSource tools,
        ILogger<InvestigationService> logger)
    {
        _queue = queue;
        _store = store;
        _agent = agent;
        _tools = tools;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Investigation service started, concurrency 1");

        try
        {
            await foreach (var id in _queue.Reader.ReadAllAsync(stoppingToken))
                await ProcessAsync(id, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        _logger.LogInformation("Investigation service stopped");
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
