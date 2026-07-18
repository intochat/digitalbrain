# Slice 1 — Demo Button Strip Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a one-tap "test button strip" above the chat composer in the Flutter app so demos and manual smoke tests don't require typing canned prompts.

**Architecture:** Pure client-side change. Six chips dispatched through the existing `ino_bloc.SendMessage` event. No backend, no proto, no gRPC stubs touched. Strip is mounted in `home_screen.dart` directly above `_InputBar` (line 232), gated by a top-level Dart const flag.

**Tech Stack:** Flutter 3.41+, Material 3, flutter_bloc, existing `InoBloc.SendMessage` event.

**Spec reference:** `docs/superpowers/specs/2026-05-02-phase4-epilogue-design.md` § Slice 1.

---

## File structure

| File | Action | Responsibility |
|---|---|---|
| `clients/ino.flutter/lib/ui/components/test_button_strip.dart` | Create | The widget — six chips + L1 tap-counter state machine |
| `clients/ino.flutter/lib/screens/home/home_screen.dart` | Modify (insert at line 232 boundary, import top) | Mount the strip above `_InputBar`, gate via `kShowDemoButtons` const |

The strip is a `StatefulWidget` because the L1 trigger button needs a tap counter and a stable session-scoped cluster key. Everything else is stateless.

---

## Task 1 — Create the demo button strip widget

**Files:**
- Create: `clients/ino.flutter/lib/ui/components/test_button_strip.dart`

- [ ] **Step 1: Create the file with the strip widget**

```dart
// Demo-only test button strip. Sits above the chat composer so manual smoke
// tests don't require typing the same canned prompts repeatedly.
//
// Visibility gated by `kShowDemoButtons` in home_screen.dart. Flip to false
// before v0.1 ship.
//
// Post-Slice-3 behavior: when IExperienceRegistry.ApprovalRequired=true, the
// L1 trigger button's 4th tap will land unrouted (the proposal sits Pending
// in the Inspector). The user opens the Inspector, approves, and re-taps the
// button to see the routed response. This is the intended demo flow once
// gating ships.

import 'package:flutter/material.dart';
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/state/ino_bloc.dart';

class TestButtonStrip extends StatefulWidget {
  const TestButtonStrip({super.key, this.onShowInspector});

  /// Optional callback to open the inspector drawer. The "Show last routing"
  /// chip calls this when set; otherwise the chip is a no-op.
  final VoidCallback? onShowInspector;

  @override
  State<TestButtonStrip> createState() => _TestButtonStripState();
}

class _TestButtonStripState extends State<TestButtonStrip> {
  // L1 cluster key is session-scoped: all four taps use the same string so
  // MissedIntentTracker.NormalizeForCluster actually clusters them. Re-tap
  // after the 4th does nothing — reload the page to re-arm.
  late final String _l1ClusterKey =
      'demo l1 marker ${DateTime.now().microsecondsSinceEpoch.toRadixString(36).substring(0, 8)}';
  int _l1Taps = 0;

  void _send(String text) {
    context.read<InoBloc>().add(SendMessage(text));
  }

  void _onL1Tap() {
    if (_l1Taps < 3) {
      setState(() => _l1Taps++);
      _send(_l1ClusterKey);
    } else if (_l1Taps == 3) {
      setState(() => _l1Taps++);
      // Pause 1 s so the user sees the 3rd unrouted reply before the 4th lands.
      Future.delayed(const Duration(seconds: 1), () {
        if (mounted) _send(_l1ClusterKey);
      });
    }
    // After 4 taps, this is a no-op (button shows "Done — reload").
  }

  String _l1Label() {
    if (_l1Taps == 0) return 'Trigger L1';
    if (_l1Taps < 4) return 'Trigger L1 ($_l1Taps/4)';
    return 'L1 fired — reload';
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Container(
      padding: const EdgeInsets.fromLTRB(8, 6, 8, 6),
      decoration: BoxDecoration(
        color: scheme.surface,
        border: Border(top: BorderSide(color: Colors.white.withAlpha(15))),
      ),
      child: Wrap(
        spacing: 6,
        runSpacing: 6,
        children: [
          _Chip(
            icon: Icons.alarm,
            label: 'Set reminder',
            onTap: () => _send('remind me to test ino in 60 seconds'),
          ),
          _Chip(
            icon: Icons.psychology,
            // Combined prompt: seeds memory and queries it in one turn so the
            // LLM can answer from in-context history. Slice C (auto-store hook)
            // will let us split this into two real turns later.
            label: 'Recall',
            onTap: () => _send(
                'my favourite colour is purple. what\'s my favourite colour?'),
          ),
          _Chip(
            icon: Icons.flight,
            label: 'Find flights',
            onTap: () => _send('find flights to bali next month'),
          ),
          _Chip(
            icon: Icons.local_taxi,
            label: 'Get an uber',
            onTap: () => _send('get me an uber home'),
          ),
          _Chip(
            icon: Icons.auto_awesome,
            label: _l1Label(),
            onTap: _l1Taps < 4 ? _onL1Tap : null,
          ),
          _Chip(
            icon: Icons.insights,
            label: 'Show last routing',
            onTap: widget.onShowInspector,
          ),
        ],
      ),
    );
  }
}

class _Chip extends StatelessWidget {
  const _Chip({required this.icon, required this.label, this.onTap});

  final IconData icon;
  final String label;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    return FilledButton.tonal(
      onPressed: onTap,
      style: FilledButton.styleFrom(
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
        shape: const RoundedRectangleBorder(
          borderRadius: BorderRadius.all(Radius.circular(20)),
        ),
        textStyle: const TextStyle(fontSize: 13),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 16),
          const SizedBox(width: 6),
          Text(label),
        ],
      ),
    );
  }
}
```

