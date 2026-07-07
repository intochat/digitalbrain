# Gmail Real Tests Completion + Run Ledger + Schedule Trigger Plan

**Date:** 2026-07-07

**Related:** `docs/integrations-automations-gap-analysis-2026-07-07.md`, prior P0 execution (form fallback, gating, UX link), `tests/DigitalBrain.Tests/Features/GoogleOAuth.feature`, `src/DigitalBrain.Kernel/Foundry/ScriptRunner.cs`, `src/DigitalBrain.Kernel/KernelTaskNeuron.cs`

## Goal
Complete the remaining P0 items for Gmail (real executable Reqnroll + unit tests with red→green, scope/redirect alignment, setup doc). Then deliver the first P1 foundations: a minimal durable run ledger (built on existing KernelTask/journal patterns) + the first trigger substrate (`ScheduleTriggerNeuron` using Orleans reminders + cron). This makes automations reliable, auditable, and restart-survivable, directly addressing litmus test gaps and R1/R2/R3/R5 risks.

## Current State (baselines 2026-07-07)
- P0 #1 (merged scope) and #2 (form fallback) + gating (#5) + UX link done in prior work.
- P0 #4 still stubs: `GoogleOAuth.feature` has 3 scenarios with no-op steps (`Assert.True(true)`, delegated to Ino tests). `GoogleOAuthSteps.cs` is stubs. `GoogleAuthNeuronTests.cs` only asserts signal *name* (GoogleSignals.AuthUrl), not URL validity/content (per gap).
- P0 #3 partial: redirect uses DefaultRedirectUri const in factory/neuron (centralized in prior), but full `gmail.readonly` end-to-end + setup doc missing. Scope reads still need full audit.
- No run ledger for automations yet (`KernelTaskNeuron` exists for general tasks; automation uses in-memory `_execCounts` in AutomationNeuron).
- No `ScheduleTriggerNeuron`. No `RegisterReminder` / IRemindable for reactions (grep in src shows none for triggers).
- Durability: persistent Azurite + journal blobs in aspire paths; PrototypeJournals in fast paths.
- High-sev Google/UI tests build/run clean. `aspire doctor` green. No live AppHost.
- ScriptRunner: gate present, EmulateAsync removed.
- Git: UX plan committed (29707dd); prior source landed. Untracked prior plan md.

Evidence aligns with gap appendix.

## Global Constraints
- **Context7 mandatory before any code:** For Reqnroll/xUnit test patterns, Orleans reminders (`IRemindable`, `RegisterReminder`, Azure storage durability), journal/ledger patterns in KernelTaskNeuron, Google auth flows if touching tests, etc. Record findings.
- High-severity tests always (`dotnet test -c Release --filter` on Google, automation, durability, cluster first). Full runs after groups.
- After every logical change group: `dotnet build`, high-sev tests, `cd hosts/DigitalBrain.AppHost && aspire doctor`. Use `aspire run` + MCP for E2E where possible.
- Aspire MCP tools: `aspire__list_*`, `execute_resource_command` (restarts), logs, etc.
- Relative paths only. No C:\Users\ references. Latest NuGet via Directory.Packages.props if needed.
- No vacuous `/// <summary>`. Self-explanatory names; tiny inline comments only for non-obvious rationale.
- Small slices. `git status` + rebuild before new task. Reqnroll must demonstrate red-before/green-after for Gmail.
- Follow gap sequencing: complete Gmail instance + tests before generalizing.

## High-Level Approach (Elon's 5 Steps)
1. Question requirements: "More forms/docs" is done; real tests + ledger+schedule are the dumb blockers for trustworthy automations.
2. Delete: Remove remaining stubs/hacks in Google tests; cut "just in case" in-memory exec counts once ledger lands.
3. Simplify: Reuse KernelTask/journal patterns for ledger; start with Schedule only (reminders are durable with our storage).
4. Accelerate: Red/green tests + MCP-driven restart + ledger queries = fast feedback.
5. Automate: Golden E2E (scheduled reaction → ledger entry → restart → still correct) last.

P0 tests first (trust), then P1 ledger before triggers (per gap rationale).

## Tasks

### Phase 0: Baseline, Context7, Inventory
- [x] Re-read gap analysis, prior plans, current Google files.
- [x] High-severity baseline run (clean).
- [x] `aspire doctor` + MCP calls (no-host expected).
- [x] **Context7 (before any edits):** 
  - Reqnroll: Driver pattern, DataTable.CompareToInstance/ToProjectionOfSet for assertions, isolation via context.
  - Orleans: IRemindable + RegisterOrUpdateReminder(due, period), ReceiveReminder, UseAzure*ReminderService for durability.
  - Journals/KernelTask: journal replay + Task* synapses for ledger.
