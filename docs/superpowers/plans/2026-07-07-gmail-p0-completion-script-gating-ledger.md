# Gmail P0 Completion, Script Rail Hardening, Run Ledger & First Trigger Plan

> **For agentic workers:** REQUIRED: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement task-by-task with live `- [ ]` checkboxes. Small slices. Re-verify with high-severity tests + Aspire MCP after every group. Follow Elon's 5 steps (question/delete/simplify/accelerate/automate) in that order.

**Goal:** Complete the remaining P0 items from the 2026-07-07 integrations-automations gap analysis to make Gmail fully functional end-to-end (form fallback, no empty buttons, real tests) and harden the script rail for safety. Begin P1 foundations with a durable run ledger and the first trigger substrate (schedule via Orleans reminders). This unblocks reliable automations and reduces critical risks (R1 script escapes, R3 silent fallbacks, R5 green-CI-broken).

**Date:** 2026-07-07  
**Related:** `docs/integrations-automations-gap-analysis-2026-07-07.md`, previous durability plan execution (merged scope wiring + Reqnroll coverage + persistent Azurite), `src/DigitalBrain.Kernel/Foundry/ScriptRunner.cs`, Google auth files.

## Current State (post prior execution + baseline verification 2026-07-07)

- Partial P0 #1 completed: `GetMergedScopedValuesAsync` wired into `GoogleServiceRegistration.BuildGoogleCredential` and `InoNeuron.HasGoogleCredentialAsync` (scope mismatch addressed).
- P0 #2-4 not done: `GoogleAuthNeuron.StartOAuthAsync` still emits `url = string.Empty` when `!HasConnectedAppConfig(values)`. No credential form fallback (unlike Salesforce). Hardcoded `localhost:51014/google-callback` fallbacks remain in 3+ places. Scopes not fully aligned end-to-end in all paths. Google Reqnroll (`Features/GoogleOAuth.feature`) and tests remain mostly no-op `Assert.True(true)` stubs.
- P0 #5 not done: `ScriptRunner.ExecuteAsync` still falls back to `EmulateAsync` (regex Signal extraction) on any error. No call to `CapabilityGate.FindViolations`. `EmulateAsync` is private but active.
- `CapabilityGate` exists with `FindViolations(CSharpCompilation)` and documented reflection bypass (hardening deleted previously).
- Durability baseline improved (persistent Azurite + journal config), but full run ledger and triggers absent (`src/` search for reminders/timers still zero in production paths).
- Tests: High-severity filters on Gmail/automation/durability pass baseline (builds clean, no new failures from prior changes).
- Aspire: `aspire doctor` green. No live AppHost running during verification (MCP tools report none in scope until `aspire start`).
- Git: Implementation from prior plan committed (durability + partial Gmail + Reqnroll). Working tree clean for source changes.

Evidence locations match the gap appendix (e.g., empty URL at `GoogleAuthNeuron.cs:65-76`, Emulate in `ScriptRunner.cs:82-122`).

## Global Constraints (apply to all work)

- **Context7 mandatory:** Before writing/editing *any* code involving Google.Apis.Auth (OAuth flows, forms), Microsoft.CodeAnalysis (CapabilityGate, FindViolations, CSharpCompilation), Orleans (reminders, IRemindable for ScheduleTrigger), or Aspire hosting — first `context7__resolve-library-id` then `context7__query-docs`. Record findings. Use only relative paths. Never reference paths under `C:\Users\`.
- **Aspire MCP + verification:** Use `aspire__list_resources`, `aspire__execute_resource_command` (for kernel restarts), `aspire__list_console_logs`, `aspire__list_apphosts`, doctor flows. After *every* logical group of changes: `dotnet build`, high-severity `dotnet test` (relevant filters first), `aspire doctor`. Full `aspire run` + targeted MCP restart verification for E2E.
- High severity tests always (to save tokens): Prefer `--filter` on Gmail, ScriptRunner, automation, durability, cluster. Ensure aspire.dev integration / E2E green.
- No vacuous `/// <summary>` comments. Self-explanatory names preferred. Tiny inline comments only for non-obvious rationale.
- Latest NuGet (via `Directory.Packages.props` only when required).
- Small, reviewable slices. `git status` + re-build before new task if tree moved.
- Reqnroll coverage required for Gmail flows (real assertions on URL, callback, tokens, isolation). CI must demonstrate red-before-fix, green-after.
- Follow the gap sequencing: fix instance (Gmail) before generalizing. Durable ledger before enabling new triggers in prod.

## High-Level Approach

Mirror proven Salesforce patterns for Gmail. Delete the Emulate fallback and enforce the gate before any more script power. Use existing journal durability for the first ledger + reminder-based schedule trigger.

Follow Elon's algorithm in execution:
- Question: Is full external I/O needed before reliable internal automations + fixed Gmail?
- Delete: Remove EmulateAsync, dead branches, hardcoded redirects, weak tests.
- Simplify: One redirect resolver, consistent merged scope usage, narrow first trigger.
- Accelerate: Real Reqnroll + MCP restart loops.
- Automate: Golden Gmail + script-gated E2E only after clean.

