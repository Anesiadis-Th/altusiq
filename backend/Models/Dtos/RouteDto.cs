namespace AltusIQ.Api.Models.Dtos;

public record RouteAirportDto(
    string? IcaoCode,
    string? IataCode,
    string? Name,
    string? Municipality,
    string? CountryName);

public record FlightRouteDto(
    string Callsign,
    string? AirlineName,
    RouteAirportDto Origin,
    RouteAirportDto Destination);
