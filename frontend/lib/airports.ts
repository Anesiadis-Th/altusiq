import codes from "@/data/airportCodes.json";

// ICAO to [IATA, name], from OurAirports. Only imported by the lazy analytics
// chunk, so it stays out of the initial map bundle.
const table = codes as Record<string, string[]>;

// Analytics is keyed by ICAO, the UI shows IATA. Airfields with no IATA code
// keep their ICAO.
export function airportLabel(icao: string): string {
  return table[icao]?.[0] ?? icao;
}

export function airportName(icao: string): string | null {
  return table[icao]?.[1] ?? null;
}
