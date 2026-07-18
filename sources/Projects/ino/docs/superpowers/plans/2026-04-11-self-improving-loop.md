# Self-Improving Loop Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close ino's self-improving loop — when SearchEngine can't route a query, an EvolutionHandler creates a new neuron at runtime via LLM + in-process CSharpScript, so the next identical query routes to it.

**Architecture:** SearchEngine fires a `no_match` synapse to an `evolution` neuron. EvolutionHandler (compile-time ISynapseHandler) calls the LLM to generate a Blueprint with ScriptSource, validates it via Roslyn compile, registers it in NeuronRegistry, then fires the query to the new neuron. NeuronGrain.HandleAsync falls back to CSharpScript.RunAsync when no DI handler exists but ScriptSource is present.

**Tech Stack:** Orleans grains, Microsoft.CodeAnalysis.CSharp.Scripting, Microsoft.Extensions.AI (IChatClient), xunit.v3 BDD tests

**Spec:** `docs/superpowers/specs/2026-04-11-self-improving-loop-design.md`

---

### Task 1: Data model updates — field renames + new fields

All mechanical renames and new field additions. This is the foundation — everything else depends on it.

**Files:**
- Modify: `features/ino-new/InoNew.Core/Synapse.cs`
- Modify: `features/ino-new/InoNew.Core/Neuron.cs`
- Modify: `features/ino-new/InoNew.Core/NeuronState.cs`
- Modify: `features/ino-new/InoNew.Core/NeuronRegistryState.cs`
- Modify: `features/ino-new/InoNew.Core/INeuronRegistry.cs`
- Modify: `features/ino-new/InoNew.Core/NeuronGrain.cs`
- Modify: `features/ino-new/InoNew.Core/NeuronRegistryGrain.cs`
- Modify: `features/ino-new/InoNew.Core/SearchEngineGrain.cs`
- Modify: `features/ino-new/InoNew.Core/InoCommandDispatcher.cs`
- Modify: `features/ino-new/InoNew.Tests/Steps/SearchEngineSteps.cs`
- Modify: `features/ino-new/InoNew.Tests/Steps/SpecialistSteps.cs`
- Modify: `features/ino-new/InoNew.Tests/Steps/ShellSpecialistTests.cs`
- Modify: `features/ino-new/InoNew.Tests/Steps/SchedulerSpecialistTests.cs`
- Modify: `features/ino-new/InoNew.Tests/Steps/RecallSpecialistTests.cs`
- Modify: `features/ino-new/InoNew.Tests/Steps/FileDeliverySpecialistTests.cs`
- Modify: `features/ino-new/InoNew.Tests/Steps/SummarizerSpecialistTests.cs`
- Modify: `features/ino-new/InoNew.Tests/Steps/SearchEngineScenarioTests.cs`
- Modify: `features/ino-new/InoNew.Tests/InoNew.Tests.csproj`
- Modify: `iaw/Telegram/Services/InoService.cs`
- Modify: All files that construct `Synapse(...)` or `NeuronBlueprint(...)` records

- [ ] **Step 1: Rename Synapse fields SourceNeuronId → SourceId, TargetNeuronId → TargetId**

In `features/ino-new/InoNew.Core/Synapse.cs`, change the record:

```csharp
[GenerateSerializer]
public sealed record Synapse(
    [property: Id(0)] string Id,
    [property: Id(1)] string SourceId,
    [property: Id(2)] string TargetId,
    [property: Id(3)] string Verb,
    [property: Id(4)] string Payload,
    [property: Id(5)] DateTimeOffset FiredAt,
    [property: Id(6)] string CorrelationId,
    [property: Id(7)] int Decay);
```

Then do a codebase-wide find-and-replace:
- `SourceNeuronId` → `SourceId` (in all `.cs` files under `features/ino-new/` and `iaw/Telegram/Services/InoService.cs`)
- `TargetNeuronId` → `TargetId` (same scope)

Key call sites to update:
- `SearchEngineGrain.cs` lines 62, 64, 75, 77 — synapse construction
- `NeuronGrain.cs` lines 56, 66 — timeline event payload references
- `NeuronRegistryGrain.cs` lines 96, 106, 121 — synapse construction and timeline
- `InoCommandDispatcher.cs` lines 131, 153, 159 — synapse construction and display
- `SpecialistSteps.cs` lines 67, 77 — synapse construction
- `InoService.cs` line 188-192 — synapse construction
- All specialist test files that construct Synapse records

- [ ] **Step 2: Rename NeuronBlueprint → Blueprint**

In `features/ino-new/InoNew.Core/Neuron.cs`, rename the record type:

```csharp
[GenerateSerializer]
public sealed record Blueprint(
    [property: Id(0)] string Name,
    [property: Id(1)] string Purpose,
    [property: Id(2)] IReadOnlyList<string> Capabilities,
    [property: Id(3)] string? Id = null,
    [property: Id(4)] IReadOnlyDictionary<string, string>? Metadata = null,
    [property: Id(5)] string? SynapseSchema = null,
    [property: Id(6)] global::Core.ML.FeatureSchema? FeatureSchema = null);
```

Update `INeuronRegistry.cs`:
```csharp
Task<Neuron> CreateAsync(Blueprint blueprint, CancellationToken ct = default);
```

Update `NeuronRegistryGrain.cs` parameter type in `CreateAsync`.

Codebase-wide: replace `NeuronBlueprint` → `Blueprint` in all `.cs` files. Key call sites:
- `NeuronRegistryGrain.cs` CreateAsync parameter
- `SearchEngineSteps.cs`, `SpecialistSteps.cs` — `new NeuronBlueprint(` → `new Blueprint(`
- `InoCommandDispatcher.cs` — `new NeuronBlueprint(` → `new Blueprint(`

- [ ] **Step 3: Rename NeuronGrainState → NeuronState, NeuronRegistryState → RegistryState**

In `features/ino-new/InoNew.Core/NeuronState.cs`:
```csharp
[GenerateSerializer]
public sealed class NeuronState
{
    [Id(0)] public Neuron? Definition { get; set; }
    [Id(1)] public long FiredCount { get; set; }
    [Id(2)] public long HandledCount { get; set; }
}
```

In `features/ino-new/InoNew.Core/NeuronRegistryState.cs`:
```csharp
[GenerateSerializer]
public sealed class RegistryState
{
    [Id(0)] public Dictionary<string, Neuron> Neurons { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    [Id(1)] public List<Synapse> Synapses { get; set; } = new();
}
```

Update `NeuronGrain.cs` — change `IPersistentState<NeuronGrainState>` → `IPersistentState<NeuronState>`.
Update `NeuronRegistryGrain.cs` — change `IPersistentState<NeuronRegistryState>` → `IPersistentState<RegistryState>`.

- [ ] **Step 4: Add ScriptSource, AuthorId, DomainId to Neuron and Blueprint**

In `features/ino-new/InoNew.Core/Neuron.cs`, add fields to both records:

Neuron record — add after FeatureSchema:
```csharp
[GenerateSerializer]
public sealed record Neuron(
    [property: Id(0)] string Id,
    [property: Id(1)] string Name,
    [property: Id(2)] string Purpose,
    [property: Id(3)] IReadOnlyList<string> Capabilities,
    [property: Id(4)] DateTimeOffset CreatedAt,
    [property: Id(5)] IReadOnlyDictionary<string, string> Metadata,
    [property: Id(6)] string? SynapseSchema = null,
    [property: Id(7)] global::Core.ML.FeatureSchema? FeatureSchema = null,
    [property: Id(8)] string? ScriptSource = null,
    [property: Id(9)] string? AuthorId = null,
    [property: Id(10)] string DomainId = "default");
```

Blueprint record — add after FeatureSchema:
```csharp
[GenerateSerializer]
public sealed record Blueprint(
    [property: Id(0)] string Name,
    [property: Id(1)] string Purpose,
    [property: Id(2)] IReadOnlyList<string> Capabilities,
    [property: Id(3)] string? Id = null,
    [property: Id(4)] IReadOnlyDictionary<string, string>? Metadata = null,
    [property: Id(5)] string? SynapseSchema = null,
    [property: Id(6)] global::Core.ML.FeatureSchema? FeatureSchema = null,
    [property: Id(7)] string? ScriptSource = null,
    [property: Id(8)] string? AuthorId = null,
    [property: Id(9)] string DomainId = "default");
```

Update `NeuronRegistryGrain.CreateAsync` to pass new fields when building Neuron from Blueprint:
```csharp
var neuron = new Neuron(
    Id: id,
    Name: blueprint.Name,
    Purpose: blueprint.Purpose,
    Capabilities: blueprint.Capabilities.ToArray(),
    CreatedAt: DateTimeOffset.UtcNow,
    Metadata: blueprint.Metadata ?? new Dictionary<string, string>(),
    SynapseSchema: blueprint.SynapseSchema,
    FeatureSchema: blueprint.FeatureSchema,
    ScriptSource: blueprint.ScriptSource,
    AuthorId: blueprint.AuthorId,
    DomainId: blueprint.DomainId);
```

- [ ] **Step 5: Fix InoNew.Tests.csproj — rename Cortex.feature → SearchEngine.feature reference**

In `features/ino-new/InoNew.Tests/InoNew.Tests.csproj`, replace:
```xml
<None Update="Features\Cortex.feature">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```
with:
```xml
<None Update="Features\SearchEngine.feature">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

- [ ] **Step 6: Build and run all tests**

Run: `dotnet build features/ino-new/InoNew.Core/InoNew.Core.csproj`
Expected: Build succeeded, 0 errors

Run: `dotnet test features/ino-new/InoNew.Tests/InoNew.Tests.csproj --verbosity normal`
Expected: All 64 tests pass

- [ ] **Step 7: Commit**

```bash
git add features/ino-new/ iaw/Telegram/Services/InoService.cs
git commit -m "refactor: rename Synapse fields, Blueprint type, add ScriptSource/AuthorId/DomainId"
```

---

### Task 2: Add CSharpScript NuGet package + NeuronScriptGlobals

**Files:**
- Modify: `features/ino-new/InoNew.Core/InoNew.Core.csproj`
- Create: `features/ino-new/InoNew.Core/NeuronScriptGlobals.cs`

- [ ] **Step 1: Add Microsoft.CodeAnalysis.CSharp.Scripting package**

In `features/ino-new/InoNew.Core/InoNew.Core.csproj`, add to the PackageReference ItemGroup:

```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.Scripting" />
```

The version is managed by `Directory.Packages.props` — verify the latest version is pinned there. If not, add it.

Run: `dotnet restore features/ino-new/InoNew.Core/InoNew.Core.csproj`
Expected: Restore succeeded

- [ ] **Step 2: Create NeuronScriptGlobals**

Create `features/ino-new/InoNew.Core/NeuronScriptGlobals.cs`:

```csharp
using Microsoft.Extensions.Logging;

namespace InoNew.Core;

// Globals object injected into every CSharpScript-compiled neuron script.
// Scripts access cluster grains via Grains, identify themselves via NeuronId,
// read the incoming synapse from Synapse, and log via Log.
public class NeuronScriptGlobals
{
    public required IGrainFactory Grains { get; init; }
    public required string NeuronId { get; init; }
    public required Synapse Synapse { get; init; }
    public required ILogger Log { get; init; }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build features/ino-new/InoNew.Core/InoNew.Core.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add features/ino-new/InoNew.Core/InoNew.Core.csproj features/ino-new/InoNew.Core/NeuronScriptGlobals.cs
git commit -m "feat: add CSharpScript package and NeuronScriptGlobals for in-process neuron scripts"
```

---

### Task 3: NeuronGrain script runtime (TDD)

NeuronGrain.HandleAsync gets a fallback path: when no ISynapseHandler exists in DI and the neuron's Definition has ScriptSource, compile and run it via CSharpScript.

**Files:**
- Create: `features/ino-new/InoNew.Tests/Features/ScriptRuntime.feature`
- Create: `features/ino-new/InoNew.Tests/Steps/ScriptRuntimeSteps.cs`
- Create: `features/ino-new/InoNew.Tests/Steps/ScriptRuntimeTests.cs`
- Modify: `features/ino-new/InoNew.Core/NeuronGrain.cs`
- Modify: `features/ino-new/InoNew.Tests/InoNew.Tests.csproj`

- [ ] **Step 1: Write the feature file**

Create `features/ino-new/InoNew.Tests/Features/ScriptRuntime.feature`:

```gherkin
Feature: NeuronGrain script runtime

  Background:
    Given a running test cluster with timeline capture

  Scenario: Neuron with ScriptSource executes in-process
    Given a neuron "greeter" with ScriptSource:
      """
      return new SynapseResult(Success: true, Payload: "Hello from script!", Verb: "greeted");
      """
    When a synapse is fired to "greeter" with verb "handle"
    Then the neuron returns SynapseResult with Success true
    And the neuron returns SynapseResult with Payload "Hello from script!"

  Scenario: Script execution failure returns error result
    Given a neuron "broken" with ScriptSource:
      """
      throw new System.InvalidOperationException("intentional failure");
      """
    When a synapse is fired to "broken" with verb "handle"
    Then the neuron returns SynapseResult with Success false
    And the timeline contains an Error event from "broken"

  Scenario: DI handler takes priority over ScriptSource
    Given a specialist "shell" is registered in DI
    And the "shell" neuron also has ScriptSource that returns "script-path"
    When a synapse is fired to "shell" with verb "handle" and payload "echo hi"
    Then the neuron returns SynapseResult with Verb "execute_result"
```

- [ ] **Step 2: Write ScriptRuntimeSteps**

Create `features/ino-new/InoNew.Tests/Steps/ScriptRuntimeSteps.cs`:

```csharp
using IAW.Testing;
using InoNew.Core;
using Timetravel.Core;
using Xunit;

namespace InoNew.Tests.Steps;

public sealed class ScriptRuntimeSteps
{
    readonly NeuronBddContext _ctx;

