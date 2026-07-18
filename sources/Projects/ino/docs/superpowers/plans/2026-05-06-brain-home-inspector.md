# Brain home + click-to-inspect Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unify `/brain` into the only home page, make every neuron / synapse-type / live pulse on the 3D brain clickable with a right-side inspector drawer, kill the experience-halo primitive, and drop `Neuron`/`Plan` suffixes from user-facing labels.

**Architecture:** Pure Flutter UX work plus one new gRPC method on the kernel (`FireTestSynapse`) and a new `payload_json` field on the existing `BrainPulseProto`. Inspector state lives in a new `BrainInspectorBloc` next to `InoBloc`/`PersonaBloc`. Three.js raycaster picking dispatches `SelectNode` events. A client-side ring buffer (last 25 events per node id) populates the drawer's "recent traffic" — no server-side history.

**Tech Stack:** Flutter 3.41 (CanvasKit), `flutter_bloc`, `bloc_test`, `three_js`, `go_router`, gRPC-Web, ASP.NET Core gRPC, Orleans 10, `xunit` for E2E.

---

## File map

**New files:**
- `clients/ino.flutter/lib/state/brain_inspector_bloc.dart`
- `clients/ino.flutter/lib/screens/brain/brain_home_screen.dart` (replaces `brain_screen.dart`)
- `clients/ino.flutter/lib/screens/brain/brain_inspector_drawer.dart`
- `clients/ino.flutter/lib/screens/brain/brain_picking.dart` (raycaster wiring)
- `clients/ino.flutter/lib/screens/brain/brain_pulse_animator.dart` (animated cyan dots between fromId/toId)
- `clients/ino.flutter/lib/screens/brain/brain_roles.dart` (`roleByNodeId` map)
- `clients/ino.flutter/test/state/brain_inspector_bloc_test.dart`
- `clients/ino.flutter/test/screens/brain/brain_inspector_drawer_test.dart`
- `clients/ino.flutter/test/screens/brain/brain_topology_test.dart`
- `test/Ino.E2E.Tests/FireTestSynapseE2ETests.cs`

**Modified files:**
- `src/Ino.Gateway.Grpc/Protos/ino.proto`
- `clients/ino.flutter/protos/ino.proto` (kept in lockstep with the server proto)
- `src/Ino.Core/Brain/BrainPulse.cs` (add `PayloadJson`)
- `src/Ino.Core.Hosting/Brain/BrainTraceFilter.cs` (serialize `context.Arguments[0]`)
- `src/Ino.Gateway.Grpc/Services/InoGrpcService.cs` (`MapPulse` adds `PayloadJson`; new `FireTestSynapse` override)
- `src/Ino.Gateway/IInoGateway.cs` + impl (new `FireTestSynapseAsync`)
- `test/Ino.E2E.Tests/BrainStreamE2ETests.cs` (assert `PayloadJson` non-empty)
- `test/Ino.Core.Hosting.Tests/BrainTraceFilterTests.cs` (assert payload serialization)
- `clients/ino.flutter/lib/screens/brain/brain_topology.dart` (drop experiences, collapse recall, rename labels)
- `clients/ino.flutter/lib/services/brain_stream_service.dart` (dispatch to bloc instead of log-only)
- `clients/ino.flutter/lib/main.dart` (register `BrainInspectorBloc`)
- `clients/ino.flutter/lib/app.dart` (delete `/home`, redirect logic)

**Deleted files:**
- `clients/ino.flutter/lib/screens/home/home_screen.dart`
- `clients/ino.flutter/lib/screens/brain/brain_screen.dart`

---

## Task 1: Topology cleanup — drop experiences, collapse recall, rewrite labels

**Files:**
- Modify: `clients/ino.flutter/lib/screens/brain/brain_topology.dart`
- Create: `clients/ino.flutter/lib/screens/brain/brain_roles.dart`
- Create test: `clients/ino.flutter/test/screens/brain/brain_topology_test.dart`

- [ ] **Step 1: Write the failing topology test**

Create `clients/ino.flutter/test/screens/brain/brain_topology_test.dart`:

```dart
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/screens/brain/brain_topology.dart';

void main() {
  group('BrainTopology', () {
    final topology = BrainTopology.load();

    test('NodeKind has only neuron and synapse', () {
      expect(NodeKind.values.map((e) => e.name).toSet(),
          equals({'neuron', 'synapse'}));
    });

    test('EdgeKind has only handler', () {
      expect(EdgeKind.values.map((e) => e.name).toSet(),
          equals({'handler'}));
    });

    test('no node id starts with "exp."', () {
      final expIds = topology.nodes.where((n) => n.id.startsWith('exp.')).toList();
      expect(expIds, isEmpty);
    });

    test('no neuron label ends in "Neuron" or "Plan"', () {
      final bad = topology.nodes
          .where((n) => n.kind == NodeKind.neuron)
          .where((n) => n.label.endsWith('Neuron') || n.label.endsWith('Plan'))
          .toList();
      expect(bad, isEmpty,
          reason: 'offending labels: ${bad.map((n) => n.label).toList()}');
    });

    test('recall collapses to a single node with id "recall"', () {
      final recallNodes =
          topology.nodes.where((n) => n.id.startsWith('recall')).toList();
      expect(recallNodes, hasLength(1));
      expect(recallNodes.single.id, equals('recall'));
      expect(recallNodes.single.label, equals('Recall'));
    });

    test('every edge points to an existing node', () {
      final ids = topology.nodes.map((n) => n.id).toSet();
      for (final e in topology.edges) {
        expect(ids, contains(e.from), reason: 'missing from: ${e.from}');
        expect(ids, contains(e.to), reason: 'missing to: ${e.to}');
      }
    });
  });
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd clients/ino.flutter && flutter test test/screens/brain/brain_topology_test.dart`
Expected: FAIL — `NodeKind.values` still contains `experience`, labels still end in `Neuron`/`Plan`, etc.

- [ ] **Step 3: Edit `brain_topology.dart`**

Replace `enum NodeKind { neuron, synapse, experience }` with `enum NodeKind { neuron, synapse }`.
Replace `enum EdgeKind { handler, composition }` with `enum EdgeKind { handler }`.
Delete the `_experienceHalo` helper function entirely.
In `_build()`:
- Delete the six `_experienceHalo(...)` lines from the `nodes` list.
- Delete the eight `BrainEdge(...)` entries whose `from` starts with `exp.` (they all have `kind: EdgeKind.composition`).
- Replace the two recall `_placedNeuron` lines:
  ```dart
  _placedNeuron('recall.neuron', 'RecallNeuron', 'recall', 0),
  _placedNeuron('recall.plan',   'RecallPlan',   'recall', 1),
  ```
  with a single line:
  ```dart
  _placedNeuron('recall', 'Recall', 'recall', 0),
  ```
- Update the handler edge that referenced `recall.neuron`:
  ```dart
  const BrainEdge(from: 'syn.recall_question', to: 'recall.neuron', kind: EdgeKind.handler),
  ```
  to:
  ```dart
  const BrainEdge(from: 'syn.recall_question', to: 'recall', kind: EdgeKind.handler),
  ```
- Apply the label rewrites to the remaining `_placedNeuron` calls (id stays the same, only the second arg changes):

  | id | old label | new label |
  |---|---|---|
  | `travel.flight_search` | `FlightSearchNeuron` | `FlightSearch` |
  | `travel.hotel_search` | `HotelSearchNeuron` | `HotelSearch` |
  | `travel.place_search` | `PlaceSearchNeuron` | `PlaceSearch` |
  | `taxi.ride_request` | `RideRequestNeuron` | `RideRequest` |
  | `reminders.neuron` | `RemindersNeuron` | `Reminders` |
  | `location.neuron` | `LocationNeuron` | `Location` |
  | `genesis.creator` | `CreatorNeuron` | `Creator` |
  | `travel.plan` | `PlanTripPlan` | `PlanTrip` |
  | `travel.find_flights` | `FindFlightsPlan` | `FindFlights` |
  | `travel.find_hotels` | `FindHotelsPlan` | `FindHotels` |
  | `travel.find_places` | `FindPlacesPlan` | `FindPlaces` |
  | `taxi.order_ride` | `OrderRideHomePlan` | `OrderRideHome` |
  | `reminders.plan` | `SetReminderPlan` | `SetReminder` |

  Leave `Cortex`, `Discovery`, `Gateway`, `Identity`, `Auth`, `FlightMonitor`, `MissedIntentTracker`, `NeuronOptimizer`, `ProposalLog` as-is (no offending suffix).

Update the doc comment at the top of the file to remove the experience-halo paragraph; replace with a one-line note that experiences were removed in slice C.4 and may return as emergent quality-test outputs in a later slice.

- [ ] **Step 4: Create `brain_roles.dart`**

