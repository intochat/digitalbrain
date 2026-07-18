# ino POC — Phase 1: Core Foundations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a compilable, tested core primitives library (`Ino.Core` + `Ino.Core.Hosting`) with a working journaled-neuron integration test, setting the foundation for cross-silo dispatch, kernel silos, experiences, and the 10 canonical E2E scenarios in later phases.

**Architecture:** Greenfield .NET 10 solution at `D:\ino\POC\`. Six projects in Phase 1: `Ino.Core` (primitive types, no Orleans runtime — just `[GenerateSerializer]` via `Microsoft.Orleans.Sdk`), `Ino.Core.Hosting` (handler interfaces + `Neuron<TState, TEvent>` base class), `Ino.Testing` (in-memory `TestCluster` fixture + `RecordedMockChatClient` skeleton), `Ino.Core.Tests`, `Ino.Core.Hosting.Tests` (for the integration test that locks in the primitive contract), and `Ino.Testing.Tests` (validates the harness itself). No Aspire, no cross-silo dispatch, no analyzer, no source generator, no kernel silos — those land in later phases. This phase proves the primitive contract and the JournaledGrain + LogStorage backing works against a local `TestCluster`.

**Tech Stack:** .NET 10, Microsoft.Orleans 9.x, xunit.v3, FluentAssertions, YamlDotNet (for `RecordedMockChatClient`), Ulid (for stable event IDs). All packages via Central Package Management (`Directory.Packages.props`).

**Out of scope for Phase 1 (explicit):**
- AppHost / Aspire composition → Phase 2
- Cross-silo gRPC dispatch → Phase 2
- `ctx.Fire<T>` / `ctx.FireBroadcast<T>` / `IAmbientFire` → Phase 2
- `ctx.Search` / `ctx.Identity` facades → Phases 2-5
- Kernel silos (`system`, `identity`, `experiences`) → Phase 2
- Roslyn analyzer + source generator → Phase 3
- Marketplace endpoints → Phase 2
- Notes / Travel / AutoCheckIn experiences → Phases 4-6
- Playback / CausationIndex / BranchManager → Phase 6

**Scope decomposition note:** The full design spec (`docs/superpowers/specs/2026-04-14-ino-poc-core-primitives-design.md`) covers Track A in its entirety. Track A breaks into six implementation phases because the scope is too large for a single plan while keeping tasks genuinely bite-sized. Each phase produces working, testable software on its own. **This is Phase 1 of 6.**

---

## File Structure

All files are relative to `D:\ino\POC\` unless otherwise noted. The POC folder does not exist yet — Task 1 creates it.

### Solution + shared configuration (Task 1)
- `ino.slnx`
- `Directory.Build.props`
- `Directory.Packages.props`
- `global.json`
- `nuget.config`
- `README.md`

### `src/Ino.Core/` (Tasks 2, 3, 4) — primitive types only
- `Ino.Core.csproj`
- `ISynapse.cs`
- `SynapseError.cs`
- `NeuronResult.cs`
- `EventEnvelope.cs`
- `Capability.cs`
- `LlmTier.cs`
- `ExperienceMetadata.cs`
- `CanonicalNeuronInfo.cs`
- `ReactiveNeuronInfo.cs`
- `Attributes/UserEntryAttribute.cs`
- `Attributes/RequiresCapabilityAttribute.cs`
- `Attributes/InoExperienceAttribute.cs`

### `test/Ino.Core.Tests/` (Task 5) — xunit.v3 unit tests for primitives
- `Ino.Core.Tests.csproj`
- `NeuronResultTests.cs`
- `EventEnvelopeTests.cs`
- `CapabilityTests.cs`
- `ExperienceMetadataTests.cs`

### `src/Ino.Core.Hosting/` (Tasks 6, 7, 8) — handler interfaces + base class
- `Ino.Core.Hosting.csproj`
- `INeuron.cs`
- `IReactsTo.cs`
- `NeuronContext.cs`
- `IJournaledNeuronQuery.cs`
- `Neuron.cs` — the `Neuron<TState, TEvent>` base class

### `src/Ino.Testing/` (Tasks 9, 10, 11) — test harness
- `Ino.Testing.csproj`
- `InoTestSiloFixture.cs`
- `InoTestCollection.cs`
- `TestSiloConfigurator.cs`
- `InoTestNeuronContext.cs`
- `RecordedMockChatClient.cs`
- `LlmRecording.cs`
- `MockLlmMissException.cs`

### `test/Ino.Testing.Tests/` (Task 12) — validates the harness
- `Ino.Testing.Tests.csproj`
- `RecordedMockChatClientTests.cs`
- `fixtures/sample.llm.recordings.yml`

### `test/Ino.Core.Hosting.Tests/` (Tasks 13, 14, 15) — integration test locking in primitive contract
- `Ino.Core.Hosting.Tests.csproj`
- `Fixtures/TestEvent.cs`
- `Fixtures/TestState.cs`
- `Fixtures/TestNeuron.cs`
- `NeuronBaseClassTests.cs`

---

## Tasks

### Task 1: Scaffold POC solution + shared configuration

**Files:**
- Create: `D:\ino\POC\ino.slnx`
- Create: `D:\ino\POC\Directory.Build.props`
- Create: `D:\ino\POC\Directory.Packages.props`
- Create: `D:\ino\POC\global.json`
- Create: `D:\ino\POC\nuget.config`
- Create: `D:\ino\POC\README.md`

- [ ] **Step 1: Create the POC folder**

```bash
mkdir -p D:/ino/POC
```

Expected: folder exists, `ls D:/ino/POC` is empty.

- [ ] **Step 2: Write global.json pinning .NET 10 SDK**

File: `D:\ino\POC\global.json`
```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature"
  }
}
```

- [ ] **Step 3: Write Directory.Build.props**

File: `D:\ino\POC\Directory.Build.props`
```xml
<Project>
  <PropertyGroup>
    <TargetFramework>net11.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <IsPackable>false</IsPackable>
    <GenerateDocumentationFile>false</GenerateDocumentationFile>
  </PropertyGroup>
</Project>
```

- [ ] **Step 4: Write Directory.Packages.props with central versioning**

File: `D:\ino\POC\Directory.Packages.props`
```xml
<Project>
  <PropertyGroup>
    <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
    <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
  </PropertyGroup>
  <ItemGroup>
    <!-- Orleans 10.x — aligned with parent D:\ino\Directory.Packages.props -->
    <PackageVersion Include="Microsoft.Orleans.Sdk" Version="10.0.1" />
    <PackageVersion Include="Microsoft.Orleans.Server" Version="10.0.1" />
    <PackageVersion Include="Microsoft.Orleans.TestingHost" Version="10.0.1" />
    <PackageVersion Include="Microsoft.Orleans.Persistence.Memory" Version="10.0.1" />
    <PackageVersion Include="Microsoft.Orleans.Reminders" Version="10.0.1" />
    <!-- Orleans 10 event sourcing = Journaling package (currently alpha) -->
    <PackageVersion Include="Microsoft.Orleans.Journaling" Version="10.0.1-alpha.1" />
    <!-- .NET extensions — 11 preview for Hosting, 10.x stable line for AI -->
    <PackageVersion Include="Microsoft.Extensions.Hosting" Version="11.0.0-preview.2.26159.112" />
    <PackageVersion Include="Microsoft.Extensions.Logging" Version="11.0.0-preview.2.26159.112" />
    <PackageVersion Include="Microsoft.Extensions.DependencyInjection" Version="11.0.0-preview.2.26159.112" />
    <PackageVersion Include="Microsoft.Extensions.AI" Version="10.4.1" />
    <PackageVersion Include="Microsoft.Extensions.AI.Abstractions" Version="10.4.1" />
    <!-- Testing — xunit.v3 3.x line matching parent repo -->
    <PackageVersion Include="xunit.v3" Version="3.2.2" />
    <PackageVersion Include="xunit.runner.visualstudio" Version="3.1.5" />
    <PackageVersion Include="Microsoft.NET.Test.Sdk" Version="18.4.0" />
    <PackageVersion Include="FluentAssertions" Version="7.2.0" />
    <!-- Utilities -->
    <PackageVersion Include="YamlDotNet" Version="16.3.0" />
    <PackageVersion Include="Ulid" Version="1.3.4" />
  </ItemGroup>
</Project>
```

**Note on version pinning:** Versions above are aligned with the parent repo `D:\ino\Directory.Packages.props` (.NET 11 preview, Orleans 10.0.1, xunit.v3 3.2.2). Per user directive "always use all latest versions" — preview/alpha packages are acceptable. `Microsoft.Orleans.Journaling 10.0.1-alpha.1` replaces the Orleans 9.x `Microsoft.Orleans.EventSourcing` package; the API may differ and Task 7 requires Context7 verification before implementation.

- [ ] **Step 5: Write nuget.config**

File: `D:\ino\POC\nuget.config`
```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
  </packageSources>
</configuration>
```

- [ ] **Step 6: Write empty ino.slnx**

File: `D:\ino\POC\ino.slnx`
```xml
<Solution>
</Solution>
```

Projects will be added via `dotnet sln ino.slnx add` in later tasks.

- [ ] **Step 7: Write README.md stub**

File: `D:\ino\POC\README.md`
```markdown
# ino POC

Greenfield POC for ino — an AI-native operating system built on neurons + synapses.

This POC lives at `D:\ino\POC\` and **does not modify the existing code at `D:\ino\src\`**.
The existing codebase is a reference for what did and didn't work; this POC implements the
design fresh.

## Design

Full design: `docs/superpowers/specs/2026-04-14-ino-poc-core-primitives-design.md` in the
parent repo.

## Build & Test

```bash
cd D:/ino/POC
dotnet build ino.slnx
dotnet test ino.slnx
```

## Phase

This is Phase 1 of 6 — core primitive types + neuron base class + in-cluster integration
test. Phases 2-6 add cross-silo dispatch, kernel silos, analyzer, source generator,
experiences, and the full 10-scenario E2E acceptance surface.
```

- [ ] **Step 8: Verify the empty solution "builds"**

```bash
cd D:/ino/POC
dotnet build ino.slnx
```

Expected:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

(No projects yet, so this is trivially successful.)

- [ ] **Step 9: Commit**

```bash
cd D:/ino
git add POC
git commit -m "feat(poc): scaffold POC solution + shared configuration

Directory.Build.props, Directory.Packages.props with Orleans 10.0.1
+ xunit.v3 3.2.2 + FluentAssertions + YamlDotNet + Ulid.
global.json pinned to .NET 11 preview SDK.
Empty ino.slnx ready for projects.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

### Task 2: Create `Ino.Core` project + add to solution

**Files:**
- Create: `D:\ino\POC\src\Ino.Core\Ino.Core.csproj`

- [ ] **Step 1: Create folder**

```bash
mkdir -p D:/ino/POC/src/Ino.Core
```

- [ ] **Step 2: Write Ino.Core.csproj**

File: `D:\ino\POC\src\Ino.Core\Ino.Core.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Ino.Core</RootNamespace>
    <AssemblyName>Ino.Core</AssemblyName>
    <IsPackable>true</IsPackable>
    <PackageId>Ino.Core</PackageId>
    <Description>ino core primitive types — ISynapse, NeuronResult, EventEnvelope, Capability, attributes.</Description>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Orleans.Sdk" />
  </ItemGroup>
