using AltusIQ.Api.Models;

namespace AltusIQ.Api.Investigation;

/// <summary>Read and investigation-write seam for the consumer side; the detector has its own writer.</summary>
public interface IAnomalyEventStore
{
    Task<AnomalyEvent?> GetAsync(Guid id, CancellationToken ct);
    Task SaveInvestigationAsync(Guid id, InvestigationResult result, CancellationToken ct);

    /// <summary>
    /// Events created before <paramref name="createdBefore"/> that are still open or
    /// still pending investigation. Called once at startup, because the detector
    /// state and the queue that would have finished them were in the previous
    /// process's memory.
    /// </summary>
    Task<IReadOnlyList<AnomalyEvent>> GetUnfinishedAsync(DateTime createdBefore, CancellationToken ct);

    /// <summary>Closes an event that no running process tracks. Does nothing if it is already closed.</summary>
    Task CloseOrphanAsync(Guid id, DateTime closedAt, CancellationToken ct);
}
