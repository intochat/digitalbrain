# DigitalBrain v2 Slice 1 — The Neuron Substrate Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make `dotnet run --project src/DigitalBrainConsole` print a weighted, durable synapse graph alongside the journal that produced it — proving a synapse is a real, learnable edge and not a message.

**Architecture:** v1 already has journaled neurons, typed handler dispatch, and a client facade. This slice (a) renames the message type `Synapse` → `Signal` to free the word, (b) introduces `Synapse` as a durable weighted edge stored in the source neuron's own state, (c) adds two-tier routing so a sender never names its receivers, and (d) proves it from a console app that references module one and nothing else.

**Tech Stack:** .NET 11, Orleans 9 (`Microsoft.Orleans.Journaling` for durable state, `InProcessTestCluster` for tests), xUnit v3 on Microsoft Testing Platform.

**Spec:** [`docs/superpowers/specs/2026-09-02-digitalbrain-v2-neuron-substrate-design.md`](../specs/2026-09-02-digitalbrain-v2-neuron-substrate-design.md)

## Global Constraints

- **TFM `net11.0`**, `Nullable=enable`, `ImplicitUsings=enable` — set in `Directory.Build.props`, never per-project.
- **`TreatWarningsAsErrors=true`** and **`AnalysisLevel=preview-all`** with `EnforceCodeStyleInBuild`. An unused `using`, a missing `ConfigureAwait`, or an undocumented public API **fails the build**. Match the surrounding file's style exactly.
- **Central Package Management** — versions live in `Directory.Packages.props`. A `PackageReference` in a `.csproj` carries **no** `Version` attribute.
- **Solution file is `DigitalBrain.slnx`** (XML format, not `.sln`). Every new project must be added to it or CI will not build it.
- **Test command (whole solution, as CI runs it):**
  `dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1`
  **Test command (one project, for the inner loop):**
  `dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj --verbosity minimal`
- **Orleans identity rules** (`IdentityPart`): a `NeuronId` type/owner/name may not contain `/` or whitespace.
- **Every `Signal` record** needs `[GenerateSerializer]` + `[Alias("db.…")]`. Orleans validates the serializer manifest at client construction; a missing attribute throws `CodecNotFoundException` at startup, not at use.
- **Journals stay capped at 512 entries / 512 KB** per feed (`NeuronFeed.MaxRetainedEntries`). Unchanged by this slice.

### Constants ratified for this slice

The spec left these open; these are the values. They live in one options class (Task 4) so they are trivially overridable.

| Constant | Value | Meaning |
|---|---|---|
| `PotentiationRate` (α) | `0.30` | `w' = w + α(1 − w)` on each handled delivery |
| `InitialLearnedWeight` | `0.50` | weight of a synapse created by a successful fire |
| `InitialDiscoveredWeight` | `0.10` | weight of a tier-3 synapse (slice 4; the constant exists now) |
| `InnateWeight` | `1.00` | declared by `IHandle<T>`; never decays |
| `HalfLife` | `14 days` | `w(t) = w × 0.5^(Δt / halfLife)` |
| `PruneFloor` | `0.05` | below this, a non-innate synapse is dropped |

**Consequence to expect:** two fires over a fresh learned synapse give `0.50 → 0.65 → 0.755`, which prints as **`w=0.76`**. The spec's §13 sample output says `0.72`; that number was illustrative and written before these constants existed. **0.76 is correct.** Update the spec's sample in Task 8.

### Deferred out of this slice

Roslyn/ALC compilation (slice 2), sensors and effectors (slice 3), tier-3 discovery and `UserCorrected` (slice 4), the Activity neuron, and the source generator (D9). `Modules/Execution` is left in place; its narrowing into Activity is a later plan.

---

## File Structure

**Renamed wholesale (Tasks 2–3):**

| From | To |
|---|---|
| `src/Kernel/DigitalBrain.Abstractions/` | `src/Kernel/DigitalBrain.Contracts/` |
| `src/Kernel/DigitalBrain.Core/` | `src/Kernel/DigitalBrain/` |
| `src/Kernel/DigitalBrain.Kernel/` | `src/Kernel/DigitalBrain.Silo/` |
| type `Synapse` (message) | type `Signal` |
| type `SynapseDelivery` | type `SignalDelivery` |
| type `SynapseId` | type `SignalId` |
| type `RequestSynapse<T>` | type `Signal<T>` |

**Created:**

| File | Responsibility |
|---|---|
| `src/Kernel/DigitalBrain.Contracts/Synapses/Synapse.cs` | The edge: identity, weight, decay, potentiation. Pure. |
| `src/Kernel/DigitalBrain.Contracts/Synapses/SynapseKind.cs` | `Innate` / `Learned` / `Discovered`. |
| `src/Kernel/DigitalBrain/Neuron/SynapseOptions.cs` | The six constants above. |
| `src/Kernel/DigitalBrain/Neuron/SynapseSet.cs` | A neuron's durable adjacency list: load, record, potentiate, prune. |
| `src/Kernel/DigitalBrain/Neuron/SignalHandlerIndex.cs` | Tier 1: signal type → neuron types declaring `IHandle<T>`. |
| `src/Kernel/DigitalBrain/Neuron/SignalRouter.cs` | Combines tiers 1 + 2 into an ordered receiver set. |
| `src/Kernel/DigitalBrain.Silo/Hosting/Brain.cs` | `Brain.CreateAsync(args)` — boots a local silo, returns `IDigitalBrain`. |
| `tests/DigitalBrain.Substrate.Tests/` | Focused test project: references module one + Testing SDK only. |

**Deleted:** `src/Modules/SmartPrompt/` (Task 1), `src/Kernel/DigitalBrain.Core/v2/CoreV2.cs` (Task 2).

**Naming note:** the console sketch calls `DigitalBrain.CreateAsync(args)`. A type named `DigitalBrain` inside namespace `DigitalBrain` makes every unqualified reference ambiguous, so the factory is **`Brain.CreateAsync(args)`**. The console line becomes `await using IDigitalBrain brain = await Brain.CreateAsync(args);`.

---

## Task 1: Retire Smart Prompts

Demolition first, so Tasks 2–3 rename a smaller surface instead of renaming code that is about to be deleted.

**Files:**
- Delete: `src/Modules/SmartPrompt/` (entire tree, 33 files)
- Delete: `tests/DigitalBrain.Simulation.Tests/SmartPrompt/` (6 files)
- Delete: `src/Kernel/DigitalBrain.Mcp/SmartPromptTools.cs`
- Modify: `DigitalBrain.slnx`, `src/Aspire/DigitalBrain.AppHost/AppHost.cs` + `.csproj`, `src/Kernel/DigitalBrain.Kernel/MapBehaviors.cs` + `.csproj`, `src/Kernel/DigitalBrain.Mcp/McpSurface.cs` + `Program.cs` + `.csproj`, `src/Modules/Execution/Contracts/WorkloadDescriptor.cs`, `src/Modules/Execution/Execution/ExecutionNeuron.cs`, `src/Modules/Salesforce/Salesforce/McpSalesforce.cs`, `tests/DigitalBrain.Aspire.Tests/NamesConformanceTests.cs` + `ReleaseModuleManifestConformanceTests.cs`, `tests/DigitalBrain.E2E.Tests/BehaviorSurfaceTests.cs` + `McpSurfaceTests.cs` + `.csproj`, `tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj`

**Interfaces:**
- Consumes: nothing.
- Produces: a repo with no `SmartPrompt` identifier anywhere. `Modules/Execution` survives with `WorkloadDescriptor` intact — narrowing it into Activity is a later plan.

- [ ] **Step 1: Record the baseline — the whole suite must be green before demolition**

```bash
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
```

Expected: PASS. If it is already red, **stop and report** — you cannot distinguish your breakage from pre-existing breakage otherwise.

- [ ] **Step 2: Delete the module, its tests, and its MCP surface**

```bash
git rm -r src/Modules/SmartPrompt
git rm -r tests/DigitalBrain.Simulation.Tests/SmartPrompt
git rm src/Kernel/DigitalBrain.Mcp/SmartPromptTools.cs
```

- [ ] **Step 3: Remove every reference and find the rest by compiling**

Remove the two `<Project Path="src/Modules/SmartPrompt/…" />` lines from `DigitalBrain.slnx`, and every `<ProjectReference>` to a `DigitalBrain.Modules.SmartPrompt*` csproj from: `DigitalBrain.AppHost.csproj`, `DigitalBrain.Kernel.csproj`, `DigitalBrain.Mcp.csproj`, `DigitalBrain.E2E.Tests.csproj`, `DigitalBrain.Simulation.Tests.csproj`.

Then let the compiler find the code sites:

```bash
dotnet build DigitalBrain.slnx -c Release
```

