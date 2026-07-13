import { Aircraft } from "@/types/aircraft";
import airlineCodes from "@/data/airlineCodes.json";

// IATA airline code → ICAO callsign prefixes (one-to-many: shared flight
// numbers across AOCs, e.g. LH → DLH + GEC). Built from the OpenFlights
// airline database (ODbL), active carriers with both codes.
const AIRLINE_CODES: Record<string, string[]> = airlineCodes;

// IATA flight number as typed on a boarding pass: 2-char airline code
// (letters or digits, e.g. SK, U2, W6) + numeric part, possibly partial.
const IATA_FLIGHT = /^([A-Z0-9]{2})(\d[\dA-Z]*)?$/;
const HEX_QUERY = /^[0-9A-F]{2,6}$/;

const MATCH_EXACT = 0;
const MATCH_PREFIX = 1;
const MATCH_SUBSTRING = 2;
const MATCH_HEX = 3;

export interface SearchResult {
  aircraft: Aircraft;
  label: string;
}

function candidatePrefixes(query: string): string[] {
  const prefixes = [query];
  const match = IATA_FLIGHT.exec(query);
  if (!match) return prefixes;
  const icaos = AIRLINE_CODES[match[1]];
  if (!icaos) return prefixes;
  const flightNumber = match[2] ?? "";
  // Callsigns usually drop leading zeros: SK0034 flies as SAS34.
  const stripped = flightNumber.replace(/^0+/, "");
  for (const icao of icaos) {
    prefixes.push(icao + flightNumber);
    if (stripped !== flightNumber) prefixes.push(icao + stripped);
  }
  return prefixes;
}

export function searchAircraft(
  aircraft: Aircraft[],
  rawQuery: string,
  limit = 8,
): SearchResult[] {
  const query = rawQuery.toUpperCase().replace(/\s+/g, "");
  if (query.length < 2) return [];

  const prefixes = candidatePrefixes(query);
  const hexQuery = HEX_QUERY.test(query) ? query.toLowerCase() : null;

  const scored: { score: number; label: string; aircraft: Aircraft }[] = [];
  for (const a of aircraft) {
    // Grounded aircraft are not rendered on the map, so searching them
    // would fly the camera to an invisible target.
    if (a.on_ground) continue;

    const callsign = a.callsign?.trim().toUpperCase() ?? "";
    let score = Number.POSITIVE_INFINITY;
    if (callsign) {
      for (const prefix of prefixes) {
        if (callsign === prefix) {
          score = MATCH_EXACT;
          break;
        }
        if (callsign.startsWith(prefix)) score = MATCH_PREFIX;
      }
      if (score > MATCH_SUBSTRING && callsign.includes(query)) {
        score = MATCH_SUBSTRING;
      }
    }
    if (score > MATCH_HEX && hexQuery && a.icao24.startsWith(hexQuery)) {
      score = MATCH_HEX;
    }
    if (Number.isFinite(score)) {
      scored.push({
        score,
        label: callsign || a.icao24.toUpperCase(),
        aircraft: a,
      });
    }
  }

  scored.sort((x, y) => x.score - y.score || x.label.localeCompare(y.label));
  return scored
    .slice(0, limit)
    .map((s) => ({ aircraft: s.aircraft, label: s.label }));
}
