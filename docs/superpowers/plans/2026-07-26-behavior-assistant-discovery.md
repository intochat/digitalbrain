# Behavior Assistant Discovery and Composition Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give an in-brain AI assistant exact primitives to discover and invoke approved Behaviors and to submit new one-file Behavior proposals without gaining approval or installation authority.

**Architecture:** Source generation and installed manifests feed a deterministic, owner-filtered catalog projection. Discovery returns advisory IDs and match reasons; callers re-resolve the authoritative module/Behavior record and repeat schema/grant checks. Proposal compilation is durable/off-turn, while only the authenticated owner client can approve an exact verified digest and grant set.

**Tech Stack:** Existing module manifests/source generation, owner-scoped Behavior catalog neuron, `BackgroundService` admission pump, Behavior admission/sandbox services, `System.Text.Json`, existing Microsoft.Extensions.AI abstractions only where an actual assistant consumes the APIs.

## Global Constraints

- Search is not authority: every candidate is re-resolved by exact catalog ID before invocation or proposal dependency binding.
- The first implementation is a deterministic in-memory projection; do not add a vector database, vector abstraction, embedding package, or provider.
- Catalog descriptors are immutable projections and contain no executable delegates, `Type` instances, grain proxies, credentials, or owner-private payloads.
- Owner and visibility filtering occurs before scoring and repeats after exact resolution.
- An assistant may invoke installed approved intents and submit source/schema/BDD proposals.
- An assistant may not approve, install, replace, roll back, uninstall, widen grants, or select an active revision.
- Owner approval binds exact revision digest, compiler/admission/BDD policy versions, requested grants, and provenance evidence.
- Proposal submission returns a durable receipt; compilation/verification never holds the caller or a grain turn open.
- Program-to-Behavior invocation is a separately grantable system capability and uses the same receipt/outcome model as client or neuron invocation.
- Dynamic Behavior intent schemas remain JSON; only modules add public CLR vocabulary.

---

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

### Task 4: Bind human approval to exact revision and grants

**Files:**
- Create: `src/DigitalBrain.Abstractions/BehaviorApprovalRequest.cs`
- Create: `src/DigitalBrain.Abstractions/BehaviorApprovalEvidence.cs`
- Modify: `src/DigitalBrain.Abstractions/ISessionNeuron.cs`
- Modify: `src/DigitalBrain.Kernel/Neuron/SessionNeuron.cs`
- Modify: `src/DigitalBrain.Kernel/Behavior/BehaviorNeuron.cs`
- Modify: `src/DigitalBrain.Kernel/Behavior/BehaviorCatalogNeuron.cs`
- Modify: `src/DigitalBrain.Client/IDigitalBrain.cs`
- Modify: `src/DigitalBrain.Client/DigitalBrainClient.cs`
- Test: `tests/DigitalBrain.ModuleTests/BehaviorApprovalAuthorization.cs`
- Test: `tests/DigitalBrain.ModuleTests/BehaviorRevisionInstallation.cs`

**Interfaces:**
- Consumes: authenticated edge approval evidence, verified digest, requested/approved grants.
- Produces: client-only approve/rollback/uninstall operations and atomic catalog selection.

- [ ] **Step 1: Write exact-digest, grant, and non-human rejection tests**

```csharp
[Fact]
public async Task ApprovalRejectsAnyChangedEvidenceOrWidenedGrant()
{
    await fixture.AssertApprovalRejectedAsync(change: ApprovalMutation.RevisionDigest);
    await fixture.AssertApprovalRejectedAsync(change: ApprovalMutation.CompilerPolicy);
    await fixture.AssertApprovalRejectedAsync(change: ApprovalMutation.Feature);
    await fixture.AssertApprovalRejectedAsync(change: ApprovalMutation.AddGrant);
}

[Fact]
public async Task AssistantSourceCannotReachApprovalOperation()
    => await Assert.ThrowsAsync<NeuronAuthorizationException>(
        () => fixture.AssistantAttemptsApprovalAsync());
```

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --filter "FullyQualifiedName~BehaviorApprovalAuthorization|FullyQualifiedName~BehaviorRevisionInstallation"`

Expected: FAIL because approval control is absent.

- [ ] **Step 3: Implement the human-only transition**

Expose approval/rollback/uninstall only through `IDigitalBrain` → owner `ISessionNeuron`; do not
put them on `IBehaviorContext`, discovery, proposal, or assistant helper interfaces. Require
authenticated edge evidence containing owner, subject, authentication event ID, timestamp,
revision digest, policy hashes, and exact grant set. `BehaviorNeuron` commits the approval fact,
then emits the installation selection; `BehaviorCatalogNeuron` verifies the approval proof and
atomically changes active revision plus complete subscriptions. Rollback selects an already
approved revision; uninstall removes the catalog record/subscriptions but retains Behavior
journal history.

- [ ] **Step 4: Run approval/install tests**

Run: `dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --filter "FullyQualifiedName~BehaviorApprovalAuthorization|FullyQualifiedName~BehaviorRevisionInstallation"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src/DigitalBrain.Abstractions src/DigitalBrain.Kernel src/DigitalBrain.Client tests/DigitalBrain.ModuleTests
git commit -m "feat(behaviors): require exact human approval for installation"
```

### Task 5: Let neurons and programs invoke exact installed Behavior intents

**Files:**
- Modify: `src/DigitalBrain.Kernel/Neuron/Neuron.Messaging.cs`
- Create: `src/DigitalBrain.Kernel/Behavior/BehaviorIntentInvoker.cs`
- Modify: `src/DigitalBrain.Behaviors/IBehaviorContext.cs`
- Modify: `src/DigitalBrain.Behaviors/Manifest/BehaviorCapabilityGrant.cs`
- Modify: `src/DigitalBrain.Behaviors.Runtime/Execution/TrustedBehaviorContext.cs`
- Modify: `hosts/DigitalBrain.BehaviorWorker/Execution/WorkerBehaviorContext.cs`
- Modify: `src/DigitalBrain.Behaviors.Protocol/Protos/behavior_broker.proto`
- Test: `tests/DigitalBrain.ModuleTests/AssistantBehaviorInvocation.cs`
- Test: `tests/DigitalBrain.Behaviors.Tests/Windows/ProgramBehaviorInvocation.cs`

**Interfaces:**
- Consumes: exact `BehaviorIntentAddress`, canonical JSON, installed catalog, `behavior-intent` grant.
- Produces: protected neuron helper and context `InvokeBehaviorAsync` returning a durable receipt.

- [ ] **Step 1: Write in-brain assistant and program-composition tests**

```csharp
[Fact]
public async Task AssistantNeuronCanInvokeAnInstalledIntentByExactAddress()
{
    var receipt = await fixture.Assistant.InvokeResolvedIntent(FixtureIntent.Address, FixtureIntent.Json);
    Assert.Equal(BehaviorExecutionStatus.Completed, (await fixture.OutcomeAsync(receipt.Execution))!.Status);
}

