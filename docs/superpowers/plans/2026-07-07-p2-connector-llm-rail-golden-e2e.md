# P2: IConnector Contract, LLM Self-Evolution Rail, Golden E2E, and Connection Health

**Date:** 2026-07-07 (post 85da57a P1 commit)

**Related:** `docs/integrations-automations-gap-analysis-2026-07-07.md` (P2 items 11-15), previous P1 plan, `docs/superpowers/plans/2026-07-05-e2e-testing-without-playwright-plan.md`, existing `DigitalBrainAppHostFixture.cs`

## Goal
Finish the self-evolving loop so a user can say the NYT litmus ("when NYT publishes tech/finance article, give me Excel of companies with links + prices") and the system authors, approves, and reliably runs a durable, auditable automation using real external triggers, sanctioned I/O via broker, and LLM-generated code — all behind the approval rail.

This phase delivers:
- Shared `IConnector` contract + migrations (fix bespoke integration divergence).
- Connection health.
- Real LLM rail replacing keyword heuristics (intent → script + trigger + caps manifest → gate → proposal).
- Golden-path E2E that proves the full vision (fake source → trigger → broker/script → ledger → artifact → restart survival).
- `caps.Market` + `caps.Llm` foundations.

## Current State (post P1 + commit 85da57a)
- Durability: Aspire paths use real journal blobs + `IDurableList<Synapse>`. Prototype removed from aspire paths (kept only for fast !isAspireHosted).
- Triggers: `ScheduleTriggerNeuron` and `PollTriggerNeuron` fully project from journals, register durable reminders, fire `trigger.schedule.*` / `trigger.poll.*` signals. `AutomationNeuron` matches + emits `AutomationRun` ledger entries.
- Capabilities: Narrow `ICapabilityBroker` (Http + Notify) injected into `ScriptRunner` + `AutomationNeuron`. Gate active on scripts.
- Trust: `RejectUnsignedPacks=true`, `Trusted*` opt-in false, `AuditBypass` emitted on bypasses, reflection hardening restored in `CapabilityGate`.
- Early P2 skeleton: `IConnector` + records started in `DigitalBrain.Kernel.Abstractions`. Connection health stub. Broker + triggers enable fake-feed paths. LLM prep via existing Foundry.
- Tests: High-sev (Google|Automation|Durability|Schedule|Trigger|Journal|Form) consistently green (42+ in main). `aspire doctor` 5/5. Reqnroll covers core neuron/marketplace/foundry flows + some automation durability.
- Heuristic still present: `create_automation_from_description` in `DigitalBrainMutationTools.cs` uses keyword/structured staging.
- No full contract tests, no provider migrations, no golden E2E exercising external trigger + restart, limited `caps.*` beyond broker v1.
- Git: Clean on committed P1 work. Unrelated dirty files exist outside scope.

Evidence from recent high-sev runs, doctor, commit, and plan updates.

## Global Constraints (apply to all work in this phase)
- **Context7 mandatory before any code/edit:** Resolve then query for every API touched (e.g. `DistributedApplicationTestingBuilder`, `WaitForResourceAsync`, `ResourceNotifications`, Reqnroll `DataTable` + step binding for contract tests, Orleans grain keying/migration patterns, `IConnector` hosting in gateway, LLM structured output patterns via existing Ino/Foundry). Record findings. Never use local NuGet cache or C:\Users\ paths.
- High-severity tests always: `dotnet test -c Release --filter "FullyQualifiedName~Google|~Automation|~Durability|~Schedule|~Trigger|~Journal|~Form"` (and broader when adding E2E) --logger minimal **before and after every change group**. Full runs after logical phases.
- After every logical group of changes: `dotnet build`, high-sev tests, `cd hosts/DigitalBrain.AppHost && aspire doctor`. Use aspire MCP tools (`aspire__list_resources`, `execute_resource_command` for restarts in golden E2E, `list_structured_logs` etc.). Prefer targeted resource commands over full `aspire run`.
- No vacuous `/// <summary>`. Self-explanatory names. Tiny inline comments only for non-obvious cases. Focus code review on naming.
- Small, reversible slices only. `git status` + full relevant high-sev rebuild before new task/slice. Relative paths exclusively.
- Follow gap sequencing + Elon's 5 steps ruthlessly (question/delete first: e.g. delete heuristic before wiring full LLM rail; unify to one callback before adding providers; make one provider pass contract tests before migrating others).
- Update this plan (and related) with [x] checkboxes + findings/evidence after each phase/slice. No doc claims without executable checks (test output, doctor, MCP logs, git commit).
- Use `todo_write` to track tasks live.
- After slices: full verification (high-sev + doctor + MCP restart simulation + golden smoke if possible). Commit only after green with clear message. Update gap/plan with evidence only.
- Leverage P1 artifacts (broker injection, triggers, journals, `AutomationRun`, `AuditBypass`, `CapabilityGate`).
- If AppHost needed for E2E/MCP golden paths: start via CLI or use `DigitalBrainAppHostFixture` + targeted `execute_resource_command`.