```dart
// One-line role descriptions surfaced in the inspector drawer's neuron view.
// Keys are BrainNode.id; missing entries render "no role declared" in the UI.
const Map<String, String> roleByNodeId = {
  'kernel.cortex': 'Routes user prompts to the matching capability.',
  'kernel.discovery': 'Tracks every neuron registered in the cluster.',
  'kernel.gateway': 'Bridges the Flutter client to the silo over gRPC.',
  'identity.neuron': 'Owns who the user is and which session they are in.',
  'identity.auth': 'Validates the session token on each request.',
  'travel.plan': 'Plans a complete trip end-to-end.',
  'travel.find_flights': 'Picks flights matching the trip brief.',
  'travel.find_hotels': 'Picks hotels matching the trip brief.',
  'travel.find_places': 'Picks places of interest at the destination.',
  'travel.flight_search': 'Calls TripRadar for live flight search.',
  'travel.hotel_search': 'Calls TripRadar for live hotel search.',
  'travel.place_search': 'Calls TripRadar for live place search.',
  'travel.flight_monitor': 'Watches booked flights for delays and changes.',
  'taxi.order_ride': 'Plans an end-to-end ride request.',
  'taxi.ride_request': 'Calls Uber for a live ride.',
  'recall': 'Answers questions about prior synapses.',
  'reminders.neuron': 'Owns the per-user reminder catalogue.',
  'reminders.plan': 'Schedules a reminder from a user prompt.',
  'location.neuron': 'Tracks the current location and known places.',
  'genesis.creator': 'Drafts new neurons proposed by the L1 loop.',
  'genesis.missed': 'Records prompts the cluster could not route.',
  'genesis.optimizer': 'Tunes routing and decay heuristics.',
  'genesis.proposal_log': 'Audit trail of every L1 proposal.',
};
```

- [ ] **Step 5: Run test to verify it passes**

Run: `cd clients/ino.flutter && flutter test test/screens/brain/brain_topology_test.dart`
Expected: PASS — all six tests green.

- [ ] **Step 6: Commit**

```bash
git add clients/ino.flutter/lib/screens/brain/brain_topology.dart clients/ino.flutter/lib/screens/brain/brain_roles.dart clients/ino.flutter/test/screens/brain/brain_topology_test.dart
git commit -m "refactor(poc-flutter): drop experience halos, collapse recall, rename suffix-y labels (slice C.4 task 1)"
```

---

## Task 2: BrainInspectorBloc — state, events, ring buffer

**Files:**
- Create: `clients/ino.flutter/lib/state/brain_inspector_bloc.dart`
- Create test: `clients/ino.flutter/test/state/brain_inspector_bloc_test.dart`

- [ ] **Step 1: Write the failing bloc test**

Create `clients/ino.flutter/test/state/brain_inspector_bloc_test.dart`:

```dart
import 'package:bloc_test/bloc_test.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/state/brain_inspector_bloc.dart';

FireEvent _fire({
  String fromId = 'a',
  String toId = 'b',
  String type = 'ChatIntent',
  String payload = '{"x":1}',
  int tsMs = 0,
}) =>
    FireEvent(
      id: '$fromId>$toId@$tsMs',
      traceParent: '00-trace-span-01',
      synapseType: type,
      fromId: fromId,
      toId: toId,
      payloadJson: payload,
      timestampUnixMs: tsMs,
    );

void main() {
  group('BrainInspectorBloc', () {
    test('IngestFire pushes onto sender and receiver buffers, newest first', () {
      final bloc = BrainInspectorBloc();
      bloc.add(IngestFire(_fire(fromId: 'cortex', toId: 'flightSearch', tsMs: 1)));
      bloc.add(IngestFire(_fire(fromId: 'cortex', toId: 'hotelSearch',  tsMs: 2)));

      expect(bloc.state.recentByNodeId['cortex']!.map((f) => f.toId),
          equals(['hotelSearch', 'flightSearch']));
      expect(bloc.state.recentByNodeId['flightSearch']!.single.fromId, equals('cortex'));
    });

    test('ring buffer caps at 25 per id, evicting oldest', () {
      final bloc = BrainInspectorBloc();
      for (var i = 0; i < 30; i++) {
        bloc.add(IngestFire(_fire(fromId: 'cortex', toId: 't$i', tsMs: i)));
      }
      expect(bloc.state.recentByNodeId['cortex'], hasLength(25));
      // newest first → t29 at head, t5 at tail (t0..t4 evicted)
      expect(bloc.state.recentByNodeId['cortex']!.first.toId, equals('t29'));
      expect(bloc.state.recentByNodeId['cortex']!.last.toId, equals('t5'));
    });

    blocTest<BrainInspectorBloc, BrainInspectorState>(
      'SelectNode then Deselect emits selected then null',
      build: BrainInspectorBloc.new,
      act: (b) {
        b.add(SelectNeuron(nodeId: 'cortex'));
        b.add(Deselect());
      },
      expect: () => [
        isA<BrainInspectorState>().having((s) => s.selected, 'selected', isA<NeuronSelection>()),
        isA<BrainInspectorState>().having((s) => s.selected, 'selected', isNull),
      ],
    );

    blocTest<BrainInspectorBloc, BrainInspectorState>(
      'PausePulse sets pausedPulse; Deselect clears it',
      build: BrainInspectorBloc.new,
      act: (b) {
        final p = _fire(tsMs: 7);
        b.add(PausePulse(pulse: p));
        b.add(Deselect());
      },
      expect: () => [
        isA<BrainInspectorState>().having((s) => s.pausedPulse?.timestampUnixMs, 'paused', 7),
        isA<BrainInspectorState>().having((s) => s.pausedPulse, 'paused', isNull),
      ],
    );
  });
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd clients/ino.flutter && flutter test test/state/brain_inspector_bloc_test.dart`
Expected: FAIL — `BrainInspectorBloc` not defined.

- [ ] **Step 3: Implement the bloc**

Create `clients/ino.flutter/lib/state/brain_inspector_bloc.dart`:

```dart
import 'package:flutter_bloc/flutter_bloc.dart';

class FireEvent {
  const FireEvent({
    required this.id,
    required this.traceParent,
    required this.synapseType,
    required this.fromId,
    required this.toId,
    required this.payloadJson,
    required this.timestampUnixMs,
  });

  final String id;
  final String traceParent;
  final String synapseType;
  final String fromId;
  final String toId;
  final String payloadJson;
  final int timestampUnixMs;
}

sealed class Selection {}
class NeuronSelection extends Selection {
  NeuronSelection(this.nodeId);
  final String nodeId;
}
class SynapseTypeSelection extends Selection {
  SynapseTypeSelection(this.nodeId);
  final String nodeId;
}
class PulseSelection extends Selection {
  PulseSelection(this.pulse);
  final FireEvent pulse;
}

class BrainInspectorState {
  const BrainInspectorState({
    this.selected,
    this.pausedPulse,
    this.recentByNodeId = const {},
  });

  final Selection? selected;
  final FireEvent? pausedPulse;
  final Map<String, List<FireEvent>> recentByNodeId;

  BrainInspectorState copyWith({
    Selection? selected,
    bool clearSelected = false,
    FireEvent? pausedPulse,
    bool clearPaused = false,
    Map<String, List<FireEvent>>? recentByNodeId,
  }) =>
      BrainInspectorState(
        selected: clearSelected ? null : (selected ?? this.selected),
        pausedPulse: clearPaused ? null : (pausedPulse ?? this.pausedPulse),
        recentByNodeId: recentByNodeId ?? this.recentByNodeId,
      );
}

sealed class BrainInspectorEvent {}
class IngestFire extends BrainInspectorEvent {
  IngestFire(this.fire);
  final FireEvent fire;
}
class SelectNeuron extends BrainInspectorEvent {
  SelectNeuron({required this.nodeId});
  final String nodeId;
}
class SelectSynapseType extends BrainInspectorEvent {
  SelectSynapseType({required this.nodeId});
  final String nodeId;
}
class PausePulse extends BrainInspectorEvent {
  PausePulse({required this.pulse});
  final FireEvent pulse;
}
class Deselect extends BrainInspectorEvent {}

const int _ringBufferCap = 25;

class BrainInspectorBloc extends Bloc<BrainInspectorEvent, BrainInspectorState> {
  BrainInspectorBloc() : super(const BrainInspectorState()) {
    on<IngestFire>((e, emit) {
      final next = Map<String, List<FireEvent>>.from(state.recentByNodeId);
      _push(next, e.fire.fromId, e.fire);
      _push(next, e.fire.toId, e.fire);
      emit(state.copyWith(recentByNodeId: next));
    });
    on<SelectNeuron>((e, emit) =>
        emit(state.copyWith(selected: NeuronSelection(e.nodeId))));
    on<SelectSynapseType>((e, emit) =>
        emit(state.copyWith(selected: SynapseTypeSelection(e.nodeId))));
    on<PausePulse>((e, emit) => emit(state.copyWith(
        selected: PulseSelection(e.pulse), pausedPulse: e.pulse)));
    on<Deselect>((e, emit) =>
        emit(state.copyWith(clearSelected: true, clearPaused: true)));
  }

  static void _push(Map<String, List<FireEvent>> map, String key, FireEvent fire) {
    final list = List<FireEvent>.from(map[key] ?? const [])..insert(0, fire);
    if (list.length > _ringBufferCap) list.removeRange(_ringBufferCap, list.length);
    map[key] = list;
  }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd clients/ino.flutter && flutter test test/state/brain_inspector_bloc_test.dart`
