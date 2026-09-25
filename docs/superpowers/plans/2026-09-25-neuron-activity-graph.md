# Neuron Activity Graph Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show live neuron calls and published signals in a selectable depth-projected graph with a bounded activity list and replay.

**Architecture:** Kernel call filters and `Neuron.PublishAsync` append metadata-only events to an in-memory activity feed. The product host exposes scoped snapshot and SSE reads. Flutter maintains a cursor, projects observed neurons and routes into the existing `UiGraph`, and synchronizes activity selection with a detail pane.

**Tech Stack:** .NET/Orleans, ASP.NET Core minimal endpoints and SSE, Flutter/Dart, xUnit and Flutter widget tests.

**Spec:** `docs/superpowers/specs/2026-09-25-neuron-activity-graph-design.md`

## Global Constraints

- Observe only neuron calls and neuron-published signals; exclude observer subscription and infrastructure calls.
- Keep at most 2,000 events or 15 minutes per scope, whichever evicts first, with a global memory cap.
- The global event cap is 20,000; evict the oldest event globally when it is reached.
- No payload or result values by default. Show identity, type, timing, and outcome.
- History is in memory and resets on restart. Replay never invokes a neuron.
- An unobserved signal delivery must not be drawn as a delivered edge.
- The first deployment targets the current cohosted runtime; do not claim multi-silo fan-in.

## Review Focus

- A call with no workspace scope must not appear in another workspace's feed: Task 2 isolation test.
- Incoming and outgoing halves of one call must display as one operation: Task 2 pairing test and Task 5 widget test.
- An exception must still reach the caller unchanged: Task 2 failure test.
- A stale SSE cursor or slow reader must show a gap and resynchronize: Task 3 transport tests and Task 4 client test.
- Rapid activity while paused must not change the selected replay frame: Task 5 widget test.

## File map

| File | Responsibility |
| --- | --- |
| `src/Modules/DigitalBrain/Kernel/Kernel/Activity/ActivityEvent.cs` | Internal metadata-only event and event-kind model. |
| `src/Modules/DigitalBrain/Kernel/Kernel/Activity/ActivityFeed.cs` | Per-scope bounded retention, sequence, snapshot, subscriber channels and gap detection. |
| `src/Modules/DigitalBrain/Kernel/Kernel/Activity/NeuronCallObservation.cs` | Orleans incoming/outgoing filters and operation matching. |
| `src/Modules/DigitalBrain/Kernel/Kernel/Hosting/BrainHosting.cs` | Register observation module and filters. |
| `src/Modules/DigitalBrain/Kernel/Kernel/Neuron.cs` | Append signal publication at the common path. |
| `src/Applications/IntoChat/IntoChat/Activity/ActivityEndpoints.cs` | Scoped snapshot and SSE endpoint mapping. |
| `src/Applications/IntoChat/IntoChat/Program.cs` | Map new endpoints after the auth gate. |
| `src/Modules/Google/Flutter/app/core/lib/src/models/activity_models.dart` | Parse the new wire format; do not extend the dead Brain contract. |
| `src/Modules/Google/Flutter/app/core/lib/src/ui_client.dart` | Read snapshot and watch SSE with cursor and reconnect. |
| `src/Modules/Google/Flutter/app/shell/lib/workspace/activity/activity_controller.dart` | Live/pause/replay state, selection, filtering and graph projection. |
| `src/Modules/Google/Flutter/app/shell/lib/workspace/activity/activity_view.dart` | Activity list, graph and inspector layout. |
| `src/Modules/Google/Flutter/app/shell/lib/workspace/workspace_store.dart` | Register Activity as a local app. |
| `src/Modules/Google/Flutter/app/shell/lib/workspace/workspace_app.dart` | Mount Activity view with the selected workspace. |

### Task 1: Establish the scope and event contract

**Files:** Create `Kernel/Activity/ActivityEvent.cs` and `Kernel/Activity/ActivityFeed.cs` under `src/Modules/DigitalBrain/Kernel`; test in `src/Modules/DigitalBrain/Kernel/Tests/Unit/ActivityFeedFacts.cs`.

