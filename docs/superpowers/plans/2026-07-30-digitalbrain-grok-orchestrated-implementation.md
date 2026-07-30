# DigitalBrain Grok-Orchestrated Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the approved neuron/synapse/behavior architecture through parallel Grok CLI worktrees while Codex remains the conductor, reviewer, integrator, and independent verifier.

**Architecture:** Pure directed synapses are the public programming model. Generated metadata forms the exact active capability catalog; MemoryModule provides a reusable vector store and rebuildable semantic projection; Tasks owns durable behavior execution; provider modules own MCP/auth/account details; authored behaviors run only in an isolated worker; Flutter exposes intent, scenarios, source, evidence, controls, and revision history.

**Tech Stack:** .NET 11 preview toolchain, C# and Roslyn, Orleans journaling, Aspire 13.5 preview, MCP, Microsoft.Extensions.AI, Qdrant behind MemoryModule, xUnit v3, Gherkin BDD, Dart/Flutter, Grok CLI 0.2.101+, CodeGraph 1.3.1+.

## Global Constraints

- The approved design is `docs/superpowers/specs/2026-07-30-neurons-synapses-behaviors-design.md`.
- Codex does not author production or test code. Every repository code change, refactor, deletion, conflict resolution, and implementation commit is performed by a Grok CLI session.
- Codex owns orchestration: establish git ground, launch isolated sessions, inspect every diff, reject scope drift, run independent gates, verify live state, and decide integration order.
- One writer owns one worktree. Never run two writing Grok sessions in the same worktree.
- Grok may use its own subagents for CodeGraph exploration, test analysis, and review. The top-level Grok session remains responsible for one coherent diff and must prevent its subagents from writing overlapping files.
- Use CodeGraph before every non-trivial edit and after the edit. Begin with `codegraph sync .` and record affected callers, callees, tests, and obsolete paths.
- Use Context7 before editing library/API integrations. For Aspire APIs, use Aspire MCP documentation tools (`aspire__search_docs`, then `aspire__get_doc`) before AppHost edits.
- Use Aspire MCP—not Computer and not shell telemetry—for AppHost selection, resource health, resource commands, structured logs, traces, and trace-correlated logs.
- Use Computer only for live Flutter visual interaction. Grok uses Dart MCP plus Flutter commands for implementation verification; Codex performs the independent visual acceptance pass.
- Use DigitalBrain MCP for live product proof. Do not substitute HTTP calls, direct grain calls, or unit-test output for the live neuron/journal/transcript evidence.
- Preserve unrelated user changes. Never use `git reset --hard`, `git checkout --`, broad deletion, or unverified generated-file cleanup.
- Never use Grok `--always-approve`, `--permission-mode bypassPermissions`, or an unrestricted sandbox. Start writers with `--permission-mode acceptEdits`; if a command is blocked, resume with the narrowest verified permission rather than widening globally.
- Existing `Claude.md` remains Grok's repository instruction source. The approved spec and these plans are explicitly user-requested execution artifacts; Grok must not delete them because of the repository's general “no docs tree” convention, and must not add unrelated prose documentation.
- Every new asynchronous operation propagates the caller or attempt cancellation token. Do not introduce `CancellationToken.None` in product flows. Use uncancelled tokens only for bounded cleanup where cancellation would corrupt cleanup semantics, and explain each occurrence in the handoff.
- Do not add `KernelTask`, `WorkId`, a shared account registry, public Qdrant types, operation-specific methods such as `ReadRecentMessages`, authored assemblies in the silo, or repository-wide preview language mode.

---

## Plans and Dependency Order

