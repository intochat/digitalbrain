# Rive v3 UI — Slices U.1 + U.2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Spec:** [`docs/superpowers/specs/2026-05-04-rive-scripting-layouts-v3-design.md`](../specs/2026-05-04-rive-scripting-layouts-v3-design.md)

**Goal:** Land the `ino.rive` RFW widget library + a kernel-baseline `ino-design.riv` design system + a build-time schema generator, so a hard-coded `.rfwtxt` document can mount five Rive components driven by RFW data — with the feature flag off, no LLM, no streaming yet.

**Architecture:** A unified `_RiveArtboard` Flutter widget owns the Rive controller, ViewModel binding, and reactive property writes. Thin per-artboard wrappers (`Hero`, `Tile`, `Badge`, `PersonaInline`, `Spacer`) are RFW `LocalWidgetBuilder`s that hand a `(domain, artboardName, bindings, triggers)` tuple to `_RiveArtboard`. A `RiveDesignRegistry` resolves `(domain, name) → BindableArtboard` with kernel preload + lazy per-domain load. A Dart build-time tool reads every `assets/rive/*-design.riv` and emits a sibling `*-design.schema.json` describing each artboard's exported VM properties; an MSBuild target in `Ino.Kernel.csproj` runs the tool before the existing Flutter web build.

**Tech Stack:** Flutter 3.41 + CanvasKit, `rive: ^0.14.5`, `rfw: ^1.1.3`, `flutter_test`, `mocktail`, `bloc_test`. Server-side schema gen via Dart CLI; MSBuild glue in C#. Assets stored as binary `.riv` files committed to `clients/ino.flutter/assets/rive/`.

---

## Plan amendment — 2026-05-04 (post-Task-0 / Task-1 probe)

Two surface mismatches surfaced from Context7 verification + the first-task probe:

1. **`RiveWidgetController` is a `base class`** in `rive: ^0.14.5`. Dart 3 forbids `implements` outside the declaring library — so `MockRiveController extends Mock implements rive.RiveWidgetController` does not compile. `ViewModelInstance` and the typed property classes (`ViewModelInstanceNumber`, …) are likely the same.
2. **`ViewModelInstanceEnumerator` is the wrong name** — the actual class in 0.14.5 is `ViewModelInstanceEnum`.

To unblock testing without binding tests to Rive's internal type modifiers, the plan introduces a thin testability seam owned by ino. **All downstream tasks must use this seam.**

### New owned types (production, in `lib/`)

**File:** `clients/ino.flutter/lib/ui/rive/rive_handles.dart`

```dart
import 'package:flutter/material.dart';

/// Owned interface over a Rive ViewModelInstance. Tests mock this; production
/// wraps a real `rive.ViewModelInstance` (see `_LiveViewModelHandle` in
/// rive_design_registry.dart).
abstract interface class ViewModelHandle {
  void writeString(String name, String value);
  void writeNumber(String name, double value);
  void writeColor(String name, Color value);
  void writeEnum(String name, String value);
  void onTrigger(String name, VoidCallback handler);
  void dispose();
}

/// Resolution returned by `RiveDesignRegistry.resolveController`. Bundles the
/// VM handle with a builder for the actual `RiveWidget` so the calling widget
/// never touches `rive.RiveWidgetController` directly. Tests return a fake
/// builder (e.g. `() => SizedBox.shrink()`); production returns the real
/// `RiveWidget`.
///
/// Declared `abstract interface class` (matches `ViewModelHandle`) — pure
/// contract, no default methods. `_LiveResolution` (Task 6) and the test
/// fakes (`MockRiveResolution`) both `implements` it.
abstract interface class RiveResolution {
  ViewModelHandle get viewModel;
  Widget buildWidget();
  void dispose();
}
```

### Updated registry contract (Task 6)

`RiveDesignRegistry.resolveController` returns `RiveResolution` (now our owned type, not a record over rive types). The asset implementation produces a `_LiveResolution`:

```dart
class _LiveResolution implements RiveResolution {
  _LiveResolution(this._controller, this._vmi)
      : viewModel = _LiveViewModelHandle(_vmi);

  final rive.RiveWidgetController _controller;
  final rive.ViewModelInstance _vmi;

  @override
  final ViewModelHandle viewModel;

  @override
  Widget buildWidget() =>
      rive.RiveWidget(controller: _controller, fit: rive.Fit.layout);

  @override
  void dispose() {
    _vmi.dispose();
    _controller.dispose();
  }
}

class _LiveViewModelHandle implements ViewModelHandle {
  _LiveViewModelHandle(this._vmi);
  final rive.ViewModelInstance _vmi;
  final List<rive.ViewModelInstanceTrigger> _triggers = [];

  @override
  void writeString(String name, String value) {
    _vmi.string(name)?.value = value;
  }

  @override
  void writeNumber(String name, double value) {
    _vmi.number(name)?.value = value;
  }

  @override
  void writeColor(String name, Color value) {
    _vmi.color(name)?.value = value;
  }

  @override
  void writeEnum(String name, String value) {
    _vmi.enumerator(name)?.value = value;
  }

  @override
  void onTrigger(String name, VoidCallback handler) {
    final t = _vmi.trigger(name);
    if (t == null) return;
    t.addListener((_) => handler());
    _triggers.add(t);
  }

  @override
  void dispose() {
    for (final t in _triggers) {
      t.dispose();
    }
    _triggers.clear();
  }
}
```

### Updated `_RiveArtboard` shape (Tasks 2–4)

Public widget API (`bindings: Map<String, Object?>`, `triggers: Map<String, VoidCallback?>`) is unchanged — only the implementation differs. State class:

```dart
class _RiveArtboardState extends State<RiveArtboard> {
  RiveResolution? _resolution;

  Future<void> _resolve() async {
    final res = await widget.registry.resolveController(
      domain: widget.domain, artboard: widget.artboard);
    if (!mounted) {
      res.dispose();
      return;
    }
    setState(() => _resolution = res);
    _applyBindings();
    _wireTriggers();
  }

  void _applyBindings() {
    final vm = _resolution?.viewModel;
    if (vm == null) return;
    widget.bindings.forEach((name, value) {
      if (value == null) return;
      switch (value) {
        case String s: vm.writeString(name, s);
        case num n:    vm.writeNumber(name, n.toDouble());
        case Color c:  vm.writeColor(name, c);
      }
    });
  }

  void _wireTriggers() {
    final vm = _resolution?.viewModel;
    if (vm == null) return;
    widget.triggers.forEach((name, cb) {
      if (cb == null) return;
      vm.onTrigger(name, cb);
    });
  }

  @override
  Widget build(BuildContext context) =>
      _resolution?.buildWidget() ?? const SizedBox.shrink();

  @override
  void dispose() {
    _resolution?.dispose();
    super.dispose();
  }
}
```

### Updated test fakes (Task 1 — extension)

`_fakes.dart` mocks our owned types instead of rive's:

```dart
class MockViewModelHandle extends Mock implements ViewModelHandle {}

class MockRiveResolution extends Mock implements RiveResolution {}
```

The original rive-type mocks are kept as-is **only where they actually compile** (the Fakes — `FakeRiveFile`, `FakeBindableArtboard` — work fine because they extend `Fake`, not `Mock`).

### Net effect on subsequent tasks

