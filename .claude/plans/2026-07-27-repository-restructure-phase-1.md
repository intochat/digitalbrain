# Repository Restructure — Phase 1 (Mechanical) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move and rename every project into the ratified `src/` (packages) + `os/` (product) + `tests/fixtures/` structure, with zero behaviour change — the root test gate must report exactly **239/239** before and after.

**Architecture:** The repository today is `src/ modules/ hosts/ behaviors/ samples/ tests/` — six buckets whose membership is decided by taste. Phase 1 replaces that with two rules that are *predicates, not opinions*: **`src/modules/` holds exactly the projects implementing `IModule`; everything else that ships is `src/core/`; `os/` ships nothing.** Namespaces are already decoupled from project names via explicit `<RootNamespace>`, so this phase is paths-and-names only. No public API is redesigned here.

**Tech Stack:** .NET 11 (`net11.0`), Orleans 10.2.2, Aspire 13.5.0-preview.1.26376.5, xunit.v3 3.2.2, Reqnroll 3.3.4, Flutter/Dart clients, MSBuild `.slnx` solution format.

## Global Constraints

- **Starting commit:** `439c5b4a` on branch `agent/digitalbrain-hosting-testing`. Verify with `git rev-parse HEAD` before starting.
- **The gate is exactly 239 passed / 0 failed / 13 assemblies.** A pure move that changes the count means something broke. Never "fix" the count by editing tests.
- **Never `--filter` for the completion gate.** Root run only: `dotnet test DigitalBrain.slnx -c Release`.
- **No behaviour changes in this phase.** The only code edits permitted are: `RootNamespace` pins, `InternalsVisibleTo` assembly-name strings, the `DigitalBrain.Os` → `DigitalBrain.OS` namespace, the `Ui` → `UI` identifier rename, and the `ResolveUiProjectPath` fallback path. Anything else is out of scope — write it down, do not do it.
- **`clients/` does not move.** CLAUDE.md trap: Flutter's Windows native build walks up looking for `Directory.Build.targets`. Moving the Dart tree changes that walk. Do not create a root `Directory.Build.targets` and do not add stub props under `clients/`.
- **Do not create a `docs/` tree.** CLAUDE.md forbids it. This plan lives in `.claude/plans/` as an execution artifact and should be deleted when phase 1 lands.
- **Aspire resource ids stay lowercase-kebab** (`silo`, `digitalbrain-mcp`, `digitalbrain-ui`, `digitalbrain-flutter`, `brain`). They are deployment identifiers, not .NET identifiers. Renaming them churns deploy config for no gain.
- **Nothing is published in this phase.** Do not add `dotnet pack`, do not push to NuGet.
- Commit at green boundaries only. Never `--no-verify`, never `reset --hard`, never `push --force`.

---

## Background an implementer needs

### How this repo tests (do not redesign it — it is already best practice)

Three tiers, verified against Microsoft's own guidance:

| Tier | Harness | Suites | What it proves |
|---|---|---|---|
| **L0** unit | none | `Behaviors.Tests` (64), `DigitalBrain.Tests` (53) | codec correctness; Aspire *builder* projection without running anything |
| **L1** cluster | Orleans `InProcessTestCluster` via `FixtureCluster` | 10 suites, ~116 tests | real 3-silo traffic, committed journals |
| **L2** app graph | Aspire `DistributedApplicationTestingBuilder` | `HostTests` (6, ~1m21s) | real AppHost boots, resources healthy |

`FixtureCluster` (`src/DigitalBrain.Testing/Cluster/FixtureCluster.cs`) builds `new InProcessTestClusterBuilder(SiloCount: 3)` and injects test doubles through the `ConfigureSilo` delegate: `IJournalStorageProvider` → `RecordingJournalStorageProvider`, `IReminderRegistry` → `TestReminderRegistry`, `TimeProvider` → `ControllableTimeProvider` (fixed epoch 2040-01-01), plus scripted external services. `DigitalBrainFixture : IAsyncLifetime` starts the cluster once per test class and leases it per method with a `SemaphoreSlim`.

Orleans' *Unit testing with Orleans* documentation states `InProcessTestCluster` is "the recommended testing infrastructure", specifically for "shared service instances — easily share mock services, test doubles, and other instances between your test code and the silo hosts", and recommends reusing a cluster "using xUnit's class or collection fixtures". This repository already does all three. **Phase 1 must not alter any of it.**

Microsoft ships **no** public `IChatClient` test double — `Microsoft.Extensions.AI.Abstractions` exposes only `IChatClient` and `DelegatingChatClient`. So `ScriptedChatClient` is the supported approach, not a reinvention. It stays exactly as-is in phase 1.

### Why namespaces do not move when projects do

Every module already pins its namespace explicitly, e.g. `DigitalBrain.Modules.AI.csproj` declares `<RootNamespace>DigitalBrain.AI</RootNamespace>`. CLAUDE.md's rule — *"Folders organize; namespaces carry public meaning. A folder does not create a namespace."* — is therefore already enforced by the project files.

**The trap:** three projects have **no** explicit `<RootNamespace>`, so MSBuild defaults it to the project file name. Renaming those projects silently renames their namespace and breaks compilation across the repo. Task 2 pins them *before* any rename.

| Project | Explicit RootNamespace today? | Namespace in code | Action |
|---|---|---|---|
| `DigitalBrain.Kernel` | **no** — defaults to `DigitalBrain.Kernel` | `DigitalBrain.Kernel` | **must pin** before renaming to `DigitalBrain` |
| `DigitalBrain.Ui` | **no** — defaults to `DigitalBrain.Ui` | `DigitalBrain.Ui` | **must pin** to `DigitalBrain.UI` |
| `DigitalBrain.Integrations.Mcp` | **no** — defaults | `DigitalBrain.Integrations.Mcp` | **must pin** to `DigitalBrain.Mcp` |
| `DigitalBrain.Behaviors.Os` | yes → `DigitalBrain.Os` | `DigitalBrain.Os` | change value to `DigitalBrain.OS` |
| all `DigitalBrain.Modules.*` | yes | unchanged | no action |

### Why `OS` and `UI`, not `Os` and `Ui`

Microsoft's *Capitalization Conventions*: "A special case is made for **two-letter acronyms in which both letters are capitalized**" (`IOStream`); acronyms of three or more letters are PascalCased (`HtmlTag`). `OS` and `UI` are two-letter acronyms. Measured case-sensitively today: **99 `Ui*` identifiers, 0 `UI*`; 13 `DigitalBrain.Os`, 0 `DigitalBrain.OS`.** This is public API about to be published, so it is fixed now — after publishing it becomes a breaking change.

### The one forced code edit

`modules/DigitalBrain.Modules.Flutter.Aspire.Hosting/FlutterHostingExtensions.cs` (around lines 186–200) resolves the UI edge project by walking the repo layout:

```csharp
Path.Combine(appHostDirectory, "..", "DigitalBrain.Ui", "DigitalBrain.Ui.csproj"),
Path.Combine(appHostDirectory, "..", "..", "hosts", "DigitalBrain.Ui", "DigitalBrain.Ui.csproj"),
```

`hosts/` stops existing in this phase, so this **must** be updated or the Flutter UI edge throws at AppHost build. (The deeper fix — shipping the UI edge as a package reference instead of a path probe — is **phase 3**, not here.)

---

## File Structure

Complete move table. Every row is `git mv`. This table *is* the guarantee that the exact structure lands.

### `src/core/kernel/`
| From | To |
|---|---|
| `src/DigitalBrain.Kernel/` | `src/core/kernel/DigitalBrain/` (package id becomes `DigitalBrain`) |
| `src/DigitalBrain.Abstractions/` | `src/core/kernel/DigitalBrain.Abstractions/` |
| `src/DigitalBrain.Client/` | `src/core/kernel/DigitalBrain.Client/` |
| `src/DigitalBrain.SourceGeneration/` | `src/core/kernel/DigitalBrain.SourceGeneration/` |
| `src/DigitalBrain/` (metapackage) | **DELETED** — 0 public types, 0 references, 0 pack jobs |

### `src/core/aspire/`, `src/core/testing/`, `src/core/security/`, `src/core/behaviors/`, `src/core/mcp/`
| From | To |
|---|---|
| `src/DigitalBrain.Aspire/` | `src/core/aspire/DigitalBrain.Aspire/` |
| `src/DigitalBrain.Aspire.Hosting/` | `src/core/aspire/DigitalBrain.Aspire.Hosting/` |
| `src/DigitalBrain.Testing/` | `src/core/testing/DigitalBrain.Testing/` |
| `src/DigitalBrain.Security/` | `src/core/security/DigitalBrain.Security/` |
| `src/DigitalBrain.Behaviors/` | `src/core/behaviors/DigitalBrain.Behaviors/` |
| `src/DigitalBrain.Behaviors.Runtime/` | **MERGED into `DigitalBrain.Behaviors`** (Task 4) |
| `src/DigitalBrain.Integrations.Mcp/` | `src/core/mcp/DigitalBrain.Mcp/` |
| `src/DigitalBrain.Integrations.Mcp.Aspire.Hosting/` | `src/core/mcp/DigitalBrain.Mcp.Aspire.Hosting/` |

