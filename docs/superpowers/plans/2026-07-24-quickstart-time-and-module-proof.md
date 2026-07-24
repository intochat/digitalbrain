# Quickstart, Time, and Module Authoring Proof Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prove the external module author path with a split Quickstart package family, then add the settled durable one-shot `ICountdown` Time module without implementing open recurring/calendar shapes.

**Architecture:** Quickstart is a real Contracts/runtime/AppHost/test consumer of the compiled-module, one-call hosting, and `TestBrain` foundations. Time follows the same package shape and owns an addressable durable Countdown neuron. Before Time lands, kernel outbox wake-up moves to a composed internal reminder grain so public/module neurons no longer inherit and chain a kernel reminder hook.

**Tech Stack:** .NET 10, Orleans 10.2.2-rc.2 journaling/reminders, Aspire 13.4.6, xUnit v3, DigitalBrain.Testing.

## Global Constraints

- Execute after the compiled-module/hosting, L1 Testing, and L2 Testing plans.
- Work only in `E:\intochat\digitalbrain` on the current branch.
- Preserve the user's unstaged `Directory.Packages.props` line-ending change; never stage it.
- Contracts packages contain neuron interfaces and synapses only and reference approved leaf dependencies.
- Runtime packages contain module markers and neuron implementations.
- Do not create an empty `*.Aspire.Hosting` package. Quickstart and Time declare no external resource.
- Tests use only `DigitalBrain.Testing` public APIs; no Orleans or Aspire references in L1 projects.
- Quickstart contains no manual localhost silo, raw `NeuronId`, grain factory, observer, or static probe.
- `ICountdown` is the only Time capability implemented in this plan.
- Do not implement `IReminder`, recurring schedules, calendar rules, DST resolution, or a recurrence library.
- Time reuses the kernel reminder provider and adds no store.
- A Countdown is one neuron identity with one destination, generation, revision, and command receipts.
- Countdown delivery is never intentionally early and is emitted once across timer/reminder races.
- Every semantic neuron method omits `Async` and uses `[Alias(nameof(Method))]`.
- Persisted commands, snapshots, state, and facts use explicit stable aliases.

---

## File structure

### Quickstart create

```text
samples/DigitalBrain.Quickstart.Contracts/DigitalBrain.Quickstart.Contracts.csproj
samples/DigitalBrain.Quickstart.Contracts/IGreeter.cs
samples/DigitalBrain.Quickstart.Contracts/GreetingSynapses.cs
samples/DigitalBrain.Quickstart/DigitalBrain.Quickstart.csproj
samples/DigitalBrain.Quickstart/QuickstartModule.cs
samples/DigitalBrain.Quickstart/Greeter.cs
hosts/DigitalBrain.Quickstart.Host/DigitalBrain.Quickstart.Host.csproj
hosts/DigitalBrain.Quickstart.Host/Program.cs
hosts/DigitalBrain.Quickstart.AppHost/DigitalBrain.Quickstart.AppHost.csproj
hosts/DigitalBrain.Quickstart.AppHost/AppHost.cs
tests/DigitalBrain.Quickstart.Tests/DigitalBrain.Quickstart.Tests.csproj
tests/DigitalBrain.Quickstart.Tests/AssemblyInfo.cs
tests/DigitalBrain.Quickstart.Tests/QuickstartFixture.cs
tests/DigitalBrain.Quickstart.Tests/GreetingBehavior.cs
```

### Kernel reminder composition create

```text
src/DigitalBrain.Kernel/IOutboxDrain.cs
src/DigitalBrain.Kernel/IOutboxWakeup.cs
src/DigitalBrain.Kernel/OutboxWakeup.cs
tests/DigitalBrain.ModuleTests/KernelOutboxWakeupContracts.cs
```

### Time create

```text
modules/DigitalBrain.Modules.Time.Contracts/DigitalBrain.Modules.Time.Contracts.csproj
modules/DigitalBrain.Modules.Time.Contracts/ICountdown.cs
modules/DigitalBrain.Modules.Time.Contracts/CountdownCommands.cs
modules/DigitalBrain.Modules.Time.Contracts/CountdownSnapshot.cs
modules/DigitalBrain.Modules.Time.Contracts/CountdownElapsed.cs
modules/DigitalBrain.Modules.Time/DigitalBrain.Modules.Time.csproj
modules/DigitalBrain.Modules.Time/TimeModule.cs
modules/DigitalBrain.Modules.Time/CountdownNeuron.cs
modules/DigitalBrain.Modules.Time/CountdownState.cs
modules/DigitalBrain.Modules.Time/ICountdownWakeup.cs
tests/DigitalBrain.Time.Tests/DigitalBrain.Time.Tests.csproj
tests/DigitalBrain.Time.Tests/AssemblyInfo.cs
tests/DigitalBrain.Time.Tests/TimeFixture.cs
tests/DigitalBrain.Time.Tests/CountdownLifecycle.cs
tests/DigitalBrain.Time.Tests/CountdownRecovery.cs
```

