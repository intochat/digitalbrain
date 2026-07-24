# DigitalBrain L2 AppHost Testing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace static `HostedApplication`/`HostedScenario` helpers with an exclusive xUnit `DigitalBrainAppHostFixture<TAppHost>`, a method-scoped `RunningAppHost`, and one-name `HostedResource` handles over real Aspire resource lifecycle.

**Architecture:** Concrete xUnit assembly fixtures remain cheap until a method calls `StartAsync`. A package-internal process-wide lease permits one full AppHost graph at a time, including across different fixture types. `RunningAppHost` owns one `DistributedApplication`; `HostedResource` binds a resource name once and exposes health, HTTP, logs, and restart without leaking Aspire objects. Dispose stops the graph, verifies terminal resource states, attaches bounded diagnostics, and releases exclusivity.

**Tech Stack:** .NET 10, xUnit v3 3.2.2, Aspire.Hosting.Testing 13.4.6, Microsoft.Testing.Platform.

## Global Constraints

- Execute after `2026-07-24-digitalbrain-testing-l1.md`.
- Work only in `E:\intochat\digitalbrain` on the current branch.
- Preserve the user's unstaged `Directory.Packages.props` line-ending change; never stage it.
- L2 proves AppHost composition, real resource readiness, endpoints, and process/resource restart only.
- Exactly one full AppHost graph may run in-process.
- Readiness means Aspire Healthy, not merely Running.
- No public `DistributedApplication`, resource notification service, command service, process IDs, or
  static exclusivity state.
- No hard-coded process-name enumeration and no broad process killing.
- Resource names are runtime instance labels and are supplied exactly once to `host.Resource(name)`.
- All operations accept cancellation; no blind timeout inflation.
- Diagnostics are bounded and redact environment values and secrets.
- Delete old HostedApplication/HostedScenario APIs; do not leave wrappers.

---

## File structure

### Create

| File | Responsibility |
|---|---|
| `src/DigitalBrain.Testing/Hosting/DigitalBrainAppHostFixture.cs` | xUnit fixture and exclusive graph lease |
| `src/DigitalBrain.Testing/Hosting/RunningAppHost.cs` | method-scoped application owner |
| `src/DigitalBrain.Testing/Hosting/HostedResource.cs` | one named resource operations |
| `src/DigitalBrain.Testing/Hosting/AppHostExclusiveLease.cs` | internal process-wide exclusivity |
| `src/DigitalBrain.Testing/Diagnostics/AppHostTestArtifact.cs` | resource state, endpoints, logs, cleanup |
| `tests/DigitalBrain.HostTests/AppHostFixtures.cs` | concrete production/testing AppHost fixtures |
| `tests/DigitalBrain.Tests/AppHostTestingSurfaceContracts.cs` | L0 public shape and leak guard |

### Modify

```text
tests/DigitalBrain.HostTests/AssemblyInfo.cs
tests/DigitalBrain.HostTests/HostedBrain.cs
tests/DigitalBrain.HostTests/HostedRestart.cs
tests/DigitalBrain.HostTests/Topology.cs
tests/DigitalBrain.HostTests/ProductionAppHost.cs
tests/DigitalBrain.HostTests/DigitalBrain.HostTests.csproj
src/DigitalBrain.Testing/DigitalBrain.Testing.csproj
docs/architecture.md
docs/packages.md
```

### Delete

```text
src/DigitalBrain.Testing/Hosting/HostedApplication.cs
src/DigitalBrain.Testing/Hosting/HostedScenario.cs
tests/DigitalBrain.HostTests/HostedCollection.cs
```

---

### Task 1: Pin the L2 public surface and the raw-Aspire ban

**Files:**
- Create: `tests/DigitalBrain.Tests/AppHostTestingSurfaceContracts.cs`

**Interfaces:**
- Produces: L0 guard for the approved names and boundaries

- [ ] **Step 1: Write failing public-shape tests**

