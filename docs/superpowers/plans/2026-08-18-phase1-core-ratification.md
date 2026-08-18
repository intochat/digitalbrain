# Phase 1: Core Ratification Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ratify the core model in code: `Entity<TState>` as a first-class plain-stateful-grain concept, the interpreted-kind cell tier retired, Orleans.Journaling *infrastructure* renamed to durable-state language (domain journals keep their name), `GetEntity` on the `IDigitalBrain` facade, the journal-ownership contract documented, and the `DigitalBrainNames` constants hoisted into `DigitalBrain.Abstractions` (dissolving the dual-compiled-type hazard flagged by phase 0's final review).

**Architecture:** Contracts land in `DigitalBrain.Abstractions` (`Entities/`, `Identity/EntityId`), the runtime base in `DigitalBrain.Core` (`Entity<TState> : DurableGrain` using the same keyed `IDurableValue<byte[]>` + `Serializer<TState>` pattern every existing grain uses — there is **no** `IPersistentState` fabric in this solution and we are not adding one). Entities are direct-call grains: no journals, no synapse membrane, not graph endpoints; neurons drive entity writes and journal the effect (spec §5).

**Tech Stack:** .NET 11 preview, Orleans 10.2.2 (+ Journaling 10.2.2-rc.2.alpha.1), Aspire 13.5.0-preview, central package management.

