# Coding phase 0: workspace and map — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A `Coding` module whose `workspace` neuron loads this solution with Roslyn and answers symbol,
reference, diagnostics and map queries through typed methods, four `code_*` chat tools, and a solution map
the shell renders.

**Architecture:** A singleton `SolutionWorkspace` service owns one Roslyn `Workspace` behind an
`ISolutionLoader` seam (MSBuild in the kernel, adhoc in tests) and answers queries on the immutable current
snapshot. A `workspace` neuron (`Neuron<WorkspaceState>`) is the durable front door: `open`/`reload`
commands schedule reactions that start the load; read methods delegate to the service. Native tools call
the service directly, the way `TableService` is used, and `code_map` returns a `kind: "graph"` result the
shell opens like a chart.

**Tech Stack:** .NET 11 rc1, Orleans 10.3.1 neurons, `Microsoft.CodeAnalysis.CSharp.Workspaces` 5.9.0,
`Microsoft.CodeAnalysis.Workspaces.MSBuild` 5.9.0, `Microsoft.Build.Locator` 1.11.2, Microsoft.Extensions.AI
10.9.0 (`AIFunctionFactory`), xunit.v3 on Microsoft.Testing.Platform, Flutter 3.47.2.

**Spec:** `docs/coding/coding-agent-design.md` (sections 4.1, 4.2, 4.3, 4.8, 4.10; decisions D1, D7, D8,
D9). Research: `docs/coding/coding-agent-research.md`.

## Global Constraints

- Branch `feature/coding-phase0-workspace` from `master`; commits prefixed `coding:`; one PR.
- `TreatWarningsAsErrors=true`, `AnalysisLevel=preview-all`, `EnforceCodeStyleInBuild=true`: every build
  must be warning-free. Services use `.ConfigureAwait(false)`; grain code uses `.ConfigureAwait(true)`.
- No `/// <summary>` comments; a short inline comment only where the reason is not visible in the code.
- Every contract DTO: `[GenerateSerializer]`, `[Alias("coding.<kebab-name>")]`, `[property: Id(n)]`, and an
  entry in `CodingJson`. Every neuron method: `[Alias]`; queries `[ReadOnly]` with one DTO; mutators take
  exactly one DTO deriving from `Command`.
- Local gate before every commit:
  `dotnet format whitespace DigitalBrain.slnx --verify-no-changes && dotnet build DigitalBrain.slnx -c Release && dotnet test DigitalBrain.slnx -c Release --no-build`
  (expect the 4 Docker-gated skips). Run one class with
  `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.<Class>`.
- Flutter gate for the shell task: `dart format --set-exit-if-changed core ui shell`, `flutter analyze` in
  `ui` and `shell`, `flutter test` in `ui` and `shell`, run from `src/Modules/UI/Flutter`.
- Never read or write under `C:\Users`; the local NuGet cache is not a documentation source.

## File structure

```
src/Modules/Coding/Contracts/DigitalBrain.Modules.Coding.Contracts.csproj
src/Modules/Coding/Contracts/CodingVocabulary.cs         grain type + signal names
src/Modules/Coding/Contracts/ICodeWorkspace.cs           the neuron contract
src/Modules/Coding/Contracts/OpenWorkspace.cs            command
src/Modules/Coding/Contracts/ReloadWorkspace.cs          command
src/Modules/Coding/Contracts/WorkspaceReceipt.cs         Accepted<> receipt
src/Modules/Coding/Contracts/OpeningBody.cs              scheduled signal body
src/Modules/Coding/Contracts/WorkspacePhase.cs           enum
src/Modules/Coding/Contracts/WorkspaceSnapshot.cs        read result
src/Modules/Coding/Contracts/SymbolSearch.cs, SymbolHit.cs, SymbolSearchResult.cs
src/Modules/Coding/Contracts/ReferenceSearch.cs, ReferenceHit.cs, ReferenceSearchResult.cs
src/Modules/Coding/Contracts/DiagnosticsQuery.cs, DiagnosticHit.cs, DiagnosticsResult.cs
src/Modules/Coding/Contracts/MapQuery.cs, ProjectNode.cs, ProjectEdge.cs, SolutionMap.cs
src/Modules/Coding/Contracts/CodingJson.cs               source-generated JSON context
src/Modules/Coding/Coding/DigitalBrain.Modules.Coding.csproj
src/Modules/Coding/Coding/CodingModule.cs                IModule: services, loader, warmup, tools
src/Modules/Coding/Coding/ISolutionLoader.cs             seam
src/Modules/Coding/Coding/MSBuildSolutionLoader.cs       kernel loader
src/Modules/Coding/Coding/WorkspaceStatus.cs             live status record (not a contract)
src/Modules/Coding/Coding/WorkspaceNotReadyException.cs
src/Modules/Coding/Coding/SolutionWorkspace.cs           the service
src/Modules/Coding/Coding/SolutionQueries.cs             static query helpers over a Solution
src/Modules/Coding/Coding/WorkspaceWarmup.cs             IHostedService
src/Modules/Coding/Coding/WorkspaceState.cs              neuron snapshot state
src/Modules/Coding/Coding/WorkspaceNeuron.cs             the neuron
src/Modules/Coding/Coding/CodingNativeTools.cs           code_find_symbols, code_references, code_diagnostics, code_map
src/Modules/Coding/Aspire.Hosting/DigitalBrain.Modules.Coding.Aspire.Hosting.csproj
src/Modules/Coding/Aspire.Hosting/CodingHostingExtensions.cs   WithSolution(path)
tests/DigitalBrain.Tests/Features/Coding/AdhocSolutionLoader.cs
tests/DigitalBrain.Tests/Features/Coding/FixtureSolutions.cs
tests/DigitalBrain.Tests/Features/Coding/SolutionWorkspaceFacts.cs
tests/DigitalBrain.Tests/Features/Coding/CodeWorkspaceNeuronFacts.cs
tests/DigitalBrain.Tests/Features/Coding/CodingNativeToolFacts.cs
tests/DigitalBrain.Tests/Features/Coding/CodingSelfTestFacts.cs   gated on DIGITALBRAIN_CODING_SELF_TESTS
src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Graph/GraphNodeKinds.cs   + Module, Entity
src/Modules/UI/Flutter/shell/lib/workspace/graph_artifact.dart            tool-result graph parser
src/Modules/UI/Flutter/shell/lib/workspace/artifact_editors.dart          'graph' case
src/Modules/UI/Flutter/shell/lib/workspace/workspace_chat_presentation.dart  'graph' opens
src/Modules/UI/Flutter/shell/lib/workspace/workspace_app.dart             _servedByResult 'graph'
src/Modules/UI/Flutter/shell/test/workspace/graph_artifact_test.dart
src/Aspire/DigitalBrain.AppHost/AppHost.cs                AddModule<CodingModule>, Graph:Enabled
src/Kernel/DigitalBrain.Silo/DigitalBrain.Silo.csproj     reference
tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj        references
Directory.Packages.props, Directory.Build.props, DigitalBrain.slnx, .mcp.json
src/Modules/AI/AI/ConversationalAgent.cs:89               allowlist
docs/coding/README.md, docs/coding/NOTES.md
```

---

### Task 1: Module scaffold that composes

**Files:**
- Create: the three `.csproj` files above, `CodingModule.cs`, `CodingVocabulary.cs`
- Modify: `Directory.Packages.props`, `Directory.Build.props`, `DigitalBrain.slnx`,
  `src/Kernel/DigitalBrain.Silo/DigitalBrain.Silo.csproj`, `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`
- Test: `tests/DigitalBrain.Tests/Features/Coding/SolutionWorkspaceFacts.cs` (first fact only)

**Interfaces:**
- Produces: `DigitalBrain.Coding.CodingModule : IModule` with `public const string ConfigurationRoot = "DigitalBrain:Coding"`;
  `DigitalBrain.Coding.CodingVocabulary.WorkspaceType = "workspace"`.

- [ ] **Step 1: Write the failing composition fact**

```csharp
// tests/DigitalBrain.Tests/Features/Coding/SolutionWorkspaceFacts.cs
using DigitalBrain.Coding;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class SolutionWorkspaceFacts
{
    [Fact]
    public async Task The_coding_module_composes_into_a_silo()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(CodingModule)]) });
        Assert.NotNull(brain.SiloServices.GetService(typeof(CodingModule)) is null ? brain.Grains : null);
    }
}
```

- [ ] **Step 2: Run it to see the compile failure**

Run: `dotnet build tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release`
Expected: error CS0246 `CodingModule` not found.

- [ ] **Step 3: Add the package versions**

`Directory.Packages.props`, inside the existing `<ItemGroup>` in alphabetical position:

```xml
    <PackageVersion Include="Microsoft.Build.Locator" Version="1.11.2" />
    <PackageVersion Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="5.9.0" />
    <PackageVersion Include="Microsoft.CodeAnalysis.Workspaces.MSBuild" Version="5.9.0" />
```

The SDK refuses to copy `Microsoft.Build.*` assemblies into an output by default. The switch that allows it
goes on the three projects that carry them (module, silo, tests) in Steps 4 and 6, never in
`Directory.Build.props`: a global suppression would hide a future conflict in an unrelated project
(design section 8, finding 4).

- [ ] **Step 4: Create the three projects**

`src/Modules/Coding/Contracts/DigitalBrain.Modules.Coding.Contracts.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>true</IsPackable>
    <Description>Coding module contracts: workspace commands, symbol, reference, diagnostics and map queries.</Description>
    <RootNamespace>DigitalBrain.Coding</RootNamespace>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <NoWarn>$(NoWarn);CS1591</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../../../Kernel/DigitalBrain.Contracts/DigitalBrain.Contracts.csproj" />
  </ItemGroup>
</Project>
```

`src/Modules/Coding/Coding/DigitalBrain.Modules.Coding.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <IsPackable>true</IsPackable>
    <Description>Coding module: a Roslyn workspace neuron and the code_* tools.</Description>
    <RootNamespace>DigitalBrain.Coding</RootNamespace>
    <NoWarn>$(NoWarn);ORLEANSEXP005</NoWarn>
    <!-- Microsoft.CodeAnalysis.Workspaces.MSBuild carries Microsoft.Build.Framework; the SDK refuses to copy it by default. -->
    <DisableMSBuildAssemblyCopyCheck>true</DisableMSBuildAssemblyCopyCheck>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="../Contracts/DigitalBrain.Modules.Coding.Contracts.csproj" />
    <ProjectReference Include="../../../Kernel/DigitalBrain/DigitalBrain.csproj" />
    <ProjectReference Include="../../AI/Contracts/DigitalBrain.Modules.AI.Contracts.csproj" />
    <ProjectReference Include="../../UI/DigitalBrain.Modules.UI.Contracts/DigitalBrain.Modules.UI.Contracts.csproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.Build.Locator" />
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" />
    <PackageReference Include="Microsoft.CodeAnalysis.Workspaces.MSBuild" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="DigitalBrain.Tests" />
  </ItemGroup>
</Project>
```

`src/Modules/Coding/Aspire.Hosting/DigitalBrain.Modules.Coding.Aspire.Hosting.csproj` (mirror
`src/Modules/Microsoft/Aspire.Hosting/*.csproj`: reference `Aspire.Hosting`, the brain hosting project
`src/Aspire/DigitalBrain.Aspire.Hosting/DigitalBrain.Aspire.Hosting.csproj`, and
`../Coding/DigitalBrain.Modules.Coding.csproj`; `RootNamespace` `DigitalBrain.Coding.Aspire.Hosting`).
Copy the Microsoft one and change the names; do not add packages it does not have.

- [ ] **Step 5: Write the module and vocabulary**

```csharp
// src/Modules/Coding/Contracts/CodingVocabulary.cs
namespace DigitalBrain.Coding;

public static class CodingVocabulary
{
    public const string WorkspaceType = "workspace";

    // ---- work a command schedules for its own reaction ----
    public const string WorkspaceOpening = "WorkspaceOpening";
    public const string WorkspaceReloading = "WorkspaceReloading";
}
```