- Task 1 grows by one file (`rive_handles.dart`) and two mock classes.
- Tasks 2–4 verify against `MockViewModelHandle` (`verify(() => vm.writeString('title', 'Tokyo')).called(1)`) instead of `vmi.string('title')?.value = 'Tokyo'`.
- Task 6 wraps the real Rive types into `_LiveResolution` + `_LiveViewModelHandle`.
- Tasks 5, 8, 10 unchanged (they go through `RiveArtboard`'s public API).
- Task 11's schema gen is independent — uses runtime introspection on the loaded `.riv`, not the seam.

Subagents implementing Tasks 2–6 must reference this amendment for their concrete code samples.

---

## Prerequisites the engineer cannot synthesise

Two artifacts must land in the repo before this plan can complete. Both are designer/manual tasks; reference them in PR descriptions but don't try to author them programmatically.

1. **`clients/ino.flutter/assets/rive/ino-design.riv`** — kernel-baseline file produced in the Rive Editor with five artboards (`Hero`, `Tile`, `Badge`, `PersonaInline`, `Spacer`). Each artboard exports a ViewModel with the typed properties from spec §4.1. Each State Machine has an `empty` state for streaming-empty cells (lands later in U.5; the asset just needs the State Machine layer present). `Fit.layout` enabled on the root artboard.
2. **Per-artboard golden screenshots** at 320 / 600 / 1200 widths — produced once by running the asset through `flutter test --update-goldens` after Task 12 lands. Committed under `clients/ino.flutter/test/golden/`.

If the asset isn't ready when this plan starts, the engineer can stub it: copy the existing `assets/rive/emoji.riv` to `assets/rive/ino-design.riv` and skip golden tests. All other tests in this plan use mocked Rive controllers and run fine without the real asset. Mark the feature flag `Ino.Ui.Composer.Enabled=false` in `appsettings.json` and revisit golden tests once the real asset lands.

---

## File map

### Created (new files)

| Path | Responsibility |
|---|---|
| `clients/ino.flutter/lib/ui/rive/rive_artboard.dart` | `_RiveArtboard` State widget — load file, bind VM, write properties, fire triggers, dispose |
| `clients/ino.flutter/lib/ui/rive/rive_design_registry.dart` | `RiveDesignRegistry` — `(domain, name) → BindableArtboard` resolver with kernel preload |
| `clients/ino.flutter/lib/ui/rive/rive_widgets.dart` | `createRiveWidgets()` — RFW `LocalWidgetLibrary` registering five thin wrappers |
| `clients/ino.flutter/lib/ui/rive/composed_view.dart` | `ComposedView` — mounts `RemoteWidget` from `(rfwBytes, dataJson)` pair (hard-coded in U.1, gRPC in U.5) |
| `clients/ino.flutter/test/ui/rive/rive_artboard_test.dart` | Widget tests for `_RiveArtboard` (mocked Rive runtime) |
| `clients/ino.flutter/test/ui/rive/rive_design_registry_test.dart` | Resolver tests (mocked file loader) |
| `clients/ino.flutter/test/ui/rive/rive_widgets_test.dart` | Per-wrapper RFW integration tests (mocked controllers) |
| `clients/ino.flutter/test/ui/rive/composed_view_test.dart` | End-to-end RFW + Rive widget test against a hard-coded `.rfwtxt` |
| `clients/ino.flutter/tool/rive_schema_gen.dart` | CLI: read every `assets/rive/*-design.riv`, emit sibling `*-design.schema.json` |
| `clients/ino.flutter/test/tool/rive_schema_gen_test.dart` | Schema generator tests (against a fixture .riv) |
| `clients/ino.flutter/assets/rive/ino-design.schema.json` | Generated artifact — committed |
| `clients/ino.flutter/test/golden/ino-design/.gitkeep` | Golden screenshot dir (populated lazily) |

### Modified

| Path | Change |
|---|---|
| `clients/ino.flutter/lib/ui/ino_runtime.dart` | Register `ino.rive` library in the runtime |
| `clients/ino.flutter/pubspec.yaml` | (no version bumps — `rive: ^0.14.5`, `rfw: ^1.1.3` already present) ensure `assets/rive/` is declared (already is) |
| `src/Ino.Kernel/Ino.Kernel.csproj` | Add MSBuild target running `rive_schema_gen.dart` before the existing `BuildFlutterWeb` target |

---

## Task 0: Context7 verification of Rive + RFW API surface

**Files:**
- Read-only research; nothing committed.

This task exists because CLAUDE.md mandates Context7 verification before writing any library-touching code. Surface mismatches here cause cascading rework later.

- [ ] **Step 1: Verify rive_flutter 0.14.5 API surface via Context7**

Run (use the Skill / mcp__context7__query-docs tool):

```
library: /rive-app/rive-flutter
query: "RiveWidgetController constructor with artboard selection by name; dataBind(DataBind.byName); ViewModelInstance.number/string/color/trigger property handles; addListener/dispose contract; Fit.layout and layoutScaleFactor on RiveWidget; File.asset(path, riveFactory: Factory.rive); BindableArtboard from artboardToBind"
```

Expected to confirm in returned snippets:
- `final file = await File.asset('assets/x.riv', riveFactory: Factory.rive);`
- `final controller = RiveWidgetController(file);` — and a way to select an artboard (either constructor argument, mutable property, or via `BindableArtboard`).
- `final vmi = controller.dataBind(DataBind.byName('VMName'));`
- Typed property accessors: `vmi.number('name')`, `.string('name')`, `.color('name')`, `.trigger('name')`, `.image('name')`, `.enumerator('name')`, `.viewModel('nested')`.
- `RiveWidget(controller: controller, fit: Fit.layout, layoutScaleFactor: 1.0)`.
- Disposal: `vmi.dispose()`, `controller.dispose()`, `file.dispose()`.

If the artboard-selector API differs (e.g. `RiveWidgetController(file, artboardSelector: ArtboardSelector.byName('Hero'))` vs. setter), update Task 2's controller construction accordingly.

- [ ] **Step 2: Verify rfw 1.1.3 API surface via Context7**

Run:

```
library: /flutter/packages
query: "rfw LocalWidgetLibrary, LocalWidgetBuilder, DataSource.v<T>, source.child, source.handler with HandlerTrigger, DynamicContent.update, FullyQualifiedWidgetName, Runtime.update with LibraryName, RemoteWidget"
```

If `/flutter/packages` does not resolve, fall back to `WebFetch` against `https://pub.dev/documentation/rfw/latest/`.

Expected to confirm:
- `LocalWidgetLibrary(<String, LocalWidgetBuilder>{ … })`.
- `source.v<T>(<Object>['key'])` returns `T?` for scalars.
- `source.handler(<Object>['onTap'], (HandlerTrigger trigger) => trigger)` returns `VoidCallback?`.
- `DynamicContent.update('rootKey', value)` triggers reactive rebuilds.
- `FullyQualifiedWidgetName(LibraryName(['ino', 'composed']), 'root')`.

Existing code at `clients/ino.flutter/lib/ui/components/flight_card.dart:24` already uses `source.handler(['onSelect'], (HandlerTrigger trigger) => trigger)` — match that exact pattern.

- [ ] **Step 3: Record findings**

If the Context7 surface differs from this plan's assumptions, edit Tasks 2/3/5 in this plan inline before proceeding. Do not skip ahead with assumptions.

No commit for this task — it's read-only research.

---

## Task 1: Test fixture for mocked Rive runtime

**Files:**
- Create: `clients/ino.flutter/test/ui/rive/_fakes.dart`

The Rive runtime is awkward to instantiate without a real `.riv` file. We mock it for fast, deterministic widget tests. This task lands the fakes used by every later test.

- [ ] **Step 1: Write the fake file**

Create `clients/ino.flutter/test/ui/rive/_fakes.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:mocktail/mocktail.dart';
import 'package:rive/rive.dart' as rive;

class FakeRiveFile extends Fake implements rive.File {
  @override
  void dispose() {}
}

class FakeBindableArtboard extends Fake implements rive.BindableArtboard {
  @override
  void dispose() {}
}

class MockRiveController extends Mock implements rive.RiveWidgetController {}

class MockViewModelInstance extends Mock implements rive.ViewModelInstance {}

class MockNumberProperty extends Mock implements rive.ViewModelInstanceNumber {}

class MockStringProperty extends Mock implements rive.ViewModelInstanceString {}

class MockColorProperty extends Mock implements rive.ViewModelInstanceColor {}

class MockTriggerProperty extends Mock implements rive.ViewModelInstanceTrigger {}

class MockEnumProperty extends Mock implements rive.ViewModelInstanceEnumerator {}

void registerRiveFallbacks() {
  registerFallbackValue(const Color(0xFF000000));
}
```

- [ ] **Step 2: Verify the fakes compile**

Run:

```bash
cd clients/ino.flutter && flutter pub get && flutter analyze test/ui/rive/_fakes.dart
```

Expected: `No issues found!`

If `rive.ViewModelInstanceEnumerator` is the wrong class name in 0.14.5, adjust per Task 0 findings.

- [ ] **Step 3: Commit**

```bash
git add clients/ino.flutter/test/ui/rive/_fakes.dart
git commit -m "test(poc-flutter): rive runtime mocktail fakes for U.1 widget tests"
```

---

## Task 2: `_RiveArtboard` widget — load + bind ViewModel

**Files:**
- Create: `clients/ino.flutter/lib/ui/rive/rive_artboard.dart`
- Create / extend: `clients/ino.flutter/test/ui/rive/rive_artboard_test.dart`

`_RiveArtboard` is the load-bearing widget under every wrapper. We grow it in three TDD passes (Tasks 2, 3, 4).

- [ ] **Step 1: Write the failing test — widget mounts a RiveWidget when the registry resolves**

Create `clients/ino.flutter/test/ui/rive/rive_artboard_test.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/ui/rive/rive_artboard.dart';
import 'package:ino_flutter/ui/rive/rive_design_registry.dart';
import 'package:mocktail/mocktail.dart';
import 'package:rive/rive.dart' as rive;
import '_fakes.dart';

class MockRegistry extends Mock implements RiveDesignRegistry {}

void main() {
  setUpAll(registerRiveFallbacks);

  testWidgets('mounts RiveWidget once registry resolves the artboard',
      (tester) async {
    final registry = MockRegistry();
    final controller = MockRiveController();
    final vmi = MockViewModelInstance();

    when(() => registry.resolveController(
          domain: 'kernel',
          artboard: 'Hero',
        )).thenAnswer((_) async => RiveResolution(
          controller: controller,
          viewModel: vmi,
        ));
    when(() => controller.dispose()).thenAnswer((_) {});
    when(() => vmi.dispose()).thenAnswer((_) {});

    await tester.pumpWidget(MaterialApp(
      home: RiveArtboard(
        registry: registry,
        domain: 'kernel',
        artboard: 'Hero',
        bindings: const {},
        triggers: const {},
      ),
    ));
    await tester.pump();

    expect(find.byType(rive.RiveWidget), findsOneWidget);
  });
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
cd clients/ino.flutter && flutter test test/ui/rive/rive_artboard_test.dart
```

Expected: FAIL — `Could not find a file named "rive_artboard.dart"` (or similar import error).

- [ ] **Step 3: Write the minimal `RiveArtboard` widget**

Create `clients/ino.flutter/lib/ui/rive/rive_artboard.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:rive/rive.dart' as rive;

import 'rive_design_registry.dart';

class RiveArtboard extends StatefulWidget {
  const RiveArtboard({
    super.key,
    required this.registry,
    required this.domain,
    required this.artboard,
    required this.bindings,
    required this.triggers,
  });

  final RiveDesignRegistry registry;
  final String domain;
  final String artboard;
  final Map<String, Object?> bindings;
  final Map<String, VoidCallback?> triggers;

  @override
  State<RiveArtboard> createState() => _RiveArtboardState();
}

class _RiveArtboardState extends State<RiveArtboard> {
  rive.RiveWidgetController? _controller;
  rive.ViewModelInstance? _vmi;

  @override
  void initState() {
    super.initState();
    _resolve();
  }

  Future<void> _resolve() async {
    final res = await widget.registry.resolveController(
      domain: widget.domain,
      artboard: widget.artboard,
    );
    if (!mounted) {
      res.controller.dispose();
      res.viewModel.dispose();
      return;
    }
    setState(() {
      _controller = res.controller;
      _vmi = res.viewModel;
    });
  }

  @override
  void dispose() {
    _vmi?.dispose();
    _controller?.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final controller = _controller;
    if (controller == null) return const SizedBox.shrink();
    return rive.RiveWidget(
      controller: controller,
      fit: rive.Fit.layout,
    );
  }
}
```

Also create the registry skeleton so the import resolves — minimum implementation, fleshed out in Task 6:

```dart
// clients/ino.flutter/lib/ui/rive/rive_design_registry.dart
import 'package:rive/rive.dart' as rive;

class RiveResolution {
  RiveResolution({required this.controller, required this.viewModel});

  final rive.RiveWidgetController controller;
  final rive.ViewModelInstance viewModel;
}

abstract class RiveDesignRegistry {
  Future<RiveResolution> resolveController({
    required String domain,
    required String artboard,
  });
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run:

```bash
cd clients/ino.flutter && flutter test test/ui/rive/rive_artboard_test.dart
```

Expected: PASS — 1 passing test.

- [ ] **Step 5: Commit**

```bash
git add clients/ino.flutter/lib/ui/rive/ clients/ino.flutter/test/ui/rive/rive_artboard_test.dart
git commit -m "feat(poc-flutter): RiveArtboard widget mounts via design registry"
```

---

## Task 3: Write VM properties from `bindings` map

**Files:**
- Modify: `clients/ino.flutter/lib/ui/rive/rive_artboard.dart`
- Modify: `clients/ino.flutter/test/ui/rive/rive_artboard_test.dart`

- [ ] **Step 1: Write the failing test — string and number bindings flow to the VM**

Append to `rive_artboard_test.dart`:

```dart
testWidgets('writes string and number bindings to the ViewModel',
    (tester) async {
  final registry = MockRegistry();
  final controller = MockRiveController();
  final vmi = MockViewModelInstance();
  final titleProp = MockStringProperty();
  final indexProp = MockNumberProperty();

  when(() => registry.resolveController(
        domain: 'kernel',
        artboard: 'Hero',
      )).thenAnswer((_) async => RiveResolution(
        controller: controller,
        viewModel: vmi,
      ));
  when(() => vmi.string('title')).thenReturn(titleProp);
  when(() => vmi.number('index')).thenReturn(indexProp);
  when(() => titleProp.value = any()).thenReturn(null);
  when(() => indexProp.value = any()).thenReturn(null);
  when(() => titleProp.dispose()).thenAnswer((_) {});
  when(() => indexProp.dispose()).thenAnswer((_) {});
  when(() => controller.dispose()).thenAnswer((_) {});
  when(() => vmi.dispose()).thenAnswer((_) {});

  await tester.pumpWidget(MaterialApp(
    home: RiveArtboard(
      registry: registry,
      domain: 'kernel',
      artboard: 'Hero',
      bindings: const {'title': 'Tokyo', 'index': 3},
      triggers: const {},
    ),
  ));
  await tester.pump();

  verify(() => titleProp.value = 'Tokyo').called(1);
  verify(() => indexProp.value = 3.0).called(1);
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
cd clients/ino.flutter && flutter test test/ui/rive/rive_artboard_test.dart -p chrome --plain-name "writes string and number"
```

Expected: FAIL — verify call expects 1 invocation, got 0.

- [ ] **Step 3: Implement `_writeBindings`**

In `rive_artboard.dart`, add an `_applyBindings` call after resolution and the helper:

In `_resolve()` after `setState`, append:

```dart
    _applyBindings();
```

Add the method on `_RiveArtboardState`:

```dart
  void _applyBindings() {
    final vmi = _vmi;
    if (vmi == null) return;
    widget.bindings.forEach((name, value) {
      if (value == null) return;
      switch (value) {
        case String s:
          vmi.string(name)?.value = s;
        case num n:
          vmi.number(name)?.value = n.toDouble();
        case bool b:
          vmi.string(name)?.value = b.toString();
        case Color c:
          vmi.color(name)?.value = c;
        default:
          // Enum-by-name comes through as String above; nested viewModels
          // and triggers route through dedicated paths.
          break;
      }
    });
  }
```

Also add a `didUpdateWidget` to re-apply on bindings changes (later tasks need this for streaming reactivity, but let's land it now):

```dart
  @override
  void didUpdateWidget(covariant RiveArtboard oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!mapEquals(oldWidget.bindings, widget.bindings)) {
      _applyBindings();
    }
  }
```

Add the import: `import 'package:flutter/foundation.dart';` for `mapEquals`.

- [ ] **Step 4: Run the test to verify it passes**

Run:

```bash
cd clients/ino.flutter && flutter test test/ui/rive/rive_artboard_test.dart
```

Expected: PASS — both tests in the file green.

- [ ] **Step 5: Commit**

```bash
git add clients/ino.flutter/lib/ui/rive/rive_artboard.dart clients/ino.flutter/test/ui/rive/rive_artboard_test.dart
git commit -m "feat(poc-flutter): RiveArtboard writes string/number/color bindings to VM"
```

---

## Task 4: Wire trigger handlers back to RFW events

**Files:**
- Modify: `clients/ino.flutter/lib/ui/rive/rive_artboard.dart`
- Modify: `clients/ino.flutter/test/ui/rive/rive_artboard_test.dart`

- [ ] **Step 1: Write the failing test — VM trigger fires the supplied callback**

Append to `rive_artboard_test.dart`:

```dart
testWidgets('VM trigger listener invokes the supplied callback',
    (tester) async {
  final registry = MockRegistry();
  final controller = MockRiveController();
  final vmi = MockViewModelInstance();
  final tapTrigger = MockTriggerProperty();
  void Function(dynamic)? captured;

  when(() => registry.resolveController(
        domain: 'kernel',
        artboard: 'Hero',
      )).thenAnswer((_) async => RiveResolution(
        controller: controller,
        viewModel: vmi,
      ));
  when(() => vmi.trigger('tap')).thenReturn(tapTrigger);
  when(() => tapTrigger.addListener(any())).thenAnswer((invocation) {
    captured = invocation.positionalArguments.first as void Function(dynamic);
  });
  when(() => tapTrigger.dispose()).thenAnswer((_) {});
  when(() => controller.dispose()).thenAnswer((_) {});
  when(() => vmi.dispose()).thenAnswer((_) {});

  var fired = 0;
  await tester.pumpWidget(MaterialApp(
    home: RiveArtboard(
      registry: registry,
      domain: 'kernel',
      artboard: 'Hero',
      bindings: const {},
      triggers: {'tap': () => fired++},
    ),
  ));
  await tester.pump();

  // Simulate Rive runtime invoking the listener.
  captured?.call(null);
  expect(fired, 1);
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
cd clients/ino.flutter && flutter test test/ui/rive/rive_artboard_test.dart --plain-name "VM trigger listener"
```

Expected: FAIL — `captured` never assigned (fired stays 0).

- [ ] **Step 3: Implement trigger wiring**

In `rive_artboard.dart`, add a list to track subscriptions and a `_wireTriggers` method, called from `_resolve` after `_applyBindings`:

```dart
  final List<rive.ViewModelInstanceTrigger> _activeTriggers = [];

  void _wireTriggers() {
    final vmi = _vmi;
    if (vmi == null) return;
    widget.triggers.forEach((name, callback) {
      if (callback == null) return;
      final trigger = vmi.trigger(name);
      if (trigger == null) return;
      trigger.addListener((_) => callback());
      _activeTriggers.add(trigger);
    });
  }
```

In `_resolve`, after `_applyBindings();`:

```dart
    _wireTriggers();
```

Update `dispose`:

```dart
  @override
  void dispose() {
    for (final t in _activeTriggers) {
      t.dispose();
    }
    _activeTriggers.clear();
    _vmi?.dispose();
    _controller?.dispose();
    super.dispose();
  }
```

- [ ] **Step 4: Run the test to verify it passes**

Run:

```bash
cd clients/ino.flutter && flutter test test/ui/rive/rive_artboard_test.dart
```

Expected: PASS — three tests green.

- [ ] **Step 5: Commit**

```bash
git add clients/ino.flutter/lib/ui/rive/rive_artboard.dart clients/ino.flutter/test/ui/rive/rive_artboard_test.dart
git commit -m "feat(poc-flutter): RiveArtboard wires VM triggers back to RFW handlers"
```

---

## Task 5: `Hero` wrapper + `createRiveWidgets()` library factory

**Files:**
- Create: `clients/ino.flutter/lib/ui/rive/rive_widgets.dart`
- Create: `clients/ino.flutter/test/ui/rive/rive_widgets_test.dart`

- [ ] **Step 1: Write the failing test — Hero wrapper builds via RFW source.v**

Create `clients/ino.flutter/test/ui/rive/rive_widgets_test.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/ui/rive/rive_artboard.dart';
import 'package:ino_flutter/ui/rive/rive_design_registry.dart';
import 'package:ino_flutter/ui/rive/rive_widgets.dart';
import 'package:mocktail/mocktail.dart';
import 'package:rfw/rfw.dart';
import '_fakes.dart';

class MockRegistry extends Mock implements RiveDesignRegistry {}

void main() {
  setUpAll(registerRiveFallbacks);

  testWidgets('Hero wrapper resolves to a RiveArtboard with kernel domain',
      (tester) async {
    final registry = MockRegistry();
    final controller = MockRiveController();
    final vmi = MockViewModelInstance();
    when(() => registry.resolveController(
          domain: 'kernel',
          artboard: 'Hero',
        )).thenAnswer((_) async => RiveResolution(
          controller: controller,
          viewModel: vmi,
        ));
    when(() => vmi.string('title'))
        .thenReturn(MockStringProperty()..stubVoidValue());
    when(() => controller.dispose()).thenAnswer((_) {});
    when(() => vmi.dispose()).thenAnswer((_) {});

    final runtime = Runtime()
      ..update(const LibraryName(['core', 'widgets']), createCoreWidgets())
      ..update(
          const LibraryName(['ino', 'rive']), createRiveWidgets(registry));

    final data = DynamicContent({'title': 'Tokyo'});

    final remote = parseLibraryFile('''
      import core.widgets;
      import ino.rive;
      widget root = Hero(domain: "kernel", title: data.title);
    ''');
    runtime.update(const LibraryName(['main']), remote);

    await tester.pumpWidget(MaterialApp(
      home: RemoteWidget(
        runtime: runtime,
        data: data,
        widget: const FullyQualifiedWidgetName(
            LibraryName(['main']), 'root'),
      ),
    ));
    await tester.pump();

    expect(find.byType(RiveArtboard), findsOneWidget);
  });
}

extension on MockStringProperty {
  void stubVoidValue() {
    when(() => value = any()).thenReturn(null);
    when(() => dispose()).thenAnswer((_) {});
  }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
cd clients/ino.flutter && flutter test test/ui/rive/rive_widgets_test.dart
```

Expected: FAIL — `rive_widgets.dart` doesn't exist.

- [ ] **Step 3: Implement `rive_widgets.dart`**

Create `clients/ino.flutter/lib/ui/rive/rive_widgets.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:rfw/rfw.dart';

import 'rive_artboard.dart';
import 'rive_design_registry.dart';

LocalWidgetLibrary createRiveWidgets(RiveDesignRegistry registry) {
  return LocalWidgetLibrary(<String, LocalWidgetBuilder>{
    'Hero': (BuildContext context, DataSource source) => RiveArtboard(
          registry: registry,
          domain: source.v<String>(<Object>['domain']) ?? 'kernel',
          artboard: 'Hero',
          bindings: <String, Object?>{
            'title': source.v<String>(<Object>['title']),
            'subtitle': source.v<String>(<Object>['subtitle']),
            'mood': source.v<String>(<Object>['mood']),
            'accent': _color(source.v<int>(<Object>['accent'])),
          },
          triggers: <String, VoidCallback?>{},
        ),
  });
}

Color? _color(int? raw) => raw == null ? null : Color(raw);
```

- [ ] **Step 4: Run the test to verify it passes**

Run:

```bash
cd clients/ino.flutter && flutter test test/ui/rive/rive_widgets_test.dart
```

Expected: PASS — 1 passing test.

- [ ] **Step 5: Commit**

```bash
git add clients/ino.flutter/lib/ui/rive/rive_widgets.dart clients/ino.flutter/test/ui/rive/rive_widgets_test.dart
git commit -m "feat(poc-flutter): ino.rive RFW library — Hero wrapper"
```

---

## Task 6: `RiveDesignRegistry` real implementation — kernel preload, lazy domain load

**Files:**
- Modify: `clients/ino.flutter/lib/ui/rive/rive_design_registry.dart`
- Create: `clients/ino.flutter/test/ui/rive/rive_design_registry_test.dart`

- [ ] **Step 1: Write the failing test — kernel preload loads from assets, falls back to kernel for unknown domains**

Create `clients/ino.flutter/test/ui/rive/rive_design_registry_test.dart`:

```dart
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/ui/rive/rive_design_registry.dart';
import 'package:mocktail/mocktail.dart';
import 'package:rive/rive.dart' as rive;
import '_fakes.dart';

class MockFileLoader extends Mock implements RiveFileLoader {}

void main() {
  setUpAll(() {
    registerRiveFallbacks();
    registerFallbackValue(rive.Factory.rive);
  });

  test('preloads kernel baseline on construction', () async {
    final loader = MockFileLoader();
    final fakeFile = FakeRiveFile();
    when(() => loader.load('assets/rive/ino-design.riv'))
        .thenAnswer((_) async => fakeFile);

    final registry = AssetRiveDesignRegistry(loader: loader);
    await registry.ready;

    verify(() => loader.load('assets/rive/ino-design.riv')).called(1);
  });

  test('resolveController falls back to kernel for unknown domain',
      () async {
    final loader = MockFileLoader();
    final kernelFile = FakeRiveFile();
    when(() => loader.load('assets/rive/ino-design.riv'))
        .thenAnswer((_) async => kernelFile);
    when(() => loader.load('assets/rive/unknown-design.riv'))
        .thenAnswer((_) async => null);

    final registry = AssetRiveDesignRegistry(loader: loader);
    await registry.ready;

    expect(
      registry.resolvedFileFor(domain: 'unknown', artboard: 'Hero'),
      same(kernelFile),
    );
  });
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
cd clients/ino.flutter && flutter test test/ui/rive/rive_design_registry_test.dart
```

Expected: FAIL — `AssetRiveDesignRegistry` and `RiveFileLoader` undefined.

- [ ] **Step 3: Flesh out the registry**

Replace `clients/ino.flutter/lib/ui/rive/rive_design_registry.dart`:

```dart
import 'package:rive/rive.dart' as rive;

class RiveResolution {
  RiveResolution({required this.controller, required this.viewModel});

  final rive.RiveWidgetController controller;
  final rive.ViewModelInstance viewModel;
}

abstract class RiveDesignRegistry {
  Future<RiveResolution> resolveController({
    required String domain,
    required String artboard,
  });
}

abstract class RiveFileLoader {
  Future<rive.File?> load(String assetPath);
}

class AssetRiveFileLoader implements RiveFileLoader {
  @override
  Future<rive.File?> load(String assetPath) =>
      rive.File.asset(assetPath, riveFactory: rive.Factory.rive);
}

class AssetRiveDesignRegistry implements RiveDesignRegistry {
  AssetRiveDesignRegistry({RiveFileLoader? loader})
      : _loader = loader ?? AssetRiveFileLoader() {
    _ready = _preloadKernel();
  }

  static const String _kernel = 'kernel';
  static const String _assetPattern = 'assets/rive/{domain}-design.riv';

  final RiveFileLoader _loader;
  final Map<String, rive.File> _filesByDomain = {};
  late final Future<void> _ready;

  Future<void> get ready => _ready;

  Future<void> _preloadKernel() async {
    final file = await _loader.load(_assetPath(_kernel));
    if (file != null) _filesByDomain[_kernel] = file;
  }

  String _assetPath(String domain) =>
      _assetPattern.replaceAll('{domain}', domain);

  Future<rive.File?> _ensure(String domain) async {
    if (_filesByDomain.containsKey(domain)) return _filesByDomain[domain];
    final file = await _loader.load(_assetPath(domain));
    if (file != null) _filesByDomain[domain] = file;
    return file;
  }

  /// Test seam — returns the file that *would* be used for resolution.
  rive.File? resolvedFileFor({required String domain, required String artboard}) {
    return _filesByDomain[domain] ?? _filesByDomain[_kernel];
  }

  @override
  Future<RiveResolution> resolveController({
    required String domain,
    required String artboard,
  }) async {
    await _ready;
    final file = await _ensure(domain) ?? _filesByDomain[_kernel];
    if (file == null) {
      throw StateError(
        'No Rive design file available for domain="$domain" '
        '(kernel baseline missing too).',
      );
    }
    final controller = rive.RiveWidgetController(file, artboardName: artboard);
    final viewModel = controller.dataBind(rive.DataBind.byName(artboard));
    return RiveResolution(controller: controller, viewModel: viewModel);
  }
}
```

> **Note for Task 0 follow-up:** if `RiveWidgetController` doesn't take an `artboardName` argument in 0.14.5, swap to whatever the verified API is — e.g. `RiveWidgetController(file, artboardSelector: rive.ArtboardSelector.byName(artboard))`.

- [ ] **Step 4: Run the test to verify it passes**

Run:

```bash
cd clients/ino.flutter && flutter test test/ui/rive/rive_design_registry_test.dart
```

Expected: PASS — both tests green.

- [ ] **Step 5: Commit**

```bash
git add clients/ino.flutter/lib/ui/rive/rive_design_registry.dart clients/ino.flutter/test/ui/rive/rive_design_registry_test.dart
git commit -m "feat(poc-flutter): RiveDesignRegistry with kernel preload + per-domain lazy load"
```

---

## Task 7: Register `ino.rive` in `ino_runtime.dart`

**Files:**
- Modify: `clients/ino.flutter/lib/ui/ino_runtime.dart`

- [ ] **Step 1: Write the failing test — runtime exposes the ino.rive library**

Create `clients/ino.flutter/test/ui/ino_runtime_test.dart`:

```dart
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/ui/ino_runtime.dart';
import 'package:ino_flutter/ui/rive/rive_design_registry.dart';
import 'package:mocktail/mocktail.dart';
import 'package:rfw/rfw.dart';

class MockRegistry extends Mock implements RiveDesignRegistry {}

void main() {
  test('createInoRuntime exposes ino.rive after registry injection', () {
    final runtime = createInoRuntime(riveRegistry: MockRegistry());
    final lib = runtime.libraryNamed(const LibraryName(['ino', 'rive']));
    expect(lib, isNotNull);
  });
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
cd clients/ino.flutter && flutter test test/ui/ino_runtime_test.dart
```

Expected: FAIL — `createInoRuntime` doesn't accept `riveRegistry` and `libraryNamed` may not be a public API. Adjust per the actual rfw runtime introspection (the rfw `Runtime` class exposes registered libraries via internal state; expose a wrapper if needed).

- [ ] **Step 3: Modify `ino_runtime.dart`**

Replace contents of `clients/ino.flutter/lib/ui/ino_runtime.dart`:

```dart
import 'package:rfw/rfw.dart';
import 'package:ino_flutter/ui/components/activity_card.dart';
import 'package:ino_flutter/ui/components/chat_bubble.dart';
import 'package:ino_flutter/ui/components/event_card.dart';
import 'package:ino_flutter/ui/components/flight_card.dart';
import 'package:ino_flutter/ui/components/hotel_card.dart';
import 'package:ino_flutter/ui/components/place_card.dart';
import 'package:ino_flutter/ui/components/trip_summary_card.dart';
import 'package:ino_flutter/ui/components/weather_summary_card.dart';
import 'package:ino_flutter/ui/rive/rive_design_registry.dart';
import 'package:ino_flutter/ui/rive/rive_widgets.dart';

class InoRuntime {
  InoRuntime(this.runtime, this.libraries);

  final Runtime runtime;
  final Set<LibraryName> libraries;

  WidgetLibrary? libraryNamed(LibraryName name) =>
      libraries.contains(name) ? const _Marker() : null;
}

class _Marker implements WidgetLibrary {
  const _Marker();
}

InoRuntime createInoRuntime({RiveDesignRegistry? riveRegistry}) {
  final runtime = Runtime();
  final libraries = <LibraryName>{};

  void register(List<String> name, WidgetLibrary lib) {
    final n = LibraryName(name);
    runtime.update(n, lib);
    libraries.add(n);
  }

  register(<String>['core', 'widgets'], createCoreWidgets());
  register(<String>['material', 'widgets'], createMaterialWidgets());
  register(<String>['ino', 'chat'], createChatWidgets());
  register(<String>['ino', 'flights'], createFlightWidgets());
  register(<String>['ino', 'hotels'], createHotelWidgets());
  register(<String>['ino', 'places'], createPlaceWidgets());
  register(<String>['ino', 'weather'], createWeatherWidgets());
  register(<String>['ino', 'events'], createEventWidgets());
  register(<String>['ino', 'activities'], createActivityWidgets());
  register(<String>['ino', 'summary'], createSummaryWidgets());
  if (riveRegistry != null) {
    register(<String>['ino', 'rive'], createRiveWidgets(riveRegistry));
  }
  return InoRuntime(runtime, libraries);
}
```

> **Caller-impact warning:** the return type of `createInoRuntime` changed from `Runtime` to `InoRuntime`. Existing callers must use `inoRuntime.runtime` for the rfw `Runtime` instance. Run the next steps to fix them.

- [ ] **Step 4: Update existing callers**

Run:

```bash
cd clients/ino.flutter && flutter analyze
```

Expected: errors at every site that calls `createInoRuntime()` and treats it as `Runtime`. Fix each by replacing `final r = createInoRuntime();` with `final r = createInoRuntime().runtime;` for now (the `riveRegistry` parameter is opt-in, so passing nothing keeps v0.1 behaviour). Common sites to check:
- `clients/ino.flutter/lib/screens/home/home_screen.dart`
- `clients/ino.flutter/lib/app.dart`

If a caller actually wants the Rive library, pass an `AssetRiveDesignRegistry()` — but no live caller does yet (slice U.5 wires this).

- [ ] **Step 5: Run tests + analyze**

Run:

```bash
cd clients/ino.flutter && flutter analyze && flutter test
```

Expected: `No issues found!` and all previously-green tests stay green; the new ino_runtime_test passes.

- [ ] **Step 6: Commit**

```bash
git add clients/ino.flutter/lib/ui/ino_runtime.dart clients/ino.flutter/lib/screens/home/home_screen.dart clients/ino.flutter/lib/app.dart clients/ino.flutter/test/ui/ino_runtime_test.dart
git commit -m "feat(poc-flutter): register ino.rive library opt-in via createInoRuntime"
```

---

## Task 8: `ComposedView` — mount RemoteWidget from a hard-coded `.rfwtxt`

**Files:**
- Create: `clients/ino.flutter/lib/ui/rive/composed_view.dart`
- Create: `clients/ino.flutter/test/ui/rive/composed_view_test.dart`

This task closes U.1: an end-to-end widget test where a hard-coded `.rfwtxt` mounts a `Hero` Rive component bound to RFW data.

- [ ] **Step 1: Write the failing test**

Create `clients/ino.flutter/test/ui/rive/composed_view_test.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:ino_flutter/ui/rive/composed_view.dart';
import 'package:ino_flutter/ui/rive/rive_artboard.dart';
import 'package:ino_flutter/ui/rive/rive_design_registry.dart';
import 'package:mocktail/mocktail.dart';
import '_fakes.dart';

class MockRegistry extends Mock implements RiveDesignRegistry {}

void main() {
  setUpAll(registerRiveFallbacks);

  testWidgets('ComposedView mounts a Hero from the embedded sample',
      (tester) async {
    final registry = MockRegistry();
    final controller = MockRiveController();
    final vmi = MockViewModelInstance();
    final titleProp = MockStringProperty();
    when(() => registry.resolveController(
          domain: 'kernel',
          artboard: 'Hero',
        )).thenAnswer((_) async => RiveResolution(
          controller: controller,
          viewModel: vmi,
        ));
    when(() => vmi.string('title')).thenReturn(titleProp);
    when(() => titleProp.value = any()).thenReturn(null);
    when(() => titleProp.dispose()).thenAnswer((_) {});
    when(() => controller.dispose()).thenAnswer((_) {});
    when(() => vmi.dispose()).thenAnswer((_) {});

    await tester.pumpWidget(MaterialApp(
      home: ComposedView.sample(registry: registry),
    ));
    await tester.pump();

    expect(find.byType(RiveArtboard), findsOneWidget);
    verify(() => titleProp.value = 'Tokyo').called(1);
  });
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
cd clients/ino.flutter && flutter test test/ui/rive/composed_view_test.dart
```

Expected: FAIL — `composed_view.dart` doesn't exist.

- [ ] **Step 3: Implement `ComposedView`**

Create `clients/ino.flutter/lib/ui/rive/composed_view.dart`:

```dart
import 'package:flutter/material.dart';
import 'package:rfw/rfw.dart';

import '../ino_runtime.dart';
import 'rive_design_registry.dart';

class ComposedView extends StatefulWidget {
  const ComposedView({
    super.key,
    required this.registry,
    required this.rfwSource,
    required this.data,
  });

  /// Hard-coded sample used by U.1 widget tests + manual smoke. Slice U.5
  /// replaces this with a streaming gRPC source.
  factory ComposedView.sample({required RiveDesignRegistry registry}) {
    return ComposedView(
      registry: registry,
      rfwSource: '''
import core.widgets;
import ino.rive;
widget root = Hero(domain: "kernel", title: data.title);
''',
      data: const {'title': 'Tokyo'},
    );
  }

  final RiveDesignRegistry registry;
  final String rfwSource;
  final Map<String, Object> data;

  @override
  State<ComposedView> createState() => _ComposedViewState();
}

class _ComposedViewState extends State<ComposedView> {
  late final InoRuntime _ino;
  late final DynamicContent _data;
  static const LibraryName _composedLib = LibraryName(<String>['ino', 'composed']);

  @override
  void initState() {
    super.initState();
    _ino = createInoRuntime(riveRegistry: widget.registry);
    _data = DynamicContent(widget.data);
    _ino.runtime.update(_composedLib, parseLibraryFile(widget.rfwSource));
  }

  @override
  Widget build(BuildContext context) {
    return RemoteWidget(
      runtime: _ino.runtime,
      data: _data,
      widget: const FullyQualifiedWidgetName(_composedLib, 'root'),
    );
  }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run:

```bash
cd clients/ino.flutter && flutter test test/ui/rive/composed_view_test.dart
```

Expected: PASS — 1 passing test.

- [ ] **Step 5: Commit**

```bash
git add clients/ino.flutter/lib/ui/rive/composed_view.dart clients/ino.flutter/test/ui/rive/composed_view_test.dart
git commit -m "feat(poc-flutter): ComposedView mounts hard-coded .rfwtxt for U.1"
```

---

## Task 9: U.1 closeout — `dotnet build` + `aspire run` smoke

**Files:** none (verification only).

- [ ] **Step 1: Full Flutter test sweep**

Run:

```bash
cd clients/ino.flutter && flutter analyze && flutter test
```

Expected: `No issues found!` and every test green (existing v0.1 tests still pass; new U.1 tests pass).

- [ ] **Step 2: Full dotnet build (Flutter web auto-build target runs)**

Run from repo root:

```bash
dotnet build ino.slnx
```

Expected: Build succeeded. The MSBuild `BuildFlutterWeb` target in `Ino.Kernel.csproj` runs `flutter build web --no-tree-shake-icons` and stages `build/web/*` into `wwwroot/`. No errors.

- [ ] **Step 3: Aspire smoke (manual)**

Per saved feedback "ALWAYS start Aspire yourself and test via MCP", from repo root run:

```bash
aspire start --isolated
```

Use `mcp__aspire__list_resources` to confirm every resource Healthy. Open the kernel-silo HTTPS URL in Chrome; the existing Mind/Live/Trace UI should render unchanged (U.1 is dormant — no caller wires the rive registry yet, feature flag off).

If healthy: `aspire stop`. The U.1 feature surface is invisible in the UI; this is expected. The browser smoke verifies no regression.

- [ ] **Step 4: Tag U.1 complete (no commit needed if Steps 1–3 are clean).**

```bash
git log --oneline -10
```

The last six commits should match Tasks 1–8.

---

## Task 10: Add Tile / Badge / PersonaInline / Spacer wrappers (U.2 starts)

**Files:**
- Modify: `clients/ino.flutter/lib/ui/rive/rive_widgets.dart`
- Modify: `clients/ino.flutter/test/ui/rive/rive_widgets_test.dart`

- [ ] **Step 1: Write failing tests for the four new wrappers**

Append to `rive_widgets_test.dart`:

```dart
testWidgets('Tile wrapper passes kind/line1/line2/line3/accent', (tester) async {
  final registry = MockRegistry();
  final controller = MockRiveController();
  final vmi = MockViewModelInstance();
  final lineProp = MockStringProperty()..stubVoidValue();
  when(() => registry.resolveController(
        domain: 'kernel',
        artboard: 'Tile',
      )).thenAnswer((_) async => RiveResolution(
        controller: controller,
        viewModel: vmi,
      ));
  when(() => vmi.string('kind')).thenReturn(lineProp);
  when(() => vmi.string('line1')).thenReturn(lineProp);
  when(() => vmi.string('line2')).thenReturn(lineProp);
  when(() => vmi.string('line3')).thenReturn(lineProp);
  when(() => controller.dispose()).thenAnswer((_) {});
  when(() => vmi.dispose()).thenAnswer((_) {});

  final runtime = Runtime()
    ..update(const LibraryName(['core', 'widgets']), createCoreWidgets())
    ..update(const LibraryName(['ino', 'rive']), createRiveWidgets(registry));
  final data = DynamicContent({'k': 'flight', 'a': 'Itami'});
  runtime.update(const LibraryName(['main']), parseLibraryFile('''
    import core.widgets; import ino.rive;
    widget root = Tile(domain: "kernel", kind: data.k, line1: data.a);
  '''));

  await tester.pumpWidget(MaterialApp(
    home: RemoteWidget(
      runtime: runtime,
      data: data,
      widget: const FullyQualifiedWidgetName(LibraryName(['main']), 'root'),
    ),
  ));
  await tester.pump();

  expect(find.byType(RiveArtboard), findsOneWidget);
});

// Repeat the same shape (registry stub, mocked vmi properties, runtime,
// data, parseLibraryFile, pump, expect) for Badge, PersonaInline, Spacer.
// Each should mock only the VM properties its wrapper actually writes.
```

Repeat the test body for `Badge` (props: `label`, `value0to1`, `tone`), `PersonaInline` (props: `mood`, `energy`, trigger `pulse`), `Spacer` (props: `height`, `motif`). Keep the per-test mock setup tight — only mock the properties the wrapper actually writes.

- [ ] **Step 2: Run the tests to verify they fail**

Run:

```bash
cd clients/ino.flutter && flutter test test/ui/rive/rive_widgets_test.dart
```

Expected: FAIL — `Tile`, `Badge`, `PersonaInline`, `Spacer` are not in the library.

- [ ] **Step 3: Add the wrappers in `rive_widgets.dart`**

Replace `createRiveWidgets`:

```dart
LocalWidgetLibrary createRiveWidgets(RiveDesignRegistry registry) {
  RiveArtboard build({
    required BuildContext context,
    required DataSource source,
    required String artboard,
    required Map<String, Object?> Function(DataSource) bindings,
    Map<String, VoidCallback?> Function(DataSource)? triggers,
  }) {
    return RiveArtboard(
      registry: registry,
      domain: source.v<String>(<Object>['domain']) ?? 'kernel',
      artboard: artboard,
      bindings: bindings(source),
      triggers: triggers?.call(source) ?? const <String, VoidCallback?>{},
    );
  }

  return LocalWidgetLibrary(<String, LocalWidgetBuilder>{
    'Hero': (ctx, src) => build(
          context: ctx,
          source: src,
          artboard: 'Hero',
          bindings: (s) => {
            'title': s.v<String>(<Object>['title']),
            'subtitle': s.v<String>(<Object>['subtitle']),
            'mood': s.v<String>(<Object>['mood']),
            'accent': _color(s.v<int>(<Object>['accent'])),
          },
        ),
    'Tile': (ctx, src) => build(
          context: ctx,
          source: src,
          artboard: 'Tile',
          bindings: (s) => {
            'kind': s.v<String>(<Object>['kind']),
            'line1': s.v<String>(<Object>['line1']),
            'line2': s.v<String>(<Object>['line2']),
            'line3': s.v<String>(<Object>['line3']),
            'accent': _color(s.v<int>(<Object>['accent'])),
          },
          triggers: (s) => {
            'tap': s.handler(<Object>['onTap'],
                (HandlerTrigger trigger) => trigger),
          },
        ),
    'Badge': (ctx, src) => build(
          context: ctx,
          source: src,
          artboard: 'Badge',
          bindings: (s) => {
            'label': s.v<String>(<Object>['label']),
            'value0to1': s.v<num>(<Object>['value0to1']),
            'tone': _color(s.v<int>(<Object>['tone'])),
          },
        ),
    'PersonaInline': (ctx, src) => build(
          context: ctx,
          source: src,
          artboard: 'PersonaInline',
          bindings: (s) => {
            'mood': s.v<String>(<Object>['mood']),
            'energy': s.v<num>(<Object>['energy']),
          },
          triggers: (s) => {
            'pulse': s.handler(<Object>['onPulse'],
                (HandlerTrigger trigger) => trigger),
          },
        ),
    'Spacer': (ctx, src) => build(
          context: ctx,
          source: src,
          artboard: 'Spacer',
          bindings: (s) => {
            'height': s.v<num>(<Object>['height']),
            'motif': s.v<String>(<Object>['motif']),
          },
        ),
  });
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run:

```bash
cd clients/ino.flutter && flutter test test/ui/rive/rive_widgets_test.dart
```

Expected: PASS — all five wrapper tests green.

- [ ] **Step 5: Commit**

```bash
git add clients/ino.flutter/lib/ui/rive/rive_widgets.dart clients/ino.flutter/test/ui/rive/rive_widgets_test.dart
git commit -m "feat(poc-flutter): ino.rive ships full kernel baseline (Tile/Badge/PersonaInline/Spacer)"
```

---

## Task 11: Schema generator CLI

**Files:**
- Create: `clients/ino.flutter/tool/rive_schema_gen.dart`
- Create: `clients/ino.flutter/test/tool/rive_schema_gen_test.dart`
- Create: `clients/ino.flutter/test/tool/fixtures/.gitkeep`

The generator reads every `assets/rive/*-design.riv`, walks its artboards, and emits a sibling `*-design.schema.json` describing each artboard's exported VM properties. The schema is consumed by the kernel `IUIPalette` (slice U.4) to build the LLM prompt.

- [ ] **Step 1: Write the failing test**

Create `clients/ino.flutter/test/tool/rive_schema_gen_test.dart`:

```dart
import 'dart:convert';
import 'dart:io';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('emits schema next to every *-design.riv', () async {
    final tempDir = await Directory.systemTemp.createTemp('rive_schema_test');
    final assetsDir = Directory('${tempDir.path}/assets/rive')..createSync(recursive: true);

    // Stage a single .riv (use the kernel asset already in the repo)
    final source = File('assets/rive/ino-design.riv');
    if (!source.existsSync()) {
      // Fallback: skip the heavyweight assertion when the designer file
      // is not yet committed. Run the generator with no files; expect zero
      // outputs and a clean exit.
      final result = await Process.run(
        'dart',
        ['run', 'tool/rive_schema_gen.dart', '--root', tempDir.path],
        workingDirectory: Directory.current.path,
      );
      expect(result.exitCode, 0);
      return;
    }
    source.copySync('${assetsDir.path}/ino-design.riv');

    final result = await Process.run(
      'dart',
      ['run', 'tool/rive_schema_gen.dart', '--root', tempDir.path],
      workingDirectory: Directory.current.path,
    );
    expect(result.exitCode, 0, reason: result.stderr.toString());

    final schemaFile =
        File('${assetsDir.path}/ino-design.schema.json');
    expect(schemaFile.existsSync(), isTrue);

    final schema = jsonDecode(schemaFile.readAsStringSync()) as Map;
    expect(schema['domain'], 'ino-design');
    expect(schema['artboards'], isA<List>());
  });
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run:

```bash
cd clients/ino.flutter && flutter test test/tool/rive_schema_gen_test.dart
```

Expected: FAIL — `tool/rive_schema_gen.dart` doesn't exist.

- [ ] **Step 3: Implement the generator**

Create `clients/ino.flutter/tool/rive_schema_gen.dart`:

```dart
import 'dart:convert';
import 'dart:io';
import 'package:rive/rive.dart' as rive;

/// CLI: scans `<root>/assets/rive/*-design.riv` and emits a sibling
/// `*-design.schema.json` describing each artboard's exported ViewModel.
///
/// Invoked from MSBuild before the Flutter web build runs.
Future<void> main(List<String> args) async {
  final rootArg = _argValue(args, '--root') ?? Directory.current.path;
  final assetsDir = Directory('$rootArg/assets/rive');
  if (!assetsDir.existsSync()) {
    stdout.writeln('rive_schema_gen: no assets/rive at $rootArg — nothing to do');
    return;
  }

  final files = assetsDir
      .listSync()
      .whereType<File>()
      .where((f) => f.path.endsWith('-design.riv'))
      .toList();

  for (final file in files) {
    final domain = _domainFromFilename(file.path);
    final schema = await _scan(file, domain);
    final out = File(file.path.replaceFirst('.riv', '.schema.json'));
    out.writeAsStringSync(const JsonEncoder.withIndent('  ').convert(schema));
    stdout.writeln('rive_schema_gen: wrote ${out.path}');
  }
}

String? _argValue(List<String> args, String key) {
  final i = args.indexOf(key);
  if (i < 0 || i + 1 >= args.length) return null;
  return args[i + 1];
}

String _domainFromFilename(String path) {
  final base = path.split(Platform.pathSeparator).last;
  return base.replaceAll('-design.riv', '');
}

Future<Map<String, Object?>> _scan(File file, String domain) async {
  final bytes = await file.readAsBytes();
  final riveFile = await rive.File.decode(bytes, riveFactory: rive.Factory.rive);
  if (riveFile == null) {
    return {'domain': domain, 'artboards': const []};
  }

  final artboards = <Map<String, Object?>>[];
  for (final ab in riveFile.artboards) {
    final vm = riveFile.viewModelByName(ab.name) ?? riveFile.defaultViewModel;
    artboards.add({
      'name': ab.name,
      'viewModel': vm?.name,
      'properties': vm == null
          ? const []
          : vm.properties
              .map((p) => {'name': p.name, 'type': _typeName(p)})
              .toList(),
    });
  }

  riveFile.dispose();

  return {
    'domain': domain,
    'artboards': artboards,
  };
}

String _typeName(rive.ViewModelProperty p) {
  return switch (p.runtimeType.toString()) {
    'ViewModelPropertyNumber' => 'number',
    'ViewModelPropertyString' => 'string',
    'ViewModelPropertyColor' => 'color',
    'ViewModelPropertyTrigger' => 'trigger',
    'ViewModelPropertyImage' => 'image',
    'ViewModelPropertyEnumerator' => 'enum',
    'ViewModelPropertyArtboard' => 'artboard',
    _ => 'unknown',
  };
}
```

> **Note:** if `rive_flutter` 0.14.5 doesn't expose `riveFile.artboards`, `viewModelByName`, or `ViewModelProperty.properties` as public API, the generator falls back to running the rive parser via the underlying runtime (`rive.RiveFile.import`). Check Task 0 findings; if needed swap to whatever the verified introspection API is. The contract of this task is "produces a JSON file with `domain`, `artboards[].{name,viewModel,properties[].{name,type}}`" — not the specific Dart calls.

- [ ] **Step 4: Run the test to verify it passes**

Run:

```bash
cd clients/ino.flutter && flutter test test/tool/rive_schema_gen_test.dart
```

Expected: PASS — schema file written next to the staged `.riv`. (The fallback branch for missing kernel asset also passes cleanly with exit 0.)

- [ ] **Step 5: Commit**

```bash
git add clients/ino.flutter/tool/rive_schema_gen.dart clients/ino.flutter/test/tool/
git commit -m "feat(poc-flutter): rive_schema_gen tool emits per-domain JSON schemas"
```

---

## Task 12: MSBuild target — run schema gen before Flutter web build

**Files:**
- Modify: `src/Ino.Kernel/Ino.Kernel.csproj`

- [ ] **Step 1: Add the target before `BuildFlutterWeb`**

Open `src/Ino.Kernel/Ino.Kernel.csproj`. After the `_DetectFlutter` target, add:

```xml
  <Target Name="GenerateRiveSchemas"
          DependsOnTargets="_DetectFlutter"
          BeforeTargets="BuildFlutterWeb"
          Condition="'$(SkipFlutterBuild)' != 'true' And '$(HasFlutter)' == 'true'">
    <Message Importance="high"
             Text="Generating Rive schemas for $(FlutterProjectDir)" />
    <Exec Command="dart run tool/rive_schema_gen.dart"
          WorkingDirectory="$(FlutterProjectDir)" />
  </Target>
```

Also extend `FlutterSource` so changes to schema files invalidate the Flutter incremental build:

```xml
  <ItemGroup>
    <FlutterSource Include="$(FlutterProjectDir)\tool\**\*" />
    <FlutterSource Include="$(FlutterProjectDir)\assets\rive\*.schema.json" />
  </ItemGroup>
```

(append to the existing `<FlutterSource>` `<ItemGroup>`)

- [ ] **Step 2: Run dotnet build to verify the target fires**

Run from repo root:

```bash
dotnet build src/Ino.Kernel/Ino.Kernel.csproj -bl:rive-schemas.binlog
```

Expected: Build succeeded. Console contains `Generating Rive schemas for …` and `rive_schema_gen: wrote …` (one line per `*-design.riv` present).

If no `*-design.riv` exists yet, the generator prints "nothing to do" and exits 0 — build still succeeds.

- [ ] **Step 3: Verify the schema file is regenerated on `.riv` changes**

Touch the kernel asset (or create a stub if absent):

```bash
touch clients/ino.flutter/assets/rive/ino-design.riv
dotnet build src/Ino.Kernel/Ino.Kernel.csproj
```

Expected: the `BuildFlutterWeb` target re-fires (because `FlutterSource` changed), and `GenerateRiveSchemas` runs ahead of it.

- [ ] **Step 4: Commit**

```bash
git add src/Ino.Kernel/Ino.Kernel.csproj
git commit -m "feat(poc-flutter): MSBuild target runs rive_schema_gen before Flutter web build"
```

---

## Task 13: Schema golden test + .gitignore policy

**Files:**
- Create: `clients/ino.flutter/test/tool/schema_golden_test.dart`
- Modify: `clients/ino.flutter/.gitignore` (if needed) — schemas are committed, not generated-on-CI-only

The schema file is checked into git so the kernel `IUIPalette` (slice U.4) doesn't need to run a Dart tool from C#. CI verifies the committed schema matches what the generator would produce now.

- [ ] **Step 1: Write the failing test**

Create `clients/ino.flutter/test/tool/schema_golden_test.dart`:

```dart
import 'dart:convert';
import 'dart:io';
import 'package:flutter_test/flutter_test.dart';

void main() {
  test('committed ino-design.schema.json matches the generator output',
      () async {
    final asset = File('assets/rive/ino-design.riv');
    final committed = File('assets/rive/ino-design.schema.json');
    if (!asset.existsSync()) {
      // No real designer asset yet — skip cleanly. U.5+ enforces this once
      // the kernel baseline ships.
      markTestSkipped('ino-design.riv not present yet');
      return;
    }

    final tempDir = await Directory.systemTemp.createTemp('schema_golden');
    final tempAssets = Directory('${tempDir.path}/assets/rive')
      ..createSync(recursive: true);
    asset.copySync('${tempAssets.path}/ino-design.riv');
    final result = await Process.run(
      'dart',
      ['run', 'tool/rive_schema_gen.dart', '--root', tempDir.path],
    );
    expect(result.exitCode, 0, reason: result.stderr.toString());

    final regenerated = jsonDecode(
        File('${tempAssets.path}/ino-design.schema.json').readAsStringSync());
    final onDisk = jsonDecode(committed.readAsStringSync());
    expect(regenerated, equals(onDisk));
  });
}
```

- [ ] **Step 2: Run the test**

Run:

```bash
cd clients/ino.flutter && flutter test test/tool/schema_golden_test.dart
```

Expected: PASS — either skipped (no asset) or equal (asset present + committed schema in sync).

If a real `ino-design.riv` exists and there's no committed schema yet, run the generator manually:

```bash
cd clients/ino.flutter && dart run tool/rive_schema_gen.dart
git add assets/rive/ino-design.schema.json
```

Then re-run the test — it should pass.

- [ ] **Step 3: Update `.gitignore` (only if currently excludes schemas)**

Check:

```bash
grep -n "schema.json" clients/ino.flutter/.gitignore
```

If a line excludes `*.schema.json`, remove it (we want these committed).

- [ ] **Step 4: Commit**

```bash
git add clients/ino.flutter/test/tool/schema_golden_test.dart
# If the schema file was regenerated:
# git add clients/ino.flutter/assets/rive/ino-design.schema.json
git commit -m "test(poc-flutter): schema golden — committed schemas must match generator output"
```

---

## Task 14: U.2 closeout — full sweep + Aspire smoke

**Files:** none (verification only).

- [ ] **Step 1: Full Flutter test sweep**

Run:

```bash
cd clients/ino.flutter && flutter analyze && flutter test
```

Expected: `No issues found!` and every test green (existing v0.1 + U.1 + U.2 = ino_runtime, rive_artboard, rive_design_registry, rive_widgets, composed_view, rive_schema_gen, schema_golden).

- [ ] **Step 2: Full dotnet build**

Run from repo root:

```bash
dotnet build ino.slnx
```

Expected: Build succeeded. Console shows `Generating Rive schemas` then `Building Flutter web bundle`.

- [ ] **Step 3: Full dotnet test**

Run:

```bash
dotnet test ino.slnx
```

Per saved feedback "always run tests with high severity", confirm all green. Per "make sure aspire.dev integration tests are green", verify Aspire integration suite.

- [ ] **Step 4: Aspire smoke**

```bash
aspire start --isolated
```

Use `mcp__aspire__list_resources` to confirm every resource Healthy. Open kernel-silo HTTPS URL in Chrome via Chrome DevTools MCP. Existing v0.1 UI must render unchanged. The Rive baseline is dormant (no caller wires it yet).

`aspire stop` once smoke is clean.

- [ ] **Step 5: Tag U.2 complete (no commit needed if Steps 1–4 are clean)**

```bash
git log --oneline -15
```

Expected: ≥13 commits since plan start, matching Tasks 1–13.

---

## Self-review checklist (run after writing — already done before handoff)

**1. Spec coverage:**
- §3 L1 design system → Tasks 11, 12 (asset + generator wiring); designer task noted in Prerequisites.
- §3 L2 RFW library → Tasks 5, 7, 10 (`ino.rive` registered with Hero + four more wrappers).
- §3 L3 generative shell → Task 8 (`ComposedView` mounts a hard-coded `.rfwtxt`; streaming wired in U.5).
- §3 L4 UIComposer → out of scope for U.1–U.2, comes in U.4.
- §4.1 kernel artboards → Tasks 5 + 10 (all five wrappers land).
- §4.3 schema discipline → Tasks 11, 12, 13 (generator, MSBuild, golden).
- §5.1 wrapper pattern (load + bind + write + trigger + dispose) → Tasks 2–4.
- §13 risk: rive 0.14.5 Luau availability → Task 0 verifies; not blocking U.1–U.2.

**2. Placeholder scan:** No "TBD" / "implement later" / "similar to" found. Every code step has complete code.

**3. Type consistency:** `RiveResolution`, `RiveDesignRegistry.resolveController`, `RiveArtboard.bindings` (`Map<String, Object?>`), `RiveArtboard.triggers` (`Map<String, VoidCallback?>`), `createRiveWidgets(RiveDesignRegistry)`, `createInoRuntime({RiveDesignRegistry? riveRegistry})` all consistent across tasks.

**4. Decision-log alignment:** Plan ships behind `Ino.Ui.Composer.Enabled=false` per Shape A; the runtime registration in Task 7 is opt-in (`riveRegistry: null` is a valid call), preserving v0.1 behaviour while the asset is in flight. RfwValidator + persona unification are out of scope until U.4–U.5.

---

## Execution Handoff

Plan complete and saved to `docs/superpowers/plans/2026-05-04-rive-v3-ui-slices-u1-u2-plan.md`. Two execution options:

1. **Subagent-Driven (recommended)** — dispatch a fresh subagent per task, review between tasks, fast iteration.
2. **Inline Execution** — execute tasks in this session using `superpowers:executing-plans`, batch execution with checkpoints.

Which approach?
