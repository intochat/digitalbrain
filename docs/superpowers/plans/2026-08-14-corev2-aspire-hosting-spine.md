# CoreV2 Aspire Hosting Spine Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Start a clean CoreV2 Aspire graph containing persistent Orleans infrastructure, a dedicated runtime silo, ProductHost as an Orleans client, and Flutter launched through its module-owned Aspire extension, with build and startup evidence independent of product operations.

**Architecture:** `Brain.Aspire.Hosting` owns the AppHost resource and module-projection model, while `Brain.Aspire` owns application-side silo/client configuration. `DigitalBrain.RuntimeHost` is the only silo process, `DigitalBrain.ProductHost` remains a stateless transport client, and `Brain.Modules.UI.Aspire.Hosting` launches Flutter from the UI module projection. This plan deliberately stops before migrating a business operation.

**Tech Stack:** .NET SDK 11.0.100-preview.6.26359.118, C#, Aspire CLI 13.4.6, repository-compatible Aspire Hosting packages, Orleans 10.2.2, Azure Storage/Azurite, OpenTelemetry, xUnit v3.

## Global Constraints

- Work only in `E:\intochat\digitalbrain\.worktrees\corev2-product` on `codex/corev2-product`.
- Preserve user-authored work unless a file is explicitly superseded by this approved migration.
- No active CoreV2 project may reference `src/Kernel`, `src/Modules`, or a V1 `DigitalBrain.*` assembly.
- ProductHost must use an Orleans client and must not host a silo or own Core durable state.
- AppHost projects contain resource composition only; runtime registration belongs to application-side extensions.
- Package versions are centrally managed in `Directory.Packages.props`; ordinary `PackageReference` items contain no `Version`.
- One top-level class, record, struct, interface, or enum per matching source file.
- Every task begins with a failing focused test or build assertion, ends with focused verification, and is committed separately.
- Use `aspire start --isolated --non-interactive`, `aspire wait`, and `aspire describe` for runtime validation.

---

## File Structure

```text
src/CoreV2/Aspire/
  Brain.ServiceDefaults/
    Brain.ServiceDefaults.csproj
    ServiceDefaultsExtensions.cs
  Brain.Aspire.Hosting/
    Brain.Aspire.Hosting.csproj
    Brain/DigitalBrainBuilder.cs
    Brain/DigitalBrainClientReference.cs
    Brain/DigitalBrainHostingExtensions.cs
    Brain/DigitalBrainNames.cs
    Modules/DigitalBrainModuleBuilder.cs
    Modules/DigitalBrainModuleProjection.cs
  Brain.Aspire/
    Brain.Aspire.csproj
    DigitalBrainRuntimeHostingExtensions.cs
    DigitalBrainClientHostingExtensions.cs
    DigitalBrainResourceNames.cs
src/CoreV2/DigitalBrain.RuntimeHost/
  DigitalBrain.RuntimeHost.csproj
  Program.cs
src/CoreV2/DigitalBrain.AppHost/
  DigitalBrain.AppHost.csproj
  AppHost.cs
  ProductResources.cs
  Properties/launchSettings.json
src/CoreV2/Modules/UI/
  Brain.Modules.UI.csproj
  UiModule.cs
src/CoreV2/Modules/UI.Aspire.Hosting/
  Brain.Modules.UI.Aspire.Hosting.csproj
  FlutterHostKind.cs
  FlutterHostLaunch.cs
  FlutterHostOptions.cs
  ShellHostingExtensions.cs
  ShellNames.cs
src/CoreV2/UI/Flutter/
  core/
  shell/
tests/CoreV2/Brain.Aspire.Hosting.Tests/
tests/CoreV2/Brain.Aspire.Tests/
status.md
aspire.config.json
```

`DigitalBrain.ProductHost` gains only executable startup, ServiceDefaults, Orleans-client registration, and health endpoints in this plan.

---

### Task 1: Replace the broken generated scaffold with a green CoreV2 baseline

**Files:**