### Delete

```text
samples/DigitalBrain.Quickstart/Program.cs
samples/DigitalBrain.Quickstart/Neurons.cs
samples/DigitalBrain.Quickstart/nuget.config
```

---

### Task 1: Split Quickstart Contracts from runtime

**Files:**
- Create: `samples/DigitalBrain.Quickstart.Contracts/DigitalBrain.Quickstart.Contracts.csproj`
- Create: `samples/DigitalBrain.Quickstart.Contracts/IGreeter.cs`
- Create: `samples/DigitalBrain.Quickstart.Contracts/GreetingSynapses.cs`
- Replace: `samples/DigitalBrain.Quickstart/DigitalBrain.Quickstart.csproj`
- Create: `samples/DigitalBrain.Quickstart/QuickstartModule.cs`
- Replace: `samples/DigitalBrain.Quickstart/Neurons.cs` with `Greeter.cs`
- Modify: `DigitalBrain.slnx`
- Modify: `tests/DigitalBrain.Tests/ModuleTemplateContracts.cs`

**Interfaces:**
- Produces:

```csharp
public partial interface IGreeter : INeuron;
public sealed record SayHello(string Name) : Synapse;
public sealed record Greeted(string Message) : Synapse;
public sealed partial class QuickstartModule : IModule;
```

- [ ] **Step 1: Add failing package-boundary assertions**

Extend `ModuleTemplateContracts` to include sample module families and assert:

```text
DigitalBrain.Quickstart.Contracts:
  project references = DigitalBrain.Abstractions only
  package references flowing to consumers = none
  no DigitalBrain.Kernel, Orleans.Server, Aspire, Client, Testing

DigitalBrain.Quickstart:
  references Quickstart.Contracts, Kernel, source generator analyzer
  OutputType absent (library)
```

Assert there is no `DigitalBrain.Quickstart.Aspire.Hosting` project because the module owns no
external resource.

- [ ] **Step 2: Run and verify red**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~ModuleTemplateContracts" --logger "console;verbosity=minimal"
```

Expected: FAIL because Quickstart is one executable project with package-local Orleans hosting.

- [ ] **Step 3: Create the Contracts project**

Use:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>true</IsPackable>
    <Description>Quickstart neuron and synapse contracts.</Description>
    <RootNamespace>DigitalBrain.Quickstart</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\DigitalBrain.Abstractions\DigitalBrain.Abstractions.csproj" />
    <ProjectReference Include="..\..\src\DigitalBrain.SourceGeneration\DigitalBrain.SourceGeneration.csproj"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false"
                      PrivateAssets="all" />
  </ItemGroup>
</Project>
```

`IGreeter.cs`:

```csharp
using DigitalBrain.Abstractions;

namespace DigitalBrain.Quickstart;

public partial interface IGreeter : INeuron;
```

`GreetingSynapses.cs`:

```csharp
using DigitalBrain.Abstractions;

namespace DigitalBrain.Quickstart;

[GenerateSerializer]
[Alias("quickstart.say-hello")]
public sealed record SayHello([property: Id(0)] string Name) : Synapse;

[GenerateSerializer]
[Alias("quickstart.greeted")]
public sealed record Greeted([property: Id(0)] string Message) : Synapse;
```

- [ ] **Step 4: Convert runtime to a packable library**

Use project references to Contracts, Kernel, and generator analyzer. Remove `OutputType`,
`ManagePackageVersionsCentrally=false`, all direct package-version declarations, Client, DevTools,
and Orleans.Server.

`QuickstartModule.cs`:

```csharp
using DigitalBrain.Abstractions;

namespace DigitalBrain.Quickstart;

public sealed partial class QuickstartModule : IModule;
```

`Greeter.cs`:

```csharp
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;

namespace DigitalBrain.Quickstart;

internal sealed class Greeter :
    Neuron,
    IGreeter,
    IHandle<SayHello>,
    IEmit<Greeted>
{
    public Task HandleAsync(SayHello request, CancellationToken cancellationToken)
        => EmitAsync(new Greeted($"Hello, {request.Name}."));
}
```

- [ ] **Step 5: Add both projects to the solution and run L0**

```powershell
dotnet build samples/DigitalBrain.Quickstart.Contracts/DigitalBrain.Quickstart.Contracts.csproj -c Release
dotnet build samples/DigitalBrain.Quickstart/DigitalBrain.Quickstart.csproj -c Release
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~ModuleTemplateContracts|FullyQualifiedName~CompiledModuleContracts" --logger "console;verbosity=minimal"
```

Expected: PASS.

- [ ] **Step 6: Commit the external package split**

```powershell
git add DigitalBrain.slnx samples tests/DigitalBrain.Tests
git commit -m "refactor(quickstart): split contracts from runtime"
```

---

### Task 2: Prove Quickstart through only the public L1 testing product