### `src/modules/` — only `IModule` implementations
| From | To |
|---|---|
| `modules/DigitalBrain.Modules.AI{,.Contracts,.Aspire.Hosting}/` | `src/modules/ai/` (names unchanged) |
| `modules/DigitalBrain.Modules.Chat{,.Contracts}/` | `src/modules/chat/` |
| `modules/DigitalBrain.Modules.Flutter{,.Contracts,.Aspire.Hosting}/` | `src/modules/flutter/` |
| `hosts/DigitalBrain.Ui/` | `src/modules/flutter/DigitalBrain.Modules.Flutter.Http/` |
| `modules/DigitalBrain.Modules.Google{,.Contracts,.Aspire.Hosting}/` | `src/modules/google/` |
| `modules/DigitalBrain.Modules.Salesforce{,.Contracts,.Aspire.Hosting}/` | `src/modules/salesforce/` |
| `modules/DigitalBrain.Modules.Tasks{,.Contracts}/` | `src/modules/tasks/` |
| `modules/DigitalBrain.Modules.Time{,.Contracts}/` | `src/modules/time/` |

### `os/` — the product, nothing packable
| From | To |
|---|---|
| `hosts/DigitalBrain.Host/` | `os/DigitalBrain.OS.Host/` |
| `hosts/DigitalBrain.Mcp/` | `os/DigitalBrain.OS.Mcp/` |
| `behaviors/DigitalBrain.Behaviors.Os/` | `os/DigitalBrain.OS.Behaviors/` |
| `hosts/DigitalBrain.AppHost/` | `os/DigitalBrain.OS.AppHost/` |

### Test projects — moved beside what they test, renamed for their subject
| From | To |
|---|---|
| `tests/DigitalBrain.ModuleTests/` | `src/modules/ai/DigitalBrain.Modules.AI.Tests/` |
| `tests/DigitalBrain.Time.Tests/` | `src/modules/time/DigitalBrain.Modules.Time.Tests/` |
| `tests/DigitalBrain.Tasks.Tests/` | `src/modules/tasks/DigitalBrain.Modules.Tasks.Tests/` |
| `tests/DigitalBrain.Flutter.Tests/` | `src/modules/flutter/DigitalBrain.Modules.Flutter.Tests/` |
| `tests/DigitalBrain.Ui.Tests/` | `src/modules/flutter/DigitalBrain.Modules.Flutter.Http.Tests/` |
| `tests/DigitalBrain.Behaviors.Tests/` | `src/core/behaviors/DigitalBrain.Behaviors.Tests/` |
| `tests/DigitalBrain.TestingTests/` | `src/core/testing/DigitalBrain.Testing.Tests/` |
| `tests/DigitalBrain.Integrations.Tests/` | `src/core/mcp/DigitalBrain.Mcp.Tests/` |
| `tests/DigitalBrain.Tests/` | `src/DigitalBrain.PublishGate.Tests/` |
| `tests/DigitalBrain.Os.Bdd.Tests/` | `os/tests/DigitalBrain.OS.Bdd.Tests/` |
| `tests/DigitalBrain.Compositions.Tests/` | `os/tests/DigitalBrain.OS.Composition.Tests/` |
| `tests/DigitalBrain.HostTests/` | `os/tests/DigitalBrain.OS.Host.Tests/` |
| `tests/DigitalBrain.Quickstart.Tests/` | `tests/fixtures/DigitalBrain.Quickstart.Tests/` |

> **Note on `DigitalBrain.Integrations.Tests` → `DigitalBrain.Mcp.Tests`:** it currently spans Google + Salesforce + MCP + AccountEnrichment. Phase 1 only renames and moves it whole. **Phase 2** splits it into `Modules.Google.Tests`, `Modules.Salesforce.Tests` and a true `Mcp.Tests`. Do not attempt the split here.

### `tests/fixtures/` — was `samples/`
| From | To |
|---|---|
| `samples/DigitalBrain.Quickstart{,.Contracts}/` | `tests/fixtures/` |
| `samples/DigitalBrain.AccountEnrichment/` | `tests/fixtures/` |
| `samples/DigitalBrain.Compositions/` | `tests/fixtures/` |
| `hosts/DigitalBrain.Quickstart.Host/` | `tests/fixtures/apphosts/` |
| `hosts/DigitalBrain.Quickstart.AppHost/` | `tests/fixtures/apphosts/` |
| `hosts/DigitalBrain.TestingAppHost/` | `tests/fixtures/apphosts/` |

### Non-project files modified
- `DigitalBrain.slnx` — every `<Project Path>` and folder rewritten
- `aspire.config.json` — appHost path → `os/DigitalBrain.OS.AppHost/DigitalBrain.OS.AppHost.csproj`
- `os/DigitalBrain.OS.AppHost/DigitalBrain.OS.AppHost.csproj` — `CodeGraphRoot` depth, all `ProjectReference` paths
- `os/DigitalBrain.OS.AppHost/AppHost.cs` — `Projects.DigitalBrain_Host` → `Projects.DigitalBrain_OS_Host`, `Projects.DigitalBrain_Mcp` → `Projects.DigitalBrain_OS_Mcp`
- `modules/.../FlutterHostingExtensions.cs` — the `hosts` path probe
- `README.md` — "Repository shape" block
- `CLAUDE.md` — the `RefreshCodeGraph` project path reference
- All `AssemblyInfo.cs` containing `InternalsVisibleTo` for a renamed assembly

---

## Tasks

### Task 1: Record the ground and capture the baseline

**Files:** none modified.

**Interfaces:**
- Produces: `baseline.txt` in the scratchpad containing the exact pre-move test tally that Task 14 must match.

- [ ] **Step 1: Verify the starting commit and a clean tree**

```powershell
git rev-parse HEAD          # expect 439c5b4a...
git status --porcelain      # expect empty
```

If HEAD differs or the tree is dirty, **stop and surface it**. Do not revert someone else's work, do not sweep it into this refactor.

- [ ] **Step 2: Build and capture the baseline gate**

```powershell
dotnet build DigitalBrain.slnx -c Release --nologo
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal" 2>&1 |
  Select-String -Pattern 'Passed!|Failed!' |
  Set-Content "$env:TEMP\digitalbrain-baseline.txt"
Get-Content "$env:TEMP\digitalbrain-baseline.txt"
```

Expected: build succeeds with **0 errors, 0 warnings**; 13 `Passed!` lines summing to **239 passed, 0 failed, 0 skipped**. `HostTests` alone takes ~1m21s, so allow a 10-minute timeout.

- [ ] **Step 3: Create the working branch**

```powershell
git switch -c agent/restructure-phase-1
```

- [ ] **Step 4: Commit nothing — this task produces only the baseline artifact**

No commit. Proceed to Task 2.

---

### Task 2: Pin every implicit `RootNamespace` before anything moves

This task is pure insurance and **must precede every rename**. Three projects rely on MSBuild defaulting `RootNamespace` to the project file name. Renaming them without pinning silently changes their namespace and produces hundreds of `CS0246` errors.

**Files:**
- Modify: `src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`
- Modify: `hosts/DigitalBrain.Ui/DigitalBrain.Ui.csproj`
- Modify: `src/DigitalBrain.Integrations.Mcp/DigitalBrain.Integrations.Mcp.csproj`

**Interfaces:**
- Produces: three project files whose namespace is now independent of their file name, so Tasks 5–8 can rename freely.

- [ ] **Step 1: Pin the kernel's namespace**

In `src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`, inside the first `<PropertyGroup>`, add:

```xml
<RootNamespace>DigitalBrain.Kernel</RootNamespace>
```

This is the load-bearing one. `src/DigitalBrain.Tests/Contracts/PublicSurface.cs` computes `KernelNamespace = typeof(Neuron).Namespace` and asserts Aspire.Hosting exposes no type from it. Collapse the namespace to `DigitalBrain` and that guard silently starts checking the whole product.

- [ ] **Step 2: Pin the UI edge's namespace, with the acronym fixed**

In `hosts/DigitalBrain.Ui/DigitalBrain.Ui.csproj`, inside the first `<PropertyGroup>`, add:

```xml
<RootNamespace>DigitalBrain.UI</RootNamespace>
```

- [ ] **Step 3: Pin the MCP client's namespace to its future name**

In `src/DigitalBrain.Integrations.Mcp/DigitalBrain.Integrations.Mcp.csproj`, inside the first `<PropertyGroup>`, add:

```xml
<RootNamespace>DigitalBrain.Mcp</RootNamespace>
```

- [ ] **Step 4: Build — expect failures, and that is the point**

```powershell
dotnet build DigitalBrain.slnx -c Release --nologo 2>&1 | Select-String -Pattern 'error' | Select-Object -First 5
```

Expected: **no errors.** `RootNamespace` only affects the default namespace of *newly generated* files and implicit usings; existing files declare their namespace explicitly. If you do see errors, they indicate a file relying on the implicit namespace — fix that file's explicit `namespace` declaration rather than reverting the pin.

