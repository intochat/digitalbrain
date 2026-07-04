# Multi-User Feed Isolation: ClientId Routing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix per-connection feed isolation (P6a) by collapsing the client-facing identity to one `clientId` (already half-built into the login flow) and replacing `HomeFeedBus`'s hand-rolled subscriber dictionary + per-silo relay with direct per-`clientId` Orleans stream subscriptions.

**Architecture:** `WatchHomeFeedRequest` gains `client_id`; every card addressed to one connection is published onto `StreamId.Create("homefeed", clientId)` instead of a shared stream filtered by a `ConcurrentDictionary` loop. `GatewayService.WatchHomeFeed` subscribes directly to that stream plus the existing unaddressed system stream (`StreamId.Create("homefeed", Guid.Empty)`, unchanged). `sessionId` becomes purely internal to `UserSessionNeuron`, resolved on demand via a new `GetSessionByClientIdAsync(clientId)` — the client never sees, stores, or round-trips a real session id again.

**Tech Stack:** C#/.NET, Orleans grains + streaming (`Orleans.Streams`, `IClusterClient.GetStreamProvider`), gRPC (Grpc.Tools proto codegen — editing the `.proto` and running `dotnet build` regenerates the C# type automatically), xUnit + Orleans `TestingHost`, Flutter/Dart (`package:grpc`, hand-maintained generated stubs — no `protoc-gen-dart` confirmed on this machine's `PATH`, see Task 1).

## Global Constraints

