# ADR-004: Mapbox GL JS for Map Rendering

## Status

Accepted

## Context

The core feature of AltusIQ is rendering thousands of aircraft on an interactive map. The map library must support:

- Rendering 10,000+ dynamic markers efficiently
- Custom marker styling and rotation (for aircraft heading)
- Dark theme to match the application UI
- Smooth pan and zoom at scale

I evaluated three options:

**Mapbox GL JS**

- WebGL-based vector map rendering
- 50,000 free map loads/month
- Extensive style customisation including built-in dark themes
- Large ecosystem and strong documentation
- Requires an access token (free tier)

**Leaflet**

- Open source, no token required
- Raster tile-based rendering
- Lightweight at small scale but performance degrades significantly with thousands of markers without plugins (e.g. Leaflet.markercluster)
- Less visually polished out of the box

**Google Maps JavaScript API**

- Familiar API with broad documentation
- $200/month free credit
- Less flexible custom styling
- Marker performance at scale requires the Advanced Markers API

## Decision

I chose **Mapbox GL JS**.

## Consequences

### Positive

- WebGL rendering handles large numbers of markers far better than DOM-based alternatives
- The `dark-v11` style integrates naturally with the application's dark UI theme
- GeoJSON source + symbol layer pattern (planned for performance optimisation) can render 10,000+ points as a single GPU-accelerated layer instead of individual DOM elements
- 50,000 free monthly map loads is more than sufficient for a portfolio project

### Negative

- Requires a `NEXT_PUBLIC_MAPBOX_TOKEN` environment variable, adding a signup dependency
- Mapbox GL JS accesses the browser `window` object, which crashes during Next.js server-side rendering. This requires wrapping the map component in a `dynamic()` import with `ssr: false` — a non-obvious requirement that causes a confusing error if missed
- The current implementation uses individual Mapbox Markers (DOM elements), which causes noticeable lag at 11,000 aircraft. Migration to a GeoJSON symbol layer is required for production-grade performance (tracked as a known optimisation)

### Future Optimisation

The marker-to-layer migration is a prerequisite for Phase 2 (historical playback), where smooth animation of aircraft positions will require the GPU-accelerated rendering path.
