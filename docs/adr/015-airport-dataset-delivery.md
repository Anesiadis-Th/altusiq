# ADR-015: Airport Datasets as Fetched Public Assets, Revealed by Zoom Tier

## Status

Accepted. Formalises a rule [ADR-013](013-analytics-presentation.md) already applied to the airport *code* table and attributed to [ADR-005](005-geojson-rendering.md), which never covered it. Both airport datasets are governed here.

## Context

Two separate airport datasets ship with the frontend, and they are easy to confuse because both are keyed by airport and neither is the other:

- **`public/airports.geojson`** — the map dots. Coordinates, 4,034 features.
- **`public/airportCodes.json`** — the ICAO→[IATA, name] lookup for analytics. 8,252 entries, no coordinates.

The map originally drew 49 hand-listed Scandinavian airports from `frontend/data/airports.json`, a static import. Once the live map went global ([ADR-011](011-global-live-coverage.md)) a map showing traffic over every continent and dots over Norway only was visibly incoherent. Scaling the dataset to the world raised two problems that a 49-entry array never had: how the file reaches the browser, and how many dots the map can draw before they stop being useful.

## Decision

**Filter the source to airports a flight tracker cares about.** `npm run build:airports` (`scripts/build-airports.mjs`, no dependencies, Node's global `fetch`) regenerates the file from the public-domain OurAirports dataset. Scope is an IATA code **and** `scheduled_service = yes`, restricted to the large/medium/small airport types. That drops roughly 4,900 IATA-coded grass strips and private fields, plus every heliport and seaplane base — all noise on a flight tracker. The result is 4,034 airports, and all 49 previous Scandinavian dots survive at identical coordinates.

**Fetch it, never import it.** `MapView` hands Mapbox the URL string (`data: "/airports.geojson"`) rather than `import`ing the JSON. A static JSON import becomes a JavaScript module inside a content-hashed chunk: 590 KB parsed by the JS parser on the main thread, before the first aircraft can render, and re-downloaded on every deploy because the hash changes. As a `public/` asset it is ~104 KB gzipped, CDN-cached across deploys, and parsed inside Mapbox's GeoJSON **worker**. The same rule and the same reasoning govern `airportCodes.json`, which is fetched by `useAirportCodes` with `staleTime: Infinity` inside the lazy analytics chunk.

**Reveal by zoom tier, not all at once.** Every feature carries `tier` (1 = large, 2 = medium, 3 = small) and both airport layers share one filter:

```js
["<=", ["get", "tier"], ["step", ["zoom"], 1, 6, 2, 8, 3]]
```

Large hubs always, medium from z6, small from z8. Verified with Mapbox's own `featureFilter` against the real file: **1,153 features visible at z4–5, 3,269 at z6–7, 4,034 at z8+.** The existing `minzoom` (circles 4, labels 5) is unchanged and independently keeps the world view free of dots.

**Carry `icao` even though nothing reads it.** It is the join key to an ICAO-only backend, so any future "click an airport to filter its flights" needs it, and it costs ~13 KB gzipped. It is absent for the 83 fields that genuinely have no ICAO code.

## Alternatives Considered

**Keep the static import and accept the parse cost.** The honest baseline, and it is what the 49-airport file did. At 590 KB it moves directly onto the critical path for time-to-first-aircraft, which is the one number the live map is judged on.

**Draw all 4,034 dots at every zoom.** Two failures, one visual and one mechanical. At world zoom the dots bury the aircraft, which are the actual product; and Mapbox's label collider runs over every feature on every camera move, so panning gets more expensive the further out you are — exactly backwards.

**Cluster the airports at low zoom**, as with any dense point layer. Rejected for the same product reason [ADR-005](005-geojson-rendering.md) rejected clustering aircraft: a numbered bubble reading "312" is not an airport, and the dots exist to give the traffic somewhere to be going.

**Vector-tile the airports** rather than ship one GeoJSON. The correct answer at an order of magnitude more features, and it would give real per-zoom simplification instead of a three-step filter. Disproportionate at 4k points and ~104 KB, and it would add a tile-hosting dependency to a dataset that changes when OurAirports changes, which is to say rarely.

**Merge the two datasets into one file.** They share a key and nothing else: the map needs coordinates and no names, analytics needs names and no coordinates, and the two are fetched by different code at different times — the geojson eagerly by the map worker, the code table lazily inside the analytics chunk. Merging would put 8,252 names on the map's critical path to save one request.

## Consequences

- **Tiers pop in at integer zoom boundaries.** Zoom expressions inside a *filter* are only evaluated at integer zooms, unlike zoom expressions in paint properties which interpolate continuously. Medium airports appear the instant the map crosses z6 rather than fading in. Accepted; the alternative is a paint-property opacity ramp on a layer that still has to draw every feature.
- **The geojson request is invisible to Playwright's page-level `response` events**, because the Mapbox worker issues it, not the page. Worth knowing before concluding the file never loaded — this cost real debugging time.
- Regenerating is a deliberate manual step (`npm run build:airports`), not a build hook. The dataset changes rarely and a network fetch inside `next build` would make CI depend on a third-party host.
- **Neither file may be "simplified" back into a static import.** Both have been converted at least once, `airportCodes.json` as recently as [ADR-013](013-analytics-presentation.md), and a static import is what a reader reaches for when tidying. The reason is a build-output property, invisible in the source diff.
- Airfields with no IATA code fall back to their raw ICAO in analytics labels (ENBM/Bømoen, and ~11.7k such fields worldwide), and are absent from the map layer entirely, since the geojson filter requires an IATA code.
- OurAirports is public domain, so unlike OpenSky's aircraft database ([ADR-009](009-aircraft-categorisation.md)) the derived file is committed to the repository rather than fetched at runtime.
