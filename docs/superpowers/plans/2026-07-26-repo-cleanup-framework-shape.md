# Repo Cleanup to Framework Shape — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the repo to best-in-class .NET framework shape (Aspire/Orleans) by deleting test-graph theater, consolidating doc sprawl, sweeping dead code, and covering the kernel's untested hard paths.

**Architecture:** A read-only **discovery wave** produces a verified `file:line` trash ledger. Five **worktree-isolated reduction lanes** apply deletions/merges in parallel (no file overlap). A **coverage wave** replaces deleted theater with real TDD proofs on kernel paths. A **gate wave** runs the full root gate + docs + aspire integration + adversarial review. All work lands as fresh commits on top of `f698e3be` — no history rewrite.

**Tech Stack:** .NET 10, C#, Orleans + Orleans.Journaling, Aspire, xUnit, VitePress (docs), Node test runner (`docs/tests/*.mjs`).

## Global Constraints

- **Baseline HEAD:** `f698e3be14eb2bda0717e673588bc81eb078b5f9` on `agent/digitalbrain-hosting-testing`. Record `git rev-parse HEAD` + `git status --porcelain` at each wave start; re-check before staging. If the tree moved for a reason not in this plan, **surface and stop**.
- **No history rewrite.** New commits only.
- **Root gate (long timeout, polled) is the only thing that permits a completion claim:**
  `dotnet build DigitalBrain.slnx -c Release` then `dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal"`. Never `--filter` for the completion gate.
