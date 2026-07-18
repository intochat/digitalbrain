# DigitalBrain Grok-Orchestrated Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement the approved reactive Orleans neuron architecture by using one Codex session as the integration authority and multiple isolated Grok CLI sessions as bounded implementation and review workers.

**Architecture:** Codex owns requirements, dependency direction, central files, integration order, and final verification. Grok workers operate in native Grok worktrees with exclusive file ownership, test-first instructions, explicit stop conditions, and mandatory commits. Foundation and kill gates are serial integration barriers; only independent vertical packages run in parallel.

**Tech Stack:** .NET 11 preview, Microsoft Orleans 10.2.1, Microsoft.Orleans.Journaling 10.2.2-rc.2.alpha.1, Microsoft Agent Framework 1.13.0, Aspire 13.4.6, Azure Blob Storage, Azure Queue/Event Hubs Orleans streams, MCP C# SDK 1.4.0, Flutter, xUnit, Grok CLI 0.2.101 with grok-4.5.

## Global Constraints

- Reactivity is intrinsic to `Brain.Kernel`.
- A synapse is typed data only.
- `CommandSynapse<T>` uses direct Orleans grain calls.
- `EventSynapse<T>` uses persistent Orleans streams.
- `Gpt56Neuron` and `Grok45Neuron` are separate durable grains.
- `GroupChatNeuron` uses Microsoft Agent Framework Group Chat.
- `GroupChatNeuron` owns its durable renderer-neutral `UiSurface`.
- One Group Chat participant step and one checkpoint are committed per Orleans turn.
- MCP calls, neuron reactions, and Flutter actions use command, journal, outbox, feed.
- `DigitalBrain.Google.IGmail` and `DigitalBrain.Salesforce.ISalesforce` are typed contracts backed by typed MCP tools.
- AgentGateway and DevUI are development-only and connect through Aspire Orleans `.AsClient()`.
- No generic workflow engine, in-memory event bus, God object, AppDomain scanning, duplicate UI rail, swallowed failure, or sensitive telemetry.
- Identity implementation is deferred, but every command carries `OrganizationId`, `PrincipalId`, and `SpaceId`.
- No empty Identity or Stripe projects. Future names are `DigitalBrain.Identity.IIdentity` and `DigitalBrain.Stripe.IStripe`.
- `Brain.Contracts` may reference Orleans contract and serialization abstractions only.
- Comments are forbidden in tracked source and configuration files.
- Context7 and dotnet-inspect precede package API code.
- CodeGraph precedes architecture exploration or source edits.
- Aspire doctor and live resource inspection precede and follow integrated runtime changes.
- Every behavior change follows red, observed failure, minimal green, refactor.
- Focused tests use the owning test project. `--filter` is forbidden.
- Every integration checkpoint runs `dotnet test --logger "console;verbosity=minimal"`.
- Grok workers may not push, merge, rebase, edit `CLAUDE.md`, edit this plan, or touch unassigned files.
- Codex must inspect every worker diff and rerun verification. A Grok success message is not evidence.

---

## Final Project Tree

```text
src/
  Brain.Contracts/
  Brain.Client/
  Brain.Kernel/
  Brain.AI/
  Brain.Google/
  Brain.Salesforce/
  Brain.Kernel.Host/
  Brain.Gateway/
  Brain.Mcp/
  Brain.AgentGateway/
  Brain.AppHost/
tests/
  Brain.Tests/
workspace/
```

Dependency direction:

```text
Brain.Contracts -> Orleans abstractions
Brain.Client -> Brain.Contracts + Orleans.Client
Brain.Kernel -> Brain.Contracts + Orleans runtime/journaling/streams/reminders
Brain.AI -> Brain.Kernel + Microsoft Agent Framework + model clients
Brain.Google -> Brain.Kernel + Agent Framework + Gmail MCP client
Brain.Salesforce -> Brain.Kernel + Salesforce MCP client
Brain.Kernel.Host -> Brain.Kernel + Brain.AI + Brain.Google + Brain.Salesforce + Azure providers
Brain.Gateway -> Brain.Contracts + Brain.Client + ASP.NET
Brain.Mcp -> Brain.Contracts + Brain.Client + MCP server SDK
Brain.AgentGateway -> Brain.Contracts + Brain.Client + Agent Framework DevUI
Brain.AppHost -> executable projects and Aspire hosting integrations
workspace -> Brain.Gateway wire protocol only
```

## Codex Integration Authority

Codex owns these files for the entire execution:

