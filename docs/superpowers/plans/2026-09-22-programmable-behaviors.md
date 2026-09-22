# Stage 2: Programmable C# Behaviors Implementation Plan

Implementation and actual verification: [stage-2 delivery record](2026-09-22-programmable-behaviors-verification.md).

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans for inline implementation, or superpowers:subagent-driven-development if the user chooses delegation. Steps use checkbox syntax for tracking.

**Goal:** Let an assistant author, test, activate, inspect, stop, update, and roll back single-source C# behaviors through typed neurons and tools.

**Architecture:** Coding owns mutable drafts and immutable verified artifacts. A Behavior module owns durable deployment intent and a supervisor runs artifacts in child processes using the existing behavior runtime. The stage-1 `IAgent` coordinates authoring through tools; it does not become a compiler or process manager.

**Tech Stack:** Existing .NET 11/C#, Orleans, Roslyn, Microsoft.Testing.Platform/xUnit, DigitalBrain testing harness, stage-1 AI contracts, local Windows process supervision, existing Aspire E2E host.

**Spec:** [Programmable behaviors design](../specs/2026-09-22-programmable-behaviors-design.md). Read the spec first; API names below are proposed additions unless explicitly identified as existing.

## Global constraints

- Preserve existing `ICodeWorkspace`, `IChangeSet`, `DigitalBrain.Core.IBehavior`, and stage-1 `IAgent` contracts and behavior unless a migration is explicitly covered here.
- One UTF-8 C# behavior source file; separate test source and host-owned build metadata. Production never starts the simulation harness.
- Generated code, builds, and tests execute outside the silo. Local execution is trusted code, not a security sandbox.
- .NET target `net11.0`; use the repository's SDK selection (`11.0.100-rc.1.26425.128` currently) and record the actual resolved SDK in artifacts. No independent package version upgrades.
- Orleans contracts use explicit aliases/IDs and serialized immutable DTOs; no SDK/Roslyn/OS handles in contracts. Follow the repository's existing flat namespace and formatting conventions.
- Windows is the initial supported supervisor platform. Fail clearly where process containment is unavailable. Multi-host active ownership is unsupported.
- Mutations use expected revision and operation ID. Persist authoritative results before publishing live signals. Never replay external effects to recover lost notifications.
- Existing signals are live and can have gaps. Stop-old/start-new updates are intentional; no claims of uninterrupted or exactly-once processing.
- Defaults: source/tests 128 KiB each; config 32 KiB; 32 modules; 100 diagnostics; 64 KiB log entry; 10 MiB retained logs; two concurrent checks; 120-second build and test deadlines; 30-second readiness; 15-second graceful stop; 5-second heartbeat/20-second loss; three crash retries at 1/2/4 seconds.
- Stage-1 edits are currently uncommitted. Record the starting tree and preserve them. Do not use `git add .`, reset, clean, or create a commit containing unrelated work. Commit steps below mean stage only that task's explicit paths when an implementation checkpoint is appropriate.

## Review focus

1. Same operation ID with changed source/config must conflict; retry with identical input must not duplicate work (Tasks 1, 3, 6).
2. Passing source with altered binary, SDK, contract assembly, tests, or validator must not activate using stale evidence (Tasks 2, 3, 6).
3. Stop/update racing with a late ready callback or host crash must never leave two owned generations active (Tasks 5, 6, 8).
4. Compiler/test subprocesses that ignore cancellation or emit unbounded output must terminate without leaking a child tree or exhausting memory (Tasks 3, 5, 8).
5. An agent-generated program that compiles but subscribes to nothing, loses a subscription, or has zero tests must not appear validated and ready (Tasks 3, 4, 8).

## File map and dependency order

| Area | Paths | Responsibility |
|---|---|---|
| Coding additions | `src/Modules/Coding/Contracts/Drafts/`, `Coding/Drafts/`, `Coding/Artifacts/`, `Coding/Validation/` | Source revisions, check operations, sealed outputs |
| New lifecycle module | `src/Modules/Behavior/Contracts/`, `Behavior/`, `Aspire.Hosting/`, `Tests/Unit/`, `Tests/E2E/` | Program neurons, durable index, supervisor, host wiring |
| Existing worker library | `src/Modules/DigitalBrain/BehaviorRuntime/` | `BehaviorApp`, scoped brain/readiness, supervised execution mode |
| App composition | `src/Applications/IntoChat/IntoChat/Behavior/` | Scoped tools, authoring service, lifecycle HTTP endpoints |
| Examples | `src/Behaviors/` | Executable one-source example and domain tests |

