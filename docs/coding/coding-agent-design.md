# Coding agent design (proposal, 2026-09-13)

Status: **ratified 2026-09-13 with every default in section 5** (the owner asked for the implementation
prompt); nothing is implemented yet. The evidence behind every claim is in `coding-agent-research.md`
(cited as R1.2, R6.3 and so on). Phase 0 starts from `plans/2026-09-13-coding-phase0-workspace.md`.

## 1. What this is for

The owner wants a C# coding agent that works on DigitalBrain the way a developer works in an IDE, and then
goes further than any general coding agent can:

1. **Roslyn inside.** The agent answers "where is this used", "who calls this", "what breaks if I change
   this" from the compiler's semantic model, in milliseconds, without reading whole files into a prompt.
2. **A solution map.** A pretty, navigable picture of the solution: projects, references, and on demand the
   neighborhood of a symbol, with the blast radius of a proposed change highlighted.
3. **Refactoring by a swarm.** For a change, the agent finds the files and projects involved, gives each
   file its own small agent, and lets them discuss the change in a group chat where "not my responsibility" is
   a valid answer. A chair consolidates a plan, the owner signs it off, the agents edit, the whole thing is
   compiled and tested before a line reaches disk.
4. **Live rebuild.** The kernel the agent runs in is the kernel it changes. Two slots of the kernel run behind
   a gateway so one is always live while the other rebuilds; a swap is a health-checked promotion with
   rollback.