Expected: PASS — all four cases green.

- [ ] **Step 5: Commit**

```bash
git add clients/ino.flutter/lib/state/brain_inspector_bloc.dart clients/ino.flutter/test/state/brain_inspector_bloc_test.dart
git commit -m "feat(poc-flutter): BrainInspectorBloc with selection + ring buffer (slice C.4 task 2)"
```

---

## Task 3: Add `payload_json` to BrainPulse + BrainTraceFilter + proto

**Files:**
- Modify: `src/Ino.Gateway.Grpc/Protos/ino.proto`
- Modify: `clients/ino.flutter/protos/ino.proto`
- Modify: `src/Ino.Core/Brain/BrainPulse.cs`
- Modify: `src/Ino.Core.Hosting/Brain/BrainTraceFilter.cs`
- Modify: `src/Ino.Gateway.Grpc/Services/InoGrpcService.cs` (`MapPulse`)
- Modify: `test/Ino.Core.Hosting.Tests/BrainTraceFilterTests.cs`
- Modify: `test/Ino.E2E.Tests/BrainStreamE2ETests.cs`

- [ ] **Step 1: Edit both proto files (server + client copies must stay in lockstep)**

In `src/Ino.Gateway.Grpc/Protos/ino.proto` and `clients/ino.flutter/protos/ino.proto`, append to `BrainPulseProto` (after the existing `timestamp_unix_ms = 9;`):

```proto
  // Slice C.4 — JSON-serialized first argument of the grain call (truncated to
  // 4 KB). Empty when the call had no args; truncated payloads end with "…<truncated>".
  string payload_json = 10;
```

- [ ] **Step 2: Add `PayloadJson` to the `BrainPulse` record**

In `src/Ino.Core/Brain/BrainPulse.cs`, append a property:

```csharp
[GenerateSerializer]
public sealed record BrainPulse(
    [property: Id(0)] string TraceParent,
    [property: Id(1)] string InoInstanceId,
    [property: Id(2)] string UserId,
    [property: Id(3)] string FromGrain,
    [property: Id(4)] string ToGrain,
    [property: Id(5)] string MethodName,
    [property: Id(6)] long DurationMs,
    [property: Id(7)] BrainPulseStatus Status,
    [property: Id(8)] long TimestampUnixMs,
    [property: Id(9)] string PayloadJson);
```

- [ ] **Step 3: Update the `BrainTraceFilterTests` expectation**

In `test/Ino.Core.Hosting.Tests/BrainTraceFilterTests.cs`, find the existing test that asserts on the emitted pulse fields and add an assertion that `pulse.PayloadJson` is non-empty when the call had at least one argument. If the existing test does not pass any arguments, add a second test:

```csharp
[Fact]
public async Task Invoke_serializes_first_argument_to_payload_json()
{
    var sink = new CapturingSink();
    var filter = new BrainTraceFilter(sink, NullLogger<BrainTraceFilter>.Instance);
    var ctx = new FakeGrainCallContext(
        targetGrainId: "test/grain",
        methodName: "DoThing",
        arguments: new object[] { new { Hello = "world" } });

    await filter.Invoke(ctx);

    var pulse = Assert.Single(sink.Captured);
    Assert.False(string.IsNullOrEmpty(pulse.PayloadJson));
    Assert.Contains("Hello", pulse.PayloadJson);
}
```

(If `FakeGrainCallContext` and `CapturingSink` already exist in the test file, reuse them; otherwise look at the existing test for the pattern and follow it.)

- [ ] **Step 4: Implement the payload serialization in `BrainTraceFilter`**

Edit `src/Ino.Core.Hosting/Brain/BrainTraceFilter.cs`. Add a static helper:

```csharp
private const int PayloadCapBytes = 4096;
private static readonly JsonSerializerOptions PayloadJsonOptions = new()
{
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    ReferenceHandler = ReferenceHandler.IgnoreCycles,
    WriteIndented = false,
};

private static string SerializePayload(IIncomingGrainCallContext context)
{
    var args = context.Arguments;
    if (args is null || args.Length == 0) return string.Empty;
    try
    {
        var json = JsonSerializer.Serialize(args[0], PayloadJsonOptions);
        if (json.Length > PayloadCapBytes)
        {
            return json[..PayloadCapBytes] + "…<truncated>";
        }
        return json;
    }
    catch
    {
        // Best-effort observability — never fail the call because of the brain stream.
        return string.Empty;
    }
}
```

In `EmitPulseAsync`, populate the new field:

```csharp
var pulse = new BrainPulse(
    TraceParent: Activity.Current?.Id ?? string.Empty,
    InoInstanceId: sessionId,
    UserId: userId,
    FromGrain: string.Empty,
    ToGrain: context.TargetContext?.GrainId.ToString() ?? string.Empty,
    MethodName: MethodNameOverrideForTests ?? context.ImplementationMethod?.Name ?? string.Empty,
    DurationMs: (long)elapsed.TotalMilliseconds,
    Status: caught is null ? BrainPulseStatus.Ok : BrainPulseStatus.Failed,
    TimestampUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
    PayloadJson: SerializePayload(context));
```

Add the missing `using` directives at the top of the file if absent:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
```

- [ ] **Step 5: Wire `payload_json` into `MapPulse`**

In `src/Ino.Gateway.Grpc/Services/InoGrpcService.cs`, extend `MapPulse`:

```csharp
private static BrainPulseProto MapPulse(BrainPulse pulse) => new()
{
    TraceParent = pulse.TraceParent,
    InoInstanceId = pulse.InoInstanceId,
    UserId = pulse.UserId,
    FromGrain = pulse.FromGrain,
    ToGrain = pulse.ToGrain,
    MethodName = pulse.MethodName,
    DurationMs = pulse.DurationMs,
    Status = pulse.Status switch
    {
        BrainPulseStatus.Ok => BrainPulseStatusProto.BrainPulseStatusOk,
        BrainPulseStatus.Failed => BrainPulseStatusProto.BrainPulseStatusFailed,
        _ => BrainPulseStatusProto.BrainPulseStatusOk,
    },
    TimestampUnixMs = pulse.TimestampUnixMs,
    PayloadJson = pulse.PayloadJson ?? string.Empty,
};
```

- [ ] **Step 6: Update existing E2E test**

In `test/Ino.E2E.Tests/BrainStreamE2ETests.cs`, after the first received `BrainPulseProto` is asserted, add:

```csharp
Assert.False(string.IsNullOrEmpty(received.PayloadJson),
    "BrainPulseProto.PayloadJson should be populated for the AskIno call");
```

- [ ] **Step 7: Build + test**

```
dotnet build ino.slnx
dotnet test test/Ino.Core.Hosting.Tests
dotnet test test/Ino.E2E.Tests --filter "FullyQualifiedName~BrainStreamE2ETests"
```

Expected: all green. The unit test asserts the new field is filled; the E2E test asserts it survives the full Aspire/gRPC roundtrip.

- [ ] **Step 8: Commit**

```bash
git add src/Ino.Gateway.Grpc/Protos/ino.proto clients/ino.flutter/protos/ino.proto src/Ino.Core/Brain/BrainPulse.cs src/Ino.Core.Hosting/Brain/BrainTraceFilter.cs src/Ino.Gateway.Grpc/Services/InoGrpcService.cs test/Ino.Core.Hosting.Tests/BrainTraceFilterTests.cs test/Ino.E2E.Tests/BrainStreamE2ETests.cs
git commit -m "feat(poc): add payload_json to BrainPulse so the inspector can show synapse contents (slice C.4 task 3)"
```

---

## Task 4: New `FireTestSynapse` RPC — proto + gateway delegation

**Files:**
- Modify: `src/Ino.Gateway.Grpc/Protos/ino.proto`
- Modify: `clients/ino.flutter/protos/ino.proto`
- Modify: `src/Ino.Gateway/IInoGateway.cs` and its default implementation file
- Modify: `src/Ino.Gateway.Grpc/Services/InoGrpcService.cs`
- Create: `test/Ino.E2E.Tests/FireTestSynapseE2ETests.cs`

The implementation reuses the existing `IInoGateway.FireSynapseAsync` machinery: the new RPC accepts a synapse type name + JSON payload, wraps the JSON into a single-entry args dictionary under the conventional key `__payload_json`, and delegates. The gateway's existing fire path already routes by `verb` via Discovery; we only need to teach `FireSynapseAsync` to honour the JSON-payload arg as the deserialized event.

- [ ] **Step 1: Write the failing E2E test**

Create `test/Ino.E2E.Tests/FireTestSynapseE2ETests.cs`:

```csharp
using System.Threading.Channels;
using Grpc.Core;
using Grpc.Net.Client;
using Ino.Grpc;
using Ino.Testing;
using Xunit;

namespace Ino.E2E.Tests;