```csharp
// src/Modules/Coding/Coding/CodingModule.cs
using DigitalBrain.Core;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DigitalBrain.Coding;

public sealed class CodingModule : IModule
{
    public const string ConfigurationRoot = "DigitalBrain:Coding";
    public const string SolutionPathKey = "DigitalBrain:Coding:SolutionPath";

    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<CodingModule>();
    }
}
```

- [ ] **Step 6: Wire the solution, the silo and the tests**

`DigitalBrain.slnx`: add after the ClickHouse folder:

```xml
  <Folder Name="/Modules/Coding/">
    <Project Path="src/Modules/Coding/Aspire.Hosting/DigitalBrain.Modules.Coding.Aspire.Hosting.csproj" />
    <Project Path="src/Modules/Coding/Coding/DigitalBrain.Modules.Coding.csproj" />
    <Project Path="src/Modules/Coding/Contracts/DigitalBrain.Modules.Coding.Contracts.csproj" />
  </Folder>
```

`src/Kernel/DigitalBrain.Silo/DigitalBrain.Silo.csproj`: add
`<ProjectReference Include="../../Modules/Coding/Coding/DigitalBrain.Modules.Coding.csproj" />` next to the
other module references (the silo resolves modules with `Type.GetType`; without this line the kernel cannot
load the module), and `<DisableMSBuildAssemblyCopyCheck>true</DisableMSBuildAssemblyCopyCheck>` in its
`<PropertyGroup>` with the same one-line comment as the module project.

`tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`: add references to
`../../src/Modules/Coding/Coding/DigitalBrain.Modules.Coding.csproj` and
`../../src/Modules/Coding/Contracts/DigitalBrain.Modules.Coding.Contracts.csproj`, and the same
`DisableMSBuildAssemblyCopyCheck` property.

- [ ] **Step 7: Run the fact**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.SolutionWorkspaceFacts`
Expected: PASS. If the build reports an MSB error about `Microsoft.Build` assemblies in the output of a
project other than the three above, that project also receives the property; record which one in
`NOTES.md`.

- [ ] **Step 8: Commit**

```bash
git add Directory.Packages.props DigitalBrain.slnx src/Modules/Coding src/Kernel/DigitalBrain.Silo/DigitalBrain.Silo.csproj tests/DigitalBrain.Tests
git commit -m "coding: scaffold the Coding module with Roslyn packages"
```

---

### Task 2: Contracts and the JSON context

**Files:**
- Create: every file listed under `src/Modules/Coding/Contracts/` in the file structure except `CodingVocabulary.cs`
- Test: `tests/DigitalBrain.Tests/Features/Coding/CodeWorkspaceNeuronFacts.cs` (descriptor fact)

**Interfaces:**
- Produces: `ICodeWorkspace` and every DTO below, exactly as named. Later tasks use these names.

- [ ] **Step 1: Write the failing descriptor fact**

Descriptor rules are validated when the silo starts with a module that declares a neuron. The fact below
fails until Task 3 registers `WorkspaceNeuron`; today it fails to compile because `ICodeWorkspace` is missing.

```csharp
// tests/DigitalBrain.Tests/Features/Coding/CodeWorkspaceNeuronFacts.cs
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Coding;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class CodeWorkspaceNeuronFacts
{
    [Fact]
    public async Task An_unopened_workspace_reads_as_not_opened()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(CodingModule)]) });
        var workspace = brain.Grains.GetGrain<ICodeWorkspace>(new NeuronId(CodingVocabulary.WorkspaceType, "fixture").ToGrainId());
        var snapshot = await workspace.Read();
        Assert.Equal(WorkspacePhase.NotOpened, snapshot.Phase);
        Assert.Null(snapshot.SolutionPath);
    }
}
```

- [ ] **Step 2: Write the contract**

```csharp
// src/Modules/Coding/Contracts/ICodeWorkspace.cs
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Coding;

[Alias("workspace")]
public interface ICodeWorkspace : INeuron
{
    [Alias("open")]
    Task<Accepted<WorkspaceReceipt>> Open(OpenWorkspace command);

    [Alias("reload")]
    Task<Accepted<WorkspaceReceipt>> Reload(ReloadWorkspace command);

    [ReadOnly]
    [Alias("read")]
    Task<WorkspaceSnapshot> Read();

    [ReadOnly]
    [Alias("find-symbols")]
    Task<SymbolSearchResult> FindSymbols(SymbolSearch query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("references")]
    Task<ReferenceSearchResult> References(ReferenceSearch query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("diagnostics")]
    Task<DiagnosticsResult> Diagnostics(DiagnosticsQuery query, CancellationToken cancellationToken = default);

    [ReadOnly]
    [Alias("map")]
    Task<SolutionMap> Map(MapQuery query, CancellationToken cancellationToken = default);
}
```

- [ ] **Step 3: Write the DTOs (one record per file)**

```csharp
// OpenWorkspace.cs
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.open-workspace")]
public sealed record OpenWorkspace(
    CommandId Id,
    [property: Id(0)] string SolutionPath,
    long? ExpectedVersion = null) : Command(Id, ExpectedVersion);
```

```csharp
// ReloadWorkspace.cs
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.reload-workspace")]
public sealed record ReloadWorkspace(CommandId Id) : Command(Id);
```

```csharp
// WorkspaceReceipt.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.workspace-receipt")]
public sealed record WorkspaceReceipt([property: Id(0)] string Key, [property: Id(1)] long Generation);
```

```csharp
// OpeningBody.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.opening-body")]
public sealed record OpeningBody([property: Id(0)] string SolutionPath);
```

```csharp
// WorkspacePhase.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.workspace-phase")]
public enum WorkspacePhase
{
    NotOpened = 0,
    Opening = 1,
    Ready = 2,
    Failed = 3,
}
```

```csharp
// WorkspaceSnapshot.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.workspace-snapshot")]
public sealed record WorkspaceSnapshot(
    [property: Id(0)] string? SolutionPath,
    [property: Id(1)] WorkspacePhase Phase,
    [property: Id(2)] int ProjectCount,
    [property: Id(3)] int DocumentCount,
    [property: Id(4)] string? Detail,
    [property: Id(5)] long Generation);
```

```csharp
// SymbolSearch.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.symbol-search")]
public sealed record SymbolSearch([property: Id(0)] string Query, [property: Id(1)] int Limit = 20);
```

```csharp
// SymbolHit.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.symbol-hit")]
public sealed record SymbolHit(
    [property: Id(0)] string Id,
    [property: Id(1)] string Kind,
    [property: Id(2)] string Name,
    [property: Id(3)] string Display,
    [property: Id(4)] string Project,
    [property: Id(5)] string Path,
    [property: Id(6)] int Line);
```

```csharp
// SymbolSearchResult.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.symbol-search-result")]
public sealed record SymbolSearchResult(
    [property: Id(0)] IReadOnlyList<SymbolHit> Items,
    [property: Id(1)] int TotalCount,
    [property: Id(2)] bool Truncated);
```

```csharp
// ReferenceSearch.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.reference-search")]
public sealed record ReferenceSearch([property: Id(0)] string SymbolId, [property: Id(1)] int Limit = 50);
```

```csharp
// ReferenceHit.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.reference-hit")]
public sealed record ReferenceHit(
    [property: Id(0)] string Path,
    [property: Id(1)] int Line,
    [property: Id(2)] string Project,
    [property: Id(3)] string Text);
```

```csharp
// ReferenceSearchResult.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.reference-search-result")]
public sealed record ReferenceSearchResult(
    [property: Id(0)] string SymbolId,
    [property: Id(1)] IReadOnlyList<ReferenceHit> Items,
    [property: Id(2)] int TotalCount,
    [property: Id(3)] bool Truncated);
```

```csharp
// DiagnosticsQuery.cs
namespace DigitalBrain.Coding;

// Exactly one of Path or Project names the scope; both null means the whole solution.
[GenerateSerializer]
[Alias("coding.diagnostics-query")]
public sealed record DiagnosticsQuery(
    [property: Id(0)] string? Path = null,
    [property: Id(1)] string? Project = null,
    [property: Id(2)] int Limit = 50);
```

```csharp
// DiagnosticHit.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.diagnostic-hit")]
public sealed record DiagnosticHit(
    [property: Id(0)] string Id,
    [property: Id(1)] string Severity,
    [property: Id(2)] string Message,
    [property: Id(3)] string Path,
    [property: Id(4)] int Line);
```

```csharp
// DiagnosticsResult.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.diagnostics-result")]
public sealed record DiagnosticsResult(
    [property: Id(0)] IReadOnlyList<DiagnosticHit> Items,
    [property: Id(1)] int ErrorCount,
    [property: Id(2)] int WarningCount,
    [property: Id(3)] bool Truncated);
```

```csharp
// MapQuery.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.map-query")]
public sealed record MapQuery([property: Id(0)] bool IncludeDocumentCounts = true);
```

```csharp
// ProjectNode.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.project-node")]
public sealed record ProjectNode(
    [property: Id(0)] string Name,
    [property: Id(1)] string Path,
    [property: Id(2)] string Cluster,
    [property: Id(3)] int DocumentCount);
```

```csharp
// ProjectEdge.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.project-edge")]
public sealed record ProjectEdge([property: Id(0)] string From, [property: Id(1)] string To);
```

```csharp
// SolutionMap.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.solution-map")]
public sealed record SolutionMap(
    [property: Id(0)] string SolutionPath,
    [property: Id(1)] IReadOnlyList<ProjectNode> Projects,
    [property: Id(2)] IReadOnlyList<ProjectEdge> References);
```

```csharp
// CodingJson.cs
using System.Text.Json.Serialization;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Descriptors;
using DigitalBrain.Coding;

[assembly: NeuronJsonContext(typeof(CodingJson))]

namespace DigitalBrain.Coding;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(OpenWorkspace))]
[JsonSerializable(typeof(ReloadWorkspace))]
[JsonSerializable(typeof(WorkspaceReceipt))]
[JsonSerializable(typeof(Accepted<WorkspaceReceipt>))]
[JsonSerializable(typeof(OpeningBody))]
[JsonSerializable(typeof(WorkspacePhase))]
[JsonSerializable(typeof(WorkspaceSnapshot))]
[JsonSerializable(typeof(SymbolSearch))]
[JsonSerializable(typeof(SymbolHit))]
[JsonSerializable(typeof(SymbolSearchResult))]
[JsonSerializable(typeof(ReferenceSearch))]
[JsonSerializable(typeof(ReferenceHit))]
[JsonSerializable(typeof(ReferenceSearchResult))]
[JsonSerializable(typeof(DiagnosticsQuery))]
[JsonSerializable(typeof(DiagnosticHit))]
[JsonSerializable(typeof(DiagnosticsResult))]
[JsonSerializable(typeof(MapQuery))]
[JsonSerializable(typeof(ProjectNode))]
[JsonSerializable(typeof(ProjectEdge))]
[JsonSerializable(typeof(SolutionMap))]
public sealed partial class CodingJson : JsonSerializerContext;
```

- [ ] **Step 4: Build the contracts project**

Run: `dotnet build src/Modules/Coding/Contracts/DigitalBrain.Modules.Coding.Contracts.csproj -c Release`
Expected: success, no warnings. The test still fails (no grain implements `ICodeWorkspace`); that is Task 3.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Coding/Contracts tests/DigitalBrain.Tests/Features/Coding/CodeWorkspaceNeuronFacts.cs
git commit -m "coding: workspace contract, queries and JSON context"
```

---

### Task 3: The Roslyn service on an adhoc fixture

**Files:**
- Create: `ISolutionLoader.cs`, `WorkspaceStatus.cs`, `WorkspaceNotReadyException.cs`, `SolutionWorkspace.cs`,
  `SolutionQueries.cs`
