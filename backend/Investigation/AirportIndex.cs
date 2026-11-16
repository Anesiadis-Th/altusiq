using System.Reflection;
using System.Text.Json;

namespace AltusIQ.Api.Investigation;

/// <summary>
/// Backend copy of frontend/public/airports.geojson (OurAirports, public
/// domain), embedded at build time. 4,034 points; a brute-force haversine
/// over them is well under a millisecond and needs no index.
/// </summary>
public sealed class AirportIndex
{
    public sealed record Airport(string Icao, string? Iata, string? Name, double Latitude, double Longitude);
    public sealed record Nearby(Airport Airport, double DistanceKm, double BearingDeg);

    private readonly IReadOnlyList<Airport> _airports;

    public AirportIndex(IReadOnlyList<Airport> airports)
    {
        _airports = airports;
    }

    public int Count => _airports.Count;

    public static AirportIndex LoadEmbedded()
    {
        var assembly = typeof(AirportIndex).Assembly;
        var name = assembly.GetManifestResourceNames()
            .Single(n => n.EndsWith("airports.geojson", StringComparison.Ordinal));

        using var stream = assembly.GetManifestResourceStream(name)!;
        return Parse(stream);
    }

    public static AirportIndex Parse(Stream geojson)
    {
        using var doc = JsonDocument.Parse(geojson);
        var airports = new List<Airport>();

        foreach (var feature in doc.RootElement.GetProperty("features").EnumerateArray())
        {
            var coords = feature.GetProperty("geometry").GetProperty("coordinates");
            var props = feature.GetProperty("properties");

            var icao = props.TryGetProperty("icao", out var i) && i.ValueKind == JsonValueKind.String ? i.GetString() : null;
            if (string.IsNullOrWhiteSpace(icao))
                continue;

            airports.Add(new Airport(
                icao,
                props.TryGetProperty("iata", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null,
                props.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null,
                coords[1].GetDouble(),
                coords[0].GetDouble()));
        }

        return new AirportIndex(airports);
    }

    public IReadOnlyList<Nearby> Nearest(double latitude, double longitude, int limit)
    {
        limit = Math.Clamp(limit, 1, 10);

        return _airports
            .Select(a => new Nearby(a, HaversineKm(latitude, longitude, a.Latitude, a.Longitude), Bearing(latitude, longitude, a.Latitude, a.Longitude)))
            .OrderBy(n => n.DistanceKm)
            .Take(limit)
            .ToList();
    }

    public static double HaversineKm(double lat1, double lon1, double lat2, double lon2)
    {
        const double r = 6371.0;
        var dLat = Rad(lat2 - lat1);
        var dLon = Rad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
            + Math.Cos(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * r * Math.Asin(Math.Sqrt(a));
    }

    private static double Bearing(double lat1, double lon1, double lat2, double lon2)
    {
        var y = Math.Sin(Rad(lon2 - lon1)) * Math.Cos(Rad(lat2));
        var x = Math.Cos(Rad(lat1)) * Math.Sin(Rad(lat2))
            - Math.Sin(Rad(lat1)) * Math.Cos(Rad(lat2)) * Math.Cos(Rad(lon2 - lon1));
        return (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
    }

    private static double Rad(double deg) => deg * Math.PI / 180;
}
