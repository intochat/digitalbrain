# Brain home + click-to-inspect (slice C.4)

**Status:** design, awaiting plan
**Date:** 2026-05-06
**Scope:** Flutter client UX + a single new gRPC method on the kernel. UX-only; no new backend domain or grain.
**Out of scope:** real `Recorder` neuron / voice path / dynamic-neuron creation / experience-as-emergent regeneration. Each is its own follow-up slice.

## Why

`/brain` exists as a separate page with a back-arrow to a chat-only `/home`. Two surfaces compete for "homepage", the brain has no interactivity beyond auto-rotation, and the prompt-keyword `_domainFromText` regex is the only thing that animates the brain on user input. The product should feel like one thing: a live, pokeable map of the running OS.

This slice unifies the two screens into a single home centred on the brain, makes every node clickable with a context-rich drawer, and removes the experience-halo primitive whose role is taken over by emergent quality-test runs in a later slice.

## Goals

1. `/brain` is the only home; `/home` route and `HomeScreen` are deleted.
2. Every neuron, synapse-type node, and live pulse on the brain is clickable; clicking opens a right-hand drawer with kind-specific detail.
3. A neuron drawer can fire a test synapse against the real grain so the brain becomes a debugger-grade surface.
4. The yellow experience halos and `composition` edges are removed; topology is just neurons + synapses + handler edges.
5. User-facing labels lose the `Neuron` / `Plan` suffixes; legend renames to "capability" / "signal" to honour the same rule.

Non-goals:

- Voice input. The composer keeps a stub mic icon (`onPressed: null`, tooltip "voice coming in slice 2"). Slice 2 brings the `Recorder` grain and wires it.
- Class / project / contract renames in C#. The `Recorder : Neuron<RecordingEvent>` going-forward rule is documented but not retroactively applied to existing concrete classes — that's a separate refactor commit.
- Server-side history persistence. The drawer's "recent traffic" buffer is in-memory on the client, dropped on reload.

## Architecture

### Routing

```
/             → redirect to /brain
/brain        → BrainHomeScreen (this slice)
/onboarding   → unchanged
/rfw-v2       → unchanged
/rfw-v3       → unchanged
```

`/home` is removed. The `?q=` deep-link path that previously redirected to `/home?q=` now routes to `/brain?q=` and `BrainHomeScreen.initState` consumes the query string the same way `HomeScreen` did, dispatching `SendMessage(q)` once on first frame.

### Screen layout

Single `Stack`, full-bleed three.js canvas behind everything else.

```
┌──────────────────────────────────────────────────────────┐
│                                          [legend]        │
│                                                          │
│              3D brain (auto-rotating; pauses             │
│               while inspector drawer is open)            │
│                                            ┌─────────────│
│                                            │ inspector   │
│                                            │ drawer      │
│                                            │ (~360 px)   │
│                                            └─────────────│
│                                                          │
│  [🎤 stub] [ Talk to ino…              ] [⏎ send]        │
└──────────────────────────────────────────────────────────┘
```

Removed from current `BrainScreen`:

- Top-left back-arrow (`IconButton` going to `/home`).
- "Run Travel demo" button (debug crutch).

Added:

- Stub mic `IconButton` on the left of the composer (`onPressed: null`, tooltip "voice coming in slice 2").
- Inspector drawer overlay, positioned right.

### State

`FireEvent` is the existing per-pulse model emitted by `BrainStreamService` (whatever the service currently passes to its pulse animator); the bloc consumes it directly. A new `BrainInspectorBloc` is registered alongside `InoBloc` and `PersonaBloc` in `main.dart`. Shape:

```dart
sealed class SelectedNode {}
class NeuronNode      extends SelectedNode { final BrainNode node; }
class SynapseTypeNode extends SelectedNode { final BrainNode node; }
class PulseNode       extends SelectedNode { final FireEvent pulse; }

class BrainInspectorState {
  final SelectedNode? selected;
  final FireEvent? pausedPulse;
  final Map<String, List<FireEvent>> recentByNodeId; // ring buffer, ≤25 per id
}
```

Events:

- `IngestFire(FireEvent)` — pushed by `BrainStreamService` for every pulse the gRPC stream emits. Updates `recentByNodeId[fromId]` and `recentByNodeId[toId]` (newest-first, evict at 25).
- `SelectNode(SelectedNode)` — set `selected`; if a `PulseNode`, also set `pausedPulse`.
- `Deselect()` — clear both.
- `FireTestSynapse(String neuronId, String synapseType, String payloadJson)` — calls the new gRPC method; on success, no state change (the resulting fire arrives via `IngestFire`).

Auto-rotate on the three.js `OrbitControls` is bound to `selected == null`: `_controls.autoRotate = state.selected == null`. The animator skips applying scale/emissive deltas to the mesh corresponding to `pausedPulse` so the dot freezes mid-edge.

### Three.js picking

A single `Raycaster` is constructed during `_setupScene` and reused. Each mesh added in `_addNodes` and each pulse mesh added by the brain-stream animator gets `userData['nodeId']` (for static nodes) or `userData['fireEventId']` (for pulses).

Picking is wired via a `Listener` on the `Stack` watching `onPointerDown`:

1. Convert the pointer position to NDC (`(2·x/width − 1, −(2·y/height − 1))`).
2. `_raycaster.setFromCamera(ndc, _threeJs.camera)`.
3. `intersectObjects([...all meshes], false)` — first hit wins.
4. Resolve `userData` to a `SelectedNode`, dispatch `SelectNode`.

Click on empty canvas, or `Esc`, dispatches `Deselect()`. A second click on the already-selected node also deselects.

### Inspector drawer chrome

Shared shell across the three node kinds:

- 360 px wide, slides in from the right with a 180 ms ease.
- Dark glass: `Colors.black.withAlpha(170)` with a 1 px `Colors.white.withAlpha(30)` border, matching the existing composer and legend.
- Header: domain-tinted dot (or cyan for synapse, white for pulse) + node title + close button.
- Body: kind-specific (below).

### Neuron drawer

