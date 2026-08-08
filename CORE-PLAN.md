# Stage A / Plan 1 — Foundations: pins, ABI, proofs of physics

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development
> (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps
> use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Kill every Stage A uncertainty against the compiler: pin the Reqnroll/xunit.v3/MTP
stack, land the two-concept ABI beside the old surface, prove `Synapse<TResult>` polymorphic
serialization on a live TestCluster, and prove restart-surviving journals through the
TestBrain seam — nothing else.

**Architecture:** New vocabulary lives in namespace `DigitalBrain` inside the existing
`DigitalBrain.Abstractions` assembly (folder `Core/`; folders organize, namespaces carry
meaning); old namespace `DigitalBrain.Abstractions` is untouched until Stage B. One new test
project is the only other artifact. Every task traces to a spec law or open item
(CORE-DESIGN.md v3); anything without a consumer here is deliberately absent — the runtime,
the facade, the registry are Plan 2, written only after this plan's pins make them precise.

**Tech Stack:** .NET 11 preview (existing), Orleans 10.2.x + Orleans.Journaling (existing
pins), Reqnroll.xunit.v3, xunit.v3, Microsoft.Orleans.TestingHost.

## Global Constraints

- **Commits require the owner's explicit approval.** Every task ends at a green boundary
  with the diff shown in chat; the executor STOPS there. A "yes" to a side question is not
  approval.
- **No comments as narrative, no `/// <summary>` boilerplate.** Names carry meaning.
- **One top-level type per file**, except a closed family read as a set (named for the family).
- **Central package management**: versions only in `Directory.Packages.props`, latest
  deliberate versions.
- **Never `--filter` and never `--nologo` on `dotnet test`** (`--nologo` breaks the MTP
  handshake — documented trap). During a task, run the smallest owning project; the root
  gate (`dotnet build DigitalBrain.slnx -c Release` then `dotnet test DigitalBrain.slnx -c
  Release`) is what permits a completion claim.
- **Context7 before any API not already proven in this repo**; the compiler is the oracle
  for existence, a live run for behavior.
- **The root gate is never red**: scenarios that outrun the runtime enter behind
  `@ignore("pending: law N")` (spec law 10).
- Spec: `CORE-DESIGN.md` v3 at repo root. Laws cited as L1–L10, open items as OI-1..7.

---

### Task 1: Test project + the pinned BDD stack (kills OI-5)

**Files:**
- Modify: `Directory.Packages.props` (three `PackageVersion` entries)
- Create: `src/core/kernel/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj`
- Create: `src/core/kernel/DigitalBrain.Core.Tests/Features/Stack.feature`
- Create: `src/core/kernel/DigitalBrain.Core.Tests/Features/StackSteps.cs`
- Modify: `DigitalBrain.slnx` (via `dotnet sln add`)

**Interfaces:**
- Consumes: existing CPM pins (read `Directory.Packages.props` first; reuse the exact
  Orleans version already pinned there for `Microsoft.Orleans.TestingHost`).
- Produces: a test project every later task adds files to; the proven
  `Reqnroll.xunit.v3` + `xunit.v3` version pair (recorded in `Directory.Packages.props`).

- [ ] **Step 1: Read current pins**

Run: `Get-Content Directory.Packages.props` — note the `Microsoft.Orleans.*` version (call
it `ORLEANS_VER` below) and confirm no `xunit`/`Reqnroll` pins exist yet.

- [ ] **Step 2: Resolve latest candidate versions**

Run: `dotnet package search Reqnroll.xunit.v3 --exact-match --take 1` and
`dotnet package search xunit.v3 --exact-match --take 1`.
Record both. If the build in Step 6 fails inside the generated `*.feature.cs`, the xunit.v3
pin steps DOWN major-first until it compiles — that final pair IS the deliverable of OI-5.

- [ ] **Step 3: Add pins to `Directory.Packages.props`**

```xml
<PackageVersion Include="Reqnroll.xunit.v3" Version="RESOLVED_IN_STEP_2" />
<PackageVersion Include="xunit.v3" Version="RESOLVED_IN_STEP_2" />
<PackageVersion Include="Microsoft.Orleans.TestingHost" Version="ORLEANS_VER" />
```

- [ ] **Step 4: Create the project**

`src/core/kernel/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <OutputType>Exe</OutputType>
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
    <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
    <NoWarn>$(NoWarn);CA1515</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Reqnroll.xunit.v3" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="Microsoft.Orleans.TestingHost" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\DigitalBrain.Abstractions\DigitalBrain.Abstractions.csproj" />
    <ProjectReference Include="..\DigitalBrain.Core\DigitalBrain.Core.csproj" />
  </ItemGroup>

  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>

</Project>
```

Run: `dotnet sln DigitalBrain.slnx add src/core/kernel/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj`

- [ ] **Step 5: Write the stack-proof feature**

`Features/Stack.feature`:

```gherkin
Feature: Stack
  The BDD stack itself is provable: a Gherkin scenario compiles through the Reqnroll
  generator against the pinned xunit.v3 and executes under Microsoft.Testing.Platform.

Scenario: A scenario runs and can fail
  Given the number 2
  When 2 is added
  Then the sum is 4
```

`Features/StackSteps.cs`:

```csharp
using Reqnroll;

namespace DigitalBrain.Core.Tests;

[Binding]
public sealed class StackSteps
{
    private int _value;

    [Given("the number {int}")]
    public void GivenTheNumber(int value) => _value = value;

    [When("{int} is added")]
    public void WhenIsAdded(int addend) => _value += addend;

    [Then("the sum is {int}")]
    public void ThenTheSumIs(int expected) => Assert.Equal(expected, _value);
}
```

- [ ] **Step 6: Build; resolve the pin pair**

Run: `dotnet build src/core/kernel/DigitalBrain.Core.Tests -c Release`
Expected: 0 errors. If the generated `Stack.feature.cs` fails against xunit.v3 APIs, step
the xunit.v3 pin down (Step 2 rule) and rebuild until green; update
`Directory.Packages.props` with the surviving pair.

- [ ] **Step 7: Run and verify the gate can fail (L10)**

Run: `dotnet test src/core/kernel/DigitalBrain.Core.Tests -c Release`
Expected: 1 passed. Then temporarily change `4` to `5` in the feature, rerun, expect
1 FAILED; revert. A gate that cannot fail is a defect — prove this one can.

- [ ] **Step 8: Stop — show the diff to the owner for commit approval**

Proposed message: `Stage A: pin Reqnroll/xunit.v3/MTP stack with a fallible proof (OI-5)`

---

### Task 2: The ABI — namespace `DigitalBrain` (L1, L2 vocabulary)

**Files:**
- Create: `src/core/kernel/DigitalBrain.Abstractions/Core/Synapse.cs`
- Create: `src/core/kernel/DigitalBrain.Abstractions/Core/IHandle.cs`
- Create: `src/core/kernel/DigitalBrain.Abstractions/Core/IAddressed.cs`

**Interfaces:**
- Consumes: nothing — this is the dependency root of the new surface.
- Produces (exact, later tasks and Plan 2 compile against these):
  `DigitalBrain.Synapse`; `DigitalBrain.Synapse<TResult> where TResult : Synapse`;
  `DigitalBrain.IHandle<TSynapse>.HandleAsync(TSynapse, CancellationToken) : Task`;
  `DigitalBrain.IHandle<TSynapse, TResult>.HandleAsync(TSynapse, CancellationToken) : Task<TResult>`;
  `DigitalBrain.IAddressed.Neuron : string`.

- [ ] **Step 1: Write `Core/Synapse.cs`** (one closed family, one file)

```csharp
namespace DigitalBrain;

public abstract record Synapse;

public abstract record Synapse<TResult> : Synapse
    where TResult : Synapse;
```

- [ ] **Step 2: Write `Core/IHandle.cs`** (one closed family, one file; no variance — L4
  matches exact types, declared variance would promise conversions the registry refuses)

```csharp
namespace DigitalBrain;

public interface IHandle<TSynapse> where TSynapse : Synapse
{
    Task HandleAsync(TSynapse synapse, CancellationToken cancellationToken);
}

public interface IHandle<TSynapse, TResult>
    where TSynapse : Synapse<TResult>
    where TResult : Synapse
{
    Task<TResult> HandleAsync(TSynapse synapse, CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Write `Core/IAddressed.cs`**

```csharp
namespace DigitalBrain;

public interface IAddressed
{
    string Neuron { get; }
}
```

- [ ] **Step 4: Root build — the old surface must not notice**

Run: `dotnet build DigitalBrain.slnx -c Release`
Expected: 0 errors, 0 new warnings. The bare `DigitalBrain` namespace must not collide with
any existing type (it holds none today — verify any failure before touching old code).

- [ ] **Step 5: Stop — show the diff to the owner for commit approval**

Proposed message: `Stage A: the two-concept ABI — Synapse, Synapse<TResult>, IHandle (L1, L2)`

---

### Task 3: Serialization proof on a live cluster (kills OI-1)

**Files:**
- Create: `src/core/kernel/DigitalBrain.Core.Tests/Probes/ProbeSynapses.cs`
- Create: `src/core/kernel/DigitalBrain.Core.Tests/Probes/IEchoGrain.cs`
- Create: `src/core/kernel/DigitalBrain.Core.Tests/Probes/EchoGrain.cs`
- Create: `src/core/kernel/DigitalBrain.Core.Tests/TestBrain.cs` (cluster only in this task;
  grows seams in Task 4)
- Create: `src/core/kernel/DigitalBrain.Core.Tests/SerializationFacts.cs`

These are physics spikes — plain facts, not Gherkin: they pin the compiler/wire oracle that
scenarios later stand on. Behavior gets Gherkin; physics gets facts.

**Interfaces:**
- Consumes: Task 2 ABI.
- Produces: `TestBrain.StartAsync() : Task<TestBrain>`, `TestBrain.Cluster : TestCluster`,
  `TestBrain.DisposeAsync()`; probe vocabulary `Greeted(string Text)`,
  `CountRequested(int By) : Synapse<Counted>`, `Counted(int Total)`.

- [ ] **Step 1: Write the probe vocabulary** — `Probes/ProbeSynapses.cs` (closed family file)

```csharp
namespace DigitalBrain.Core.Tests;

[GenerateSerializer]
[Alias("db.test.greeted")]
public sealed record Greeted(string Text) : Synapse;

[GenerateSerializer]
[Alias("db.test.count-requested")]
public sealed record CountRequested(int By) : Synapse<Counted>;

[GenerateSerializer]
[Alias("db.test.counted")]
public sealed record Counted(int Total) : Synapse;
```

Leaves carry `[GenerateSerializer]` + `[Alias]`; the abstract bases carry nothing. If
Step 6 fails to round-trip, the recorded fallback is attributes on the bases — whichever
branch the compiler and wire accept IS the alias policy, recorded in the spec's OI-1.

- [ ] **Step 2: Write `Probes/IEchoGrain.cs`**

```csharp
namespace DigitalBrain.Core.Tests;

[Alias("db.test.echo")]
public interface IEchoGrain : IGrainWithStringKey
{
    [Alias("echo")]
    Task<Synapse> Echo(Synapse synapse);
}
```

- [ ] **Step 3: Write `Probes/EchoGrain.cs`**

```csharp
namespace DigitalBrain.Core.Tests;

public sealed class EchoGrain : Grain, IEchoGrain
{
    public Task<Synapse> Echo(Synapse synapse) => Task.FromResult(synapse);
}
```

- [ ] **Step 4: Write `TestBrain.cs` — the single-configurator cluster**

```csharp
using Orleans.TestingHost;

namespace DigitalBrain.Core.Tests;

public sealed class TestBrain : IAsyncDisposable
{
    private TestBrain(TestCluster cluster) => Cluster = cluster;

    public TestCluster Cluster { get; }

    public static async Task<TestBrain> StartAsync()
    {
        var builder = new TestClusterBuilder(initialSilosCount: 1);
        builder.AddSiloBuilderConfigurator<BrainSiloConfigurator>();
        var cluster = builder.Build();
        await cluster.DeployAsync();
        return new TestBrain(cluster);
    }

    public async ValueTask DisposeAsync()
    {
        await Cluster.StopAllSilosAsync();
        await Cluster.DisposeAsync();
    }
}

public sealed class BrainSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
    }
}
```

Oracle note: `TestClusterBuilder(initialSilosCount: 1)` and `ISiloConfigurator` are the
shapes this repo's lineage ran on TestingHost 10.2.x; if the pinned TestingHost's ctor
differs, the compiler says so in Step 6 and the fix is mechanical (builder option property).

- [ ] **Step 5: Write `SerializationFacts.cs`**

```csharp
using Orleans.Serialization;