Fix each error by deleting the SmartPrompt-specific code, not by stubbing it. In `AppHost.cs` remove the SmartPrompt resource registration; in `MapBehaviors.cs` remove the SmartPrompt endpoint group; in `McpSurface.cs` / `Program.cs` remove the `SmartPromptTools` registration; in `WorkloadDescriptor.cs` and `ExecutionNeuron.cs` remove the SmartPrompt workload case; in `McpSalesforce.cs` remove the SmartPrompt chip binding. Delete `BehaviorSurfaceTests.cs` outright and remove SmartPrompt assertions from the two Aspire conformance tests and `McpSurfaceTests.cs`.

- [ ] **Step 4: Verify no identifier survives**

```bash
grep -rn -i "smartprompt" --include="*.cs" --include="*.csproj" --include="*.slnx" src/ tests/ *.slnx
```

Expected: **no output**. If a doc under `docs/` matches, that is fine — docs are Step 5.

- [ ] **Step 5: Retire the superseded docs**

```bash
git rm docs/superpowers/specs/2026-08-23-smart-prompt-execution-architecture-design.md \
       docs/superpowers/specs/2026-08-22-type-safe-behavior-event-architecture-design.md \
       docs/superpowers/specs/2026-08-25-reqnroll-behavior-runtime-design.md \
       docs/superpowers/plans/2026-08-23-smart-prompt-execution-harness.md \
       docs/smart-prompt-scenarios.html
```

In `docs/ARCHITECTURE.md`, replace the entire `## Smart Prompts` section with:

```markdown
## Automations

Retired in favour of generated C#. See
[2026-09-02-digitalbrain-v2-neuron-substrate-design.md](superpowers/specs/2026-09-02-digitalbrain-v2-neuron-substrate-design.md)
§9.3 — an automation is a neuron, authored by the system and compiled against module contracts.
```

- [ ] **Step 6: Full suite must be green**

```bash
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
```

Expected: PASS, with the SmartPrompt tests simply gone from the count.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor!: retire Smart Prompts in favour of generated C#

Deletes Modules/SmartPrompt (33 files), its 6 simulation tests, and the
MCP tool surface. Modules/Execution survives; its narrowing into the
Activity neuron is a later slice. Implements spec D11/M3."
```

---

## Task 2: Rename the message type to `Signal`

**Files:**
- Modify: every `.cs` referencing `Synapse`, `SynapseDelivery`, `SynapseId`, `RequestSynapse<>` (~40 files across `DigitalBrain.Abstractions`, `DigitalBrain.Core`, `DigitalBrain.Client`, `DigitalBrain.Sdk`, all modules, all tests)
- Rename: `Abstractions/Messaging/Synapse.cs` → `Abstractions/Signals/Signal.cs`, `Messaging/SynapseDelivery.cs` → `Signals/SignalDelivery.cs`, `Messaging/RequestSynapse.cs` → `Signals/Signal{TResponse}.cs`, `Identity/SynapseId.cs` → `Identity/SignalId.cs`
- Delete: `src/Kernel/DigitalBrain.Core/v2/CoreV2.cs`

**Interfaces:**
- Consumes: Task 1's clean tree.
- Produces: `Signal`, `Signal<TResponse>`, `SignalDelivery`, `SignalId`, `IHandle<TSignal> where TSignal : Signal`. The name `Synapse` is now **free** and unused — Task 4 claims it.

- [ ] **Step 1: Delete the non-compiling sketch**

`CoreV2.cs` declares `Synapse` twice, uses `IHandle<>` with an empty type argument, and has an empty `public` member. Nothing in it survives; the spec replaced it.

```bash
git rm src/Kernel/DigitalBrain.Core/v2/CoreV2.cs
rmdir src/Kernel/DigitalBrain.Core/v2 2>/dev/null || true
```

- [ ] **Step 2: Rename the types, longest identifier first**

Order matters: renaming `Synapse` before `SynapseDelivery` would corrupt `SynapseDelivery` into `SignalDelivery`-but-wrong. Longest first avoids that.

```bash
FILES=$(grep -rl --include="*.cs" -E "SynapseDelivery|RequestSynapse|SynapseId|\bSynapse\b" src/ tests/)
for f in $FILES; do
  sed -i \
    -e 's/\bSynapseDelivery\b/SignalDelivery/g' \
    -e 's/\bRequestSynapse\b/Signal/g' \
    -e 's/\bSynapseId\b/SignalId/g' \
    -e 's/\bSynapseTelemetry\b/SignalTelemetry/g' \
    -e 's/\bSynapseTypeIndex\b/SignalTypeIndex/g' \
    -e 's/\bSynapseAlias\b/SignalAlias/g' \
    -e 's/\bSynapse\b/Signal/g' \
    "$f"
done
```

`RequestSynapse<TResponse> : Synapse` becomes `Signal<TResponse> : Signal`, which is the shape the spec §4 specifies.

- [ ] **Step 3: Move the files to match**

```bash
mkdir -p src/Kernel/DigitalBrain.Abstractions/Signals
git mv src/Kernel/DigitalBrain.Abstractions/Messaging/Synapse.cs         src/Kernel/DigitalBrain.Abstractions/Signals/Signal.cs
git mv src/Kernel/DigitalBrain.Abstractions/Messaging/SynapseDelivery.cs src/Kernel/DigitalBrain.Abstractions/Signals/SignalDelivery.cs
git mv src/Kernel/DigitalBrain.Abstractions/Messaging/RequestSynapse.cs  src/Kernel/DigitalBrain.Abstractions/Signals/SignalOfResponse.cs
git mv src/Kernel/DigitalBrain.Abstractions/Identity/SynapseId.cs        src/Kernel/DigitalBrain.Abstractions/Identity/SignalId.cs
git mv src/Kernel/DigitalBrain.Core/SynapseTypeIndex.cs                  src/Kernel/DigitalBrain.Core/SignalTypeIndex.cs
git mv src/Kernel/DigitalBrain.Core/SynapseAlias.cs                      src/Kernel/DigitalBrain.Core/SignalAlias.cs
git mv src/Kernel/DigitalBrain.Core/Serialization/SynapseTelemetry.cs    src/Kernel/DigitalBrain.Core/Serialization/SignalTelemetry.cs
```

Then update the namespace in the four moved `Abstractions` files from `DigitalBrain.Abstractions.Messaging` to `DigitalBrain.Abstractions.Signals`, and fix the resulting `using` errors by compiling.

- [ ] **Step 4: Update the Orleans aliases**

Serializer aliases are **wire identity**, and this is a pre-1.0 rename with no deployed state to preserve, so change them to match:

```bash
grep -rn 'Alias("db.synapse' --include="*.cs" src/
```

Change `"db.synapse"` → `"db.signal"`, `"db.synapse-delivery"` → `"db.signal-delivery"`, `"db.synapse-id"` → `"db.signal-id"`, and `"DigitalBrain.Abstractions.ISynapse…"` interface aliases likewise.

- [ ] **Step 5: Build and fix the residue**

```bash
dotnet build DigitalBrain.slnx -c Release
```

Expected: a handful of errors from comments, XML doc text, and string literals the word-boundary `sed` missed (e.g. `"synapse membrane"` in prose). Fix each by hand. Prose that means *the edge* stays "synapse"; prose that means *the message* becomes "signal".

- [ ] **Step 6: Full suite must be green — this rename changes no behaviour**

```bash
dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
```

Expected: PASS, same test count as end of Task 1. **A behavioural failure here is a bad rename, not a bad design.**

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor!: rename the message type Synapse -> Signal

Frees the word 'synapse' for the durable weighted edge introduced in the
next commit. Pure rename, no behaviour change. Wire aliases move from
db.synapse* to db.signal* (pre-1.0, no deployed state). Implements D1/M1."
```

---

## Task 3: Rename the packages

**Files:**
- Rename: three project directories + their `.csproj`
- Modify: `DigitalBrain.slnx`, every `.csproj` with a `ProjectReference` to them, `src/Aspire/**` manifests, `src/Testing/DigitalBrain.Testing/DigitalBrain.Testing.csproj`, all test `.csproj`

**Interfaces:**
- Consumes: Task 2's tree.
- Produces: assemblies `DigitalBrain.Contracts`, `DigitalBrain`, `DigitalBrain.Silo`. **Namespaces are unchanged** — `DigitalBrain.Abstractions.*` and `DigitalBrain.Core` stay as C# namespaces. Only assembly and package identity move. This keeps the diff to project files.

- [ ] **Step 1: Move the directories and project files**