```csharp
using System.Reflection;
using Aspire.Hosting;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class AppHostTestingSurfaceContracts
{
    [Fact]
    public void L2SurfaceNamesTheFixtureGraphAndResource()
    {
        var exported = typeof(DigitalBrainFixture).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("DigitalBrainAppHostFixture`1", exported);
        Assert.Contains(nameof(RunningAppHost), exported);
        Assert.Contains(nameof(HostedResource), exported);
        Assert.DoesNotContain("HostedApplication", exported);
        Assert.DoesNotContain("HostedScenario", exported);
    }

    [Fact]
    public void PublicL2MembersDoNotLeakAspireRuntimeObjects()
    {
        var exposed = typeof(RunningAppHost).Assembly
            .GetExportedTypes()
            .SelectMany(type => type.GetMembers(BindingFlags.Instance | BindingFlags.Public))
            .SelectMany(MemberTypes)
            .SelectMany(Expand)
            .Where(type => type.FullName?.StartsWith(
                "Aspire.Hosting.",
                StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Empty(exposed);
    }

    private static IEnumerable<Type> MemberTypes(MemberInfo member) => member switch
    {
        MethodInfo method => method.GetParameters()
            .Select(parameter => parameter.ParameterType)
            .Append(method.ReturnType),
        PropertyInfo property => [property.PropertyType],
        _ => [],
    };

    private static IEnumerable<Type> Expand(Type type)
    {
        yield return type;

        if (type.HasElementType)
        {
            foreach (var nested in Expand(type.GetElementType()!))
            {
                yield return nested;
            }
        }

        foreach (var argument in type.GetGenericArguments())
        {
            foreach (var nested in Expand(argument))
            {
                yield return nested;
            }
        }
    }
}
```

- [ ] **Step 2: Run and verify red**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~AppHostTestingSurfaceContracts" --logger "console;verbosity=minimal"
```

Expected: build FAIL because the new L2 types do not exist.

- [ ] **Step 3: Commit the red contract**

```powershell
git add tests/DigitalBrain.Tests/AppHostTestingSurfaceContracts.cs
git commit -m "test: pin AppHost testing surface"
```

---

### Task 2: Add exclusive generic AppHost fixtures

**Files:**
- Create: `src/DigitalBrain.Testing/Hosting/AppHostExclusiveLease.cs`
- Create: `src/DigitalBrain.Testing/Hosting/DigitalBrainAppHostFixture.cs`
- Create: `tests/DigitalBrain.HostTests/AppHostFixtures.cs`
- Modify: `tests/DigitalBrain.HostTests/AssemblyInfo.cs`
- Create: `tests/DigitalBrain.HostTests/FixtureExclusivity.cs`

**Interfaces:**
- Produces:

```csharp
public abstract class DigitalBrainAppHostFixture<TAppHost> : IAsyncLifetime
    where TAppHost : class
{
    public Task<RunningAppHost> StartAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 1: Register concrete xUnit fixtures**

```csharp
using DigitalBrain.Testing;

namespace DigitalBrain.HostTests;

public sealed class TestingAppHostFixture :
    DigitalBrainAppHostFixture<Projects.DigitalBrain_TestingAppHost>;

public sealed class ProductionAppHostFixture :
    DigitalBrainAppHostFixture<Projects.DigitalBrain_AppHost>;
```

`AssemblyInfo.cs`:

```csharp
using DigitalBrain.HostTests;
using Xunit;

[assembly: AssemblyFixture(typeof(TestingAppHostFixture))]
[assembly: AssemblyFixture(typeof(ProductionAppHostFixture))]
```

Remove assembly-wide `DisableTestParallelization`; fixture ownership must enforce exclusivity.

- [ ] **Step 2: Write exclusivity tests**

```csharp
public sealed class FixtureExclusivity(
    TestingAppHostFixture testing,
    ProductionAppHostFixture production)
{
    [Fact]
    public async Task ASecondGraphWaitsForTheFirstAcrossFixtureTypes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var first = await testing.StartAsync(cancellationToken);
        var waiting = production.StartAsync(cancellationToken);

        Assert.False(waiting.IsCompleted);
        await first.DisposeAsync();

        await using var second = await waiting;
        Assert.NotNull(second);
    }
}
```

- [ ] **Step 3: Run and verify red**

```powershell
dotnet test tests/DigitalBrain.HostTests/DigitalBrain.HostTests.csproj -c Release --filter "FullyQualifiedName~FixtureExclusivity" --logger "console;verbosity=minimal"
```

Expected: build FAIL.

- [ ] **Step 4: Implement the package-internal exclusive lease**

Use one static `SemaphoreSlim` only inside `AppHostExclusiveLease`. It exposes no state:

```csharp
internal sealed class AppHostExclusiveLease : IAsyncDisposable
{
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private int _disposed;