namespace DigitalBrain.Core.Tests;

public sealed class SerializationFacts
{
    [Fact(DisplayName = "A leaf synapse round-trips the wire polymorphically as its concrete type")]
    public async Task WireRoundTripPreservesConcreteType()
    {
        await using var brain = await TestBrain.StartAsync();
        var echo = brain.Cluster.Client.GetGrain<IEchoGrain>("probe");

        var greeted = await echo.Echo(new Greeted("hello"));
        var counted = await echo.Echo(new Counted(3));

        Assert.Equal(new Greeted("hello"), Assert.IsType<Greeted>(greeted));
        Assert.Equal(new Counted(3), Assert.IsType<Counted>(counted));
    }

    [Fact(DisplayName = "A directed request record serializes through its Synapse<TResult> base")]
    public async Task DirectedRequestRoundTripsThroughGenericBase()
    {
        await using var brain = await TestBrain.StartAsync();
        var echo = brain.Cluster.Client.GetGrain<IEchoGrain>("probe");

        var request = await echo.Echo(new CountRequested(2));

        Assert.Equal(new CountRequested(2), Assert.IsType<CountRequested>(request));
    }

    [Fact(DisplayName = "The in-silo polymorphic serializer copies a synapse byte-for-byte")]
    public async Task SerializerRoundTripsInProcess()
    {
        await using var brain = await TestBrain.StartAsync();
        var serializer = brain.Cluster.ServiceProvider.GetRequiredService<Serializer<Synapse>>();

        var bytes = serializer.SerializeToArray(new CountRequested(5));
        var back = serializer.Deserialize(bytes);

        Assert.Equal(new CountRequested(5), Assert.IsType<CountRequested>(back));
    }
}
```

- [ ] **Step 6: Run — first to fail honestly, then to pass**

Run: `dotnet test src/core/kernel/DigitalBrain.Core.Tests -c Release`
Expected: all three pass. If the generic-base round-trip fails with a codec/unknown-type
error: apply the recorded fallback (attributes on the abstract bases), rerun, and record
the surviving policy. Either outcome closes OI-1 — that is this task's meaning.

- [ ] **Step 7: Stop — show the diff to the owner for commit approval**

Proposed message: `Stage A: Synapse<TResult> serialization proven on a live cluster (OI-1)`

---

### Task 4: TestBrain physics seams — restart-surviving journals (L5, harness seam 1)

**Files:**
- Modify: `src/core/kernel/DigitalBrain.Core.Tests/TestBrain.cs` (add `RestartAsync`,
  journal-store seam)
- Create: `src/core/kernel/DigitalBrain.Core.Tests/Probes/IJournalProbeGrain.cs`
- Create: `src/core/kernel/DigitalBrain.Core.Tests/Probes/JournalProbeGrain.cs`
- Create: `src/core/kernel/DigitalBrain.Core.Tests/Features/Physics.feature`
- Create: `src/core/kernel/DigitalBrain.Core.Tests/Features/PhysicsSteps.cs`

**Interfaces:**
- Consumes: Task 3's `TestBrain`; the journal-storage registration pattern from
  `src/core/kernel/DigitalBrain.Core/Hosting/JournalStorageHosting.cs` (read it first —
  it is the in-repo oracle for the Orleans.Journaling registration API; mirror it with the
  test-side provider).
- Produces: `TestBrain.RestartAsync() : Task` (silo restart, journals survive);
  the process-static journal store every later restart scenario rides.

- [ ] **Step 1: Read the in-repo oracle**

Run: `Get-Content src/core/kernel/DigitalBrain.Core/Hosting/JournalStorageHosting.cs`
Note the exact `AddJournalStorage`/format registration calls and the storage-provider
service type it binds — the test configurator mirrors these against a process-static
instance instead of the product store.

- [ ] **Step 2: Add the seam to `BrainSiloConfigurator`**

```csharp
public sealed class BrainSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        siloBuilder.AddJournalStorage();
        siloBuilder.UseJsonJournalFormat(JournalJsonContext.Default);
        siloBuilder.Services.AddSingleton(ProcessJournalStore.Provider);
    }
}

