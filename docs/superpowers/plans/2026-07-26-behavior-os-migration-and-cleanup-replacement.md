# Behavior OS Migration and Cleanup — Replacement

> **Status:** Designed/current. This responsibility record is part of the [OS migration/cleanup plan index](2026-07-26-behavior-os-migration-and-cleanup.md); it does not authorize a live execution rail or retirement of the current parity routes.

### Task 1: Move the product test home and freeze replacement outcomes

**Files:**
- Move: `tests/DigitalBrain.Compositions.Tests` → `tests/DigitalBrain.OperatingSystem.Tests`
- Move/rename: `tests/DigitalBrain.OperatingSystem.Tests/DigitalBrain.Compositions.Tests.csproj` → `tests/DigitalBrain.OperatingSystem.Tests/DigitalBrain.OperatingSystem.Tests.csproj`
- Modify: `tests/DigitalBrain.OperatingSystem.Tests/AssemblyInfo.cs`
- Modify: `tests/DigitalBrain.OperatingSystem.Tests/CompositionsFixture.cs` → `OperatingSystemFixture.cs`
- Delete: `tests/DigitalBrain.OperatingSystem.Tests/CompositionBehaviorShape.cs`
- Delete: `tests/DigitalBrain.OperatingSystem.Tests/BehaviorOsActivationHonesty.cs`
- Create: `tests/DigitalBrain.OperatingSystem.Tests/Features/UiBehaviors.feature`
- Create: `tests/DigitalBrain.OperatingSystem.Tests/Features/AccountEnrichmentBehavior.feature`
- Create: `tests/DigitalBrain.OperatingSystem.Tests/Features/MigrationParityBindings.cs`
- Modify: `DigitalBrain.slnx`

**Interfaces:**
- Consumes: existing product outcomes plus the built Behavior test host.
- Produces: outcome-first OS test project with explicit migration scenarios.

- [ ] **Step 1: Add Gherkin outcomes before moving implementation**

```gherkin
Feature: UI operating system behaviors

  Scenario: Activation starts the first screen
    Given the source-controlled UI behaviors are installed for the owner
    When DigitalBrain activation is committed
    Then StartUi records a Behavior execution
    And IShell receives OpenScene for "login"
    And Flutter renders from SceneOpened

  Scenario: Countdown surface composes module vocabulary
    Given ShowCountdown is installed
    When its exact intent is invoked
    Then IShell opens the countdown scene
    And ICountdown receives the approved schedule request
```

```gherkin
Feature: Account enrichment behavior

  Scenario: Gmail evidence becomes an approved Salesforce account update
    Given AccountEnrichment is installed with exact Gmail and Salesforce grants
    When its start intent names a message and account
    Then Gmail is read and Salesforce records a proposed mutation
    When the owner session emits the matching Salesforce approval
    Then the Behavior uses that durable trigger evidence
    And Salesforce records one completed update
    And the Behavior journal records completion without provider secrets
```

- [ ] **Step 2: Move the project, convert namespaces/references, and bind parity scenarios**

Use repository-aware moves, update solution/project references and namespaces from
`DigitalBrain.Compositions.Tests` to `DigitalBrain.OperatingSystem.Tests`, and rename the fixture.
Delete tests which assert the old class shape or dual-route honesty; the Gherkin scenarios replace
them with product evidence. Bind the new UI scenarios to the current composition classes and the
account scenario to the current `IAccountEnrichment` process so this task captures a green
behavioral baseline before replacement.

- [ ] **Step 3: Run the moved tests and preserve the baseline**

Run: `dotnet test tests/DigitalBrain.OperatingSystem.Tests/DigitalBrain.OperatingSystem.Tests.csproj -c Release`

Expected: PASS, including the new parity scenarios through the current implementation.

- [ ] **Step 4: Commit**

```powershell
git add DigitalBrain.slnx tests/DigitalBrain.OperatingSystem.Tests
git add -u tests/DigitalBrain.Compositions.Tests
git commit -m "test(os): move product outcomes to the operating system suite"
```

### Task 2: Re-express shell and surface policy as OS Behavior artifacts

