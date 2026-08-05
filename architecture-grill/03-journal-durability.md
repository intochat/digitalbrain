# 03 · Journal durability — grill + ratification

Date: 2026-08-05. Status: **RATIFIED** for durable structures, turn pipeline, forbidden
patterns. Inputs: live Core sources (`Neuron.cs`, `NeuronJournal.cs`, `Neuron.Dispatch.cs`,
`DeliveryPolicy.cs`, `Brain.cs`, hosting DI gate), product v1 kernel
(`Neuron.Turns.cs`, `Neuron.Outbox.cs`, `NeuronFeed`, capability mid-turn commit),
`CORE-DESIGN.md` §4/§7/§9, `CORE-ARCHITECTURE.md` delivery physics, restart/redelivery
tests. Method: claim → strongest attack → defend or fold → ratify.

This document freezes **how a turn becomes durable truth** and **how that truth becomes
delivery**. It does not re-litigate topology, Abstractions thickness, or ask typing.

---

## 1 · The problem in one sentence

A neuron must accept a fact, run module code that may stage more facts and touch state,
commit **once**, survive crash, redeliver without dual truth, and never hang itself by
awaiting its own delivery chain inside the emitting turn.

---

## 2 · Claims under grill

### C1 · Journal-as-outbox (one payload store)

**Claim:** Said journal entries *are* the outbox. Body + receiver snapshot live on the
said line; a lazy progress map holds only `(pending receivers, attempts)`. No separate
`IDurableList<byte[]>` payload outbox.

**Attack:** Separate outbox is simpler for partial settlement and for "don't re-read
journal on every drain." Payload duplication is cheap storage.

**Defense:**
- Dual payload is dual truth under partial commit: journal said "I said F to R" while
  outbox still holds F, or the reverse after crash mid-flush. Product v1 paid that tax
  (`_outgoing` + `_outbox` + mid-turn capability commits).
- Dispatch-from-journal makes **first delivery, redelivery, and self-delivery ship the
  same bytes** (`journal = wire`). No second serializer path for "what we meant to send."
- Progress is sparse: absence means "untouched / full snapshot pending." Settled rows
  are empty-pending until the cursor advances and clears them.

**Fold?** No. Journal-as-outbox stands.

**Ratified:** one journal; lazy `outbox.progress`; durable `outbox.cursor` = smallest
unsettled said position (0 until first said).

---

### C2 · At-least-once + receiver watermark (no dual truth on reception)

**Claim:** Delivery is at-least-once. Dedup identity is `(Source, Sequence)` on a
per-source watermark. Duplicate → silent success ack (never throw). Sequence is the
emitter's said-entry position (1-based, never restarts).

**Attack A:** GUID `SynapseId` is clearer and independent of journal layout.  
**Defense:** GUIDs are a second identity; `(Source, Sequence)` is the journal line. Human
readable, compaction-stable, no mint table.

**Attack B:** 4096-entry handled window (product v1) bounds memory without watermarks.  
**Defense:** Window eviction is a **bug class**: when the window is full, `Remember`
evicts; a thrown turn that only truncates to "checkpoint count" leaves the delivery marked
handled while the turn was discarded — outbox redelivery is swallowed forever
(`Neuron.Turns.cs` ForgetHandled / `_evictedWhileHandling`). Watermark has no eviction of
identity; prune only by age past `RetryHorizon + slack` (safe: no attempt outlives the
horizon). Capacity under fan-in does not silently re-accept committed sequences.

**Attack C:** Duplicate should error so operators see it.  
**Defense:** A throw on redelivery mints **false terminal** records (`DeliveryFailed`) for
facts the receiver already handled — dual truth on the sender. Silent success is the only
ack that keeps sender progress honest.

**Fold?** No. Watermark + silent ack stands.

---

### C3 · Post-handler staging + poison (delete retraction)

**Claim:** During the handler, durable structures are **not** mutated. Staging is
in-memory (encoded emissions, schedule changes, lazy `TState` working copy). After the
handler returns, Core stages the batch into `IDurable*` then one `WriteStateAsync`. Any
commit failure **poisons** the activation and deactivates; next activation reloads
committed truth. No compensation commit, no checkpoint/restore of durable lists.

**Attack:** Product v1 already has turn checkpoints, feed restore, outbox discard,
`CommitRetractionAsync`, capability mid-turn protect — proven under load; keep it.