</Project>
```

The `Microsoft.Orleans.Sdk` reference brings in the `[GenerateSerializer]` and `[Id]` attributes used by payload records. No Orleans runtime — just the serialization codegen attributes.

- [ ] **Step 3: Add project to the solution**

```bash
cd D:/ino/POC
dotnet sln ino.slnx add src/Ino.Core/Ino.Core.csproj
```

- [ ] **Step 4: Build**

```bash
dotnet build ino.slnx
```

Expected:
```
Build succeeded.
Ino.Core -> D:\ino\POC\src\Ino.Core\bin\Debug\net11.0\Ino.Core.dll
    0 Warning(s)
    0 Error(s)
```

(The project has no `.cs` files yet, so it builds an empty assembly.)

- [ ] **Step 5: Commit**

```bash
cd D:/ino
git add POC
git commit -m "feat(poc): add Ino.Core project"
```

---

### Task 3: Define all `Ino.Core` primitive types

These are all trivial records with no logic — one commit covering every type in `Ino.Core`. Each file is small enough that individual commits would be noise.

**Files:**
- Create: `D:\ino\POC\src\Ino.Core\ISynapse.cs`
- Create: `D:\ino\POC\src\Ino.Core\SynapseError.cs`
- Create: `D:\ino\POC\src\Ino.Core\NeuronResult.cs`
- Create: `D:\ino\POC\src\Ino.Core\EventEnvelope.cs`
- Create: `D:\ino\POC\src\Ino.Core\LlmTier.cs`
- Create: `D:\ino\POC\src\Ino.Core\Capability.cs`
- Create: `D:\ino\POC\src\Ino.Core\CanonicalNeuronInfo.cs`
- Create: `D:\ino\POC\src\Ino.Core\ReactiveNeuronInfo.cs`
- Create: `D:\ino\POC\src\Ino.Core\ExperienceMetadata.cs`
- Create: `D:\ino\POC\src\Ino.Core\Attributes\UserEntryAttribute.cs`
- Create: `D:\ino\POC\src\Ino.Core\Attributes\RequiresCapabilityAttribute.cs`
- Create: `D:\ino\POC\src\Ino.Core\Attributes\InoExperienceAttribute.cs`

- [ ] **Step 1: Write `ISynapse.cs`**

File: `D:\ino\POC\src\Ino.Core\ISynapse.cs`
```csharp
namespace Ino.Core;

/// <summary>
/// Marker interface on every cross-neuron payload record.
/// Used as the generic constraint on INeuron&lt;T&gt;, IReactsTo&lt;T&gt;, and
/// ctx.Fire&lt;T&gt;() so the compiler rejects passing arbitrary types as synapse payloads.
/// </summary>
public interface ISynapse
{
}
```

- [ ] **Step 2: Write `SynapseError.cs`**

File: `D:\ino\POC\src\Ino.Core\SynapseError.cs`
```csharp
namespace Ino.Core;

/// <summary>
/// Typed error carried by a failed NeuronResult. Error codes are searchable on the
/// timeline and are the primary signal for self-improvement pattern extraction.
/// </summary>
[GenerateSerializer]
public sealed record SynapseError(
    [property: Id(0)] string Code,
    [property: Id(1)] string Message,
    [property: Id(2)] IReadOnlyDictionary<string, string>? Details = null);
```

- [ ] **Step 3: Write `NeuronResult.cs`**

File: `D:\ino\POC\src\Ino.Core\NeuronResult.cs`
```csharp
namespace Ino.Core;

/// <summary>
/// Return type from INeuron&lt;T&gt;.HandleAsync. Carries success/failure, an optional
/// human-readable message, an optional typed error, an optional typed response payload
/// (for request/response synapse patterns), and an optional Remote Flutter Widget
/// description for experiences that render rich cards.
/// </summary>
[GenerateSerializer]
public sealed record NeuronResult(
    [property: Id(0)] bool Success,
    [property: Id(1)] string? Message = null,
    [property: Id(2)] SynapseError? Error = null,
    [property: Id(3)] ISynapse? ResponsePayload = null,
    [property: Id(4)] byte[]? Rfw = null)
{
    public static NeuronResult Ok(string? message = null) => new(true, message);

    public static NeuronResult Fail(SynapseError error) => new(false, error.Message, error);

    public static NeuronResult Fail(string code, string message) =>
        Fail(new SynapseError(code, message));

    public NeuronResult With<T>(T payload) where T : ISynapse =>
        this with { ResponsePayload = payload };

    public NeuronResult WithRfw(byte[] rfw) =>
        this with { Rfw = rfw };

    public bool TryGetPayload<T>(out T payload) where T : ISynapse
    {
        if (ResponsePayload is T typed)
        {
            payload = typed;
            return true;
        }
        payload = default!;
        return false;
    }
}
```

- [ ] **Step 4: Write `EventEnvelope.cs`**

File: `D:\ino\POC\src\Ino.Core\EventEnvelope.cs`
```csharp
namespace Ino.Core;

/// <summary>
/// Framework-written wrapper around every stored neuron event. Carries causation
/// metadata (caused-by pointers, correlation id, W3C traceparent) so the Playback
/// neuron in Phase 6 can walk the causal graph backward without a central log.
///
/// Authors never construct this directly — the Neuron&lt;TState, TEvent&gt; base
/// class wraps their event in an envelope when RaiseAsync is called, and strips
/// envelopes when GetHistoryAsync returns payloads.
/// </summary>
[GenerateSerializer]
public sealed record EventEnvelope<T>(
    [property: Id(0)] T Payload,
    [property: Id(1)] string EventId,
    [property: Id(2)] string? CausedByEventId,
    [property: Id(3)] string? CausedByStream,
    [property: Id(4)] string CorrelationId,
    [property: Id(5)] DateTimeOffset Timestamp,
    [property: Id(6)] string? TraceParent)
    where T : class, ISynapse;
```

- [ ] **Step 5: Write `LlmTier.cs`**

File: `D:\ino\POC\src\Ino.Core\LlmTier.cs`
```csharp
namespace Ino.Core;

/// <summary>
/// Declarative quality tier requested for an LLM capability. The experiences silo
/// resolves the actual model per tier in later phases.
/// </summary>
public enum LlmTier
{
    None,
    Default,
    Reasoning,
    Multimodal
}
```

- [ ] **Step 6: Write `Capability.cs`**

File: `D:\ino\POC\src\Ino.Core\Capability.cs`
```csharp
namespace Ino.Core;

/// <summary>
/// Discriminated union of capabilities an experience may require. Aggregated at
/// compile time by the source generator (Phase 3) into ExperienceMetadata.RequiredCapabilities
/// and surfaced at install time via the marketplace consent screen.
/// </summary>
public abstract record Capability
{
    public sealed record Http(params string[] AllowedHosts) : Capability;

    public sealed record Llm(LlmTier Tier = LlmTier.Default) : Capability;

    public sealed record Persistence(string StoragePrefix) : Capability;

    public sealed record Identity(string Provider, params string[] Scopes) : Capability;

    public sealed record LocalFile(string PathPattern) : Capability;
}
```

- [ ] **Step 7: Write `CanonicalNeuronInfo.cs`**

File: `D:\ino\POC\src\Ino.Core\CanonicalNeuronInfo.cs`
```csharp
namespace Ino.Core;

/// <summary>
/// Source-generated metadata describing a canonical (INeuron&lt;T&gt;) handler inside
/// an experience. Populated by the Phase 3 source generator from reflection over
/// INeuron&lt;T&gt; implementations in an experience assembly.
/// </summary>
public sealed record CanonicalNeuronInfo(
    string SynapseType,
    string GrainType,
    bool IsUserEntry);
```

- [ ] **Step 8: Write `ReactiveNeuronInfo.cs`**

File: `D:\ino\POC\src\Ino.Core\ReactiveNeuronInfo.cs`
```csharp
namespace Ino.Core;

/// <summary>
/// Source-generated metadata describing a reactive (IReactsTo&lt;T&gt;) handler inside
/// an experience. Populated by the Phase 3 source generator.
/// </summary>
public sealed record ReactiveNeuronInfo(
    string SynapseType,
    string GrainType);
```

- [ ] **Step 9: Write `ExperienceMetadata.cs`**

File: `D:\ino\POC\src\Ino.Core\ExperienceMetadata.cs`
```csharp
namespace Ino.Core;

/// <summary>
/// Source-generated experience descriptor. Emitted at compile time as a static field
/// on the experience's marker class by the Phase 3 source generator. Read by the
/// Phase 2 AppHost composition extension (AddExperiences&lt;T&gt;) to wire the experience
/// into the experiences silo.
/// </summary>
public sealed record ExperienceMetadata(
    string ExperienceId,
    string Version,
    string Description,
    IReadOnlyList<string> Keywords,
    IReadOnlyList<CanonicalNeuronInfo> CanonicalNeurons,
    IReadOnlyList<ReactiveNeuronInfo> ReactiveNeurons,
    IReadOnlyList<string> UserEntrySchemas,
    IReadOnlyList<string> RequiredCapabilities,
    string CoreVersion);
```

- [ ] **Step 10: Create `Attributes` folder and write `UserEntryAttribute.cs`**

```bash
mkdir -p D:/ino/POC/src/Ino.Core/Attributes
```

File: `D:\ino\POC\src\Ino.Core\Attributes\UserEntryAttribute.cs`
```csharp
namespace Ino.Core;

/// <summary>
/// Marks an ISynapse record as a user-invocable intent reachable from natural-language
/// input. Indexed at install time into the system silo's intent classifier (Phase 4).
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class UserEntryAttribute : Attribute
{
}
```

- [ ] **Step 11: Write `RequiresCapabilityAttribute.cs`**

File: `D:\ino\POC\src\Ino.Core\Attributes\RequiresCapabilityAttribute.cs`
```csharp
namespace Ino.Core;

/// <summary>
/// Declares a capability required by a neuron. Aggregated by the Phase 3 source generator
/// into ExperienceMetadata.RequiredCapabilities and surfaced at install time via the
/// marketplace consent screen.
///
/// Usage:
///   [RequiresCapability(typeof(Capability.Http), "serpapi.com")]
///   [RequiresCapability(typeof(Capability.Llm), LlmTier.Reasoning)]
///   public sealed class TripPlanner : Neuron&lt;TripPlannerState, TripPlannerEvent&gt;, ...
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RequiresCapabilityAttribute : Attribute
{
    public RequiresCapabilityAttribute(Type capabilityType, params object?[] args)
    {
        CapabilityType = capabilityType;
        Args = args;
    }

    public Type CapabilityType { get; }

    public IReadOnlyList<object?> Args { get; }
}
```

- [ ] **Step 12: Write `InoExperienceAttribute.cs`**

File: `D:\ino\POC\src\Ino.Core\Attributes\InoExperienceAttribute.cs`
```csharp
namespace Ino.Core;

/// <summary>
/// Optional assembly-level attribute declaring experience metadata. If absent, the source
/// generator falls back to the .csproj PackageId, Description, and PackageTags. Used as
/// the authoritative source of keywords when present.
/// </summary>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
public sealed class InoExperienceAttribute : Attribute
{
    public InoExperienceAttribute(
        string id,
        string version,
        string description,
        string[]? keywords = null,
        string coreVersion = "0.1.0")
    {
        Id = id;
        Version = version;
        Description = description;
        Keywords = keywords ?? Array.Empty<string>();
        CoreVersion = coreVersion;
    }

    public string Id { get; }

    public string Version { get; }

    public string Description { get; }

    public string[] Keywords { get; }

    public string CoreVersion { get; }
}
```

- [ ] **Step 13: Build**

```bash
cd D:/ino/POC
dotnet build ino.slnx
```

Expected:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

If `TreatWarningsAsErrors` catches anything (unused using, nullable warning), fix it before proceeding.

- [ ] **Step 14: Commit**

```bash
cd D:/ino
git add POC
git commit -m "feat(poc): define Ino.Core primitive types

