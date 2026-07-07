using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AltusIQ.Api.Models.Dtos;
using Microsoft.Extensions.Caching.Memory;

namespace AltusIQ.Api.Services;

public partial class RouteLookupService : IRouteLookupService
{
    private static readonly TimeSpan PositiveTtl = TimeSpan.FromHours(24);
    private static readonly TimeSpan NegativeTtl = TimeSpan.FromHours(6);

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;
    private readonly ILogger<RouteLookupService> _logger;

    public RouteLookupService(
        HttpClient httpClient,
        IMemoryCache cache,
        IConfiguration config,
        ILogger<RouteLookupService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _config = config;
        _logger = logger;
    }

    public async Task<FlightRouteDto?> GetRouteAsync(
        string callsign, CancellationToken cancellationToken = default)
    {
        var key = callsign.Trim().ToUpperInvariant();

        if (!CallsignPattern().IsMatch(key))
            return null;

        var cacheKey = $"route:{key}";
        if (_cache.TryGetValue<FlightRouteDto?>(cacheKey, out var cached))
            return cached;

        var baseUrl = _config["Adsbdb:ApiBaseUrl"]
            ?? "https://api.adsbdb.com";

        try
        {
            var response = await _httpClient.GetAsync(
                $"{baseUrl}/v0/callsign/{key}", cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                _cache.Set<FlightRouteDto?>(cacheKey, null, NegativeTtl);
                return null;
            }

            response.EnsureSuccessStatusCode();

            var payload = await response.Content
                .ReadFromJsonAsync<AdsbdbWrapper>(cancellationToken);

            var route = payload?.Response?.Flightroute;
            if (route?.Origin is null || route.Destination is null)
            {
                _cache.Set<FlightRouteDto?>(cacheKey, null, NegativeTtl);
                return null;
            }

            var dto = new FlightRouteDto(
                key,
                route.Airline?.Name,
                MapAirport(route.Origin),
                MapAirport(route.Destination));

            _cache.Set<FlightRouteDto?>(cacheKey, dto, PositiveTtl);
            return dto;
        }
        catch (Exception ex) when (
            ex is HttpRequestException or TaskCanceledException
            && !cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning(ex,
                "Route lookup failed for callsign {Callsign}", key);
            return null;
        }
    }

    private static RouteAirportDto MapAirport(AdsbdbAirport airport) =>
        new(airport.IcaoCode,
            airport.IataCode,
            airport.Name,
            airport.Municipality,
            airport.CountryName,
            airport.Latitude,
            airport.Longitude);

    [GeneratedRegex("^[A-Z0-9]{2,8}$")]
    private static partial Regex CallsignPattern();

    private sealed record AdsbdbWrapper(
        [property: JsonPropertyName("response")] AdsbdbResponse? Response);

    private sealed record AdsbdbResponse(
        [property: JsonPropertyName("flightroute")] AdsbdbFlightRoute? Flightroute);

    private sealed record AdsbdbFlightRoute(
        [property: JsonPropertyName("airline")] AdsbdbAirline? Airline,
        [property: JsonPropertyName("origin")] AdsbdbAirport? Origin,
        [property: JsonPropertyName("destination")] AdsbdbAirport? Destination);

    private sealed record AdsbdbAirline(
        [property: JsonPropertyName("name")] string? Name);

    private sealed record AdsbdbAirport(
        [property: JsonPropertyName("icao_code")] string? IcaoCode,
        [property: JsonPropertyName("iata_code")] string? IataCode,
        [property: JsonPropertyName("name")] string? Name,
        [property: JsonPropertyName("municipality")] string? Municipality,
        [property: JsonPropertyName("country_name")] string? CountryName,
        [property: JsonPropertyName("latitude")] double? Latitude,
        [property: JsonPropertyName("longitude")] double? Longitude);
}
