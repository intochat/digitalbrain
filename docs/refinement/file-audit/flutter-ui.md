# Subsystem Audit — flutter-ui (presentation layer)

- **Subsystem**: flutter-ui — `ui_kit` design system, `digital_brain_ui` adaptive/glass/glow package, `rfw_host` (server-driven UI host), `theme`.
- **Scope**: 69 files from `scratchpad/filelists/flutter-b.txt` (`app/lib/ui_kit/*`, `app/lib/digital_brain_ui/**`, `app/lib/rfw_host/**`, `app/lib/theme/digitalbrain_theme.dart`).
- **Commit**: `72400e3ebbec27e17af4ae6b5b2c4158c2797fa4` (branch `docs/refinement-audit`)
- **Date**: 2026-07-13
- **Framework verification**: rfw latest is 1.1.3 (repo pins `^1.0.0`, resolves compatibly); forui `^0.21.3` pinned deliberately (0.22+ needs Flutter >=3.44); google_fonts `^6.2.1`, lottie `^3.3.3`, flutter_earth_globe `^2.2.1`, graphic `^2.7.0`. Context7 quota was exhausted for both configured servers during this audit; rfw security model verified against the official pub.dev/GitHub documentation via WebFetch (recorded as a partial documentation-source substitution, not a gap in the finding).

## Subsystem overview

This is the entire client presentation layer. It has three cooperating pieces:

1. **`rfw_host`** — the runtime that renders **server-driven UI**. `RfwRuntimeHost` owns one process-wide `rfw.Runtime` seeded with the host-authored `digitalbrain` widget dictionary (`digitalbrain_rfw_library.dart` + its `library/*.dart` parts). `UiSurfaceTreeRenderer` is an *additional*, hand-rolled JSON-tree renderer that dispatches `ui:*` / `neuron:*` / `forui:*` node types into `ui_kit` and ForUI widgets. Both are the trust boundary: the kernel/neurons emit UI descriptions the client executes.
2. **`ui_kit`** — 42 thin wrappers over ForUI components (`FButton`, `FCard`, `FTile`, …) plus a `ui_registry.dart` switchboard, a form-state scope, and one large bespoke `ui_graph_canvas.dart`. This is the allowlisted vocabulary `UiSurfaceTreeRenderer` maps `ui:*` nodes onto.
3. **`digital_brain_ui`** — a would-be extractable pub package: Material 3 window-size/input-mode breakpoints, adaptive overlay routing (`AdaptiveSurface`), glass materials, glow-icon raster cache, and a scene-effects notifier.
4. **`theme`** — `DigitalBrainColors` tokens + `buildDigitalBrainTheme()` + `GlassBorder`.

Data flow: neuron → `SurfaceEnvelope` → `SurfaceView` (`runtime/widgets/surface_view.dart`, outside this list) → either `RfwRuntimeHost.render` (RFW document) or `UiSurfaceTreeRenderer().build` (JSON tree) → `ui_kit`/dictionary widgets → user events flow back through `RemoteEventHandler onEvent` → `SurfaceView._onRemoteEvent` → synapse dispatch. Several widgets (`_NeuronForm`, `_SynapseRowWidget`, `_PromptInputBody`, `_CodeEditorBody`) bypass `onEvent` and talk to the gRPC client directly via `DigitalBrainClientScope`.

---

## Per-file review

### rfw_host

**`rfw_host/rfw_runtime_host.dart`** (827 lines) — Purpose: the RFW runtime wrapper (`RfwRuntimeHost`) and the JSON-tree renderer (`UiSurfaceTreeRenderer`). `RfwRuntimeHost` is sound: idempotent `ensureLoaded` with per-key parse-error capture (bad document degrades to an error string, good), CRLF normalization, `Semantics` wrapper. `UiSurfaceTreeRenderer.build` is a ~340-line god-method mixing `ui:*` registry delegation, `neuron:*`/`forui:*` switch cases, and a long chain of raw `if (type == …)` fallbacks with `contains()` heuristics (`cType.contains('sidebar')`) — directly contradicting its own comment "Aliases and heuristics removed in prior step." Duplicated dispatch authority with `ui_registry.dart`. `_NeuronForm` (embedded StatefulWidget) correctly manages TextEditingControllers with sync/dispose. Security-critical: form submit and button handlers forward server-supplied `props`/`synapseType` into `onEvent('action'/'press', …)` (see SEC-800). `navigatorKey` global + `_sidebarTitleStyle()` reaching into `navigatorKey.currentContext` for theme is a hidden global coupling smell. **Verdict**: split (extract `UiSurfaceTreeRenderer` dispatch into a table; separate `_NeuronForm`) + harden event surface.

