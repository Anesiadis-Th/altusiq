using AltusIQ.Api.Data;
using AltusIQ.Api.Models;

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
}
