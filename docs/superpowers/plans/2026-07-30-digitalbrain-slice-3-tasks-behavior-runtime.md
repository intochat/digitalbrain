# Slice 3: Tasks-Owned Isolated Behavior Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make TasksModule the only durable execution lifecycle for behaviors, with isolated workers, deterministic operation replay, user-action suspension, cooperative cancellation, and honest uncertain outcomes.

**Architecture:** BehaviorNeuron remains the owner-scoped revision/binding/control plane. Each activation starts an existing Tasks `ITask` neuron whose worker is the behavior execution neuron. The worker process receives a signed artifact, protected trigger reference, granted directed-synapse broker, deterministic operation history, time, and cancellation. No authored assembly enters a silo process.

**Tech Stack:** Orleans journals/reminders, DigitalBrain.Tasks, Behavior Host process, protected state, HTTP worker broker, xUnit v3, DigitalBrain.Testing.

## Global Constraints

- Add `TasksModule` to the DigitalBrain product composition; do not create `KernelTask` or `WorkId`.
- Reuse `TaskState`, `AttemptId`, blockers, retry, cancel, and `OutcomeUncertain`.
- Do not add a second task store or a parallel behavior execution ID as the durability key.
- Pass cancellation tokens through every new async boundary.
- Stop closes an activation gate; it does not rewrite or delete bindings.

---

## Task 1: Make behavior activation create an existing Task

**Files:**
- Modify: `src/modules/tasks/DigitalBrain.Modules.Tasks.Contracts/ITask.cs`
- Modify: `src/modules/tasks/DigitalBrain.Modules.Tasks.Contracts/TaskCommands.cs`
- Modify: `src/modules/tasks/DigitalBrain.Modules.Tasks/TaskNeuron.Commands.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Runtime/BehaviorNeuron.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Runtime/BehaviorNeuron.State.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors/Rail/BehaviorCommands.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors/Rail/BehaviorExecution.cs`
- Create: `src/core/behaviors/DigitalBrain.Behaviors/Rail/BehaviorActivationBindings.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Tests/BehaviorRailLifecycle.cs`
- Modify: `src/modules/tasks/DigitalBrain.Modules.Tasks.Tests/TaskLifecycle.Start.cs`

- [ ] CodeGraph `BehaviorNeuron.Execute`, `StartTask`, `IWorker`, `AcceptWorkerDispatch`, and all behavior execution identities.
- [ ] Add failing tests proving an explicit binding starts one owner-scoped Task pinned to behavior ID, revision, contract version, case ID, and protected payload reference.
- [ ] Prove union membership alone does not create a subscription.
- [ ] Prove duplicate delivery/recovery cannot create a second Task.
- [ ] Convert the touched Tasks client operation to directed request/result synapses while retaining only the migration seam defined by Slice 1.
- [ ] Run:

```powershell
dotnet test src/core/behaviors/DigitalBrain.Behaviors.Tests -c Release --filter "BehaviorRailLifecycle"
dotnet test src/modules/tasks/DigitalBrain.Modules.Tasks.Tests -c Release --filter "TaskLifecycle"
```

Expected RED.

- [ ] Implement activation-to-Task mapping with existing `TaskId`/neuron identity and `AttemptId`.
- [ ] Re-run focused tests.
- [ ] Commit: `feat: execute behaviors through tasks`

## Task 2: Broker deterministic directed operations

**Files:**
- Create: `src/core/security/DigitalBrain.Security/IProtectedPayloadStore.cs`
- Create: `src/core/security/DigitalBrain.Security/DurableProtectedPayloadStore.cs`
- Create: `src/core/behaviors/DigitalBrain.Behaviors/Rail/BehaviorOperation.cs`
- Create: `src/core/behaviors/DigitalBrain.Behaviors/Rail/BehaviorOperationResult.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors/Rail/BehaviorHostCommands.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Host/HostBehaviorContext.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Host/BehaviorHostEngine.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Runtime/HostedBehaviorExecutor.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Runtime/GrainBehaviorCapabilityResolver.cs`
- Create: `src/core/behaviors/DigitalBrain.Behaviors.Tests/BehaviorOperationReplay.cs`

- [ ] Add failing tests for operation identity `(Task neuron ID, AttemptId, sequence)`.
- [ ] Add failing tests proving trigger/provider payload bytes are encrypted in an owner-scoped protected store and only an opaque, expiring `ProtectedPayloadReference` crosses Task/worker/module contracts.
- [ ] Prove a completed operation returns its journaled result on replay without calling the provider twice.
- [ ] Prove a crash before dispatch is safely retried and a crash after an unprovable external effect produces `AttemptOutcomeUncertain`.
- [ ] Prove capability grants are exact target-neuron/request/result synapse edges; method aliases are not accepted.
- [ ] Run focused tests and capture RED.
- [ ] Implement the minimal broker/history in durable task/behavior facts. Store protected references or redacted summaries, never raw sensitive payloads.
- [ ] Re-run focused tests.
- [ ] Commit: `feat: replay behavior synapse operations`

## Task 3: Suspend and continue on module-owned user action