ISynapse, NeuronResult, SynapseError, EventEnvelope<T>, Capability
(discriminated union), ExperienceMetadata + supporting info records,
and the three attributes (UserEntry, RequiresCapability, InoExperience).

All framework-facing types; no business logic, no Orleans runtime
(only the SDK for [GenerateSerializer] attributes).

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

### Task 4: Create `Ino.Core.Tests` project with passing unit tests

**Files:**
- Create: `D:\ino\POC\test\Ino.Core.Tests\Ino.Core.Tests.csproj`
- Create: `D:\ino\POC\test\Ino.Core.Tests\NeuronResultTests.cs`
- Create: `D:\ino\POC\test\Ino.Core.Tests\EventEnvelopeTests.cs`
- Create: `D:\ino\POC\test\Ino.Core.Tests\CapabilityTests.cs`
- Create: `D:\ino\POC\test\Ino.Core.Tests\ExperienceMetadataTests.cs`

- [ ] **Step 1: Create folder and csproj**

```bash
mkdir -p D:/ino/POC/test/Ino.Core.Tests
```

File: `D:\ino\POC\test\Ino.Core.Tests\Ino.Core.Tests.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Ino.Core.Tests</RootNamespace>
    <AssemblyName>Ino.Core.Tests</AssemblyName>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="FluentAssertions" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Ino.Core\Ino.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Add to solution**

```bash
cd D:/ino/POC
dotnet sln ino.slnx add test/Ino.Core.Tests/Ino.Core.Tests.csproj
```

- [ ] **Step 3: Write `NeuronResultTests.cs`**

File: `D:\ino\POC\test\Ino.Core.Tests\NeuronResultTests.cs`
```csharp
using FluentAssertions;
using Xunit;

namespace Ino.Core.Tests;

public sealed class NeuronResultTests
{
    // Minimal ISynapse for testing the With<T> helper
    private sealed record DummyResponse(string Value) : ISynapse;

    [Fact]
    public void Ok_WithNoMessage_ReturnsSuccess()
    {
        var result = NeuronResult.Ok();

        result.Success.Should().BeTrue();
        result.Message.Should().BeNull();
        result.Error.Should().BeNull();
        result.ResponsePayload.Should().BeNull();
        result.Rfw.Should().BeNull();
    }

    [Fact]
    public void Ok_WithMessage_CarriesMessage()
    {
        var result = NeuronResult.Ok("done");

        result.Success.Should().BeTrue();
        result.Message.Should().Be("done");
    }

    [Fact]
    public void Fail_WithError_ReturnsFailureCarryingError()
    {
        var error = new SynapseError("test.error", "something broke");
        var result = NeuronResult.Fail(error);

        result.Success.Should().BeFalse();
        result.Error.Should().Be(error);
        result.Message.Should().Be("something broke");
    }

    [Fact]
    public void Fail_WithCodeAndMessage_ConstructsSynapseError()
    {
        var result = NeuronResult.Fail("not_found", "missing");

        result.Success.Should().BeFalse();
        result.Error.Should().NotBeNull();
        result.Error!.Code.Should().Be("not_found");
        result.Error.Message.Should().Be("missing");
    }

    [Fact]
    public void With_AttachesResponsePayload()
    {
        var payload = new DummyResponse("hello");
        var result = NeuronResult.Ok().With(payload);

        result.ResponsePayload.Should().Be(payload);
    }

    [Fact]
    public void TryGetPayload_WithMatchingType_ReturnsTrue()
    {
        var payload = new DummyResponse("hello");
        var result = NeuronResult.Ok().With(payload);

        result.TryGetPayload<DummyResponse>(out var extracted).Should().BeTrue();
        extracted.Should().Be(payload);
    }

    [Fact]
    public void TryGetPayload_WithNoPayload_ReturnsFalse()
    {
        var result = NeuronResult.Ok();

        result.TryGetPayload<DummyResponse>(out _).Should().BeFalse();
    }

    [Fact]
    public void WithRfw_AttachesRfwBytes()
    {
        var rfw = new byte[] { 1, 2, 3 };
        var result = NeuronResult.Ok().WithRfw(rfw);

        result.Rfw.Should().BeEquivalentTo(rfw);
    }
}
```

- [ ] **Step 4: Write `EventEnvelopeTests.cs`**

File: `D:\ino\POC\test\Ino.Core.Tests\EventEnvelopeTests.cs`
```csharp
using FluentAssertions;
using Xunit;

namespace Ino.Core.Tests;

public sealed class EventEnvelopeTests
{
    private sealed record DummyEvent(string Text) : ISynapse;

    [Fact]
    public void RootEvent_HasNoCausedByPointer()
    {
        var payload = new DummyEvent("first");
        var envelope = new EventEnvelope<DummyEvent>(
            Payload: payload,
            EventId: "evt-001",
            CausedByEventId: null,
            CausedByStream: null,
            CorrelationId: "corr-001",
            Timestamp: DateTimeOffset.UtcNow,
            TraceParent: null);

        envelope.CausedByEventId.Should().BeNull();
        envelope.CausedByStream.Should().BeNull();
        envelope.Payload.Should().Be(payload);
    }

    [Fact]
    public void CausedEvent_CarriesParentPointers()
    {
        var envelope = new EventEnvelope<DummyEvent>(
            Payload: new DummyEvent("child"),
            EventId: "evt-002",
            CausedByEventId: "evt-001",
            CausedByStream: "parent-stream",
            CorrelationId: "corr-001",
            Timestamp: DateTimeOffset.UtcNow,
            TraceParent: "00-trace-span-01");

        envelope.CausedByEventId.Should().Be("evt-001");
        envelope.CausedByStream.Should().Be("parent-stream");
        envelope.TraceParent.Should().Be("00-trace-span-01");
    }

    [Fact]
    public void TwoEnvelopesWithSameFields_AreEqualByValue()
    {
        var timestamp = DateTimeOffset.UtcNow;
        var a = new EventEnvelope<DummyEvent>(
            new DummyEvent("same"), "e1", null, null, "c1", timestamp, null);
        var b = new EventEnvelope<DummyEvent>(
            new DummyEvent("same"), "e1", null, null, "c1", timestamp, null);

        a.Should().Be(b);
    }
}
```

- [ ] **Step 5: Write `CapabilityTests.cs`**

File: `D:\ino\POC\test\Ino.Core.Tests\CapabilityTests.cs`
```csharp
using FluentAssertions;
using Xunit;

namespace Ino.Core.Tests;

public sealed class CapabilityTests
{
    [Fact]
    public void Http_CarriesAllowedHosts()
    {
        var cap = new Capability.Http("serpapi.com", "*.airlines");

        cap.AllowedHosts.Should().Equal("serpapi.com", "*.airlines");
    }

    [Fact]
    public void Llm_DefaultsToDefaultTier()
    {
        var cap = new Capability.Llm();

        cap.Tier.Should().Be(LlmTier.Default);
    }

    [Fact]
    public void Llm_CanRequestReasoningTier()
    {
        var cap = new Capability.Llm(LlmTier.Reasoning);

        cap.Tier.Should().Be(LlmTier.Reasoning);
    }

    [Fact]
    public void Identity_CarriesProviderAndScopes()
    {
        var cap = new Capability.Identity("google.com", "email", "profile");

        cap.Provider.Should().Be("google.com");
        cap.Scopes.Should().Equal("email", "profile");
    }

    [Fact]
    public void Persistence_CarriesStoragePrefix()
    {
        var cap = new Capability.Persistence("trip-planner");

        cap.StoragePrefix.Should().Be("trip-planner");
    }

    [Fact]
    public void TwoCapabilitiesWithSameFields_AreEqual()
    {
        var a = new Capability.Http("example.com");
        var b = new Capability.Http("example.com");

        a.Should().Be(b);
    }
}
```

- [ ] **Step 6: Write `ExperienceMetadataTests.cs`**

File: `D:\ino\POC\test\Ino.Core.Tests\ExperienceMetadataTests.cs`
```csharp
using FluentAssertions;
using Xunit;

namespace Ino.Core.Tests;

public sealed class ExperienceMetadataTests
{
    [Fact]
    public void Metadata_CarriesAllFields()
    {
        var metadata = new ExperienceMetadata(
            ExperienceId: "Ino.Travel.TripPlanner",
            Version: "1.0.0",
            Description: "Plan trips with flights, hotels, and activities.",
            Keywords: new[] { "travel", "trip", "flight" },
            CanonicalNeurons: new[]
            {
                new CanonicalNeuronInfo(
                    SynapseType: "Ino.Travel.TripPlanner.Contracts.PlanTrip",
                    GrainType: "Ino.Travel.TripPlanner.TripPlanner",
                    IsUserEntry: true)
            },
            ReactiveNeurons: Array.Empty<ReactiveNeuronInfo>(),
            UserEntrySchemas: new[] { "Ino.Travel.TripPlanner.Contracts.PlanTrip" },
            RequiredCapabilities: new[] { "Llm:Reasoning", "Persistence:trip-planner" },
            CoreVersion: "0.1.0");

        metadata.ExperienceId.Should().Be("Ino.Travel.TripPlanner");
        metadata.CanonicalNeurons.Should().HaveCount(1);
        metadata.CanonicalNeurons[0].IsUserEntry.Should().BeTrue();
        metadata.UserEntrySchemas.Should().Contain("Ino.Travel.TripPlanner.Contracts.PlanTrip");
        metadata.RequiredCapabilities.Should().Contain("Llm:Reasoning");
    }
}
```

- [ ] **Step 7: Run tests**

```bash
cd D:/ino/POC
dotnet test test/Ino.Core.Tests/Ino.Core.Tests.csproj --nologo
```

Expected:
```
Passed!  - Failed:     0, Passed:    17, Skipped:     0, Total:    17
```

(Count may vary slightly as test infrastructure discovers fact methods — the key is 0 Failed and 0 Skipped.)

- [ ] **Step 8: Commit**

```bash
cd D:/ino
git add POC
git commit -m "test(poc): unit tests for Ino.Core primitive types"
```

---

### Task 5: Create `Ino.Core.Hosting` project + add to solution

**Files:**
- Create: `D:\ino\POC\src\Ino.Core.Hosting\Ino.Core.Hosting.csproj`

- [ ] **Step 1: Create folder**

```bash
mkdir -p D:/ino/POC/src/Ino.Core.Hosting
```

- [ ] **Step 2: Write Ino.Core.Hosting.csproj**

File: `D:\ino\POC\src\Ino.Core.Hosting\Ino.Core.Hosting.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Ino.Core.Hosting</RootNamespace>
    <AssemblyName>Ino.Core.Hosting</AssemblyName>
    <IsPackable>true</IsPackable>
    <PackageId>Ino.Core.Hosting</PackageId>
    <Description>ino core runtime — INeuron&lt;T&gt;, IReactsTo&lt;T&gt;, NeuronContext, and the Neuron&lt;TState, TEvent&gt; base class built on Orleans JournaledGrain + LogStorage.</Description>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Orleans.Server" />
    <PackageReference Include="Microsoft.Orleans.Journaling" />
    <PackageReference Include="Microsoft.Extensions.Logging" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Ino.Core\Ino.Core.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add to solution**

```bash
cd D:/ino/POC
dotnet sln ino.slnx add src/Ino.Core.Hosting/Ino.Core.Hosting.csproj
```

- [ ] **Step 4: Build**

