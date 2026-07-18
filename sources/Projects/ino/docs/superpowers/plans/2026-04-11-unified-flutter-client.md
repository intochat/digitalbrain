# Unified Flutter Client Migration — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace all old TUI client surfaces with a single Flutter codebase (web + Windows desktop), merge the gRPC service into the Telegram service, and add real-data-backed visualization screens for timeline, time travel, and parallel universes.

**Architecture:** Flutter web build served as static files from the Telegram ASP.NET service (single origin, same ngrok tunnel). Flutter Windows desktop launched from Aspire. gRPC service merged into Telegram service — one process hosts bot webhook, gRPC, audio WebSocket, and static files. All gRPC RPCs backed by real Orleans grain calls.

**Tech Stack:** Flutter 3.x (Dart), gRPC + gRPC-Web, BLoC, GoRouter, ASP.NET Core, Orleans, Aspire

**Spec:** `docs/superpowers/specs/2026-04-11-unified-flutter-client-design.md`

---

## File Map

### C# — Merge gRPC into Telegram

| Action | File | Responsibility |
|--------|------|----------------|
| Move | `iaw/Grpc/Protos/ino.proto` → `iaw/Telegram/Protos/ino.proto` | Proto definition (expanded with universe + time-travel RPCs) |
| Move | `iaw/Grpc/Services/InoService.cs` → `iaw/Telegram/Services/InoService.cs` | gRPC service implementation |
| Modify | `iaw/Telegram/Telegram.csproj` | Add gRPC packages + proto reference |
| Modify | `iaw/Telegram/Program.cs` | Add gRPC middleware, `/ws/audio` endpoint, remove hex1b TUI |
| Delete | `iaw/Grpc/` (entire project) | Replaced by merge |
| Modify | `iaw/Aspire/Aspire.csproj` | Remove Grpc + Ino.Windows project references |
| Modify | `iaw/Aspire/AppHost.cs` | Remove grpc/ino-windows/ino-flutter resources, add ino-flutter-win |
| Modify | `ino.slnx` | Remove Grpc.csproj + Ino.Windows.csproj entries |
| Delete | `ino.windows/` (entire project) | Replaced by Flutter Windows |

### Proto — New RPCs

| Action | File | What's added |
|--------|------|-------------|
| Modify | `iaw/Telegram/Protos/ino.proto` | `ForkUniverse`, `ReplayUniverse`, `CompareUniverses`, `GetUniverseTimeline`, `GetUniverseInfo`, `GetStateAt` RPCs + messages |

### C# — Real gRPC Implementations

| Action | File | What changes |
|--------|------|-------------|
| Modify | `iaw/Telegram/Services/InoService.cs` | `StreamEvents` → real observer, `StreamPersonaState` → event-derived, `FireSynapse` → real grain call, `GetTimeline` → real events, + all new universe/time-travel RPCs |

### Flutter — Config + Navigation

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `ino.flutter/lib/main.dart` | Dynamic endpoint detection (web=same origin, desktop=arg/env) |
| Modify | `ino.flutter/lib/grpc/ino_client.dart` | Add universe + time-travel client methods |
| Modify | `ino.flutter/lib/app.dart` | Add routes, switch to `ShellRoute` with bottom nav |

### Flutter — New BLoCs

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `ino.flutter/lib/state/timeline_bloc.dart` | Live event stream + history loading |
| Create | `ino.flutter/lib/state/time_travel_bloc.dart` | Scrub position, state-at snapshots, caching |
| Create | `ino.flutter/lib/state/universe_bloc.dart` | Universe list, fork/replay/compare |

### Flutter — New Screens

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `ino.flutter/lib/screens/timeline/timeline_screen.dart` | Live event feed with filters |
| Create | `ino.flutter/lib/screens/time_travel/time_travel_screen.dart` | Scrubber + state snapshot |
| Create | `ino.flutter/lib/screens/universes/universes_screen.dart` | Fork/compare split view |

### Flutter — New UI Components

| Action | File | Responsibility |
|--------|------|----------------|
| Create | `ino.flutter/lib/ui/components/timeline_event_card.dart` | Single event card rendering |
| Create | `ino.flutter/lib/ui/components/neural_map.dart` | Mini neural map (nodes + edges) |
| Create | `ino.flutter/lib/ui/components/timeline_scrubber.dart` | Horizontal scrubber widget |
| Create | `ino.flutter/lib/ui/components/universe_diff_view.dart` | Side-by-side diff rendering |

---

## Task 1: Merge gRPC into Telegram — Project Wiring

**Files:**
- Modify: `iaw/Telegram/Telegram.csproj`
- Copy: `iaw/Grpc/Protos/ino.proto` → `iaw/Telegram/Protos/ino.proto`
- Copy: `iaw/Grpc/Services/InoService.cs` → `iaw/Telegram/Services/InoService.cs`
- Modify: `iaw/Telegram/Program.cs`
- Delete: `iaw/Grpc/` (entire directory)
- Modify: `iaw/Aspire/Aspire.csproj`
- Modify: `iaw/Aspire/AppHost.cs`
- Modify: `ino.slnx`

- [ ] **Step 1: Add gRPC packages and proto to Telegram.csproj**

Add to `iaw/Telegram/Telegram.csproj` in the `<ItemGroup>` with PackageReferences:

```xml
<PackageReference Include="Grpc.AspNetCore" />
<PackageReference Include="Grpc.AspNetCore.Web" />
```

Add a new ItemGroup for the proto:

```xml
<ItemGroup>
  <Protobuf Include="Protos/ino.proto" GrpcServices="Server" />
</ItemGroup>
```

- [ ] **Step 2: Copy proto and service files**

```bash
cp iaw/Grpc/Protos/ino.proto iaw/Telegram/Protos/ino.proto
cp iaw/Grpc/Services/InoService.cs iaw/Telegram/Services/InoService.cs
```

- [ ] **Step 3: Update InoService.cs namespace**

In `iaw/Telegram/Services/InoService.cs`, change the namespace from `Ino.Grpc.Services` to `TelegramClient.Services` and update the base class reference. The using for `Ino.Grpc` stays because the generated proto types live in that namespace (set by `option csharp_namespace = "Ino.Grpc"` in the proto file).

```csharp
using Core.AI;
using Core.Registry;
using Google.Protobuf;
using Grpc.Core;
using Ino.Grpc;
using InoNew.Core;
using InoNew.Core.Skills;

namespace TelegramClient.Services;

public class InoService(IClusterClient clusterClient, IAudioTranscriptionService transcriber) : global::Ino.Grpc.Ino.InoBase
{
    // ... rest stays identical
}
```

- [ ] **Step 4: Add gRPC middleware to Telegram Program.cs**

In `iaw/Telegram/Program.cs`, add after `builder.AddIAWClient();`:

```csharp
builder.Services.AddGrpc();
```

Add after `app.UseWebSockets();`:

```csharp
app.UseGrpcWeb();
app.MapGrpcService<TelegramClient.Services.InoService>().EnableGrpcWeb();
```

Add the `/ws/audio` endpoint (from Grpc/Program.cs) before `app.MapPost("/ino", ...)`:

```csharp
app.Map("/ws/audio", async (HttpContext context, IAudioTranscriptionService transcriber) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = 400;
        return;
    }

    using var ws = await context.WebSockets.AcceptWebSocketAsync();
    using var ms = new MemoryStream();

    var buffer = new byte[4096];
    while (true)
    {
        var result = await ws.ReceiveAsync(buffer, context.RequestAborted);
        if (result.MessageType == System.Net.WebSockets.WebSocketMessageType.Close)
            break;
        ms.Write(buffer, 0, result.Count);
    }

    ms.Position = 0;
    var text = await transcriber.TranscribeAsync(ms, "audio.wav", context.RequestAborted);

    var responseBytes = System.Text.Encoding.UTF8.GetBytes(text);
    await ws.SendAsync(responseBytes, System.Net.WebSockets.WebSocketMessageType.Text, true, context.RequestAborted);
    await ws.CloseAsync(System.Net.WebSockets.WebSocketCloseStatus.NormalClosure, null, context.RequestAborted);
});
```

Add the `using TelegramClient.Services;` to the top of the file (for InoService).

- [ ] **Step 5: Remove hex1b TUI from Telegram Program.cs**

Remove the entire `BuildInoTui` method (lines 78-213), the `/ws/ino` endpoint (lines 218-240), and the `InoTuiState` class (lines 298-307). Also remove the hex1b-related usings (`using Hex1b;`, `using Hex1b.Widgets;`).

Remove `Hex1b` from the PackageReference in `Telegram.csproj`:

```xml
<!-- DELETE this line -->
<PackageReference Include="Hex1b" />
```

- [ ] **Step 6: Update Aspire wiring**

In `iaw/Aspire/Aspire.csproj`, remove:

```xml
<ProjectReference Include="..\Grpc\Grpc.csproj" />
<ProjectReference Include="..\..\ino.windows\Ino.Windows.csproj" />
```

In `iaw/Aspire/AppHost.cs`, remove these resource blocks:

