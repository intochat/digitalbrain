# DigitalBrain v2 — Slice 1: Universal Kernel with Chat, Provable via MCP

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The one universal `NeuronGrain` on Orleans journaling, with the invocation pipeline (idempotency, revisions, grants, effect gate), `Brain.Client` typed proxies, a minimal Chat kind, and a 3-tool MCP edge — proven end-to-end: `MCP neuron_invoke → Chat Neuron → journal revision → neuron_read/events`.

**Architecture:** One grain type keyed by `NeuronAddress`; the journal (an `IDurableList<NeuronEvent>`) is the storage truth and revision counter; kinds are keyed-DI handlers registered by modules; typed interfaces are `DispatchProxy` sugar over the universal envelope; effects are kernel-built-in Neurons that fail closed without an approval event.

**Tech Stack:** .NET 11 preview, Orleans 10.2.1 + `Microsoft.Orleans.Journaling` (latest 10.2.x-matching, alpha channel — IAW-proven), `ModelContextProtocol.AspNetCore` 1.4.0, xunit 2.9.3 + Orleans TestingHost, Aspire 13.4.6.

## Why this slice first (and not modules/ai)

The ModelFacet seam is *defined by* the kernel contract — modules/ai has nothing real to plug into until `INeuron`, the pipeline, and kind registration exist. Chat is the smallest real kind that exercises every kernel mechanism (state, command, idempotency, events) and it reproduces EVERYTHING-IS-A-NEURON's first proof (MCP → Chat) with the fewest moving parts. modules/ai lands as Slice 2 against a real seam instead of an imagined one.

## Slice map (each later slice gets its own plan when reached)

1. **This plan** — kernel/ + Sdk skeleton + Chat kind + minimal Brain.Mcp + smoke script.
2. modules/ai — ILlm kind, model catalog + Fast/Balanced/Reasoning, workflow-runner port, Ollama + AzureOpenAI only; sever `InoOperationWorkerGrain` coupling at the ModelFacet seam.
3. modules/workspace (destinations, feed, block vocabulary, inspector queries) + google/salesforce/web kinds + the conformance suite.
4. edge — Brain.UiGateway (`POST /ui/invoke`, `GET /ui/describe`, WS `/ui/watch`) + MCP catalog resource.
5. behaviors — behavior lifecycle kind (hash + journal), agent-authoring compile loop, Reqnroll BDD harness for behaviors.
6. app — Flutter rebuild: shell, Tier 1 views, block renderers, inspector, Today.
7. demolition — delete v1 trees and doomed tests; record deletion metrics; final gates (root suite green zero skips, live MCP→Flutter proof).

## Global Constraints