```bash
git mv src/Kernel/DigitalBrain.Abstractions src/Kernel/DigitalBrain.Contracts
git mv src/Kernel/DigitalBrain.Contracts/DigitalBrain.Abstractions.csproj \
       src/Kernel/DigitalBrain.Contracts/DigitalBrain.Contracts.csproj
git mv src/Kernel/DigitalBrain.Core src/Kernel/DigitalBrain
git mv src/Kernel/DigitalBrain/DigitalBrain.Core.csproj \
       src/Kernel/DigitalBrain/DigitalBrain.csproj
git mv src/Kernel/DigitalBrain.Kernel src/Kernel/DigitalBrain.Silo
git mv src/Kernel/DigitalBrain.Silo/DigitalBrain.Kernel.csproj \
       src/Kernel/DigitalBrain.Silo/DigitalBrain.Silo.csproj
```

- [ ] **Step 2: Repoint every reference**

```bash
grep -rl --include="*.csproj" --include="*.slnx" \
  -E "DigitalBrain\.(Abstractions|Core|Kernel)" . \
| grep -v -E "/(obj|bin)/" \
| xargs sed -i \
    -e 's#DigitalBrain\.Abstractions/DigitalBrain\.Abstractions\.csproj#DigitalBrain.Contracts/DigitalBrain.Contracts.csproj#g' \
    -e 's#DigitalBrain\.Core/DigitalBrain\.Core\.csproj#DigitalBrain/DigitalBrain.csproj#g' \
    -e 's#DigitalBrain\.Kernel/DigitalBrain\.Kernel\.csproj#DigitalBrain.Silo/DigitalBrain.Silo.csproj#g'
```

Then hand-fix the remaining path segments in `DigitalBrain.slnx` (`<Project Path="src/Kernel/…" />`) and any `InternalsVisibleTo` / `AssemblyName` values.

- [ ] **Step 3: Update the two package descriptions**

In `DigitalBrain.Contracts.csproj`: `<Description>The DigitalBrain programming model: signals, synapses, lineage metadata, handle contracts, and neuron identity.</Description>`

In `DigitalBrain.csproj`: `<Description>The DigitalBrain neuron substrate: journaled neurons, the weighted synapse graph, and typed signal routing on Orleans.</Description>`

- [ ] **Step 4: Build, test, commit**

```bash
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
git add -A
git commit -m "refactor!: rename kernel packages to Contracts/DigitalBrain/Silo

Abstractions -> Contracts, Core -> DigitalBrain (module one), Kernel ->
Silo. Assembly and package identity only; C# namespaces unchanged.
Nothing is published (0.1.0-alpha.1, no push step), so this is free now
and would cost a shim later. Implements D5/M2."
```

---

## Task 4: The synapse — a pure, testable edge

**Files:**
- Create: `src/Kernel/DigitalBrain.Contracts/Synapses/SynapseKind.cs`
- Create: `src/Kernel/DigitalBrain.Contracts/Synapses/Synapse.cs`
- Create: `src/Kernel/DigitalBrain/Neuron/SynapseOptions.cs`
- Create: `tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj`
- Create: `tests/DigitalBrain.Substrate.Tests/SynapseTests.cs`
- Modify: `DigitalBrain.slnx`

**Interfaces:**
- Consumes: `NeuronId` from `DigitalBrain.Abstractions.Identity`.
- Produces:
  - `enum SynapseKind { Innate, Learned, Discovered }`
  - `readonly record struct Synapse(NeuronId Source, NeuronId Target, string SignalType, double Weight, DateTimeOffset LastFiredAt, SynapseKind Kind, long FireCount, bool IsBlocking)`
  - `double Synapse.WeightAt(DateTimeOffset now, TimeSpan halfLife)`
  - `Synapse Synapse.Potentiate(DateTimeOffset now, double rate)`
  - `bool Synapse.IsPrunedAt(DateTimeOffset now, TimeSpan halfLife, double floor)`
  - `sealed class SynapseOptions` with the six constants

- [ ] **Step 1: Create the focused test project**

Deliberately narrow: it references module one and the Testing SDK, nothing else. `DigitalBrain.Simulation.Tests` drags in every module and is slow; this slice needs a fast loop.

`tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
    <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
    <NoWarn>$(NoWarn);ORLEANSEXP005</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3.mtp-v2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/Kernel/DigitalBrain/DigitalBrain.csproj" />
    <ProjectReference Include="../../src/Testing/DigitalBrain.Testing/DigitalBrain.Testing.csproj" />
  </ItemGroup>
</Project>
```

Add to `DigitalBrain.slnx` inside the `/tests/` folder element:

```xml
<Project Path="tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj" />
```

- [ ] **Step 2: Write the failing tests**

`tests/DigitalBrain.Substrate.Tests/SynapseTests.cs`:

```csharp
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Synapses;

namespace DigitalBrain.Substrate.Tests;

public sealed class SynapseTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 2, 12, 0, 0, TimeSpan.Zero);
    private static readonly NeuronId Chat = new("chat", new OwnerId("owner"), "main");
    private static readonly NeuronId Greeter = new("greeter", new OwnerId("owner"), "default");

    private static Synapse Learned(double weight = 0.50, DateTimeOffset? at = null)
        => new(Chat, Greeter, "UserMessageReceived", weight, at ?? T0, SynapseKind.Learned);

    [Fact]
    public void Potentiate_MovesWeightTowardOneByTheRate()
    {
        var potentiated = Learned().Potentiate(T0, rate: 0.30);

        Assert.Equal(0.65, potentiated.Weight, precision: 10);
        Assert.Equal(1, potentiated.FireCount);
    }

    [Fact]
    public void Potentiate_TwiceMatchesTheConsoleProof()
    {
        var twice = Learned().Potentiate(T0, 0.30).Potentiate(T0, 0.30);

        Assert.Equal(0.755, twice.Weight, precision: 10);
        Assert.Equal(2, twice.FireCount);
    }

    [Fact]
    public void Potentiate_NeverExceedsOne()
    {
        var synapse = Learned(weight: 0.99);

        for (var i = 0; i < 200; i++)
        {
            synapse = synapse.Potentiate(T0, 0.30);
        }

        Assert.True(synapse.Weight < 1.0);
    }

    [Fact]
    public void Potentiate_StampsTheFiringInstant()
    {
        var later = T0.AddDays(3);

        Assert.Equal(later, Learned().Potentiate(later, 0.30).LastFiredAt);
    }

    [Fact]
    public void WeightAt_HalvesEveryHalfLife()
    {
        var synapse = Learned(weight: 0.80);
        var halfLife = TimeSpan.FromDays(14);

        Assert.Equal(0.80, synapse.WeightAt(T0, halfLife), precision: 10);
        Assert.Equal(0.40, synapse.WeightAt(T0.AddDays(14), halfLife), precision: 10);
        Assert.Equal(0.20, synapse.WeightAt(T0.AddDays(28), halfLife), precision: 10);
    }

    [Fact]
    public void WeightAt_LeavesInnateSynapsesUntouched()
    {
        var innate = new Synapse(Chat, Greeter, "UserMessageReceived", 1.0, T0, SynapseKind.Innate);

        Assert.Equal(1.0, innate.WeightAt(T0.AddDays(3650), TimeSpan.FromDays(14)), precision: 10);
    }

    [Fact]
    public void IsPrunedAt_IsTrueOnlyOnceDecayCrossesTheFloor()
    {
        var synapse = Learned(weight: 0.50);
        var halfLife = TimeSpan.FromDays(14);

        Assert.False(synapse.IsPrunedAt(T0.AddDays(28), halfLife, floor: 0.05));  // 0.125
        Assert.True(synapse.IsPrunedAt(T0.AddDays(70), halfLife, floor: 0.05));   // ~0.0156
    }

    [Fact]
    public void IsPrunedAt_NeverPrunesAnInnateSynapse()
    {
        var innate = new Synapse(Chat, Greeter, "UserMessageReceived", 1.0, T0, SynapseKind.Innate);

        Assert.False(innate.IsPrunedAt(T0.AddDays(3650), TimeSpan.FromDays(14), floor: 0.05));
    }

    [Fact]
    public void Construction_RefusesABlockingSynapseThatIsNotInnate()
    {
        Assert.Throws<ArgumentException>(() =>
            new Synapse(Chat, Greeter, "UserMessageReceived", 0.5, T0, SynapseKind.Discovered, isBlocking: true));
    }

    [Fact]
    public void Construction_RefusesAnEmptySignalType()
    {
        Assert.Throws<ArgumentException>(() =>
            new Synapse(Chat, Greeter, "  ", 0.5, T0, SynapseKind.Learned));
    }
}
```

- [ ] **Step 3: Run to verify it fails**

```bash
dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj --verbosity minimal
```

Expected: FAIL to **compile** — `CS0246: The type or namespace name 'Synapse' could not be found`. A compile failure is the correct red for a type that does not exist yet.

