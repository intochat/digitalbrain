# Repo cleanup to framework shape — design & parallel execution plan

**Date:** 2026-07-26
**HEAD at authoring:** `f698e3be14eb2bda0717e673588bc81eb078b5f9`
**Branch:** `agent/digitalbrain-hosting-testing`
**Goal (one sentence):** Bring the repository to the shape of a best-in-class .NET framework
(Aspire/Orleans quality) — clean code, self-explanatory naming, explicit flows, tests that prove
product behavior, and docs that are decision records — by attacking the two structural problems the
`cleanup-041…074` micro-series never touched.

---

## 1. Diagnosis

The unpushed `cleanup-0NN` series (30 commits vs `origin`) is **real but micro**: validation fixes
(067–074), husk deletions, `NoWarn` drops, internalizations. Individually fine; collectively they
never touched the structural problems. Four problems, ranked by leverage:

| # | Problem | Firsthand evidence |
| --- | --- | --- |
| **P1** | Test tier is **inverted** — it over-pins the project graph and under-covers the kernel | tests **9,575 LOC** > src **9,374 LOC**. `DigitalBrain.Tests` = 3,610 LOC; its `Packages/` + `Boundary/` = **2,535 LOC** (~26% of all test code). `PackageInventory.cs` (392 LOC) hand-mirrors every project name + packability flag as string constants — duplicating `.slnx`/`.csproj`. codegraph: `CapabilityDelegation` (16 callers) and `TurnCheckpoint` report **"no covering tests found."** The hardest durable paths are untested; the csproj graph is triple-pinned. |
| **P2** | **Doc sprawl** — 202 markdown files; many are campaign session-logs | `*-scorecard`, `*-grill`, and `behavior-os-implementation-ledger.md` (a `pending/pending` checklist for the **unbuilt** rail). `200-grill-scorecard.md` self-describes as "Durable record of the 200-agent campaign." 20 fragmented `behavior-*` plan files for a Designed-only rail. Repo's own rule: *"Delete session logs, progress reports, and task checklists."* |
| **P3** | **Dead / orphan residue** | `hosts/DigitalBrain.BehaviorBuilder` — 0 git-tracked files, absent from `.slnx`, obj-only ghost — yet still pinned by `tests/.../Boundary/BehaviorPackageBoundaries.cs`. Plus zero-consumer surface to be enumerated in Wave A. |
| **P4** | **Framework polish** — naming, over-abstraction, explicit flows | Requires the Wave A discovery ledger for the exhaustive `file:line` list. Early hints: module orchestration adapters and naming. |

### Explicitly NOT trash — do not touch

- **`Neuron.*.cs` partial split** is legitimate durable-grain mechanics (turn checkpointing,
  delegation eviction, outbox drain) — Orleans-grade complexity, correctly split. Not churn.
- **Code honesty is accurate.** Grep confirms **no** Behavior rail types (compiler/worker/broker/
  installer) exist in code — the rail is honestly Designed-only. The honesty risk lives in *docs*
  (P2), not code. Do not "fix" honesty in code that is already honest.

## 2. Ratified decisions (owner, 2026-07-26)

1. **Test guards → aggressive.** Delete the package-graph / boundary / csproj-shape pins; rely on the
   compiler + real behavior tests. **Nuance:** "aggressive" targets the *theater*, not the folder —
   any file under `Packages/`/`Boundary/` that proves genuine **runtime** behavior (e.g. real FIFO
   send ordering) is preserved and, if needed, re-homed to a behavior test project.
2. **Git history → non-destructive.** Keep the 30 `cleanup-0NN` commits as-is; all new cleanup lands
   as fresh commits on top. No history rewrite.
3. **Docs → in scope.** Delete campaign session-logs; preserve decision content by folding grill
   *conclusions* into the design authorities before deleting the grill scaffolding; consolidate the
   fragmented `behavior-*` plans.

## 3. Agent scoring rule (applies to every lane)

A change **counts** only if it is concrete, cites `file:line`, and falls in one category:
`DEAD` (zero consumers, verified via codegraph/grep) · `THEATER` (asserts framework/compiler/graph
shape, not product behavior) · `MISPLACED` (vocabulary/logic in the wrong layer) · `OVER-ABSTRACTED`
(single-impl/single-caller indirection) · `NAMING` (not self-explanatory) · `DUPLICATION`.
No summaries, no "looks fine", no premature abstraction. Deletion is preferred to simplification.

## 4. Wave plan (parallel subagents, worktree-isolated)