```text
Brain.slnx
Directory.Build.props
Directory.Build.targets
Directory.Packages.props
CLAUDE.md
README.md
docs/superpowers/plans/2026-07-18-digitalbrain-grok-orchestration.md
src/Brain.AppHost/**
```

Codex responsibilities:

1. Run pre-change ritual and baseline.
2. Prepare package versions, project skeletons, and test references before parallel dispatch.
3. Launch Grok workers from an integrated commit.
4. Capture session IDs, worktree names, starting commit, and assigned paths.
5. Inspect `git diff --stat`, `git diff --check`, full diffs, and worker test output.
6. Reject out-of-scope edits before considering behavior.
7. Cherry-pick one worker commit at a time.
8. Run owning tests after each cherry-pick.
9. Run root tests after each integration batch.
10. Run separate adversarial review sessions before completion.
11. Delete rejected architecture and historical plans only after replacement behavior is green.

Codex must never ask multiple editing workers to share a project file, test file, package file, AppHost file, or solution file.

## Grok Session Contract

Every editing prompt must contain this contract verbatim:

```text
You are a bounded implementation worker. Work only in the native Grok worktree created for this session.

Before editing:
1. Read AGENTS.md and CLAUDE.md completely.
2. Use Context7 for every package API involved. If Context7 is unavailable, use dotnet-inspect and official Microsoft documentation and record that fallback.
3. Use CodeGraph before reading or editing indexed source.
4. Run Aspire doctor and inspect available AppHosts/resources.
5. Run the owning baseline test project.
6. Apply the five steps in CLAUDE.md in order.

Execution:
1. Touch only the Assigned paths.
2. Follow test-driven development. Add one failing test, run it and observe the expected failure, implement the minimum, rerun it, then refactor.
3. Never use dotnet test --filter.
4. Do not add comments to tracked source or configuration.
5. Do not add generic JSON invocation, reflection proxies, assembly scanning, in-memory buses, duplicate UI state, or catch-and-ignore handling.
6. Do not edit central files, the orchestration plan, README, CLAUDE.md, AppHost, solution, or package props unless they are explicitly assigned.
7. Do not push, merge, rebase, or rewrite history.
8. Preserve unrelated changes and do not touch sources/.

Before finishing:
1. Run the owning test project.
2. Run git diff --check.
3. Search changed source for comments and prohibited generic invocation.
4. Commit only assigned files with the requested commit message.
5. Return the commit SHA, exact tests run, red failure observed, green result, changed paths, unresolved risks, and any requirement you could not prove.

Stop without committing if a kill condition occurs or an assigned requirement cannot be met through public supported APIs.
```

## Grok Launch Template

The Codex orchestrator launches workers from the repository worktree containing this plan:

```powershell
$prompt = @'
<worker-specific prompt followed by the Grok Session Contract>
'@

grok `
  --cwd . `
  --worktree=<unique-worker-name> `
  --worktree-ref=codex/reactive-neurons `
  --model=grok-4.5 `
  --reasoning-effort=high `
  --permission-mode=acceptEdits `
  --check `
  --max-turns=80 `
  -p $prompt