- [ ] **Step 4: Write the implementation**

`src/Kernel/DigitalBrain.Contracts/Synapses/SynapseKind.cs`:

```csharp
namespace DigitalBrain.Abstractions.Synapses;

[GenerateSerializer]
[Alias("db.synapse-kind")]
public enum SynapseKind
{
    // Declared by IHandle<T> at compile time. Never decays, never pruned, may block.
    Innate,

    // Created by a successful fire. Decays; pruned below the floor.
    Learned,

    // Created by tier-3 similarity search. Decays fastest to nothing; may never block.
    Discovered,
}
```

`src/Kernel/DigitalBrain.Contracts/Synapses/Synapse.cs`:

```csharp
using System.Text.Json.Serialization;

using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Synapses;

// A directed, typed, weighted edge between two neurons. Stored in the SOURCE neuron's durable
// state, never as a grain of its own: an edge per grain does not survive the first million edges.
[GenerateSerializer]
[Alias("db.synapse")]
public readonly record struct Synapse
{
    [JsonConstructor]
    public Synapse(
        NeuronId source,
        NeuronId target,
        string signalType,
        double weight,
        DateTimeOffset lastFiredAt,
        SynapseKind kind,
        long fireCount = 0,
        bool isBlocking = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);
        ArgumentOutOfRangeException.ThrowIfNegative(weight);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(weight, 1.0);
        ArgumentOutOfRangeException.ThrowIfNegative(fireCount);

        // Spec D10: a discovered route can never gain veto power over a turn.
        if (isBlocking && kind != SynapseKind.Innate)
        {
            throw new ArgumentException(
                $"Only an innate synapse may block; '{kind}' may not.",
                nameof(isBlocking));
        }

        Source = source;
        Target = target;
        SignalType = signalType;
        Weight = weight;
        LastFiredAt = lastFiredAt;
        Kind = kind;
        FireCount = fireCount;
        IsBlocking = isBlocking;
    }

    [Id(0)] public NeuronId Source { get; }
    [Id(1)] public NeuronId Target { get; }
    [Id(2)] public string SignalType { get; }
    [Id(3)] public double Weight { get; }
    [Id(4)] public DateTimeOffset LastFiredAt { get; }
    [Id(5)] public SynapseKind Kind { get; }
    [Id(6)] public long FireCount { get; }
    [Id(7)] public bool IsBlocking { get; }

    // Read-time decay. There is deliberately no timer and no sweep on the hot path: a synapse
    // nobody uses is already weak the next time anyone looks at it.
    public double WeightAt(DateTimeOffset now, TimeSpan halfLife)
    {
        if (Kind == SynapseKind.Innate)
        {
            return Weight;
        }

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(halfLife, TimeSpan.Zero);

        var elapsed = now - LastFiredAt;
        return elapsed <= TimeSpan.Zero
            ? Weight
            : Weight * Math.Pow(0.5, elapsed / halfLife);
    }

    // Hebbian: a firing the receiver HANDLED raises the weight asymptotically toward 1 and
    // stamps the instant. An unhandled signal must not call this.
    public Synapse Potentiate(DateTimeOffset now, double rate)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(rate);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rate, 1.0);

        return new Synapse(
            Source,
            Target,
            SignalType,
            Weight + (rate * (1.0 - Weight)),
            now,
            Kind,
            FireCount + 1,
            IsBlocking);
    }

    public bool IsPrunedAt(DateTimeOffset now, TimeSpan halfLife, double floor)
        => Kind != SynapseKind.Innate && WeightAt(now, halfLife) < floor;

    public override string ToString()
        => $"{Source} --{SignalType}--> {Target}  w={Weight:F2}  fired={FireCount}  {Kind.ToString().ToLowerInvariant()}";
}
```

`src/Kernel/DigitalBrain/Neuron/SynapseOptions.cs`:

```csharp
namespace DigitalBrain.Core;

// The six constants that govern how the graph learns and forgets. Registered as a singleton by
// DigitalBrainRuntime.Add so a host or a test can replace the whole set.
public sealed class SynapseOptions
{
    // w' = w + rate * (1 - w). Asymptotic to 1, so a weight can never reach or exceed it.
    public double PotentiationRate { get; init; } = 0.30;

    public double InitialLearnedWeight { get; init; } = 0.50;

    // Tier-3 routes start weak and must earn their place through use (spec 5.1).
    public double InitialDiscoveredWeight { get; init; } = 0.10;

    public double InnateWeight { get; init; } = 1.00;

    public TimeSpan HalfLife { get; init; } = TimeSpan.FromDays(14);

    public double PruneFloor { get; init; } = 0.05;

    public double InitialWeightFor(SynapseKind kind) => kind switch
    {
        SynapseKind.Innate => InnateWeight,
        SynapseKind.Learned => InitialLearnedWeight,
        SynapseKind.Discovered => InitialDiscoveredWeight,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
```

Add `using DigitalBrain.Abstractions.Synapses;` to `SynapseOptions.cs`.

- [ ] **Step 5: Run to verify it passes**

```bash
dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj --verbosity minimal
```

Expected: PASS, 10 tests.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat: add Synapse as a durable weighted edge

The word freed by the previous commit now names the connection: source,
target, signal type, weight, kind, fire count. Decay is read-time
arithmetic (no timers); potentiation is Hebbian and asymptotic to 1.
Only innate synapses may block or resist pruning. Implements D1/D7/D10."
```

---

## Task 5: The synapse set — a neuron's durable adjacency list

**Files:**
- Create: `src/Kernel/DigitalBrain/Neuron/SynapseSet.cs`
- Modify: `src/Kernel/DigitalBrain/Neuron/Neuron.cs` (construct the set; expose `ReadSynapses`)
- Modify: `src/Kernel/DigitalBrain.Contracts/Neurons/INeuron.cs` (add `ReadSynapses`)
- Modify: `src/Kernel/DigitalBrain/Hosting/DigitalBrainRuntime.cs` (register `SynapseOptions`)
- Create: `tests/DigitalBrain.Substrate.Tests/SynapseSetTests.cs`

**Interfaces:**
- Consumes: `Synapse`, `SynapseKind`, `SynapseOptions` (Task 4); `NeuronFeed`'s keyed-`IDurable*` pattern.
- Produces:
  - `internal sealed class SynapseSet` with `IReadOnlyList<Synapse> All()`, `IReadOnlyList<Synapse> For(string signalType)`, `Synapse Record(NeuronId target, string signalType, SynapseKind kind)`, `int Prune()`
  - `Task<IReadOnlyList<Synapse>> INeuron.ReadSynapses()`

- [ ] **Step 1: Write the failing test**

`tests/DigitalBrain.Substrate.Tests/SynapseSetTests.cs`. This is a cluster test — `SynapseSet` resolves keyed `IDurableDictionary` from the grain's service provider, so it can only be exercised through a real neuron in `BrainSimulation`.

```csharp
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Synapses;
using DigitalBrain.Core;
using DigitalBrain.Testing;

namespace DigitalBrain.Substrate.Tests;

[GenerateSerializer]
[Alias("db.test.ping")]
public sealed record Ping(string Text) : Signal;

[Alias("DigitalBrain.Substrate.Tests.IPingSource")]
public interface IPingSource : INeuron
{
    [Alias(nameof(SendTo))]
    Task SendTo(NeuronId target, string text);
}

[Alias("DigitalBrain.Substrate.Tests.IPingSink")]
public interface IPingSink : INeuron;

internal sealed class PingSource : Neuron, IPingSource
{
    public Task SendTo(NeuronId target, string text) => FireAsync(target, new Ping(text));
}

internal sealed class PingSink : Neuron, IPingSink, IHandle<Ping>
{
    public Task HandleAsync(Ping signal, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class SynapseSetTests
{
    [Fact]
    public async Task FirstFire_CreatesALearnedSynapseAtTheInitialWeightThenPotentiatesIt()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });

        var source = brain.Grains.GetGrain<IPingSource>(
            new NeuronId("pingsource", new OwnerId("owner"), "a").ToGrainId());
        var sinkId = new NeuronId("pingsink", new OwnerId("owner"), "b");

        await source.SendTo(sinkId, "one");