**Defense (why v1 needed retraction):**
| v1 mechanism | Why it existed |
|---|---|
| `TurnCheckpoint` / `NeuronFeedCheckpoint` | Feeds and outbox were mutated **during** the turn (append on emit, remember on handle) |
| `Discard` / `Restore` | Handler throw must undo list appends already in durable structures |
| `ForgetHandled` + `_evictedWhileHandling` | Dedup window was a set that could evict mid-turn; rollback needed arithmetic |
| `StageInboundCause` / mid-turn capability commit | Capability path committed **inbound** before work finished, so retraction had to spare the request while undoing the cause |
| `CommitRetractionAsync` swallows failure | Retraction commit can fail; telemetry only — **ambiguous durable state** |

Post-handler staging deletes every consumer of that machinery:
- Handler throw → clear in-memory turn → rethrow → **zero durable trace**.
- Commit fail → in-memory mutations never became committed truth (Orleans.Journaling load
  on next activation) **or** the write landed and the ack was lost — poison+reload is
  correct for **both** (retrying a "successful but unacked" commit against a live
  activation with half-staged memory is worse).
- Committed-only read marker (`LastCommitted` / `MarkCommitted`) ensures edge reads and
  drain never observe uncommitted staging Orleans exposes in-memory.

**Attack:** Poison is harsh; soft retry of the same activation is enough.  
**Defense:** After a failed `WriteStateAsync`, in-memory durable structures and storage
disagree. Continuing on that activation is dual truth by definition. Fresh activation =
committed snapshot only.

**Fold?** No. Post-handler staging + poison stands. **v1 retraction is deleted by
construction**, not ported.

---

### C4 · Never Drain-await-Deliver inside the emitting handler

**Claim:** After commit, delivery runs in **separate** serialized turns (grain timer /
reminder → `DrainAsync`). The emitting handler never awaits remote `Deliver`.
`NeuronConcurrency.RequireSerializedTurns` forbids reentrancy.

**Attack:** Await drain in the same turn for "sync" edge latency / simpler tests.  
**Defense:** Documented **reentrancy deadlock**: drain awaits `Deliver` on the receiver;
if that chain re-enters the still-open emitter turn, the grain serializes and hangs.
Product trap (CLAUDE.md): facts flow one way; answers return as later directed turns.

Same-turn reply **ride-back** is a second delivery path that bypasses the outbox and
breaks per-(sender,receiver) FIFO the watermark stands on (grill h2 FATAL). Edge
`AskAsync` fires once, then **polls the session journal**.

**Fold?** No. Commit-then-dispatch is physics.

---

### C5 · FIFO per receiver + abandonment barrier

**Claim:** One drain pass uses `blockedTargets`: if receiver R fails transiently on said
position N, later said positions that also target R wait in that pass. Progress rewrites
in place (never re-append). On exhaustion or terminal refusal: stage `DeliveryFailed`,
**commit the hole**, then unblock R. Crash must never jump a hole that only existed in
memory.

**Attack:** Parallel deliver all pending for throughput.  
**Defense:** Parallel breaks FIFO → watermark on the receiver can advance on seq 5 while
seq 4 is still in flight; a crash then redelivers 4 after 5 was handled → either
duplicate side effects (if 4 accepted) or silent loss of ordering assumptions modules
rely on. The four legs stay: (1) serialized drains, (2) sequential awaited attempts per
receiver in a position, (3) rewrite-in-place, (4) blocked-stays-blocked.

**Attack:** Don't journal `DeliveryFailed`; just drop and advance cursor.  
**Defense:** Physics: never silent loss. Terminal records live on the **sender** journal.

**Fold?** No. FIFO + barrier stands.

---

### C6 · Horizons, depth, compaction floor

**Claim (bounds, one home `DeliveryPolicy`):**

| Bound | Value | Job |
|---|---|---|
| MaximumAttempts | 1000 | Attempt ceiling |
| RetryHorizon | 30 min | Age of said entry; exhaust |
| DeliveryAttemptTimeout | 30 s | Per-attempt cancel bound (reminders need a real token) |
| RetryInterval | 50 ms | In-activation drain timer |
| WakeupCadence | 1 min | Reminder backstop |
| AskHorizon | 2 × RetryHorizon | Ask pin expiry → `AskExpired` |
| WatermarkRetention | RetryHorizon + 5 min | Safe watermark prune |
| MaxRetainedEntries / Bytes | 512 / 512 KiB | Soft compaction targets |
| ScheduleFailureLimit | 5 | Tick failures → `ScheduleFailed` + unschedule |

