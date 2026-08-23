# Smart Prompt Execution Harness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a simple, high-quality Execution harness (Neuron + ExecutionContext Entity + Sdk) so chat and Smart Prompts share one spine, with Aspire fakes, full simulation/E2E coverage, live Aspire + digitalbrain MCP verification — ready for real Gmail/Salesforce MCP later.

**Architecture:** Approach A from the ratified spec. Reuse existing `Neuron` / `Entity<TState>` / `BrainSimulation` / `IModule` / MEAI `AIFunction` / OTel ServiceDefaults. Do not invent parallel buses, hop DTOs, or a second tool stack. Agent driver binds MEAI tools to `Execution.Sdk.CallAsync`; façade modules return `ContextDelta`; Fake transports swap behind Aspire fakes.

**Tech Stack:** .NET / Orleans 10.2 (`DurableGrain`, `IPersistentState`, BroadcastChannel, journaling), Aspire 13.5, Microsoft.Extensions.AI 10.9, Microsoft.Agents.AI.Workflows 1.18 (teams only), xunit v3 + `BrainSimulation` + `JournalWait` + Aspire.Hosting.Testing / E2E / Reqnroll BDD, OpenTelemetry via ServiceDefaults, Ollama Gemma for live reasoning.

**Spec:** [docs/superpowers/specs/2026-08-23-smart-prompt-execution-architecture-design.md](../specs/2026-08-23-smart-prompt-execution-architecture-design.md)

**Quality bar:** No ceremony. Small files, self-explanatory names, no empty `/// <summary>`. Every public Sdk/façade/Neuron port has simulation tests. Live path: `aspire start` + digitalbrain MCP + fake integrations + Gemma. Stop only after code review and “ready for real MCP transports.”

---

## File map (create / touch)

```text
src/Kernel/DigitalBrain.Abstractions/
  Execution/ExecutionId.cs, ContextPath.cs, ContextDigest.cs   # concepts only
  DigitalBrainNames.cs                                         # + FakesEnabled key

src/Kernel/DigitalBrain.Core/
  FactLedger/FactLedger.cs                                     # thin append/read helper over IDurableList

src/Modules/Execution/
  Contracts/   DigitalBrain.Modules.Execution.Contracts.csproj
    IExecution.cs, IExecutionContext.cs, StartExecution.cs, ContextDelta.cs,
    CapabilityId.cs, WorkloadDescriptor.cs, ExecutionStatus.cs, …
  Execution/   DigitalBrain.Modules.Execution.csproj
    ExecutionModule.cs, ExecutionNeuron.cs, ExecutionContextEntity.cs,
    AgentExecutionDriver.cs, ExecutionSession.cs (Sdk), EffectBroker.cs
  Aspire.Hosting/ DigitalBrain.Modules.Execution.Aspire.Hosting.csproj
    ExecutionHostingExtensions.cs  # WithDigitalBrainFakes bridge if needed

src/Modules/Integrations/Gmail/   (façade + Fake transport; MCP later)
src/Modules/Integrations/Salesforce/
src/Modules/Integrations/Search/

src/Modules/SmartPrompt/
  Contracts/ + SmartPrompt/ + Aspire.Hosting/

src/Modules/UI/.../Chat/   # ActiveExecutionId, lineage seed, ChatTurnWorker → Sdk
src/Aspire/DigitalBrain.AppHost/AppHost.cs
tests/DigitalBrain.Simulation.Tests/Execution/*.cs
tests/DigitalBrain.E2E.Tests/ExecutionHarnessTests.cs
docs/CONTEXT.md, docs/ARCHITECTURE.md  # vocabulary align at end
```

---

### Task 1: Abstractions execution identity types

**Files:**
- Create: `src/Kernel/DigitalBrain.Abstractions/Execution/ExecutionId.cs`
- Create: `src/Kernel/DigitalBrain.Abstractions/Execution/ContextPath.cs`
- Create: `src/Kernel/DigitalBrain.Abstractions/Execution/ContextDigest.cs`
- Modify: `src/Kernel/DigitalBrain.Abstractions/DigitalBrainNames.cs`
- Test: `tests/DigitalBrain.Simulation.Tests/Execution/ExecutionIdTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using DigitalBrain.Abstractions.Execution;
using Xunit;

namespace DigitalBrain.Simulation.Tests.Execution;

public sealed class ExecutionIdTests
{
    [Fact]
    public void New_produces_non_empty_id_and_round_trips_string_key()
    {
        var id = ExecutionId.New();
        Assert.NotEqual(Guid.Empty, id.Value);
        Assert.Equal(id, ExecutionId.Parse(id.ToString()));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj --filter ExecutionIdTests --severity high`