- Create: `status.md`
- Modify: `aspire.config.json`
- Modify: `DigitalBrain.slnx`
- Delete: `DigitalBrain.AppHost/aspire.config.json`
- Delete: `DigitalBrain.AppHost/DigitalBrain.AppHost.AppHost/*`
- Delete: `DigitalBrain.AppHost/DigitalBrain.AppHost.ServiceDefaults/*`
- Create: `src/CoreV2/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`
- Create: `src/CoreV2/DigitalBrain.AppHost/AppHost.cs`
- Create: `src/CoreV2/DigitalBrain.AppHost/Properties/launchSettings.json`

**Interfaces:**

- Consumes: repository-wide central package management and global .NET target.
- Produces: exactly one tracked AppHost location and an honest migration baseline.

- [ ] **Step 1: Record the existing red build.**

Run:

```powershell
dotnet build DigitalBrain.slnx -c Release --no-restore --nologo
```

Expected: FAIL with `NU1008` from the generated ServiceDefaults project because it embeds package versions under central package management.

- [ ] **Step 2: Write the corrected status.**

Create `status.md` with these states:

```markdown
# CoreV2 migration status

## Verified
- Framework proof and product authority/catalog boundary.

## In progress
- Aspire/Orleans hosting spine.

## Not yet production-hosted
- Durable distributed operations, product transports, Flutter, and modules.

## Superseded
- ProductHost-local EF persistence and the monolithic twenty-task cutover sequence.
```

Include current branch/HEAD, the reverted experiment, and the rule that a task is complete only after its current verification command passes.

- [ ] **Step 3: Rehome the AppHost skeleton.**

Create `src/CoreV2/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`:

```xml
<Project Sdk="Aspire.AppHost.Sdk/13.4.6">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <UserSecretsId>digitalbrain-corev2-apphost</UserSecretsId>
  </PropertyGroup>
</Project>
```

Create the minimal `AppHost.cs`:

```csharp
var builder = DistributedApplication.CreateBuilder(args);
builder.Build().Run();
```

Update root `aspire.config.json` to point only at this project. Remove the superseded generated scaffold source and its solution entries; do not remove unrelated user files.

- [ ] **Step 4: Verify the baseline builds.**

Run:

```powershell
dotnet restore DigitalBrain.slnx --nologo
dotnet build DigitalBrain.slnx -c Release --no-restore --nologo
git diff --check
```

Expected: PASS with zero warnings and errors.

- [ ] **Step 5: Commit.**

```powershell
git add status.md aspire.config.json DigitalBrain.slnx src/CoreV2/DigitalBrain.AppHost DigitalBrain.AppHost
git commit -m "chore(aspire): establish CoreV2 AppHost baseline"
```

### Task 2: Add shared ServiceDefaults without package-version drift

**Files:**

- Create: `src/CoreV2/Aspire/Brain.ServiceDefaults/Brain.ServiceDefaults.csproj`
- Create: `src/CoreV2/Aspire/Brain.ServiceDefaults/ServiceDefaultsExtensions.cs`
- Modify: `DigitalBrain.slnx`
- Create: `tests/CoreV2/Brain.Aspire.Tests/Brain.Aspire.Tests.csproj`
- Create: `tests/CoreV2/Brain.Aspire.Tests/ServiceDefaultsTests.cs`

**Interfaces:**

- Produces: `AddServiceDefaults<TBuilder>()` and `MapDefaultEndpoints(WebApplication)`.
- Consumes: centrally managed resilience, service discovery, OpenTelemetry, and health-check packages.

- [ ] **Step 1: Write the failing registration test.**

```csharp
[Fact]
public void Service_defaults_register_health_checks_and_service_discovery()
{
    var builder = Host.CreateApplicationBuilder();
    builder.AddServiceDefaults();
    using var host = builder.Build();

    Assert.NotNull(host.Services.GetService<HealthCheckService>());
    Assert.NotNull(host.Services.GetService<IServiceEndpointWatcherFactory>());
}
```

- [ ] **Step 2: Run the test and prove it is red.**

```powershell
dotnet test tests/CoreV2/Brain.Aspire.Tests/Brain.Aspire.Tests.csproj --filter FullyQualifiedName~ServiceDefaultsTests
```

Expected: FAIL because `AddServiceDefaults` does not exist.

- [ ] **Step 3: Implement the focused defaults.**

Register OpenTelemetry logging/metrics/tracing, Orleans meters/sources, service discovery, standard HTTP resilience, and a `self` liveness check. Map `/health` and `/alive` only through `MapDefaultEndpoints`.

