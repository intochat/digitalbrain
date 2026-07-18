# Plan 3 — Dissolve Core into Os (assembly + namespace alignment) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename the leftover Core runtime library to `DigitalBrain.Os`, and align every namespace to its owning assembly — `DigitalBrain.Protocol.*`, `DigitalBrain.InoLang.*`, `DigitalBrain.Os.*` — so there are no namespace/assembly mismatches and no namespaces shared across assemblies. Keep the solution building and all tests green at every task.

**Architecture:** Third milestone of `docs/superpowers/specs/2026-06-17-multirepo-distribution-design.md`. After Plans 1 & 2, `DigitalBrain.Core` IS the OS runtime (Orleans `Neuron` base grain, dispatch/stream/journaling, state, UI widgets, app-layer neuron interfaces, simulation, lifecycle synapses) — a shared library, not a host. This plan renames it to `DigitalBrain.Os` and finishes the namespace cleanup deferred by Plans 1 & 2 (which preserved `DigitalBrain.Core.*` namespaces to keep moves green). Does NOT split git repos (that is Plan 5). `DigitalBrain.SourceGen` is grouped under os but otherwise untouched.

**Tech Stack:** .NET 11, Orleans 10, `.slnx`, xUnit v3 / Microsoft Testing Platform, central package management. No new dependencies.

**Scale note:** This is a large, mostly-mechanical refactor (~60 consumer files touch these namespaces). It is broken into green-gated tasks: an isolated assembly rename, a safe blanket rename of the 8 single-assembly namespaces, then careful build-fix-loop splits of the 3 entangled namespaces, then test-project rename + regroup. Use a capable model for the entangled-split tasks (3 & 4).

---

## Pre-verified namespace → assembly matrix (codebase inspection 2026-06-17)

**Single-assembly namespaces (safe blanket prefix-swap — Task 2):**

| Old namespace | New namespace | Assembly |
|---|---|---|
| `DigitalBrain.Core.Domain.ValueObjects.Identity` | `DigitalBrain.Protocol.Domain.ValueObjects.Identity` | Protocol |
| `DigitalBrain.Core.Domain.ValueObjects.Distribution` | `DigitalBrain.Protocol.Domain.ValueObjects.Distribution` | Protocol |
| `DigitalBrain.Core.Domain.Ino` | `DigitalBrain.InoLang.Domain.Ino` | InoLang |
| `DigitalBrain.Core.Domain.Yaml` | `DigitalBrain.InoLang.Domain.Yaml` | InoLang |
| `DigitalBrain.Core.Application` | `DigitalBrain.Os.Application` | Os |
| `DigitalBrain.Core.Infrastructure.Orleans` | `DigitalBrain.Os.Infrastructure.Orleans` | Os |
| `DigitalBrain.Core.UI` | `DigitalBrain.Os.UI` | Os |
| `DigitalBrain.Core.State` | `DigitalBrain.Os.State` | Os |

**Entangled namespaces (split by file via build-fix loop — Tasks 3 & 4):**

- `DigitalBrain.Core.Domain.Events` — **Protocol** files (`Synapse`, `DynamicSynapse`, `Distribution`, `InstallBundle`, `BundleInstalled`, `BundlePublished`, `RuleTelemetry`) → `DigitalBrain.Protocol.Domain.Events`; **Os** files (`Activated`, `Agent`, `Agentic`, `Deactivated`, `Guide`, `HandlerReacted`, `KernelTask`, `NeuronTelemetry`, `SimulationSynapses`, `SynapseIncoming`, `SynapseOutgoing`) → `DigitalBrain.Os.Domain.Events`. (49 consumer usings.)
- Root `DigitalBrain.Core` — **Protocol**: `INeuron.cs` (`INeuron`/`IHandle`/`IEmit`) → `DigitalBrain.Protocol`; **Os**: `SimulationCatalog.cs`, `SurfaceFanout.cs`, and `ICluster.cs` (verify its namespace at execution) → `DigitalBrain.Os`. (27 consumer usings.)
- `DigitalBrain.Sdk.Microsoft.Aspire` — **Protocol** files (`IAspire`, `StartDistributedApp`, `DistributedAppStarted`, `RestartResource`, `ResourceRestarted`) → `DigitalBrain.Protocol.Microsoft.Aspire`; **Sdk** files (`Aspire.cs` impl, `DigitalBrainDomainResource.cs`, etc.) stay `DigitalBrain.Sdk.Microsoft.Aspire`. (5 consumer usings.)

