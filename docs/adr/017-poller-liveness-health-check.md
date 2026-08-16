# ADR-017: A Health Check That Reports Poller Liveness and Restarts Nothing

## Status

Accepted. Constrains the always-on deployment from [ADR-010](010-poll-interval-and-dead-reckoning.md).

## Context

`/health` was a bare `AddHealthChecks()` with no registered checks. It returned 200 whenever the process could answer an HTTP request, which is the one failure mode this application does not have.

The failure that actually matters is a stalled poll loop. If OpenSky has an outage, the daily credit bucket is exhausted, or the OAuth2 token stops refreshing, the API keeps serving history, analytics, search and routes perfectly while the live map quietly dies: the last snapshot keeps being pushed to new clients, dead reckoning keeps advancing aircraft from fixes that are minutes old, and after `MAX_EXTRAPOLATE_S` the planes evict one by one until the map is empty. Throughout, `/health` reports 200.

The gap widened with the move to always-on ([ADR-010](010-poll-interval-and-dead-reckoning.md)). A machine that auto-stops when idle effectively restarts itself out of many transient faults; a machine that never stops carries a stalled loop indefinitely.

## Decision

**Record a heartbeat, and check its age.** `backend/Health/` holds three small pieces:

- `PollHeartbeat` — a singleton with an `Interlocked`-guarded tick count and timestamp. `FlightPollingService` calls `RecordSuccess()` immediately after `_snapshotStore.Update(aircraft)`, so the heartbeat means "a poll completed and its data reached the snapshot", not "an HTTP request returned".
- `PollerHealthCheck` — Unhealthy, and therefore HTTP 503, when the last success is older than `3 × PollIntervalSeconds`.
- `HealthResponseWriter` — emits JSON carrying the reason and the diagnostic data. The default writer emits the bare status word, which tells an operator that something is wrong and nothing about what.

**The heartbeat must not live on `FlightPollingService`.** `AddHttpClient<FlightPollingService>` registers the service as **transient**, so a health check that resolved it would get a second instance with a fresh clock and report healthy forever. This is a silent trap: the wiring compiles, the check runs, and it is simply always green.

**Seed the heartbeat to process start, not to null.** That gives a fresh deploy a three-interval grace window instead of reporting unhealthy before the first poll can land. `successful_polls: 0` still distinguishes "starting up" from "steady state" for anyone reading the payload.

**Wire nothing to it.** `fly.toml` deliberately declares no `http_checks`.

## Alternatives Considered

**Wire Fly's `http_checks` to `/health`.** The obvious move, and the reason it is refused is the point of this ADR. With `max_machines_running = 1`, a failing health check pulls the only machine out of rotation — so a stalled *poller* would take down history, analytics, search and routes, none of which depend on it. And if the cause is credit exhaustion, a restart cannot fix it: the machine would restart-loop with the whole site down until the daily refill hours later. The check would convert a degraded live map into a total outage, on the failure mode most likely to trigger it.

**Restart only the polling loop on staleness, in-process.** Narrower than a machine restart and it avoids the blast radius above. Rejected for now because the plausible causes divide into ones a restart cannot help (credit exhaustion, OpenSky down) and ones the existing retry already handles. It would add a self-healing mechanism whose main effect is to hide the condition the check exists to expose.

**Report degraded rather than unhealthy** so the endpoint stays 2xx. Rejected because an external uptime monitor keys on the status code, and a poller that has not succeeded in six minutes is not a warning.

**Track the heartbeat inside `FlightIngestionService`**, which is already a singleton. Rejected on layering: ingestion would be reporting on the poller's behalf, and ingestion legitimately receives nothing when a poll returns zero usable rows.

## Consequences

- **`/health` is truthful and nothing acts on it.** It is for a human or an external uptime monitor. This is a deliberate asymmetry and it should not be read as an unfinished wiring job — the "missing" `http_checks` block is the decision.
- The threshold is derived from configuration (`3 × PollIntervalSeconds`), so changing the poll interval moves the threshold with it and there is no second constant to forget.
- **A 503 alone does not prove the check works.** A wiring bug makes it permanently 503, which looks like a working check catching a real fault. Testing it means confirming the healthy branch flips back to 200 — pointing the poller at a dead URL to force the unhealthy branch, then at the local stub to confirm recovery (see the stub-testing note in `CLAUDE.md`, and never against real OpenSky).
- `app.UseSerilogRequestLogging()` was added alongside. Serilog had been configured since Phase 1 but no HTTP request was ever logged, so the request path was invisible in exactly the incidents this check is meant to surface.
- The check covers the poll loop only. Retention, enrichment and the analytics cache warmer have no heartbeat, and a stalled nightly worker is still silent. They fail far less consequentially and on a daily rather than two-minute cadence, so they were left out rather than overlooked.
