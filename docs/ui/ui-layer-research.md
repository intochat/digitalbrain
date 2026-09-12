# UI layer research (2026-09-13, master at f8f3897d)

Inventory of the UI layer as it is, gathered before the UI-layer redesign. Findings only; the
design lives in `ui-layer-design.md`. Line counts are from the day of the audit.

Totals: C# Contracts 962 + Module 1,981 + Aspire.Hosting 1,050 = 3,993 lines across 101 files.
Dart: `core` 2,205 (tests 680), `ui` 8,939 (tests 793), `shell` 12,946 (tests 2,580).

## 1. C# UI module

### 1.1 Contracts (`DigitalBrain.Modules.UI.Contracts`)

RootNamespace is `DigitalBrain.UI`, but `Chat/` uses `DigitalBrain.Chat` (same assembly, two
namespaces). The project references AI.Contracts and the kernel Contracts and ships
`flutter-wire-contracts.golden.json`.

Neuron interfaces (all `: INeuron`):

| Interface | File | Methods | Grain type |
|---|---|---|---|
| `IChat` | `Chat/IChat.cs` | `Send`, `Cancel`, `ReadTranscript`, `ReadTurns`, `ReadTurn` | `uichat` |
| `ITable` | `Table/ITable.cs` | `Create`, `Update`, `Read`, `ReadOperation`, `ReadSummary` | `table` |
| `IChart` | `Chart/IChart.cs` | `Render`, `Append`, `Read` | `chart` |
| `IGraph` | `Graph/IGraph.cs` | `Render`, `Read` | `graph` |
| `IImage` | `Image/IImage.cs` | `Describe`, `Read` | `image` |
| `ISurface` | `Surface/ISurface.cs` | `Open`, `Activate`, `Read` | `surface` |
| `ITranscript` | `Transcript/ITranscript.cs` | `Append`, `Read` | `transcript` |
| `IWorkspaces` | `Workspaces/IWorkspaces.cs` | `Ensure`, `Find` | `workspaces` |
| `IActivities` | `Activities/IActivities.cs` | `Read` | `activities` |

Record families: Chat (`SendMessage`, `CancelTurn`, `ChatTurnSnapshot` with `Cards` and
`Context`, `Responded` carrying `IReadOnlyList<UiCardOffer>?`), Table (`ITable.cs` holds six
records plus the interface, `TableModels.cs` eight more), Chart (`RenderChart`, `AppendChartPoint`,
`ChartState` with `MaxPoints = 512`, `ChartPoint`), Graph, Image, Surface (`SurfaceState` also
carries activities and open receipts), Transcript, Workspaces, Activities.

Vocabulary: `UIVocabulary.cs` (9 grain types + ~19 signal names as string constants, "this
project ships no C# signal types"), `UiCardKinds` (`chart`, `image`, `spreadsheet`, `graph`,
`table`), `UiCardOffer(Kind, Name, Caption)` in `DigitalBrain.Chat`, `UiCard(Name, Title)` in
`DigitalBrain.UI` (the signal body the neurons announce; `UiCardOffer` is the snapshot shape).
`UIJson.cs` is the only `JsonSerializerContext` (66 entries).

Dead contracts: the whole Transcript family (`ITranscript`, `TranscriptNeuron`, `AppendTranscript`,
`TranscriptState`, `TranscriptEntry`, two vocabulary constants, three JSON entries) has no caller
outside its own files. `ChatTranscript`, `ChatTurn` and `ReadTranscript` are reachable only through
`/chats/{name}/transcript`, which production never maps (see 1.4).

### 1.2 Module (`DigitalBrain.Modules.UI`)

`UIModule.cs` registers `TableService`, the in-memory `ITableSource("table-")`, blob-or-memory
`IUiImageStore` and `ITurnContextBlobStore`, and contributes native tools through the AI seam:
`create_table`, `read_table`, `update_table_view`, `list_tables`, `render_chart`, `show_graph`,
and `generate_image` when an image model is configured. Each tool is built by instantiating the
whole tool class and picking one function by name (four instantiations for four table tools).

Neurons:

| Neuron | Lines | Handles | Announces |
|---|---|---|---|
| `ChatNeuron` | 284 | `Instruct`, `TurnRequested`, `Reply`, `Said`, `ChartRendered`, `GraphRendered`, `ImageDescribed`, `SheetChanged`, `TableRendered`, `TurnCancelling` | `Ask`, `TurnAccepted`, `CardOffered`, `Responded`, `TurnFailed`, `ActivityExecutionChanged` |
| `TableNeuron` | 84 | `TableCreating`, `TableUpdating` | none (`TableRendered` is fired by other modules) |
| `ChartNeuron` | 71 | `ChartRendering`, `ChartAppending` | `ChartRendered` + `UiCard` |
| `GraphNeuron` | 44 | `GraphRendering` | `GraphRendered` + `UiCard` |
| `ImageNeuron` | 45 | `ImageDescribing` | `ImageDescribed` + `UiCard` |
| `SurfaceNeuron` | 161 | `SurfaceOpening`, `SurfaceActivating`, `ActivityChanged` | `SurfaceOpened`, `ComponentAdded`, `ControlActivated`, `ControlRefused` |
| `WorkspacesNeuron` | 55 | `WorkspaceEnsuring` | none |
| `ActivitiesNeuron` | 53 | `ActivityExecutionChanged` | `ActivityChanged` |
| `TranscriptNeuron` | 42 | `TranscriptAppending` | none (dead) |

