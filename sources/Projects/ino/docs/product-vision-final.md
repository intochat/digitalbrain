# ino — product vision, final (v0.1)

Locked output of the Phase A brainstorm on 2026-04-21. This document is load-bearing — every Phase 3 scope decision traces back here. The companion plan is [`plan-poc-phase-3.md`](./plan-poc-phase-3.md).

## Mission

ino v0.1 is a **personal AI assistant first**, with the OS-layer substrate underneath and a dev platform falling out for free. All three layers ship working at once in v0.1 — not staged — so the demo proves the neuron/synapse primitives under realistic load. The canonical flow is **trip planning**: it exercises routing, multi-specialist composition, reactive monitoring, and visible machine learning inside one narrative.

**Shipping discipline:** ship fast, test design / reactivity / neuron-synapse behavior. If a scope item does not validate those things in a trip-planning flow, it can slip.

**Killer feature:** visible local ML. Users watch ino learn their preferences; the persona orb's mood and energy are driven by live multifractal analysis of their own activity. Nobody has shipped a UI mascot driven by real-time fractal math on user behavior.

---

## Thirteen load-bearing decisions

### 1. Product shape

**Decision.** Personal AI assistant first, OS-layer substrate underneath, dev platform falling out for free. All three layers ship working at once in v0.1.

**Why.** User: *"ship poc all working at once"*. A single cohesive demo beats an MVP that gets layers bolted on later.

**How to apply.** Every v0.1 deliverable makes all three layers visible in the demo: assistant-facing UX, substrate observability (traces, journal, ML), and platform hooks (domain install / uninstall).

---

### 2. POC vs legacy `src/`

**Decision.** POC replaces `src/` over time. v0.1 ports only what the Travel demo needs. Freeze `src/` post-v0.1 — no new features there; delete once POC surpasses it. One carveout: `src/Telegram/` stays alive during v0.1.

**Why.** Clean greenfield discipline; the POC's point is the right primitives, not a kernel migration.

**How to apply.**
- **Port forward into POC for v0.1:** LightGBM `NeuronOptimizerGrain` + `FeatureArchitectGrain`, Cortex (minimal form), RFW template infrastructure + travel card templates, 19 Travel neurons, ~500 seeded preference decisions.
- **Carveout:** `src/Telegram/` bot stays as-is; its WebApp URL points at the POC Flutter web bundle.
- **Archive:** everything else in `src/` — reference only, no new features.

---

### 3. Client surfaces

**Decision.** Three surfaces in v0.1, all consuming the same Flutter codebase.
- **Flutter web** — POC backend serves `build/web`; primary demo surface.
- **Telegram mini-app** — legacy `src/Telegram/` stays running; `WebAppInfo(url)` points at POC Flutter bundle.
- **ino-windows desktop** — Flutter-desktop client stays wired as-is, connects to POC silo via gRPC + OTLP.

**Why.** User wants to keep the Telegram mini-app and desktop surfaces because they already work. Zero port cost to keep them — same codebase, different deployment targets.

**How to apply.**
- Dropped from v0.1: Blazor DevUI, standalone Flutter mobile build.
- `src/Telegram/` and `ino-windows` are legacy carveouts; full migration is post-v0.1.
- All three surfaces use the same `IInoGateway` transports.

---

### 4. Persona rendering

**Decision.** Ship the marketplace Rive asset (`D:\ino\26076-48718-011y.riv` → `clients/ino.flutter/assets/rive/persona_orb.riv`). Keep CustomPaint as automatic fallback on load failure.

**Why.** Richer than CustomPaint, ships immediately, fallback keeps demo robust.

**How to apply.**
- Drop the `.riv` into assets, declare in `pubspec.yaml`, set `PersonaStateModel.riveAssetUrl`.
- First load introspects state machines + inputs via Rive runtime; bind discovered inputs to BLoC state (mood ≈ emotion, energy ≈ energy, pulse ≈ signalPulse). Unmatched inputs use Rive defaults.
- **Claude cannot edit `.riv` files** — they are binary and require the Rive editor. Extending the state machine (adding `trigger_searching_flights`, etc.) is a design-tool task done by a human.

---

### 5. RFW vs native Flutter widgets

**Decision.** Split by role.
- **Native Flutter:** app chrome (Mind / Live / Trace, tab bar, persona drawer, themes, router) — never changes per domain flow.
- **RFW:** domain card surfaces (flight card, hotel card, place card, itinerary card) — lets domains ship UI without Flutter rebuilds.

