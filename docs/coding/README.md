# Coding module

Gives the brain a Roslyn-backed view of its own solution: symbol search, references, compiler
diagnostics and a project dependency map, all served from a real `MSBuildWorkspace` running inside
the silo. Phase 0 ships the read-only workspace and the map; edits, live rebuild and the swarm are
later phases (see "What phase 1 adds" below).

| Project | Namespace | What it holds |
|---|---|---|
| `src/Modules/Coding/Contracts` | `DigitalBrain.Coding` | The `ICodeWorkspace` contract, every DTO and enum it uses, `CodingJson` |
| `src/Modules/Coding/Coding` | `DigitalBrain.Coding` | `CodingModule`, `SolutionWorkspace`, `SolutionQueries`, `MSBuildSolutionLoader`, `WorkspaceWarmup`, `WorkspaceNeuron`, `CodingNativeTools` |
| `src/Modules/Coding/Aspire.Hosting` | `DigitalBrain.Coding.Aspire.Hosting` | `WithSolution()` projection |

## Neuron contract

**`workspace:<key>`** (`ICodeWorkspace`, grain type `workspace`, name = a caller-chosen workspace
key; the live kernel opens the repository's own solution under `digitalbrain`) answers seven
methods:

| Method | Argument | Result | Does |
|---|---|---|---|
| `open` | `OpenWorkspace(SolutionPath, ExpectedVersion?)` | `Accepted<WorkspaceReceipt>` | Refuses a blank path or a stale `ExpectedVersion`; schedules the load and returns at once. |
| `reload` | `ReloadWorkspace()` | `Accepted<WorkspaceReceipt>` | Refuses if nothing has been opened yet; otherwise re-opens or reloads the same solution path. |
| `read` | none | `WorkspaceSnapshot(SolutionPath, Phase, ProjectCount, DocumentCount, Detail, Generation)` | Reports the live `SolutionWorkspace` status (phase, counts, detail) together with the grain's own path and generation. |
| `find-symbols` | `SymbolSearch(Query, Limit=20)` | `SymbolSearchResult(Items, TotalCount, Truncated)` | Case-insensitive substring match over type and member declarations; `NamedType` hits sort first. |
| `references` | `ReferenceSearch(SymbolId, Limit=50)` | `ReferenceSearchResult(SymbolId, Items, TotalCount, Truncated)` | Every location that references a symbol id, with the trimmed source line. |
| `diagnostics` | `DiagnosticsQuery(Path?, Project?, Limit=50)` | `DiagnosticsResult(Items, ErrorCount, WarningCount, Truncated, TotalCount)` | Compiler diagnostics of warning severity or above, for one file, one project, or the whole solution. |
| `map` | `MapQuery(IncludeDocumentCounts=true)` | `SolutionMap(SolutionPath, Projects, References)` | The project dependency graph, projects clustered by solution folder. |

Notes on the shapes above:

- `find-symbols` and `references` share the envelope `{ items, totalCount, truncated }`;
  `diagnostics` reports `{ items, errorCount, warningCount, truncated, totalCount }` instead.
- Symbol ids are documentation-comment ids, the same format Roslyn uses in XML doc comments
  (`T:DigitalBrain.Time.ITimer`, `M:...`).
- A diagnostic with no source location (a project that fails to compile at all, such as a missing
  entry point) is reported against the project file, at line 0, instead of being dropped.
- A query against an unknown symbol id, or against a workspace that is not `Ready`, does not throw
  an opaque error: the caller gets an advice string explaining what happened and what to do next
  (`WorkspaceStatus.Advice`, or the tool's own `{ advice: ... }` result below).

## Native tools

| Tool | Does |
|---|---|
| `code_find_symbols(query, limit=20)` | Find types and members by name in the loaded solution. Returns ids to use with `code_references`. |
| `code_references(symbolId, limit=50)` | Every place a symbol is used, with file, line and the source line. Semantic, not text search. |
| `code_diagnostics(path?, project?)` | Compiler errors and warnings for a file, a project, or the whole solution, without running a build. |
| `code_map(title="Solution map")` | A graph of every project in the solution and the references between them. |

`code_map` returns `{ kind: "graph", id, name, title, nodes, edges }`. `id` and `name` are both
`"map-" + <8 hex chars>`, a stable hash of the solution path (`map-20a66d67` in the live run below).
Nodes are `{ id, label, kind: "module", cluster }`; edges are `{ id, sourceId, targetId, dotted }`.
The Flutter shell opens a `graph` result with `UiGraph`.

Every tool answers with `{ advice: <message> }` instead of failing the call when the underlying
query throws (an unknown symbol id, a workspace that is not ready, and so on).

## Configuration

```csharp
.AddModule<CodingModule>(coding => coding.WithSolution(pathToSlnxOrSln))
```

`WithSolution` projects the path as `DigitalBrain__Coding__SolutionPath` into the kernel's
environment. `WorkspaceWarmup`, a hosted service, reads it back at silo start and opens the
solution without blocking startup. A malformed path (one `Path.GetFullPath` rejects) is a logged
warning, not a startup failure: the workspace stays `NotOpened` until it is opened by hand.

| Key | Meaning |
|---|---|
| `DigitalBrain:Coding:SolutionPath` | Full path of the `.slnx` or `.sln` file to open at silo start. |

The dev AppHost also sets `DigitalBrain__Graph__Enabled=true`, but only in run mode (never during
`aspire publish`). That flag turns on the kernel's typed neuron surface over `/mcp` (`describe`,
`call`), which is how the `workspace` neuron's methods above are reachable outside the chat.
`.mcp.json`'s `digitalbrain-mcp` entry points at `http://localhost:5080/mcp`, the kernel's own HTTP
endpoint.

## How to run

Facts, one class per concern:

```bash
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.SolutionWorkspaceFacts
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.CodeWorkspaceNeuronFacts
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.CodingNativeToolFacts
```

- `SolutionWorkspaceFacts` drives `SolutionWorkspace`/`SolutionQueries` against a two-project adhoc
  fixture: symbol search, references, diagnostics (including a compiler error with no source
  location), the map, reload, the lease/dispose lifetime, and the failure path.
- `CodeWorkspaceNeuronFacts` drives the same behaviour through the real `workspace` grain interface
  over Orleans, plus a reflection fact that validates the contract's DTO shapes.
- `CodingNativeToolFacts` proves the four `code_*` tools resolve and return the shapes above.

The gated self-test opens this repository's own `DigitalBrain.slnx` through the real MSBuild
loader:

```bash
DIGITALBRAIN_CODING_SELF_TESTS=1 dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.CodingSelfTestFacts
```

It asserts at least 30 projects load with no partial-load failures, finds
`T:DigitalBrain.Time.ITimer`, finds a reference to it in `TimerNeuron.cs`, and maps
`DigitalBrain.Modules.Time` into cluster `Modules/Time`. On this machine it opens 38 projects in
about 18 seconds; without the environment variable the fact is skipped.

Flutter, from `src/Modules/UI/Flutter/shell`:

```bash
flutter test test/workspace/graph_artifact_test.dart
flutter test test/workspace_tool_results_test.dart
```

The first covers the `code_map` result parser (`GraphArtifactData.fromMap`); the second covers the
end-to-end path from a `TOOL_CALL_RESULT` event to a rendered `UiGraph` widget.

## Build notes

- `DisableMSBuildAssemblyCopyCheck` is `true` on the five projects whose build output carries the
  Roslyn or MSBuild assemblies: the module (`DigitalBrain.Modules.Coding`), the silo
  (`DigitalBrain.Silo`), the tests (`DigitalBrain.Tests`), the module's own `Aspire.Hosting`
  project, and the AppHost. It is never set in `Directory.Build.props`.
- `MSBuildLocator.RegisterDefaults()` is the first statement of the silo's `Program.cs`, before the
  host is built, so no other module can load a `Microsoft.Build` assembly ahead of it.
  `MSBuildSolutionLoader` registers it again, guarded, as a fallback for test processes that have no
  such entry point.
- The tree the loader opens must be restored (`obj/project.assets.json` present for every project);
  `MSBuildWorkspace` cannot resolve project references otherwise.

## What phase 1 adds

Phase 1 turns the read-only workspace into an edit path: a `changeset` neuron, skeleton and member
reads, member-level edits and rename, `check`/`commit`, a file watcher, and the durable map cache.
See `coding-agent-design.md` section 9.1.
