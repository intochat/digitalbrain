# DigitalBrain Core — the design

Working document, 2026-08-05. The Core of DigitalBrain OS as a disciplined composition of
the proven prototype capabilities. Inputs: CORE-RESEARCH.md, OS.md, FLOWS.md, CORE-PROMPT.md;
two evidence sweeps over all eight prototypes plus the v1 kernel (10 readers, file:line
dossiers); a nine-adversary grill panel over the four open problems plus a flows writer that
expressed all ten FLOWS.md flows and the north-star in the final vocabulary. Every
design-deciding claim below traces to code that ran or a probe that compiled; the dossiers
live in the session archive, the load-bearing citations are inline.

---

## 1 · The physics — the numbered contract

1. **Nothing leaves a neuron before its turn commits.** The return of a delivery means
   exactly "committed", never the answer. There is no same-turn reply ride-back — the
   ride-back was killed because it is a second delivery route that bypasses the outbox and
   breaks the per-(sender,receiver) FIFO the watermark dedup stands on (grill h2, FATAL).
2. **Neurons never await neurons.** Continuations are declared handlers
   (`INeuron<Answer<Q,R>>`), durable and journal-visible. A turn may await its own IO.
3. **Journals are the only truth about communication.** Append-only, human/model-readable
   JSON lines, fact bodies included, no `System.Type`/AQN/GUIDs anywhere durable. Module
   state is a Core-committed *consequence* of journaled turns — readable
   (`ReadStateAsync`), never authoritative for what happened. A retried turn re-executes
   its own IO; the journal records committed turns, not attempts (the capability seam,
   Stage 2, is what will journal external effects).
4. **Delivery is at-least-once with receiver-side dedup; failure is terminal and
   journaled.** Dedup = per-source watermark on `(Source, Sequence)`. Every failed
   delivery ends in a journaled `DeliveryFailed` on the **sender** — never silent loss,
   never infinite silent retry. Permanent conditions (no such kind, no declared handler)
   settle on the **first** attempt; only transient conditions burn the bounded retry.
5. **`NeuronId(Kind: string, Name: string)`.** Kind = lowercased class name, minted by one
   convention function used everywhere. Kind collisions fail boot loudly. (The uncommitted
   `NeuronId(Type, Name)` on disk must revert: the journal format cannot serialize
   `System.Type` — proven — and physics bans AQN in durable data.)
6. **Core mints all durable metadata before journaling.** `RequestContext` is transport
   convenience only. Sequences are 1-based, survive compaction, and never restart —
   deleting one neuron's storage without resetting the brain is corruption, not cleanup.
7. **A neuron's journal has exactly one writer.** Journal storage must fence a stale
   activation's commit (ETag/conditional append). Watermark dedup identity and journal
   integrity both stand on this; the production provider must be verified and the test
   provider must fence identically.
8. **UI is a module; a widget's action IS a synapse; renderers are listeners.**
9. **Never fake a proof.** No stub gates, no synthetic observations, no "durable" names on
   volatile things. Boot refusals are themselves tested contracts (the failure message is
   asserted).
10. **Journaled vocabularies are append-only** — including Core's own (`Connect`,
    `DeliveryFailed`, …) and every module's `TState` shape.

---

## 2 · The ABI — DigitalBrain.Abstractions, complete

No Orleans. No dependencies. Namespace `DigitalBrain`. Every line has a named consumer.

### Synapse.cs

```csharp
namespace DigitalBrain;

public abstract record Synapse;

// A question carries its reply type: compile-time ask/answer pairing, edge AskAsync
// inference, and the boot answerer-cardinality check all hang on this one declaration.
public abstract record Synapse<TReply> : Synapse
    where TReply : Synapse;
```

### NeuronId.cs

```csharp
namespace DigitalBrain;

public readonly record struct NeuronId(string Kind, string Name)
{
    // The one minting convention. The boot catalog, journal entries, grain addresses and
    // test sugar all call this — one derivation, no second truth source.
    public static string KindOf(Type type) => type.Name.ToLowerInvariant();

    public override string ToString() => $"{Kind}/{Name}";
}
```

### SynapseRef.cs

```csharp
namespace DigitalBrain;

// Identity of a journaled fact: who said it, at which position of their journal.
// This is the dedup key, the causation reference and the answer reference — no GUIDs.
public readonly record struct SynapseRef(NeuronId Source, long Sequence);
```

### SynapseMetadata.cs

```csharp
namespace DigitalBrain;

// The envelope. Cause = the fact whose turn produced this one (null for edge- or
// timer-born facts). Answers = Core-stamped reference to the ask this fact answers;
// modules never set it, continuation and edge matching key on it, never on Cause-scanning.
public sealed record SynapseMetadata(
    NeuronId Source,
    long Sequence,
    DateTimeOffset Timestamp,
    SynapseRef? Cause,
    SynapseRef? Answers);
```

### INeuron.cs

```csharp
namespace DigitalBrain;

// Listener: hearing IS the behavior. The default implementations make the bodiless
// declaration-only listener legal (`class Diary : Neuron, INeuron<DayPlanned>;`) and give
// synchronous handlers a zero-ceremony surface: override void Hear for verb-only turns,
// override Task HandleAsync when the turn awaits its own IO. Core always invokes
// HandleAsync; the pair is one delivery path with two author surfaces.
public interface INeuron<in TFact>
    where TFact : Synapse
{
    Task HandleAsync(TFact fact, CancellationToken cancellationToken)
    {
        Hear(fact);
        return Task.CompletedTask;
    }

    void Hear(TFact fact)
    {
    }
}

// Answerer: at most ONE kind per question type across the composition (two+ fails boot).
// Return the reply to answer this turn; return null to defer — Core keeps the ask durably
// open and the neuron's later emission of a TReply-typed fact closes it (multi-turn
// answers: chat with tools, approvals, fan-out — the OS's flagship flows).
public interface INeuron<in TQuestion, TReply>
    where TQuestion : Synapse<TReply>
    where TReply : Synapse
{
    Task<TReply?> HandleAsync(TQuestion question, CancellationToken cancellationToken)
        => Task.FromResult(Answer(question));

    TReply? Answer(TQuestion question) => null;
}
```

A bodiless listener (`class Diary : Neuron, INeuron<DayPlanned>;`) is legal and meaningful:
journaling the reception IS the behavior. An **answerer** kind that overrides neither
member fails boot (`GetInterfaceMap` in the catalog build — a never-overridden
`Answer() => null` would defer every ask forever, a dead claim). A kind declaring both
`INeuron<TQ>` and `INeuron<TQ,TR>` for the same `TQ` fails boot (the explicit-interface-
implementation loophole is closed where the compiler cannot — probe-confirmed CS0111 gap).
Override exactly one member per interface: a custom `HandleAsync` never calls `Hear`
unless you call it yourself, and `Hear` is the sync surface — a turn that awaits its own
IO overrides `HandleAsync`.

### Answer.cs

```csharp
namespace DigitalBrain;

// The continuation view: Core pairs the reply with the original typed question,
// reconstructed from the asker's own journal at Answers.Sequence. Never journaled, never
// emittable by modules (Emit refuses it) — the reply fact is the journal record; this
// record exists only at dispatch.
public sealed record Answer<TQuestion, TReply>(TQuestion Question, TReply Reply) : Synapse
    where TQuestion : Synapse<TReply>
    where TReply : Synapse;
```

### JournalFact.cs

```csharp
namespace DigitalBrain;

// The public read shape (edge, tests, introspection — flow 10). One record per journal
// line: Position = the entry's own sequence, Entry = "heard" | "said", To = the said
// entry's receiver snapshot with per-receiver provenance ("declared" | "connected" |
// "ask"). Body is null when the line's kind is not in the running catalog: journals
// outlive code, reads never throw. Connections ride beside the journal so per-instance
// introspection never lies, before or after compaction.
public sealed record JournalFact(
    long Position,
    string Entry,
    string Kind,
    SynapseMetadata Metadata,
    IReadOnlyList<Delivery>? To,
    Synapse? Body);

public sealed record Delivery(NeuronId Receiver, string Via);

public sealed record NeuronReading(
    IReadOnlyList<JournalFact> Journal,
    IReadOnlyDictionary<string, IReadOnlyList<NeuronId>> Connections);
```

### CoreSynapses.cs — one closed vocabulary, read as a set (append-only forever)

```csharp
namespace DigitalBrain;

// ── topology (handled by Core on the receiving emitter). Connect/Disconnect/Schedule/
//    Unschedule are RESERVED kinds: a module declaring INeuron<> for any of them fails
//    boot, so Core's interception is never ambiguous with module dispatch. The outcome
//    kinds (ConnectionRefused, DeliveryFailed, AskExpired, ScheduleFailed) are ordinary
//    listenable facts — self-healing is one line: Hear(ScheduleFailed f) => Schedule(…).
public sealed record Connect(string Fact, NeuronId To) : Synapse;
public sealed record Disconnect(string Fact, NeuronId To) : Synapse;
public sealed record ConnectionRefused(SynapseRef Request, string Fact, NeuronId To, string Reason) : Synapse;

// ── delivery outcomes (journaled on the sender; any module may listen) ────────────────
public sealed record DeliveryFailed(SynapseRef Fact, NeuronId Receiver, string Reason, int Attempts) : Synapse;

// ── asks (journaled on the asker) ─────────────────────────────────────────────────────
public sealed record AskExpired(SynapseRef Ask, string Question) : Synapse;

// ── time (facts to a neuron mutate its Core-owned schedule table at commit; the same
//    table is what the in-turn Schedule/Unschedule verbs write — one mechanism; an
//    Unschedule naming an unknown/unscheduled kind is a journaled no-op reception) ─────
public sealed record Schedule(Synapse Fact, TimeSpan Period) : Synapse;
public sealed record Unschedule(string Fact) : Synapse;
public sealed record ScheduleFailed(string Fact, string Reason, int ConsecutiveFailures) : Synapse;
```

