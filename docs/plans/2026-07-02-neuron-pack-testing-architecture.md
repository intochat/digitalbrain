# Neuron/Pack Testing Architecture Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give pack authors a reusable, Driver-Pattern-backed Reqnroll vocabulary for cross-neuron and cross-replica specs; flip `CapabilityGate` to an allowlist; delete the redundant `JournalJsonContext` registry in favor of Orleans' native serializer; replace `Neuron.cs`'s hand-rolled broadcast subscription bookkeeping with Orleans' native `AddBroadcastChannel`.

**Architecture:** Ten tasks, sequenced for safety (spike → additive test infra → contained security fix → core-touching swaps last), each independently testable. See `docs/specs/2026-07-02-neuron-pack-testing-architecture-design.md` for full rationale.

**Tech Stack:** .NET/C#, Orleans 10.2 (`Orleans.Journaling` alpha, `Orleans.BroadcastChannel`), Reqnroll, xUnit, Roslyn (`Microsoft.CodeAnalysis.CSharp`).

## Global Constraints

- No `app/` (Flutter) changes — this plan is entirely `brain/` backend/test architecture.
- No new grain-type creation path — all new neuron behavior remains pack-only (`IPackBehavior`).
- Every existing seeded pack in `MarketplaceSeeds.cs` must still compile and install successfully after Task 6 (CapabilityGate allowlist) — this is a hard regression gate, not a judgment call.
- Task 1 (spike) gates Task 8's exact approach: if `JournaledStateManagerOptions.JournalFormatKey = "orleans-binary"` (or equivalent) doesn't eliminate the need for JSON type registration within the spike's bounded investigation, Task 8 falls back to the Roslyn source-generator alternative documented in that task.
- Full `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj` must pass after every task before moving to the next — no batching failures forward.

---

### Task 1: Spike — confirm Orleans.Journaling's native (non-JSON) format eliminates manual type registration

**Files:**
- Create: `DigitalBrain.Tests/Spikes/JournalFormatSpikeTests.cs`
- Create: `DigitalBrain.Tests/Spikes/README.md` (records the finding — kept as a durable decision record, not deleted after the spike)

**Interfaces:**
- Produces: a documented yes/no finding (in the README) that Task 8 depends on. No production code changes in this task.

- [ ] **Step 1: Inspect the actual installed `Microsoft.Orleans.Journaling` package's public API surface**

Context7 and web search both had thin coverage of this alpha API (`ORLEANSEXP005`). Rather than guess, discover it directly: create a throwaway scratch file referencing the namespace and let the compiler enumerate what's actually there.

```csharp
// DigitalBrain.Tests/Spikes/_scratch_journaling_api_probe.cs (temporary — delete after Step 1)
using Orleans.Journaling;
#pragma warning disable ORLEANSEXP005

namespace DigitalBrain.Tests.Spikes;

public static class ApiProbe
{
    public static void Probe()
    {
        // Intentionally reference types/members whose existence and shape are uncertain.
        // Build errors ("type or namespace does not exist", "no such method") tell you
        // the real surface without touching any NuGet cache directly.
        var options = new JournaledStateManagerOptions();
        // options.JournalFormatKey = "orleans-binary"; // uncomment once JournaledStateManagerOptions is confirmed to exist
    }
}
```

