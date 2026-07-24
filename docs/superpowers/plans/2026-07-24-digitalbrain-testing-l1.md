# DigitalBrain L1 Testing Product Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the public Simulation/Scenario runtime with an assembly-owned real multi-silo `DigitalBrainFixture`, a serialized method-scoped `TestBrain`, typed owners/neurons/journals, deterministic time, closed faults, and always-active failure evidence.

**Architecture:** xUnit v3 owns assembly fixture lifetime. Each concrete fixture composes generated module capsules once, starts one real three-silo cluster, and grants one active method lease. Production interaction enters through `IDigitalBrain`; privileged test controls remain on `TestBrain` and `TestNeuron<T>`. Journal observation is the synchronization authority, and Gherkin is generated/thin over the same API.

**Tech Stack:** .NET 10, xUnit v3 3.2.2, Orleans TestingHost 10.2.2-rc.2, Reqnroll 3.3.4, `System.Threading.Channels`.

## Global Constraints

- Execute after `2026-07-24-compiled-modules-and-brain-hosting.md`.
- Work only in `E:\intochat\digitalbrain` on the current branch.
- Preserve the user's unstaged `Directory.Packages.props` line-ending change; never stage it.
- One real three-silo cluster per concrete assembly fixture; no fake kernel.
- One active `TestBrain` per fixture; fixtures in independent test assemblies may run in parallel.
- Every method receives a unique opaque owner namespace and controllable clock.
- `TestBrain.Client` is the real `IDigitalBrain`; `TestBrain` does not implement it.
- No public `IGrainFactory`, `GrainId`, cluster singleton, silo object, DI, or hosting callback.
- No custom assertion DSL; provide typed evidence and use ordinary assertion libraries.
- Substitutes are limited to `IChatClient`, southbound MCP transport, OAuth/parameters, and test time.
- No wall-clock sleep/settle API.
- Failure artifacts are bounded and never contain secrets or unbounded provider/model payloads.
- Delete old public Simulation/Scenario APIs after consumers move; do not retain forwarding wrappers.

---

## File structure

### Create in `src/DigitalBrain.Testing`

| File | Responsibility |
|---|---|
| `DigitalBrainFixture.cs` | xUnit v3 assembly lifecycle and one-method lease |
| `DigitalBrainTestBuilder.cs` | closed compiled-module composition |
| `TestBrain.cs` | method scope, default client, owners, clock, diagnostics |
| `TestOwner.cs` | scoped logical owner, client, typed neurons |
| `TestNeuron.cs` | typed production reference, journals, faults, restart |
| `TestClock.cs` | public deterministic clock control |
| `Cluster/FixtureCluster.cs` | instance-owned three-silo cluster |
| `Cluster/ControllableTimeProvider.cs` | fixed-origin `TimeProvider` |
| `Cluster/TestReminderDriver.cs` | deliver due reminders after clock advance |
| `Journals/TestJournal.cs` | typed committed-journal stream |
| `Journals/TestJournalObserver.cs` | method-owned Orleans observer reference |
| `Journals/ObservedSynapse.cs` | immutable typed evidence |
| `Faults/JournalFaultHandle.cs` | closed target-scoped journal failure lease |
| `Diagnostics/BrainTestArtifact.cs` | bounded structured L1 evidence |
| `Diagnostics/BrainTestFailureException.cs` | operation/cleanup failure carrying artifact |
| `Gherkin/GeneratedVocabularySteps.cs` | thin Reqnroll adapter over typed generated vocabulary |

### Create in tests

| File/project | Responsibility |
|---|---|
| `tests/DigitalBrain.TestingTests/DigitalBrain.TestingTests.csproj` | public-package self-tests |
| `tests/DigitalBrain.TestingTests/AssemblyInfo.cs` | xUnit assembly fixture registration |
| `tests/DigitalBrain.TestingTests/TestingFixture.cs` | concrete fixture and probe module |
| `tests/DigitalBrain.TestingTests/FixtureLifecycleContracts.cs` | lifecycle/lease/isolation |
| `tests/DigitalBrain.TestingTests/ClientAndOwnerContracts.cs` | real client and owner scoping |
| `tests/DigitalBrain.TestingTests/JournalContracts.cs` | typed journal synchronization |
| `tests/DigitalBrain.TestingTests/ClockAndFaultContracts.cs` | deterministic clock/fault/restart |
| `tests/DigitalBrain.TestingTests/ArtifactContracts.cs` | bounded xUnit attachments |
| `tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj` | retained public-surface L1 proofs |

### Delete after migration

```text
src/DigitalBrain.Testing/Simulations.cs
src/DigitalBrain.Testing/Simulation.cs
src/DigitalBrain.Testing/Scenario.cs
src/DigitalBrain.Testing/SimulationCluster.cs
src/DigitalBrain.Testing/Cluster/SimulationClusterHost.cs
src/DigitalBrain.Testing/Cluster/ScenarioClock.cs
src/DigitalBrain.Testing/SimulationNeuron.cs
src/DigitalBrain.Testing/SimulationAssertionException.cs
src/DigitalBrain.Testing/NeuronCatalog.cs
src/DigitalBrain.Testing/SynapseObserver.cs
src/DigitalBrain.Testing/Diagnostics/ScenarioFailureArtifact.cs
src/DigitalBrain.Testing/Diagnostics/ScenarioStages.cs
src/DigitalBrain.Testing/Faults/FaultPoint.cs
src/DigitalBrain.Testing/Faults/FaultHandle.cs
src/DigitalBrain.Testing/Faults/ScenarioFaults.cs
src/DigitalBrain.Testing/Gherkin/ScenarioSteps.cs
tests/DigitalBrain.Simulations/
```

---

### Task 1: Add a public package-shape guard and dedicated self-test project

**Files:**
- Create: `tests/DigitalBrain.TestingTests/DigitalBrain.TestingTests.csproj`
- Create: `tests/DigitalBrain.TestingTests/PublicSurfaceContracts.cs`
- Modify: `DigitalBrain.slnx`
- Modify: `src/DigitalBrain.Testing/DigitalBrain.Testing.csproj`

**Interfaces:**
- Consumes: `DigitalBrain.Testing` package
- Produces: an independent external-consumer test assembly

- [ ] **Step 1: Create the external-consumer project**