**Files:**
- Modify: `os/DigitalBrain.OperatingSystem/DigitalBrain.OperatingSystem.csproj`
- Create: `os/DigitalBrain.OperatingSystem/Behaviors/PostAuthHome/program.cs`
- Create: `os/DigitalBrain.OperatingSystem/Behaviors/PostAuthHome/manifest.json`
- Create: `os/DigitalBrain.OperatingSystem/Behaviors/PostAuthHome/post-auth-home.feature`
- Create: `os/DigitalBrain.OperatingSystem/Behaviors/NavigateShell/program.cs`
- Create: `os/DigitalBrain.OperatingSystem/Behaviors/NavigateShell/manifest.json`
- Create: `os/DigitalBrain.OperatingSystem/Behaviors/ShowCountdown/program.cs`
- Create: `os/DigitalBrain.OperatingSystem/Behaviors/ShowCountdown/manifest.json`
- Create: `os/DigitalBrain.OperatingSystem/Behaviors/ShowAiPane/program.cs`
- Create: `os/DigitalBrain.OperatingSystem/Behaviors/ShowAiPane/manifest.json`
- Create: `os/DigitalBrain.OperatingSystem/Behaviors/ShowAccountEnrichment/program.cs`
- Create: `os/DigitalBrain.OperatingSystem/Behaviors/ShowAccountEnrichment/manifest.json`
- Modify: `tests/DigitalBrain.OperatingSystem.Tests/Features/UiBehaviors.feature`
- Create: `tests/DigitalBrain.OperatingSystem.Tests/Features/UiBehaviorBindings.cs`
- Modify: `tests/DigitalBrain.OperatingSystem.Tests/Features/MigrationParityBindings.cs`

**Interfaces:**
- Consumes: `IShell.Open`, `ICountdown`, `ILlama32`, exact Behavior intent schemas.
- Produces: signed built-in UI policy replacing useful composition helpers.

- [ ] **Step 1: Add focused failing scenarios for each retained outcome**

Require:

```text
PostAuthHome -> shell opens "home"
NavigateShell -> shell opens schema-provided scenes in order
ShowCountdown -> shell opens "countdown", countdown is scheduled
ShowAiPane -> shell opens "ai", ILlama32 returns the response
ShowAccountEnrichment -> shell opens "enrichment" only
```

Assert module journals and `SceneOpened`; do not assert private program fields or source text.

- [ ] **Step 2: Run UI feature tests and verify failure**

Run: `dotnet test tests/DigitalBrain.OperatingSystem.Tests/DigitalBrain.OperatingSystem.Tests.csproj -c Release --filter "FeatureTitle=UI operating system behaviors"`

Expected: FAIL for missing installed artifacts.

- [ ] **Step 3: Implement one-file programs and exact manifests**

Each artifact has one public program type, schema-owned request/response records, deterministic
command IDs, exact target neuron names, and only required grants. `NavigateShell` validates at
least one scene and preserves input order. `ShowAccountEnrichment` opens a scene only and does not
perform Gmail/Salesforce work. Configure the OS csproj:

```xml
<ItemGroup>
  <Compile Remove="Behaviors\**\program.cs" />
  <EmbeddedResource Include="Behaviors\**\program.cs" />
  <EmbeddedResource Include="Behaviors\**\manifest.json" />
  <EmbeddedResource Include="Behaviors\**\*.feature" />
</ItemGroup>
```

Admit, sandbox-verify, sign, and register each exact artifact through the shared built-in catalog;
do not compile the program into the OS assembly.

Move the UI step definitions from `MigrationParityBindings` to `UiBehaviorBindings`; leave only
the account-enrichment parity bindings for Task 3.

- [ ] **Step 4: Run UI Behavior scenarios**

Run: `dotnet test tests/DigitalBrain.OperatingSystem.Tests/DigitalBrain.OperatingSystem.Tests.csproj -c Release --filter "FeatureTitle=UI operating system behaviors"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add os/DigitalBrain.OperatingSystem tests/DigitalBrain.OperatingSystem.Tests
git commit -m "feat(os): move shell and surface policy into behaviors"
```

### Task 3: Re-express account enrichment as one multi-entry Behavior

