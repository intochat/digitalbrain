# UI phase 0: remove what is unreachable — implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Delete every UI-layer path that production cannot reach, so the contract work in phase 1 starts from a code base where each remaining file is used.

**Architecture:** The rule is reachability: Dart code that `shell/lib/main.dart` cannot reach and C# routes that `src/Kernel/DigitalBrain.Silo/Program.cs` does not map are deleted, not refactored. Kernel neurons with scenarios stay. Tests that only covered deleted code go with it; two valuable legacy tests are ported onto the production path (`WorkspaceApp`), and the ten copies of the workspace persistence fake become one file.

**Tech Stack:** .NET 11 / Orleans 10.3.1 / xunit v3 + Reqnroll 3.3.4; Flutter 3.47.2 workspace (`core`, `ui`, `shell`) with `flutter_test`, `package:http` `MockClient`/`BaseClient`.

**Spec:** `docs/ui/ui-layer-design.md` (sections 2, 4.5, 5, 7 phase 0) with evidence in `docs/ui/ui-layer-research.md`. Decision points 1, 4, 5 and 8 apply with their defaults.

## Global Constraints

- Branch `feature/ui-phase0-remove-unreachable` (created from `feature/ui-layer-redesign`). Commit prefix `ui:`. End every commit message with `Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>`.
- Line numbers below are as of commit `c6a2ca73`; verify with `grep -n` before editing, the file may have shifted after an earlier task.
- Gates after every task (run the ones that touch the changed side; task 8 runs all):
  - C#: `dotnet format whitespace DigitalBrain.slnx --verify-no-changes`, `dotnet build DigitalBrain.slnx -c Release`, `dotnet test DigitalBrain.slnx -c Release --no-build` (expect 4 Docker-gated skips).
  - Dart, from `src/Modules/UI/Flutter`: `dart format --output=none --set-exit-if-changed core ui shell`; per package `flutter analyze` (core: `dart analyze`); `dart test` in `core`, `flutter test` in `ui` and `shell`; from `shell`: `MSYS_NO_PATHCONV=1 flutter build web --release --base-href "/"` (Git Bash needs the env var).
- No `/// <summary>` that restates a signature; no new comments except one line where the reason is not visible in the code.
- Never add a package. Removing packages requires `flutter pub get` at `src/Modules/UI/Flutter` afterwards; commit the updated `pubspec.lock` (CI uses `--enforce-lockfile`).
- Before writing any new Dart test code, look up the APIs used (`flutter_test` `WidgetTester.enterText`, `pumpAndSettle`, `find.byTooltip`, `package:http` `BaseClient`) with Context7 (`resolve-library-id` then `query-docs`); the plan's code was written against the current versions but the rule is absolute.
- Do not touch `ISurface`, `IActivities`, `SurfaceEndpoints`, `ActivityEndpoints`, `GraphEndpoints` or `surface.feature` (decision point 2).
- `IChat.ReadTranscript`, `ChatTranscript`, `ChatTurn` and `ReadTranscript` stay: `UiChatSteps.cs:102,107` use them.

---

### Task 1: Delete the dead Transcript neuron family (C#)

**Files:**
- Delete: `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Transcript/ITranscript.cs`, `.../Transcript/AppendTranscript.cs`, `.../Transcript/TranscriptEntry.cs`, `.../Transcript/TranscriptState.cs`, `src/Modules/UI/DigitalBrain.Modules.UI/Transcript/TranscriptNeuron.cs`
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/UIVocabulary.cs:24` and `:104-105`, `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/UIJson.cs:56-57,71`

**Interfaces:**
- Consumes: nothing.
- Produces: nothing; `UIVocabulary` loses `TranscriptType` and `TranscriptAppending`.

- [ ] **Step 1: Prove nothing else references the family**

Run from the repo root:
```bash
grep -rn "ITranscript\|TranscriptNeuron\|AppendTranscript\|TranscriptState\|TranscriptEntry\|TranscriptType\|TranscriptAppending" --include="*.cs" --include="*.feature" src tests | grep -v "/bin/\|/obj/\|\.feature\.cs"
```
Expected: only lines inside the five files to delete plus `UIVocabulary.cs:24,104,105` and `UIJson.cs:56,57,71`. If anything else appears, stop and report.

- [ ] **Step 2: Delete the files**

```bash
git rm src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Transcript/ITranscript.cs src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Transcript/AppendTranscript.cs src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Transcript/TranscriptEntry.cs src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Transcript/TranscriptState.cs src/Modules/UI/DigitalBrain.Modules.UI/Transcript/TranscriptNeuron.cs
```

- [ ] **Step 3: Remove the vocabulary constants and JSON entries**

In `UIVocabulary.cs` delete the line `public const string TranscriptType = "transcript";` (and the blank line after it) and the two lines
```csharp
    // { ...AppendTranscript... }
    public const string TranscriptAppending = "TranscriptAppending";