**Files:**
- Create: `tests/DigitalBrain.Quickstart.Tests/DigitalBrain.Quickstart.Tests.csproj`
- Create: `tests/DigitalBrain.Quickstart.Tests/AssemblyInfo.cs`
- Create: `tests/DigitalBrain.Quickstart.Tests/QuickstartFixture.cs`
- Create: `tests/DigitalBrain.Quickstart.Tests/GreetingBehavior.cs`
- Modify: `DigitalBrain.slnx`

**Interfaces:**
- Consumes: `DigitalBrainFixture`, `TestBrain`, `TestNeuron<IGreeter>`
- Produces: third-party-shaped fact/journal/restart proof

- [ ] **Step 1: Create a test project with no Orleans/Aspire references**

Use:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="xunit.v3" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\samples\DigitalBrain.Quickstart.Contracts\DigitalBrain.Quickstart.Contracts.csproj" />
    <ProjectReference Include="..\..\samples\DigitalBrain.Quickstart\DigitalBrain.Quickstart.csproj" />
    <ProjectReference Include="..\..\src\DigitalBrain.Testing\DigitalBrain.Testing.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Register the fixture**

```csharp
using DigitalBrain.Quickstart;
using DigitalBrain.Testing;

namespace DigitalBrain.Quickstart.Tests;

public sealed class QuickstartFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
        => brain.AddModule<QuickstartModule>();
}
```

```csharp
using DigitalBrain.Quickstart.Tests;
using Xunit;

[assembly: AssemblyFixture(typeof(QuickstartFixture))]
```

- [ ] **Step 3: Write the acceptance test**

```csharp
using Xunit;

namespace DigitalBrain.Quickstart.Tests;

public sealed class GreetingBehavior(QuickstartFixture fixture)
{
    [Fact]
    public async Task GreetingIsDurableAcrossItsHostingSiloRestart()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var test = await fixture.CreateBrainAsync(cancellationToken);
        var greeter = test.Neuron<IGreeter>("welcome");

        await test.Client.SendAsync<IGreeter>("welcome", new SayHello("Ada"));

        var first = await greeter.Outgoing.NextAsync<Greeted>(cancellationToken);
        Assert.Equal("Hello, Ada.", first.Synapse.Message);

        await greeter.RestartHostAsync(cancellationToken);

        var committed = await greeter.Outgoing.ReadAsync<Greeted>(
            afterSequence: 0,
            cancellationToken);
        Assert.Single(committed);
        Assert.Equal(first.SynapseId, committed[0].SynapseId);
    }
}
```

`ReadAsync<T>` is the committed evidence read defined by the L1 plan, not an assertion DSL.

- [ ] **Step 4: Add a package-source ban**

Assert the Quickstart test project's source and project XML contain none of:

```text
Orleans
Aspire
IGrainFactory
GrainId
NeuronId
Simulation
Scenario
ConcurrentDictionary
```

- [ ] **Step 5: Run twice**

```powershell
dotnet test tests/DigitalBrain.Quickstart.Tests/DigitalBrain.Quickstart.Tests.csproj -c Release --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.Quickstart.Tests/DigitalBrain.Quickstart.Tests.csproj -c Release --logger "console;verbosity=minimal"
```

Expected: both PASS.

- [ ] **Step 6: Commit Quickstart L1**

```powershell
git add DigitalBrain.slnx tests/DigitalBrain.Quickstart.Tests src/DigitalBrain.Testing
git commit -m "test(quickstart): prove external module author path"
```

---

### Task 3: Host Quickstart with one AddDigitalBrain call

**Files:**
- Create: `hosts/DigitalBrain.Quickstart.Host/DigitalBrain.Quickstart.Host.csproj`
- Create: `hosts/DigitalBrain.Quickstart.Host/Program.cs`
- Create: `hosts/DigitalBrain.Quickstart.AppHost/DigitalBrain.Quickstart.AppHost.csproj`
- Create: `hosts/DigitalBrain.Quickstart.AppHost/AppHost.cs`
- Delete: `samples/DigitalBrain.Quickstart/Program.cs`
- Delete: `samples/DigitalBrain.Quickstart/nuget.config`
- Modify: `DigitalBrain.slnx`
- Modify: `docs/quickstart.md`

**Interfaces:**
- Consumes: `AddDigitalBrain("quickstart")`, `AddModule<QuickstartModule>()`
- Produces: a real compiled host with no storage setup in AppHost

- [ ] **Step 1: Create the compiled silo host**

The host project references Quickstart runtime, Kernel, source generator analyzer, Orleans Server,
Azure clustering/reminder/journal packages, and `Aspire.Azure.Data.Tables`.

`Program.cs`:

```csharp
using DigitalBrain.Kernel;

var builder = WebApplication.CreateBuilder(args);

builder.AddKeyedAzureTableServiceClient("quickstart-clustering");
builder.AddKeyedAzureTableServiceClient("quickstart-reminders");
builder.UseOrleans(silo => silo
    .AddDigitalBrain()
    .AddDigitalBrainJournalStorage(builder.Configuration));

var app = builder.Build();
app.MapGet("/health", () => Results.Ok("healthy"));
app.Run();
```