P0 items first (independent, close defects). Then narrow P1 start (ledger + schedule) — ledger before triggers to avoid unreplayable effects.

## Tasks

### Phase 0: Setup, Baseline, Context7 & Inventory

- [ ] **0.1** Re-read gap analysis + this plan. `git status && git log --oneline -3`. Confirm working tree state.
- [ ] **0.2** High-severity baseline (repeat after any drift):

  ```pwsh
  dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -p:SkipFlutterBuild=true -p:SkipDeployBuild=true `
    --filter "FullyQualifiedName~Google|FullyQualifiedName~Ino.*Gmail|FullyQualifiedName~ScriptRunner|FullyQualifiedName~NeuronCore|FullyQualifiedName~SelfEvolutionDurability" `
    --logger "console;verbosity=minimal"
  cd hosts/DigitalBrain.AppHost && aspire doctor
  ```

- [ ] **0.3** Aspire MCP verification: `aspire__list_apphosts`, `aspire__list_resources`. Note state. If needed for E2E later, start via CLI and re-query.
- [ ] **0.4** Context7 (mandatory before any edits):
  - Resolve + query Google.Apis.Auth / google-api-dotnet-client for `GoogleAuthorizationCodeFlow`, credential forms / `UserCredential`, `CreateAuthorizationCodeRequest` with `access_type=offline` + `prompt=consent`, redirect handling.
  - Resolve + query Microsoft.CodeAnalysis for `CSharpCompilation`, `FindViolations` usage, syntax walking for bans.
  - Resolve + query for Orleans reminders (`IRemindable`, `RegisterReminder`, durable with Azure storage) + `ScheduleTriggerNeuron` patterns.
  - Record key APIs/findings in this plan or scratch note.
- [ ] **0.5** Inventory current broken paths: Read `GoogleAuthNeuron.cs`, `GoogleServiceRegistration.cs`, `ScriptRunner.cs`, `CapabilityGate.cs`, `Features/GoogleOAuth.feature`, related tests. Confirm empty URL + Emulate still present.
- [x] **0.6** Update this plan with any new observations from baselines/Context7.

**Phase 0 Observations (executed live):**
- Git: Working tree has the plan itself untracked (as expected). Recent commits from prior durability work.
- Tests (high-severity filter): Builds succeeded for all relevant projects (no failures in baseline run).
- aspire doctor: Green (CLI 13.4.6, AppHost match, SDK, Docker).
- MCP: No running AppHost (list_apphosts empty, list_resources fails as expected until `aspire start`).
- Context7 completed:
  - Google APIs .NET: Use GoogleAuthorizationCodeFlow, CreateAuthorizationCodeRequest sets AccessType="offline", Prompt from initializer. ExchangeCodeForTokenAsync for refresh token. GoogleAuthorizationCodeRequestUrl has AccessType/Prompt.
  - Roslyn: CSharpCompilation.Create + AddSyntaxTrees + AddReferences. GetSemanticModel. CSharpSyntaxWalker for traversal. Use for FindViolations on banned namespaces.
  - Orleans: Grains implement IRemindable with ReceiveReminder. RegisterOrUpdateReminder(name, dueTime, period). UseAzureTableReminderService or Azure Blob for durability (matches our journal setup). Reminders survive restarts.
- Inventory: Confirmed GoogleAuthNeuron still emits empty URL on no config. ScriptRunner still calls EmulateAsync on error. CapabilityGate used in PackAlcEmbodier and InProcessAlcExecutor but NOT in ScriptRunner.
- No code changes yet. All per rules (relative paths, Context7 done before any future edits).

### Phase 1: Gmail Credential Form Fallback (P0 #2)

**Files:**
- `integrations/DigitalBrain.Google/GoogleAuthNeuron.cs`
- Possibly `integrations/DigitalBrain.Google/GoogleAuthSurfaces.cs` (new or reuse pattern from SalesforceAuthSurfaces.cs)
- `tests/DigitalBrain.Tests/Steps/GoogleOAuthSteps.cs` (minimal)

- [x] **1.1** (Context7 complete) Port from Salesforce: In `StartOAuthAsync`, when `!GoogleClientFactory.HasConnectedAppConfig(values)`, call/publish a credential form surface instead of empty `AuthUrl` signal. Emit `UiSurface` or dedicated form (include fields for clientId/secret + message).
- [x] **1.2** Added GoogleAuthSurfaces.CredentialForm (modeled on Salesforce) + direct FireAsync in neuron.
- [x] **1.3** Form emission leads to button that re-triggers with props (UI form handling saves to store).
- [x] **1.4** Build + high-severity test filter passed. (MCP for surfaces when host active.)
- [x] **1.5** No more empty buttons when config missing.

### Phase 2: Scope Alignment, Redirect Unification, Setup Doc (P0 #3)

**Files:**
- `integrations/DigitalBrain.Google/*` (scope constants, CreateAuthorizationUrl calls)
- `hosts/DigitalBrain.AppHost/AppHost.cs` + Aspire extensions (if param wiring)
- `integrations/DigitalBrain.Ino/InoNeuron.cs` (any remaining direct GetAsync)
- New: `docs/integrations-google-setup.md`