**Untouched namespaces:** `DigitalBrain.Sdk.DigitalBrain`, `DigitalBrain.Sdk.Microsoft.Flutter`, `DigitalBrain.Sdk.Microsoft.Windows`, `DigitalBrain.Sdk` and all `DigitalBrain.Kernel.*`, `DigitalBrain.Ino.*`, `DigitalBrain.Awesome*`, `DigitalBrain.Clients.*` — these are correctly named for their assemblies already.

**Build-fix-loop technique (for entangled namespaces):** rename the declaration in each owning file; build the solution; for every `CS0246`/`CS0234` (type not found), add the correct new `using` (`DigitalBrain.Protocol.*` and/or `DigitalBrain.Os.*`) to that file — keeping any still-valid old usings — until the build is clean. A file that used both a Protocol synapse and an Os synapse via the old shared namespace will end up with both new usings.

---

## Task 0: Branch + baseline

- [ ] **Step 1:** `git checkout -b feat/dissolve-core-into-os` (confirm with `git branch --show-current`).
- [ ] **Step 2:** Build: `dotnet build DigitalBrain.slnx -c Debug -v minimal` → `Build succeeded`, 0 errors.
- [ ] **Step 3:** Baseline tests: `dotnet test DigitalBrain.slnx -c Debug` → Core.Tests 82/6/0, Protocol.Tests 3/0/0, InoLang.Tests 3/0/0, overall 0 failures. This is the regression bar.

---

## Task 1: Rename the `DigitalBrain.Core` project/assembly → `DigitalBrain.Os` (namespaces unchanged)

This task only renames the project, assembly, folder, references, and removes the stale `DigitalBrain.Contracts` PackageId. Namespaces stay `DigitalBrain.Core.*` (migrated in later tasks). Isolated and fully green-gated.

**Files:** rename `src/DigitalBrain.Core/` → `src/DigitalBrain.Os/`; rename the `.csproj`; edit it; update every dependent `.csproj` ProjectReference; edit `DigitalBrain.slnx`; check `InternalsVisibleTo.cs`.

- [ ] **Step 1: Move the project folder + csproj with git**

```bash
git mv src/DigitalBrain.Core src/DigitalBrain.Os
git mv src/DigitalBrain.Os/DigitalBrain.Core.csproj src/DigitalBrain.Os/DigitalBrain.Os.csproj
```

- [ ] **Step 2: Edit `src/DigitalBrain.Os/DigitalBrain.Os.csproj`**

Remove the stale contracts packaging (the whole `<IsPackable>`, `<PackageId>DigitalBrain.Contracts</PackageId>`, `<GeneratePackageOnBuild>`, `<Version>`, `<Description>`, `<PackageTags>` lines and their misleading comments). Add an explicit assembly/root-namespace name. The PropertyGroup should read:

```xml
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <LangVersion>preview</LangVersion>
    <NoWarn>$(NoWarn);ORLEANSEXP005</NoWarn>
    <AssemblyName>DigitalBrain.Os</AssemblyName>
    <RootNamespace>DigitalBrain.Os</RootNamespace>
  </PropertyGroup>
```

Leave the `<ItemGroup>`s (Orleans/YamlDotNet package refs + Protocol/InoLang project refs) unchanged.

- [ ] **Step 3: Update every dependent project's ProjectReference**

