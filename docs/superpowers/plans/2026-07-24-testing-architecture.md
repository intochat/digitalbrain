# Testing Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the process-global simulation and ad-hoc HostTests harness with a deep, production-aligned `DigitalBrain.Testing` product — typed method-scoped `Scenario` on an assembly-owned multi-silo cluster (L1), exclusive Aspire fixture (L2), edge-only substitutes, controllable clock, closed faults, always-on failure artifacts, and Gherkin as a first-class thin authoring/generation surface — then make PR #39 framework CI green for explained reasons, without even-engineering.

**Architecture:** One testing product mirrors the DigitalBrain product: real kernel and modules, AppHost composition for hosting claims, substitute only true external edges. L1 is the default proof depth for module semantic/durable behavior; L2 is only for hosting, process restart, endpoints, and multi-resource composition. Behavior ≠ Neuron; southbound MCP ≠ AI module. No parallel fake runtime.

**Tech Stack:** .NET 10, xUnit v3, Orleans 10.2.2-rc.2 TestingHost / InProcessTestCluster, Aspire.Hosting.Testing 13.4.6, Reqnroll 3.3.4 (thin over Scenario), Microsoft.Testing Platform via existing test projects.

**Branch / PR:** `agent/lean-architecture-final` @ `2c3aa4e5` — update PR #39 only; do not open a replacement PR; do not merge.

**Baseline recorded at plan write:**

```text
git rev-parse HEAD
2c3aa4e5ef8268b2e6c0e69aade1f003393e5fe0

git merge-base origin/master HEAD
312ee5993b2b0c4e3e2a145c6f8205f5c1058465

git status --porcelain
(empty)

Worktree: E:\intochat\digitalbrain\.worktrees\lean-architecture-implementation
```

---

## 0. Locked decisions (grilling complete)

| # | Decision |
|---|---|
| **1** | L1 cluster: **assembly-owned** multi-silo + mandatory per-scenario isolation/leak contracts |
| **2** | **L1 default** for module semantic/durable proofs; **L2** only for hosting/restart/endpoints/multi-resource composition; L0 shape; L3 surface never owns domain truth |
| **3** | L1 isolation unit: **method-scoped typed `Scenario`** |
| **4** | L2: **exclusive** hosted fixture + method-scoped use/leak checks; no parallel full AppHosts |
| **5** | Substitute **only** at true external edges (closed list): `IChatClient`, southbound MCP transport, OAuth/params, shared `TimeProvider` registration — never fake neurons/journals/filters |
| **6** | **Mandatory controllable clock** on every L1 Scenario |
| **7** | **Scenario-scoped closed durability fault catalog** (journal commit after N, host silo restart, grow only with real second consumer) |
| **8** | **Gherkin is core** as authoring/generation surface; steps stay thin over typed Scenario; generation may only compose existing vocabulary |
| **9** | **Always-on structured failure artifacts** on Scenario failure/timeout |
| **10** | **One public packable** `DigitalBrain.Testing` (dev-only), package quality |
| **11** | **Full L0+L1+L2 merge-blocking**; stress/shuffle loops are additive only; no skip/quarantine/timeout inflation |

**Laws:** Vision-first; no even-engineering; Behavior is not a Neuron subtype (architecture §5); `DigitalBrain.Integrations.Mcp` is not part of AI; do not push speculative work; temporary diagnostics for hang diagnosis must be removed before commit.

---

## 1. Current-state diagnosis

### 1.1 CI failure dossier (run `30055807730`, SHA `2c3aa4e5`)

| Attempt | Simulations | HostTests | Tests |
|---|---|---|---|
| **1** | 244/244 pass | **2 fail** (probe HTTP 100s after Healthy) | 212/212 |
| **2** | **1 fail** / 243 pass (delegation timeout 30s) | **2 fail** (same HostedRestart pair) | 212/212 |

**Dossier 1 —** `CapabilityDelegationSecurityContracts.DelegatedCallPreservesCommittedCausalRequestAndCompletesExactlyOnce` @ line ~1105: `runner.InvokeAsync` never returns; Orleans reports activation Valid, `IsExecuting=True`, queue empty, work group Waiting. Path: `DelegatedRunner` → `DigitalBrainRuntime.InvokeAsync` → redeem → target enter (journal + **caller journal read** + emit) → finish. Process-global static observation dictionaries; shared 3-silo cluster; assembly serial.