Tasks 1 → 2 → 3 establish the artifact pipeline. Task 4 builds on Task 1. Task 5 uses Tasks 2 and 4; Task 6 uses Task 5. Task 7 joins Tasks 3 and 6 to AI. Task 8 verifies the entire path. Keep the first vertical slice Timer-to-Flutter; expand examples only after it works.

## Task 1: Define contracts, invariants, and module registration

**Files**
- Create `src/Modules/Coding/Contracts/Drafts/ICodeDraft.cs`, `CodeDraftContracts.cs`, `Signals/CodeDraftSignals.cs`.
- Create `src/Modules/Coding/Contracts/Artifacts/CodeArtifactRef.cs`.
- Create `src/Modules/Behavior/Contracts/DigitalBrain.Modules.Behavior.Contracts.csproj`, `IBehaviorProgram.cs`, `BehaviorContracts.cs`, `Signals/BehaviorSignals.cs`.
- Create `src/Modules/Behavior/Behavior/DigitalBrain.Modules.Behavior.csproj`, `BehaviorModule.cs`, `Configuration/BehaviorOptions.cs`.
- Create `src/Modules/Behavior/Tests/Unit/DigitalBrain.Modules.Behavior.Tests.Unit.csproj`, `Contracts/ContractFacts.cs`.
- Create `src/Modules/Coding/Coding/Drafts/DraftRules.cs`, `Tests/Unit/Drafts/DraftRulesFacts.cs`.
- Modify `DigitalBrain.slnx` to include new projects without removing stage-1 additions.

**Interfaces:** Implement the two public neuron interfaces and DTO fields/statuses in the spec. Use these command signatures to freeze cross-task naming:

```csharp
public sealed record SaveCodeDraft(long ExpectedRevision, Guid OperationId,
    string Source, string Tests, IReadOnlyList<string> ModuleIds);
public sealed record CheckCodeDraft(long Revision, Guid OperationId);
public sealed record CodeArtifactRef(string Id, string SourceHash, string EnvironmentHash);
public sealed record DeployBehavior(long ExpectedRevision, Guid OperationId,
    CodeArtifactRef Artifact, string ConfigurationJson, Guid? AgentRunId = null);
public sealed record ChangeBehaviorState(long ExpectedRevision, Guid OperationId);
public sealed record RollbackBehavior(long ExpectedRevision, Guid OperationId, long DeploymentRevision);
```

Each declaration goes in its named contract area with Orleans attributes. `BehaviorSnapshot.Revision` is the command-state CAS revision; `DesiredDeploymentRevision`/`ActiveDeploymentRevision` identify immutable deployments. Do not overload these meanings. `CodeCheckSnapshot.Artifact` is nullable and only populated after Passed. Use UTC `DateTimeOffset` and serializable diagnostic records with code/severity/message/file/line/column.

- [ ] Add serializer round-trip tests for every new command, snapshot, enum, artifact reference, and signal using the existing Orleans test harness. Test the empty state explicitly.
- [ ] Add `DraftRules.Validate(SaveCodeDraft request)` with byte limits, unique bounded module IDs, nonempty source/tests, nonnegative revision and nonempty operation ID. Reject managed-source build directives through syntax-aware inspection before build.

```csharp
[Fact]
public void ManagedDraftCannotImportAnArbitraryProject()
{
    var request = new SaveCodeDraft(0, Guid.NewGuid(),
        "#:project ../../private.csproj\nConsole.WriteLine(1);", "// tests", []);
    Assert.Throws<ArgumentException>(() => DraftRules.Validate(request));
}
```

- [ ] Run `dotnet test --project src/Modules/Coding/Tests/Unit/DigitalBrain.Modules.Coding.Tests.Unit.csproj`; expect the new missing-type/behavior test to fail before implementation.
- [ ] Implement contract attributes, validation, options validation, and module scaffolding. Registration must not launch a worker or require a configured solution workspace.
- [ ] Run Coding and new Behavior unit projects; check serialized values survive deactivation/storage round trips where applicable. Commit the explicit new contracts/scaffolding paths with `feat: define code draft and behavior lifecycle contracts`.

