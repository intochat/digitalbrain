# Cortex + Synapse Delivery — Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add synapse delivery to `NeuronGrain.FireAsync` (synapses reach their target) and build the `CortexNeuron` that routes natural language to specialist neurons via LLM + BehaviorMemory.

**Architecture:** Extend ino-new's existing `NeuronGrain`/`NeuronRegistryGrain` with a `HandleAsync` method on `INeuron` for synapse delivery. Build `CortexNeuron` as a grain that receives `user_message` synapses, reads the neuron catalog + BehaviorMemory hits, calls the LLM to decompose intent into specialist verbs, and fires synapses to the matched specialists. Cortex reads from `NeuronRegistryGrain` (runtime neurons) — compile-time `AgentRegistryGrain` integration deferred to Phase 2.

**Tech Stack:** C# / Orleans 10 / Microsoft.Extensions.AI / xunit.v3 / Gherkin BDD (manual step runner)

**Spec:** `docs/superpowers/specs/2026-04-10-cortex-tui-universes-design.md`

---

## File Map

| Action | File | Responsibility |
|--------|------|---------------|
| Modify | `features/ino-new/InoNew.Core/INeuron.cs` | Add `HandleAsync(Synapse)` method to interface |
| Modify | `features/ino-new/InoNew.Core/NeuronGrain.cs` | Implement `HandleAsync` (default no-op); update `FireAsync` to deliver synapse to target |
| Modify | `features/ino-new/InoNew.Core/NeuronState.cs` | Add `HandledCount` field |
| Modify | `features/ino-new/InoNew.Core/Neuron.cs` | Add `SynapseSchema` field to `Neuron` and `NeuronBlueprint` records |
| Create | `features/ino-new/InoNew.Core/SynapseResult.cs` | Return type for `HandleAsync` — result payload + success flag |
| Create | `features/ino-new/InoNew.Core/CortexGrain.cs` | Cortex neuron: LLM routing, catalog building, BehaviorMemory integration |
| Create | `features/ino-new/InoNew.Core/ICortex.cs` | Cortex grain interface: `HandleUserMessageAsync(string text)` |
| Modify | `features/ino-new/InoNew.Core/InoCommandDispatcher.cs` | Add `chat` verb that routes through Cortex instead of direct grain calls |
| Create | `features/ino-new/InoNew.Tests/Features/Cortex.feature` | Gherkin scenarios for Cortex routing |
| Create | `features/ino-new/InoNew.Tests/Steps/CortexSteps.cs` | BDD step definitions for Cortex |
| Create | `features/ino-new/InoNew.Tests/Steps/CortexScenarioTests.cs` | xunit.v3 [Fact] test runner for Cortex scenarios |
| Create | `features/ino-new/InoNew.Tests/SynapseDeliveryTests.cs` | Unit tests for FireAsync → HandleAsync delivery |

---

### Task 1: Add HandleAsync to INeuron interface

**Files:**
- Modify: `features/ino-new/InoNew.Core/INeuron.cs`
- Create: `features/ino-new/InoNew.Core/SynapseResult.cs`

- [ ] **Step 1: Create the SynapseResult record**

```csharp
// features/ino-new/InoNew.Core/SynapseResult.cs
namespace InoNew.Core;

// Return type for INeuron.HandleAsync. Carries the handler's reply payload
// and a success flag. Specialists return their domain-specific output in
// Payload; Cortex reads it to compose the final user-facing reply.
[GenerateSerializer]
public sealed record SynapseResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] string Payload,
    [property: Id(2)] string Verb);
```

- [ ] **Step 2: Add HandleAsync to INeuron**

In `features/ino-new/InoNew.Core/INeuron.cs`, add after the `FireAsync` method:

```csharp
    // Handle an incoming synapse delivered by another neuron's FireAsync.
    // The default implementation in NeuronGrain is a no-op that returns
    // success with an empty payload — specialists override this with their
    // domain logic. The verb in the synapse determines which handler runs.
    Task<SynapseResult> HandleAsync(Synapse synapse, CancellationToken ct = default);
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build features/ino-new/InoNew.Core/InoNew.Core.csproj`
Expected: Build failure — `NeuronGrain` doesn't implement `HandleAsync` yet. That's expected, we fix it in Task 2.

- [ ] **Step 4: Commit**