**Dossier 2 —** `HostedRestart.TheOrleansDashboardIsServedInDevelopment` and `ADurableTurnAndDeliverySurviveAKernelRestart`: after `WaitForResourceHealthyAsync("probe")`, `CreateHttpClient("probe")` hangs on transport (`InitialFillAsync`). No HostTests parallelization disable; each fact can start a full AppHost.

### 1.2 Testing substrate defects (not symptoms)

| Defect | Evidence |
|---|---|
| Static process singleton cluster | `SimulationCluster` `_cluster` with lazy `StartAsync`, weak dispose story |
| Process-global probes/gates | Dozens of `ConcurrentDictionary` statics in Simulations (AIWorker, Delegation, TaskLifecycle, …) |
| Magic strings | `NeuronSteps` / `Simulation` neuron type names as `{word}` |
| Wall-clock sleeps | `Task.Delay` settle paths; shortened reminders still wall-bound |
| Fault gates not scenario-scoped | `FailJournalWriteAfter` on shared provider |
| HostTests ownership absent | Parallel AppHosts legal; process cleanup by name in one test |
| Hang diagnostics absent | Only Orleans timeout text |
| Gherkin dual runtime risk | Steps reimplement string catalogs instead of a typed engine |
| Packable but shallow public surface | `IsPackable=true` over statics |

### 1.3 What is *not* claimed yet

Which await in the delegation path stalls is **unknown** until a red-capable loop + stage diagnostics exist. Do not “fix” with timeouts, retries, or serializing HostTests without ownership proof. Do not claim Orleans/Aspire upstream defect without a minimized first-party-only repro.

---

## 2. Historical framework comparison (read-only corpus)

| Lineage | Interface / lifecycle | Verdict |
|---|---|---|
| **v3 Simulation + Substrate** | Per-class `IAsyncLifetime`, real single silo, Fire/Expect | Deep enough for stream era; no multi-silo/journal durability |
| **ino fixtures** | Collection/multi-silo/AppHost fixtures, explicit ownership | **Best lifecycle pattern** — adopt ownership ideas, not code |
| **ino NeuronTesting + Bdd** | AppHost + Playwright; mode in `TAppHost` type | L3/surface pattern later; heavy |
| **IAW AgentTest** | Per-class TestCluster; mock only at chat edge | Correct edge discipline |
| **final Simulation factory** | Thin StartAsync | Shallow helper |
| **self-improving SimulationContext** | Stub always-green | **Collapsed** — forbid |
| **v4 Sdk.Testing** | Empty packable shell | **Collapsed** — forbid consumerless packages |

**Deletion test:** Fixture-owned real cluster + edge-only doubles earned keep; process-global probes and stub contexts did not.

---

## 3. Selected framework design

### 3.1 Tiers

```text
L0  Compiler/shape     DigitalBrain.Tests (contracts, packages, generators)
L1  Kernel simulation  assembly multi-silo + method Scenario (+ Gherkin)
L2  Hosted OS          exclusive Aspire fixture + method handle
L3  Surface (later)    Flutter/web client of same brain — out of this plan's delivery
```

### 3.2 Public interface (target shape — implement exactly in tasks)

```csharp
// L1 — author surface
public sealed class Scenario : IAsyncDisposable
{
    public OwnerId Owner { get; }
    public TimeProvider Clock { get; }
    public IGrainFactory Grains { get; }

    public NeuronRef<TNeuron> Neuron<TNeuron>(string name) where TNeuron : INeuron;
    public Task StimulateAsync<TSynapse>(NeuronRef target, TSynapse synapse) where TSynapse : Synapse;
    public Task<JournalRead> ReadJournalAsync(NeuronRef neuron, JournalKind kind, long afterSequence = 0);
    public Task WatchAsync(NeuronRef neuron, JournalKind kind, long afterSequence, IJournalObserver observer);
    public FaultHandle Arm(FaultPoint point);           // closed catalog
    public Task RestartHostOfAsync(NeuronRef neuron);
    public void AdvanceClock(TimeSpan delta);
    // dispose => disarm faults, drop probes/refs, leak assert, attach artifact on failure
}

public static class Simulations
{
    // assembly fixture entry — tests call:
    public static Task<Scenario> OpenAsync(CancellationToken cancellationToken = default);
}

// Closed faults (initial)
public abstract record FaultPoint;
public sealed record JournalCommitAfter(NeuronRef Neuron, int CompletedWritesBeforeFailure, string Message) : FaultPoint;
// Host restart is an operation, not a sticky fault: Scenario.RestartHostOfAsync

// L2
public sealed class HostedApplication : IAsyncDisposable { /* exclusive; CreateScenario-like handle */ }
public sealed class HostedScenario : IAsyncDisposable
{
    public HttpClient CreateHttpClient(string resourceName);
    public Task WaitHealthyAsync(string resourceName, CancellationToken ct);
    public Task RestartResourceAsync(string resourceName, CancellationToken ct);
}
```

