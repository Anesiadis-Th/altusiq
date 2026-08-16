# ADR-016: Mercator Projection, Because Globe Drapes Line Layers Below Every Symbol

## Status

Accepted. Amends [ADR-004](004-map-rendering.md) (the library choice) and [ADR-005](005-geojson-rendering.md) (the layer stack this protects).

## Context

Selecting a flight draws its track as a `line` layer. Two of them exist: `track-line` for a replayed historical flight and `live-track-line` for the in-memory trail of a live aircraft.

Both were being cut to pieces. At the app's default zoom of 4, a selected flight's trail was chopped up by every aircraft icon and airport label it crossed, and at low zoom it vanished completely. At Scandinavian traffic density a 2 px line dashes in and out of existence exactly where following it matters.

The layer stack was not the problem, which is what made this hard to see. Mapbox draws layers in add order, none of these layers pass a `beforeId`, and both trail layers had already been moved above `aircraft-layer` for precisely this reason (`b1af0e5`). The selection ring and the playback marker, sitting in the same region of the stack, rendered correctly the whole time.

The difference between the layers that broke and the layers that did not is their type, not their position:

**GL JS v3 defaults to globe when the style does not name a projection**, and `dark-v11` does not. `transform.projection` only flips to mercator at **zoom ≥ 6**. Below that, `Style.applyProjectionUpdate` sees `projection.requiresDraping` and installs a mock terrain (`setTerrainForDraping`, `DrapeRenderMode.deferred`). With a terrain present, `isLayerDraped` becomes true for `line` and `fill` layers: they are rendered into the sphere's surface texture and composited **before** the undraped layers, whatever their position in the layer list. `symbol` and `circle` are never draped.

Measured with GL JS 3.24, `isLayerDraped` returns `{line: true, fill: true, circle: false, symbol: false}` at z2, z4, z5 and z5.5, and false for all four from z6. That is the whole bug: `track-line` and `live-track-line` are the only `line` layers in the app, `aircraft-highlight` and `playback-marker` are `circle`, and everything else is `symbol`.

The line itself is not altered. A pixel profile across it is identical under both projections — same peak of 208, same width — so the symptom reads as a z-order bug even though the layer stack is untouched.

## Decision

**Pass `projection: "mercator"` explicitly when constructing the map.** It is one option in `MapView`'s map constructor and it is load-bearing, not a style preference.

## Alternatives Considered

Four fixes were tried against the real map before settling on this one. All four are dead ends, recorded here so they are not retried.

**`line-emissive-strength`.** No effect. Draping is a compositing order, not a lighting result.

**`setFog(null)`.** No effect, and it confirmed the same thing: this is not a fog or atmosphere artifact, which is what globe-related rendering surprises usually are.

**`line-elevation-reference: ground` and `sea`, with and without `line-z-offset`.** This does genuinely un-drape the layer, which is why it looked like the answer. But an elevated line then renders nothing at all on the globe surface, trading an occluded trail for an invisible one.

**Switch projections dynamically** — globe while browsing, mercator while a flight is selected. Technically works and keeps the globe for the idle view. Rejected because changing projection animates the entire map, so every click on an aircraft would trigger a full world morph before the trail appears. The projection would become a side effect of selection, which is a worse surprise than a flat world map.

## Consequences

- **The world view is flat below z6 instead of a sphere.** This is the entire cost, and it is a real one: a globe is more attractive at the zoom level a first-time visitor lands on.
- **The globe cannot be restored without giving up the trails.** They are the same decision. Anyone who reads `projection: "mercator"` as a leftover default and deletes it will reintroduce a bug whose symptom appears nowhere near its cause.
- **The constraint is on the layer *type*, not the layer order.** Any future `line` or `fill` layer — a route great-circle, an airspace polygon, a bbox outline — inherits it. Debugging one of those by reordering `addLayer` calls would be time spent in the wrong file, which is exactly what happened here.
- Everything in [ADR-005](005-geojson-rendering.md)'s stack still holds: the trails sit above `aircraft-layer` on purpose, and that ordering is now actually honoured at every zoom rather than only from z6 up.
- The fix was verified against the live feed in headless Chrome at 1440×900 (the `frontend/verify` skill), not only reasoned from the GL JS source.
