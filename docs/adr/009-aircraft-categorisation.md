# ADR-009: Aircraft Categorisation for Map Icons

## Status

Rejected — built, measured against live traffic, and reverted (2026-08-12).

The rejection applies to the data source, not to the feature. The implementation is preserved in commit `1e7a67b` and reverted in `3fa6dee`; a viable alternative source is documented below and remains open.

## Context

Every aircraft on the live map renders with one identical icon (see [ADR-005](005-geojson-rendering.md) for why they share a single GeoJSON symbol layer). A flight tracker that draws a helicopter, a Cessna and an A380 as the same silhouette loses information a viewer can otherwise absorb at a glance, so the map should categorise aircraft and vary the icon by class.

The rendering side of this is cheap and was never in question. All aircraft must stay in one symbol layer, so the icon is selected by a data-driven `match` expression over a feature property rather than by splitting the layer — one expression, five images, no change to the click/hover binding or the selection overdraw.

The entire question is **where the category comes from**. Two sources exist, and they differ by two orders of magnitude in coverage.

## Decision

**Do not derive aircraft category from the ADS-B emitter category.** The field is present in OpenSky's feed and costs nothing to request, but it is empty in practice.

Measured against live global traffic on 2026-08-12, across three consecutive polls (~7,600 aircraft per sample, so sampling error is negligible):

| metric | value |
|---|---|
| aircraft reporting a usable category (≥ 2) | **2.3%** |
| aircraft reporting category 0, "no information" | **95.2%** |
| distinct icons in the default Scandinavian view | **1 aircraft in 294 — 0.34%** |
| distinct icons over Europe | 0.77% |

The parse was correct — the categories that do arrive are internally consistent, with heavy (72) outnumbering large (54) outnumbering light (14), exactly the shape real traffic has. The source data is simply absent. OpenSky's state vector only carries the emitter category when an aircraft *identification* message happened to be captured by a receiver, and in their aggregation that is rare.

At 0.34% the feature is not visible. It was reverted rather than kept, because the field costs ~110 KB on every 2–3 MB broadcast (`"category":0,` × ~7,600 aircraft) for no observable benefit.

## Alternatives Considered

**Registration-database lookup (viable, not adopted).** OpenSky publishes `aircraftDatabase.csv` — 90 MB, ~520k rows, 82.5% carrying `icaoaircrafttype`, the ICAO designator code (`L2J` = Landplane / 2 engines / Jet, `H` prefix = rotorcraft). Measured against the same live traffic it resolves **70.1% of aircraft globally and 63.7% over Scandinavia, putting a distinct icon on 19.3% of aircraft in the default view** — 57× the emitter category, and with a real jet/turboprop/piston distinction the ADS-B enum cannot express at all.

The design that would work: stream the CSV at startup into a flat `byte[16_777_216]` indexed by the 24-bit icao24 address. That is 16 MB resident with O(1) lookup and no per-entry dictionary overhead, comfortable on the 1 GB machine alongside the ~50 MB of in-memory trails, and it must be populated in the background so the map degrades to the single icon until it is ready rather than blocking startup.

Not adopted for now on cost and complexity grounds: ~90 MB re-downloaded on every deploy, 30% of aircraft still unresolved, and meaningful moving parts for what is ultimately cosmetic polish.

**Licensing note, since it will be the first question on revisiting this.** OpenSky grants use "solely for the purpose of non-profit research and non-profit education"; only for-profit and commercial entities need to request a written licence, so a personal project needs no permission and no correspondence. What is *not* covered is redistribution — committing the dataset or a derived lookup into this repository, or baking it into the Docker image, republishes their data, which is a different act from consuming it. Fetch it at runtime and keep it out of the repo.

**adsbdb.** Already integrated for live routes, and its `/v0/aircraft/{icao24}` endpoint returns the ICAO type (`B772`). Unusable here: it offers per-aircraft lookups only, with no bulk download, so it cannot categorise ~7,700 aircraft per broadcast. It remains a good fit for enriching the *selected* aircraft in `FlightPanel`, reusing the existing `RouteLookupService` pattern.

**Heuristic classification from altitude and speed.** Rejected on principle rather than measurement. A rule such as "low and slow implies light aircraft" misclassifies any airliner on approach, and because altitude and speed change continuously the derived class would change mid-flight — the icon would flicker between silhouettes as an aircraft climbs. A categorisation that is a property of the airframe must not be derived from a property of the fix.

## Consequences

- The map keeps its single aircraft icon. No regression: the implementation was built with the uncategorised case falling through to the original icon, so the reverted state and the shipped state were visually identical for 98.8% of aircraft.
- The broadcast payload is unchanged, and `Aircraft` carries no `Category`.
- **The emitter category is weight-based, not propulsion-based**, and this misleads on first reading. There is no "jet" value; category 3 (15,500–75,000 lb) is regional and business jets rather than light GA, so it would map to the airliner icon, not the light one. Anyone revisiting this should not expect the enum to express the categories a viewer actually perceives.
- If revisited, the rendering work is already done and reusable from `1e7a67b` — five nose-up 24×24 SVGs, the `match` expressions for `icon-image` and `icon-size`, the sticky-category handling in `DeadReckoningEngine`, and the invariant that `aircraft-layer` and `aircraft-selected` must carry identical layout properties or the selection overdraw becomes visible. Only the source of the category needs replacing.
- The broader lesson, and the reason this ADR records a rejection rather than being deleted: **the cheap data source was worth trying precisely because it was cheap, but it was only worth keeping if measured.** The whole exercise cost half a day and produced a number — 2.3% — that now closes the question permanently instead of leaving it to be re-attempted.