Use:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <NoWarn>$(NoWarn);ORLEANSEXP005;CA2007;CA1812</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="xunit.v3" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\DigitalBrain.Abstractions\DigitalBrain.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\DigitalBrain.Client\DigitalBrain.Client.csproj" />
    <ProjectReference Include="..\..\src\DigitalBrain.Kernel\DigitalBrain.Kernel.csproj" />
    <ProjectReference Include="..\..\src\DigitalBrain.Testing\DigitalBrain.Testing.csproj" />
    <ProjectReference Include="..\..\src\DigitalBrain.SourceGeneration\DigitalBrain.SourceGeneration.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false"
                      PrivateAssets="all" />
  </ItemGroup>
</Project>
```

Add it under `/tests/` in `DigitalBrain.slnx`.

- [ ] **Step 2: Write the failing public-surface contract**

```csharp
using System.Reflection;
using DigitalBrain.Client;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class PublicSurfaceContracts
{
    [Fact]
    public void TestingSurfaceNamesWhatItOwns()
    {
        var exported = typeof(DigitalBrainFixture).Assembly
            .GetExportedTypes()
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains(nameof(DigitalBrainFixture), exported);
        Assert.Contains(nameof(DigitalBrainTestBuilder), exported);
        Assert.Contains(nameof(TestBrain), exported);
        Assert.Contains(nameof(TestOwner), exported);
        Assert.Contains("TestNeuron`1", exported);
        Assert.Contains("ObservedSynapse`1", exported);
        Assert.DoesNotContain("Simulation", exported);
        Assert.DoesNotContain("Simulations", exported);
        Assert.DoesNotContain("Scenario", exported);
        Assert.DoesNotContain("SimulationCluster", exported);
    }

    [Fact]
    public void TestBrainDoesNotImplementTheProductionClient()
        => Assert.DoesNotContain(typeof(IDigitalBrain), typeof(TestBrain).GetInterfaces());

    [Fact]
    public void NoPublicMemberLeaksOrleans()
    {
        var leaked = typeof(TestBrain).Assembly
            .GetExportedTypes()
            .SelectMany(type => type.GetMembers())
            .SelectMany(ReferencedTypes)
            .SelectMany(Expand)
            .Where(type => type.FullName?.StartsWith("Orleans.", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Empty(leaked);
    }

    private static IEnumerable<Type> ReferencedTypes(MemberInfo member) =>
        member switch
        {
            MethodInfo method => method.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType),
            ConstructorInfo constructor => constructor.GetParameters()
                .Select(parameter => parameter.ParameterType),
            PropertyInfo property => [property.PropertyType],
            FieldInfo field => [field.FieldType],
            EventInfo @event => [@event.EventHandlerType!],
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

- [ ] **Step 3: Run and verify red**

```powershell
dotnet test tests/DigitalBrain.TestingTests/DigitalBrain.TestingTests.csproj -c Release --filter "FullyQualifiedName~PublicSurfaceContracts" --logger "console;verbosity=minimal"
```

Expected: build FAIL because the new types do not exist.

- [ ] **Step 4: Add required package/project references to Testing**

Add:

```xml
<PackageReference Include="xunit.v3" />
<ProjectReference Include="..\DigitalBrain.Client\DigitalBrain.Client.csproj" />
```

Keep `Aspire.Hosting.Testing` temporarily; Plan 3 owns the L2 split.

- [ ] **Step 5: Commit the red external contract**

```powershell
git add DigitalBrain.slnx src/DigitalBrain.Testing/DigitalBrain.Testing.csproj tests/DigitalBrain.TestingTests
git commit -m "test: define DigitalBrain testing product surface"
```

---

### Task 2: Build the assembly fixture, typed composition, and serial lease

**Files:**
- Create: `src/DigitalBrain.Testing/DigitalBrainFixture.cs`
- Create: `src/DigitalBrain.Testing/DigitalBrainTestBuilder.cs`
- Create: `src/DigitalBrain.Testing/TestBrain.cs`
- Create: `src/DigitalBrain.Testing/Cluster/FixtureCluster.cs`
- Create: `tests/DigitalBrain.TestingTests/AssemblyInfo.cs`
- Create: `tests/DigitalBrain.TestingTests/TestingFixture.cs`
- Create: `tests/DigitalBrain.TestingTests/FixtureLifecycleContracts.cs`

**Interfaces:**
- Consumes: `IModule`, generated `ICompiledModule`
- Produces:

```csharp
public abstract class DigitalBrainFixture : IAsyncLifetime
{
    protected abstract void Configure(DigitalBrainTestBuilder brain);
    public Task<TestBrain> CreateBrainAsync(CancellationToken cancellationToken = default);
}

public sealed class DigitalBrainTestBuilder
{
    public void AddModule<TModule>() where TModule : class, IModule, new();
}
```

- [ ] **Step 1: Add a fixture probe module**

`TestingFixture.cs`:

```csharp
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Testing;

namespace DigitalBrain.TestingTests;

public sealed partial class TestingProbeModule : IModule;

public partial interface IEchoNeuron : INeuron
{
    [Alias(nameof(Echo))]
    Task<string> Echo(string value);
}

internal sealed class EchoNeuron : Neuron, IEchoNeuron
{
    public Task<string> Echo(string value) => Task.FromResult(value);
}

public sealed class TestingFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
        => brain.AddModule<TestingProbeModule>();
}
```

`AssemblyInfo.cs`:

```csharp
using DigitalBrain.TestingTests;
using Xunit;

[assembly: AssemblyFixture(typeof(TestingFixture))]
```

- [ ] **Step 2: Write lifecycle tests**

```csharp
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class FixtureLifecycleContracts(TestingFixture fixture)
{
    [Fact]
    public async Task AMethodLeaseDoesNotStopTheAssemblyCluster()
    {
        await using (var first = await fixture.CreateBrainAsync(TestContext.Current.CancellationToken))
        {
            Assert.NotNull(first);
        }

        await using var second = await fixture.CreateBrainAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(second);
    }

    [Fact]
    public async Task ASecondMethodLeaseWaitsUntilTheFirstIsDisposed()
    {
        await using var first = await fixture.CreateBrainAsync(TestContext.Current.CancellationToken);
        var waiting = fixture.CreateBrainAsync(TestContext.Current.CancellationToken);

        Assert.False(waiting.IsCompleted);
        await first.DisposeAsync();

        await using var second = await waiting;
        Assert.NotNull(second);
    }
}
```

- [ ] **Step 3: Run and verify red**

```powershell
dotnet test tests/DigitalBrain.TestingTests/DigitalBrain.TestingTests.csproj -c Release --filter "FullyQualifiedName~FixtureLifecycleContracts" --logger "console;verbosity=minimal"
```

Expected: build FAIL.

- [ ] **Step 4: Implement the sealed test builder**

`DigitalBrainTestBuilder` stores a `Dictionary<ModuleId,ICompiledModule>`. `AddModule<TModule>`
creates `TModule`, casts it to the generator-supplied `ICompiledModule`, rejects duplicate IDs, and
throws after `Seal()`:

```csharp
public void AddModule<TModule>()
    where TModule : class, IModule, new()
{
    if (_sealed)
    {
        throw new InvalidOperationException("The DigitalBrain test composition is already sealed.");
    }

    var compiled = (ICompiledModule)new TModule();
    if (!_modules.TryAdd(compiled.Id, compiled))
    {
        throw new InvalidOperationException(
            $"Module '{compiled.Id}' is already configured for this fixture.");
    }
}
```

- [ ] **Step 5: Implement instance-owned FixtureCluster**

Move the useful three-silo setup from `SimulationClusterHost` into an instance class. Its constructor
accepts the sealed compiled module list. `StartAsync` configures every silo with:

```csharp
DigitalBrainRuntime.Add(silo, FixtureCluster.LabelOf(options.SiloName), modules);
```

Keep the real volatile journal/reminder providers, but move all mutable fields off statics and onto
the fixture instance. Do not inspect `AppDomain.CurrentDomain.GetAssemblies()`.

- [ ] **Step 6: Implement fixture lifetime and lease**

Use xUnit v3's exact interface:

```csharp
public abstract class DigitalBrainFixture : IAsyncLifetime
{
    private readonly SemaphoreSlim _methodLease = new(1, 1);
    private FixtureCluster? _cluster;

    protected abstract void Configure(DigitalBrainTestBuilder brain);

    public async ValueTask InitializeAsync()
    {
        var brain = new DigitalBrainTestBuilder();
        Configure(brain);
        _cluster = await FixtureCluster.StartAsync(brain.Seal());
    }

    public async Task<TestBrain> CreateBrainAsync(CancellationToken cancellationToken = default)
    {
        await _methodLease.WaitAsync(cancellationToken);
        try
        {
            return TestBrain.Create(Cluster(), _methodLease.Release);
        }
        catch
        {
            _methodLease.Release();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_cluster is not null)
        {
            await _cluster.DisposeAsync();
        }

        _methodLease.Dispose();
    }
}
```

Add the exact method-lease owner used by this task:

```csharp
public sealed class TestBrain : IAsyncDisposable
{
    private Action? _release;

    private TestBrain(FixtureCluster cluster, Action release)
    {
        Cluster = cluster;
        _release = release;
    }

    internal FixtureCluster Cluster { get; }

    internal static TestBrain Create(FixtureCluster cluster, Action release)
        => new(cluster, release);

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _release, null)?.Invoke();
        return ValueTask.CompletedTask;
    }
}
```

Task 3 adds client and owner behavior to this same type. Disposal remains idempotent so an explicit
dispose inside an `await using` scope cannot release the method lease twice.

- [ ] **Step 7: Run lifecycle tests**

```powershell
dotnet test tests/DigitalBrain.TestingTests/DigitalBrain.TestingTests.csproj -c Release --filter "FullyQualifiedName~FixtureLifecycleContracts" --logger "console;verbosity=minimal"
```

Expected: PASS.

- [ ] **Step 8: Commit fixture ownership**

```powershell
git add src/DigitalBrain.Testing tests/DigitalBrain.TestingTests
git commit -m "feat(testing): add assembly-owned DigitalBrain fixture"
```

---

### Task 3: Add TestBrain, scoped owners, and the real client

**Files:**
- Modify: `src/DigitalBrain.Testing/TestBrain.cs`
- Create: `src/DigitalBrain.Testing/TestOwner.cs`
- Create: `tests/DigitalBrain.TestingTests/ClientAndOwnerContracts.cs`
- Modify: `src/DigitalBrain.Testing/DigitalBrainFixture.cs`

**Interfaces:**
- Produces:

```csharp
public sealed class TestBrain : IAsyncDisposable
{
    public IDigitalBrain Client { get; }
    public TestOwner Owner(string label);
    public TestNeuron<TNeuron> Neuron<TNeuron>(string name = "default")
        where TNeuron : class, INeuron;
}

public sealed class TestOwner
{
    public OwnerId Id { get; }
    public IDigitalBrain Client { get; }
    public TestNeuron<TNeuron> Neuron<TNeuron>(string name = "default")
        where TNeuron : class, INeuron;
}
```

- [ ] **Step 1: Write owner/client tests**

```csharp
using DigitalBrain.Client;
using Xunit;

namespace DigitalBrain.TestingTests;

public sealed class ClientAndOwnerContracts(TestingFixture fixture)
{
    [Fact]
    public async Task ClientIsTheProductionContract()
    {
        await using var test = await fixture.CreateBrainAsync(TestContext.Current.CancellationToken);

        Assert.IsAssignableFrom<IDigitalBrain>(test.Client);
        Assert.Equal("hello", await test.Client.Get<IEchoNeuron>().Echo("hello"));
    }

    [Fact]
    public async Task LogicalOwnersAreScopedToTheMethod()
    {
        string firstAlice;
        await using (var first = await fixture.CreateBrainAsync(TestContext.Current.CancellationToken))
        {
            firstAlice = first.Owner("alice").Id.Value;
            Assert.NotEqual(first.Owner("alice").Id, first.Owner("bob").Id);
            Assert.Equal(first.Owner("alice").Id, first.Owner("alice").Client.Owner);
        }

        await using var second = await fixture.CreateBrainAsync(TestContext.Current.CancellationToken);
        Assert.NotEqual(firstAlice, second.Owner("alice").Id.Value);
    }
}
```

- [ ] **Step 2: Run and verify red**

```powershell
dotnet test tests/DigitalBrain.TestingTests/DigitalBrain.TestingTests.csproj -c Release --filter "FullyQualifiedName~ClientAndOwnerContracts" --logger "console;verbosity=minimal"
```

Expected: build FAIL because `TestBrain` has no `Client`, `Owner`, or `Neuron` members.

- [ ] **Step 3: Implement scoped identities**

At method creation generate:

```csharp
var scope = $"test-{Guid.NewGuid():N}";
```

Map the default owner to `new OwnerId($"{scope}-default")` and a logical owner to:

```csharp
new OwnerId($"{scope}-{IdentityLabel.Validate(label)}")
```

`IdentityLabel.Validate` rejects whitespace, `/`, and duplicate labels with different casing.
Cache `TestOwner` by ordinal label so repeated `Owner("alice")` returns the same instance.

- [ ] **Step 4: Create real production clients**

Use:

```csharp
DigitalBrainClient.Connect(cluster.Client, owner.Id.Value)
```

Store it as `IDigitalBrain`. Do not expose the cluster client.

- [ ] **Step 5: Run owner/client tests**

```powershell
dotnet test tests/DigitalBrain.TestingTests/DigitalBrain.TestingTests.csproj -c Release --filter "FullyQualifiedName~ClientAndOwnerContracts" --logger "console;verbosity=minimal"
```

Expected: PASS.

- [ ] **Step 6: Commit method identity**

```powershell
git add src/DigitalBrain.Testing tests/DigitalBrain.TestingTests
git commit -m "feat(testing): add scoped owners and production client"
```

---

### Task 4: Add typed TestNeuron journal observation

**Files:**
- Create: `src/DigitalBrain.Testing/TestNeuron.cs`
- Create: `src/DigitalBrain.Testing/Journals/TestJournal.cs`
- Create: `src/DigitalBrain.Testing/Journals/TestJournalObserver.cs`
- Create: `src/DigitalBrain.Testing/Journals/ObservedSynapse.cs`
- Create: `tests/DigitalBrain.TestingTests/JournalContracts.cs`
- Modify: `src/DigitalBrain.Testing/TestBrain.cs`
- Modify: `src/DigitalBrain.Testing/TestOwner.cs`

**Interfaces:**
- Produces:

```csharp
public sealed class TestNeuron<TNeuron> where TNeuron : class, INeuron
{
    public NeuronId Id { get; }
    public TNeuron Reference { get; }
    public TestJournal Incoming { get; }
    public TestJournal Outgoing { get; }
}

public sealed class TestJournal
{
    public Task<ObservedSynapse<TSynapse>> NextAsync<TSynapse>(
        CancellationToken cancellationToken = default)
        where TSynapse : Synapse;

    public Task<IReadOnlyList<ObservedSynapse<TSynapse>>> ReadAsync<TSynapse>(
        long afterSequence = 0,
        CancellationToken cancellationToken = default)
        where TSynapse : Synapse;
}

public sealed record ObservedSynapse<TSynapse>(
    TSynapse Synapse,
    NeuronId Subject,
    NeuronId Caller,
    JournalKind Direction,
    long Sequence,
    DateTimeOffset Timestamp,
    CorrelationId CorrelationId,
    SynapseId SynapseId)
    where TSynapse : Synapse;
```

- [ ] **Step 1: Add a fact-emitting probe**

Extend the probe module:

```csharp
[GenerateSerializer]
[Alias("tests.echo-requested")]
internal sealed record EchoRequested([property: Id(0)] string Value) : Synapse;

[GenerateSerializer]
[Alias("tests.echoed")]
internal sealed record Echoed([property: Id(0)] string Value) : Synapse;

internal sealed class EchoNeuron :
    Neuron,
    IEchoNeuron,
    IHandle<EchoRequested>,
    IEmit<Echoed>
{
    public Task<string> Echo(string value) => Task.FromResult(value);

    public Task HandleAsync(EchoRequested request, CancellationToken cancellationToken)
        => EmitAsync(new Echoed(request.Value));
}
```

- [ ] **Step 2: Write journal tests**

```csharp
public sealed class JournalContracts(TestingFixture fixture)
{
    [Fact]
    public async Task ATestNeuronUsesTheRealReferenceAndCommittedJournal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var echo = test.Neuron<IEchoNeuron>("primary");

        await test.Client.SendAsync<IEchoNeuron>("primary", new EchoRequested("Ada"));

        var observed = await echo.Outgoing.NextAsync<Echoed>(cancellationToken);

        Assert.Equal("direct", await echo.Reference.Echo("direct"));
        Assert.Equal("client", await test.Client.Get<IEchoNeuron>("primary").Echo("client"));
        Assert.Equal("Ada", observed.Synapse.Value);
        Assert.Equal(echo.Id, observed.Subject);
        Assert.Equal(JournalKind.Outgoing, observed.Direction);
        Assert.True(observed.Sequence > 0);
    }
}
```

- [ ] **Step 3: Run and verify red**

```powershell
dotnet test tests/DigitalBrain.TestingTests/DigitalBrain.TestingTests.csproj -c Release --filter "FullyQualifiedName~JournalContracts" --logger "console;verbosity=minimal"
```

Expected: build FAIL.

- [ ] **Step 4: Implement the typed handle**

`TestOwner.Neuron<TNeuron>` derives `NeuronId.For<TNeuron>(Id, name)`, gets
`Client.Get<TNeuron>(name)`, and asks the owning `TestBrain` for two `TestJournal` instances.

`TestBrain.Neuron<TNeuron>` delegates to its default `TestOwner`.

- [ ] **Step 5: Implement observation without polling**

`TestJournalObserver` implements `IJournalObserver`, owns a bounded `Channel<SynapseDelivery>`, and
writes every pushed delta. `TestJournal` lazily:

1. creates the Orleans object reference through internal `FixtureCluster`;
2. calls the hidden session neuron's `WatchNeuron(subject, kind, cursor, observer)`;
3. reads the channel until a delivery whose `Synapse` is `TSynapse`;
4. maps it to `ObservedSynapse<TSynapse>`;
5. calls `UnwatchNeuron` and deletes the object reference during `TestBrain.DisposeAsync`.

Compaction produces `BrainTestFailureException` with the subject, direction, requested cursor, and
snapshot tallies. Do not fall back to `Task.Delay` or Activity listeners.

- [ ] **Step 6: Run journal and public-leak tests**

```powershell
dotnet test tests/DigitalBrain.TestingTests/DigitalBrain.TestingTests.csproj -c Release --filter "FullyQualifiedName~JournalContracts|FullyQualifiedName~PublicSurfaceContracts" --logger "console;verbosity=minimal"
```

Expected: PASS.

- [ ] **Step 7: Commit typed observation**

```powershell
git add src/DigitalBrain.Testing tests/DigitalBrain.TestingTests
git commit -m "feat(testing): add typed neuron journal evidence"
```

---

### Task 5: Add deterministic clock and due-reminder driver

**Files:**
- Create: `src/DigitalBrain.Testing/TestClock.cs`
- Create: `src/DigitalBrain.Testing/Cluster/ControllableTimeProvider.cs`
- Create: `src/DigitalBrain.Testing/Cluster/TestReminderDriver.cs`
- Modify: `src/DigitalBrain.Testing/VolatileReminderTable.cs`
- Modify: `src/DigitalBrain.Testing/Cluster/FixtureCluster.cs`
- Modify: `src/DigitalBrain.Testing/TestBrain.cs`
- Create: `tests/DigitalBrain.TestingTests/ClockAndFaultContracts.cs`

**Interfaces:**
- Produces:

```csharp
public sealed class TestClock
{
    public DateTimeOffset UtcNow { get; }
    public Task AdvanceAsync(TimeSpan duration, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 1: Write fixed-origin clock tests**

```csharp
[Fact]
public async Task ClockStartsFixedAndAdvancesWithoutWallTime()
{
    await using var test = await fixture.CreateBrainAsync(TestContext.Current.CancellationToken);
    var origin = test.Clock.UtcNow;
    var stopwatch = Stopwatch.StartNew();

    await test.Clock.AdvanceAsync(
        TimeSpan.FromDays(7),
        TestContext.Current.CancellationToken);

    Assert.Equal(origin + TimeSpan.FromDays(7), test.Clock.UtcNow);
    Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
}
```

Add a probe neuron with two capabilities:

1. record `TimeProvider.GetUtcNow()` into an emitted fact;
2. create a one-shot timer through `TimeProvider.CreateTimer` and emit `TimerFired` from the callback
   through a self-proxy.

Assert that a neuron activated after the advance observes the advanced instant. Arm the timer for
one hour, advance 59 minutes and assert no `TimerFired`, then advance one minute and consume exactly
one `TimerFired` from the committed outgoing journal.

- [ ] **Step 2: Run and verify red**

```powershell
dotnet test tests/DigitalBrain.TestingTests/DigitalBrain.TestingTests.csproj -c Release --filter "FullyQualifiedName~ClockAndFaultContracts.Clock" --logger "console;verbosity=minimal"
```

Expected: FAIL because no test clock exists.

- [ ] **Step 3: Implement fixed-origin TimeProvider**

Use a lock-protected exact instant, not `DateTimeOffset.UtcNow + offset`:

```csharp
internal sealed class ControllableTimeProvider(DateTimeOffset origin) : TimeProvider
{
    private readonly Lock _gate = new();
    private DateTimeOffset _utcNow = origin;

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate)
        {
            return _utcNow;
        }
    }

    internal void Advance(TimeSpan duration)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(duration, TimeSpan.Zero);
        lock (_gate)
        {
            _utcNow += duration;
        }
    }
}
```

Override:

```csharp
public override ITimer CreateTimer(
    TimerCallback callback,
    object? state,
    TimeSpan dueTime,
    TimeSpan period);
