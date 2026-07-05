import codes from "@/data/airportCodes.json";

// Comprehensive ICAO → [IATA, name] table (OurAirports, public domain), covering
// every airport that has both an ICAO and an IATA code. Lives in the lazily
// loaded analytics chunk, so it never touches the initial map bundle.
const table = codes as Record<string, string[]>;

/**
 * Analytics data is keyed by ICAO (4-letter, from OpenSky). The UI prefers IATA
 * (3-letter). Falls back to the raw ICAO for airfields that have no IATA code.
 */
export function airportLabel(icao: string): string {
  return table[icao]?.[0] ?? icao;
}

export function airportName(icao: string): string | null {
  return table[icao]?.[1] ?? null;
}
