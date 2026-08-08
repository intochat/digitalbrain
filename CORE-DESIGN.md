# DigitalBrain Core — architecture (2026-08-08, v2)

Two concepts. One verb. A brain is interconnected by **what exists**, not by what is wired
in code. This document is architecture, not a prototype: every member below survives only
because a law or a scenario forces it, and the proof suite is written **before** the runtime.

---

## 1. The paradigm

- **Neuron** — durable, addressable, owner-scoped actor. Declares the synapse kinds it
  handles. Its life is its journal.
- **Synapse** — data type = data contract = data carrier. Immutable record. Journaled.

DigitalBrain is the **compiler/runtime** — registry, routing, journal, discovery,
inspection, and later the self-improvement rail. It is not a concept. Only things that
handle synapses are neurons; everything else (time, models, transports, storage) is a
service a neuron's constructor asks for.

## 2. Interconnection — how "when Musk posts, do X; when Vlad posts, do Y" works

There is no wiring registry, no subscription table, no connection objects.
**Three mechanisms compose every flow in the system:**

1. **Kinds declare, instances exist.** `IHandle<XPosted>` on a kind puts it in the
   broadcast receiver set. Creating an *instance* is the only wiring act — a behavior
   exists, therefore it hears.
2. **Selection is domain data, not routing.** `XPosted(Author, Text)` carries the author
   *because the domain cares*. The runtime routes by type; the behavior selects by data.
   Musk-vs-Vlad is a `record` field, not an infrastructure feature.
3. **A behavior is a neuron whose definition is durable state.** Compiled behaviors are
   classes; runtime-created behaviors are *instances of one interpreter kind* whose state
   says what to select and what to fire. Creating one is a conversation, not a deploy.

The scenario, concretely — every arrow is a journaled synapse:

```
watcher x:elonmusk ──fires──> XPosted("elonmusk", "doge to the moon")     [broadcast fact]
watcher x:vlad     ──fires──> XPosted("vlad", "shipped the kernel")       [broadcast fact]

behavior:musk-chart   state: { when: XPosted.Author == "elonmusk",
                               fire: ChartPointRequested at chart:btc }
behavior:vlad-notes   state: { when: XPosted.Author == "vlad",
                               fire: NoteFiled at notes:default }

both behaviors hear both facts (they declare XPosted); each acts on its own selection;
chart:btc answers ChartPointRequested with ChartPointAdded — which IS its UI update.
```

Nothing was deployed. Two `XPosted` facts, two behavior instances, one directed
request/result — the whole flow readable back from journals, one correlation per post.
The same three mechanisms carry "monitor the dev blog at 10:00" (a clock neuron fires
`TimerElapsed` facts; the behavior instance selects on its own schedule id) and every
future automation: **new behavior = new instance = new data.**

## 3. The ABI — nothing that a law does not force

```csharp
// ── The two concepts ────────────────────────────────────────────────────────
public abstract record Synapse;                       // broadcast fact, one-way

public abstract record Synapse<TResult> : Synapse     // directed request; the contract
    where TResult : Synapse;                          // declares its typed result

public interface IHandle<in TSynapse> where TSynapse : Synapse
{
    Task HandleAsync(TSynapse synapse, CancellationToken cancellationToken);
}

public interface IHandle<in TSynapse, TResult>
    where TSynapse : Synapse<TResult> where TResult : Synapse
{
    Task<TResult> HandleAsync(TSynapse synapse, CancellationToken cancellationToken);
}

// ── The neuron base — three members, complete ───────────────────────────────
public abstract class Neuron : DurableGrain
{
    public NeuronId Id { get; }                       // (Owner, Kind, Name)

    protected Task FireSynapse(Synapse synapse, CancellationToken ct = default);
    protected Task<TResult> FireSynapse<TResult>(Synapse<TResult> synapse, CancellationToken ct = default)
        where TResult : Synapse;
}
// Everything else — time, models, MCP clients, durable state slots — arrives by
// constructor injection. The base class carries no service a handler can ask for.

// ── The facade — same verb, plus reading the brain ──────────────────────────
public interface IDigitalBrain
{
    OwnerId Owner { get; }

    Task FireSynapse(Synapse synapse, CancellationToken ct = default);
    Task<TResult> FireSynapse<TResult>(Synapse<TResult> synapse, CancellationToken ct = default)
        where TResult : Synapse;

    Task<IReadOnlyList<NeuronContract>> DiscoverAsync(string query, int limit = 8, CancellationToken ct = default);
    Task<JournalRead> ReadJournalAsync(NeuronId subject, JournalKind kind, long after = 0, CancellationToken ct = default);
    Task<CausalChain> TraceAsync(CorrelationId correlation, CancellationToken ct = default);
    Task<JournalRead> RecallAsync(RecallQuery query, CancellationToken ct = default);
    IAsyncEnumerable<JournalRead> WatchAsync(NeuronId subject, JournalKind kind, long after = 0, CancellationToken ct = default);
}

// Directed fire targets instance "default" unless the synapse carries its address:
public interface IAddressed { string Neuron { get; } }
```

