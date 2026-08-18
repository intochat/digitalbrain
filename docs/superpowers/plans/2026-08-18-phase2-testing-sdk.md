# Phase 2: Testing SDK Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship the first two packable testing-SDK packages (`DigitalBrain.Testing` for Tiers 1+2, `DigitalBrain.Testing.E2E` for Tier 3) plus three test projects that prove them: a Tier 1 conformance suite (model-only, no containers), a Tier 2 Simulation smoke that pins the phase-1 owner-wall and entity semantics, and a Tier 3 e2e smoke that boots the production AppHost and drives the `IDigitalBrain` facade across processes. Also closes the carried-over probe cleanup.

**Architecture:** Tier 2 runs the **production silo composition** (`DigitalBrainRuntime.Add`) inside an `InProcessTestClusterBuilder` cluster, swapping only persistence: a `VolatileJournalStorage` (in-memory `IJournalStorageProvider`) replaces Azure blob durable state, memory streams/pubsub/reminders replace Azure queues/tables. Tier 1 wraps `DistributedApplicationTestingBuilder` + `ExecutionConfigurationBuilder` with automatic parameter stubbing. Tier 3's fixture boots `Projects.DigitalBrain_AppHost` with test args, reshapes the model (session containers, no volumes, heavy resources explicit-start, test-mode stamp), and connects the facade through real clustering via the `DigitalBrainScriptHost` path.

**Tech Stack:** xunit.v3 4.0.0-pre.154 under Microsoft.Testing.Platform (`global.json` already mandates the runner), `Aspire.Hosting.Testing` 13.5.0-preview, `Microsoft.Orleans.TestingHost` 10.2.2, Orleans.Journaling 10.2.2-rc.2.alpha.1 — all already pinned in `Directory.Packages.props`; **no new package pins**.

**Spec:** `docs/superpowers/specs/2026-08-18-digitalbrain-aspire-testing-sdk-design.md` (§6 SDK design, §11 phase 2 row).

**Scope deltas vs the spec (ruled by the controller, amended into the spec in Task 8):**
1. `DigitalBrain.Testing.Bdd` defers to phase 3 — an empty Reqnroll shell has no substance until the chat edge + corpus exist; the compat shim ships with its first real consumer.
2. `BrainTestHost.Compose` (ad-hoc minimal AppHost) defers to phase 4/5 with the community template — in-repo suites use the production AppHost; no bet on an unverified non-generic compose API now.
3. `Microsoft.Playwright` and the fake-server toolkit defer to phases 3/4 with their first consumers (headless UI, OAuth fakes).
4. `sim.Time` (deterministic TimeProvider), `sim.Capture`, and `MockEmbeddingGenerator` defer until code consumes them (phase 3) — journals already serve as capture.
5. `BrainSimulation` takes explicit `ModuleAssemblies` instead of the spec's `BrainSimulationFixture<TModule>` sketch — module marker types are hosting-side and cannot derive contract+implementation assemblies.
6. The layout gains `tests/DigitalBrain.Simulation.Tests` (Tier 2 must not pollute the model-only Tier 1 assembly, and per-assembly fixture sharing is the perf rule).

