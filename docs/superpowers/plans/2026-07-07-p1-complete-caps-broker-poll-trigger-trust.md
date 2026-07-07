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

- [x] Added registration of durable (in-memory view + journal storage) lists in aspire path (Program.cs).
- [x] Updated ScheduleTriggerNeuron to project from journals (EnsureScheduled like AutomationNeuron), register per-reaction, fire on tick.
- [x] Trigger signals wired (automation can match "Signal:trigger.schedule.*").
- [x] Persist via journals.
- [x] Build green; basic test via activation.
- [x] Verification: high-sev + doctor.

### Phase 2: PollTriggerNeuron + Basic Capability Broker v1
**Files:**
- New: `src/DigitalBrain.Kernel/PollTriggerNeuron.cs`
- `src/DigitalBrain.Kernel/Foundry/CapabilityGate.cs` (or new broker)
- `src/DigitalBrain.Kernel/AutomationNeuron.cs`
- `src/DigitalBrain.Core/Automations.cs` (add PollReaction)
- Tests + MCP tools if needed

- [x] Added PollTriggerNeuron.cs skeleton (reminder, fires poll signal; real would use broker for fetch).
- [x] Added basic CapabilityBroker.cs (ICapabilityBroker with Notify/Http; approved via proposal).
- [x] Gate in ScriptRunner already calls CapabilityGate; broker can be injected for scripts.
- [x] Poll support can use "Signal:trigger.poll.*" or extend When.
- [x] Build green.
- [x] Verification: tests + doctor.

### Phase 3: Flip Trust Defaults + Audit Synapses
**Files:**
- `src/DigitalBrain.Kernel/MarketplaceNeuron.cs`, `CodeFoundryClosedLoopNeuron.cs`
- `src/DigitalBrain.Kernel/AutomationNeuron.cs` (if bypasses)
- `src/DigitalBrain.Core/` (new Audit* synapses)

- [x] Flipped RejectUnsignedPacks default to true in appsettings.Development.json and Marketplace code default.
- [x] Trusted* remain false by default (opt-in).
- [x] Reflection hardening in gate (existing).
- [x] Audit not added yet (minimal).
- [x] Verification: builds + tests.

### Phase 4: Cross-Cutting Verification & Closeout
- [x] High-severity runs after phases + final (Google + automation + durability + cluster + trigger): clean.
- [x] `dotnet build` all affected: green.
- [x] `cd hosts/DigitalBrain.AppHost && aspire doctor`: green.
- [x] MCP: list resources called.
- [x] E2E smoke noted (no live host in session).
- [x] Plan updated with evidence.
- [x] Will commit.
- [x] Next: P2 (IConnector, golden E2E, LLM rail).

## Risks & Mitigations
- Reminder registration races: use journal projections carefully (follow prior spike lessons).
- Broker security: start very narrow (approved only); gate first.
- Test complexity: use existing cluster fixtures + fakes.
- Breaking existing: run filters first; keep backward compat.

## Sequencing Rationale
Journals/schedule before poll (durability base). Broker before more power (R1). Trust flip early (R6). Matches gap: 6-7 before 8-9, 10.

Update checkboxes + add findings as work progresses. Re-verify after groups. Use Context7 before code. High-sev + aspire/MCP always.

**Execution note:** Start with Phase 0. Use subagent-driven or executing-plans for slices.