Expected: FAIL (type missing)

- [ ] **Step 3: Implement minimal types (mirror `CommandId` style)**

```csharp
namespace DigitalBrain.Abstractions.Execution;

[GenerateSerializer]
[Alias("db.execution-id")]
public readonly record struct ExecutionId
{
    public ExecutionId(Guid value)
    {
        if (value == Guid.Empty) throw new ArgumentException("ExecutionId cannot be empty.");
        Value = value;
    }

    [Id(0)] public Guid Value { get; }

    public static ExecutionId New() => new(Guid.NewGuid());
    public static ExecutionId Parse(string value) => new(Guid.Parse(value));
    public override string ToString() => Value.ToString("N");
}
```

```csharp
namespace DigitalBrain.Abstractions.Execution;

[GenerateSerializer]
[Alias("db.context-path")]
public readonly record struct ContextPath([property: Id(0)] string Value)
{
    public ContextPath
    {
        if (string.IsNullOrWhiteSpace(Value)) throw new ArgumentException("ContextPath required.");
        Value = Value.Trim().Trim('/');
    }

    public override string ToString() => Value;
}
```

```csharp
namespace DigitalBrain.Abstractions.Execution;

[GenerateSerializer]
[Alias("db.context-digest")]
public readonly record struct ContextDigest([property: Id(0)] string Sha256Hex)
{
    public ContextDigest
    {
        if (string.IsNullOrWhiteSpace(Sha256Hex)) throw new ArgumentException("Digest required.");
    }
}
```

Add to `DigitalBrainNames`:

```csharp
public const string Fakes = "DigitalBrain:Fakes:Enabled";
```

- [ ] **Step 4: Run tests — expect PASS**

- [ ] **Step 5: Commit**

```bash
git add src/Kernel/DigitalBrain.Abstractions tests/DigitalBrain.Simulation.Tests/Execution
git commit -m "feat(abstractions): add ExecutionId, ContextPath, ContextDigest"
```

---

### Task 2: Execution contracts project (IExecution + IExecutionContext + ContextDelta)

**Files:**
- Create: `src/Modules/Execution/Contracts/DigitalBrain.Modules.Execution.Contracts.csproj` (copy Time Contracts pattern; RootNamespace `DigitalBrain.Execution`)
- Create: contracts listed below
- Modify: `DigitalBrain.slnx` — add under `/Modules/Execution/`
- Test: `tests/DigitalBrain.Simulation.Tests/Execution/ContextDeltaTests.cs`

- [ ] **Step 1: Failing test for ContextDelta digest**

```csharp
[Fact]
public void ContextDelta_requires_path_and_schema_hash()
{
    var delta = new ContextDelta(
        new ContextPath("gmail.search"),
        SchemaHash: "abc",
        PayloadJson: """{"messages":[]}""",
        BlobRef: null);
    Assert.Equal("gmail.search", delta.Path.Value);
}
```

- [ ] **Step 2: Run — FAIL missing types**

- [ ] **Step 3: Implement contracts (keep small)**

`CapabilityId` — `readonly record struct` like ExecutionId.