```csharp
// Remove the grpc resource (lines 81-84):
builder.AddProject<Projects.Grpc>("grpc")
    .WithReference(iaw.AsClient())
    .WithHttpEndpoint(port: 5400, name: "grpc-direct", isProxied: false)
    .WaitFor(assistant);

// Remove the ino-windows resource (lines 76-79):
builder.AddProject<Projects.Ino_Windows>("ino-windows")
    .WithReference(iaw.AsClient())
    .WaitFor(assistant)
    .WithExplicitStart();

// Remove the ino-flutter executable resource (lines 86-89):
builder.AddExecutable("ino-flutter", "flutter", "../../ino.flutter",
        "run", "-d", "chrome", "--web-port", "8080", "--web-hostname", "0.0.0.0")
    .WithHttpEndpoint(port: 8080, name: "http")
    .WithExplicitStart();
```

Add the Flutter Windows resource:

```csharp
builder.AddExecutable("ino-flutter-win", "flutter", "../../ino.flutter",
        "run", "-d", "windows")
    .WithExplicitStart();
```

- [ ] **Step 7: Update ino.slnx**

Remove these lines:

```xml
<Project Path="iaw/Grpc/Grpc.csproj" />
<Project Path="ino.windows/Ino.Windows.csproj" />
```

- [ ] **Step 8: Delete old projects**

```bash
rm -rf iaw/Grpc/
rm -rf ino.windows/
```

- [ ] **Step 9: Build and verify**

```bash
dotnet build ino.slnx
```

Expected: Build succeeds with 0 errors. The Telegram project now hosts gRPC.

- [ ] **Step 10: Commit**

```bash
git add -A
git commit -m "refactor: merge gRPC into Telegram service, remove DevUI + ino-windows

gRPC service (InoService + ino.proto) moved into Telegram project.
Hex1b TUI removed from Telegram. ino-windows console client removed.
DevUI Blazor project removed. Aspire wired for Flutter Windows desktop."
```

---

## Task 2: Expand Proto with Universe + Time-Travel RPCs

**Files:**
- Modify: `iaw/Telegram/Protos/ino.proto`

- [ ] **Step 1: Add new service RPCs to proto**

In `iaw/Telegram/Protos/ino.proto`, add after the `TranscribeAudio` RPC in the service block:

```protobuf
  rpc ForkUniverse(ForkUniverseRequest) returns (ForkUniverseResponse);
  rpc ReplayUniverse(ReplayUniverseRequest) returns (ReplayUniverseResponse);
  rpc CompareUniverses(CompareUniversesRequest) returns (CompareUniversesResponse);
  rpc GetUniverseTimeline(UniverseTimelineQuery) returns (stream TimelineEvent);
  rpc GetUniverseInfo(UniverseInfoRequest) returns (UniverseInfoResponse);
  rpc GetStateAt(StateAtRequest) returns (StateAtResponse);
  rpc ListUniverses(ListUniversesRequest) returns (ListUniversesResponse);
```

- [ ] **Step 2: Add universe message definitions**

Add after the existing `TranscribeResponse` message:

```protobuf
// --- Universe RPCs ---

message ForkUniverseRequest {
  string source_timeline = 1;
  int64 checkpoint_sequence = 2;
  string modified_event_kind = 3;
  string modified_event_source = 4;
  string modified_event_verb = 5;
  map<string, string> modified_event_payload = 6;
}

message ForkUniverseResponse {
  bool ok = 1;
  string universe_id = 2;
}

message ReplayUniverseRequest {
  string universe_id = 1;
}

message ReplayUniverseResponse {
  bool ok = 1;
  int32 events_replayed = 2;
  string summary = 3;
}

message CompareUniversesRequest {
  string universe_a = 1;
  string universe_b = 2;
}

message CompareUniversesResponse {
  int32 shared_events = 1;
  int64 diverged_after_sequence = 2;
  repeated TimelineEvent only_in_a = 3;
  repeated TimelineEvent only_in_b = 4;
}

message UniverseTimelineQuery {
  string universe_id = 1;
}

message UniverseInfoRequest {
  string universe_id = 1;
}

message UniverseInfoResponse {
  string universe_id = 1;
  string source_timeline = 2;
  int64 fork_sequence = 3;
  int32 total_events = 4;
  bool has_replayed = 5;
}

message ListUniversesRequest {}

message ListUniversesResponse {
  repeated UniverseInfoResponse universes = 1;
}

// --- Time-Travel RPCs ---

message StateAtRequest {
  int64 sequence = 1;
}

message StateAtResponse {
  int64 as_of_sequence = 1;
  int64 as_of_timestamp = 2;
  repeated string active_neurons = 3;
  repeated string open_correlations = 4;
  map<string, int32> counts_by_kind = 5;
}
```

- [ ] **Step 3: Build to regenerate proto**

```bash
dotnet build iaw/Telegram/Telegram.csproj
```

Expected: Build succeeds. C# proto stubs generated for all new RPCs.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat(proto): add universe + time-travel RPCs to ino.proto"
```

---

## Task 3: Implement Real gRPC Service Methods

**Files:**
- Modify: `iaw/Telegram/Services/InoService.cs`

- [ ] **Step 1: Add required usings**

At the top of `InoService.cs`, add:

```csharp
using Timetravel.Core;
```

- [ ] **Step 2: Implement StreamEvents with real timeline observer**

Replace the heartbeat stub with a real Orleans observer that live-tails the timeline:

```csharp
public override async Task StreamEvents(
    EventSubscription request,
    IServerStreamWriter<InoEvent> responseStream,
    ServerCallContext context)
{
    var timeline = clusterClient.GetGrain<ITimelineReader>("global");
    var tcs = new TaskCompletionSource();

    var observer = new TimelineStreamObserver(responseStream, context.CancellationToken);
    var observerRef = clusterClient.CreateObjectReference<ITimelineObserver>(observer);

    await timeline.SubscribeAsync(observerRef, context.CancellationToken);
    try
    {
        // send recent history first
        var latest = await timeline.GetLatestSequenceAsync(context.CancellationToken);
        var from = Math.Max(0, latest - 50);
        var history = await timeline.GetEventsInRangeAsync(from, latest, ct: context.CancellationToken);
        foreach (var evt in history)
            await responseStream.WriteAsync(MapTimelineEvent(evt), context.CancellationToken);

        // keep stream open until client disconnects
        await Task.Delay(Timeout.Infinite, context.CancellationToken);
    }
    catch (OperationCanceledException) { }
    finally
    {
        await timeline.UnsubscribeAsync(observerRef);
    }
}

sealed class TimelineStreamObserver(IServerStreamWriter<InoEvent> stream, CancellationToken ct) : ITimelineObserver
{
    public void OnTimelineEvent(TimelineEvent evt)
    {
        if (ct.IsCancellationRequested) return;
        _ = stream.WriteAsync(MapTimelineEvent(evt), ct);
    }
}

