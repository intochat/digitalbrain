# Plan 1 — Extract the `DigitalBrain.Protocol` Seam Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Carve the irreducible neuron/synapse "seam" out of `DigitalBrain.Core` into a new leaf project `DigitalBrain.Protocol` that depends on nothing but Orleans, while keeping the whole solution building and every test green.

**Architecture:** This is the first of seven milestones from `docs/superpowers/specs/2026-06-17-multirepo-distribution-design.md`. It does NOT split git repos yet — it makes the project dependency graph a clean DAG so a later plan can. The technique that keeps every step green: **move files into the new project but keep their existing `DigitalBrain.Core.*` namespaces** (in C# a namespace is independent of its assembly), so no consumer code changes. `Core` then references `Protocol`.

**Tech Stack:** .NET 11 (`net11.0`, SDK `11.0.100-preview.4`), Microsoft.Orleans 10 (`[GenerateSerializer]`/`[Id]` codegen), `.slnx` solution, Reqnroll test harness in `DigitalBrain.Core.Tests`, xUnit-style asserts.

**Scope of Protocol (this plan):** the seam only — Identity ids + `SynapseMetadata` + `RoutingMode` + `BrainScope` (all in `Ids.cs`), `Synapse` base, `INeuron`/`IHandle`/`IEmit`, the marketplace **public contract** value objects + install synapse vocabulary, and the `IAspire` capability interface. Everything else (Ino, Yaml, Orleans runtime, UI, State) stays in Core for now and moves in Plans 2–3.

---

## File Structure (created/modified in this plan)

- **Create** `src/DigitalBrain.Protocol/DigitalBrain.Protocol.csproj` — the leaf project (Orleans deps only).
- **Create** `src/DigitalBrain.Protocol.Tests/DigitalBrain.Protocol.Tests.csproj` — proves Protocol is self-contained (references *only* Protocol).
- **Create** `src/DigitalBrain.Protocol.Tests/ProtocolLeafTests.cs` — leaf-self-containment + serialization round-trip guard.
- **Move** (Core → Protocol, namespaces unchanged): `Domain/ValueObjects/Identity/Ids.cs`, `Domain/Events/Synapse.cs`, `INeuron.cs`, `Domain/Events/Distribution.cs`, `Domain/Events/InstallBundle.cs`, `Domain/Events/BundleInstalled.cs`, `Domain/Events/BundlePublished.cs`, `Domain/ValueObjects/Distribution/ExperiencePackage.cs`.
- **Move** (Sdk → Protocol, namespace unchanged): `Microsoft/Aspire/IAspire.cs` + the four Aspire synapse records it references (`StartDistributedApp.cs`, `DistributedAppStarted.cs`, `RestartResource.cs`, `ResourceRestarted.cs`).
- **Modify** `src/DigitalBrain.Core/DigitalBrain.Core.csproj` — add `ProjectReference` to Protocol.
- **Modify** `src/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj` — add `ProjectReference` to Protocol (for IAspire move).
- **Modify** `DigitalBrain.slnx` — add both new projects under a new `/Protocol/` folder.

Every Task ends green: `dotnet build DigitalBrain.slnx` succeeds and the Core.Tests suite passes.

---

## Task 0: Baseline — capture current green state

**Files:** none (verification only).

- [ ] **Step 1: Build the solution**

Run: `dotnet build DigitalBrain.slnx -c Debug`
Expected: `Build succeeded`. If it fails, STOP — fix the pre-existing break or report; do not start the extraction on a red baseline.

- [ ] **Step 2: Run the test suite and record the pass count**

Run: `dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj -c Debug`
Expected: all tests pass. Note the total passed count — this is the regression bar for every later task.

- [ ] **Step 3: Commit a clean marker (optional, no code change)**

```bash
git add -A && git commit -m "chore: baseline before protocol extraction" --allow-empty
```

---

## Task 1: Create the empty `DigitalBrain.Protocol` leaf project

**Files:**
- Create: `src/DigitalBrain.Protocol/DigitalBrain.Protocol.csproj`
- Modify: `DigitalBrain.slnx`

- [ ] **Step 1: Write the project file**

Create `src/DigitalBrain.Protocol/DigitalBrain.Protocol.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>preview</LangVersion>
    <NoWarn>$(NoWarn);ORLEANSEXP005</NoWarn>
    <IsPackable>true</IsPackable>
    <PackageId>DigitalBrain.Protocol</PackageId>
    <GeneratePackageOnBuild>false</GeneratePackageOnBuild>
    <Version>0.1.0-preview</Version>
    <Description>DigitalBrain protocol seam: synapse base, metadata, INeuron/IHandle/IEmit, marketplace public contracts, IAspire capability. Leaf — Orleans only.</Description>
    <PackageTags>orleans;digitalbrain;protocol;contracts</PackageTags>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Orleans.Sdk" />
    <PackageReference Include="Microsoft.Orleans.Core.Abstractions" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Register the project in the solution**

In `DigitalBrain.slnx`, add a new folder + project entry. Insert after the `/Contracts/` folder block (around line 17):

```xml
  <Folder Name="/Protocol/">
    <Project Path="src/DigitalBrain.Protocol/DigitalBrain.Protocol.csproj" />
  </Folder>
```

- [ ] **Step 3: Build the new project alone to verify it compiles empty**

Run: `dotnet build src/DigitalBrain.Protocol/DigitalBrain.Protocol.csproj -c Debug`
Expected: `Build succeeded` (empty project, 0 warnings related to it).

- [ ] **Step 4: Build the full solution to confirm nothing broke**

Run: `dotnet build DigitalBrain.slnx -c Debug`
Expected: `Build succeeded`.

- [ ] **Step 5: Commit**

```bash
git add src/DigitalBrain.Protocol/DigitalBrain.Protocol.csproj DigitalBrain.slnx
git commit -m "feat(protocol): add empty DigitalBrain.Protocol leaf project"
```

---

## Task 2: Add the leaf-self-containment guard test (fails first)

**Files:**
- Create: `src/DigitalBrain.Protocol.Tests/DigitalBrain.Protocol.Tests.csproj`
- Create: `src/DigitalBrain.Protocol.Tests/ProtocolLeafTests.cs`
- Modify: `DigitalBrain.slnx`

This test references **only** Protocol. It uses the seam types directly, so it will not compile until Tasks 3–7 have moved them in. That compile-red is the failing-test state for this refactor; it goes green as the seam lands.

- [ ] **Step 1: Write the test project file**

Create `src/DigitalBrain.Protocol.Tests/DigitalBrain.Protocol.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>preview</LangVersion>
    <IsPackable>false</IsPackable>
    <!-- Match Core.Tests harness: xUnit v3 standalone exe under Microsoft Testing Platform. -->
    <OutputType>Exe</OutputType>
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
    <IsTestProject>true</IsTestProject>
    <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
    <NoWarn>$(NoWarn);ORLEANSEXP005</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.v3" />
    <!-- Orleans.Sdk runs serialization codegen IN this test assembly so the
         test-local [GenerateSerializer] Ping below is serializable; it also
         provides the AddSerializer()/Serializer API transitively. -->
    <PackageReference Include="Microsoft.Orleans.Sdk" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\DigitalBrain.Protocol\DigitalBrain.Protocol.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

> Note: `xunit.v3`, `Microsoft.NET.Test.Sdk`, and `Microsoft.Orleans.Sdk` are already pinned in `Directory.Packages.props` (Core.Tests uses them) — no central-props edit needed. Do NOT use the v2 `xunit` package; it is not in central props.

- [ ] **Step 2: Write the guard test**

Create `src/DigitalBrain.Protocol.Tests/ProtocolLeafTests.cs`:

```csharp
using System.Linq;
using System.Reflection;
using DigitalBrain.Core;
using DigitalBrain.Core.Domain.Events;
using DigitalBrain.Core.Domain.ValueObjects.Identity;
using Orleans; // [GenerateSerializer], [Id]
using Orleans.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Protocol.Tests;

// A minimal synapse defined IN the test asm proves the base type + metadata
// stamping are fully available from Protocol alone. [GenerateSerializer] +
// the Orleans.Sdk ref make it round-trippable in this assembly.
[GenerateSerializer]
public sealed record Ping([property: Id(0)] string Text) : Synapse;

public class ProtocolLeafTests
{
    [Fact]
    public void Protocol_assembly_does_not_reference_Core_or_Sdk_assemblies()
    {
        var protocolAsm = typeof(Synapse).Assembly;
        Assert.Equal("DigitalBrain.Protocol", protocolAsm.GetName().Name);

        var referenced = protocolAsm.GetReferencedAssemblies().Select(a => a.Name).ToArray();
        Assert.DoesNotContain("DigitalBrain.Core", referenced);
        Assert.DoesNotContain("DigitalBrain.Sdk", referenced);
    }

    [Fact]
    public void Synapse_stamp_threads_correlation_and_caller()
    {
        var firing = new NeuronId("DigitalBrain.Core.INeuron", "ping-1");
        var stamped = new Ping("hi").Stamp(firing);

        Assert.NotEqual(default, stamped.CorrelationId);
        Assert.Equal(firing.Type, stamped.Metadata.Caller.Type);
        Assert.Equal(BrainScope.LocalPrivate, stamped.Scope);
    }

    [Fact]
    public void Synapse_round_trips_through_orleans_serializer()
    {
        var services = new ServiceCollection().AddSerializer().BuildServiceProvider();
        var serializer = services.GetRequiredService<Serializer>();

        var original = new Ping("payload").Stamp(new NeuronId("t", "k"));
        var bytes = serializer.SerializeToArray((Synapse)original);
        var restored = (Ping)serializer.Deserialize<Synapse>(bytes);

        Assert.Equal("payload", restored.Text);
        Assert.Equal(original.CorrelationId, restored.CorrelationId);
    }
}
```

- [ ] **Step 3: Register the test project in the solution**

In `DigitalBrain.slnx`, add to the existing `/Tests/` folder:

```xml
    <Project Path="src/DigitalBrain.Protocol.Tests/DigitalBrain.Protocol.Tests.csproj" />
```

- [ ] **Step 4: Run the test project to confirm it fails to COMPILE (the red state)**

Run: `dotnet build src/DigitalBrain.Protocol.Tests/DigitalBrain.Protocol.Tests.csproj -c Debug`
Expected: FAIL — compile errors `The type or namespace name 'Synapse'/'NeuronId'/'BrainScope' could not be found` (because the seam types still live in Core, not Protocol). This confirms the guard is meaningful.

- [ ] **Step 5: Commit the failing guard**

```bash
git add src/DigitalBrain.Protocol.Tests DigitalBrain.slnx Directory.Packages.props
git commit -m "test(protocol): add leaf-self-containment guard (red until seam moves)"
```

---

## Task 3: Move the identity/value foundation (`Ids.cs`) into Protocol

`Ids.cs` defines `SynapseId`, `CorrelationId`, `CausationId`, `NeuronId`, `SynapseMetadata`, `RoutingMode`, `BrainScope` — the base everything else needs. It references `INeuron` (in `NeuronId.For<TNeuron>`), so this task also wires the Core→Protocol reference and provisionally brings `INeuron` along is NOT needed yet: `NeuronId.For<TNeuron>` only needs the `INeuron` *constraint*. To keep this task self-contained, move `INeuron.cs` together with `Ids.cs` (they are mutually referencing seam types).

**Files:**
- Move: `src/DigitalBrain.Core/Domain/ValueObjects/Identity/Ids.cs` → `src/DigitalBrain.Protocol/Domain/ValueObjects/Identity/Ids.cs`
- Move: `src/DigitalBrain.Core/INeuron.cs` → `src/DigitalBrain.Protocol/INeuron.cs`
- Modify: `src/DigitalBrain.Core/DigitalBrain.Core.csproj`

- [ ] **Step 1: Move the two files with git (namespaces stay `DigitalBrain.Core.*` and `DigitalBrain.Core`)**

```bash
mkdir -p src/DigitalBrain.Protocol/Domain/ValueObjects/Identity
git mv src/DigitalBrain.Core/Domain/ValueObjects/Identity/Ids.cs src/DigitalBrain.Protocol/Domain/ValueObjects/Identity/Ids.cs
git mv src/DigitalBrain.Core/INeuron.cs src/DigitalBrain.Protocol/INeuron.cs
```

Do NOT edit the namespace lines — they remain `namespace DigitalBrain.Core.Domain.ValueObjects.Identity;` and `namespace DigitalBrain.Core;`. This is what keeps consumers unchanged.

- [ ] **Step 2: Add the Protocol reference to Core**

In `src/DigitalBrain.Core/DigitalBrain.Core.csproj`, add inside a new `<ItemGroup>`:

```xml
  <ItemGroup>
    <ProjectReference Include="..\DigitalBrain.Protocol\DigitalBrain.Protocol.csproj" />
  </ItemGroup>
```

- [ ] **Step 3: Build the solution**

Run: `dotnet build DigitalBrain.slnx -c Debug`
Expected: `Build succeeded`. Synapse.cs (still in Core) resolves `SynapseMetadata`/`NeuronId` from Protocol via the project reference; consumers are unchanged because namespaces are identical.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj -c Debug`
Expected: same pass count as Task 0 Step 2.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(protocol): move Ids + INeuron seam into Protocol (namespaces unchanged)"
```

---

## Task 4: Move the `Synapse` base record into Protocol

**Files:**
- Move: `src/DigitalBrain.Core/Domain/Events/Synapse.cs` → `src/DigitalBrain.Protocol/Domain/Events/Synapse.cs`

- [ ] **Step 1: Move the file**

```bash
mkdir -p src/DigitalBrain.Protocol/Domain/Events
git mv src/DigitalBrain.Core/Domain/Events/Synapse.cs src/DigitalBrain.Protocol/Domain/Events/Synapse.cs
```

Namespace stays `namespace DigitalBrain.Core.Domain.Events;`. Its `using DigitalBrain.Core.Domain.ValueObjects.Identity;` now resolves within Protocol itself.

- [ ] **Step 2: Build the solution**

Run: `dotnet build DigitalBrain.slnx -c Debug`
Expected: `Build succeeded`.

- [ ] **Step 3: Run the test suite**

Run: `dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj -c Debug`
Expected: same pass count as baseline.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor(protocol): move Synapse base record into Protocol"
```

---

## Task 5: Move the marketplace public-contract value objects + install vocabulary

These are the synapse/value types the spec puts in the protocol seam: the `.brain` manifest model and the install/publish synapses. They depend only on already-moved seam types.

**Files:**
- Move: `src/DigitalBrain.Core/Domain/ValueObjects/Distribution/ExperiencePackage.cs` → `src/DigitalBrain.Protocol/Domain/ValueObjects/Distribution/ExperiencePackage.cs`
- Move: `src/DigitalBrain.Core/Domain/Events/Distribution.cs` → `src/DigitalBrain.Protocol/Domain/Events/Distribution.cs`
- Move: `src/DigitalBrain.Core/Domain/Events/InstallBundle.cs` → `src/DigitalBrain.Protocol/Domain/Events/InstallBundle.cs`
- Move: `src/DigitalBrain.Core/Domain/Events/BundleInstalled.cs` → `src/DigitalBrain.Protocol/Domain/Events/BundleInstalled.cs`
- Move: `src/DigitalBrain.Core/Domain/Events/BundlePublished.cs` → `src/DigitalBrain.Protocol/Domain/Events/BundlePublished.cs`

- [ ] **Step 1: Confirm these files have no dependency on Core runtime types**

Run: `grep -nE "using DigitalBrain.Core.(Application|Infrastructure|State|UI|Domain.Ino|Domain.Yaml)" src/DigitalBrain.Core/Domain/Events/Distribution.cs src/DigitalBrain.Core/Domain/Events/InstallBundle.cs src/DigitalBrain.Core/Domain/Events/BundleInstalled.cs src/DigitalBrain.Core/Domain/Events/BundlePublished.cs src/DigitalBrain.Core/Domain/ValueObjects/Distribution/ExperiencePackage.cs`
Expected: no output. If any line appears, that file is NOT a pure contract — leave it in Core and note it in the plan's deviations log; move only the clean ones.

- [ ] **Step 2: Move the clean files**

```bash
mkdir -p src/DigitalBrain.Protocol/Domain/ValueObjects/Distribution
git mv src/DigitalBrain.Core/Domain/ValueObjects/Distribution/ExperiencePackage.cs src/DigitalBrain.Protocol/Domain/ValueObjects/Distribution/ExperiencePackage.cs
git mv src/DigitalBrain.Core/Domain/Events/Distribution.cs src/DigitalBrain.Protocol/Domain/Events/Distribution.cs
git mv src/DigitalBrain.Core/Domain/Events/InstallBundle.cs src/DigitalBrain.Protocol/Domain/Events/InstallBundle.cs
git mv src/DigitalBrain.Core/Domain/Events/BundleInstalled.cs src/DigitalBrain.Protocol/Domain/Events/BundleInstalled.cs
git mv src/DigitalBrain.Core/Domain/Events/BundlePublished.cs src/DigitalBrain.Protocol/Domain/Events/BundlePublished.cs
```

(`BundleId` referenced by InstallBundle/BundleInstalled lives in `Ids.cs`, already in Protocol — verify with `grep -n "BundleId" src/DigitalBrain.Protocol/Domain/ValueObjects/Identity/Ids.cs`; if it is defined elsewhere in Core, move that definition too in this step.)

- [ ] **Step 3: Build the solution**

Run: `dotnet build DigitalBrain.slnx -c Debug`
Expected: `Build succeeded`.

- [ ] **Step 4: Run the test suite**

Run: `dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj -c Debug`
Expected: same pass count as baseline.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(protocol): move marketplace public contracts + install vocabulary"
```

---

## Task 6: Move the `IAspire` capability interface (Sdk → Protocol)

Per the design, the `IAspire` *interface* lives in protocol so `os` can implement it without depending on `apps`. Move the interface and the four synapse records it references.

**Files:**
- Move: `src/DigitalBrain.Sdk/Microsoft/Aspire/IAspire.cs` → `src/DigitalBrain.Protocol/Microsoft/Aspire/IAspire.cs`
- Move: `src/DigitalBrain.Sdk/Microsoft/Aspire/StartDistributedApp.cs`, `DistributedAppStarted.cs`, `RestartResource.cs`, `ResourceRestarted.cs` → `src/DigitalBrain.Protocol/Microsoft/Aspire/`
- Modify: `src/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj`

- [ ] **Step 1: Move the five files (namespace stays `DigitalBrain.Sdk.Microsoft.Aspire`)**

```bash
mkdir -p src/DigitalBrain.Protocol/Microsoft/Aspire
git mv src/DigitalBrain.Sdk/Microsoft/Aspire/IAspire.cs src/DigitalBrain.Protocol/Microsoft/Aspire/IAspire.cs
git mv src/DigitalBrain.Sdk/Microsoft/Aspire/StartDistributedApp.cs src/DigitalBrain.Protocol/Microsoft/Aspire/StartDistributedApp.cs
git mv src/DigitalBrain.Sdk/Microsoft/Aspire/DistributedAppStarted.cs src/DigitalBrain.Protocol/Microsoft/Aspire/DistributedAppStarted.cs
git mv src/DigitalBrain.Sdk/Microsoft/Aspire/RestartResource.cs src/DigitalBrain.Protocol/Microsoft/Aspire/RestartResource.cs
git mv src/DigitalBrain.Sdk/Microsoft/Aspire/ResourceRestarted.cs src/DigitalBrain.Protocol/Microsoft/Aspire/ResourceRestarted.cs
```

`IAspire.cs` uses `using DigitalBrain.Core;` and `using DigitalBrain.Core.Application;`. The `DigitalBrain.Core` (INeuron/IHandle/IEmit) part now resolves inside Protocol. The `DigitalBrain.Core.Application` using is for nothing IAspire actually needs once moved — verify:

- [ ] **Step 2: Confirm IAspire has no real dependency on `DigitalBrain.Core.Application`**

Run: `grep -nE "Application\.|IMarketplace|IPackager|IUiNeuron|IDigitalBrain|IAgent" src/DigitalBrain.Protocol/Microsoft/Aspire/IAspire.cs`
Expected: no output. If empty, delete the now-unused `using DigitalBrain.Core.Application;` line from `IAspire.cs`. If it has output, STOP — IAspire is not a pure contract; leave it in Sdk and record the deviation.

- [ ] **Step 3: Ensure Sdk still references Protocol**

In `src/DigitalBrain.Sdk/DigitalBrain.Sdk.csproj`, the existing `ProjectReference` to Core transitively provides Protocol, but add a direct reference for clarity:

```xml
    <ProjectReference Include="..\DigitalBrain.Protocol\DigitalBrain.Protocol.csproj" />
```

- [ ] **Step 4: Build the solution**

Run: `dotnet build DigitalBrain.slnx -c Debug`
Expected: `Build succeeded`. Any neuron implementing `IAspire` (in Kernel/AppHost) compiles unchanged because the namespace is identical.

- [ ] **Step 5: Run the test suite**

Run: `dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj -c Debug`
Expected: same pass count as baseline.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(protocol): move IAspire capability interface + aspire synapses into Protocol"
```

---

## Task 7: Turn the leaf guard green and lock the boundary

**Files:**
- Modify (if needed): `src/DigitalBrain.Protocol.Tests/ProtocolLeafTests.cs`

- [ ] **Step 1: Build the Protocol.Tests project — it should now COMPILE**

Run: `dotnet build src/DigitalBrain.Protocol.Tests/DigitalBrain.Protocol.Tests.csproj -c Debug`
Expected: `Build succeeded` (all seam types now resolve from Protocol alone).

- [ ] **Step 2: Run the Protocol guard tests**

Run: `dotnet test src/DigitalBrain.Protocol.Tests/DigitalBrain.Protocol.Tests.csproj -c Debug`
Expected: PASS — all three tests, including `Protocol_assembly_does_not_reference_Core_or_Sdk_assemblies`.

- [ ] **Step 3: Run the full suite one more time**

Run: `dotnet test DigitalBrain.slnx -c Debug`
Expected: Core.Tests at baseline pass count + Protocol.Tests (3) all green.

- [ ] **Step 4: Verify Core no longer physically owns the seam files**

Run: `ls src/DigitalBrain.Core/Domain/Events/Synapse.cs src/DigitalBrain.Core/INeuron.cs 2>&1`
Expected: "No such file" for both — confirms the move, not a copy.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "test(protocol): leaf-self-containment guard green; seam boundary locked"
```

---

## Done-when (this plan)

- `DigitalBrain.Protocol` exists, references only Orleans, and owns: `Ids.cs` (ids + metadata + RoutingMode + BrainScope), `Synapse`, `INeuron`/`IHandle`/`IEmit`, the marketplace public contracts + install vocabulary, and `IAspire`.
- `DigitalBrain.Core` references Protocol; all consumers compile unchanged (namespaces preserved).
- `ProtocolLeafTests` proves Protocol does not reference Core or Sdk and that synapses round-trip via Orleans serialization.
- Full solution builds; Core.Tests at baseline pass count.

## Deviations log (fill during execution)

- (record any file that could not be moved cleanly and why)

---

## Roadmap — subsequent plans (each its own doc, written when reached)

These are **not** detailed here; they are the remaining milestones from the spec, in dependency order. Each becomes its own `docs/superpowers/plans/...` file via the writing-plans skill when we start it.

- **Plan 2 — Extract `inolang`:** move `Domain/Ino/*`, `Domain/Yaml/*`, and `DigitalBrain.SourceGen` into a new `DigitalBrain.InoLang` project depending on Protocol; add interpreter/parser guard tests; keep solution green.
- **Plan 3 — Re-home `os` / dissolve Core:** move `Infrastructure/Orleans/*`, `State`, `UI`, `SimulationCatalog`, `SurfaceFanout`, the `Application` neuron interfaces, Kernel/AppHost/Aspire.Hosting into the os surface; delete the empty `DigitalBrain.Core` and its stale `DigitalBrain.Contracts` PackageId. Move `DistributionDynamicHandlers.feature` to os.
- **Plan 4 — Form `apps`:** rename `Connectors` → `DigitalBrain.Sdk` (connectors + author helpers + IAspire wrapper); group Awesome, Ino/Experiences, `os/*.ino`, `os-on-yaml/*.yaml`, Clients; clean DAG `apps → Sdk → protocol`.
- **Plan 5 — Workspace meta-repo + git split:** create `digitalbrain-workspace` with the four repos as submodules, root `DigitalBrain.slnx`, and the `UseLocalSources` ProjectReference↔PackageReference switch (PackageReference path deferred).
- **Plan 6 — Local marketplace registry:** add the file/endpoint registry Aspire resource serving manifest-declared bundles (interpreted `.ino`/`.yaml` + compiled assembly), wired into the os AppHost.
- **Plan 7 — Cross-process distribution proof:** extend the distribution feature to prove N+1 from the local registry, across a separate Aspire-orchestrated silo, for both bundle kinds.