    public ScriptRuntimeSteps(NeuronBddContext ctx) => _ctx = ctx;

    INeuronRegistry Registry =>
        _ctx.Cluster.GrainFactory.GetGrain<INeuronRegistry>("global");

    ITimelineReader Timeline =>
        _ctx.Cluster.GrainFactory.GetGrain<ITimelineReader>("global");

    public async Task Given_ANeuronWithScriptSource(string id, string scriptSource, CancellationToken ct)
    {
        await Registry.CreateAsync(
            new Blueprint(
                Name: id,
                Purpose: $"script neuron {id}",
                Capabilities: new[] { id },
                Id: id,
                ScriptSource: scriptSource),
            ct);
    }

    public async Task Given_AShellNeuronWithScriptSource(string scriptSource, CancellationToken ct)
    {
        await Registry.CreateAsync(
            new Blueprint(
                Name: "shell",
                Purpose: "shell specialist with script override",
                Capabilities: new[] { "shell" },
                Id: "shell",
                SynapseSchema: "execute: runs OS commands",
                ScriptSource: scriptSource),
            ct);
    }

    public async Task When_SynapseIsFiredTo(string neuronId, string verb, string payload, CancellationToken ct)
    {
        var neuron = _ctx.Cluster.GrainFactory.GetGrain<INeuron>(neuronId);
        var result = await neuron.HandleAsync(new Synapse(
            Id: $"synapse-test-{Guid.NewGuid():N}",
            SourceId: "test-driver",
            TargetId: neuronId,
            Verb: verb,
            Payload: payload,
            FiredAt: DateTimeOffset.UtcNow,
            CorrelationId: $"test-{Guid.NewGuid():N}",
            Decay: TimelineEvent.DecayHot), ct);
        _ctx.Scenario["LastSynapseResult"] = result;
    }

    public Task Then_ResultHasSuccess(bool expected)
    {
        var result = (SynapseResult)_ctx.Scenario["LastSynapseResult"]!;
        Assert.Equal(expected, result.Success);
        return Task.CompletedTask;
    }

    public Task Then_ResultHasPayload(string expected)
    {
        var result = (SynapseResult)_ctx.Scenario["LastSynapseResult"]!;
        Assert.Equal(expected, result.Payload);
        return Task.CompletedTask;
    }

    public Task Then_ResultHasPayloadContaining(string substring)
    {
        var result = (SynapseResult)_ctx.Scenario["LastSynapseResult"]!;
        Assert.Contains(substring, result.Payload);
        return Task.CompletedTask;
    }

    public Task Then_ResultHasVerb(string expected)
    {
        var result = (SynapseResult)_ctx.Scenario["LastSynapseResult"]!;
        Assert.Equal(expected, result.Verb);
        return Task.CompletedTask;
    }

    public async Task Then_TimelineContainsErrorFrom(string sourceId, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var latest = await Timeline.GetLatestSequenceAsync(ct);
            if (latest >= 0)
            {
                var events = await Timeline.GetEventsInRangeAsync(0, latest, ct: ct);
                if (events.Any(e => e.Kind == TimelineEventKind.Error && e.SourceId == sourceId))
                    return;
            }
            await Task.Delay(50, ct);
        }
        Assert.Fail($"Timeline did not contain an Error event from '{sourceId}' within 3s");
    }
}
```

- [ ] **Step 3: Write ScriptRuntimeTests**

Create `features/ino-new/InoNew.Tests/Steps/ScriptRuntimeTests.cs`:

```csharp
using IAW.Testing;
using Timetravel.Core;
using Xunit;

namespace InoNew.Tests.Steps;

public class ScriptRuntimeTests : IAsyncLifetime
{
    NeuronBddContext _ctx = null!;
    ScriptRuntimeSteps _steps = null!;

    public async ValueTask InitializeAsync()
    {
        _ctx = await NeuronBddContext.StartAsync(silo =>
        {
            silo.AddTimelineCapture();
            silo.AddInoNew();
        });
        _steps = new ScriptRuntimeSteps(_ctx);
    }

    public async ValueTask DisposeAsync() => await _ctx.DisposeAsync();

    [Fact(DisplayName = "Neuron with ScriptSource executes in-process")]
    public async Task NeuronWithScriptSourceExecutesInProcess()
    {
        var ct = TestContext.Current.CancellationToken;
        var script = """return new SynapseResult(Success: true, Payload: "Hello from script!", Verb: "greeted");""";

        await _steps.Given_ANeuronWithScriptSource("greeter", script, ct);
        await _steps.When_SynapseIsFiredTo("greeter", "handle", "test", ct);
        await _steps.Then_ResultHasSuccess(true);
        await _steps.Then_ResultHasPayload("Hello from script!");
    }

    [Fact(DisplayName = "Script execution failure returns error result")]
    public async Task ScriptExecutionFailureReturnsErrorResult()
    {
        var ct = TestContext.Current.CancellationToken;
        var script = """throw new System.InvalidOperationException("intentional failure");""";

        await _steps.Given_ANeuronWithScriptSource("broken", script, ct);
        await _steps.When_SynapseIsFiredTo("broken", "handle", "test", ct);
        await _steps.Then_ResultHasSuccess(false);
        await _steps.Then_ResultHasPayloadContaining("intentional failure");
        await _steps.Then_TimelineContainsErrorFrom("broken", ct);
    }

    [Fact(DisplayName = "DI handler takes priority over ScriptSource")]
    public async Task DIHandlerTakesPriorityOverScriptSource()
    {
        var ct = TestContext.Current.CancellationToken;
        var script = """return new SynapseResult(Success: true, Payload: "script-path", Verb: "script");""";

        await _steps.Given_AShellNeuronWithScriptSource(script, ct);
        await _steps.When_SynapseIsFiredTo("shell", "handle", "echo hi", ct);
        await _steps.Then_ResultHasVerb("execute_result");
    }
}
```

- [ ] **Step 4: Add ScriptRuntime.feature to InoNew.Tests.csproj**

Add to the ItemGroup with feature file references:
```xml
<None Update="Features\ScriptRuntime.feature">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

- [ ] **Step 5: Run tests to verify they fail**

Run: `dotnet test features/ino-new/InoNew.Tests/InoNew.Tests.csproj --filter "ScriptRuntime" --verbosity normal`
Expected: Tests fail (ScriptSource not yet handled in NeuronGrain)

- [ ] **Step 6: Implement script runtime in NeuronGrain.HandleAsync**

In `features/ino-new/InoNew.Core/NeuronGrain.cs`, add the following:

1. Add using statements at top:
```csharp
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
```