static InoEvent MapTimelineEvent(TimelineEvent evt) => new()
{
    Type = evt.Kind.ToString(),
    SourceNeuron = evt.SourceId,
    Payload = Google.Protobuf.ByteString.CopyFromUtf8(
        System.Text.Json.JsonSerializer.Serialize(new
        {
            evt.SequenceNumber, evt.TargetId, evt.CorrelationId,
            evt.SynapseVerb, evt.Decay, evt.Payload
        })),
    Timestamp = evt.Timestamp.ToUnixTimeMilliseconds()
};
```

- [ ] **Step 3: Implement FireSynapse with real grain call**

Replace the stub:

```csharp
public override async Task<FireResponse> FireSynapse(FireRequest request, ServerCallContext context)
{
    var registry = clusterClient.GetGrain<INeuronRegistry>("global");
    var neuron = await registry.GetNeuronAsync(request.TargetNeuron, context.CancellationToken);
    if (neuron is null)
        return new FireResponse { Ok = false };

    var source = clusterClient.GetGrain<INeuron>(request.TargetNeuron);
    var synapse = new InoNew.Core.Synapse(
        Id: Guid.NewGuid().ToString(),
        SourceNeuronId: "user",
        TargetNeuronId: request.TargetNeuron,
        Verb: request.Verb,
        Payload: new Dictionary<string, string>(request.Args),
        FiredAt: DateTimeOffset.UtcNow,
        CorrelationId: Guid.NewGuid().ToString(),
        Decay: 100);

    var receipt = await source.FireAsync(synapse, context.CancellationToken);
    return new FireResponse { Ok = true, SynapseId = receipt.SynapseId };
}
```

- [ ] **Step 4: Implement GetTimeline with real events**

Replace the text-dump stub:

```csharp
public override async Task GetTimeline(
    TimelineQuery request,
    IServerStreamWriter<Ino.Grpc.TimelineEvent> responseStream,
    ServerCallContext context)
{
    var timeline = clusterClient.GetGrain<ITimelineReader>("global");
    var latest = await timeline.GetLatestSequenceAsync(context.CancellationToken);
    var limit = request.Limit > 0 ? request.Limit : 50;
    var from = Math.Max(0, latest - limit);
    var minDecay = request.MinDecay > 0 ? request.MinDecay : Timetravel.Core.TimelineEvent.DefaultSearchFloor;

    var events = await timeline.GetEventsInRangeAsync(from, latest, minDecay, context.CancellationToken);
    foreach (var evt in events)
    {
        await responseStream.WriteAsync(new Ino.Grpc.TimelineEvent
        {
            Sequence = evt.SequenceNumber,
            Kind = evt.Kind.ToString(),
            Source = evt.SourceId,
            Target = evt.TargetId ?? "",
            Timestamp = evt.Timestamp.ToUnixTimeMilliseconds(),
            Decay = evt.Decay
        }, context.CancellationToken);
    }
}
```

- [ ] **Step 5: Implement StreamPersonaState from event activity**

Replace the static stub:

```csharp
public override async Task StreamPersonaState(
    PersonaSubscription request,
    IServerStreamWriter<PersonaState> responseStream,
    ServerCallContext context)
{
    var timeline = clusterClient.GetGrain<ITimelineReader>("global");

    while (!context.CancellationToken.IsCancellationRequested)
    {
        var counts = await timeline.CountByKindAsync(ct: context.CancellationToken);
        var total = counts.Values.Sum();
        var llmCount = counts.GetValueOrDefault(TimelineEventKind.LlmCallStarted);
        var toolCount = counts.GetValueOrDefault(TimelineEventKind.ToolInvoked);

        var emotion = total == 0 ? "idle"
            : llmCount > toolCount ? "thinking"
            : toolCount > 0 ? "acting"
            : "idle";
        var energy = Math.Min(1.0f, total / 100f);

        await responseStream.WriteAsync(new PersonaState
        {
            Emotion = emotion,
            Energy = energy,
            Confidence = 1.0f
        }, context.CancellationToken);

        await Task.Delay(3000, context.CancellationToken);
    }
}
```

- [ ] **Step 6: Implement GetStateAt**

```csharp
public override async Task<StateAtResponse> GetStateAt(StateAtRequest request, ServerCallContext context)
{
    var timeline = clusterClient.GetGrain<ITimelineReader>("global");
    var snapshot = await timeline.GetStateAtAsync(request.Sequence, context.CancellationToken);

    var response = new StateAtResponse
    {
        AsOfSequence = snapshot.AsOfSequence,
        AsOfTimestamp = snapshot.AsOfTimestamp.ToUnixTimeMilliseconds()
    };
    response.ActiveNeurons.AddRange(snapshot.ActiveNeurons);
    response.OpenCorrelations.AddRange(snapshot.OpenCorrelations);
    foreach (var kv in snapshot.CountsByKind)
        response.CountsByKind[kv.Key.ToString()] = kv.Value;

    return response;
}
```

- [ ] **Step 7: Implement ForkUniverse**

```csharp
public override async Task<ForkUniverseResponse> ForkUniverse(ForkUniverseRequest request, ServerCallContext context)
{
    var universeId = $"universe-fork-{request.CheckpointSequence}-{Guid.NewGuid():N[..6]}";
    var universe = clusterClient.GetGrain<IUniverse>(universeId);

    var modifiedEvent = new Timetravel.Core.TimelineEvent(
        SequenceNumber: request.CheckpointSequence,
        Timestamp: DateTimeOffset.UtcNow,
        Kind: Enum.TryParse<TimelineEventKind>(request.ModifiedEventKind, out var kind) ? kind : TimelineEventKind.SynapseFired,
        SourceId: request.ModifiedEventSource,
        TargetId: null,
        CorrelationId: Guid.NewGuid().ToString(),
        SynapseVerb: request.ModifiedEventVerb,
        Payload: new Dictionary<string, string>(request.ModifiedEventPayload),
        Decay: Timetravel.Core.TimelineEvent.DecayHot);

    await universe.ForkAsync(
        request.SourceTimeline.Length > 0 ? request.SourceTimeline : "global",
        request.CheckpointSequence,
        modifiedEvent,
        context.CancellationToken);

    return new ForkUniverseResponse { Ok = true, UniverseId = universeId };
}
```

- [ ] **Step 8: Implement ReplayUniverse**

```csharp
public override async Task<ReplayUniverseResponse> ReplayUniverse(ReplayUniverseRequest request, ServerCallContext context)
{
    var universe = clusterClient.GetGrain<IUniverse>(request.UniverseId);
    var result = await universe.ReplayAsync(context.CancellationToken);
    return new ReplayUniverseResponse
    {
        Ok = result.Success,
        EventsReplayed = result.EventsReplayed,
        Summary = result.Summary
    };
}
```

- [ ] **Step 9: Implement CompareUniverses**

```csharp
public override async Task<CompareUniversesResponse> CompareUniverses(CompareUniversesRequest request, ServerCallContext context)
{
    var universe = clusterClient.GetGrain<IUniverse>(request.UniverseA);
    var diff = await universe.CompareAsync(request.UniverseB, context.CancellationToken);

    var response = new CompareUniversesResponse
    {
        SharedEvents = diff.SharedEvents,
        DivergedAfterSequence = diff.DivergedAfterSequence
    };

    foreach (var evt in diff.OnlyInA)
        response.OnlyInA.Add(new Ino.Grpc.TimelineEvent
        {
            Sequence = evt.SequenceNumber, Kind = evt.Kind.ToString(),
            Source = evt.SourceId, Target = evt.TargetId ?? "",
            Timestamp = evt.Timestamp.ToUnixTimeMilliseconds(), Decay = evt.Decay
        });

    foreach (var evt in diff.OnlyInB)
        response.OnlyInB.Add(new Ino.Grpc.TimelineEvent
        {
            Sequence = evt.SequenceNumber, Kind = evt.Kind.ToString(),
            Source = evt.SourceId, Target = evt.TargetId ?? "",
            Timestamp = evt.Timestamp.ToUnixTimeMilliseconds(), Decay = evt.Decay
        });

    return response;
}
```

- [ ] **Step 10: Implement GetUniverseTimeline and GetUniverseInfo**

```csharp
public override async Task GetUniverseTimeline(
    UniverseTimelineQuery request,
    IServerStreamWriter<Ino.Grpc.TimelineEvent> responseStream,
    ServerCallContext context)
{
    var universe = clusterClient.GetGrain<IUniverse>(request.UniverseId);
    var events = await universe.GetTimelineAsync(context.CancellationToken);
    foreach (var evt in events)
    {
        await responseStream.WriteAsync(new Ino.Grpc.TimelineEvent
        {
            Sequence = evt.SequenceNumber, Kind = evt.Kind.ToString(),
            Source = evt.SourceId, Target = evt.TargetId ?? "",
            Timestamp = evt.Timestamp.ToUnixTimeMilliseconds(), Decay = evt.Decay
        }, context.CancellationToken);
    }
}

public override async Task<UniverseInfoResponse> GetUniverseInfo(UniverseInfoRequest request, ServerCallContext context)
{
    var universe = clusterClient.GetGrain<IUniverse>(request.UniverseId);
    var info = await universe.GetInfoAsync(context.CancellationToken);
    return new UniverseInfoResponse
    {
        UniverseId = info.UniverseId,
        SourceTimeline = info.SourceTimeline,
        ForkSequence = info.ForkSequence,
        TotalEvents = info.TotalEvents,
        HasReplayed = info.HasReplayed
    };
}
```

- [ ] **Step 11: Implement ListUniverses**

This requires a way to discover universe grain IDs. Since `INeuronRegistry` tracks synapses and the universe fork events are on the timeline, query the timeline for `SynapseFired` events with verb "fork":

```csharp
public override async Task<ListUniversesResponse> ListUniverses(ListUniversesRequest request, ServerCallContext context)
{
    // Universe IDs are discoverable from timeline fork events
    var timeline = clusterClient.GetGrain<ITimelineReader>("global");
    var latest = await timeline.GetLatestSequenceAsync(context.CancellationToken);
    var events = await timeline.GetEventsInRangeAsync(0, latest, ct: context.CancellationToken);

    var response = new ListUniversesResponse();
    var seen = new HashSet<string>();

    foreach (var evt in events.Where(e => e.Payload.ContainsKey("universe_id")))
    {
        var uid = evt.Payload["universe_id"];
        if (!seen.Add(uid)) continue;

        try
        {
            var universe = clusterClient.GetGrain<IUniverse>(uid);
            var info = await universe.GetInfoAsync(context.CancellationToken);
            response.Universes.Add(new UniverseInfoResponse
            {
                UniverseId = info.UniverseId,
                SourceTimeline = info.SourceTimeline,
                ForkSequence = info.ForkSequence,
                TotalEvents = info.TotalEvents,
                HasReplayed = info.HasReplayed
            });
        }
        catch { }
    }

    return response;
}
```

- [ ] **Step 12: Build and verify**

```bash
dotnet build ino.slnx
```

Expected: Build succeeds with 0 errors.

- [ ] **Step 13: Commit**

```bash
git add -A
git commit -m "feat(grpc): implement real Orleans-backed gRPC service methods