## Task 2: Seal verified build artifacts

**Files**
- Create `src/Modules/Coding/Coding/Artifacts/ArtifactManifest.cs`, `ArtifactStore.cs`, `ArtifactHashing.cs`, `BuildEnvironment.cs`.
- Create `src/Modules/Coding/Tests/Unit/Artifacts/ArtifactStoreFacts.cs`.
- Modify `src/Modules/Coding/Coding/Configuration/CodingModuleOptions.cs` and `CodingModule.cs` for a private artifact/build root and pinned environment registration.

**Interfaces**

```csharp
public interface ICodeArtifactStore
{
    Task<CodeArtifactRef> SealAsync(VerifiedBuild build, CancellationToken cancellationToken);
    Task<VerifiedArtifact> OpenVerifiedAsync(CodeArtifactRef reference, CancellationToken cancellationToken);
}
```

Host-only records: `VerifiedBuild` contains source/test UTF-8 bytes, environment manifest, candidate payload directory, and a passing `CodeTestReport`; `VerifiedArtifact` contains reference, parsed manifest, private launch directory and entry assembly. `CodeTestReport` contains discovered/passed/failed/skipped counts and report hash. Neither record is exposed through Orleans. A host-controlled validation service constructs verified builds; model input cannot do so.

- [ ] Add a disposable `ArtifactStoreFixture` in the test file. It creates a private temp root, a minimal payload, and a passing report; exposes `SealValidAsync()`, `TamperPayloadAsync(reference)` and `Store`.

```csharp
[Fact]
public async Task ModifiedPayloadCannotBeOpenedForExecution()
{
    await using var fixture = new ArtifactStoreFixture();
    var artifact = await fixture.SealValidAsync();
    await fixture.TamperPayloadAsync(artifact);
    await Assert.ThrowsAsync<InvalidDataException>(
        () => fixture.Store.OpenVerifiedAsync(artifact, CancellationToken.None));
}
```

- [ ] Run the Coding unit project and confirm failure. Add tests for identical-content deduplication, changed tests/environment hashes, interrupted sealing, missing payload, traversal/rooted paths, reparse-point escapes, and rejected zero-test reports.
- [ ] Implement SHA-256 manifests with deterministic serialization, normalized relative payload paths, atomic temporary-directory promotion and private-root checks. Keep exact source bytes; no normalization after hashing. Validate hashes and current host compatibility every time `OpenVerifiedAsync` is called.
- [ ] Do not add automatic garbage collection yet: retain artifacts and deployment references, with explicit disk quota failure before writing. This avoids deleting an artifact still needed for rollback.
- [ ] Run the tests and `git diff --check`; commit the explicit artifact/config changes with `feat: store immutable verified behavior artifacts`.

## Task 3: Implement draft revisions and the real build/test pipeline

**Files**
- Create `src/Modules/Coding/Coding/Drafts/CodeDraftNeuron.cs`, `CodeDraftState.cs`, `CodeCheckCoordinator.cs`, `CodeCheckIndex.cs`.
- Create `src/Modules/Coding/Coding/Validation/BehaviorBuildTemplate.cs`, `CodeValidationService.cs`, `CodeTestReportReader.cs`, `ContractCatalog.cs`.
- Modify `CodingModule.cs`, `ProcessRunner.cs`, `DotnetRunner.cs` and options only where shared process/test behavior needs correction.
- Create `src/Modules/Coding/Tests/Unit/Drafts/CodeDraftFacts.cs`, `Validation/CodeValidationFacts.cs`, `Validation/TestReportFacts.cs`.

**Interfaces:** `CodeValidationService.ValidateAsync(CodeDraftSnapshot snapshot, Guid operationId, CancellationToken cancellationToken) -> Task<CodeCheckSnapshot>` consumes Task 2's store. `ContractCatalog.Read(IReadOnlyList<string> moduleIds) -> ContractCatalogSnapshot` returns approved module IDs, contract fingerprint, relevant interface/signal documentation and examples. `CodeCheckCoordinator` owns a bounded two-slot queue; the persistent index enumerates incomplete operations on startup.

- [ ] Add `CodePipelineFixture.StartAsync()` implementing `IAsyncDisposable`, with a real temp build root, Coding module, pinned test references and no production credentials. It exposes `Draft`, `ValidSource`, `ValidTests`, and `WaitForCheckAsync(Guid)` with a bounded deadline. Valid source/test fixtures are checked-in files under `Validation/Fixtures/`, excluded from the unit project's default compile glob.

