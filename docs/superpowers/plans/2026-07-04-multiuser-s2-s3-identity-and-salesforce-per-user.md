# MULTIUSER Stage S2 (Identity Spine) + Stage S3 (Salesforce Per-User) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn `UserSessionNeuron`'s existing identity (`UserId`/`sessionId`) into something the rest of the kernel actually uses: filter the Home feed by session, stop trusting client-supplied identity fields at the gateway, and move Salesforce auth/CRM grains + their pack-config storage from global singletons to per-user.

**Architecture:** Add a shared `NeuronScope`/`PackConfigScopes` identity spine in `DigitalBrain.Core`. The gateway resolves `sessionId → UserSessionState` once via a new helper and uses the resolved `UserId` everywhere it previously trusted a payload field. `HomeFeedBus` gains per-session card addressing. Salesforce grains move to `{userId}` keys; the OAuth callback (a cold, unauthenticated HTTP GET) learns which grain to route to via a plaintext `{userId}:{nonce}` OAuth `state` (owner-approved minimal mechanism — NOT the full DataProtection-encrypted state, which is deferred to S4/D-MU2). `SalesforceCrmNeuron` switches from eager constructor-injected DI to an explicit per-call `ISalesforceApiClientFactory` (D-MU7, supersedes the original doc's `IGrainContextAccessor` wording).

**Tech Stack:** C#/.NET, Orleans grains (journaled), gRPC (Grpc.Tools proto codegen — adding a proto field and running `dotnet build` regenerates the C# type automatically, no manual codegen step), xUnit + Orleans `TestingHost` + Reqnroll (BDD).

## Global Constraints

- No new NuGet packages required by this plan. If any dependency version ever needs bumping while doing this work, use the latest version and verify any unfamiliar API via Context7 first — this plan does not introduce any new library APIs, only existing Orleans/gRPC/xUnit patterns already used elsewhere in this repo.
- No `///` doc-comment summaries anywhere. Only add an inline comment where the WHY is genuinely non-obvious (e.g. the plaintext-state-routing tradeoff). Prefer self-explanatory C# naming over comments.
- `NeuronScope` / `PackConfigScopes` are plain value-shape helpers, not wire types — no `[GenerateSerializer]` (per the design doc: "neither type crosses a grain-interface boundary as a wire record"). `RfwCard`'s new field piggybacks on its existing `[GenerateSerializer]` attribute; no new `[Id(n)]` attributes needed since the existing `RfwCard`/`LoginRequest`/etc. records in this repo already omit them for plain broadcast records — stay consistent with that established local pattern, not the "every new synapse needs explicit Ids" rule (which governs *new* cross-grain record types, e.g. none are introduced here).
- Do not touch `GoogleAuthNeuron`, `BuildGoogleCredential`, or the `GoogleSignals.AuthRequested` gateway branch — Google's identical eager-throw-on-activation shape is explicitly out of scope until S4.
- Do not implement DataProtection-encrypted OAuth state (D-MU2) — that is S4 scope. This plan uses the owner-approved minimal alternative: prefix the existing bare-GUID `state` with the plaintext userId, split on it to route the callback, and keep relying on the grain's existing exact-string state comparison for CSRF protection.
- After finishing all tasks, run the full test suite (`dotnet test`) and confirm green before considering this plan done, per this repo's standing instruction to run tests at high severity before declaring work complete.

---

## Stage S2 — Identity Spine & Gateway Isolation

### Task 1 (S2.1): Username charset validation (prerequisite)

**Files:**
- Modify: `DigitalBrain.Kernel/Auth/UserSessionNeuron.cs:24-71` (add a rejection branch), `:332-333` (leave `NormalizeUsername` as-is, add a sibling validator)
- Test: `DigitalBrain.Tests/Auth/UserSessionNeuronTests.cs`

**Interfaces:**
- Produces: nothing new consumed elsewhere; this only tightens `HandleAsync(LoginRequest)`'s existing rejection path (`RejectAsync`, already defined at `UserSessionNeuron.cs:226-230`).

- [ ] **Step 1: Write the failing test**

Add to `DigitalBrain.Tests/Auth/UserSessionNeuronTests.cs` (inside the existing `UserSessionNeuronTests` class, alongside the other `[Fact]`s):

```csharp
[Fact]
public async Task Registration_With_Slash_In_Username_Is_Rejected()
{
    var session = Grain<IUserSessionNeuron>("session-auth-invalid-charset");

    await session.FireAsync(new LoginRequest("alice/bob", "some-password-123", "test"));

    var timeline = await session.GetOutgoingTimelineAsync();
    Assert.Empty(timeline.OfType<LocalUserRegistered>());
    var failed = Assert.Single(timeline.OfType<LoginFailed>());
    Assert.Equal("alice/bob", failed.Username);
    Assert.Contains("invalid characters", failed.Reason);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.Tests --filter Registration_With_Slash_In_Username_Is_Rejected`
Expected: FAIL — today `alice/bob` is accepted (first-user provisioning), so `LocalUserRegistered` is fired instead of `LoginFailed`.

- [ ] **Step 3: Write minimal implementation**

In `DigitalBrain.Kernel/Auth/UserSessionNeuron.cs`, change `HandleAsync(LoginRequest request)` (currently lines 24-33):

```csharp
public async Task HandleAsync(LoginRequest request)
{
    var username = NormalizeUsername(request.Username);
    var clientId = string.IsNullOrWhiteSpace(request.ClientId) ? "flutter" : request.ClientId.Trim();

    if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(request.Password))
    {
        await RejectAsync(username, "username and password are required", clientId);
        return;
    }

    if (!IsValidUsernameCharset(username))
    {
        await RejectAsync(username, "username may not contain invalid characters ('/', whitespace, or quotes)", clientId);
        return;
    }
```

(the rest of the method — dev-credentials bypass, user lookup, etc. — is unchanged).

Add the new private static method next to `NormalizeUsername` (`UserSessionNeuron.cs:332-333`):

```csharp
private static string NormalizeUsername(string value) =>
    (value ?? string.Empty).Trim().ToLowerInvariant();

private static bool IsValidUsernameCharset(string username) =>
    username.Length > 0 && !username.Any(ch => ch is '/' or '\'' or '"' || char.IsWhiteSpace(ch));
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DigitalBrain.Tests --filter Registration_With_Slash_In_Username_Is_Rejected`
Expected: PASS. Also run `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~UserSessionNeuronTests` to confirm the other 4 existing tests in this file are still green (usernames `alice.local`, `bob`, `carol` all pass the new charset check).

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Kernel/Auth/UserSessionNeuron.cs DigitalBrain.Tests/Auth/UserSessionNeuronTests.cs
git commit -m "feat(auth): reject usernames containing '/', whitespace, or quotes"
```

---

### Task 2 (S2.2): `NeuronScope` + `PackConfigScopes` identity spine in Core

**Files:**
- Create: `DigitalBrain.Core/NeuronScope.cs`
- Test: `DigitalBrain.Tests/Core/NeuronScopeTests.cs` (new file/folder)

**Interfaces:**
- Produces: `NeuronScope(UserId UserId, string? ThreadId)` with `NeuronScope.TryParse(string grainKey, out NeuronScope scope)` and `scope.ToKey()`; `PackConfigScopes.App` (const `"default"`) and `PackConfigScopes.ForUser(UserId userId)`; extension method `NeuronId.AsScope()`. All consumed by S3 tasks.

- [ ] **Step 1: Write the failing test**

Create `DigitalBrain.Tests/Core/NeuronScopeTests.cs`:

```csharp
using DigitalBrain.Core;

namespace DigitalBrain.Tests.Core;

public class NeuronScopeTests
{
    [Fact]
    public void TryParse_Without_Slash_Yields_UserId_Only()
    {
        Assert.True(NeuronScope.TryParse("alice", out var scope));
        Assert.Equal("alice", scope.UserId.Value);
        Assert.Null(scope.ThreadId);
        Assert.Equal("alice", scope.ToKey());
    }

    [Fact]
    public void TryParse_With_Slash_Splits_UserId_And_ThreadId()
    {
        Assert.True(NeuronScope.TryParse("alice/thread-1", out var scope));
        Assert.Equal("alice", scope.UserId.Value);
        Assert.Equal("thread-1", scope.ThreadId);
        Assert.Equal("alice/thread-1", scope.ToKey());
    }

    [Fact]
    public void TryParse_Empty_Key_Fails()
    {
        Assert.False(NeuronScope.TryParse("", out _));
        Assert.False(NeuronScope.TryParse(null!, out _));
    }

    [Fact]
    public void AsScope_Extension_Parses_NeuronId()
    {
        var scope = new NeuronId("bob").AsScope();
        Assert.Equal("bob", scope.UserId.Value);
        Assert.Null(scope.ThreadId);
    }

    [Fact]
    public void PackConfigScopes_App_Is_Default_And_ForUser_Prefixes_UserId()
    {
        Assert.Equal("default", PackConfigScopes.App);
        Assert.Equal("user:alice", PackConfigScopes.ForUser(new UserId("alice")));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~NeuronScopeTests`
Expected: FAIL to compile — `NeuronScope`/`PackConfigScopes`/`AsScope` don't exist yet.

- [ ] **Step 3: Write minimal implementation**

Create `DigitalBrain.Core/NeuronScope.cs`:

```csharp
namespace DigitalBrain.Core;

public readonly record struct NeuronScope(UserId UserId, string? ThreadId)
{
    public static bool TryParse(string grainKey, out NeuronScope scope)
    {
        if (string.IsNullOrWhiteSpace(grainKey))
        {
            scope = default;
            return false;
        }

        var separatorIndex = grainKey.IndexOf('/');
        scope = separatorIndex < 0
            ? new NeuronScope(new UserId(grainKey), null)
            : new NeuronScope(new UserId(grainKey[..separatorIndex]), grainKey[(separatorIndex + 1)..]);
        return true;
    }

    public string ToKey() => ThreadId is null ? UserId.Value : $"{UserId.Value}/{ThreadId}";
}

public static class PackConfigScopes
{
    public const string App = "default";
    public static string ForUser(UserId userId) => $"user:{userId.Value}";
}

public static class NeuronScopeExtensions
{
    public static NeuronScope AsScope(this NeuronId id) =>
        NeuronScope.TryParse(id.Value, out var scope)
            ? scope
            : throw new InvalidOperationException($"Grain key '{id.Value}' cannot be parsed as a NeuronScope.");
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~NeuronScopeTests`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Core/NeuronScope.cs DigitalBrain.Tests/Core/NeuronScopeTests.cs
git commit -m "feat(core): add NeuronScope + PackConfigScopes identity spine"
```

---

### Task 3 (S2.3): `RfwCard` gains an addressable `SessionId`; `HomeFeedBus` filters by it

**Files:**
- Modify: `DigitalBrain.Ui.Contracts/Ui/RfwCard.cs`, `DigitalBrain.Kernel/Ui/UiSurfaceRfwBridge.cs`, `DigitalBrain.Kernel/Ui/HomeFeedBus.cs`
- Test: `DigitalBrain.Tests/Ui/HomeFeedBusTests.cs`

**Interfaces:**
- Produces: `RfwCard(string LibraryName, string RootWidget, string DataJson, string? SessionId = null)`; `HomeFeedBus.Subscribe(string? sessionId = null)`. Consumed by S2.4 (`GatewayService.WatchHomeFeed`).

- [ ] **Step 1: Write the failing test**

Add to `DigitalBrain.Tests/Ui/HomeFeedBusTests.cs` (inside the existing `HomeFeedBusTests` class):

```csharp
[Fact]
public async Task Subscriber_With_SessionId_Only_Receives_Cards_Addressed_To_It_Or_Unaddressed()
{
    var bus = new HomeFeedBus();
    using var subscriptionA = bus.Subscribe("session-a");

    bus.Broadcast(new RfwCard("digitalbrain", "ForA", "{}", "session-a"));
    bus.Broadcast(new RfwCard("digitalbrain", "ForB", "{}", "session-b"));
    bus.Broadcast(new RfwCard("digitalbrain", "Unaddressed", "{}"));

    var first = await subscriptionA.Reader.ReadAsync();
    var second = await subscriptionA.Reader.ReadAsync();
    Assert.Equal("ForA", first.RootWidget);
    Assert.Equal("Unaddressed", second.RootWidget);
    Assert.False(subscriptionA.Reader.TryRead(out _));
}

[Fact]
public async Task Subscriber_Without_SessionId_Only_Receives_Unaddressed_Cards()
{
    var bus = new HomeFeedBus();
    using var subscription = bus.Subscribe();

    bus.Broadcast(new RfwCard("digitalbrain", "ForA", "{}", "session-a"));
    bus.Broadcast(new RfwCard("digitalbrain", "Unaddressed", "{}"));

    var received = await subscription.Reader.ReadAsync();
    Assert.Equal("Unaddressed", received.RootWidget);
    Assert.False(subscription.Reader.TryRead(out _));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~HomeFeedBusTests`
Expected: FAIL to compile (`RfwCard` has no 4th constructor argument yet); once that's stubbed in, FAIL on assertions (today every subscriber gets every card).

- [ ] **Step 3: Write minimal implementation**

`DigitalBrain.Ui.Contracts/Ui/RfwCard.cs` — change the record:

```csharp
[GenerateSerializer]
public record RfwCard(string LibraryName, string RootWidget, string DataJson, string? SessionId = null)
    : Synapse(nameof(RfwCard), DateTimeOffset.UtcNow);
```

`DigitalBrain.Kernel/Ui/HomeFeedBus.cs` — change the subscriber storage and filtering:

```csharp
public sealed class HomeFeedBus(IClusterClient? clusterClient = null, ILogger<HomeFeedBus>? logger = null)
{
    private const int MaxSeenEntries = 5_000;
    private readonly ConcurrentDictionary<Guid, (string? SessionId, Channel<RfwCard> Channel)> _subscribers = new();
    private readonly HashSet<string> _seen = new();
    private readonly Queue<string> _seenOrder = new();
    private readonly object _seenLock = new();
    private readonly IClusterClient? _clusterClient = clusterClient;
    private readonly ILogger<HomeFeedBus>? _logger = logger;
    private IAsyncStream<RfwCard>? _stream;

    public Subscription Subscribe(string? sessionId = null)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateUnbounded<RfwCard>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });
        _subscribers[id] = (sessionId, channel);
        return new Subscription(this, id, channel);
    }

    // Called by HomeFeedStreamSubscriber on each silo to fan a received stream card to local gRPC subscribers.
    public void FanLocal(RfwCard card)
    {
        if (IsDuplicate(card)) return;
        foreach (var (_, subscriber) in _subscribers)
        {
            if (card.SessionId is not null && !string.Equals(card.SessionId, subscriber.SessionId, StringComparison.Ordinal))
                continue;
            subscriber.Channel.Writer.TryWrite(card);
        }
    }
```

(the rest of `HomeFeedBus` — `Broadcast`, `GetOrCreateStream`, `IsDuplicate`, `ContentHash`, `Subscription` — is unchanged; `Subscription`'s `Dispose` still keys off `id` via `TryRemove`, unaffected by the tuple value change).

`DigitalBrain.Kernel/Ui/UiSurfaceRfwBridge.cs` — thread the session id through all three branches. Change `FromUiSurface`:

```csharp
public static RfwCard FromUiSurface(UiSurface surface, string emitter)
{
    var addressedSessionId = surface.Props.TryGetValue("sessionId", out var sessionIdValue) && sessionIdValue is not null
        ? sessionIdValue.ToString()
        : null;

    // If the surface already carries a full RFW or widget tree definition, honor it directly.
    if (surface.Kind == UiSurface.RfwKind || surface.Props.ContainsKey("source") || surface.Props.ContainsKey("rfwSource"))
    {
        var lib = ValueOrDefault(surface, "libraryName", "digitalbrain");
        var root = ValueOrDefault(surface, "rootWidget", "root");
        var dataJson = surface.Props.TryGetValue("dataJson", out var dj) && dj is string s ? s
            : JsonSerializer.Serialize(surface.Props);
        var correlation = surface.Props.TryGetValue(UiSurfaceKeys.SurfaceId, out var sid) && sid is string sidStr && sidStr.Length > 0
            ? sidStr
            : surface.CorrelationId ?? surface.SynapseId;
        return new RfwCard(lib, root, dataJson, addressedSessionId) { CorrelationId = correlation };
    }

    if (surface.Kind == UiSurface.WidgetTreeKind && surface.Props.TryGetValue("tree", out var treeObj))
    {
        var kind = surface.Props.TryGetValue("surfaceKind", out var surfaceKind) && surfaceKind is not null
            ? surfaceKind
            : surface.Kind;
        var payload = new Dictionary<string, object?> { ["tree"] = treeObj, ["kind"] = kind };
        foreach (var markerKey in new[] { "activeExperience", "experienceId", UiSurfaceKeys.SurfaceId, UiSurfaceKeys.Title, "sessionId", "role", "surfaceKind" })
        {
            if (surface.Props.TryGetValue(markerKey, out var markerValue) && markerValue is not null)
                payload[markerKey] = markerValue;
        }
        var correlation = surface.Props.TryGetValue(UiSurfaceKeys.SurfaceId, out var sid) && sid is string sidStr && sidStr.Length > 0
            ? sidStr
            : surface.CorrelationId ?? surface.SynapseId;
        return new RfwCard("digitalbrain", "WidgetTreeHost", JsonSerializer.Serialize(payload), addressedSessionId)
        {
            CorrelationId = correlation
        };
    }

    var title = ValueOrDefault(surface, UiSurfaceKeys.Title, "Live embodied surface");
    var body = ValueOrDefault(surface, "body", "A typed C# pack emitted this UiSurface through the kernel.");
    var status = ValueOrDefault(surface, "status", "live");
    var tone = ValueOrDefault(surface, "tone", "teal");
    var source = ValueOrDefault(surface, "source", DefaultSource);

    var data = new Dictionary<string, object?>
    {
        ["source"] = source,
        ["title"] = title,
        ["body"] = body,
        ["status"] = status,
        ["tone"] = tone,
        ["kind"] = surface.Kind,
        ["footer"] = "emitter: " + ValueOrDefault(surface, UiSurfaceKeys.Emitter, emitter),
        ["surfaceId"] = ValueOrDefault(surface, UiSurfaceKeys.SurfaceId, surface.SynapseId)
    };

    foreach (var (key, value) in surface.Props)
    {
        data[key] = value;
    }

    return new RfwCard("digitalbrain", "root", JsonSerializer.Serialize(data), addressedSessionId)
    {
        CorrelationId = surface.CorrelationId ?? surface.SynapseId
    };
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~HomeFeedBusTests`
Expected: PASS (all 4 tests, including the 2 pre-existing ones — `Subscribe()` still defaults to `sessionId: null`, so old callers are unaffected).

Also run the cross-silo suite to confirm no regression: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~HomeFeedCrossSiloTests` — both tests broadcast unaddressed cards (`SessionId` omitted), so they still reach every subscriber regardless of that subscriber's own session filter.

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Ui.Contracts/Ui/RfwCard.cs DigitalBrain.Kernel/Ui/HomeFeedBus.cs DigitalBrain.Kernel/Ui/UiSurfaceRfwBridge.cs DigitalBrain.Tests/Ui/HomeFeedBusTests.cs
git commit -m "feat(ui): address RfwCards to a sessionId and filter HomeFeedBus subscribers by it"
```

---

### Task 4 (S2.4): Gateway session resolution + `WatchHomeFeed` wiring (fixes P3, P6a)

**Files:**
- Modify: `DigitalBrain.Kernel/Protos/digitalbrain.proto`, `DigitalBrain.Kernel/Gateway/GatewayService.cs`
- Test: `DigitalBrain.Tests/Gateway/GatewayServiceTests.cs`

**Interfaces:**
- Consumes: `HomeFeedBus.Subscribe(string? sessionId)` (S2.3), `IUserSessionNeuron.GetSessionAsync(string)` (existing).
- Produces: `GatewayService.ResolveSessionAsync(string? sessionId) → Task<UserSessionState?>`, reused by S2.5 and S3.1.

- [ ] **Step 1: Write the failing test**

Add to `DigitalBrain.Tests/Gateway/GatewayServiceTests.cs` (inside the existing `GatewayServiceTests` class):

```csharp
[Fact]
public async Task WatchHomeFeed_Only_Delivers_Cards_Addressed_To_The_Resolved_Session()
{
    var svc = NewService();

    await svc.Send(new SynapseEnvelope
    {
        TypeName = nameof(LoginRequest),
        Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            username = "feed-isolation-user",
            password = "correct horse battery staple",
            clientId = "test"
        }))
    }, TestContext());

    var session = Grain<IUserSessionNeuron>("session-main");
    var sessionId = (await session.GetOutgoingTimelineAsync()).OfType<UserSessionCreated>().Single().SessionId;

    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var writer = new CapturingServerStreamWriter<RfwCardEnvelope>();
    var watchTask = svc.WatchHomeFeed(new WatchHomeFeedRequest { SessionId = sessionId }, writer, TestContext(cts.Token));

    for (var attempt = 0; attempt < 40 && writer.Messages.Count == 0; attempt++)
        await Task.Delay(25);

    _homeFeedBus.Broadcast(new RfwCard("digitalbrain", "AddressedToMe", "{}", sessionId));
    _homeFeedBus.Broadcast(new RfwCard("digitalbrain", "AddressedToSomeoneElse", "{}", "someone-elses-session"));
    _homeFeedBus.Broadcast(new RfwCard("digitalbrain", "Unaddressed", "{}"));

    await Task.Delay(300);
    cts.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watchTask);

    Assert.Contains(writer.Messages, m => m.RootWidget == "AddressedToMe");
    Assert.Contains(writer.Messages, m => m.RootWidget == "Unaddressed");
    Assert.DoesNotContain(writer.Messages, m => m.RootWidget == "AddressedToSomeoneElse");
}

[Fact]
public async Task WatchHomeFeed_Unauthenticated_Never_Receives_Session_Addressed_Cards()
{
    var svc = NewService();
    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
    var writer = new CapturingServerStreamWriter<RfwCardEnvelope>();
    var watchTask = svc.WatchHomeFeed(new WatchHomeFeedRequest(), writer, TestContext(cts.Token));

    for (var attempt = 0; attempt < 40 && writer.Messages.Count == 0; attempt++)
        await Task.Delay(25);

    _homeFeedBus.Broadcast(new RfwCard("digitalbrain", "AddressedToSomeone", "{}", "some-real-session"));
    _homeFeedBus.Broadcast(new RfwCard("digitalbrain", "SystemUnaddressed", "{}"));

    await Task.Delay(300);
    cts.Cancel();
    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => watchTask);

    Assert.Contains(writer.Messages, m => m.RootWidget == "SystemUnaddressed");
    Assert.DoesNotContain(writer.Messages, m => m.RootWidget == "AddressedToSomeone");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~WatchHomeFeed_Only_Delivers|FullyQualifiedName~WatchHomeFeed_Unauthenticated_Never`
Expected: FAIL to compile (`WatchHomeFeedRequest.SessionId` doesn't exist yet); once the proto field is added, FAIL on assertions (today `WatchHomeFeed` calls `homeFeedBus.Subscribe()` with no filter, so every card reaches every subscriber).

- [ ] **Step 3: Write minimal implementation**

`DigitalBrain.Kernel/Protos/digitalbrain.proto` — change line 40:

```proto
message WatchHomeFeedRequest {
  string session_id = 1;
}
```

`DigitalBrain.Kernel/Gateway/GatewayService.cs` — change `WatchHomeFeed` (currently lines 238-254) and add the new helper:

```csharp
// Server-driven UI: stream RfwCards to the client as neurons broadcast them, until the client disconnects.
public override async Task WatchHomeFeed(WatchHomeFeedRequest request, IServerStreamWriter<RfwCardEnvelope> responseStream, ServerCallContext context)
{
    logger.LogInformation("WatchHomeFeed opened for {Peer}", context.Peer);
    // The first card a client sees is the login surface — pre-fill it with the dev credentials in Development.
    var initialLogin = DevAuth.Enabled(configuration, environment)
        ? UiSurfaceSamples.Login(clientId: "flutter", defaultUsername: DevAuth.Username, defaultPassword: DevAuth.Password)
        : UiSurfaceSamples.Login(clientId: "flutter");
    await WriteCardAsync(responseStream, UiSurfaceRfwBridge.FromUiSurface(initialLogin, "session-main"));
    logger.LogInformation("WatchHomeFeed sent initial login surface to {Peer}", context.Peer);

    var session = await ResolveSessionAsync(request.SessionId);
    using var subscription = homeFeedBus.Subscribe(session?.SessionId);
    await foreach (var card in subscription.Reader.ReadAllAsync(context.CancellationToken))
    {
        await WriteCardAsync(responseStream, card);
    }
}

// Resolves a client-supplied sessionId to its server-side UserSessionState exactly once. Callers must use
// the RESULT's fields (session?.UserId, session?.SessionId), never the raw request field, downstream — an
// invalid/expired/fabricated sessionId collapses to null here so nothing built on it can trust a lie.
private async Task<UserSessionState?> ResolveSessionAsync(string? sessionId)
{
    if (string.IsNullOrWhiteSpace(sessionId)) return null;
    var session = grains.GetGrain<IUserSessionNeuron>("session-main");
    return await session.GetSessionAsync(sessionId);
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet build` (regenerates `WatchHomeFeedRequest.SessionId` from the proto), then `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~GatewayServiceTests`
Expected: PASS (all tests in the file, including the pre-existing `WatchHomeFeed_Writes_Login_Surface_To_New_Client`, which sends `new WatchHomeFeedRequest()` — `SessionId` defaults to `""`, `ResolveSessionAsync` returns `null`, behavior unchanged).

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Kernel/Protos/digitalbrain.proto DigitalBrain.Kernel/Gateway/GatewayService.cs DigitalBrain.Tests/Gateway/GatewayServiceTests.cs
git commit -m "feat(gateway): resolve WatchHomeFeed sessions server-side and filter the feed by them"
```

---

### Task 5 (S2.5): Delete trust in payload identity fields (fixes P6b)

**Files:**
- Modify: `DigitalBrain.Kernel/Gateway/GatewayService.cs:56-71` (`InstallFromMarketplace`), `:107-146` (`ConfigurationProvided`), `:148-158` (`InoRequest`)
- Modify: `DigitalBrain.Tests/Steps/ConfigFormSteps.cs` (owner-approved: reconcile the BDD scenario with the new whitelist instead of relaxing it)

**Interfaces:**
- Consumes: `GatewayService.ResolveSessionAsync` (S2.4), `PackConfigScopes` (S2.2).

- [ ] **Step 1: Write the failing test**

Add to `DigitalBrain.Tests/Gateway/GatewayServiceTests.cs`:

```csharp
[Fact]
public async Task ConfigurationProvided_With_Scope_Not_Owned_By_Caller_Is_Rejected()
{
    // NewService() passes packConfigStore: null, which trips the earlier "store not configured" guard
    // before ever reaching the scope check. Build a service with a real store instead.
    var services = new ServiceCollection();
    services.AddPackConfigStore(blobsForKeyRing: null);
    var packConfigStore = services.BuildServiceProvider().GetRequiredService<IPackConfigStore>();

    var svc = new GatewayService(Cluster.GrainFactory, new ConfigurationBuilder().Build(), _homeFeedBus,
        new SignalEgressBus(), new FakeHostEnvironment(), NullLogger<GatewayService>.Instance, packConfigStore);

    var ex = await Assert.ThrowsAsync<RpcException>(() => svc.Send(new SynapseEnvelope
    {
        TypeName = nameof(ConfigurationProvided),
        Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            pack = "some-pack",
            scope = "user:someone-else",
            secretField = "value"
        }))
    }, TestContext()));

    Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
}

[Fact]
public async Task InstallFromMarketplace_Ignores_Client_Supplied_BuyerId_When_Unauthenticated()
{
    using var subscription = _homeFeedBus.Subscribe();
    var svc = NewService();

    await svc.Send(new SynapseEnvelope
    {
        TypeName = nameof(InstallFromMarketplace),
        Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            packName = "nonexistent-pack",
            version = "1.0",
            buyerId = "attacker-supplied-victim-id"
        }))
    }, TestContext());

    var market = Grain<IMarketplaceNeuron>("market-main");
    var timeline = await market.GetOutgoingTimelineAsync();
    var install = Assert.Single(timeline.OfType<InstallFromMarketplace>(), i => i.PackName == "nonexistent-pack");
    Assert.Equal("anonymous", install.BuyerId);
}
```

Add `using DigitalBrain.Core.Config;` and `using DigitalBrain.Kernel.Config;` to `GatewayServiceTests.cs`'s existing using block (needed for `IPackConfigStore` and the `AddPackConfigStore` extension method used above; both namespaces are new to this file).

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~ConfigurationProvided_With_Scope_Not_Owned|FullyQualifiedName~InstallFromMarketplace_Ignores_Client_Supplied`
Expected: FAIL — today `ConfigurationProvided` accepts any scope string with no rejection, and `InstallFromMarketplace` uses the client-supplied `buyerId` verbatim (`"attacker-supplied-victim-id"`, not `"anonymous"`).