The generated `AddDigitalBrain()` activates `QuickstartModule` only when AppHost selects it.

- [ ] **Step 2: Create the AppHost**

```csharp
using DigitalBrain.Aspire.Hosting;
using DigitalBrain.Quickstart;

var builder = DistributedApplication.CreateBuilder(args);

var brain = builder.AddDigitalBrain("quickstart")
    .AddModule<QuickstartModule>();

builder.AddProject<Projects.DigitalBrain_Quickstart_Host>("host")
    .WithReference(brain);

builder.Build().Run();
```

There is no `AddAzureStorage`, `RunAsEmulator`, storage profile, or module-specific hosting package.

- [ ] **Step 3: Build the AppHost release graph**

```powershell
dotnet build hosts/DigitalBrain.Quickstart.AppHost/DigitalBrain.Quickstart.AppHost.csproj -c Release
```

Expected: PASS and the host appears in the project reference graph.

- [ ] **Step 4: Replace the manual quickstart documentation**

Show:

1. Contracts project;
2. partial module marker;
3. `builder.AddDigitalBrain("quickstart").AddModule<QuickstartModule>()`;
4. `DigitalBrainFixture` test;
5. production `IDigitalBrain` usage.

Delete manual localhost clustering, memory reminders, direct grain factory, observer, raw ID, and
fixed 30-second wait from the page and repository.

- [ ] **Step 5: Verify the sample deletion budget**

```powershell
rg -n "UseLocalhostClustering|UseInMemoryReminderService|IGrainFactory|NeuronId|FirstMatchWatch|AwaitMatchAsync|Task.Delay" samples/DigitalBrain.Quickstart hosts/DigitalBrain.Quickstart.Host hosts/DigitalBrain.Quickstart.AppHost
```

Expected: no matches.

- [ ] **Step 6: Commit hosted Quickstart**

```powershell
git add DigitalBrain.slnx samples/DigitalBrain.Quickstart hosts/DigitalBrain.Quickstart.Host hosts/DigitalBrain.Quickstart.AppHost docs/quickstart.md
git commit -m "feat(quickstart): host the compiled module with Aspire"
```

---

### Task 4: Move the kernel outbox reminder behind composition

**Files:**
- Create: `src/DigitalBrain.Kernel/IOutboxDrain.cs`
- Create: `src/DigitalBrain.Kernel/IOutboxWakeup.cs`
- Create: `src/DigitalBrain.Kernel/OutboxWakeup.cs`
- Modify: `src/DigitalBrain.Kernel/Neuron.cs`
- Modify: `modules/DigitalBrain.Modules.Tasks/TaskNeuron.cs`
- Modify: `modules/DigitalBrain.Modules.AI/GroupChat.cs`
- Create: `tests/DigitalBrain.ModuleTests/KernelOutboxWakeupContracts.cs`

**Interfaces:**
- Produces: internal composed outbox reminder; `Neuron` no longer implements `IRemindable`

- [ ] **Step 1: Write the architecture and durability tests**

Assert:

```csharp
Assert.DoesNotContain(typeof(IRemindable), typeof(Neuron).GetInterfaces());
Assert.Null(typeof(Neuron).GetMethod("ReceiveReminder"));
```

With a journal-commit fault leaving an outbox entry pending, deactivate/restart its hosting silo and
assert the dedicated wakeup drains the delivery exactly once after recovery.

Assert `TaskNeuron` and `GroupChat` explicit reminder handlers contain no call to
`base.ReceiveReminder`.

- [ ] **Step 2: Run and verify red**

```powershell
dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --filter "FullyQualifiedName~KernelOutboxWakeupContracts" --logger "console;verbosity=minimal"
```

Expected: FAIL because `Neuron` implements `IRemindable`.

- [ ] **Step 3: Add hidden drain and wakeup contracts**

```csharp
[Alias("db.outbox-drain")]
internal interface IOutboxDrain : IGrainWithStringKey
{
    [Alias(nameof(Drain))]
    Task Drain();
}

[Alias("db.outbox-wakeup")]
internal interface IOutboxWakeup : IGrainWithStringKey
{
    [Alias(nameof(Arm))]
    Task Arm();

    [Alias(nameof(Disarm))]
    Task Disarm();
}
```

The wakeup grain key is `NeuronId.ToString()` (`type:owner/name`). `OutboxWakeup` parses the first
colon, reconstructs the target through `NeuronId.FromGrainKey`, and implements `IRemindable`.

- [ ] **Step 4: Implement the dedicated reminder grain**

`Arm` registers `db.outbox`; `Disarm` removes it when present. On `ReceiveReminder`, call:

```csharp
await GrainFactory
    .GetGrain<IOutboxDrain>(target.ToGrainId())
    .Drain();
```

