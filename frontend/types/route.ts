export interface RouteAirport {
  icao_code: string | null;
  iata_code: string | null;
  name: string | null;
  municipality: string | null;
  country_name: string | null;
}

export interface FlightRoute {
  callsign: string;
  airline_name: string | null;
  origin: RouteAirport;
  destination: RouteAirport;
}
