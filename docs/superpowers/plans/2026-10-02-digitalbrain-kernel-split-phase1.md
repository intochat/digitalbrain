# DigitalBrain / Kernel Split — Phase 1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Lock the pure `src/DigitalBrain` model package in place — purity enforced by test, model behavior pinned by facts, the ABI seam documented in code — without changing anything modules compile against.

**Architecture:** The pure package already exists (committed). Phase 1 adds a dependency-free test project proving the package's laws, and `// ABI-bound` seam notes in `DigitalBrain.Contracts`. The planned `Contracts → DigitalBrain` project reference is **deliberately deferred**: the pure namespace is `DigitalBrain`, and C# resolves enclosing namespaces before `using` directives, so the transitive reference would silently rebind `Signal`/`INeuron`/`INeuronObserver` in every module type declared under `namespace DigitalBrain.*` — worst case `record Foo : Signal` rebasing onto the attribute-free pure `Signal` and breaking Orleans serialization at runtime. The reference edge therefore lands together with Signal unification in the migration phase. Task 3 records this in the spec.

**Tech Stack:** .NET / xunit v3 (repo style: facts with sentence-shaped names, `TestContext.Current.CancellationToken` where async — not needed here, all facts are synchronous).

**Spec:** `docs/superpowers/specs/2026-10-02-digitalbrain-kernel-split-design.md`

## Global Constraints

- `src/DigitalBrain/DigitalBrain.csproj` must have zero `PackageReference`, `ProjectReference`, and `FrameworkReference` items (spec: "It has no dependencies, and a test enforces that").
- Never build the `.slnx` (Windows handshake bug); build/test per project: `dotnet test src/<path>`.
- No module-visible type, name, or signature changes in phase 1 ("modules keep compiling untouched").
- No boilerplate `/// <summary>`; comments only for what code cannot say.
- Commit messages end with `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`.

## Review Focus

- A test project referencing the pure package *and* `DigitalBrain.Contracts` under a `DigitalBrain.*` namespace hits the shadowing hazard itself — Task 1's test project must reference ONLY the model package (plus xunit), and Task 1 Step 6 asserts that.
- `NeuronId` default value: `new NeuronId()` leaves `Id` null; `ToString()` must not throw and `Publisher` of a default `Signal` must behave (Task 1 pins `default(NeuronId).ToString()` and default-`Signal` equality).
- `Signal` is a non-sealed record: derived records must get value equality including `Publisher` (Task 1 pins equality for a derived record).
- The purity test must fail when a reference is *added*, not only pass today — Task 1 Step 3 verifies the test actually reads the csproj on disk by asserting item counts, and Step 2 runs it red first against a wrong path to prove it can fail.
- Future editors deleting the "do not add a ProjectReference to src/DigitalBrain" warning in `Contracts` — Task 2 places the note in `INeuron.cs`, `Signal.cs`, and the csproj itself, so the warning sits where the edit would happen.

---

### Task 1: Model test project with purity and law facts

**Files:**
- Create: `src/DigitalBrain.Tests/DigitalBrain.Tests.csproj`
- Create: `src/DigitalBrain.Tests/ModelFacts.cs`

**Interfaces:**
- Consumes: the pure package `src/DigitalBrain` (types `Signal`, `NeuronId`).
- Produces: nothing later tasks call; this task's deliverable is the enforced invariant.

- [ ] **Step 1: Create the test project**

`src/DigitalBrain.Tests/DigitalBrain.Tests.csproj` — mirror the xunit package set from `src/Modules/DigitalBrain/Kernel/DigitalBrain.Core.Tests.Unit/DigitalBrain.Core.Tests.Unit.csproj` (open it and copy the exact xunit/test-sdk `PackageReference` lines and any shared test props import), with exactly one ProjectReference:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <!-- xunit + test-sdk PackageReference lines copied verbatim from DigitalBrain.Core.Tests.Unit -->
    <ProjectReference Include="../DigitalBrain/DigitalBrain.csproj" />
  </ItemGroup>
</Project>
```

The root namespace defaults to `DigitalBrain.Tests` — that is fine: its enclosing namespace chain reaches `DigitalBrain`, which is exactly the pure package, and it references nothing else, so no shadowing is possible.

- [ ] **Step 2: Write the facts, run red first**

`src/DigitalBrain.Tests/ModelFacts.cs`:

```csharp
using System.Xml.Linq;