StreamEvents live-tails timeline via observer. FireSynapse fires real
synapses. GetTimeline returns real events with decay. GetStateAt returns
timeline snapshots. All universe RPCs (fork/replay/compare/info/list)
backed by IUniverse grain. PersonaState derived from event activity."
```

---

## Task 4: Flutter — Dynamic Endpoint + Proto Regeneration

**Files:**
- Modify: `ino.flutter/lib/main.dart`
- Modify: `ino.flutter/lib/grpc/ino_client.dart`
- Regenerate: `ino.flutter/lib/grpc/generated/`

- [ ] **Step 1: Copy updated proto to Flutter and regenerate**

```bash
cp iaw/Telegram/Protos/ino.proto ino.flutter/protos/ino.proto
cd ino.flutter
protoc --dart_out=grpc:lib/grpc/generated -Iprotos protos/ino.proto
```

If `protoc` with dart plugin is not available, use:

```bash
dart pub global activate protoc_plugin
protoc --dart_out=grpc:lib/grpc/generated -Iprotos --plugin=protoc-gen-dart=$HOME/.pub-cache/bin/protoc-gen-dart protos/ino.proto
```

- [ ] **Step 2: Update main.dart for dynamic endpoint detection**

Replace the hardcoded `localhost:5400` in `ino.flutter/lib/main.dart`:

```dart
import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/app.dart';
import 'package:ino_flutter/grpc/ino_client.dart';
import 'package:ino_flutter/state/ino_bloc.dart';
import 'package:ino_flutter/state/persona_bloc.dart';
import 'package:ino_flutter/state/skills_bloc.dart';
import 'package:ino_flutter/state/timeline_bloc.dart';
import 'package:ino_flutter/state/time_travel_bloc.dart';
import 'package:ino_flutter/state/universe_bloc.dart';
import 'package:ino_flutter/voice/audio_recorder.dart';
import 'package:ino_flutter/voice/audio_transport.dart';
import 'package:ino_flutter/voice/grpc_audio_transport.dart';
import 'package:ino_flutter/voice/websocket_audio_transport.dart';

void main() {
  final (host, port) = _resolveEndpoint();

  final client = InoGrpcClient(host: host, port: port);

  final AudioTransport audioTransport = kIsWeb
      ? WebSocketAudioTransport(wsUrl: 'ws://$host:$port/ws/audio')
      : GrpcAudioTransport(channel: client.channel);
  final recorder = AudioRecorderService();

  runApp(
    MultiBlocProvider(
      providers: [
        BlocProvider(
          create: (_) => InoBloc(
            client: client,
            audioTransport: audioTransport,
            recorder: recorder,
          ),
        ),
        BlocProvider(create: (_) => PersonaBloc(client: client)),
        BlocProvider(create: (_) => SkillsBloc(client: client)),
        BlocProvider(create: (_) => TimelineBloc(client: client)),
        BlocProvider(create: (_) => TimeTravelBloc(client: client)),
        BlocProvider(create: (_) => UniverseBloc(client: client)),
      ],
      child: const InoApp(),
    ),
  );
}

(String, int) _resolveEndpoint() {
  if (kIsWeb) {
    final uri = Uri.base;
    return (uri.host, uri.port);
  }
  // Desktop: use environment variable or default to localhost
  const envHost = String.fromEnvironment('GRPC_HOST', defaultValue: 'localhost');
  const envPort = int.fromEnvironment('GRPC_PORT', defaultValue: 5400);
  return (envHost, envPort);
}
```

- [ ] **Step 3: Add universe + time-travel methods to gRPC client**

In `ino.flutter/lib/grpc/ino_client.dart`, add these methods to the `InoGrpcClient` class:

```dart
Future<pb.ForkUniverseResponse> forkUniverse({
  String sourceTimeline = 'global',
  required int checkpointSequence,
  required String modifiedEventKind,
  required String modifiedEventSource,
  String modifiedEventVerb = '',
  Map<String, String> modifiedEventPayload = const {},
}) {
  return _stub.forkUniverse(pb.ForkUniverseRequest()
    ..sourceTimeline = sourceTimeline
    ..checkpointSequence = checkpointSequence.toInt64()
    ..modifiedEventKind = modifiedEventKind
    ..modifiedEventSource = modifiedEventSource
    ..modifiedEventVerb = modifiedEventVerb
    ..modifiedEventPayload.addAll(modifiedEventPayload));
}

Future<pb.ReplayUniverseResponse> replayUniverse(String universeId) {
  return _stub.replayUniverse(pb.ReplayUniverseRequest()..universeId = universeId);
}

Future<pb.CompareUniversesResponse> compareUniverses(String universeA, String universeB) {
  return _stub.compareUniverses(pb.CompareUniversesRequest()
    ..universeA = universeA
    ..universeB = universeB);
}

Stream<pb.TimelineEvent> getUniverseTimeline(String universeId) {
  return _stub.getUniverseTimeline(pb.UniverseTimelineQuery()..universeId = universeId);
}

Future<pb.UniverseInfoResponse> getUniverseInfo(String universeId) {
  return _stub.getUniverseInfo(pb.UniverseInfoRequest()..universeId = universeId);
}

Future<pb.ListUniversesResponse> listUniverses() {
  return _stub.listUniverses(pb.ListUniversesRequest());
}

Future<pb.StateAtResponse> getStateAt(int sequence) {
  return _stub.getStateAt(pb.StateAtRequest()..sequence = sequence.toInt64());
}
```

- [ ] **Step 4: Build Flutter to verify**

```bash
cd ino.flutter && flutter analyze
```

Expected: No analysis errors.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(flutter): dynamic endpoint detection + universe/time-travel gRPC client"
```

---

## Task 5: Flutter — Timeline BLoC + Screen

**Files:**
- Create: `ino.flutter/lib/state/timeline_bloc.dart`
- Create: `ino.flutter/lib/screens/timeline/timeline_screen.dart`
- Create: `ino.flutter/lib/ui/components/timeline_event_card.dart`

- [ ] **Step 1: Create TimelineBloc**

Create `ino.flutter/lib/state/timeline_bloc.dart`:

```dart
import 'dart:async';
import 'dart:convert';

import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/grpc/ino_client.dart';

sealed class TimelineBlocEvent {}

class TimelineStarted extends TimelineBlocEvent {}

class TimelinePaused extends TimelineBlocEvent {}

class TimelineResumed extends TimelineBlocEvent {}

class TimelineFilterChanged extends TimelineBlocEvent {
  TimelineFilterChanged({this.minDecay, this.kinds});
  final int? minDecay;
  final Set<String>? kinds;
}

class _EventReceived extends TimelineBlocEvent {
  _EventReceived(this.event);
  final TimelineEntry event;
}

class TimelineEntry {
  const TimelineEntry({
    required this.sequence,
    required this.kind,
    required this.source,
    required this.target,
    required this.timestamp,
    required this.decay,
    this.verb,
    this.correlationId,
    this.payload = const {},
  });

  final int sequence;
  final String kind;
  final String source;
  final String target;
  final int timestamp;
  final int decay;
  final String? verb;
  final String? correlationId;
  final Map<String, dynamic> payload;
}

class TimelineBlocState {
  const TimelineBlocState({
    this.events = const [],
    this.isLive = true,
    this.isLoading = false,
    this.minDecay = 30,
    this.activeKinds = const {},
  });

  final List<TimelineEntry> events;
  final bool isLive;
  final bool isLoading;
  final int minDecay;
  final Set<String> activeKinds;

  TimelineBlocState copyWith({
    List<TimelineEntry>? events,
    bool? isLive,
    bool? isLoading,
    int? minDecay,
    Set<String>? activeKinds,
  }) {
    return TimelineBlocState(
      events: events ?? this.events,
      isLive: isLive ?? this.isLive,
      isLoading: isLoading ?? this.isLoading,
      minDecay: minDecay ?? this.minDecay,
      activeKinds: activeKinds ?? this.activeKinds,
    );
  }
}

class TimelineBloc extends Bloc<TimelineBlocEvent, TimelineBlocState> {
  TimelineBloc({required InoGrpcClient client})
      : _client = client,
        super(const TimelineBlocState()) {
    on<TimelineStarted>(_onStarted);
    on<TimelinePaused>(_onPaused);
    on<TimelineResumed>(_onResumed);
    on<TimelineFilterChanged>(_onFilterChanged);
    on<_EventReceived>(_onEventReceived);
  }

  final InoGrpcClient _client;
  StreamSubscription<dynamic>? _subscription;

  Future<void> _onStarted(
    TimelineStarted event,
    Emitter<TimelineBlocState> emit,
  ) async {
    emit(state.copyWith(isLoading: true));

    // load history
    final history = <TimelineEntry>[];
    await for (final evt in _client.getTimeline(limit: 50, minDecay: state.minDecay)) {
      history.add(_mapEvent(evt));
    }
    emit(state.copyWith(events: history, isLoading: false));

    // start live tail
    _startLiveTail();
  }

  void _startLiveTail() {
    _subscription?.cancel();
    _subscription = _client.streamEvents().listen(
      (evt) => add(_EventReceived(_mapInoEvent(evt))),
      onError: (_) {},
    );
  }

  void _onPaused(TimelinePaused event, Emitter<TimelineBlocState> emit) {
    _subscription?.cancel();
    _subscription = null;
    emit(state.copyWith(isLive: false));
  }

  void _onResumed(TimelineResumed event, Emitter<TimelineBlocState> emit) {
    emit(state.copyWith(isLive: true));
    _startLiveTail();
  }

  void _onFilterChanged(TimelineFilterChanged event, Emitter<TimelineBlocState> emit) {
    emit(state.copyWith(
      minDecay: event.minDecay ?? state.minDecay,
      activeKinds: event.kinds ?? state.activeKinds,
    ));
    // reload with new filters
    add(TimelineStarted());
  }

  void _onEventReceived(_EventReceived event, Emitter<TimelineBlocState> emit) {
    if (!state.isLive) return;
    final filtered = state.activeKinds.isEmpty || state.activeKinds.contains(event.event.kind);
    if (!filtered) return;
    emit(state.copyWith(events: [...state.events, event.event]));
  }

  TimelineEntry _mapEvent(TimelineEvent evt) {
    return TimelineEntry(
      sequence: evt.sequence.toInt(),
      kind: evt.kind,
      source: evt.source,
      target: evt.target,
      timestamp: evt.timestamp.toInt(),
      decay: evt.decay,
    );
  }

  TimelineEntry _mapInoEvent(InoEvent evt) {
    Map<String, dynamic> payload = {};
    String? verb;
    String? correlationId;
    try {
      payload = jsonDecode(String.fromCharCodes(evt.payload)) as Map<String, dynamic>;
      verb = payload['SynapseVerb'] as String?;
      correlationId = payload['CorrelationId'] as String?;
    } catch (_) {}

    return TimelineEntry(
      sequence: payload['SequenceNumber'] as int? ?? 0,
      kind: evt.type,
      source: evt.sourceNeuron,
      target: payload['TargetId'] as String? ?? '',
      timestamp: evt.timestamp.toInt(),
      decay: payload['Decay'] as int? ?? 100,
      verb: verb,
      correlationId: correlationId,
      payload: payload,
    );
  }

  @override
  Future<void> close() {
    _subscription?.cancel();
    return super.close();
  }
}
```