- Full design rationale, rejected alternatives, and the approval trail: `docs/superpowers/specs/2026-07-04-multiuser-feed-isolation-design.md` — read it before starting if anything below seems unmotivated.
- **Refinement made during planning, not in the spec:** the spec's §4 described a separate `Address(card, clientId)` method with routing as a call parameter. Researching the actual call sites (`UserSessionNeuron.Broadcast`, `FlutterUiNeuron.HandleAsync`, `KernelTaskNeuron`'s two direct-broadcast sites) showed every producer already goes through a card carrying its own routing field (`RfwCard.SessionId`) into a single `Broadcast` method. Keeping that shape — renaming the field to `ClientId` and changing what `Broadcast` does internally — means **zero call-site changes** at every existing producer, versus rewriting every producer to choose between two methods. This plan uses the simpler shape; the end-to-end behavior (per-`clientId` Orleans streams, no hand-rolled dictionary, no `HomeFeedStreamSubscriber`) is identical to what the spec approved.
- **Scoped out, explicitly not touched by this plan:** `InstallFromMarketplace.SessionId`, `RunTask`/`CancelTask`'s `SessionId` fields (`KernelTaskNeuron`), and `ExperienceUsed.SessionId` keep their current C# field name even though the value flowing through them becomes a `clientId` after Task 4. These are adjacent systems (marketplace, kernel tasks) not in the spec's explicit file list; renaming them is a reasonable future cleanup, not part of this plan. Do not expand scope to cover them.
- Every new/changed synapse or cross-grain record keeps `[GenerateSerializer]`; this plan only renames existing record fields (`RfwCard.SessionId`, `InoRequest.SessionId`, `DbInspectSchema.SessionId`, `DbSchemaInspected.SessionId`) — no new fields are added to any of them, so no new `[Id(n)]` numbering is needed.
- Full solution must build and `dotnet test Brain.slnx` must be 0 failures after every task (existing pre-existing skips aside — confirm the baseline skip count before Task 1 and don't let it grow).
- Mandatory per this repo's own process rule (`docs/CONTINUATION-MULTIUSER-FEED-ISOLATION.md` §0, carried into the spec): the real Flutter app must be driven end-to-end before this is declared done (Task 9) — a green backend suite is exactly what gave false confidence last time.

---

### Task 1: Proto + Orleans-native `HomeFeedBus` (core transport rewrite)

This is the largest task because the proto field, `HomeFeedBus`'s rewrite, and `GatewayService.WatchHomeFeed`'s consumption of it are mutually dependent — none of the three is independently meaningful without the other two.

**Files:**
- Modify: `DigitalBrain.Kernel/Protos/digitalbrain.proto`
- Modify: `DigitalBrain.Ui.Contracts/Ui/RfwCard.cs`
- Modify: `DigitalBrain.Kernel/Ui/HomeFeedBus.cs`
- Delete: `DigitalBrain.Kernel/Ui/HomeFeedStreamSubscriber.cs`
- Modify: `DigitalBrain.Kernel/Program.cs:75-77,209` (comment + registration)
- Modify: `DigitalBrain.TestKit/NeuronTestSiloConfigurator.cs:56` (remove registration)
- Modify: `DigitalBrain.Kernel/Gateway/GatewayService.cs` (`WatchHomeFeed` method only — other branches come in later tasks)
- Modify: `DigitalBrain.Kernel/Ui/UiSurfaceRfwBridge.cs` (prop key rename `"sessionId"` → `"clientId"`)
- Modify: `app/lib/grpc/digitalbrain.pb.dart` (hand-edit generated stub — see Step 1)
- Modify: `DigitalBrain.Tests/Ui/HomeFeedCrossSiloTests.cs` (rewrite both tests)
- Modify: `DigitalBrain.Tests/Gateway/GatewayServiceTests.cs` (fix `_homeFeedBus` construction + every test that calls `.Subscribe()`/constructs an addressed `RfwCard`/`WatchHomeFeedRequest { SessionId = ... }`)

**Interfaces:**
- Produces: `HomeFeedBus(IClusterClient clusterClient, ILogger<HomeFeedBus>? logger = null)` — constructor now requires a real, non-null `IClusterClient` (no more single-silo/test fallback). `void Broadcast(RfwCard card)` — unchanged signature, now always publishes to an Orleans stream keyed by `card.ClientId` (or the unaddressed key when null). `Task<HomeFeedBus.Subscription> SubscribeAsync(string? clientId)` — new, replaces `Subscribe(string? sessionId)`. `HomeFeedBus.Subscription : IAsyncDisposable` with `ChannelReader<RfwCard> Reader`.
- Produces: `RfwCard(string LibraryName, string RootWidget, string DataJson, string? ClientId = null)` — same shape as today's `SessionId`, renamed.
- Consumes (later tasks rely on these): `WatchHomeFeedRequest.ClientId` (proto-generated), `UiSurfaceRfwBridge.FromUiSurface` reading `surface.Props["clientId"]`.

- [ ] **Step 1: Proto field rename + regenerate/hand-edit both language stubs**

Edit `DigitalBrain.Kernel/Protos/digitalbrain.proto`:

```protobuf
message WatchHomeFeedRequest {
  string client_id = 1;
}
```

(This replaces the existing `string session_id = 1;` — it was never populated at connect time by any real client, which was the actual defect; see spec §5.)

Run: `dotnet build DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`
Expected: build succeeds; Grpc.Tools regenerates `WatchHomeFeedRequest` with a `ClientId` property. (Every C# call site that referenced `.SessionId` on this type will now fail to compile — that's expected and fixed later in this same task, not a separate one.)

For the Dart side: `app/lib/grpc/digitalbrain.pb.dart`'s `WatchHomeFeedRequest` class currently has **zero fields defined** (confirmed by reading it — the `session_id` field added in Stage S2 was never regenerated into the Dart stub, since no Flutter code used it). Check whether `protoc` + the Dart plugin can regenerate it cleanly first:

Run: `dart pub global activate protoc_plugin` (idempotent if already activated), then:
```
protoc --dart_out=grpc:app/lib/grpc -IDigitalBrain.Kernel/Protos DigitalBrain.Kernel/Protos/digitalbrain.proto
```
Expected: regenerates `digitalbrain.pb.dart`, `digitalbrain.pbgrpc.dart`, `digitalbrain.pbjson.dart` cleanly, with `WatchHomeFeedRequest` now carrying a `clientId` field alongside every other message already in that combined file (`TranscribeRequest`, `SubmitPromptRequest`, etc. — these are unrelated messages merged into the same generated files from other `.proto` sources; confirm the regenerated file still contains all of them, not just `digitalbrain.proto`'s messages, before committing — if `protoc` only sees `digitalbrain.proto` and the file previously included messages from elsewhere, treat this as a signal to investigate the multi-proto build wiring rather than silently drop those messages).

**If `protoc-gen-dart` is not on `PATH`** (confirmed missing in this shell at plan-writing time despite `protoc_plugin` showing as globally activated — likely a `PATH` issue, not a real unavailability), hand-edit `app/lib/grpc/digitalbrain.pb.dart`'s `WatchHomeFeedRequest` class (lines 98-134) to match the exact generated-code shape of the adjacent `SynapseEnvelope` class (lines 19-96), which already has this precise single-string-field pattern:

```dart
class WatchHomeFeedRequest extends $pb.GeneratedMessage {
  factory WatchHomeFeedRequest({
    $core.String? clientId,
  }) {
    final result = create();
    if (clientId != null) result.clientId = clientId;
    return result;
  }

  WatchHomeFeedRequest._();

  factory WatchHomeFeedRequest.fromBuffer($core.List<$core.int> data,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromBuffer(data, registry);
  factory WatchHomeFeedRequest.fromJson($core.String json,
          [$pb.ExtensionRegistry registry = $pb.ExtensionRegistry.EMPTY]) =>
      create()..mergeFromJson(json, registry);

  static final $pb.BuilderInfo _i = $pb.BuilderInfo(
      _omitMessageNames ? '' : 'WatchHomeFeedRequest',
      package: const $pb.PackageName(_omitMessageNames ? '' : 'digitalbrain'),
      createEmptyInstance: create)
    ..aOS(1, _omitFieldNames ? '' : 'clientId')
    ..hasRequiredFields = false;

  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  WatchHomeFeedRequest clone() => deepCopy();
  @$core.Deprecated('See https://github.com/google/protobuf.dart/issues/998.')
  WatchHomeFeedRequest copyWith(void Function(WatchHomeFeedRequest) updates) =>
      super.copyWith((message) => updates(message as WatchHomeFeedRequest))
          as WatchHomeFeedRequest;

  @$core.override
  $pb.BuilderInfo get info_ => _i;

  @$core.pragma('dart2js:noInline')
  static WatchHomeFeedRequest create() => WatchHomeFeedRequest._();
  @$core.override
  WatchHomeFeedRequest createEmptyInstance() => create();
  @$core.pragma('dart2js:noInline')
  static WatchHomeFeedRequest getDefault() => _defaultInstance ??=
      $pb.GeneratedMessage.$_defaultFor<WatchHomeFeedRequest>(create);
  static WatchHomeFeedRequest? _defaultInstance;

  @$pb.TagNumber(1)
  $core.String get clientId => $_getSZ(0);
  @$pb.TagNumber(1)
  set clientId($core.String value) => $_setString(0, value);
  @$pb.TagNumber(1)
  $core.bool hasClientId() => $_has(0);
  @$pb.TagNumber(1)
  void clearClientId() => $_clearField(1);
}
```

Leave `digitalbrain.pbjson.dart`'s `watchHomeFeedRequestDescriptor` bytes untouched in this fallback path — it already went stale when `session_id` was added in S2 with no reported issues (it's a reflection/debug-JSON artifact, not used by `mergeFromBuffer`/`writeToBuffer`, which is what the wire path actually uses). Note in the commit message which path was used (regenerated vs. hand-edited).

- [ ] **Step 2: Rename `RfwCard.SessionId` → `ClientId`**

Edit `DigitalBrain.Ui.Contracts/Ui/RfwCard.cs:7`:

```csharp
public record RfwCard(string LibraryName, string RootWidget, string DataJson, string? ClientId = null)
    : Synapse(nameof(RfwCard), DateTimeOffset.UtcNow);
```

Run: `dotnet build Brain.slnx`
Expected: fails — every constructor call passing a 4th positional argument still compiles fine (it's positional, name doesn't matter there), but any site using the named property `.SessionId` on an `RfwCard` instance fails. Confirm via the build error list; the only production site is `HomeFeedBus.cs`'s `FanLocal`/`Broadcast` (rewritten in Step 3) and `UiSurfaceRfwBridge.cs` (Step 4). Test sites are fixed in Step 6.

- [ ] **Step 3: Rewrite `HomeFeedBus.cs`**

Replace the entire file:

```csharp
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using DigitalBrain.Core.Ui;
using Orleans.Streams;

namespace DigitalBrain.Kernel.Ui;

// Server-driven-UI backbone. Broadcast publishes an RfwCard onto an Orleans stream keyed by the card's
// ClientId (or a well-known unaddressed key when ClientId is null); Orleans's own pub-sub delivers it to
// exactly the WatchHomeFeed connections subscribed to that key, cluster-wide, regardless of which silo
// published it or which silo the subscriber is attached to. Each WatchHomeFeed call subscribes directly to
// its own client stream plus the shared unaddressed stream via SubscribeAsync — there is no in-process
// subscriber registry and no per-silo relay; Orleans tracks who is listening.
public sealed class HomeFeedBus(IClusterClient clusterClient, ILogger<HomeFeedBus>? logger = null)
{
    private const int MaxSeenEntries = 5_000;
    private static readonly Guid UnaddressedKey = Guid.Empty;
    private readonly HashSet<string> _seen = new();
    private readonly Queue<string> _seenOrder = new();
    private readonly object _seenLock = new();

    public void Broadcast(RfwCard card)
    {
        if (IsDuplicate(card)) return;

        var streamId = card.ClientId is null
            ? StreamId.Create("homefeed", UnaddressedKey)
            : StreamId.Create("homefeed", card.ClientId);

        _ = Task.Run(async () =>
        {
            try
            {
                await clusterClient.GetStreamProvider("HomeFeed").GetStream<RfwCard>(streamId).OnNextAsync(card);
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, "HomeFeed stream publish failed for clientId={ClientId}", card.ClientId);
            }
        });
    }

    // One subscription per WatchHomeFeed gRPC call: the caller's own personal stream (only if it supplied a
    // clientId) plus the shared unaddressed stream every connection receives. DisposeAsync unsubscribes both.
    public async Task<Subscription> SubscribeAsync(string? clientId)
    {
        var channel = Channel.CreateUnbounded<RfwCard>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        var provider = clusterClient.GetStreamProvider("HomeFeed");

        Task OnCard(RfwCard card, StreamSequenceToken _)
        {
            channel.Writer.TryWrite(card);
            return Task.CompletedTask;
        }

        var unaddressedHandle = await provider.GetStream<RfwCard>(StreamId.Create("homefeed", UnaddressedKey)).SubscribeAsync(OnCard);

        StreamSubscriptionHandle<RfwCard>? personalHandle = null;
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            personalHandle = await provider.GetStream<RfwCard>(StreamId.Create("homefeed", clientId)).SubscribeAsync(OnCard);
        }

        return new Subscription(channel, unaddressedHandle, personalHandle);
    }

    private bool IsDuplicate(RfwCard card)
    {
        var key = $"{card.CorrelationId}|{ContentHash(card)}";
        lock (_seenLock)
        {
            if (!_seen.Add(key)) return true;
            _seenOrder.Enqueue(key);
            while (_seenOrder.Count > MaxSeenEntries)
                _seen.Remove(_seenOrder.Dequeue());
            return false;
        }
    }

    private static string ContentHash(RfwCard card) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{card.LibraryName}|{card.RootWidget}|{card.DataJson}")));

    public sealed class Subscription(
        Channel<RfwCard> channel,
        StreamSubscriptionHandle<RfwCard> unaddressedHandle,
        StreamSubscriptionHandle<RfwCard>? personalHandle) : IAsyncDisposable
    {
        public ChannelReader<RfwCard> Reader { get; } = channel.Reader;

        public async ValueTask DisposeAsync()
        {
            await unaddressedHandle.UnsubscribeAsync();
            if (personalHandle is not null)
                await personalHandle.UnsubscribeAsync();
            channel.Writer.TryComplete();
        }
    }
}
```

**Verification note on `StreamId.Create(string, string)`:** Orleans 7+ moved `StreamId` to a string-backed `Namespace`/`Key` shape specifically to make non-GUID stream identities easy to construct (confirmed via Context7 against current Orleans docs, migration-guide page). If `StreamId.Create("homefeed", clientId)` (a `string` second argument) does not resolve when you build, use go-to-definition on `Orleans.Runtime.StreamId` to find the exact string-key overload name (it mirrors `GrainId.Create(string type, string key)`, which does exist and is documented) and use that instead — the shape of the rest of this file does not change either way.

Run: `dotnet build Brain.slnx`
Expected: `HomeFeedBus.cs` compiles. Every consumer of the old `Subscribe(string?)`/`FanLocal` still fails (fixed next).

- [ ] **Step 4: Delete `HomeFeedStreamSubscriber.cs` and its registrations**

Delete the file: `DigitalBrain.Kernel/Ui/HomeFeedStreamSubscriber.cs`

Edit `DigitalBrain.Kernel/Program.cs:74-77`, replace:
```csharp
// Server-driven UI fanout: neurons broadcast RfwCards; WatchHomeFeed gRPC subscribers stream them.
// The per-silo HomeFeedStreamSubscriber (wired into the silo below) re-fans cards from the shared Orleans
// MemoryStream so cards broadcast on any silo reach all replicas.
builder.Services.AddSingleton<HomeFeedBus>();
```
with:
```csharp
// Server-driven UI fanout: neurons broadcast RfwCards; each WatchHomeFeed gRPC call subscribes directly to
// its own per-clientId Orleans stream plus the shared unaddressed stream (see HomeFeedBus.SubscribeAsync) —
// Orleans's own pub-sub delivers cross-silo, no per-silo relay needed.
builder.Services.AddSingleton<HomeFeedBus>();
```

Find and remove the silo-side registration (`siloBuilder.ConfigureServices(services => services.AddHomeFeedStreamSubscriber());` around `Program.cs:209`) — delete that line entirely (do not leave a commented-out line).

Edit `DigitalBrain.TestKit/NeuronTestSiloConfigurator.cs:56`, delete the line:
```csharp
services.AddHomeFeedStreamSubscriber();
```

Run: `dotnet build Brain.slnx`
Expected: fails only on `HomeFeedCrossSiloTests.cs` and `GatewayServiceTests.cs` (fixed in Step 6) — confirm no other reference to `HomeFeedStreamSubscriber` or `AddHomeFeedStreamSubscriber` remains: run `grep -rn "HomeFeedStreamSubscriber" --include=*.cs .` and expect zero hits outside historical docs/plans.

- [ ] **Step 5: `GatewayService.WatchHomeFeed` + `UiSurfaceRfwBridge` prop rename**

Edit `DigitalBrain.Kernel/Gateway/GatewayService.cs`, replace the `WatchHomeFeed` method (currently lines 255-272):

```csharp
    // Server-driven UI: stream RfwCards to the client as neurons broadcast them, until the client disconnects.
    public override async Task WatchHomeFeed(WatchHomeFeedRequest request, IServerStreamWriter<RfwCardEnvelope> responseStream, ServerCallContext context)
    {
        logger.LogInformation("WatchHomeFeed opened for {Peer}", context.Peer);
        var clientId = string.IsNullOrWhiteSpace(request.ClientId) ? null : request.ClientId;

        // The first card a client sees is the login surface — pre-fill it with the dev credentials in
        // Development. clientId rides along on the form's submitAction payload (UiSurfaceRuntime.Login), so
        // the client's own submit button re-sends it with no further Flutter code needed for that leg.
        var initialLogin = DevAuth.Enabled(configuration, environment)
            ? UiSurfaceSamples.Login(clientId: clientId ?? "flutter", defaultUsername: DevAuth.Username, defaultPassword: DevAuth.Password)
            : UiSurfaceSamples.Login(clientId: clientId ?? "flutter");
        await WriteCardAsync(responseStream, UiSurfaceRfwBridge.FromUiSurface(initialLogin, "session-main"));
        logger.LogInformation("WatchHomeFeed sent initial login surface to {Peer}", context.Peer);

        await using var subscription = await homeFeedBus.SubscribeAsync(clientId);
        await foreach (var card in subscription.Reader.ReadAllAsync(context.CancellationToken))
        {
            await WriteCardAsync(responseStream, card);
        }
    }
```

This removes the `ResolveSessionAsync(request.SessionId)` call and the old `homeFeedBus.Subscribe(session?.SessionId)` — resolving a real session is no longer needed just to open the feed; `clientId` alone drives subscription. (`ResolveSessionAsync` itself is renamed and repointed in Task 2 — it's still used by other branches later in this file.)

Edit `DigitalBrain.Kernel/Ui/UiSurfaceRfwBridge.cs` — rename every `"sessionId"` prop-key reference to `"clientId"`:
- Line 43: `surface.Props.TryGetValue("sessionId", ...)` → `surface.Props.TryGetValue("clientId", ...)`, and rename the local `addressedSessionId` to `addressedClientId` throughout the file (lines 43-44, 57, 76, 105).
- Line 68: the marker-key array `new[] { "activeExperience", "experienceId", UiSurfaceKeys.SurfaceId, UiSurfaceKeys.Title, "sessionId", "role", "surfaceKind" }` → replace `"sessionId"` with `"clientId"` in that array, and update the adjacent comment ("sessionId/role let a chat client..." → "clientId/role let a chat client...").

Run: `dotnet build Brain.slnx`
Expected: still fails only on the two test files (Step 6).

- [ ] **Step 6: Rewrite `HomeFeedCrossSiloTests.cs` and fix `GatewayServiceTests.cs`**

Replace `DigitalBrain.Tests/Ui/HomeFeedCrossSiloTests.cs` entirely:

```csharp
using DigitalBrain.Core;
using DigitalBrain.Core.Ui;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;

namespace DigitalBrain.Tests.Ui;

public class HomeFeedCrossSiloTests : NeuronTestBase
{
    protected override short InitialSilosCount => 2;

    [Fact]
    public async Task Broadcast_On_Silo0_Received_On_Silo1_Unaddressed()
    {
        var bus0 = ((InProcessSiloHandle)Cluster.Silos[0]).SiloHost.Services.GetRequiredService<HomeFeedBus>();
        var bus1 = ((InProcessSiloHandle)Cluster.Silos[1]).SiloHost.Services.GetRequiredService<HomeFeedBus>();

        await using var subscription = await bus1.SubscribeAsync(clientId: null);
        var card = new RfwCard("digitalbrain", "CrossSiloCard", "{\"x\":1}");

        bus0.Broadcast(card);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = await subscription.Reader.ReadAsync(cts.Token);
        Assert.Equal("CrossSiloCard", received.RootWidget);
    }

    [Fact]
    public async Task Broadcast_Addressed_To_ClientId_Received_Only_By_That_Clients_Silo()
    {
        var bus0 = ((InProcessSiloHandle)Cluster.Silos[0]).SiloHost.Services.GetRequiredService<HomeFeedBus>();
        var bus1 = ((InProcessSiloHandle)Cluster.Silos[1]).SiloHost.Services.GetRequiredService<HomeFeedBus>();

        // Silo 1 holds the subscriber for "client-a"; silo 0 publishes the card that's addressed to it.
        await using var subscriptionA = await bus1.SubscribeAsync(clientId: "client-a");
        var card = new RfwCard("digitalbrain", "AddressedCrossSiloCard", "{\"x\":2}", ClientId: "client-a");

        bus0.Broadcast(card);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = await subscriptionA.Reader.ReadAsync(cts.Token);
        Assert.Equal("AddressedCrossSiloCard", received.RootWidget);
    }

    [Fact]
    public async Task Broadcast_On_Silo0_Also_Delivered_To_Silo0_Subscriber()
    {
        // In cluster mode Broadcast goes out via the stream and loops back through this silo's own subscriber,
        // so a client connected to the producing silo must still receive the card (no synchronous local fanout).
        var bus0 = ((InProcessSiloHandle)Cluster.Silos[0]).SiloHost.Services.GetRequiredService<HomeFeedBus>();

        await using var subscription = await bus0.SubscribeAsync(clientId: null);
        var card = new RfwCard("digitalbrain", "SelfDeliveryCard", "{\"x\":3}");

        bus0.Broadcast(card);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var received = await subscription.Reader.ReadAsync(cts.Token);
        Assert.Equal("SelfDeliveryCard", received.RootWidget);
    }

    [Fact]
    public async Task Card_Addressed_To_One_ClientId_Never_Reaches_A_Different_Clients_Subscriber()
    {
        var bus0 = ((InProcessSiloHandle)Cluster.Silos[0]).SiloHost.Services.GetRequiredService<HomeFeedBus>();

        await using var subscriptionB = await bus0.SubscribeAsync(clientId: "client-b");
        bus0.Broadcast(new RfwCard("digitalbrain", "AddressedToA", "{}", ClientId: "client-a"));
        bus0.Broadcast(new RfwCard("digitalbrain", "UnaddressedSystemCard", "{}"));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        // Only the unaddressed card should ever arrive on B's subscription — read exactly one message and
        // confirm it's the system card, then confirm nothing else shows up within a bounded window.
        var first = await subscriptionB.Reader.ReadAsync(cts.Token);
        Assert.Equal("UnaddressedSystemCard", first.RootWidget);

        using var drainCts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => subscriptionB.Reader.ReadAsync(drainCts.Token).AsTask());
    }
}
```

Run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~HomeFeedCrossSiloTests`
Expected: 4 passed, 0 failed.

Now fix `DigitalBrain.Tests/Gateway/GatewayServiceTests.cs`. This file does not currently import `Orleans.TestingHost` (it never needed `InProcessSiloHandle` before); add it to the existing `using` block at the top of the file, alongside `using Orleans.Journaling;`:
```csharp
using Orleans.Journaling;
using Orleans.TestingHost;
using DigitalBrain.Kernel.Ui;
```

Replace the field + `ConfigureSilo` + `NewService()` (currently lines 28-52):

```csharp
[Collection("silo-host")]
public class GatewayServiceTests : NeuronTestBase
{
    private HomeFeedBus? _homeFeedBusInstance;
    private readonly FakeMarketDataApiClient _marketClient = new();

    // Lazily resolved via the silo's own DI container (same pattern as HomeFeedCrossSiloTests) — HomeFeedBus
    // now requires a real IClusterClient, which is only available once the cluster has finished starting,
    // i.e. after NeuronTestBase.InitializeAsync runs. A field initializer would run too early.
    private HomeFeedBus HomeFeedBus => _homeFeedBusInstance ??=
        ((InProcessSiloHandle)Cluster.Silos[0]).SiloHost.Services.GetRequiredService<HomeFeedBus>();

    protected override void ConfigureSilo(ISiloBuilder builder) => builder
        .AddMemoryGrainStorageAsDefault()
        .AddMemoryStreams("Default")
        .AddMemoryStreams("HomeFeed")
        .AddMemoryStreams("DigitalBrainTimeline")
        .AddMemoryGrainStorage("PubSubStore")
        .ConfigureServices(services =>
        {
            services.AddKeyedScoped<IDurableList<Synapse>>("in-journal", (_, _) => new InMemoryDurableList<Synapse>());
            services.AddKeyedScoped<IDurableList<Synapse>>("out-journal", (_, _) => new InMemoryDurableList<Synapse>());
            services.AddScoped<NeuronJournals>();
            services.AddSingleton<IJournaledStateManager, TestJournaledStateManager>();
            services.AddSingleton<IPackEmbodiment, PackAlcEmbodier>();
            services.AddSingleton<HomeFeedBus>();
            services.AddSingleton<IMarketDataApiClient>(_marketClient);
        });

    private GatewayService NewService() =>
        new(Cluster.GrainFactory, new ConfigurationBuilder().Build(), HomeFeedBus,
            new SignalEgressBus(),
            new FakeHostEnvironment(),
            NullLogger<GatewayService>.Instance);
```

Update the `ConfigurationProvided_With_Scope_Not_Owned_By_Caller_Is_Rejected` test's service construction (currently line 63) to use the `HomeFeedBus` property instead of the deleted `_homeFeedBus` field:
```csharp
        var svc = new GatewayService(Cluster.GrainFactory, new ConfigurationBuilder().Build(), HomeFeedBus,
            new SignalEgressBus(), new FakeHostEnvironment(), NullLogger<GatewayService>.Instance, packConfigStore);
```

Update `InstallFromMarketplace_Ignores_Client_Supplied_BuyerId_When_Unauthenticated` (currently line 83) and `Send_SurfaceDemoRequested_InstallsPack_And_BroadcastsRenderableSurface` (currently line 410) — both call `_homeFeedBus.Subscribe()` synchronously; change to:
```csharp
        await using var subscription = await HomeFeedBus.SubscribeAsync(clientId: null);
```
(and change the enclosing test method to `async Task` if not already — both already are).

Replace `WatchHomeFeed_Writes_Login_Surface_To_New_Client` (currently lines 128-141) — unchanged in intent, `WatchHomeFeedRequest()` with no fields still means "no clientId":
```csharp
    [Fact]
    public async Task WatchHomeFeed_Writes_Login_Surface_To_New_Client()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var writer = new CapturingServerStreamWriter<RfwCardEnvelope>(() => cts.Cancel());
        var svc = NewService();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            svc.WatchHomeFeed(new WatchHomeFeedRequest(), writer, TestContext(cts.Token)));

        var card = Assert.Single(writer.Messages);
        Assert.Contains("\"kind\":\"login\"", card.DataJson);
        Assert.Contains("\"synapseType\":\"LoginRequest\"", card.DataJson);
    }
```

Replace `WatchHomeFeed_Only_Delivers_Cards_Addressed_To_The_Resolved_Session` (currently lines 143-193) with a clientId-addressed version — this connection now supplies its own `clientId` up front (rather than a server-resolved `sessionId` after the fact), matching how the real client will behave from Task 8 onward:

```csharp
    [Fact]
    public async Task WatchHomeFeed_Only_Delivers_Cards_Addressed_To_This_Connections_ClientId()
    {
        var svc = NewService();
        const string myClientId = "feed-isolation-client";

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var writer = new CapturingServerStreamWriter<RfwCardEnvelope>();
        var watchTask = svc.WatchHomeFeed(new WatchHomeFeedRequest { ClientId = myClientId }, writer, TestContext(cts.Token));

        for (var attempt = 0; attempt < 40 && writer.Messages.Count == 0; attempt++)
            await Task.Delay(25);

        // The line above only proves the (synchronously-written) initial login card arrived — it says nothing
        // about whether HomeFeedBus.SubscribeAsync's Orleans subscriptions have actually landed yet, which is
        // genuinely asynchronous. Broadcasting before that lands would be silently missed, so give the
        // subscribe round-trip a bounded, generous window before addressing cards.
        for (var attempt = 0; attempt < 40 && writer.Messages.Count < 2; attempt++)
        {
            // DataJson varies per attempt because HomeFeedBus content-hash-dedups identical cards at the point
            // of Broadcast (before any subscriber even sees them) — an unvarying probe would only ever land once.
            HomeFeedBus.Broadcast(new RfwCard("digitalbrain", "ReadinessProbe", $"{{\"attempt\":{attempt}}}", myClientId));
            await Task.Delay(25);
        }

        HomeFeedBus.Broadcast(new RfwCard("digitalbrain", "AddressedToMe", "{}", myClientId));
        HomeFeedBus.Broadcast(new RfwCard("digitalbrain", "AddressedToSomeoneElse", "{}", "someone-elses-client-id"));
        HomeFeedBus.Broadcast(new RfwCard("digitalbrain", "Unaddressed", "{}"));

        await Task.Delay(300);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watchTask);

        Assert.Contains(writer.Messages, m => m.RootWidget == "AddressedToMe");
        Assert.Contains(writer.Messages, m => m.RootWidget == "Unaddressed");
        Assert.DoesNotContain(writer.Messages, m => m.RootWidget == "AddressedToSomeoneElse");
    }
```

**Delete** `WatchHomeFeed_Unauthenticated_Receives_Every_Card_Fail_Open` (currently lines 195-219) entirely — it asserted the temporary fail-open shim's behavior, which no longer exists once addressing works by construction (an unaddressed connection was never subscribed to anyone else's personal stream in the first place, so there's no "fail open" state to test — replace it with the opposite assertion):

```csharp
    [Fact]
    public async Task WatchHomeFeed_Without_A_ClientId_Never_Receives_Cards_Addressed_To_Someone_Else()
    {
        var svc = NewService();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var writer = new CapturingServerStreamWriter<RfwCardEnvelope>();
        var watchTask = svc.WatchHomeFeed(new WatchHomeFeedRequest(), writer, TestContext(cts.Token));

        for (var attempt = 0; attempt < 40 && writer.Messages.Count == 0; attempt++)
            await Task.Delay(25);

        HomeFeedBus.Broadcast(new RfwCard("digitalbrain", "AddressedToSomeone", "{}", "some-real-client-id"));
        HomeFeedBus.Broadcast(new RfwCard("digitalbrain", "SystemUnaddressed", "{}"));

        await Task.Delay(300);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watchTask);

        Assert.Contains(writer.Messages, m => m.RootWidget == "SystemUnaddressed");
        Assert.DoesNotContain(writer.Messages, m => m.RootWidget == "AddressedToSomeone");
    }