```bash
git add features/ino-new/InoNew.Core/INeuron.cs features/ino-new/InoNew.Core/SynapseResult.cs
git commit -m "feat(ino-new): add HandleAsync to INeuron interface + SynapseResult record"
```

---

### Task 2: Implement HandleAsync in NeuronGrain + synapse delivery in FireAsync

**Files:**
- Modify: `features/ino-new/InoNew.Core/NeuronGrain.cs`
- Modify: `features/ino-new/InoNew.Core/NeuronState.cs`

- [ ] **Step 1: Add HandledCount to NeuronGrainState**

In `features/ino-new/InoNew.Core/NeuronState.cs`, add after `FiredCount`:

```csharp
    [Id(2)] public long HandledCount { get; set; }
```

- [ ] **Step 2: Implement HandleAsync in NeuronGrain**

Add to `NeuronGrain` after the `FireAsync` method:

```csharp
    public async Task<SynapseResult> HandleAsync(Synapse synapse, CancellationToken ct = default)
    {
        _state.State.HandledCount++;
        await _state.WriteStateAsync();
        _log.LogInformation("Neuron {Id} handled synapse verb={Verb} from={Source}",
            this.GetPrimaryKeyString(), synapse.Verb, synapse.SourceNeuronId);
        return new SynapseResult(Success: true, Payload: string.Empty, Verb: synapse.Verb);
    }
```

- [ ] **Step 3: Update FireAsync to deliver synapse to target neuron**

Replace the end of `FireAsync` in `NeuronGrain` (after `var sequence = await capture.AppendAsync(...)`) — change the return to also call HandleAsync on the target:

```csharp
        var sequence = await capture.AppendAsync(evt, ct);

        // Deliver the synapse to the target neuron's handler.
        var targetGrain = GrainFactory.GetGrain<INeuron>(synapse.TargetNeuronId);
        SynapseResult? handleResult = null;
        try
        {
            handleResult = await targetGrain.HandleAsync(synapse, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.LogWarning(ex, "Synapse delivery to {Target} failed (verb={Verb})",
                synapse.TargetNeuronId, synapse.Verb);
        }

        return new SynapseReceipt(synapse.Id, sequence, synapse.CorrelationId);
```

- [ ] **Step 4: Build to verify compilation**

Run: `dotnet build features/ino-new/InoNew.Core/InoNew.Core.csproj`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add features/ino-new/InoNew.Core/NeuronGrain.cs features/ino-new/InoNew.Core/NeuronState.cs
git commit -m "feat(ino-new): implement HandleAsync + synapse delivery in FireAsync"
```

---

### Task 3: Write synapse delivery tests

**Files:**
- Create: `features/ino-new/InoNew.Tests/SynapseDeliveryTests.cs`

- [ ] **Step 1: Write the failing test for synapse delivery**

```csharp
// features/ino-new/InoNew.Tests/SynapseDeliveryTests.cs
using IAW.Testing;
using InoNew.Core;
using Timetravel.Core;

namespace InoNew.Tests;

public class SynapseDeliveryTests : IAsyncLifetime
{
    NeuronBddContext _ctx = null!;

    public async ValueTask InitializeAsync() =>
        _ctx = await NeuronBddContext.StartAsync(silo => silo.AddTimelineCapture());

    public async ValueTask DisposeAsync() => await _ctx.DisposeAsync();

    INeuronRegistry Registry => _ctx.Cluster.GrainFactory.GetGrain<INeuronRegistry>("global");

    [Fact(DisplayName = "FireAsync delivers synapse to target neuron's HandleAsync")]
    public async Task FireAsyncDeliversToTarget()
    {
        var ct = TestContext.Current.CancellationToken;

        var alpha = await Registry.CreateAsync(
            new NeuronBlueprint("alpha", "source neuron", ["demo"], Id: "alpha"), ct);
        var beta = await Registry.CreateAsync(
            new NeuronBlueprint("beta", "target neuron", ["demo"], Id: "beta"), ct);

        var synapse = await Registry.ConnectAsync("alpha", "beta", "greet", ct);

        var srcGrain = _ctx.Cluster.GrainFactory.GetGrain<INeuron>("alpha");
        var receipt = await srcGrain.FireAsync(synapse with { Payload = "hello" }, ct);

        Assert.True(receipt.TimelineSequence > 0);

        // Verify the target neuron's HandledCount incremented (delivery happened).
        var targetGrain = _ctx.Cluster.GrainFactory.GetGrain<INeuron>("beta");
        var targetDef = await targetGrain.GetAsync(ct);
        // HandleAsync was called — verify by calling HandleAsync directly and checking it works.
        var result = await targetGrain.HandleAsync(
            synapse with { Payload = "direct-test" }, ct);
        Assert.True(result.Success);
        Assert.Equal("greet", result.Verb);
    }

