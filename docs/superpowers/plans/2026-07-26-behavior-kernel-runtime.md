# Behavior Kernel Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make every installed Behavior a real journaled neuron with durable event/intent execution, exact revision selection, private state, and replay-safe module capabilities.

**Architecture:** `BehaviorNeuron` owns proposal/approval/execution/state history; an owner-scoped `BehaviorCatalogNeuron` atomically selects an installed revision and its complete alias set; an owner-scoped execution queue neuron hands committed work to a hosted pump outside Orleans turns. The existing capability delegation/filter path is extended to commit exact typed results before returning, and a signed in-process executor proves the whole rail before unknown-code execution is enabled.

**Tech Stack:** Existing `Neuron`/durable outbox, Orleans 10.2.2-rc.2 and Journaling 10.2.2-rc.2.alpha.1, Orleans standalone serialization, generated capability adapters, `BackgroundService`, bounded `System.Threading.Channels`, Reqnroll/xUnit v3.

## Global Constraints

- Exactly one `[GrainType("behavior")]` implementation exists in the Orleans manifest.
- `BehaviorNeuron : Neuron, IBehavior`; `BehaviorCapabilityBroker` is deliberately not a `Neuron` so the existing delegated-call filter branch is used.
- Every owner-level durable runtime coordinator is a neuron: catalog and execution queue included.
- A Behavior turn commits a receipt and queue handoff, then returns; it never awaits user code, a process, gRPC, or compilation.
- Catalog installation changes the active revision and complete subscription set in one durable catalog commit.
- Runtime routing uses stable `[Alias]` values; CLR full names never appear in persisted subscription keys.
- Sequential capability calls use `(BehaviorExecutionId, CallOrdinal)` plus canonical request fingerprint.
- An identical replay returns committed result bytes; a changed fingerprint fails; consumed-without-terminal is outcome-uncertain and is never silently repeated.
- Orleans `RequestContext` carries bounded causal metadata only; actual source grain, delegation, target, contract, method, owner, revision, ordinal, and fingerprint checks authorize.
- Behavior-private state commits only with a valid correlated completion.
- The trusted in-process executor is available only for source-controlled, signed boot/recovery artifacts and uses the same artifact, context, broker, journals, and BDD.
- The current Flutter concrete Behavior is removed in the same commit that introduces the sole generic grain and the signed `StartUi` replacement.

---

### Task 1: Characterize and lock the Orleans durability boundary

**Files:**
- Modify: `Directory.Packages.props`
- Test: `tests/DigitalBrain.Tests/Packages/OrleansVersionCoherence.cs`
- Test: `tests/DigitalBrain.ModuleTests/KernelDurabilityCharacterization.cs`
- Test: `tests/DigitalBrain.ModuleTests/OutboxRecoveryCharacterization.cs`

**Interfaces:**
- Consumes: existing `Neuron`, `DurableGrain`, journals, outbox, delegations, reminders.
- Produces: a locked coherent Orleans RC family and recovery tests protecting later refactors.

- [ ] **Step 1: Add failing package-coherence and recovery tests**

```csharp
[Fact]
public void OrleansRuntimeAndSerializationStayOnTheJournalingRcFamily()
{
    var resolved = PackageGraph.ReadAssets(KernelProject);
    Assert.All(
        resolved.Where(p => p.Id.StartsWith("Microsoft.Orleans.", StringComparison.Ordinal)),
        p => Assert.StartsWith("10.2.2-rc.2", p.Version, StringComparison.Ordinal));
}

[Fact]
public async Task FailedJournalAppendRestoresEveryStagedNeuronStructure()
{
    var before = await fixture.SnapshotAsync();
    await Assert.ThrowsAsync<InjectedJournalFailure>(() => fixture.FireWithFailedCommitAsync());
    Assert.Equal(before, await fixture.SnapshotAsync());
}
```

- [ ] **Step 2: Run the tests and capture the baseline**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~OrleansVersionCoherence"; dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --filter "FullyQualifiedName~DurabilityCharacterization|FullyQualifiedName~OutboxRecoveryCharacterization"`

Expected: version test FAIL because direct serialization is not pinned; existing recovery behavior is recorded before edits.

