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
    /// ADS-B emitter category: 2 light, 3 small, 4 large, 5 high-vortex large,
    /// 6 heavy, 7 high performance, 8 rotorcraft, 9 glider. 0 and 1 mean the
    /// transponder reported none; null means the field was absent.
    /// </summary>
    public int? Category { get; set; }
}