### Wave A — Discovery (6 read-only agents, parallel)

Re-run the audits that died on the session limit. Each returns a `file:line → category → action →
evidence` list under the §3 rule; **the orchestrator verifies every finding** before it enters Wave B
(per the repo rule "verify the review's findings yourself"). Consolidated output: a single **trash
ledger** committed to `docs/superpowers/specs/2026-07-26-repo-cleanup-trash-ledger.md`.

| Agent | Scope |
| --- | --- |
| A1 kernel/core | `src/DigitalBrain.Kernel`, `.Abstractions`, `.SourceGeneration`, `.Client`, `.` metapackage |
| A2 testing | `src/DigitalBrain.Testing` + all `tests/*` (theater classification, per-file keep/delete) |
| A3 behaviors/OS | `src/DigitalBrain.Behaviors(.Runtime)`, `samples/DigitalBrain.Compositions`, `.AccountEnrichment`, activation path |
| A4 modules | `modules/*` (AI, Tasks, Time, Google, Salesforce, Flutter + Contracts + Aspire.Hosting) |
| A5 hosts/edge | `src/DigitalBrain.Aspire(.Hosting)`, `.Security`, `.Integrations.Mcp(.Aspire.Hosting)`, `hosts/*`, `clients/digitalbrain_wire`, `samples/Quickstart*` |
| A6 commits+docs | classify 30 unpushed commits; enumerate deletable docs + plan consolidation map |

### Wave B — Reduction (5 lanes, parallel, each in its own git worktree — no file overlap)

- **B1 Test-theater (aggressive):** delete `PackageInventory.cs`, `AspireContracts.cs`,
  `ResidualPackageGraphContracts.cs`, the `Boundary/*` graph pins, and every ledger-confirmed shape
  pin. Preserve/re-home genuine runtime-behavior tests. Target ≥ ~2,000 LOC removed.
- **B2 Docs:** delete `*-scorecard.md`, `*-grill.md` (after folding conclusions), `*-mass-deletion`
  campaign specs, `behavior-os-implementation-ledger.md`; consolidate the 20 fragmented
  `behavior-*` plans to one file per topic. Keep `docs/architecture/*` authorities.
- **B3 Dead/orphan sweep:** delete `hosts/DigitalBrain.BehaviorBuilder`, `BehaviorPackageBoundaries.cs`,
  and all Wave-A zero-consumer surface.
- **B4 Modules polish:** inline single-caller indirection, rename per ledger — **no behavior change**.
- **B5 Kernel/core polish:** naming + inlining only — surgical, small.

Each lane runs its **owning-project** build+test green before handing back its worktree.

### Wave C — Coverage (TDD, after B merges)

Replace deleted theater with *real* proofs on the under-covered hard kernel paths, red-first:
delegation eviction (`MakeRoomForDelegation` limit throw, `TryEvictOldest`), turn rollback
(`RollbackTurnState`, `StageInboundCause`, `AdvanceTurnCheckpoint`), outbox drain (`Neuron.Outbox.cs`).
API lookups (Orleans.Journaling, xUnit) go through Context7 / Microsoft Learn first, per global rules.

### Wave D — Gate + adversarial review

Root gate with a long timeout, polled:
```
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal"
npm --prefix docs test && npm --prefix docs run build
```
Then `aspire` build → run → test (integration green) per global rules. Then one adversarial review
per changed area; the orchestrator re-verifies each finding before accepting it.

### Wave E — Integration

Land as fresh commits on top of `f698e3be` (decision 2). Each commit message carries the diff-grill
answers (no-consumer / claimed-without-command / changed-that-I-didn't-change).

## 5. Verification & rollback

- **Baseline discipline:** record `git rev-parse HEAD` + `git status --porcelain` at wave start;
  re-check before staging. If the tree moved for a reason not in this plan, **surface and stop**.
- **Rollback:** every lane is a worktree branch; a bad lane is dropped without touching the others.
  No history rewrite means the pre-cleanup tip stays reachable.

## 6. Success criteria

- Root gate + docs proofs + aspire integration all green.
- Test tier no longer exceeds source; graph/shape theater gone; the hard kernel paths gain real
  coverage (net: fewer test LOC, more behavior proven).
- Docs are decision records + authorities only — no scorecards/grills/ledgers; plans consolidated.
- No orphan projects; no zero-consumer public surface introduced or left behind.
- A reviewer opening kernel/testing/os/behaviors/modules reads explicit flows and self-explanatory
  names, the way they would in Aspire or Orleans.
