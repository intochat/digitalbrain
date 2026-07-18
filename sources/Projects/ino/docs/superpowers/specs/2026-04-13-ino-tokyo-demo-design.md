# ino v0.1 — Tokyo trip demo (design)

**Date:** 2026-04-13
**Branch base:** master @ 421d95d (PR #5 merged)
**Goal:** a working end-to-end demo where a user plans a Tokyo trip through the ino Flutter web app and the whole neuron/synapse architecture visibly carries the experience — including a live Rive persona that reflects the live synapse graph.

This spec captures the design decisions reached in brainstorming. The implementation plan is derived from it via `superpowers:writing-plans`.

---

## 1. North-star flow

User opens the Flutter web app (served by the `telegram` resource on Aspire, rendered in Telegram webview or any browser) and types:

> *"Plan me 5 days in Tokyo, mid-May, mid-range budget, I like food markets."*

Expected flow over 4–6 turns:

1. **Destination overview card** — Tokyo hero + 3 neighborhood suggestions with "why this fits you" lines sourced from persona preferences. Inline follow-up chips: "Show flights", "Pick hotels", "Build itinerary".
2. **Flight list card** — 5 options with airline/price/duration; one carries a "best for you" badge rationalised from persona.
3. **Hotel list card** — 4 hotels near the chosen neighborhood, distance-to-food-market annotated, persona tag.
4. **Day-by-day itinerary card** — 5 days, each with 3–5 timeline entries; card body composed at runtime by `ItineraryComposerNeuron` (L1 Roslyn script) from flight arrival + hotel location + place results, NOT hardcoded.
5. **Conversation thread** — every card above stays visible in an append-only scrollable list; no replace-in-place.
6. **Persona memory across sessions** — close the app, reopen, type *"Find me another weekend getaway"*. The assistant's response references the earlier Tokyo preferences ("Given you liked the food markets…"), proving cross-session memory came through a live persona read.
7. **Persona presence** — a live Rive orb (with CustomPaint fallback) above the chat thread animates with the live subtree of in-flight synapses: thinks while ino plans, searches while flight-search runs, pulses when hotel-search returns, settles when the itinerary lands.

If Playwright can drive that flow end-to-end, intercept the gRPC-Web frames, assert each RFW card byte-stream, and screenshot the persona orb mid-turn — the mission is done.

---

## 2. Architecture — ino as a neuron

The user-facing persona *is* a neuron. Every chat request lands on `InoNeuron.HandleAsync`. `ino` plans, fires synapses at capability neurons, watches the resulting live subtree, asks clarifying questions when ambiguous, and synthesises the final answer. The persona the user sees is a *projection* of the live synapse graph rooted at `ino`.

### 2.1 Neuron hierarchy

```
creator                         (catalog-only metadata row, no handler)
 └── ino                        (InoNeuron — compile-time root handler)
      ├── travel:recommender    (TravelRecommenderHandler — LLM + tool-as-synapse)
      │    ├── travel:flight-search      (FlightSearchHandler, wraps ISerpApiProviderService)
      │    ├── travel:hotel-search       (HotelSearchHandler,  wraps ISerpApiProviderService)
      │    ├── travel:place-discovery    (PlaceDiscoveryHandler)
      │    └── travel:itinerary-composer (L1 — Neuron.ScriptSource, no handler)
      └── (future peers: insurance:*, health:*, …)
```

- **`creator`** — registry row with `ScriptSource = null`, `ToolRefs = null`, metadata `{ lineage: "root", purpose: "spawns user-facing ino neurons (L3 human-gated in future)" }`. Visible in the catalog without needing L3 wiring. Provides architectural lineage.
- **`ino`** — compile-time `InoNeuron` (lives at `src/Core/Neurons/Ino/InoNeuron.cs` + `InoNeuronHandler : ISynapseHandler`). Stable id `"ino"`. Registry row at startup with `AuthorId = "creator"`, `Metadata["parent"] = "creator"`. ToolRefs = `[ITravelRecommender]` initially (and any future top-level capabilities). `ModelHints` with a concise planner system prompt.
- **`travel:*`** — each registered as a Neuron row with `ToolRefs` (only the grain interfaces the handler is allowed to call, e.g. `ISerpApiProviderService` for flight-search) and `RfwTemplateSource` (a Roslyn template script that builds the per-type RFW card). Compile-time `ISynapseHandler` implementations; the existing `Agent<T>` travel classes stay during transition as thin forwarders so the current 6 travel E2Es remain green until flipped.
- **`travel:itinerary-composer`** — pure L1. Registry row has `ScriptSource` + `RfwTemplateSource` only; no specialist handler. Script fires synapses at flight-search / hotel-search / place-discovery, reads their `SynapseResult.Payload`, composes a typed `ItineraryView`, emits it as its own `SynapseResult.Payload`. The `RfwTemplateSource` script wraps that into the itinerary card RFW bytes. This is the self-improving loop's first live test: a capability that exists only as registry rows, composed at runtime.

### 2.2 Chat pipeline

`src/Telegram/Program.cs` `Chat` RPC:

```
ChatRequest
    │
    ▼
read IPersonaGrain.GetStateAsync("ino") once per turn
    │
    ▼
construct Synapse(target="ino", verb="user.request",
                  correlationId=new, payload={ request, personaContext })
    │
    ▼
Grains.GetGrain<INeuron>("ino").HandleAsync(synapse)
    │
    ▼
SynapseResult  →  ChatResponse { reply, rfw_description, rfw_data, verb, payload }
```

The `Chat` RPC stops knowing about travel entirely. Routing, planning, and domain dispatch live inside `InoNeuronHandler`. Adding insurance later = a new capability neuron + (optionally) an intent classifier branch in ino's planner. No changes to `Chat`, no changes to Flutter.

### 2.3 Persona propagation through synapses

Every synapse `ino` fires carries a `personaContext` field in its JSON payload:

```json
{
  "personaContext": {
    "snapshot": <PersonaBrainState JSON>,
    "parentNeuronId": "ino",
    "correlationId": "<turn-id>"
  },
  "request": { /* neuron-specific payload */ }
}
```

- `InoNeuronHandler` reads `IPersonaGrain.GetStateAsync("ino")` **once** per turn and inlines the snapshot into the first outbound synapse. Downstream handlers propagate the context into their own outbound synapses unchanged.
- `ChatFacade.AskAsync` inside any handler automatically prepends a condensed persona summary to the LLM system prompt when `personaContext` is present. Tone, preferences, and ranking bias flow to every LLM call in the subtree with zero per-handler wiring.
- Every handler deserialises the snapshot and uses it to bias queries (e.g., flight-search filters by morning-departure if the persona `traits["prefers_morning_departures"]` is set) and to generate "best for you" rationales.
- One `IPersonaGrain.GetStateAsync` per chat turn. Not four.

### 2.4 Persona activity = live graph projection

New grain `InoPersonaProjector` (keyed `"ino"`, registered in the silo DI) that:

1. Subscribes to `ITimelineReader` events (may require adding a correlation-id-filtered subscription method — checked during plan-writing).
2. Maintains an in-memory rolling view of **in-flight synapses** (synapses fired but not yet completed) per correlation id.
3. On every new timeline event, recomputes the active subtree and derives:
   - `emotion`: coarse mood (idle / thinking / acting / searching / presenting / confused), derived from whether ino is planning (LLM in flight) vs delegating (child synapse in flight) vs synthesising.
   - `activity` (primary): the *single* most semantically specific active child, mapped from target neuron id by a simple table (`travel:flight-search → SearchingFlights`, `travel:hotel-search → SearchingHotels`, `travel:itinerary-composer → ComposingItinerary`, etc.).
   - `subActivities[]`: the full live set, so the Flutter widget can render multiple satellites orbiting when parallel sub-calls are in flight.
   - `signalPulse`: spike on each new event, decays (already wired in Flutter).
   - `energy`: derived from rate of events over the last 2s.
4. Pushes `PersonaBrainState` updates through the existing `IPersonaObserver` push mechanism (already wired to `StreamPersonaState`).

The existing `PersonaSignalFilter` (from PR #4) **stays** — it emits background persona signals for grain calls outside the ino subtree. The new projector layers the ino-specific correlation-id view on top, specifically for the active turn. Activity derivation is by default; handlers can additionally call `NeuronScriptGlobals.Activity.AnnotateAsync(label, durationHintMs)` for phases that have no synapse (e.g., "reading your preferences" during a local JSON parse), which the projector blends into the derived state.

No handler is required to manually label activity. The projector sees the graph; the graph tells the story.

### 2.5 Clarification mid-flow

New `SynapseResult.NeedsClarification(question, options?, expectedShape)` factory alongside `AuthRequired` and `NeedsEvolution`. When `ino`'s planner (or any child) hits ambiguity, it returns `NeedsClarification`. `InoNeuronHandler` propagates it up. `Chat` RPC maps it to a `ChatResponse` with a special `content_type = "clarification"` carrying the question + options JSON. Flutter renders a compact prompt card. User's answer becomes the next `ChatRequest` with a `replyToCorrelationId` field. ino's next turn re-plans with the answer in context. No persistent pause state.

Scope cut: one-shot clarifications only. Multi-step dialog trees are a future layer.

### 2.6 Durability model

`IReceiver<TMessage>` is dead code and stays dead. Synapses continue to flow via `INeuron.FireAsync/HandleAsync` direct grain calls. Persistence for audit and decay lives in the existing `NeuronRegistryGrain.Synapses` state field. True cluster-wide at-least-once delivery is a future primitive project, deferred until a real failure mode demands it.

Correlation ids propagate through every synapse in a turn (already the case) and are the sole requirement for replay and auditability in the demo.

---

## 3. Schema-driven RFW

`Neuron.RfwTemplateSource` already exists and is executed post-`HandleAsync` by `NeuronGrain` (`src/Core/Neurons/NeuronGrain.cs:164-189`) with a `ScriptRunner<object>` over `NeuronRfwGlobals { Result, Rfw }`. This is the schema-driven composition surface.

- Each travel neuron's registry row carries its `RfwTemplateSource` as a Roslyn script string. The script reads `Result.Payload`, builds a `Rfw.Description` RFW DSL string, binds `Rfw.Data[...]` values, returns. `NeuronGrain` serialises to bytes and attaches to `SynapseResult`.
- The legacy static template classes at `domains/travel/Ino.Travel/UI/*Template.cs` (`FlightCardTemplate`, `HotelCardTemplate`, `PlaceCardTemplate`, `DestinationCardTemplate`) are **replaced** by `RfwTemplateSource` scripts on the corresponding Neuron rows. The C# files are deleted once all travel E2Es are flipped to the runtime path.
- New: `ItineraryCardTemplate` — exists **only** as the `RfwTemplateSource` script on the `travel:itinerary-composer` Neuron row. No C# class.
- Adding a new card type = new registry row with a new script. No Flutter rebuild, no new C# class, no deploy.
- LLM never emits RFW DSL directly. LLMs emit typed JSON payloads; scripts emit RFW DSL.

---

## 4. Decay consolidation (primitive, not travel-specific)

Adds the missing piece to the three-primitive story. Scoped to its own PR-equivalent phase so it doesn't block the demo when cross-checked.

- New `IDecayConsolidationGrain` with an Orleans reminder firing every 6h (configurable via options).
- Sweep walks `NeuronRegistryGrain.Synapses` state (already persisted), applies the decay schedule (100 → 30 → 1 over N days with access boost), drops synapses with `decay == 0`.
- New method `NeuronRegistryGrain.TouchSynapseAsync(id)` boosts decay when a synapse is read on any query path (persona projector, timeline query, search engine).
- Test with a fake clock fixture stepping 30 days forward, asserting decay values.
- The travel flow **uses** the decay field (default Hot on creation) but does not implement the sweep. The primitive is independent of travel.

Scope cut: background ML-driven importance ranking layered on top of decay. That's a future "consolidation also drives what to forget" story.

---

## 5. Flutter client

### 5.1 Chat thread — append-only

`clients/ino.flutter/lib/screens/home/home_screen.dart` refactored to an append-only `ListView` of `ChatMessage` widgets. Each message is either:

- `TextMessage` — plain assistant or user text
- `RfwCardMessage` — a `RemoteWidget` wired to the parsed RFW bytes of that specific turn
- `ClarificationMessage` — prompt card for `NeedsClarification` responses (chips or text field, wires next `ChatRequest` with `replyToCorrelationId`)
- `ErrorMessage` — graceful failure (empty-state card with retry, not a stack trace)

New cards slide in (`AnimatedList` + `SizeTransition` + opacity tween) so accumulation feels organic. No message replaces an earlier message.

### 5.2 Persona orb — Rive primary, CustomPaint fallback

The existing `clients/ino.flutter/lib/persona/persona_widget.dart` already has:
- A sophisticated `CustomPaint` renderer with energy-driven morphing, heartbeat, pulse-on-signal, orbiting dots, searching radar rings, per-emotion colour.
- A Rive slot that currently renders a loading indicator when `riveAssetUrl` is set.
- `PersonaStateModel` fields for `emotion`, `energy`, `signalPulse`, `activeSkillCount`, `currentAction`, `riveAssetUrl`.
- A `timeline_bloc.dart` feed that already pushes signal pulses.

The demo work:

1. **Replace `_RivePlaceholder` with real `RiveAnimation.asset`** (package `rive: ^0.14.5` already in pubspec) wrapped in an error boundary that falls back to the CustomPaint path if the asset is missing, fails to load, or reports a state machine mismatch.
2. **Ship `assets/rive/persona_orb.riv`** (the `assets/rive/` folder is already declared and currently only contains `.gitkeep`). The spec defines the contract the file must fulfil:
   - State machine name: `"Persona"`
   - Number inputs: `mood` (0..1 mapped from emotion), `energy` (0..1), `pulse` (0..1 momentary).
   - Trigger inputs: one per `PersonaActivity` enum value (`trigger_searching_flights`, `trigger_searching_hotels`, …) fired by the `RiveController` when the activity changes.
   - The current CustomPaint renderer stays wired in parallel as fallback — if the `.riv` is absent or the state machine contract fails validation, the widget renders CustomPaint automatically.
3. **Source the `.riv` file**: during plan execution the user supplies (or the plan sources) a `.riv` that fulfils the contract. If none available when the execution step runs, the fallback is the shipping visual and the demo proceeds without blocking. The plan gates the Rive-specific E2E assertion on asset presence.
4. **Add the `activity` enum to the Flutter `PersonaStateModel`** and the gRPC `PersonaState` proto (both sides). Populate from the projector. The widget maps enum → Rive trigger + per-activity label shown in `_StatusLine`.
5. **Multiple satellites** — the existing orbiting-dot renderer already handles `activeSkillCount`. Extend to render per-`subActivity` glyphs (plane, bed, map pin, sparkle) instead of anonymous dots when `subActivities[]` is non-empty.

### 5.3 New Flutter message type — ClarificationCard

Small widget: question label + option chips or a compact text field. On tap/submit, issues the next chat request with `replyToCorrelationId` set so the backend knows this is a resume.

---

## 6. Verification loop (unchanged from CLAUDE.md but explicitly followed per phase)

After every phase:

1. `dotnet build ino.slnx`
2. `aspire start` (or per-resource rebuild if already running: `mcp__aspire__execute_resource_command(resourceName="...", commandName="rebuild")`).
3. Drive the scenario via `mcp__iaw__assistant_chat`. Confirm structured logs + `gen_ai.*` traces for the ino subtree.
4. Cross-check Aspire dashboard: `ino-flutter` BLoC transitions, gRPC traces, persona state stream events.
5. `dotnet test ino.slnx` as the regression net.
6. For Flutter changes: `flutter build web --no-tree-shake-icons`, copy to `src/Telegram/wwwroot/`, rebuild `telegram` resource, reload browser, verify OTLP traces flow.

End-to-end acceptance: `tests/E2E.Tests/Travel/TokyoTripPlanningE2E.cs` (new) mirrors `NeuronE2ETest`, drives the full Tokyo flow via `ToolCallingMockChat`, intercepts gRPC-Web frames, asserts each RFW card's byte stream, saves screenshots.

---

## 7. Phase list (written by plan; acceptance per phase here)

**A — InoNeuron + travel migration to runtime.** `InoNeuron` + `InoNeuronHandler` compile-time with registry row. `TravelRecommenderHandler` + four capability handlers registered. `Chat` RPC fires synapses at `"ino"` only. Existing 6 travel E2Es flipped to the synapse entry path (assertions unchanged — they still verify multi-tool LLM flow).
*Acceptance:* all existing travel E2Es green on the synapse path; new `InoNeuronIsRoot` test asserts chat → ino → travel:recommender → travel:flight-search timeline chain via `ITimelineReader`.

**B — Decay consolidation primitive.** `IDecayConsolidationGrain` + reminder + sweep + `TouchSynapseAsync`. Isolated tests with fake clock.
*Acceptance:* new `DecaySweepTests` pass; ino demo still green.

**C — ItineraryComposerNeuron (L1).** Registry row with `ScriptSource` + `RfwTemplateSource`. Script fires synapses at flight-search / hotel-search / place-discovery, composes `ItineraryView`, template wraps RFW bytes.
*Acceptance:* `PlanFullTokyoTrip_RendersItineraryCard` E2E (new) asserts the itinerary RFW structure.

**D — Persona propagation + live graph projector + clarification.** `InoPersonaProjector` grain, `personaContext` propagation, `ChatFacade` auto-prepends snapshot, `SynapseResult.NeedsClarification`, proto field additions, clarification round-trip. Timeline correlation-id query method added if needed.
*Acceptance:* `StreamPersonaState` emits `SearchingFlights + SearchingHotels` simultaneously during parallel turn; cross-session memory test passes (two separate chat sessions, second references first).

**E — Flutter chat thread + Rive persona orb.** `home_screen.dart` append-only refactor; `RiveAnimation.asset` wiring with CustomPaint fallback; `.riv` asset shipped (or fallback ships as visual); `ClarificationCard` widget; satellite glyphs per sub-activity.
*Acceptance:* manual screenshot of multi-card thread with animated orb; `flutter analyze` clean; OTLP logs show BLoC transitions for every turn.

**F — `TokyoTripPlanningE2E` Playwright + travel observability.** New E2E covering full 5-day flow; travel-specific OTel spans (`ino.persona.read`, `ino.activity.transition`, `ino.rfw.composed`); screenshots saved.
*Acceptance:* E2E passes headless; screenshots saved under `tests/E2E.Tests/bin/.../screenshots/`; Aspire dashboard shows the full trace tree rooted at `ino`.

**G — Polish.** Graceful SerpApi timeout fallback, empty-state handling on every card, follow-up chip suggestions from the recommender, error telemetry.
*Acceptance:* force SerpApi timeout → user sees a graceful card (not a stack trace); `ino.errors` metric increments; trace shows the error span with a clear error type.

**Demo-fast ordering:** A → E (minimum viable demo with existing single-card flow) → C → F → D (persona depth) → G → B (primitive hardening).

The priority ordering lets us cut a working demo at the end of F (steps A+E+C+F) and land persona-depth + decay + polish as follow-up work if the "ASAP" window closes early.

---

## 8. Out of scope (explicitly)

- Booking / payment / OAuth UX (vault infrastructure stays; no booking UI)
- Insurance / health / any non-travel domain (architecture supports them; we don't ship them)
- L3 compilation (creator → ino runtime spawning)
- `IReceiver<TMessage>` as a durability primitive (dead code stays dead)
- Multi-silo `PersonaSignalFilter` observability fix (PR #4 known limitation)
- Streaming `Chat` gRPC (unary stays)
- Rive authoring — we wire the runtime and ship a contract for the asset; the art asset itself is sourced or accepted as a fallback
- Persistent multi-step clarification dialog state (one-shot only)
- Background ML-driven importance ranking on top of decay
- Aspire stop/start for code changes (per-resource rebuild only)

---

## 9. Known constraints and gotchas (load-bearing)

- `Neuron.cs` and `NeuronGrain.cs` live at `src/Core/Neurons/` — not `features/ino-new/InoNew.Core/` (prompt paths are pre-restructure).
- `IReceiver<TMessage>` is a 9-line dead interface with zero implementations.
- `TripPlanningE2E.cs` already exists and verifies multi-tool orchestration via `MockLlm.OnMultiToolCall` but does NOT assert RFW bytes — the new `TokyoTripPlanningE2E` extends rather than replaces it.
- `PersonaSignalFilter` has a multi-silo hazard (single-silo subscriber counter). Demo runs single-silo so it's acceptable; documented.
- RFW Dart parser rejects Windows CRLF — `RfwBuilder.Build` already strips `\r`, any new template script must produce LF-only output (the existing builder handles this).
- Aspire AppHost topology is frozen after `Build()` — runtime neuron additions go through the registry + `NeuronGrain`, not through AppHost resource changes.
- Orleans grain-type manifest is cluster-wide at silo startup — runtime neurons use the pre-existing `NeuronGrain` host type activated by id; no new grain types per new neuron.
- CSharpScript compilation cost: ~100–500 ms first-activation, cached by SHA256 thereafter in `NeuronGrain` (already implemented).
- Context7 verification required at plan time for: Orleans `IPersistentState` (new grain), Orleans reminders (decay sweep), `Microsoft.CodeAnalysis.CSharp.Scripting` (script globals), `rfw` Dart package (DSL syntax), `rive` Dart package 0.14.5 (`StateMachineController`, `SMIInput<double>`, `SMITrigger`). **Context7 must be called before writing any code per CLAUDE.md.**