- [ ] **Step 2: Create TimelineEventCard widget**

Create `ino.flutter/lib/ui/components/timeline_event_card.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:ino_flutter/state/timeline_bloc.dart';

class TimelineEventCard extends StatelessWidget {
  const TimelineEventCard({super.key, required this.entry});

  final TimelineEntry entry;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final time = DateTime.fromMillisecondsSinceEpoch(entry.timestamp);
    final timeStr = '${time.hour.toString().padLeft(2, '0')}:${time.minute.toString().padLeft(2, '0')}:${time.second.toString().padLeft(2, '0')}';

    return Card(
      margin: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
      child: ExpansionTile(
        leading: _kindIcon(entry.kind),
        title: Text(
          entry.kind,
          style: theme.textTheme.bodyMedium?.copyWith(fontWeight: FontWeight.w600),
        ),
        subtitle: Text(
          '${entry.source}${entry.target.isNotEmpty ? ' → ${entry.target}' : ''} · $timeStr',
          style: theme.textTheme.bodySmall,
        ),
        trailing: _decayBadge(entry.decay, theme),
        children: [
          if (entry.verb != null)
            ListTile(
              dense: true,
              title: Text('Verb: ${entry.verb}'),
            ),
          if (entry.payload.isNotEmpty)
            Padding(
              padding: const EdgeInsets.all(12),
              child: Text(
                entry.payload.entries.map((e) => '${e.key}: ${e.value}').join('\n'),
                style: theme.textTheme.bodySmall?.copyWith(fontFamily: 'monospace'),
              ),
            ),
        ],
      ),
    );
  }

  Widget _kindIcon(String kind) {
    final (icon, color) = switch (kind) {
      'NeuronActivated' => (Icons.flash_on, Colors.green),
      'NeuronDeactivated' => (Icons.flash_off, Colors.grey),
      'SynapseFired' => (Icons.call_split, Colors.blue),
      'ToolInvoked' => (Icons.build, Colors.orange),
      'ToolCompleted' => (Icons.check_circle, Colors.green),
      'LlmCallStarted' => (Icons.psychology, Colors.purple),
      'LlmCallCompleted' => (Icons.psychology_alt, Colors.deepPurple),
      'SelfImprovementL1' => (Icons.auto_fix_high, Colors.amber),
      'SelfImprovementL2' => (Icons.auto_fix_high, Colors.deepOrange),
      'SelfImprovementL3' => (Icons.auto_fix_high, Colors.red),
      'Error' => (Icons.error, Colors.red),
      _ => (Icons.circle, Colors.grey),
    };
    return Icon(icon, color: color, size: 24);
  }

  Widget _decayBadge(int decay, ThemeData theme) {
    final (label, color) = switch (decay) {
      >= 80 => ('HOT', Colors.red),
      >= 50 => ('WARM', Colors.orange),
      >= 30 => ('COLD', Colors.blue),
      _ => ('FADED', Colors.grey),
    };
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 2),
      decoration: BoxDecoration(
        color: color.withValues(alpha: 0.2),
        borderRadius: BorderRadius.circular(4),
      ),
      child: Text(label, style: theme.textTheme.labelSmall?.copyWith(color: color)),
    );
  }
}
```

- [ ] **Step 3: Create TimelineScreen**

Create `ino.flutter/lib/screens/timeline/timeline_screen.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/state/timeline_bloc.dart';
import 'package:ino_flutter/ui/components/timeline_event_card.dart';

class TimelineScreen extends StatefulWidget {
  const TimelineScreen({super.key});

  @override
  State<TimelineScreen> createState() => _TimelineScreenState();
}

class _TimelineScreenState extends State<TimelineScreen> {
  final _scrollController = ScrollController();

  @override
  void initState() {
    super.initState();
    context.read<TimelineBloc>().add(TimelineStarted());
  }

  @override
  void dispose() {
    _scrollController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return BlocConsumer<TimelineBloc, TimelineBlocState>(
      listenWhen: (prev, curr) => curr.isLive && curr.events.length > prev.events.length,
      listener: (context, state) {
        if (_scrollController.hasClients) {
          _scrollController.animateTo(
            _scrollController.position.maxScrollExtent,
            duration: const Duration(milliseconds: 300),
            curve: Curves.easeOut,
          );
        }
      },
      builder: (context, state) {
        return Column(
          children: [
            _buildControls(context, state),
            Expanded(
              child: state.isLoading
                  ? const Center(child: CircularProgressIndicator())
                  : state.events.isEmpty
                      ? const Center(child: Text('No events yet'))
                      : ListView.builder(
                          controller: _scrollController,
                          itemCount: state.events.length,
                          itemBuilder: (context, index) =>
                              TimelineEventCard(entry: state.events[index]),
                        ),
            ),
          ],
        );
      },
    );
  }

  Widget _buildControls(BuildContext context, TimelineBlocState state) {
    final bloc = context.read<TimelineBloc>();
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
      child: Row(
        children: [
          IconButton(
            icon: Icon(state.isLive ? Icons.pause : Icons.play_arrow),
            onPressed: () => bloc.add(state.isLive ? TimelinePaused() : TimelineResumed()),
          ),
          const SizedBox(width: 8),
          Text('Decay ≥ ${state.minDecay}'),
          Expanded(
            child: Slider(
              value: state.minDecay.toDouble(),
              min: 1,
              max: 100,
              divisions: 99,
              onChanged: (v) => bloc.add(TimelineFilterChanged(minDecay: v.round())),
            ),
          ),
          Text('${state.events.length} events'),
        ],
      ),
    );
  }
}
```

- [ ] **Step 4: Verify Flutter analysis**

```bash
cd ino.flutter && flutter analyze
```

Expected: No errors.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(flutter): timeline screen with live event stream and decay filters"
```

---

## Task 6: Flutter — Time Travel BLoC + Screen

**Files:**
- Create: `ino.flutter/lib/state/time_travel_bloc.dart`
- Create: `ino.flutter/lib/screens/time_travel/time_travel_screen.dart`
- Create: `ino.flutter/lib/ui/components/timeline_scrubber.dart`
- Create: `ino.flutter/lib/ui/components/neural_map.dart`

- [ ] **Step 1: Create TimeTravelBloc**

Create `ino.flutter/lib/state/time_travel_bloc.dart`:

```dart
import 'dart:async';

import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/grpc/ino_client.dart';

sealed class TimeTravelEvent {}

class TimeTravelStarted extends TimeTravelEvent {}

class TimeTravelScrubbed extends TimeTravelEvent {
  TimeTravelScrubbed(this.sequence);
  final int sequence;
}

class StateSnapshot {
  const StateSnapshot({
    required this.asOfSequence,
    required this.asOfTimestamp,
    required this.activeNeurons,
    required this.openCorrelations,
    required this.countsByKind,
  });

  final int asOfSequence;
  final int asOfTimestamp;
  final List<String> activeNeurons;
  final List<String> openCorrelations;
  final Map<String, int> countsByKind;
}

class TimeTravelState {
  const TimeTravelState({
    this.currentSequence = 0,
    this.maxSequence = 0,
    this.snapshot,
    this.isLoading = false,
  });

  final int currentSequence;
  final int maxSequence;
  final StateSnapshot? snapshot;
  final bool isLoading;

  TimeTravelState copyWith({
    int? currentSequence,
    int? maxSequence,
    StateSnapshot? snapshot,
    bool? isLoading,
  }) {
    return TimeTravelState(
      currentSequence: currentSequence ?? this.currentSequence,
      maxSequence: maxSequence ?? this.maxSequence,
      snapshot: snapshot ?? this.snapshot,
      isLoading: isLoading ?? this.isLoading,
    );
  }
}

class TimeTravelBloc extends Bloc<TimeTravelEvent, TimeTravelState> {
  TimeTravelBloc({required InoGrpcClient client})
      : _client = client,
        super(const TimeTravelState()) {
    on<TimeTravelStarted>(_onStarted);
    on<TimeTravelScrubbed>(_onScrubbed);
  }

  final InoGrpcClient _client;
  final Map<int, StateSnapshot> _cache = {};
  Timer? _debounce;

