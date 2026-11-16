using AltusIQ.Api.Data;
using AltusIQ.Api.Models;

namespace AltusIQ.Api.Services;

public class EfAnomalyEventWriter : IAnomalyEventWriter
{
    private readonly IServiceScopeFactory _scopeFactory;

    public EfAnomalyEventWriter(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task UpsertAsync(AnomalyEvent anomalyEvent, CancellationToken ct)
    {
        // Singleton detector, scoped DbContext: scope per write, as EfFlightWriter.
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AltusIqDbContext>();

        var existing = await db.AnomalyEvents.FindAsync([anomalyEvent.Id], ct);

        if (existing is null)
        {
            db.AnomalyEvents.Add(anomalyEvent);
        }
        else
        {
            // Only the detector-owned columns. The investigator writes its own
            // columns on the same row and a late close must not blank them.
            existing.Callsign = anomalyEvent.Callsign;
            existing.LastSeenAt = anomalyEvent.LastSeenAt;
            existing.ClosedAt = anomalyEvent.ClosedAt;
            existing.Status = anomalyEvent.Status;
            existing.LastLongitude = anomalyEvent.LastLongitude;
            existing.LastLatitude = anomalyEvent.LastLatitude;
            existing.LastAltitude = anomalyEvent.LastAltitude;
            existing.EvidenceJson = anomalyEvent.EvidenceJson;
        }

        await db.SaveChangesAsync(ct);
    }
}