[Collection(nameof(InoE2ECollection))]
public sealed class FireTestSynapseE2ETests(InoTestAppHost<Projects.Ino_AppHost> fixture)
{
    [Fact]
    public async Task FireTestSynapse_emits_a_brain_pulse_for_the_target_synapse()
    {
        using var http = fixture.CreateKernelHttpClient();
        var kernelUrl = http.BaseAddress!.ToString().TrimEnd('/');

        using var channel = GrpcChannel.ForAddress(kernelUrl, new GrpcChannelOptions
        {
            HttpHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            },
        });

        var ino = new global::Ino.Grpc.Ino.InoClient(channel);

        var pulses = Channel.CreateUnbounded<BrainPulseProto>();
        var watchCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var watch = ino.WatchBrainActivity(new BrainWatchRequest(), cancellationToken: watchCts.Token);
        var pump = Task.Run(async () =>
        {
            try
            {
                while (await watch.ResponseStream.MoveNext(watchCts.Token))
                {
                    pulses.Writer.TryWrite(watch.ResponseStream.Current);
                }
            }
            catch { /* stream cancelled */ }
        }, TestContext.Current.CancellationToken);

        await ino.FireTestSynapseAsync(new FireTestSynapseRequest
        {
            SynapseType = "ChatIntent",
            PayloadJson = "{\"text\":\"hello from test\",\"userId\":\"alice\"}",
        });

        var deadline = DateTimeOffset.UtcNow.AddSeconds(10);
        var sawIt = false;
        while (DateTimeOffset.UtcNow < deadline)
        {
            using var oneCts = CancellationTokenSource.CreateLinkedTokenSource(watchCts.Token);
            oneCts.CancelAfter(TimeSpan.FromMilliseconds(500));
            try
            {
                var pulse = await pulses.Reader.ReadAsync(oneCts.Token);
                if (pulse.PayloadJson.Contains("hello from test"))
                {
                    sawIt = true;
                    break;
                }
            }
            catch (OperationCanceledException) { /* try again */ }
        }
        Assert.True(sawIt, "expected a brain pulse carrying the test payload to arrive");

