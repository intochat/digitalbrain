# CLAUDE.md — DigitalBrain / NeuroOS Way of Working

This is the single source of truth for how to work in this repo. Keep it short, actionable, and ruthless.

## The 5 Steps (Elon's "Algorithm")

Follow **in order** on every task, change, or refactor:

**Prompt template**: "Apply Elon's 5 steps in order: 1. Make reqs less dumb (question, trace to person). 2. Delete first (target >10% reduction). 3. Simplify. 4. Accelerate. 5. Automate last."

1. **Make your requirements less dumb**
   - Question every requirement. Trace to a specific person.
   - Challenge assumptions. Your requirements (even from smart people or management) are probably dumb in parts.
   - Example: "Do we really need full AppHost for every MCP debug? Delete that req."

2. **Delete the part or process (try very hard)**
   - Target >10% net reduction.
   - If you're not adding things back ~10% of the time, you didn't delete enough.
   - Ruthlessly cut "just in case", hedges, unnecessary steps/components.
   - Example: Delete old plans, dead code, full restarts.

3. **Simplify or optimize (what remains)**
   - Only after 1-2. Don't polish something that should have been deleted.
   - Example: Simplify WoW to 5 steps + MCP tools.

4. **Accelerate cycle time**
   - Speed up feedback loops — only after prior steps.
   - Example: Use MCP resource restart + bg test poll instead of full run.

5. **Automate**
   - Last. Never automate a bad (undeleted, un-simplified) process.
   - Example: Add script only after deleting waste.

Key: Order matters. Jumping to optimize/automate locks in waste.

## Pre-Change Ritual (Non-Negotiable)

Before every edit, brainstorm, or code change:
1. Use Context7 to query docs for ALL package/framework APIs involved (Orleans, Aspire, MCP, .NET, etc.). No exceptions.
2. Use the `codegraph` MCP server (from .mcp.json) to explore architecture, symbols, call paths, and relationships. Run `codegraph init` (or rely on auto-init via build) after `git clean -fdx`.
3. Run `aspire doctor` (CLI or `aspire__doctor` MCP) + `aspire__list_resources`.
4. If task has >2 steps, `todo_write` first.
5. Follow Elon's 5 steps in order.

Prefix all prompts with the ritual. Rely on CodeGraph for architecture understanding instead of manual file reads or grep.

## Core Principles for Fast Iteration (Local Dev)

- **Use Context7 for EVERY package/framework API** before writing code that touches it (Orleans, Aspire, .NET, Google.Apis, etc.). No exceptions. Never use local NuGet cache or C:\ paths.
- **Use CodeGraph MCP for architecture understanding**: The `codegraph` server (configured in .mcp.json) provides the pre-indexed knowledge graph. Use it (via `codegraph_explore`, status, etc.) for symbols, call graphs, impact analysis, and architecture instead of raw file exploration. Auto-rebuilds on build after cleans.
- **Parallel tools**: Always call Context7 + multiple Aspire MCP tools (`aspire__*`) in parallel at start of responses.
- **Use Aspire MCP tools + `aspire` CLI for speed**:
  - `aspire__doctor`, `aspire__list_resources`, `aspire__list_console_logs`, `aspire__list_traces`, `aspire__list_structured_logs`, `aspire__execute_resource_command` (restart specific kernel/flutter-ui without full stop).
  - Prefer targeted resource commands + logs/traces over `aspire run` every time.
  - `aspire doctor` before/after changes.
- **Pre-build for MCP**: Before MCP tasks or starting digitalbrain, run quick `dotnet build edge/Brain.Mcp/Brain.Mcp.csproj --no-restore`. Use `--no-build` in run args. Delete repeated rebuild waste.
- **Minimal/isolated AppHost**: Use `aspire run` with resource filters or isolated mode when full stack not needed. Inject live `aspire__list_resources` + doctor output into context at start (dynamic state over static docs).
- **Tests**: During TDD, run the smallest owning test project with `dotnet test <project> --logger "console;verbosity=minimal"`. **Never use --filter**. Before every checkpoint or completion claim, run the exact root command `dotnet test --logger "console;verbosity=minimal"`.
  - Launch the full root suite in the background and poll immediately. Focused project tests may run in the foreground.
- **Targeted restarts (use Aspire MCP)**: After changes to Mcp/Kernel/INO, use `aspire__execute_resource_command` "restart" on only the affected resource (e.g. "mcp" or specific kernel). Poll with `aspire__list_console_logs` / `list_traces`. Delete full `aspire run` default.
- **After every change** (small slice):
  1. Build (root or targeted).
  2. Run the owning test project; run the exact root suite once the slice integrates.
  3. `aspire doctor` (MCP or CLI).
  4. Relevant MCP inspection (resources, logs, traces).
