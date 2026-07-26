# Behavior Assistant Discovery — Catalog and Proposal

### Task 1: Generate exact module and synapse discovery descriptors

**Files:**
- Create: `src/DigitalBrain.Behaviors/Catalog/CatalogDescriptor.cs`
- Create: `src/DigitalBrain.Behaviors/Catalog/ModuleContractDescriptor.cs`
- Create: `src/DigitalBrain.Behaviors/Catalog/BehaviorIntentDescriptor.cs`
- Modify: `src/DigitalBrain.SourceGeneration/BehaviorCapabilityGenerator.cs`
- Create: `src/DigitalBrain.SourceGeneration/CatalogDescriptorGenerator.cs`
- Test: `tests/DigitalBrain.Tests/SourceGeneration/CatalogDescriptorGeneration.cs`
- Test: `tests/DigitalBrain.Tests/Boundary/CatalogDescriptorBoundaries.cs`

**Interfaces:**
- Consumes: stable module/contract/method/synapse aliases, descriptions, examples, and versions.
- Produces: immutable `CatalogDescriptor` records and generated `ICompiledCatalogManifest`.

- [ ] **Step 1: Write generator and boundary tests**

```csharp
[Fact]
public void GeneratedModuleDescriptorUsesStableWireIdentity()
{
    var descriptor = fixture.DescriptorFor<IShell>();
    Assert.Equal("flutter.shell", descriptor.ContractAlias);
    Assert.Contains(descriptor.Methods, m => m.MethodAlias == "Open");
    Assert.DoesNotContain("DigitalBrain.Flutter.IShell", descriptor.SearchableText);
}

[Fact]
public void DescriptorsContainDataOnly()
    => Assert.All(
        typeof(CatalogDescriptor).GetProperties(),
        p => Assert.False(typeof(Type).IsAssignableFrom(p.PropertyType)));
```

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~CatalogDescriptorGeneration|FullyQualifiedName~CatalogDescriptorBoundaries"`

Expected: FAIL with missing descriptor/manifest.

- [ ] **Step 3: Generate stable descriptors**

```csharp
public sealed record CatalogDescriptor(
    string CatalogId,
    CatalogDescriptorKind Kind,
    string Title,
    string Description,
    string SearchableText,
    CatalogVisibility Visibility,
    string Version,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> Examples);
```

Generate module descriptors from exact compiled manifests. Reject missing/duplicate IDs, aliases,
method aliases, or incompatible versions at build time. Normalize searchable text to Unicode Form C
and preserve the original reader-facing title/description separately.

- [ ] **Step 4: Run generator tests**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~CatalogDescriptorGeneration|FullyQualifiedName~CatalogDescriptorBoundaries"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/DigitalBrain.Behaviors src/DigitalBrain.SourceGeneration tests/DigitalBrain.Tests
git commit -m "feat(catalog): generate exact module discovery descriptors"
```

### Task 2: Add deterministic owner-filtered candidate discovery

**Files:**
- Create: `src/DigitalBrain.Behaviors/Catalog/ICatalogCandidateDiscovery.cs`
- Create: `src/DigitalBrain.Behaviors/Catalog/CatalogDiscoveryQuery.cs`
- Create: `src/DigitalBrain.Behaviors/Catalog/CatalogCandidate.cs`
- Create: `src/DigitalBrain.Behaviors.Runtime/Catalog/DeterministicCatalogCandidateDiscovery.cs`
- Create: `src/DigitalBrain.Behaviors.Runtime/Catalog/CatalogProjection.cs`
- Test: `tests/DigitalBrain.Behaviors.Tests/DeterministicCatalogDiscovery.cs`
- Test: `tests/DigitalBrain.Behaviors.Tests/CatalogOwnerIsolation.cs`

**Interfaces:**
- Consumes: generated module descriptors and installed Behavior manifest descriptors.
- Produces: `FindAsync(CatalogDiscoveryQuery, CancellationToken)` returning stable advisory candidates.

- [ ] **Step 1: Write ordering, reason, and owner-isolation tests**

```csharp
[Fact]
public async Task ExactAliasRanksBeforeTokenMatchesWithStableReasons()
{
    var result = await fixture.FindAsync("flutter.shell");
    Assert.Equal("module:flutter.shell", result[0].CatalogId);
    Assert.Equal("exact-alias", result[0].MatchReason);
}

[Fact]
public async Task OwnerPrivateBehaviorNeverAppearsForAnotherOwner()
{
    for (var attempt = 0; attempt < 5; attempt++)
    {
        Assert.DoesNotContain(
            await fixture.FindAsAsync("owner-b", "private mail sorter"),
            c => c.CatalogId == "behavior:owner-a:community.alice.mail-sorter");
    }
}
```

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~DeterministicCatalogDiscovery|FullyQualifiedName~CatalogOwnerIsolation"`