Names may be adjusted for clarity but **must** preserve: method-scoped Scenario, assembly cluster, exclusive L2, closed faults, clock, always-on failure artifact.

### 3.3 Lifecycle

| Layer | Lifetime | Isolation | Cleanup |
|---|---|---|---|
| L1 cluster | Assembly / process (xUnit collection or assembly fixture) | Shared topology | Stop on assembly teardown |
| L1 Scenario | One test method (or one Gherkin scenario) | Typed owner namespace + scenario registries | Dispose: leak assert + wipe |
| L2 graph | Exclusive fixture (serial collection) | One AppHost at a time | Dispose app + process/resource leak assert |
| L2 method handle | One test method | Fresh clients; no shared mutable probes | Dispose handle |

### 3.4 Gherkin

- Core authoring/generation surface for fabric and future OS specs.
- Every step calls Scenario APIs only.
- Bindings live in `DigitalBrain.Testing` (or thin partials); string neuron names in features migrate toward typed catalog helpers where generation needs stability.
- Reqnroll remains a package dependency of Testing (Decision 10) with documentation that the package is development-only.

### 3.5 Rejected designs (explicit)

1. Per-test cluster redeploy as default (cost destroys gate).
2. L2-default for all module proofs (Decision 2).
3. Contract-level NSubstitute of `IGmail`/`ITask` as SUT (Decision 5).
4. Behavior : Neuron as test base class (architecture contradiction).
5. Open string fault hooks (Decision 7).
6. Opt-in-only diagnostics (Decision 9).
7. Multi empty testing packages (v4 failure).
8. Timeout/retry/skip as fix (prohibited).

---

## 4. Keep / delete / deepen / move

| Item | Decision | Notes |
|---|---|---|
| `DigitalBrain.Testing` project | **Deepen** | Becomes the product; rewrite public surface |
| `InProcessTestCluster` 3-silo real kernel | **Keep** | Behind assembly fixture |
| `RecordingJournalStorageProvider` | **Deepen** | Scenario-scoped arming only |
| `VolatileReminderTable` / spoof reminder | **Keep/deepen** | Wire through clock where possible |
| `SimulationCluster` static API | **Delete** | After migration |
| `Simulation` stringly API | **Delete or reduce** to Scenario internals |
| `NeuronSteps` thick string catalog | **Rewrite** thin Gherkin → Scenario |
| Process-global `ConcurrentDictionary` probes in Simulations | **Delete** | Replace with scenario-scoped observations or journal asserts |
| `McpTestDoubles` protocol double | **Keep** | Edge substitute (Decision 5) |
| HostTests per-fact AppHost free-for-all | **Replace** | Exclusive L2 fixture |
| `GetProcessesByName("DigitalBrain.ProbeHost")` as primary cleanup | **Replace** | Fixture-owned process tracking + assert |
| L0 contract tests | **Keep** | May gain Testing public API contracts |
| Behavior install rail | **Out of scope** | Designed not built; Scenario must leave a seam for future `Install` without implementing rail |
| Integrations.Mcp under AI | **Do not move** | Architecture |
| Repo-wide folder restructure | **Out of scope for hang fix** | Optional later slice if required for package clarity only |

### Exact expected deletions (end state)

- Public static `SimulationCluster.StartAsync/StopAsync/Grains/FailJournalWriteAfter/...` author surface
- Author use of static observation types (`DelegatedTargetCommitObservations`, `AIWorker*Gate`, `ScriptedWorker` static counters, etc.) — either deleted or moved to scenario-scoped test helpers that cannot outlive Scenario
- `Task.Delay` as primary wait in Simulations (replace with condition + clock/observe)
- Parallel unrestricted HostTests AppHost starts

---

## 5. Migration map by suite