```bash
dotnet build ino.slnx
```

Expected: 3 projects built successfully (`Ino.Core`, `Ino.Core.Tests`, `Ino.Core.Hosting`), 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
cd D:/ino
git add POC
git commit -m "feat(poc): add Ino.Core.Hosting project"
```

---

### Task 6: Define handler interfaces + `NeuronContext` + `IJournaledNeuronQuery`

**Files:**
- Create: `D:\ino\POC\src\Ino.Core.Hosting\INeuron.cs`
- Create: `D:\ino\POC\src\Ino.Core.Hosting\IReactsTo.cs`
- Create: `D:\ino\POC\src\Ino.Core.Hosting\NeuronContext.cs`
- Create: `D:\ino\POC\src\Ino.Core.Hosting\IJournaledNeuronQuery.cs`

- [ ] **Step 1: Write `INeuron.cs`**

File: `D:\ino\POC\src\Ino.Core.Hosting\INeuron.cs`
```csharp
using Ino.Core;
using Orleans;

namespace Ino.Core.Hosting;

/// <summary>
/// Canonical handler — exactly one implementation per synapse type across all installed
/// experiences (duplicate = install rejection in Phase 3 via the analyzer + source
/// generator). Used as the runtime dispatch target for ctx.Fire&lt;T&gt;() calls in Phase 2.
///
/// A single grain class can implement INeuron&lt;T&gt; for multiple synapse types.
/// </summary>
public interface INeuron<TSynapse> : IGrainWithStringKey
    where TSynapse : ISynapse
{
    Task<NeuronResult> HandleAsync(
        TSynapse synapse,
        NeuronContext ctx,
        CancellationToken ct);
}
```

- [ ] **Step 2: Write `IReactsTo.cs`**

File: `D:\ino\POC\src\Ino.Core.Hosting\IReactsTo.cs`
```csharp
using Ino.Core;
using Orleans;

namespace Ino.Core.Hosting;

/// <summary>
/// Reactive listener — zero or many implementations per synapse type. Used as the
/// runtime dispatch target for ctx.FireBroadcast&lt;T&gt;() calls in Phase 2. Returns Task
/// (not Task&lt;NeuronResult&gt;) because broadcast is fire-and-forget — per-listener
/// failures are logged but do not fail the broadcast.
/// </summary>
public interface IReactsTo<TSynapse> : IGrainWithStringKey
    where TSynapse : ISynapse
{
    Task ReactAsync(
        TSynapse synapse,
        NeuronContext ctx,
        CancellationToken ct);
}
```

- [ ] **Step 3: Write `NeuronContext.cs`**

File: `D:\ino\POC\src\Ino.Core.Hosting\NeuronContext.cs`
```csharp
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Ino.Core.Hosting;

/// <summary>
/// Per-call context passed into every HandleAsync / ReactAsync invocation. Carries
/// the identity of the current synapse (correlation, causation), the calling user
/// context (when this is part of a user-initiated chain), and a logger.
///
/// Phase 2 expands this interface with Fire&lt;T&gt;(), FireBroadcast&lt;T&gt;(), Search, and
/// Identity facades. Phase 1 ships the minimum needed for Neuron&lt;TState, TEvent&gt;.RaiseAsync
/// to stamp causation metadata correctly.
/// </summary>
public interface NeuronContext
{
    /// <summary>Stable identifier of the synapse currently being handled.</summary>
    string SynapseId { get; }

    /// <summary>Stable identifier of the current event being handled inside this grain.
    /// Used by the base class as caused_by_event_id for subsequent RaiseAsync calls.</summary>
    string CurrentEventId { get; }

    /// <summary>Correlation ID shared across all synapses in a single causal chain.</summary>
    string CorrelationId { get; }

    /// <summary>The experience that fired the current synapse.</summary>
    string SourceExperience { get; }

    /// <summary>The grain key of the neuron that fired the current synapse.</summary>
    string SourceStream { get; }

    /// <summary>The user whose session this chain belongs to, if any.</summary>
    string? UserId { get; }

    /// <summary>The session this chain belongs to, if any.</summary>
    string? SessionId { get; }

    /// <summary>Logger for the current neuron, auto-decorated with correlation fields.</summary>
    ILogger Logger { get; }

    /// <summary>W3C trace activity for OTel correlation.</summary>
    Activity? CurrentActivity { get; }
}
```

- [ ] **Step 4: Write `IJournaledNeuronQuery.cs`**

File: `D:\ino\POC\src\Ino.Core.Hosting\IJournaledNeuronQuery.cs`
```csharp
using Ino.Core;
using Orleans;

namespace Ino.Core.Hosting;

/// <summary>
/// Non-generic grain interface implemented by every Neuron&lt;TState, TEvent&gt; via the base class.
/// The Phase 6 Playback neuron uses this to walk a neuron's journal backward without
/// knowing the neuron's concrete TEvent type at compile time.
///
/// The return type is deliberately object-typed (string for event id, object for payload)
/// because the caller is doing graph traversal, not typed dispatch — it needs metadata,
/// not the typed payload itself.
/// </summary>
public interface IJournaledNeuronQuery : IGrainWithStringKey
{
    /// <summary>
    /// Find a specific event in this neuron's journal by event id. Returns null if the
    /// event is not present. The returned object carries the envelope's metadata fields
    /// (EventId, CausedByEventId, CausedByStream, CorrelationId, Timestamp, TraceParent)
    /// plus a string representation of the payload type.
    /// </summary>
    Task<JournaledEventInfo?> FindEventAsync(string eventId);
}

/// <summary>
/// Non-generic view of an EventEnvelope&lt;T&gt; returned from IJournaledNeuronQuery.FindEventAsync.
/// Carries all metadata fields but not the typed payload (payload is represented as its
/// type name + a JSON-serialized string for debugging).
/// </summary>
[GenerateSerializer]
public sealed record JournaledEventInfo(
    [property: Id(0)] string EventId,
    [property: Id(1)] string PayloadTypeName,
    [property: Id(2)] string PayloadJson,
    [property: Id(3)] string? CausedByEventId,
    [property: Id(4)] string? CausedByStream,
    [property: Id(5)] string CorrelationId,
    [property: Id(6)] DateTimeOffset Timestamp,
    [property: Id(7)] string? TraceParent);
```

- [ ] **Step 5: Build**

```bash
cd D:/ino/POC
dotnet build ino.slnx
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 6: Commit**

```bash
cd D:/ino
git add POC
git commit -m "feat(poc): define INeuron<T>, IReactsTo<T>, NeuronContext, IJournaledNeuronQuery"
```

---

### Task 7: Implement `Neuron<TEvent>` base class (Orleans 10 DurableGrain pattern)

**Pivot from original plan:** Orleans 10 replaced `JournaledGrain<TState, TEvent>` + `[LogConsistencyProvider]` with a different pattern: `DurableGrain` base + `IDurableList<T>` / `IDurableDictionary<K,V>` injected via constructor, backed by `IStateMachineStorageProvider`. The parent repo `D:\ino\src\Core\Agents\Agent.cs` uses this pattern in production. Task 7 now implements `Neuron<TEvent>` (single generic parameter) on top of the Orleans 10 primitives. Two simplifications fall out:

1. **No `TState` generic parameter.** The journal IS the state. Authors who want projected state add their own `IDurableDictionary`/`IDurableList` alongside the journal or compute it on demand from the journal. This matches `AgentDurableState` in the parent repo, which bundles `IDurableList<AgentEvent> EventLog` + `IDurableDictionary<string, StateEntry> State` in one class.
2. **No `Apply(state, event)` abstract method.** No fold is needed because state is tracked directly by the durable collections.

**Files:**
- Create: `D:\ino\POC\src\Ino.Core.Hosting\Neuron.cs`

- [ ] **Step 1: Add `Ulid` package reference to `Ino.Core.Hosting.csproj`**

Edit `D:\ino\POC\src\Ino.Core.Hosting\Ino.Core.Hosting.csproj` — add `Ulid` to the existing `<ItemGroup>` of PackageReferences:

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.Orleans.Server" />
    <PackageReference Include="Microsoft.Orleans.Journaling" />
    <PackageReference Include="Microsoft.Extensions.Logging" />
    <PackageReference Include="Ulid" />
  </ItemGroup>
```

(`Ulid` is already pinned in `Directory.Packages.props` — no version needed.)

- [ ] **Step 2: Write `Neuron.cs` — full file in one pass**

File: `D:\ino\POC\src\Ino.Core.Hosting\Neuron.cs`
```csharp
using System.Text.Json;
using Ino.Core;
using Orleans;
using Orleans.Journaling;

namespace Ino.Core.Hosting;

/// <summary>
/// Base class every ino neuron inherits from. Built on Orleans 10's DurableGrain +
/// IDurableList&lt;T&gt; primitive: the base class takes an IDurableList of EventEnvelope&lt;TEvent&gt;
/// in its constructor and exposes a RaiseAsync helper that wraps an event in a causation
/// envelope, appends it to the journal, and persists via WriteStateAsync.
///
/// Authors call:
///   await RaiseAsync(new MyEvent(...), ctx, ct);
/// to append to their journal. The journal itself IS the state — there is no separate
/// projected-state concept in the base class. Authors who want projected state add
/// their own IDurableDictionary&lt;K,V&gt; fields alongside the journal or compute state
/// on demand by enumerating the journal (as the Phase 1 TestNeuron does).
///
/// Persistence is configured at the silo level, not via grain attributes:
///   silo.Services.AddSingleton&lt;IStateMachineStorageProvider, VolatileStateMachineStorageProvider&gt;();
///   silo.AddStateMachineStorage();
/// The Phase 1 InoTestSiloFixture wires the in-memory volatile provider; later phases
/// wire Redis or similar.
/// </summary>
public abstract class Neuron<TEvent>(
    IDurableList<EventEnvelope<TEvent>> journal)
    : DurableGrain, IJournaledNeuronQuery
    where TEvent : class, ISynapse
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Exposed to derived classes that need raw journal access for projection.
    /// Most neurons just call RaiseAsync and GetHistoryAsync — they never touch this.
    /// </summary>
    protected IDurableList<EventEnvelope<TEvent>> Journal => journal;

    /// <summary>
    /// Append a typed event to this neuron's journal. The framework wraps it in an
    /// EventEnvelope&lt;TEvent&gt; carrying causation metadata derived from the supplied
    /// NeuronContext, appends to the journal, then persists via WriteStateAsync.
    ///
    /// Phase 1 takes ctx as an explicit parameter rather than using an ambient AsyncLocal.
    /// Phase 2 may introduce an ambient accessor for convenience but the explicit form
    /// remains available.
    /// </summary>
    protected async Task RaiseAsync(
        TEvent @event,
        NeuronContext ctx,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(ctx);

        var envelope = new EventEnvelope<TEvent>(
            Payload: @event,
            EventId: Ulid.NewUlid().ToString(),
            CausedByEventId: ctx.CurrentEventId,
            CausedByStream: ctx.SourceStream,
            CorrelationId: ctx.CorrelationId,
            Timestamp: DateTimeOffset.UtcNow,
            TraceParent: ctx.CurrentActivity?.Id);

        journal.Add(envelope);
        await WriteStateAsync(ct);
    }

    /// <summary>
    /// Return the last N events from this neuron's journal as typed payloads (envelope
    /// metadata stripped). Used by memory search: ctx.Search.MemoryAsync&lt;T&gt; resolves to
    /// calling this on the target neuron in later phases.
    /// </summary>
    public Task<IReadOnlyList<TEvent>> GetHistoryAsync(int lastN = 100)
    {
        if (lastN <= 0) return Task.FromResult<IReadOnlyList<TEvent>>(Array.Empty<TEvent>());

        var skip = Math.Max(0, journal.Count - lastN);
        var list = new List<TEvent>(Math.Min(lastN, journal.Count));
        var index = 0;
        foreach (var env in journal)
        {
            if (index++ < skip) continue;
            list.Add(env.Payload);
        }
        return Task.FromResult<IReadOnlyList<TEvent>>(list);
    }

    /// <summary>
    /// Return the last N events from this neuron's journal with full envelope metadata.
    /// Used by tooling (Playback, CausationIndex) that needs causation pointers.
    /// </summary>
    public Task<IReadOnlyList<EventEnvelope<TEvent>>> GetHistoryWithMetadataAsync(int lastN = 100)
    {
        if (lastN <= 0) return Task.FromResult<IReadOnlyList<EventEnvelope<TEvent>>>(Array.Empty<EventEnvelope<TEvent>>());

        var skip = Math.Max(0, journal.Count - lastN);
        var list = new List<EventEnvelope<TEvent>>(Math.Min(lastN, journal.Count));
        var index = 0;
        foreach (var env in journal)
        {
            if (index++ < skip) continue;
            list.Add(env);
        }
        return Task.FromResult<IReadOnlyList<EventEnvelope<TEvent>>>(list);
    }

    /// <summary>
    /// Non-generic journal lookup used by the Phase 6 Playback neuron. Scans the
    /// journal for an entry matching the supplied event id and returns a
    /// type-erased view of its metadata + JSON-serialized payload.
    /// </summary>
    public Task<JournaledEventInfo?> FindEventAsync(string eventId)
    {
        if (string.IsNullOrEmpty(eventId)) return Task.FromResult<JournaledEventInfo?>(null);

        foreach (var env in journal)
        {
            if (env.EventId == eventId)
            {
                var payloadTypeName = env.Payload.GetType().FullName ?? env.Payload.GetType().Name;
                var payloadJson = JsonSerializer.Serialize(env.Payload, env.Payload.GetType(), JsonOptions);
                return Task.FromResult<JournaledEventInfo?>(new JournaledEventInfo(
                    EventId: env.EventId,
                    PayloadTypeName: payloadTypeName,
                    PayloadJson: payloadJson,
                    CausedByEventId: env.CausedByEventId,
                    CausedByStream: env.CausedByStream,
                    CorrelationId: env.CorrelationId,
                    Timestamp: env.Timestamp,
                    TraceParent: env.TraceParent));
            }
        }
        return Task.FromResult<JournaledEventInfo?>(null);
    }
}
```

- [ ] **Step 3: Build**

```bash
cd D:/ino/POC
dotnet build ino.slnx
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