- [ ] **Step 3: Pin standalone serialization without mixing stable Orleans**

```xml
<PackageVersion Include="Microsoft.Orleans.Serialization" Version="10.2.2-rc.2" />
```

Add direct references only to projects which build a standalone serializer. Preserve all existing
Orleans pins at `10.2.2-rc.2` while Journaling remains `10.2.2-rc.2.alpha.1`.

- [ ] **Step 4: Run characterization tests**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~OrleansVersionCoherence"; dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --filter "FullyQualifiedName~DurabilityCharacterization|FullyQualifiedName~OutboxRecoveryCharacterization"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add Directory.Packages.props tests/DigitalBrain.Tests tests/DigitalBrain.ModuleTests
git commit -m "test(kernel): lock behavior runtime durability invariants"
```

### Task 2: Make capability terminals result-aware and replayable

**Files:**
- Modify: `src/DigitalBrain.Kernel/CapabilityDelegation.cs`
- Modify: `src/DigitalBrain.Kernel/CapabilityDelegationState.cs`
- Modify: `src/DigitalBrain.Kernel/ICapabilityDelegationAuthority.cs`
- Modify: `src/DigitalBrain.Kernel/Neuron/Neuron.Capability.Delegation.cs`
- Modify: `src/DigitalBrain.Kernel/Filters/OutgoingReificationFilter.cs`
- Create: `src/DigitalBrain.Kernel/CapabilityTerminal.cs`
- Create: `src/DigitalBrain.Kernel/Behavior/BehaviorCallIdentity.cs`
- Test: `tests/DigitalBrain.ModuleTests/BehaviorCapabilityReplay.cs`
- Test: `tests/DigitalBrain.ModuleTests/CapabilityDelegationL1.cs`

**Interfaces:**
- Consumes: existing exact delegation source/target/interface/method checks and generated result codecs.
- Produces: `FinishAsync(CapabilityDelegation, CapabilityTerminal)` and durable replay records.

- [ ] **Step 1: Write worker-loss, mismatch, and terminal-write tests**

```csharp
[Fact]
public async Task CommittedResultReplaysWithoutSecondModuleEffect()
{
    var first = await fixture.InvokeThenLoseWorkerResponseAsync();
    var replay = await fixture.ReplayAsync(first.Execution, callOrdinal: 0);
    Assert.Equal(first.ResultBytes, replay.ResultBytes);
    Assert.Equal(1, await fixture.ModuleInvocationCountAsync());
}

[Fact]
public async Task SameOrdinalWithDifferentFingerprintIsRejected()
    => await Assert.ThrowsAsync<BehaviorReplayMismatchException>(
        () => fixture.ReplayWithChangedArgumentsAsync());
```

- [ ] **Step 2: Run focused tests and verify failure**

Run: `dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --filter "FullyQualifiedName~BehaviorCapabilityReplay|FullyQualifiedName~CapabilityDelegationL1"`

Expected: new replay tests FAIL because only a success boolean is persisted.

- [ ] **Step 3: Extend terminal state and the outgoing filter**

```csharp
[GenerateSerializer, Alias("db.capability-terminal")]
internal sealed record CapabilityTerminal(
    [property: Id(0)] CapabilityTerminalKind Kind,
    [property: Id(1)] string ResultAlias,
    [property: Id(2)] byte[] ResultBytes,
    [property: Id(3)] string? FailureCode,
    [property: Id(4)] string? FailureDetail);

