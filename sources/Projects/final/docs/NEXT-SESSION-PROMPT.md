You are a senior C# developer and Microsoft Orleans expert working on **DigitalBrain** at `E:\Projects\final` (.NET 11 preview 4, Orleans 10.1.1-preview.1 + Journaling alpha, Aspire 13.4.x, Hex1b TUI 0.164.1, Reqnroll BDD, xunit v3 + Microsoft.Testing.Platform). Use context7, codegraph, and microsoft-learn MCPs; prefer a Claude Code session (Claude-app sessions cannot execute commands; also `Filesystem:search_files`/`directory_tree` crash on this repo — use `list_directory` + `read_text_file`). Code rules: no comments (self-documenting only), production-ready, proper Orleans usage (no `GrainFactory` in constructors, `[GenerateSerializer]` discipline, never assign collection expressions `[...]` to `IReadOnlyList<T>` properties on serialized types — `Column`/`Row` children are concrete `UiWidget[]` for exactly this reason). Process is The 5 Steps (Elon's Algorithm) — this session is a FULL pass starting at Step 1; do the steps in order, do not optimize or build anything whose requirement hasn't survived Steps 1–2.

## Repo state

Phase 0 is DONE and committed: HEAD = `master @ e0ba491` ("Phase 0 green (Step 4-5): collector fallback + probe kept, ..."). Suite 18/19 green (1 known ino E2E skip). `run-ci.ps1` at root is the CI loop. FIRST COMMAND of the session: `git status` (verify clean tree) then `.\run-ci.ps1` (verify green baseline) — never start a redesign from an unverified baseline. Also verify `.codegraph/` now exists (`codegraph init` previously produced nothing); re-run it if absent.

## Mission

Rethink the console client UI **from scratch** around four real user scenarios, matching our `UiWidget` vocabulary to what hex1b 0.164.1 actually offers — instead of the current everything-in-one-Border stack in `TaskManagerClient.cs`. All requirements below: owner = Vlad (stated 2026-06-10). Question each one back to him anyway — that's Step 1.

## Hex1b capability map (researched 2026-06-10 — verify against the installed 0.164.1, the site tracks main)

Docs: https://hex1b.dev/guide/widgets/ (catalog), /guide/layout, /guide/input, /guide/theming, /guide/testing, /guide/composition. Repo: https://github.com/mitchdenny/hex1b — has `samples/`, `docs/`, AGENTS.md, and a `.claude/skills` folder (pull that skill into context before writing hex1b code), plus a hex1b MCP server (https://hex1b.dev/guide/mcp-server).

- Layout: VStack/HStack, **Grid** (row/col spanning), Border (titled), Align, **Scroll**, **Splitter** (resizable), **DragBarPanel**, Float, WrapPanel, **Responsive** (breakpoints), **Windows** (floating, draggable).
- Interactive: Button, Checkbox, **SplitButton**, **TabPanel**, **Accordion**, **TextBox (single AND multi-line)**, List (selection + activation), **Tree** (expand/collapse, multi-select), **Table** (sorting, selection, virtualization), Picker (dropdown), Slider, ToggleSwitch, **Navigator** (stack-based pages), Drag & Drop.
- Display: **Markdown** (headings, code blocks, tables, links), Charts, FigletText, Icon, InfoBar, **Notifications** (floating, with actions + drawer), Text (rich styling), **Progress**, **Spinner**, Hyperlink (OSC 8), QrCode, KgpImage.
- Utility: **Rescue** (error boundaries), StatePanel (identity-anchored state/animations), ThemePanel, **Terminal** (embed child terminal sessions), Surface, EffectPanel.
- Testing: **headless execution, input sequence builders, screen assertions** — TUI tests like web tests. This is the Step-5 lever: the manual TUI smoke from Phase 0 becomes an automated scenario test.

## The four user scenarios (the requirements — Step 1 raw material, owner: Vlad)

1. **Ask.** A chat tab: talk to ino, history seeded from journals + live AgentRequest/AgentResponse (exists today, keep working).
2. **Creator mode.** A tab with an ino-like writing panel (multi-line TextBox editing `.ino` content) + **Pack** and **Publish** buttons next to it — the GUI path for what `/pack` + `/publish` do today.
3. **Marketplace tab.** See listings (the `MarketplaceNeuron` `"marketplace"` UiSurface and/or a Table), install with a button tap. The marketplace must come **pre-seeded with the Awesome SE team bundle** (`awesome-se-team` from `DigitalBrain.Awesome`) — packed + published at kernel boot via the launcher's core-experiences path, so the tab is never empty and scenario 4 is one tap away.
4. **Analyze a C# project.** After installing the SE bundle, asking ino (or a creator/market action) to "analyze the C# project at <path>" actually does it: kernel-side reads the project's `.cs` files (the kernel runs on the user's machine — local paths are valid there, NOT on the client), feeds them through `ReviewRequest` → `SoftwareEngineeringTeamNeuron` → `ReviewResult` rendered as a surface (Markdown widget is the natural fit for the summary). Today's `HandleAsync(ReviewRequest)` is a TODO-counting heuristic on pasted content — it must become real enough to be honest: path overload or a path→content resolver, file count/size caps, and optionally the LLM (`LlmAgentNeuron` tool like `review_project(path)`) when a real model is wired.

