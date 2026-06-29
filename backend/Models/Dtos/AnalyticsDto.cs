namespace AltusIQ.Api.Models.Dtos;

public record AnalyticsResponseDto(
    int RangeDays,
    DateTime From,
    DateTime To,
    int TotalFlights,
    int EnrichedFlights,
    IReadOnlyList<AirportTrafficDto> BusiestAirports,
    IReadOnlyList<RouteDto> TopRoutes,
    IReadOnlyList<FlightsPerDayDto> FlightsPerDay,
    IReadOnlyList<FlightsPerHourDto> FlightsPerHour,
    IReadOnlyList<AltitudeBandDto> AltitudeBands
);

public record AirportTrafficDto(string Icao, int Departures, int Arrivals, int Total);

public record RouteDto(string Departure, string Arrival, int Count);

public record FlightsPerDayDto(DateOnly Date, int Count);

public record FlightsPerHourDto(int Hour, int Count);

public record AltitudeBandDto(string Label, int Count);
