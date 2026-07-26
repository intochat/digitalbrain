# Behavior Kernel Runtime Plan — Dispatch

**Status:** Designed. This responsibility file owns Tasks 4–6 of the [stable Kernel runtime index](./2026-07-26-behavior-kernel-runtime.md).

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
