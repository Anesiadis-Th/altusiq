# ADR-007: In-Memory Flight Segmentation over Redis

## Status

Accepted

## Context

The ingestion pipeline must track which aircraft are currently mid-flight, accumulate their position histories, and detect when a flight has ended in order to flush its completed track to Postgres. This requires maintaining per-aircraft state across polling cycles:

- The open flight's database ID
- The accumulated track points so far
- The timestamp of the last received position (for gap detection)

Two approaches were considered for holding this state: an in-memory singleton on the backend process, or an external Redis instance via Upstash.

Upstash was already provisioned in the project's planned infrastructure for this purpose. The decision to use it required evaluating whether the free tier budget would survive the polling load.

At approximately 300 active aircraft in the Scandinavia region and 4 Redis operations per aircraft per poll (read last position, write last position, append track point, update last-seen timestamp), the daily command volume is:

```
300 aircraft × 4 ops × 6 polls/min × 60 min × 24 hours ≈ 10.4 million commands/day
```

Upstash's free tier provides **10,000 commands per day**. The ingestion pipeline would exhaust this budget in approximately 90 seconds of operation.

## Decision

Active flight state is maintained **in memory** in a singleton `FlightIngestionService` using a `Dictionary<string, ActiveFlight>` keyed by ICAO 24-bit address. Access is serialised with a `SemaphoreSlim` since the dictionary is mutated from a background polling loop.

`FlightIngestionService` is registered as a singleton in the ASP.NET Core DI container. It receives `IServiceScopeFactory` rather than a direct `DbContext` reference, enabling it to create a scoped `DbContext` on demand when flushing completed flights to Postgres — the correct pattern for scoped dependencies consumed by singletons.

Redis (Upstash) remains in the infrastructure but its role is limited to caching the OpenSky OAuth2 bearer token, where its command volume is negligible (one read and one write per token refresh cycle, typically every 10 minutes).

## Alternatives Considered

**Redis with a reduced polling scope** — shrinking the bounding box to Denmark only (~50 aircraft) would bring the daily Redis command volume to approximately 1.7 million, still 170× over budget. Reducing the polling frequency to once per minute would bring it to ~864,000, still 86× over budget. No practical combination of scope and frequency reductions makes Redis viable on the free tier for this use case.

**Periodic flushing of in-progress tracks** — writing the in-progress track to Postgres on a schedule (e.g. every 5 minutes) rather than only on flight close. This would survive backend restarts at the cost of more frequent Postgres writes and a more complex upsert-based write path. Deferred to a future phase; the current write-on-close approach is simpler and sufficient for a single-instance deployment where restarts are infrequent.

**A paid Upstash tier** — the Pay-as-you-go tier at $0.2 per 100,000 commands would cost approximately $600/day at full polling load. Not viable.

## Consequences

- In-progress flight tracks are lost if the backend process restarts. Aircraft that were mid-flight when the restart occurred will begin new flight records when they reappear in the next poll. This produces artificially short tracks following a restart but does not affect the correctness of completed flights already persisted to Postgres.
- The singleton lifecycle requires careful handling of the `DbContext`. Using `IServiceScopeFactory` to create a short-lived scope per flush avoids the captured-scoped-dependency anti-pattern and keeps each Postgres write within a properly bounded unit of work.
- The gap detection threshold (`GapThresholdSeconds: 120`) means a flight that disappears from the OpenSky feed for more than 2 minutes is treated as closed. If the aircraft reappears after this window, a new flight record is opened. In practice, ADS-B coverage within the Scandinavia bounding box is dense enough that 2-minute gaps are rare and typically indicate a genuine flight event (landing, leaving the region) rather than a receiver outage.
- Upstash's daily command budget is preserved entirely for the token cache and any future caching layer (e.g. caching the latest global aircraft snapshot for newly connected clients).