2. Add a cached runner field to the class:
```csharp
ScriptRunner<NeuronScriptGlobals>? _scriptRunner;
string? _compiledScriptHash;
```

3. Replace HandleAsync with this implementation:

```csharp
public async Task<SynapseResult> HandleAsync(Synapse synapse, CancellationToken ct = default)
{
    _state.State.HandledCount++;
    await _state.WriteStateAsync();

    var neuronId = this.GetPrimaryKeyString();

    // Priority 1: compile-time DI handler
    var handler = ServiceProvider.GetKeyedService<ISynapseHandler>(neuronId);
    if (handler is not null)
    {
        _log.LogInformation("Neuron {Id} dispatching to specialist handler (verb={Verb})",
            neuronId, synapse.Verb);
        return await handler.HandleAsync(synapse, GrainFactory, ct);
    }

    // Priority 2: in-process CSharpScript from ScriptSource
    var scriptSource = _state.State.Definition?.ScriptSource;
    if (scriptSource is not null)
    {
        _log.LogInformation("Neuron {Id} executing ScriptSource (verb={Verb})",
            neuronId, synapse.Verb);
        return await ExecuteScriptAsync(scriptSource, synapse, ct);
    }

    // Priority 3: default no-op
    _log.LogInformation("Neuron {Id} handled synapse verb={Verb} from={Source} (default)",
        neuronId, synapse.Verb, synapse.SourceId);
    return new SynapseResult(Success: true, Payload: string.Empty, Verb: synapse.Verb);
}

async Task<SynapseResult> ExecuteScriptAsync(string scriptSource, Synapse synapse, CancellationToken ct)
{
    var neuronId = this.GetPrimaryKeyString();
    try
    {
        var runner = GetOrCompileRunner(scriptSource);
        var globals = new NeuronScriptGlobals
        {
            Grains = GrainFactory,
            NeuronId = neuronId,
            Synapse = synapse,
            Log = _log
        };
        var result = await runner.Invoke(globals, ct);
        return result;
    }
    catch (CompilationErrorException ex)
    {
        _log.LogError(ex, "Neuron {Id} script compilation failed", neuronId);
        await EmitErrorEvent(neuronId, $"Compilation error: {ex.Message}", synapse.CorrelationId);
        return new SynapseResult(Success: false, Payload: ex.Message, Verb: "error");
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        _log.LogError(ex, "Neuron {Id} script execution failed", neuronId);
        await EmitErrorEvent(neuronId, ex.Message, synapse.CorrelationId);
        return new SynapseResult(Success: false, Payload: ex.Message, Verb: "error");
    }
}

ScriptRunner<NeuronScriptGlobals> GetOrCompileRunner(string scriptSource)
{
    var hash = scriptSource.GetHashCode().ToString();
    if (_scriptRunner is not null && _compiledScriptHash == hash)
        return _scriptRunner;

    var options = ScriptOptions.Default
        .AddReferences(AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location)))
        .AddImports(
            "System",
            "System.Threading.Tasks",
            "System.Collections.Generic",
            "InoNew.Core",
            "Timetravel.Core");

    var script = CSharpScript.Create<SynapseResult>(
        scriptSource, options, typeof(NeuronScriptGlobals));
    script.Compile();

    _scriptRunner = script.CreateDelegate();
    _compiledScriptHash = hash;
    return _scriptRunner;
}

async Task EmitErrorEvent(string neuronId, string message, string correlationId)
{
    var timeline = GrainFactory.GetGrain<ITimelineCaptureGrain>("global");
    await timeline.AppendAsync(new TimelineEvent(
        SequenceNumber: 0,
        Timestamp: DateTimeOffset.UtcNow,
        Kind: TimelineEventKind.Error,
        SourceId: neuronId,
        TargetId: null,
        CorrelationId: correlationId,
        SynapseVerb: "script_error",
        Payload: new Dictionary<string, string> { ["error"] = message },
        Decay: TimelineEvent.DecayHot));
}
```

4. Add the missing using for MetadataReference:
```csharp
using Microsoft.CodeAnalysis;
```

- [ ] **Step 7: Run tests to verify they pass**

Run: `dotnet test features/ino-new/InoNew.Tests/InoNew.Tests.csproj --filter "ScriptRuntime" --verbosity normal`
Expected: All 3 ScriptRuntime tests pass

Run: `dotnet test features/ino-new/InoNew.Tests/InoNew.Tests.csproj --verbosity normal`
Expected: All tests pass (including existing 64 + 3 new = 67)

- [ ] **Step 8: Commit**

```bash
git add features/ino-new/
git commit -m "feat: add CSharpScript runtime in NeuronGrain for L1 neurons"
```

---

### Task 4: EvolutionHandler (TDD)

The compile-time handler that creates new neurons when SearchEngine can't route a query.

**Files:**
- Create: `features/ino-new/InoNew.Core/Specialists/EvolutionHandler.cs`
- Create: `features/ino-new/InoNew.Tests/Features/Evolution.feature`
- Create: `features/ino-new/InoNew.Tests/Steps/EvolutionSteps.cs`
- Create: `features/ino-new/InoNew.Tests/Steps/EvolutionTests.cs`
- Modify: `features/ino-new/InoNew.Core/InoNewSiloExtensions.cs`
- Modify: `features/ino-new/InoNew.Tests/InoNew.Tests.csproj`

- [ ] **Step 1: Write the feature file**

Create `features/ino-new/InoNew.Tests/Features/Evolution.feature`:

```gherkin
Feature: Evolution — self-improving loop

  Background:
    Given a running test cluster with timeline capture
    And the neuron registry is available
    And an Evolution neuron is registered

  Scenario: No-match synapse triggers Evolution to create a new neuron
    When Evolution receives a no_match synapse with payload "what's the weather?"
    Then the neuron registry contains a new neuron for weather queries
    And the timeline contains a SelfImprovementL1 event
    And the new neuron has ScriptSource

  Scenario: Evolution compile failure retries once
    Given the LLM generates invalid C# on the first attempt
    And the LLM generates valid C# on the retry
    When Evolution receives a no_match synapse with payload "do something"
    Then a new neuron is created successfully

  Scenario: Evolution total compile failure returns error
    Given the LLM always generates invalid C#
    When Evolution receives a no_match synapse with payload "do something"
    Then the result is unsuccessful
    And the timeline contains an Error event from "evolution"
```

- [ ] **Step 2: Write EvolutionSteps**

Create `features/ino-new/InoNew.Tests/Steps/EvolutionSteps.cs`:

```csharp
using IAW.Testing;
using InoNew.Core;
using Timetravel.Core;
using Xunit;

namespace InoNew.Tests.Steps;

public sealed class EvolutionSteps
{
    readonly NeuronBddContext _ctx;

    public EvolutionSteps(NeuronBddContext ctx) => _ctx = ctx;

    INeuronRegistry Registry =>
        _ctx.Cluster.GrainFactory.GetGrain<INeuronRegistry>("global");

    ITimelineReader Timeline =>
        _ctx.Cluster.GrainFactory.GetGrain<ITimelineReader>("global");

    public async Task Given_EvolutionNeuronIsRegistered(CancellationToken ct)
    {
        await Registry.CreateAsync(
            new Blueprint(
                Name: "evolution",
                Purpose: "creates new neurons when no specialist matches",
                Capabilities: new[] { "evolution", "self-improvement" },
                Id: "evolution",
                SynapseSchema: "no_match: handles unmatched user queries by creating new neurons"),
            ct);
    }

    public async Task When_EvolutionReceivesNoMatch(string payload, CancellationToken ct)
    {
        var evolution = _ctx.Cluster.GrainFactory.GetGrain<INeuron>("evolution");
        var result = await evolution.HandleAsync(new Synapse(
            Id: $"synapse-{Guid.NewGuid():N}",
            SourceId: "search-engine",
            TargetId: "evolution",
            Verb: "no_match",
            Payload: payload,
            FiredAt: DateTimeOffset.UtcNow,
            CorrelationId: $"evo-{Guid.NewGuid():N}",
            Decay: TimelineEvent.DecayHot), ct);
        _ctx.Scenario["LastEvolutionResult"] = result;
    }

    public async Task Then_RegistryContainsNewNeuron(CancellationToken ct)
    {
        var neurons = await Registry.ListNeuronsAsync(ct);
        var evolved = neurons.FirstOrDefault(n =>
            n.Id != "evolution" && n.ScriptSource is not null);
        Assert.NotNull(evolved);
        _ctx.Scenario["EvolvedNeuron"] = evolved;
    }

    public Task Then_EvolvedNeuronHasScriptSource()
    {
        var neuron = (Neuron)_ctx.Scenario["EvolvedNeuron"]!;
        Assert.NotNull(neuron.ScriptSource);
        Assert.NotEmpty(neuron.ScriptSource);
        return Task.CompletedTask;
    }

    public async Task Then_TimelineContainsL1Event(CancellationToken ct)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var latest = await Timeline.GetLatestSequenceAsync(ct);
            if (latest >= 0)
            {
                var events = await Timeline.GetEventsInRangeAsync(0, latest, ct: ct);
                if (events.Any(e => e.Kind == TimelineEventKind.SelfImprovementL1))
                    return;
            }
            await Task.Delay(50, ct);
        }
        Assert.Fail("Timeline did not contain a SelfImprovementL1 event within 3s");
    }

    public async Task Then_TimelineContainsErrorFrom(string sourceId, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 60; attempt++)
        {
            var latest = await Timeline.GetLatestSequenceAsync(ct);
            if (latest >= 0)
            {
                var events = await Timeline.GetEventsInRangeAsync(0, latest, ct: ct);
                if (events.Any(e => e.Kind == TimelineEventKind.Error && e.SourceId == sourceId))
                    return;
            }
            await Task.Delay(50, ct);
        }
        Assert.Fail($"Timeline did not contain an Error event from '{sourceId}' within 3s");
    }

    public Task Then_ResultIsSuccessful()
    {
        var result = (SynapseResult)_ctx.Scenario["LastEvolutionResult"]!;
        Assert.True(result.Success);
        return Task.CompletedTask;
    }

    public Task Then_ResultIsUnsuccessful()
    {
        var result = (SynapseResult)_ctx.Scenario["LastEvolutionResult"]!;
        Assert.False(result.Success);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 3: Write EvolutionTests**

Create `features/ino-new/InoNew.Tests/Steps/EvolutionTests.cs`:

```csharp
using IAW.Testing;
using Timetravel.Core;
using Xunit;

namespace InoNew.Tests.Steps;

public class EvolutionTests : IAsyncLifetime
{
    NeuronBddContext _ctx = null!;
    EvolutionSteps _steps = null!;

    public async ValueTask InitializeAsync()
    {
        _ctx = await NeuronBddContext.StartAsync(silo =>
        {
            silo.AddTimelineCapture();
            silo.AddInoNew();
        });

        // Mock LLM: when asked to generate a neuron, return valid JSON with ScriptSource
        _ctx.MockLlm
            .When(p => p.Contains("Evolution engine") && p.Contains("weather"),
                """
                {
                    "id": "weather_lookup",
                    "name": "Weather Lookup",
                    "purpose": "Returns weather information for a location",
                    "capabilities": ["weather"],
                    "synapseSchema": "weather: looks up weather for a location",
                    "scriptSource": "return new SynapseResult(Success: true, Payload: $\"Weather for {Synapse.Payload}: sunny, 22C\", Verb: \"weather_result\");"
                }
                """)
            .When(p => p.Contains("Evolution engine"),
                """
                {
                    "id": "general_handler",
                    "name": "General Handler",
                    "purpose": "Handles general queries",
                    "capabilities": ["general"],
                    "synapseSchema": "handle: handles general queries",
                    "scriptSource": "return new SynapseResult(Success: true, Payload: \"Handled: \" + Synapse.Payload, Verb: \"handled\");"
                }
                """)
            .When(p => true, "{}");

        _steps = new EvolutionSteps(_ctx);
    }

    public async ValueTask DisposeAsync() => await _ctx.DisposeAsync();

    [Fact(DisplayName = "No-match synapse triggers Evolution to create a new neuron")]
    public async Task NoMatchTriggersEvolutionToCreateNewNeuron()
    {
        var ct = TestContext.Current.CancellationToken;

        await _steps.Given_EvolutionNeuronIsRegistered(ct);
        await _steps.When_EvolutionReceivesNoMatch("what's the weather?", ct);
        await _steps.Then_RegistryContainsNewNeuron(ct);
        await _steps.Then_EvolvedNeuronHasScriptSource();
        await _steps.Then_TimelineContainsL1Event(ct);
        await _steps.Then_ResultIsSuccessful();
    }
}

public class EvolutionCompileFailureTests : IAsyncLifetime
{
    NeuronBddContext _ctx = null!;
    EvolutionSteps _steps = null!;

    public async ValueTask InitializeAsync()
    {
        _ctx = await NeuronBddContext.StartAsync(silo =>
        {
            silo.AddTimelineCapture();
            silo.AddInoNew();
        });

        // EvolutionHandler's BuildPrompt includes "Fix the error" on retries.
        // Match order matters: specific predicates first.
        _ctx.MockLlm
            .When(p => p.Contains("Evolution engine") && p.Contains("Fix the error"),
                // Retry attempt (prompt includes error context) — valid C#
                """{"id":"retry_test","name":"Retry","purpose":"test","capabilities":["test"],"synapseSchema":"test","scriptSource":"return new SynapseResult(Success: true, Payload: \"ok\", Verb: \"handled\");"}""")
            .When(p => p.Contains("Evolution engine"),
                // First attempt — invalid C#
                """{"id":"retry_test","name":"Retry","purpose":"test","capabilities":["test"],"synapseSchema":"test","scriptSource":"THIS IS NOT VALID C#!!!"}""");

        _steps = new EvolutionSteps(_ctx);
    }