- [ ] **Step 4: Verify and commit.**

```powershell
dotnet test tests/CoreV2/Brain.Aspire.Tests/Brain.Aspire.Tests.csproj --filter FullyQualifiedName~ServiceDefaultsTests
dotnet build DigitalBrain.slnx -c Release --no-restore --nologo
git add src/CoreV2/Aspire/Brain.ServiceDefaults tests/CoreV2/Brain.Aspire.Tests DigitalBrain.slnx
git commit -m "feat(aspire): add CoreV2 service defaults"
```

### Task 3: Model the DigitalBrain AppHost resource

**Files:**

- Create: `src/CoreV2/Aspire/Brain.Aspire.Hosting/Brain.Aspire.Hosting.csproj`
- Create: `src/CoreV2/Aspire/Brain.Aspire.Hosting/Brain/DigitalBrainNames.cs`
- Create: `src/CoreV2/Aspire/Brain.Aspire.Hosting/Brain/DigitalBrainBuilder.cs`
- Create: `src/CoreV2/Aspire/Brain.Aspire.Hosting/Brain/DigitalBrainClientReference.cs`
- Create: `src/CoreV2/Aspire/Brain.Aspire.Hosting/Brain/DigitalBrainHostingExtensions.cs`
- Create: `src/CoreV2/Aspire/Brain.Aspire.Hosting/Modules/DigitalBrainModuleBuilder.cs`
- Create: `src/CoreV2/Aspire/Brain.Aspire.Hosting/Modules/DigitalBrainModuleProjection.cs`
- Create: `tests/CoreV2/Brain.Aspire.Hosting.Tests/Brain.Aspire.Hosting.Tests.csproj`
- Create: `tests/CoreV2/Brain.Aspire.Hosting.Tests/DigitalBrainResourceModelTests.cs`
- Modify: `DigitalBrain.slnx`

**Interfaces:**

- Produces: `AddDigitalBrain(string)`, `DigitalBrainBuilder`, `DigitalBrainClientReference`, `AddModule<TModule>`, module projections, `WithReference(brain)`, and `WithReference(brain.AsClient())`.
- Consumes: Aspire Orleans and Azure Storage hosting integrations.

- [ ] **Step 1: Write failing resource-model tests.**

```csharp
[Fact]
public void AddDigitalBrain_declares_the_complete_durable_fabric()
{
    var builder = DistributedApplication.CreateBuilder();
    _ = builder.AddDigitalBrain("brain");

    var names = builder.Resources.Select(resource => resource.Name).ToArray();
    Assert.Contains("storage", names);
    Assert.Contains("clustering", names);
    Assert.Contains("reminders", names);
    Assert.Contains("grainstate", names);
    Assert.Contains("brain", names);
}

[Fact]
public void Client_reference_is_distinct_from_silo_reference()
{
    var builder = DistributedApplication.CreateBuilder();
    var brain = builder.AddDigitalBrain("brain");
    Assert.IsType<DigitalBrainClientReference>(brain.AsClient());
}

[Fact]
public void Module_projection_is_applied_to_the_client_resource()
{
    var builder = DistributedApplication.CreateBuilder();
    var brain = builder.AddDigitalBrain("brain");
    var projection = new RecordingProjection();
    brain.AddModule<Marker>(module => module.AddProjection(projection));
    builder.AddExecutable("client", "dotnet").WithReference(brain.AsClient());
    Assert.True(projection.ClientApplied);
}
```

- [ ] **Step 2: Run the tests and prove they are red.**

```powershell
dotnet test tests/CoreV2/Brain.Aspire.Hosting.Tests/Brain.Aspire.Hosting.Tests.csproj
```

- [ ] **Step 3: Implement the smallest resource model.**

`AddDigitalBrain` must build this graph:

```csharp
var storage = builder.AddAzureStorage("storage")
    .RunAsEmulator(emulator => emulator.WithDataVolume().WithLifetime(ContainerLifetime.Persistent));
var clustering = storage.AddTables("clustering");
var reminders = storage.AddTables("reminders");
var grainState = storage.AddBlobs("grainstate");
var orleans = builder.AddOrleans(name)
    .WithClustering(clustering)
    .WithReminders(reminders)
    .WithGrainStorage("Default", grainState);
```

