// ICAO to [IATA, name], from OurAirports. Fetched from public/ and never
// imported, same rule as airports.geojson. See ADR-013.
export type AirportCodes = Record<string, string[]>;

export const AIRPORT_CODES_URL = "/airportCodes.json";

export function airportLabel(
  codes: AirportCodes | undefined,
  icao: string,
): string {
  return codes?.[icao]?.[0] ?? icao;
}

export function airportName(
  codes: AirportCodes | undefined,
  icao: string,
): string | null {
  return codes?.[icao]?.[1] ?? null;
}