**Why.** The RFW promise of "new domain behavior lands without Flutter rebuild" matters for the part of UI that varies by domain. App chrome is invariant → native is simpler and more debuggable.

**How to apply.**
- Port `FlightCardTemplate` / `HotelCardTemplate` / `PlaceCardTemplate` / `DestinationCardTemplate` from `domains/travel/Ino.Travel/UI/` forward.
- Keep the CRLF gotcha fix: `InoService.TryBuildRfw` strips `\r` before sending — Dart RFW parser rejects Windows CRLF.
- Boundary is a single `RfwWidget` in the Live view's card slot.
- ML prediction visualizations inside the inspector drawer are also RFW so specialists can declare their own.

---

### 6. Cortex routing

**Decision.** One Cortex neuron in the `system` silo. Minimal routing — single-synapse NL → typed synapse. No decomposition, no BehaviorMemory search, no self-creation. **Never create `<Domain>Cortex` per domain flow.**

**Why.** Installed domains become routable automatically because their synapse types register in `IDiscovery`. Cortex just reads the catalog and maps NL → one typed synapse. The primitive doesn't change shape when decomposition / memory / self-creation land in later phases.

**How to apply.**
- `Cortex : Neuron<CortexEvent>` lives in `Ino.System` or a system-silo neighbor.
- Prompts LLM with `IDiscovery.DumpAsync()` catalog; response is a typed synapse to fire via `ctx.Fire<T>()`.
- For the trip-planning demo, Cortex routes `"plan me a trip to Bali"` → `PlanTripRequest` → `ItineraryComposer` (which fans out internally).

---

### 7. LLM driving surface

**Decision.** One `IInoGateway` interface, three transports. gRPC + MCP required in v0.1; CLI optional and cheap.

**Why.** Phase 3 source generator can target one interface to emit MCP tool schemas + gRPC protos + CLI subcommands. No hand-written drift.

**How to apply.**
- New projects: `Ino.Gateway`, `Ino.Gateway.Grpc`, `Ino.Gateway.Mcp`, optionally `Ino.Gateway.Cli`.
- Interface methods: `ChatAsync`, `FireAsync<T>`, `ListDomainsAsync`, `StreamEventsAsync`, plus introspection methods for the inspector: `GetJournalAsync`, `GetMetricsAsync`, `GetMlStateAsync`, `GetSchedulingAsync`, `GetIntegrationsAsync`.
- Implementation lives in the `system` silo, backed by `IFirePort` + `IDiscovery`.
- Telegram (legacy carveout) calls the gRPC transport for all NL routing.

---

### 8. One vocabulary

**Decision.** Domains, neurons, and synapses are the only product/runtime vocabulary. Users may see friendly domain names, but the architecture does not introduce another unit between a domain and its neurons.

- **End users see domains and outcomes.** Mind / Live / Trace copy, notifications, and tooltips speak in domain terms: *"Travel noticed your flight was delayed"*, *"ino learned your hotel preferences"*.
- **Builders see neurons and synapses.** The creator-mode drawer reached via the persona launcher's "Neurons" / "Synapses" tiles speaks in neuron / synapse / journal terms.

**Why.** Keeping a second product term caused the codebase to grow a parallel routing/planning model. The architecture is simpler and more honest if domains own neurons and synapses directly.

**How to apply.** Any visible label, Aspire dashboard pane, BDD scenario description, notification, or tooltip uses domain/neuron/synapse terms. Audit copy during review. In code, `IDomain` expresses the installable bundle and neuron/synapse metadata expresses routable behavior inside that domain.

---

### 9. NeuronML — user-visible killer feature

**Decision.** LightGBM + Mandelbrot both ship in v0.1 with two user-visible use cases, both inside Travel.

**Use case 1 — FlightSearch personal preferences (LightGBM, specialist-level).** FlightSearch records click / dismiss / book decisions. Features: airline, price bucket, duration bucket, stops, depart time-of-day, day-of-week. Model re-ranks future results by learned style. Flight cards show `ino thinks 82%` confidence bars.

**Use case 2 — Activity rhythm → persona mood (Mandelbrot, persona-level).** MF-DFA runs on synapse firing time-series. Detects focus bursts, evening calm, morning acceleration. Output drives `PersonaStateModel.emotion` + `energy`. The Rive orb literally pulses with multifractal rhythm.