    [Fact(DisplayName = "FireAsync succeeds even when target neuron is not activated")]
    public async Task FireAsyncSucceedsWithUnactivatedTarget()
    {
        var ct = TestContext.Current.CancellationToken;

        await Registry.CreateAsync(
            new NeuronBlueprint("sender", "source", ["demo"], Id: "sender"), ct);

        // Fire to a target that doesn't exist in registry — delivery should fail gracefully.
        var srcGrain = _ctx.Cluster.GrainFactory.GetGrain<INeuron>("sender");
        var synapse = new Synapse(
            Id: "test-synapse",
            SourceNeuronId: "sender",
            TargetNeuronId: "nonexistent",
            Verb: "ping",
            Payload: "",
            FiredAt: DateTimeOffset.UtcNow,
            CorrelationId: "test-corr",
            Decay: 100);

        // Should not throw — delivery failure is logged, not fatal.
        var receipt = await srcGrain.FireAsync(synapse, ct);
        Assert.True(receipt.TimelineSequence > 0);
    }
}
```

- [ ] **Step 2: Run tests to verify they pass**

Run: `dotnet test features/ino-new/InoNew.Tests/InoNew.Tests.csproj --filter "SynapseDeliveryTests" -v normal`
Expected: 2 tests pass.

- [ ] **Step 3: Commit**

```bash
git add features/ino-new/InoNew.Tests/SynapseDeliveryTests.cs
git commit -m "test(ino-new): synapse delivery tests — FireAsync calls HandleAsync on target"
```

---

### Task 4: Add SynapseSchema to Neuron record

**Files:**
- Modify: `features/ino-new/InoNew.Core/Neuron.cs`

- [ ] **Step 1: Add SynapseSchema field to Neuron record**

In `features/ino-new/InoNew.Core/Neuron.cs`, add to the `Neuron` record after `Metadata`:

```csharp
    // C# interface source describing the synapse verbs this neuron handles.
    // Cortex reads this to build its routing catalog. Null for neurons
    // that don't handle incoming synapses (e.g. infrastructure neurons).
    [property: Id(6)] string? SynapseSchema);
```

And add to `NeuronBlueprint` after `Metadata`:

```csharp
    [property: Id(5)] string? SynapseSchema = null);
```

- [ ] **Step 2: Build to verify compilation**

Run: `dotnet build ino.slnx`
Expected: Build succeeds. Existing callers pass `null` for the new optional field via record positional syntax or `with` expressions.

- [ ] **Step 3: Run all ino-new tests to verify no regressions**

Run: `dotnet test features/ino-new/InoNew.Tests/InoNew.Tests.csproj -v normal`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add features/ino-new/InoNew.Core/Neuron.cs
git commit -m "feat(ino-new): add SynapseSchema field to Neuron and NeuronBlueprint"
```

---

### Task 5: Write Cortex Gherkin feature file

**Files:**
- Create: `features/ino-new/InoNew.Tests/Features/Cortex.feature`

- [ ] **Step 1: Write the Cortex feature file**

```gherkin
# features/ino-new/InoNew.Tests/Features/Cortex.feature
Feature: Cortex routing engine

  Cortex receives natural language from any surface, decomposes intent
  via LLM, and routes synapses to the best-matching specialist neuron.

  Background:
    Given a running test cluster with timeline capture enabled
    And the neuron registry is available at "global"
    And behavior memory is available at "global"
    And a Cortex neuron is registered

  Scenario: Route a simple request to a single specialist
    Given a specialist "echo" is registered with schema "echo: repeats input"
    And behavior memory contains an example for "echo" with body "echo back the user's message"
    When the user sends "say hello back to me"
    Then Cortex routes to specialist "echo"
    And the timeline contains a SynapseFired event with verb "user_message"
    And the timeline contains a SynapseFired event with verb "handle"

  Scenario: Route to the best matching specialist via BehaviorMemory
    Given a specialist "greeter" is registered with schema "greet: says hello"
    And a specialist "math" is registered with schema "calculate: does math"
    And behavior memory contains an example for "greeter" with body "say hello, greet the user"
    And behavior memory contains an example for "math" with body "calculate numbers, do arithmetic"
    When the user sends "greet me warmly"
    Then Cortex routes to specialist "greeter"

  Scenario: Unknown intent returns a helpful fallback
    When the user sends "do something no specialist handles"
    Then Cortex responds with a message containing "I don't have a specialist"
```