```csharp
[Fact]
public async Task CheckCannotValidateAnOldRevisionAsTheCurrentSource()
{
    await using var fixture = await CodePipelineFixture.StartAsync();
    var first = await fixture.Draft.Save(new(0, Guid.NewGuid(), fixture.ValidSource, fixture.ValidTests, []));
    await fixture.Draft.Save(new(first.Revision, Guid.NewGuid(), "invalid C#", fixture.ValidTests, []));
    await Assert.ThrowsAsync<InvalidOperationException>(
        () => fixture.Draft.Check(new(first.Revision, Guid.NewGuid())));
}
```

- [ ] Run Coding unit tests; confirm the new behavior fails. Add tests for concurrent saves, operation-ID replay/conflict, save while check is running (the check stays pinned), queued/running cancellation, restart → Interrupted, rejected unknown modules, zero/skipped tests, unreadable report, and compile diagnostics with source line numbers.
- [ ] Implement immutable draft history and operation ledger. Check validates requested current revision, persists Queued and enqueues once; later edits never retarget it. Use stable operation IDs plus input hashes. Bound retained operation history explicitly and reject expired replay IDs rather than treating them as new work.
- [ ] Generate a private host-owned executable project and test project. Build the behavior once; reference its exact output from tests. Supply only approved module/SDK/test assemblies, fixed package sources and pinned dependencies. Exclude ambient MSBuild imports and arbitrary draft directives. Use a separate private directory per operation.
- [ ] Reuse process argument-list execution and bounded output, but add allowlisted environment and child-tree containment. Run tests through the repository's Microsoft.Testing.Platform mode. Parse a machine-readable result; require all tests to pass, at least one user test, and host conformance checks. Do not infer success only from a text summary or exit code.
- [ ] Seal only successful output with Task 2; persist Passed and artifact reference before notifying. Persist diagnostics/cancellation/failure on other exits. Host crash marks incomplete work Interrupted; it does not resume tests automatically.
- [ ] Add real compiler tests for success and syntax error, plus a hanging-test fixture and a high-output fixture. Verify their children exit and output caps hold. Run Coding unit tests and commit explicit pipeline files with `feat: validate versioned C# drafts into executable artifacts`.

## Task 4: Extract the single-file behavior SDK and readiness protocol

**Files**
- Create `src/Modules/DigitalBrain/BehaviorRuntime/BehaviorApp.cs`, `BehaviorExecutionOptions.cs`, `BehaviorControlChannel.cs`.
- Modify `BehaviorHosting.cs`, `BehaviorScopedBrain.cs`, `BehaviorReadiness.cs` in that directory.
- Modify `src/Behaviors/timer-report.cs` to demonstrate the new bootstrap without changing timer semantics.
- Extend `src/Behaviors/Tests/Unit/BehaviorReadinessFacts.cs`; create `BehaviorAppFacts.cs` and `BehaviorSubscriptionFacts.cs` beside it.

**Interfaces**

```csharp
public static Task RunAsync<TBehavior>(string[] args,
    Func<IDigitalBrain, IReadOnlyList<SubscriptionRequirement>> requirements)
    where TBehavior : class, IBehavior;
```

This is `BehaviorApp.RunAsync`. Resolve production brain connection from trusted host configuration, construct `TBehavior` with the scoped brain, and propagate shutdown cancellation. `BehaviorExecutionOptions` distinguishes existing static-host retries from supervisor-owned retries. Control records carry protocol version, generation ID, monotonically increasing sequence, event kind, readiness and bounded error text. Use a private authenticated parent/child channel, not stdout parsing.

- [ ] Extend existing readiness tests with the following and add integration tests for normal completion, source reactivation, buffer overflow, startup cancellation, and a required subscription closing while user code is otherwise blocked.

```csharp
[Fact]
public void ClosedRequiredSubscriptionWithdrawsReadiness()
{
    var readiness = new BehaviorReadiness();
    var generation = readiness.Begin("timer", [new("timer/tea", typeof(string))]);
    readiness.SubscriptionReady(generation, "timer/tea", typeof(string));
    Assert.True(readiness.IsReady);
    readiness.SubscriptionClosed(generation, "timer/tea", typeof(string));
    Assert.False(readiness.IsReady);
}
```

