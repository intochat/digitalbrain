# DigitalBrain v2 — Slice 7: The Flutter Workspace

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:subagent-driven-development. Checkbox steps.

**Goal:** The attention-first workspace from spec v2 §2/§6 as a NEW Flutter app (`workspace/`) that speaks only to the v2 gateway: a Dart gateway client (invoke/read/describe over HTTP, watch over WebSocket), native renderers for the two-tier UI vocabulary (11 Tier-2 block primitives + 5 Tier-1 governed kinds), the shell (Today + Chat/Abilities/Connections/Activity) and shared inspector, on a v2 theme that restores the semantic chroma the spec calls for. Proven: `flutter analyze` clean, widget tests green for every renderer, and a live Dart-client integration against the running gateway (invoke chat → read the window's rendered blocks → receive a `/ui/watch` frame).

**Architecture:** `workspace/` is a fresh `flutter create` app beside the untouched v1 `app/` (deleted in Slice 8). The gateway client is the ONLY backend contact — no Orleans, no credentials, JSON+WS exactly as Slice 4 serves. Rendering is a closed switch: `BlockView` dispatches Tier-2 documents (`{version,blocks:[…]}`) to one hand-written widget per primitive; `KindView` dispatches Tier-1 semantic kinds. Actions are revision-bound: an `ActionRow` button calls `/ui/invoke` with the action's `{contract,inputJson}`. State is `ChangeNotifier` + `InheritedNotifier` (spec §6), controller+gateway per destination, no bloc.

**Deliberate boundaries / carries:** Pixel-perfect goldens at three breakpoints, full build-out of every destination's bespoke content, real OIDC/session auth in the client, and wiring `workspace/` into the AppHost dev graph are DEFERRED (Slice 8 / a polish pass). This slice delivers the substrate — client + renderers + shell + theme — functional and widget-tested, not visually final. v1 `app/` is untouched. The gateway's dev caller (`actor/ui-dev`) remains cooperative; the hard session-auth gate is unchanged from Slices 4-6.

## Global Constraints

Zero comments in tracked Dart/source · pin dep versions in pubspec (match v1's where shared: http, web_socket_channel) · `flutter analyze` zero issues per commit · widget tests green per commit · v1 `app/` and all C# untouched (root `dotnet test` stays green — Flutter is not in that graph) · the block/kind renderer set is CLOSED (a new visual needs a new widget, never a generic interpreter — RFW stays dead) · Material 3 base + the v2 theme; Inter + JetBrains Mono.

### Task 1: Scaffold + gateway client + models (commit 1)

**Files:** `workspace/` (flutter create), `workspace/lib/gateway/brain_gateway.dart`, `workspace/lib/gateway/envelope.dart`, `workspace/lib/blocks/block_document.dart`; tests under `workspace/test/`.

- `flutter create --org com.digitalbrain --project-name workspace workspace` (or `dart`/`flutter` equivalent producing a runnable app). Add deps: `http`, `web_socket_channel` (versions matching v1 app/pubspec.yaml — read it). Remove the default counter app; `main.dart` boots a minimal MaterialApp with the shell (filled in Task 3 — stub for now).
- `BrainGateway({required String httpBase, required String wsBase, http.Client? client})`:
  - `Future<Map<String,dynamic>> invoke(String address, String contract, String inputJson, String commandId, {int? expectedRevision})` → POST `$httpBase/ui/invoke`; 409 body `{code,detail}` → throw `GatewayException(code, detail)`; 200 → decoded receipt.
  - `Future<NeuronSnapshot> read(String address, {String projection='default'})` → GET `/ui/read`.
  - `Future<NeuronDescription> describe(String address)` → GET `/ui/describe`.
  - `Stream<FeedFrame> watch({int cursor=0, String space='actor/ui-dev'})` → connect `$wsBase/ui/watch?cursor=&space=`; map each text frame; ignore `{ping:true}`; expose reconnect-with-cursor as a documented method (implementation: caller re-subscribes — keep the stream simple, surface the last sequence).
- Models (plain classes, `fromJson`): `NeuronSnapshot(revision, stateJson)`, `NeuronDescription(kind, revision, contracts)`, `FeedFrame(sequence, record)`, `GatewayException(code, detail)`.
- `BlockDocument.parse(String json)` → `{version, blocks:[Block]}`; `Block(kind, Map raw)`; reject version != 1 or unknown top-level shape (defensive parse; the SERVER already validated, but the client must not crash on garbage → throw `FormatException` surfaced as an error tile later).
- Tests (`flutter test`, mocked `http.Client` via a fake): invoke happy path returns receipt; invoke 409 throws GatewayException with code; read/describe parse; BlockDocument.parse round-trips a doc with metric+timeline; watch frame mapping skips ping (feed a fake stream). Commit `feat(workspace): gateway client and vocabulary models`.

### Task 2: Tier-2 block renderers (commit 2)

**Files:** `workspace/lib/blocks/block_view.dart` (+ one small widget file per primitive or grouped), `workspace/lib/blocks/block_action.dart`; widget tests `workspace/test/blocks/`.

- `BlockView(BlockDocument doc, {void Function(BlockAction)? onAction})` renders `doc.blocks` in a Column. A `switch` over the 11 kinds → one widget each: `section`(titled group + children), `columns`(Row of children), `text`, `metric`(label + value, tabular), `field`(label:value row), `list`(bulleted), `table`(header + rows, horizontal scroll), `timeline`(entries), `entry`(title+detail), `media`(network image with `max-width:100%`, alt fallback), `progress`(label + LinearProgressIndicator by fraction), `actionRow`(buttons; each button → `onAction(BlockAction(label,contract,inputJson))`). Unknown kind → a visible "unsupported block" tile (never throw).
- Bounded recursion: section/columns render children via BlockView recursively; the server already caps depth 8.
- Widget tests: each primitive renders its data (find text/labels); actionRow button tap invokes onAction with the right contract; table with jagged row degrades gracefully; unknown kind shows the fallback tile; a nested section renders children. Commit `feat(workspace): native renderers for the block vocabulary`.

### Task 3: Tier-1 kind views + theme + shell + inspector (commit 3)

**Files:** `workspace/lib/theme/brain_theme.dart`, `workspace/lib/kinds/kind_view.dart` (+ per-kind widgets), `workspace/lib/shell/app_shell.dart`, `workspace/lib/shell/today_page.dart`, `workspace/lib/shell/inspector.dart`, destination stubs; widget tests.

- `brain_theme.dart`: Material 3 dark ColorScheme — obsidian surfaces (`#070708`/`#0A0A0C`), Inter (body) + JetBrains Mono (data/mono) via google_fonts, hairline borders, ONE indigo accent (`#6C7BFF`) for liveness, semantic status hues amber `#E8B34B` (needs-you) / green `#4BC98A` (healthy) / orange `#E8734B` (error). Expose as `BrainTheme.dark` + a `BrainColors` token class. Reverse v1's greyscale flattening; keep the calm-instrument identity.
- `KindView(String viewKind, Map data, {onAction})` switch over the 5 Tier-1 governed kinds → `DecisionCard`(approve/decline, revision-bound), `ConnectionHealth`(closed health union → status chip + one fix action), `Conversation`(message stream), `GrantPrompt`(per-scope reasons + consent), `EffectPreview`(what will change + payload digest). Each a hand-written widget; unknown viewKind → fallback tile.
- `AppShell`: persistent nav (Today · Chat · Abilities · Connections · Activity) + working surface + summonable inspector (side panel desktop / sheet compact via a breakpoint). `TodayPage`: reads `local-owner|actor/ui-dev|feed/main` `recent` projection through the gateway and renders it as a block-ish list (attention items); empty state "Nothing needs you." Destinations Chat/Abilities/Connections/Activity are stub pages reading their respective neuron/catalog projections (thin — full build-out carried). `Inspector`: the four fixed sections (Status / Caused by / Depends on / Actions) as a skeleton fed by a selected address's describe+read.
- Wire `main.dart` → `MaterialApp(theme: BrainTheme.dark, home: AppShell(gateway: BrainGateway(httpBase:'http://localhost:5320', wsBase:'ws://localhost:5320')))`.
- Widget tests: DecisionCard approve button calls onAction with effect.approve.v1-shaped action; ConnectionHealth renders each health state's fix action; the shell shows five destinations and Today's empty state; theme smoke (app builds with BrainTheme). Commit `feat(workspace): tier-1 kind views, theme, shell and inspector`.

### Task 4: Live proof (controller)

1. `flutter analyze` in `workspace/` → zero issues; `flutter test` → all green.
2. Silo + gateway up (from prior slices). A Dart integration test or a small `dart run` script `workspace/tool/gateway_probe.dart` using the real `BrainGateway` against `http://localhost:5320`: invoke `chat.post.v1` on `chat/main` → receipt revision advances; render a window: invoke `window.render.v1` (or read an existing window) → `BlockDocument.parse` yields the metric+timeline blocks → assert the parsed structure; open `watch(cursor:0)` → post a chat → assert a `FeedFrame` for the chat arrives. This proves the DART client works end-to-end against the live edge (the widget layer is proven by widget tests).
3. Record the proof; root `dotnet test` still green (Flutter untouched by it — confirm one run).
4. Commit `feat(v2): slice 7 complete — the workspace renders the brain`.

## Self-review

§2 five destinations + Today + inspector ✓ (shell; full destination content carried), §6 two-tier vocabulary rendered natively ✓ (11 blocks + 5 kinds, closed switches, RFW-free), §6 theme reversing greyscale + semantic chroma ✓, §7 JSON+WS client ✓ (no proto). Goldens/breakpoint matrix, per-destination depth, client auth, and AppHost wiring are honest carries to Slice 8 / polish. v1 app untouched.