Find them: `grep -rl "DigitalBrain.Core\\\\DigitalBrain.Core.csproj\|DigitalBrain.Core/DigitalBrain.Core.csproj" --include=*.csproj src | grep -v /obj/`
In each, replace `..\DigitalBrain.Core\DigitalBrain.Core.csproj` with `..\DigitalBrain.Os\DigitalBrain.Os.csproj` (known dependents include `DigitalBrain.Kernel`, `DigitalBrain.Aspire.Hosting`, `DigitalBrain.Sdk`, `DigitalBrain.Awesome`, `DigitalBrain.Connectors`, `DigitalBrain.Ino`, `DigitalBrain.Clients.Console`, `DigitalBrain.Mcp`, `DigitalBrain.AppHost`, `DigitalBrain.Core.Tests` — but rely on the grep, do not assume).

- [ ] **Step 4: Update `DigitalBrain.slnx`**

Change the `Contracts` folder's project path from `src/DigitalBrain.Core/DigitalBrain.Core.csproj` to `src/DigitalBrain.Os/DigitalBrain.Os.csproj`. (Folder regrouping happens in Task 5; for now just fix the path so the solution loads.)

- [ ] **Step 5: Check InternalsVisibleTo**

Open `src/DigitalBrain.Os/InternalsVisibleTo.cs`. If it names the assembly `DigitalBrain.Core` anywhere, update to `DigitalBrain.Os`. If it grants access TO another assembly (e.g. tests), leave the target name as-is.

- [ ] **Step 6: Build**

Run: `dotnet build DigitalBrain.slnx -c Debug -v minimal`
Expected: `Build succeeded`, 0 errors. The assembly is now `DigitalBrain.Os` while namespaces remain `DigitalBrain.Core.*` — that is the intended intermediate state. If a NEW error mentions an unresolved `DigitalBrain.Core` assembly reference, a `.csproj` ProjectReference was missed in Step 3 — fix it.

- [ ] **Step 7: Test**

Run: `dotnet test DigitalBrain.slnx -c Debug`
Expected: baseline counts (Core.Tests 82/6/0, Protocol 3/0/0, InoLang 3/0/0).

> Note: the `Protocol.Tests` assertion `Protocol_assembly_does_not_reference_Core_or_Sdk_assemblies` checks for an assembly named `DigitalBrain.Core`. Since Protocol never referenced Core, renaming Core→Os does not affect it; it stays green. Do not change that test in this task.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "refactor(os): rename DigitalBrain.Core project/assembly to DigitalBrain.Os; drop stale Contracts PackageId"
```

---

## Task 2: Blanket-rename the 8 single-assembly namespaces

Each namespace here lives entirely in one assembly, so a whole-solution find/replace of the namespace STRING (covering both the `namespace X;` declaration and every `using X;`) is safe.

**Apply these replacements across all `*.cs` files under `src/` (exclude `obj/` and `bin/`), longest-prefix first to avoid partial overlaps:**

1. `DigitalBrain.Core.Domain.ValueObjects.Identity` → `DigitalBrain.Protocol.Domain.ValueObjects.Identity`
2. `DigitalBrain.Core.Domain.ValueObjects.Distribution` → `DigitalBrain.Protocol.Domain.ValueObjects.Distribution`
3. `DigitalBrain.Core.Domain.Ino` → `DigitalBrain.InoLang.Domain.Ino`
4. `DigitalBrain.Core.Domain.Yaml` → `DigitalBrain.InoLang.Domain.Yaml`
5. `DigitalBrain.Core.Infrastructure.Orleans` → `DigitalBrain.Os.Infrastructure.Orleans`
6. `DigitalBrain.Core.Application` → `DigitalBrain.Os.Application`
7. `DigitalBrain.Core.UI` → `DigitalBrain.Os.UI`
8. `DigitalBrain.Core.State` → `DigitalBrain.Os.State`

- [ ] **Step 1: Apply replacements 1–8**

Use a scripted, reviewable replace. Example (Git Bash) — run each replacement over tracked `.cs` files only:

```bash
files=$(git ls-files 'src/**/*.cs')
for f in $files; do
  sed -i \
    -e 's/DigitalBrain\.Core\.Domain\.ValueObjects\.Identity/DigitalBrain.Protocol.Domain.ValueObjects.Identity/g' \
    -e 's/DigitalBrain\.Core\.Domain\.ValueObjects\.Distribution/DigitalBrain.Protocol.Domain.ValueObjects.Distribution/g' \
    -e 's/DigitalBrain\.Core\.Domain\.Ino/DigitalBrain.InoLang.Domain.Ino/g' \
    -e 's/DigitalBrain\.Core\.Domain\.Yaml/DigitalBrain.InoLang.Domain.Yaml/g' \
    -e 's/DigitalBrain\.Core\.Infrastructure\.Orleans/DigitalBrain.Os.Infrastructure.Orleans/g' \
    -e 's/DigitalBrain\.Core\.Application/DigitalBrain.Os.Application/g' \
    -e 's/DigitalBrain\.Core\.UI/DigitalBrain.Os.UI/g' \
    -e 's/DigitalBrain\.Core\.State/DigitalBrain.Os.State/g' \
    "$f"
