# PR #5 Self-Review Fixes — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Land the deferred/unfixed items called out in PR #5 self-review comment 4235077155 onto the `prod-demo-2026-04-12` branch, in three independent workstreams that can ship separately.

**Source comment:** `gh api repos/LeftTwixWand/ino/issues/comments/4235077155` (author: LeftTwixWand, created 2026-04-13). The comment identifies (1) an intentional Batch 6 scope reduction that needs a follow-up, (2) a namespace convention deviation from the plan text, (3) already-done circular reference fix, (4) already-done rebase persona relocation, and (5) five pre-existing issues explicitly not fixed. It also requests eyes on three specific code locations.

**Architecture:** This document contains **three independent workstreams** (Parts A/B/C), plus a **Part 0 verification audit**. Each Part commits and merges independently; there is no coupling between them. Execute in order A → (B and/or C) — Part A is the quickest win and unblocks cleaner builds for the subsequent Parts.

**Tech Stack:** .NET 11 preview (SDK `11.0.100-preview.1.26104.118`), Orleans 10.0.1, Aspire 13.2.2 (AppHost SDK), Flutter/CanvasKit, TripRadar.Bot (nested solution under `domains/travel/TripRadar`), central package management (`Directory.Packages.props`), git `prod-demo-2026-04-12` branch.

**Target branch:** `prod-demo-2026-04-12`. Each Part pushes incrementally; merging PR #5 is blocked until all Parts the reviewer requires have landed.

**Assumptions:**
- You have `aspire` CLI installed, `gh` CLI authenticated as the repo owner, and can run `dotnet build ino.slnx` and `dotnet test ino.slnx`.
- You are starting from a clean working tree on `prod-demo-2026-04-12`.
- The Telegram bot token and ngrok auth token are available via Aspire user secrets (not needed for Parts 0/A/B; only Part C, Task C.9 needs them).

---

## Part 0 — Eyes-on Verification Audit (no code changes expected)

This is a pre-flight audit for the three locations the comment flagged as "what I'd love eyes on". The investigation that produced this plan already confirmed all three are correct, but the audit is kept here so an executor in a fresh session reproduces the verification before shipping.

### Task 0.1: Audit `src/Core/Contracts/SynapseResult.cs`

**Files:**
- Read: `src/Core/Contracts/SynapseResult.cs`

- [ ] **Step 1: Read the file**

Run: `cat src/Core/Contracts/SynapseResult.cs`

Expected field order (13 fields, all `[property: Id(N)]`):
```
0: Success (bool)
1: Payload (string)
2: Verb (string)  ← required positional, no default
3: RfwDescription (byte[]?, default null)
4: RfwData (byte[]?, default null)
5: Service (string?, default null)
6: Scopes (IReadOnlyList<string>?, default null)
7: EvolutionBlueprint (EvolutionBlueprintHint?, default null)
8: WorkspacePath (string?, default null)
9: Artifacts (IReadOnlyList<string>?, default null)
10: Metrics (IReadOnlyDictionary<string,string>?, default null)
11: ErrorDetail (string?, default null)
12: TaskId (string?, default null)
```

And four factory methods: `Ok`, `Error`, `AuthRequired`, `NeedsEvolution`. Confirm each populates `Verb`.

- [ ] **Step 2: Record finding**

Expected outcome: record shape matches spec, all factories populate `Verb`, no changes needed. If the file differs, open a thread reply on comment 4235077155 with the diff before proceeding.

### Task 0.2: Audit `src/Core/Timeline/TimelineCallFilter.cs` cycle-break

**Files:**
- Read: `src/Core/Timeline/TimelineCallFilter.cs`
- Read: `features/timetravel/Timetravel.Core/Timetravel.Core.csproj`
- Read: `src/Core/Core.csproj`

- [ ] **Step 1: Confirm Timetravel.Core is a leaf project**

Run: `grep -c 'ProjectReference' features/timetravel/Timetravel.Core/Timetravel.Core.csproj`
Expected: `0`

Any non-zero result means Timetravel.Core gained an outbound ProjectReference after 2026-04-13 and must be audited for a new cycle.

- [ ] **Step 2: Confirm Core → Timetravel.Core is the forward edge**

Run: `grep 'Timetravel.Core.csproj' src/Core/Core.csproj`
Expected: `<ProjectReference Include="..\..\features\timetravel\Timetravel.Core\Timetravel.Core.csproj" />`

- [ ] **Step 3: Confirm TimelineCallFilter sits in `namespace Core.Timeline`**

Run: `head -15 src/Core/Timeline/TimelineCallFilter.cs`
Expected: includes `namespace Core.Timeline;` and `using Timetravel.Core;`

- [ ] **Step 4: Record finding**

Expected outcome: cycle-break is correct. No changes needed.

### Task 0.3: Audit every `new SynapseResult(...)` construction site repo-wide

The reviewer's concern is that SynapseResult's field order changed during the merge with `OrchestrationResult`, so positional-arg callers might silently break. Named-arg calls are safe by construction; positional calls must have `(bool, string, string)` in exactly `(Success, Payload, Verb)` order.

**Files:**
- Read: every `.cs` that contains `new SynapseResult(`

- [ ] **Step 1: Enumerate all construction sites**

Run: `git grep -n 'new SynapseResult(' -- '*.cs' | grep -v '".*new SynapseResult'`