```

Use a fresh worker name for every attempt. Do not resume a worker after its base branch has changed. A follow-up review of the same unchanged worktree may use `grok --continue --cwd <worker-worktree>`.

After launch:

```powershell
grok sessions list
grok worktree list
```

Codex records the session and worktree in its task plan. Parallel processes may be started only when the ownership table says `Parallel`.

---

## Phase 0: Codex Bootstrap

**Mode:** Serial, Codex only.

**Files:**

- Modify: `Directory.Packages.props`
- Create: `tests/Brain.FeasibilityTests/Brain.FeasibilityTests.csproj`
- Modify: `Brain.slnx`

**Produces:**

- One test project containing no product behavior.
- Central versions for Agent Framework Workflows, Azure Blob Storage, Orleans Azure Queue streams, and test dependencies.
- Package references required by all three kill-gate workers.

- [ ] Verify current branch is `codex/reactive-neurons` and the current worktree is clean except this plan.
- [ ] Run `dotnet test --logger "console;verbosity=minimal"` and require 157 passing tests.
- [ ] Run `flutter analyze` and `flutter test` in `workspace/`; record the baseline without changing Flutter.
- [ ] Query Context7 and dotnet-inspect for the exact pinned APIs.
- [ ] Run `aspire doctor` and inspect running AppHosts before modifying hosting inputs.
- [ ] Create `Brain.FeasibilityTests.csproj` with xUnit, Orleans testing/client packages, `Microsoft.Agents.AI.Workflows`, `Microsoft.Orleans.Journaling`, Azure Blob SDK, and Testcontainers/Azurite support selected by current official documentation.
- [ ] Add the project to `Brain.slnx`.
- [ ] Run `dotnet test tests/Brain.FeasibilityTests/Brain.FeasibilityTests.csproj --logger "console;verbosity=minimal"` and require a zero-test success.
- [ ] Commit as `test: prepare distributed feasibility gates`.

No worker launches from an uncommitted bootstrap.

---

## Phase 1: Parallel Feasibility Kill Gates

All three workers start from the Phase 0 commit.

### Worker 1A: Typed Orleans References

**Mode:** Parallel.

**Assigned paths:**

```text
tests/Brain.FeasibilityTests/TypedReferences/**
```

**Forbidden paths:** Every other path.

**Worker prompt:**

```text
Prove the typed-client kill gate for DigitalBrain.

Assigned paths:
- tests/Brain.FeasibilityTests/TypedReferences/**

Create test-only contracts and a minimal Orleans test-cluster fixture proving all of the following:
1. A generic Brain.Get<T>()-shaped helper can return a real Orleans grain reference where T is a typed grain interface.
2. IGpt56 and IGrok45 references can be supplied as typed IAgent participant references to an IGroupChat grain call.
3. The participant references survive Orleans serialization and preserve grain identity.
4. No DispatchProxy, dynamic invocation, JSON method envelope, AppDomain scan, or assembly scan is required.
5. Stable neuron identity is derived from explicit contract metadata plus OrganizationId, SpaceId, and instance ID.

Use only test-local types. Do not alter current production code. The first test must fail because the test-local typed resolver is absent, then add the minimum test-local resolver.

Required tests:
- Brain_get_returns_real_typed_grain_reference
- Group_chat_receives_typed_agent_grain_references
- Typed_grain_reference_round_trip_preserves_identity
- Resolver_contains_no_dispatch_proxy_or_dynamic_invocation

Run:
dotnet test tests/Brain.FeasibilityTests/Brain.FeasibilityTests.csproj --logger "console;verbosity=minimal"

Kill condition:
Stop if Orleans source generation or serialization cannot support typed agent grain references without a runtime proxy or generic JSON invocation.

Commit message:
test: prove typed Orleans neuron references
```

Append the Grok Session Contract.

### Worker 1B: Agent Framework One-Step Checkpoint

**Mode:** Parallel.

**Assigned paths:**

```text
tests/Brain.FeasibilityTests/AgentFramework/**
```

**Forbidden paths:** Every other path.

**Worker prompt:**

```text
Prove the Microsoft Agent Framework Group Chat kill gate for DigitalBrain using Microsoft.Agents.AI.Workflows 1.13.0.

Assigned paths:
- tests/Brain.FeasibilityTests/AgentFramework/**

Use deterministic fake AIAgent participants. Exercise the public Group Chat builder, RoundRobinGroupChatManager, TurnToken, StreamingRun, CheckpointManager.CreateJson, and SuperStepCompletedEvent APIs.

Required tests:
- One_advance_produces_one_participant_response
- One_advance_produces_one_checkpoint
- Second_advance_selects_the_next_participant_once
- Stopping_after_the_checkpoint_leaves_no_background_conversation
- Restored_or_rebuilt_state_preserves_transcript_and_participant_cursor

The preferred result resumes a captured checkpoint and advances once. The permitted fallback rebuilds a Group Chat from the durable transcript and participant cursor with MaximumIterationCount equal to one. Both paths must use Microsoft Agent Framework Group Chat and must prove that no workflow continues after the method returns.

Store checkpoint JSON only in a test-local in-memory ICheckpointStore<JsonElement>. Do not introduce a second production persistence rail.

Run:
dotnet test tests/Brain.FeasibilityTests/Brain.FeasibilityTests.csproj --logger "console;verbosity=minimal"

Kill conditions:
- One invocation can generate more than one participant response.
- The workflow continues in the background after the method returns.
- A checkpoint cannot be extracted through public APIs.
- Advancing the next participant requires a generic workflow engine outside Agent Framework.

Commit message:
test: prove one-step Agent Framework group chat
```

Append the Grok Session Contract.

### Worker 1C: Azure Blob Journal Storage

**Mode:** Parallel.

**Assigned paths:**

```text
src/Brain.Kernel.Host/JournalStorage/**
tests/Brain.FeasibilityTests/AzureJournal/**
```

**Forbidden paths:** Every other path.

**Worker prompt:**

```text
Implement and prove the Azure Blob IJournalStorageProvider kill gate for the pinned Microsoft.Orleans.Journaling package.

Assigned paths:
- src/Brain.Kernel.Host/JournalStorage/**
- tests/Brain.FeasibilityTests/AzureJournal/**

Implement these provider-host types:
- AzureBlobJournalStorageOptions
- AzureBlobJournalStorageProvider
- AzureBlobJournalStorage

Brain.Kernel must not reference Azure SDKs. Use one blob per JournalId. Preserve journal bytes exactly. Use Azure conditional requests and ETags to fence stale writers. Implement CreateIfNotExistsAsync, AppendAsync, ReadAsync, ReplaceAsync, DeleteAsync, GetMetadataAsync, UpdateMetadataAsync, and IsCompactionRequested. Normalize JournalId into a deterministic collision-resistant blob name.

Required integration tests against an isolated Azurite resource:
- Create_append_read_round_trip
- Restart_replays_acknowledged_bytes
- Replace_compacts_without_changing_logical_content
- Delete_removes_journal_and_metadata
- Stale_writer_is_rejected
- Metadata_update_honors_etag
- Cancellation_does_not_report_success

Write each failing test before its implementation and record the observed red result.

Run:
dotnet test tests/Brain.FeasibilityTests/Brain.FeasibilityTests.csproj --logger "console;verbosity=minimal"

Kill conditions:
- A stale storage handle can overwrite or append after another writer takes ownership.
- An acknowledged append is absent after provider recreation.
- Replace cannot preserve metadata and content atomically enough for Orleans journal compaction.
- Azurite and Azure Blob behavior required by the adapter are materially different.

Commit message:
feat: prove Azure Blob Orleans journal storage
```

Append the Grok Session Contract.

### Phase 1 Integration Gate

Codex processes workers one at a time:

- [ ] Verify every worker changed only assigned paths.
- [ ] Inspect every test and confirm it exercises public APIs rather than mocks of the behavior under proof.
- [ ] Cherry-pick Worker 1A.
- [ ] Run `dotnet test tests/Brain.FeasibilityTests/Brain.FeasibilityTests.csproj --logger "console;verbosity=minimal"`.
- [ ] Cherry-pick Worker 1B.
- [ ] Run the same feasibility test command.
- [ ] Cherry-pick Worker 1C.
- [ ] Run the same feasibility test command.
- [ ] Run `dotnet test --logger "console;verbosity=minimal"`.
- [ ] Stop the entire implementation if any kill condition remains unresolved.
- [ ] Commit integration corrections separately; never silently modify a worker commit during cherry-pick.

---

## Phase 2: Serial Foundation Worker

**Mode:** Serial. No other editing worker runs.

**Assigned paths:**

```text
src/Brain.Contracts/**
src/Brain.Client/**
src/Brain.Kernel/**
tests/Brain.Tests/Contracts/**
tests/Brain.Tests/Client/**
tests/Brain.Tests/Kernel/**
```

Codex pre-creates the assigned project skeletons, test project, package versions, project references, and solution entries before launch.

**Worker prompt:**

```text
Build the typed DigitalBrain contracts, proxy-free client, and reactive Kernel foundation from the accepted feasibility tests.

Assigned paths:
- src/Brain.Contracts/**
- src/Brain.Client/**
- src/Brain.Kernel/**
- tests/Brain.Tests/Contracts/**
- tests/Brain.Tests/Client/**
- tests/Brain.Tests/Kernel/**

Required public contracts:
- OrganizationId, PrincipalId, SpaceId
- NeuronAddress and explicit NeuronContractAttribute
- SynapseMetadata
- CommandSynapse<T>
- EventSynapse<T>
- CommandReceipt
- UiSurface, UiBlock, UiAction, UiSurfaceSnapshot, UiSurfacePatch, UiPatchOperation
- DigitalBrain.AI.IAgent, IGpt56, IGrok45, IGroupChat
- DigitalBrain.Google.IGmail
- DigitalBrain.Salesforce.ISalesforce

Required client behavior:
- brain.Get<T>() returns IClusterClient.GetGrain<T>() directly
- grain keys include organization, space, contract identity, and instance identity
- ergonomic IGroupChat.StartDiscussion(topic, gpt, grok) creates CommandSynapse<StartDiscussion>
- no proxy, generic invoke, JSON envelope, or scanning

Required Kernel behavior:
- narrow ReactiveNeuron base or composition helper
- journal initialization and replay
- durable command receipt deduplication
- journaled domain state, UI revision, and typed outbox intent committed before acknowledgement
- generic publishing helper for EventSynapse<T> to persistent Orleans streams
- explicit typed subscription registration by each neuron
- event deduplication by EventId
- source sequence tracking without a fake global order
- durable outbox retry state
- activation drain plus reminder retry
- durable sanitized failures
- causal depth limit and duplicate causation rejection
- no domain routing catalog and no workflow engine

Required tests:
- contracts serialize with Orleans
- Contracts references no forbidden SDK
- duplicate command returns the durable original receipt
- failed journal write does not acknowledge
- committed pending event survives reactivation
- duplicate event is not reacted to twice
- out-of-order source event is buffered or rejected explicitly
- causal loop is durably rejected
- UI action expected revision conflict is explicit
- no failure path catches and ignores an exception

Run:
dotnet test tests/Brain.Tests/Brain.Tests.csproj --logger "console;verbosity=minimal"

Commit message:
feat: add typed reactive neuron foundation
```

Append the Grok Session Contract.

### Phase 2 Integration Gate

- [ ] Reject any public `string contract`, `string method`, `PayloadJson`, `dynamic`, `DispatchProxy`, or `AppDomain` dispatch.
- [ ] Reject any Kernel reference to Agent Framework, MCP, Azure SDK, ASP.NET, Flutter, Gmail, or Salesforce SDK.
- [ ] Run `dotnet test tests/Brain.Tests/Brain.Tests.csproj --logger "console;verbosity=minimal"`.
- [ ] Run `dotnet test --logger "console;verbosity=minimal"`.
- [ ] Commit the frozen foundation.

No parallel vertical worker starts before this commit.

---

## Phase 3: Parallel Vertical Workers

Codex pre-creates all project skeletons, central package versions, references, solution entries, and empty test directories. Workers start from the same Phase 2 commit.

### Worker 3A: AI and Group Chat

**Assigned paths:**

```text
src/Brain.AI/**
tests/Brain.Tests/AI/**
```

**Worker prompt:**

```text
Implement the independent durable Gpt56Neuron, Grok45Neuron, and checkpointed GroupChatNeuron.

Assigned paths:
- src/Brain.AI/**
- tests/Brain.Tests/AI/**

Microsoft Agent Framework belongs only in Brain.AI. Gpt56Neuron and Grok45Neuron own independent provider sessions and durable request state. GroupChatNeuron owns discussion lifecycle, transcript, participant cursor, buffered Agent Framework checkpoint JSON, and one UiSurface. Agent Framework AIAgent adapters call the participant grains through typed Orleans references.

One GroupChat reaction must restore or build the workflow, execute one participant, capture one checkpoint, create one transcript entry, create one UI revision, commit the typed outbox event for the next step, and return. Cancellation is durable and takes effect at the next checkpoint boundary. The current provider call has a timeout.

Required tests:
- Gpt56_and_Grok45_use_distinct_grain_identities_and_state
- Start_discussion_commits_before_first_participant_step
- One_reaction_commits_one_participant_response
- One_reaction_commits_one_checkpoint
- One_reaction_commits_one_UiSurface_revision
- Next_step_occurs_in_a_later_Orleans_turn
- Duplicate_step_event_does_not_call_participant_twice
- Reactivation_restores_transcript_checkpoint_and_next_participant
- Cancel_prevents_later_steps
- Provider_failure_is_durable_sanitized_and_visible_in_UiSurface
- No_prompt_token_or_provider_payload_appears_in_telemetry

Use deterministic fake IChatClient implementations in tests. Do not require real model credentials.

Commit message:
feat: add durable one-step AI group chat neurons
```

Append the Grok Session Contract.

### Worker 3B: Google and Salesforce

**Assigned paths:**

```text
src/Brain.Google/**
src/Brain.Salesforce/**
tests/Brain.Tests/Google/**
tests/Brain.Tests/Salesforce/**
```

**Worker prompt:**

```text
Implement typed GmailNeuron and SalesforceNeuron vertical packages.

Assigned paths:
- src/Brain.Google/**
- src/Brain.Salesforce/**
- tests/Brain.Tests/Google/**
- tests/Brain.Tests/Salesforce/**

Google.IGmail combines a configured IChatClient, an Agent Framework agent, typed Gmail MCP client tools, durable neuron state, and the common UI path. Salesforce uses typed contracts and typed Salesforce MCP client tools. Provider adapters live inside their vertical projects. Do not introduce modules, integrations, connectors, a generic external invoke API, or a second connection abstraction.

Read operations may call providers during the command turn when they are repeat-safe. Mutations must first journal a typed effect intent with EffectId and provider idempotency key, execute through the domain outbox path, journal the result, and emit the UI event. Durable failures must be sanitized and actionable.

Required tests:
- Gmail_contract_exposes_only_typed_operations
- Salesforce_contract_exposes_only_typed_operations
- Gmail_agent_uses_typed_MCP_tools
- Read_result_updates_UiSurface_through_outbox_and_feed_event
- Mutation_intent_is_durable_before_provider_call
- Duplicate_effect_does_not_repeat_provider_mutation
- Provider_failure_is_not_swallowed
- Provider_credentials_and_message_bodies_are_absent_from_telemetry

Commit message:
feat: add typed Gmail and Salesforce neurons
```

Append the Grok Session Contract.

### Worker 3C: Gateway, MCP, and AgentGateway

**Assigned paths:**

```text
src/Brain.Gateway/**
src/Brain.Mcp/**
src/Brain.AgentGateway/**
tests/Brain.Tests/Gateway/**
tests/Brain.Tests/Mcp/**
tests/Brain.Tests/AgentGateway/**
```

**Worker prompt:**

```text
Implement the production Gateway, typed MCP server, and development-only AgentGateway.

Assigned paths:
- src/Brain.Gateway/**
- src/Brain.Mcp/**
- src/Brain.AgentGateway/**
- tests/Brain.Tests/Gateway/**
- tests/Brain.Tests/Mcp/**
- tests/Brain.Tests/AgentGateway/**

Gateway responsibilities are identity metadata creation, typed UI action commands, snapshots, durable feed paging, live stream subscription, subscribe-before-read reconnect, merge/deduplication, and snapshot fallback on a revision gap. Use a deterministic development principal now. Do not implement identity providers, roles, claims, billing, or Stripe.

Brain.Mcp exposes named typed tools for IGroupChat, Google.IGmail, and Salesforce.ISalesforce. It contains no business logic and no generic neuron_describe, neuron_read, or neuron_invoke tool.

AgentGateway is development-only. It connects to the Aspire Orleans resource through .AsClient(), adapts typed neurons for Agent Framework DevUI, owns no durable state, and is excluded from production composition.

Required tests:
- Ui_action_calls_surface_owner_with_expected_revision
- Reconnect_subscribes_before_reading_durable_feed
- Reconnect_deduplicates_buffered_and_paged_events
- Revision_gap_fetches_snapshot
- MCP_tools_are_named_and_typed
- MCP_contains_no_generic_neuron_invoke
- MCP_command_uses_same_journal_outbox_feed_path
- AgentGateway_uses_Orleans_client_reference
- AgentGateway_is_not_referenced_by_production_projects
- Development_principal_populates_organization_principal_and_space

Commit message:
feat: add typed gateways and MCP tools
```

Append the Grok Session Contract.

### Worker 3D: Flutter Renderer

**Assigned paths:**

```text
workspace/lib/**
workspace/test/**
workspace/integration_test/**
```

**Worker prompt:**

```text
Replace the Flutter duplicate UI rail with a renderer for the approved UiSurface protocol.

Assigned paths:
- workspace/lib/**
- workspace/test/**
- workspace/integration_test/**

Flutter renders UiSurface and UiBlock, stores the feed cursor and per-surface revision, applies validated UiSurfacePatch operations, sends opaque UiAction IDs with expected revision, and requests a snapshot on gaps. It contains no neuron catalog, workflow state, provider logic, arbitrary action URLs, or alternate window/feed model.

Required tests:
- snapshot_renders_supported_block_kinds
- contiguous_patch_updates_surface
- duplicate_patch_is_ignored
- revision_gap_requests_snapshot
- action_sends_surface_action_and_expected_revision
- reconnect_persists_and_reuses_feed_cursor
- unknown_schema_version_fails_closed
- renderer_displays_sanitized_failure_without_raw_provider_data

Run:
flutter analyze
flutter test

Commit message:
feat: render durable neuron UI surfaces in Flutter
```

Append the Grok Session Contract and the Flutter-specific requirement to use the Dart MCP package tools before inspecting unfamiliar dependencies.

### Phase 3 Integration Order

Cherry-pick and verify in this order:

1. Worker 3A.
2. Worker 3B.
3. Worker 3C.
4. Worker 3D.

After every cherry-pick:

```text
dotnet test tests/Brain.Tests/Brain.Tests.csproj --logger "console;verbosity=minimal"
dotnet test --logger "console;verbosity=minimal"
```

After Flutter:

```text
flutter analyze
flutter test
```

Resolve integration failures with a new narrowly scoped Grok session. Never resume the original worker against a changed base.

---

## Phase 4: Serial Hosting and Azure Integration Worker

**Assigned paths:**

```text
src/Brain.Kernel.Host/**
tests/Brain.Tests/Hosting/**
```

Codex retains ownership of `src/Brain.AppHost/**` and central files.

**Worker prompt:**

```text
Complete production and local hosting composition outside the AppHost.

Assigned paths:
- src/Brain.Kernel.Host/**
- tests/Brain.Tests/Hosting/**

Register Brain.Kernel, Brain.AI, Brain.Google, and Brain.Salesforce explicitly. Register the Azure Blob journal provider from the accepted kill gate. Configure provider-neutral names for journal, persistent events, UI feed, reminders, membership, and grain storage. Local concrete resources will be injected by AppHost; production concrete resources come from configuration and managed identity.

Required tests:
- Host_registers_all_neuron_grain_classes_explicitly
- Host_uses_AzureBlobJournalStorageProvider_when_configured
- Host_rejects_volatile_journal_in_non_development_environment
- Host_configures_persistent_stream_provider
- Host_registers_durable_reminders
- Host_contains_no_AppDomain_scanning
- Host_logs_sanitized_provider_failures

Commit message:
feat: compose durable neuron host
```

Append the Grok Session Contract.

Codex then updates `src/Brain.AppHost/**` itself:

- local Azurite-backed Azure Blob journal;
- Azure Queue persistent streams locally;
- Event Hubs persistent streams in production configuration;
- persistent reminders and membership;
- Orleans `.AsClient()` reference only for Gateway, MCP, and development AgentGateway;
- development-only DevUI;
- model resources and secrets passed by references, never logged.

---

## Phase 5: Demolition and Topology Migration

**Mode:** Serial, Codex-controlled. A Grok worker may propose deletions, but Codex performs or approves the final deletion set.

Move or replace:

```text
kernel/Brain.Contracts           -> src/Brain.Contracts
kernel/Brain.Client              -> src/Brain.Client
kernel/Brain.Kernel              -> src/Brain.Kernel
modules/Brain.Modules.Ai         -> src/Brain.AI
modules/Brain.Modules.Google     -> src/Brain.Google
modules/Brain.Modules.Salesforce -> src/Brain.Salesforce
hosts/Brain.Kernel.Host          -> src/Brain.Kernel.Host
edge/Brain.UiGateway             -> src/Brain.Gateway
edge/Brain.Mcp                   -> src/Brain.Mcp
hosts/DigitalBrain.AppHost       -> src/Brain.AppHost
```

Delete after replacement tests are green:

```text
kernel/Brain.Contracts/INeuronKind.cs
kernel/Brain.Contracts/NeuronEnvelope.cs
kernel/Brain.Kernel/NeuronGrain.cs
kernel/Brain.Kernel/KindCatalog.cs
kernel/Brain.Kernel/CatalogKind.cs
kernel/Brain.Kernel/EffectKind.cs
kernel/Brain.Client/NeuronProxy.cs
edge/Brain.Mcp/NeuronTools.cs
modules/Brain.Modules.Behaviors/**
modules/Brain.Modules.Connections/**
modules/Brain.Modules.Sdk/**
modules/Brain.Modules.Web/**
modules/Brain.Modules.Workspace/**
behaviors/**
hosts/DigitalBrain.ServiceDefaults/**
tests/Brain.ConformanceTests/**
tests/Brain.KernelTests/**
docs/superpowers/plans/2026-07-16-*.md
docs/superpowers/plans/2026-07-17-*.md
```

Retain this execution plan only while execution is active. Delete it after owner acceptance, leaving `README.md` and `CLAUDE.md` as the living documentation.

Required topology assertions:

- exactly the approved projects remain;
- no project or namespace contains `Modules`, `Integrations`, `Edges`, `Kinds`, or generic `Invoke`;
- `Brain.Contracts` has only allowed dependencies;
- Agent Framework appears only in AI, Google where agent composition requires it, and development AgentGateway;
- Azure SDK appears only in executable hosting/provider composition;
- MCP SDK appears only in MCP edge and domain MCP client adapters;
- production projects do not reference AgentGateway;
- one UI contract and one feed path remain.

---

## Phase 6: Adversarial Grok Reviews

These sessions are read-only. Run all three in parallel against the integrated branch. Use `--permission-mode=plan` and do not use `--worktree`.

### Reviewer 6A: Distributed Semantics

```text
Review the integrated DigitalBrain implementation as an adversarial principal distributed-systems architect. Do not edit. Verify activation behavior, nonreentrancy, one participant step per turn, journal acknowledgement boundaries, command deduplication, event redelivery, ordering, outbox crash windows, reminder recovery, cancellation, external effect idempotency, checkpoint restoration, and causal-loop prevention. Return findings only, ordered by severity, with exact file and line evidence. Treat any unproven exactly-once claim as a defect.
```

### Reviewer 6B: Boundary and Dependency Review

```text
Review the integrated DigitalBrain project graph and public types. Do not edit. Verify the approved tree, SDK isolation, client-safe Brain.Contracts, absence of taxonomy projects, absence of generic invocation/scanning, expressive public naming, development-only AgentGateway, and one UI rail. Return findings only, ordered by severity, with exact project/file evidence and the dependency arrow that is violated.
```

### Reviewer 6C: Security, Identity Seam, and Telemetry

```text
Review the integrated DigitalBrain implementation for trust-boundary and data-exposure defects. Do not edit. Verify OrganizationId, PrincipalId, and SpaceId propagation; no premature identity framework; future Stripe compatibility; provider credential isolation; typed MCP authorization points; approval/idempotency for mutations; sanitized durable failures; and absence of prompts, tokens, email bodies, Salesforce records, credentials, or payment data in logs, traces, exceptions, feeds, and test snapshots. Return findings only, ordered by severity, with exact evidence.
```

Codex triages every finding. Any accepted finding receives a new test-first repair session with exclusive ownership. Reviewers never fix their own findings.

---

## Phase 7: Final Verification

- [ ] `git diff --check`
- [ ] `dotnet test tests/Brain.Tests/Brain.Tests.csproj --logger "console;verbosity=minimal"`
- [ ] `dotnet test --logger "console;verbosity=minimal"`
- [ ] `flutter analyze`
- [ ] `flutter test`
- [ ] Start the integrated AppHost through Aspire.
- [ ] Confirm all required resources become healthy.
- [ ] Run the smallest vertical proof through typed MCP StartDiscussion.
- [ ] Kill the silo after journal commit and before outbox publication.
- [ ] Restart and verify one logical participant response, one checkpoint, and one UI revision.
- [ ] Redeliver the UI event and verify feed deduplication.
- [ ] Reconnect Gateway during publication and verify no gap.
- [ ] Deliver a revision gap and verify snapshot fallback.
- [ ] Cancel during a participant call and verify no later participant begins.
- [ ] Inspect console logs, structured logs, and traces for sensitive data.
- [ ] Run architecture dependency tests.
- [ ] Run prohibited-symbol searches.
- [ ] Verify `sources/` was never added or modified.
- [ ] Verify only the approved project tree remains.
- [ ] Run a final Grok `--best-of-n=2 --check` read-only acceptance review.

Completion requires command output, not summaries.

## New Codex Session Bootstrap Prompt

```text
Work in the existing reactive-neurons worktree on branch codex/reactive-neurons.

Your goal is to implement the owner-approved DigitalBrain reactive-neuron architecture by orchestrating multiple Grok CLI 0.2.101 sessions using grok-4.5. You are the integration authority. Grok workers are bounded implementation and review workers; never trust or merge their output without inspecting diffs and rerunning tests.

Read completely before acting:
1. AGENTS.md
2. CLAUDE.md
3. docs/superpowers/plans/2026-07-18-digitalbrain-grok-orchestration.md

Use superpowers:executing-plans, superpowers:using-git-worktrees, superpowers:test-driven-development, and superpowers:dispatching-parallel-agents as applicable.

Start at Phase 0. Update a visible task plan. Run the pre-change ritual and record baseline evidence. Use native Grok --worktree sessions exactly as defined in the plan. Foundation and integration are serial. Dispatch only workers explicitly marked Parallel. Enforce assigned paths, kill conditions, test-first evidence, commits, and review gates.

Do not ask Grok to make broad architectural decisions. Do not allow Grok workers to edit central files, AppHost, CLAUDE.md, README, the plan, or unassigned paths. Do not let workers push, merge, rebase, or modify sources/. Do not continue past a kill gate. Do not implement a workaround that reintroduces generic JSON invocation, runtime scanning, an in-memory bus, a workflow engine, or a duplicate UI rail.

After every worker:
1. Record session ID, worktree, base commit, and returned commit SHA.
2. Inspect scope and full diff.
3. Confirm the failing test was observed for the intended reason.
4. Cherry-pick only if requirements and ownership are satisfied.
5. Run owning tests.
6. Run the exact root test command at each integration checkpoint.

Continue until all phases, adversarial reviews, restart tests, Flutter checks, Aspire checks, telemetry inspection, demolition, and final project-tree verification are complete. Stop and ask the owner only for a genuinely unresolved product decision or a failed kill gate.
```