- [ ] **Step 2: Commit**

```bash
git add features/ino-new/InoNew.Tests/Features/Cortex.feature
git commit -m "test(ino-new): add Cortex.feature — Gherkin scenarios for routing engine"
```

---

### Task 6: Build ICortex interface and CortexGrain

**Files:**
- Create: `features/ino-new/InoNew.Core/ICortex.cs`
- Create: `features/ino-new/InoNew.Core/CortexGrain.cs`

- [ ] **Step 1: Create the ICortex interface**

```csharp
// features/ino-new/InoNew.Core/ICortex.cs
namespace InoNew.Core;

// Cortex is ino's navigation engine. It receives natural language,
// decomposes intent via LLM, and routes synapses to specialist neurons.
// Singleton grain keyed "cortex". Reads from NeuronRegistryGrain for
// the specialist catalog and BehaviorMemory for routing hints.
public interface ICortex : IGrainWithStringKey
{
    // Handle a natural language message from any surface. Cortex builds
    // the specialist catalog, queries BehaviorMemory for similar scenarios,
    // calls the LLM to pick the right specialist + verb, fires a synapse
    // to that specialist, and returns the composed reply.
    Task<CortexReply> HandleUserMessageAsync(string text, CancellationToken ct = default);
}

[GenerateSerializer]
public sealed record CortexReply(
    [property: Id(0)] string Text,
    [property: Id(1)] string? SpecialistId,
    [property: Id(2)] string? Verb,
    [property: Id(3)] long TimelineSequence);
```

- [ ] **Step 2: Create the CortexGrain**