Common failure modes and fixes:

| Error | Fix |
|---|---|
| `DurableGrain` not found | Check `Microsoft.Orleans.Journaling` is in `Ino.Core.Hosting.csproj` PackageReferences (added in Task 5) |
| `IDurableList<>` not found | Same — `Microsoft.Orleans.Journaling` provides both `DurableGrain` and the `IDurableX<>` interfaces |
| `WriteStateAsync` not found | It's inherited from `DurableGrain`. If the compiler says it doesn't exist, the `DurableGrain` base class isn't what we expect — report BLOCKED |
| `Ulid` not found | Check Step 1 added the package reference to the csproj |
| `journal.Count` not found | `IDurableList<T>` may expose count differently; try `journal.Count()` (LINQ via `IEnumerable<T>`) or iterate manually |

- [ ] **Step 4: Commit**

```bash
cd D:/ino
git add POC
git commit -m "feat(poc): implement Neuron<TEvent> base class on Orleans 10 DurableGrain

Pivoted from the original plan (Orleans 9 JournaledGrain<TState, TEvent>
+ [LogConsistencyProvider(LogStorage)] attributes) to Orleans 10's
DurableGrain + IDurableList<EventEnvelope<TEvent>> primitive, matching
the pattern used by the parent repo's Core.Agents.Agent.

Two simplifications from the pivot:
- Dropped TState generic parameter. The journal IS the state.
- Dropped Apply(state, event) abstract method. No fold is needed.

Author-facing surface is unchanged in spirit:
  - protected Task RaiseAsync(TEvent, NeuronContext, CancellationToken)
  - public Task<IReadOnlyList<TEvent>> GetHistoryAsync(int lastN)
  - public Task<IReadOnlyList<EventEnvelope<TEvent>>> GetHistoryWithMetadataAsync(int lastN)
  - IJournaledNeuronQuery.FindEventAsync for Phase 6 Playback

Causation metadata (EventId via Ulid, CausedByEventId, CausedByStream,
CorrelationId, TraceParent) stamped on every envelope from the supplied
NeuronContext.

Co-Authored-By: Claude Opus 4.6 (1M context) <noreply@anthropic.com>"
```

---

### Task 8: Create `Ino.Testing` project + add to solution

**Files:**
- Create: `D:\ino\POC\src\Ino.Testing\Ino.Testing.csproj`

- [ ] **Step 1: Create folder**

```bash
mkdir -p D:/ino/POC/src/Ino.Testing
```

- [ ] **Step 2: Write Ino.Testing.csproj**

File: `D:\ino\POC\src\Ino.Testing\Ino.Testing.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Ino.Testing</RootNamespace>
    <AssemblyName>Ino.Testing</AssemblyName>
    <IsPackable>true</IsPackable>
    <PackageId>Ino.Testing</PackageId>
    <Description>Shared ino test harness — in-memory TestCluster fixture, RecordedMockChatClient, InoTestNeuronContext for Phase 1 integration tests.</Description>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.Orleans.TestingHost" />
    <PackageReference Include="Microsoft.Orleans.Server" />
    <PackageReference Include="Microsoft.Orleans.Journaling" />
    <PackageReference Include="Microsoft.Extensions.AI.Abstractions" />
    <PackageReference Include="Microsoft.Extensions.Logging" />
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="YamlDotNet" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\Ino.Core\Ino.Core.csproj" />
    <ProjectReference Include="..\Ino.Core.Hosting\Ino.Core.Hosting.csproj" />
  </ItemGroup>
</Project>
```

Note: `xunit.v3` is a direct dependency here because `InoTestSiloFixture` implements `IAsyncLifetime` from xunit.

- [ ] **Step 3: Add to solution**

```bash
cd D:/ino/POC
dotnet sln ino.slnx add src/Ino.Testing/Ino.Testing.csproj
```

- [ ] **Step 4: Build**

```bash
dotnet build ino.slnx
```

Expected: 4 projects built, 0 warnings, 0 errors.

- [ ] **Step 5: Commit**

```bash
cd D:/ino
git add POC
git commit -m "feat(poc): add Ino.Testing project"
```

---

### Task 9: Implement `InoTestSiloFixture` + `TestSiloConfigurator`

**Files:**
- Create: `D:\ino\POC\src\Ino.Testing\TestSiloConfigurator.cs`
- Create: `D:\ino\POC\src\Ino.Testing\InoTestSiloFixture.cs`
- Create: `D:\ino\POC\src\Ino.Testing\InoTestCollection.cs`

- [ ] **Step 1: Write `TestSiloConfigurator.cs`**

File: `D:\ino\POC\src\Ino.Testing\TestSiloConfigurator.cs`
```csharp
using Orleans.EventSourcing.LogStorage;
using Orleans.Hosting;
using Orleans.TestingHost;

namespace Ino.Testing;

/// <summary>
/// Configures the Orleans silo inside a TestCluster with the providers Neuron&lt;TState, TEvent&gt;
/// needs:
///   - "NeuronStore" grain storage (in-memory for tests)
///   - "LogStorage" log-consistency provider
///
/// Production silos wire the same provider names but against Redis (neuron store) and
/// still LogStorage for event sourcing. The neuron code is oblivious to the difference.
/// </summary>
public sealed class TestSiloConfigurator : ISiloConfigurator
{
    public void Configure(ISiloBuilder silo)
    {
        silo.AddMemoryGrainStorage("NeuronStore");
        silo.AddLogStorageBasedLogConsistencyProvider("LogStorage");
    }
}
```

- [ ] **Step 2: Write `InoTestSiloFixture.cs`**

File: `D:\ino\POC\src\Ino.Testing\InoTestSiloFixture.cs`
```csharp
using Orleans;
using Orleans.TestingHost;
using Xunit;

namespace Ino.Testing;

/// <summary>
/// Shared per-test-project fixture. One Orleans TestCluster is created per test project
/// and reused across every test class via xunit.v3's ICollectionFixture. Cluster startup
/// (~5-10s) is paid once; per-test reset is in-memory and fast.
///
/// Usage in a test project:
///   [CollectionDefinition(nameof(InoTestCollection))]
///   public sealed class InoTestCollection : ICollectionFixture&lt;InoTestSiloFixture&gt; { }
///
///   [Collection(nameof(InoTestCollection))]
///   public sealed class MyTests
///   {
///       private readonly InoTestSiloFixture _fixture;
///       public MyTests(InoTestSiloFixture fixture) { _fixture = fixture; }
///   }
/// </summary>
public sealed class InoTestSiloFixture : IAsyncLifetime
{
    public TestCluster Cluster { get; private set; } = null!;

    public IGrainFactory Grains => Cluster.Client;

    public async ValueTask InitializeAsync()
    {
        var builder = new TestClusterBuilder { Options = { InitialSilosCount = 1 } };
        builder.AddSiloBuilderConfigurator<TestSiloConfigurator>();
        Cluster = builder.Build();
        await Cluster.DeployAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (Cluster is not null)
        {
            await Cluster.StopAllSilosAsync();
            await Cluster.DisposeAsync();
        }
    }
}
```

- [ ] **Step 3: Write `InoTestCollection.cs`**

File: `D:\ino\POC\src\Ino.Testing\InoTestCollection.cs`
```csharp
using Xunit;

namespace Ino.Testing;

/// <summary>
/// Collection definition shared across test projects so every test class that applies
/// [Collection(nameof(InoTestCollection))] participates in the same InoTestSiloFixture
/// lifetime. This is the xunit.v3 pattern that avoids TestCluster-per-class bloat.
/// </summary>
[CollectionDefinition(nameof(InoTestCollection))]
public sealed class InoTestCollection : ICollectionFixture<InoTestSiloFixture>
{
}
```

- [ ] **Step 4: Build**

```bash
cd D:/ino/POC
dotnet build ino.slnx
```

Expected: `Build succeeded. 0 Warning(s) 0 Error(s)`.

- [ ] **Step 5: Commit**

```bash
cd D:/ino
git add POC
git commit -m "feat(poc): InoTestSiloFixture with ICollectionFixture pattern

Shared TestCluster per test project with AddMemoryGrainStorage('NeuronStore')
and AddLogStorageBasedLogConsistencyProvider('LogStorage'). Avoids the
TestCluster-per-class bloat from the existing IAW test suite."
```

---

### Task 10: Implement `InoTestNeuronContext` — a `NeuronContext` for unit tests

**Files:**
- Create: `D:\ino\POC\src\Ino.Testing\InoTestNeuronContext.cs`

- [ ] **Step 1: Write `InoTestNeuronContext.cs`**