The envelope (`SynapseDelivery`: id, correlation, causation, source, sequence, timestamp)
is runtime-stamped and journal-visible; authors never construct or read it in handlers —
lineage questions are answered by `Trace`/`Recall`, not by ambient state.

A module is: synapse records + neuron classes. Nothing else exists to write.

## 4. The laws

1. **Two concepts.** Not a neuron or a synapse ⇒ runtime machinery, never vocabulary.
2. **One verb.** Routing lives in the contract: `Synapse` broadcasts, `Synapse<TResult>`
   directs and returns. The registry is invisible.
3. **The DAG law.** Directed calls flow down; results return only as return values;
   facts flow one way. The runtime refuses a fire that would re-enter a turn already in
   the call chain — typed failure before the call, never a deadlock, never interleaving.
4. **Broadcast is journal-first, at-least-once, never awaited into handlers.** Receiver
   set = the `"default"` instance of every declaring kind + every observed named instance:
   *instance creation is wiring*. Zero receivers is legal. Duplicates are silently acked
   (per-source watermark). Streams are refused as carrier.
5. **The journal is the only truth.** Both planes on one timeline (directed calls entered
   by the runtime's call filter). Telemetry is a projection. Unjournaled effects did not
   happen — including failures.
6. **The atomic turn.** Incoming synapse, emissions, state: one commit or full retraction.
7. **The registry is observed, never computed.** Truth = `IHandle<>` declarations + live
   instances; the source-generated manifest only accelerates it; degradation is loud.
8. **Activation identity is declared.** Never correlation-keyed.
9. **Directed failures are typed and journaled.** No handler, ambiguity, denial — typed to
   the caller, facts in the journal. Silent drops do not exist.
10. **Gates must be able to fail.** Unprovable = skipped-with-reason, never green.

## 5. BDD-first — the proof suite is the specification

**Order of work: harness → features red → runtime until green.** Framework: **Reqnroll**
(SpecFlow's continuation — already the lineage standard: `final/` runs Reqnroll over a real
Orleans TestCluster; "extend Gherkin via tags, never invent a DSL" is settled prototype law).

**TestBrain, mined from the prototypes, is Stage A commit #1:**
- one-configurator real TestCluster harness — from IAW's `AgentTest<TAgent>`;
- the session surface `s.Fire(...)` / `await s.Get(request)` / `s.Journal(neuron)` /
  `s.Surface(panel)` — from ino's `NeuronE2ETest` + `s.Chat()/s.Last.Rfw`;
- the honesty ratchet (no tolerant asserts; a scenario that cannot fail is a defect) —
  from `final/`'s GateHonestyTests, and law 10.

**UI is assertable in the same scenarios** — because surfaces are synapses, a UI change is
a journal fact, and no browser is involved (ino proved this shape end-to-end):

```gherkin
Feature: Physics
Scenario: A failing handler retracts its whole turn
  Given a Diary neuron whose handler fires Noted and then throws
  When Remember is fired at diary:default
  Then diary:default journals no Noted
  And no neuron received Noted

Scenario: A broadcast survives a crash between commit and delivery
  Given a Greeter that fires Greeted on hearing Hello
  And the silo restarts after Greeted is committed but before delivery
  Then every declaring listener journals Greeted exactly once

Feature: Routing
Scenario: A directed synapse returns its typed result
  When GmailSearch "in:inbox" take 3 is fired
  Then the caller holds a GmailResult with 3 messages
  And gmail:default journals the request and the result on one correlation

Scenario: A cycle is refused, not deadlocked
  Given neuron A whose handler fires a directed synapse at B
  And B's handler fires a directed synapse back at A
  When the chain is fired
  Then the fire fails typed as a cycle within one second

Feature: Interconnection
Scenario: Two watchers, two behaviors, zero shared code       # §2, executable
  Given a behavior musk-chart selecting XPosted where Author is "elonmusk", firing ChartPointRequested at chart:btc
  And a behavior vlad-notes selecting XPosted where Author is "vlad", firing NoteFiled at notes:default
  When XPosted "elonmusk" "doge to the moon" is fired
  Then chart:btc journals ChartPointRequested
  And notes:default journals nothing

Scenario: A behavior created mid-run hears the next fact
  Given XPosted was already fired once
  When behavior musk-chart is created
  And XPosted "elonmusk" "again" is fired
  Then chart:btc journals exactly one ChartPointRequested

Feature: Inspection
Scenario: The brain answers what happened
  Given the musk-chart flow has run once
  Then Trace of the post's correlation lists XPosted, ChartPointRequested, ChartPointAdded in order
  And Recall LastN 1 OfType ChartPointAdded on chart:btc returns the point

Feature: Surface
Scenario: A UI change is a journal assertion
  Given chart:btc declares its surface
  When ChartPointRequested is handled
  Then the surface synapse for panel chart:btc carries the new point
  And the scenario asserted it without any renderer running
```

Remaining scenarios (same features, listed to fix scope): duplicate-delivery ack,
ambiguous/absent directed handler typed failures, broadcast-with-zero-receivers completes,
journals survive restart byte-for-byte, registry-equals-journal observation, directed call
on the shared timeline, architecture guard (every synapse serializable+aliased; no kind
declares both `IHandle<T>` and `IHandle<T,R>` for one T; behaviors' selection state is
serializable data).

Stage C acceptance (fixed now, executed later): the two live flows — Gmail last-3 summary
via the official Gmail MCP, Salesforce profile via hosted MCP SOQL — driven through
discover/fire/journal only, plus this Surface feature re-run against the real shell.

## 6. Stages and demolition

- **A (this pass):** TestBrain harness → features red → §3 ABI + runtime green beside the
  existing surface (same assemblies, new types; nothing else touched). Kept physics:
  atomic turn, outbox drain, watermark dedup, feeds, session locus, owner filter.
- **B:** all 12 modules + shell migrate; the demolition executes as green commits —
  capability-interface plane, reification filters, second turn machine, poll loops,
  hand-written manifests, `IEmit`, verb zoo, phantom Neuron surface, Guid-ring dedup,
  Security project fold, ResourceNames out of Abstractions, streams/pubsub resources
  (no consumer under law 4).
- **C:** the two live flows. **D:** creation ladder (interpreter tier + Creator green-gate
  toward the hot-install chain), RFW surface vocabulary, vector discovery from registry facts.

## 7. Open items — each resolves against the compiler in Stage A

1. `Synapse<TResult>` closed-generic serialization shape (leaf `[GenerateSerializer]` +
   alias policy) — commit #2, immediately after the harness.
2. `NeuronContract` / `CausalChain` / `RecallQuery` records — shaped by their first red
   scenario, not before.
3. Delivery-context access (`CurrentDelivery` / authorization hook): **not in the ABI** —
   added only if a Stage B consumer (Salesforce approval evidence, task caller gating)
   proves undeniable, likely as handler-parameter overload rather than ambient state.
4. Behavior-definition schema for the interpreter kind (selection + action as data):
   fixed by the Interconnection feature, kept to a closed, serializable shape.