The responder is `DigitalBrain.AI.AgentNeuron`; `ChatNeuron.Responder` defaults to
`agent:<chat name>` unless an `Instruct` signal set `State.Agent`.

Table services: `TableService` (create through the `ui-table-catalog` neuron's synapses, read,
update, list via `ReadSummary`, longest-prefix source routing, `WaitAppliedAsync` polling 25 ms up
to 30 s), `TablePolicy` (internal; validation, filter and sort semantics, paging, the
15-significant-digit numeric rule), `ITableSource` (`chtable-` registered by ClickHouse),
`TableErrors` (four Orleans-serialisable exceptions), `TableAgentTools` (four `AIFunction`s with
long prompt descriptions; errors as `{kind:"tableError"}`).

`UiTools.cs` (296): `render_chart` (optional `chatName`, `kind:"chart"` JSON result), `show_graph`
and `generate_image` (required `chatName`, prose results). Tools connect the new neuron to the chat
before invoking render, then `WaitForCardAsync` polls `ReadTurns` every 25 ms up to 30 s.

Card offer flow: `ChartNeuron`/`GraphNeuron`/`ImageNeuron`/`ClickHouseTableNeuron`/Excel
`SpreadsheetNeuron` announce a rendered signal with a `UiCard` body along the synapse the tool
created; `ChatNeuron.ReceiveAsync` → `OfferAsync` maps the signal type to a `UiCardKinds` value
(unknown types fall into the `Spreadsheet` default arm), announces `CardOffered`, upserts the card on
the running turn by (kind, name); `SettleAsync` ships `Responded { Text, Cards }`.

### 1.3 HTTP endpoints live in the kernel, not the module

`src/Kernel/DigitalBrain.Silo/Http/`: `UiEndpoints.cs` (`/ui/charts|graphs|images|spreadsheets|
surfaces/{name}`, image content; imports `DigitalBrain.Excel` directly), `TableEndpoints.cs`
(`/ui/tables`), `ChatEndpoints.cs` (`/chats/{name}/send|turns|transcript|events`),
`ChatVoiceEndpoints.cs`, `SurfaceEndpoints.cs`, `ActivityEndpoints.cs`, `GraphEndpoints.cs`
(`/chats/{n}/brain`), `ConversationalAgentEndpoints.cs` (`POST /agent`, AG-UI),
`WorkspaceEndpoints.cs` (`/agent/capabilities`, Salesforce connection, transcribe,
`/workspace/artifacts*`).

### 1.4 The two chat paths

Path A, workspace: `POST /agent` → `ConversationalAgent` (a plain `ChatClientAgent`, in-memory
session store keyed by the client thread id). No neuron, no turn record, no journal, no cards. Its
tool list is a hard-coded name array (Salesforce four, ClickHouse three, `render_chart`; not
`show_graph`, not `generate_image`) plus the table tools and the workspace artifact tools. UI is
produced by kind-tagged JSON in tool results (`kind:"table"`, `kind:"chart"`, `kind:"tableError"`,
`kind:"artifactError"`) streamed as AG-UI `TOOL_CALL_RESULT` events. The 65-line system prompt mixes
Salesforce, ClickHouse, table, artifact, diagram and web-search policy.

Path B, uichat: `IChat.Send` → `ChatNeuron` → `Ask` to `AgentNeuron` with `Chat: uichat:<name>` in
the text (how the model learns `chatName`), reply → turn completes; UI is cards on the turn, and
tools block until the card lands.

Production gap: `Program.cs` maps `/agent`, workspace, `/ui/*`, brain observation, and (only under
`DigitalBrain:Graph:Enabled`) surfaces, activities, graph mutations and MCP. It never maps
`MapChatEndpoints()` or `MapChatVoiceEndpoints()`; only the test host `BrainHttp.cs` does, while the
Flutter core client still calls `/chats/{n}/send`, `/voice`, `/turns/{id}/cancel` and `/events`.
Path B is unreachable over HTTP in the shipped host.

### 1.5 Smells

- Dead: Transcript family; `ChatEndpoints.cs`/`ChatVoiceEndpoints.cs` (224 lines) unmapped;
  `ShellNames.cs:20` cites a `task-4-report.md` that does not exist.
- Duplicated: two 25 ms/30 s poll loops (`TableService.WaitAppliedAsync`, `UiTools.WaitForCardAsync`);
  four exception-to-result mappers (`TableAgentTools`, `TableEndpoints`, `WorkspaceEndpoints`,
  `WorkspaceAgentTools`); button lookup in `SurfaceNeuron.Components` and `SurfaceEndpoints.Find`
  (with a comment claiming the HTTP pre-check was deleted while it still exists); `SheetChanged`
  declared in `UIVocabulary` and Excel's `ExcelSignals`; the default table source registered in
  `UIModule` and again in `TableService`; `ShellHostingExtensions` re-exports all 16 `ShellNames`.