| Suite | Tier | Migration |
|---|---|---|
| `tests/DigitalBrain.Tests` | L0 (+ light Testing self-tests) | Keep; add contracts for Scenario public surface / packable Testing; no cluster required for pure L0 |
| `tests/DigitalBrain.Simulations` `*.feature` + steps | L1 Gherkin | Open Scenario per scenario; rewrite steps |
| `tests/DigitalBrain.Simulations` capability/delegation/request/causal | L1 | Method Scenario; delete static probes; journal-first asserts |
| `tests/DigitalBrain.Simulations` TaskLifecycle / AI* / MCP / enrichment | L1 | Same; edge doubles only via framework registration hooks |
| `tests/DigitalBrain.Simulations` multi-silo / durability features | L1 | `RestartHostOfAsync` via Scenario |
| `tests/DigitalBrain.HostTests` | L2 | Exclusive collection fixture; method handles; leak assert |
| `docs/tests` | docs | Unchanged unless architecture text updated for testing vision |
| samples | not gate | Optional later Scenario examples — only if a consumer needs them this PR |

---

## 6. Failure and cleanup invariants (framework self-tests)

Every merge-blocking run must preserve:

1. **Scenario dispose disarms all faults** — leftover arm fails the test.
2. **Scenario dispose asserts no scenario-owned object references / gates left.**
3. **Failure artifact present** when Scenario throws or times out (schema contract).
4. **No wall-clock sleep API** on public Scenario surface.
5. **L2 exclusive** — two concurrent hosted starts in-process must be refused or serialized by fixture.
6. **L2 dispose** — no orphan probe/silo processes from that fixture’s tracked set.
7. **Gherkin steps** only call public Scenario APIs (architectural test via analyzer or source inspection of bindings assembly).

---

## 7. Diagnosis before “fix” (mandatory slice 0)

Per `$diagnosing-bugs`: no production behavior change until red-capable loop exists and stages discriminate.

### Hypotheses (still ranked)

**L1 hang:** H1 shared residue → H2 activation cycle (target↔issuer) → H3 scheduler starvation → H4 context loss → H5 Orleans upstream.

**L2 HTTP:** H1 concurrent AppHosts → H2 stale endpoint → H3 probe hang → H4 process leakage.

### Required loops (run, quote output, before kernel “fixes”)

```text
# From worktree root; adjust configuration as needed
dotnet test tests/DigitalBrain.Simulations -c Release --filter "FullyQualifiedName~DelegatedCallPreservesCommittedCausalRequestAndCompletesExactlyOnce" --logger "console;verbosity=detailed"
# Repeat N>=10 for flake rate

dotnet test tests/DigitalBrain.Simulations -c Release --logger "console;verbosity=minimal"
# Full L1

dotnet test tests/DigitalBrain.HostTests -c Release --logger "console;verbosity=detailed"
# Full L2; then with forced serial if measuring H1
```

Temporary stage tags (unique prefix e.g. `[DBG-DEL-20260724]`) along redeem/enter/finish — **remove before any commit**.

---

## 8. File plan (target)

### Create

| Path | Role |
|---|---|
| `src/DigitalBrain.Testing/Scenario.cs` | Method-scoped L1 handle |
| `src/DigitalBrain.Testing/Simulations.cs` | OpenAsync + assembly cluster access |
| `src/DigitalBrain.Testing/Cluster/SimulationClusterHost.cs` | Assembly-owned cluster (internal) |
| `src/DigitalBrain.Testing/Cluster/ScenarioClock.cs` | Controllable TimeProvider |
| `src/DigitalBrain.Testing/Faults/FaultPoint.cs` | Closed catalog |
| `src/DigitalBrain.Testing/Faults/ScenarioFaults.cs` | Arm/disarm |
| `src/DigitalBrain.Testing/Diagnostics/ScenarioFailureArtifact.cs` | Always-on artifact |
| `src/DigitalBrain.Testing/Diagnostics/ScenarioStages.cs` | Fixed stage names |
| `src/DigitalBrain.Testing/Gherkin/ScenarioSteps.cs` | Thin Reqnroll bindings |
| `src/DigitalBrain.Testing/Hosting/HostedApplicationFixture.cs` | Exclusive L2 |
| `src/DigitalBrain.Testing/Hosting/HostedScenario.cs` | Method L2 handle |
| `tests/DigitalBrain.Testing.Tests/` **or** self-tests inside `DigitalBrain.Tests` | Framework contracts (prefer `DigitalBrain.Tests` if one less project is enough — **prefer no new project** unless isolation requires it) |

