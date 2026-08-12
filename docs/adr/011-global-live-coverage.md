# ADR-011: Global Live Coverage with the Bounding Box Scoped to Persistence

## Status

Accepted. Supersedes the **regional scoping** lever of [ADR-006](006-storage-strategy.md); the flight-as-track schema and the other two levers in that ADR stand, with the corrected arithmetic noted below.

## Context

[ADR-006](006-storage-strategy.md) used one bounding box for two jobs at once. The Scandinavia box (lon 4–32, lat 54–72) decided both **which aircraft the system paid attention to** and **which position fixes got recorded**, and conflating those two things produced a bug that went unnoticed for a phase.

Because only in-bbox fixes were appended to a track, a flight leaving the region simply stopped being recorded at the boundary. A Copenhagen–Rome departure was tracked to the edge of the box, timed out `GapThresholdSeconds` later, and was persisted as a flight whose track ended in mid-air over northern Germany. `ClosedAt` was derived from that timeout, so it recorded a boundary crossing rather than a landing, and every duration shown in the history panel was correspondingly wrong.

This was not a rare edge case. Measured on the live database: **25% of all history rows were affected**; 32% of closed flights ended above 5,000 m, and 79% of those ended at the bbox edge.

Separately, the live map had been showing only Scandinavian traffic — a coherent product story, but a visually thin one for a map that renders the whole globe.

## Decision

**The `/states/all` poll is unbounded.** The live map shows the entire world, ~10,000 aircraft, a 2–3 MB SignalR broadcast per poll and per connect. This is free: a global call bills the same **4 credits** as any bbox over 400 sq°, and the previous Scandinavian box was already 504 sq°. The poll got strictly more data at identical cost.

**Do not split the global call into per-region calls.** Each region bills separately, so a staged Scandinavia → Europe → world fetch multiplies the credit cost for data a single global call already returns.

**The bounding box now scopes persistence only.** It no longer decides what is observed or what is recorded — only which completed flights are written to Postgres. A flight qualifies by having **at least two fixes inside the region**, the same eligibility bar the old bbox-filtered dictionary applied in practice, so the persisted population (~5,300 flights/day) and the analytics built on it stay comparable. Aircraft that merely clip a corner of the region are still dropped rather than stored at full global length.

**One dictionary, not two.** `_trails` in `FlightIngestionService` replaces the former `_activeFlights` + `_liveTrails` pair. The full global track was always being captured for the live-trail feature — it was simply discarded at eviction instead of persisted. Four properties follow from the merge:

- **The close trigger is disappearance from the global feed, not bbox exit.** `OpenedAt` and `ClosedAt` are derived from the first and last persisted track point, so they always bracket the stored track. A side effect worth knowing: a flight that leaves and re-enters the region is no longer split into two database rows.
- **The region counter is sticky.** `MaxTrackPoints` trimming must never erase the fact that an aircraft was over the region, or a long-haul departure would stop qualifying for persistence part-way through its own flight.
- **Out-of-region points are thinned at persist time, never at capture.** `ThinOutOfRegionPoints` keeps every in-region fix at full poll resolution and reduces the rest to one point per `OutOfRegionPointIntervalSeconds` (300). Thinning at capture would coarsen the live trail for every aircraft worldwide; thinning on write takes the storage saving for free. The fixes on either side of a boundary crossing are always kept, so the drawn line enters and leaves the region on the real track instead of cutting a chord across it. Measured on a synthetic CPH–Rome track: 68 points reduced to 29 (43%), with all 8 in-region fixes preserved.
- **`LastAltitude` stopped being a cruise proxy** the moment tracks began running to landing. It now reports a touchdown altitude and collapsed the analytics altitude-band chart into the bottom bucket. Hence the `MaxAltitude` column, computed at flight close over the *untrimmed* track; the altitude bands bucket on `MaxAltitude ?? LastAltitude` so rows written before the migration still bucket sanely for the remainder of the retention window.

## Alternatives Considered

**Persist every global flight.** Roughly 100× the volume, which would exhaust the Supabase 500 MB free tier almost immediately. Global *observation* is free; global *storage* is not, and separating the two is the entire point of this ADR.

**Keep the bbox as an ingestion filter and accept truncated tracks.** This was the status quo, and it silently corrupted a quarter of the history table while making every duration in the panel wrong. Not a viable position once measured.

**Extend the bbox to cover Europe instead of going global.** Any box over 400 sq° costs the same 4 credits as the whole planet, so a larger-but-bounded box pays full price for partial data. There is no economic reason to stop short of global.

## Consequences

- **Three numbers in [ADR-006](006-storage-strategy.md) are now wrong** and should be read against this ADR: regional scoping no longer reduces the working set to 200–300 aircraft per poll (all ~10,000 are ingested); downsampling no longer drops "two of every three readings", because `MinPointIntervalSeconds` (30) is shorter than the 120s poll and so never discards anything; and `MaxTrackPoints` (300) is ~10 hours of flight at 120s intervals, not the ~2.5 hours it was at 30s intervals.
- Storage grows. Measured at a full 15-day window before the change: 152 MB total, 134 MB in `Flights`, averaging 22.3 points per flight at **~43 bytes per point after TOAST compression** — not the ~130 bytes the raw JSONB suggests, because sequential timestamps and coordinates compress hard. Do not size this from raw JSONB. Projection after the change is ~270–320 MB against the 500 MB free tier. If it overshoots, the safety valve is `Retention:RetentionDays` from 15 to 10, a one-value configuration change.
- In-memory cost of tracking every airborne aircraft globally is ~50 MB at peak (~10k aircraft × ~60 points × ~80 B), which is comfortable on the 1 GB machine.
- **A flight longer than ~10 hours loses its earliest points** to `MaxTrackPoints`, and for a long-haul departure those earliest points are precisely the Scandinavian leg the flight qualified on.
- **~7% of flights are still cut off**, ending at cruise away from the boundary. These are genuine ADS-B dropouts mid-region, and no ingestion change fixes them — the aircraft stopped being reported.
- The history panel's duration is *tracked* time, not block time: it starts at the first airborne fix, since aircraft are only appended when not on-ground, and ends at the last received fix, usually just before touchdown as low-altitude reception degrades. It undercounts at both ends by a few minutes. Once tracks ran to landing, the panel also had to learn to render `Xh YYm` — a long-haul had been displaying as `487m`.
- If the global broadcast ever feels heavy, the planned upgrade is **viewport subscription** — the client sends its map bbox to the hub and the server returns only aircraft in view — not a smaller poll bbox. Shrinking the poll would reintroduce exactly the observation/persistence conflation this ADR removes.