- [ ] **Step 5: Commit**

```powershell
git add src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj hosts/DigitalBrain.Ui/DigitalBrain.Ui.csproj src/DigitalBrain.Integrations.Mcp/DigitalBrain.Integrations.Mcp.csproj
git commit -m @'
refactor: pin implicit RootNamespace before any project rename

Three projects let MSBuild default RootNamespace to the project file name.
Renaming them would silently move their namespace; the kernel's would collapse
from DigitalBrain.Kernel to DigitalBrain and quietly widen the PublishGate
guard that asserts Aspire.Hosting exposes no kernel type.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
'@
```

---

### Task 3: Rename the `Os` namespace to `OS` and `Ui` identifiers to `UI`

Done **before** the moves so the rename diff is readable rather than tangled with path churn.

**Files:**
- Modify: `behaviors/DigitalBrain.Behaviors.Os/DigitalBrain.Behaviors.Os.csproj` (RootNamespace value)
- Modify: all files declaring or referencing `DigitalBrain.Os` (13 occurrences)
- Modify: all files with `Ui`-prefixed PascalCase identifiers (99 occurrences)

**Interfaces:**
- Produces: namespace `DigitalBrain.OS`; identifiers `UIEdge`, `UISurface`, `UIHttpEndpointName`, `DefaultUIResourceName`, `FlutterUIEdgeOptions`, `UIHealthPath`, `UIBaseEnvironmentVariable` — consumed by Tasks 7 and 9.

- [ ] **Step 1: Change the Behaviors.Os RootNamespace value**

In `behaviors/DigitalBrain.Behaviors.Os/DigitalBrain.Behaviors.Os.csproj`:

```xml
<RootNamespace>DigitalBrain.OS</RootNamespace>
```

- [ ] **Step 2: Rewrite the namespace across all C# files**

```powershell
$files = git ls-files '*.cs' | Where-Object { (Select-String -Path $_ -Pattern 'DigitalBrain\.Os\b' -CaseSensitive -Quiet) }
foreach ($f in $files) {
  (Get-Content $f -Raw) -creplace 'DigitalBrain\.Os\b', 'DigitalBrain.OS' | Set-Content $f -NoNewline
}
$files
```

`-creplace` is the case-**sensitive** operator. Using `-replace` here would also mangle unrelated casings.

- [ ] **Step 3: Rewrite `Ui` and `Os` acronym identifiers**

The acronym appears **mid-identifier**, not only at the start — `ResolveUiProjectPath`, `EnsureUiEdge`, `FlutterUiEdgeOptions`, `IUiGateway`, `BehaviorOsActivationBoot`. A `\bUi[A-Z]` pattern catches only 99 of the **143** `Ui` occurrences and would leave the codebase half-renamed and non-compiling. Use a lookahead with no word-boundary anchor:

```powershell
foreach ($f in (git ls-files '*.cs')) {
  $t = Get-Content $f -Raw; $o = $t
  $t = $t -creplace 'Ui(?=[A-Z])', 'UI'
  $t = $t -creplace 'Os(?=[A-Z])', 'OS'
  if ($t -ne $o) { Set-Content $f $t -NoNewline; "updated: $f" }
}
```

**Why this is safe here (verified, not assumed):** the danger of dropping `\b` is a real word ending in "ui"/"os" followed by an uppercase letter — `MauiApp` would become `MaUIApp`. Every matching identifier in this repo was enumerated and all are genuine acronyms: `AddUiEdgeServices`, `AssertUiHasNamedHttpEndpoint`, `DefaultUiResourceName`, `EnsureUiEdge`, `FlutterUiEdgeOptions`, `IUiGateway`, `IUiRoot`, `LiveProductUiNorthbound`, `MapUiHost`, `ResolveUiProjectPath`, `UiEndpoints`, `UiGateway`, `UiHost`, `OsBehaviorsModule`, `OsCluster`, `OsFixture`, `BehaviorOsActivationBoot`, `AssertNoOsSurfaceResources`. No `Maui`, no `Gui`, and `Dispose`/`Compose` are unaffected because their `os` is followed by a lowercase letter.

- [ ] **Step 4: Rename the files whose type name changed**

CLAUDE.md: *"One top-level type per file."* A renamed type needs a renamed file, or the next reader cannot find it.

```powershell
git mv behaviors/DigitalBrain.Behaviors.Os/OsBehaviorsModule.cs                     behaviors/DigitalBrain.Behaviors.Os/OSBehaviorsModule.cs
git mv hosts/DigitalBrain.Ui/UiEdgeContract.cs                                      hosts/DigitalBrain.Ui/UIEdgeContract.cs
git mv hosts/DigitalBrain.Ui/UiEdgeServices.cs                                      hosts/DigitalBrain.Ui/UIEdgeServices.cs
git mv hosts/DigitalBrain.Ui/UiEndpoints.cs                                         hosts/DigitalBrain.Ui/UIEndpoints.cs
git mv hosts/DigitalBrain.Ui/UiHost.cs                                              hosts/DigitalBrain.Ui/UIHost.cs
git mv modules/DigitalBrain.Modules.Flutter.Aspire.Hosting/FlutterUiEdgeOptions.cs  modules/DigitalBrain.Modules.Flutter.Aspire.Hosting/FlutterUIEdgeOptions.cs
git mv tests/DigitalBrain.Compositions.Tests/BehaviorOsActivationBoot.cs            tests/DigitalBrain.Compositions.Tests/BehaviorOSActivationBoot.cs
git mv tests/DigitalBrain.Compositions.Tests/BehaviorOsActivationHonesty.cs         tests/DigitalBrain.Compositions.Tests/BehaviorOSActivationHonesty.cs
git mv tests/DigitalBrain.Os.Bdd.Tests/Support/OsCluster.cs                         tests/DigitalBrain.Os.Bdd.Tests/Support/OSCluster.cs
git mv tests/DigitalBrain.Os.Bdd.Tests/Support/OsFixture.cs                         tests/DigitalBrain.Os.Bdd.Tests/Support/OSFixture.cs
git mv tests/DigitalBrain.Tests/Hosting/FlutterHostingUiEdgeContracts.cs            tests/DigitalBrain.Tests/Hosting/FlutterHostingUIEdgeContracts.cs
git mv tests/DigitalBrain.Ui.Tests/LiveProductUiNorthbound.cs                       tests/DigitalBrain.Ui.Tests/LiveProductUINorthbound.cs
git mv tests/DigitalBrain.Ui.Tests/UiEdgeRoundTrip.cs                               tests/DigitalBrain.Ui.Tests/UIEdgeRoundTrip.cs
git mv tests/DigitalBrain.Ui.Tests/UiEdgeSse.cs                                     tests/DigitalBrain.Ui.Tests/UIEdgeSse.cs
git mv tests/DigitalBrain.Ui.Tests/UiEdgeVocabulary.cs                              tests/DigitalBrain.Ui.Tests/UIEdgeVocabulary.cs
git mv tests/DigitalBrain.Ui.Tests/UiFixture.cs                                     tests/DigitalBrain.Ui.Tests/UIFixture.cs
```

Paths are pre-move (Tasks 5–9 relocate them afterwards). On Windows, `git mv` handles case-only renames correctly where a plain filesystem rename would not.

- [ ] **Step 5: Verify the rename is complete**

```powershell
"remaining 'DigitalBrain.Os' : " + (Select-String -Path (git ls-files '*.cs') -Pattern 'DigitalBrain\.Os\b' -CaseSensitive).Count
"remaining 'Ui'+Upper        : " + (Select-String -Path (git ls-files '*.cs') -Pattern 'Ui[A-Z]' -CaseSensitive).Count
"remaining 'Os'+Upper        : " + (Select-String -Path (git ls-files '*.cs') -Pattern 'Os[A-Z]' -CaseSensitive).Count
"new 'DigitalBrain.OS'       : " + (Select-String -Path (git ls-files '*.cs') -Pattern 'DigitalBrain\.OS\b' -CaseSensitive).Count
"new 'UI'+Upper              : " + (Select-String -Path (git ls-files '*.cs') -Pattern 'UI[A-Z]' -CaseSensitive).Count
"new 'OS'+Upper              : " + (Select-String -Path (git ls-files '*.cs') -Pattern 'OS[A-Z]' -CaseSensitive).Count
```

Expected: first three **0**; then **13**, **143**, **19**.

> **`clients/` is deliberately NOT renamed here — this is a decision, not an oversight.** The Dart code uses the same acronym: `DigitalBrainUiEdgeClient` (`edge_client.dart`, `main.dart`, `digitalbrain_host.dart`, and 3 test files), plus `resolveUiBaseRaw`/`requireUiBaseUri` in `host_environment.dart`. Dart's style guide carries the same two-letter rule, so they arguably should become `DigitalBrainUIEdgeClient`. It is excluded from phase 1 because (a) it needs the separate `clients/` gates — `dart test`, `dart analyze`, `flutter analyze`, `flutter test`, `flutter build windows` — which are not part of the 239-test root gate, and (b) `wire_contract_golden_test.dart` pins the wire contract and must be checked before touching wire-adjacent names. **Raise this with the owner before phase 2.**