- [ ] Run `dotnet test --project src/Behaviors/Tests/Unit/DigitalBrain.Behaviors.Tests.Unit.csproj`. Some existing readiness tests already pass; ensure the new hosted-completion/subscription-loss tests fail before modifying lifecycle behavior.
- [ ] Extract the production client setup from the current timer app. Require deployed gateways/cluster/service identity; retain explicit local-development configuration. Do not silently connect a deployed worker to localhost.
- [ ] In supervised mode, await initial connection, report actual subscription readiness, observe required subscription failures, cancel execution on parent-channel loss, and always end the generation/dispose subscriptions in `finally`. Normal completion reports Completed, never healthy-running. Disable internal retries in supervised mode.
- [ ] Keep `IBehavior.RunAsync` unchanged. Test the same compiled class with `UnitTest` and production bootstrap with a separate-process fixture. Run existing behavior tests and commit explicit SDK/example files with `feat: host single-source behaviors with supervised readiness`.

## Task 5: Build the local worker supervisor

**Files**
- Create `src/Modules/Behavior/Behavior/Execution/IBehaviorExecutor.cs`, `LocalBehaviorExecutor.cs`, `BehaviorSupervisor.cs`, `BehaviorLogStore.cs`, `BehaviorControlServer.cs`.
- Create `src/Shared/ProcessExecution/WindowsProcessContainment.cs`; link this internal OS-only source from the Coding and Behavior implementation projects. Coding owns its shared contract tests; Behavior adds worker-lifecycle tests.
- Create `src/Modules/Behavior/Tests/Unit/Execution/SupervisorFacts.cs`, `ProcessContainmentFacts.cs`, `LogStoreFacts.cs`, and `Fixtures/WorkerFixture/WorkerFixture.csproj` with `Program.cs`.

**Interfaces**

```csharp
public interface IBehaviorExecutor
{
    Task<BehaviorExecution> StartAsync(BehaviorLaunch launch, CancellationToken cancellationToken);
    Task StopAsync(Guid generationId, CancellationToken cancellationToken);
}
```

`BehaviorLaunch` is host-only: program ID, generation ID, verified artifact, configuration, connection settings and runtime limits. `BehaviorExecution` carries generation ID and `Task<BehaviorExit> Completion`; `BehaviorExit` carries exit kind/code/error. `BehaviorSupervisor.ReconcileAsync(BehaviorSnapshot desired, CancellationToken)` serializes transitions per program. Executor events are authenticated and checked against the currently owned generation before updating state.

- [ ] Create `SupervisorFixture.StartAsync()` with fake time and a controllable executor for unit tests. Expose `DeployAsync(artifactId)`, `StopAsync()`, `ReportReadyAsync(generationId)`, `Snapshot`, and `ActiveProcessCount`. These call the real supervisor; the fake replaces OS launching only.

```csharp
[Fact]
public async Task LateReadyCannotReviveStoppedGeneration()
{
    await using var fixture = await SupervisorFixture.StartAsync();
    var generation = await fixture.DeployAsync("artifact-a");
    await fixture.StopAsync();
    await fixture.ReportReadyAsync(generation);
    Assert.False(fixture.Snapshot.Ready);
    Assert.Equal(0, fixture.ActiveProcessCount);
}
```

- [ ] Run Behavior unit tests and confirm failure. Add stop-during-start, failed-old-process-stop preventing replacement, readiness timeout, stale heartbeat, heartbeat loss, normal completion without restart, retry exhaustion, and log truncation tests.
- [ ] Implement process launch containment before user code runs; on Windows attach the suspended process to a kill-on-close job before resume. Validate attachment and fail closed. Keep executable paths internal and arguments structured. Extract the Task 3 containment implementation into `src/Shared/ProcessExecution/WindowsProcessContainment.cs`, linked into both implementation assemblies with `<Compile Include="../../../Shared/ProcessExecution/WindowsProcessContainment.cs" Link="Execution/WindowsProcessContainment.cs" />` in both projects. Keep it internal; Coding must not reference Behavior lifecycle.
- [ ] Implement private control-channel handshake, environment allowlist, nonsecret config delivery, bounded stdout/stderr capture, graceful stop followed by force termination, exit confirmation, and private-root exclusive supervisor ownership. Do not kill by PID alone after restart: verify ownership identity and containment.
- [ ] Add real worker fixtures for ignored cancellation, spawned grandchildren, crash, output flood and forged/old control messages. Validate every child exits on parent loss. Use bounded waits and condition/event assertions, not fixed sleeps.
- [ ] Run unit tests plus real-process containment tests on Windows. Commit explicit executor/runtime changes with `feat: supervise behavior processes and bound their resources`.