        watchCts.Cancel();
        await pump;
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test test/Ino.E2E.Tests --filter "FullyQualifiedName~FireTestSynapse"`
Expected: FAIL — `FireTestSynapseAsync` not generated yet (proto doesn't define the rpc).

- [ ] **Step 3: Add the proto definitions to both ino.proto copies**

In `src/Ino.Gateway.Grpc/Protos/ino.proto` and `clients/ino.flutter/protos/ino.proto`, after the existing `WatchBrainActivity` rpc inside the `service Ino { ... }` block:

```proto
  // Slice C.4 — fire a synapse from the inspector "Fire test synapse" button.
  // The gateway resolves the synapse type via Discovery and delegates to the
  // existing fire pipeline. payload_json is deserialized into the resolved
  // synapse type before dispatch.
  rpc FireTestSynapse(FireTestSynapseRequest) returns (FireTestSynapseResponse);
```

At the bottom of each file:

```proto
message FireTestSynapseRequest {
  string synapse_type = 1;
  string payload_json = 2;
  // Optional client-side label used only for logs; the actual handler grain is
  // determined by Discovery on the synapse_type.
  string source_node_id = 3;
}

message FireTestSynapseResponse {
  bool ok = 1;
  string error = 2;
}
```

- [ ] **Step 4: Add the method to `IInoGateway`**

In `src/Ino.Gateway/IInoGateway.cs` (locate via `grep -n "interface IInoGateway"`), add:

```csharp
Task<FireSynapseOutcome> FireTestSynapseAsync(
    string synapseType,
    string payloadJson,
    string sourceNodeId,
    string userId,
    CancellationToken ct);
```

If `FireSynapseOutcome` is the existing return type of `FireSynapseAsync`, reuse it. If not, locate the existing return type and reuse it; do not invent a new one.

- [ ] **Step 5: Implement `FireTestSynapseAsync` in the default gateway**

In the file that holds the default `IInoGateway` implementation (likely `src/Ino.Gateway/InoGateway.cs` — locate via `grep -n "FireSynapseAsync" src/Ino.Gateway`), add:

```csharp
public Task<FireSynapseOutcome> FireTestSynapseAsync(
    string synapseType,
    string payloadJson,
    string sourceNodeId,
    string userId,
    CancellationToken ct)
{
    // Delegate to the existing fire pipeline. The convention `__payload_json` is
    // recognised by FireSynapseAsync (see below) as a fully-formed event body
    // that should be deserialized into the resolved synapse type instead of
    // being mapped from per-key string args.
    var args = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["__payload_json"] = payloadJson,
        ["__source_node_id"] = sourceNodeId,
    };
    return FireSynapseAsync(synapseType, args, correlationId: string.Empty, userId, ct);
}
```

In the existing `FireSynapseAsync`, add a fast-path at the top: if `args` contains key `__payload_json`, the gateway should deserialize that JSON into the resolved synapse type and dispatch. Locate the line where the synapse type is resolved (search the method body for `LookupCanonical` / `Discovery`) and inject:

```csharp
if (args.TryGetValue("__payload_json", out var payloadJson))
{
    var eventInstance = JsonSerializer.Deserialize(payloadJson, resolvedSynapseType)
        ?? throw new ArgumentException($"payload_json could not deserialize as {resolvedSynapseType.Name}");
    // dispatch eventInstance through the same path the args-keyed branch uses
    // (look for the existing call to grain.HandleAsync / grain.RaiseAsync just below)
    return await DispatchResolvedAsync(eventInstance, /* preserve other params */);
}
```

If the existing dispatch flow does not factor out into a helper, factor it out so both the args-built branch and the JSON branch can call the same downstream code. Keep the change minimal.

- [ ] **Step 6: Wire the gRPC handler**

In `src/Ino.Gateway.Grpc/Services/InoGrpcService.cs`, after the existing `FireSynapse` override, add:

```csharp
public override async Task<FireTestSynapseResponse> FireTestSynapse(
    FireTestSynapseRequest request,
    ServerCallContext context)
{
    if (string.IsNullOrWhiteSpace(request.SynapseType))
        throw new RpcException(new Status(StatusCode.InvalidArgument, "synapse_type is required"));

    try
    {
        var outcome = await gateway.FireTestSynapseAsync(
            request.SynapseType,
            request.PayloadJson ?? string.Empty,
            request.SourceNodeId ?? string.Empty,
            userId: "inspector",
            context.CancellationToken);

        return new FireTestSynapseResponse { Ok = outcome.Success, Error = outcome.Reply ?? string.Empty };
    }
    catch (ArgumentException ex)
    {
        throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
    }
    catch (NotSupportedException ex)
    {
        throw new RpcException(new Status(StatusCode.Unimplemented, ex.Message));
    }
}
```

- [ ] **Step 7: Run the E2E test**

```
dotnet build ino.slnx
dotnet test test/Ino.E2E.Tests --filter "FullyQualifiedName~FireTestSynapse"
```

Expected: PASS within ~10 s.

- [ ] **Step 8: Commit**

```bash
git add src/Ino.Gateway.Grpc/Protos/ino.proto clients/ino.flutter/protos/ino.proto src/Ino.Gateway/IInoGateway.cs src/Ino.Gateway/InoGateway.cs src/Ino.Gateway.Grpc/Services/InoGrpcService.cs test/Ino.E2E.Tests/FireTestSynapseE2ETests.cs
git commit -m "feat(poc): FireTestSynapse rpc — fire a synapse with a JSON payload from the inspector (slice C.4 task 4)"
```

---

## Task 5: Wire BrainStreamService into BrainInspectorBloc

**Files:**
- Modify: `clients/ino.flutter/lib/services/brain_stream_service.dart`
- Modify: `clients/ino.flutter/lib/main.dart`
- Modify: `clients/ino.flutter/test/state/brain_inspector_bloc_test.dart` (add an integration-style test)

- [ ] **Step 1: Extend the bloc test with a service-shape case**

Append to `clients/ino.flutter/test/state/brain_inspector_bloc_test.dart`:

```dart
test('FireEvent.fromPulse maps proto fields correctly', () {
  // proto-shaped fixture; mirrors what BrainStreamService will produce.
  final raw = (
    fromGrain: 'cortex',
    toGrain: 'travel.flight_search',
    methodName: 'HandleAsync',
    payloadJson: '{"text":"hi"}',
    traceParent: '00-trace-span-01',
    timestampUnixMs: 12345,
  );
  final fire = FireEvent.fromBrainPulse(
    fromGrain: raw.fromGrain,
    toGrain: raw.toGrain,
    methodName: raw.methodName,
    payloadJson: raw.payloadJson,
    traceParent: raw.traceParent,
    timestampUnixMs: raw.timestampUnixMs,
  );
  expect(fire.fromId, equals('cortex'));
  expect(fire.toId, equals('travel.flight_search'));
  expect(fire.synapseType, equals('HandleAsync'));
  expect(fire.payloadJson, equals('{"text":"hi"}'));
});
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd clients/ino.flutter && flutter test test/state/brain_inspector_bloc_test.dart`
Expected: FAIL — `FireEvent.fromBrainPulse` not defined.

- [ ] **Step 3: Add the factory to `FireEvent`**

In `clients/ino.flutter/lib/state/brain_inspector_bloc.dart`, add a static method on `FireEvent`:

```dart
factory FireEvent.fromBrainPulse({
  required String fromGrain,
  required String toGrain,
  required String methodName,
  required String payloadJson,
  required String traceParent,
  required int timestampUnixMs,
}) {
  // The grain-call's method name doubles as the synapse type label for the UI.
  // Empty fromGrain ("system" calls have no source) folds to the toGrain so
  // the buffer still keys on something useful.
  return FireEvent(
    id: '$traceParent#$timestampUnixMs',
    traceParent: traceParent,
    synapseType: methodName,
    fromId: fromGrain.isEmpty ? toGrain : fromGrain,
    toId: toGrain,
    payloadJson: payloadJson,
    timestampUnixMs: timestampUnixMs,
  );
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd clients/ino.flutter && flutter test test/state/brain_inspector_bloc_test.dart`
Expected: PASS.

- [ ] **Step 5: Edit `BrainStreamService` to dispatch into the bloc**

Replace the body of `clients/ino.flutter/lib/services/brain_stream_service.dart` with:

```dart
import 'dart:async';
import 'dart:developer' as developer;

import 'package:grpc/grpc.dart';

import '../grpc/generated/ino.pbgrpc.dart';
import '../state/brain_inspector_bloc.dart';

class BrainStreamService {
  BrainStreamService(this._stub, this._bloc);

  final InoClient _stub;
  final BrainInspectorBloc _bloc;
  ResponseStream<BrainPulseProto>? _subscription;
  StreamSubscription<BrainPulseProto>? _listener;

  void start({String? userIdFilter, String? sessionIdFilter}) {
    if (_subscription != null) return;
    final request = BrainWatchRequest()
      ..userIdFilter = userIdFilter ?? ''
      ..sessionIdFilter = sessionIdFilter ?? '';

    _subscription = _stub.watchBrainActivity(request);
    _listener = _subscription!.listen(
      _onPulse,
      onError: (Object err, StackTrace st) =>
          developer.log('brain.pulse.error', name: 'ino-flutter', error: err, stackTrace: st),
      onDone: () => developer.log('brain.pulse.done', name: 'ino-flutter'),
      cancelOnError: false,
    );
  }

  Future<void> stop() async {
    await _listener?.cancel();
    _listener = null;
    await _subscription?.cancel();
    _subscription = null;
  }

  void _onPulse(BrainPulseProto pulse) {
    _bloc.add(IngestFire(FireEvent.fromBrainPulse(
      fromGrain: pulse.fromGrain,
      toGrain: pulse.toGrain,
      methodName: pulse.methodName,
      payloadJson: pulse.payloadJson,
      traceParent: pulse.traceParent,
      timestampUnixMs: pulse.timestampUnixMs.toInt(),
    )));
  }
}
```

- [ ] **Step 6: Register `BrainInspectorBloc` in `main.dart`**

In `clients/ino.flutter/lib/main.dart`, add the import:

```dart
import 'package:ino_flutter/state/brain_inspector_bloc.dart';
```

In the `MultiBlocProvider.providers` list, add:

```dart
BlocProvider(create: (_) => BrainInspectorBloc(), lazy: false),
```

(`lazy: false` so the bloc exists when `BrainStreamService` starts.)

- [ ] **Step 7: Build to surface any callers of the old `BrainStreamService(stub)` constructor**

Run: `cd clients/ino.flutter && flutter analyze`
Expected: one error in `brain_screen.dart` calling the old single-arg constructor (will be fixed in task 8). No other callers.

- [ ] **Step 8: Commit**

```bash
git add clients/ino.flutter/lib/services/brain_stream_service.dart clients/ino.flutter/lib/state/brain_inspector_bloc.dart clients/ino.flutter/lib/main.dart clients/ino.flutter/test/state/brain_inspector_bloc_test.dart
git commit -m "feat(poc-flutter): BrainStreamService dispatches pulses into BrainInspectorBloc (slice C.4 task 5)"
```

---

## Task 6: Inspector drawer widget

**Files:**
- Create: `clients/ino.flutter/lib/screens/brain/brain_inspector_drawer.dart`
- Create test: `clients/ino.flutter/test/screens/brain/brain_inspector_drawer_test.dart`

- [ ] **Step 1: Write the failing widget tests**

Create `clients/ino.flutter/test/screens/brain/brain_inspector_drawer_test.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/screens/brain/brain_inspector_drawer.dart';
import 'package:ino_flutter/state/brain_inspector_bloc.dart';

Widget _wrap(BrainInspectorBloc bloc) => MaterialApp(
      home: Scaffold(
        body: BlocProvider.value(
          value: bloc,
          child: const BrainInspectorDrawer(),
        ),
      ),
    );

void main() {
  group('BrainInspectorDrawer', () {
    testWidgets('renders nothing when no node is selected', (tester) async {
      final bloc = BrainInspectorBloc();
      await tester.pumpWidget(_wrap(bloc));
      expect(find.byKey(const Key('brain-inspector-drawer-panel')), findsNothing);
    });

    testWidgets('neuron selection renders title + role + traffic list', (tester) async {
      final bloc = BrainInspectorBloc()
        ..add(IngestFire(FireEvent(
          id: '1',
          traceParent: 't',
          synapseType: 'ChatIntent',
          fromId: 'kernel.cortex',
          toId: 'travel.find_flights',
          payloadJson: '{"text":"hi"}',
          timestampUnixMs: DateTime.now().millisecondsSinceEpoch,
        )))
        ..add(SelectNeuron(nodeId: 'kernel.cortex'));

      await tester.pump();
      await tester.pumpWidget(_wrap(bloc));
      await tester.pump();

      expect(find.text('Cortex'), findsOneWidget);
      // Role from brain_roles.dart for kernel.cortex:
      expect(find.textContaining('Routes user prompts'), findsOneWidget);
      // Traffic row mentions the synapse type:
      expect(find.textContaining('ChatIntent'), findsOneWidget);
    });

    testWidgets('synapse type selection renders producers + consumers chips', (tester) async {
      final bloc = BrainInspectorBloc()
        ..add(SelectSynapseType(nodeId: 'syn.chat_intent'));
      await tester.pumpWidget(_wrap(bloc));
      await tester.pump();
      expect(find.text('ChatIntent'), findsOneWidget);
      expect(find.text('Consumers'), findsOneWidget);
    });

    testWidgets('pulse selection renders traceparent + payload', (tester) async {
      final pulse = FireEvent(
        id: 'p1',
        traceParent: '00-abc-def-01',
        synapseType: 'ChatIntent',
        fromId: 'kernel.cortex',
        toId: 'travel.plan',
        payloadJson: '{"text":"hi"}',
        timestampUnixMs: 0,
      );
      final bloc = BrainInspectorBloc()..add(PausePulse(pulse: pulse));
      await tester.pumpWidget(_wrap(bloc));
      await tester.pump();

      expect(find.textContaining('00-abc-def-01'), findsOneWidget);
      expect(find.textContaining('"text"'), findsOneWidget);
    });
  });
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd clients/ino.flutter && flutter test test/screens/brain/brain_inspector_drawer_test.dart`
Expected: FAIL — `BrainInspectorDrawer` not defined.

- [ ] **Step 3: Implement the drawer**

Create `clients/ino.flutter/lib/screens/brain/brain_inspector_drawer.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/screens/brain/brain_roles.dart';
import 'package:ino_flutter/screens/brain/brain_topology.dart';
import 'package:ino_flutter/state/brain_inspector_bloc.dart';

class BrainInspectorDrawer extends StatelessWidget {
  const BrainInspectorDrawer({super.key});

  @override
  Widget build(BuildContext context) {
    return BlocBuilder<BrainInspectorBloc, BrainInspectorState>(
      buildWhen: (a, b) =>
          a.selected != b.selected ||
          a.recentByNodeId != b.recentByNodeId ||
          a.pausedPulse != b.pausedPulse,
      builder: (context, state) {
        final sel = state.selected;
        if (sel == null) return const SizedBox.shrink();
        return Align(
          alignment: Alignment.topRight,
          child: Container(
            key: const Key('brain-inspector-drawer-panel'),
            width: 360,
            margin: const EdgeInsets.only(top: 60, right: 12, bottom: 80),
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(
              color: Colors.black.withAlpha(170),
              borderRadius: BorderRadius.circular(14),
              border: Border.all(color: Colors.white.withAlpha(30)),
            ),
            child: switch (sel) {
              NeuronSelection s => _NeuronView(nodeId: s.nodeId, recent: state.recentByNodeId),
              SynapseTypeSelection s => _SynapseTypeView(nodeId: s.nodeId, recent: state.recentByNodeId),
              PulseSelection s => _PulseView(pulse: s.pulse),
            },
          ),
        );
      },
    );
  }
}

class _DrawerHeader extends StatelessWidget {
  const _DrawerHeader({required this.title, required this.dotColor});
  final String title;
  final Color dotColor;
  @override
  Widget build(BuildContext context) {
    return Row(children: [
      Container(width: 12, height: 12, decoration: BoxDecoration(color: dotColor, shape: BoxShape.circle)),
      const SizedBox(width: 10),
      Expanded(child: Text(title, style: const TextStyle(color: Colors.white, fontSize: 16, fontWeight: FontWeight.w600))),
      IconButton(
        tooltip: 'Close',
        icon: const Icon(Icons.close, color: Colors.white70, size: 18),
        onPressed: () => context.read<BrainInspectorBloc>().add(Deselect()),
      ),
    ]);
  }
}

class _NeuronView extends StatelessWidget {
  const _NeuronView({required this.nodeId, required this.recent});
  final String nodeId;
  final Map<String, List<FireEvent>> recent;

  @override
  Widget build(BuildContext context) {
    final node = BrainTopology.load().nodes.firstWhere(
      (n) => n.id == nodeId,
      orElse: () => throw StateError('unknown node $nodeId'),
    );
    final role = roleByNodeId[nodeId] ?? 'no role declared';
    final traffic = (recent[nodeId] ?? const <FireEvent>[]).take(10).toList();

    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        _DrawerHeader(title: node.label, dotColor: Color(domainColor(node.domain)).withAlpha(255)),
        const SizedBox(height: 4),
        Text(node.domain, style: TextStyle(color: Colors.white.withAlpha(140), fontSize: 11, letterSpacing: 0.5)),
        const SizedBox(height: 12),
        Text(role,
            style: TextStyle(color: roleByNodeId.containsKey(nodeId) ? Colors.white70 : Colors.white24, fontSize: 13)),
        const SizedBox(height: 16),
        const Text('Recent traffic', style: TextStyle(color: Colors.white60, fontSize: 11, letterSpacing: 0.6)),
        const SizedBox(height: 6),
        if (traffic.isEmpty)
          const Text('no traffic yet — interact to populate', style: TextStyle(color: Colors.white24, fontSize: 12))
        else
          ...traffic.map((e) => _TrafficRow(event: e, anchorId: nodeId)),
      ],
    );
  }
}

