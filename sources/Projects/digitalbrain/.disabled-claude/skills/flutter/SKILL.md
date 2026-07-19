---
name: flutter
description: "Work on the BrainOS Flutter desktop/web client (src/clients/flutter). Use for any change to the Flutter UI — single-scene full-bleed brain view (graph, prompt dock, voice input, RFW card overlays), routing, theme, gRPC stubs — and for inspecting the running app's widget tree / runtime errors via the Dart MCP server. Covers the three standalone Aspire Flutter resources (flutter-web release / flutter-chrome / flutter-windows), the flutter-windows-only Dart-MCP DTD recipe, the per-target verify loops, and the no-flutter-test rule."
---

# BrainOS Flutter client

The only client. Flutter desktop + web, run as **three standalone Aspire
resources** (the kernel no longer serves it). The entire interface is a
single full-bleed living brain scene constellation view (**BrainSceneScreen**) with a
glassmorphic floating prompt and Whisper voice-input dock at the bottom center.
Routing is in `lib/router.dart` (hash routing on web, e.g. `/#/app`). Read
`CLAUDE.md` first — its rules win over anything here.

## Hard rules (from CLAUDE.md)

- **No `flutter test`. Ever.** UI assertions live in `BrainOS.E2E.Tests` and
  assert on the RFW payload contract over gRPC, not on rendered widgets.
- The client only knows the gateway proto in
  `BrainOS.Kernel.Contracts/Protos`. It never references domain projects.
- Lifecycle is Aspire-only: never `taskkill`/`Stop-Process`. Resource
  changes via the Aspire MCP; AppHost/kernel source changes via
  `aspire stop` → `aspire start` (a kernel `rebuild` deadlocks on domain
  DLL locks while silos run).

## The three Flutter resources

All decoupled from the kernel; all injected with
`--dart-define=KERNEL_ENDPOINT`; gRPC-Web is cross-origin (kernel CORS policy
`flutter-web` via `RequireCors`, origins `:5800` & `:5801`).

| Resource | Run | URL | Start | Dart MCP tree? |
|---|---|---|---|---|
| `flutter-web` | `-d web-server --release` | `http://localhost:5800` | auto | **No** (browser = no Dart VM) |
| `flutter-chrome` | `-d chrome` | `http://localhost:5801` | explicit | **No** (no DTD; human DevTools URL only) |
| `flutter-windows` | `-d windows --print-dtd` | desktop window | explicit | **Yes** — the only one |

`flutter-web` is `--release` deliberately: a *debug* `-d web-server` build
paints blank (its DWDS client needs the Chrome extension). `flutter-chrome`
renders and prints a Flutter DevTools URL (`:5811/...devtools...`) for a
human, but the current Flutter toolchain emits no Dart-MCP DTD for web
targets.

## Live inspection — Dart MCP (flutter-windows) + Playwright (web)

The `dart` MCP server attaches **only to `flutter-windows`** (real Dart VM).
Recipe:

1. Aspire MCP `start` the `flutter-windows-…` resource; wait for the debug
   build (~2–5 min).
2. Read its **on-disk DCP stdout** for the DTD URI — Aspire's
   `list_console_logs` ring buffer truncates and usually misses it:
   `ls -t "$TEMP"/aspire-dcp*/flutter-windows*_out_* | head -1`, then grep
   for `Dart Tooling Daemon is available at: ws://…`.
3. `mcp__dart__connect_dart_tooling_daemon` with that `ws://` URI →
   `get_widget_tree` / `get_runtime_errors` / `hot_reload` (desktop hot
   reload is instant; verified edit→reload→revert round-trips).

`analyze_files`, `format_code`, `pub_dev_search`, `resolve_symbols`,
`manage_dependencies` also work once connected.

For web UI, drive the Playwright MCP at `http://localhost:5800` (or `:5801`)
— discover the URL via `mcp__aspire__list_resources`. A *working* Flutter web
a11y snapshot is just an "Enable accessibility" button (canvas-rendered); an
**empty** snapshot means the app didn't mount.

## The verify loop

- **Dart change, desktop:** edit → `mcp__dart__hot_reload` on the connected
  `flutter-windows` (seconds). Primary fast loop.
- **Dart change, web (`flutter-web`):** `rebuild` the `flutter-web` resource
  via the Aspire MCP (re-runs `flutter build web --release`, ~2–4 min). The
  Flutter service worker caches hard — in Playwright unregister it + clear
  caches before reload or you verify the stale bundle.
- **AppHost/kernel `Program.cs` or `.csproj` change:** `aspire stop` then
  `aspire start` (a kernel `rebuild` fails on MSB3021 DLL locks while silos
  run).

Heavy loops bounce the app — batch UI changes and verify once.

## Cheap checks (no Aspire, fast)

```pwsh
cd src/clients/flutter
flutter analyze lib/path/to/changed.dart   # scope to changed files — full run is slow
dart format lib/path/to/changed.dart
```

## gRPC stubs

After editing a `.proto` in `BrainOS.Kernel.Contracts/Protos`, regenerate:

```pwsh
cd src/clients/flutter
dart run build_runner build --delete-conflicting-outputs
```

## Layout gotchas seen in this codebase

- `SingleChildScrollView` gives its child **unbounded** main-axis space.
  A `Row`/`Column` with `crossAxisAlignment: stretch` inside that unbounded
  axis has no bound to stretch to and the subtree fails to lay out. Wrap the
  `Row` in `IntrinsicHeight`, or put the scroll view *inside* a `Stack` that
  inherits a bounded height.
- Don't auto-open modals on first render — it ambushes visitors. Gate
  hosting/upsell surfaces behind an explicit user action.
- A blank panel with only an absolutely-`Positioned` child showing = the
  non-positioned subtree threw during layout. Check `get_runtime_errors`.

## File map

```
lib/
├── router.dart            # GoRouter: maps / and /app to the single living brain scene
├── app.dart               # MaterialApp.router root
├── features/
│   ├── brain/             # full-bleed living brain view (brain_scene_screen.dart & voice_input.dart)
│   ├── live/              # brain graph, cards, timeline, search, domain_palette.dart
│   └── rfw_gallery/       # local dev RFW preview and kit harness
├── widgets/               # RFW card widgets (plan, code, gherkin, etc.)
├── grpc/                  # generated stubs + channel/endpoint helpers
├── shell/                 # root container / shell
├── telemetry/             # OTLP exporters, bloc observer
└── theme/brainos_theme.dart  # BrainOSColors + typography
```
