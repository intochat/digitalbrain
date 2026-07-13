# DigitalBrain Behavior Programming Architecture Convergence Implementation Plan

> For agentic workers: execute this roadmap as a sequence of phase-specific plans. Use `superpowers:executing-plans` for each approved phase, `superpowers:test-driven-development` for every behavior change, and `superpowers:verification-before-completion` before claiming a phase complete. Do not implement the entire roadmap in one branch or one session.

**Status:** Planning only. The behavior-programming design is approved; the shaping amendments and operational defaults in this document still require explicit acceptance.

**Source design:** `docs/superpowers/specs/2026-07-13-behavior-programming-design.md`

**Goal:** Replace the remaining generic Neuron/Synapse programming model, duplicated runtime rails, and overgrown client surface with one understandable product path for installing trusted C# behaviors that react to typed events and schedules, use narrow capabilities, and send every external mutation through the existing durable effect gate.

**Target path:** `Client or connector -> typed Synapse -> durable dispatcher/inbox -> BehaviorHost -> IBrainContext capability -> deterministic function or bounded model workflow -> effect gate -> connector adapter -> durable outcome/feed`

**Primary technology:** .NET 11, Aspire 13.4, Orleans 10.2, ASP.NET Core, Microsoft Agent Framework, Microsoft.Extensions.AI, Model Context Protocol C# SDK, OpenTelemetry, Reqnroll 3, xUnit, Flutter, Gmail API, Salesforce APIs, Azure Storage.

---

## 1. Executive recommendation

Do not build the approved architecture as an infrastructure-first rewrite. Deliver it as four vertical product slices, preceded by a deletion phase and followed by hardening:

1. **Converge and delete:** finish the in-flight typed Salesforce capability work, remove the old generic runtime and disconnected client surfaces, and establish a measured deletion budget before adding projects.
2. **Scheduled read-only behavior:** install a package, derive its manifest, fire a schedule, run a behavior, read bounded capabilities, write behavior state, and render a feed card. This proves the complete programming model without mutation risk.
3. **Event-driven propose-only behavior:** ingest Gmail history, emit `EmailReceived`, run a behavior, read Salesforce, and produce a gated proposal. This proves delivery, deduplication, retries, provider-scoped identities, and approval evidence.
4. **Policy-controlled apply:** graduate an already-proven proposal path to policy-authorized application with unchanged effect-gate invariants.
5. **Authoring and operations:** add natural-language scaffolding, Reqnroll acceptance gates, pause/resume/rollback/replay, observability, deployment, and capacity hardening only after the runtime path is real.

This ordering keeps the behavior programming product continuously demonstrable. Every slice must run through Aspire, use real Orleans persistence in its integration tests, and leave no compatibility rail behind.

## 2. Requirement challenges and shaping amendments

The following amendments reduce coordination state, project count, or premature product surface. They are recommendations, not silent changes to the approved design.

### Amendment A: merge subscription ownership into the behavior registry for v1

The approved design names both `BehaviorRegistryNeuron` and `SubscriptionRegistryNeuron`. A package manifest already defines its subscriptions, so separate authorities create a dual-write problem during install, upgrade, rollback, and uninstall.

Recommended v1 model:

- `BehaviorRegistryGrain` owns installed versions, active version, lifecycle state, derived schedule declarations, and derived Synapse subscriptions.
- `SynapseDispatcherGrain` reads an indexed subscription projection maintained atomically by the registry.
- Split out a subscription registry later only if measured fan-out or registry contention justifies it.

Acceptance condition: one grain transaction changes the active behavior version and its active subscriptions. No window may route a Synapse to a version that is not active.

### Amendment B: one workbench/CLI executable in addition to BehaviorHost

Keep `hosts/DigitalBrain.BehaviorHost` as the sandboxed execution process. Build `tools/DigitalBrain.Brain` as the user-facing CLI, including local workbench commands. Do not create a separate workbench web service in v1. If a browser UI is later required, it should call the CLI/runtime API rather than own a second compiler or package lifecycle.

### Amendment C: no separate behavior runtime class library

Use only:

- `src/DigitalBrain.Behaviors.Sdk` for the Orleans-free public programming surface;
- `src/DigitalBrain.Behaviors.TestKit` for `FakeBrainContext`, Reqnroll bindings, and deterministic fixtures;
- `hosts/DigitalBrain.BehaviorHost` for package loading, polling, execution budgets, and capability proxies;
- existing `DigitalBrain.Kernel.Abstractions` and `DigitalBrain.Kernel` for cluster contracts and grain implementations.

Avoid a `DigitalBrain.Behaviors.Runtime` project that would become another shared dependency layer.

### Amendment D: use ordinary persistent state for inboxes before adopting Orleans Journaling

The repository currently consumes preview/alpha Orleans Journaling packages for a legacy Synapse journal. The behavior rail needs explicit inbox operations, deduplication, leases, acknowledgements, retries, parking, and replay. Those operations map directly to the repository's existing encrypted persistent-state wrapper and are easier to reason about as a single state transition.

Default decision:

- implement bounded inbox and dispatcher ledgers with `EncryptedPersistentState<TState>`;
- run a focused capacity and write-amplification spike before finalizing state shape;
- remove Journaling packages and the legacy journal storage resource unless the spike proves a concrete requirement that ordinary grain persistence cannot meet.

### Amendment E: one configured tenant/workspace execution scope in v1

The BehaviorHost must not discover every tenant by scanning cluster state. Start with an explicit configured tenant/workspace scope per deployment. The host asks the registry in that scope for active behaviors and polls those inboxes. Multi-scope hosting requires a later authority and capacity design.

### Amendment F: defer `ProposalDecided` and rich timezone UI

- Do not emit `ProposalDecided` until a real behavior needs to react to approvals. The feed and audit records already expose decisions.
- Store schedule timezone explicitly and use a timezone-aware cron calculation, but expose UTC-only authoring in the first slice. Add friendly timezone selection after daylight-saving acceptance scenarios exist.

### Amendment G: one state provider for behavior rail grains

Use one encrypted `behaviorstate` storage provider/container for registry, dispatcher, inbox, policy, schedule, and behavior state in v1. Distinguish record kinds inside encrypted envelopes. Split providers only when different durability, throughput, retention, or access policies are measured.

### Decision gate 1

Before Phase 1 begins, explicitly accept or reject Amendments A through G. Rejection is allowed, but the phase plan must document the additional consistency or operational mechanism it introduces.

## 3. Non-negotiable architecture invariants

1. Behavior source code and `DigitalBrain.Behaviors.Sdk` have no Orleans, Aspire, ASP.NET Core, connector SDK, database, filesystem, network, process, environment-variable, or secret APIs.
2. Only the trusted BehaviorHost owns an Orleans client and converts grain operations into `IBrainContext` calls.
3. Every external mutation uses the existing typed INO effect-plan authority, durable approval evidence, idempotency, lease/fence validation, connector execution, and outcome verification.
4. Read capabilities are typed, provider-scoped, allowlisted, and bounded. No generic reflection-based tool gateway is exposed to behavior code.
5. Synapse is the single user-visible typed event concept. “Neuron” may remain only as an Orleans grain naming convention during migration; it is not a base class or public programming abstraction.
6. Delivery is at least once. Handlers, capability proxies, proposal creation, feed writes, and emitted Synapses must be idempotent under duplicate execution.
7. FIFO is guaranteed only within one behavior inbox. No global ordering claim is made.
8. Orleans streams and broadcast channels are not the correctness rail. They may later carry non-critical progress or observability only.
9. Package manifests are derived from compiled code and checked artifacts. Users do not maintain a second handwritten manifest.
10. Package install, upgrade, activate, pause, resume, rollback, uninstall, replay, and policy changes are durable audited operations.
11. Package code cannot grant itself capabilities. Requested capabilities are intersected with server-side grants and environment policy.
12. The Flutter app remains a thin authenticated client over the retained runtime surface; it does not embed behavior execution or duplicate server authority.
13. No compatibility facade remains after a phase's callers have migrated. Delete adapters in the same phase that makes them obsolete.
14. New tracked C#, Dart, Proto, PowerShell, shell, XML, MSBuild, YAML, and JSON-with-comments files contain zero comments. Generated source is untracked or deterministically sanitized.

## 4. Research-backed framework decisions

### 4.1 Aspire 13.4

Use the AppHost as the only distributed application model and `aspire start` as the local lifecycle command required by the repository's Aspire skill.

Planned usage:

- Declare Orleans with `AddOrleans("default")` and attach clustering, one default grain-storage resource, reminders, and the behavior-state provider in the AppHost model.
- Give silo processes `.WithReference(orleans)` and Orleans client processes `.WithReference(orleans.AsClient())`.
- Rely on the Orleans reference's transitive storage references instead of repeating each Azure Storage reference on the RuntimeHost. Prove the environment-variable and dependency graph parity with AppHost model tests before deleting explicit references.
- Use `.WithReplicas` only for stateless processes such as BehaviorHost and MCP after leasing/idempotency tests prove safe concurrency.
- Add `/health` and `/alive` checks through ServiceDefaults, `.WithHttpHealthCheck`, and `.WaitFor` dependencies so the dashboard reflects usable state rather than process existence.
- Add full AppHost integration tests with `DistributedApplicationTestingBuilder.CreateAsync`, `BuildAsync`, `StartAsync`, `ResourceNotifications.WaitForResourceHealthyAsync`, and real clients.
- Preserve the production managed-identity configuration until a deployed parity test proves the Aspire-generated Orleans configuration covers it. Local simplification must not erase the production authentication path by assumption.

Execution commands for every phase that changes composition:

```powershell
aspire doctor
aspire start --isolated --non-interactive
aspire describe
$resource = "<resource-from-aspire-describe>"
aspire wait $resource
aspire logs $resource
aspire otel traces $resource
```

The exact resource name must be taken from `aspire describe`; do not bake an unverified name into automation.