class _TrafficRow extends StatelessWidget {
  const _TrafficRow({required this.event, required this.anchorId});
  final FireEvent event;
  final String anchorId;
  @override
  Widget build(BuildContext context) {
    final fired = event.fromId == anchorId;
    final counterpartId = fired ? event.toId : event.fromId;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 3),
      child: Row(children: [
        Icon(fired ? Icons.arrow_upward : Icons.arrow_downward, color: Colors.white54, size: 14),
        const SizedBox(width: 8),
        Expanded(child: Text('${event.synapseType} · $counterpartId',
            style: const TextStyle(color: Colors.white70, fontSize: 12), overflow: TextOverflow.ellipsis)),
      ]),
    );
  }
}

class _SynapseTypeView extends StatelessWidget {
  const _SynapseTypeView({required this.nodeId, required this.recent});
  final String nodeId;
  final Map<String, List<FireEvent>> recent;

  @override
  Widget build(BuildContext context) {
    final topo = BrainTopology.load();
    final node = topo.nodes.firstWhere((n) => n.id == nodeId);
    final consumers = topo.edges
        .where((e) => e.from == nodeId && e.kind == EdgeKind.handler)
        .map((e) => e.to)
        .toList();
    final fires = (recent[nodeId] ?? const <FireEvent>[]).take(10).toList();

    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        _DrawerHeader(title: node.label, dotColor: const Color(0xFF5EEAD4)),
        const SizedBox(height: 14),
        const Text('Consumers', style: TextStyle(color: Colors.white60, fontSize: 11, letterSpacing: 0.6)),
        const SizedBox(height: 6),
        Wrap(spacing: 6, runSpacing: 6, children: [
          for (final c in consumers) _Chip(text: c),
        ]),
        const SizedBox(height: 16),
        const Text('Recent fires', style: TextStyle(color: Colors.white60, fontSize: 11, letterSpacing: 0.6)),
        const SizedBox(height: 6),
        if (fires.isEmpty)
          const Text('no traffic yet', style: TextStyle(color: Colors.white24, fontSize: 12))
        else
          ...fires.map((e) => Padding(
                padding: const EdgeInsets.symmetric(vertical: 3),
                child: Text('${e.fromId} → ${e.toId}',
                    style: const TextStyle(color: Colors.white70, fontSize: 12)),
              )),
      ],
    );
  }
}

class _PulseView extends StatelessWidget {
  const _PulseView({required this.pulse});
  final FireEvent pulse;
  @override
  Widget build(BuildContext context) {
    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        _DrawerHeader(title: 'Pulse · ${pulse.synapseType}', dotColor: Colors.white),
        const SizedBox(height: 12),
        Text('${pulse.fromId} → ${pulse.toId}', style: const TextStyle(color: Colors.white70, fontSize: 13)),
        const SizedBox(height: 8),
        SelectableText(pulse.traceParent,
            style: const TextStyle(color: Colors.white54, fontFamily: 'monospace', fontSize: 11)),
        const SizedBox(height: 12),
        const Text('Payload', style: TextStyle(color: Colors.white60, fontSize: 11, letterSpacing: 0.6)),
        const SizedBox(height: 6),
        Container(
          padding: const EdgeInsets.all(8),
          decoration: BoxDecoration(
            color: Colors.white.withAlpha(8),
            border: Border.all(color: Colors.white.withAlpha(20)),
            borderRadius: BorderRadius.circular(6),
          ),
          child: SelectableText(
            pulse.payloadJson.isEmpty ? '{}' : pulse.payloadJson,
            style: const TextStyle(color: Colors.white70, fontFamily: 'monospace', fontSize: 11),
          ),
        ),
      ],
    );
  }
}

class _Chip extends StatelessWidget {
  const _Chip({required this.text});
  final String text;
  @override
  Widget build(BuildContext context) => Container(
        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
        decoration: BoxDecoration(
          color: Colors.white.withAlpha(15),
          borderRadius: BorderRadius.circular(6),
          border: Border.all(color: Colors.white.withAlpha(25)),
        ),
        child: Text(text, style: const TextStyle(color: Colors.white70, fontSize: 11)),
      );
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd clients/ino.flutter && flutter test test/screens/brain/brain_inspector_drawer_test.dart`
Expected: PASS — all four cases.

- [ ] **Step 5: Commit**

```bash
git add clients/ino.flutter/lib/screens/brain/brain_inspector_drawer.dart clients/ino.flutter/test/screens/brain/brain_inspector_drawer_test.dart
git commit -m "feat(poc-flutter): inspector drawer widget for neurons / synapse types / pulses (slice C.4 task 6)"
```

---

## Task 7: Three.js raycaster picking helper

**Files:**
- Create: `clients/ino.flutter/lib/screens/brain/brain_picking.dart`

This task isn't unit-testable on CanvasKit (the GL context doesn't initialise headlessly), so it ships as a typed helper used by `BrainHomeScreen` in task 8 with manual verification. Keep the helper small and dependency-injectable so the screen test in task 8 can fake it.

- [ ] **Step 1: Create the helper**

```dart
// clients/ino.flutter/lib/screens/brain/brain_picking.dart
import 'package:flutter/widgets.dart';
import 'package:three_js/three_js.dart' as three;

class BrainPicker {
  BrainPicker(this._three) : _raycaster = three.Raycaster();

  final three.ThreeJS _three;
  final three.Raycaster _raycaster;

  /// Returns the `userData['nodeId']` (for static meshes) or
  /// `userData['fireEventId']` (for animated pulse meshes) of the first hit,
  /// or null if nothing was hit.
  PickResult? pick(Offset localPosition, List<three.Object3D> targets) {
    final size = _three.size;
    if (size == null) return null;
    final ndc = three.Vector2(
      (localPosition.dx / size.width) * 2 - 1,
      -((localPosition.dy / size.height) * 2 - 1),
    );
    _raycaster.setFromCamera(ndc, _three.camera);
    final hits = _raycaster.intersectObjects(targets, false);
    if (hits.isEmpty) return null;
    final mesh = hits.first.object;
    final nodeId = mesh?.userData['nodeId'] as String?;
    if (nodeId != null) return PickResult.node(nodeId);
    final fireEventId = mesh?.userData['fireEventId'] as String?;
    if (fireEventId != null) return PickResult.pulse(fireEventId);
    return null;
  }
}

sealed class PickResult {
  const PickResult();
  factory PickResult.node(String nodeId) = NodePick;
  factory PickResult.pulse(String fireEventId) = PulsePick;
}

class NodePick extends PickResult {
  const NodePick(this.nodeId);
  final String nodeId;
}

class PulsePick extends PickResult {
  const PulsePick(this.fireEventId);
  final String fireEventId;
}
```

- [ ] **Step 2: Build sanity check**

Run: `cd clients/ino.flutter && flutter analyze lib/screens/brain/brain_picking.dart`
Expected: no analyzer issues. (No tests here — three.js GL state isn't headless.)

- [ ] **Step 3: Commit**

```bash
git add clients/ino.flutter/lib/screens/brain/brain_picking.dart
git commit -m "feat(poc-flutter): BrainPicker helper for raycaster-based node + pulse picking (slice C.4 task 7)"
```

---

## Task 7b: Animated fire pulses — moving cyan dots between fromId and toId

**Files:**
- Create: `clients/ino.flutter/lib/screens/brain/brain_pulse_animator.dart`

The spec assumes a "moving pulse" you can click and freeze, but `BrainStreamService` on master only logs — there is no pulse mesh today. This task adds the animator. Lives as a standalone class so `BrainHomeScreen` (next task) just owns one instance.

- [ ] **Step 1: Create the animator**

```dart
// clients/ino.flutter/lib/screens/brain/brain_pulse_animator.dart
import 'package:three_js/three_js.dart' as three;
import 'package:ino_flutter/screens/brain/brain_topology.dart';
import 'package:ino_flutter/state/brain_inspector_bloc.dart';

class _ActivePulse {
  _ActivePulse(this.fire, this.mesh, this.from, this.to, this.start);
  final FireEvent fire;
  final three.Mesh mesh;
  final three.Vector3 from;
  final three.Vector3 to;
  final double start; // seconds since scene t0
  double t = 0;       // 0..1 progress
}

class BrainPulseAnimator {
  BrainPulseAnimator(this._scene, this._topology) {
    _positions = {for (final n in _topology.nodes) n.id: three.Vector3(n.x, n.y, n.z)};
  }