Silo references use `orleans`; client references use `orleans.AsClient()`. Both receive explicit health waits for required storage resources. Module projections expose separate runtime/client application methods so the UI launcher can bind to ProductHost without contaminating the silo. Do not add streams, pub/sub, OAuth, AI, Memory, or Flutter resources in this task.

- [ ] **Step 4: Verify and commit.**

```powershell
dotnet test tests/CoreV2/Brain.Aspire.Hosting.Tests/Brain.Aspire.Hosting.Tests.csproj
dotnet build DigitalBrain.slnx -c Release --no-restore --nologo
git add src/CoreV2/Aspire/Brain.Aspire.Hosting tests/CoreV2/Brain.Aspire.Hosting.Tests DigitalBrain.slnx
git commit -m "feat(aspire): model the CoreV2 DigitalBrain resource"
```

### Task 4: Add application-side silo and client hosting extensions

**Files:**

- Create: `src/CoreV2/Aspire/Brain.Aspire/Brain.Aspire.csproj`
- Create: `src/CoreV2/Aspire/Brain.Aspire/DigitalBrainResourceNames.cs`
- Create: `src/CoreV2/Aspire/Brain.Aspire/DigitalBrainRuntimeHostingExtensions.cs`
- Create: `src/CoreV2/Aspire/Brain.Aspire/DigitalBrainClientHostingExtensions.cs`
- Create: `tests/CoreV2/Brain.Aspire.Tests/DigitalBrainHostingTests.cs`
- Modify: `DigitalBrain.slnx`

**Interfaces:**

- Produces: `AddDigitalBrainRuntime(IHostApplicationBuilder)` and `AddDigitalBrainClient(IHostApplicationBuilder)`.
- Consumes: ServiceDefaults, keyed Azure Storage clients, Orleans server/client hosting.

- [ ] **Step 1: Write failing extension tests.**

```csharp
[Fact]
public void Runtime_extension_configures_a_silo_and_default_storage()
{
    var builder = Host.CreateApplicationBuilder();
    builder.AddDigitalBrainRuntime();
    using var host = builder.Build();
    Assert.NotNull(host.Services.GetService<ISiloHost>());
}

[Fact]
public void Client_extension_never_registers_a_silo()
{
    var builder = Host.CreateApplicationBuilder();
    builder.AddDigitalBrainClient();
    using var host = builder.Build();
    Assert.Null(host.Services.GetService<ISiloHost>());
    Assert.NotNull(host.Services.GetService<IClusterClient>());
}
```

- [ ] **Step 2: Run the tests and prove they are red.**

```powershell
dotnet test tests/CoreV2/Brain.Aspire.Tests/Brain.Aspire.Tests.csproj --filter FullyQualifiedName~DigitalBrainHostingTests
```

- [ ] **Step 3: Implement server/client registration.**

The runtime extension calls `AddServiceDefaults`, registers keyed clients named `clustering`, `reminders`, and `grainstate`, and calls `UseOrleans`. The client extension calls `AddServiceDefaults`, registers only `clustering`, and calls `UseOrleansClient`. Keep module registration outside these extensions.

- [ ] **Step 4: Verify and commit.**

```powershell
dotnet test tests/CoreV2/Brain.Aspire.Tests/Brain.Aspire.Tests.csproj --filter FullyQualifiedName~DigitalBrainHostingTests
dotnet build DigitalBrain.slnx -c Release --no-restore --nologo
git add src/CoreV2/Aspire/Brain.Aspire tests/CoreV2/Brain.Aspire.Tests DigitalBrain.slnx
git commit -m "feat(aspire): add CoreV2 silo and client hosting"
```

### Task 5: Add the runtime process and make ProductHost an executable client

**Files:**

- Create: `src/CoreV2/DigitalBrain.RuntimeHost/DigitalBrain.RuntimeHost.csproj`
- Create: `src/CoreV2/DigitalBrain.RuntimeHost/Program.cs`
- Modify: `src/CoreV2/DigitalBrain.ProductHost/DigitalBrain.ProductHost.csproj`
- Create: `src/CoreV2/DigitalBrain.ProductHost/Program.cs`
- Create: `tests/CoreV2/Brain.Aspire.Tests/ProcessCompositionTests.cs`
- Modify: `DigitalBrain.slnx`