The helper carries no semantic state and owns only the kernel reminder name plus the existing
one-minute retry cadence. `Arm` uses `RegisterOrUpdateReminder` idempotently; `Disarm` is a no-op
when no registration exists.

- [ ] **Step 5: Remove reminder inheritance from Neuron**

`Neuron` implements `IOutboxDrain` explicitly. Replace its direct reminder registration/removal with
calls to the helper grain keyed by `Id.ToString()`. Delete public `ReceiveReminder`.

Remove `base.ReceiveReminder` fallback branches from Tasks and AI; their explicit `IRemindable`
handlers reject unknown names and own only their private reminder names. Add `IRemindable`
explicitly to the `TaskNeuron` and `GroupChat` class declarations now that it is no longer inherited
from `Neuron`.

- [ ] **Step 6: Run kernel/module durability twice**

```powershell
dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --filter "FullyQualifiedName~KernelOutboxWakeupContracts|FullyQualifiedName~Task|FullyQualifiedName~AI" --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.ModuleTests/DigitalBrain.ModuleTests.csproj -c Release --filter "FullyQualifiedName~KernelOutboxWakeupContracts|FullyQualifiedName~Task|FullyQualifiedName~AI" --logger "console;verbosity=minimal"
```

Expected: both PASS.

- [ ] **Step 7: Commit composed kernel wakeup**

```powershell
git add src/DigitalBrain.Kernel modules/DigitalBrain.Modules.Tasks modules/DigitalBrain.Modules.AI tests/DigitalBrain.ModuleTests
git commit -m "refactor(kernel): compose outbox reminder ownership"
```

---

### Task 5: Add the settled ICountdown Contracts package

**Files:**
- Create: `modules/DigitalBrain.Modules.Time.Contracts/DigitalBrain.Modules.Time.Contracts.csproj`
- Create: `modules/DigitalBrain.Modules.Time.Contracts/ICountdown.cs`
- Create: `modules/DigitalBrain.Modules.Time.Contracts/CountdownCommands.cs`
- Create: `modules/DigitalBrain.Modules.Time.Contracts/CountdownSnapshot.cs`
- Create: `modules/DigitalBrain.Modules.Time.Contracts/CountdownElapsed.cs`
- Create: `tests/DigitalBrain.Tests/TimeContracts.cs`
- Modify: `DigitalBrain.slnx`
- Modify: `tests/DigitalBrain.Tests/ModuleTemplateContracts.cs`

**Interfaces:**
- Produces exact one-shot vocabulary:

```csharp
public partial interface ICountdown : INeuron
{
    Task<CountdownSnapshot> Start(StartCountdown command);
    Task<CountdownSnapshot> Reschedule(RescheduleCountdown command);
    Task<CountdownSnapshot> Cancel(CancelCountdown command);
    Task<CountdownSnapshot> Restart(RestartCountdown command);
    Task<CountdownSnapshot> Read();
}
```

- [ ] **Step 1: Write failing contract/leaf tests**

Assert:

- `ICountdown` exists and `IReminder` does not yet exist;
- all five methods use `nameof` aliases and have no `Async` suffix;
- every command carries `CommandId`;
- Start carries duration and explicit destination;
- reschedule/cancel carry expected revision;
- Restart carries a new duration and no new destination;
- CountdownElapsed carries no arbitrary payload;
- Contracts references only Abstractions plus private generator analyzer.

- [ ] **Step 2: Run and verify red**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~TimeContracts|FullyQualifiedName~ModuleTemplateContracts" --logger "console;verbosity=minimal"
```

Expected: build FAIL because Time does not exist.

- [ ] **Step 3: Create the leaf project**

Use the same leaf/analyzer shape as Quickstart Contracts.

`ICountdown.cs`:

```csharp
using DigitalBrain.Abstractions;

namespace DigitalBrain.Time;

public partial interface ICountdown : INeuron
{
    [Alias(nameof(Start))]
    Task<CountdownSnapshot> Start(StartCountdown command);

    [Alias(nameof(Reschedule))]
    Task<CountdownSnapshot> Reschedule(RescheduleCountdown command);

    [Alias(nameof(Cancel))]
    Task<CountdownSnapshot> Cancel(CancelCountdown command);

    [Alias(nameof(Restart))]
    Task<CountdownSnapshot> Restart(RestartCountdown command);

    [Alias(nameof(Read))]
    Task<CountdownSnapshot> Read();
}
```

- [ ] **Step 4: Add exact commands**

```csharp
[GenerateSerializer]
[Alias("time.start-countdown")]
public sealed record StartCountdown(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] TimeSpan Duration,
    [property: Id(2)] NeuronId Destination);

[GenerateSerializer]
[Alias("time.reschedule-countdown")]
public sealed record RescheduleCountdown(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] long ExpectedRevision,
    [property: Id(2)] TimeSpan Duration);

[GenerateSerializer]
[Alias("time.cancel-countdown")]
public sealed record CancelCountdown(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] long ExpectedRevision);

