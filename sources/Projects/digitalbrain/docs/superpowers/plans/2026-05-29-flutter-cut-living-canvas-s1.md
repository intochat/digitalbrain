# Flutter Cut → Living Canvas (S1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the three giant route screens (~14,400 lines) with one lean `LivingCanvasScreen` (full-bleed neuron graph + floating dock + a constructed RFW host), then delete the orphaned legacy Dart, leaving the app at a fraction of its current size with no behavior-specific UI.

**Architecture:** Reuse the already-self-contained `LiveScreen` graph widget (it wires its own BrainWatch gRPC stream) and the existing `FloatingPromptDock`, composed under `SynapseStreamScope` + `DigitalBrainClientScope`. The new screen owns the gateway client exactly the way `BrainSceneScreen` does today. Deletion is incremental and **driven by `flutter analyze`** after every removal — never delete a file without first confirming zero inbound imports.

**Tech Stack:** Flutter (Dart), `go_router`, `grpc`/`grpc_or_grpcweb`, RFW (`rfw` package), the in-repo `digital_brain_ui` liquid-glass kit. No `flutter test` (project rule): verification is `flutter analyze` + build + render-observation + `dotnet test` for the E2E RFW contract.

**This is slice S1 of the spec** `docs/superpowers/specs/2026-05-29-living-canvas-ui-unification-design.md`. Slices S2–S7 are separate follow-on plans (see end of file).

---

## Pre-flight context (read once before starting)

- **Working dir for all Flutter commands:** `E:\digitalbrain\UI\flutter`. Run `flutter` there (it has the `pubspec.yaml`).
- **The keepers** (do NOT delete): `digital_brain_ui/**` (kit), `rfw_host/**`, `theme/**`, `grpc/**`, `telemetry/**`, `shell/**`, `features/brain/widgets/floating_prompt_dock.dart`, `features/brain/voice_input.dart`, `features/live/**` (the graph + cards + timeline that `LiveScreen` composes), `features/neuron_constructor/visual_constructor_models.dart`, `features/neuron_constructor/visual_constructor_state.dart` (small, reused in S4).
- **The targets** (delete in this slice, verified by analyze): `features/brain/brain_scene_screen.dart` (6474 L), `features/home/constructor_editor_home_page.dart` (3840 L), `features/constellation/**` (5 files), `features/neuron_constructor/neuron_constructor_view.dart` (2885 L), `features/neuron_constructor/liquid_glass_3d_brain.dart` (704 L), and whatever `flutter analyze` then proves orphaned (most of `features/ino_editor/**`, some `widgets/**`).
- **Real APIs you will reuse** (verified in the current tree):
  - Gateway client: `final (host, port, secure) = resolveKernelEndpoint();` → `createKernelChannel(host: host, port: port, secure: secure)` → `DigitalBrainGatewayClient(channel, interceptors: kernelInterceptors())`. (`brain_scene_screen.dart:656–661`)
  - Prompt submit: `SubmitPromptRequest()..userId = ..; ..text = ..; ..correlationId = ..;` then `client.submitPrompt(req, options: ...)`. (`brain_scene_screen.dart:1344–1349`)
  - Graph widget: `LiveScreen({controller, onSynapseEdge, activeScope, activeLayout, ...})` — self-wires BrainWatch. (`features/live/live_screen.dart:80`)
  - Dock: `FloatingPromptDock({required client, required onSubmit, onListeningChanged})`. (`features/brain/widgets/floating_prompt_dock.dart:10`)
  - Stream scope: `SynapseStreamScope({required notifier, required child})` + `SynapseStreamFeed`. (`rfw_host/synapse_stream_scope.dart`)
  - Client scope: `DigitalBrainClientScope({required client, required child})`. (`shell/digitalbrain_client_scope.dart`)
  - RFW host: `RfwRuntimeHost()` with `ensureLoaded(key, source)` + `render(key, data:, onEvent:, rootWidget:)`. (`rfw_host/rfw_runtime_host.dart`)

---

### Task 0: Branch + baseline

**Files:** none (git + measurement only)

- [ ] **Step 1: Create the working branch**

Run (from `E:\digitalbrain`):
```bash
git checkout -b feat/flutter-cut-living-canvas-s1
```

- [ ] **Step 2: Record the baseline Dart file count**

