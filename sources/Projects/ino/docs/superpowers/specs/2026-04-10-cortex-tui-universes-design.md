# Cortex + Specialists + Parallel Universes + TUI Shell

**Date:** 2026-04-10
**Status:** Approved
**Approach:** A (bottom-up — engine first, TUI last)

## Summary

This spec covers the next major evolution of ino: a Cortex routing engine that turns natural language into neuron/synapse chains, five starter specialist neurons with BDD coverage, a parallel universes simulation engine, and a cross-platform hex1b TUI shell with four views.

### Decisions made

- **Routing architecture:** Option C — Cortex + Specialists. Cortex uses LLM to decompose intent and route to specialists. Specialists act independently. New capabilities added via L1 self-improvement (register in AgentRegistry, Cortex discovers automatically).
- **Foundation:** ino-new is ~60% of the target. Additive changes, no rewrites.
- **Persona:** Warm assistant — conversational but not chatty. Evolves as ino accumulates knowledge.
- **TUI navigation:** Tab bar — `[Chat] [Timeline] [Universes] [Dev]`.
- **Voice:** Deferred to a follow-up spec.
- **Build order:** Engine first (Cortex → specialists → universes), TUI last.

---

## 1. Cortex — ino's routing engine

### What it is

A specialized neuron that receives all user input, uses an LLM to decompose intent, and fans out synapses to specialist neurons. The only neuron that "thinks" about routing — specialists just act.

### How it works

1. User says "send me the quarterly report" on any surface (terminal, Telegram, future Flutter).
2. Surface layer fires `Synapse { verb: "user_message", payload: { text: "..." } }` to Cortex.
3. Cortex's LLM receives:
   - The user message
   - The AgentRegistry catalog (all specialists with their synapse schemas)
   - Top BehaviorMemory hits for the query (vector similarity)
4. LLM outputs: `[{ specialist: "file-delivery", verb: "handle", payload: { ... } }]`
5. Cortex fires synapses to each specialist. Specialists fire back `done` synapses. Cortex composes the final reply.

### Multi-step decomposition

"Send the file AND remind me to review it" — Cortex decomposes into two parallel specialist firings, waits for both, composes one reply.

### Self-improvement (L1)

When Cortex can't find a matching specialist in the catalog:
1. Cortex fires L2 thinking synapse: "I need a new capability"
2. Fires to NeuronFactory: `create_neuron { prompt, tools, verbs }`
3. NeuronFactory generates Roslyn script, registers in AgentRegistry
4. Cortex retries routing — finds the new specialist instantly
5. Next request of this type routes to the new specialist automatically

Cost: ~10ms grain write + ~200ms Roslyn compile. No silo restart. No code changes to Cortex.

### Changes to ino-new