**`rfw_host/digitalbrain_rfw_library.dart`** (3218 lines) — Purpose: the host-owned RFW widget **dictionary** (`createDigitalBrainWidgets`) plus a large amount of embedded editor machinery (`InoLangTextEditingController`, `PromptTextEditingController`, `DigitalBrainCatalogManager` singleton, `_CodeEditorBody`, `_StateEditorBody`, `_SynapseRowWidget`, `_LlmSettingsPanelBody`, telemetry panel). The dictionary map (lines 40–80) is the real allowlist — small, host-authored, no arbitrary code (good; matches rfw's intended model). But this file is massively oversized and conflates the vocabulary with heavy stateful editor widgets and a gRPC-calling catalog manager. `_SynapseRowWidget._fireSynapse` and `_CodeEditorBody._runCompileAndStage` build `SynapseEnvelope`s from server/user input and `client.send()` them directly (SEC-801, SEC-803). `DigitalBrainCatalogManager` is a mutable singleton whose `_loaded` flag is never invalidated (REL-800) and whose network failures are swallowed to `debugPrint` (REL-801). Highlighting/autocomplete re-run multiple whole-document regexes per keystroke/build (PERF-801). **Verdict**: split hard — dictionary vs. editor widgets vs. catalog manager into separate files.

**`rfw_host/library/basic.dart`** (255 lines, `part of`) — Panel/Text/Button/Badge/Progress/Avatar/TaskRow builders. Clean, theme-aware, reads from `DataSource`. `_avatar` uses `Image.network` with an `errorBuilder` fallback (good). Minor: `_button` ignores any onTap target/navigation (only voidHandler). **Verdict**: retain.

**`rfw_host/library/chat.dart`** (36 lines, `part of`) — Only `_synapseStream`; header comment says "will be moved here" but the promised chat widgets live in the parent file. Thin, correct (`ListenableBuilder` over `SynapseStreamScope`). **Verdict**: retain; fold stale comment.

**`rfw_host/library/data.dart`** (90 lines, `part of`) — Only `_timeline`; same "will be moved here" stale comment; `Table`/`Progress`/telemetry it references live in the parent. Correct. **Verdict**: retain; fix comment.

**`rfw_host/library/helpers.dart`** (85 lines, `part of`) — `DataSource` readers (`_d/_str/_int/_bool/_sp`), `_tone`, `_cross`, `_variant`. Correct. Note `_tone` maps teal/gold/violet/indigo/rose to `DigitalBrainColors` values that are now nearly all identical silver (FRAME-800). **Verdict**: retain.

**`rfw_host/library/layout.dart`** (74 lines, `part of`) — `_divider`, `_stack`, `_pad`. `_stack` correctly handles gaps/`between`/`equal` and wraps stretch rows in `IntrinsicHeight` (documented reason). **Verdict**: retain.

**`rfw_host/palette/palette_primitives.dart`** (810 lines) — Purpose: "Tier-1" RFW palette primitives: `lottiePlayer`, `analogClock`, `countdownClock`, `earthGlobe` (+ web flat-map fallback), `floatingWindow`, with two `CustomPainter`s. **None of these functions is registered in any `LocalWidgetLibrary` or referenced anywhere outside this file** (verified by grep across `lib`/`test`). This is ~810 lines of dead/speculative code that also justifies the `lottie` and `flutter_earth_globe` dependencies (CLEAN-800, ARCH-800). The countdown-clock drift-free math and painter logic are individually well-written, but unreachable. Local re-declared `_d/_s/_i/_b/_dp/_sp` readers duplicate helpers.dart (CLEAN-801). **Verdict**: delete (or wire into the dictionary and register) — biggest single deletion opportunity in the subsystem.

**`rfw_host/synapse_stream_scope.dart`** (28 lines) — `SynapseStreamFeed` ChangeNotifier + InheritedNotifier scope. Clean; `forCorrelation` is O(n) per call inside a builder but n is small. **Verdict**: retain.

### ui_kit

**`ui_kit/ui_registry.dart`** (237 lines) — The `ui:*` → widget switchboard used by `UiSurfaceTreeRenderer`. Coherent, exhaustive, safe default (`SizedBox.shrink`). `_buttonEventProps` strips visual keys before forwarding — but still forwards all other server props into the button event (feeds SEC-800). Duplicated dispatch authority with the `neuron:*`/`forui:*`/raw branches in `rfw_runtime_host.dart` (ARCH-801). **Verdict**: retain; consolidate the two dispatch tables.

**`ui_kit/ui_graph_canvas.dart`** (538 lines) — Bespoke schema/force graph: parsing, a grid `_GraphLayout`, `_GraphEdgePainter`, node cards, `InteractiveViewer`. Self-contained, well-factored into private widgets, theme-aware via `FTheme`. Layout is a fixed grid (`layout` prop only toggles column count; "force" is not actually a force layout — misleading naming, CLEAN-802). `shouldRepaint` compares list identity (`edges != edges`) which is always false for new lists — over-repaints, though acceptable here. **Verdict**: retain; rename layout modes to match behavior.

**The 40 thin ForUI wrappers** (`ui_alert`, `ui_avatar`, `ui_badge`, `ui_bottom_nav`, `ui_breadcrumb`, `ui_button`, `ui_checkbox`, `ui_column`, `ui_date_field`, `ui_dialog`, `ui_divider`, `ui_form_scope`, `ui_gap`, `ui_header`, `ui_heading`, `ui_icon`, `ui_link`, `ui_list`, `ui_nav_item`, `ui_overlay_host`, `ui_pagination`, `ui_panel`, `ui_progress`, `ui_radio_group`, `ui_row`, `ui_screen`, `ui_select`, `ui_sheet`, `ui_sidebar`, `ui_slider`, `ui_spinner`, `ui_switch`, `ui_table`, `ui_tabs`, `ui_text`, `ui_text_area`, `ui_text_field`, `ui_tile`, `ui_toast`, `ui_tooltip`) — Collectively a **coherent, consistent design system**, not sprawl: each is a small mapping onto a ForUI component, form-bound widgets uniformly push into `UiKitFormScope`, nav widgets uniformly use `parseNavItems`+`fireNav`. StatefulWidgets that own controllers (`ui_date_field`, `ui_select`, `ui_screen`) dispose correctly. Naming is uniform (`UiKit*`). Notable specifics:
  - **`ui_link.dart`** — launches any `Uri.tryParse(url)` with `LaunchMode.externalApplication` and **no scheme allowlist**; `url` comes from server-driven `ui:link` props → SEC-802.
  - **`ui_button.dart`** — forwards `eventProps` (server-controlled) plus captured form values into `onEvent('press', …)`; imports app-level `NeuronVectorLogo` (leaks a `features/` dependency into the would-be-standalone kit, ARCH-802).
  - **`ui_overlay_host.dart`** — `PresentOnce` mixin; `ui_toast.dart` calls `presentOnce(true, …)` so it re-toasts on every remount, and `_UiKitToast` re-shows whenever rebuilt from a fresh instance (REL-802, low).
  - **`ui_checkbox`/`ui_switch`/`ui_text_field`/`ui_text_area`** store `String` values ("true"/"false") into the form scope — untyped but consistent.
  - **`ui_screen.dart`** pulls a `UiKitSidebar` out of vertical flow to avoid unbounded-height throws — a real fix, documented inline.
  **Verdict**: retain the set; fix ui_link (SEC), decouple ui_button from `features/`.

### digital_brain_ui

**`digital_brain_ui.dart`** — Barrel export for the future package. Clean. **Verdict**: retain.

**`adaptive/adaptive_surface.dart`** (167 lines) — `SurfaceWeight × WindowSize` dispatch table routing to sheet/dialog/side-sheet/full-screen. Exhaustive `switch` on the tuple (compile-checked), clean morph wiring. **Verdict**: retain — good example of the OS-model adaptive shell.

**`adaptive/adaptive_dialog.dart`, `adaptive_sheet.dart`, `adaptive_side_sheet.dart`** — Platform-conventional overlays with scrim-tap dismiss and flick-to-dismiss. Correct gesture handling (inner `GestureDetector` with empty onTap to swallow). `AdaptiveDialog` doc-comment says iOS gets Cupertino styling but the body always uses `GlassMaterial` (which itself branches on platform) — minor doc drift. **Verdict**: retain.

**`breakpoints/window_size.dart`, `window_size_scope.dart`, `input_mode.dart`** — Material 3 window-size classes + InheritedWidget scopes + input-mode tracker. Idiomatic, correct `updateShouldNotify`, `assert`-guarded `.of()`. **Verdict**: retain.

**`density/adaptive_density.dart`** — Spacing tokens + `adaptiveVisualDensity`. Clean; note `buildDigitalBrainTheme()` still hardcodes `VisualDensity.adaptivePlatformDensity` rather than using this (CLEAN-803, the function's own doc says it "replaces" that). **Verdict**: retain; wire it in or delete the claim.

**`debug/debug_brain_stats.dart`** (150 lines) — Glass HUD with a pulsing dot. Uses `fontFamily: 'Orbitron'`/`'Outfit'` string families (not the google_fonts path the rest of the app uses) — inconsistent typography source (FRAME-801). Debug-only widget. **Verdict**: retain (debug), or gate behind kDebugMode.

**`effects/brain_scene_effects.dart`, `effects_pulse.dart`** — `BrainSceneEffects` ChangeNotifier + sealed `EffectsPulse`/`CollapseWave`/`BirthPulse`. `pulses` getter allocates `List.unmodifiable` on every read (PERF-802, low). Clean sealed hierarchy. **Verdict**: retain.

**`glass/glass_material.dart`** (219 lines) — The shader path is **permanently disabled**: `static bool get _shadersEnabled => false;`, yet `initState` still calls `_loadShader()` which `await`s `FragmentProgram.fromAsset('assets/shaders/glass_refract.frag')` on every instance, only to never use the result (`hasShader` is always false). Dead async work + a shipped shader asset that is never rendered (PERF-800, CLEAN-804). Worse, a `Ticker` calls `setState` every frame while the pointer hovers, rebuilding the whole glass subtree purely to update `_elapsedTime` — which is only consumed by the disabled shader painter (PERF-800). `LiquidGlassShaderPainter` is entirely dead. **Verdict**: simplify hard — delete shader loading, ticker, mouse tracking, and the painter; keep the static gradient fallback.

**`glass/liquid_glass_surface.dart`** (136 lines) — Morph-in/collapse animation from an origin point; respects `disableAnimations`. Well-written; disposes controller. **Verdict**: retain.

**`glow/glow_icon.dart`, `glow_icon_spec.dart`, `glow_painter.dart`** — `GlowIcon` raster-caches painted glow orbs keyed by a value-equal `GlowIconSpec`; `prewarm` builds images off-frame; perf tier drives dot count/blur via the SDK. Cache eviction removes `_cache.keys.first` (insertion-order, not LRU) — acceptable but not true LRU (PERF-803, low). Painter uses the documented saveLayer+BlendMode.plus heatmap technique. Value equality via `toARGB32()` correct. **Verdict**: retain — good perf-conscious code.

### theme

**`theme/digitalbrain_theme.dart`** (254 lines) — `DigitalBrainColors`, `DigitalBrainTypography`, `buildDigitalBrainTheme()`, `GlassBorder`(+painter). The theme is coherent Material 3 dark. **But the color token names are actively misleading**: `teal = Color(0xFF27272A)` (charcoal), `gold = 0xFFF5F5F7` (white), `tealSoft/violetSoft/goldSoft = 0xFFE5E5E5` (identical silver), `indigoSoft = platinum white`. The palette was collapsed to monochrome silver + a single amber accent (`rose = 0xFFFF9500`) but the semantic names (teal/gold/violet/indigo) were kept, so `_tone('teal')`, `_tone('gold')`, `_tone('violet')` all now render near-identical silver (FRAME-800). Every consumer that "picks a color by meaning" is silently getting grey. **Verdict**: rename tokens to their real values (`silver`, `silverSoft`, `charcoal`, `amber`) or restore the palette; this is the single biggest maintainability hazard in the subsystem.

---

## Findings

### SEC-800: Server-emitted UI forwards attacker-controlled `synapseType`/props into the client's synapse dispatch
- **Severity**: High
- **Confidence**: High
- **Evidence**: `rfw_host/rfw_runtime_host.dart:129-140,185-204,482-489` (button/menuitem handlers spread `...props` into `onEvent('press', {…})`); `rfw_host/rfw_runtime_host.dart:732-761` (`_NeuronForm._submit` emits `onEvent('action', {'synapseType': synapseType, 'props': props})` where both derive from server-provided `props['submitAction']`/`props['synapseType']`); `ui_kit/ui_button.dart:34-46` and `ui_kit/ui_nav_item.dart:19-35` (`fireNav`) both forward server `synapseType`/`eventName`/props.
- **Current behavior**: The renderer copies whole server-supplied prop maps (including a caller-chosen `synapseType`) into the `RemoteEventHandler` payload that `SurfaceView._onRemoteEvent` turns into a real synapse. The server-driven UI therefore chooses which synapse type fires and with what payload.
- **Why it matters** (INFERENCE): This is the RFW trust boundary. rfw's own model guarantees only that no *arbitrary Dart* runs; it explicitly leaves all side-effects to the host `onEvent`. Here `onEvent` is a thin pass-through that lets the UI description name the synapse. A compromised/malicious surface author can drive privileged kernel actions the user never intended.
- **OS/product consequence**: Breaks the "all external mutations previewed/approved" and fail-closed authorization invariants — the presentation layer becomes a synapse-injection surface into the neuron kernel.
- **Recommendation** (PROPOSAL): Make `onEvent` map an *opaque binding id* (chosen server-side but validated against the surface's declared, approved action set) rather than a free-form `synapseType`. Whitelist permitted synapse types per surface/pack; reject unknown types fail-closed at `_onRemoteEvent`.
- **Deletion/simplification opportunity**: no
- **Dependencies**: SEC-801, SEC-803; runtime `surface_view.dart` `_onRemoteEvent` (outside this list).
- **Tests/measurements required**: widget/integration test asserting a surface declaring an un-approved `synapseType` is rejected before `client.send`.
- **Effort**: L
- **Migration/rollback concern**: Changes the surface→event contract; needs kernel-side binding registry.

### SEC-801: `_SynapseRowWidget` builds and sends arbitrary synapse envelopes straight to the gRPC client from server-provided type
- **Severity**: High
- **Confidence**: High
- **Evidence**: `rfw_host/digitalbrain_rfw_library.dart:2356-2411` — `_fireSynapse` sets `fqn = widget.type` (from the `SynapseRow` dictionary widget's `type` data), even hardcodes receiver routing (`GmailDigestNeuron`, `DigestEmailFeedNeuron`) by substring match, packs user text-field values into a JSON payload, and calls `client.send(envelope)` with `typeName = fqn`.
- **Current behavior**: A rendered `SynapseRow` (emitted by a surface) fires a fully attacker-shaped synapse (type + payload + receiver neuron type) directly to the kernel, bypassing `onEvent` entirely.
- **Why it matters** (INFERENCE): Same trust-boundary breach as SEC-800 but more direct — no host mediation at all, plus provider-specific receiver routing (Gmail/Digest) is hardcoded into a generic UI widget, leaking connector concerns into the presentation layer.
- **OS/product consequence**: Direct, unmediated mutation channel from server-driven UI to kernel neurons; also violates "provider concerns must not leak into the kernel/UI."
- **Recommendation**: Route through the same validated binding mechanism as SEC-800; delete the hardcoded Gmail/Digest receiver heuristics.
- **Deletion/simplification opportunity**: yes — remove receiver-substring routing.
- **Dependencies**: SEC-800.
- **Tests/measurements required**: test that a `SynapseRow` cannot fire a type outside the approved catalog.
- **Effort**: M
- **Migration/rollback concern**: Behavior change for the synapse-debug UI.

### SEC-802: `UiKitLink` launches arbitrary server-supplied URIs with no scheme validation
- **Severity**: High
- **Confidence**: High
- **Evidence**: `ui_kit/ui_link.dart:12-24` — `final uri = Uri.tryParse(url); if (uri != null) await launchUrl(uri, mode: LaunchMode.externalApplication);` where `url` is `ui:link` `props['url']` from the surface (`ui_registry.dart:68-69`).
- **Current behavior**: Any `ui:link` node opens its URL in the OS default handler with no allowlist of schemes/hosts. `javascript:`, `file:`, `intent:`, custom app schemes, or phishing `https:` targets all launch.
- **Why it matters** (INFERENCE): Server-driven UI can trigger navigation to arbitrary external targets / custom scheme handlers — a classic SSRF-adjacent / open-redirect / scheme-abuse vector on a trust boundary.
- **OS/product consequence**: Presentation layer becomes an unmediated "launch external capability" surface; violates least-privilege at the UI boundary.
- **Recommendation**: Allowlist `http`/`https` (and `mailto` if needed), validate host, and `canLaunchUrl` before launching; consider routing external navigation through an approval prompt.
- **Deletion/simplification opportunity**: no
- **Dependencies**: SEC-800.
- **Tests/measurements required**: unit test rejecting non-http(s) schemes.
- **Effort**: S
- **Migration/rollback concern**: none.

### SEC-803: `_CodeEditorBody._runCompileAndStage` presents a fabricated "compiled successfully" result on the self-evolution mutation path
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `rfw_host/digitalbrain_rfw_library.dart:795-961` — on gRPC failure it falls back to a local `Future.delayed(600ms)` "simulation verification" running regex checks (BOSN001–005) and, if they pass, sets `_compileStatus = 'success'` and shows a "Staged and compiled successfully!" SnackBar.
- **Current behavior**: When the real Promote/compile call to the kernel fails (or no client), the UI can still tell the user their `.ino` neuron was "staged & compiled/verified" based on a client-side regex heuristic.
- **Why it matters** (INFERENCE): The Ino compile/stage flow is a self-evolution mutation (propose → validate → approve → apply). Fabricating a success verdict client-side undermines the governed rail's "verified" guarantee and misleads the user into believing a mutation landed.
- **OS/product consequence**: Weakens the self-evolution rail's trust/verification invariant; false-positive apply feedback.
- **Recommendation**: On gRPC failure, surface an explicit "could not reach compiler" error; never emit a success verdict without a server-confirmed result.
- **Deletion/simplification opportunity**: yes — delete the local simulation success path (keep it as an offline *lint hint* clearly labeled non-authoritative).
- **Dependencies**: kernel Introspector PromoteNeuron path.
- **Tests/measurements required**: test that gRPC failure never yields `_compileStatus == 'success'`.
- **Effort**: S
- **Migration/rollback concern**: none.

### ARCH-800: `palette/palette_primitives.dart` is unregistered dead code (~810 lines) that anchors two heavy dependencies
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `rfw_host/palette/palette_primitives.dart:42,103,166,405,758` define `lottiePlayer/analogClock/countdownClock/earthGlobe/floatingWindow`; grep across `lib` and `test` finds **no** registration or reference outside this file. The dictionary map in `digitalbrain_rfw_library.dart:40-80` does not include them.
- **Current behavior**: The file compiles and imports `lottie` + `flutter_earth_globe`, but none of its widgets is reachable through any `LocalWidgetLibrary`/`Runtime` or the tree renderer.
- **Why it matters** (INFERENCE): ~810 lines of speculative code plus two non-trivial native/asset dependencies (globe shaders, lottie) ship for nothing — build weight, review surface, and confusion (the header claims it is "the one batched binary rebuild (Tier 1)").
- **OS/product consequence**: Dead surface in the trust-boundary host; contradicts delete-first WoW.
- **Recommendation**: Either register these in the dictionary (if the redesign still wants them) or delete the file and drop `lottie`/`flutter_earth_globe`.
- **Deletion/simplification opportunity**: yes — largest single deletion in the subsystem.
- **Dependencies**: CLEAN-800, CLEAN-801.
- **Tests/measurements required**: build succeeds after removal; grep confirms no references.
- **Effort**: S
- **Migration/rollback concern**: none if truly unused.

### ARCH-801: Duplicated widget-dispatch authority between `ui_registry.dart` and `UiSurfaceTreeRenderer.build`
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `ui_kit/ui_registry.dart:61-228` (switch on `ui:*`) vs. `rfw_host/rfw_runtime_host.dart:112-421` (switch/if-chain on `neuron:*`, `forui:*`, `fcard`/`card`/`panel`, `button`/`action`, `list`, `row`/`column`, plus a raw recursive default).
- **Current behavior**: Two parallel, differently-styled node→widget dispatchers exist; the tree renderer even re-implements cards/buttons/lists that `ui_kit` already provides.
- **Why it matters** (INFERENCE): Two sources of truth for "what node types are renderable" makes the allowlist ambiguous and drift-prone; the renderer's `contains()` heuristics (`cType.contains('sidebar')`) reintroduce the aliasing the comment claims was removed.
- **OS/product consequence**: A fuzzy trust-boundary vocabulary is harder to reason about for safety.
- **Recommendation**: Consolidate into one table-driven dispatcher; delete the raw `if (type == …)` fallbacks and route everything through the registry.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: SEC-800.
- **Tests/measurements required**: renderer test enumerating the full supported type set from one table.
- **Effort**: M
- **Migration/rollback concern**: surface authors relying on legacy alias types would need migration.

### ARCH-802: `ui_kit` (the extractable design system) depends on app `features/` code
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `ui_kit/ui_button.dart:5` imports `package:digitalbrain_flutter/widgets/neuron_vector_logo.dart`; `digitalbrain_rfw_library.dart` imports `features/live/graph/domain_palette.dart`, `features/brain/voice_input.dart`, `runtime/buses/*`, `shell/digitalbrain_client_scope.dart`, `grpc/digitalbrain.pbgrpc.dart`.
- **Current behavior**: The nominally-standalone kit/host reach up into feature/runtime/grpc layers.
- **Why it matters** (INFERENCE): Prevents clean extraction of `ui_kit`/`digital_brain_ui` as packages (the barrel file explicitly anticipates extraction) and creates upward layering coupling.
- **OS/product consequence**: Weakens the "presentation is a reusable layer" boundary.
- **Recommendation**: Invert the dependency (inject the logo/voice/client via constructor params or a scope interface).
- **Deletion/simplification opportunity**: no
- **Dependencies**: none.
- **Tests/measurements required**: dependency-direction lint / import graph check.
- **Effort**: M
- **Migration/rollback concern**: none.

### PERF-800: `GlassMaterial` runs a per-frame ticker + async shader load that are permanently dead
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `glass/glass_material.dart:43` (`static bool get _shadersEnabled => false;`), `:52-58` (ticker `setState` every frame updating `_elapsedTime`), `:60-75` (`_loadShader` awaits `FragmentProgram.fromAsset` on every instance), `:96` (`hasShader = _shaderProgram != null && _shadersEnabled` — always false), `:149-166` (MouseRegion drives ticker + `setState` per hover event), `:179-218` (`LiquidGlassShaderPainter` never constructed).
- **Current behavior**: Every `GlassMaterial` loads a shader asset it never uses and, while hovered, rebuilds its whole subtree each frame to update a value only the dead shader painter reads. `GlassMaterial` wraps most panels/dialogs/side-sheets, so this is a hot path.
- **Why it matters** (INFERENCE): Wasted async asset loads and continuous full-subtree rebuilds on hover for a purely decorative static gradient.
- **OS/product consequence**: Jank/CPU on pointer platforms (the primary desktop target).
- **Recommendation**: Delete `_loadShader`, `_ticker`, mouse tracking, `_shaderProgram`, and `LiquidGlassShaderPainter`; keep the static specular gradient. Drop the shader asset from pubspec if unused elsewhere.
- **Deletion/simplification opportunity**: yes — large.
- **Dependencies**: CLEAN-804.
- **Tests/measurements required**: frame-timing before/after on a hovered glass panel; confirm no shader asset load.
- **Effort**: S
- **Migration/rollback concern**: none (visual output already the fallback).

### PERF-801: Ino editor re-runs whole-document regex highlighting/autocomplete on every keystroke and build
- **Severity**: Medium
- **Confidence**: Medium
- **Evidence**: `rfw_host/digitalbrain_rfw_library.dart:204-393` (`InoLangTextEditingController.buildTextSpan` runs several `RegExp.allMatches` over the entire text on every rebuild), `:518-592` (`PromptTextEditingController` similar), `:1190-1315` (`_checkAutocomplete` runs more regexes per change), `:1380-1449` (`_buildCode` splits the whole text and builds a `Text` per line each build).
- **Current behavior**: Editing cost scales with document length × several regex passes per frame; the gutter rebuilds one `Text` widget per line.
- **Why it matters** (INFERENCE): Fine for short snippets, degrades for larger `.ino` documents (typing latency).
- **OS/product consequence**: Sluggish authoring in the self-evolution editor.
- **Recommendation**: Memoize spans per unchanged text; cap highlighting to the visible viewport; precompute alias/bound-var sets once per text change.
- **Deletion/simplification opportunity**: partial.
- **Dependencies**: none.
- **Tests/measurements required**: input-latency benchmark at 200/1000 lines.
- **Effort**: M
- **Migration/rollback concern**: none.

### PERF-802: `BrainSceneEffects.pulses` allocates an unmodifiable copy on every read
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `digital_brain_ui/effects/brain_scene_effects.dart:9` — `List<EffectsPulse> get pulses => List.unmodifiable(_pulses);`
- **Current behavior**: Each access (potentially per frame in a painter) copies the list.
- **Why it matters** (INFERENCE): Avoidable allocations on an animation hot path.
- **OS/product consequence**: minor GC pressure.
- **Recommendation**: Expose an `UnmodifiableListView` field created once, or return the list directly with a documented no-mutate contract.
- **Deletion/simplification opportunity**: yes (small).
- **Dependencies**: none.
- **Tests/measurements required**: allocation profile.
- **Effort**: S
- **Migration/rollback concern**: none.

### PERF-803: `GlowIcon` cache eviction is insertion-order, not LRU
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `digital_brain_ui/glow/glow_icon.dart:65` — `if (_cache.length > _kCacheLimit) _cache.remove(_cache.keys.first);`
- **Current behavior**: Evicts the oldest-inserted key regardless of recency of use.
- **Why it matters** (INFERENCE): Hot specs inserted early can be evicted while cold recent specs survive; marginal cache thrash.
- **OS/product consequence**: negligible.
- **Recommendation**: Use a small LRU (move-to-end on hit) if churn is observed; otherwise document the choice.
- **Deletion/simplification opportunity**: no
- **Dependencies**: none.
- **Tests/measurements required**: cache hit-rate under realistic spec churn.
- **Effort**: S
- **Migration/rollback concern**: none.

### REL-800: `DigitalBrainCatalogManager` singleton never invalidates `_loaded`
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `rfw_host/digitalbrain_rfw_library.dart:416-506` — `_loaded` is set true after the first successful load; `ensureLoaded` short-circuits forever after; there is no TTL or invalidation hook.
- **Current behavior**: The contract catalog (synapse/signal/neuron FQNs used for highlighting, autocomplete, hover cards, and `_fireSynapse` field discovery) is fetched once per process and never refreshed.
- **Why it matters** (INFERENCE): New/changed neurons and contracts (the whole point of a self-evolving system) won't appear until app restart; stale schema drives stale autocomplete and can mis-shape fired synapses.
- **OS/product consequence**: Self-evolution changes are invisible to the authoring UI without restart.
- **Recommendation**: Add invalidation (on catalog-changed synapse, or a short TTL / manual refresh).
- **Deletion/simplification opportunity**: no
- **Dependencies**: SEC-801 (field discovery), PERF-801.
- **Tests/measurements required**: test that a catalog-changed signal repopulates the manager.
- **Effort**: M
- **Migration/rollback concern**: none.

### REL-801: Catalog load failures are swallowed to `debugPrint`
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `rfw_host/digitalbrain_rfw_library.dart:475-504` — both the gRPC and asset-fallback paths `catch (e) { debugPrint(...) }` and leave `_cachedCatalog = []`.
- **Current behavior**: On failure the catalog is silently empty; highlighting/autocomplete quietly degrade with no user or telemetry signal.
- **Why it matters** (INFERENCE): Silent failure hides broken introspection connectivity.
- **OS/product consequence**: Debuggability gap on the authoring path.
- **Recommendation**: Surface a non-fatal status (banner/telemetry) distinguishing "empty catalog" from "load failed."
- **Deletion/simplification opportunity**: no
- **Dependencies**: REL-800.
- **Tests/measurements required**: test that failure sets an observable error state.
- **Effort**: S
- **Migration/rollback concern**: none.

### REL-802: `UiKitToast` re-presents on every remount via `presentOnce(true, …)`
- **Severity**: Low
- **Confidence**: Medium
- **Evidence**: `ui_kit/ui_toast.dart:16-19` passes a constant `true`; `ui_overlay_host.dart:6-16` `presentOnce` only de-dupes within a single State instance.
- **Current behavior**: A `ui:toast` node re-fires its toast whenever the tree rebuilds it as a fresh widget instance (common when a surface re-renders).
- **Why it matters** (INFERENCE): Duplicate/looping toasts on surface refresh.
- **OS/product consequence**: minor UX noise.
- **Recommendation**: Key toasts by message id and dedupe across rebuilds, or drive toasts from events not tree presence.
- **Deletion/simplification opportunity**: no
- **Dependencies**: none.
- **Tests/measurements required**: test that a rebuild with the same toast does not double-show.
- **Effort**: S
- **Migration/rollback concern**: none.

### REL-803: Inline-RFW documents are keyed by `source.hashCode`
- **Severity**: Low
- **Confidence**: Medium
- **Evidence**: `rfw_host/rfw_runtime_host.dart:216-223` — `final key = 'dyn-${source.hashCode}';` then `ensureLoaded(key, source)`.
- **Current behavior**: Two distinct sources with a `String.hashCode` collision would map to the same cached library; also two different documents that happen to collide never re-parse.
- **Why it matters** (INFERENCE): `String.hashCode` is not collision-free; low probability but a correctness footgun for a document cache.
- **OS/product consequence**: Rare wrong-document render.
- **Recommendation**: Key by a content hash (or a monotonic id) with the full source, not `hashCode`.
- **Deletion/simplification opportunity**: no
- **Dependencies**: none.
- **Tests/measurements required**: n/a (defensive).
- **Effort**: S
- **Migration/rollback concern**: none.

### FRAME-800: Color tokens are misnamed — `teal`/`gold`/`violet`/`indigo` all resolve to near-identical silver
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `theme/digitalbrain_theme.dart:47-63` — `indigoSoft = 0xFFF5F5F7`, `gold = 0xFFF5F5F7`, `tealSoft = violetSoft = goldSoft = 0xFFE5E5E5`, `teal = 0xFF27272A`, `violet = 0xFF86868B`; consumed by `_tone` (`library/helpers.dart:15-29`) and ~all rfw widgets.
- **Current behavior**: The palette was collapsed to monochrome silver + one amber accent, but the semantic names were retained; any code selecting a color "by meaning" (tone='teal'/'gold'/'violet') gets essentially the same grey.
- **Why it matters** (INFERENCE): Names no longer describe values, so every future color decision is made blind; tonal encoding of state (e.g. TaskRow status, timeline tones, badges) is visually lost.
- **OS/product consequence**: Theme incoherence and a persistent maintainability trap across the whole UI.
- **Recommendation**: Rename tokens to real values (`silver`, `silverSoft`, `charcoal`, `amber`) and delete redundant aliases, or deliberately restore a differentiated palette.
- **Deletion/simplification opportunity**: yes — collapse the duplicated silver aliases.
- **Dependencies**: CLEAN-802 (tone system), PROD-800.
- **Tests/measurements required**: golden tests for representative widgets.
- **Effort**: M
- **Migration/rollback concern**: pervasive references; mechanical rename.

### FRAME-801: `DebugBrainStats` uses raw `fontFamily` strings while the app uses google_fonts
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `digital_brain_ui/debug/debug_brain_stats.dart:88,99,110,127,137` use `fontFamily: 'Orbitron'` / `'Outfit'`; the rest of the subsystem uses `GoogleFonts.*`.
- **Current behavior**: These families resolve only if registered as pubspec assets (not seen in the assets list), else fall back to default.
- **Why it matters** (INFERENCE): Inconsistent typography source; likely silently wrong font.
- **OS/product consequence**: Minor visual inconsistency (debug HUD only).
- **Recommendation**: Use `GoogleFonts.orbitron()` / `GoogleFonts.outfit()` or register the assets.
- **Deletion/simplification opportunity**: no
- **Dependencies**: none.
- **Tests/measurements required**: none.
- **Effort**: S
- **Migration/rollback concern**: none.

### PROD-800: `LlmSettingsPanel` and `TelemetryPanel` ship hardcoded/fake data
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `rfw_host/digitalbrain_rfw_library.dart:2969` (`models = ['GPT-4o','Claude-3.5','Gemini-1.5']`), `:2764-2767` (telemetry defaults `24/192/3`).
- **Current behavior**: Model list is a static, dated set; telemetry counters default to invented numbers when data props are absent.
- **Why it matters** (INFERENCE): Presents fabricated operational data; model list will drift from the real backend catalog.
- **OS/product consequence**: Misleading control surface; not wired to real model/telemetry sources.
- **Recommendation**: Source models and counters from backend data; default to empty/"—", not invented values.
- **Deletion/simplification opportunity**: no
- **Dependencies**: none.
- **Tests/measurements required**: none.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-800: `graphic ^2.7.0` dependency is unused
- **Severity**: Low
- **Confidence**: Medium
- **Evidence**: `pubspec.yaml` declares `graphic: ^2.7.0`; grep for `package:graphic` across `lib` and `test` returns no matches. Charts in scope are hand-rolled (`ui_graph_canvas.dart`, palette painters).
- **Current behavior**: The dependency is fetched but never imported (within this subsystem and, per grep, the wider `lib`/`test`).
- **Why it matters** (INFERENCE): Dead dependency = build weight + supply-chain surface.
- **OS/product consequence**: none functionally; hygiene.
- **Recommendation**: Confirm across the whole repo, then remove from pubspec.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: ARCH-800 (lottie/globe deps).
- **Tests/measurements required**: build after removal.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-801: Duplicated `DataSource` reader helpers across the RFW library
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `rfw_host/library/helpers.dart:3-13` (`_d/_str/_int/_bool/_sp`) vs. `rfw_host/palette/palette_primitives.dart:27-37` (`_d/_s/_i/_b/_dp/_sp`) — near-identical, "local copies so the palette stays self-contained."
- **Current behavior**: Two copies of the same coercion helpers.
- **Why it matters** (INFERENCE): Drift risk; extra code.
- **OS/product consequence**: none.
- **Recommendation**: Share one helper set (moot if palette is deleted per ARCH-800).
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: ARCH-800.
- **Tests/measurements required**: none.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-802: `ui_graph_canvas` "force" layout is actually a grid
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `ui_kit/ui_graph_canvas.dart:401-445` — `_GraphLayout.compute` only varies column count by `layout`; both "schema" and "force" produce a row/column grid.
- **Current behavior**: The `layout: 'force'` prop implies a force-directed graph but yields a grid.
- **Why it matters** (INFERENCE): Misleading API; surface authors may expect force layout.
- **OS/product consequence**: minor.
- **Recommendation**: Rename modes to `grid`/`schema`, or implement force layout.
- **Deletion/simplification opportunity**: no
- **Dependencies**: none.
- **Tests/measurements required**: none.
- **Effort**: S
- **Migration/rollback concern**: surface prop rename.

### CLEAN-803: `adaptiveVisualDensity` claims to replace `adaptivePlatformDensity` but isn't wired in
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `digital_brain_ui/density/adaptive_density.dart:34-42` doc says it "Replaces the blanket `VisualDensity.adaptivePlatformDensity` in buildDigitalBrainTheme"; `theme/digitalbrain_theme.dart:144` still sets `visualDensity: VisualDensity.adaptivePlatformDensity`.
- **Current behavior**: The adaptive density helper is defined but the theme uses the blanket value; `AdaptiveSpacing` is the only consumer of the module.
- **Why it matters** (INFERENCE): Comment misrepresents reality; feature half-wired.
- **OS/product consequence**: none functionally.
- **Recommendation**: Wire `adaptiveVisualDensity` into a `Theme` override at the appropriate scope, or correct the comment.
- **Deletion/simplification opportunity**: possibly (delete if unused).
- **Dependencies**: none.
- **Tests/measurements required**: none.
- **Effort**: S
- **Migration/rollback concern**: none.

### CLEAN-804: `LiquidGlassShaderPainter` + shader asset are dead
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `glass/glass_material.dart:179-218` painter is never instantiated (guarded by `_shadersEnabled == false`); `pubspec.yaml` still ships `assets/shaders/glass_refract.frag`.
- **Current behavior**: Painter class and shader asset are compiled/bundled but unreachable.
- **Why it matters** (INFERENCE): Dead code + shipped-but-unused asset.
- **OS/product consequence**: none functionally; bundle weight.
- **Recommendation**: Delete with PERF-800; drop the shader asset if unused elsewhere.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: PERF-800.
- **Tests/measurements required**: build after removal.
- **Effort**: S
- **Migration/rollback concern**: none.

### TEST-800: Large RFW stateful widgets (editor, synapse-row, catalog manager) lack focused tests
- **Severity**: Low
- **Confidence**: Medium
- **Evidence**: `app/test/ui_kit/*` covers `ui_registry` and the kit wrappers well (each `ui_*` group has widget tests); no tests target `_CodeEditorBody`, `_SynapseRowWidget._fireSynapse`, `DigitalBrainCatalogManager`, or `UiSurfaceTreeRenderer`'s `neuron:*`/`forui:*`/raw branches.
- **Current behavior**: The highest-risk (security-relevant) code paths in `rfw_host` are the least tested.
- **Why it matters** (INFERENCE): Regressions in synapse-firing/event forwarding would ship silently.
- **OS/product consequence**: Trust-boundary behavior unverified.
- **Recommendation**: Add tests asserting event payload shape and rejection of un-approved synapse types (ties to SEC-800/801), plus catalog invalidation (REL-800).
- **Deletion/simplification opportunity**: no
- **Dependencies**: SEC-800, SEC-801, REL-800.
- **Tests/measurements required**: the tests themselves.
- **Effort**: M
- **Migration/rollback concern**: none.

---

## Answers to subsystem-specific questions

**RFW host — widget dictionary/allowlist, and can a compromised server trigger unsafe actions?**
The dictionary *is* a real allowlist and matches rfw's intended model: `createDigitalBrainWidgets()` returns a fixed `LocalWidgetLibrary` of host-authored builders (`digitalbrain_rfw_library.dart:40-80`), and `RfwRuntimeHost` only ever `Runtime.update`s the `digitalbrain` dictionary plus parsed *data* documents under `['doc', key]`. A remote document cannot execute arbitrary Dart or add widgets — verified against rfw's docs ("remote widget libraries all eventually bottom out in the predefined widgets… the app, not the remote document, controls all side-effects"). **However, the event/callback surface is under-defended.** Server-emitted UI chooses the `synapseType` and payload that flow back through `onEvent` (SEC-800), and two widgets (`_SynapseRowWidget._fireSynapse`, `_CodeEditorBody._runCompileAndStage`) bypass `onEvent` entirely and call the gRPC client directly with attacker-shaped envelopes (SEC-801). `UiKitLink` launches arbitrary URIs with no scheme allowlist (SEC-802). So while *code execution* is prevented, *privileged action selection* (which synapse fires, what URL opens) is effectively delegated to the server — the real trust gap. The additional `type == 'rfw'` branch (`rfw_runtime_host.dart:210-226`) also lets a JSON tree inline-parse a fresh RFW document at runtime; still dictionary-bounded, but it widens the surface and is keyed by `hashCode` (REL-803). Recommendation: introduce a per-surface, kernel-validated binding registry so the UI names an approved *binding id*, not a raw synapse type, and fail-closed on unknown types.

**ui_kit — coherent design system or sprawl? oversized files? P1.14 dead-code residue?**
Coherent, not sprawl: 40 of the 42 files are small, uniform ForUI wrappers with consistent naming and a shared form-scope/nav-event pattern; `ui_registry.dart` is a clean single switchboard. Only `ui_graph_canvas.dart` (538 lines) is large, and it is well-factored. The P1.14 prune is real and mostly clean — the dictionary map documents the removed widgets (`digitalbrain_rfw_library.dart:78-79`). The significant residue is **outside** ui_kit: `palette/palette_primitives.dart` (~810 lines, unregistered, ARCH-800) and the dead `GlassMaterial` shader path (PERF-800/CLEAN-804). Duplicate dispatch authority between the registry and the tree renderer (ARCH-801) is the main structural debt.

**Performance — rebuilds, expensive painters, memory, hot paths.**
Top concern is `GlassMaterial` (PERF-800): per-frame ticker `setState` on hover + dead async shader load, and it wraps most overlays. Second is the Ino editor's per-keystroke whole-document regex highlighting (PERF-801). Painters are otherwise reasonable: `GlowPainter` is raster-cached (`GlowIcon`, good), the clock/route painters have proper `shouldRepaint`, and `ui_graph_canvas` bounds its viewport. The 3D globe is unreachable (ARCH-800) so its GPU cost is moot today. Minor: `BrainSceneEffects.pulses` per-read copy (PERF-802), GlowIcon non-LRU eviction (PERF-803).

**Maintainability, naming, consistency, theme coherence.**
Widget naming and structure are strong and consistent. The glaring hazard is the theme (FRAME-800): color tokens named `teal`/`gold`/`violet`/`indigo` all resolve to near-identical silver after a monochrome redesign that kept the old names — every "pick a color by meaning" call is silently grey, and the tonal state-encoding across TaskRow/timeline/badges is visually lost. `digitalbrain_rfw_library.dart` (3218 lines) badly needs splitting (dictionary vs. editor vs. catalog). Several stale "will be moved here" comments in `library/chat.dart`/`data.dart`.

---

## Coverage note
All 69 listed files were read in full (line 1–EOF each). `digitalbrain_rfw_library.dart` was read across two passes (1–1780, 1781–3218). rfw API/security verified against official pub.dev/GitHub docs because Context7's monthly quota was exhausted for both configured Context7 servers during this session; this is a source substitution, not an unresolved gap.