## Task 6: Implement durable behavior neurons and hosting

**Files**
- Create `src/Modules/Behavior/Behavior/Programs/BehaviorProgramNeuron.cs`, `BehaviorProgramState.cs`, `BehaviorProgramIndex.cs`, `BehaviorReconciler.cs`, `BehaviorCommandRules.cs`.
- Create `src/Modules/Behavior/Aspire.Hosting/DigitalBrain.Modules.Behavior.Aspire.Hosting.csproj`, `BehaviorHostingExtensions.cs`.
- Modify `BehaviorModule.cs`, options and `DigitalBrain.slnx`.
- Create `src/Modules/Behavior/Tests/Unit/Programs/BehaviorProgramFacts.cs`, `BehaviorRecoveryFacts.cs`.

**Interfaces:** Implement all `IBehaviorProgram` methods from Task 1. `BehaviorProgramIndex` persists discoverable program IDs and pending intents; registration is idempotent and reconciles partial writes. `BehaviorReconciler` enumerates the index on startup and invokes Task 5. Use a recoverable registration protocol: register the ID before accepting the first program mutation; an empty indexed entry is harmless. Do not rely on observing a live signal to discover deployments.

- [ ] Add `BehaviorProgramFixture.StartAsync()` with a real test silo, persistent fake executor and artifact store. Expose `Program`, `Artifact`, `ReadinessAsync()`, `RestartSiloAsync()` and `ActiveProcessCount`.

```csharp
[Fact]
public async Task StoppedProgramRemainsStoppedAfterRecovery()
{
    await using var fixture = await BehaviorProgramFixture.StartAsync();
    var deployed = await fixture.Program.Deploy(new(0, Guid.NewGuid(), fixture.Artifact, "{}"));
    await fixture.Program.Stop(new(deployed.Revision, Guid.NewGuid()));
    await fixture.RestartSiloAsync();
    var state = await fixture.Program.Read();
    Assert.Equal(BehaviorDesiredState.Stopped, state.DesiredState);
    Assert.Equal(0, fixture.ActiveProcessCount);
}
```

- [ ] Run Behavior unit tests; confirm failure. Cover CAS conflicts, operation replay/different payload conflict, mutation cancellation at persistence boundary, corrupted artifact, incompatible environment, update/stop races, stale callbacks, storage failure, notification failure after commit, recovery after process launch but before state update, and rollback config/artifact pairing.
- [ ] Implement persist-intent-then-reconcile. Keep read/cancel/control access responsive without allowing simultaneous conflicting mutations. Store deployment history separately from execution generation and state revision. Commands return promptly; callers observe readiness through Read/signals.
- [ ] Verify artifact before disrupting the current process. During replacement stop/confirm old exit before new launch. Retain prior deployment on failure; explicit rollback creates a new deployment revision using the retained verified artifact/config. Reject missing/corrupt rollback artifacts.
- [ ] Wire local state/artifact volumes, explicit single-supervisor ownership, cluster connection and limits through module configuration and Aspire hosting. Use private configuration for secrets. A deactivated grain must not terminate a healthy worker; supervisor restart follows the recovery rules in the spec.
- [ ] Run Behavior unit/recovery tests and a solution build. Commit explicit lifecycle/hosting files with `feat: persist and reconcile behavior deployments`.

## Task 7: Connect authoring agents, native/MCP tools and HTTP

**Files**
- Create `src/Applications/IntoChat/IntoChat/Behavior/BehaviorAuthoringService.cs`, `BehaviorAuthoringContracts.cs`, `BehaviorAgentTools.cs`, `BehaviorToolService.cs`, `BehaviorToolScope.cs`.
- Modify existing `BehaviorEndpoints.cs`, `Program.cs`, and the actual existing agent-tool registration point discovered during implementation.
- Create `src/Applications/IntoChat/Tests/Unit/Behavior/BehaviorAuthoringFacts.cs`, `BehaviorToolFacts.cs` in the existing test project.
- Modify project references for Coding/Behavior contracts and services. Do not add a reverse dependency from AI.

