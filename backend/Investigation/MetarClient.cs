using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace AltusIQ.Api.Investigation;

/// <summary>
/// aviationweather.gov METAR, no key needed, 100 requests a minute. Cached ten
/// minutes per station, which is well under the issue interval anyway.
/// </summary>
public sealed class MetarClient
{
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(10);

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly ILogger<MetarClient> _logger;

    public MetarClient(HttpClient http, IMemoryCache cache, ILogger<MetarClient> logger)
    {
        _http = http;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>Compact JSON of the latest report, or a JSON error object. Never throws.</summary>
    public async Task<string> GetMetarJsonAsync(string icao, CancellationToken ct)
    {
        var station = icao.Trim().ToUpperInvariant();
        if (station.Length != 4 || !station.All(char.IsLetterOrDigit))
            return Error(station, "not a four-character station id");

        var key = $"metar:{station}";
        if (_cache.TryGetValue<string>(key, out var cached) && cached is not null)
            return cached;

        try
        {
            using var response = await _http.GetAsync(
                $"https://aviationweather.gov/api/data/metar?ids={station}&format=json", ct);

            if (!response.IsSuccessStatusCode)
                return Error(station, $"HTTP {(int)response.StatusCode}");

            var text = await response.Content.ReadAsStringAsync(ct);
            var compact = Compact(station, text);

            _cache.Set(key, compact, Ttl);
            return compact;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException
            && !ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "METAR fetch failed for {Station}", station);
            return Error(station, ex.GetType().Name);
        }
    }

    private static string Compact(string station, string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Array || root.GetArrayLength() == 0)
            return Error(station, "no report returned");

        var m = root[0];
        string? Str(string name) => m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        double? Num(string name) => m.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

        return JsonSerializer.Serialize(new
        {
            station,
            raw = Str("rawOb"),
            report_time = Str("reportTime"),
            temp_c = Num("temp"),
            dewpoint_c = Num("dewp"),
            wind_dir_deg = Num("wdir"),
            wind_kt = Num("wspd"),
            visibility = m.TryGetProperty("visib", out var vis) ? vis.ToString() : null,
            flight_category = Str("fltCat"),
            clouds = m.TryGetProperty("clouds", out var clouds) ? clouds.ToString() : null
        });
    }

    private static string Error(string station, string reason) =>
        JsonSerializer.Serialize(new { station, error = reason });
}