```csharp
namespace DigitalBrain.Execution;

public enum ExecutionStatus : byte
{
    Pending = 0,
    Running = 1,
    AwaitingApproval = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5,
    Uncertain = 6,
}

public enum ExecutionDriverKind : byte
{
    Agent = 0,
    Script = 1,
}

[GenerateSerializer, Alias("db.workload.chat-turn.v1")]
public sealed record ChatTurnWorkload(
    [property: Id(0)] NeuronId ChatId,
    [property: Id(1)] Guid TurnId,
    [property: Id(2)] string UserText) : WorkloadDescriptor;

[GenerateSerializer, Alias("db.workload.smart-prompt.v1")]
public sealed record SmartPromptWorkload(
    [property: Id(0)] Guid SmartPromptId,
    [property: Id(1)] Guid RevisionId,
    [property: Id(2)] string GoalText) : WorkloadDescriptor;

[GenerateSerializer, Alias("db.workload")]
public abstract record WorkloadDescriptor;

[GenerateSerializer, Alias("db.context-delta.v1")]
public sealed record ContextDelta(
    [property: Id(0)] ContextPath Path,
    [property: Id(1)] string SchemaHash,
    [property: Id(2)] string? PayloadJson,
    [property: Id(3)] string? BlobRef);

[GenerateSerializer, Alias("db.execution-context-state.v1")]
public sealed record ExecutionContextState(
    [property: Id(0)] ExecutionId ExecutionId,
    [property: Id(1)] IReadOnlyDictionary<string, ContextEntry> Entries);

[GenerateSerializer, Alias("db.context-entry.v1")]
public sealed record ContextEntry(
    [property: Id(0)] string SchemaHash,
    [property: Id(1)] string? PayloadJson,
    [property: Id(2)] string? BlobRef,
    [property: Id(3)] ContextDigest Digest);

[GenerateSerializer, Alias("db.context-query.v1")]
public sealed record ContextQuery([property: Id(0)] ContextPath Path);

[GenerateSerializer, Alias("db.execution-projection.v1")]
public sealed record ExecutionProjection(
    [property: Id(0)] ExecutionId ExecutionId,
    [property: Id(1)] ExecutionStatus Status,
    [property: Id(2)] ExecutionDriverKind Driver,
    [property: Id(3)] WorkloadDescriptor Workload);

[Alias("db.execution")]
public partial interface IExecution : INeuron, IHandle<StartExecution>, IHandle<CancelExecution>
{
    [Alias(nameof(Read))] Task<ExecutionProjection> Read();
}

[Alias("db.execution-context")]
public interface IExecutionContext : IEntity<ExecutionContextState>
{
    [Alias(nameof(Query))] Task<ContextEntry?> Query(ContextQuery query);
}

[GenerateSerializer, Alias("db.execution.start.v1")]
public sealed record StartExecution(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] ExecutionId ExecutionId,
    [property: Id(2)] WorkloadDescriptor Workload,
    [property: Id(3)] ExecutionDriverKind Driver,
    [property: Id(4)] IReadOnlyList<CapabilityId> Grants) : Synapse;

[GenerateSerializer, Alias("db.execution.cancel.v1")]
public sealed record CancelExecution(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] ExecutionId ExecutionId) : Synapse;
```

Wire csproj + slnx like Time module.

- [ ] **Step 4: PASS + commit**

```bash
git commit -m "feat(execution): add Execution contracts and ContextDelta"
```

---

### Task 3: ExecutionContext Entity + ExecutionNeuron (simulation-first)

**Files:**
- Create: `src/Modules/Execution/Execution/DigitalBrain.Modules.Execution.csproj`
- Create: `ExecutionModule.cs`, `ExecutionContextEntity.cs`, `ExecutionNeuron.cs`, `EffectBroker.cs`, `ExecutionSession.cs`
- Test: `tests/DigitalBrain.Simulation.Tests/Execution/ExecutionSpineTests.cs`
- Modify: `SimulationCollection` / fixture modules list to include `ExecutionModule`

- [ ] **Step 1: Failing simulation test**

```csharp
[Collection(SimulationCollection.Name)]
public sealed class ExecutionSpineTests(SimulationFixture fixture)
{
    [Fact]
    public async Task StartExecution_creates_context_and_runs_fake_capability()
    {
        var brain = fixture.Sim.Brain;
        var executionId = ExecutionId.New();
        var exec = brain.GetGrainProxy<IExecution>(executionId.ToString());

        await exec.HandleAsync(
            new StartExecution(
                CommandId.New(),
                executionId,
                new ChatTurnWorkload(NeuronId.For<IChat>(brain.Owner, "main"), Guid.NewGuid(), "hi"),
                ExecutionDriverKind.Agent,
                Grants: [CapabilityId.Parse("test.echo")]),
            CancellationToken.None);

        await JournalWait.ForAsync(
            brain,
            NeuronId.For<IExecution>(brain.Owner, executionId.ToString()),
            JournalKind.Outgoing,
            d => d.Synapse is ExecutionLifecycle { Status: ExecutionStatus.Completed });

        var ctx = brain.GetGrainProxy<IExecutionContext>(executionId.ToString());
        var entry = await ctx.Query(new ContextQuery(new ContextPath("test.echo")));
        Assert.NotNull(entry);
        Assert.Contains("pong", entry!.PayloadJson);
    }
}
```