**Interfaces**

```csharp
public sealed record AuthorBehaviorRequest(string DraftId, string Intent, bool Activate);
public sealed record AuthorBehaviorResult(string DraftId, long Revision,
    CodeArtifactRef? Artifact, string Status, string? Error);
public interface IBehaviorAuthoringService
{
    Task<AuthorBehaviorResult> AuthorAsync(AuthorBehaviorRequest request, CancellationToken cancellationToken);
}
```

The service is scoped to trusted workspace identity; it resolves the configured `IAgent` and neuron keys internally. `Activate` is accepted only when the application's request authorization allows execution. It is not an LLM-granted permission.

Tool names: `code_contracts`, `code_draft_read`, `code_draft_save`, `code_draft_check`, `code_check_read`, `code_check_cancel`, `behavior_read`, `behavior_deploy`, `behavior_start`, `behavior_stop`, `behavior_rollback`, `behavior_logs`. Each maps directly to catalog/neuron methods; native and MCP adapters share `BehaviorToolService`. Return typed, bounded results with operation IDs and diagnostics. The model cannot supply arbitrary paths, credentials or another workspace scope.

- [ ] Create `AuthoringFixture.StartAsync()` with the real service/tool adapters and scripted `IAgent` responses. Expose `Service`, `ModelCalls`, `DeploymentCalls`; script invalid-first/valid-second candidates and check results without a cloud model.

```csharp
[Fact]
public async Task DraftOnlyAuthoringDoesNotDeploy()
{
    await using var fixture = await AuthoringFixture.StartAsync();
    var result = await fixture.Service.AuthorAsync(
        new("timer-report", "Show timer ticks", false), CancellationToken.None);
    Assert.NotNull(result.Artifact);
    Assert.Equal(0, fixture.DeploymentCalls);
}
```

- [ ] Run the existing IntoChat unit project and confirm failure. Test repair success, three failed candidates, deadline/cancellation, model refusal, stale revision, unsupported model capability, scope spoofing, explicit authorized activation, and identical native/MCP behavior.
- [ ] Implement at most three candidate checks and a ten-minute total deadline. Feed compiler/test diagnostics as data into the existing agent, use current contract catalog, and preserve agent/draft/check/deployment correlation. Do not infer a passing artifact from assistant prose. Stop on cancellation and cancel outstanding check work.
- [ ] Expose scoped draft/check and behavior lifecycle endpoints using existing application authentication patterns. HTTP mutation bodies carry expected revision and operation ID; Read/source/log endpoints never expose secret config. Return accepted/pending state for asynchronous work. Register tool sets without changing unrelated workspace artifact tools.
- [ ] Run IntoChat unit tests and AI unit tests to catch registration/tool-loop regressions. Commit explicit integration files with `feat: author and manage C# behaviors through agents`.

## Task 8: Prove the full system with a deterministic end-to-end slice

**Files**
- Create `src/Modules/Behavior/Tests/E2E/DigitalBrain.Modules.Behavior.Tests.E2E.csproj`, `ProgrammableBehaviorFacts.cs`, `Fixtures/BehaviorE2EFixture.cs`.
- Add checked-in source/test fixtures under `src/Modules/Behavior/Tests/E2E/Fixtures/Programs/` for valid Timer-to-Flutter, updated output, syntax failure, readiness failure and crashing behavior. Exclude fixture source from the host project's default compilation.
- Add `src/Modules/Behavior/README.md`; update `src/Modules/AI/README.md` with authoring composition and `src/Behaviors/timer-report.cs` documentation/example usage.
- Modify `DigitalBrain.slnx` to include E2E project.

**Interfaces:** `BehaviorE2EFixture.StartAsync()` starts a separate Aspire silo with Coding, Behavior, Time, Flutter and a scripted model adapter. It exposes `AuthorAsync(intent, activate)`, `WaitUntilReadyAsync()`, `EmitTimerTickAsync()`, `ReadOutputAsync()`, `StopAsync()`, `RestartHostAsync()` and async disposal. These helpers use real endpoint/neuron calls, not in-process DI callbacks into the silo. `AuthorAsync` returns `AuthorBehaviorResult`; output is the observable Flutter text value.

