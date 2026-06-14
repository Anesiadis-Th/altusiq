namespace AltusIQ.Api.Models.Dtos;

public record FlightTrackDto(
    Guid Id,
    string Icao24,
    string? Callsign,
    string? OriginCountry,
    DateTime OpenedAt,
    DateTime? ClosedAt,
    IReadOnlyList<TrackPointDto> TrackPoints
);

public record TrackPointDto(
    long Timestamp,
    double Longitude,
    double Latitude,
    double? Altitude,
    double? Heading,
    double? Velocity
);