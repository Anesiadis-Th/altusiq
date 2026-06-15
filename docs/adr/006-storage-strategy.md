# ADR-006: Flight-as-Track Storage with Regional Scoping

## Status

Accepted

## Context

Phase 2 requires persisting aircraft position history to support historical flight playback. OpenSky returns approximately 11,000 aircraft globally on every 10-second poll, producing a theoretical maximum of:

```
11,000 aircraft × 6 polls/min × 60 min × 24 hours ≈ 95 million positions/day
```

The database is hosted on Supabase's free tier, which provides 500 MB of storage. A naive row-per-position schema at approximately 200 bytes per row (geometry column, timestamp, altitude, speed, heading, indexes) would exhaust this budget in under an hour of operation.

The storage approach had to be designed to fit comfortably within the free tier indefinitely without manual intervention.

## Decision

I store **one row per flight** in a `Flights` table. The complete position history for a flight is accumulated in memory and written to a single row as a JSONB array when the flight closes. The row also carries a PostGIS `geometry(Point, 4326)` column for the last known position, which carries the GiST spatial index used for bounding box queries.

Three levers control storage volume:

**Regional scoping** — only aircraft within a configurable Scandinavia bounding box (lon 4–32, lat 54–72) are ingested. This reduces the working set from ~11,000 aircraft to ~200–300 per poll.

**Downsampling** — at most one position is recorded per aircraft per 30 seconds. Since OpenSky is polled every 10 seconds, two of every three readings are dropped.

**Track capping** — the in-memory track per flight is capped at 300 points, corresponding to approximately 2.5 hours of flight at 30-second intervals. Points beyond this cap are dropped from the front of the list, preserving the most recent positions.

At these parameters, daily storage consumption sits well under 10 MB, leaving the free tier budget with substantial headroom for retention and growth.

## Alternatives Considered

**Per-position rows with a `flight_positions` table** — this is the canonical time-series schema and supports richer spatial queries (e.g. "which flights passed through this polygon at this time"). It was ruled out because storage costs are an order of magnitude higher: a 60-point flight consumes ~12 KB across 60 rows versus ~1 KB in a single JSONB column. On the free tier, this schema would require aggressive partitioning and retention jobs that add operational complexity without meaningful benefit at this scale.

**Global ingestion with aggressive retention** — storing all 11,000 aircraft globally with a very short retention window (hours rather than days). Ruled out because the volume spikes are hard to bound, and regional scoping provides a coherent product story ("Scandinavian airspace analytics") that global storage does not.

**PostGIS LineString with M-ordinate for time** — storing the track as a `LineStringM` geometry where the M coordinate carries the Unix timestamp per vertex. This is the spatially correct representation and supports PostGIS temporal queries natively. Ruled out because NetTopologySuite's handling of M-ordinates is fiddly, and the JSONB approach provides equivalent playback capabilities with simpler read/write code. Noted as the preferred approach if true spatiotemporal queries (e.g. `ST_LocateAlong`) are introduced.

**Redis (Upstash) for track accumulation** — see ADR-007.

## Consequences

- Track data is not queryable at the individual point level via SQL. Filtering by altitude range or speed during a flight requires deserialising the JSONB array in application code. This is acceptable because the current query patterns (fetch a track by flight ID, list flights by time range and bbox) do not require point-level filtering.
- The `LastPosition` PostGIS column with a GiST index enables efficient bounding box queries (`ST_Intersects`) over the `Flights` table without loading track data. This is the intended query path for the Phase 3 analytics dashboard.
- Flights in progress when the backend restarts are lost. The in-memory accumulation buffer is not persisted. This is an acceptable tradeoff for a single-instance deployment; a production system would periodically flush in-progress tracks to the database.
- The bounding box coordinates and timing parameters (`MinPointIntervalSeconds`, `GapThresholdSeconds`, `MaxTrackPoints`) are externalised to `appsettings.json` as `IngestionSettings`, making it straightforward to adjust regional coverage or storage density without code changes.