**Why.** User: *"real ultra-small ml parts is a killer feature nobody did before"*. Visibility IS the product. Infrastructure-flavored use cases (retrain cadence, graceful degradation) are not the right framing — pick use cases the user can see learning happen on.

**How to apply.**
- Port `NeuronOptimizerGrain` + `FeatureArchitectGrain` from `src/Core/ML/` to `POC/src/Ino.Core.Hosting/ML/`. Every `Neuron<TEvent>` gets a companion optimizer grain.
- Implement Mandelbrot MF-DFA or windowed-Hurst (~200–400 LOC; Context7 to pick library or port).
- **Seeded preference data:** `domains/travel/Ino.Travel/SeedData/` ships ~500 fake-but-realistic prior decisions so models are confident on day one. Real user data trains on top.
- OTel counters: `agents.ml.predictions`, `agents.ml.fallbacks`, `agents.ml.retrains`, `ino.mandelbrot.analyses`, `ino.mandelbrot.regime_shifts`, `ino.mandelbrot.alerts`.
- Full multifractal spectrum research (wavelet leaders, α(q)↔f(α)) is post-v0.1. Minimal + demonstrable = detector runs on seed data, fires regime-shift signal, drives two decisions.

---

### 10. Neuron inspector drawer — full 7-aspect view

**Decision.** Creator-mode drawer reached from Mind (tap neuron) or Trace (tap row). Shows all seven aspects from `neuron-unified-vision.md`.

| # | Panel | Content |
|---|---|---|
| 1 | Identity | Grain id, canonical target, domain, declared capabilities |
| 2 | State — multi-state | Journal (scrubbable) + any projected `IDurableDictionary` / `IDurableList` side state the neuron declares |
| 3 | Reasoning | LLM persona + model + last prompt/response, token cost; dev shows `mocked via BDD` badge with the matched scenario name |
| 4 | Actions | Interface methods (from `INeuron<>` / `IReactsTo<>`) + recent invocations + success rate |
| 5 | Scheduling | **Live Orleans reminders** (name, period, next-fire countdown) + grain timers + fire history |
| 6 | Integrations | DI-injected services visible per neuron (HttpClient bases, MCP clients, external APIs) |
| 7 | Metrics + ML | Counter sparklines; LightGBM training-record count + recent prediction confidence bars; Mandelbrot α(q) mini-spectrum + regime label (`stable` / `bursty` / `drifting`) |

**Why.** User: *"full neuron visualization! with all what's happening!"* Orleans reminders aspect is genuinely load-bearing (monitoring neurons use them), not panel-for-show.

**How to apply.**
- Drawer is native Flutter chrome; ML panels can be RFW so specialists ship their own prediction visualizations.
- Gateway methods (per Decision 7) back every panel.
- Audience: builders / debuggers only. End users won't see it unless they open the creator drawer explicitly.

---

### 11. BDD-mocked LLM for dev

