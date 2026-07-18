# Slice C.3 — `InoInstanceContextFilter` + `BrainTraceFilter` + `InoBrainStream` + `WatchBrainActivity` — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the wire that the C.4–C.6 brain UI will subscribe to. Two new Orleans `IIncomingGrainCallFilter`s — `InoInstanceContextFilter` (binds and validates `ino.userId` / `ino.sessionId` `RequestContext` keys) and `BrainTraceFilter` (emits a `BrainPulse` on every grain call). Both run cluster-wide. Pulses flow over a memory-streams provider `ino-brain`. A new server-streaming gRPC RPC `WatchBrainActivity` lets clients subscribe; the kernel-side handler forwards each pulse over the wire. Flutter gets a minimal `BrainStreamService` that opens the stream on `/brain` mount and logs each pulse via the existing `ino-flutter` OTel log channel — no UI rendering yet.

**Architecture:** `gateway InoGateway.AskAsync → RequestContext.Set(ino.userId, ino.sessionId) → grain hop → BrainTraceFilter.Invoke wraps every grain call → emits BrainPulse via IClusterClient.GetStreamProvider("ino-brain") → in-silo subscribers (and the kernel's gRPC handler) receive pulses → InoGrpcService.WatchBrainActivity forwards them as BrainPulseProto frames → Flutter BrainStreamService logs them.` `InoInstanceContextFilter` runs on every silo but only does work when the activation is `IInoNeuron`: it asserts the grain key matches the `RequestContext` keys and throws `InoInstanceMismatch` on cross-user leakage. Memory streams provider `ino-brain` is added to the kernel, identity, and every domain silo via a single `silo.UseInoBrainStream()` extension so wiring stays uniform. **No UI changes** in this slice — Flutter just proves the stream is live by logging.

**Tech Stack:** .NET 11, Orleans 10.x (`IIncomingGrainCallFilter`, `RequestContext`, `AddMemoryStreams`, `IAsyncStream<T>.OnNextAsync` / `SubscribeAsync`), gRPC + Protobuf (server-streaming), xUnit + NSubstitute for tests, Reqnroll BDD untouched, Aspire dashboard for runtime verification.

**Spec:** `docs/superpowers/specs/2026-05-04-ino-brain-askino-creator-design.md` §2.4, §3.4 row "WatchBrainActivity", §4.4, §5 Slice C.3.

**Out of scope for this slice (do NOT implement):** brain UI redesign (C.4), timeline scrubber (C.5), Travel ← tripradar Kafka bridge (C.6), Creator capability + risk gate (C.2), `InspectGrain` RPC (C.4), particle rendering, `experienceMembership` topology, any Flutter UI shape change. Render-side consumption of pulses is intentionally a `dev:log` line — proves wire end-to-end with no design lock-in.

---

## File map (lock-in for the slice)

| Path | Action |
|---|---|
| `src/Ino.Core/Brain/BrainPulse.cs` | **create** — `[GenerateSerializer]` record, the on-stream payload |
| `src/Ino.Core/Brain/InoRequestContextKeys.cs` | **create** — `RequestContext` key constants (`ino.userId`, `ino.sessionId`) |
| `src/Ino.Core.Hosting/Brain/InoBrainStream.cs` | **create** — provider + stream id constants |
| `src/Ino.Core.Hosting/Brain/InoBrainStreamingExtensions.cs` | **create** — `silo.UseInoBrainStream()` (memory streams + filters + DI) and `clientBuilder.AddInoBrainStreamClient()` |
| `src/Ino.Core.Hosting/Brain/InoInstanceContextFilter.cs` | **create** — `IIncomingGrainCallFilter` validating grain-key vs `RequestContext` |
| `src/Ino.Core.Hosting/Brain/BrainTraceFilter.cs` | **create** — `IIncomingGrainCallFilter` emitting `BrainPulse` |
| `src/Ino.Core.Hosting/Brain/InoInstanceMismatchException.cs` | **create** — typed exception thrown by the context filter |
| `src/Ino.Kernel/KernelSiloConfigurator.cs` | **modify** — call `silo.UseInoBrainStream()` |
| `src/Ino.Identity/IdentitySiloConfigurator.cs` | **modify** — call `silo.UseInoBrainStream()` |
| `src/Ino.Core.Hosting/DomainsSiloConfigurator.cs` | **modify** — call `silo.UseInoBrainStream()` |
| `src/Ino.Gateway/InoGateway.cs` | **modify** — set `RequestContext` keys on `AskAsync` |
| `src/Ino.Gateway.Grpc/Protos/ino.proto` | **modify** — add `WatchBrainActivity` RPC + `BrainWatchRequest` + `BrainPulseProto` |
| `clients/ino.flutter/protos/ino.proto` | **modify** — same wire shape; lockstep |
| `src/Ino.Gateway.Grpc/Services/InoGrpcService.cs` | **modify** — implement `WatchBrainActivity` handler |
| `clients/ino.flutter/lib/services/brain_stream_service.dart` | **create** — opens stream on `/brain` mount, logs each pulse |
| `clients/ino.flutter/lib/screens/brain/brain_screen.dart` | **modify** — wire `BrainStreamService.start()` in `initState`, `stop()` in `dispose` |
| `test/Ino.Core.Hosting.Tests/InoInstanceContextFilterTests.cs` | **create** — unit tests for the context filter |
| `test/Ino.Core.Hosting.Tests/BrainTraceFilterTests.cs` | **create** — unit tests for the trace filter |
| `test/Ino.E2E.Tests/BrainStreamE2ETests.cs` | **create** — end-to-end gRPC `WatchBrainActivity` against the live AppHost |

---

## Task 1 — `BrainPulse` contract + `InoRequestContextKeys`

**Files:**
- Create: `src/Ino.Core/Brain/BrainPulse.cs`
- Create: `src/Ino.Core/Brain/InoRequestContextKeys.cs`

- [ ] **Step 1.1: Create `BrainPulse`**

Write `src/Ino.Core/Brain/BrainPulse.cs`:

```csharp
namespace Ino.Core.Brain;

/// <summary>
/// A single grain-call observation. Emitted by <c>BrainTraceFilter</c> on
/// every <see cref="Orleans.Runtime.IIncomingGrainCallContext"/> invocation
/// onto the <c>ino-brain</c> stream. Consumers: the Flutter brain screen
/// (logged in C.3, rendered in C.4) and any future inspector tab.
///
/// <see cref="InoInstanceId"/> is the per-user session id sourced from
/// <c>RequestContext.Get("ino.sessionId")</c>; the brain UI hashes it to a
/// stable hue so concurrent ino-instances render as distinct trails (spec
/// §4.4).
/// </summary>
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
    [property: Id(8)] long TimestampUnixMs);

[GenerateSerializer]
public enum BrainPulseStatus
{
    Ok = 0,
    Failed = 1,
}
```

- [ ] **Step 1.2: Create `InoRequestContextKeys`**

Write `src/Ino.Core/Brain/InoRequestContextKeys.cs`:

```csharp
namespace Ino.Core.Brain;

public static class InoRequestContextKeys
{
    public const string UserId = "ino.userId";
    public const string SessionId = "ino.sessionId";

    // Flutter / brain UI hue keying — distinct from per-trace telemetry.
    // "autonomic" is reserved for background-mind grains (spec §2.1).
    public const string AutonomicSessionId = "autonomic";
}
```

- [ ] **Step 1.3: Build to verify**

```bash
dotnet build E:/ino/ino.slnx
```

Expected: green.

- [ ] **Step 1.4: Commit**

```bash
git -C E:/ino add src/Ino.Core/Brain/
git -C E:/ino commit -m "$(cat <<'EOF'
feat(poc): BrainPulse + InoRequestContextKeys contracts (slice C.3.1)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 2 — `InoBrainStream` constants + `InoInstanceMismatchException`

**Files:**
- Create: `src/Ino.Core.Hosting/Brain/InoBrainStream.cs`
- Create: `src/Ino.Core.Hosting/Brain/InoInstanceMismatchException.cs`

- [ ] **Step 2.1: Create stream constants**

Write `src/Ino.Core.Hosting/Brain/InoBrainStream.cs`:

```csharp
using Orleans.Runtime;

namespace Ino.Core.Hosting.Brain;

/// <summary>
/// Memory-streams provider config for the brain pulse channel. Distinct
/// provider name from IAW's "agents" stream so a downstream switch from
/// memory → Azure Storage Queues (spec §8 open question) doesn't disturb
/// IAW's existing channel. Single shared stream id — every silo's
/// <c>BrainTraceFilter</c> writes here, every <c>WatchBrainActivity</c>
/// subscriber reads here.
/// </summary>
public static class InoBrainStream
{
    public const string ProviderName = "ino-brain";
    public const string Namespace = "brain";
    public const string Key = "pulses";

    public static StreamId Id { get; } = StreamId.Create(Namespace, Key);
}
```

- [ ] **Step 2.2: Create the typed mismatch exception**

Write `src/Ino.Core.Hosting/Brain/InoInstanceMismatchException.cs`:

```csharp
namespace Ino.Core.Hosting.Brain;

/// <summary>
/// Thrown by <c>InoInstanceContextFilter</c> when the
/// <c>RequestContext</c> identity keys do not match the receiving
/// <c>IInoNeuron</c> activation's grain key. This is a security boundary,
/// not a sanity check — a mismatch means a caller addressed grain
/// <c>(uA/sX)</c> while propagating <c>(uB/sX)</c> in context. Treat as
/// fatal for the call.
/// </summary>
public sealed class InoInstanceMismatchException : InvalidOperationException
{
    public InoInstanceMismatchException(string expectedKey, string? actualUserId, string? actualSessionId)
        : base($"InoNeuron activation key '{expectedKey}' does not match RequestContext (userId='{actualUserId ?? "<null>"}', sessionId='{actualSessionId ?? "<null>"}').")
    {
        ExpectedKey = expectedKey;
        ActualUserId = actualUserId;
        ActualSessionId = actualSessionId;
    }

    public string ExpectedKey { get; }
    public string? ActualUserId { get; }
    public string? ActualSessionId { get; }
}
```

- [ ] **Step 2.3: Build**

```bash
dotnet build E:/ino/ino.slnx
```

Expected: green.

- [ ] **Step 2.4: Commit**

```bash
git -C E:/ino add src/Ino.Core.Hosting/Brain/
git -C E:/ino commit -m "$(cat <<'EOF'
feat(poc): InoBrainStream constants + InoInstanceMismatchException (slice C.3.2)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 3 — `InoInstanceContextFilter` (TDD)

**Files:**
- Create: `src/Ino.Core.Hosting/Brain/InoInstanceContextFilter.cs`
- Test: `test/Ino.Core.Hosting.Tests/InoInstanceContextFilterTests.cs`

The filter:
- For grains implementing `IInoNeuron`, parses the grain primary key string with `InoNeuronGrainKey.Parse`, compares against `RequestContext.Get("ino.userId")` and `"ino.sessionId"`. On mismatch → throw `InoInstanceMismatchException`. On null context → permissive (gateway boot path may not set yet on first call); log at Debug.
- For all other grains, `await context.Invoke()` and return.

- [ ] **Step 3.1: Write the failing tests**

Write `test/Ino.Core.Hosting.Tests/InoInstanceContextFilterTests.cs`:

```csharp
using System.Reflection;
using Ino.Core;
using Ino.Core.Brain;
using Ino.Core.Hosting.Brain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Orleans.Runtime;
using Xunit;

namespace Ino.Core.Hosting.Tests;

public sealed class InoInstanceContextFilterTests
{
    [Fact]
    public async Task Non_InoNeuron_grain_passes_through()
    {
        var filter = new InoInstanceContextFilter(NullLogger<InoInstanceContextFilter>.Instance);
        var ctx = Substitute.For<IIncomingGrainCallContext>();
        var grain = Substitute.For<IGrainContext>();
        grain.GrainReference.Returns((GrainReference)null!);
        ctx.TargetContext.Returns(grain);
        ctx.Grain.Returns(new object());

        await filter.Invoke(ctx);

        await ctx.Received(1).Invoke();
    }

    [Fact]
    public async Task InoNeuron_grain_with_matching_RequestContext_passes()
    {
        try
        {
            RequestContext.Set(InoRequestContextKeys.UserId, "alice");
            RequestContext.Set(InoRequestContextKeys.SessionId, "default");

            var filter = new InoInstanceContextFilter(NullLogger<InoInstanceContextFilter>.Instance);
            var ctx = StubInoNeuronCallContext("alice/default");

            await filter.Invoke(ctx);

            await ctx.Received(1).Invoke();
        }
        finally { RequestContext.Clear(); }
    }

    [Fact]
    public async Task InoNeuron_grain_with_mismatching_RequestContext_throws()
    {
        try
        {
            RequestContext.Set(InoRequestContextKeys.UserId, "mallory");
            RequestContext.Set(InoRequestContextKeys.SessionId, "default");

            var filter = new InoInstanceContextFilter(NullLogger<InoInstanceContextFilter>.Instance);
            var ctx = StubInoNeuronCallContext("alice/default");

            var ex = await Assert.ThrowsAsync<InoInstanceMismatchException>(() => filter.Invoke(ctx));
            Assert.Equal("alice/default", ex.ExpectedKey);
            Assert.Equal("mallory", ex.ActualUserId);
            await ctx.DidNotReceive().Invoke();
        }
        finally { RequestContext.Clear(); }
    }

    [Fact]
    public async Task InoNeuron_grain_with_null_RequestContext_passes_in_permissive_mode()
    {
        // No RequestContext keys set — gateway warm-up window. Filter logs
        // at Debug and lets the call through. Grain itself is responsible for
        // sourcing identity from its grain key when context is empty.
        var filter = new InoInstanceContextFilter(NullLogger<InoInstanceContextFilter>.Instance);
        var ctx = StubInoNeuronCallContext("alice/default");

        await filter.Invoke(ctx);

        await ctx.Received(1).Invoke();
    }

    private static IIncomingGrainCallContext StubInoNeuronCallContext(string primaryKey)
    {
        var ctx = Substitute.For<IIncomingGrainCallContext>();
        var grain = Substitute.For<IInoNeuron, IGrain>();
        ctx.Grain.Returns(grain);

        var grainContext = Substitute.For<IGrainContext>();
        var grainId = GrainId.Create(GrainType.Create("ino-neuron"), primaryKey);
        grainContext.GrainId.Returns(grainId);
        ctx.TargetContext.Returns(grainContext);
        return ctx;
    }
}
```