done
```

IMPORTANT: these patterns are ordered so longer prefixes (`...Domain.ValueObjects.Identity`) are handled, and none is a prefix of another in a way that corrupts (e.g. `DigitalBrain.Core.Domain.Ino` will NOT accidentally hit `DigitalBrain.Core.Domain.InoSomething` — there is no such namespace; verify with grep before/after). Do NOT touch `DigitalBrain.Core.Domain.Events` or bare `DigitalBrain.Core` here — those are entangled and handled in Tasks 3–4.

- [ ] **Step 2: Sanity-check no stray partial matches**

Run: `grep -rn "DigitalBrain.Os.UISomething\|DigitalBrain.Protocol.Domain.ValueObjects.IdentitySomething" src` (expect empty) and visually confirm a couple of edited files.

- [ ] **Step 3: Build**

Run: `dotnet build DigitalBrain.slnx -c Debug -v minimal`
Expected: `Build succeeded`. These namespaces are single-assembly, so the blanket rename is self-consistent. If errors appear, they will be in files where the namespace was referenced fully-qualified in an unusual way — fix those specific spots. Report BLOCKED only if a non-mechanical issue arises.

- [ ] **Step 4: Test**

Run: `dotnet test DigitalBrain.slnx -c Debug` → baseline counts.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(ns): align 8 single-assembly namespaces to Protocol/InoLang/Os"
```

---

## Task 3: Split the entangled `DigitalBrain.Core.Domain.Events` namespace

Protocol synapse types and Os lifecycle synapse types currently share this namespace. Split by file, then fix consumers.

- [ ] **Step 1: Rename declarations in the owning files**

Protocol Event files → `namespace DigitalBrain.Protocol.Domain.Events;`:
```bash
for f in Synapse DynamicSynapse Distribution InstallBundle BundleInstalled BundlePublished RuleTelemetry; do
  sed -i 's/^namespace DigitalBrain\.Core\.Domain\.Events;/namespace DigitalBrain.Protocol.Domain.Events;/' "src/DigitalBrain.Protocol/Domain/Events/$f.cs"
done
```

Os Event files → `namespace DigitalBrain.Os.Domain.Events;`:
```bash
for f in Activated Agent Agentic Deactivated Guide HandlerReacted KernelTask NeuronTelemetry SimulationSynapses SynapseIncoming SynapseOutgoing; do
  sed -i 's/^namespace DigitalBrain\.Core\.Domain\.Events;/namespace DigitalBrain.Os.Domain.Events;/' "src/DigitalBrain.Os/Domain/Events/$f.cs"
done
```

Also fix INTERNAL references within Protocol/Os Event files that fully-qualify sibling types (e.g. `DigitalBrain.Core.Domain.Events.X` used inside `Synapse.cs`): grep each renamed file for residual `DigitalBrain.Core.Domain.Events` and update to the correct new root.

- [ ] **Step 2: Build to get the consumer error list**

Run: `dotnet build DigitalBrain.slnx -c Debug -v minimal 2>&1 | grep -E "error CS0246|error CS0234"`
This lists every file/type that can no longer resolve a synapse via the old namespace.

- [ ] **Step 3: Fix consumers via the build-fix loop**