    public async ValueTask DisposeAsync() => await _ctx.DisposeAsync();

    [Fact(DisplayName = "Evolution compile failure retries once and succeeds")]
    public async Task CompileFailureRetriesOnce()
    {
        var ct = TestContext.Current.CancellationToken;

        await _steps.Given_EvolutionNeuronIsRegistered(ct);
        await _steps.When_EvolutionReceivesNoMatch("do something", ct);
        await _steps.Then_RegistryContainsNewNeuron(ct);
        await _steps.Then_ResultIsSuccessful();
    }
}

public class EvolutionTotalFailureTests : IAsyncLifetime
{
    NeuronBddContext _ctx = null!;
    EvolutionSteps _steps = null!;

    public async ValueTask InitializeAsync()
    {
        _ctx = await NeuronBddContext.StartAsync(silo =>
        {
            silo.AddTimelineCapture();
            silo.AddInoNew();
        });

        // Always return invalid C#
        _ctx.MockLlm.When(p => p.Contains("Evolution engine"),
            """{"id":"bad","name":"Bad","purpose":"test","capabilities":["test"],"synapseSchema":"test","scriptSource":"NOT VALID C#"}""");

        _steps = new EvolutionSteps(_ctx);
    }

    public async ValueTask DisposeAsync() => await _ctx.DisposeAsync();

    [Fact(DisplayName = "Evolution total compile failure returns error")]
    public async Task TotalCompileFailureReturnsError()
    {
        var ct = TestContext.Current.CancellationToken;

        await _steps.Given_EvolutionNeuronIsRegistered(ct);
        await _steps.When_EvolutionReceivesNoMatch("do something", ct);
        await _steps.Then_ResultIsUnsuccessful();
        await _steps.Then_TimelineContainsErrorFrom("evolution", ct);
    }
}
```

- [ ] **Step 4: Add Evolution.feature to InoNew.Tests.csproj**

```xml
<None Update="Features\Evolution.feature">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

- [ ] **Step 5: Run tests to verify they fail**

Run: `dotnet test features/ino-new/InoNew.Tests/InoNew.Tests.csproj --filter "Evolution" --verbosity normal`
Expected: Tests fail (EvolutionHandler not yet implemented)

- [ ] **Step 6: Implement EvolutionHandler**

Create `features/ino-new/InoNew.Core/Specialists/EvolutionHandler.cs`:

```csharp
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Timetravel.Core;

namespace InoNew.Core.Specialists;

public sealed class EvolutionHandler(IChatClient llm, ILogger<EvolutionHandler> log) : ISynapseHandler
{
    const int MaxRetries = 1;

    public async Task<SynapseResult> HandleAsync(Synapse synapse, IGrainFactory grains, CancellationToken ct)
    {
        var query = synapse.Payload;
        var registry = grains.GetGrain<INeuronRegistry>("global");
        var timeline = grains.GetGrain<ITimelineCaptureGrain>("global");

        var existingNeurons = await registry.ListNeuronsAsync(ct);
        var catalog = string.Join("\n", existingNeurons
            .Where(n => n.SynapseSchema is not null)
            .Select(n => $"  - {n.Id}: {n.SynapseSchema}"));

        string? lastError = null;

        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            var systemPrompt = BuildPrompt(catalog, query, lastError);
            var messages = new List<ChatMessage>
            {
                new(ChatRole.System, systemPrompt),
                new(ChatRole.User, query),
            };

            var response = await llm.GetResponseAsync(messages, cancellationToken: ct);
            var raw = StripMarkdownFences(response.Text ?? "");

            EvolutionOutput? output;
            try { output = JsonSerializer.Deserialize<EvolutionOutput>(raw, JsonOpts); }
            catch (JsonException ex)
            {
                log.LogWarning("Evolution: failed to parse LLM JSON (attempt {Attempt}): {Error}", attempt, ex.Message);
                lastError = $"JSON parse error: {ex.Message}";
                continue;
            }

            if (output?.Id is null || output.ScriptSource is null)
            {
                lastError = "LLM returned null id or scriptSource";
                continue;
            }

            // Roslyn compile check
            var compileError = TryCompile(output.ScriptSource);
            if (compileError is not null)
            {
                log.LogWarning("Evolution: script compile failed (attempt {Attempt}): {Error}", attempt, compileError);
                lastError = compileError;
                continue;
            }

            // Register the new neuron
            var blueprint = new Blueprint(
                Name: output.Name ?? output.Id,
                Purpose: output.Purpose ?? $"Auto-created for: {query}",
                Capabilities: output.Capabilities ?? new[] { output.Id },
                Id: output.Id,
                SynapseSchema: output.SynapseSchema,
                ScriptSource: output.ScriptSource,
                AuthorId: null,
                DomainId: "default");

            var neuron = await registry.CreateAsync(blueprint, ct);

            // Emit L1 timeline event
            await timeline.AppendAsync(new TimelineEvent(
                SequenceNumber: 0,
                Timestamp: DateTimeOffset.UtcNow,
                Kind: TimelineEventKind.SelfImprovementL1,
                SourceId: "evolution",
                TargetId: neuron.Id,
                CorrelationId: synapse.CorrelationId,
                SynapseVerb: "create_neuron",
                Payload: new Dictionary<string, string>
                {
                    ["neuron_id"] = neuron.Id,
                    ["query"] = query,
                    ["author"] = "evolution"
                },
                Decay: TimelineEvent.DecayHot));

            log.LogInformation("Evolution created neuron {Id} for query: {Query}", neuron.Id, query);

            // Fire the original query to the new neuron
            var newNeuron = grains.GetGrain<INeuron>(neuron.Id);
            var result = await newNeuron.HandleAsync(new Synapse(
                Id: $"synapse-{Guid.NewGuid():N}",
                SourceId: "evolution",
                TargetId: neuron.Id,
                Verb: "handle",
                Payload: query,
                FiredAt: DateTimeOffset.UtcNow,
                CorrelationId: synapse.CorrelationId,
                Decay: TimelineEvent.DecayHot), ct);

            return result;
        }

        // All retries exhausted
        log.LogError("Evolution: failed to create neuron after {Retries} retries. Last error: {Error}", MaxRetries + 1, lastError);
        await timeline.AppendAsync(new TimelineEvent(
            SequenceNumber: 0,
            Timestamp: DateTimeOffset.UtcNow,
            Kind: TimelineEventKind.Error,
            SourceId: "evolution",
            TargetId: null,
            CorrelationId: synapse.CorrelationId,
            SynapseVerb: "evolution_failed",
            Payload: new Dictionary<string, string>
            {
                ["query"] = query,
                ["error"] = lastError ?? "unknown"
            },
            Decay: TimelineEvent.DecayHot));

        return new SynapseResult(Success: false, Payload: lastError ?? "Evolution failed", Verb: "error");
    }

    static string? TryCompile(string scriptSource)
    {
        try
        {
            var options = ScriptOptions.Default
                .AddReferences(AppDomain.CurrentDomain.GetAssemblies()
                    .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
                    .Select(a => MetadataReference.CreateFromFile(a.Location)))
                .AddImports("System", "System.Threading.Tasks", "System.Collections.Generic",
                    "InoNew.Core", "Timetravel.Core");

            var script = CSharpScript.Create<SynapseResult>(scriptSource, options, typeof(NeuronScriptGlobals));
            var diagnostics = script.Compile();
            var errors = diagnostics.Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
            if (errors.Length > 0)
                return string.Join("; ", errors.Select(e => e.GetMessage()));
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    static string BuildPrompt(string catalog, string query, string? lastError)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("You are the Evolution engine for ino. A user query had no matching specialist neuron.");
        sb.AppendLine();
        sb.AppendLine("Your job: generate a new neuron that can handle this type of query.");
        sb.AppendLine();
        if (!string.IsNullOrEmpty(catalog))
        {
            sb.AppendLine("Existing neurons (do not duplicate):");
            sb.AppendLine(catalog);
            sb.AppendLine();
        }
        sb.AppendLine($"The user asked: \"{query}\"");
        if (lastError is not null)
        {
            sb.AppendLine();
            sb.AppendLine($"Your previous attempt failed with: {lastError}");
            sb.AppendLine("Fix the error and try again.");
        }
        sb.AppendLine();
        sb.AppendLine("Generate a JSON object with:");
        sb.AppendLine("- id: snake_case identifier (e.g. \"weather_lookup\")");
        sb.AppendLine("- name: human-readable name");
        sb.AppendLine("- purpose: one sentence");
        sb.AppendLine("- capabilities: array of tags");
        sb.AppendLine("- synapseSchema: verb description for routing");
        sb.AppendLine("- scriptSource: C# top-level statements returning SynapseResult");
        sb.AppendLine();
        sb.AppendLine("The scriptSource receives globals: Grains (IGrainFactory), NeuronId (string), Synapse (Synapse with .Verb and .Payload), Log (ILogger).");
        sb.AppendLine("MUST return: new SynapseResult(Success: true, Payload: \"result\", Verb: \"verb\");");
        sb.AppendLine("No IO, no network, no reflection. Use grain calls for complex work.");
        sb.AppendLine();
        sb.AppendLine("Respond with ONLY the JSON object, no markdown.");
        return sb.ToString();
    }

    static string StripMarkdownFences(string text)
    {
        var trimmed = text.Trim();
        if (trimmed.StartsWith("```"))
        {
            var nl = trimmed.IndexOf('\n');
            if (nl >= 0) trimmed = trimmed[(nl + 1)..];
        }
        if (trimmed.EndsWith("```")) trimmed = trimmed[..^3];
        return trimmed.Trim();
    }

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    sealed record EvolutionOutput(
        string? Id,
        string? Name,
        string? Purpose,
        string[]? Capabilities,
        string? SynapseSchema,
        string? ScriptSource);
}
```

- [ ] **Step 7: Register EvolutionHandler in DI**

In `features/ino-new/InoNew.Core/InoNewSiloExtensions.cs`, add:

```csharp
silo.Services.AddKeyedSingleton<ISynapseHandler, EvolutionHandler>("evolution");
```

- [ ] **Step 8: Run tests to verify they pass**

Run: `dotnet test features/ino-new/InoNew.Tests/InoNew.Tests.csproj --filter "Evolution" --verbosity normal`
Expected: All 3 Evolution tests pass

Run: `dotnet test features/ino-new/InoNew.Tests/InoNew.Tests.csproj --verbosity normal`
Expected: All tests pass

- [ ] **Step 9: Commit**

```bash
git add features/ino-new/
git commit -m "feat: add EvolutionHandler — LLM-powered neuron creation for self-improving loop"
```

---

### Task 5: SearchEngine gap signal — wire no_match to Evolution

SearchEngine fires a `no_match` synapse to the Evolution neuron when no specialist matches, then awaits the result.

**Files:**
- Modify: `features/ino-new/InoNew.Core/SearchEngineGrain.cs`
- Create: `features/ino-new/InoNew.Tests/Steps/EvolutionE2ETests.cs`
- Create: `features/ino-new/InoNew.Tests/Features/EvolutionE2E.feature`
- Modify: `features/ino-new/InoNew.Tests/InoNew.Tests.csproj`

- [ ] **Step 1: Write the E2E feature file**

Create `features/ino-new/InoNew.Tests/Features/EvolutionE2E.feature`:

```gherkin
Feature: End-to-end self-improving loop

  Background:
    Given a running test cluster with timeline capture
    And a SearchEngine neuron is registered
    And an Evolution neuron is registered

  Scenario: Unhandled query triggers Evolution via SearchEngine
    When the user sends "what's the weather in Tokyo?" via SearchEngine
    Then the response contains weather information
    And the neuron registry contains a new "weather" neuron
    And the timeline contains a SelfImprovementL1 event

  Scenario: Second identical query routes to evolved neuron without Evolution
    Given a "weather_lookup" neuron was previously evolved
    When the user sends "what's the weather in Paris?" via SearchEngine
    Then SearchEngine routes to "weather_lookup"
    And Evolution is not invoked
```

- [ ] **Step 2: Write EvolutionE2ETests**

Create `features/ino-new/InoNew.Tests/Steps/EvolutionE2ETests.cs`:

```csharp
using IAW.Testing;
using InoNew.Core;
using Timetravel.Core;
using Xunit;

namespace InoNew.Tests.Steps;

public class EvolutionE2ETests : IAsyncLifetime
{
    NeuronBddContext _ctx = null!;
    SearchEngineSteps _searchSteps = null!;
    EvolutionSteps _evoSteps = null!;

    public async ValueTask InitializeAsync()
    {
        _ctx = await NeuronBddContext.StartAsync(silo =>
        {
            silo.AddTimelineCapture();
            silo.AddInoNew();
        });

        // SearchEngine routing: weather → no known specialist → null
        // Evolution: generate weather neuron with valid ScriptSource
        _ctx.MockLlm
            .When(p => p.Contains("Evolution engine") && p.Contains("weather"),
                """
                {
                    "id": "weather_lookup",
                    "name": "Weather Lookup",
                    "purpose": "Returns weather information",
                    "capabilities": ["weather"],
                    "synapseSchema": "weather: looks up weather for a location",
                    "scriptSource": "return new SynapseResult(Success: true, Payload: $\"Weather for {Synapse.Payload}: sunny, 22C\", Verb: \"weather_result\");"
                }
                """)
            .When(p => p.Contains("SearchEngine") && p.Contains("weather_lookup"),
                """{"specialistId": "weather_lookup", "verb": "handle", "payload": "what's the weather in Paris?"}""")
            .When(p => p.Contains("SearchEngine"),
                """{"specialistId": null, "verb": null, "payload": null}""")
            .When(p => true, "{}");

        _searchSteps = new SearchEngineSteps(_ctx);
        _evoSteps = new EvolutionSteps(_ctx);
    }