internal interface ICapabilityDelegationAuthority : IGrainInterface
{
    Task RedeemAsync(CapabilityDelegation delegation);
    Task FinishAsync(CapabilityDelegation delegation, CapabilityTerminal terminal);
}
```

Add optional execution ID, call ordinal, and request fingerprint to Behavior delegations.
After `context.Invoke()` succeeds, `OutgoingReificationFilter` uses the generated declared-result
codec for `context.Result`, then awaits `FinishAsync` before allowing the result to return. Normalize
failures to bounded codes/details. Never deserialize a worker-selected runtime type.

- [ ] **Step 4: Run capability tests**

Run: `dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --filter "FullyQualifiedName~BehaviorCapabilityReplay|FullyQualifiedName~CapabilityDelegationL1"`

Expected: PASS, including terminal-storage failure never reporting success.

- [ ] **Step 5: Commit**

```powershell
git add src/DigitalBrain.Kernel tests/DigitalBrain.ModuleTests
git commit -m "feat(kernel): persist replayable capability results"
```

### Task 3: Replace CLR-name subscriptions with an owner-scoped Behavior catalog neuron

**Files:**
- Create: `src/DigitalBrain.Abstractions/IBehaviorCatalogNeuron.cs`
- Create: `src/DigitalBrain.Abstractions/BehaviorRouteBinding.cs`
- Create: `src/DigitalBrain.Abstractions/BehaviorInstallationRecord.cs`
- Create: `src/DigitalBrain.Kernel/Behavior/BehaviorCatalogNeuron.cs`
- Modify: `src/DigitalBrain.Kernel/BroadcastCatalog.cs`
- Modify: `src/DigitalBrain.Kernel/Neuron/Neuron.Messaging.cs`
- Modify: `src/DigitalBrain.Kernel/Outbox/OutboxEntry.cs`
- Modify: `src/DigitalBrain.Kernel/Filters/OwnerBoundCallFilter.cs`
- Delete: `src/DigitalBrain.Abstractions/ISubscriptionRegistry.cs`
- Delete: `src/DigitalBrain.Kernel/SubscriptionRegistry.cs`
- Test: `tests/DigitalBrain.ModuleTests/BehaviorCatalogRouting.cs`
- Test: `tests/DigitalBrain.Tests/Boundary/StableWireIdentity.cs`

**Interfaces:**
- Consumes: generated stable synapse alias catalog and approved revision manifest.
- Produces: atomic `SelectRevision`, `Uninstall`, `RoutesFor` on the owner catalog neuron.

- [ ] **Step 1: Write atomic replacement and stable-alias tests**

```csharp
[Fact]
public async Task SelectingRevisionReplacesTheCompleteSubscriptionSet()
{
    await catalog.SelectRevision(FixtureInstall.Revision1(["db.started", "mail.received"]));
    await catalog.SelectRevision(FixtureInstall.Revision2(["db.started"]));
    Assert.Empty(await catalog.RoutesFor("mail.received"));
    Assert.Equal(FixtureInstall.Revision2.Id, (await catalog.RoutesFor("db.started")).Single().Revision);
}

[Fact]
public void PersistedRoutingContainsNoClrFullNames()
    => Assert.DoesNotContain("DigitalBrain.", fixture.SerializedCatalogState, StringComparison.Ordinal);
```

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --filter "FullyQualifiedName~BehaviorCatalogRouting"; dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~StableWireIdentity"`

Expected: FAIL because the current registry appends CLR full-name keys and cannot replace/uninstall.

- [ ] **Step 3: Implement the catalog and route binding**

```csharp
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IBehaviorCatalogNeuron : INeuron
{
    Task SelectRevision(BehaviorInstallationRecord installation);
    Task Uninstall(BehaviorId behavior);
    Task<IReadOnlyList<BehaviorRouteBinding>> RoutesFor(string synapseAlias);
    Task<BehaviorInstallationRecord?> Resolve(BehaviorId behavior);
}
```

`BehaviorCatalogNeuron : Neuron` stores installation records and a reverse alias index in durable
collections and commits both in one turn. `OutboxEntry` carries an optional
`BehaviorRouteBinding`; compiled handlers remain static catalog receivers. A Behavior delivery
includes the selected revision so a late delivery cannot run under a different revision.
Uninstall removes the complete reverse index in the same commit.

- [ ] **Step 4: Run routing and existing broadcast tests**

Run: `dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --filter "FullyQualifiedName~BehaviorCatalogRouting|FullyQualifiedName~Broadcast"; dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~StableWireIdentity"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/DigitalBrain.Abstractions src/DigitalBrain.Kernel tests/DigitalBrain.ModuleTests tests/DigitalBrain.Tests
git commit -m "refactor(kernel): make behavior catalog own stable subscriptions"
```

### Task 4: Define explicit Behavior intent envelopes and client routing

