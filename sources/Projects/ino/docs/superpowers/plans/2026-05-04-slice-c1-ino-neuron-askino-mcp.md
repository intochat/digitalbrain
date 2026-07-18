# Slice C.1 — `InoNeuron` + `AskIno` entry — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refactor the chat entry point so every prompt lands on a per-`(userId, sessionId)` `InoNeuron` grain that delegates routing to a new `ICortexCapability`. No new behaviour — existing experiences must keep working. Adds `AskIno` gRPC RPC and `IInoGateway.AskAsync` so external MCP-style callers have a unified entry. Lays the foundation for slice C.2 (Creator) and C.3 (filter + brain stream) without committing to either yet.

**Architecture:** `gRPC AskIno → IInoGateway.AskAsync → IInoNeuron(userId/sessionId).AskAsync → ICortexCapability.RouteAsync → existing IExperiencePlan dispatch`. The legacy `Chat()` RPC is rerouted through the same `AskAsync` path so there is exactly one routing implementation. `CortexNeuron` (the grain handling `ChatIntent` synapses) stays in place but becomes orphaned of inbound traffic from the gateway; final removal is deferred to a follow-up slice so this PR stays purely additive.

**Tech Stack:** .NET 11, Orleans 10.x, IAW Agent base class, xUnit + Reqnroll for BDD, gRPC + Protobuf, `Microsoft.Extensions.AI` for `IChatClient`, Aspire dashboard for runtime verification.

**Spec:** `docs/superpowers/specs/2026-05-04-ino-brain-askino-creator-design.md` §2.1, §2.2, §3, §5 Slice C.1.

**Out of scope for this slice (do NOT implement):** Creator capability, risk gate, BrainTraceFilter, InoInstanceContextFilter, InoBrainStream, Travel ← tripradar bridge, brain UI redesign, click-to-inspect drawer, timeline scrubber. Those are slices C.2–C.7.

---

## File map (lock-in for the slice)

| Path | Action |
|---|---|
| `src/Ino.Core.Hosting/Capabilities/ICortexCapability.cs` | **create** — interface (lives in Ino.Core.Hosting because the contract takes `NeuronContext`; namespace `Ino.Core.Capabilities` preserved for callers) |
| `src/Ino.Core/Capabilities/RoutingResult.cs` | **create** — return type + RoutingSource enum (no Ino.Core.Hosting dependency) |
| `src/Ino.Core/IInoNeuron.cs` | **create** — grain interface |
| `src/Ino.Core/InoJournalEvent.cs` | **create** — journal event types |
| `src/Ino.Core/InoResponse.cs` | **create** — response DTO |
| `src/Ino.Core.Hosting/Capabilities/CortexCapability.cs` | **create** — lifts logic from `CortexNeuron` |
| `src/Ino.Core.Hosting/InoNeuron.cs` | **create** — grain class |
| `src/Ino.Core.Hosting/InoNeuronHostingExtensions.cs` | **create** — DI registration |
| `src/Ino.Aspire.Hosting/AddInoExtensions.cs` | **modify** — call new DI extension on the silo |
| `src/Ino.Gateway/IInoGateway.cs` | **modify** — add `AskAsync` |
| `src/Ino.Gateway/InoGateway.cs` | **modify** — implement `AskAsync` + reroute `ChatAsync` through it |
| `src/Ino.Gateway.Grpc/Protos/ino.proto` | **modify** — add `AskIno` RPC + messages |
| `src/Ino.Gateway.Grpc/Services/InoGrpcService.cs` | **modify** — add `AskIno` handler |
| `clients/ino.flutter/protos/ino.proto` | **modify** — keep in lockstep (Flutter codegen) |
| `test/Ino.E2E.Tests/AskInoTests.cs` | **create** — gRPC e2e for `AskIno` |
| `test/Ino.Core.Hosting.Tests/InoNeuronTests.cs` | **create** — TestCluster grain tests |
| `test/Ino.Core.Hosting.Tests/CortexCapabilityTests.cs` | **create** — unit-test the lifted routing |
| `domains/travel/Ino.Domains.Travel/Features/ino-ask.feature` | **create** — BDD scenarios for the routing boundary |

`CortexNeuron.cs` is **untouched** in this slice. Its `HandleAsync(ChatIntent)` becomes orphaned (gateway no longer fires `ChatIntent`), but the grain stays registered as canonical handler. Removal in a follow-up.

---

## Task 1 — `ICortexCapability` + `RoutingResult` contracts

**Files:**
- Create: `src/Ino.Core/Capabilities/ICortexCapability.cs`
- Create: `src/Ino.Core/Capabilities/RoutingResult.cs`

- [ ] **Step 1.1: Create the result record**

Write `src/Ino.Core/Capabilities/RoutingResult.cs`:

```csharp
using Ino.Core;

namespace Ino.Core.Capabilities;

[GenerateSerializer]
public sealed record RoutingResult(
    [property: Id(0)] NeuronResult Outcome,
    [property: Id(1)] RoutingSource Source,
    [property: Id(2)] string? ScenarioName);

[GenerateSerializer]
public enum RoutingSource
{
    Unrouted = 0,
    Regex = 1,
    Ml = 2,
    Llm = 3,
}
```

(`RoutingSource` is duplicated from `Ino.Kernel.Contracts.RoutingSource` for slice
sequencing — the Kernel.Contracts copy stays for now; Task 2 picks one to keep.)