- Create: `tests/DigitalBrain.Tests/Features/Coding/FixtureSolutions.cs`, `AdhocSolutionLoader.cs`
- Modify: `CodingModule.cs` (register the service), `SolutionWorkspaceFacts.cs`

**Interfaces:**
- Consumes: the DTOs from Task 2.
- Produces:
  - `public sealed record LoadedSolution(Workspace Workspace, IReadOnlyList<string> Failures)`
  - `public interface ISolutionLoader { Task<LoadedSolution> OpenAsync(string solutionPath, IProgress<string> progress, CancellationToken cancellationToken); }`
  - `public sealed record WorkspaceStatus(WorkspacePhase Phase, string? SolutionPath, int ProjectCount, int DocumentCount, string? Detail)`;
    a `Ready` status carries `Detail = "N load failures; first: ..."` when the loader reported any, else null
  - `public sealed class SolutionWorkspace` with `WorkspaceStatus Status`, `Solution? Current`,
    `Task BeginOpenAsync(string solutionPath)`, `Task BeginReloadAsync()`, `Task WhenReadyAsync(CancellationToken)`,
    `Task<SymbolSearchResult> FindSymbolsAsync(SymbolSearch, CancellationToken)`,
    `Task<ReferenceSearchResult> ReferencesAsync(ReferenceSearch, CancellationToken)`,
    `Task<DiagnosticsResult> DiagnosticsAsync(DiagnosticsQuery, CancellationToken)`,
    `Task<SolutionMap> MapAsync(MapQuery, CancellationToken)`.
  - `public sealed class WorkspaceNotReadyException(WorkspaceStatus status) : InvalidOperationException`.

- [ ] **Step 1: Write the fixture**

```csharp
// tests/DigitalBrain.Tests/Features/Coding/FixtureSolutions.cs
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace DigitalBrain.Tests.Coding;

// Two projects: Alpha declares Greeter, Beta references Alpha and calls it; Broken.cs has one error.
internal static class FixtureSolutions
{
    internal const string Root = "E:/fixture";
    internal const string GreeterPath = Root + "/Alpha/Greeter.cs";
    internal const string ProgramPath = Root + "/Beta/Program.cs";
    internal const string BrokenPath = Root + "/Beta/Broken.cs";

    private static readonly IReadOnlyList<MetadataReference> Runtime = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
        .Split(Path.PathSeparator)
        .Where(static path => Path.GetFileName(path) is "System.Runtime.dll" or "System.Private.CoreLib.dll" or "netstandard.dll" or "System.Console.dll")
        .Select(static path => (MetadataReference)MetadataReference.CreateFromFile(path))
        .ToArray();

    internal static Workspace TwoProjects()
    {
        var workspace = new AdhocWorkspace();
        var alpha = ProjectId.CreateNewId("Alpha");
        var beta = ProjectId.CreateNewId("Beta");
        var options = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(alpha, VersionStamp.Create(), "Alpha", "Alpha", LanguageNames.CSharp,
                filePath: Root + "/Alpha/Alpha.csproj", compilationOptions: options, metadataReferences: Runtime))
            .AddProject(ProjectInfo.Create(beta, VersionStamp.Create(), "Beta", "Beta", LanguageNames.CSharp,
                filePath: Root + "/Beta/Beta.csproj", compilationOptions: options, metadataReferences: Runtime,
                projectReferences: [new ProjectReference(alpha)]))
            .AddDocument(DocumentId.CreateNewId(alpha), "Greeter.cs", SourceText.From("""
                namespace Alpha;

                public sealed class Greeter
                {
                    public string Greet(string name) => $"Hello, {name}";
                }
                """), filePath: GreeterPath)
            .AddDocument(DocumentId.CreateNewId(beta), "Program.cs", SourceText.From("""
                using Alpha;

                namespace Beta;

                public static class Program
                {
                    public static string Run() => new Greeter().Greet("world");
                }
                """), filePath: ProgramPath)
            .AddDocument(DocumentId.CreateNewId(beta), "Broken.cs", SourceText.From("""
                namespace Beta;

                public static class Broken
                {
                    public static int Count() => "not a number";
                }
                """), filePath: BrokenPath);
        if (!workspace.TryApplyChanges(solution))
        {
            throw new InvalidOperationException("The adhoc fixture did not apply.");
        }

        return workspace;
    }
}
```

```csharp
// tests/DigitalBrain.Tests/Features/Coding/AdhocSolutionLoader.cs
using DigitalBrain.Coding;
using Microsoft.CodeAnalysis;

namespace DigitalBrain.Tests.Coding;

internal sealed class AdhocSolutionLoader(Func<Workspace> open) : ISolutionLoader
{
    public int Opens { get; private set; }

    public Task<LoadedSolution> OpenAsync(string solutionPath, IProgress<string> progress, CancellationToken cancellationToken)
    {
        Opens++;
        progress.Report($"opened {solutionPath}");
        return Task.FromResult(new LoadedSolution(open(), []));
    }
}
```

- [ ] **Step 2: Write the failing service facts**

Replace the body of `SolutionWorkspaceFacts.cs` with:

```csharp
using DigitalBrain.Coding;
using DigitalBrain.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class SolutionWorkspaceFacts
{
    private static async Task<SolutionWorkspace> ReadyAsync()
    {
        var workspace = new SolutionWorkspace(new AdhocSolutionLoader(FixtureSolutions.TwoProjects), TimeProvider.System, NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync("E:/fixture/Fixture.slnx");
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        return workspace;
    }

    [Fact]
    public async Task The_coding_module_composes_into_a_silo()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(CodingModule)]) });
        Assert.NotNull(brain.SiloServices.GetService(typeof(SolutionWorkspace)));
    }

    [Fact]
    public void A_fresh_workspace_is_not_opened()
    {
        using var workspace = new SolutionWorkspace(new AdhocSolutionLoader(FixtureSolutions.TwoProjects), TimeProvider.System, NullLogger<SolutionWorkspace>.Instance);
        Assert.Equal(WorkspacePhase.NotOpened, workspace.Status.Phase);
        Assert.Throws<WorkspaceNotReadyException>(() => workspace.FindSymbolsAsync(new("Greeter"), CancellationToken.None).GetAwaiter().GetResult());
    }

    [Fact]
    public async Task Opening_reports_projects_and_documents()
    {
        using var workspace = await ReadyAsync();
        Assert.Equal(WorkspacePhase.Ready, workspace.Status.Phase);
        Assert.Equal(2, workspace.Status.ProjectCount);
        Assert.Equal(3, workspace.Status.DocumentCount);
        Assert.Null(workspace.Status.Detail);
    }

    [Fact]
    public async Task Load_failures_stay_visible_on_a_ready_workspace()
    {
        var loader = new FailingAdhocLoader(FixtureSolutions.TwoProjects, ["Alpha.csproj: reference Missing.dll not found"]);
        using var workspace = new SolutionWorkspace(loader, TimeProvider.System, NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync("E:/fixture/Fixture.slnx");
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WorkspacePhase.Ready, workspace.Status.Phase);
        Assert.Equal("1 load failures; first: Alpha.csproj: reference Missing.dll not found", workspace.Status.Detail);
    }

    private sealed class FailingAdhocLoader(Func<Workspace> open, IReadOnlyList<string> failures) : ISolutionLoader
    {
        public Task<LoadedSolution> OpenAsync(string solutionPath, IProgress<string> progress, CancellationToken cancellationToken)
            => Task.FromResult(new LoadedSolution(open(), failures));
    }

    [Fact]
    public async Task Find_symbols_returns_the_type_with_a_documentation_id()
    {
        using var workspace = await ReadyAsync();
        var result = await workspace.FindSymbolsAsync(new("greet"), TestContext.Current.CancellationToken);
        Assert.False(result.Truncated);
        Assert.Equal(2, result.TotalCount);
        var type = Assert.Single(result.Items, hit => hit.Kind == "NamedType");
        Assert.Equal("T:Alpha.Greeter", type.Id);
        Assert.Equal("Alpha", type.Project);
        Assert.Equal(FixtureSolutions.GreeterPath, type.Path);
        Assert.Equal(3, type.Line);
        Assert.Contains(result.Items, hit => hit.Id == "M:Alpha.Greeter.Greet(System.String)");
    }

    [Fact]
    public async Task Find_symbols_honours_the_limit()
    {
        using var workspace = await ReadyAsync();
        var result = await workspace.FindSymbolsAsync(new("greet", Limit: 1), TestContext.Current.CancellationToken);
        Assert.Single(result.Items);
        Assert.True(result.Truncated);
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task References_cross_the_project_boundary()
    {
        using var workspace = await ReadyAsync();
        var result = await workspace.ReferencesAsync(new("M:Alpha.Greeter.Greet(System.String)"), TestContext.Current.CancellationToken);
        var hit = Assert.Single(result.Items);
        Assert.Equal(FixtureSolutions.ProgramPath, hit.Path);
        Assert.Equal("Beta", hit.Project);
        Assert.Equal(7, hit.Line);
        Assert.Equal("""public static string Run() => new Greeter().Greet("world");""", hit.Text);
    }

    [Fact]
    public async Task References_of_an_unknown_id_is_advice_not_a_crash()
    {
        using var workspace = await ReadyAsync();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => workspace.ReferencesAsync(new("T:Nowhere.Missing"), TestContext.Current.CancellationToken));
        Assert.Contains("T:Nowhere.Missing", error.Message, StringComparison.Ordinal);
        Assert.Contains("find-symbols", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnostics_for_a_document_report_its_error()
    {
        using var workspace = await ReadyAsync();
        var result = await workspace.DiagnosticsAsync(new(Path: FixtureSolutions.BrokenPath), TestContext.Current.CancellationToken);
        var hit = Assert.Single(result.Items);
        Assert.Equal("CS0029", hit.Id);
        Assert.Equal("Error", hit.Severity);
        Assert.Equal(5, hit.Line);
        Assert.Equal(1, result.ErrorCount);
    }

    [Fact]
    public async Task Diagnostics_for_a_clean_project_are_empty()
    {
        using var workspace = await ReadyAsync();
        var result = await workspace.DiagnosticsAsync(new(Project: "Alpha"), TestContext.Current.CancellationToken);
        Assert.Empty(result.Items);
        Assert.Equal(0, result.ErrorCount);
    }

    [Fact]
    public async Task The_map_lists_projects_clusters_and_references()
    {
        using var workspace = await ReadyAsync();
        var map = await workspace.MapAsync(new(), TestContext.Current.CancellationToken);
        Assert.Equal(["Alpha", "Beta"], map.Projects.Select(project => project.Name).Order());
        Assert.Equal("Alpha", Assert.Single(map.Projects, project => project.Name == "Alpha").Cluster);
        Assert.Equal(2, Assert.Single(map.Projects, project => project.Name == "Beta").DocumentCount);
        var edge = Assert.Single(map.References);
        Assert.Equal(("Beta", "Alpha"), (edge.From, edge.To));
    }

    [Fact]
    public async Task Reload_opens_again_and_bumps_nothing_durable()
    {
        var loader = new AdhocSolutionLoader(FixtureSolutions.TwoProjects);
        using var workspace = new SolutionWorkspace(loader, TimeProvider.System, NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync("E:/fixture/Fixture.slnx");
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        await workspace.BeginReloadAsync();
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, loader.Opens);
        Assert.Equal(WorkspacePhase.Ready, workspace.Status.Phase);
    }
}
```

- [ ] **Step 3: Run to verify the facts fail**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.SolutionWorkspaceFacts`
Expected: compile errors for `SolutionWorkspace`, `ISolutionLoader`, `WorkspaceNotReadyException`.

- [ ] **Step 4: Write the seam, status and exception**

```csharp
// src/Modules/Coding/Coding/ISolutionLoader.cs
using Microsoft.CodeAnalysis;