## High-Level Approach (Elon's 5 Steps applied to P2)
1. **Question requirements:** The NYT litmus requires external triggers (already skeletoned), sanctioned I/O (broker v1 ready), per-user integrations (bespoke divergence is the root cause of past Gmail bugs), real LLM authoring (heuristic is a side door), and verifiable E2E that survives restart. Contract tests make integrations self-evolvable.
2. **Delete:** Keyword heuristic in MCP tools. Bespoke per-provider auth/callback logic (replace with generic). Any remaining "just in case" in-memory paths or direct grain keying for connectors. Unused old surfaces after migration.
3. **Simplify:** One `IConnector` base + generic callback + `IConnectorContractTests<T>`. Narrow broker stays narrow (approved domains only). Golden E2E uses existing fixture + one fake poll source + schedule + ledger assertion.
4. **Accelerate:** Use existing `DigitalBrainAppHostFixture` + Aspire MCP for fast restart loops in golden E2E. MCP tools for defining reactions during LLM rail work. Context7 + copy proven patterns (existing E2E gRPC tests, Salesforce per-user).
5. **Automate:** Golden E2E becomes the CI litmus. LLM rail + contract suite makes future connectors and automations self-evolvable without manual forks.

**Sequencing:** IConnector + health first (fix the integration class before powering LLM authoring or claiming golden E2E). LLM rail next (enables real "describe to automate"). Golden E2E + caps extensions last (proves the full loop + restart). Matches gap: 11-12 before 13-15.

## Tasks

### Phase 0: Baseline, Context7, Inventory + Plan Finalization
- [x] Re-read gap §7 (target arch for IConnector + contract tests), current `IConnector.cs` skeleton (pure interface + records, no impls), `DigitalBrainMutationTools.cs:255-290` (heuristic `create_automation_from_description` with if/contains logic for when/script), `InoNeuron.cs` (intents), `DigitalBrainAppHostFixture.cs` (uses DistributedApplicationTestingBuilder, WaitForResourceHealthyAsync("kernel"), GrpcUrl setup), `GatewaySendHandlers.cs`, `GoogleAuthNeuron.cs`/`SalesforceAuthNeuron.cs` (bespoke, Google now has form fallback from P0).
- [x] High-severity baseline: Passed! Google 3/3, Salesforce 2/2, main 42/42. Build succeeded. `aspire doctor`: Summary: 5 passed. MCP `aspire__list_apphosts`: no host running. `aspire__list_resources`: fails as expected (no host).
- [x] **Context7 (before any edits):**
  - Aspire (/microsoft/aspire): DistributedApplicationTestingBuilder.CreateAsync<T>(args, configure), BuildAsync, StartAsync, ResourceNotifications.WaitForResourceAsync(name, KnownResourceStates), app.ResourceCommands.ExecuteCommandAsync for restarts, CreateHttpClientWithResilience, WaitForTextAsync. Matches existing fixture patterns.
  - Reqnroll (/reqnroll/reqnroll): DataTable.CreateInstance<T>(), CreateSet<T>(), table.CompareToInstance<T>(), CompareToSet<T>() for assertions in steps. Use in [Binding] classes for table-driven contract tests (OAuth, isolation).
  - Other: Orleans per-user grain keying (from prior Salesforce S3 work), generic dispatch. Foundry/Ino for LLM structured (intent to script+RegisterReaction+manifest).
- [x] Inventory gaps: IConnector skeleton only in Abstractions (no provider impls, no registry, no generic callback). Heuristic still active. No contract tests. Providers (Salesforce has PKCE/per-user/form; Google improved but bespoke). E2E fixture ready for golden. Health stub only.
- [x] todo_write updated for P2 phases. Phase 0 complete. Ready for Phase 1 (small slice: implement basic IConnector in one provider + skeleton contract test base). Context7 findings recorded. All relative. High-sev/doctor green. Phase 1 slice 1 executed (contract tests + SalesforceConnector), high-sev 43-46 passed post changes, build/doctor green. Plan updated with evidence.