5. **It learns the owner.** Conventions the owner states or corrects ("no summary comments", "names are the
   documentation") are remembered and injected into every future task.
6. **Model choice per role.** A cheap fast model for the many file agents, a strong model for the chair, per
   configuration, drawing on the model roster that already exists.

Definition of done for the whole programme:

- The chat "where is `ITimer` used" answers from Roslyn with file, line and project in under a second on
  the loaded solution.
- The chat "map the solution" opens a graph of every project and reference in the workspace.
- The chat "rename `TimerNeuron.Alarm` to `AlarmFor` and rebuild" ends with the rename applied across the
  solution, the suite green, a git commit, the standby slot promoted, and the old slot stopped, with the
  chat never losing its connection for longer than a reconnect.
- The chat "add a `Pause` command to timers" convenes a swarm, produces a plan the owner approves in a card,
  lands the change through the same verify-and-swap path, and the report names every file and why it changed.
- The owner says "never add `/// <summary>` comments"; the next task's agents carry that instruction.
- Every capability above is a typed neuron method callable through `/mcp` (`describe`, `call`), so Claude
  Code or Codex can use the same instrument.

## 2. Principles that decide the details

- **Snapshot transactions, never file writes.** Every edit is a transformation of the current Roslyn
  `Solution` snapshot; the snapshot is diagnosed; only a clean snapshot is applied through
  `TryApplyChanges` (R1.2). IAW's defects (R2.3, items 3 and 4) came from breaking this rule.
- **Degrade, don't fail.** While the workspace loads, answers come from durable caches or say "opening, 12
  of 35 projects"; a tool never throws at the model, it returns advice (R2.1, R3.2).
- **Neurons first.** Long-lived things are neurons with typed methods (`workspace`, `changeset`, `slot`,
  `refactoring`); the swarm's agents and chats are the existing `agent` and `chat` neurons; Roslyn itself is a
  singleton service behind the `workspace` neuron, the way `TableService` sits behind tables (R6.2, R6.3).
- **No waiting inside a reaction.** A neuron never awaits another neuron's answer inside its own turn:
  `Announce` only buffers until the reaction saves, and reactions are serialized, so a synchronous wait for
  a peer's reply can never be answered (review finding 9). Every "ask and wait" is a saved phase
  ("waiting for X") completed by a later correlated reaction; every tool returns a receipt at once.
- **One result envelope.** Every list result is `{items, totalCount, truncated}` with a caller-set limit
  (R1.4). Nothing unbounded reaches a prompt.
- **Symbols have ids.** A symbol is addressed by its documentation-comment id (`T:DigitalBrain.Time.ITimer`),
  never by a substring (R1.2, R2.3 item 5).
- **The owner signs the plan and the landing.** A swarm cannot edit before the plan card is approved and
  cannot promote a slot before the build-and-test card is approved (decision point D12).
- **Repo vocabulary and taste.** Neurons fire signals along synapses; module folders, `[Alias]` names,
  `Accepted<T>` receipts, `<module>:` commit prefixes; no `/// <summary>` restating a signature; names are
  the documentation.

## 3. Approaches considered

**A. A `Coding` module inside the kernel, with kernel slots (recommended).** Roslyn, the swarm and the
rebuild loop live in a module composed into the same silo the product runs in. Two slots of that silo run
behind a small gateway; the agent lands a change by building the standby slot, promoting it, and continuing
its own conversation from durable state in the new slot. Cost: the module ships in the dev kernel only
(composition decides), and the slot mechanism is new infrastructure. Benefit: one code base, one set of
neurons, every capability reachable through the existing chat, MCP and Flutter paths; the swap is the very
thing the neuron model is built for (journals and snapshots survive the process).

**B. A separate "forge" silo.** The coding agent runs in its own process built from a pinned tree, never
rebuilt with the product. Simpler slot story, but two silos with different module sets, a second port for
the shell, and the forge still needs the slot mechanism to upgrade itself. Rejected as the default; A
subsumes it (a slot can be composed with a different module set later).

**C. External Roslyn MCP server plus Claude Code.** Run roslyn-codelens-mcp (R1.4) next to Claude Code.
Fastest to try and worth doing as a benchmark, but it has no swarm, no rebuild loop, no memory, and nothing
of it lives in the brain. Rejected as the product; kept as the comparison baseline in phase 0.

## 4. Target architecture

### 4.1 The module

`src/Modules/Coding/{Contracts, Coding, Aspire.Hosting}`, namespaces `DigitalBrain.Coding` and
`DigitalBrain.Coding.Aspire.Hosting`, module class `CodingModule`, configuration root `DigitalBrain:Coding`.
Registered in `DigitalBrain.slnx` under `/Modules/Coding/`, referenced by the silo and the test project
(R6.1). Packages: `Microsoft.CodeAnalysis.CSharp.Workspaces` 5.9.0, `Microsoft.CodeAnalysis.Workspaces.MSBuild`
5.9.0, `Microsoft.Build.Locator` 1.11.2 (R1.1); `Directory.Build.props` gains
`DisableMSBuildAssemblyCopyCheck=true` (R1.2).

Neurons (grain type in parentheses; every method obeys the descriptor rules of R6.2):

| Neuron | Phase | Commands | Reads |
|---|---|---|---|
| `ICodeWorkspace` (`workspace`), name = workspace key | 0 | `open`, `reload` | `read`, `find-symbols`, `references`, `diagnostics`, `map` |
| `IChangeSet` (`changeset`), name = change id | 1 | `propose`, `check`, `commit`, `discard` | `read` (edits, diff, diagnostics) |
| `ISlot` (`slot`), name = `a` or `b` | 2 | `build`, `promote`, `retire` | `read` |
| `IRefactoring` (`refactoring`), name = task id | 3 | `start`, `approve`, `revise`, `cancel` | `read` (phase, scope, plan, transcript pointer, report) |

Singleton services in the implementation project: `SolutionWorkspace` (the Roslyn workspace and its
current snapshot), `WorkspaceWarmup` (a hosted service that opens the configured solution at silo start,
R2.1), `CodingNativeTools` (the `code_*` tools), later `ChangeSetEditor`, `DotnetRunner`, `SlotBuilder`,
`ImpactAnalyzer`.

### 4.2 Roslyn workspace service

`SolutionWorkspace` owns one `Workspace` per silo behind an `ISolutionLoader` seam: `MSBuildSolutionLoader`
in the kernel (registers `MSBuildLocator` once, opens the `.slnx`, records every workspace failure through
`RegisterWorkspaceFailedHandler`, reports project-load progress) and an adhoc loader in tests (D9). Its
status is `NotOpened | Opening | Ready | Failed` with project and document counts and a detail line.
Queries run on the immutable `CurrentSolution` and never block the neuron's turn beyond the query itself:

- `FindSymbols(query, limit)`: `SymbolFinder.FindSourceDeclarationsAsync` with a name predicate; hits carry
  the documentation-comment id, kind, display string, project, path and line.
- `References(symbolId, limit)`: resolve the id in each project's compilation, `SymbolFinder.FindReferencesAsync`,
  one hit per location with the trimmed source line.
- `Diagnostics(path or project)`: the document's semantic model diagnostics, or the project compilation's,
  warnings and errors only, counts included.
- `Map()`: `GetProjectDependencyGraph()`, nodes clustered by solution folder, edges from
  `ProjectReferences`; package edges are phase 5.
- Phase 1 adds `Skeleton(path)` (types and member signatures without bodies), `Member(symbolId)` (one body),
  `Callers`, `Implementations`, `Derived`, and the edit primitives.

Ordering and readiness (review findings 4 and 5): `MSBuildLocator.RegisterDefaults()` is the first
statement of the silo's `Program.cs`, before host composition, as IAW does (R2.1); the loader keeps a
guarded registration only as a fallback for test processes. The loader returns the workspace **and** the
list of workspace failures; `Ready` with failures carries them in the status detail, and the gated
self-test requires zero failures on this solution. The `DisableMSBuildAssemblyCopyCheck` property is set
only on the three projects that carry the Roslyn assemblies (module, silo, tests), never globally.

Memory and concurrency (review finding 6): semantic queries run through a bounded gate (two at a time),
every result is capped, and if the silo's working set under load proves unacceptable, D1's sidecar is the
recorded fallback; nothing in the neuron contract changes for that move.

Freshness: phase 1 adds a `FileSystemWatcher` over the solution root that folds `.cs` saves into the snapshot
with `WithDocumentText` and marks `.csproj`/`.props`/`.targets` changes as "reload needed" (R1.4 lessons).

Durable cache: the `workspace` neuron's state keeps the last solution path, the generation, and the last
`SolutionMap`, so `map` answers before Roslyn is ready (R2.1).

### 4.3 The solution map

`code_map` returns `{ kind: "graph", id, name, title, nodes, edges }` exactly as `render_chart` returns
`kind: "chart"`, and the shell opens it from the tool result (R6.3). The `id` is stable per solution
(`map-` plus a hash of the solution path) because the shell's artifact acceptance drops any result without
one (review finding 12), and both opening paths in the shell (`workspace_chat.dart`'s auto-open list and
`workspace_chat_presentation.dart`'s "open in workspace" list) learn the `graph` kind. Nodes are projects
(`kind: "module"`, `cluster` = the module or kernel folder) with the graph's existing
`GraphNodeState`/`GraphEdgeState` shapes. Phase 0 ships the inline snapshot only; a graph *reference*
(the `uichat` card path through `IGraph.Render`) needs a core-client read route for `/ui/graphs/{name}`
and arrives in phase 5. The shell gains the missing `graph` editor case that renders `UiGraph` from the
artifact data (R6.4), verified end to end from a `TOOL_CALL_RESULT` event to the rendered widget.

Pretty comes in phase 5, after the UI phase 1 part contract lands: layered positions computed in C# from
the project DAG (longest-path layer, stable order within a layer), a force layout for symbol neighborhoods
(`graphview` 1.5.1 algorithms feeding the existing painter, R6.6), blast-radius dimming (`GraphNode.dimmed`
already exists), and click-through from a node to `references`.

### 4.4 Live rebuild: slots and a gateway

- **Slots.** The AppHost declares `kernel-a` and `kernel-b` from the same silo project with
  `WithExplicitStart()` on the standby, each with `DigitalBrain__Slot=a|b` and its own output root
  `artifacts/slot-a`, `artifacts/slot-b` built with `-p:ArtifactsPath` (R5.4, D3). Each start mints a fresh
  `Orleans__ClusterId` (`{slot}-{timestamp}`) when the environment leaves it empty, with the stable
  `ServiceId`, so grain state, journals and reminders are shared and membership is fresh (R5.3).
- **Gateway.** A small `DigitalBrain.Gateway` project (YARP `LoadFromMemory`, R5.5) listens on 5080 and
  reassigns every request to the active slot's cluster; `POST /switch/{slot}` flips the active slot; it
  proxies SSE and streamable HTTP unchanged. The shell and every MCP client keep talking to 5080.
- **The active-slot fence** (review findings 1 and 2). Two silos sharing a `ServiceId` share reminder
  rows and grain storage, so a standby that merely starts would already run reminders and activate the same
  neurons. A single `ActiveSlot` lease row (compare-and-swap in the clustering table) names the owner.
  A silo whose slot does not hold the lease starts in standby: its reminder ticks are ignored, its
  reactions are not drained, and its commands refuse with "standby slot" (one check in the kernel's
  existing grain-call filter and reminder handler). Promotion is: flip the lease to the new slot, switch the
  gateway, let the old slot drain in-flight HTTP for a grace period, then stop it. Activations never run in
  two slots at once because only the lease holder reacts.
- **The `slot` neuron** runs `build` (through `SlotBuilder`: `dotnet build DigitalBrain.slnx
  -p:ArtifactsPath=...`, errors parsed into `DiagnosticHit`s), asks Aspire to start the standby resource,
  waits for `/health` and a read-only smoke check (no mutation, the standby is fenced), then promotes.
  Aspire commands go through the Microsoft module's `AspireConnection` with its allowlist widened to
  `execute_resource_command` for `start|stop|restart` (R5.2).
- **The swap is a durable follow-up, never part of a turn** (review finding 3). The turn that decides to
  land ends by saving `Landing`; a later reaction performs the promotion. No model run and no tool call is
  in flight in the old slot when the lease flips, so nothing is repeated in the new slot. The shell's SSE
  client reconnects by run id and journal cursor (phase 2 adds both to the session stream); an interrupted
  stream replays from the cursor.
- **Rollback** is flipping the lease and the gateway back while the old slot still runs, and it is allowed
  only for landings whose changeset touched no `[GenerateSerializer]` state type: a change to persisted
  state shapes is landed as forward-only, with the old slot stopped before any state is written by the new
  one (Orleans versions interfaces, not state, R5.3).
- **The agent survives its own swap** because everything it is doing is neuron state: the `refactoring`
  neuron's phase, the `changeset`, the chat transcript in journals, the agents' sessions. The new slot's
  `refactoring` activation continues from `Landing`.

### 4.5 The swarm

A refactoring is a `refactoring` neuron driving these phases, each a saved state and a card in the chat:

1. **Scope.** `ImpactAnalyzer` turns the request into seed symbols (names mentioned, `find-symbols`), then
   references, callers, implementations and derived types, then the set of documents grouped by project,
   each with a reason ("declares `ITimer`", "calls `Schedule`"). Capped at 24 documents (D5); beyond that,
   agents are per type or per project. The owner sees the scope as the solution map with the blast radius
   highlighted and can add or remove files.
2. **Convene.** For each scoped document an `agent:file/<relative path>` neuron is `Instruct`ed with a role
   prompt, the file's skeleton, the reasons it is in scope, the owner's conventions (4.6), the file-agent
   model (4.7), and tools scoped to its document (`code_member`, `code_references` from this file,
   `code_propose_edit`). A `chat:refactoring/<id>` neuron is `Instruct`ed with the file agents and the chair
   `agent:chair/<id>` (strong model) as participants, manager `chair` (D4). The owner is **not** a
   participant (a `uichat` cannot answer a `Turn`, review finding 10): the owner watches the transcript as
   a card and acts through the sign-off commands. Adding files later is an explicit `roster` transition on
   the active run (the AI module's `RunPolicy.Participants` is updated, not only the synapses).
3. **Discuss.** The chair opens with the request. Each file agent answers a structured stance:
   `{ involved: yes|no, why, changes: [...], risks: [...], needs: [other files] }`. "Not my responsibility"
   is `involved: no` with a reason. The chair pulls in files named under `needs` (scope expansion re-runs
   step 2 for them), asks follow-ups, and ends with a plan: per-file change list, order, verification.
   The `chair` manager makes **exactly one** model call per completed turn over a bounded summary plus the
   unresolved-needs list, and persists every selection and termination decision in the run's policy state;
   replay reads the persisted choices instead of re-deciding (review finding 8). The `refactoring` neuron
   keeps its own transcript checkpoint (summary, cursor) so a journal window reset never erases the request,
   and each file agent receives only the messages it has not seen (review finding 11).
4. **Sign-off.** The plan is a card with Approve and Revise; `revise(feedback)` reopens step 3.
5. **Edit.** Each file agent proposes edits into the `changeset` (member replacement, insertion, using
   directives, or a rename by symbol id). `changeset.check` applies them to one snapshot and returns
   diagnostics; errors route back to the responsible file agent (three attempts), then the chair.
6. **Verify.** `changeset.commit` writes the clean snapshot, `DotnetRunner` builds the standby slot and runs
   the affected tests (tests whose symbols reference changed symbols, then the full suite), results as a
   card. Failures go back to step 5 with the failing output.
7. **Land.** After the owner's approval: git commit on a `coding/<id>` branch with a `coding:` message, then
   `slot.promote`; a smoke turn in the new slot closes the loop.
8. **Report.** A card: files changed and why, tests run, slot promoted, conventions learned.

Every phase is a state saved in a reaction; the transcript is the chat's journal; a restart resumes from
the saved phase (R6.3). The Magentic workflow (R4.2) is the alternative engine for steps 3 and 4 (D4).

### 4.6 Learning the owner

Conventions live in `IMemory` (R6.5) under namespace `coding.<workspace>` with tags `convention`,
`decision`, `correction`, `file:<path>`, `symbol:<id>`. Writes come from three places: the owner says
"remember: ..." in chat; the owner corrects an agent during a discussion or review (the chair records a
`correction` with the original text); a landed refactoring records a `decision` (what changed and why).
Reads: every agent's instructions in step 2 carry the top conventions for the workspace and the file's
tags; the chair's prompt carries decisions near the scope. The digitalbraincore loop (R3.2) is preserved in
spirit: the model stores and recalls through named tools with descriptions that teach the pattern.

### 4.7 Model routing

`DigitalBrain:Coding:Models:{Chair, File, Reviewer}` name model markers (`IOpus5`, `IGrok46`, ...). Defaults:
chair and reviewer = the configured default LLM; file agents = `IGrok46` when the xAI key is configured,
else the default. The AppHost enables `ai.WithLlm<XaiModels.IGrok46>()` with its secret parameter. The
`agent` neuron already accepts `provider` and `model` in `Instruct`, so no new client plumbing is needed
(R6.3).

### 4.8 Tool surface

Native tools (all prefixed `code_`, registered with `AddNativeTool`, added to the workspace agent
allowlist): phase 0 `code_find_symbols`, `code_references`, `code_diagnostics`, `code_map`; phase 1
`code_skeleton`, `code_member`, `code_callers`, `code_implementations`, `code_propose_edit`,
`code_check`, `code_commit`, `code_build`, `code_test`; phase 2 `code_promote`; phase 3 `code_refactor`;
phase 4 `code_remember`, `code_recall`. Every neuron method is reachable through `/mcp` `describe` and
`call` once `DigitalBrain:Graph:Enabled` is on in the dev AppHost (D8).

### 4.9 Error handling

- Workspace not ready: reads return the status as advice ("opening, 12 of 35 projects; try again").
- Load failure: `Failed` with the first workspace diagnostic; `reload` retries; the durable map still answers.
- A `changeset.check` with errors never reaches disk; `commit` refuses with the diagnostics.
- `commit` is not atomic over several files (review finding 7): it writes the documents in order, then
  records the git generation; a crash between the two is reconciled at the next `workspace` activation by
  comparing the tree with the last recorded generation and reloading.
- A failed slot build leaves the live slot untouched; a failed smoke turn switches back and reports.
- Model timeouts inside a file agent's turn end that turn with a `Reply` rather than escaping the reaction
  (R3.2); the chair treats silence as "no stance" after one retry.
- Aspire unreachable: `slot` commands refuse with the Aspire error verbatim (R3.2, "a rejection is advice").

### 4.10 Testing

Three tiers, matching the repo (R6.5):

1. **Facts on an adhoc solution.** A two-project fixture (a declaring project, a referencing project, one
   file with a deliberate error) built with `AdhocWorkspace`; every query and edit primitive has a fact; the
   `workspace` neuron facts run on `BrainSimulation` with the adhoc loader registered.
2. **Gated self-tests.** `DIGITALBRAIN_CODING_SELF_TESTS=1` opens `DigitalBrain.slnx` through the real
   MSBuild loader and asserts project count, a known reference, and a clean diagnostics run; phase 2 adds a
   gated slot build.
3. **Scripted swarm scenarios.** `ScriptedChatClient` drives the chair and two file agents through scope,
   stances, plan, sign-off, edit and check on the adhoc fixture; a restart mid-discussion resumes.

## 5. Decision points

| # | Question | Default | Alternative |
|---|---|---|---|
| D1 | Where Roslyn runs | In the kernel silo as a module singleton (4.2) | A sidecar process with an RPC seam; only if the BuildHost or Locator misbehaves inside the silo (phase 0 proves it) |
| D2 | Live rebuild shape | Two kernel slots behind a gateway with the active-slot fence (4.4) | A single kernel restarted by Aspire (downtime per landing) or a separate forge silo (approach B) |
| D3 | Slot outputs | `ArtifactsPath` per slot, one working tree, the slot built from a recorded git generation | A git worktree per slot |
| D4 | Discussion engine | `chat` neuron with a new `chair` manager; `refactoring` neuron drives phases (4.5) | MAF Magentic workflow with `RequirePlanSignoff` inside the `refactoring` reaction, checkpointed to blob storage |
| D5 | Agent granularity | One agent per document, cap 24, then per type or project | One agent per project |
| D6 | Model routing | Config per role with the defaults in 4.7 | Fixed models |
| D7 | Map rendering | Reuse `IGraph` and `UiGraph`; layout and highlighting in phase 5 after UI phase 1 | Adopt `graphview` widgets wholesale now |
| D8 | MCP reachability | Set `DigitalBrain:Graph:Enabled=true` in the dev AppHost; fix `.mcp.json` to 5080 | Leave `/mcp` off; only the chat reaches the tools |
| D9 | Roslyn test strategy | Adhoc-loader facts plus gated self-tests (4.10) | Check a restored fixture solution into the repo |
| D10 | Edit path | Snapshot transactions only; file watcher folds external edits | Direct file writes with reload (IAW's path) |
| D11 | Learning store | `IMemory` namespace `coding.<workspace>` with tags | A dedicated `convention` neuron with latest-per-type signals |
| D12 | Sign-off gates | Before Edit and before Land, always | Land only |

## 6. Phases

Each phase is one PR with `coding:` commits and leaves the suite green.

0. **Workspace and map.** Module scaffold, Roslyn service with the adhoc and MSBuild loaders, `workspace`
   neuron (`open`, `reload`, `read`, `find-symbols`, `references`, `diagnostics`, `map`), four `code_*`
   tools in the workspace agent, the shell's `graph` editor, AppHost projection `WithSolution`, D8.
   Exit: the two chat lines in section 1 ("where is `ITimer` used", "map the solution") work against the
   running kernel; facts and the gated self-test pass. Plan: `plans/2026-09-13-coding-phase0-workspace.md`.
1. **Edits as transactions.** `changeset` neuron, skeleton and member reads, member-level edits, rename by
   symbol id, code fixes (`Features` package), `check` and `commit`, file watcher, the durable map cache in
   the `workspace` neuron's state (4.2), `DotnetRunner` build and test with parsed results, git commit on a
   branch. Exit: "rename X to Y" lands as a commit with the suite green.
2. **Slots.** Gateway project, `kernel-a`/`kernel-b`, ArtifactsPath builds, ClusterId minting, Aspire
   `execute_resource_command` widening, `slot` neuron, promote with health, smoke and rollback, shell SSE
   reconnect. Exit: "rebuild" promotes the standby without the chat losing more than a reconnect.
3. **Swarm.** `refactoring` neuron, `ImpactAnalyzer`, file agents, `chair` manager, stance and plan
   protocol, sign-off cards, edit fan-out, verification loop, report. Exit: the "add a `Pause` command"
   scenario in section 1.
4. **Learning.** Memory namespace, `code_remember`/`code_recall`, correction capture, injection into
   instructions. Exit: the "never add summary comments" scenario.
5. **Pretty map and depth.** Layered and force layouts, blast-radius highlighting, symbol neighborhoods,
   tests-for-symbol, package edges, multiple workspaces, model-routing card.

## 7. Traps recorded before starting

- Windows locks loaded assemblies: never build a slot into the output the live slot runs from (R5.4).
- `MSBuildLocator.RegisterDefaults()` must run before any `Microsoft.Build` type is JIT-compiled and from a
  method that references none (R1.1); the `BuildHost-netcore` folder must be in the silo output (R1.2).
- The tree Roslyn reads must be restored: `obj/project.assets.json` present for every project (R1.2).
- `TreatWarningsAsErrors` with `AnalysisLevel preview-all`: services use `ConfigureAwait(false)`, grain
  code `ConfigureAwait(true)` as the kernel does (R6.1).
- Descriptor rules reject a read method with two parameters or a mutator whose DTO is not a `Command`
  (R6.2); every DTO is `[GenerateSerializer]` with `[property: Id(n)]` and listed in the JSON context.
- A new card signal must be added in both `ChatNeuron.ReceiveAsync` and `OfferAsync` (R6.4); phase 0 avoids
  a new signal by rendering the map through `IGraph` and the `chart`-style tool result.
- `/mcp` is off without `DigitalBrain:Graph:Enabled` (R6.3); the flag also enables the session-neuron
  middleware and surface endpoints, so phase 0 verifies the shell after flipping it.
- A restarted silo with a reused ClusterId stalls on the dead membership row (R5.3).
- `[Llm<TModel>]` is internal to the AI module (R6.3); the coding module instructs agents by provider and
  model name instead.
- MAF moves to 1.21.0 when workflows are first used; `RoundRobinGroupChatManager` is the documented
  extension base (R4.1).
- UI phase 1 will rename `UiCardOffer` to `UiPartRef`; phase 0 adds no new card kind so nothing here
  migrates (R6.4).
- Native tool results pass the untrusted-content screen (R6.2); source code excerpts may trip it. Phase 0
  observes this on the live kernel and, if it does, the `code_*` tools are registered outside the screen
  with the reason recorded in `NOTES.md`.
- The shell accepts a tool result as an artifact only when it carries an `id`, and the auto-open kinds are
  listed in two files (`workspace_chat.dart` and `workspace_chat_presentation.dart`); a graph result must
  satisfy both (review finding 12).
- Two silos with one `ServiceId` share reminders and activations; a standby that is not fenced runs work
  before it is promoted (review finding 1).

## 8. Review findings (Codex, 2026-09-13)

An adversarial pass over sections 4.2 to 4.5 produced twelve findings; each is folded in above and
recorded here with its disposition.

| # | Severity | Finding | Disposition |
|---|---|---|---|
| 1 | blocker | A standby silo with the same `ServiceId` runs shared reminders before promotion | Active-slot lease and fence in 4.4 |
| 2 | blocker | Shared journals do not transfer activation ownership; rollback can expose older state | Fence (only the lease holder reacts); rollback only for changes to no persisted state type; old slot stopped after drain (4.4) |
| 3 | blocker | A reconnecting SSE stream cannot resume a MAF run that was in flight during the swap | The swap is a durable follow-up after the turn commits; reconnect by run id and cursor (4.4, phase 2) |
| 4 | major | Registering the locator from a hosted service is too late; a global copy-check suppression hides conflicts | Locator first in `Program.cs`; property scoped to three projects (4.2, phase 0 plan) |
| 5 | major | Locator success does not prove BuildHost works; a partial load became `Ready` | Loader returns failures; self-test requires zero (4.2, phase 0 plan) |
| 6 | major | Roslyn memory pressure can take down the silo | Bounded query gate, capped results, sidecar as the recorded fallback (4.2, D1) |
| 7 | major | Isolated outputs do not give a coherent source snapshot; `TryApplyChanges` is not atomic over files | Slot built from a recorded git generation (D3); ordered writes plus generation marker and reconciliation (4.9) |
| 8 | major | An LLM-driven manager replayed per turn re-decides history and multiplies cost | One persisted selection per completed turn (4.5) |
| 9 | blocker | Awaiting a peer's reply inside a reaction can never be answered | Principle "no waiting inside a reaction" (section 2) |
| 10 | major | A `uichat` participant cannot answer a `Turn`; re-instructing does not update the active run's roster | Owner is an observer and approver; explicit roster transition (4.5) |
| 11 | major | Journal window reset empties the transcript; sessions re-send it whole | Transcript checkpoint and cursor; unseen messages only (4.5) |
| 12 | blocker | The graph result lacked the `id` the shell requires and one of two opening lists | Stable `id`, both lists, end-to-end widget test (4.3, phase 0 plan) |