**Interfaces:** Produce `ActivityEvent(string ScopeId, long Sequence, Guid Id, Guid OperationId, string? CorrelationId, DateTimeOffset At, ActivityKind Kind, string? SourceId, string? TargetId, string Type, string Status, double? DurationMs, string? FailureCode)`. Produce `ActivityFeed.Append(ActivityEvent)`, `Snapshot(string scopeId, long? afterSequence = null)` and `Watch(string scopeId, long afterSequence, CancellationToken)`; snapshot includes events, `NextSequence`, `Gap`, `ObservedAt`. Scope ID is the opaque `WorkspaceScope.Id` already carried in `IntentContext`, never a display label. Inspect `IntentContext.cs` and workspace HTTP flows before coding; only calls with a verified scope enter a scoped feed.

- [ ] **Step 1: Write a failing bounded-feed test.** Append 2,001 events in one scope, one in another, then assert the first scope retains 2,000 ordered events, reports a gap for cursor 0, and never includes the second scope. Add a clock-controlled test where an event older than 15 minutes is evicted and an event on the boundary remains.
- [ ] **Step 2: Run `dotnet test src/Modules/DigitalBrain/Kernel/Tests/Unit/DigitalBrain.Runtime.Tests.Unit.csproj --filter FullyQualifiedName~ActivityFeedFacts`; verify the new tests fail.**
- [ ] **Step 3: Implement the feed.** Use a lock per scope around sequence assignment, pruning and subscriber fan-out; a global cap and bounded subscriber channels must drop/close without awaiting callers. Reject empty scope IDs. Use an injected `TimeProvider` for time-limit tests.

  ```csharp
  public ActivitySnapshot Snapshot(string scopeId, long? afterSequence = null);
  public IAsyncEnumerable<ActivityUpdate> Watch(string scopeId, long afterSequence, CancellationToken cancellationToken);
  public void Append(ActivityEvent item); // assigns the next sequence, then prunes
  ```
- [ ] **Step 4: Run the filtered tests; verify ordered retention, time eviction, scope isolation and gap reporting pass. Commit only this task's files.**

### Task 2: Observe calls and publications without changing behavior

**Files:** Create `Kernel/Activity/NeuronCallObservation.cs`; modify `Kernel/Hosting/BrainHosting.cs` and `Kernel/Neuron.cs`; test in `Kernel/Tests/Unit/NeuronActivityFacts.cs` and an existing hosted grain test fixture.

**Interfaces:** Consume `ActivityFeed.Append`. Produce start and terminal `ActivityEvent` records sharing one `OperationId`. Outgoing records provide source and target; incoming records confirm arrival; `Neuron.PublishAsync` produces `SignalPublished` at the source. Propagate operation/correlation metadata through Orleans request context, preserving any existing values. Use the documented Orleans `IIncomingGrainCallFilter` and `IOutgoingGrainCallFilter` registration and `context.Invoke()` semantics.

- [ ] **Step 1: Write a failing hosted test.** A scoped test neuron calls a second neuron and publishes a test signal. Assert one operation contains outgoing start, incoming arrival and completion plus a separate signal publication. Assert an unscoped call is absent, `Watch`/`Unwatch` are absent, and no argument or return value appears in serialized events.
- [ ] **Step 2: Add a failing exception test.** The target throws a distinct exception; assert the original exception reaches the caller and the matching operation has a failed terminal event.
- [ ] **Step 3: Run `dotnet test src/Modules/DigitalBrain/Kernel/Tests/Unit/DigitalBrain.Runtime.Tests.Unit.csproj --filter FullyQualifiedName~NeuronActivityFacts`; verify failures.**
- [ ] **Step 4: Implement both filters and publication capture.** Check neuron interface identity before recording, preserve the current `IntentContext` scope, stamp one operation ID on the outgoing leg, and reuse it on arrival. Record terminal status in `finally` or `catch` after invocation, never modify `context.Result`, and rethrow with the original stack. Resolve the feed once per grain activation or filter instance. Do not await SSE clients from grain code.

  ```csharp
  try { await context.Invoke(); RecordTerminal(operationId, "completed", null); }
  catch (Exception error) { RecordTerminal(operationId, "failed", error.GetType().Name); throw; }
  ```