- Mixed responsibilities: `UiEndpoints` hard-codes Excel; `SurfaceNeuron` stores and prunes an
  activities feed that duplicates `ActivitiesNeuron`; `ChatNeuron.OfferAsync` silently defaults
  unknown card signals to `Spreadsheet`; `TableService` is public while its validator `TablePolicy`
  is internal and reached through `InternalsVisibleTo`.
- Inconsistent: two `ChatType` constants (`uichat` in UI, `chat` in AI) and two `ChatNeuron`
  classes; `UiCard` in `DigitalBrain.UI` next to `UiCardOffer`/`UiCardKinds` in `DigitalBrain.Chat`;
  memory stores public, blob stores internal; `render_chart` returns JSON while `show_graph` and
  `generate_image` return prose and require `chatName`; `TableSnapshot.Kind` hard-codes `"table"`;
  table constants sit above the grain-type header in `UIVocabulary`; file-per-record everywhere
  except the two table files; `TableNeuron` uses dense single-line bodies.
- Empty `/// <summary>` lines that restate the signature: `IChart.Read`, `IGraph.Read`,
  `IImage.Read`, `ITranscript.Read`, `IActivities.Read` (duplicated on `ReadActivities`),
  three on `IChat`, `IWorkspaces.Find` duplicated on `FindWorkspace`.
- No TODO/FIXME markers anywhere in the UI module or the silo HTTP folder.

### 1.6 Tests that touch the UI module

Reqnroll features: `ui.feature` (chart card round trip with delayed card delivery, chart without a
chat, duplicate append, workspace ensure, transient tool retry), `uichat.feature` (12 scenarios:
settlement retry, turns, `Instruct`, chart and table cards, cancel, context rules, activity
projection), `surface.feature`, `http.feature` (`/chats/desk/*`, `/surfaces/desk/*`, `/ui/charts`,
basic auth; runs against `BrainHttp`, which maps what `Program.cs` does not). Facts:
`TablePolicyFacts`, `TableNeuronFacts`, `TableAgentFacts` (agent + HTTP + `/agent` round trip with
revision conflict), `ClickHouseTableAgentFacts`, `ConversationalAgentFacts` (`/agent` streaming,
tool results, thread isolation), `FlutterWireGoldenFacts` (reflection diff against the golden, three
grandfathered exceptions), `TurnContextSpillFacts`, `TurnContextDigestRecipeFacts`, `WorkspaceFacts`.