- [ ] **Step 1.2: Create the interface**

Write `src/Ino.Core/Capabilities/ICortexCapability.cs`:

```csharp
using Ino.Core.Hosting;

namespace Ino.Core.Capabilities;

public interface ICortexCapability
{
    Task<RoutingResult> RouteAsync(string prompt, NeuronContext ctx, CancellationToken ct);
}
```

- [ ] **Step 1.3: Resolve `RoutingSource` duplication**

The capability needs a single `RoutingSource` enum — duplicating the one in
`Ino.Kernel.Contracts.RoutingSource` will collide on namespace import. Move
the enum: delete `RoutingSource` from
`src/Ino.Kernel.Contracts/RoutingSource.cs` (or wherever it lives — grep
`enum RoutingSource`) and re-export from `Ino.Core.Capabilities` via a
type-forward, or update existing usages to import the new namespace.

Run:
```bash
grep -rn "enum RoutingSource" --include="*.cs" E:/ino/src
grep -rn "RoutingSource\." --include="*.cs" E:/ino/src
```

For each file referencing the old enum, swap `using Ino.Kernel.Contracts;` → `using Ino.Core.Capabilities;` for the `RoutingSource` lines. Delete the old enum once references are migrated.

- [ ] **Step 1.4: Build to verify**

```bash
dotnet build E:/ino/ino.slnx
```

Expected: green. If `RoutingSource` references break, fix imports until clean.

- [ ] **Step 1.5: Commit**

```bash
git -C E:/ino add src/Ino.Core/Capabilities/ src/Ino.Kernel.Contracts/RoutingSource.cs
git -C E:/ino commit -m "feat(poc): ICortexCapability + RoutingResult contracts (slice C.1.1)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 2 — `CortexCapability` implementation (lift from `CortexNeuron`)

**Files:**
- Create: `src/Ino.Core.Hosting/Capabilities/CortexCapability.cs`
- Test: `test/Ino.Core.Hosting.Tests/CortexCapabilityTests.cs`

The capability is a behaviour-preserving extraction of `CortexNeuron.HandleAsync`. Same logic, no `ChatIntent` synapse argument — just `(prompt, ctx)`.

- [ ] **Step 2.1: Write the failing capability test**

Write `test/Ino.Core.Hosting.Tests/CortexCapabilityTests.cs`:

```csharp
using Ino.Core;
using Ino.Core.Capabilities;
using Ino.Core.Hosting;
using Ino.Core.Hosting.Capabilities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Ino.Core.Hosting.Tests;