```
(and the blank line after them). In `UIJson.cs` delete the three attribute lines
```csharp
[JsonSerializable(typeof(TranscriptState))]
[JsonSerializable(typeof(TranscriptEntry))]
[JsonSerializable(typeof(AppendTranscript))]
```

- [ ] **Step 4: Build and test**

```bash
dotnet format whitespace DigitalBrain.slnx --verify-no-changes && dotnet build DigitalBrain.slnx -c Release && dotnet test DigitalBrain.slnx -c Release --no-build
```
Expected: build 0 warnings 0 errors; test summary `failed: 0`, 4 skipped.

- [ ] **Step 5: Commit**

```bash
git commit -am "ui: delete the unreachable Transcript neuron family" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 2: Delete the unmapped chat HTTP routes (C#)

**Files:**
- Delete: `src/Kernel/DigitalBrain.Silo/Http/ChatEndpoints.cs`, `src/Kernel/DigitalBrain.Silo/Http/ChatVoiceEndpoints.cs`
- Modify: `tests/DigitalBrain.Tests/Features/BrainHttp.cs:52-62`, `tests/DigitalBrain.Tests/Features/http.feature`, `tests/DigitalBrain.Tests/Features/HttpSteps.cs`
- Regenerate: `tests/DigitalBrain.Tests/Features/http.feature.cs` (Reqnroll regenerates it on build; commit the result)

**Interfaces:**
- Consumes: nothing.
- Produces: `http.feature` keeps four scenarios (surface stream, surface not-found, chart read, auth gate).

- [ ] **Step 1: Prove the routes are unmapped in production and unused elsewhere**

```bash
grep -rn "MapChatEndpoints\|MapChatVoiceEndpoints\|ChatEndpoints\.\|ChatVoiceEndpoints\." --include="*.cs" src tests | grep -v "/bin/\|/obj/"
```
Expected: definitions in the two files and the two calls in `BrainHttp.cs:61-62`. Nothing in `Program.cs`.

- [ ] **Step 2: Delete the endpoint files and the test-host mappings**

```bash
git rm src/Kernel/DigitalBrain.Silo/Http/ChatEndpoints.cs src/Kernel/DigitalBrain.Silo/Http/ChatVoiceEndpoints.cs
```
In `BrainHttp.cs` delete the lines `app.MapChatEndpoints();` and `app.MapChatVoiceEndpoints();` and the block that only the voice route needed:
```csharp
        if (brain.SiloServices.GetService<IAudioTranscriptionService>() is { } transcription)
        {
            builder.Services.AddSingleton(transcription);
        }
```
Remove the `using` that becomes unused (the compiler reports it as IDE0005 if the build treats it as an error; otherwise `dotnet format` keeps it, delete it anyway).

- [ ] **Step 3: Rewrite `http.feature`**

Replace the whole file with:
```gherkin
Feature: http
  The silo's HTTP edge is a thin adapter: every endpoint is one typed neuron call or one journal read.

  Scenario: A control activation reaches the surface stream before the next poll
    Given a running brain with AI and UI behind HTTP
    And surface "desk" has opened "home" with button "confirm"
    When the stream "/surfaces/desk/events" is opened
    And POST "/surfaces/desk/controls/confirm/activate" with {"surfaceKey":"home","intent":"do"}
    Then the stream carries a "surface" saying "ControlActivated" within 10 seconds

  Scenario: A ui read is not found before the chart exists and found after
    Given a running brain with AI and UI behind HTTP
    When GET "/ui/charts/sales"
    Then the response status is 404
    When chart "sales" renders "Quarterly sales"
    And GET "/ui/charts/sales"
    Then the response status is 200
    And the response body has "title" of "Quarterly sales"

  Scenario: Activating a button that is not on the surface is not found
    Given a running brain with AI and UI behind HTTP
    And surface "desk" has opened "home" with button "confirm"
    When POST "/surfaces/desk/controls/ghost/activate" with {"surfaceKey":"home","intent":"do"}
    Then the response status is 404
    When POST "/surfaces/desk/controls/confirm/activate" with {"surfaceKey":"home","intent":"do"}
    Then the response status is 202

  Scenario: The gate refuses a request that carries no credentials
    Given a running brain with AI and UI behind HTTP requiring "owner" and "secret"
    And chart "sales" renders "Quarterly sales"
    When GET "/ui/charts/sales"
    Then the response status is 401
    When GET "/ui/charts/sales" as "owner" with "secret"
    Then the response status is 200
    And GET "/auth/check" as "owner" with "secret" returns 204
```

- [ ] **Step 4: Remove the step bindings only the deleted scenarios used**

In `HttpSteps.cs` delete these methods with their attributes: `Cancel` (`POST the cancel of the accepted work on chat`), `Transcript` (`GET ... has user text`), `Turn` (`GET ... reports the accepted work as`), `WaitForTurn` (`the accepted work on chat ... becomes ... within`), `ReadStream` (`the stream ... is read up to ... seconds`), `StreamCarriesStatus` (`the stream carries a ... with status ...`), `StreamText` (`the stream reset carries user text`). Delete the private helpers only they used: `HasTurn`, `AssertUserText` (search for them; they are below line 95), the fields `_work` and `_reset`, and in `Post` the block that parses `body["work"]` into `_work`:
```csharp
        var mediaType = _response!.Content.Headers.ContentType?.MediaType;
        if (string.Equals(mediaType, "application/json", StringComparison.OrdinalIgnoreCase)
            || mediaType?.EndsWith("+json", StringComparison.OrdinalIgnoreCase) == true)
        {
            if (JsonNode.Parse(_body) is JsonObject body && body["work"] is { } work)
            {
                _work = work.Deserialize<SignalId>(JsonSerializerOptions.Web);
            }
        }
```
Keep `Start`, `StartWithCredentials`, `Post`, `Get`, `GetAuthenticated`, `Status`, `BodyProperty`, `AuthenticatedStatus`, `OpenStream`, `StreamCarries`, `WaitForFrame`, `CollectFrames`, `ReadFrames`, `RenderChart`, `OpenSurface`, `Remember`, `DisposeAsync`. Remove `using DigitalBrain.Abstractions.Identity;` if `SignalId` no longer appears in the file.

- [ ] **Step 5: Build and test**

```bash
dotnet format whitespace DigitalBrain.slnx --verify-no-changes && dotnet build DigitalBrain.slnx -c Release && dotnet test DigitalBrain.slnx -c Release --no-build
```
Expected: 0 errors; the four `http.feature` scenarios pass; total scenarios drop by six. If Reqnroll reports an unbound step, the feature text and a kept binding disagree; fix the text, never re-add a deleted binding.

- [ ] **Step 6: Commit**

```bash
git add -A src/Kernel/DigitalBrain.Silo/Http tests/DigitalBrain.Tests/Features
git commit -m "ui: delete the chat HTTP routes production never mapped" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 3: Small C# cleanups the audit found (module and hosting)

**Files:**
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI/UIModule.cs:18`, `src/Modules/UI/DigitalBrain.Modules.UI.Aspire.Hosting/ShellHostingExtensions.cs:13-27`, `src/Modules/UI/DigitalBrain.Modules.UI.Aspire.Hosting/FlutterHostOptions.cs`, `src/Modules/UI/DigitalBrain.Modules.UI.Aspire.Hosting/FlutterHostLaunch.cs:70,91,95,102,135,137`, `src/Modules/UI/DigitalBrain.Modules.UI.Aspire.Hosting/ShellNames.cs:17-20`

**Interfaces:**
- Produces: `ShellNames` is the only home of the shell constants; `ShellHostingExtensions` keeps its methods only.

- [ ] **Step 1: Drop the duplicate in-memory table source registration**

`TableService.cs:10-15` already appends `InMemorySource("table-", "table")` to the injected sources. In `UIModule.cs` delete the line
```csharp
        builder.Services.AddSingleton<ITableSource>(new TableSource("table-", UIVocabulary.TableType));
```

- [ ] **Step 2: Replace the constant re-exports with `ShellNames`**

In `ShellHostingExtensions.cs` delete the fifteen `public const string ... = ShellNames....;` lines (13–27). In `FlutterHostOptions.cs` replace `ShellHostingExtensions.` with `ShellNames.` in the four initialisers. In `FlutterHostLaunch.cs` replace `ShellHostingExtensions.DefaultDeviceTarget`, `.HeadlessHostEntry`, `.DefaultWebDeviceTarget` with the `ShellNames.` spelling (lines 70, 91, 95, 102, 135, 137); leave the `nameof(ShellHostingExtensions.WithHeadlessHost)` and `nameof(ShellHostingExtensions.WithWindowHost)` uses as they are (those are methods). Then check nothing outside the assembly used the constants:
```bash
grep -rn "ShellHostingExtensions\.[A-Z][A-Za-z]*\b" --include="*.cs" src tests | grep -v "/bin/\|/obj/" | grep -v "nameof(ShellHostingExtensions\.With"
```
Expected: no output.

- [ ] **Step 3: Fix the stale citation**

In `ShellNames.cs` replace the comment block above `DefaultWebDeviceTarget` with:
```csharp
    // web-server is Flutter's headless web device: it serves the app over HTTP without driving
    // a browser of its own, so the fixed FlutterWebPort below is a real, addressable endpoint.
    // The "chrome" device never prints or exposes a served URL, which made WithWebHost
    // unreachable for automation.
```

- [ ] **Step 4: Build and test**

```bash
dotnet format whitespace DigitalBrain.slnx --verify-no-changes && dotnet build DigitalBrain.slnx -c Release && dotnet test DigitalBrain.slnx -c Release --no-build
```
Expected: 0 errors, `failed: 0`. `TableAgentFacts`, `ClickHouseTableAgentFacts` and `clickhouse.feature` still pass (they prove the `chtable-` and `table-` routing survives without the DI registration).

- [ ] **Step 5: Commit**

```bash
git commit -am "ui: one home for the shell constants and the table source" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 4: Delete the unreachable shell subtree and rewire what it hid

**Files:**
- Delete (shell): `lib/chat/` except two files moved below, `lib/demos/`, `lib/windowing/`, `lib/onboarding/`, `lib/activity_screen.dart`, `lib/chat_screen.dart`, `lib/brain_theme.dart`, `assets/salesforce.svg`, `assets/google-signin-light-2x.png`, `test/agent_chat_test.dart`, `test/agent_table_test.dart`, `test/chat_stream_test.dart`, `test/surface_control_test.dart`, `test/workspace_startup_test.dart`
- Move: `lib/chat/brain_graph_store.dart` → `lib/workspace/brain_graph_store.dart`; `lib/chat/voice_file_io.dart` and `lib/chat/voice_file_web.dart` → `lib/workspace/`
- Modify (core): `core/lib/src/agent_events.dart` (add `AgentRunner`)
- Modify (shell): `lib/workspace/workspace_app.dart:10-11`, `lib/workspace/workspace_chat.dart:14`, `lib/workspace/artifact_editors.dart:10`, `lib/workspace/workspace_voice.dart:8-10`, `lib/integrations/integrations_menu.dart:3`, `lib/workspace/workspace_settings.dart:4`, `lib/auth/brain_session_gate.dart:6,139,147`, `lib/auth/login_screen.dart:3,75,85,89,129`, `pubspec.yaml`

All paths in this task are relative to `src/Modules/UI/Flutter/shell` unless they start with `core/`.

**Interfaces:**
- Produces: `typedef AgentRunner` in `package:digitalbrain_flutter` (core barrel already exports `agent_events.dart`); `typedef OpenUrl` in `lib/integrations/integrations_menu.dart`; `BrainGraphStore` at `lib/workspace/brain_graph_store.dart`.

- [ ] **Step 1: Add the run typedef to core**

Append to `core/lib/src/agent_events.dart` (after the `AgentEvent` class, before `decodeAgentEvents`):
```dart
/// One AG-UI run: the shell asks for it and consumes the event stream.
typedef AgentRunner = Stream<AgentEvent> Function({
  required String threadId,
  required String runId,
  String? parentRunId,
  required String text,
});
```

- [ ] **Step 2: Move the two kept files, delete the rest**

```bash
git mv lib/chat/brain_graph_store.dart lib/workspace/brain_graph_store.dart
git mv lib/chat/voice_file_io.dart lib/workspace/voice_file_io.dart
git mv lib/chat/voice_file_web.dart lib/workspace/voice_file_web.dart
git rm -r -q lib/chat lib/demos lib/windowing lib/onboarding lib/activity_screen.dart lib/chat_screen.dart lib/brain_theme.dart assets
git rm -q test/agent_chat_test.dart test/agent_table_test.dart test/chat_stream_test.dart test/surface_control_test.dart test/workspace_startup_test.dart
```

- [ ] **Step 3: Rewire the imports**

- `lib/workspace/workspace_app.dart`: delete `import '../chat/agent_chat_app.dart';`; change `import '../chat/brain_graph_store.dart';` to `import 'brain_graph_store.dart';`. (`AgentRunner` now comes from the `package:digitalbrain_flutter/digitalbrain_flutter.dart` import already present.)
- `lib/workspace/workspace_chat.dart`: delete `import '../chat/agent_chat_app.dart';`.
- `lib/workspace/artifact_editors.dart`: change `import '../chat/brain_graph_store.dart';` to `import 'brain_graph_store.dart';`.
- `lib/workspace/workspace_voice.dart`: change the conditional import to
  ```dart
  import 'voice_file_io.dart'
      if (dart.library.js_interop) 'voice_file_web.dart'
      as voice_file;
  ```
- `lib/integrations/integrations_menu.dart`: replace `import '../chat/chat_contracts.dart';` with, after the material import and a blank line:
  ```dart
  typedef OpenUrl = Future<void> Function(Uri url);
  ```
- `lib/workspace/workspace_settings.dart`: delete `import '../chat/chat_contracts.dart';` (`OpenUrl` now arrives through the `integrations_menu.dart` import on the next line).
- `lib/auth/brain_session_gate.dart` and `lib/auth/login_screen.dart`: replace `import '../brain_theme.dart';` with `import 'package:digitalbrain_ui/digitalbrain_ui.dart';` (keep the package imports sorted: `digitalbrain_ui` before `flutter`), then replace `BrainTheme.dark()` with `UiTheme.dark()`, `BrainPalette.` with `UiPalette.`, `BrainType.` with `UiType.`. In `login_screen.dart:85` change the literal `'IntoCaht'` to `'IntoChat'`.

- [ ] **Step 4: Trim `pubspec.yaml`**

Remove these dependency lines: `flyer_chat_text_message: ^2.6.0`, `flyer_chat_text_stream_message: ^2.3.0`, `graphic: ^2.7.0`, `flutter_svg: ^2.3.0`. Move `http: ^1.6.0` from `dependencies` to `dev_dependencies` (task 5's startup test needs `http.BaseClient`). Replace the `flutter:` section with:
```yaml
flutter:
  uses-material-design: true
```
Then, from `src/Modules/UI/Flutter`:
```bash
flutter pub get
```
Expected: resolves; `pubspec.lock` changes (commit it).

- [ ] **Step 5: Analyze, format, test**

From `src/Modules/UI/Flutter/shell`:
```bash
dart format lib test && flutter analyze && flutter test
```
Expected: `No issues found!`; all tests pass (12 files remain). An "unused import" or "undefined name" points at a rewiring miss in step 3; fix it there.

- [ ] **Step 6: Commit**

```bash
git add -A . ../core/lib/src/agent_events.dart ../pubspec.lock
git commit -m "ui: delete the shell paths main.dart cannot reach" -m "The uichat screens, demos, windowing and onboarding folders were reachable only from tests; the graph store, the voice file helpers, the run typedef and the open-url typedef move next to their users." -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 5: One persistence fake, and the two legacy tests ported onto the workspace

**Files:**
- Create: `shell/test/support/memory_workspace.dart`, `shell/test/shell_startup_test.dart`, `shell/test/workspace_tool_results_test.dart`
- Modify: `shell/test/workspace_app_test.dart`, `workspace_attachment_draft_test.dart`, `workspace_brain_render_test.dart`, `workspace_chat_design_test.dart`, `workspace_explore_test.dart`, `workspace_routes_test.dart`, `workspace_settings_test.dart`, `workspace_store_test.dart`, `workspace_windows_test.dart`

**Interfaces:**
- Produces: `MemoryWorkspacePersistence({String? value, bool failWrites = false})` implementing `WorkspacePersistence`.

- [ ] **Step 1: Write the shared fake**

`shell/test/support/memory_workspace.dart`:
```dart
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';

final class MemoryWorkspacePersistence implements WorkspacePersistence {
  MemoryWorkspacePersistence({this.value, this.failWrites = false});

  String? value;
  bool failWrites;

  @override
  Future<String?> read() async => value;

  @override
  Future<void> write(String data) async {
    if (failWrites) throw StateError('Device full');
    await null;
    value = data;
  }
}
```

- [ ] **Step 2: Replace the nine local fakes**

In each file delete the local class and add `import 'support/memory_workspace.dart';` (after the package imports, separated by a blank line), then rename the usages:

| File | Delete class | Replace constructor calls |
|---|---|---|
| `workspace_app_test.dart` | `MemoryWorkspace` | `MemoryWorkspace()` → `MemoryWorkspacePersistence()` |
| `workspace_attachment_draft_test.dart` | `_Storage` | `_Storage()` → `MemoryWorkspacePersistence()` |
| `workspace_brain_render_test.dart` | `_Memory` | `_Memory()` → `MemoryWorkspacePersistence()` |
| `workspace_chat_design_test.dart` | `_Memory` | `_Memory()` → `MemoryWorkspacePersistence()` |
| `workspace_explore_test.dart` | `Memory` | `Memory()` → `MemoryWorkspacePersistence()` |
| `workspace_routes_test.dart` | `_Storage` | `_Storage()` → `MemoryWorkspacePersistence()` |
| `workspace_settings_test.dart` | `_Storage` | `_Storage()` → `MemoryWorkspacePersistence()` |
| `workspace_store_test.dart` | `MemoryPersistence` | `MemoryPersistence()` → `MemoryWorkspacePersistence()`, and `.fail = ` → `.failWrites = ` |
| `workspace_windows_test.dart` | `MemoryWindows` | `MemoryWindows()` → `MemoryWorkspacePersistence()` |

If a deleted local class had a `value` field the test reads (the explore, settings and store tests keep a saved workspace across two stores), the shared fake has the same `value` field, so those reads keep compiling. Remove `import 'dart:convert';` or other imports that only the deleted class used when `flutter analyze` flags them.

- [ ] **Step 3: Run the shell tests**

```bash
cd src/Modules/UI/Flutter/shell && flutter test
```
Expected: all pass. If `workspace_store_test` "serialized writes keep the newest value" fails, the fake's microtask yield changed the interleaving; make `write` `await Future<void>.value()` instead of `await null` and rerun (both are microtask yields; only a real timer would break widget tests).

- [ ] **Step 4: Port the startup test**

`shell/test/shell_startup_test.dart`:
```dart
import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/main.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;

import 'support/memory_workspace.dart';

class NoRequests extends http.BaseClient {
  int calls = 0;
  @override
  Future<http.StreamedResponse> send(http.BaseRequest request) async {
    calls++;
    throw StateError('Unexpected startup request: ${request.url}');
  }
}

void main() {
  testWidgets('the shell starts on the project directory without any request', (
    tester,
  ) async {
    final transport = NoRequests();
    await tester.pumpWidget(
      buildShell(
        chat: 'main',
        workspaceStore: WorkspaceStore(persistence: MemoryWorkspacePersistence()),
        edge: DigitalBrainUiClient(
          baseUri: Uri.parse('http://localhost'),
          httpClient: transport,
        ),
      ),
    );
    await tester.pump();
    expect(find.text('Your projects'), findsWidgets);
    expect(find.byKey(const Key('workspace_graph')), findsNothing);
    expect(transport.calls, 0);
  });
}
```

- [ ] **Step 5: Port the table tool-result tests onto `WorkspaceApp`**

These are the first tests that push a `TOOL_CALL_RESULT` through the production chat into the working area (the research note, R3.7, records that no test did). `shell/test/workspace_tool_results_test.dart`:
```dart
import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_app.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:digitalbrain_ui/digitalbrain_ui.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/memory_workspace.dart';

TableSnapshot table({int revision = 1, String value = 'Alice'}) =>
    TableSnapshot(
      id: 'table-people',
      title: 'People',
      revision: revision,
      columns: const [TableColumn(id: 'name', label: 'Name', type: 'text')],
      rows: [
        TableRowData(id: 'r1', cells: [value]),
      ],
      filters: [],
      visibleColumns: ['name'],
      totalRows: 1,
      filteredRows: 1,
      offset: 0,
      limit: 50,
    );

Future<StreamController<AgentEvent>> sendFirstMessage(
  WidgetTester tester,
  WorkspaceStore store,
) async {
  final events = StreamController<AgentEvent>();
  await tester.pumpWidget(
    WorkspaceApp(
      store: store,
      onRun:
          ({
            required threadId,
            required runId,
            parentRunId,
            required text,
          }) => events.stream,
    ),
  );
  await tester.pumpAndSettle();
  await tester.tap(find.text('My project'));
  await tester.pumpAndSettle();
  await tester.tap(find.byTooltip('New conversation'));
  await tester.pumpAndSettle();
  await tester.enterText(find.byType(TextField), 'Make a table');
  await tester.pump();
  await tester.tap(find.byTooltip('Send message'));
  await tester.pump();
  return events;
}

Future<void> finish(WidgetTester tester, StreamController<AgentEvent> events) async {
  events.add(AgentEvent({'type': 'RUN_FINISHED'}));
  await events.close();
  await tester.pumpAndSettle();
}

void main() {
  testWidgets(
    'a table tool result opens in the working area and a newer result reuses its controller',
    (tester) async {
      final store = WorkspaceStore(persistence: MemoryWorkspacePersistence());
      final events = await sendFirstMessage(tester, store);
      events.add(
        AgentEvent({
          'type': 'TOOL_CALL_START',
          'toolCallId': 'a',
          'toolCallName': 'create_table',
        }),
      );
      events.add(
        AgentEvent({
          'type': 'TOOL_CALL_RESULT',
          'toolCallId': 'a',
          'content': table().toJson(),
        }),
      );
      await tester.pumpAndSettle();
      expect(find.byType(UiDataTable), findsOneWidget);
      final first = tester
          .widget<UiDataTable>(find.byType(UiDataTable))
          .controller;
      events.add(
        AgentEvent({
          'type': 'TOOL_CALL_RESULT',
          'toolCallId': 'b',
          'content': table(revision: 2, value: 'Bob').toJson(),
        }),
      );
      await tester.pumpAndSettle();
      final tables = tester
          .widgetList<UiDataTable>(find.byType(UiDataTable))
          .toList();
      expect(tables.every((t) => identical(t.controller, first)), isTrue);
      expect(first.snapshot.rows.single.cells.single, 'Bob');
      expect(store.currentProject.artifacts.single.kind, 'table');
      await finish(tester, events);
    },
  );

  testWidgets('a table error result stays a tool tile and creates no artifact', (
    tester,
  ) async {
    final store = WorkspaceStore(persistence: MemoryWorkspacePersistence());
    final events = await sendFirstMessage(tester, store);
    events.add(
      AgentEvent({
        'type': 'TOOL_CALL_START',
        'toolCallId': 'bad',
        'toolCallName': 'update_table_view',
      }),
    );
    events.add(
      AgentEvent({
        'type': 'TOOL_CALL_RESULT',
        'toolCallId': 'bad',
        'content': {
          'kind': 'tableError',
          'code': 'revision_conflict',
          'message': 'Read the latest table before trying again.',
        },
      }),
    );
    await tester.pumpAndSettle();
    expect(store.currentProject.artifacts, isEmpty);
    expect(find.byType(UiDataTable), findsNothing);
    expect(find.text('update table view'), findsOneWidget);
    await finish(tester, events);
  });
}
```

- [ ] **Step 6: Run, format, commit**

```bash
cd src/Modules/UI/Flutter/shell && dart format lib test && flutter analyze && flutter test
```
Expected: all pass, including the three new tests. If the tool-result test cannot find `UiDataTable`, the production seam (`workspace_chat.dart` `_event` → `onArtifact` → `_accept`) is broken; report it rather than adjusting the test.

```bash
git add -A test && git commit -m "ui: one persistence fake and the table tool-result tests on the workspace path" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 6: Delete the dead kit symbols and the 3D graph

**Files** (relative to `src/Modules/UI/Flutter/ui`):
- Delete: `lib/src/onboarding/` (four files), `lib/src/components/view/ui_view.dart`, `lib/src/chat/ui_chat_builders.dart`, `lib/src/chat/ui_message_factory.dart`, `lib/src/components/graph/ui_graph_view.dart`, `lib/src/components/graph/graph_scene.dart`, `lib/src/components/graph/three_graph_scene.dart`, `lib/src/components/graph/ui_graph_navigator.dart`, `test/table_ref_card_test.dart`
- Modify: `lib/src/chat/ui_chat.dart`, `lib/src/models/ui_part.dart`, `lib/src/components/project/ui_explore.dart:1-32`, `lib/src/components/project/ui_artifact_dock.dart:1-29`, `lib/src/gallery/ui_gallery_preview.dart`, `lib/digitalbrain_ui.dart`, `pubspec.yaml`

**Interfaces:**
- Consumes: nothing new.
- Produces: `UiChat` keeps its copy affordance through a private `_withCopy`; `UiPart.tryParse` recognises `button`, `chart`, `card`, `timer`, `spreadsheet` only.

- [ ] **Step 1: Delete**

```bash
git rm -r -q lib/src/onboarding lib/src/components/view/ui_view.dart lib/src/chat/ui_chat_builders.dart lib/src/chat/ui_message_factory.dart lib/src/components/graph/ui_graph_view.dart lib/src/components/graph/graph_scene.dart lib/src/components/graph/three_graph_scene.dart lib/src/components/graph/ui_graph_navigator.dart test/table_ref_card_test.dart
```

- [ ] **Step 2: Keep the copy affordance inside `ui_chat.dart`**

Replace `lib/src/chat/ui_chat.dart` with:
```dart
import 'package:flutter/material.dart';
import 'package:flutter_chat_core/flutter_chat_core.dart';
import 'package:flutter_chat_ui/flutter_chat_ui.dart';

import '../models/ui_part.dart';
import '../theme/ui_theme.dart';
import 'ui_copyable_message.dart';

/// The shared chat surface for full pages and embedded assistant panels.
///
/// Hosts own conversations, transport, and optional specialized builders. The
/// ui owns the chat presentation and theme in every workspace destination.
final class UiChat extends StatelessWidget {
  const UiChat({
    super.key,
    required this.chatController,
    required this.currentUserId,
    required this.resolveUser,
    this.builders,
    this.onMessageSend,
    this.onAttachmentTap,
    this.workspaceTheme = false,
  });

  final bool workspaceTheme;
  final ChatController chatController;
  final UserID currentUserId;
  final ResolveUserCallback resolveUser;
  final Builders? builders;
  final OnMessageSendCallback? onMessageSend;
  final OnAttachmentTapCallback? onAttachmentTap;

  @override
  Widget build(BuildContext context) => Chat(
    chatController: chatController,
    currentUserId: currentUserId,
    resolveUser: resolveUser,
    builders: _withCopy(builders),
    onMessageSend: onMessageSend,
    onAttachmentTap: onAttachmentTap,
    theme: workspaceTheme
        ? UiChatTheme.workspace(Theme.of(context))
        : Theme.of(context).brightness == Brightness.light
        ? UiChatTheme.light()
        : UiChatTheme.dark(),
  );
}

/// Wraps text, stream, and custom bubbles with selection + copy.
Builders _withCopy(Builders? host) {
  final base = host ?? const Builders();
  final text = base.textMessageBuilder;
  final stream = base.textStreamMessageBuilder;
  final custom = base.customMessageBuilder;
  var builders = base.copyWith(
    textMessageBuilder:
        (
          context,
          message,
          index, {
          required bool isSentByMe,
          MessageGroupStatus? groupStatus,
        }) {
          final child =
              text?.call(
                context,
                message,
                index,
                isSentByMe: isSentByMe,
                groupStatus: groupStatus,
              ) ??
              SimpleTextMessage(message: message, index: index);
          return UiCopyableMessage(
            copyText: (_) => message.text,
            child: child,
          );
        },
  );
  if (stream != null) {
    builders = builders.copyWith(
      textStreamMessageBuilder:
          (
            context,
            message,
            index, {
            required bool isSentByMe,
            MessageGroupStatus? groupStatus,
          }) {
            return UiCopyableMessage(
              copyText: (context) =>
                  UiStreamCopy.streamText(context, message.streamId),
              child: stream(
                context,
                message,
                index,
                isSentByMe: isSentByMe,
                groupStatus: groupStatus,
              ),
            );
          },
    );
  }
  if (custom != null) {
    builders = builders.copyWith(
      customMessageBuilder:
          (
            context,
            message,
            index, {
            required bool isSentByMe,
            MessageGroupStatus? groupStatus,
          }) {
            final child = custom(
              context,
              message,
              index,
              isSentByMe: isSentByMe,
              groupStatus: groupStatus,
            );
            if (child is UiCopyableMessage) {
              return child;
            }
            final part = UiPart.tryParse(
              message.metadata == null
                  ? null
                  : Map<String, dynamic>.from(message.metadata!),
            );
            final copy = part?.copyText ?? '';
            if (copy.trim().isEmpty) {
              return child;
            }
            return UiCopyableMessage(copyText: (_) => copy, child: child);
          },
    );
  }
  return builders;
}
```

- [ ] **Step 3: Remove the reference parts**

In `lib/src/models/ui_part.dart` delete the classes `UiChartRefPart`, `UiImageRefPart`, `UiSheetRefPart`, `UiGraphRefPart` and `UiTableRefPart` (each is a `final class ... extends UiPart` block with `kindName`, `name`, `caption`, `fromMetadata`, `toMetadata`, `copyText`; `UiSheetPart` between them stays) and the five matching `tryParse` arms, leaving:
```dart
    return switch (kind) {
      UiButtonPart.kindName => UiButtonPart.fromMetadata(metadata),
      UiChartPart.kindName => UiChartPart.fromMetadata(metadata),
      UiCardPart.kindName => UiCardPart.fromMetadata(metadata),
      UiTimerPart.kindName => UiTimerPart.fromMetadata(metadata),
      UiSheetPart.kindName => UiSheetPart.fromMetadata(metadata),
      _ => null,
    };
```

- [ ] **Step 4: Remove the two dead widgets**

In `lib/src/components/project/ui_explore.dart` delete the `UiSpecialistSuggestions` class (lines 5–32, from `class UiSpecialistSuggestions` to its closing `}`); the imports stay because `UiSpecialistIcon` uses `flutter_svg` and `forui` further down. In `lib/src/components/project/ui_artifact_dock.dart` delete the `UiEditorFrame` class (lines 3–29).

- [ ] **Step 5: Prune the gallery**

In `lib/src/gallery/ui_gallery_preview.dart`:
- delete the `GalleryEntry(` whose first argument is `'view'` (lines 173–183) and the one whose first argument is `'graph-3d'` (search `'graph-3d'`; it sits after the `'brain-graph'` entry);
- delete the `case 'view':` arm (lines 697–705) and the `case 'graph-3d':` arm (from `case 'graph-3d':` up to, not including, `default:`);
- delete the fields `_launched` (from the `bool _checked = true, _switched = false, _launched = false;` line, keep the other two), `_display` (keep `_lastAction` on that line), the `_graph` controller field and its `_graph.dispose();` line, and the `void _key(String key)` method;
- delete the imports `../components/graph/ui_graph_controller.dart`, `../components/graph/ui_graph_navigator.dart`, `../components/graph/ui_graph_view.dart`, `../components/view/ui_view.dart`.
The `_nodes`/`_edges` fixtures stay: the `'graph'` case renders them with the 2D `UiGraph`.

- [ ] **Step 6: Barrel and dependencies**

Replace `lib/digitalbrain_ui.dart` with the sorted export list:
```dart
library;

export 'src/chat/ui_chat.dart';
export 'src/chat/ui_copyable_message.dart';
export 'src/components/button/ui_button.dart';
export 'src/components/card/ui_card.dart';
export 'src/components/chart/ui_chart.dart';
export 'src/components/clock/ui_clock.dart';
export 'src/components/graph/graph_camera.dart';
export 'src/components/graph/graph_layout.dart';
export 'src/components/graph/graph_models.dart';
export 'src/components/graph/ui_graph.dart';
export 'src/components/graph/ui_graph_controller.dart';
export 'src/components/image/ui_image.dart';
export 'src/components/project/ui_artifact_dock.dart';
export 'src/components/project/ui_explore.dart';
export 'src/components/project/ui_project_library.dart';
export 'src/components/sheet/ui_sheet.dart';
export 'src/components/table/ui_data_table.dart';
export 'src/components/table/ui_table_controller.dart';
export 'src/gallery/ui_gallery_screen.dart';
export 'src/lumen/ino_presence.dart';
export 'src/lumen/lumen_brain_graph.dart';
export 'src/lumen/lumen_controls.dart';
export 'src/lumen/lumen_palette.dart';
export 'src/lumen/neuron_icon.dart';
export 'src/models/ui_part.dart';
export 'src/theme/ui_theme.dart';
```
In `pubspec.yaml` delete the line `three_js: ^0.3.0`. From `src/Modules/UI/Flutter` run `flutter pub get` (commit `pubspec.lock`).

- [ ] **Step 7: Analyze, format, test both packages that depend on the kit**

```bash
cd src/Modules/UI/Flutter/ui && dart format lib test && flutter analyze && flutter test
cd ../shell && flutter analyze && flutter test
```
Expected: `No issues found!` in both; ui tests 25 pass (26 minus the deleted ref-card file's 4 plus nothing added; the count printed is what matters, all green), shell tests unchanged. `ui_chart_test`, `data_table_test`, `gallery_table_test`, `project_library_test`, `explore_test` must still pass.

- [ ] **Step 8: Commit**

```bash
git add -A . ../pubspec.lock && git commit -m "ui: delete the kit symbols nothing reaches and the 3D graph scene" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 7: Trim the core client to the routes production uses

**Files** (relative to `src/Modules/UI/Flutter/core`):
- Delete: `lib/src/models/chat_models.dart`, `lib/src/models/surface_models.dart`, `lib/src/models/execution_activity.dart`, `lib/src/sse_frames.dart`, `lib/src/sse_chat_frames.dart`, `lib/src/ui_models.dart`, `test/ui_client_routes_test.dart`, `test/chat_stream_parser_test.dart`
- Modify: `lib/src/ui_client.dart`, `lib/digitalbrain_flutter.dart`, `pubspec.yaml`

**Interfaces:**
- Produces: `DigitalBrainUiClient` with `checkAuth`, `transcribeVoice`, `salesforceConnected`, `listWorkspaceArtifacts`, `readWorkspaceArtifact`, `createWorkspaceArtifact`, `updateWorkspaceArtifact`, `listTables`, `readTable`, `createTable`, `updateTableView`, `runAgent`, `watchBrain`, `readBrain`, `close`.

- [ ] **Step 1: Delete the files**

```bash
git rm -q lib/src/models/chat_models.dart lib/src/models/surface_models.dart lib/src/models/execution_activity.dart lib/src/sse_frames.dart lib/src/sse_chat_frames.dart lib/src/ui_models.dart test/ui_client_routes_test.dart test/chat_stream_parser_test.dart
```

- [ ] **Step 2: Delete the dead client members**

In `lib/src/ui_client.dart` delete these members in full (each is one contiguous method; original line of each signature in parentheses): `workspaceCapabilities` (115), `readActivityResults` (325), `watchActivities` (344), `setBrainSubscription` (492), `openSurface` (550), `activateControl` (562), `cancelTurn` (579), `watchSurfaceEvents` (589), `_watchEvents` (603, ends at 709 with its private helpers for backoff, keep nothing of it), `sendMessage` (710), `sendVoice` (727), `_acceptedWorkId` (757), `watchChatTurns` (773), `readChart` (787), `readGraph` (792), `readSurface` (797), `readSpreadsheet` (802), `readImage` (810), `readImageBytes` (813), `_getUiEntity` (828). Keep `_request` (`readBrain` uses it), `_tableRequest`, `runAgent`, `watchBrain`, `readBrain`, `close`.

Then fix the imports at the top of the file to:
```dart
import 'dart:async';
import 'dart:convert';

import 'package:http/http.dart' as http;
import 'package:uuid/uuid.dart';

import 'agent_events.dart';
import 'basic_credentials.dart';
import 'cookie_http_client.dart';
import 'host_environment.dart';
import 'models/brain_models.dart';
import 'models/table_models.dart';
```
`dart analyze` reports any member that still references a deleted type; delete that member too if it is in the list above, otherwise stop and report.

- [ ] **Step 3: Barrel and pubspec**

Replace `lib/digitalbrain_flutter.dart` with:
```dart
export 'src/agent_events.dart';
export 'src/basic_credentials.dart';
export 'src/host_environment.dart';
export 'src/models/brain_models.dart';
export 'src/models/table_models.dart';
export 'src/ui_client.dart';
```
(`CookieHttpClient` is a transport detail; nothing outside `core` used it. If `flutter analyze` in `shell` reports it missing, stop and report.)

In `pubspec.yaml` delete `web_socket_channel: ^3.0.3` and the two-line `executables:` block, and replace the description with:
```yaml
description: >
  DigitalBrain kernel client for the Flutter shell: AG-UI runs, tables,
  workspace artifacts, brain observation. Never Orleans, never MCP-as-UI.
```
From `src/Modules/UI/Flutter` run `flutter pub get`.

- [ ] **Step 4: Analyze and test everything that depends on core**

```bash
cd src/Modules/UI/Flutter/core && dart format lib test && dart analyze && dart test
cd ../ui && flutter analyze && flutter test
cd ../shell && flutter analyze && flutter test
```
Expected: core `agent_stream_test`, `table_client_test`, `workspace_client_test` pass; ui and shell unchanged and green.

- [ ] **Step 5: Commit**

```bash
git add -A . ../pubspec.lock && git commit -m "ui: trim the core client to the routes the shell uses" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 8: Run Dart analysis and tests on every pull request, then the full gate

**Files:**
- Modify: `.github/workflows/ci.yml` (the `flutter-web-build` job)

- [ ] **Step 1: Add the steps**

In the `flutter-web-build` job, after the `Dart format` step and before `Web release build`, insert:
```yaml
      - name: Analyze core, ui and shell
        working-directory: src/Modules/UI/Flutter
        run: |
          (cd core && dart analyze --fatal-infos)
          (cd ui && flutter analyze --no-pub)
          (cd shell && flutter analyze --no-pub)

      - name: Core tests
        working-directory: src/Modules/UI/Flutter/core
        run: dart test

      - name: Ui tests
        working-directory: src/Modules/UI/Flutter/ui
        run: flutter test --no-pub

      - name: Shell tests
        working-directory: src/Modules/UI/Flutter/shell
        run: flutter test --no-pub
```
Rename the job's `name:` to `Flutter analyze, test and web build`.

- [ ] **Step 2: Run the complete gate locally**

```bash
dotnet format whitespace DigitalBrain.slnx --verify-no-changes && dotnet build DigitalBrain.slnx -c Release && dotnet test DigitalBrain.slnx -c Release --no-build
cd src/Modules/UI/Flutter && dart format --output=none --set-exit-if-changed core ui shell
(cd core && dart analyze --fatal-infos && dart test) && (cd ui && flutter analyze && flutter test) && (cd shell && flutter analyze && flutter test && MSYS_NO_PATHCONV=1 flutter build web --release --base-href "/")
```
Expected: every command exits 0. `dart analyze --fatal-infos` may surface info-level hints in `core` that were invisible before; fix them (they are in files this plan touched or in the two remaining test files), never lower the flag.

- [ ] **Step 3: Start the AppHost and smoke the shell**

```bash
aspire stop; DigitalBrain__Graph__Enabled=true aspire run --detach --non-interactive --project src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj
```
Wait for `curl -s -o /dev/null -w "%{http_code}" http://localhost:5080/health` to print 200, then run the AG-UI driver against the workspace agent with a table and a chart prompt (the scratchpad `smoke.py` from the ClickHouse work, or an equivalent `POST /agent` script) and confirm a `kind: "table"` and a `kind: "chart"` tool result still stream. The shell resource must reach `Running` in `aspire` (its `flutter run -d windows` compiles the trimmed shell).

- [ ] **Step 4: Commit**

```bash
git add .github/workflows/ci.yml && git commit -m "ui: analyze and test the Flutter packages on every pull request" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
```

---

### Task 9: Record the outcome and open the pull request

**Files:**
- Modify: `docs/ui/ui-layer-research.md` (append a section), `docs/ui/ui-layer-design.md` (phase 0 line in section 7 gains "done" and the commit range)

- [ ] **Step 1: Measure**

From `src/Modules/UI/Flutter`:
```bash
for d in core ui shell; do printf "%s lib=" "$d"; find $d/lib -name "*.dart" | xargs cat | wc -l | tr -d '\n'; printf " test="; find $d/test -name "*.dart" | xargs cat | wc -l; done
```
and from the repo root for C#:
```bash
for d in src/Modules/UI/DigitalBrain.Modules.UI.Contracts src/Modules/UI/DigitalBrain.Modules.UI src/Modules/UI/DigitalBrain.Modules.UI.Aspire.Hosting; do printf "%s: " "$d"; find "$d" -name "*.cs" -not -path "*/bin/*" -not -path "*/obj/*" | xargs cat | wc -l; done
```

- [ ] **Step 2: Append to the research note**

Add at the end of `docs/ui/ui-layer-research.md`:
```markdown
## 5. Phase 0 outcome (<date>)

Deleted by the reachability rule on `feature/ui-phase0-remove-unreachable`: the Transcript neuron
family, the chat and voice HTTP routes with six `http.feature` scenarios, the shell `chat/`,
`demos/`, `windowing/` and `onboarding/` folders plus `activity_screen.dart`, `chat_screen.dart`,
`brain_theme.dart` and two assets, the kit's onboarding folder, `UiView`, the chat builders and
message factory, the five reference parts, the 3D graph scene and `three_js`, and the core client's
surface, activity, chat and entity-reader methods with their models. `AgentRunner` lives in core,
`OpenUrl` next to the integrations menu, `BrainGraphStore` and the voice file helpers under
`workspace/`. Tests: five shell files and two core files deleted, the persistence fake consolidated
into `test/support/memory_workspace.dart`, the startup and table tool-result tests ported onto
`WorkspaceApp`, and `ci.yml` now analyzes and tests the three Dart packages on every pull request.

Line counts after phase 0: C# Contracts <n> / Module <n> / Aspire.Hosting <n>; Dart core <n>
(tests <n>), ui <n> (tests <n>), shell <n> (tests <n>).
```
Fill every `<n>` and `<date>` from step 1.

- [ ] **Step 3: Commit, push, open the pull request**

```bash
git add docs/ui && git commit -m "ui: record the phase 0 outcome" -m "Co-Authored-By: Claude Fable 5.1 <noreply@anthropic.com>"
git push -u origin feature/ui-phase0-remove-unreachable
```
Open the PR against `master` titled `ui: phase 0, remove what is unreachable` with a body that lists: the design documents added, the deletion table from the research note's section 5, the ported tests, the CI change, the line counts before and after, and the gate results. End the body with `🤖 Generated with [Claude Code](https://claude.com/claude-code)`. Run the code-review skill on the branch before declaring the task done, and fix what it confirms.