**Reference sources (read, then mirror — these are the user's own proven prototypes):**
- `E:\intochat\Projects\ino\src\Ino.Testing\VolatileJournalStorageProvider.cs` — in-memory `IJournalStorageProvider` at the SAME Orleans.Journaling version. Also `TestSiloConfigurator.cs` (registration pattern: `silo.Services.AddSingleton<IJournalStorageProvider, VolatileJournalStorageProvider>(); silo.AddJournalStorage();`).
- `git show master:srcv2/CoreV2/Brain.Testing/BrainTestHost.cs` (run in E:\intochat\digitalbrain) — this repo's own earlier `InProcessTestClusterBuilder(1)` harness.
- `E:\intochat\Projects\ino\src\Ino.Testing\InoTestAppHost.cs` — parameter stubbing (`builder.Configuration[$"Parameters:{p.Name}"] = "test"` for every `ParameterResource`) and health-wait patterns.

## Global Constraints

- Working directory `E:\intochat\digitalbrain`, branch `finalv2` (HEAD `2e06a4c4`, builds green). NEVER read or write any path under `C:\Users\`. Reading the two reference paths under `E:\intochat\Projects\ino` is explicitly allowed (read-only).
- Central package management: bare `<PackageReference>` only, all versions already pinned; no new pins, no bumps.
- SDK projects (`src/Testing/*`): `<IsPackable>true</IsPackable>`; test projects (`tests/*`): the MTP shape below. Every new project is added to `DigitalBrain.slnx` in the task that creates it (SDK projects under a new `<Folder Name="/Testing/">`, test projects under `<Folder Name="/Tests/">`).
- Test project csproj shape (from the repo's own conventions + MTP mandate in `global.json`):

```xml
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <UseMicrosoftTestingPlatformRunner>true</UseMicrosoftTestingPlatformRunner>
    <TestingPlatformDotnetTestSupport>true</TestingPlatformDotnetTestSupport>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="xunit.runner.visualstudio" />
  </ItemGroup>
```

- Build gate per task: `dotnet build DigitalBrain.slnx -warnaserror` (timeout 600000) → exit 0. Test gates where a task says so: `dotnet test <test-project-path> -c Debug` (MTP; timeout 600000) → exit 0, all tests passing, output pristine.
- Domain-journal vocabulary and `DigitalBrainNames` member values remain untouchable (phase-1 rules).
- Commits with two `-m` flags, trailer `Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>`. Never add meaningless `/// <summary>`. Fix analyzers in code, never suppress.
- Test assertions use xunit's built-in `Assert` (no FluentAssertions/Shouldly — neither is pinned).

---

### Task 1: Probe cleanup (carried ruling from phase 1)

**Files:**
- Delete: `src/Kernel/DigitalBrain.Scripting/wave2-registry-probe.cs` (17 pre-existing stale-API compile errors; superseded by this phase's real tests)
- Modify: `src/Kernel/DigitalBrain.Scripting/Program.cs` (remove step 3 — the `// 3) Wave 2 registry probe.` comment, the `probe` variable, and the two `Console.WriteLine` lines that run it; steps 1–2 and `RunScriptAsync`/`LocateRepoRoot` stay byte-identical)

**Interfaces:** none produced; consumes the phase-1 explicit-start + env-gate state.

- [ ] **Step 1:** `git rm src/Kernel/DigitalBrain.Scripting/wave2-registry-probe.cs`; edit `Program.cs` as above.
- [ ] **Step 2:** Verify: `grep -rn "wave2" src/` → no output.
- [ ] **Step 3:** Build gate → exit 0.
- [ ] **Step 4:** Commit: `git commit -am "Remove the stale wave2 registry probe" -m "Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"`

---

### Task 2: `DigitalBrain.Testing` package (Tiers 1+2 SDK)

**Files:**
- Create: `src/Testing/DigitalBrain.Testing/DigitalBrain.Testing.csproj`
- Create: `src/Testing/DigitalBrain.Testing/VolatileJournalStorage.cs`
- Create: `src/Testing/DigitalBrain.Testing/BrainSimulation.cs`
- Create: `src/Testing/DigitalBrain.Testing/JournalWait.cs`
- Create: `src/Testing/DigitalBrain.Testing/BrainModel.cs`
- Create: `src/Testing/DigitalBrain.Testing/BrainTestMode.cs`
- Modify: `src/Kernel/DigitalBrain.Abstractions/DigitalBrainNames.cs` (add two consts)
- Modify: `DigitalBrain.slnx` (add `/Testing/` folder + project)

**Interfaces:**
- Consumes: `DigitalBrainRuntime.Add(ISiloBuilder, ModuleAssemblies)` (`Core/Hosting/DigitalBrainRuntime.cs`), `DigitalBrainClient.Connect(IGrainFactory, string)`, `IDigitalBrain`, `DigitalBrainNames.{StreamProvider, PubSubStore, DefaultOwner}`, Orleans.Journaling's `IJournalStorageProvider`/`IJournalStorage` (mirror ino's file for the exact interface shape at this version).
- Produces (later tasks + phases build on these exact names):
  - `public static class BrainTestMode { public static IResourceBuilder<T> WithBrainTestMode<T>(this IResourceBuilder<T>) where T : IResourceWithEnvironment; }`
  - `DigitalBrainNames.Mode = "DigitalBrain:Mode"` and `DigitalBrainNames.TestingMode = "Testing"`
  - `public sealed class BrainSimulationOptions { required ModuleAssemblies Modules; string Owner = DigitalBrainNames.DefaultOwner; int SiloCount = 1; Action<ISiloBuilder>? ConfigureSilo; }`
  - `public sealed class BrainSimulation : IAsyncDisposable { static Task<BrainSimulation> StartAsync(BrainSimulationOptions); IGrainFactory Grains; IDigitalBrain Brain; IDigitalBrain BrainFor(string owner); string UniqueId(string prefix); }`
  - `public static class JournalWait { static Task<SynapseDelivery> ForAsync(IDigitalBrain brain, NeuronId subject, JournalKind kind, Func<SynapseDelivery, bool> match, TimeSpan? timeout = null); }` — poll-based, timeout default 30 s, timeout failure message lists every observed delivery type ("Saw: …").
  - `public sealed class BrainModel { static Task<BrainModel> BuildAsync<TAppHost>(params string[] args) where TAppHost : class; IReadOnlyList<IResource> Resources; IResource Resource(string name); Task<IReadOnlyDictionary<string,string>> RenderedEnvironmentAsync(string resourceName); ValueTask DisposeAsync-compatible ownership of the built app; }`

- [ ] **Step 1: csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>true</IsPackable>
    <Description>DigitalBrain testing SDK: model-level assertions and in-process brain simulation.</Description>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.Testing" />
    <PackageReference Include="Microsoft.Orleans.TestingHost" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../Kernel/DigitalBrain.Core/DigitalBrain.Core.csproj" />
    <ProjectReference Include="../../Kernel/DigitalBrain.Client/DigitalBrain.Client.csproj" />
  </ItemGroup>
</Project>
```

(Orleans.Journaling flows transitively from Core. If the compiler wants an explicit `Microsoft.Orleans.Journaling` reference for `IJournalStorageProvider`, add the bare reference — it is pinned.)

- [ ] **Step 2: VolatileJournalStorage**

Read `E:\intochat\Projects\ino\src\Ino.Testing\VolatileJournalStorageProvider.cs` and port it (namespace `DigitalBrain.Testing`, repo code style). It implements the Journaling package's storage-provider abstraction with per-grain in-memory journal segments. Keep the port behaviorally identical; rename to `VolatileJournalStorage`/`VolatileJournalStorageProvider` matching whatever split ino uses.

- [ ] **Step 3: BrainSimulation**

```csharp
public static async Task<BrainSimulation> StartAsync(BrainSimulationOptions options)
{
    var builder = new InProcessTestClusterBuilder(options.SiloCount);
    builder.ConfigureSilo((_, silo) =>
    {
        silo.Services.AddSingleton<IJournalStorageProvider, VolatileJournalStorageProvider>();
        DigitalBrainRuntime.Add(silo, options.Modules);
        silo.AddMemoryGrainStorage(DigitalBrainNames.PubSubStore);
        silo.AddMemoryStreams(DigitalBrainNames.StreamProvider);
        silo.UseInMemoryReminderService();
        options.ConfigureSilo?.Invoke(silo);
    });
    var cluster = builder.Build();
    await cluster.DeployAsync().ConfigureAwait(false);
    return new BrainSimulation(cluster, options.Owner);
}
```

`Grains` = `cluster.Client`; `Brain` = `DigitalBrainClient.Connect(cluster.Client, owner)` (it is `[EditorBrowsable(Never)]` but public — call it directly); `UniqueId(prefix)` = `$"{prefix}-{Guid.NewGuid():N}"` truncated to 8 hex chars; `DisposeAsync` disposes the cluster. Before writing, read `git show master:srcv2/CoreV2/Brain.Testing/BrainTestHost.cs` and mirror any InProcess API details that differ from this sketch (e.g. exact deploy/dispose member names) — the git-history harness compiled against the same TestingHost version.

- [ ] **Step 4: JournalWait**

Poll `brain.ReadJournalAsync(subject, kind, afterSequence)` every 200 ms, advancing `afterSequence` by `ResumeSequence`, collecting every delivery seen; return the first match; on timeout throw `TimeoutException` with message `$"No matching {kind} delivery on {subject} within {timeout}. Saw: [{string.Join(", ", seenTypes)}]"` (seenTypes = distinct `Synapse` runtime type names, `"(none)"` when empty). `SynapseDelivery`'s shape: read `src/Kernel/DigitalBrain.Abstractions/Journals/JournalRead.cs` and the `SynapseDelivery` type it references first; match accessor names exactly.

- [ ] **Step 5: BrainModel (Tier 1)**

`BuildAsync<TAppHost>`: `DistributedApplicationTestingBuilder.CreateAsync<TAppHost>(args, (appOptions, hostSettings) => { ... })`; after create, stub every parameter: `foreach (var p in builder.Resources.OfType<ParameterResource>()) builder.Configuration[$"Parameters:{p.Name}"] = "test";` then `await builder.BuildAsync()` and keep the built `DistributedApplication` WITHOUT starting it. `Resources` = the app model's resources (`app.Services.GetRequiredService<DistributedApplicationModel>().Resources` — or the builder's `Resources` captured pre-build; use whichever compiles cleanly, note the choice). `Resource(name)` throws with the available names listed when missing. `RenderedEnvironmentAsync(name)`:

```csharp
var resource = Resource(resourceName);
var configuration = await ExecutionConfigurationBuilder.Create(resource)
    .WithEnvironmentVariablesConfig()
    .BuildAsync(new(DistributedApplicationOperation.Publish), NullLogger.Instance, cancellationToken)
    .ConfigureAwait(false);
return configuration.EnvironmentVariables.ToDictionary();
```

(Exact `BuildAsync` overload per the current Aspire 13.5 surface — if the `DistributedApplicationExecutionContext` ctor needs a `ServiceProvider`, mirror the pattern from aspire.dev's EnvVarTests, which this sketch follows.)

- [ ] **Step 6: BrainTestMode + names**

Add to `DigitalBrainNames`: `public const string Mode = "DigitalBrain:Mode";` and `public const string TestingMode = "Testing";` (after the `Modules` const). `WithBrainTestMode<T>` sets `WithEnvironment("DigitalBrain__Mode", DigitalBrainNames.TestingMode)`.

- [ ] **Step 7:** slnx entry; build gate → exit 0.
- [ ] **Step 8:** Commit: `"Add DigitalBrain.Testing SDK package (model assertions + brain simulation)"`.

---

### Task 3: `tests/DigitalBrain.Simulation.Tests` (Tier 2 smoke + conformance pins)

**Files:**
- Create: `tests/DigitalBrain.Simulation.Tests/DigitalBrain.Simulation.Tests.csproj` (MTP shape + ProjectReferences: `DigitalBrain.Testing`, `../../src/Modules/Time/Time/`, `../../src/Modules/Time/Contracts/`)
- Create: `tests/DigitalBrain.Simulation.Tests/TestEntities.cs` (test-only entity contract + implementation)
- Create: `tests/DigitalBrain.Simulation.Tests/SimulationCollection.cs` (fixture + collection definition)
- Create: `tests/DigitalBrain.Simulation.Tests/EntityTests.cs`
- Create: `tests/DigitalBrain.Simulation.Tests/JournalSmokeTests.cs`
- Modify: `DigitalBrain.slnx` (`/Tests/` folder + project)

**Interfaces:**
- Consumes: Task 2's `BrainSimulation`/`JournalWait`; phase-1's `Entity<TState>`, `IEntity<TState>`, `GetEntity`, owner wall (`NeuronAuthorizationException` — locate its namespace in `DigitalBrain.Abstractions` first), `ModuleAssemblies(Contracts, Implementations)`.
- Produces: the executable proof the SDK works, and the conformance pins phase 1's final review demanded (owner wall + entity call semantics).

- [ ] **Step 1: Test entity**

```csharp
[ClientEntryPoint]
[Alias("test.counter")]
public interface ICounterEntity : IEntity<CounterState>
{
    [Alias(nameof(Add))]
    Task Add(int amount);
}

[GenerateSerializer]
[Alias("test.counter-state")]
public sealed record CounterState([property: Id(0)] int Total);

[GrainType("counterentity")]
internal sealed class CounterEntity : Entity<CounterState>, ICounterEntity
{
    public async Task Add(int amount)
        => await SaveAsync(new CounterState((State?.Total ?? 0) + amount));
}
```

(`[GrainType]` value must equal `GrainTypeNames.Of(typeof(ICounterEntity))` = `"counterentity"` — the phase-3 convention, pinned here first. Match `Entity<TState>`'s actual `SaveAsync` signature from `src/Kernel/DigitalBrain.Core/Entities/Entity.cs`.)

- [ ] **Step 2: Fixture + collection**

```csharp
public sealed class SimulationFixture : IAsyncLifetime
{
    public BrainSimulation Sim { get; private set; } = null!;

    public async ValueTask InitializeAsync()
        => Sim = await BrainSimulation.StartAsync(new()
        {
            Modules = new ModuleAssemblies(
                [typeof(DigitalBrain.Modules.Time.Contracts.AssemblyMarker).Assembly],
                [typeof(DigitalBrain.Modules.Time.AssemblyMarker).Assembly, typeof(SimulationFixture).Assembly]),
        });

    public async ValueTask DisposeAsync() => await Sim.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class SimulationCollection : ICollectionFixture<SimulationFixture>
{
    public const string Name = "simulation";
}
```

The Time module's marker types: read `src/Kernel/DigitalBrain.Kernel/ProductModules.cs` first and use the SAME `typeof(X).Assembly` expressions it uses for the Time contract + implementation assemblies (there is no `AssemblyMarker` class — substitute the real types `ProductModules` names). The test assembly itself goes into `Implementations` so `CounterEntity` is discovered. `IAsyncLifetime` member signatures: xunit.v3 uses `ValueTask` — verify against the installed package if the compiler disagrees and adapt.

- [ ] **Step 3: EntityTests** (all `[Collection(SimulationCollection.Name)]`)

```csharp
[Fact]
public async Task EntityRoundTripsState()
{
    var name = fixture.Sim.UniqueId("counter");
    var counter = fixture.Sim.Brain.GetEntity<ICounterEntity>(name);
    await counter.Add(2);
    await counter.Add(3);
    var state = await counter.Read();
    Assert.Equal(5, state!.Total);
}

[Fact]
public async Task CrossOwnerEntityCallIsWalled()
{
    var name = fixture.Sim.UniqueId("walled");
    await fixture.Sim.Brain.GetEntity<ICounterEntity>(name).Add(1);
    var stranger = fixture.Sim.BrainFor(fixture.Sim.UniqueId("stranger"));
    await Assert.ThrowsAsync<NeuronAuthorizationException>(
        () => stranger.GetEntity<ICounterEntity>(name).Read());
}
```

Note the wall's actual semantics: `BrainFor(other)` produces grain ids under the OTHER owner — so to prove the wall, the second test must target the FIRST owner's grain id. If `GetEntity` cannot address a foreign owner by design (it always scopes to its own `Owner`), then the wall test instead goes through `Grains` directly: `fixture.Sim.Grains.GetGrain<ICounterEntity>(EntityId.For<ICounterEntity>(firstOwner, name).ToGrainId())` from an unattributed client context and asserts the authorization exception. Read `OwnerBoundCallFilter.cs` to determine which shape actually exercises the wall, implement that one, and document the choice in a one-line comment. Also pin the facade guard:

```csharp
[Fact]
public void BareMarkerEntityContractIsRefused()
    => Assert.Throws<NeuronAuthorizationException>(() => fixture.Sim.Brain.GetEntity<IEntity>());
```

(Match the exception type `RequireDomainEntityContract` actually throws — read `DigitalBrainClient.cs`.)

- [ ] **Step 4: JournalSmokeTests**

Activate + observe the session journal through the facade:

```csharp
[Fact]
public async Task ActivationLandsInTheSessionJournal()
{
    var brain = fixture.Sim.BrainFor(fixture.Sim.UniqueId("journal-owner"));
    await brain.ActivateAsync();
    var read = await brain.ReadJournalAsync(SessionSubject(brain.Owner), JournalKind.Outgoing);
    Assert.True(read.ResumeSequence >= 0);
}
```

Before writing: read `src/Kernel/DigitalBrain.Kernel/OwnerSessionJournal.cs` to see how the kernel names the session-neuron subject (`NeuronId` construction) and reuse that exact expression for `SessionSubject`. If activation produces no Outgoing delivery on the session journal (read `DigitalBrainNeuron.Activate` to check where `DigitalBrainActivated` lands), assert on the neuron that DOES receive it (the surface-boot instance) via `JournalWait.ForAsync(brain, <that subject>, JournalKind.Incoming, d => d.Synapse is DigitalBrainActivated)` — implement whichever assertion is true to the code, with a comment naming the flow.

- [ ] **Step 5:** slnx entry; build gate; then `dotnet test tests/DigitalBrain.Simulation.Tests -c Debug` → all green, output pristine.
- [ ] **Step 6:** Commit: `"Add Tier 2 simulation smoke tests pinning entity and owner-wall semantics"`.

---

### Task 4: `tests/DigitalBrain.Aspire.Tests` (Tier 1 conformance suite)

**Files:**
- Create: `tests/DigitalBrain.Aspire.Tests/DigitalBrain.Aspire.Tests.csproj` (MTP shape + `DigitalBrain.Testing` ref + `<ProjectReference Include="../../src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj" IsAspireProjectResource="false" />`)
- Create: `tests/DigitalBrain.Aspire.Tests/ModelCollection.cs` (one shared `BrainModel` per assembly — class fixture holding `BrainModel.BuildAsync<Projects.DigitalBrain_AppHost>()`)
- Create: `tests/DigitalBrain.Aspire.Tests/TopologyConformanceTests.cs`
- Create: `tests/DigitalBrain.Aspire.Tests/NamesConformanceTests.cs`
- Modify: `DigitalBrain.slnx`

**Interfaces:**
- Consumes: Task 2's `BrainModel`; `DigitalBrainNames`; the AppHost's `ProductSurfaceResources` names (kernel/mcp/scripting — read `src/Aspire/DigitalBrain.AppHost/ProductSurfaceResources.cs` for the literal values).
- Produces: the anti-rot conformance suite (spec §6.1) — model-only, milliseconds, no containers.

- [ ] **Step 1: Topology conformance** (each `[Fact]` against the shared model):
  - Fabric resources exist: `storage`, `clustering`, `reminders`, `journal`, `streams`, `pubsub` (values from `DigitalBrainNames` — the names conformance test, not string literals).
  - `kernel`, `mcp`, `scripting` project resources exist (names from the same constants `ProductSurfaceResources` uses — read the file; if those consts are `internal` to the AppHost, use the literal strings with a comment pointing at the source).
  - `scripting` carries the explicit-start annotation (find the annotation type with `model.Resource("scripting").Annotations` — Aspire 13's `ExplicitStartupAnnotation` or equivalent; locate by listing annotation type names in the failure message if the first guess misses). **This pins the phase-1 mcp fix.**
  - `mcp` has a `WaitAnnotation` on `kernel`.
- [ ] **Step 2: Names conformance:** rendered env of `kernel` (`RenderedEnvironmentAsync("kernel")`) contains `ConnectionStrings__clustering`, `ConnectionStrings__reminders`, `ConnectionStrings__streams`, `ConnectionStrings__pubsub`, `ConnectionStrings__journal` keys (case-insensitive compare; these prove hosting-side resource names and runtime-side keyed-client names agree — the single-source `DigitalBrainNames` conformance from spec §4). Plus: a `WithBrainTestMode()` unit check — apply it to a throwaway builder resource and assert the env var `DigitalBrain__Mode=Testing` lands (build a tiny `DistributedApplication.CreateBuilder`-based model inside the test, or assert via `RenderedEnvironmentAsync` after stamping — whichever compiles cleanly against the testing builder).
- [ ] **Step 3:** slnx; build gate; `dotnet test tests/DigitalBrain.Aspire.Tests -c Debug` → green (no Docker needed — nothing starts).
- [ ] **Step 4:** Commit: `"Add Tier 1 model conformance suite"`.

---

### Task 5: `DigitalBrain.Testing.E2E` package (Tier 3 SDK)

**Files:**
- Create: `src/Testing/DigitalBrain.Testing.E2E/DigitalBrain.Testing.E2E.csproj` (`IsPackable=true`; refs: `DigitalBrain.Testing` project, `DigitalBrain.Aspire` project (for the ScriptHost client path); bare `Aspire.Hosting.Testing` comes transitively)
- Create: `src/Testing/DigitalBrain.Testing.E2E/BrainE2EOptions.cs`
- Create: `src/Testing/DigitalBrain.Testing.E2E/BrainAppHostFixture.cs`
- Create: `src/Testing/DigitalBrain.Testing.E2E/BrainSession.cs`
- Create: `src/Testing/DigitalBrain.Testing.E2E/ResourceLogCollector.cs`
- Modify: `DigitalBrain.slnx`

**Interfaces:**
- Consumes: `DigitalBrainScriptHost` (read `src/Aspire/DigitalBrain.Aspire/DigitalBrainScriptHost.cs` FIRST — its connect entry point builds an Orleans client from `ConnectionStrings:clustering` + `ConnectionStrings:streams`; reuse it or mirror its host construction if its signature doesn't accept injected connection strings), `BrainTestMode.WithBrainTestMode`, `JournalWait`.
- Produces:
  - `public sealed class BrainE2EOptions { string[] Args = []; string[] ExplicitStart = ["ollama", "openwebui"]; string[] ExpectedHealthy = ["kernel", "mcp"]; TimeSpan HealthTimeout = TimeSpan.FromMinutes(5); }` — note: the Flutter window-host resource name goes into `ExplicitStart` too; read `src/Modules/UI/DigitalBrain.Modules.UI.Aspire.Hosting/ShellHostingExtensions.cs` for its resource name and put the actual literal in the default list.
  - `public class BrainAppHostFixture<TAppHost> : IAsyncLifetime where TAppHost : class { DistributedApplication App; IDigitalBrain BrainFor(string owner); Task<BrainSession> OpenSessionAsync(); HttpClient CreateHttpClient(string resource, string? endpointName = null); virtual BrainE2EOptions Configure(); }`
  - `public sealed class BrainSession : IAsyncDisposable { IDigitalBrain Brain; OwnerId Owner; Task<SynapseDelivery> WaitForJournalAsync(NeuronId subject, JournalKind kind, Func<SynapseDelivery,bool> match, TimeSpan? timeout = null); }` — `OpenSessionAsync` mints a unique owner (`e2e-{8 hex}`), calls `ActivateAsync`, wraps `JournalWait`.

- [ ] **Step 1: Fixture InitializeAsync pipeline** (each item is a small private method):
  1. `DistributedApplicationTestingBuilder.CreateAsync<TAppHost>(options.Args)`.
  2. Stub every `ParameterResource` (`Configuration[$"Parameters:{p.Name}"] = "test"`).
  3. Container isolation: every `ContainerResource` → `ContainerLifetime.Session` annotation replace + remove `ContainerMountAnnotation`s (volumes) — mirror TripRadar's recovered pattern: locate annotation, remove, re-add via `builder.CreateResourceBuilder(resource)`.
  4. Proxied-port randomization: every `EndpointAnnotation` with a fixed `Port` on a proxied endpoint → `Port = null`. EXCEPTION: leave the kernel's `isProxied: false` HTTP endpoint (5080) untouched — the kernel serves unproxied by design and the session's health check uses `App.CreateHttpClient`, not the literal port.
  5. `WithExplicitStart()` on every resource named in `options.ExplicitStart` (skip silently if absent).
  6. `WithBrainTestMode()` on every `ProjectResource`.
  7. Attach `ResourceLogCollector` (below), `BuildAsync`, `StartAsync`.
  8. Parallel `ResourceNotifications.WaitForResourceHealthyAsync` for `options.ExpectedHealthy`, each `.WaitAsync(options.HealthTimeout)`; on failure, throw with the collector's diagnostics: per expected resource its `TryGetCurrentState` snapshot (State/ExitCode/HealthStatus) + last 40 log lines.
  9. Resolve the clustering + streams connection strings (`App.GetConnectionStringAsync("clustering")`, `"streams"`) and construct the Orleans client host via the `DigitalBrainScriptHost` path; `BrainFor(owner)` = `DigitalBrainClient.Connect(<client host's IGrainFactory/IClusterClient>, owner)`.
- [ ] **Step 2: ResourceLogCollector** — subscribe `ResourceLoggerService.WatchAsync(resourceId)` for the expected-healthy set, ring-buffer 500 lines each, expose `LastLines(name, count)`; `IAsyncDisposable` cancels watchers. Mirror TripRadar's recovered collector shape (described in the spec's fact base) but keep it minimal — no file export yet (CI artifacts are phase 4).
- [ ] **Step 3:** DisposeAsync: dispose sessions' client host, then `App.DisposeAsync()`.
- [ ] **Step 4:** slnx; build gate → exit 0.
- [ ] **Step 5:** Commit: `"Add DigitalBrain.Testing.E2E SDK package (AppHost fixture + brain sessions)"`.

---

### Task 6: `tests/DigitalBrain.E2E.Tests` (Tier 3 smoke)

**Files:**
- Create: `tests/DigitalBrain.E2E.Tests/DigitalBrain.E2E.Tests.csproj` (MTP shape; refs: `DigitalBrain.Testing.E2E` + AppHost with `IsAspireProjectResource="false"`)
- Create: `tests/DigitalBrain.E2E.Tests/E2ECollection.cs` (`AppHostFixture : BrainAppHostFixture<Projects.DigitalBrain_AppHost>` + collection definition + `[assembly: CollectionBehavior(DisableTestParallelization = true)]`)
- Create: `tests/DigitalBrain.E2E.Tests/BootSmokeTests.cs`
- Modify: `DigitalBrain.slnx`

**Interfaces:** consumes Task 5's fixture/session exactly as produced.

- [ ] **Step 1: Tests**

```csharp
[Fact]
public async Task KernelServesHealth()
{
    using var http = fixture.CreateHttpClient("kernel");
    var response = await http.GetAsync("/health");
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
}

[Fact]
public async Task FacadeFiresAcrossProcessesAndJournals()
{
    await using var session = await fixture.OpenSessionAsync();
    // Activation already fired during OpenSessionAsync; observe its journal footprint
    // using the same subject expression the Tier 2 smoke pinned (mirror JournalSmokeTests).
    var delivery = await session.WaitForJournalAsync(
        <subject from Task 3's pinned flow>, <kind from Task 3>, d => true, TimeSpan.FromSeconds(60));
    Assert.NotNull(delivery.Synapse);
}
```

The `<subject/kind>` placeholders are resolved by READING Task 3's committed `JournalSmokeTests.cs` and reusing its exact pinned flow — the two tiers must assert the same semantics through the same facade. The kernel's HTTP endpoint name: check `AppHost.cs`'s `WithHttpEndpoint(... name: ShellHostingExtensions.HttpEndpointName)` and pass that endpoint name to `CreateHttpClient` if the default lookup fails.

- [ ] **Step 2:** Preflight `docker info` (daemon must be reachable; if not, STOP and report). Build gate; then `dotnet test tests/DigitalBrain.E2E.Tests -c Debug` (timeout 600000 — first run pulls nothing new; Azurite/persistent containers exist) → all green.
- [ ] **Step 3:** Commit: `"Add Tier 3 e2e boot smoke driving the facade across processes"`.

---

### Task 7: Full gate — everything builds, every suite green

**Files:** none (verification; residual fixes route through the controller's rules: only compile/test fixes traceable to a specific failure, no design changes).

- [ ] **Step 1:** `dotnet build DigitalBrain.slnx -warnaserror` → exit 0.
- [ ] **Step 2:** `dotnet test tests/DigitalBrain.Aspire.Tests -c Debug` → green. `dotnet test tests/DigitalBrain.Simulation.Tests -c Debug` → green. `dotnet test tests/DigitalBrain.E2E.Tests -c Debug` → green (Docker running).
- [ ] **Step 3:** Report the three suites' test counts and durations. No commit unless fixes were needed.

---

### Task 8: Spec amendment for the ruled scope deltas

**Files:**
- Modify: `docs/superpowers/specs/2026-08-18-digitalbrain-aspire-testing-sdk-design.md`

- [ ] **Step 1:** §11 phase 2 row: change deliverable to "Testing SDK: `DigitalBrain.Testing` + `DigitalBrain.Testing.E2E` packages, Tier 1 conformance suite, Simulation, E2E fixtures + `BrainSession`, test-mode contract (`.Bdd` package moves to phase 3 with its first real consumers; `BrainTestHost.Compose` + fake-server toolkit + Playwright move to phases 3–5)". Exit criterion unchanged.
- [ ] **Step 2:** §4 layout block: add `tests/DigitalBrain.Simulation.Tests/   Tier 2 simulation smoke + semantics pins` under the `tests/` section.
- [ ] **Step 3:** §6.1 `DigitalBrain.Testing` bullet: replace `BrainSimulationFixture<TModule>` wording with "fixtures supply explicit `ModuleAssemblies`"; note `sim.Time`/`Capture`/`MockEmbeddingGenerator` arrive with their phase-3 consumers.
- [ ] **Step 4:** Build gate (uniform); commit: `"Amend spec for phase 2 scope rulings"`.