- [ ] Add the baseline test before finishing E2E wiring:

```csharp
[Fact]
public async Task AgentGeneratedArtifactRunsInASeparateWorker()
{
    await using var fixture = await BehaviorE2EFixture.StartAsync();
    var result = await fixture.AuthorAsync("Show timer ticks in the workspace", activate: true);
    Assert.NotNull(result.Artifact);
    await fixture.WaitUntilReadyAsync();
    await fixture.EmitTimerTickAsync();
    Assert.Equal("tick received", await fixture.ReadOutputAsync());
}
```

- [ ] Run the new E2E project and confirm it fails before the pipeline is fully connected. Add assertions proving the worker has a different PID from the silo and that the artifact ID executed equals the one returned by validation.
- [ ] Extend the scenario: edit source → validation → new artifact → replace → exactly one effect for a deliberately emitted post-readiness test event; syntax/test/readiness failure leaves an inspectable failed revision; stop prevents later effects; rollback restores the prior artifact/config; stopped state survives restart; running desired state recovers with a new generation. Do not interpret this controlled event assertion as a general exactly-once guarantee.
- [ ] Add crash-after-launch, parent-loss child cleanup, model cancellation, test timeout, corrupted artifact, and missed-signal/overflow scenarios. Explicitly wait for subscription readiness before emitting normal test events. Test gaps separately and assert they are surfaced, not silently replayed.
- [ ] Keep live provider/mail credentials out of required tests. Document optional live-model smoke testing separately. If adding an invoice example, compile it against actual installed mail/notification contracts and fake outbound effects.
- [ ] Run the final verification commands below, record actual results, review the complete diff, and commit only explicit stage-2 paths with `test: verify programmable behavior lifecycle end to end`.

## Final verification and delivery

Run these commands once all tasks are integrated; do not claim these checks ran during planning:

```powershell
dotnet build DigitalBrain.slnx
dotnet test --project src/Modules/Coding/Tests/Unit/DigitalBrain.Modules.Coding.Tests.Unit.csproj
dotnet test --project src/Modules/Behavior/Tests/Unit/DigitalBrain.Modules.Behavior.Tests.Unit.csproj
dotnet test --project src/Behaviors/Tests/Unit/DigitalBrain.Behaviors.Tests.Unit.csproj
dotnet test --project src/Modules/AI/Tests/Unit/DigitalBrain.Modules.AI.Tests.Unit.csproj
dotnet test --project src/Applications/IntoChat/Tests/Unit/IntoChat.Tests.Unit.csproj
dotnet test --project src/Modules/DigitalBrain/Tests/Unit/DigitalBrain.Runtime.Tests.Unit.csproj
dotnet test --project src/Modules/AI/Tests/E2E/DigitalBrain.Modules.AI.Tests.E2E.csproj
dotnet test --project src/Modules/Behavior/Tests/E2E/DigitalBrain.Modules.Behavior.Tests.E2E.csproj
git diff --check
```

The IntoChat and DigitalBrain runtime unit projects and AI E2E project are included because tool registration, hosting and process lifecycle affect their integrations. Aspire E2E runs require the same local Docker/Azurite prerequisites as the stage-1 E2E harness.

Delivery must include a compiling one-source example, its tests, documented local execution trust boundary, command/signal reference, operation/deployment recovery rules, artifact retention and disk-limit behavior, and actual verification results. The first implementation milestone is Tasks 1–4 with a manually authored validated artifact; the second is Tasks 5–6 with manual lifecycle control; the final is Tasks 7–8 with agent-driven authoring and full E2E proof.

## Plan self-review

- Spec coverage: artifact identity/checking → Tasks 1–3; SDK/subscription readiness → Task 4; process ownership/limits → Task 5; durable lifecycle/recovery/rollback → Task 6; agent/tools/scope → Task 7; whole-flow evidence/examples → Task 8.
- Existing solution editing and static behaviors remain supported. JSON graph migration, scheduler duplication and new provider abstractions are deliberately absent.
- Distinct identities are retained throughout: draft revision, check operation ID, artifact ID, command-state revision, deployment revision, worker generation ID, and agent run ID.
- Each review-focus failure has an explicit owning test task. Unit fakes test coordination; real compiler/process/E2E fixtures test the boundaries that fakes cannot prove.
- This document proposes implementation and test targets. It does not report stage-2 implementation or test completion.