**Depth:** Product v1 carried hop depth (max 16) on outbox entries to kill cycles. Core
v2 **does not** reintroduce hop-depth as delivery identity. Cycle control is structural:
emitter never fans to self on declaration path; schedules tick as self-sourced ordinary
turns without watermark; open-ask backpressure refuses concurrent same-kind asks.
Unbounded graph chatter is bounded by **retry horizon + attempts**, not a hop counter
that modules can game with `EmitAtDepthAsync`.

**Compaction:** Floor is **hard**:
`min(cursor, oldest ask pin, floorLimit)`. Soft targets only pull **below** the floor.
Never v1 `NeuronFeed.Compact` unconditional head eviction (which could drop unsettled
outbox-relevant lines only because feeds were separate from outbox). Tallies outlive
eviction; `ResetSnapshot` resynchronizes readers who fell off the window.

**Attack:** Pin journal until every ask answers (unbounded floor).  
**Defense:** One lost reply pins storage forever — infinite-silent-retry disease moved
into disk. Bounded `AskHorizon` + journaled `AskExpired` instead.

**Fold?** Soft targets and horizons stand. Hop-depth as wire metadata stays deleted.

---

### C7 · Arm wakeup before commit

**Claim:** If the batch is deliverable (or has ask pins / schedules), arm the durable
reminder **before** `WriteStateAsync`. Post-commit crash still wakes the neuron.
Armed-but-uncommitted is a benign leak that self-disarms when idle (v1
`Neuron.Outbox.cs` ordering, ported).

**Attack:** Arm after commit only — cleaner.  
**Defense:** Crash between commit and arm → idle neuron with unsettled outbox until
accidental reactivation. Reminder is the survival of delivery under deactivation.

**Fold?** No.

---

### C8 · Module surface: read shapes only; no raw `IDurable*`

**Claim:** Modules and edges see:

| Can | Cannot |
|---|---|
| `Brain.ReadAsync(NeuronId, afterPosition)` → `NeuronReading` | Resolve keyed `IDurableList` / `Dictionary` / `Value` |
| `JournalFact` lines (position, entry, kind, metadata, to, body) | Mutate journal, cursor, progress, watermarks |
| Connections snapshot beside the journal | Open second durable keys under the grain |
| `ReadStateAsync` committed `JsonElement` / `Neuron<TState>` working copy in turn | `WriteStateAsync`, `GrainFactory`, `DeactivateOnIdle` |
| Verbs `Emit` / `Ask` / `Schedule` / `Reply` (in turn only) | Mid-turn commits, raw timers, `IRemindable` |

**Enforcement:**
- Shadowed `[Obsolete(error: true)]` on `WriteStateAsync`, `GrainFactory`,
  `DeactivateOnIdle`.
- DI gatekeeper on `IJournaledStateManager.RegisterState`: only `NeuronJournal.CoreKeys`.
- Reads serve **committed** markers only (`LastCommitted`, `CommittedState`,
  `CommittedConnections`).

**Attack:** Modules need custom durable lists for "performance."  
**Defense:** Unenlisted durable mutation is the atomicity hole. Module state = `TState`
slot committed in the same batch. External stores (vector DB, blob) are IO inside the
turn, not Orleans journal keys.

**Fold?** No.

---

## 3 · RATIFIED — durable structures list

All Core-owned. One grain, one batch commit. Keys are the complete set the DI gate
allows (`NeuronJournal.CoreKeys`).

| Key | Type | Role |
|---|---|---|
| `journal` | `IDurableList<JournalEntry>` | Append-only heard/said lines; **the outbox payload** |
| `journal.sequence` | `IDurableValue<long>` | Last minted seq (1-based; survives compaction) |
| `outbox.cursor` | `IDurableValue<long>` | Smallest unsettled said position; compaction floor input |
| `outbox.progress` | `IDurableDictionary<string, DeliveryProgress>` | Lazy per-said-position pending + attempts |
| `asks` | `IDurableDictionary<string, DateTimeOffset>` | Ask pins (asker side) for horizon + compaction floor |
| `asks.open` | `IDurableDictionary<string, SynapseRefEntry>` | Answerer-side open asks (at most one per question kind) |
| `dedup` | `IDurableDictionary<string, WatermarkEntry>` | Per-source watermark `(Seq, Touched)` |
| `connections` | `IDurableDictionary<string, NeuronIdEntry[]>` | Emitter connection table by fact kind |
| `schedule` | `IDurableDictionary<string, ScheduleEntry>` | Core schedule table |
| `tallies.heard` | `IDurableDictionary<string, long>` | Heard tallies by fact kind (outlive compaction) |
| `tallies.said` | `IDurableDictionary<string, long>` | Said tallies by fact kind |
| `state` | `IDurableValue<JsonElement>` | Module `TState` slot (codec-encoded) |

