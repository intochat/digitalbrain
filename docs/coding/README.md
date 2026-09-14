# Coding module

Gives the brain a Roslyn-backed view of its own solution: symbol search, references, compiler
diagnostics and a project dependency map, all served from a real `MSBuildWorkspace` running inside
the silo. Phase 0 shipped the read-only workspace and the map. Phase 1 adds an edit path: more
workspace reads, a `changeset` neuron that turns edits into a transaction (propose, check, commit),
a file watcher that folds external saves into the live snapshot, and runners for `dotnet build`,
`dotnet test` and git. Live rebuild and the swarm are later phases (see "What phase 2 adds" below).

| Project | Namespace | What it holds |
|---|---|---|
| `src/Modules/Coding/Contracts` | `DigitalBrain.Coding` | The `ICodeWorkspace` and `IChangeSet` contracts, every DTO and enum they use, `CodingJson` |
| `src/Modules/Coding/Coding` | `DigitalBrain.Coding` | `CodingModule`, `SolutionWorkspace`, `SolutionQueries`, `MSBuildSolutionLoader`, `WorkspaceWarmup`, `WorkspaceNeuron`, `ChangeSetNeuron`, `ChangeSetEditor`, `CodeFixCatalog`, `SolutionFileWatcher`, `DotnetRunner`, `GitRunner`, `CodingNativeTools` |
| `src/Modules/Coding/Aspire.Hosting` | `DigitalBrain.Coding.Aspire.Hosting` | `WithSolution()` projection |

## Neuron contract