- [ ] **Step 6: Build**

```powershell
dotnet build DigitalBrain.slnx -c Release --nologo 2>&1 | Select-String -Pattern 'error|Build succeeded|Error\(s\)'
```

Expected: `Build succeeded`, 0 errors.

If you get `CS0246` for a `UI*`/`OS*` type, something outside `*.cs` still uses the old spelling. The `.feature` files were checked and contain neither acronym, but re-check `tests/DigitalBrain.Os.Bdd.Tests/Features/*.feature` if a step binding fails to resolve — Reqnroll matches step text to `[Binding]` methods by string, so a renamed *method* is fine but renamed step *text* is not. This rename touches no step text.

- [ ] **Step 7: Run the full gate**

```powershell
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal" 2>&1 | Select-String -Pattern 'Passed!|Failed!'
```

Expected: **239 passed, 0 failed**, 13 assemblies — identical to baseline. The BDD suite (4 tests) is the one to watch: its bindings are discovered by attribute, so a broken rename shows up as "no matching step definition" rather than a compile error.

- [ ] **Step 8: Commit**

```powershell
git add -A
git commit -m @'
refactor: OS and UI are two-letter acronyms, so both letters capitalise

Microsoft Capitalization Conventions: "A special case is made for two-letter
acronyms in which both letters are capitalized" (IOStream); three-plus letters
PascalCase (HtmlTag). Measured before this change: 143 Ui+Upper occurrences and
0 UI+Upper, 19 Os+Upper and 13 DigitalBrain.Os.

The acronym occurs mid-identifier as well as word-initially - ResolveUiProjectPath,
EnsureUiEdge, IUiGateway, BehaviorOsActivationBoot - so the sweep is anchored on a
lookahead rather than a word boundary. Every match was enumerated first to rule out
a real word ending in "ui"/"os"; there is no Maui or Gui here, and Dispose/Compose
are unaffected because their "os" precedes a lowercase letter. Sixteen files were
renamed to match their type, per one-top-level-type-per-file.

clients/ is deliberately untouched: the Dart side uses DigitalBrainUiEdgeClient and
needs its own gates plus a wire-contract check before renaming.

Done before publishing, because afterwards it is a breaking change.
Aspire resource ids stay lowercase-kebab - they are deployment identifiers,
not .NET identifiers.

Root gate 239/239 across 13 assemblies, unchanged.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
'@
```

---

### Task 4: Merge `Behaviors.Runtime` into `Behaviors` and delete the metapackage

Two deletions justified by measurement, done before the moves so fewer projects need relocating.

**Files:**
- Move: `src/DigitalBrain.Behaviors.Runtime/Artifacts/*.cs` → `src/DigitalBrain.Behaviors/Artifacts/`
- Delete: `src/DigitalBrain.Behaviors.Runtime/` (whole directory)
- Delete: `src/DigitalBrain/` (metapackage + `_._`)
- Modify: `tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj`
- Modify: `DigitalBrain.slnx`

**Interfaces:**
- Produces: a single `DigitalBrain.Behaviors` assembly containing both the authoring SDK and the canonical artifact codec. `CanonicalArtifactReader`, `CanonicalArtifactWriter`, `CanonicalJson`, `CanonicalZip` keep their existing namespaces and accessibility.

**Why:** `Behaviors.Runtime` has **2 public types**, **zero `PackageReference`s** (only a `ProjectReference` to `DigitalBrain.Behaviors`), manipulates that package's own types (`BehaviorArtifactEnvelope`, `BehaviorDefinitionManifest`), and **its tests already live in `DigitalBrain.Behaviors.Tests`**. The split is artificial and merging adds no dependency. The metapackage has **0 public types, 0 references anywhere, 0 pack jobs in CI, and 0 packages on nuget.org** — it exists only to shape a NuGet graph that does not exist.

- [ ] **Step 1: Move the codec source into the SDK project**

```powershell
New-Item -ItemType Directory -Force src/DigitalBrain.Behaviors/Artifacts | Out-Null
git mv src/DigitalBrain.Behaviors.Runtime/Artifacts/CanonicalArtifactReader.cs src/DigitalBrain.Behaviors/Artifacts/
git mv src/DigitalBrain.Behaviors.Runtime/Artifacts/CanonicalArtifactWriter.cs src/DigitalBrain.Behaviors/Artifacts/
git mv src/DigitalBrain.Behaviors.Runtime/Artifacts/CanonicalJson.cs          src/DigitalBrain.Behaviors/Artifacts/
git mv src/DigitalBrain.Behaviors.Runtime/Artifacts/CanonicalZip.cs           src/DigitalBrain.Behaviors/Artifacts/
```

- [ ] **Step 2: Delete the emptied project and the metapackage**

```powershell
git rm -r src/DigitalBrain.Behaviors.Runtime
git rm -r src/DigitalBrain
```

- [ ] **Step 3: Drop the dead ProjectReference from the test project**

In `tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj`, delete the line:

```xml
<ProjectReference Include="..\..\src\DigitalBrain.Behaviors.Runtime\DigitalBrain.Behaviors.Runtime.csproj" />
```

Keep the `DigitalBrain.Behaviors` reference.

- [ ] **Step 4: Remove both projects from the solution**

In `DigitalBrain.slnx`, delete these two lines:

```xml
<Project Path="src/DigitalBrain/DigitalBrain.csproj" />
<Project Path="src/DigitalBrain.Behaviors.Runtime/DigitalBrain.Behaviors.Runtime.csproj" />
```

- [ ] **Step 5: Build and run the owning suite**

```powershell
dotnet build DigitalBrain.slnx -c Release --nologo 2>&1 | Select-String -Pattern 'error|Build succeeded|Error\(s\)'
dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --logger "console;verbosity=minimal" 2>&1 | Select-String -Pattern 'Passed!|Failed!'
```

Expected: build succeeds, 0 errors; `Behaviors.Tests` reports **64 passed, 0 failed**.

If the codec files were `internal` and the tests used `InternalsVisibleTo`, they now live in the same assembly and still compile — no action needed. If you get `CS0436` (type conflicts), a file was copied rather than moved; check for leftovers under `src/DigitalBrain.Behaviors.Runtime/`.

- [ ] **Step 6: Run the full gate**

```powershell
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal" 2>&1 | Select-String -Pattern 'Passed!|Failed!'
```

Expected: **239 passed, 0 failed**, now across **13** assemblies still (no test project was removed).

- [ ] **Step 7: Commit**

```powershell
git add -A
git commit -m @'
refactor: one Behaviors package; delete the metapackage

Behaviors.Runtime had 2 public types, zero PackageReferences, manipulated
DigitalBrain.Behaviors' own types, and its tests already lived in
DigitalBrain.Behaviors.Tests. The split was artificial and merging costs no
dependency.

The metapackage had 0 public types, 0 references anywhere in the repo, 0 pack
jobs in CI, and 0 packages published. It existed only to shape a NuGet graph
that does not exist.

Root gate 239/239, unchanged.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
'@
```

---

### Task 5: Move core packages into `src/core/`

**Files:** directory moves only, plus `DigitalBrain.slnx` and every `ProjectReference` whose relative path changed.

**Interfaces:**
- Produces: `src/core/kernel/DigitalBrain/DigitalBrain.csproj` (assembly `DigitalBrain`, namespace `DigitalBrain.Kernel`), consumed by Tasks 6–9.

- [ ] **Step 1: Create the folders and move**

```powershell
New-Item -ItemType Directory -Force src/core/kernel, src/core/aspire, src/core/testing, src/core/security, src/core/behaviors, src/core/mcp | Out-Null

git mv src/DigitalBrain.Abstractions      src/core/kernel/DigitalBrain.Abstractions
git mv src/DigitalBrain.Client            src/core/kernel/DigitalBrain.Client
git mv src/DigitalBrain.SourceGeneration  src/core/kernel/DigitalBrain.SourceGeneration
git mv src/DigitalBrain.Kernel            src/core/kernel/DigitalBrain
git mv src/core/kernel/DigitalBrain/DigitalBrain.Kernel.csproj src/core/kernel/DigitalBrain/DigitalBrain.csproj

git mv src/DigitalBrain.Aspire            src/core/aspire/DigitalBrain.Aspire
git mv src/DigitalBrain.Aspire.Hosting    src/core/aspire/DigitalBrain.Aspire.Hosting
git mv src/DigitalBrain.Testing           src/core/testing/DigitalBrain.Testing
git mv src/DigitalBrain.Security          src/core/security/DigitalBrain.Security
git mv src/DigitalBrain.Behaviors         src/core/behaviors/DigitalBrain.Behaviors

git mv src/DigitalBrain.Integrations.Mcp  src/core/mcp/DigitalBrain.Mcp
git mv src/core/mcp/DigitalBrain.Mcp/DigitalBrain.Integrations.Mcp.csproj src/core/mcp/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj
git mv src/DigitalBrain.Integrations.Mcp.Aspire.Hosting src/core/mcp/DigitalBrain.Mcp.Aspire.Hosting
git mv src/core/mcp/DigitalBrain.Mcp.Aspire.Hosting/DigitalBrain.Integrations.Mcp.Aspire.Hosting.csproj src/core/mcp/DigitalBrain.Mcp.Aspire.Hosting/DigitalBrain.Mcp.Aspire.Hosting.csproj
```