**Decision.** `BddMockChatClient` (extends POC's `RecordedMockChatClient`) matches prompts against `.feature` scenario examples and returns canned responses. Production flips to real `IChatClient` (Azure OpenAI / Anthropic) via config.

**Why.** User: *"you can mock llm responses with bdd responses for dev purposes and when it's ready — id just switch to preferred models in the cloud"*. Dev loop stays instant and reproducible; BDD scenarios are dual-purpose specs + dev mocks.

**How to apply.**
- Config key `Ino.Llm.Provider` = `bdd-mock` | `azure-openai` | `anthropic`.
- `.feature` files under `POC/domains/.../Features/` declare scenario examples used for both BDD tests and prompt-matching.
- Inspector's Reasoning panel shows `mocked via BDD` badge + the matched scenario name when `bdd-mock` is active.

---

### 12. Self-improvement gate

**Decision.** No Cortex self-creation in v0.1. Ship the missed-intent plumbing (seeds post-v0.1 work); defer L1 / L2 / L3 self-creation.

**v0.1 behavior.** When Cortex can't route: log `UnroutedIntent` to timeline, return user-visible *"I don't have a specialist for that yet"* message. Per-user Gaps view in the inspector drawer lists missed intents ranked by frequency.

**Why.** Travel has a fixed specialist set → self-creation adds no v0.1 demo value. L1 / L2 / L3 deserves its own brainstorm. The missed-intent log is the seed for the post-v0.1 loop.

**How to apply.**
- `UnroutedIntent` synapse type: `{ rawText, correlationId, timestamp, rejectedReason }`.
- Timeline logging via the existing journal primitive.
- Gaps view: timeline query on `UnroutedIntent`, rendered in Flutter.
- **Cross-user aggregation + marketplace demand + default-build promotion = post-v0.1 epic** ("Missed-intent marketplace loop") — needs identity silo, anonymization pipeline, developer portal, governance model.

---

### 13. Travel domain — 19 neurons as real working demo

**Decision.** Travel ships ~19 production-feel neurons grouped by role. Reactivity is load-bearing — monitoring neurons fire live synapses visibly.

| Role | Neurons | Count |
|---|---|---|
| Planning + decomposition | `ItineraryComposer`, `FlightSearch`, `HotelSearch`, `PlaceSearch`, `RestaurantSearch`, `TransportPlanner` | 6 |
| Preference models (LightGBM) | `FlightPreferenceModel`, `HotelPreferenceModel`, `CuisinePreferenceModel`, `ActivityPreferenceModel` | 4 |
| Monitoring (Orleans reminders) | `FlightMonitor`, `WeatherMonitor`, `PriceMonitor`, `CheckInReminder` | 4 |
| Booking / export | `BookingCoordinator`, `ItineraryExporter` | 2 |
| Memory | `TravelMemory`, `LocalTipsAggregator`, `BudgetTracker` | 3 |

**Why.** User: *"travel must feel like real working demo rather than poc"*. 19 narrow neurons tests the primitive at scale — neurons are cheap (design principle), each stays ~50–200 LOC.

**How to apply.**
- Entry point: system-level Cortex → `PlanTripRequest` → `ItineraryComposer` (internal fan-out).
- Each neuron is BDD-covered; ML-wired where it makes sense.
- End-user framing: *"Travel can: plan trips · find flights · remember your preferences · monitor flights · notify on gate changes · track budgets"* — sub-neurons invisible unless the creator drawer is open.
- Hotel / Place / Destination specialists ship in v0.1 (per this decision) — overrides the Q2 "one specialist only" framing.

---

### 14. Marketplace — thin v0.1 slice

**Decision.** Wire Travel through the Phase 2 marketplace HTTP scaffold. List Travel in `~/.ino/marketplace.json`. Add a "Marketplace" tile to the persona launcher drawer. Uninstall → reinstall demo works end-to-end.

**Deferred past v0.1.** Consent flow (501 stub kept), developer submission portal, ratings/reviews/search, revenue model decision, cross-user aggregation.

**Why.** Install *primitive* is what v0.1 validates. Revenue model needs real user data before a decision is meaningful.

**How to apply.**
- `GET /marketplace/available` + `GET /marketplace/installed` drive the drawer tile.
- `POST /marketplace/install/Ino.Domains.Travel` triggers the Phase 2 domains-silo restart hook.
- Demo moment: uninstall Travel → Mind goes empty (no domains = no orbs) → install → silo restarts visibly in Aspire → Travel orb returns with all 19 neurons.

---

## Post-v0.1 epics (captured, not scheduled)

Listed here so they don't get lost and so Phase 3 work doesn't accidentally prejudge them:

- **Missed-intent marketplace loop** — cross-user anonymous aggregation of unrouted intents, NeuronML clustering on intent embeddings, developer-portal demand dashboard, install-threshold → default-build promotion.
- **Cortex self-creation (L1/L2/L3)** — auto-create persisted specialist with visible approval UX (L1), ephemeral reasoning-time C# (L2), human-gated compiled capability (L3).
- **Full multifractal spectrum research** — wavelet leaders, publication-grade α(q)↔f(α) estimation.
- **Revenue model decision** — freemium / paid / dev-fee — after real user data.
- **Additional domains** — Notes, Calendar, Mail, etc.
- **`src/` migration + deletion** — once POC surpasses every legacy capability.
- **Telegram + ino-windows full migration** — move off legacy Aspire config entirely.

---

## Two audiences — one final reminder

Every artifact shipped in Phase 3 should pick its vocabulary deliberately:

- Mind / Live / Trace / notifications / copy / tooltips / Aspire dashboard panel names meant for end users → **domain vocabulary** (*Travel, preferences, trips, flight delay*).
- Creator drawer / neuron inspector / BDD scenarios / source code / dev docs → **neuron/synapse vocabulary** (*FlightMonitor, PlanTripRequest, journal, canonical target*).

Mixing the two breaks both surfaces. Audit during review.
