using AltusIQ.Api.Models.Dtos;

namespace AltusIQ.Api.Services;

/// <summary>
/// Read seam over the ingestion service's in-memory trails, so the detector and
/// the investigation tools can be tested without running segmentation.
/// </summary>
public interface IActiveTrackSource
{
    Task<FlightTrackDto?> GetActiveTrackAsync(string icao24, CancellationToken ct);
}
