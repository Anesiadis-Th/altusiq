import airports from "@/data/airports.json";

const byIcao = new Map<string, { iata: string; name: string }>(
  airports.map((a) => [a.icao, { iata: a.iata, name: a.name }]),
);

/**
 * Analytics data is keyed by ICAO (4-letter, from OpenSky). The map labels
 * airports by IATA. These helpers translate ICAO → IATA/name for display,
 * falling back to the raw ICAO for airports outside our static set.
 */
export function airportLabel(icao: string): string {
  return byIcao.get(icao)?.iata ?? icao;
}

export function airportName(icao: string): string | null {
  return byIcao.get(icao)?.name ?? null;
}