    private AppHostExclusiveLease() { }

    internal static async Task<AppHostExclusiveLease> AcquireAsync(
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        return new AppHostExclusiveLease();
    }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0)
        {
            Gate.Release();
        }

        return ValueTask.CompletedTask;
    }
}
```

- [ ] **Step 5: Implement lazy fixture lifetime**

`InitializeAsync` is `ValueTask.CompletedTask`. `StartAsync`:

1. acquires `AppHostExclusiveLease`;
2. calls `DistributedApplicationTestingBuilder.CreateAsync<TAppHost>` with resource logging enabled;
3. creates a linked token with the package-owned five-minute startup bound;
4. builds and starts one application using that linked token;
5. starts resource notification capture;
6. returns `RunningAppHost`;
7. disposes the application and lease if any preceding step fails.

The fixture tracks its one active handle so `DisposeAsync` fails if a method leaked it.

- [ ] **Step 6: Run exclusivity twice**

```powershell
dotnet test tests/DigitalBrain.HostTests/DigitalBrain.HostTests.csproj -c Release --filter "FullyQualifiedName~FixtureExclusivity" --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.HostTests/DigitalBrain.HostTests.csproj -c Release --filter "FullyQualifiedName~FixtureExclusivity" --logger "console;verbosity=minimal"
```

Expected: both PASS.

- [ ] **Step 7: Commit exclusive fixture ownership**

```powershell
git add src/DigitalBrain.Testing tests/DigitalBrain.HostTests
git commit -m "feat(testing): add exclusive AppHost fixture"
```

---

### Task 3: Add RunningAppHost and one-name HostedResource

**Files:**
- Create: `src/DigitalBrain.Testing/Hosting/RunningAppHost.cs`
- Create: `src/DigitalBrain.Testing/Hosting/HostedResource.cs`
- Create: `tests/DigitalBrain.HostTests/HostedResourceContracts.cs`
- Modify: `src/DigitalBrain.Testing/Hosting/DigitalBrainAppHostFixture.cs`

**Interfaces:**
- Produces:

```csharp
public sealed class RunningAppHost : IAsyncDisposable
{
    public HostedResource Resource(string name);
}

public sealed class HostedResource
{
    public string Name { get; }
    public Task WaitUntilHealthyAsync(CancellationToken cancellationToken = default);
    public HttpClient CreateHttpClient(string? endpointName = null);
    public Task RestartAsync(CancellationToken cancellationToken = default);
}
```

- [ ] **Step 1: Write resource tests**

```csharp
public sealed class HostedResourceContracts(TestingAppHostFixture fixture)
{
    [Fact]
    public async Task ResourceBindsItsNameOnceAndWaitsForHealth()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await fixture.StartAsync(cancellationToken);
        var silo = host.Resource("silo");

        await silo.WaitUntilHealthyAsync(cancellationToken);
        using var client = silo.CreateHttpClient();
        using var response = await client.GetAsync("/health", cancellationToken);

        Assert.Equal("silo", silo.Name);
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task UnknownResourceFailsWithKnownResourceState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await fixture.StartAsync(cancellationToken);

