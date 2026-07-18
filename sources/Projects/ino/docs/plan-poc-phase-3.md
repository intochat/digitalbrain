# ino POC Phase 3 — implementation plan

Companion to [`product-vision-final.md`](./product-vision-final.md). Sequenced, testable, concrete.

## Mission

Prove ino's neuron / synapse primitives, reactivity, and UI framework under the realistic load of a 19-neuron Travel domain. Ship fast; make the trip-planning flow feel like a real working product. NeuronML is user-visible. Full seven-aspect neuron inspector.

## Approach — thin vertical slices

Each slice cuts end-to-end: neuron(s) + gateway + Flutter + BDD + E2E. Don't build any layer alone — the whole point of vertical slices is that integration issues surface in the first week, not after month three.

**Per-slice success criteria (from `CLAUDE.md`'s verification loop):**

1. `dotnet build POC/ino.slnx`
2. `dotnet test POC/ino.slnx`
3. `aspire start` — all POC resources Healthy in dashboard
4. Flutter web verified in Chromium; Aspire dashboard shows OTel traces (`grpc Chat`, synapse `fire` / `handle` spans) + Flutter BLoC transitions under `ino-flutter` structured logs
5. Playwright E2E green — gRPC-Web response interception + screenshot

**Context7 is mandatory per slice** before writing code. Library verification is a task inside each slice, not an afterthought.

**Commit discipline.** Granular commits per slice: `feat(poc):` / `test(poc):` / `fix(poc):` / `docs:`. One logical change per commit.

---

## Milestone 1 — Foundation (slices 1–3)

**End state.** Flutter web boots against POC, one flight card renders from a POC neuron, LightGBM recording runs + trains on seeded data, Mandelbrot validated on seed time-series, persona orb reacts to activity rhythm.

### Slice 1 — First pixel end-to-end

**Goal.** Flutter in Chromium loads POC's Flutter bundle, calls `IInoGateway.ChatAsync("find flights to Bali")` via gRPC-Web, renders one RFW flight card.

**Build.**
- New projects: `Ino.Gateway` (interface + implementation in `system` silo), `Ino.Gateway.Grpc` (Kestrel + gRPC + gRPC-Web + static file serving for Flutter bundle).
- Skeleton `FlightSearch` neuron (hard-coded result, no ML yet) inside a new `Ino.Domains.Travel` domain.
- Port `FlightCardTemplate` from `domains/travel/Ino.Travel/UI/` (Phase-2 origin) forward into `POC/domains/travel/Ino.Domains.Travel/`; wire `InoService.TryBuildRfw` equivalent with CRLF-strip.
- Flutter: boot against gRPC-Web; Mind view shows one Travel orb; Chat input fires `ChatAsync`.

**Test.**
- L5 E2E (`GrpcTestFixture` pattern from `test/E2E.Tests/Infrastructure/NeuronE2ETest.cs`) in a new `POC/test/Ino.E2E.Flutter.Tests/` project.
- Playwright intercepts gRPC-Web response, asserts `FlightCard` RFW shape, saves screenshot.

**Context7.** Flutter CanvasKit, Remote Flutter Widgets package, Aspire `DistributedApplicationTestingBuilder` multi-silo + ASP.NET composition, Playwright .NET.

**Done when.** Browser shows one flight card served from a POC neuron; E2E passes; screenshot in `POC/test/Ino.E2E.Flutter.Tests/bin/.../screenshots/`.

---

### Slice 2 — NeuronML LightGBM lift + first visible prediction

**Goal.** `FlightPreferenceModel` records decisions, trains on seeded data, `FlightSearch` re-ranks results by model output, flight cards show confidence bars.

**Build.**
- Port `NeuronOptimizerGrain` + `FeatureArchitectGrain` + `FeatureCatalog` + `DecisionRecord` + `OptimizationResult` + `NeuronOptimizerState` from `src/Core/ML/` → `POC/src/Ino.Core.Hosting/ML/`.
- Add `ML.NET` + `Microsoft.ML.LightGbm` to POC central package versions.
- `FlightPreferenceModel` (new neuron) + `FlightSearch` integration: record decisions, query optimizer before LLM.
- Seed data loader in `Ino.Domains.Travel` — `SeedData/flight-preferences.jsonl` with ~500 fake-but-realistic click/dismiss/book records. Auto-imports on first run.
- Flutter: flight cards render `ino thinks 82%` confidence badge from model confidence.

**Test.**
- BDD port — "Optimizer records and trains after threshold", "Optimizer predicts with high confidence" (from `features/ino-new/InoNew.Tests/Features/NeuronML.feature`).
- E2E — "flight cards render confidence badges when seeded data is loaded".

**Context7.** ML.NET 5.0 preview, LightGBM API surface, Orleans grain persistence for model bytes.

**Done when.** Browser renders flight cards with confidence bars; counters `agents.ml.predictions` / `.retrains` visible in Aspire dashboard.

---

### Slice 3 — Mandelbrot seed validation + persona rhythm

**Goal.** Mandelbrot MF-DFA (or sliding-window Hurst) runs on synapse-firing time-series, detects seed regime shift, output wires to persona `emotion` + `energy`. Rive orb pulses with real fractal rhythm.

**Build.**
- New namespace `POC/src/Ino.Core.Hosting/ML/Mandelbrot/`:
  - `MultifractalAnalyzer` — MF-DFA or windowed-Hurst implementation (Context7 to pick approach).
  - `MandelbrotGrain` — per-neuron analysis grain, consumes synapse-firing time-series.
  - Seed time-series fixtures under `POC/test/Ino.ML.Mandelbrot.Tests/SeedData/` — stable baseline → regime shift → recovery.
- `IPersonaGrain` consumes Mandelbrot output; maps regime + rhythm → `PersonaStateModel.emotion` + `energy`.
- Rive asset wiring per Decision 4 — drop `26076-48718-011y.riv` into `clients/ino.flutter/assets/rive/persona_orb.riv`, bind discovered inputs.
- OTel counters `ino.mandelbrot.analyses` / `.regime_shifts` / `.alerts`.

**Test.**
- BDD — "regime shift detected on synthetic data", "analyzer outputs stable label during baseline".
- E2E — "persona emotion changes when simulated synapse burst fires".

**Context7.** ML.NET time-series API, MF-DFA / Hurst library choices (native C# vs port), Rive Flutter state-machine introspection.

**Done when.** Running the seed time-series through `MandelbrotGrain` detects the regime shift; persona orb visibly changes during the activity burst in the browser.

---

## Milestone 2 — Reactivity (slices 4–6)

**End state.** Multi-neuron synapse chain visible, monitoring neurons fire live reactive updates, inspector drawer up with four panels.

### Slice 4 — Inspector drawer (Identity + State + Metrics + ML panels)

**Goal.** Tap a Travel neuron in Mind → drawer opens with four of the seven aspect panels.

**Build.**
- `IInoGateway` introspection methods: `GetJournalAsync(GrainId)`, `GetMetricsAsync(GrainId)`, `GetMlStateAsync(GrainId)`.
- Flutter drawer widget routed from Mind orb tap; four panel widgets.
- State panel scrubs journal events (typed payload JSON + timestamp).
- ML panel renders LightGBM confidence bars + Mandelbrot α(q) mini-spectrum.
- Metrics panel renders sparklines from per-neuron counters.

**Test.** E2E — "drawer renders `FlightSearch` details with journal + metrics + ML".

**Context7.** Flutter ScaffoldMessenger / bottom-sheet patterns, Rive-independent chart widgets.

**Done when.** Drawer opens in browser, panels populated from real grain state.

---

### Slice 5 — Multi-specialist synapse chain

**Goal.** Trip request fans out to parallel specialists; synapse chain renders in Trace; composed itinerary card in Live.

**Build.**
- `PlanTripRequest` synapse type.
- `ItineraryComposer` neuron — receives `PlanTripRequest`, `ctx.FireBroadcast<FlightSearchRequest>` + `ctx.FireBroadcast<HotelSearchRequest>`, composes results.
- `HotelSearch` neuron + `HotelPreferenceModel` (LightGBM over hotel features: rating / price / amenity set).
- Port `HotelCardTemplate` from `domains/travel/Ino.Travel/UI/` (Phase-2 origin) into `POC/domains/travel/Ino.Domains.Travel/`.
- Flutter Live view renders multi-card itinerary progressively as synapses arrive; Trace view shows the synapse chain.

**Test.**
- BDD per new neuron.
- E2E — "trip plan request renders flights + hotels progressively; Trace shows 3 synapse fires".

**Done when.** Browser shows a multi-card trip plan; Trace view shows `PlanTripRequest` → `FlightSearchRequest` / `HotelSearchRequest` → responses chain.

---

### Slice 6 — Monitoring (FlightMonitor + Scheduling panel)

**Goal.** `FlightMonitor` uses Orleans reminder (scaled down to 15s for demo), fires `FlightDelayed` → flight card mutates reactively in Live. Scheduling panel (aspect 5 of 7) added to inspector.

**Build.**
- `FlightMonitor` neuron — `IRemindable`; `RegisterOrUpdateReminder` on activation; on fire, checks simulated airline API; may fire `FlightDelayed` / `GateChanged` synapses.
- RFW flight card subscribes via `StreamEventsAsync` to reactively mutate.
- `IInoGateway.GetSchedulingAsync` returns reminder name + period + next-fire + recent-fire history.
- Flutter Scheduling panel widget.
- Persona orb pulses amber on `FlightDelayed` synapse fire (via `signalPulse`).

**Test.**
- BDD — "FlightMonitor fires FlightDelayed when simulated delay present".
- E2E — "simulate delay, card updates live, persona pulses, Scheduling panel shows recent fire".

**Context7.** Orleans 10 reminders API, `IRemindable` + `RegisterOrUpdateReminder` semantics, reminder storage for in-memory cluster.

**Done when.** Live card mutates in browser without page reload when monitor fires; Scheduling panel shows the reminder state.

---

## Milestone 3 — Travel depth (slices 7–10)

**End state.** All 19 Travel neurons shipped; Travel feels like a real working demo.

### Slice 7 — Remaining preference models

`CuisinePreferenceModel` + `ActivityPreferenceModel`. Each with seeded decisions. Bias future `RestaurantSearch` / `PlaceSearch` results.

### Slice 8 — Remaining monitoring + check-in reminder

`WeatherMonitor` (6h reminder, fires on significant forecast change) + `PriceMonitor` (uses Mandelbrot to detect downward price regime → `PriceDropped`) + `CheckInReminder` (Orleans reminder 24h before departure).

### Slice 9 — Search specialists

`PlaceSearch` + `RestaurantSearch` + `TransportPlanner`. RFW cards (port `PlaceCardTemplate` + `DestinationCardTemplate` from `domains/travel/Ino.Travel/UI/` (Phase-2 origin) into `POC/domains/travel/Ino.Domains.Travel/`; write restaurant card + transport card fresh).

### Slice 10 — Memory + booking + export

`TravelMemory` (journal of past trips; feeds preference models) + `LocalTipsAggregator` (crowdsourced tips at destination) + `BudgetTracker` (estimate + warn on overage) + `BookingCoordinator` (stubbed transactions) + `ItineraryExporter` (calendar / PDF / share).

---

## Milestone 4 — Polish + visibility (slices 11–14)

### Slice 11 — Full 7-aspect inspector

Add Reasoning + Actions + Integrations panels to the drawer:
- Reasoning: last LLM prompt/response, token cost, `mocked via BDD` badge + scenario name in dev.
- Actions: interface methods list + recent invocations + success rate.
- Integrations: DI-injected service list visible per neuron.

### Slice 12 — BDD-mocked LLM wiring

`BddMockChatClient` extending POC's `RecordedMockChatClient`. Scans `.feature` scenarios for prompt-example → response pairs. Config key `Ino.Llm.Provider` = `bdd-mock` | `azure-openai` | `anthropic`. Inspector Reasoning panel shows scenario-match badge when bdd-mock is active.

### Slice 13 — Missed-intent plumbing + Gaps view

`UnroutedIntent` synapse; Cortex logs on unmatched route + returns user-visible message; timeline query endpoint; Gaps panel in inspector showing per-user missed intents ranked by frequency.

### Slice 14 — Marketplace tile + install/uninstall demo

`Ino.Domains.Travel` listed in `~/.ino/marketplace.json`. Marketplace tile in persona launcher drawer renders `GET /installed` + `GET /available`. Demo: uninstall Travel → Mind empty → reinstall → domains silo restarts visibly in Aspire dashboard → Travel orb returns.

---

## Milestone 5 — Surface expansion (slices 15–16)

### Slice 15 — Telegram mini-app bridge

Legacy `src/Telegram/` keeps running; `WebhookSetupService` updated so `/app` button's `WebAppInfo(url)` points at POC Flutter bundle's public URL. `/ino` command handler proxies to `IInoGateway` via gRPC.

### Slice 16 — ino-windows desktop migration

Add `ino-windows` resource to POC `Ino.AppHost` (with `WithExplicitStart`). Wire OTLP/HTTP → POC Aspire HTTP endpoint. Flutter-desktop build connects to POC silo via gRPC. Legacy Aspire config stops referencing desktop.

---

## Cross-cutting concerns

### Aspire CLI discipline

From `CLAUDE.md`: **never** `dotnet run --project`, **never** `aspire run`. Always:
- `aspire start` / `aspire stop` for full topology
- `mcp__aspire__execute_resource_command(resourceName=..., commandName="rebuild" | "restart" | "stop" | "start")` for individual resources

### Issue #12 status updates

Each slice that lands a neuron aspect updates:
- The `Mapping to current state` table in `docs/neuron-unified-vision.md` (POC status column)
- The Roadmap checkboxes in issue #12

### Branch + PR cadence

- **Phase A PR** (branch `feature/poc-phase-3-flutter-ml-e2e`, this branch): `docs/product-vision-final.md` + `docs/plan-poc-phase-3.md`. Merge before Phase B.
- **Phase B PRs** (one per milestone, or per 2–3 slices if review bandwidth allows): branched from master after Phase A lands.

### Verification loop — never skip

For every slice, before committing:
1. Build — `dotnet build POC/ino.slnx`
2. Unit / BDD — `dotnet test POC/ino.slnx`
3. Integration — `aspire start`, confirm every resource Healthy in dashboard
4. Flutter in browser — open `http://localhost:<gateway-port>`, drive the scenario, check Aspire Structured Logs (filter `ino-flutter` → BLoC transitions) and Traces (`grpc Chat`, `fire` / `handle` spans linked by `traceparent`)
5. E2E — `INO_E2E_NO_BROWSER=true dotnet test POC/test/Ino.E2E.Flutter.Tests --filter "Category=E2E"`
6. Regression sweep — remaining unit + BDD last, as safety net

Type-checking and test suites verify code correctness, not feature correctness. If the UI can't be driven in a browser, say so explicitly — never claim success on build + test alone.

### Out of scope for Phase 3

Everything listed under Post-v0.1 epics in `product-vision-final.md`. Don't accidentally let scope creep into:

- Cortex self-creation (L1/L2/L3)
- Cross-user missed-intent aggregation
- Full multifractal spectrum research
- Revenue model decisions
- Additional domains beyond Travel
- `src/` deletion / full migration

If a Phase 3 review comment pushes toward any of these, defer with a link to the post-v0.1 epic list.

---

## Timeline estimate (informational, not binding)

- Milestone 1 (slices 1–3): ~1 week if Context7 cycles land clean
- Milestone 2 (slices 4–6): ~1 week
- Milestone 3 (slices 7–10): ~2 weeks (batches of neurons per slice)
- Milestone 4 (slices 11–14): ~1 week
- Milestone 5 (slices 15–16): ~3 days

**Total Phase 3: ~5–6 weeks** to v0.1 ship. Tight but achievable because neurons are cheap and the primitive amortizes per-neuron cost. Actual cadence may flex as Context7 verification cycles land.