### 4.2 Orleans 10.2

Use Orleans for identity, single-writer state transitions, reminders, activation placement, and typed commands/queries.

Planned grain boundaries:

- `IBehaviorRegistryGrain`: package/version/lifecycle/subscription authority.
- `ISynapseDispatcherGrain`: durable fan-out ledger for one scope and Synapse partition.
- `IBehaviorInboxGrain`: ordered delivery state for one behavior installation.
- `IApprovalPolicyGrain`: effective policy and graduation evidence for one scope/capability.
- `IScheduleGrain`: cron state, next due time, reminder reconciliation, and `ScheduleFired` emission.
- `IBehaviorStateGrain`: bounded opaque JSON state for one behavior installation and state key.

Use additive interface evolution and explicit `[GrainType]` names for durable public identities. Avoid overloaded grain methods, mutable wire types, implementation types in contracts, and implicit type-name persistence. Pass cancellation tokens where abandoning client work is safe, but do not treat client cancellation as transaction rollback.

Do not use broadcast channels for delivery: they are intentionally lossy. Do not use streams merely because an event exists: the behavior rail requires durable per-subscriber acknowledgement, replay, caps, poison handling, and causal depth.

Use Orleans reminders as wake-up hints, not a perfect cron log. Persist `nextDueAt`; on activation or reminder delivery, atomically reconcile due occurrences and emit at most the configured catch-up count. Orleans reminders skip missed ticks, so correctness lives in persisted schedule state.

### 4.3 Reqnroll 3 and xUnit

Treat executable behavior specifications as package acceptance gates, not documentation.

- Keep scenario state instance-scoped; do not use statics because feature-level tests may run in parallel.
- Configure missing or pending steps as errors.
- Run Reqnroll dry-run binding validation in CI before executing scenarios.
- Generate code-behind under `obj` with `ReqnrollUseIntermediateOutputPathForCodeBehind=true`; never track generated feature files.
- Put shared bindings in `DigitalBrain.Behaviors.TestKit` and keep package-specific steps narrow.
- Run the root `dotnet test --logger "console;verbosity=minimal"` command without filters as the repository gate.

### 4.4 Microsoft Agent Framework and Microsoft.Extensions.AI

Keep model APIs behind a bounded `IBrainContext.Model` capability implemented by the host.

- Register `IChatClient` in host dependency injection and compose middleware through Microsoft.Extensions.AI.
- Use structured output with strict JSON contracts and rejection of unmapped members.
- Apply OpenTelemetry middleware at the host boundary with content recording disabled by default.
- Use Agent Framework sessions only inside trusted host workflows that genuinely require multi-turn state. Serialize session state into platform-owned storage; never leak framework session objects into the SDK.
- Expose named platform workflows such as classification or structured extraction before exposing a general agent/tool loop.
- Enforce wall-clock, token, model, and tool-call budgets outside package code.

### 4.5 Model Context Protocol C# SDK

Keep MCP as a thin external edge over typed application operations.

- Preserve ASP.NET Core authentication and authorization before MCP dispatch.
- Evaluate stateless HTTP transport because the current MCP surface does not require server-to-client requests. Prove capability parity before changing transport state.
- Register explicit tool classes and map each tool to retained typed grain operations.
- Delete `PlanInoToolGateway` and generic reflection/tool-routing code once the current typed Salesforce operations have replaced every caller.
- Configure MCP Orleans access from `orleans.AsClient()` through Aspire. Add an AppHost integration test before removing custom Azure Table/Redis client configuration branches.

### 4.6 OpenTelemetry

- Define `ActivitySource` and `Meter` names for behavior dispatch, inbox delivery, execution, capability calls, policy decisions, effect plans, connector calls, and feed projection.
- Propagate correlation, causation, Synapse, behavior installation, and effect-plan identifiers as trace context or bounded tags.
- Never use tenant IDs, message IDs, package names from untrusted code, prompts, email subjects, or payload bodies as metric dimensions.
- Add in-memory exporter tests for span parentage, failure status, and mandatory low-cardinality tags.
- Remove the legacy `DigitalBrain.Neuron` source after its last caller is deleted.

### 4.7 Flutter

- Preserve the production path `RuntimeSessionOwner -> RuntimeController -> gRPC -> SurfaceView`.
- Keep only widget primitives reachable from authenticated chat, feed, approval, behavior lifecycle, and error states.
- Delete the unused performance SDK if the server has no matching RPC.
- Prove import reachability before deleting `features`, `widgets`, old InoLang/RFW editor surfaces, or media packages.
- Add lifecycle tests for inactive/hidden/resumed transitions and accessibility guideline tests for new behavior cards.
- Keep server-provided UI data declarative and allowlisted; do not allow behavior packages to send executable Flutter/RFW code.

### 4.8 Gmail synchronization, cron, and package isolation

- Gmail history IDs are increasing but not contiguous. Persist the latest returned history ID only after all pages are processed. On HTTP 404 for an expired `startHistoryId`, perform a bounded full synchronization and establish a new cursor.
- Use `messageAdded` data as a trigger and fetch only the metadata/body fields needed by granted capabilities.
- Use Cronos as a cron occurrence calculator, not as a scheduler. Store UTC instants and an explicit timezone; let Orleans reminders wake the schedule grain.
- Do not claim `AssemblyLoadContext` is a security boundary. It provides loading and unloadability, but package code still has process permissions. Graduated trust therefore requires process/container identity, OS restrictions, network policy, read-only package mounts, resource quotas, and a narrow host IPC boundary.

Primary operational references:

- [Aspire Orleans integration](https://aspire.dev/integrations/frameworks/orleans/)
- [Aspire integration testing](https://aspire.dev/testing/write-your-first-test/)
- [Aspire Service Defaults](https://aspire.dev/fundamentals/service-defaults/)
- [Orleans grain persistence](https://learn.microsoft.com/en-us/dotnet/orleans/grains/grain-persistence/)
- [Orleans reminders](https://learn.microsoft.com/en-us/dotnet/orleans/grains/timers-and-reminders/)
- [Gmail synchronization guide](https://developers.google.com/workspace/gmail/api/guides/sync)
- [Gmail history API](https://developers.google.com/workspace/gmail/api/reference/rest/v1/users.history/list)
- [Cronos](https://github.com/HangfireIO/Cronos/blob/main/README.md)
- [AssemblyLoadContext](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.loader.assemblyloadcontext)
- [Assembly loading concepts](https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/understanding-assemblyloadcontext)

---

## 5. Current-state baseline and deletion budget

### 5.1 Baseline captured during shaping

- 634 tracked files.
- 451 tracked code/configuration files.
- Approximately 58,043 code/configuration lines.
- Approximate distribution: `app` 22,122; `src` 16,396; `tests` 12,354; `integrations` 5,099; `hosts` 758; `deploy` 538.
- 17 C# projects plus Flutter.
- 174 files contain approximately 1,211 tracked comment lines in file types where repository policy forbids them.
- The first conservative legacy-deletion set is approximately 4,240 lines across 59 files, or 7.3% of the baseline, before Flutter reachability cleanup, duplicate provider branches, comments, generated source, and deployment simplification.

Recalculate these numbers at Phase 0 because the worktree currently contains an unfinished typed Salesforce operation change.

### 5.2 Existing user work that must be preserved

Phase 0 begins from a dirty worktree containing typed Salesforce capability work in:

- `integrations/DigitalBrain.Salesforce/ISalesforceApiClient.cs`
- `integrations/DigitalBrain.Salesforce/SalesforceApiClient.cs`
- `integrations/DigitalBrain.Salesforce/SalesforceMutationNeuron.cs`
- `integrations/DigitalBrain.Salesforce/SalesforceReadContracts.cs`
- `src/DigitalBrain.Kernel.Abstractions/SalesforceTool.cs`
- `src/DigitalBrain.Kernel/Runtime/PlanInoToolGateway.cs`
- `src/DigitalBrain.Kernel.Abstractions/TypedInoOperations.cs`
- related Salesforce and typed-operation tests.

Do not reset, overwrite, or fold unrelated cleanup into those edits. Finish or explicitly park that work before structural deletion.

### 5.3 Deletion target

The repository rule is a net deletion target of at least 10% before automation. For this roadmap, use a stricter gate:

- delete at least 5,805 baseline code/configuration lines before adding the authoring workbench;
- keep the repository below its Phase 0 line count after the scheduled read-only slice is complete;
- every new project must replace a named old project or prove that its boundary cannot live in an existing project;
- report gross deleted lines, gross added lines, net lines, projects removed/added, packages removed/added, comments removed, and test count after every phase.

### 5.4 High-confidence deletion set

Delete after CodeGraph/call-site proof and migration of any live contract:

- generic Core abstractions: `Experience`, checkpoint interfaces, `IHandle`, `INeuron`, `INeuronStateProtector`, activation/identity/scope records, protected checkpoint types, old SDK `IAgent`, signal types, generic Synapse payload helpers, and DB Synapse types;
- generic Kernel abstractions and runtime: `LlmAttribute`, `Neuron`, state protectors, `SynapseDispatch`, `SynapseStream`, `LlmNeuron`, `Responder`, prototype journals, checkpoint helpers, journal JSON helpers, task Synapses, and the encrypted Synapse JSON converter;
- old `DigitalBrain.TestKit` generic runtime/timeline helpers and their legacy tests;
- stale `BrainProgramming` and architecture-convergence documents after their surviving decisions are represented by executable tests and the final README/CLAUDE text;
- obsolete `journal` Aspire resource and Orleans Journaling packages after the inbox persistence spike passes;
- Redis Orleans client packages and configuration if AppHost client-injection parity is proven;
- unused Flutter performance SDK and its dependency wiring;
- disconnected `DigitalBrain.Ui.Runtime` and likely `DigitalBrain.Ui.Contracts` after all retained feed/card DTOs are migrated;
- old Flutter feature/widget/editor/media surfaces that are unreachable from the production router;
- generic MCP plan/tool gateway after typed operation parity;
- comments in all prohibited tracked file types;
- tracked generated Dart/Proto/Reqnroll source, replaced by deterministic generation under ignored output directories.

### 5.5 Deletion proof protocol

For each deletion cluster:

1. Use CodeGraph callers/callees and `rg` to enumerate references.
2. Identify runtime registration, reflection, serialization, source-generator, config, and package references that static call graphs may miss.
3. Add or identify the acceptance test that protects the retained product behavior.
4. Delete the entire cluster, its tests, registrations, package references, storage resource, telemetry source, and docs in one change.
5. Run the full root test suite and the affected Aspire vertical path.
6. Do not create a deprecated wrapper unless an external versioned consumer has been proven.

---

## 6. Target repository shape

### 6.1 Projects to retain

- `src/DigitalBrain.Core`
- `src/DigitalBrain.Kernel.Abstractions`
- `src/DigitalBrain.Kernel`
- `src/DigitalBrain.Mcp`
- `src/DigitalBrain.Aspire`
- `hosts/DigitalBrain.ServiceDefaults`
- `integrations/DigitalBrain.Google`
- `integrations/DigitalBrain.Salesforce`
- `hosts/DigitalBrain.RuntimeHost`
- `hosts/DigitalBrain.AppHost`
- `tests/DigitalBrain.Tests`, with focused folders and fixtures rather than a project per small component
- `app`, after reachability cleanup
- `deploy`, until deployment parity proves it can be simplified or replaced

### 6.2 Projects to add

- `src/DigitalBrain.Behaviors.Sdk`
- `src/DigitalBrain.Behaviors.TestKit`
- `hosts/DigitalBrain.BehaviorHost`
- `tools/DigitalBrain.Brain`

Add one `tests/DigitalBrain.Behaviors.Tests` project only if keeping behavior SDK/TestKit tests inside `DigitalBrain.Tests` would force references from the main test assembly to package-compilation tooling or undermine isolation. This is a Phase 2 decision backed by the actual dependency graph.

### 6.3 Projects to remove

- `src/DigitalBrain.Ui.Runtime`
- `src/DigitalBrain.Ui.Contracts`, if no independent retained client contract remains
- existing generic `tests/DigitalBrain.TestKit` and `tests/DigitalBrain.TestKit.Tests` projects

The project-count target after convergence is no more than the current 17 projects plus one. Four additions therefore require at least three removals and preferably consolidation of another obsolete project boundary.

### 6.4 Dependency direction

```text
DigitalBrain.Core
    ^
DigitalBrain.Kernel.Abstractions       DigitalBrain.Behaviors.Sdk
    ^                                      ^
    |                                      |
DigitalBrain.Kernel                  Behavior package DLLs
    ^                                      |
    |                               DigitalBrain.BehaviorHost
    +--------------+-----------------------+
                   |
       DigitalBrain.RuntimeHost
          ^       ^       ^
          |       |       |
       Google  Salesforce  MCP edge
                   ^
          DigitalBrain.AppHost

DigitalBrain.Behaviors.TestKit -> DigitalBrain.Behaviors.Sdk
DigitalBrain.Brain -> authenticated runtime management API
Flutter -> authenticated gRPC/runtime API
```

Forbidden references:

- SDK -> Orleans/Aspire/connectors/ASP.NET/Agent Framework/MCP.
- package -> Kernel or integration projects.
- Kernel.Abstractions -> connector implementations or BehaviorHost.
- AppHost -> runtime service-registration implementation.
- Flutter -> package compiler or grain implementation details.

---

## 7. Core contract plan

### 7.1 SDK public surface

Create these files in `src/DigitalBrain.Behaviors.Sdk`:

- `BehaviorAttribute.cs`
- `OnSynapseAttribute.cs`
- `OnScheduleAttribute.cs`
- `BehaviorCapability.cs`
- `IBehaviorOn.cs`
- `IBrainContext.cs`
- `IBrainClock.cs`
- `IBrainLog.cs`
- `IBrainModel.cs`
- `IBrainSurface.cs`
- `IBrainState.cs`
- `IGmailCapability.cs`
- `ISalesforceCapability.cs`
- `MutationIntentResult.cs`
- `Synapses/EmailReceived.cs`
- `Synapses/ScheduleFired.cs`
- `Serialization/BehaviorJsonContext.cs`

Minimum semantic contracts:

```csharp
public interface IBehaviorOn<in TSynapse>
{
    ValueTask HandleAsync(TSynapse synapse, IBrainContext brain, CancellationToken cancellationToken);
}

public interface IBrainContext
{
    IGmailCapability Gmail { get; }
    ISalesforceCapability Salesforce { get; }
    IBrainModel Model { get; }
    IBrainSurface Surface { get; }
    IBrainState State { get; }
    IBrainClock Clock { get; }
    IBrainLog Log { get; }
    ValueTask EmitAsync<TSynapse>(TSynapse synapse, CancellationToken cancellationToken);
}
```

The exact capability method names must be copied from the retained typed INO contracts rather than invented in parallel. Every method takes a cancellation token, returns bounded DTOs, and has a documented budget enforced by the host.

### 7.2 Manifest model

The package builder derives an immutable manifest containing:

- package ID, semantic version, assembly hash, SDK contract version, and entry types;
- supported typed Synapse aliases and schema versions;
- schedule declarations;
- requested capability IDs and operation-level scopes;
- declared state keys and maximum schema versions;
- minimum host/API version;
- compiled acceptance-test artifact hash;
- signature metadata.

The manifest is emitted as canonical JSON into the package. Installation recomputes it from the assembly and rejects mismatches. Capability IDs use an explicit provider namespace such as `gmail.google.read.message` or `salesforce.salesforce.update.opportunity`; never infer provider from a friendly tool name.

### 7.3 Cluster contracts

Add under `src/DigitalBrain.Kernel.Abstractions/Behaviors`:

- `BehaviorPackageIdentity.cs`
- `BehaviorInstallationIdentity.cs`
- `BehaviorManifest.cs`
- `BehaviorLifecycle.cs`
- `BehaviorRegistryContracts.cs`
- `SynapseEnvelope.cs`
- `SynapseTypeAlias.cs`
- `SynapseDispatchContracts.cs`
- `BehaviorInboxContracts.cs`
- `BehaviorExecutionLease.cs`
- `BehaviorExecutionOutcome.cs`
- `ApprovalPolicyContracts.cs`
- `BehaviorScheduleContracts.cs`
- `BehaviorStateContracts.cs`

`SynapseEnvelope` contains only:

- stable Synapse ID;
- type alias and schema version;
- tenant/workspace scope;
- creation timestamp;
- correlation and causation IDs;
- causal depth;
- canonical payload bytes plus content type;
- producer identity and trace context;
- optional dedupe key.

Do not persist CLR assembly-qualified type names.

### 7.4 Grain state transitions

Implement each transition as a pure state function plus a thin grain shell. State functions make duplicate, stale, and failure cases directly testable.

`BehaviorRegistryGrain` operations:

- install staged version;
- record verification result;
- activate version and derived subscriptions atomically;
- pause/resume installation;
- roll back to verified prior version;
- uninstall while retaining audit tombstone;
- query active projection and package history.

`SynapseDispatcherGrain` operations:

- accept an envelope idempotently;
- snapshot matching active subscriptions;
- create pending inbox deliveries;
- mark each delivery appended;
- retry incomplete appends after activation/restart;
- expire only after every target is appended or parked by explicit operator decision.

`BehaviorInboxGrain` operations:

- append with Synapse-ID dedupe;
- claim head item with lease/fence token;
- renew a bounded execution lease;
- acknowledge outcome with matching fence;
- reschedule after classified transient failure;
- park after retry exhaustion or permanent failure;
- replay a parked item by creating a new delivery attempt linked to the original;
- report depth, bytes, age, and paused/backpressured state.

`ApprovalPolicyGrain` operations:

- set requested policy;
- calculate effective policy from environment ceiling, server grant, behavior grant, operation, and evidence;
- record human approval or denial;
- record graduation/revocation evidence;
- return immutable policy-decision evidence for the effect plan.

`ScheduleGrain` operations:

- validate cron/timezone;
- calculate and persist next due instant;
- reconcile reminder wake-up;
- emit deterministic `ScheduleFired` ID;
- allow one configured catch-up occurrence;
- pause/resume/update while preserving audit history.

`BehaviorStateGrain` operations:

- get typed JSON by key and schema version;
- compare-and-set with expected revision;
- enforce per-value and per-installation byte caps;
- reject undeclared keys;
- migrate through a package-provided pure transform under install-time verification, not on arbitrary reads.

### 7.5 Idempotency construction

Derive operation keys from canonical inputs:

```text
behavior installation ID
+ active package version
+ input Synapse ID
+ handler identity
+ capability operation ID
+ invocation ordinal
+ canonical request hash
```

Use the derived key for proposal creation, effect plans, emitted Synapses, feed surfaces, and external connector idempotency where supported. Persist the invocation ledger before returning success to the package. If a host crashes after the effect but before acknowledgement, replay returns the recorded outcome or resumes verification; it never blindly repeats the mutation.

### 7.6 Proposed initial operational limits

These values are product defaults to validate with load tests, not architectural constants:

| Limit | Proposed v1 value | Behavior |
|---|---:|---|
| Causal depth | 8 | Reject further emit and park the originating delivery |
| Inbox items | 1,000 | Pause dispatch to that inbox and surface backpressure |
| Inbox serialized bytes | 8 MiB | Same as item cap |
| Synapse payload | 256 KiB | Reject before dispatch |
| Behavior state value | 64 KiB | Reject compare-and-set |
| Total behavior state | 1 MiB | Reject new growth |
| Handler wall time | 60 seconds | Cancel host work and classify timeout |
| Model calls per delivery | 3 | Reject additional calls |
| Model output tokens | 4,000 total | Stop/reject over budget |
| Mutation intents | 3 per delivery | Reject additional proposals |
| Retry schedule | 1m, 5m, 30m | Park after third failed retry |
| Auto-pause threshold | 5 consecutive parked/permanent failures | Pause installation and alert |
| Parked/audit retention | 30 days minimum | Deployment policy may retain longer |

### Decision gate 2

Accept these defaults or replace them with measured values before Phase 2 contracts become durable.

---

## 8. Migration roadmap

Each phase below becomes a separate dated execution plan before code changes begin. A phase plan may split into multiple commits, but every commit must keep the full root test suite green. Do not run independent structural phases concurrently because they share project files, package references, and serialization contracts.

### Phase 0: preserve current work and freeze the baseline

**Purpose:** establish a trustworthy starting point without touching product behavior.

#### Task 0.1: finish or park the typed Salesforce operation work

**Files to inspect:**

- `src/DigitalBrain.Kernel.Abstractions/TypedInoOperations.cs`
- `src/DigitalBrain.Kernel.Abstractions/SalesforceTool.cs`
- `src/DigitalBrain.Kernel/Runtime/PlanInoToolGateway.cs`
- `integrations/DigitalBrain.Salesforce/ISalesforceApiClient.cs`
- `integrations/DigitalBrain.Salesforce/SalesforceApiClient.cs`
- `integrations/DigitalBrain.Salesforce/SalesforceMutationNeuron.cs`
- `integrations/DigitalBrain.Salesforce/SalesforceReadContracts.cs`
- `tests/DigitalBrain.Tests/Runtime/TypedInoOperationCapabilityTests.cs`
- `tests/DigitalBrain.Tests/Runtime/TypedReadWorkflowRunnerTests.cs`
- `tests/DigitalBrain.Salesforce.Tests/SalesforceMutationApiClientTests.cs`

Steps:

1. Diff every dirty file and write a one-paragraph scope statement in the Phase 0 execution plan.
2. Run the existing targeted test mentally only for orientation; the actual repository gate remains the full root command.
3. Complete the smallest internally consistent typed-operation slice or move all of its files to a user-approved isolated branch. Never discard them.
4. Record which generic gateway calls the typed operations replace; these become deletion preconditions in Phase 1.
5. Run:

```powershell
dotnet test --logger "console;verbosity=minimal"
aspire doctor
git status --short
```

Exit: the worktree has a clearly owned, verified Salesforce slice, and structural cleanup will not overlap an unfinished edit.

#### Task 0.2: capture architecture and size baselines reproducibly

**Create:** `qa-artifacts/behavior-convergence/baseline.md` as an ignored execution artifact, not a living tracked document.

Capture:

- `git rev-parse HEAD` and branch;
- tracked file/project/package counts;
- code/config lines by top-level directory;
- tracked comments by prohibited file type;
- root test count, pass/fail/skip, and duration;
- Flutter test count and duration;
- `aspire doctor` output;
- AppHost model/resource graph from `aspire describe` in an isolated run;
- CodeGraph status and named legacy cluster caller reports;
- current package versions from `Directory.Packages.props` and Flutter lockfile.

Use scripts under `qa-artifacts` only. Do not add a permanent metrics framework yet.

#### Task 0.3: add characterization tests for retained product paths

**Modify:** existing test folders under `tests/DigitalBrain.Tests` and `app/test`.

Add tests only where an impending deletion lacks protection:

- authenticated chat reaches the retained conversation grain and SurfaceFeed;
- MCP invokes a typed read and typed mutation intent through authentication;
- Salesforce update verify/apply stays behind effect-plan authority;
- RuntimeHost gRPC feed contract renders in Flutter;
- AppHost execution-mode graph contains RuntimeHost, MCP, Flutter, storage, and Orleans references;
- encrypted persistent state detects stale revisions and unknown-write outcomes.

Do not characterize the generic runtime merely to preserve it. Protect user-visible outcomes and safety invariants.

Phase 0 gate:

```powershell
dotnet test --logger "console;verbosity=minimal"
Push-Location app
flutter test
Pop-Location
aspire doctor
```

### Phase 1: delete legacy runtime and shrink the active product surface

**Purpose:** cross the deletion threshold before building behavior infrastructure.

#### Task 1.1: remove the generic Neuron/Synapse execution cluster

**Delete after caller proof:**

- generic Core abstractions listed in section 5.4;
- `src/DigitalBrain.Kernel.Abstractions/LlmAttribute.cs`;
- `src/DigitalBrain.Kernel.Abstractions/Neuron.cs`;
- `src/DigitalBrain.Kernel.Abstractions/NeuronStateProtectors.cs`;
- `src/DigitalBrain.Kernel.Abstractions/SynapseDispatch.cs`;
- `src/DigitalBrain.Kernel.Abstractions/SynapseStream.cs`;
- generic LLM/responder/prototype journal/checkpoint/task-Synapse implementation files under `src/DigitalBrain.Kernel`;
- legacy runtime/timeline tests that test only deleted abstractions.

**Modify:**

- `src/DigitalBrain.Core/DigitalBrain.Core.csproj`
- `src/DigitalBrain.Kernel.Abstractions/DigitalBrain.Kernel.Abstractions.csproj`
- `src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`
- `src/DigitalBrain.Kernel/Hosting/DigitalBrainOrleansExtensions.cs`
- `hosts/DigitalBrain.ServiceDefaults/Extensions.cs`
- `Brain.slnx`

Tests first:

1. Add an architecture test that rejects references to deleted public type names and namespaces.
2. Add a registration test that enumerates retained grain types and fails if legacy grains are registered.
3. Delete implementation and update tests until the new tests and full suite pass.

Exit: `Synapse` remains only as the new typed event term in the approved design/specification; no generic base class or stream service survives.

#### Task 1.2: replace the old TestKit instead of evolving it in place

**Delete:** old generic `DigitalBrain.TestKit` source/project and tests after checking no external package consumer exists.

Do not create the new TestKit in this task. Its API must be driven by real SDK scenarios in Phase 2.

#### Task 1.3: remove legacy journaling and redundant state kinds

**Modify:**

- `Directory.Packages.props`
- `src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`
- `src/DigitalBrain.Kernel/Hosting/DigitalBrainOrleansExtensions.cs`
- `src/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs`
- `hosts/DigitalBrain.AppHost/AppHost.cs`
- encrypted state-kind declarations and tests.

Steps:

1. Add an encrypted-state round-trip/load test using a representative bounded inbox ledger with 1,000 small envelopes and an 8 MiB rejection case.
2. Add crash-boundary state-machine tests for append/claim/ack using the pure proposed model in test code.
3. If the spike stays within the phase's latency and storage targets, remove Orleans Journaling packages, journal provider configuration, journal resource, prototype journal state, and tests.
4. If it fails, stop and document the measured failure. Compare sharded ordinary persistent state with the stable Orleans event-sourcing API before keeping alpha packages.

Exit: no alpha/preview package remains solely for deleted runtime code.

#### Task 1.4: remove duplicate UI server projects

**Inspect and migrate:**

- `src/DigitalBrain.Ui.Contracts`
- `src/DigitalBrain.Ui.Runtime`
- SurfaceFeed projection/presentation contracts in the retained runtime.

Tests first:

1. Serialize each retained chat, data table, approval, error, and feed item through the actual gRPC contract.
2. Render those fixtures through Flutter golden/widget tests.
3. Add an architecture test that the RuntimeHost does not reference `DigitalBrain.Ui.Runtime`.

Then:

- move only genuinely shared wire DTOs to the existing retained contract assembly with the narrowest ownership;
- delete `UiSurface : Synapse`, `ExperienceStep`, old RFW card contracts, sample runtime code, registrations, tests, and both projects where possible;
- update `Brain.slnx` and project references.

Exit: one server-to-client presentation rail remains: SurfaceFeed/runtime gRPC.

#### Task 1.5: prune the Flutter dependency and route graph

**Inspect:**

- `app/lib/main.dart`
- `app/lib/runtime`
- `app/lib/rfw_host`
- `app/lib/features`
- `app/lib/widgets`
- `app/packages/digital_brain_sdk_flutter`
- `app/pubspec.yaml`
- `app/pubspec.lock`

Steps:

1. Generate an import graph from the production entrypoint and compare it with route/widget registration that uses strings or reflection-like maps.
2. Add golden/widget tests for the production chat/feed/approval/error path and app lifecycle transitions.
3. Delete the performance SDK when the absence of its server RPC is confirmed.
4. Delete unreachable feature/widget/editor code.
5. Reduce the giant RFW dictionary to allowlisted primitives used by retained fixtures.
6. Remove media, chart, globe, animation, or embedded-video packages unless a retained acceptance fixture requires them.
7. Run `dart format`, `flutter analyze`, and `flutter test`.

Exit: every direct Flutter dependency has a production import or a named build/test purpose.

#### Task 1.6: enforce the comment/generated-source policy after deletion

Only after the preceding deletion work:

1. Remove prohibited comments from retained files by improving names and splitting functions where necessary.
2. Move generated Proto, Reqnroll, and Dart source to ignored `obj`, `build`, or tool-generated paths.
3. Add a small deterministic repository check under existing build tooling that fails on newly tracked comments in prohibited file types and on known generated-file headers.
4. Exempt only files explicitly required by tool formats after documenting the syntax constraint in CLAUDE.md.

This automation is last in the phase because deletion and simplification determine what remains worth enforcing.

Phase 1 gate:

- at least 5,805 gross lines deleted relative to Phase 0;
- net code/configuration lines down at least 10%;
- at least three projects deleted or consolidated;
- no behavior-related project added yet;
- root .NET tests pass with zero skips;
- Flutter analyze/tests pass;
- isolated Aspire stack becomes healthy;
- authenticated chat, MCP typed operation, Salesforce effect plan, and feed rendering remain green.

### Phase 2: define the SDK, package format, and executable acceptance model

**Purpose:** make the user programming contract small and stable before cluster machinery depends on it.

#### Task 2.1: create the Orleans-free SDK with dependency guard tests

**Create:** files listed in section 7.1 and `src/DigitalBrain.Behaviors.Sdk/DigitalBrain.Behaviors.Sdk.csproj`.

Tests first:

- SDK assembly references only approved base-class-library assemblies;
- public API contains no Orleans, Aspire, ASP.NET, connector, MCP, Agent Framework, Extensions.AI, reflection loader, filesystem, process, HTTP, environment, or dependency-injection types;
- all DTOs serialize under source-generated strict JSON context;
- cancellation is accepted by every asynchronous operation;
- provider-scoped capability IDs are unique and canonical;
- no public mutable collections or implementation types appear.

Implement the smallest surface necessary for the first two behaviors:

- Weekly Email Stats;
- Lead Reply Notifier.

Do not add a general-purpose service locator, dynamic tool invocation, arbitrary prompt API, or generic connector client.

#### Task 2.2: create `FakeBrainContext` and shared Reqnroll bindings

**Create:**

- `src/DigitalBrain.Behaviors.TestKit/DigitalBrain.Behaviors.TestKit.csproj`
- `src/DigitalBrain.Behaviors.TestKit/FakeBrainContext.cs`
- `src/DigitalBrain.Behaviors.TestKit/FakeBrainClock.cs`
- `src/DigitalBrain.Behaviors.TestKit/FakeBrainModel.cs`
- `src/DigitalBrain.Behaviors.TestKit/FakeBrainState.cs`
- `src/DigitalBrain.Behaviors.TestKit/FakeCapabilityLedger.cs`
- `src/DigitalBrain.Behaviors.TestKit/Reqnroll/BrainContextSteps.cs`
- `src/DigitalBrain.Behaviors.TestKit/Reqnroll/SynapseSteps.cs`
- `src/DigitalBrain.Behaviors.TestKit/Reqnroll/MutationIntentSteps.cs`

Tests first:

- deterministic clock and IDs;
- recorded reads, emits, surfaces, model calls, and mutation intents preserve invocation order;
- ungranted capability calls fail exactly as host calls will fail;
- configured duplicate invocation returns the same recorded outcome;
- strict fixtures reject unexpected model/request fields;
- Reqnroll feature-level parallel execution has no shared static state.

#### Task 2.3: define package compilation and manifest derivation

**Create under `tools/DigitalBrain.Brain`:**

- `Commands/BuildCommand.cs`
- `Packaging/BehaviorProjectInspector.cs`
- `Packaging/BehaviorManifestDeriver.cs`
- `Packaging/BehaviorPackageWriter.cs`
- `Packaging/BehaviorPackageVerifier.cs`
- `Packaging/CanonicalJson.cs`

Package contents:

```text
manifest.json
assemblies/<package>.dll
symbols/<package>.pdb
acceptance/<compiled test artifacts>
signature.json
```

Tests first:

- the same inputs produce byte-identical canonical manifest and package hash;
- manifest derivation finds behavior handlers, schedules, requested capabilities, and state declarations;
- ambiguous handlers, duplicate aliases, unsupported SDK versions, reflection/dynamic-code references, and mismatched manifests fail the build;
- path traversal, duplicate ZIP entries, oversized entries, and zip bombs are rejected;
- package build runs Reqnroll dry run and tests before emitting an installable package;
- package assembly cannot reference forbidden assemblies.

Use Roslyn/MSBuild APIs only in the trusted CLI. Behavior code remains normal C# source and standard project output.

#### Task 2.4: add example package fixtures as tests, not production samples

**Create under the behavior test project or test-data folder:**

- `WeeklyEmailStatsBehavior` with `weekly-email-stats.feature`;
- `LeadReplyNotifierBehavior` with `lead-reply-notifier.feature`.

The fixtures compile, derive manifests, run against `FakeBrainContext`, and demonstrate only APIs that will be implemented in the next slices. Do not ship a gallery or marketplace yet.

Phase 2 gate:

- SDK public API review approved;
- both fixture packages build deterministically and pass Reqnroll tests;
- no SDK/package assembly references a forbidden framework;
- SDK and TestKit package APIs stay below the agreed public-member budget;
- repository remains below the Phase 0 net line baseline.

### Phase 3: scheduled read-only behavior vertical slice

**Purpose:** prove install-to-execution-to-feed without mutation complexity.

#### Task 3.1: add behavior rail contracts and pure state machines

**Create:** files listed in sections 7.3 and 7.4 under `src/DigitalBrain.Kernel.Abstractions/Behaviors` and `src/DigitalBrain.Kernel/Behaviors`.

Tests first, organized by state machine:

- registry install, verify, activate, duplicate install, upgrade, rollback, pause, resume, and uninstall;
- atomic active-version/subscription projection;
- dispatcher duplicate Synapse acceptance and partial fan-out recovery;
- inbox append dedupe, FIFO claim, stale fence rejection, lease expiry, retry, park, replay, byte cap, and item cap;
- schedule next occurrence, invalid cron, UTC behavior, DST cases, activation reconciliation, duplicate reminder, and one-catch-up policy;
- behavior state compare-and-set, size cap, undeclared key, schema migration, and duplicate write.

Each test calls a pure transition function first. Grain integration tests then prove persistence and activation behavior with a real Orleans test cluster/AppHost.

#### Task 3.2: add one encrypted behavior storage provider

**Modify:**

- encrypted state-kind declarations;
- `src/DigitalBrain.Kernel/Hosting/DigitalBrainOrleansExtensions.cs`;
- `src/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs`;
- `hosts/DigitalBrain.AppHost/AppHost.cs`;
- AppHost execution-mode tests.

Steps:

1. Declare one `behaviorstate` Azure Storage resource/provider.
2. Map explicit record kinds for registry, dispatcher, inbox, policy, schedule, and user state.
3. Verify encryption, optimistic revision, rollback reconciliation, and poison behavior for every new state kind.
4. Use Aspire model tests to ensure local/test/prod configuration supplies the same logical provider names.
5. Prove Orleans reference transitivity, then remove redundant direct storage references from RuntimeHost configuration.

#### Task 3.3: implement registry, dispatcher, inbox, state, and schedule grains

**Create grain shells under:** `src/DigitalBrain.Kernel/Behaviors/Grains`.

The grain shells:

- validate grain-key identity against command scope;
- call the pure transition;
- write state once per accepted transition where possible;
- perform external grain calls only through an explicit pending-work ledger;
- record activities/metrics;
- never load or execute package code.

Integration tests must force deactivation/restart between each durable step to prove recovery, not only the happy in-memory activation path.

#### Task 3.4: create the trusted BehaviorHost

**Create:**

- `hosts/DigitalBrain.BehaviorHost/DigitalBrain.BehaviorHost.csproj`
- `hosts/DigitalBrain.BehaviorHost/Program.cs`
- `Hosting/BehaviorHostOptions.cs`
- `Execution/BehaviorPackageCatalog.cs`
- `Execution/BehaviorLoadContext.cs`
- `Execution/BehaviorExecutor.cs`
- `Execution/BehaviorExecutionBudget.cs`
- `Execution/BehaviorExecutionScope.cs`
- `Execution/BehaviorInboxWorker.cs`
- `Capabilities/BrainContext.cs`
- capability proxy files required by Weekly Email Stats;
- health and telemetry tests.

Tests first:

- package hash and manifest are reverified before load;
- only the active verified version loads;
- one installation version gets one collectible load context generation;
- unload succeeds after pause/upgrade with no leaked host references;
- configured scope only is polled;
- inbox claim/ack uses lease/fence tokens;
- duplicate delivery returns recorded surfaces/emits/state outcomes;
- wall-time and call budgets are enforced outside behavior code;
- host cancellation does not acknowledge unfinished work;
- missing capability grant fails before handler execution;
- health is unhealthy when the registry or inbox rail is unreachable.

The initial process is trusted code plus trusted/signed package code. Record the absence of a security boundary inside `AssemblyLoadContext`; do not describe it as sandboxing.

#### Task 3.5: wire the BehaviorHost through Aspire

**Modify:**

- `hosts/DigitalBrain.AppHost/AppHost.cs`
- `src/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs`
- AppHost graph tests and full-stack fixture.

AppHost requirements:

- BehaviorHost receives `orleans.AsClient()`;
- package directory is mounted read-only;
- configured tenant/workspace scope is explicit;
- execution identity has no connector secrets and no direct storage credentials beyond Orleans client needs;
- dependency waits for usable Orleans/RuntimeHost health;
- initial replica count is one; scale-out is a later lease test;
- OTLP and health endpoints use ServiceDefaults.

#### Task 3.6: implement install/activate/pause/rollback CLI management path

**Create under `tools/DigitalBrain.Brain/Commands`:**

- `InstallCommand.cs`
- `ActivateCommand.cs`
- `ListCommand.cs`
- `InspectCommand.cs`
- `PauseCommand.cs`
- `ResumeCommand.cs`
- `RollbackCommand.cs`
- `UninstallCommand.cs`

The CLI calls an authenticated management API owned by RuntimeHost; it does not connect directly to storage and does not embed an Orleans client unless a later measured local-only requirement justifies it.

Tests first:

- install rejects unsigned/unverified/mismatched packages;
- activation is separate from installation;
- pause prevents new claims but preserves inbox;
- rollback atomically changes active version/subscriptions;
- uninstall creates an audit tombstone and rejects new dispatch;
- every command is idempotent and prints machine-readable JSON with a stable exit code.

#### Task 3.7: deliver Weekly Email Stats end to end

Acceptance scenario:

1. Build and verify the package.
2. Install and activate it through the CLI.
3. Reconcile a weekly schedule and emit one deterministic `ScheduleFired` Synapse.
4. Dispatcher appends it to the behavior inbox.
5. BehaviorHost claims it and calls a bounded Gmail statistics read capability.
6. Behavior writes its last-success state and publishes one idempotent table/feed surface.
7. Flutter renders the surface.
8. A duplicate reminder, duplicate Synapse, host crash before ack, and host restart produce no duplicate user-visible table.

Add one full Aspire E2E fixture for this exact path. This is the first release candidate.

Phase 3 gate:

- Weekly Email Stats passes unit, Reqnroll, real Orleans persistence, full AppHost, and Flutter rendering tests;
- crash/restart and duplicate-delivery tests pass;
- BehaviorHost has no connector secrets or direct mutation adapters;
- registry/subscription decision remains atomic;
- a healthy Aspire dashboard shows the new resource and trace chain;
- net repository size remains below Phase 0.

### Phase 4: Gmail event ingestion and propose-only Salesforce behavior

**Purpose:** prove real external events, history recovery, cross-provider reads, and mutation proposals without automatic application.

#### Task 4.1: add Gmail watch state and typed event production

**Create or modify under `integrations/DigitalBrain.Google`:**

- `Gmail/GmailWatchOptions.cs`
- `Gmail/GmailHistoryCursorState.cs`
- `Gmail/GmailWatchGrainContracts.cs` in the inward contract project if a grain contract is required;
- `Gmail/GmailWatchGrain.cs`;
- `Gmail/GmailHistoryReader.cs`;
- `Gmail/GmailSynapseMapper.cs`;
- connector and state tests.

State machine tests first:

- initial bounded full synchronization establishes a cursor without replaying all historical mail as new;
- increasing, non-contiguous history IDs are accepted;
- all pages are processed before the new cursor commits;
- duplicate history records and duplicate Pub/Sub wake-ups emit one `EmailReceived` Synapse;
- expired cursor HTTP 404 causes bounded full synchronization and cursor replacement;
- partial page failure retains the previous committed cursor;
- labels and message-added filtering are deterministic;
- payload contains only granted/minimum metadata, not an entire mailbox object;
- OAuth revocation pauses ingestion and surfaces an actionable health state.

The Pub/Sub or polling wake-up is not the correctness record. Gmail history plus the persisted cursor is authoritative.

#### Task 4.2: define a versioned `EmailReceived` schema and evolution policy

**Modify:** SDK and Kernel behavior contracts.

Include stable provider/account/message/thread identities, received instant, normalized sender identity, allowed headers, label IDs, and a capability reference for deferred body access. Do not embed credentials or unrestricted body/attachment content.

Tests:

- v1 canonical JSON fixture remains stable;
- unknown additive fields are tolerated by consumers where safe;
- incompatible changes require a new type alias/schema version and an explicit adapter;
- provider/account scope is part of identity and dedupe.

#### Task 4.3: extend BehaviorHost with bounded Gmail and Salesforce reads

**Create capability proxies under:** `hosts/DigitalBrain.BehaviorHost/Capabilities`.

The host calls typed grain interfaces, not connector SDKs. For every call:

- authorize provider-scoped operation against installation grants;
- validate resource scope;
- enforce item/byte/time budgets;
- canonicalize request and idempotency key;
- record trace and invocation outcome;
- redact secrets/content from logs;
- return SDK DTOs only.

Tests first:

- grant intersection denies a requested but ungranted operation;
- wrong provider/account/workspace identity is rejected;
- pagination cannot exceed budget;
- replay returns recorded outcome when applicable;
- connector exceptions map to stable capability errors.

#### Task 4.4: route mutation intents into the existing effect gate

**Modify:**

- typed INO operation contracts retained from Phase 0;
- `InoEffectPlanAuthority` integration points;
- BehaviorHost Salesforce mutation proxy;
- approval/feed projections;
- effect-plan tests.

The behavior-facing method returns `MutationIntentResult` with `Applied`, `Proposed`, or `Rejected`, but Phase 4 policy always caps the Salesforce update at `Proposed`.

Tests first:

- identical duplicate intent maps to one effect plan/proposal;
- proposal evidence includes behavior installation/version, input Synapse, capability operation, canonical request hash, policy decision, and intended connector call;
- package cannot choose `Applied`;
- direct connector invocation is impossible from BehaviorHost;
- stale lease/fence cannot apply;
- approval followed by apply still performs outcome verification;
- denied/rejected intents are durable and visible without leaking secrets.

#### Task 4.5: delete the generic MCP/plan tool gateway

After typed read and mutation parity tests pass:

- migrate all remaining MCP tools to explicit typed application operations;
- delete `src/DigitalBrain.Kernel/Runtime/PlanInoToolGateway.cs` and generic routing contracts;
- remove reflection-based tool discovery that exists only for the gateway;
- add an architecture test forbidding connector tool IDs that omit the provider namespace.

#### Task 4.6: deliver Lead Reply Notifier end to end

Acceptance scenario:

1. Gmail history emits one typed `EmailReceived` for a lead reply.
2. Dispatcher routes it to an installed Lead Reply Notifier.
3. Behavior reads bounded email context and Salesforce lead/opportunity data.
4. A bounded model workflow produces strict structured classification.
5. Behavior writes a feed notification and proposes a Salesforce update.
6. Proposal is visible but not automatically applied.
7. Duplicate Gmail history, duplicate dispatch, model retry, host crash, and replay produce one notification and one proposal.
8. Expired Gmail history cursor recovers through full synchronization without incorrectly duplicating the proposal.

Phase 4 gate:

- all Gmail cursor/recovery cases pass against a fake API and at least one authorized integration environment;
- Lead Reply Notifier passes full Aspire E2E;
- generic tool gateway and its packages/registrations are deleted;
- no automatic external mutation is possible for a behavior;
- trace shows Gmail ingestion -> Synapse -> inbox -> behavior -> proposal -> feed.

### Phase 5: approval policy, evidence, and controlled automatic application

**Purpose:** make graduated trust an explicit server-owned state machine.

#### Task 5.1: implement approval policy calculation as a pure function

Inputs:

- deployment/environment ceiling;
- tenant/workspace ceiling;
- connector/account ceiling;
- operation risk classification;
- behavior installation grant;
- requested policy;
- verification/test evidence;
- observed successful proposal/apply history;
- active incident or revocation flags.

Output:

- `Reject`, `Propose`, or `Apply`;
- immutable reason codes;
- evidence references;
- expiration/review instant;
- policy revision.

Tests first cover every precedence rule. A lower-trust input always wins. Package code and package manifest never raise the result.

#### Task 5.2: persist policy history and approval evidence

**Create under Kernel behaviors:** policy grain implementation and audited records.

Required operations:

- request policy change;
- approve/deny by authorized human identity;
- graduate after explicit evidence threshold and human/policy authorization;
- revoke immediately;
- expire a grant;
- query effective policy as of an effect-plan decision;
- retain immutable decision evidence after package upgrade/uninstall.

#### Task 5.3: connect the policy result to effect-plan authority

The effect gate, not BehaviorHost, translates policy to proposal/apply behavior. Tests must prove:

- an `Apply` result without matching durable policy evidence is rejected;
- policy revision changes between plan and apply require re-evaluation;
- environment ceiling can downgrade all behaviors immediately;
- approval is bound to canonical request and cannot authorize a mutated payload;
- connector result verification and idempotency are unchanged;
- rollback to an older package version does not restore expired grants.

#### Task 5.4: graduate one narrow Salesforce operation

Choose one reversible, verifiable update already exercised successfully in propose-only mode. Require:

- named operation-level grant;
- minimum successful proposal/approval history;
- zero unresolved verification failures;
- bounded field allowlist;
- operator-visible kill switch;
- compensation or explicit manual recovery procedure;
- E2E test for revoke-during-flight.

Do not graduate arbitrary Salesforce object updates or Gmail sends as a group.

#### Task 5.5: add operator lifecycle and replay controls

Extend CLI/runtime management API with:

- inspect effective policy and evidence;
- grant/downgrade/revoke;
- inspect inbox depth and parked deliveries;
- replay one parked delivery with a reason;
- pause one installation or all behavior execution in a scope;
- inspect effect-plan correlation chain.

Every command is authorized, audited, idempotent, and has a dry-run/inspect form before mutation.

Phase 5 gate:

- one narrow operation applies automatically only under explicit policy;
- revocation and global downgrade tests pass;
- proposal-only remains the default for mutation capabilities;
- no connector mutation path bypasses effect-plan authority;
- policy and effect evidence survive package rollback and uninstall.

### Phase 6: natural-language workbench and package authoring loop

**Purpose:** add the user-facing creation experience only after the target runtime is stable and small.

#### Task 6.1: define the workbench intermediate specification

Do not generate C# directly from an unconstrained prompt. Define a strict `BehaviorIntent` schema containing:

- behavior name and user outcome;
- triggering Synapses and schedules;
- requested read/mutation capabilities;
- state needs;
- expected surfaces/notifications;
- mutation policy request;
- examples and acceptance criteria;
- unresolved questions and risk flags.

The model produces structured output that rejects unmapped fields. The user approves this intent before code generation.

#### Task 6.2: implement the CLI authoring commands

**Create under `tools/DigitalBrain.Brain/Commands`:**

- `NewCommand.cs`
- `ShapeCommand.cs`
- `GenerateCommand.cs`
- `TestCommand.cs`
- `PackCommand.cs`
- `DiffCommand.cs`

Flow:

```text
brain new -> intent interview -> BehaviorIntent -> generated project
-> generated Reqnroll scenarios -> local fake tests -> package build
-> source/manifest/capability diff -> signed install request
```

Tests first:

- ambiguous mutation intent asks for resolution instead of inventing access;
- generated code references only SDK/TestKit;
- generated package requests the minimum capability set derivable from intent;
- regeneration is deterministic for a fixed model fixture;
- user edits survive regeneration through owned-file boundaries;
- dangerous requests fail shaping before compilation;
- no package installs automatically after generation.

#### Task 6.3: use bounded model workflows

Use Microsoft.Extensions.AI structured output for intent and file plans. Use Agent Framework only if multi-step authoring state materially helps; persist its serialized session in workbench-owned local state, not in behavior packages.

Apply budgets:

- allowlisted model IDs;
- token/cost/time ceiling per command;
- no arbitrary tools;
- workspace access limited to the new behavior project;
- source diff and capability diff shown before acceptance;
- prompts and generated source excluded from telemetry content by default.

#### Task 6.4: make Reqnroll the install gate

`brain pack` performs, in order:

1. restore with locked dependencies;
2. compile;
3. architecture/dependency checks;
4. Reqnroll dry run;
5. package tests;
6. deterministic manifest derivation;
7. binary/package policy scan;
8. canonical package creation;
9. signing request.

Any failure prevents package output. The server repeats the safety-verifiable checks and never trusts only the CLI report.

#### Task 6.5: dogfood two authored behaviors

Regenerate the Weekly Email Stats and Lead Reply Notifier fixtures using the workbench. Compare handwritten and generated versions for:

- public behavior complexity;
- capability request minimality;
- test coverage;
- package determinism;
- execution outcome equivalence;
- amount of generated boilerplate.

Simplify the SDK/workbench until both examples remain readable without platform internals.

Phase 6 gate:

- a user can shape, generate, test, inspect, pack, install, and activate a behavior without editing a manifest;
- generated code is ordinary readable C#;
- capability and source diffs are mandatory before install;
- generated examples use only stable released SDK APIs;
- no new runtime rail or workbench service was added.

### Phase 7: Behavior management UI and client cleanup completion

**Purpose:** expose lifecycle and evidence without making Flutter a second authority.

#### Task 7.1: define retained runtime DTOs

Add minimal gRPC/runtime DTOs for:

- installed behavior summary and active version;
- verification status;
- requested/granted/effective capabilities;
- schedule next due;
- inbox depth/oldest age/parked count;
- recent executions and correlated proposals/effects;
- pause/resume/rollback eligibility;
- policy/evidence summary;
- actionable health/error state.

DTOs contain server-calculated actions and reason codes. Flutter does not reproduce authorization or policy logic.

#### Task 7.2: add behavior feed and detail cards

**Modify under `app/lib/runtime` and retained UI-kit folders:**

- behavior installed/activated/paused/failed cards;
- proposal and policy evidence cards;
- schedule and recent-run summary;
- inbox backpressure/parked alert;
- pause/resume/rollback/replay confirmation flows.

Tests first:

- golden tests for all states and text scaling;
- screen-reader labels, tap target, contrast, and semantics tests;
- stale revision/action conflict refreshes server state;
- hidden/resumed lifecycle refreshes sensitive status;
- destructive actions require explicit confirmation and render server reason codes.

#### Task 7.3: finish Flutter pruning

With real behavior screens present, repeat import/dependency analysis. Remove any remaining old RFW editor, unused widget dictionary, old feature, or media dependency not used by authenticated chat/feed/behavior management.

Phase 7 gate:

- operators can understand what ran, why it ran, what it accessed, and whether it proposed/applied an effect;
- all management actions remain server-authorized;
- Flutter dependency count and code size do not exceed the Phase 1 post-prune baseline without an approved reason;
- accessibility and lifecycle suites pass.

### Phase 8: Aspire and host composition convergence

**Purpose:** remove configuration duplication after all real resources and clients exist.

#### Task 8.1: test the AppHost resource model as a contract

**Modify:** existing AppHost execution-mode tests.

Assert:

- one Orleans resource with clustering, reminders, default grain storage, and named behavior storage;
- RuntimeHost is a silo reference;
- MCP and BehaviorHost are Orleans client references;
- transitive storage dependencies exist without duplicate explicit references;
- package mount, scope, auth, OTLP, health, and connector resources vary correctly by execution profile;
- deleted journal/Redis/legacy services are absent;
- no secret value is serialized into the model snapshot.

#### Task 8.2: converge Orleans server configuration

**Modify:** `src/DigitalBrain.Kernel/Hosting/DigitalBrainOrleansExtensions.cs` and RuntimeHost composition.

Steps:

1. Characterize local, test, and production provider names and credentials.
2. Let Aspire-provided `UseOrleans()` configuration own local/test clustering, storage, and reminders.
3. Isolate only the production managed-identity delta behind a focused extension.
4. Remove duplicate registrations and environment parsing.
5. Start a real AppHost in each supported execution profile and verify grain persistence across process restart.

#### Task 8.3: converge Orleans client configuration

**Modify:** `src/DigitalBrain.Mcp/Program.cs`, BehaviorHost composition, and AppHost.

Steps:

1. Add integration tests for `.WithReference(orleans.AsClient())` generated configuration.
2. Switch clients to `UseOrleansClient()` with Aspire-provided configuration.
3. Remove Azure Table/Redis manual branch logic and packages when parity is proven.
4. Verify authentication middleware remains before MCP transport.
5. Evaluate stateless MCP HTTP transport with tool parity and concurrency tests.

#### Task 8.4: split oversized composition files without creating projects

Refactor `hosts/DigitalBrain.RuntimeHost/Program.cs`, `hosts/DigitalBrain.AppHost/AppHost.cs`, and `src/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs` into focused same-project files organized by responsibility:

- resource/profile model;
- secrets/model providers;
- runtime registration;
- auth endpoints;
- gRPC/static hosting;
- behavior resources;
- integration resources;
- health/telemetry.

Use self-explanatory types and methods, not comments or region blocks. Keep AppHost declarative and RuntimeHost as the only concrete runtime composition root.

#### Task 8.5: deployment parity and isolation

**Inspect:** `deploy/Program.cs`, `deploy/Pulumi.yaml`, `deploy/Pulumi.dev.yaml`, and `deploy/DigitalBrain.Deploy.csproj`.

Before changing deployment:

- document the actual target platform and current deployed resources in the phase execution plan;
- compare the Pulumi model with the AppHost resource graph;
- decide whether Aspire publish/deployment artifacts can replace any handwritten resource declaration without losing identity, secret, network, storage-retention, or rollback controls.

BehaviorHost production requirements:

- separate process/container identity;
- no connector or storage secrets beyond its authenticated cluster/client channel;
- network egress denied except the runtime capability channel and telemetry endpoint;
- read-only verified package mount;
- CPU, memory, process, and execution timeout limits;
- non-root identity where supported;
- rolling upgrade with inbox leases preventing duplicate concurrent execution;
- emergency scale-to-zero/pause procedure that preserves queued inboxes.

Do not delete Pulumi merely because Aspire can emit deployment artifacts. Delete only after an environment parity and rollback exercise.

Phase 8 gate:

- all supported profiles start through Aspire;
- RuntimeHost restart preserves grain and behavior state;
- MCP and BehaviorHost clients reconnect without custom provider branches;
- AppHost graph and deployment inventory agree;
- behavior execution has an enforceable process/container boundary;
- no deleted storage resource or package remains.

### Phase 9: reliability, capacity, observability, and final cleanup

**Purpose:** turn the proven product path into an operable system, then delete temporary scaffolding.

#### Task 9.1: deterministic fault-injection suite

Add tests that stop/restart or fault at every durable boundary:

- after dispatcher accepts Synapse but before subscription snapshot persists;
- after snapshot but midway through inbox fan-out;
- after inbox claim but before host starts handler;
- after capability side effect plan persists but before host receives result;
- after external connector success but before outcome verification/ack;
- during state compare-and-set;
- during package upgrade/rollback;
- during policy downgrade/revocation;
- during Gmail page processing before cursor commit;
- during schedule reminder reconciliation.

Assert no lost accepted Synapse, no unauthorized mutation, bounded duplicates, and actionable parked state.

#### Task 9.2: capacity and backpressure tests

Measure at least:

- dispatch fan-out for 1, 10, 100, and 1,000 subscribers;
- inbox state size and write latency at proposed caps;
- BehaviorHost throughput at 1, 2, and 4 replicas;
- hot behavior versus many idle behaviors;
- schedule wake-up bursts;
- model/connector latency amplification;
- poison delivery rate and operator replay throughput;
- Azure Storage transaction volume/cost projection.

Use results to accept or change section 7.6 defaults. If one grain becomes hot, shard by a stable scope/partition key without changing SDK semantics.

#### Task 9.3: telemetry contract and dashboards

Add spans and low-cardinality metrics for:

- Synapses accepted/routed/pending/parked;
- inbox depth, oldest age, retries, and lease conflicts;
- behavior runs, duration, budget rejection, and outcome class;
- capability calls by operation and outcome;
- proposals/applies/rejections and verification failures;
- Gmail cursor age/recovery;
- schedules late/missed/reconciled;
- package install/activate/rollback and host load/unload failures.

Trace tests require the correlation chain across connector, grain, BehaviorHost, effect plan, and feed. Content remains redacted by default.

#### Task 9.4: security review and adversarial package suite

Test packages that attempt:

- forbidden assembly references;
- reflection/dynamic loading;
- filesystem/environment/process/network access;
- thread/task explosion;
- excessive allocation;
- infinite loop and cancellation suppression;
- oversized payload/state/surface;
- capability spoofing or provider confusion;
- package path traversal and signature substitution;
- stale version execution after rollback;
- forged Synapse scope/identity;
- prompt injection into structured model workflows.

Build-time scanning is defense in depth; the production process/container boundary must limit consequences even when a package passes static checks.

#### Task 9.5: documentation convergence and artifact deletion

Update only living documentation:

- README quick start and architecture path;
- CLAUDE working rules, current commands, and retained framework decisions;
- CLI `--help` output generated from command definitions;
- SDK XML/API documentation only if generated outside tracked prohibited comments.

Then delete:

- superseded architecture specs and phase plans whose decisions are executable and reflected in README/CLAUDE;
- ignored QA artifacts after metrics are copied into the completion report;
- example packages that exist only as duplicate documentation, keeping executable fixtures;
- compatibility scripts, migration adapters, temporary feature flags, and stale package pins.

Retain `AGENTS.md` and `.agents/skills` because they are active operational tooling even though the repository generally minimizes living docs.

#### Task 9.6: final dependency and line-budget audit

Produce the completion comparison:

| Measure | Phase 0 | Final | Change |
|---|---:|---:|---:|
| Tracked files | captured | measured | measured |
| Code/config lines | captured | measured | measured |
| C# projects | captured | measured | measured |
| NuGet direct packages | captured | measured | measured |
| Flutter direct packages | captured | measured | measured |
| Prohibited comment lines | captured | 0 or approved syntax exceptions | measured |
| Root tests passed/skipped | captured | measured/0 | measured |
| Full test duration | captured | measured | measured |
| AppHost resources | captured | measured | measured |

Final gate:

- all user journeys and fault cases pass;
- no skipped tests;
- no generic runtime, duplicate UI rail, generic gateway, journaling residue, or dead provider branch remains;
- final dependency graph matches section 6;
- no temporary feature flag or dual-write migration remains;
- production rollback and emergency behavior pause are exercised;
- net repository size and project/package counts meet the accepted convergence targets.

---

## 9. Release slices and observable product outcomes

| Release | User-visible outcome | New correctness proof | Automatic mutations |
|---|---|---|---|
| R0 Converged core | Existing chat, MCP, Salesforce, and feed still work with less code | Characterization tests and deletion audit | Existing gated paths only |
| R1 Scheduled behavior | User installs Weekly Email Stats and receives a weekly table | Package verification, schedule reconciliation, durable inbox, crash idempotency | None |
| R2 Event behavior | Lead reply produces notification and Salesforce proposal | Gmail history recovery, typed event, cross-provider reads, proposal dedupe | None |
| R3 Graduated trust | One narrow verified Salesforce update can auto-apply | Policy evidence, revoke/downgrade, effect verification | One allowlisted operation |
| R4 Authoring | User shapes and generates a tested C# behavior from natural language | Structured intent, capability diff, deterministic package and Reqnroll gate | No expansion by generation |
| R5 Operable platform | User/operator can inspect, pause, replay, roll back, and understand behavior effects | Capacity, fault injection, telemetry, deployment isolation | Policy-controlled |

Do not market R1 as “arbitrary user code sandboxing.” It is trusted/signed package execution in a restricted host. The graduated-trust story becomes credible only after Phase 8 process/container isolation and Phase 9 adversarial testing.

## 10. Test strategy and required commands

### 10.1 Test pyramid

1. **Pure transition tests:** registry, inbox, dispatcher, schedule, policy, state, Gmail cursor, idempotency.
2. **Contract tests:** strict JSON fixtures, public API/dependency rules, provider identity, additive schema evolution.
3. **Grain integration tests:** real persistence, deactivate/reactivate, reminders, client reconnect, lease/fence behavior.
4. **Host component tests:** package loader, capability proxies, budgets, model structured output, telemetry.
5. **AppHost model tests:** resources, references, environment/configuration, profile differences, secrets absent.
6. **Full Aspire E2E:** Weekly Email Stats and Lead Reply Notifier through real processes and storage.
7. **Flutter tests:** contract fixtures, golden rendering, actions, lifecycle, accessibility.
8. **Deployment smoke/fault tests:** identity, network restrictions, rolling upgrade, pause, persistence, rollback.

### 10.2 Mandatory local phase verification

```powershell
dotnet test --logger "console;verbosity=minimal"
Push-Location app
dart format --output=none --set-exit-if-changed .
flutter analyze
flutter test
Pop-Location
aspire doctor
```

When distributed composition changed, additionally run an isolated stack, wait for the relevant resources, execute the named E2E journey, inspect resource logs and traces, then stop it cleanly. Never keep a manually started stack alive while the root E2E fixture owns its AppHost.

No phase uses `dotnet test --filter`, skipped tests, or a substitute partial suite as its completion gate.

### 10.3 CI order

1. formatting/comment/generated-source policy;
2. restore and build;
3. Reqnroll dry-run binding validation;
4. root .NET tests;
5. Flutter analyze/tests;
6. deterministic package reproducibility;
7. AppHost model tests;
8. full Aspire E2E;
9. package/security scan;
10. deployment smoke tests for release candidates.

Fast checks may run in parallel, but a release result is reported only after the complete ordered safety gate passes.

---

## 11. Data migration and compatibility policy

### 11.1 No migration for deleted generic runtime state by default

Inventory existing production data for legacy Synapse journals, generic neuron checkpoints, timelines, or UI surfaces. If no active user-owned behavior depends on it, archive/export for the agreed retention period and delete the runtime reader. Do not carry dead state schemas into the new rail.

If active data is found, create a one-time offline converter with:

- source/target counts and hashes;
- dry-run report;
- idempotent rerun;
- rollback/export;
- no dual-write runtime;
- deletion date for converter and legacy reader.

### 11.2 Synapse schema evolution

- stable alias independent of CLR name;
- integer schema version;
- additive optional evolution within a compatible version policy;
- explicit adapter for incompatible versions;
- dispatcher routes only versions declared by the active manifest;
- package activation fails if required adapters/capabilities are unavailable.

### 11.3 Package upgrade

1. install new version staged;
2. verify assembly, manifest, signature, tests, capabilities, state migrations;
3. stop new claims for the installation and let current lease finish or expire;
4. atomically activate version plus subscriptions/schedules;
5. load new host generation;
6. resume claims;
7. retain prior verified version for rollback;
8. never run two active versions against the same inbox item.

### 11.4 API compatibility

The SDK follows semantic versioning. Removing or changing a public member requires a new major SDK contract and an explicit package-host compatibility window. Cluster grain interfaces evolve additively where possible. Internal classes are not compatibility surfaces.

---

## 12. Risk register and stop conditions

| Risk | Early signal | Mitigation | Stop condition |
|---|---|---|---|
| Inbox state exceeds single-grain practical size | write latency/storage transactions spike near caps | lower caps, compact outcomes, shard by installation partition | p95 transition exceeds accepted SLO in Phase 1 spike |
| Dispatcher becomes hot | fan-out latency or activation contention rises | partition by scope/type hash, keep stable Synapse ID | 1,000-subscriber test misses accepted latency/cost |
| Package unload leaks | collectible load contexts remain alive | remove static/event/DI references, recycle host generation | repeated upgrade grows memory beyond budget |
| In-process package escapes restrictions | adversarial test accesses OS/network | deploy process/container isolation before untrusted packages | no enforceable production boundary |
| Duplicate external mutation | crash test repeats connector action | durable invocation/effect ledger and verification | any test produces two applied effects for one key |
| Gmail event loss | cursor commits before all pages | atomic page completion and cursor transition | recovery test loses message-added history |
| Aspire simplification breaks production identity | managed-identity profile cannot connect | retain focused production extension and parity test | no deployed environment proof |
| Workbench over-requests capability | generated manifest broader than intent | deterministic intent-to-capability mapping and diff | unexplained capability appears |
| UI cleanup removes dynamic registration | golden/E2E misses widget | fixture every allowed server surface before prune | production payload cannot render |
| Scope discovery becomes unbounded | host scans tenant grains | explicit configured scope in v1 | multi-tenant requirement arrives without authority design |
| Policy ambiguity permits apply | conflicting ceilings/revisions | pure lower-trust-wins calculation and immutable evidence | apply cannot cite one effective decision |
| Preview dependency remains accidental | alpha package has no measured need | delete or isolate behind accepted spike | stable alternative passes all tests |

Global stop conditions:

- current dirty Salesforce work cannot be isolated without data loss;
- root baseline tests fail for unrelated reasons and the failure is not understood;
- a phase requires a second mutation rail, second auth system, second UI authority, or direct connector access from package code;
- deletion threshold cannot be reached without removing a proven active user journey;
- a public durable identity/schema is about to be committed without duplicate, upgrade, rollback, and versioning tests;
- an automatic mutation cannot be tied to immutable policy and effect evidence.

---

## 13. Phase review checklist

Before starting a phase:

- [ ] Read this roadmap and the approved design.
- [ ] Confirm the previous phase gate from fresh command output.
- [ ] Re-run Context7/Aspire documentation lookup for APIs touched by the phase.
- [ ] Use CodeGraph to refresh caller/dependency impact.
- [ ] Check `git status` and preserve unrelated user changes.
- [ ] Write the phase-specific plan with exact current file paths and test names.
- [ ] Record target deletion/addition/project/package budgets.

Before completing a phase:

- [ ] Run the full root test suite with zero skips.
- [ ] Run Flutter formatting, analysis, and tests when client code or contracts changed.
- [ ] Run `aspire doctor` and the affected isolated distributed journey.
- [ ] Inspect logs, health, traces, and persisted recovery after restart.
- [ ] Re-run architecture and forbidden-dependency checks.
- [ ] Report gross/net lines, files, projects, packages, comments, tests, and elapsed cycle time.
- [ ] Delete temporary adapters, flags, scripts, QA artifacts, and stale docs owned by the phase.
- [ ] Perform the repository's five-step retrospective in order: requirements, deletion, simplification, acceleration, automation.

---

## 14. Recommended first execution plan

After Decision Gates 1 and 2 are accepted, write and execute only:

`docs/superpowers/plans/2026-07-13-behavior-convergence-phase-0-baseline.md`

That plan must contain Task 0.1 through Task 0.3 with the then-current dirty diff, exact baseline commands, and characterization tests. Do not begin Phase 1 deletion until the typed Salesforce slice has a verified owner and the baseline is reproducible.

The next plan is then:

`docs/superpowers/plans/2026-07-13-behavior-convergence-phase-1-deletion.md`

Phase 1 is successful only when the quantified deletion gate is crossed and the retained product paths pass. New behavior projects start in Phase 2, never earlier.
