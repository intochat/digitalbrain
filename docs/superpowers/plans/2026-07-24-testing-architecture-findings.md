# Testing architecture Task 0 findings

**Worktree:** `E:\intochat\digitalbrain\.worktrees\lean-architecture-implementation`  
**Branch:** `agent/lean-architecture-final`  
**HEAD at diagnosis:** `2c3aa4e5ef8268b2e6c0e69aade1f003393e5fe0`  
**Date:** 2026-07-24  
**Scope:** Reproduce / instrument only — no product fixes, no timeout inflation, no skip/quarantine.

---

## 1. Baseline

```text
git rev-parse HEAD
2c3aa4e5ef8268b2e6c0e69aade1f003393e5fe0

git status --porcelain
?? docs/superpowers/plans/2026-07-24-testing-architecture.md
```

HEAD matches the plan baseline. Only the untracked plan file was present before this task’s findings/commit work.

---

## 2. Commands and outcomes (quoted)

### 2.1 Isolated delegation — 10× Release

```text
dotnet test tests/DigitalBrain.Simulations -c Release --filter "FullyQualifiedName~DelegatedCallPreservesCommittedCausalRequestAndCompletesExactlyOnce" --logger "console;verbosity=minimal"
```

| Run | Exit | Wall duration | Test duration (summary line) | Result |
|---|---|---|---|---|
| 1 | 0 | 19.2 s | 665 ms | pass |
| 2 | 0 | 7.0 s | 315 ms | pass |
| 3 | 0 | 7.5 s | 300 ms | pass |
| 4 | 0 | 7.4 s | 324 ms | pass |
| 5 | 0 | 8.1 s | 320 ms | pass |
| 6 | 0 | 7.4 s | 318 ms | pass |
| 7 | 0 | 7.4 s | 326 ms | pass |
| 8 | 0 | 7.7 s | 313 ms | pass |
| 9 | 0 | 7.8 s | 368 ms | pass |
| 10 | 0 | 8.9 s | 401 ms | pass |

Representative summary line (run 10):

```text
Passed!  - Failed:     0, Passed:     1, Skipped:     0, Total:     1, Duration: 401 ms - DigitalBrain.Simulations.dll (net10.0)
```

**Local isolated flake rate:** **0 / 10 fail** (0%).

### 2.2 Full Simulations — 2× Release

```text
dotnet test tests/DigitalBrain.Simulations -c Release --logger "console;verbosity=minimal"
```

| Run | Exit | Wall duration | Summary |
|---|---|---|---|
| 1 | 0 | 1 m 24 s | `Passed!  - Failed: 0, Passed: 244, Skipped: 0, Total: 244, Duration: 1 m 17 s` |
| 2 | 0 | 1 m 27 s | `Passed!  - Failed: 0, Passed: 244, Skipped: 0, Total: 244, Duration: 1 m 18 s` |

**Local full-suite flake rate (this machine, n=2):** **0 / 2 fail**. Includes the CI-named test under the same shared 3-silo cluster + assembly serial schedule.

### 2.3 HostTests — 2× Release

```text
dotnet test tests/DigitalBrain.HostTests -c Release --logger "console;verbosity=minimal"
```

| Run | Exit | Wall duration | Summary |
|---|---|---|---|
| 1 | 0 | 43 s | `Passed!  - Failed: 0, Passed: 7, Skipped: 0, Total: 7, Duration: 36 s` |
| 2 | 0 | 40 s | `Passed!  - Failed: 0, Passed: 7, Skipped: 0, Total: 7, Duration: 33 s` |

Includes `HostedRestart.TheOrleansDashboardIsServedInDevelopment` and `HostedRestart.ADurableTurnAndDeliverySurviveAKernelRestart` (the CI dossier pair).

**Local HostTests flake rate (n=2):** **0 / 2 fail**.

---

## 3. CI dossier contrast (from plan §1.1 — not re-run here)

CI run `30055807730` @ same SHA `2c3aa4e5`:

| Attempt | Simulations | HostTests |
|---|---|---|
| 1 | 244/244 | **2 fail** (probe HTTP ~100s after Healthy) |
| 2 | **1 fail** (delegation timeout 30s) / 243 pass | **2 fail** (same HostedRestart pair) |

**H1 (suite interaction vs pure unit) evidence from this Task 0 loop:**

| Observation | Strength |
|---|---|
| Hang **does not** reproduce in isolation (10/10 green) | Isolation-only path is healthy under light load on this host |
| Hang **does not** reproduce in full Simulations either (2× 244/244 green) | **Local non-repro of CI’s suite failure** — not a clean “only under suite” local proof |
| CI still failed once under full Simulations on this SHA | Residual intermittent failure remains real; environment/load differs from this workstation |

So: **local evidence does not establish “isolation green / suite red” (classic H1 lab signature)**. It establishes **CI-only intermittent** relative to this workstation, with structural factors still favoring residue/contention theories over a always-broken code path.

---

## 4. Instrumentation

**Not applied.** Task 0 step “optional only if hang reproduces” — hang did **not** reproduce locally in isolation or full suite.