internal static class ProcessJournalStore
{
    internal static readonly IJournalStorageProvider Provider = new VolatileJournalStorageProvider();
}
```

Oracle note: the singleton's service type and the provider class name come from Step 1's
read — the shape above is the lineage's proven pattern (shared volatile provider =
restart survival in-process); the compiler corrects the exact identifiers.

- [ ] **Step 3: Add `RestartAsync` to `TestBrain`**

```csharp
public async Task RestartAsync()
{
    var silo = Cluster.Silos[0];
    await Cluster.RestartSiloAsync(silo);
}
```

- [ ] **Step 4: Write the journal probe grain pair**

`Probes/IJournalProbeGrain.cs`:

```csharp
namespace DigitalBrain.Core.Tests;

[Alias("db.test.journal-probe")]
public interface IJournalProbeGrain : IGrainWithStringKey
{
    [Alias("append")]
    Task Append(string entry);

    [Alias("read")]
    Task<IReadOnlyList<string>> Read();
}
```

`Probes/JournalProbeGrain.cs` (mirrors the product Neuron's keyed durable-state pattern —
`src/core/kernel/DigitalBrain.Core/Neuron/Neuron.cs:40-48` is the in-repo oracle):

```csharp
using Orleans.Journaling;

namespace DigitalBrain.Core.Tests;