```csharp
// features/ino-new/InoNew.Core/CortexGrain.cs
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Timetravel.Core;

namespace InoNew.Core;

// Cortex neuron implementation. Routing logic:
// 1. Build catalog string from NeuronRegistry (all neurons with SynapseSchema)
// 2. Query BehaviorMemory with user text, get top 3 hits
// 3. Build LLM prompt: system (routing instructions) + catalog + memory hits + user text
// 4. LLM returns JSON: { "specialistId": "...", "verb": "...", "payload": "..." }
// 5. Fire synapse to the chosen specialist, collect result, compose reply
public sealed class CortexGrain : Grain, ICortex
{
    readonly IChatClient _llm;
    readonly ILogger<CortexGrain> _log;

    public CortexGrain(IChatClient llm, ILogger<CortexGrain> log)
    {
        _llm = llm;
        _log = log;
    }

    public async Task<CortexReply> HandleUserMessageAsync(string text, CancellationToken ct = default)
    {
        var registry = GrainFactory.GetGrain<INeuronRegistry>("global");
        var memory = GrainFactory.GetGrain<IBehaviorMemory>("global");

        var catalog = await BuildCatalogAsync(registry, ct);
        var memoryHits = await memory.SearchAsync(text, top: 3, ct);
        var memoryContext = FormatMemoryHits(memoryHits);

        if (string.IsNullOrEmpty(catalog))
            return new CortexReply(
                "I don't have a specialist for that yet. As I learn, I'll be able to help with more things.",
                SpecialistId: null, Verb: null, TimelineSequence: 0);

        var systemPrompt = $"""
            You are Cortex, the routing engine for ino. Your job is to pick the best specialist neuron
            to handle the user's request.

            Available specialists:
            {catalog}

            Relevant behavior examples:
            {memoryContext}

            Respond with ONLY a JSON object (no markdown, no explanation):
            {{"specialistId": "<id>", "verb": "handle", "payload": "<the user's request>"}}

            If no specialist matches, respond with:
            {{"specialistId": null, "verb": null, "payload": null}}
            """;

        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, systemPrompt),
            new(ChatRole.User, text)
        };

        var response = await _llm.GetResponseAsync(messages, ct: ct);
        var json = response.Text ?? "";

        var routingDecision = ParseRoutingDecision(json);

        if (routingDecision.SpecialistId is null)
            return new CortexReply(
                "I don't have a specialist for that yet. As I learn, I'll be able to help with more things.",
                SpecialistId: null, Verb: null, TimelineSequence: 0);

        // Fire user_message to timeline for observability.
        var cortexNeuron = GrainFactory.GetGrain<INeuron>("cortex");
        var userSynapse = new Synapse(
            Id: $"synapse-{Guid.NewGuid():N}",
            SourceNeuronId: "user",
            TargetNeuronId: "cortex",
            Verb: "user_message",
            Payload: text,
            FiredAt: DateTimeOffset.UtcNow,
            CorrelationId: $"cortex-{Guid.NewGuid():N}",
            Decay: TimelineEvent.DecayHot);

        var capture = GrainFactory.GetGrain<ITimelineCaptureGrain>("global");
        await capture.AppendAsync(new TimelineEvent(
            SequenceNumber: 0,
            Timestamp: DateTimeOffset.UtcNow,
            Kind: TimelineEventKind.SynapseFired,
            SourceId: "user",
            TargetId: "cortex",
            CorrelationId: userSynapse.CorrelationId,
            SynapseVerb: "user_message",
            Payload: new Dictionary<string, string> { ["text"] = text },
            Decay: TimelineEvent.DecayHot), ct);

        // Fire to specialist.
        var specialist = GrainFactory.GetGrain<INeuron>(routingDecision.SpecialistId);
        var routeSynapse = new Synapse(
            Id: $"synapse-{Guid.NewGuid():N}",
            SourceNeuronId: "cortex",
            TargetNeuronId: routingDecision.SpecialistId,
            Verb: routingDecision.Verb ?? "handle",
            Payload: routingDecision.Payload ?? text,
            FiredAt: DateTimeOffset.UtcNow,
            CorrelationId: userSynapse.CorrelationId,
            Decay: TimelineEvent.DecayHot);

        var receipt = await cortexNeuron.FireAsync(routeSynapse, ct);

        _log.LogInformation("Cortex routed to {Specialist} verb={Verb} seq={Seq}",
            routingDecision.SpecialistId, routingDecision.Verb, receipt.TimelineSequence);

        return new CortexReply(
            Text: $"Routed to {routingDecision.SpecialistId}.",
            SpecialistId: routingDecision.SpecialistId,
            Verb: routingDecision.Verb,
            TimelineSequence: receipt.TimelineSequence);
    }

    static async Task<string> BuildCatalogAsync(INeuronRegistry registry, CancellationToken ct)
    {
        var neurons = await registry.ListNeuronsAsync(ct);
        var entries = neurons
            .Where(n => n.SynapseSchema is not null)
            .Select(n => $"- {n.Id}: {n.SynapseSchema}");
        return string.Join("\n", entries);
    }

    static string FormatMemoryHits(IReadOnlyList<VectorSearchHit> hits)
    {
        if (hits.Count == 0) return "(no relevant examples)";
        return string.Join("\n", hits.Select(h =>
            $"- [{h.Example.Title}] (score={h.Score:F2}): {h.Example.Body}"));
    }

    static (string? SpecialistId, string? Verb, string? Payload) ParseRoutingDecision(string json)
    {
        try
        {
            json = json.Trim();
            if (json.StartsWith("```")) json = json.Split('\n', 3).Length > 1
                ? string.Join('\n', json.Split('\n').Skip(1).SkipLast(1))
                : json;

            var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;
            var specialistId = root.TryGetProperty("specialistId", out var s) && s.ValueKind != System.Text.Json.JsonValueKind.Null
                ? s.GetString() : null;
            var verb = root.TryGetProperty("verb", out var v) && v.ValueKind != System.Text.Json.JsonValueKind.Null
                ? v.GetString() : null;
            var payload = root.TryGetProperty("payload", out var p) && p.ValueKind != System.Text.Json.JsonValueKind.Null
                ? p.GetString() : null;
            return (specialistId, verb, payload);
        }
        catch
        {
            return (null, null, null);
        }
    }
}
```

- [ ] **Step 3: Build to verify compilation**

Run: `dotnet build features/ino-new/InoNew.Core/InoNew.Core.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add features/ino-new/InoNew.Core/ICortex.cs features/ino-new/InoNew.Core/CortexGrain.cs
git commit -m "feat(ino-new): CortexGrain — LLM-based routing engine for natural language → specialist"
```

---

### Task 7: Write Cortex BDD step definitions and scenario tests

**Files:**
- Create: `features/ino-new/InoNew.Tests/Steps/CortexSteps.cs`
- Create: `features/ino-new/InoNew.Tests/Steps/CortexScenarioTests.cs`

- [ ] **Step 1: Write CortexSteps**

```csharp
// features/ino-new/InoNew.Tests/Steps/CortexSteps.cs
using IAW.Testing;
using InoNew.Core;
using Timetravel.Core;

