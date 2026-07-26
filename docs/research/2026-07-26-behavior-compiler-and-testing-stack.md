# Behavior compiler, admission, and testing stack

Research date: 2026-07-26

## Decision

Use the pinned .NET 10 SDK through the supported `dotnet` CLI to compile each canonical
single-file Behavior. Do not embed Roslyn as a second compiler and do not package Behavior
revisions as NuGet tools.

The smallest maintained stack is:

- the pinned .NET SDK for virtual-project evaluation, restore, analyzers, and compilation;
- `Microsoft.CodeAnalysis.BannedApiAnalyzers` 5.6.0 as a defense-in-depth source gate;
- the .NET 10 `System.Reflection.Metadata` and `PEReader` APIs, with no extra package, for a
  post-build artifact gate inside the build sandbox;
- `Reqnroll.xUnit.v3` 3.3.4 for approval-time Behavior scenarios, using only trusted, generic
  bindings over the same `BehaviorTestHost` used by product tests;
- xUnit v3 product-sentence tests for lower-level contracts and invariants;
- `AssemblyLoadContext` and `AssemblyDependencyResolver`, with no extra package, only for
  dependency identity and unloading inside an already isolated runtime worker.

The admission order must be:

```text
canonicalize proposal and directives
  -> resolve and lock only catalog-approved packages
  -> clean offline restore in the build sandbox
  -> SDK compile with analyzers
  -> PE/metadata validation in that same sandbox
  -> Reqnroll/xUnit BDD against the admitted artifact
  -> hash the complete evidence envelope
  -> human approval
  -> immutable installation
```

Neither an analyzer, metadata inspection, nor `AssemblyLoadContext` is a security boundary.
Restore, compilation, inspection, testing, and execution of community or AI-produced code remain
out-of-process sandbox operations.

## Repository baseline

The repository currently declares:

| Concern | Current version/state | Decision |
| --- | --- | --- |
| SDK | `global.json` requests `10.0.100`, `rollForward: latestFeature`, `allowPrerelease: true` | The product compiler must pin an exact installed SDK identity; it must not inherit this rolling development policy. |
| Target framework | `net10.0` | Keep. |
| Roslyn | `Microsoft.CodeAnalysis.CSharp` 4.14.0, private to `DigitalBrain.SourceGeneration` | Keep there; do not promote it into the Behavior compiler. |
| Test host | `Microsoft.NET.Test.Sdk` 18.8.1 | Keep. |
| BDD | `Reqnroll.xUnit.v3` 3.3.4 in `DigitalBrain.Compositions.Tests` | Keep and reuse for the trusted approval harness. |
| xUnit | `xunit.v3` 3.2.2, runner 3.1.5, extensibility core 3.2.2 | Keep for unit, contract, and invariant tests. |
| Banned API analyzer | absent | Add 5.6.0 only to the system-owned Behavior compilation template. |
| Metadata/load APIs | no explicit packages | Use the .NET 10 shared framework APIs; add no package. |

The current machine selected `10.0.400-preview.0.26322.102` because the repository permits
feature-band roll-forward and previews. That is useful for development but insufficient as an
artifact identity. The approved revision must record the full `dotnet --info` SDK identity and the
compiler policy version. Changing either invalidates the previous compilation evidence.

