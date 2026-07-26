# Behavior Kernel Runtime Plan — Capabilities and Product Proof

**Status:** Designed. This responsibility file owns Tasks 7–8 of the [stable Kernel runtime index](./2026-07-26-behavior-kernel-runtime.md).

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
