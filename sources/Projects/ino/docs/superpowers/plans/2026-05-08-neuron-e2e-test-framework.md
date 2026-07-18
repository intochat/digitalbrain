# NeuronE2E Test Framework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the fragmented `InoTestAppHost` / `InoBrowserFixture` / per-collection-class scaffolding with a single `NeuronE2ETest<TNeuron, TAppHost>` base + a Reqnroll BDD step library that lets developers write neuron behaviour as `.feature` scenarios first (TDD), and migrate every existing E2E test onto it.

**Architecture:** The base class boots the real Aspire AppHost via `DistributedApplicationTestingBuilder` once per test class, exposes a `NeuronSession` per test that handles gRPC chat streaming, RFW event firing, lazy Playwright browser attach, and outbound-synapse observation via the in-process `ino` `ActivitySource`. A sibling `Ino.NeuronTesting.Bdd` assembly registers a Reqnroll `[Binding]` step library so `.feature` files become the canonical test+spec for every neuron — one folder per neuron, agent-named (e.g. `domains/travel/TripPlanner/`), holds the synapse contract + impl + tests + corpus + RFW builders together. Migration is five additive slices: framework bring-up (smoke), Travel migration (canonical port), other domains (parallel-safe), kernel-level + platform-tests rename, finally retire the dead old infra.

**Tech Stack:** .NET 11 preview, Aspire 13 (`Aspire.Hosting.Testing`), Microsoft.Orleans 10, Microsoft.Playwright, Reqnroll.xUnit.v3, xUnit v3, FluentAssertions, OpenTelemetry .NET `ActivityListener` API.

**Spec:** [`docs/superpowers/specs/2026-05-08-neuron-e2e-test-base-design.md`](../specs/2026-05-08-neuron-e2e-test-base-design.md)

**Mandatory before writing any code:** Use Context7 to verify the current API surface of every library you touch — `Aspire.Hosting.Testing`, `Microsoft.Playwright`, `Reqnroll.xUnit.v3`, `OpenTelemetry`, `Grpc.Net.Client`. The user's `CLAUDE.md` is explicit on this. Do NOT read the local NuGet cache.

---

## File Structure (lock decomposition before tasks)

### New files (Slice 1 — framework bring-up)

| Path | Responsibility |
|------|----------------|
| `src/Ino.NeuronTesting/Ino.NeuronTesting.csproj` | New assembly. Refs Aspire.Hosting.Testing, Playwright, Grpc.Net.Client, Ino.Core, Ino.Core.Hosting, Ino.Kernel.Contracts, Ino.Gateway.Grpc (for client stub), xUnit.v3, FluentAssertions. |
| `src/Ino.NeuronTesting/NeuronE2ETest.cs` | `NeuronE2ETest<TNeuron, TAppHost>` abstract base. xUnit `IAsyncLifetime`. Owns the per-class `NeuronAppHostFixture`. Exposes `Open()` / `Chat(prompt)`. |
| `src/Ino.NeuronTesting/NeuronAppHostFixture.cs` | Boots `DistributedApplicationTestingBuilder<TAppHost>`, stamps `INO_TEST_MODE=true`, stubs every `ParameterResource`, waits all `ProjectResource`s healthy (telegram excluded), exposes `App` + `KernelGrpcUrl`. |
| `src/Ino.NeuronTesting/NeuronSession.cs` | Per-test handle. `Chat`/`Fire`/`OpenBrowser`/`WaitForRfw`/`WaitForSynapse`. Holds correlationId, userId, frames, observed synapses. `IAsyncDisposable`. |
| `src/Ino.NeuronTesting/ChatFrame.cs` | Value record for one gateway response. `ContentType`, `Reply`, `IsSkeleton`, `Rfw?`. |
| `src/Ino.NeuronTesting/RfwPayload.cs` | Wraps the RFW description (UTF-8 text) + data (JSON). `ContainsWidgets(params string[])` and `DataAt<T>(string jsonPath)` helpers. |
| `src/Ino.NeuronTesting/SynapseFire.cs` | Captured outbound synapse: `Type`, `CorrelationId`, `Args` (dict), `FiredAt`. |
| `src/Ino.NeuronTesting/NeuronPage.cs` | Wraps Playwright `IPage` for the session. `Screenshot()`, `Playwright` escape hatch. `IAsyncDisposable`. |
| `src/Ino.NeuronTesting/Internals/NeuronIdResolver.cs` | Reflects `TNeuron` → matches against `IDomain.DeclaredNeurons` to find the `NeuronId`. Falls back to a `[NeuronId("…")]` attribute on TNeuron. |
| `src/Ino.NeuronTesting/Internals/SynapseObserver.cs` | Registers an `ActivityListener` on the `ino` ActivitySource, captures `ino.neuron.handle` activities tagged with `ino.synapse.type`, exposes `Observed`. |
| `src/Ino.NeuronTesting/Internals/InoGrpcChannelFactory.cs` | Creates an HTTP/2 `GrpcChannel` against the kernel HTTPS endpoint with the dev-cert bypass handler. Removes per-test boilerplate. |
| `src/Ino.NeuronTesting/Internals/PlaywrightLifecycle.cs` | Lazy-initialised singleton-per-fixture `IPlaywright` + `IBrowser`. Headed locally, headless if `CI=true`. |
| `src/Ino.NeuronTesting/Attributes/NeuronIdAttribute.cs` | `[NeuronId("travel.plan-trip")]` opt-in attribute for explicit binding when reflection can't resolve. |

| `src/Ino.NeuronTesting.Bdd/Ino.NeuronTesting.Bdd.csproj` | New assembly. Refs Reqnroll.xUnit.v3, Ino.NeuronTesting. |
| `src/Ino.NeuronTesting.Bdd/NeuronSteps.cs` | Reqnroll `[Binding]` class. Given/When/Then phrases that wrap `NeuronSession`. |
| `src/Ino.NeuronTesting.Bdd/KeyValueParser.cs` | Parses Gherkin step args like `flightId="FL-001", price=180` into `Dictionary<string, string>`. |
| `src/Ino.NeuronTesting.Bdd/ScenarioContextExtensions.cs` | Stash/retrieve `NeuronSession`/`NeuronPage` keyed across steps. |

| `domains/travel/TripPlanner.Smoke/TripPlanner.Smoke.csproj` | Throwaway smoke test project deleted at end of slice 2. Refs Ino.NeuronTesting + Bdd + Projects.Ino_AppHost + Ino.Domains.Travel. |
| `domains/travel/TripPlanner.Smoke/_TravelTestBase.cs` | `TravelNeuronTest<T> : NeuronE2ETest<T, Projects.Ino_AppHost>` — the per-domain intermediate from the spec. |
| `domains/travel/TripPlanner.Smoke/TripPlannerSmokeTests.cs` | One C# `[Fact]` proving `Chat()` works, one Gherkin scenario proving the BDD path works. |
| `domains/travel/TripPlanner.Smoke/trip-planner-smoke.feature` | One scenario: "Bali initial card emits ino.travel.intro" — proves Reqnroll wiring. |

### Modified files (Slice 1)

| Path | Change |
|------|--------|
| `src/Ino.Core.Hosting/Llm/BddMockChatClientFactory.cs` | Add `RegisterCorpusForFixture(string fixtureId, string featureFileText)` and `UnregisterCorpusForFixture(string fixtureId)` alongside the existing static API. New per-fixture corpus dict keyed by `INO_TEST_FIXTURE_ID` env var, falls back to the legacy global corpus when unset. |
| `ino.slnx` | Add the three new csprojs + smoke project. |

### Slice 2+ moves/renames

Spelled out per-task in the slice 2 sections. Bulk-summary:
- `domains/travel/Ino.Domains.Travel/Plans/PlanTripPlan.cs` → `domains/travel/TripPlanner/TripPlanner.cs` (type renamed)
- All other Travel neurons + RFW builders moved into agent-named folders
- `domains/travel/Ino.Domains.Travel.Tests/` → deleted; replaced by `domains/travel/Tests.csproj` globbing `**/*Tests.cs` + `**/*.feature`
- Same pattern for Taxi/Genesis/Location/Recall/Reminders in slice 3
- Slice 4 renames `InoTestAppHost` → `InoPlatformTestAppHost` in new `src/Ino.PlatformTesting/`
- Slice 5 deletes `src/Ino.Testing.E2E/`, slims `src/Ino.Testing/`

---

# Slice 1 — Framework bring-up

**Exit criteria:** All existing 38+ tests still pass. New `TripPlanner.Smoke` test passes both headed and headless. `dotnet build ino.slnx` clean.

## Task 1: Create Ino.NeuronTesting csproj + project graph

**Files:**
- Create: `src/Ino.NeuronTesting/Ino.NeuronTesting.csproj`
- Modify: `ino.slnx`

- [ ] **Step 1: Create the csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>false</IsTestProject>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.Testing" />
    <PackageReference Include="Microsoft.Playwright" />
    <PackageReference Include="Grpc.Net.Client" />
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="xunit.v3.extensibility.core" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Ino.Core\Ino.Core.csproj" />
    <ProjectReference Include="..\Ino.Core.Hosting\Ino.Core.Hosting.csproj" />
    <ProjectReference Include="..\Ino.Kernel.Contracts\Ino.Kernel.Contracts.csproj" />
    <ProjectReference Include="..\Ino.Gateway.Grpc\Ino.Gateway.Grpc.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add to solution**

Run: `dotnet sln ino.slnx add src/Ino.NeuronTesting/Ino.NeuronTesting.csproj`
Expected: solution updated, `dotnet build ino.slnx` still succeeds (empty assembly compiles).

- [ ] **Step 3: Verify package versions match the rest of the repo**

Inspect `Directory.Packages.props` and confirm every `PackageReference` above already has a `<PackageVersion>` entry at root. If `Aspire.Hosting.Testing` isn't declared centrally yet, add it pinned to the same Aspire major as `Directory.Packages.props` already uses (Aspire 13 family).

Use Context7 first: `mcp__context7__resolve-library-id` for "Aspire.Hosting.Testing" then `query-docs` for "DistributedApplicationTestingBuilder current API surface" to confirm the correct package name + entrypoint hasn't changed since the cutoff date.

- [ ] **Step 4: Build clean**

Run: `dotnet build src/Ino.NeuronTesting/Ino.NeuronTesting.csproj`
Expected: 0 errors. Warnings about unreferenced packages are fine — types come in following tasks.

- [ ] **Step 5: Commit**

```bash
git add src/Ino.NeuronTesting/Ino.NeuronTesting.csproj ino.slnx Directory.Packages.props
git commit -m "build(neuron-testing): add Ino.NeuronTesting assembly skeleton"
```

## Task 2: ChatFrame + RfwPayload + SynapseFire value types

**Files:**
- Create: `src/Ino.NeuronTesting/ChatFrame.cs`
- Create: `src/Ino.NeuronTesting/RfwPayload.cs`
- Create: `src/Ino.NeuronTesting/SynapseFire.cs`
- Test: `test/Ino.NeuronTesting.Tests/RfwPayloadTests.cs` (new test project — see step 1)

- [ ] **Step 1: Create the test project for the framework's own unit tests**

```bash
mkdir test/Ino.NeuronTesting.Tests
```

`test/Ino.NeuronTesting.Tests/Ino.NeuronTesting.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Ino.NeuronTesting\Ino.NeuronTesting.csproj" />
  </ItemGroup>
</Project>
```

Add to slnx: `dotnet sln ino.slnx add test/Ino.NeuronTesting.Tests/Ino.NeuronTesting.Tests.csproj`

- [ ] **Step 2: Write the failing test for RfwPayload**

`test/Ino.NeuronTesting.Tests/RfwPayloadTests.cs`:

```csharp
using System.Text;
using FluentAssertions;
using Ino.NeuronTesting;
using Xunit;

namespace Ino.NeuronTesting.Tests;

public class RfwPayloadTests
{
    [Fact]
    public void ContainsWidgets_returns_true_when_all_named_widgets_appear_in_description()
    {
        var dsl = """
            import ino.weather;
            import ino.flights;
            widget root = Column(children: [
              WeatherSummaryCard(season: data.season),
              FlightCard(airline: data.flights.0.airline),
            ]);
            """;
        var payload = RfwPayload.FromBytes(
            Encoding.UTF8.GetBytes(dsl),
            Encoding.UTF8.GetBytes("""{"season":"dry","flights":[{"airline":"ANA"}]}"""));

        payload.ContainsWidgets("WeatherSummaryCard", "FlightCard").Should().BeTrue();
        payload.ContainsWidgets("HotelCard").Should().BeFalse();
    }

    [Fact]
    public void DataAt_returns_value_at_simple_dotted_path()
    {
        var payload = RfwPayload.FromBytes(
            Encoding.UTF8.GetBytes("widget root = Container();"),
            Encoding.UTF8.GetBytes("""{"flights":[{"airline":"ANA","price":1180}]}"""));

        payload.DataAt<string>("flights.0.airline").Should().Be("ANA");
        payload.DataAt<int>("flights.0.price").Should().Be(1180);
        payload.DataAt<string>("flights.0.missing").Should().BeNull();
    }
}
```

