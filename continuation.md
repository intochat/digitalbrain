# Continuation — keep cleaning on subscribe/unsubscribe

## MCP-backed IAspire and GenAI telemetry — 2026-09-05

Implemented the approved Microsoft-module specialist. `IAspire : IAgent` inherits
AgentRequest/AgentReply; `Aspire : Agent` discovers native Aspire MCP functions.
There are no Aspire status signals, resource DTOs or per-tool method wrappers.
Ino gets the generic generated `ask_aspire` delegation capability. The source
neuron sends and reads the target's exact-causation reply without reentering the
serialized owner root. Principal scope, cancellation, restricted continuations,
and source-owned Learned/Bound semantics are preserved.

The local connection binds the configured AppHost using `list_apphosts` before
`select_apphost`; the CLI initializes its discovery lazily, so immediate selection
was the cause of the initial 2.9-second failure. Sessions and tool catalogs are
isolated per agent identity. Discovery refresh, transport failure, cancellation,
native errors and truncation have generic SDK tests. Read-only resources/logs/
traces are permitted; no background monitor, restart or deploy behavior is added.

Live `list_resources` returned about 64 KB and has no filtering arguments. The
initial 24 KB budget prevented the user's healthy-resource count. Response and
whole-result classifier bounds are now 128 KiB. Generic redaction preserves JSON
embedded after MCP prose and marks modified envelopes. The graph uses bounded,
screened previews, generic agent/tool lifecycle signals, first-request target
discovery, clickable transient activity and actual Learned edges. Aspire uses
the official unchanged SVG in the UI kit.

Also fixed compact chat: an old failed pre-acceptance stream could cover a later
durable reply. Timestamp selection plus independent active-stream feedback fixes
this without changing exact-command recovery. The reported question did have a
persisted 212-character truncation explanation; the display defect was reproduced
separately in a widget regression.

GenAI content had been hardcoded off in AppHost and AIClients despite the exposed
setting. AIClients now honors DigitalBrain:AI:Telemetry:EnableSensitiveData, then
the standard OTEL content flag, default false. Local Development run mode opts in;
publish defaults off. AgentTelemetry creates `invoke_agent Ino` / `invoke_agent
Aspire · DigitalBrain` with agent/conversation/command identity. MEAI 10.9 also
requires non-exported `__EnableSensitiveData__` on its nearest invoke_agent parent
for tool arguments/results. Model/content capture remains in the MEAI pipeline,
without duplicate payload logging on agent spans. Live exported traces verified
both model messages and tool results. Old traces cannot gain omitted content later.

Validation: clean host build; 33 focused Simulation, 19 substrate, 8 scripting,
2 telemetry hosting, and 13 backend graph/HTTP/chat-stream tests passed. Flutter
graph/core/kit: 85 tests; compact/surface/workspace: 18 tests; affected analysis
clean. Run HTTP E2E with the main AppHost stopped (both bind 5080). After an
overlapping run was cancelled, all 13 checks passed with the fixture alone.

Implementation/design evidence: docs/plans/2026-09-05-microsoft-aspire-neuron.md.
User instructions and telemetry configuration: docs/GETTING_STARTED.md. The main
AppHost and native Flutter app are running with the final implementation. No skills
or plugins were used for this implementation; CLI and Microsoft documentation were
used for investigation. There are no commits from this work.

## Approved Lumen + Forui implementation — 2026-09-05

The user approved Lumen and Forui. Flutter now opens **My brain**, with warm light
surfaces, icon neurons in module regions, Ino, a bottom composer/latest reply,
and the same full conversation reachable through history or navigation. Forui
0.26 is behind digitalbrain_ui_kit; KitThemeScope bridges its extracted Material
widgets into the existing MaterialApp. Legacy demos remain secondary surfaces.
Ino is a programmatic, reduced-motion-aware face; no unaudited old Rive artwork
was imported.

GET /chats/{chatName}/brain projects actual current-chat participants, reachable
stored synapses, and bounded recent journal activity. POST the same prefix plus
/subscriptions dispatches existing Subscribe/Unsubscribe through the kernel.
Scope and principal validation remain server-side; arbitrary payloads are omitted.
The client polls after each completed read (~2 seconds), aborts timed-out HTTP
requests, ignores late disposed results, preserves stale snapshots visibly, and
only highlights fresh activity. It waits for a fresh snapshot to confirm mutations.
No alternate graph store/runtime was added to the kernel. The Flutter store is
only a disposable UI projection. Some direct assistant/tool calls have no journal
event; this graph does not claim complete internal tool tracing.