### Rewrite / migrate

| Path | Role |
|---|---|
| All `tests/DigitalBrain.Simulations/*.cs` | Scenario API |
| All `tests/DigitalBrain.Simulations/*.feature` | Keep prose; steps via new bindings |
| `tests/DigitalBrain.HostTests/*` | Exclusive L2 |
| `src/DigitalBrain.Testing/NeuronSteps.cs` | Replace with thin steps |
| `src/DigitalBrain.Testing/SimulationCluster.cs` | Internalize then delete public static surface |

### Delete when unused

| Path |
|---|
| Public static probe types embedded in simulation files (inline deletions) |
| Obsolete `Simulation.cs` public string API once zero callers |

### Docs

| Path | Role |
|---|---|
| `docs/architecture.md` | Short “Testing” subsection: tiers, Scenario, Gherkin-as-generation surface, edge substitutes — **only after behavior is proven** |
| This plan | Living checklist |

---

## 9. Tasks (red/green slices)

Each task ends with: focused tests green → commit with five grill answers → no push until slice intentional.

### Task 0: Reproduce and instrument (no product fix)

**Files:** temporary diagnostics only under worktree; must end clean.

- [ ] **Step 0.1** Record `git rev-parse HEAD` and `git status --porcelain`.
- [ ] **Step 0.2** Run isolated delegation test ≥10×; record pass/fail rate.
- [ ] **Step 0.3** Run full Simulations once; record.
- [ ] **Step 0.4** Run HostTests once serial vs default; record.
- [ ] **Step 0.5** Add temporary `[DBG-DEL-…]` stage logs on redeem/enter/caller-read/emit/finish; capture one hang or prove non-repro locally.
- [ ] **Step 0.6** Remove all temporary diagnostics; tree clean.
- [ ] **Step 0.7** Write findings into this plan §1 or a short `docs/superpowers/plans/2026-07-24-testing-architecture-findings.md` **only if** durable; else keep in commit messages of later fixes.

**Commands:**

```text
dotnet test tests/DigitalBrain.Simulations -c Release --filter "FullyQualifiedName~DelegatedCallPreservesCommittedCausalRequestAndCompletesExactlyOnce" --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.Simulations -c Release --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.HostTests -c Release --logger "console;verbosity=minimal"
```

**Commit:** none required if no durable file; if findings file, `docs: record testing failure diagnosis evidence`.

---

### Task 1: Framework skeleton — Scenario + assembly cluster (failing self-tests first)

**Files:**
- Create: `src/DigitalBrain.Testing/Scenario.cs`, `Simulations.cs`, `Cluster/SimulationClusterHost.cs`, `Cluster/ScenarioClock.cs`
- Modify: `src/DigitalBrain.Testing/DigitalBrain.Testing.csproj` as needed
- Test: `tests/DigitalBrain.Tests/TestingFrameworkContracts.cs` (new)

- [ ] **Step 1.1** Write failing tests:
  - `OpenAsync` returns Scenario with unique Owner
  - Second open in parallel (if allowed) gets different Owner
  - Dispose of Scenario does not stop assembly cluster (second Open works)
  - Public API has no `SimulationCluster` statics required for Open
- [ ] **Step 1.2** Run `dotnet test tests/DigitalBrain.Tests -c Release --filter "FullyQualifiedName~TestingFramework" --logger "console;verbosity=minimal"` — expect FAIL.
- [ ] **Step 1.3** Implement minimal assembly host + Scenario open/dispose (cluster start once).
- [ ] **Step 1.4** Run tests — expect PASS for new contracts; existing Tests still pass.
- [ ] **Step 1.5** Commit.

```text
git add src/DigitalBrain.Testing tests/DigitalBrain.Tests/TestingFrameworkContracts.cs
git commit -m "$(cat <<'EOF'
feat(testing): add assembly-owned cluster and method Scenario skeleton

What was added without a current consumer? Only framework self-tests as consumer.
What was claimed without verification? Nothing; TestingFrameworkContracts ran.
What changed outside the intended slice? None.
Which original failure does this diagnose/prevent? Isolation ownership for L1 residue (H1 setup).
What cleanup remains? Full suite still on old SimulationCluster until migration.
EOF
)"
```

---

### Task 2: Controllable clock + forbid public sleep API

