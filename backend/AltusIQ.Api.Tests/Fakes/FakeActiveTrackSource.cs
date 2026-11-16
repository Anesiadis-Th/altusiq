using AltusIQ.Api.Models.Dtos;
using AltusIQ.Api.Services;

namespace AltusIQ.Api.Tests.Fakes;

internal sealed class FakeActiveTrackSource : IActiveTrackSource
{
    private readonly Dictionary<string, FlightTrackDto> _tracks = new();

    public void Set(string icao24, FlightTrackDto track) => _tracks[icao24.ToLowerInvariant()] = track;
    public void Remove(string icao24) => _tracks.Remove(icao24.ToLowerInvariant());

    public Task<FlightTrackDto?> GetActiveTrackAsync(string icao24, CancellationToken ct) =>
        Task.FromResult(_tracks.GetValueOrDefault(icao24.ToLowerInvariant()));

    public static FlightTrackDto Track(string icao24, int points, double lon = 10, double lat = 60)
    {
        var start = 1_754_000_000L;
        return new FlightTrackDto(
            Guid.NewGuid(),
            icao24,
            "SAS123",
            "Sweden",
            DateTimeOffset.FromUnixTimeSeconds(start).UtcDateTime,
            null,
            Enumerable.Range(0, points)
                .Select(i => new TrackPointDto(start + i * 120, lon + i * 0.01, lat, 10_000, 180, 220))
                .ToList());
    }
}