**Files:**
- Modify: `src/DigitalBrain.Abstractions/IBehavior.cs`
- Create: `src/DigitalBrain.Abstractions/IBehaviorControl.cs`
- Create: `src/DigitalBrain.Abstractions/BehaviorIntentInvocation.cs`
- Create: `src/DigitalBrain.Abstractions/BehaviorExecutionReceipt.cs`
- Create: `src/DigitalBrain.Abstractions/BehaviorExecutionOutcome.cs`
- Modify: `src/DigitalBrain.Abstractions/ISessionNeuron.cs`
- Modify: `src/DigitalBrain.Kernel/Neuron/SessionNeuron.cs`
- Modify: `src/DigitalBrain.Client/IDigitalBrain.cs`
- Modify: `src/DigitalBrain.Client/DigitalBrainClient.cs`
- Test: `tests/DigitalBrain.Tests/Packages/ClientApiContracts.cs`

**Interfaces:**
- Consumes: exact owner, Behavior ID, schema ID/version, canonical JSON bytes.
- Produces: explicit client routing contract and hidden `IBehaviorControl`; generic `Get<IBehavior>` remains forbidden.

- [ ] **Step 1: Write client-surface and owner-isolation tests**

```csharp
[Fact]
public void ClientExposesExactBehaviorIntentOperations()
{
    ClientApiAssert.HasMethod(nameof(IDigitalBrain.InvokeBehaviorAsync));
    ClientApiAssert.HasMethod(nameof(IDigitalBrain.ReadBehaviorExecutionAsync));
    ClientApiAssert.GenericGetRejects(typeof(IBehavior));
}

```

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~ClientApiContracts"`

Expected: FAIL with missing explicit operations.

- [ ] **Step 3: Implement exact routing**

```csharp
Task<BehaviorExecutionReceipt> InvokeBehaviorAsync(
    BehaviorIntentAddress address,
    ReadOnlyMemory<byte> canonicalJson);

Task<BehaviorExecutionOutcome?> ReadBehaviorExecutionAsync(
    BehaviorExecutionId execution);
```

The client sends the exact envelope through the owner session, which resolves the owner catalog
record and calls hidden `IBehaviorControl` on the selected grain identity. Keep `IBehavior` a
marker, and keep `Get<IBehavior>` plus generic `SendAsync<IBehavior>` rejected so callers cannot
bypass intent envelopes. Task 7 supplies the sole control implementation; Task 8 proves the runtime
receipt/outcome path.

- [ ] **Step 4: Run client and intent tests**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~ClientApiContracts"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/DigitalBrain.Abstractions src/DigitalBrain.Client tests/DigitalBrain.Tests
git commit -m "feat(client): define exact behavior intent routing"
```

### Task 5: Add the minimal dispatch seam and Behavior durable state model

**Files:**
- Modify: `src/DigitalBrain.Kernel/Neuron/Neuron.Turns.cs`
- Create: `src/DigitalBrain.Kernel/Behavior/BehaviorExecutionRecord.cs`
- Create: `src/DigitalBrain.Kernel/Behavior/BehaviorPrivateState.cs`
- Create: `src/DigitalBrain.Kernel/Behavior/BehaviorLifecycleFacts.cs`
- Test: `tests/DigitalBrain.ModuleTests/BehaviorDispatchExtension.cs`
- Test: `tests/DigitalBrain.ModuleTests/BehaviorExecutionStateModel.cs`

**Interfaces:**
- Consumes: `Neuron.Deliver` and catalog route bindings.
- Produces: one protected dispatch override plus immutable execution/private-state records for Tasks 6–7.

- [ ] **Step 1: Write dispatch-ownership and state-transition tests**

```csharp
[Fact]
public async Task DerivedDispatchStillCommitsThroughBaseDeliver()
{
    await fixture.DeliverToProbeAsync(new ProbeSynapse("value"));
    Assert.Single(await fixture.IncomingAsync());
    Assert.Equal("value", fixture.Probe.Handled);
}

[Fact]
public void CompletionStateRequiresMatchingExecutionRevisionAndLease()
    => BehaviorExecutionStateAssert.RejectsEveryMismatchedBinding();