namespace DigitalBrain.Coding;

// Failures are the workspace diagnostics a loader saw while opening (a project that did not evaluate,
// a reference that did not resolve); the solution still opened, so they travel next to it.
public sealed record LoadedSolution(Workspace Workspace, IReadOnlyList<string> Failures);

public interface ISolutionLoader
{
    Task<LoadedSolution> OpenAsync(string solutionPath, IProgress<string> progress, CancellationToken cancellationToken);
}
```

```csharp
// src/Modules/Coding/Coding/WorkspaceStatus.cs
namespace DigitalBrain.Coding;

public sealed record WorkspaceStatus(WorkspacePhase Phase, string? SolutionPath, int ProjectCount, int DocumentCount, string? Detail)
{
    public static readonly WorkspaceStatus NotOpened = new(WorkspacePhase.NotOpened, null, 0, 0, null);

    public string Advice => Phase switch
    {
        WorkspacePhase.NotOpened => "No solution is open. Open one with the workspace's open command.",
        WorkspacePhase.Opening => $"The solution is still opening ({Detail}). Try again in a moment.",
        WorkspacePhase.Failed => $"The solution failed to open: {Detail}. Fix the cause and reload.",
        _ => "The workspace is ready.",
    };
}
```

```csharp
// src/Modules/Coding/Coding/WorkspaceNotReadyException.cs
namespace DigitalBrain.Coding;

public sealed class WorkspaceNotReadyException(WorkspaceStatus status) : InvalidOperationException(status.Advice)
{
    public WorkspaceStatus Status { get; } = status;
}
```

- [ ] **Step 5: Write the service**

```csharp
// src/Modules/Coding/Coding/SolutionWorkspace.cs
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Coding;

public sealed class SolutionWorkspace(ISolutionLoader loader, TimeProvider clock, ILogger<SolutionWorkspace> logger) : IDisposable
{
    private readonly Lock _gate = new();
    private readonly CancellationTokenSource _lifetime = new();
    private Workspace? _workspace;
    private Task _pending = Task.CompletedTask;
    private WorkspaceStatus _status = WorkspaceStatus.NotOpened;
    private string? _solutionPath;

    public WorkspaceStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public Solution? Current => _workspace?.CurrentSolution;

    public DateTimeOffset? ReadyAt { get; private set; }

    // Starts the load and returns at once; the returned task completes with the load and never faults.
    public Task BeginOpenAsync(string solutionPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        lock (_gate)
        {
            if (_status.Phase == WorkspacePhase.Opening && string.Equals(_solutionPath, solutionPath, StringComparison.OrdinalIgnoreCase))
            {
                return _pending;
            }

            _solutionPath = solutionPath;
            _status = new WorkspaceStatus(WorkspacePhase.Opening, solutionPath, 0, 0, "starting");
            _pending = OpenCoreAsync(solutionPath, _lifetime.Token);
            return _pending;
        }
    }

    public Task BeginReloadAsync()
    {
        var path = _solutionPath ?? throw new WorkspaceNotReadyException(Status);
        lock (_gate)
        {
            _status = new WorkspaceStatus(WorkspacePhase.Opening, path, 0, 0, "reloading");
            _pending = OpenCoreAsync(path, _lifetime.Token);
            return _pending;
        }
    }

    public async Task WhenReadyAsync(CancellationToken cancellationToken)
    {
        Task pending;
        lock (_gate)
        {
            pending = _pending;
        }

        await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (Status.Phase != WorkspacePhase.Ready)
        {
            throw new WorkspaceNotReadyException(Status);
        }
    }

    public Task<SymbolSearchResult> FindSymbolsAsync(SymbolSearch query, CancellationToken cancellationToken)
        => SolutionQueries.FindSymbolsAsync(Ready(), query, cancellationToken);

    public Task<ReferenceSearchResult> ReferencesAsync(ReferenceSearch query, CancellationToken cancellationToken)
        => SolutionQueries.ReferencesAsync(Ready(), query, cancellationToken);

    public Task<DiagnosticsResult> DiagnosticsAsync(DiagnosticsQuery query, CancellationToken cancellationToken)
        => SolutionQueries.DiagnosticsAsync(Ready(), query, cancellationToken);

    public Task<SolutionMap> MapAsync(MapQuery query, CancellationToken cancellationToken)
        => SolutionQueries.MapAsync(Ready(), _solutionPath ?? string.Empty, query, cancellationToken);

    public void Dispose()
    {
        _lifetime.Cancel();
        _workspace?.Dispose();
        _lifetime.Dispose();
    }

    private Solution Ready()
    {
        var status = Status;
        return status.Phase == WorkspacePhase.Ready && _workspace is { } workspace
            ? workspace.CurrentSolution
            : throw new WorkspaceNotReadyException(status);
    }

    private async Task OpenCoreAsync(string solutionPath, CancellationToken cancellationToken)
    {
        // Yield so the caller's lock is released before any loader work runs.
        await Task.Yield();
        var progress = new Progress<string>(detail =>
        {
            lock (_gate)
            {
                if (_status.Phase == WorkspacePhase.Opening)
                {
                    _status = _status with { Detail = detail };
                }
            }
        });
        Workspace? previous;
        try
        {
            var loaded = await loader.OpenAsync(solutionPath, progress, cancellationToken).ConfigureAwait(false);
            var solution = loaded.Workspace.CurrentSolution;
            var documents = solution.Projects.Sum(static project => project.DocumentIds.Count);
            var detail = loaded.Failures.Count == 0 ? null : $"{loaded.Failures.Count} load failures; first: {loaded.Failures[0]}";
            lock (_gate)
            {
                previous = _workspace;
                _workspace = loaded.Workspace;
                _status = new WorkspaceStatus(WorkspacePhase.Ready, solutionPath, solution.ProjectIds.Count, documents, detail);
                ReadyAt = clock.GetUtcNow();
            }
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            logger.LogError(error, "Opening {SolutionPath} failed.", solutionPath);
            lock (_gate)
            {
                previous = null;
                _status = new WorkspaceStatus(WorkspacePhase.Failed, solutionPath, 0, 0, error.Message);
            }
        }

        previous?.Dispose();
    }
}
```

- [ ] **Step 6: Write the queries**

```csharp
// src/Modules/Coding/Coding/SolutionQueries.cs
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.FindSymbols;

namespace DigitalBrain.Coding;

internal static class SolutionQueries
{
    private const int MaxLimit = 200;

    internal static async Task<SymbolSearchResult> FindSymbolsAsync(Solution solution, SymbolSearch query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.Query);
        var limit = Math.Clamp(query.Limit, 1, MaxLimit);
        var declarations = await SymbolFinder.FindSourceDeclarationsAsync(solution,
            name => name.Contains(query.Query, StringComparison.OrdinalIgnoreCase), SymbolFilter.TypeAndMember, cancellationToken).ConfigureAwait(false);
        var hits = declarations
            .Select(symbol => Hit(solution, symbol))
            .OfType<SymbolHit>()
            .OrderBy(static hit => hit.Kind == "NamedType" ? 0 : 1)
            .ThenBy(static hit => hit.Name, StringComparer.Ordinal)
            .ToArray();
        return new SymbolSearchResult([.. hits.Take(limit)], hits.Length, hits.Length > limit);
    }

    internal static async Task<ReferenceSearchResult> ReferencesAsync(Solution solution, ReferenceSearch query, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query.SymbolId);
        var limit = Math.Clamp(query.Limit, 1, MaxLimit);
        var symbol = await ResolveAsync(solution, query.SymbolId, cancellationToken).ConfigureAwait(false);
        var referenced = await SymbolFinder.FindReferencesAsync(symbol, solution, cancellationToken).ConfigureAwait(false);
        var hits = new List<ReferenceHit>();
        foreach (var location in referenced.SelectMany(static reference => reference.Locations))
        {
            var document = location.Document;
            var span = location.Location.GetLineSpan();
            var text = await document.GetTextAsync(cancellationToken).ConfigureAwait(false);
            var line = span.StartLinePosition.Line;
            hits.Add(new ReferenceHit(document.FilePath ?? document.Name, line + 1, document.Project.Name, text.Lines[line].ToString().Trim()));
        }

        hits.Sort(static (left, right) => string.CompareOrdinal(left.Path, right.Path) is var byPath && byPath != 0 ? byPath : left.Line.CompareTo(right.Line));
        return new ReferenceSearchResult(query.SymbolId, [.. hits.Take(limit)], hits.Count, hits.Count > limit);
    }

    internal static async Task<DiagnosticsResult> DiagnosticsAsync(Solution solution, DiagnosticsQuery query, CancellationToken cancellationToken)
    {
        var limit = Math.Clamp(query.Limit, 1, MaxLimit);
        IEnumerable<Diagnostic> diagnostics;
        if (!string.IsNullOrWhiteSpace(query.Path))
        {
            var id = solution.GetDocumentIdsWithFilePath(query.Path).FirstOrDefault()
                ?? throw new InvalidOperationException($"No document at '{query.Path}' is in the solution. Use a path from find-symbols.");
            var model = await solution.GetDocument(id)!.GetSemanticModelAsync(cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException($"'{query.Path}' has no semantic model.");
            diagnostics = model.GetDiagnostics(cancellationToken: cancellationToken);
        }
        else
        {
            var projects = string.IsNullOrWhiteSpace(query.Project)
                ? solution.Projects
                : solution.Projects.Where(project => string.Equals(project.Name, query.Project, StringComparison.OrdinalIgnoreCase));
            var collected = new List<Diagnostic>();
            foreach (var project in projects)
            {
                var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false)
                    ?? throw new InvalidOperationException($"Project '{project.Name}' has no compilation.");
                collected.AddRange(compilation.GetDiagnostics(cancellationToken));
            }

            diagnostics = collected;
        }

        var hits = diagnostics
            .Where(static diagnostic => diagnostic.Severity >= DiagnosticSeverity.Warning && diagnostic.Location.IsInSource)
            .Select(static diagnostic => new DiagnosticHit(diagnostic.Id, diagnostic.Severity.ToString(), diagnostic.GetMessage(),
                diagnostic.Location.SourceTree!.FilePath, diagnostic.Location.GetLineSpan().StartLinePosition.Line + 1))
            .OrderBy(static hit => hit.Severity == "Error" ? 0 : 1)
            .ThenBy(static hit => hit.Path, StringComparer.Ordinal)
            .ThenBy(static hit => hit.Line)
            .ToArray();
        return new DiagnosticsResult([.. hits.Take(limit)],
            hits.Count(static hit => hit.Severity == "Error"), hits.Count(static hit => hit.Severity == "Warning"), hits.Length > limit);
    }

    internal static Task<SolutionMap> MapAsync(Solution solution, string solutionPath, MapQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var root = string.IsNullOrEmpty(solutionPath) ? null : Path.GetDirectoryName(solutionPath);
        var graph = solution.GetProjectDependencyGraph();
        var byId = solution.Projects.ToDictionary(static project => project.Id);
        var nodes = graph.GetTopologicallySortedProjects(cancellationToken)
            .Select(id => byId[id])
            .Select(project => new ProjectNode(project.Name, project.FilePath ?? project.Name, ClusterOf(root, project),
                query.IncludeDocumentCounts ? project.DocumentIds.Count : 0))
            .ToArray();
        var edges = solution.Projects
            .SelectMany(project => project.ProjectReferences
                .Where(reference => byId.ContainsKey(reference.ProjectId))
                .Select(reference => new ProjectEdge(project.Name, byId[reference.ProjectId].Name)))
            .OrderBy(static edge => edge.From, StringComparer.Ordinal)
            .ThenBy(static edge => edge.To, StringComparer.Ordinal)
            .ToArray();
        return Task.FromResult(new SolutionMap(solutionPath, nodes, edges));
    }

    // "src/Modules/AI/AI/x.csproj" clusters as "Modules/AI"; "src/Kernel/DigitalBrain/x.csproj" as "Kernel";
    // a project outside src clusters by its own folder name.
    private static string ClusterOf(string? root, Project project)
    {
        var path = project.FilePath;
        if (path is null)
        {
            return project.Name;
        }

        var relative = root is null ? path : Path.GetRelativePath(root, path);
        var segments = relative.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments switch
        {
            ["src", "Modules", var module, ..] => $"Modules/{module}",
            ["src", var area, ..] => area,
            [var single] => Path.GetFileNameWithoutExtension(single),
            _ => segments[^2],
        };
    }

    private static SymbolHit? Hit(Solution solution, ISymbol symbol)
    {
        var location = symbol.Locations.FirstOrDefault(static location => location.IsInSource);
        if (location is null || symbol.GetDocumentationCommentId() is not { } id)
        {
            return null;
        }

        var document = solution.GetDocument(location.SourceTree);
        return new SymbolHit(id, symbol.Kind.ToString(), symbol.Name,
            symbol.ToDisplayString(SymbolDisplayFormat.CSharpErrorMessageFormat), document?.Project.Name ?? string.Empty,
            location.SourceTree!.FilePath, location.GetLineSpan().StartLinePosition.Line + 1);
    }

    private static async Task<ISymbol> ResolveAsync(Solution solution, string symbolId, CancellationToken cancellationToken)
    {
        foreach (var project in solution.Projects)
        {
            var compilation = await project.GetCompilationAsync(cancellationToken).ConfigureAwait(false);
            if (compilation is not null && DocumentationCommentId.GetFirstSymbolForDeclarationId(symbolId, compilation) is { } symbol
                && symbol.Locations.Any(static location => location.IsInSource))
            {
                return symbol;
            }
        }

        throw new InvalidOperationException($"No symbol '{symbolId}' is declared in the solution. Use an id returned by find-symbols.");
    }
}
```

- [ ] **Step 7: Register the service**

`CodingModule.Configure` becomes:

```csharp
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton<CodingModule>();
        builder.Services.TryAddSingleton(TimeProvider.System);
        builder.Services.TryAddSingleton<SolutionWorkspace>();
    }
