using AltusIQ.Api.Data;
using AltusIQ.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AltusIQ.Api.Investigation;

public sealed class EfAnomalyEventStore : IAnomalyEventStore
{
    private readonly IServiceScopeFactory _scopeFactory;

    public EfAnomalyEventStore(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<AnomalyEvent?> GetAsync(Guid id, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AltusIqDbContext>();
        return await db.AnomalyEvents.FindAsync([id], ct);
    }

    public async Task SaveInvestigationAsync(Guid id, InvestigationResult result, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AltusIqDbContext>();

        var evt = await db.AnomalyEvents.FindAsync([id], ct);
        if (evt is null)
            return;

        result.ApplyTo(evt);
        await db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<AnomalyEvent>> GetUnfinishedAsync(DateTime createdBefore, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AltusIqDbContext>();

        return await db.AnomalyEvents
            .AsNoTracking()
            .Where(e => e.CreatedAt < createdBefore
                && (e.Status == AnomalyEventStatus.Open || e.InvestigationStatus == InvestigationStatus.Pending))
            .OrderBy(e => e.ConfirmedAt)
            .ToListAsync(ct);
    }

    public async Task CloseOrphanAsync(Guid id, DateTime closedAt, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AltusIqDbContext>();

        var evt = await db.AnomalyEvents.FindAsync([id], ct);
        if (evt is null || evt.Status != AnomalyEventStatus.Open)
            return;

        evt.Status = AnomalyEventStatus.Closed;
        evt.ClosedAt = closedAt;
        await db.SaveChangesAsync(ct);
    }
}
