# Behavior Assistant Discovery — Approval, Invocation, and Proof

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