        var synapse = Assert.Single(await source.ReadSynapses());
        Assert.Equal(sinkId, synapse.Target);
        Assert.Equal(nameof(Ping), synapse.SignalType);
        Assert.Equal(SynapseKind.Learned, synapse.Kind);
        Assert.Equal(0.65, synapse.Weight, precision: 10);
        Assert.Equal(1, synapse.FireCount);
    }

    [Fact]
    public async Task SecondFire_PotentiatesTheSameSynapseRatherThanAddingOne()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });

        var source = brain.Grains.GetGrain<IPingSource>(
            new NeuronId("pingsource", new OwnerId("owner"), "c").ToGrainId());
        var sinkId = new NeuronId("pingsink", new OwnerId("owner"), "d");

        await source.SendTo(sinkId, "one");
        await source.SendTo(sinkId, "two");

        var synapse = Assert.Single(await source.ReadSynapses());
        Assert.Equal(0.755, synapse.Weight, precision: 10);
        Assert.Equal(2, synapse.FireCount);
    }

    [Fact]
    public async Task DistinctTargets_GetDistinctSynapses()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });

        var source = brain.Grains.GetGrain<IPingSource>(
            new NeuronId("pingsource", new OwnerId("owner"), "e").ToGrainId());

        await source.SendTo(new NeuronId("pingsink", new OwnerId("owner"), "f"), "one");
        await source.SendTo(new NeuronId("pingsink", new OwnerId("owner"), "g"), "two");

        Assert.Equal(2, (await source.ReadSynapses()).Count);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj --verbosity minimal
```

Expected: FAIL to compile — `'INeuron' does not contain a definition for 'ReadSynapses'` and `'Neuron' does not contain a definition for 'FireAsync'`.

- [ ] **Step 3: Add `ReadSynapses` to the contract**

In `src/Kernel/DigitalBrain.Contracts/Neurons/INeuron.cs`, add the `using` for `DigitalBrain.Abstractions.Synapses` and this member alongside `ReadJournal`:

```csharp
    // The neuron's own outgoing edges. Free: no journal entry, no correlation, decay applied
    // at read. This is the query the graph UI and the console proof both use.
    [ReadOnly]
    [AlwaysInterleave]
    [Alias(nameof(ReadSynapses))]
    Task<IReadOnlyList<Synapse>> ReadSynapses();
```

- [ ] **Step 4: Write `SynapseSet`**

`src/Kernel/DigitalBrain/Neuron/SynapseSet.cs`:

```csharp
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Synapses;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.Core;

// A neuron's outgoing edges, held in its own durable state (spec D7). Keyed by target+signal
// type so the same pair of neurons can carry two differently-typed synapses.
internal sealed class SynapseSet
{
    private const string StateName = "synapses";

    private readonly IDurableDictionary<string, Synapse> _synapses;
    private readonly SynapseOptions _options;
    private readonly TimeProvider _time;
    private readonly NeuronId _owner;

    internal SynapseSet(IServiceProvider services, NeuronId owner, TimeProvider time)
    {
        _synapses = services.GetRequiredKeyedService<IDurableDictionary<string, Synapse>>(StateName);
        _options = services.GetService<SynapseOptions>() ?? new SynapseOptions();
        _owner = owner;
        _time = time;
    }

    internal static string KeyFor(NeuronId target, string signalType)
        => $"{target} {signalType}";

    // Ordered strongest-first, with decay applied. Callers see the CURRENT strength, not the
    // stored one — which is what makes read-time decay work without any background job.
    internal IReadOnlyList<Synapse> All()
    {
        var now = _time.GetUtcNow();

        return [.. _synapses.Values.OrderByDescending(synapse => synapse.WeightAt(now, _options.HalfLife))];
    }

    internal IReadOnlyList<Synapse> For(string signalType)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);

        var now = _time.GetUtcNow();

        return
        [
            .. _synapses.Values
                .Where(synapse => string.Equals(synapse.SignalType, signalType, StringComparison.Ordinal))
                .Where(synapse => !synapse.IsPrunedAt(now, _options.HalfLife, _options.PruneFloor))
                .OrderByDescending(synapse => synapse.WeightAt(now, _options.HalfLife))
        ];
    }

    // Hebbian bookkeeping. Call ONLY after the receiver handled the signal — an unhandled
    // delivery must not strengthen the path that produced it.
    internal Synapse Record(NeuronId target, string signalType, SynapseKind kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(signalType);

        var now = _time.GetUtcNow();
        var key = KeyFor(target, signalType);

        var current = _synapses.TryGetValue(key, out var existing)
            ? existing
            : new Synapse(
                _owner,
                target,
                signalType,
                _options.InitialWeightFor(kind),
                now,
                kind,
                isBlocking: false);

        var potentiated = current.Kind == SynapseKind.Innate
            ? current with { }
            : current.Potentiate(now, _options.PotentiationRate);

        _synapses[key] = potentiated;
        return potentiated;
    }

    // Storage reclamation only. Driven by ONE reminder per neuron, never one per synapse.
    internal int Prune()
    {
        var now = _time.GetUtcNow();

        var dead = _synapses
            .Where(entry => entry.Value.IsPrunedAt(now, _options.HalfLife, _options.PruneFloor))
            .Select(entry => entry.Key)
            .ToArray();

        foreach (var key in dead)
        {
            _synapses.Remove(key);
        }

        return dead.Length;
    }
}
```

**Note on the innate branch:** an innate synapse keeps weight `1.00` forever, so potentiating it would only churn `FireCount` and force a state write on every fire. `current with { }` returns it unchanged; if you later want innate fire counts, change this line and add a test for it.

- [ ] **Step 5: Wire it into `Neuron` and add `FireAsync`**

In `src/Kernel/DigitalBrain/Neuron/Neuron.cs`, add the field and construct it in the constructor after `_journal`:

```csharp
    private readonly SynapseSet _synapses;
```

```csharp
        _synapses = new SynapseSet(ServiceProvider, Id, TimeProvider);
```

Implement the contract member and the new protected fire:

```csharp
    public Task<IReadOnlyList<Synapse>> ReadSynapses() => Task.FromResult(_synapses.All());

    // Directed fire: deliver, then record the edge. The synapse is written only after Deliver
    // returns, so a receiver that threw never strengthens the path to itself.
    protected async Task<SignalDelivery> FireAsync(NeuronId receiver, Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var delivery = await SendAsync(receiver, signal)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        _synapses.Record(receiver, signal.GetType().Name, SynapseKind.Learned);
        await WriteStateAsync().ConfigureAwait(true);

        return delivery;
    }
```

Add `using DigitalBrain.Abstractions.Synapses;` at the top of `Neuron.cs`.

- [ ] **Step 6: Register `SynapseOptions`**

In `src/Kernel/DigitalBrain/Hosting/DigitalBrainRuntime.cs`, inside `Add`, before the module hook loop:

```csharp
        builder.Services.TryAddSingleton<SynapseOptions>();
```

Add `using Microsoft.Extensions.DependencyInjection.Extensions;`.

- [ ] **Step 7: Run to verify it passes**

```bash
dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj --verbosity minimal
```

Expected: PASS, 13 tests.

- [ ] **Step 8: Full suite, then commit**

```bash
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
git add -A
git commit -m "feat: store a neuron's synapses in its own durable state

SynapseSet is the adjacency list: keyed by target+signal type, ordered
strongest-first with decay applied at read, potentiated only after a
delivery the receiver accepted. INeuron gains ReadSynapses(), the free
query the console proof and the graph UI both use. Implements D7."
```

---

## Task 6: Routing — the sender stops naming its receivers

**Files:**
- Create: `src/Kernel/DigitalBrain/Neuron/SignalHandlerIndex.cs`
- Create: `src/Kernel/DigitalBrain/Neuron/SignalRouter.cs`
- Modify: `src/Kernel/DigitalBrain/Neuron/Neuron.cs` (add `BroadcastAsync`)
- Modify: `src/Kernel/DigitalBrain/Hosting/DigitalBrainRuntime.cs` (register the index)
- Create: `tests/DigitalBrain.Substrate.Tests/SignalRoutingTests.cs`

**Interfaces:**
- Consumes: `SynapseSet.For(signalType)` (Task 5); `ModuleManifest` from `DigitalBrain.Core.Hosting`.
- Produces:
  - `sealed class SignalHandlerIndex` with `IReadOnlyList<string> ReceiversOf(Type signalType)` returning **grain type names**
  - `sealed class SignalRouter` with `IReadOnlyList<NeuronId> Resolve(Signal signal, OwnerId owner, SynapseSet learned)`
  - `protected Task<int> Neuron.BroadcastAsync(Signal signal)` returning the receiver count

**Tier 3 is not built here.** `SignalRouter` has exactly two sources; discovery is slice 4.

- [ ] **Step 1: Write the failing test**

`tests/DigitalBrain.Substrate.Tests/SignalRoutingTests.cs`:

```csharp
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Testing;

namespace DigitalBrain.Substrate.Tests;

[GenerateSerializer]
[Alias("db.test.announced")]
public sealed record Announced(string Text) : Signal;

[Alias("DigitalBrain.Substrate.Tests.IAnnouncer")]
public interface IAnnouncer : INeuron
{
    [Alias(nameof(Announce))]
    Task<int> Announce(string text);
}

