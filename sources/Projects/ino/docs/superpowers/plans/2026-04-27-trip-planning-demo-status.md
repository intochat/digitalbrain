# Trip-Planning Demo — Status (slices 3–9 landed)

All seven slices from `2026-04-26-trip-planning-demo-continuation.md` are on master. The demo boots clean under `INO_TEST_MODE=true` with all three silos reporting Healthy; the conversational round-trip (PlanTripRequest → AskClarification → ProvideClarification → TripItinerary) is fully wired through neurons, synapses, gateway, gRPC, and Flutter RFW.

## Master timeline (this work)

| Commit | Slice | What |
|---|---|---|
| `f5ea81a` | 1 | BddMockChatClientFactory wired on `INO_TEST_MODE=true` |
| `c9d5201` | 2 | Kernel-level `Ino.Core.AskClarification` / `Ino.Core.ProvideClarification` |
| `21d4542` | — | Continuation plan doc (slices 3–9) |
| `45b1195` | 3 | TripPlannerNeuron + Neuron<TripPlannerEvent> journal + slot parser (17 tests) |
| `2e53f0c` | 4 | Destination-keyed FlightFixture / HotelFixture / PlaceFixture (Tokyo/Paris/NYC, 9 tests) |
| `a2151d2` | 7 | BDD scenarios for Cortex intent classification |
| `2d64683` | 5 | ClarificationChipsTemplate RFW + AskClarification wires it (4 tests) |
| `aad4a06` | 6a | Proto: correlation_id; gateway threading; FireSynapse impl |
| `815a1b8` | 6b | Flutter chip-tap → FireSynapse → render response inline |
| `226c8d5` | 8+9 | Playwright fixture (headed by default; CI=true → headless) + two TripPlanning experience tests |

## Architectural decisions (locked in by this work)

1. **Conversation grain key = correlation_id.** `TripPlannerNeuron` is `Neuron<TripPlannerEvent>` with the conversation's correlation_id as its grain primary key. Orleans keeps the same activation across turns; the journal recovers slot-fill state.
2. **Discovery routes by synapse type.** Both `PlanTripRequest` (turn 1) and `ProvideClarification` (turn N≥2) flow through canonical-handler lookup in Discovery — no special direct-routing layer.
3. **`ProvideClarification` is canonical at the kernel level.** v0.1 has one canonical handler (TripPlannerNeuron). Multi-domain clarification routing is a post-v0.1 typed wrapper (`ProvideClarification<TQuestion>`) or correlation-id dispatcher.
4. **AppHost stays pristine.** Production `.WithLlm<Grok4FastNonReasoning>().AsFast()` declarations are *architectural*, not env-conditional. Dev variation lives in the `IChatClientFactory` selection inside `AddInoChatClients`: `INO_TEST_MODE=true` → BddMockChatClientFactory; otherwise XaiChatClientFactory.
5. **userId → correlationId session table** in `InoGateway` for client compatibility while Dart codegen lacks the new wire field. Chat() mints fresh per turn and overwrites the slot; FireSynapse() falls back to the slot when the client doesn't echo correlation_id back.
6. **Fakes-in-process travel data.** No `tripradar/` HTTP. `FlightFixture/HotelFixture/PlaceFixture.For(destination)` look up Tokyo/Paris/NYC with case-insensitive matching; unknown destinations fall back to Tokyo.
7. **Browser tests headed by default.** `dotnet test POC/test/Ino.E2E.Tests` opens visible Chromium; CI=true (auto-set on GitHub Actions, Azure Pipelines, GitLab, CircleCI) flips to headless. No xUnit traits, no env-var gymnastics.
8. **CanvasKit + gRPC interception.** Flutter web with CanvasKit paints labels into `<canvas>` — DOM `text=` selectors find nothing. Experience tests assert via `Page.Response` interception of gRPC-Web Chat() / FireSynapse() bodies, matching on content_type strings + fixture data.

## How the demo runs end-to-end