The native Windows app was hot-restarted into Lumen and tested using Computer:
real 'hi' sent from the graph returned 'Hi! What would you like help with?'; the
saved conversation restored; actual node and Learned-edge inspectors rendered.
A temporary assistant:assistant -> current main chat Bound Note subscription was
created in the dialog, observed in the live graph, then removed via Unsubscribe.
The UI confirmed removal and no Note edge remained. Ordinary Learned control
routes from sending Subscribe/Unsubscribe remain observable kernel history.

Focused checks so far: 37 unique shell tests, 3 Forui kit tests, 22 core/client
tests, and 8 backend tests passed (70 total). Backend includes an isolated real
HTTP/AppHost subscription round trip and foreign-principal rejection. Saved main
snapshot also returned 200 (6 nodes, initially 2 stored edges, 36 activities;
167ms warm read). Whole-workspace analysis had no type errors; its import/brace findings were fixed. Final targeted analysis of the six affected files passed with no issues (--fatal-infos).

Implementation plan: docs/plans/2026-09-05-lumen-forui-implementation.md.
Updated usage: docs/GETTING_STARTED.md. Native app remains managed by the main
Aspire AppHost. Do not reintroduce the old simulation as the default live graph.

## Graph-first redesign options — 2026-09-05

The user requested a complete visual redesign explored through 3–4 interactive
HTML options before choosing the Flutter implementation. The design collection is
`docs/design/2026-09-05-app-redesign/index.html`: Lumen (warm glass), Aurora (dark
immersive glass), Tactile (light neumorphism), and Atlas (compact technical graph).
Run `node docs/design/2026-09-05-app-redesign/serve.cjs`; the local gallery uses
`http://127.0.0.1:8743/`. Each page has a graph as its main surface, icon neurons,
an Ino persona, a bottom composer/latest reply, expandable history, inspectors,
and simulated subscribe/unsubscribe behavior. These are illustrative local
prototypes, not a live backend projection. No Flutter dependencies were changed.

`research.html` contains the sourced six-kit comparison. Leading recommendation:
Forui behind `digitalbrain_ui_kit`, Rive for the state-driven Ino persona, ordinary
Flutter animation for node feedback, and a dedicated graph renderer. shadcn_ui
is the incremental Material-compatible alternative. Glass/depth are our surface
tokens, not a reason to adopt the old flutter_neumorphic package as a foundation.
Actual old Ino Rive assets and implementation were found under
`D:/Projects/ino/clients/ino.flutter/assets/rive` and `lib/persona`; their detailed
state mapping needs auditing. The HTML personas are authored CSS stand-ins.

Read `design-brief.md` in that design directory for the full behavior/architecture
and migration boundaries. Preserve current chat acceptance/journal recovery and
source-owned Bound/Learned semantics. The future live graph needs a current
topology projection and observed, correlated traffic; do not treat the HTML
example graph as production state. Desktop/mobile screenshots and browser
verification JSON are saved beside the concepts. Direction selection is next.

## Chat recovery and modular Graph — 2026-09-05

The original Flutter `main` failure had two causes: persisted ExecutionContext
JSON still named the old execution-contract namespace, and Flutter required
`synapse` while the server sent `signal`, silently discarding every journal row.
The Execution module now reads the six known historical context types without
erasing saved state. Flutter uses `signal`; malformed known frames surface errors.
Text and voice share a turn observer that emits `chat-accepted` identities and
`chat-error` terminal outcomes. UI sends reconcile by exact command ID, including
late history and identical prompts. Activity retains diagnostics; chat shows short
failures and hides transient lifecycle rows.

Graph now has the common `KitChat` surface on the left and a modular 3D scene on
the right. The Chat reply, Code review, and Subscribe / unsubscribe examples are
explicit local simulations, with pause/reset and source-owned Bound edge changes.
The renderer reloads changed topology, animates signals, sizes itself to its pane,
and handles asynchronous setup/disposal. Onboarding no longer teaches default-type
broadcast fallback. The Personal code review shortcut immediately requests a local
diff review.

