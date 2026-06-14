using NetTopologySuite.Geometries;

namespace AltusIQ.Api.Models;

public class Flight
{
    public Guid Id { get; set; }
    public required string Icao24 { get; set; }
    public string? Callsign { get; set; }
    public string? OriginCountry { get; set; }
    public DateTime OpenedAt { get; set; }
    public DateTime? ClosedAt { get; set; }
    public Point? LastPosition { get; set; }
    public double? LastAltitude { get; set; }
    public List<TrackPoint> TrackPoints { get; set; } = [];
}