**Closed journal element types** (Orleans.Journaling / `JournalJsonContext` only — no
module CLR types):

- `JournalEntry`, `SynapseRefEntry`, `NeuronIdEntry`, `ScheduleEntry`,
  `DeliveryProgress`, `WatermarkEntry`, primitives, `JsonElement` bodies.

**Volatile per-activation (not durable):**
- `poisoned` flag, open `Turn`, unsettled said index, rehydration cache, drain timer
  handle, wakeup-armed flag, schedule grain timers.

**Not durable structures (forbidden as second truth):**
- Separate payload outbox list.
- GUID delivery identity tables.
- Fixed-size handled-id windows.
- Module-minted `IDurable*` keys.
- Streams as causal bus.

---

## 4 · RATIFIED — turn pipeline (numbered steps)

Receiver side. Self-delivery enters at step 1 via **direct method call**, never the grain
proxy. One journal, one sequence: every heard or said entry consumes a position; a said
entry's position **is** `SynapseRef.Sequence`.

### Reception → commit

1. **`Deliver` arrives.** If activation is **poisoned**, throw — sender retries a fresh
   activation.
2. **Dedup.** If `metadata.Sequence <= watermark[metadata.Source]`, return success
   silently (duplicate ack). Never error.
3. **Reserved-kind interception / answer routes** (Connect, Disconnect, Schedule,
   Unschedule, Answers-targeted reply, answerer open-ask backpressure) run in Core before
   ordinary module dispatch where applicable.
4. **Open the turn.** Ambient envelope is Core-internal. `TState` materializes lazily on
   first `State` access from **committed** JSON. **No durable structure is mutated.**