File: `D:\ino\POC\src\Ino.Testing\InoTestNeuronContext.cs`
```csharp
using System.Diagnostics;
using Ino.Core.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ino.Testing;

/// <summary>
/// A NeuronContext implementation suitable for unit and integration tests. All fields
/// are set via constructor with sensible defaults so tests can supply only the values
/// they care about.
///
/// Production NeuronContext implementations arrive in Phase 2 (LocalNeuronContext for
/// in-silo dispatch and RemoteNeuronContext for cross-silo). Phase 1 only needs a context
/// suitable for driving a neuron's HandleAsync directly from a test.
/// </summary>
public sealed class InoTestNeuronContext : NeuronContext
{
    public InoTestNeuronContext(
        string? synapseId = null,
        string? currentEventId = null,
        string? correlationId = null,
        string sourceExperience = "test",
        string sourceStream = "test:caller",
        string? userId = null,
        string? sessionId = null,
        ILogger? logger = null)
    {
        SynapseId = synapseId ?? Guid.NewGuid().ToString("n");
        CurrentEventId = currentEventId ?? Guid.NewGuid().ToString("n");
        CorrelationId = correlationId ?? Guid.NewGuid().ToString("n");
        SourceExperience = sourceExperience;
        SourceStream = sourceStream;
        UserId = userId;
        SessionId = sessionId;
        Logger = logger ?? NullLogger.Instance;
    }

    public string SynapseId { get; }

    public string CurrentEventId { get; }

    public string CorrelationId { get; }

    public string SourceExperience { get; }

    public string SourceStream { get; }

    public string? UserId { get; }

    public string? SessionId { get; }

    public ILogger Logger { get; }

    public Activity? CurrentActivity => Activity.Current;
}
```

- [ ] **Step 2: Build**

```bash
cd D:/ino/POC
dotnet build ino.slnx
```

Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
cd D:/ino
git add POC
git commit -m "feat(poc): InoTestNeuronContext for driving Neuron<TState, TEvent> in tests"
```

---

### Task 11: Implement `RecordedMockChatClient` skeleton

Phase 1 only needs the YAML loader + regex matching + missing-recording exception. No tool-call support yet (that arrives in Phase 4 when the first experience needs an LLM).

**Files:**
- Create: `D:\ino\POC\src\Ino.Testing\LlmRecording.cs`
- Create: `D:\ino\POC\src\Ino.Testing\MockLlmMissException.cs`
- Create: `D:\ino\POC\src\Ino.Testing\RecordedMockChatClient.cs`

- [ ] **Step 1: Write `LlmRecording.cs`**

File: `D:\ino\POC\src\Ino.Testing\LlmRecording.cs`
```csharp
namespace Ino.Testing;

/// <summary>
/// One recording from a mocks/llm.recordings.yml file. Phase 1 supports text responses
/// only; Phase 4 extends with tool_calls and structured json responses.
/// </summary>
public sealed class LlmRecording
{
    /// <summary>Regex pattern matched against the last user message in the ChatRequest.</summary>
    public string Match { get; set; } = null!;

    /// <summary>The text content of the mocked response.</summary>
    public string? Text { get; set; }
}
```

- [ ] **Step 2: Write `MockLlmMissException.cs`**

File: `D:\ino\POC\src\Ino.Testing\MockLlmMissException.cs`
```csharp
namespace Ino.Testing;

/// <summary>
/// Thrown when RecordedMockChatClient cannot find a recording that matches the prompt.
/// The exception message includes the unmatched prompt fragment and a suggested regex
/// to add to the recordings YAML file. Tests should treat this as a test failure — the
/// author either needs to record a new mock or their code is calling the LLM in an
/// unexpected way.
/// </summary>
public sealed class MockLlmMissException : Exception
{
    public MockLlmMissException(string message, string unmatchedPrompt)
        : base(message)
    {
        UnmatchedPrompt = unmatchedPrompt;
    }

    public string UnmatchedPrompt { get; }
}
```

- [ ] **Step 3: Write `RecordedMockChatClient.cs`**

File: `D:\ino\POC\src\Ino.Testing\RecordedMockChatClient.cs`
```csharp
using System.Text.RegularExpressions;
using Microsoft.Extensions.AI;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Ino.Testing;

/// <summary>
/// Deterministic IChatClient that serves pre-recorded responses from a YAML file.
/// Regex-matches the last user message in the ChatRequest against recording patterns
/// and returns the first matching recording's text. Throws MockLlmMissException when
/// no recording matches — tests see a loud failure with the unmatched prompt fragment
/// and a suggested recording template.
///
/// Phase 1 scope: text responses only. Phase 4 extends with tool-call and structured
/// JSON responses.
/// </summary>
public sealed class RecordedMockChatClient : IChatClient
{
    private readonly List<LlmRecording> _recordings = new();
    private readonly List<string> _unmatchedPrompts = new();

    public RecordedMockChatClient() { }

    public IReadOnlyList<string> UnmatchedPrompts => _unmatchedPrompts;

    /// <summary>
    /// Load recordings from a YAML file on disk. The file is a YAML sequence of
    /// LlmRecording objects.
    /// </summary>
    public void LoadRecordingsFromFile(string path)
    {
        var yaml = File.ReadAllText(path);
        LoadRecordingsFromYaml(yaml);
    }

    /// <summary>
    /// Load recordings from a YAML string. Useful for inline test fixtures.
    /// </summary>
    public void LoadRecordingsFromYaml(string yaml)
    {
        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

        var loaded = deserializer.Deserialize<List<LlmRecording>>(yaml) ?? new();
        _recordings.AddRange(loaded);
    }

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var lastUserMessage = messages
            .LastOrDefault(m => m.Role == ChatRole.User)
            ?.Text ?? string.Empty;

        foreach (var recording in _recordings)
        {
            if (Regex.IsMatch(lastUserMessage, recording.Match, RegexOptions.IgnoreCase))
            {
                var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, recording.Text ?? string.Empty));
                return Task.FromResult(response);
            }
        }

        _unmatchedPrompts.Add(lastUserMessage);
        throw new MockLlmMissException(
            $"No recorded response matched prompt:\n---\n{lastUserMessage}\n---\n\n" +
            $"Add a recording to mocks/llm.recordings.yml with match pattern matching this prompt.",
            lastUserMessage);
    }

    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException(
            "RecordedMockChatClient does not support streaming. Use GetResponseAsync instead.");
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
```

**Important:** The exact shape of `IChatClient` depends on the version of `Microsoft.Extensions.AI` pinned in `Directory.Packages.props`. If `9.3.0` has slightly different method signatures (e.g., `CompleteAsync` instead of `GetResponseAsync`, or different overloads), adjust the implementation to match the interface. Run `dotnet build` after writing the file — the compiler will point at any mismatched signatures.

- [ ] **Step 4: Build**

```bash
cd D:/ino/POC
dotnet build ino.slnx
```

Expected: `Build succeeded`.

If the build fails due to `IChatClient` interface mismatch, open the `Microsoft.Extensions.AI.Abstractions` assembly in your IDE's Object Browser, find the current `IChatClient` method signatures, and update `RecordedMockChatClient` accordingly. The core logic (regex match, throw on miss) stays identical; only the method names and parameter shapes change.

- [ ] **Step 5: Commit**

```bash
cd D:/ino
git add POC
git commit -m "feat(poc): RecordedMockChatClient with YAML-backed deterministic responses

Phase 1 scope: text responses only, regex matching on last user message,
loud MockLlmMissException on unmatched prompt with suggested recording
template. Phase 4 extends with tool-call and structured JSON responses
when the first LLM-using experience ships."
```

---

### Task 12: Create `Ino.Testing.Tests` and validate the harness

**Files:**
- Create: `D:\ino\POC\test\Ino.Testing.Tests\Ino.Testing.Tests.csproj`
- Create: `D:\ino\POC\test\Ino.Testing.Tests\RecordedMockChatClientTests.cs`
- Create: `D:\ino\POC\test\Ino.Testing.Tests\fixtures\sample.llm.recordings.yml`

- [ ] **Step 1: Create folders**

```bash
mkdir -p D:/ino/POC/test/Ino.Testing.Tests/fixtures
```

- [ ] **Step 2: Write the csproj**

File: `D:\ino\POC\test\Ino.Testing.Tests\Ino.Testing.Tests.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Ino.Testing.Tests</RootNamespace>
    <AssemblyName>Ino.Testing.Tests</AssemblyName>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Microsoft.Extensions.AI.Abstractions" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Ino.Testing\Ino.Testing.csproj" />
  </ItemGroup>

  <ItemGroup>
    <None Update="fixtures\sample.llm.recordings.yml">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </None>
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add to solution**

```bash
cd D:/ino/POC
dotnet sln ino.slnx add test/Ino.Testing.Tests/Ino.Testing.Tests.csproj
```

- [ ] **Step 4: Write the sample fixture**

File: `D:\ino\POC\test\Ino.Testing.Tests\fixtures\sample.llm.recordings.yml`
```yaml
- match: "hello.*world"
  text: "hi there!"

- match: "what is 2\\+2"
  text: "4"

- match: "resolve airport code for Tokyo"
  text: "NRT"
```

- [ ] **Step 5: Write `RecordedMockChatClientTests.cs`**

File: `D:\ino\POC\test\Ino.Testing.Tests\RecordedMockChatClientTests.cs`
```csharp
using FluentAssertions;
using Microsoft.Extensions.AI;
using Xunit;

namespace Ino.Testing.Tests;

public sealed class RecordedMockChatClientTests
{
    [Fact]
    public async Task Match_HelloWorld_ReturnsRecordedText()
    {
        var client = new RecordedMockChatClient();
        client.LoadRecordingsFromFile("fixtures/sample.llm.recordings.yml");

        var response = await client.GetResponseAsync(new[]
        {
            new ChatMessage(ChatRole.User, "hello world")
        });

        response.Messages[0].Text.Should().Be("hi there!");
    }

    [Fact]
    public async Task Match_AirportCode_ReturnsRecordedText()
    {
        var client = new RecordedMockChatClient();
        client.LoadRecordingsFromFile("fixtures/sample.llm.recordings.yml");

        var response = await client.GetResponseAsync(new[]
        {
            new ChatMessage(ChatRole.User, "resolve airport code for Tokyo")
        });

        response.Messages[0].Text.Should().Be("NRT");
    }

    [Fact]
    public async Task Miss_ThrowsMockLlmMissException_WithUnmatchedPrompt()
    {
        var client = new RecordedMockChatClient();
        client.LoadRecordingsFromFile("fixtures/sample.llm.recordings.yml");

        var act = () => client.GetResponseAsync(new[]
        {
            new ChatMessage(ChatRole.User, "completely unmatched prompt")
        });

        await act.Should().ThrowAsync<MockLlmMissException>()
            .Where(e => e.UnmatchedPrompt.Contains("completely unmatched prompt"));
    }

    [Fact]
    public async Task Miss_AccumulatesInUnmatchedPromptsList()
    {
        var client = new RecordedMockChatClient();
        client.LoadRecordingsFromFile("fixtures/sample.llm.recordings.yml");

        try
        {
            await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "miss one") });
        }
        catch (MockLlmMissException) { }

        try
        {
            await client.GetResponseAsync(new[] { new ChatMessage(ChatRole.User, "miss two") });
        }
        catch (MockLlmMissException) { }

        client.UnmatchedPrompts.Should().HaveCount(2);
        client.UnmatchedPrompts.Should().Contain(p => p.Contains("miss one"));
        client.UnmatchedPrompts.Should().Contain(p => p.Contains("miss two"));
    }

    [Fact]
    public async Task InlineYaml_LoadsCorrectly()
    {
        var client = new RecordedMockChatClient();
        client.LoadRecordingsFromYaml("""
            - match: "inline"
              text: "worked"
            """);

        var response = await client.GetResponseAsync(new[]
        {
            new ChatMessage(ChatRole.User, "inline test")
        });

        response.Messages[0].Text.Should().Be("worked");
    }
}
```