[Alias("DigitalBrain.Substrate.Tests.IEarA")]
public interface IEarA : INeuron;

[Alias("DigitalBrain.Substrate.Tests.IEarB")]
public interface IEarB : INeuron;

internal sealed class Announcer : Neuron, IAnnouncer
{
    public Task<int> Announce(string text) => BroadcastAsync(new Announced(text));
}

internal sealed class EarA : Neuron, IEarA, IHandle<Announced>
{
    public Task HandleAsync(Announced signal, CancellationToken cancellationToken) => Task.CompletedTask;
}

internal sealed class EarB : Neuron, IEarB, IHandle<Announced>
{
    public Task HandleAsync(Announced signal, CancellationToken cancellationToken) => Task.CompletedTask;
}

public sealed class SignalRoutingTests
{
    private static IAnnouncer AnnouncerIn(BrainSimulation brain, string name)
        => brain.Grains.GetGrain<IAnnouncer>(
            new NeuronId("announcer", new OwnerId("owner"), name).ToGrainId());

    [Fact]
    public async Task Broadcast_ReachesEveryNeuronTypeThatDeclaresIHandle()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });

        Assert.Equal(2, await AnnouncerIn(brain, "a").Announce("hello"));
    }

    [Fact]
    public async Task Broadcast_RecordsOneSynapsePerReceiver()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var announcer = AnnouncerIn(brain, "b");

        await announcer.Announce("hello");

        var synapses = await announcer.ReadSynapses();
        Assert.Equal(2, synapses.Count);
        Assert.All(synapses, synapse => Assert.Equal(nameof(Announced), synapse.SignalType));
    }

    [Fact]
    public async Task Broadcast_PotentiatesRatherThanDuplicatingOnTheSecondRun()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var announcer = AnnouncerIn(brain, "c");

        await announcer.Announce("one");
        await announcer.Announce("two");

        var synapses = await announcer.ReadSynapses();
        Assert.Equal(2, synapses.Count);
        Assert.All(synapses, synapse => Assert.Equal(0.755, synapse.Weight, precision: 10));
        Assert.All(synapses, synapse => Assert.Equal(2, synapse.FireCount));
    }

    [Fact]
    public async Task Broadcast_JournalsOneOutgoingEntryPerReceiver()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var announcer = AnnouncerIn(brain, "d");

        await announcer.Announce("hello");

        var read = await announcer.ReadJournal(JournalKind.Outgoing, 0);
        Assert.Equal(2, read.Delta.Count);
        Assert.Single(read.Delta.Select(delivery => delivery.CorrelationId).Distinct());
    }

    [Fact]
    public async Task Broadcast_WithNoDeclaredHandlerReachesNobodyAndRecordsNothing()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });
        var announcer = AnnouncerIn(brain, "e");

        // Unhandled: no neuron type declares IHandle<Ping> except PingSink, which handles Ping,
        // not Announced. Announced has exactly the two ears above.
        Assert.Equal(2, await announcer.Announce("hello"));
        Assert.Equal(2, (await announcer.ReadSynapses()).Count);
    }
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj --verbosity minimal
```

Expected: FAIL to compile — `'Neuron' does not contain a definition for 'BroadcastAsync'`.

- [ ] **Step 3: Write the tier-1 index**

`src/Kernel/DigitalBrain/Neuron/SignalHandlerIndex.cs`:

```csharp
using System.Collections.Concurrent;
using System.Reflection;

using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

// Tier 1 of routing (spec 5): which neuron GRAIN TYPES declare IHandle<TSignal>. Innate, free,
// and never wrong. Built by reflection for this slice; D9 replaces it with a source generator,
// which is also what removes the assembly scan from startup.
public sealed class SignalHandlerIndex
{
    private readonly ConcurrentDictionary<Type, IReadOnlyList<string>> _receivers = new();

    public IReadOnlyList<string> ReceiversOf(Type signalType)
    {
        ArgumentNullException.ThrowIfNull(signalType);

        return _receivers.GetOrAdd(signalType, static type =>
        {
            var handler = typeof(IHandle<>).MakeGenericType(type);

            return
            [
                .. AppDomain.CurrentDomain.GetAssemblies()
                    .Where(static assembly => !assembly.IsDynamic)
                    .SelectMany(SafeTypes)
                    .Where(candidate =>
                        candidate is { IsClass: true, IsAbstract: false }
                        && typeof(INeuron).IsAssignableFrom(candidate)
                        && handler.IsAssignableFrom(candidate))
                    .Select(NeuronId.GrainTypeNameOf)
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
            ];
        });
    }

    private static IEnumerable<Type> SafeTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException partial)
        {
            return partial.Types.OfType<Type>();
        }
    }
}
```

Add `using DigitalBrain.Abstractions.Identity;` for `NeuronId`.

- [ ] **Step 4: Write the router**

`src/Kernel/DigitalBrain/Neuron/SignalRouter.cs`:

```csharp
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrain.Core;

// Assembles the receiver set from tier 1 (innate) and tier 2 (learned). Tier 3 — similarity
// search — is slice 4 and deliberately absent: a miss here returns an empty set rather than
// guessing, which keeps every test in this slice deterministic.
public sealed class SignalRouter(SignalHandlerIndex index)
{
    private readonly SignalHandlerIndex _index = index;

    internal IReadOnlyList<NeuronId> Resolve(Signal signal, OwnerId owner, SynapseSet learned)
    {
        ArgumentNullException.ThrowIfNull(signal);
        ArgumentNullException.ThrowIfNull(learned);

        var signalType = signal.GetType();

        // Tier 2 first: a learned edge carries a weight and an ordering that tier 1 cannot.
        var receivers = new List<NeuronId>();
        var seen = new HashSet<NeuronId>();

        foreach (var synapse in learned.For(signalType.Name))
        {
            if (seen.Add(synapse.Target))
            {
                receivers.Add(synapse.Target);
            }
        }

        // Tier 1 fills in every declared handler the graph has not learned an edge to yet.
        foreach (var grainType in _index.ReceiversOf(signalType))
        {
            var id = new NeuronId(grainType, owner, "default");
            if (seen.Add(id))
            {
                receivers.Add(id);
            }
        }

        return receivers;
    }
}
```

- [ ] **Step 5: Add `BroadcastAsync` to `Neuron`**

In `Neuron.cs`, add the field, construct it, and add the method:

```csharp
    private readonly SignalRouter _router;
```

```csharp
        _router = ServiceProvider.GetService<SignalRouter>()
            ?? new SignalRouter(new SignalHandlerIndex());
```

```csharp
    // Emit to whoever the graph says listens. The sender names nobody: the receiver set comes
    // from its own synapses plus the innate handler index. Returns how many were reached.
    protected async Task<int> BroadcastAsync(Signal signal)
    {
        ArgumentNullException.ThrowIfNull(signal);

        var receivers = _router.Resolve(signal, Id.Owner, _synapses);
        if (receivers.Count == 0)
        {
            return 0;
        }

        // One correlation for the whole fan-out: the turn reconstructs from the graph alone.
        var correlation = ResolveEmissionCorrelation();
        var signalType = signal.GetType().Name;

        foreach (var receiver in receivers)
        {
            var delivery = await StageOutgoingAsync(signal, _handling, correlation)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            await GrainFactory.GetGrain<INeuron>(receiver.ToGrainId())
                .Deliver(delivery)
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            _synapses.Record(receiver, signalType, SynapseKind.Learned);
        }

        await WriteStateAsync().ConfigureAwait(true);
        return receivers.Count;
    }
```

**Why sequential and not `Task.WhenAll`:** a neuron has serialized turns (`NeuronConcurrency.RequireSerializedTurns`), and `_synapses.Record` mutates durable state. Parallel delivery here would interleave state writes within one turn. Parallel fan-out is a slice-3 concern and needs the writes staged first.

- [ ] **Step 6: Register the router and index**

In `DigitalBrainRuntime.Add`:

```csharp
        builder.Services.TryAddSingleton<SignalHandlerIndex>();
        builder.Services.TryAddSingleton<SignalRouter>();
```

- [ ] **Step 7: Run to verify it passes**

```bash
dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj --verbosity minimal
```

Expected: PASS, 18 tests.

- [ ] **Step 8: Full suite, then commit**

```bash
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
git add -A
git commit -m "feat: route signals by synapse set instead of by name

