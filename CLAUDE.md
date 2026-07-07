# CLAUDE.md — DigitalBrain / NeuroOS Way of Working

This is the single source of truth for how to work in this repo. Keep it short, actionable, and ruthless.

## The 5 Steps (Elon's "Algorithm")

Follow **in order** on every task, change, or refactor:

1. **Make your requirements less dumb**
   - Question every requirement. Trace to a specific person.
   - Challenge assumptions. Your requirements (even from smart people or management) are probably dumb in parts.

2. **Delete the part or process (try very hard)**
   - Target >10% net reduction.
   - If you're not adding things back ~10% of the time, you didn't delete enough.
   - Ruthlessly cut "just in case", hedges, unnecessary steps/components.

3. **Simplify or optimize (what remains)**
   - Only after 1-2. Don't polish something that should have been deleted.

4. **Accelerate cycle time**
   - Speed up feedback loops — only after prior steps.

5. **Automate**
   - Last. Never automate a bad (undeleted, un-simplified) process.

Key: Order matters. Jumping to optimize/automate locks in waste.

## Core Principles for Fast Iteration (Local Dev)

- **Use Context7 for EVERY package/framework API** before writing code that touches it (Orleans, Aspire, .NET, Google.Apis, etc.). No exceptions. Never use local NuGet cache or C:\ paths.
- **Use Aspire MCP tools + `aspire` CLI for speed**:
  - `aspire__doctor`, `aspire__list_resources`, `aspire__list_console_logs`, `aspire__list_traces`, `aspire__list_structured_logs`, `aspire__execute_resource_command` (restart specific kernel/flutter-ui without full stop).
  - Prefer targeted resource commands + logs/traces over `aspire run` every time.
  - `aspire doctor` before/after changes.
- **Tests**: ONLY run `dotnet test --logger "console;verbosity=minimal"` from the repo root. **Never use --filter**. Run full from root for high signal.
- **After every change** (small slice):
  1. Build (root or targeted).
  2. `dotnet test --logger "console;verbosity=minimal"`.
  3. `aspire doctor` (MCP or CLI).
  4. Relevant MCP inspection (resources, logs, traces).
- **Delete trash aggressively** (docs, dead code, plans). 99% of historical plans/specs are noise — kill them. Keep only living README.md + this CLAUDE.md.
- **Relative paths only**. Never reference anything under C:\Users\.
- **Self-explanatory names**. No vacuous `/// <summary>`. Small inline `//` comments only in exceptional cases.
- **Latest deliberate versions** via central `Directory.Packages.props`.
- **Self-evolution is the product**: The only path for user-visible mutations (packs, automations, new neurons, Ino creations) is human-approved proposals through the journaled rail. Ino + Foundry + Marketplace feed the same approved rail. Durable, replayable, rollback-capable.

## Self-Evolving System Vision (North Star)

NeuroOS makes safe, explicit, journaled, human-approved self-evolution the *only* path.

- Every mutation stages a `SelfEvolutionProposal`.
- Only after `SelfEvolutionDecision.Approved` does an apply handler run the effect.
- Ino is the orchestrator that proposes; actual creation/apply happens in the rail.
- Everything is a Neuron (grain) or Synapse (message). Packs are signed C# embodied at runtime.
- Durable journals + replay for the evolution stream itself.
- Fast inner loop + MCP for inspection; full `aspire run` only for end-to-end when necessary.

See the self-evolution rail in Core + Kernel/SelfEvolution + apply handlers. Bypasses are explicit/trusted/config-gated only (never default for user/MCP paths).

## Local Dev Speed Hacks (Brainstormed Improvements)

To accelerate iteration cycles (using Context7 + Aspire MCP/CLI + 5 steps):

- **MCP-first inspection**: Before any `aspire run` or manual debug, use `aspire__list_resources`, execute "restart" on specific resource, pull logs/traces. This replaces slow full restarts and log tailing.
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
- Aspire MCP/CLI + doctor in every cycle.
- `dotnet test` root + min verbosity only. No filters.
- Delete > add. Clean docs = fast brains.
- Relative paths. Meaningful names.
- Self-evolution rail is sacred for mutations.

Update this file when the loop improves (via the rail, of course).

---

Follow the 5 steps. Use the tools. Delete the trash. Ship self-evolution.