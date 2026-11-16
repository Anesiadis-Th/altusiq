using AltusIQ.Api.Models;

namespace AltusIQ.Api.Services;

/// <summary>
/// Persistence seam for the squawk detector. Upsert, because the event id is
/// deterministic and a retried write must land on the same row.
/// </summary>
public interface IAnomalyEventWriter
{
    Task UpsertAsync(AnomalyEvent anomalyEvent, CancellationToken ct);
}