Native Computer Use verified the repaired saved chat: command
`3862f8668b6240e1a7a44e44d2f341c4` completed at journal sequence 57; Aspire trace
`a53db302949380cbe14f97583be6eb2e` had no errors and OpenAI HTTP 200. A typed `hi`
sent with the Graph composer also returned a visible answer. MCP uses a different
principal; its independent success alone could not verify the old UI context.
The Graph Personal code review shortcut also completed, with an actual
`read_repository_diff` tool span in Aspire trace
`acde0b82c28558832032a64bdfd1dbf1`. Its review was partial because this checkout's
patch exceeded the tool limit. Native simulation pause/resume/reset and navigation
were verified. The renderer's inner gesture detector was isolated so the UI-kit
graph owns mouse orbit and zoom.
Final focused verification: 65 Flutter tests and four backend regressions passed;
native orbit was confirmed after loading the final Dart kernel. The full Flutter
workspace analyzer reported no errors or warnings; its redundant test import was
removed. The AppHost services are running, with Graph open in the native client.
See `docs/plans/2026-09-05-chat-and-graph-stability.md` for the plan and evidence.

## Latest working slice — 2026-09-05

Flutter chat is now the C# behavior authoring surface. Start with `docs/GETTING_STARTED.md`
and `docs/examples/personal-code-review.csx`. `Assistant` has `admit_behavior`,
`list_behaviors`, `read_behavior`, and `remove_behavior`; mutations use its normal
neuron send path. Development AppHost config enables a read-only `read_repository_diff`
tool for this checkout and owner. The obsolete Gherkin/Gemma behavior IDE, dead HTTP
calls, Experience prompt/fake branches, and mandatory codegraph build hook are removed.

`BehaviorsNeuron` owns durable current source/revision/status/diagnostics, with
`IBehaviorsKernel.ReadCurrent` as a narrowly whitelisted interleaved read. The worker
hydrates current definitions, reconciles removals/replacements, refreshes every five
seconds to recover lost notifications, restores the admitting principal, and reports
revision-matched results. Completed/failed definitions require explicit readmission to
run again. Cancellation is cooperative and bounded during shutdown. Legacy journal-only
definitions need readmission; old sources are not automatically revived.

Background model work should use the existing queued `IChat` turn path and observe its
result, as the example does. A direct script `IAssistant.RequestAsync(AgentRequest)` holds
the owner root until the model finishes. The integration test verifies chat can accept
another message during a blocked background reviewer.

Verified: Scripting 29, Substrate 78, Simulation 72, focused Flutter/Dart 28 tests;
core/shell analyzers clean; AppHost builds with zero warnings/errors. Live AppHost launch
was healthy (Flutter/kernel/scripting/MCP), and actual chat smoke checks successfully read
the repository diff and admitted/ran/read/removed a temporary C# behavior. No smoke behavior
remains. These changes and the naming/membrane work below are still uncommitted.

---

Handoff for the next session (OpenAI Astra 6, ultrathink). **Goal: keep cleaning and simplifying the repo on the new subscribe/unsubscribe architecture.** Do not invent a second graph, a synapse grain, or a catalog. Make the existing graph smaller and truer.

**Branch:** `codex/day-zero-scripting`  
**Repo:** DigitalBrain (`D:\digitalbrain`)  
**Date:** 2026-09-04  
**Product sentence:** a personal assistant whose durable graph a user (or the assistant) programs with typed C#. **A neuron fires a signal along a synapse.**

Read first: `CONTEXT.md`, `docs/JOURNALS.md`, `docs/superpowers/specs/2026-09-04-neuron-synapse-signal-research.md`.

---

## Goal

Ruthlessly simplify around **who calls whom**:

| Primitive | Job |
|---|---|
| `IHandle<T>` | capability — this *type* may receive `T` |
| **Synapse** (on the **source**) | who *does* receive `T` from **this** instance |
| `SubscribeTo` / `UnsubscribeFrom` | write / delete a **Bound** synapse (always-on; does not decay) |
| `Send` / `Publish` | one directed fire; handled send writes **Learned** (decays) |
| `Broadcast` | fan-out **only** along existing synapses of that signal type; never self; never “all default `IHandle` types” |

Keep doing what the last waves did: delete leftover dual runtimes, catalogs, and “tier 1 innate routing.” Align comments, tests, and names with the graph above. Do **not** big-bang rewrite synapse kinds until the owner answers the open questions in the research paper.

---

## How DigitalBrain works now

### Owner API (scripts, assistant)

`IDigitalBrain` is the only product handle. Scripts are out-of-process C#. Behaviors are admitted scripts that watch journals and fire typed signals.

