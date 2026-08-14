using AltusIQ.Api.Models;

namespace AltusIQ.Api.Services;

/// <summary>
/// Persistence seam for completed flights. Keeps FlightIngestionService about
/// segmentation only, so the close/open boundary is testable without a database.
/// </summary>
public interface IFlightWriter
{
    Task WriteAsync(IReadOnlyList<Flight> flights, CancellationToken ct);
}