BroadcastAsync resolves receivers from the neuron's own edges (tier 2)
plus the innate IHandle<T> index (tier 1), fires each under one shared
correlation, and potentiates the edge it used. A sender no longer names
its receivers. Tier 3 discovery is deliberately absent. Implements D4/5."
```

---

## Task 7: Expose the graph through the facade

**Files:**
- Modify: `src/Kernel/DigitalBrain.Client/IDigitalBrain.cs` (add `IAsyncDisposable`, `GetSynapsesAsync`)
- Modify: `src/Kernel/DigitalBrain.Client/DigitalBrainClient.cs` (implement both)
- Modify: `src/Kernel/DigitalBrain.Contracts/Neurons/ISessionNeuron.cs` (add `ReadNeuronSynapses`)
- Modify: `src/Kernel/DigitalBrain/Neuron/SessionNeuron.cs` (implement it)
- Create: `tests/DigitalBrain.Substrate.Tests/FacadeTests.cs`

**Interfaces:**
- Consumes: `INeuron.ReadSynapses()` (Task 5).
- Produces:
  - `IDigitalBrain : IAsyncDisposable`
  - `Task<IReadOnlyList<Synapse>> IDigitalBrain.GetSynapsesAsync(NeuronId subject, CancellationToken)`
  - `Task<IReadOnlyList<Synapse>> ISessionNeuron.ReadNeuronSynapses(NeuronId subject)`

**Found during planning:** `IDigitalBrain` and `NeuronReference<T>` already exist in `DigitalBrain.Client` and already match spec §4.2 apart from these two additions. This task is small on purpose.

- [ ] **Step 1: Write the failing test**

`tests/DigitalBrain.Substrate.Tests/FacadeTests.cs`:

```csharp
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Testing;

namespace DigitalBrain.Substrate.Tests;

public sealed class FacadeTests
{
    [Fact]
    public async Task GetSynapsesAsync_ReturnsTheSubjectNeuronsEdges()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([]) });

        var announcerId = new NeuronId("announcer", new OwnerId(DigitalBrainNames.DefaultOwner), "facade");
        var announcer = brain.Grains.GetGrain<IAnnouncer>(announcerId.ToGrainId());

        await announcer.Announce("hello");

        var synapses = await brain.Brain.GetSynapsesAsync(announcerId);

        Assert.Equal(2, synapses.Count);
        Assert.All(synapses, synapse => Assert.Equal(announcerId, synapse.Source));
    }
}
```

Add `using DigitalBrain.Abstractions;` for `DigitalBrainNames`.

- [ ] **Step 2: Run to verify it fails**

```bash
dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj --verbosity minimal
```

Expected: FAIL to compile — `'IDigitalBrain' does not contain a definition for 'GetSynapsesAsync'`.

- [ ] **Step 3: Extend the contracts**

In `src/Kernel/DigitalBrain.Contracts/Neurons/ISessionNeuron.cs`, add:

```csharp
    [Alias(nameof(ReadNeuronSynapses))]
    [ResponseTimeout(NeuronCallTimeouts.LongRunning)]
    Task<IReadOnlyList<Synapse>> ReadNeuronSynapses(NeuronId subject);
```

In `src/Kernel/DigitalBrain.Client/IDigitalBrain.cs`, change the declaration to `public interface IDigitalBrain : IAsyncDisposable` and add:

```csharp
    Task<IReadOnlyList<Synapse>> GetSynapsesAsync(
        NeuronId subject,
        CancellationToken cancellationToken = default);
```

Add `using DigitalBrain.Abstractions.Synapses;` to both files.

- [ ] **Step 4: Implement both**

In `src/Kernel/DigitalBrain/Neuron/SessionNeuron.cs`, mirroring `ReadNeuronJournal` exactly:

```csharp
    public Task<IReadOnlyList<Synapse>> ReadNeuronSynapses(NeuronId subject)
        => subject == Id
            ? ReadSynapses()
            : GrainFactory.GetGrain<INeuron>(subject.ToGrainId()).ReadSynapses();
```

In `src/Kernel/DigitalBrain.Client/DigitalBrainClient.cs`, add the delegating method next to `ReadJournalAsync` (follow that method's existing session-neuron resolution) and an explicit disposal:

```csharp
    public Task<IReadOnlyList<Synapse>> GetSynapsesAsync(
        NeuronId subject,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Session().ReadNeuronSynapses(subject);
    }

    // The client owns no unmanaged resource and does not own the cluster client it was handed;
    // IAsyncDisposable exists so `await using IDigitalBrain brain = …` reads naturally in hosts
    // that DO own one (see Brain.CreateAsync).
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
```

If `DigitalBrainClient` has no `Session()` helper, use the same expression `ReadJournalAsync` already uses to obtain the `ISessionNeuron`.

- [ ] **Step 5: Run to verify it passes**

```bash
dotnet test tests/DigitalBrain.Substrate.Tests/DigitalBrain.Substrate.Tests.csproj --verbosity minimal
```

Expected: PASS, 19 tests.

- [ ] **Step 6: Full suite, then commit**

```bash
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
git add -A
git commit -m "feat: expose the synapse graph through IDigitalBrain

GetSynapsesAsync proxies through the session neuron the same way journal
reads do, and IDigitalBrain becomes IAsyncDisposable so hosts that own a
cluster client can 'await using' it. Implements D3."
```

---

## Task 8: The console proof

**Files:**
- Create: `src/Kernel/DigitalBrain.Silo/Hosting/Brain.cs`
- Modify: `src/DigitalBrainConsole/DigitalBrainConsole.csproj`
- Modify: `src/DigitalBrainConsole/Program.cs`
- Create: `src/DigitalBrainConsole/ChatNeuron.cs`, `GreeterNeuron.cs`, `LoggerNeuron.cs`, `Signals.cs`
- Modify: `DigitalBrain.slnx`
- Modify: `docs/superpowers/specs/2026-09-02-digitalbrain-v2-neuron-substrate-design.md` (§13 sample output)

**Interfaces:**
- Consumes: everything above.
- Produces: `static Task<IDigitalBrain> Brain.CreateAsync(string[] args, CancellationToken)`.

- [ ] **Step 1: Write the host factory**

`src/Kernel/DigitalBrain.Silo/Hosting/Brain.cs`. A local single-node silo with in-memory persistence, so `dotnet run` needs no external dependency:

```csharp
using DigitalBrain.Abstractions;
using DigitalBrain.Client;
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Orleans.Journaling;

namespace DigitalBrain;

// Named Brain, not DigitalBrain: a type sharing its namespace's name makes every unqualified
// reference to the namespace ambiguous.
public static class Brain
{
    public static async Task<IDigitalBrain> CreateAsync(
        string[] args,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);

        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        builder.UseOrleans(silo =>
        {
            silo.UseLocalhostClustering();
            silo.Services.AddSingleton<IJournalStorageProvider, VolatileJournalStorageProvider>();
            DigitalBrainRuntime.Add(silo, new ModuleManifest([]));
            silo.AddMemoryGrainStorage(DigitalBrainNames.DefaultGrainStorage);
            silo.UseInMemoryReminderService();
        });

        var host = builder.Build();
        await host.StartAsync(cancellationToken).ConfigureAwait(false);

        return new HostedBrain(host);
    }

    private sealed class HostedBrain(IHost host) : IDigitalBrain
    {
        private readonly IDigitalBrain _inner =
            DigitalBrainClient.Connect(host.Services.GetRequiredService<IGrainFactory>(), DigitalBrainNames.DefaultOwner);

        public OwnerId Owner => _inner.Owner;

        public Task ActivateAsync(CancellationToken cancellationToken = default)
            => _inner.ActivateAsync(cancellationToken);

        public NeuronReference<TNeuron> Get<TNeuron>(string name = "default") where TNeuron : INeuron
            => _inner.Get<TNeuron>(name);

        public TEntity GetEntity<TEntity>(string name = "default") where TEntity : class, IEntity
            => _inner.GetEntity<TEntity>(name);

        public TNeuron GetGrainProxy<TNeuron>(string name = "default") where TNeuron : class, INeuron
            => _inner.GetGrainProxy<TNeuron>(name);

        public Task FireAsync<TNeuron>(string name, Signal signal, CancellationToken cancellationToken = default)
            where TNeuron : INeuron
            => _inner.FireAsync<TNeuron>(name, signal, cancellationToken);

        public Task<IReadOnlyList<Synapse>> GetSynapsesAsync(NeuronId subject, CancellationToken cancellationToken = default)
            => _inner.GetSynapsesAsync(subject, cancellationToken);

        public Task<JournalRead> ReadJournalAsync(NeuronId subject, JournalKind kind, long afterSequence = 0, CancellationToken cancellationToken = default)
            => _inner.ReadJournalAsync(subject, kind, afterSequence, cancellationToken);

        public IAsyncEnumerable<JournalRead> WatchJournalAsync(NeuronId subject, JournalKind kind, long afterSequence = 0, CancellationToken cancellationToken = default)
            => _inner.WatchJournalAsync(subject, kind, afterSequence, cancellationToken);

        public async ValueTask DisposeAsync()
        {
            await host.StopAsync().ConfigureAwait(false);
            host.Dispose();
        }
    }
}
```

Add the `using` lines for `DigitalBrain.Abstractions.Identity`, `.Entities`, `.Journals`, `.Neurons`, `.Signals`, `.Synapses`.

- [ ] **Step 2: Point the console at module one and nothing else**

`src/DigitalBrainConsole/DigitalBrainConsole.csproj` — remove the container-tools package (unused for this proof) and add the two references:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Kernel/DigitalBrain/DigitalBrain.csproj" />
    <ProjectReference Include="../Kernel/DigitalBrain.Silo/DigitalBrain.Silo.csproj" />
  </ItemGroup>
</Project>
```