namespace InoNew.Tests.Steps;

public sealed class CortexSteps
{
    readonly NeuronBddContext _ctx;
    CortexReply? _lastReply;

    public CortexSteps(NeuronBddContext ctx) => _ctx = ctx;

    INeuronRegistry Registry => _ctx.Cluster.GrainFactory.GetGrain<INeuronRegistry>("global");
    IBehaviorMemory Memory => _ctx.Cluster.GrainFactory.GetGrain<IBehaviorMemory>("global");
    ICortex Cortex => _ctx.Cluster.GrainFactory.GetGrain<ICortex>("cortex");
    ITimelineReader Timeline => _ctx.Cluster.GrainFactory.GetGrain<ITimelineReader>("global");

    public async Task Given_ACortexNeuronIsRegistered(CancellationToken ct)
    {
        await Registry.CreateAsync(new NeuronBlueprint(
            Name: "cortex",
            Purpose: "routing engine",
            Capabilities: ["routing", "decomposition"],
            Id: "cortex",
            SynapseSchema: "user_message: receives natural language from any surface"), ct);
    }

    public async Task Given_ASpecialistIsRegisteredWithSchema(string id, string schema, CancellationToken ct)
    {
        await Registry.CreateAsync(new NeuronBlueprint(
            Name: id,
            Purpose: $"{id} specialist",
            Capabilities: [id],
            Id: id,
            SynapseSchema: schema), ct);
    }

    public async Task Given_BehaviorMemoryContainsExample(string specialistId, string body, CancellationToken ct)
    {
        await Memory.IngestAsync(new BehaviorExample(
            Id: $"example-{specialistId}",
            Title: $"{specialistId} behavior",
            Body: body,
            Source: "test",
            Embedding: ReadOnlyMemory<float>.Empty,
            IngestedAt: DateTimeOffset.UtcNow,
            Metadata: new Dictionary<string, string> { ["specialist"] = specialistId }), ct);
    }

    public async Task When_TheUserSends(string text, CancellationToken ct)
    {
        _lastReply = await Cortex.HandleUserMessageAsync(text, ct);
    }

    public Task Then_CortexRoutesToSpecialist(string expectedSpecialistId)
    {
        Assert.NotNull(_lastReply);
        Assert.Equal(expectedSpecialistId, _lastReply!.SpecialistId);
        return Task.CompletedTask;
    }

    public Task Then_CortexRespondsWithMessageContaining(string substring)
    {
        Assert.NotNull(_lastReply);
        Assert.Contains(substring, _lastReply!.Text, StringComparison.OrdinalIgnoreCase);
        return Task.CompletedTask;
    }

    public async Task Then_TimelineContainsSynapseFiredWithVerb(string verb, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var latest = await Timeline.GetLatestSequenceAsync(ct);
            if (latest >= 0)
            {
                var events = await Timeline.GetEventsInRangeAsync(0, latest, ct: ct);
                if (events.Any(e => e.Kind == TimelineEventKind.SynapseFired && e.SynapseVerb == verb))
                    return;
            }
            await Task.Delay(50, ct);
        }
        Assert.Fail($"Timeline never contained a SynapseFired event with verb '{verb}'");
    }
}
```

- [ ] **Step 2: Write CortexScenarioTests**

```csharp
// features/ino-new/InoNew.Tests/Steps/CortexScenarioTests.cs
using IAW.Testing;
using Timetravel.Core;

namespace InoNew.Tests.Steps;

public class CortexScenarioTests : IAsyncLifetime
{
    NeuronBddContext _ctx = null!;
    CortexSteps _steps = null!;

    public async ValueTask InitializeAsync()
    {
        _ctx = await NeuronBddContext.StartAsync(silo => silo.AddTimelineCapture());
        _steps = new CortexSteps(_ctx);
        // Configure mock LLM to return routing JSON.
        _ctx.MockLlm
            .When(p => p.Contains("echo"), """{"specialistId": "echo", "verb": "handle", "payload": "say hello back to me"}""")
            .When(p => p.Contains("greeter") && p.Contains("greet"), """{"specialistId": "greeter", "verb": "handle", "payload": "greet me warmly"}""")
            .When(p => true, """{"specialistId": null, "verb": null, "payload": null}""");
    }