- [ ] **Step 2: Run — FAIL**

- [ ] **Step 3: Implement Entity (mirror Chart/Entity pattern)**

```csharp
[GrainType("execution-context")]
public sealed class ExecutionContextEntity(
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<ExecutionContextState> state)
    : Entity<ExecutionContextState>(state), IExecutionContext
{
    public Task<ContextEntry?> Query(ContextQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (State is null) return Task.FromResult<ContextEntry?>(null);
        return Task.FromResult(State.Entries.TryGetValue(query.Path.Value, out var entry) ? entry : null);
    }

    internal async Task ApplyDeltaAsync(ContextDelta delta, ContextDigest digest)
    {
        var entries = State?.Entries is { } existing
            ? new Dictionary<string, ContextEntry>(existing)
            : new Dictionary<string, ContextEntry>();
        entries[delta.Path.Value] = new ContextEntry(delta.SchemaHash, delta.PayloadJson, delta.BlobRef, digest);
        var executionId = State?.ExecutionId ?? ExecutionId.Parse(this.GetPrimaryKeyString());
        await SaveAsync(new ExecutionContextState(executionId, entries));
    }
}
```

`ExecutionNeuron` : `Neuron`, `IExecution` — on `StartExecution`, create/bind context grain, register test capability handler in module DI for simulation, run a minimal in-neuron agent step that calls `EffectBroker.CallAsync`, merges delta, emits `ExecutionLifecycle` completed.

`ExecutionSession` (Sdk used inside silo):

```csharp
public sealed class ExecutionSession(ExecutionId id, IGrainFactory grains, EffectBroker broker)
{
    public ExecutionId Id => id;
    public Task<ContextEntry?> QueryAsync(ContextPath path) =>
        grains.GetGrain<IExecutionContext>(id.ToString()).Query(new ContextQuery(path));
    public Task<ContextDelta> CallAsync(CapabilityId capability, string requestJson, CancellationToken ct) =>
        broker.InvokeAsync(id, capability, requestJson, ct);
}
```

`EffectBroker` resolves `ICapabilityHandler` by `CapabilityId` from DI; records effect in neuron durable list; returns delta.

Register in `ExecutionModule.Configure`:

```csharp
builder.Services.AddSingleton<ICapabilityHandler, TestEchoCapabilityHandler>(); // only when fakes/testing — or always register test handler keyed and enable via config
```

Prefer: handlers registered by modules; ExecutionModule registers nothing vendor-specific. For Task 3 test, register echo handler in test `ConfigureSilo` **or** a `DigitalBrain.Modules.Execution.Testing` helper. Cleanest: `ExecutionModule` registers handlers only from DI; simulation test uses `options.ConfigureSilo` to `AddSingleton<ICapabilityHandler, TestEchoCapabilityHandler>()`.

- [ ] **Step 4: PASS**

- [ ] **Step 5: Commit**

```bash
git commit -m "feat(execution): ExecutionNeuron, ExecutionContext Entity, EffectBroker Sdk"
```

---

### Task 4: Aspire fakes flag + Fake Gmail/Salesforce/Search façades

**Files:**
- Create Integration modules under `src/Modules/Integrations/{Gmail,Salesforce,Search}/` (Contracts + impl + optional Aspire.Hosting)
- Modify: `AppHost.cs` — `.WithDigitalBrainFakes()` in Development
- Modify: `DigitalBrainNames` / hosting to stamp `DigitalBrain:Fakes:Enabled=true`
- Test: simulation capability calls + Aspire model test that env contains fakes flag

- [ ] **Step 1: Failing test — FakeGmail search merges ContextDelta**

