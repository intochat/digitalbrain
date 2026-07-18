# Phase 4 Epilogue — Demo Strip + Tripradar Relocation + Inspector E.3 + RFW

**Date:** 2026-05-02
**Status:** Approved (brainstorm); awaiting spec review.
**Predecessor:** [`plan-poc-phase-4.md`](../../plan-poc-phase-4.md) (Slices A–E.2 shipped — see memory `project-phase-4-shipped-2026-05-02.md` and `project-phase-4-slice-e2-shipped-2026-05-02.md`).
**Successor:** Implementation plan to be drafted via `superpowers:writing-plans` after this spec is approved.

---

## Mission

Phase 4 closed the L1 self-improvement loop end-to-end on the backend (commit `503ea8e` — Genesis silo + RoslynPlan + CreatorNeuron). The Flutter surface exposes none of this directly. This epilogue makes the loop visible, makes manual smoke-testing one-tap, and turns trip planning into rich server-authored UI. It also cleans up the repo by folding the external `tripradar/` product under `domains/travel/`.

Four slices, strict sequential, demo-strip first.

```
Slice 1 — Demo button strip (Flutter only)              ── ½–1 day
Slice 2 — Tripradar relocation (structural, no features) ── ½–1 day
Slice 3 — Inspector E.3 (Proposals + Routing tabs)       ── 2–3 days
Slice 4 — RFW for trip planning                          ── 3–5 days
```

InoNeuron (per-user agent grain with installed-domains list, system prompt, tool selection over Cortex) is **explicitly out of scope**. Possibly a Phase 5 conversation later.

---

## Key facts that re-shape the original prompts

A pre-design survey of the codebase (commit `503ea8e`) turned up five facts that meaningfully change scope from the three original prompts:

1. **`NeuronResult` already has `byte[]? Rfw` (Id=4) + `WithRfw()` extension** at `src/Ino.Core/NeuronResult.cs`. The RFW byte payload seam is scaffolded; the slice reuses it (with an upgrade to a structured `RfwPayload` record carrying `LibraryName + DescriptionDsl + DataPayload` to match the proto's two-byte-field shape).
2. **`ChatResponse` proto already carries `rfw_description` (bytes) + `rfw_data` (bytes) + `is_skeleton` (bool) + `correlation_id`** at `src/Ino.Gateway.Grpc/Protos/ino.proto`. The RFW wire format is in place — the slice fills in producers (Travel plans) and consumers (Flutter widget library), not the contract.
3. **`inspector_drawer.dart` is NOT a placeholder.** It's production code at `clients/ino.flutter/lib/ui/components/inspector_drawer.dart` with active Identity / State / Reasoning / Metrics panels and three stub panels (Actions / Scheduling / Integrations). The Inspector slice **adds two new tabs** alongside, doesn't rebuild the drawer wholesale.
4. **`ino_bloc` already exposes `SendMessage(message)`** at `clients/ino.flutter/lib/state/ino_bloc.dart:14`. The demo strip dispatches through that — no new event needed.
5. **`CortexNeuron.RecordRoutingDecisionAsync` only feeds the optimizer today, doesn't journal.** The Inspector slice has to fork the write so the new `CortexJournal` grain captures the same decisions.

---

## Slice 1 — Demo button strip

**Goal.** Six tappable chips above the chat composer that send canned prompts through the existing `ino_bloc.SendMessage` event. Pure client; no backend changes.

### Files touched

| File | Change |
|---|---|
| `clients/ino.flutter/lib/ui/components/test_button_strip.dart` | new — `Wrap` of `FilledButton.tonal` chips, Material 3 token-driven styling matching `chat_bubble.dart` |
| `clients/ino.flutter/lib/screens/home/home_screen.dart` | mount the strip directly above `_InputBar` (line 737), inside the same `Column`, gated by top-level `const kShowDemoButtons = true` |

`ino_bloc.dart` is not modified — `SendMessage(text)` already exists.

### The six buttons

| # | Label | Behavior |
|---|---|---|
| 1 | Set reminder | `SendMessage("remind me to test ino in 60 seconds")` |
| 2 | Recall | `SendMessage("my favourite colour is purple. what's my favourite colour?")` (combined prompt — sidesteps the missing auto-store hook) |
| 3 | Find flights | `SendMessage("find flights to bali next month")` |
| 4 | Get an uber | `SendMessage("get me an uber home")` |
| 5 | Trigger L1 loop | tap dispatches `SendMessage("demo l1 marker {sessionId.first8}")`; tracks taps; tap 4 fires after a 1 s pause |
| 6 | Show last routing | calls `Scaffold.of(context).showInspectorDrawer()` (pre-wires the Inspector drawer trigger that Slice 3 fills with real data) |

### The L1 trigger button — the only stateful one

```dart
class TestButtonStrip extends StatefulWidget { ... }

class _TestButtonStripState extends State<TestButtonStrip> {
  int _l1Taps = 0;
  late final String _l1ClusterKey = 'demo l1 marker ${_freshShortId()}';

  void _onL1Tap() {
    if (_l1Taps < 3) {
      _l1Taps++;
      context.read<InoBloc>().add(SendMessage(_l1ClusterKey));
    } else if (_l1Taps == 3) {
      _l1Taps++;
      Future.delayed(const Duration(seconds: 1), () {
        if (mounted) context.read<InoBloc>().add(SendMessage(_l1ClusterKey));
      });
    }
  }
}
```

The cluster key is **session-scoped** — all four taps use the same string so `MissedIntentTracker.NormalizeForCluster` actually clusters them. Re-tapping after the 4th fires nothing (visual state goes "Done — reload to re-arm").

### Subtleties

- Once Slice 3 lands with `IExperienceRegistry.ApprovalRequired = true`, the L1 button's tap-4 stops auto-routing. The user opens Inspector → approves → re-taps to see the routed response. **For Slice 1 in isolation, gating doesn't exist yet**; auto-registration still applies and the button works end-to-end. Document the post-Slice-3 behavior change in a source comment.
- The auto-generated tap-4 response reads `"Got it — I'll help with 'demo l1 marker a3f9e2'. (Auto-generated from 3 unrouted prompts.)"` — deterministic stub from `CreatorNeuron.DraftScriptBody`. Demo's value is the loop CLOSES, not response richness.
- Strip is a `Wrap`, not a `Row` — reflows on narrow viewports (Telegram WebApp inside iPhone SE viewport).

### Verification

1. `dotnet build ino.slnx` clean.
2. `flutter --version` works (otherwise the MSBuild target ships stale assets).
3. `cd clients/ino.flutter && flutter build web --no-tree-shake-icons`.
4. `mcp__aspire__execute_resource_command(resourceName="kernel", commandName="rebuild")`.
5. `mcp__chrome-devtools__navigate_page` to kernel HTTPS URL.
6. Tap each button, screenshot result. Confirm:
   - Reminder fires within 60 s (`ReminderNarration` lands on Chat stream).
   - Find-flights / Get-uber return plan responses.
   - Recall: LLM echoes "purple" in the same turn.
   - L1: 4th tap returns auto-generated response. Aspire structured logs match `MissedIntentTracker: emitted L1Proposal` and `CreatorNeuron: registered dynamic experience`.

### Commit

Single: `feat(poc): demo button strip on chat screen`.

### Risks

Low. Pure client. Worst case: M3 chip styling clashes with chat bubble palette — fix with one borderRadius tweak.

---

## Slice 2 — Tripradar relocation

**Goal.** Move `D:\ino\tripradar\` to `D:\ino\domains\travel\tripradar\` and add all 25 of its projects to `ino.slnx` under a new `/domains/travel/tripradar/` folder. Tripradar's own `TripRadar.slnx` is deleted (one solution to rule them all). Tripradar's Aspire AppHost (`tripradar/src/Aspire/Aspire.csproj`) **stays a separate AppHost** — `aspire run` from `D:\ino` still launches Ino.AppHost only; tripradar runs on demand via its own AppHost (Option C from the brainstorm).

**Why this slot in the sequence.** RFW (Slice 4) edits Travel plans heavily. Doing the relocation after means rebasing edits across a folder rename. Doing it now is cheap.

### Solution / build files

- **Delete** `tripradar/TripRadar.slnx`.
- **Add to `ino.slnx`** under `/domains/travel/tripradar/` folder block — all 25 projects with paths like `domains/travel/tripradar/src/Aspire/Aspire.csproj`. Mirror the project list from the current `TripRadar.slnx`.
- **Keep** tripradar's `Directory.Packages.props` for now (move with the folder; deletion deferred until version-conflict audit). Tripradar's `Directory.Build.props` already deleted on integration.
- **Audit** `D:\ino\.gitignore` — verify tripradar-relative patterns still apply. (Most should — `bin/`, `obj/`.)

### AppHost & relative-path fixes

- `tripradar/src/Aspire/AppHost.cs` — uses `..\..\..\` style paths. Internal structure unchanged so most resolve. Verify no path climbs to repo root expecting `D:\ino\tripradar\`.
- `tripradar/src/Aspire/Hosting/Cloudflared/CloudflaredExtensions.cs` — grep for `Path.Combine` / `ContentRootPath` traversal that climbs to repo root.
- `tripradar/aspire.config.json` if present — update or delete.
- `D:\ino\aspire.config.json` at repo root — **no change** (still points at `src/Ino.AppHost/Ino.AppHost.csproj`).

### Code references

- **No C# `ProjectReference`** from `D:\ino\src\` or `D:\ino\domains\` into tripradar today (survey confirmed). No `.csproj` edits in ino's project files.
- **`D:\ino\CLAUDE.md`** — multiple paragraphs reference `tripradar/`. Search-and-replace to `domains/travel/tripradar/` as part of the slice.
- Memory files at `C:\Users\vhorb\.claude\projects\D--ino\memory\*.md` — out of scope for this slice (user-personal).

### Move command

```
git mv tripradar domains/travel/tripradar
```
Then edit slnx + AppHost paths, delete old `TripRadar.slnx`, build to verify.

### Verification

1. `dotnet build ino.slnx` — must compile all 28 ino projects + 25 tripradar projects + 11 iaw projects (~64 total) without errors.
2. `dotnet test ino.slnx` — all existing tests still green.
3. `aspire run` from `D:\ino\` — Ino.AppHost boots clean.
4. `aspire run --apphost domains/travel/tripradar/src/Aspire/Aspire.csproj` — tripradar's own dashboard boots clean.
5. Grep `tripradar/` in CLAUDE.md — every hit reads `domains/travel/tripradar/`.

### Commits

- `refactor(repo): move tripradar/ → domains/travel/tripradar/`
- `chore(repo): merge tripradar projects into ino.slnx, delete TripRadar.slnx`
- `docs: update CLAUDE.md tripradar paths after relocation`

### Risks

- **Medium:** relative path leaks inside tripradar's source tree. Two known places (AppHost + Cloudflared); may be others. `aspire run` for tripradar's own AppHost catches most.
- **Low:** slnx folder visual clutter after merging 28 projects under `/domains/travel/`. Acceptable.
- **Low:** package version conflicts. Keep tripradar's `Directory.Packages.props` during slice; consolidate later.

### Out of scope

- Folding tripradar services into Ino.AppHost (Option C preserves separation).
- Tripradar consuming `Ino.Llm.Xai` or any cross-runtime sharing.
- Deleting tripradar's `Directory.Packages.props`.
- Cloudflared tunnel reconfiguration if tripradar uses one for prod webhooks.

---

## Slice 3 — Inspector E.3 (Proposals + Routing tabs)

**Goal.** Make the L1 self-improvement loop user-visible. Two new tabs in the existing inspector drawer: **Routing** (last 20 routing decisions per user) and **Proposals** (Pending → Approved/Rejected lifecycle on `L1Proposal`s). Approval gating uses **Option A** — `IExperienceRegistry.ApprovalRequired` flag.

### Sub-commit 3A — `feat(poc): ProposalLog + CortexJournal grains + ApprovalRequired gating`

**`src/Ino.Kernel/ProposalLog.cs`** (new)
- `[PinToSilo("kernel")]` `Grain, IProposalLog, IReactsTo<L1Proposal>, IReactsTo<NeuronCreated>, IReactsTo<ProposalDecided>`.
- In-memory `Dictionary<string, ProposalEntry>` keyed by `ProposalId`. Volatile (rebuilds via broadcast journal — gap noted, falls under #22).
- `ProposalEntry` shape:
  ```csharp
  [GenerateSerializer]
  public sealed record ProposalEntry(
      string ProposalId,
      string UserId,
      string ClusterKey,
      string ExamplePrompt,
      string[] AllPrompts,            // concrete T[] per <>z__ReadOnlyArray trap
      int Occurrences,
      DateTimeOffset ProposedAt,
      ProposalStatus Status,          // Pending | Approved | Rejected
      string? ActivatedExperienceId,
      DateTimeOffset? DecidedAt,
      string? DecidedBy);
  ```
- API: `ListAsync(ProposalStatus? filter, int skip, int take)`, `GetAsync(proposalId)`, `RecordDecisionAsync(proposalId, decision, decidedBy)`.

**`src/Ino.Kernel/CortexJournal.cs`** (new)
- `[PinToSilo("kernel")]` `Grain, ICortexJournal`. Per-user circular buffer (last 20 decisions/user).
- `RoutingDecision` record carries `Prompt, RoutingSource, ExperienceId?, Confidence?, At, MlPrediction?, MlConfidence?, LlmCalled, RoutingDurationMs, CorrelationId`.
- API: `RecordAsync(userId, RoutingDecision)`, `GetRecentAsync(userId, count)`.

**`src/Ino.Kernel/CortexNeuron.cs:RecordRoutingDecisionAsync`** — fork the write. Today only feeds `INeuronOptimizer`. Add a second call to `ICortexJournal.RecordAsync`. Both writes fire-and-forget.

**`src/Ino.Core.Hosting/Registry/IExperienceRegistry.cs`** — extend:
- New: `bool ApprovalRequired { get; }` (sourced from `Ino:Inspector:ApprovalRequired`, defaults `true`).
- New: `Task<bool> ApproveAsync(string proposalId, CancellationToken ct)` — registers stashed draft.
- New: `Task<bool> RejectAsync(string proposalId, CancellationToken ct)` — discards stash.
- Internal: `Dictionary<string, DraftExperience>` of pending drafts (proposalId → script body + metadata).

**`domains/genesis/Ino.Domains.Genesis/Neurons/CreatorNeuron.cs`** — branch on `registry.ApprovalRequired`:
```csharp
public async Task ReactAsync(L1Proposal proposal, NeuronContext ctx, CancellationToken ct)
{
    if (await registry.IsAlreadyRegisteredAsync(DraftExperienceId(proposal), ct)) return;

    var draft = ComposeDraft(proposal);
    if (registry.ApprovalRequired)
    {
        await registry.StashDraftAsync(proposal.ProposalId, draft, ct);
        return;  // Don't broadcast NeuronCreated yet — wait for approval.
    }
    await registry.RegisterAsync(draft, ct);
    await firePort.FireBroadcast(new NeuronCreated(...), ctx, ct);
}
```
And `ApproveAsync` (called from gateway) does `RegisterAsync` + `FireBroadcast(NeuronCreated)` on demand.

**New synapse `ProposalDecided`** in `Ino.Kernel.Contracts` — broadcast by gateway when user clicks Approve/Reject; `ProposalLog` reacts to update its state.

**Tests:**
- `test/Ino.Kernel.Tests/ProposalLogTests.cs` — 4 tests
- `test/Ino.Kernel.Tests/CortexJournalTests.cs` — 3 tests
- `domains/genesis/Ino.Domains.Genesis.Tests/CreatorNeuronApprovalGatingTests.cs` — 3 tests

**Context7 ahead of coding:** Orleans 10 `IReactsTo<T>` semantics — confirm a single grain can implement multiple reactor interfaces and Orleans dispatches each broadcast to the right `ReactAsync` overload.

### Sub-commit 3B — `feat(poc): Inspector gRPC RPCs (proposals + routing decisions)`

**Extend `src/Ino.Gateway.Grpc/Protos/ino.proto`** — add to existing `Ino` service:
```proto
rpc ListProposals(ListProposalsRequest) returns (ListProposalsResponse);
rpc DecideProposal(DecideProposalRequest) returns (DecideProposalResponse);
rpc ListRoutingDecisions(ListRoutingDecisionsRequest) returns (ListRoutingDecisionsResponse);
```

**`src/Ino.Gateway/InoGateway.cs`** — extend `IInoGateway` with three async methods that delegate to the kernel grains via `IGrainFactory`.

**`src/Ino.Gateway.Grpc/Services/InoGrpcService.cs`** — three handlers, all read user from `Caller.UserId` (existing pattern).

**Tests:** `test/Ino.Hosting.Tests/InoGrpcServiceInspectorRpcsTests.cs` — 3 tests.

### Sub-commit 3C — `feat(poc): Flutter inspector drawer — Proposals + Routing tabs`

**Pre-step:** regenerate Dart stubs. Verify `clients/ino.flutter/lib/grpc/generated/*.dart` timestamps after regen — stale stubs silently 12-status the new RPCs.

**`clients/ino.flutter/lib/state/proposals_bloc.dart`** (new) — events `ProposalsRefreshRequested | ProposalApproved | ProposalRejected`; states `ProposalsLoading | ProposalsLoaded(pending, approved, rejected) | ProposalsError`; 5 s polling timer when drawer visible.

**`clients/ino.flutter/lib/state/routing_bloc.dart`** (new) — same pattern, 2 s polling.

**`clients/ino.flutter/lib/ui/components/inspector_drawer.dart`** — additive change. Add two new tabs alongside existing panels.

**Routing tab:** `ListView.builder` of routing entries, color-coded by `RoutingSource`:
- Regex → `colorScheme.primary`
- ML → `colorScheme.tertiary`
- LLM → `colorScheme.secondary`
- Unrouted → `colorScheme.error`

Expandable card showing `prompt`, `experienceId`, `Confidence`, `MlConfidence`, `LlmCalled`, `RoutingDurationMs`, `correlationId` (small monospace).

**Proposals tab:** Three sections — Pending (top), Approved (collapsible), Rejected (collapsible).
- Pending: `ClusterKey`, `ExamplePrompt`, `Occurrences`, `Approve` (filled) + `Reject` (outlined) buttons.
- Approved: `ActivatedExperienceId` + "test it now" button (dispatches `SendMessage(ExamplePrompt)`).
- Rejected: muted, no actions.

**Tests:** none (Flutter has no Dart tests today; manual browser verification covers it).

### End-to-end acceptance test

1. `aspire run` — all silos Healthy.
2. Open kernel HTTPS URL.
3. Send 3× the same unrouted prompt (e.g., `"frobnicate the gizmo"`).
4. Inspector → Proposals tab. Expect: one Pending entry, Occurrences=3.
5. Click Approve. Expect: entry → Approved, ActivatedExperienceId populated.
6. Send `"frobnicate the gizmo"` 4th time. Expect: routes to auto-generated experience, response is the deterministic stub.
7. Routing tab. Expect: 4 entries — first 3 Unrouted (red), 4th ML or LLM (green/amber) with new ExperienceId.
8. Aspire structured logs include `MissedIntentTracker: emitted L1Proposal`, `CreatorNeuron: stashed draft`, `CreatorNeuron: registered dynamic experience` (after Approve).

### Test fixture impact

Existing E.2 acceptance test (`test/Ino.Kernel.Tests/L1LoopTests.cs` or similar) expects auto-registration. After Slice 3, `ApprovalRequired=true` is the default. **Adapt the test** to call `await registry.ApproveAsync(proposalId)` after the proposal lands — exercises the gating path, which is now production behavior. No backwards-compat shim.

### Out of scope

- Inspector ML pane (per-user optimizer histogram).
- Editing draft script body before approving (`DecideProposalRequest` carries optional override field for future use).
- Server-streaming RPCs for live deltas (polling is fine).
- Persistence of ProposalLog / CortexJournal (#22).
- Cross-user proposal aggregation.

---

## Slice 4 — RFW (Remote Flutter Widgets) for trip planning

**Goal.** Travel plans return server-authored UI — flight cards, hotel cards, place cards rendered by the Flutter client from RFW DSL — instead of plain text. Tap "Select" on a flight card → fires a callback synapse → next plan step (hotel) emits a fresh RFW tree.

**Largest slice by ~5×.** Budget 3–5 days.

### Blocking research at slice start

**Stop and reconvene with the user if R1 (security) reveals a showstopper.**

- **R1 — RFW security model.** Is the parser declarative or does it allow code execution? `mcp__context7__resolve-library-id "Remote Flutter Widgets"` → query for "security threat model arbitrary widget execution". For v0.1 we control all domain silos so academic; marketplace post-v0.1 needs sandbox.
- **R2 — CRLF parser bug, still true?** Verify against current `package:rfw`. If still true, bake server-side strip into `Ino.Gateway.Grpc/Services/InoGrpcService.cs` before wire-write.
- **R3 — Two-way data flow shape.** RFW emits Dart-side events via `Runtime.eventStream`. Verify the event-binding shape (`args` dict in DSL → callback signature).
- **R4 — Streaming / incremental.** Each plan step swaps in a fresh RFW tree (full replacement). Verify nothing in `package:rfw` requires DSL deltas.

**Output:** paragraph at top of design recording R1–R4 answers + chosen approach, before code.

### Sub-commit 4A — `feat(poc): RfwPayload contract + NeuronResult upgrade + ChatService wiring`

**`src/Ino.Core/RfwPayload.cs`** (new)
```csharp
[GenerateSerializer]
public sealed record RfwPayload(
    [property: Id(0)] string LibraryName,    // e.g. "ino.travel.flights"
    [property: Id(1)] byte[] DescriptionDsl, // serialized RemoteWidgetLibrary (raw text, CRLF-stripped)
    [property: Id(2)] byte[] DataPayload);   // serialized DynamicContent (raw JSON, CRLF-stripped)
```
Concrete `byte[]` per `<>z__ReadOnlyArray` trap.

**`src/Ino.Core/NeuronResult.cs`** — replace `byte[]? Rfw` (Id=4) with `RfwPayload? Rfw`. Reuse Id slot (safe break since unused). Add `WithRfwPayload(RfwPayload)`; remove old `WithRfw(byte[])`.

**`src/Ino.Gateway.Grpc/Services/InoGrpcService.cs`** — when responding with `NeuronResult.Rfw` non-null:
- Strip `\r` from `DescriptionDsl` and `DataPayload` (R2 mitigation).
- Set `ChatResponse.rfw_description = DescriptionDsl`, `rfw_data = DataPayload`, `is_skeleton = false`.

**Tests:** `test/Ino.Core.Tests/NeuronResultRfwTests.cs` — 3 tests (roundtrip, Orleans serializer, CRLF strip).

### Sub-commit 4B — `feat(poc): Travel plans return RFW payloads`

**`domains/travel/Ino.Domains.Travel/Plans/FindFlightsPlan.cs`** — keep static `ExecuteAsync`; enrich `NeuronResult.Ok(...)` with `.WithRfwPayload(rfwPayload)`.

**`domains/travel/Ino.Domains.Travel/Rfw/FlightCardListBuilder.cs`** (new) — emits DSL like:
```
import widgets;
import ino.travel;

widget root = Column(
  children: [
    ...for flight in data.flights: FlightCard(
      airline: flight.airline,
      price: flight.price,
      durationMin: flight.durationMin,
      flightId: flight.id,
      onSelect: event 'flight.selected' { flightId: flight.id }
    ),
  ]
);
```
DSL never contains user-typed text — no injection concerns. Verify exact event syntax in R3.

**`PlanTripPlan.cs`** — multi-step flow:
1. First step emits flight-card list.
2. On `flight.selected` callback (via 4D's `RfwEvent` RPC), plan resumes, calls hotel sub-plan, emits `HotelCardListBuilder.Build(...)`.
3. Same for places.

State held in plan grain's journal (inherited from `Neuron<TEvent>`).

**Mock data caveat.** Travel plans don't call real tripradar today. RFW slice ships against mocks (3 hardcoded flights, 3 hotels, 3 places). Real tripradar↔Travel data flow is post-RFW.

**Tests:** `domains/travel/Ino.Domains.Travel.Tests/FlightCardListBuilderTests.cs` — 3 tests (DSL well-formed, CRLF strip, JSON shape).

### Sub-commit 4C — `feat(poc): Flutter RFW widget library + chat bubble integration`

**`clients/ino.flutter/pubspec.yaml`** — add `rfw: ^1.x`. `flutter pub get`.

**`clients/ino.flutter/lib/rfw/widget_library.dart`** (new) — five widgets registered with `LocalWidgetLibrary`:
- `FlightCard(airline, price, durationMin, flightId, onSelect)`
- `HotelCard(name, pricePerNight, rating, hotelId, onSelect)`
- `PlaceCard(name, category, rating, placeId, onSelect)`
- `TripStepIndicator(currentStep, totalSteps)`
- `CtaButton(label, eventName, args)`

**`clients/ino.flutter/lib/rfw/event_dispatcher.dart`** — listens on `Runtime.eventStream`. Dispatches into chat BLoC via `RfwEventEmitted(eventName, args)`.

**`clients/ino.flutter/lib/ui/components/chat_bubble.dart`** — branch on `response.rfwDescription.isNotEmpty`:
```dart
RemoteWidget(
  runtime: _rfwRuntime,
  data: _rfwData,
  widget: const FullyQualifiedWidgetName(LibraryName('ino.travel.flights'), 'root'),
)
```

No proto changes in this sub-commit → no Dart stub regen. Slot regen in 4D.

### Sub-commit 4D — `feat(poc): two-way RFW event callbacks (RfwEvent gRPC RPC)`

**Extend `Ino.Gateway.Grpc/Protos/ino.proto`:**
```proto
rpc RfwEvent(RfwEventRequest) returns (RfwEventResponse);

message RfwEventRequest {
    string correlation_id = 1;
    string event_name = 2;
    google.protobuf.Struct args = 3;
}
message RfwEventResponse {
    bool accepted = 1;
}
```

**`src/Ino.Gateway.Grpc/Services/InoGrpcService.RfwEvent`** — handler resolves originating plan grain via `correlation_id` (kernel-pinned `ICorrelationRegistry` grain — new) and fires `IPlan.HandleRfwEventAsync`.

**`src/Ino.Kernel/CorrelationRegistry.cs`** (new) — `[PinToSilo("kernel")]` mapping `correlationId → plan grainId`. In-memory, volatile.

**Plan side:** `IRfwEventHandler` interface added to plans expecting events. PlanTripPlan implements; FindFlightsPlan one-shot.

**Flutter:** `clients/ino.flutter/lib/state/ino_bloc.dart` — `RfwEventEmitted(eventName, args)` handler calls `inoGrpcClient.rfwEvent(...)`.

**Tests:**
- `test/Ino.Hosting.Tests/RfwEventEndpointTests.cs` — 3 tests
- `domains/travel/Ino.Domains.Travel.Tests/PlanTripPlanRfwEventsTests.cs` — 3 tests

### Verification

1. R1–R4 answered first.
2. `dotnet build ino.slnx --no-incremental` clean.
3. `dotnet test ino.slnx --no-build` clean.
4. `flutter --version` works, then `cd clients/ino.flutter && flutter build web --no-tree-shake-icons`.
5. `mcp__aspire__execute_resource_command(resourceName="kernel", commandName="rebuild")` and `... resourceName="travel" ...`.
6. Open kernel HTTPS URL.
7. Send `find flights to bali next month`. Expect: chat bubble renders 3 flight cards, screenshot.
8. Tap "Select". Expect: confirmation bubble, then hotel cards.
9. Send `plan a trip to bali`. Expect: full multi-step flow.
10. Aspire structured logs include `ino-flutter` events for `RfwEventEmitted`; traces show `grpc Chat` → `fire FindFlightsRequest` → `... RFW emitted, size=N bytes, parse=Mms` → `grpc RfwEvent` → next step.
11. **Telegram check:** `/start` → mini-app → drive same flow inside WebApp viewport.

### Commit cadence

1. `feat(poc): RfwPayload contract + NeuronResult upgrade + ChatService wiring`
2. `feat(poc): Travel plans return RFW payloads`
3. `feat(poc): Flutter RFW widget library + chat bubble integration`
4. `feat(poc): two-way RFW event callbacks (RfwEvent gRPC RPC)`

### Risks

- **R1 surfaces showstopper.** Pause, reconvene. Marketplace sandbox story changes.
- **CRLF strip missed at one site.** Test in `InoGrpcServiceTests` with `\r\n` payload; assert only `\n` lands.
- **gRPC-dart `ResponseStream<T>` single-subscription** (CLAUDE.md trap). Existing telemetry interceptor fine; don't add a second listener.
- **`google.protobuf.Struct` Dart codegen** — verify clean. Fall back to `map<string, string>` if not.
- **Plan state volatility on silo restart** — acceptable v0.1 limitation; #22 fixes.
- **Mock flight data** — documented gap; not blocking.

### Out of scope

- Marketplace / sandbox security for third-party RFW DSL.
- Animations / Hero transitions.
- Reskinning existing chat bubbles.
- RFW for Reminders / Recall / Genesis.
- Real tripradar↔Travel structured data flow.

---

## Cross-cutting concerns

### Verification loop reuse

Every slice goes through CLAUDE.md's 6-step loop. Type-check + dotnet test alone is never a green light — the visible browser scenario is the gate.

### Aspire iteration

`mcp__aspire__execute_resource_command(resourceName="X", commandName="rebuild")` is the per-resource hot-restart. Don't stop/start the whole AppHost between attempts. Resource names: `kernel`, `identity`, `travel`, `taxi`, `genesis`, `reminders`, `recall`, `telegram`.

### Stale-codegen ritual

After any `[GenerateSerializer]` record changes (Slice 3 ProposalEntry/RoutingDecision/ProposalDecided, Slice 4 RfwPayload):
```
dotnet build ino.slnx --no-incremental
dotnet test ino.slnx --no-build
```
Skipping `--no-incremental` causes spurious `CodecNotFoundException`.

### Stub regen ritual

After any `.proto` changes (Slice 3 three new RPCs, Slice 4 RfwEvent):
1. Run generator (`flutter pub run grpc:protoc_plugin` or project-specific tooling).
2. **Verify timestamps on `clients/ino.flutter/lib/grpc/generated/*.dart`** — stale stubs silently 12-status new RPCs.
3. `flutter build web --no-tree-shake-icons`.
4. `mcp__aspire__execute_resource_command(resourceName="kernel", commandName="rebuild")`.

### OTel `service.name` discipline

Every silo declares its own. No new silos in this plan; existing ones keep working.

### Counters added

**Slice 3:**
- `ino.l1.proposals_emitted` (already from MissedIntentTracker)
- `ino.l1.proposals_pending` (gauge)
- `ino.l1.proposals_approved`, `ino.l1.proposals_rejected` (counters)
- `ino.cortex.routing.decisions` (counter, by source)

**Slice 4:**
- `ino.rfw.payloads_emitted` (counter, by domain)
- `ino.rfw.events_received` (counter, by event_name)
- `ino.rfw.payload_bytes` (histogram)

### Commit discipline

Granular commits per sub-slice. Push after green. Proceed without per-slice user gate (memory `feedback-autopilot`). Exception: Slice 4 R1–R4 research — pause if R1 reveals a showstopper.

### `[PinToSilo]` policy

Reserved for cluster singletons. New pinned grains: `ProposalLog`, `CortexJournal`, `CorrelationRegistry` — all `[PinToSilo("kernel")]`. NOT pinned: anything else.

### No env-var branches

`kShowDemoButtons` (Slice 1) is a top-level Dart const. `Ino:Inspector:ApprovalRequired` (Slice 3) is appsettings-config-driven via factory pattern. RFW (Slice 4) has no flag. (Memory `feedback-no-env-var-branches-in-apphost` applied by analogy.)

---

## Master risk register

| # | Risk | Likelihood | Impact | Slice | Mitigation |
|---|---|---|---|---|---|
| 1 | RFW security model permissive | Low | High | 4 | R1 research first; pause if confirmed |
| 2 | RFW CRLF parser bug active | High | Low | 4 | Server-side strip in ChatService; tested |
| 3 | Tripradar relative paths leak across move | Medium | Low | 2 | Verify by booting tripradar's own AppHost post-move |
| 4 | Stale Dart stubs after proto change | Medium | Medium | 3, 4 | Verify file timestamps before claiming green |
| 5 | Codec exception on new [GenerateSerializer] records | Medium | Medium | 3, 4 | `dotnet build --no-incremental` ritual |
| 6 | Demo strip's L1 button breaks under approval gating | High | Low | 1→3 | Document re-tap-after-approve flow in source comment |
| 7 | Plan state volatility (silo restart mid-trip) | Medium | Low | 4 | Documented v0.1 limitation; #22 fixes |
| 8 | Polling overhead from Inspector tabs | Low | Low | 3 | 2s/5s acceptable; combined RPC if laggy |
| 9 | Tripradar package version conflicts post-merge | Medium | Low | 2 | Keep tripradar's Directory.Packages.props during slice |
| 10 | Flutter MSBuild target ships stale wwwroot | Medium | High | all | `flutter --version` precondition before each rebuild |
| 11 | RFW two-way callback correlation lost on restart | High | Low | 4 | Acceptable v0.1; correlation registry in-memory |
| 12 | ApprovalRequired flag default flip breaks E.2 tests | High | Low | 3 | Adapt tests to exercise gating path; no shim |

---

## Hard out-of-scope

From the three prompts:
- InoNeuron / per-user agent grain
- Cross-user proposal aggregation
- Inspector ML pane (per-user optimizer histogram)
- Editing draft script body before approving (UI doesn't surface override)
- Server-streaming gRPC for live deltas (polling for v0.1)
- Persistence of ProposalLog / CortexJournal / CorrelationRegistry (#22)
- Marketplace sandbox security for third-party RFW DSL
- Animations / Hero transitions in RFW
- RFW for Reminders / Recall / Genesis (Travel only)
- Real tripradar↔Travel structured data flow

From broader Phase 4 plan (post-v0.1):
- L2 reasoning-time C# user-facing surface
- L3 compiled-silo + rolling restart loop
- Marketplace promotion (high-confidence proposal → default-install)
- Topology decision (#21)
- Synapse decay + reinforcement (#23)
- Telegram / ino-windows full migration

---

## Dependency graph

```
Slice 1 (demo strip) ── ½–1 day
   │
   ▼
Slice 2 (tripradar relocation) ── ½–1 day
   │
   ▼
Slice 3 (Inspector E.3) ── 2–3 days
   ├── 3A: Backend grains + ApprovalRequired
   ├── 3B: gRPC RPCs
   └── 3C: Flutter Routing + Proposals tabs
   │
   ▼
Slice 4 (RFW) ── 3–5 days  ◀── BLOCKING research R1–R4 first
   ├── 4A: RfwPayload + NeuronResult upgrade
   ├── 4B: Travel plans emit
   ├── 4C: Flutter widget library
   └── 4D: RfwEvent two-way callback

Total: ~7–10 working days, sequential.
```

---

## Roll-forward gate

After Slice 4 lands and the multi-step trip-planning acceptance test passes, this epilogue is done. Phase 5 candidates: InoNeuron / per-user agent design, #21 topology, #22 durable persistence, #23 synapse decay.