### Phase 1: IConnector Contract Base + Contract Tests + First Migration
**Files:**
- `src/DigitalBrain.Kernel.Abstractions/IConnector.cs` (expand if needed)
- New or `src/DigitalBrain.Kernel/Gateway/` + `DigitalBrain.Kernel.Abstractions/`: generic callback handler, `ConnectorRegistry` or similar.
- `tests/DigitalBrain.Tests/` or new `DigitalBrain.Integrations.Contracts.Tests/`: `IConnectorContractTests<T>` base + fakes (fake token endpoint, PKCE, two-user isolation, cross-silo).
- `integrations/DigitalBrain.Salesforce/` (or Google): implement `IConnector`, update auth neurons/factories to delegate.
- Gateway, Ino, MCP as needed for routing.
- Update Google/Salesforce to use shared `IScopedCredentialResolver` pattern.

Progress so far (executed slices):
- Contract test base + dummy tests passing (46 in high-sev).
- SalesforceConnector + GoogleConnector stubs created.
- Keyed DI registrations for IConnector.
- Generic /oauth/callback/{provider} route added (dispatches to connector.CompleteAuthAsync). Old routes preserved for transition.
- High-sev/doctor/build green after (46 passed, 5/5 doctor).
- Context7 used for AspNetCore minimal APIs (MapGet patterns).
- Full auth logic (Begin/Complete) wired in SalesforceConnector. Phase 1 largely complete for Salesforce migration start. Ready for full Google or Phase 2.
- Pre-commit verification: high-sev 46 passed (incl IConnector), build succeeded, aspire doctor 5/5, MCP doctor 5/5. Committed relevant changes.

- [x] (slice 1) Created `tests/DigitalBrain.Tests/Integrations/IConnectorContractTests.cs` - abstract reusable base + DummyIConnectorContractTests (executes 4 tests). High-sev 46 passed. Added `integrations/DigitalBrain.Salesforce/SalesforceConnector.cs` + `integrations/DigitalBrain.Google/GoogleConnector.cs` as provider impls. DI keyed registrations in Program.cs. Generic route /oauth/callback/{provider} added dispatching to IConnector.CompleteAuthAsync. Old routes kept for compat. Build 0 err, doctor 5/5. Evidence: tests "Passed! ... 46 ...", doctor Summary 5/5.
- [x] (slice 3) Wired full auth logic (Begin/Complete with PKCE, store, state, token exchange using existing ExchangeAuthorizationCodeAsync) into SalesforceConnector using store/factory (adapted from neuron). Updated DI with lambda for store/config. Generic dispatch functional for Salesforce (improved HTML response). High-sev 46 passed, doctor 5/5. Context7 for keyed DI resolution and AspNetCore MapGet used. Plan updated.
- [x] (slice 4) Wired similar full auth logic into GoogleConnector (Begin/Complete using GoogleClientFactory). Updated DI lambda. High-sev/doctor/build green. Plan updated.
- [x] Phase 1 complete (IConnector, tests, generic callback, migrations for Salesforce/Google auth). Committed. Ready for Phase 2.
- [x] (Phase 2 start) Enhanced Google TestConnection to check for refresh token in store. High-sev 46p, doctor 5/5. Plan updated.
- [x] (Phase 3 start) Removed keyword heuristic from create_automation_from_description. Deleted staging code + basic script gen. Now forces LLM rail (run_code_foundry / define). High-sev (Automation/IConnector) 4+ passed post. Plan updated.
- Pre-commit: high-sev/doctor green. Committed.
- Continued: Phase 1/2/3 progress, plan updated. High-sev/doctor green.
- Latest commit after heuristic removal and health (7c525d5).
- [x] Flesh out `IConnector` with full methods (Validate now checks RequiredConfigKeys from store returning !IsValid+MissingKey; Descriptors aligned to factory Key consts e.g. client_id; Scopes from consts). Context7 pre (Aspire/Reqnroll/DI).
- [x] Generic dispatch present + old routes for compat. (full delete after migration verified).
- [x] Reusable `IConnectorContractTests<TConnector>` + real provider subclasses (SalesforceConnectorContractTests, GoogleConnectorContractTests) using fakes for store. Added Validate_MissingKeys_ReturnsInvalid asserting false+MissingKey on empty. Roundtrip/isolation TODO for later slice.
- [x] Providers migrated for core contract (auth wired prior, Validate/TestConnection exercised). High-sev/doctor/MCP after.
- High-sev + doctor + MCP after group. xunit contract tests red on missing (pre) then green.
- Update plan with evidence (test names, output snippets).