For each file still containing `using DigitalBrain.Core.Domain.Events;`: determine which synapse types it uses.
- If it uses any Protocol synapse (`Synapse`, `DynamicSynapse`, `InstallBundle`, `BundleInstalled`, `BundlePublished`, `RuleMatched`/`RuleFault`/etc. from `RuleTelemetry`/`Distribution`), add `using DigitalBrain.Protocol.Domain.Events;`.
- If it uses any Os synapse (`Activated`, `SynapseIncoming`, `SynapseOutgoing`, `HandlerReacted`, `KernelTask`, `NeuronTelemetry`, `Agent`, `Agentic`, `Guide`, `Deactivated`, `SimulationSynapses`), add `using DigitalBrain.Os.Domain.Events;`.
- Most files that touch the timeline will need BOTH. Then remove the now-dangling `using DigitalBrain.Core.Domain.Events;`.

A fast first pass: replace `using DigitalBrain.Core.Domain.Events;` with BOTH usings everywhere it appears, then let the compiler's unused-using analysis / build guide cleanup:
```bash
for f in $(git ls-files 'src/**/*.cs'); do
  sed -i 's#^using DigitalBrain\.Core\.Domain\.Events;#using DigitalBrain.Protocol.Domain.Events;\nusing DigitalBrain.Os.Domain.Events;#' "$f"
done
```
(Adding an unused `using` is harmless — it is at most a warning, never an error. This converts the whole namespace split into a single mechanical pass.) After this, also catch any remaining FULLY-QUALIFIED `DigitalBrain.Core.Domain.Events.TypeName` references with a targeted grep and rewrite each to the correct new root based on whether `TypeName` is a Protocol or Os synapse.

- [ ] **Step 4: Build**

Run: `dotnet build DigitalBrain.slnx -c Debug -v minimal`
Expected: `Build succeeded`. If any `CS0246` remains, it is a fully-qualified reference missed in Step 3 — fix it. If a type is genuinely ambiguous (same simple name in both new namespaces), there is none here (Protocol and Os synapse type names are disjoint), so ambiguity errors indicate a real mistake — investigate.

- [ ] **Step 5: Test**

Run: `dotnet test DigitalBrain.slnx -c Debug` → baseline counts.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(ns): split Domain.Events into Protocol.Domain.Events + Os.Domain.Events"
```

---

## Task 4: Split the root `DigitalBrain.Core` namespace + the Aspire namespace; fix the InoValidator probe

- [ ] **Step 1: Rename the root-namespace declarations**

Protocol root file (`INeuron`/`IHandle`/`IEmit`) → `namespace DigitalBrain.Protocol;`:
```bash
sed -i 's/^namespace DigitalBrain\.Core;/namespace DigitalBrain.Protocol;/' src/DigitalBrain.Protocol/INeuron.cs
```

Os root files → `namespace DigitalBrain.Os;` (confirm the file list first: `grep -rln "^namespace DigitalBrain.Core;" src/DigitalBrain.Os`):
```bash
for f in $(grep -rl "^namespace DigitalBrain\.Core;" src/DigitalBrain.Os --include=*.cs); do
  sed -i 's/^namespace DigitalBrain\.Core;/namespace DigitalBrain.Os;/' "$f"
done
```

- [ ] **Step 2: Rename the Protocol-owned Aspire namespace**

The five Protocol Aspire files → `namespace DigitalBrain.Protocol.Microsoft.Aspire;`:
```bash
for f in IAspire StartDistributedApp DistributedAppStarted RestartResource ResourceRestarted; do
  sed -i 's/^namespace DigitalBrain\.Sdk\.Microsoft\.Aspire;/namespace DigitalBrain.Protocol.Microsoft.Aspire;/' "src/DigitalBrain.Protocol/Microsoft/Aspire/$f.cs"
done
```
(The Sdk's own Aspire files keep `DigitalBrain.Sdk.Microsoft.Aspire`.)

- [ ] **Step 3: Fix consumers via build-fix loop (root + Aspire)**

Root `using DigitalBrain.Core;` consumers need `DigitalBrain.Protocol` (for `INeuron`/`IHandle`/`IEmit`) and/or `DigitalBrain.Os` (for `ICluster`/`SimulationCatalog`/`SurfaceFanout`). Almost every neuron uses `INeuron`/`IHandle`, so add both then let build guide:
```bash
for f in $(git ls-files 'src/**/*.cs'); do
  sed -i 's#^using DigitalBrain\.Core;#using DigitalBrain.Protocol;\nusing DigitalBrain.Os;#' "$f"
