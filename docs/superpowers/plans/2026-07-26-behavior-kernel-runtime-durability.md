# Behavior Kernel Runtime Plan — Durability

**Status:** Designed. This responsibility file owns Tasks 1–3 of the [stable Kernel runtime index](./2026-07-26-behavior-kernel-runtime.md).

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