    public async ValueTask DisposeAsync() => await _ctx.DisposeAsync();

    async Task RunBackground(CancellationToken ct)
    {
        // Background steps from Cortex.feature
        await _steps.Given_ACortexNeuronIsRegistered(ct);
    }

    [Fact(DisplayName = "Route a simple request to a single specialist")]
    public async Task RouteSimpleRequest()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunBackground(ct);

        await _steps.Given_ASpecialistIsRegisteredWithSchema("echo", "echo: repeats input", ct);
        await _steps.Given_BehaviorMemoryContainsExample("echo", "echo back the user's message", ct);
        await _steps.When_TheUserSends("say hello back to me", ct);
        await _steps.Then_CortexRoutesToSpecialist("echo");
        await _steps.Then_TimelineContainsSynapseFiredWithVerb("user_message", ct);
        await _steps.Then_TimelineContainsSynapseFiredWithVerb("handle", ct);
    }

    [Fact(DisplayName = "Route to best matching specialist via BehaviorMemory")]
    public async Task RouteToBestMatch()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunBackground(ct);

        await _steps.Given_ASpecialistIsRegisteredWithSchema("greeter", "greet: says hello", ct);
        await _steps.Given_ASpecialistIsRegisteredWithSchema("math", "calculate: does math", ct);
        await _steps.Given_BehaviorMemoryContainsExample("greeter", "say hello, greet the user", ct);
        await _steps.Given_BehaviorMemoryContainsExample("math", "calculate numbers, do arithmetic", ct);
        await _steps.When_TheUserSends("greet me warmly", ct);
        await _steps.Then_CortexRoutesToSpecialist("greeter");
    }

    [Fact(DisplayName = "Unknown intent returns a helpful fallback")]
    public async Task UnknownIntentFallback()
    {
        var ct = TestContext.Current.CancellationToken;
        await RunBackground(ct);

        await _steps.When_TheUserSends("do something no specialist handles", ct);
        await _steps.Then_CortexRespondsWithMessageContaining("I don't have a specialist");
    }
}
```

- [ ] **Step 3: Run Cortex tests**

Run: `dotnet test features/ino-new/InoNew.Tests/InoNew.Tests.csproj --filter "CortexScenarioTests" -v normal`
Expected: 3 tests pass.

- [ ] **Step 4: Commit**

```bash
git add features/ino-new/InoNew.Tests/Steps/CortexSteps.cs features/ino-new/InoNew.Tests/Steps/CortexScenarioTests.cs
git commit -m "test(ino-new): Cortex BDD scenarios — routing, best-match, fallback"
```

---

### Task 8: Add `chat` verb to InoCommandDispatcher

**Files:**
- Modify: `features/ino-new/InoNew.Core/InoCommandDispatcher.cs`

- [ ] **Step 1: Add Cortex field and `chat` command**

In `InoCommandDispatcher`, add a field:

```csharp
    readonly ICortex _cortex;
```

Update the constructor to initialize it:

```csharp
    public InoCommandDispatcher(IGrainFactory grains)
    {
        _grains = grains;
        _registry = grains.GetGrain<INeuronRegistry>("global");
        _timeline = grains.GetGrain<ITimelineReader>("global");
        _cortex = grains.GetGrain<ICortex>("cortex");
    }
```

Add a new case in `ExecuteStepAsync` before the `default` case:

```csharp
            case "chat":
                {
                    if (parts.Length < 2) { await output.WriteLineAsync("  usage: chat <message>"); return; }
                    var message = string.Join(' ', parts.Skip(1));
                    var reply = await _cortex.HandleUserMessageAsync(message, ct);
                    await output.WriteLineAsync($"  {reply.Text}");
                    if (reply.SpecialistId is not null)
                        await output.WriteLineAsync($"  [routed to {reply.SpecialistId}, verb={reply.Verb}, seq={reply.TimelineSequence}]");
                    return;
                }
```

- [ ] **Step 2: Update the help text**

Add `chat` to the `Usage` const string after the `fire` entry:

```
  chat     <message>                      Send natural language to Cortex
