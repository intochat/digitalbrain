# ino — vision and roadmap

ino is an AI-native operating system that integrates into every existing OS. Not a desktop metaphor. A continuously self-improving multi-agent system built on three primitives: neurons, synapses, and the self-improving loop.

> **See also:** [`neuron-unified-vision.md`](./neuron-unified-vision.md) — the seven aspects of a neuron (identity, state, reasoning, actions, scheduling, integrations, metrics + NeuronML), domains as composition units, and the roadmap beats toward self-improvement. Tracked as epic [#12](https://github.com/LeftTwixWand/ino/issues/12).

## Current state (April 2026)

### What works today

**Neuron primitives** — Orleans grains that create, connect, and fire synapses. Every neuron has an ID, purpose, capabilities, and a synapse schema. Every synapse carries a verb, payload, and decay score. The primitives compile, persist, and are testable.

**Timeline** — a singleton grain that captures every neuron activation and synapse firing as a durable event with sequence numbers. Supports range queries, decay-aware filtering, consolidation (hot → warm → cold → soft-deleted), and access boost on retrieval. Events carry kind, source/target IDs, correlation IDs, and arbitrary payload.

**Time travel** — scrub to any point in the timeline and reconstruct the system state at that moment: which neurons were active, which correlations were open, cumulative event counts. Exposed via MCP tools (`timetravel_list_events`, `timetravel_get_state_at`, etc.) and a hex1b widget tree (`TimelineView`).

**Behavior memory** — a vector-searchable store of BDD scenario examples. Ingest Gherkin `.feature` files, embed them, and search by semantic similarity. Currently used as a reference; will become load-bearing for Cortex routing.

**Command dispatcher** — a universal command shell (`InoCommandDispatcher`) that drives all surfaces: `create`, `connect`, `fire`, `list`, `synapses`, `timeline`, `count`, `at`, `help`. Presentation-agnostic — writes to any `TextWriter`.

**Surfaces** — Telegram bot + mini-app (terminal-style HTML UI with command chips), MCP server at localhost:5300, Blazor DevUI with Cytoscape.js real-time visualization, ino-windows console REPL.

**Infrastructure** — Aspire orchestration, Orleans clustering, OpenTelemetry tracing, ngrok tunneling for Telegram webhooks, Whisper voice-to-text integration (wired, not yet surfaced).

### What's missing

The system can create neurons and fire synapses, but **there is no intelligence routing user intent to the right neurons**. Today, a human types explicit commands (`fire alpha beta greet`). The gap between "send me the quarterly report" and the right chain of synapse firings is bridged by the human, not by ino.

---

## Cortex — ino's navigation engine

Cortex is a specialized neuron that closes the intent-to-action gap. It receives natural language from any surface, uses an LLM to decompose the request, and routes synapses to specialist neurons.

### How it works

```
User: "send me the quarterly report and remind me to review it tomorrow"
  │
  ▼
Cortex receives Synapse { verb: "user_message" }
  │
  ├── reads AgentRegistry catalog (all specialists + synapse schemas)
  ├── queries BehaviorMemory (vector search for similar past scenarios)
  │
  ├── LLM decomposes: [file_delivery, scheduler]
  │
  ├──► FileDeliverySpecialist: find_file → deliver_file → done
  └──► SchedulerSpecialist: schedule → scheduled
  │
  ▼
Cortex composes: "Sent Q1-report.pdf to Telegram.
                   I'll remind you to review it tomorrow at 10am."
```

Every arrow is a synapse. Every synapse persists at `decay=100`. The chain of firings IS the memory of this interaction. No separate memory store.

### Self-improvement

When Cortex can't find a specialist for a request, it creates one:

1. Fires to NeuronFactory with the intent description
2. NeuronFactory generates: system prompt + tool selection + Roslyn script
3. Registers the new specialist in AgentRegistry (~10ms)
4. Cortex retries routing — discovers the new specialist instantly
5. The specialist persists. Next similar request routes directly.

This is L1 self-improvement: the overwhelmingly common case. No silo restart, no code deployment, no human intervention. The compiled neuron types (IShell, IFileSystem, etc.) form the kernel; everything else grows organically.

---

## Specialist neurons (starter set)

Each specialist owns one domain end-to-end. Each is covered by a Gherkin `.feature` file — because every synapse+neuron combination forms behavior, and behavior requires tests.

| Specialist | What it does | Key synapses |
|---|---|---|
| **ShellSpecialist** | Executes OS commands, interprets results | `execute` → IShell → `execute_result` |
| **FileDeliverySpecialist** | Finds files and pushes them to the user's surface | `find_file` → IFileSystem, `deliver_file` → Delivery |
| **SchedulerSpecialist** | Time-triggered reminders via Orleans reminders | `schedule` → persist, `reminder_due` → Cortex |
| **RecallSpecialist** | Queries the timeline as memory with decay filtering | `recall_recent`, `recall_range` → ITimelineReader |
| **SummarizerSpecialist** | Synthesizes natural language summaries of timeline ranges | `summarize` → timeline + LLM → `summary` |

---

## Parallel universes

What-if analysis over ino's own decision history. Fork from a timeline checkpoint, modify an event, replay the system forward, and compare the divergent outcome against the original.

### The idea

Every decision ino makes is a synapse firing recorded on the timeline. Parallel universes let you ask: "what would have happened if ino had made a different decision at step 3?"

1. **Checkpoint** — pick a sequence number as the fork point
2. **Modify** — change the synapse at the fork (different verb, different payload, different target)
3. **Replay** — re-fire synapses from the fork point forward in a sandboxed environment
4. **Compare** — see both timelines side-by-side: where they diverge, which neurons activated differently, different outcomes

### Why it matters

This is event sourcing with projections applied to an AI system's own reasoning. It enables:

- **Debugging** — "why did ino route to the wrong specialist?" Fork, try the correct routing, see if the outcome improves.
- **Learning** — if the alternate universe produced a better outcome, ino can learn from it (update BehaviorMemory with the better scenario).
- **Safety** — before deploying a new specialist or changing Cortex's routing, simulate the change in a parallel universe against historical requests.
- **User-facing** — "what would have happened if I'd asked differently?" is a natural question for a personal assistant.

### Implementation

`UniverseGrain` keyed by universe ID. Contains its own `TimelineState` seeded from the source timeline up to the checkpoint. Replay re-fires synapses against sandboxed neurons (mock surfaces, isolated state). `CompareAsync` returns a structured diff of the two timelines.

---

## Client shell — ino everywhere

ino renders natively on every surface via a surface-agnostic `SceneGraph`.
One tree, multiple renderers:

| Surface | Renderer | Status |
|---|---|---|
| Flutter mobile/web (primary) | `ino.flutter` (CanvasKit) | prototypes landed |
| Telegram mini-app | reuses `ino.flutter` in Telegram webview | planned |
| Windows/macOS/Linux terminal | `ConsolePresentationAdapter` (hex1b) | planned |
| Tests | `HeadlessPresentationAdapter` | planned |

### Views

Tab bar: `[Mind] [Live] [Trace]`. A persona mascot floats bottom-right
as the launcher for system surfaces (Settings, Marketplace, Domains,
Neurons, Synapses, Memory).

**Mind** — spatial, passive. A living constellation of the user's
installed domain neurons as glowing neurons. Synapse arcs fire between them
with floating message labels (the message payload IS the visible
element). The camera gently follows the active neuron.

**Live** — active, interactive. Contextual cockpit where every
currently-running neuron flow surfaces its own Remote Flutter Widget
card. User swipes between cards and taps verb buttons directly
(`AddStop`, `Skip`, `Cancel`, `Reply`) — no voice required for simple
controls.

**Trace** — chronological, historical. Scrollable log of every neuron
activation and synapse firing, each row tagged with its synapse
signature (e.g. `mime: write → send`). Same data as Mind, different
lens. Range queries, decay-aware filtering, and time-travel scrubbing
live here. Parallel-universe forking is reachable from any Trace row.

Creator mode (neuron list, raw command shell, live synapse graph) moves
into the persona launcher's **Neurons** / **Synapses** tiles — out of
the main tab bar, into a drawer for builders and debuggers.

Full UI spec and the Stitch mockup project are in
[`docs/design.md`](./design.md).

---

## Build phases

| Phase | Focus | Outcome |
|---|---|---|
| **1** | Cortex + synapse delivery | Natural language → specialist routing works. FireAsync delivers synapses to target neurons. |
| **2** | 5 specialist neurons + BDD | Complete Cortex→specialist→result chains. One `.feature` file per specialist. |
| **3** | Parallel universes engine | Fork, replay, compare. Sandboxed execution. No UI yet — MCP/command-line driven. |
| **4** | TUI shell | hex1b upgrade, tab bar, 4 views, Aspire stream tee, welcome/persona. |
| **5** | Telegram convergence | Same widget tree served to Telegram mini-app via WebSocket. |

Voice integration, Flutter renderer, and NeuronFactory (self-creating specialists) are follow-up phases after Phase 5.