```

(The loader registration arrives in Task 4; until then a silo without a loader still composes because
`SolutionWorkspace` is resolved lazily.)

- [ ] **Step 8: Run the facts**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.SolutionWorkspaceFacts`
Expected: all PASS. If `Find_symbols_returns_the_type_with_a_documentation_id` reports a different
`TotalCount`, print the hits: the predicate matches declared names only, so exactly `Greeter` and `Greet`
match "greet". If the reference line text differs, the fixture's raw-string indentation changed; keep the
source lines flush with the `"""` delimiters.

- [ ] **Step 9: Commit**

```bash
git add src/Modules/Coding tests/DigitalBrain.Tests/Features/Coding
git commit -m "coding: Roslyn workspace service with symbol, reference, diagnostics and map queries"
```

---

### Task 4: The MSBuild loader, warmup, and the gated self-test

**Files:**
- Create: `MSBuildSolutionLoader.cs`, `WorkspaceWarmup.cs`, `tests/DigitalBrain.Tests/Features/Coding/CodingSelfTestFacts.cs`
- Modify: `CodingModule.cs`

**Interfaces:**
- Produces: `MSBuildSolutionLoader : ISolutionLoader` (registered `TryAddSingleton`), `WorkspaceWarmup : IHostedService`.

- [ ] **Step 1: Write the gated self-test**

```csharp
// tests/DigitalBrain.Tests/Features/Coding/CodingSelfTestFacts.cs
using DigitalBrain.Coding;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Tests.Coding;

// Opens this repository's own solution through MSBuild. Slow (about a minute) and needs a restored tree,
// so it runs only when DIGITALBRAIN_CODING_SELF_TESTS=1.
public sealed class CodingSelfTestFacts
{
    private static readonly string SolutionPath = FindSolution();

    [Fact]
    public async Task The_real_solution_opens_and_answers_a_known_reference()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("DIGITALBRAIN_CODING_SELF_TESTS") == "1", "set DIGITALBRAIN_CODING_SELF_TESTS=1 to run");
        using var workspace = new SolutionWorkspace(new MSBuildSolutionLoader(), TimeProvider.System, NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync(SolutionPath);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        await workspace.WhenReadyAsync(timeout.Token);
        Assert.True(workspace.Status.ProjectCount >= 30, workspace.Status.Detail);
        // A partial load must never pass as ready on our own solution (design section 8, finding 5).
        Assert.Null(workspace.Status.Detail);

        var symbols = await workspace.FindSymbolsAsync(new("ITimer"), timeout.Token);
        var contract = Assert.Single(symbols.Items, hit => hit.Id == "T:DigitalBrain.Time.ITimer");
        var references = await workspace.ReferencesAsync(new(contract.Id), timeout.Token);
        Assert.Contains(references.Items, hit => hit.Path.EndsWith("TimerNeuron.cs", StringComparison.OrdinalIgnoreCase));

        var map = await workspace.MapAsync(new(), timeout.Token);
        Assert.Contains(map.Projects, project => project.Name == "DigitalBrain.Modules.Time" && project.Cluster == "Modules/Time");
        Assert.Contains(map.References, edge => edge.From == "DigitalBrain.Modules.Time" && edge.To == "DigitalBrain");
    }

    private static string FindSolution()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DigitalBrain.slnx")))
        {
            directory = directory.Parent;
        }

        return directory is null ? "DigitalBrain.slnx" : Path.Combine(directory.FullName, "DigitalBrain.slnx");
    }
}
```

- [ ] **Step 2: Run it ungated to see it fail to compile**

Run: `dotnet build tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release`
Expected: CS0246 `MSBuildSolutionLoader`.

- [ ] **Step 3: Write the loader**

```csharp
// src/Modules/Coding/Coding/MSBuildSolutionLoader.cs
using System.Runtime.CompilerServices;
using Microsoft.Build.Locator;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.MSBuild;

namespace DigitalBrain.Coding;

public sealed class MSBuildSolutionLoader : ISolutionLoader
{
    private static readonly Lock RegistrationGate = new();

    public Task<LoadedSolution> OpenAsync(string solutionPath, IProgress<string> progress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        ArgumentNullException.ThrowIfNull(progress);
        if (!File.Exists(solutionPath))
        {
            throw new FileNotFoundException($"Solution '{solutionPath}' does not exist.", solutionPath);
        }

        EnsureLocatorRegistered();
        return OpenCoreAsync(solutionPath, progress, cancellationToken);
    }

    // The silo registers the locator as its first statement (Program.cs); this guard is for the test
    // process, which has no such entry point. It must run before any Microsoft.Build type is
    // JIT-compiled, so nothing in this method or its callers references one; OpenCoreAsync is kept out
    // of line for the same reason.
    private static void EnsureLocatorRegistered()
    {
        lock (RegistrationGate)
        {
            if (!MSBuildLocator.IsRegistered)
            {
                MSBuildLocator.RegisterDefaults();
            }
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<LoadedSolution> OpenCoreAsync(string solutionPath, IProgress<string> progress, CancellationToken cancellationToken)
    {
        var workspace = MSBuildWorkspace.Create();
        var failures = new List<string>();
        workspace.RegisterWorkspaceFailedHandler(args =>
        {
            failures.Add(args.Diagnostic.Message);
            progress.Report(args.Diagnostic.Message);
        });
        try
        {
            await workspace.OpenSolutionAsync(solutionPath,
                new Progress<ProjectLoadProgress>(load => progress.Report($"{load.Operation} {Path.GetFileNameWithoutExtension(load.FilePath)}")),
                cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }

        if (workspace.CurrentSolution.ProjectIds.Count == 0)
        {
            var reason = failures.Count == 0 ? "no projects were loaded" : failures[0];
            workspace.Dispose();
            throw new InvalidOperationException($"Solution '{solutionPath}' loaded no projects: {reason}");
        }

        return new LoadedSolution(workspace, failures);
    }
}
```

If `RegisterWorkspaceFailedHandler` returns a registration object in 5.9.0, keep it alive by storing it in a
field of a small holder disposed with the workspace; the compiler tells you at this step.

`src/Kernel/DigitalBrain.Silo/Program.cs`: make the locator registration the **first statement** of the
file, before `WebApplication.CreateBuilder`, exactly as IAW does (research R2.1), so no other module can
load a `Microsoft.Build` assembly first:

```csharp
Microsoft.Build.Locator.MSBuildLocator.RegisterDefaults();
```

The silo project already references the module, which carries the package. If the analyzers flag the
fully qualified call, add `using Microsoft.Build.Locator;` at the top and call `MSBuildLocator.RegisterDefaults();`.

- [ ] **Step 4: Write the warmup and register both**

```csharp
// src/Modules/Coding/Coding/WorkspaceWarmup.cs
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Coding;

// Opens the configured solution when the silo starts so the first question does not pay for the load.
internal sealed class WorkspaceWarmup(SolutionWorkspace workspace, IConfiguration configuration) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var path = configuration[CodingModule.SolutionPathKey];
        if (!string.IsNullOrWhiteSpace(path))
        {
            _ = workspace.BeginOpenAsync(Path.GetFullPath(path));
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

`CodingModule.Configure` adds, after the `SolutionWorkspace` line:

```csharp
        builder.Services.TryAddSingleton<ISolutionLoader, MSBuildSolutionLoader>();
        builder.Services.AddHostedService<WorkspaceWarmup>();
```

- [ ] **Step 5: Run the self-test**

Run (PowerShell): `$env:DIGITALBRAIN_CODING_SELF_TESTS=1; dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.CodingSelfTestFacts`
Expected: PASS within five minutes. If it fails with "The build host could not be found", the
`BuildHost-netcore` folder is missing from the test output: check that the Coding project's package
references are `PackageReference` (not `PrivateAssets=all`) so the folder flows transitively. If every
project loads with zero documents, the tree is not restored: run `dotnet restore DigitalBrain.slnx` first.
Then run the whole suite once without the variable and confirm the fact is reported as skipped.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Coding src/Kernel/DigitalBrain.Silo/Program.cs tests/DigitalBrain.Tests/Features/Coding/CodingSelfTestFacts.cs
git commit -m "coding: MSBuild loader, silo warmup and the gated self-test on the real solution"
```

---

### Task 5: The `workspace` neuron

**Files:**
- Create: `WorkspaceState.cs`, `WorkspaceNeuron.cs`
- Modify: `CodeWorkspaceNeuronFacts.cs`

**Interfaces:**
- Consumes: `SolutionWorkspace` (Task 3), the contract (Task 2).
- Produces: `[GrainType("workspace")] WorkspaceNeuron`.

- [ ] **Step 1: Write the failing neuron facts**

Add to `CodeWorkspaceNeuronFacts`:

```csharp
    private static Task<BrainSimulation> StartAsync() => BrainSimulation.StartAsync(new()
    {
        Modules = new([typeof(CodingModule)]),
        ConfigureSilo = silo => silo.Services.AddSingleton<ISolutionLoader>(new AdhocSolutionLoader(FixtureSolutions.TwoProjects)),
    });

    private static async Task<WorkspaceSnapshot> WaitAsync(ICodeWorkspace workspace, WorkspacePhase phase)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            var snapshot = await workspace.Read();
            if (snapshot.Phase == phase)
            {
                return snapshot;
            }

            await Task.Delay(25, timeout.Token);
        }
    }

    [Fact]
    public async Task Open_is_accepted_and_the_workspace_becomes_ready()
    {
        await using var brain = await StartAsync();
        var workspace = brain.Grains.GetGrain<ICodeWorkspace>(new NeuronId(CodingVocabulary.WorkspaceType, "fixture").ToGrainId());
        var accepted = await workspace.Open(new OpenWorkspace(CommandId.New(), "E:/fixture/Fixture.slnx"));
        Assert.Equal("fixture", accepted.Receipt.Key);
        var ready = await WaitAsync(workspace, WorkspacePhase.Ready);
        Assert.Equal("E:/fixture/Fixture.slnx", ready.SolutionPath);
        Assert.Equal(2, ready.ProjectCount);
        Assert.Equal(1, ready.Generation);
    }

    [Fact]
    public async Task Open_refuses_a_blank_path_with_advice()
    {
        await using var brain = await StartAsync();
        var workspace = brain.Grains.GetGrain<ICodeWorkspace>(new NeuronId(CodingVocabulary.WorkspaceType, "fixture").ToGrainId());
        var error = await Assert.ThrowsAnyAsync<Exception>(() => workspace.Open(new OpenWorkspace(CommandId.New(), " ")));
        Assert.Contains("solution path", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Queries_go_through_the_neuron()
    {
        await using var brain = await StartAsync();
        var workspace = brain.Grains.GetGrain<ICodeWorkspace>(new NeuronId(CodingVocabulary.WorkspaceType, "fixture").ToGrainId());
        await workspace.Open(new OpenWorkspace(CommandId.New(), "E:/fixture/Fixture.slnx"));
        await WaitAsync(workspace, WorkspacePhase.Ready);
        var symbols = await workspace.FindSymbols(new("Greeter"), TestContext.Current.CancellationToken);
        var type = Assert.Single(symbols.Items, hit => hit.Kind == "NamedType");
        var references = await workspace.References(new(type.Id), TestContext.Current.CancellationToken);
        Assert.Single(references.Items);
        var diagnostics = await workspace.Diagnostics(new(Path: FixtureSolutions.BrokenPath), TestContext.Current.CancellationToken);
        Assert.Equal(1, diagnostics.ErrorCount);
        var map = await workspace.Map(new(), TestContext.Current.CancellationToken);
        Assert.Equal(2, map.Projects.Count);
    }

    [Fact]
    public async Task A_query_before_open_is_advice()
    {
        await using var brain = await StartAsync();
        var workspace = brain.Grains.GetGrain<ICodeWorkspace>(new NeuronId(CodingVocabulary.WorkspaceType, "fixture").ToGrainId());
        var error = await Assert.ThrowsAnyAsync<Exception>(() => workspace.FindSymbols(new("Greeter"), TestContext.Current.CancellationToken));
        Assert.Contains("No solution is open", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reload_bumps_the_generation_and_stays_ready()
    {
        await using var brain = await StartAsync();
        var workspace = brain.Grains.GetGrain<ICodeWorkspace>(new NeuronId(CodingVocabulary.WorkspaceType, "fixture").ToGrainId());
        await workspace.Open(new OpenWorkspace(CommandId.New(), "E:/fixture/Fixture.slnx"));
        await WaitAsync(workspace, WorkspacePhase.Ready);
        await workspace.Reload(new ReloadWorkspace(CommandId.New()));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        WorkspaceSnapshot snapshot;
        do
        {
            await Task.Delay(25, timeout.Token);
            snapshot = await workspace.Read();
        } while (snapshot.Generation < 2 || snapshot.Phase != WorkspacePhase.Ready);
        Assert.Equal(2, snapshot.Generation);
    }
```

Add the usings `DigitalBrain.Abstractions.Commands` and `Microsoft.Extensions.DependencyInjection` at the top.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.CodeWorkspaceNeuronFacts`
Expected: the first fact fails with an Orleans "no implementation for ICodeWorkspace" error; the rest
fail the same way.

- [ ] **Step 3: Write the state and the neuron**

```csharp
// src/Modules/Coding/Coding/WorkspaceState.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.workspace-state")]
internal sealed record WorkspaceState(
    [property: Id(0)] string SolutionPath,
    [property: Id(1)] long Generation,
    [property: Id(2)] DateTimeOffset RequestedAt);
```

```csharp
// src/Modules/Coding/Coding/WorkspaceNeuron.cs
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Coding;

[GrainType(CodingVocabulary.WorkspaceType)]
internal sealed class WorkspaceNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<WorkspaceState>> state,
    SolutionWorkspace workspace)
    : Neuron<WorkspaceState>(runtime, state), ICodeWorkspace
{
    public Task<Accepted<WorkspaceReceipt>> Open(OpenWorkspace command) => ExecuteCommandAsync(
        Descriptor("open"), command, CodingJson.Default.OpenWorkspace, CodingJson.Default.AcceptedWorkspaceReceipt, arguments =>
        {
            if (string.IsNullOrWhiteSpace(arguments.SolutionPath))
            {
                throw new CommandRejectedException(arguments.Id, "solution path is blank", "Provide the full path of a .slnx or .sln file.");
            }

            var generation = State?.Generation ?? 0;
            if (arguments.ExpectedVersion is { } expected && expected != generation)
            {
                throw new CommandRejectedException(arguments.Id, $"expected generation {expected} but the workspace is at {generation}",
                    "Read the workspace and retry with the generation it reports.");
            }

            var work = Schedule(Signal.FromJson(CodingVocabulary.WorkspaceOpening, new OpeningBody(arguments.SolutionPath), CodingJson.Default.OpeningBody));
            return new Accepted<WorkspaceReceipt>(new WorkspaceReceipt(Id.Name, generation), work);
        });

    public Task<Accepted<WorkspaceReceipt>> Reload(ReloadWorkspace command) => ExecuteCommandAsync(
        Descriptor("reload"), command, CodingJson.Default.ReloadWorkspace, CodingJson.Default.AcceptedWorkspaceReceipt, arguments =>
        {
            if (State is null)
            {
                throw new CommandRejectedException(arguments.Id, "no solution has been opened", "Open a solution before reloading.");
            }

            var work = Schedule(Signal.Create(CodingVocabulary.WorkspaceReloading, "{}"));
            return new Accepted<WorkspaceReceipt>(new WorkspaceReceipt(Id.Name, State.Generation), work);
        });

    [ReadOnly]
    public Task<WorkspaceSnapshot> Read()
    {
        var live = workspace.Status;
        var phase = State is null ? WorkspacePhase.NotOpened : live.Phase;
        return Task.FromResult(new WorkspaceSnapshot(State?.SolutionPath, phase, live.ProjectCount, live.DocumentCount, live.Detail, State?.Generation ?? 0));
    }

    [ReadOnly]
    public Task<SymbolSearchResult> FindSymbols(SymbolSearch query, CancellationToken cancellationToken = default)
        => workspace.FindSymbolsAsync(query, cancellationToken);

    [ReadOnly]
    public Task<ReferenceSearchResult> References(ReferenceSearch query, CancellationToken cancellationToken = default)
        => workspace.ReferencesAsync(query, cancellationToken);

    [ReadOnly]
    public Task<DiagnosticsResult> Diagnostics(DiagnosticsQuery query, CancellationToken cancellationToken = default)
        => workspace.DiagnosticsAsync(query, cancellationToken);

    [ReadOnly]
    public Task<SolutionMap> Map(MapQuery query, CancellationToken cancellationToken = default)
        => workspace.MapAsync(query, cancellationToken);

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        switch (delivery.Signal.Type)
        {
            case CodingVocabulary.WorkspaceOpening:
                {
                    if (Body(delivery, CodingJson.Default.OpeningBody) is not { } body)
                    {
                        return;
                    }

                    // The load runs in the service; the reaction only records the request so a restart re-opens.
                    _ = workspace.BeginOpenAsync(body.SolutionPath);
                    await SaveAsync(new WorkspaceState(body.SolutionPath, (State?.Generation ?? 0) + 1, TimeProvider.GetUtcNow()), cancellationToken).ConfigureAwait(true);
                    break;
                }
            case CodingVocabulary.WorkspaceReloading:
                {
                    if (State is not { } current)
                    {
                        return;
                    }

                    _ = workspace.Status.Phase == WorkspacePhase.NotOpened ? workspace.BeginOpenAsync(current.SolutionPath) : workspace.BeginReloadAsync();
                    await SaveAsync(current with { Generation = current.Generation + 1, RequestedAt = TimeProvider.GetUtcNow() }, cancellationToken).ConfigureAwait(true);
                    break;
                }
            default:
                return;
        }
    }

    protected override Task OnNeuronActivatedAsync(CancellationToken cancellationToken)
    {
        // A silo that restarted still knows which solution this workspace had open.
        if (State is { } current && workspace.Status.Phase == WorkspacePhase.NotOpened)
        {
            _ = workspace.BeginOpenAsync(current.SolutionPath);
        }

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Run the facts**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.CodeWorkspaceNeuronFacts`
Expected: PASS. If the silo refuses to start with a descriptor rule message, read it: it names the method
and the rule; the contract in Task 2 satisfies every rule, so the likely cause is a DTO that lost
`[GenerateSerializer]` or a missing `CodingJson` entry. If `Open_refuses_a_blank_path_with_advice` sees a
different exception type, assert on `Message` only (it already does) and note the type in `NOTES.md`.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Coding tests/DigitalBrain.Tests/Features/Coding
git commit -m "coding: workspace neuron with open, reload and typed queries"
```

---

### Task 6: The four `code_*` tools

**Files:**
- Create: `CodingNativeTools.cs`, `tests/DigitalBrain.Tests/Features/Coding/CodingNativeToolFacts.cs`
- Modify: `CodingModule.cs`, `src/Modules/AI/AI/ConversationalAgent.cs:89`,
  `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Graph/GraphNodeKinds.cs`

**Interfaces:**
- Consumes: `SolutionWorkspace`, `NativeTools.Resolve(names)` from `DigitalBrain.AI`, `GraphNodeState`/`GraphEdgeState` from `DigitalBrain.UI`.
- Produces: native tools `code_find_symbols`, `code_references`, `code_diagnostics`, `code_map`;
  `GraphNodeKinds.Module = "module"`, `GraphNodeKinds.Entity = "entity"`; the map result JSON
  `{ kind: "graph", id, name, title, nodes: [{id,label,kind,cluster}], edges: [{id,sourceId,targetId,dotted}] }`
  where `id` and `name` are the same stable string `map-<8 hex chars of the solution path's SHA-256>`;
  the shell drops any tool result without an `id` (design section 8, finding 12).

- [ ] **Step 1: Write the failing tool facts**

```csharp
// tests/DigitalBrain.Tests/Features/Coding/CodingNativeToolFacts.cs
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.Coding;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class CodingNativeToolFacts
{
    private static async Task<(BrainSimulation Brain, NativeTools Tools)> StartAsync()
    {
        var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(AIModule), typeof(CodingModule)]),
            ConfigureSilo = silo => silo.Services.AddSingleton<ISolutionLoader>(new AdhocSolutionLoader(FixtureSolutions.TwoProjects)),
            Configuration = new Dictionary<string, string?> { [CodingModule.SolutionPathKey] = "E:/fixture/Fixture.slnx" },
        });
        await brain.SiloServices.GetRequiredService<SolutionWorkspace>().WhenReadyAsync(TestContext.Current.CancellationToken);
        return (brain, brain.SiloServices.GetRequiredService<NativeTools>());
    }

    private static async Task<JsonElement> InvokeAsync(NativeTools tools, string name, Dictionary<string, object?> arguments)
    {
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(tools.Resolve([name])));
        var result = await tool.InvokeAsync(new AIFunctionArguments(arguments), TestContext.Current.CancellationToken);
        return JsonSerializer.SerializeToElement(result);
    }

    [Fact]
    public async Task The_four_tools_resolve()
    {
        var (brain, tools) = await StartAsync();
        await using var _ = brain;
        Assert.Equal(4, tools.Resolve(["code_find_symbols", "code_references", "code_diagnostics", "code_map"]).Count);
    }

    [Fact]
    public async Task Find_symbols_returns_the_envelope()
    {
        var (brain, tools) = await StartAsync();
        await using var _ = brain;
        var result = await InvokeAsync(tools, "code_find_symbols", new() { ["query"] = "Greeter", ["limit"] = 10 });
        Assert.Equal(1, result.GetProperty("totalCount").GetInt32());
        Assert.Equal("T:Alpha.Greeter", result.GetProperty("items")[0].GetProperty("id").GetString());
    }

    [Fact]
    public async Task References_with_a_bad_id_return_advice()
    {
        var (brain, tools) = await StartAsync();
        await using var _ = brain;
        var result = await InvokeAsync(tools, "code_references", new() { ["symbolId"] = "T:Nope" });
        Assert.Contains("find-symbols", result.GetProperty("advice").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Diagnostics_report_the_broken_file()
    {
        var (brain, tools) = await StartAsync();
        await using var _ = brain;
        var result = await InvokeAsync(tools, "code_diagnostics", new() { ["path"] = FixtureSolutions.BrokenPath });
        Assert.Equal(1, result.GetProperty("errorCount").GetInt32());
    }

    [Fact]
    public async Task Map_is_a_graph_result_the_shell_can_open()
    {
        var (brain, tools) = await StartAsync();
        await using var _ = brain;
        var result = await InvokeAsync(tools, "code_map", new() { ["title"] = "Fixture" });
        Assert.Equal("graph", result.GetProperty("kind").GetString());
        var id = result.GetProperty("id").GetString();
        Assert.StartsWith("map-", id, StringComparison.Ordinal);
        Assert.Equal(12, id!.Length);
        Assert.Equal(id, result.GetProperty("name").GetString());
        var again = await InvokeAsync(tools, "code_map", new() { ["title"] = "Fixture" });
        Assert.Equal(id, again.GetProperty("id").GetString());
        var nodes = result.GetProperty("nodes").EnumerateArray().ToArray();
        Assert.Equal(2, nodes.Length);
        Assert.All(nodes, node => Assert.Equal("module", node.GetProperty("kind").GetString()));
        var edge = Assert.Single(result.GetProperty("edges").EnumerateArray());
        Assert.Equal("Beta", edge.GetProperty("sourceId").GetString());
        Assert.Equal("Alpha", edge.GetProperty("targetId").GetString());
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.CodingNativeToolFacts`
Expected: `The_four_tools_resolve` fails with 0 resolved tools.

- [ ] **Step 3: Add the node kinds**

`src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Graph/GraphNodeKinds.cs`:

```csharp
namespace DigitalBrain.UI;

// Mirrors GraphNodeKind in ui/lib/src/components/graph/graph_models.dart.
public static class GraphNodeKinds
{
    public const string Hub = "hub";
    public const string Leaf = "leaf";
    public const string Entity = "entity";
    public const string Module = "module";
}
```

- [ ] **Step 4: Write the tools**

```csharp
// src/Modules/Coding/Coding/CodingNativeTools.cs
using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DigitalBrain.UI;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Coding;

public sealed class CodingNativeTools(SolutionWorkspace workspace)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public IReadOnlyList<AIFunction> Create()
    {
        Task<JsonElement> FindSymbols(
            [Description("Part of a symbol name, case-insensitive")] string query,
            [Description("Maximum hits, default 20")] int limit = 20,
            CancellationToken cancellationToken = default)
            => GuardedAsync(() => workspace.FindSymbolsAsync(new SymbolSearch(query, limit), cancellationToken));

        Task<JsonElement> References(
            [Description("A symbol id from code_find_symbols, such as T:DigitalBrain.Time.ITimer")] string symbolId,
            [Description("Maximum hits, default 50")] int limit = 50,
            CancellationToken cancellationToken = default)
            => GuardedAsync(() => workspace.ReferencesAsync(new ReferenceSearch(symbolId, limit), cancellationToken));

        Task<JsonElement> Diagnostics(
            [Description("Full path of one source file, or empty")] string? path = null,
            [Description("A project name, or empty for the whole solution")] string? project = null,
            CancellationToken cancellationToken = default)
            => GuardedAsync(() => workspace.DiagnosticsAsync(new DiagnosticsQuery(path, project), cancellationToken));

        Task<JsonElement> Map(
            [Description("Title for the map card")] string title = "Solution map",
            CancellationToken cancellationToken = default)
            => MapAsync(title, cancellationToken);

        return
        [
            AIFunctionFactory.Create(FindSymbols, new AIFunctionFactoryOptions
            {
                Name = "code_find_symbols",
                Description = "Find types and members by name in the loaded solution. Returns ids to use with code_references.",
            }),
            AIFunctionFactory.Create(References, new AIFunctionFactoryOptions
            {
                Name = "code_references",
                Description = "Every place a symbol is used, with file, line and the source line. Semantic, not text search.",
            }),
            AIFunctionFactory.Create(Diagnostics, new AIFunctionFactoryOptions
            {
                Name = "code_diagnostics",
                Description = "Compiler errors and warnings for a file, a project, or the whole solution, without running a build.",
            }),
            AIFunctionFactory.Create(Map, new AIFunctionFactoryOptions
            {
                Name = "code_map",
                Description = "A graph of every project in the solution and the references between them. "
                    + "Use it whenever the person asks to see or map the solution, its projects, or their dependencies.",
            }),
        ];
    }

    private async Task<JsonElement> MapAsync(string title, CancellationToken cancellationToken)
    {
        try
        {
            var map = await workspace.MapAsync(new MapQuery(), cancellationToken).ConfigureAwait(false);
            var nodes = map.Projects.Select(project => new GraphNodeState(project.Name, project.Name, GraphNodeKinds.Module, project.Cluster)).ToArray();
            var edges = map.References.Select(edge => new GraphEdgeState($"{edge.From}-{edge.To}", edge.From, edge.To)).ToArray();
            // One artifact per solution: the shell keys artifacts by id, so a second map replaces the first.
            var id = "map-" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(map.SolutionPath)))[..8];
            return JsonSerializer.SerializeToElement(new { kind = "graph", id, name = id, title, nodes, edges }, Json);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return Advice(error);
        }
    }

    private static async Task<JsonElement> GuardedAsync<T>(Func<Task<T>> query)
    {
        try
        {
            return JsonSerializer.SerializeToElement(await query().ConfigureAwait(false), Json);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return Advice(error);
        }
    }

    // A rejection is advice: the model reads why and what to do next instead of a failed tool call.
    private static JsonElement Advice(Exception error)
        => JsonSerializer.SerializeToElement(new { advice = error.Message }, Json);
}
```

- [ ] **Step 5: Register and allowlist**

`CodingModule.Configure` adds:

```csharp
        builder.Services.AddSingleton<CodingNativeTools>();
        foreach (var tool in new[] { "code_find_symbols", "code_references", "code_diagnostics", "code_map" })
        {
            builder.Services.AddNativeTool(tool, services => services.GetRequiredService<CodingNativeTools>().Create().Single(function => function.Name == tool));
        }
```

with `using DigitalBrain.AI;` and `using Microsoft.Extensions.DependencyInjection;`.

`src/Modules/AI/AI/ConversationalAgent.cs:89`: append `"code_find_symbols", "code_references", "code_diagnostics", "code_map"`
to the `Resolve([...])` array. Add one paragraph to the instructions string in the same file, next to the
chart guidance: "For questions about the code base (where a type or method is used, what a file's errors
are, how projects depend on each other) use the code_* tools; never guess from memory."

