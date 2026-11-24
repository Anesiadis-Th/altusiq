# ADR-018: Emergency Squawk Events with a Grounded Investigation Pipeline

## Status

Accepted, amended 2026-11-22 (see [Amendments](#amendments-2026-11-22)). Builds on the seam pattern of [ADR-014](014-backend-test-seams.md) and the global poll of [ADR-011](011-global-live-coverage.md). Nothing in [ADR-010](010-poll-interval-and-dead-reckoning.md) changes: the poll interval, the ingestion rules, the bounding box and the SignalR payload are exactly as they were.

## Context

Every `/states/all` row carries a transponder code at index 14, and the parser was ignoring it. Three codes have a fixed meaning worldwide: 7500 (unlawful interference), 7600 (radio failure) and 7700 (general emergency). They are rare, they are the one thing in the feed that is an *event* rather than a position, and a global poll sees all of them at no extra credit cost.

Two things are not known at the time of writing and shape the design:

- **How many there are.** Public figures suggest a handful of 7700s a day worldwide, far fewer 7600s, and 7500s rare enough that each one is news. There is no measurement for this feed, with its 120 s cadence and coverage gaps.
- **What they mean.** The code says a crew declared something. ADS-B never says why. A 7700 can be a sick passenger, a pressurisation fault, a precautionary return or a wrong button. Anything written about the reason is inference, and the data supports very little of it.

The second point rules out the obvious product, an "incident feed" with explanations. What the data supports is a record of the episode, the evidence around it, and a short reading of that evidence that says plainly what it cannot show.

## Decision

### Squawk stays off the wire

`Aircraft.Squawk` is parsed from index 14 and marked `[JsonIgnore]`. The broadcast payload, the snapshot pushed on connect and the frontend `Aircraft` type are unchanged. The frontend never sees a raw squawk; it sees *events*, which have passed a persistence rule and carry evidence. Putting the raw code on the wire would invite exactly the live "emergency map" this ADR declines to build, and would make every pilot who dials through 7700 on the way to another code a thirty-second spectacle.

One Serilog line per poll counts aircraft on each of the three codes. That line is the volume measurement the design is waiting for.

### Two polls to persist

A code seen on one poll is a candidate, held in memory only. The same code on the next consecutive poll confirms it, and an `AnomalyEvent` is opened. A candidate that misses a poll, or changes code, is dropped. This suppresses transit through a code, which at 120 s spacing is the dominant false positive, at the cost of a two-minute delay nobody will notice on a 120 s feed.

The event closes when the code is absent for one poll (the aircraft is present, on another code) or when the aircraft is unseen for longer than `Ingestion:GapThresholdSeconds`, the same 360 s the flight segmenter uses. `LastSeenAt` is the last fix *on the code*, never the moment the gap expired, for the same reason `ClosedAt` on a flight is the last fix and not the timeout.

The event id is a hash of `icao24` and the confirm second. A write that fails is retried on the next poll with the same id against an upsert, so a flaky database produces one row, not several. `EmergencySquawkDetector` holds its state per `icao24`, exactly like `FlightIngestionService._trails`, runs after ingestion so the trail it snapshots includes the current fix, and runs inside its own try/catch so it can never cost a poll.

### Evidence is snapshotted, not referenced

Most of the globe never gets a `Flights` row: persistence is scoped to Scandinavia ([ADR-011](011-global-live-coverage.md)) and a flight only lands in the table at close. An event over the Atlantic has nothing to foreign-key to, and an event over Sweden would point at a row that does not exist yet and will be purged after 15 days while the event lives for 90.

So the live trail is copied into the event, once at confirm and once at close, thinned to at most 120 points. `EvidenceJson` is self-contained: the investigator, the API and the eval fixtures all read the same shape, and no event ever dangles.

### The model never runs on the poll path

The poll is the one thing in the backend that must finish on time. A model call takes seconds, can take a minute, and can fail in a dozen ways. None of that belongs anywhere near `PollAsync`.

The detector enqueues an event id on a `Channel<Guid>` when the event closes, or after 30 minutes open, and moves on. `InvestigationService` is a `BackgroundService` that drains the channel with concurrency 1. The test `The_consumer_runs_independently_of_the_poll` drives the detector through a full confirm-and-close on a fake clock and asserts that the fake model client was never called until the consumer ran.

### Provider fallback and a daily budget

`ILlmClient` has one implementation, `OpenAiCompatibleChatClient`, over the chat-completions shape with the `tools` parameter that both Groq and OpenRouter accept. Providers are tried in configured order: Groq `llama-3.3-70b-versatile`, then OpenRouter `google/gemma-4-31b-it:free`. On a 429, a 5xx, a timeout, or invalid output after one retry, the agent moves to the next provider. A model that rejects the `tools` parameter gets single-shot mode, where code gathers every tool output first and asks for the brief in one request.

Both providers are free plans with no card on file. OpenRouter's free models allow 50 requests a day, hence `Llm:MaxCallsPerDay` of 40, counted per UTC day across all providers, with a 3 s floor between calls. `LlmCallBudget` is the whole cost model: when it is spent, or every provider fails, or `Llm:Enabled` is false, code assembles a deterministic brief from the tool outputs and stores it with status `uninvestigated`. The event always gets *a* brief; only the model is optional.

Keys come from the environment variables each provider names (`GROQ_API_KEY`, `OPENROUTER_API_KEY`), set as Fly secrets. Nothing about a key is in configuration or in the repository.

### What the model is allowed to say

The system prompt (`backend/Prompts/investigate.v1.md`) states what the data is and is not, forbids any claim beyond the tool outputs, forbids safety-tool language, and requires one JSON shape. Its version string is written onto every event, so a prompt change is visible in the data.

The shape has no `cause` and no `confidence` field, on purpose. A cause is not observable here, and a number labelled confidence would be read as one. Explanations carry a word, `plausibility`, and the evidence for each. `BriefValidator` rejects anything else, and the agent retries once with the validation error before storing `invalid_output`.

`GroundednessChecker` is mechanical: every four-letter upper-case code and every integer of 100 or more in the brief must appear in what the model was shown, meaning the tool outputs plus the event record. It cannot tell a true sentence from a false one. It catches the model importing an airport, an altitude or a distance from nowhere, which is the failure a reader cannot see. The result is stored either way; an ungrounded brief is shown with that flag, not hidden.

Sensitive events (7500) are never investigated and never exposed by the API unless `Events:ExposeSensitive` is on. The code means unlawful interference; it is not demo material, and a model's reading of it is worth less than nothing.

### What the eval harness measures

`backend/AltusIQ.Evals` runs the same `InvestigationAgent` over `cases/*.json`, each holding an event, recorded outputs for all five tools, expectations, and a recorded model response. In replay mode, the default, the model is a `ReplayLlmClient` and nothing leaves the process. With `LLM_API_KEY` and `LLM_PROVIDER` set it uses the real client, capped at 20 calls.

It reports six numbers: schema-valid rate, grounded rate, tool-selection accuracy (were the tools the case requires actually called), speculation rate (a `high` plausibility on `poor` evidence), mean latency and mean tokens. In replay mode the first four are checks on the *code*, and a mismatch fails the run. In live mode they are a reading of the *model*, and nothing fails: the nightly workflow uploads the results and the numbers are compared by hand.

What it cannot measure: whether a brief is *right*. There is no ground truth for why a crew set a code, so there can be no accuracy metric. The harness checks that the brief stays inside its evidence and inside its schema, which is the most that can be verified.

## Alternatives Considered

**Broadcast the squawk and draw emergency aircraft on the map.** Zero backend work, immediate visual. Rejected because a live "look, an emergency" marker is the safety-tool framing this project avoids, because transit through 7700 would flash on screen constantly, and because a raw code without evidence or context tells a viewer nothing true.

**A `Flights` foreign key for events.** Rejected above: most events have no flight row, and the ones that do outlive it.

**Run the investigation synchronously at close.** Simplest code. Rejected because a model call on the poll path risks the one timing guarantee the backend has.

**A paid model API.** Better briefs, almost certainly. Rejected on the project's standing rule of zero recurring cost; the design must work when the model is absent, and does.

**A vector store, an embeddings step, or an orchestration framework.** Nothing here needs retrieval over a corpus; the evidence is five tool calls with known shapes. Each would be a dependency in service of a diagram.

**Redis for detector state.** Same answer as [ADR-007](007-flight-segmentation.md): the state is tiny, lives one process, and a restart losing a candidate costs one poll.

## Consequences

- The data limits are the product's limits. Positions arrive every 120 s, so a brief can describe a descent but never a go-around. Coverage gaps end trails in mid-air. The reason for a code is, in the overwhelming majority of cases, unknowable from this feed, and every brief says so in its `unknowns`.
- **The volume is unmeasured.** The per-poll log line is the measurement. If it shows a 7700 every few polls the two-poll rule and the 40-call budget are about right; if it shows one a week the budget is irrelevant and a second trigger type (a sharp altitude change, a squawk-free loss of contact) might be worth adding. If it shows dozens, the budget will spend by mid-morning and most events will carry deterministic briefs, which is the designed behaviour.
- Everything the model writes is shown under an "AI-generated, unverified" label with the model name and, where the check failed, an "ungrounded" mark. The label is not decoration; it is the claim the project is making about the text.
- Free-tier model availability is not stable. Model ids were taken from each provider's list on the day of writing and will need refreshing. The code does not care which model answers; the eval harness exists so a swap can be measured rather than eyeballed.
- `TimeProvider` now reaches `EmergencySquawkDetector`, `LlmCallBudget` and `InvestigationAgent`. `RetentionService` still reads the system clock.
- 98 backend tests, 47 frontend tests. Three defects were injected and reverted to confirm the suite is load-bearing: the persistence rule reduced to one poll, the groundedness check disabled, and the provider fallback skipped. Each turned the suite red.
- **Still untested:** `OpenAiCompatibleChatClient` against a real provider, `MetarClient` against aviationweather.gov, `EfAnomalyEventWriter` and `EfAnomalyEventStore` against Postgres, and the Events controller. The first two want a stubbed `HttpMessageHandler`, the last three a real database, as in ADR-014.

## Amendments (2026-11-22)

Changes made before the first deployment. The decision above stands; these items change details it describes.

- **Primary model.** Groq's deprecations page lists `llama-3.3-70b-versatile` with a 2026-08-16 shutdown (its models page still lists it). A decommission error is a non-transient 4xx, so every event would have spent a budget call on Groq before falling through. The primary is now `openai/gpt-oss-120b` (free plan: 30 RPM, 1K RPD, 8K TPM, 200K TPD). The eval CLI reads the provider table from `appsettings.json` instead of keeping its own copy.
- **Tokens, not calls, may be the binding limit.** The tool loop resends the whole conversation each round. With a full 120-point trail, a four-call investigation is roughly 8K to 12K input tokens inside a minute, at or over Groq's 8K TPM. Not yet measured against a live provider; single-shot mode and the loop are to be compared in the harness.
- **Trace persisted.** `InvestigationTraceJson` keeps the tool outputs exactly as the model saw them, the tools called, the model's raw reply and the unsupported tokens; `ModelCalls` counts calls across every provider tried. Neither is served by the API. Without them a real event could not be audited or replayed.
- **Startup sweep.** The detector state and the queue are in memory, so a deploy used to leave events `open` forever and briefs `pending` forever. On startup `InvestigationService` closes events created before the process started at their `LastSeenAt` and re-queues every pending one. Events this process creates are never touched.
- **Deterministic brief crash.** The tool loop turns a null result into a `nothing returned` object and a tool exception into an `error` object. The data-only brief assumed a route object and a METAR station and threw on both, so an event with no adsbdb route stayed `pending` whenever no model answered. It now checks every shape.
- **Call spacing.** The budget stamped the call time before computing the wait, so every call waited `MinSecondsBetweenCalls`, the first included. The wait is now measured from the previous call.
- **Eval harness.** Replay runs in CI as a gate. The nightly workflow is now manual-only, because free quotas are per account and a scheduled live run would spend the deployed investigator's budget. Live runs are capped at 40 calls, 10 per case; a case the cap stops is reported as `budget_exhausted` and excluded from the rates instead of counting as invalid output. Results record the git commit. The fixtures' trail timestamps were a year and ten hours before their events; they are shifted onto the event times, and each fixture's at-confirm snapshot now holds only the fixes up to `confirmed_at`, with the full trail in the at-close snapshot.
- **Tests.** 108 backend tests. Reverting the deterministic-brief fix turns its regression test red.

## Amendments (2026-11-24)

Found by four live runs of case 02, a 7600 in cruise, on `openai/gpt-oss-120b` after deployment. The first completed on Groq in 4 model calls, 11,911 tokens and about 12 s; its brief was schema-valid and called the expected tools, but failed the groundedness check and put the aircraft on the wrong side of the nearest airport. The second and third hit Groq's per-minute token limit and fell back to the data-only brief. The fourth, with the fixes below, completed without a 429 and placed the aircraft correctly, but wrote "11,277 m" with a comma.

- **Groundedness and typographic spaces.** gpt-oss writes figures such as "11 277 m" with a narrow no-break space (U+202F). The check runs on `brief.ToJson()`, and System.Text.Json escapes every space separator except U+0020 even under `UnsafeRelaxedJsonEscaping`, so the integer pattern read `202` from the escape and `277` from the split figure. `GroundednessChecker` now decodes `\uXXXX` escapes and joins digit groups across no-break, thin and narrow no-break spaces before tokenising, and across commas in the brief only: in tool outputs a comma separates JSON values, and joining `40,240` in a trail row would hide the 240. A plain space still separates two figures, and a rounded figure ("about 11 300 m") is still unsupported, as `11300`.
- **Bearing direction.** `nearest_airports` returns the bearing from the position to the airport. The tool description said only "bearing", and the brief read 140° as the aircraft's direction from Göteborg Landvetter, placing it southeast of the airport rather than northwest. The tool description and the prompt now state the direction, so the prompt is `investigate.v2`. `investigate.v1.md` stays in the repo because stored events carry that version.
- **Per-minute token limit, measured.** The 2026-11-22 item on tokens predicted it, and two of the three runs hit it. Each tool round resends the conversation, so by the fourth or fifth call one request asks for about 3,300 tokens with 5,500 to 5,900 already used that minute, against a limit of 8,000. The 429 names its wait ("try again in 9.225s"), but the retry went out after the 3 s call spacing, hit the limit again and dropped the provider. The client now reads the wait from `Retry-After` or from Groq's message, whichever is longer, and the agent waits that long before retrying, up to `Llm:MaxRetryWaitSeconds` (30). A longer wait, which a daily limit asks for, moves to the next provider. The one retry is now per call rather than per provider run, since the limit can be hit again later in the same run. The eval harness prints the agent's log lines in live mode; before, a fallback said only "no provider produced a brief".
- **Tests.** 122 backend tests. Turning the normalisation off makes the three new groundedness tests fail, the comma test fails if commas are joined nowhere or in the evidence too, and undoing the wait, the cap and the per-call reset makes the three new retry tests fail. One more pins the bearing direction and six cover reading the retry hint.