```

Every other reference to `_homeFeedBus` remaining in this file (there should be none left after the above — `grep -n "_homeFeedBus" DigitalBrain.Tests/Gateway/GatewayServiceTests.cs` to confirm) must be replaced with the `HomeFeedBus` property. Do not fix the `sessionId`-payload tests further down in this file yet (`Send_InoRequest_...`, `Send_SalesforceAuthRequested_...`, etc.) — those are Tasks 4-6's responsibility; leave them exactly as they are for now even though some will still reference `sessionId` payload fields (they'll keep compiling against `GatewayService.Send`'s current, not-yet-changed branches).

Run: `dotnet build Brain.slnx`
Expected: builds clean.

Run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~GatewayServiceTests`
Expected: all pass except none should newly fail — this task only touched `WatchHomeFeed`-related tests and the `HomeFeedBus` construction/subscribe calls; every other test in this file is unaffected because `GatewayService.Send`'s other branches aren't touched until later tasks.

- [ ] **Step 7: Commit**

```bash
git add DigitalBrain.Kernel/Protos/digitalbrain.proto DigitalBrain.Ui.Contracts/Ui/RfwCard.cs DigitalBrain.Kernel/Ui/HomeFeedBus.cs DigitalBrain.Kernel/Ui/UiSurfaceRfwBridge.cs DigitalBrain.Kernel/Program.cs DigitalBrain.TestKit/NeuronTestSiloConfigurator.cs DigitalBrain.Kernel/Gateway/GatewayService.cs app/lib/grpc/digitalbrain.pb.dart DigitalBrain.Tests/Ui/HomeFeedCrossSiloTests.cs DigitalBrain.Tests/Gateway/GatewayServiceTests.cs
git rm DigitalBrain.Kernel/Ui/HomeFeedStreamSubscriber.cs
git commit -m "feat(feed): replace HomeFeedBus's hand-rolled subscriber dictionary with per-clientId Orleans streams

WatchHomeFeedRequest gains client_id (replacing the never-populated session_id).
HomeFeedBus.Broadcast publishes to StreamId.Create(\"homefeed\", card.ClientId ?? Guid.Empty)
instead of fanning through a ConcurrentDictionary; HomeFeedStreamSubscriber's per-silo relay
is deleted entirely since every WatchHomeFeed call now subscribes directly to its own stream."
```

