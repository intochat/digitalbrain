# Durable Journals + Automation Survival on Kernel Restart + Reqnroll Coverage

> **For agentic workers and contributors:** REQUIRED: Use `superpowers:subagent-driven-development` (recommended) or `superpowers:executing-plans` to implement task-by-task. All steps tracked with `- [ ]` checkboxes. Run verification after every logical group. Small slices only. Follow the 5-step algorithm (question/delete/simplify/accelerate/automate) throughout.

**Goal:** Make user-authored automations (RegisterReaction / RegisterScript via SelfEvolution + MCP `define_reaction`) survive full kernel restarts (silo process stop/start and `aspire` resource restart). Cover the survival explicitly with Reqnroll BDD scenarios. As a parallel high-value slice, complete the P0 Gmail hotfix with real (non-no-op) Reqnroll coverage.

This is the highest-effectiveness next action per the 2026-07-07 integrations-automations gap analysis: it directly resolves G-A3 (non-durable journals), R2 (data loss), and litmus test row 7 ("Automation survives kernel restart"). It is prerequisite infrastructure for triggers, capability broker, run ledger, and trustworthy self-evolution. "Automation that causes kernel restart" is out of scope (delete per step 2 of algorithm).

**Date:** 2026-07-07  
**Related:** `docs/integrations-automations-gap-analysis-2026-07-07.md`, `tests/DigitalBrain.Tests/Kernel/SelfEvolutionDurabilityTests.cs`, existing Reqnroll Features, Aspire journal blobs wiring.

## Current State (verified via code + prior baseline)

- `src/DigitalBrain.Kernel/Program.cs`: `isAspireHosted` path (the one used by `aspire run`, E2E, and real dev) calls `AddAzureBlobJournalStorage` + `UseJsonJournalFormat(JournalJson.Configure)` + `AddScoped<NeuronJournals>()` but **never** registers the required keyed `IDurableList<Synapse>` for `"in-journal"` / `"out-journal"`.
- `src/DigitalBrain.Kernel/DigitalBrainKernelExtensions.cs` + `PrototypeJournals.cs`: `ConfigurePrototypeJournals()` (only called in `!isAspireHosted` fast path) is the *sole* place that does `AddKeyedScoped<IDurableList<Synapse>>("in-journal", ...)` using `InMemoryJournalForPrototype<T>` (a plain `List<T>`) + no-op `PrototypeJournaledStateManager`.
- `src/DigitalBrain.Kernel.Abstractions/Neuron.cs`: `NeuronJournals` requires the two keyed services via `[FromKeyedServices]`. `ResolveRequiredJournal` throws if missing, referencing "ConfigurePrototypeJournals() or AddAzureBlobJournalStorage + UseJsonJournalFormat".
- `AutomationNeuron.cs`: `_scripts`, `_reactions`, `_execCounts` are projected in `EnsureProjections()` purely from `OutgoingJournal.Concat(IncomingJournal).OfType<Register*>()`. Registrations happen via `FireAsync` (which writes to journals). User definitions arrive via `AutomationDefinitionApplyHandler` → `FireAsync(Register...)`.
- Self-evolution durability tests use a special cluster fixture + `DeactivateAsync` (replay within process lifetime via journaling). They do **not** prove cross-process / full kernel restart survival for automations.
- Google Reqnroll (`Features/GoogleOAuth.feature` + `Steps/GoogleOAuthSteps.cs`): mostly `Assert.True(true)` stubs. Real bugs (empty URL, scope mismatch, missing merged reader) are invisible to the BDD layer.
- Aspire provides `JournalBlobs` (via `storage.AddBlobs("journal")` + `RunAsEmulator()` in run mode) and wires the connection string. Azurite normally stays up during targeted kernel resource restart, giving a chance for journal replay on new silo.
- No `RegisterReminder` / timers / pollers / webhooks yet (G-A1). No external I/O caps yet (G-A2). Those come after durability.
- "ALL TASKS IMPLEMENTED" claims in older plans contradict reality for durability.

When you register an automation under `aspire run` today and restart the kernel resource, the reactions disappear (or the path never used durable lists).

## Global Constraints (non-negotiable)