| Slice | Plan | Dependency |
|---|---|---|
| 1 | `2026-07-30-digitalbrain-slice-1-synapse-catalog.md` | none |
| 2 | `2026-07-30-digitalbrain-slice-2-behavior-contracts.md` | none; integrate after Slice 1 client primitives |
| 3 | `2026-07-30-digitalbrain-slice-3-tasks-behavior-runtime.md` | Slices 1 and 2 |
| 4 | `2026-07-30-digitalbrain-slice-4-vector-memory.md` | Slice 1 |
| 5 | `2026-07-30-digitalbrain-slice-5-provider-neurons.md` | Slices 1 and 3 |
| 6 | `2026-07-30-digitalbrain-slice-6-automatic-ai-routing.md` | Slices 1, 4, and 5 |
| 7 | `2026-07-30-digitalbrain-slice-7-flutter-behavior-studio.md` | Slices 2 and 3; final wiring after Slice 6 |
| 8 | `2026-07-30-digitalbrain-slice-8-live-hardening.md` | all previous slices |

The plans are separate because their first implementation tasks can use non-overlapping worktrees. Dependency gates still prevent dependent work from starting from stale bases.

## Grok Roles

### Explorer

Read-only. Uses CodeGraph to map the slice, identifies current tests and trash candidates, and returns a scope report. It never edits.

### Writer

Owns one slice worktree. Uses TDD, changes only its declared file surface, runs targeted tests, removes only proven obsolete code, commits atomic changes, and returns evidence.

### Reviewer

Read-only against the writer's committed worktree. Checks the approved spec, public surface, cancellation, security, test quality, unnecessary compatibility layers, and CodeGraph impact. It never fixes its own findings.

### Verifier

Read-only against the writer's committed worktree or integrated worktree. Re-runs targeted commands and, where the slice is live-addressable, uses Aspire and DigitalBrain MCP. It never edits.

### Integrator

A dedicated Grok writer. Starts from the accepted integration base, brings in accepted slice commits in declared order, resolves conflicts, runs root gates, and commits the integrated wave. Codex independently reviews and verifies that commit.

## Standard Grok Handoff

Every writing session must end with this exact information:

```text
lane:
base_sha:
head_sha:
commits:
codegraph_queries:
files_changed:
files_deleted:
trash_removed_and_proof:
tests_added:
red_command_and_failure:
green_commands_and_results:
build_command_and_result:
digitalbrain_mcp_evidence:
aspire_mcp_evidence:
remaining_risks:
scope_deviations:
```

Empty evidence fields must say `not applicable` and why. “Tests pass” without command output, test counts, and exit status is not acceptable.

## Orchestration Preflight

### Task 0: Establish the immutable execution ground

**Files:**
- Read: `Claude.md`
- Read: `docs/superpowers/specs/2026-07-30-neurons-synapses-behaviors-design.md`
- Read: all eight slice plans
- No source edits

- [ ] Record `git status --short`, `git rev-parse HEAD`, `git log -5 --oneline`, and `git worktree list`.
- [ ] If the primary worktree is dirty, identify every changed path and preserve it. Do not start writers until each planned edit surface is known not to overlap.
- [ ] Run `grok --version`, `codegraph --version`, `dotnet --info`, and `grok mcp doctor`.
- [ ] Run `codegraph sync .` followed by `codegraph status .`; require an up-to-date index.
- [ ] Run the baseline gates:

```powershell
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release
```

- [ ] If a baseline gate is already red, record the exact pre-existing failure and dispatch a read-only Grok diagnosis. Do not let slice writers “fix” unrelated baseline failures.
- [ ] Ask Aspire MCP `aspire__list_apphosts`. If more than one is running, select the DigitalBrain AppHost with `aspire__select_apphost` using its exact returned `appHostPath`.
- [ ] If no AppHost is running, report that Aspire MCP has no AppHost-start operation. The only permitted bootstrap exception is one Codex-controlled background `aspire start --non-interactive` invocation. After it starts, every Aspire operation must use MCP.
- [ ] Call `aspire__refresh_tools`, `aspire__list_resources`, and record the initial resource health.
- [ ] Call `digitalbrain-mcp__list_active_neurons` and `digitalbrain-mcp__read_chat_transcript` for the initial owner state.

Expected: clean or fully accounted git ground, green baseline or documented pre-existing failure, healthy MCP connections, complete CodeGraph index.

## Parallel Discovery Wave

### Task 1: Launch four read-only Grok explorers

Run these concurrently from the same recorded base SHA:

| Explorer | Scope |
|---|---|
| `db-explore-contracts` | client reference/send plumbing, generated catalog, active module selection, public method-shaped contracts |
| `db-explore-behaviors` | artifact/compiler/BDD/host/Tasks execution, cancellation, in-process loading |
| `db-explore-memory-routing` | IAW Qdrant precedent, new MemoryModule, AI `ToolsFor`, capability projection |
| `db-explore-product` | Google/Salesforce auth rails, OS AppHost/MCP/UI endpoints, Flutter Behavior Studio surface |

Each explorer prompt must require:

- [ ] `codegraph explore` queries naming the core symbols in its slice.
- [ ] Exact affected files and a “must not touch” list.
- [ ] Existing tests that should fail first.
- [ ] Candidate dead/duplicate/compatibility code with incoming-reference proof.
- [ ] Cross-slice collision risks, especially `DigitalBrain.slnx`, `Directory.Packages.props`, AppHost files, behavior contracts, and Flutter HTTP models.
- [ ] No edits, no commits, no resource mutations.

Codex synthesizes the reports. If they contradict the approved design, the design wins unless repository evidence shows it is impossible; in that case stop before code and report the specific contradiction.

## Launching Parallel Writers

Use Grok worktrees, prompt files, hidden background processes, and separate outputs. A representative PowerShell launch shape is:

```powershell
$repo = (Resolve-Path "E:\intochat\digitalbrain").Path
$baseSha = (git -C $repo rev-parse HEAD).Trim()
$runRoot = Join-Path $repo ".grok\runs\$baseSha"
New-Item -ItemType Directory -Force -Path $runRoot | Out-Null

$process = Start-Process `
    -FilePath "grok" `
    -ArgumentList @(
        "--cwd", $repo,
        "--worktree=db-slice-1",
        "--worktree-ref=$baseSha",
        "--prompt-file=$runRoot\slice-1.prompt.md",
        "--check",
        "--permission-mode", "acceptEdits",
        "--no-memory",
        "--max-turns", "80",
        "--output-format", "json"
    ) `
    -RedirectStandardOutput "$runRoot\slice-1.stdout.json" `
    -RedirectStandardError "$runRoot\slice-1.stderr.log" `
    -WindowStyle Hidden `
    -PassThru
```

Codex may launch several such processes concurrently only when the wave table says their edit surfaces are independent. Prompt/output files are orchestration scratch data and must remain ignored.

## Execution Waves

### Wave 1: Foundations

Phase A launches two writers in parallel from the same clean base:

- Slice 1 — client synapse plumbing and generated exact catalog.
- Slice 2A — Slice 2 Tasks 1–3 only: behavior artifact, feature contract, compiler, union lowering; it must not edit Slice 1 client files.

Wave 1 writers report required `DigitalBrain.slnx` additions but do not edit that shared file; the Wave 1 integrator owns all solution-file changes.

After each Phase A writer commits:

- [ ] Run a separate read-only reviewer in that worktree.
- [ ] If the reviewer finds an issue, resume the original writer with only the accepted findings.
- [ ] Run a separate verifier.
- [ ] Have an integration Grok session merge accepted commits in order: Slice 1, then Slice 2A.
- [ ] Codex independently runs the root build/test gates on the integrated commit.

From that green integration commit, Phase B launches two writers in parallel:

- Slice 2B — Slice 2 Tasks 4–5: attach the single-file behavior SDK and derive/enforce directed capability grants from the client primitives created by Slice 1.
- Slice 4A — vector-memory contracts, provider abstraction, in-memory provider, and tests. It consumes `ProtectedPayloadReference` from the integrated Slice 1 base and does not add Qdrant packages.

Review and verify both writers independently. Integrate Slice 2B, then Slice 4A; let the integrator add their projects to `DigitalBrain.slnx`, and rerun behavior, client, memory, and root gates before Wave 2.

### Wave 2: Durable runtime and provider modules

From the green Wave 1/Slice 2B integration commit, run two phases.

Phase A launches in parallel:

- Slice 3 — Tasks-owned behavior runtime, isolation, and the shared module-user-action/MCP rail.
- Slice 4B — Qdrant provider and capability/behavior projection.

Review, verify, and integrate Phase A. Then Phase B launches in parallel from that green base:

- Slice 5 — Google and Salesforce intent synapses consuming the integrated Tasks user-action rail.
- Slice 7A — Slice 7 Tasks 1–3: frozen OS UI behavior contracts, Flutter core models, Library, Overview, and Scenarios.

Reserve these central files for the Wave 2 composition integrator:

- `DigitalBrain.slnx`
- `os/DigitalBrain.OS.AppHost/AppHost.cs`
- `os/DigitalBrain.OS.AppHost/DigitalBrain.OS.AppHost.csproj`
- `os/DigitalBrain.OS.Host/DigitalBrain.OS.Host.csproj`

Slice 4B alone may edit `Directory.Packages.props` for Qdrant packages. No other Wave 2 writer may edit it.

After Phase B, a dedicated `db-wave2-composition` Grok integrator adds all new projects to `DigitalBrain.slnx`, composes Tasks/Memory/Qdrant/provider resources in AppHost/Host projects, resolves generated project references, and runs composition tests. Then run root gates and targeted Flutter gates.

### Wave 3: Automatic routing and complete product flow

From the green Wave 2 integration commit:

1. Run the Slice 6 writer for semantic candidate retrieval, exact validation, and removal of hard-coded capability tools.
2. Review, verify, and integrate Slice 6.
3. Run the Slice 7B writer for behavior change flow, operational controls, revisions, and final Flutter integration from the accepted Slice 6 base.

Integrate Slice 6 then Slice 7B. Run all root, Flutter, and OS UI gates.

### Wave 4: Adversarial review, deletion, and live hardening

Launch four read-only Grok reviewers in parallel:

- architecture/spec compliance;
- security/auth/payload/journal review;
- cancellation/replay/Tasks correctness;
- simplification/dead-code/public-surface review.

Codex deduplicates findings. A single Slice 8 hardening Grok writer applies only accepted findings, deletes proven obsolete seams, runs the complete regression suite, and performs live proof.

## Per-Lane TDD Protocol

Every writer follows this order:

- [ ] Run CodeGraph exploration and record current callers/callees.
- [ ] Read the narrow existing implementation and tests identified by CodeGraph.
- [ ] Add one focused failing test for the next behavior.
- [ ] Run the smallest test command and capture the expected failure.
- [ ] Implement the minimum coherent change.
- [ ] Re-run the focused test and its owning project.
- [ ] Refactor only after green.
- [ ] Run `codegraph explore` again on changed public symbols.
- [ ] Identify obsolete paths; delete only when CodeGraph and tests prove replacement.
- [ ] Run the slice build/test matrix.
- [ ] Commit one logical task with an imperative message.
- [ ] Repeat for the next task.

## Live Proof Protocol

Live proof occurs on an integrated worktree, never simultaneously from several slice worktrees against the same running product.

### Aspire MCP

- [ ] `aspire__list_apphosts`; select exact AppHost when necessary.
- [ ] `aspire__refresh_tools`.
- [ ] `aspire__list_resources`; require all necessary resources running and healthy.
- [ ] Use `aspire__execute_resource_command(resourceName, "restart")` only for the resource changed; use `"start"` if stopped.
- [ ] Poll `aspire__list_resources` for readiness because Aspire MCP has no separate wait tool.
- [ ] Query `aspire__list_structured_logs` by resource and correlation/task/command ID.
- [ ] Query `aspire__list_traces`, capture the trace ID, then use `aspire__list_trace_structured_logs(traceId)`.
- [ ] Use `aspire__list_console_logs` only as secondary evidence.

### DigitalBrain MCP

