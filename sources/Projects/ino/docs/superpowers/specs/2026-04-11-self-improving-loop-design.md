# Self-Improving Loop — Closing the Cycle

**Date:** 2026-04-11
**Status:** Approved
**Scope:** Phase 1 (closes the loop), with Phase 2/3 domain sharing and marketplace deferred

## Vision

ino is not AGI. It's general intelligence that grows. Each domain is narrow expertise, but the ecosystem of domains is general. ino grows by accumulating human expertise as domains, and the self-improving loop makes each domain better over time.

The self-improving loop is the mechanism by which ino observes gaps in its own capabilities and fills them — autonomously creating new neurons at runtime. This spec covers closing the loop end-to-end for the first time.

## The Closed Loop

```
User message
    |
    v
SearchEngine --routes--> specialist (if found) --> response
    |
    | no specialist found
    v
SearchEngine fires synapse { verb: "no_match", payload: original query }
    |
    v
EvolutionHandler (compile-time ISynapseHandler)
    |
    |-- 1. LLM call: generate a Blueprint with ScriptSource for this gap
    |-- 2. CSharpScript.Create() — compile check, fail fast if bad
    |-- 3. NeuronRegistry.CreateAsync(blueprint) — neuron is live
    |-- 4. Emit SelfImprovementL1 timeline event
    |-- 5. Fire the original query to the new neuron
    |
    v
Response returned to user via the newly created neuron
    |
    v
Next identical query --> SearchEngine routes to the new neuron directly
```

Five participants, all existing grains plus one new handler:
- **SearchEngine** (existing) — routes queries, signals gaps
- **EvolutionHandler** (new, compile-time) — fills gaps by creating neurons
- **NeuronGrain** (existing, updated) — runs ScriptSource via CSharpScript
- **NeuronRegistry** (existing) — registers new neurons
- **Timeline** (existing) — captures L1 events

## Architecture

### 1. SearchEngine Gap Signal

When SearchEngine finds no specialist match, instead of just returning a fallback message, it also fires a `no_match` synapse to the `evolution` neuron:

```csharp
// in SearchEngineGrain.HandleUserMessageAsync, after "no specialist found":
var evolutionNeuron = GrainFactory.GetGrain<INeuron>("evolution");
await evolutionNeuron.FireAsync(new Synapse(
    Id: $"synapse-{Guid.NewGuid():N}",
    SourceId: "search-engine",
    TargetId: "evolution",
    Verb: "no_match",
    Payload: text,
    FiredAt: DateTimeOffset.UtcNow,
    CorrelationId: correlationId,
    Decay: TimelineEvent.DecayHot), ct);
```

**Synchronous evolution (Phase 1 default).** SearchEngine awaits the Evolution handler's result and returns it to the user in the same request. This adds latency (LLM call + Roslyn compile + neuron execution ~2-5s) but the user gets a real answer on the first try, and the neuron is ready for instant routing on subsequent queries. The alternative — async evolution where SearchEngine returns a fallback and the neuron is only ready next time — is simpler but delivers less value.

### 2. EvolutionHandler — Compile-Time ISynapseHandler

A standard `ISynapseHandler` registered in DI, keyed as `"evolution"`. Ships in the silo assembly. This is the seed — it can be promoted to a self-bootstrapping runtime neuron later (Option 2 migration path).

**HandleAsync flow:**

1. Extract the unhandled query from `synapse.Payload`
2. Gather context:
   - Current neuron catalog from `INeuronRegistry.ListNeuronsAsync()` (to avoid duplicates)
   - Recent `no_match` events from timeline (to detect patterns)
3. LLM call with system prompt:
   - "You are the Evolution engine for ino. A user query had no matching specialist. Generate a new neuron to handle it."
   - Input: the unhandled query + existing neuron catalog
   - Output: JSON with `{ id, name, purpose, capabilities, synapseSchema, scriptSource }`
4. Parse LLM output into a `Blueprint`
5. Roslyn compile check: `CSharpScript.Create<SynapseResult>(scriptSource, scriptOptions, typeof(NeuronScriptGlobals))`
   - If compile fails: feed errors back to LLM, retry once
   - If still fails: emit Error timeline event, return `SynapseResult(Success: false)`
6. `NeuronRegistry.CreateAsync(blueprint)` — neuron is live
7. Emit `SelfImprovementL1` timeline event with payload: `{ neuronId, query, scriptSourceHash }`
8. Fire the original query to the new neuron: `newNeuronGrain.HandleAsync(querySynapse)`
9. Return the new neuron's `SynapseResult` as the Evolution handler's result

**System prompt for LLM (neuron generation):**

```
You are the Evolution engine for ino. A user query had no matching specialist neuron.

Your job: generate a new neuron that can handle this type of query.

Existing neurons (do not duplicate):
{catalog}

The user asked: "{query}"

Generate a JSON object with:
- id: snake_case identifier for the neuron (e.g. "weather_lookup")
- name: human-readable name
- purpose: one sentence describing what this neuron does
- capabilities: array of capability tags
- synapseSchema: verb description for SearchEngine routing
- scriptSource: C# top-level statements that handle the synapse

The scriptSource receives these globals:
- Grains (IGrainFactory) — access to all grains in the cluster
- NeuronId (string) — this neuron's id
- Synapse (Synapse) — the incoming synapse with .Verb and .Payload
- Log (ILogger) — structured logging

The script MUST return a SynapseResult:
  return new SynapseResult(Success: true, Payload: "result text", Verb: "handled");

Keep the script simple. No IO, no network calls, no reflection. Use grain calls for anything complex.

Respond with ONLY the JSON object, no markdown.
```