- Header dot uses `domainColor(node.domain)`. Title is the prettified `node.label`. Below: small grey domain chip.
- **Role** — single-line description sourced from a new `Map<String, String> roleByNodeId` declared next to the topology. If absent, render "no role declared" in muted grey rather than hiding the section.
- **Recent traffic** — list of last N (≤10, bounded by ring buffer) events where this node id is `fromId` or `toId`, newest first. Row format: arrow glyph (↑ fired / ↓ handled) · synapse type · counterpart label · relative time ("2 s ago"). Tapping a row dispatches `SelectNode` with the matching pulse or synapse-type node and the drawer swaps; a back-arrow on the new view restores.
- **Fire test synapse** button at the bottom. Opens a sub-sheet:
  - Dropdown of synapse types this neuron declares as outbound. Source: topology edges of `EdgeKind.handler` whose `from` is this node id (i.e. the synapse types it's a handler of — same set the brain stream shows it firing).
  - Payload textarea, prefilled with the most recent payload of the selected type from the ring buffer, fallback `{}`.
  - "Fire" submits via `BrainInspectorBloc.add(FireTestSynapse(...))`.
  - Disabled with tooltip "no outbound synapses declared" when the dropdown would be empty.

### Synapse-type drawer

- Header cyan dot. Title is the synapse type name (already suffix-free in topology).
- **Producers** — chip row of neurons whose handler edges have this synapse as `from`.
- **Consumers** — chip row of neurons reached via this synapse's handler edges.
- **Recent fires** — last N events of this type from the ring buffer; row format: from → to · relative time · payload preview (first 80 chars, monospace; tap-expand shows full).

### Live-pulse drawer

- Header white dot. Title: "Pulse · `<synapse type>`".
- The animator pauses only the clicked pulse (it stops mid-edge, slightly enlarged + brighter); other pulses keep flowing.
- Body: from → to neurons (clickable, swap drawer view), absolute + relative timestamps, `traceparent` (monospace, click-to-copy), full JSON payload (collapsible tree).
- Closing the drawer (or selecting another node) dispatches `Deselect()` which clears `pausedPulse`; the animator resumes from where it stopped.

## Data flow

```
                        ┌──────────────────────────────────────┐
                        │  Aspire kernel silo                  │
                        │   ┌────────────────┐                 │
                        │   │ BrainTraceFilter│                │
                        │   └─────┬───────────┘                │
                        │         │ stream pulses              │
                        │         ▼                            │
                        │   IInoGateway.WatchBrainActivity     │
                        │       (existing, gains payload_json) │
                        │                                      │
                        │   IInoGateway.FireTestSynapse        │
                        │       (NEW, see proto changes below) │
                        └──────────────────┬───────────────────┘
                                           │ gRPC-Web
                                           ▼
            ┌──────────────────────────────────────────────────┐
            │ Flutter client                                   │
            │                                                  │
            │   BrainStreamService ──FireEvent──► BrainInspectorBloc │
            │           │                                ▲     │
            │           │ pulse meshes                   │     │
            │           ▼                                │     │
            │     three.js scene                  SelectNode   │
            │           │                                │     │
            │     raycaster picking ─────────────────────┘     │
            │                                                  │
            │     InspectorDrawer reads (selected,             │
            │       recentByNodeId, pausedPulse)               │
            └──────────────────────────────────────────────────┘
```

### Proto changes

Two additions to `proto/ino.proto`, both backwards-compatible:

1. `BrainPulseProto` gains `string payload_json = N;` (next available field number). Server fills it with `JsonSerializer.Serialize(envelope.Event)` truncated at 4 KB; longer payloads end with `…<truncated>`. Empty when the trace filter has no payload (rare; treated as `{}` client-side).
2. New unary rpc:
   ```proto
   rpc FireTestSynapse (FireTestSynapseRequest) returns (google.protobuf.Empty);

   message FireTestSynapseRequest {
     string neuron_id     = 1; // matches BrainNode.id
     string synapse_type  = 2; // e.g. "ChatIntent"
     string payload_json  = 3; // bounded to 16 KB
   }
   ```
   Implementation in `Ino.Kernel`:
   - Resolves `neuron_id` to a grain via `IDiscovery.LookupAsync` (already exists).
   - Validates `synapse_type` against the neuron's declared outbound types (Discovery result includes them) — rejects with `INVALID_ARGUMENT` otherwise.
   - Compiles a tiny Roslyn shim `(grain, json) => grain.RaiseAsync(JsonSerializer.Deserialize<TEvent>(json))` keyed by `synapse_type`, cached. **Roslyn cache key is `synapse_type` only — first call compiles, subsequent calls hit cache.**
   - Bounded surface: only neurons whose outbound types are declared via topology can be fired against; we do not expose a generic remote-fire API.

### Topology changes

`clients/ino.flutter/lib/screens/brain/brain_topology.dart`:

- `enum NodeKind` drops `experience` (becomes `{ neuron, synapse }`).
- `enum EdgeKind` drops `composition` (becomes `{ handler }`).
- `_experienceHalo` helper deleted.
- The six `exp.*` nodes and the eight `exp.* → ...` edges in `_build()` deleted.
- The `recall.neuron` + `recall.plan` pair collapses into a single node with id `recall` and label `Recall`. The handler edge `syn.recall_question → recall.neuron` updates to `syn.recall_question → recall`. The composition edge `exp.taxi.order_ride_home → recall.neuron` is gone with its experience anyway.
- Label rewrites applied to the remaining nodes (suffix removal):

| `id` | Old label | New label |
|---|---|---|
| `travel.flight_search` | FlightSearchNeuron | FlightSearch |
| `travel.hotel_search` | HotelSearchNeuron | HotelSearch |
| `travel.place_search` | PlaceSearchNeuron | PlaceSearch |
| `taxi.ride_request` | RideRequestNeuron | RideRequest |
| `recall` (collapsed) | RecallNeuron / RecallPlan | Recall |
| `reminders.neuron` | RemindersNeuron | Reminders |
| `location.neuron` | LocationNeuron | Location |
| `genesis.creator` | CreatorNeuron | Creator |
| `travel.plan` | PlanTripPlan | PlanTrip |
| `travel.find_flights` | FindFlightsPlan | FindFlights |
| `travel.find_hotels` | FindHotelsPlan | FindHotels |
| `travel.find_places` | FindPlacesPlan | FindPlaces |
| `taxi.order_ride` | OrderRideHomePlan | OrderRideHome |
| `reminders.plan` | SetReminderPlan | SetReminder |

Synapse-type labels are already suffix-free; they remain.

A new `Map<String, String> roleByNodeId` is added to the same file with one-line roles for each remaining neuron. Missing entries gracefully degrade in the drawer.

### Legend

Three rows reduce to two:
- "capability" (was "neuron (domain-tinted)"), domain-tinted dot.
- "signal" (was "synapse type"), cyan dot.

Yellow halo entry removed.

## Tests

1. **`brain_inspector_bloc_test.dart`** (new, `clients/ino.flutter/test/state/`). bloc_test cases:
   - Stream of `FireEvent`s populates `recentByNodeId` for sender + receiver, capped at 25, oldest evicted.
   - `SelectNode(neuron)` then `SelectNode(synapseType)` swaps `selected` and emits both transitions.
   - `PausePulse` sets `pausedPulse`; `Deselect` clears both.
   - Empty-buffer selection: `selected` is set but `recentByNodeId[id]` is empty.

2. **`brain_inspector_drawer_test.dart`** (new, `clients/ino.flutter/test/screens/brain/`). Drawer is extracted as its own widget so it pumps without a three.js context. Cases:
   - Neuron + 3 events → 3 traffic rows render.
   - Neuron with no outbound types → fire button disabled, expected tooltip.
   - Synapse-type → producers/consumers chips reflect topology.
   - Pulse → traceparent + payload tree visible; close callback resumes.

3. **`brain_topology_test.dart`** (new, `clients/ino.flutter/test/screens/brain/`). Asserts `NodeKind.values.length == 2`, no node label ends in `Neuron`/`Plan`, `recall.plan` is absent.

4. **`BrainStreamE2ETests` extension** — single new assertion in the existing test: first received `BrainPulseProto` has non-empty `PayloadJson`. No new test class.

5. **`FireTestSynapseE2ETests`** (new, `test/Ino.E2E.Tests/`). Same `InoE2ECollection` fixture. Open watch → call `FireTestSynapse(neuron_id="kernel.cortex", synapse_type="ChatIntent", payload_json="{\"text\":\"hello from test\"}")` → expect matching pulse on stream within 5 s.

### Manual verification

After `aspire start --isolated`, on the kernel-silo HTTPS URL:

1. `/` redirects to `/brain`. No back arrow.
2. Click a neuron → drawer slides in; auto-rotate pauses.
3. Type a prompt → fire-chain animates, drawer's "recent traffic" populates within ~1 s.
4. Click a moving pulse → that pulse freezes brighter; others keep flowing; drawer shows traceparent + payload.
5. Close drawer → pulse resumes; auto-rotate resumes.
6. Open neuron drawer → "Fire test synapse" → submit → drawer's recent traffic shows the new fire and the brain pulses correspondingly.
7. Aspire dashboard Traces tab shows the `FireTestSynapse` rpc → `fire` span chain linked by traceparent.

## Risks

- **Three.js raycaster on CanvasKit** — `three_js` package's web target uses the gles_bindings shim already loaded for slice C.3. Picking via raycaster is documented in the package; if it misbehaves on CanvasKit (we have no priors), fallback is per-mesh `MeshUserData` flags + a manual screen-space hit test (slower but no GL state needed). Prefer the raycaster path; only fall back if it produces wrong intersections.
- **Stream cardinality** — if pulses arrive faster than the bloc can drain its event queue, the drawer's buffer shows stale-then-jumpy data. The 25-entry cap and event coalescing in `IngestFire` are designed to absorb a typical multi-domain demo; sustained > 50 pulses/s would need a different design (rate limiting on server). Out of scope for v0.1 demos which are single-user.
- **Roslyn shim cache leak** — `FireTestSynapse` caches one compiled delegate per `synapse_type`. The cache lives for the kernel process lifetime; fine because the surface area is bounded by the topology, not user input. No invalidation needed within a process.

## Going-forward naming rule (slice 2+)

When concrete capabilities are added in later slices, drop the suffix at the C# class level too: `Recorder : Neuron<RecordingEvent>`, `Recording : ISynapse<...>`. Base classes keep `Neuron`/`Synapse` — they are framework primitives. Existing concretes (`RideRequestNeuron`, `PlanTripPlan`, etc.) are not renamed in this slice; that is a separate chore.
