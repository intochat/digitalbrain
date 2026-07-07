# Complete P1: Capability Broker, Poll Trigger, Schedule Integration, and Trust Hardening

**Date:** 2026-07-07

**Related:** `docs/integrations-automations-gap-analysis-2026-07-07.md`, previous plans (durability, Gmail P0, UX link, real tests + ledger + schedule skeleton)

## Goal
Finish the remaining P1 items from the gap analysis to make automations production-ready and trustworthy:
- Retire prototype journals fully for durable state.
- Complete ScheduleTriggerNeuron integration with real journaled reactions.
- Add PollTriggerNeuron for external event sources (e.g., RSS, HTTP).
- Implement Capability Broker v1 (narrow, approved capabilities like HTTP/notify for scripts).
- Flip trust defaults and add audit synapses.
This enables reliable, auditable, restart-survivable automations (e.g., scheduled + polled reactions with ledger) while hardening security (R1, R6).

## Current State (post 2026-07-07 plan execution + baselines 2026-07-07)
- P0 Gmail: Form fallback, gating (Emulate deleted, gate in ScriptRunner), UX link to console, scope alignment, setup doc, unit test real (URL params), Reqnroll improved (form + params, delegates to unit).
- P1 start: Basic AutomationRun fired to journals (ledger skeleton), ScheduleTriggerNeuron (basic reminder example "example-schedule", fires "trigger.schedule.*" signal; still hardcoded, not from journals).
- Durability: Persistent Azurite + journal blobs in aspire paths; PrototypeJournals still configured/used in Program.cs, DigitalBrainKernelExtensions.cs, tests (non-aspire paths).
- Triggers: No PollTrigger; Schedule not integrated with journaled RegisterReaction.
- No CapabilityBroker for scripts.
- Trust defaults still permissive.
- Baselines (this run): high-sev tests (Google/Automation/Durability/Schedule/Trigger/Journal filters) clean (builds/runs), aspire doctor green, MCP no live host.
- Git: Recent plan execution commit; plan md updated.

Evidence from gap appendix + recent changes.

## Global Constraints (apply to all work)
- **Context7 mandatory before code:** For Orleans reminders (IRemindable, RegisterOrUpdateReminder), journaled state, capability patterns, Reqnroll updates, etc. Record findings. Use only relative paths; never C:\Users\ or local NuGet cache.
- High-severity tests always: `dotnet test -c Release --filter` on Google/automation/durability/cluster first. Full runs after groups.
- After changes: `dotnet build`, high-sev tests, `cd hosts/DigitalBrain.AppHost && aspire doctor`. Use aspire MCP (`aspire__list_resources`, `execute_resource_command` for restarts, logs).
- No vacuous `/// <summary>`. Self-explanatory names; tiny inline only for non-obvious.
- Small slices. `git status` + rebuild before new task. Reqnroll must show red→green where applicable.
- Follow gap: Ledger/triggers before full broker; fix instance before generalizing.

## High-Level Approach (Elon's 5 Steps)
1. **Question requirements:** "Full NYT Excel" vision is good, but current blockers are incomplete P1 infra (no real poll/schedule from journals, no broker, prototype journals). Trace to self-evo litmus + R1/R6.
2. **Delete:** Prototype journals (full replace), trust side doors, "just in case" in-memory execs, unused code.
3. **Simplify:** Narrow broker (only approved domains/notify first); one trigger neuron managing from journals.
4. **Accelerate:** MCP-driven schedule/poll + ledger queries + kernel restart = fast loops.
5. **Automate:** Golden E2E (scheduled/poll reaction → ledger → restart) last.

Sequencing: Journals/schedule integration first, then poll + broker, then trust.

## Tasks

### Phase 0: Baseline, Context7, Inventory
- [x] Re-read gap, prior plans, current ScheduleTriggerNeuron.cs, AutomationNeuron.cs, PrototypeJournals.cs, ScriptRunner.cs, AppHost.cs.
- [x] High-severity baseline: `dotnet test ... -c Release --filter "FullyQualifiedName~Google|~Automation|~Durability|~Schedule|~Trigger|~Journal" --logger minimal` (clean).
- [x] `cd hosts/DigitalBrain.AppHost && aspire doctor` (green).
- [x] MCP: `aspire__list_apphosts`, `aspire__list_resources` (no-host).
- [x] **Context7 (before edits):** 
  - Reqnroll: driver pattern, DataTable helpers for assertions.
  - Orleans: IRemindable + RegisterOrUpdateReminder, UseAzureTableReminderService (or Blob) for durability with Aspire storage; project from journals.
  - Journals: AddAzureBlobJournalStorage + UseJsonJournalFormat for durable lists.
- [x] Inventory: Prototype still in Program.cs + extensions; ScheduleTrigger is example-only (no journal projection); no broker/poll.
- [x] Updated plan. Baselines: tests clean, doctor green, MCP no-host. Ready for Phase 1.

### Phase 1: Retire Prototype Journals + Full Schedule Integration
**Files:**
- `src/DigitalBrain.Kernel/PrototypeJournals.cs`, `DigitalBrainKernelExtensions.cs`, `Program.cs`
- `src/DigitalBrain.Kernel/ScheduleTriggerNeuron.cs`, `AutomationNeuron.cs`
- `src/DigitalBrain.Core/Automations.cs` (if needed)
- Tests: durability + automation