```csharp
await Brain.Get<IBehaviors>().SendAsync(new AdmitBehavior("elon-chart", source));
await Brain.Get<ITimeline>("alice")
    .SubscribeToAsync<ITimeline, IAccount, NewPost>(elon.Id);
await Brain.Get<IXAccount>("elon").SendAsync(new PublishPost("starship"));
await Brain.GetEntity<IChart>("elon-activity").Append(point, title);
```

- `SendAsync` / `PublishAsync` compile only if `TNeuron : IHandle<TSignal>`.
- `SubscribeToAsync` sends a `Subscribe` signal to the **subscriber**; the subscriber asks the **source** to `BindOutgoing`.
- Inside a neuron, `SubscribeToAsync` calls `BindOutgoing` directly (no extra `Subscribe` journal row).

English is how the owner asks. A compiled script is what they get. **No second runtime, grant catalog, or JSON capability bus.**

### Hosting (Orleans — not product language)

| Surface | Role |
|---|---|
| `INeuron` | marker + `IHandle<Subscribe>` / `Unsubscribe` |
| `INeuronGrain` | membrane: `Deliver`, `BindOutgoing`, `UnbindOutgoing` |
| `INeuronQuery` | `ReadJournal` / `ReadSynapses` (`[AlwaysInterleave]`), `Watch` / `Unwatch` |
| `Neuron` | `DurableGrain`; owns `NeuronSynapses` + incoming/outgoing `JournalWindow` through `NeuronJournals` |
| `Entity<TState>` | `IPersistentState` snapshot; **not** on the graph |
| `IBrainNeuron` | owner root (`sessionneuron` / `session`); `Send`, proxy journal/synapse reads |

`NeuronId` is a DDD struct `(type, owner, name)`. Grain mapping is hosting. Scripts use `For<TNeuron>`, not grain ids. **`INeuronGrain` is not a synapse** (one neuron, many edges).

### Three planes (do not collapse)

1. **Anatomy** — `NeuronSynapses` (`IDurableDictionary` on the **source**). Bound = program. Learned = scar of a handled send. Value, not a grain, not an event.
2. **Traffic** — `JournalWindow` incoming/outgoing. **512 entries or 512 KB.** Recency, script triggers, highlight. Not event sourcing; not a record of synapses.
3. **Episode** — `CorrelationId` on `SignalDelivery` **rows**. Envelope has **`Caller`, no `Target`**. Reconstruct edges from **incoming** journals of a **seed set**. Optional later `IGraph` entity snapshot.

Orleans `DurableGrain` journaling persists those collections. It is **not** “replay events to rebuild the neuron.” Production journal storage is blob, not table.

### Fire path

`SignalSender`:

- Journals **outgoing** on the source **before** `Deliver`.
- Same-activation send uses `_deliverLocally` (serialized neuron cannot await its own proxy).
- Other activations: `GetGrain<INeuronGrain>(id).Deliver`.
- After `Handled`, `NeuronSynapses.Reinforce` on the **source** (Learned heat; Bound stays Bound).
- Incoming journal appends inside `DispatchDeliveryAsync` (any non-throwing outcome).

`SignalRouter.BroadcastRecipientsFor` = synapses of that signal type on this source, excluding self. **No silo-wide `IHandle` scan** (`SignalHandlerIndex` deleted).

### Membrane filter (implemented, may still be uncommitted)

`NeuronMembraneFilter` (`IIncomingGrainCallFilter`): auth + tracing on `INeuronGrain` and `INeuronQuery` only.

- May refuse foreign-owner `Deliver` / `Bind` / grain-to-grain query.
- **Must not** `Reinforce`, `Bind`, or append journals.
- Does **not** run on self-send.
- Client `GetGrain<INeuronQuery>` still allowed (tests). Remaining hole: **client** raw query of a foreign grain. Product path uses `IBrainNeuron`.

### Chat / agents (adjacent, do not mix into this cleanup unless it is trash)

- Chat stays `IHandle` on `IChat`; `ChatTurnWorker` + `IGrainFactory` (nested `IDigitalBrain.RequestAsync` from a neuron turn **deadlocks** `BrainNeuron.Send`).
- `AgentRequest` / in-silo `IAgentKernel.Ask` landed (`58318ccf`). Live MCP bind + Gmail/Salesforce/Aspire specialist neurons are a **later** approved design, not this cleanup wave.
- Hop count 1 is that spec, not kernel law.

