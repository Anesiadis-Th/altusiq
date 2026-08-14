# ADR-014: Backend Test Seams over an In-Memory Database

## Status

Accepted. Amends [ADR-007](007-flight-segmentation.md), which had `FlightIngestionService` receive `IServiceScopeFactory` directly; the scope-per-write pattern it describes is unchanged but now lives one layer down, in `EfFlightWriter`.

## Context

The frontend had 44 tests and the backend had none. That split was never a judgement about where the risk lived — it was a reflection of what was cheap to test. The rules most likely to break quietly are all on the backend:

- **The gap boundary** in `FlightIngestionService` decides where one flight ends and the next begins. Wrong in one direction and a real flight fragments into several rows; wrong in the other and separate flights merge into one.
- **The `RegionPointCount >= 2` eligibility bar** decides which of the ~10,000 aircraft in each global poll get persisted. Loosening it is the difference between ~5,300 rows a day and exhausting the Supabase free tier.
- **The sticky region counter** keeps a long-haul departure eligible after `MaxTrackPoints` has trimmed its Scandinavian leg out of the track. It is one `+` in one expression, and nothing on screen reveals it is wrong.
- **Row-level parse tolerance** exists because a single malformed state row once threw out of `ParseStates` and discarded an entire poll.

None of these fail loudly. A broken gap boundary produces plausible flights with wrong durations; broken eligibility silently fills a database. They are the class of defect a test catches and a demo does not.

Two obstacles were structural rather than a matter of effort. **`FlightIngestionService` could not be constructed without a database** — it took `IServiceScopeFactory`, resolved an `AltusIqDbContext` and called `SaveChangesAsync` inside the same method that owns the close decision, so reaching the boundary logic meant standing up a provider first. And **its clock was `DateTime.UtcNow`**, while the boundary itself is defined in seconds against `GapThresholdSeconds` (360). Testing it against the real clock means sleeping six minutes per assertion, or not testing it.

## Decision

**Introduce `IFlightWriter` as a persistence seam.** `FlightIngestionService` takes it instead of `IServiceScopeFactory` and no longer references `AltusIQ.Api.Data` at all. `EfFlightWriter` is a ~25-line singleton holding the scope-per-write pattern from ADR-007 unchanged. Tests substitute a fake and assert on exactly which flights were handed over, with what `OpenedAt`, `ClosedAt` and track points — which is the interesting question. Whether EF can round-trip a `Point` is not.

**Inject `TimeProvider`.** `TimeProvider.System` in production, `FakeTimeProvider` in tests. This is what .NET 8 added the abstraction for, and it turns the gap boundary into an exact assertion: at precisely `GapThresholdSeconds` the flight stays open, one second later it closes.

**Extract `OpenSkyStateParser` from `FlightPollingService`.** Parsing is not polling, and testing it in place meant constructing a service with nine constructor dependencies to exercise a pure string-to-list function. It returns `ParsedStates(Aircraft, Skipped)`; the service still owns the logging.

**Test through the public entry point, not private methods.** Thinning, `MaxTrackPoints` trimming and the region counter are all driven through `ProcessAsync` and observed via the fake writer or `GetActiveTrackAsync`. The internals stay private and refactorable.

**Mutation-check the suite rather than trust the green.** Fourteen defects were injected one at a time into the production code — the boundary off by one, the region counter recomputed instead of accumulated, the eligibility bar lowered, the boundary-neighbour rule dropped from thinning, `on_ground` read with `GetBoolean()`, the trails forgotten before the write succeeds — and each run confirmed the suite went red. The first pass caught 13 of 14: the survivor was a test that polled every 10 s against a 30 s record interval, so the recording path refreshed `LastSeen` regardless and the throttled path it claimed to cover was never load-bearing. It was rewritten to push the record interval past the gap threshold.

**Keep the frontend's scope rule.** Only pure input-to-output logic is tested. Nothing mocks EF, SignalR, or HTTP; where a rule needs testing, a seam is extracted rather than a mock wall built.

## Alternatives Considered

**EF Core's in-memory provider.** No production change, which was its appeal. Rejected on three counts: the EF team explicitly discourages it for testing; it would test EF's ability to store objects rather than the segmentation rules that are actually at risk; and `OnModelCreating` calls `HasPostgresExtension`, `HasDefaultValueSql` and `HasMethod("gist")`, so the model would likely need provider guards added purely to satisfy a test double.

**SQLite in-memory.** Closer to a real relational provider, but `Flight` carries a NetTopologySuite `Point` and a `jsonb` column. Neither maps, and working around both would mean a schema that no longer matches production.

**Testcontainers against real Postgres + PostGIS.** The highest fidelity option and the right answer if `AnalyticsService` or `FlightQueryService` are ever tested, since those are almost entirely SQL. Rejected here: CI would need a Docker service and the run would go from 13 ms to tens of seconds, to test segmentation logic that never touches SQL.

**Parser tests only, leaving the DB path alone.** Zero production change and it would still have covered the malformed-row regression. Rejected because it skips the gap boundary, which is the single highest-consequence rule in the ingestion path.

**Restructuring to `backend/src` + `backend/tests`.** The conventional .NET layout. Rejected as disproportionate: `fly.toml`, the Dockerfile, both workflows and every documented path reference `backend/` directly, and the same isolation is achieved with four `Remove` items in the API csproj.

## Consequences

- **37 backend tests running in 13 ms**, 81 across the project. `dotnet test` runs in CI between build and deploy, so a broken segmentation rule now fails a check rather than shipping.
- **`backend/` now holds a `.sln` beside a `.csproj`**, which makes bare `dotnet build` and `dotnet publish` fail as ambiguous. The Dockerfile and CI name their target explicitly. This is a genuine trap for anyone running a bare `dotnet` command in that directory.
- **The API csproj globs every `.cs` beneath it**, so the test project is excluded via `Compile`/`Content`/`EmbeddedResource`/`None` `Remove` items, and from the Docker build context via `.dockerignore`. Verified by replicating the build context and publishing from it: no test assemblies in the image.
- **`TimeProvider` is registered but consumed only by `FlightIngestionService`.** Other time-dependent workers (retention, enrichment, the poll heartbeat) still read the system clock directly. They can adopt it when they are tested.
- `Microsoft.EntityFrameworkCore.Design` moved from a floating `8.*` to a pinned `8.0.11`. Floating resolved to 8.0.28 and pulled an EF Relational newer than the one Npgsql pins, which surfaced as MSB3277 in any project referencing the API.
- **Still untested:** controllers, `AnalyticsService`, `RetentionService`, `FlightEnrichmentService`, and the HTTP paths in `FlightPollingService` (401-refresh-retry, rate-limit header handling). The first four are predominantly SQL or scheduling and want a real database; the last wants a stubbed `HttpMessageHandler` and is the cheapest remaining target.