---

### Task 2: `IUserSessionNeuron.GetSessionByClientIdAsync`

Pure addition — does not change any existing behavior. Safe to land independently.

**Files:**
- Modify: `DigitalBrain.Ui.Contracts/UiNeuronContracts.cs`
- Modify: `DigitalBrain.Kernel/Auth/UserSessionNeuron.cs`
- Test: `DigitalBrain.Tests/Auth/UserSessionNeuronClientIdTests.cs` (new file)

**Interfaces:**
- Produces: `Task<UserSessionState?> GetSessionByClientIdAsync(string clientId)` on `IUserSessionNeuron` — resolves the latest active (non-expired, non-ended) session created for that `clientId`, or `null` if none. Consumed by `GatewayService` from Task 4 onward.

- [ ] **Step 1: Write the failing test**

Create `DigitalBrain.Tests/Auth/UserSessionNeuronClientIdTests.cs`:

```csharp
using DigitalBrain.Core;
using DigitalBrain.TestKit;

namespace DigitalBrain.Tests.Auth;

public class UserSessionNeuronClientIdTests : NeuronTestBase
{
    [Fact]
    public async Task GetSessionByClientIdAsync_Returns_The_Session_Created_For_That_ClientId()
    {
        var session = Grain<IUserSessionNeuron>("session-main");
        await session.HandleAsync(new LoginRequest("clientid-user", "correct horse battery staple", "my-connection"));

        var resolved = await session.GetSessionByClientIdAsync("my-connection");

        Assert.NotNull(resolved);
        Assert.Equal("clientid-user", resolved!.UserId.Value);
    }

    [Fact]
    public async Task GetSessionByClientIdAsync_Returns_Null_For_An_Unknown_ClientId()
    {
        var session = Grain<IUserSessionNeuron>("session-main");
        var resolved = await session.GetSessionByClientIdAsync("never-logged-in");
        Assert.Null(resolved);
    }

    [Fact]
    public async Task GetSessionByClientIdAsync_Returns_Null_After_That_ClientIds_Session_Logged_Out()
    {
        var session = Grain<IUserSessionNeuron>("session-main");
        await session.HandleAsync(new LoginRequest("clientid-logout-user", "correct horse battery staple", "logout-connection"));
        var beforeLogout = await session.GetSessionByClientIdAsync("logout-connection");
        Assert.NotNull(beforeLogout);

        await session.HandleAsync(new LogoutRequest(beforeLogout!.SessionId, "logout-connection"));

        var afterLogout = await session.GetSessionByClientIdAsync("logout-connection");
        Assert.Null(afterLogout);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~UserSessionNeuronClientIdTests`
Expected: FAIL — `GetSessionByClientIdAsync` does not exist on `IUserSessionNeuron` (compile error).

- [ ] **Step 3: Add the interface method**

Edit `DigitalBrain.Ui.Contracts/UiNeuronContracts.cs:3-7`:

```csharp
public interface IUserSessionNeuron : INeuron, IHandle<LoginRequest>, IHandle<LogoutRequest>
{
    Task<UserSessionState?> GetSessionAsync(string sessionId);
    Task<UserSessionState?> GetSessionByClientIdAsync(string clientId);
    Task<UiSurface> BuildLoginSurfaceAsync(string? clientId = null);
}
```

- [ ] **Step 4: Implement it in `UserSessionNeuron`**

Edit `DigitalBrain.Kernel/Auth/UserSessionNeuron.cs` — add this method right after the existing `GetSessionAsync` (after line 130):

```csharp
    public Task<UserSessionState?> GetSessionByClientIdAsync(string clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return Task.FromResult<UserSessionState?>(null);
        }

        var ended = OutgoingJournal
            .Concat(IncomingJournal)
            .OfType<UserSessionEnded>()
            .Select(e => e.SessionId)
            .ToHashSet(StringComparer.Ordinal);

        var created = OutgoingJournal
            .Concat(IncomingJournal)
            .OfType<UserSessionCreated>()
            .DistinctBy(s => s.SynapseId)
            .Where(s => string.Equals(s.ClientId, clientId, StringComparison.Ordinal))
            .Where(s => s.ExpiresAt > DateTimeOffset.UtcNow && !ended.Contains(s.SessionId))
            .OrderBy(s => s.ExpiresAt)
            .LastOrDefault();

        if (created is null)
        {
            return Task.FromResult<UserSessionState?>(null);
        }

        var login = OutgoingJournal
            .Concat(IncomingJournal)
            .OfType<LoginSucceeded>()
            .DistinctBy(s => s.SynapseId)
            .LastOrDefault(s => string.Equals(s.SessionId, created.SessionId, StringComparison.Ordinal));

        return Task.FromResult<UserSessionState?>(new UserSessionState(
            created.UserId,
            created.SessionId,
            login?.DisplayName ?? created.UserId.Value,
            login?.Roles ?? Array.Empty<string>(),
            created.ExpiresAt,
            Active: true));
    }
```

This mirrors `GetSessionAsync`'s exact shape, filtering by `ClientId` instead of `SessionId` — same journal, no new synapse types, no new storage.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~UserSessionNeuronClientIdTests`
Expected: PASS (3/3).

- [ ] **Step 6: Commit**

```bash
git add DigitalBrain.Ui.Contracts/UiNeuronContracts.cs DigitalBrain.Kernel/Auth/UserSessionNeuron.cs DigitalBrain.Tests/Auth/UserSessionNeuronClientIdTests.cs
git commit -m "feat(auth): add GetSessionByClientIdAsync, resolving a session from its login clientId

Backed by data UserSessionCreated already journals (ClientId has been recorded
alongside SessionId since S2/S3) — no new synapse types, same LINQ-over-journal
shape as the existing GetSessionAsync."
```

---

### Task 3: Thread the real connecting `clientId` through login broadcasts

Fixes the actual bug: the login form's `clientId` is already correct end-to-end (Task 1 wired `WatchHomeFeed` to pass the connection's real `clientId` into the login surface), but everything `UserSessionNeuron` broadcasts *after* a successful login (shell surface, marketplace lists, task manager) still stamps the client's `sessionId` — a value the client never learns — instead of `clientId`, which the client already has.

**Files:**
- Modify: `DigitalBrain.Kernel/Auth/UserSessionNeuron.cs`
- Modify: `DigitalBrain.Marketplace.Contracts/MarketplaceUiSurfaces.cs`
- Modify: `DigitalBrain.Ui.Runtime/UiSurfaceRuntime.cs` (`TaskManagerFromTasks`)
- Modify: `DigitalBrain.Tests/UiSurfaceContractTests.cs`

**Interfaces:**
- Consumes: `HomeFeedBus.Broadcast(RfwCard)` (Task 1), `RfwCard.ClientId` (Task 1).
- Produces: `MarketplaceUiSurfaces.InstalledBundlesFromPacks`/`MarketplaceListFromPacks` and `UiSurfaceLiveData.TaskManagerFromTasks` (aliased from `UiSurfaceRuntime`) now take a `clientId` parameter (renamed from `sessionId`) and stamp `Props["clientId"]` instead of `Props["sessionId"]`.

- [ ] **Step 1: Write the failing test**

Edit `DigitalBrain.Tests/UiSurfaceContractTests.cs` — the existing test at line ~190-213 currently calls `UiSurfaceLiveData.TaskManagerFromTasks(..., sessionId: "session-1")` and asserts `Props["sessionId"]`. Change it to:

```csharp
        var surface = UiSurfaceLiveData.TaskManagerFromTasks(
            new Synapse[] { new TaskCreated(taskId, "Summarize latest mail") },
            userId: "alice",
            clientId: "client-1");

        Assert.Equal("alice", surface.Props["userId"]);
        Assert.Equal("client-1", surface.Props["clientId"]);

        var runProps = AssertActionProps(surface.Props["runAction"], nameof(RunTask));
        Assert.Equal("alice", runProps["userId"]);
        Assert.Equal("client-1", runProps["sessionId"]);
```

(`runProps["sessionId"]` on the `RunTask` action payload is left as `sessionId` deliberately — `RunTask`/`CancelTask`'s `SessionId` field is explicitly out of scope for this plan, per the Global Constraints note. Only the surface-level addressing prop, which `HomeFeedBus` actually reads for routing, changes name.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~UiSurfaceContractTests`
Expected: FAIL — `TaskManagerFromTasks` has no `clientId` parameter yet.

- [ ] **Step 3: Rename the parameter in `UiSurfaceRuntime.TaskManagerFromTasks`**

Edit `DigitalBrain.Ui.Runtime/UiSurfaceRuntime.cs:509-513`, change the signature:

```csharp
    public static UiSurface TaskManagerFromTasks(
        IReadOnlyList<Synapse> taskEvents,
        int maxEvents = 10,
        string userId = "anonymous",
        string? clientId = null)
```

Every use of `sessionId` inside that method body (constructing `Props["sessionId"]` and the per-row `["sessionId"]` entries) renames to `clientId`/`Props["clientId"]`. Read the full method body (`UiSurfaceRuntime.cs:509` through its closing brace) before editing — every `sessionId` local variable inside it becomes `clientId`; every `["sessionId"]` **surface-level prop key** becomes `["clientId"]`. Leave the `runAction`/`cancelAction`'s own `["sessionId"]` entry (the `RunTask`/`CancelTask` payload) untouched — same out-of-scope reasoning as Step 1.

- [ ] **Step 4: Rename the parameter in `MarketplaceUiSurfaces`**

Edit `DigitalBrain.Marketplace.Contracts/MarketplaceUiSurfaces.cs`:
- `MarketplaceListFromPacks` (line 7-11): rename `string? sessionId = null` → `string? clientId = null`.
- `InstalledBundlesFromPacks` (line 137-141): same rename.
- Every internal reference to `sessionId` in both methods (including the `BundleRow(group.First(), userId, sessionId)` call at line 153, and the `MarketplaceListFromPacks(published, installed, userId, sessionId)` call at line 80) renames to `clientId`.
- Wherever these methods stamp a surface/row prop with `["sessionId"]`, rename to `["clientId"]`.

- [ ] **Step 5: Thread the real `clientId` through `UserSessionNeuron`**

Edit `DigitalBrain.Kernel/Auth/UserSessionNeuron.cs`:

`HandleAsync(LoginRequest request)` (lines 24-77) already has `clientId` in scope — change line 76's call:
```csharp
        await BroadcastProductHomeAsync(user, sessionId, clientId);
```

`BroadcastProductHomeAsync` (lines 148-165) gains the parameter and stops passing `sessionId` for addressing:
```csharp
    private async Task BroadcastProductHomeAsync(LocalUserRegistered user, string sessionId, string clientId)
    {
        var userId = user.UserId.Value;
        var taskEvents = OutgoingJournal.Concat(IncomingJournal).ToList();
        var surfaces = new[]
        {
            BuildSignedInShellSurface(user, sessionId, clientId),
            MarketplaceUiSurfaces.InstalledBundlesFromPacks(MarketplaceSeeds.LocalUiPacks, Array.Empty<NeuroPack>(), userId, clientId),
            MarketplaceUiSurfaces.MarketplaceListFromPacks(MarketplaceSeeds.LocalUiPacks, Array.Empty<NeuroPack>(), userId, clientId),
            UiSurfaceLiveData.TaskManagerFromTasks(taskEvents, userId: userId, clientId: clientId)
        };

        foreach (var surface in surfaces)
        {
            await FireAsync(surface);
            Broadcast(surface);
        }
    }
```

`BuildSignedInShellSurface` (lines 167-223) gains the `clientId` parameter, keeps `sessionId` only where it's genuinely content (the logout button's `["sessionId"] = sessionId` stays — logout is one of the few places that legitimately needs the real session id, since `GatewayService`'s `LogoutRequest` branch resolves identity itself in Task 4; but the surface's own routing/addressing prop switches to `clientId`):