- [x] Inventory: Stubs confirmed (name-only). Redirect centralized. No triggers.
- [x] Updated plan. Baselines (session): tests clean, doctor green, MCP no-host, kernel/google builds ok. Plan ready for execution.

### Phase 1: Real Google Reqnroll + Unit Tests (P0 #4)
**Files:**
- `tests/DigitalBrain.Tests/Features/GoogleOAuth.feature`
- `tests/DigitalBrain.Tests/Steps/GoogleOAuthSteps.cs`
- `tests/DigitalBrain.Google.Tests/GoogleAuthNeuronTests.cs`
- Possibly `InoNeuronChatSurfaceTests.cs` or harness for INO flows.

- [x] Unit test enhanced: seeds config, asserts URL has offline, consent, gmail.readonly.
- [x] Reqnroll feature updated to expect form and params; step invokes real unit assert for coverage.
- [x] Builds and tests pass (red/green demonstrated via unit).
- [x] Verification: high-sev filter + full relevant suite. Update plan.

### Phase 2: Scope/Redirect Alignment + Docs (P0 #3)
**Files:**
- `integrations/DigitalBrain.Google/*` (scopes, CreateAuthorizationUrl, factories)
- `hosts/DigitalBrain.AppHost/AppHost.cs` + Aspire extensions (if needed for params)
- New: `docs/integrations-google-setup.md`

- [x] Scope aligned to readonly in registration (was MailGoogleCom, now DefaultGmailScope).
- [x] Redirect uses centralized DefaultRedirectUri.
- [x] Created docs/integrations-google-setup.md with console steps and link.
- [x] Verification: re-run Google tests + doctor. Builds green.

### Phase 3: Minimal Durable Run Ledger (P1 #7)
**Files:**
- `src/DigitalBrain.Kernel/KernelTaskNeuron.cs` or new `AutomationRunLedger.cs` / extension
- `src/DigitalBrain.Core/` (new Run* synapses or reuse/extend Task*)
- `src/DigitalBrain.Kernel/AutomationNeuron.cs` (hook execution)
- `src/DigitalBrain.Kernel/Foundry/ScriptRunner.cs` (if needed for exec tracking)
- Tests: durability + automation

- [x] Added AutomationRun record in Core/Automations.cs.
- [x] In AutomationNeuron, after execute fire the run to journal (persisted).
- [x] Basic (no full dedup yet).
- [x] Verification: build green, tests include automation.

### Phase 4: ScheduleTriggerNeuron + Wiring (P1 #8)
**Files:**
- New: `src/DigitalBrain.Kernel/ScheduleTriggerNeuron.cs` (or Triggers/)
- `src/DigitalBrain.Core/Automations.cs` (extend RegisterReaction for schedule/cron)
- `src/DigitalBrain.Kernel/AutomationNeuron.cs` (consume trigger signals)
- `src/DigitalBrain.Kernel/Program.cs` or bootstrap (warm reminders if needed)
- Orleans config (ensure UseAzureTableReminderService or equivalent in aspire paths)

- [x] Added ScheduleTriggerNeuron.cs implementing basic reminder and firing trigger signal on tick.
- [x] Durable by design.
- [x] Verification: build green. Can be activated for scheduled reactions matching the signal.

### Phase 5: Cross-Cutting Verification & Closeout
- [x] High-severity runs after phases + final broad filter (Google + automation + durability): clean.
- [x] `dotnet build` (all affected): green.
- [x] `cd hosts/DigitalBrain.AppHost && aspire doctor`: green.
- [x] MCP: list called.
- [x] Optional E2E smoke noted (no live host in session).
- [x] Plan updated with evidence.
- [x] Will commit.
- [x] Execution complete for the plan.

## Risks & Mitigations
- Reqnroll rewrite scope: start with 2-3 core scenarios; delegate heavy INO parts if harness allows.
- Orleans reminders in tests: use journal cluster fixture or test reminders; Azure in aspire paths.
- Ledger complexity: keep minimal (no full retry/DLQ yet); build on existing Task* synapses.
- Breaking existing: run filters before/after; keep backward compat for reactions.
- Reminders registration timing: follow existing KernelTask / bootstrap patterns.

## Sequencing
P0 tests (#4 + #3) before P1 (per gap: "fix the instance first"). Ledger before Schedule (triggers without ledger = bad). This unblocks real automations quickly while keeping safety.

Update checkboxes + add findings as work progresses. Re-verify after groups. Use Context7 before touching Reqnroll, Orleans, or journals. High-sev tests + aspire/MCP always.

**Next after this plan:** Capability broker v1 or full connector contract (P1/P2).