- [ ] **Step 5: Run the filtered tests and existing kernel unit suite. Commit the observation files and registration.**

### Task 3: Serve scoped snapshots and live events

**Files:** Create `src/Applications/IntoChat/IntoChat/Activity/ActivityEndpoints.cs`; modify `Program.cs`; test in `src/Applications/IntoChat/Tests/Unit/ActivityEndpointFacts.cs` and the app's existing HTTP integration fixture.

**Interfaces:** `GET /workspaces/{workspaceId}/activity` returns `{events, nextSequence, gap, observedAt}`. `GET /workspaces/{workspaceId}/activity/events?after={long}` emits SSE `snapshot`, `activity`, and `gap` messages. Derive `WorkspaceScope.Create(auth username or DefaultLogin, workspaceId)` as `AgentEndpoints` does. Both routes run behind `UseBasicAuthGate`.

- [ ] **Step 1: Write failing endpoint tests.** Invalid workspace IDs get 400; wrong credentials get 401; two workspaces see only their own events; an evicted `after` cursor receives `gap` and a new snapshot; cancellation ends SSE; a full subscriber channel cannot block the append path.
- [ ] **Step 2: Run `dotnet test src/Applications/IntoChat/Tests/Unit --filter FullyQualifiedName~ActivityEndpointFacts`; verify failures.**
- [ ] **Step 3: Implement endpoints using the feed's snapshot/watch interface.** Serialize only contract fields, send SSE IDs from feed sequence, flush each event, pass `RequestAborted`, and avoid a heartbeat that fabricates activity. Return `Cache-Control: no-cache` on streams.

  ```text
  GET /workspaces/{workspaceId}/activity
  GET /workspaces/{workspaceId}/activity/events?after=42
  event: activity\nid: 43\ndata: {"sequence":43,"kind":"CallStarted",...}
  ```
- [ ] **Step 4: Run the filtered tests and an authenticated HTTP integration test. Commit endpoint and wiring files.**

### Task 4: Build a cursor-aware Flutter client

**Files:** Create `app/core/lib/src/models/activity_models.dart`; modify `app/core/lib/src/ui_client.dart` and `app/core/lib/digitalbrain_flutter.dart`; test in `app/core/test/activity_client_test.dart` (paths relative to `src/Modules/Google/Flutter`).

**Interfaces:** Produce `ActivitySnapshot readActivity(String workspaceId)` and `Stream<ActivityUpdate> watchActivity(String workspaceId, {required int afterSequence})`. `ActivityUpdate` is a sealed representation of snapshot, event and gap, with sequence retained. Reuse the existing authenticated `_http` path and cancellation pattern; do not use the legacy `watchBrain` route.

- [ ] **Step 1: Write failing parser/client tests.** Feed snapshot, event and gap frames from a fake HTTP client; assert ordered cursors, proper workspace URL encoding, reconnect after a dropped stream, and explicit gap instead of silently joining discontinuous sequences.
- [ ] **Step 2: Run `flutter test test/activity_client_test.dart` from `src/Modules/Google/Flutter/app/core`; verify failures.**
- [ ] **Step 3: Implement metadata-only models and SSE parsing.** Preserve unknown future event kinds as displayable text. Dispose the stream on widget/controller cancellation and avoid overlapping reconnect attempts.

  ```dart
  Future<ActivitySnapshot> readActivity(String workspaceId);
  Stream<ActivityUpdate> watchActivity(String workspaceId, {required int afterSequence});
  ```
- [ ] **Step 4: Run the client test and `flutter analyze` for the core package. Commit client files.**

### Task 5: Make activity selection and replay coherent