### Tests that pin the graph

- `tests/DigitalBrain.Substrate.Tests/Features/broadcast-and-pubsub.feature`
- `tests/DigitalBrain.Substrate.Tests/Features/journal-and-state.feature`
- `SignalRoutingTests`, `SignalSenderTests`, `FacadeTests`, `MembraneFilterTests`
- Simulation suite must stay green when you touch the kernel.

---

## Already done (this branch)

Commits (newest first):

| Commit | What |
|---|---|
| `b1423ecf` | Research paper + JOURNALS rule 5: filters are membrane, not writers |
| `fd01b595` | Synapse-only broadcast; typed `SubscribeTo` / `Publish`; `INeuron` vs `INeuronGrain`; delete `SignalHandlerIndex` |
| `b80cfe70` | Execution trash removal |
| `58318ccf` | `AgentRequest` + in-silo `IAgentKernel` |
| `d00ccd68` / `b7844ab2` | Typed `Send` / `Request`; drop unused execution wrappers |
| `1682e98c` | Admit typed behaviors |
| `ea66c000` | Catalog contracts removed |

Also in history of this work (already on the branch unless noted):

- GetGrainProxy deleted (Orleans backdoor).
- Catalog / capability bus / grants sandbox rejected as product path.
- Execution kept chat-turn only; no dual scripting runtime inside Execution.
- `Subscribe` / `Unsubscribe` signals; `SynapseKind.Bound`.
- `NeuronReferenceExtensions.SubscribeToAsync` / `UnsubscribeFromAsync`.

**Working tree (implement, not necessarily committed):**

- `src/Kernel/DigitalBrain/Neuron/NeuronMembraneFilter.cs`
- `src/Kernel/DigitalBrain/Hosting/DigitalBrainRuntime.cs` (registers the filter)
- `tests/DigitalBrain.Substrate.Tests/MembraneFilterTests.cs`

Commit these if still dirty before starting a new cleanup wave.

---

## Cleanup still worth doing (same architecture)

Do these in small PRs. Prefer delete over wrap.

1. **Commit the membrane filter** if uncommitted; do not extend it to write the graph.
2. **Naming:** `NeuronSynapses` owns outgoing relationships; `NeuronJournals` coordinates incoming/outgoing `JournalWindow`. `docs/JOURNALS.md` now uses `IBrainNeuron`; `OwnerSessionJournal` remains the actual kernel SSE helper name, and `sessionneuron` remains the persisted brain-neuron address.
3. **Comments and docs** that still say “tier 1 default `IHandle`”, “broadcast to all defaults”, `INeuron.Deliver`, `SignalHandlerIndex`.
4. **`SynapseKind` leftovers** (do **not** delete from the wire until the owner answers): `Innate`, `Discovered`, `IsBlocking`. Product kinds in use: **Bound** and **Learned**. Weight does **not** choose Broadcast audience (only order + Learned prune).
5. **`signalType: string`** (`typeof(T).Name`) — collision-prone; leave unless a tiny typed helper is obviously better.
6. **Client raw `INeuronQuery`** on a foreign grain — still open; only close if tests can move to `IDigitalBrain` / `IBrainNeuron` without a rewrite explosion.
7. **In-neuron `SubscribeToAsync` vs script `Subscribe` journal** — different observability; do not “fix” by logging Subscribe into the traffic window unless the owner wants Q8.
8. **Do not implement:** synapse-as-grain, synapse-as-event in `JournalWindow`, `INeuronGrain` → `ISynapse`, `SynapseMetadata : Neuron`, grain-per-edge metadata, second runtime, JSON tools bus, `ListNeurons` registry, walking the graph as an execution engine.

Specialist MCP agents (Gmail / Salesforce / Aspire as `IAgent`) are a **separate** approved design (`docs/superpowers/specs/2026-09-04-mcp-specialist-agents-design.md`). Do not interleave that with synapse cleanup unless a file is obviously trash.

---

## Invariants (fail the change if you break one)

- Broadcast without a Bound/Learned synapse reaches **nobody**, even if a type `IHandle`s the signal.
- Unsubscribe **removes** the dict key (no leftover Learned scar).
- Self-send stays in-process; neurons stay serialized (`NeuronConcurrency`).
- Nested `IDigitalBrain.RequestAsync` / `BrainNeuron.Send` from a still-open `Deliver` deadlocks — use `IGrainFactory` + kernel interfaces (`IChatKernel`, `IAgentKernel`, …).
- Kernel must not reference Scripting.
- No generated Orleans grain types as the product API.
- `dotnet test tests/DigitalBrain.Substrate.Tests` and `tests/DigitalBrain.Simulation.Tests` green.