  Future<void> _onStarted(
    TimeTravelStarted event,
    Emitter<TimeTravelState> emit,
  ) async {
    emit(state.copyWith(isLoading: true));

    // get timeline bounds
    final events = <int>[];
    await for (final evt in _client.getTimeline(limit: 1000)) {
      events.add(evt.sequence.toInt());
    }

    final maxSeq = events.isEmpty ? 0 : events.last;
    emit(state.copyWith(maxSequence: maxSeq, currentSequence: maxSeq, isLoading: false));

    if (maxSeq > 0) add(TimeTravelScrubbed(maxSeq));
  }

  Future<void> _onScrubbed(
    TimeTravelScrubbed event,
    Emitter<TimeTravelState> emit,
  ) async {
    emit(state.copyWith(currentSequence: event.sequence, isLoading: true));

    // check cache
    if (_cache.containsKey(event.sequence)) {
      emit(state.copyWith(snapshot: _cache[event.sequence], isLoading: false));
      return;
    }

    try {
      final response = await _client.getStateAt(event.sequence);
      final snapshot = StateSnapshot(
        asOfSequence: response.asOfSequence.toInt(),
        asOfTimestamp: response.asOfTimestamp.toInt(),
        activeNeurons: response.activeNeurons.toList(),
        openCorrelations: response.openCorrelations.toList(),
        countsByKind: Map.fromEntries(
          response.countsByKind.entries.map((e) => MapEntry(e.key, e.value)),
        ),
      );
      _cache[event.sequence] = snapshot;
      emit(state.copyWith(snapshot: snapshot, isLoading: false));
    } catch (e) {
      emit(state.copyWith(isLoading: false));
    }
  }

  @override
  Future<void> close() {
    _debounce?.cancel();
    return super.close();
  }
}
```

- [ ] **Step 2: Create TimelineScrubber widget**

Create `ino.flutter/lib/ui/components/timeline_scrubber.dart`:

```dart
import 'package:flutter/material.dart';

class TimelineScrubber extends StatelessWidget {
  const TimelineScrubber({
    super.key,
    required this.current,
    required this.max,
    required this.onChanged,
  });

  final int current;
  final int max;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
      child: Column(
        children: [
          Row(
            children: [
              Icon(Icons.history, size: 20, color: theme.colorScheme.primary),
              const SizedBox(width: 8),
              Text('Sequence: $current / $max',
                  style: theme.textTheme.bodyMedium),
            ],
          ),
          Slider(
            value: current.toDouble(),
            min: 0,
            max: max > 0 ? max.toDouble() : 1,
            onChanged: max > 0
                ? (v) => onChanged(v.round())
                : null,
          ),
        ],
      ),
    );
  }
}
```

- [ ] **Step 3: Create NeuralMap widget**

Create `ino.flutter/lib/ui/components/neural_map.dart`:

```dart
import 'dart:math';

import 'package:flutter/material.dart';
import 'package:ino_flutter/state/time_travel_bloc.dart';

class NeuralMap extends StatelessWidget {
  const NeuralMap({super.key, required this.snapshot});

  final StateSnapshot snapshot;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    if (snapshot.activeNeurons.isEmpty) {
      return Center(
        child: Text('No active neurons at this point',
            style: theme.textTheme.bodyLarge),
      );
    }

    return CustomPaint(
      painter: _NeuralMapPainter(
        neurons: snapshot.activeNeurons,
        correlations: snapshot.openCorrelations,
        primaryColor: theme.colorScheme.primary,
        surfaceColor: theme.colorScheme.surface,
        textColor: theme.colorScheme.onSurface,
      ),
      child: const SizedBox.expand(),
    );
  }
}

class _NeuralMapPainter extends CustomPainter {
  _NeuralMapPainter({
    required this.neurons,
    required this.correlations,
    required this.primaryColor,
    required this.surfaceColor,
    required this.textColor,
  });

  final List<String> neurons;
  final List<String> correlations;
  final Color primaryColor;
  final Color surfaceColor;
  final Color textColor;

  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width / 2, size.height / 2);
    final radius = min(size.width, size.height) * 0.35;
    final nodeRadius = 24.0;

    // position neurons in a circle
    final positions = <String, Offset>{};
    for (var i = 0; i < neurons.length; i++) {
      final angle = (2 * pi * i / neurons.length) - pi / 2;
      positions[neurons[i]] = Offset(
        center.dx + radius * cos(angle),
        center.dy + radius * sin(angle),
      );
    }

    // draw connection lines for correlations
    final linePaint = Paint()
      ..color = primaryColor.withValues(alpha: 0.3)
      ..strokeWidth = 2;

    for (var i = 0; i < neurons.length; i++) {
      for (var j = i + 1; j < neurons.length; j++) {
        canvas.drawLine(positions[neurons[i]]!, positions[neurons[j]]!, linePaint);
      }
    }

    // draw neuron nodes
    final nodePaint = Paint()..color = primaryColor;
    final glowPaint = Paint()
      ..color = primaryColor.withValues(alpha: 0.15)
      ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 12);
    final textPainter = TextPainter(textDirection: TextDirection.ltr);

    for (final entry in positions.entries) {
      final pos = entry.value;
      canvas.drawCircle(pos, nodeRadius + 8, glowPaint);
      canvas.drawCircle(pos, nodeRadius, nodePaint);

      textPainter.text = TextSpan(
        text: entry.key.length > 8 ? '${entry.key.substring(0, 8)}…' : entry.key,
        style: TextStyle(color: surfaceColor, fontSize: 10, fontWeight: FontWeight.w600),
      );
      textPainter.layout();
      textPainter.paint(canvas, pos - Offset(textPainter.width / 2, textPainter.height / 2));
    }
  }

  @override
  bool shouldRepaint(covariant _NeuralMapPainter old) =>
      neurons != old.neurons || correlations != old.correlations;
}
```

- [ ] **Step 4: Create TimeTravelScreen**

Create `ino.flutter/lib/screens/time_travel/time_travel_screen.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/state/time_travel_bloc.dart';
import 'package:ino_flutter/ui/components/neural_map.dart';
import 'package:ino_flutter/ui/components/timeline_scrubber.dart';

class TimeTravelScreen extends StatefulWidget {
  const TimeTravelScreen({super.key});

  @override
  State<TimeTravelScreen> createState() => _TimeTravelScreenState();
}

class _TimeTravelScreenState extends State<TimeTravelScreen> {
  @override
  void initState() {
    super.initState();
    context.read<TimeTravelBloc>().add(TimeTravelStarted());
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return BlocBuilder<TimeTravelBloc, TimeTravelState>(
      builder: (context, state) {
        return Column(
          children: [
            TimelineScrubber(
              current: state.currentSequence,
              max: state.maxSequence,
              onChanged: (seq) =>
                  context.read<TimeTravelBloc>().add(TimeTravelScrubbed(seq)),
            ),
            if (state.isLoading)
              const Expanded(child: Center(child: CircularProgressIndicator()))
            else if (state.snapshot != null) ...[
              // counts summary
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 16),
                child: Wrap(
                  spacing: 8,
                  children: state.snapshot!.countsByKind.entries.map((e) {
                    return Chip(
                      label: Text('${e.key}: ${e.value}'),
                      visualDensity: VisualDensity.compact,
                    );
                  }).toList(),
                ),
              ),
              const SizedBox(height: 8),
              // neural map
              Expanded(child: NeuralMap(snapshot: state.snapshot!)),
            ] else
              Expanded(
                child: Center(
                  child: Text('Drag the scrubber to explore the timeline',
                      style: theme.textTheme.bodyLarge),
                ),
              ),
          ],
        );
      },
    );
  }
}
```

- [ ] **Step 5: Verify Flutter analysis**

```bash
cd ino.flutter && flutter analyze
```

Expected: No errors.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(flutter): time-travel screen with scrubber and neural map visualization"
```

---

## Task 7: Flutter — Parallel Universes BLoC + Screen

**Files:**
- Create: `ino.flutter/lib/state/universe_bloc.dart`
- Create: `ino.flutter/lib/screens/universes/universes_screen.dart`
- Create: `ino.flutter/lib/ui/components/universe_diff_view.dart`

- [ ] **Step 1: Create UniverseBloc**

Create `ino.flutter/lib/state/universe_bloc.dart`:

```dart
import 'dart:async';

import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/grpc/ino_client.dart';
import 'package:ino_flutter/state/timeline_bloc.dart';

sealed class UniverseBlocEvent {}

class UniverseListLoaded extends UniverseBlocEvent {}

class UniverseForked extends UniverseBlocEvent {
  UniverseForked({
    required this.checkpointSequence,
    required this.modifiedEventKind,
    required this.modifiedEventSource,
    this.modifiedEventVerb = '',
  });
  final int checkpointSequence;
  final String modifiedEventKind;
  final String modifiedEventSource;
  final String modifiedEventVerb;
}

class UniverseReplayed extends UniverseBlocEvent {
  UniverseReplayed(this.universeId);
  final String universeId;
}

class UniverseCompared extends UniverseBlocEvent {
  UniverseCompared(this.universeA, this.universeB);
  final String universeA;
  final String universeB;
}

class UniverseSelected extends UniverseBlocEvent {
  UniverseSelected(this.universeId);
  final String universeId;
}

class UniverseItem {
  const UniverseItem({
    required this.id,
    required this.sourceTimeline,
    required this.forkSequence,
    required this.totalEvents,
    required this.hasReplayed,
  });

  final String id;
  final String sourceTimeline;
  final int forkSequence;
  final int totalEvents;
  final bool hasReplayed;
}

class UniverseDiffResult {
  const UniverseDiffResult({
    required this.sharedEvents,
    required this.divergedAfterSequence,
    required this.onlyInA,
    required this.onlyInB,
  });

  final int sharedEvents;
  final int divergedAfterSequence;
  final List<TimelineEntry> onlyInA;
  final List<TimelineEntry> onlyInB;
}

class UniverseBlocState {
  const UniverseBlocState({
    this.universes = const [],
    this.selectedId,
    this.selectedTimeline = const [],
    this.diff,
    this.isLoading = false,
    this.replayResult,
    this.error,
  });

  final List<UniverseItem> universes;
  final String? selectedId;
  final List<TimelineEntry> selectedTimeline;
  final UniverseDiffResult? diff;
  final bool isLoading;
  final String? replayResult;
  final String? error;

  UniverseBlocState copyWith({
    List<UniverseItem>? universes,
    String? selectedId,
    List<TimelineEntry>? selectedTimeline,
    UniverseDiffResult? diff,
    bool? isLoading,
    String? replayResult,
    String? error,
  }) {
    return UniverseBlocState(
      universes: universes ?? this.universes,
      selectedId: selectedId ?? this.selectedId,
      selectedTimeline: selectedTimeline ?? this.selectedTimeline,
      diff: diff ?? this.diff,
      isLoading: isLoading ?? this.isLoading,
      replayResult: replayResult ?? this.replayResult,
      error: error,
    );
  }
}

class UniverseBloc extends Bloc<UniverseBlocEvent, UniverseBlocState> {
  UniverseBloc({required InoGrpcClient client})
      : _client = client,
        super(const UniverseBlocState()) {
    on<UniverseListLoaded>(_onListLoaded);
    on<UniverseForked>(_onForked);
    on<UniverseReplayed>(_onReplayed);
    on<UniverseCompared>(_onCompared);
    on<UniverseSelected>(_onSelected);
  }

  final InoGrpcClient _client;

  Future<void> _onListLoaded(
    UniverseListLoaded event,
    Emitter<UniverseBlocState> emit,
  ) async {
    emit(state.copyWith(isLoading: true));
    try {
      final response = await _client.listUniverses();
      final items = response.universes.map((u) => UniverseItem(
        id: u.universeId,
        sourceTimeline: u.sourceTimeline,
        forkSequence: u.forkSequence.toInt(),
        totalEvents: u.totalEvents,
        hasReplayed: u.hasReplayed,
      )).toList();
      emit(state.copyWith(universes: items, isLoading: false));
    } catch (e) {
      emit(state.copyWith(isLoading: false, error: e.toString()));
    }
  }

  Future<void> _onForked(
    UniverseForked event,
    Emitter<UniverseBlocState> emit,
  ) async {
    emit(state.copyWith(isLoading: true));
    try {
      await _client.forkUniverse(
        checkpointSequence: event.checkpointSequence,
        modifiedEventKind: event.modifiedEventKind,
        modifiedEventSource: event.modifiedEventSource,
        modifiedEventVerb: event.modifiedEventVerb,
      );
      add(UniverseListLoaded());
    } catch (e) {
      emit(state.copyWith(isLoading: false, error: e.toString()));
    }
  }

  Future<void> _onReplayed(
    UniverseReplayed event,
    Emitter<UniverseBlocState> emit,
  ) async {
    emit(state.copyWith(isLoading: true));
    try {
      final result = await _client.replayUniverse(event.universeId);
      emit(state.copyWith(
        isLoading: false,
        replayResult: '${result.eventsReplayed} events replayed: ${result.summary}',
      ));
      add(UniverseListLoaded());
    } catch (e) {
      emit(state.copyWith(isLoading: false, error: e.toString()));
    }
  }

  Future<void> _onCompared(
    UniverseCompared event,
    Emitter<UniverseBlocState> emit,
  ) async {
    emit(state.copyWith(isLoading: true));
    try {
      final result = await _client.compareUniverses(event.universeA, event.universeB);
      emit(state.copyWith(
        isLoading: false,
        diff: UniverseDiffResult(
          sharedEvents: result.sharedEvents,
          divergedAfterSequence: result.divergedAfterSequence.toInt(),
          onlyInA: result.onlyInA.map(_mapProtoEvent).toList(),
          onlyInB: result.onlyInB.map(_mapProtoEvent).toList(),
        ),
      ));
    } catch (e) {
      emit(state.copyWith(isLoading: false, error: e.toString()));
    }
  }

  Future<void> _onSelected(
    UniverseSelected event,
    Emitter<UniverseBlocState> emit,
  ) async {
    emit(state.copyWith(selectedId: event.universeId, isLoading: true));
    try {
      final events = <TimelineEntry>[];
      await for (final evt in _client.getUniverseTimeline(event.universeId)) {
        events.add(_mapProtoEvent(evt));
      }
      emit(state.copyWith(selectedTimeline: events, isLoading: false));
    } catch (e) {
      emit(state.copyWith(isLoading: false, error: e.toString()));
    }
  }

  TimelineEntry _mapProtoEvent(TimelineEvent evt) {
    return TimelineEntry(
      sequence: evt.sequence.toInt(),
      kind: evt.kind,
      source: evt.source,
      target: evt.target,
      timestamp: evt.timestamp.toInt(),
      decay: evt.decay,
    );
  }
}
```

- [ ] **Step 2: Create UniverseDiffView widget**

Create `ino.flutter/lib/ui/components/universe_diff_view.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:ino_flutter/state/timeline_bloc.dart';
import 'package:ino_flutter/state/universe_bloc.dart';
import 'package:ino_flutter/ui/components/timeline_event_card.dart';

class UniverseDiffView extends StatelessWidget {
  const UniverseDiffView({
    super.key,
    required this.diff,
    required this.labelA,
    required this.labelB,
  });

  final UniverseDiffResult diff;
  final String labelA;
  final String labelB;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.all(12),
          child: Row(
            children: [
              Chip(label: Text('${diff.sharedEvents} shared events')),
              const SizedBox(width: 8),
              Chip(label: Text('Diverged at seq ${diff.divergedAfterSequence}')),
            ],
          ),
        ),
        Expanded(
          child: Row(
            children: [
              Expanded(child: _buildSide(theme, labelA, diff.onlyInA, Colors.blue)),
              const VerticalDivider(width: 1),
              Expanded(child: _buildSide(theme, labelB, diff.onlyInB, Colors.orange)),
            ],
          ),
        ),
      ],
    );
  }

  Widget _buildSide(ThemeData theme, String label, List<TimelineEntry> events, Color accent) {
    return Column(
      children: [
        Container(
          padding: const EdgeInsets.all(8),
          color: accent.withValues(alpha: 0.1),
          width: double.infinity,
          child: Text(label,
              style: theme.textTheme.titleSmall?.copyWith(color: accent),
              textAlign: TextAlign.center),
        ),
        Expanded(
          child: events.isEmpty
              ? Center(child: Text('No exclusive events', style: theme.textTheme.bodySmall))
              : ListView.builder(
                  itemCount: events.length,
                  itemBuilder: (context, index) =>
                      TimelineEventCard(entry: events[index]),
                ),
        ),
      ],
    );
  }
}
```

- [ ] **Step 3: Create UniversesScreen**

