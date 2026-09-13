# UI layer design (proposal, 2026-09-13)

Status: **ratified 2026-09-13**; phase 0 has landed (section 7), phases 1 to 5 are not implemented. The evidence behind
every claim is in `ui-layer-research.md` (section numbers are cited as R1.4, R2.5 and so on).

## 1. What this is for

The owner wants three things from the UI layer:

1. A UI kit whose components (chart, data table and more) have rich, clean interfaces and
   implementations, with proper separation of responsibilities.
2. The ability to test functionality end to end, from a chat message through the kernel to a
   rendered UI update.
3. A smaller, clearer UI module and Flutter code base: contracts and interfaces that carry their
   weight, and everything unreachable removed.

Definition of done for the whole programme:

- One wire contract, `UiPart`, describes everything the UI renders, on both sides of the wire,
  pinned by shared JSON fixtures that C# writes and Dart reads.
- Every kit component follows one anatomy (model, controller, view, tokens) and is testable in
  isolation with fixtures, without a kernel and without pixels.
- The shell dispatches on a registry, not on kind strings; artifacts are typed; the two god
  objects (`WorkspaceApp`, `WorkspaceChat`) are gone.
- This test exists and runs in CI: *a user sends "chart companies by country", the recorded kernel
  stream is replayed through the real client, and the shell shows a chart with three bars*. A
  second, environment-gated tier runs the same scenario against a live in-process kernel.
- Dead paths are deleted: about 6,300 lines of unreachable shell code, the dead C# Transcript
  family, the unmapped chat HTTP routes, unused client methods and packages (R1.5, R2.4, R3.5).

## 2. Principles that decide the details

- **Reachability decides what stays.** Code that `main.dart` cannot reach and `Program.cs` does not
  map is deleted, not refactored. Kernel neurons with scenarios stay unless a decision point below
  says otherwise.
- **Contract first.** The kernel owns the shape of every UI payload as a C# record; Dart mirrors
  it and a fixture test proves the mirror. No anonymous objects in tool results, no literal keys in
  the shell.
- **One anatomy per component.** Model (immutable, wire), controller (state and IO seams, a
  `ChangeNotifier`), view (stateless over the controller), tokens (from `Theme.of(context)`).
  A component never fetches in `initState` and never reads a palette constant.
- **One registry.** Adding a kind means one C# record, one Dart model case and one renderer
  registration. Icons, tiles, editors and chat bubbles come from the registry.
- **Tests own the fixtures.** Gallery previews, widget tests and contract tests share the same
  fixture files; no fixture is inlined in a `State`.
- **Repo vocabulary.** Neurons fire signals along synapses; UI payloads are parts; a card is a
  part reference; nothing is called an entity, event bus or grain in user-facing code.
- **No decorative comments.** No `/// <summary>` that restates a signature; a short inline comment
  only where the reason is not visible in the code.

## 3. Approaches considered

**A. Contract first, then components, then shell (recommended).** Delete the unreachable code,
define `UiPart` on both sides with fixtures, migrate tools and endpoints onto it, rebuild the kit
components on one anatomy and a registry, then decompose the shell onto the registry and typed
artifacts, then add the end-to-end tiers. Each phase leaves the app working and the suites green.
Cost: the shell god objects survive until phase 3. Benefit: every later step has a typed contract
to lean on, and the kind-string duplication (R3.3) disappears once instead of file by file.

**B. Shell first.** Split `WorkspaceApp` and `WorkspaceChat` immediately, keep the wire as it is,
introduce the contract afterwards. Visible cleanup arrives sooner, but the split would be done
against untyped maps and string kinds and would have to be redone when the contract lands.

**C. New shell package from scratch.** Highest risk, longest time without a working product,
and it would throw away the parts of the shell that are sound (store persistence, window
management, voice, table import). Rejected.

## 4. Target architecture

### 4.1 One contract: parts

A `UiPart` is the unit the UI renders. It has a `kind`, an `id` (the backing neuron's name, whose
prefix routes reads: `table-`, `chtable-`, `chart-`, `graph-`, `image-`, `document-`, `diagram-`,
`sheet-`), a `title`, a `revision`, and a kind-specific payload. A `UiPartRef` is the same
identity without the payload; it is what a chat card carries and what a listing returns.