done
```
Aspire: the 5 consumers using `using DigitalBrain.Sdk.Microsoft.Aspire;` that reference `IAspire`/the 4 synapses now also need `using DigitalBrain.Protocol.Microsoft.Aspire;`. The Sdk impl `Aspire.cs` (which implements `IAspire`) is one of them. Add the new using to each; keep the Sdk one only where a Sdk-namespace Aspire type is also used:
```bash
for f in $(grep -rl "using DigitalBrain\.Sdk\.Microsoft\.Aspire;" src --include=*.cs | grep -v /obj/); do
  grep -q "using DigitalBrain.Protocol.Microsoft.Aspire;" "$f" || sed -i 's#^using DigitalBrain\.Sdk\.Microsoft\.Aspire;#using DigitalBrain.Sdk.Microsoft.Aspire;\nusing DigitalBrain.Protocol.Microsoft.Aspire;#' "$f"
done
```

- [ ] **Step 4: Fix the InoValidator runtime probe**

In `src/DigitalBrain.InoLang/Domain/Ino/InoValidator.cs`, replace the broken hard-coded probe (currently lines ~109-110):

```csharp
                var t = Type.GetType($"DigitalBrain.Core.Domain.Events.{synapseType}") 
                     ?? Type.GetType($"DigitalBrain.Core.Domain.Events.{synapseType}, DigitalBrain.Core");
```

with an assembly-name-independent scan across loaded assemblies (synapse types now live in the Protocol and Os assemblies under their new namespaces):

```csharp
                Type? t = null;
                string[] candidateNamespaces =
                {
                    $"DigitalBrain.Protocol.Domain.Events.{synapseType}",
                    $"DigitalBrain.Os.Domain.Events.{synapseType}",
                };
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    foreach (var fullName in candidateNamespaces)
                    {
                        t = assembly.GetType(fullName);
                        if (t != null) break;
                    }
                    if (t != null) break;
                }
```

- [ ] **Step 5: Build**

Run: `dotnet build DigitalBrain.slnx -c Debug -v minimal`
Expected: `Build succeeded`. Resolve any residual `CS0246` (a fully-qualified `DigitalBrain.Core.X` reference missed by the using passes — grep `DigitalBrain.Core` over `src/**/*.cs` and rewrite each remaining hit to `DigitalBrain.Protocol`/`DigitalBrain.Os` based on the type).

- [ ] **Step 6: Verify no `DigitalBrain.Core.*` identifiers remain in source**

Run: `grep -rn "DigitalBrain\.Core\b" src --include=*.cs | grep -v /obj/`
Expected: empty (no code references the old root anymore). Any hit is a leftover to fix. (The `Protocol.Tests` leaf assertion still references the string "DigitalBrain.Core" as a NEGATIVE assertion — that is in a test file and is correct to keep; if the grep shows only that line, it is fine. Confirm it is the `Assert.DoesNotContain("DigitalBrain.Core", ...)` line.)

- [ ] **Step 7: Test**

Run: `dotnet test DigitalBrain.slnx -c Debug` → baseline counts. The `InoValidator` probe fix is exercised by the existing ino validation tests; confirm they still pass.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "refactor(ns): split root Core namespace into Protocol+Os; align Protocol Aspire ns; fix InoValidator probe"
```

---

## Task 5: Rename the test project, regroup the solution, finalize

- [ ] **Step 1: Rename `DigitalBrain.Core.Tests` → `DigitalBrain.Os.Tests`**