[GenerateSerializer]
[Alias("time.restart-countdown")]
public sealed record RestartCountdown(
    [property: Id(0)] CommandId CommandId,
    [property: Id(1)] TimeSpan Duration);
```

- [ ] **Step 5: Add snapshot and elapsed fact**

```csharp
public enum CountdownStatus
{
    Unscheduled,
    Scheduled,
    Elapsed,
    Cancelled,
}

public enum CountdownResolution
{
    OnTime,
    Recovered,
}

[GenerateSerializer]
[Alias("time.countdown-snapshot")]
public sealed record CountdownSnapshot(
    [property: Id(0)] CountdownStatus Status,
    [property: Id(1)] long Generation,
    [property: Id(2)] long Revision,
    [property: Id(3)] NeuronId? Destination,
    [property: Id(4)] DateTimeOffset? ScheduledAt,
    [property: Id(5)] DateTimeOffset? DueAt,
    [property: Id(6)] TimeSpan? Duration);

[GenerateSerializer]
[Alias("time.countdown-elapsed")]
public sealed record CountdownElapsed(
    [property: Id(0)] NeuronId Countdown,
    [property: Id(1)] long Generation,
    [property: Id(2)] long Revision,
    [property: Id(3)] NeuronId Destination,
    [property: Id(4)] DateTimeOffset ScheduledAt,
    [property: Id(5)] DateTimeOffset DueAt,
    [property: Id(6)] DateTimeOffset ObservedAt,
    [property: Id(7)] CountdownResolution Resolution) : Synapse;
```

- [ ] **Step 6: Run contracts and commit**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~TimeContracts|FullyQualifiedName~ModuleTemplateContracts|FullyQualifiedName~NeuronContractNamingContracts" --logger "console;verbosity=minimal"
git add DigitalBrain.slnx modules/DigitalBrain.Modules.Time.Contracts tests/DigitalBrain.Tests
git commit -m "feat(time): add countdown vocabulary"
```

Expected: tests PASS, then commit succeeds.

---

### Task 6: Implement and prove the durable Countdown neuron

**Files:**
- Create: `modules/DigitalBrain.Modules.Time/DigitalBrain.Modules.Time.csproj`
- Create: `modules/DigitalBrain.Modules.Time/TimeModule.cs`
- Create: `modules/DigitalBrain.Modules.Time/CountdownNeuron.cs`
- Create: `modules/DigitalBrain.Modules.Time/CountdownState.cs`
- Create: `modules/DigitalBrain.Modules.Time/ICountdownWakeup.cs`
- Create: `tests/DigitalBrain.Time.Tests/DigitalBrain.Time.Tests.csproj`
- Create: `tests/DigitalBrain.Time.Tests/AssemblyInfo.cs`
- Create: `tests/DigitalBrain.Time.Tests/TimeFixture.cs`
- Create: `tests/DigitalBrain.Time.Tests/CountdownLifecycle.cs`
- Create: `tests/DigitalBrain.Time.Tests/CountdownRecovery.cs`
- Modify: `DigitalBrain.slnx`
- Modify: `tests/DigitalBrain.Tests/PackableProjects.cs`

**Interfaces:**
- Consumes: `ICountdown`, shared `TimeProvider`, Orleans reminder provider
- Produces: one durable schedule per neuron with timer/reminder race dedupe

- [ ] **Step 1: Create the runtime package and module marker**

Reference Time Contracts, Kernel, and generator analyzer. Do not reference Testing or any Aspire
package.

```csharp
namespace DigitalBrain.Time;

public sealed partial class TimeModule : IModule;
```

Create the external Time test project with references to Time Contracts, Time runtime, and
`DigitalBrain.Testing`. It does not reference Orleans, Kernel, or Aspire. Register:

```csharp
public sealed class TimeFixture : DigitalBrainFixture
{
    protected override void Configure(DigitalBrainTestBuilder brain)
        => brain.AddModule<TimeModule>();
}
```

- [ ] **Step 2: Write failing lifecycle, race, and recovery tests**

In `CountdownLifecycle.cs`, cover:

```text
Start only from Unscheduled
same CommandId returns same snapshot
different Start after scheduled fails
Reschedule revision fence
Cancel revision fence and terminal state
Restart retains destination and increments generation
same-owner destination required
zero/negative duration rejected
Read returns durable state
no occurrence at 59 minutes
one destination occurrence at 60 minutes
local timer/reminder race emits once
restart before due emits once with Recovered resolution
journal failure during occurrence recovers once after restart
second restart does not duplicate a committed occurrence
```

Use a second, unscheduled `ICountdown` neuron as the destination. Use only typed references,
`TestBrain.Clock`, committed journals, and xUnit assertions.

The basic occurrence proof is:

```csharp
var countdown = test.Neuron<ICountdown>("primary");
var destination = test.Neuron<ICountdown>("destination");

await countdown.Reference.Start(
    new StartCountdown(CommandId.New(), TimeSpan.FromHours(1), destination.Id));

await test.Clock.AdvanceAsync(TimeSpan.FromMinutes(59), cancellationToken);
Assert.Empty(await destination.Incoming.ReadAsync<CountdownElapsed>(0, cancellationToken));

await test.Clock.AdvanceAsync(TimeSpan.FromMinutes(1), cancellationToken);
var elapsed = await destination.Incoming.NextAsync<CountdownElapsed>(cancellationToken);

Assert.Equal(countdown.Id, elapsed.Synapse.Countdown);
Assert.Equal(CountdownResolution.OnTime, elapsed.Synapse.Resolution);
```

For fault recovery, arm `countdown.FailNextJournalCommit` only after Start has committed, advance to
the due instant and observe the injected failure, restart through
`countdown.RestartHostAsync(cancellationToken)`, then call
`test.Clock.AdvanceAsync(TimeSpan.Zero, cancellationToken)` to drive the still-due reminder.

- [ ] **Step 3: Run and verify red**

```powershell
dotnet test tests/DigitalBrain.Time.Tests/DigitalBrain.Time.Tests.csproj -c Release --logger "console;verbosity=minimal"
```

Expected: FAIL because no Countdown neuron implements the contract.

- [ ] **Step 4: Add hidden wakeup interface**

```csharp
[Alias("time.countdown-wakeup")]
internal interface ICountdownWakeup : IGrainWithStringKey
{
    [Alias(nameof(Wake))]
    Task Wake(long generation, long revision);
}
```

`CountdownNeuron` implements it explicitly. Both the activation-local timer and
`IRemindable.ReceiveReminder` call the self proxy through this interface; they do not run state
transitions outside an Orleans turn.

- [ ] **Step 5: Add persisted state**

`CountdownState` has an explicit stable alias and fields:

```text
Status
Generation
Revision
Destination
ScheduledAt
DueAt
Duration
Dictionary<CommandId, CountdownSnapshot> Receipts (bounded to latest 64)
bool OccurrenceCommitted
string? ActiveReminderName
```

Store serialized bytes in `IDurableValue<byte[]>` keyed `time.countdown`. Command mutations assign
the serialized state and call the inherited `WriteStateAsync`. The occurrence transition stages
terminal state, then calls `SendAsync(destination, elapsed)` so the kernel persists Time state,
outgoing fact, and durable outbox in one journal write.

- [ ] **Step 6: Implement command state transitions**

Rules:

```text
Start:
  valid only Unscheduled
  duration > 0
  destination.Owner == Id.Owner
  generation = 1, revision = 1

Reschedule:
  valid only Scheduled
  ExpectedRevision must equal current Revision
  same destination, generation unchanged, revision + 1

Cancel:
  valid only Scheduled
  ExpectedRevision must equal current Revision
  persist Cancelled before removing wakeups

Restart:
  valid only Elapsed or Cancelled
  destination retained
  generation + 1, revision = 1

Repeated CommandId:
  return the previously committed snapshot without another side effect
```

For Start/Reschedule/Restart: register the revision-fenced durable reminder first, persist the new
state/receipt second, retire the previous reminder third, then arm the local timer.

- [ ] **Step 7: Implement the on-time timer/reminder path**

Reminder name:

```csharp
private static string ReminderName(long generation, long revision)
    => $"time.countdown.{generation}.{revision}";
```

Use `TimeProvider.CreateTimer` for the prompt activation-local wakeup. Its callback invokes the
`ICountdownWakeup` self proxy. Store the armed generation/revision only in an activation-local
field. Register the Orleans reminder as durable backstop; it survives activation and silo loss.

`Wake`:

1. load state;
2. ignore non-Scheduled or mismatched generation/revision;
3. if `TimeProvider.GetUtcNow() < DueAt`, re-arm and return;
4. set `OccurrenceCommitted=true` and `Status=Elapsed`;
5. create one `CountdownElapsed` with `OnTime` when this activation still owns the matching local
   timer registration, otherwise `Recovered`;
6. stage terminal state and call `SendAsync` to the stored destination;
7. let the kernel journal commit persist terminal state, outgoing delivery, and outbox together;
8. retire timer/reminder.

A second racing callback reads terminal/committed state and returns without emitting. After restart,
the activation-local marker is absent, so the durable reminder path reports `Recovered` without an
activation hook or inherited reminder behavior.

- [ ] **Step 8: Run lifecycle, runtime, and package tests**

```powershell
dotnet build modules/DigitalBrain.Modules.Time/DigitalBrain.Modules.Time.csproj -c Release
dotnet test tests/DigitalBrain.Time.Tests/DigitalBrain.Time.Tests.csproj -c Release --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.Time.Tests/DigitalBrain.Time.Tests.csproj -c Release --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.Time.Tests/DigitalBrain.Time.Tests.csproj -c Release --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~TimeContracts|FullyQualifiedName~PackableProjects|FullyQualifiedName~CompiledModuleContracts" --logger "console;verbosity=minimal"
```

