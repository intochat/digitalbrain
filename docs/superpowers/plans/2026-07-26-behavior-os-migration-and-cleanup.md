# Behavior OS Migration and Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move every surviving UI and account-enrichment policy into installed OS Behaviors, delete the compiled composition/process paths, and make code, tests, packages, and documentation describe one built system.

**Architecture:** Source-controlled OS artifacts compose Flutter, Time, AI, Google, and Salesforce module vocabulary through the same Behavior rail proven in prior plans. Product tests move from “Compositions” to “OperatingSystem,” assert journals and edge outcomes, and become deletion gates; only then are the two sample projects, their process contracts/facts, duplicate helpers, and stale claims removed.

**Tech Stack:** Signed built-in Behavior artifacts, existing Flutter/Time/AI/Google/Salesforce module contracts and runtimes, Reqnroll/xUnit v3, DigitalBrain test brain, root package/boundary tests, VitePress docs.

## Global Constraints

- Modules retain vocabulary/provider mechanics; no Behavior logic remains in a module implementation.
- OS policy is one-file Behavior source plus manifest/schema/features; files are embedded as artifacts, not compiled as trusted grain classes.
- Start/UI/account Behaviors use exact module contracts and grants; they add no public CLR neuron or synapse vocabulary.
- Human Salesforce approval remains durable evidence from the owner session and is validated by the Salesforce module.
- Account-enrichment private request history moves to `BehaviorNeuron` state; it is not duplicated in another process neuron or database.
- The product has one activation-to-first-screen path and one account-enrichment path.
- Every current composition either becomes a named OS Behavior with product value or is deleted; no wrapper/helper layer survives.
- Rename `DigitalBrain.Compositions.Tests` to `DigitalBrain.OperatingSystem.Tests`; delete source-grep shape tests and keep journal/edge product proofs.
- Remove projects and references in the same commits as their green replacements.
- Historical design/grill documents remain only as clearly archived evidence; current docs contain no “rail unbuilt,” pre-rail, or compiled-Behavior claims.
- No empty directories, build output, temp artifacts, commented-out code, redundant samples, obsolete tests, or contradictory package descriptions remain.

---

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

### Task 4: Register the built-in OS through hosting and test fixtures

**Files:**
- Create: `os/DigitalBrain.OperatingSystem/DigitalBrainOperatingSystem.cs`
- Create: `os/DigitalBrain.OperatingSystem/Hosting/OperatingSystemHostingExtensions.cs`
- Modify: `src/DigitalBrain.Aspire.Hosting/DigitalBrainBuilder.cs`
- Modify: `hosts/DigitalBrain.AppHost/AppHost.cs`
- Modify: `hosts/DigitalBrain.Host/Program.cs`
- Modify: `src/DigitalBrain.Testing/DigitalBrainTestBuilder.cs`
- Modify: `tests/DigitalBrain.OperatingSystem.Tests/OperatingSystemFixture.cs`
- Modify: `tests/DigitalBrain.Integrations.Tests/IntegrationsFixture.cs`
- Test: `tests/DigitalBrain.HostTests/ProductOperatingSystemTopology.cs`

**Interfaces:**
- Consumes: embedded signed built-in definitions and selected module catalog.
- Produces: explicit `brain.AddOperatingSystem<DigitalBrainOperatingSystem>()` registration.

- [ ] **Step 1: Write topology and missing-module tests**

```csharp
[Fact]
public void ProductAppHostSelectsExactlyOneOperatingSystem()
    => Assert.Equal(
        "DigitalBrain.OperatingSystem",
        fixture.ProductBrain.OperatingSystemId);

[Fact]
public void OsRegistrationFailsWhenRequiredModuleContractsAreMissing()
    => Assert.Throws<BehaviorDependencyException>(
        () => fixture.BuildWithoutFlutter());
```

- [ ] **Step 2: Run topology tests and verify failure**

Run: `dotnet test tests/DigitalBrain.HostTests/DigitalBrain.HostTests.csproj -c Release --filter "FullyQualifiedName~ProductOperatingSystemTopology"`

Expected: FAIL because OS registration is absent.

- [ ] **Step 3: Add explicit OS registration**

`DigitalBrainOperatingSystem` enumerates exact embedded definition digests and required module
contract/version ranges. Hosting validates the compiled module catalog at startup, registers the
built-in definitions with admission/artifact services, and makes the signed defaults available
for owner activation. AppHost explicitly selects the OS after modules; Host wires runtime services
and blob/sandbox references. Test builders select the same OS, not a second simulator.

- [ ] **Step 4: Run host and OS tests**

Run: `dotnet test tests/DigitalBrain.HostTests/DigitalBrain.HostTests.csproj -c Release --filter "FullyQualifiedName~ProductOperatingSystemTopology"; dotnet test tests/DigitalBrain.OperatingSystem.Tests/DigitalBrain.OperatingSystem.Tests.csproj -c Release`

