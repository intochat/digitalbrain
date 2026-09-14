# Coding module notes

## Phase 0 outcome (2026-09-13)

Exit criteria from design section 6 and how each was met:

- **"Where is `ITimer` used?" works against the running kernel.** Chat 1 in the live run below:
  `code_find_symbols` then `code_references`; the answer names `TimerNeuron.cs:13` and
  `TimeSteps.cs:10`/`98`.
- **"Map the solution" works against the running kernel.** Chat 2: `code_map` returns a `graph`
  result with 38 project nodes; the shell's rendering is evidenced by the Task 7 widget test, not a
  driven desktop session.
- **Facts pass.** `SolutionWorkspaceFacts` (16), `CodeWorkspaceNeuronFacts` (8) and
  `CodingNativeToolFacts` (5) are all green.
- **The gated self-test passes.** `CodingSelfTestFacts` opens `DigitalBrain.slnx` through the real
  MSBuild loader: 38 projects, no partial-load failures, finds `ITimer`, its reference in
  `TimerNeuron.cs`, and the `Modules/Time` cluster.

Suite counts at the end: 325 passed, 5 skipped (4 Docker-gated ClickHouse facts, 1 coding
self-test), 0 failed. Flutter gate: `ui` 22/22, `shell` 49/49 (includes the graph-parser and
tool-results widget tests), `flutter analyze` clean in both, `dart format` clean on `core ui
shell/lib shell/test`.

## Deviations from the plan

1. Task 1: the plan's composition fact asserted the module was *not* registered (an inverted
   ternary); ruled to assert `Assert.NotNull(brain.SiloServices.GetService(typeof(CodingModule)))`
   instead, the evident intent.