(The `grep -v` filters out string-literal occurrences inside script templates — those are Roslyn scripts compiled at runtime and are verified by the NeuronML tests, not the C# compiler.)

Expected sites (≈11 C# call sites as of this plan):
- `src/Neurons/Synapse/SynapseNeuron.cs` — 5 named-arg calls (already converted by the author in Batch 1; confirm `Verb:` is present on each)
- `src/Core/Neurons/NeuronGrain.cs` — 3 named-arg calls
- `src/Core/Neurons/Specialists/EvolutionHandler.cs` — 1 named-arg call
- `src/Core/Neurons/Specialists/FileDeliveryHandler.cs` — 4 positional calls `(bool, string, string)`
- `src/Core/Neurons/Specialists/FlightSearchHandler.cs` — 2 positional calls `(bool, string, string)`
- `src/Core/Neurons/Specialists/HotelSearchHandler.cs` — 2 positional calls
- `src/Core/Neurons/Specialists/PlaceDiscoveryHandler.cs` — 2 positional calls
- `src/Core/Neurons/Specialists/RecallHandler.cs` — 2 positional calls
- `src/Core/Neurons/Specialists/SchedulerHandler.cs` — 2 positional calls
- `src/Core/Neurons/Specialists/ShellHandler.cs` — 3 positional + 1 named-arg
- `src/Core/Neurons/Specialists/SummarizerHandler.cs` — 1 positional call

- [ ] **Step 2: Verify every positional call is `(bool, string, string)`**

For each positional site, confirm the three args are in `(Success, Payload, Verb)` order. Because the record's current definition has Success/Payload/Verb as positions 0/1/2, every 3-arg positional call is correct.

Expected outcome: no bugs. Ship as-is.

- [ ] **Step 3: (Optional) Add a compiler-enforced guard**

If you want future regressions to fail fast, add a tiny test:

```csharp
// tests/Core.Tests/Contracts/SynapseResultFieldOrderTests.cs
using Core.Contracts;
using Xunit;

namespace Core.Tests.Contracts;

public class SynapseResultFieldOrderTests
{
    [Fact]
    public void Positional_ctor_is_Success_Payload_Verb()
    {
        var r = new SynapseResult(true, "ok", "verb");
        Assert.True(r.Success);
        Assert.Equal("ok", r.Payload);
        Assert.Equal("verb", r.Verb);
    }
}
```

**Decision:** add this test only if Part 0 takes under 30 minutes; otherwise skip (YAGNI — the 11 positional sites are the entire population).

- [ ] **Step 4: Commit the audit (no-op or guard-test only)**

```bash
git add tests/Core.Tests/Contracts/SynapseResultFieldOrderTests.cs  # only if Step 3 was done
git commit -m "test(core): lock SynapseResult positional-ctor field order"
```

If no test was added, skip the commit.

---

## Part A — Section 5 Quick Wins

Five pre-existing issues the comment calls out as explicitly not fixed. Every task in Part A is low-risk and mechanical. Ship this Part first — it improves the signal-to-noise for Parts B and C.

### File Structure

**Created:**
- `tests/Core.Tests/Contracts/SynapseResultFieldOrderTests.cs` (optional; see Task 0.3 Step 3)

**Modified:**
- `Directory.Packages.props` — add Npgsql central pin
- `deployment/ino/Dockerfiles/silo.Dockerfile` — rewrite iaw/* paths
- `deployment/ino/Dockerfiles/mcp.Dockerfile` — rewrite iaw/* paths
- `deployment/ino/Dockerfiles/telegram.Dockerfile` — rewrite iaw/* paths
- `README.md` — 3 iaw/* references → src/*
- `CLAUDE.md` — 17 iaw/* references → src/*
- `docs/bdd_test_assessment.md` — 1 ref
- `docs/deployment/architecture-assessment.md` — 1 ref
- `docs/deployment/azure-stack.md` — 1 ref
- `docs/deployment/deployment-guide.md` — 2 refs
- `docs/deployment/hosting-options.md` — 1 ref
- `docs/neuron-ml.md` — 5 refs
- `docs/product_features/00_plan.md` — 5 refs
- `docs/product_features/feature_02_ino_new.md` — 1 ref
- `docs/product_features/feature_03_behavior_memory_vector_search.md` — 1 ref
- `src/Testing/InoTestHost.cs:17` — comment rot
- `src/Testing/NeuronBddHooks.cs:15,18,44` — comment rot
- `features/ino-new/InoNew.Tests/BehaviorMemorySiloConfigurator.cs:14` — comment rot
- `features/ino-new/InoNew.Tests/DeterministicEmbeddingGenerator.cs:11` — comment rot
- `features/timetravel/Timetravel.Tests/Steps/ShellNeuronScenarioTests.cs:24` — comment rot
- `features/timetravel/Timetravel.Tests/Steps/ShellNeuronSteps.cs:25` — comment rot
- `Aspire/ino.AppHost/Aspire.csproj` — possible TargetFramework/RID/SDK version fix (NETSDK1047)

**NOT modified in this Part (historical plan records, left as-is):**
- `docs/superpowers/plans/**/*.md` — these are point-in-time plan snapshots; retroactively rewriting them is inaccurate
- `docs/superpowers/specs/**/*.md` — review case-by-case only if architecture intent has actually shifted

### Task A.1: Pin Npgsql centrally to break the 10.0.0 vs 10.0.2 warning

The warning comes from `Aspire.Hosting.PostgreSQL 13.2.2` (which transitively requests Npgsql 10.0.0) colliding with `TripRadar/Directory.Packages.props:187` which pins `Npgsql 10.0.2`. Because `TripRadar/` is a nested solution with its own `Directory.Packages.props`, the two PackageVersions live in different scopes; the ino-root build pulls 10.0.0, the TripRadar-root build pulls 10.0.2, and the Aspire ResourcePool sees both. Fix: add Npgsql to the **root** `Directory.Packages.props` pinned to 10.0.2.

**Files:**
- Modify: `Directory.Packages.props`

- [ ] **Step 1: Locate insertion point**

Run: `grep -n 'Microsoft.Orleans.Persistence.Memory' Directory.Packages.props`
Find the Orleans block so you add Npgsql in alphabetical order next to other `N*` packages. If no `N*` packages exist, add it just after `Microsoft.VisualStudio.Azure.Containers.Tools.Targets`.

- [ ] **Step 2: Add the pin**

Insert this line inside the `<ItemGroup>` that holds `PackageVersion` entries:

```xml
<PackageVersion Include="Npgsql" Version="10.0.2" />
```

- [ ] **Step 3: Verify it takes effect**

Run: `dotnet restore ino.slnx 2>&1 | grep -i npgsql`
Expected: no `NU1605` or `NU1608` warnings about conflicting Npgsql versions. A single resolved version of `10.0.2` across the ino silo and the TripRadar.Server.Db project.

- [ ] **Step 4: Commit**

```bash
git add Directory.Packages.props
git commit -m "build: pin Npgsql 10.0.2 centrally to break 10.0.0/10.0.2 skew

The root Directory.Packages.props didn't pin Npgsql, so Aspire.Hosting.PostgreSQL
13.2.2 transitively pulled 10.0.0 while TripRadar.Server.Db (via the nested
TripRadar/Directory.Packages.props) pinned 10.0.2. Central pin at 10.0.2
aligns both trees."
```

### Task A.2: Fix `iaw/Testing` comment rot in 6 files

These are stale docstrings referencing the old path — the code itself is correct, only the prose is wrong.

**Files:**
- Modify: `src/Testing/InoTestHost.cs:17`
- Modify: `src/Testing/NeuronBddHooks.cs:15, 18, 44`
- Modify: `features/ino-new/InoNew.Tests/BehaviorMemorySiloConfigurator.cs:14`
- Modify: `features/ino-new/InoNew.Tests/DeterministicEmbeddingGenerator.cs:11`
- Modify: `features/timetravel/Timetravel.Tests/Steps/ShellNeuronScenarioTests.cs:24`
- Modify: `features/timetravel/Timetravel.Tests/Steps/ShellNeuronSteps.cs:25`

- [ ] **Step 1: Replace `iaw/Testing` with `src/Testing` in each file**

These are the exact lines to replace:

```
src/Testing/InoTestHost.cs:17
BEFORE: // travel services, and full silo wiring. Lives in iaw/Testing so it can be
AFTER:  // travel services, and full silo wiring. Lives in src/Testing so it can be
```

```
src/Testing/NeuronBddHooks.cs:15
BEFORE: // This lives in iaw/Testing (not Core, not any feature project) because it
AFTER:  // This lives in src/Testing (not Core, not any feature project) because it
```

```
src/Testing/NeuronBddHooks.cs:18
BEFORE: // leak into Core. Per architectural hygiene, iaw/Testing stays feature-
AFTER:  // leak into Core. Per architectural hygiene, src/Testing stays feature-
```

```
src/Testing/NeuronBddHooks.cs:44
BEFORE:     // AddTimelineCapture). Keeping it a delegate means iaw/Testing stays
AFTER:      // AddTimelineCapture). Keeping it a delegate means src/Testing stays
```

```
features/ino-new/InoNew.Tests/BehaviorMemorySiloConfigurator.cs:14
BEFORE: // instead of the zero-vector mock from iaw/Testing. This lets behavior-
AFTER:  // instead of the zero-vector mock from src/Testing. This lets behavior-
```

```
features/ino-new/InoNew.Tests/DeterministicEmbeddingGenerator.cs:11
BEFORE: // This deliberately does NOT live in iaw/Testing: per feedback_no_testing_in_core,
AFTER:  // This deliberately does NOT live in src/Testing: per feedback_no_testing_in_core,
```

```
features/timetravel/Timetravel.Tests/Steps/ShellNeuronScenarioTests.cs:24
BEFORE:         // iaw/Testing stays feature-agnostic; this project opts in.
AFTER:          // src/Testing stays feature-agnostic; this project opts in.
```

```
features/timetravel/Timetravel.Tests/Steps/ShellNeuronSteps.cs:25
BEFORE:     // (lives in iaw/Testing) so this step class gets the reader grain
AFTER:      // (lives in src/Testing) so this step class gets the reader grain
```

- [ ] **Step 2: Verify no remaining `iaw/Testing` refs in `.cs` files**

Run: `git grep -n 'iaw/Testing' -- '*.cs'`
Expected: empty output.

- [ ] **Step 3: Build to prove no accidental logic edits**

Run: `dotnet build ino.slnx 2>&1 | tail -20`
Expected: `Build succeeded.` (or the same pre-existing NETSDK1047 error — Task A.7 addresses that; the rest of the build should be green).

- [ ] **Step 4: Commit**

```bash
git add src/Testing/InoTestHost.cs src/Testing/NeuronBddHooks.cs features/ino-new/InoNew.Tests/BehaviorMemorySiloConfigurator.cs features/ino-new/InoNew.Tests/DeterministicEmbeddingGenerator.cs features/timetravel/Timetravel.Tests/Steps/ShellNeuronScenarioTests.cs features/timetravel/Timetravel.Tests/Steps/ShellNeuronSteps.cs
git commit -m "chore(comments): fix iaw/Testing -> src/Testing rot in 6 files"
```

### Task A.3: Rewrite README.md `iaw/*` references

**Files:**
- Modify: `README.md` (3 refs at lines 98, 107, 124)

- [ ] **Step 1: Replace line 98 tree-diagram entry**

```
BEFORE: ├── iaw/              C# kernel source — Core, Agents, Aspire, DevUI, MCP, Telegram, Testing
AFTER:  ├── src/              C# kernel source — Core, Neurons, Host, Gateways, Telegram, Testing
```

- [ ] **Step 2: Replace line 107 prose**

```
BEFORE: The `iaw/` kernel is derived from [IAW — Interactive Agents Web](https://github.com/InteractiveAgents/IAW). ino reframes and rebuilds the parts that were orchestration-flavored (the old `CodeOrchestratorAgent`, the top-down planning framing), but keeps the Orleans grain runtime, Aspire hosting, typed messaging, and observability stack.
AFTER:  The `src/` kernel is derived from [IAW — Interactive Agents Web](https://github.com/InteractiveAgents/IAW). ino reframes and rebuilds the parts that were orchestration-flavored (the old `CodeOrchestratorAgent`, the top-down planning framing), but keeps the Orleans grain runtime, Aspire hosting, typed messaging, and observability stack.
```

- [ ] **Step 3: Replace line 124 build command**

Line 124 currently says `dotnet run --project iaw/Aspire/Aspire.csproj`. This is doubly wrong: the path changed AND the project renamed. The correct incantation is documented in `CLAUDE.md` — never use `dotnet run --project` for Aspire. Replace with:

```
BEFORE: dotnet run --project iaw/Aspire/Aspire.csproj
AFTER:  aspire start
```

Add a one-line note above the code block: `Starts the AppHost detached; dashboard at https://localhost:17280.`

- [ ] **Step 4: Verify**

Run: `git grep -n 'iaw/' README.md`
Expected: empty output.

- [ ] **Step 5: Commit**

```bash
git add README.md
git commit -m "docs(readme): iaw/ -> src/ after phase-3 restructure, use aspire start"
```

### Task A.4: Rewrite CLAUDE.md `iaw/*` references

This is the larger doc sweep (17 refs). Most are in narrative paragraphs describing the kernel layout. The rewrite is largely mechanical (`iaw/` → `src/`) but a few references point to specific files that moved under a different subfolder — see the table below.

**Files:**
- Modify: `CLAUDE.md` (17 refs)

**Path mapping (old → new):**

| Old path | New path |
|---|---|
| `iaw/` (bare) | `src/` |
| `iaw/Core/Agents/Agent.cs` | `src/Core/Agents/Agent.cs` |
| `iaw/Core/Communication/IReceiver.cs` | `src/Core/Communication/IReceiver.cs` |
| `iaw/Core/Communication/` | `src/Core/Communication/` |
| `iaw/Agents/Orchestration/` | `src/Neurons/Synapse/` |
| `iaw/Agents/Orchestration/CodeOrchestratorAgent.cs` | `src/Neurons/Synapse/SynapseNeuron.cs` |
| `iaw/Aspire.Client/IAWCluster.cs` | `Aspire/ino.Client/IAWCluster.cs` |
| `iaw/Aspire/AppHost.cs` | `Aspire/ino.AppHost/AppHost.cs` |
| `iaw/Aspire/Properties/launchSettings.json` | `Aspire/ino.AppHost/Properties/launchSettings.json` |
| `iaw/Telegram/` | `src/Telegram/` |
| `iaw/Telegram/Program.cs` | `src/Telegram/Program.cs` |
| `iaw/Telegram/wwwroot/` | `src/Telegram/wwwroot/` |
| `iaw/Testing/ToolCallingMockChat.cs` | `src/Testing/ToolCallingMockChat.cs` |
| `iaw/MCP/Tools/` | `src/Gateways/Mcp/Tools/` |

- [ ] **Step 1: Rewrite the opening sentence (line 3)**

```
BEFORE: An AI-native OS built on three primitives: **neurons** (Orleans grains, LLM-optional), **synapses** (durable messages that are signal + memory + thinking at once), and a **self-improving loop** powered by Aspire. Kernel source lives at `iaw/`, solution at `ino.slnx`.
AFTER:  An AI-native OS built on three primitives: **neurons** (Orleans grains, LLM-optional), **synapses** (durable messages that are signal + memory + thinking at once), and a **self-improving loop** powered by Aspire. Kernel source lives at `src/`, solution at `ino.slnx`.
```

- [ ] **Step 2: Rewrite line 8 (Agent.cs path)**

```
BEFORE: ... The `Agent<T>` base class at `iaw/Core/Agents/Agent.cs` wires ...
AFTER:  ... The `Agent<T>` base class at `src/Core/Agents/Agent.cs` wires ...
```

- [ ] **Step 3: Rewrite line 11 (IReceiver.cs path)**

```
BEFORE: A synapse is a typed durable message delivered via `iaw/Core/Communication/IReceiver.cs`.
AFTER:  A synapse is a typed durable message delivered via `src/Core/Communication/IReceiver.cs`.
```

- [ ] **Step 4: Rewrite line 17 (Rename-pending block)**

The Rename-pending block says `iaw/Agents/Orchestration/` → `iaw/Agents/Synapse/`. Both halves are now obsolete — the rename **landed** as part of the PR under review. Replace the bullet with a historical note:

```
BEFORE: **Rename pending** (staged, not a rewrite): `CodeOrchestratorAgent` → `SynapseAgent`, `ICodeOrchestrator` → `ISynapse`, `OrchestrationResult` → `SynapseResult`, `iaw/Agents/Orchestration/` → `iaw/Agents/Synapse/`, purge "orchestration" from code/comments/prose. The `IAW.slnx` → `ino.slnx` file rename landed 2026-04-10.
AFTER:  **Rename landed 2026-04-12** (PR #5): `CodeOrchestratorAgent` → `SynapseNeuron`, `ICodeOrchestrator` → `ISynapseNeuron`, `OrchestrationResult` → `SynapseResult` (merged record), `iaw/Agents/Orchestration/` → `src/Neurons/Synapse/`. The `IAW.slnx` → `ino.slnx` file rename landed 2026-04-10.
```

- [ ] **Step 5: Rewrite lines 124, 131, 134 (Flutter build + test server)**

```
BEFORE (~line 124): - `ToolCallingMockChat` (`iaw/Testing/ToolCallingMockChat.cs`) — returns ...
AFTER:               - `ToolCallingMockChat` (`src/Testing/ToolCallingMockChat.cs`) — returns ...
```

```
BEFORE (~line 131): cp -r build/web/* ../iaw/Telegram/wwwroot/
AFTER:               cp -r build/web/* ../src/Telegram/wwwroot/
```

```
BEFORE (~line 134): The test server serves Flutter from `iaw/Telegram/wwwroot/` via `PhysicalFileProvider`. ...
AFTER:               The test server serves Flutter from `src/Telegram/wwwroot/` via `PhysicalFileProvider`. ...
```

- [ ] **Step 6: Rewrite lines 142–149 (Telegram mini app)**

```
BEFORE (~line 142): Single `index.html` served from `iaw/Telegram/wwwroot/`. Opens inside ...
AFTER:               Single `index.html` served from `src/Telegram/wwwroot/`. Opens inside ...
```

```
BEFORE (~line 144): **Architecture:** mini app POSTs to `/ino` (same origin as the bot — both served by `iaw/Telegram`).
AFTER:               **Architecture:** mini app POSTs to `/ino` (same origin as the bot — both served by `src/Telegram`).
```

```
BEFORE (~line 146): **Tunnel wiring:** `iaw/Aspire/AppHost.cs` wires ngrok onto the Telegram resource.
AFTER:               **Tunnel wiring:** `Aspire/ino.AppHost/AppHost.cs` wires ngrok onto the Telegram resource.
```

```
BEFORE (~line 149): 1. Edit `iaw/Telegram/wwwroot/index.html` or `iaw/Telegram/Program.cs`
AFTER:               1. Edit `src/Telegram/wwwroot/index.html` or `src/Telegram/Program.cs`
```

- [ ] **Step 7: Rewrite lines 178, 194, 200 (OTel endpoints + bridge)**

```
BEFORE (~line 178): **Aspire OTLP endpoints** (in `iaw/Aspire/Properties/launchSettings.json`):
AFTER:               **Aspire OTLP endpoints** (in `Aspire/ino.AppHost/Properties/launchSettings.json`):
```

```
BEFORE (~line 194): 2. `cp -r build/web/* ../iaw/Telegram/wwwroot/`
AFTER:               2. `cp -r build/web/* ../src/Telegram/wwwroot/`
```

```
BEFORE (~line 200): **Telegram OTLP bridge** (`iaw/Telegram/Program.cs`):
AFTER:               **Telegram OTLP bridge** (`src/Telegram/Program.cs`):
```

- [ ] **Step 8: Rewrite lines 231, 246, 250 (Known problems section)**

```
BEFORE (~line 231): ... Extend `iaw/Core/Communication/` to tag delivered messages.
AFTER:               ... Extend `src/Core/Communication/` to tag delivered messages.
```

```
BEFORE (~line 246): **Flow (b): ino recursively inspecting its own timeline via MCP** — deferred follow-up. `TimetravelTools` in `iaw/MCP/Tools/` ...
AFTER:               **Flow (b): ino recursively inspecting its own timeline via MCP** — deferred follow-up. `TimetravelTools` in `src/Gateways/Mcp/Tools/` ...
```

```
BEFORE (~line 250): **In-process Roslyn Scripts for orchestration** — today's `CodeOrchestratorAgent` (`iaw/Agents/Orchestration/CodeOrchestratorAgent.cs:202-302`) generates a standalone csproj per task and runs it as a child process (`IAWCluster.Connect(args)` at `iaw/Aspire.Client/IAWCluster.cs:23-31`). ...
AFTER:               **In-process Roslyn Scripts for orchestration** — today's `SynapseNeuron` (`src/Neurons/Synapse/SynapseNeuron.cs`) generates a standalone csproj per task and runs it as a child process (`IAWCluster.Connect(args)` at `Aspire/ino.Client/IAWCluster.cs`). ...
```

Note: the `:202-302` and `:23-31` line ranges are stale after the rename — drop them rather than fabricating new numbers.

- [ ] **Step 9: Verify**

Run: `git grep -n 'iaw/' CLAUDE.md`
Expected: empty output.

- [ ] **Step 10: Commit**

```bash
git add CLAUDE.md
git commit -m "docs(claude): iaw/ -> src/ path sweep after phase-3 restructure"
```

### Task A.5: Rewrite operational docs `iaw/*` references

**Files:**
- Modify: `docs/bdd_test_assessment.md` (1 ref)
- Modify: `docs/deployment/architecture-assessment.md` (1 ref)
- Modify: `docs/deployment/azure-stack.md` (1 ref)
- Modify: `docs/deployment/deployment-guide.md` (2 refs)
- Modify: `docs/deployment/hosting-options.md` (1 ref)
- Modify: `docs/neuron-ml.md` (5 refs)
- Modify: `docs/product_features/00_plan.md` (5 refs)
- Modify: `docs/product_features/feature_02_ino_new.md` (1 ref)
- Modify: `docs/product_features/feature_03_behavior_memory_vector_search.md` (1 ref)

Use the path mapping table from Task A.4 to resolve each reference. The common substitutions are `iaw/` → `src/`, `iaw/Core` → `src/Core`, `iaw/Agents` → `src/Neurons`, `iaw/MCP` → `src/Gateways/Mcp`, `iaw/Testing` → `src/Testing`, `iaw/Aspire` → `Aspire/ino.AppHost`, `iaw/Aspire.Client` → `Aspire/ino.Client`, `iaw/Aspire.Hosting` → `Aspire/ino.Hosting`.

- [ ] **Step 1: Enumerate every ref**

Run: `git grep -n 'iaw/' docs/bdd_test_assessment.md docs/deployment docs/neuron-ml.md docs/product_features`

- [ ] **Step 2: Rewrite each ref in-place**

For each match, apply the most specific mapping from the table. If the context implies a file that moved to a subfolder not listed (e.g. `iaw/Core/Persona/*` → `src/Core/Persona/*`), extend the mapping locally — the persona subdirectory was relocated in commit `38e8c47` during this same PR.

- [ ] **Step 3: Verify**

Run: `git grep -n 'iaw/' docs/bdd_test_assessment.md docs/deployment docs/neuron-ml.md docs/product_features`
Expected: empty output.

- [ ] **Step 4: Commit**

```bash
git add docs/bdd_test_assessment.md docs/deployment docs/neuron-ml.md docs/product_features
git commit -m "docs: sweep operational docs from iaw/ to src/ paths"
```

### Task A.6: Rewrite the 3 deployment Dockerfiles

**Files:**
- Modify: `deployment/ino/Dockerfiles/silo.Dockerfile`
- Modify: `deployment/ino/Dockerfiles/mcp.Dockerfile`
- Modify: `deployment/ino/Dockerfiles/telegram.Dockerfile`

Each Dockerfile has two sections to rewrite: the project-file COPY block (listed individually for restore caching) and the source-tree COPY + `dotnet publish` step. Entry point dll names are unchanged — the csprojs didn't set explicit `AssemblyName`, so defaults match csproj filenames (`Agents.Host.dll`, `MCP.dll`, `Telegram.dll`).

- [ ] **Step 1: Rewrite `silo.Dockerfile`**

Replace the entire COPY + restore + publish block with:

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src

COPY ino.slnx .
COPY Directory.Packages.props .
COPY Directory.Build.props* .
COPY Directory.Build.targets* .
COPY global.json* .

# Copy all project files for restore caching
COPY src/Core/Core.csproj src/Core/
COPY src/Neurons/Agents.csproj src/Neurons/
COPY src/Neurons/Coding/Agents.CSharp.csproj src/Neurons/Coding/
COPY src/Host/Agents.Host.csproj src/Host/
COPY Aspire/ino.Client/Aspire.Client.csproj Aspire/ino.Client/
COPY Aspire/ino.Hosting/Aspire.Hosting.csproj Aspire/ino.Hosting/
COPY src/Testing/Testing.csproj src/Testing/
COPY features/timetravel/Timetravel.Core/Timetravel.Core.csproj features/timetravel/Timetravel.Core/
COPY domains/travel/Ino.Travel/Ino.Travel.csproj domains/travel/Ino.Travel/
COPY domains/travel/TripRadar/src/TripRadar.Server.Db/TripRadar.Server.Db.csproj domains/travel/TripRadar/src/TripRadar.Server.Db/

RUN dotnet restore src/Host/Agents.Host.csproj

# Copy everything and build
COPY src/ src/
COPY Aspire/ Aspire/
COPY features/ features/
COPY domains/ domains/

RUN dotnet publish src/Host/Agents.Host.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080 11111 30000

ENTRYPOINT ["dotnet", "Agents.Host.dll"]
```

Note: the old Dockerfile did not copy `InoNew.Core.csproj` explicitly, but InoNew was merged into `src/Core/Neurons` by commit `30ebeaa`, so it no longer exists as a separate project. The `COPY src/ src/` line picks up everything now.

Also note: the old Dockerfile didn't copy `Directory.Build.targets*` — I'm adding it defensively because `Directory.Build.targets` exists at the repo root (it's listed in `git ls-tree prod-demo-2026-04-12`) and its absence from the build context can silently skip pre-compile targets.

- [ ] **Step 2: Rewrite `mcp.Dockerfile`**

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src

COPY ino.slnx .
COPY Directory.Packages.props .
COPY Directory.Build.props* .
COPY Directory.Build.targets* .
COPY global.json* .

COPY src/Core/Core.csproj src/Core/
COPY src/Neurons/Agents.csproj src/Neurons/
COPY src/Neurons/Coding/Agents.CSharp.csproj src/Neurons/Coding/
COPY src/Gateways/Mcp/MCP.csproj src/Gateways/Mcp/
COPY Aspire/ino.Client/Aspire.Client.csproj Aspire/ino.Client/
COPY Aspire/ino.Hosting/Aspire.Hosting.csproj Aspire/ino.Hosting/
COPY features/timetravel/Timetravel.Core/Timetravel.Core.csproj features/timetravel/Timetravel.Core/

RUN dotnet restore src/Gateways/Mcp/MCP.csproj

COPY src/ src/
COPY Aspire/ Aspire/
COPY features/ features/

RUN dotnet publish src/Gateways/Mcp/MCP.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:5300
EXPOSE 5300

ENTRYPOINT ["dotnet", "MCP.dll"]
```

- [ ] **Step 3: Rewrite `telegram.Dockerfile`**

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src

COPY ino.slnx .
COPY Directory.Packages.props .
COPY Directory.Build.props* .
COPY Directory.Build.targets* .
COPY global.json* .

COPY src/Core/Core.csproj src/Core/
COPY src/Neurons/Agents.csproj src/Neurons/
COPY src/Telegram/Telegram.csproj src/Telegram/
COPY Aspire/ino.Client/Aspire.Client.csproj Aspire/ino.Client/
COPY Aspire/ino.Hosting/Aspire.Hosting.csproj Aspire/ino.Hosting/
COPY src/Testing/Testing.csproj src/Testing/
COPY features/timetravel/Timetravel.Core/Timetravel.Core.csproj features/timetravel/Timetravel.Core/
COPY domains/travel/Ino.Travel/Ino.Travel.csproj domains/travel/Ino.Travel/
COPY domains/travel/TripRadar/src/TripRadar.Server.Db/TripRadar.Server.Db.csproj domains/travel/TripRadar/src/TripRadar.Server.Db/

RUN dotnet restore src/Telegram/Telegram.csproj

COPY src/ src/
COPY Aspire/ Aspire/
COPY features/ features/
COPY domains/ domains/

RUN dotnet publish src/Telegram/Telegram.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Telegram.dll"]
```

Caveat: `src/Telegram/Telegram.csproj` sets `<RuntimeIdentifier>win-x64</RuntimeIdentifier>`. That will fail on the Linux runtime base image. **If the Telegram container has ever worked in CI**, someone has overridden RID. Verify: build the image locally:

```bash
docker build -f deployment/ino/Dockerfiles/telegram.Dockerfile -t ino/telegram:test .
```

If the build fails on publish with a RID error, Part A Task A.6 Step 3 must additionally strip `<RuntimeIdentifier>` from `src/Telegram/Telegram.csproj` (or add `-r linux-x64` to the publish command). That editing decision is left to the executor because it couples Dockerfile fix with a Windows-bot feature question — hand off to the user if the container build fails.

- [ ] **Step 4: Verify all three Dockerfiles syntactically**

Run:
```bash
docker build -f deployment/ino/Dockerfiles/silo.Dockerfile -t ino/silo:test . 2>&1 | tail -5
docker build -f deployment/ino/Dockerfiles/mcp.Dockerfile -t ino/mcp:test . 2>&1 | tail -5
docker build -f deployment/ino/Dockerfiles/telegram.Dockerfile -t ino/telegram:test . 2>&1 | tail -5
```

Expected: each ends with `Successfully built ...`. Non-fatal warnings are acceptable.

If Docker is unavailable in this environment, skip Step 4 and mark it as a follow-up for CI. Make the commit message say so.

- [ ] **Step 5: Commit**

```bash
git add deployment/ino/Dockerfiles/silo.Dockerfile deployment/ino/Dockerfiles/mcp.Dockerfile deployment/ino/Dockerfiles/telegram.Dockerfile
git commit -m "deploy: rewrite Dockerfile paths iaw/ -> src/ + Aspire/ino.*/

silo   -> src/Host/Agents.Host.csproj
mcp    -> src/Gateways/Mcp/MCP.csproj
telegram -> src/Telegram/Telegram.csproj

Aspire.Client and Aspire.Hosting now live at Aspire/ino.Client/ and
Aspire/ino.Hosting/. InoNew.Core was merged into src/Core/Neurons
so it no longer needs its own csproj copy. Entry-point dll names
unchanged (csprojs do not set explicit AssemblyName)."
```

### Task A.7: Investigate and fix NETSDK1047 on `Aspire/ino.AppHost/Aspire.csproj`

The comment says the error "predates this PR, slnx-level build fails on that one project, per-project tests clean". NETSDK1047 means "Assets file doesn't have a target for '[framework]/[rid]'". Most common causes:

1. **Stale `obj/` lockfile** — restore ran against an old target framework and the `obj/project.assets.json` is cached.
2. **Aspire.AppHost.Sdk 13.2.2 vs SDK `11.0.100-preview.1` skew** — the AppHost SDK may lag the dotnet SDK on preview channels.
3. **Implicit RID** on preview SDK — `net11.0` needs an explicit `RuntimeIdentifiers` list if one of its transitive references is RID-specific.
4. **`TargetFrameworks` typo** — single vs plural attribute. The current csproj uses singular `<TargetFramework>net11.0</TargetFramework>` which is correct for single-framework.

**Files:**
- Modify (if necessary): `Aspire/ino.AppHost/Aspire.csproj`
- Possibly: `Directory.Packages.props` if an Aspire hosting version needs updating

- [ ] **Step 1: Reproduce the error with a full log**

Run:
```bash
dotnet build Aspire/ino.AppHost/Aspire.csproj 2>&1 | tee /tmp/aspire-apphost-build.log
```

Locate the exact error line. NETSDK1047 prints the missing framework/RID as `'[framework]/[rid]'` — record both values before applying any fix.

- [ ] **Step 2: Clear stale restore state (cause 1)**

Run:
```bash
rm -rf Aspire/ino.AppHost/obj Aspire/ino.AppHost/bin
dotnet restore Aspire/ino.AppHost/Aspire.csproj --force
dotnet build Aspire/ino.AppHost/Aspire.csproj 2>&1 | tail -20
```

If this clears the error, the fix is done — commit no code changes (just a clean rebuild). Skip to Step 6.

- [ ] **Step 3: Check Aspire.Hosting version skew (cause 2)**

Run:
```bash
grep -nE '(Aspire\.Hosting|Aspire\.AppHost\.Sdk)' Directory.Packages.props Aspire/ino.AppHost/Aspire.csproj
```

If `Aspire.Hosting.Orleans` is `13.2.2` but `Aspire.Hosting.Testing` is `13.1.2` (the current state on `prod-demo-2026-04-12`), align both to `13.2.2` in `Directory.Packages.props`. Context7 query to verify the correct current version before editing:

```
mcp__context7__resolve-library-id("dotnet/aspire")
mcp__context7__query-docs(id, topic: "Aspire.Hosting.Testing 13.2 compatibility")
```

Edit `Directory.Packages.props`:

```
BEFORE: <PackageVersion Include="Aspire.Hosting.Testing" Version="13.1.2" />
AFTER:  <PackageVersion Include="Aspire.Hosting.Testing" Version="13.2.2" />
```

Run: `dotnet restore ino.slnx && dotnet build Aspire/ino.AppHost/Aspire.csproj`

- [ ] **Step 4: Check RID requirements (cause 3)**

If Steps 2 and 3 don't clear it, the NETSDK1047 error message includes a specific RID (e.g. `'net11.0/win-x64'`). Add the missing RID to the csproj:

```xml
<PropertyGroup>
  <OutputType>Exe</OutputType>
  <TargetFramework>net11.0</TargetFramework>
  <RuntimeIdentifiers>win-x64;linux-x64</RuntimeIdentifiers>
  <ImplicitUsings>enable</ImplicitUsings>
  <Nullable>enable</Nullable>
  <UserSecretsId>e9789e79-129d-4802-82cf-71931d9466fe</UserSecretsId>
</PropertyGroup>
```

(Note: `RuntimeIdentifiers` plural, not `RuntimeIdentifier` singular — the plural form is additive rather than overriding the default inference.)

Run: `dotnet restore Aspire/ino.AppHost/Aspire.csproj && dotnet build Aspire/ino.AppHost/Aspire.csproj`

- [ ] **Step 5: Escalate if none of 2/3/4 work**

If the error persists after all three, stop and post a thread reply on PR #5 comment 4235077155 with:
1. Full build log
2. Output of `dotnet --info`
3. Output of `grep -r 'Aspire.AppHost.Sdk' Aspire/`

The fix may require a dotnet SDK version bump (update `global.json`) or downgrading `Aspire.AppHost.Sdk` to match. Don't bump the root SDK without user approval — global.json changes affect every developer.

- [ ] **Step 6: Verify ino.slnx builds cleanly**

Run: `dotnet build ino.slnx 2>&1 | tail -10`
Expected: `Build succeeded. 0 Error(s)` (warnings OK).

- [ ] **Step 7: Run the full test matrix**

Run:
```bash
dotnet test test/Core.Tests 2>&1 | tail -5
dotnet test features/ino-new/InoNew.Tests 2>&1 | tail -5
dotnet test features/timetravel/Timetravel.Tests 2>&1 | tail -5
dotnet test domains/travel/TripRadar/src/TripRadar.Server.Tests 2>&1 | tail -5
```

Expected (per the comment's pre-rebase baseline):
- Core.Tests: 402/402 passed
- InoNew.Tests: 124/124 passed
- Timetravel.Tests: 47/47 passed
- TripRadar.Server.Tests: 51/51 passed

- [ ] **Step 8: Commit only if code changed**

If Step 2 cleared the error with a clean rebuild (no code edits), there is nothing to commit — skip. Otherwise:

```bash
git add Aspire/ino.AppHost/Aspire.csproj Directory.Packages.props  # whichever was edited
git commit -m "fix(aspire): resolve NETSDK1047 on Aspire.AppHost slnx build

[root-cause one-liner based on actual diagnosis from Step 2/3/4]"
```

### Task A.8: Push Part A and report to the PR thread

- [ ] **Step 1: Push the branch**

```bash
git push origin prod-demo-2026-04-12
```

- [ ] **Step 2: Reply on PR #5 with Part A status**

Comment 4235077155 is a top-level issue comment (confirmed via `issue_url` in the API response), not an inline review comment, so the `/replies` endpoint doesn't apply. Post a new top-level PR comment:

```bash
gh pr comment 5 --repo LeftTwixWand/ino --body "Part A landed (Section 5 quick wins):
- Npgsql centrally pinned at 10.0.2
- iaw/Testing comment rot fixed in 6 files
- README.md / CLAUDE.md / operational docs: iaw/ -> src/ sweep
- 3 deployment Dockerfiles rewritten to new src/ + Aspire/ino.*/ layout
- NETSDK1047 resolved via [one-line root cause from Task A.7]

Section 5 is clean. Part B (namespace Ino.* rename) and Part C (Batch 6
bot consolidation) tracked in docs/superpowers/plans/2026-04-13-pr5-self-review-fixes.md."
```

---

## Part B — Namespace `Ino.*` Rename (decision-gated)

Section 2 of the comment says the author used `Core.Neurons` and `Neurons.*` to match the existing `Core.*` convention inherited from `iaw/Core`. The original plan text specified `Ino.Core.Neurons` and `Ino.Neurons.*`. You answered "Namespace Ino.* rename" in scoping, so Part B implements the literal plan text. **Before starting, confirm the scope variant below.**

### Task B.1: Decide scope — Variant A (literal) vs Variant B (full prefix)

**Variant A (literal plan text, default).** Rename only the two namespaces the plan text specifically called out:
- `Core.Neurons` → `Ino.Core.Neurons` (20+ files in `src/Core/Neurons/`)
- `Neurons.*` (`Neurons.Coding`, `Neurons.System`, `Neurons.Genesis`, `Neurons.Infrastructure`, `Neurons.Models`, `Neurons.Security`, `Neurons.Quality`, `Neurons.Personal`, `Neurons.Synapse`, `Neurons.Messages`, `Neurons.Coding.GitHub`, `Neurons.Coding.Models`, `Neurons.Coding.Prompts`, `Neurons.Coding.Tools`, `Neurons.Coding.Roslyn.Workspace`) → `Ino.Neurons.*`
- Approx 84 `using Core.Neurons;` and `using Neurons.*;` call sites across the whole repo to update.

**Tradeoff:** creates mixed prefixes in `src/Core/` — `Core.Contracts`, `Core.Registry`, `Core.AI`, `Core.Communication`, `Core.Timeline`, `Core.Telemetry`, `Core.Services` stay without the `Ino.` prefix, sitting next to the newly-renamed `Ino.Core.Neurons`. This is the inconsistency the comment warned about.

**Variant B (full prefix, consistent).** Also rename the rest of `Core.*` → `Ino.Core.*`:
- `Core` → `Ino.Core`
- `Core.Contracts` → `Ino.Core.Contracts`
- `Core.Registry` → `Ino.Core.Registry`
- `Core.AI` → `Ino.Core.AI`
- `Core.Communication` → `Ino.Core.Communication`
- `Core.Timeline` → `Ino.Core.Timeline`
- `Core.Telemetry` → `Ino.Core.Telemetry`
- `Core.Services` → `Ino.Core.Services`
- `Core.Orchestration` → `Ino.Core.Orchestration`
- Plus the Variant A sub-renames.

**Tradeoff:** consistent but ~10x the churn. Probably 400+ files, 1000+ using-statement updates. Also edits `iaw.IAWConstants` / `IAW.Core.*` vestigial names under `src/Core/*` that were never renamed from the upstream IAW fork — those may clash.

- [ ] **Step 1: Pick a variant**

**Recommendation:** Variant A. It matches the plan text literally and is what was scoped during brainstorming. The inconsistency is a conscious trade for cheaper churn, and the author's deviation was a valid judgment call that the user may have overridden without realizing the full cost. **If you want Variant B, stop here and revise this plan — Part B below implements Variant A.**

- [ ] **Step 2: Confirm and proceed**

Document your decision inline as a short note in the commit message of Task B.7.

### File Structure (Variant A)

**Modified (namespace declarations):**
- `src/Core/Neurons/*.cs` — ~20 files, `namespace Core.Neurons;` → `namespace Ino.Core.Neurons;`
- `src/Core/Neurons/Specialists/*.cs` — ~10 files, similar rename
- `src/Core/Neurons/Runtime/*.cs` — ~5 files
- `src/Core/Neurons/Startup/*.cs` — ~3 files
- `src/Neurons/**/*.cs` — every `namespace Neurons.X;` → `namespace Ino.Neurons.X;`

**Modified (using-statement sites, approx 84 across):**
- `features/ino-new/InoNew.Demo/Program.cs`
- `features/ino-new/InoNew.Tests/**/*.cs`
- `features/timetravel/Timetravel.Tests/**/*.cs`
- `features/timetravel/Timetravel.Tui/**/*.cs`
- `src/Gateways/Mcp/**/*.cs`
- `src/Host/**/*.cs`
- `src/Telegram/**/*.cs`
- `src/Testing/**/*.cs`
- `tests/Core.Tests/**/*.cs`
- `tests/E2E.Tests/**/*.cs`
- `tests/Integration.Tests/**/*.cs`

**Modified (Core.csproj + Testing.csproj InternalsVisibleTo):**
- `src/Core/Core.csproj` — assembly names in `InternalsVisibleTo` don't change (assembly is still `Core.dll`; namespace rename doesn't rename the assembly), so no edits here — just verify.

### Task B.2: Rename `Core.Neurons` → `Ino.Core.Neurons` namespace declarations

- [ ] **Step 1: Enumerate affected files**

Run: `git grep -l '^namespace Core\.Neurons' -- 'src/Core/Neurons/**/*.cs'`

- [ ] **Step 2: Replace each `namespace` declaration**

For each file:

```
BEFORE: namespace Core.Neurons;
AFTER:  namespace Ino.Core.Neurons;
```

```
BEFORE: namespace Core.Neurons.Runtime;
AFTER:  namespace Ino.Core.Neurons.Runtime;
```

```
BEFORE: namespace Core.Neurons.Startup;
AFTER:  namespace Ino.Core.Neurons.Startup;
```

```
BEFORE: namespace Core.Neurons.Specialists;
AFTER:  namespace Ino.Core.Neurons.Specialists;
```

(Use `Edit` with `replace_all: false` per-file to avoid accidentally matching string literals that happen to contain the same text.)

- [ ] **Step 3: Verify no stragglers**

Run: `git grep '^namespace Core\.Neurons' -- '*.cs'`
Expected: empty output.

### Task B.3: Update every `using Core.Neurons*;` across the repo

- [ ] **Step 1: Enumerate call sites**

Run: `git grep -n 'using Core\.Neurons' -- '*.cs' | wc -l`
Expected: approximately 60 sites.

- [ ] **Step 2: Rewrite in bulk**

For each file, replace:

```
BEFORE: using Core.Neurons;
AFTER:  using Ino.Core.Neurons;
```

```
BEFORE: using Core.Neurons.Runtime;
AFTER:  using Ino.Core.Neurons.Runtime;
```

```
BEFORE: using Core.Neurons.Startup;
AFTER:  using Ino.Core.Neurons.Startup;
```

```
BEFORE: using Core.Neurons.Specialists;
AFTER:  using Ino.Core.Neurons.Specialists;
```

- [ ] **Step 3: Check for fully-qualified references**

Run: `git grep -n '\bCore\.Neurons\.' -- '*.cs' | grep -v '^.*:.*//\|"'`
Expected: any matches are in `nameof(...)` or documentation. For every match, rewrite `Core.Neurons.X` → `Ino.Core.Neurons.X`.

- [ ] **Step 4: Build to catch misses**

Run: `dotnet build ino.slnx 2>&1 | grep -E '^(error|Error)' | head -20`
Expected: zero errors. If you see `CS0246 type or namespace 'Core' not found`, a `using Core.Neurons;` was missed.

### Task B.4: Rename `Neurons.*` namespace declarations

- [ ] **Step 1: Enumerate affected files**

Run: `git grep -nE '^namespace Neurons(\.|;)' -- 'src/Neurons/**/*.cs'`

Expected sub-namespaces (full list from Task 0 investigation):
- `Neurons.Coding`, `Neurons.Coding.GitHub`, `Neurons.Coding.Models`, `Neurons.Coding.Prompts`, `Neurons.Coding.Tools`, `Neurons.Coding.Roslyn.Workspace`
- `Neurons.Genesis`
- `Neurons.Infrastructure`, `Neurons.System` (note: `Neurons.System` covers `FileSystemAgent`, `IFileSystem`, `IShell`, `ShellAgent`)
- `Neurons.LLM` (namespace `Neurons.Models`)
- `Neurons.Messages`
- `Neurons.Personal`
- `Neurons.Quality`
- `Neurons.Security`
- `Neurons.Synapse`

- [ ] **Step 2: Replace each declaration**

For each sub-namespace, rewrite `namespace Neurons.X;` → `namespace Ino.Neurons.X;`. Note that `src/Neurons/Infrastructure/` contains files split across `Neurons.Infrastructure`, `Neurons.System`, and `Neurons.Coding` — rename each according to its current decl, adding `Ino.` prefix.

- [ ] **Step 3: Verify**

Run: `git grep -nE '^namespace Neurons(\.|;)' -- '*.cs'`
Expected: empty output.

### Task B.5: Update every `using Neurons.*;` across the repo

- [ ] **Step 1: Enumerate**

Run: `git grep -n 'using Neurons\.' -- '*.cs' | wc -l`
Expected: approximately 24 sites.

- [ ] **Step 2: Rewrite each**

```
BEFORE: using Neurons.Coding;
AFTER:  using Ino.Neurons.Coding;
```

```
BEFORE: using Neurons.Coding.GitHub;
AFTER:  using Ino.Neurons.Coding.GitHub;
```

```
BEFORE: using Neurons.System;
AFTER:  using Ino.Neurons.System;
```

```
BEFORE: using Neurons.Models;
AFTER:  using Ino.Neurons.Models;
```

(etc. for every sub-namespace found in Task B.4)

- [ ] **Step 3: Rewrite the SynapseNeuron.cs template literal**

`src/Neurons/Synapse/SynapseNeuron.cs` has a large docstring that embeds a C# code template used by the LLM to generate orchestration scripts. The template contains hard-coded `using` statements that will execute against the runtime cluster — those must also be renamed, or the runtime-generated code will not compile.

```
BEFORE (inside the template):
        using System.Text.Json;
        using Aspire.IAW;
        using Core;
        using Core.Contracts;
        using Neurons.System;
        using Neurons.Coding;
        using Neurons.Models;

AFTER:
        using System.Text.Json;
        using Aspire.IAW;
        using Core;
        using Core.Contracts;
        using Ino.Neurons.System;
        using Ino.Neurons.Coding;
        using Ino.Neurons.Models;
```

Note: `Core` and `Core.Contracts` stay unchanged (Variant A keeps `Core.*` without the `Ino.` prefix).

- [ ] **Step 4: Build**

Run: `dotnet build ino.slnx 2>&1 | grep -E '^(error|Error)' | head -20`
Expected: zero errors.

### Task B.6: Full test matrix + commit

- [ ] **Step 1: Run tests**

```bash
dotnet test ino.slnx 2>&1 | tail -10
```

Expected: all green. Namespace renames are typically compile-time-only, but the SynapseNeuron runtime template (Task B.5 Step 3) is exercised by the NeuronML evolution tests, so watch those specifically.

- [ ] **Step 2: Behavioral verification via MCP**

Per the project's testing discipline (`CLAUDE.md` / `memory/feedback_always_test.md`), unit tests aren't enough. Drive one end-to-end check:

```bash
aspire start
# wait for dashboard healthy
```

Then via the iaw MCP server:

```
mcp__iaw__assistant_chat(content: "create a calculator at C:\\tmp\\calc and run it")
```

Watch the response and `agent_get_events`. The runtime-generated Roslyn script should still compile against the new `Ino.Neurons.*` namespaces. If it fails with `CS0246 type or namespace 'Neurons' not found`, Task B.5 Step 3 missed a template.

```bash
aspire stop
```

- [ ] **Step 3: Commit**

```bash
git add -A
git commit -m "refactor(namespaces): rename Core.Neurons -> Ino.Core.Neurons + Neurons.* -> Ino.Neurons.*

Applies the namespace convention from the original phase-3 plan text.
Variant A: literal rename of only the two namespaces the plan text
called out. Core.Contracts, Core.Registry, Core.AI, Core.Communication,
Core.Timeline, Core.Telemetry, Core.Services stay without the Ino.
prefix — mixed-prefix trade accepted per reviewer decision on PR #5
comment 4235077155.

Also updates the runtime orchestration template in SynapseNeuron.cs
so LLM-generated Roslyn scripts compile against the new namespaces."
```

### Task B.7: Push Part B and report

- [ ] **Step 1: Push**

```bash
git push origin prod-demo-2026-04-12
```

- [ ] **Step 2: Reply on the PR comment**

```bash
gh pr comment 5 --repo LeftTwixWand/ino --body "Part B landed (namespace Ino.* rename, Variant A):
- Core.Neurons -> Ino.Core.Neurons (20+ files)
- Neurons.* -> Ino.Neurons.* (~45 files across 11 sub-namespaces)
- Runtime orchestration template in SynapseNeuron.cs updated so LLM-generated scripts stay compilable

The mixed-prefix trade vs other Core.* namespaces is documented in the commit message. If you want Variant B (full Core.* -> Ino.Core.* rename), say the word — it's a separate PR because of churn volume (~400+ files)."
```

---

## Part C — Batch 6 Bot Consolidation (6a + 6b + 6c)

The comment's Section 1 says Batch 6 was deferred because it crosses three distinct concerns: 6a touches TripRadar domain source (Phase 4 functional work), 6b is an operational `setWebhook` call needing live infra, and 6c edits a nested-solution `.slnx` file with its own `CLAUDE.md`. You asked for Batch 6 on the PR branch anyway. This Part lays out the concrete steps; Task C.9 (webhook switch) is unavoidable manual operational work and is marked as such.

**What Batch 6 does:** merges `src/Telegram` (the ino bot, formerly `iaw/Telegram`) INTO `domains/travel/TripRadar/src/TripRadar.Bot` (the travel bot), yielding a single bot process that serves both the `/ino` command endpoint, the Flutter Telegram mini-app, the gRPC `InoService`, and TripRadar's existing reverse-proxy + Kafka notification pipeline. Then renames the merged project to `ino.Bot`, deletes `src/Telegram`, and updates both `ino.slnx` and `TripRadar.slnx`.

**Architectural concerns to surface before starting:**

1. **Domain boundary violation.** `TripRadar.Bot` lives under `domains/travel/TripRadar/` which has its own `CLAUDE.md` and is a nested solution. Adding ino-kernel dependencies (`Aspire.Client`, `Core`, `InoCommandDispatcher`) crosses that boundary. The clean architecture says domains should not know about the kernel — the kernel orchestrates domains. Merging them inverts that. **Unless you explicitly want the travel domain to own the shared bot process, Batch 6 should produce `ino.Bot` at `src/Bot/` (not under `domains/travel/TripRadar/src/`), with the travel-specific features moved in as a dependency. That is a more invasive rewrite and is outside the scope the comment described.**

2. **Two webhook setup paths exist.** `src/Telegram/WebhookSetupService.cs` and `domains/travel/TripRadar/src/TripRadar.Bot/Telegram/TelegramWebhookSetup.cs` both call `setWebhook` at startup. After consolidation only one can own the webhook route — decide which.

3. **Routes conflict.** `src/Telegram` uses `POST /webhook` (implicit from its own service). `TripRadar.Bot` uses `POST /api/telegram/webhook`. These are different routes so they can coexist in one process; Telegram only cares that the URL registered via `setWebhook` is valid. Decide which route is the "blessed" one for the consolidated bot.

4. **Static file root collision.** Both projects serve Flutter from a wwwroot. They must converge on one `wwwroot/` directory with a single `index.html`.

5. **gRPC service routing.** `src/Telegram` registers `MapGrpcService<TelegramClient.Services.InoService>()` with `.EnableGrpcWeb()`. `TripRadar.Bot` currently has no gRPC services. Merged project needs ASP.NET Kestrel configured for both HTTP/1.1 (YARP reverse proxy, static files, webhook) and HTTP/2 (gRPC) simultaneously.

Hand these five architecture concerns to the user before starting Task C.1. They are **not auto-resolvable** — they're product decisions. The rest of Part C assumes the user answers with:

- (1) **Merge into TripRadar.Bot in-place** (accept boundary violation as a demo-era trade-off)
- (2) **Use TripRadar.Bot's TelegramWebhookSetup**; delete src/Telegram/WebhookSetupService
- (3) **Use `/api/telegram/webhook`** as the blessed route; update ngrok + setWebhook to point there
- (4) **Merge wwwroot directories**; TripRadar.Bot already has MapFallbackToFile("index.html") so the Flutter UI wins if src/Telegram's wwwroot is strictly a subset
- (5) **Add Grpc.AspNetCore + Grpc.AspNetCore.Web** to TripRadar.Bot.csproj, register `Kestrel` with both protocols

### File Structure (Part C)

**Created:**
- `domains/travel/TripRadar/src/TripRadar.Bot/Ino/InoEndpoint.cs` — extracted `/ino` endpoint handler
- `domains/travel/TripRadar/src/TripRadar.Bot/Ino/AudioTranscriptionEndpoint.cs` — extracted `/ws/audio` handler
- `domains/travel/TripRadar/src/TripRadar.Bot/Ino/InoService.cs` — gRPC InoService (copy from src/Telegram/Services/InoService.cs)
- `domains/travel/TripRadar/src/TripRadar.Bot/Protos/ino.proto` — copy of src/Telegram/Protos/ino.proto
- `domains/travel/TripRadar/src/TripRadar.Bot/wwwroot/` — merged Flutter static files

**Modified:**
- `domains/travel/TripRadar/src/TripRadar.Bot/TripRadar.Bot.csproj` — add ProjectReferences to src/Core, src/Neurons, Aspire/ino.Client, Aspire/ino.Hosting, and src/Testing (testing only for test env wiring); add Grpc packages; register proto
- `domains/travel/TripRadar/src/TripRadar.Bot/Program.cs` — add `builder.AddIAWClient()` / `builder.AddIAWClientTesting()`, register `InoCommandDispatcher`, wire `MapGrpcService<InoService>().EnableGrpcWeb()`, mount `/ino` + `/ws/audio`
- `domains/travel/TripRadar/src/TripRadar.Bot/Telegram/TelegramEndpoints.cs` — mount new `/ino` and `/ws/audio` endpoints
- `Aspire/ino.AppHost/AppHost.cs` — replace `.AddProject<...>("telegram")` with a reference to `TripRadar.Bot`, reroute ngrok onto it, inject the iaw reference, delete old telegram resource wiring
- `Aspire/ino.AppHost/Aspire.csproj` — drop `src\Telegram\Telegram.csproj` ProjectReference, add `domains\travel\TripRadar\src\TripRadar.Bot\TripRadar.Bot.csproj`
- `ino.slnx` — drop `<Project Path="src/Telegram/Telegram.csproj" />`
- `domains/travel/TripRadar/TripRadar.slnx` — rename `TripRadar.Bot` → `ino.Bot` (if Task C.10 is in scope)
- `deployment/ino/Dockerfiles/telegram.Dockerfile` — retarget to new bot project path (or delete + replace with `ino-bot.Dockerfile`)

**Deleted:**
- `src/Telegram/` — entire directory (after verification that consolidation works)
- `deployment/ino/Dockerfiles/telegram.Dockerfile` — replaced by ino-bot.Dockerfile

### Task C.0: Architecture decision gate

Before touching code, get explicit user sign-off on the five concerns listed above. Paste them into a PR thread reply or ask inline. Do not proceed until you have answers.

- [ ] **Step 1: Ask user (or post thread reply on 4235077155)**

```
Five architecture decisions gate Part C:

1. Boundary: merge ino endpoints INTO domains/travel/TripRadar/src/TripRadar.Bot (accepts domain-owns-kernel inversion) OR lift TripRadar.Bot into src/Bot/ and make travel a dependency (clean but invasive)?
2. Which webhook setup wins: src/Telegram/WebhookSetupService.cs or domains/travel/TripRadar/src/TripRadar.Bot/Telegram/TelegramWebhookSetup.cs?
3. Which webhook route: / webhook or /api/telegram/webhook?
4. Flutter wwwroot: accept TripRadar.Bot's wwwroot as the new home, or vice versa?
5. gRPC hosting: add Grpc.AspNetCore + Grpc.AspNetCore.Web to TripRadar.Bot (lights up gRPC alongside YARP + Kafka)?

If the answer to (1) is "the second option", Part C must be rewritten — this plan assumes the first.
```

- [ ] **Step 2: Capture answers in the plan**

Edit this file (`docs/superpowers/plans/2026-04-13-pr5-self-review-fixes.md`) and append the answers under Task C.0 before starting C.1. Commit the plan edit — this is the load-bearing design record for future you.

### Task C.1: Pre-flight read of both bot processes

- [ ] **Step 1: Read every current-state file**

Read (full contents, not just heads):
- `src/Telegram/Program.cs`
- `src/Telegram/Services/InoService.cs`
- `src/Telegram/WebhookSetupService.cs`
- `src/Telegram/Protos/ino.proto`
- `src/Telegram/Telegram.csproj`
- `domains/travel/TripRadar/src/TripRadar.Bot/Program.cs`
- `domains/travel/TripRadar/src/TripRadar.Bot/Telegram/TelegramEndpoints.cs`
- `domains/travel/TripRadar/src/TripRadar.Bot/Telegram/TelegramWebhookSetup.cs`
- `domains/travel/TripRadar/src/TripRadar.Bot/TripRadar.Bot.csproj`
- `domains/travel/TripRadar/TripRadar.slnx`
- `Aspire/ino.AppHost/AppHost.cs` (to see Telegram resource wiring)

- [ ] **Step 2: Draft an integration sketch**

Write a 10-line bullet list of which services, endpoints, static files, and gRPC registrations need to move. This is not a deliverable — it's scratch for Task C.2. Save it to the plan or a scratch file; do not commit.

### Task C.2: Add kernel ProjectReferences + packages to TripRadar.Bot.csproj

Context7 verification first:

```
mcp__context7__resolve-library-id("dotnet/aspire")
mcp__context7__query-docs(id, topic: "Aspire.Client registration in hosted services")
```

Verify the current Aspire.Client AddIAWClient signature before wiring.

**Files:**
- Modify: `domains/travel/TripRadar/src/TripRadar.Bot/TripRadar.Bot.csproj`

- [ ] **Step 1: Add ProjectReferences**

Add inside the `<ItemGroup>` that holds existing `<ProjectReference>`:

```xml
<ProjectReference Include="..\..\..\..\..\Aspire\ino.Client\Aspire.Client.csproj" />
<ProjectReference Include="..\..\..\..\..\src\Core\Core.csproj" />
<ProjectReference Include="..\..\..\..\..\src\Neurons\Agents.csproj" />
<ProjectReference Include="..\..\..\..\..\features\timetravel\Timetravel.Core\Timetravel.Core.csproj" />
<ProjectReference Include="..\..\..\..\..\domains\travel\Ino.Travel\Ino.Travel.csproj" />
<ProjectReference Include="..\..\..\..\..\src\Testing\Testing.csproj" />
```

Verify the relative path depth: `domains/travel/TripRadar/src/TripRadar.Bot/` → repo root is 5 levels up (`..\..\..\..\..\`). Confirm with `realpath` if uncertain.

- [ ] **Step 2: Add Grpc packages**

```xml
<ItemGroup>
  <PackageReference Include="Grpc.AspNetCore" />
  <PackageReference Include="Grpc.AspNetCore.Web" />
</ItemGroup>
```

Central package management means versions come from `Directory.Packages.props` — verify both are declared there. If not, stop and add them.

- [ ] **Step 3: Register the ino.proto**

```xml
<ItemGroup>
  <Protobuf Include="Protos\ino.proto" GrpcServices="Both" />
</ItemGroup>
```

(The proto file itself is copied in Task C.4.)

- [ ] **Step 4: Verify TripRadar.Bot still builds in isolation**

Run: `dotnet build domains/travel/TripRadar/src/TripRadar.Bot/TripRadar.Bot.csproj 2>&1 | tail -10`

Expected: `Build FAILED` (because Protos\ino.proto doesn't exist yet). That's fine — continue to Task C.4.

### Task C.3: Copy the `/ino` endpoint handler

**Files:**
- Create: `domains/travel/TripRadar/src/TripRadar.Bot/Ino/InoEndpoint.cs`

- [ ] **Step 1: Read the current /ino handler**

Read `src/Telegram/Program.cs` fully. The `/ino` endpoint starts around line 115 (`app.MapPost("/ino", ...)`) and ends where the handler closes. Extract the body.

- [ ] **Step 2: Create `Ino/InoEndpoint.cs` with extracted handler**

```csharp
using Core.Neurons;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace TripRadar.Bot.Ino;

public static class InoEndpoint
{
    public static WebApplication MapInoEndpoint(this WebApplication app)
    {
        app.MapPost("/ino", async (HttpContext ctx, InoCommandDispatcher dispatcher) =>
        {
            var body = await ctx.Request.ReadFromJsonAsync<InoRequest>(ctx.RequestAborted);
            if (body is null || string.IsNullOrWhiteSpace(body.Script))
                return Results.BadRequest(new { ok = false, error = "missing 'script' field" });

            try
            {
                var output = await dispatcher.ExecuteScriptToStringAsync(body.Script, ctx.RequestAborted);
                return Results.Ok(new { ok = true, output });
            }
            catch (Exception ex)
            {
                return Results.Ok(new { ok = false, error = ex.Message });
            }
        });

        return app;
    }

    private sealed record InoRequest(string Script);
}
```

(Paste the full handler body from `src/Telegram/Program.cs` — the above is a template. The real handler may have extra fields, auth checks, etc. Copy verbatim, do not paraphrase.)

- [ ] **Step 3: Update namespace if Part B landed**

If Part B (namespace Ino.* rename) has already been committed, change `using Core.Neurons;` → `using Ino.Core.Neurons;` at the top of `InoEndpoint.cs`. Otherwise leave as-is.

### Task C.4: Copy gRPC InoService + proto

**Files:**
- Create: `domains/travel/TripRadar/src/TripRadar.Bot/Protos/ino.proto`
- Create: `domains/travel/TripRadar/src/TripRadar.Bot/Ino/InoService.cs`

- [ ] **Step 1: Copy the proto file**

```bash
cp src/Telegram/Protos/ino.proto domains/travel/TripRadar/src/TripRadar.Bot/Protos/ino.proto
```

- [ ] **Step 2: Copy the gRPC service implementation**

```bash
cp src/Telegram/Services/InoService.cs domains/travel/TripRadar/src/TripRadar.Bot/Ino/InoService.cs
```

- [ ] **Step 3: Rewrite the namespace inside InoService.cs**

The file currently uses `namespace TelegramClient.Services;` (matching `src/Telegram/Telegram.csproj`'s `<RootNamespace>TelegramClient</RootNamespace>`). In TripRadar.Bot, it should be `namespace TripRadar.Bot.Ino;`:

```
BEFORE: namespace TelegramClient.Services;
AFTER:  namespace TripRadar.Bot.Ino;
```

- [ ] **Step 4: Check using statements inside InoService.cs**

The file may use:
- `using Core.Neurons;` — keep (or rewrite to `Ino.Core.Neurons` if Part B landed)
- `using Core.Contracts;` — keep
- `using TelegramClient.Services;` — drop if self-reference
- Any `using Telegram.X;` — namespaces inside the old `Telegram` project — may need to be renamed or the code lifted

- [ ] **Step 5: Copy audio transcription endpoint**

`src/Telegram/Program.cs` has a `/ws/audio` WebSocket endpoint. Extract it the same way as Task C.3:

Create `domains/travel/TripRadar/src/TripRadar.Bot/Ino/AudioTranscriptionEndpoint.cs`:

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Net.WebSockets;

namespace TripRadar.Bot.Ino;

public static class AudioTranscriptionEndpoint
{
    public static WebApplication MapAudioTranscriptionEndpoint(this WebApplication app)
    {
        app.Map("/ws/audio", async (HttpContext context, IAudioTranscriptionService transcriber) =>
        {
            if (!context.WebSockets.IsWebSocketRequest)
            {
                context.Response.StatusCode = 400;
                return;
            }

            using var ws = await context.WebSockets.AcceptWebSocketAsync();
            // ... paste the rest from src/Telegram/Program.cs verbatim
        });

        return app;
    }
}
```

This brings `IAudioTranscriptionService` into TripRadar.Bot's DI graph. Resolve whether `FoundryLocalTranscriptionService` should also move (Task C.6).

### Task C.5: Wire the new endpoints in TripRadar.Bot/Program.cs

**Files:**
- Modify: `domains/travel/TripRadar/src/TripRadar.Bot/Program.cs`

- [ ] **Step 1: Add top-of-file using statements**

```csharp
using Aspire.IAW;
using Core.Neurons; // or Ino.Core.Neurons if Part B landed
using TripRadar.Bot.Ino;
```

- [ ] **Step 2: Add AddIAWClient to the builder**

After `builder.AddServiceDefaults()`:

```csharp
if (builder.Environment.EnvironmentName == "Testing")
    builder.AddIAWClientTesting();
else
    builder.AddIAWClient();
```

- [ ] **Step 3: Register InoCommandDispatcher**

After the other singleton registrations:

```csharp
builder.Services.AddSingleton<InoCommandDispatcher>(sp =>
    new InoCommandDispatcher(sp.GetRequiredService<IClusterClient>()));
```

- [ ] **Step 4: Register gRPC services**

```csharp
builder.Services.AddGrpc();
```

- [ ] **Step 5: Mount endpoints after `var app = builder.Build();`**

```csharp
app.UseWebSockets();
app.UseGrpcWeb();
app.MapGrpcService<TripRadar.Bot.Ino.InoService>().EnableGrpcWeb();
app.MapInoEndpoint();
app.MapAudioTranscriptionEndpoint();
```

These go BEFORE `app.MapTelegramEndpoints()` so gRPC + /ino are registered early in the route pipeline.

- [ ] **Step 6: Build TripRadar.Bot**

Run: `dotnet build domains/travel/TripRadar/src/TripRadar.Bot/TripRadar.Bot.csproj 2>&1 | tail -20`

Expected: build succeeds. If not, fix `using`-statement errors and missing symbol errors until it does. The most common failure is `IAudioTranscriptionService` not found — Task C.6 addresses that.

### Task C.6: Wire audio transcription services

The `IAudioTranscriptionService` interface and `FoundryLocalTranscriptionService` implementation live under `src/Telegram/Services/`. They need to land in TripRadar.Bot. Two options:

**Option 1 (lift to a new shared package):** create `src/AudioTranscription/AudioTranscription.csproj`, move the files, reference from both TripRadar.Bot and (anywhere else that needs them). Cleaner but higher churn.

**Option 2 (copy inline):** copy the files directly into `domains/travel/TripRadar/src/TripRadar.Bot/Ino/` and rename namespaces. Faster.

- [ ] **Step 1: Pick an option**

Recommendation: Option 2 for this PR (minimize churn), then revisit extraction in a follow-up if a second consumer appears.

- [ ] **Step 2: Copy the files**

```bash
cp src/Telegram/Services/AudioConverter.cs domains/travel/TripRadar/src/TripRadar.Bot/Ino/AudioConverter.cs
cp src/Telegram/Services/FoundryLocalTranscriptionService.cs domains/travel/TripRadar/src/TripRadar.Bot/Ino/FoundryLocalTranscriptionService.cs
# Also any supporting interfaces — grep src/Telegram for IAudioTranscriptionService, IAudioConverter, NoOpTranscriptionService
git grep -l 'IAudioTranscriptionService\|IAudioConverter\|NoOpTranscriptionService' src/Telegram/
```

- [ ] **Step 3: Rename namespaces in each copied file**

`namespace TelegramClient.Services;` → `namespace TripRadar.Bot.Ino;` (or whatever target namespace you chose).

- [ ] **Step 4: Add NuGet packages**

`FoundryLocalTranscriptionService` requires `Microsoft.AI.Foundry.Local`. `AudioConverter` requires `Concentus`, `Concentus.Oggfile`, `NAudio`. Add to TripRadar.Bot.csproj:

```xml
<PackageReference Include="Microsoft.AI.Foundry.Local" />
<PackageReference Include="Concentus" />
<PackageReference Include="Concentus.Oggfile" />
<PackageReference Include="NAudio" />
```

(Verify each package version is pinned centrally in `domains/travel/TripRadar/Directory.Packages.props` or the root `Directory.Packages.props`.)

- [ ] **Step 5: Register the services in Program.cs**

```csharp
builder.Services.AddSingleton<IAudioConverter, AudioConverter>();
builder.AddWhisperProvider<FoundryLocalTranscriptionService>(); // or whatever the ino.Client extension is called
```

- [ ] **Step 6: Build**

Run: `dotnet build domains/travel/TripRadar/src/TripRadar.Bot/TripRadar.Bot.csproj 2>&1 | tail -20`

Expected: clean build.

### Task C.7: Move Flutter wwwroot

**Files:**
- Create: `domains/travel/TripRadar/src/TripRadar.Bot/wwwroot/` (merged contents)

- [ ] **Step 1: Inspect current wwwroot(s)**

```bash
ls src/Telegram/wwwroot/
ls domains/travel/TripRadar/src/TripRadar.Bot/wwwroot/ 2>/dev/null || echo "no existing wwwroot"
```

- [ ] **Step 2: Copy src/Telegram wwwroot content**

```bash
cp -r src/Telegram/wwwroot/* domains/travel/TripRadar/src/TripRadar.Bot/wwwroot/
```

If both wwwroots exist with different `index.html` files, stop and get explicit user direction on which wins.

- [ ] **Step 3: Add `<Content Include="wwwroot\**\*" />` in TripRadar.Bot.csproj if needed**

Most Web SDK projects auto-include wwwroot; verify:

```bash
grep -A2 'wwwroot' domains/travel/TripRadar/src/TripRadar.Bot/TripRadar.Bot.csproj || echo "implicit wwwroot handling"
```

- [ ] **Step 4: Verify static files are served**

Run: `dotnet run --project domains/travel/TripRadar/src/TripRadar.Bot/TripRadar.Bot.csproj &` (brief dev test only — this is the ONE case where `dotnet run` is acceptable because we are not going through Aspire yet). Then:

```bash
curl -sSf http://localhost:5000/index.html | head -10
```

Kill the process. This confirms static files are served from the new location before we wire Aspire.

### Task C.8: Update Aspire/ino.AppHost/AppHost.cs to reference TripRadar.Bot

**Files:**
- Modify: `Aspire/ino.AppHost/AppHost.cs`
- Modify: `Aspire/ino.AppHost/Aspire.csproj`

- [ ] **Step 1: Read the current Telegram resource wiring**

Read `Aspire/ino.AppHost/AppHost.cs` and identify the `.AddProject<...>("telegram")` registration. Note its ngrok wiring, IAW reference injection, environment variable propagation, and any `WithReference(...)` calls.

- [ ] **Step 2: Replace with TripRadar.Bot resource**

Delete the old `.AddProject<Projects.Telegram>("telegram")...` block. Replace with a reference to the TripRadar.Bot project. Two approaches:

**Approach A — direct project reference.** Add to Aspire.csproj ProjectReferences:

```xml
<ProjectReference Include="..\..\domains\travel\TripRadar\src\TripRadar.Bot\TripRadar.Bot.csproj" />
```

Then in AppHost.cs:

```csharp
var inoBot = builder.AddProject<Projects.TripRadar_Bot>("ino-bot")
    .WithReference(iaw)
    .WithEnvironment("ASPNETCORE_ENVIRONMENT", "Development");
```

**Approach B — reuse TripRadar's own BotExtensions.** `domains/travel/TripRadar/src/Aspire/Hosting/Bot/BotExtensions.cs` already wires TripRadar.Bot into Aspire. Call that extension from ino.AppHost:

```csharp
// Requires ProjectReference to domains/travel/TripRadar/src/Aspire/Aspire.csproj OR package reference
var inoBot = builder.AddTripRadarBot("ino-bot", iaw);
```

**Decision:** Approach A. Approach B pulls in TripRadar's AppHost extension which may drag more resources than we want. Direct reference is clearer.

- [ ] **Step 3: Reroute ngrok**

The existing AppHost wires ngrok onto the `telegram` resource. Change it to wire onto `ino-bot`:

```csharp
var ngrok = builder.AddNgrok("ngrok")
    .WithTunnelEndpoint(inoBot, "https");
```

(Syntax depends on CommunityToolkit.Aspire.Hosting.Ngrok 13.1.2-beta.518 — verify via Context7.)

- [ ] **Step 4: Delete src/Telegram ProjectReference**

In `Aspire/ino.AppHost/Aspire.csproj`:

```
DELETE: <ProjectReference Include="..\..\src\Telegram\Telegram.csproj" />
```

- [ ] **Step 5: Build AppHost**

Run: `dotnet build Aspire/ino.AppHost/Aspire.csproj 2>&1 | tail -10`
Expected: clean build. `Projects.Telegram` type no longer resolves — confirm the old `.AddProject<Projects.Telegram>(...)` line was removed (Step 2) or the build will fail here.

### Task C.9: Operational — switch the webhook (manual step)

**This task requires running Aspire + a real bot token. If you don't have them, STOP and hand off to the user with a reminder of what's left.**

- [ ] **Step 1: Start Aspire**

```bash
aspire start
```

Wait for dashboard healthy at https://localhost:17280. Confirm the `ino-bot` resource shows Running.

- [ ] **Step 2: Find the new public URL**

Use Aspire MCP:

```
mcp__aspire__list_resources()
```

Find the ngrok tunnel URL attached to `ino-bot`. It should look like `https://abc-123.ngrok-free.app`.

- [ ] **Step 3: Set the webhook via Telegram API**

```bash
curl -X POST "https://api.telegram.org/bot<YOUR_BOT_TOKEN>/setWebhook" \
  -F "url=https://abc-123.ngrok-free.app/api/telegram/webhook"
```

(Use the route you chose in Task C.0 Step 2. If you picked `/webhook` instead, adjust.)

Expected response: `{"ok":true,"result":true,"description":"Webhook was set"}`.

- [ ] **Step 4: Verify webhook via getWebhookInfo**

```bash
curl "https://api.telegram.org/bot<YOUR_BOT_TOKEN>/getWebhookInfo"
```

Expected: `url` matches what you set, `pending_update_count` is low.

- [ ] **Step 5: Exercise the bot end-to-end**

Open Telegram, send `/ino timeline` to the bot. Expected: response comes back from the consolidated `ino-bot` process. Verify in Aspire Structured Logs that the request landed on `ino-bot` (not on `telegram`, which is now gone).

- [ ] **Step 6: Verify /ino endpoint independently**

```bash
curl -X POST https://abc-123.ngrok-free.app/ino \
  -H "Content-Type: application/json" \
  -d '{"script":"timeline"}'
```

Expected: `{"ok":true,"output":"..."}`.

- [ ] **Step 7: Verify Flutter mini app loads**

Open `https://abc-123.ngrok-free.app/` in a browser. Expected: the Flutter Telegram mini-app renders (CanvasKit, no DOM text).

- [ ] **Step 8: Stop Aspire**

```bash
aspire stop
```

### Task C.10: Delete src/Telegram and update ino.slnx

**Files:**
- Delete: `src/Telegram/` (entire directory)
- Modify: `ino.slnx`
- Delete/Modify: `deployment/ino/Dockerfiles/telegram.Dockerfile`

- [ ] **Step 1: Delete the directory**

```bash
git rm -r src/Telegram/
```

- [ ] **Step 2: Remove from ino.slnx**

Edit `ino.slnx` and delete the line referencing `src/Telegram/Telegram.csproj`. Verify the file still parses:

```bash
dotnet sln ino.slnx list 2>&1 | head -20
```

(`dotnet sln` works with `.slnx` in .NET 9+.)

- [ ] **Step 3: Replace or delete telegram.Dockerfile**

Option A — rename + retarget the Dockerfile:

```bash
git mv deployment/ino/Dockerfiles/telegram.Dockerfile deployment/ino/Dockerfiles/ino-bot.Dockerfile
```

Then rewrite its contents to publish `domains/travel/TripRadar/src/TripRadar.Bot/TripRadar.Bot.csproj` instead. Copy the structure from the updated telegram.Dockerfile (Part A Task A.6 Step 3) and change the csproj path.

Option B — delete the Dockerfile and let the user decide:

```bash
git rm deployment/ino/Dockerfiles/telegram.Dockerfile
```

Recommendation: Option A. Deleting leaves a dangling hole in the deployment pipeline.

- [ ] **Step 4: Build and test**

```bash
dotnet build ino.slnx 2>&1 | tail -10
dotnet test ino.slnx 2>&1 | tail -10
```

Expected: all green. If any test references `TelegramClient.*` namespaces, they'll fail — fix by re-importing from the new TripRadar.Bot namespace, OR by deleting the test if it was exclusive to the old Telegram project.

- [ ] **Step 5: Commit the deletion**

```bash
git add -A
git commit -m "refactor(bot): delete src/Telegram after consolidation into ino.Bot

Batch 6c cleanup: src/Telegram was superseded by the consolidated
TripRadar.Bot (now also serving /ino, /ws/audio, gRPC InoService,
and Flutter static files). ino.slnx and ino-bot.Dockerfile
updated accordingly."
```

### Task C.11: (Optional) Rename TripRadar.Bot → ino.Bot

This is the "rename to `ino.Bot`" part of the comment. It is **orthogonal** to the rest of Part C — the consolidation works with either name. Skip if the user prefers to keep the name `TripRadar.Bot`.

**Files:**
- Rename: `domains/travel/TripRadar/src/TripRadar.Bot/` → `domains/travel/TripRadar/src/ino.Bot/`
- Modify: `domains/travel/TripRadar/src/ino.Bot/TripRadar.Bot.csproj` → `domains/travel/TripRadar/src/ino.Bot/ino.Bot.csproj`
- Modify: `domains/travel/TripRadar/TripRadar.slnx` — project path update
- Modify: `Aspire/ino.AppHost/Aspire.csproj` — ProjectReference path update
- Modify: `Aspire/ino.AppHost/AppHost.cs` — `Projects.TripRadar_Bot` → `Projects.ino_Bot`
- Modify: `domains/travel/TripRadar/src/TripRadar.Bot.Tests/TripRadar.Bot.Tests.csproj` — assembly reference update

- [ ] **Step 1: Git mv the directory**

```bash
git mv domains/travel/TripRadar/src/TripRadar.Bot domains/travel/TripRadar/src/ino.Bot
git mv domains/travel/TripRadar/src/ino.Bot/TripRadar.Bot.csproj domains/travel/TripRadar/src/ino.Bot/ino.Bot.csproj
```

- [ ] **Step 2: Update root namespace in ino.Bot.csproj (optional)**

If you want the C# namespace to also rename, set `<RootNamespace>Ino.Bot</RootNamespace>` in the csproj and rewrite all `namespace TripRadar.Bot.X;` → `namespace Ino.Bot.X;`. This is a BIG rename. Recommendation: **skip the namespace rename**, keep `namespace TripRadar.Bot.*` for source stability, only the csproj/dll renames. The dll will be `ino.Bot.dll` but the types inside stay under `TripRadar.Bot.*`. Explicit note in the csproj:

```xml
<PropertyGroup>
  <AssemblyName>ino.Bot</AssemblyName>
  <RootNamespace>TripRadar.Bot</RootNamespace>  <!-- kept stable for source churn reasons -->
</PropertyGroup>
```

- [ ] **Step 3: Update TripRadar.slnx**

```
BEFORE: <Project Path="src/TripRadar.Bot/TripRadar.Bot.csproj" />
AFTER:  <Project Path="src/ino.Bot/ino.Bot.csproj" />
```

Note: `domains/travel/TripRadar/` is a **nested solution** with its own `CLAUDE.md`. Editing `TripRadar.slnx` from outside requires user awareness — the TripRadar maintainers may have opinions about project names landing in their solution. Flag this in the PR thread.

- [ ] **Step 4: Update Aspire/ino.AppHost references**

In `Aspire/ino.AppHost/Aspire.csproj`:

```
BEFORE: <ProjectReference Include="..\..\domains\travel\TripRadar\src\TripRadar.Bot\TripRadar.Bot.csproj" />
AFTER:  <ProjectReference Include="..\..\domains\travel\TripRadar\src\ino.Bot\ino.Bot.csproj" />
```

In `Aspire/ino.AppHost/AppHost.cs`:

```
BEFORE: builder.AddProject<Projects.TripRadar_Bot>("ino-bot")
AFTER:  builder.AddProject<Projects.ino_Bot>("ino-bot")
```

(The generated `Projects` class name is derived from the csproj filename: `TripRadar.Bot.csproj` → `Projects.TripRadar_Bot`, `ino.Bot.csproj` → `Projects.ino_Bot`. Dots become underscores.)

- [ ] **Step 5: Update TripRadar.Bot.Tests**

```bash
git mv domains/travel/TripRadar/src/TripRadar.Bot.Tests/TripRadar.Bot.Tests.csproj domains/travel/TripRadar/src/ino.Bot.Tests/ino.Bot.Tests.csproj
# Actually — keep the test project named TripRadar.Bot.Tests since it tests TripRadar-specific features;
# only the bot process has been renamed. Revisit if the test suite is covering the ino portions too.
```

- [ ] **Step 6: Build + test**

```bash
dotnet build ino.slnx 2>&1 | tail -10
dotnet build domains/travel/TripRadar/TripRadar.slnx 2>&1 | tail -10
dotnet test ino.slnx 2>&1 | tail -10
```

Expected: all green.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor(bot): rename TripRadar.Bot -> ino.Bot (AssemblyName only)

Batch 6c final rename. The dll is now ino.Bot.dll, but source
namespaces stay TripRadar.Bot.* for stability. TripRadar.slnx,
Aspire.csproj, and AppHost.cs updated to new path."
```

### Task C.12: Push Part C and report

- [ ] **Step 1: Push**

```bash
git push origin prod-demo-2026-04-12
```

- [ ] **Step 2: Reply on the PR comment**

```bash
gh pr comment 5 --repo LeftTwixWand/ino --body "Part C landed (Batch 6 bot consolidation):
- 6a: /ino, /ws/audio, gRPC InoService, Flutter wwwroot merged into TripRadar.Bot
- 6b: setWebhook switched to new ngrok tunnel at /api/telegram/webhook, verified end-to-end
- 6c: src/Telegram deleted, ino.slnx cleaned up, telegram.Dockerfile retargeted to ino-bot.Dockerfile
- Optional rename to ino.Bot: [landed / skipped — state your choice]

Architecture decisions captured in docs/superpowers/plans/2026-04-13-pr5-self-review-fixes.md Task C.0."
```

---

## Execution Order Summary

| Part | Effort | Risk | Ships independently |
|---|---|---|---|
| **0 — Audit** | 30 min | None | Yes (no-op commit) |
| **A — Section 5 Quick Wins** | 2-4 hours | Low | Yes |
| **B — Namespace Ino.* rename (Variant A)** | 3-5 hours | Medium (mass edit) | Yes |
| **C — Batch 6 bot consolidation** | 1-2 days | High (cross-domain, operational) | Yes, but requires Part A first to fix Dockerfiles |

**Recommended execution order:** 0 → A → (B in parallel with C) → merge all into PR #5.

**Blocker between Parts:** none, except C depends on A's Dockerfile rewrite (Task A.6) because the new `ino-bot.Dockerfile` template references the src/ layout.

## Known plan gaps (to resolve during execution)

- **NETSDK1047 diagnosis is speculative.** Task A.7 has three candidate causes and a fallback. If none apply, the executor must escalate before bumping the global.json SDK version.
- **Part C Task C.6 audio transcription option is not pre-verified.** Foundry Local may have Linux compatibility issues that force Task C.6 to stay Windows-only. Verify via `dotnet publish -r linux-x64` before assuming the consolidation works in the Docker image.
- **Part C Task C.11 renaming is orthogonal.** If you skip C.11, the AssemblyName stays `TripRadar.Bot` — that is not wrong, just inconsistent with the comment's "rename to `ino.Bot`" phrasing. Explicit skip is valid.
- **Part B Variant B (full-prefix) is out of scope.** If the user wants fully consistent `Ino.*` prefixes across every `Core.*` namespace, stop and rewrite Part B. The current plan only implements Variant A.
