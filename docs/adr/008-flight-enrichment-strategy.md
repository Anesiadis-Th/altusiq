# ADR-008: Flight Enrichment as a Nightly Next-Day Batch

## Status

Accepted

## Context

A closed flight persisted from the ingestion pipeline (see [ADR-007](007-flight-segmentation.md)) records where an aircraft *was* — its track points, last position, and last altitude — but not where it was *going*. The live OpenSky `/states/all` feed carries no route information: `origin_country` is the airframe's registration country, not a departure airport, and there is no destination field at all. To show departure and arrival airports on a historical flight, the flight must be enriched from a separate source after it closes.

OpenSky exposes this through its `/flights/*` endpoints, which return the estimated departure and arrival airport for each aircraft over a time window. Two properties of these endpoints shape every decision below:

1. **They are next-day batch.** OpenSky computes estimated airports by a batch process that runs after the fact. Only the previous day and earlier are ever available — a flight that closed an hour ago cannot be enriched until OpenSky's batch has processed it, which is the following day at the earliest. There is no live route lookup here.
2. **They bill from an independent credit bucket.** OpenSky tracks credits in three separate buckets — `/states/*`, `/tracks/*`, and `/flights/*` — each with a 4,000 credit/day quota for a standard authenticated user. Spending on `/flights/*` does not draw down the `/states/*` budget that the ~120s live poll depends on. Enrichment and live polling never compete for credits.

The core design question was how to spend the `/flights/*` budget to cover a full day's closed flights. The measured close rate is ~5,000 flights/day across several thousand distinct aircraft, and OpenSky offers two shapes of endpoint to enrich them.

## Decision

Enrichment runs as `FlightEnrichmentService`, an in-process `BackgroundService` that fires once at startup and then nightly at **04:00 UTC**, and enriches a whole day at a time using the `/flights/all` endpoint.

**Day-batch, not per-aircraft.** `/flights/all` returns every flight in a time window for a flat **4 credits per call**, capped at a 2-hour window per call. A full previous day is therefore ~12 calls ≈ **48 credits**, independent of how many flights closed that day. The service fetches the day into a `Dictionary<string, List<OpenSkyFlightInfo>>` keyed by ICAO address, then matches each pending flight to the leg with the greatest time-overlap against the flight's own open/close interval.

**Bounded, self-limiting eligibility.** A flight is eligible for an enrichment pass when it is closed, not yet enriched, older than `MinAgeMinutes` (240), within `MaxLookbackDays` (2) of today, and has had fewer than `MaxAttempts` (3) passes. Crucially, the eligibility cutoff is clamped to **today's midnight UTC** — a flight that closed earlier today is never eligible, because its batch data provably cannot exist yet. Each pass over a still-pending flight increments `EnrichmentAttempts`; after three no-match passes the flight is abandoned as permanently unenrichable (a genuinely uncovered GA/military flight, or one OpenSky's batch never resolved).

**Null-tolerant.** Estimated airports are frequently null even for covered flights. `DepartureAirport` and `ArrivalAirport` are nullable, a match on one but not the other is still recorded, and the frontend treats absence as a first-class "unknown" state rather than an error.

**Staggered before retention.** The nightly enrichment at 04:00 UTC runs two hours ahead of the retention purge at 06:00 UTC (see [ADR-006](006-storage-strategy.md)). The stagger enforces "enrich before purge" so a flight's airports are captured before it can ever be deleted, even though in practice the 2-day enrichment lookback and the 15-day retention window never touch the same rows.

## Alternatives Considered

**Per-aircraft enrichment via `/flights/aircraft`.** This endpoint returns one aircraft's flights and was the original implementation, but it costs **30 credits per call** — one call per aircraft. At 4,000 credits/day it covers only ~133 aircraft/day before the bucket is exhausted, against a daily volume of several thousand. Per-aircraft enrichment cannot cover a day's traffic within budget by a factor of ~30×. Day-batching the same coverage for ~48 credits is the entire reason the design switched.

**Live enrichment at flight close.** Not possible. OpenSky's route data does not exist for a flight until its next-day batch runs, so there is nothing to look up at close time. Next-day is inherent to the data source, not a design choice.

**A third-party live route source instead of OpenSky batch.** Live callsign→route lookups exist (the project uses adsbdb for the *live* panel), but those are crowdsourced static callsign mappings, not per-flight actuals — they cannot confirm which airports a specific historical flight actually used, and GA/charter/military callsigns return nothing. They complement enrichment for the live view; they do not replace the historical airport fields.

## Consequences

- Historical departure/arrival airports lag reality by at least a day and are frequently null. This is acceptable for an analytics/portfolio surface where the airports feed aggregate "busiest airports" and "top routes" charts over a 15-day window, not a real-time operational display.
- Enrichment consumes ~48 credits of the 4,000/day `/flights/*` bucket on a full day, leaving the bucket almost untouched and the `/states/*` live-poll budget entirely unaffected.
- **Eligibility must exclude same-day flights, and this bit us.** Because the service also runs once at every startup, deploying three times in an afternoon originally ran three enrichment passes over that day's flights — every one a guaranteed no-match, because the data cannot exist yet — and each pass incremented `EnrichmentAttempts`. Three afternoon deploys permanently exhausted `MaxAttempts` on the day's flights *before their batch data was ever published*, so they were abandoned unenriched the next day when the data finally arrived. The fix clamps the eligibility cutoff to today's midnight UTC so same-day flights can never enter a pass, and skips the `MaxLookbackDays` window's most recent hours that a startup run before 04:00 UTC would otherwise reach into prematurely.
- **An all-empty day must not burn an attempt either.** OpenSky returns 404 (surfaced as an empty result) for any window whose batch it has not yet processed, so "batch not ready" is indistinguishable from "quiet day" at the row level — but a global `/flights/all` day with genuinely zero flights worldwide is impossible. The service therefore treats a day that returns zero legs across all windows as *not ready* and retries the whole date the next night without incrementing any attempts, rather than spending one of three attempts on data that has not landed.
- The startup run makes enrichment resilient to missed nightly windows (a restart near 04:00 UTC still gets a pass in), at the cost of the two failure modes above — both now guarded — that only a same-afternoon redeploy could trigger.
- The lessons above are really one principle: **never spend a bounded retry on data that cannot exist yet.** Attempt limits are for exhausting genuine uncertainty, not for outrunning a data source's publication schedule.