---

## Open questions (do not silently pick)

From the research paper — ask the owner if a cleanup needs one:

1. Hebbian `weight` on the wire vs Bound/Learned + `lastFiredAt`/`fireCount` only.
2. “Last time I did this” = journal tools vs Entity vs both.
3. Whole-brain viz = seed fan-out vs kit `IGraph` vs projection grain.
4. May the assistant `SubscribeTo` without an admitted behavior.
5. Alternative D: NeuronSynapses holds **only Bound** (one-shot Send no longer Broadcast-able).
6. Physical prune of Learned keys.
7. Name index vs BFS for viz.
8. Journal `Subscribe` on the in-neuron path.
9. Auto episode snapshot at end of `ChatTurnWorker`.

---

## Prompt for OpenAI Astra 6 — ultrathink mode

Copy everything in the fence.

```
You are continuing DigitalBrain on branch codex/day-zero-scripting. Workspace: the digitalbrain repo.

ULTRATHINK. Before any edit: read CONTEXT.md, docs/JOURNALS.md, docs/superpowers/specs/2026-09-04-neuron-synapse-signal-research.md, then verify claims in code (INeuron, INeuronGrain, Neuron, SignalSender, SignalRouter, NeuronSynapses, JournalWindow, NeuronMembraneFilter, NeuronReferenceExtensions). Do not trust this prompt over the code. Think through blast radius. Prefer delete.

GOAL
Keep cleaning and simplifying the repo on the NEW subscribe/unsubscribe architecture. Small PRs. No new product runtime.

HOW THE GRAPH WORKS (must preserve)
- Sentence: a neuron fires a signal along a synapse.
- IHandle<T> = capability. Synapse on the SOURCE = who actually receives T from this instance.
- SubscribeTo writes Bound (durable, no decay). Unsubscribe removes the key.
- Handled Send writes Learned (decays). Broadcast follows existing synapses only — never all default IHandle types, never self.
- Scripts: IDigitalBrain.Get / SendAsync / PublishAsync / SubscribeToAsync. Typed: TNeuron must IHandle TSignal.
- INeuron = marker + IHandle Subscribe/Unsubscribe. INeuronGrain = Orleans Deliver/BindOutgoing/UnbindOutgoing. Not ISynapse.
- Anatomy (NeuronSynapses) vs traffic (JournalWindow 512 entries / 512KB) vs episode (CorrelationId on SignalDelivery; Caller, no Target). DurableGrain is not event sourcing.
- SignalSender appends outgoing deliveries and reinforces synapses after a handled send; Neuron appends incoming deliveries and binds/unbinds outgoing synapses. Incoming grain call filter is membrane (auth/trace) only. Self-send is in-process and skips the filter.
- No nested IDigitalBrain.RequestAsync from a neuron turn (deadlocks BrainNeuron.Send).
- No second runtime, grant catalog, JSON capability bus, synapse grains, synapse event store, ListNeurons registry.

ALREADY DONE
- fd01b595 synapse-only broadcast + typed subscribe; SignalHandlerIndex gone.
- b1423ecf three-plane research paper; filters must not write the graph.
- Membrane filter implemented (NeuronMembraneFilter + MembraneFilterTests) — commit if still uncommitted.
- Catalog deleted; GetGrainProxy deleted; Execution chat-turn only; typed Send/Request; AgentRequest/IAgentKernel (do not expand MCP specialists in this wave).

YOUR WAVE
1. git status; commit membrane filter if dirty.
2. Find leftover “tier 1 / default IHandle broadcast / INeuron.Deliver / SignalHandlerIndex / ISessionNeuron” comments, docs, tests.
3. Delete or rewrite only what contradicts subscribe/unsubscribe. Do not delete SynapseKind.Innate/Discovered/IsBlocking/Weight from the wire without the owner.
4. Keep Substrate + Simulation tests green.
5. Stop and ask if a change needs an open question from the research paper.

OUTPUT
Say what you deleted/simplified, what you left, and which tests you ran. Ultrathink first; then implement. Do not write a new architecture novel unless the owner asks.
```