```bash
git mv src/DigitalBrain.Core.Tests src/DigitalBrain.Os.Tests
git mv src/DigitalBrain.Os.Tests/DigitalBrain.Core.Tests.csproj src/DigitalBrain.Os.Tests/DigitalBrain.Os.Tests.csproj
```
In `DigitalBrain.Os.Tests.csproj`, update the ProjectReference to the renamed runtime (`..\DigitalBrain.Os\DigitalBrain.Os.csproj` — should already be correct from Task 1's grep pass; verify). Update any `<RootNamespace>`/`<AssemblyName>` if present.

- [ ] **Step 2: Update the test project's own namespaces**

Replace `namespace DigitalBrain.Core.Tests` → `namespace DigitalBrain.Os.Tests` (and any `DigitalBrain.Core.Tests` references) across `src/DigitalBrain.Os.Tests/**/*.cs`:
```bash
for f in $(git ls-files 'src/DigitalBrain.Os.Tests/**/*.cs'); do
  sed -i 's/DigitalBrain\.Core\.Tests/DigitalBrain.Os.Tests/g' "$f"
done
```
Also check any `[assembly: ...]` / pa-files path references to the old name (e.g. in `run-ci.ps1` the test exe path `DigitalBrain.Core.Tests.dll`) and update them.

- [ ] **Step 3: Update `DigitalBrain.slnx` references + regroup folders**

- Point the test project entry to `src/DigitalBrain.Os.Tests/DigitalBrain.Os.Tests.csproj`.
- Rename the `/Contracts/` folder to `/Os/` (it now holds the Os runtime project), or create an `/Os/` folder grouping `DigitalBrain.Os`, `DigitalBrain.Kernel`, `DigitalBrain.AppHost`, `DigitalBrain.Aspire.Hosting`, `DigitalBrain.SourceGen`. Keep `/Protocol/`, `/InoLang/`, and the test entries valid. (Solution folders are cosmetic; the only hard requirement is every `<Project Path>` resolves.)

- [ ] **Step 4: Update `run-ci.ps1` (and any tooling) test paths**

Run: `grep -rn "DigitalBrain.Core.Tests\|DigitalBrain.Core\b" run-ci.ps1 *.slnx 2>/dev/null` and update remaining `DigitalBrain.Core.Tests` → `DigitalBrain.Os.Tests` and `DigitalBrain.Core` project path → `DigitalBrain.Os`.

- [ ] **Step 5: Build**

Run: `dotnet build DigitalBrain.slnx -c Debug -v minimal` → `Build succeeded`.

- [ ] **Step 6: Full suite**

Run: `dotnet test DigitalBrain.slnx -c Debug`
Expected: Os.Tests 82/6/0 (was Core.Tests), Protocol.Tests 3/0/0, InoLang.Tests 3/0/0, overall 0 failures.

- [ ] **Step 7: Final grep — no `DigitalBrain.Core` anywhere except intentional negative test assertion**

Run: `grep -rn "DigitalBrain\.Core" src run-ci.ps1 DigitalBrain.slnx --include=*.cs --include=*.csproj 2>/dev/null | grep -v /obj/`
Expected: only the `Protocol.Tests` `Assert.DoesNotContain("DigitalBrain.Core", ...)` line (a deliberate negative assertion). Everything else gone.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "refactor(os): rename Core.Tests to Os.Tests; regroup solution under /Os/; finalize Core dissolution"
```

---

## Done-when (this plan)

- No project, assembly, or namespace named `DigitalBrain.Core` remains (only the deliberate negative test assertion string).
- `DigitalBrain.Os` is the runtime library; namespaces are `DigitalBrain.Protocol.*`, `DigitalBrain.InoLang.*`, `DigitalBrain.Os.*`, each matching its assembly.
- Stale `DigitalBrain.Contracts` PackageId removed; InoValidator probe resolves synapse types by scanning loaded assemblies.
- Full solution builds; Os.Tests 82/6/0, Protocol.Tests 3/0/0, InoLang.Tests 3/0/0.

## Deviations log (fill during execution)

- `DigitalBrain.Sdk.Microsoft.Aspire` intentionally split: Protocol-owned Aspire types → `DigitalBrain.Protocol.Microsoft.Aspire`; Sdk-owned Aspire impl stays `DigitalBrain.Sdk.Microsoft.Aspire` (distinct roots, no longer shared).
- (record anything that could not be renamed cleanly and why)