- [ ] **Step 3: Write minimal implementation**

`DigitalBrain.Kernel/Gateway/GatewayService.cs` — change the `InstallFromMarketplace` branch (currently lines 56-71):

```csharp
if (request.TypeName == nameof(InstallFromMarketplace) || request.TypeName.Contains("InstallFromMarketplace", StringComparison.OrdinalIgnoreCase))
{
    var market = grains.GetGrain<IMarketplaceNeuron>("market-main");
    var payloadStr = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
    var p = CaseInsensitive(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadStr));
    var packName = p.TryGetValue("packName", out var pn) ? pn?.ToString() ?? p.GetValueOrDefault("name")?.ToString() ?? "" : "";
    var ver = p.TryGetValue("version", out var v) ? v?.ToString() ?? "" : "";
    var sessionId = p.TryGetValue("sessionId", out var sid) ? sid?.ToString() : null;
    var installSession = await ResolveSessionAsync(sessionId);
    var buyer = installSession?.UserId.Value ?? "anonymous";
    if (string.IsNullOrWhiteSpace(packName)) packName = request.CorrelationId; // fallback
    await market.FireAsync(new InstallFromMarketplace(packName, ver, buyer, sessionId));
    return request;
}
```

Change the `ConfigurationProvided` branch (currently lines 107-146):