Microsoft's supported-download catalog on the research date lists .NET SDK `10.0.302` (released
2026-07-14) as the current .NET 10 LTS SDK. The implementation plan therefore pins repository and
Behavior compilation to `10.0.302`, sets `rollForward` to `disable`, and sets `allowPrerelease` to
`false`; execution must install that supported SDK before changing `global.json`.
([official .NET download catalog](https://dotnet.microsoft.com/en-us/download))

`DigitalBrain.Compositions.Tests` already contains both product-sentence `[Fact(DisplayName =
"...")]` tests and a Reqnroll `.feature`. The implementation should converge these onto one
public Behavior test harness rather than preserve two ways to simulate the operating system.

## Why the SDK CLI is the compiler boundary

.NET 10 file-based apps are backed by an SDK-created virtual project. The SDK owns target-framework
selection, directives, MSBuild evaluation, reference packs, NuGet restore, compiler/analyzer
loading, output layout, `.deps.json`, and `.runtimeconfig.json`. Its design calls the project
implicit and in-memory. The source confirms that `VirtualProjectBuilder` defaults to
`OutputType=Exe`, `PublishAot=true`, and `PackAsTool=true`, while
`VirtualProjectBuildingCommand` decides between an optimized compiler path and full MSBuild based
on directives and inherited files.
([file-app documentation](https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps),
[SDK design](https://github.com/dotnet/sdk/blob/release/10.0.4xx/documentation/general/dotnet-run-file.md),
[`VirtualProjectBuilder`](https://github.com/dotnet/sdk/blob/release/10.0.4xx/src/Microsoft.DotNet.ProjectTools/VirtualProjectBuilder.cs),
[`VirtualProjectBuildingCommand`](https://github.com/dotnet/sdk/blob/release/10.0.4xx/src/Cli/dotnet/Commands/Run/VirtualProjectBuildingCommand.cs))

Call an exact `dotnet` executable as a child process with `UseShellExecute=false`. Use separate
restore and build commands:

```text
dotnet restore Behavior.cs --locked-mode --configfile NuGet.Config \
  --packages <fresh-private-global-packages> --no-http-cache

dotnet build Behavior.cs --no-restore --no-incremental \
  --configuration Release --output <fresh-output>
```

The first creation of a revision lock is a distinct resolver operation against the vetted local
catalog. A second clean restore must consume that lock with `--locked-mode`; the compile never
updates it. The system-owned canonical header sets at least:

```csharp
#:property TargetFramework=net10.0
#:property OutputType=Library
#:property PublishAot=false
#:property AllowUnsafeBlocks=false
```

The proposal body may contain no `#:` directives. The installer supplies exact Behavior SDK and
module-contract package references from the approved manifest. The isolated directory supplies
the exact `global.json`, `NuGet.Config`, `.globalconfig`, `BannedSymbols.txt`, and
`Directory.Build.props/targets`; it must not sit below the repository or user profile where
ambient MSBuild/NuGet files can be inherited.

Do not use:

- `dotnet run` in production: it couples execution to restore/build/cache state;
- `dotnet pack Behavior.cs`: file apps default to `PackAsTool=true`, and a NuGet package adds
  package identity, install scripts/assets, and another dependency graph without helping runtime
  admission;
- SDK implementation types such as `VirtualProjectBuilder`,
  `FileLevelDirectiveHelpers`, or `NuGetVirtualProjectBuilder`: they are implementation details,
  not the supported product API.

Store the admitted DLL, PDB if retained, dependency manifest, canonical source, manifest, lock
file, diagnostics, policy versions, BDD inputs/results, and hashes in DigitalBrain's own
content-addressed revision envelope.

## Why not compile directly with Roslyn

The public Roslyn path is technically viable:

```text
CSharpSyntaxTree.ParseText
MetadataReference.CreateFromFile
CSharpCompilation.Create
Compilation.WithAnalyzers / CompilationWithAnalyzers.GetAllDiagnosticsAsync
Compilation.Emit
```

([Roslyn SDK](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/),
[`CSharpCompilation`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.csharp.csharpcompilation),
[`CompilationWithAnalyzers`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.diagnostics.compilationwithanalyzers),
[`Compilation.Emit`](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.compilation.emit))

That path would make DigitalBrain responsible for reference-pack discovery, language-version
alignment, NuGet graph resolution, analyzer and generator loading, SDK defaults, generated files,
and dependency/runtime manifests. It would also compile Behaviors with the repository's Roslyn
4.14.0 package while the selected .NET 10 SDK may carry a different compiler. That is a second
toolchain and a second semantics surface.

Keep `Microsoft.CodeAnalysis.CSharp` 4.14.0 private to source generation. Introduce Roslyn APIs in
the admission host only if a later prototype proves an SDK-CLI limitation that cannot be solved
with supported file-app/MSBuild inputs. If that ever happens, the relevant Roslyn source areas are
[`CSharpCompilation`](https://github.com/dotnet/roslyn/blob/main/src/Compilers/CSharp/Portable/Compilation/CSharpCompilation.cs),
[`CompilationWithAnalyzers`](https://github.com/dotnet/roslyn/blob/main/src/Compilers/Core/Portable/DiagnosticAnalyzer/CompilationWithAnalyzers.cs),
and [`Emit`](https://github.com/dotnet/roslyn/tree/main/src/Compilers/Core/Portable/Emit).

## Restore and MSBuild are part of the threat surface

A NuGet package can contribute `build`, `buildTransitive`, analyzers, content files, runtime
assets, tools, and native assets. MSBuild tasks are executable code, including inline and `Exec`
tasks. A lock file makes the dependency graph repeatable; it does not make package contents safe.
([PackageReference asset classes](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files),
[MSBuild tasks](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-tasks),
[inline tasks](https://learn.microsoft.com/en-us/visualstudio/msbuild/msbuild-inline-tasks),
[`Exec`](https://learn.microsoft.com/en-us/visualstudio/msbuild/exec-task))

The module catalog must therefore inspect every contract package before it can appear in a
Behavior manifest. A contract package is admitted only when:

- its exact ID, version, package hash, assembly identities, and public contract surface match the
  compiled module catalog;
- its archive contains only the expected `.nuspec`, metadata, and managed `ref/` or `lib/`
  contract assemblies;
- it has no `build/`, `buildTransitive/`, `tools/`, `contentFiles/`, package analyzers, SDK
  imports, native assets, or provider/runtime implementations.

The Microsoft banned-API analyzer is a narrowly approved infrastructure exception and is injected
by the trusted compilation template, never selected by a proposal.

The compilation sandbox has no network. Its `NuGet.Config` clears inherited package sources and
fallback folders and names one read-only local feed. Give it fresh private locations for the
global-packages and HTTP-cache folders, disable interactive authentication, and do not use
`--ignore-failed-sources`. `packages.lock.json` records the complete resolved graph and content
hashes; `--locked-mode` must fail rather than rewrite it.
([NuGet configuration](https://learn.microsoft.com/en-us/nuget/reference/nuget-config-file),
[global packages and caches](https://learn.microsoft.com/en-us/nuget/consume-packages/managing-the-global-packages-and-cache-folders),
[lock files and locked mode](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files),
[`dotnet restore`](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-restore))

## Source and analyzer admission

Add `Microsoft.CodeAnalysis.BannedApiAnalyzers` 5.6.0 as `PrivateAssets=all` in the system-owned
compilation template and add `BannedSymbols.txt` as an `AdditionalFiles` item. The package is
Microsoft/Roslyn-owned, analyzer-only, and has no dependencies. Its documented configuration uses
documentation-comment IDs and analyzes generated code by default.
([NuGet package 5.6.0](https://www.nuget.org/packages/Microsoft.CodeAnalysis.BannedApiAnalyzers/),
[configuration](https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.BannedApiAnalyzers/BannedApiAnalyzers.Help.md),
[implementation](https://github.com/dotnet/roslyn/tree/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.BannedApiAnalyzers/Core))

The initial banned-symbol policy should cover direct access to:

- reflection, dynamic assembly loading, emit, and runtime activation;
- filesystem, environment, process, shell, console, registry, and direct networking;
- native interop, P/Invoke helper APIs, pointers, and unmanaged memory;
- ambient wall-clock time, non-deterministic random/GUID generation, timers, threads, and
  untracked background-task creation;
- dependency/service locators such as `IServiceProvider` and all Orleans/client/provider SDK
  escape hatches.

The allowlisted Behavior SDK must provide the deterministic replacements: journal-backed time,
IDs, randomness, state, intent/event input, and capability calls.

The banned-API analyzer is a denylist, so it cannot prove the complete structural contract or
contain malicious code. Add one small first-party analyzer only for rules the official package
does not express:

- exactly one public non-abstract Behavior program type implementing the expected closed contract;
- only the approved entry method as public Behavior surface;
- no assembly/module attributes supplied by the proposal;
- no unsafe syntax, extern methods, module/type initializers, or source generators;
- no user directives.

Compiler warnings and all policy diagnostics are errors. Diagnostics exposed to the editor and AI
must use stable DigitalBrain policy codes rather than raw compiler text.

## Post-build PE and metadata admission

After a successful SDK build, inspect the exact output before it leaves the build sandbox. Use
`PEReader` and `MetadataReader` to verify:

- a managed, IL-only assembly with the expected target framework and deterministic build marker;
- the exact assembly name and one expected Behavior implementation;
- only allowlisted assembly references, with contract public-key token/version identities
  matching the manifest lock;
- no module references, linked assembly files, unexpected resources, or exported public types;
- no `MethodAttributes.PinvokeImpl`, non-empty `MethodDefinition.GetImport()`, extern/native
  entry points, or module/type initializers.

The useful public APIs are `PEReader.HasMetadata`, `PEReader.GetMetadataReader()`,
`MetadataReader.AssemblyReferences`, `TypeDefinitions`, `MethodDefinitions`, `ModuleReferences`,
`AssemblyFiles`, and `ManifestResources`, plus `MethodDefinition.Attributes` and
`MethodDefinition.GetImport()`.
([`PEReader`](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.portableexecutable.pereader?view=net-10.0),
[`MethodAttributes`](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.methodattributes?view=net-10.0),
[`MethodDefinition.GetImport`](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.metadata.methoddefinition.getimport?view=net-10.0))

Critical ordering constraint: Microsoft explicitly warns that `MetadataReader`/`PEReader` are not
designed for untrusted input; malformed PE, metadata, or PDB data can cause out-of-bounds access,
crashes, or hangs. The generated DLL is still untrusted because build targets could replace it.
Run inspection with the same CPU/memory/time/process containment as compilation, and never make
the main silo parse a candidate artifact.
([System.Reflection.Metadata warning](https://learn.microsoft.com/en-us/dotnet/api/system.reflection.metadata?view=net-10.0#remarks))

Metadata admission is defense in depth. It can reject impossible artifact shapes and references;
it does not replace the runtime sandbox or the trusted capability broker.

## BDD choice

| Option | What it supplies | xUnit v3 fit | Decision |
| --- | --- | --- | --- |
| Reqnroll 3.3.4 | Gherkin parsing, code generation, bindings, hooks, test-provider integration | `Reqnroll.xUnit.v3` supports xUnit v3; its package brings the MSBuild generator | Use for Behavior acceptance and approval scenarios. |
| Cucumber `Gherkin` 42.0.0 | The official parser and Cucumber message model | No test discovery, bindings, execution model, or xUnit provider | Do not reference directly; it would require building a second BDD framework. |
| xUnit v3 facts with sentence `DisplayName` | Ordinary compiled tests with readable names | Native | Keep for unit, contract, invariant, and narrow composition tests; do not treat them as the persisted Behavior scenario artifact. |

Reqnroll is listed by Cucumber as a maintained semi-official .NET implementation and has supported
xUnit v3 since Reqnroll 3.1. Its xUnit v3 package depends on
`Reqnroll.Tools.MsBuild.Generation`, so it modifies the build. Keep that generator out of the
Behavior program compilation and run it only in a trusted, temporary BDD test project after
metadata admission.
([Cucumber implementations](https://cucumber.io/docs/installation/),
[Reqnroll xUnit integration](https://docs.reqnroll.net/latest/integrations/xunit.html),
[Reqnroll generation](https://docs.reqnroll.net/latest/installation/configuring-build.html),
[`Reqnroll.xUnit.v3` 3.3.4](https://www.nuget.org/packages/Reqnroll.xUnit.v3/),
[`Gherkin` 42.0.0](https://www.nuget.org/packages/Gherkin/))

A proposed revision may provide a canonical `.feature` document but never C# step bindings,
plugins, hooks, or test configuration. The trusted test project supplies a deliberately small,
versioned vocabulary:

- trigger or intent envelope as schema-validated data;
- deterministic module capability stubs and returned facts;
- execution through `BehaviorTestHost`;
- assertions over the resulting synapse journals, capability requests, output, and failure facts.

Bindings must be thin adapters to `BehaviorTestHost`; they must not call `IGrainFactory`, maintain
another behavior catalog, or simulate a second operating system. A scenario and its feature text
are part of the revision hash. Do not duplicate the same proof in both a `.feature` and a
product-sentence `[Fact]`.

## Runtime assembly loading

Start with one worker process per execution. Process exit gives a stronger and simpler lifetime
boundary than cooperative unload, so a custom load context is optional in the first slice.

If measurements later justify a reusable one-revision worker, use one collectible
`AssemblyLoadContext` with an `AssemblyDependencyResolver` rooted at the exact admitted main DLL:

- return the already loaded Behavior SDK and module-contract assemblies from
  `AssemblyLoadContext.Default` so type identity is shared;
- resolve private managed dependencies only from the immutable revision directory;
- refuse all unmanaged-library resolution;
- keep no threads, statics, event handlers, or other roots after execution, then request unload.

One load context can load one version per simple assembly name; equal type names loaded from
different assembly instances are different types; unloading is cooperative. Microsoft also states
that there is no binary isolation between load contexts. It is a dependency/unload mechanism,
not a security control.
([AssemblyLoadContext concepts](https://learn.microsoft.com/en-us/dotnet/core/dependency-loading/understanding-assemblyloadcontext),
[unloadability](https://learn.microsoft.com/en-us/dotnet/standard/assembly/unloadability),
[`AssemblyDependencyResolver`](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.loader.assemblydependencyresolver?view=net-10.0),
[official plugin sample](https://learn.microsoft.com/en-us/samples/dotnet/samples/appwithplugin-demo/),
[untrusted-plugin warning](https://learn.microsoft.com/en-us/dotnet/core/tutorials/creating-app-with-plugin-support))

## Public seams for the implementation

Keep process, policy, artifact, and test mechanics behind four small interfaces:

```csharp
public interface IBehaviorRevisionCompiler
{
    ValueTask<BehaviorCompilationReport> CompileAsync(
        BehaviorCompilationRequest request,
        CancellationToken cancellationToken);
}

public interface IBehaviorArtifactAdmission
{
    ValueTask<BehaviorAdmissionReport> ValidateAsync(
        BehaviorCandidateArtifact artifact,
        CancellationToken cancellationToken);
}

public interface IBehaviorRevisionVerifier
{
    ValueTask<BehaviorVerificationReport> VerifyAsync(
        BehaviorAdmittedArtifact artifact,
        BehaviorFeature feature,
        CancellationToken cancellationToken);
}

public interface IBehaviorTestHost
{
    ValueTask<BehaviorTestResult> ExecuteAsync(
        BehaviorTestCase testCase,
        CancellationToken cancellationToken);
}
```

The request/report records should contain data, paths, hashes, diagnostics, and evidence only.
They must not expose Roslyn, MSBuild, Reqnroll, `Assembly`, or `AssemblyLoadContext` types. This
keeps the public Behavior OS contract stable if a compiler worker, analyzer, or BDD provider is
replaced.

## SDK and analyzer source map for implementers

These are useful for understanding behavior, not APIs DigitalBrain should bind to:

- virtual project defaults and materialization:
  [`VirtualProjectBuilder.cs`](https://github.com/dotnet/sdk/blob/release/10.0.4xx/src/Microsoft.DotNet.ProjectTools/VirtualProjectBuilder.cs);
- file-app build/cache/full-MSBuild selection:
  [`VirtualProjectBuildingCommand.cs`](https://github.com/dotnet/sdk/blob/release/10.0.4xx/src/Cli/dotnet/Commands/Run/VirtualProjectBuildingCommand.cs);
- directive tokenization and evaluation:
  [`FileLevelDirectiveHelpers.cs`](https://github.com/dotnet/sdk/blob/release/10.0.4xx/src/Cli/Microsoft.DotNet.FileBasedPrograms/FileLevelDirectiveHelpers.cs);
- NuGet projection into a virtual file-app project:
  [`NuGetVirtualProjectBuilder.cs`](https://github.com/dotnet/sdk/blob/release/10.0.4xx/src/Cli/dotnet/Commands/NuGet/NuGetVirtualProjectBuilder.cs);
- reflecting package changes back into source directives:
  [`VirtualProjectPackageReflector.cs`](https://github.com/dotnet/sdk/blob/release/10.0.4xx/src/Cli/dotnet/Commands/Package/VirtualProjectPackageReflector.cs);
- materializing a conventional project for diagnosis:
  [`ProjectConvertCommand.cs`](https://github.com/dotnet/sdk/blob/release/10.0.4xx/src/Cli/dotnet/Commands/Project/Convert/ProjectConvertCommand.cs);
- banned-symbol parsing and operation analysis:
  [`SymbolIsBannedAnalyzerBase.cs`](https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.BannedApiAnalyzers/Core/SymbolIsBannedAnalyzerBase.cs)
  and
  [`CSharpSymbolIsBannedAnalyzer.cs`](https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.BannedApiAnalyzers/CSharp/CSharpSymbolIsBannedAnalyzer.cs).

## Plan changes caused by this research

1. Add a dependency-catalog/lock stage before Behavior compilation; exact package versions alone
   are not enough.
2. Pin the compiler worker to an exact SDK identity rather than the repository's rolling
   `global.json` policy.
3. Use `dotnet restore` then `dotnet build --no-restore`; do not implement a Roslyn compiler host
   and do not use `dotnet pack` for revisions.
4. Add BannedApiAnalyzers 5.6.0 plus a small structural DigitalBrain analyzer to the trusted build
   template.
5. Add sandboxed PE/metadata inspection after compilation and before BDD; never parse candidate
   PE files in the silo.
6. Run Reqnroll only in a trusted test project after artifact admission. A proposal supplies
   feature data, never bindings or test code.
7. Make Reqnroll bindings and xUnit tests share `IBehaviorTestHost`; delete duplicate acceptance
   proofs as migrations land.
8. Start runtime execution with one process per invocation. Defer reusable collectible load
   contexts until measurement proves they are needed.