- [ ] **Step 2: Add the package id to the renamed kernel project**

The project file is now `DigitalBrain.csproj`, so the package id is already `DigitalBrain`. Confirm the `<RootNamespace>DigitalBrain.Kernel</RootNamespace>` pinned in Task 2 is still present — it is what keeps the namespace stable.

- [ ] **Step 3: Update every `ProjectReference` that pointed at a moved project**

```powershell
$map = @{
  'DigitalBrain.Kernel\DigitalBrain.Kernel.csproj'                                   = 'DigitalBrain\DigitalBrain.csproj'
  'DigitalBrain.Integrations.Mcp\DigitalBrain.Integrations.Mcp.csproj'               = 'DigitalBrain.Mcp\DigitalBrain.Mcp.csproj'
  'DigitalBrain.Integrations.Mcp.Aspire.Hosting\DigitalBrain.Integrations.Mcp.Aspire.Hosting.csproj' = 'DigitalBrain.Mcp.Aspire.Hosting\DigitalBrain.Mcp.Aspire.Hosting.csproj'
}
foreach ($p in (git ls-files '*.csproj')) {
  $t = Get-Content $p -Raw; $o = $t
  foreach ($k in $map.Keys) { $t = $t.Replace($k, $map[$k]) }
  if ($t -ne $o) { Set-Content $p $t -NoNewline; "updated: $p" }
}
```

Then fix the `..\` depth by hand. Every reference now needs one or two extra `..\` segments. Build errors name each broken path precisely — use them as the worklist. Do **not** try to regex the depth; it differs per project.

- [ ] **Step 4: Rewrite `DigitalBrain.slnx` paths for the `/src/` folder**

Replace the `<Folder Name="/src/">` block's project paths with the new locations, e.g.:

```xml
<Project Path="src/core/kernel/DigitalBrain/DigitalBrain.csproj" />
<Project Path="src/core/kernel/DigitalBrain.Abstractions/DigitalBrain.Abstractions.csproj" />
<Project Path="src/core/kernel/DigitalBrain.Client/DigitalBrain.Client.csproj" />
<Project Path="src/core/kernel/DigitalBrain.SourceGeneration/DigitalBrain.SourceGeneration.csproj" />
<Project Path="src/core/aspire/DigitalBrain.Aspire/DigitalBrain.Aspire.csproj" />
<Project Path="src/core/aspire/DigitalBrain.Aspire.Hosting/DigitalBrain.Aspire.Hosting.csproj" />
<Project Path="src/core/testing/DigitalBrain.Testing/DigitalBrain.Testing.csproj" />
<Project Path="src/core/security/DigitalBrain.Security/DigitalBrain.Security.csproj" />
<Project Path="src/core/behaviors/DigitalBrain.Behaviors/DigitalBrain.Behaviors.csproj" />
<Project Path="src/core/mcp/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj" />
<Project Path="src/core/mcp/DigitalBrain.Mcp.Aspire.Hosting/DigitalBrain.Mcp.Aspire.Hosting.csproj" />
```

- [ ] **Step 4b: Update `InternalsVisibleTo` for the two renamed assemblies**

Assembly names changed, so the strings must change. In `src/core/security/DigitalBrain.Security/AssemblyInfo.cs`:

```csharp
[assembly: InternalsVisibleTo("DigitalBrain.Mcp")]        // was DigitalBrain.Integrations.Mcp
[assembly: InternalsVisibleTo("DigitalBrain.Modules.AI")]
```

In `src/core/aspire/DigitalBrain.Aspire.Hosting/AssemblyInfo.cs`, change `DigitalBrain.Integrations.Mcp.Aspire.Hosting` to `DigitalBrain.Mcp.Aspire.Hosting`.

In `src/core/mcp/DigitalBrain.Mcp/AssemblyInfo.cs`, keep the module entries and change the test entry to the Task 8 name (`DigitalBrain.Mcp.Tests`).

`InternalsVisibleTo` fails **silently** — a stale name produces `CS0122 inaccessible due to its protection level` at the consumer, not at the declaration. If you see CS0122 after this task, check these files first.

- [ ] **Step 5: Build until clean**

```powershell
dotnet build DigitalBrain.slnx -c Release --nologo 2>&1 | Select-String -Pattern 'error' | Select-Object -First 20
```

Iterate on the reported paths until `Build succeeded`, 0 errors. Expect several rounds — this is normal for a move of this size.

- [ ] **Step 6: Full gate**

```powershell
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal" 2>&1 | Select-String -Pattern 'Passed!|Failed!'
```

Expected: **239 passed, 0 failed**.

- [ ] **Step 7: Commit**

```powershell
git add -A
git commit -m @'
refactor: core packages move under src/core, Kernel becomes the DigitalBrain package

The headline install for a framework whose pitch is "a brain you program by
writing ordinary C#" is the package giving you `class X : Neuron`. That is the
kernel, so it takes the DigitalBrain name; the namespace stays
DigitalBrain.Kernel, pinned in an earlier commit.

Integrations.Mcp becomes DigitalBrain.Mcp: it is the OUTBOUND client, and the
name no longer collides with the inbound server now that the latter is
os/DigitalBrain.OS.Mcp.

Root gate 239/239, unchanged.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
'@
```

---

### Task 6: Move modules into `src/modules/`

**Files:** directory moves, `DigitalBrain.slnx`, `ProjectReference` paths.

**Interfaces:**
- Produces: `src/modules/<domain>/DigitalBrain.Modules.<Domain>*.csproj`. Project and assembly names are **unchanged** — only paths move. Namespaces are unchanged (already pinned via `<RootNamespace>`).

- [ ] **Step 1: Move each module family**

```powershell
foreach ($d in 'ai','chat','flutter','google','salesforce','tasks','time') {
  New-Item -ItemType Directory -Force "src/modules/$d" | Out-Null
}
git mv modules/DigitalBrain.Modules.AI              src/modules/ai/DigitalBrain.Modules.AI
git mv modules/DigitalBrain.Modules.AI.Contracts    src/modules/ai/DigitalBrain.Modules.AI.Contracts
git mv modules/DigitalBrain.Modules.AI.Aspire.Hosting src/modules/ai/DigitalBrain.Modules.AI.Aspire.Hosting
git mv modules/DigitalBrain.Modules.Chat            src/modules/chat/DigitalBrain.Modules.Chat
git mv modules/DigitalBrain.Modules.Chat.Contracts  src/modules/chat/DigitalBrain.Modules.Chat.Contracts
git mv modules/DigitalBrain.Modules.Flutter         src/modules/flutter/DigitalBrain.Modules.Flutter
git mv modules/DigitalBrain.Modules.Flutter.Contracts src/modules/flutter/DigitalBrain.Modules.Flutter.Contracts
git mv modules/DigitalBrain.Modules.Flutter.Aspire.Hosting src/modules/flutter/DigitalBrain.Modules.Flutter.Aspire.Hosting
git mv modules/DigitalBrain.Modules.Google          src/modules/google/DigitalBrain.Modules.Google
git mv modules/DigitalBrain.Modules.Google.Contracts src/modules/google/DigitalBrain.Modules.Google.Contracts
git mv modules/DigitalBrain.Modules.Google.Aspire.Hosting src/modules/google/DigitalBrain.Modules.Google.Aspire.Hosting
git mv modules/DigitalBrain.Modules.Salesforce      src/modules/salesforce/DigitalBrain.Modules.Salesforce
git mv modules/DigitalBrain.Modules.Salesforce.Contracts src/modules/salesforce/DigitalBrain.Modules.Salesforce.Contracts
git mv modules/DigitalBrain.Modules.Salesforce.Aspire.Hosting src/modules/salesforce/DigitalBrain.Modules.Salesforce.Aspire.Hosting
git mv modules/DigitalBrain.Modules.Tasks           src/modules/tasks/DigitalBrain.Modules.Tasks
git mv modules/DigitalBrain.Modules.Tasks.Contracts src/modules/tasks/DigitalBrain.Modules.Tasks.Contracts
git mv modules/DigitalBrain.Modules.Time            src/modules/time/DigitalBrain.Modules.Time
git mv modules/DigitalBrain.Modules.Time.Contracts  src/modules/time/DigitalBrain.Modules.Time.Contracts
```

- [ ] **Step 2: Move the UI edge into the Flutter module and rename it**

```powershell
git mv hosts/DigitalBrain.Ui src/modules/flutter/DigitalBrain.Modules.Flutter.Http
git mv src/modules/flutter/DigitalBrain.Modules.Flutter.Http/DigitalBrain.Ui.csproj src/modules/flutter/DigitalBrain.Modules.Flutter.Http/DigitalBrain.Modules.Flutter.Http.csproj
```

The Flutter module **owns** this project — `FlutterHostingExtensions.EnsureUIEdge` projects it as the `digitalbrain-ui` Aspire resource, and `DigitalBrain.AppHost` never references it. Its `<RootNamespace>DigitalBrain.UI</RootNamespace>` from Task 2 keeps the namespace stable across the rename.

- [ ] **Step 3: Rewrite the `/modules/` folder in `DigitalBrain.slnx`**

Replace every module path with its `src/modules/<domain>/` equivalent and add the new project:

```xml
<Project Path="src/modules/flutter/DigitalBrain.Modules.Flutter.Http/DigitalBrain.Modules.Flutter.Http.csproj" />
```

- [ ] **Step 4: Fix `ProjectReference` depths and build until clean**

```powershell
dotnet build DigitalBrain.slnx -c Release --nologo 2>&1 | Select-String -Pattern 'error' | Select-Object -First 20
```

Every module's references to `..\..\src\...` need re-depthing to `..\..\..\core\...`. Work the error list until `Build succeeded`.

- [ ] **Step 5: Full gate**

```powershell
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal" 2>&1 | Select-String -Pattern 'Passed!|Failed!'
```

Expected: **239 passed, 0 failed**.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m @'
refactor: modules move under src/modules; the UI edge joins the Flutter module

src/modules/ now holds only IModule implementations, measured: Modules.AI and
Modules.Chat implement it, Integrations.Mcp and Security do not - which is why
those two went to src/core in the previous commit.

DigitalBrain.Ui becomes DigitalBrain.Modules.Flutter.Http because the Flutter
module already owns it: FlutterHostingExtensions projects the digitalbrain-ui
resource and the AppHost never references the project. Named for its protocol,
retiring the word "edge" which spanned inbound servers and outbound test doubles.

Root gate 239/239, unchanged.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
'@
```