**Files:** Create `app/shell/lib/workspace/activity/activity_controller.dart` and `activity_view.dart`; use `app/ui/lib/src/components/graph/ui_graph.dart`, `graph_models.dart` and `graph_painter.dart` for graph presentation; test in `app/shell/test/workspace/activity_view_test.dart`.

**Interfaces:** `ActivityController` consumes `readActivity` and `watchActivity`, exposes retained events, visible cursor, selected operation/correlation, node/edge projection, live/paused mode, and `stale`/`gap` state. `ActivityView` consumes the controller and workspace ID. Stable graph IDs derive from observed neuron IDs; route IDs derive from source, target and relation kind. Do not invent a delivery edge for a publication.

- [ ] **Step 1: Write failing widget/controller tests.** Select an event and assert the list row, graph edge and detail steps refer to the same operation; pause while new events arrive and assert replay frame stays fixed; select a missing-target event and see `Unknown target`; show a gap after eviction; enable reduced motion and verify static highlight plus readable step list.
- [ ] **Step 2: Run `flutter test test/workspace/activity_view_test.dart` from `src/Modules/Google/Flutter/app/shell`; verify failures.**
- [ ] **Step 3: Implement controller and three-area view.** On wide screens show list/graph/details; below 700 logical pixels stack graph and a list/details switch. Add search, type/status filters, Live/Pause, time scrubber, clear selection, keyboard-accessible rows, and an explicit disconnected state. Keep node placement keyed by ID across feed updates. Use `UiGraph` pulse/selection callbacks; extend it only for a verified missing presentation capability.

  ```dart
  void selectActivity(String activityId);
  void pause();
  void seek(int sequence);
  void resumeLive();
  ```
- [ ] **Step 4: Run widget tests and `flutter analyze` for shell and UI packages. Commit view/controller changes.**

### Task 6: Integrate the activity app and prove the vertical slice

**Files:** Modify `app/shell/lib/workspace/workspace_store.dart` and `workspace_app.dart`; adjust dead Brain client callers if any remain; add `src/Applications/IntoChat/Tests/E2E/NeuronActivityGraphFacts.cs` or the existing equivalent product E2E fixture.

**Interfaces:** Add `activity` to `launchLocalApp`, title it `Activity`, and mount `ActivityView` for the selected project using the current authenticated client. Visibility controls stream lifetime; changing projects closes the old scoped stream before opening the new one.

- [ ] **Step 1: Write a failing product test.** Start the app, open Activity, trigger a scoped neuron-to-neuron call and signal, then assert the panel shows them, selecting a row reveals the matching graph route and details, and no request goes to `/chats/{chatName}/brain/events`.
- [ ] **Step 2: Run `dotnet test src/Applications/IntoChat/Tests/E2E/IntoChat.Tests.E2E.csproj --filter FullyQualifiedName~NeuronActivityGraphFacts`; verify the Activity app is absent.**
- [ ] **Step 3: Wire the launcher, view and client.** Remove or disconnect legacy Brain polling from any reachable product path. Keep the existing gallery sample only if it is clearly a static gallery example.

  ```dart
  if (a.data['app'] == 'activity') {
    return ActivityView(workspaceId: store.currentProject.id, client: client);
  }
  ```
- [ ] **Step 4: Run `dotnet test src/Applications/IntoChat/Tests/E2E/IntoChat.Tests.E2E.csproj --filter FullyQualifiedName~NeuronActivityGraphFacts`, `dotnet test src/Modules/DigitalBrain/Kernel/Tests/Unit/DigitalBrain.Runtime.Tests.Unit.csproj`, and `dotnet test src/Applications/IntoChat/Tests/Unit/IntoChat.Tests.Unit.csproj`. Run `flutter test` and `flutter analyze` in the core, UI, and shell packages. Check `git diff --check` and commit the vertical slice.**

## Completion review

Inspect the final diff against the spec. Confirm every visual line represents an observed relation; scope is verified before recording or reading; exceptions and grain return values are unchanged; a restarted process begins with empty activity; and the UI labels gaps. Record commands and outcomes in the final handoff. A single-silo product run is the supported deployment for this version.