Expected: all three Time runs and the L0 package gate PASS without `Task.Delay`.

- [ ] **Step 9: Prove no open Time scope leaked**

```powershell
rg -n "\bIReminder\b|Calendar|Cron|Recurrence|TimeZone|Noda|Ical" modules/DigitalBrain.Modules.Time.Contracts modules/DigitalBrain.Modules.Time tests/DigitalBrain.Time.Tests
```

Expected: no matches except a negative architecture assertion explaining that those types are
absent.

- [ ] **Step 10: Commit Time runtime and deterministic proof**

```powershell
git add DigitalBrain.slnx modules/DigitalBrain.Modules.Time tests/DigitalBrain.Tests tests/DigitalBrain.Time.Tests
git commit -m "feat(time): add durable countdown neuron"
```

---

### Task 7: Audit every shipped module through the same authoring seam

**Files:**
- Modify: `tests/DigitalBrain.Tests/ModuleTemplateContracts.cs`
- Modify: `tests/DigitalBrain.Tests/PackageBoundaryContracts.cs`
- Modify: `tests/DigitalBrain.Tests/PackableProjects.cs`
- Modify: `docs/architecture.md`
- Modify: `docs/packages.md`
- Modify: `docs/quickstart.md`
- Modify: `docs/concepts.md`
- Modify: `CLAUDE.md`

**Interfaces:**
- Consumes: Quickstart, AI, Tasks, Time, Google, Salesforce
- Produces: one enforceable external module pattern

- [ ] **Step 1: Add a module-family matrix guard**

For each shipped module assert:

```text
Contracts leaf exists
runtime marker exists and has generated capsule
runtime references its Contracts
Contracts do not reference runtime/Kernel/Aspire/Testing
optional Aspire.Hosting exists only when the module owns an external resource
module L1 tests reference Testing but not Orleans/Aspire
module selected exactly once in its AppHost/fixture
```

Expected families:

```text
Quickstart: Contracts + runtime; no module Aspire.Hosting
AI: Contracts + runtime + Aspire.Hosting
Tasks: Contracts + runtime; no module Aspire.Hosting
Time: Contracts + runtime; no module Aspire.Hosting
Google: Contracts + runtime + Aspire.Hosting
Salesforce: Contracts + runtime + Aspire.Hosting
```

- [ ] **Step 2: Run and resolve every matrix failure**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release --filter "FullyQualifiedName~ModuleTemplateContracts|FullyQualifiedName~PackageBoundaryContracts|FullyQualifiedName~PackableProjects" --logger "console;verbosity=minimal"
```

Expected initially: failures identify any remaining inconsistent family. Fix package references and
test placement only; do not add empty packages to make the matrix rectangular.

- [ ] **Step 3: Update architecture status honestly**

Mark Quickstart author path and `ICountdown` built. Keep `IReminder`, interval/calendar scheduling,
and Behavior execution designed/unbuilt. Replace `WithAzureStorage` and Simulation/Scenario examples
with the approved APIs.

- [ ] **Step 4: Keep the live program status accurate**

Update `CLAUDE.md` without changing its program-index authority. Describe Quickstart and
`ICountdown` as built only after their gates pass. Keep Behavior execution, `IReminder`, recurring,
and calendar capabilities named as unbuilt. Keep findings/evidence records and the approved design
spec.

- [ ] **Step 5: Run the full clean release gate**

```powershell
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal"
npm --prefix docs test
npm --prefix docs run build
```

Expected: PASS.

- [ ] **Step 6: Run final deletion searches**

```powershell
rg -n "Simulation|Simulations|Scenario|SimulationCluster|HostedScenario|HostedApplication|AddBrain|WithAzureStorage|WithDevelopmentStores|UseLocalhostClustering|IGrainFactory|NeuronCatalog" src modules samples hosts tests docs --glob "!docs/superpowers/**"
```

Expected: no live implementation/documentation matches. Historical approved specs may describe
deleted names.

- [ ] **Step 7: Commit the architecture audit**

```powershell
git add CLAUDE.md docs tests/DigitalBrain.Tests modules samples hosts
git restore --staged Directory.Packages.props
git commit -m "docs: publish the external module authoring path"
```

---

## Plan 4 completion gate

Run:

```powershell
dotnet build DigitalBrain.slnx -c Release
dotnet test DigitalBrain.slnx -c Release --logger "console;verbosity=minimal"
npm --prefix docs test
npm --prefix docs run build
git status --short
```

Expected:

- all commands pass;
- only the user's unrelated `Directory.Packages.props` line-ending change remains unstaged;
- Quickstart is a real split module with AppHost and public Testing proof;
- `ICountdown` is durable, deterministic under TestBrain, and the only implemented Time capability;
- kernel outbox reminders are composed rather than inherited;
- all shipped modules satisfy the same package/hosting/testing matrix;
- no old Simulation/Scenario, storage-profile, or manual-hosting path remains.