public sealed class JournalProbeGrain : DurableGrain, IJournalProbeGrain
{
    private readonly IDurableList<string> _entries;

    public JournalProbeGrain()
        => _entries = ServiceProvider.GetRequiredKeyedService<IDurableList<string>>("probe");

    public async Task Append(string entry)
    {
        _entries.Add(entry);
        await WriteStateAsync();
    }

    public Task<IReadOnlyList<string>> Read()
        => Task.FromResult<IReadOnlyList<string>>([.. _entries]);
}
```

- [ ] **Step 5: Write the first real feature — spec P12, un-ignored**

`Features/Physics.feature`:

```gherkin
Feature: Physics
  The journal is the only truth, and truth survives a restart. (Law 5)

Scenario: Journals survive silo restart intact
  Given a journal probe named "diary" holding "first" and "second"
  When the silo restarts
  Then the probe named "diary" reads "first" and "second" in order
```

`Features/PhysicsSteps.cs`:

```csharp
using Reqnroll;

namespace DigitalBrain.Core.Tests;

[Binding]
public sealed class PhysicsSteps
{
    private TestBrain? _brain;

    [Given("a journal probe named {string} holding {string} and {string}")]
    public async Task GivenAProbeHolding(string name, string first, string second)
    {
        _brain = await TestBrain.StartAsync();
        var probe = _brain.Cluster.Client.GetGrain<IJournalProbeGrain>(name);
        await probe.Append(first);
        await probe.Append(second);
    }