namespace DigitalBrain.Tests;

public class ModelFacts
{
    [Fact]
    public void The_model_package_references_nothing()
    {
        var csproj = XDocument.Load(ModelProjectPath());
        Assert.Empty(csproj.Descendants("PackageReference"));
        Assert.Empty(csproj.Descendants("ProjectReference"));
        Assert.Empty(csproj.Descendants("FrameworkReference"));
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

    private static string ModelProjectPath()
    {
        var directory = AppContext.BaseDirectory;
        while (!File.Exists(Path.Combine(directory, "DigitalBrain", "DigitalBrain.csproj")))
        {
            directory = Path.GetDirectoryName(directory)
                ?? throw new InvalidOperationException("src root with DigitalBrain/DigitalBrain.csproj not found above " + AppContext.BaseDirectory);
        }
        return Path.Combine(directory, "DigitalBrain", "DigitalBrain.csproj");
    }
}
```

First prove the purity fact *can* fail: temporarily change `"DigitalBrain.csproj"` in `ModelProjectPath` to `"DigitalBrain.Tests.csproj"` (which has references) — pointing the walk at this test project's own csproj.

Run: `dotnet test src/DigitalBrain.Tests`
Expected: `The_model_package_references_nothing` FAILS (ProjectReference present); other facts PASS.

- [ ] **Step 3: Restore the real path, run green**

Revert the filename to `"DigitalBrain.csproj"`.

Run: `dotnet test src/DigitalBrain.Tests`
Expected: all 4 facts PASS.

- [ ] **Step 4: Confirm note on default NeuronId behavior**

`default(NeuronId).ToString()` returning null is pinned deliberately: the model treats an unstamped publisher as "born outside a neuron" (see `Signal.cs` comment). If Step 3 instead shows it throwing or returning "", stop — that is a model decision, not a test fix; surface it to your human partner.

- [ ] **Step 5: Assert the test project itself cannot shadow**

Open `src/DigitalBrain.Tests/DigitalBrain.Tests.csproj` and confirm the only `ProjectReference` is `../DigitalBrain/DigitalBrain.csproj` and there is no reference to `DigitalBrain.Contracts` or any module. This is a review step, not a code change; the Review Focus explains why it matters.

- [ ] **Step 6: Commit**

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

- [ ] **Step 1: Annotate INeuron.cs**

Add above the existing `public interface INeuron` declaration (keep every existing member and comment untouched):

```csharp
// ABI-bound, see docs/superpowers/specs/2026-10-02-digitalbrain-kernel-split-design.md:
// this is the Orleans binding of the pure model in src/DigitalBrain (there: Watch returns the
// ISynapse and observers receive the pure Signal). Re-deriving from the pure interfaces waits
// for Signal unification — and do NOT add a ProjectReference to src/DigitalBrain before then:
// the pure namespace is DigitalBrain, and enclosing-namespace lookup would silently rebind
// Signal/INeuron in every module type declared under a DigitalBrain.* namespace.
```

- [ ] **Step 2: Annotate Signal.cs**

Add above `public record Signal`:

```csharp
// ABI-bound, see docs/superpowers/specs/2026-10-02-digitalbrain-kernel-split-design.md: the
// pure model's Signal (src/DigitalBrain) carries Publisher as a NeuronId value; this one keeps
// the string the whole repo compares against. Unifying the two is the keystone of the module
// migration phase — thirteen call sites compare Publisher to raw strings today.
```

- [ ] **Step 3: Annotate the csproj**

Add inside the first `<ItemGroup>` of `DigitalBrain.Contracts.csproj`:

```xml
<!-- Do not reference src/DigitalBrain here until Signal unification (see the kernel-split
     spec): its DigitalBrain namespace shadows these contracts across module namespaces. -->
```

- [ ] **Step 4: Build Contracts and run the kernel unit suite — comments must change nothing**

Run: `dotnet build src/Modules/DigitalBrain/Kernel/DigitalBrain.Contracts`
Expected: Build succeeded, 0 warnings beyond baseline.

Run: `dotnet test src/Modules/DigitalBrain/Kernel/DigitalBrain.Core.Tests.Unit`
Expected: all facts PASS (same count as a pre-change run; record the count before editing).

- [ ] **Step 5: Commit**

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