`TargetFramework`, `Nullable` and `ImplicitUsings` come from `Directory.Build.props`; do not repeat them.

Add to `DigitalBrain.slnx`:

```xml
<Project Path="src/DigitalBrainConsole/DigitalBrainConsole.csproj" />
```

- [ ] **Step 3: Write the three neurons and their signals**

`src/DigitalBrainConsole/Signals.cs`:

```csharp
using DigitalBrain.Abstractions.Signals;

namespace DigitalBrainConsole;

[GenerateSerializer]
[Alias("db.console.user-message-received")]
public sealed record UserMessageReceived(string Text) : Signal;
```

`src/DigitalBrainConsole/ChatNeuron.cs`:

```csharp
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;

namespace DigitalBrainConsole;

[Alias("DigitalBrainConsole.IChatNeuron")]
public interface IChatNeuron : INeuron;

// Handles the message, then broadcasts it onward. It names no receiver: who hears this is a
// property of the graph, not of this code.
internal sealed class ChatNeuron : Neuron, IChatNeuron, IHandle<UserMessageReceived>
{
    public async Task HandleAsync(UserMessageReceived signal, CancellationToken cancellationToken)
    {
        var reached = await BroadcastAsync(signal).ConfigureAwait(true);
        Console.WriteLine($"[chat]    broadcast {signal.Text.Length} chars -> {reached} receivers");
    }
}
```

`src/DigitalBrainConsole/GreeterNeuron.cs`:

```csharp
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;

namespace DigitalBrainConsole;

[Alias("DigitalBrainConsole.IGreeterNeuron")]
public interface IGreeterNeuron : INeuron;

internal sealed class GreeterNeuron : Neuron, IGreeterNeuron, IHandle<UserMessageReceived>
{
    public Task HandleAsync(UserMessageReceived signal, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[greeter] handled UserMessageReceived(\"{signal.Text}\") -> \"Hello!\"");
        return Task.CompletedTask;
    }
}
```

`src/DigitalBrainConsole/LoggerNeuron.cs`:

```csharp
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Core;

namespace DigitalBrainConsole;

[Alias("DigitalBrainConsole.ILoggerNeuron")]
public interface ILoggerNeuron : INeuron;

internal sealed class LoggerNeuron : Neuron, ILoggerNeuron, IHandle<UserMessageReceived>
{
    public Task HandleAsync(UserMessageReceived signal, CancellationToken cancellationToken)
    {
        Console.WriteLine($"[logger]  recorded \"{signal.Text}\"");
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Write the proof**

`src/DigitalBrainConsole/Program.cs`:

```csharp
using DigitalBrain;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Journals;
using DigitalBrain.Client;
using DigitalBrainConsole;

await using IDigitalBrain brain = await Brain.CreateAsync(args);

var chat = brain.Get<IChatNeuron>("main");

// Fire twice: the second fire must potentiate the same synapses, not add new ones.
await chat.FireAsync(new UserMessageReceived("hello"));
await chat.FireAsync(new UserMessageReceived("hello again"));

Console.WriteLine();
Console.WriteLine("-- synapses (anatomy) ------------------------------------------");
foreach (var synapse in await brain.GetSynapsesAsync(chat.Id))
{
    Console.WriteLine(synapse);
}

Console.WriteLine();
Console.WriteLine("-- chat:main outgoing journal (physiology) ---------------------");
var journal = await brain.ReadJournalAsync(chat.Id, JournalKind.Outgoing);
foreach (var delivery in journal.Delta)
{
    Console.WriteLine(
        $"#{delivery.Sequence}  {delivery.Signal.GetType().Name}  corr={delivery.CorrelationId}");
}
```

- [ ] **Step 5: Run it**

```bash
dotnet run --project src/DigitalBrainConsole
```

Expected output — the four claims of spec §13, made visible:

```
[greeter] handled UserMessageReceived("hello") -> "Hello!"
[logger]  recorded "hello"
[chat]    broadcast 5 chars -> 2 receivers
[greeter] handled UserMessageReceived("hello again") -> "Hello!"
[logger]  recorded "hello again"
[chat]    broadcast 11 chars -> 2 receivers

-- synapses (anatomy) ------------------------------------------
chat:owner/main --UserMessageReceived--> greeter:owner/default  w=0.76  fired=2  learned
chat:owner/main --UserMessageReceived--> logger:owner/default   w=0.76  fired=2  learned

-- chat:main outgoing journal (physiology) ---------------------
#1  UserMessageReceived  corr=…
#2  UserMessageReceived  corr=…
#3  UserMessageReceived  corr=…
#4  UserMessageReceived  corr=…
```

**Verify all four claims before continuing.** (1) The greeter and logger ran although `ChatNeuron` names neither. (2) Synapses print with source, target, signal type and weight. (3) `w=0.76` after two fires, not a constant — potentiation happened. (4) Two lists, two shapes: the graph has 2 rows, the journal has 4 entries.

If a weight reads `0.65`, the second fire created a second synapse instead of potentiating — check `SynapseSet.KeyFor`. If the graph is empty, `WriteStateAsync` is not being awaited in `BroadcastAsync`.

- [ ] **Step 6: Correct the spec's illustrative number**

In `docs/superpowers/specs/2026-09-02-digitalbrain-v2-neuron-substrate-design.md` §13, change both `w=0.72` occurrences to `w=0.76`, and the claim text `**w=0.72** after two fires` to `**w=0.76** after two fires`. The spec's 0.72 predates the ratified constants; 0.76 is what α=0.30 from an initial 0.50 produces.

- [ ] **Step 7: Full suite, then commit**

```bash
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --no-build --no-restore --verbosity minimal --max-parallel-test-modules 1
git add -A
git commit -m "feat: prove the weighted durable graph from the console

DigitalBrainConsole references module one and the silo, nothing else.
Two fires through a chat neuron that names no receiver reach a greeter
and a logger via the innate handler index, and print a synapse graph
whose weights moved from 0.50 to 0.76. Implements D6, slice 1 complete."
```

---

## Self-Review

**Spec coverage.** D1→T2. D2 — `Neuron<TState>` composition is **not** in this slice: no neuron here needs folded state, and introducing the generic before a consumer exists would be speculative. It lands in slice 2 with the script neuron. D3→T7. D4 — signals and internal events are separated in practice (synapse writes go to durable state, deliveries to the journal); broadcast plane and direct-call plane are untouched from v1. D5→T3. D6→T8. D7→T4/T5. D8, D9, D14, D15, D16, D18 — deferred by design, listed under "Deferred out of this slice". D10→T4 (the constructor guard). D11→T1. D12 — the activation pipeline has no consumer until slice 3's sensors; deferred. D13 — honoured: nothing compiles C# here. D17→T2/T3 (rename in place, `v2/` folder deleted).

**Placeholder scan.** No TBD, no "add error handling", no "similar to Task N". Every code step carries complete code. Two forward references are explicit and bounded: `SignalRouter` has a two-tier body with tier 3 named as slice 4, and `SynapseOptions.InitialDiscoveredWeight` is defined but unused until then — both stated in the task text rather than left implicit.

**Type consistency.** `Synapse` ctor arity and order is identical in T4's implementation, T5's `SynapseSet.Record`, and T5's tests. `Potentiate(DateTimeOffset, double)`, `WeightAt(DateTimeOffset, TimeSpan)`, `IsPrunedAt(DateTimeOffset, TimeSpan, double)` are used with those exact signatures everywhere. `ReadSynapses()` returns `Task<IReadOnlyList<Synapse>>` in the contract (T5), the session proxy (T7), and both call sites. `BroadcastAsync` returns `Task<int>` in T6's implementation and T6/T8's callers. `Brain.CreateAsync` returns `Task<IDigitalBrain>` in T8's definition and its one caller.

**Arithmetic check.** 0.50 → +0.30(0.50) = 0.65 → +0.30(0.35) = 0.755. Tests assert 0.65 and 0.755 at precision 10; the console prints `F2` = `0.76`. Consistent.