C# (Contracts, namespace `DigitalBrain.UI`; the `DigitalBrain.Chat` namespace folds into it):

```csharp
public static class UiPartKinds
{
    public const string Table = "table", Chart = "chart", Graph = "graph", Image = "image",
        Document = "document", Diagram = "diagram", Brain = "brain", Stat = "stat",
        Dashboard = "dashboard", Spreadsheet = "spreadsheet";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(TablePart), UiPartKinds.Table)]
[JsonDerivedType(typeof(ChartPart), UiPartKinds.Chart)]
// … one line per kind
public abstract record UiPart(string Id, string Title, long Revision);

public sealed record ChartPart(string Id, string Title, long Revision, string ChartKind,
    IReadOnlyList<ChartPoint> Points) : UiPart(Id, Title, Revision);

public sealed record TablePart(/* today's TableSnapshot members */) : UiPart(Id, Title, Revision);

public sealed record UiPartRef(string Kind, string Id, string Title);   // replaces UiCardOffer

public sealed record UiToolError(string Tool, string Code, string Message); // kind: "error"
```

Rules:

- Every native tool that produces UI returns a `UiPart` (inline payload) or a `UiToolError`.
  `render_chart`, `show_query_table`, `create_table`, `read_table`, `update_table_view`,
  `show_graph` and `generate_image` all follow this; the three prose-or-JSON inconsistencies in
  R1.5 disappear. The four exception-to-result mappers become one.
- A chat card is a `UiPartRef`. `Responded.Cards` becomes `IReadOnlyList<UiPartRef>`; the Orleans
  alias `ui.kit-card` is kept so persisted snapshots still load. A rendered signal
  (`ChartRendered`, `TableRendered`, ...) carries a `UiPartRef` as its body, so the announcing
  neuron states its own kind and `ChatNeuron.OfferAsync` no longer maps signal types to kinds or
  defaults unknown ones to `spreadsheet`; a body that is not a `UiPartRef` is refused.
