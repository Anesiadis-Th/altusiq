export interface AirportTraffic {
  icao: string;
  departures: number;
  arrivals: number;
  total: number;
}

export interface Route {
  departure: string;
  arrival: string;
  count: number;
}

export interface FlightsPerDay {
  date: string;
  count: number;
}

export interface FlightsPerHour {
  hour: number;
  count: number;
}

export interface AltitudeBand {
  label: string;
  count: number;
}

export interface Analytics {
  range_days: number;
  from: string;
  to: string;
  total_flights: number;
  enriched_flights: number;
  busiest_airports: AirportTraffic[];
  top_routes: Route[];
  flights_per_day: FlightsPerDay[];
  flights_per_hour: FlightsPerHour[];
  altitude_bands: AltitudeBand[];
}