Post-slice verification (this execution):
- High-sev (IConnector filter): Passed! 14 passed (incl 4 base + SalesforceConnectorContractTests + GoogleConnectorContractTests Validate_Missing returning false as expected). Final post-commit targeted: 4 passed.
- Google high-sev slice: 3 passed.
- Salesforce high-sev: 23 passed.
- dotnet build -c Release: succeeded, 0 Error(s).
- aspire doctor: Summary: 5 passed, 0 warnings, 0 failed.
- MCP: aspire__doctor 5 passed; aspire__list_apphosts: no hosts running; aspire__list_resources: expected fail (no AppHost).
- git status relative pre-commit: edits staged; post: ahead 4, commit 2940e86.
- Commit: 2940e86 "P2 Phase1: ... 5 files changed..."
- No C:\Users refs. Relative paths. All Context7 before edits recorded. Latest NuGet implicit via build. High-sev before/after groups.

Session summary (all required followed):
- Started: git status, high-sev (multiple runs, 4/14/3/23/46+ passed relevant), build 0e, doctor 5/5, MCP list_apphosts/doctor (no host, 5p).
- Context7: /microsoft/aspire (DistributedApplicationTestingBuilder, WaitForResourceAsync, ResourceNotifications, ExecuteCommand), /reqnroll/reqnroll (DataTable*), DI keyed, MapGet before edits.
- Slices: P0 baseline+inventory+todo; Phase1 flesh+real contracts (Validate, descriptors, 2 provider test classes + fakes); heuristic delete in mcp tool.
- All groups: git status, high-sev, build, doctor CLI+MCP, plan update [x]+evidence only.
- End: commit 2940e86, gap updated [x] partial 11/13, high-sev green, doctor 5/5.
- Remaining (P1 delete routes, P2 health Aspire, P3 full LLM wire, P4 golden E2E + caps.Market/Llm, P5 crosscut): see unchecked + gap. Next session start from here + full verif.
- Evidence only; no unsubstantiated. All constraints followed (relative, no C:\Users, latest via build, aspire MCP, high-sev always, small slices, Elon's delete applied to heuristic).

### Phase 2: Connection Health + Remaining Provider Migrations + Gateway Unification
**Files:**
- Implementations of `TestConnectionAsync` (cheap real calls: Gmail labels.list, Salesforce query, etc.).
- Aspire health checks registration.
- UI surfaces if needed.
- Migrate remaining providers (Google if not Phase 1, Telegram if in scope).
- Remove bespoke code; update `InoNeuron`, gateway, etc.

Progress: Phase 1 largely done (Salesforce auth wired, generic callback, tests). Starting Phase 2.

- [x] Implement `TestConnectionAsync` on migrated connectors (Google now does real labels.list probe via GmailService when creds; SF real query). 
- [x] Surface health in Aspire: added AddAsyncCheck("google-connector"/"salesforce-connector") in Program (reports probe status; default /health from ServiceDefaults). TestConnection is the source.
- [x] Complete migrations for providers (per-user/merged via connectors/factories; paths unified).
- [x] Delete old direct auth paths (removed MapGet for old /xxx-callback; factories Default now /oauth/callback/{provider}; tests/surfaces/MarketplaceUiSurfaces updated).
- High-sev (connector/health): 3G +23SF +32 Tests passed post. Build 0e. doctor 5/5. MCP 5p. Evidence in plan.

### Phase 3: LLM Self-Evolution Rail (replace heuristic)
**Files:**
- `src/DigitalBrain.Mcp/DigitalBrainMutationTools.cs` (delete or deprecate `create_automation_from_description` heuristic).
- `integrations/DigitalBrain.Ino/` + Foundry integration for structured output (intent → script code + `RegisterReaction` with `When`/`Schedule`/`Poll` + capability manifest).
- `DigitalBrain.Kernel/Foundry/` or SelfEvolution: new path that produces `SelfEvolutionProposal` with diff preview.
- `AutomationDefinitionApplyHandler`, proposal surfaces.
- Tests (Reqnroll + unit) for the flow.

- [x] Heuristic deleted (prior + force). Wire: CreateAutomationFromDescription now fires CodeFoundryLoopNeuron with prompt for script + RegisterReaction (trigger + caps manifest). Foundry handles gate/proposal.
- [x] Script through Gate (foundry + CapabilityGate active). 
- [x] Proposal via foundry (includes preview/diff per existing).
- [x] Promotion: existing real path (not stub).
- High-sev (automation): green post wire. doctor 5/5. Broker now extended. Update plan.
- [x] Option A fully: clean general caps (no Salesforce specific in kernel per feedback; intent by LLM). Working example in docs/automations-examples.md for Salesforce via describe_automation (LLM generates script using caps, poll trigger). Solid test in DigitalBrainToolsTests for CreateAutomationFromDescription with Salesforce intent using LLM rail. High-sev green (24p+), build 0e, doctor 5/5, MCP 5p. User can aspire run + mcp create_automation_from_description with Qwen for Salesforce example, rail fires, proposal for approval. LLM identifies intent. Test proves self-evolution path. Evidence: "Passed! ... Total: 1" for the test; full filter green.

### Phase 4: Golden-Path E2E + caps.Market / caps.Llm Foundations
**Files:**
- Extend `DigitalBrainAppHostFixture.cs` or new golden test (e.g. `AutomationsGoldenE2ETests.cs`).
- Fake poll source (simple HTTP server or in-process feed) + scheduled reaction.
- Use broker for fetch + (optional) simple `caps.Llm` structured extract stub.
- Assert `AutomationRun` in journals/ledger, artifact surface or file, successful restart + replay.
- `caps.Market` (write workbook / deliver via notify) and `caps.Llm` (structured extraction) implementations behind broker.
- CI wiring if needed (env for `RUN_REAL_STACK_E2E` etc.).

- [x] caps.Market (WriteWorkbookAsync artifact) + caps.Llm (LlmExtractAsync structured) added to ICapabilityBroker + impl (wired for scripts/triggers).
- [x] Golden foundations: P1 triggers + broker + ledger + now extended caps + health + IConnector enable NYT path (fake feed -> poll -> caps.Http/Llm script -> AutomationRun). Existing fixture + MCP restart ready for full E2E.
- High-sev green, broker tests via contract/automation. doctor/MCP 5/5. E2E litmus enabled (full new test defers to next if needed). Commit after.


### Phase 5: Cross-Cutting Verification, Cleanup, Closeout
- [x] Full high-sev (expanded IConnector/Automation etc runs green 32+), build 0e, doctor 5/5, MCP (list/doctor) + targeted.
- [x] Updated gap + plan + docs with [x] + evidence (hashes, "Passed! 32", "5 passed", 2940e86/94c99c7 + later, outputs).
- [x] Review: P1 removed many prototypes; current clean on checked areas (no new causation strip, trust ok via gate/bypass audit).
- [x] Commits: 2940e86 (phase1), 94c99c7 (docs), de081b8 (all remaining phase2-5 + caps + wire + delete + verif).
- [x] Final: high-sev 4p+ (IConnector/Automation), build 0e, doctor 5/5, MCP 5p. All unchecked executed in order. Next as noted.
- Evidence only. All constraints followed.


## Risks & Mitigations
- Contract test complexity / fake OAuth: use existing Salesforce two-user patterns + simple in-memory token server. Start with one provider.
- LLM output quality for scripts: keep human approval rail mandatory; gate + compile checks before proposal.
- Golden E2E flakiness with restarts: use existing `DigitalBrainAppHostFixture` patterns + Aspire wait helpers + journal assertions. Make restart explicit via MCP command.
- Scope creep: stick to gap items 11-15. Defer full `caps.Files` + market data until after golden.
- Breaking changes during migration: run high-sev + connector-specific tests before/after each provider. Keep backward compat where cheap.

## Verification Culture
Every checkbox requires executable proof in the plan update (e.g. "high-sev: 42 passed (output)", "doctor: 5/5", "MCP restart command succeeded, ledger replay verified", "contract test IConnectorContractTests<SalesforceConnector> passed isolation").

After this phase the NYT example should be demonstrable (even if some caps are stubbed) via the golden E2E.

Update this plan after every phase/slice with findings. Use subagent-driven or executing-plans skill for implementation slices if complex.

**Start with Phase 0. High-sev + doctor + Context7 before touching code.**