```csharp
[Fact]
public async Task FakeGmail_search_writes_schema_shaped_context()
{
    // StartExecution with Gmail.Search grant; driver or direct CallAsync;
    // Query path "gmail.search" has JSON with subject New Customer
}
```

- [ ] **Step 2: Implement façade**

```csharp
public interface IGmailTransport
{
    Task<string> SearchJsonAsync(string account, string topic, CancellationToken ct);
}

public sealed class FakeGmailTransport : IGmailTransport
{
    public Task<string> SearchJsonAsync(string account, string topic, CancellationToken ct)
        => Task.FromResult("""{"messages":[{"id":"1","subject":"New Customer","from":"lead@acme.test"}]}""");
}

public sealed class GmailSearchHandler(IGmailTransport transport) : ICapabilityHandler
{
    public CapabilityId Id => CapabilityId.Parse("gmail.search");
    public async Task<ContextDelta> InvokeAsync(ExecutionId executionId, string requestJson, CancellationToken ct)
    {
        var json = await transport.SearchJsonAsync("fake", "New Customer", ct);
        return new ContextDelta(new ContextPath("gmail.search"), "gmail.search.v1", json, null);
    }
}
```

Same pattern for `salesforce.upsert` and `websearch.company` fakes.

`GmailModule.Configure`: if fakes or testing → `FakeGmailTransport`, else stub throwing `NotImplementedException` (“real MCP later”).

Aspire: `WithDigitalBrainFakes()` sets env `DigitalBrain__Fakes__Enabled=true` on brain/kernel.

- [ ] **Step 3: PASS + Aspire conformance test for env key**

- [ ] **Step 4: Commit**

```bash
git commit -m "feat(integrations): fake Gmail/Salesforce/Search façade transports"
```

---

### Task 5: Chat-on-Execution (ActiveExecutionId + lineage + MEAI tools → Sdk)

**Files:**
- Modify: Chat contracts/state for `ActiveExecutionId`, related execution ids
- Modify: `ChatTurnWorker` to `StartExecution` + Agent driver using MEAI tools that call `ExecutionSession.CallAsync`
- Modify: Kit tools remain allow-listed Capabilities where possible
- Test: existing ChatTurnTests still pass; add follow-up lineage test

- [ ] **Step 1: Failing test — follow-up seeds related context**

```csharp
[Fact]
public async Task FollowUpTurn_can_query_related_execution_context()
{
    // E1 completes with gmail.search in context
    // Chat sets ActiveExecutionId E1, then Send follow-up
    // E2 seed includes related E1; agent/tool reads C1 path
}
```

- [ ] **Step 2: Wire ChatTurnWorker**

Use existing `Agent` + `IChatClient` with `ChatOptions.Tools` from allow-listed `AIFunctionFactory.Create` wrappers that close over `ExecutionSession` (owner from trusted actor — never from model). Prefer MEAI function invocation already used by `TurnBoundFunction`.

Do **not** replace chat queue; wrap the AI work as an Execution workload.

- [ ] **Step 3: PASS prior ChatTurnTests + new tests**

- [ ] **Step 4: Commit**

```bash
git commit -m "feat(chat): run turns on Execution spine with ActiveExecutionId lineage"
```

---

### Task 6: Context providers, preferences, explainability

**Files:**
- Create: `IExecutionContextProvider` in Execution.Contracts
- Create: Preference Neuron/Entity + providers (Preference, Transcript, RelatedExecution, FactLedger compact)
- Create: Explainability capability/handler returning evidence JSON into Context or reply
- Test: preference injected into seed; explainability returns CommandId + ExecutionId refs

- [ ] **Step 1–4: TDD as above — keep providers pure and small**

```csharp
public interface IExecutionContextProvider
{
    Task ContributeAsync(ExecutionSeedBuilder seed, CancellationToken ct);
}
```

Commit: `feat(execution): context providers, preferences, explainability`

---

### Task 7: Smart Prompt module (chips doc + triggers + fake run)

**Files:**
- SmartPrompt Contracts/Entity/Neuron registry
- Time trigger subscription → StartExecution(SmartPromptWorkload)
- Manual Run now API/MCP tool
- Test: schedule or manual fire with fake Gmail→Search→SF→Chart capability chain via agent driver