- No `[DBG-DEL-20260724]` stage logs added to redeem/enter/caller-read/emit/finish.
- **Last known hang stage remains unknown** (CI still only has Orleans timeout text: activation Valid, `IsExecuting=True`, queue empty, work group Waiting).
- Tree must remain free of DBG tags (verified at end of task).

Revisit instrumentation only after a red-capable local loop (stress/shuffle, CI agent repro, or intentional residue harness) exists — not as a blind production change.

---

## 5. Structural substrate facts (code inspection; not runtime hang proof)

### 5.1 L1 Simulations / delegation

- Assembly is serial: `tests/DigitalBrain.Simulations/AssemblyInfo.cs` has  
  `[assembly: CollectionBehavior(DisableTestParallelization = true)]`.
- Shared process cluster: tests call `SimulationCluster.StartAsync()` / `SimulationCluster.Grains` (process-global).
- The failing test (`DelegatedCallPreservesCommittedCausalRequestAndCompletesExactlyOnce`) at  
  `CapabilityDelegationSecurityContracts.cs` ~1072:
  - Opens owner `"delegation-valid"`.
  - Uses process-global observation dictionaries:  
    `DelegatedTargetCommitObservations`, `DelegatedTargetStorageObservations`,  
    `DelegatedTargetEntryObservations`, `DelegationIssuance`, `DelegatedInvocationGate`  
    (all `ConcurrentDictionary` statics in the same file).
  - Awaits `runner.InvokeAsync(delegation, targetId)` as the completion barrier (CI hang site per dossier).

These facts keep **H1 shared residue** as the leading *design* risk for suite-order flakes even when this host did not red.

### 5.2 L2 HostTests

- **No** `CollectionBehavior(DisableTestParallelization = true)`, **no** exclusive collection fixture, **no** assembly-level serial lock in `DigitalBrain.HostTests`.
- Each fact in `HostedRestart` builds its own `DistributedApplicationTestingBuilder.CreateAsync<Projects.DigitalBrain_TestingAppHost>` and starts a full AppHost.
- After `WaitForResourceHealthyAsync("probe")`, tests call `CreateHttpClient("probe")` then HTTP — CI hang is transport/`InitialFillAsync` after Healthy.
- Cleanup still includes `Process.GetProcessesByName("DigitalBrain.ProbeHost")` as a post-assert (not exclusive ownership).

These facts keep **Host H1 concurrent AppHosts** as the leading *design* risk for the CI pair, even though this host ran 7/7 twice.

---

## 6. Hypothesis board after Task 0

### L1 hang (`DelegatedCallPreserves…`)

| Id | Hypothesis | After local loop |
|---|---|---|
| **H1** shared residue (statics + shared cluster) | **Slightly strengthened as structural explanation for CI intermittency**; **not confirmed by a local red**. Isolation 10/10 green weakens “always broken even alone”; dual full-suite green weakens easy local residue repro. CI 1/2 suite red keeps H1 alive. |
| **H2** activation cycle (target↔issuer) | **Unchanged / untested** — needs hang + stage logs or deadlock dump. |
| **H3** scheduler starvation | **Unchanged** — no local hang under serial 244-test load on this host. |
| **H4** context loss | **Unchanged** — no discriminating evidence. |
| **H5** Orleans upstream | **Weakened as first move** — no first-party-only minimized red; CI still intermittent and substrate has first-party residue candidates. Do not file upstream without minimized repro. |

### L2 Host HTTP (HostedRestart pair)

| Id | Hypothesis | After local loop |
|---|---|---|
| **Host H1** concurrent AppHosts | **Structurally strengthened** (parallelism not disabled; per-fact AppHosts). **Not runtime-confirmed** (0/2 local fails). CI 2/2 fails on same pair remain consistent with contention under CI agents. |
| **Host H2** stale endpoint | **Open** — Healthy then hang on client fill fits endpoint churn; no local capture. |
| **Host H3** probe hang | **Open** — resource Healthy ≠ HTTP accept/ready race possible. |
| **Host H4** process leakage | **Open** — name-based process listing is present; exclusive fixture absent. |

---

## 7. What Task 0 does *not* authorize

- Production behavior “fixes” for delegation or Host restart.
- Increasing timeouts, retries, `[Skip]`, quarantine.
- Claiming root cause of either CI failure as proven.
- Claiming Orleans/Aspire upstream defects.
- Leaving temporary diagnostics in tree (none added).

---

## 8. Recommended next diagnostic moves (for later tasks — not executed here)

1. **Keep building the Scenario / exclusive L2 substrate** (plan Tasks 1+) — residue and concurrent AppHosts are defects independent of hang stage identity.
2. If a hang red is required before substrate migration: stress loop (repeat full Simulations N times under load) or CI artifact capture with stage tags — **only temporarily**, remove before commit.
3. For Host H1 measurement: force parallel HostTests vs exclusive serial collection and compare flake rates **as measurement only**, not as a permanent “fix” without ownership.

---

## 9. Cleanup checklist (Task 0 end state)

- [x] No `[DBG-DEL-20260724]` in sources  
- [x] Findings recorded here from executed commands  
- [x] Temporary raw logs (`_task0-*`) not treated as durable product artifacts  
- [x] No push  
- [x] No changes under `E:\intochat\Projects`  