- [ ] **Step 2: Verify Dart analyzer is clean**

Run from `D:\ino\clients\ino.flutter`:
```
flutter analyze lib/ui/components/test_button_strip.dart
```
Expected: `No issues found!`

If `flutter` isn't on PATH, run `flutter --version` first to confirm — bail and surface to the user if missing (memory rule: stale wwwroot ships otherwise).

---

## Task 2 — Mount the strip above `_InputBar`

**Files:**
- Modify: `clients/ino.flutter/lib/screens/home/home_screen.dart`

- [ ] **Step 1: Add the import at the top of the file**

Find the import block (near the top, after `import 'package:flutter/material.dart';`). Add:

```dart
import 'package:ino_flutter/ui/components/test_button_strip.dart';
```

If the file already has alphabetized `ui/components/...` imports, slot it into alphabetical order with the others.

- [ ] **Step 2: Add the demo flag const**

After the imports, before any class definitions, add:

```dart
/// Toggles the demo button strip above the chat composer. Flip to false
/// before v0.1 ship. Gated as a const (not env-driven) per CLAUDE.md
/// "no env-var branches" rule applied by analogy to the client.
const bool kShowDemoButtons = true;
```

- [ ] **Step 3: Mount the strip in the Column above `_InputBar`**

Find this block at `home_screen.dart:231-237`:

```dart
            ),
            _InputBar(
              controller: _inputController,
              onSend: _sendMessage,
              onMicToggle: _toggleRecording,
            ),
          ],
```

Replace it with:

```dart
            ),
            if (kShowDemoButtons)
              TestButtonStrip(
                onShowInspector: _showInspectorDrawer,
              ),
            _InputBar(
              controller: _inputController,
              onSend: _sendMessage,
              onMicToggle: _toggleRecording,
            ),
          ],
```

- [ ] **Step 4: Wire up `_showInspectorDrawer` if it doesn't exist**

Search the file for `_showInspectorDrawer`:
```
grep -n '_showInspectorDrawer' clients/ino.flutter/lib/screens/home/home_screen.dart
```

If the symbol exists (Inspector drawer is already trigger-able from somewhere in the screen), the `onShowInspector: _showInspectorDrawer` call wires through it — done.

If it does NOT exist, find the existing inspector-drawer trigger pattern. The drawer's entry point is `showInspectorDrawer(context)` from `clients/ino.flutter/lib/ui/components/inspector_drawer.dart`. Add a method to the home_screen `State` class:

```dart
void _showInspectorDrawer() {
  showInspectorDrawer(context);
}
```

And add the import if missing:

```dart
import 'package:ino_flutter/ui/components/inspector_drawer.dart';
```

- [ ] **Step 5: Verify analyzer is clean**

```
flutter analyze lib/screens/home/home_screen.dart lib/ui/components/test_button_strip.dart
```
Expected: `No issues found!`

---

## Task 3 — Build the Flutter web bundle

- [ ] **Step 1: Confirm `flutter` is on PATH**

```
flutter --version
```
Expected: prints Flutter 3.41+ version. If it errors, STOP — the MSBuild auto-build target won't pick up the changes and Aspire will serve a stale wwwroot. Fix the PATH first; do not proceed.

- [ ] **Step 2: Build the web bundle**

```
cd D:\ino\clients\ino.flutter
flutter build web --no-tree-shake-icons
```
Expected: completes with `Compiling lib\main.dart for the Web... done` and `✓ Built build\web`. No errors.

`--no-tree-shake-icons` is required because the strip uses Material icons (`Icons.alarm`, `Icons.flight`, etc.) that the tree-shaker can't always prove reachable.

---

## Task 4 — Hot-rebuild the kernel silo

- [ ] **Step 1: Rebuild kernel via Aspire MCP**

```
mcp__aspire__execute_resource_command(resourceName="kernel", commandName="rebuild")
```
Expected: command succeeds; kernel resource transitions Running → Building → Running in dashboard. The MSBuild target should copy the new `build/web/*` into the kernel's `wwwroot/`.

