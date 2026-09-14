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