Harness: `BrainSimulation` (Orleans in-process cluster, module manifest, volatile or file-backed
journals and grain storage, fault plans, silo restart), `ScriptedChatClient` (queue of
`Say`/`CallTool`/`Pause`/`TimeOut`, records every request), `DelayedUiCardFilter` (delays only the
card signals so the model's reply can race), `UiWait` (10 s / 50 ms poll).

Nothing asserts rendered UI: the deepest assertions are the `Responded` payload's cards, the tool
result JSON shape, an SSE `chat-turn` frame, and the reflection golden. The Dart route tests assert
the client calls `/chats/desk/send` and `/chats/desk/events`, routes production does not map; both
sides are green and nothing tests the seam between them.

## 2. Flutter kit (`digitalbrain_ui`) and core client (`digitalbrain_flutter`)

### 2.1 Kit layout

42 files, 8,939 lines; 6 test files, 793 lines; no README, example or changelog. Dependencies:
`forui` 0.26.0, `material_ui` 1.2.0, `gpt_markdown` 1.2.1, `flutter_svg` 2.3.0,
`flutter_chat_core` 2.9.0, `flutter_chat_ui` 2.11.1, `graphic` 2.7.0, `three_js` 0.3.0.
`markdraw` is a shell dependency only and forces the workspace overrides of
`freezed_annotation` and `re_editor`.

Files by concern: models (`ui_part.dart` 436), theme (`ui_theme.dart` 416: `UiPalette`, `UiType`,
`UiTheme`, `UiThemeScope`, `UiChatTheme`), chat (`ui_chat.dart` 46, `ui_chat_builders.dart` 678,
`ui_copyable_message.dart` 116, `ui_message_factory.dart` 65), components (button 36, card 40,
chart 115, clock 204, image 67, sheet 87, view 136, data table 619 + controller 116, explore 500,
project library 477, project templates 132, artifact dock 124), graph (models 72, layout 57,
camera 108, geometry 178, painter 271, scene 40, `three_graph_scene.dart` 398, `ui_graph.dart`
145, controller 175, navigator 142, view 224), gallery (screen 342, preview 843, not exported),
lumen (palette 20, controls 164, brain graph 537, `ino_presence.dart` 203, neuron icon 88),
onboarding (models 37, catalog 282, player 65, rail 98).

### 2.2 Component catalogue (interfaces as they are)

- `UiChart` (stateless): `part: UiChartPart`, `height = 200`. No controller, no reload, no
  error or empty callbacks; themed only by hard-coded `UiPalette`/`UiType`; key derived from the
  title. Builds the `graphic` `Chart` inline on every build.
- `UiDataTable` (stateless over `UiTableController`): `controller`, `onActivate`, `active`,
  `showTitle`, `toolbarActions`; themed from `Theme.of(context)`; private filter and column dialogs;
  `_parseFilterNumber` (30 lines of precision rules) and `_operatorLabels` live in the widget file
  while the operator vocabulary is generated in `core`.
- `UiTableController` (`ChangeNotifier`): `snapshot`, `accept` (monotonic on revision), `reload`,
  `change` with 409 reconciliation; `busy` and `error` are public mutable fields.
- `UiSheet`, `UiCard`, `UiImage`, `UiButton` (the only widget that installs `UiThemeScope`),
  `UiClock` (stateful, `DateTime.now()` in the tick, no injectable clock), `UiView`
  (kind-dispatched calculator/spreadsheet with no matching wire part).
- Chat: `UiChat` (wrapper over flyer `Chat`), `UiChatBuilders.customMessageBuilder` (exhaustive
  switch over the sealed `UiPart` hierarchy plus nine reader/callback parameters), five private
  fetch-in-`initState` ref loaders (~55 lines each, only the table one implements
  `didUpdateWidget`), `UiCopyableMessage`, `UiStreamCopy`, `UiMessageFactory`.
- Graph: `UiGraph` (2D projected), `UiGraphView` + `UiGraphController` (history, breadcrumbs,
  camera; `GraphSceneFactory` is a proper test seam that no test uses), `ThreeGraphScene`
  (`three_js`).
- Project: `UiExplore`, `UiSpecialist` (seven positional parameters) with the `uiSpecialists`
  catalogue, `UiProjectStartDialog`, `UiProjectLibrary`, `UiProjectCard`, `UiProjectNameDialog`,
  `UiProjectTemplates` (public, not exported), `UiArtifactDock`, `uiArtifactIcon`.
- Gallery: `UiGalleryScreen` forces `UiTheme.light()`; `GalleryPreview` holds fixtures for all 18
  entries in one `State` and dispatches on `entry.id` in a 390-line switch; its heading reads
  "UI Ui".
- Lumen: `LumenBrainGraph`, four Lumen controls, `UiMarkdown`, `InoPresence`, `NeuronIcon`.
- Onboarding: catalogue of eight hard-coded lessons, a timer-driven player, a rail.

### 2.3 Models

`UiPart` is a sealed class with ten `final` subtypes (`timer`, `button`, `chart`, `card`,
`chart-ref`, `image-ref`, `spreadsheet`, `spreadsheet-ref`, `graph-ref`, `table-ref`), each with
a `kindName`, `fromMetadata` and `toMetadata`; the five ref parts are 28-line copies differing only
in `kindName`. Serialisation is hand-written everywhere (no `build_runner`, `json_serializable` or
`freezed`), null-tolerant with defaults, so malformed payloads degrade silently.

`core` models: `table_models.dart` (`TableColumn` with derived `operators`, `TableRowData`,
`TableFilter`, `TableSort`, `TableSummary`, `TableSnapshot`, `TableViewUpdate`,
`TableRequestException`, typedefs `ReadTable`/`UpdateTableView`/`ListTables`), `brain_models.dart`
(`BrainSnapshot` computes correlations client-side in `fromJson`), `chat_models.dart` (offers,
`UiCardRef`, `ChatTurnEvent.replayTurns` with negative sequences as a sentinel, surface state; no
`toJson` anywhere), `execution_activity.dart`, `surface_models.dart`.

Duplicated definitions: chart series (`ChatChartOffer`/`ChatChartPoint` in core vs
`UiChartPart`/`UiChartPoint` in the kit, converted field by field), spreadsheet row coercion
(character-for-character identical in `chat_models.dart` and `ui_part.dart`), graph nodes/edges
(core vs kit, mapped by hand), ref cards (`UiCardRef` vs five ref parts, mapped in the shell),
reader typedefs (kit vs shell `chat_contracts.dart`), design tokens (kit vs shell `brain_theme.dart`
aliases), chart widgets (`UiLineChart`, `UiTimeChart`, `UiChartCard` defined in the shell's demos).

### 2.4 Core client

`DigitalBrainUiClient` (851 lines, 30 public methods, every path a literal): auth check,
transcribe, Salesforce connection, capabilities, workspace artifacts (untyped maps), tables
(typed), `runAgent` (`POST /agent`, AG-UI `RunAgentInput`, `Stream<AgentEvent>`), activities,
brain (`readBrain`, `watchBrain`, `setBrainSubscription`), surfaces, chat (`sendMessage`,
`sendVoice`, `cancelTurn`, `watchChatTurns`), and six `/ui/*` entity readers. `AgentEvent` is an
untyped map wrapper; `decodeAgentEvents` is its own SSE decoder. Only `_watchEvents` has a resume
cursor and backoff; `watchActivities` and `watchBrain` parse SSE inline (two duplicated loops) and
never reconnect. Four HTTP paths with three failure models (`TableRequestException`, `StateError`,
`null` for 404). `CookieHttpClient` injects Basic auth and captures cookies.
`DigitalBrainHostEnv` resolves `DIGITALBRAIN_UI_BASE`, `DIGITALBRAIN_SHELL`, `DIGITALBRAIN_CHAT`
from defines, process env or same origin (gated on `dart.library.html`, false under Wasm).

Dead in core: `readChart`, `readGraph`, `readSpreadsheet`, `readImage`, `readImageBytes`,
`readSurface`, `watchSurfaceEvents`, `watchActivities`, `workspaceCapabilities`,
`setBrainSubscription`, `openSurface` (no caller in ui, shell or tests; the shell threads matching
typedefs through five widget layers but `main.dart` never supplies them, so every ref loader in
production takes its offline branch); `web_socket_channel` never imported; `executables:
digitalbrain_host:` with no `bin/`.

### 2.5 Smells

- God files: gallery preview 843, `ui_client.dart` 851, chat builders 678, data table 619, lumen
  brain graph 537, explore 500 (eight unrelated public symbols), project library 477, `ui_part.dart`
  436, theme 416 (five concerns).
- Two incompatible theming conventions: `UiChart`, `UiCard`, `UiImage`, `UiSheet`, `UiView`,
  `UiClock`, `UiCopyableMessage`, `UiGraphNavigator`, painters and the onboarding rail ignore
  `Theme.of(context)` and use the dark-only `UiPalette`/`UiType`; `UiDataTable`, `UiArtifactDock`,
  `UiExplore`, `UiProjectLibrary` use `Theme.of(context)`. `LumenPalette` is a light-only third
  palette, and `UiTheme.workspace()` adds ~20 inline hex literals that live in no palette class.
  `ui_theme.dart` imports both `package:flutter/material.dart` and `package:material_ui` (pulled in
  by forui), so `UiThemeScope` installs a `material_ui` `Theme` that SDK Material widgets do not see.
- Missing abstractions: no chart controller (a chart ref can never refresh), two places to register
  a part kind (`UiPart.tryParse` and the builder switch), no shared ref loader, no typed AG-UI
  events, no result/error type.
- Dead: `UiSpecialistSuggestions`, `UiEditorFrame`, `UiChatBuilders.uiCustoms`,
  `UiMessageFactory.customMessageForPart`, `UiView`.
- Exports: core exports transport internals (`CookieHttpClient`, `SseFrameParser`); the kit exports
  `graph_camera`/`graph_layout` but not `graph_geometry`, `graph_painter`, `three_graph_scene`,
  `ui_project_templates`, `ui_gallery_preview` (a test imports it through `src/`);
  `lumen_brain_graph.dart` imports its own package barrel.
- Naming: `class` vs `final class` at random; four prefix families (`Ui*`, `Lumen*`,
  `Onboarding*`, bare `Graph*`); `spreadsheet` vs `UiSheet` vs `ChatSpreadsheetOffer`; the product
  name spelled `IntoCaht` and `IntoChat`; `GalleryEntry` takes nine positional parameters.
- Correctness: `SseFrameParser.addLine` overwrites instead of appending multi-line `data:` frames
  (the one shared abstraction has the bug; the three inline decoders accumulate correctly);
  `TableSnapshot.toJson` emits a `kind` that `fromJson` never reads; `UiSheetPart` accepts two row
  shapes and emits one; widget keys derived from titles collide; `analysis_options.yaml` excludes
  copied from an app template.
- Not testable in isolation: `UiClock` (`DateTime.now()`), private ref loaders, inlined gallery
  fixtures, `OnboardingLessonPlayer` (real `Timer`), `UiChart` (inline `graphic` config).

### 2.6 Tests

Kit: six files (data table 307 with conflict reload and controller monotonicity, table ref card
164 incl. paging across parent rebuilds, project library 154, chart 69 mark types only, explore 62,
gallery table 37). No goldens, no accessibility assertions, nothing for card, sheet, image,
button, clock, view, chat builders beyond the table ref, `UiPart.tryParse` for the other nine kinds,
the graph family, themes, Lumen, onboarding, gallery filtering.

Core: five files (client routes 189, chat stream parser 173, agent stream 148, table client 113,
workspace client 57). Nothing for `CookieHttpClient`, `DigitalBrainHostEnv`, `checkAuth`,
`watchBrain`/`watchActivities`, brain model parsing and `BrainCorrelation.group`, the `/ui/*`
readers, the 40 s table timeout, or multi-line `data:` frames through `SseFrameParser`.

## 3. Flutter shell (`digitalbrain_flutter_shell`)

46 files, 12,946 lines under `lib/`; 17 test files, 2,580 lines, 45 cases. Dependencies:
`digitalbrain_flutter` (core), `digitalbrain_ui`, `flutter_chat_ui`, `flutter_chat_core`,
`flyer_chat_text_message`, `flyer_chat_text_stream_message`, `gpt_markdown`, `uuid`, `provider`,
`web`, `graphic`, `record`, `path_provider`, `http`, `url_launcher`, `flutter_svg` (0 usages),
`shared_preferences`, `file_picker`, `markdraw`, `archive`, `xml`.

### 3.1 Layout

`main.dart` (68) resolves the chat name, wraps `BrainSessionGate`, and `buildShell()` wires 15
`DigitalBrainUiClient` methods into `WorkspaceApp` as loose callbacks.

`workspace/` (4,674 lines, the production path): `workspace_app.dart` (1,312; store, table
controller cache, live brain graph, save timers, routes, directory, artifact acceptance, five
dialogs, window chrome, `MaterialApp`, theming), `workspace_store.dart` (654; seven model classes,
persistence, 22 mutators), `artifact_editors.dart` (631; kind switch plus diagram, image and brain
editors), `workspace_chat_presentation.dart` (526; `part of workspace_chat.dart`),
`workspace_desktop.dart` (453; free-window canvas), `workspace_settings.dart` (392),
`workspace_chat.dart` (360; AG-UI run loop, transcript entries, attachment prompt assembly,
Salesforce polling), `workspace_table_import.dart` (260), `workspace_voice.dart` (217),
`workspace_routes.dart` (69), `workspace_brain_observation.dart` (67).

`chat/` (5,505 lines, the uichat path plus a legacy AG-UI UI): `graph_home_screen.dart` (1,085;
24 constructor parameters, 27 state fields) with its part `activity_home.dart` (736),
`agent_chat_app.dart` (724; a second complete AG-UI conversation UI, instantiated only by tests,
imported by production for the six-line `AgentRunner` typedef), `brain_workspace.dart` (546; 24
fields; router, surface reader and settings page at once), `brain_chat_screen.dart` (467; 21
parameters) with part `brain_chat_presentation.dart` (398), `graph_examples_screen.dart` (356),
`workspace_chrome.dart` (335), `brain_chat_composer.dart` (270), `brain_graph_simulation.dart`
(248), `brain_graph_store.dart` (184; shared by both paths), `activity_projection.dart` (123),
`workspace_session.dart` (70), `chat_contracts.dart` (44; ten callback typedefs and the
`UiCardOffer.kind → UiPart` mapping), `brain_chat_app.dart` (96), `stream_state_store.dart` (24),
voice file helpers.

`auth/` (330), `windowing/` (903: a demo desk canvas and a `PanelManager` that solves the same
problem as `WorkspacePresentation` + `WorkspaceDesktop` with a separate model), `demos/` (459),
`onboarding/` (139), `integrations/` (33), `activity_screen.dart` (254), `brain_theme.dart` (45),
`chat_screen.dart` (4, dead barrel).

### 3.2 State flow on the production path

`WorkspaceStore` (`ChangeNotifier`) holds `projects → conversations + artifacts + presentation`.
`WorkspaceArtifact` has mutable `title`/`kind`/`content` and two untyped bags: `data`
(payload plus private flags) and `editorState` (aliased `viewState`). `WorkspaceConversation.messages`
is `List<Map<String, dynamic>>`. Every mutator calls `save()` (22 sites), which serialises the whole
workspace to `SharedPreferencesAsync`; a window drag writes the entire JSON per frame.

`WorkspaceApp._accept` is the artifact ingestion funnel: it branches on `kind == 'table'` to build a
`UiTableController` whose listener rewrites `data` and `editorState` for every artifact with that id
across every project and saves; wraps the result into a `WorkspaceArtifact` with `_revision` and
`_remote` flags; merges by revision unless `_dirty`; and always opens the artifact (a background
tool result can steal the active window). `_open` re-reads from the backend unless the kind is
served by its result, `_live` or `_dirty`. `_editArtifact` sets `_dirty` and debounces
`_saveArtifact` (600 ms), which strips `_`-prefixed keys, fingerprints with `jsonEncode`, and calls
`onUpdateArtifact(expectedRevision:)`. `_liveGraph` registers `_publishLiveObservation`, which
walks all projects and artifacts on every graph notification and saves. `_workspace` mounts one
`WorkspaceChat` per conversation of every project in an `IndexedStack`; each mounts a 3-second
`Timer.periodic` polling Salesforce login regardless of need. `_routes.sync` runs inside
`MaterialApp.builder` and `_editor` schedules a table hydration during build.

`WorkspaceChat._send` mints thread and run ids, assembles an attachment context block with
`syncState`, `currentObservation`, `localDraft` and five sentences of embedded policy text, and
subscribes to `onRun`. `_event` handles `RUN_*`, `TEXT_MESSAGE_*` and `TOOL_CALL_*`; on
`TOOL_CALL_RESULT` it json-decodes `content` and, when `kind` is in the six-kind allow-list, calls
`onArtifact`. Transcript entries become `CustomMessage(metadata: entry)` for `flutter_chat_ui`, with
`composerBuilder`, `emptyChatListBuilder` and `customMessageBuilder` all overridden, so only the
list plumbing of `UiChat` is reused.

`WorkspaceArtifactEditor.build` switches on `kind`: `table` → `UiDataTable`, `chart` → `UiChart`,
`diagram` → markdraw, `image` → image editor, `brain` → `LumenBrainGraph`, default → "cannot be
edited yet". `document`, an accepted tool-result kind, has no editor.

The import graph is a DAG; the `part` files (`workspace_chat_presentation`,
`brain_chat_presentation`, `activity_home`) reach into another file's private `State`, so those
pairs are inseparable while presenting as two files.

### 3.3 Kind dispatch and flag reads (refactoring targets)

Kind switches: `workspace_app.dart` `_servedByResult` (251), `_accept` (258, 304, 309, 314),
`_editArtifact` (352, 359), `_open` (442, 456), new-work menu (497–592, including a pseudo-kind
`liveBrain` that is stored as `brain`), summaries (620–656), `_icon` (719–726), `_editor` (745);
`artifact_editors.dart` (37–88); `workspace_chat.dart` (197, 314–322);
`workspace_chat_presentation.dart` (75–76, 141–148 the same six-kind list, 160–167 a third icon
switch, 188–194 tool-name dispatch); `workspace_desktop.dart` (308, `uiArtifactIcon` from the kit,
the fourth icon switch); `workspace_store.dart` (105); `agent_chat_app.dart` (222, 318, 353);
`chat_contracts.dart` (33–42, the card vocabulary `chart|image|spreadsheet|graph|table`, disjoint
from the artifact vocabulary `table|chart|diagram|brain|image|document`); `panel_manager.dart`
(41–59, 109–117) and `windowing_screen.dart` (396) for `WindowPanelKind`.

Flag reads and writes: `workspace_app.dart` has 30 sites for `_live`, `_dirty`, `_remote`,
`_revision`, `_observationStale`, `_observationFailure`; `workspace_chat.dart` 7; `artifact_editors.dart`
reads the public `live` key while the other files read `_live`; `_revision` and `revision` coexist
with a fallback between them; the `_`-prefix contract is enforced by three separate
`removeWhere(key.startsWith('_'))` calls.

### 3.4 Duplication across the paths

AG-UI run loop (`workspace_chat.dart` 170–341 vs `agent_chat_app.dart` 113–251); transcript entry
model (untyped map vs typed `_Entry`); table controller cache; tool result tile; markdown rendering
(three places); URL safety guard (twice); composer (three); voice capture (two); window management
(`WorkspacePresentation` + `WorkspaceDesktop` vs `PanelManager` + `windowing_screen`); brain graph
host (`_BrainEditor` vs `GraphHomeScreen`); agent catalogue (`workspaceAgents` in the shell vs
`uiSpecialists` in the kit, same four ids, two description strings, validated against one and
rendered from the other); kind→icon (four copies); gallery route (three); integrations list (two).

### 3.5 Dead code

`chat_screen.dart`, `demos/shell_gallery.dart`, `demos/shell_chat_demo.dart` (no importers);
`AgentChatApp` (706 of 724 lines test-only); the whole `BrainChatApp`/`BrainWorkspace` subtree
(about 6,300 lines: brain chat, graph home, activity home, examples, simulation, projection, chrome,
session, stream state, activity screen, onboarding screen, windowing screen, panel manager, chart
demo) unreachable from `main.dart` and reached only by three tests; the `_scriptedHomeEnabled`
toggle in `brain_workspace.dart` (199 unreachable lines by default); `WorkspacePresentation.layout`
(legacy); two declared assets referenced nowhere; `flutter_svg`; the `document` kind; the image
editor's `data['brightness']` fallback that nothing writes.

### 3.6 Other smells

Untyped `Map<String, dynamic>`: 57 occurrences (`workspace_store.dart` 28, `workspace_app.dart` 13,
`workspace_chat.dart` 8); the artifact wire format is a raw map on both sides of
`onCreateArtifact`/`onReadArtifact`/`onUpdateArtifact`; `workspaceBrainObservation` builds a
60-line map that `BrainSnapshot.fromJson` re-parses.

Magic strings: the persistence key three times, the agent id `intocaht` five times, window mode
strings in three files, layout strings, sync-state vocabulary asserted by substring in tests, route
strings, tool names, Salesforce status strings, embedded prompt paragraphs asserted by substring;
two different clamp ranges for the chat width (280–700 on load, 300–60 % at render); six different
breakpoints.

Behaviour: silent `catch (_) {}` in seven places; `_query` is a `State` field used only inside a
dialog's `StatefulBuilder`; `WorkspaceApp.persistenceKey` is ignored when a store is injected;
`WorkspaceChat.project` is nullable with a fallback while the app always passes it.

### 3.7 Tests

Tests drive `WorkspaceApp` with counting closures for each callback, an in-memory persistence fake
(ten copies of the same six-line class under six names), and `StreamController<AgentEvent>` behind
`onRun`. `agent_table_test.dart` pushes a `TOOL_CALL_RESULT` through the legacy `AgentChatApp` and
asserts a rendered `UiDataTable`; on the production path no test crosses the seam: every artifact
test calls `WorkspaceChat.onArtifact` directly, so the six-kind allow-list, the string `jsonDecode`
and the hand-off into `_accept` are covered only indirectly. Untested: the `document` kind, save
conflict resolution and its banner, import, search, and the card mapping in `chat_contracts.dart`.

## 4. Test infrastructure and the end-to-end gap

### 4.1 C# side

One test project, `tests/DigitalBrain.Tests` (xunit v3 with the Microsoft Testing Platform
runner, Reqnroll 3.3.4 with generated `*.feature.cs` checked in, `Microsoft.AspNetCore.TestHost`,
`FakeTimeProvider`, `Testcontainers.ClickHouse`). `Aspire.Hosting.Testing` 13.5.3 is pinned in
`Directory.Packages.props` but referenced by no project; there is no
`DistributedApplicationTestingBuilder` anywhere.

`BrainSimulation` boots the silo in-process with Orleans `InProcessTestClusterBuilder(1)`;
`BrainWorld` is the scenario context (clock, fault plan, fixtures, `ScriptedChatClient`, card
delay); `BrainSteps` composes Memory, Time and Excel modules plus fixtures. `BrainHttp` builds a
slim `WebApplication` with `UseTestServer()` over the simulation's grains and maps the basic-auth
gate, session neuron, chat, chat voice, surface, ui, activity and graph endpoints; it does not map
`/agent`. `TableAgentFacts.StartAsync` is the only place a real silo and the `/agent` SSE endpoint
are combined, and nothing there exercises `render_chart`. `ConversationalAgentFacts` posts the full
AG-UI envelope to `/agent` and asserts event types.

26 feature files hold 156 scenarios (157 cases); 19 fact classes hold 55 cases. A chat scenario
sends through `IChat.Send`, waits on the observer's incoming journal and asserts
`Assert.Contains(new UiCardOffer(kind, name, title), response.Cards)`; the deepest UI assertion
reads the chart neuron's snapshot. No C# test asserts a rendered widget.

`flutter-wire-contracts.golden.json` pins 15 C# DTO types (aliases, property names, simple type
names) by reflection. It never touches Dart and says nothing about AG-UI event payloads or the
`render_chart`/`show_query_table` tool-result JSON, which are produced as anonymous objects in
`UiTools.cs` and `ClickHouseNativeTools.cs` and consumed by literal keys in
`workspace_chat.dart`; nothing compares the two.

`DigitalBrainFakes` is a configuration gate (`DigitalBrain:Fakes:Enabled`) consumed by the
ClickHouse, Google, GitHub, Microsoft and Salesforce modules and by Aspire's
`WithDigitalBrainFakes`. The only running-kernel check is the deploy workflow's Docker smoke test,
which polls `/health` and nothing else.

### 4.2 Flutter side

28 test files, about 105 cases. No `integration_test/` folder, no `integration_test` dependency,
no image goldens. `core/test` covers the HTTP seam with hand-rolled `http.BaseClient` fakes and
`package:http/testing.dart` `MockClient` (including `MockClient.streaming` for SSE): the AG-UI
decoder over synthetic SSE bytes, `/chats/desk/*` routes, tables, workspace client, the chat
stream parser. `shell/test` drives `AgentChatApp`/`WorkspaceApp` with a
`StreamController<AgentEvent>` behind the `onRun` callback, and boots the full shell through
`buildShell(chat:, edge: DigitalBrainUiClient(httpClient: NoRequests()))`. The chart tests added on
2026-09-13 call `WorkspaceChat.onArtifact` directly, bypassing the `TOOL_CALL_RESULT` hop.
`ui/test/ui_chart_test.dart` asserts on the `graphic` package's `Chart` marks; there is no per-bar
widget to count, so "three bars" is only reachable as `part.points.length == 3` or the
`IntervalMark` data.

### 4.3 CI

`ci.yml` runs `dotnet format whitespace --verify-no-changes`, `dotnet build -c Release`,
`dotnet test`, then `dart format --set-exit-if-changed` and `flutter build web --release`. It never
runs `flutter analyze` or `flutter test`; Dart tests run only in `deploy.yml` on a published
release, so a Flutter regression cannot fail a pull request. `DIGITALBRAIN_CLICKHOUSE_TESTS=1`
is the only env gate and is set in neither workflow.

### 4.4 What is missing for "send a message, see a chart with three bars"

- A kernel that Dart tests can drive: nothing spawns or attaches a kernel from Dart.
- A shared schema for the AG-UI event envelope and the kind-tagged tool results that both sides
  validate against.
- A captured-SSE fixture format so a C# test can record what `/agent` streams and a Dart test can
  replay it through the real `DigitalBrainUiClient` → `decodeAgentEvents` → `WorkspaceChat` →
  artifact → `UiChart` path.
- `flutter test` and `flutter analyze` in `ci.yml`.
- Any test of `render_chart` through `/agent` (the C# half is one small test away with
  `ScriptedChatClient.CallTool("render_chart", …)` over `TableAgentFacts.StartAsync`-style hosting).

Seams that already exist and would carry such a test: `buildShell(...)` marked
`@visibleForTesting`, `DigitalBrainUiClient(httpClient:)`, `WorkspaceApp(onRun:, onReadTable:,
onReadArtifact:, …)`, the `TOOL_CALL_RESULT` branch in `workspace_chat.dart`, and
`UiChart.part.points`.

### 4.5 Docs

`docs/` holds only `docs/clickhouse/`. `CONTEXT.md` links to `docs/GETTING_STARTED.md` and
`docs/programmable-behaviors-implementation.md`, both deleted by commit `0e8ef906` together with
`ARCHITECTURE.md` and about twenty other documents. The ClickHouse plan is the only written
statement of the testing strategy (no Docker in unit tests, fakes behind `DigitalBrainFakes`, the
four CI commands).