public sealed class CortexCapabilityTests
{
    [Fact]
    public async Task RouteAsync_returns_unrouted_when_no_experiences_installed()
    {
        var discovery = new Mock<IDiscoveryClient>();
        discovery.Setup(d => d.DumpExperiencesAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(Array.Empty<IExperience>());
        var firePort = new Mock<IFirePort>();
        var chat = new Mock<IChatClient>();
        var corpus = new Mock<IExperiencePromptCorpus>();
        corpus.SetupGet(c => c.Count).Returns(0);
        var grainFactory = new Mock<IGrainFactory>();

        var capability = new CortexCapability(
            discovery.Object,
            firePort.Object,
            chat.Object,
            corpus.Object,
            grainFactory.Object,
            NullLogger<CortexCapability>.Instance);

        var ctx = new NeuronContext(
            SynapseId: SynapseId.New(),
            CorrelationId: CorrelationId.New(),
            Source: new Caller.Ambient(DomainId.From("test")),
            SourceStream: new StreamKey("<test>"),
            UserId: "user-1");

        var result = await capability.RouteAsync("anything", ctx, CancellationToken.None);

        Assert.Equal(RoutingSource.Unrouted, result.Source);
        Assert.True(result.Outcome.Success);
        Assert.Contains("No specialist", result.Outcome.Message);
    }
}
```

- [ ] **Step 2.2: Run the test, verify it fails to compile**

```bash
dotnet test E:/ino/test/Ino.Core.Hosting.Tests/Ino.Core.Hosting.Tests.csproj --filter "RouteAsync_returns_unrouted_when_no_experiences_installed"
```

Expected: build error — `CortexCapability` does not exist.

- [ ] **Step 2.3: Create the capability — lift `CortexNeuron.HandleAsync`**

Write `src/Ino.Core.Hosting/Capabilities/CortexCapability.cs`:

```csharp
using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Ino.Core;
using Ino.Core.Capabilities;
using Ino.Core.Hosting.Llm;
using Ino.Core.Hosting.ML;
using Ino.Kernel.Contracts;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Ino.Core.Hosting.Capabilities;

public sealed class CortexCapability(
    IDiscoveryClient discovery,
    IFirePort firePort,
    IChatClient chatClient,
    IExperiencePromptCorpus corpus,
    IGrainFactory grainFactory,
    ILogger<CortexCapability> log) : ICortexCapability
{
    public async Task<RoutingResult> RouteAsync(string prompt, NeuronContext ctx, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var experiences = await discovery.DumpExperiencesAsync(ct);
        if (experiences.Count == 0)
            return new RoutingResult(await EmitUnroutedAsync(prompt, ctx, ct), RoutingSource.Unrouted, ScenarioName: null);

        var features = BuildRoutingFeatures(prompt, experiences);

        if (await TryFastPathAsync(prompt, experiences, ctx, ct) is { } fast)
        {
            await RecordRoutingDecisionAsync(ctx.UserId, features, routed: true, ct,
                prompt, RoutingSource.Regex,
                correlationId: ctx.CorrelationId.Value, durationMs: (int)sw.ElapsedMilliseconds);
            return new RoutingResult(fast.Outcome, RoutingSource.Regex, fast.ScenarioName);
        }

        if (await PredictWillRouteAsync(ctx.UserId, features, ct) is OptimizationResult pred
            && pred is { Predicted: false, Confidence: >= 0.90f })
        {
            await RecordRoutingDecisionAsync(ctx.UserId, features, routed: false, ct,
                prompt, RoutingSource.Ml,
                mlPrediction: pred.Predicted ? 1.0 : 0.0, mlConfidence: pred.Confidence,
                correlationId: ctx.CorrelationId.Value, durationMs: (int)sw.ElapsedMilliseconds);
            return new RoutingResult(await EmitUnroutedAsync(prompt, ctx, ct), RoutingSource.Ml, ScenarioName: null);
        }

        if (await TryClassifyWithLlmAsync(prompt, experiences, ctx, ct) is { } llm)
        {
            await RecordRoutingDecisionAsync(ctx.UserId, features, routed: true, ct,
                prompt, RoutingSource.Llm, llmCalled: true,
                correlationId: ctx.CorrelationId.Value, durationMs: (int)sw.ElapsedMilliseconds);
            return new RoutingResult(llm.Outcome, RoutingSource.Llm, llm.ScenarioName);
        }

        await RecordRoutingDecisionAsync(ctx.UserId, features, routed: false, ct,
            prompt, RoutingSource.Unrouted, llmCalled: true,
            correlationId: ctx.CorrelationId.Value, durationMs: (int)sw.ElapsedMilliseconds);
        return new RoutingResult(await EmitUnroutedAsync(prompt, ctx, ct), RoutingSource.Unrouted, ScenarioName: null);
    }

    // The bodies of BuildRoutingFeatures, PredictWillRouteAsync,
    // RecordRoutingDecisionAsync, TryFastPathAsync, TryClassifyWithLlmAsync,
    // BuildClassifierSystemMessage, TryParseClassifiedExperienceId,
    // TryRouteToAsync, TryExecutePlanAsync, CanConstructSynapse,
    // AnnotateReasoningAsync, EmitUnroutedAsync are LIFTED VERBATIM from
    // src/Ino.Kernel/CortexNeuron.cs lines 97-471 with the following changes:
    //   - drop `synapse` parameter; use `prompt` and `ctx.UserId` directly
    //   - `liveCtx` becomes `ctx` (no surrogate stripping at this layer)
    //   - methods that previously returned `NeuronResult?` now return either
    //     `null` (unchanged semantics) or, where the caller treats null as
    //     "fall through", a small wrapper { Outcome, ScenarioName } so we
    //     can carry the scenario name out of TryFastPathAsync. Define a
    //     local `record FastPathHit(NeuronResult Outcome, string? ScenarioName);`
    //     used internally only.
    //   - `EmitUnroutedAsync` takes `(string prompt, NeuronContext ctx, CancellationToken ct)`
    //     instead of the full ChatIntent.
}
```

Concretely the lift of `EmitUnroutedAsync` at `CortexNeuron.cs:465`:

```csharp
async Task<NeuronResult> EmitUnroutedAsync(string prompt, NeuronContext ctx, CancellationToken ct)
{
    var unrouted = new UnroutedIntent(prompt, ctx.UserId);
    await firePort.FireBroadcast(unrouted, ctx, ct);
    log.LogInformation("Cortex unrouted {Text} for user {UserId}", prompt, ctx.UserId);
    return NeuronResult.Ok("No specialist is installed for that intent yet.").With(unrouted);
}
```

- [ ] **Step 2.4: Run the failing test, verify it passes**

```bash
dotnet test E:/ino/test/Ino.Core.Hosting.Tests/Ino.Core.Hosting.Tests.csproj --filter "RouteAsync_returns_unrouted_when_no_experiences_installed"
```

Expected: PASS.

- [ ] **Step 2.5: Run the broader existing test suite, verify nothing broke**

```bash
dotnet test E:/ino/ino.slnx
```

Expected: green. If a test that exercises CortexNeuron fails, the lift is not yet behaviour-preserving — diff `CortexCapability.RouteAsync` against the original `CortexNeuron.HandleAsync` (`src/Ino.Kernel/CortexNeuron.cs:41-91`) line-by-line.

- [ ] **Step 2.6: Commit**

```bash
git -C E:/ino add src/Ino.Core.Hosting/Capabilities/ test/Ino.Core.Hosting.Tests/CortexCapabilityTests.cs
git -C E:/ino commit -m "feat(poc): CortexCapability — behaviour-preserving extraction from CortexNeuron (slice C.1.2)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 3 — `IInoNeuron` + `InoJournalEvent` + `InoResponse` contracts

**Files:**
- Create: `src/Ino.Core/IInoNeuron.cs`
- Create: `src/Ino.Core/InoJournalEvent.cs`
- Create: `src/Ino.Core/InoResponse.cs`

- [ ] **Step 3.1: Write the journal event types**

Write `src/Ino.Core/InoJournalEvent.cs`:

```csharp
using Ino.Core.Capabilities;

namespace Ino.Core;

// Marker for the InoNeuron's journal event union. v0.1 carries routing
// outcomes so the activation can replay its history; future slices add
// CreatedNeuron, ToolCalled etc. once Creator and the LlmNeuron rewrite land.
[GenerateSerializer]
public abstract record InoJournalEvent : ISynapse;

[GenerateSerializer]
public sealed record InoAsked(
    [property: Id(0)] string Prompt,
    [property: Id(1)] string SessionId,
    [property: Id(2)] DateTimeOffset At) : InoJournalEvent;

[GenerateSerializer]
public sealed record InoRouted(
    [property: Id(0)] string Prompt,
    [property: Id(1)] string ExperienceId,
    [property: Id(2)] RoutingSource Source,
    [property: Id(3)] DateTimeOffset At) : InoJournalEvent;
```

- [ ] **Step 3.2: Write the response DTO**

Write `src/Ino.Core/InoResponse.cs`:

```csharp
namespace Ino.Core;

[GenerateSerializer]
public sealed record InoResponse(
    [property: Id(0)] string Text,
    [property: Id(1)] string CorrelationId,
    [property: Id(2)] RfwPayload? Rfw,
    [property: Id(3)] bool Success,
    [property: Id(4)] string? Source);
```

- [ ] **Step 3.3: Write the grain interface**

Write `src/Ino.Core/IInoNeuron.cs`:

```csharp
using Ino.Core.Hosting;
using Orleans;

namespace Ino.Core;

public interface IInoNeuron : IGrainWithStringKey
{
    /// <summary>
    /// Single entry. Routes via ICortexCapability and returns a response. The
    /// grain key is "{userId}/{sessionId}" — see InoNeuronGrainKey.Format.
    /// </summary>
    Task<InoResponse> AskAsync(string prompt, string correlationId, CancellationToken ct);
}

public static class InoNeuronGrainKey
{
    public const string DefaultSessionId = "default";
    public const string AutonomicSessionId = "autonomic";

    public static string Format(string userId, string sessionId) => $"{userId}/{sessionId}";

    public static (string UserId, string SessionId) Parse(string key)
    {
        var slash = key.IndexOf('/');
        return slash < 0
            ? (key, DefaultSessionId)
            : (key[..slash], key[(slash + 1)..]);
    }
}
```

- [ ] **Step 3.4: Build to verify**

```bash
dotnet build E:/ino/src/Ino.Core/Ino.Core.csproj
```

Expected: green.

- [ ] **Step 3.5: Commit**

```bash
git -C E:/ino add src/Ino.Core/IInoNeuron.cs src/Ino.Core/InoJournalEvent.cs src/Ino.Core/InoResponse.cs
git -C E:/ino commit -m "feat(poc): IInoNeuron grain interface + InoJournalEvent journal + InoResponse DTO (slice C.1.3)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 4 — `InoNeuron` grain class

**Files:**
- Create: `src/Ino.Core.Hosting/InoNeuron.cs`
- Test: `test/Ino.Core.Hosting.Tests/InoNeuronTests.cs`

- [ ] **Step 4.1: Write the failing TestCluster grain test**

Write `test/Ino.Core.Hosting.Tests/InoNeuronTests.cs`:

```csharp
using Ino.Core;
using Ino.Core.Capabilities;
using Ino.Core.Hosting;
using Ino.Testing;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Ino.Core.Hosting.Tests;

public sealed class InoNeuronTests : IClassFixture<InoTestClusterFixture>
{
    private readonly InoTestClusterFixture _fixture;