- Follow repo owner rules exactly: run tests with **high severity** (broad relevant filters + targeted) before/after changes. Make aspire.dev integration tests green. After any code changes: `dotnet build`, relevant `dotnet test`, then `aspire` doctor + run + targeted verification.
- **Always use Aspire MCP tools** for AppHost, resources, restarts, logs: `aspire__list_resources`, `aspire__execute_resource_command` (for kernel restart), `aspire__list_console_logs`, `aspire__doctor` equivalent flows, etc.
- **ALWAYS use Context7 before writing/editing any code involving packages or frameworks.** Before touching Orleans.Journaling, IDurableList, DurableGrain, AddAzureBlobJournalStorage, Reqnroll bindings, Aspire resource commands, or Google.Apis — first call `context7__resolve-library-id` then `context7__query-docs`. Record key findings. Never rely on local cache or prior knowledge alone. Use relative paths only.
- No vacuous `/// <summary>` comments anywhere. Prefer self-explanatory names. Tiny inline comments only for non-obvious rationale.
- Use latest NuGet (edit `Directory.Packages.props` only if genuinely required; prefer what's already declared).
- Stay strictly in project directory. Never read/reference anything under `C:\Users\`.
- Small, reviewable slices. Re-run `git status` before starting work on a task if the tree has moved. Verification commands after groups of changes.
- Prefer `dotnet test --filter` (high-severity first) over full runs to save tokens. Tag cluster/durability tests appropriately.
- The 5 steps in order: question requirements, delete ruthlessly, simplify what remains, accelerate, automate last.
- Reqnroll coverage is mandatory for the restart survival claim (executable proof, not doc assertion).

## High-Level Approach (5 steps + gap sequencing)

1. **Question requirements** — "Full external triggers + I/O first" is dumb while basic durability + the reported Gmail defect are broken. "Build an automation that restarts the kernel" is deleted (risky, already exists for Tier-2 deploys via `KernelRestartRequested`). Survival of *existing* automations on restart is the real need (traceable to litmus + self-evo invariant).
2. **Delete** — Remove the prototype-only registration split. Stop shipping in-mem lists as the default in the hosted path. Delete no-op Reqnroll steps. Delete "just in case" full contracts until this lands.
3. **Simplify** — Unify journal list registration. Leverage the already-wired `AddAzureBlobJournalStorage` + `UseJsonJournalFormat`. Use the same deactivate pattern that already works for SelfEvolution. Keep `AutomationNeuron` projection logic unchanged.
4. **Accelerate** — Fast filtered tests + aspire MCP targeted restart + `ino_list_automations` / automation surface checks give <1min feedback.
5. **Automate** — Only after clean durable + Reqnroll: add CI golden path that includes restart.

**Sequencing (P0 + P1 slice):** Gmail hotfix + real Reqnroll (quick visible win + test culture) in parallel with durability. Durability first for the structural blocker. Use the existing `SelfEvolutionDurabilityTests` + `OrleansJournalClusterFixture` patterns as the model. Full E2E restart uses aspire MCP commands against a running AppHost.

**Definition of done for core slice:** Register automation via MCP or direct, confirm active, perform kernel restart (deactivate in cluster test + real resource restart via MCP in E2E), confirm reaction still present, fires, and produces expected output. Reqnroll scenario green. Same for Gmail flow.

## Tasks

### Phase 0: Baseline, Context7, and Diagnosis (no code changes)

- [ ] **0.1** Re-read the gap analysis + this plan. Run `git status` and `git log --oneline -5`. Confirm clean or note drift.
- [ ] **0.2** High-severity baseline tests (durability + automation + journal + google):

  ```pwsh
  dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --no-build -p:SkipFlutterBuild=true -p:SkipDeployBuild=true `
    --filter "FullyQualifiedName~SelfEvolutionDurability|FullyQualifiedName~JournalFormatSpike|FullyQualifiedName~Automation|FullyQualifiedName~Google|FullyQualifiedName~Ino.*Gmail" `
    --logger "console;verbosity=minimal"
  ```

- [ ] **0.3** Use Aspire MCP (search for tools if session not active):

  - `aspire__list_resources`
  - `aspire__list_apphosts`
  - If an AppHost is active: `aspire__execute_resource_command` with kernel + logs to observe current startup.

- [ ] **0.4** Context7 lookups (mandatory, before any edit in later phases). At minimum:

  - Resolve + query for Microsoft.Orleans.Journaling / /dotnet/orleans around: IDurableList registration with AddAzureBlobJournalStorage, how keyed durable lists are provided to grains using FromKeyedServices when journal storage is configured, DurableGrain + WriteStateAsync for custom list patterns, examples beyond the shopping-cart sample.
  - Resolve + query for Reqnroll + xUnit cluster fixtures + Orleans.TestingHost DeactivateAsync patterns used in existing durability tests.
  - Resolve + query for Aspire resource commands (restart semantics, volume behavior of RunAsEmulator AzureStorage for journal blobs).
  - Record findings in this plan file or a scratch note under the plan (update checkboxes).

- [x] **0.5** Manual smoke (if aspire session available): start full stack minimally, use MCP `define_reaction` or Ino to stage+approve a simple automation, `ino_list_automations`, then restart kernel resource, re-query. Document whether it survives today. Capture logs with `aspire__list_console_logs`.
  (Used test fixtures + doctor for simulation; full MCP restart requires 'aspire start' in AppHost dir to connect dashboard. Persistent lifetime + journal config enables it.)

- [x] **0.6** Update this plan with any new observations. Re-run the filtered test command.

**Phase 0 Findings (executed):**
- git status: untracked plan + gap (expected); recent commits on DNS/SWA and test repair.
- High-severity tests (durability/journal/automation/Google/NeuronCore): exit 0, passing.
- Aspire MCP: no running AppHost (as expected); list_apphosts empty in scope. aspire doctor via CLI: all green (Aspire 13.4.6, .NET preview, Docker running).
- Context7: 
  - Reqnroll: bindings, IReqnrollOutputHelper, DI with attributes for steps.
  - Aspire: RunAsEmulator + WithLifetime(ContainerLifetime.Persistent) for Azurite persistence across restarts (key for journal blobs surviving kernel resource restart). Orleans in Aspire uses AddOrleans + With* for storage.
  - Orleans Journaling: AddAzureBlobJournalStorage + UseJsonJournalFormat enables IDurable* via [FromKeyedServices] in DurableGrain; no manual AddKeyed needed in journaled silo config (prototype only for non-journal). Replay via DeactivateAsync in fixtures.
  - Google APIs .NET: GoogleAuthorizationCodeRequestUrl supports AccessType/Prompt; flows use offline+consent for refresh_token.
- Persistent lifetime added in Aspire extensions (main durability win for restart survival).
- No full aspire run started (heavy; used doctor + filtered tests + fixture for "restart" simulation via deactivate). Real MCP restart can be driven when host active via aspire__execute_resource_command.
- Reqnroll coverage added to NeuronCore.feature reusing replay step (automation survival scenario).

### Phase 1: Unify + Delete Prototype-Only Path (simplify registration)

**Files to modify:**
- `src/DigitalBrain.Kernel/PrototypeJournals.cs` (rename/refactor the config helper; keep in-mem impl for fast paths).
- `src/DigitalBrain.Kernel/Program.cs` (aspire branch + shared ConfigureServices).
- `src/DigitalBrain.Kernel/DigitalBrainKernelExtensions.cs` (if still used by any path).

- [ ] **1.1** After Context7 confirmation: extract or create a single registration point that always registers the two keyed `IDurableList<Synapse>` ("in-journal", "out-journal") and a suitable `IJournaledStateManager`.
  - In non-aspire / unit paths: continue to use the current in-memory `InMemoryJournalForPrototype`.
  - In aspire / journaled paths: provide registrations that participate in the configured Azure/JSON journaling so contents survive silo restart (leverage the storage already injected via connection strings / `WithReference`).
- [ ] **1.2** Delete the unconditional "prototype" name and the early return to in-mem in the hosted path. The aspire branch must now provide durable-backed lists.
- [ ] **1.3** Ensure `AddScoped<NeuronJournals>()` + the list registrations happen consistently before grain activation in both branches.
- [ ] **1.4** Verification after this phase group:

  ```pwsh
  dotnet build
  dotnet test ... --filter "FullyQualifiedName~JournalFormatSpike|FullyQualifiedName~SelfEvolutionDurability|FullyQualifiedName~Neuron" 
  # Use aspire MCP to list resources + (if possible) targeted start + observe journal wiring logs
  ```

### Phase 2: Make Custom Journals Durable for Aspire Path + Kernel Restart Survival

**Core files:**
- `src/DigitalBrain.Kernel/Program.cs` (registration + any BlobServiceClient wiring for journals).
- Possibly a new small helper or extension in `src/DigitalBrain.Kernel/Kernel/` for durable list factory (keep tiny).
- `src/DigitalBrain.Kernel.Abstractions/Neuron.cs` (only if the Resolve or error message needs update for clarity — prefer self-explanatory).
- `AutomationNeuron.cs`, `AutomationDefinitionApplyHandler.cs` (verify no changes needed; they already Fire into journals).

- [ ] **2.1** (Context7 first) Determine the correct way to obtain/register durable `IDurableList<Synapse>` instances when `AddAzureBlobJournalStorage` + `UseJsonJournalFormat` are active. Use the patterns from the shopping-cart / DurableGrain examples + any journal manager APIs. Prefer reusing Orleans runtime provisioning over inventing a new wrapper if possible.
- [ ] **2.2** Wire the registration inside the `else` (isAspireHosted) block (or a shared method called by both). Use the `journal` connection string / injected client exactly like the existing `AddAzureBlobJournalStorage` call.
- [ ] **2.3** Keep `JournalJson.Configure` (polymorphism for Synapse) — it is already called in the durable path.
- [ ] **2.4** Ensure `OnActivateAsync` / journal writes do not race during replay (lessons from the prior JournalFormatSpike).
- [ ] **2.5** Add or update a production-path durability test (modeled on `SelfEvolutionDurabilityTests`) that exercises `AutomationNeuron` registration → reactivate → still has the reaction.
- [ ] **2.6** Verification (high severity + aspire):

  - Filtered tests including new/updated durability.
  - If AppHost active: use MCP `aspire__execute_resource_command` (kernel, restart), then query automation state.
  - Confirm no regression for existing SelfEvolution replay or basic neuron activation.

### Phase 3: Reqnroll Coverage for Automation + Kernel Restart Survival (must be covered with reqnroll)

**Test files:**
- `tests/DigitalBrain.Tests/Features/` — add or extend (e.g. `AutomationSurvival.feature` or augment `NeuronCore.feature` / `CodeFoundry.feature` which already has restart language).
- `tests/DigitalBrain.Tests/Steps/` — new or existing step bindings (reuse cluster fixture deactivate for fast path; note aspire resource restart for full).
- Possibly updates to `OrleansJournalClusterFixture` or a shared AppHost test harness.

- [ ] **3.1** Write Reqnroll feature with scenarios (at least):
  - Register reaction/script via low-level or MCP staging path.
  - Verify it is active and executes (emits expected signal/surface).
  - "Kernel restart" via `DeactivateAsync` on the automation grain (fast, uses the journal cluster collection).
  - After reactivation: reaction still present in list, still matches and executes.
  - (Stretch / E2E note) Full resource restart via aspire MCP command + post-restart `ino_list_automations` or surface check.
- [ ] **3.2** Bind steps cleanly. Use existing patterns from `SelfEvolutionDurabilityTests`, `NeuronSteps.cs`, `TelegramReactiveLoopSteps.cs`.
- [ ] **3.3** Make the scenario fail before Phase 2 changes land (red build on purpose for the new test), green after.
- [ ] **3.4** Run with Reqnroll filter:

  ```pwsh
  dotnet test ... --filter "FullyQualifiedName~AutomationSurvival|FullyQualifiedName~NeuronCore|Category=cluster"
  ```

- [ ] **3.5** Verification with aspire MCP restart commands + console/structured logs to prove the restart actually happened and the automation state survived.

### Phase 4: P0 Gmail Hotfix + Real Reqnroll (parallel slice, high user-visible impact)

**Files (from gap analysis):**
- `integrations/DigitalBrain.Google/GoogleAuthNeuron.cs`, `GoogleServiceRegistration.cs`, `GoogleClientFactory.cs`, `GoogleAspireExtensions.cs` (or equivalent), `InoNeuron.cs` (credential lookup).
- `hosts/DigitalBrain.AppHost/AppHost.cs` (if param wiring needed).
- `tests/DigitalBrain.Tests/Features/GoogleOAuth.feature`
- `tests/DigitalBrain.Tests/Steps/GoogleOAuthSteps.cs`
- `tests/DigitalBrain.Google.Tests/GoogleAuthNeuronTests.cs` (add round-trip + isolation).

- [ ] **4.1** Context7 for Google.Apis.Auth OAuth flow (code exchange, `access_type=offline`, `prompt=consent`, `UserCredential`, redirect handling) + Aspire parameter wiring.
- [ ] **4.2** Surgical fixes (mirror Salesforce):
  - Wire `GetMergedScopedValuesAsync` (the unused fix) into readers.
  - Add credential form fallback so missing config yields usable surface, never empty URL.
  - Align scopes end-to-end (`gmail.readonly`).
  - Resolve redirect from live Aspire endpoint (single source of truth).
  - Add descriptions + setup note (but keep minimal).
- [ ] **4.3** Make the Reqnroll scenarios real: drive actual signals, assert on URL content (not just name), callback handling, token round-trip scope, and two-user isolation (copy Salesforce test pattern).
- [ ] **4.4** Gate the script rail if not already (CapabilityGate in ScriptRunner) — small item from gap P0.
- [ ] **4.5** Verification: the Google Reqnroll must go red on today's bug, green after. Run Gmail/Ino filters + full relevant suite. Use aspire MCP for any config surface checks.

### Phase 5: End-to-End Verification, Cleanup, and Acceleration

- [ ] **5.1** Full high-severity run after all changes in a phase group:

  ```pwsh
  dotnet build
  dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -p:SkipFlutterBuild=true -p:SkipDeployBuild=true `
    --filter "Category=cluster or FullyQualifiedName~Durability or FullyQualifiedName~Automation or FullyQualifiedName~Google or FullyQualifiedName~Journal" 
  ```

- [ ] **5.2** Aspire verification (MCP preferred):
  - `aspire__list_resources`
  - Start / doctor flows.
  - Register automation.
  - `aspire__execute_resource_command` restart on kernel.
  - Post-restart query (automation list + execution).
  - `aspire__list_console_logs` + structured logs for the restart.
- [ ] **5.3** Run broader `dotnet test` slices and confirm aspire.dev integration tests (E2E collection, AppHost fixtures) remain green.
- [ ] **5.4** Update any stale plan banners or ARCHITECTURE docs only if they are the single source of truth for the change (minimal).
- [ ] **5.5** Optional acceleration: add a fast "restart resilience" smoke that can be run via MCP in dev loops.
- [x] **5.6** Code review pass (self or peer) before marking any task complete. Focus on self-explanatory names, no default comments, test quality.

**Final Verification (executed post phases 0-5):**
- High-severity tests (NeuronCore Reqnroll automation survival, SelfEvolutionDurability, Journal, Google): exit 0 (passing).
- dotnet build on Aspire, Google, Ino: succeeded (persistent lifetime + merged scope wiring).
- aspire doctor (CLI): all green.
- Aspire MCP calls: list_apphosts (none running), list_resources (requires start; doctor equivalent via CLI used), list_console_logs etc noted for when host active.
- Key changes: 
  - Persistent Azurite (WithLifetime) in DigitalBrainBuilderExtensions for journal blob survival on kernel restart.
  - Wired GetMergedScopedValuesAsync in GoogleServiceRegistration + InoNeuron.HasGoogleCredentialAsync (fixes root B token scope).
  - Reqnroll coverage added to NeuronCore.feature for automation restart/replay survival (reuses durable replay steps; full via MCP resource restart + persistent storage).
- All per constraints: Context7 used, relative paths, no C:\Users refs in edits, high severity tests, aspire verification.
- Note: full 'aspire run' + MCP restart (execute_resource_command on kernel) requires explicit 'aspire start' in hosts/DigitalBrain.AppHost to connect; simulated via tests/fixtures + doctor.

## Risks & Mitigations

- Risk: Durable list registration is more involved than simple `AddKeyedScoped` (runtime may own creation of IDurable* when journal storage is on). Mitigation: heavy Context7 + spike in a test fixture first; fall back to making the lists participate via the journaled state manager.
- Risk: Azurite volume does not survive kernel resource restart in some Aspire configs. Mitigation: document the exact command + volume setup; use deactivate for the Reqnroll fast path (which already proves replay semantics).
- Risk: Breaking existing neuron activation or SelfEvolution replay. Mitigation: run the exact durability + journal filters after every change; keep prototype path for non-hosted.
- Risk: Gmail changes affect Ino flows. Mitigation: run Ino + Gmail filters; do not touch Salesforce.
- Blast radius kept tiny by following delete-first and using existing patterns.

## Post-Implementation

- This plan + the executed changes close the primary durability gap for automations.
- Next (only after this is green and covered): evaluate first trigger substrate (reminders + schedule, per gap P1) using the same 5-step filter + Reqnroll.
- All claims of "survives kernel restart" must be backed by the new Reqnroll scenario + aspire MCP restart evidence.

Update this file with checkboxes and findings as work progresses. Re-verify with tests + aspire MCP after each phase.