  static const double _travelSeconds = 1.4;
  final three.Scene _scene;
  final BrainTopology _topology;
  late final Map<String, three.Vector3> _positions;
  final List<_ActivePulse> _active = [];
  double _now = 0;
  String? _pausedFireEventId;

  /// Inspect-side hook: pause animation for a single pulse without stopping the others.
  void setPaused(String? fireEventId) => _pausedFireEventId = fireEventId;

  /// Maps a fireEventId back to its FireEvent so the screen can call PausePulse.
  FireEvent? lookupFire(String fireEventId) =>
      _active.where((p) => p.fire.id == fireEventId).map((p) => p.fire).firstOrNull;

  /// Called from the screen's animation tick. dt is seconds.
  void tick(double dt) {
    _now += dt;
    final paused = _pausedFireEventId;
    for (var i = _active.length - 1; i >= 0; i--) {
      final p = _active[i];
      if (paused != null && p.fire.id == paused) continue; // freeze
      p.t = ((_now - p.start) / _travelSeconds).clamp(0.0, 1.0);
      final eased = p.t * p.t * (3 - 2 * p.t); // smoothstep
      p.mesh.position
        ..x = p.from.x + (p.to.x - p.from.x) * eased
        ..y = p.from.y + (p.to.y - p.from.y) * eased
        ..z = p.from.z + (p.to.z - p.from.z) * eased;
      if (p.t >= 1.0) {
        _scene.remove(p.mesh);
        _active.removeAt(i);
      }
    }
  }

  /// Returns every currently-living pulse mesh so picking can include them.
  Iterable<three.Object3D> get meshes => _active.map((p) => p.mesh);

  /// Called when a new FireEvent is ingested.
  void spawn(FireEvent fire) {
    final from = _positions[fire.fromId];
    final to = _positions[fire.toId];
    if (from == null || to == null) return; // unknown node — drop silently
    final mesh = three.Mesh(
      three.SphereGeometry(0.07, 12, 8),
      three.MeshBasicMaterial.fromMap({'color': 0x5EEAD4, 'transparent': true, 'opacity': 0.95}),
    );
    mesh.userData['fireEventId'] = fire.id;
    mesh.position.setValues(from.x, from.y, from.z);
    _scene.add(mesh);
    _active.add(_ActivePulse(fire, mesh, from, to, _now));
  }