    [When("the silo restarts")]
    public Task WhenTheSiloRestarts() => _brain!.RestartAsync();

    [Then("the probe named {string} reads {string} and {string} in order")]
    public async Task ThenTheProbeReads(string name, string first, string second)
    {
        var probe = _brain!.Cluster.Client.GetGrain<IJournalProbeGrain>(name);
        Assert.Equal([first, second], await probe.Read());
    }

    [AfterScenario]
    public async Task TearDown()
    {
        if (_brain is not null)
        {
            await _brain.DisposeAsync();
            _brain = null;
        }
    }
}
```

- [ ] **Step 6: Run — red first is expected and meaningful**

Run: `dotnet test src/core/kernel/DigitalBrain.Core.Tests -c Release`
First run may fail on the restarted silo losing its store — that failure is exactly what
the process-static seam exists to fix; iterate the Step 2 registration (per-silo vs shared
instance) until the scenario passes. If `RestartSiloAsync` does not exist on the pinned
TestingHost, the compiler names the actual member (`RestartStoppedSiloAsync` after a stop);
fix mechanically. This scenario green = harness seam 1 proven = the spec's P12.

- [ ] **Step 7: Stop — show the diff to the owner for commit approval**

Proposed message: `Stage A: restart-surviving journals through TestBrain (L5, P12)`

---

### Task 5: The architecture guard — the laws police themselves (L1, L2, L10)

**Files:**
- Create: `src/core/kernel/DigitalBrain.Core.Tests/ArchitectureGuardFacts.cs`

**Interfaces:**
- Consumes: Task 2 ABI + Task 3 probe vocabulary (the guard's first subjects).
- Produces: the reflection guard every future synapse and neuron in the new namespace is
  born under.

- [ ] **Step 1: Write the guard**

```csharp
using System.Reflection;