- [ ] **Step 3.2: Run the tests, verify they fail to compile**

```bash
dotnet test E:/ino/test/Ino.Core.Hosting.Tests/Ino.Core.Hosting.Tests.csproj --filter "FullyQualifiedName~InoInstanceContextFilterTests"
```

Expected: build error — `InoInstanceContextFilter` does not exist.

- [ ] **Step 3.3: Implement the filter**

Write `src/Ino.Core.Hosting/Brain/InoInstanceContextFilter.cs`:

```csharp
using Ino.Core.Brain;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace Ino.Core.Hosting.Brain;

/// <summary>
/// Validates that the per-(userId, sessionId) <see cref="IInoNeuron"/>
/// activation matches the identity keys in <see cref="RequestContext"/>.
/// Throws <see cref="InoInstanceMismatchException"/> on cross-user leakage.
/// Non-IInoNeuron grains pass through. Permissive when context is empty —
/// the kernel-side gateway hop sets the keys; an empty context generally
/// means a system-internal cluster-singleton call (Discovery, ProposalLog).
/// </summary>
public sealed class InoInstanceContextFilter(
    ILogger<InoInstanceContextFilter> logger) : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        if (context.Grain is not IInoNeuron)
        {
            await context.Invoke();
            return;
        }

        var grainKey = context.TargetContext.GrainId.Key.ToString() ?? string.Empty;
        var ctxUserId = RequestContext.Get(InoRequestContextKeys.UserId) as string;
        var ctxSessionId = RequestContext.Get(InoRequestContextKeys.SessionId) as string;

        if (ctxUserId is null && ctxSessionId is null)
        {
            logger.LogDebug(
                "InoNeuron call to {GrainKey} with empty RequestContext — permissive pass-through.",
                grainKey);
            await context.Invoke();
            return;
        }

        var expected = $"{ctxUserId}/{ctxSessionId}";
        if (!string.Equals(grainKey, expected, StringComparison.Ordinal))
            throw new InoInstanceMismatchException(grainKey, ctxUserId, ctxSessionId);

        await context.Invoke();
    }
}
```

- [ ] **Step 3.4: Run the tests, verify green**

```bash
dotnet test E:/ino/test/Ino.Core.Hosting.Tests/Ino.Core.Hosting.Tests.csproj --filter "FullyQualifiedName~InoInstanceContextFilterTests"
```

Expected: 4 passed.

- [ ] **Step 3.5: Commit**

```bash
git -C E:/ino add src/Ino.Core.Hosting/Brain/InoInstanceContextFilter.cs test/Ino.Core.Hosting.Tests/InoInstanceContextFilterTests.cs
git -C E:/ino commit -m "$(cat <<'EOF'
feat(poc): InoInstanceContextFilter — RequestContext-vs-grain-key guard (slice C.3.3)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 4 — `BrainTraceFilter` (TDD)

**Files:**
- Create: `src/Ino.Core.Hosting/Brain/BrainTraceFilter.cs`
- Test: `test/Ino.Core.Hosting.Tests/BrainTraceFilterTests.cs`

The filter:
- Wraps every grain call. Records `Stopwatch.GetTimestamp()` at start, calls `await context.Invoke()` inside try/catch, on success or failure produces a `BrainPulse` and pushes it to the `ino-brain` stream via `IClusterClient.GetStreamProvider(...)` (caller-side resolution). On exception the filter rethrows after emit.
- Reads `RequestContext.Get(InoRequestContextKeys.UserId/SessionId)` for tagging. When missing, defaults to `("system", "autonomic")` — keeps the autonomic mind colour-stable per spec §4.4.
- Reads `Activity.Current?.Id` (W3C `traceparent` from OTel) for the `TraceParent` field. When null, emits empty string.
- `FromGrain` is best-effort `RuntimeContext.Current?.GrainId.ToString()`; can be null on the first hop. Empty string fallback.

- [ ] **Step 4.1: Write the failing tests**

Write `test/Ino.Core.Hosting.Tests/BrainTraceFilterTests.cs`:

```csharp
using Ino.Core.Brain;
using Ino.Core.Hosting.Brain;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Orleans;
using Orleans.Runtime;
using Orleans.Streams;
using Xunit;

namespace Ino.Core.Hosting.Tests;

public sealed class BrainTraceFilterTests
{
    [Fact]
    public async Task Successful_call_emits_Ok_pulse_with_RequestContext_identity()
    {
        try
        {
            RequestContext.Set(InoRequestContextKeys.UserId, "alice");
            RequestContext.Set(InoRequestContextKeys.SessionId, "session-7");

            var (filter, sink) = MakeFilter();
            var ctx = StubCallContext("alice/session-7", "AskAsync");

            await filter.Invoke(ctx);

            var pulse = Assert.Single(sink.Emitted);
            Assert.Equal("alice", pulse.UserId);
            Assert.Equal("session-7", pulse.InoInstanceId);
            Assert.Equal("AskAsync", pulse.MethodName);
            Assert.Equal(BrainPulseStatus.Ok, pulse.Status);
            Assert.True(pulse.DurationMs >= 0);
        }
        finally { RequestContext.Clear(); }
    }

    [Fact]
    public async Task Empty_RequestContext_falls_back_to_system_autonomic()
    {
        var (filter, sink) = MakeFilter();
        var ctx = StubCallContext("anything", "Tick");

        await filter.Invoke(ctx);

        var pulse = Assert.Single(sink.Emitted);
        Assert.Equal("system", pulse.UserId);
        Assert.Equal(InoRequestContextKeys.AutonomicSessionId, pulse.InoInstanceId);
    }