```

- [ ] **Step 2: Run tests and verify the new assertions fail**

Run: `dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --filter "FullyQualifiedName~BehaviorDispatchExtension|FullyQualifiedName~BehaviorExecutionStateModel"`

Expected: FAIL because dispatch is private and the state model is absent.

- [ ] **Step 3: Expose only the protected dispatch hook**

Change:

```csharp
private Task DispatchAsync(Synapse synapse)
```

to:

```csharp
protected virtual Task DispatchAsync(Synapse synapse, CancellationToken cancellationToken)
    => SynapseDispatch.HandlersFor(GetType()).TryGetValue(synapse.GetType(), out var handler)
        ? handler(this, synapse, cancellationToken)
        : Task.CompletedTask;
```

Base `Deliver` continues to own snapshots, dedupe, staging, commit, rollback, outbox, and watcher
notification. Expose the immutable current route binding to derived code; do not expose journal
collections or commit methods.

- [ ] **Step 4: Add immutable state records and transition validation**

Define records for input fingerprint, selected revision, trigger/intent metadata, start/lease/
terminal timestamps, proposed private-state delta, output bytes, and stable failure. Centralize
transition validation so completion requires the same owner, Behavior, execution, revision,
lease, and input fingerprint, and only a successful terminal may expose state changes.

- [ ] **Step 5: Run focused tests**

Run: `dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --filter "FullyQualifiedName~BehaviorDispatchExtension|FullyQualifiedName~BehaviorExecutionStateModel"`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/DigitalBrain.Kernel tests/DigitalBrain.ModuleTests
git commit -m "refactor(kernel): expose the behavior dispatch extension"
```

### Task 6: Add the durable execution queue and off-turn hosted pump

**Files:**
- Create: `src/DigitalBrain.Abstractions/IBehaviorExecutionQueue.cs`
- Create: `src/DigitalBrain.Abstractions/BehaviorExecutionLease.cs`
- Create: `src/DigitalBrain.Kernel/Behavior/BehaviorExecutionQueueNeuron.cs`
- Create: `src/DigitalBrain.Behaviors.Runtime/Execution/BehaviorExecutionDispatcher.cs`
- Create: `src/DigitalBrain.Behaviors.Runtime/Execution/BehaviorExecutionPump.cs`
- Create: `src/DigitalBrain.Behaviors.Runtime/Execution/IBehaviorExecutor.cs`
- Modify: `src/DigitalBrain.Kernel/Hosting/DigitalBrainSiloBuilderExtensions.cs`
- Test: `tests/DigitalBrain.ModuleTests/BehaviorExecutionQueueRecovery.cs`

**Interfaces:**
- Consumes: `BehaviorExecutionStarted` outbox messages.
- Produces: durable pending/leased queue, bounded volatile handoff, and `IBehaviorExecutor.ExecuteAsync`.

- [ ] **Step 1: Write lease/restart/reminder tests**

