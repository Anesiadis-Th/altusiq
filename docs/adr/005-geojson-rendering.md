# ADR-005: GeoJSON Symbol Layers over DOM Markers

## Status

Accepted

## Context

Phase 1 rendered aircraft on the map using individual `mapboxgl.Marker` instances — one DOM node per aircraft, each containing an SVG icon, positioned and rotated via CSS transforms. At the OpenSky Network's full feed of ~11,000 aircraft, the browser main thread was saturated by the layout and transform calculations required on every 10-second poll. This manifested as visible frame drops and scroll lag on the map.

The live map rendering approach needed to be replaced.

## Decision

I migrated to a **Mapbox GeoJSON source with a symbol layer**.

All aircraft positions are serialised into a single `GeoJSON.FeatureCollection` on each poll and pushed to Mapbox via `source.setData()`. Mapbox renders the entire fleet in WebGL via a single `symbol` layer, with heading-based rotation driven by a data expression (`icon-rotate: ['coalesce', ['get', 'heading'], 0]`).

The selected aircraft highlight was implemented as a **filtered circle layer** sitting beneath the symbol layer, rather than a feature-state expression. This was required because `feature-state` is restricted to paint properties in Mapbox GL JS — it cannot drive layout properties like `icon-size`. The circle layer is filtered to the selected `icao24` via `map.setFilter()`, which is a GPU-side operation and does not require re-uploading the feature data.

## Alternatives Considered

**Retain DOM markers, limit the fleet** — filtering to a smaller geographic region would reduce marker count, but this would require hiding data I already have and would not resolve the fundamental O(n) DOM cost.

**Mapbox cluster layer** — clusters aggregate nearby aircraft into numbered circles at low zoom levels. This is the conventional answer to "too many points" but destroys the live-radar feel that is central to the product: a clustered map cannot show individual aircraft orientation or callsigns. Ruled out on product grounds.

**SDF (signed distance field) icons** — loading the aircraft image as an SDF sprite would allow `icon-color` to be driven by data expressions (e.g. colouring by altitude band). Not used because it prevents drop shadows and requires re-cutting the icon. Noted as the correct approach if per-aircraft colour coding is introduced in a future phase.

## Consequences

- Frame rate is no longer tied to aircraft count. The GPU handles all rendering; the main thread only serialises the GeoJSON array once per poll.
- Click handling moves from per-element event listeners to a single `map.on('click', 'aircraft-layer', ...)` handler. Selected aircraft state is stored as an `icao24` string rather than a reference to the clicked feature, which ensures the detail panel always reflects the current poll's data rather than a stale snapshot captured at click time.
- The GeoJSON source and `setData` pattern established here is reused directly for the track line and playback marker in Phase 2.
- `icon-allow-overlap: true` is required. Without it, Mapbox's collision detection silently suppresses aircraft in dense airspace.
