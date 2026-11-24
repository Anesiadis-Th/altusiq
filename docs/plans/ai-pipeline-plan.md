# AI Pipeline Plan

Status: in progress, started 2026-11-22 on `feature/emergency-squawk-events`.

The squawk detector, investigation agent and eval harness from [ADR-018](../adr/018-emergency-squawk-events-and-investigation.md) already exist on this branch. The plan is to make that pipeline defensible before adding anything new: deploy it, give it ground truth, and pick models and modes by measurement.

Rules that apply to every step: no new recurring cost, never describe the system as streaming (it is polled, then pushed), never frame anything as a safety tool, and no claim that has not been measured.

## Current state and known problems

| # | Problem | Where |
|---|---|---|
| 1 | `llama-3.3-70b-versatile` is listed on Groq's deprecations page with a 2026-08-16 shutdown. A decommission error is a non-transient 4xx, so each event would spend a budget call on Groq before falling through to OpenRouter. The model id is duplicated in the eval CLI. | `backend/appsettings.json`, `backend/AltusIQ.Evals/Program.cs` |
| 2 | The tool loop resends the whole conversation every round (3 to 4 calls per case). With a full trail that is roughly 8K to 12K input tokens inside a minute, against Groq's free 8K tokens per minute. | `backend/Investigation/InvestigationAgent.cs` |
| 3 | The groundedness check only covers 4-letter capitals and integers of 100 or more. It misses IATA codes and small numbers, flags correct derived numbers, and passes any number that appears anywhere in the evidence. | `backend/Investigation/GroundednessChecker.cs` |
| 4 | All 8 eval fixtures are hand-written, and every trail is timestamped about a year before its event. | `backend/AltusIQ.Evals/cases/*.json` |
| 5 | CI never runs the replay. The live cap (20 calls) is below what the recorded scripts use (23), and capped cases count as schema-invalid. A live nightly run shares the production OpenRouter quota (50 per day). | `.github/workflows/`, `backend/AltusIQ.Evals/EvalRunner.cs` |
| 6 | What the model saw is not persisted: tool outputs, tools called, call count and ungrounded tokens are dropped, so real events cannot be audited or replayed. | `backend/Investigation/InvestigationResult.cs` |
| 7 | A restart loses the detector state, the queue and the budget counter. Nothing re-queues `pending` events or closes orphaned `open` events at startup. | `backend/Background/InvestigationService.cs` |
| 8 | The budget stamps the last-call time before computing the wait, so every call waits about 3 s, including the first. | `backend/Investigation/LlmCallBudget.cs` |
| 9 | The README describes the live map as "streaming". | `README.md` |

## Free options (checked 2026-11-22)

| Option | Limits and terms | Use |
|---|---|---|
| Groq free | `gpt-oss-120b`, `gpt-oss-20b`, `qwen3.8-27b`: 30 RPM, 1K RPD, 8K TPM, 200K TPD | Primary |
| OpenRouter free | 20 RPM, 50 RPD under $10 of lifetime credits; separate privacy toggle for free models that may train on inputs | Fallback |
| Gemini free | "Paid Services only" for API clients serving users in the EEA, Switzerland or the UK; free inputs used for training | Not used |
| Cloudflare Workers AI | 10,000 neurons per day, then errors; about 40 single-shot briefs | Possible third fallback |
| GitHub Models | Retired 2026-07-30 | Not used |
| Ollama (local) | OpenAI-compatible, tools and JSON mode; set `num_ctx` in a Modelfile | Evals only |

Also confirmed: OpenSky credits are three independent 4,000 per day buckets and `/flights/all` covers 2 h per call; aviationweather.gov METAR accepts `date` and `hours` over the last 30 days at 100 requests per minute. OpenSky's terms page could not be loaded and should be read before sending OpenSky-derived trails to providers that may train on them.

## Steps

### Step 0: harden before merging

- [x] Swap the Groq model to a live one (`openai/gpt-oss-120b`) and read the eval CLI's provider table from `appsettings.json` instead of a copy.
- [x] Persist an investigation trace per event: tool outputs, tools called, model calls, ungrounded tokens.
- [x] Startup sweep: close events left `open` by a previous process and re-queue every `pending` event.
- [x] Fix the budget so the spacing wait is measured from the previous call.
- [x] Shift fixture trail timestamps onto their event times.
- [x] Run the replay eval in CI as a gate.
- [x] Eval budget per case; cases that run out are reported as `budget_exhausted` and excluded from the rates; results record the git commit.
- [x] Live evals only on manual dispatch, so they never share the production quota by accident.
- [x] README wording: "pushed", not "streaming"; refresh test counts and provider names.
- [x] Fix the data-only brief crashing on a missing route or a tool error object.
- [ ] Apply the `AddAnomalyEvents` and `AddInvestigationTrace` migrations to Supabase (nothing applies them at startup).
- [ ] Merge, deploy, and collect two to three weeks of events and the per-poll volume log.

### Step 1: next-day outcome labels

The nightly enrichment already downloads OpenSky's global `/flights/all` batch for each day and keeps only the Scandinavian matches. Reuse it to label each event's outcome (returned to origin, planned destination, elsewhere, unknown) at no extra credit cost, add a structured `trajectory` field to the brief, and score it against the label and against a nearest-airport-ahead baseline. Export real events into eval cases.

### Step 2: local model bake-off, then claim-level groundedness

- Run 2 or 3 Ollama models next to the hosted free models on the same cases; record schema-valid rate, grounded rate, calls, tokens and latency per model and prompt version.
- Compute trajectory facts in code, give them ids, require observations to cite them, and verify citations and numbers in code.
- Run the tool loop and single-shot mode side by side and keep the one the numbers support.

### Step 3: diversion and turnback episodes

Nightly SQL over stored, enriched flights: arrivals that differ from the callsign's usual destination over the last 14 days, and same-airport flights that climbed high. Merge diversions from one airport within about 2 h into one episode and brief each episode with a historical METAR tool. Start this sooner if Step 0's volume log shows only a few squawk events a week.

### Optional

- Learned ETA (gradient boosting on stored tracks) against the current great-circle estimate.
- Holding detection on live trails, after Step 3.

### Not doing

Go-around detection (invisible at 120 s), squawk clustering (too few events), a daily LLM briefing, natural-language filters, a loss-of-contact trigger, Gemini's free tier in production, vector stores or orchestration frameworks.

## Tests per step

- **Step 0:** sweep closes only events created before startup and re-queues `pending` once; trace round-trips through the store; budget waits nothing on the first call and the full spacing on the second; replay runs in CI with zero expectation failures.
- **Step 1:** matcher picks the max-overlap leg; null arrival gives `unknown`; equal departure and arrival gives returned-to-origin; reruns are idempotent and never fetch twice.
- **Step 2:** unknown fact ids rejected; converted units pass within tolerance; underivable numbers and absent IATA codes fail; disabling the check turns the suite red.
- **Step 3:** baseline needs N prior flights and a clear majority; GA and low circuits never flag; episodes merge inside the window only; ids are deterministic across reruns.