- [ ] **Step 6: Run the facts and the agent facts**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.CodingNativeToolFacts`
Expected: PASS. If `InvokeAsync` returns a wrapped object rather than the JSON element (the screen wraps
native results), unwrap the `AIFunction` result with `JsonSerializer.SerializeToElement` on the returned
object as the fact already does, and record the observed shape in `NOTES.md`.
Then run `-- --filter-class DigitalBrain.Tests.ConversationalAgentFacts` and update any assertion that pins
the tool list.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Coding src/Modules/AI/AI/ConversationalAgent.cs src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Graph/GraphNodeKinds.cs tests/DigitalBrain.Tests/Features/Coding/CodingNativeToolFacts.cs
git commit -m "coding: code_find_symbols, code_references, code_diagnostics and code_map tools"
```

---

### Task 7: The shell opens a graph result

**Files:**
- Create: `src/Modules/UI/Flutter/shell/lib/workspace/graph_artifact.dart`,
  `src/Modules/UI/Flutter/shell/test/workspace/graph_artifact_test.dart`
- Modify: `shell/lib/workspace/artifact_editors.dart` (add the `'graph'` case),
  `shell/lib/workspace/workspace_chat.dart:316` (the auto-open kind list),
  `shell/lib/workspace/workspace_chat_presentation.dart:141` (the "open in workspace" kind list),
  `shell/lib/workspace/workspace_app.dart:250-260` (`_servedByResult` includes `'graph'`),
  `shell/test/workspace_tool_results_test.dart` (end-to-end case)

**Interfaces:**
- Consumes: the `code_map` result JSON from Task 6; `GraphNode`, `GraphEdge`, `GraphNodeKind`, `UiGraph` from `digitalbrain_ui`.
- Produces: `GraphArtifactData.fromMap(Map<String, dynamic>)` with `nodes`, `edges`, `title`.

- [ ] **Step 1: Write the failing parser test**

```dart
// shell/test/workspace/graph_artifact_test.dart
import 'package:digitalbrain_flutter_shell/workspace/graph_artifact.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('a code_map result parses into graph nodes and edges', () {
    final data = GraphArtifactData.fromMap({
      'kind': 'graph',
      'name': 'map-1',
      'title': 'Fixture',
      'nodes': [
        {'id': 'Alpha', 'label': 'Alpha', 'kind': 'module', 'cluster': 'Alpha'},
        {'id': 'Beta', 'label': 'Beta', 'kind': 'module', 'cluster': 'Beta'},
      ],
      'edges': [
        {'id': 'Beta-Alpha', 'sourceId': 'Beta', 'targetId': 'Alpha', 'dotted': false},
      ],
    });
    expect(data.title, 'Fixture');
    expect(data.nodes.map((n) => n.id), ['Alpha', 'Beta']);
    expect(data.nodes.first.kind, GraphNodeKind.module);
    expect(data.nodes.first.cluster, 'Alpha');
    expect(data.edges.single.sourceId, 'Beta');
  });

  test('an unknown kind falls back to leaf and missing edges to empty', () {
    final data = GraphArtifactData.fromMap({
      'title': 'x',
      'nodes': [
        {'id': 'a', 'label': 'A', 'kind': 'weird'},
      ],
    });
    expect(data.nodes.single.kind, GraphNodeKind.leaf);
    expect(data.edges, isEmpty);
  });
}
```

- [ ] **Step 2: Run it to see it fail**

Run (in `src/Modules/UI/Flutter/shell`): `flutter test test/workspace/graph_artifact_test.dart`
Expected: fails to compile, `graph_artifact.dart` missing.

- [ ] **Step 3: Write the parser**

```dart
// shell/lib/workspace/graph_artifact.dart
import 'package:digitalbrain_ui/digitalbrain_ui.dart';

/// The `kind: "graph"` tool result as the shell renders it.
final class GraphArtifactData {
  const GraphArtifactData({
    required this.title,
    required this.nodes,
    required this.edges,
  });

  factory GraphArtifactData.fromMap(Map<String, dynamic> data) {
    final nodes = <GraphNode>[
      for (final raw in (data['nodes'] as List? ?? const []))
        if (raw is Map)
          GraphNode(
            id: '${raw['id']}',
            label: '${raw['label'] ?? raw['id']}',
            kind: _kind('${raw['kind'] ?? 'leaf'}'),
            cluster: raw['cluster'] as String?,
          ),
    ];
    final edges = <GraphEdge>[
      for (final raw in (data['edges'] as List? ?? const []))
        if (raw is Map)
          GraphEdge(
            id: '${raw['id'] ?? '${raw['sourceId']}-${raw['targetId']}'}',
            sourceId: '${raw['sourceId']}',
            targetId: '${raw['targetId']}',
            dotted: raw['dotted'] == true,
          ),
    ];
    return GraphArtifactData(
      title: '${data['title'] ?? 'Graph'}',
      nodes: nodes,
      edges: edges,
    );
  }

  final String title;
  final List<GraphNode> nodes;
  final List<GraphEdge> edges;

  static GraphNodeKind _kind(String name) => GraphNodeKind.values.firstWhere(
    (kind) => kind.name == name,
    orElse: () => GraphNodeKind.leaf,
  );
}
```