If `aspire run` is not currently up, start it first from `D:\ino`:
```
aspire run
```
Wait for all silos (kernel, identity, travel, taxi, genesis, telegram) to be Healthy, then run the rebuild command.

---

## Task 5 — Manual browser verification (the gate that matters)

- [ ] **Step 1: Open the kernel HTTPS URL in Chrome via DevTools MCP**

```
mcp__chrome-devtools__list_pages   # find the kernel URL from Aspire dashboard or env
mcp__chrome-devtools__navigate_page(url="https://localhost:<kernel-port>/")
```

Expected: Flutter app loads, you see the chat composer at the bottom. **Above** the composer, six chips arranged horizontally (or wrapping if narrow): Set reminder, Recall, Find flights, Get an uber, Trigger L1, Show last routing.

- [ ] **Step 2: Screenshot the strip**

```
mcp__chrome-devtools__take_screenshot
```
Expected: image shows the six chips with M3 styling (tonal-filled, rounded, icons + labels).

- [ ] **Step 3: Tap "Set reminder"**

```
mcp__chrome-devtools__take_snapshot   # find the chip's UID
mcp__chrome-devtools__click(uid="<chip-uid>")
```
Expected within 5 s: chat shows the user prompt `"remind me to test ino in 60 seconds"` and an ino reply roughly like `"OK, I'll remind you in 1 minute."`.

Wait 60 s. Expected: a `ReminderNarration` arrives as a new chat bubble (text matching the reminder description).

- [ ] **Step 4: Tap "Recall"**

Click the chip. Expected: chat shows the combined prompt and ino's reply contains the word `"purple"` (the LLM is answering from in-context memory, not from Qdrant).

- [ ] **Step 5: Tap "Find flights"**

Click the chip. Expected: chat shows `"find flights to bali next month"` and ino's reply is a plan response (text — RFW lands in Slice 4). Mock data is fine.

- [ ] **Step 6: Tap "Get an uber"**

Click the chip. Expected: chat shows the prompt and a Taxi-domain plan response (mock).

- [ ] **Step 7: Tap "Trigger L1" four times**

Click the chip 4× with ~500 ms gaps between each. Expected:
- Taps 1–3: each fires the same `demo l1 marker <id>` prompt. Each gets an "I don't know how to handle that" type response.
- Tap 4 fires after a 1 s pause. The reply is the deterministic stub: `"Got it — I'll help with 'demo l1 marker <id>'. (Auto-generated from 3 unrouted prompts.)"`.

The chip label should progress: `Trigger L1 (1/4)` → `(2/4)` → `(3/4)` → `(4/4)` → `L1 fired — reload`.

- [ ] **Step 8: Verify Aspire structured logs**

In the Aspire dashboard's Structured Logs tab, filter on the kernel silo. Confirm at least one entry each:

- `MissedIntentTracker: emitted L1Proposal` (after the 3rd tap)
- `CreatorNeuron: registered dynamic experience` (after the 4th tap)

(The exact log text may differ — search for `L1Proposal` and `CreatorNeuron` substrings.)

- [ ] **Step 9: Tap "Show last routing"**

Click the chip. Expected: the inspector drawer opens. The Routing/Proposals tabs are NOT yet wired (that's Slice 3) — so the existing panels (Identity, State, Reasoning, Metrics) are what shows. This step verifies the trigger wiring works; the new tabs come later.

If the drawer does not open: check that `_showInspectorDrawer` was wired correctly in Task 2 Step 4.

---

## Task 6 — Commit

- [ ] **Step 1: Stage the changes**

```
git add clients/ino.flutter/lib/ui/components/test_button_strip.dart clients/ino.flutter/lib/screens/home/home_screen.dart
```

- [ ] **Step 2: Verify nothing else got staged**

```
git status
```
Expected: only the two Flutter files staged. If anything else (e.g., generated build artifacts, pubspec.lock) is in the diff, investigate before committing.

- [ ] **Step 3: Commit**

```
git commit -m "$(cat <<'EOF'
feat(poc): demo button strip on chat screen

Six tappable chips above the chat composer for one-tap manual smoke
testing. Dispatches through the existing InoBloc.SendMessage event;
no backend changes. L1 trigger uses a session-scoped cluster key so
MissedIntentTracker actually clusters the four taps.

Gated by `kShowDemoButtons` const in home_screen.dart — flip to false
before v0.1 ship.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
```

- [ ] **Step 4: Push to remote**

```
git push
```
Expected: pushes to `master`. (Per memory `feedback-autopilot` — push after green, no per-slice user gate.)

---

## Done when

All Task 5 steps verified in the browser with screenshots, and the commit is pushed. Per CLAUDE.md verification loop: type-check + manual browser drive is the gate, not just `dotnet build`.

## Out of scope

- Persisting demo-button taps across page reloads.
- Hiding the strip via build flag — flip `kShowDemoButtons` manually.
- New gRPC endpoints — strip uses the existing `Chat` bidi stream only.
- Inspector drawer Routing/Proposals tabs — that's Slice 3.