```bash
# Cold-boot demo, no XAI_API_KEY required
INO_TEST_MODE=true aspire run

# Open the system silo HTTPS URL printed in the dashboard
# Type: "plan a trip to Tokyo next week"
# → TripPlannerNeuron parses both slots inline, fans out to the three
#   search neurons, composes itinerary RFW with ANA flight data,
#   streams it back as the next chat frame.

# OR: "plan a trip to Tokyo"
# → TripPlannerNeuron sees only destination, journals SlotFilled, returns
#   AskClarification with chip suggestions ["this weekend","next week","next month"].
# → Click "next week" chip
# → RFW emits ino:provide-clarification event {field:"dates", value:"next week"}
# → Bloc fires gRPC FireSynapse, gateway looks up correlation_id from session,
#   pins fire to same TripPlannerNeuron activation, journal recovers destination,
#   composes itinerary, streams it back.
```

```bash
# Experience tests — boots AppHost + Chromium per fixture, runs both tests
dotnet test POC/test/Ino.E2E.Tests
# → 2 TripPlanning tests pass; you see Chromium open and drive the chat
# → in any CI runner, CI=true → headless transparently
```

## What's NOT done (intentionally deferred)

- **Multi-domain clarification routing.** v0.1 has one canonical handler for `ProvideClarification`. Adding Taxi or another conversational domain needs either a typed wrapper (`ProvideClarification<TQuestion>` per domain) or a correlation-id-keyed dispatcher. Out of scope for v0.1.
- **Real LLM integration.** Cortex routing is BDD-mocked under `INO_TEST_MODE=true`. Removing test mode lights up the real xAI factory but no neuron actually calls `IChatClient` yet — that's tasks 13–23 of the original Spec A plan (Cortex hybrid routing, LLM narratives in 6 neurons).
- **Real travel data.** `tripradar/` integration deferred — fakes are in-process for the demo.
- **Voice path.** WebSpeechApi declared in AppHost but voice round-trip isn't wired into the trip planner. Tasks 25–27 of the original Spec A plan.
- **Dart codegen for new proto fields.** Hand-edit on `FireResponse` works on the wire (proto3 forward-compat). Once `protoc` + `protoc-gen-dart` are installed in CI, regen will produce a clean diff.

## Known traps that bit us during this work

- **AppHost LLM declarations are architectural, not env-conditional.** Sniffing `XAI_API_KEY` in `Program.cs` corrupts the contract — dev variation lives in the silo's factory selection. Memorialised in `feedback-no-env-var-branches-in-apphost.md`.
- **Dart proto codegen needs `protoc` + plugin on PATH.** Not installed locally. Hand-edited `FireResponse` fields 3–7; the comment on `_i` flags the manual edit so a future regen run is a clean diff.
- **CanvasKit doesn't render text into DOM.** Flutter web with CanvasKit paints all chip labels and itinerary rows into `<canvas>`. Tests use `Page.Response` interception, not text scraping.
- **Neuron<TEvent> requires DurableGrain + IDurableList.** Plain Grain unit tests don't work for journaled neurons; we factored `TripPlannerSlotParser` out as a static class so the slot-extraction rules are unit-testable without a silo.

## Pre-existing flakes (not caused by this work)

- `InMemoryInoEventBusTests.Events_for_other_users_are_not_delivered` and `.Multiple_subscriptions_for_same_user_all_receive_the_event` — 400 ms cancellation timer is too tight under parallel test load. Passes on retry.
- `DiscoveryTableEndpointTests.Table_endpoint_is_routed_on_system_silo` — pre-existing cross-silo cold-boot race (memo: `project-cold-boot-race.md`). Reproduces under preview.3 SDK; not regressed by this work.

## Tests added in this work

- `TripPlannerSlotParserTests` — 17 tests covering destination extraction (Tokyo/Paris/NYC/New York), date extraction (this weekend / next week / next month / tomorrow / today / ISO), suggestion lists, slot order, prompts.
- `TravelFixturesTests` — 9 tests for destination-keyed Flight/Hotel/Place lookups + Tokyo fallback.
- `ClarificationChipsTemplateTests` — 4 tests for LF-only RFW, JSON payload shape, empty-suggestions guard, event-name constant.
- `TripPlanningExperienceTests` — 2 browser-driven E2E tests (gated by `InoExperienceFixture`).