---

### Task 7: Create `os/` and fix the path probe it breaks

**Files:**
- Move: 4 project directories into `os/`
- Modify: `os/DigitalBrain.OS.AppHost/AppHost.cs` (Aspire `Projects.*` symbols)
- Modify: `os/DigitalBrain.OS.AppHost/DigitalBrain.OS.AppHost.csproj` (`CodeGraphRoot`)
- Modify: `src/modules/flutter/DigitalBrain.Modules.Flutter.Aspire.Hosting/FlutterHostingExtensions.cs`
- Modify: `aspire.config.json`

**Interfaces:**
- Produces: `Projects.DigitalBrain_OS_Host`, `Projects.DigitalBrain_OS_Mcp` — Aspire generates these symbols from project file names, so they change with the rename.

- [ ] **Step 1: Move and rename**

```powershell
New-Item -ItemType Directory -Force os | Out-Null
git mv hosts/DigitalBrain.Host os/DigitalBrain.OS.Host
git mv os/DigitalBrain.OS.Host/DigitalBrain.Host.csproj os/DigitalBrain.OS.Host/DigitalBrain.OS.Host.csproj
git mv hosts/DigitalBrain.Mcp os/DigitalBrain.OS.Mcp
git mv os/DigitalBrain.OS.Mcp/DigitalBrain.Mcp.csproj os/DigitalBrain.OS.Mcp/DigitalBrain.OS.Mcp.csproj
git mv behaviors/DigitalBrain.Behaviors.Os os/DigitalBrain.OS.Behaviors
git mv os/DigitalBrain.OS.Behaviors/DigitalBrain.Behaviors.Os.csproj os/DigitalBrain.OS.Behaviors/DigitalBrain.OS.Behaviors.csproj
git mv hosts/DigitalBrain.AppHost os/DigitalBrain.OS.AppHost
git mv os/DigitalBrain.OS.AppHost/DigitalBrain.AppHost.csproj os/DigitalBrain.OS.AppHost/DigitalBrain.OS.AppHost.csproj
```

- [ ] **Step 2: Update the Aspire `Projects.*` symbols in AppHost.cs**

In `os/DigitalBrain.OS.AppHost/AppHost.cs`:

```csharp
var silo = builder.AddProject<Projects.DigitalBrain_OS_Host>(ProductSurfaceResources.Silo)
    .WithReference(brain);

builder.AddProject<Projects.DigitalBrain_OS_Mcp>(ProductSurfaceResources.Mcp)
```

Aspire's source generator derives these type names from the referenced project file names, replacing `.` with `_`. **Do not** change the resource *string* arguments — `ProductSurfaceResources.Silo` is `"silo"` and `.Mcp` is `"digitalbrain-mcp"`, and those are deployment identifiers.

- [ ] **Step 3: Fix `CodeGraphRoot` depth**

In `os/DigitalBrain.OS.AppHost/DigitalBrain.OS.AppHost.csproj`, the property walks up to the repo root. `hosts/DigitalBrain.AppHost` and `os/DigitalBrain.OS.AppHost` are both two levels deep, so the existing value is still correct:

```xml
<CodeGraphRoot>$([System.IO.Path]::GetFullPath('$(MSBuildThisFileDirectory)..\..'))</CodeGraphRoot>
```

Verify it, do not change it. Also re-depth every `ProjectReference` in this file to the new `src/...` locations.

- [ ] **Step 4: Fix the path probe that `hosts/` disappearing breaks**

In `src/modules/flutter/DigitalBrain.Modules.Flutter.Aspire.Hosting/FlutterHostingExtensions.cs`, in `ResolveUIProjectPath` (renamed by Task 3), replace the candidate paths:

```csharp
private static string ResolveUIProjectPath(string appHostDirectory, string? configured)
{
    if (!string.IsNullOrWhiteSpace(configured))
    {
        return configured;
    }

    string[] candidates =
    [
        Path.Combine(appHostDirectory, "..", "DigitalBrain.Modules.Flutter.Http", "DigitalBrain.Modules.Flutter.Http.csproj"),
        Path.Combine(appHostDirectory, "..", "..", "src", "modules", "flutter", "DigitalBrain.Modules.Flutter.Http", "DigitalBrain.Modules.Flutter.Http.csproj"),
    ];

    return candidates.FirstOrDefault(File.Exists) ?? candidates[^1];
}
```

Update the accompanying error message — it currently says *"place DigitalBrain.Ui next to the AppHost under hosts/"*. Replace with: *"Pass FlutterUIEdgeOptions.ProjectPath, or place DigitalBrain.Modules.Flutter.Http under src/modules/flutter/."*

**Known debt, do not fix here:** this project is packable, so a shipped package still probes the repo layout — an external consumer must pass `ProjectPath`. **Phase 3** replaces the probe with a package reference. Leave it.

- [ ] **Step 5: Update `aspire.config.json`**

```json
{
  "appHost": {
    "path": "os/DigitalBrain.OS.AppHost/DigitalBrain.OS.AppHost.csproj"
  }
}
```

- [ ] **Step 6: Rewrite the `/hosts/` folder in `DigitalBrain.slnx`**

Rename the folder to `/os/` and list the four projects at their new paths. The three scaffolding AppHosts (`Quickstart.Host`, `Quickstart.AppHost`, `TestingAppHost`) move in Task 9 — leave their existing paths for now.

- [ ] **Step 7: Build until clean, then run the full gate**

```powershell
dotnet build DigitalBrain.slnx -c Release --nologo 2>&1 | Select-String -Pattern 'error' | Select-Object -First 20
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal" 2>&1 | Select-String -Pattern 'Passed!|Failed!'
```

Expected: `Build succeeded`; **239 passed, 0 failed**. `HostTests` is the suite that will catch a broken `Projects.*` symbol or a broken UI path probe — if it fails here, re-check Steps 2 and 4.

- [ ] **Step 8: Commit**

```powershell
git add -A
git commit -m @'
refactor: the product becomes os/, named for what the repo already calls it

hosts/ mixed four concepts. The reference graph separates them: DigitalBrain.Host
links IMPLEMENTATIONS (it is the silo), while Ui and Mcp link only Contracts plus
Client (they are protocol adapters), and AppHost is a deployment descriptor, not
a service. The adapters went to their owning module or to os/; the scaffolding
AppHosts follow in the next commit.

"os" is the repo's own word - RootNamespace DigitalBrain.OS, Os.Bdd.Tests, and a
feature file titled "The operating system answers through a behaviour".

Forced edit: FlutterHostingExtensions probed Path.Combine(appHost, "..","..",
"hosts",...) to find the UI project, and hosts/ no longer exists. Still a repo
layout probe inside a packable project - phase 3 replaces it with a package
reference.

Root gate 239/239, unchanged.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
'@
```

---

### Task 8: Move test suites beside what they test

**Files:** 12 test project directories moved and renamed, `DigitalBrain.slnx`, `InternalsVisibleTo` strings.

**Interfaces:**
- Produces: renamed test assemblies. Every `InternalsVisibleTo` naming an old test assembly must be updated in the same commit or the build breaks with `CS0122`.