  void dispose() {
    for (final p in _active) {
      _scene.remove(p.mesh);
    }
    _active.clear();
  }
}
```

- [ ] **Step 2: Build sanity check**

Run: `cd clients/ino.flutter && flutter analyze lib/screens/brain/brain_pulse_animator.dart`
Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add clients/ino.flutter/lib/screens/brain/brain_pulse_animator.dart
git commit -m "feat(poc-flutter): BrainPulseAnimator spawns cyan dots between neurons on every fire (slice C.4 task 7b)"
```

---

## Task 8: BrainHomeScreen — replaces BrainScreen, hosts inspector + composer

**Files:**
- Delete: `clients/ino.flutter/lib/screens/brain/brain_screen.dart`
- Create: `clients/ino.flutter/lib/screens/brain/brain_home_screen.dart`

The new screen is `BrainScreen` minus the back-arrow + Run-Travel-demo button, plus the inspector drawer overlay, plus a stub mic icon, plus picking + bloc dispatch.

- [ ] **Step 1: Create `brain_home_screen.dart`**

Start by copying the current `brain_screen.dart` body, then apply these changes:

1. Class rename: `BrainScreen` → `BrainHomeScreen`, `_BrainScreenState` → `_BrainHomeScreenState`.
2. Remove the import of `package:go_router/go_router.dart` and the back-arrow `Positioned` block in `build()`.
3. Remove the `_runTravelDemo` method, the `_travelDemoPrompt` constant, and the `onRunTravelDemo` parameter wiring; in the composer row, replace the `TextButton.icon(... 'Run Travel demo' ...)` with:
   ```dart
   IconButton(
     onPressed: null, // wired in slice 2
     tooltip: 'voice coming in slice 2',
     icon: const Icon(Icons.mic_none, color: Colors.white38),
   ),
   ```
4. Remove `_domainFromText` and the entire `BlocListener<InoBloc, InoBlocState>` block that calls it — pulses now drive the brain via `BrainInspectorBloc.recentByNodeId` and `state.pausedPulse`.
5. Update the `BrainStreamService` constructor call to the new two-arg form:
   ```dart
   _brainStream = BrainStreamService(stub, context.read<BrainInspectorBloc>());
   ```
6. In `_meshForNode`, after constructing each mesh, set `mesh.userData['nodeId'] = node.id;` so picking works.
7. Construct a `BrainPulseAnimator` (from task 7b) once in `_setupScene`, store on the state. In the existing `_animate` callback, call `_pulseAnimator.tick(dt)` (the animator already sets `userData['fireEventId']` on every pulse mesh it spawns). Listen for new fires via `BrainInspectorBloc.stream` (only the buffer-changed deltas) and call `_pulseAnimator.spawn(latestFire)` — alternatively, subscribe to the same `BrainStreamService` callback chain. Pick whichever is cleaner to wire.
8. Add a `BrainPicker` field initialized in `_setupScene` after `_threeJs.scene` is built.
9. Wrap the `Positioned.fill(child: _threeJs.build())` in a `Listener`:
   ```dart
   Positioned.fill(
     child: Listener(
       onPointerDown: _handleTap,
       child: _threeJs.build(),
     ),
   ),
   ```
   And implement:
   ```dart
   void _handleTap(PointerDownEvent e) {
     final box = context.findRenderObject() as RenderBox?;
     if (box == null) return;
     final local = box.globalToLocal(e.position);
     final all = <three.Object3D>[];
     for (final list in _neuronsByDomain.values) all.addAll(list);
     all.addAll(_synapseMeshes);
     all.addAll(_pulseAnimator.meshes); // currently-living pulse spheres
     final result = _picker.pick(local, all);
     final inspector = context.read<BrainInspectorBloc>();
     switch (result) {
       case null: inspector.add(Deselect()); _controls?.autoRotate = true; break;
       case NodePick p:
         final node = BrainTopology.load().nodes.firstWhere((n) => n.id == p.nodeId);
         if (node.kind == NodeKind.neuron) {
           inspector.add(SelectNeuron(nodeId: p.nodeId));
         } else {
           inspector.add(SelectSynapseType(nodeId: p.nodeId));
         }
         _controls?.autoRotate = false;
         break;
       case PulsePick p:
         final pulse = _pulseAnimator.lookupFire(p.fireEventId);
         if (pulse != null) {
           _pulseAnimator.setPaused(p.fireEventId);
           inspector.add(PausePulse(pulse: pulse));
           _controls?.autoRotate = false;
         }
         break;
     }
   }
   ```
   Track `_synapseMeshes` separately when you add synapse-type nodes (the existing `_addNodes` puts them all in `_neuronsByDomain` only by accident — fix during this task by storing synapse-kind nodes in `_synapseMeshes` instead). The animator already owns the fire-event-id → mesh mapping, so no separate dictionary is needed on the screen.

When `Deselect` fires (via `BrainInspectorBloc.stream`), call `_pulseAnimator.setPaused(null)` so the frozen pulse resumes.

10. Add the inspector drawer to the `Stack`:

```dart
const Positioned.fill(child: BrainInspectorDrawer()),
```

11. Listen for `Deselect` to resume `autoRotate`:

```dart
@override
void initState() {
  super.initState();
  // ... existing init ...
  _selectedSub = context.read<BrainInspectorBloc>().stream.listen((s) {
    if (!mounted) return;
    _controls?.autoRotate = s.selected == null;
  });
}

@override
void dispose() {
  _selectedSub?.cancel();
  // ... existing dispose ...
}
```

12. Bind `Esc` to `Deselect` via a top-level `Focus` + `KeyboardListener` around the `Stack`:

```dart
Focus(
  autofocus: true,
  onKeyEvent: (node, event) {
    if (event is KeyDownEvent && event.logicalKey == LogicalKeyboardKey.escape) {
      context.read<BrainInspectorBloc>().add(Deselect());
      return KeyEventResult.handled;
    }
    return KeyEventResult.ignored;
  },
  child: Stack(...),
)
```

- [ ] **Step 2: Delete `brain_screen.dart`**

```
git rm clients/ino.flutter/lib/screens/brain/brain_screen.dart
```

- [ ] **Step 3: Build to surface analyzer issues**

Run: `cd clients/ino.flutter && flutter analyze`
Expected: only references to the old `BrainScreen` symbol from `app.dart` (fixed in task 9). Fix any other analyzer errors locally before continuing.

- [ ] **Step 4: Commit**

```bash
git add clients/ino.flutter/lib/screens/brain/
git commit -m "feat(poc-flutter): BrainHomeScreen with picking, drawer, stub mic, no demo button (slice C.4 task 8)"
```

---

## Task 9: Routing — delete `/home`, point everything at `/brain`

**Files:**
- Modify: `clients/ino.flutter/lib/app.dart`
- Delete: `clients/ino.flutter/lib/screens/home/home_screen.dart`
- Delete (if it exists): `clients/ino.flutter/test/screens/home/home_screen_test.dart`

- [ ] **Step 1: Edit `app.dart`**

Replace the file with:

```dart
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:ino_flutter/screens/brain/brain_home_screen.dart';
import 'package:ino_flutter/screens/onboarding/onboarding_screen.dart';
import 'package:ino_flutter/screens/rfw_v2_demo/rfw_v2_demo_screen.dart';
import 'package:ino_flutter/screens/rfw_v3_demo/rfw_v3_demo_screen.dart';

final _router = GoRouter(
  initialLocation: '/brain',
  redirect: (context, state) {
    // Bare / always lands on /brain. The `?q=` deep link is preserved on the
    // brain route; BrainHomeScreen.initState consumes it on first frame.
    if (state.uri.path == '/') {
      final q = state.uri.queryParameters['q'];
      if (q != null && q.isNotEmpty) {
        return '/brain?q=${Uri.encodeComponent(q)}';
      }
      return '/brain';
    }
    return null;
  },
  routes: [
    GoRoute(path: '/brain', builder: (context, state) => const BrainHomeScreen()),
    GoRoute(path: '/onboarding', builder: (context, state) => const OnboardingScreen()),
    GoRoute(path: '/rfw-v2', builder: (context, state) => const RfwV2DemoScreen()),
    GoRoute(path: '/rfw-v3', builder: (context, state) => const RfwV3DemoScreen()),
  ],
);

class InoApp extends StatelessWidget {
  const InoApp({super.key});

  @override
  Widget build(BuildContext context) {
    return MaterialApp.router(
      title: 'ino',
      debugShowCheckedModeBanner: false,
      theme: ThemeData(
        colorScheme: ColorScheme.fromSeed(
          seedColor: const Color(0xFF6C63FF),
          brightness: Brightness.dark,
        ),
        useMaterial3: true,
      ),
      routerConfig: _router,
    );
  }
}
```

- [ ] **Step 2: In `BrainHomeScreen.initState`, consume the `?q=` deep link**

Add (after the existing post-frame callback that starts `BrainStreamService`):

```dart
WidgetsBinding.instance.addPostFrameCallback((_) {
  if (!mounted) return;
  final q = GoRouterState.of(context).uri.queryParameters['q'];
  if (q != null && q.isNotEmpty) {
    context.read<InoBloc>().add(SendMessage(q));
  }
});
```

(Use `addPostFrameCallback` so the bloc is attached before dispatch.)

- [ ] **Step 3: Delete `home_screen.dart` and any home tests**

```
git rm clients/ino.flutter/lib/screens/home/home_screen.dart
git rm -r clients/ino.flutter/lib/screens/home  # if directory empty
git rm -r clients/ino.flutter/test/screens/home  # if exists
```

- [ ] **Step 4: Run analyzer + all flutter tests**

```
cd clients/ino.flutter
flutter analyze
flutter test
```

Expected: no analyzer issues, all tests green. If any test references `HomeScreen`, update or delete it (it should already be obsolete since slice C.3 made `/brain` primary).

- [ ] **Step 5: Commit**

```bash
git add clients/ino.flutter/lib/app.dart clients/ino.flutter/lib/screens/brain/brain_home_screen.dart
git commit -m "refactor(poc-flutter): /brain is the only home; delete /home + HomeScreen (slice C.4 task 9)"
```

---

## Task 10: Legend rename — "capability" / "signal", drop yellow row

**Files:**
- Modify: `clients/ino.flutter/lib/screens/brain/brain_home_screen.dart` (the `_BrainLegend` widget)

- [ ] **Step 1: Edit `_BrainLegend`**

Replace its `Column.children` with the two-row form:

```dart
children: const [
  _LegendDot(color: Color(0xFFF5B4A0), label: 'capability (domain-tinted)'),
  SizedBox(height: 4),
  _LegendDot(color: Color(0xFF5EEAD4), label: 'signal'),
],
```

(`0xFFF5B4A0` is the travel anchor; the row's purpose is to indicate a domain-tinted dot. Keep this single representative colour to avoid implying every domain has its own legend row.)

- [ ] **Step 2: Build + run analyzer**

```
cd clients/ino.flutter && flutter analyze
```

Expected: clean.

- [ ] **Step 3: Commit**

```bash
git add clients/ino.flutter/lib/screens/brain/brain_home_screen.dart
git commit -m "refactor(poc-flutter): legend uses 'capability'/'signal' instead of 'neuron'/'synapse type' (slice C.4 task 10)"
```

---

## Task 11: Full build, full test, manual verification, screenshot

- [ ] **Step 1: Full build**

```
dotnet build ino.slnx
```

Expected: succeeds with zero new warnings.

- [ ] **Step 2: Full test**

```
dotnet test ino.slnx
cd clients/ino.flutter && flutter test
```

Expected: all green. Pay particular attention to `BrainStreamE2ETests` and `FireTestSynapseE2ETests`.

- [ ] **Step 3: Aspire run + manual verification**

```
aspire start --isolated
```

In Chrome (via Chrome DevTools MCP):

1. Visit `https://localhost:<kernel-https-port>/`. Confirm it redirects to `/brain`.
2. Confirm no top-left back-arrow is rendered.
3. Click on a neuron node. Confirm:
   - Inspector drawer slides in from the right with the neuron's name + role + (initially empty) traffic list.
   - Auto-rotate stops.
4. Type "plan a trip to Bali" into the composer and press Enter. Confirm pulses animate; reopen the same neuron's drawer; "recent traffic" is now non-empty.
5. Click a moving cyan pulse. Confirm:
   - That single pulse freezes mid-edge and brightens.
   - Drawer shows "Pulse · `<type>`" with `traceparent` and the JSON payload.
   - Other pulses continue flowing.
6. Press `Esc`. Confirm the drawer closes, the pulse resumes, auto-rotate resumes.
7. Open a neuron drawer; if/when the "Fire test synapse" affordance is wired (see open work below), exercise it and confirm a fresh pulse appears on the brain matching the chosen type + payload. **For slice 1, the drawer renders the action stub but actual UI for Fire-test-synapse is OPTIONAL — if it isn't wired, mark this step as deferred and file a follow-up issue.**
8. Aspire dashboard → Traces tab → confirm `FireTestSynapse` rpc shows up with a child fire-chain when triggered, linked by `traceparent`.

- [ ] **Step 4: Capture a hero screenshot**

Use Chrome DevTools MCP `take_screenshot` (full page) and save under `reviews/slice-c4-brain-home-inspector.png`.

- [ ] **Step 5: Commit screenshot**

```bash
git add reviews/slice-c4-brain-home-inspector.png
git commit -m "docs: hero screenshot for slice C.4 brain home + inspector"
```

- [ ] **Step 6: Final summary**

Write a 5-line summary in the chat: what shipped, what's deferred (Fire-test-synapse wiring inside the drawer if it ended up out of scope, voice path entirely), and the SHA range of the slice's commits.

---

## Open work that's intentionally NOT part of this slice

- Real `Recorder` neuron + voice pipeline (slice 2).
- C# class renames to drop `Neuron`/`Plan` suffixes from existing concretes (separate refactor commit).
- Server-side persistence of fire history (out of scope; client-side ring buffer is enough for the demo).
- Dynamic neuron creation + regrouping (post-v0.1 epic).
- Fire-test-synapse drawer affordance — backend RPC ships in task 4, but if the drawer's "Fire" button + sub-sheet isn't wired in task 6, defer that to a small follow-up; the RPC is independently useful for E2E and tooling.