```

The returned internal `ITimer` supports `Change`, `Dispose`, and `DisposeAsync`. Store registrations
by next due instant and a monotonic registration sequence so equal-due callbacks have stable order.
`Timeout.InfiniteTimeSpan` disables the corresponding due/period value. Invoke callbacks outside
the provider lock. A periodic timer advances from its previous due instant; a one-shot timer becomes
disabled before its callback runs. Use the same provider instance in every silo for the active
serialized method.

- [ ] **Step 4: Drive timers and due reminders after every advance**

Add an internal snapshot method to `VolatileReminderTable` returning copied entries whose
`StartAt <= now`. `TestReminderDriver` delivers each due entry through the existing grain-service
bridge and records the delivery in diagnostics. It repeats until no newly due entry remains, with a
hard bounded maximum of 1,024 deliveries to detect a rescheduling cycle.

`TestClock.AdvanceAsync` owns the entire deterministic drain:

1. compute the exact target instant;
2. move to the earliest timer/reminder due at or before the target;
3. fire all work due at that instant in stable order;
4. yield through the cluster work queue so self-proxy calls and journal commits finish;
5. repeat until no work remains at or before the target;
6. set the provider to the exact target.

Count timer callbacks and reminder deliveries against one shared 1,024-operation bound. Throw
`BrainTestFailureException` with the pending registrations when the bound is exceeded.

- [ ] **Step 5: Reset time between methods**

Before returning each `TestBrain`, reset the provider to the fixture's fixed epoch and clear
method-owned due-delivery diagnostics. Since only one method lease exists, no other test can observe
the reset.

- [ ] **Step 6: Run clock tests**

```powershell
dotnet test tests/DigitalBrain.TestingTests/DigitalBrain.TestingTests.csproj -c Release --filter "FullyQualifiedName~ClockAndFaultContracts.Clock" --logger "console;verbosity=minimal"
```

Expected: PASS with no wall-clock wait.

- [ ] **Step 7: Commit deterministic time**

```powershell
git add src/DigitalBrain.Testing tests/DigitalBrain.TestingTests
git commit -m "feat(testing): drive deterministic test time"
```

---

### Task 6: Close faults around TestNeuron and support host restart

**Files:**
- Create: `src/DigitalBrain.Testing/Faults/JournalFaultHandle.cs`
- Modify: `src/DigitalBrain.Testing/RecordingJournalStorageProvider.cs`
- Modify: `src/DigitalBrain.Testing/TestNeuron.cs`
- Modify: `src/DigitalBrain.Testing/Cluster/FixtureCluster.cs`
- Modify: `tests/DigitalBrain.TestingTests/ClockAndFaultContracts.cs`

**Interfaces:**
- Produces:

```csharp
public JournalFaultHandle FailNextJournalCommit(string message);
public JournalFaultHandle FailJournalCommitAfter(int completedWrites, string message);
public Task RestartHostAsync(CancellationToken cancellationToken = default);
```

- [ ] **Step 1: Write fault and restart tests**

Test that:

1. a fault is scoped to exactly one `TestNeuron`;
2. the requested commit throws the supplied message;
3. leaving an untriggered fault undisposed causes method cleanup to fail;
4. explicitly disposing an untriggered fault disarms it without a cleanup failure;
5. restart preserves committed outgoing journal evidence and the production reference becomes usable
   again.

Use:

```csharp
await using var fault = echo.FailNextJournalCommit("expected commit failure");
var failure = await Assert.ThrowsAsync<InvalidOperationException>(
    () => test.Client.SendAsync<IEchoNeuron>("primary", new EchoRequested("Ada")));