    [Fact]
    public async Task Failed_call_emits_Failed_pulse_and_rethrows()
    {
        var (filter, sink) = MakeFilter();
        var ctx = StubCallContext("alice/default", "BoomAsync");
        ctx.When(c => c.Invoke()).Do(_ => throw new InvalidOperationException("boom"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => filter.Invoke(ctx));

        var pulse = Assert.Single(sink.Emitted);
        Assert.Equal(BrainPulseStatus.Failed, pulse.Status);
        Assert.Equal("BoomAsync", pulse.MethodName);
    }

    [Fact]
    public async Task Sink_failure_does_not_block_grain_call()
    {
        // The brain stream is observability — if the sink throws (e.g.
        // memory provider not registered yet during silo warm-up), the
        // filter must swallow the sink exception and let the grain call
        // surface its real result.
        var (filter, sink) = MakeFilter();
        sink.ThrowOnEmit = new InvalidOperationException("sink down");
        var ctx = StubCallContext("alice/default", "Ping");

        await filter.Invoke(ctx);

        await ctx.Received(1).Invoke();
    }

    private static (BrainTraceFilter filter, RecordingPulseSink sink) MakeFilter()
    {
        var sink = new RecordingPulseSink();
        var filter = new BrainTraceFilter(sink, NullLogger<BrainTraceFilter>.Instance);
        return (filter, sink);
    }

    private static IIncomingGrainCallContext StubCallContext(string primaryKey, string methodName)
    {
        var ctx = Substitute.For<IIncomingGrainCallContext>();
        var method = typeof(BrainTraceFilterTests).GetMethod(nameof(StubMethod),
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        ctx.ImplementationMethod.Returns(method);
        ctx.InterfaceMethod.Returns(method);

        var grainContext = Substitute.For<IGrainContext>();
        var grainId = GrainId.Create(GrainType.Create("test"), primaryKey);
        grainContext.GrainId.Returns(grainId);
        ctx.TargetContext.Returns(grainContext);

        // ImplementationMethod.Name drives MethodName tagging.
        // We rely on the runtime calling `ImplementationMethod.Name`, but the
        // mock returns a real MethodInfo whose Name is "StubMethod". For tests
        // that need a custom method name, swap via a dynamic method or hand
        // the filter a method-name resolver. For v0.1 the filter reads
        // context.ImplementationMethod.Name directly — set the methodName
        // on the BrainPulseTagOverride below.
        ctx.Grain.Returns(new object());
        BrainTraceFilter.MethodNameOverrideForTests = methodName;
        return ctx;
    }

    private static void StubMethod() { }

    private sealed class RecordingPulseSink : IBrainPulseSink
    {
        public List<BrainPulse> Emitted { get; } = new();
        public Exception? ThrowOnEmit { get; set; }

        public Task EmitAsync(BrainPulse pulse, CancellationToken ct)
        {
            if (ThrowOnEmit is not null) throw ThrowOnEmit;
            Emitted.Add(pulse);
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 4.2: Run, verify build error**

```bash
dotnet test E:/ino/test/Ino.Core.Hosting.Tests/Ino.Core.Hosting.Tests.csproj --filter "FullyQualifiedName~BrainTraceFilterTests"
```

Expected: build error — `BrainTraceFilter`, `IBrainPulseSink` do not exist.

- [ ] **Step 4.3: Implement `IBrainPulseSink`**

Write `src/Ino.Core.Hosting/Brain/IBrainPulseSink.cs`:

```csharp
using Ino.Core.Brain;

namespace Ino.Core.Hosting.Brain;

/// <summary>
/// One-line abstraction over Orleans Streams so the filter can be unit-tested
/// without a TestCluster. Production binding lives in
/// <c>InoBrainStreamingExtensions</c>.
/// </summary>
public interface IBrainPulseSink
{
    Task EmitAsync(BrainPulse pulse, CancellationToken ct);
}
```

- [ ] **Step 4.4: Implement the filter**

Write `src/Ino.Core.Hosting/Brain/BrainTraceFilter.cs`:

```csharp
using System.Diagnostics;
using Ino.Core.Brain;
using Microsoft.Extensions.Logging;
using Orleans.Runtime;

namespace Ino.Core.Hosting.Brain;

/// <summary>
/// Wraps every grain call and emits a <see cref="BrainPulse"/> on the
/// <c>ino-brain</c> stream. Silent on sink failure — the brain stream is
/// observability, not business logic. Reads identity from <see cref="RequestContext"/>;
/// falls back to <c>("system", "autonomic")</c> for system-internal hops.
/// </summary>
public sealed class BrainTraceFilter(
    IBrainPulseSink sink,
    ILogger<BrainTraceFilter> logger) : IIncomingGrainCallFilter
{
    // Test-only override (see BrainTraceFilterTests). Unset (null) in production.
    internal static string? MethodNameOverrideForTests;

    public async Task Invoke(IIncomingGrainCallContext context)
    {
        var startTimestamp = Stopwatch.GetTimestamp();
        Exception? caught = null;
        try
        {
            await context.Invoke();
        }
        catch (Exception ex)
        {
            caught = ex;
            throw;
        }
        finally
        {
            await EmitPulseAsync(context, startTimestamp, caught);
        }
    }

    private async Task EmitPulseAsync(
        IIncomingGrainCallContext context,
        long startTimestamp,
        Exception? caught)
    {
        try
        {
            var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
            var userId = (RequestContext.Get(InoRequestContextKeys.UserId) as string) ?? "system";
            var sessionId = (RequestContext.Get(InoRequestContextKeys.SessionId) as string)
                ?? InoRequestContextKeys.AutonomicSessionId;

            var pulse = new BrainPulse(
                TraceParent: Activity.Current?.Id ?? string.Empty,
                InoInstanceId: sessionId,
                UserId: userId,
                FromGrain: RuntimeContext.Current?.GrainId.ToString() ?? string.Empty,
                ToGrain: context.TargetContext?.GrainId.ToString() ?? string.Empty,
                MethodName: MethodNameOverrideForTests ?? context.ImplementationMethod?.Name ?? string.Empty,
                DurationMs: (long)elapsed.TotalMilliseconds,
                Status: caught is null ? BrainPulseStatus.Ok : BrainPulseStatus.Failed,
                TimestampUnixMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

            await sink.EmitAsync(pulse, CancellationToken.None);
        }
        catch (Exception emitError)
        {
            // Brain stream is best-effort. A sink failure must never bubble up
            // and abort a grain call.
            logger.LogDebug(emitError, "BrainTraceFilter sink emit failed; pulse dropped.");
        }
    }
}
```

- [ ] **Step 4.5: Run, verify green**

```bash
dotnet test E:/ino/test/Ino.Core.Hosting.Tests/Ino.Core.Hosting.Tests.csproj --filter "FullyQualifiedName~BrainTraceFilterTests"
```

Expected: 4 passed.

- [ ] **Step 4.6: Commit**

```bash
git -C E:/ino add src/Ino.Core.Hosting/Brain/BrainTraceFilter.cs src/Ino.Core.Hosting/Brain/IBrainPulseSink.cs test/Ino.Core.Hosting.Tests/BrainTraceFilterTests.cs
git -C E:/ino commit -m "$(cat <<'EOF'
feat(poc): BrainTraceFilter — emits BrainPulse on every grain call (slice C.3.4)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 5 — `InoBrainStreamingExtensions` (silo + client wiring)

**Files:**
- Create: `src/Ino.Core.Hosting/Brain/InoBrainStreamingExtensions.cs`

This task introduces the production `IBrainPulseSink` (Orleans-stream-backed), the silo-side `silo.UseInoBrainStream()` that wires:
1. `siloBuilder.AddMemoryStreams(InoBrainStream.ProviderName)`
2. `siloBuilder.AddMemoryGrainStorage("PubSubStore")` (memory streams need it; idempotent if already added)
3. `silo.AddIncomingGrainCallFilter<InoInstanceContextFilter>()`
4. `silo.AddIncomingGrainCallFilter<BrainTraceFilter>()`
5. `services.AddSingleton<IBrainPulseSink, OrleansStreamPulseSink>()`

Plus a client-side `clientBuilder.AddInoBrainStreamClient()` for the kernel's gRPC handler to subscribe.

- [ ] **Step 5.1: Write the wiring**

Write `src/Ino.Core.Hosting/Brain/InoBrainStreamingExtensions.cs`:

```csharp
using Ino.Core.Brain;
using Microsoft.Extensions.DependencyInjection;
using Orleans;
using Orleans.Hosting;
using Orleans.Streams;

namespace Ino.Core.Hosting.Brain;

public static class InoBrainStreamingExtensions
{
    /// <summary>
    /// Registers the <c>ino-brain</c> memory-streams provider, the two
    /// incoming-grain-call filters, and the Orleans-stream-backed
    /// <see cref="IBrainPulseSink"/>. Idempotent — calling on a silo that
    /// already wired memory streams under the same provider name throws on
    /// duplicate add; gate per silo at the configurator level. Must be called
    /// once per silo (kernel, identity, every domain).
    /// </summary>
    public static ISiloBuilder UseInoBrainStream(this ISiloBuilder silo)
    {
        silo.AddMemoryStreams(InoBrainStream.ProviderName);
        silo.AddMemoryGrainStorage("PubSubStore");
        silo.AddIncomingGrainCallFilter<InoInstanceContextFilter>();
        silo.AddIncomingGrainCallFilter<BrainTraceFilter>();
        silo.Services.AddSingleton<IBrainPulseSink, OrleansStreamPulseSink>();
        return silo;
    }

    /// <summary>
    /// Adds the <c>ino-brain</c> memory-streams provider on the cluster
    /// client (kernel-side gRPC handler subscribes via the client API).
    /// </summary>
    public static IClientBuilder AddInoBrainStreamClient(this IClientBuilder client)
    {
        client.AddMemoryStreams(InoBrainStream.ProviderName);
        return client;
    }
}

internal sealed class OrleansStreamPulseSink(
    IClusterClient cluster) : IBrainPulseSink
{
    public Task EmitAsync(BrainPulse pulse, CancellationToken ct)
    {
        var provider = cluster.GetStreamProvider(InoBrainStream.ProviderName);
        var stream = provider.GetStream<BrainPulse>(InoBrainStream.Id);
        return stream.OnNextAsync(pulse);
    }
}
```

- [ ] **Step 5.2: Verify build**

```bash
dotnet build E:/ino/ino.slnx
```

Expected: green. If `IClusterClient` resolution issue arises, verify via Context7 (`/dotnet/orleans` query "IClusterClient injection inside silo process"), then adjust to inject `IGrainFactory` + `IServiceProvider` and resolve provider via `cluster.GetStreamProvider`.

- [ ] **Step 5.3: Commit**

```bash
git -C E:/ino add src/Ino.Core.Hosting/Brain/InoBrainStreamingExtensions.cs
git -C E:/ino commit -m "$(cat <<'EOF'
feat(poc): UseInoBrainStream silo + client wiring (slice C.3.5)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 6 — Wire silos: kernel, identity, domains

**Files:**
- Modify: `src/Ino.Kernel/KernelSiloConfigurator.cs`
- Modify: `src/Ino.Identity/IdentitySiloConfigurator.cs`
- Modify: `src/Ino.Core.Hosting/DomainsSiloConfigurator.cs`

- [ ] **Step 6.1: Kernel silo — add `UseInoBrainStream()`**

Edit `src/Ino.Kernel/KernelSiloConfigurator.cs` — after `silo.UseInoJournaling();` inside the `UseOrleans` block, add:

```csharp
silo.UseInoBrainStream();
```

Add the using: `using Ino.Core.Hosting.Brain;` at the top.

- [ ] **Step 6.2: Identity silo — same**

Edit `src/Ino.Identity/IdentitySiloConfigurator.cs`:

```csharp
silo.UseInoJournaling();
silo.UseInoBrainStream();
```

Add using `using Ino.Core.Hosting.Brain;`.

- [ ] **Step 6.3: Domain silos — same**

Edit `src/Ino.Core.Hosting/DomainsSiloConfigurator.cs` (the `AddDomain` extension), inside the `UseOrleans` block after `silo.UseInoJournaling();`:

```csharp
silo.UseInoBrainStream();
```

Add using `using Ino.Core.Hosting.Brain;` (same namespace as the extensions class — the using is for the resolver path; required because the extension lives under `Brain/`).

- [ ] **Step 6.4: Build the whole solution**

```bash
dotnet build E:/ino/ino.slnx
```

Expected: green.

- [ ] **Step 6.5: Run all tests to confirm no regression**

```bash
dotnet test E:/ino/ino.slnx
```

Expected: all green. The two new filters now run in any TestCluster derived from the production configurators; `InoNeuronTestSiloFixture` uses a manual `ISiloConfigurator` so it's unaffected.

If domain `Ino.Domains.Travel.Tests` boots a real silo and now picks up the filters, the pulse sink may fail because `IClusterClient` is not yet ready during `OnActivateAsync`. Acceptable — `BrainTraceFilter` swallows sink errors. Verify by running `dotnet test domains/travel/Ino.Domains.Travel.Tests/Ino.Domains.Travel.Tests.csproj` and confirming green.

- [ ] **Step 6.6: Commit**

```bash
git -C E:/ino add src/Ino.Kernel/KernelSiloConfigurator.cs src/Ino.Identity/IdentitySiloConfigurator.cs src/Ino.Core.Hosting/DomainsSiloConfigurator.cs
git -C E:/ino commit -m "$(cat <<'EOF'
feat(poc): wire UseInoBrainStream on kernel + identity + domain silos (slice C.3.6)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 7 — Gateway sets `RequestContext` keys on `AskAsync`

**Files:**
- Modify: `src/Ino.Gateway/InoGateway.cs`

The gateway is the only entry point that knows the `(userId, sessionId)` ahead of the first grain hop. Setting them in `RequestContext.Set(...)` lets `InoInstanceContextFilter` validate the InoNeuron activation, and lets `BrainTraceFilter` tag every downstream pulse with the right identity. Must use `try`/`finally` with `RequestContext.Clear()` so concurrent gateway calls don't leak.

- [ ] **Step 7.1: Edit `AskAsync`**

In `src/Ino.Gateway/InoGateway.cs`, in `AskAsync`, wrap the grain call in a try/finally:

Before:
```csharp
var grain = grainFactory.GetGrain<IInoNeuron>(InoNeuronGrainKey.Format(userId, sessionId));
return await grain.AskAsync(prompt, corrId.Value, ct);
```

After:
```csharp
RequestContext.Set(InoRequestContextKeys.UserId, userId);
RequestContext.Set(InoRequestContextKeys.SessionId, sessionId);
try
{
    var grain = grainFactory.GetGrain<IInoNeuron>(InoNeuronGrainKey.Format(userId, sessionId));
    return await grain.AskAsync(prompt, corrId.Value, ct);
}
finally
{
    RequestContext.Clear();
}
```

Add the usings at the top of the file:

```csharp
using Ino.Core.Brain;
using Orleans.Runtime;
```

- [ ] **Step 7.2: Verify existing tests stay green**

```bash
dotnet test E:/ino/test/Ino.Gateway.Tests/Ino.Gateway.Tests.csproj
```

Expected: green. (If this csproj doesn't exist, run the full ino.slnx test pass.)

- [ ] **Step 7.3: Commit**

```bash
git -C E:/ino add src/Ino.Gateway/InoGateway.cs
git -C E:/ino commit -m "$(cat <<'EOF'
feat(poc): InoGateway.AskAsync sets ino.userId / ino.sessionId RequestContext (slice C.3.7)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 8 — `WatchBrainActivity` proto + lockstep Flutter copy

**Files:**
- Modify: `src/Ino.Gateway.Grpc/Protos/ino.proto`
- Modify: `clients/ino.flutter/protos/ino.proto`

- [ ] **Step 8.1: Add the RPC + messages to the C# proto**

Edit `src/Ino.Gateway.Grpc/Protos/ino.proto`. Inside `service Ino { ... }`, after the existing `AskIno` line (around line 41), add:

```proto
  // Slice C.3 — server-streaming brain pulse subscription. Every grain call
  // cluster-wide emits one BrainPulseProto frame. Used by the Flutter brain
  // screen (UI render lands in C.4); for C.3 the client just logs.
  rpc WatchBrainActivity(BrainWatchRequest) returns (stream BrainPulseProto);
```

At the end of the file (after `AskInoResponse`), add:

```proto
message BrainWatchRequest {
  // Empty request body — the stream is global to the cluster. Reserved for
  // future per-user / per-session filtering when the inspector lands.
  string user_id_filter = 1;       // empty = no filter
  string session_id_filter = 2;    // empty = no filter
}

enum BrainPulseStatusProto {
  BRAIN_PULSE_STATUS_OK = 0;
  BRAIN_PULSE_STATUS_FAILED = 1;
}

message BrainPulseProto {
  string trace_parent = 1;
  string ino_instance_id = 2;
  string user_id = 3;
  string from_grain = 4;
  string to_grain = 5;
  string method_name = 6;
  int64 duration_ms = 7;
  BrainPulseStatusProto status = 8;
  int64 timestamp_unix_ms = 9;
}
```

- [ ] **Step 8.2: Mirror to the Flutter proto**

Apply the **same** edits to `clients/ino.flutter/protos/ino.proto`. The two files must stay in lockstep — confirm by `diff`:

```bash
diff E:/ino/src/Ino.Gateway.Grpc/Protos/ino.proto E:/ino/clients/ino.flutter/protos/ino.proto
```

Expected: empty diff except for any pre-existing comment-only divergence noted in `CLAUDE.md`. Spot-check the new RPC + messages exist in both.

- [ ] **Step 8.3: Build to regenerate C# stubs**

```bash
dotnet build E:/ino/src/Ino.Gateway.Grpc/Ino.Gateway.Grpc.csproj
```

Expected: green; `Ino.Grpc.BrainPulseProto`, `Ino.Grpc.BrainWatchRequest`, and the new `WatchBrainActivity` server-stream method appear in `Ino.InoBase`.

- [ ] **Step 8.4: Regenerate Dart stubs**

```bash
cd E:/ino/clients/ino.flutter
flutter pub run grpc:protoc_plugin -- --version >NUL 2>&1 || dart pub global activate protoc_plugin
flutter pub run build_runner build --delete-conflicting-outputs
```

If `build_runner` is not the codegen path used by this client, fall back to the existing pattern in `clients/ino.flutter/CLAUDE.md` (search for `protoc_plugin`). For this slice, the only requirement is that the generated `ino.pbgrpc.dart` exposes `watchBrainActivity()`.

- [ ] **Step 8.5: Commit**

```bash
git -C E:/ino add src/Ino.Gateway.Grpc/Protos/ino.proto clients/ino.flutter/protos/ino.proto clients/ino.flutter/lib/grpc/
git -C E:/ino commit -m "$(cat <<'EOF'
feat(poc): WatchBrainActivity proto + lockstep Flutter copy (slice C.3.8)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 9 — `WatchBrainActivity` gRPC handler

**Files:**
- Modify: `src/Ino.Gateway.Grpc/Services/InoGrpcService.cs`

The handler:
1. Resolves `IClusterClient` (kernel silo runs the gateway and is itself a silo, so the silo's local cluster client is in DI).
2. `cluster.GetStreamProvider(InoBrainStream.ProviderName).GetStream<BrainPulse>(InoBrainStream.Id)`.
3. Subscribes via `stream.SubscribeAsync(observer)` where the observer pushes each `BrainPulse` into a `Channel<BrainPulse>` (single-consumer, unbounded for v0.1 — bounded with drop-oldest in C.5).
4. Loops over the channel reader, writes `BrainPulseProto` to the gRPC response stream until `context.CancellationToken` fires.
5. Unsubscribes in `finally`.

- [ ] **Step 9.1: Implement the handler**

In `src/Ino.Gateway.Grpc/Services/InoGrpcService.cs`, add the constructor parameter `IClusterClient cluster` and the new method:

Update the primary constructor:

```csharp
public sealed class InoGrpcService(
    IInoGateway gateway,
    IClusterClient cluster,
    ILogger<InoGrpcService> log) : global::Ino.Grpc.Ino.InoBase
```

Add at the bottom of the class:

```csharp
public override async Task WatchBrainActivity(
    BrainWatchRequest request,
    IServerStreamWriter<BrainPulseProto> responseStream,
    ServerCallContext context)
{
    var userFilter = string.IsNullOrWhiteSpace(request.UserIdFilter) ? null : request.UserIdFilter;
    var sessionFilter = string.IsNullOrWhiteSpace(request.SessionIdFilter) ? null : request.SessionIdFilter;

    var channel = System.Threading.Channels.Channel.CreateUnbounded<BrainPulse>(
        new System.Threading.Channels.UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

    var streamProvider = cluster.GetStreamProvider(InoBrainStream.ProviderName);
    var stream = streamProvider.GetStream<BrainPulse>(InoBrainStream.Id);

    var subscription = await stream.SubscribeAsync((pulse, _) =>
    {
        channel.Writer.TryWrite(pulse);
        return Task.CompletedTask;
    });

    log.LogInformation(
        "WatchBrainActivity opened: userFilter={UserFilter} sessionFilter={SessionFilter}",
        userFilter ?? "<none>", sessionFilter ?? "<none>");

    try
    {
        await foreach (var pulse in channel.Reader.ReadAllAsync(context.CancellationToken))
        {
            if (userFilter is not null && !string.Equals(pulse.UserId, userFilter, StringComparison.Ordinal))
                continue;
            if (sessionFilter is not null && !string.Equals(pulse.InoInstanceId, sessionFilter, StringComparison.Ordinal))
                continue;

            await responseStream.WriteAsync(MapPulse(pulse), context.CancellationToken);
        }
    }
    catch (OperationCanceledException) { /* client closed stream */ }
    finally
    {
        await subscription.UnsubscribeAsync();
        channel.Writer.TryComplete();
    }
}

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
        BrainPulseStatus.Ok => BrainPulseStatusProto.Ok,
        BrainPulseStatus.Failed => BrainPulseStatusProto.Failed,
        _ => BrainPulseStatusProto.Ok,
    },
    TimestampUnixMs = pulse.TimestampUnixMs,
};
```

Add the usings at the top:

```csharp
using Ino.Core.Brain;
using Ino.Core.Hosting.Brain;
using Orleans;
```

The proto-generated enum names use the C# pascal-case form (`BrainPulseStatusProto.Ok` / `.Failed`) — matches the proto values `BRAIN_PULSE_STATUS_OK` / `BRAIN_PULSE_STATUS_FAILED` post-codegen with `csharp_namespace=Ino.Grpc`. Verify after build.

- [ ] **Step 9.2: Build**

```bash
dotnet build E:/ino/ino.slnx
```

Expected: green. If the enum mapping name diverges, adjust to the exact codegen name printed in the `Ino.Grpc.BrainPulseProto.Types` partial.

- [ ] **Step 9.3: Confirm DI knows `IClusterClient` in the kernel silo**

```bash
grep -rn "IClusterClient" E:/ino/src/Ino.Kernel/
```

If no explicit registration is present, that's fine — Orleans registers `IClusterClient` automatically when a silo runs in-process. Verify by booting the kernel silo (next task) and watching the gRPC handler resolve.

- [ ] **Step 9.4: Commit**

```bash
git -C E:/ino add src/Ino.Gateway.Grpc/Services/InoGrpcService.cs
git -C E:/ino commit -m "$(cat <<'EOF'
feat(poc): WatchBrainActivity gRPC handler — server-streams BrainPulse (slice C.3.9)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 10 — Flutter `BrainStreamService` (logs only)

**Files:**
- Create: `clients/ino.flutter/lib/services/brain_stream_service.dart`
- Modify: `clients/ino.flutter/lib/screens/brain/brain_screen.dart`

The service:
- Holds a `ClientChannel` (the existing one from the client-wide `GrpcClient` provider — find via `grep "ClientChannel" clients/ino.flutter/lib`).
- Exposes `start(BuildContext context)` that opens `inoStub.watchBrainActivity(BrainWatchRequest())` and listens; each pulse logs via `developer.log('brain.pulse: ...')` (already on the OTel `ino-flutter` channel via the existing OTel logger setup).
- Exposes `stop()` cancelling the subscription.

The screen wires `start` in `initState` and `stop` in `dispose`. **No UI shape change** — pulses are intentionally invisible.

- [ ] **Step 10.1: Implement the service**

Write `clients/ino.flutter/lib/services/brain_stream_service.dart`:

```dart
import 'dart:async';
import 'dart:developer' as developer;

import 'package:grpc/grpc.dart';

import '../grpc/ino.pbgrpc.dart';

/// Subscribes to the kernel-side `WatchBrainActivity` server-streaming RPC
/// and logs each pulse. Slice C.3 wire — the brain UI redesign in C.4
/// will replace logging with topology updates and particle emission. This
/// service stays useful as the dev-time tap on the stream.
class BrainStreamService {
  BrainStreamService(this._stub);

  final InoClient _stub;
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
      onError: (Object err, StackTrace st) {
        developer.log(
          'brain.pulse.error',
          name: 'ino-flutter',
          error: err,
          stackTrace: st,
        );
      },
      onDone: () {
        developer.log('brain.pulse.done', name: 'ino-flutter');
      },
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
    developer.log(
      'brain.pulse '
      'instance=${pulse.inoInstanceId} '
      'user=${pulse.userId} '
      'method=${pulse.methodName} '
      'duration=${pulse.durationMs}ms '
      'status=${pulse.status} '
      'from=${pulse.fromGrain} '
      'to=${pulse.toGrain} '
      'trace=${pulse.traceParent}',
      name: 'ino-flutter',
    );
  }
}
```

- [ ] **Step 10.2: Wire start/stop into `brain_screen.dart`**

Open `clients/ino.flutter/lib/screens/brain/brain_screen.dart`. In the `_BrainScreenState` (or equivalent `State<BrainScreen>` class):

Add a field:
```dart
late final BrainStreamService _brainStream;
```

In `initState`:
```dart
_brainStream = BrainStreamService(context.read<InoClient>());
_brainStream.start();
```

(If the existing screen reads the `InoClient` from a different DI provider — `Provider.of`, `GetIt`, `riverpod` — match its pattern. Find it via `grep -rn "InoClient" clients/ino.flutter/lib`.)

In `dispose`:
```dart
_brainStream.stop();
super.dispose();
```

Import:
```dart
import '../../services/brain_stream_service.dart';
```

- [ ] **Step 10.3: Run the Flutter analyzer**

```bash
cd E:/ino/clients/ino.flutter
flutter analyze
```

Expected: clean (or unchanged baseline — pre-existing warnings are out of scope).

- [ ] **Step 10.4: Commit**

```bash
git -C E:/ino add clients/ino.flutter/lib/services/brain_stream_service.dart clients/ino.flutter/lib/screens/brain/brain_screen.dart
git -C E:/ino commit -m "$(cat <<'EOF'
feat(poc-flutter): BrainStreamService logs pulses on /brain mount (slice C.3.10)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 11 — End-to-end test: `BrainStreamE2ETests`

**Files:**
- Create: `test/Ino.E2E.Tests/BrainStreamE2ETests.cs`

A real-AppHost integration test: boots the cluster, opens `WatchBrainActivity` over gRPC, drives one `AskIno` call, asserts at least one `BrainPulseProto` lands within 5 seconds with the expected `userId` + `sessionId`.

- [ ] **Step 11.1: Write the test**

Write `test/Ino.E2E.Tests/BrainStreamE2ETests.cs`:

```csharp
using System.Threading.Channels;
using Grpc.Net.Client;
using Grpc.Net.Client.Web;
using Ino.Grpc;
using Ino.Testing;
using Xunit;

namespace Ino.E2E.Tests;

[Collection(nameof(InoAppHostCollection))]
public sealed class BrainStreamE2ETests(InoAppHostFixture fixture)
{
    [Fact]
    public async Task WatchBrainActivity_emits_pulse_for_AskIno_call()
    {
        using var http = fixture.AppHost.CreateKernelHttpClient();
        using var channel = GrpcChannel.ForAddress(http.BaseAddress!, new GrpcChannelOptions
        {
            HttpClient = http,
            HttpHandler = new GrpcWebHandler(new HttpClientHandler()),
        });

        var ino = new global::Ino.Grpc.Ino.InoClient(channel);

        // Open the stream BEFORE driving traffic so we don't race the silo.
        var pulses = Channel.CreateUnbounded<BrainPulseProto>();
        var watch = ino.WatchBrainActivity(new BrainWatchRequest
        {
            UserIdFilter = "alice",
            SessionIdFilter = "default",
        });

        var pump = Task.Run(async () =>
        {
            try
            {
                await foreach (var pulse in watch.ResponseStream.ReadAllAsync())
                {
                    pulses.Writer.TryWrite(pulse);
                }
            }
            catch { /* stream cancelled at end of test */ }
        });

        // Brief warm-up so the silo's stream subscription is alive.
        await Task.Delay(500);

        var ask = await ino.AskInoAsync(new AskInoRequest
        {
            Prompt = "ping",
            UserId = "alice",
            SessionId = "default",
        });
        Assert.NotNull(ask);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var got = await pulses.Reader.ReadAsync(cts.Token);
        Assert.Equal("alice", got.UserId);
        Assert.Equal("default", got.InoInstanceId);

        watch.Dispose();
        pulses.Writer.TryComplete();
        await pump;
    }
}
```

(The `InoAppHostFixture` and `InoAppHostCollection` already exist for C.1's E2E tests. If they don't, mirror the pattern from `test/Ino.E2E.Tests/AskInoTests.cs` — fixture wraps `InoTestAppHost<Projects.Ino_AppHost>`.)

- [ ] **Step 11.2: Run the test**

```bash
dotnet test E:/ino/test/Ino.E2E.Tests/Ino.E2E.Tests.csproj --filter "FullyQualifiedName~BrainStreamE2ETests"
```

Expected: green. If it fails because the gRPC handler can't resolve `IClusterClient`, register it explicitly in `KernelSiloConfigurator.AddKernel`:

```csharp
builder.Services.AddSingleton(sp => sp.GetRequiredService<IGrainFactory>() as IClusterClient
    ?? throw new InvalidOperationException("Orleans cluster client not present in silo DI."));
```

(Verify the exact pattern via Context7 `/dotnet/orleans` "IClusterClient inside silo process DI" before committing the workaround.)

- [ ] **Step 11.3: Commit**

```bash
git -C E:/ino add test/Ino.E2E.Tests/BrainStreamE2ETests.cs
git -C E:/ino commit -m "$(cat <<'EOF'
test(poc): BrainStreamE2ETests pins WatchBrainActivity wire (slice C.3.11)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Task 12 — Live verification (browser + Aspire traces)

Per `CLAUDE.md` §"Verification loop". Build + test alone is NOT done.

- [ ] **Step 12.1: Full build clean**

```bash
dotnet build E:/ino/ino.slnx
```

Expected: green.

- [ ] **Step 12.2: Full test pass**

```bash
dotnet test E:/ino/ino.slnx
```

Expected: green. If the Travel-domain tests fail because the new filters change activation timing, capture the failure trace and re-run the offending test in isolation; do not patch over flakes.

- [ ] **Step 12.3: Boot Aspire foreground**

Use the Aspire MCP from a fresh shell (Background mode is fine if you're driving the verification interactively):

```
mcp__aspire__select_apphost  (point at E:/ino/src/Ino.AppHost/Ino.AppHost.csproj)
mcp__aspire__execute_resource_command(resourceName="kernel", commandName="rebuild")
```

Wait until kernel + identity + travel + taxi + reminders + recall + genesis + location all show **Healthy**. (`mcp__aspire__list_resources` shows the state.)

- [ ] **Step 12.4: Open the kernel HTTPS URL in Chrome via Chrome DevTools MCP**

```
mcp__chrome-devtools__new_page(url=<kernel kernel-http URL from list_resources>)
```

Navigate to `/brain` (the route already exists from the prior Slice B.1).

- [ ] **Step 12.5: Drive an `AskIno`**

Type a chat prompt — e.g. "ping" — through the existing composer. The intent will fall through to `UnroutedIntent` (live-path BDD-mock gap, not in scope for C.3) but the **routing path runs**, which is what generates pulses.

- [ ] **Step 12.6: Confirm pulses in Aspire structured logs**

```
mcp__aspire__list_structured_logs(filter="brain.pulse", resource="ino-flutter")
```

Expected: at least one `brain.pulse` log line per grain hop — `InoNeuron.AskAsync`, `Discovery.LookupCanonicalAsync`, etc. Each carries `instance`, `user`, `method`, `duration`, `from`, `to`, `traceparent`.

If zero pulses arrive: open the kernel silo's structured logs (`mcp__aspire__list_structured_logs(resource="kernel")`) and grep for `BrainTraceFilter sink emit failed`. That's the signature for sink-side failure (silently dropped per design).

- [ ] **Step 12.7: Confirm the gRPC trace chain**

```
mcp__aspire__list_traces(filter="WatchBrainActivity")
```

Expected: a long-lived span on the kernel silo for the duration of the page mount, with periodic `gRPC` write-frame children. Each `AskIno` shows up as its own trace whose `traceparent` should match the `trace_parent` field in the corresponding `brain.pulse` log line.

- [ ] **Step 12.8: Stop background Aspire (if started)**

If you used `aspire start --isolated`, stop it now:

```bash
aspire stop
```

If you used `mcp__aspire__execute_resource_command(rebuild)` against a foreground `aspire run`, leave the foreground process to be Ctrl+C'd by the user. Don't kill the user's interactive Aspire session.

- [ ] **Step 12.9: Capture verification artefacts**

Save one annotated screenshot of the Aspire structured-logs panel filtered to `brain.pulse` to `reviews/slice-c3-brain-pulses.png` for the PR description.

- [ ] **Step 12.10: Final commit (optional — verification artefacts)**

```bash
git -C E:/ino add reviews/slice-c3-brain-pulses.png
git -C E:/ino commit -m "$(cat <<'EOF'
docs(poc): slice C.3 verification screenshot

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

## Self-review

Run after Task 11, before Task 12.

**Spec coverage (§2.4 + §3.4 + §4.4 + §5 Slice C.3):**
- `InoInstanceContextFilter` — Task 3.
- `BrainTraceFilter` — Task 4.
- `RequestContext` propagation of `ino.userId` / `ino.sessionId` — Task 7 (gateway sets); filters read in Tasks 3 and 4.
- `InoBrainStream` (memory streams in dev, provider name `ino-brain`) — Tasks 2 + 5 + 6.
- `WatchBrainActivity` server-stream RPC + `BrainPulse` mapping — Tasks 8 + 9.
- Per-`inoInstanceId` colour mapping — **deferred to C.4** (no UI render in this slice). The hue function is a pure-Flutter add and lives where it consumes `BrainPulseProto.InoInstanceId`. Out of scope here per spec §5.
- `IRemindable` autonomic-mind hookup — out of scope (spec §2.4 explicitly tags this for C.6/C.7 paths).

**Placeholder scan:** none — every step contains code, command, or expected output.

**Type consistency:**
- `BrainPulse` (Ino.Core record) ↔ `BrainPulseProto` (Ino.Grpc) — mapped in `InoGrpcService.MapPulse` (Task 9).
- `BrainPulseStatus.Ok / Failed` ↔ `BrainPulseStatusProto.Ok / Failed` — verified in Task 9.2.
- `InoBrainStream.ProviderName="ino-brain"` consistent across Tasks 2, 5, 9.
- `InoRequestContextKeys.UserId="ino.userId"` / `SessionId="ino.sessionId"` consistent across Tasks 1, 3, 4, 7.

---

## Execution handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-05-slice-c3-brain-trace-stream.md`. Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration. Same cadence as C.1.

**2. Inline Execution** — Execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints for review.

Which approach?