- [ ] **Step 3: Run the test to verify it fails (no type yet)**

Run: `dotnet test test/Ino.NeuronTesting.Tests --no-restore`
Expected: build error, `RfwPayload` not found.

- [ ] **Step 4: Implement ChatFrame**

`src/Ino.NeuronTesting/ChatFrame.cs`:

```csharp
namespace Ino.NeuronTesting;

public sealed record ChatFrame(
    string Reply,
    string ContentType,
    bool IsSkeleton,
    string CorrelationId,
    RfwPayload? Rfw);
```

- [ ] **Step 5: Implement SynapseFire**

`src/Ino.NeuronTesting/SynapseFire.cs`:

```csharp
namespace Ino.NeuronTesting;

public sealed record SynapseFire(
    string Type,
    string CorrelationId,
    IReadOnlyDictionary<string, string> Args,
    DateTimeOffset FiredAt);
```

- [ ] **Step 6: Implement RfwPayload**

`src/Ino.NeuronTesting/RfwPayload.cs`:

```csharp
using System.Text;
using System.Text.Json;

namespace Ino.NeuronTesting;

public sealed class RfwPayload
{
    RfwPayload(string description, JsonElement data)
    {
        Description = description;
        Data = data;
    }

    public string Description { get; }
    public JsonElement Data { get; }

    public static RfwPayload FromBytes(ReadOnlySpan<byte> descriptionBytes, ReadOnlySpan<byte> dataBytes)
    {
        var description = Encoding.UTF8.GetString(descriptionBytes);
        using var doc = JsonDocument.Parse(dataBytes.ToArray());
        return new RfwPayload(description, doc.RootElement.Clone());
    }

    public bool ContainsWidgets(params string[] widgetNames)
    {
        foreach (var name in widgetNames)
            if (!Description.Contains(name, StringComparison.Ordinal)) return false;
        return true;
    }

    public T? DataAt<T>(string dottedPath)
    {
        var current = Data;
        foreach (var segment in dottedPath.Split('.'))
        {
            if (current.ValueKind == JsonValueKind.Object)
            {
                if (!current.TryGetProperty(segment, out var next)) return default;
                current = next;
            }
            else if (current.ValueKind == JsonValueKind.Array && int.TryParse(segment, out var index))
            {
                if (index < 0 || index >= current.GetArrayLength()) return default;
                current = current[index];
            }
            else return default;
        }
        return current.ValueKind == JsonValueKind.Null ? default : current.Deserialize<T>();
    }
}
```

- [ ] **Step 7: Run the test, verify pass**

Run: `dotnet test test/Ino.NeuronTesting.Tests --no-restore`
Expected: 2 passed, 0 failed.

- [ ] **Step 8: Commit**

```bash
git add src/Ino.NeuronTesting/ChatFrame.cs src/Ino.NeuronTesting/RfwPayload.cs src/Ino.NeuronTesting/SynapseFire.cs test/Ino.NeuronTesting.Tests/ ino.slnx
git commit -m "feat(neuron-testing): ChatFrame + RfwPayload + SynapseFire value types"
```

## Task 3: NeuronIdAttribute + NeuronIdResolver

**Files:**
- Create: `src/Ino.NeuronTesting/Attributes/NeuronIdAttribute.cs`
- Create: `src/Ino.NeuronTesting/Internals/NeuronIdResolver.cs`
- Test: `test/Ino.NeuronTesting.Tests/NeuronIdResolverTests.cs`

- [ ] **Step 1: Write the failing tests**

`test/Ino.NeuronTesting.Tests/NeuronIdResolverTests.cs`:

```csharp
using FluentAssertions;
using Ino.Core;
using Ino.NeuronTesting;
using Ino.NeuronTesting.Attributes;
using Ino.NeuronTesting.Internals;
using Xunit;

namespace Ino.NeuronTesting.Tests;

public class NeuronIdResolverTests
{
    [Fact]
    public void Resolves_from_NeuronIdAttribute_when_present()
    {
        var id = NeuronIdResolver.Resolve(typeof(WithAttribute));
        id.Value.Should().Be("test.with-attribute");
    }

    [Fact]
    public void Throws_when_no_attribute_and_no_domain_match()
    {
        Action act = () => NeuronIdResolver.Resolve(typeof(WithoutAnything));
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no NeuronId*");
    }

    [NeuronId("test.with-attribute")]
    sealed class WithAttribute { }

    sealed class WithoutAnything { }
}
```

- [ ] **Step 2: Run, verify fail**

Run: `dotnet test test/Ino.NeuronTesting.Tests --no-restore`
Expected: build errors — types missing.

- [ ] **Step 3: Implement the attribute**

`src/Ino.NeuronTesting/Attributes/NeuronIdAttribute.cs`:

```csharp
namespace Ino.NeuronTesting.Attributes;

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class NeuronIdAttribute : Attribute
{
    public NeuronIdAttribute(string value) => Value = value;
    public string Value { get; }
}
```

- [ ] **Step 4: Implement the resolver**

`src/Ino.NeuronTesting/Internals/NeuronIdResolver.cs`:

```csharp
using System.Reflection;
using Ino.Core;
using Ino.NeuronTesting.Attributes;

namespace Ino.NeuronTesting.Internals;

public static class NeuronIdResolver
{
    public static NeuronId Resolve(Type neuronType)
    {
        var attr = neuronType.GetCustomAttribute<NeuronIdAttribute>();
        if (attr is not null) return NeuronId.From(attr.Value);

        // Walk the AppDomain's IDomain implementations and find one whose
        // DeclaredNeurons[i].PlanType (or implementation type) is assignable
        // from neuronType.
        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = asm.GetTypes(); } catch (ReflectionTypeLoadException ex) { types = ex.Types.OfType<Type>().ToArray(); }
            foreach (var t in types.Where(t => !t.IsAbstract && typeof(IDomain).IsAssignableFrom(t)))
            {
                IDomain? domain = null;
                try { domain = (IDomain?)Activator.CreateInstance(t); } catch { /* domain may have ctor deps; skip */ }
                if (domain is null) continue;
                foreach (var n in domain.DeclaredNeurons)
                {
                    if (n.PlanType is not null && n.PlanType.IsAssignableFrom(neuronType))
                        return n.Id;
                    if (n.CanonicalSynapseType is not null &&
                        neuronType.GetInterfaces().Any(i => i.IsGenericType &&
                            i.GetGenericTypeDefinition().Name.StartsWith("INeuron`") &&
                            i.GetGenericArguments()[0] == n.CanonicalSynapseType))
                        return n.Id;
                }
            }
        }

        throw new InvalidOperationException(
            $"Could not resolve NeuronId for {neuronType.FullName}: no [NeuronId] attribute and no matching IDomain.DeclaredNeurons entry.");
    }
}
```

- [ ] **Step 5: Run, verify pass**

Run: `dotnet test test/Ino.NeuronTesting.Tests --no-restore`
Expected: 4 passed, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/Ino.NeuronTesting/Attributes/ src/Ino.NeuronTesting/Internals/NeuronIdResolver.cs test/Ino.NeuronTesting.Tests/NeuronIdResolverTests.cs
git commit -m "feat(neuron-testing): NeuronIdResolver with attribute + IDomain registry fallback"
```

## Task 4: SynapseObserver via ActivityListener

**Files:**
- Create: `src/Ino.NeuronTesting/Internals/SynapseObserver.cs`
- Test: `test/Ino.NeuronTesting.Tests/SynapseObserverTests.cs`

- [ ] **Step 1: Inspect existing neurons to confirm the activity tag schema**

Run: `grep -rn "ino.synapse" src/ domains/ --include="*.cs"`

Confirm the tag names actually used (the spec mentions `ino.synapse.type`). If the codebase emits different tags, adapt the observer's predicate. Document the actual schema in a comment on `SynapseObserver`.

Concretely from `domains/travel/Ino.Domains.Travel/Neurons/TripPlannerNeuron.cs`:
```csharp
static readonly ActivitySource ActivitySource = new("ino");
span?.SetTag("ino.neuron.type", nameof(TripPlannerNeuron));
span?.SetTag("ino.synapse.type", nameof(PlanTripRequest));
span?.SetTag("ino.correlation_id", ctx.CorrelationId.Value);
```

So the observer filters Activities on `Source.Name == "ino"`, `OperationName == "ino.neuron.handle"`, and reads tags `ino.synapse.type` + `ino.correlation_id`.

- [ ] **Step 2: Write the failing test (use an Activity emitted from the test itself to prove capture)**

`test/Ino.NeuronTesting.Tests/SynapseObserverTests.cs`:

```csharp
using System.Diagnostics;
using FluentAssertions;
using Ino.NeuronTesting.Internals;
using Xunit;

namespace Ino.NeuronTesting.Tests;

public class SynapseObserverTests
{
    static readonly ActivitySource _testSource = new("ino");

    [Fact]
    public void Captures_synapse_handle_activities_with_matching_correlation_id()
    {
        using var observer = new SynapseObserver(correlationId: "corr-1");

        using (var act = _testSource.StartActivity("ino.neuron.handle"))
        {
            act?.SetTag("ino.synapse.type", "PlanTripRequest");
            act?.SetTag("ino.correlation_id", "corr-1");
        }

        using (var act = _testSource.StartActivity("ino.neuron.handle"))
        {
            act?.SetTag("ino.synapse.type", "FindFlightsRequest");
            act?.SetTag("ino.correlation_id", "other-corr");
        }

        observer.Observed.Should().HaveCount(1);
        observer.Observed[0].Type.Should().Be("PlanTripRequest");
        observer.Observed[0].CorrelationId.Should().Be("corr-1");
    }
}
```

- [ ] **Step 3: Run, verify fail**

Run: `dotnet test test/Ino.NeuronTesting.Tests --no-restore`
Expected: build error.

- [ ] **Step 4: Implement SynapseObserver**

`src/Ino.NeuronTesting/Internals/SynapseObserver.cs`:

```csharp
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Ino.NeuronTesting.Internals;

/// <summary>
/// Captures `ino.neuron.handle` activities matching one correlation_id.
/// Reads tags `ino.synapse.type` + `ino.correlation_id`. Disposable —
/// removes its ActivityListener on teardown.
/// </summary>
public sealed class SynapseObserver : IDisposable
{
    readonly string _correlationId;
    readonly ConcurrentBag<SynapseFire> _captured = [];
    readonly ActivityListener _listener;

    public SynapseObserver(string correlationId)
    {
        _correlationId = correlationId;
        _listener = new ActivityListener
        {
            ShouldListenTo = src => src.Name == "ino",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = OnStopped,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyList<SynapseFire> Observed => _captured.ToArray();

    void OnStopped(Activity activity)
    {
        if (activity.OperationName != "ino.neuron.handle") return;
        var corr = activity.GetTagItem("ino.correlation_id") as string;
        if (corr != _correlationId) return;
        var type = activity.GetTagItem("ino.synapse.type") as string ?? "(unknown)";
        var args = activity.Tags
            .Where(kv => kv.Key.StartsWith("ino.synapse.arg.", StringComparison.Ordinal))
            .ToDictionary(kv => kv.Key["ino.synapse.arg.".Length..], kv => kv.Value ?? "");
        _captured.Add(new SynapseFire(type, corr, args, DateTimeOffset.UtcNow));
    }

    public void Dispose() => _listener.Dispose();
}
```

- [ ] **Step 5: Run, verify pass**

Run: `dotnet test test/Ino.NeuronTesting.Tests --no-restore`
Expected: 5 passed, 0 failed.

- [ ] **Step 6: Commit**

```bash
git add src/Ino.NeuronTesting/Internals/SynapseObserver.cs test/Ino.NeuronTesting.Tests/SynapseObserverTests.cs
git commit -m "feat(neuron-testing): SynapseObserver via ActivityListener"
```

## Task 5: InoGrpcChannelFactory