**Files:** `ScenarioClock.cs`, `Scenario.cs`, `TestingFrameworkContracts.cs`

- [ ] **Step 2.1** Failing tests: Scenario.Clock is not `TimeProvider.System` by default; `AdvanceClock` advances scenario time; grains constructed after open observe advanced time for kernel timestamp path (compiler/runtime probe as needed).
- [ ] **Step 2.2** Implement registration of clock into silo services for new activations as far as Orleans composition allows; document any wall-clock reminder residual honestly in test DisplayName if incomplete.
- [ ] **Step 2.3** Green + commit.

---

### Task 3: Closed faults + leak assert on dispose

**Files:** `Faults/*`, `RecordingJournalStorageProvider.cs` (internalize), contracts

- [ ] **Step 3.1** Failing tests: arm journal fault → write fails after N; dispose without disarm fails test; dispose after natural completion succeeds; static `FailJournalWriteAfter` not required.
- [ ] **Step 3.2** Implement scenario-scoped fault table.
- [ ] **Step 3.3** Green + commit.

---

### Task 4: Always-on failure artifacts + fixed stages

**Files:** `Diagnostics/*`, contracts

- [ ] **Step 4.1** Failing test: when Scenario operation times out or assert helper fails, artifact contains stage timeline + owner + armed faults.
- [ ] **Step 4.2** Instrument Scenario public operations with stage marks.
- [ ] **Step 4.3** Green + commit.

---

### Task 5: Thin Gherkin bindings on Scenario

**Files:** `Gherkin/ScenarioSteps.cs`, migrate features one-by-one starting `Durability.feature` / `Journals.feature`

- [ ] **Step 5.1** Hook Reqnroll to open/dispose Scenario per scenario.
- [ ] **Step 5.2** Port steps for durability + journals; run feature filter green.
- [ ] **Step 5.3** Port remaining features; delete old `NeuronSteps` when zero callers.
- [ ] **Step 5.4** Commit per green feature group if large.

**Command:**

```text
dotnet test tests/DigitalBrain.Simulations -c Release --filter "Durability" --logger "console;verbosity=minimal"
```

---

### Task 6: Migrate capability delegation suite + fix root cause if proven

**Files:** `tests/DigitalBrain.Simulations/CapabilityDelegationSecurityContracts.cs`, kernel files **only if** Task 0+artifacts prove a kernel bug

- [ ] **Step 6.1** Migrate to Scenario; delete static `DelegatedTarget*Observations` / gates in favor of journal + scenario watchers.
- [ ] **Step 6.2** Establish red-capable loop if hang still present; use artifact stages (not ad-hoc forever logs).
- [ ] **Step 6.3** If H2 cycle/deadlock proven: implement minimal kernel/runtime fix with regression test that fails without fix.
- [ ] **Step 6.4** If H1 only: migration+leak asserts sufficient; document evidence.
- [ ] **Step 6.5** Repeat isolated test ≥20× and full Simulations ≥2×.
- [ ] **Step 6.6** Commit.

**Prohibited in this task:** increasing Orleans response timeout; `[Fact(Skip=…)]`; retry loops.

---

### Task 7: Migrate remaining Simulations to Scenario

**Order (suggested):** Capability* → Delivery* → Journal* → Watch → TaskLifecycle → CentralMcp/Mcp doubles → AI* → AccountEnrichment → fabric neurons.

- [ ] **Step 7.1** For each file group: migrate, delete static probes, green focused, commit.
- [ ] **Step 7.2** After last group: delete public `SimulationCluster` / dead `Simulation` APIs.
- [ ] **Step 7.3** Full Simulations green twice.

**Command:**

```text
dotnet test tests/DigitalBrain.Simulations -c Release --logger "console;verbosity=minimal"
```

---

### Task 8: Exclusive L2 fixture + HostTests migration

**Files:** `Hosting/*`, `tests/DigitalBrain.HostTests/*`, `AssemblyInfo`/collection definitions

- [ ] **Step 8.1** Failing test: second concurrent hosted start blocked or serialized by fixture contract.
- [ ] **Step 8.2** Implement exclusive fixture + HostedScenario.
- [ ] **Step 8.3** Migrate HostedBrain, HostedRestart, Topology, ProductionAppHost.
- [ ] **Step 8.4** Replace broad process-name cleanup with tracked dispose + assert.
- [ ] **Step 8.5** Diagnose remaining HTTP hangs with L2 artifact (endpoints, resource state); fix root cause (readiness, endpoint selection, probe) — **not** blind serial without fixture.
- [ ] **Step 8.6** HostTests green ≥2×.