```csharp
if (request.TypeName == nameof(ConfigurationProvided))
{
    if (packConfigStore is null)
        throw new RpcException(new Status(StatusCode.FailedPrecondition, "Pack config store is not configured."));

    var payloadStr = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
    var p = CaseInsensitive(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadStr));
    string? Field(string key) => p.TryGetValue(key, out var v) ? v?.ToString() : null;

    var pack = Field("pack") ?? Field("packName") ?? request.CorrelationId;
    var scope = Field("scope") ?? PackConfigScopes.App;

    // The scope must be either the shared app-level slot every reader (responder pack, LlmResponderNeuron,
    // Telegram transport) actually pulls from, or the caller's OWN resolved per-user slot — never an
    // arbitrary/other-user scope, per P6b.
    var configSession = await ResolveSessionAsync(Field("sessionId"));
    var callerOwnScope = configSession is not null ? PackConfigScopes.ForUser(configSession.UserId) : null;
    if (scope != PackConfigScopes.App && scope != callerOwnScope)
        throw new RpcException(new Status(StatusCode.PermissionDenied, $"Scope '{scope}' is not permitted for this caller."));

    var controlKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "pack", "packName", "scope", "sessionId", "buyerId", "userId", "synapseType", "eventName"
    };
    var values = p
        .Where(kv => !controlKeys.Contains(kv.Key))
        .ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? string.Empty);

    await packConfigStore.SetAsync(scope, pack, values);
    logger.LogInformation("Stored configuration for pack {Pack} ({FieldCount} fields).", pack, values.Count);

    // Non-secret notification only: subscribers learn config changed and re-PULL the values
    // point-to-point via GetPackConfig. The stored values (which may be secrets) are NOT broadcast.
    var notifyKey = string.IsNullOrWhiteSpace(request.CorrelationId)
        ? $"pack-configured-{scope}-{pack}"
        : request.CorrelationId;
    var notifyIngress = grains.GetGrain<IIngressNeuron>(notifyKey);
    await notifyIngress.IngestAsync("PackConfigured", new Dictionary<string, object?>
    {
        ["pack"] = pack,
        ["scope"] = scope
    });
    return request;
}
```