5. **Run the handler** with the delivery's `CancellationToken`. `Emit` / `Ask` /
   `Schedule` eagerly encode into **in-memory** staged entries only. Unserializable types
   throw here (author's turn).
6. **Handler throws** (including cancellation): discard staging + working copy, clear
   turn, rethrow. **Zero durable trace.** Sender outbox retries; on exhaustion sender
   journals `DeliveryFailed`.
7. **Stage the batch (in memory on durable structures, not yet committed):**
   1. Heard entry (body + From + Cause/Answers copy),
   2. Said entries: receiver snapshot resolved **now** (declared ∪ connected, ghost rule,
      via provenance; ask route frozen),
   3. State slot if `State` was touched,
   4. `watermark[Source] = Sequence`,
   5. Ask pins / open-ask table / unpin on answer,
   6. Schedule table mutations (+ zero-receiver said records for Schedule/Unschedule),
   7. Answer emission **last** (stamped `Answers` when applicable).
8. **Arm durable reminder wakeup** if deliverable or pins/schedules require it (**before**
   the write).
9. **ONE `WriteStateAsync(CancellationToken.None)`.** On any failure: **poison**,
   `DeactivateOnIdle`, rethrow. No retraction commit.
10. **Success:** `MarkCommitted` (advance committed read/dispatch watermark), index new
    unsettled said positions, schedule fast drain timer, return ack to sender.

### Dispatch (separate turns)

11. **Drain** (timer 50 ms / reminder 1 min): poison-guard; expire asks; iterate unsettled
    said in order; rehydrate fact **from journal bytes**; per-receiver FIFO via
    `blockedTargets`; lazy progress rewrite; terminal / exhaustion → stage
    `DeliveryFailed`, **commit hole before unblocking receiver** (abandonment barrier);
    advance cursor; compact only under hard floor; disarm wakeup when idle.
12. **Activation:** load durable state; `MarkCommitted` from storage; rebuild unsettled
    index from `cursor..lastCommitted`; re-arm schedule timers; arm wakeup + drain if
    work remains.

### Commit sites (all poison on failure)

- Module turn commit (`CommitTurnAsync` → `CommitCoreBatchAsync`)
- Drain progress / abandonment / cursor commits
- Ask expiry commits
- Core reserved-kind reception commits
- Terminal-unhandled reception commit (then throw `UnhandledFactException` for sender)

---

## 5 · RATIFIED — forbidden patterns

These are **structural bans**, not style. A PR that reintroduces one fails review even if
tests are green for the happy path.

| # | Forbidden | Why |
|---|---|---|
| F1 | Mutate `IDurable*` / journal / watermark **during** the handler | Forces retraction; dual truth on throw |
| F2 | Module-visible `WriteStateAsync`, raw keyed `IDurable*`, extra grain interfaces, `IRemindable` | Unenlisted durable mutation / second bus |
| F3 | Separate payload outbox that duplicates said bodies | Dual truth under partial commit |
| F4 | Fixed-size handled-id / GUID dedup window with eviction | Eviction-retraction bug class; capacity silent re-accept |
| F5 | Throw on duplicate delivery | False `DeliveryFailed` / dual truth |
| F6 | Await `Drain` / remote `Deliver` **inside** the emitting handler | Reentrancy deadlock; FIFO break |
| F7 | Same-turn reply ride-back as correctness path | Second delivery route bypasses outbox |
| F8 | Advance outbox cursor / unblock receiver past a hole that is not **committed** | Crash resurrects gap behind watermark |
| F9 | Infinite silent retry (no horizon, no journaled terminal) | Physics violation |
| F10 | Compaction below `min(cursor, oldest ask pin)` or unconditional feed eviction | Drop unsettled truth / pin-forever disease if inverted badly |
| F11 | Serve uncommitted journal/state to `ReadAsync` / drain | Orleans in-memory exposure becomes dual truth |
| F12 | Continue work on a **poisoned** activation | Memory ≠ storage |
| F13 | Streams / pub-sub / registries as neuron↔neuron authoritative delivery | Late-join loss; dual bus |
| F14 | `System.Type` / AQN / GUID as durable delivery identity | Journals outlive code; identity must be structural strings + seq |
| F15 | Soft-fail retraction commits ("best effort undo") | Ambiguous durable state (v1 `CommitRetractionAsync` swallow) |
| F16 | Neuron-awaits-neuron RPC veneer | Occupies single-threaded turn; deadlocks under fan-out |
| F17 | Arm wakeup **only** after successful commit when deliverables exist | Lost wakeup after post-commit crash |

---

## 6 · Product v1 retraction vs Core post-handler staging

### Side-by-side

| Concern | Product v1 | Core (ratified) |
|---|---|---|
| When durable mutates | During turn (feeds, outbox, handled set) | **After** handler, before one write |
| Handler throw | Checkpoint restore, Discard, ForgetHandled, optional mid-turn protect | Clear memory only |
| Commit failure | Partial; retraction path tries another write (may fail silently) | Poison + deactivate + reload |
| Dedup | 4096 GUID window + eviction dance | Per-source watermark; age prune only |
| Outbox | Separate `IDurableList<byte[]>` + outgoing feed | Journal said lines + lazy progress |
| Journals | Two feeds (in/out), Orleans body serializer | One interleaved JSON journal |
| Terminal failure | Telemetry abandon / settled attribute | Journaled `DeliveryFailed` on **sender** |
| Capability special case | Mid-turn inbound commit + asymmetric retraction | Ordinary facts; Stage-2 external effects later |
| Compaction | Unconditional soft bounds on each feed | Floor-subordinate soft bounds; pins + cursor |
| Depth | Hop counter max 16 on outbox entry | Structural + horizon (no hop identity) |

### Why the delete is safe

Every v1 retraction consumer exists **only** because staging was mid-handler and dedup
could evict. Remove those two premises and the machinery has **zero** remaining job.
Poison handles the only residual case retraction never handled correctly: **ambiguous
commit** (write may have landed).

Proof surface already in tree:
- `RestartSurvivalTests.SurvivesDeactivation` — journals/watermarks across deactivate.
- `RestartSurvivalTests.RedeliveredEmissionDoesNotDuplicate` — commit fault + redelivery +
  watermark; no `DeliveryFailed` when delivery eventually settles; FIFO via third day.

---

## 7 · What modules read — shapes

### Edge / introspection / tests

```text
Brain.ReadAsync(neuron, afterPosition)
  → NeuronReading
       Journal: IReadOnlyList<JournalFact>
         Position, Entry ("heard"|"said"), Kind,
         Metadata (Source, Sequence, Timestamp [, Cause/Answers if ABI exposes via entry projection]),
         To?: IReadOnlyList<Delivery(Receiver, Via)>,
         Body?: Synapse  // null if kind not in running catalog
       Connections: IReadOnlyDictionary<string, IReadOnlyList<NeuronId>>

ITransport.ReadStateAsync()
  → JsonElement  // committed TState only
```

Cursor semantics (v1 `NeuronFeed.Read` port, committed-only):
- `afterPosition` inside retained window → delta after it.
- Fell off or ran ahead → empty delta + reset tallies snapshot path for resync
  (implementation may surface reset via read contract evolution; tallies outlive
  compaction either way).

### What modules must not assume

- Journal position density (heard and said interleave; per-receiver sequences are sparse).
- Body always deserializes (unloaded modules → `Body: null`, still a line).
- That an emission is delivered when `Emit` returns (returns after **commit**, not delivery).
- That `TState` is the audit log (journals are; state is a consequence).

---

## 8 · Restart / redelivery matrix (must hold)

| Event | Outcome |
|---|---|
| Crash before commit | No journal line, no watermark advance, no progress; redelivery re-runs handler |
| Crash after commit, before drain | Reminder/timer delivers; journal rehydrates payload |
| Crash after successful Deliver, before sender progress commit | Redelivery; receiver watermark swallows |
| Commit ack lost (write may have landed) | Poison; fresh activation; sender retries; watermark if already applied |
| Handler throw | No durable change; redelivery |
| Transient Deliver failure | Pending stays; blockedTargets FIFO; retry until horizon/attempts |
| Permanent (no kind / unhandled) | Terminal attempt 1; sender `DeliveryFailed`; receiver may journal unhandled reception |
| Compaction | Never drops below cursor/ask floor; tallies remain; readers reset if cursor fell off |
| Idle with unsettled outbox | Reminder wakeup → `DrainAsync` |

---

## 9 · Self-grill log (quick)

| # | Attack | Verdict |
|---|---|---|
| G1 | Keep v1 retraction "just in case" | **Reject** — no consumer under post-handler staging; swallow path is dual truth |
| G2 | Separate outbox for speed | **Reject** — dual payload under partial commit |
| G3 | Window dedup + watermark hybrid | **Reject** — inherits both bug classes |
| G4 | Drain inside Emit for latency | **Reject** — deadlock + FIFO break (FATAL) |
| G5 | Ride-back reply | **Reject** — second path (FATAL) |
| G6 | Module `IDurableList` for big blobs | **Reject** — atomicity hole; use external IO + TState pointer |
| G7 | Unbounded ask pin floor | **Reject** — storage pin forever |
| G8 | Parallel multi-receiver without FIFO | **Reject** — ordering/watermark premises collapse |
| G9 | Soft-continue after failed WriteStateAsync | **Reject** — poison only |
| G10 | Journal terminal only on receiver | **Reject** — sender owns delivery truth (`DeliveryFailed`) |

---

## 10 · Implementation map (don't re-derive)

| File | Owns |
|---|---|
| `NeuronJournal.cs` | Structures, append, read, watermark, compact, pins |
| `JournalEntry.cs` | Closed schema + `JournalJsonContext` |
| `Neuron.cs` | Turn open/stage/commit/poison, verbs, watermark on reception |
| `Neuron.Dispatch.cs` | Drain, FIFO, barrier, cursor, wakeup arm order |
| `OutboxWakeup.cs` | Reminder backstop grain |
| `DeliveryPolicy.cs` | All bounds |
| `Neuron.Transport.cs` | Deliver / Read committed-only |
| `Hosting/DigitalBrainSiloExtensions.cs` | CoreKeys gate |
| `Brain.cs` | Edge session fire-once + journal poll |

---

## 11 · Ratification stamp

**RATIFIED 2026-08-05:**

1. **Durable structures** — the table in §3 is the complete Core key set; journal-as-outbox;
   no module keys.
2. **Turn pipeline** — the numbered steps in §4 (commit-before-dispatch, post-handler
   staging, poison on commit failure, abandonment barrier).
3. **Forbidden patterns** — the table in §5 (especially F1–F8, F11–F12, F15–F17).

Any design change that edits those three sections requires a new grill document, not a
silent patch.
)