**`workspace:<key>`** (`ICodeWorkspace`, grain type `workspace`, name = a caller-chosen workspace
key; the live kernel opens the repository's own solution under `digitalbrain`) answers twelve
methods:

| Method | Argument | Result | Does |
|---|---|---|---|
| `open` | `OpenWorkspace(SolutionPath, ExpectedVersion?)` | `Accepted<WorkspaceReceipt>` | Refuses a blank path or a stale `ExpectedVersion`; schedules the load and returns at once. |
| `reload` | `ReloadWorkspace()` | `Accepted<WorkspaceReceipt>` | Refuses if nothing has been opened yet; otherwise re-opens or reloads the same solution path. |
| `read` | none | `WorkspaceSnapshot(SolutionPath, Phase, ProjectCount, DocumentCount, Detail, Generation, ReloadNeeded)` | Reports the live `SolutionWorkspace` status (phase, counts, detail, whether a project-file change is waiting on a reload) together with the grain's own path and generation. |
| `find-symbols` | `SymbolSearch(Query, Limit=20)` | `SymbolSearchResult(Items, TotalCount, Truncated)` | Case-insensitive substring match over type and member declarations; `NamedType` hits sort first. |
| `references` | `ReferenceSearch(SymbolId, Limit=50)` | `ReferenceSearchResult(SymbolId, Items, TotalCount, Truncated)` | Every location that references a symbol id, with the trimmed source line. |
| `diagnostics` | `DiagnosticsQuery(Path?, Project?, Limit=50)` | `DiagnosticsResult(Items, ErrorCount, WarningCount, Truncated, TotalCount)` | Compiler diagnostics of warning severity or above, for one file, one project, or the whole solution. |
| `map` | `MapQuery(IncludeDocumentCounts=true)` | `SolutionMap(SolutionPath, Projects, References)` | The project dependency graph, projects clustered by solution folder; answered from the durable `LastMap` cache while a reload is still in flight. |
| `skeleton` | `SkeletonQuery(Path)` | `Skeleton(Path, Project, Members)` | The types and member signatures of one file, without bodies; each `SkeletonMember(Id, Kind, Signature, Line, Depth)` carries a symbol id to use with `member`. |
| `member` | `MemberQuery(SymbolId)` | `MemberSource(Id, Path, StartLine, EndLine, Source)` | One declaration with its body, by symbol id. |
| `callers` | `CallersQuery(SymbolId, Limit=50)` | `CallersResult(SymbolId, Items, TotalCount, Truncated)` | The symbols that call a method or read a property; each `CallerHit(Id, Display, Path, Line, Project)` is one call site. |
| `implementations` | `ImplementationsQuery(SymbolId, Limit=50)` | `SymbolSearchResult(Items, TotalCount, Truncated)` | The implementations of an interface or an abstract or virtual member. |
| `derived` | `DerivedQuery(SymbolId, Limit=50)` | `SymbolSearchResult(Items, TotalCount, Truncated)` | The classes derived from a base type. Reachable through `/mcp`; phase 1 does not wire a `code_*` tool to it. |

**`changeset:<id>`** (`IChangeSet`, grain type `changeset`, name = a caller-chosen change id; edits
with the same id compose into one snapshot) answers five methods:

| Method | Argument | Result | Does |
|---|---|---|---|
| `propose` | `ProposeEdit(Edit, ExpectedVersion?)` | `Accepted<ChangeSetReceipt>` | Refuses a closed change set (`Committed`/`Discarded`) or a stale `ExpectedVersion` (the edit count); appends one `EditRequest` and schedules the reaction that clears `Diagnostics`/`Diff`/`Detail` (a `check` afterwards derives them again from the full edit list). |
| `check` | `CheckChangeSet()` | `Accepted<ChangeSetReceipt>` | Refuses a closed or empty change set; applies every edit to one snapshot of the live solution (no files written) and reports the diagnostics the edits *introduce* and a unified diff. |
| `commit` | `CommitChangeSet(Message)` | `Accepted<ChangeSetReceipt>` | Refuses a closed or empty change set, or a blank message; re-applies the edits, and only if they are still clean writes the changed documents to disk and records the written paths and the new generation. |
| `discard` | `DiscardChangeSet()` | `Accepted<ChangeSetReceipt>` | Refuses a closed change set; marks it `Discarded`. Nothing on disk changes. |
| `read` | none | `ChangeSetSnapshot(Status, Edits, Diagnostics, Diff, Generation, Detail, Files, Revision)` | The current snapshot: `Status` is `Draft`/`Checked`/`Committed`/`Discarded`; `Detail` names the edit a failed check or commit refused on; `Files` lists the paths a commit wrote; `Revision` increments on every reaction that saves (propose, check, commit - success or failure - and discard), so a caller can wait for exactly its own command's settle instead of a leftover result. |

`EditRequest(Kind, SymbolId?, Path?, Source?, NewName?, StartLine?, EndLine?, Namespace?, DiagnosticId?, FixTitle?)`
carries one edit; each `Kind` reads only the fields it needs:

| Kind | Fields it reads | Does |
|---|---|---|
| `ReplaceMember` | `SymbolId`, `Source` | Replaces one member declaration (by symbol id) with a freshly parsed one. |
| `InsertMember` | `SymbolId`, `Source` | Inserts a member after the given symbol, or into the given type if `SymbolId` names a type. |
| `AddUsing` | `Path`, `Namespace` | Adds a `using` directive to a file if it is not already there. |
| `ReplaceRange` | `Path`, `StartLine`, `EndLine`, `Source` | Replaces the 1-based inclusive line range with the given text. |
| `Rename` | `SymbolId`, `NewName` | Renames a symbol everywhere in the solution (`Renamer.RenameSymbolAsync`), including across project boundaries. |
| `ApplyCodeFix` | `Path`, `DiagnosticId`, optional `StartLine`, `FixTitle` | Runs a Roslyn `CodeFixProvider` from `CodeFixCatalog` against one diagnostic and applies its first (or named) fix. |

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
- `check`/`commit` refuse only for diagnostics the change set *introduces*: a multiset difference
  against the pre-edit baseline, keyed by id, severity, message and path, over the changed projects
  and their transitive dependents. A pre-existing error elsewhere in the tree (the fixture's
  `Broken.cs`, or a real solution with a stray warning) never blocks an edit (design 4.9, amended
  2026-09-14).

## Native tools

| Tool | Does |
|---|---|
| `code_find_symbols(query, limit=20)` | Find types and members by name in the loaded solution. Returns ids to use with `code_references`. |
| `code_references(symbolId, limit=50)` | Every place a symbol is used, with file, line and the source line. Semantic, not text search. |
| `code_diagnostics(path?, project?)` | Compiler errors and warnings for a file, a project, or the whole solution, without running a build. |
| `code_map(title="Solution map")` | A graph of every project in the solution and the references between them. Use it whenever the person asks to see or map the solution, its projects, or their dependencies. |
| `code_skeleton(path)` | The types and member signatures of one file, without bodies, with symbol ids. |
| `code_member(symbolId)` | One declaration with its body, by symbol id. |
| `code_callers(symbolId, limit=50)` | The symbols that call a method or read a property, with the call sites. |
| `code_implementations(symbolId, limit=50)` | The implementations of an interface or an abstract or virtual member. |
| `code_propose_edit(changeId, kind, symbolId?, path?, source?, newName?, startLine?, endLine?, namespace?, diagnosticId?, fixTitle?)` | Add one edit to a change set. Nothing touches disk until `code_commit`; `code_check` compiles the snapshot first. |
| `code_check(changeId)` | Apply a change set to one snapshot and compile it: diagnostics and a diff, no files written. |
| `code_commit(changeId, message)` | Write a clean change set to disk and commit it on a `coding/<changeId>` git branch. Refuses when the check has errors or the tree is dirty elsewhere. |
| `code_build(artifactsPath?)` | `dotnet build` of the solution in Release; parsed errors and warnings. |
| `code_test(filterClass?, artifactsPath?)` | `dotnet test` without rebuilding; counts and the failing tests. Run `code_build` first. |

`code_map` returns `{ kind: "graph", id, name, title, nodes, edges }`. `id` and `name` are both
`"map-" + <8 hex chars>`, a stable hash of the solution path (`map-20a66d67` in the live run below).
Nodes are `{ id, label, kind: "module", cluster }`; edges are `{ id, sourceId, targetId, dotted }`.
The Flutter shell opens a `graph` result with `UiGraph`.

`code_propose_edit` and `code_check` return the `changeset` snapshot shape above (`{ status, edits,
diagnostics, diff, generation, detail, files, revision }`). `code_commit` returns a different,
constant shape in every branch: `{ status, files, generation, diff, detail, branch, commit, advice }`
— on a clean git commit, `commit` is filled and `advice` is null; on a git refusal after the files
were already written, `status` is still `Committed`, `commit` is null and `advice` explains what to
do; when the change set itself was never committed (`status` stays `Draft`), `advice` is exactly
`detail`. Every one of `code_propose_edit`/`code_check`/`code_commit` waits on the grain's `Revision`
for its own command to settle, up to `CodingToolOptions.ReactionWait` (2 minutes by default), and
turns a wait that expires into advice rather than an exception. `code_build` returns `{ succeeded,
errors, warningCount, durationSeconds, command, detail }`; `code_test` returns `{ succeeded, total,
passed, failed, skipped, failures, durationSeconds, command, detail }`.

Every tool answers with `{ advice: <message> }` instead of failing the call when the underlying
query throws (an unknown symbol id, a workspace that is not ready, a change set that never
settles, and so on).

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
| `DigitalBrain:Coding:WorkspaceKey` | The `workspace:<key>` grain name `WorkspaceWarmup` records the open on. Default `digitalbrain`. |
| `DigitalBrain:Coding:TestProject` | The project or solution path `code_test` runs against. Default: the configured solution path. |

The watcher (`SolutionFileWatcher`, started once the workspace's first open reports its directory)
folds a saved `.cs` file straight into the live snapshot with `SolutionWorkspace.FoldAsync`
(`WithDocumentText`), so an owner's editor save, a `dotnet format` pass or a git checkout is visible
to the next query without a reload. A saved `.csproj`/`.props`/`.targets`/`.slnx`/`.sln` file cannot
be folded that way, so it only flags `WorkspaceSnapshot.ReloadNeeded` (`workspace.read`'s
`reloadNeeded`) instead; `bin/`, `obj/`, `.git/`, `artifacts/` and `node_modules/` are ignored.

`code_commit` (design 4.9, D10): applies the change set's edits to one snapshot of the live
solution; if that snapshot is still clean, `SolutionWorkspace.CommitAsync` writes the changed
documents to disk in path order inside one `TryApplyChanges` transaction, then `GitRunner` ensures
a `coding/<changeId>` branch exists (creating and checking it out if not) and commits exactly the
written files. A dirty working tree outside those files refuses the git step with the paths named,
but the files the change set wrote are already on disk and the returned `files`/`generation` name
them, so nothing is silently lost; `advice` explains what to do next.

The runners (`DotnetRunner`, `GitRunner`) both take an `IProcessRunner` (`ProcessRunner` in
production, a fake in tests) and a bounded timeout per call (10 minutes build, 20 minutes test, 60
seconds per git call). `code_build`/`code_test` accept an optional `artifactsPath`, passed through
as `dotnet ... -p:ArtifactsPath=<path>` so a self-test or a slot build (phase 2) never collides with
the live build's `bin`/`obj`.

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
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.WorkspaceReadFacts
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.SolutionFileWatcherFacts
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.ChangeSetEditorFacts
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.ChangeSetNeuronFacts
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.RunnerFacts
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.CodingNativeToolFacts
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.CodingChatFacts
```

- `SolutionWorkspaceFacts` drives `SolutionWorkspace`/`SolutionQueries` against a two-project adhoc
  fixture: symbol search, references, diagnostics (including a compiler error with no source
  location), the map, reload, the lease/dispose lifetime, the failure path, and the three commit
  facts (`Commit_applies_the_snapshot_and_writes_only_the_changed_files`,
  `Commit_of_unchanged_text_writes_nothing`, `Commit_refuses_a_snapshot_the_workspace_has_moved_past`).
- `CodeWorkspaceNeuronFacts` drives the same behaviour through the real `workspace` grain interface
  over Orleans — including `The_map_answers_from_the_durable_cache_while_a_reload_is_in_flight` — plus
  a reflection theory that validates `ICodeWorkspace` (12 methods) and `IChangeSet` (5 methods)
  against the contract's section 7 types.
- `WorkspaceReadFacts` covers the five phase 1 reads (`skeleton`, `member`, `callers`,
  `implementations`, `derived`) against the two-project fixture, including a cross-project caller
  and an interface's two implementers.
- `SolutionFileWatcherFacts` proves a saved `.cs` file folds into the snapshot, a project-file save
  flags `reloadNeeded`, an unknown path or unchanged text is a no-op, and `bin`/`obj`/`.git` are
  ignored.
- `ChangeSetEditorFacts` drives `ChangeSetEditor.ApplyAsync` directly: every `EditKind` (including a
  rename that crosses the Alpha/Beta project boundary and a code fix from `CodeFixCatalog`), the
  diagnostics-introduced baseline diff, and the responsible-edit `Detail` text.
- `ChangeSetNeuronFacts` drives `propose`/`check`/`commit`/`discard`/`read` through the real
  `changeset` grain interface: closed and empty-change-set refusals, and `Revision` bumping on
  every reaction that saves.
- `RunnerFacts` covers `DotnetRunner`'s build/test output parsing (including a parameterized test's
  display name and a timed-out process) and `GitRunner`'s branch/commit/refusal/timeout paths
  against a `FakeProcessRunner`.
- `CodingNativeToolFacts` proves the thirteen `code_*` tools resolve and return the shapes above,
  including the change-set-never-settles-is-advice and repeated-check-answers-at-once cases.
- `CodingChatFacts` drives the whole edit path through the `/agent` HTTP endpoint with a
  `ScriptedChatClient`: `code_find_symbols`, `code_propose_edit` (Rename), `code_check`,
  `code_commit`, `code_build`, `code_test`, ending with the model's own summary naming the
  `coding/<changeId>` branch — the scripted proof of design 9.1's phase 1 exit criterion, quoted
  verbatim: "rename `TimerNeuron.Alarm` to `AlarmFor` and run the tests" lands as a commit on a
  `coding/<id>` branch with the suite green, driven from the chat. On the adhoc fixture the scripted
  rename is `Greeter.Greet` to `Hello`, standing in for the live `TimerNeuron.Alarm` rename;
  `NOTES.md` records that live scenario driven through `aspire run`.

The gated self-tests open this repository's own `DigitalBrain.slnx` through the real MSBuild
loader:

```bash
DIGITALBRAIN_CODING_SELF_TESTS=1 dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.CodingSelfTestFacts
```

The first asserts at least 30 projects load with no partial-load failures, finds
`T:DigitalBrain.Time.ITimer`, finds a reference to it in `TimerNeuron.cs`, and maps
`DigitalBrain.Modules.Time` into cluster `Modules/Time`. On this machine it opens 38 projects in
about 18 seconds. The second runs `DotnetRunner.BuildAsync`/`TestAsync` against the real solution
(`-p:ArtifactsPath` under `artifacts/self-test`) and asserts a clean build and at least 7 passing
`WorkspaceReadFacts`. Without the environment variable both facts are skipped.

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

## What phase 2 adds

Phase 2 turns the single silo into two: `kernel-a` and `kernel-b` from the same silo project, each
with its own `DigitalBrain__Slot` and `-p:ArtifactsPath` output, behind a small YARP gateway
(`DigitalBrain.Gateway`) on port 5080 that reassigns every request to the active slot and proxies
SSE and streamable HTTP unchanged; `POST /switch/{slot}` flips the active slot. An `ActiveSlot`
lease (a compare-and-swap row in the clustering table) fences the standby: a silo whose slot does
not hold the lease ignores its reminder ticks, does not drain its reactions, and refuses commands
with "standby slot", so activations never run in two slots at once. A `slot` neuron drives `build`
(`dotnet build` into the standby's artifacts path, parsed with the same `DotnetRunner`), asks Aspire
to start the standby, waits for health and a read-only smoke check, then promotes by flipping the
lease and the gateway while the old slot drains its in-flight HTTP for a grace period before it
stops; the swap itself is a durable follow-up saved as a reaction, never performed inside a model
turn, so nothing in flight is repeated in the new slot. Rollback flips the lease and the gateway
back while the old slot still runs, allowed only for a landing whose change set touched no
`[GenerateSerializer]` state type; a change to persisted state shapes lands forward-only, the old
slot stopped before the new one writes any state. The new tool is `code_promote`. See
`coding-agent-design.md` sections 4.4 and 9.2.