**Spec:** `docs/superpowers/specs/2026-08-18-digitalbrain-aspire-testing-sdk-design.md` (§5 ratified model, §11 phase 1 row). The phase-1 item "`Signal` removal" is already satisfied: the seed project carrying `Signal` was deleted in phase 0, and a whole-word repo grep for `Signal` returns zero hits — no task needed. Two spec amendments are part of this plan (Task 6): entity persistence wording (`IPersistentState<T>` → durable state — the spec's own fabric has no `IPersistentState` provider) and the names-file location (linked file → hoisted class in Abstractions).

## Global Constraints

- Working directory `E:\intochat\digitalbrain`, branch `finalv2`. NEVER read or write any path under `C:\Users\`.
- `TreatWarningsAsErrors=true`, `AnalysisLevel=preview-all`; central package management — no `Version` attributes, no new packages, no version bumps.
- Every task ends with `dotnet build DigitalBrain.slnx -warnaserror` → exit 0 (timeout 600000 ms) before its commit. No test projects exist yet (the testing SDK is phase 2); build + review is this phase's gate.
- Domain-journal names are UNTOUCHABLE in this phase: `NeuronJournal`, `NeuronFeed`, `JournalEntry`, `JournalKind`, `JournalRead`, `JournalSnapshot`, `JournalTally`, `IJournalObserver`, `JournalProjectionAttribute`, `INeuron.ReadJournal/Watch/Unwatch`, `ISessionNeuron.ReadNeuronJournal/WatchNeuron/UnwatchNeuron`, `IDigitalBrain.ReadJournalAsync/WatchJournalAsync`, `ChannelJournalObserver`, `OwnerSessionJournal`, `JournalProjection`, `NeuronJournalPage`, `JournaledSynapse`, Introspection's `TallyJournalRequest`/`JournaledFact`, and every kernel `Map*Streams`/`MapChatVoice`/`MapOwnerCommands` use.
- Also untouchable: package-owned APIs `AddJournalStorage()`, `UseJsonJournalFormat(...)`, `AddAzureBlobJournalStorage(...)` (Microsoft.Orleans.Journaling); and `DigitalBrainNames` member names AND values `Journal = "journal"` / `JournalConnection = "journal"` (deployed resource/connection names — renaming values would orphan persistent Azurite volumes).
- Commit per task with two `-m` flags (never here-strings): `git commit -m "<subject>" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"`.
- Never add meaningless `/// <summary>` comments; match surrounding code style. Line numbers cited below were verified at commit `053d5873` — if they have drifted, locate by the quoted content.

---

### Task 1: Hoist `DigitalBrainNames` into `DigitalBrain.Abstractions`

**Files:**
- Create: `src/Kernel/DigitalBrain.Abstractions/DigitalBrainNames.cs` (moved content)
- Delete: `src/Aspire/Shared/DigitalBrainNames.cs` (and the now-empty `src/Aspire/Shared/` directory)
- Modify: `src/Aspire/DigitalBrain.Aspire/DigitalBrain.Aspire.csproj` (remove the `<Compile Include="../Shared/DigitalBrainNames.cs" ...>` ItemGroup)
- Modify: `src/Aspire/DigitalBrain.Aspire.Hosting/DigitalBrain.Aspire.Hosting.csproj` (remove the Compile link; change `<Using Include="DigitalBrain.Aspire" />` to `<Using Include="DigitalBrain.Abstractions" />`)

**Interfaces:**
- Consumes: `DigitalBrain.Aspire.Hosting` directly references `DigitalBrain.Abstractions`; `DigitalBrain.Aspire` gets it transitively (via Client/Core, no PrivateAssets suppression) — both verified compile-visible.
- Produces: `public static class DigitalBrainNames` in namespace `DigitalBrain.Abstractions`, ONE copy in one assembly, all 14 members unchanged. Tasks 2+ reference it by this identity.

- [ ] **Step 1: Move the file**

`git mv src/Aspire/Shared/DigitalBrainNames.cs src/Kernel/DigitalBrain.Abstractions/DigitalBrainNames.cs`, then edit it: change `namespace DigitalBrain.Aspire;` to `namespace DigitalBrain.Abstractions;` and replace the 4-line header comment with:

```csharp
// Single source of truth for resource/connection names and configuration keys used by
// the Aspire hosting integration (AppHost side) and the silo/client runtime integration.
```

All 14 const members and values stay byte-identical.

- [ ] **Step 2: Update both csprojs**

In `DigitalBrain.Aspire.csproj`: delete the ItemGroup containing `<Compile Include="../Shared/DigitalBrainNames.cs" Link="DigitalBrainNames.cs" />`.
In `DigitalBrain.Aspire.Hosting.csproj`: delete the same `<Compile ...>` line and change `<Using Include="DigitalBrain.Aspire" />` to `<Using Include="DigitalBrain.Abstractions" />` (the hosting project has no `GlobalUsings.Abstractions.cs`; this keeps `DigitalBrainNames` unqualified there).

- [ ] **Step 3: Verify unqualified resolution on the runtime side**

`grep -n "global using DigitalBrain.Abstractions;" src/Aspire/DigitalBrain.Aspire/GlobalUsings.Abstractions.cs` — the line must exist (it does at `053d5873`); if absent, add it. The runtime files (`DigitalBrainClientHostingExtensions.cs`, `DigitalBrainRuntimeHostingExtensions.cs`, `DigitalBrainScriptHost.cs`) and `src/Kernel/DigitalBrain.Kernel/Auth/Hosting/AuthHostingExtensions.cs` keep using `DigitalBrainNames.X` unqualified.

- [ ] **Step 4: Build**

Run: `dotnet build DigitalBrain.slnx -warnaserror` → exit 0. If a file fails to resolve `DigitalBrainNames`, add `using DigitalBrain.Abstractions;` to that file — no other fix class is expected.

- [ ] **Step 5: Commit**

`git add -A && git commit -m "Hoist DigitalBrainNames into DigitalBrain.Abstractions" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"`

---

### Task 2: Rename Orleans.Journaling infrastructure wrappers to durable-state language

**Files:**
- Rename: `src/Kernel/DigitalBrain.Core/Hosting/JournalStorageHosting.cs` → `DurableStateHosting.cs`
- Rename: `src/Kernel/DigitalBrain.Core/Serialization/JournalJson.cs` → `DurableStateJson.cs`
- Modify: `src/Kernel/DigitalBrain.Core/Hosting/DigitalBrainRuntime.cs` (line 22 area)
- Modify: `src/Aspire/DigitalBrain.Aspire/DigitalBrainRuntimeHostingExtensions.cs` (line 35 area)
- Modify: `src/Aspire/DigitalBrain.Aspire.Hosting/Brain/DigitalBrainBuilder.cs` (property `Journal`)
- Modify: `src/Aspire/DigitalBrain.Aspire.Hosting/Brain/DigitalBrainHostingExtensions.cs` (lines 9, 34, 40, 91 areas)

**Interfaces:**
- Consumes: Task 1's hoisted `DigitalBrainNames` (members `Journal`, `JournalConnection` — names/values unchanged, see Global Constraints).
- Produces: `DurableStateHosting.AddDigitalBrainDurableState(this ISiloBuilder, IConfiguration)`; `DigitalBrainBuilder.DurableStateStore` (internal `IResourceBuilder<AzureBlobStorageResource>`); `DigitalBrainHostingExtensions.DurableStateConnectionName` (public). Later phases and docs use these names.

- [ ] **Step 1: Core rename**

In the renamed `DurableStateHosting.cs`: class `JournalStorageHosting` → `DurableStateHosting`; method `AddDigitalBrainJournalStorage` → `AddDigitalBrainDurableState`; keep `ConnectionName = "journal"` (value untouchable) but update its comment to `// must match DigitalBrainNames.JournalConnection — the blob connection backing Orleans.Journaling durable state`. Replace the startup-failure message string with:

```csharp
"Missing connection string 'journal'. Neuron journals and all durable grain state live in "
+ "Orleans.Journaling blob storage, so the host refuses to start without it."
```

In the renamed `DurableStateJson.cs`: class `JournalJson` → `DurableStateJson`. In `DigitalBrainRuntime.cs`: `JournalJson.TypeInfoResolver` → `DurableStateJson.TypeInfoResolver` (the package call `UseJsonJournalFormat` stays).

- [ ] **Step 2: Call-site and hosting-side renames**

`DigitalBrainRuntimeHostingExtensions.cs`: `silo.AddDigitalBrainJournalStorage(builder.Configuration);` → `silo.AddDigitalBrainDurableState(builder.Configuration);`.
`DigitalBrainBuilder.cs`: internal property `Journal` → `DurableStateStore` (ctor parameter `journal` → `durableStateStore`).
`DigitalBrainHostingExtensions.cs`: `public static string JournalConnectionName => DigitalBrainNames.JournalConnection;` → `public static string DurableStateConnectionName => DigitalBrainNames.JournalConnection;`; update the ctor argument (line 34 area), `RequireHealthyBeforeStart(journal.Resource)` variable naming (line 40 area), and `builder.WithReference(brain.Journal, DigitalBrainNames.JournalConnection)` → `builder.WithReference(brain.DurableStateStore, DigitalBrainNames.JournalConnection)` (line 91 area). The local variable holding `storage.AddBlobs(DigitalBrainNames.Journal)` (line 25 area) renames `journal` → `durableStateStore`.

- [ ] **Step 3: Verify no stragglers**

`grep -rn "JournalStorageHosting\|AddDigitalBrainJournalStorage\|JournalJson\|JournalConnectionName" src/` → no output. (`DigitalBrainNames.Journal`/`JournalConnection` member uses remain — they are resource vocabulary and stay.)

- [ ] **Step 4: Build**

`dotnet build DigitalBrain.slnx -warnaserror` → exit 0.

- [ ] **Step 5: Commit**

`git add -A && git commit -m "Rename Orleans.Journaling infrastructure wrappers to durable-state language" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"`

---

### Task 3: Introduce Entity contracts, `EntityId`, and the `Entity<TState>` base

**Files:**
- Create: `src/Kernel/DigitalBrain.Abstractions/Entities/IEntity.cs`
- Create: `src/Kernel/DigitalBrain.Abstractions/Identity/EntityId.cs`
- Create: `src/Kernel/DigitalBrain.Abstractions/Identity/GrainTypeNames.cs`
- Modify: `src/Kernel/DigitalBrain.Abstractions/Identity/NeuronId.cs` (delegate `GrainTypeNameOf` to the shared helper)
- Create: `src/Kernel/DigitalBrain.Core/Entities/Entity.cs`

**Interfaces:**
- Consumes: `OwnerId`, `IdentityPart.OwnerNameSeparator` (`Abstractions/Identity/`); `DurableGrain`, keyed `IDurableValue<byte[]>`, `Serializer<TState>` (the exact pattern at `Core/Cell/CellNeuron.cs:18-25` — being deleted in Task 4, so copy the pattern now); `NeuronId.GrainTypeNameOf`'s existing resolution rules (`NeuronId.cs:37`: `[GrainType]` ctor arg via `GetCustomAttributesData()`, else strip leading `I` from interfaces, else strip trailing `"Grain"`).
- Produces: `IEntity` (marker, mirrors `INeuron`'s base grain interface), `IEntity<TState> : IEntity` with `Task<TState?> Read()`, `EntityId` with `For<TEntity>`/`ToGrainId`, `internal static class GrainTypeNames { internal static string Of(Type contractType); }`, and `public abstract class Entity<TState> : DurableGrain, IEntity<TState>`. Task 5's facade and phase 3's `ChartEntity` build on exactly these.

- [ ] **Step 1: Contracts**

`src/Kernel/DigitalBrain.Abstractions/Entities/IEntity.cs` — first read `src/Kernel/DigitalBrain.Abstractions/Neurons/INeuron.cs` line 1-20 and mirror whatever grain base interface `INeuron` extends (expected `IGrainWithStringKey`); then:

```csharp
namespace DigitalBrain.Abstractions.Entities;

// An entity is a plain stateful grain: direct-call read/write, no journals, no synapse
// membrane, never a graph endpoint. Neurons drive entity writes and journal the effect.
public interface IEntity : IGrainWithStringKey
{
}

public interface IEntity<TState> : IEntity
    where TState : class
{
    [Alias("Read")]
    Task<TState?> Read();
}
```

(If the Abstractions project's implicit/global usings do not already cover `Orleans`/`System.Threading.Tasks` the way `INeuron.cs` relies on them, match `INeuron.cs`'s using style exactly.)

- [ ] **Step 2: Shared grain-type-name helper**

Create `src/Kernel/DigitalBrain.Abstractions/Identity/GrainTypeNames.cs` by MOVING the body of `NeuronId.GrainTypeNameOf` (`NeuronId.cs:37` onward) unchanged into:

```csharp
namespace DigitalBrain.Abstractions;

internal static class GrainTypeNames
{
    internal static string Of(Type contractType)
    {
        // body moved verbatim from NeuronId.GrainTypeNameOf
    }
}
```

Then in `NeuronId.cs` keep the public API but delegate: `public static string GrainTypeNameOf(Type neuronType) => GrainTypeNames.Of(neuronType);`. Match the file's existing namespace/usings (NeuronId lives in `DigitalBrain.Abstractions` via its Identity file conventions — mirror it).

- [ ] **Step 3: EntityId**

`src/Kernel/DigitalBrain.Abstractions/Identity/EntityId.cs` — mirror `NeuronId.cs`'s shape (same namespace as `NeuronId`, same serializer attributes style):

```csharp
using DigitalBrain.Abstractions.Entities;

namespace DigitalBrain.Abstractions;

[GenerateSerializer]
[Alias("db.entity-id")]
public readonly record struct EntityId
{
    public EntityId(string type, OwnerId owner, string name)
    {
        Type = type.ToLowerInvariant();
        Owner = owner;
        Name = name;
    }

    [Id(0)] public string Type { get; init; }
    [Id(1)] public OwnerId Owner { get; init; }
    [Id(2)] public string Name { get; init; }

    public string GrainKey => $"{Owner.Value}{IdentityPart.OwnerNameSeparator}{Name}";

    public GrainId ToGrainId() => GrainId.Create(Type, GrainKey);

    public static EntityId For<TEntity>(OwnerId owner, string name)
        where TEntity : IEntity
        => new(GrainTypeNames.Of(typeof(TEntity)), owner, name);

    public override string ToString() => $"{Type}:{GrainKey}";
}
```

Adjust ONLY to match `NeuronId.cs`'s literal conventions where they differ (e.g. how it lower-cases `Type` in the ctor, exact `GrainKey` composition at `NeuronId.cs:26` — copy that expression verbatim).

- [ ] **Step 4: Entity base**

`src/Kernel/DigitalBrain.Core/Entities/Entity.cs`. First read `src/Kernel/DigitalBrain.Core/Neuron/Neuron.cs` lines 1-40 and 240-250 to copy the exact `using` set, `ServiceProvider` access pattern, and the `WriteStateAsync` signature it wraps at `Neuron.cs:247` (`WriteNeuronStateAsync(ct)`). Then:

```csharp
using DigitalBrain.Abstractions.Entities;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;
using Orleans.Serialization;

namespace DigitalBrain.Core;

public abstract class Entity<TState> : DurableGrain, IEntity<TState>
    where TState : class
{
    private const string StateName = "entity.state";

    private readonly IDurableValue<byte[]> _state;
    private readonly Serializer<TState> _serializer;
    private TState? _snapshot;

    protected Entity()
    {
        _state = ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName);
        _serializer = ServiceProvider.GetRequiredService<Serializer<TState>>();
    }

    protected TState? State
        => _snapshot ??= _state.Value is { Length: > 0 } bytes ? _serializer.Deserialize(bytes) : null;

    public Task<TState?> Read() => Task.FromResult(State);

    protected async Task SaveAsync(TState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        _state.Value = _serializer.SerializeToArray(state);
        _snapshot = state;
        await WriteStateAsync(cancellationToken).ConfigureAwait(true);
    }
}
```

If `DurableGrain.WriteStateAsync` takes no `CancellationToken` (check how `Neuron.cs:247` calls it), drop the argument and keep the parameter for future-proofing only if the analyzer allows an unused parameter — otherwise remove the parameter entirely and match `DigitalBrainNeuron.Activate()`'s parameterless `WriteStateAsync()` style.

- [ ] **Step 5: Build**

`dotnet build DigitalBrain.slnx -warnaserror` → exit 0. Expected friction: analyzer rules on the new files (fix style, never suppress); `GrainId`/`Alias` needing a `using Orleans*;` — mirror `NeuronId.cs`.

- [ ] **Step 6: Commit**

`git add -A && git commit -m "Introduce Entity contracts, EntityId, and the Entity<TState> base" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"`

---

### Task 4: Retire the interpreted-kind cell tier

**Files:**
- Delete: `src/Kernel/DigitalBrain.Abstractions/Cell/ICell.cs`, `CellApply.cs`, `CellReset.cs`, `CellSnapshot.cs` (whole `Cell/` folder)
- Delete: `src/Kernel/DigitalBrain.Core/Cell/CellNeuron.cs`, `CellState.cs`, `ICellKind.cs`, `CalculatorKind.cs` (whole `Cell/` folder)
- Delete: `src/Kernel/DigitalBrain.Abstractions/Registry/IKindRegistry.cs`, `InstallKind.cs`; `src/Kernel/DigitalBrain.Core/Registry/KindRegistryNeuron.cs` (zero consumers outside these files — verified at `053d5873`)
- Modify: `src/Kernel/DigitalBrain.Mcp/TimeTools.cs` (delete `CellApplyAsync` ~L123-143 and `CellResetAsync` ~L145-162)
- Modify: `src/Kernel/DigitalBrain.Mcp/McpSurface.cs` (delete `CellApply`/`CellReset` consts ~L22-23)
- Modify: `src/Kernel/DigitalBrain.Core/Neuron/SynapseGraphNeuron.cs` (remove two cell special-cases)
- Modify: `src/Kernel/DigitalBrain.Abstractions/Registry/IRegistry.cs` (comment reword)
- Modify: `src/Kernel/DigitalBrain.Abstractions/GlobalUsings.cs` + all 29 `GlobalUsings.Abstractions.cs` copies (swap the Cell using for Entities)

**Interfaces:**
- Consumes: Task 3's `DigitalBrain.Abstractions.Entities` namespace (the global-using swap targets it).
- Produces: a solution with no `ICell`/cell-tier symbols; `SynapseGraphNeuron` validates ALL endpoint types uniformly via `ActiveModuleContractTypeMap.KnownGrainTypes`.

- [ ] **Step 1: Delete the definition files**

`git rm -r src/Kernel/DigitalBrain.Abstractions/Cell src/Kernel/DigitalBrain.Core/Cell src/Kernel/DigitalBrain.Core/Registry/KindRegistryNeuron.cs src/Kernel/DigitalBrain.Abstractions/Registry/IKindRegistry.cs src/Kernel/DigitalBrain.Abstractions/Registry/InstallKind.cs`

- [ ] **Step 2: Trim the MCP surface**

In `TimeTools.cs` delete the two whole methods `CellApplyAsync` and `CellResetAsync` (each is a `[McpServerTool]`-attributed method ~20 lines; delete attribute-to-closing-brace). In `McpSurface.cs` delete the two consts `CellApply = "cell_apply"` and `CellReset = "cell_reset"`.

- [ ] **Step 3: Remove the graph special-cases**

In `SynapseGraphNeuron.cs`:
- `RequireKnownEndpoint` (~L250-262): delete the `if (subject.Type == ICell.GrainTypeName) { ... }` branch (the `kind@instance` shape check and the KnownGrainTypes bypass) so every subject falls through to the uniform `KnownGrainTypes` check (~L270-277).
- `RequireTargetHandlesAlias` (~L294-300): delete the `if (target.Type == ICell.GrainTypeName) { return; }` early-out.
Keep everything else in both methods byte-identical.

- [ ] **Step 4: Swap the global usings (one command)**

```bash
grep -rl "global using DigitalBrain.Abstractions.Cell;" src/ | xargs sed -i 's/global using DigitalBrain.Abstractions.Cell;/global using DigitalBrain.Abstractions.Entities;/'
```

Expected: 30 files changed (`Abstractions/GlobalUsings.cs` + 29 `GlobalUsings.Abstractions.cs`).

- [ ] **Step 5: Reword the stale comment**

`IRegistry.cs` ~L4: replace the comment mentioning "idle/disabled/cold cells" with `// Durable per-owner catalog of installed instances.` (adjust to keep any non-cell part of the original sentence).

Explicitly NOT changed (do not touch, reviewers take note): `LibraryNeuron.cs` L263/L273's `"cell"` default bundle-member role string — a library-artifact vocabulary word unrelated to the retired `ICell` tier.

- [ ] **Step 6: Verify zero stragglers**

`grep -rn "ICell\b\|CellApply\|CellReset\|CellSnapshot\|CellNeuron\|ICellKind\|CalculatorKind\|IKindRegistry\|InstallKind\b\|KindRegistryNeuron\|Abstractions.Cell" src/` → no output.

- [ ] **Step 7: Build**

`dotnet build DigitalBrain.slnx -warnaserror` → exit 0. Expected friction: an orphaned `using` or an unused-member analyzer hit in `TimeTools.cs`/`McpSurface.cs`/`SynapseGraphNeuron.cs` after the deletions — fix in place.

- [ ] **Step 8: Commit**

`git add -A && git commit -m "Retire the interpreted-kind cell tier" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"`

---

### Task 5: Add `GetEntity` to the `IDigitalBrain` facade

**Files:**
- Modify: `src/Kernel/DigitalBrain.Client/IDigitalBrain.cs` (add one member after `Get<TNeuron>` ~L12)
- Modify: `src/Kernel/DigitalBrain.Client/DigitalBrainClient.cs` (implement it)

**Interfaces:**
- Consumes: Task 3's `IEntity` and `EntityId.For<TEntity>`; `DigitalBrainClient`'s existing `_grains` field and `Owner` property.
- Produces: `TEntity GetEntity<TEntity>(string name = "default") where TEntity : class, IEntity` — the facade's entity half. Phase 2's testing SDK and phase 3's MVP consume it.

- [ ] **Step 1: Interface member**

In `IDigitalBrain.cs`, directly after the `Get<TNeuron>` declaration:

```csharp
    TEntity GetEntity<TEntity>(string name = "default")
        where TEntity : class, IEntity;
```

Add `using DigitalBrain.Abstractions.Entities;` only if the project's global usings (updated in Task 4) don't already cover it — check first, don't duplicate.

- [ ] **Step 2: Implementation**

In `DigitalBrainClient.cs`, next to `GetGrainProxy` (~L45):

```csharp
    public TEntity GetEntity<TEntity>(string name = "default")
        where TEntity : class, IEntity
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _grains.GetGrain<TEntity>(EntityId.For<TEntity>(Owner, name).ToGrainId());
    }
```

No deny-list is needed (that guard exists to keep clients off `ISessionNeuron`/`IDigitalBrainNeuron`, which are neurons; entities have no such reserved contracts). Entities return the grain proxy directly — no `NeuronReference`-style wrapper, because entities are direct-call by design.

- [ ] **Step 3: Build**

`dotnet build DigitalBrain.slnx -warnaserror` → exit 0.

- [ ] **Step 4: Commit**

`git add -A && git commit -m "Add GetEntity to the IDigitalBrain facade" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"`

---

### Task 6: Journal-ownership contract doc + spec amendments

**Files:**
- Create: `docs/JOURNALS.md`
- Modify: `docs/superpowers/specs/2026-08-18-digitalbrain-aspire-testing-sdk-design.md` (two amendments)

**Interfaces:**
- Consumes: the ratified model (spec §5) and the names produced by Tasks 2–5.
- Produces: the documented contract phase 2's Tier 2 journal-semantics tests assert against.

- [ ] **Step 1: Write `docs/JOURNALS.md`**

```markdown
# Journals, Durable State, and History — who owns what

Three concepts share the word "journal" in this codebase's ancestry. This is the contract:

| Concept | Owner | What it is | Retention |
|---|---|---|---|
| Traffic journal | Every **neuron** (only neurons) | Incoming/Outgoing `SynapseDelivery` feeds: sequence-numbered observation windows with per-synapse-type tallies | Bounded: 512 entries / 512 KB per feed; reads past retention return a `ResetSnapshot` |
| Durable state | Every durable grain (neurons AND entities) | Orleans.Journaling persistence (`IDurableValue`/`IDurableList` over the `journal` blob connection) — infrastructure, not a domain concept; hosted via `DurableStateHosting.AddDigitalBrainDurableState` | Managed by Orleans.Journaling (append + compaction) |
| Corpus | The **owner** (or principal) | Watermarked, resumable story facts (`ICorpus`) — long-term history | Effectively unbounded |

## The rules

1. **Neurons own traffic journals. Entities own snapshots. Corpus owns history.**
   An `Entity<TState>` (`DigitalBrain.Core`) is a plain stateful grain: `Read()`/`SaveAsync()`
   over durable state. It has no journals and no synapse membrane, and it is never a
   synapse-graph endpoint.
2. **The session neuron is the owner's journal hub.** Owner-level views watch the session
   neuron's Outgoing journal (`OwnerSessionJournal`, the kernel SSE maps) and proxy-read
   subject neurons via `ISessionNeuron.ReadNeuronJournal`.
3. **Writes journal, reads don't.** Entity mutations are driven by neurons: a synapse fires,
   the handling neuron mutates the entity, and that neuron's Outgoing journal records the
   effect. Clients and UI read entities directly (`IDigitalBrain.GetEntity<TEntity>()`) —
   free and unjournaled.
4. **The word "journal" in domain code always means the traffic journal.** The persistence
   infrastructure uses durable-state language (`DurableStateHosting`, `DurableStateJson`,
   `DigitalBrainBuilder.DurableStateStore`). The blob resource/connection is still literally
   named `journal` (`DigitalBrainNames.Journal` / `JournalConnection`) — a deployed-name
   compatibility constraint, not vocabulary.

## Semantics pinned by tests (phase 2)

Resume sequences, the reset-snapshot path at the retention boundary, tallies,
checkpoint/restore, and watcher-drop behavior are pinned by the Tier 2 simulation suite
(`DigitalBrain.Testing`) — see the design spec §6 and §9.
```

- [ ] **Step 2: Amend the spec (two precise edits)**

In `docs/superpowers/specs/2026-08-18-digitalbrain-aspire-testing-sdk-design.md`:
1. §5, the `Entity<T>` paragraph: replace `SaveAsync`/`ReadAsync` over `IPersistentState<T>` wording with "SaveAsync/Read over Orleans.Journaling durable state (the solution's only persistence fabric — there is no `IPersistentState` provider and none is added)". Keep the rest of the paragraph.
2. §4's name-constant paragraph: replace the linked-source-file description with "one public `DigitalBrainNames` class in `DigitalBrain.Abstractions`, referenced by both Aspire packages (hoisted in phase 1 to dissolve the dual-compiled-type hazard)".

- [ ] **Step 3: Build (docs don't compile, but keep the gate uniform)**

`dotnet build DigitalBrain.slnx -warnaserror` → exit 0.

- [ ] **Step 4: Commit**

`git add -A && git commit -m "Document the journal ownership contract; amend spec for durable-state entities and hoisted names" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"`

---

### Task 7: Boot smoke — `aspire run` including the scripting resource

**Files:** none (verification only; any discovered fix routes back through the controller).

**Interfaces:**
- Consumes: everything above; Docker Desktop running.
- Produces: phase 1 exit evidence — the ratified solution still boots; closes phase 0's open recommendation (the `scripting` resource was never re-verified after its `#:project` fix).

- [ ] **Step 1: Preflight** — `docker info` reachable; else STOP and report.
- [ ] **Step 2: Launch** — `aspire run` in the background from the repo root.
- [ ] **Step 3: Wait and verify** — poll `aspire ps` until: brain fabric + `kernel` + `mcp` Healthy (up to 5 min); `curl -fsS http://localhost:5080/health` → `Healthy`. Additionally watch the `scripting` resource: it must NOT crash with a `#:project` path error (it runs client probes; a clean run-to-completion or a healthy waiting state both pass — capture its final state and last log lines either way).
- [ ] **Step 4: Stop and clean up** — kill the background run, `aspire stop` if needed, note remaining persistent containers.
- [ ] **Step 5: Final gate** — `dotnet build DigitalBrain.slnx -warnaserror` → exit 0; report resource states + scripting outcome. No commit (nothing changed).