- **Docs proofs:** `npm --prefix docs test` and `npm --prefix docs run build`.
- **Aspire integration** must be green: build → run → test (per owner global rules; use the `aspire` MCP tools).
- **API lookups** (Orleans, Aspire, xUnit) go through Context7 / Microsoft Learn **before** writing code. Never read local NuGet cache or anything under `C:\Users`.
- **No `/// <summary>` narration.** Meaning lives in names, types, and `[Fact(DisplayName=...)]`.
- **Every commit message** carries the three diff-grill answers (no-consumer / claimed-without-command / changed-that-I-didn't-change) and ends with `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`.
- **Deletion is preferred to simplification.** Aim for net reduction.
- **Session limit note:** subagent dispatch and aspire runs require the session limit to be clear (resets 10:50pm Europe/Prague). Waves A/D need it; Waves B/C mechanical edits do not.

---

## Wave A — Discovery (read-only, 6 parallel agents → verified ledger)

### Task A0: Ledger scaffold

**Files:**
- Create: `docs/superpowers/specs/2026-07-26-repo-cleanup-trash-ledger.md`

- [ ] **Step 1:** Create the ledger with one section per area (A1–A6) and this table header in each:
  `| id | category | path:line | action | evidence | verified |`
  where `category ∈ {DEAD, THEATER, MISPLACED, OVER-ABSTRACTED, NAMING, DUPLICATION}`, `action ∈ {DELETE, MERGE, INLINE, RENAME, MOVE, REHOME, KEEP}`, `verified ∈ {pending, yes-by-orchestrator}`.
- [ ] **Step 2:** Commit: `docs: scaffold repo-cleanup trash ledger`.

### Task A1–A6: Dispatch discovery agents

Dispatch six `general-purpose` agents **in one message** (parallel). Each returns rows for the ledger only — `path:line | category | action | evidence` — no file dumps, capped ~35 rows, ranked by impact, using codegraph to verify consumer counts.

- [ ] **Step 1:** Dispatch the six agents with these exact scopes:
  - **A1 kernel/core** — `src/DigitalBrain.Kernel`, `.Abstractions`, `.SourceGeneration`, `.Client`, `.` metapackage. Protect the `Neuron.*.cs` partial split (justified).
  - **A2 testing** — `src/DigitalBrain.Testing` + all `tests/*`. Per-file keep/delete classification. Explicit rule: a `Packages/`/`Boundary/` file is THEATER unless it proves runtime behavior or guards a *real, likely* architectural regression; `ClientSendOrdering`, `AiContractBoundaries` (MAF-internal), `KernelPackageBoundaryContracts` (kernel purity) get a genuine-invariant check.
  - **A3 behaviors/OS** — `src/DigitalBrain.Behaviors(.Runtime)`, `samples/DigitalBrain.Compositions`, `.AccountEnrichment`, activation path. Confirm the `DigitalBrainActivated` synapse in code matches the grill conclusion (`db.digitalbrain-activated`, `Abstractions`, `OwnerId Owner` only).
  - **A4 modules** — `modules/*`. AI keeps M.E.AI public / MAF internal; Google/SF southbound only; Time Countdown-only; Flutter first-five only.
  - **A5 hosts/edge** — `src/DigitalBrain.Aspire(.Hosting)`, `.Security`, `.Integrations.Mcp(.Aspire.Hosting)`, `hosts/*`, `clients/digitalbrain_wire`, `samples/Quickstart*`.
  - **A6 commits+docs** — classify the 30 unpushed commits; produce the exact deletable-docs list + `behavior-*` plan consolidation map.
- [ ] **Step 2:** As each returns, **the orchestrator re-verifies every row** (codegraph/grep the claimed consumer count; open the cited line). Mark `verified: yes-by-orchestrator` only on rows that survive. Drop unverified rows.
- [ ] **Step 3:** Commit the completed ledger: `docs: verified repo-cleanup trash ledger`.

> Wave B lanes below apply **only** ledger rows marked `verified: yes-by-orchestrator`. The firsthand-verified deletions in B1/B2/B3 are pre-seeded and do not wait on discovery.

---

## Wave B — Reduction (5 lanes, each in its own git worktree; parallel, no file overlap)

Create one worktree per lane via `superpowers:using-git-worktrees` at execution time. Each lane ends with its **owning-project** gate green before its branch is handed back.

### Task B1: Delete test-graph theater (aggressive)

**Files (firsthand-verified DELETE):**
- `tests/DigitalBrain.Tests/Packages/PackageInventory.cs` (392 — hand-mirrors the csproj graph)
- `tests/DigitalBrain.Tests/Packages/AspireContracts.cs` (199), `ResidualPackageGraphContracts.cs` (165), `AccountEnrichmentSampleContracts.cs` (69), `PackableProjects.cs` (6)
- `tests/DigitalBrain.Tests/Boundary/AssemblyBoundaryContracts.cs` (205), `ContractsPackageBoundaryContracts.cs` (181), `HostingPackageBoundaryContracts.cs` (170), `PackageBoundarySupport.cs` (169), `CompositionBoundaryContracts.cs` (60), `RepositoryLayout.cs` (53), `PackablePackageBoundaryContracts.cs` (25)

**Files (classify via ledger A2 before acting):**
- `Packages/ClientSendOrdering.cs` (145) — **REHOME** to a behavior test if it proves FIFO send ordering; else DELETE.
- `Packages/ClientApiContracts.cs` (154), `TasksContracts.cs` (144), `TimeContracts.cs` (131), `IdentityContracts.cs` (53) — DELETE unless a row proves runtime behavior.
- `Boundary/AiContractBoundaries.cs` (116) + `Boundary/KernelPackageBoundaryContracts.cs` (62) — **KEEP** as the only two genuine architectural invariants (MAF-internal; kernel free of Flutter/UI), collapsed into one small `Boundary/ArchitecturalInvariants.cs` if trivial.
- `Hosting/ProductModuleSet.cs` (54) — classify.

- [ ] **Step 1:** For each DELETE candidate, `grep -rn` its type names across `tests/` to confirm no other test consumes it (support files like `PackageBoundarySupport` must die *after* their consumers).
- [ ] **Step 2:** Delete the verified files. For KEEP invariants, if collapsing, move the two surviving `[Fact]`s into `tests/DigitalBrain.Tests/Boundary/ArchitecturalInvariants.cs` and delete the originals.
- [ ] **Step 3:** Build + test the owning project:
  `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --logger "console;verbosity=minimal"`
  Expected: PASS, with the deleted test count gone and no compile break.
- [ ] **Step 4:** Commit: `test: delete package-graph and boundary theater (aggressive)` — note LOC removed in the body.

### Task B2: Docs — delete campaign logs, consolidate fragmented plans

**Files (DELETE — self-described session logs / campaign records):**
- `docs/superpowers/specs/2026-07-25-200-grill-scorecard.md`, `architecture-ownership-scorecard.md`, `behavior-os-scorecard.md`, `test-truth-scorecard.md`
- `docs/superpowers/specs/2026-07-25-architecture-aligned-mass-deletion.md`
- `docs/architecture/behavior-os-implementation-ledger.md` (a `pending/pending` checklist for the unbuilt rail — referenced by `docs/architecture.md`? grep first; if linked, drop the link in the same commit)

**Files (FOLD-then-DELETE — preserve decision content):**
- `docs/superpowers/specs/2026-07-25-behavior-os-*-grill.md` (activation-synapse, emitter, flutter-reaction, openhome-dual, package-graph). Fold each grill's **conclusion** into `docs/superpowers/specs/2026-07-25-behavior-os-design.md` (its designated merge target), then delete the grill file. The activation-synapse grill's merge note names the exact section.

**Files (CONSOLIDATE — the 20 fragmented `behavior-*` plans):**
- Merge each split family back to one file: `behavior-foundation-and-admission{,-build-and-admission,-evidence,-sdk-and-identities}.md` → one; same for `-kernel-runtime*`, `-windows-sandbox*`, `-assistant-discovery*`, `-os-migration-and-cleanup*`. Keep `behavior-operating-system-roadmap.md` as the index.

- [ ] **Step 1:** `grep -rn "implementation-ledger\|200-grill-scorecard\|mass-deletion" docs/` to find inbound links; note them.
- [ ] **Step 2:** Fold grill conclusions into `behavior-os-design.md`; delete the grill files.
- [ ] **Step 3:** Consolidate each fragmented plan family into one file; delete the fragments; fix inbound links found in Step 1.
- [ ] **Step 4:** Delete the scorecards / mass-deletion / ledger files.
- [ ] **Step 5:** Docs proofs: `npm --prefix docs test` then `npm --prefix docs run build`. Expected: PASS (fix any dead-link failures the reader tests catch).
- [ ] **Step 6:** Commit: `docs: delete campaign session-logs and consolidate behavior plans`.

### Task B3: Dead / orphan sweep

**Files:**
- Delete on-disk orphan `hosts/DigitalBrain.BehaviorBuilder/` (0 git-tracked files; not in `.slnx`; obj-only ghost). Confirm with `git ls-files hosts/DigitalBrain.BehaviorBuilder/` returns empty, then `rm -rf` the directory.
- Delete `tests/DigitalBrain.Tests/Boundary/BehaviorPackageBoundaries.cs` (guards the ghost) — coordinate with B1 (same folder; assign to whichever lane owns `Boundary/` to avoid worktree overlap; default: B1 deletes it).
- Apply all ledger rows tagged `DEAD` + `verified` outside the `tests/` tree (zero-consumer public surface in `src/`, `modules/`, `hosts/`).

- [ ] **Step 1:** For each DEAD row, re-confirm zero consumers with codegraph before deleting.
- [ ] **Step 2:** Delete; run each owning project's test.
- [ ] **Step 3:** Commit: `chore: sweep orphan BehaviorBuilder and zero-consumer surface`.

### Task B4: Modules polish (no behavior change)

**Files:** per ledger A4 rows tagged `OVER-ABSTRACTED` / `NAMING` / `DUPLICATION` in `modules/*`.

- [ ] **Step 1:** Apply inlining/renames one module at a time; after each module run its test project (e.g. `dotnet test tests/DigitalBrain.ModuleTests/... -c Release`, `.Tasks.Tests`, `.Time.Tests`, `.Integrations.Tests`, `.Flutter.Tests`).
- [ ] **Step 2:** Commit per module: `refactor(<module>): inline single-caller indirection / rename per ledger`.

### Task B5: Kernel/core polish (surgical)

**Files:** per ledger A1 rows in `src/DigitalBrain.Kernel`/`.Abstractions`/`.Client`/`.SourceGeneration`. **Do not** merge the `Neuron.*.cs` partials.

- [ ] **Step 1:** Apply naming/inlining only; run `dotnet test tests/DigitalBrain.Tests/... -c Release` after each change.
- [ ] **Step 2:** Commit: `refactor(kernel): naming and inlining per ledger`.

---

## Wave C — Coverage (TDD; after B lanes merge back)

Replace deleted theater with real proofs on the kernel's untested hard paths (codegraph flagged `CapabilityDelegation` and `TurnCheckpoint` as having *no covering tests*). **Mirror the existing L1 pattern** — use `tests/DigitalBrain.Time.Tests/CountdownRecovery.cs` as the template for the `DigitalBrainFixture`/`TestBrain` API and journal-assertion style; confirm the exact API from that file before writing (do not invent `TestBrain` members).

### Task C1: Delegation eviction limit is enforced

**Files:**
- Create: `tests/DigitalBrain.Tests/Kernel/DelegationEviction.cs`
- Under test: `src/DigitalBrain.Kernel/Neuron/Neuron.Turns.cs:132` `MakeRoomForDelegation` / `TryEvictOldest` and the `MaximumRememberedDelegations = 32` throw.

- [ ] **Step 1: Write the failing test** — using the `TestBrain`/fixture pattern from `CountdownRecovery.cs`, drive a neuron to mint delegations past the 32 limit with no evictable terminal/consumed history and assert the `InvalidOperationException` with the "reached its limit of 32 remembered capability delegations" message; and assert that with evictable history, the oldest non-protected delegation is evicted and a new one succeeds.
- [ ] **Step 2: Run — verify it fails** (red) via the owning project: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter DelegationEviction`. Expected: FAIL (no such test / behavior unproven).
- [ ] **Step 3:** No production change needed if behavior already exists — this is a *coverage* test; if the test reveals a real defect, fix minimally in `Neuron.Turns.cs`.
- [ ] **Step 4: Run — verify it passes** (green). Same command. Expected: PASS.
- [ ] **Step 5: Commit:** `test(kernel): cover capability delegation eviction limit`.

### Task C2: Turn checkpoint rolls back on failed inbound commit

**Files:**
- Create: `tests/DigitalBrain.Tests/Kernel/TurnCheckpointRollback.cs`
- Under test: `Neuron.Turns.cs` `RollbackTurnState`, `StageInboundCause`, `AdvanceTurnCheckpoint`; drive via the existing `FailNextJournalCommit` fault handle on `TestNeuron` (`src/DigitalBrain.Testing/TestNeuron.cs:32`).

- [ ] **Step 1: Write the failing test** — arm a journal fault, deliver a synapse, assert the turn rolls back (outbox/handled/incoming unchanged, `InboundCommitted` not falsely set) and that after recovery the synapse is processed exactly once (no duplicate in the outgoing journal). Use `TestNeuron.Incoming`/`Outgoing` journals to assert.
- [ ] **Step 2: Run — verify red.** `--filter TurnCheckpointRollback`. Expected: FAIL.
- [ ] **Step 3:** Coverage test; fix `Neuron.Turns.cs` only if a real defect surfaces.
- [ ] **Step 4: Run — verify green.**
- [ ] **Step 5: Commit:** `test(kernel): cover turn checkpoint rollback on journal fault`.

### Task C3: Outbox drain resumes after restart

**Files:**
- Create: `tests/DigitalBrain.Tests/Kernel/OutboxDrain.cs`
- Under test: `src/DigitalBrain.Kernel/Neuron/Neuron.Outbox.cs` (read it first to name the exact drain entrypoints).

- [ ] **Step 1:** Read `Neuron.Outbox.cs` to identify the drain path and public/observable effect.
- [ ] **Step 2: Write the failing test** — enqueue outbox entries, `RestartHostAsync` (`TestNeuron.RestartHostAsync`), assert every queued entry is delivered exactly once after restart (durable drain, no loss, no duplicate).
- [ ] **Step 3: Run — verify red**, then **green** (`--filter OutboxDrain`).
- [ ] **Step 4: Commit:** `test(kernel): cover durable outbox drain across restart`.

---

## Wave D — Gate + adversarial review

### Task D1: Root gate

- [ ] **Step 1:** `git status --porcelain` clean of unexpected foreign changes; `git rev-parse HEAD` recorded.
- [ ] **Step 2 (long timeout, polled):** `dotnet build DigitalBrain.slnx -c Release` → exit 0, 0 warn/0 err.
- [ ] **Step 3:** `dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal"` → Failed 0. Quote the summary line.
- [ ] **Step 4:** `npm --prefix docs test` and `npm --prefix docs run build` → PASS.

### Task D2: Aspire integration

- [ ] **Step 1:** Via the `aspire` MCP tools: build the AppHost, `aspire run`, confirm the silo resource reaches Healthy, run the integration tests. Quote resource states.

### Task D3: Adversarial review + verify

- [ ] **Step 1:** Invoke `superpowers:requesting-code-review` (or dispatch a `pr-review-toolkit:code-reviewer` per changed area) over the Wave B/C diff.
- [ ] **Step 2:** For each finding, **the orchestrator re-verifies it independently** (open the line, run the check) before acting — a review is a claim like any other.
- [ ] **Step 3:** Apply only verified findings; re-run the root gate.

---

## Wave E — Integration

- [ ] **Step 1:** Confirm all lane branches are merged into `agent/digitalbrain-hosting-testing` as fresh commits on top of `f698e3be` (no rebase/squash of existing history).
- [ ] **Step 2:** Final `git log --oneline f698e3be..HEAD` review; each new commit message carries diff-grill answers.
- [ ] **Step 3:** Report net LOC delta (target: tests no longer exceed source; ≥2,000 test-theater LOC removed; docs md count materially down), and quote the final green root gate.

---

## Self-Review (against the spec)

- **Spec coverage:** P1 test inversion → B1 (delete theater) + C1–C3 (add coverage); P2 doc sprawl → B2; P3 dead/orphan → B3; P4 polish → B4/B5. Decision 1 (aggressive) → B1 with the genuine-invariant carve-out. Decision 2 (non-destructive) → Wave E. Decision 3 (docs in scope) → B2. All covered.
- **Placeholders:** discovery-dependent lanes (B4/B5, and the classify-first B1 files) reference the *produced, format-defined* ledger, not vague TODOs; firsthand-verified deletions carry exact paths. Coverage tasks cite the exact methods under test and the template test file rather than inventing `TestBrain` API that must be confirmed against the codebase.
- **Type/name consistency:** ledger categories/actions are fixed enums used identically across waves; test file paths under `tests/DigitalBrain.Tests/Kernel/` are new and non-colliding.