```text
dotnet test tests/DigitalBrain.HostTests -c Release --logger "console;verbosity=minimal"
```

---

### Task 9: Edge-substitute registration hooks (MCP + chat)

**Files:** Testing DI hooks; `McpTestDoubles.cs` adaptation; AI tests that currently use statics

- [ ] **Step 9.1** Document closed edge list in Testing public docs comment or architecture subsection.
- [ ] **Step 9.2** Ensure MCP protocol double and chat doubles register only via Scenario/cluster configuration API.
- [ ] **Step 9.3** Grep for `NSubstitute` / fake `INeuron` in tests — must be zero for SUT paths.

---

### Task 10: Package surface + architecture doc

**Files:** `DigitalBrain.Testing.csproj`, `PackableProjects` if needed, `docs/architecture.md` testing subsection, public API review

- [ ] **Step 10.1** Ensure `IsPackable=true`, description dev-only, no production project references Testing.
- [ ] **Step 10.2** Architecture paragraph: tiers, Scenario, Gherkin generation rule, edges, Behavior≠Neuron reminder.
- [ ] **Step 10.3** Docs gate: `node tools/render-specification.mjs` && `node --test tests/*.test.mjs` from `docs/`.

---

### Task 11: Final gates (evidence before claim)

- [ ] **Step 11.1** Minimized delegation loop ≥20×.
- [ ] **Step 11.2** Full Simulations ≥3× Release.
- [ ] **Step 11.3** HostTests ≥3× Release.
- [ ] **Step 11.4** Root gate:

```text
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal"
```

- [ ] **Step 11.5** Docs render + tests.
- [ ] **Step 11.6** `git diff --check`
- [ ] **Step 11.7** CodeGraph refresh (build) + MCP query smoke.
- [ ] **Step 11.8** Process/container leak proof after HostTests.
- [ ] **Step 11.9** Push to `agent/lean-architecture-final` **only when** gates green; watch PR #39 framework CI **twice** green for explained reasons.
- [ ] **Step 11.10** Do **not** merge PR #39.

---

## 10. Per-commit grill (mandatory)

Every commit message must answer:

1. What was added without a current consumer?
2. What was claimed without verification?
3. What changed outside the intended slice?
4. Which exact original failure can this slice now diagnose or prevent?
5. What cleanup or global state remains after the test?

---

## 11. Linux parity

- CI is Linux GitHub-hosted; all claims require PR framework job green, not only Windows local.
- Prefer running Release configuration matching CI: `dotnet test DigitalBrain.slnx -c Release`.
- If local Linux unavailable, state that PR CI is the Linux oracle and do not claim Linux-local runs.

---

## 12. Out of scope (explicit)

- Implementing behavior proposal/install rail
- Flutter L3 harness
- Moving Integrations.Mcp under AI
- Full repo directory redesign unrelated to Testing
- Opening new PRs or merging #39
- Upstream Orleans bug claim without minimized pure repro

---

## 13. Self-review of this plan

| Spec requirement | Task coverage |
|---|---|
| Diagnose before fix | Task 0 |
| Scenario + assembly cluster | Task 1 |
| Clock | Task 2 |
| Faults | Task 3 |
| Diagnostics | Task 4 |
| Gherkin core thin layer | Task 5 |
| Delegation failure | Task 6 |
| Full L1 migration | Task 7 |
| L2 exclusive + HostTests | Task 8 |
| Edge substitutes | Task 9 |
| Package + docs | Task 10 |
| Final gates / PR CI | Task 11 |
| No even-engineering | Laws + prohibited steps |
| Historical comparison | §2 |
| Keep/delete table | §4 |

**Placeholder scan:** none intentional; API names in §3.2 are normative targets for Task 1.

---

## 14. Approval gate

**Do not start Tasks 1–11 until this plan is explicitly approved.**

Task 0 (read-only reproduce) may run before approval if the owner wants evidence first; it must not change product code.

**Plan complete and saved to** `docs/superpowers/plans/2026-07-24-testing-architecture.md`.

**Execution options after approval:**

1. **Subagent-Driven (recommended)** — fresh subagent per task, review between tasks  
2. **Inline Execution** — this session with checkpoints  

Which approach, after you approve?
