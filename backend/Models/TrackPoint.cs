namespace AltusIQ.Api.Models;

public record TrackPoint(
    long Timestamp,
    double Longitude,
    double Latitude,
    double? Altitude,
    double? Heading,
    double? Velocity
);