**Renames (assembly name changes with the project file name):**

| Old assembly | New assembly |
|---|---|
| `DigitalBrain.ModuleTests` | `DigitalBrain.Modules.AI.Tests` |
| `DigitalBrain.Time.Tests` | `DigitalBrain.Modules.Time.Tests` |
| `DigitalBrain.Tasks.Tests` | `DigitalBrain.Modules.Tasks.Tests` |
| `DigitalBrain.Flutter.Tests` | `DigitalBrain.Modules.Flutter.Tests` |
| `DigitalBrain.Ui.Tests` | `DigitalBrain.Modules.Flutter.Http.Tests` |
| `DigitalBrain.TestingTests` | `DigitalBrain.Testing.Tests` |
| `DigitalBrain.Integrations.Tests` | `DigitalBrain.Mcp.Tests` |
| `DigitalBrain.Tests` | `DigitalBrain.PublishGate.Tests` |
| `DigitalBrain.Os.Bdd.Tests` | `DigitalBrain.OS.Bdd.Tests` |
| `DigitalBrain.Compositions.Tests` | `DigitalBrain.OS.Composition.Tests` |
| `DigitalBrain.HostTests` | `DigitalBrain.OS.Host.Tests` |
| `DigitalBrain.Behaviors.Tests` | unchanged |
| `DigitalBrain.Quickstart.Tests` | unchanged |

- [ ] **Step 1: Move and rename each suite**

```powershell
New-Item -ItemType Directory -Force os/tests, tests/fixtures | Out-Null

git mv tests/DigitalBrain.ModuleTests src/modules/ai/DigitalBrain.Modules.AI.Tests
git mv src/modules/ai/DigitalBrain.Modules.AI.Tests/DigitalBrain.ModuleTests.csproj src/modules/ai/DigitalBrain.Modules.AI.Tests/DigitalBrain.Modules.AI.Tests.csproj

git mv tests/DigitalBrain.Time.Tests src/modules/time/DigitalBrain.Modules.Time.Tests
git mv src/modules/time/DigitalBrain.Modules.Time.Tests/DigitalBrain.Time.Tests.csproj src/modules/time/DigitalBrain.Modules.Time.Tests/DigitalBrain.Modules.Time.Tests.csproj

git mv tests/DigitalBrain.Tasks.Tests src/modules/tasks/DigitalBrain.Modules.Tasks.Tests
git mv src/modules/tasks/DigitalBrain.Modules.Tasks.Tests/DigitalBrain.Tasks.Tests.csproj src/modules/tasks/DigitalBrain.Modules.Tasks.Tests/DigitalBrain.Modules.Tasks.Tests.csproj

git mv tests/DigitalBrain.Flutter.Tests src/modules/flutter/DigitalBrain.Modules.Flutter.Tests
git mv src/modules/flutter/DigitalBrain.Modules.Flutter.Tests/DigitalBrain.Flutter.Tests.csproj src/modules/flutter/DigitalBrain.Modules.Flutter.Tests/DigitalBrain.Modules.Flutter.Tests.csproj

git mv tests/DigitalBrain.Ui.Tests src/modules/flutter/DigitalBrain.Modules.Flutter.Http.Tests
git mv src/modules/flutter/DigitalBrain.Modules.Flutter.Http.Tests/DigitalBrain.Ui.Tests.csproj src/modules/flutter/DigitalBrain.Modules.Flutter.Http.Tests/DigitalBrain.Modules.Flutter.Http.Tests.csproj

git mv tests/DigitalBrain.Behaviors.Tests src/core/behaviors/DigitalBrain.Behaviors.Tests

git mv tests/DigitalBrain.TestingTests src/core/testing/DigitalBrain.Testing.Tests
git mv src/core/testing/DigitalBrain.Testing.Tests/DigitalBrain.TestingTests.csproj src/core/testing/DigitalBrain.Testing.Tests/DigitalBrain.Testing.Tests.csproj

git mv tests/DigitalBrain.Integrations.Tests src/core/mcp/DigitalBrain.Mcp.Tests
git mv src/core/mcp/DigitalBrain.Mcp.Tests/DigitalBrain.Integrations.Tests.csproj src/core/mcp/DigitalBrain.Mcp.Tests/DigitalBrain.Mcp.Tests.csproj

git mv tests/DigitalBrain.Tests src/DigitalBrain.PublishGate.Tests
git mv src/DigitalBrain.PublishGate.Tests/DigitalBrain.Tests.csproj src/DigitalBrain.PublishGate.Tests/DigitalBrain.PublishGate.Tests.csproj

git mv tests/DigitalBrain.Os.Bdd.Tests os/tests/DigitalBrain.OS.Bdd.Tests
git mv os/tests/DigitalBrain.OS.Bdd.Tests/DigitalBrain.Os.Bdd.Tests.csproj os/tests/DigitalBrain.OS.Bdd.Tests/DigitalBrain.OS.Bdd.Tests.csproj

git mv tests/DigitalBrain.Compositions.Tests os/tests/DigitalBrain.OS.Composition.Tests
git mv os/tests/DigitalBrain.OS.Composition.Tests/DigitalBrain.Compositions.Tests.csproj os/tests/DigitalBrain.OS.Composition.Tests/DigitalBrain.OS.Composition.Tests.csproj

git mv tests/DigitalBrain.HostTests os/tests/DigitalBrain.OS.Host.Tests
git mv os/tests/DigitalBrain.OS.Host.Tests/DigitalBrain.HostTests.csproj os/tests/DigitalBrain.OS.Host.Tests/DigitalBrain.OS.Host.Tests.csproj

git mv tests/DigitalBrain.Quickstart.Tests tests/fixtures/DigitalBrain.Quickstart.Tests
```

- [ ] **Step 2: Update every `InternalsVisibleTo` naming a renamed test assembly**

```powershell
$renames = @{
  'DigitalBrain.Integrations.Tests' = 'DigitalBrain.Mcp.Tests'
  'DigitalBrain.Ui.Tests'           = 'DigitalBrain.Modules.Flutter.Http.Tests'
  'DigitalBrain.Tests'              = 'DigitalBrain.PublishGate.Tests'
}
foreach ($f in (git ls-files '*AssemblyInfo.cs')) {
  $t = Get-Content $f -Raw; $o = $t
  foreach ($k in $renames.Keys) {
    $t = $t -creplace ('InternalsVisibleTo\("' + [regex]::Escape($k) + '"\)'), ('InternalsVisibleTo("' + $renames[$k] + '")')
  }
  if ($t -ne $o) { Set-Content $f $t -NoNewline; "updated: $f" }
}
```

Expected updates in at least: `src/core/testing/DigitalBrain.Testing/AssemblyInfo.cs`, `src/core/mcp/DigitalBrain.Mcp/AssemblyInfo.cs`, `src/modules/flutter/DigitalBrain.Modules.Flutter.Aspire.Hosting/AssemblyInfo.cs`.

- [ ] **Step 3: Rewrite the `/tests/` folder in `DigitalBrain.slnx`**

Give the solution folders that mirror the tree: `/src/core/`, `/src/modules/`, `/os/`, `/tests/`. Every test project now lives at its new path.

- [ ] **Step 4: Build until clean**

```powershell
dotnet build DigitalBrain.slnx -c Release --nologo 2>&1 | Select-String -Pattern 'error' | Select-Object -First 20
```

`CS0122 ... inaccessible due to its protection level` means a stale `InternalsVisibleTo` — return to Step 2.

- [ ] **Step 5: Full gate**

```powershell
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal" 2>&1 | Select-String -Pattern 'Passed!|Failed!'
```

Expected: **239 passed, 0 failed, 13 assemblies** — with new assembly names.

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m @'
refactor: every suite moves beside what it tests

DigitalBrain.ModuleTests was the AI suite all along - it referenced only
Modules.AI. Renamed to DigitalBrain.Modules.AI.Tests and moved into the module.

DigitalBrain.Tests becomes DigitalBrain.PublishGate.Tests at the root of src/,
because its subject is src/ as a whole: it walks GetReferencedAssemblies from its
own assembly and asserts no shipped package exports a MAF type, Aspire.Hosting
exposes no kernel type, and the kernel declares no UI vocabulary. It cannot live
in core/ without core's tests depending on modules - inverting the layering it
polices.

InternalsVisibleTo strings updated for every renamed assembly; those fail
silently as CS0122 at the consumer, never at the declaration.