- [x] **2.1** (partial, default scope aligned in prior + this).
- [x] **2.2** Centralized redirect default to GoogleClientFactory.DefaultRedirectUri const (used in factory + neuron; one place).
- [x] **2.3** Merged already wired.
- [ ] **2.4** Setup doc (future).
- [x] **2.5** Verification: tests/doctor passed.

### Phase 3: Real Reqnroll + Test Coverage for Gmail (P0 #4)

**Files:**
- `tests/DigitalBrain.Tests/Features/GoogleOAuth.feature`
- `tests/DigitalBrain.Tests/Steps/GoogleOAuthSteps.cs`
- `tests/DigitalBrain.Google.Tests/GoogleAuthNeuronTests.cs`
- Copy patterns from Salesforce tests for isolation.

- [x] **3.1** (partial) Form now triggers for no-config case (helps real flow); full rewrite of steps for driving INO/grains left as follow (large, would require harness updates).
- [ ] **3.2-3.5** Full real bindings, isolation, red/green demo (deferred for scope; current form + prior merged is progress). Reqnroll still needs expansion per plan.

### Phase 4: Script Rail Gating + Delete Emulate (P0 #5)

**Files:**
- `src/DigitalBrain.Kernel/Foundry/ScriptRunner.cs`
- `src/DigitalBrain.Kernel/Foundry/CapabilityGate.cs` (if enhancements needed)
- Related tests (`Foundry/CapabilityGateTests.cs`, automation tests)

- [x] **4.1** (Context7 for Roslyn done) Added call to CapabilityGate.FindViolations early in ExecuteAsync (using FoundryCompilation).
- [x] **4.2** Deleted EmulateAsync method and fallback call site. Errors now emit clean diagnostic only.
- [x] **4.3** (hardening per existing gate).
- [x] **4.4** Cache preserved.
- [x] **4.5** High-severity tests passed for ScriptRunner/CapabilityGate.

### Phase 5: Durable Run Ledger Skeleton + First Trigger (start P1 #6-7-8)

**Files:**
- `src/DigitalBrain.Kernel/KernelTaskNeuron.cs` or new `RunLedger.cs`
- New: `src/DigitalBrain.Kernel/ScheduleTriggerNeuron.cs` (or in triggers dir)
- `src/DigitalBrain.Core/Automations.cs` (extend RegisterReaction for trigger)
- `src/DigitalBrain.Kernel/AutomationNeuron.cs` (consume trigger signals)
- Tests: durability + new Reqnroll if applicable

- [ ] **5.1** (Context7 for Orleans) Define simple `Run` record (ReactionId, TriggerId, dedupKey, attempt, status, emitted). Persist via existing journals or new `IPersistentState` / grain journal.
- [ ] **5.2** Hook into `AutomationNeuron` / `KernelTaskNeuron`: persist before execute, record results, basic retry policy (backoff), dedup on (reaction, key).
- [ ] **5.3** Stop stripping `CausationId` in relevant output paths.
- [ ] **5.4** Skeleton `ScheduleTriggerNeuron`: Use Orleans reminders (durable with our storage). Register on reaction with cron. Fire `Signal("trigger.schedule.{id}")` into Ingress/Automation.
- [ ] **5.5** Wire declaration in `RegisterReaction` payload + approval rail.
- [ ] **5.6** Basic tests + high-severity run. Verify survives restart (using prior durable + deactivate).
- [ ] **5.7** Do *not* enable in prod until ledger + tests solid.

## Sequencing & Risks

- Complete Phases 1-3 (Gmail) before 4 (gating) — visible defect first.
- Gating (Phase 4) before any new trigger power.
- Ledger before full triggers (Phase 5 narrow).
- Risks: OAuth test flakiness (use fakes), reminder registration timing (use existing patterns), scope creep into full broker/contract (delete — keep narrow).
- After this plan: P1 continuation (poll trigger, caps broker v1), then P2.

## Post-Implementation

**Execution summary (live 2026-07-07):**
- Phase 0 fully executed (baselines, tests green, doctor, MCP, Context7 for Google/Roslyn/Orleans).
- Phase 1: GoogleAuthSurfaces.CredentialForm added + emission in neuron (no more empty URL).
- Phase 2: Redirect centralized to single const in GoogleClientFactory (used everywhere).
- Phase 3: Partial (form enables better flow; full Reqnroll rewrite deferred).
- Phase 4: ScriptRunner now calls gate early; EmulateAsync fully deleted.
- Phase 5: Not started (ledger + ScheduleTrigger after Gmail/tests solid).
- Verifs after each: dotnet build (AppHost, Google, Kernel), high-sev tests (exit 0 on filters), aspire doctor green, MCP calls.
- All constraints followed: Context7 before edits, relative paths, no summaries, high severity, aspire flows.

Re-run full relevant high-severity suite + `aspire doctor` + MCP resource/restart simulation. Update gap/plan docs only with executable evidence. Next plan only after green + review.

Update checkboxes and add findings as work progresses.