```csharp
    private UiSurface BuildSignedInShellSurface(LocalUserRegistered user, string sessionId, string clientId)
    {
        var menuItems = new[]
        {
            MenuItem("Installed", UiSurfaceKinds.InstalledBundles),
            MenuItem("Marketplace", UiSurfaceKinds.MarketplaceList),
            MenuItem("Tasks", UiSurfaceKinds.TaskManager),
            MenuItem("INO Chat", "chat"),
            new UiWidgetTree(NeuronUiKit.Divider, new Dictionary<string, object?>()),
            new UiWidgetTree(NeuronUiKit.MenuItem, new Dictionary<string, object?>
            {
                ["label"] = "Sign Out",
                ["action"] = UiSurfaceSamples.SynapseAction(
                    "logout",
                    "Sign Out",
                    nameof(LogoutRequest),
                    new Dictionary<string, object?>
                    {
                        ["clientId"] = clientId
                    })
            })
        };

        var tree = new UiWidgetTree(
            NeuronUiKit.Scaffold,
            new Dictionary<string, object?>
            {
                ["title"] = "DigitalBrain",
                ["activeContent"] = UiSurfaceKinds.InstalledBundles,
                ["userId"] = user.UserId.Value,
                ["clientId"] = clientId
            },
            new List<UiWidgetTree>
            {
                NeuronUiKit.BuildHeader("DigitalBrain", user.DisplayName),
                new(NeuronUiKit.Sidebar, new Dictionary<string, object?> { ["title"] = user.DisplayName }, menuItems),
                new("content", new Dictionary<string, object?>
                {
                    ["defaultView"] = UiSurfaceKinds.InstalledBundles
                })
            });

        return new UiSurface(UiSurface.WidgetTreeKind, new Dictionary<string, object?>
        {
            ["tree"] = tree,
            [UiSurfaceKeys.SurfaceId] = "surface.shell." + user.UserId.Value,
            [UiSurfaceKeys.Emitter] = Self.Value,
            [UiSurfaceKeys.Title] = "DigitalBrain",
            [UiSurfaceKeys.Priority] = 100,
            [UiSurfaceKeys.RequiresInput] = false,
            [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Panel,
            ["userId"] = user.UserId.Value,
            ["displayName"] = user.DisplayName,
            ["clientId"] = clientId
        });
    }
```

Note: the logout action's payload dropped `["sessionId"] = sessionId` entirely — Task 4 changes `GatewayService`'s `LogoutRequest` branch to resolve the real session server-side from `clientId` via `GetSessionByClientIdAsync`, so the client no longer needs to carry or resend a raw session id for logout either.