    public async ValueTask DisposeAsync() => await _ctx.DisposeAsync();

    [Fact(DisplayName = "Unhandled query triggers Evolution via SearchEngine")]
    public async Task UnhandledQueryTriggersEvolution()
    {
        var ct = TestContext.Current.CancellationToken;

        await _searchSteps.Given_ARunningTestClusterWithTimelineCapture();
        await _searchSteps.Given_TheNeuronRegistryIsAvailable();
        await _searchSteps.Given_BehaviorMemoryIsAvailable();
        await _searchSteps.Given_ASearchEngineNeuronIsRegistered(ct);
        await _evoSteps.Given_EvolutionNeuronIsRegistered(ct);

        // First query: no specialist → Evolution creates weather neuron
        var searchEngine = _ctx.Cluster.GrainFactory.GetGrain<ISearchEngine>("search-engine");
        var reply = await searchEngine.HandleUserMessageAsync("what's the weather in Tokyo?", ct);

        // SearchEngine should have routed through Evolution
        Assert.NotNull(reply.SpecialistId);

        // Verify new neuron exists
        var registry = _ctx.Cluster.GrainFactory.GetGrain<INeuronRegistry>("global");
        var neurons = await registry.ListNeuronsAsync(ct);
        Assert.Contains(neurons, n => n.Id == "weather_lookup" && n.ScriptSource is not null);

        await _evoSteps.Then_TimelineContainsL1Event(ct);
    }

    [Fact(DisplayName = "Second query routes to evolved neuron")]
    public async Task SecondQueryRoutesToEvolvedNeuron()
    {
        var ct = TestContext.Current.CancellationToken;

        await _searchSteps.Given_ARunningTestClusterWithTimelineCapture();
        await _searchSteps.Given_TheNeuronRegistryIsAvailable();
        await _searchSteps.Given_BehaviorMemoryIsAvailable();
        await _searchSteps.Given_ASearchEngineNeuronIsRegistered(ct);
        await _evoSteps.Given_EvolutionNeuronIsRegistered(ct);

        // Pre-create the weather neuron (simulating previous Evolution)
        var registry = _ctx.Cluster.GrainFactory.GetGrain<INeuronRegistry>("global");
        await registry.CreateAsync(new Blueprint(
            Name: "Weather Lookup",
            Purpose: "Returns weather information",
            Capabilities: new[] { "weather" },
            Id: "weather_lookup",
            SynapseSchema: "weather: looks up weather for a location",
            ScriptSource: """return new SynapseResult(Success: true, Payload: $"Weather for {Synapse.Payload}: sunny", Verb: "weather_result");"""), ct);

        // Now the MockLlm routing includes weather_lookup as a known specialist
        // The second .When above matches: SearchEngine + weather_lookup → routes to it
        var searchEngine = _ctx.Cluster.GrainFactory.GetGrain<ISearchEngine>("search-engine");
        var reply = await searchEngine.HandleUserMessageAsync("what's the weather in Paris?", ct);

        Assert.Equal("weather_lookup", reply.SpecialistId);
    }
}
```

- [ ] **Step 3: Modify SearchEngineGrain — synchronous Evolution on no_match**

In `features/ino-new/InoNew.Core/SearchEngineGrain.cs`, replace the no-match block (lines 46-53) with:

```csharp
if (decision.SpecialistId is null)
{
    _log.LogInformation("SearchEngine found no specialist for: {Text}", text);

    // Signal the evolution neuron to create a new specialist
    var evolutionNeuron = GrainFactory.GetGrain<INeuron>("evolution");
    var noMatchSynapse = new Synapse(
        Id: $"synapse-{Guid.NewGuid():N}",
        SourceId: "search-engine",
        TargetId: "evolution",
        Verb: "no_match",
        Payload: text,
        FiredAt: DateTimeOffset.UtcNow,
        CorrelationId: correlationId,
        Decay: TimelineEvent.DecayHot);

    try
    {
        var evoResult = await evolutionNeuron.HandleAsync(noMatchSynapse, ct);
        if (evoResult.Success)
        {
            return new SearchEngineReply(
                Text: evoResult.Payload,
                SpecialistId: "evolution",
                Verb: evoResult.Verb,
                TimelineSequence: -1);
        }
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        _log.LogWarning(ex, "SearchEngine: Evolution handler failed for: {Text}", text);
    }

    return new SearchEngineReply(
        Text: "I don't have a specialist for that yet.",
        SpecialistId: null,
        Verb: null,
        TimelineSequence: -1);
}
```

Note: `correlationId` must be declared before the no-match check. Move the `var correlationId` line earlier — before the `if (decision.SpecialistId is null)` block. Currently it's declared at line 57 (after the no-match return). Change to:

```csharp
var decision = ParseRoutingDecision(rawJson);
var correlationId = $"search-engine-{Guid.NewGuid():N}";

if (decision.SpecialistId is null)
{
    // ... evolution path above ...
}
```

- [ ] **Step 4: Add EvolutionE2E.feature to InoNew.Tests.csproj**

```xml
<None Update="Features\EvolutionE2E.feature">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</None>
```

- [ ] **Step 5: Run all tests**

Run: `dotnet test features/ino-new/InoNew.Tests/InoNew.Tests.csproj --verbosity normal`
Expected: All tests pass (original + ScriptRuntime + Evolution + E2E)

- [ ] **Step 6: Commit**

```bash
git add features/ino-new/
git commit -m "feat: wire SearchEngine → Evolution gap signal, close the self-improving loop"
```

---

### Task 6: Verify the full loop end-to-end + final cleanup

Run the full test suite, verify no regressions, clean up.

**Files:**
- No new files — verification only

- [ ] **Step 1: Run the full InoNew test suite**

Run: `dotnet test features/ino-new/InoNew.Tests/InoNew.Tests.csproj --verbosity normal`
Expected: All tests pass

- [ ] **Step 2: Build the full solution**

Run: `dotnet build ino.slnx`
Expected: Build succeeded (the pre-existing E2E.Tests error about ScreenshotsDir is acceptable)

- [ ] **Step 3: Verify timeline formatter handles L1 events**

The `TimelineEventFormatter` already maps `SelfImprovementL1` → `1`. Verify by running the InoCommandDispatcher `timeline` command in an Evolution test — the L1 event should appear as `1` in the output. This is already covered by the E2E test asserting `TimelineContainsL1Event`.

- [ ] **Step 4: Final commit with any cleanup**

If any test adjustments were needed:
```bash
git add features/ino-new/
git commit -m "test: verify full self-improving loop end-to-end"
```
