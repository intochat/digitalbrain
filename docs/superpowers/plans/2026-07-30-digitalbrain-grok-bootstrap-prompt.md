# New Codex Session Bootstrap Prompt

Copy the text below into a new Codex session opened at `E:\intochat\digitalbrain`.

```text
Implement the approved DigitalBrain neuron/synapse/behavior architecture now.

The approved design is:
E:\intochat\digitalbrain\docs\superpowers\specs\2026-07-30-neurons-synapses-behaviors-design.md

The master execution plan is:
E:\intochat\digitalbrain\docs\superpowers\plans\2026-07-30-digitalbrain-grok-orchestrated-implementation.md

The eight executable slice plans are in the same plans directory:
- 2026-07-30-digitalbrain-slice-1-synapse-catalog.md
- 2026-07-30-digitalbrain-slice-2-behavior-contracts.md
- 2026-07-30-digitalbrain-slice-3-tasks-behavior-runtime.md
- 2026-07-30-digitalbrain-slice-4-vector-memory.md
- 2026-07-30-digitalbrain-slice-5-provider-neurons.md
- 2026-07-30-digitalbrain-slice-6-automatic-ai-routing.md
- 2026-07-30-digitalbrain-slice-7-flutter-behavior-studio.md
- 2026-07-30-digitalbrain-slice-8-live-hardening.md

Operating model is mandatory:

1. You are the Codex conductor. Do not write production or test code yourself.
2. All implementation, refactoring, deletion, integration conflict resolution, and implementation commits must be done through Grok CLI sessions.
3. Run multiple Grok sessions in parallel only in separate Grok worktrees and only for lanes declared independent by the master plan.
4. Use one writer per worktree. Let Grok use its own subagents for CodeGraph exploration, tests, and reviews, but the top-level Grok writer owns one coherent diff.
5. Your job is to establish git ground, launch/poll Grok, inspect every diff, reject drift, dispatch corrections, integrate in dependency order, independently run gates, and verify live behavior.
6. Never trust a Grok statement that something passes. Re-run the relevant commands and inspect the evidence yourself.

Start immediately with the master plan preflight:

- Read Claude.md, the approved spec, master plan, and all slice plans.
- Record git status, HEAD, log, and worktrees; preserve all unrelated user changes.
- Run grok --version, codegraph --version, dotnet --info, grok mcp doctor.
- Run codegraph sync . and require an up-to-date index.
- Run the baseline Release build and test gates.
- Use Aspire MCP to list/select the running AppHost, refresh tools, list resource health, and inspect telemetry. Do not use Computer for Aspire.
- Aspire MCP cannot start an AppHost process. Prefer the already running AppHost. If none is running, explicitly report the limitation and use only the single background `aspire start --non-interactive` bootstrap exception authorized by the master plan; then return to Aspire MCP for all lifecycle/resource/health/log/trace work.
- Use Computer only for the Flutter app.
- Use DigitalBrain MCP for live product interaction, exact active-neuron lookup, journals, transcript, and behavior revision rail.

Then launch four read-only Grok explorers in parallel:

- db-explore-contracts
- db-explore-behaviors
- db-explore-memory-routing
- db-explore-product

Each explorer must use CodeGraph, identify exact files/tests/callers/trash/collisions, make no edits, and return the standard handoff. Synthesize their reports against the approved design. If there is no genuine blocker, proceed into Wave 1 without asking me to restate or re-approve the design.

For Grok writers:

- Use `grok --worktree=<lane> --worktree-ref=<recorded-base> --prompt-file=<lane-prompt> --check --permission-mode acceptEdits --no-memory --output-format json`.
- Launch background processes with separate stdout/stderr files and `Start-Process -WindowStyle Hidden`.
- Never use `--always-approve`, `bypassPermissions`, unrestricted sandboxing, destructive git, or shared writer worktrees.
- Require TDD: focused failing test, captured RED, minimal implementation, GREEN, CodeGraph impact check, proven trash deletion, targeted build/tests, atomic commit, standard handoff.
- Require Context7 for library APIs and Aspire MCP docs for AppHost/integration APIs.
- Existing Claude.md is authoritative for repository conventions, but the approved spec and plans are user-requested artifacts and must not be deleted under the general no-docs convention.

After each writer:

- Run a separate read-only Grok reviewer.
- Send accepted findings back to the original writer.
- Run a separate read-only verifier.
- Inspect base/head diff and scope yourself.
- Use a dedicated Grok integration worktree/session to merge accepted commits in the plan's order.
- Independently run the root gates on the integrated commit.

Live verification is mandatory, not optional:

- Aspire MCP: list_resources, execute_resource_command, list_structured_logs, list_traces, list_trace_structured_logs.
- DigitalBrain MCP: send_chat_message, list_active_neurons, read_neuron_journal, read_chat_transcript, and the behavior read/propose/test/approve tools.
- For each live scenario, identify the exact neuron instance, journal sequence/correlation, owning Task, and Aspire trace.
- For missing Google/Salesforce auth, verify a minimal clickable user action, pause, and continuation of the same Task. Never fake or expose owner credentials.
- Run the explicit product tests and use Computer to inspect the six Flutter Behavior Studio views before completion.

Architectural invariants:

- Pure directed synapses; marker neuron interfaces; no `ReadRecentMessages`-style public methods.
- `brain.Get<IGmail>().SendAsync(new GmailRequest(...))` is common synapse plumbing, not a Gmail wrapper.
- TasksModule owns durability; no KernelTask or WorkId.
- MemoryModule exposes provider-independent IVectorMemory; Qdrant remains internal; graph memory remains separate.
- Exact generated catalog is authoritative; vector search only retrieves candidates.
- Google/Salesforce own configuration, OAuth, multiple connections, MCP tools, and model planning; no shared account subsystem.
- Behaviors are Behavior.cs + Behavior.feature, scenario-first/TDD, one logical input union, explicit bindings, isolated worker, deterministic replay, proper CancellationToken propagation.
- Flutter defaults to intent/evidence, offers Stop and assistant-led change, and exposes real source/tests without becoming a full IDE.

Continue autonomously through the plans unless a real approval/credential/external-state blocker is reached. Keep me updated at meaningful wave boundaries and whenever a Grok session finds a design contradiction or a live user action is required.
```
