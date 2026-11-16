using System.Text.Json.Serialization;

namespace AltusIQ.Api.Models;

public class Aircraft
{
    public string Icao24 { get; set; } = string.Empty;
    public string? Callsign { get; set; }
    public string? OriginCountry { get; set; }
    public double? Longitude { get; set; }
    public double? Latitude { get; set; }
    public double? BarometricAltitude { get; set; }
    public bool OnGround { get; set; }
    public double? Velocity { get; set; }
    public double? Heading { get; set; }
    public double? VerticalRate { get; set; }
    public long LastContact { get; set; }

    /// <summary>
    /// Transponder code, OpenSky state index 14. Kept off the SignalR wire on
    /// purpose: it feeds the emergency-squawk detector only, and the frontend
    /// Aircraft type must not change. See ADR-018.
    /// </summary>
    [JsonIgnore]
    public string? Squawk { get; set; }
}