Expected: PASS.

- [ ] **Step 5: Commit**

```powershell
git add os/DigitalBrain.OperatingSystem src/DigitalBrain.Aspire.Hosting src/DigitalBrain.Testing hosts tests
git commit -m "feat(hosting): register the built-in behavior operating system"
```

### Task 5: Delete the compiled composition and account-process projects

**Files:**
- Delete: `samples/DigitalBrain.Compositions/DigitalBrain.Compositions.csproj`
- Delete: `samples/DigitalBrain.Compositions/Shell/ActivateDigitalBrain.cs`
- Delete: `samples/DigitalBrain.Compositions/Shell/BootOnActivation.cs`
- Delete: `samples/DigitalBrain.Compositions/Shell/NavigateShell.cs`
- Delete: `samples/DigitalBrain.Compositions/Shell/OpenHome.cs`
- Delete: `samples/DigitalBrain.Compositions/Shell/PostAuthBootstrap.cs`
- Delete: `samples/DigitalBrain.Compositions/Surfaces/AccountEnrichmentSurface.cs`
- Delete: `samples/DigitalBrain.Compositions/Surfaces/AiPaneSurface.cs`
- Delete: `samples/DigitalBrain.Compositions/Surfaces/CountdownSurface.cs`
- Delete: `samples/DigitalBrain.AccountEnrichment/DigitalBrain.AccountEnrichment.csproj`
- Delete: `samples/DigitalBrain.AccountEnrichment/IAccountEnrichment.cs`
- Delete: `samples/DigitalBrain.AccountEnrichment/EnrichmentModule.cs`
- Delete: `samples/DigitalBrain.AccountEnrichment/AccountEnrichment.cs`
- Delete: `samples/DigitalBrain.AccountEnrichment/AccountEnrichmentFacts.cs`
- Delete: `tests/DigitalBrain.Integrations.Tests/AccountEnrichmentComposition.cs`
- Delete: `tests/DigitalBrain.Tests/Packages/AccountEnrichmentSampleContracts.cs`
- Delete: `tests/DigitalBrain.Tests/Boundary/CompositionBoundaryContracts.cs`
- Modify: `tests/DigitalBrain.Integrations.Tests/DigitalBrain.Integrations.Tests.csproj`
- Modify: `tests/DigitalBrain.Integrations.Tests/IntegrationsFixture.cs`
- Modify: `tests/DigitalBrain.Tests/Packages/PackageInventory.cs`
- Modify: `tests/DigitalBrain.Tests/Packages/ResidualPackageGraphContracts.cs`
- Modify: `DigitalBrain.slnx`

**Interfaces:**
- Consumes: green Tasks 1–4 product scenarios.
- Produces: no compiled composition/process path or obsolete package/test pins.

- [ ] **Step 1: Re-run deletion gates immediately before removal**

Run:

```powershell
dotnet test tests/DigitalBrain.OperatingSystem.Tests/DigitalBrain.OperatingSystem.Tests.csproj -c Release
dotnet test tests/DigitalBrain.Integrations.Tests/DigitalBrain.Integrations.Tests.csproj -c Release --filter "FullyQualifiedName~GmailReadMessage|FullyQualifiedName~SalesforceMutation"
```

Expected: PASS.

- [ ] **Step 2: Delete projects, code, obsolete tests, and all references**

Remove the projects from `DigitalBrain.slnx`, fixtures, project references, package inventory, and
boundary allowlists. Keep Google/Salesforce/Flutter/Time/AI modules; delete only composition/process
logic now represented by OS artifacts.

- [ ] **Step 3: Prove no production/test references remain**

Run:

```powershell
rg -n "OpenHomeOnActivationBehavior|IAccountEnrichment|EnrichmentModule|DigitalBrain\.Compositions|ActivateDigitalBrain|BootOnActivation|OpenHome|PostAuthBootstrap" src modules hosts os samples tests DigitalBrain.slnx
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --no-build
```

Expected: search returns no matches; build and tests PASS.

- [ ] **Step 4: Commit the deletion**

```powershell
git add DigitalBrain.slnx src modules hosts os samples tests
git add -u samples/DigitalBrain.Compositions samples/DigitalBrain.AccountEnrichment tests
git commit -m "refactor(os): delete compiled composition and process paths"
```

### Task 6: Rewrite current documentation and archive superseded decisions

**Files:**
- Modify: `README.md`
- Modify: `CLAUDE.md`
- Modify: `docs/index.md`
- Modify: `docs/architecture.md`
- Modify: `docs/concepts.md`
- Modify: `docs/packages.md`
- Modify: `docs/quickstart.md`
- Modify: `docs/specification.md`
- Modify: `docs/contributing.md`
- Modify: `docs/.vitepress/config.mts`
- Move: `docs/superpowers/specs/2026-07-25-behavior-os-*.md` → `docs/archive/superpowers/specs/`
- Move: `docs/superpowers/specs/2026-07-25-200-grill-scorecard.md` → `docs/archive/superpowers/specs/`
- Move: `docs/superpowers/specs/2026-07-25-test-truth-scorecard.md` → `docs/archive/superpowers/specs/`
- Test: `docs/tests/content.test.mjs`
- Test: `docs/tests/links.test.mjs`

