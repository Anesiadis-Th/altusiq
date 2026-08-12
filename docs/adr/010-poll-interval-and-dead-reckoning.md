# ADR-010: Always-On Backend at ~120s Polling with Client-Side Dead Reckoning

## Status

Accepted. Amends [ADR-001](001-flight-data-provider.md), whose consequence "no request limits for authenticated users beyond a 10-second polling interval" was true when written and is no longer true.

## Context

OpenSky moved to a credit system. Credits are tracked in three independent buckets — `/states/*`, `/tracks/*`, `/flights/*` — and spending in one does not affect the others. A standard authenticated user gets **4,000 credits per day per bucket**. (Active feeders running a receiver get 8,000 and licensed users 14,400/hour; neither applies here.)

The cost of a `/states/all` call scales with the area of the requested bounding box:

| bounding box area | credits |
| --- | --- |
| ≤ 25 sq° | 1 |
| 25 – 100 sq° | 2 |
| 100 – 400 sq° | 3 |
| > 400 sq° or global | 4 |

The Scandinavia bbox is 504 sq°, so every poll costs 4 credits. That fixes the budget arithmetic:

```
4,000 credits/day ÷ 4 credits/call = 1,000 calls/day maximum
86,400 s/day ÷ 1,000 calls        ≈ one call every 87 s minimum
```

Phase 1 polled every 10 seconds. That is 8,640 calls/day × 4 = **34,560 credits — 8.6× the daily budget**, exhausting the allowance in under three hours and returning 429 until the next refill. This was never sustainable; it was merely never observed, because Phase 1's machine auto-stopped when idle and so never ran long enough in a single day to hit the wall.

Phase 3 makes the backend always-on for demo quality, which surfaces the problem immediately. The interval had to change, and a 12× slower poll would make aircraft visibly jump across the map.

## Decision

**Poll every ~120 seconds, not 87.** 720 calls/day × 4 = 2,880 credits, comfortably inside 4,000 with headroom for retries and restarts. The floor is not used as the target, because a budget consumed exactly is a budget that fails on the first anomaly. The real burn is verified from the `x-rate-limit-remaining` response header on every call rather than trusted from this arithmetic.

**Run always-on.** `min_machines_running = 1` with auto-stop disabled in `fly.toml`. This is a deliberate ~$2–5/month cost on shared-cpu-1x 1GB, accepted for demo quality. Compute is the cheap part: there is no Fly volume and no dedicated IPv4 attached, so there are no surprise line items.

**Recover the smoothness client-side, not by polling harder.** `DeadReckoningEngine` (`frontend/lib/deadReckoning.ts`) advances each aircraft between polls from its reported `velocity` and `true_track`. When a real fix lands the engine does not snap to it — it records the offset between the on-screen position and the new truth, then blends it out over `CORRECTION_MS` (1,500 ms). Teleporting reads as broken in a demo; a 1.5-second glide does not.

Three constants carry the design, and two of them are load-bearing in non-obvious ways:

- **`MAX_EXTRAPOLATE_S` (150) does double duty** as both the extrapolation clamp and the unseen-track eviction TTL. It must exceed the poll interval: an aircraft absent from a single 120s poll has to keep dead-reckoning rather than be dropped, or planes flicker off the map for 120–240s at a time.
- **`RENDER_INTERVAL_MS` (100)** throttles the render loop to ~10fps. Rebuilding the whole GeoJSON source and calling `setData` on every animation frame saturates the main thread at global aircraft counts — visible stutter, and `setFilter` repaints get starved. Dead-reckoned motion is sub-pixel per frame, so 10fps is visually identical and far cheaper.
- **`GapThresholdSeconds` rises from 120 to 360** in the ingestion settings. At 10s polling, a 120s gap meant twelve consecutive misses. At 120s polling it would mean *one*, so a single poll in which an aircraft is briefly absent would close its flight and fragment one real flight into many.

**Push a cached snapshot on connect.** `LiveSnapshotStore` holds the last parsed aircraft list, and `FlightHub.OnConnectedAsync` sends it to the joining client as a normal `ReceiveFlightData` message. Without this a new visitor stares at an empty map for up to 120 seconds. The snapshot can be two minutes stale; the correction blend absorbs the discrepancy on the first real fix.

## Alternatives Considered

**Keep 10s polling on a smaller bounding box.** Shrinking below 400 sq° drops the call to 3 credits and below 100 sq° to 2, but even at 1 credit per call 8,640 calls/day exceeds 4,000. No bbox small enough to fix the arithmetic is large enough to be interesting, and the bbox now serves a different purpose entirely (see [ADR-011](011-global-live-coverage.md)).

**Let the machine auto-stop, as in Phase 1.** This is what masked the problem originally. It also breaks continuous flight segmentation, empties the in-memory trails on every stop, and gives the first visitor after an idle period a cold start. The always-on cost buys correctness in the ingestion pipeline, not just responsiveness.

**Interpolate on the server and stream at a higher rate.** Moves the same guesswork server-side while multiplying SignalR traffic on a 2–3 MB payload, and the server has no better information than the client — both are extrapolating the same fix. Client-side interpolation costs nothing per additional viewer.

## Consequences

- Live positions are extrapolated for up to two minutes and are therefore approximate between fixes. Acceptable for a visualisation; it would not be for anything operational.
- Enrichment is unaffected. It draws on the `/flights/*` bucket, which is independent of `/states/*`, so the nightly batch never competes with live polling for credits (see [ADR-008](008-flight-enrichment-strategy.md)).
- **Landing overshoot, known and deliberately unfixed.** The model is constant-velocity and straight-line, with no concept of landing: it reads `velocity` and `heading` and ignores `barometric_altitude` and `vertical_rate`. The `on_ground: true` case is handled, but the common landing path is an aircraft that simply drops out of `/states/all` at low altitude as ground ADS-B reception degrades, never reporting it. Its track keeps extrapolating at approach speed until eviction — roughly 10.8 km past the last fix at 72 m/s over the full 150s window — and then vanishes in mid-air. Left unfixed because it is a look-for-it artifact that requires tracking one specific aircraft through touchdown, and every available fix is a heuristic over frequently-null data that trades a rare artifact for a worse one: a legitimately descending aircraft (step-down, hold, crossing traffic) freezing in mid-air far from any airport. The remedy if the approach view ever matters is an altitude gate — cap extrapolation at ~20–30s for tracks last seen low and descending, leaving cruise traffic's missed-poll tolerance untouched.
- Because `MAX_EXTRAPOLATE_S` is both the motion clamp and the eviction TTL, the clamp can never actually engage — any surviving track has `dt ≤ 150` by definition — so aircraft fly at full speed right up to the instant they are deleted rather than coasting to a halt. Splitting the constant would fix the cosmetics but only trims the overshoot from ~10.8 km to ~9.7 km, so it is only worth doing alongside the altitude gate.
- `ingest` stamps its base time from SignalR receipt rather than the payload's `last_contact`, OpenSky's own observation timestamp, which can itself be tens of seconds stale. The effect is a small persistent lag behind reality during normal flight — distinct from the landing artifact, and the one fetched field the engine never reads.
- The engine's pure logic is covered by unit tests (`lib/deadReckoning.test.ts`), including the correction blend at 0/half/full window, missed-poll survival at exactly 120s, and eviction at exactly `MAX_EXTRAPOLATE_S` versus one millisecond past it. The constants are duplicated in the test deliberately, so changing them in `lib/` fails the suite and forces a deliberate decision.