- [ ] Keep editor document as structured segments + binding records (C# model first; Flutter chips can follow)
- [ ] User-facing surface stays goal text; no C# shown
- [ ] Commit: `feat(smart-prompt): activate and run on Execution with fake façades`

---

### Task 8: Multi-agent team participant (MAF Workflows, thin)

**Files:**
- Agent driver can run a two-participant workflow from `Microsoft.Agents.AI.Workflows` **only** for team workloads
- Both participants share `ExecutionSession` / grants
- Test: team workload completes with two capability calls recorded

Do not build a custom orchestration framework — wrap MAF minimally or use sequential second agent call if Workflows API friction is high; document choice in commit.

Commit: `feat(execution): multi-agent participants on shared ExecutionSession`

---

### Task 9: Script driver seam (out-of-process stub)

**Files:**
- `src/Kernel/DigitalBrain.Scripting` executable (or Modules sibling) accepting lease + calling Sdk over authenticated local channel **or** in-v1: Script driver compiles/runs only allow-listed Sdk calls in isolated AssemblyLoadContext if full process IPC is too large — prefer separate process per spec
- Test: Smart Prompt revision with `DriverKind.Script` echoes capability call

If IPC is large, deliver: script artifact stored + ScriptDriver invokes same handlers in sandbox AppDomain/ALC with no Orleans client — Sdk is in-process facade over grain calls from a restricted host. Spec forbids Kernel loading generated code — Scripting is separate executable started by AppHost when driver=Script.

Commit: `feat(scripting): out-of-process script driver seam for Execution`

---

### Task 10: Full test suite + live Aspire verification

- [ ] **Step 1: Run all tests high severity**

```powershell
dotnet test DigitalBrain.slnx --severity high
```

Expected: all green

- [ ] **Step 2: `aspire start` with fakes + Gemma**

```powershell
aspire start
```

Verify resources healthy (kernel, mcp, ollama/gemma as configured).

- [ ] **Step 3: Simulate user activity via digitalbrain MCP / HTTP**

- Create/open chat, send message, observe Execution + Context  
- Trigger Smart Prompt Run now (fake Gmail path)  
- Follow-up “list new customers from today”  
- Ask “why did you do it this way?” → explainability evidence  
- Confirm Aspire traces show `execution.*` / capability spans  

- [ ] **Step 4: Code review** (pr-review / code-simplifier / naming) — fix findings

- [ ] **Step 5: Update CONTEXT.md + ARCHITECTURE.md Smart Prompt sections**

- [ ] **Step 6: Final commit**

```bash
git commit -m "test: live Aspire harness verification and docs for fake-ready integrations"
```

**Done when:** tests green, live Aspire chat+Smart Prompt on fakes works, review clean, real MCP is the only remaining swap behind `I*Transport`.

---

## Library usage rules (do not invent)

| Need | Use |
|---|---|
| Durable aggregate | Existing `Neuron` : `DurableGrain` |
| Snapshot state | Existing `Entity<TState>` |
| Tools for LLM | `Microsoft.Extensions.AI` `AIFunctionFactory` + `ChatOptions.Tools` |
| Teams | `Microsoft.Agents.AI.Workflows` thinly, or sequential agents on same Sdk |
| Hosting | Aspire 13.5 `AddDigitalBrain` / `AddModule` / env stamps |
| Tests | `BrainSimulation` + `JournalWait` + xunit v3 + E2E AppHost fixture |
| Observation | Journals + BroadcastChannel + OTel (no new pub/sub product) |
| Integrations | Façade `I*Transport` Fake now / MCP later |

## Spec coverage check

| Spec area | Tasks |
|---|---|
| Execution Neuron + Context Entity + Sdk | 2–3 |
| No hop DTOs / ContextDelta | 2–4 |
| Multi-chat ActiveExecutionId + follow-up lineage | 5 |
| Fakes + Aspire | 4, 10 |
| Preferences / providers / explainability | 6 |
| Smart Prompt | 7 |
| Script + multi-agent | 8–9 |
| Live verify ready for real MCP | 10 |

## Placeholder scan

None intentional. Exact commands and core types included; façade modules follow the Gmail sample literally for Salesforce/Search.