### 3. NeuronGrain Script Runtime — In-Process CSharpScript

**NeuronScriptGlobals** (new class, injected into every script):

```csharp
public class NeuronScriptGlobals
{
    public IGrainFactory Grains { get; init; }
    public string NeuronId { get; init; }
    public Synapse Synapse { get; init; }
    public ILogger Log { get; init; }
}
```

**NeuronGrain.HandleAsync updated flow:**

```
1. Try ISynapseHandler from DI by keyed service [NeuronId]
   -> if found, delegate to handler (existing path, unchanged)

2. If not found, check state.Definition.ScriptSource
   -> if ScriptSource is not null:
      a. Check compile cache: _cachedRunner (ScriptRunner<NeuronScriptGlobals>)
      b. Cache miss: compile via CSharpScript.Create<SynapseResult>(
             source, scriptOptions, typeof(NeuronScriptGlobals))
         - scriptOptions: references from AppDomain.CurrentDomain.GetAssemblies()
         - scriptOptions: imports for InoNew.Core, Timetravel.Core, Orleans
         Cache the ScriptRunner on the grain instance
      c. Build globals: new NeuronScriptGlobals { Grains, NeuronId, Synapse, Log }
      d. Execute: var result = await runner.RunAsync(globals)
      e. Return result.ReturnValue (SynapseResult)
      f. On exception: emit Error timeline event, return SynapseResult(Success: false, Payload: ex.Message)

3. If no ScriptSource either: return default SynapseResult(Success: true) (bare neuron)
```

**Cache invalidation:** The compiled ScriptRunner is cached for the grain activation lifetime. If ScriptSource changes (neuron self-modifies via L2), the grain must be deactivated — next activation picks up new source. No explicit cache coherency needed.

**Assembly references for script compilation:**

```csharp
var references = AppDomain.CurrentDomain.GetAssemblies()
    .Where(a => !a.IsDynamic && !string.IsNullOrEmpty(a.Location))
    .Select(a => MetadataReference.CreateFromFile(a.Location));

var scriptOptions = ScriptOptions.Default
    .AddReferences(references)
    .AddImports(
        "System",
        "System.Threading.Tasks",
        "System.Collections.Generic",
        "InoNew.Core",
        "Timetravel.Core");
```

### 4. Data Model Changes

**Blueprint record** — add fields:

| Field | Type | Description |
|---|---|---|
| ScriptSource | string? | C# top-level statements for L1 runtime neurons. Null for compile-time neurons. |
| AuthorId | string? | Null for Evolution-generated, user ID for human-published |
| DomainId | string | Domain this neuron belongs to. Defaults to "default" |

**Neuron record** — add same fields (persisted from Blueprint at creation):

| Field | Type | Description |
|---|---|---|
| ScriptSource | string? | Persisted from blueprint |
| AuthorId | string? | Persisted from blueprint |
| DomainId | string | Persisted from blueprint |

**Synapse record** — field renames (strip redundant suffixes):

| Current | New |
|---|---|
| SourceNeuronId | SourceId |
| TargetNeuronId | TargetId |

**NeuronBlueprint** renamed to **Blueprint** (the context is always neurons).
**NeuronGrainState** renamed to **NeuronState**.
**NeuronRegistryState** renamed to **RegistryState**.

`INeuron`, `Neuron`, `Synapse`, `INeuronRegistry`, `ISynapseHandler` stay as-is — they are the domain concepts, not suffixed repetitions.

### 5. Timeline Events

**SelfImprovementL1** event emitted by EvolutionHandler after successful neuron creation:

```csharp
new TimelineEvent(
    SequenceNumber: 0, // assigned by timeline grain
    Timestamp: DateTimeOffset.UtcNow,
    Kind: TimelineEventKind.SelfImprovementL1,
    SourceId: "evolution",
    TargetId: newNeuronId,
    CorrelationId: synapse.CorrelationId,
    SynapseVerb: "create_neuron",
    Payload: new Dictionary<string, string>
    {
        ["neuron_id"] = newNeuronId,
        ["query"] = originalQuery,
        ["script_hash"] = ComputeHash(scriptSource),
        ["author"] = "evolution"
    },
    Decay: TimelineEvent.DecayHot)
```

The `SelfImprovementL1` kind already exists in `TimelineEventKind` and is rendered as `1` in the timeline formatter. Currently not wired — this spec wires it.

### 6. NuGet Dependencies

**New package needed:** `Microsoft.CodeAnalysis.CSharp.Scripting` — the CSharpScript API for in-process script compilation and execution.

Add to `InoNew.Core.csproj` (or a new `InoNew.Scripting` project if isolation is preferred):