**Interfaces:**
- Consumes: actual final package graph, runtime APIs, tests, and security claims.
- Produces: current reader docs plus clearly labeled historical evidence.

- [ ] **Step 1: Strengthen documentation truth tests**

```javascript
test("current docs describe the built Behavior rail", async () => {
  const current = await readCurrentDocs();
  assert.doesNotMatch(current, /Behavior rail.*unbuilt|pre-rail compositions|compiled OS Behavior/i);
  assert.match(current, /BehaviorNeuron/);
  assert.match(current, /LPAC/);
  assert.match(current, /human approval/);
});
```

- [ ] **Step 2: Run docs tests and verify failure**

Run: `npm --prefix docs test`

Expected: FAIL on stale current-state claims.

- [ ] **Step 3: Rewrite current docs from the code**

Document framework/modules/OS ownership, packages, one-file authoring, compiler/admission, artifact
identity, catalog installation, event/intent execution, capability replay, Windows security tier,
assistant proposal/approval boundary, hosting, test strategy, and both product paths. Separate
“Built” from the only deferred item: hostile hosted multi-tenant isolation/vector adoption.

Archive superseded grills/scorecards with a generated archive index stating they are historical
decision evidence and may name deleted types. Update links from the approved 2026-07-26 spec.

- [ ] **Step 4: Run docs tests and build**

Run: `npm --prefix docs test; npm --prefix docs run build`

Expected: both exit `0`.

- [ ] **Step 5: Commit**

```powershell
git add README.md CLAUDE.md docs
git commit -m "docs: describe the built behavior operating system"
```

### Task 7: Remove repository trash and prove one coherent system

**Files:**
- Modify only where audit finds an intentional current-state defect.
- Delete any discovered empty directory, checked-in output, temporary artifact, dead helper, obsolete sample, duplicate current doc, or commented-out implementation.
- Modify: `docs/architecture/behavior-os-implementation-ledger.md`

**Interfaces:**
- Consumes: all five plans.
- Produces: completed ledger and root proof with no unowned residual.

- [ ] **Step 1: Audit retired names and forbidden runtime shortcuts**

Run:

```powershell
rg -n "OpenHomeOnActivationBehavior|IAccountEnrichment|EnrichmentModule|DigitalBrain\.Compositions|ActivateDigitalBrain|BootOnActivation|OpenHome|PostAuthBootstrap" src modules hosts os samples tests README.md CLAUDE.md docs --glob "!docs/archive/**" --glob "!docs/research/**" --glob "!docs/superpowers/plans/**"
rg -n "Type\.FullName|GetType\(\)\.FullName" src/DigitalBrain.Kernel src/DigitalBrain.SourceGeneration
rg -n "DispatchProxy|MethodInfo\.Invoke|IClusterClient|Microsoft\.Orleans\.Client" hosts/DigitalBrain.BehaviorWorker src/DigitalBrain.Behaviors.Windows
rg -n "NotImplementedException|throw new NotSupportedException|HACK|TEMP" src modules hosts os samples tests
git ls-files | rg "(^|/)(bin|obj|TestResults|artifacts|\.vs|\.idea)/|\.user$|\.suo$"
```

Expected: no unintended matches. Fix an actual match by deleting or replacing its cause; do not
weaken the search.

- [ ] **Step 2: Audit project graph and filesystem ownership**

Run:

```powershell
dotnet sln DigitalBrain.slnx list
dotnet list DigitalBrain.slnx reference
dotnet list DigitalBrain.slnx package --include-transitive
git status --short
```

Verify every project has a documented owner, every direct package is used, all Orleans versions are
coherent, no empty project/folder remains, and status contains only the current audit edits.

- [ ] **Step 3: Run the complete release gate**

Run:

```powershell
dotnet format DigitalBrain.slnx --verify-no-changes
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --no-build
npm --prefix docs test
npm --prefix docs run build
git diff --check
```

Expected: every command exits `0`; no test filter or skip hides a failing project.

- [ ] **Step 4: Complete the ledger with exact commits and evidence**

Replace every `pending` cell in `docs/architecture/behavior-os-implementation-ledger.md` with the
completion commit and root-gate evidence date. Link the security threat model and the final OS BDD
features.

- [ ] **Step 5: Commit the audit**

```powershell
git add .
git commit -m "chore: finish behavior operating system repository audit"
```

- [ ] **Step 6: Verify the final commit is clean**

Run: `git status --short; git log -6 --oneline`

Expected: no status entries and the five slice completions plus final audit are visible.
