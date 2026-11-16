using AltusIQ.Api.Investigation;
using AltusIQ.Api.Models;

namespace AltusIQ.Api.Tests.Fakes;

internal sealed class FakeAnomalyEventStore : IAnomalyEventStore
{
    public Dictionary<Guid, AnomalyEvent> Rows { get; } = new();
    public List<(Guid Id, InvestigationResult Result)> Saved { get; } = [];

    public Task<AnomalyEvent?> GetAsync(Guid id, CancellationToken ct) =>
        Task.FromResult(Rows.GetValueOrDefault(id));

    public Task SaveInvestigationAsync(Guid id, InvestigationResult result, CancellationToken ct)
    {
        Saved.Add((id, result));
        if (Rows.TryGetValue(id, out var evt))
            result.ApplyTo(evt);
        return Task.CompletedTask;
    }
}
