# AltusIQ — Frontend

Next.js + TypeScript client for AltusIQ. Renders the live global map, flight history and playback, search, and the analytics overlay.

For what the project is, the architecture, and the constraints behind it, see the [root README](../README.md) and the [ADRs](../docs/adr/).

## Setup

```bash
cp .env.local.example .env.local   # set NEXT_PUBLIC_MAPBOX_TOKEN and NEXT_PUBLIC_API_URL
npm install
npm run dev                        # http://localhost:3000
```

The backend must be running for anything beyond an empty map. `NEXT_PUBLIC_API_URL` points at it (`http://localhost:8080` locally); in production it lives in Vercel's environment, not in the repo.

## Scripts

| Command                 | What it does                                                             |
| ----------------------- | ------------------------------------------------------------------------ |
| `npm run dev`           | Dev server                                                               |
| `npm run build`         | Production build. Does not need a Mapbox token — the map is client-only  |
| `npm run lint`          | ESLint                                                                   |
| `npm test`              | Vitest, run once (45 tests)                                              |
| `npm run test:watch`    | Vitest, watch mode                                                       |
| `npm run build:airports`| Regenerates `public/airports.geojson` from OurAirports ([ADR-015](../docs/adr/015-airport-dataset-delivery.md)) |

## Layout

```
app/          Next.js app router — one page, the map
components/
  map/        FlightMap (state owner), MapView (all Mapbox), FlightPanel, FlightSearch
  flights/    History panel, playback controls
  analytics/  Overlay shell, lazy content, and the hand-drawn SVG charts
  ui/         Button, Card, Label, Rail
hooks/        TanStack Query hooks, playback loop, panel state
lib/          Pure logic: dead reckoning, flight search, chart maths, geo, aircraft layer
public/       airports.geojson (map dots), airportCodes.json (ICAO→IATA lookup)
data/         airlineCodes.json (IATA→ICAO callsign prefixes, bundled)
```

## Things that will bite you

- **Mapbox is client-only.** `FlightMap` is imported with `dynamic(ssr: false)`; GL JS touches `window` and crashes during SSR otherwise.
- **The map projection is pinned to mercator on purpose.** Removing it restores GL JS v3's globe default, which drapes `line` layers below every symbol and silently breaks flight trails. See [ADR-016](../docs/adr/016-mercator-projection.md).
- **`airports.geojson` and `airportCodes.json` are fetched, not imported.** A static import moves them into a JS chunk on the critical path. See [ADR-015](../docs/adr/015-airport-dataset-delivery.md).
- **API responses are snake_case**, matching the backend's global serialisation. The types in `types/` mirror it exactly.
- **Panels are one `activePanel` value, never a boolean each** (`hooks/useActivePanel.ts`). A fourth panel extends the union.
- **Tests cover pure logic only.** Nothing mocks Mapbox, SignalR, or TanStack Query — that is what the `frontend/verify` skill does against a real browser and the live backend.