**Interfaces:**

- Produces: two independently hosted processes with `/health` and `/alive`.
- Consumes: `AddDigitalBrainRuntime`, `AddDigitalBrainClient`, and ServiceDefaults.

- [ ] **Step 1: Write failing project-boundary tests.**

```csharp
[Fact]
public void ProductHost_references_client_hosting_but_not_orleans_server()
{
    var references = ProjectReferenceScanner.Read("src/CoreV2/DigitalBrain.ProductHost/DigitalBrain.ProductHost.csproj");
    Assert.Contains("Brain.Aspire", references);
    Assert.DoesNotContain("Microsoft.Orleans.Server", references);
}
```

- [ ] **Step 2: Run the test and prove it is red.**

```powershell
dotnet test tests/CoreV2/Brain.Aspire.Tests/Brain.Aspire.Tests.csproj --filter FullyQualifiedName~ProcessCompositionTests
```

- [ ] **Step 3: Add minimal process entry points.**

RuntimeHost:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddDigitalBrainRuntime();
var app = builder.Build();
app.MapDefaultEndpoints();
app.Run();
```

ProductHost:

```csharp
var builder = WebApplication.CreateBuilder(args);
builder.AddDigitalBrainClient();
var app = builder.Build();
app.MapDefaultEndpoints();
app.Run();
```

- [ ] **Step 4: Verify and commit.**

```powershell
dotnet test tests/CoreV2/Brain.Aspire.Tests/Brain.Aspire.Tests.csproj --filter FullyQualifiedName~ProcessCompositionTests
dotnet build DigitalBrain.slnx -c Release --no-restore --nologo
git add src/CoreV2/DigitalBrain.RuntimeHost src/CoreV2/DigitalBrain.ProductHost tests/CoreV2/Brain.Aspire.Tests DigitalBrain.slnx
git commit -m "feat(product): add runtime silo and client processes"
```

### Task 6: Compose and validate the Aspire hosting spine

**Files:**

- Modify: `src/CoreV2/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`
- Modify: `src/CoreV2/DigitalBrain.AppHost/AppHost.cs`
- Create: `src/CoreV2/DigitalBrain.AppHost/ProductResources.cs`
- Create: `tests/CoreV2/Brain.Aspire.Hosting.Tests/AppHostCompositionTests.cs`
- Modify: `status.md`

**Interfaces:**

- Consumes: DigitalBrain resource model, RuntimeHost, and ProductHost.
- Produces: storage → runtime → ProductHost startup graph and runtime evidence.

- [ ] **Step 1: Write the failing AppHost graph test.**

```csharp
[Fact]
public async Task AppHost_declares_runtime_and_client_with_correct_dependencies()
{
    var appHost = await DistributedApplicationTestingBuilder
        .CreateAsync<Projects.DigitalBrain_AppHost>();
    var resources = appHost.Resources.ToDictionary(resource => resource.Name);
    Assert.Contains("runtime", resources.Keys);
    Assert.Contains("product", resources.Keys);
}
```

- [ ] **Step 2: Run the test and prove it is red.**

```powershell
dotnet test tests/CoreV2/Brain.Aspire.Hosting.Tests/Brain.Aspire.Hosting.Tests.csproj --filter FullyQualifiedName~AppHostCompositionTests
```

- [ ] **Step 3: Compose the graph.**

```csharp
var builder = DistributedApplication.CreateBuilder(args);
var brain = builder.AddDigitalBrain("brain");
var runtime = builder.AddProject<Projects.DigitalBrain_RuntimeHost>("runtime")
    .WithReference(brain)
    .WithHttpHealthCheck("/health");
builder.AddProject<Projects.DigitalBrain_ProductHost>("product")
    .WithReference(brain.AsClient())
    .WithHttpHealthCheck("/health")
    .WaitFor(runtime);