- [ ] `digitalbrain-mcp__list_active_neurons` to identify the exact neuron type and owner-scoped instance activated by the scenario.
- [ ] `digitalbrain-mcp__send_chat_message` with a fresh GUID `commandId` for user-facing flows.
- [ ] `digitalbrain-mcp__read_neuron_journal` with exact `grainType`, `name`, direction, and cursor.
- [ ] `digitalbrain-mcp__read_chat_transcript` to verify the owner-visible result.
- [ ] For behavior revisions, exercise `read_behavior` → `propose_behavior_revision` → `run_behavior_tests` → `approve_behavior_revision`, using fresh command and approval GUIDs.

For missing Google/Salesforce authorization, the successful first proof is a typed user-action/auth requirement tied to the same Task, with no secret in journals. If completing the proof requires the owner to click the real provider link, pause at that explicit user action; after completion, verify continuation of the same durable Task rather than creating a replacement Task. Reuse the original command ID only when the implemented and documented command semantics require it.

## Independent Codex Review Gates

Codex must not trust a Grok completion claim. For every accepted lane:

- [ ] Compare base and head with `git diff --stat`, `git diff --check`, and a full diff review.
- [ ] Verify changed files stay within the declared surface.
- [ ] Search for prohibited artifacts: operation-specific public methods, `KernelTask`, `WorkId`, public Qdrant references, shared accounts, `CancellationToken.None`, authored assembly loading in silo projects, and hard-coded Gmail tools in AI.
- [ ] Re-run targeted tests independently.
- [ ] Confirm every deletion has incoming-reference and test proof.
- [ ] Confirm no secrets, auth codes, raw protected payloads, or MCP content enter journal DTOs, logs, manifests, vector records, or Grok handoffs.
- [ ] Reject any lane that weakens tests, ignores cancellation, edits unrelated code, or leaves its worktree dirty.

## Final Gates

- [ ] `dotnet build DigitalBrain.slnx -c Release`
- [ ] `dotnet test DigitalBrain.slnx -c Release`
- [ ] `dotnet test os/tests/DigitalBrain.OS.Product.Tests -c Release -- -explicit only`
- [ ] `cd clients/flutter/core ; dart analyze ; dart test`
- [ ] `cd clients/flutter/shell ; flutter analyze ; flutter test ; flutter build windows`
- [ ] Live “read my last three emails” path through DigitalBrain MCP and Aspire telemetry.
- [ ] Live behavior proposal/test/approval/execution path through DigitalBrain MCP.
- [ ] Computer-only visual inspection of all six Flutter Behavior Studio views and the Google/Salesforce user-action flow.
- [ ] Final CodeGraph query proves no AI code contains provider-specific tool selection and no production silo path loads authored behavior assemblies.
- [ ] Final `git status --short` is clean and all accepted commits are listed.

## Completion Standard

The effort is complete only when all nine acceptance criteria in the approved design are demonstrated with tests and live evidence. A green build alone is insufficient. A working happy path without cancellation, auth continuation, replay safety, vector namespace isolation, behavior BDD admission, and automatic module discovery is also insufficient.

## Spec Coverage Matrix

| Approved acceptance criterion | Primary implementation | Final proof |
|---|---|---|
| Active modules automatically publish neurons/synapses | Slices 1 and 4 | Slice 8 automatic test-module BDD |
| “Read my last three emails” uses `IGmail` + `GmailRequest` | Slices 5 and 6 | Slice 8 live Gmail MCP proof |
| Missing auth shows an action and continues the same Task | Slices 3, 5, and 7 | Slice 8 auth continuation journals/trace/UI |
| Community code can use `IVectorMemory` | Slice 4 | Slice 8 user/community memory proof |
| A behavior owns a new input union without central-interface edits | Slice 2 | Slice 8 compatibility BDD |
| English scenarios/C#/compatibility/grants/security gate publication | Slice 2 Tasks 1–5 and Slice 7 | Slice 8 behavior revision rail |
| Authored code is isolated and replays safely | Slice 3 | Slice 8 crash/replay and process-boundary proof |
| A non-programmer can understand, stop, and change a behavior | Slice 7 | Slice 8 Computer visual acceptance |
| Fifteen future modules require no AI tool-list edits | Slices 1, 4, and 6 | Slice 8 CodeGraph plus discovery BDD |