## Step 1 — question these requirements explicitly (each answer traceable to Vlad)

- Do tabs/editor belong in the **UiWidget union** (neuron-emitted, serialized, cross-client incl. Flutter) or in the **client shell** (hex1b-only chrome)? Default position: shell = client (TabPanel, editor, InfoBar, Notifications); union = only what neurons must emit. Extending the union is the expensive move — every case needs Orleans serialization, WidgetTree.Render, SurfaceRenderer, and Flutter eventually.
- Which union extensions do the four scenarios actually force? Candidates: `Markdown(string)` for ReviewResult/agent responses; `Progress`? `Input`? Reject anything no scenario emits ("just in case" = delete).
- Does `CommandRouter` survive when Pack/Publish/Market are buttons and tabs? Slash commands are also the test seam (`CommandRouterTests`) and power users — keep, shrink, or delete: decide, don't default.
- Is "analyze a project" chat-driven (AgentRequest + LLM tool), synapse-driven (`ReviewRequest(path)` overload), or both? Pick the dumbest version that is real, with the no-LLM fallback (heuristic over real files) still honest.
- Does the SE-team seed belong in the launcher's core experiences, in `start.cs`, or as a marketplace bootstrap on `MarketplaceNeuron` activation? One owner, no duplicates (remember the start.cs duplicate-registration deletion from Phase 0).

## Step 2 — deletion candidates in the current client (read `TaskManagerClient.cs` first)

- The string-based `MarketLines()` panel + the `/market` string-listing branch: `MarketplaceNeuron` already emits the `"marketplace"` UiSurface — two parallel renderings of the same data, one must die.
- `LastMsg` single-string status → hex1b Notifications (or keep the string and reject Notifications — but not both).
- The all-in-one Border layout itself; `Row` widget if the consolidated marketplace surface no longer needs it (check usages — Phase 0 already deleted its marketplace usage).
- Any CommandRouter command made redundant by Step 1's decision.
If you're not adding ~10% back later, you didn't delete enough.

## Step 3 — only after 1–2: shell design

Likely shape (validate, don't assume): TabPanel(Ask | Creator | Marketplace) inside the app frame, Notifications for surface-arrival/pack/publish/install feedback, Rescue around `SurfaceRenderer` output (a bad neuron surface must not crash the client), Splitter where a tab needs two panes (creator: editor | preview via WidgetTree.Render or Markdown). `SurfaceRenderer` stays the one generic UiWidget→hex1b walker; surfaces route to tabs by SurfaceId convention or emitter — decide and write it down.

## Step 4 — cycle time

Keep `.\run-ci.ps1` green and fast; inner loop <60s. Use hex1b headless testing for the new shell: at minimum one scenario test per tab driven by input sequences + screen assertions (this replaces the Phase-0 manual smoke). One real manual run at the end anyway — confirm LIVE stream delivery to the external client (the Phase-0 collector fallback proved grain-side delivery; the client-side leg was only smoke-checked).

## Step 5 — automate + commit

Extend run-ci.ps1 (or the test suite it runs) with the headless TUI scenario tests + a headless pack→publish→install→analyze assertion via `WidgetTree.Render`/screen assertions. ONE commit; message lists deletions; update `docs/DELETED.md`, `docs/USER-FLOWS.md` (flows 2, 7, 8, 13 get the tab UX; add the analyze flow), `docs/ROADMAP.md` if phases shift.

## Archaeology & Cross-Version References (added during root analysis pass)
See `docs/PROGRESS-ARCHAEOLOGY.md` (full enumeration of every folder's initial idea, clean/product features, what was mined vs cut, with literal file references) and `docs/CONTINUATION-PROMPTS.md` (distilled operating instructions + high-sev gates + lessons for future agents). These consolidate the v1/v2/ino/IAW history so later sessions do not re-learn the same cuts or re-discover the same good patterns (capsule co-location, Simulation=neuron substrate, E2E fixtures, brain-owned orchestration, creator loop, interface wiring for N+1 provability).

## Landmines (do not relearn)

- Orleans serialization: concrete arrays on serialized records; the `union UiWidget` decl carries `[GenerateSerializer]`; test every new union case through a stream round-trip (the collector grain + probe pattern in `DistributionSimulationBindings.cs` is the harness).
- Silo-wide shared journals (Phase 1 item, ROADMAP): `GetRecentHistoryAsync` ordering is flaky by design right now — don't write new assertions that depend on per-grain journal isolation, and don't fix the registration this session.
- `WidgetTree.Render` output format is load-bearing in test assertions — extend it for new union cases before writing asserts.
- Hex1b requires .NET 10+; repo is on .NET 11 preview — fine, but check 0.164.1's actual API against hex1b.dev (site tracks main; pin-verify TabPanel/Markdown/Notifications exist in 0.164.1, bump the package if not, note it in Directory.Packages.props).
- Do NOT start Phase 1 (Redis/journal durability) or Phase 2 (Ed25519/quarantine).

## Definition of done

`git status` clean at start; baseline green; the four scenarios work end-to-end in the TUI (analyze = real files from a real path on the kernel machine); marketplace pre-seeded with `awesome-se-team`; union extended only by scenario-forced cases, serialization-round-trip-tested; headless TUI scenario tests in CI; docs updated; one commit.