    public InoNeuronTests(InoTestClusterFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AskAsync_delegates_to_ICortexCapability_and_returns_outcome()
    {
        var capability = _fixture.GetMock<ICortexCapability>();
        var expected = NeuronResult.Ok("hello from cortex");
        capability
            .Setup(c => c.RouteAsync("hi", It.IsAny<NeuronContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new RoutingResult(expected, RoutingSource.Regex, ScenarioName: "test"));

        var grain = _fixture.Cluster.GrainFactory.GetGrain<IInoNeuron>(
            InoNeuronGrainKey.Format("user-1", InoNeuronGrainKey.DefaultSessionId));

        var response = await grain.AskAsync("hi", correlationId: "corr-1", CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("hello from cortex", response.Text);
        Assert.Equal("corr-1", response.CorrelationId);
        Assert.Equal("Regex", response.Source);
    }
}
```

This depends on `InoTestClusterFixture` — see `src/Ino.Testing/` for the
existing pattern (similar to `IAW.Testing.AgentTest<TAgent>`). If a fixture
that swaps in mock capabilities does not yet exist, add it as part of
this task — write a minimal `InoTestClusterFixture` that:

1. Hosts a `TestCluster` with `AddIno()` minus real silos.
2. Exposes `GetMock<T>()` for replacing services.
3. Registers `InoNeuron` and a no-op `IFirePort`.

Use the existing `IAW.Testing.AgentTest<TAgent>` shape as the template
(`iaw/src/Testing/AgentTest.cs`) — same TestCluster + `MockChatClient`
pattern.

- [ ] **Step 4.2: Run the failing test, verify it fails**

```bash
dotnet test E:/ino/test/Ino.Core.Hosting.Tests/Ino.Core.Hosting.Tests.csproj --filter "AskAsync_delegates_to_ICortexCapability"
```

Expected: build error or test failure — `InoNeuron` doesn't exist.

- [ ] **Step 4.3: Implement the grain**

Write `src/Ino.Core.Hosting/InoNeuron.cs`:

```csharp
using Core.Contracts;
using IAW.Core;
using Ino.Core;
using Ino.Core.Capabilities;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Orleans;
using Orleans.Journaling;

namespace Ino.Core.Hosting;

public sealed class InoNeuron(
    [AgentState] AgentDurableState durableState,
    IChatClient chatClient,
    [FromKeyedServices("journal")] IDurableList<EventEnvelope<InoJournalEvent>> journal,
    ICortexCapability cortex,
    IFirePort firePort,
    ILogger<InoNeuron> log)
    : LlmNeuron<InoJournalEvent>(durableState, chatClient, journal), IInoNeuron
{
    public async Task<InoResponse> AskAsync(string prompt, string correlationId, CancellationToken ct)
    {
        var (userId, sessionId) = InoNeuronGrainKey.Parse(this.GetPrimaryKeyString());

        var ctx = new NeuronContext(
            SynapseId: SynapseId.New(),
            CorrelationId: new CorrelationId(correlationId),
            Source: new Caller.Ambient(DomainId.From("ino")),
            SourceStream: new StreamKey($"ino:{userId}/{sessionId}"),
            UserId: userId)
        {
            FirePort = firePort,
            Logger = log,
        };

        await RaiseAsync(new InoAsked(prompt, sessionId, DateTimeOffset.UtcNow), ctx, ct);

        var routing = await cortex.RouteAsync(prompt, ctx, ct);

        // Journal the routed experience id when we got a hit. Source.ToString
        // gives Regex/Ml/Llm/Unrouted matching the spec's terminology.
        await RaiseAsync(
            new InoRouted(prompt, ExperienceId: routing.Outcome.RoutedExperienceId ?? string.Empty,
                Source: routing.Source.ToString(), DateTimeOffset.UtcNow),
            ctx, ct);

        return new InoResponse(
            Text: routing.Outcome.Message ?? "(no reply)",
            CorrelationId: correlationId,
            Rfw: routing.Outcome.Rfw,
            Success: routing.Outcome.Success,
            Source: routing.Source.ToString());
    }
}
```

If `NeuronResult` does not have a `RoutedExperienceId` property,
substitute `string.Empty` until C.2 adds one. Confirm by reading
`src/Ino.Core/NeuronResult.cs`; if absent, change `routing.Outcome.RoutedExperienceId` to `string.Empty` in this task and open a follow-up.

- [ ] **Step 4.4: Run the failing test, verify it passes**

```bash
dotnet test E:/ino/test/Ino.Core.Hosting.Tests/Ino.Core.Hosting.Tests.csproj --filter "AskAsync_delegates_to_ICortexCapability"
```

Expected: PASS.

- [ ] **Step 4.5: Commit**

```bash
git -C E:/ino add src/Ino.Core.Hosting/InoNeuron.cs test/Ino.Core.Hosting.Tests/InoNeuronTests.cs
git -C E:/ino commit -m "feat(poc): InoNeuron grain delegating to ICortexCapability (slice C.1.4)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 5 — DI registration

**Files:**
- Create: `src/Ino.Core.Hosting/InoNeuronHostingExtensions.cs`
- Modify: `src/Ino.Aspire.Hosting/AddInoExtensions.cs`

- [ ] **Step 5.1: Write the DI extension**

Write `src/Ino.Core.Hosting/InoNeuronHostingExtensions.cs`:

```csharp
using Ino.Core.Capabilities;
using Ino.Core.Hosting.Capabilities;
using Microsoft.Extensions.DependencyInjection;

namespace Ino.Core.Hosting;

public static class InoNeuronHostingExtensions
{
    /// <summary>
    /// Registers InoNeuron's dependencies. Call from each silo that hosts
    /// the kernel — InoNeuron grains are placed via Orleans' default
    /// strategy (no PinToSilo for v0.1) so any silo with the kernel
    /// dependencies wired up can activate one.
    /// </summary>
    public static IServiceCollection AddInoNeuron(this IServiceCollection services)
    {
        services.AddSingleton<ICortexCapability, CortexCapability>();
        return services;
    }
}
```

- [ ] **Step 5.2: Wire from AddIno**

Read `src/Ino.Aspire.Hosting/AddInoExtensions.cs` to find the silo-side
hook, then ensure each silo project that previously registered
`CortexNeuron` also calls `services.AddInoNeuron()`. Search for the
silo hosting setup:

```bash
grep -rn "AddCortex\|AddSingleton.*Cortex\|services.Add.*Discovery" E:/ino/src --include="*.cs"
```

In whichever file registers Cortex's dependencies (likely
`src/Ino.Kernel/InoKernelHostingExtensions.cs` or similar), add a single
line `services.AddInoNeuron();` next to the existing Cortex registrations.

If no per-silo hosting extension exists yet, add the call inside the
Aspire `AddInoExtensions` builder pattern by exposing a
`Configure(silo => silo.AddInoNeuron())` overload — depends on the
existing shape. Inspect first, then patch.

- [ ] **Step 5.3: Build to verify**

```bash
dotnet build E:/ino/ino.slnx
```

Expected: green.

- [ ] **Step 5.4: Run all tests to confirm no regression**

```bash
dotnet test E:/ino/ino.slnx
```

Expected: green.

- [ ] **Step 5.5: Commit**

```bash
git -C E:/ino add src/Ino.Core.Hosting/InoNeuronHostingExtensions.cs src/Ino.Aspire.Hosting/ src/Ino.Kernel/
git -C E:/ino commit -m "feat(poc): wire InoNeuron + CortexCapability into the silo (slice C.1.5)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 6 — `IInoGateway.AskAsync` + `InoGateway.AskAsync`

**Files:**
- Modify: `src/Ino.Gateway/IInoGateway.cs`
- Modify: `src/Ino.Gateway/InoGateway.cs`

- [ ] **Step 6.1: Add the interface method**

Edit `src/Ino.Gateway/IInoGateway.cs`. After the existing `ChatAsync`
declaration (lines 60-64), add:

```csharp
    /// <summary>
    /// Single-shot natural-language entry point used by the AskIno gRPC RPC and
    /// (in a follow-up slice) the MCP server. Resolves the per-(userId, sessionId)
    /// InoNeuron grain and delegates to its AskAsync. ChatAsync is the
    /// streaming variant that adds skeleton frames + RFW unwrap; AskAsync
    /// returns one final response.
    /// </summary>
    Task<InoResponse> AskAsync(
        string prompt,
        string userId,
        string sessionId,
        string? correlationId = null,
        CancellationToken ct = default);
```

- [ ] **Step 6.2: Implement on `InoGateway`**

Edit `src/Ino.Gateway/InoGateway.cs`. Add the field and method:

```csharp
public async Task<InoResponse> AskAsync(
    string prompt,
    string userId,
    string sessionId,
    string? correlationId = null,
    CancellationToken ct = default)
{
    using var span = ActivitySource.StartActivity("ino.gateway.ask", ActivityKind.Internal);
    span?.SetTag("ino.user.id", userId);
    span?.SetTag("ino.session.id", sessionId);

    var corrId = string.IsNullOrWhiteSpace(correlationId) ? CorrelationId.New() : new CorrelationId(correlationId);
    span?.SetTag("ino.correlation_id", corrId.Value);

    var grain = grainFactory.GetGrain<IInoNeuron>(InoNeuronGrainKey.Format(userId, sessionId));
    return await grain.AskAsync(prompt, corrId.Value, ct);
}
```

- [ ] **Step 6.3: Reroute `ChatAsync` through `AskAsync`**

Replace the body of `ChatAsync` from `firePort.Fire(new ChatIntent(...))`
through to `result = ...` (lines 132-142 of
`src/Ino.Gateway/InoGateway.cs`) with:

```csharp
NeuronResult result;
Exception? handlerError = null;
try
{
    var ino = await AskAsync(message, userId, InoNeuronGrainKey.DefaultSessionId, corrId.Value, ct);
    result = ino.Success
        ? (ino.Rfw is { } rfw
            ? NeuronResult.Ok(ino.Text).WithRfwPayload(rfw)
            : NeuronResult.Ok(ino.Text))
        : NeuronResult.Fail(SynapseErrorCode.NoCanonicalHandler, ino.Text);
}
catch (Exception ex)
{
    handlerError = ex;
    result = NeuronResult.Fail(SynapseErrorCode.NoCanonicalHandler, ex.Message);
    log.LogError(ex, "AskAsync threw on {Message}", message);
}
```

If `NeuronResult.WithRfwPayload` does not exist (it should — see
`src/Ino.Core/NeuronResult.cs` "RfwPayload contract on NeuronResult"
commit `9e6569b`), substitute the existing extension. If
`InoResponse.Rfw` round-trips lossily, document the lossy bit in the
commit message.

- [ ] **Step 6.4: Run all existing chat-flow tests, verify still green**

```bash
dotnet test E:/ino/ino.slnx --filter "Category!=E2E"
```

Expected: every chat-related test passes. If `Plan_trip_to_bali_next_month_drives_full_six_hop_flow` (or any plan
test) regresses, the rewire is missing the `firePort.Fire` semantics that downstream plans relied on — diff
`InoGateway.ChatAsync` before/after to see what dropped.

- [ ] **Step 6.5: Commit**

```bash
git -C E:/ino add src/Ino.Gateway/
git -C E:/ino commit -m "feat(poc): IInoGateway.AskAsync + reroute ChatAsync through it (slice C.1.6)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 7 — `AskIno` gRPC RPC

**Files:**
- Modify: `src/Ino.Gateway.Grpc/Protos/ino.proto`
- Modify: `clients/ino.flutter/protos/ino.proto`
- Modify: `src/Ino.Gateway.Grpc/Services/InoGrpcService.cs`
- Test: `test/Ino.E2E.Tests/AskInoTests.cs`

- [ ] **Step 7.1: Write the failing E2E test**

Write `test/Ino.E2E.Tests/AskInoTests.cs`:

```csharp
using Grpc.Net.Client;
using Ino.Grpc;
using Xunit;

namespace Ino.E2E.Tests;

[Collection("aspire")]
public sealed class AskInoTests
{
    private readonly AspireFixture _fixture;
    public AskInoTests(AspireFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task AskIno_routes_plan_trip_to_PlanTripPlan()
    {
        using var channel = GrpcChannel.ForAddress(_fixture.GatewayUrl);
        var client = new Ino.IngClient(channel);  // generated client

        var response = await client.AskInoAsync(new AskInoRequest
        {
            Prompt = "plan a trip to Bali next month",
            UserId = "test-user",
            SessionId = "default",
        });

        Assert.True(response.Success);
        Assert.NotEmpty(response.Reply);
        Assert.NotEmpty(response.CorrelationId);
    }
}
```

If the `AspireFixture` shape differs in this repo, substitute the equivalent fixture used by the existing E2E tests — see `test/Ino.E2E.Tests/RichTripPlanningE2ETests.cs` for the pattern.

- [ ] **Step 7.2: Run, verify it fails to build**

```bash
dotnet test E:/ino/test/Ino.E2E.Tests/Ino.E2E.Tests.csproj --filter "AskIno_routes"
```

Expected: build error — `AskInoAsync` not generated.

- [ ] **Step 7.3: Add the proto messages**

Edit `src/Ino.Gateway.Grpc/Protos/ino.proto`. After the `RfwEvent` RPC (line 37), add inside the `service Ino { ... }` block:

```protobuf
  // Slice C.1 — single-shot natural-language entry. Returns one InoResponse
  // (no streaming, no skeleton frames). Used by the MCP server (next slice)
  // and by integration tests. Chat() stays for the Flutter client.
  rpc AskIno(AskInoRequest) returns (AskInoResponse);
```

After `RfwEventResponse` (line 331), add:

```protobuf
message AskInoRequest {
  string prompt = 1;
  string user_id = 2;
  string session_id = 3;
  string correlation_id = 4;
}

message AskInoResponse {
  bool success = 1;
  string reply = 2;
  string correlation_id = 3;
  bytes rfw_description = 4;
  bytes rfw_data = 5;
  string content_type = 6;
  string source = 7;            // "Regex" | "Ml" | "Llm" | "Unrouted"
}
```

- [ ] **Step 7.4: Mirror in the Flutter copy**

Edit `clients/ino.flutter/protos/ino.proto` with the identical additions (RPC line + two messages). The two .proto files are kept in lockstep per the comment at the top of each file.

- [ ] **Step 7.5: Implement the gRPC handler**

Edit `src/Ino.Gateway.Grpc/Services/InoGrpcService.cs`. After the `RfwEvent` override at the end of the class (around line 220+), add:

```csharp
public override async Task<AskInoResponse> AskIno(AskInoRequest request, ServerCallContext context)
{
    var userId = string.IsNullOrWhiteSpace(request.UserId) ? "anonymous" : request.UserId;
    var sessionId = string.IsNullOrWhiteSpace(request.SessionId) ? InoNeuronGrainKey.DefaultSessionId : request.SessionId;
    var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId) ? null : request.CorrelationId;

    var ino = await gateway.AskAsync(request.Prompt, userId, sessionId, correlationId, context.CancellationToken);

    var response = new AskInoResponse
    {
        Success = ino.Success,
        Reply = ino.Text,
        CorrelationId = ino.CorrelationId,
        Source = ino.Source ?? string.Empty,
    };
    if (ino.Rfw is { } payload)
    {
        response.RfwDescription = StripCarriageReturns(payload.DescriptionDsl.Span);
        response.RfwData = StripCarriageReturns(payload.DataPayload.Span);
        response.ContentType = $"rfw/{payload.LibraryName}";
    }
    return response;
}
```

- [ ] **Step 7.6: Build, verify codegen produces the client**

```bash
dotnet build E:/ino/ino.slnx
```

Expected: green. The C# generated client now has `AskInoAsync`.

- [ ] **Step 7.7: Run the E2E test**

```bash
dotnet test E:/ino/test/Ino.E2E.Tests/Ino.E2E.Tests.csproj --filter "AskIno_routes"
```

Expected: PASS.

- [ ] **Step 7.8: Commit**

```bash
git -C E:/ino add src/Ino.Gateway.Grpc/ clients/ino.flutter/protos/ test/Ino.E2E.Tests/AskInoTests.cs
git -C E:/ino commit -m "feat(poc): AskIno gRPC RPC (slice C.1.7)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 8 — BDD `ino-ask.feature`

**Files:**
- Create: `domains/travel/Ino.Domains.Travel/Features/ino-ask.feature`

- [ ] **Step 8.1: Write the feature file**

Write `domains/travel/Ino.Domains.Travel/Features/ino-ask.feature`:

```gherkin
Feature: AskIno — single-method routing boundary
  These scenarios pin the contract that gRPC AskIno → InoNeuron.AskAsync →
  ICortexCapability.RouteAsync routes to the correct experience and
  matches today's BDD-mock fixtures verbatim. They duplicate the routing
  cases from travel-intent.feature but from the AskIno entry, so a future
  refactor that moves Cortex deeper can't silently break the new entry
  while keeping the old one green.

  @experience:travel.plan-trip
  Scenario: AskIno plans a trip
    Given the user calls AskIno with prompt "plan a trip to Bali next month"
    Then the response Source is "Regex"
    And the response Success is true
    And the response Reply is not empty

  @experience:travel.find-flights
  Scenario: AskIno finds flights
    Given the user calls AskIno with prompt "find flights to Tokyo"
    Then the response Source is "Regex"
    And the response Success is true

  Scenario: AskIno on unknown intent returns Unrouted
    Given the user calls AskIno with prompt "asdf qwerty"
    Then the response Source is "Unrouted"
    And the response Success is true
    And the response Reply contains "No specialist"
```

- [ ] **Step 8.2: Add the step bindings**

Search for an existing Reqnroll step bindings file under `Ino.Domains.Travel.Tests`:

```bash
ls E:/ino/domains/travel/Ino.Domains.Travel.Tests/Steps/
```

If a binding class exists (e.g. `RoutingSteps.cs`), append three steps:
1. `[Given(@"the user calls AskIno with prompt ""(.*)""")]` — calls
   `IInoNeuron.AskAsync` against the test cluster's grain factory.
2. `[Then(@"the response Source is ""(.*)""")]` — asserts on the captured `InoResponse.Source`.
3. `[Then(@"the response Reply (is not empty|contains ""(.*)"")")]` — asserts on `InoResponse.Text`.

If no binding folder exists, create
`domains/travel/Ino.Domains.Travel.Tests/Steps/AskInoSteps.cs` with the
three bindings. Use the existing
`Plans/PlanTripPlanRfwEventsTests.cs` as a template for the
TestCluster wiring.

- [ ] **Step 8.3: Run BDD tests**

```bash
dotnet test E:/ino/domains/travel/Ino.Domains.Travel.Tests/Ino.Domains.Travel.Tests.csproj --filter "Category=Routing|Feature=ino-ask"
```

Expected: 3 scenarios pass.

- [ ] **Step 8.4: Commit**

```bash
git -C E:/ino add domains/travel/Ino.Domains.Travel/Features/ino-ask.feature domains/travel/Ino.Domains.Travel.Tests/Steps/
git -C E:/ino commit -m "test(poc): BDD ino-ask.feature — pins AskIno routing boundary (slice C.1.8)

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>"
```

---

## Task 9 — Verification

- [ ] **Step 9.1: Full build**

```bash
dotnet build E:/ino/ino.slnx
```

Expected: green.

- [ ] **Step 9.2: Full test suite**

```bash
dotnet test E:/ino/ino.slnx
```

Expected: green. If any pre-existing test regresses, diff against master and fix before proceeding — slice C.1 must be behaviour-preserving.

- [ ] **Step 9.3: Aspire run**

Start the AppHost via Aspire MCP:

```
mcp__aspire__execute_resource_command(resourceName="kernel", commandName="rebuild")
mcp__aspire__execute_resource_command(resourceName="travel", commandName="rebuild")
```

Then verify all resources Healthy:

```
mcp__aspire__list_resources
```

Expected: `kernel`, `identity`, `travel`, `taxi` all in `Running` state with `Healthy` health status.

- [ ] **Step 9.4: Browser smoke**

Open the kernel HTTPS URL in Chrome (via Chrome DevTools MCP). Send the prompt "plan a trip to Bali next month" through the chat composer. Verify:

1. The chat returns a result (not "Routing error" / not "No specialist installed").
2. Aspire **Traces** show a span tree: `grpc Chat` → `ino.gateway.chat` → `ino.gateway.ask` → grain hop into `InoNeuron` → grain hop into `PlanTripPlan` (or whichever plan handles the intent).
3. Aspire **Structured Logs** show `gateway chat:` followed by routing logs from `CortexCapability` (no longer from `CortexNeuron`).

- [ ] **Step 9.5: Commit if any verification fixes were required**

If Step 9.3-9.4 surfaced fix-ups (config wiring, missed file move), commit them with:

```bash
git -C E:/ino add -A
git -C E:/ino commit -m "fix(poc): slice C.1 verification fix-ups"
```

---

## Done criteria

- All 9 tasks above committed.
- `dotnet build` + `dotnet test` green.
- `aspire run` Healthy across all resources.
- Browser smoke: trip planning still works end-to-end through the new path.
- Aspire traces show `ino.gateway.ask` span between `ino.gateway.chat` and the plan grains.
- No new flakes; existing snapshot/golden tests pass.

The slice is purely additive on the architecture — `CortexNeuron.cs` stays untouched, no Creator code lands, no UI changes. The next slice (C.2) implements `ICreatorCapability` and the risk gate; this slice's `InoNeuron.AskAsync` calls only `_cortex.RouteAsync`, never Creator.

---

## Self-review checklist (run before handing off)

- ☑ **Spec coverage:** §2.1 InoNeuron+capabilities → Tasks 1-5; §2.2 AskIno entry → Tasks 6-7; §2.3 Creator → out of scope; §2.4 Filters/Reminders → out of scope; §3.1 file paths → File map matches spec; §5 Slice C.1 verification → Task 9.
- ☑ **Placeholder scan:** no "TBD", no "TODO", no "fill in details". Where a path branches on observed code shape (Step 2.3 lift, Step 5.2 silo registration), the plan tells the engineer how to inspect and decide.
- ☑ **Type consistency:** `RoutingResult { NeuronResult Outcome, RoutingSource Source, string? ScenarioName }` is used identically in Tasks 1-4. `InoNeuronGrainKey.Format/Parse` referenced consistently across Tasks 3, 6, 7. `InoResponse { Text, CorrelationId, Rfw, Success, Source }` constructed identically in Tasks 4 + 6.
- ☑ **Out-of-scope explicit:** named in the header.
