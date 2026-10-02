# DigitalBrain / Kernel Split — Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lock the pure `src/DigitalBrain` model package in place — purity enforced by test, model behavior pinned by facts, the ABI seam documented in code — without changing anything modules compile against.

**Architecture:** The pure package already exists (committed). Phase 1 adds a dependency-free test project proving the package's laws, and `// ABI-bound` seam notes in `DigitalBrain.Contracts`. The planned `Contracts → DigitalBrain` project reference is **deliberately deferred**: the pure namespace is `DigitalBrain`, and C# resolves enclosing namespaces before `using` directives, so the transitive reference would silently rebind `Signal`/`INeuron`/`INeuronObserver` in every module type declared under `namespace DigitalBrain.*` — worst case `record Foo : Signal` rebasing onto the attribute-free pure `Signal` and breaking Orleans serialization at runtime. The reference edge therefore lands together with Signal unification in the migration phase. Task 3 records this in the spec.

**Tech Stack:** .NET / xunit (repo style: facts with sentence-shaped names) / ArchUnitNET (TNG — architecture facts over the compiled assembly, stronger than csproj parsing, and the future home of the spec's ring rules: Contracts never depends on Sdk, modules never touch Platform).

**Spec:** `docs/superpowers/specs/2026-10-02-digitalbrain-kernel-split-design.md`

**Scope — explicit:** Phase 1 touches the model package, its new test project, and comments/doc text in the kernel ring ONLY. No module is migrated, and the kernel/Sdk runtime is not yet rewired onto the pure types — that rewiring requires Signal unification (string `Publisher` → `NeuronId`), which is the migration phase's named first step. Nothing any module compiles against changes in this plan.

## Global Constraints

- `src/DigitalBrain/DigitalBrain.csproj` must have zero `PackageReference`, `ProjectReference`, and `FrameworkReference` items (spec: "It has no dependencies, and a test enforces that").
- Never build the `.slnx` (Windows handshake bug); build/test per project: `dotnet test src/<path>`.
- No module-visible type, name, or signature changes in phase 1 ("modules keep compiling untouched").
- No boilerplate `/// <summary>`; comments only for what code cannot say.
- Commit messages end with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

## Review Focus

- A test project referencing the pure package *and* `DigitalBrain.Contracts` under a `DigitalBrain.*` namespace hits the shadowing hazard itself — Task 1's test project must reference ONLY the model package (plus xunit and ArchUnitNET), and Task 1 Step 5 asserts that.
- `NeuronId` default value: `new NeuronId()` leaves `Id` null; `ToString()` must not throw and `Publisher` of a default `Signal` must behave (Task 1 pins `default(NeuronId).ToString()` and default-`Signal` equality).
- `Signal` is a non-sealed record: derived records must get value equality including `Publisher` (Task 1 pins equality for a derived record).
- The purity facts must fail when a dependency is *added*, not only pass today — Task 1 Step 2 runs the ArchUnitNET rule red first with a deliberately wrong namespace pattern to prove it can fail.
- Future editors deleting the "do not add a ProjectReference to src/DigitalBrain" warning in `Contracts` — Task 2 places the note in `INeuron.cs`, `Signal.cs`, and the csproj itself, so the warning sits where the edit would happen.

---

### Task 1: Model test project with purity and law facts

**Files:**
- Create: `src/DigitalBrain.Tests/DigitalBrain.Tests.csproj`
- Create: `src/DigitalBrain.Tests/ModelFacts.cs`

**Interfaces:**
- Consumes: the pure package `src/DigitalBrain` (types `Signal`, `NeuronId`).
- Produces: nothing later tasks call; this task's deliverable is the enforced invariant.

- [x] **Step 1: Create the test project**

`src/DigitalBrain.Tests/DigitalBrain.Tests.csproj` — mirror the xunit package set from `src/Modules/DigitalBrain/Kernel/DigitalBrain.Core.Tests.Unit/DigitalBrain.Core.Tests.Unit.csproj` (open it and copy the exact xunit/test-sdk `PackageReference` lines and any shared test props import), plus ArchUnitNET, with exactly one ProjectReference:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <!-- xunit + test-sdk PackageReference lines copied verbatim from DigitalBrain.Core.Tests.Unit -->
    <PackageReference Include="TngTech.ArchUnitNET.xUnit" Version="0.11.4" />
    <ProjectReference Include="../DigitalBrain/DigitalBrain.csproj" />
  </ItemGroup>
</Project>
```

Pick the xUnit-integration package matching the repo's xunit major: `TngTech.ArchUnitNET.xUnit` for xunit 2.x, `TngTech.ArchUnitNET.xUnitV3` for xunit 3.x (check which the copied package lines use), and use the latest stable version NuGet offers if newer than 0.11.4. If the repo uses central package management (check for `Directory.Packages.props` at the root), add the version there instead and keep the versionless `PackageReference` here.

The root namespace defaults to `DigitalBrain.Tests` — that is fine: its enclosing namespace chain reaches `DigitalBrain`, which is exactly the pure package, and it references nothing else, so no shadowing is possible.

- [x] **Step 2: Write the facts, run red first**

`src/DigitalBrain.Tests/ModelFacts.cs`:

```csharp
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;
using System.Reflection;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace DigitalBrain.Tests;

public class ModelFacts
{
    private static readonly Assembly Model = typeof(Signal).Assembly;

    private static readonly Architecture Architecture =
        new ArchLoader().LoadAssembly(Model).Build();

    [Fact]
    public void The_model_depends_on_nothing_but_the_runtime()
    {
        Types().Should()
            .OnlyDependOnTypesThat().ResideInNamespaceMatching("^(System|DigitalBrain)($|\\.)")
            .Because("the model defines meaning on any runtime; Orleans and every other host stay in the Kernel ring")
            .Check(Architecture);
    }

    [Fact]
    public void The_model_assembly_references_only_the_runtime()
    {
        // ArchUnitNET sees what types USE; this sees what the assembly LINKS — an unused
        // reference still widens what a future edit can reach without anyone noticing.
        Assert.All(Model.GetReferencedAssemblies(),
            reference => Assert.StartsWith("System", reference.Name));
    }

    [Fact]
    public void A_default_neuron_id_prints_without_throwing()
    {
        Assert.Null(default(NeuronId).ToString());
        Assert.Equal("button-1", new NeuronId("button-1").ToString());
    }

    [Fact]
    public void Neuron_ids_are_values()
    {
        Assert.Equal(new NeuronId("a"), new NeuronId("a"));
        Assert.NotEqual(new NeuronId("a"), new NeuronId("b"));
    }

    [Fact]
    public void Signals_are_values_including_their_publisher()
    {
        Assert.Equal(new Clicked { Publisher = new("b1") }, new Clicked { Publisher = new("b1") });
        Assert.NotEqual(new Clicked { Publisher = new("b1") }, new Clicked { Publisher = new("b2") });
        Assert.Equal(default(NeuronId), new Clicked().Publisher);
    }

    private sealed record Clicked : Signal;
}
```

First prove the architecture facts *can* fail: temporarily change the namespace pattern in
`The_model_depends_on_nothing_but_the_runtime` to `"^DigitalBrain($|\\.)"` (excluding `System`,
which every type uses).

Run: `dotnet test src/DigitalBrain.Tests`
Expected: `The_model_depends_on_nothing_but_the_runtime` FAILS listing the model's types; other facts PASS.

- [x] **Step 3: Restore the pattern, run green**

Revert to `"^(System|DigitalBrain)($|\\.)"`.

Run: `dotnet test src/DigitalBrain.Tests`
Expected: all 5 facts PASS. If `The_model_assembly_references_only_the_runtime` fails on a
non-System name the SDK injects (e.g. `netstandard` or `mscorlib` facades), widen the assertion
to the exact observed set — list the names explicitly rather than loosening to a prefix soup.

- [x] **Step 4: Confirm note on default NeuronId behavior**

`default(NeuronId).ToString()` returning null is pinned deliberately: the model treats an unstamped publisher as "born outside a neuron" (see `Signal.cs` comment). If Step 3 instead shows it throwing or returning "", stop — that is a model decision, not a test fix; surface it to your human partner.

- [x] **Step 5: Assert the test project itself cannot shadow**

Open `src/DigitalBrain.Tests/DigitalBrain.Tests.csproj` and confirm the only `ProjectReference` is `../DigitalBrain/DigitalBrain.csproj` and there is no reference to `DigitalBrain.Contracts` or any module (xunit/test-sdk/ArchUnitNET packages are fine). This is a review step, not a code change; the Review Focus explains why it matters.

- [x] **Step 6: Commit**

```bash
git add src/DigitalBrain.Tests
git commit -m "Pin the pure model's laws and enforce its zero references

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

### Task 2: ABI-bound seam notes in DigitalBrain.Contracts

**Files:**
- Modify: `src/Modules/DigitalBrain/Kernel/DigitalBrain.Contracts/INeuron.cs`
- Modify: `src/Modules/DigitalBrain/Kernel/DigitalBrain.Contracts/Signal.cs`
- Modify: `src/Modules/DigitalBrain/Kernel/DigitalBrain.Contracts/DigitalBrain.Contracts.csproj`

**Interfaces:**
- Consumes: nothing from Task 1.
- Produces: no API change — comments only. Later (migration-phase) tasks rely on these notes naming the seam.

- [x] **Step 1: Annotate INeuron.cs**

Add above the existing `public interface INeuron` declaration (keep every existing member and comment untouched):

```csharp
// ABI-bound, see docs/superpowers/specs/2026-10-02-digitalbrain-kernel-split-design.md:
// this is the Orleans binding of the pure model in src/DigitalBrain (there: Watch returns the
// ISynapse and observers receive the pure Signal). Re-deriving from the pure interfaces waits
// for Signal unification — and do NOT add a ProjectReference to src/DigitalBrain before then:
// the pure namespace is DigitalBrain, and enclosing-namespace lookup would silently rebind
// Signal/INeuron in every module type declared under a DigitalBrain.* namespace.
```

- [x] **Step 2: Annotate Signal.cs**

Add above `public record Signal`:

```csharp
// ABI-bound, see docs/superpowers/specs/2026-10-02-digitalbrain-kernel-split-design.md: the
// pure model's Signal (src/DigitalBrain) carries Publisher as a NeuronId value; this one keeps
// the string the whole repo compares against. Unifying the two is the keystone of the module
// migration phase — thirteen call sites compare Publisher to raw strings today.
```

- [x] **Step 3: Annotate the csproj**

Add inside the first `<ItemGroup>` of `DigitalBrain.Contracts.csproj`:

```xml
<!-- Do not reference src/DigitalBrain here until Signal unification (see the kernel-split
     spec): its DigitalBrain namespace shadows these contracts across module namespaces. -->
```

- [x] **Step 4: Build Contracts and run the kernel unit suite — comments must change nothing**

Run: `dotnet build src/Modules/DigitalBrain/Kernel/DigitalBrain.Contracts`
Expected: Build succeeded, 0 warnings beyond baseline.

Run: `dotnet test src/Modules/DigitalBrain/Kernel/DigitalBrain.Core.Tests.Unit`
Expected: all facts PASS (same count as a pre-change run; record the count before editing).

- [x] **Step 5: Commit**

```bash
git add src/Modules/DigitalBrain/Kernel/DigitalBrain.Contracts
git commit -m "Name the ABI seam and the namespace-shadowing hazard in Contracts

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

### Task 3: Record the deferral in the spec

**Files:**
- Modify: `docs/superpowers/specs/2026-10-02-digitalbrain-kernel-split-design.md` (Phase 1 section and Declared debt section)

**Interfaces:**
- Consumes: the discovery documented in Task 2's comments.
- Produces: the spec's phase-1 section matches what was actually shipped; migration phase inherits the Signal-unification keystone as its named first step.

- [ ] **Step 1: Amend the Phase 1 section**

In the spec, replace phase-1 item 2 (the sentence beginning "Existing `DigitalBrain.Contracts` takes the **role** of `Kernel.Contracts`: it references `DigitalBrain`…") with:

```markdown
2. Existing `DigitalBrain.Contracts` takes the **role** of `Kernel.Contracts`, but the project
   reference to `DigitalBrain` is deferred: the pure package's `DigitalBrain` namespace is
   resolved from enclosing namespaces *before* `using DigitalBrain.Contracts;`, so a transitive
   reference would silently rebind `Signal`/`INeuron`/`INeuronObserver` in every module type
   declared under a `DigitalBrain.*` namespace — worst case rebasing a module's signal record
   onto the attribute-free pure `Signal` and breaking Orleans serialization at runtime. The
   reference edge lands together with Signal unification in the migration phase; until then the
   seam is named by `// ABI-bound` notes in `INeuron.cs`, `Signal.cs` and the Contracts csproj.
```

- [ ] **Step 2: Add one line to Declared debt**

Append to the "Declared debt" section:

```markdown
Phase 1 additionally defers the `Contracts → DigitalBrain` reference itself (namespace
shadowing, see Phase 1 item 2); the migration phase's first step is Signal unification —
`Publisher` becomes `NeuronId` at its thirteen string-comparison call sites — which unlocks
that reference and the `IKernelNeuron`/`IKernelNeuronObserver` re-derivation.
```

- [ ] **Step 3: Commit**

```bash
git add docs/superpowers/specs/2026-10-02-digitalbrain-kernel-split-design.md
git commit -m "Record the phase-1 reference deferral and its namespace-shadowing cause

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```