**Files:**
- Create: `src/Ino.NeuronTesting/Internals/InoGrpcChannelFactory.cs`

- [ ] **Step 1: Implement (no test — pure passthrough; covered by integration tests later)**

`src/Ino.NeuronTesting/Internals/InoGrpcChannelFactory.cs`:

```csharp
using Grpc.Net.Client;

namespace Ino.NeuronTesting.Internals;

/// <summary>
/// Centralises the kernel HTTPS gRPC channel creation. The Aspire dev silo
/// serves HTTP/2 over a self-signed cert; tests connect server-to-server
/// via plain HTTP/2 gRPC and must opt out of cert validation.
/// </summary>
public static class InoGrpcChannelFactory
{
    public static GrpcChannel ForKernel(string kernelHttpsUrl) =>
        GrpcChannel.ForAddress(kernelHttpsUrl, new GrpcChannelOptions
        {
            HttpHandler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback =
                    HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
            },
        });
}
```

- [ ] **Step 2: Build clean, commit**

Run: `dotnet build src/Ino.NeuronTesting`
Expected: 0 errors.

```bash
git add src/Ino.NeuronTesting/Internals/InoGrpcChannelFactory.cs
git commit -m "feat(neuron-testing): InoGrpcChannelFactory for HTTPS kernel channel"
```

## Task 6: NeuronAppHostFixture

**Files:**
- Create: `src/Ino.NeuronTesting/NeuronAppHostFixture.cs`

- [ ] **Step 1: Use Context7 to confirm DistributedApplicationTestingBuilder + ResourceNotifications API hasn't shifted in Aspire 13.x**

Run via tool: `mcp__context7__query-docs` library `/dotnet/aspire` query "DistributedApplicationTestingBuilder.CreateAsync ResourceNotifications.WaitForResourceHealthyAsync ProjectResource enumeration".

Note any API drift in this task before implementing. Update code below if signatures differ.

- [ ] **Step 2: Implement the fixture**

`src/Ino.NeuronTesting/NeuronAppHostFixture.cs`:

```csharp
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace Ino.NeuronTesting;

/// <summary>
/// Boots the production AppHost via DistributedApplicationTestingBuilder.
/// Per-test-class scope (xUnit IAsyncLifetime). Discovers ProjectResources
/// from the AppHost graph — adding a domain in Ino.AppHost requires zero
/// changes here. Telegram is skipped because it depends on a cloudflared
/// tunnel that's unhealthy in tests.
/// </summary>
public sealed class NeuronAppHostFixture<TAppHost> : IAsyncLifetime
    where TAppHost : class
{
    public DistributedApplication App { get; private set; } = null!;
    public string FixtureId { get; } = Guid.NewGuid().ToString("N");
    public string KernelGrpcUrl { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Environment.SetEnvironmentVariable("INO_TEST_MODE", "true");
        Environment.SetEnvironmentVariable("INO_TEST_FIXTURE_ID", FixtureId);

        var builder = await DistributedApplicationTestingBuilder.CreateAsync<TAppHost>();

        foreach (var p in builder.Resources.OfType<ParameterResource>())
            builder.Configuration[$"Parameters:{p.Name}"] = "test";

        App = await builder.BuildAsync();
        await App.StartAsync();

        var siloResources = builder.Resources
            .OfType<ProjectResource>()
            .Where(r => !r.Name.StartsWith("telegram", StringComparison.OrdinalIgnoreCase))
            .Select(r => r.Name)
            .ToArray();

        await Task.WhenAll(siloResources.Select(name =>
            App.ResourceNotifications.WaitForResourceHealthyAsync(name).AsTask()));

        KernelGrpcUrl = App.GetEndpoint("kernel", "https").ToString();
    }

    public async ValueTask DisposeAsync()
    {
        try { await App.DisposeAsync(); }
        finally
        {
            Environment.SetEnvironmentVariable("INO_TEST_MODE", null);
            Environment.SetEnvironmentVariable("INO_TEST_FIXTURE_ID", null);
        }
    }
}
```

- [ ] **Step 3: Build, commit**

Run: `dotnet build src/Ino.NeuronTesting`
Expected: 0 errors.

```bash
git add src/Ino.NeuronTesting/NeuronAppHostFixture.cs
git commit -m "feat(neuron-testing): NeuronAppHostFixture boots Aspire AppHost per test class"
```

## Task 7: PlaywrightLifecycle (lazy)

**Files:**
- Create: `src/Ino.NeuronTesting/Internals/PlaywrightLifecycle.cs`

- [ ] **Step 1: Confirm Playwright init pattern via Context7**

`mcp__context7__query-docs` library `/microsoft/playwright-dotnet` query "Playwright.CreateAsync browser launch headless option new context ignore https errors".

- [ ] **Step 2: Implement**

`src/Ino.NeuronTesting/Internals/PlaywrightLifecycle.cs`:

```csharp
using Microsoft.Playwright;

namespace Ino.NeuronTesting.Internals;

/// <summary>
/// Singleton-per-fixture Playwright + Browser. Lazily created on first
/// session.OpenBrowser() call so test classes that never open a browser
/// pay zero Chromium cost. Headed locally; headless if CI=true (auto-set
/// by every standard CI runner).
/// </summary>
public sealed class PlaywrightLifecycle : IAsyncDisposable
{
    IPlaywright? _playwright;
    IBrowser? _browser;
    readonly SemaphoreSlim _gate = new(1, 1);

    public async ValueTask<IBrowserContext> NewContextAsync()
    {
        await _gate.WaitAsync();
        try
        {
            _playwright ??= await Playwright.CreateAsync();
            _browser ??= await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = string.Equals(Environment.GetEnvironmentVariable("CI"),
                    "true", StringComparison.OrdinalIgnoreCase),
            });
        }
        finally { _gate.Release(); }

        return await _browser.NewContextAsync(new BrowserNewContextOptions
        {
            IgnoreHTTPSErrors = true,
        });
    }

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
    }
}
```

- [ ] **Step 3: Build, commit**

```bash
git add src/Ino.NeuronTesting/Internals/PlaywrightLifecycle.cs
git commit -m "feat(neuron-testing): PlaywrightLifecycle lazy browser per fixture"
```

## Task 8: NeuronPage

**Files:**
- Create: `src/Ino.NeuronTesting/NeuronPage.cs`

- [ ] **Step 1: Implement**

`src/Ino.NeuronTesting/NeuronPage.cs`:

```csharp
using Microsoft.Playwright;

namespace Ino.NeuronTesting;

public sealed class NeuronPage : IAsyncDisposable
{
    readonly IBrowserContext _context;

    internal NeuronPage(IBrowserContext context, IPage playwright)
    {
        _context = context;
        Playwright = playwright;
    }

    /// <summary>Escape hatch for Playwright APIs the wrapper hasn't surfaced.</summary>
    public IPage Playwright { get; }

    public Task<byte[]> Screenshot() => Playwright.ScreenshotAsync(new() { FullPage = true });

    public async ValueTask DisposeAsync()
    {
        await Playwright.CloseAsync();
        await _context.CloseAsync();
    }
}
```

- [ ] **Step 2: Build, commit**

```bash
git add src/Ino.NeuronTesting/NeuronPage.cs
git commit -m "feat(neuron-testing): NeuronPage wraps Playwright IPage"
```

## Task 9: NeuronSession (the heart)

**Files:**
- Create: `src/Ino.NeuronTesting/NeuronSession.cs`

This is the largest task — break the steps in your head if helpful, but the code is one file.

- [ ] **Step 1: Inspect the gRPC contract surface**

Run: `grep -n "rpc Chat\|rpc RfwEvent\|message ChatRequest\|message RfwEventRequest" src/Ino.Kernel.Contracts/`

Confirm the actual proto field names (`message`, `user_id`, `correlation_id`, `event_name`, `args` map). The code below uses today's contract from `domains/travel/Ino.Domains.Travel.Tests/RichTripPlanningE2ETests.cs:128-148`. If the contract has shifted, adapt.

- [ ] **Step 2: Implement**

`src/Ino.NeuronTesting/NeuronSession.cs`:

```csharp
using System.Text;
using Grpc.Core;
using Ino.Grpc;
using Ino.NeuronTesting.Internals;
using Microsoft.Playwright;

namespace Ino.NeuronTesting;

public sealed class NeuronSession : IAsyncDisposable
{
    readonly Ino.InoClient _client;
    readonly PlaywrightLifecycle _playwright;
    readonly SynapseObserver _observer;
    readonly List<ChatFrame> _frames = [];
    readonly List<NeuronPage> _pages = [];

    string _correlationId = string.Empty;

    internal NeuronSession(Ino.InoClient client, PlaywrightLifecycle playwright, string userId)
    {
        _client = client;
        _playwright = playwright;
        UserId = userId;
        _observer = new SynapseObserver(correlationId: "");  // re-bound after first frame
    }

    public string UserId { get; }
    public string CorrelationId => _correlationId;
    public IReadOnlyList<ChatFrame> Frames => _frames;
    public ChatFrame Last => _frames.LastOrDefault(f => !f.IsSkeleton)
        ?? throw new InvalidOperationException("No non-skeleton frame received yet.");
    public IReadOnlyList<SynapseFire> Observed => _observer.Observed;

    public async Task<ChatFrame> Chat(string prompt, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(30));
        var call = _client.Chat(new ChatRequest { Message = prompt, UserId = UserId });

        ChatFrame? final = null;
        await foreach (var resp in call.ResponseStream.ReadAllAsync(LinkedCt(deadline)))
        {
            if (!string.IsNullOrEmpty(resp.CorrelationId)) _correlationId = resp.CorrelationId;
            var frame = ToFrame(resp);
            _frames.Add(frame);
            if (!frame.IsSkeleton) final = frame;
        }
        return final ?? throw new InvalidOperationException(
            $"Chat({prompt}) closed without a non-skeleton frame.");
    }

    public async Task<ChatFrame> Fire(string eventName, IReadOnlyDictionary<string, string> args)
    {
        var req = new RfwEventRequest { CorrelationId = _correlationId, EventName = eventName };
        foreach (var kv in args) req.Args[kv.Key] = kv.Value;
        var resp = await _client.RfwEventAsync(req);
        if (!resp.Accepted)
            throw new InvalidOperationException(
                $"RfwEvent({eventName}) rejected — reply: {resp.Reply}");
        var frame = ToFrame(resp);
        _frames.Add(frame);
        return frame;
    }

    public Task<ChatFrame> Fire(string eventName, object args) =>
        Fire(eventName, ReflectArgs(args));

    public async Task<NeuronPage> OpenBrowser(string kernelHttpsUrl, string? prompt = null)
    {
        var ctx = await _playwright.NewContextAsync();
        var page = await ctx.NewPageAsync();
        var url = prompt is null
            ? kernelHttpsUrl
            : $"{kernelHttpsUrl}?q={Uri.EscapeDataString(prompt)}";
        await page.GotoAsync(url, new PageGotoOptions { WaitUntil = WaitUntilState.Load });
        var wrapper = new NeuronPage(ctx, page);
        _pages.Add(wrapper);
        return wrapper;
    }

    public async Task<ChatFrame> WaitForRfw(string contentType, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < deadline)
        {
            var match = _frames.LastOrDefault(f =>
                !f.IsSkeleton &&
                f.ContentType.Contains(contentType, StringComparison.Ordinal));
            if (match is not null) return match;
            await Task.Delay(100);
        }
        throw new TimeoutException(
            $"No frame with content_type containing '{contentType}' within timeout. " +
            $"Saw: {string.Join(", ", _frames.Where(f => !f.IsSkeleton).Select(f => f.ContentType))}");
    }

    public async Task<SynapseFire> WaitForSynapse(string synapseType, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (DateTime.UtcNow < deadline)
        {
            var match = _observer.Observed.FirstOrDefault(s => s.Type == synapseType);
            if (match is not null) return match;
            await Task.Delay(100);
        }
        throw new TimeoutException(
            $"No synapse '{synapseType}' fired within timeout. " +
            $"Saw: {string.Join(", ", _observer.Observed.Select(s => s.Type))}");
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var p in _pages) await p.DisposeAsync();
        _observer.Dispose();
    }

    static ChatFrame ToFrame(ChatResponse r) => new(
        Reply: r.Reply,
        ContentType: r.ContentType,
        IsSkeleton: r.IsSkeleton,
        CorrelationId: r.CorrelationId,
        Rfw: r.RfwDescription.Length == 0 ? null
            : RfwPayload.FromBytes(r.RfwDescription.Span, r.RfwData.Span));

    static ChatFrame ToFrame(RfwEventResponse r) => new(
        Reply: r.Reply,
        ContentType: r.ContentType,
        IsSkeleton: false,
        CorrelationId: r.CorrelationId,
        Rfw: r.RfwDescription.Length == 0 ? null
            : RfwPayload.FromBytes(r.RfwDescription.Span, r.RfwData.Span));

    static IReadOnlyDictionary<string, string> ReflectArgs(object args)
    {
        var dict = new Dictionary<string, string>();
        foreach (var prop in args.GetType().GetProperties())
        {
            var val = prop.GetValue(args);
            dict[prop.Name] = val?.ToString() ?? "";
        }
        return dict;
    }

    static CancellationToken LinkedCt(DateTime deadline)
    {
        var cts = new CancellationTokenSource();
        cts.CancelAfter(deadline - DateTime.UtcNow);
        return cts.Token;
    }
}
```