`BroadcastSignedIn` (lines 238-256) — stays keyed by `clientId` for `SurfaceId` (unchanged, it already was), but its addressing prop also switches:
```csharp
    private void BroadcastSignedIn(LocalUserRegistered user, string sessionId, string clientId)
    {
        var surface = new UiSurface("session-status", new Dictionary<string, object?>
        {
            [UiSurfaceKeys.SurfaceId] = "surface.session." + clientId,
            [UiSurfaceKeys.Emitter] = Self.Value,
            [UiSurfaceKeys.Title] = "Signed In",
            [UiSurfaceKeys.Priority] = 90,
            [UiSurfaceKeys.RequiresInput] = false,
            [UiSurfaceKeys.Layout] = UiSurfaceLayouts.Compact,
            ["userId"] = user.UserId.Value,
            ["displayName"] = user.DisplayName,
            ["clientId"] = clientId,
            ["status"] = "signed-in",
            ["body"] = $"Signed in as {user.DisplayName}"
        });

        Broadcast(surface);
    }
```
(`HandleAsync(LoginRequest)`'s existing call `BroadcastSignedIn(user, sessionId, clientId)` at line 72 is unchanged — it already passes both.)

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~UiSurfaceContractTests`
Expected: PASS.

Run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~UserSessionNeuron`
Expected: PASS — 0 failures (existing `UserSessionNeuron` tests don't assert on `Props["sessionId"]` for the shell/marketplace surfaces; if any do, update them the same way as Step 1 above).

Run: `dotnet build Brain.slnx`
Expected: clean — this confirms no other file references the old `sessionId`-named parameters on `MarketplaceUiSurfaces`/`UiSurfaceLiveData.TaskManagerFromTasks` besides `KernelTaskNeuron.cs` (which calls `UiSurfaceLiveData.TaskManagerFromTasks(recent, userId: cmd.UserId, sessionId: cmd.SessionId)` at two call sites, `KernelTaskNeuron.cs:38,58`). Update both to `clientId: cmd.SessionId` (yes — `cmd.SessionId` on the `RunTask`/`CancelTask` command, which stays named `SessionId` per the Global Constraints scoping note, gets passed into the renamed `clientId:` parameter; the record field name and the parameter name legitimately differ here, which is exactly the boundary this plan draws around scope).

- [ ] **Step 7: Commit**

```bash
git add DigitalBrain.Kernel/Auth/UserSessionNeuron.cs DigitalBrain.Marketplace.Contracts/MarketplaceUiSurfaces.cs DigitalBrain.Ui.Runtime/UiSurfaceRuntime.cs DigitalBrain.Kernel/KernelTaskNeuron.cs DigitalBrain.Tests/UiSurfaceContractTests.cs
git commit -m "fix(auth): address post-login shell/marketplace/task-manager surfaces by clientId, not sessionId

The client never learns a real sessionId, but it already knows its own clientId from the
moment it opens WatchHomeFeed — addressing by clientId means these surfaces actually reach
the connection that logged in, instead of being silently dropped once real addressing is
enforced (Task 1)."
```

---

### Task 4: `GatewayService.Send` — `InstallFromMarketplace`, `ConfigurationProvided`, `LogoutRequest` read `clientId`

**Files:**
- Modify: `DigitalBrain.Kernel/Gateway/GatewayService.cs`
- Modify: `DigitalBrain.Tests/Gateway/GatewayServiceTests.cs`

**Interfaces:**
- Consumes: `IUserSessionNeuron.GetSessionByClientIdAsync` (Task 2).
- Produces: `GatewayService`'s private `ResolveSessionAsync(string?)` renamed to `ResolveSessionByClientIdAsync(string?)` — consumed by Tasks 5-6 too.

- [ ] **Step 1: Write the failing test**

Add to `DigitalBrain.Tests/Gateway/GatewayServiceTests.cs`, near `InstallFromMarketplace_Ignores_Client_Supplied_BuyerId_When_Unauthenticated`:

```csharp
    [Fact]
    public async Task InstallFromMarketplace_Resolves_Real_Buyer_From_ClientId_After_Login()
    {
        var svc = NewService();

        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(LoginRequest),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                username = "install-clientid-user",
                password = "correct horse battery staple",
                clientId = "install-connection"
            }))
        }, TestContext());

        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(InstallFromMarketplace),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                packName = "nonexistent-pack-2",
                version = "1.0",
                clientId = "install-connection"
            }))
        }, TestContext());

        var market = Grain<IMarketplaceNeuron>("market-main");
        var timeline = await market.GetOutgoingTimelineAsync();
        var install = Assert.Single(timeline.OfType<InstallFromMarketplace>(), i => i.PackName == "nonexistent-pack-2");
        Assert.Equal("install-clientid-user", install.BuyerId);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~InstallFromMarketplace_Resolves_Real_Buyer_From_ClientId_After_Login`
Expected: FAIL — `GatewayService.Send`'s `InstallFromMarketplace` branch still reads `"sessionId"` off the payload, which this test never sends, so `BuyerId` resolves to `"anonymous"`, not the real user.

- [ ] **Step 3: Rename `ResolveSessionAsync` and update the three branches**

Edit `DigitalBrain.Kernel/Gateway/GatewayService.cs` — rename the private helper (currently lines 274-280):

```csharp
    // Resolves a client-supplied clientId once; callers must use the result's fields downstream, never trust
    // a raw client-supplied userId/sessionId directly.
    private async Task<UserSessionState?> ResolveSessionByClientIdAsync(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return null;
        var session = grains.GetGrain<IUserSessionNeuron>("session-main");
        return await session.GetSessionByClientIdAsync(clientId);
    }
```

`InstallFromMarketplace` branch (currently lines 56-70):
```csharp
            if (request.TypeName == nameof(InstallFromMarketplace) || request.TypeName.Contains("InstallFromMarketplace", StringComparison.OrdinalIgnoreCase))
            {
                var market = grains.GetGrain<IMarketplaceNeuron>("market-main");
                var payloadStr = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
                var p = CaseInsensitive(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadStr));
                var packName = p.TryGetValue("packName", out var pn) ? pn?.ToString() ?? p.GetValueOrDefault("name")?.ToString() ?? "" : "";
                var ver = p.TryGetValue("version", out var v) ? v?.ToString() ?? "" : "";
                var clientId = p.TryGetValue("clientId", out var cid) ? cid?.ToString() : null;
                var installSession = await ResolveSessionByClientIdAsync(clientId);
                var buyer = installSession?.UserId.Value ?? "anonymous";
                if (string.IsNullOrWhiteSpace(packName)) packName = request.CorrelationId; // fallback
                await market.FireAsync(new InstallFromMarketplace(packName, ver, buyer, clientId));
                return request;
            }
```
(`InstallFromMarketplace`'s own `SessionId` field keeps its C# name per the Global Constraints scoping note — only the JSON payload key being read, and the local variable, rename.)

`ConfigurationProvided` branch (currently lines 115-158) — every `Field("sessionId")`/`configSession` reference:
```csharp
                var configSession = await ResolveSessionByClientIdAsync(Field("clientId"));
                var callerOwnScope = configSession is not null ? PackConfigScopes.ForUser(configSession.UserId) : null;
                if (scope != PackConfigScopes.App && scope != callerOwnScope)
                    throw new RpcException(new Status(StatusCode.PermissionDenied, $"Scope '{scope}' is not permitted for this caller."));

                var controlKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "pack", "packName", "scope", "clientId", "buyerId", "userId", "synapseType", "eventName"
                };
```
(replace `"sessionId"` with `"clientId"` in the `controlKeys` set too.)

`LogoutRequest` branch (currently lines 189-198):
```csharp
            if (request.TypeName == nameof(LogoutRequest) || request.TypeName.Contains("LogoutRequest", StringComparison.OrdinalIgnoreCase))
            {
                var session = grains.GetGrain<IUserSessionNeuron>("session-main");
                var payloadStr = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
                var p = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadStr) ?? new();
                var clientId = p.TryGetValue("clientId", out var cid) ? cid?.ToString() ?? "grpc" : "grpc";
                var logoutSession = await ResolveSessionByClientIdAsync(clientId);
                await session.FireAsync(new LogoutRequest(logoutSession?.SessionId ?? "", clientId));
                return request;
            }
```
(This resolves the real `sessionId` server-side now, instead of trusting whatever the client happened to send — the shell surface's logout button, per Task 3, no longer even sends a `sessionId` field.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~GatewayServiceTests`
Expected: `InstallFromMarketplace_Resolves_Real_Buyer_From_ClientId_After_Login` passes; `InstallFromMarketplace_Ignores_Client_Supplied_BuyerId_When_Unauthenticated` still passes unchanged (it never logs in, so `clientId` resolves to no session, buyer stays `"anonymous"`); `ConfigurationProvided_With_Scope_Not_Owned_By_Caller_Is_Rejected` still passes (it never supplies a `clientId` either, so `callerOwnScope` is `null` and the foreign `"user:someone-else"` scope is still rejected the same way).

Run: `dotnet build Brain.slnx`
Expected: clean.

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Kernel/Gateway/GatewayService.cs DigitalBrain.Tests/Gateway/GatewayServiceTests.cs
git commit -m "fix(gateway): resolve real identity from clientId for install/config/logout, not a raw sessionId

ResolveSessionAsync renamed to ResolveSessionByClientIdAsync; InstallFromMarketplace,
ConfigurationProvided, and LogoutRequest now read clientId off the payload and resolve
the caller's real session server-side instead of trusting a client-supplied sessionId."
```

---

### Task 5: Fix the still-live Salesforce-via-chat identity bug (`InoRequest`/`InoNeuron`)

This is the bug the spec's §1 flagged as *not* fixed by the existing fail-open shim: `InoNeuron.ResolveUserIdAsync` tries to resolve the chat correlation token as a real session and always fails. Renaming `InoRequest.SessionId` → `ClientId` (and the two schema-inspection synapses it threads through) makes the actual value flowing through match what the name says, and lets `ResolveUserIdAsync` do real resolution.

**Files:**
- Modify: `DigitalBrain.Core/Synapse.cs` (`InoRequest`, `DbInspectSchema`, `DbSchemaInspected` record declarations)
- Modify: `DigitalBrain.Kernel/Ino/InoNeuron.cs`
- Modify: `DigitalBrain.Kernel/DbSupportNeuron.cs`
- Modify: `DigitalBrain.Kernel/Db/SqliteSchemaInspector.cs`
- Modify: `DigitalBrain.Core/TabularDataSynapses.cs`
- Modify: `DigitalBrain.Kernel/Program.cs` (`/upload` endpoint only)
- Modify: `DigitalBrain.Kernel/Uploads/ChatUploadClassifier.cs`
- Modify: `DigitalBrain.Kernel/DataVisualizationNeuron.cs` (prop rename only)
- Modify: `DigitalBrain.Kernel/Gateway/GatewayService.cs` (`InoRequest` branch)
- Modify: `DigitalBrain.Tests/Gateway/GatewayServiceTests.cs`, `DigitalBrain.Tests/Ino/*.cs`, `DigitalBrain.Tests/Uploads/ChatUploadClassifierTests.cs`

**Interfaces:**
- Consumes: `GatewayService.ResolveSessionByClientIdAsync` (Task 4).
- Produces: `InoRequest(string Prompt, string? ClientId = null)`, `DbInspectSchema(..., string? ClientId = null)`, `DbSchemaInspected(..., string? ClientId = null)`, `TabularDataIngested(..., string? ClientId = null)`.

- [ ] **Step 1: Write the failing test**

Add to `DigitalBrain.Tests/Gateway/GatewayServiceTests.cs`, replacing nothing yet (new test, additive):

```csharp
    [Fact]
    public async Task Send_SalesforceAuthRequested_Via_InoRequest_Resolves_Real_User_Not_Anonymous()
    {
        var svc = NewService();

        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(LoginRequest),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                username = "salesforce-via-chat-user",
                password = "correct horse battery staple",
                clientId = "chat-connection-1"
            }))
        }, TestContext());

        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(InoRequest),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                prompt = "check my salesforce accounts",
                clientId = "chat-connection-1"
            }))
        }, TestContext());

        var auth = Grain<ISalesforceAuthNeuron>("salesforce-via-chat-user");
        var timeline = await auth.GetOutgoingTimelineAsync();
        Assert.Contains(timeline.OfType<UiSurface>(), surface => surface.Kind == ConfigFormSurface.Kind);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~Send_SalesforceAuthRequested_Via_InoRequest_Resolves_Real_User_Not_Anonymous`
Expected: FAIL — today, `InoRequest`'s `clientId` payload field isn't read at all (the branch reads `"sessionId"`, and even if it did, `InoNeuron.ResolveUserIdAsync` would try to resolve it via `GetSessionAsync` against the *chat correlation token* value, not a real session — it resolves to `UserId.Anonymous`, and the credential form is delivered to the `"anonymous"` grain, not `"salesforce-via-chat-user"`).

- [ ] **Step 3: Rename the three Synapse record fields**

Edit `DigitalBrain.Core/Synapse.cs:226`:
```csharp
public record InoRequest(string Prompt, string? ClientId = null) : Synapse(nameof(InoRequest), DateTimeOffset.UtcNow);
```

Edit `DigitalBrain.Core/Synapse.cs:294-299`:
```csharp
public record DbInspectSchema(
    string ConnectionName,
    string Provider,
    string? ConnectionString = null,
    string? SourcePath = null,
    string? ClientId = null) : Synapse(nameof(DbInspectSchema), DateTimeOffset.UtcNow);
```

Edit `DigitalBrain.Core/Synapse.cs:302-308`:
```csharp
public record DbSchemaInspected(
    string ConnectionName,
    string Provider,
    DbSchemaModel? Schema,
    bool Succeeded = true,
    string? Error = null,
    string? ClientId = null) : Synapse(nameof(DbSchemaInspected), DateTimeOffset.UtcNow);
```

- [ ] **Step 4: Update `DbSupportNeuron.cs` and `SqliteSchemaInspector.cs`**

Edit `DigitalBrain.Kernel/DbSupportNeuron.cs` — every `SessionId: cmd.SessionId` named-argument (lines 41, 71, 87) becomes `ClientId: cmd.ClientId`; every positional `cmd.SessionId` passed to `sqliteSchemaInspector.InspectConnectionStringAsync`/`InspectFileAsync` (lines 58, 63) becomes `cmd.ClientId`.

Edit `DigitalBrain.Kernel/Db/SqliteSchemaInspector.cs` — rename the `sessionId` parameters (lines 19, 48) to `clientId`, and the internal `sessionId` locals/pass-through (lines 40, 110) to `clientId`.

- [ ] **Step 5: Update `InoNeuron.cs`**

This file threads the renamed field through ~15 call sites. Read the current file (`DigitalBrain.Kernel/Ino/InoNeuron.cs`, 822 lines) and apply this rename mechanically — every occurrence of `.SessionId` on an `InoRequest`/`DbSchemaInspected`/`DbInspectSchema` instance, and every `sessionId` parameter name on `InoNeuron`'s own private helper methods, becomes `.ClientId`/`clientId`:

- `HandleAsync(InoRequest req)` (lines 34-106): every `req.SessionId` → `req.ClientId` (lines 41, 48, 51, 60, 69, 73, 102).
- `ResolveUserIdAsync(string? sessionId)` (lines 147-155): rename parameter to `clientId`, body unchanged otherwise (it already correctly calls `session.GetSessionAsync(sessionId)` — **this stays `GetSessionAsync`, not `GetSessionByClientIdAsync`, only if you keep the current call shape**; but since `ResolveUserIdAsync` is now handed a real `clientId` from `HandleAsync(Signal signal)`'s `pendingSalesforce.SessionId` — soon `.ClientId` — and from `HandleSalesforceIntentAsync`'s `req.SessionId` — soon `req.ClientId` — it must resolve via `GetSessionByClientIdAsync`, not `GetSessionAsync` (which expects a real session id, not a clientId). Rewrite it:

```csharp
    private async Task<string> ResolveUserIdAsync(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId))
            return UserId.Anonymous.Value;

        var session = GrainFactory.GetGrain<IUserSessionNeuron>("session-main");
        var state = await session.GetSessionByClientIdAsync(clientId);
        return state?.UserId.Value ?? UserId.Anonymous.Value;
    }
```

  This is the actual fix for the still-live bug: previously this called `GetSessionAsync(sessionId)` where `sessionId` was really the chat correlation token — never a real session id — so it always fell through to `UserId.Anonymous`. Now it's handed a real `clientId` and calls the method built for exactly that lookup (Task 2).

- `HandleAsync(Signal signal)` (lines 112-145): `pendingSalesforce.SessionId` → `pendingSalesforce.ClientId` (line 123).
- `HandleAsync(TabularDataIngested ingested)` (lines 157-184): `ingested.SessionId`/`props["sessionId"]` → `ingested.ClientId`/`props["clientId"]` (line 174). (Confirm `TabularDataIngested` itself has a `SessionId` field — if it does and is out of this rename's declared scope, leave its own field name as-is and just rename the local variable/prop key at this call site; do not expand into renaming `TabularDataIngested` unless it's trivially one more field in `Synapse.cs` you're already editing. Given the pattern established, renaming it too for consistency is preferred if it's a 2-line change — check before deciding.)
- `HandleAsync(DbSchemaInspected inspected)` / `ProcessSchemaInspectedAsync` (lines 186-209): `inspected.SessionId` → `inspected.ClientId`, parameter `sessionId` → `clientId` throughout (lines 189, 198, 201).
- `InspectReferencedDatabaseAsync(string databasePath, string? sessionId)` (lines 211-226): parameter → `clientId`, `SessionId: sessionId` → `ClientId: clientId` (line 217).
- `DeliverReplySurfaceAsync(string reply, string? sessionId)` (lines 228-241): parameter → `clientId`, `props["sessionId"]` → `props["clientId"]` (line 236).
- `HandleGmailIntentAsync`/`HandleSalesforceIntentAsync` (lines 243-270): `req.SessionId` → `req.ClientId` (lines 250, 259, 265).
- `DeliverGoogleAuthSurfaceAsync`, `DeliverSalesforceCredentialSurfaceAsync`, `DeliverGmailMessagesSurfaceAsync`, `DeliverSalesforceRecordsSurfaceAsync`, `DeliverGraphSurfaceAsync` (lines 313-514): every `sessionId` parameter → `clientId`, every `props["sessionId"]` → `props["clientId"]`.
- `FetchRecentGmailAsync`/`FetchSalesforceAccountsAsync` (lines 351-418): every `req.SessionId` → `req.ClientId`, every `["sessionId"] = req.SessionId` in the `Signal` prop dictionaries → `["clientId"] = req.ClientId`.
- `LatestSuccessfulSchema(string? sessionId)` (lines 549-570): parameter → `clientId`, `schema.SessionId` → `schema.ClientId` (line 564).

Run `grep -n "SessionId\|sessionId" DigitalBrain/Kernel/Ino/InoNeuron.cs` after editing and confirm zero remaining hits.

- [ ] **Step 6: `TabularDataIngested` — same rename, plus its one producer**

`TabularDataIngested` is the same kind of "chat correlation token" carrier as `DbInspectSchema`/`DbSchemaInspected` (its only consumer is `InoNeuron.HandleAsync(TabularDataIngested)`, already touched in Step 5) — rename it for the same consistency reason, not left as an open judgment call.

Edit `DigitalBrain.Core/TabularDataSynapses.cs:6-11`:
```csharp
public record TabularDataIngested(
    string FileName,
    string HeadersJson,
    string RowsJson,
    string ColumnStatsJson,
    string? ClientId = null) : Synapse(nameof(TabularDataIngested), DateTimeOffset.UtcNow);
```

Its only producer is the `/upload` minimal-API endpoint in `DigitalBrain.Kernel/Program.cs:234-307`, which also constructs `DbInspectSchema` via `ChatUploadClassifier.BuildDbInspectSchema` for the SQLite-upload branch — both read the same form field. Edit `Program.cs:246`:
```csharp
    var clientId = form["clientId"].FirstOrDefault();
```
and rename every `sessionId` reference below it in this endpoint (lines 259, 304) to `clientId`.

Edit `DigitalBrain.Kernel/Uploads/ChatUploadClassifier.cs:37-55` (`BuildDbInspectSchema`) — rename the parameter and the positional argument it passes:
```csharp
    public static DbInspectSchema BuildDbInspectSchema(string fileName, string serverPath, string? clientId)
    {
        var safeFileName = SafeFileName(fileName);
        var connectionName = Path.GetFileNameWithoutExtension(safeFileName);
        if (string.IsNullOrWhiteSpace(connectionName))
            connectionName = "sqlite-upload";

        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = serverPath
        }.ToString();

        return new DbInspectSchema(
            connectionName,
            "sqlite",
            connectionString,
            safeFileName,
            clientId);
    }
```

Update `DigitalBrain.Tests/Ino/InoNeuronTabularDataTests.cs` — both `new TabularDataIngested(..., "session-1")` calls (lines 24-29, 53-58) keep the literal value `"session-1"` (it's just a test fixture string, now flowing through the renamed positional `ClientId` slot — no need to reword the string itself), but the assertion at line 36 changes:
```csharp
        Assert.Equal("session-1", surface.Props["clientId"]);
```

Check `DigitalBrain.Tests/Uploads/ChatUploadClassifierTests.cs` for any `BuildDbInspectSchema(..., sessionId: ...)` named-argument call or `.SessionId` assertion and rename the same way.

- [ ] **Step 7: `VisualizeDataRequest`/`ChartNeuron` prop rename**

Edit `DigitalBrain.Kernel/DataVisualizationNeuron.cs:167-173` (`ScopeSurface`) — rename the `sessionId` parameter to `clientId` and `["sessionId"] = sessionId` to `["clientId"] = clientId`. Its one call site is `ChartNeuron.HandleAsync(VisualizeDataRequest request)` at `DataVisualizationNeuron.cs:33`: `ScopeSurface(UiSurfaceSamples.Chart(surfaceId, Self.Value, spec), request.UserId, request.SessionId)`. `VisualizeDataRequest.SessionId` is out of this plan's scope per the Global Constraints note (an adjacent system, not in the spec's file list) — leave that record field named `SessionId` and just pass `request.SessionId` into the renamed `clientId:` parameter, the same pattern as Task 3's `KernelTaskNeuron` fix:
```csharp
        var surface = ScopeSurface(UiSurfaceSamples.Chart(surfaceId, Self.Value, spec), request.UserId, request.SessionId);
```
(unchanged at the call site — only `ScopeSurface`'s own parameter name changes, which is positional-compatible.)

- [ ] **Step 8: `GatewayService.Send`'s `InoRequest` branch**

Edit `DigitalBrain.Kernel/Gateway/GatewayService.cs`, replace the `InoRequest` branch (currently lines 160-175):

```csharp
            if (request.TypeName == nameof(InoRequest) || request.TypeName.Contains("InoRequest", StringComparison.OrdinalIgnoreCase))
            {
                var payloadStr = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
                var p = CaseInsensitive(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadStr));
                var prompt = p.TryGetValue("prompt", out var pr) ? pr?.ToString() ?? "" : "";
                var clientId = p.TryGetValue("clientId", out var cid) ? cid?.ToString() : null;

                var ino = grains.GetGrain<IInoNeuron>("ino-main");
                await ino.FireAsync(new InoRequest(prompt, clientId));
                return request;
            }
```

This deletes the fail-open passthrough comment and shim entirely — `clientId` is now a single, unambiguous concept read the same way every other branch reads it, resolved to real identity inside `InoNeuron.ResolveUserIdAsync` (Step 5) exactly when real identity is actually needed (Salesforce/Gmail), and used as-is for reply routing everywhere else.

- [ ] **Step 9: Update existing tests referencing `InoRequest`'s old field**

`DigitalBrain.Tests/Gateway/GatewayServiceTests.cs`:
- `Send_InoRequest_Routes_The_Real_Prompt_Not_A_Placeholder` (line ~222-241): change payload key `sessionId` → `clientId` (value can stay `"chat-session-1"` or be renamed to `"chat-client-1"` for clarity — rename it, since it's a value the test controls).
- `Send_InoRequest_BitcoinPriceIntent_DeliversFormattedPriceSurface` (line ~243-289): change the `LoginRequest`'s `clientId` (already correct name) to reuse consistently; change the `InoRequest` payload's `sessionId` key to `clientId`, passing the SAME `clientId` value the login used (previously this test resolved a fresh `sessionId` via `GetOutgoingTimelineAsync` and sent that — now it should just resend the same `clientId` the login used, since that's what `ResolveUserIdAsync` now expects):

```csharp
    [Fact]
    public async Task Send_InoRequest_BitcoinPriceIntent_DeliversFormattedPriceSurface()
    {
        _marketClient.Price = "$42,123.45";
        var svc = NewService();
        const string myClientId = "bitcoin-price-connection";

        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(LoginRequest),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                username = "bitcoin-price-user",
                password = "correct horse battery staple",
                clientId = myClientId
            }))
        }, TestContext());

        var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            prompt = "what's the bitcoin price?",
            clientId = myClientId
        });

        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(InoRequest),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(payload)
        }, TestContext());

        var flutter = Grain<IFlutterUiNeuron>("flutter-ui");
        var timeline = await flutter.GetIncomingTimelineAsync();

        var surface = Assert.Single(timeline.OfType<UiSurface>());
        Assert.Equal(UiSurface.WidgetTreeKind, surface.Kind);
        Assert.Equal(myClientId, surface.Props["clientId"]);
        Assert.Equal("assistant", surface.Props["role"]);

        var tree = Assert.IsType<UiWidgetTree>(surface.Props["tree"]);
        Assert.Equal(UiKitVocabulary.Text, tree.Type);
        Assert.Contains("$42,123.45", tree.Props["text"]!.ToString());
    }
```

Check `DigitalBrain.Tests/Ino/InoNeuronChatSurfaceTests.cs`, `InoNeuronGraphCanvasTests.cs`, `InoNeuronTabularDataTests.cs` for any direct `new InoRequest(..., sessionId: ...)`/`.SessionId` reference and rename to `clientId:`/`.ClientId` the same way.

- [ ] **Step 10: Run tests to verify they pass**

Run: `dotnet build Brain.slnx`
Expected: clean — this is the real signal that every `SessionId` reference on these renamed record types was caught; a lingering one fails the build, not silently misbehaves.

Run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~Ino|FullyQualifiedName~GatewayServiceTests|FullyQualifiedName~DbSupport|FullyQualifiedName~ChatUploadClassifier`
Expected: PASS, including the new `Send_SalesforceAuthRequested_Via_InoRequest_Resolves_Real_User_Not_Anonymous` test.

- [ ] **Step 11: Commit**

```bash
git add DigitalBrain.Core/Synapse.cs DigitalBrain.Core/TabularDataSynapses.cs DigitalBrain.Kernel/Ino/InoNeuron.cs DigitalBrain.Kernel/DbSupportNeuron.cs DigitalBrain.Kernel/Db/SqliteSchemaInspector.cs DigitalBrain.Kernel/DataVisualizationNeuron.cs DigitalBrain.Kernel/Program.cs DigitalBrain.Kernel/Uploads/ChatUploadClassifier.cs DigitalBrain.Kernel/Gateway/GatewayService.cs DigitalBrain.Tests/Gateway/GatewayServiceTests.cs DigitalBrain.Tests/Ino DigitalBrain.Tests/Uploads
git commit -m "fix(ino): resolve real Salesforce/Gmail identity from clientId instead of the chat correlation token

InoRequest.SessionId (and DbInspectSchema/DbSchemaInspected's matching field) renamed to
ClientId to match what it always actually carried — a per-connection correlation token, not
a real login session. InoNeuron.ResolveUserIdAsync now calls GetSessionByClientIdAsync,
fixing the still-live bug where Salesforce-via-chat silently resolved to the anonymous
identity no matter who was logged in."
```

---

### Task 6: `SalesforceSignals.AuthRequested` — real resolution, remove the anonymous-fallback shim

**Files:**
- Modify: `DigitalBrain.Kernel/Gateway/GatewayService.cs`
- Modify: `DigitalBrain.Kernel/Salesforce/SalesforceAuthSurfaces.cs`
- Modify: `DigitalBrain.Kernel/Salesforce/SalesforceAuthNeuron.cs` (prop rename only, lines 29-30, 191-192)
- Modify: `DigitalBrain.Tests/Gateway/GatewayServiceTests.cs`

**Interfaces:**
- Consumes: `GatewayService.ResolveSessionByClientIdAsync` (Task 4).

- [ ] **Step 1: Write the failing test**

Add to `DigitalBrain.Tests/Gateway/GatewayServiceTests.cs`:

```csharp
    [Fact]
    public async Task Send_SalesforceAuthRequested_Without_A_Real_Session_Is_Rejected()
    {
        var svc = NewService();

        var ex = await Assert.ThrowsAsync<RpcException>(() => svc.Send(new SynapseEnvelope
        {
            TypeName = SalesforceSignals.AuthRequested,
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                clientId = "never-logged-in-connection"
            }))
        }, TestContext()));

        Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~Send_SalesforceAuthRequested_Without_A_Real_Session_Is_Rejected`
Expected: FAIL — today this falls back to the `"anonymous"` identity instead of rejecting (the shim being removed).

- [ ] **Step 3: Remove the anonymous-fallback shim**

Edit `DigitalBrain.Kernel/Gateway/GatewayService.cs`, replace the `SalesforceSignals.AuthRequested` branch (currently lines 93-111):

```csharp
            if (request.TypeName == SalesforceSignals.AuthRequested || request.TypeName.Contains(SalesforceSignals.AuthRequested, StringComparison.OrdinalIgnoreCase))
            {
                var authProps = PayloadProps(request);
                var authClientId = authProps.TryGetValue("clientId", out var authCid) ? authCid?.ToString() : null;
                var authSession = await ResolveSessionByClientIdAsync(authClientId);
                if (authSession is null)
                    throw new RpcException(new Status(StatusCode.Unauthenticated, "A real login session is required to connect Salesforce."));

                var auth = grains.GetGrain<ISalesforceAuthNeuron>(authSession.UserId.Value);
                var signal = new Signal(SalesforceSignals.AuthRequested, authProps)
                {
                    Receiver = new NeuronId(authSession.UserId.Value)
                };
                await auth.DeliverAsync(signal);
                return request;
            }
```

- [ ] **Step 4: `SalesforceAuthSurfaces` and `SalesforceAuthNeuron` prop rename**

Edit `DigitalBrain.Kernel/Salesforce/SalesforceAuthSurfaces.cs:20` — rename the parameter and both usages:
```csharp
    public static UiSurface CredentialForm(string emitter, string? clientId = null, string? message = null)
    {
        ...
            if (!string.IsNullOrWhiteSpace(clientId))
                oauthButtonProps["clientId"] = clientId;
        ...
        if (!string.IsNullOrWhiteSpace(clientId))
            props["clientId"] = clientId;

        return surface with { Props = props };
    }
```
(lines 46-47, 69-70 — same structure, `sessionId` → `clientId` throughout the method and its parameter.)

Edit `DigitalBrain.Kernel/Salesforce/SalesforceAuthNeuron.cs:29-30,191-192` — rename the local `sessionId` variable read from `signal.Props`/`props` (`TryGetValue("sessionId", ...)`) to read `"clientId"` instead, and pass it into `SalesforceAuthSurfaces.CredentialForm(Self.Value, clientId, ...)`.

`InoNeuron.DeliverSalesforceCredentialSurfaceAsync` (already renamed in Task 5, Step 5) already calls `SalesforceAuthSurfaces.CredentialForm(Self.Value, clientId)` — confirm the parameter name lines up after this task (it's positional, so it already works; this is a naming-consistency confirmation, not a functional dependency).

- [ ] **Step 5: Update the existing Salesforce tests**

`Send_SalesforceAuthRequested_Routes_To_SalesforceAuthNeuron`, `Send_SalesforceAuthRequested_Routes_To_The_Callers_Own_UserKeyed_Grain` (currently lines 312-380): both already log in first and then send `SalesforceSignals.AuthRequested` with `{ sessionId }` (the just-created session id) — change the payload key to `clientId`, and send the *clientId used at login* (not the resolved session id):

```csharp
    [Fact]
    public async Task Send_SalesforceAuthRequested_Routes_To_SalesforceAuthNeuron()
    {
        var svc = NewService();
        const string myClientId = "salesforce-auth-connection";

        await svc.Send(new SynapseEnvelope
        {
            TypeName = nameof(LoginRequest),
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                username = "salesforce-auth-test-user",
                password = "correct horse battery staple",
                clientId = myClientId
            }))
        }, TestContext());

        await svc.Send(new SynapseEnvelope
        {
            TypeName = SalesforceSignals.AuthRequested,
            Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
            {
                clientId = myClientId
            }))
        }, TestContext());

        var auth = Grain<ISalesforceAuthNeuron>("salesforce-auth-test-user");
        var timeline = await auth.GetOutgoingTimelineAsync();
        var form = Assert.Single(timeline.OfType<UiSurface>(), surface => surface.Kind == ConfigFormSurface.Kind);
        Assert.Equal("salesforce", form.Props["pack"]);
        Assert.Equal(myClientId, form.Props["clientId"]);
    }
```

Apply the same transformation to `Send_SalesforceAuthRequested_Routes_To_The_Callers_Own_UserKeyed_Grain` (identical shape, different username/grain-key assertion — keep its distinct assertion, just apply the same login+clientId send pattern).

**Delete** `Send_SalesforceAuthRequested_Without_A_Session_Falls_Back_To_Anonymous` (currently lines 382-405) entirely — replaced by the new `Send_SalesforceAuthRequested_Without_A_Real_Session_Is_Rejected` test from Step 1.

`Send_GoogleAuthRequested_Routes_To_GoogleAuthNeuron` (line ~291-310) is untouched by this task — Google auth wasn't part of the incident and isn't in the spec's scope; leave its `sessionId` payload key as-is (it's a pre-existing, separately-scoped path that doesn't call `ResolveSessionByClientIdAsync` today and this plan doesn't change that).

- [ ] **Step 6: Run tests to verify they pass**

Run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~GatewayServiceTests`
Expected: PASS, all Salesforce-related tests green, including the new rejection test.

Run: `dotnet test DigitalBrain.Salesforce.Tests`
Expected: PASS — confirm nothing in this separate test project references `SalesforceAuthSurfaces.CredentialForm`'s old `sessionId:` named parameter (`grep -rn "CredentialForm" DigitalBrain.Salesforce.Tests` to check) — if it does, apply the same rename.

Run: `dotnet build Brain.slnx`
Expected: clean.

- [ ] **Step 7: Commit**

```bash
git add DigitalBrain.Kernel/Gateway/GatewayService.cs DigitalBrain.Kernel/Salesforce/SalesforceAuthSurfaces.cs DigitalBrain.Kernel/Salesforce/SalesforceAuthNeuron.cs DigitalBrain.Tests/Gateway/GatewayServiceTests.cs
git commit -m "fix(salesforce): reject Connect-Salesforce without a real session instead of falling back to anonymous

Removes the last of the three fail-open shims from commit 50ed11e now that clientId
round-trips correctly end-to-end. SalesforceAuthSurfaces/SalesforceAuthNeuron's addressing
prop renamed sessionId -> clientId to match."
```

---

### Task 7: Flutter — `chat_screen.dart` unifies its correlation token into `clientId`

**Files:**
- Modify: `app/lib/features/chat/chat_screen.dart`

**Interfaces:**
- Consumes: `WatchHomeFeedRequest.clientId` (Task 1, Dart stub).

- [ ] **Step 1: Rename `_sessionId` to `_clientId`, send it on `WatchHomeFeedRequest`, delete the reply-matching filter**

Edit `app/lib/features/chat/chat_screen.dart`:

Line 58, rename the field:
```dart
  final String _clientId = 'chat-${Random().nextInt(1 << 31)}';
```

Line 98-106 (`_connect`), pass it to `watchHomeFeed`:
```dart
      final sub = client
          .watchHomeFeed(gw.WatchHomeFeedRequest(clientId: _clientId))
          .listen(_onCard, onError: _onFeedError);
```

Lines 133-144 (`_onCard`) — delete the now-unnecessary reply-matching check (the server only ever delivers this connection's own addressed cards on its own personal stream, so a client-side re-check of the same field is redundant):
```dart
  void _onCard(gw.RfwCardEnvelope envelope) {
    if (!mounted) return;
    final data = _decode(envelope.dataJson);
    if (data['role'] != 'assistant') return;
    final tree = data['tree'] as Map<String, Object?>?;
    if (tree == null) return;
    setState(() {
      _messages.add(_ChatMessage.assistant(tree));
      _sending = false;
    });
    _scrollToEnd();
  }
```

Line 166-170 (`_send`), rename the payload key:
```dart
    final envelope = gw.SynapseEnvelope()
      ..typeName = 'InoRequest'
      ..payload = utf8.encode(
        jsonEncode({'prompt': text, 'clientId': _clientId}),
      );
```

Check the file for any other `_sessionId`/`'sessionId'` reference (e.g. an upload/multipart path mentioned around line 257 in earlier exploration — `..fields['sessionId'] = _sessionId`) and rename it the same way: `..fields['clientId'] = _clientId`.

- [ ] **Step 2: Run the existing widget test**

Run: `flutter test app/test/features/chat/chat_screen_test.dart`
Expected: PASS — this test only exercises the kernel-unreachable error banner, unaffected by the rename.

- [ ] **Step 3: Commit**

```bash
git add app/lib/features/chat/chat_screen.dart
git commit -m "fix(chat): rename the per-widget correlation token to clientId and stop re-checking it client-side

Reaching for the same field name as the real per-connection identity (renamed from
_sessionId) instead of a separate correlation-token concept — this is exactly the
collision that caused the live multi-user bug. The client-side reply filter is deleted,
not renamed: the server now only ever delivers this connection's own cards on its own
personal stream, so a client-side re-check of the same field was redundant."
```

---

### Task 8: Flutter — remaining `WatchHomeFeed` call sites send a `clientId`

**Files:**
- Modify: `app/lib/shell/forui_app_shell.dart`
- Modify: `app/lib/features/canvas/living_canvas_screen.dart`
- Modify: `app/lib/features/experience/experience_host_screen.dart`

**Interfaces:**
- Consumes: `WatchHomeFeedRequest.clientId` (Task 1, Dart stub).

- [ ] **Step 1: `forui_app_shell.dart`**

This is the connection that actually drives real login (per `router.dart`'s own comment — it's the only `WatchHomeFeed` caller reached from a `ShellRoute` gated behind sign-in). Add a stable per-instance clientId and send it.

Edit `app/lib/shell/forui_app_shell.dart` — add a field near the other connection state (alongside `_channel`, `_gatewayClient`, etc.):
```dart
  final String _clientId = 'shell-${Random().nextInt(1 << 31)}';
```
(add `import 'dart:math';` if not already present in this file — check first.)

Change line 95-97:
```dart
      final sub = client
          .watchHomeFeed(gw.WatchHomeFeedRequest(clientId: _clientId))
          .listen(_onCard, onError: _onFeedError, onDone: _onFeedDone);
```

- [ ] **Step 2: `living_canvas_screen.dart`**

Edit `app/lib/features/canvas/living_canvas_screen.dart:239` — add the same pattern (add a `_clientId` field near the screen's other state, generated once in `initState`/at field-declaration time, then):
```dart
    _homeFeedSub = client.watchHomeFeed(gw.WatchHomeFeedRequest(clientId: _clientId)).listen((
```

- [ ] **Step 3: `experience_host_screen.dart`**

Edit `app/lib/features/experience/experience_host_screen.dart:63` — same pattern:
```dart
          .watchHomeFeed(gw.WatchHomeFeedRequest(clientId: _clientId))
```

- [ ] **Step 4: Run existing widget tests**

Run: `flutter test app/test/shell/forui_app_shell_test.dart`
Expected: PASS — this test only exercises the pure `autoSwitchTargetForKind` function, unaffected.

Run: `flutter analyze app/lib`
Expected: no new warnings/errors (confirms `dart:math`'s `Random` import, if newly added, isn't flagged unused elsewhere and every field is actually referenced).

- [ ] **Step 5: Commit**

```bash
git add app/lib/shell/forui_app_shell.dart app/lib/features/canvas/living_canvas_screen.dart app/lib/features/experience/experience_host_screen.dart
git commit -m "fix(client): send a stable clientId when opening WatchHomeFeed from every screen

Completes the client side of per-connection addressing — each of the app's independent
WatchHomeFeed connections (confirmed mutually exclusive via go_router in router.dart, never
concurrent) now identifies itself from the first packet instead of connecting anonymously
and never being addressable."
```

---

### Task 9: Full verification — build, test, real app, close out the incident doc

**Files:**
- Modify: `docs/CONTINUATION-MULTIUSER-FEED-ISOLATION.md` (status header)
- Modify: `CONTINUITY.md` (new dated entry)

- [ ] **Step 1: Full solution build and test**

Run: `dotnet build Brain.slnx`
Expected: 0 errors, 0 warnings introduced.

Run: `dotnet test Brain.slnx`
Expected: 0 failures. Compare the skip count against the baseline noted before Task 1 — it must not have grown.

- [ ] **Step 2: `aspire run` + real Flutter end-to-end drive**

Per this repo's mandatory process rule (`CONTINUATION-MULTIUSER-FEED-ISOLATION.md` §0/§6) and the user's own standing instruction to always verify with `aspire run` after changes: start the AppHost via the Aspire MCP tools, then drive the actual Flutter app (not just backend tests):

1. Launch/rebuild the AppHost (`mcp__aspire__execute_resource_command` rebuild, or `aspire run` if starting fresh) across all kernel replicas.
2. Open the Flutter app, log in as a real user (or the seeded dev credentials in Development), confirm the signed-in shell, installed bundles, and task manager all render.
3. Open a **second** app instance (or a second browser tab hitting `/`) and log in as a **different** user concurrently; confirm neither user's shell/marketplace/chat-reply cards ever appear on the other's connection.
4. From the chat screen, trigger a Salesforce-dependent prompt ("check my salesforce accounts") for a logged-in user; confirm the credential form (or account list, if already connected) is delivered to that specific user's own connection — this is the concrete manual check for the bug fixed in Task 5.
5. Log out from one connection; confirm the login form reappears there and only there.

Use `mcp__aspire__list_console_logs`/`mcp__aspire__list_structured_logs` to confirm no exceptions were logged on any replica during this drive.

- [ ] **Step 3: Close out the continuation doc**

Edit `docs/CONTINUATION-MULTIUSER-FEED-ISOLATION.md`'s status line (line 3):
```markdown
Status: RESOLVED — see docs/superpowers/specs/2026-07-04-multiuser-feed-isolation-design.md (design) and
docs/superpowers/plans/2026-07-04-multiuser-feed-isolation-clientid-routing.md (implementation). Per-session
feed isolation (P6a) is enforced via per-clientId Orleans stream routing; the fail-open shim from commit
50ed11e has been fully reverted.
```

- [ ] **Step 4: Add a `CONTINUITY.md` dated entry**

Following this repo's existing convention (see the two `2026-07-04` entries already there), add a new entry at the top of `CONTINUITY.md` summarizing: what shipped (clientId as the single client-facing identity, Orleans-native per-clientId stream routing replacing `HomeFeedBus`'s hand-rolled dictionary and `HomeFeedStreamSubscriber`), the still-live bug it fixed beyond the original mitigation (Salesforce-via-chat identity resolution), and that the fail-open shim is fully reverted.

- [ ] **Step 5: Commit**

```bash
git add docs/CONTINUATION-MULTIUSER-FEED-ISOLATION.md CONTINUITY.md
git commit -m "docs: close out multi-user feed isolation — clientId routing shipped, fail-open shim reverted"
```
