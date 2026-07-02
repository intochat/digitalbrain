# Telegram Marketplace Setup Fix Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Telegram bot's config form actually appear in the Flutter app after a user installs it from the marketplace, so pasting a bot token and LLM key is possible without an AppHost restart or env var.

**Architecture:** The backend already does everything correctly — the pack is published, installable, and correctly emits a `pack-config-form` `UiSurface` onto the home-feed stream after install. The only gap is that `ForuiAppShell` (`app/lib/shell/forui_app_shell.dart`) stores that surface in its `_surfacesByKind` map but never switches the visible body to show it. The fix adds one small, pure, unit-testable auto-switch function and wires it into the existing `_onCard` handler, following the exact same pattern already used for the UI Kit Gallery auto-switch a few lines above it.

**Tech Stack:** Flutter/Dart (`app/`), `flutter_test` for the unit test. No backend (.NET) changes.

## Global Constraints

- No backend/`brain` code changes — the entire fix is in `app/lib/shell/forui_app_shell.dart` plus one new test file.
- Do not touch the existing gallery auto-switch logic (lines 191-199) — add alongside it, following its shape.
- No new UI screens, routes, or "Channels" concept — reuse the existing marketplace list and the existing generic `ui:*` tree renderer.
- Keep the new logic as a plain top-level pure function (no new class, no state, no gRPC/network mocking) so it's testable without pumping the full widget tree.

---

### Task 1: Auto-switch the shell body to the `pack-config-form` surface when it arrives

**Files:**
- Modify: `app/lib/shell/forui_app_shell.dart` (add a top-level function; call it from `_onCard`, `forui_app_shell.dart:191-199`)
- Create: `app/test/shell/forui_app_shell_test.dart`

**Interfaces:**
- Produces: `String? autoSwitchTargetForKind(String kind)` — a top-level function in `forui_app_shell.dart`. Returns the `_selectedTarget` value the shell should switch to when a surface of the given `kind` arrives, or `null` if that kind shouldn't trigger an auto-switch. Exported (no underscore prefix) specifically so the test file can import and call it directly without needing to pump the full `ForuiAppShell` widget or mock its gRPC connection.

- [ ] **Step 1: Write the failing test**

Create `app/test/shell/forui_app_shell_test.dart`:

```dart
import 'package:flutter_test/flutter_test.dart';
import 'package:digitalbrain_flutter/shell/forui_app_shell.dart';

void main() {
  group('autoSwitchTargetForKind', () {
    test('pack-config-form triggers an auto-switch to itself', () {
      expect(autoSwitchTargetForKind('pack-config-form'), equals('pack-config-form'));
    });

    test('unrelated kinds do not trigger an auto-switch', () {
      expect(autoSwitchTargetForKind('toast'), isNull);
      expect(autoSwitchTargetForKind('installed-bundles'), isNull);
      expect(autoSwitchTargetForKind('marketplace-list'), isNull);
      expect(autoSwitchTargetForKind(''), isNull);
    });
  });
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `cd app && flutter test test/shell/forui_app_shell_test.dart`
Expected: FAIL — `autoSwitchTargetForKind` is not defined in `forui_app_shell.dart` (compile error: "The function 'autoSwitchTargetForKind' isn't defined").

- [ ] **Step 3: Add the top-level function and wire it into `_onCard`**

In `app/lib/shell/forui_app_shell.dart`, add this function directly above the `class _ForuiAppShellState` declaration (i.e. right after the `ForuiAppShell` widget class closes, before line 28's `class _ForuiAppShellState extends State<ForuiAppShell> {`):

```dart
/// Surface kinds that should immediately become the visible shell body the moment
/// they arrive over the home-feed stream, mirroring the existing gallery auto-switch
/// a few lines below. Returns the `_selectedTarget` to switch to, or null if [kind]
/// shouldn't trigger an auto-switch. A plain top-level function (not a method) so it's
/// unit-testable without pumping the full widget tree or mocking the gRPC connection.
String? autoSwitchTargetForKind(String kind) {
  if (kind == 'pack-config-form') return kind;
  return null;
}
```

Then in `_onCard` (inside the `setState(() { ... })` block), add the call immediately after the existing gallery auto-switch block. The method currently reads (lines 190-200):

```dart
      // Auto-switch to the dynamic gallery when its surface arrives (from marketplace open).
      // This ensures the components tree renders instead of staying on loading or market fallback.
      final isGallery = kind == 'ui-kit-gallery' ||
          (data['galleryPack'] as String? ?? '') == 'DigitalBrain.UI.Gallery' ||
          (data['galleryExperienceId'] as String? ?? '') == 'ui-kit-gallery' ||
          (data['pack'] as String? ?? '') == 'DigitalBrain.UI.Gallery';
      if (isGallery) {
        _selectedTarget = 'ui-kit-gallery';
      }
    });
  }