- Reads go through one endpoint, `GET /ui/parts/{id}` (with `offset` and `limit` for paged kinds),
  served by `IUiPartSource` implementations registered per id prefix (the generalisation of
  today's `ITableSource`). The UI module registers the chart, graph, image, document, diagram and
  in-memory table sources; ClickHouse registers `chtable-`; Excel registers `sheet-`. `UiEndpoints`
  loses its per-kind routes and its Excel import. `GET /ui/parts` lists `UiPartRef`s plus
  revisions from saved summaries. Kind-specific mutations keep their own routes
  (`PUT /ui/tables/{id}/view`, `PUT /ui/documents/{id}`).
- The workspace artifact store speaks parts too: `POST/GET/PUT /workspace/artifacts` carry a
  `UiPart` instead of an untyped `content` map, so the shell has one artifact type.

Dart (`digitalbrain_flutter`, the core package, which becomes the home of every wire model):

```dart
sealed class UiPart {
  String get kind; String get id; String get title; int get revision;
  factory UiPart.fromJson(Map<String, Object?> json) => switch (json['kind']) { … };
  Map<String, Object?> toJson();
}
final class ChartPart extends UiPart { final String chartKind; final List<ChartPoint> points; … }
final class TablePart extends UiPart { … }           // replaces TableSnapshot
final class UiPartRef { final String kind, id, title; … }
final class UiToolError { final String tool, code, message; … }
```

The contract is pinned by fixtures, not by reflection. A C# fact serialises one sample of every
kind, every ref, every error and every AG-UI tool-result envelope into
`src/Modules/UI/contracts/fixtures/parts/*.json` and fails when the checked-in file differs
(`DIGITALBRAIN_UPDATE_FIXTURES=1` rewrites). A Dart test in `core` loads the same files, parses
each into the expected subtype and asserts a lossless `toJson` round trip. The reflection golden
(`flutter-wire-contracts.golden.json`) is retired once the fixtures cover its types.

### 4.2 Kernel and UI module

- **Module shape** stays: `UIModule` registers services, part sources and native tools. Tools are
  built once per tool class, not once per tool name (R1.5).
- **Neurons** keep their contracts (`IChat`, `ITable`, `IChart`, `IGraph`, `IImage`,
  `IWorkspaces`) and gain `IDocument` and `IDiagram` (text neurons with revision conflicts, the
  `ITable` pattern without paging) and `IStat`/`IDashboard` only if decision point 6 keeps them.
  `ITranscript` and `TranscriptNeuron` are deleted. `ISurface`, `IActivities` and their endpoints
  are untouched and out of scope (decision point 2).
- **Chat paths.** The workspace AG-UI agent is the shipped transport (R1.4). `ChatNeuron` stays
  as the kernel's durable conversation model with its scenarios. The unmapped HTTP chat and voice
  routes are deleted with their `http.feature` scenarios and the matching core client methods
  (decision point 1). Bridging `/agent` onto `ChatNeuron` (durable turns, journals and cards for
  the workspace) is the natural follow-up and is written down in section 8, not done here.
- **Prompt ownership.** The 65-line `ConversationalAgent` prompt is split into per-module
  instruction fragments contributed through the same seam as native tools (`AddNativeTool` gets a
  sibling `AddAgentInstruction`), so ClickHouse, Salesforce and the UI module own their own
  sentences and the allowlist is derived from the registered fragments.
- **Small fixes bundled in:** one poll-until-applied helper for `TableService` and `UiTools`; the
  duplicate in-memory table source registration; the `SheetChanged` constant declared once;
  memory and blob stores with the same visibility; `TableSnapshot.Kind` from the constant.

### 4.3 Core client (`digitalbrain_flutter`)

- `DigitalBrainUiClient` shrinks to the routes production uses and is split into cohesive
  files behind one class: `auth`, `agent` (AG-UI run), `parts` (`readPart`, `listParts`,
  `updateTableView`, `updateDocument`), `artifacts`, `brain`, `voice`. Every request goes through
  one `_send` with one failure type, `UiRequestException(statusCode, code, message)`; 404 on a part
  read is `UiPartMissingException`, not `null`.
- `AgentEvent` becomes a sealed hierarchy (`RunStarted`, `RunFinished`, `RunError`,
  `TextMessageStart/Content/End`, `ToolCallStart/Args/End/Result`, `UnknownAgentEvent`) parsed
  from one `SseDecoder` that is also used by `watchBrain`. The decoder accumulates multi-line
  `data:` frames (the bug in `SseFrameParser`, R2.5) and reconnects with a resume cursor and
  backoff everywhere, not only in `_watchEvents`.
- Dead methods, `web_socket_channel`, the empty `executables` entry, the `ui_models.dart` shim and
  the exported transport internals go.

### 4.4 Kit (`digitalbrain_ui`)

**Component anatomy.** Every component `X` consists of:

- `XPart` (in core): the immutable wire model.
- `UiPartController<T extends UiPart>` (kit base class): `part`, `state`
  (`idle | loading | ready | error(message)`), `load()`, `reload()`, `accept(T)` (monotonic on
  revision), `dispose()`, with IO injected as function types (`ReadPart`, kind-specific writers).
  `UiTableController` adds `change(...)` with 409 reconciliation and paging; `UiChartController`
  adds `append`; `UiDocumentController` adds `edit`/`save` with conflict state. `busy`/`error`
  public fields become the `state` value.
- `UiX` view (stateless over the controller, or over the part for static kinds), themed only
  through `UiTokens`, keyed by `id`, with `Semantics` for what it shows (a chart exposes one
  semantics node per point, "GB 11", which is also what tests assert).
- `UiXTile` (compact chat/dock representation) and, where editing exists, `UiXEditor`.

**Registry.** `UiPartRegistry` maps kind to a `UiPartRenderer` (`icon`, `label`,
`createController(part, io)`, `tile`, `view`, `editor?`, `servedInline`, `editable`). The shell
never switches on a kind string again; the registry is also what the gallery iterates. A kind
missing from the registry renders a generic "unsupported part" tile with the raw JSON, never a
crash.

**Theme.** One `UiTokens` `ThemeExtension` with light and dark values (the Lumen light values
become the light tokens; today's dark `UiPalette` becomes the dark tokens; the ~20 inline hex
literals in `UiTheme.workspace()` move into it). `UiType` is derived from `TextTheme` plus tokens.
`forui` and `material_ui` are removed (decision point 4), which also removes the double-Material
problem in `UiThemeScope`.

**Components after the refactor** (wave 1):

| Kind | Model | Controller | View / editor | Backing |
|---|---|---|---|---|
| `table` | `TablePart` | `UiTableController` | `UiDataTable`, filter and column dialogs as their own files, number parsing moved to core | `ITable`, ClickHouse `chtable-` |
| `chart` | `ChartPart` | `UiChartController` | `UiChart` (bar, line; tooltips; per-point semantics) | `IChart` |
| `graph` | `GraphPart` | `UiGraphController` (kept) | `UiGraph` 2D; `three_js` scene dropped (decision point 4) | `IGraph` |
| `image` | `ImagePart` | `UiImageController` (bytes) | `UiImage`, `UiImageEditor` (rotate, brightness, promoted from the shell) | `IImage` |
| `document` | `DocumentPart` (markdown) | `UiDocumentController` | `UiDocument` (render), `UiDocumentEditor` | `IDocument` (new) |
| `diagram` | `DiagramPart` (markdraw source) | `UiDiagramController` | `UiDiagram`, `UiDiagramEditor` (promoted from the shell; `markdraw` moves to the kit) | `IDiagram` (new) |
| `brain` | `BrainPart` (`BrainSnapshot`) | `UiBrainController` (live watch) | `UiBrainGraph` (today's `LumenBrainGraph`) | brain observation endpoints |
| `stat` | `StatPart` (label, value, unit, delta, series?) | none (static) | `UiStat` | `render_stats` tool (decision point 6) |
| `dashboard` | `DashboardPart` (grid of `UiPartRef`s) | `UiDashboardController` (loads children) | `UiDashboard` | `IDashboard` (decision point 6) |
| `spreadsheet` | `SheetPart` | `UiSheetController` | `UiSheet` | Excel `sheet-` |

Shared building blocks: `UiTranscript` (the chat list of typed entries; replaces the
`flutter_chat_ui` wrapper and its builders, decision point 4), `UiComposer` (multi-line, Enter to
send, voice slot), `UiToolTile` (generic tool result), `UiToolErrorTile`, `UiPartTile`,
`UiArtifactDock`, `UiProjectLibrary`, `UiExplore` (kept, given named parameters), `UiCopyable`.

Deleted from the kit: `UiView`, `UiSpecialistSuggestions`, `UiEditorFrame`,
`UiChatBuilders`, `UiMessageFactory`, `UiStreamCopy`, the five private ref loaders, the
`onboarding/` folder, `InoPresence` unless the owner wants it kept (decision point 5), the
`three_graph_scene`. The gallery stays as the kit's living catalogue but is driven by the registry
and the shared fixtures, one preview widget per kind, no 843-line switch.

### 4.5 Shell (`digitalbrain_flutter_shell`)

**Structure after the refactor:**

```
lib/
  main.dart                     builds the shell from one DigitalBrainUiClient
  auth/                         session gate, login, credential stores (kept)
  workspace/
    model/                      WorkspaceProject, WorkspaceConversation, WorkspaceArtifact,
                                WorkspacePresentation, TranscriptEntry (all typed)
    store/                      WorkspaceStore (state + mutators), WorkspacePersistence,
                                debounced save
    artifacts/                  ArtifactCatalog (accept / open / edit / save / conflict),
                                PartControllers (one cache for all kinds), LiveBrainObserver
    agent/                      AgentRun (pure reducer over AgentEvent -> transcript changes and
                                artifact offers), AttachmentContext (pure prompt fragment builder),
                                SalesforceConnectFlow
    screens/                    WorkspaceShell (MaterialApp, routes), DirectoryScreen,
                                ProjectScreen, SettingsScreen
    chat/                       WorkspaceChat (composer + UiTranscript over AgentRun)
    desktop/                    WorkspaceDesktop, window geometry (kept)
    dialogs/                    new work, attach, import, project files, search
    voice/                      WorkspaceVoiceButton (kept)
```

**Typed artifacts.** `WorkspaceArtifact { id, kind, title, UiPart? part, int revision,
ArtifactSync sync, EditorState editorState }` with `enum ArtifactSync { local, saved, draft,
live }` replaces the `_remote`/`_dirty`/`_live`/`_revision`/`live`/`revision` flag soup (R3.3).
`servedByResult`, `editable` and the icon come from the registry entry for `kind`.

**Agent run as a reducer.** `AgentRun.apply(state, event)` is a pure function from a typed
`AgentEvent` to a new transcript state plus a list of effects (`OpenPart(UiPart)`,
`ShowError(UiToolError)`, `Finished`, `Failed`). `WorkspaceChat` subscribes to the stream, applies
the reducer, and hands effects to `ArtifactCatalog`. The reducer is tested with the recorded
fixtures without any widget.

**Behavioural fixes that fall out:** one `WorkspaceChat` mounted per visible conversation, the
Salesforce poll only while a login is pending, saves debounced per store instead of per frame,
no store writes during build, no cross-project mutation from a table listener, `_accept` no longer
steals the active window for background results, and no `catch (_) {}`.

**Deleted from the shell** (the reachability rule): the whole `chat/` subtree except
`brain_graph_store.dart` (which becomes the kit's `UiBrainController`) and the `AgentRunner`
typedef (which moves to core), `demos/`, `windowing/`, `onboarding/`, `activity_screen.dart`,
`chat_screen.dart`, `brain_theme.dart`, the two unused assets, `flutter_svg`, and the
`WorkspacePresentation.layout` legacy field with a one-time migration in `load()`.

### 4.6 Testing

Three tiers, all deterministic, the third gated:

- **T0, units and components.** C# facts and scenarios as today plus the fixture facts. Dart:
  model round trips over the shared fixtures; controllers with fake readers; every kit view with
  its fixture; `AgentRun` with recorded event lists; store and persistence.
- **T1, replay end to end (CI default).** A C# fact drives the scripted model through the real
  `/agent` host over a `BrainSimulation` (the `TableAgentFacts.StartAsync` hosting, R4.1) for a
  small set of scenarios (`chart_by_country`, `query_table`, `table_error`, `text_only`) and
  records the raw SSE bytes, normalised for ids and timestamps, into
  `src/Modules/UI/contracts/fixtures/agui/<scenario>.sse`; the fact fails when a recording
  drifts. Dart shell tests serve those bytes through `MockClient.streaming` to the real
  `DigitalBrainUiClient`, boot the real shell with `buildShell(edge:)`, type the prompt, and assert
  on widgets and semantics (`find.bySemanticsLabel('GB 11')`, `UiChart` with three points, the
  artifact open in the working area, the transcript text). This is the sentence in section 1.
- **T2, live end to end (`DIGITALBRAIN_UI_E2E=1`).** A C# fact hosts the same simulation on a real
  local port and runs `flutter test test/e2e --dart-define=DIGITALBRAIN_UI_BASE=http://127.0.0.1:<port>`
  as a child process; the Dart e2e tests use the same assertions as T1 against the live server
  (with `HttpOverrides` reset so the test binding allows real sockets). Runs in a separate CI job
  that installs both SDKs.
- **CI.** `ci.yml` gains `flutter analyze` and `flutter test` for `core`, `ui` and `shell` on
  every pull request (today they run only on release, R4.3), and the T2 job.

## 5. What is deleted, by the reachability rule

| Area | Deleted | Evidence |
|---|---|---|
| C# Contracts | `ITranscript`, `AppendTranscript`, `TranscriptState`, `TranscriptEntry`, two vocabulary constants, three JSON entries; `UiCardOffer` renamed to `UiPartRef` (alias kept); `UiCard` replaced by `UiPartRef` as the rendered-signal body | R1.1 |
| C# module | `TranscriptNeuron`; duplicate table source registration; per-name tool construction | R1.2, R1.5 |
| Kernel HTTP | `ChatEndpoints.cs`, `ChatVoiceEndpoints.cs` (never mapped); per-kind `/ui/*` readers replaced by `/ui/parts`; Excel import in `UiEndpoints` | R1.3, R1.4 (decision point 1) |
| C# tests | `http.feature` chat scenarios; the reflection golden once fixtures cover it | R1.6 |
| Aspire hosting | `ShellNames` re-exports in `ShellHostingExtensions`; the stale `task-4-report.md` citation | R1.5 |
| Core client | eleven uncalled methods, `web_socket_channel`, `executables`, `ui_models.dart`, exported internals, two inline SSE loops | R2.4, R2.5 |
| Kit | `UiView`, `UiSpecialistSuggestions`, `UiEditorFrame`, `uiCustoms`, `customMessageForPart`, chat builders and loaders, `onboarding/`, `three_graph_scene`, `forui`, `material_ui`, `three_js`, `flutter_chat_*` | R2.5 (decision points 4, 5) |
| Shell | about 6,300 lines: `chat/` (minus the graph store and the typedef), `demos/`, `windowing/`, `onboarding/`, `activity_screen.dart`, `chat_screen.dart`, `brain_theme.dart`, unused assets, `flutter_svg`, `AgentChatApp` | R3.5 |
| Shell tests | the three tests that only reach the deleted subtree; the ten copies of the persistence fake become one `test/support/memory_workspace.dart` | R3.7 |

Expected size after wave 1: shell from 12,946 to roughly 5,000 lines, kit from 8,939 to roughly
6,000 with more components, core from 2,205 to roughly 1,800 with typed events and models.

## 6. Decision points

Each has a default. Silence means the default.

1. **Which chat is the product?** Default: the workspace AG-UI agent is the shipped transport;
   `ChatNeuron` stays as the kernel model with its scenarios; the unmapped HTTP chat and voice
   routes, their `http.feature` scenarios, the core client chat methods and the shell `chat/`
   subtree are deleted. Alternative: map the routes in `Program.cs` and make the workspace use
   `ChatNeuron` now (a larger, AI-module change; section 8 keeps it as the follow-up).
2. **Surfaces and activities.** Default: `ISurface`, `IActivities`, their neurons, endpoints and
   scenarios stay in the kernel untouched; nothing in the kit or shell renders them after the
   cleanup. Alternative: delete them too (their only client was the deleted shell subtree).
3. **Dart models.** Default: hand-written immutable classes with `fromJson`/`toJson`, equality and
   fixture round-trip tests; no `build_runner`. Alternative: `freezed` + `json_serializable`
   (generated files in the repo, a build step in CI).
4. **Framework dependencies.** Default: remove `forui`, `material_ui` (one widget uses them),
   `three_js` (the 3D graph scene; the 2D graph stays), `flutter_chat_core`/`flutter_chat_ui`/
   `flyer_chat_*` (the workspace overrides every builder and only uses the list), `flutter_svg` in
   the shell; keep `graphic` for charts, `gpt_markdown` for markdown, `markdraw` for diagrams
   (moved to the kit). Alternative: keep `flutter_chat_ui` and rebuild the builders on the registry.
5. **Lumen visuals.** Default: keep `UiBrainGraph` (today's `LumenBrainGraph`), `NeuronIcon`, and
   the `InoPresence` and Lumen controls it renders with (they are reachable from `main.dart`
   through the brain editor); phase 2 rebuilds the brain graph on tokens and drops what it no
   longer needs then. Alternative: keep `InoPresence` as a standalone kit component with a
   fixture.
6. **New components in wave 1.** Default: `document` and `diagram` (promoted from the shell into
   real components with kernel neurons), `stat` (a `render_stats` tool for KPI tiles from query
   aggregates) and `dashboard` (a grid of part references, so "chart like a table" becomes a
   composable surface). Alternative: only `document` and `diagram` now.
7. **Live end-to-end tier.** Default: T2 is the last phase, gated by `DIGITALBRAIN_UI_E2E=1`,
   with its own CI job. Alternative: T1 only.
8. **Namespaces and names.** Default: `DigitalBrain.Chat` folds into `DigitalBrain.UI`; the
   product name is spelled `IntoChat` everywhere; `spreadsheet` is the kind and `Sheet` the
   Dart type. Alternative: leave namespaces alone to limit churn.

## 7. Phases

Each phase is one pull request, green on the CI commands, with a `ui:` commit prefix, and leaves
the app working. Phases 0 and 1 are the first implementation plan; the later ones get their own
plans once the contract exists.

0. **Remove what is unreachable.** Everything in section 5 that needs no replacement: the shell
   subtree, demos, windowing, onboarding, dead kit symbols, dead client methods and packages, the
   Transcript family, the chat HTTP routes (decision point 1). Tests that only covered deleted code
   go with it; the persistence fake is consolidated. Exit gate: suites green, `main.dart`
   unchanged in behaviour, line counts in the research note updated. Done on 2026-09-13 on branch
   feature/ui-phase0-remove-unreachable (commits a5fb62b2 to 8b22bab1 inclusive, plus the outcome
   note).
1. **The contract.** C# `UiPart` records, `UiPartRef`, `UiToolError`, polymorphic JSON, tool
   results as parts, `IUiPartSource` and `/ui/parts`, cards as refs, the fixture fact and the
   fixture files. Dart core: the `UiPart` hierarchy, typed `AgentEvent`, one `SseDecoder`, the
   fixture round-trip test, the client split. The shell keeps working through a thin adapter that
   maps parts to today's maps (removed in phase 3). Exit gate: fixture tests pass on both sides;
   `render_chart` and `show_query_table` smoke tests unchanged from the user's point of view.
2. **Kit components on one anatomy.** `UiTokens`, `UiPartController`, `UiPartRegistry`,
   `UiChart` and `UiDataTable` rebuilt (semantics, tokens, files split), `UiGraph`, `UiImage`,
   `UiSheet`, `UiBrainGraph` migrated, `UiTranscript`/`UiComposer`/tiles, the gallery driven by the
   registry and fixtures, framework dependencies removed. Exit gate: every kit component has a
   fixture test; gallery shows every registered kind in light and dark.
3. **Shell decomposition.** Typed artifacts and store, `ArtifactCatalog`, `PartControllers`,
   `AgentRun` reducer with fixture tests, `WorkspaceChat` and `WorkspaceApp` split into the
   structure in 4.5, registry-driven dispatch, behavioural fixes. Exit gate: no kind string
   switch left in the shell; `WorkspaceApp` gone; T1 replay test for `chart_by_country` green.
4. **New components.** `IDocument`, `IDiagram` neurons and endpoints; `UiDocument`, `UiDiagram`
   promoted; `stat` and `dashboard` with their tools (decision point 6); prompt fragments per
   module; the workspace agent allowlist derived from registrations.
5. **End-to-end tiers and CI.** All T1 scenarios, `flutter analyze`/`flutter test` in `ci.yml`,
   the T2 live tier and its job, the memory and docs updates (`CONTEXT.md` one sentence,
   `docs/ui/README.md`).

## 8. Risks and follow-ups

- **Persisted workspaces.** `WorkspaceStore.load()` must migrate `version 1` data (untyped
  artifact maps, legacy layout) once, and keep the "corrupt storage is preserved" behaviour.
- **Persisted chat snapshots.** `UiPartRef` keeps the `ui.kit-card` alias; the fixture fact covers
  the alias.
- **`graphic` semantics.** Charts render to a canvas; per-point semantics are synthetic nodes the
  view adds itself. This is also the accessibility story for charts.
- **T2 and test bindings.** `flutter test` blocks real sockets by default; the e2e tests reset
  `HttpOverrides`. The exact API is verified against current Flutter docs when phase 5 starts.
- **Follow-up, not in scope:** bridging `/agent` onto `ChatNeuron` so workspace conversations are
  durable neurons with journals and cards; a dedicated read-only ClickHouse user; multi-user
  identity for workspaces.