**Files:**
- Create: `src/modules/tasks/DigitalBrain.Modules.Tasks.Contracts/UserActionRequired.cs`
- Modify: `src/modules/tasks/DigitalBrain.Modules.Tasks.Contracts/TaskBlockers.cs`
- Modify: `src/modules/tasks/DigitalBrain.Modules.Tasks/TaskNeuron.Attempts.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Runtime/HostedBehaviorExecutor.cs`
- Modify: `src/core/mcp/DigitalBrain.Mcp/McpAuthorizationRail.cs`
- Modify: `src/core/mcp/DigitalBrain.Integrations.Tests/AuthorizationRail.cs`
- Modify: `src/core/mcp/DigitalBrain.Integrations.Tests/AccountEnrichmentBehaviorRail.cs`

- [ ] Add failing tests for minimal `UserActionRequired`: Task identity, module identity, display text, protected action reference, expiration.
- [ ] Prove Tasks enters waiting with one blocker, the worker may terminate, and continuation starts a fresh worker that replays to the interrupted operation.
- [ ] Prove completed authorization continues the same Task/attempt policy rather than creating a replacement Task.
- [ ] Prove secrets, tokens, authorization codes, and provider response content are absent from all journals.
- [ ] Implement the control synapse and map existing MCP authorization facts at the module boundary.
- [ ] Re-run:

```powershell
dotnet test src/core/mcp/DigitalBrain.Integrations.Tests -c Release --filter "Authorization|AccountEnrichment"
dotnet test src/modules/tasks/DigitalBrain.Modules.Tasks.Tests -c Release
```

- [ ] Commit: `feat: suspend tasks for module user actions`

## Task 4: Implement stop and cancellation without a second lifecycle

**Files:**
- Modify: `src/core/behaviors/DigitalBrain.Behaviors/Rail/BehaviorLifecycleFacts.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors/Rail/BehaviorSnapshot.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Runtime/BehaviorNeuron.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Runtime/BehaviorNeuron.State.cs`
- Modify: `src/modules/tasks/DigitalBrain.Modules.Tasks/TaskNeuron.Commands.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Tests/BehaviorRailLifecycle.cs`
- Modify: `src/modules/tasks/DigitalBrain.Modules.Tasks.Tests/TaskLifecycle.Cancel.cs`

- [ ] Add failing tests for `Running → Stopping → Stopped`.
- [ ] Assert Stop atomically closes the activation gate before cancellation requests.
- [ ] Assert bindings/revisions/source/scenarios remain unchanged.
- [ ] Assert active operations observe cancellation at safe synapse boundaries and already-started ambiguous effects become uncertain.
- [ ] Implement with linked attempt/request tokens; do not store `CancellationToken` or a token source in durable state.
- [ ] Re-run focused tests.
- [ ] Commit: `feat: stop behavior tasks cooperatively`

## Task 5: Remove production in-silo authored-code paths

**Files:**
- Delete when proven unused: `src/core/behaviors/DigitalBrain.Behaviors.Runtime/InProcessBehaviorExecutor.cs`
- Delete or test-scope when proven unused: `src/core/behaviors/DigitalBrain.Behaviors.Tests/InProcessBehaviorHostGatewayModule.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Runtime/BehaviorsModule.Runtime.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Host/BehaviorProgramLoader.cs`
- Create: `os/tests/DigitalBrain.OS.Host.Tests/AuthoredAssemblyIsolation.cs`
- Integrator only: `os/DigitalBrain.OS.Host/DigitalBrain.OS.Host.csproj`
- Integrator only: `os/DigitalBrain.OS.AppHost/AppHost.cs`
- Integrator only: `os/DigitalBrain.OS.AppHost/DigitalBrain.OS.AppHost.csproj`
- Integrator only: `DigitalBrain.slnx`

- [ ] Add a failing process-boundary test proving the silo cannot resolve/load an authored behavior assembly.
- [ ] Prove the Behavior Host alone loads the pinned artifact and unloads it after execution.
- [ ] Return the exact Tasks/Behavior Host composition changes needed by the Wave 2 integrator; do not edit shared AppHost/Host/solution files in the Slice 3 worktree.
- [ ] Delete in-process production paths only after CodeGraph shows all production callers migrated.
- [ ] Run OS host and behavior tests.
- [ ] Commit: `refactor: isolate all authored behavior code`

## Slice Verification

- [ ] `dotnet test src/modules/tasks/DigitalBrain.Modules.Tasks.Tests -c Release`
- [ ] `dotnet test src/core/behaviors/DigitalBrain.Behaviors.Tests -c Release`
- [ ] `dotnet test src/core/mcp/DigitalBrain.Integrations.Tests -c Release`
- [ ] `dotnet test os/tests/DigitalBrain.OS.Host.Tests -c Release`
- [ ] `dotnet build DigitalBrain.slnx -c Release`
- [ ] CodeGraph proves no silo project references or loads authored assemblies.
- [ ] Slice-local tests prove behavior/Task journals, suspension, continuation, replay, cancellation, uncertain outcomes, and process-boundary isolation.
- [ ] Defer DigitalBrain MCP behavior-rail evidence and Aspire MCP behavior-host/silo traces until the Wave 2 composition integrator has wired Tasks and Behavior Host into the product. Repeat the complete live proof in Slice 8.
- [ ] Return the standard handoff.
