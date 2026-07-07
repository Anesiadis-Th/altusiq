using AltusIQ.Api.Models.Dtos;

namespace AltusIQ.Api.Services;

public interface IRouteLookupService
{
    Task<FlightRouteDto?> GetRouteAsync(string callsign, CancellationToken cancellationToken = default);
}