Assert.Equal("expected commit failure", failure.Message);
```

- [ ] **Step 2: Run and verify red**

```powershell
dotnet test tests/DigitalBrain.TestingTests/DigitalBrain.TestingTests.csproj -c Release --filter "FullyQualifiedName~ClockAndFaultContracts.Fault|FullyQualifiedName~ClockAndFaultContracts.Restart" --logger "console;verbosity=minimal"
```

Expected: build FAIL.

- [ ] **Step 3: Make journal injection report consumption**

`RecordingJournalStorageProvider` stores a per-grain fault object containing remaining writes,
message, and a `TaskCompletionSource` completed when the failure is thrown. It exposes only internal
arm/disarm operations using `NeuronId`; conversion to `GrainId` remains inside Testing.

`JournalFaultHandle.DisposeAsync` disarms the target. `TestBrain.DisposeAsync` reports every handle
that was neither consumed nor explicitly disposed before method cleanup.

- [ ] **Step 4: Move restart logic behind TestNeuron**

Port the existing management-grain placement lookup and `RestartSiloAsync` implementation into
instance-owned `FixtureCluster.RestartHostAsync(NeuronId, CancellationToken)`. After restart, await
client membership by calling `IManagementGrain.GetHosts()` with the test cancellation token and
record the transition. Do not expose silo address or cluster handles.

- [ ] **Step 5: Run fault/restart tests twice**

```powershell
dotnet test tests/DigitalBrain.TestingTests/DigitalBrain.TestingTests.csproj -c Release --filter "FullyQualifiedName~ClockAndFaultContracts" --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.TestingTests/DigitalBrain.TestingTests.csproj -c Release --filter "FullyQualifiedName~ClockAndFaultContracts" --logger "console;verbosity=minimal"
```

Expected: both PASS.

- [ ] **Step 6: Commit closed faults**

```powershell
git add src/DigitalBrain.Testing tests/DigitalBrain.TestingTests
git commit -m "feat(testing): add scoped durability faults and restart"
```

---

### Task 7: Attach bounded BrainTestArtifact evidence

**Files:**
- Create: `src/DigitalBrain.Testing/Diagnostics/BrainTestArtifact.cs`
- Create: `src/DigitalBrain.Testing/Diagnostics/BrainTestFailureException.cs`
- Modify: `src/DigitalBrain.Testing/TestBrain.cs`
- Modify: operations in `TestClock`, `TestJournal`, `TestNeuron`, and fault handles
- Create: `tests/DigitalBrain.TestingTests/ArtifactContracts.cs`

**Interfaces:**
- Produces:

```csharp
public sealed class BrainTestFailureException : Exception
{
    public BrainTestArtifact Artifact { get; }
}
```

- [ ] **Step 1: Write artifact bounds and attachment tests**

Assert an untriggered fault cleanup failure contains:

- fixture/module IDs;
- scoped owners;
- clock origin/advance;
- target neuron;
- fault state;
- cleanup stage.

Assert xUnit contains an attachment named `digitalbrain-test.json` after the framework records a
failure:

```csharp
Assert.Contains(
    "digitalbrain-test.json",
    TestContext.Current.Attachments.Keys);