Check that `digitalbrain_ui.dart` exports `graph_models.dart` and `ui_graph.dart`; if not, add the two
`export` lines to the package's barrel file.

- [ ] **Step 4: Wire the editor and the openers**

`artifact_editors.dart`: add before the `'brain'` case:

```dart
    'graph' => LayoutBuilder(
      builder: (context, constraints) {
        final data = GraphArtifactData.fromMap(artifact.data);
        return SizedBox(
          height: constraints.hasBoundedHeight ? constraints.maxHeight : 480,
          child: UiGraph(nodes: data.nodes, edges: data.edges),
        );
      },
    ),
```

with `import 'graph_artifact.dart';`.

`workspace_chat.dart:316`: add `'graph'` to the list of kinds that call `widget.onArtifact` when a
`TOOL_CALL_RESULT` arrives (this is the list that actually opens the artifact; the presentation list only
renders the tile).

`workspace_chat_presentation.dart:141`: add `'graph'` to the list of kinds that render "Open in workspace".

`workspace_app.dart` `_servedByResult`: `kind == 'table' || kind == 'chart' || kind == 'graph'`; update the
comment above it to name graphs alongside tables and charts. `_accept` keeps its `id` guard; the map
result carries one (Task 6).

- [ ] **Step 5: Write the end-to-end widget test**

Append to `shell/test/workspace_tool_results_test.dart`, inside `main()`, reusing its `sendFirstMessage`
and `finish` helpers:

```dart
  testWidgets('a graph tool result opens as a graph artifact in the working area', (
    tester,
  ) async {
    final store = WorkspaceStore(persistence: MemoryWorkspacePersistence());
    final events = await sendFirstMessage(tester, store);
    events.add(
      AgentEvent({
        'type': 'TOOL_CALL_START',
        'toolCallId': 'map',
        'toolCallName': 'code_map',
      }),
    );
    events.add(
      AgentEvent({
        'type': 'TOOL_CALL_RESULT',
        'toolCallId': 'map',
        'content': {
          'kind': 'graph',
          'id': 'map-0123abcd',
          'name': 'map-0123abcd',
          'title': 'Fixture',
          'nodes': [
            {'id': 'Alpha', 'label': 'Alpha', 'kind': 'module', 'cluster': 'Alpha'},
            {'id': 'Beta', 'label': 'Beta', 'kind': 'module', 'cluster': 'Beta'},
          ],
          'edges': [
            {'id': 'Beta-Alpha', 'sourceId': 'Beta', 'targetId': 'Alpha', 'dotted': false},
          ],
        },
      }),
    );
    await tester.pumpAndSettle();
    expect(store.currentProject.artifacts.single.kind, 'graph');
    expect(store.currentProject.artifacts.single.id, 'map-0123abcd');
    expect(find.byType(UiGraph), findsOneWidget);
    expect(tester.widget<UiGraph>(find.byType(UiGraph)).nodes.length, 2);
    await finish(tester, events);
  });
```

Run (in `shell`): `flutter test test/workspace_tool_results_test.dart`
Expected: the new case passes; if `UiGraph` is not found, the artifact was accepted but the editor did not
open it: check the `artifact_editors.dart` case and that the working area shows the newest artifact the
way it does for charts.

- [ ] **Step 6: Run the Flutter gate**

Run (in `src/Modules/UI/Flutter`): `dart format --set-exit-if-changed core ui shell && (cd ui && flutter analyze && flutter test) && (cd shell && flutter analyze && flutter test)`
Expected: clean. If a widget test in the shell pins the served-by-result kinds, extend its expectation.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/UI/Flutter/shell
git commit -m "coding: the shell opens graph tool results with UiGraph"
```

---

### Task 8: AppHost projection, MCP flag, and the live smoke

**Files:**
- Create: `src/Modules/Coding/Aspire.Hosting/CodingHostingExtensions.cs`
- Modify: `src/Aspire/DigitalBrain.AppHost/AppHost.cs`, `src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`
  (project references to the Coding hosting and implementation projects, as the other modules have), `.mcp.json`

**Interfaces:**
- Produces: `coding.WithSolution(path)` writing `DigitalBrain__Coding__SolutionPath`.

- [ ] **Step 1: Write the projection**

```csharp
// src/Modules/Coding/Aspire.Hosting/CodingHostingExtensions.cs
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;

namespace DigitalBrain.Coding.Aspire.Hosting;

public static class CodingHostingExtensions
{
    public static DigitalBrainModuleBuilder<CodingModule> WithSolution(this DigitalBrainModuleBuilder<CodingModule> module, string solutionPath)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionPath);
        module.AddProjection(new SolutionProjection(Path.GetFullPath(solutionPath)));
        return module;
    }

    private sealed class SolutionProjection(string solutionPath) : DigitalBrainModuleProjection
    {
        public override void Apply<TResource>(IResourceBuilder<TResource> builder)
        {
            ArgumentNullException.ThrowIfNull(builder);
            builder.WithEnvironment(EnvironmentKeys.For(CodingModule.ConfigurationRoot, "SolutionPath"), solutionPath);
        }
    }
}
```

- [ ] **Step 2: Compose it and flip the MCP flag**

`AppHost.cs`, after the `MicrosoftModule` block:

```csharp
    .AddModule<CodingModule>(coding => coding.WithSolution(Path.Combine(builder.AppHostDirectory, "..", "..", "..", "DigitalBrain.slnx")))
```

with `using DigitalBrain.Coding;` and `using DigitalBrain.Coding.Aspire.Hosting;`. On the kernel project
resource, inside the existing `WithEnvironment(context => ...)` callback:

```csharp
        // The typed neuron surface (/mcp describe and call) is how Claude Code and Codex reach the coding tools.
        if (builder.ExecutionContext.IsRunMode)
        {
            context.EnvironmentVariables["DigitalBrain__Graph__Enabled"] = "true";
        }
```

`.mcp.json`: change the `digitalbrain-mcp` URL port from 5000 to 5080.

- [ ] **Step 3: Build and run the AppHost**

Run: `dotnet build DigitalBrain.slnx -c Release` then `aspire run --detach` from the repo root (the
`aspire.config.json` names the AppHost). Wait for the kernel to report healthy through the Aspire MCP
`list_resources` tool. Read the kernel console logs and confirm the warmup line sequence: project loads
reported, then no `Failed` status. Expected load time: under two minutes.

- [ ] **Step 4: Smoke through the shell agent**

Send to `/agent` (the repo's `smoke.py` helper from the ClickHouse work drives it) or through the shell:

1. "Where is ITimer used?" — expected: an answer listing `TimerNeuron.cs` with a line number, produced by a
   `code_find_symbols` then `code_references` call (visible in the kernel traces).
2. "Map the solution" — expected: a `graph` tool result; in the shell it opens as a graph with one node per
   project clustered by module.
3. Through the Aspire MCP `list_traces`, confirm the two tool calls and their durations; record them.

If the untrusted-content screen rewrites or blocks a `code_*` result, record the observed text in
`NOTES.md`, and register the four tools through a screen-exempt path only if the screen changed the
result's meaning (design section 7, last trap).

- [ ] **Step 5: Smoke through MCP**

With the kernel running, from a Claude Code or Codex session that has `digitalbrain-mcp` configured:
`describe` the `workspace` type and `call` `find-symbols` on `workspace:digitalbrain` with
`{ "query": "ITimer" }`. Expected: the same hit as the chat path. Record the exact request and reply in
`NOTES.md`.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Coding/Aspire.Hosting src/Aspire/DigitalBrain.AppHost .mcp.json
git commit -m "coding: compose the module in the AppHost and expose the typed surface over /mcp"
```

---

### Task 9: Module docs, notes, final gate and PR

**Files:**
- Create: `docs/coding/README.md`, `docs/coding/NOTES.md`

- [ ] **Step 1: Write the module README**

Follow `docs/clickhouse/README.md`: a project table (the three projects and what they hold), the neuron
contract (`workspace` with its seven methods and the DTO names), the four tools with one line each, the
configuration key `DigitalBrain:Coding:SolutionPath`, how to run the facts and the gated self-test, and a
"what phase 1 adds" pointer to the design.

- [ ] **Step 2: Write NOTES.md**

Record every deviation from this plan, the `aspire run` observations from Task 8 (load time, project count,
any workspace failures reported by `RegisterWorkspaceFailedHandler`), the two smoke transcripts, the MCP
`describe`/`call` exchange, and whether the untrusted-content screen touched a `code_*` result.

- [ ] **Step 3: Run the full local gate**

Run: `dotnet format whitespace DigitalBrain.slnx --verify-no-changes && dotnet build DigitalBrain.slnx -c Release && dotnet test DigitalBrain.slnx -c Release --no-build`
Expected: green with the 4 Docker-gated skips and the 1 coding self-test skip. Then the Flutter gate from
Task 7. Then `$env:DIGITALBRAIN_CODING_SELF_TESTS=1` and the self-test class once more.

- [ ] **Step 4: Commit and open the PR**

```bash
git add docs/coding
git commit -m "coding: record the phase 0 outcome"
git push -u origin feature/coding-phase0-workspace
gh pr create --title "coding: phase 0, Roslyn workspace and solution map" --body-file docs/coding/NOTES.md
```

Add to the PR body the design's exit criteria for phase 0 and a link to `docs/coding/coding-agent-design.md`.

## Self-review

- **Spec coverage.** Design 4.1 (module, neuron table row for `workspace`): Tasks 1, 2, 5. Design 4.2
  (service, loader seam, status, four queries, warmup): Tasks 3, 4. Design 4.3 (map as a `graph` result,
  shell editor): Tasks 6, 7. Design 4.8 (four tools, allowlist, MCP reachability): Tasks 6, 8. Design 4.10
  tiers 1 and 2: Tasks 3, 4, 5, 6. Decisions D1, D7, D8, D9: Tasks 4, 6, 8, 3. The durable map cache
  named in 4.2 ("`map` answers before Roslyn is ready") is not in phase 0; the neuron records the path and
  generation only, and the cache is listed for phase 1 in the design's phase table by this note.
- **Placeholders.** None: every step has its code or its exact command and expected output.
- **Type consistency.** `SymbolSearch(Query, Limit)`, `ReferenceSearch(SymbolId, Limit)`,
  `DiagnosticsQuery(Path, Project, Limit)`, `MapQuery(IncludeDocumentCounts)` are used with the same
  positional and named arguments in Tasks 3, 5 and 6; `ISolutionLoader.OpenAsync` returns `LoadedSolution`
  in Tasks 3 and 4 and both loaders in the tests; `WorkspaceStatus.Phase` is the contract's
  `WorkspacePhase`; `CodingModule.SolutionPathKey` is read by `WorkspaceWarmup` and set by the projection
  through `EnvironmentKeys.For(ConfigurationRoot, "SolutionPath")`, which yields the same key; the map
  result's `id` produced in Task 6 is what Task 7's widget test feeds through `TOOL_CALL_RESULT`.
- **Review findings applied.** Finding 4 (locator first in `Program.cs`, copy-check scoped) in Tasks 1
  and 4; finding 5 (failures visible, self-test requires none) in Tasks 3 and 4; finding 12 (stable `id`,
  both opening lists, end-to-end widget test) in Tasks 6 and 7.