Change the `InoRequest` branch (currently lines 148-158):

```csharp
if (request.TypeName == nameof(InoRequest) || request.TypeName.Contains("InoRequest", StringComparison.OrdinalIgnoreCase))
{
    var payloadStr = System.Text.Encoding.UTF8.GetString(request.Payload.ToArray());
    var p = CaseInsensitive(System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, object?>>(payloadStr));
    var prompt = p.TryGetValue("prompt", out var pr) ? pr?.ToString() ?? "" : "";
    var sessionId = p.TryGetValue("sessionId", out var sid) ? sid?.ToString() : null;
    var inoSession = await ResolveSessionAsync(sessionId);

    var ino = grains.GetGrain<IInoNeuron>("ino-main");
    await ino.FireAsync(new InoRequest(prompt, inoSession?.SessionId));
    return request;
}
```

`DigitalBrain.Tests/Steps/ConfigFormSteps.cs` — the BDD scenario currently submits `ConfigurationProvided` under an arbitrary buyer scope with no session; route it through a real login instead. Change `WhenISubmitConfiguration` and `ThenTheStoreReturnsValues`:

```csharp
private string? _configScope;

[When(@"I submit configuration for the pack with token ""(.*)"", provider ""(.*)"", key ""(.*)""")]
public async Task WhenISubmitConfiguration(string token, string provider, string key)
{
    var gateway = new GatewayService(
        _cluster.GrainFactory,
        new ConfigurationBuilder().Build(),
        new HomeFeedBus(),
        new SignalEgressBus(),
        new FakeHostEnvironment(),
        NullLogger<GatewayService>.Instance,
        SharedConfigStore);

    await gateway.Send(new SynapseEnvelope
    {
        TypeName = nameof(LoginRequest),
        Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            username = Scope,
            password = "config-form-test-password",
            clientId = "test"
        }))
    }, TestServerCallContext.Create());

    var session = _cluster.GrainFactory.GetGrain<IUserSessionNeuron>("session-main");
    var sessionId = (await session.GetOutgoingTimelineAsync()).OfType<UserSessionCreated>().Last().SessionId;
    _configScope = PackConfigScopes.ForUser(new UserId(Scope));

    var values = new Dictionary<string, string>
    {
        ["telegram_token"] = token,
        ["llm_provider"] = provider,
        ["llm_key"] = key,
        ["pack"] = _packName,
        ["scope"] = _configScope,
        ["sessionId"] = sessionId
    };
    var payload = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(values);

    await gateway.Send(new SynapseEnvelope
    {
        TypeName = nameof(ConfigurationProvided),
        Payload = global::Google.Protobuf.ByteString.CopyFrom(payload)
    }, TestServerCallContext.Create());
}

[Then(@"the pack config store returns token ""(.*)"", provider ""(.*)"", key ""(.*)""")]
public async Task ThenTheStoreReturnsValues(string token, string provider, string key)
{
    var stored = await SharedConfigStore.GetAsync(_configScope!, _packName);
    Assert.Equal(token, stored["telegram_token"]);
    Assert.Equal(provider, stored["llm_provider"]);
    Assert.Equal(key, stored["llm_key"]);
}
```