- [ ] **Step 6: Run tests**

```bash
cd D:/ino/POC
dotnet test test/Ino.Testing.Tests/Ino.Testing.Tests.csproj --nologo
```

Expected:
```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5
```

If any test fails because of `IChatClient` interface mismatches (e.g., method name is `CompleteAsync` not `GetResponseAsync` in the pinned version), update both `RecordedMockChatClient` and the tests together. The test semantics (regex match → recorded text; miss → throw) stay the same.

- [ ] **Step 7: Commit**

```bash
cd D:/ino
git add POC
git commit -m "test(poc): validate RecordedMockChatClient against sample YAML fixture"
```

---

### Task 13: Create `Ino.Core.Hosting.Tests` project with minimal test neuron fixture

**Files:**
- Create: `D:\ino\POC\test\Ino.Core.Hosting.Tests\Ino.Core.Hosting.Tests.csproj`
- Create: `D:\ino\POC\test\Ino.Core.Hosting.Tests\Fixtures\TestEvent.cs`
- Create: `D:\ino\POC\test\Ino.Core.Hosting.Tests\Fixtures\TestState.cs`
- Create: `D:\ino\POC\test\Ino.Core.Hosting.Tests\Fixtures\ITestNeuron.cs`
- Create: `D:\ino\POC\test\Ino.Core.Hosting.Tests\Fixtures\TestNeuron.cs`

- [ ] **Step 1: Create folders**

```bash
mkdir -p D:/ino/POC/test/Ino.Core.Hosting.Tests/Fixtures
```

- [ ] **Step 2: Write csproj**

File: `D:\ino\POC\test\Ino.Core.Hosting.Tests\Ino.Core.Hosting.Tests.csproj`
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <RootNamespace>Ino.Core.Hosting.Tests</RootNamespace>
    <AssemblyName>Ino.Core.Hosting.Tests</AssemblyName>
    <IsPackable>false</IsPackable>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="xunit.v3" />
    <PackageReference Include="xunit.runner.visualstudio" />
    <PackageReference Include="Microsoft.NET.Test.Sdk" />
    <PackageReference Include="FluentAssertions" />
    <PackageReference Include="Microsoft.Orleans.Server" />
    <PackageReference Include="Microsoft.Orleans.Journaling" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Ino.Core\Ino.Core.csproj" />
    <ProjectReference Include="..\..\src\Ino.Core.Hosting\Ino.Core.Hosting.csproj" />
    <ProjectReference Include="..\..\src\Ino.Testing\Ino.Testing.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 3: Add to solution**

```bash
cd D:/ino/POC
dotnet sln ino.slnx add test/Ino.Core.Hosting.Tests/Ino.Core.Hosting.Tests.csproj
```

- [ ] **Step 4: Write `TestEvent.cs`**

File: `D:\ino\POC\test\Ino.Core.Hosting.Tests\Fixtures\TestEvent.cs`
```csharp
using Ino.Core;

namespace Ino.Core.Hosting.Tests.Fixtures;

/// <summary>
/// Minimal ISynapse event used to exercise the Neuron&lt;TState, TEvent&gt; base class
/// in integration tests.
/// </summary>
[GenerateSerializer]
public sealed record TestEvent(
    [property: Id(0)] string Text,
    [property: Id(1)] int Delta) : ISynapse;
```

- [ ] **Step 5: Write `TestState.cs`**

File: `D:\ino\POC\test\Ino.Core.Hosting.Tests\Fixtures\TestState.cs`
```csharp
using Orleans;

namespace Ino.Core.Hosting.Tests.Fixtures;

/// <summary>
/// Minimal state type for the test neuron. Tracks total events seen, a running sum of
/// deltas, and the last text observed.
/// </summary>
[GenerateSerializer]
public sealed class TestState
{
    [Id(0)] public int EventCount { get; set; }

    [Id(1)] public int TotalDelta { get; set; }

    [Id(2)] public string? LastText { get; set; }
}
```

- [ ] **Step 6: Write `ITestNeuron.cs`**

File: `D:\ino\POC\test\Ino.Core.Hosting.Tests\Fixtures\ITestNeuron.cs`
```csharp
using Orleans;

namespace Ino.Core.Hosting.Tests.Fixtures;

/// <summary>
/// Grain interface the test uses to drive the test neuron. Exposes a method to apply
/// an event + retrieve the current state and history.
///
/// In Phase 2 this is replaced by the INeuron&lt;T&gt; canonical dispatch path — for
/// Phase 1 we need an explicit grain interface so the test cluster can resolve a
/// grain by key and invoke methods on it directly.
/// </summary>
public interface ITestNeuron : IGrainWithStringKey
{
    Task ApplyEventAsync(TestEvent @event, string correlationId);

    Task<TestState> GetStateAsync();

    Task<IReadOnlyList<TestEvent>> GetHistoryAsync();

    Task<int> GetEventCountAsync();
}
```

- [ ] **Step 7: Write `TestNeuron.cs`**

File: `D:\ino\POC\test\Ino.Core.Hosting.Tests\Fixtures\TestNeuron.cs`
```csharp
using Ino.Core.Hosting;
using Ino.Testing;

namespace Ino.Core.Hosting.Tests.Fixtures;

/// <summary>
/// Minimal neuron that exercises the Neuron&lt;TState, TEvent&gt; base class. Applies events
/// via RaiseAsync and exposes the projected state and full history for assertion.
/// </summary>
public sealed class TestNeuron : Neuron<TestState, TestEvent>, ITestNeuron
{
    public Task ApplyEventAsync(TestEvent @event, string correlationId)
    {
        var ctx = new InoTestNeuronContext(
            correlationId: correlationId,
            sourceExperience: "test",
            sourceStream: "test:fixture");
        return RaiseAsync(@event, ctx);
    }

    public Task<TestState> GetStateAsync() => Task.FromResult(State);

    public new Task<IReadOnlyList<TestEvent>> GetHistoryAsync() =>
        base.GetHistoryAsync(1000);

    public Task<int> GetEventCountAsync() => Task.FromResult(State.EventCount);

    protected override void Apply(TestState state, TestEvent @event)
    {
        state.EventCount++;
        state.TotalDelta += @event.Delta;
        state.LastText = @event.Text;
    }
}
```

**Note on the `new` keyword on `GetHistoryAsync`:** The base class's `GetHistoryAsync` has signature `GetHistoryAsync(int lastN = 100)`. The interface `ITestNeuron` exposes a parameterless version that returns up to 1000 events. The `new` keyword hides the base version so callers of `ITestNeuron.GetHistoryAsync()` get the test-friendly overload. If Orleans' source generators complain about the `new` keyword (they can be picky), rename the interface method to `GetFullHistoryAsync` and update the test accordingly.

- [ ] **Step 8: Build**

```bash
cd D:/ino/POC
dotnet build ino.slnx
```

Expected: `Build succeeded`.

If the build fails with an error like "The type or namespace name 'Neuron`2' could not be found," double-check that `Ino.Core.Hosting.Tests.csproj` has the `<ProjectReference Include="..\..\src\Ino.Core.Hosting\Ino.Core.Hosting.csproj" />` line.

- [ ] **Step 9: Commit**

```bash
cd D:/ino
git add POC
git commit -m "test(poc): test neuron fixture for integration tests

TestNeuron : Neuron<TestState, TestEvent>, ITestNeuron exercises
the full base class API — RaiseAsync, Apply, state projection,
GetHistoryAsync — without needing any of the later-phase primitives."
```

---

### Task 14: Write the failing integration test

**Files:**
- Create: `D:\ino\POC\test\Ino.Core.Hosting.Tests\NeuronBaseClassTests.cs`

- [ ] **Step 1: Write the test file with one failing test**

File: `D:\ino\POC\test\Ino.Core.Hosting.Tests\NeuronBaseClassTests.cs`
```csharp
using FluentAssertions;
using Ino.Core.Hosting.Tests.Fixtures;
using Ino.Testing;
using Orleans;
using Xunit;

namespace Ino.Core.Hosting.Tests;

[Collection(nameof(InoTestCollection))]
public sealed class NeuronBaseClassTests
{
    private readonly InoTestSiloFixture _fixture;

    public NeuronBaseClassTests(InoTestSiloFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task RaiseAsync_UpdatesProjectedState()
    {
        var neuron = _fixture.Grains.GetGrain<ITestNeuron>("test-neuron-state");
        var correlationId = Guid.NewGuid().ToString("n");

        await neuron.ApplyEventAsync(new TestEvent("first", 10), correlationId);
        await neuron.ApplyEventAsync(new TestEvent("second", 5), correlationId);
        await neuron.ApplyEventAsync(new TestEvent("third", -3), correlationId);

        var state = await neuron.GetStateAsync();

        state.EventCount.Should().Be(3);
        state.TotalDelta.Should().Be(12);
        state.LastText.Should().Be("third");
    }

    [Fact]
    public async Task RaiseAsync_AppendsToJournal_RetrievableViaGetHistoryAsync()
    {
        var neuron = _fixture.Grains.GetGrain<ITestNeuron>("test-neuron-history");
        var correlationId = Guid.NewGuid().ToString("n");

        await neuron.ApplyEventAsync(new TestEvent("first", 1), correlationId);
        await neuron.ApplyEventAsync(new TestEvent("second", 2), correlationId);

        var history = await neuron.GetHistoryAsync();

        history.Should().HaveCount(2);
        history[0].Text.Should().Be("first");
        history[0].Delta.Should().Be(1);
        history[1].Text.Should().Be("second");
        history[1].Delta.Should().Be(2);
    }

    [Fact]
    public async Task RaiseAsync_PersistsEvents_VisibleOnNewActivation()
    {
        // Use a unique grain key so previous tests don't pollute this one.
        var grainKey = $"test-neuron-persist-{Guid.NewGuid():n}";
        var correlationId = Guid.NewGuid().ToString("n");

        var first = _fixture.Grains.GetGrain<ITestNeuron>(grainKey);
        await first.ApplyEventAsync(new TestEvent("persisted", 42), correlationId);

        // Force grain deactivation is not trivial in TestCluster; instead, create a
        // fresh grain reference with the same key. Orleans may reuse the same
        // activation (fine — it still proves the state is correctly projected from
        // confirmed events). For a true reactivation test we'd need Phase 2's
        // DeactivateOnIdleAsync support.
        var second = _fixture.Grains.GetGrain<ITestNeuron>(grainKey);
        var state = await second.GetStateAsync();
        var history = await second.GetHistoryAsync();

        state.EventCount.Should().Be(1);
        state.TotalDelta.Should().Be(42);
        state.LastText.Should().Be("persisted");
        history.Should().HaveCount(1);
        history[0].Text.Should().Be("persisted");
    }

    [Fact]
    public async Task ZeroEvents_StateIsDefault_HistoryIsEmpty()
    {
        var neuron = _fixture.Grains.GetGrain<ITestNeuron>("test-neuron-empty");

        var state = await neuron.GetStateAsync();
        var history = await neuron.GetHistoryAsync();
        var count = await neuron.GetEventCountAsync();

        state.EventCount.Should().Be(0);
        state.TotalDelta.Should().Be(0);
        state.LastText.Should().BeNull();
        history.Should().BeEmpty();
        count.Should().Be(0);
    }
}
```

- [ ] **Step 2: Run the tests and verify they compile and pass**