```

Add a loop recording more than the bounds and assert:

```text
owners <= 32
events <= 512
faults <= 32
strings <= 2048 chars each
serialized JSON <= 1 MiB
```

- [ ] **Step 2: Run and verify red**

```powershell
dotnet test tests/DigitalBrain.TestingTests/DigitalBrain.TestingTests.csproj -c Release --filter "FullyQualifiedName~ArtifactContracts" --logger "console;verbosity=minimal"
```

Expected: build FAIL.

- [ ] **Step 3: Implement the bounded artifact**

Use immutable snapshots over bounded internal ring buffers. Serialize with source-generated
`System.Text.Json` metadata. Redact values whose diagnostic keys contain `secret`, `token`, `key`,
`authorization`, or `password` using ordinal-ignore-case matching.

- [ ] **Step 4: Attach through the exact xUnit v3 API**

Use:

```csharp
TestContext.Current.AddAttachment(
    "digitalbrain-test.json",
    artifact.ToJson());
```

Call it when a framework operation fails, cancellation/timeout is converted to a framework
diagnostic, or cleanup detects a leak. Preserve the original exception as `InnerException`.

- [ ] **Step 5: Run artifact and lifecycle tests**

```powershell
dotnet test tests/DigitalBrain.TestingTests/DigitalBrain.TestingTests.csproj -c Release --filter "FullyQualifiedName~ArtifactContracts|FullyQualifiedName~FixtureLifecycleContracts" --logger "console;verbosity=minimal"
```

Expected: PASS.

- [ ] **Step 6: Commit diagnostics**

```powershell
git add src/DigitalBrain.Testing tests/DigitalBrain.TestingTests
git commit -m "feat(testing): attach bounded brain test evidence"
```

---

### Task 8: Add typed external-edge controls without DI escape

**Files:**
- Replace: `src/DigitalBrain.Testing/TestingEdges.cs`
- Create: `src/DigitalBrain.Testing/Edges/TestEdgeRegistry.cs`
- Modify: `src/DigitalBrain.Testing/DigitalBrainTestBuilder.cs`
- Modify: `src/DigitalBrain.Testing/Cluster/FixtureCluster.cs`
- Create: `tests/DigitalBrain.TestingTests/EdgeContracts.cs`

**Interfaces:**
- Consumes: fixture serialization and generated module composition
- Produces: internal closed edge registry and extension points for module-owned typed controls

- [ ] **Step 1: Write negative escape-hatch tests**

Assert `DigitalBrainTestBuilder` has no public property/method involving:

```text
IServiceCollection
IServiceProvider
ISiloBuilder
IHostBuilder
Action<IServiceCollection>
Action<ISiloBuilder>
```

Assert a test edge can be declared only from this enum:

```csharp
internal enum TestEdgeKind
{
    ChatClient,
    SouthboundMcpTransport,
    OAuthParameters,
    TimeProvider,
}
```

- [ ] **Step 2: Run and verify red**

```powershell
dotnet test tests/DigitalBrain.TestingTests/DigitalBrain.TestingTests.csproj -c Release --filter "FullyQualifiedName~EdgeContracts" --logger "console;verbosity=minimal"
```

Expected: FAIL against the old string constant catalog and any remaining serializer/DI mutation API.

- [ ] **Step 3: Implement the internal registry**

`TestEdgeRegistry` stores one assembly-configured adapter per `TestEdgeKind` and resets its
method-scoped script before each `TestBrain`. Module-owned public extensions accept
`DigitalBrainTestBuilder` or `TestBrain` and call hidden, `[EditorBrowsable(Never)]` typed methods;
they never receive `IServiceCollection`.

Keep the initial implementation minimal: port the existing `IChatClient` and MCP test adapters that
already have consumers. OAuth parameters and time are framework-owned value/script slots. Do not
create a generic `Substitute<T>()`.

- [ ] **Step 4: Run edge and package-shape tests**

```powershell
dotnet test tests/DigitalBrain.TestingTests/DigitalBrain.TestingTests.csproj -c Release --filter "FullyQualifiedName~EdgeContracts|FullyQualifiedName~PublicSurfaceContracts" --logger "console;verbosity=minimal"
```

Expected: PASS.

- [ ] **Step 5: Commit the closed edge seam**

```powershell
git add src/DigitalBrain.Testing tests/DigitalBrain.TestingTests
git commit -m "refactor(testing): close external edge substitution"
```

---

### Task 9: Migrate retained L1 proofs and delete process-global test state

**Files:**
- Create: `tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj`
- Create: `tests/DigitalBrain.ModuleTests/AssemblyInfo.cs`
- Create: `tests/DigitalBrain.ModuleTests/ModuleFixture.cs`
- Move/rewrite retained contracts from `tests/DigitalBrain.Simulations/*.cs`
- Modify: `DigitalBrain.slnx`

**Interfaces:**
- Consumes: only public `DigitalBrain.Testing` APIs for module tests
- Produces: focused kernel/module L1 suite without statics

- [ ] **Step 1: Create the replacement module-test project**

Use the same xUnit project shape as `DigitalBrain.TestingTests`, plus references to AI, Tasks,
Google, Salesforce, and MCP runtime packages. Do not reference
`Microsoft.Orleans.TestingHost`, `Aspire.Hosting.Testing`, or expose kernel internals.

Register:

```csharp
[assembly: AssemblyFixture(typeof(ModuleFixture))]
```

`ModuleFixture.Configure` selects `AIModule`, `TasksModule`, `GoogleModule`, `SalesforceModule`, and
no sample assembly. AccountEnrichment is not a module marker today; its process-global composition
tests are deleted here and a future sample deepening must first give it the same Contracts/runtime
shape proved by Quickstart.

- [ ] **Step 2: Port the kernel/fabric acceptance matrix**

Retain one typed proof for each invariant:

```text
owner authorization
exactly-once inbound dedupe
durable incoming/outgoing journal cursor
broadcast routing and cycle stop
multi-silo placement
journal commit rollback
capability delegation causal completion
watch resume after cursor
```

Each test:

1. opens one `TestBrain`;
2. creates typed logical owners and `TestNeuron<T>`;
3. stimulates through `IDigitalBrain` or a typed neuron reference;
4. synchronizes through `Incoming`/`Outgoing.NextAsync<T>`;
5. uses xUnit assertions;
6. uses `TestNeuron` fault/restart controls where needed.

Delete duplicate tests that only prove the same invariant through raw `IGrainFactory` or static
counters.

- [ ] **Step 3: Port module semantic proofs**

Retain:

```text
AI: typed ILLM response, durable direct session/checkpoint, one group-chat success/failure/cancel
Tasks: start, waiting blocker, progress, success, failure, cancellation, retry successor
Google: typed Gmail mapping with southbound MCP edge
Salesforce: proposal, exact approval evidence, uncertain mutation reconciliation
```

Replace process-global gates/counters with method-scoped edge scripts, typed journals, or durable
facts. Delete tests whose only purpose is an internal implementation branch already covered by a
public invariant.

- [ ] **Step 4: Leave natural-language bindings to Task 10**

Keep `.feature` files out of this commit. C# contracts must be green before the natural-language
adapter is changed.

- [ ] **Step 5: Run the replacement suite twice**

```powershell
dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --logger "console;verbosity=minimal"
```

Expected: both PASS with one assembly cluster per run.

- [ ] **Step 6: Prove no process-global probes remain in retained tests**

```powershell
rg -n "static\s+(readonly\s+)?Concurrent(Dictionary|Bag|Queue)|static\s+TaskCompletionSource|SimulationCluster|IGrainFactory|GrainId" tests/DigitalBrain.ModuleTests
```

Expected: no matches.

- [ ] **Step 7: Commit the focused L1 suite**

```powershell
git add DigitalBrain.slnx tests/DigitalBrain.ModuleTests
git commit -m "test: migrate retained module proofs to TestBrain"
```

---

### Task 10: Generate thin Gherkin vocabulary over TestBrain

**Files:**
- Create: `src/DigitalBrain.Testing/Gherkin/GeneratedVocabularySteps.cs`
- Modify: `src/DigitalBrain.SourceGeneration/DispatchManifestGenerator.cs`
- Move: retained `.feature` files to `tests/DigitalBrain.ModuleTests/Features/`
- Create: `tests/DigitalBrain.ModuleTests/Features/Bindings.cs`
- Modify: `tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj`

**Interfaces:**
- Consumes: generated module neuron/synapse vocabulary and public TestBrain APIs
- Produces: compile-time Gherkin name resolution with no reflection catalog

- [ ] **Step 1: Add an architectural red test**

Assert the bindings assembly source contains none of:

```text
IGrainFactory
GrainId
SimulationCluster
AppDomain
Assembly.GetTypes
NeuronCatalog
```

Assert generated vocabulary maps fully-qualified contract/synapse identity to a factory delegate and
that short names are accepted only when unique across selected modules.

- [ ] **Step 2: Run and verify red**

```powershell
dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --filter "FullyQualifiedName~GherkinArchitecture" --logger "console;verbosity=minimal"
```

Expected: FAIL because old steps and reflection catalog remain.

- [ ] **Step 3: Extend source generation**

Emit a test-assembly-local catalog:

```csharp
internal static class GeneratedTestVocabulary
{
    internal static bool TryResolveNeuron(string name, out TestNeuronContract contract);
    internal static bool TryCreateSynapse(
        string name,
        IReadOnlyDictionary<string, string> arguments,
        out Synapse synapse);
}
```

Factories are direct compiled delegates over discovered public contract constructors/properties.
Ambiguous short names produce a generator diagnostic listing fully-qualified candidates. Do not use
`Activator.CreateInstance` at runtime.

- [ ] **Step 4: Implement thin bindings**

Bindings obtain the assembly `ModuleFixture`, open one `TestBrain` per Reqnroll scenario, call
generated factories, send through `IDigitalBrain`, and observe through typed/non-generic internal
adapters over `TestJournal`. They contain no module-specific switch statement.

- [ ] **Step 5: Retain only product-readable features**

Port these feature intents:

```text
owner authorization
durable incoming/outgoing journals
cycle-safe fabric
multi-silo durability
client send/emit
```

Delete generated `.feature.cs` files from source control if Reqnroll generates them during build.
Delete feature wording that names Simulation, grain IDs, silos as direct APIs, or test-driver neurons.

- [ ] **Step 6: Run Gherkin and C# L1**

```powershell
dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --logger "console;verbosity=minimal"
```

Expected: PASS.

- [ ] **Step 7: Commit thin natural-language testing**

```powershell
git add src/DigitalBrain.SourceGeneration src/DigitalBrain.Testing tests/DigitalBrain.ModuleTests
git commit -m "feat(testing): generate thin Gherkin vocabulary"
```

---

### Task 11: Delete Simulation/Scenario and finish package guards

**Files:**
- Delete: old `src/DigitalBrain.Testing` files listed in File structure
- Delete: `tests/DigitalBrain.Simulations/`
- Modify: `src/DigitalBrain.Testing/DigitalBrain.Testing.csproj`
- Modify: `DigitalBrain.slnx`
- Modify: `tests/DigitalBrain.Tests/PackableSurfaceContracts.cs`
- Modify: `tests/DigitalBrain.Tests/ArchitectureCutContracts.cs`
- Modify: `docs/packages.md`
- Modify: `docs/architecture.md`
- Modify: `docs/concepts.md`
- Modify: `docs/index.md`

**Interfaces:**
- Consumes: all migrated L1 consumers
- Produces: one packable L1 testing product with no old vocabulary

- [ ] **Step 1: Add the final forbidden-surface tests**

Reject exported or source-visible public:

```text
Simulation
Simulations
Scenario
SimulationCluster
ScenarioClock
ScenarioStages
SimulationAssertionException
ISimulationNeuron
IBehavior
IBehaviorTest
BehaviorFixture
FaultPoint
raw Grains
AddJsonSerializer
StartAsync/StopAsync cluster controls
Expect/Should/matcher assertion APIs
arbitrary delay/settle/eventually helpers
```

Allow the words only under `docs/superpowers/**` and git history.

- [ ] **Step 2: Remove old project/source consumers**

Delete `tests/DigitalBrain.Simulations` from `DigitalBrain.slnx`, then delete its directory. Delete
the old Testing source files only after:

```powershell
rg -n "Simulation|Scenario|SimulationCluster|NeuronCatalog" src tests hosts samples --glob "*.cs"
```

shows references confined to files being deleted.

- [ ] **Step 3: Update package metadata**

Set the description to:

```xml
<Description>Development-only DigitalBrain testing: assembly-owned real multi-silo fixtures, method-scoped TestBrain, deterministic time, typed journal evidence, closed durability faults, and exclusive Aspire AppHost testing.</Description>
```

Keep `IsPackable=true`. Ensure the package includes the source generator analyzer needed for external
module capsules/vocabulary, matching the Kernel analyzer packaging pattern.

- [ ] **Step 4: Update durable docs**

Replace Simulation/Scenario terminology with `DigitalBrainFixture`, `TestBrain`, `TestOwner`, and
`TestNeuron<T>`. Preserve `Behavior` exclusively as a user-authored product class/file and ordinary
test-class suffix; the framework defines no `IBehavior`, `IBehaviorTest`, or `BehaviorFixture`.
Document serialized within-fixture execution and parallelism across assemblies.

- [ ] **Step 5: Run all L0 and L1 gates**

```powershell
dotnet build DigitalBrain.slnx -c Release
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.TestingTests/DigitalBrain.TestingTests.csproj -c Release --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --logger "console;verbosity=minimal"
npm --prefix docs test
npm --prefix docs run build
```

Expected: PASS.

- [ ] **Step 6: Verify the deletion budget**

```powershell
rg -n "Simulation|Simulations|Scenario|SimulationCluster|NeuronCatalog|IGrainFactory|GrainId" src/DigitalBrain.Testing tests/DigitalBrain.ModuleTests --glob "*.cs"
```

Expected: no old vocabulary and no raw Orleans types in public/module tests. Internal
`FixtureCluster` may reference Orleans implementation types.

- [ ] **Step 7: Commit the clean cut**

```powershell
git add DigitalBrain.slnx src/DigitalBrain.Testing tests docs
git restore --staged Directory.Packages.props
git commit -m "refactor(testing): replace simulations with TestBrain"
```

---

## Plan 2 completion gate

Run:

```powershell
dotnet build DigitalBrain.slnx -c Release
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.TestingTests/DigitalBrain.TestingTests.csproj -c Release --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --logger "console;verbosity=minimal"
npm --prefix docs test
npm --prefix docs run build
git status --short
```

Expected:

- every command passes;
- the only unrelated unstaged change remains `Directory.Packages.props`;
- no public/static Simulation or Scenario path exists;
- no module test references Orleans or Aspire;
- all waits use committed journal evidence, cancellation, resource notification, or deterministic
  clock driving;
- one `DigitalBrainFixture` owns one three-silo cluster and admits one active method handle.
