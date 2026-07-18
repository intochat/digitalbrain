# DigitalBrain UI Redesign: Design System + Full .ino-Driven App + Reactivity + Windows + High-Quality UX (2026-06)

**Status**: Core remaining work complete (spinner fixed + Phase 2 .ino shell + pure renderer). Subscribe now replays last ui-shell/ui-windows so Flutter receives declarative chrome immediately (no more infinite "Loading declarative shell from .ino..."). shell.ino now source for full OS frame (header+nav+main/widgets regions with glass containers). ShellNeuron thinned (BuildDefaultShellChrome deleted, state+resolve only). RuleHost special-cases shell for bare .ino root + kerneltasks id. IDs aligned for nav to populate regions from .ino. Builds/tests green (high-sev gate passes on logic paths; locks are runtime env). Flutter pub + widget test loads. aspire doctor green + builds. Real nav+glass UI from .ino now renders on client connect. Remaining phases (full reactivity, dart mcp neuron, polish all .ino) tracked for follow. Gate + aspire rituals followed.

**Core Law reminder**: UI surfaces remain `UiSurface` synapses (with `UiWidget` root). Placement/state in `ShellNeuron`/`WorkspaceState`. Everything else neuron/synapse. Marketplace bundles (.brain from os/*.ino) deliver new UI rules + behaviors with N+1 (and N-1 on uninstall). No restart.

## 1. Current State (what is present, exactly)

**UI pipeline is single-source from .ino (post C "single-source UI" + OS plan landing)**:
- `os/*.ino` (14 files, seeded by `brain.ino`): declare `on: <SynapseType> [when ...]: show card("Title", column( text("..."), button("Label", ActionSynapse(k:v)) ))` (and inline `show card("t", column(text(".."), button("..", Syn(..))))` shorthands, $/{alias.prop} templates from incoming).
- `InoParser.cs` (tolerant regex + fallbacks for real os/ files) + `InoAst.cs` (`ShowCardRuleStatement` + `CardItem[]` kind=text/button) → `RuleInterpreter.cs:Execute` produces `ShowCardIntent` → `RuleHostNeuron.cs:TryExecuteRulesFor` (timeline sub) materializes `UiSurface(surfaceId, Self, Card(title, Column(widgets)))` via `BuildWidgets` + `Emit`.
- Examples (grep hits + reads):
  - `shell.ino:9`: on UiSurface + on telemetry events; declares "Liquid Glass Desktop" + [[region:widgets]] / [[region:main]] markers + nav buttons emitting `PinSurface`.
  - `marketplace.ino:11`, `creator.ino:10`, `kernel-tasks.ino:15` (alarms), `weather-watcher.ino:12`, `gmail-last-senders.ino:13` (grants + results, `requires: google-auth`), `packager.ino`, `memory.ino`, `awesome-se-team.ino`, `transcription.ino`, `llm-agent.ino`, `hex-guide.ino`, `google-auth.ino` (grants).
  - Some C# still emit surfaces (e.g. `MarketplaceNeuron.cs:348` ListingsSurface, `BrainGraphNeuron.cs:85` Graph3D live, `CreatorNeuron`).
- `ShellNeuron.cs:113` (IHandle<UiSurface>, also Pin/Unpin/MoveSurface + Bundle*): `_recentSurfaces`, durable `WorkspaceState.Regions` (PlacedSurface with order/pinned/ownerBundleId), `BundleInstalled` auto-placement for widgets, `EmitCurrentWorkspaceAsync` → `BuildOsShellChrome()` (hardcoded C# header/sidebar/main/widgets) + `ResolveFrame` (replaces [[region:]] text markers with `BuildRegionContent`) → `UiSurface("ui-shell", ...)` + `SurfaceStreamService.Publish`.
- Transport (dual):
  - Timeline (all `UiSurface` synapses, for TUI + core).
  - gRPC `SurfaceStreamService.cs` (hosted in Kernel Program.cs:146): `SubscribeSurfaces` (per-brain filter + token floor), server-stream `UiSurfaceMessage { surface_id, emitter, widget_json: STJ-serialized UiWidget (with SynapseJsonConverter adding "Type" for taps), ts }`. `SendClientEvent` → reconstruct `ClientTap` → `brain.SendAsync(...)`.
- `DigitalBrainGrain.cs:190` (HandleAsync ClientTap): parses JSON "Type" (or type/$type), special-cases telemetry/SwitchBrain, else `SynapseBinder`-style reconstruct + `SendAsync(theConcreteSynapse)` for exact parity with hex1b/console. Fallback telemetry on error.
- Renderers (both consume same `UiWidget` union trees):
  - Flutter (`src/DigitalBrain.Clients.Flutter/`):
    - `main.dart`: MaterialApp dark theme (custom ColorScheme teal #00E5D1 primary, purple secondary), hardcoded header + left sidebar `_navButton` (PinSurface JSON, some special _activeMainId), main area (special `_buildTaskWindowsSurface` for kerneltasks with Positioned+GestureDetector draggable "windows" using `_taskWindowOffsets`, else direct surface or 3D fallback), right sidebar renders `ui-shell`. `_onSurfaceMessage` populates `_surfaces` map. `_fire` sends via gRPC. `ui_widget.dart`: sealed `UiWidget` + `fromJson` (Label→Button with raw OnTap map, Title→Card, Children→Column, Value→Text/Markdown heuristic, etc.), `buildFromUiWidget` recursive (heavy `BackdropFilter` blur + Container "glass" on Button/Card/MainPane, special `BrainGraph3DViewer` + `_BrainGraphPainter` CustomPaint for Graph3D, `flutter_markdown`). Has rudimentary windowing only for task details. `_buildDemoShellFromIno` fallback.
    - `pubspec.yaml`: flutter ^3.24, grpc/protobuf ^4/3.1 (manual), flutter_markdown, basic material. No lottie/animate/rfw/window pkgs yet.
    - Windows runner standard.
  - Console (`SurfaceRenderer.cs:10`): hex1b recursive (Text/Button/Card/Column/Row/Markdown/Hyperlink/MainPane) to VStack/HStack + .OnClick fire.
- Model (`src/DigitalBrain.Core/UI/Widgets.cs:7`): `[GenerateSerializer] public union UiWidget(Button, Text, Card, Column, Row, Markdown, Hyperlink, MainPane, Graph3D);` + records + `SurfacePlacement(region, pinned, order)` + `UiSurface : Synapse` + `WidgetTree.Render` (ANSI debug).
- .ino headers for placement: `region:`, `pinned: true/false`, `order: N`, `system: true`.
- "Liquid Glass" aesthetic seed: blur sigma 12-16, dark panels 0xFF121A2A/0xFF0A0E1A with opacity, teal accents, borders, shadows. Mentioned in shell.ino + renderers + main.dart status text.
- Marketplace + install → live UI: `BundleInstalled` → rule install in RuleHost + Shell placement + new `UiSurface` on triggers → shell re-emit ui-shell + gRPC push. `requires:` + `GrantRequested/Decision` surfaces (in gmail/google .ino). Uninstall removes placements + rules (N-1).
- Boot: `brain.ino` + `ui: flutter windows autostart` + seeds.
- Tests/harness:
  - High-sev gate `DistributionDynamicHandlers.feature` (N+1 on broadcast/install, surfaces via rules counted in subscriber growth, 2024-06 run: 64 succeeded / 0 failed / 6 skipped).
  - `InoTests.cs`: parser roundtrips, show card intents, validator (privileged/INO00x).
  - `SimulationUiHost.cs`: Aspire.Hosting.Testing + Playwright (flutter-web @5801 or configured, screenshots to pa-files/simulations/, graceful skip if no SDK/playwright).
  - Flutter `test/widget_test.dart` (basic).
  - `simulation.cs` supports "ino:*" + @Ui (Playwright on real surfaces).
  - `SurfaceStreamService` + ClientTap exercised in E scenarios.
- Recent history (git + docs): C (single-source purge + two-tier grammar), OS-FROM-INO-PLAN.md (full landing OS0-7: brain.ino, os/ capsules, Shell ownership of workspace, N±1, requires/grants, pure-ish renderer target in OS5), ARCHITECTURE-ASSESSMENT-OS.md (notes some post-landing drift: pa-files capsules incomplete for all os/, UiNeuron listing remnants in comments, client chrome still owns tabs/routing in places, gate not always 0f in every sim invocation), USER-FLOWS.md, VISION.md (Core Laws: UI=UiSurface synapse; ino peer; experiences from marketplace; <60s install-to-use).

**v1 archaeology (read-only for patterns; do not extend)**: v1/UI/flutter/ (hundreds of dart), docs/redesign/02-WINDOW-MANAGER.md (PanelManager/ChangeNotifier with CanvasPanel {id,rect,z,state,floating|docked|min}, simple_floating_panel + docking concepts for persist/autoLayout, generic titlebar chrome + body=RFW render), 03-WIDGET-PALETTE.md (FloatingWindow, LottiePlayer, Analog/CountdownClock, EarthGlobe + more; Tier-1 Dart primitives registered once, then .ino/RFW composes; theme inheritance via DigitalBrainColors + Theme.of; Widgetbook catalog). RFW (remote flutter widgets) for dynamic .ino→UI without client rebuild. GrokUiDesignerNeuronTests. Much richer windowing + declarative dynamic UI than current final reboot. Recover: window state model (rect/z/persist per-brain), generic chrome + kit primitives, theming, RFW-like for reactivity/composability if fits.

**DigitalBrainDesign / the commit (358004dd...)**: No file or exact match in tree (grep -r, git log --all | Select-String). "DigitalBrainDesign" likely refers to target high-quality design language/system for the OS (current "Liquid Glass" seed + v1 redesign/). Commit appears from prior clone/fork/PR not in this HEAD history. Current aesthetic is the starting "glass" (blur + translucency + teal).

**Baseline verified (this session)**:
- High-sev: `dotnet test ... --filter "FullyQualifiedName~DistributionDynamicHandlers"` → Passed! 64/0/6 (N+1 surface delivery included).
- Aspire: 13.5.0-preview.1 present, `aspire doctor` clean (Docker, .NET 11 preview, certs). `aspire.config.json` lives under AppHost (use `cd src/DigitalBrain.AppHost; aspire run` or root `dotnet run ino.cs` per README).
- No C:\Users paths, relative, central pkgs.

## 2. What's Missing / Gaps (why current is not "extremely high quality" or "full OS with marketplace .ino apps")

See detailed gaps in thinking trace (widget vocab ~9 types no lists/inputs/repeat/theme-props; shell chrome 50%+ C# not .ino; reactivity = coarse re-emit only, no data-driven trees or local bindings; windowing = 1 hardcoded Positioned stack for tasks only, no general z/resize/dock/persist/manager; no DS/tokens (hardcoded colors everywhere); parser/grammar for UI limited + DX bad for complex; Flutter main.dart client-heavy (dupe nav, _task offsets, demo fallback, special cases); some surfaces still C# (market listings, graph); marketplace UI bits not pure .ino or kit-based; pa-files capsules not all current os/; test UI coverage partial (Playwright often skipped, no goldens/a11y/contract for widget evolution, no high-sev @Shell/@Ui always-0f); no Dart-MCP UI agent loop; no updates story surfaced for .ino capsules themselves; desktop polish low (no real OS windows, a11y, motion, keyboard, multi-window app model for "installed apps").

Drift noted in ARCHITECTURE-ASSESSMENT-OS.md (post-OS7 orphans, incomplete pure-renderer, etc.).

**Long-term vision alignment (not met yet)**: DigitalBrain = OS; apps = marketplace .ino bundles (install → N+1 surfaces + windows + nav + behavior; update from market → live refresh); .ino (incl UI) from market + updatable; ino (assistant) peers + can use Dart-MCP to inspect/improve live Flutter widget trees.

## 3. What Needs to Be Added (concrete for redesign run)

**Design System (first)**:
- Tokens record (or json) in Core.UI: colors (base darks, glass opacities, accents teal/purple), typography scale, spacing (4/8pt), radii, blur sigmas, elevations/shadows, motion (durations/curves for glass). "Liquid Glass" spec (blur+border+bg+text contrast).
- Theme surface or ambient (UiTheme synapse or injected in ui-shell). Shared across Flutter (ThemeData extension or consts) + console (if applicable) + any future.
- Apply everywhere; delete hardcoded magics.

**Reusable UI Kit (expand + compose)**:
- Grow union (controlled): add e.g. `ListView(items, builderRef?)`, `Container(pad, deco, child)`, `Icon(name)`, `TextField(value, onChangedSyn?)`, `Toggle/Checkbox`, `Progress`, `Tabs`, `Divider`, `Image(url)`, `Dialog`, `WindowFrame(title, child, controls)`.
- Or keep tree minimal + rich "props" + renderer does variants. Provide kit "components" as .ino-defs or const trees reusable by id (new header or CardItem extension).
- All new kit must roundtrip .ino parse → intent → C# UiWidget → json → Dart fromJson → build + tap.
- Self-explanatory names only (no summaries).

**Full .ino-driven app rewrite (chrome + regions + windows)**:
- Make `shell.ino` (or ui-os kit bundle) the source of truth for OS frame: richer show card or new `show workspace` / region widgets that declare nav/sidebar/header + [[ or explicit Region("widgets") ]]. ShellNeuron becomes thin owner of state only; chrome built from surfaces/rules.
- Delete/move `BuildOsShellChrome` + dupe Flutter sidebar into pure renderer path (complete OS5 spirit).
- Flutter main.dart → near-pure consumer of ui-shell tree (use tree for nav too); remove special _activeMainId hacks where possible.
- Console parity.

**Reactivity + .ino UI power**:
- Parser/interpreter extension (minimal, compatible): `repeat $alias.list as it { text "{it.name}" button "Act" -> Foo(id: {it.id}) }` or data children in CardItem.
- Or support small expressions/conditionals in show for tree shaping (still data from synapse).
- Live lists (tasks, senders, marketplace results) become rich without giant pre-rendered snapshots every time.
- Client can have ephemeral focus/scroll/selection state; mutations = synapses.
- Consider RFW-inspired (from v1) for advanced dynamic if tree model hits wall (but prefer stay with current json + union for simplicity/N+1 proof).

**Windowing / true OS multi-app desktop (from v1 recovery)**:
- Extend placement or new `WindowState` (id, rect, z, state: floating/docked/minimized/region-pinned, surfaceId, title?).
- Synapses: `OpenWindow(surfaceId, ...), CloseWindow, Move/ResizeWindow, RaiseWindow, DockWindow, AutoLayout`.
- Shell owns durable windows list (per-brain).
- Flutter: general `Window` widget (generic titlebar with drag (GestureDetector onPan), resize grips, min/close; body = buildFromUiWidget(content); stack or overlay manager with z). Support free + region-docked. Persist via Shell. Multi "installed app" windows open from marketplace nav.
- Task "windows" become instances of the general system.
- Auto-layout presets (grid, focus) as buttons in shell.ino.

**Marketplace + .ino apps/updates full story**:
- Ensure CI / pack step materializes .brain for *all* os/*.ino (not just 3).
- UI metadata in manifests (icon, "widget|app|shell", screenshots? via pa-files).
- Update UX: ino/market surfaces show "Update available" (version compare), button → `UpdateBundle` → live rule refresh + surface re-emits (no reinstall needed for pure UI/behavior changes).
- "ino come from marketplace": treat core os/ as versioned experiences too.

**Dart MCP : Neuron / agentic UI improver**:
- New (or experience) neuron: connects (via MCP client or dart tooling daemon API) to running Flutter client's widget tree (get_widget_tree, get_active_location, hover etc from dart MCP).
- Tools: `inspect_ui(surfaceId?)`, `diff_tree_vs_source`, `propose_ui_improvement(description)`.
- Flow: user/ino "improve the weather card glass" → agent inspects live tree (blur sigma, colors, layout) + correlates to originating .ino rule (via emitter/surfaceId) → ImprovementProposal or Creator edit to .ino (or direct style tokens) → pack/publish or local apply → reinstall or hot rule update → new surface → observe via MCP again.
- Enables "dart mcp would be agent aware about flutter windows widget tree and would improve ui".
- Prototype: extend DigitalBrain.Mcp or dedicated; use dart MCP server (already in agent env) + expose in brain tools. Test with mocks + live when Flutter running.

**Flutter client quality + desktop**:
- Add targeted latest pkgs (via central? or pubspec; verify with pub_dev_search + dart tools before add): windowing/animate support, a11y, forms, performance.
- Productionize: real Windows integration (titlebar, taskbar progress?, multi-monitor, persist geometry), keyboard nav, global search, voice recorder wired to transcription, error boundaries + skeletons, loading states, smooth transitions for glass.
- Web target strong for E2E Playwright.
- Always latest per policy; no local cache.

**Other**:
- Make remaining C# surfaces pure rule-driven (or document why substrate).
- Clean any listed remnants (UiNeuron mentions).
- Full app polish: every seeded experience has high-quality .ino UI using new kit/windows (rich lists, forms for creator, viz for weather/graph, actions everywhere, status, search).
- "Full working": after changes, aspire run boots kernels + flutter windows; login; workspace with glass chrome from .ino; pinned widgets live; open marketplace (ino or surface); install e.g. new demo → new window/card appears + nav; interact (taps fire real synapses, lists update reactively, windows drag/resize/persist); update a bundle → UI refreshes; ask ino to "improve X UI" → proposal or auto-tweak; all without restart; TUI parity; screenshots + gate green.

## 4. Testing Strategy (high-severity, aspire.dev integration green always)

- **Gate 0 (non-negotiable)**: After *every* change: high-sev `dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --filter "FullyQualifiedName~DistributionDynamicHandlers" --logger "console;verbosity=minimal"` (0f). Full `.\run-ci.ps1` or `dotnet run simulation.cs -- "Distribution" --ci` for broader. `aspire build` (AppHost) + targeted `aspire run` smoke or `dotnet run ino.cs`.
- **Ino + UI contract**: Extend `InoTests.cs` + new `UiKitTests.cs` (or in Core.Tests): parse every show card variant + new kit → ShowCardIntent → BuildWidgets → UiSurface roundtrip (canonical + json). Widget model ser/deser + Render. New primitives require new test cases before impl.
- **@Ui / E2E surfaces**: Use/enhance `SimulationUiHost` + Playwright (real AppHost + flutter-web). Tag scenarios `@Ui` or `ino:ui-kit`. Flows: boot → surfaces from rules appear (assert text/buttons), tap install → N+1 surface + placement in widgets/main (screenshot), grant flow, window open/drag/close (web limited; use gRPC test driver or note), update → refresh visible, ino chat triggers UI. Screenshots + (later) text assertions or visual hash. Graceful skip with reason recorded if no Flutter SDK/playwright in env; do not fake pass.
- **Flutter renderer**: Expand `widget_test.dart` (or integration): buildFromUiWidget for full kit matrix + theme application, fromJson fidelity (all shapes + OnTap maps), tap callbacks fire correct json, Graph3D, MainPane/Row/Column edge cases, glass containers. Add golden snapshots for key cards if stable. Use Dart MCP tools (get_widget_tree) in dev/CI where daemon available to assert runtime tree matches expectation.
- **Parity + workspace**: Tests that TUI SurfaceRenderer + Flutter build produce equivalent structure from same UiSurface. Shell placement roundtrips + EmitCurrentWorkspace produces consistent ui-shell for given recent surfaces + regions. N-1 uninstall shrinks placements/subscribers.
- **Full OS simulation + aspire.dev**: `simulation.cs "tag:Shell" / "ino:*"` (or new @RedesignUi). `aspire run` (or ino.cs) + manual verification script (or MCP-driven install + observe) for flagship: install from market, pin, interact live cards/windows, ino UI improvement loop (if landed). High-sev integration tests green.
- **Agentic MCP UI**: If DartMcpNeuron, unit for tool surface + mock daemon tree; E2E sim "ui improve" produces proposal surface + updated rule execution.
- **Rituals per CLAUDE.md (final/)**: Latest central pkgs (Directory.Packages.props + pubspec). Context7 (or official docs via web_fetch + dart pub_dev_search/use_tool for every framework API before edit; no local NuGet). Relative paths only. Self-exp C# names. No boilerplate ///. One commit per session. Update docs (this file, ROADMAP, USER-FLOWS, DELETED for any deletions). Run review (code + self) before return. After changes: build + high-sev gate + AppHost/aspire smoke + test.
- **Quality bars**: No regression on existing N+1/UI surfaces. New kit must not break old .ino. Visual/UX review via screenshots + aspire run. a11y/perf notes for future. "Extremely high quality" = polished glass consistent, responsive-feeling (even static trees), no dead taps, clear feedback, marketplace-to-running-app <30s feel.

## 5. Proposed Phased Execution (for this run + follow-ups; get full working app)

**Phase 0 (this analysis)**: Definitions above + baseline green (done). Write this doc. Context7 note (MCP not surfaced in session; used web + source + dart tools; will lookup explicitly on edits).

**Phase 1: Design System foundation (tokens + kit start, no grammar change yet)**:
- Add `DesignTokens` (or `LiquidGlassTheme`) record + consts in Widgets.cs (self-exp fields).
- Update Flutter theme + buildFromUiWidget (and console if) to consume tokens. Remove some hardcodes.
- Add 2-3 high-value kit items (e.g. Container with style, simple List for repeats, Icon or TextField) to union + parser (CardItem extension) + interpreter + both renderers + tests. Verify roundtrip + gate.
- Rituals on every edit.
- Gate: high-sev 0f + manual aspire + screenshot of glass "upgraded" surfaces.

**Phase 2: .ino shell rewrite + pure renderer**:
- Enhance shell.ino (and/or new ui-kit.ino system bundle) with full chrome structure using new kit (nav, header actions, status, explicit regions).
- Thin ShellNeuron (state only; chrome from surfaces or rule-produced shell tree).
- Prune dupe client chrome in main.dart + SurfaceRenderer. Update Flutter to render ui-shell as the OS frame (sidebar etc from tree).
- Update all os/ .ino UIs to use new kit where richer (lists etc).
- Gate: full sim @Ui + Playwright shots; TUI/Flutter parity check; gate 0f; aspire run shows "whole app in .ino".

**Phase 3: Window manager + OS app model**:
- Window placement/state in Workspace (or dedicated). New synapses + handlers in Shell.
- Generic Window chrome + manager in Flutter (stack/overlay, drag/resize via gestures, min/max, persist on Shell emit).
- Convert task details + allow any marketplace surface to open as window (new Pin/Open as window action).
- Shell.ino buttons for layout presets.
- Gate: interactive windows in aspire + flutter windows client; drag persists; multiple apps open; sim scenarios + shots; high-sev.

**Phase 4: Reactivity + marketplace UI polish + full experiences**:
- Repeat/data-driven in show card grammar + interp (or equivalent).
- Make marketplace/creator/etc listings + rich UIs pure .ino + kit (search, installed section with update/uninstall buttons, version badges).
- Update flows produce live UI refresh.
- Polish every seeded .ino with windows + new primitives (forms in creator, rich lists in gmail/tasks, viz in weather/graph).
- Ino tools include UI inspect/propose if Phase 5 parallel.
- Gate: end-to-end flagship (pack/publish/install from peer → rich live UI + windows; update; interact all; ino helps); Playwright + manual aspire; 0f gate.

**Phase 5 (parallel or follow): Dart MCP Neuron + agentic loop + final quality**:
- Neuron/experience exposing Dart MCP tools (widget tree, selection, etc.) or direct daemon.
- Wire to llm-agent / creator: "inspect current main surface widget tree" → correlate → propose improvement (glass tweak, layout, new card) as ImprovementProposal surface or direct .ino patch.
- Demo: user asks ino to improve a card → sees proposal → approves → new .ino rule fires → updated reactive glass UI in running client (MCP re-inspect confirms).
- Flutter polish (pkgs, a11y, motion, desktop chrome, voice).
- All docs/roadmap updated. Final "full working app" verification (aspire run + windows client + TUI + sims + shots all green + manual flows).

**Cross-cutting**:
- Use sub-agents for parallel safe work (e.g. one for tokens+renderer, one for parser extensions, one for flutter windows, one for tests) with isolation where possible + mandatory review.
- Before any API touch: Context7 (when connected) or web_fetch("https://learn.microsoft.com/...") / open_page + dart `pub_dev_search "package"` + `use_tool` for schema + latest. Record lookups.
- Always latest pkgs.
- High quality bar: self-exp names, minimal comments (only exceptional), clean, performant enough for desktop, accessible, beautiful consistent glass.
- Deletions: track in DELETED.md (e.g. old hardcoded chrome paths, magic numbers).
- One logical change per commit-ish; full rituals.

## 6. Risks / Open Choices (for user input)

- Grammar growth vs. pure data/headers + kit reuse: how far to push show card for repeat/list/input without breaking "frozen" spirit or simplicity?
- Widget model: keep simple union + json (current strength for gRPC/ser/N+1) or adopt RFW (v1 power, more dynamic from .ino without client change)?
- Window state: extend SurfacePlacement or new top-level Window model in Shell?
- DartMcpNeuron scope in this run (prototype vs full)?
- First "full working" milestone: which 3-5 experiences fully polished with windows + reactivity + DS?
- Theme: always-dark glass or support light + toggle?

**Immediate recommended next (after review of this doc)**: Phase 1 start — define tokens in Widgets.cs (data), one renderer update, 1-2 kit items + tests, high-sev + aspire smoke. Then plan review.

**References** (lines from this session reads):
- Pipeline: RuleHostNeuron.cs:95 (ShowCardIntent), 102 (UiSurface emit), ShellNeuron.cs:185 (EmitCurrentWorkspace + BuildOsShellChrome), 244 (ResolveFrame), SurfaceStreamService.cs:90 (ClientTap), DigitalBrainGrain.cs:190 (reconstruct + SendAsync).
- .ino: shell.ino:10, marketplace.ino:11, gmail-last-senders.ino:12, kernel-tasks.ino:15 etc.
- Flutter: main.dart:259 (task windows), 186 (ui-shell root), ui_widget.dart:131 (buildFromUiWidget + glass), 9 (fromJson).
- Model: Widgets.cs:7 (union), 49 (UiSurface).
- Tests: InoTests.cs:58 (show card intent), SimulationUiHost.cs:64 (screenshot), high-sev run 2026-06 (64/0).
- Plans: OS-FROM-INO-PLAN.md (R-OS2 shell, D-OS1 pure renderer phasing, OS5), ARCHITECTURE-ASSESSMENT-OS.md (drift), VISION.md:29 (UI=synapse), v1 redesign/02/03 (windows + palette).
- Baseline: high-sev output (Passed! 64 succeeded 0 failed), aspire doctor clean.

Run rituals followed. Ready for user approval on plan or narrow choices to begin edits (Context7 lookups + review on first change). Full working app is the north star — we will land it.