using AltusIQ.Api.Investigation;
using AltusIQ.Api.Models;

namespace AltusIQ.Api.Tests.Fakes;

internal sealed class FakeAnomalyEventStore : IAnomalyEventStore
{
    public Dictionary<Guid, AnomalyEvent> Rows { get; } = new();
    public List<(Guid Id, InvestigationResult Result)> Saved { get; } = [];
    public List<(Guid Id, DateTime ClosedAt)> Closed { get; } = [];
    public bool FailReads { get; set; }

    public Task<AnomalyEvent?> GetAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Rows.GetValueOrDefault(id));

    public Task SaveInvestigationAsync(Guid id, InvestigationResult result, CancellationToken ct)
    {
        Saved.Add((id, result));
        if (Rows.TryGetValue(id, out var evt))
            result.ApplyTo(evt);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AnomalyEvent>> GetUnfinishedAsync(DateTime createdBefore, CancellationToken ct)
    {
        if (FailReads)
            throw new InvalidOperationException("database unavailable");

        IReadOnlyList<AnomalyEvent> rows = Rows.Values
            .Where(e => e.CreatedAt < createdBefore
                && (e.Status == AnomalyEventStatus.Open || e.InvestigationStatus == InvestigationStatus.Pending))
            .OrderBy(e => e.ConfirmedAt)
            .ToList();
        return Task.FromResult(rows);
    }

    public Task CloseOrphanAsync(Guid id, DateTime closedAt, CancellationToken ct)
    {
        if (Rows.TryGetValue(id, out var evt) && evt.Status == AnomalyEventStatus.Open)
        {
            evt.Status = AnomalyEventStatus.Closed;
            evt.ClosedAt = closedAt;
            Closed.Add((id, closedAt));
        }
        return Task.CompletedTask;
    }
}
