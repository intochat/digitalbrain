# Behavior OS Migration and Cleanup — Cutover

> **Status:** Designed/current. This responsibility record is part of the [OS migration/cleanup plan index](2026-07-26-behavior-os-migration-and-cleanup.md); Task 5 remains parity-gated and cannot be executed merely because a replacement compiles.

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