Create `ino.flutter/lib/screens/universes/universes_screen.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/state/universe_bloc.dart';
import 'package:ino_flutter/ui/components/universe_diff_view.dart';

class UniversesScreen extends StatefulWidget {
  const UniversesScreen({super.key});

  @override
  State<UniversesScreen> createState() => _UniversesScreenState();
}

class _UniversesScreenState extends State<UniversesScreen> {
  @override
  void initState() {
    super.initState();
    context.read<UniverseBloc>().add(UniverseListLoaded());
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return BlocBuilder<UniverseBloc, UniverseBlocState>(
      builder: (context, state) {
        if (state.isLoading && state.universes.isEmpty) {
          return const Center(child: CircularProgressIndicator());
        }

        if (state.diff != null) {
          return Column(
            children: [
              Padding(
                padding: const EdgeInsets.all(8),
                child: Row(
                  children: [
                    IconButton(
                      icon: const Icon(Icons.arrow_back),
                      onPressed: () => context.read<UniverseBloc>().add(UniverseListLoaded()),
                    ),
                    Text('Comparison', style: theme.textTheme.titleMedium),
                  ],
                ),
              ),
              Expanded(
                child: UniverseDiffView(
                  diff: state.diff!,
                  labelA: 'Global Timeline',
                  labelB: state.selectedId ?? 'Universe',
                ),
              ),
            ],
          );
        }

        return Column(
          children: [
            Padding(
              padding: const EdgeInsets.all(12),
              child: Row(
                children: [
                  Text('Parallel Universes', style: theme.textTheme.titleMedium),
                  const Spacer(),
                  FilledButton.icon(
                    icon: const Icon(Icons.call_split),
                    label: const Text('Fork'),
                    onPressed: () => _showForkDialog(context),
                  ),
                ],
              ),
            ),
            if (state.replayResult != null)
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 12),
                child: Card(
                  color: theme.colorScheme.primaryContainer,
                  child: Padding(
                    padding: const EdgeInsets.all(12),
                    child: Text(state.replayResult!),
                  ),
                ),
              ),
            Expanded(
              child: state.universes.isEmpty
                  ? Center(
                      child: Text('No universes yet. Fork from the timeline to create one.',
                          style: theme.textTheme.bodyLarge),
                    )
                  : ListView.builder(
                      itemCount: state.universes.length,
                      itemBuilder: (context, index) {
                        final u = state.universes[index];
                        return Card(
                          margin: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
                          child: ListTile(
                            leading: Icon(
                              u.hasReplayed ? Icons.replay_circle_filled : Icons.circle_outlined,
                              color: u.hasReplayed ? Colors.green : Colors.grey,
                            ),
                            title: Text(u.id),
                            subtitle: Text(
                              'Fork @ seq ${u.forkSequence} · ${u.totalEvents} events · Source: ${u.sourceTimeline}',
                            ),
                            trailing: PopupMenuButton<String>(
                              onSelected: (action) {
                                final bloc = context.read<UniverseBloc>();
                                switch (action) {
                                  case 'replay':
                                    bloc.add(UniverseReplayed(u.id));
                                  case 'compare':
                                    bloc.add(UniverseSelected(u.id));
                                    bloc.add(UniverseCompared('global', u.id));
                                  case 'view':
                                    bloc.add(UniverseSelected(u.id));
                                }
                              },
                              itemBuilder: (_) => [
                                const PopupMenuItem(value: 'replay', child: Text('Replay')),
                                const PopupMenuItem(value: 'compare', child: Text('Compare with global')),
                                const PopupMenuItem(value: 'view', child: Text('View timeline')),
                              ],
                            ),
                          ),
                        );
                      },
                    ),
            ),
          ],
        );
      },
    );
  }

  void _showForkDialog(BuildContext context) {
    final seqController = TextEditingController();
    final sourceController = TextEditingController(text: 'system');
    final kindController = TextEditingController(text: 'SynapseFired');
    final verbController = TextEditingController();

    showDialog(
      context: context,
      builder: (ctx) => AlertDialog(
        title: const Text('Fork Universe'),
        content: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextField(
              controller: seqController,
              decoration: const InputDecoration(labelText: 'Checkpoint sequence'),
              keyboardType: TextInputType.number,
            ),
            TextField(
              controller: kindController,
              decoration: const InputDecoration(labelText: 'Modified event kind'),
            ),
            TextField(
              controller: sourceController,
              decoration: const InputDecoration(labelText: 'Modified event source'),
            ),
            TextField(
              controller: verbController,
              decoration: const InputDecoration(labelText: 'Modified event verb'),
            ),
          ],
        ),
        actions: [
          TextButton(onPressed: () => Navigator.pop(ctx), child: const Text('Cancel')),
          FilledButton(
            onPressed: () {
              final seq = int.tryParse(seqController.text) ?? 0;
              context.read<UniverseBloc>().add(UniverseForked(
                checkpointSequence: seq,
                modifiedEventKind: kindController.text,
                modifiedEventSource: sourceController.text,
                modifiedEventVerb: verbController.text,
              ));
              Navigator.pop(ctx);
            },
            child: const Text('Fork'),
          ),
        ],
      ),
    );
  }
}
```

- [ ] **Step 4: Verify Flutter analysis**

```bash
cd ino.flutter && flutter analyze
```

Expected: No errors.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "feat(flutter): parallel universes screen with fork/replay/compare"
```

---

## Task 8: Flutter — Navigation with Bottom Nav Bar

**Files:**
- Modify: `ino.flutter/lib/app.dart`

- [ ] **Step 1: Replace GoRouter with ShellRoute + bottom nav**

Replace the entire content of `ino.flutter/lib/app.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:ino_flutter/screens/home/home_screen.dart';
import 'package:ino_flutter/screens/onboarding/onboarding_screen.dart';
import 'package:ino_flutter/screens/skills/skills_screen.dart';
import 'package:ino_flutter/screens/timeline/timeline_screen.dart';
import 'package:ino_flutter/screens/time_travel/time_travel_screen.dart';
import 'package:ino_flutter/screens/universes/universes_screen.dart';

final _router = GoRouter(
  initialLocation: '/onboarding',
  routes: [
    GoRoute(
      path: '/onboarding',
      builder: (context, state) => const OnboardingScreen(),
    ),
    ShellRoute(
      builder: (context, state, child) => _Shell(child: child),
      routes: [
        GoRoute(
          path: '/home',
          builder: (context, state) => const HomeScreen(),
        ),
        GoRoute(
          path: '/timeline',
          builder: (context, state) => const TimelineScreen(),
        ),
        GoRoute(
          path: '/timetravel',
          builder: (context, state) => const TimeTravelScreen(),
        ),
        GoRoute(
          path: '/universes',
          builder: (context, state) => const UniversesScreen(),
        ),
        GoRoute(
          path: '/skills',
          builder: (context, state) => const SkillsScreen(),
        ),
      ],
    ),
  ],
);

class _Shell extends StatelessWidget {
  const _Shell({required this.child});
  final Widget child;

  static const _tabs = [
    (path: '/home', icon: Icons.chat, label: 'Chat'),
    (path: '/timeline', icon: Icons.timeline, label: 'Timeline'),
    (path: '/timetravel', icon: Icons.history, label: 'Time Travel'),
    (path: '/universes', icon: Icons.call_split, label: 'Universes'),
    (path: '/skills', icon: Icons.extension, label: 'Skills'),
  ];

  @override
  Widget build(BuildContext context) {
    final location = GoRouterState.of(context).uri.path;
    final currentIndex = _tabs.indexWhere((t) => t.path == location).clamp(0, _tabs.length - 1);

    return Scaffold(
      body: child,
      bottomNavigationBar: NavigationBar(
        selectedIndex: currentIndex,
        onDestinationSelected: (i) => context.go(_tabs[i].path),
        destinations: _tabs
            .map((t) => NavigationDestination(icon: Icon(t.icon), label: t.label))
            .toList(),
      ),
    );
  }
}

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

- [ ] **Step 2: Verify Flutter analysis**

```bash
cd ino.flutter && flutter analyze
```

Expected: No errors.

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "feat(flutter): bottom navigation bar with 5 tabs (chat, timeline, time-travel, universes, skills)"
```

---

## Task 9: Build Flutter Web + Serve from Telegram

**Files:**
- Overwrite: `iaw/Telegram/wwwroot/` (Flutter web build output)
- Modify: `iaw/Telegram/Program.cs` (update redirect)

- [ ] **Step 1: Build Flutter web**

```bash
cd ino.flutter && flutter build web --release
```

Expected: Build succeeds, output in `build/web/`.

- [ ] **Step 2: Replace Telegram wwwroot with Flutter web build**

```bash
rm -rf iaw/Telegram/wwwroot/*
cp -r ino.flutter/build/web/* iaw/Telegram/wwwroot/
```

- [ ] **Step 3: Update Telegram Program.cs redirect**

The Flutter web build has its own `index.html` at the root. Update the redirect in `iaw/Telegram/Program.cs`. The `app.MapGet("/", ...)` line should serve the Flutter app. Since `UseStaticFiles()` serves `index.html` for `/index.html`, but not for bare `/`, keep the redirect:

```csharp
app.MapGet("/", () => Results.Redirect("/index.html"));
```

This already exists and works. No change needed.

- [ ] **Step 4: Build full solution**

```bash
dotnet build ino.slnx
```

Expected: Build succeeds.

- [ ] **Step 5: Start Aspire and verify**

```bash
aspire start
```

Then open the Telegram service URL in a browser. Expected: Flutter web app loads with the 5-tab bottom navigation.

- [ ] **Step 6: Commit**

```bash
git add iaw/Telegram/wwwroot/
git commit -m "feat: serve Flutter web build from Telegram service wwwroot"
```

---

## Task 10: Wire Flutter Windows in Aspire + Final Cleanup

**Files:**
- Verify: `iaw/Aspire/AppHost.cs` (already updated in Task 1)

- [ ] **Step 1: Verify Flutter Windows resource in AppHost**

Confirm `AppHost.cs` has:

```csharp
builder.AddExecutable("ino-flutter-win", "flutter", "../../ino.flutter",
        "run", "-d", "windows")
    .WithExplicitStart();
```

This was added in Task 1.

- [ ] **Step 2: Test Flutter Windows launch from Aspire**

Start Aspire, then start the Flutter Windows resource:

```bash
aspire start
```

Then via MCP or Aspire dashboard, start the Flutter Windows resource:

```
mcp__aspire__execute_resource_command(resourceName="ino-flutter-win", commandName="start")
```

Expected: Flutter Windows desktop app launches with the same 5-tab UI.

- [ ] **Step 3: Verify Telegram mini app**

Open the Telegram bot, tap `/app`. The mini app should load the Flutter web UI inside the Telegram webview.

- [ ] **Step 4: Final verification**

All three surfaces show the Flutter app:
1. Browser at the Telegram service URL → Flutter web
2. Telegram mini app (`/app` command) → Flutter web in webview
3. Flutter Windows from Aspire dashboard → native desktop window

- [ ] **Step 5: Commit any remaining cleanup**

```bash
git add -A
git commit -m "chore: final cleanup — Flutter client unified across all surfaces"
```
