namespace AltusIQ.Api.Models.Dtos;

public record FlightSummaryDto(
    Guid Id,
    string Icao24,
    string? Callsign,
    string? OriginCountry,
    DateTime OpenedAt,
    DateTime? ClosedAt,
    double? LastLatitude,
    double? LastLongitude,
    string? DepartureAirport,
    string? ArrivalAirport
);