- [ ] **Step 3: Build clean, commit**

```bash
git add src/Ino.NeuronTesting/NeuronSession.cs
git commit -m "feat(neuron-testing): NeuronSession — Chat/Fire/OpenBrowser/WaitFor*"
```

## Task 10: NeuronE2ETest base class

**Files:**
- Create: `src/Ino.NeuronTesting/NeuronE2ETest.cs`

- [ ] **Step 1: Implement**

`src/Ino.NeuronTesting/NeuronE2ETest.cs`:

```csharp
using Ino.Core;
using Ino.Grpc;
using Ino.NeuronTesting.Internals;
using Xunit;

namespace Ino.NeuronTesting;

/// <summary>
/// Base for neuron end-to-end tests. Generic on the neuron impl AND the
/// AppHost project. Per-domain test projects typically narrow with an
/// intermediate base (e.g. TravelNeuronTest&lt;T&gt;) so per-neuron test
/// classes are one line.
/// </summary>
public abstract class NeuronE2ETest<TNeuron, TAppHost> : IAsyncLifetime
    where TNeuron : class
    where TAppHost : class
{
    NeuronAppHostFixture<TAppHost>? _fixture;
    PlaywrightLifecycle? _playwright;
    Grpc.Net.Client.GrpcChannel? _channel;
    Ino.InoClient? _client;

    protected NeuronAppHostFixture<TAppHost> Fixture =>
        _fixture ?? throw new InvalidOperationException("Fixture not initialised");
    protected DistributedApplication App => Fixture.App;
    protected string KernelGrpcUrl => Fixture.KernelGrpcUrl;
    protected NeuronId NeuronUnderTest { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        NeuronUnderTest = NeuronIdResolver.Resolve(typeof(TNeuron));
        _fixture = new NeuronAppHostFixture<TAppHost>();
        await _fixture.InitializeAsync();
        _playwright = new PlaywrightLifecycle();
        _channel = InoGrpcChannelFactory.ForKernel(KernelGrpcUrl);
        _client = new Ino.InoClient(_channel);
    }

    /// <summary>Open a fresh session with a unique user id.</summary>
    protected NeuronSession Open(string? userId = null) =>
        new(_client!, _playwright!,
            userId: userId ?? $"{NeuronUnderTest.Value}-{Guid.NewGuid():N}");

    /// <summary>Sugar: open session and immediately Chat the prompt.</summary>
    protected async Task<NeuronSession> Chat(string prompt)
    {
        var s = Open();
        await s.Chat(prompt);
        return s;
    }

    public async ValueTask DisposeAsync()
    {
        if (_playwright is not null) await _playwright.DisposeAsync();
        _channel?.Dispose();
        if (_fixture is not null) await _fixture.DisposeAsync();
    }
}
```

- [ ] **Step 2: Build, commit**

```bash
git add src/Ino.NeuronTesting/NeuronE2ETest.cs
git commit -m "feat(neuron-testing): NeuronE2ETest<TNeuron, TAppHost> base"
```

## Task 11: BddMockChatClientFactory per-fixture corpus

**Files:**
- Modify: `src/Ino.Core.Hosting/Llm/BddMockChatClientFactory.cs`
- Test: `test/Ino.Core.Hosting.Tests/BddMockChatClientFactoryFixtureScopeTests.cs` (new)

- [ ] **Step 1: Read the current factory to understand the static API**

Run: `cat src/Ino.Core.Hosting/Llm/BddMockChatClientFactory.cs`

Note the current corpus storage shape (presumably a `static List<BddScenario> _scenarios`). The change is additive: add a per-fixture dictionary keyed by `FixtureId`, fall back to global when unset.

- [ ] **Step 2: Write the failing test**

`test/Ino.Core.Hosting.Tests/BddMockChatClientFactoryFixtureScopeTests.cs`:

```csharp
using FluentAssertions;
using Ino.Core.Hosting.Llm;
using Xunit;

namespace Ino.Core.Hosting.Tests;

public class BddMockChatClientFactoryFixtureScopeTests
{
    [Fact]
    public void Per_fixture_corpus_is_isolated_from_other_fixtures()
    {
        var feature1 = """
            Feature: F1
              Scenario: A
                Given the user says "alpha"
                Then the assistant replies "from-fixture-1"
            """;
        var feature2 = """
            Feature: F2
              Scenario: A
                Given the user says "alpha"
                Then the assistant replies "from-fixture-2"
            """;

        BddMockChatClientFactory.RegisterCorpusForFixture("fix-1", feature1);
        BddMockChatClientFactory.RegisterCorpusForFixture("fix-2", feature2);

        try
        {
            BddMockChatClientFactory.LookupReply("fix-1", "alpha").Should().Be("from-fixture-1");
            BddMockChatClientFactory.LookupReply("fix-2", "alpha").Should().Be("from-fixture-2");
            BddMockChatClientFactory.LookupReply("fix-3", "alpha").Should().BeNull(
                because: "no fixture registered for fix-3 and no global fallback");
        }
        finally
        {
            BddMockChatClientFactory.UnregisterCorpusForFixture("fix-1");
            BddMockChatClientFactory.UnregisterCorpusForFixture("fix-2");
        }
    }
}
```