        var failure = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.Resource("missing").WaitUntilHealthyAsync(cancellationToken));

        Assert.Contains("missing", failure.Message, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run and verify red**

```powershell
dotnet test tests/DigitalBrain.HostTests/DigitalBrain.HostTests.csproj -c Release --filter "FullyQualifiedName~HostedResourceContracts" --logger "console;verbosity=minimal"
```

Expected: build FAIL.

- [ ] **Step 3: Implement the internal Aspire bridge**

`RunningAppHost` stores the `DistributedApplication` privately. Resolve
`DistributedApplicationModel` from `application.Services` once and capture its ordinal set of
resource names. `Resource(name)` validates non-whitespace, rejects a name absent from that set with
the sorted known names in the error, and caches `HostedResource` by ordinal name. Every async
operation links the caller token with the package-owned five-minute operation bound.

`HostedResource.WaitUntilHealthyAsync` calls:

```csharp
await application.ResourceNotifications
    .WaitForResourceHealthyAsync(
        Name,
        WaitBehavior.StopOnResourceUnavailable,
        cancellationToken);
```

`CreateHttpClient` calls Aspire Testing's `application.CreateHttpClient(Name, endpointName)`.

`RestartAsync` calls:

```csharp
var result = await application.ResourceCommands.ExecuteCommandAsync(
    Name,
    "resource-restart",
    cancellationToken);
```

The wire command string is owned once in a private constant. Inspect
`ExecuteCommandResult.Success`; when it is false, throw with `ErrorMessage`, `Message`, `Canceled`,
and the current resource state in the diagnostic. On success, wait for Healthy again.

- [ ] **Step 4: Do not expose generic resource operations**

There is no public `ExecuteCommand`, `Notifications`, `Application`, `Services`, or arbitrary
resource-state mutation. Add reflection assertions to `AppHostTestingSurfaceContracts`.

- [ ] **Step 5: Run resource and public-surface tests**

```powershell
dotnet test tests/DigitalBrain.HostTests/DigitalBrain.HostTests.csproj -c Release --filter "FullyQualifiedName~HostedResourceContracts" --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~AppHostTestingSurfaceContracts" --logger "console;verbosity=minimal"
```

Expected: PASS.

- [ ] **Step 6: Commit resource handles**

```powershell
git add src/DigitalBrain.Testing tests
git commit -m "feat(testing): add typed hosted resource handles"
```

---

### Task 4: Add bounded AppHost artifact and terminal-state cleanup

**Files:**
- Create: `src/DigitalBrain.Testing/Diagnostics/AppHostTestArtifact.cs`
- Modify: `src/DigitalBrain.Testing/Hosting/RunningAppHost.cs`
- Modify: `src/DigitalBrain.Testing/Hosting/HostedResource.cs`
- Create: `tests/DigitalBrain.HostTests/AppHostArtifactContracts.cs`

**Interfaces:**
- Consumes: `ResourceNotificationService.TryGetCurrentState`, `WatchAsync`
- Produces: bounded resource evidence and graph-owned cleanup proof

- [ ] **Step 1: Write failure-artifact tests**

Cause an unknown resource health wait and assert the attached `digitalbrain-apphost.json` contains:

```text
requested resource name
known resource IDs
state and health
URLs without credentials/query secrets
last 200 bounded log lines per relevant resource
command transitions
cleanup result
```

Assert serialized JSON is at most 2 MiB and individual log lines are at most 4,096 characters.

- [ ] **Step 2: Run and verify red**

```powershell
dotnet test tests/DigitalBrain.HostTests/DigitalBrain.HostTests.csproj -c Release --filter "FullyQualifiedName~AppHostArtifactContracts" --logger "console;verbosity=minimal"
```

Expected: FAIL because no artifact exists.

- [ ] **Step 3: Record resource notifications**

Start one background collector per `RunningAppHost` over:

```csharp
application.ResourceNotifications.WatchAsync(cancellationToken)
```

Store bounded `ResourceEvent` snapshots. Copy only resource ID, resource type, state text, health,
timestamps, exit code, and sanitized URLs. Never capture environment-variable values.

- [ ] **Step 4: Capture logs through Aspire's resource logger service**

Resolve `ResourceLoggerService` internally from `application.Services`, consume
`WatchAsync(resourceName)`, retain only the bounded relevant tail, and keep the service type out of
every public signature. Do not read process stdout directly.

On a failed resource operation or cleanup, attach through:

```csharp
TestContext.Current.AddAttachment(
    "digitalbrain-apphost.json",
    artifact.ToJson());
```

Preserve the original exception as the inner exception of the closed Testing diagnostic.

- [ ] **Step 5: Stop and verify the graph on dispose**

`RunningAppHost.DisposeAsync`:

1. calls `StopAsync` with a linked cleanup token;
2. observes each runtime resource that emitted a state notification until its state is in
   `KnownResourceStates.TerminalStates`; model-only values with no runtime state are recorded but not
   awaited;
3. cancels and awaits the notification/log collectors after their final snapshots;
4. calls `DisposeAsync`;
5. attaches `digitalbrain-apphost.json` if any operation or cleanup fails;
6. releases the exclusive lease in `finally`.

Do not enumerate or kill processes by executable name. Aspire owns every child process in the graph;
terminal resource state plus successful application disposal is the cleanup contract.
Make disposal idempotent so an explicitly disposed `await using` value cannot release the graph
lease twice.

- [ ] **Step 6: Run artifact and exclusivity tests twice**

```powershell
dotnet test tests/DigitalBrain.HostTests/DigitalBrain.HostTests.csproj -c Release --filter "FullyQualifiedName~AppHostArtifactContracts|FullyQualifiedName~FixtureExclusivity" --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.HostTests/DigitalBrain.HostTests.csproj -c Release --filter "FullyQualifiedName~AppHostArtifactContracts|FullyQualifiedName~FixtureExclusivity" --logger "console;verbosity=minimal"
```

Expected: both PASS and no orphan `DigitalBrain.Host`/`DigitalBrain.ProbeHost` processes remain.

- [ ] **Step 7: Commit AppHost evidence**

```powershell
git add src/DigitalBrain.Testing tests/DigitalBrain.HostTests
git commit -m "feat(testing): attach AppHost resource evidence"
```

---

### Task 5: Migrate HostTests to concrete fixtures

**Files:**
- Modify: `tests/DigitalBrain.HostTests/HostedBrain.cs`
- Modify: `tests/DigitalBrain.HostTests/HostedRestart.cs`
- Modify: `tests/DigitalBrain.HostTests/Topology.cs`
- Modify: `tests/DigitalBrain.HostTests/ProductionAppHost.cs`
- Delete: `tests/DigitalBrain.HostTests/HostedCollection.cs`
- Modify: `tests/DigitalBrain.HostTests/AssemblyInfo.cs`

**Interfaces:**
- Consumes: `TestingAppHostFixture`, `ProductionAppHostFixture`, `RunningAppHost`, `HostedResource`
- Produces: real L2 proofs with no static helper/collection

- [ ] **Step 1: Rewrite health and topology tests**

Constructor-inject the exact fixture:

```csharp
public sealed class HostedBrain(TestingAppHostFixture fixture)
{
    [Fact]
    public async Task TheSiloReachesHealthyOnTheRealHost()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var host = await fixture.StartAsync(cancellationToken);
        var silo = host.Resource("silo");

        await silo.WaitUntilHealthyAsync(cancellationToken);
        using var client = silo.CreateHttpClient();
        using var health = await client.GetAsync("/health", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }
}
```

Bind `probe`, storage, MCP, and website names once in their respective tests. Use
`WaitUntilHealthyAsync` before every endpoint assertion.

- [ ] **Step 2: Rewrite restart tests**

Keep durability setup and assertion through the production client/probe endpoint, but restart only
through:

```csharp
await host.Resource("silo").RestartAsync(cancellationToken);
```

After restart, wait on the same handle's Healthy state. Do not call resource commands or inspect
`DistributedApplication`.

- [ ] **Step 3: Rewrite production composition tests**

Inject `ProductionAppHostFixture` and verify:

- brain silo Healthy;
- MCP client Healthy after the silo;
- website Healthy/external endpoint;
- selected module resources exist and become Healthy;
- client projections cannot read storage/state-protection/provider secrets.

Use `HostedResource` operations and HTTP only.

- [ ] **Step 4: Remove collection serialization**

Delete `[Collection]`, `HostedCollectionDefinition`, and assembly
`DisableTestParallelization`. The package-internal graph lease is the only exclusivity owner.

- [ ] **Step 5: Run HostTests three times**

```powershell
dotnet test tests/DigitalBrain.HostTests/DigitalBrain.HostTests.csproj -c Release --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.HostTests/DigitalBrain.HostTests.csproj -c Release --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.HostTests/DigitalBrain.HostTests.csproj -c Release --logger "console;verbosity=minimal"
```

Expected: all three PASS.

- [ ] **Step 6: Commit L2 migration**

```powershell
git add tests/DigitalBrain.HostTests
git commit -m "test: migrate hosted proofs to AppHost fixtures"
```

---

### Task 6: Delete old hosted helpers and publish the L2 boundary

**Files:**
- Delete: `src/DigitalBrain.Testing/Hosting/HostedApplication.cs`
- Delete: `src/DigitalBrain.Testing/Hosting/HostedScenario.cs`
- Modify: `tests/DigitalBrain.Tests/AppHostTestingSurfaceContracts.cs`
- Modify: `tests/DigitalBrain.Tests/ArchitectureCutContracts.cs`
- Modify: `docs/architecture.md`
- Modify: `docs/packages.md`

**Interfaces:**
- Produces: no old L2 vocabulary or raw Aspire escape

- [ ] **Step 1: Add final forbidden-source assertions**

Reject outside `docs/superpowers/**`:

```text
HostedApplication
HostedScenario
DefaultTrackedProcessNames
GetProcessesByName
IsExclusiveHeld
ExclusiveOwner
public DistributedApplication
```

- [ ] **Step 2: Delete old source and fix compiler consumers**

Delete both old helpers. Run:

```powershell
dotnet build DigitalBrain.slnx -c Release
```

Expected initially: any missed old consumer fails. Migrate it to the exact fixture/resource API; do
not restore a compatibility class.

- [ ] **Step 3: Update documentation**

Use:

```csharp
await using var host = await fixture.StartAsync(cancellationToken);
var silo = host.Resource("silo");
await silo.WaitUntilHealthyAsync(cancellationToken);
await silo.RestartAsync(cancellationToken);
```

Explain that L2 is exclusive and operational while L1 owns module semantics.

- [ ] **Step 4: Run L0/L1/L2/docs gates**

```powershell
dotnet build DigitalBrain.slnx -c Release
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.TestingTests/DigitalBrain.TestingTests.csproj -c Release --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.HostTests/DigitalBrain.HostTests.csproj -c Release --logger "console;verbosity=minimal"
npm --prefix docs test
npm --prefix docs run build
```

Expected: PASS.

- [ ] **Step 5: Verify deletion and process cleanup**

```powershell
rg -n "HostedApplication|HostedScenario|DefaultTrackedProcessNames|GetProcessesByName|IsExclusiveHeld|ExclusiveOwner" src tests hosts docs --glob "!docs/superpowers/**"
```

Expected: no source matches. The HostTests terminal-state cleanup contract proves graph-owned process
cleanup without inspecting unrelated machine processes.

- [ ] **Step 6: Commit the clean L2 cut**

```powershell
git add src/DigitalBrain.Testing tests docs
git restore --staged Directory.Packages.props
git commit -m "refactor(testing): replace hosted scenarios with AppHost resources"
```

---

## Plan 3 completion gate

Run:

```powershell
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal"
npm --prefix docs test
npm --prefix docs run build
git status --short
```

Expected:

- all gates pass;
- only the unrelated `Directory.Packages.props` change remains unstaged;
- one full AppHost can run in-process;
- every resource operation starts from one `HostedResource`;
- no public raw Aspire runtime or process-name cleanup exists;
- disposal leaves every graph-owned resource terminal and no child process alive.
