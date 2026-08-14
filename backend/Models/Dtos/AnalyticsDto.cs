namespace AltusIQ.Api.Models.Dtos;

public record AnalyticsResponseDto(
    int RangeDays,
    DateTime From,
    DateTime To,
    int TotalFlights,
    int EnrichedFlights,
    int DistinctAirports,
    int DistinctRoutes,
    IReadOnlyList<AirportTrafficDto> BusiestAirports,
    IReadOnlyList<RouteDto> TopRoutes,
    IReadOnlyList<FlightsPerDayDto> FlightsPerDay,
    IReadOnlyList<FlightsPerHourDto> FlightsPerHour
);

public record AirportTrafficDto(string Icao, int Departures, int Arrivals, int Total);

public record RouteDto(string Departure, string Arrival, int Count);

public record FlightsPerDayDto(DateOnly Date, int Count);

public record FlightsPerHourDto(int Hour, int Count);