```csharp
[Fact]
public async Task ExpiredLeaseBecomesClaimableAfterMissedReminderAndRestart()
{
    var lease = await fixture.ClaimAsync();
    await fixture.StopClusterAcrossReminderTickAsync();
    fixture.Time.AdvancePast(lease.ExpiresAt);
    await fixture.RestartAsync();
    Assert.NotNull(await fixture.ClaimAsync());
}

[Fact]
public async Task GrainTurnEndsBeforeExecutorFinishes()
{
    var receipt = await fixture.SubmitBlockingExecutionAsync();
    Assert.Equal(BehaviorExecutionStatus.Pending, await fixture.StatusAsync(receipt.Execution));
}
```

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --filter "FullyQualifiedName~BehaviorExecutionQueueRecovery"`

Expected: FAIL because no queue or pump exists.

- [ ] **Step 3: Implement durable queue plus volatile prompt**

`BehaviorExecutionQueueNeuron : Neuron` stores pending/lease/deadline timestamps. After its durable
commit it writes an owner/queue key to a bounded `Channel<BehaviorQueueSignal>`. A
`BackgroundService` reads signals, claims an exact lease through the hidden
`[ClientEntryPoint]` control contract, calls `IBehaviorExecutor`, and submits correlated
completion. An activation timer prompts active reconciliation; a reminder reactivates after
process/cluster loss. Stored `TimeProvider` timestamps, not ticks, determine expiration.

- [ ] **Step 4: Run queue recovery tests**

Run: `dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --filter "FullyQualifiedName~BehaviorExecutionQueueRecovery"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/DigitalBrain.Abstractions src/DigitalBrain.Kernel src/DigitalBrain.Behaviors.Runtime tests/DigitalBrain.ModuleTests
git commit -m "feat(behaviors): execute committed work outside grain turns"
```

### Task 7: Broker generated capabilities and commit private state

**Files:**
- Create: `src/DigitalBrain.Abstractions/IBehaviorCapabilityBroker.cs`
- Create: `src/DigitalBrain.Abstractions/BehaviorCapabilityCall.cs`
- Create: `src/DigitalBrain.Kernel/Behavior/BehaviorNeuron.cs`
- Create: `src/DigitalBrain.Kernel/Behavior/BehaviorCapabilityBroker.cs`
- Create: `src/DigitalBrain.Behaviors.Runtime/Execution/TrustedBehaviorContext.cs`
- Create: `src/DigitalBrain.Behaviors.Runtime/Execution/TrustedInProcessBehaviorExecutor.cs`
- Create: `os/DigitalBrain.OperatingSystem/DigitalBrain.OperatingSystem.csproj`
- Create: `os/DigitalBrain.OperatingSystem/Behaviors/StartUi/program.cs`
- Create: `os/DigitalBrain.OperatingSystem/Behaviors/StartUi/manifest.json`
- Create: `os/DigitalBrain.OperatingSystem/Behaviors/StartUi/start-ui.feature`
- Modify: `modules/DigitalBrain.Modules.Flutter/DigitalBrain.Modules.Flutter.csproj`
- Delete: `modules/DigitalBrain.Modules.Flutter/OpenHomeOnActivationBehavior.cs`
- Modify: `tests/DigitalBrain.Compositions.Tests/Features/DigitalBrainActivation.feature`
- Modify: `DigitalBrain.slnx`
- Test: `tests/DigitalBrain.ModuleTests/BehaviorCapabilityAuthorization.cs`
- Test: `tests/DigitalBrain.ModuleTests/BehaviorStateCommit.cs`
- Test: `tests/DigitalBrain.ModuleTests/BehaviorNeuronIdentity.cs`
- Test: `tests/DigitalBrain.ModuleTests/BehaviorIntentClient.cs`

**Interfaces:**
- Consumes: generated module invokers/codecs, execution lease, approved grants, result-aware delegation.
- Produces: sole generic Behavior neuron, non-Neuron broker, sequential context, signed in-process executor, and StartUi replacement.

- [ ] **Step 1: Write authorization, ordinal, and state-rollback tests**

```csharp
[Fact]
public async Task BrokerRequiresExactOwnerRevisionTargetMethodAndGrant()
    => await fixture.AssertEveryMutatedBindingIsRejectedAsync();

[Fact]
public async Task FailedExecutionDoesNotCommitProposedPrivateState()
{
    await fixture.ExecuteProgramThatSetsStateThenFailsAsync();
    Assert.Null(await fixture.ReadStateAsync("checkpoint"));
}

[Fact]
public void OrleansManifestHasExactlyOneBehaviorImplementation()
    => Assert.Single(fixture.GrainImplementations("behavior"));

[Fact]
public async Task IntentSubmissionReturnsBeforeExecutionCompletes()
{
    var receipt = await brain.InvokeBehaviorAsync(FixtureIntent.Address, FixtureIntent.Json);
    Assert.NotEqual(Guid.Empty, receipt.Execution.Value);
    Assert.Equal(
        BehaviorExecutionStatus.Pending,
        (await brain.ReadBehaviorExecutionAsync(receipt.Execution))!.Status);
}
```

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --filter "FullyQualifiedName~BehaviorCapabilityAuthorization|FullyQualifiedName~BehaviorStateCommit"`

Expected: FAIL with missing generic neuron, broker, and context.

- [ ] **Step 3: Implement the exact broker path**

`BehaviorNeuron` mints a one-use delegation to the actual owner-bound
`BehaviorCapabilityBroker` grain ID. The broker does not inherit `Neuron`; it executes only a
generated invoker inside `CapabilityRequestContext.InvokeAsync`. The context allocates ordinals
strictly in call order, fingerprints canonical exact-type argument bytes, rejects overlapping
calls, and buffers state changes locally. Completion returns the final state delta to
`BehaviorNeuron`, which validates execution/revision/lease and commits state with the terminal fact.