builder.Build().Run();
```

- [ ] **Step 4: Pass the automated graph test.**

```powershell
dotnet test tests/CoreV2/Brain.Aspire.Hosting.Tests/Brain.Aspire.Hosting.Tests.csproj --filter FullyQualifiedName~AppHostCompositionTests
```

- [ ] **Step 5: Validate the live graph.**

Run:

```powershell
aspire start --isolated --non-interactive --format Json
aspire wait storage
aspire wait runtime
aspire wait product
aspire describe --format Json
```

Expected: storage, runtime, and product reach healthy/running states. Stop through Aspire after capturing evidence.

- [ ] **Step 6: Update status and commit.**

Mark only the hosting spine verified. Keep durable operations, HTTP product protocol, Flutter, and module migration under Not Started.

```powershell
dotnet build DigitalBrain.slnx -c Release --no-restore --nologo
git diff --check
git add src/CoreV2/DigitalBrain.AppHost tests/CoreV2/Brain.Aspire.Hosting.Tests status.md
git commit -m "feat(aspire): compose the CoreV2 hosting spine"
```

### Task 7: Restore Flutter as a module-owned Aspire resource

**Files:**

- Create: `src/CoreV2/Modules/UI/Brain.Modules.UI.csproj`
- Create: `src/CoreV2/Modules/UI/UiModule.cs`
- Create: `src/CoreV2/Modules/UI.Aspire.Hosting/Brain.Modules.UI.Aspire.Hosting.csproj`
- Create: `src/CoreV2/Modules/UI.Aspire.Hosting/FlutterHostKind.cs`
- Create: `src/CoreV2/Modules/UI.Aspire.Hosting/FlutterHostOptions.cs`
- Create: `src/CoreV2/Modules/UI.Aspire.Hosting/FlutterHostLaunch.cs`
- Create: `src/CoreV2/Modules/UI.Aspire.Hosting/ShellNames.cs`
- Create: `src/CoreV2/Modules/UI.Aspire.Hosting/ShellHostingExtensions.cs`
- Create: `src/CoreV2/UI/Flutter/core/pubspec.yaml`
- Create: `src/CoreV2/UI/Flutter/core/lib/digitalbrain_flutter.dart`
- Create: `src/CoreV2/UI/Flutter/core/lib/src/host_environment.dart`
- Create: `src/CoreV2/UI/Flutter/core/test/host_environment_test.dart`
- Create: `src/CoreV2/UI/Flutter/shell/pubspec.yaml`
- Create: `src/CoreV2/UI/Flutter/shell/lib/main.dart`
- Create: `src/CoreV2/UI/Flutter/shell/test/shell_smoke_test.dart`
- Create: `tests/CoreV2/Brain.Aspire.Hosting.Tests/FlutterHostingExtensionsTests.cs`
- Modify: `src/CoreV2/DigitalBrain.AppHost/AppHost.cs`
- Modify: `src/CoreV2/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`
- Modify: `DigitalBrain.slnx`

**Interfaces:**

- Consumes: `DigitalBrainModuleBuilder<UiModule>` and the ProductHost HTTP endpoint.
- Produces: `WithWindowHost`, `WithWebHost`, `WithHeadlessHost`, and one Aspire `flutter` executable resource.

- [ ] **Step 1: Write failing Flutter hosting tests.**

```csharp
[Fact]
public void Window_host_adds_exactly_one_flutter_executable()
{
    var builder = DistributedApplication.CreateBuilder();
    var brain = builder.AddDigitalBrain("brain");
    brain.AddModule<UiModule>(ui => ui.WithWindowHost(options => options.WorkingDirectory = FlutterFixture.Root));
    builder.AddExecutable("product", "dotnet").WithHttpEndpoint(name: "http").WithReference(brain.AsClient());
    Assert.Single(builder.Resources.Where(resource => resource.Name == "flutter"));
}