[WindowsFact]
public async Task ProgramNeedsAnExactBehaviorIntentGrant()
    => Assert.Equal(
        "DBB403",
        (await fixture.InvokeProgramWithoutIntentGrantAsync()).FailureCode);
```

- [ ] **Step 2: Run tests and verify failure**

Run: `dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --filter "FullyQualifiedName~AssistantBehaviorInvocation"; dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~ProgramBehaviorInvocation"`

Expected: FAIL because internal callers have no exact intent path.

- [ ] **Step 3: Implement one shared exact invoker**

Add:

```csharp
ValueTask<BehaviorExecutionReceipt> InvokeBehaviorAsync(
    BehaviorIntentAddress address,
    ReadOnlyMemory<byte> canonicalJson,
    CancellationToken cancellationToken = default);
```

to `IBehaviorContext`, and a protected equivalent on `Neuron`. Both delegate to one
`BehaviorIntentInvoker` which resolves the authoritative owner catalog, checks active revision,
schema, visibility, and exact grant, validates input, then invokes hidden `IBehaviorControl` and
returns the receipt. The worker sends this as a distinct broker operation; it cannot disguise it as a module
method. A cycle/depth budget carried in causal metadata prevents unbounded Behavior recursion.

- [ ] **Step 4: Run composition tests**

Run: `dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --filter "FullyQualifiedName~AssistantBehaviorInvocation"; dotnet test tests/DigitalBrain.Behaviors.Tests/DigitalBrain.Behaviors.Tests.csproj -c Release --filter "FullyQualifiedName~ProgramBehaviorInvocation"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add src hosts tests/DigitalBrain.ModuleTests tests/DigitalBrain.Behaviors.Tests
git commit -m "feat(behaviors): compose installed intents through exact grants"
```

### Task 6: Prove the assistant contract and record the vector deferral

**Files:**
- Create: `tests/DigitalBrain.Compositions.Tests/Features/AssistantBehaviorComposition.feature`
- Create: `tests/DigitalBrain.Compositions.Tests/Features/AssistantBehaviorApprovalBoundary.feature`
- Create: `docs/architecture/behavior-discovery-and-assistants.md`
- Modify: `docs/architecture.md`
- Modify: `docs/index.md`

**Interfaces:**
- Consumes: Tasks 1–5.
- Produces: product BDD showing invoke/propose works and self-approval does not.

- [ ] **Step 1: Add assistant product scenarios**

```gherkin
Scenario: Assistant invokes an installed Behavior
  Given the owner has approved and installed a mail triage intent
  When the assistant discovers candidates for "triage this mail"
  And resolves the exact installed intent address
  Then the Behavior returns a durable execution receipt
  And its result matches the approved output schema

Scenario: Assistant proposes but cannot approve a composed Behavior
  Given the assistant composed one C# file, schemas, grants, and BDD
  When it submits the proposal
  Then the exact revision is compiled and verified outside the silo
  But no revision is installed until the authenticated owner approves its digest and grants
```

- [ ] **Step 2: Run scenarios and verify failure**

Run: `dotnet test tests/DigitalBrain.Compositions.Tests/DigitalBrain.Compositions.Tests.csproj -c Release --filter "FeatureTitle=Assistant Behavior"`

Expected: FAIL until bindings use the exact catalog/proposal APIs.

- [ ] **Step 3: Wire scenarios and document the discovery contract**

Document descriptor sources, deterministic ranking, visibility isolation, exact re-resolution,
proposal/approval separation, and the program/neuron/client intent paths. Record:

```text
Built: deterministic discovery over hundreds/thousands of descriptors.
Deferred by evidence: vector infrastructure. Add it only if a reviewed 100/1,000/10,000-descriptor
benchmark proves recall or latency value; any vector index remains a disposable non-authoritative
projection keyed by model+dimension+normalization+catalog version.
```

- [ ] **Step 4: Run focused and root gates**

Run:

```powershell
dotnet test tests/DigitalBrain.Compositions.Tests/DigitalBrain.Compositions.Tests.csproj -c Release --filter "FeatureTitle=Assistant Behavior"
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
git add src hosts tests docs
git commit -m "feat(assistants): discover invoke and propose exact behaviors"
```