Run (from `E:\digitalbrain\UI\flutter`):
```bash
git ls-files "lib/**/*.dart" | wc -l
```
Expected: `120`. Write this number at the top of your scratch notes — Task 10 compares against it.

- [ ] **Step 3: Confirm a clean analyze baseline**

Run (from `E:\digitalbrain\UI\flutter`):
```bash
flutter analyze
```
Expected: completes with `No issues found!` (or the repo's known pre-existing baseline). If there are pre-existing warnings, copy them to scratch notes so you can tell new breakage from old.

---

### Task 1: Create `LivingCanvasScreen`

**Files:**
- Create: `UI/flutter/lib/features/canvas/living_canvas_screen.dart`

- [ ] **Step 1: Write the new screen file**

Create `UI/flutter/lib/features/canvas/living_canvas_screen.dart` with exactly this content:

```dart
import 'package:flutter/material.dart';

import 'package:digitalbrain_flutter/grpc/digitalbrain.pbgrpc.dart';
import 'package:digitalbrain_flutter/grpc/endpoint.dart';
import 'package:digitalbrain_flutter/grpc/grpc_channel.dart';
import 'package:digitalbrain_flutter/telemetry/grpc_interceptor.dart';
import 'package:digitalbrain_flutter/theme/digitalbrain_theme.dart';
import 'package:digitalbrain_flutter/shell/digitalbrain_client_scope.dart';
import 'package:digitalbrain_flutter/rfw_host/synapse_stream_scope.dart';
import 'package:digitalbrain_flutter/rfw_host/rfw_runtime_host.dart';
import 'package:digitalbrain_flutter/features/live/live_screen.dart';
import 'package:digitalbrain_flutter/features/brain/widgets/floating_prompt_dock.dart';

/// The single home surface (Assist mode, S1). A full-bleed neuron graph with a
/// floating prompt dock. The RFW host is constructed and ready; live RFW cards
/// from the home feed are wired in S2. Operate mode is wired in S4.
class LivingCanvasScreen extends StatefulWidget {
  const LivingCanvasScreen({super.key});

  @override
  State<LivingCanvasScreen> createState() => _LivingCanvasScreenState();
}

class _LivingCanvasScreenState extends State<LivingCanvasScreen> {
  final RfwRuntimeHost _host = RfwRuntimeHost();
  final LiveScreenController _liveController = LiveScreenController();
  final SynapseStreamFeed _streamFeed = SynapseStreamFeed();

  static const String _kLocalUserId = 'local-user';

  dynamic _channel;
  DigitalBrainGatewayClient? _client;
  bool _voiceActive = false;

  @override
  void initState() {
    super.initState();
    try {
      final (host, port, secure) = resolveKernelEndpoint();
      _channel = createKernelChannel(host: host, port: port, secure: secure);
      _client = DigitalBrainGatewayClient(
        _channel,
        interceptors: kernelInterceptors(),
      );
    } catch (_) {
      // Kernel endpoint unresolved (standalone run without the dart-define):
      // the canvas still renders; the dock just can't submit until connected.
    }
  }

  Future<void> _handleSubmitPrompt(String text) async {
    final client = _client;
    if (text.isEmpty || client == null) return;
    final cid = 'prompt-${DateTime.now().microsecondsSinceEpoch}'
        '-${identityHashCode(this)}';
    final req = SubmitPromptRequest()
      ..userId = _kLocalUserId
      ..text = text
      ..correlationId = cid;
    try {
      await client.submitPrompt(req);
    } catch (_) {
      // Send failed: leave the dock intact so the user can re-submit.
    }
  }

  @override
  Widget build(BuildContext context) {
    final client = _client;
    Widget body = Scaffold(
      backgroundColor: DigitalBrainColors.bg0,
      body: SynapseStreamScope(
        notifier: _streamFeed,
        child: Stack(
          children: [
            Positioned.fill(
              child: LiveScreen(
                controller: _liveController,
                onSynapseEdge: (edge) =>
                    _streamFeed.publish([..._streamFeed.forCorrelation('')]),
              ),
            ),
            if (client != null)
              Positioned(
                bottom: 28,
                left: 0,
                right: 0,
                child: Center(
                  child: FloatingPromptDock(
                    client: client,
                    onSubmit: _handleSubmitPrompt,
                    onListeningChanged: (active) =>
                        setState(() => _voiceActive = active),
                  ),
                ),
              ),
          ],
        ),
      ),
    );
    if (client != null) {
      body = DigitalBrainClientScope(client: client, child: body);
    }
    return body;
  }
}
```

> Note on `onSynapseEdge`: S1 only needs the graph to render and the dock to
> submit. The `_streamFeed.publish(...)` call above keeps the scope wired
> without depending on edge internals; S2 replaces it with real edge fan-out.
> `_voiceActive` is read by the dock toggle only — it is intentionally minimal
> here and grows in S2. If `flutter analyze` flags `_voiceActive` as unused,
> prefix it with `// ignore: unused_field` rather than deleting the setter (S2
> uses it).

- [ ] **Step 2: Analyze the new file in isolation**

Run (from `E:\digitalbrain\UI\flutter`):
```bash
flutter analyze lib/features/canvas/living_canvas_screen.dart
```
Expected: no errors. If `submitPrompt`, `LiveScreen`, `FloatingPromptDock`, or `kernelInterceptors` resolve with errors, open the cited source line in the Pre-flight section and match the real signature before continuing.

- [ ] **Step 3: Commit**

```bash
git add UI/flutter/lib/features/canvas/living_canvas_screen.dart
git commit -m "feat(flutter): add lean LivingCanvasScreen (graph + dock)"
```

---

### Task 2: Route `/canvas` to the new screen and verify it renders

**Files:**
- Modify: `UI/flutter/lib/router.dart`

- [ ] **Step 1: Add a temporary verification route**

In `UI/flutter/lib/router.dart`, add this import near the other feature imports (after line 8):
```dart
import 'features/canvas/living_canvas_screen.dart';
```
Then add this route as the **first** entry inside the `routes: [` list (before the `/constellation` route):
```dart
    GoRoute(
      path: '/canvas',
      name: 'canvas',
      builder: (context, state) => const LivingCanvasScreen(),
    ),
```

- [ ] **Step 2: Analyze**

Run (from `E:\digitalbrain\UI\flutter`):
```bash
flutter analyze lib/router.dart lib/features/canvas/living_canvas_screen.dart
```
Expected: no errors.

- [ ] **Step 3: Run the app and observe the canvas renders**

Use the Aspire Flutter resource per the `flutter` skill (rebuild the `flutter-web` resource), then open `http://localhost:5800/#/canvas` with the Playwright MCP.

Expected: the neuron graph renders full-bleed and the frosted prompt dock is centered at the bottom. The a11y snapshot shows the "Enable accessibility" button (canvas-rendered) — an *empty* snapshot means the app failed to mount; check `get_runtime_errors` via `flutter-windows`.

- [ ] **Step 4: Commit**

```bash
git add UI/flutter/lib/router.dart
git commit -m "feat(flutter): add temporary /canvas route for verification"
```

---

### Task 3: Make the Living Canvas the root, retire legacy routes

**Files:**
- Modify: `UI/flutter/lib/router.dart`

- [ ] **Step 1: Point `/` at the new screen and remove legacy routes**

In `UI/flutter/lib/router.dart`:
1. Change the `/` route's `child:` from `const ConstructorEditorHomePage()` to `const LivingCanvasScreen()` (keep the `CallbackShortcuts`/`Focus` wrapper).
2. Delete the `/canvas` route added in Task 2 (it was temporary — `/` is now the canvas).
3. Delete the `/constellation` `GoRoute` and the `/brain/:brainId` `GoRoute`.
4. Delete the now-unused imports at the top: `features/brain/brain_scene_screen.dart`, `features/constellation/constellation_screen.dart`, `features/home/constructor_editor_home_page.dart`.
5. Delete the entire `BrainScenePlaceholder` class at the bottom of the file (lines ~79–176) — it is dead code referenced by nothing.

- [ ] **Step 2: Analyze the whole project**

Run (from `E:\digitalbrain\UI\flutter`):
```bash
flutter analyze
```
Expected: errors will now appear **only** in files that imported the deleted screens or `BrainScenePlaceholder`. The three big screen files themselves still exist (deleted in later tasks) so they should NOT error yet. If `router.dart` is clean, proceed.

- [ ] **Step 3: Run and observe `/` is the canvas**

Rebuild the `flutter-web` resource; open `http://localhost:5800/` with Playwright. Expected: identical to Task 2's render (graph + dock), now at root.

- [ ] **Step 4: Commit**

```bash
git add UI/flutter/lib/router.dart
git commit -m "feat(flutter): make LivingCanvasScreen the root, drop legacy routes"
```

---

### Task 4: Delete `BrainSceneScreen`

**Files:**
- Delete: `UI/flutter/lib/features/brain/brain_scene_screen.dart`

- [ ] **Step 1: Confirm nothing still imports it**

Run (from `E:\digitalbrain\UI\flutter`):
```bash
grep -rl "brain_scene_screen.dart\|BrainSceneScreen" lib/
```
Expected: **no output** (router no longer references it after Task 3). If any file is listed, it is another dead screen — note it; it will be handled in its own task. Do not delete this file until the list is empty.

- [ ] **Step 2: Delete the file**

```bash
git rm UI/flutter/lib/features/brain/brain_scene_screen.dart
```

- [ ] **Step 3: Analyze to surface freshly-orphaned imports**

Run (from `E:\digitalbrain\UI\flutter`):
```bash
flutter analyze
```
Expected: no *errors* (broken references). Unused-import or unused-element **warnings** are expected and are your deletion to-do list for Task 9 — copy them to scratch notes. If an *error* appears, a survivor still depended on the screen; restore with `git checkout -- <file>` and investigate before re-deleting.

- [ ] **Step 4: Commit**

```bash
git commit -m "refactor(flutter): delete BrainSceneScreen (replaced by LivingCanvasScreen)"
```

---

### Task 5: Delete the Constellation feature

**Files:**
- Delete: `UI/flutter/lib/features/constellation/constellation_screen.dart`
- Delete: `UI/flutter/lib/features/constellation/brain_camera.dart`
- Delete: `UI/flutter/lib/features/constellation/brain_mesh.dart`
- Delete: `UI/flutter/lib/features/constellation/brain_node_widget.dart`
- Delete: `UI/flutter/lib/features/constellation/comparative_harness_widget.dart`

- [ ] **Step 1: Confirm no survivor imports the constellation feature**

Run (from `E:\digitalbrain\UI\flutter`):
```bash
grep -rl "features/constellation/\|ConstellationScreen\|ComparativeHarness" lib/ | grep -v "lib/features/constellation/"
```
Expected: **no output**. (`comparative_harness_widget.dart` was shared with the now-deleted `BrainSceneScreen`; confirm it is no longer referenced outside its own folder.) If anything outside the folder is listed, stop and inspect.

- [ ] **Step 2: Delete the folder**

```bash
git rm -r UI/flutter/lib/features/constellation/
```

- [ ] **Step 3: Analyze**

Run (from `E:\digitalbrain\UI\flutter`):
```bash
flutter analyze
```
Expected: no errors. New unused warnings → scratch notes.

- [ ] **Step 4: Commit**

```bash
git commit -m "refactor(flutter): delete constellation feature (folded into single canvas)"
```

---

### Task 6: Delete `ConstructorEditorHomePage`

**Files:**
- Delete: `UI/flutter/lib/features/home/constructor_editor_home_page.dart`

- [ ] **Step 1: Confirm nothing imports it**

Run (from `E:\digitalbrain\UI\flutter`):
```bash
grep -rl "constructor_editor_home_page.dart\|ConstructorEditorHomePage" lib/
```
Expected: **no output**.

- [ ] **Step 2: Delete**

```bash
git rm UI/flutter/lib/features/home/constructor_editor_home_page.dart
```

- [ ] **Step 3: Analyze**

Run (from `E:\digitalbrain\UI\flutter`):
```bash
flutter analyze
```
Expected: no errors. New unused warnings → scratch notes.

- [ ] **Step 4: Commit**

```bash
git commit -m "refactor(flutter): delete ConstructorEditorHomePage"
```

---

### Task 7: Delete the heavyweight INO-coupled constructor view (keep the small models)

**Files:**
- Delete: `UI/flutter/lib/features/neuron_constructor/neuron_constructor_view.dart`
- Delete: `UI/flutter/lib/features/neuron_constructor/liquid_glass_3d_brain.dart`
- Keep: `visual_constructor_models.dart`, `visual_constructor_state.dart`

- [ ] **Step 1: Confirm the two heavy files are now orphaned**

Run (from `E:\digitalbrain\UI\flutter`):
```bash
grep -rl "neuron_constructor_view.dart\|NeuronConstructorView\|liquid_glass_3d_brain.dart\|LiquidGlass3dBrain" lib/
```
Expected: **no output** (their only importers were the three deleted screens). If a survivor is listed, stop and inspect.

- [ ] **Step 2: Delete the two files**

```bash
git rm UI/flutter/lib/features/neuron_constructor/neuron_constructor_view.dart
git rm UI/flutter/lib/features/neuron_constructor/liquid_glass_3d_brain.dart
```

- [ ] **Step 3: Verify the small models survive and still analyze**

Run (from `E:\digitalbrain\UI\flutter`):
```bash
flutter analyze lib/features/neuron_constructor/
flutter analyze
```
Expected: `visual_constructor_models.dart` and `visual_constructor_state.dart` remain and analyze clean (they are reused in S4). No project errors.

- [ ] **Step 4: Commit**

```bash
git commit -m "refactor(flutter): delete INO-coupled constructor view, keep visual models for S4"
```

---

### Task 8: Sweep the now-orphaned files (analyze-driven)

**Files:** determined at runtime by `flutter analyze` — likely most of `features/ino_editor/**` and unused `widgets/**` (e.g. `brain_video_player.dart`, `option_chip_stack_card.dart`, `canvas_3d.dart`, `brain_canvas*.dart`, `neuron_vector_logo.dart`). **Do not assume — verify each.**

- [ ] **Step 1: List every file the analyzer reports as unused / unreferenced**

Run (from `E:\digitalbrain\UI\flutter`):
```bash
flutter analyze
```
Collect the file paths from `unused_import` / `unused_element` / dead-code warnings. These are *candidates*, not confirmations.

- [ ] **Step 2: For each candidate, confirm zero inbound imports before deleting**

For a candidate file `lib/<path>.dart`, derive its filename and class and grep:
```bash
grep -rl "<filename>.dart" lib/ | grep -v "lib/<path>.dart"
```
- If output is empty → it is safe to `git rm` the file.
- If a survivor (the new canvas, `LiveScreen` and its subtree, the kit, rfw_host, dock, grpc, telemetry, shell, theme, or the kept `visual_constructor_*`) imports it → **keep it**.

> Guardrail: `LiveScreen` (`features/live/live_screen.dart`) is a survivor and it
> imports `features/ino_editor/llm_settings_bus.dart`, the whole of
> `features/live/cards/**`, `features/live/graph/**`, `features/live/timeline/**`,
> `features/live/search/**`, `features/live/tooltip/**`, and `introspector_client.dart`.
> Those must NOT be deleted. Only `ino_editor` files that NOTHING surviving
> imports go.

- [ ] **Step 3: Delete confirmed-orphan files in one batch**

For every file proven orphaned in Step 2:
```bash
git rm UI/flutter/lib/<path>.dart
```

- [ ] **Step 4: Analyze until clean**

Run (from `E:\digitalbrain\UI\flutter`):
```bash
flutter analyze
```
Expected: removing a batch may orphan the next layer (a file only the just-deleted files imported). Repeat Steps 1–3 until `flutter analyze` reports **no unused-import / unused-element warnings introduced by this cut** and **no errors**.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "refactor(flutter): sweep orphaned ino_editor/widget files after cut"
```

---

### Task 9: Remove dead asset/dependency references (if any)

**Files:**
- Possibly modify: `UI/flutter/pubspec.yaml`

- [ ] **Step 1: Check for now-unused package deps**

If Task 8 deleted the only users of a package (e.g. `media_kit` if `brain_video_player.dart` was removed — verify it is not used elsewhere first):
```bash
grep -rl "media_kit" lib/
```
If empty AND `main.dart`'s `MediaKit.ensureInitialized()` is the only remaining reference, you may remove that call and the dep. **If unsure, leave the dependency** — an unused dep is harmless; a wrongly-removed one breaks the build.

- [ ] **Step 2: Analyze + pub get**

Run (from `E:\digitalbrain\UI\flutter`):
```bash
flutter pub get
flutter analyze
```
Expected: no errors.

- [ ] **Step 3: Commit (only if you changed anything)**

```bash
git add UI/flutter/pubspec.yaml UI/flutter/lib/main.dart
git commit -m "chore(flutter): drop dependencies orphaned by the cut"
```

---

### Task 10: Final verification + measure the cut

**Files:** none (verification only)

- [ ] **Step 1: Clean analyze**

Run (from `E:\digitalbrain\UI\flutter`):
```bash
flutter analyze
```
Expected: `No issues found!` (or only the pre-existing baseline from Task 0 Step 3).

- [ ] **Step 2: Release web build succeeds**

Run (from `E:\digitalbrain\UI\flutter`):
```bash
flutter build web --release
```
Expected: build completes without error.

- [ ] **Step 3: Render check**

Rebuild the `flutter-web` Aspire resource; open `http://localhost:5800/` with Playwright. Expected: graph + dock render; submitting a prompt in the dock issues a `submitPrompt` (observe via `mcp__aspire__list_traces` / kernel logs). Check `get_runtime_errors` via `flutter-windows` — release web swallows exceptions and paints blank, so confirm there are none.

- [ ] **Step 4: Backend contract unaffected**

Run (from `E:\digitalbrain`):
```bash
dotnet test
```
Expected: green. The gateway proto + RFW payload contract are untouched by this slice, so `DigitalBrain.E2E.Tests` must still pass. Per the user's standing rule, the Aspire integration tests must be green before this slice is considered done.

- [ ] **Step 5: Measure the cut**

Run (from `E:\digitalbrain\UI\flutter`):
```bash
git ls-files "lib/**/*.dart" | wc -l
```
Compare to the Task 0 baseline (120). Record the delta in the final commit message. Expectation: a reduction on the order of ~20+ files and well over 15,000 lines (the three screens alone are ~14,400 lines).

- [ ] **Step 6: Final commit**

```bash
git commit --allow-empty -m "chore(flutter): S1 cut complete — N files removed, ~M lines deleted"
```
(Replace N and M with the measured numbers.)

---

## Self-review notes (author)

- **Spec §5 coverage:** keep/delete boundary (Tasks 1,4–9), single canvas route (Tasks 2–3), generic RFW host preserved (Task 1 constructs `RfwRuntimeHost`; `rfw_host/**` never deleted), LSP/analyze-governed deletion (every delete task greps + analyzes). ✓
- **Spec §10 S1 exit criteria** ("single canvas route + dock + generic RFW host; delete neuron-specific Dart; green build"): Tasks 1–3 build it, 4–9 delete, 10 proves green. ✓
- **No `flutter test`:** verification is analyze + build + render + `dotnet test` (E2E RFW), per CLAUDE.md. ✓
- **Type consistency:** all reused symbols (`SubmitPromptRequest`, `LiveScreen`, `LiveScreenController`, `FloatingPromptDock`, `SynapseStreamFeed`, `SynapseStreamScope`, `DigitalBrainClientScope`, `RfwRuntimeHost`, `resolveKernelEndpoint`, `createKernelChannel`, `kernelInterceptors`) are verified against current source lines cited in Pre-flight. ✓
- **Risk:** Task 1's `onSynapseEdge` body is a deliberate minimal stub (S2 replaces it). Called out inline so it isn't mistaken for finished edge fan-out.

---

## Follow-on plans (separate specs/plans, not this slice)

Each becomes its own `docs/superpowers/plans/*.md` when reached:

- **S2 — Assist canvas live:** wire `watchHomeFeed` → render incoming `RfwCardEnvelope`s as floating RFW cards on the canvas via `RfwRuntimeHost.ensureLoaded` + `host.render`; real `onSynapseEdge` fan-out into `SynapseStreamFeed`.
- **S3 — Lifecycle synapses (backend):** emit `Neuron.Activated/Deactivated/UnresolvedReference`; canvas reflects state (amber = working).
- **S4 — Operate mode + `+` flow:** ports/wiring on the canvas (reusing `visual_constructor_models.dart` / `visual_constructor_state.dart`), the three-door `+` (new neuron / new synapse / reference SDK) → `NeuronBuilder`, inspector handlers + lifecycle-reaction binding.
- **S5 — Debug drawer:** real synapse stream + payload inspector + replay over the ring buffer + edit-&-fire test synapse.
- **S6 — Composite drill-in:** breadcrumb navigation into a neuron's inner wiring.
- **S7 — Vector search:** embed + query neuron/domain/scenario; `⌘K` + per-door search.