Expected: FAIL with missing discovery implementation.

- [ ] **Step 3: Implement deterministic ranking**

Filter by owner, visibility, kind, and required capability aliases before scoring. Score in this
order: exact catalog ID, exact alias, alias prefix, all query tokens present, token overlap,
description/example overlap. Normalize with Form C plus invariant lowercase, tokenize ASCII
letters/digits/hyphens/dots, cap query at 1 KiB and results at 50, then order by descending score
and ordinal catalog ID. Return stable `MatchReason`; never return a runtime `Type` or proxy.

- [ ] **Step 4: Run discovery tests**

Run: `dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~DeterministicCatalogDiscovery|FullyQualifiedName~CatalogOwnerIsolation"`

Expected: PASS; the test itself repeats queries and asserts identical ordering.

- [ ] **Step 5: Commit**

```powershell
git add src/DigitalBrain.Behaviors src/DigitalBrain.Behaviors.Runtime tests/DigitalBrain.Behaviors.Tests
git commit -m "feat(catalog): discover candidates deterministically"
```

### Task 3: Add durable proposal submission and off-turn admission

**Files:**
- Create: `src/DigitalBrain.Abstractions/BehaviorProposalId.cs`
- Create: `src/DigitalBrain.Abstractions/BehaviorProposalReceipt.cs`
- Create: `src/DigitalBrain.Abstractions/BehaviorProposalStatus.cs`
- Modify: `src/DigitalBrain.Abstractions/IBehaviorControl.cs`
- Create: `src/DigitalBrain.Abstractions/IBehaviorAdmissionQueueNeuron.cs`
- Create: `src/DigitalBrain.Kernel/Behavior/BehaviorAdmissionQueueNeuron.cs`
- Create: `src/DigitalBrain.Kernel/Behavior/BehaviorProposalRecord.cs`
- Create: `src/DigitalBrain.Behaviors.Runtime/Admission/BehaviorAdmissionPump.cs`
- Modify: `src/DigitalBrain.Client/IDigitalBrain.cs`
- Modify: `src/DigitalBrain.Client/DigitalBrainClient.cs`
- Test: `tests/DigitalBrain.ModuleTests/BehaviorProposalLifecycle.cs`

**Interfaces:**
- Consumes: canonical proposal, admission compiler/verifier, sandbox, artifact store.
- Produces: `SubmitBehaviorProposalAsync`, `ReadBehaviorProposalAsync`, durable status transitions.

- [ ] **Step 1: Write immediate-receipt and crash-recovery tests**

```csharp
[Fact]
public async Task SubmissionReturnsBeforeCompilationAndCanBeObserved()
{
    var receipt = await brain.SubmitBehaviorProposalAsync(FixtureProposal.Valid);
    Assert.Equal(BehaviorProposalStatus.Queued, (await brain.ReadBehaviorProposalAsync(receipt.Id))!.Status);
}

[Fact]
public async Task AdmissionLeaseRecoversAfterPumpDeath()
{
    var proposal = await fixture.QueueThenKillPumpAsync();
    await fixture.AdvancePastLeaseAndRestartAsync();
    Assert.Equal(BehaviorProposalStatus.Verified, await fixture.WaitForTerminalStatusAsync(proposal));
}
```

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --filter "FullyQualifiedName~BehaviorProposalLifecycle"`

Expected: FAIL with missing proposal queue/lifecycle.

- [ ] **Step 3: Implement journaled proposal state**

`BehaviorNeuron` validates bounded source/manifest/features, commits `Queued`, emits a durable
admission queue message, and returns. The owner-scoped `BehaviorAdmissionQueueNeuron : Neuron`
uses the same durable lease/timer/reminder pattern as execution. `BehaviorAdmissionPump` runs
compile → metadata admission → sandbox BDD → artifact upload off-turn and submits a correlated
terminal report. Statuses are exactly:

```text
Queued -> Compiling -> Admitted -> Verifying -> Verified
Queued|Compiling|Admitted|Verifying -> Rejected
```

Only `Verified` contains an approval-eligible `BehaviorRevisionId`.

- [ ] **Step 4: Run proposal lifecycle tests**

Run: `dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --filter "FullyQualifiedName~BehaviorProposalLifecycle"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/DigitalBrain.Abstractions src/DigitalBrain.Kernel src/DigitalBrain.Behaviors.Runtime src/DigitalBrain.Client tests/DigitalBrain.ModuleTests
git commit -m "feat(behaviors): admit proposals through a durable queue"
```