```

Change it to (adding the new block between the gallery `if` and the closing `});`):

```dart
      // Auto-switch to the dynamic gallery when its surface arrives (from marketplace open).
      // This ensures the components tree renders instead of staying on loading or market fallback.
      final isGallery = kind == 'ui-kit-gallery' ||
          (data['galleryPack'] as String? ?? '') == 'DigitalBrain.UI.Gallery' ||
          (data['galleryExperienceId'] as String? ?? '') == 'ui-kit-gallery' ||
          (data['pack'] as String? ?? '') == 'DigitalBrain.UI.Gallery';
      if (isGallery) {
        _selectedTarget = 'ui-kit-gallery';
      }

      // Auto-switch to a pack's config form the moment it's emitted post-install
      // (e.g. Telegram bot token + LLM provider/key), instead of leaving it sitting
      // unseen in _surfacesByKind.
      final autoSwitchTarget = autoSwitchTargetForKind(kind);
      if (autoSwitchTarget != null) {
        _selectedTarget = autoSwitchTarget;
      }
    });
  }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `cd app && flutter test test/shell/forui_app_shell_test.dart`
Expected: PASS — both tests in the `autoSwitchTargetForKind` group succeed.

- [ ] **Step 5: Run the full verification ritual**

Even though this change is Flutter-only, run the full ritual (per this repo's standing convention) to catch any accidental regression:

```
cd app && flutter analyze && flutter test
cd brain && dotnet build Brain.slnx
cd brain && dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "Telegram|Config"
```
Then use the aspire MCP `doctor` tool (or `aspire doctor` from `brain/`) to confirm the resource graph is healthy.

Expected: `flutter analyze` reports no new issues; full Flutter test suite passes (existing `config_form_tree_test.dart` and all others remain green — this change doesn't touch any code they exercise); the untouched backend build and Telegram/Config-filtered tests stay green; `aspire doctor` reports no new issues.

- [ ] **Step 6: Commit**

```bash
git -C app add lib/shell/forui_app_shell.dart test/shell/forui_app_shell_test.dart
git -C app commit -m "fix(shell): auto-switch to pack-config-form surface when it arrives

The backend already correctly emits a ConfigFormSurface after a pack
with RequiredConfig is installed (e.g. the Telegram bot), but the
shell never switched the visible body to show it - it just sat in
_surfacesByKind unseen. Mirrors the existing gallery auto-switch."
```

- [ ] **Step 7: Manual end-to-end verification**

This is the one part no automated test covers end-to-end (it requires a real Telegram bot token). Run:

```
cd brain && aspire run
```

Then in the running Flutter client:
1. Open the marketplace list and find the `DigitalBrain.Telegram.Responder` tile (description starts with "Telegram bot responder").
2. Tap **Install**.
3. Confirm the view automatically switches to a form titled "DigitalBrain.Telegram.Responder configuration" with three fields: a secret "Bot token" field, a "LLM" choice field, and a secret "API key" field that appears only when the LLM choice is "openai".
4. Enter a real bot token from [@BotFather](https://t.me/BotFather) (or a throwaway test bot), choose `ollama` as the provider (no API key needed), tap **Save**.
5. Confirm a toast/notification appears indicating the pack configured successfully (`PackConfigured`).
6. Message the bot on Telegram and confirm it replies.

Expected: all six steps succeed with no manual env var / Aspire parameter / restart involved — this is the exact loop the spec (`brain/docs/specs/2026-07-02-telegram-marketplace-miniapp-design.md`) targets. If step 3 fails (form doesn't appear), re-check Step 3's edit landed in the running build (hot-reload/hot-restart the Flutter client). If step 6 fails but steps 1-5 succeed, that's a backend issue outside this plan's scope (this plan only fixes surface visibility) — capture the kernel logs and treat as a new bug, not a regression of this fix.

No code changes in this step — it's a checklist, not a task with a deliverable to commit.