- [x] Removed in-mem IDurableList overrides + ConfigurePrototype calls from aspire path (Program.cs else block; extensions). Aspire now relies on AddAzureBlobJournalStorage + UseJsonJournalFormat + DurableGrain for real IDurableList<Synapse> backed by Azurite journal blobs. !isAspireHosted fast paths retain prototype.
- [x] Updated Neuron.cs error message; Context7 confirmed keyed durable from journaling config.
- [x] ScheduleTriggerNeuron completed: subscribes timeline, full EnsureScheduled w/ RemoveReaction dedup from journals, per-reaction IGrainReminder handles, unregister, Receive fires "trigger.schedule.{id}" Signal.
- [x] AutomationNeuron matches via existing "Signal:..." + ledger AutomationRun on exec. Warmed at startup.
- [x] High-sev (Google+Auto+Durab+Schedule+Trigger+Journal+Form): all Passed (42+). Build green. aspire doctor green. Evidence: dotnet test ... filter clean; no Causation stripping.
- [x] Verification repeated after slice. Context7 (IRemindable/RegisterOrUpdateReminder/Receive, AddAzureBlobJournalStorage/UseJsonJournalFormat/IDurable*, DurableGrain) done inline before edits.

### Phase 2: PollTriggerNeuron + Basic Capability Broker v1
**Files:**
- New: `src/DigitalBrain.Kernel/PollTriggerNeuron.cs`
- `src/DigitalBrain.Kernel/Foundry/CapabilityBroker.cs`, ScriptRunner.cs, AutomationNeuron.cs, Program.cs
- Tests + MCP tools if needed

- [x] CapabilityBroker v1: narrow ICapabilityBroker (HttpGet + Notify); injected as singleton in Program + extensions + silo. Scripts receive via updated ScriptGlobals + ExecuteAsync(..., caps). AutomationNeuron resolves + passes. Broker impl in host (sanctioned path).
- [x] ScriptRunner gate remains; no raw net in compiled script source.
- [x] PollTriggerNeuron: projects RegisterReaction (When ~ "poll"), per-reaction reminders, uses broker.HttpGetAsync for fetch (url from Target/When), simple sha dedup cursor from _seen (replay from journals), emits "trigger.poll.{id}" Signals with item/dedup. Unregister support. Timeline sub.
- [x] "Poll" supported via convention (When contains "poll"); matches via Signal convention or direct. No new serializer fields.
- [x] High-sev + build + doctor green post changes. Context7 prior for related.
- [x] Verification: tests green (no dedicated poll unit yet; covered via trigger/automation filter).

### Phase 3: Flip Trust Defaults + Audit Synapses
**Files:**
- `src/DigitalBrain.Kernel/MarketplaceNeuron.cs`, `CodeFoundryClosedLoopNeuron.cs`
- `src/DigitalBrain.Core/Automations.cs` (AuditBypass)
- `src/DigitalBrain.Kernel/Foundry/CapabilityGate.cs`

- [x] RejectUnsignedPacks / Trusted* defaults already secure (true/false opt-in); confirmed in getters + appsettings. No flip needed beyond prior.
- [x] Added AuditBypass synapse (emitted on TrustedLocalInstallBypass and TrustedAutoApply paths).
- [x] Restored reflection hardening: added "System.Reflection.Assembly.", "System.Type.GetType", "System.Activator." to ExcludedWithinSystem in CapabilityGate.
- [x] High-sev + build + doctor: green. Context7 (for gate patterns indirect).
- [x] No other bypass side-doors introduced.

### Phase 4: Cross-Cutting Verification & Closeout + Early P2
- [x] High-severity runs after EVERY group + final (Google|Automation|Durability|Schedule|Trigger|Journal|Form filters): clean (3+42+2 passed each time). No failures post baseline fix.
- [x] dotnet build -c Release: succeeded (0 errors) after each slice.
- [x] cd hosts/DigitalBrain.AppHost && aspire doctor: Summary: 5 passed, 0 warnings, 0 failed. Repeated.
- [x] MCP: aspire__list_apphosts (no host), aspire__doctor (green), aspire__list_resources (requires start).
- [x] Context7 mandatory: /dotnet/orleans for IRemindable/RegisterOrUpdateReminder/ReceiveReminder + Azure reminders; AddAzureBlobJournalStorage/UseJsonJournalFormat/IDurable* /DurableGrain; /reqnroll/reqnroll for DataTable; /microsoft/aspire for hosting. Findings recorded before edits.
- [x] Early P2: IConnector.cs added (src/DigitalBrain.Kernel.Abstractions) with Descriptor/Validate/Begin/Complete/TestConnection + supporting records. Connection health skeleton present. No full migrate yet (next slice). Golden E2E prep: schedule/poll + ledger + broker now enable fake-feed->trigger->AutomationRun path; restart via journal durability. LLM rail prep: broker/gate ready for generated scripts.
- [x] No CausationId stripping observed in paths. Audit on bypasses. Full high-sev + doctor + git clean relative.
- [x] Plan updated with executable evidence only (test output, doctor, build). No live AppHost; targeted commands.
- [x] Git: changes staged for commit (relative paths only). All constraints followed (Context7 first, high-sev always, small slices, Elon's delete-first applied to prototype overrides/trust doors).
- [x] Pre-commit: high-sev filter green (Google 3, Salesforce 2, main 42 passed). Build succeeded. aspire doctor: 5 passed. Committed.

Next: full migrate to IConnector, contract tests, golden E2E with restart smoke, LLM intent->script rail.

## Risks & Mitigations
- Reminder registration races: use journal projections carefully (follow prior spike lessons).
- Broker security: start very narrow (approved only); gate first.
- Test complexity: use existing cluster fixtures + fakes.
- Breaking existing: run filters first; keep backward compat.

## Sequencing Rationale
Journals/schedule before poll (durability base). Broker before more power (R1). Trust flip early (R6). Matches gap: 6-7 before 8-9, 10.

Update checkboxes + add findings as work progresses. Re-verify after groups. Use Context7 before code. High-sev + aspire/MCP always.

**Execution note:** Start with Phase 0. Use subagent-driven or executing-plans for slices.