| Component | Change |
|---|---|
| `NeuronGrain.FireAsync` | After timeline logging, forward synapse to target neuron's `HandleAsync(Synapse)` method on the target grain. Synchronous: source waits for target to finish before returning receipt. |
| `NeuronRegistryGrain` | Extend `Neuron` record with `SynapseSchema` field (C# interface source). Cortex reads this as its runtime neuron catalog. |
| Both registries | Cortex reads BOTH `NeuronRegistryGrain` (runtime L1 neurons from ino-new) and `AgentRegistryGrain` (compile-time typed agents from iaw/Core/Registry). Split-by-origin model per CLAUDE.md known-problem #7 option epsilon. |
| `BehaviorMemory` | Becomes load-bearing: Cortex embeds the user query, searches BehaviorMemory for matching `.feature` scenarios, includes top hits in the LLM prompt to inform specialist selection. |
| New: `CortexNeuron` | Grain that receives `user_message` synapses, calls LLM with unified catalog (both registries) + BehaviorMemory hits, fires to specialists, composes reply. |
| New: `NeuronFactory` | Specialist that creates L1 neurons from intent descriptions (deferred until after initial 5 specialists work). |

---

## 2. Specialist neurons (5 starters)

Each specialist is an L1-style neuron: system prompt + tool selection + handler logic. Each gets a Gherkin `.feature` BDD file. Every synapse+neuron combination forms testable behavior.

### ShellSpecialist

- **Verbs:** `execute` (in), `execute_result` (out)
- **Calls:** IShell (compile-time neuron)
- **Proves:** Cortex → specialist → compile-time neuron chain
- **Example:** "run npm test in the frontend project and tell me if it passes"

```gherkin
Feature: ShellSpecialist

  Scenario: Execute a command and interpret results
    Given a specialist "shell" is registered
    When the user says "run npm test in frontend"
    Then Cortex routes to "shell" with verb "execute"
    And ShellSpecialist fires "execute" to IShell with cmd "npm test" cwd "frontend"
    And the user receives a natural language interpretation of the result
    And the timeline contains the full synapse chain
```

### FileDeliverySpecialist

- **Verbs:** `handle` (in), `find_file` → FileSystem, `deliver_file` → Delivery, `done` (out)
- **Calls:** IFileSystem (compile-time), Delivery surface
- **Proves:** Multi-hop synapse chain, cross-surface delivery

```gherkin
Feature: FileDeliverySpecialist

  Scenario: User requests a file by name
    Given a specialist "file-delivery" is registered
    And a file "docs/Q1-report.pdf" exists
    When the user says "send me the quarterly report"
    Then Cortex routes to "file-delivery" with verb "handle"
    And a synapse "find_file" fires to FileSystem
    And a synapse "deliver_file" fires to Delivery
    And the user receives confirmation with the filename
    And the timeline contains 5 synapses for this interaction
```

### SchedulerSpecialist

- **Verbs:** `schedule` (in), `scheduled` (out), `reminder_due` (timer-triggered)
- **Calls:** Orleans reminders for time-triggered firing
- **Proves:** Persistence across silo restarts, time-triggered synapse firing

```gherkin
Feature: SchedulerSpecialist

  Scenario: Schedule a reminder
    Given a specialist "scheduler" is registered
    When the user says "remind me to review the PR tomorrow at 10am"
    Then Cortex routes to "scheduler" with verb "schedule"
    And SchedulerSpecialist persists the reminder as a synapse
    And the user receives confirmation with the scheduled time

  Scenario: Reminder fires at the scheduled time
    Given a reminder "review the PR" is scheduled
    When the scheduled time arrives
    Then a synapse "reminder_due" fires to Cortex
    And Cortex delivers the notification to the user's active surface
```

### RecallSpecialist

- **Verbs:** `recall_recent` (in), `recall_range` (in), `recall_result` (out)
- **Calls:** ITimelineReader (timeline grain)
- **Proves:** Synapse-as-memory, decay-filtered queries, access boost

```gherkin
Feature: RecallSpecialist

  Scenario: Recall recent activity
    Given a timeline with 20 events over the past hour
    When the user says "what happened while I was away?"
    Then Cortex routes to "recall" with verb "recall_recent"
    And RecallSpecialist queries timeline since last user activity
    And cold synapses accessed during recall are boosted to warm
    And the user receives a summary of recent events
```

### SummarizerSpecialist

- **Verbs:** `summarize` (in), `summary` (out)
- **Calls:** ITimelineReader + LLM
- **Proves:** Time travel as a user feature (not just dev tooling), LLM synthesis over synapses

```gherkin
Feature: SummarizerSpecialist

  Scenario: Summarize a time range
    Given a timeline with 50 events over the past week
    When the user says "what did we talk about last week?"
    Then Cortex routes to "summarizer" with verb "summarize"
    And SummarizerSpecialist queries timeline with decay >= 1 for the date range
    And the LLM synthesizes a natural language summary
    And the user receives the summary
```

---

## 3. Parallel universes

### Concept

What-if analysis over ino's own decision history. Fork from a timeline checkpoint, modify an event, replay forward, and compare the divergent outcome against the original. Like git branch but for runtime behavior. Naturally fits ino's event-sourcing architecture.

### Engine: UniverseGrain

- **Keyed by:** universe ID (e.g., `"universe-fork-t3-001"`)
- **Created by forking:** `ForkAsync(sourceTimeline: "global", checkpointSequence: 3, modifiedEvent: newSynapse)`
- **Contains:** its own `TimelineState` — seeded with events `[t=1..checkpoint]` from the source, with the modified event replacing the original at the fork point
- **Replay:** `ReplayAsync()` re-fires synapses from the fork point forward against a sandboxed grain environment. Each neuron activated in the universe gets scoped state.
- **Compare:** `CompareAsync(otherUniverseId)` returns a diff: which synapses diverged, which neurons activated differently, outcome comparison.

### Safety

Universes run in a sandboxed scope — they don't touch the real timeline, real file system, or real delivery surfaces. Specialist neurons in a universe use mock surfaces. Replay is read-only from the real system's perspective.

### Interface

```csharp
public interface IUniverse : IGrainWithStringKey
{
    Task ForkAsync(string sourceTimeline, long checkpointSequence, Synapse modifiedEvent);
    Task<ReplayResult> ReplayAsync();
    Task<UniverseDiff> CompareAsync(string otherUniverseId);
    Task<IReadOnlyList<TimelineEvent>> GetTimelineAsync();
}
```

### BDD

```gherkin
Feature: Parallel Universes

  Scenario: Fork timeline and replay with modified event
    Given a timeline with events:
      | seq | verb          | source | target |
      | 1   | activated     | alpha  | -      |
      | 2   | activated     | beta   | -      |
      | 3   | greet         | alpha  | beta   |
      | 4   | greet_reply   | beta   | alpha  |
      | 5   | find_file     | alpha  | fs     |
    When I fork at sequence 3 changing verb "greet" to "request_file"
    Then a new universe is created with events 1 through 3
    And the event at sequence 3 has verb "request_file" instead of "greet"
    When the universe replays from sequence 3
    Then the universe timeline diverges from the original
    And the original timeline is unchanged

  Scenario: Compare two universes
    Given universe "A" with the original timeline
    And universe "B" forked at sequence 3 with a modified event
    When I compare universe A with universe B
    Then the diff shows which synapses diverged after the fork point
    And the diff shows different neurons activated in each universe
```

---

## 4. Infrastructure changes

### Synapse delivery in FireAsync

- **Current:** log to timeline, return receipt
- **New:** log to timeline → forward to target neuron's `HandleAsync(Synapse)` → return receipt with handler result
- Synchronous delivery for v1. No inbox queue needed yet — add later if latency becomes a problem.

### Aspire stream tee for ino-windows

The current `FreeConsole`/`AllocConsole` fix disconnects ino-windows from Aspire's dashboard logging entirely. Replace with a `TeeTextWriter`:

1. Save original redirected stdout/stderr streams (pipe handles from DCP) before `FreeConsole`
2. `FreeConsole()` + `AllocConsole()` for the visible console window
3. Create `TeeTextWriter` that writes to both the new console AND the saved Aspire pipes
4. Dashboard keeps getting all logs, user gets interactive REPL

### Console log cleanup

Suppress Orleans connection noise on startup. Replace with a clean branded startup sequence:
- `ino` title
- Brief connection status (one line, not 30 lines of Orleans config)
- Welcome message once connected

---

## 5. TUI shell (Phase 4 — after engine works)

### hex1b upgrade

Upgrade from 0.1.0 to latest stable. Verify widget constructor API, interactive runtime, and presentation adapters via Context7 before upgrading. The widget tree record API (`VStackWidget`, `TextBlockWidget`, `BorderWidget`) is stable; the interactive runtime surface needs verification.

### Tab bar

Four views, bottom of screen: `[Chat] [Timeline] [Universes] [Dev]`

- **Chat** — natural language input, ino replies via Cortex. Neurons/synapses invisible to the user. Welcome screen with branding on first launch. Warm assistant persona.
- **Timeline** — interactive time travel scrubber built on existing `TimelineView` widget tree. Scroll through events, see neuron activations, synapse chains with verb/payload details.
- **Universes** — fork/compare UI. Pick a checkpoint, modify an event, replay, see divergent timelines side-by-side.
- **Dev** — raw command shell (current `ino>` REPL), neuron list, synapse graph. Creator/developer mode.

### Cross-platform rendering

Same hex1b widget tree renders everywhere via adapters:
- `ConsolePresentationAdapter` — Windows/macOS/Linux terminal
- `WebSocketPresentationAdapter` — Telegram mini-app webview (replaces current `index.html`)
- `HeadlessPresentationAdapter` — tests (in-memory, no console I/O)

---

## 6. Build order (Approach A — engine first)

| Phase | Deliverable | Test strategy |
|---|---|---|
| **1: Cortex + synapse delivery** | CortexNeuron routes user messages to specialists via LLM. FireAsync delivers synapses. BehaviorMemory integrated into routing. | BDD: Cortex.feature — routing scenarios. Unit: FireAsync delivery. |
| **2: 5 specialist neurons** | Shell, FileDelivery, Scheduler, Recall, Summarizer. Full Cortex→specialist→result chains working. | BDD: one .feature per specialist (5 files). Each scenario exercises a complete synapse chain. |
| **3: Parallel universes engine** | UniverseGrain: fork, replay, compare. Sandboxed execution. | BDD: ParallelUniverses.feature — fork, replay, compare, safety scenarios. |
| **4: TUI shell** | hex1b upgrade, tab bar, 4 views (Chat, Timeline, Universes, Dev), Aspire stream tee, welcome/persona, console log cleanup. | Widget tree rendering tests (existing pattern from Timetravel.Tui). |
| **5: Telegram convergence** | WebSocketPresentationAdapter serves same widget tree to Telegram mini-app. Replace current index.html. | E2E: same scenarios work on both terminal and Telegram surfaces. |

---

## Architecture diagram

```
User Input (any surface)
    │
    ▼
┌─────────┐  user_message   ┌────────────────┐
│ Surface  │ ──────────────► │     Cortex     │
│ (TUI /   │                 │  (LLM router)  │
│ Telegram)│ ◄────────────── │                │
└─────────┘    reply         └───────┬────────┘
                                     │ reads catalog + memory
                              ┌──────┴──────┐
                              ▼             ▼
                     ┌─────────────┐ ┌──────────────┐
                     │ AgentRegistry│ │BehaviorMemory│
                     │  (catalog)  │ │ (vector search│
                     └─────────────┘ └──────────────┘
                              │
                    ┌─────────┼──────────┐
                    ▼         ▼          ▼
              ┌──────────┐ ┌────────┐ ┌──────────┐
              │  Shell   │ │  File  │ │Scheduler │ ...
              │Specialist│ │Delivery│ │Specialist│
              └────┬─────┘ └───┬────┘ └────┬─────┘
                   │           │           │
                   ▼           ▼           ▼
              ┌────────┐ ┌──────────┐ ┌─────────┐
              │ IShell │ │IFileSystem│ │ Orleans │
              │(compile│ │(compile  │ │Reminders│
              │ -time) │ │ -time)   │ │         │
              └────────┘ └──────────┘ └─────────┘

Every arrow is a Synapse (verb + payload + decay).
Every Synapse persists on the Timeline.
Timeline IS the memory. No separate store.
Parallel Universes fork the Timeline and replay.
```
