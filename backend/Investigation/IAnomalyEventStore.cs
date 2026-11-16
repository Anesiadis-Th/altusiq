using AltusIQ.Api.Models;

namespace AltusIQ.Api.Investigation;

/// <summary>Read and investigation-write seam for the consumer side; the detector has its own writer.</summary>
public interface IAnomalyEventStore
{
    Task<AnomalyEvent?> GetAsync(Guid id, CancellationToken ct);
    Task SaveInvestigationAsync(Guid id, InvestigationResult result, CancellationToken ct);
}