(`_packName`, `Scope`, `SharedConfigStore`, `_cluster` are the file's existing fields — unchanged; `BuyerId: Scope` on line 95's `InstallFromMarketplace` call stays exactly as-is since that's a direct grain call bypassing the gateway entirely, unaffected by this task. `IUserSessionNeuron`, `LoginRequest`, `UserSessionCreated`, `UserId`, `PackConfigScopes` need `using DigitalBrain.Core;`, already present at the top of the file.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~GatewayServiceTests`
Expected: PASS (all tests, including the 2 new ones and every pre-existing test in the file — `Send_SurfaceDemoRequested...` calls `InstallFromMarketplace` directly on the marketplace grain, not through this gateway branch, so it's unaffected; the `ConfigurationProvided`-adjacent tests all live in `PackConfigPullTests.cs` and already pass `scope: "default"` explicitly, which still equals `PackConfigScopes.App`).

Then run the BDD suite: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~ConfigForm`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Kernel/Gateway/GatewayService.cs DigitalBrain.Tests/Gateway/GatewayServiceTests.cs DigitalBrain.Tests/Steps/ConfigFormSteps.cs
git commit -m "fix(gateway): stop trusting client-supplied buyerId/scope/sessionId identity fields"
```

---

## Stage S3 — Salesforce Per-User

### Task 6 (S3.1): Rekey Salesforce grains to `{userId}`; route the OAuth callback via a userId-prefixed state

**Files:**
- Modify: `DigitalBrain.Kernel/Salesforce/SalesforceAuthNeuron.cs:36,68` (state generation), `DigitalBrain.Kernel/Program.cs:310-328` (callback endpoint), `DigitalBrain.Kernel/Gateway/GatewayService.cs:94-103` (`AuthRequested` routing), `DigitalBrain.Kernel/Ino/InoNeuron.cs:112-128,243-296,368-403` (CRM grain key + credential-check resolution)
- Test: `DigitalBrain.Tests/Gateway/GatewayServiceTests.cs`

**Interfaces:**
- Consumes: `GatewayService.ResolveSessionAsync` (S2.4), `NeuronScope`/`Self.AsScope()` (S2.2).
- Produces: `SalesforceAuthNeuron`/`SalesforceCrmNeuron` now keyed by real userId in production; `InoNeuron.ResolveUserIdAsync(string?)` helper reused by S3.3's factory wiring indirectly (via the grain key it resolves to).

- [ ] **Step 1: Write the failing test**

Add to `DigitalBrain.Tests/Gateway/GatewayServiceTests.cs`:

```csharp
[Fact]
public async Task Send_SalesforceAuthRequested_Routes_To_The_Callers_Own_UserKeyed_Grain()
{
    var svc = NewService();

    await svc.Send(new SynapseEnvelope
    {
        TypeName = nameof(LoginRequest),
        Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            username = "salesforce-test-user",
            password = "correct horse battery staple",
            clientId = "test"
        }))
    }, TestContext());

    var session = Grain<IUserSessionNeuron>("session-main");
    var sessionId = (await session.GetOutgoingTimelineAsync()).OfType<UserSessionCreated>().Single().SessionId;

    await svc.Send(new SynapseEnvelope
    {
        TypeName = SalesforceSignals.AuthRequested,
        Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            sessionId
        }))
    }, TestContext());

    var auth = Grain<ISalesforceAuthNeuron>("salesforce-test-user");
    var timeline = await auth.GetOutgoingTimelineAsync();
    var form = Assert.Single(timeline.OfType<UiSurface>(), surface => surface.Kind == ConfigFormSurface.Kind);
    Assert.Equal("salesforce", form.Props["pack"]);
    Assert.Equal(sessionId, form.Props["sessionId"]);
}

[Fact]
public async Task Send_SalesforceAuthRequested_Without_A_Session_Is_Rejected()
{
    var svc = NewService();

    var ex = await Assert.ThrowsAsync<RpcException>(() => svc.Send(new SynapseEnvelope
    {
        TypeName = SalesforceSignals.AuthRequested,
        Payload = global::Google.Protobuf.ByteString.CopyFrom(System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            sessionId = "not-a-real-session"
        }))
    }, TestContext()));

    Assert.Equal(StatusCode.Unauthenticated, ex.StatusCode);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~Send_SalesforceAuthRequested`
Expected: FAIL — today the gateway always routes to the literal `"salesforce-auth-main"` grain regardless of session, so the first test finds nothing at `Grain<ISalesforceAuthNeuron>("salesforce-test-user")` and the second never throws.

- [ ] **Step 3: Write minimal implementation**

`DigitalBrain.Kernel/Gateway/GatewayService.cs` — change the Salesforce `AuthRequested` branch (currently lines 94-103):

```csharp
if (request.TypeName == SalesforceSignals.AuthRequested || request.TypeName.Contains(SalesforceSignals.AuthRequested, StringComparison.OrdinalIgnoreCase))
{
    var authProps = PayloadProps(request);
    var authSessionId = authProps.TryGetValue("sessionId", out var authSid) ? authSid?.ToString() : null;
    var authSession = await ResolveSessionAsync(authSessionId);
    if (authSession is null)
        throw new RpcException(new Status(StatusCode.Unauthenticated, "Sign in before connecting Salesforce."));

    var salesforceUserId = authSession.UserId.Value;
    var auth = grains.GetGrain<ISalesforceAuthNeuron>(salesforceUserId);
    var signal = new Signal(SalesforceSignals.AuthRequested, authProps)
    {
        Receiver = new NeuronId(salesforceUserId)
    };
    await auth.DeliverAsync(signal);
    return request;
}
```

`DigitalBrain.Kernel/Salesforce/SalesforceAuthNeuron.cs` — change the state line inside `StartOAuthAsync` (currently line 68):

```csharp
var state = $"{Self.AsScope().UserId.Value}:{Guid.NewGuid():N}";
```

`DigitalBrain.Kernel/Program.cs` — change the callback endpoint (currently lines 310-328) and add a local function near `SalesforceCallbackUri`:

```csharp
app.MapGet(SalesforceClientFactory.DefaultCallbackPath, async (
    HttpRequest request,
    IGrainFactory grains) =>
{
    var state = request.Query["state"].FirstOrDefault();
    var callback = new SalesforceOAuthCallback(
        Code: request.Query["code"].FirstOrDefault(),
        State: state,
        Error: request.Query["error"].FirstOrDefault(),
        ErrorDescription: request.Query["error_description"].FirstOrDefault(),
        FallbackRedirectUri: SalesforceCallbackUri(request));

    var auth = grains.GetGrain<ISalesforceAuthNeuron>(SalesforceOAuthUserIdFromState(state));
    var result = await auth.CompleteOAuthAsync(callback);

    return Results.Content(
        SalesforceCallbackPage(result.Title, result.Message),
        "text/html",
        statusCode: result.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);
});
```

```csharp
static string SalesforceCallbackUri(HttpRequest request) =>
    new UriBuilder(request.Scheme, request.Host.Host, request.Host.Port ?? -1, SalesforceClientFactory.DefaultCallbackPath)
        .Uri
        .ToString();

// The callback is a cold, unauthenticated GET from Salesforce's redirect — it carries no session, only
// code/state. StartOAuthAsync prefixes state with its own userId ("{userId}:{nonce}") so this endpoint can
// route to the right per-user grain; the grain still exact-matches the FULL state string against its own
// stored pending value, so CSRF protection is unchanged. This is NOT D-MU2's encrypted state (deferred to
// S4) — a malformed/tampered state just fails to route to a real pending flow and fails closed.
static string SalesforceOAuthUserIdFromState(string? state)
{
    if (string.IsNullOrWhiteSpace(state)) return "salesforce-auth-unknown";
    var separatorIndex = state.LastIndexOf(':');
    return separatorIndex > 0 ? state[..separatorIndex] : "salesforce-auth-unknown";
}
```

`DigitalBrain.Kernel/Ino/InoNeuron.cs` — change the Salesforce-adjacent methods. Replace `HandleAsync(Signal signal)` (currently lines 112-141), keeping the Google half unchanged:

```csharp
public async Task HandleAsync(Signal signal)
{
    if (signal.Name == "PackConfigured" &&
        signal.Props.TryGetValue("pack", out var pack) &&
        string.Equals(pack?.ToString(), SalesforceClientFactory.PackName, StringComparison.OrdinalIgnoreCase))
    {
        var pendingSalesforce = _pendingSalesforceRequest
            ?? IncomingJournal.OfType<InoRequest>().LastOrDefault(r => IsSalesforceIntent(r.Prompt));

        if (pendingSalesforce is not null)
        {
            var salesforceUserId = await ResolveUserIdAsync(pendingSalesforce.SessionId);
            if (await HasSalesforceCredentialAsync(salesforceUserId))
            {
                _pendingSalesforceRequest = null;
                await FetchSalesforceAccountsAsync(pendingSalesforce, salesforceUserId);
            }
        }

        return;
    }

    if (signal.Name != GoogleSignals.AuthCompleted)
        return;

    var pending = _pendingGmailRequest
        ?? IncomingJournal.OfType<InoRequest>().LastOrDefault(r => IsGmailIntent(r.Prompt));

    if (pending is null || !await HasGoogleCredentialAsync())
        return;

    _pendingGmailRequest = null;
    await FetchRecentGmailAsync(pending);
}

private async Task<string> ResolveUserIdAsync(string? sessionId)
{
    if (string.IsNullOrWhiteSpace(sessionId))
        return UserId.Anonymous.Value;

    var session = GrainFactory.GetGrain<IUserSessionNeuron>("session-main");
    var state = await session.GetSessionAsync(sessionId);
    return state?.UserId.Value ?? UserId.Anonymous.Value;
}
```

Replace `HandleSalesforceIntentAsync` (currently lines 243-255):

```csharp
private async Task HandleSalesforceIntentAsync(InoRequest req)
{
    var salesforceUserId = await ResolveUserIdAsync(req.SessionId);
    if (!await HasSalesforceCredentialAsync(salesforceUserId))
    {
        _pendingSalesforceRequest = req;
        var reply = "Salesforce credentials are required to query CRM records.";
        await FireAsync(new InoResponse(req.Prompt, reply, []));
        await DeliverSalesforceCredentialSurfaceAsync(req.SessionId);
        return;
    }

    await FetchSalesforceAccountsAsync(req, salesforceUserId);
}
```

Replace `HasSalesforceCredentialAsync` (currently lines 280-296) — merge App + per-user scope, since password-flow credentials (and, pre-S3.2, everything) still live at App scope:

```csharp
private async Task<bool> HasSalesforceCredentialAsync(string userId)
{
    var store = ServiceProvider.GetService<IPackConfigStore>();
    if (store is null)
        return false;

    try
    {
        var appValues = await store.GetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName);
        var userValues = await store.GetAsync(PackConfigScopes.ForUser(new UserId(userId)), SalesforceClientFactory.PackName);
        var merged = new Dictionary<string, string>(appValues, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in userValues)
            merged[key] = value;
        return SalesforceClientFactory.HasUsableCredential(merged);
    }
    catch (Exception ex)
    {
        Logger.LogDebug(ex, "Salesforce credential check failed.");
        return false;
    }
}
```

Replace `FetchSalesforceAccountsAsync` (currently lines 368-403) to take and use the resolved userId as the CRM grain key:

```csharp
private async Task FetchSalesforceAccountsAsync(InoRequest req, string salesforceUserId)
{
    var maxResults = SalesforceResultCount(req.Prompt);
    await Broadcast(new Signal(SalesforceSignals.QueryRequested, new Dictionary<string, object?>
    {
        ["prompt"] = req.Prompt,
        ["sessionId"] = req.SessionId,
        ["maxResults"] = maxResults
    }));

    string[] records;
    try
    {
        var salesforce = GrainFactory.GetGrain<ISalesforceCrmNeuron>(salesforceUserId);
        records = await salesforce.ListAccountsAsync(maxResults);
    }
    catch (Exception ex) when (IsSalesforceIntegrationFailure(ex))
    {
        Logger.LogWarning(ex, "Salesforce query failed after credentials were configured.");
        var failureReply = SalesforceFailureReply(ex);
        await FireAsync(new InoResponse(req.Prompt, failureReply, []));
        await DeliverReplySurfaceAsync(failureReply, req.SessionId);
        await DeliverSalesforceCredentialSurfaceAsync(req.SessionId);
        return;
    }

    await Broadcast(new Signal(SalesforceSignals.QueryResultsReady, new Dictionary<string, object?>
    {
        ["sessionId"] = req.SessionId,
        ["count"] = records.Length
    }));

    var reply = SalesforceReplyText(records);
    await FireAsync(new InoResponse(req.Prompt, reply, []));
    await DeliverSalesforceRecordsSurfaceAsync(records, req.SessionId);
}
```

(`using DigitalBrain.Core;` is already present at the top of `InoNeuron.cs`, so `UserId`, `PackConfigScopes`, `IUserSessionNeuron` all resolve without new usings.)

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~GatewayServiceTests`
Expected: PASS — including the 2 new tests and the file's pre-existing `Send_SalesforceAuthRequested_Routes_To_SalesforceAuthNeuron` test, which you must first update to log in and use the resulting sessionId (same shape as the new test above), since it previously asserted against a session-less request.

Then run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~InoNeuronChatSurfaceTests`
Expected: PASS unchanged — `SalesforceIntent_WithoutCredential_Emits_Credential_Form_Surface` fires an `InoRequest` with a session id that doesn't resolve to a real session, so `ResolveUserIdAsync` falls back to `"anonymous"`; `HasSalesforceCredentialAsync("anonymous")` still finds no credential (nothing stored anywhere), same outcome as before.

Then run: `dotnet test DigitalBrain.Salesforce.Tests --filter FullyQualifiedName~SalesforceOAuthCrossSiloTests|FullyQualifiedName~SalesforceAuthNeuronTests`
Expected: PASS unchanged — these tests call the grain directly with their own literal test keys (e.g. `"salesforce-auth-main"`, `"salesforce-auth-test-complete"`), which remain valid arbitrary grain keys; `Self.AsScope().UserId.Value` just becomes that literal string, and state comparison is still exact-match on the full string, so behavior is unaffected.

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Kernel/Salesforce/SalesforceAuthNeuron.cs DigitalBrain.Kernel/Program.cs DigitalBrain.Kernel/Gateway/GatewayService.cs DigitalBrain.Kernel/Ino/InoNeuron.cs DigitalBrain.Tests/Gateway/GatewayServiceTests.cs
git commit -m "feat(salesforce): key auth/CRM grains by userId; route OAuth callback via userId-prefixed state"
```

---

### Task 7 (S3.2): Split pack-config scope by tier in `SalesforceAuthNeuron` (fixes P2)

**Files:**
- Modify: `DigitalBrain.Kernel/Salesforce/SalesforceAuthNeuron.cs:36-100` (`StartOAuthAsync`), `:102-175` (`CompleteOAuthAsync`)
- Test: `DigitalBrain.Salesforce.Tests/SalesforceAuthNeuronTests.cs`

**Interfaces:**
- Consumes: `PackConfigScopes.App` / `PackConfigScopes.ForUser(UserId)` (S2.2), `Self.AsScope()` (S2.2).

- [ ] **Step 1: Write the failing test**

The existing tests in `SalesforceAuthNeuronTests.cs` already exercise this path; 3 of them assert against the wrong (pre-split) scope and need fixing to prove the split actually happened. Change `OAuthStart_Pending_State_Survives_Concurrent_Credential_Write` (currently reads `writer.ReadPackAsync(SalesforceClientFactory.DefaultScope, ...)` for pending state at line 122):

```csharp
[Fact]
public async Task OAuthStart_Pending_State_Survives_Concurrent_Credential_Write()
{
    var writer = Grain<ISalesforceConnectedAppConfigWriter>("salesforce-connected-app-writer-race");
    await writer.StoreConnectedAppConfigAsync();

    var auth = Grain<ISalesforceAuthNeuron>("salesforce-auth-test-race");
    await auth.DeliverAsync(new Signal(SalesforceSignals.AuthRequested, new Dictionary<string, object?>
    {
        ["sessionId"] = "session-race",
        ["callbackPath"] = SalesforceClientFactory.DefaultCallbackPath,
        [SalesforceClientFactory.RedirectUriKey] = "http://localhost:8081/salesforce-callback"
    })
    { Receiver = new NeuronId("salesforce-auth-test-race") });

    await auth.GetOutgoingTimelineAsync();

    await writer.StoreConnectedAppConfigAsync();

    var pending = await writer.ReadPackAsync(PackConfigScopes.ForUser(new UserId("salesforce-auth-test-race")), SalesforceClientFactory.OAuthPendingPackName);
    Assert.True(pending.ContainsKey(SalesforceClientFactory.OAuthStateKey));
    Assert.True(pending.ContainsKey(SalesforceClientFactory.OAuthCodeVerifierKey));
}
```

Change `CompleteOAuthAsync_With_Valid_Code_And_State_Stores_Tokens_And_Succeeds` (currently reads token/pending back from `DefaultScope` at lines 157,160):

```csharp
var stored = await writer.ReadPackAsync(PackConfigScopes.ForUser(new UserId("salesforce-auth-test-complete")), SalesforceClientFactory.PackName);
Assert.Equal("fake-access-token", stored[SalesforceClientFactory.AccessTokenKey]);

var pendingAfter = await writer.ReadPackAsync(PackConfigScopes.ForUser(new UserId("salesforce-auth-test-complete")), SalesforceClientFactory.OAuthPendingPackName);
Assert.False(pendingAfter.ContainsKey(SalesforceClientFactory.OAuthStateKey));
```

Change `CompleteOAuthAsync_With_Mismatched_State_Fails_Without_Exchanging_Code` (currently reads at line 189):

```csharp
var stored = await writer.ReadPackAsync(PackConfigScopes.ForUser(new UserId("salesforce-auth-test-mismatch")), SalesforceClientFactory.PackName);
Assert.False(stored.ContainsKey(SalesforceClientFactory.AccessTokenKey));
```

(`using DigitalBrain.Core;` is already present at the top of `SalesforceAuthNeuronTests.cs`.)

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.Salesforce.Tests --filter FullyQualifiedName~SalesforceAuthNeuronTests`
Expected: FAIL — `OAuthStart_Pending_State_Survives_Concurrent_Credential_Write` and `CompleteOAuthAsync_With_Valid_Code_And_State_Stores_Tokens_And_Succeeds` fail because today's code still writes pending/tokens under `DefaultScope`, not `PackConfigScopes.ForUser(...)`, so the new scope reads come back empty.

- [ ] **Step 3: Write minimal implementation**

`DigitalBrain.Kernel/Salesforce/SalesforceAuthNeuron.cs` — change `StartOAuthAsync` (currently lines 36-100):

```csharp
private async Task StartOAuthAsync(IReadOnlyDictionary<string, object?> props)
{
    var store = ServiceProvider.GetRequiredService<IPackConfigStore>();
    var existing = await store.GetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName);
    var values = new Dictionary<string, string>(existing, StringComparer.OrdinalIgnoreCase);

    CopyIfPresent(props, values, SalesforceClientFactory.ClientIdKey);
    CopyIfPresent(props, values, SalesforceClientFactory.ClientSecretKey);
    CopyIfPresent(props, values, SalesforceClientFactory.LoginUrlKey);
    CopyIfPresent(props, values, SalesforceClientFactory.ApiVersionKey);
    CopyIfPresent(props, values, SalesforceClientFactory.OAuthScopeKey);

    var configuredRedirectUri = ServiceProvider
        .GetService<IConfiguration>()?["DigitalBrain:Salesforce:RedirectUri"];
    if (!string.IsNullOrWhiteSpace(configuredRedirectUri))
        values[SalesforceClientFactory.RedirectUriKey] = configuredRedirectUri.Trim();
    else
        CopyIfPresent(props, values, SalesforceClientFactory.RedirectUriKey);

    if (!values.TryGetValue(SalesforceClientFactory.RedirectUriKey, out var redirectUri) ||
        string.IsNullOrWhiteSpace(redirectUri))
    {
        redirectUri = SalesforceClientFactory.DefaultRedirectUri;
        values[SalesforceClientFactory.RedirectUriKey] = redirectUri;
    }

    if (!SalesforceClientFactory.HasConnectedAppConfig(values))
    {
        await PublishCredentialFormAsync(props, SalesforceClientFactory.MissingConnectedAppConfigMessage);
        return;
    }

    var state = $"{Self.AsScope().UserId.Value}:{Guid.NewGuid():N}";
    var codeVerifier = SalesforceClientFactory.CreatePkceCodeVerifier();
    var codeChallenge = SalesforceClientFactory.CreatePkceCodeChallenge(codeVerifier);

    string url;
    try
    {
        url = SalesforceClientFactory.CreateAuthorizationUrl(values, redirectUri, state, codeChallenge);
    }
    catch (InvalidOperationException ex)
    {
        await PublishCredentialFormAsync(props, ex.Message);
        return;
    }

    await store.SetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName, values);

    // Pending PKCE state lives under the caller's OWN per-user scope (I3/I4): each user's grain activation
    // is the single writer of its own pending slot, so two users starting OAuth concurrently never clobber
    // each other (the pre-S3 clobbering race this comment used to describe was between config-form writes and
    // OAuth-start writes to the SAME shared slot; per-user scoping removes that shared slot entirely).
    var userScope = PackConfigScopes.ForUser(Self.AsScope().UserId);
    await store.SetAsync(userScope, SalesforceClientFactory.OAuthPendingPackName, new Dictionary<string, string>
    {
        [SalesforceClientFactory.OAuthStateKey] = state,
        [SalesforceClientFactory.OAuthCodeVerifierKey] = codeVerifier
    });
    await Broadcast(new Signal(SalesforceSignals.AuthUrl, new Dictionary<string, object?>
    {
        ["provider"] = "salesforce",
        ["url"] = url
    }));
}
```

Change `CompleteOAuthAsync` (currently lines 102-175):

```csharp
public async Task<SalesforceOAuthCallbackResult> CompleteOAuthAsync(SalesforceOAuthCallback callback)
{
    if (!string.IsNullOrWhiteSpace(callback.Error))
    {
        return new SalesforceOAuthCallbackResult(
            false,
            "Salesforce login failed",
            $"{callback.Error}: {callback.ErrorDescription}".TrimEnd(':', ' '));
    }

    if (string.IsNullOrWhiteSpace(callback.Code))
    {
        return new SalesforceOAuthCallbackResult(
            false,
            "Salesforce login failed",
            "The callback did not include an authorization code.");
    }

    var store = ServiceProvider.GetRequiredService<IPackConfigStore>();
    var userScope = PackConfigScopes.ForUser(Self.AsScope().UserId);
    var appValues = await store.GetAsync(PackConfigScopes.App, SalesforceClientFactory.PackName);
    var pending = await store.GetAsync(userScope, SalesforceClientFactory.OAuthPendingPackName);

    if (pending.TryGetValue(SalesforceClientFactory.OAuthStateKey, out var expectedState) &&
        !string.IsNullOrWhiteSpace(expectedState) &&
        !string.Equals(expectedState, callback.State, StringComparison.Ordinal))
    {
        return new SalesforceOAuthCallbackResult(
            false,
            "Salesforce login failed",
            "The callback state did not match the pending login.");
    }

    var redirectUri = appValues.TryGetValue(SalesforceClientFactory.RedirectUriKey, out var storedRedirectUri)
        ? storedRedirectUri
        : callback.FallbackRedirectUri;

    try
    {
        var exchangeValues = new Dictionary<string, string>(appValues, StringComparer.OrdinalIgnoreCase);
        if (pending.TryGetValue(SalesforceClientFactory.OAuthCodeVerifierKey, out var pendingCodeVerifier))
            exchangeValues[SalesforceClientFactory.OAuthCodeVerifierKey] = pendingCodeVerifier;

        var handler = ServiceProvider.GetService<HttpMessageHandler>();
        var tokenValues = await SalesforceClientFactory.ExchangeAuthorizationCodeAsync(exchangeValues, callback.Code, redirectUri, handler);
        var userTokenValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in tokenValues)
            userTokenValues[key] = value;

        await store.SetAsync(userScope, SalesforceClientFactory.PackName, userTokenValues);
        await store.SetAsync(userScope, SalesforceClientFactory.OAuthPendingPackName, new Dictionary<string, string>());

        await Broadcast(new Signal("PackConfigured", new Dictionary<string, object?>
        {
            ["pack"] = SalesforceClientFactory.PackName,
            ["scope"] = userScope
        }));
        await Broadcast(new Signal(SalesforceSignals.AuthCompleted, new Dictionary<string, object?>
        {
            ["provider"] = "salesforce",
            ["pack"] = SalesforceClientFactory.PackName,
            ["scope"] = userScope
        }));

        return new SalesforceOAuthCallbackResult(
            true,
            "Salesforce connected",
            "You can close this browser tab and return to DigitalBrain.");
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        Logger.LogWarning(ex, "Salesforce OAuth callback failed.");
        return new SalesforceOAuthCallbackResult(false, "Salesforce login failed", ex.GetBaseException().Message);
    }
}
```

Add `using DigitalBrain.Core;` to `SalesforceAuthNeuron.cs` if not already present (it already is, line 2).

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DigitalBrain.Salesforce.Tests --filter FullyQualifiedName~SalesforceAuthNeuronTests`
Expected: PASS (all 6 tests).

Then run: `dotnet test DigitalBrain.Salesforce.Tests --filter FullyQualifiedName~SalesforceOAuthCrossSiloTests`
Expected: PASS unchanged — same grain activation on both silo references, so its own `userScope` derived from `Self.AsScope()` is self-consistent regardless of what the literal key string means.

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Kernel/Salesforce/SalesforceAuthNeuron.cs DigitalBrain.Salesforce.Tests/SalesforceAuthNeuronTests.cs
git commit -m "feat(salesforce): split pack-config scope — connected-app config stays app-level, tokens move per-user"
```

---

### Task 8 (S3.3): D-MU7 — lazy per-user `ISalesforceApiClientFactory` (fixes the eager-throw-on-activation gap)

**Files:**
- Create: `DigitalBrain.Salesforce/ISalesforceApiClientFactory.cs`, `DigitalBrain.Salesforce/SalesforceApiClientFactory.cs`
- Modify: `DigitalBrain.Salesforce/SalesforceClientFactory.cs` (add a shared merge helper), `DigitalBrain.Kernel/Salesforce/SalesforceCrmNeuron.cs`, `DigitalBrain.Kernel/Program.cs:152-158`, `DigitalBrain.Kernel/Ino/InoNeuron.cs` (dedupe `HasSalesforceCredentialAsync`'s merge logic against the new shared helper — Task 6 wrote its own inline copy because this helper didn't exist yet; this task removes that duplication now that it does)
- Test: `DigitalBrain.Salesforce.Tests/SalesforceCrmNeuronTests.cs`, `DigitalBrain.Tests/Ino/InoNeuronChatSurfaceTests.cs` (`InoNeuronAuthenticatedSalesforceFailureTests`)

**Interfaces:**
- Consumes: `NeuronScope`, `PackConfigScopes` (S2.2), `Self.AsScope()`.
- Produces: `SalesforceClientFactory.GetMergedScopedValuesAsync(IPackConfigStore store, NeuronScope scope) → Task<IReadOnlyDictionary<string,string>>`, reused by both `SalesforceApiClientFactory` and `InoNeuron.HasSalesforceCredentialAsync`. `ISalesforceApiClientFactory.CreateAsync(NeuronScope scope) → Task<ISalesforceApiClient>`, registered as a DI singleton, called explicitly per-method by `SalesforceCrmNeuron`.

- [ ] **Step 1: Write the failing test**

Rewrite `DigitalBrain.Salesforce.Tests/SalesforceCrmNeuronTests.cs` in full:

```csharp
using DigitalBrain.Core;
using DigitalBrain.TestKit;
using Microsoft.Extensions.DependencyInjection;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Salesforce.Tests;

public class SalesforceCrmNeuronTests : NeuronTestBase
{
    private readonly RecordingSalesforceApiClient _client = new();

    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services =>
            services.AddSingleton<ISalesforceApiClientFactory>(new FakeSalesforceApiClientFactory(_client)));

    [Fact]
    public async Task QueryAsync_Delegates_To_Api_Client_For_Its_Own_Scope()
    {
        var crm = Grain<ISalesforceCrmNeuron>("alice");

        var records = await crm.QueryAsync("SELECT Id, Name FROM Account LIMIT 1");

        Assert.Equal("SELECT Id, Name FROM Account LIMIT 1", _client.Queries.Single());
        Assert.Equal(["{\"Name\":\"Acme\"}"], records);
        Assert.Equal("alice", _client.ScopesRequested.Single().UserId.Value);
    }

    [Fact]
    public async Task ListAccountsAsync_Delegates_To_Api_Client_For_Its_Own_Scope()
    {
        var crm = Grain<ISalesforceCrmNeuron>("bob");

        await crm.ListAccountsAsync(3);

        Assert.Equal(3, _client.AccountListLimits.Single());
        Assert.Equal("bob", _client.ScopesRequested.Single().UserId.Value);
    }
}

internal sealed class FakeSalesforceApiClientFactory(ISalesforceApiClient client) : ISalesforceApiClientFactory
{
    public Task<ISalesforceApiClient> CreateAsync(NeuronScope scope)
    {
        if (client is RecordingSalesforceApiClient recording)
            recording.ScopesRequested.Add(scope);
        return Task.FromResult(client);
    }
}

internal sealed class RecordingSalesforceApiClient : ISalesforceApiClient
{
    public List<string> Queries { get; } = [];
    public List<int> AccountListLimits { get; } = [];
    public List<NeuronScope> ScopesRequested { get; } = [];

    public Task<string[]> QueryAsync(string soql, CancellationToken ct)
    {
        Queries.Add(soql);
        return Task.FromResult(new[] { "{\"Name\":\"Acme\"}" });
    }

    public Task<string[]> ListAccountsAsync(int maxResults, CancellationToken ct)
    {
        AccountListLimits.Add(maxResults);
        return Task.FromResult(new[] { "{\"Name\":\"Acme\"}" });
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.Salesforce.Tests --filter FullyQualifiedName~SalesforceCrmNeuronTests`
Expected: FAIL to compile — `ISalesforceApiClientFactory` doesn't exist yet, and `SalesforceCrmNeuron`'s constructor still requires `ISalesforceApiClient` directly.

- [ ] **Step 3: Write minimal implementation**

`DigitalBrain.Salesforce/SalesforceClientFactory.cs` — add a shared merge helper next to `CreateApiClientAsync` (this is the one place both the CRM client factory and `InoNeuron`'s credential check will read the App+per-user split from, so the merge logic exists exactly once):

```csharp
public static async Task<IReadOnlyDictionary<string, string>> GetMergedScopedValuesAsync(
    IPackConfigStore store,
    NeuronScope scope)
{
    var appValues = await store.GetAsync(PackConfigScopes.App, PackName).ConfigureAwait(false);
    var userValues = await store.GetAsync(PackConfigScopes.ForUser(scope.UserId), PackName).ConfigureAwait(false);

    var merged = new Dictionary<string, string>(appValues, StringComparer.OrdinalIgnoreCase);
    foreach (var (key, value) in userValues)
        merged[key] = value;

    return merged;
}
```

(add `using DigitalBrain.Core;` to `SalesforceClientFactory.cs`'s using block for `NeuronScope`/`PackConfigScopes` — it currently only has `using DigitalBrain.Core.Config;`.)

Create `DigitalBrain.Salesforce/ISalesforceApiClientFactory.cs`:

```csharp
using DigitalBrain.Core;

namespace DigitalBrain.Salesforce;

public interface ISalesforceApiClientFactory
{
    Task<ISalesforceApiClient> CreateAsync(NeuronScope scope);
}
```

Create `DigitalBrain.Salesforce/SalesforceApiClientFactory.cs`:

```csharp
using DigitalBrain.Core;
using DigitalBrain.Core.Config;

namespace DigitalBrain.Salesforce;

public sealed class SalesforceApiClientFactory(IPackConfigStore store) : ISalesforceApiClientFactory
{
    public async Task<ISalesforceApiClient> CreateAsync(NeuronScope scope)
    {
        var merged = await SalesforceClientFactory.GetMergedScopedValuesAsync(store, scope).ConfigureAwait(false);
        return new SalesforceApiClient(await SalesforceClientFactory.CreateForceClientAsync(merged).ConfigureAwait(false));
    }
}
```

Now dedupe `InoNeuron.HasSalesforceCredentialAsync` (written in Task 6 with its own inline App+User merge, before this shared helper existed) to call the same helper:

```csharp
private async Task<bool> HasSalesforceCredentialAsync(string userId)
{
    var store = ServiceProvider.GetService<IPackConfigStore>();
    if (store is null)
        return false;

    try
    {
        var merged = await SalesforceClientFactory.GetMergedScopedValuesAsync(store, new NeuronScope(new UserId(userId), null));
        return SalesforceClientFactory.HasUsableCredential(merged);
    }
    catch (Exception ex)
    {
        Logger.LogDebug(ex, "Salesforce credential check failed.");
        return false;
    }
}
```

`DigitalBrain.Kernel/Salesforce/SalesforceCrmNeuron.cs` — replace the whole file:

```csharp
using DigitalBrain.Core;
using DigitalBrain.Salesforce;

namespace DigitalBrain.Kernel.Salesforce;

[GrainType("digitalbrain.salesforce.crm.v1")]
public class SalesforceCrmNeuron(
    ILogger<SalesforceCrmNeuron> logger,
    NeuronJournals journals,
    ISalesforceApiClientFactory apiClientFactory)
    : Neuron(logger, journals), ISalesforceCrmNeuron
{
    public async Task<string[]> QueryAsync(string soql, CancellationToken ct = default)
    {
        var client = await apiClientFactory.CreateAsync(Self.AsScope());
        return await client.QueryAsync(soql, ct);
    }

    public async Task<string[]> ListAccountsAsync(int maxResults = 20, CancellationToken ct = default)
    {
        var client = await apiClientFactory.CreateAsync(Self.AsScope());
        return await client.ListAccountsAsync(maxResults, ct);
    }
}
```

`DigitalBrain.Kernel/Program.cs` — replace the eager Salesforce client registration (currently lines 152-158):

```csharp
// Salesforce CRM REST API client: built lazily per call from the shared app-level connected-app config
// ("default" scope) merged with the calling grain's own per-user token scope ("user:{userId}"). Singleton
// (not scoped) because, unlike the old eager factory, it no longer resolves a client at grain-activation
// time — SalesforceCrmNeuron calls CreateAsync explicitly per method with its own NeuronScope, so "user
// hasn't connected yet" is a normal per-call condition instead of an activation-time throw.
builder.Services.AddSingleton<DigitalBrain.Salesforce.ISalesforceApiClientFactory, DigitalBrain.Salesforce.SalesforceApiClientFactory>();
```

`DigitalBrain.Tests/Ino/InoNeuronChatSurfaceTests.cs` — in `InoNeuronAuthenticatedSalesforceFailureTests.ConfigureSilo` (currently registers `services.AddSingleton<ISalesforceApiClient, FailingSalesforceApiClient>();`), swap to the factory shape:

```csharp
public sealed class InoNeuronAuthenticatedSalesforceFailureTests : NeuronTestBase
{
    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.AddPackConfigStore(blobsForKeyRing: null);
            services.AddSingleton<ISalesforceApiClientFactory>(new FailingSalesforceApiClientFactory());
        });
```

Add next to `FailingSalesforceApiClient` (near the bottom of the file):

```csharp
internal sealed class FailingSalesforceApiClientFactory : ISalesforceApiClientFactory
{
    public Task<ISalesforceApiClient> CreateAsync(NeuronScope scope) =>
        Task.FromResult<ISalesforceApiClient>(new FailingSalesforceApiClient());
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DigitalBrain.Salesforce.Tests --filter FullyQualifiedName~SalesforceCrmNeuronTests`
Expected: PASS (both tests, including the scope assertions).

Then run: `dotnet test DigitalBrain.Tests --filter FullyQualifiedName~InoNeuronChatSurfaceTests`
Expected: PASS — `SalesforceIntent_WithInvalidCredential_Renders_Clear_Error_And_Credential_Form` still reaches the CRM neuron (the password-flow credential the test seeds lives at App scope, which `HasSalesforceCredentialAsync`'s merge picks up regardless of the resolved — here, `"anonymous"` — userId), gets the `FailingSalesforceApiClient`'s exception, and shows the same error surface as before.

Then run the full Salesforce suite: `dotnet test DigitalBrain.Salesforce.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Salesforce/ISalesforceApiClientFactory.cs DigitalBrain.Salesforce/SalesforceApiClientFactory.cs DigitalBrain.Salesforce/SalesforceClientFactory.cs DigitalBrain.Kernel/Salesforce/SalesforceCrmNeuron.cs DigitalBrain.Kernel/Program.cs DigitalBrain.Kernel/Ino/InoNeuron.cs DigitalBrain.Salesforce.Tests/SalesforceCrmNeuronTests.cs DigitalBrain.Tests/Ino/InoNeuronChatSurfaceTests.cs
git commit -m "feat(salesforce): replace eager constructor-injected API client with a lazy per-user factory (D-MU7)"
```

---

### Task 9 (S3.4): Two-user cross-contamination acceptance test (S3 acceptance criterion)

**Files:**
- Create: `DigitalBrain.Salesforce.Tests/SalesforceTwoUserOAuthIsolationTests.cs`

**Interfaces:**
- Consumes: everything from S3.1-S3.3 — this task only adds a test, no production code.

- [ ] **Step 1: Write the failing test**

Create `DigitalBrain.Salesforce.Tests/SalesforceTwoUserOAuthIsolationTests.cs`, modeled on `SalesforceOAuthCrossSiloTests.cs`'s shape but with two distinct user-keyed grains in the same silo instead of two silos of the same key:

```csharp
using DigitalBrain.Core;
using DigitalBrain.Kernel.Config;
using DigitalBrain.TestKit;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Salesforce.Tests;

public class SalesforceTwoUserOAuthIsolationTests : NeuronTestBase
{
    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services =>
        {
            services.AddPackConfigStore(blobsForKeyRing: null);
            services.AddSingleton<HttpMessageHandler>(
                new FakeSalesforceTokenHandler("fake-access-token", "https://fake.my.salesforce.com"));
        });

    [Fact]
    public async Task Two_Users_Interleaved_OAuth_Flows_Do_Not_Cross_Contaminate()
    {
        var writer = Grain<ISalesforceConnectedAppConfigWriter>("salesforce-connected-app-writer-two-user");
        await writer.StoreConnectedAppConfigAsync();

        var alice = Grain<ISalesforceAuthNeuron>("alice");
        var bob = Grain<ISalesforceAuthNeuron>("bob");

        await alice.DeliverAsync(new Signal(SalesforceSignals.AuthRequested, new Dictionary<string, object?>
        {
            ["sessionId"] = "session-alice",
            ["callbackPath"] = SalesforceClientFactory.DefaultCallbackPath,
            [SalesforceClientFactory.RedirectUriKey] = "http://localhost:8081/salesforce-callback"
        })
        { Receiver = new NeuronId("alice") });

        await bob.DeliverAsync(new Signal(SalesforceSignals.AuthRequested, new Dictionary<string, object?>
        {
            ["sessionId"] = "session-bob",
            ["callbackPath"] = SalesforceClientFactory.DefaultCallbackPath,
            [SalesforceClientFactory.RedirectUriKey] = "http://localhost:8081/salesforce-callback"
        })
        { Receiver = new NeuronId("bob") });

        var aliceAuthUrl = Assert.Single((await alice.GetOutgoingTimelineAsync()).OfType<Signal>(), s => s.Name == SalesforceSignals.AuthUrl);
        var bobAuthUrl = Assert.Single((await bob.GetOutgoingTimelineAsync()).OfType<Signal>(), s => s.Name == SalesforceSignals.AuthUrl);
        var aliceState = FakeSalesforceTokenHandler.ExtractQueryValue((string)aliceAuthUrl.Props["url"]!, "state");
        var bobState = FakeSalesforceTokenHandler.ExtractQueryValue((string)bobAuthUrl.Props["url"]!, "state");

        Assert.StartsWith("alice:", aliceState);
        Assert.StartsWith("bob:", bobState);

        // Wrong-user callback: Alice's grain, Bob's state — fails closed, no exchange, no cross-write.
        var crossResult = await alice.CompleteOAuthAsync(new SalesforceOAuthCallback(
            Code: "some-code", State: bobState, Error: null, ErrorDescription: null,
            FallbackRedirectUri: "http://localhost:8081/salesforce-callback"));
        Assert.False(crossResult.Success);
        Assert.Equal("The callback state did not match the pending login.", crossResult.Message);

        var aliceResult = await alice.CompleteOAuthAsync(new SalesforceOAuthCallback(
            Code: "alice-code", State: aliceState, Error: null, ErrorDescription: null,
            FallbackRedirectUri: "http://localhost:8081/salesforce-callback"));
        var bobResult = await bob.CompleteOAuthAsync(new SalesforceOAuthCallback(
            Code: "bob-code", State: bobState, Error: null, ErrorDescription: null,
            FallbackRedirectUri: "http://localhost:8081/salesforce-callback"));

        Assert.True(aliceResult.Success);
        Assert.True(bobResult.Success);

        var aliceTokens = await writer.ReadPackAsync(PackConfigScopes.ForUser(new UserId("alice")), SalesforceClientFactory.PackName);
        var bobTokens = await writer.ReadPackAsync(PackConfigScopes.ForUser(new UserId("bob")), SalesforceClientFactory.PackName);
        Assert.Equal("fake-access-token", aliceTokens[SalesforceClientFactory.AccessTokenKey]);
        Assert.Equal("fake-access-token", bobTokens[SalesforceClientFactory.AccessTokenKey]);

        // Neither user's pending PKCE state is readable from the other's scope.
        var alicePendingFromBobScope = await writer.ReadPackAsync(PackConfigScopes.ForUser(new UserId("bob")), SalesforceClientFactory.OAuthPendingPackName);
        Assert.False(alicePendingFromBobScope.ContainsKey(SalesforceClientFactory.OAuthStateKey) && string.Equals(alicePendingFromBobScope.GetValueOrDefault(SalesforceClientFactory.OAuthStateKey), aliceState));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DigitalBrain.Salesforce.Tests --filter FullyQualifiedName~SalesforceTwoUserOAuthIsolationTests`
Expected: PASS already if S3.1-S3.3 landed correctly — this task is a pure regression/acceptance check, not new production behavior. If it fails, it means one of the earlier S3 tasks has a gap (e.g. state prefix missing, scope split incomplete) — treat a failure here as a signal to revisit S3.1/S3.2, not as license to weaken this test.

- [ ] **Step 3: N/A — this task adds no production code.**

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DigitalBrain.Salesforce.Tests`
Expected: PASS (full Salesforce test project green).

- [ ] **Step 5: Commit**

```bash
git add DigitalBrain.Salesforce.Tests/SalesforceTwoUserOAuthIsolationTests.cs
git commit -m "test(salesforce): prove two-user OAuth flows never cross-contaminate tokens or pending state"
```

---

## Final verification (both stages)

- [ ] Run the full suite: `dotnet test`
- [ ] Grep for stale hardcoded Salesforce singleton keys that should no longer exist in production code paths: `grep -rn "salesforce-auth-main\|salesforce-main" DigitalBrain.Kernel/` — expect zero hits outside of comments/docs.
- [ ] Per this repo's standing instruction: run the Aspire integration path (`aspire run` / the Aspire MCP tools) and confirm the app still boots and the Salesforce "Connect" button + Home feed both work end-to-end before considering this plan complete.