- Zero comments in any tracked source file (C#, csproj, etc.). Names carry meaning.
- Exact root gate: `dotnet test --logger "console;verbosity=minimal"` green, zero skips, after every task's commit (old suites remain untouched and green through this slice).
- All package versions live in `Directory.Packages.props` (central management, latest deliberate versions).
- Framework primitives over custom abstractions: durability = Orleans journaling; scheduling = reminders (not in this slice); observation = streams (not in this slice); handler wiring = keyed DI; test isolation = `TestClusterBuilder`. No custom persistence layer, no custom dispatcher, no mediator.
- Relative paths only; nothing under `C:\Users\`.
- New projects target `net11.0`, `<Nullable>enable</Nullable>`, `<ImplicitUsings>enable</ImplicitUsings>`.
- The v1 stack is not modified by this slice; demolition is Slice 7.

## File structure this slice creates

```text
kernel/Brain.Contracts/Brain.Contracts.csproj
kernel/Brain.Contracts/NeuronAddress.cs
kernel/Brain.Contracts/NeuronEnvelope.cs
kernel/Brain.Contracts/Synapses.cs
kernel/Brain.Contracts/BrainErrors.cs
kernel/Brain.Contracts/INeuron.cs
kernel/Brain.Contracts/INeuronKind.cs
kernel/Brain.Kernel/Brain.Kernel.csproj
kernel/Brain.Kernel/NeuronDurableState.cs
kernel/Brain.Kernel/NeuronGrain.cs
kernel/Brain.Kernel/EffectKind.cs
kernel/Brain.Kernel/KernelHosting.cs
kernel/Brain.Client/Brain.Client.csproj
kernel/Brain.Client/BrainCluster.cs
kernel/Brain.Client/NeuronProxy.cs
modules/Brain.Modules.Sdk/Brain.Modules.Sdk.csproj
modules/Brain.Modules.Sdk/BrainTest.cs
modules/Brain.Modules.Workspace/Brain.Modules.Workspace.csproj
modules/Brain.Modules.Workspace/ChatKind.cs
edge/Brain.Mcp/Brain.Mcp.csproj
edge/Brain.Mcp/Program.cs
edge/Brain.Mcp/NeuronTools.cs
behaviors/smoke/ChatSmoke.cs
behaviors/smoke/smoke.csproj
tests/Brain.KernelTests/Brain.KernelTests.csproj
tests/Brain.KernelTests/*.cs (per task below)
hosts/DigitalBrain.AppHost/AppHost additions (brain-kernel, brain-mcp resources)
```

---

### Task 1: Brain.Contracts — NeuronAddress

**Files:**
- Create: `kernel/Brain.Contracts/Brain.Contracts.csproj`, `kernel/Brain.Contracts/NeuronAddress.cs`
- Create: `tests/Brain.KernelTests/Brain.KernelTests.csproj`, `tests/Brain.KernelTests/NeuronAddressTests.cs`
- Modify: solution file via `dotnet sln add`

**Interfaces:**
- Produces: `NeuronAddress(string OwnerId, string SpaceId, string NeuronId)` with `ToGrainKey()`, `static Parse(string)`, `Kind` (first `/`-segment of `NeuronId`).

- [ ] **Step 1: Create projects and add to solution**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Orleans.Sdk" />
    <PackageReference Include="Microsoft.Orleans.Core.Abstractions" />
  </ItemGroup>
</Project>
```

Test project references `Brain.Contracts`, `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`, `Microsoft.Orleans.TestingHost`. Run: `dotnet sln add kernel/Brain.Contracts tests/Brain.KernelTests`

- [ ] **Step 2: Write the failing test**

```csharp
using Brain.Contracts;
namespace Brain.KernelTests;

public class NeuronAddressTests
{
    [Fact]
    public void Round_trips_through_grain_key()
    {
        var address = new NeuronAddress("local-owner", "actor/dev", "chat/main");
        var parsed = NeuronAddress.Parse(address.ToGrainKey());
        Assert.Equal(address, parsed);
        Assert.Equal("chat", parsed.Kind);
    }

    [Fact]
    public void Rejects_malformed_keys()
    {
        Assert.Throws<ArgumentException>(() => NeuronAddress.Parse("no-separators"));
    }
}
```

- [ ] **Step 3: Run to verify failure** — `dotnet test tests/Brain.KernelTests --logger "console;verbosity=minimal"` — expect compile failure (`NeuronAddress` missing).

- [ ] **Step 4: Implement**

```csharp
namespace Brain.Contracts;

[GenerateSerializer, Alias("brain.neuron-address.v2")]
public readonly record struct NeuronAddress(
    [property: Id(0)] string OwnerId,
    [property: Id(1)] string SpaceId,
    [property: Id(2)] string NeuronId)
{
    public string ToGrainKey() => $"{OwnerId}|{SpaceId}|{NeuronId}";
    public string Kind => NeuronId[..NeuronId.IndexOf('/', StringComparison.Ordinal) switch { < 0 => NeuronId.Length, var i => i }];

    public static NeuronAddress Parse(string grainKey)
    {
        var parts = grainKey.Split('|');
        if (parts.Length != 3 || parts.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException($"Invalid neuron grain key '{grainKey}'.", nameof(grainKey));
        return new NeuronAddress(parts[0], parts[1], parts[2]);
    }
}
```

- [ ] **Step 5: Run test to verify pass**, then commit: `git commit -m "feat(kernel): NeuronAddress universal identity"`

---

### Task 2: Brain.Contracts — envelope, events, synapses, errors, INeuron

**Files:**
- Create: `kernel/Brain.Contracts/NeuronEnvelope.cs`, `Synapses.cs`, `BrainErrors.cs`, `INeuron.cs`, `INeuronKind.cs`
- Test: `tests/Brain.KernelTests/EnvelopeSerializationTests.cs`

**Interfaces:**
- Produces (used by every later task):

```csharp
namespace Brain.Contracts;

[GenerateSerializer, Alias("brain.invocation.v2")]
public sealed record NeuronInvocation(
    [property: Id(0)] string Contract,
    [property: Id(1)] string InputJson,
    [property: Id(2)] string CommandId,
    [property: Id(3)] string CallerKey,
    [property: Id(4)] long? ExpectedRevision = null);

[GenerateSerializer, Alias("brain.receipt.v2")]
public sealed record NeuronReceipt(
    [property: Id(0)] string CommandId,
    [property: Id(1)] long Revision,
    [property: Id(2)] string Status,
    [property: Id(3)] string OutputJson,
    [property: Id(4)] string? EffectKey = null);

[GenerateSerializer, Alias("brain.event.v2")]
public sealed record NeuronEvent(
    [property: Id(0)] long Revision,
    [property: Id(1)] string Kind,
    [property: Id(2)] string PayloadJson,
    [property: Id(3)] string CommandId,
    [property: Id(4)] DateTimeOffset OccurredAt);

[GenerateSerializer, Alias("brain.event-page.v2")]
public sealed record NeuronEventPage([property: Id(0)] NeuronEvent[] Events, [property: Id(1)] long NextRevision);

[GenerateSerializer, Alias("brain.description.v2")]
public sealed record NeuronDescription(
    [property: Id(0)] string Kind,
    [property: Id(1)] long Revision,
    [property: Id(2)] string[] Contracts);

[GenerateSerializer, Alias("brain.snapshot.v2")]
public sealed record NeuronSnapshot([property: Id(0)] long Revision, [property: Id(1)] string StateJson);
```

```csharp
public enum SynapseRelation { Contains, Requires, Grants, BackedBy, Projects, CausedBy, Awaits, Approves, EmitsTo, UsesModule }

[GenerateSerializer, Alias("brain.synapse.v2")]
public sealed record SynapseRecord(
    [property: Id(0)] SynapseRelation Relation,
    [property: Id(1)] string TargetKey,
    [property: Id(2)] string Constraint,
    [property: Id(3)] long Revision);
```

```csharp
public static class BrainErrors
{
    public const string UnknownKind = "kind.unknown";
    public const string UnknownContract = "contract.unknown";
    public const string RevisionConflict = "action.revision-stale";
    public const string Replayed = "action.replayed";
    public const string GrantMissing = "grant.missing";
    public const string EffectNotApproved = "effect.not-approved";
}

public sealed class BrainException(string code, string detail) : Exception($"{code}: {detail}")
{
    public string Code { get; } = code;
}
```

```csharp
[Alias("brain.neuron.v2")]
public interface INeuron : IGrainWithStringKey
{
    [Alias("describe")] Task<NeuronDescription> DescribeAsync();
    [Alias("read")] Task<NeuronSnapshot> ReadAsync(string projection);
    [Alias("invoke")] Task<NeuronReceipt> InvokeAsync(NeuronInvocation invocation);
    [Alias("events")] Task<NeuronEventPage> ReadEventsAsync(long fromRevision, int max);
}
```

```csharp
public sealed record NeuronContext(NeuronAddress Address, string CallerKey, long Revision, IReadOnlyList<SynapseRecord> Synapses, IReadOnlyList<NeuronEvent> Journal);

public sealed record EffectProposal(string Provider, string PayloadJson, string PayloadDigest);

public sealed record KindResult(string OutputJson, IReadOnlyList<(string Kind, string PayloadJson)> Events, EffectProposal? Effect = null, SynapseRecord? Synapse = null);

public interface INeuronKind
{
    string Kind { get; }
    string[] Contracts { get; }
    ValueTask<KindResult> InvokeAsync(NeuronContext context, NeuronInvocation invocation);
    string Project(NeuronContext context, string projection);
}
```

- [ ] **Step 1: Write serialization round-trip test** (`Orleans.Serialization` deep-copy through `TestCluster` is covered later; here assert record equality semantics and `BrainException.Code`).

```csharp
public class EnvelopeSerializationTests
{
    [Fact]
    public void Receipt_and_invocation_are_value_equal()
    {
        var a = new NeuronInvocation("chat.post.v1", "{}", "cmd-1", "session|dev|s/1");
        var b = a with { };
        Assert.Equal(a, b);
    }

    [Fact]
    public void Brain_exception_carries_stable_code()
    {
        var exception = new BrainException(BrainErrors.GrantMissing, "no grant");
        Assert.Equal("grant.missing", exception.Code);
    }
}
```

- [ ] **Step 2: Run (fail) → implement the types above verbatim → run (pass) → commit** `feat(kernel): universal envelope, synapses, error codes, INeuron`

---

### Task 3: Brain.Kernel — NeuronGrain on journaling, kind registry, fold

**Files:**
- Create: `kernel/Brain.Kernel/Brain.Kernel.csproj` (refs `Brain.Contracts`; packages `Microsoft.Orleans.Server`, `Microsoft.Orleans.Journaling` — add `<PackageVersion Include="Microsoft.Orleans.Journaling" Version="<latest 10.2.x-matching>" />` to `Directory.Packages.props`)
- Create: `kernel/Brain.Kernel/NeuronDurableState.cs`, `NeuronGrain.cs`, `KernelHosting.cs`
- Test: `tests/Brain.KernelTests/NeuronGrainTests.cs` + `tests/Brain.KernelTests/TestKind.cs` + silo fixture

**Interfaces:**
- Consumes: everything from Tasks 1–2.
- Produces: `NeuronGrain` (grain type `"neuron"`), `KernelHosting.AddBrainKernel(this ISiloBuilder, params INeuronKind[] kinds)` registering kinds as keyed singletons, `BrainTestFixture` pattern for tests.

- [ ] **Step 1: Write the failing tests**

```csharp
using Brain.Contracts;
using Orleans.TestingHost;
namespace Brain.KernelTests;

public sealed class TestKind : INeuronKind
{
    public string Kind => "test";
    public string[] Contracts => ["test.echo.v1"];

    public ValueTask<KindResult> InvokeAsync(NeuronContext context, NeuronInvocation invocation) =>
        invocation.Contract switch
        {
            "test.echo.v1" => ValueTask.FromResult(new KindResult(invocation.InputJson, [("echoed", invocation.InputJson)])),
            _ => throw new BrainException(BrainErrors.UnknownContract, invocation.Contract)
        };

    public string Project(NeuronContext context, string projection) =>
        $$"""{"eventCount":{{context.Journal.Count}}}""";
}

public class NeuronGrainTests(ClusterFixture fixture) : IClassFixture<ClusterFixture>
{
    INeuron Neuron(string id) => fixture.Cluster.GrainFactory.GetGrain<INeuron>(
        new NeuronAddress("owner", "actor/test", $"test/{id}").ToGrainKey());

    static NeuronInvocation Echo(string commandId, string input = """{"v":1}""") =>
        new("test.echo.v1", input, commandId, "owner|actor/test|session/t");

    [Fact]
    public async Task Invoke_appends_exactly_one_revision()
    {
        var neuron = Neuron(Guid.NewGuid().ToString("N"));
        var receipt = await neuron.InvokeAsync(Echo("cmd-1"));
        Assert.Equal(1, receipt.Revision);
        var events = await neuron.ReadEventsAsync(0, 10);
        Assert.Single(events.Events);
        Assert.Equal("echoed", events.Events[0].Kind);
    }

    [Fact]
    public async Task Duplicate_command_replays_original_receipt()
    {
        var neuron = Neuron(Guid.NewGuid().ToString("N"));
        var first = await neuron.InvokeAsync(Echo("cmd-dup"));
        var second = await neuron.InvokeAsync(Echo("cmd-dup", """{"v":2}"""));
        Assert.Equal(first, second);
        Assert.Single((await neuron.ReadEventsAsync(0, 10)).Events);
    }

    [Fact]
    public async Task Wrong_expected_revision_fails_closed()
    {
        var neuron = Neuron(Guid.NewGuid().ToString("N"));
        await neuron.InvokeAsync(Echo("cmd-a"));
        var stale = Echo("cmd-b") with { ExpectedRevision = 0 };
        var exception = await Assert.ThrowsAsync<BrainException>(() => neuron.InvokeAsync(stale));
        Assert.Equal(BrainErrors.RevisionConflict, exception.Code);
    }

    [Fact]
    public async Task Unknown_kind_fails_closed_without_state()
    {
        var neuron = fixture.Cluster.GrainFactory.GetGrain<INeuron>(
            new NeuronAddress("owner", "actor/test", "nope/x").ToGrainKey());
        var exception = await Assert.ThrowsAsync<BrainException>(
            () => neuron.InvokeAsync(Echo("cmd-1")));
        Assert.Equal(BrainErrors.UnknownKind, exception.Code);
    }
}
```

`ClusterFixture` configures a `TestCluster` with volatile journaling storage — copy the exact registration lines from `E:\IAW\src\Testing\AgentTest.cs` (`AddMemoryGrainStorage`, `Services.AddSingleton<IStateMachineStorageProvider, VolatileStateMachineStorageProvider>()`, `AddStateMachineStorage()`) and call `AddBrainKernel(new TestKind())`.

- [ ] **Step 2: Run to verify failure** — types missing.

- [ ] **Step 3: Implement**

```csharp
namespace Brain.Kernel;

public sealed class NeuronDurableState(
    [FromKeyedServices("neuron-journal")] IDurableList<NeuronEvent> journal,
    [FromKeyedServices("neuron-receipts")] IDurableDictionary<string, NeuronReceipt> receipts,
    [FromKeyedServices("neuron-synapses")] IDurableList<SynapseRecord> synapses)
{
    public IDurableList<NeuronEvent> Journal { get; } = journal;
    public IDurableDictionary<string, NeuronReceipt> Receipts { get; } = receipts;
    public IDurableList<SynapseRecord> Synapses { get; } = synapses;
}
```

(If constructor keyed-injection needs an attribute mapper as in IAW, copy the `AgentStateMapper` pattern from `E:\IAW\src\Core\AI\AgentStateMapper.cs` into `NeuronStateMapper` — verify against IAW at execution time.)

```csharp
namespace Brain.Kernel;

[GrainType("neuron")]
public sealed class NeuronGrain(NeuronDurableState state, IServiceProvider services) : DurableGrain, INeuron
{
    NeuronAddress _address;
    INeuronKind? _kind;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _address = NeuronAddress.Parse(this.GetPrimaryKeyString());
        _kind = services.GetKeyedService<INeuronKind>(_address.Kind);
        return base.OnActivateAsync(cancellationToken);
    }

    long Revision => state.Journal.Count;

    NeuronContext Context(string callerKey) =>
        new(_address, callerKey, Revision, [.. state.Synapses], [.. state.Journal]);

    public Task<NeuronDescription> DescribeAsync() =>
        Task.FromResult(new NeuronDescription(_address.Kind, Revision, RequireKind().Contracts));

    public Task<NeuronSnapshot> ReadAsync(string projection) =>
        Task.FromResult(new NeuronSnapshot(Revision, RequireKind().Project(Context(""), projection)));

    public Task<NeuronEventPage> ReadEventsAsync(long fromRevision, int max)
    {
        var events = state.Journal.Skip((int)fromRevision).Take(Math.Clamp(max, 1, 500)).ToArray();
        return Task.FromResult(new NeuronEventPage(events, fromRevision + events.Length));
    }

    public async Task<NeuronReceipt> InvokeAsync(NeuronInvocation invocation)
    {
        var kind = RequireKind();
        if (state.Receipts.TryGetValue(invocation.CommandId, out var replay))
            return replay;
        if (invocation.ExpectedRevision is { } expected && expected != Revision)
            throw new BrainException(BrainErrors.RevisionConflict, $"expected {expected}, actual {Revision}");

        var result = await kind.InvokeAsync(Context(invocation.CallerKey), invocation);

        foreach (var (eventKind, payload) in result.Events)
            state.Journal.Add(new NeuronEvent(Revision + 1, eventKind, payload, invocation.CommandId, DateTimeOffset.UtcNow));
        if (result.Synapse is { } synapse)
            state.Synapses.Add(synapse with { Revision = Revision });

        var receipt = new NeuronReceipt(invocation.CommandId, Revision, "accepted", result.OutputJson);
        state.Receipts[invocation.CommandId] = receipt;
        await WriteStateAsync();
        return receipt;
    }

    INeuronKind RequireKind() =>
        _kind ?? throw new BrainException(BrainErrors.UnknownKind, _address.Kind);
}
```

```csharp
namespace Brain.Kernel;

public static class KernelHosting
{
    public static ISiloBuilder AddBrainKernel(this ISiloBuilder silo, params INeuronKind[] kinds)
    {
        foreach (var kind in kinds)
            silo.Services.AddKeyedSingleton<INeuronKind>(kind.Kind, kind);
        return silo;
    }
}
```

Note: exactly-one-revision semantics — a `KindResult` with multiple events still advances the receipt revision by the journal growth; the tests pin the observable contract. Verify `DurableGrain`/`WriteStateAsync` names against `E:\IAW\src\Core\Agents\Agent.cs` at execution time; adjust to the real base-class name if it differs, keeping the tests unchanged.

- [ ] **Step 4: Run tests to pass** — `dotnet test tests/Brain.KernelTests --logger "console;verbosity=minimal"`

- [ ] **Step 5: Root gate + commit** — `dotnet test --logger "console;verbosity=minimal"` (background), commit `feat(kernel): NeuronGrain with journaled pipeline core`

---

### Task 4: Grants fail closed

**Files:**
- Modify: `kernel/Brain.Kernel/NeuronGrain.cs` (insert grant check between replay check and revision check)
- Test: `tests/Brain.KernelTests/GrantTests.cs`

**Interfaces:**
- Rule produced: a caller whose `OwnerId` differs from the target's, or whose `SpaceId` starts with `"behavior/"`, must hold a `Grants` synapse on the target whose `Constraint` equals the invoked contract. Same-owner sessions pass.

- [ ] **Step 1: Failing tests**

```csharp
public class GrantTests(ClusterFixture fixture) : IClassFixture<ClusterFixture>
{
    [Fact]
    public async Task Foreign_caller_without_grant_fails_closed()
    {
        var neuron = fixture.Neuron("test", Guid.NewGuid().ToString("N"));
        var invocation = new NeuronInvocation("test.echo.v1", "{}", "cmd-1", "other-owner|actor/x|session/1");
        var exception = await Assert.ThrowsAsync<BrainException>(() => neuron.InvokeAsync(invocation));
        Assert.Equal(BrainErrors.GrantMissing, exception.Code);
        Assert.Empty((await neuron.ReadEventsAsync(0, 10)).Events);
    }

    [Fact]
    public async Task Behavior_identity_requires_contract_grant()
    {
        var neuron = fixture.Neuron("test", Guid.NewGuid().ToString("N"));
        var behaviorCaller = "owner|behavior/abc123|behavior/abc123";
        var denied = await Assert.ThrowsAsync<BrainException>(() =>
            neuron.InvokeAsync(new("test.echo.v1", "{}", "cmd-1", behaviorCaller)));
        Assert.Equal(BrainErrors.GrantMissing, denied.Code);
    }
}
```

- [ ] **Step 2: Fail → implement in `InvokeAsync`:**

```csharp
        var caller = NeuronAddress.Parse(invocation.CallerKey);
        var requiresGrant = caller.OwnerId != _address.OwnerId || caller.SpaceId.StartsWith("behavior/", StringComparison.Ordinal);
        if (requiresGrant && !state.Synapses.Any(s =>
                s.Relation == SynapseRelation.Grants
                && s.TargetKey == invocation.CallerKey
                && s.Constraint == invocation.Contract))
            throw new BrainException(BrainErrors.GrantMissing, $"{invocation.CallerKey} lacks {invocation.Contract}");
```

- [ ] **Step 3: Pass → commit** `feat(kernel): grants fail closed before handler invocation`

---

### Task 5: Effect gate — kernel-built-in Effect kind

**Files:**
- Create: `kernel/Brain.Kernel/EffectKind.cs`
- Modify: `kernel/Brain.Kernel/NeuronGrain.cs` (handle `KindResult.Effect`), `KernelHosting.cs` (always register `EffectKind`)
- Test: `tests/Brain.KernelTests/EffectGateTests.cs`

**Interfaces:**
- Produces: Effect Neuron addresses `{owner}|{space}|effect/{commandId}`; contracts `effect.approve.v1`, `effect.decline.v1`, `effect.claim-proof.v1`; `ApprovedEffectProof(string EffectKey, long EffectRevision, string PayloadDigest, string DecisionCommandId)` in `Brain.Contracts`. Connector kinds (later slices) accept only this proof.

- [ ] **Step 1: Failing tests**

```csharp
public class EffectGateTests(ClusterFixture fixture) : IClassFixture<ClusterFixture>
{
    [Fact]
    public async Task Proposing_kind_gets_effect_key_and_effect_awaits_decision()
    {
        var neuron = fixture.Neuron("proposer", Guid.NewGuid().ToString("N"));
        var receipt = await neuron.InvokeAsync(new("proposer.send.v1", """{"to":"x"}""", "cmd-1", fixture.OwnerSession));
        Assert.NotNull(receipt.EffectKey);
        var effect = fixture.Cluster.GrainFactory.GetGrain<INeuron>(receipt.EffectKey!);
        var claim = await Assert.ThrowsAsync<BrainException>(() =>
            effect.InvokeAsync(new("effect.claim-proof.v1", "{}", "cmd-2", fixture.OwnerSession)));
        Assert.Equal(BrainErrors.EffectNotApproved, claim.Code);
    }

    [Fact]
    public async Task Approved_effect_yields_proof_exactly_once()
    {
        var neuron = fixture.Neuron("proposer", Guid.NewGuid().ToString("N"));
        var receipt = await neuron.InvokeAsync(new("proposer.send.v1", """{"to":"x"}""", "cmd-1", fixture.OwnerSession));
        var effect = fixture.Cluster.GrainFactory.GetGrain<INeuron>(receipt.EffectKey!);
        await effect.InvokeAsync(new("effect.approve.v1", "{}", "cmd-approve", fixture.OwnerSession));
        var proof = await effect.InvokeAsync(new("effect.claim-proof.v1", "{}", "cmd-claim", fixture.OwnerSession));
        Assert.Contains("payloadDigest", proof.OutputJson);
        var replay = await effect.InvokeAsync(new("effect.claim-proof.v1", "{}", "cmd-claim", fixture.OwnerSession));
        Assert.Equal(proof, replay);
        var secondClaim = await Assert.ThrowsAsync<BrainException>(() =>
            effect.InvokeAsync(new("effect.claim-proof.v1", "{}", "cmd-claim-2", fixture.OwnerSession)));
        Assert.Equal(BrainErrors.EffectNotApproved, secondClaim.Code);
    }
}
```

Add `ProposerKind` test kind (contract `proposer.send.v1` returning `KindResult` with `Effect: new EffectProposal("test-provider", input, Sha256Hex(input))`) to the fixture registration.

- [ ] **Step 2: Fail → implement.** In `NeuronGrain.InvokeAsync`, after handler execution and before appending events:

```csharp
        string? effectKey = null;
        if (result.Effect is { } proposal)
        {
            effectKey = new NeuronAddress(_address.OwnerId, _address.SpaceId, $"effect/{invocation.CommandId}").ToGrainKey();
            var effect = GrainFactory.GetGrain<INeuron>(effectKey);
            await effect.InvokeAsync(new("effect.propose.v1",
                JsonSerializer.Serialize(proposal), invocation.CommandId, this.GetPrimaryKeyString()));
            state.Synapses.Add(new SynapseRecord(SynapseRelation.Awaits, effectKey, invocation.Contract, Revision));
        }
```

`EffectKind` folds its journal: `proposed` → (`approved` | `declined`) → `claimed`. `effect.claim-proof.v1` throws `BrainErrors.EffectNotApproved` unless the fold is exactly `approved`-and-unclaimed, and emits a `claimed` event carrying the serialized `ApprovedEffectProof`. The `effect.propose.v1` caller check: only same-owner callers whose `NeuronId` is not itself an effect.

- [ ] **Step 3: Pass → root gate → commit** `feat(kernel): unbypassable effect gate with single-claim proof`

---

### Task 6: Brain.Client — BrainCluster + typed DispatchProxy

**Files:**
- Create: `kernel/Brain.Client/Brain.Client.csproj` (refs `Brain.Contracts`; packages `Microsoft.Orleans.Client`, `Microsoft.Extensions.Hosting`)
- Create: `kernel/Brain.Client/BrainCluster.cs`, `kernel/Brain.Client/NeuronProxy.cs`
- Test: `tests/Brain.KernelTests/TypedProxyTests.cs`

**Interfaces:**
- Produces: `BrainCluster.Connect(string[] args)`, `brain.Get<T>(string addressKey)` where `T : INeuronContract`; `INeuronContract` marker + `IChatContract`-style interfaces map method `Post` → contract `"chat.post.v1"` via `[NeuronContract("chat.post.v1")]` attribute on methods.

- [ ] **Step 1: Failing test** (uses `TestKind` through the fixture's client):

```csharp
public interface ITestNeuron : INeuronContract
{
    [NeuronContract("test.echo.v1")]
    Task<EchoReply> EchoAsync(EchoRequest request);
}
public sealed record EchoRequest(int V);
public sealed record EchoReply(int V);

public class TypedProxyTests(ClusterFixture fixture) : IClassFixture<ClusterFixture>
{
    [Fact]
    public async Task Typed_call_travels_the_universal_envelope()
    {
        var proxy = NeuronProxy.Create<ITestNeuron>(
            fixture.Cluster.Client, fixture.AddressKey("test", "proxy-1"), fixture.OwnerSession);
        var reply = await proxy.EchoAsync(new EchoRequest(7));
        Assert.Equal(7, reply.V);
    }
}
```

- [ ] **Step 2: Fail → implement.** `NeuronProxy : DispatchProxy` — on invoke: read `[NeuronContract]` from the method, `JsonSerializer.Serialize` the single argument, call `INeuron.InvokeAsync(new(contract, json, Guid.NewGuid().ToString("N"), callerKey))`, deserialize `OutputJson` to the method's `Task<TResult>` type. `BrainCluster` is `IAWCluster` verbatim with renames (`AddBrainClient` extension configuring the Orleans client from Aspire config).

- [ ] **Step 3: Pass → commit** `feat(client): BrainCluster and typed proxies over the envelope`

---

### Task 7: Brain.Modules.Sdk — BrainTest harness

**Files:**
- Create: `modules/Brain.Modules.Sdk/Brain.Modules.Sdk.csproj` (refs `Brain.Contracts`, `Brain.Kernel`; packages `Microsoft.Orleans.TestingHost`, `xunit`)
- Create: `modules/Brain.Modules.Sdk/BrainTest.cs` — extracts `ClusterFixture` internals into the reusable public harness
- Modify: `tests/Brain.KernelTests` fixture to consume `BrainTest`

**Interfaces:**
- Produces: `abstract class BrainTest : IAsyncLifetime` with `TestCluster Cluster`, `virtual INeuronKind[] Kinds`, `INeuron Neuron(string kind, string id)`, `string OwnerSession`, `string AddressKey(string kind, string id)`. Every module's conformance tests and every behavior BDD test inherit this.

- [ ] **Step 1: Move, don't rewrite** — lift the silo configuration from the Task 3 fixture verbatim (memory storage + volatile state-machine storage + `AddBrainKernel(Kinds)`); delete the private fixture; kernel tests now inherit `BrainTest`.
- [ ] **Step 2: Full test project run passes unchanged → commit** `feat(sdk): reusable BrainTest cluster harness`

---

### Task 8: Chat kind — the first real Neuron

**Files:**
- Create: `modules/Brain.Modules.Workspace/Brain.Modules.Workspace.csproj` (refs `Brain.Contracts` only)
- Create: `modules/Brain.Modules.Workspace/ChatKind.cs`
- Test: `tests/Brain.KernelTests/ChatKindTests.cs`

**Interfaces:**
- Produces: kind `"chat"`; contracts `chat.post.v1` (input `{"text":...}`, event `chat.message`, output `{"revision":n}`) and projection `"conversation"` returning `{"messages":[{"text":...,"at":...}]}`; `IChat` typed interface in the same project:

```csharp
public interface IChat : INeuronContract
{
    static string ContractDescription => "Owner-scoped conversation neuron.";
    [NeuronContract("chat.post.v1")]
    Task<ChatPostReply> PostAsync(ChatPost post);
}
public sealed record ChatPost(string Text);
public sealed record ChatPostReply(long Revision);
```

- [ ] **Step 1: Failing tests** — post two messages (distinct command ids) → projection lists both in order; duplicate command id → one message; empty text → `BrainException` with `contract.unknown`? No — add `chat.invalid-input` guard: reject empty/oversize (>8 KB) text with `ArgumentException` mapped to `BrainException("input.invalid", ...)`; assert journal unchanged.

```csharp
public class ChatKindTests : BrainTest
{
    public override INeuronKind[] Kinds => [new ChatKind()];

    [Fact]
    public async Task Posts_fold_into_conversation_projection()
    {
        var chat = Neuron("chat", "main");
        await chat.InvokeAsync(new("chat.post.v1", """{"text":"hello"}""", "cmd-1", OwnerSession));
        await chat.InvokeAsync(new("chat.post.v1", """{"text":"world"}""", "cmd-2", OwnerSession));
        var snapshot = await chat.ReadAsync("conversation");
        Assert.Equal(2, snapshot.Revision);
        Assert.Contains("hello", snapshot.StateJson);
        Assert.Contains("world", snapshot.StateJson);
    }

    [Fact]
    public async Task Empty_text_fails_closed()
    {
        var chat = Neuron("chat", "guard");
        var exception = await Assert.ThrowsAsync<BrainException>(() =>
            chat.InvokeAsync(new("chat.post.v1", """{"text":""}""", "cmd-1", OwnerSession)));
        Assert.Equal("input.invalid", exception.Code);
        Assert.Equal(0, (await chat.ReadAsync("conversation")).Revision);
    }
}
```

- [ ] **Step 2: Fail → implement `ChatKind`** (~50 lines: parse input, guard, emit `chat.message` event; `Project` folds journal into the message array with `JsonSerializer`).
- [ ] **Step 3: Pass → root gate → commit** `feat(workspace): chat kind as first universal neuron`

---

### Task 9: Brain.Mcp — three tools

**Files:**
- Create: `edge/Brain.Mcp/Brain.Mcp.csproj` (refs `Brain.Client`, `Brain.Contracts`; packages `ModelContextProtocol.AspNetCore`, `Microsoft.Orleans.Client`)
- Create: `edge/Brain.Mcp/Program.cs`, `edge/Brain.Mcp/NeuronTools.cs`

**Interfaces:**
- Produces MCP tools: `neuron_describe(address)`, `neuron_read(address, projection)`, `neuron_invoke(address, contract, inputJson, commandId, expectedRevision?)`. Dev session identity: `{owner}|actor/mcp-dev|session/{connectionId}` — same owner as Flutter dev identity.

- [ ] **Step 1: Implement (IAW `MCP/Program.cs` shape, 13 lines + tools class):**

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddBrainClient();
builder.Services.AddMcpServer().WithHttpTransport().WithTools<NeuronTools>();
var app = builder.Build();
app.MapDefaultEndpoints();
app.MapMcp();
app.Run();
```

```csharp
internal sealed class NeuronTools(IClusterClient orleans)
{
    static string DevCaller => "local-owner|actor/mcp-dev|session/dev";

    [McpServerTool(Name = "neuron_describe")]
    [Description("Describe a neuron: kind, revision, contracts.")]
    public async Task<string> Describe([Description("Neuron address key")] string address) =>
        JsonSerializer.Serialize(await orleans.GetGrain<INeuron>(address).DescribeAsync());

    [McpServerTool(Name = "neuron_read")]
    [Description("Read a bounded projection of a neuron.")]
    public async Task<string> Read(string address, string projection = "default") =>
        JsonSerializer.Serialize(await orleans.GetGrain<INeuron>(address).ReadAsync(projection));

    [McpServerTool(Name = "neuron_invoke")]
    [Description("Invoke a typed contract on a neuron. Replays are idempotent by commandId.")]
    public async Task<string> Invoke(string address, string contract, string inputJson, string commandId, long? expectedRevision = null) =>
        JsonSerializer.Serialize(await orleans.GetGrain<INeuron>(address)
            .InvokeAsync(new(contract, inputJson, commandId, DevCaller, expectedRevision)));
}
```

- [ ] **Step 2: Wire hosts** — in `hosts/DigitalBrain.AppHost`, add a `brain-kernel` silo project resource (new minimal `hosts/Brain.Kernel.Host` or reuse existing kernel host pattern with `AddBrainKernel(new ChatKind())` + journaling storage: volatile provider for dev profile) and `brain-mcp` project resource referencing it. Old resources untouched.
- [ ] **Step 3: Build + `aspire doctor` + commit** `feat(edge): universal MCP tools over the kernel`

---

### Task 10: End-to-end proof + smoke script

**Files:**
- Create: `behaviors/smoke/smoke.csproj`, `behaviors/smoke/ChatSmoke.cs`
- Test: `tests/Brain.KernelTests/EndToEndChatTests.cs` (in-process E2E via BrainTest + typed proxy + ChatKind, already covered) — the live proof is procedural.

- [ ] **Step 1: Smoke script (the scripting-DX proof):**

```csharp
using Brain.Client;
using Brain.Modules.Workspace;

using var brain = await BrainCluster.Connect(args);
var chat = brain.Get<IChat>("local-owner|actor/mcp-dev|chat/main");
var reply = await chat.PostAsync(new ChatPost($"smoke {DateTimeOffset.UtcNow:O}"));
Console.WriteLine($"revision {reply.Revision}");
```

- [ ] **Step 2: Live proof procedure (record output in the PR/commit body):**

1. `dotnet build` (root) → success.
2. `aspire run` (background) → `aspire__list_resources`: `brain-kernel`, `brain-mcp` healthy.
3. MCP call `neuron_invoke(address: "local-owner|actor/mcp-dev|chat/main", contract: "chat.post.v1", inputJson: {"text":"hello from MCP"}, commandId: "proof-1")` → receipt revision 1.
4. Same call again (same commandId) → identical receipt, `neuron_read` projection shows exactly one message.
5. `dotnet run --project behaviors/smoke` → revision advances to 2; `neuron_read` shows both messages — MCP and script mutated the same Neuron.

- [ ] **Step 3: Exit gate** — exact root `dotnet test --logger "console;verbosity=minimal"` green, zero skips; commit `feat(v2): slice 1 complete — kernel proven MCP-to-script on one chat neuron` and record cycle metrics (lines added; nothing deleted yet — demolition is Slice 7).

---

## Self-review notes

- Spec coverage: §3 kernel (Tasks 1–5), §5.1 scripting (Tasks 6, 10), §4.1 harness (Task 7), Chat kind (Task 8), §7.1 MCP (Task 9). Feed/streams, workspace projections, ScheduleFacet/WorkFacet deliberately out of slice 1 (slices 3–5). Effect gate in slice 1 because the rail is non-negotiable from the first line.
- Placeholders: none; two explicit execution-time verifications are flagged (journaling base-class/mapper names — verify against `E:\IAW\src\Core\Agents\Agent.cs` and `E:\IAW\src\Core\AI\AgentStateMapper.cs`; `Microsoft.Orleans.Journaling` version matching Orleans 10.2.1).
- Type consistency: `NeuronInvocation(Contract, InputJson, CommandId, CallerKey, ExpectedRevision?)` used identically in Tasks 3–10; `OwnerSession`/`Neuron(kind,id)`/`AddressKey` defined in Task 7 and consumed in Tasks 8, 10.