The trusted executor refuses artifacts without source-controlled provenance and trusted-executor
policy. It loads the exact admitted DLL and uses the same context/broker path as the worker.

- [ ] **Step 4: Replace the compiled Flutter Behavior with StartUi**

`BehaviorNeuron.DispatchAsync` verifies the catalog route revision, records the unique execution,
stages `BehaviorExecutionStarted`, enqueues through the durable outbox, and returns. Intent
submission follows the same path after exact schema validation. It commits private-state changes
only from a matching successful completion.

Compile/sign `StartUi/program.cs` through the foundation pipeline, register it as the
source-controlled boot artifact, and exclude program files from OS project compilation. Delete
`OpenHomeOnActivationBehavior.cs` in the same change so the grain type `behavior` moves directly
from the Flutter class to the one generic implementation without an intermediate duplicate or
fallback.

- [ ] **Step 5: Run authorization, identity, state, and activation tests**

Run: `dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --filter "FullyQualifiedName~BehaviorCapabilityAuthorization|FullyQualifiedName~BehaviorStateCommit|FullyQualifiedName~BehaviorNeuronIdentity|FullyQualifiedName~BehaviorIntentClient"; dotnet test tests/DigitalBrain.Compositions.Tests/DigitalBrain.Compositions.Tests.csproj -c Release --filter "FeatureTitle=DigitalBrain activation"`

Expected: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src modules os tests DigitalBrain.slnx
git commit -m "feat(behaviors): install the generic neuron and StartUi behavior"
```

### Task 8: Prove event, intent, replay, and activation-to-UI product paths

**Files:**
- Create: `tests/DigitalBrain.Compositions.Tests/Features/BehaviorIntent.feature`
- Create: `tests/DigitalBrain.Compositions.Tests/Features/BehaviorRecovery.feature`
- Modify: `tests/DigitalBrain.Compositions.Tests/Features/DigitalBrainActivationBindings.cs`
- Create: `docs/architecture/behavior-kernel-runtime.md`
- Modify: `docs/architecture.md`
- Modify: `docs/index.md`

**Interfaces:**
- Consumes: Tasks 1–7 and the admitted StartUi artifact.
- Produces: green signed built-in event/intent runtime and accurate Built/Not Built documentation.

- [ ] **Step 1: Add the intent and recovery scenarios**

```gherkin
Scenario: Exact approved intent returns a durable receipt and result
  Given an installed signed Behavior intent
  When the owner invokes its exact schema address
  Then invocation returns a durable execution receipt
  And the correlated result matches the approved output schema

Scenario: Capability result survives executor loss
  Given a capability result was committed
  When the executor loses the response and retries the same execution
  Then the recorded result is returned
  And the module effect count is one
```

- [ ] **Step 2: Run scenarios and verify failure**

Run: `dotnet test tests/DigitalBrain.Compositions.Tests/DigitalBrain.Compositions.Tests.csproj -c Release --filter "FeatureTitle=Behavior intent|FeatureTitle=Behavior recovery|FeatureTitle=DigitalBrain activation"`

Expected: FAIL until fixtures use the new rail.

- [ ] **Step 3: Wire fixtures and write current-state docs**

Document the catalog/Behavior/queue/broker sequence and state:

```text
Built: generic Behavior neuron, stable routing, signed in-process execution, event and intent entry points, durable replay.
Not built yet: LPAC worker for unknown code, assistant proposal/discovery API, account-enrichment migration.
```

- [ ] **Step 4: Run focused and root gates**

Run:

```powershell
dotnet test tests/DigitalBrain.Compositions.Tests/DigitalBrain.Compositions.Tests.csproj -c Release
dotnet format DigitalBrain.slnx --verify-no-changes
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --no-build
npm --prefix docs test
npm --prefix docs run build
git diff --check
```

Expected: all commands exit `0`.

- [ ] **Step 5: Commit**

```powershell
git add src modules os tests docs Directory.Packages.props DigitalBrain.slnx
git commit -m "feat(behaviors): complete the durable signed runtime rail"
```