Run: `cd brain && dotnet build DigitalBrain.Tests/DigitalBrain.Tests.csproj 2>&1 | grep -A2 "journaling_api_probe"`
Expected: either a clean build (type exists, uncomment the next line and re-build to discover `JournalFormatKey`'s real name/type) or CS0246/CS1061 errors naming exactly what's missing — record whichever happens in the README from Step 4.

Delete `_scratch_journaling_api_probe.cs` once the real API shape is confirmed (it's not a permanent test).

- [ ] **Step 2: Write a minimal round-trip test using the confirmed API**

Using whatever Step 1 confirmed, configure a `TestCluster` silo with a real (not the in-memory no-op `TestJournaledStateManager`/`InMemoryDurableList`) journal storage path — the point of this spike is to exercise actual serialization, which the standard `NeuronTestSiloConfigurator` deliberately skips. Use a `MemoryStream`-backed custom storage adapter so the test stays fast and dependency-free (no Azurite needed for the spike):

```csharp
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using Orleans.Journaling;
using Orleans.TestingHost;
using Xunit;

namespace DigitalBrain.Tests.Spikes;

#pragma warning disable ORLEANSEXP005

public class JournalFormatSpikeTests
{
    [Fact]
    public async Task Orleans_Native_Format_Round_Trips_A_Synapse_Without_JournalJsonContext()
    {
        // Fill in AddXxxJournalStorage + the format-selection call confirmed by Step 1.
        // The assertion that matters: this compiles and passes WITHOUT any reference to
        // DigitalBrain.Kernel.JournalJsonContext anywhere in this file or its silo configurator.
        var cluster = new TestClusterBuilder()
            .AddSiloBuilderConfigurator<NativeFormatSiloConfigurator>()
            .Build();
        cluster.Deploy();

        try
        {
            var grain = cluster.GrainFactory.GetGrain<IDemoNeuron>("spike-native-format");
            await grain.FireAsync(new DemoMessageSynapse("spike-payload"));

            var timeline = await grain.GetTimelineAsync();
            Assert.Contains(timeline, s => s is DemoMessageSynapse d && d.Text == "spike-payload");
        }
        finally
        {
            cluster.StopAllSilos();
        }
    }
}

file sealed class NativeFormatSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder siloBuilder)
    {
        // Step 1's finding goes here — e.g.:
        // siloBuilder.AddMemoryStreams("Default")... .Configure<JournaledStateManagerOptions>(o => o.JournalFormatKey = "orleans-binary");
        // Left intentionally incomplete pending Step 1's confirmed API — this is the ONE
        // place in this plan where exact code is deferred to an investigation result,
        // per this task's own stated purpose.
    }
}
```

- [ ] **Step 3: Run the spike test**

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "JournalFormatSpikeTests"`
Expected: PASS if native format works without JSON registration; a compile or runtime error naming what's missing otherwise.

- [ ] **Step 4: Record the finding**

Write `DigitalBrain.Tests/Spikes/README.md`:

```markdown
# Journal Format Spike — Finding

Date: <fill in actual date when run>
Confirmed API: <exact type/property/method names discovered in Step 1>
Result: <PASS — native format works, no JSON registration needed | FAIL — reason>
Decision: Task 8 proceeds with <native-format deletion of JournalJsonContext | Roslyn source-generator fallback>
```

- [ ] **Step 5: Commit**

```bash
git -C brain add DigitalBrain.Tests/Spikes/
git -C brain commit -m "spike: confirm Orleans.Journaling native format eliminates manual type registration"
```

If Step 1 or Step 2 cannot reach a clear PASS/FAIL within this bounded investigation (e.g. the alpha API's shape genuinely can't be determined from available tools), report **BLOCKED** with the specific compiler errors encountered — the controller decides whether to spend more investigation time or commit to the Task 8 fallback immediately.

---

### Task 2: `PackSpecDriver` — the shared engine behind every pack spec

**Files:**
- Create: `DigitalBrain.Tests/Specs/PackSpecDriver.cs`
- Test: `DigitalBrain.Tests/Specs/PackSpecDriverTests.cs`

**Interfaces:**
- Consumes: `NeuronTestBase` (`Grain<TGrain>(string)`, `FireAsync<T>`, `Cluster`) — `DigitalBrain.TestKit/NeuronTestBase.cs:7-28`, already proven by `HandlerGrowthTests`.
- Produces: `PackSpecDriver` — constructed with a `NeuronTestBase`-derived host (passed as `INeuronTestHost` — see below), used by Task 3's step bindings and Task 5's migration.

```csharp
public interface INeuronTestHost
{
    TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey;
    Task FireAsync<T>(T synapse) where T : Synapse;
}

public sealed class PackSpecDriver(INeuronTestHost host)
{
    public Task PublishPackAsync(string name, string version, string code, string ownerId = "spec-author");
    public Task InstallPackAsync(string name, string version, string buyerId = "spec-buyer");
    public Task FireSynapseAtPackAsync(string packName, Synapse synapse);
    public Task<IReadOnlyList<PackEmission>> GetEmissionsAsync(string packName);
    public Task AssertEmittedAsync(string packName, string expectedOutput);
}
```

- [ ] **Step 1: Write the failing test**

```csharp
// DigitalBrain.Tests/Specs/PackSpecDriverTests.cs
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.TestKit;
using Xunit;

namespace DigitalBrain.Tests.Specs;

public class PackSpecDriverTests : NeuronTestBase
{
    [Fact]
    public async Task PublishInstallFire_RoundTrips_A_Minimal_Pack()
    {
        const string packCode = """
            public sealed class DriverProbePack : DigitalBrain.Core.IPackBehavior
            {
                public string Respond(string input) => "driver-echo:" + (input ?? string.Empty);
            }
            """;

        var driver = new PackSpecDriver(new NeuronTestHostAdapter(this));
        await driver.PublishPackAsync("DriverProbePack", "1.0", packCode);
        await driver.InstallPackAsync("DriverProbePack", "1.0");
        await driver.FireSynapseAtPackAsync("DriverProbePack", new ExperienceUsed("DriverProbePack", "probe"));

        await driver.AssertEmittedAsync("DriverProbePack", "driver-echo:probe");
    }
}
```

Note: `NeuronTestBase`'s `Grain<TGrain>`/`FireAsync<T>` are `protected` — `PackSpecDriverTests` (which extends `NeuronTestBase`) needs a small adapter (`NeuronTestHostAdapter`, private nested class) that forwards to `this.Grain<TGrain>`/`this.FireAsync` so `PackSpecDriver` can be a plain class outside the `NeuronTestBase` hierarchy (this is exactly the seam Task 3's Reqnroll `[Binding]` classes need too — they can't extend `NeuronTestBase` directly, since Reqnroll owns their construction via its DI container).

```csharp
    private sealed class NeuronTestHostAdapter(PackSpecDriverTests owner) : INeuronTestHost
    {
        public TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey => owner.Grain<TGrain>(key);
        public Task FireAsync<T>(T synapse) where T : Synapse => owner.FireAsync(synapse);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "PublishInstallFire_RoundTrips_A_Minimal_Pack"`
Expected: FAIL — `PackSpecDriver`/`INeuronTestHost` don't exist yet (CS0246).

- [ ] **Step 3: Implement `PackSpecDriver`**

```csharp
// DigitalBrain.Tests/Specs/PackSpecDriver.cs
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using Xunit;

namespace DigitalBrain.Tests.Specs;

public interface INeuronTestHost
{
    TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey;
    Task FireAsync<T>(T synapse) where T : Synapse;
}

public sealed class PackSpecDriver(INeuronTestHost host)
{
    private static string GeneratedKeyFor(string packName) => "generated-" + packName.ToLowerInvariant();

    public Task PublishPackAsync(string name, string version, string code, string ownerId = "spec-author") =>
        host.Grain<IMarketplaceNeuron>("market-spec").FireAsync(
            new PublishToMarketplace(name, version, Code: code, OwnerId: ownerId, IsPrivate: false, CommissionRate: 0.0));

    public Task InstallPackAsync(string name, string version, string buyerId = "spec-buyer") =>
        host.Grain<IMarketplaceNeuron>("market-spec").FireAsync(
            new InstallFromMarketplace(name, version, BuyerId: buyerId));

    public Task FireSynapseAtPackAsync(string packName, Synapse synapse) =>
        host.Grain<IGeneratedNeuron>(GeneratedKeyFor(packName)).FireAsync(synapse);

    public async Task<IReadOnlyList<PackEmission>> GetEmissionsAsync(string packName)
    {
        var timeline = await host.Grain<IGeneratedNeuron>(GeneratedKeyFor(packName)).GetTimelineAsync();
        return timeline.OfType<PackEmission>().ToList();
    }

    public async Task AssertEmittedAsync(string packName, string expectedOutput)
    {
        var emissions = await GetEmissionsAsync(packName);
        Assert.Contains(emissions, e => e.Pack == packName && e.Output == expectedOutput);
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "PublishInstallFire_RoundTrips_A_Minimal_Pack"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git -C brain add DigitalBrain.Tests/Specs/PackSpecDriver.cs DigitalBrain.Tests/Specs/PackSpecDriverTests.cs
git -C brain commit -m "test(specs): add PackSpecDriver, the shared engine behind pack Reqnroll specs"
```

---

### Task 3: Reqnroll step bindings on the Driver + first co-located `.feature` spec

**Files:**
- Create: `DigitalBrain.Tests/Authoring/DriverProbePack/DriverProbePack.feature`
- Create: `DigitalBrain.Tests/Authoring/DriverProbePack/DriverProbePack.cs` (the pack source, as a plain embedded string constant — mirrors `MarketplaceSeeds.TelegramResponderPackCode`'s pattern but co-located per this spec's §2.1 convention)
- Create: `DigitalBrain.Tests/Steps/PackSpecSteps.cs`

**Interfaces:**
- Consumes: `PackSpecDriver`, `INeuronTestHost` (Task 2).
- Produces: the reusable Gherkin vocabulary every later pack spec (Task 5, Task 7, Task 10) is written against: *Given a pack "X" with source "Y"*, *Given pack "X" is installed*, *When I fire synapse "T" at pack "X" with props {...}*, *Then pack "X" emits "V"*.

- [ ] **Step 1: Write the failing feature file**

```gherkin
# DigitalBrain.Tests/Authoring/DriverProbePack/DriverProbePack.feature
Feature: Driver probe pack
  As a pack author
  I want a minimal pack proven by the shared Reqnroll vocabulary
  So that the vocabulary itself is validated end to end

@packspec
Scenario: A minimal pack echoes its input
  Given a pack "DriverProbePack" version "1.0" with source from "DriverProbePack.cs"
  And pack "DriverProbePack" is installed
  When I fire synapse "ExperienceUsed" at pack "DriverProbePack" with pack "DriverProbePack" action "probe"
  Then pack "DriverProbePack" emits "driver-echo:probe"
```

- [ ] **Step 2: Add the co-located pack source**

```csharp
// DigitalBrain.Tests/Authoring/DriverProbePack/DriverProbePack.cs
namespace DigitalBrain.Tests.Authoring.DriverProbePack;

// Embedded as a resource so PackSpecSteps can read it by filename without a hand-copied string
// duplicate — same pattern MarketplaceSeeds.PersonalAssistantPackCode already uses for the real
// PersonalAssistantNeuron.cs source.
public static class Source
{
    public const string Code = """
        public sealed class DriverProbePack : DigitalBrain.Core.IPackBehavior
        {
            public string Respond(string input) => "driver-echo:" + (input ?? string.Empty);
        }
        """;
}
```

- [ ] **Step 3: Run to verify it fails**

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~DriverProbePack"`
Expected: FAIL — Reqnroll reports "no matching step definition" for every step (no `[Binding]` class exists yet).

- [ ] **Step 4: Implement the step bindings**

```csharp
// DigitalBrain.Tests/Steps/PackSpecSteps.cs
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.TestKit;
using DigitalBrain.Tests.Specs;
using Reqnroll;
using Xunit;

namespace DigitalBrain.Tests.Steps;

// Reqnroll owns construction of [Binding] classes via its own per-scenario DI container — this
// class can't extend NeuronTestBase directly, so it hosts one itself and forwards through the
// same INeuronTestHost seam PackSpecDriverTests uses. One cluster per scenario (Reqnroll's
// ScenarioContainer lifetime), matching NeuronTestBase's own IAsyncLifetime semantics.
[Binding]
public sealed class PackSpecSteps : NeuronTestBase, IAsyncLifetime
{
    private PackSpecDriver? _driver;
    private PackSpecDriver Driver => _driver ??= new PackSpecDriver(new HostAdapter(this));

    [Given(@"a pack ""(.*)"" version ""(.*)"" with source from ""(.*)""")]
    public async Task GivenAPackWithSourceFrom(string name, string version, string sourceFileName)
    {
        var code = sourceFileName switch
        {
            "DriverProbePack.cs" => Authoring.DriverProbePack.Source.Code,
            _ => throw new NotSupportedException($"Unknown pack source file '{sourceFileName}'.")
        };
        await Driver.PublishPackAsync(name, version, code);
    }

    [Given(@"pack ""(.*)"" is installed")]
    public async Task GivenPackIsInstalled(string name) => await Driver.InstallPackAsync(name, "1.0");

    [When(@"I fire synapse ""ExperienceUsed"" at pack ""(.*)"" with pack ""(.*)"" action ""(.*)""")]
    public async Task WhenIFireExperienceUsed(string targetPack, string packArg, string action) =>
        await Driver.FireSynapseAtPackAsync(targetPack, new ExperienceUsed(packArg, action));

    [Then(@"pack ""(.*)"" emits ""(.*)""")]
    public async Task ThenPackEmits(string name, string expectedOutput) =>
        await Driver.AssertEmittedAsync(name, expectedOutput);

    private sealed class HostAdapter(PackSpecSteps owner) : INeuronTestHost
    {
        public TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey => owner.Grain<TGrain>(key);
        public Task FireAsync<T>(T synapse) where T : Synapse => owner.FireAsync(synapse);
    }
}
```

- [ ] **Step 5: Run to verify it passes**

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~DriverProbePack"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git -C brain add DigitalBrain.Tests/Authoring/DriverProbePack/ DigitalBrain.Tests/Steps/PackSpecSteps.cs
git -C brain commit -m "feat(specs): reusable Gherkin vocabulary for pack behavior, backed by PackSpecDriver"
```

---

### Task 4: 3-silo cluster capability on the Driver

**Files:**
- Modify: `DigitalBrain.Tests/Specs/PackSpecDriver.cs`
- Modify: `DigitalBrain.Tests/Steps/PackSpecSteps.cs`
- Create: `DigitalBrain.Tests/Authoring/DriverProbePack/DriverProbeClusterBroadcast.feature`

**Interfaces:**
- Consumes: `NeuronTestBase.InitialSilosCount` (`DigitalBrain.TestKit/NeuronTestBase.cs:13`), `PackSpecDriver` (Task 2).
- Produces: `PackSpecDriver.AssertReceivedOnAnotherSiloAsync(string receiverGrainKey, string expectedSynapseType)` and the Gherkin vocabulary `Given the cluster has 3 replicas` / `Then "X" observes the broadcast on another silo` — reused verbatim by Task 10.

- [ ] **Step 1: Write the failing feature**

```gherkin
# DigitalBrain.Tests/Authoring/DriverProbePack/DriverProbeClusterBroadcast.feature
Feature: Cross-replica broadcast
  As a pack author
  I want to prove a broadcast reaches subscribers regardless of which silo hosts them
  So that cluster/HA behavior is a reusable, provable spec capability, not a manual aspire-run check

@packspec @cluster
Scenario: A broadcast synapse reaches a subscriber on another silo
  Given the cluster has 3 replicas
  And a pack "DriverProbePack" version "1.0" with source from "DriverProbePack.cs"
  And pack "DriverProbePack" is installed
  When a broadcast synapse "DemoMessageSynapse" with text "cross-silo-probe" is fired
  Then pack "DriverProbePack" observes the broadcast on another silo
```

- [ ] **Step 2: Run to verify it fails**

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~DriverProbeClusterBroadcast"`
Expected: FAIL — no matching step for "the cluster has 3 replicas" / "observes the broadcast on another silo".

- [ ] **Step 3: Add cluster-aware steps and driver method**

`PackSpecSteps` needs `InitialSilosCount` to be settable per-scenario before `NeuronTestBase.InitializeAsync()` runs (which happens via Reqnroll's `IAsyncLifetime` before any step executes) — override it based on a flag the `Given` step sets *before* the base class initializes. Since `NeuronTestBase.InitializeAsync()` runs unconditionally via `IAsyncLifetime`, and Reqnroll also calls `IAsyncLifetime.InitializeAsync()` before the first step, the silo count must be decided in the constructor, not a step body. Resolve this by reading an `[FeatureContext]`/tag-driven value: Reqnroll exposes the current scenario's tags via `ScenarioContext` injected in the constructor.

```csharp
// Addition to DigitalBrain.Tests/Steps/PackSpecSteps.cs — replace the class declaration and add:
[Binding]
public sealed class PackSpecSteps : NeuronTestBase, IAsyncLifetime
{
    private readonly bool _wantsThreeSilos;
    private PackSpecDriver? _driver;
    private PackSpecDriver Driver => _driver ??= new PackSpecDriver(new HostAdapter(this));

    public PackSpecSteps(ScenarioContext scenarioContext) =>
        _wantsThreeSilos = scenarioContext.ScenarioInfo.Tags.Contains("cluster");

    protected override short InitialSilosCount => (short)(_wantsThreeSilos ? 3 : 1);

    [Given(@"the cluster has 3 replicas")]
    public void GivenTheClusterHasThreeReplicas()
    {
        // No-op step: InitialSilosCount is already resolved from the @cluster tag by the time
        // this runs (NeuronTestBase.InitializeAsync ran before any step). This step exists purely
        // for Gherkin readability — the tag is what actually drives cluster size.
        Assert.Equal(3, InitialSilosCount);
    }

    [When(@"a broadcast synapse ""DemoMessageSynapse"" with text ""(.*)"" is fired")]
    public async Task WhenABroadcastSynapseIsFired(string text) =>
        await Driver.BroadcastAsync(new DemoMessageSynapse(text) with { IsBroadcast = true });

    [Then(@"pack ""(.*)"" observes the broadcast on another silo")]
    public async Task ThenPackObservesTheBroadcastOnAnotherSilo(string packName) =>
        await Driver.AssertBroadcastObservedAsync(packName);
}
```

Add to `PackSpecDriver`:

```csharp
// Addition to DigitalBrain.Tests/Specs/PackSpecDriver.cs
public Task BroadcastAsync<T>(T synapse) where T : Synapse =>
    host.Grain<IGeneratedNeuron>(GeneratedKeyFor("DriverProbePack")).FireAsync(synapse);

public async Task AssertBroadcastObservedAsync(string packName)
{
    var incoming = await host.Grain<IGeneratedNeuron>(GeneratedKeyFor(packName)).GetIncomingTimelineAsync();
    Assert.Contains(incoming, s => s is DemoMessageSynapse d && d.Text == "cross-silo-probe");
}
```

`INeuronTestHost` needs `GetIncomingTimelineAsync` exposed — add it:

```csharp
public interface INeuronTestHost
{
    TGrain Grain<TGrain>(string key) where TGrain : IGrainWithStringKey;
    Task FireAsync<T>(T synapse) where T : Synapse;
}
```

(No change needed here — `Grain<IGeneratedNeuron>(...)` already exposes `GetIncomingTimelineAsync()` directly on the grain interface per `Neuron.cs:182-183`; `AssertBroadcastObservedAsync` calls it straight off the grain, not through a new host method.)

- [ ] **Step 4: Run to verify it passes**

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~DriverProbeClusterBroadcast"`
Expected: PASS — proves a 3-silo `TestCluster` (Orleans' own `TestClusterBuilder`, verified against Microsoft's documented pattern) correctly fans out a broadcast today, via the *existing* `IsBroadcast`/stream mechanism (this task doesn't touch Task 9's `AddBroadcastChannel` swap — it proves the vocabulary works against current behavior first, so Task 10 has a known-good baseline to re-target after Task 9 lands).

- [ ] **Step 5: Commit**

```bash
git -C brain add DigitalBrain.Tests/Specs/PackSpecDriver.cs DigitalBrain.Tests/Steps/PackSpecSteps.cs DigitalBrain.Tests/Authoring/DriverProbePack/DriverProbeClusterBroadcast.feature
git -C brain commit -m "feat(specs): 3-replica cluster broadcast as a reusable spec capability"
```

---

### Task 5: Migrate `TelegramReactiveLoopSteps.cs` onto the shared Driver, delete duplicated boilerplate

**Files:**
- Modify: `DigitalBrain.Tests/Steps/TelegramReactiveLoopSteps.cs` (delete lines 52-56, 207-237 — the hand-rolled `TestClusterBuilder`/`TelegramReactiveLoopSiloConfig`)
- Test: `DigitalBrain.Tests/Features/TelegramExperience.feature` (already exists — unchanged Gherkin, only the binding implementation changes)

**Interfaces:**
- Consumes: `PackSpecSteps`'s pattern from Task 3 (extend `NeuronTestBase` directly instead of hand-building `TestClusterBuilder`).
- Produces: proof that the new pattern actually replaces the old one, not just sits alongside it — the concrete regression check for this task.

- [ ] **Step 1: Confirm current behavior as a baseline**

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~TelegramExperience"`
Expected: PASS (this is the existing, working N+1 reactivity scenario — record the pass before touching anything).

- [ ] **Step 2: Rewrite the binding class to extend `NeuronTestBase`**

Replace the class declaration in `DigitalBrain.Tests/Steps/TelegramReactiveLoopSteps.cs` — delete the hand-rolled `TestClusterBuilder` construction (original lines 52-56) and the entire `TelegramReactiveLoopSiloConfig : ISiloConfigurator` class (original lines 207-237), changing the binding class to:

```csharp
[Binding]
public sealed class TelegramReactiveLoopSteps : NeuronTestBase
{
    // All prior manual DI registrations (in-journal/out-journal, IPackEmbodiment, HomeFeedBus,
    // SignalEgressBus, RejectUnsignedPacks=false, etc.) are now provided by NeuronTestBase's
    // default single-silo TestCluster — the same NeuronTestSiloConfigurator every migrated
    // xUnit test already shares. Only Telegram-specific extras (if any were in the deleted
    // configurator beyond the shared set) go in an override:
    protected override void ConfigureSilo(ISiloBuilder builder)
    {
        // Add back only what NeuronTestSiloConfigurator doesn't already provide, if anything —
        // verify by diffing the deleted TelegramReactiveLoopSiloConfig.Configure body against
        // NeuronTestSiloConfigurator.Configure (DigitalBrain.TestKit/NeuronTestSiloConfigurator.cs:30-65)
        // before assuming this override body can be empty.
    }

    // ... existing step methods and their bodies (GivenBothPacksAreInstalled, WhenATelegramMessage...,
    // ThenAReplyReachesTheEgressBus, etc.) are unchanged — only the class's own construction/cluster
    // setup moves from hand-rolled to inherited.
}
```

- [ ] **Step 3: Run to verify it still passes**

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~TelegramExperience"`
Expected: PASS — same scenario, same assertions, now on the shared harness. If it fails, diff the deleted `TelegramReactiveLoopSiloConfig.Configure` body against `NeuronTestSiloConfigurator.Configure` line by line to find what's missing from the override in Step 2.

- [ ] **Step 4: Run the full suite to check for regressions**

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj`
Expected: same pass/fail counts as the pre-Task-5 baseline (record the count before Step 2 if not already known from CI).

- [ ] **Step 5: Commit**

```bash
git -C brain add DigitalBrain.Tests/Steps/TelegramReactiveLoopSteps.cs
git -C brain commit -m "refactor(tests): migrate TelegramReactiveLoopSteps onto NeuronTestBase, delete duplicated TestClusterBuilder"
```

---

### Task 6: `CapabilityGate` — blocklist to allowlist

**Files:**
- Modify: `DigitalBrain.Kernel/Foundry/CapabilityGate.cs`
- Modify: `DigitalBrain.Tests/Foundry/CapabilityGateTests.cs` (exact path per the earlier research citation — verify and adjust if it differs)

**Interfaces:**
- Consumes: nothing new — same `CapabilityGate.FindViolations(CSharpCompilation)` signature.
- Produces: same signature, same return type (`IReadOnlyList<string>`), inverted logic.

- [ ] **Step 1: Write the failing tests (both directions)**

```csharp
// Additions to DigitalBrain.Tests/Foundry/CapabilityGateTests.cs
[Fact]
public void Rejects_System_Net_By_Default()
{
    var compilation = CompileSnippet("""
        using System.Net.Http;
        public class Probe {
            public void Run() { var c = new HttpClient(); }
        }
        """);
    var violations = CapabilityGate.FindViolations(compilation);
    Assert.NotEmpty(violations);
}

[Fact]
public void Rejects_System_IO_By_Default()
{
    var compilation = CompileSnippet("""
        using System.IO;
        public class Probe {
            public void Run() { File.ReadAllText("x"); }
        }
        """);
    var violations = CapabilityGate.FindViolations(compilation);
    Assert.NotEmpty(violations);
}

[Fact]
public void Allows_DigitalBrain_Core_Types_And_Basic_Collections()
{
    var compilation = CompileSnippet("""
        using System.Collections.Generic;
        using System.Linq;
        public class Probe {
            public int Run() { var list = new List<int> { 1, 2, 3 }; return list.Sum(); }
        }
        """);
    var violations = CapabilityGate.FindViolations(compilation);
    Assert.Empty(violations);
}
```

(`AllowsBenignArithmetic` and `FlagsProcessStart` already exist per the earlier research citation and stay as regression cases for the allowlist's coverage of the original 5 blocklist entries — verify they still pass unmodified after Step 3.)

- [ ] **Step 2: Run to verify the new tests fail**

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~CapabilityGateTests"`
Expected: `Rejects_System_Net_By_Default` and `Rejects_System_IO_By_Default` FAIL (currently allowed — that's the gap this task closes).

- [ ] **Step 3: Flip `CapabilityGate` to an allowlist**

```csharp
// DigitalBrain.Kernel/Foundry/CapabilityGate.cs
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace DigitalBrain.Kernel.Foundry;

public static class CapabilityGate
{
    private static readonly string[] AllowedNamespacePrefixes =
    {
        "System.",              // narrowed further below by explicit exclusions
        "DigitalBrain.Core.",
    };

    // Explicit exclusions within the broad "System." allowance above — these remain banned
    // even though they start with "System.".
    private static readonly string[] ExcludedWithinSystem =
    {
        "System.Net.",
        "System.IO.",
        "System.Diagnostics.Process.",
        "System.Reflection.Emit.",
        "System.Runtime.InteropServices.",
        "System.Runtime.Loader.",
    };

    public static IReadOnlyList<string> FindViolations(CSharpCompilation compilation)
    {
        var violations = new HashSet<string>();
        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree);
            var root = tree.GetRoot();
            foreach (var node in root.DescendantNodes())
            {
                if (node is not (IdentifierNameSyntax or MemberAccessExpressionSyntax or ObjectCreationExpressionSyntax))
                    continue;

                var symbol = model.GetSymbolInfo(node).Symbol;
                if (symbol is null)
                    continue;

                var fullName = symbol.ContainingType is null
                    ? symbol.ToDisplayString()
                    : symbol.ContainingType.ToDisplayString() + "." + symbol.Name;

                if (ExcludedWithinSystem.Any(excluded => fullName.StartsWith(excluded, StringComparison.Ordinal)))
                {
                    violations.Add(ExcludedWithinSystem.First(excluded => fullName.StartsWith(excluded, StringComparison.Ordinal)));
                    continue;
                }

                if (!AllowedNamespacePrefixes.Any(allowed => fullName.StartsWith(allowed, StringComparison.Ordinal)))
                {
                    violations.Add(fullName);
                }
            }
        }
        return violations.ToList();
    }
}
```

(`Microsoft.Win32.Registry.` from the old blocklist is covered automatically — it never matched the new `AllowedNamespacePrefixes` at all, so no explicit exclusion entry is needed for it.)

- [ ] **Step 4: Run to verify all CapabilityGate tests pass**

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~CapabilityGateTests"`
Expected: PASS, including the pre-existing `AllowsBenignArithmetic`/`FlagsProcessStart`.

- [ ] **Step 5: Regression-check every seeded pack still compiles under the allowlist**

```csharp
// New test in DigitalBrain.Tests/Foundry/CapabilityGateTests.cs
[Theory]
[MemberData(nameof(AllSeededPackCodes))]
public void Every_Seeded_Pack_Compiles_Clean_Under_The_New_Allowlist(string packName, string code)
{
    var compilation = CompileSnippet(code);
    var violations = CapabilityGate.FindViolations(compilation);
    Assert.Empty(violations); // fails loudly with the pack's name if the allowlist is too narrow
}

public static IEnumerable<object[]> AllSeededPackCodes() =>
    MarketplaceSeeds.LocalUiPacks
        .Where(p => !string.IsNullOrWhiteSpace(p.Code))
        .Select(p => new object[] { p.Name, p.Code });
```

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~Every_Seeded_Pack_Compiles_Clean"`
Expected: PASS for every pack in `MarketplaceSeeds.LocalUiPacks`. If any fails, widen `AllowedNamespacePrefixes` for exactly what that pack legitimately needs (not a blanket reversion) and re-run.

- [ ] **Step 6: Commit**

```bash
git -C brain add DigitalBrain.Kernel/Foundry/CapabilityGate.cs DigitalBrain.Tests/Foundry/CapabilityGateTests.cs
git -C brain commit -m "security(foundry): flip CapabilityGate from a blocklist to an allowlist"
```

---

### Task 7: CapabilityGate consistency (Tier-2) + gate rules as Reqnroll specs

**Files:**
- Modify: `DigitalBrain.Kernel/Sandbox/OutOfProcessSandbox.cs`
- Modify: `Foundry/README.md`
- Create: `DigitalBrain.Tests/Authoring/CapabilityGate/CapabilityGate.feature`
- Modify: `DigitalBrain.Tests/Steps/PackSpecSteps.cs`

**Interfaces:**
- Consumes: `CapabilityGate.FindViolations` (Task 6), `PackSpecDriver` (Task 2).
- Produces: `PackSpecDriver.AssertCompilationOutcomeAsync(string code, bool shouldSucceed, string? expectedViolationPrefix)`.

- [ ] **Step 1: Decide and document the Tier-1/Tier-2 relationship**

Read `Sandbox/OutOfProcessSandbox.cs:28`'s current `CapabilityGate` usage (or absence) and `Foundry/README.md:20-25`'s existing caveat. This is a documented decision, not silent code — update `Foundry/README.md` to state explicitly either "Tier-2 (out-of-process) relies on OS-level process isolation instead of `CapabilityGate`, by design, because X" or wire the same gate call into the out-of-process compile path. Given `OutOfProcessSandbox.cs:28` already references `CapabilityGate` per the earlier research citation, confirm by reading the file directly whether it's actually invoked or just referenced/unused before deciding which branch applies.

- [ ] **Step 2: Write the failing feature for gate-as-spec**

```gherkin
# DigitalBrain.Tests/Authoring/CapabilityGate/CapabilityGate.feature
Feature: CapabilityGate accept/reject rules
  As the platform
  I want the pack sandbox's own rules expressed as specs
  So that security invariants are provable the same way pack behavior is

@packspec @security
Scenario: A pack using System.Net.Http.HttpClient is rejected
  Given a pack source that calls "System.Net.Http.HttpClient"
  When the pack is compiled
  Then compilation is rejected with violation "System.Net."

@packspec @security
Scenario: A pack using only collections and LINQ is accepted
  Given a pack source that only uses collections and LINQ
  When the pack is compiled
  Then compilation is accepted
```

- [ ] **Step 3: Run to verify it fails**

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~CapabilityGate.feature"`
Expected: FAIL — no matching steps.

- [ ] **Step 4: Add driver method and steps**

```csharp
// Addition to DigitalBrain.Tests/Specs/PackSpecDriver.cs
using DigitalBrain.Kernel.Foundry;
using Microsoft.CodeAnalysis.CSharp;

public IReadOnlyList<string> CheckCompilation(string code)
{
    var compilation = FoundryCompilation.CreateWith(code); // reuse the real production compile path
    return CapabilityGate.FindViolations(compilation);
}
```

```csharp
// Addition to DigitalBrain.Tests/Steps/PackSpecSteps.cs
private IReadOnlyList<string>? _lastViolations;

[Given(@"a pack source that calls ""(.*)""")]
public void GivenAPackSourceThatCalls(string apiCall) => _pendingSource = apiCall switch
{
    "System.Net.Http.HttpClient" => """
        using System.Net.Http;
        public sealed class NetProbe : DigitalBrain.Core.IPackBehavior
        {
            public string Respond(string input) { var c = new HttpClient(); return input; }
        }
        """,
    _ => throw new NotSupportedException($"Unknown probe API '{apiCall}'.")
};

[Given(@"a pack source that only uses collections and LINQ")]
public void GivenAPackSourceUsingCollectionsAndLinq() => _pendingSource = """
    using System.Collections.Generic;
    using System.Linq;
    public sealed class LinqProbe : DigitalBrain.Core.IPackBehavior
    {
        public string Respond(string input) => new List<string> { input }.First();
    }
    """;

[When(@"the pack is compiled")]
public void WhenThePackIsCompiled() => _lastViolations = Driver.CheckCompilation(_pendingSource!);

[Then(@"compilation is rejected with violation ""(.*)""")]
public void ThenCompilationIsRejectedWithViolation(string expectedPrefix) =>
    Assert.Contains(_lastViolations!, v => v.StartsWith(expectedPrefix, StringComparison.Ordinal));

[Then(@"compilation is accepted")]
public void ThenCompilationIsAccepted() => Assert.Empty(_lastViolations!);
```

Add `private string? _pendingSource;` as a field on `PackSpecSteps`.

- [ ] **Step 5: Run to verify it passes**

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~CapabilityGate.feature"`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git -C brain add DigitalBrain.Kernel/Sandbox/OutOfProcessSandbox.cs Foundry/README.md DigitalBrain.Tests/Authoring/CapabilityGate/ DigitalBrain.Tests/Specs/PackSpecDriver.cs DigitalBrain.Tests/Steps/PackSpecSteps.cs
git -C brain commit -m "security(foundry): document/fix Tier-2 gate consistency; express gate rules as Reqnroll specs"
```

---

### Task 8: Delete `JournalJsonContext` (or install the source-generator fallback)

**Files:**
- Delete (primary path) or keep-and-generate (fallback path): `DigitalBrain.Kernel/JournalJsonContext.cs`
- Modify: `DigitalBrain.Kernel/Program.cs:177-179`
- Create (fallback path only): `DigitalBrain.SourceGen/SynapseJsonContextGenerator.cs`

**Interfaces:**
- Consumes: Task 1's spike finding (`DigitalBrain.Tests/Spikes/README.md`).
- Produces: no change to any Synapse type's public shape — this task is purely about the journal's serialization wiring.

- [ ] **Step 1: Read the spike's recorded finding**

Read `DigitalBrain.Tests/Spikes/README.md` (Task 1, Step 4). If it says PASS (native format confirmed), continue with Steps 2-4 below. If FAIL, skip to Step 5 (fallback).

- [ ] **Step 2 (primary path): Reconfigure `Program.cs` to use the native format**

```csharp
// DigitalBrain.Kernel/Program.cs — replace lines 177-179
siloBuilder.AddAzureBlobJournalStorage(options =>
    options.ConfigureBlobServiceClient(builder.Configuration.GetConnectionString("journal")!));
    // .UseJsonJournalFormat(DigitalBrain.Kernel.JournalJsonContext.Default) deleted —
    // replaced by whatever exact configuration Task 1's spike confirmed (e.g. setting
    // JournaledStateManagerOptions.JournalFormatKey, or an equivalent fluent call this
    // task's implementer copies verbatim from the spike's working NativeFormatSiloConfigurator).
```

- [ ] **Step 3 (primary path): Delete `JournalJsonContext.cs` and its references**

```bash
git -C brain rm DigitalBrain.Kernel/JournalJsonContext.cs
```

Grep for any remaining reference (`grep -rn "JournalJsonContext" brain/`) and remove them — expect only `Program.cs` (already handled in Step 2).

- [ ] **Step 4 (primary path): Run the full journal-dependent test suite**

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~PackConfigStoreTests|FullyQualifiedName~HandlerGrowthTests|FullyQualifiedName~PackSpecDriverTests"`
Expected: PASS — no serialization regressions. Then run the full suite (`dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj`) to confirm no other breakage, then skip to Step 8 (commit).

- [ ] **Step 5 (fallback path — only if Step 1 found FAIL): Write the failing generator test**

```csharp
// DigitalBrain.Tests/SourceGen/SynapseJsonContextGeneratorTests.cs
[Fact]
public void Generator_Emits_JsonSerializable_For_Every_Synapse_Subtype_In_Compilation()
{
    var compilation = CompileWithSynapseTypes("""
        public sealed record ProbeSynapse(string Text) : DigitalBrain.Core.Synapse(nameof(ProbeSynapse), System.DateTimeOffset.UtcNow);
        """);
    var result = RunGenerator(new SynapseJsonContextGenerator(), compilation);
    Assert.Contains(result.GeneratedSources, s => s.SourceText.ToString().Contains("[JsonSerializable(typeof(ProbeSynapse))]"));
}
```

- [ ] **Step 6 (fallback path): Run to verify it fails**

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "Generator_Emits_JsonSerializable"`
Expected: FAIL — `SynapseJsonContextGenerator` doesn't exist yet.

- [ ] **Step 7 (fallback path): Implement the incremental generator**

```csharp
// DigitalBrain.SourceGen/SynapseJsonContextGenerator.cs
using Microsoft.CodeAnalysis;
using System.Linq;
using System.Text;

namespace DigitalBrain.SourceGen;

[Generator]
public sealed class SynapseJsonContextGenerator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var synapseTypes = context.CompilationProvider.Select((compilation, _) =>
        {
            var synapseBase = compilation.GetTypeByMetadataName("DigitalBrain.Core.Synapse");
            if (synapseBase is null) return System.Array.Empty<INamedTypeSymbol>();

            return compilation.GetSymbolsWithName(_ => true, SymbolFilter.Type)
                .OfType<INamedTypeSymbol>()
                .Where(t => IsDerivedFrom(t, synapseBase) && !t.IsAbstract)
                .ToArray();
        });

        context.RegisterSourceOutput(synapseTypes, (spc, types) =>
        {
            var sb = new StringBuilder();
            sb.AppendLine("using System.Text.Json.Serialization;");
            sb.AppendLine("namespace DigitalBrain.Kernel;");
            foreach (var t in types.OrderBy(t => t.ToDisplayString()))
                sb.AppendLine($"[JsonSerializable(typeof({t.ToDisplayString()}))]");
            sb.AppendLine("public partial class JournalJsonContext : System.Text.Json.Serialization.JsonSerializerContext;");
            spc.AddSource("JournalJsonContext.g.cs", sb.ToString());
        });
    }

    private static bool IsDerivedFrom(INamedTypeSymbol type, INamedTypeSymbol baseType)
    {
        for (var t = type.BaseType; t is not null; t = t.BaseType)
            if (SymbolEqualityComparer.Default.Equals(t, baseType)) return true;
        return false;
    }
}
```

Delete the hand-written `JournalJsonContext.cs` (its content is now generated) and reference `DigitalBrain.SourceGen` as an analyzer project from `DigitalBrain.Kernel.csproj`.

- [ ] **Step 8: Run full suite, then commit**

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj`
Expected: same pass count as the Task 7 baseline.

```bash
git -C brain add -A
git -C brain commit -m "refactor(journal): delete JournalJsonContext, use Orleans.Journaling native format"
# or, on the fallback path:
git -C brain commit -m "refactor(journal): auto-generate JournalJsonContext via Roslyn incremental generator"
```

---

### Task 9: `Neuron.cs` — swap broadcast subscription + publish to Orleans `AddBroadcastChannel`

**Files:**
- Modify: `DigitalBrain.Kernel/Neuron.cs`
- Modify: `DigitalBrain.Kernel/Program.cs` (silo builder registration)
- Modify: `DigitalBrain.TestKit/NeuronTestSiloConfigurator.cs` (same registration for tests)
- Test: `DigitalBrain.Tests/Kernel/NeuronBroadcastTests.cs` (new — or extend an existing broadcast test file if one exists at that path; verify before creating)

**Interfaces:**
- Consumes: verified Orleans API — `[ImplicitChannelSubscription]`, `IOnBroadcastChannelSubscribed.OnSubscribed(IBroadcastChannelSubscription)`, `subscription.Attach<T>(onItem, onError)`, `IBroadcastChannelProvider.GetChannelWriter<T>(ChannelId)`, `IBroadcastChannelWriter<T>.Publish(T)`.
- Produces: `Neuron.Broadcast(Synapse)` keeps its exact existing signature (`protected Task Broadcast(Synapse s)`) — no caller anywhere in the codebase needs to change.

- [ ] **Step 1: Write the failing test**

```csharp
// DigitalBrain.Tests/Kernel/NeuronBroadcastTests.cs
using DigitalBrain.Core;
using DigitalBrain.TestKit;
using Xunit;

namespace DigitalBrain.Tests.Kernel;

public class NeuronBroadcastTests : NeuronTestBase
{
    [Fact]
    public async Task Broadcast_Reaches_A_Different_Grain_Via_Implicit_Channel_Subscription()
    {
        var sender = Grain<IDemoNeuron>("broadcast-sender");
        var receiver = Grain<IDemoNeuron>("broadcast-receiver");

        // Activate the receiver first so its implicit subscription is established.
        await receiver.FireAsync(new NeuronActivated(new NeuronId("broadcast-receiver")));

        await sender.FireAsync(new DemoMessageSynapse("channel-probe") with { IsBroadcast = true });

        var incoming = await receiver.GetIncomingTimelineAsync();
        Assert.Contains(incoming, s => s is DemoMessageSynapse d && d.Text == "channel-probe");
    }
}
```

- [ ] **Step 2: Run to verify it fails or passes for the wrong reason**

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "Broadcast_Reaches_A_Different_Grain_Via_Implicit_Channel_Subscription"`
Expected: this may already PASS against the *current* `IsBroadcast`/stream mechanism (that's fine — it's the regression baseline). The meaningful check is Step 4: it must still pass *after* the swap, proving the new mechanism preserves the same observable behavior.

- [ ] **Step 3: Register the broadcast channel and swap `Neuron.cs`**

Add to `DigitalBrain.Kernel/Program.cs` (alongside the existing `siloBuilder.AddMemoryStreams("DigitalBrainTimeline")` at line 183) and to `DigitalBrain.TestKit/NeuronTestSiloConfigurator.cs` (alongside its own `.AddMemoryStreams("DigitalBrainTimeline")` at line 36):

```csharp
siloBuilder.AddBroadcastChannel("digitalbrain-timeline");
```

In `DigitalBrain.Kernel/Neuron.cs`, replace the class declaration and the subscription/publish logic:

```csharp
[GrainType("digitalbrain.base.v2")]
[ImplicitChannelSubscription]
public abstract class Neuron(ILogger logger, NeuronJournals journals) :
    DurableGrain, INeuron, IOnBroadcastChannelSubscribed
{
    // ... existing fields/properties unchanged (Logger, journals, Self, CurrentCause, etc.) ...

    // Replaces SubscribeTimelineIfNeeded (deleted) and the IAsyncObserver<Synapse> interface
    // implementation this grain no longer needs — Orleans' own implicit-subscription plumbing
    // now owns activation-time subscribe/resume/dedup.
    public Task OnSubscribed(IBroadcastChannelSubscription subscription)
    {
        if (!ShouldSubscribeToTimeline)
            return Task.CompletedTask; // opt-out per the runtime ShouldSubscribeToTimeline check —
                                        // see design spec 2.2 for why this can't be a compile-time attribute

        return subscription.Attach<Synapse>(OnBroadcastReceived, OnBroadcastError);
    }

    private Task OnBroadcastReceived(Synapse item) =>
        SynapseDispatch.HandledTypes(GetType()).Contains(item.GetType())
            ? SynapseDispatch.DispatchAsync(this, Logger, Self, item)
            : Task.CompletedTask;

    private Task OnBroadcastError(Exception ex)
    {
        Logger.LogError(ex, "Broadcast channel error in {Neuron}", Self);
        return Task.CompletedTask;
    }

    // OnActivateAsync no longer calls SubscribeTimelineIfNeeded() — delete that call
    // (Neuron.cs original line 97). Everything else in OnActivateAsync is unchanged.
}
```

Update `FireAsync`'s broadcast branch (`Neuron.cs:159-162`):

```csharp
if (stamped.IsBroadcast)
{
    var channelId = ChannelId.Create("digitalbrain-timeline", Guid.Empty);
    var writer = this.GetBroadcastChannelProvider("digitalbrain-timeline") // exact accessor name to
        .GetChannelWriter<Synapse>(channelId);                            // confirm against Orleans'
    await writer.Publish(stamped);                                        // grain-side provider API —
}                                                                          // client-side used IClusterClient
                                                                           // per the verified docs snippet;
                                                                           // grain-side equivalent needs a
                                                                           // one-line confirmation during
                                                                           // implementation (this.GetBroadcastChannelProvider
                                                                           // is the Orleans grain-extension-method
                                                                           // naming convention used by every other
                                                                           // grain-side provider accessor in this
                                                                           // codebase, e.g. this.GetStreamProvider).
```

Delete `SubscribeTimelineIfNeeded` (original lines 110-136) and the now-unused `IAsyncObserver<Synapse>`/`OnNextAsync`/`OnCompletedAsync`/`OnErrorAsync` members it required (original lines 20, 140-151) — `OnBroadcastReceived`/`OnBroadcastError` above replace their role.

- [ ] **Step 4: Run to verify it still passes**

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "Broadcast_Reaches_A_Different_Grain_Via_Implicit_Channel_Subscription"`
Expected: PASS, now via `AddBroadcastChannel` instead of the old memory-stream mechanism.

- [ ] **Step 5: Run the full suite**

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj`
Expected: same pass count as the Task 8 baseline — this is the highest-blast-radius task in the plan (every neuron extends `Neuron`), so a full green run here matters more than at any other task.

- [ ] **Step 6: Commit**

```bash
git -C brain add DigitalBrain.Kernel/Neuron.cs DigitalBrain.Kernel/Program.cs DigitalBrain.TestKit/NeuronTestSiloConfigurator.cs DigitalBrain.Tests/Kernel/NeuronBroadcastTests.cs
git -C brain commit -m "refactor(kernel): replace hand-rolled broadcast subscription bookkeeping with Orleans AddBroadcastChannel"
```

---

### Task 10: Re-target the 3-replica cluster spec at the new broadcast mechanism

**Files:**
- Modify: `DigitalBrain.Tests/Authoring/DriverProbePack/DriverProbeClusterBroadcast.feature` (Gherkin text unchanged — this task proves the same spec now exercises the new code path)

**Interfaces:**
- Consumes: Task 4's cluster vocabulary, Task 9's `AddBroadcastChannel` implementation.
- Produces: nothing new — this is the closing verification task tying A and B together, per the design spec's explicit intent that cluster-interaction proof and the broadcast mechanism swap validate each other.

- [ ] **Step 1: Run the existing 3-replica scenario against the new mechanism**

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~DriverProbeClusterBroadcast"`
Expected: PASS — the same Gherkin from Task 4, unmodified, now proving Task 9's `AddBroadcastChannel`-based mechanism fans out correctly across 3 real silos, not just the mechanism that existed when Task 4 was written.

- [ ] **Step 2: If it fails, diagnose against Task 9's implementation specifically**

If Step 1 fails where Task 4's original run passed, the regression is in Task 9's swap, not the vocabulary — re-check `Neuron.cs`'s `OnSubscribed`/`Broadcast` implementation and the `AddBroadcastChannel` registration in `NeuronTestSiloConfigurator.cs` before touching the feature file or driver.

- [ ] **Step 3: Run the complete full suite one final time**

Run: `cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj && aspire doctor`
Expected: full green, matching this plan's opening baseline count, plus a clean `aspire doctor`.

- [ ] **Step 4: Commit (if Step 2's diagnosis required any fix)**

```bash
git -C brain add -A
git -C brain commit -m "test(specs): confirm 3-replica broadcast spec passes against AddBroadcastChannel"
```

If Step 1 passed cleanly with no changes needed, there is nothing to commit — note this explicitly in the task report rather than creating an empty commit.

---

## Self-Review

**Spec coverage:** §2.1 (Driver Pattern + vocabulary) → Tasks 2-3; §2.1's boilerplate-deletion claim → Task 5; §2.2 (BroadcastChannel) → Tasks 9-10; §2.3 (CapabilityGate allowlist + tier consistency + gate-as-spec) → Tasks 6-7; §2.4 (JournalJsonContext deletion, spike-gated) → Tasks 1, 8; §3 sequencing → task order matches exactly (D spike, A, C, D-impl, B). No spec section without a task.

**Placeholder scan:** the one intentionally incomplete code block is `NativeFormatSiloConfigurator.Configure` in Task 1, Step 2 — this is the plan's own declared exception (an investigation task whose purpose is discovering that exact code), not an oversight. Task 9's broadcast-channel-provider accessor name (`this.GetBroadcastChannelProvider`) is flagged inline as needing a one-line confirmation against Orleans' grain-side extension API during implementation, following the same naming convention as the codebase's existing `this.GetStreamProvider` — a reasonable, narrow, explicitly-flagged uncertainty rather than a silent guess.

**Type consistency:** `INeuronTestHost`/`PackSpecDriver` signatures introduced in Task 2 are used identically in Tasks 3, 4, 5, 7 — `Grain<TGrain>`, `FireAsync<T>`, `PublishPackAsync`, `InstallPackAsync`, `FireSynapseAtPackAsync`, `AssertEmittedAsync` never change shape after Task 2. `PackSpecSteps` (introduced Task 3) is extended, never redefined, in Tasks 4 and 7.