namespace DigitalBrain.Core.Tests;

public sealed class ArchitectureGuardFacts
{
    private static readonly Assembly[] Vocabulary =
        [typeof(Synapse).Assembly, typeof(ArchitectureGuardFacts).Assembly];

    private static IEnumerable<Type> ConcreteSynapses => Vocabulary
        .SelectMany(assembly => assembly.GetTypes())
        .Where(type => typeof(Synapse).IsAssignableFrom(type) && !type.IsAbstract);

    [Fact(DisplayName = "Every concrete synapse is serializable and alias-pinned")]
    public void EveryConcreteSynapseIsSerializableAndAliased()
    {
        foreach (var synapse in ConcreteSynapses)
        {
            Assert.True(
                synapse.GetCustomAttribute<GenerateSerializerAttribute>() is not null,
                $"{synapse.FullName} lacks [GenerateSerializer]");
            Assert.True(
                synapse.GetCustomAttribute<AliasAttribute>() is not null,
                $"{synapse.FullName} lacks [Alias]");
        }
    }

    [Fact(DisplayName = "Aliases are unique across the vocabulary")]
    public void AliasesAreUnique()
    {
        var duplicates = ConcreteSynapses
            .Select(synapse => synapse.GetCustomAttribute<AliasAttribute>()?.Alias)
            .Where(alias => alias is not null)
            .GroupBy(alias => alias)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        Assert.Empty(duplicates);
    }

    [Fact(DisplayName = "A directed synapse kind is declared only through its result-bearing handler")]
    public void DirectedKindsAreOnlyDeclaredWithResults()
    {
        foreach (var type in Vocabulary.SelectMany(assembly => assembly.GetTypes()))
        {
            var oneWayHandles = type.GetInterfaces()
                .Where(candidate => candidate.IsGenericType
                    && candidate.GetGenericTypeDefinition() == typeof(IHandle<>))
                .Select(candidate => candidate.GetGenericArguments()[0]);

            foreach (var synapse in oneWayHandles)
            {
                Assert.True(
                    synapse.BaseType is not { IsGenericType: true } baseType
                        || baseType.GetGenericTypeDefinition() != typeof(Synapse<>),
                    $"{type.FullName} declares one-way IHandle<{synapse.Name}> on a directed kind");
            }
        }
    }
}
```

- [ ] **Step 2: Run — then prove the guard can fail (L10)**

Run: `dotnet test src/core/kernel/DigitalBrain.Core.Tests -c Release`
Expected: all pass. Then add, in the test assembly only, a temporary
`sealed record Sneaky(int X) : Synapse;` with no attributes plus a
`sealed class SneakyHandler : IHandle<CountRequested>` — expect BOTH guard facts to fail —
then delete both. A guard that cannot fail is a defect.

- [ ] **Step 3: Root gate**

Run: `dotnet build DigitalBrain.slnx -c Release` then `dotnet test DigitalBrain.slnx -c Release`
Expected: green everywhere — the old surface untouched, the new one guarded.

- [ ] **Step 4: Stop — show the diff to the owner for commit approval**

Proposed message: `Stage A: architecture guard over the new vocabulary (L1, L2, L10)`

---

## What this plan deliberately does not contain

The runtime, `IDigitalBrain`, the registry, the envelope, the behavior kind, the delivery
gate, cycle guards — all Plan 2, and every line of Plan 2 will be written against the pins
and policies this plan PROVES rather than assumes. OI-6/OI-7 (generator-in-pack, ALC
unload) gate Stage D, not Stage A, and stay out. Nothing here exists without a consumer:
Task 1's consumer is every feature after it; Task 2's is Tasks 3 and 5; Task 3 closes OI-1;
Task 4 is P12 and harness seam 1; Task 5 is the law enforcing itself.