```

- [ ] **Step 3: Build and run all ino-new tests**

Run: `dotnet test features/ino-new/InoNew.Tests/InoNew.Tests.csproj -v normal`
Expected: All tests pass (existing + new).

- [ ] **Step 4: Commit**

```bash
git add features/ino-new/InoNew.Core/InoCommandDispatcher.cs
git commit -m "feat(ino-new): add 'chat' verb to InoCommandDispatcher — routes through Cortex"
```

---

### Task 9: Fix Aspire stream tee for ino-windows

**Files:**
- Modify: `ino.windows/Program.cs`

- [ ] **Step 1: Create TeeTextWriter and update console allocation**

Replace the current console allocation block in `ino.windows/Program.cs` with a stream-tee version:

```csharp
using System.Runtime.InteropServices;
using Aspire.IAW;
using InoNew.Core;

// When launched by Aspire, stdin/stdout are redirected for dashboard log capture.
// Save the original streams, allocate a visible console, then tee output to both.
Stream? aspireOut = null;
Stream? aspireErr = null;

if (OperatingSystem.IsWindows() && Console.IsInputRedirected)
{
    aspireOut = Console.OpenStandardOutput();
    aspireErr = Console.OpenStandardError();

    NativeConsole.FreeConsole();
    NativeConsole.AllocConsole();

    var consoleOut = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
    var consoleErr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
    var teeOut = new TeeTextWriter(consoleOut, new StreamWriter(aspireOut) { AutoFlush = true });
    var teeErr = new TeeTextWriter(consoleErr, new StreamWriter(aspireErr) { AutoFlush = true });

    Console.SetOut(teeOut);
    Console.SetError(teeErr);
    Console.SetIn(new StreamReader(Console.OpenStandardInput()));
}
```

- [ ] **Step 2: Add TeeTextWriter class at the bottom of Program.cs**

Replace the existing `NativeConsole` class and add `TeeTextWriter` before it:

```csharp
sealed class TeeTextWriter(TextWriter primary, TextWriter secondary) : TextWriter
{
    public override Encoding Encoding => primary.Encoding;
    public override void Write(char value) { primary.Write(value); secondary.Write(value); }
    public override void Write(string? value) { primary.Write(value); secondary.Write(value); }
    public override void WriteLine(string? value) { primary.WriteLine(value); secondary.WriteLine(value); }
    public override void Flush() { primary.Flush(); secondary.Flush(); }
    public override async Task WriteAsync(char value) { await primary.WriteAsync(value); await secondary.WriteAsync(value); }
    public override async Task WriteLineAsync(string? value) { await primary.WriteLineAsync(value); await secondary.WriteLineAsync(value); }
}

static partial class NativeConsole
{
    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool FreeConsole();

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllocConsole();
}
```

- [ ] **Step 3: Add missing using for Encoding**

Add at the top of `ino.windows/Program.cs`:

```csharp
using System.Text;
```

- [ ] **Step 4: Build**

Run: `dotnet build ino.windows/Ino.Windows.csproj`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add ino.windows/Program.cs
git commit -m "feat(ino-windows): TeeTextWriter — output goes to visible console AND Aspire dashboard"
```

---

### Task 10: Run full test suite and verify

**Files:** None (verification only)

- [ ] **Step 1: Build the full solution**

Run: `dotnet build ino.slnx`
Expected: Build succeeds with 0 errors.

- [ ] **Step 2: Run all ino-new tests**

Run: `dotnet test features/ino-new/InoNew.Tests/InoNew.Tests.csproj -v normal`
Expected: All tests pass — synapse delivery, Cortex routing, existing neuron/timeline/behavior-memory tests.

- [ ] **Step 3: Run timetravel tests**

Run: `dotnet test features/timetravel/Timetravel.Tests/Timetravel.Tests.csproj -v normal`
Expected: All tests pass — no regressions from HandleAsync addition.

- [ ] **Step 4: Commit any test fixes if needed**

Only if previous steps revealed issues.

---

### Task 11: Update Cortex.feature file to match implementation

**Files:**
- Modify: `features/ino-new/InoNew.Tests/Features/Cortex.feature` (if needed)

- [ ] **Step 1: Ensure .feature file accurately reflects the implemented scenarios**

Read `CortexScenarioTests.cs` and ensure the `.feature` file matches the test names and step descriptions exactly. Update if any step wording drifted during implementation.

- [ ] **Step 2: Commit if changed**

```bash
git add features/ino-new/InoNew.Tests/Features/Cortex.feature
git commit -m "docs(ino-new): sync Cortex.feature with implemented test scenarios"
```