[Fact]
public void A_second_flutter_host_is_rejected()
{
    var builder = DistributedApplication.CreateBuilder();
    var brain = builder.AddDigitalBrain("brain");
    Assert.Throws<InvalidOperationException>(() => brain.AddModule<UiModule>(ui =>
    {
        ui.WithWindowHost(options => options.WorkingDirectory = FlutterFixture.Root);
        ui.WithWebHost(options => options.WorkingDirectory = FlutterFixture.Root);
    }));
}
```

- [ ] **Step 2: Run the tests and prove they are red.**

```powershell
dotnet test tests/CoreV2/Brain.Aspire.Hosting.Tests/Brain.Aspire.Hosting.Tests.csproj --filter FullyQualifiedName~FlutterHostingExtensionsTests
```

- [ ] **Step 3: Implement the module-owned launcher.**

Preserve the master-facing composition:

```csharp
brain.AddModule<UiModule>(ui => ui.WithWindowHost());
```

The projection adds one `flutter` executable, selects `flutter run -d windows`, `flutter run -d chrome`, or `dart run` according to host kind, injects `DIGITALBRAIN_PRODUCT_BASE` from ProductHost's `http` endpoint, and applies a health wait on ProductHost. Window and web modes add a dashboard hot-reload command. Command and working-directory discovery must provide actionable failures and accept explicit overrides for tests and CI.

- [ ] **Step 4: Add the minimal CoreV2 Flutter packages test-first.**

`HostEnvironment.productBase` reads `DIGITALBRAIN_PRODUCT_BASE` from `--dart-define` on web and from the process environment on desktop/headless. The shell renders a Material application showing `DigitalBrain CoreV2` and the configured ProductHost origin; no V1 client model is imported.

Run:

```powershell
flutter pub get --directory src/CoreV2/UI/Flutter/core
flutter test src/CoreV2/UI/Flutter/core
flutter pub get --directory src/CoreV2/UI/Flutter/shell
flutter test src/CoreV2/UI/Flutter/shell
```

- [ ] **Step 5: Compose Flutter from the AppHost extension.**

Configure the UI module before adding ProductHost, then add ProductHost's `http` endpoint before applying `WithReference(brain.AsClient())` so the projection can bind its endpoint:

```csharp
var brain = builder.AddDigitalBrain("brain");
brain.AddModule<UiModule>(ui => ui.WithWindowHost());
var product = builder.AddProject<Projects.DigitalBrain_ProductHost>("product")
    .WithHttpEndpoint(name: "http")
    .WithReference(brain.AsClient())
    .WaitFor(runtime);
```

- [ ] **Step 6: Verify live Flutter startup and commit.**

```powershell
dotnet test tests/CoreV2/Brain.Aspire.Hosting.Tests/Brain.Aspire.Hosting.Tests.csproj --filter FullyQualifiedName~FlutterHostingExtensionsTests
flutter test src/CoreV2/UI/Flutter/core
flutter test src/CoreV2/UI/Flutter/shell
dotnet build DigitalBrain.slnx -c Release --no-restore --nologo
aspire start --isolated --non-interactive --format Json
aspire wait runtime
aspire wait product
aspire wait flutter
aspire describe --format Json
```

Expected: Flutter is declared by `Brain.Modules.UI.Aspire.Hosting`, starts after ProductHost, and receives the allocated ProductHost endpoint. Stop through Aspire after verification.

```powershell
git add src/CoreV2/Modules/UI src/CoreV2/Modules/UI.Aspire.Hosting src/CoreV2/UI/Flutter src/CoreV2/DigitalBrain.AppHost tests/CoreV2/Brain.Aspire.Hosting.Tests DigitalBrain.slnx
git commit -m "feat(flutter): restore module-owned Aspire hosting"
```

## Self-Review

### Specification coverage

- Honest baseline and scaffold repair: Task 1.
- Shared telemetry/health/service discovery: Task 2.
- Reusable AppHost-side DigitalBrain resource: Task 3.
- Separate silo/client application integrations: Task 4.
- RuntimeHost and stateless ProductHost process boundary: Task 5.
- Live Aspire graph verification: Task 6.
- Module-owned Flutter launch, endpoint injection, and hot reload: Task 7.

### Scope boundary

This plan intentionally implements only Flutter's module-owned Aspire launch and a minimal shell. It does not implement durable product operations, product HTTP routes beyond health, or product module behavior. Those receive separate plans after the hosting spine is green, preventing the migration from recreating the rejected oversized Task 5.

### Type and naming consistency

- AppHost-side type: `DigitalBrainBuilder`; client marker: `DigitalBrainClientReference`.
- Application-side methods: `AddDigitalBrainRuntime` and `AddDigitalBrainClient`.
- Stable resource names: `brain`, `storage`, `clustering`, `reminders`, `grainstate`, `runtime`, `product`, `flutter`.
- ProductHost is always a client; RuntimeHost is always a silo.