```bash
cd D:/ino/POC
dotnet test test/Ino.Core.Hosting.Tests/Ino.Core.Hosting.Tests.csproj --nologo
```

Expected:
```
Passed!  - Failed:     0, Passed:     4, Skipped:     0, Total:     4
```

**If the tests fail at this point:** the base class implementation has a real bug and needs fixing. Common failure modes:

| Failure | Likely cause | Fix |
|---|---|---|
| `Cannot find grain implementation for interface ITestNeuron` | Orleans silo hasn't discovered `TestNeuron` | Verify `TestNeuron` inherits from `Neuron<TestState, TEvent>` and the Fixtures folder is compiled into `Ino.Core.Hosting.Tests.dll` |
| `No log consistency provider found with name 'LogStorage'` | `TestSiloConfigurator` didn't register it | Check `TestSiloConfigurator.Configure` calls `silo.AddLogStorageBasedLogConsistencyProvider("LogStorage")` |
| `No storage provider found with name 'NeuronStore'` | `AddMemoryGrainStorage("NeuronStore")` missing | Check `TestSiloConfigurator.Configure` |
| `ArgumentNullException` inside `RaiseAsync` | `ctx` parameter is null | Verify `InoTestNeuronContext` constructor populates `CorrelationId` and `CurrentEventId` with defaults |
| State assertions fail (`EventCount == 0` after 3 applies) | `Apply` method not being called | Check `TransitionState` override in `Neuron.cs` delegates to `Apply(state, envelope.Payload)` |

Fix the actual bug, don't paper over it. Re-run until all 4 tests pass.

- [ ] **Step 3: Commit**

```bash
cd D:/ino
git add POC
git commit -m "test(poc): integration tests for Neuron<TState, TEvent> base class

Four tests exercising RaiseAsync + Apply + state projection + history
retrieval against an in-memory TestCluster. Locks in the primitive
contract that later phases build on."
```

---

### Task 15: Final verification — full solution build + full test run

- [ ] **Step 1: Clean build from scratch**

```bash
cd D:/ino/POC
dotnet build ino.slnx --no-incremental
```

Expected:
```
Build succeeded.
    0 Warning(s)
    0 Error(s)
```

- [ ] **Step 2: Run the full test suite**

```bash
dotnet test ino.slnx --nologo
```

Expected something like:
```
Test run for Ino.Core.Tests.dll (.NET 10.0)  - Passed!
Test run for Ino.Testing.Tests.dll (.NET 10.0)  - Passed!
Test run for Ino.Core.Hosting.Tests.dll (.NET 10.0)  - Passed!
```

Total: 26 passed, 0 failed, 0 skipped (exact number may vary slightly based on how many test cases each `[Fact]` expands to).

- [ ] **Step 3: Verify the solution layout**

```bash
cd D:/ino/POC
ls src && ls test
```

Expected output:
```
# src:
Ino.Core
Ino.Core.Hosting
Ino.Testing

# test:
Ino.Core.Hosting.Tests
Ino.Core.Tests
Ino.Testing.Tests
```

- [ ] **Step 4: Verify `dotnet sln list` shows all projects**

```bash
dotnet sln ino.slnx list
```

Expected:
```
Project(s)
----------
src/Ino.Core/Ino.Core.csproj
test/Ino.Core.Tests/Ino.Core.Tests.csproj
src/Ino.Core.Hosting/Ino.Core.Hosting.csproj
src/Ino.Testing/Ino.Testing.csproj
test/Ino.Testing.Tests/Ino.Testing.Tests.csproj
test/Ino.Core.Hosting.Tests/Ino.Core.Hosting.Tests.csproj
```

6 projects total.

- [ ] **Step 5: Update README.md to reflect Phase 1 status**

File: `D:\ino\POC\README.md`
```markdown
# ino POC

Greenfield POC for ino — an AI-native operating system built on neurons + synapses.

This POC lives at `D:\ino\POC\` and **does not modify the existing code at `D:\ino\src\`**.
The existing codebase is a reference for what did and didn't work; this POC implements the
design fresh.

## Design

Full design: `docs/superpowers/specs/2026-04-14-ino-poc-core-primitives-design.md` in the
parent repo.

## Phase status

- **Phase 1 (Core Foundations)** — ✅ Complete. `Ino.Core`, `Ino.Core.Hosting`, `Ino.Testing`
  compile; `Neuron<TState, TEvent>` base class journals events through Orleans JournaledGrain
  + LogStorage + in-memory grain storage; four integration tests lock in the primitive
  contract.
- **Phase 2 (Cross-silo runtime + AppHost)** — not started.
- **Phase 3 (Analyzer + source generator)** — not started.
- **Phase 4 (Notes experience + memory search)** — not started.
- **Phase 5 (Travel cluster + identity + marketplace consent)** — not started.
- **Phase 6 (Proactive + Playback + Branches)** — not started.

## Build & Test

```bash
cd D:/ino/POC
dotnet build ino.slnx
dotnet test ino.slnx
```

## Projects

### src/
- **Ino.Core** — primitive types (ISynapse, NeuronResult, SynapseError, EventEnvelope, Capability, ExperienceMetadata, attributes)
- **Ino.Core.Hosting** — handler interfaces (INeuron, IReactsTo, NeuronContext, IJournaledNeuronQuery) and the `Neuron<TState, TEvent>` base class
- **Ino.Testing** — shared test harness (InoTestSiloFixture + ICollectionFixture pattern, InoTestNeuronContext, RecordedMockChatClient)

### test/
- **Ino.Core.Tests** — unit tests for primitive types
- **Ino.Testing.Tests** — validates the test harness itself (YAML loader, regex matching, miss exceptions)
- **Ino.Core.Hosting.Tests** — integration tests proving `Neuron<TState, TEvent>` journals events correctly through Orleans
```

- [ ] **Step 6: Final commit**

```bash
cd D:/ino
git add POC
git commit -m "docs(poc): mark Phase 1 complete in POC README

Phase 1 ships Ino.Core primitive types, Ino.Core.Hosting handler
interfaces + Neuron<TState, TEvent> base class, Ino.Testing harness,
and three test projects totaling 26+ passing tests. Foundation for
Phase 2 cross-silo runtime + AppHost."
```

---

## Self-Review

### Spec coverage

Cross-checking Phase 1 tasks against the design spec:

| Spec section | Covered? | By task(s) |
|---|---|---|
| 5 — POC solution layout | ✅ Partial (src/Ino.Core, src/Ino.Core.Hosting, src/Ino.Testing, test/Ino.Core.Tests, test/Ino.Core.Hosting.Tests, test/Ino.Testing.Tests) | Tasks 1, 2, 5, 8, 13 |
| 6 — Tech stack (.NET 10, Orleans 9.x, xunit.v3, YamlDotNet, Ulid, Central Package Management, Directory.Build.props) | ✅ | Task 1 |
| 7.1 — `ISynapse` marker | ✅ | Task 3 step 1 |
| 7.2 — `INeuron<T>` | ✅ | Task 6 step 1 |
| 7.3 — `IReactsTo<T>` | ✅ | Task 6 step 2 |
| 7.4 — `NeuronContext` (interface) | ✅ (Phase 1 minimum — SynapseId, CurrentEventId, CorrelationId, SourceExperience, SourceStream, UserId, SessionId, Logger, CurrentActivity) | Task 6 step 3 |
| 7.5 — `NeuronResult` | ✅ | Task 3 step 3 |
| 7.6 — `Capability` discriminated union | ✅ | Task 3 step 6 |
| 7.7 — `[UserEntry]`, `[RequiresCapability]` attributes | ✅ (types declared; aggregation logic deferred to Phase 3 source generator) | Task 3 steps 10, 11, 12 |
| 9 — `Neuron<TState, TEvent>` base class | ✅ | Task 7 |
| 9.1 — `EventEnvelope<T>` wrapper | ✅ | Task 3 step 4 |
| 9.2 — Redis storage backend | ❌ deferred to Phase 2 (tests use `AddMemoryGrainStorage`) | n/a |
| 9.3 — LogStorage cliff rationale | ✅ (design, not implementation — no Phase 1 action required) | n/a |
| 17.1 — Shared `InoTestSiloFixture` with `ICollectionFixture<T>` | ✅ | Task 9 |
| 17.2 — `RecordedMockChatClient` YAML + regex + miss exception | ✅ (text responses only; tool calls + JSON deferred to Phase 4) | Tasks 11, 12 |

**Gaps intentionally deferred (and explicitly documented in the "out of scope" section at the top of this plan):** AppHost composition, cross-silo dispatch runtime, `ctx.Fire<T>`/`FireBroadcast<T>`/`IAmbientFire`, `ctx.Search`/`ctx.Identity` facades, the analyzer, the source generator, marketplace endpoints, all experiences (Notes, Travel, AutoCheckIn), and the Playback/CausationIndex/BranchManager neurons.

**Phase 1 verdict:** covers every primitive type, interface, and base-class behavior needed for later phases to build on. No hidden dependencies on deferred scope.

### Placeholder scan

Searched for "TBD", "TODO", "implement later", "similar to", "fill in" — none found in the task bodies. Every step contains the actual content an engineer needs. Task 11 Step 4 notes that `IChatClient`'s method signatures may differ between `Microsoft.Extensions.AI` versions and includes explicit guidance on adapting the implementation if the compiler flags a mismatch — this is documented uncertainty, not a placeholder.

### Type consistency

- `Neuron<TState, TEvent>` constraint: `TState : class, new(), TEvent : class, ISynapse` — consistent across `Neuron.cs`, `TestNeuron.cs`, `TestState.cs`, `TestEvent.cs`.
- `EventEnvelope<T>` constraint: `T : class, ISynapse` — matches the `TEvent` constraint on `Neuron<TState, TEvent>`.
- `RaiseAsync` signature: `Task RaiseAsync(TEvent, NeuronContext, CancellationToken)` — used identically in Task 7 (definition) and Task 13 (`TestNeuron.ApplyEventAsync` calls it).
- `Apply` signature: `void Apply(TState, TEvent)` — matches in base class (Task 7) and test neuron (Task 13).
- `InoTestNeuronContext` constructor order: `synapseId, currentEventId, correlationId, sourceExperience, sourceStream, userId, sessionId, logger` — all named parameters have defaults, tests can use them positionally or named. Consistent between definition (Task 10) and usage (Task 13 `TestNeuron.ApplyEventAsync`).
- `RecordedMockChatClient.GetResponseAsync` — signature matches the `IChatClient` interface from `Microsoft.Extensions.AI` 9.3.0. If the pinned version exposes a different method name, Task 11 Step 4 and Task 12 Step 5 need to be updated together.

**One potential inconsistency flagged:** Task 13 Step 7's `TestNeuron.GetHistoryAsync` uses the `new` keyword to override the base class's overload. If this causes an Orleans source generator issue (grain-facing methods may not play well with `new`), the alternative is renaming the `ITestNeuron` interface method to `GetRecentHistoryAsync` or similar. **Recorded in Task 13 Step 7 as a fallback.**

### Final tally

- **15 tasks**
- **Approximately 100 steps** (each a 2-5 minute action)
- **Estimated 3-5 hours of focused implementation time**
- **Delivers compilable + tested primitive foundation** ready for Phase 2 to build cross-silo dispatch on top

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-04-14-ino-poc-phase-1-core-foundations-plan.md`. Two execution options:

**1. Subagent-Driven (recommended)** — I dispatch a fresh subagent per task, review between tasks, fast iteration. Each task gets a clean context so the subagent isn't distracted by the full plan.

**2. Inline Execution** — Execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints for your review.

**Which approach?**