Consumers, named: `Connect`/`Disconnect` — the north-star and every runtime rewiring;
`ConnectionRefused` — the behavior creator and the BDD "typo refused loudly" scenario;
`DeliveryFailed` — physics #4, edge `AskFailedException`, any watchdog module;
`AskExpired` — flow 5's timeout ledger, edge ask failure; `Schedule`/`Unschedule`/
`ScheduleFailed` — flow 7 and every ingestion poller (the north-star's `XAccount`).

That is the entire ABI: 8 files, ~115 meaningful lines. Everything else is Core.

---

## 3 · Topology — decided

**The two-source union, both sources local.** `Emit(fact)` resolves receivers at commit:

```
receivers = ( declaredListeners(exactFactType) @ emitterName
              EXCEPT kinds that appear as connection targets for this factKind )
          ∪ connections[factKind]
          — deduplicated by NeuronId, snapshotted into the emission's journal entry
            with per-receiver provenance: declared | connected
```

- **Declared listeners** come from an in-process catalog built once at boot by ONE
  reflection pass (`Catalog.Build`) over the composition's explicit neuron type set. No
  registry, no lookup, no timeout, no repair — the three killers of v1's Subscribe
  (correlation-named fresh receivers; a 5s remote registry read inside the emitting turn
  that retracted it; dual derivation) are each structurally absent, verified against the
  dossier. Declarations never cross context (the locus rule).
- **Connections** are Core-owned durable state **on the emitter**
  (`IDurableDictionary<string factKind, NeuronId[]> connections`, set semantics), mutated
  only by Core handling `Connect`/`Disconnect` facts delivered through the ordinary bus —
  so every rewiring is journaled, causal, deduped, at-least-once, and happens-before every
  subsequent emission by that emitter. Connections are exactly the cross-context routes.
  They commit in the same one-batch `WriteStateAsync` as everything else — atomic with the
  turn by construction.
- **Connection-overrides-declaration (the ghost rule).** A connection for fact F to kind K
  removes K from F's declaration fan-out *at this emitter, while the connection row
  exists*. Without it, the north-star's behavior kind (declaring `INeuron<XPost>` —
  required, see validation) would also materialize as a ghost `behavior@"elonmusk"` via
  same-context fan-out and spawn a whole parallel ghost pipeline (chart, surface,
  renderer — all at "elonmusk"). With it, the connection *redirects* the kind.
  **Scope, honestly**: before any connection exists and after `Disconnect`, the declared
  route is live — a pre-wiring emission WOULD reach the ghost, and `Disconnect` returns
  the kind to declaration routing with the journaled outcome saying so. Wiring therefore
  precedes ingestion in practice (the north-star Connects before it starts watching), and
  "mute a declared listener" remains a Revision-stage operation (module deactivation),
  named in §9. A kind that adopts a well-known instance name (OS.md system-neuron style)
  opts out of locus-rule fan-in by convention and is reached by connection only —
  **OS.md amendment flagged** (§12).
- **Connect validation at handling time, against the local catalog** (no lookup hazard):
  the factKind must be a minted kind; `To.Kind` must be a known neuron kind declaring the
  exact `INeuron<factType>`; the factType must not be a question (`Synapse<TReply>`) —
  questions are not connectable (a connected second answerer instance would mint duplicate
  replies; no consumer exists for "forward my asks"). Violations journal the reception,
  leave the table untouched, and send `ConnectionRefused` back to the requester. The
  string-typo disease that killed Projects/digitalbrain (`ReceiverNeUniformType`) is
  refused at the door instead of dying silently in the table.
