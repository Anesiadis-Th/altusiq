# ADR-012: adsbdb for Live Route Lookup

## Status

Accepted. Referenced in passing by [ADR-008](008-flight-enrichment-strategy.md) before this record existed.

## Context

Clicking a live aircraft should answer the first question anyone asks about a flight: where is it going? Neither field the live feed provides can answer it.

OpenSky's `/states/all` carries **no route information at all**. Its `origin_country` field is the airframe's country of *registration*, not a departure airport — an Irish-registered aircraft flying Oslo to Berlin reports "Ireland" — which is why the panel labels it "Registered in" rather than allowing the misreading.

OpenSky's `/flights/*` endpoints do carry departure and arrival airports, but they are next-day batch and cannot be queried for a flight currently in the air (see [ADR-008](008-flight-enrichment-strategy.md)). They serve historical enrichment and are structurally incapable of serving the live panel.

A live route therefore requires a third source, and the only free one that maps a callsign to its endpoints is a crowdsourced callsign database.

## Decision

**Use adsbdb.com, keyed by callsign, behind a backend endpoint.** `RouteLookupService` is a typed `HttpClient` fronted by `IMemoryCache` and exposed as `GET /api/routes/{callsign}`. The base URL lives in `appsettings` under `Adsbdb:ApiBaseUrl`.

**Cache both outcomes, asymmetrically.** Successful lookups are held for 24 hours — a callsign's route is a static mapping that does not change intraday — and misses for 6 hours. Caching negatives matters more than caching positives here, because the miss rate is high and uncached misses would hammer a free third-party API on every click.

**Distinguish "unknown" from "broken", and never cache the latter.** A callsign adsbdb does not know returns 404. An upstream failure is logged and *also* returned as 404, but deliberately not cached, so a transient outage does not blind the panel for six hours. Callsigns are validated against `^[A-Z0-9]{2,8}$` before being used in an upstream URL.

**Treat absence as a first-class UI state.** The frontend `useRoute` hook is a TanStack Query with `retry: false`, mapping 404 to `null` rather than to an error, and `FlightPanel` renders "Route unknown". A missing route is the expected case for a large share of traffic, not a failure.

**Compute the ETA locally rather than buying it.** The route DTO carries adsbdb's airport coordinates, so the panel derives an arrival estimate from a haversine distance between the aircraft's last fix and the destination, divided by current ground speed (`lib/geo.ts`). It renders in the viewer's local time and is explicitly `≈`-labelled, and it is suppressed entirely when the aircraft is on the ground, below 30 m/s, or missing coordinates. It refreshes for free, because `MapView` re-resolves the selected aircraft from every broadcast.

## Alternatives Considered

**A commercial schedule API.** This is the only way to get *scheduled* and *actual* times rather than a great-circle estimate. AeroDataBox's free tier (600 units/month via RapidAPI) was the pick and remains the choice if this is ever revisited, but it requires a RapidAPI key, and the free allowance does not survive a public map where any visitor can click any aircraft. Scheduled times are on hold rather than rejected — neither OpenSky nor adsbdb has schedule data at all, so no free path to them exists.

**Deriving the route from OpenSky enrichment.** Structurally impossible for live flights: the data does not exist until the next-day batch runs.

**Calling adsbdb directly from the frontend.** Would expose the dependency to every visitor's browser, forfeit the shared server-side cache (each client would re-fetch the same popular callsigns), and put a third-party origin in the page's request path. The backend proxy gives one cache across all viewers.

## Consequences

- **adsbdb is a static, crowdsourced callsign→route mapping, not per-flight actuals.** Scheduled airline callsigns resolve well; GA, charter and military callsigns generally return nothing, and a rerouted or seasonally-changed flight can be confidently wrong. The panel's route is an indication, not a source of truth — which is also why it must never be used to backfill the historical `DepartureAirport`/`ArrivalAirport` fields that [ADR-008](008-flight-enrichment-strategy.md) populates from OpenSky's own batch.
- The ETA inherits every weakness of a straight-line estimate: it ignores routing, winds, holding and approach sequencing, and it degrades as ground speed fluctuates. The `≈` and the suppression rules are the honesty mechanism.
- **adsbdb's route data carries a redistribution restriction.** The project is MIT-licensed and its aircraft data comes from PlaneBase, but the route data is marked as not to be copied, published, or incorporated into other databases without explicit permission. Consuming the API per-callsign, as here, is the intended use; bulk-harvesting it into a local table would not be. If route lookups ever need to scale beyond per-click, that constraint governs the design, not just the rate limit.
- adsbdb also exposes an aircraft-type endpoint that would enrich the selected aircraft in the panel using this same service pattern. It cannot serve the map's icons, because it offers no bulk download and the map needs ~7,700 lookups per broadcast (see [ADR-009](009-aircraft-categorisation.md)).
- `FlightRouteDto` is named to avoid colliding with the pre-existing analytics `RouteDto` (top routes) in `AnalyticsDto.cs`. The two are unrelated: one is a live callsign lookup, the other an aggregate over persisted flights.
