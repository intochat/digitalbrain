# TUI Remake Plan - DigitalBrain Console Client (final/)

## Goals (from user query + project constraints)
- Fix: No Aspire dashboard URL shown (or active) after `dotnet run start.cs` in TUI (especially Settings tab). The URL must be real/active/valid for the *current cluster*, or clearly indicate simulation with a way to make dashboard active.
- Remake **ALL** TUI from scratch (no incremental trash patches on old tab code).
- Create UI plan in Markdown (this file) describing a "real app" look.
- Implement using Hex1b following https://hex1b.dev/guide/theming (use Theme, Hex1bAppOptions, built-in or custom colors/borders/styles for polished, modern, dark "real app" feel - think status bars, cards, prominent actions, no raw text dumps).
- Fix completely broken "Start personal (root)" and "Start work (example-world)" buttons in Settings.
- Result: appropriate, clean, no trash, professional TUI. All features work (world starts actually launch real or sim clusters and update UI live via timeline, dashboard link works and is clickable/active when possible).
- Verification required (post-implement): 
  - `dotnet run start.cs` shows proper dashboard (real if possible, or actionable sim with launch action).
  - Buttons work end-to-end.
  - Use **aspire CLI** (run, ps, logs, dashboard, otel, etc.).
  - Use **aspire MCP tools** (search/use for dashboard, resources, telemetry).
  - Check **telemetry, logs, metrics** (OTel flowing to aspire dashboard, visible via aspire otel logs/traces/metrics or dashboard UI).
  - High-severity tests green (Reqnroll DistributionDynamicHandlers.feature + E2E/NeuronE2ETest that boot AppHost + aspire resources).
  - After changes: aspire build + aspire run + tests (per CLAUDE.md).
  - Full code review (self-explanatory names, no /// summaries, neuron/synapse law, latest pkgs, relative paths, no C:\Users, Context7 lookups done).

## Constraints (CLAUDE.md + Core Law)
- Everything Neuron or Synapse. No exceptions. TUI is thin client (uses timeline sub for WorldConnectionInfo/UiSurface, brain.Send for actions). World start is via IDigitalBrain.StartWorldAsync (grain + launcher).
- `final/` only. Relative paths.
- Fast loop `dotnet run start.cs` + targeted tests. Full `aspire run` for hosting/dashboard.
- **ALWAYS** Context7 (search_tool + use_tool for resolve-library-id/query-docs) for *ALL* package/framework APIs (Hex1b, Aspire.Hosting 13.x, Orleans grains/Launch, OTel) *before* any edit or subagent. Fallback official docs/web only.
- Use aspire MCP tools (search first).
- Latest nuget (via Directory.Packages.props + verify).
- No default /// <summary>. Only small inline comments in exceptional cases.
- Self-explanatory C# names (e.g. CurrentClusterDashboardUrl, StartWorldButton, PolishedSettingsPanel).
- Run high-severity tests (Reqnroll sims are the "aspire.dev integration tests"). Make them green.
- After *any* change: aspire do build, aspire run (non-int where possible), test.
- Verify with telemetry/logs/metrics (aspire dashboard receives OTel from the app; use cli/mcp to inspect).
- Enter/exit plan mode for this high-impact full remake.
- Code review at end.
- "Simulation = neuron = test": cover dashboard + world start with Simulation paths + real AppHost.
- Dashboard for "current cluster": tie to WorldConnectionInfo (synapse already enhanced in prior work with DashboardUrl). For pure start.cs (local sim cluster with no AppHost), provide actionable path to real dashboard (e.g. auto-launch standalone `aspire dashboard run` best-effort + capture url, or clear "sim - launch dashboard" action).

## Current State Diagnosis (from exploration with relative reads/greps in final/, web for hex1b + aspire docs)
- TUI entry: TaskManagerClient.RunAsync (called from start.cs with aspireDashboardUrl=null, or Program.cs with real from launcher). Creates Hex1bApp with VStack > TabPanel (4 tabs) + InfoBar.
- Tabs (current "trash"):
  - Settings: basic text + world list (from KnownWorlds populated by WorldConnectionInfo via timeline sub or history seed or DoRefresh), "Refresh/Start personal/Start work" buttons in HStack, versions lineage text. Uses state.AspireDashboardUrl (init-only) and wld.DashboardUrl (from recent WorldConnectionInfo enhancement).
  - Ino: chat + surfaces (via SurfaceRenderer for UiWidget from brain), shows dashboard link if present.
  - Creator/Marketplace: editor/surface + actions.
- World start buttons: call ClientState.DoStartWorld -> brain.StartWorldAsync (grain impl always does DigitalBrainLauncher.LaunchAsync(AspireHosted) + Emit(WorldConnectionInfo) + persist in CustomState). TUI receives via sub -> ApplyWorld -> list update. Notifications on result.
- Why buttons "don't work" (likely):
  - In pure `dotnet run start.cs` (direct local silo/client, no LaunchResolver set on IDigitalBrain like in Program.cs): grain's StartWorld still calls launcher (spawns child "dotnet run AppHost" with envs for world). Child may fail (no ollama/gemma for child? path/workingdir from grain process? port conflicts with the parent start.cs ports? 60s connect timeout in launcher returns DefaultDigitalBrainClient -> placeholder info in grain (even after our "use launchResult" improvement) -> "start world failed" notif or useless gw in list. Emit may succeed but UI doesn't "feel" like it worked (no visible cluster spinup, no real connect).
  - No progress/feedback during long launch.
  - GetCurrentWorld in direct path often null (no DIGITALBRAIN_* envs set in plain start.cs).
  - Timeline sub is on the *meta* cluster; child worlds are separate clusters (peer via gw addr).
  - No real "current cluster" switch in TUI (list is just known, "set as peer" only updates LastPeer for marketplace).
- Dashboard problem in start.cs:
  - start.cs deliberately "local sim" (own silo, gemma, no DistributedApplication/AppHost). No aspire dashboard process runs -> no real printed "Login to the dashboard at ..." url -> passed as null -> sim note in info/Ino/Settings.
  - Prior change tied it to WorldConnectionInfo.DashboardUrl (populated only on *real* launcher success from AppHost spawn) and AppHost env wiring for kernels/flutter. Good for full `aspire run` path + --world launcher path. Useless for direct start.cs.
  - Hardcoded 18888 (previous) was invalid (no listener).
  - User wants it visible + "active" + "for current cluster" even after direct start.cs.
- Hex1b current usage: raw ctx.VStack/TabPanel/HStack/Text/Button/Markdown/Rescue/TextBox/InfoBar. No Theme/ Hex1bAppOptions. Looks basic/console-dump-y ("trash").
- From web (hex1b.dev/guide/theming + github): 
  - `new Hex1bApp(builder, new Hex1bAppOptions { Theme = Hex1bThemes.Sunset /* or Dark, custom */ })`.
  - Theming for colors, borders, widget styling. Built-in themes. Custom via provider.
  - React-like declarative, full-screen alternate buffer, input/focus, etc.
  - Other widgets from docs/roadmap (SplitButton, Table, Accordion, Navigator for "real app" pages, etc.).
- Aspire dashboard: printed by AppHost host at runtime (scrape in launcher works). Standalone `aspire dashboard run`. Env ASPIRE_DASHBOARD_*. OTel flows to it (logs/traces/metrics). Resources visible in dashboard. For verification: aspire ps, logs, otel logs/traces, dashboard url in output.
- No current "real app" polish: no sidebar, no cards for worlds, no prominent status for "current cluster + its dashboard", no live telemetry pane, mixed slash + buttons, basic info bar.
- Other: SurfaceRenderer for dynamic brain-driven UI (good, keep). ClientState for local tab state + timeline apply/seed. Works for real + sim.

## Multiple Approaches + Trade-offs (explored)
1. **Incremental polish on existing tabs** (low effort): Add Theme to Hex1bApp, use more HStack/VStack with styling, show dashboard from env in start.cs, make buttons call a wrapped launcher with better feedback. Trade-off: still "trashy" base layout (user said "remake all... from scratch"), doesn't address "completely from scratch" or "more look like real app".
2. **Full from-scratch TUI with plan + theming + sidebar/nav** (chosen): New structure (e.g. extract ModernDigitalBrainTui class or keep static but rewrite Build* with theme applied at root, use cards/sections for "real app" feel, dedicated DashboardHeader always visible, world "cards" with status + start/open-dashboard buttons, live "Telemetry" section pulling from aspire or local). For start.cs dashboard: best-effort launch of `aspire dashboard run --detach` (or via MCP/cli), capture url from its output (like launcher does), set into state, show active link + "current cluster: sim-root" note. World buttons: wrap in progress state, use client-side launch where possible or improve grain path + notifications + auto-refresh list. Trade-off: more work (but user asked "completely from scratch"), but satisfies "remake", "real app", "no trash", "all works", "verify with aspire/telemetry". Aligns with Core Law (keep thin, use existing synapses like WorldConnectionInfo for cluster+dashboard, Emit for updates). Matches roadmap mentions of richer widgets (Table for worlds, Navigator?).
3. **Switch to different TUI lib or Flutter-only**: Out of scope (project uses hex1b for console REPL, Flutter is separate renderer for surfaces). Would violate "implement in hex1b".
4. **Always require aspire run for TUI**: Breaks "fast loop: dotnet run start.cs". User explicitly uses start.cs and wants dashboard there.
Chosen: #2. Plan first (this md), then implement (post exit_plan_mode + user approve implied by continuation). Use Context7 for hex1b theme APIs + aspire dashboard launch/otel before edits.

## Detailed UI Plan (Real App Look, Hex1b Theming, Structure)
**Overall App Feel (inspired by real modern CLI/TUI apps like lazygit, htop polished, or aspire dashboard itself + hex1b examples)**: Dark modern theme (blues/teals accents per project "DigitalBrain"), clean cards/sections with borders, prominent action buttons (colored via theme), always-visible header for "current cluster + its aspire dashboard (clickable/open)", sidebar-like nav or enhanced top tabs with icons (text+emoji for hex1b compat), status footer with live cluster info + metrics hint, notifications as non-intrusive bottom or side (use InfoBar + list). No raw multi-line dumps; use cards, short rows, markdown for content. Responsive to timeline (live world list updates, dashboard appears when cluster launches). "No trash": every element has purpose (world card shows id/gw/dash/status + actions; no long version text walls - collapsible or link to Ino chat).

**Hex1b Theming (per https://hex1b.dev/guide/theming + github)**:
- At root Hex1bApp creation (in RunAsync): `new Hex1bApp(..., new Hex1bAppOptions { Theme = Hex1bThemes.Dark /* or create custom with project colors: primary teal #00C4B4, bg dark, accent purple, borders subtle, success green for active worlds */ })`.
- Theme affects colors for Text/Button/Tab/InfoBar/Markdown/links, borders, focus, etc. Customize for "real app": accent for dashboard links + start buttons, dim for sim notes, highlight for current cluster card.
- Use throughout: styled buttons (primary for world starts), cards via VStack with border (if theme supports or manual), sections with headers.
- Lookup (Context7/official): confirm Hex1bThemes.*, Hex1bAppOptions.Theme, how to define custom ITheme or provider before coding.
- Polish: consistent padding/spacing (HStack/VStack gaps via context?), focus management for keyboard (Tab/Enter as noted in old footer).

**New Structure (from scratch rewrite of TaskManagerClient.cs + supporting; keep SurfaceRenderer + ClientActions + ClientState as-is or lightly extend for new state like CurrentCluster, DashboardLaunchInProgress)**:
- Root: Hex1bApp with Theme + VStack (HeaderBar | MainContent (sidebar nav + content or enhanced TabPanel) | StatusFooter).
- **HeaderBar** (always visible, "real app" chrome): HStack( AppTitle "DigitalBrain", CurrentClusterBadge (worldId + "sim" or "aspire"), DashboardLink (if real url on current: Markdown clickable or Button "Open Aspire Dashboard" that launches if needed + updates url; else "Launch/Attach Dashboard" action) , Spacer, PeerInfo, Quit hint).
- **Nav**: Keep TabPanel for main areas but themed + with counts/badges (e.g. "Settings (3 worlds)", "Ino (live)"); or migrate to Navigator/Split for more "real app" page feel if hex1b supports (from roadmap). Tabs: 
  - **Cluster / Settings** (renamed/enhanced from Settings; the home for "current cluster dashboard + worlds management").
    - Prominent "Current Cluster" Card: shows name/gw, Aspire Dashboard section (big link or "not active - [Launch standalone dashboard for this sim cluster]" button that does Process.Start("aspire", "dashboard run --detach") or equivalent via cli helper, captures url like launcher, sets state, shows active link. Uses env ASPIRE_DASHBOARD_URL if present).
    - Worlds Management: nice list (use Table or repeated Card rows via VStack) of known worlds (from KnownWorlds + WorldConnectionInfo.DashboardUrl). Each: id | cluster | gw | dashboard (link if present) | status | actions ( "Switch to / set peer", "Restart kernel" via IAspire if available).
    - Big action buttons row (themed primary buttons): "Start personal (root) world", "Start work (example-world)", "Refresh all". 
    - On click: show inline progress (notification + temp "launching..." card), call DoStartWorld (improved), on success live update list via timeline (WorldConnectionInfo with real dash/gw now), set as current if appropriate. Use better error + "view launch logs" (if captured).
    - Versions/lineage: collapsed section or moved to "About" or Ino-seeded.
  - **Ino** (chat + awareness): keep but polish with theme (chat bubbles via cards? Markdown surfaces prominent). Dashboard link in header (not duplicated).
  - **Creator**: ino editor (multi TextBox) + preview card + primary "Pack" / "Publish" buttons. Themed.
  - **Marketplace**: surface (rendered) + peer query + table-like listings with install buttons. Clean rows.
- **Footer/InfoBar enhanced**: themed, shows connected/sim + current cluster dash summary (short) + telemetry hint ("OTel to aspire dashboard") + shortcuts.
- **Live updates**: rely on existing timeline sub (add case for any new dashboard synapse if needed, but use WorldConnectionInfo). On world start success, Apply + refresh list + auto "current" highlight.
- **Dashboard for start.cs specifically**: 
  - In RunAsync for start.cs path: attempt best-effort standalone dashboard launch (new helper: try Process for "aspire dashboard run" detached, parse its stdout for url like launcher, timeout short, set into state.AspireDashboardUrl or per-sim "current world" entry).
  - If succeeds: show active link in header + Settings current card. "This sim cluster is now associated with dashboard at <url> (launched for you)".
  - If not (no aspire cli?): clear note "No active Aspire dashboard (run `aspire dashboard run` separately or use full `aspire run` the AppHost for integrated resources + real per-cluster dashboards). Current cluster: local sim."
  - Always prefer real from WorldConnectionInfo (for started worlds) or entry param (from launcher paths).
  - "Current cluster" concept: enhance ClientState with ActiveWorld (last started or from GetCurrent), its DashboardUrl takes precedence for display.
- **No trash / appropriate**: Every widget purposeful. Use Rescue around dynamic parts. Consistent naming in code (e.g. not "w" for list - use settingsWidgets). Keyboard friendly (existing Tab/Enter). Responsive notifs (take last N, themed).
- **Flutter alignment**: keep env wiring; surfaces can include dashboard info if needed (via UiNeuron emitting Markdown with link for current cluster).

**World Start Buttons Fix (make "completely work")**:
- Improve DoStartWorld: add "inProgress" state per worldId (show spinner text or disabled button during launch).
- Better notifs + live: on start, emit a temp "WorldLaunchStarted" (or use existing), on grain side ensure real info returned (our prior grain/launcher improvements help; if still placeholder, fall back to computed ports + note "launched, use refresh or peer gw").
- In grain StartWorld: the launchResult handling is good; ensure working dir is project root, inherit some env (gemma endpoint), shorter timeout or better error surfacing via notification synapse.
- After start: the emitted WorldConnectionInfo (with real gw + DashboardUrl if child AppHost) updates the list live. User can then "set as peer" or the TUI could auto-switch context (future).
- Test: in sim (start.cs) starts "sim world" (launcher may return marker but info updates); in full aspire, real child with real dash.
- Cover with Simulation: the Reqnroll tests already hit StartWorld paths via feature; add explicit in DistributionSimulationBindings if needed.

**Implementation Order (post-plan approval, in feature-dev style if subagents)**:
1. Context7 lookups for: Hex1bAppOptions + Theme + built-in themes + widget styling (before any hex1b edit); Aspire CLI dashboard run + otel commands + MCP if available (for verify); Orleans grain + launcher for StartWorld success path.
2. Write/enhance this plan (done).
3. Add theme to Hex1bApp creation (small first change).
4. Full rewrite of BuildSettingsTab (and header/footer) as the "Cluster" real-app view with cards, dashboard actions, working buttons. Extend ClientState with launch progress, active cluster helpers (self-explanatory names).
5. Polish other tabs lightly for consistency (theming + minor layout).
6. Add dashboard launch helper (in ClientState or new thin launcher util; uses Process + parse, best effort, sets url, works from start.cs).
7. Fix/enhance DoStartWorld + grain side if needed for reliable success + real info (no placeholders).
8. Update start.cs/Program.cs calls if needed for new state (e.g. pass more cluster info).
9. Minor flutter if dashboard display needs surface.
10. Verify loop: build, aspire (AppHost for real dash), dotnet run start.cs (check TUI dashboard + buttons), high-sev tests, aspire cli (ps/logs/otel), mcp if avail, telemetry confirm (OTel in dashboard), metrics/logs show activity.

**Verification Plan (mandatory, use in terminal + mcp)**:
- Pre: `cd final; aspire --version; cd src/DigitalBrain.AppHost; aspire ps || true; aspire logs --help`.
- Post changes (after each significant): `dotnet build ...` (relative), `dotnet test ... --filter DistributionDynamicHandlers` (high sev, green).
- Full: `cd final/src/DigitalBrain.AppHost; aspire run --non-interactive --detach` (or background), note dashboard url from output.
- Then separate: `dotnet run start.cs` (or with --world), observe TUI: dashboard link present/active (opens real), click Start buttons -> success notif + list updates with real gw/dash for new cluster, no errors.
- Verify via aspire: `aspire ps` (sees kernels + flutter + dashboard), `aspire logs [resource]`, `aspire otel logs --dashboard-url <the one>`, metrics/traces visible.
- Use aspire MCP (search_tool "aspire__...", use_tool for dashboard/resources/telemetry queries).
- `monitor` tool on aspire logs or app for events.
- Check OTel: the app (kernel + TUI? + flutter) exports to dashboard (configured in start/AppHost).
- End: `aspire stop` or kill, tests re-run, no trash in UI, all self-explanatory.
- If buttons still flaky: surface exact ex via notif + fix (e.g. ensure launcher cwd).

**Risks / Trade-offs in Plan**:
- Standalone dashboard launch from TUI may have cli not in PATH or port issues -> graceful fallback to note + "manual: aspire dashboard run".
- Full remake is larger change -> use small steps, test after each (aspire + start.cs + unit sim tests).
- Hex1b theme may require specific version (props has 0.164.1; verify latest via search but no local cache).
- World starts in sim context remain "best effort" (per existing launcher comments); full power in aspire run.
- Aligns with "cover with simulation": sim path gets simulated-but-actionable dashboard + world starts update UI.

**Success Criteria**:
- `dotnet run start.cs` -> TUI Settings shows real or launched dashboard url for the (sim) current cluster; buttons launch and list reflects (with dash if possible).
- Full aspire path: real per-cluster dashboards in list + header.
- UI looks "real app" (themed, cards, header dashboard, clean).
- All verified with aspire cli + (attempt) mcp + logs/telemetry/metrics (OTel flowing, resources visible).
- Tests green, builds clean, code review passed, no C:\Users, Context7 done, latest pkgs, neuron/synapse, no trash.

This plan is the "from scratch" blueprint. Implement only after exit + (implied) approval. All exploration used relative final/ paths + web/official (hex1b, aspire).

Next (after this): exit_plan_mode, then implement per plan + full verify loop.