**Files:**
- Create: `os/DigitalBrain.OperatingSystem/Behaviors/AccountEnrichment/program.cs`
- Create: `os/DigitalBrain.OperatingSystem/Behaviors/AccountEnrichment/manifest.json`
- Create: `os/DigitalBrain.OperatingSystem/Behaviors/AccountEnrichment/start-request.schema.json`
- Create: `os/DigitalBrain.OperatingSystem/Behaviors/AccountEnrichment/start-result.schema.json`
- Create: `os/DigitalBrain.OperatingSystem/Behaviors/AccountEnrichment/account-enrichment.feature`
- Modify: `tests/DigitalBrain.OperatingSystem.Tests/Features/AccountEnrichmentBehavior.feature`
- Create: `tests/DigitalBrain.OperatingSystem.Tests/Features/AccountEnrichmentBindings.cs`
- Delete: `tests/DigitalBrain.OperatingSystem.Tests/Features/MigrationParityBindings.cs`
- Modify: `src/DigitalBrain.Behaviors/BehaviorExecutionMetadata.cs`
- Modify: `src/DigitalBrain.Behaviors.Runtime/Execution/TrustedBehaviorContext.cs`
- Modify: `hosts/DigitalBrain.BehaviorWorker/Execution/WorkerBehaviorContext.cs`

**Interfaces:**
- Consumes: `IGmail.ReadMessage`, `ISalesforce.ProposeAccountDescription`, `SalesforceMutationApproval`, `ISalesforce.ApproveAccountDescription`.
- Produces: intent `com.digitalbrain.account-enrichment.start/v1`, approval event subscription, private state keyed by `CommandId`.

- [ ] **Step 1: Add parity and recovery assertions**

The feature must assert:

```text
same CommandId + same input -> same proposal, no duplicate provider effect
same CommandId + changed input -> rejected
wrong approval caller/fingerprint -> ignored/rejected
matching session approval -> exactly one completed Salesforce mutation
worker loss after proposal/approval -> recorded capability result replayed
private state contains no OAuth token, provider response envelope, or raw credential
```

- [ ] **Step 2: Run the account feature and verify failure**

Run: `dotnet test tests/DigitalBrain.OperatingSystem.Tests/DigitalBrain.OperatingSystem.Tests.csproj -c Release --filter "FeatureTitle=Account enrichment behavior"`

Expected: FAIL because the Behavior is absent.

- [ ] **Step 3: Implement the program over module contracts**

The start intent validates non-empty message/account/Gmail account, reads Gmail, derives the same
description, calls Salesforce proposal, and stores:

```csharp
private sealed record EnrichmentState(
    string MessageId,
    string GmailAccount,
    string AccountId,
    string Description,
    string MutationFingerprint,
    bool Completed);
```

The `IBehaviorProgram<SalesforceMutationApproval>` entry reads state by `CommandId`, requires the
approval caller to be the owner session and fingerprint to match, then calls
`ApproveAccountDescription(approval, context.Execution.TriggerDelivery, cancellationToken)`.
`TriggerDelivery` is immutable execution evidence supplied by the broker; the broker accepts it as
a capability argument only when it byte-matches the current execution trigger. Commit
`Completed=true` only when Salesforce returns `Completed`.

Move the remaining account steps to `AccountEnrichmentBindings` and delete the migration binding
file; every OS feature now executes only through installed Behaviors.

- [ ] **Step 4: Run account Behavior scenarios and integration edge tests**

Run: `dotnet test tests/DigitalBrain.OperatingSystem.Tests/DigitalBrain.OperatingSystem.Tests.csproj -c Release --filter "FeatureTitle=Account enrichment behavior"; dotnet test tests/DigitalBrain.Integrations.Tests/DigitalBrain.Integrations.Tests.csproj -c Release --filter "FullyQualifiedName~GmailReadMessage|FullyQualifiedName~SalesforceMutation"`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add os/DigitalBrain.OperatingSystem src/DigitalBrain.Behaviors src/DigitalBrain.Behaviors.Runtime hosts/DigitalBrain.BehaviorWorker tests/DigitalBrain.OperatingSystem.Tests
git commit -m "feat(os): migrate account enrichment into a behavior"
```