- [ ] **Step 3: Run, verify fail (methods don't exist)**

Run: `dotnet test test/Ino.Core.Hosting.Tests --filter "FullyQualifiedName~FixtureScope"`
Expected: build error.

- [ ] **Step 4: Add the per-fixture API alongside the existing static corpus**

In `src/Ino.Core.Hosting/Llm/BddMockChatClientFactory.cs`:

Add a new static dictionary and three methods. Do NOT alter existing methods — they remain the global fallback.

```csharp
static readonly System.Collections.Concurrent.ConcurrentDictionary<string, IReadOnlyList<BddScenario>> _perFixtureCorpus = new();

public static void RegisterCorpusForFixture(string fixtureId, string featureFileText)
{
    var scenarios = BddFeatureParser.Parse(featureFileText);
    _perFixtureCorpus[fixtureId] = scenarios;
}

public static void UnregisterCorpusForFixture(string fixtureId) =>
    _perFixtureCorpus.TryRemove(fixtureId, out _);

public static string? LookupReply(string fixtureId, string prompt)
{
    if (!_perFixtureCorpus.TryGetValue(fixtureId, out var corpus)) return null;
    foreach (var s in corpus)
        if (System.Text.RegularExpressions.Regex.IsMatch(prompt, s.Pattern))
            return s.Reply;
    return null;
}
```

When constructing a `BddMockChatClient` inside the factory's existing `IChatClientFactory` impl, prefer the per-fixture corpus when `INO_TEST_FIXTURE_ID` env var is set:

```csharp
var fixtureId = Environment.GetEnvironmentVariable("INO_TEST_FIXTURE_ID");
var scenarios = (fixtureId is not null && _perFixtureCorpus.TryGetValue(fixtureId, out var fix))
    ? fix
    : _globalScenarios;   // existing fallback
return new BddMockChatClient(scenarios, ...);
```

If `BddFeatureParser` doesn't already exist, factor the existing parsing logic out of the current factory into one. Don't duplicate parsing code.

- [ ] **Step 5: Run the new test, verify pass; also run the full Ino.Core.Hosting.Tests to ensure no regression**

Run: `dotnet test test/Ino.Core.Hosting.Tests`
Expected: previous tests + 1 new = all pass.

- [ ] **Step 6: Commit**

```bash
git add src/Ino.Core.Hosting/Llm/BddMockChatClientFactory.cs test/Ino.Core.Hosting.Tests/BddMockChatClientFactoryFixtureScopeTests.cs
git commit -m "feat(bdd-mock): per-fixture corpus scoping via INO_TEST_FIXTURE_ID"
```

## Task 12: Ino.NeuronTesting.Bdd csproj + step library

**Files:**
- Create: `src/Ino.NeuronTesting.Bdd/Ino.NeuronTesting.Bdd.csproj`
- Create: `src/Ino.NeuronTesting.Bdd/KeyValueParser.cs`
- Create: `src/Ino.NeuronTesting.Bdd/NeuronSteps.cs`
- Test: `test/Ino.NeuronTesting.Tests/KeyValueParserTests.cs`

- [ ] **Step 1: Create the csproj + slnx entry**

`src/Ino.NeuronTesting.Bdd/Ino.NeuronTesting.Bdd.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Reqnroll.xUnit" />
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Ino.NeuronTesting\Ino.NeuronTesting.csproj" />
  </ItemGroup>
</Project>
```

Run: `dotnet sln ino.slnx add src/Ino.NeuronTesting.Bdd/Ino.NeuronTesting.Bdd.csproj`

Use Context7: `mcp__context7__query-docs` library `/reqnroll/reqnroll` query "xUnit v3 binding registration scenario context dependency injection".

- [ ] **Step 2: Write the failing KeyValueParser test**

`test/Ino.NeuronTesting.Tests/KeyValueParserTests.cs`:

```csharp
using FluentAssertions;
using Ino.NeuronTesting.Bdd;
using Xunit;

namespace Ino.NeuronTesting.Tests;

public class KeyValueParserTests
{
    [Theory]
    [InlineData("flightId=\"FL-001\"", "flightId", "FL-001")]
    [InlineData("flightId=FL-001", "flightId", "FL-001")]
    [InlineData("rainProbability=0.85", "rainProbability", "0.85")]
    public void Parses_single_kv(string input, string expectedKey, string expectedValue)
    {
        var dict = KeyValueParser.Parse(input);
        dict.Should().ContainKey(expectedKey).WhoseValue.Should().Be(expectedValue);
    }

    [Fact]
    public void Parses_multiple_kv_separated_by_commas()
    {
        var dict = KeyValueParser.Parse("flightId=\"FL-001\", price=180, airline=\"ANA\"");
        dict.Should().HaveCount(3);
        dict["flightId"].Should().Be("FL-001");
        dict["price"].Should().Be("180");
        dict["airline"].Should().Be("ANA");
    }
}
```

Add a project reference from `Ino.NeuronTesting.Tests` to `Ino.NeuronTesting.Bdd`.

- [ ] **Step 3: Run, verify fail**

Run: `dotnet test test/Ino.NeuronTesting.Tests`
Expected: build error.

- [ ] **Step 4: Implement KeyValueParser**

`src/Ino.NeuronTesting.Bdd/KeyValueParser.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Ino.NeuronTesting.Bdd;

public static class KeyValueParser
{
    static readonly Regex _kvRegex = new("""(\w+)=(?:"([^"]*)"|([^,\s]+))""", RegexOptions.Compiled);

    public static IReadOnlyDictionary<string, string> Parse(string input)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in _kvRegex.Matches(input))
        {
            var key = m.Groups[1].Value;
            var value = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            dict[key] = value;
        }
        return dict;
    }
}
```

- [ ] **Step 5: Implement NeuronSteps**

`src/Ino.NeuronTesting.Bdd/NeuronSteps.cs`:

```csharp
using FluentAssertions;
using Ino.NeuronTesting;
using Reqnroll;

namespace Ino.NeuronTesting.Bdd;

[Binding]
public sealed class NeuronSteps
{
    readonly NeuronSession _s;
    readonly ScenarioContext _ctx;

    public NeuronSteps(NeuronSession session, ScenarioContext ctx)
    {
        _s = session;
        _ctx = ctx;
    }

    [Given(@"the user says ""(.*)"""), When(@"the user says ""(.*)""")]
    public Task UserSays(string prompt) => _s.Chat(prompt);

    [Given(@"the user said ""(.*)""")]
    public Task UserSaid(string prompt) => _s.Chat(prompt);

    [When(@"the user fires ""([^""]+)"" with (.+)")]
    [Given(@"the user fired ""([^""]+)"" with (.+)")]
    public Task UserFires(string eventName, string args) =>
        _s.Fire(eventName, KeyValueParser.Parse(args));

    [When(@"a ""(\w+)"" synapse arrives with (.+)")]
    public Task SynapseArrives(string synapseType, string args) =>
        _s.Fire(synapseType, KeyValueParser.Parse(args));

    [When(@"the user opens the chat in a browser")]
    public async Task OpensBrowser()
    {
        var page = await _s.OpenBrowser(_ctx.Get<string>("kernelHttpsUrl"));
        _ctx.Set(page, "page");
    }

    [Then(@"the user sees a card with content type ""(.*)""")]
    public async Task SeesContentType(string contentType)
    {
        var frame = await _s.WaitForRfw(contentType);
        frame.ContentType.Should().Contain(contentType);
    }

    [Then(@"the card includes widgets? ""(.+)""")]
    public void CardIncludesWidgets(string widgetsCsv)
    {
        var widgets = widgetsCsv.Split(',', StringSplitOptions.TrimEntries)
            .Select(w => w.Trim('"'))
            .ToArray();
        _s.Last.Rfw!.ContainsWidgets(widgets).Should().BeTrue();
    }

    [Then(@"the card data includes ""(.+)""")]
    public void CardDataIncludes(string substringsCsv)
    {
        var raw = _s.Last.Rfw!.Data.GetRawText();
        foreach (var fragment in substringsCsv.Split(',', StringSplitOptions.TrimEntries)
                                              .Select(f => f.Trim('"')))
            raw.Should().Contain(fragment);
    }

    [Then(@"(\w+) emitted a ""(\w+)"" synapse with (.+)")]
    public async Task NeuronEmittedSynapse(string _, string synapseType, string args)
    {
        var fire = await _s.WaitForSynapse(synapseType);
        var expected = KeyValueParser.Parse(args);
        foreach (var kv in expected)
            fire.Args.Should().ContainKey(kv.Key).WhoseValue.Should().Be(kv.Value);
    }
}
```

- [ ] **Step 6: Run KeyValueParser tests, verify pass**

Run: `dotnet test test/Ino.NeuronTesting.Tests`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add src/Ino.NeuronTesting.Bdd/ test/Ino.NeuronTesting.Tests/KeyValueParserTests.cs ino.slnx
git commit -m "feat(neuron-testing-bdd): step library + KeyValueParser"
```

## Task 13: TripPlanner smoke test (proves end-to-end)

**Files:**
- Create: `domains/travel/TripPlanner.Smoke/TripPlanner.Smoke.csproj`
- Create: `domains/travel/TripPlanner.Smoke/_TravelTestBase.cs`
- Create: `domains/travel/TripPlanner.Smoke/TripPlannerSmokeTests.cs`
- Create: `domains/travel/TripPlanner.Smoke/trip-planner-smoke.feature`

- [ ] **Step 1: Create the csproj**

`domains/travel/TripPlanner.Smoke/TripPlanner.Smoke.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Reqnroll.xUnit" />
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\..\src\Ino.NeuronTesting\Ino.NeuronTesting.csproj" />
    <ProjectReference Include="..\..\..\src\Ino.NeuronTesting.Bdd\Ino.NeuronTesting.Bdd.csproj" />
    <ProjectReference Include="..\..\..\src\Ino.AppHost\Ino.AppHost.csproj" />
    <ProjectReference Include="..\Ino.Domains.Travel\Ino.Domains.Travel.csproj" />
  </ItemGroup>
  <ItemGroup>
    <None Update="*.feature" Generator="ReqnrollSingleFileGenerator" LastGenOutput="%(Filename).feature.cs" />
  </ItemGroup>
</Project>
```

Run: `dotnet sln ino.slnx add domains/travel/TripPlanner.Smoke/TripPlanner.Smoke.csproj`

- [ ] **Step 2: Write the per-domain intermediate base**

`domains/travel/TripPlanner.Smoke/_TravelTestBase.cs`:

```csharp
using Ino.NeuronTesting;

namespace Ino.Domains.Travel.SmokeTests;

public abstract class TravelNeuronTest<TNeuron> : NeuronE2ETest<TNeuron, Projects.Ino_AppHost>
    where TNeuron : class { }
```

- [ ] **Step 3: Write the smoke test (C# Fact path first — proves NeuronE2ETest works without Reqnroll)**

`domains/travel/TripPlanner.Smoke/TripPlannerSmokeTests.cs`:

```csharp
using FluentAssertions;
using Ino.Domains.Travel.Plans;   // PlanTripPlan still lives here pre-rename
using Ino.NeuronTesting;
using Ino.NeuronTesting.Attributes;
using Xunit;

namespace Ino.Domains.Travel.SmokeTests;

// Until slice 2 renames the type, bind via attribute since NeuronIdResolver
// would otherwise need to introspect IPlanTripPlan-style markers.
[NeuronId("travel.plan-trip")]
public sealed class TripPlannerSmokeTests : TravelNeuronTest<PlanTripPlan>
{
    [Fact]
    public async Task Bali_initial_card_emits_intro_content_type()
    {
        var s = await Chat("plan a trip to Bali next month");
        s.Last.ContentType.Should().Be("ino.travel.intro");
        s.Last.Rfw!.ContainsWidgets("WeatherSummaryCard", "FlightCard").Should().BeTrue();
    }
}
```

The `[NeuronId]` is on the test class (because the resolver looks at `TNeuron` which is `PlanTripPlan` here). To put it on `PlanTripPlan` directly would touch production code — defer to slice 2 when we rename. For the smoke we accept the attribute on the test class temporarily and special-case `NeuronIdResolver` to also check the calling test class's attribute.

Adjust `NeuronIdResolver.Resolve` to also check the test class type if passed (overload). Practical patch: have `NeuronE2ETest.InitializeAsync` look first on `this.GetType()`, then fall back to `typeof(TNeuron)`:

```csharp
NeuronUnderTest = NeuronIdResolver.Resolve(this.GetType()) ?? NeuronIdResolver.Resolve(typeof(TNeuron));
```

Add a `TryResolve` overload to `NeuronIdResolver` returning `NeuronId?`. Update `NeuronE2ETest.InitializeAsync`. This is incremental on Task 10's code — apply the patch here rather than retrofit.

- [ ] **Step 4: Write the BDD scenario**

`domains/travel/TripPlanner.Smoke/trip-planner-smoke.feature`:

```gherkin
@neuron:travel.plan-trip
Feature: TripPlanner smoke

  Scenario: Bali initial card emits ino.travel.intro
    When the user says "plan a trip to Bali next month"
    Then the user sees a card with content type "ino.travel.intro"
     And the card includes widgets "WeatherSummaryCard", "FlightCard"
```

For Reqnroll to pass `NeuronSession` into `NeuronSteps`, the scenario context needs the session. Ship a Reqnroll `[BeforeScenario]` hook in a class that bridges `NeuronE2ETest`'s session to Reqnroll's container. Add this in slice 1 if straightforward (Context7 the Reqnroll DI surface), else stub the session-injection and only run the C# Fact in slice 1.

If the Reqnroll bridge isn't trivial: SKIP the Gherkin scenario in slice 1 — keep only the C# Fact. The Bdd assembly still ships; it just isn't end-to-end-validated until slice 2 has a real test.

- [ ] **Step 5: Run the smoke**

```powershell
$env:CI = "true"  # headless
dotnet test domains/travel/TripPlanner.Smoke/TripPlanner.Smoke.csproj --verbosity normal
```

Expected: 1 passed (the C# Fact). If the Gherkin scenario was kept and the Reqnroll bridge works: 2 passed.

- [ ] **Step 6: Run the full existing suite to confirm no regression**

```powershell
dotnet test ino.slnx --verbosity minimal
```

Expected: every existing test still passes. The only NEW tests are in `Ino.NeuronTesting.Tests` and `TripPlanner.Smoke`.

- [ ] **Step 7: Commit**

```bash
git add domains/travel/TripPlanner.Smoke/ ino.slnx src/Ino.NeuronTesting/Internals/NeuronIdResolver.cs src/Ino.NeuronTesting/NeuronE2ETest.cs
git commit -m "test(travel): TripPlanner smoke proves NeuronE2E framework end-to-end"
```

**Slice 1 exit:** All existing tests still pass. Smoke test passes headed and headless. Framework + Bdd assemblies build clean.

---

# Slice 2 — Migrate Travel domain

**Exit criteria:** Old `domains/travel/Ino.Domains.Travel.Tests/` deleted. Every test that lived in it has an equivalent (Gherkin or C#) in the new layout. CI green.

## Task 14: Rename PlanTripPlan → TripPlanner, move into agent folder

**Files:**
- Move: `domains/travel/Ino.Domains.Travel/Plans/PlanTripPlan.cs` → `domains/travel/TripPlanner/TripPlanner.cs`
- Move: `domains/travel/Ino.Domains.Travel/Contracts/PlanTripRequest.cs` → `domains/travel/TripPlanner/PlanTripRequest.cs` (if separate file; otherwise extract from existing Contracts)
- Modify: `domains/travel/Ino.Domains.Travel/Travel.cs` — `PlanType = typeof(IPlanTripPlan)` → `typeof(ITripPlanner)` (rename interface) or `typeof(TripPlanner)` if dropping the interface
- Modify: every reference to `PlanTripPlan` / `IPlanTripPlan` in the Travel project + its test project

- [ ] **Step 1: Catalogue references**

Run: `grep -rn "PlanTripPlan\|IPlanTripPlan" domains/travel/ src/`

- [ ] **Step 2: Move + rename in one commit (no behaviour change)**

```bash
mkdir -p domains/travel/TripPlanner
git mv domains/travel/Ino.Domains.Travel/Plans/PlanTripPlan.cs domains/travel/TripPlanner/TripPlanner.cs
```

Edit the file:
- `namespace Ino.Domains.Travel.Plans;` → `namespace Ino.Domains.Travel.TripPlanner;`
- `class PlanTripPlan` → `class TripPlanner`
- `interface IPlanTripPlan` → if it lives in this file, rename to `ITripPlanner`. If it lives in `Ino.Domains.Travel.Contracts`, rename there.
- Add `[Ino.NeuronTesting.Attributes.NeuronId("travel.plan-trip")]` to the type (so the resolver works without the test-class fallback). Requires conditional reference: see step 3.

- [ ] **Step 3: Add NeuronId attribute reference (production code → testing assembly?)**

Two choices:
1. **Move `NeuronIdAttribute` into `Ino.Core`** (no test-only attribute on production types). Update `Ino.NeuronTesting/Attributes/NeuronIdAttribute.cs` to re-export or delete.
2. **Don't put the attribute on production code** — keep using the test-class fallback resolver from Task 13 step 3.

Recommendation: option 1. Move the attribute to `Ino.Core`. It's a tiny declarative annotation, not test-specific behaviour.

```bash
git mv src/Ino.NeuronTesting/Attributes/NeuronIdAttribute.cs src/Ino.Core/NeuronIdAttribute.cs
```

Edit namespace: `Ino.NeuronTesting.Attributes` → `Ino.Core`.

Update `NeuronIdResolver` `using Ino.NeuronTesting.Attributes;` → `using Ino.Core;`.

Update `Ino.NeuronTesting.csproj` already references `Ino.Core` so nothing else moves.

- [ ] **Step 4: Add the attribute to TripPlanner.cs**

```csharp
[NeuronId("travel.plan-trip")]
public sealed class TripPlanner(...) : Grain, ITripPlanner, IRfwEventHandler { ... }
```

- [ ] **Step 5: Update Travel.cs**

In `domains/travel/Ino.Domains.Travel/Travel.cs:41`:

```csharp
PlanType = typeof(ITripPlanner),
```

- [ ] **Step 6: Build everything that referenced the old names**

Run: `dotnet build ino.slnx`

Fix every compile error by replacing `PlanTripPlan`/`IPlanTripPlan` with `TripPlanner`/`ITripPlanner`. Update test files in `Ino.Domains.Travel.Tests` (smoke test stays on the OLD type until step 7).

- [ ] **Step 7: Update the smoke test from Task 13 to use the renamed type**

```csharp
[NeuronId("travel.plan-trip")]   // remove if attribute now on TripPlanner production class
public sealed class TripPlannerSmokeTests : TravelNeuronTest<TripPlanner> { ... }
```

If the attribute is on the production class, drop the `[NeuronId]` here and rely on the resolver.

- [ ] **Step 8: Test**

```powershell
dotnet build ino.slnx
dotnet test ino.slnx --filter "FullyQualifiedName~Travel"
```

Expected: 0 build errors, all Travel tests pass.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "refactor(travel): rename PlanTripPlan → TripPlanner, move to agent folder, NeuronId attribute"
```

## Task 15: Move other Travel neurons into agent folders

**Files:** Mechanical moves analogous to Task 14.

- [ ] **Step 1: Catalogue neurons + their files**

Run: `find domains/travel/Ino.Domains.Travel/Neurons -name "*.cs"`
Run: `find domains/travel/Ino.Domains.Travel/Plans -name "*.cs"`
Run: `find domains/travel/Ino.Domains.Travel/Rfw -name "*.cs"`

Map each neuron to its new agent-folder + new name:

| Old | New |
|-----|-----|
| `Neurons/FlightSearchNeuron.cs` | `FlightSearch/FlightSearch.cs` |
| `Neurons/HotelSearchNeuron.cs`  | `HotelSearch/HotelSearch.cs` |
| `Neurons/PlaceSearchNeuron.cs`  | `PlaceSearch/PlaceSearch.cs` |
| `Neurons/FlightMonitorNeuron.cs`| `FlightMonitor/FlightMonitor.cs` |
| `Plans/FindFlightsPlan.cs`      | `FlightSearch/FlightSearchPlan.cs` (if exists; merge) |
| `Plans/FindHotelsPlan.cs`       | `HotelSearch/HotelSearchPlan.cs` |
| `Plans/FindPlacesPlan.cs`       | `PlaceSearch/PlaceSearchPlan.cs` |
| `Rfw/TripIntroBuilder.cs`       | `TripPlanner/Rfw/TripIntroBuilder.cs` |
| `Rfw/HotelCardListBuilder.cs`   | `HotelSearch/Rfw/HotelCardListBuilder.cs` (only if used by HotelSearch alone; if also by TripPlanner via composition, leave in TripPlanner/Rfw/) |
| `Rfw/EventCardListBuilder.cs`   | `TripPlanner/Rfw/EventCardListBuilder.cs` (events are part of TripPlanner's flow) |
| `Rfw/ActivityCardListBuilder.cs`| `TripPlanner/Rfw/ActivityCardListBuilder.cs` |
| `Rfw/TripSummaryBuilder.cs`     | `TripPlanner/Rfw/TripSummaryBuilder.cs` |
| `Rfw/PlaceCardListBuilder.cs`   | `PlaceSearch/Rfw/PlaceCardListBuilder.cs` |
| `Rfw/MockFlightCorpus.cs`       | `FlightSearch/MockFlightCorpus.cs` |
| `Rfw/MockEventsCorpus.cs`       | `TripPlanner/MockEventsCorpus.cs` |
| `Rfw/MockActivityCorpus.cs`     | `TripPlanner/MockActivityCorpus.cs` |
| `Rfw/MockWeatherCorpus.cs`      | `TripPlanner/MockWeatherCorpus.cs` |
| `Rfw/MockHotelCorpus.cs`        | `HotelSearch/MockHotelCorpus.cs` |
| `SeedData/FlightFixture.cs`     | `FlightSearch/FlightFixture.cs` |
| `SeedData/HotelFixture.cs`      | `HotelSearch/HotelFixture.cs` |
| `SeedData/PlaceFixture.cs`      | `PlaceSearch/PlaceFixture.cs` |
| `Plans/PlanTripPromptParser.cs` | `TripPlanner/PlanTripPromptParser.cs` |
| `Neurons/TripPlannerSlotParser.cs` | DELETE (was used only by deprecated TripPlannerNeuron; confirm via grep before deleting) |
| `Neurons/TripPlannerNeuron.cs`  | DELETE (deprecated; confirm no remaining references) |

- [ ] **Step 2: For each row, do `git mv`, then update namespaces**

Per file:

```bash
mkdir -p domains/travel/FlightSearch
git mv domains/travel/Ino.Domains.Travel/Neurons/FlightSearchNeuron.cs domains/travel/FlightSearch/FlightSearch.cs
```

Edit FlightSearch.cs:
- `namespace Ino.Domains.Travel.Neurons;` → `namespace Ino.Domains.Travel.FlightSearch;`
- `class FlightSearchNeuron` → `class FlightSearch`
- Add `[NeuronId("travel.find-flights")]`

Repeat per row.

- [ ] **Step 3: Update Travel.cs PlanType references**

```csharp
PlanType = typeof(IFlightSearch),  // for find-flights
PlanType = typeof(IHotelSearch),   // etc.
```

Confirm the interface names exist or rename interfaces too. Plain rule: every interface that named `*Plan` or `*Neuron` becomes `I<AgentName>`.

- [ ] **Step 4: Build, fix all compile errors**

Run: `dotnet build ino.slnx`

- [ ] **Step 5: Run all Travel tests still in the OLD test project**

Run: `dotnet test domains/travel/Ino.Domains.Travel.Tests --filter "FullyQualifiedName~Travel"`

Expected: pass — the moves are pure-rename, no behaviour change.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(travel): move every neuron into its agent folder, rename to drop *Neuron/*Plan suffix"
```

## Task 16: Create domains/travel/Tests.csproj with globs

**Files:**
- Create: `domains/travel/Tests.csproj`
- Create: `domains/travel/_TravelTestBase.cs`

- [ ] **Step 1: Create the csproj**

`domains/travel/Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <AssemblyName>Ino.Domains.Travel.Tests</AssemblyName>
    <RootNamespace>Ino.Domains.Travel.Tests</RootNamespace>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Reqnroll.xUnit" />
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Ino.NeuronTesting\Ino.NeuronTesting.csproj" />
    <ProjectReference Include="..\..\src\Ino.NeuronTesting.Bdd\Ino.NeuronTesting.Bdd.csproj" />
    <ProjectReference Include="..\..\src\Ino.AppHost\Ino.AppHost.csproj" />
    <ProjectReference Include="Ino.Domains.Travel\Ino.Domains.Travel.csproj" />
  </ItemGroup>
  <!-- Glob test files that live next to neurons -->
  <ItemGroup>
    <Compile Include="**/*Tests.cs" Exclude="bin/**;obj/**;Ino.Domains.Travel/**" />
    <None Update="**/*.feature" Generator="ReqnrollSingleFileGenerator"
                                LastGenOutput="%(Filename).feature.cs"
                                Exclude="bin/**;obj/**;Ino.Domains.Travel/**" />
  </ItemGroup>
</Project>
```

The exclude on `Ino.Domains.Travel/**` is because the production project lives one level down; we don't want its own .cs files swept in.

- [ ] **Step 2: Add per-domain intermediate base**

`domains/travel/_TravelTestBase.cs`:

```csharp
using Ino.NeuronTesting;

namespace Ino.Domains.Travel.Tests;

public abstract class TravelNeuronTest<TNeuron> : NeuronE2ETest<TNeuron, Projects.Ino_AppHost>
    where TNeuron : class { }
```

- [ ] **Step 3: Add to slnx**

Run: `dotnet sln ino.slnx add domains/travel/Tests.csproj`

- [ ] **Step 4: Build (no test files yet — should compile empty)**

Run: `dotnet build domains/travel/Tests.csproj`
Expected: 0 errors.

- [ ] **Step 5: Commit**

```bash
git add domains/travel/Tests.csproj domains/travel/_TravelTestBase.cs ino.slnx
git commit -m "build(travel): add Tests.csproj with **/*Tests.cs glob"
```

## Task 17: Translate RichTripPlanningE2ETests → trip-planner.feature

**Files:**
- Create: `domains/travel/TripPlanner/trip-planner.feature`
- Create: `domains/travel/TripPlanner/TripPlanner.Tests.cs` (one-line subclass)

- [ ] **Step 1: Read the source test once more to make sure no assertion is dropped**

Run: `cat domains/travel/Ino.Domains.Travel.Tests/RichTripPlanningE2ETests.cs`

Inventory the assertions per scenario.

- [ ] **Step 2: Write the feature**

`domains/travel/TripPlanner/trip-planner.feature`:

```gherkin
@neuron:travel.plan-trip
Feature: TripPlanner

  Scenario: Bali trip — initial card emits intro
    When the user says "plan a trip to Bali next month"
    Then the user sees a card with content type "ino.travel.intro"
     And the card includes widgets "WeatherSummaryCard", "FlightCard"

  Scenario: Bali trip — full 6-hop flow
    Given the user said "plan a trip to Bali next month"
    When the user fires "flight.selected" with flightId="FL-001"
    Then the user sees a card with content type "ino.travel.hotels"
     And the card includes widget "HotelCard"

    When the user fires "hotel.selected" with hotelId="H-001"
    Then the user sees a card with content type "ino.travel.events"
     And the card includes widgets "EventCard", "EventSkipButton"

    When the user fires "event.selected" with eventId="EV-001"
    Then the user sees a card with content type "ino.travel.activities"
     And the card data includes "weatherBadge"

    When the user fires "activity.selected" with activityId="AC-001"
    Then the user sees a card with content type "ino.travel.summary"
     And the card data includes "Bali", "Singapore Airlines"

  Scenario: Events skipped still reaches activities
    Given the user said "plan a trip to Bali next month"
     And the user fired "flight.selected" with flightId="FL-001"
     And the user fired "hotel.selected" with hotelId="H-001"
    When the user fires "events.skipped" with _=
    Then the user sees a card with content type "ino.travel.activities"
     And the card includes widget "ActivityCard"
```

The empty-args trick (`with _=`) is awkward — the step regex `with (.+)` requires SOMETHING. Adjust the regex or add a separate step:

```csharp
[When(@"the user fires ""([^""]+)""$")]
public Task UserFiresNoArgs(string eventName) =>
    _s.Fire(eventName, new Dictionary<string, string>());
```

Add that step to `NeuronSteps` and rewrite the scenario:

```gherkin
    When the user fires "events.skipped"
    Then the user sees a card with content type "ino.travel.activities"
```

- [ ] **Step 3: Write the test class subclass**

`domains/travel/TripPlanner/TripPlanner.Tests.cs`:

```csharp
namespace Ino.Domains.Travel.Tests;

public sealed class TripPlannerTests : TravelNeuronTest<TripPlanner.TripPlanner> { }
```

(Disambiguate the type vs the namespace if needed.)

- [ ] **Step 4: Run the new tests**

```powershell
$env:CI = "true"
dotnet test domains/travel/Tests.csproj --filter "Neuron=travel.plan-trip"
```

Expected: 3 scenarios pass (initial, six-hop, events-skipped).

- [ ] **Step 5: Run BOTH old and new tests to confirm parity**

Run: `dotnet test ino.slnx --filter "FullyQualifiedName~Travel|Neuron=travel.plan-trip"`

The old `RichTripPlanningE2ETests` should still be passing (we haven't deleted it yet). The new feature scenarios should also be passing.

- [ ] **Step 6: Commit**

```bash
git add domains/travel/TripPlanner/trip-planner.feature domains/travel/TripPlanner/TripPlanner.Tests.cs
git commit -m "test(travel): TripPlanner BDD scenarios cover RichTripPlanningE2E parity"
```

## Task 18: Translate AskInoRoutingTests → cortex.feature (kernel domain)

**Files:**
- Create: `src/Ino.Kernel/Cortex/cortex.feature`
- Create: `src/Ino.Kernel/Cortex/Cortex.Tests.cs`
- Create: `test/Ino.Kernel/Tests.csproj` (or reuse existing kernel test project — see step 1)

- [ ] **Step 1: Check whether a kernel-level NeuronE2E project exists yet**

Run: `find test/Ino.Kernel.Tests -type f`

`Ino.Kernel.Tests` exists but uses `InoTestSiloFixture` (TestCluster, no Aspire). We need a NEW Aspire-backed test project for Cortex E2E. Name: `test/Ino.Kernel.E2E.Tests` (parallels `test/Ino.E2E.Tests`).

Or — put the kernel-level NeuronE2E tests in a new `src/Ino.Kernel/Tests.csproj` mirroring the per-domain pattern. Recommended since Cortex is a kernel-level neuron and the per-neuron-folder convention applies.

- [ ] **Step 2: Create src/Ino.Kernel/Tests.csproj**

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <AssemblyName>Ino.Kernel.E2E.Tests</AssemblyName>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="Reqnroll.xUnit" />
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Ino.NeuronTesting\Ino.NeuronTesting.csproj" />
    <ProjectReference Include="..\Ino.NeuronTesting.Bdd\Ino.NeuronTesting.Bdd.csproj" />
    <ProjectReference Include="..\Ino.AppHost\Ino.AppHost.csproj" />
    <ProjectReference Include="Ino.Kernel.csproj" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="**/*Tests.cs" />
    <None Update="**/*.feature" Generator="ReqnrollSingleFileGenerator" LastGenOutput="%(Filename).feature.cs" />
  </ItemGroup>
</Project>
```

Add `_KernelTestBase.cs`:

```csharp
public abstract class KernelNeuronTest<T> : NeuronE2ETest<T, Projects.Ino_AppHost> where T : class { }
```

- [ ] **Step 3: Find or define the Cortex neuron**

Run: `grep -rn "class Cortex\|CortexNeuron\|CortexCapability" src/Ino.Kernel src/Ino.Core.Hosting`

Cortex lives in `src/Ino.Kernel/CortexNeuron.cs` and `src/Ino.Core.Hosting/Capabilities/CortexCapability.cs`. Since the user-visible behaviour we're testing (AskIno routing) is what `CortexCapability.RouteAsync` does, target `CortexCapability` as the neuron impl. Add `[NeuronId("ino.cortex")]` to it (introducing a `NeuronId` for Cortex if one doesn't exist; today's CortexNeuron may not have a registry entry).

If Cortex doesn't have a `NeuronId`, add a synthetic one for the test framework's purposes: define a constant `CortexCapability.NeuronId = "ino.cortex"` and stamp the attribute. This is a small new declaration but doesn't affect production routing because Cortex is itself the router.

- [ ] **Step 4: Write the cortex.feature**

`src/Ino.Kernel/Cortex/cortex.feature`:

```gherkin
@neuron:ino.cortex
Feature: Cortex routing

  Scenario: Plan-trip prompt routes via regex
    When the user says "plan a trip to Bali next month"
    Then the user sees a card with content type "ino.travel.intro"

  Scenario: Find-flights prompt routes via regex
    When the user says "find flights to Tokyo"
    Then the assistant reply contains "Searching flights"

  Scenario: Unknown prompt returns the unrouted reply
    When the user says "asdf qwerty"
    Then the assistant reply contains "No specialist"
```

The "assistant reply contains" Then doesn't exist in `NeuronSteps` yet — add it:

```csharp
[Then(@"the assistant reply contains ""(.*)""")]
public void ReplyContains(string fragment) =>
    _s.Last.Reply.Should().Contain(fragment);
```

- [ ] **Step 5: One-line bootstrap**

`src/Ino.Kernel/Cortex/Cortex.Tests.cs`:

```csharp
using Ino.Core.Hosting.Capabilities;
public sealed class CortexTests : KernelNeuronTest<CortexCapability> { }
```

- [ ] **Step 6: Add to slnx, build, run**

```bash
dotnet sln ino.slnx add src/Ino.Kernel/Tests.csproj
dotnet test src/Ino.Kernel/Tests.csproj
```

Expected: 3 scenarios pass.

- [ ] **Step 7: Commit**

```bash
git add src/Ino.Kernel/Cortex/ src/Ino.Kernel/Tests.csproj src/Ino.NeuronTesting.Bdd/NeuronSteps.cs ino.slnx
git commit -m "test(kernel): Cortex BDD scenarios cover AskInoRouting parity"
```

## Task 19: Translate TripPlanningNeuronTests → @ui scenarios

**Files:**
- Modify: `domains/travel/TripPlanner/trip-planner.feature` (add @ui scenarios)

- [ ] **Step 1: Append to trip-planner.feature**

```gherkin
  @ui
  Scenario: Bali trip — render in browser shows intro
    When the user says "plan a trip to Bali next month"
     And the user opens the chat in a browser
    Then the user sees a card with content type "ino.travel.intro"

  @ui
  Scenario: Tokyo no-dates still emits intro
    When the user says "plan a trip to Tokyo"
     And the user opens the chat in a browser
    Then the user sees a card with content type "ino.travel.intro"
```

The `When … And the user opens the chat in a browser` step needs to know the kernel HTTPS URL. Update the `OpensBrowser` step:

```csharp
[When(@"the user opens the chat in a browser")]
public async Task OpensBrowser()
{
    var url = _ctx.Get<string>("kernelHttpsUrl");
    var page = await _s.OpenBrowser(url, _ctx.GetValueOrDefault<string>("lastPrompt"));
    _ctx.Set(page, "page");
}
```

The session needs to know the URL — currently `NeuronSession.OpenBrowser` takes it as an arg. Either:
- Cache the URL on `NeuronSession` at construction (pass in via `Open()`)
- Fetch from a Reqnroll `[BeforeScenario]` hook that reads `App.GetEndpoint("kernel", "https")`

Recommendation: cache on `NeuronSession`. Modify `NeuronSession`'s constructor to accept `kernelHttpsUrl`, drop the parameter from `OpenBrowser`. Update Task 9's emitted code accordingly (apply the patch in this task, not retroactively).

- [ ] **Step 2: Patch NeuronSession + NeuronE2ETest**

`NeuronSession.cs`:

```csharp
internal NeuronSession(Ino.InoClient client, PlaywrightLifecycle playwright, string kernelHttpsUrl, string userId)
{
    _client = client;
    _playwright = playwright;
    _kernelHttpsUrl = kernelHttpsUrl;
    UserId = userId;
    _observer = new SynapseObserver(correlationId: "");
}

readonly string _kernelHttpsUrl;

public async Task<NeuronPage> OpenBrowser(string? prompt = null)
{
    // … same body, use _kernelHttpsUrl instead of parameter
}
```

`NeuronE2ETest.Open`:

```csharp
protected NeuronSession Open(string? userId = null) =>
    new(_client!, _playwright!, KernelGrpcUrl,
        userId ?? $"{NeuronUnderTest.Value}-{Guid.NewGuid():N}");
```

- [ ] **Step 3: Run @ui scenarios**

```powershell
$env:CI = "true"  # headless
dotnet test domains/travel/Tests.csproj --filter "Category=ui"
```

Reqnroll xUnit auto-converts `@ui` scenario tags into xUnit `[Trait("Category", "ui")]`. Confirm via Context7 (`mcp__context7__query-docs` `/reqnroll/reqnroll` query "scenario tags xunit category trait conversion").

Expected: 2 @ui scenarios pass.

- [ ] **Step 4: Try headed once for visual confirmation**

```powershell
Remove-Item env:CI
dotnet test domains/travel/Tests.csproj --filter "Category=ui"
```

Expected: 2 Chromium tabs pop up. Visual confirmation only — no programmatic assertion.

- [ ] **Step 5: Commit**

```bash
git add domains/travel/TripPlanner/trip-planner.feature src/Ino.NeuronTesting/NeuronSession.cs src/Ino.NeuronTesting/NeuronE2ETest.cs src/Ino.NeuronTesting.Bdd/NeuronSteps.cs
git commit -m "test(travel): @ui scenarios pop Chromium showing the rendered cards"
```

## Task 20: Delete the old Travel test project

**Files:**
- Delete: `domains/travel/Ino.Domains.Travel.Tests/` (entire directory)

- [ ] **Step 1: Verify every old test has a new equivalent**

Run: `git ls-files domains/travel/Ino.Domains.Travel.Tests/*.cs | grep -v Tokyo`

For each remaining `.cs` file (RichTripPlanningE2ETests, AskInoRoutingTests already moved to kernel, TripPlanningNeuronTests, PlanTripPlanRfwEventsTests, FlightCardListBuilderTests, Storyboard tests):

| Old file | New home |
|----------|----------|
| RichTripPlanningE2ETests.cs | trip-planner.feature 6-hop scenario (Task 17) |
| AskInoRoutingTests.cs | cortex.feature (Task 18) |
| TripPlanningNeuronTests.cs | trip-planner.feature @ui scenarios (Task 19) |
| PlanTripPlanRfwEventsTests.cs | TODO — translate to scenarios in this task |
| FlightCardListBuilderTests.cs | UNIT TEST — move to `domains/travel/FlightSearch/Rfw/FlightCardListBuilderTests.cs` and add to Tests.csproj glob (NOT a NeuronE2E test) |
| Storyboard/* | Storyboard infra — review whether still needed; if yes, move to `domains/travel/Storyboard/` and keep as-is |
| ReqnrollXunitV3Compat.cs | If still needed for Reqnroll v3 compat, move to `domains/travel/_ReqnrollCompat.cs` |
| Features/Tokyo.feature(.cs) | Move to `domains/travel/Tokyo/Tokyo.feature` if you want to keep the storyboard scenario; else delete |

- [ ] **Step 2: Translate PlanTripPlanRfwEventsTests scenarios**

Read the file and append matching scenarios to `trip-planner.feature` if they aren't already covered.

- [ ] **Step 3: Move FlightCardListBuilderTests + Storyboard**

```bash
mkdir -p domains/travel/FlightSearch/Rfw
git mv domains/travel/Ino.Domains.Travel.Tests/FlightCardListBuilderTests.cs domains/travel/FlightSearch/Rfw/

mkdir -p domains/travel/Storyboard
git mv domains/travel/Ino.Domains.Travel.Tests/Storyboard/*.cs domains/travel/Storyboard/
```

- [ ] **Step 4: Verify the new Tests.csproj glob picks them up**

Run: `dotnet build domains/travel/Tests.csproj`
Expected: includes the moved test files.

Run: `dotnet test domains/travel/Tests.csproj`
Expected: every translated and moved test passes.

- [ ] **Step 5: Remove from slnx and delete**

```bash
dotnet sln ino.slnx remove domains/travel/Ino.Domains.Travel.Tests/Ino.Domains.Travel.Tests.csproj
git rm -r domains/travel/Ino.Domains.Travel.Tests
```

- [ ] **Step 6: Run the full Travel suite**

```powershell
dotnet test domains/travel/Tests.csproj --verbosity normal
```

Expected: every previously-existing Travel test now lives in the new csproj and passes.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "refactor(travel): retire Ino.Domains.Travel.Tests; everything migrated to per-neuron folders + Tests.csproj"
```

## Task 21: Delete the smoke project (real tests cover everything)

**Files:**
- Delete: `domains/travel/TripPlanner.Smoke/`

- [ ] **Step 1: Confirm Tests.csproj covers what Smoke covered**

The smoke had one C# Fact and (if implemented) one Gherkin scenario, both already covered by `trip-planner.feature` Bali initial scenario.

- [ ] **Step 2: Delete + remove from slnx**

```bash
dotnet sln ino.slnx remove domains/travel/TripPlanner.Smoke/TripPlanner.Smoke.csproj
git rm -r domains/travel/TripPlanner.Smoke
```

- [ ] **Step 3: Build clean, full test pass**

```powershell
dotnet build ino.slnx
dotnet test ino.slnx --filter "FullyQualifiedName~Travel"
```

Expected: 0 errors, all Travel tests green via Tests.csproj.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "chore(travel): retire smoke project — real Tests.csproj covers it"
```

**Slice 2 exit:** Travel domain fully on the new framework. Old `Ino.Domains.Travel.Tests` deleted. CI green.

---

# Slice 3 — Migrate other domains

For each domain, repeat the slice 2 pattern: rename neuron impls, move into agent folders, create Tests.csproj, translate tests to scenarios, delete old test project.

This slice produces 5 PRs (one per domain). Each PR is independent.

## Task 22: Migrate Taxi domain

**Files:**
- Reorganize: `domains/taxi/Ino.Domains.Taxi/` neurons into agent folders
- Create: `domains/taxi/Tests.csproj`, `domains/taxi/_TaxiTestBase.cs`
- Translate: `domains/taxi/Ino.Domains.Taxi.Tests/OrderRideHomePlanTests.cs` → `domains/taxi/RideOrderer/ride-orderer.feature`
- Delete: `domains/taxi/Ino.Domains.Taxi.Tests/`

- [ ] **Step 1: Catalogue Taxi neurons**

Run: `cat domains/taxi/Ino.Domains.Taxi/Taxi.cs`

For each `NeuronDefinition` in the Taxi domain:
- Identify the impl class (Plan or Neuron)
- Pick the agent name (e.g. `OrderRide` synapse → `RideOrderer` agent)
- Create folder `domains/taxi/<AgentName>/`
- `git mv` the impl + RFW + corpus + fixtures into it
- Rename type, update namespaces, add `[NeuronId("…")]`
- Update `Taxi.cs` `PlanType` references

- [ ] **Step 2: Create Tests.csproj + base**

Use the slice 2 / Task 16 template, substituting "Travel" for "Taxi".

- [ ] **Step 3: Translate every test in `Ino.Domains.Taxi.Tests` to a scenario**

Read every test file and write equivalent Gherkin in `domains/taxi/<AgentName>/<agent-name>.feature`.

For each test:
- Open the file, read the assertions
- Express the When/Then in the step library's vocabulary
- If a step doesn't exist, add it to `NeuronSteps` and verify it works

- [ ] **Step 4: Add @ui scenarios for any visual flows**

The Taxi domain may not have any (it's MCP-driven, mostly server-to-server). Skip if so.

- [ ] **Step 5: Build, test, delete old, commit**

```bash
dotnet sln ino.slnx add domains/taxi/Tests.csproj
dotnet test domains/taxi/Tests.csproj
dotnet sln ino.slnx remove domains/taxi/Ino.Domains.Taxi.Tests/Ino.Domains.Taxi.Tests.csproj
git rm -r domains/taxi/Ino.Domains.Taxi.Tests
git add -A
git commit -m "refactor(taxi): migrate domain to NeuronE2E framework"
```

## Task 23: Migrate Genesis domain

Same pattern as Task 22, applied to `domains/genesis/`. Read `domains/genesis/Ino.Domains.Genesis/Genesis.cs` for the neuron registry. Translate `domains/genesis/Ino.Domains.Genesis.Tests/{CreatorNeuronApprovalGatingTests,L1LoopAcceptanceTests,NeuronRegistryTests}.cs` to scenarios.

- [ ] Same five steps as Task 22, substituting "Genesis" for "Taxi".

## Task 24: Migrate Location domain

Same pattern. `domains/location/Ino.Domains.Location.Tests/LocationNeuronTests.cs` is small — likely 1-3 scenarios.

- [ ] Same five steps as Task 22, substituting "Location" for "Taxi".

## Task 25: Migrate Recall domain

Same pattern. Recall test project is currently mostly empty (just `InoTestCollection.cs`). Confirm via `ls domains/recall/Ino.Domains.Recall.Tests/` — if no tests exist yet, the migration is a no-op for tests but still creates the new folder structure for future use.

- [ ] Same five steps.

## Task 26: Migrate Reminders domain

Same pattern. Same caveat about possibly-empty test project.

- [ ] Same five steps.

**Slice 3 exit:** every domain follows the per-neuron folder pattern, every domain has its own Tests.csproj, every old `Ino.Domains.<X>.Tests/` is deleted. CI green.

---

# Slice 4 — Kernel-level + platform tests

**Exit criteria:** Every test class is on either `NeuronE2ETest<T>`, `InoTestSiloFixture` (TestCluster), `InoMultiSiloFixture` (TestCluster), or the new `InoPlatformTestAppHost` (platform Aspire). No more `InoTestAppHost` / `InoBrowserFixture` references in any test.

## Task 27: Migrate remaining kernel tests

**Files:**
- Modify: `test/Ino.Kernel.Tests/*` — these are TestCluster-based, mostly stay
- Migrate: any Aspire-backed kernel tests into `src/Ino.Kernel/Tests.csproj` (the project from Task 18)

- [ ] **Step 1: Catalogue kernel tests**

Run: `find test/Ino.Kernel.Tests -name "*.cs" | xargs grep -l "InoTestAppHost\|InoBrowserFixture"`

For each match: that test boots Aspire today. Migrate it.

For everything else (TestCluster-only): leave alone. They use `InoTestSiloFixture` which stays.

- [ ] **Step 2: Move Aspire-backed kernel tests**

Move them into `src/Ino.Kernel/<NeuronOrFeature>/<Name>.Tests.cs`, glob picked up by the existing `src/Ino.Kernel/Tests.csproj`.

- [ ] **Step 3: Build, test**

```bash
dotnet build ino.slnx
dotnet test test/Ino.Kernel.Tests src/Ino.Kernel/Tests.csproj
```

Expected: all pass.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "refactor(kernel): migrate Aspire-backed kernel tests to NeuronE2E framework"
```

## Task 28: Rename InoTestAppHost → InoPlatformTestAppHost in new src/Ino.PlatformTesting

**Files:**
- Create: `src/Ino.PlatformTesting/Ino.PlatformTesting.csproj`
- Move: `src/Ino.Testing/InoTestAppHost.cs` → `src/Ino.PlatformTesting/InoPlatformTestAppHost.cs`
- Move: `src/Ino.Testing/InoE2ECollection.cs` → `src/Ino.PlatformTesting/InoPlatformCollection.cs`
- Modify: `test/Ino.E2E.Tests/Ino.E2E.Tests.csproj` — switch reference + namespace
- Modify: `test/Ino.E2E.Tests/InoE2ECollection.cs` — point at new collection base

- [ ] **Step 1: Create the new csproj**

`src/Ino.PlatformTesting/Ino.PlatformTesting.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Aspire.Hosting.Testing" />
    <PackageReference Include="xunit.v3.extensibility.core" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\Ino.Core\Ino.Core.csproj" />
    <ProjectReference Include="..\Ino.Core.Hosting\Ino.Core.Hosting.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Move + rename files**

```bash
git mv src/Ino.Testing/InoTestAppHost.cs src/Ino.PlatformTesting/InoPlatformTestAppHost.cs
git mv src/Ino.Testing/InoE2ECollection.cs src/Ino.PlatformTesting/InoPlatformCollection.cs
```

Edit both files: `class InoTestAppHost` → `class InoPlatformTestAppHost`, namespace `Ino.Testing` → `Ino.PlatformTesting`. The class is otherwise unchanged — its existing port-allocation, marketplace stub, etc. all stay since `Ino.E2E.Tests` (install flow tests) genuinely needs them.

- [ ] **Step 3: Add to slnx, update Ino.E2E.Tests references**

```bash
dotnet sln ino.slnx add src/Ino.PlatformTesting/Ino.PlatformTesting.csproj
```

Edit `test/Ino.E2E.Tests/Ino.E2E.Tests.csproj` — replace `<ProjectReference Include="..\..\src\Ino.Testing\Ino.Testing.csproj" />` with `<ProjectReference Include="..\..\src\Ino.PlatformTesting\Ino.PlatformTesting.csproj" />`.

Update `test/Ino.E2E.Tests/InoE2ECollection.cs` to reference the new namespace + base class.

- [ ] **Step 4: Build, test**

```bash
dotnet build ino.slnx
dotnet test test/Ino.E2E.Tests
```

Expected: all install-flow / brain-stream / fire-test E2E tests pass.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(testing): rename InoTestAppHost → InoPlatformTestAppHost in new Ino.PlatformTesting"
```

**Slice 4 exit:** every test classified into one of the four buckets (NeuronE2E / TestCluster single / TestCluster multi / PlatformTestAppHost). `git grep "class.*InoTestAppHost"` returns one hit (the renamed class only).

---

# Slice 5 — Retire old infra

**Exit criteria:** No dead code. `git grep "InoTestAppHost\|InoBrowserFixture\|InoBrowserCollection"` returns nothing in `src/` (except possibly the renamed PlatformTestAppHost). `Ino.Testing.E2E` deleted.

## Task 29: Delete Ino.Testing.E2E

**Files:**
- Delete: `src/Ino.Testing.E2E/` (entire directory)

- [ ] **Step 1: Confirm no remaining references**

Run: `grep -rn "Ino.Testing.E2E\|InoBrowserFixture\|InoBrowserCollection" --include="*.cs" --include="*.csproj"`

Expected: zero hits in `src/`, `domains/`, `test/`. If any remain, the migration missed them — go fix the holdouts before deleting.

- [ ] **Step 2: Remove from slnx, delete**

```bash
dotnet sln ino.slnx remove src/Ino.Testing.E2E/Ino.Testing.E2E.csproj
git rm -r src/Ino.Testing.E2E
```

- [ ] **Step 3: Build, test**

```bash
dotnet build ino.slnx
dotnet test ino.slnx
```

Expected: all green.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "chore(testing): retire Ino.Testing.E2E (replaced by Ino.NeuronTesting)"
```

## Task 30: Slim Ino.Testing to TestCluster fixtures only

**Files:**
- Delete from `src/Ino.Testing/`: `InoTestCollection.cs` (already migrated to test-project-local subclasses earlier), any other stragglers.
- Keep: `InoTestSiloFixture.cs`, `InoMultiSiloFixture.cs`, `InoMultiSiloCollection.cs`, `BddMockChatClientFactory.cs`, `RecordedMockChatClient.cs`, `MockLlmMissException.cs`, `NeuronContextForTest.cs`, `TestSiloConfigurator.cs`, `InoTestCapture.cs`, `IInoTestCapture.cs`, `CaptureEntry.cs`, `LlmRecording.cs`.

- [ ] **Step 1: Catalogue what's left vs what's referenced**

Run: `ls src/Ino.Testing/*.cs`
Run: `grep -rn "Ino.Testing\." --include="*.cs" src/ test/ domains/ | sort -u`

For each file in `src/Ino.Testing/`: if grep shows zero references outside `src/Ino.Testing/`, candidate for deletion.

- [ ] **Step 2: Delete confirmed-unused files**

```bash
git rm src/Ino.Testing/InoTestCollection.cs       # if confirmed unused
# ... others
```

- [ ] **Step 3: Build, test**

```bash
dotnet build ino.slnx
dotnet test ino.slnx
```

Expected: all green.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "chore(testing): slim Ino.Testing to TestCluster fixtures + BDD mock"
```

## Task 31: Remove the BddMockChatClientFactory static-corpus API

**Files:**
- Modify: `src/Ino.Core.Hosting/Llm/BddMockChatClientFactory.cs`

- [ ] **Step 1: Confirm no remaining static-API callers**

Run: `grep -rn "BddMockChatClientFactory\.\(Register\|Unregister\)Corpus\b" --include="*.cs"`

If anything calls a non-`ForFixture` variant, migrate it first.

- [ ] **Step 2: Delete the legacy methods**

Remove the pre-fixture `RegisterCorpus(string)` / `UnregisterCorpus()` / `_globalScenarios` field. Keep only the `*ForFixture` API.

- [ ] **Step 3: Build, test**

```bash
dotnet build ino.slnx
dotnet test ino.slnx
```

Expected: all green.

- [ ] **Step 4: Commit**

```bash
git add src/Ino.Core.Hosting/Llm/BddMockChatClientFactory.cs
git commit -m "chore(bdd-mock): drop legacy global-corpus API; per-fixture is the only path"
```

**Slice 5 exit:** `git grep InoTestAppHost InoBrowserFixture InoBrowserCollection RegisterCorpus` (without `ForFixture`) returns nothing in `src/`, `domains/`, `test/` (except the renamed `InoPlatformTestAppHost`). Codebase is at its target shape.

---

## Self-review notes (filled by author after writing the plan)

**Spec coverage check:** Every spec section (vocabulary, project topology, folder layout, API, BDD layer, fixture lifecycle, migration plan, risk surface) has at least one task. The "open questions" in the spec (synapse observation channel, Reqnroll discovery, Tokyo.feature placement) are addressed concretely in this plan: synapse observation = ActivityListener (Task 4); Reqnroll discovery = embedded resources via `<None Update="*.feature" Generator>` (Task 13 step 1, Task 16 step 1); Tokyo.feature = move to `domains/travel/Tokyo/` if kept (Task 20 step 1 inventory).

**Placeholder scan:** No "TBD" / "TODO". One "TODO" appears in Task 20 step 1's table for PlanTripPlanRfwEventsTests — but it's followed immediately by Task 20 step 2 which says "translate them now". Promoted to a real step.

**Type consistency:** `NeuronSession` constructor signature shifts in Task 19 step 2 (adds `kernelHttpsUrl` param). The patch is described in-place rather than retroactively edited into Task 9 — execution order is sequential, the engineer reading Task 19 will see the patch and apply it. Risk is low because Task 9 is a fresh file at that moment.

**Per-domain repetition:** Tasks 22-26 deliberately compress to "same five steps as Task 22, substituting <X> for Taxi" — this is the one place I depart from the strict no-placeholders rule, because writing out 5×5 = 25 mechanically-identical tasks would bury the actual differences. Each Task 23-26 lists its specific source/target inventory inline; the steps are referenced by Task 22's full expansion. If your subagent runner can't follow that, expand the per-domain tasks before dispatching.