2. Task 1: `MSBL001` (Microsoft.Build.Locator's copy-check) fired on the Coding module's own
   `Aspire.Hosting` project too, so `DisableMSBuildAssemblyCopyCheck` sat on four projects there
   (module, silo, tests, Coding `Aspire.Hosting`) before Task 8 added a fifth.
3. Task 1: new files must be CRLF (`.gitattributes`) or `dotnet format whitespace` fails; recorded
   as a working note for every later task.
4. Task 2: the plan's runtime fact ("an unopened workspace reads as not opened") could not run yet
   because no grain exists until Task 5, so it moved there; Task 2 committed a descriptor fact
   instead, validating `ICodeWorkspace`'s DTOs against the kernel's `DescriptorRules`.
5. Task 3: the composition fact's own assertion forces DI resolution of `SolutionWorkspace`, so the
   plan's claim that it resolves lazily did not hold for that one fact; ruled to have the fact
   supply an adhoc `ISolutionLoader` itself, the same pattern Task 5 uses, rather than add a stub
   loader to `CodingModule`.
6. Task 3: a compilation-level diagnostic with no source location (a project that fails to compile
   at all) was silently dropped by the original filter; fixed so it is reported against the project
   file, at line 0, instead of reading as clean.
7. Task 3: a reload or a `Dispose()` could each race a query already holding the previous
   `Workspace`; fixed with a lease/retire scheme (`Acquire`/`Release`, `_leases`, `_retired`) so a
   workspace is disposed only once every in-flight query has released it, `Dispose()` is idempotent
   and sets `Status` to `Failed("the workspace was disposed")`, and a follow-up round made
   `Dispose()` itself honor the same `_leases == 0` guard instead of disposing unconditionally.
8. Task 3: the failure path (a loader that throws) had no fact; added one asserting `Failed`, the
   detail text, the advice text, and that queries throw `WorkspaceNotReadyException`.
9. Task 4: the gated self-test uses the repo's `[Fact(Skip = ..., SkipUnless = nameof(...))]`
   attribute form (matching `ClickHouseDriverProviderIntegrationFacts`) instead of an
   `Assert.SkipUnless` call in the body.
10. Task 4: `WorkspaceWarmup.StartAsync` called `Path.GetFullPath` unguarded, so a malformed
    `DigitalBrain:Coding:SolutionPath` could abort silo startup; fixed to catch the path-shape
    exceptions and log a warning instead (design's "degrade, don't fail"), leaving the workspace
    `NotOpened`.
11. Task 5: `WorkspaceNotReadyException` gained `[GenerateSerializer]`/`[Alias]` because it crosses
    the Orleans grain boundary for real once a grain calls it, and lost its unused `Status`
    property, since tagging it would have required `WorkspaceStatus` itself to become serializable.
12. Task 5: the blank-path rejection advice was reworded to contain "solution path" ("Provide the
    full solution path of a .slnx or .sln file.") because `CommandRejectedException.Message` comes
    from its third constructor argument only, not the rejection reason.
13. Task 6: the plan's fact snippet wrote `.Count` (a method group) instead of `.Count()`, a
    pre-existing typo repeated at plan line 1827; fixed so it compiles.
14. Task 6: the native-tool facts register the test-local `ScriptedContentScreen` (the same
    substitution every scenario makes), because the real `UntrustedContentScreen` needs a
    configured OpenAI model and otherwise turns every native tool result into an error string.
15. Task 7: the graph parser silently turned a node or edge without an `id` into the literal string
    `"null"`, risking silent id collisions; fixed to skip a node without a non-empty string `id` and
    an edge without non-empty `sourceId`/`targetId`. `dotted` was left strict-bool: the only
    producer, `code_map`, always emits a real bool.
16. Task 8: the plan's AppHost projection snippet omitted `using Aspire.Hosting;`, needed for the
    `WithEnvironment` extension method every other hosting-extensions file already carries.
17. Task 8: `MSBL001` also fired on the AppHost once the Coding module's MSBuild dependency reached
    its transitive closure, so `DisableMSBuildAssemblyCopyCheck` sits on a fifth project there.
18. Task 8 live fix: `WorkspaceNeuron.Read()` derived `Phase` from whether the grain had persisted
    state, so a solution the warmup had already opened still read as `NotOpened`; fixed to read
    `Phase`/`ProjectCount`/`DocumentCount`/`Detail` from the live `SolutionWorkspace.Status`,
    keeping only `SolutionPath`'s fallback and `Generation` tied to the grain's own state.

## Live run (`aspire run --detach`, 2026-09-13 21:36 UTC)

The kernel was healthy within seconds of starting. Two chat transcripts were captured over HTTP
against `/agent`, and one MCP exchange against `/mcp`, both with `DigitalBrain__Graph__Enabled=true`
in the AppHost's environment. Tool results below are abbreviated to their first lines; assistant
answers are whole.

### Chat 1: "Where is `ITimer` used?"

```text
> Where is ITimer used?   (thread e68a14c3-d554-47a9-9c06-6fedc2b740c1)
  [http 200]
  [   2.1s tool call] code_find_symbols
  [args] {"query":"ITimer","limit":20}
  [   3.0s tool result, 0.8s after start] {
  "items": [
    {
      "id": "T:OrleansCodeGen.DigitalBrain.Time.Codec_Invokable_ITimerAlarm_GrainReference_785AC049",
      ...
  [   4.1s tool call] code_references
  [args] {"symbolId":"T:DigitalBrain.Time.ITimer","limit":50}
  [  12.7s tool result, 8.6s after start] {
  "symbolId": "T:DigitalBrain.Time.ITimer",
  "items": [
    {
      "path": "E:\\intochat\\digitalbrain\\src\\Modules\\Time\\Contracts\\obj\\Debug\\net11.0\\...",
      ...
  [  15.0s run finished]
  [assistant] `ITimer` is used in these source locations:

              - **Implementation:** `src/Modules/Time/Time/TimerNeuron.cs:13`
                `TimerNeuron` implements `ITimer`.

              - **Tests:** `tests/DigitalBrain.Tests/Features/TimeSteps.cs`
                - Line 10: aliases `DigitalBrain.Time.ITimer`
                - Line 98: resolves an Orleans grain with `GetGrain<ITimer>(...)`

              - **Generated Orleans code:** several files under `src/Modules/Time/Contracts/obj/...`
                These contain generated proxies, serializers, and metadata for the `Schedule`, `Stop`, and `Read` methods. They are build artifacts rather than handwritten usage.

              The main handwritten runtime usage is therefore `TimerNeuron`, with test interaction in `TimeSteps.cs`.
```

Tool durations: `code_find_symbols` 0.8 s; `code_references` 8.6 s on this first call (it builds
every project's compilation before it can search); run finished at 15.0 s.

### Chat 2: "Map the solution"

```text
> Map the solution   (thread c9aedce6-a99b-4215-8fd5-cb47a09fa9bb)
  [http 200]
  [   1.2s tool call] code_map
  [args] {"title":"Solution map"}
  [   2.2s tool result, 1.0s after start] {
  "kind": "graph",
  "id": "map-20a66d67",
  "name": "map-20a66d67",
  "title": "Solution map",
  "nodes": [
    {
      "id": "DigitalBrain.Contracts",
      ...
  [   3.8s run finished]
  [assistant] The solution map is open in the workspace.

              It contains **38 projects**, organized into:

              - **Kernel:** `DigitalBrain`, `DigitalBrain.Contracts`, `DigitalBrain.Mcp`, `DigitalBrain.Silo`
              - **Aspire:** hosting, service defaults, app host
              - **Modules:** AI, ClickHouse, Coding, Excel, Google, Memory, Microsoft, Salesforce, Time, and UI
              - **Contracts:** paired contract projects for most modules
              - **Testing:** `DigitalBrain.Testing` and `DigitalBrain.Tests`

              The dependency structure is centered on `DigitalBrain.Contracts`, with `DigitalBrain.AppHost` wiring together the modules and their Aspire hosting projects.
```

`code_map` answered in 1.0 s.

### Trace (chat 2, traceId `a8cde7584ca160ca8e2bd919884cbf23`)

`POST /agent` 3798 ms total: `chat` (`gpt-5.6-luna`) 1203 ms, then `execute_tool code_map` 961 ms
(of which the untrusted-content screen's OpenAI classifier call was ~949 ms and the Roslyn map
itself ~10 ms), then a closing `chat` 1630 ms; input tokens 17697.

### MCP exchange (`live/mcp-smoke-2.txt`)

```text
>>> initialize {"protocolVersion": "2025-06-18", ...}
<<< {"protocolVersion": "2025-06-18", "capabilities": {"logging": {}, "tools": {}}, "serverInfo": {"name": "brain", "version": "0.1"}}

>>> tools/list {}
<<< tools: describe, read, cancel, fire, call, ... (the kernel's fixed MCP operations; the code_* tools
    are not separate MCP tools, they are reached by calling workspace:digitalbrain's methods)

>>> tools/call {"name": "describe", "arguments": {"neuron": "workspace:digitalbrain"}}
<<< 7 methods: open (mutator), reload (mutator), read (read-only), find-symbols (read-only),
    references (read-only), diagnostics (read-only), map (read-only); each with its argument and
    result JSON schema.

>>> tools/call {"name": "describe", "arguments": {"interface": "workspace", "method": "find-symbols"}}
<<< find-symbols takes {query, limit=20} and returns {items: [{id, kind, name, display, project, path,
    line}], totalCount, truncated}.

>>> tools/call {"name": "call", "arguments": {"neuron": "workspace:digitalbrain", "interface": "workspace", "method": "find-symbols", "args": "{\"query\": \"ITimer\"}"}}
<<< {
  "items": [
    {
      "id": "T:OrleansCodeGen.DigitalBrain.Time.Codec_Invokable_ITimerAlarm_GrainReference_785AC049",
      "kind": "NamedType",
      "name": "Codec_Invokable_ITimerAlarm_GrainReference_785AC049",
      "display": "OrleansCodeGen.DigitalBrain.Time.Codec_Invokable_ITimerAlarm_GrainReference_785AC049",
      "project": "DigitalBrain.Modules.Time",
      "path": "E:\\intochat\\digitalbrain\\src\\Modules\\Time\\Time\\obj\\Debug\\net11.0\\...",
      "line": 100
    },
    ...

>>> tools/call {"name": "call", "arguments": {"neuron": "workspace:digitalbrain", "interface": "workspace", "method": "read", "args": "{}"}}
<<< {
  "solutionPath": null,
  "phase": "NotOpened",
  "projectCount": 38,
  "documentCount": 790,
  "detail": null,
  "generation": 0
}
```

That `read` reply is the one that motivated the live fix below: `phase: NotOpened` while
`projectCount`/`documentCount` already reflect a warmed, `Ready` service. After the fix, the same
call reports the live phase.

### Untrusted-content screen

The OpenAI classifier passed every `code_*` result unchanged (raw JSON reached the model). No
registration outside the screen was needed for phase 0. The shell's rendering of the `graph` result
is evidenced by the Task 7 end-to-end widget test rather than a driven desktop session.

## Observations for later phases

- `find-symbols("ITimer")` ranks `OrleansCodeGen.*` generated types above the exact `ITimer` hit:
  both are `NamedType` symbols, and the current sort is kind-then-name, so
  `Codec_Invokable_ITimerAlarm_...` sorts ahead of `ITimer` alphabetically. `references` likewise
  returns hits inside `obj/**/*.g.cs` generated documents. Both are deferred to phase 1; design
  section 9.1 is amended with a query-hygiene bullet.
- The neuron snapshot observed live before the fix:
  `{"solutionPath":null,"phase":"NotOpened","projectCount":38,"documentCount":790,"detail":null,"generation":0}`.
- `SolutionWorkspace` is one singleton behind every `workspace:<key>` grain: two workspace neurons
  would share the same underlying Roslyn workspace. Multiple workspaces are phase 5 scope.
- Deferred minors, one line each:
  - Task 2: `CodeWorkspaceNeuronFacts.cs:26` assumes every `ICodeWorkspace` method returns
    `Task<T>`; the "at least seven" assertion counts `methods.Length`, not visited iterations.
  - Task 3: stringly enum comparisons, a repeated clamp expression, `IncludeDocumentCounts=false`
    ambiguity, `ReadyAt` left unasserted, `Path`/`Project` both supplied to `diagnostics`, a
    cryptic comparator in the reference sort, `.Order()` hiding the topological order in `map`,
    `Opens++` not interlocked, and `ResolveAsync`'s per-project compilation cost (phase 1 revisits
    with a declaration index).
  - Task 4: `RegisterWorkspaceFailedHandler` sits outside the `try` (a workspace leak if it threw),
    and `Failures` is handed out as a mutable list.
  - Task 5: `WorkspaceNeuron`'s reload branch relies on `SolutionWorkspace` never nulling
    `_solutionPath` (a cross-file invariant with no comment); `WorkspaceNotReadyException` lost its
    structured `Status` (an English message only).
  - Task 6: `MapAsync` and `GuardedAsync` duplicate the same advice-catch logic; `code_diagnostics`'
    description is silent on path-over-project precedence.
  - Task 7: the "Open in workspace" tile keeps the generic document icon for the `graph` kind.

## Test-suite observations

- The pre-existing flaky scenario `A full pending queue refuses a command before it is attempted`
  (`command.feature`, a timing race in `Neuron.FireCoreAsync` under full-suite parallel load) failed
  once or twice under load in several gates (Tasks 5 and 8) before a clean re-run. It is unrelated
  to the Coding module; no file on its call path changed in this phase.
- `dotnet test DigitalBrain.slnx` ran to completion directly against the `.slnx` on this machine
  throughout the phase; the older Windows+slnx test-host handshake bug did not appear.
- `dart format --set-exit-if-changed core ui shell` reports one stale file under the git-ignored
  `shell/build` (`shell/build/windows/x64/plugins/super_native_extensions/cargokit_build/tool/bin/build_tool_runner.dart`)
  on this machine; the source folders (`core`, `ui`, `shell/lib`, `shell/test`) are clean.

## Branch hygiene

Two docs-only commits from another session landed on `feature/coding-phase0-workspace` at
23:38/23:40 on 2026-09-13 and were left in place (docs-only, build-neutral; rewriting a branch
another session is committing to is not this session's call):

- `ed6933d9`: "docs: design Flutter chart migration from graphic to fl_chart"
- `8377d3f9`: "docs: plan Flutter UiChart migration to fl_chart"

Both are under `docs/superpowers/` and unrelated to the Coding module.

Implementer commits carry the implementer model's own `Co-Authored-By` trailer, set by the subagent
harness for each dispatch rather than by a single fixed instruction; the trailer text therefore
varies commit to commit across this branch's history. This is left as-is; history is not rewritten.

## Whole-branch review and fix wave (2026-09-14)

The branch review ran with the code-review skill at high effort (eight finder angles, one verifier
per correctness candidate) with the programme's focus: naming, dead code, comment noise, analyzer
cleanliness and silent failures. Ten findings were reported; nine were fixed in one wave
(`8f397b42`, "coding: apply the phase 0 branch review") and re-reviewed clean:

- `reload` works after a warmup-only start: the command rejects only when nothing is open anywhere,
  and the reaction records the live path in the grain's state (new fact
  `Reload_after_a_warmed_start_records_the_solution`).
- The design's bounded query gate (two semantic queries at a time, design 4.2) now exists as a
  `SemaphoreSlim(2, 2)` inside the query lease (new fact `At_most_two_queries_run_at_once`, which
  proves completion and the restored slot count; the throttle itself is not observable on the
  in-memory fixture).
- `DiagnosticsResult` carries `TotalCount` (`Id(4)`), so every list result has the
  `{ items, totalCount, truncated }` envelope of design section 2.
- Dispose during a load disposes the lifetime token source on the late-load and cancelled paths, and a
  cancelled load no longer overwrites the "the workspace was disposed" detail (new fact
  `Dispose_during_a_cancelled_load_keeps_the_disposed_status`).
- `CodingNativeTools` builds its four functions once and exposes `Named(string)`; the module
  registers through it.
- Dead surface removed: `SolutionWorkspace.ReadyAt`, `Current` and the `TimeProvider` constructor
  parameter that only fed `ReadyAt`.
- `SolutionQueries`: one `ClampLimit`, `nameof(...)` enum comparisons, a plain two-key comparator,
  document text fetched once per document in `references`; `CodingNativeTools` keeps one advice catch.

One finding was skipped by ruling: a `workspace` grain whose persisted path differs from the
configured one answers from the singleton's solution after a restart. That is the one-singleton-per-silo
limit phase 5 (multiple workspaces) removes.

Parked minors from the re-review of the fix wave (real, not load-bearing, left for phase 1 which
reshapes `SolutionWorkspace`): the semaphore is disposed on the `_leases == 0` signal, which has a
window between the gate wait and the lease count where a concurrent `Dispose` could dispose a gate
with a permit outstanding (simplest fix: never dispose the gate); a query issued after `Dispose`
surfaces `ObjectDisposedException` from the gate instead of the disposed advice; the generic
`catch (Exception)` path in `OpenCoreAsync` still leaves the lifetime token source undisposed when
`Dispose` ran during that load; the new cancelled-load fact sits between the helper classes; the
reload fact's poll loop fails by timeout rather than by a named assertion; `CodingNativeTools.Functions`
is public with no external caller.

### Final gates

- .NET: `dotnet format whitespace DigitalBrain.slnx --verify-no-changes && dotnet build DigitalBrain.slnx -c Release && dotnet test DigitalBrain.slnx -c Release --no-build`
  at `8f397b42`: 0 warnings, 333 total, 328 passed, 5 skipped (4 Docker + 1 coding self-test), 0 failed.
- Gated self-test at `8f397b42`: `DIGITALBRAIN_CODING_SELF_TESTS=1 dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.CodingSelfTestFacts`
  passed (1/1).
- Flutter (from `src/Modules/UI/Flutter`): `dart format --output=none --set-exit-if-changed core ui shell/lib shell/test`
  87 files, 0 changed; `flutter analyze` no issues in `ui` and `shell`; `flutter test` 22 passed in `ui`,
  49 passed in `shell`.

## Phase 1 spikes (2026-09-14)

Design 9.1 lists no spikes for phase 1. Before the plan was written the APIs it relies on were checked
against current documentation (Context7, /dotnet/roslyn): `Renamer.RenameSymbolAsync(Solution, ISymbol,
SymbolRenameOptions, string, CancellationToken)`, `DocumentEditor`, `Solution.WithDocumentText`,
`SymbolFinder.FindCallersAsync`, `FindImplementationsAsync`, `FindDerivedClassesAsync`,
`CodeFixContext(Document, Diagnostic, Action<CodeAction, ImmutableArray<Diagnostic>>, CancellationToken)`,
`CodeAction.GetOperationsAsync` and `ApplyChangesOperation.ChangedSolution`. `Microsoft.CodeAnalysis.CSharp.Features`
5.9.0 is the latest stable on nuget.org. The C# code fixers are MEF exports; the plan discovers the ones with a
parameterless constructor by reflection, which is the part most likely to surprise (recorded here so the
outcome lands next to it). Plan: `plans/2026-09-14-coding-phase1-changesets.md`.

## Phase 1 outcome (2026-09-14)

Fact classes added or extended for phase 1, and their counts at the end of Task 9:

- The reflection theory in `CodeWorkspaceNeuronFacts` (`Every_contract_method_uses_section_7_types`)
  validates `ICodeWorkspace` (12 methods) and `IChangeSet` (5 methods) against the contract's section
  7 types; `CodeWorkspaceNeuronFacts` itself is 12 facts (10 `[Fact]` plus the 2-case theory),
  including `The_map_answers_from_the_durable_cache_while_a_reload_is_in_flight`.
- `WorkspaceReadFacts`: 9.
- `ChangeSetEditorFacts`: 16.
- `SolutionWorkspaceFacts`: 21 (phase 0's map/reload/lifetime facts plus the three commit facts:
  `Commit_applies_the_snapshot_and_writes_only_the_changed_files`,
  `Commit_of_unchanged_text_writes_nothing`, `Commit_refuses_a_snapshot_the_workspace_has_moved_past`).
- `ChangeSetNeuronFacts`: 7.
- `SolutionFileWatcherFacts`: 4.
- `RunnerFacts`: 11.
- `CodingNativeToolFacts`: 14 (up from phase 0's 5, now covering all thirteen `code_*` tools plus the
  never-settles-is-advice and repeated-check-answers-at-once cases).
- `CodingChatFacts`: 1 (`Rename_X_to_Y_lands_as_a_commit_through_the_chat`).
- The second gated fact, `CodingSelfTestFacts.The_real_solution_builds_and_one_class_tests_through_the_runner`,
  alongside phase 0's `The_real_solution_opens_and_answers_a_known_reference`.

Suite total after `CodingChatFacts`: `dotnet test DigitalBrain.slnx -c Release --no-build` reports
397 total, 391 passed, 0 failed, 6 skipped (4 Docker-gated ClickHouse scenarios plus the two coding
gated self-tests).

Exit criterion (design 9.1): "rename `TimerNeuron.Alarm` to `AlarmFor` and run the tests" lands as a
commit on a `coding/<id>` branch with the suite green, driven from the chat.
`CodingChatFacts.Rename_X_to_Y_lands_as_a_commit_through_the_chat` proves this scripted, end to end,
on the adhoc fixture (a rename of `Greeter.Greet` to `Hello`, standing in for the live
`TimerNeuron.Alarm` rename the controller drives against the real solution): a `ScriptedChatClient`
drives the `/agent` HTTP endpoint through one function-invocation turn — `code_find_symbols`,
`code_propose_edit` (`Rename`), `code_check`, `code_commit`, `code_build`, `code_test` — ending with
the model's own closing message. The fact asserts, in order, the six `TOOL_CALL_RESULT` payloads
(`Draft` after propose, `Checked` after check, `Committed` with `branch: "coding/rename-1"` after
commit, a succeeded build, 2 passed tests), the renamed call site on disk
(`.Hello("world")` in `Beta/Program.cs`), the git log's `coding: rename Greet to Hello` message, and
that the assistant's own `TEXT_MESSAGE_CONTENT` names `coding/rename-1`. The live counterpart
(`aspire run`, renaming `TimerNeuron.Alarm` verbatim against the real solution) is recorded below
under "Phase 1 live run" by the controller.

## Phase 1 deviations

1. Design 4.9 amended: `check` and `commit` refuse a change set for the diagnostics it *introduces*
   (a multiset difference against the pre-edit baseline, keyed by id, severity, message and path,
   over the changed projects and their dependents), never for errors the tree already had. Reason:
   the fixture's Beta project is permanently broken, and a literal "any error" filter failed five of
   nine editor facts; on a clean solution the two readings coincide. Cost: a pre-existing error stays
   invisible to the model unless it asks `code_diagnostics`; the baseline costs one extra compilation
   per check.
2. `ChangeSetSnapshot` carries `Detail` and `Files` beyond design 9.1's five members: the responsible
   edit must be named and the written files listed.
3. `SyntaxGenerator` is not used: `SyntaxFactory.ParseMemberDeclaration` plus `DocumentEditor` cover
   the four member edits.
4. `ExpectedVersion` on `ProposeEdit` is the change set's edit count.
5. A rejection's reason travels in the `CommandRejectedException` message argument: only `Message`
   crosses the grain boundary (the phase 0 lesson repeated).
6. The changeset neuron keeps `ConfigureAwait(true)` throughout, including inside the commit lambda;
   an earlier ruling allowing `ConfigureAwait(false)` there was withdrawn and the `ORLEANS0014`
   pragma removed.
7. Kernel rule surfaced by a throw: in a reaction `Schedule` must be called before `SaveAsync`.
8. `WorkspaceState.LastMap` is carried forward on open and reload so the map answers from the
   durable cache while a reload is in flight; the mapping loop carries an attempt counter
   (`MappingBody`) and gives up after 600 attempts.
9. The service counter is `SolutionWorkspace.SnapshotVersion` (and `CommitOutcome.SnapshotVersion`);
   the contract's `Generation` field keeps its design name.
10. The phase 0 fact `A_warmed_solution_reads_ready_before_any_open` now sets `WorkspaceKey` to the
    fixture and polls for generation 1: the warmup records the open on the grain asynchronously.
11. `DiskFixture.InitGitAsync` (the plan's interface bullet said `InitGit`); collection expressions
    where IDE0300 forces them.
12. Task 7's runner snippets were amended in review: the branch probe distinguishes "absent" (exit 1)
    from a timeout or any other failure; every git call surfaces `TimedOut`; the failed-test pattern
    keeps parameterized display names; `ProcessRunner` drains both streams after a kill and returns
    the partial output; `core.quotepath=false`; path comparison ignores case only on Windows.
13. The suite's skip count is 6 (4 Docker-gated scenarios + 2 coding gated facts).
14. Parked for later phases: `FoldAsync` returning false after a lost race (the next save
    re-enqueues); deleted files are not folded out of the snapshot (phase 5);
    `WorkspaceWarmup._stopping` is never disposed; the summary pattern lacks a word boundary;
    `ChangedPathsAsync` does not unescape C-quoted porcelain paths; the Windows timeout fact asserts
    English `ping` output.
15. Task 8: `code_commit` returns one key set in every branch (`status, files, generation, diff,
    detail, branch, commit, advice`) instead of the plan's two shapes, so a git refusal after the
    files are written still shows the model what landed on disk; the change-set tools wait for
    *this* command's outcome (status moved, `Discarded`, or a `Detail` different from the one read
    before the command) rather than any non-null `Detail`; a wait that expires is advice, not a
    thrown cancellation; `code_propose_edit` sends `ExpectedVersion` from a fresh read.
    `code_implementations("T:Alpha.IWelcome")` counts two implementations (`Shouter` inherits
    `Greeter`'s); `DiskFixture.BrokenPath` replaces the `E:/fixture` constant.
16. Task 8 (round 2): `ChangeSetSnapshot` gains `Revision` (`Id(7)`), incremented by every reaction
    that saves; the tools wait for the revision to move past the one read before the command, which
    is the only marker that tells "no new result" from "the same result again" (the `Detail` text is
    a pure function of the edits, so it repeats). The tool wait (`ReactionWait`, two minutes) lives
    in `CodingToolOptions`, registered by the module and shortened by facts through DI, not through
    static state.
17. Task 9: the brief's scripted-chat snippet calls `fixture.InitGitAsync()` with no argument, but
    `DiskFixture.InitGitAsync` (deviation 11) takes a required `CancellationToken` — `CS7036` on
    build; fixed by passing `TestContext.Current.CancellationToken`, as the task's own facts specify.

## Phase 1 observations

- Code-fix discovery by reflection: `CodeFixCatalog` loads `Microsoft.CodeAnalysis.CSharp.Features`
  5.9.0 and finds 155 exported `CodeFixProvider` types (non-abstract, carrying
  `[ExportCodeFixProviderAttribute]`); every one of them exposes a parameterless constructor in this
  version, so none is skipped for constructor shape. Only a fixer whose registration itself throws
  (one that needs the real IDE host) is skipped, at the `RegisterCodeFixesAsync` call site rather
  than at discovery.
- Formatter annotations: `ParseMember` and `AddUsingAsync` tag only the syntax they insert or replace
  with `Formatter.Annotation`, and `ChangeSetEditor.FormatAsync` calls
  `Formatter.FormatAsync(document, Formatter.Annotation, ...)` rather than formatting the whole
  document. An inserted member or using directive comes out correctly indented while the rest of the
  file's existing formatting — and the diff — stays untouched.
- Reload-needed semantics: the watcher folds a saved `.cs` file straight into the live `Solution`
  (`SolutionWorkspace.FoldAsync`, `WithDocumentText`), but a `.csproj`/`.props`/`.targets`/`.slnx`/
  `.sln` change cannot be folded that way — it changes project structure, not one document's text —
  so `MarkReloadNeeded` only flags `WorkspaceSnapshot.ReloadNeeded`; nothing reloads until asked.
- The grain-turn lesson: a `[ReadOnly]` `Read()` still queues behind whatever reaction currently
  holds the grain's one turn. A genuinely stuck `check`/`commit` reaction therefore blocks even a
  plain read behind it, which is why `CodingNativeTools.WaitAsync` bounds its own `Read()` calls with
  its own deadline instead of trusting Orleans' own, much longer, request timeout to notice.
- The "Detail repeats" lesson: `ChangeSetSnapshot.Detail` is a pure function of the edit list, so two
  unrelated reactions (two identical failing `check`s, for instance) can produce byte-identical
  `Detail`/`Status`. A wait keyed on "the detail changed" cannot tell that apart from "nothing
  happened yet"; `Revision`, bumped by every reaction that saves regardless of outcome, is the only
  marker that answers correctly either way (deviation 16 above).

## Phase 1 live run (`aspire run --detach`, 2026-09-14)

### Run 1, 04:11 UTC: "rename `TimerNeuron.Alarm` to `AlarmFor` and run the tests"

The kernel answered on 5080 eight seconds after `aspire run --detach` returned; the chat was sent at 04:12:42Z
through `/agent` (transcript `live/chat-rename-1.txt`, tool durations measured client-side from the AG-UI events).

| Step | Tool | Duration | Result |
|---|---|---|---|
| 1 | `code_find_symbols("TimerNeuron.Alarm")` | 2.0 s | 0 hits (qualified names do not match) |
| 2 | `code_find_symbols("Alarm", 50)` | 4.8 s | hits led by Orleans-generated `Codec_Invokable_ITimerAlarm_*` types from `obj/Debug/...g.cs` |
| 3 | `code_references(M:DigitalBrain.Time.TimerNeuron.Alarm(System.Int64))` | 8.2 s | the five call sites in `TimerNeuron.cs` |
| 4 | `code_propose_edit(Rename → AlarmFor)` | 0.7 s | `Draft`, revision 1 |
| 5 | `code_check` | 2.7 s | `Checked`, no diagnostics, a six-line diff of `TimerNeuron.cs` |
| 6 | `code_commit` | 1.4 s | `Committed`, generation 1, **`files: []`**, git refused: "changes outside the change set: src/Modules/Time/Time/TimerNeuron.cs" |
| 7 | `code_build(artifactsPath: "artifacts/rename-timer-alarm")` | 9.4 s | "succeeded", 0 warnings — but see finding 2 |
| 8 | `code_test(artifactsPath: ...)` | — | result lost: the smoke script crashed printing U+2193 on a cp1252 console |

Findings, each fixed before run 2:

1. **`MSBuildWorkspace.TryApplyChanges` writes the files itself.** `SolutionWorkspace.CommitAsync` applied the
   snapshot and then wrote every changed document whose disk text differed from the new text; against the real
   workspace the apply had already saved the file, so nothing differed, `WrittenPaths` came back empty, the tool
   handed git an empty list and git refused the tree's own change. The adhoc test workspace never writes on apply,
   which is why no fact caught it. Fix: the commit snapshots each document's disk text before the apply and reports
   every document whose text changed; the test workspace now mirrors the MSBuild behaviour so the commit facts are
   the regression proof.
2. **A relative `ArtifactsPath` is per project.** `-p:ArtifactsPath=artifacts/rename-timer-alarm` produced
   `artifacts/rename-timer-alarm/{obj,bin}` under all 38 project directories (git-ignored only through the bin/obj
   patterns), and the generated `obj/**/*.cs` there enter every later build's compile glob: the next build in the
   tree failed with 17 CS0579 duplicate-attribute errors. Fix: `DotnetRunner` roots the path at the solution
   directory; phase 2's slots must use absolute paths under `<repo>/artifacts/<slot>`.
3. The smoke script printed with the console code page; it now forces UTF-8.

Open observation: `code_find_symbols` still lists Orleans-generated types from `obj/` first for a short query
(design 9.1's query-hygiene note); the model recovered by asking for references of the exact id.

### Run 2, 04:58 UTC, on the three fixes

| Step | Tool | Duration | Result |
|---|---|---|---|
| 1 | `code_find_symbols("TimerNeuron")` | 4.0 s | exactly `T:DigitalBrain.Time.TimerNeuron` — the generated codec types no longer appear |
| 2 | `code_skeleton(TimerNeuron.cs)` | 1.0 s | the type, its fields and members with ids |
| 3 | `code_propose_edit(Rename, changeId "rename-timer-alarm")` | 0.8 s | refused: "the change set is committed. Start a new change set with a new id." |
| 4 | `code_check("rename-timer-alarm")` | 1.2 s | refused likewise; the model asked for a new change-set context instead of choosing one |

Finding 4: change sets are durable grains keyed by a model-chosen id, so run 1's committed change set was
still there after the restart. Fix: the agent instructions ask for a fresh id per task and the refusal
suggests one.

### Run 3, 05:22 UTC, on all four fixes: the exit criterion

Kernel restarted on `45e26df3`; the same sentence, sent at 05:31:47Z; the run finished after 130 s
(transcript `live/chat-rename-3.txt`).

| Step | Tool | Duration | Result |
|---|---|---|---|
| 1 | `code_find_symbols` | 4.4 s | `T:DigitalBrain.Time.TimerNeuron` only |
| 2 | `code_references(M:DigitalBrain.Time.TimerNeuron.Alarm(System.Int64))` | 7.1 s | the five call sites |
| 3 | `code_propose_edit(Rename → AlarmFor, changeId "rename-timer-alarm-20260914")` | 1.2 s | `Draft`, revision 1 — a fresh id this time |
| 4 | `code_check` | 3.6 s | `Checked`, no new diagnostics, six-line diff |
| 5 | `code_commit` | 1.5 s | `Committed`, `files: [TimerNeuron.cs]`, branch `coding/rename-timer-alarm-20260914`, commit `272f9499` |
| 6 | `code_build(artifactsPath "artifacts/rename-timer-alarm-20260914")` | 8.4 s | succeeded, 0 warnings, outputs under `<repo>/artifacts/rename-timer-alarm-20260914` (nothing under project folders) |
| 7 | `code_test(artifactsPath …)` | 88.8 s | 417 total, 411 passed, 0 failed, 6 skipped |
| 8 | assistant | — | "Renamed `TimerNeuron.Alarm` to `AlarmFor`." |

`git log -1 coding/rename-timer-alarm-20260914` shows `272f9499 coding: Rename TimerNeuron Alarm helper to
AlarmFor`, one file, six lines each way — design 9.1's exit, driven from the chat. The branch stays local as
evidence and is not pushed; the phase branch is untouched by it. The dashboard trace for the run exceeded the
MCP tool's size limit and could not be listed; the durations above are measured client-side from the AG-UI
events.

## Phase 1 whole-branch review and fix wave (2026-09-14)

The review ran the code-review skill at high effort over `feature/coding-phase0-workspace..HEAD`: eight
finder angles (line-by-line, removed behaviour, cross-file, reuse, simplification, efficiency, altitude,
conventions) over the branch diff split by area, then one verifier per angle (recall-biased: PLAUSIBLE by
default, REFUTED only when constructible from the code). Ten findings were reported; the fix wave took every
confirmed one plus the confirmed minors, in five commits (`006074cf`, `ae77e759`, `e804be1a`, `4a526e96`,
`5e223563`). Two deviations the implementer recorded: the "already open" branch also requires the grain to
have recorded that path, so the warmup's first open still reaches generation 1; and the check-side catch is
covered by regression only (every exception the check path raises today derives from
`InvalidOperationException`), the commit-side denied-write fact being the red-before-green proof of the same
catch shape.

Important, fixed:

1. `CommitAsync` and `FoldAsync` shared the two-slot query gate, so a watcher fold during a commit could be
   refused as stale or silently dropped. A dedicated writer gate now serialises writers ahead of the lease.
2. The checking and committing reactions caught only `InvalidOperationException`; an `IOException` from a locked
   file left the change set unsettled and retrying while the tool reported only "did not settle". Both catch
   every non-cancellation exception and settle with the message.
3. Nothing bounded the Roslyn work inside a reaction, so a hung edit could hold the grain's single turn after
   the tool's client-side wait gave up. `CodingToolOptions.EditDeadline` (90 s) bounds the check query and the
   commit's apply; a timeout settles as `Draft` with the reason.
4. `code_map` called the service directly and never reached the grain's durable map cache. It goes through the
   `workspace` grain, which answers from the cache while a reload is in flight.
5. `code_commit` created and checked out `coding/<id>` before the dirty-tree check, so a refused commit parked
   the tree on an empty branch. The check runs first; the result reports the base branch; the tree stays on
   `coding/<id>` afterwards and later change sets commit on top of it (one working tree keeps disk and snapshot
   coherent — design 4.5 step 7, D3).
6. Re-opening the already loaded path was a service no-op while the grain bumped `Generation`. `BeginOpenAsync`
   reports whether a load started; the reaction records "already open" without a bump.
7. Per-project diagnostics compiled one project at a time, twice per check; the baseline and the edited
   snapshot now compile concurrently, as do the changed projects.

Minor, fixed: blame prefers the edit whose path and line span contain the error; the dead four-tools fact is
gone and `code_derived` joins the tools (fourteen); the mapping reaction waits 250 ms per attempt; renames
enqueue the old path and a vanished file flags a reload; the reload reason is bounded (last path plus a count);
the watcher uses `TimeProvider`; the tool poll backs off from 20 ms to 500 ms; formatting runs concurrently;
the idle watcher tick no longer snapshots an empty dictionary; the runner shares one artifacts-path helper; one
`TestWait.UntilAsync` replaces three test wait helpers and four hand-rolled loops; the one pragma without an
inline reason got one.

Refuted or deferred with a note: dropping generated declarations from `find-symbols` is what design 9.1
sanctions (references mark them); the exact-name-first ordering is the intended phase 0 follow-up; the
post-apply read in `CommitAsync` avoids a redundant write under `MSBuildWorkspace`; `Generation` stays on the
change-set snapshot (design 9.1's name); the warmup's bounded retry and a path-based generated-code test are
acceptable for phase 1; a `cached` marker on `SolutionMap`, a shared process runner with the AI module's
`RepositoryDiffFunction`, a shared poll helper with the ClickHouse tools and a shared settle helper in the
neuron are later refactors.

### Final gates (2026-09-14, at `5e223563`)

Run by the controller after the fix wave, on a clean tree:

```text
dotnet format whitespace DigitalBrain.slnx --verify-no-changes   -> no changes
dotnet build DigitalBrain.slnx -c Release                         -> 0 Warning(s), 0 Error(s)
dotnet test DigitalBrain.slnx -c Release --no-build               -> total 423, failed 0, succeeded 417, skipped 6 (1m 25s)
DIGITALBRAIN_CODING_SELF_TESTS=1 dotnet test ... CodingSelfTestFacts -> total 2, failed 0, succeeded 2 (21s)
```

Re-run after the follow-up commit `d7b0db44`: the same counts (417 passed, 0 failed, 6 skipped; a clean rebuild
reported 0 warnings).

The six skips are the four Docker-gated ClickHouse scenarios and the two coding self-tests when the variable
is unset. No Dart file changed in phase 1, so the Flutter gate was not run. The scoped re-review of the fix
wave (five commits) verified every finding and found no new Critical or Important breakage; its three minors
(the writer gate disposed from the lease-release path at shutdown, the new map-cache fact racing the warmup,
an unbounded fan-out for whole-solution diagnostics) were folded into one follow-up commit and the gate was
re-run on it. Accepted residual: an `open` of the same path that arrives while the first load is still in
flight bumps the generation once more.