```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.Scripting" />
```

This is distinct from `Microsoft.CodeAnalysis.CSharp` (already used by `iaw/Agents.CSharp/Roslyn/`) — it adds the scripting host (`CSharpScript.Create`, `ScriptRunner`, globals injection).

### 7. BDD Test Scenarios

**Feature: Evolution — self-improving loop**

```gherkin
Feature: SearchEngine evolution

  Background:
    Given a running test cluster with timeline capture
    And the neuron registry is available
    And behavior memory is available
    And a SearchEngine neuron is registered
    And an Evolution neuron is registered

  Scenario: Unhandled query triggers Evolution to create a new neuron
    Given no specialist exists for weather queries
    When the user sends "what's the weather in Tokyo?"
    Then Evolution creates a new neuron with id containing "weather"
    And the timeline contains a SelfImprovementL1 event
    And the new neuron returns a successful result

  Scenario: Second identical query routes directly to the evolved neuron
    Given Evolution previously created a "weather" neuron
    When the user sends "what's the weather in Paris?"
    Then SearchEngine routes to the "weather" neuron
    And Evolution is NOT invoked

  Scenario: Evolution compile failure emits error and retries
    Given the LLM generates invalid C# on the first attempt
    And the LLM generates valid C# on the retry
    When the user sends an unhandled query
    Then Evolution retries compilation once
    And a new neuron is created successfully

  Scenario: Evolution total compile failure returns graceful fallback
    Given the LLM generates invalid C# on both attempts
    When the user sends an unhandled query
    Then the timeline contains an Error event from "evolution"
    And the user receives "I don't have a specialist for that yet."
```

**Feature: NeuronGrain script execution**

```gherkin
Feature: NeuronGrain script runtime

  Scenario: Neuron with ScriptSource executes in-process
    Given a neuron "greeter" with ScriptSource that returns "Hello!"
    When a synapse is fired to "greeter" with verb "handle"
    Then the neuron returns SynapseResult with Payload "Hello!"

  Scenario: Script execution failure emits Error timeline event
    Given a neuron "broken" with ScriptSource that throws an exception
    When a synapse is fired to "broken" with verb "handle"
    Then the neuron returns SynapseResult with Success false
    And the timeline contains an Error event from "broken"

  Scenario: Compile-time handler takes priority over ScriptSource
    Given a specialist "shell" is registered in DI
    And the "shell" neuron also has ScriptSource
    When a synapse is fired to "shell"
    Then the DI handler is used, not the ScriptSource
```

## Domain Sharing Model (Phase 2 — deferred, design only)

The domain is the atomic unit of sharing. A domain is someone's expertise packaged as a cohesive unit: neurons + synapses + behavior memory + ScriptSource.

**Three states for a domain:**

| State | Who sees it | How |
|---|---|---|
| Private | Only you | Your personal ino instance. Default. |
| Shared | Subscribers | Published to domain registry. Others install it, isolated by Orleans cluster name. |
| Evolving | The system | Evolution is building neurons into it. Ralph loop manually, self-improving loop autonomously. |

**Security via Orleans cluster name isolation.** Each shared domain runs in its own silo partition. A bad neuron in someone's travel domain can't touch another user's private domain. No sandboxing layer — Orleans infrastructure handles it.

**Revenue model:**
- Users pay for domains (other people's expertise), not for ino the platform
- Domain subscriptions: contributors set pricing
- Attribution: every synapse fired to a domain neuron is a trackable event on the timeline
- Payout: proportional to successful fires per billing period
- ino takes a platform cut

**Discovery:** When SearchEngine hits no_match and Evolution has no solution, query the global Domain Registry for shared domains that handle this query type. Auto-install matching domain. The marketplace IS the self-improving loop extended across users.

**Migration path from Phase 1:**
- Phase 1 neurons all live in domain "default" (private)
- Phase 2 adds DomainRegistry grain, domain install/subscribe, cluster isolation
- Phase 3 adds revenue engine, domain versioning, trust tiers

## What We Build Now (Phase 1)

1. **EvolutionHandler** — compile-time `ISynapseHandler` that creates neurons via LLM + Roslyn
2. **NeuronGrain script runtime** — `CSharpScript.RunAsync` fallback in HandleAsync
3. **NeuronScriptGlobals** — globals class injected into scripts
4. **Data model updates** — ScriptSource, AuthorId, DomainId on Blueprint/Neuron; field renames on Synapse
5. **SearchEngine gap signal** — fire `no_match` synapse to evolution neuron
6. **SelfImprovementL1 timeline events** — wired in EvolutionHandler
7. **BDD tests** — Evolution scenarios + script runtime scenarios

## What We Don't Build Now

- Domain Registry grain (Phase 2)
- Domain install/subscribe (Phase 2)
- Cluster isolation per domain (Phase 2)
- Revenue/attribution engine (Phase 3)
- Domain discovery marketplace (Phase 3)
- Self-bootstrapping Evolution neuron (Option 2 migration — when the loop proves itself)
- L2 in-process scripts (keep current CodeOrchestrator standalone process for now)
- NeuronValidator allowlist (needed for shared domains, not for self-generated)