- **Cycle metrics + retro**: Log start/end time for iterations (use terminal Measure-Command or timestamps). At end, quick retro: "which of 5 steps skipped?". Use Aspire MCP traces for "time to green". Apply 5 steps to reduce time.
- **Delete trash aggressively** (docs, dead code, plans). 99% of historical plans/specs are noise — kill them. Keep only living README.md + this CLAUDE.md.
- **Relative paths only**. Never reference anything under C:\Users\.
- COMMENTS ARE FORBIDDEN.
- No tracked C#, Dart, Proto, PowerShell, shell, XML, MSBuild, YAML, or JSON-with-comments file may contain line comments, block comments, documentation comments, commented-out code, generated comments, or explanatory annotations.
- Markdown prose is documentation, not a source-code comment. Only `README.md`, `CLAUDE.md`, and this temporary execution plan may remain while the plan is active.
- Generated source must either be untracked and produced during build or be sanitized before it is tracked. Generated code is not exempt from the zero-comment rule.
- Replace useful comments with names, types, tests, validation, or smaller functions. Delete stale, narrative, redundant, and commented-out code.
- **Latest deliberate versions** via central `Directory.Packages.props`.
- **Self-evolution is the product**: The only path for user-visible mutations (packs, automations, new neurons, Ino creations) is human-approved proposals through the journaled rail. Ino + Foundry + Marketplace feed the same approved rail. Durable, replayable, rollback-capable.
- **Self-evolution meta for WoW**: Use Ino/rail to propose improvements to CLAUDE.md, .mcp.json, or this WoW (e.g. "propose new optimization"). Stage via proposal, get approved, apply. The process improves itself.

## Retained Execution Path (North Star)

Keep one path: `Client -> Edge/Auth -> INO operation -> deterministic function or bounded model workflow -> effect gate -> connector adapter`. Commands and queries use typed grain interfaces. Orleans streams are reserved for progress, fan-out, and observability. The generic Neuron/Synapse runtime, legacy gateway, second auth system, Foundry execution loop, pack runtime, and duplicate UI rail are removed after their remaining behavior is either migrated or explicitly discarded.

## Local Dev Speed Hacks (Brainstormed Improvements)

To accelerate iteration cycles (using Context7 + Aspire MCP/CLI + 5 steps):

- **CodeGraph for architecture**: Use the `codegraph` MCP (not manual reads/grep) for fast queries on structure, call paths, and blast radius. Prefer `codegraph_explore` over file crawling.
- **MCP-first inspection**: Before any `aspire run` or manual debug, use `aspire__list_resources`, execute "restart" on specific resource, pull logs/traces. This replaces slow full restarts and log tailing.
- To start the MCP edge on its fixed port: `dotnet run --project edge/Brain.Mcp/Brain.Mcp.csproj --no-build` (http on :5310 via its launch profile; connect via url in .mcp.json). Start the silo (`hosts/Brain.Kernel.Host`) first.
- **Context7 + parallel tools**: Lookup APIs in parallel while editing. Never context-switch to docs/search.
- **Strict delete-first + clean docs**: This CLAUDE.md + README are the *only* living docs. All plan/*.md, archive, superpowers, old specs = trash. Deleting them reduces reading waste and decision fatigue (Step 2 of algorithm).
- **Resource-level control**: Stop/restart only kernels or flutter via MCP before builds to avoid DLL locks from live replicas. Then test.
- **Minimal full runs**: `dotnet test` (min verbosity, root) + `aspire doctor` + targeted MCP. Use background for long tests. Parallel calls.
- **Follow 5 steps on every edit**: Question why the change, delete related trash first, simplify the diff, make feedback faster (e.g. more MCP), automate only if the loop is clean.
- **Self-evolution meta**: Use the rail itself to evolve the WoW (e.g. new automation for "stop kernels + test + doctor").
- **Avoid early optimize/automate**: Don't add watch scripts or fancy until the base loop (question/delete/simplify) is clean.
- Result: Smaller context, faster feedback, less "running the app" tax.

## Rules (Non-Negotiable)

- Context7 before any API-touching code.
- **CodeGraph MCP first for architecture**: Use `codegraph` server (init after clean via build target or `codegraph init`) for all codebase structure, symbols, and impact questions. Do not manually explore files for architecture.
- Aspire MCP/CLI + doctor in every cycle.
- Project-level TDD + exact root `dotnet test` integration gate. No filters.
- Delete > add. Clean docs = fast brains.
- Relative paths. Meaningful names.
- Self-evolution rail is sacred for mutations.

Update this file when the loop improves (via the rail, of course).

---

Follow the 5 steps. Use the tools. Delete the trash. Ship self-evolution.
