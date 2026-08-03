#!/usr/bin/env node
// Regenerates public/airports.geojson from the OurAirports dataset (public domain).
//
//   node scripts/build-airports.mjs
//
// Scope is airports with an IATA code AND scheduled service. That drops ~4,900
// IATA-coded grass strips and private fields, plus heliports and seaplane
// bases, which are noise on a flight tracker. The `tier` property drives
// zoom-based filtering in MapView so the world view is not a wall of dots.

import { writeFile, mkdir } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

const SOURCE = "https://davidmegginson.github.io/ourairports-data/airports.csv";
const OUT = resolve(
  dirname(fileURLToPath(import.meta.url)),
  "../public/airports.geojson",
);

const TIERS = { large_airport: 1, medium_airport: 2, small_airport: 3 };
const COORD_DECIMALS = 4;

// Fields can contain commas and escaped quotes, so the CSV needs real parsing.
function parseCsv(text) {
  const rows = [];
  let row = [];
  let field = "";
  let quoted = false;

  for (let i = 0; i < text.length; i++) {
    const char = text[i];
    if (quoted) {
      if (char !== '"') {
        field += char;
      } else if (text[i + 1] === '"') {
        field += '"';
        i++;
      } else {
        quoted = false;
      }
      continue;
    }
    if (char === '"') quoted = true;
    else if (char === ",") {
      row.push(field);
      field = "";
    } else if (char === "\n") {
      row.push(field);
      rows.push(row);
      row = [];
      field = "";
    } else if (char !== "\r") field += char;
  }
  if (field !== "" || row.length > 0) {
    row.push(field);
    rows.push(row);
  }
  return rows;
}

function round(value) {
  return Number(value.toFixed(COORD_DECIMALS));
}

const response = await fetch(SOURCE);
if (!response.ok) {
  throw new Error(`OurAirports fetch failed: ${response.status}`);
}

const rows = parseCsv(await response.text());
const header = rows.shift();
const col = Object.fromEntries(header.map((name, index) => [name, index]));

const airports = rows
  .filter((row) => row.length === header.length)
  .map((row) => ({
    iata: row[col.iata_code].trim(),
    icao: row[col.icao_code].trim(),
    name: (row[col.municipality].trim() || row[col.name].trim()),
    tier: TIERS[row[col.type]],
    lon: Number(row[col.longitude_deg]),
    lat: Number(row[col.latitude_deg]),
    scheduled: row[col.scheduled_service] === "yes",
  }))
  .filter(
    (a) =>
      a.scheduled &&
      a.iata !== "" &&
      a.tier !== undefined &&
      Number.isFinite(a.lon) &&
      Number.isFinite(a.lat),
  )
  // Stable ordering keeps regeneration diffs readable.
  .sort((a, b) => a.tier - b.tier || a.iata.localeCompare(b.iata));

if (airports.length === 0) {
  throw new Error("No airports matched — the upstream schema likely changed");
}

const collection = {
  type: "FeatureCollection",
  features: airports.map((a) => ({
    type: "Feature",
    geometry: { type: "Point", coordinates: [round(a.lon), round(a.lat)] },
    // icao is the join key to the backend (which is ICAO-only); a handful of
    // fields genuinely have no ICAO code, so the property is omitted there.
    properties: {
      iata: a.iata,
      ...(a.icao ? { icao: a.icao } : {}),
      name: a.name,
      tier: a.tier,
    },
  })),
};

await mkdir(dirname(OUT), { recursive: true });
await writeFile(OUT, JSON.stringify(collection));

const byTier = airports.reduce((acc, a) => {
  acc[a.tier] = (acc[a.tier] ?? 0) + 1;
  return acc;
}, {});
console.log(
  `Wrote ${airports.length} airports to public/airports.geojson ` +
    `(tier 1: ${byTier[1]}, tier 2: ${byTier[2]}, tier 3: ${byTier[3]})`,
);