Root gate 239/239, unchanged.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
'@
```

---

### Task 9: Samples become fixtures

**Files:** 4 sample projects + 3 scaffolding AppHosts moved, `DigitalBrain.slnx`.

**Interfaces:**
- Produces: `tests/fixtures/*`. Project and assembly names are **unchanged** — only paths move, so no `InternalsVisibleTo` or `ProjectReference` name edits are needed, just depth.

**Why:** `samples/` was a lie. All four terminate in a test — `AccountEnrichment` → 2 test projects and nothing else; `Compositions` → 1; `Quickstart` → 2 hosts + 3 tests, and those hosts are reachable only from `HostTests`. The product AppHost composes AI, Chat, OS behaviours, Flutter, Google and Salesforce — not one sample. The docs site is a separate repository (`intochat/digitalbrain.docs`), so they document nothing here either. They are shared test fixtures. **Phase 3** adds one real sample that builds only against published package references — the only thing that would prove the ecosystem works.

- [ ] **Step 1: Move**

```powershell
New-Item -ItemType Directory -Force tests/fixtures/apphosts | Out-Null
git mv samples/DigitalBrain.Quickstart.Contracts tests/fixtures/DigitalBrain.Quickstart.Contracts
git mv samples/DigitalBrain.Quickstart          tests/fixtures/DigitalBrain.Quickstart
git mv samples/DigitalBrain.AccountEnrichment   tests/fixtures/DigitalBrain.AccountEnrichment
git mv samples/DigitalBrain.Compositions        tests/fixtures/DigitalBrain.Compositions
git mv hosts/DigitalBrain.Quickstart.Host       tests/fixtures/apphosts/DigitalBrain.Quickstart.Host
git mv hosts/DigitalBrain.Quickstart.AppHost    tests/fixtures/apphosts/DigitalBrain.Quickstart.AppHost
git mv hosts/DigitalBrain.TestingAppHost        tests/fixtures/apphosts/DigitalBrain.TestingAppHost
```

- [ ] **Step 2: Confirm `hosts/`, `modules/`, `behaviors/` and `samples/` are gone**

```powershell
foreach ($d in 'hosts','modules','behaviors','samples') {
  "{0,-12} exists: {1}" -f $d, (Test-Path $d)
}
```

Expected: all four **False**. If a directory lingers it holds an untracked file — investigate rather than deleting blindly.

- [ ] **Step 3: Update the solution and fix reference depths, then build**

Rewrite the remaining `.slnx` paths, then:

```powershell
dotnet build DigitalBrain.slnx -c Release --nologo 2>&1 | Select-String -Pattern 'error' | Select-Object -First 20
```

- [ ] **Step 4: Full gate**

```powershell
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal" 2>&1 | Select-String -Pattern 'Passed!|Failed!'
```

Expected: **239 passed, 0 failed**.

- [ ] **Step 5: Commit**

```powershell
git add -A
git commit -m @'
refactor: samples become fixtures, which is what they already were

Every sample terminates in a test. AccountEnrichment is referenced by two test
projects and nothing else; Compositions by one; Quickstart by two hosts plus
three tests, and those hosts are reachable only from HostTests. The product
AppHost composes no sample, and the docs site is a separate repository, so they
documented nothing here.

Kept rather than deleted: they are the test subject for ~48 tests, and Quickstart
is the only 1-module brain the suite ever boots - os/ boots six.

Root gate 239/239, unchanged.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
'@
```

---

### Task 10: Update prose, CI and the harness to the new layout

**Files:**
- Modify: `README.md` (Repository shape block)
- Modify: `CLAUDE.md` (the `RefreshCodeGraph` path)
- Verify: `.github/workflows/ci.yml`

- [ ] **Step 1: Rewrite the README repository-shape block**

```text
src/       published packages: core/ (framework) and modules/ (IModule domains)
os/        the product: silo, MCP server, OS behaviours, AppHost
clients/   Flutter shell and the Dart wire package
tests/     the publish gate and shared fixtures
```

- [ ] **Step 2: Fix the CLAUDE.md path reference**

CLAUDE.md states the index is refreshed by `RefreshCodeGraph` in `hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`. Change to `os/DigitalBrain.OS.AppHost/DigitalBrain.OS.AppHost.csproj`.

- [ ] **Step 3: Confirm CI needs no change**

```powershell
Get-Content .github/workflows/ci.yml
```

CI runs `dotnet test DigitalBrain.slnx -c Release` — solution-relative, so it is unaffected. **Known gap, do not fix here:** CI never runs the `clients/` gates (`dart test`, `flutter analyze`, `flutter test`, `flutter build windows`) that CLAUDE.md mandates. That is additive work, out of scope for a mechanical phase.

- [ ] **Step 4: Final verification from a clean tree**

```powershell
git clean -fdx
dotnet build DigitalBrain.slnx -c Release --nologo 2>&1 | Select-String -Pattern 'error|Build succeeded|Warning\(s\)|Error\(s\)'
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal" 2>&1 | Select-String -Pattern 'Passed!|Failed!'
```

`git clean -fdx` deletes the gitignored CodeGraph index; the build regenerates it. Expected: **0 warnings, 0 errors**, and **239 passed / 0 failed / 13 assemblies**.

> If `aspire run` is available, also confirm the AppHost still starts and `list_resources` shows every resource Healthy. A green suite proves the code holds, not that the product runs.

- [ ] **Step 5: Verify the structural predicate actually holds**

```powershell
"projects under src/modules that do NOT implement IModule (expect none):"
foreach ($p in (git ls-files 'src/modules/*.csproj')) {
  $d = Split-Path $p -Parent
  if ($d -match '\.Tests$|\.Contracts$|\.Aspire\.Hosting$|\.Http$') { continue }
  $cs = git ls-files "$d/*.cs"
  if (-not $cs -or -not (Select-String -Path $cs -Pattern 'IModule' -Quiet)) { "  VIOLATION: $p" }
}
"projects still living outside src/ or os/ (expect only clients + fixtures):"
git ls-files '*.csproj' | Where-Object { $_ -notmatch '^(src|os|tests/fixtures)/' }
```

- [ ] **Step 6: Commit**

```powershell
git add -A
git commit -m @'
docs: point the harness and README at the new layout

What did I add that has no consumer today?
  Nothing. This phase moved and renamed only.

What did I claim without running a command to check?
  Nothing. Verified from a clean tree after git clean -fdx: build 0 warnings
  0 errors, root gate 239/239 across 13 assemblies - identical to the baseline
  captured before the first move.

What changed that I did not change?
  Nothing. HEAD was 439c5b4a at branch creation and the tree held only these
  moves.

Deferred deliberately, with reasons: the Flutter.Aspire.Hosting path probe is
still a repo-layout probe inside a packable project; DigitalBrain.Testing still
carries Aspire.Hosting.Testing and an MCP project reference that only 1 of 13
suites needs; CI still does not run the clients/ gates. All three are phase 3.

Co-Authored-By: Claude Opus 5 (1M context) <noreply@anthropic.com>
'@
```

---

## Phase 1 done means

- `git ls-files '*.csproj'` returns projects only under `src/`, `os/`, and `tests/fixtures/`
- `hosts/`, `modules/`, `behaviors/`, `samples/` do not exist
- `dotnet build DigitalBrain.slnx -c Release` → **0 warnings, 0 errors**
- `dotnet test DigitalBrain.slnx -c Release` → **239 passed, 0 failed, 13 assemblies**
- Verified from a clean tree (`git clean -fdx` first)
- No public API redesigned, no package published

## Explicitly NOT in phase 1

| Deferred | To | Why |
|---|---|---|
| Split the 6 cross-cutting suites | Phase 2 | authorship, not `git mv`; it would move the 239 |
| New Chat/Google/Salesforce/Security/Kernel suites | Phase 2 | same |
| Decouple the Flutter fixtures (fan-in 8) | Phase 2 | needs fixture redesign |
| Split `DigitalBrain.Testing` by tier | Phase 3 | package split; only 1 of 13 suites needs Aspire.Hosting.Testing |
| `SubstituteService<TService,TScript>` / `Substituted<TScript>` | Phase 3 | replaces public `TestBrain.McpSessionScript<T>()`; API design |
| Move `ScriptedChatClient` / `ScriptedMcpSessionFactory` out of core | Phase 3 | depends on the generic API above |
| Public MCP / Security / Aspire.Hosting seams; drop extension-seam `InternalsVisibleTo` | Phase 3 | ecosystem contract design |
| UI edge as a package reference instead of a path probe | Phase 3 | needs a test proving an out-of-repo AppHost can add it |
| `dotnet pack` in CI; reserve the `DigitalBrain.*` NuGet prefix | Phase 3 | 31 packable projects have never been packed once |
| One real sample built only against published packages | Phase 3 | the only proof the ecosystem works |
| Observability spine (README's top open defect) | separate | untouched by this restructure |

## Recovery

Every task ends at a green commit, so `git reset --soft HEAD~1` (never `--hard`) recovers a bad task without losing work. If a build spirals, `git stash` and rebuild from the last green commit rather than fighting a half-moved tree — the move tasks are cheap to re-run from the table above.

Two traps from CLAUDE.md that will bite during this phase:

- **`LNK1168`** — a running `digitalbrain_flutter.exe` locks the build output. Find and kill the holding process; it is not a code error.
- **`DOTNET_ROOT`** — `dotnet build`/`test` resolve through the CLI, but the AppHost executable resolves the runtime through `DOTNET_ROOT`. Pointed at a .NET 10 location, `aspire run` fails with a missing `Microsoft.NETCore.App` 11 while the gates stay green.