- **Delivery-time backstop** (tables outlive deployments): a delivery to a kind no silo
  implements, or to a receiver lacking the exact declared handler, is **terminal on the
  first attempt** — receiver journals the reception as terminal-unhandled (its truth),
  sender journals `DeliveryFailed` (its truth, physics #4), cross-referenced by
  `SynapseRef`. No 30-minute horizon burn, no per-receiver FIFO blockade.
- **Zero-receiver emission** is legitimate (module not installed): journaled with
  `to: []`, delivered nowhere, visible to introspection.
- **Heterogeneous silos are refused**: the catalog fingerprint (hash of sorted
  (kind, declaredFact, answeredFact) rows) must match across the cluster; a differing silo
  fails join loudly. Module changes deploy blue/green or stop-the-world. When Revision
  lands (Stage 3), the catalog becomes an epoch-versioned projection of journaled
  `ModuleActivated` facts and the fingerprint becomes the epoch — the boot-computed
  catalog is the Stage-1 degenerate case of that seam.
- **Authorization stance, Stage 1: provenance, not prevention.** Any neuron or the edge
  may send `Connect`/`Disconnect`; the reception's Source is the audit record. Topology
  mutation is named a privileged action whose enforcement point is the future Kernel
  capability gate (the owner's tap) — stated now so the open channel is never inherited
  silently.
- **Introspection**: per-instance — the connection table rides the compaction
  `ResetSnapshot` and is returned beside the journal by the read surface, so it never
  lies, before or after compaction. Global "who hears X?" enumeration = catalog (static) +
  per-instance tables by name; a global directory, if ever wanted, must be a passive
  projection OFF the emission path — never v1's in-turn registry.
- **Emitter instance names in `Connect` must be learned from facts/journals, never
  invented** — virtual actors make every typo'd name "succeed" into a silent parallel
  world; the north-star BDD test derives "elonmusk" from the ingestion module's own
  emitted facts. The *target* name is different: the connecting party mints it — that is
  precisely how a new behavior instance is born.

**Rejected** (evidence in §9): connections-only (ALT-1), declarations-only with code-gen'd
behaviors (ALT-2), kind-level runtime connections, a topology-registry neuron, install-time
connection manifests.

---

## 4 · The turn pipeline — definitive

Receiver side; self-delivery enters at step 1 via direct method call, never the proxy
(deadlock, proven). Sequence numbers: **one journal, one sequence, position IS the
sequence** — every entry (heard or said) consumes one position; a said entry's position is
its `SynapseRef.Sequence`. Sparse per-receiver sequences are normal and harmless: dedup
needs monotonicity, not density.

1. `Deliver(fact, metadata)` arrives. If the activation is **poisoned**, throw — the
   sender retries against a fresh activation.
2. **Dedup**: if `metadata.Sequence <= watermark[metadata.Source]`, return success
   silently — the duplicate ack (v1 semantics). Never an error: a throw here would mint
   false terminal records for facts the receiver actually handled.
3. **Open the turn**: ambient envelope set (Core-internal only — modules never see it);
   `TState` working copy materializes lazily on first `State` access (deserialize the
   committed `JsonElement` → fresh `TState`). From here to step 6, no durable structure is
   touched.
4. **Run the handler** with the delivery's CancellationToken. `Emit`/`Ask`/`Schedule`
   eagerly encode each fact through the Core body codec into the staged entry
   (unserializable types throw here, in the author's turn, naming the type; post-verb
   mutation of the author's object can reach nothing). Staging is in-memory only.
   `Ask` additionally consults the catalog in-turn: the calling kind must declare
   `INeuron<Answer<TQ,TR>>` (throw = retract, deterministic, names the missing
   declaration; announce-only questions use `Emit`); a zero-answerer ask journals an
   immediate terminal `DeliveryFailed(no-answerer)` instead of retrying. `Schedule`
   likewise requires the scheduling kind to declare `INeuron<TFact>` for the scheduled
   fact (throw in-turn otherwise — a tick nobody handles is a dead claim).
5. **Handler throws** (including cancellation): discard staging + working copy, clear the
   turn, rethrow. Zero durable trace. The sender's outbox retries; on exhaustion it
   journals the terminal `DeliveryFailed` on its side. The receiver never journals another
   module's failure.
6. **Stage the batch, in order**: the heard entry; one said entry per staged fact with the
   receiver snapshot resolved NOW (catalog ∪ connections, ghost rule applied, provenance
   marked, is-answerer/role flags frozen); the state slot (only if `State` was accessed);
   `watermark[Source] = Sequence`; ask-pins for asks; schedule-table mutations.
   The answer, if any, is staged **last** — its entry is the one stamped `Answers`.
7. **Arm the durable reminder wakeup before committing** if any staged emission has
   receivers (a post-commit crash must still get a wakeup; armed-but-uncommitted is a
   benign self-disarming leak — v1 `Neuron.Outbox.cs:9-20` ordering, ported).
8. **ONE `WriteStateAsync`** under `CancellationToken.None`. On ANY failure: poison the
   activation, `DeactivateOnIdle`, rethrow. No retraction commit, no in-memory
   compensation — reload of committed truth on the next activation is the only
   resynchronization, and the only answer that is also correct when the write landed but
   the ack was lost. (This deletes v1's entire retraction machinery — `TurnCheckpoint`,
   `Discard`/`Restore`, `ForgetHandled`/`_evictedWhileHandling` and the `Turns.cs:33`
   eviction-retraction dance, `StageInboundCause`, `CommitRetractionAsync`,
   `RecallHandledDeliveries` — each proven consumer-free once staging is post-handler and
   dedup has no eviction.)
9. **Success**: advance the in-memory `lastCommitted` marker (reads and dispatch never see
   past it — Orleans.Journaling exposes uncommitted in-memory mutations, so
   committed-only is a stated invariant, not an accident); schedule the fast drain timer;
   return the ack.
10. **Dispatch** (separate serialized timer/reminder turns): iterate said entries in
    `(cursor, lastCommitted]`; **always rehydrate the fact from the journal entry's
    `JsonElement`** — first delivery, redelivery and self-delivery all ship the same bytes
    (journal = wire, no dual-story serialization); per-receiver FIFO via `blockedTargets`
    with in-place pending rewrites (v1 port; the four legs: serialized drains, sequential
    awaited attempts per receiver, rewrite-in-place never re-append, blocked-stays-blocked);
    progress map is **lazy** — written only when a pass leaves an emission partially
    settled (absence = untouched, so the map holds strictly progress, never payload: the
    journal IS the outbox, verifiably); on exhaustion (attempts bound or horizon from the
    entry's journaled timestamp) or terminal classification: stage `DeliveryFailed`,
    settle, and **commit immediately before unblocking the receiver** — the abandonment
    barrier: only a committed hole may be jumped, or a crash resurrects a retracted hole
    behind an advanced watermark (grill a2, FATAL). A drain-commit failure poisons exactly
    like step 8 — timers swallow exceptions, so anything quieter is silent divergence.
    `cursor` = smallest unsettled said position.
    **Terminal classification at the wire**: before opening a turn, a receiver whose
    catalog binds no exact handler for the fact journals the reception as
    terminal-unhandled (its truth) and returns a Core-typed refusal (v1's
    `NeuronAuthorizationException` semantics, ported); the sender's drain treats the
    refusal — and Orleans' unresolvable-grain-kind error, where no receiver activation
    exists at all — as terminal on attempt 1 and journals `DeliveryFailed`. All other
    exceptions stay transient under the bounded policy.
    **The poison check guards every entry point and every commit site**: Deliver, drain
    tick, ask-expiry, and compaction all check it first and no-op/throw until the fresh
    activation reloads committed truth.
11. **Activation**: reload durable state; if unsettled emissions exist, arm wakeup +
    schedule drain; rebuild the volatile index of unsettled said positions (one scan from
    `cursor` — in-memory only, maintained incrementally by commits and drain passes, so a
    reception-heavy neuron's 50ms drain stays O(in-flight), not O(retained-window)).
    Nothing else durable is rebuilt — there is nothing else.

**Deliveries carry the envelope in `RequestContext` headers** (incoming/outgoing filter
pair, no AQN — kind strings only); the journal is the source of truth, headers are
transport convenience (physics #6).

---

## 5 · The handler algebra — decided

- **Listener/answerer split stands.** `Task<Synapse?>` was rejected: null-noise on
  listeners, no compile-time reply pairing, no boot cardinality check (flow 6's
  overhearers make single-interface cardinality undecidable), no `AskAsync` inference
  from the handler side. ino's canonical/reactive graveyard indicts two *delivery paths*
  and correlation-keyed grain identity — here there is one delivery path; the split is
  only in the module-facing signature.
- **`Ask` is a distinct verb; it does not collapse into `Emit`.** Ask = Emit + the
  answerer route (role=ask, frozen into the outbox snapshot) + open-ask registration +
  the in-turn continuation guard. `Emit(question)` is legal but announce-only — it
  reaches declarations only (question connections are refused at Connect, §3), never the
  answerer, and mints no open ask. v3's verdict holds: the verb is the act; the durable
  role bit is the metadata.
- **Ask routing, as a rule**: a question resolves THE answerer kind **at the asking
  turn's context name** (`answererKind@asker.Name`); the edge asks at its session's
  context. Aggregation across contexts is what connections and directed edge sends are
  for — to ask a well-known aggregate, ask *in its context*
  (`brain.Session("main").AskAsync(new GetDigest())`), because asking from "chat-1"
  reaches `digest@"chat-1"`, a different (empty) world. Wrong-context asks are the
  silent-parallel-world trap; this rule is the one sentence that prevents it.
- **Answers, not Cause-scanning.** Every emission's `Cause` is pure turn-causation. The
  answer alone additionally carries `Answers = the ask's SynapseRef`, Core-stamped.
  Continuation dispatch and edge matching key on `Answers`; an emitted fact of the same
  CLR type as `TReply` in the same turn is an ordinary announcement. Reply routing =
  declared listeners of `TReply` at the answerer's context ∪ the asker — one rule, so
  replies are overhearable (flow 6) without a second mechanism.
- **Deferred answers**: `Task<TReply?>` — null defers; Core holds the ask durably open at
  the answerer. **The closure rule, precisely**: while an ask is open, the FIRST
  `TReply`-typed emission in any LATER turn of that answerer closes it (stamped
  `Answers`, additionally delivered to the asker); same-turn emissions beside a non-null
  return are ordinary announcements. This is deliberate: an answerer kind's `TReply`
  vocabulary is its answer vocabulary — while an ask is open, emit announcements of a
  different type or answer first. At most one open ask per question kind per answerer
  activation; a second concurrent ask is refused in-turn and stays in the sender's
  outbox — backpressure from existing machinery; if never accepted, the delivery
  exhausts first and the sender journals `DeliveryFailed` at the RetryHorizon (the
  AskHorizon's `AskExpired` covers the case where delivery succeeded but no answer ever
  came). Ask expiry executes as a self-delivered turn via the direct-call path, never as
  bare timer-callback work (timer exceptions are swallowed).
- **Answer reconstruction is guarded, never fabricated**: the asker's journal at
  `Answers.Sequence` must hold a `Synapse<TFact>` ask entry AND the reply's `Source.Kind`
  must equal the catalog's answerer kind for that question (kills reply-type impersonation
  via connections) AND the entry's shape fingerprint (hash of member names, computed at
  boot) must match — a drifted question shape journals `AskExpired`-style terminal record
  instead of dispatching a question with silently-defaulted fabricated members
  (probe-confirmed STJ silent-default risk).
- **Ask lifetime is bounded**: AskHorizon = 2× RetryHorizon. Expiry journals `AskExpired`
  and releases the compaction pin. A late reply is journaled as an ordinary reception,
  dispatches nothing — falling off the window is loud exactly once, never silent.
- **Verbs inside a turn**: `Emit(fact)`, `Ask(question)`, `Schedule(fact, period)`,
  `Unschedule<TFact>()`. Nothing else. An in-turn directed `Send` was deleted too — zero
  consumers across all ten flows and the north-star (directed sends today happen at the
  edge: `Session.SendAsync` delivers the `Connect`); it returns when the Kernel
  behavior-creator becomes its consumer. The ambient envelope (`Handling`) and
  Send-to-source are deleted — exposing Sequence/Cause to modules is the
  correlation-as-API disease returning; if respond-to-source ever earns a consumer, the
  shape is `protected NeuronId Sender` — an address, not an envelope.

  The module author's complete in-turn surface, as code:

  ```csharp
  public abstract class Neuron                       // Core; modules subclass, never configure
  {
      public NeuronId Id { get; }                                    // Id.Name = the context
      protected void Emit(Synapse fact);
      protected void Ask<TReply>(Synapse<TReply> question) where TReply : Synapse;
      protected void Schedule(Synapse fact, TimeSpan period);        // requires INeuron<fact's type>
      protected void Unschedule<TFact>() where TFact : Synapse;      // typed — no magic strings
  }

  public abstract class Neuron<TState> : Neuron where TState : class, new()
  {
      protected TState State { get; set; }           // lazy per turn; committed only if accessed
  }
  ```

  **Instance fields on a neuron are volatile and die with the activation** — all durable
  module state lives in `TState`. A private cache field works in every test and loses
  data in production; the rule is stated here so nobody learns it that way.
- **Dispatch is exact-declared-type from the catalog, one derivation.** Probe-confirmed:
  contravariant cast selection is interface-declaration-order dependent, so polymorphic
  dispatch is banned by construction: every concrete fact is `sealed` (boot-enforced),
  `INeuron<abstract TFact>` fails boot (wildcard ban; journal-mirrors read journals
  instead), and the base `Neuron` implements no `INeuron<>` at all (v2's
  `INeuron<Synapse>` throw-fallback becomes the terminal-unhandled journal record).
  The invoker stays v2's zero-table generic cast, extended: the sender closes one of two
  transport methods per fact type (cached `MakeGenericMethod`, the existing
  `ForwarderFor` pattern) — `DeliverAsync<TFact>` for listeners,
  `DeliverQuestionAsync<TQ,TR>` for the answerer route (TR extracted once per question
  type); the receiver casts `((INeuron<TQ,TR>)this)` and Core captures the returned
  reply. No FrozenDictionary, no source generator, no defensive union.

### The edge

```csharp
public sealed class Brain
{
    public Session Session(string context);   // NeuronId("session", context) — Core-owned kind
    public Task<NeuronReading> ReadAsync(     // journal lines + live connection table
        NeuronId neuron, long afterPosition = 0, CancellationToken ct = default);
}

public sealed class Session
{
    public NeuronId Id { get; }
    public Task EmitAsync(Synapse fact, CancellationToken ct = default);
    public Task SendAsync(NeuronId receiver, Synapse fact, CancellationToken ct = default);
    public Task<TReply> AskAsync<TReply>(Synapse<TReply> question, CancellationToken ct = default)
        where TReply : Synapse;
}
```

- The session **is a neuron** (journal, watermark, outbox, connections). Emit/Send/Ask
  run one session turn each and return after commit. Edge-born facts carry `Cause: null`.
- `AskAsync` = fire the ask exactly once, then poll the session journal from a cursor
  (immediate first read, then backoff): a reception with `Answers == askRef` → return the
  typed reply; a `DeliveryFailed` whose `Fact == askRef` → `AskFailedException`; an
  `AskExpired` → `AskFailedException`; cancellation → `OperationCanceledException` naming
  session + askRef. The Task is volatile sugar; **the journal is the ask** — a crashed and
  restarted edge reconstructs the whole round trip from the session journal alone.
- Reads serve `journal[0..committedCount)` only (committed-count watermark advanced per
  successful commit) and are the sole interleaving surface — an interleaved read must
  never surface an entry a failed commit will retract.
- Wire calls fire **exactly once**; a wire failure is an ambiguous-outcome exception
  naming the session — recovery is reading the session journal, not retrying the call
  (a retried ask would mint a second journaled ask that dedup correctly cannot catch).
- `Get<TNeuron>` dies as a send/ask surface (type-coupled edge — naming another module's
  class is orchestration; grown modules have no `TNeuron`). Typed references survive only
  as journal-read sugar in DigitalBrain.Testing.
- Push observers (v1 `WatchNeuron` lineage) are Stage 2, replacing the poll behind the
  same cursor shape without touching callers.

### Module authoring rules (all boot-enforced, zero module lines)

- Module dependencies arrive by primary constructor from DI
  (`class XAccount(HttpClient http) : Neuron, INeuron<Poll>`); the `Neuron` base
  constructor is parameterless and resolves Core internals via keyed services (the
  IAW 34-file facet tax is structurally impossible). Host composition is five lines:

  ```csharp
  var builder = Host.CreateApplicationBuilder(args);
  builder.Services.AddHttpClient().AddSingleton<IXClient, XClient>();   // module services
  builder.UseOrleans(silo => silo.AddDigitalBrain(typeof(XAccount).Assembly,
                                                  typeof(Chart).Assembly));
  await builder.Build().RunAsync();
  ```
- Every concrete fact record is `sealed`; `INeuron<abstract TFact>` and generic synapse
  types fail boot (exact dispatch made total — the variance-order probe; journal-mirrors
  read journals instead of declaring wildcards).
- `TState` must be default-constructible, STJ-round-trippable, with no `required`
  members / non-defaulted constructor parameters (boot-checked — a revision adding one
  would brick the neuron at materialization). TState shapes are append-only; renames are
  removal-plus-addition and lose data by design.
- Sealed away from modules: `OnActivateAsync`/`OnDeactivateAsync` (`public sealed
  override`; no module activation hook exists — nothing needs one once timers are Core
  vocabulary), `WriteStateAsync`/`GrainFactory`/`DeactivateOnIdle` (shadowed
  `[Obsolete(error: true)]`), keyed `IDurable*` resolution (DI-root gatekeeper: module
  keys fail activation loudly), extra grain interfaces / `IRemindable` (activation check,
  v1 `NeuronConcurrency` port), non-Core incoming calls (filter whitelist), self-proxy
  calls (outgoing filter converts the proven deadlock into a loud exception). The runtime
  backstop that makes even reflection harmless: Core stages durable mutations strictly
  post-handler, so a mid-turn commit by any means commits an empty batch.

---

## 6 · Atomicity and state — decided

`class Chart : Neuron<ChartState>, INeuron<ChartPoint>` — one generic parameter is the
entire module surface.

- The slot is `IDurableValue<JsonElement>`; Core encodes/decodes `TState` through the
  **Core body codec** — Core-owned reflection `JsonSerializerOptions` plus a
  `SynapseConverter` that renders every `Synapse`-declared member (e.g. `Button.OnTap`)
  as `{"kind": …, "body": {…}}` recursively. Module POCOs need zero attributes, zero
  contexts, zero registration. The proven bare-resolver constraint binds only
  `JournalJsonContext`, whose registrations are exactly Core's closed entry types +
  `JsonElement` + the journaling package's own bookkeeping — **no module CLR type ever
  enters the Orleans.Journaling surface** (the named invariant that makes "journals
  outlive code" true by construction; the current `JournalEntry` AQN + `Type.GetType`
  on disk is the live counterexample and does not survive).
- Working copy: lazy materialization on first `State` access; committed only if accessed;
  discarded unconditionally at turn end. `JsonElement` immutability makes aliasing into
  the committed copy impossible; stale captured references die deterministically.
- One `WriteStateAsync` batch commits journal + state + watermark + connections + schedule
  + ask-pins + progress — the Orleans.Journaling one-batch behavior v1's whole engine
  already relied on, verified against current docs.
- The whole synapse vocabulary and every `TState` are codec-validated at boot, failing
  loudly beside the kind-collision checks — a serialization defect never waits for the
  first emission, and commit-time codec failures that do occur are classified settled
  (terminal, correctly blamed), never 30 minutes of futile retry.
- Rejected: state-as-fold (collapses into a snapshot slot the moment compaction exists,
  plus activation replay cost), declared `Apply` folds (duplicated intent — ceremony),
  module-visible `IDurable*` (unenlisted durable mutation is the atomicity hole itself).

### Time (flow 7 and every ingestion module)

`Schedule(fact, period)` / `Unschedule<TFact>()` — in-turn verbs writing the Core-owned
schedule table; remote scheduling via the `Schedule`/`Unschedule` facts (same table, one
mechanism; note the deliberate name pun — inside a neuron `Schedule(new Tick(), period)`
binds the verb, `new Schedule(...)` builds the remote fact; both compile clean in one
class, probe-verified). Core re-arms grain timers from the table at activation with a
reminder backstop for idle neurons. A tick runs the **ordinary pipeline** as a
self-sourced heard entry (`Cause` = the schedule's journal entry; no watermark —
in-activation direct call is exactly-once by construction). Failures honor physics #4
against the timer-swallowing constraint: Core catches the turn failure; after
**5 consecutive failures** (one Core constant, `DeliveryPolicy` family) it journals
`ScheduleFailed` and unschedules — terminal, journaled, never infinite silent retry.
`ScheduleFailed` is an ordinary listenable fact; a module that wants to survive a flaky
endpoint re-arms itself in one line: `public void Hear(ScheduleFailed f) =>
Schedule(new PollX(), Backoff(f.ConsecutiveFailures));`.
This deletes v1's out-of-turn emission path entirely: **every emission happens inside a
turn** — a stronger invariant than v1 had.

---

## 7 · The engine — what ports, what changed, journal schema

| Concern | v1 | This design |
|---|---|---|
| Delivery identity | GUID `SynapseId` minted at fire | `SynapseRef(Source, Sequence)` — structural, journal-native |
| Dedup | 4096-entry durable window; eviction-retraction bug class (`Neuron.Turns.cs:33`) | Per-source watermark `(Seq, Touched)`; duplicate = silent success ack; no eviction, no capacity failure (window silently re-accepts under >4096 in-flight fan-in — decisive); pruned past RetryHorizon+slack (safe: no attempt outlives the horizon); rollback = poison+reload, no arithmetic |
| Retry | 1000 attempts / 30-min horizon / 30s attempt / depth 16; telemetry-only abandonment | Same bounds; terminal records are **journaled** `DeliveryFailed` on the sender; permanent conditions settle on attempt 1; the abandonment barrier commits the hole before unblocking |
| Outbox | Separate `IDurableList<byte[]>` duplicating every payload | The journal IS the outbox: said entries carry body + receivers; lazy progress map holds strictly progress; durable cursor; dispatch-from-journal always |
| Wakeup | Reminder grain (1-min) + 50ms in-activation timer | Ported as-is; armed **before** commit |
| Ordering | blockedTargets per pass; in-place rewrites | Ported as-is; the four legs named and tested (they are the watermark's premise) |
| Journals | Two feeds (incoming/outgoing), Orleans-serialized `byte[]` bodies | One journal, JSON lines, interleaved heard/said in turn order; explicit `Cause` on every entry |
| Cursors/compaction | Tallies-outlive-compaction + `ResetSnapshot` | Ported; tallies keyed by factKind (never CLR names), heard/said dicts; floor = min(cursor, oldest unexpired ask-pin); retained bounds are soft targets subordinate to the floor |
| Concurrency | `RequireSerializedTurns` | Ported + extended (interface whitelist, no IRemindable) |
| Retraction | Checkpoint/Discard/Restore/CommitRetraction | Deleted — poison + reload (post-handler staging has nothing to retract) |

### The journal schema (the only durable element types; all Core-owned, closed)

```csharp
internal sealed record JournalEntry(
    long Seq,                  // own-journal position; for "said" this IS SynapseRef.Sequence
    string Entry,              // "heard" | "said"
    string Kind,               // boot-catalog factKind (facts mint kinds exactly as neurons do:
                               // NeuronId.KindOf — lowercased record name)
    DateTimeOffset At,         // said: turn commit time (the retry horizon runs from it);
                               // heard: the emitter's envelope timestamp
    SynapseRefEntry? Cause,    // turn causation; null = EDGE-BORN ONLY (ticks carry the
                               // schedule entry's ref; every in-brain fact has a cause)
    SynapseRefEntry? Answers,  // said: the answer emission; heard: copied from the reply's
                               // envelope — the edge poll and continuation dispatch match on it
    SynapseRefEntry? From,     // heard only: the emission's identity = the dedup key
    NeuronIdEntry[]? To,       // said only: receiver snapshot + provenance; [] = zero-receiver
    JsonElement Body);         // Core body codec output; opaque to Orleans.Journaling

internal sealed record SynapseRefEntry(string Kind, string Name, long Seq);
internal sealed record NeuronIdEntry(string Kind, string Name, string? Via); // Via: declared|connected|ask
internal sealed record ScheduleEntry(string Kind, JsonElement Fact, TimeSpan Period,
    DateTimeOffset NextDue, int ConsecutiveFailures);   // the scheduled fact stays JsonElement —
                                                        // no module CLR type in durable data
```

Example lines (readable, no Type/AQN/GUID):

```json
{"seq":41,"entry":"heard","kind":"xpost","at":"2026-08-05T09:00:00Z","from":{"kind":"xaccount","name":"elonmusk","seq":17},"cause":{"kind":"xaccount","name":"elonmusk","seq":16},"body":{"author":"elonmusk","text":"..."}}
{"seq":42,"entry":"said","kind":"chartpoint","at":"2026-08-05T09:00:01Z","cause":{"kind":"xaccount","name":"elonmusk","seq":17},"to":[{"kind":"chart","name":"dashboard","via":"declared"}],"body":{"x":1754380801,"y":98123.5}}
```

Sidecar durable structures (same grain, same batch): `lastSeq`, `cursor`,
`progress: IDurableDictionary<long, DeliveryProgress>` (lazy),
`asks: IDurableDictionary<long, DateTimeOffset>`,
`heardFrom: IDurableDictionary<string, WatermarkEntry>`,
`connections: IDurableDictionary<string, NeuronIdEntry[]>`,
`schedule: IDurableDictionary<string, ScheduleEntry>`,
`heardTallies`/`saidTallies: IDurableDictionary<string, long>`,
`state: IDurableValue<JsonElement>`.

A journal containing a kind whose module is unloaded: activation replays only Core types
(safe by schema); redelivery of it settles as `DeliveryFailed("kind not in catalog")` on
attempt 1; reads return `JournalFact` with `Body: null`; tallies/compaction/corpus never
need the CLR type.

---

## 8 · The Core file list

`src/DigitalBrain.Core` — implementation, one responsibility per file:

| File | Responsibility |
|---|---|
| `Neuron.cs` | The base grain: turn pipeline (§4), verbs, poisoning, sealed lifecycle, shadowed escape hatches |
| `NeuronOfState.cs` | `Neuron<TState>`: lazy working copy, commit-if-accessed |
| `Catalog.cs` | `Catalog.Build(types)`: kinds/listeners/answerers/factKinds + every boot refusal + fingerprint; pure function, per-silo in DI (never static — test clusters compose independently) |
| `BodyCodec.cs` | Core STJ options + `SynapseConverter` (recursive kind/body shape) + boot vocabulary/TState validation |
| `JournalEntry.cs` | The closed durable schema family + `JournalJsonContext` |
| `NeuronJournal.cs` | Entries, lastSeq, committed-count read watermark, tallies, retained window, floor, `ResetSnapshot` |
| `Neuron.Dispatch.cs` | Drain: blockedTargets FIFO, lazy progress, terminal classification, abandonment barrier, `DeliveryFailed` |
| `Neuron.Asks.cs` | Ask registration/pins, open asks at the answerer, `Answers` stamping, guarded `Answer<Q,R>` reconstruction, `AskExpired` |
| `Neuron.Connections.cs` | Table semantics, `Connect`/`Disconnect` handling + validation, ghost rule, `ConnectionRefused` |
| `Neuron.Schedule.cs` | Schedule table, timer re-arm, tick turns, `ScheduleFailed` |
| `Neuron.Transport.cs` | Nested internal `ITransport`: `DeliverAsync<TFact>` / `DeliverQuestionAsync<TQ,TR>` / `ReadJournalAsync` / `ReadStateAsync` (committed-only, sole interleaving surface) + `AddressOf` |
| `OutboxWakeup.cs` | Reminder-backstop grain (v1 port), callable only by the dedicated wakeup |
| `NeuronConcurrency.cs` | Activation checks: serialized turns, interface whitelist, no IRemindable, TState contract |
| `Filters/SynapseHeaders.cs` | Envelope ↔ RequestContext (kind strings, no AQN) |
| `Filters/IncomingSynapseFilter.cs` | Header consume → turn entry; whitelist enforcement |
| `Filters/OutgoingSynapseFilter.cs` | Header write; self-proxy-call guard |
| `Brain.cs` | Edge: `Brain`, `Session` (Emit/Send/Ask + journal polling), `AskFailedException` |
| `Hosting/DigitalBrainSiloExtensions.cs` | `AddDigitalBrain(assemblies)`: catalog build + fingerprint, DI gatekeeper on keyed `IDurable*`, journal format, filters |

`src/DigitalBrain.Testing` — ports of the proven v1 machinery: `DigitalBrainTest` /
`NeuronTest<T>` (the only two kinds), `BrainTestClusters`/`ComposedFixture`
(cluster-per-composition, fingerprinted), `RecordingJournalStorageProvider` +
`JournalFaultHandle` (sticky commit faults keyed per neuron, armed-fault leak detection at
dispose), `TestClock`/`ControllableTimeProvider` (time that refuses to run backwards),
journal assertion helpers over the public read surface only.

---

## 9 · Considered and rejected — with reasons

| Rejected | Reason (evidence) |
|---|---|
| v1's Subscribe machinery | ~27 files/~25 types; correlation-named fresh receivers (unaddressable); 5s in-turn registry lookup retracting turns; dual derivation with two silent-divergence points; activation-time repair on every activation; production audience ≈ one fact (dossier, exhaustive) |
| Connections-only topology (ALT-1) | Kind-level routes must apply to emitter instances that don't exist yet → resolved from kind-level data at emit = the catalog re-encoded as journaled facts with a distribution problem; install-time manifests can't know instance names (contexts are born at edge-first-speech); v1's registry — the only ALT-1 artifact ever built — rotted ("silently deaf", repaired every activation) |
| Declarations-only topology (ALT-2) | Declarations never cross context; the north-star is cross-context; a code-gen'd behavior module bakes the route into unreadable source (kills flow 10), costs an LLM+Roslyn+ALC pipeline per table row, and depends on Stage-3 Revision while the north-star is THIS stage's acceptance test |
| Kernel-owned topology-registry neuron | Reintroduces a remote lookup in the emit path (v1's turn-retraction killer) or an eventual-consistency window where "connected" is a lie |
| Source-generated dispatch manifest (+ defensive union) | The graveyard's dual-derivation entry: v1's display-string/FullName divergence silently emptied audiences; final needed defensive-union + warn-and-continue + tests to keep the table honest — reflection was the authority all along; the union converts incompleteness into permanent silence |
| Orleans type manifest as catalog source | Structurally unable: `INeuron<TFact>` is a plain interface in an Orleans-free assembly — not in the manifest |
| Owner's `Task<Synapse?> HandleAsync` single interface | Null-noise on listeners; no compile-time reply pairing; answerer cardinality undecidable with overhearers (flow 6); no handler-side reply-type check |
| GUID `SynapseId` | `(Source, Sequence)` carries the same stability guarantee structurally (minted once, committed, persisted), is journal-native and human-readable |
| 4096 dedup window | The eviction-retraction bug class (`Neuron.Turns.cs:33`) deleted by construction; capacity failure under fan-in saturation silently duplicates committed turns; zombie late arrivals delivered after their terminal record (journals telling two stories) |
| Watermark-with-window fallback | Inherits both bug classes, doubles the retraction surface, adds zero coverage |
| Same-turn reply ride-back | A second delivery route bypassing the outbox breaks the per-(sender,receiver) FIFO the watermark stands on — deterministic fact loss (grill h2 FATAL); OS.md's fast-path sentence must be amended |
| `SettledDeliveryFailureAttribute` | Module errors are vocabulary (reply unions); kernel-side permanent conditions are classified terminal at delivery; nothing remains for the attribute |
| `protected SynapseMetadata Handling` + Send-to-source | Zero consumers in ten flows + north-star; exposes Sequence/Cause to modules = correlation-as-API returning; captured-context trap |
| In-turn directed `Send(address, fact)` verb | Zero consumers across all eleven demo compositions — directed sends today are edge-only (`Session.SendAsync` delivering `Connect`); returns when the Kernel behavior-creator becomes a real consumer |
| Ambient current-question (instead of `Answer<Q,R>`) | Hidden-state trap of the same species as Handling; FLOWS 3/5 ratify the typed wrapper |
| `Completed` synapse (CONTEXT.md) | A heard entry with no said entries already records "handled, nothing to say"; with boot-verified answerers every ask gets a typed outcome; no consumer remains — **CONTEXT.md amendment flagged** |
| Polymorphic/contravariant dispatch | Probe-confirmed interface-declaration-order dependence — an invisible routing change; exact-type + sealed facts + wildcard ban make dispatch total and deterministic |
| `INeuron<Synapse>` catch-all base fallback | Makes every kind a universal listener under assignability, and its throw-fallback yields infinite silent retry at HEAD (physics-4 violation); terminal-unhandled journal record replaces it |
| State-as-fold / declared `Apply` folds | Fold collapses into a snapshot slot at compaction anyway; `Apply` duplicates handler intent (ceremony) |
| Module-visible `WriteStateAsync` / keyed `IDurable*` / raw timers / extra grain interfaces / `IRemindable` | Each is an unenlisted durable write, a second bus, or an out-of-pipeline entry — the atomicity hole itself; sealed by the enforcement set (§5) |
| Per-module `JsonSerializerContext` registration | ~40+ lines of ceremony across the corpus — more than all counted ceremony combined; Core body codec makes it zero |
| Second dense emission-only sequence | Breaks position=identity, forces an index, recreates v1's two-sequence bookkeeping for zero consumer |
| Edge `Get<TNeuron>` send/ask | Type-coupled orchestration (research forced-consequence #3); grown modules have no TNeuron; survives only as test journal-read sugar |
| `[WireTo]`/string streams | The typo evidence (`ReceiverNeUniformType`: read-side probe of a key written nowhere, silently defaulting) — string routing dies silently; zero production users ever |
| Orleans Streams as neuron-to-neuron delivery | final + v4: memory streams don't replay to not-yet-active subscriptions — documented silent loss, twice |
| Correlation IDs in the ABI | Causation = `Cause`; operation identity = the ask's `SynapseRef` via `Answers`; live tracing = Activity/traceparent |
| Unbounded compaction floor (pin until answered) | One lost reply pins the journal forever — v2's infinite-silent-retry disease relocated into storage; bounded AskHorizon + `AskExpired` instead |
| v1's turn retraction machinery | Every consumer proven dead once staging is post-handler and dedup has no eviction; poison+reload is also the only correct answer under ambiguous commit |

---

## 10 · All ten flows + the north-star, in the final vocabulary

Ceremony count: **0 module-author ceremony lines in all eleven flows** (the grill's flows
pass counted 27 under the draft ABI; 26 were `return Task.CompletedTask;` — deleted by
`Hear`/`Answer` — and 1 was the forced listener body — deleted by the DIM default). The
counting unit is lines; the boot-mandated `sealed` modifier on fact records is a token,
not a line, and is stated in the authoring rules. Every module below compiled verbatim
(zero errors, zero warnings, net10.0 + net11.0, probe in the session archive).

The demo UI vocabulary, shared by flows 2/9 and the north-star (a module like any other —
the closed-union widget lineage of final/self-improving, trimmed to what the demos use):

```csharp
public sealed record UiSurface(Widget Root) : Synapse;
public abstract record Widget;
public sealed record Label(string Text) : Widget;
public sealed record Column(ImmutableArray<Widget> Children) : Widget;
public sealed record Button(string Text, Synapse OnTap) : Widget;
public sealed record LineChart(ImmutableArray<Dot> Dots) : Widget;
public sealed record Dot(double X, double Y) : Widget;
```

### 1 · Ask → Answer

```csharp
public sealed record Greet(string Who) : Synapse<Greeted>;
public sealed record Greeted(string Message) : Synapse;

public sealed class Greeter : Neuron, INeuron<Greet, Greeted>
{
    public Greeted Answer(Greet question) => new($"Hello, {question.Who}!");
}

Greeted greeted = await brain.Session("chat-1").AskAsync(new Greet("Ada"), ct);
```

### 2 · Announce → Listen

```csharp
public sealed record PlanDay(DateOnly Date) : Synapse;
public sealed record DayPlanned(DateOnly Date, ImmutableArray<string> Tasks) : Synapse;

public sealed class Planner : Neuron, INeuron<PlanDay>
{
    public void Hear(PlanDay fact) => Emit(new DayPlanned(fact.Date, ["write core", "walk"]));
}

public sealed class Diary : Neuron, INeuron<DayPlanned>;          // hearing IS the behavior

public sealed class UiProjector : Neuron, INeuron<DayPlanned>
{
    public void Hear(DayPlanned fact) => Emit(new UiSurface(new Label($"{fact.Date}: {fact.Tasks.Length} tasks")));
}

await brain.Session("day-7").EmitAsync(new PlanDay(new DateOnly(2026, 8, 7)), ct);
```

### 3 · Ask → Answer → Continue

```csharp
public sealed record AddTask(string Title) : Synapse;
public sealed record FindTasks(DateOnly Date) : Synapse<TaskList>;
public sealed record TaskList(ImmutableArray<string> Tasks) : Synapse;

public sealed class Planner : Neuron, INeuron<PlanDay>, INeuron<Answer<FindTasks, TaskList>>
{
    public void Hear(PlanDay fact) => Ask(new FindTasks(fact.Date));

    public void Hear(Answer<FindTasks, TaskList> answer)
        => Emit(new DayPlanned(answer.Question.Date, answer.Reply.Tasks));
}

public sealed class TaskStore : Neuron<TaskStoreState>, INeuron<AddTask>, INeuron<FindTasks, TaskList>
{
    public void Hear(AddTask fact) => State.Tasks.Add(fact.Title);
    public TaskList Answer(FindTasks question) => new([.. State.Tasks]);
}

public sealed class TaskStoreState { public List<string> Tasks { get; } = []; }
```

The planner holds zero fields; killing the silo between the two turns loses nothing.

### 4 · Chain — pipelines without a pipeline

```csharp
public sealed record AudioCaptured(byte[] Audio) : Synapse;
public sealed record Transcribed(string Text) : Synapse;
public sealed record Summarized(string Summary) : Synapse;

public sealed class Transcriber(ISpeechClient speech) : Neuron, INeuron<AudioCaptured>
{
    public async Task HandleAsync(AudioCaptured fact, CancellationToken ct)
        => Emit(new Transcribed(await speech.TranscribeAsync(fact.Audio, ct)));
}

public sealed class Summarizer(IModelClient model) : Neuron, INeuron<Transcribed>
{
    public async Task HandleAsync(Transcribed fact, CancellationToken ct)
        => Emit(new Summarized(await model.SummarizeAsync(fact.Text, ct)));
}

public sealed class Memory : Neuron, INeuron<Summarized>;

public sealed class UiProjector : Neuron, INeuron<Summarized>
{
    public void Hear(Summarized fact) => Emit(new UiSurface(new Label(fact.Summary)));
}

public interface ISpeechClient { Task<string> TranscribeAsync(byte[] audio, CancellationToken ct); }
public interface IModelClient { Task<string> SummarizeAsync(string text, CancellationToken ct); }
```

No stage names another; the causal chain reconstructs across journals from
`From`/`Cause`/`To` alone.

### 5 · Fan-out / Fan-in — the join state is the state slot

```csharp
public sealed record MorningStarted(DateOnly Date) : Synapse;
public sealed record GetForecast : Synapse<Forecast>;
public sealed record Forecast(string Sky, double High) : Synapse;
public sealed record BriefingReady(Forecast Forecast, TaskList Tasks) : Synapse;

public interface IWeatherClient { Task<Forecast> TodayAsync(CancellationToken ct); }

public sealed class Weather(IWeatherClient sky) : Neuron, INeuron<GetForecast, Forecast>
{
    public async Task<Forecast?> HandleAsync(GetForecast question, CancellationToken ct)
        => await sky.TodayAsync(ct);
}

public sealed class Briefing : Neuron<BriefingState>, INeuron<MorningStarted>,
    INeuron<Answer<GetForecast, Forecast>>, INeuron<Answer<FindTasks, TaskList>>
{
    public void Hear(MorningStarted fact) { Ask(new GetForecast()); Ask(new FindTasks(fact.Date)); }

    public void Hear(Answer<GetForecast, Forecast> answer)
    { State.Forecast = answer.Reply; EmitWhenComplete(); }

    public void Hear(Answer<FindTasks, TaskList> answer)
    { State.Tasks = answer.Reply; EmitWhenComplete(); }

    private void EmitWhenComplete()
    {
        if (State is { Forecast: { } f, Tasks: { } t }) Emit(new BriefingReady(f, t));
    }
}

public sealed class BriefingState { public Forecast? Forecast { get; set; } public TaskList? Tasks { get; set; } }
```

`BriefingReady` fires exactly once regardless of arrival order; a restart mid-gather
resumes from the committed slot. An unanswered leg ends in `AskExpired` — declare
`INeuron<AskExpired>` to act on it (the timeout ledger). *(FLOWS.md amendment flagged:
the join mechanism is the state slot, not a module-visible journal read.)*

### 6 · Overhear

```csharp
public sealed class UsageMemory : Neuron, INeuron<FindTasks>, INeuron<TaskList>;
```

Questions and replies are facts; declaring them is hearing them (replies fan out to
declared listeners at the answerer's context ∪ the asker). Removing the module changes
nothing else.

### 7 · Pulse — time as a fact

```csharp
public sealed record StartPulse(TimeSpan Period) : Synapse;
public sealed record Tick : Synapse;

public sealed class Pulse : Neuron, INeuron<StartPulse>, INeuron<Tick>
{
    public void Hear(StartPulse fact) => Schedule(new Tick(), fact.Period);
    public void Hear(Tick fact) => Emit(fact);   // fan out to Countdown/Agenda at this context
}

public sealed class Countdown : Neuron<CountdownState>, INeuron<Tick>
{
    public void Hear(Tick fact) => State.Remaining -= 1;
}

public sealed class CountdownState { public int Remaining { get; set; } = 10; }
```

Consumers are tested by emitting `Tick` directly — time is mockable because it is just a
fact. A failing tick ends in journaled `ScheduleFailed`, never silent. *(FLOWS.md
amendment flagged: the timer is Core vocabulary, not a module's private grain timer.)*

### 8 · Contexts — one brain, many parallel worlds

```csharp
var chat1 = brain.Session("chat-1");
var chat2 = brain.Session("chat-2");
await Task.WhenAll(chat1.AskAsync(new Greet("Ada"), ct), chat2.AskAsync(new Greet("Grace"), ct));
```

Same modules, isolated columns, separate journals — concurrency for free. Each context's
journals contain only its own conversation (asserted from journals).

### 9 · The UI loop — pixels are facts

```csharp
public sealed record CompleteTask(int Id) : Synapse;
public sealed record TaskCompleted(int Id) : Synapse;

public sealed class TaskStore : Neuron<TaskItems>, INeuron<CompleteTask>
{
    public void Hear(CompleteTask fact)
    {
        State.Open.RemoveAll(t => t.Id == fact.Id);
        Emit(new TaskCompleted(fact.Id));
    }
}

public sealed class TaskItems { public List<OpenTask> Open { get; } = []; }

public sealed class Agenda : Neuron<AgendaState>, INeuron<Tick>, INeuron<TaskCompleted>
{
    public void Hear(Tick fact)
        => Emit(new UiSurface(new Column([.. State.Open.Select(t =>
               new Button($"done: {t.Title}", new CompleteTask(t.Id)))])));

    // Calling one's own Hear is same-turn composition (the nested Emit stages into THIS
    // turn's batch) — not a self-delivery through the pipeline.
    public void Hear(TaskCompleted fact) { State.Complete(fact.Id); Hear(new Tick()); }
}

public sealed class AgendaState
{
    public List<OpenTask> Open { get; } = [];
    public void Complete(int id) => Open.RemoveAll(t => t.Id == id);
}

public sealed record OpenTask(int Id, string Title);

public interface IRenderChannel { Task RenderAsync(UiSurface surface, CancellationToken ct); }

public sealed class FlutterRenderer(IRenderChannel channel) : Neuron, INeuron<UiSurface>
{
    public async Task HandleAsync(UiSurface fact, CancellationToken ct)
        => await channel.RenderAsync(fact, ct);          // private IO; the tap comes back below
}

// the owner taps → the renderer's edge session emits the widget's own synapse:
await brain.Session("day-7").EmitAsync(new CompleteTask(42), ct);
```

The whole loop asserts from journals: surface said → tap heard → `TaskCompleted` said →
new surface said. A UI test with no UI running.

### 10 · Introspection

```csharp
var reading = await brain.ReadAsync(new NeuronId("chart", "dashboard"), 0, ct);
// reading.Journal: bodies included; kinds without a loaded module read with Body: null
// reading.Connections: the live table — the wiring diagram's dynamic half, never stale
```

The wiring diagram = the catalog (declarations, enumerable) + per-instance connection
tables (returned beside the journal by the same read surface). The corpus is a projection
module built on flow 6 + journal reads. In-brain self-reading (Core question vocabulary
like `ReadJournal : Synapse<JournalSlice>`) is deferred until its consumer (the AI module,
Stage 2) exists — the seam is named here so nobody reinvents it ad hoc.

### ★ The north-star

```csharp
// ── ingestion module ──
public sealed record WatchAccount : Synapse;
public sealed record PollX : Synapse;
public sealed record XPost(string Author, string Text, DateTimeOffset At) : Synapse;

public sealed class XCursor { public DateTimeOffset LastSeen { get; set; } }

public interface IXClient
{
    Task<IReadOnlyList<(string Text, DateTimeOffset At)>> PostsSinceAsync(
        string account, DateTimeOffset since, CancellationToken ct);
}

public interface IPriceClient { Task<double> UsdAsync(string symbol, CancellationToken ct); }

public sealed class XAccount(IXClient x) : Neuron<XCursor>, INeuron<WatchAccount>, INeuron<PollX>
{
    public void Hear(WatchAccount fact) => Schedule(new PollX(), TimeSpan.FromMinutes(1));

    public async Task HandleAsync(PollX fact, CancellationToken ct)
    {
        foreach (var post in await x.PostsSinceAsync(Id.Name, State.LastSeen, ct))
        {
            State.LastSeen = post.At;
            Emit(new XPost(Id.Name, post.Text, post.At));
        }
    }
}

// ── price module (the boot-verified answerer for GetBtcPrice) ──
public sealed record GetBtcPrice : Synapse<BtcPrice>;
public sealed record BtcPrice(double Usd) : Synapse;

public sealed class BtcPriceFeed(IPriceClient price) : Neuron, INeuron<GetBtcPrice, BtcPrice>
{
    public async Task<BtcPrice?> HandleAsync(GetBtcPrice question, CancellationToken ct)
        => new(await price.UsdAsync("BTC", ct));
}

// ── behavior neuron (the piece the brain wires in at the owner's request) ──
public sealed record ChartPoint(double X, double Y) : Synapse;

public sealed class BtcDotOnXPost : Neuron, INeuron<XPost>, INeuron<Answer<GetBtcPrice, BtcPrice>>
{
    public void Hear(XPost fact) => Ask(new GetBtcPrice());
    public void Hear(Answer<GetBtcPrice, BtcPrice> answer)
        => Emit(new ChartPoint(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), answer.Reply.Usd));
}

// ── chart module ──
public sealed class ChartState { public List<ChartPoint> Points { get; } = []; }

public sealed class Chart : Neuron<ChartState>, INeuron<ChartPoint>
{
    public void Hear(ChartPoint fact)
    {
        State.Points.Add(fact);
        Emit(new UiSurface(new LineChart([.. State.Points.Select(p => new Dot(p.X, p.Y))])));
    }
}

// ── fake renderer (the test's second fake, beside a scripted IXClient/IPriceClient) ──
public sealed class FakeRenderer : Neuron, INeuron<UiSurface>;   // hearing IS the render proof

// ── the owner's utterance becomes one journaled fact, WIRED BEFORE WATCHING:
//    the emitter name is learned from journals; the target name is minted here ──
var session = brain.Session("owner");
await session.SendAsync(new NeuronId("xaccount", "elonmusk"),
    new Connect("xpost", new NeuronId("btcdotonxpost", "dashboard")), ct);
await session.SendAsync(new NeuronId("xaccount", "elonmusk"), new WatchAccount(), ct);
```

The flow: `Connect` lands in `xaccount/elonmusk`'s journal (heard, table row committed
atomically) — wiring precedes watching, so no pre-connection XPost ever fans out to a
ghost → `XPost` said with `to:[{btcdotonxpost/dashboard, via:connected}]` and no
`btcdotonxpost` instance at "elonmusk" (the ghost rule, asserted from its absent journal)
→ the behavior asks the price (the `BtcPriceFeed` answerer at "dashboard"), continues,
says `ChartPoint` → `chart/dashboard` hears by declaration, says `UiSurface` → the fake
renderer hears by declaration. `Disconnect` removes the connected route at the next
emission — and because the kind still declares `INeuron<XPost>`, the journaled outcome
states the declared same-context route survives (asserted, honestly). `Connect("xpots",
…)` is refused with a journaled `ConnectionRefused`. The zero-receiver case is asserted
in a composition variant without the behavior module: `XPost` journals with `to: []` and
delivers nowhere. The entire causal chain — utterance to pixel — reconstructs from
journal lines alone.

---

## 11 · BDD coverage — the definition of done

Two test kinds only (`NeuronTest` / `DigitalBrainTest`), public API only, one real cluster
per composition, controllable time, fault injection via the ported testing package. A
capability without its scenario does not exist.

**Feature: Atomic turn**
- A thrown handler leaves no journal entries, no state change, no watermark advance, no
  outbox progress; redelivery converges; the sender journals terminal `DeliveryFailed`
  after the bounded retry.
- A cancelled handler is an ordinary throw (zero trace).
- A handler mutating `State` then throwing leaves the committed slot byte-identical.
- Emissions snapshot at the verb: mutating the emitted record object after `Emit` changes
  neither journal nor delivery.

**Feature: Commit faults (sticky journal-commit faults + armed-fault leak detection)**
- A failed commit poisons the activation: the next delivery is refused, reactivation
  reloads committed truth, redelivery converges, and no uncommitted emission ever reached
  any receiver.
- A failed **drain** commit poisons identically (timer-swallowing proof).
- Ambiguous commit (write landed, ack lost): redelivery is swallowed by the watermark —
  no duplicate turn.
- The successor of v1's shrunk-bound eviction test: GIVEN `watermark[S]=3` with a sequence
  gap, WHEN S delivers seq 7 and the commit faults, THEN a fresh activation reads
  watermark 3 and the redelivered seq 7 journals exactly once. Variants: first-contact
  (no entry), independence (another source's commit unaffected).

**Feature: Dedup**
- A redelivered `(Source, Sequence)` acks as success without a second turn.
- Fan-in past any bound: no capacity failure exists (the window's 4096 disease is
  inexpressible — asserted by scenario, not by absence).
- Watermark pruning: a duplicate within RetryHorizon is still rejected after pruning of
  idle sources (shrunk retention).
- Split-brain fencing: a stale activation's commit is refused by storage (two-writer test
  against the fencing test provider).

**Feature: Connections**
- Connect → emit → deliver (provenance `connected` in the said entry); Disconnect stops
  delivery on the next emission; changes take effect without redeploy.
- The ghost rule: with a connection for F→K, no K instance materializes at the emitter's
  context; after Disconnect, if the catalog still routes F to K at that context, the
  journaled outcome says the declared route survives.
- `Connect` with a typo'd factKind / unknown kind / non-declaring target / question type
  is refused: table untouched, `ConnectionRefused` delivered to the requester.
- Duplicate Connect is idempotent (set semantics); Disconnect of a missing row is a
  journaled no-op.
- Zero-receiver emission journals `to: []` and delivers nowhere.
- Connection to a kind removed by redeploy: `DeliveryFailed` on attempt 1, no horizon
  burn, no FIFO blockade of other receivers.
- Connections survive restart and ride the compaction `ResetSnapshot`.

**Feature: Ask / Answer**
- Round trip with the reply arriving after the asking call returned (poll observes the
  journal; the answer is matched by `Answers`, never by reply type).
- Deferred answer: the answerer returns null, replies three turns later; the asker's
  continuation receives the typed `Answer<Q,R>`; the edge task completes.
- A second concurrent ask of the same kind at a deferring answerer is refused in-turn and
  retried from the sender's outbox (backpressure), expiring as `AskExpired` if never
  accepted.
- `Ask` from a kind not declaring `INeuron<Answer<Q,R>>` throws in-turn naming the
  missing declaration; `Emit(question)` is announce-only (no answerer delivery, no open
  ask).
- Ask with zero answerers in the composition journals immediate terminal
  `DeliveryFailed(no-answerer)`; `AskAsync` surfaces `AskFailedException`.
- Reply-type impersonation: a connected non-answerer fact with a matching Cause does NOT
  dispatch a continuation (anti-fabrication conjunct).
- Question-shape drift (fingerprint mismatch) yields a journaled terminal record, never a
  fabricated `Answer.Question`.
- AskExpired: reply lost beyond the horizon → `AskExpired` journaled, compaction pin
  released, late reply journals as plain reception and dispatches nothing.
- Crashed-and-restarted edge: the full round trip reconstructs from the session journal
  alone.

**Feature: Restart survival**
- Journals, outbox backlog, watermarks, connections, schedules and ask-pins survive
  deactivation and silo restart in one batch; pending deliveries resume via the reminder
  wakeup with zero inbound traffic (kill between commit and first dispatch attempt).

**Feature: Ordering**
- Per-sender-per-receiver order preserved across retries, drain passes, and a
  reminder-drain racing a timer-drain.
- The abandonment barrier: a receiver is unblocked only after the terminal record's
  commit; crash injected between wire-delivery of a later fact and the abandonment commit
  — the poison fact is either handled or terminally recorded, never neither.

**Feature: Boot refusals (each message asserted verbatim)**
- Kind collision; factKind collision; two answerer kinds; a kind declaring both
  interfaces for one question; abstract-TFact declaration; generic synapse type;
  unsealed concrete fact; `INeuron<Answer<Q,R>>` with no answerer in the composition;
  an **answerer** interface with neither member overridden; a module declaring
  `INeuron<>` for a reserved kind (`Connect`/`Disconnect`/`Schedule`/`Unschedule`);
  `TState` with `required` members; codec-unresolvable vocabulary; module
  keyed-`IDurable*` resolution; module `IRemindable`/extra grain interface; catalog
  fingerprint mismatch at silo join.

**Feature: Flow guarantees (the FLOWS.md-side "a flow without its test does not exist")**
- Flow 2: adding a listener module changes no existing code — the new module's journal
  holds the reception with the emitter as source; removing it changes nothing else.
- Flow 5: `BriefingReady` fires exactly once under permuted answer arrival; a restart
  mid-gather still completes from the committed slot.
- Flow 6: the overhearing module's journal mirrors the ask and reply traffic with
  provenance; removing it changes nothing else.
- Flow 8: two contexts interleave asks; each context's journals contain only its own
  conversation.
- Flow 10: `Brain.ReadAsync` returns bodies + connections for a named neuron; the
  introspection assertion derives its answer from the same journals it is checked
  against.
- Edge read isolation: arm a commit fault on the session's reply-reception turn while a
  poll runs concurrently — the poll never surfaces the retracted entry
  (committed-count watermark).

**Feature: Schedule**
- `Schedule` → ticks arrive as self-sourced turns with `Cause` = the schedule entry;
  `Unschedule` stops them; both survive restart; N consecutive tick failures journal
  `ScheduleFailed` and unschedule — never silent.

**Feature: The north-star** (as §10 ★, end-to-end with fake ingestion + fake renderer)
- The full pipeline delivers, Connect-before-Watch; no `btcdotonxpost` instance exists at
  "elonmusk" (the ghost rule, asserted by its absent journal); `Disconnect` removes the
  connected route and the journaled outcome honestly states the declared route survives;
  the typo'd `Connect` is refused with `ConnectionRefused`; the composition variant
  without the behavior module journals `XPost` with `to: []`; the emitter instance name
  is derived from journals, not literals.

**What a DigitalBrainTest looks like** (the Testing package's contract, v1 lineage — one
complete example so no scaffold line is ever invented):

```csharp
public sealed class NorthStarTests(BrainCluster cluster) : DigitalBrainTest(cluster)
{
    protected override void Compose(DigitalBrainTestBuilder brain) => brain
        .AddModule<XAccount>().AddModule<BtcPriceFeed>().AddModule<BtcDotOnXPost>()
        .AddModule<Chart>().AddModule<FakeRenderer>()
        .AddService<IXClient>(new ScriptedXClient(post: "gm"))
        .AddService<IPriceClient>(new ScriptedPriceClient(usd: 98123.5));

    [Fact(DisplayName = "An owner utterance wires X to the chart and a post becomes a dot")]
    public async Task PostBecomesDot()
    {
        var ct = TestContext.Current.CancellationToken;
        var session = Brain.Session("owner");
        await session.SendAsync(new NeuronId("xaccount", "elonmusk"),
            new Connect("xpost", new NeuronId("btcdotonxpost", "dashboard")), ct);
        await session.SendAsync(new NeuronId("xaccount", "elonmusk"), new WatchAccount(), ct);

        await Clock.AdvanceAsync(TimeSpan.FromMinutes(1), ct);        // the poll tick fires

        var chart = await WaitForAsync<UiSurface>(new NeuronId("chart", "dashboard"), ct);
        var dot = Assert.Single(Assert.IsType<LineChart>(chart.Root).Dots);
        Assert.Equal(98123.5, dot.Y);

        var ghost = await Brain.ReadAsync(new NeuronId("btcdotonxpost", "elonmusk"), 0, ct);
        Assert.Empty(ghost.Journal);                                  // the ghost rule, proven
    }
}
```

`NeuronTest<TNeuron>` is the same surface scoped to one kind (adds
`NeuronAsync(name)`); `Clock` is the controllable time that refuses to run backwards;
`WaitForAsync<TFact>` polls `Brain.ReadAsync` under the test's timeout; commit faults
arm per neuron via `FailNextJournalCommit(neuron)` handles whose unconsumed faults fail
the test at dispose (leak detection).

**Feature: Journal readability**
- The complete causal chain of the north-star — owner utterance to rendered surface —
  reconstructs from journal lines alone (`From`/`Cause`/`Answers`/`To`), across five
  journals, with no out-of-band data.
- A journal containing a kind whose module is unloaded: activation succeeds, reads return
  `Body: null` entries, tallies and snapshot intact.

---

## 12 · Flagged for owner ratification (amendments to ratified docs)

1. **OS.md**: delete the same-turn ride-back sentence ("A same-turn reply may ride back…")
   — proven unsound under watermark dedup. The envelope grows `Cause` + `Answers` (both
   Core-stamped; `SynapseMetadata` is now five fields) and "causation = adjacency"
   becomes "causation = the explicit `Cause` field; adjacency remains a readable
   nicety". Add the well-known-name convention sentence: a kind adopting a well-known
   instance name opts out of locus-rule fan-in and is reached by connection only.
   Until these land, OS.md misleads on exactly these points — it should carry a
   "superseded in part, see CORE-DESIGN §12" stamp the day this design is ratified.
2. **FLOWS.md flow 1**: "boot fails loudly if zero or two kinds answer" — the *two* half
   stands; the *zero* half moves to a journaled runtime fact (`DeliveryFailed`
   no-answerer) + static boot evidence via declared continuations. Zero-answerer boot
   failure would force every test composition to drag in every answerer and contradicts
   staged module install. The answerer signature also changes from the flow's
   `Task<Greeted>` to `Task<TReply?>` (null = deferred) with the sync `Answer` surface.
3. **FLOWS.md flow 5**: the join mechanism is the `Neuron<TState>` slot, not a
   module-visible journal read (none exists; the slot is restart-safe and
   order-independent).
4. **FLOWS.md flow 7**: Pulse's timer is Core vocabulary (`Schedule`), not a module's
   private grain timer (turn-only emission + timer exception swallowing force this).
5. **FLOWS.md flow 10**: the Stage-1 securing test reads through the edge
   (`Brain.ReadAsync`); in-brain self-reading (Core question vocabulary such as
   `ReadJournal : Synapse<JournalSlice>`) is deferred to the AI module's stage — the
   "asks the brain what happened" wording amends to the edge-read form until then.
6. **CONTEXT.md**: `Completed` is deleted (no consumer — reception-without-emission
   already records it). `Connection`, `Schedule`, `Watermark` may deserve language
   entries.
7. **NeuronId reverts to `(string Kind, string Name)`** — the uncommitted `Type`-based
   form on disk cannot be journaled (proven) and physics #5 bans it. Kind minting =
   `NeuronId.KindOf` (lowercased class name), the same function minting fact kinds;
   renames are journal-orphaning events until Revision facts own migration.
8. **Physics #3 rewording** (journals = communication truth; state = readable
   consequence) — as §1.3 above.
