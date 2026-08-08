# DigitalBrain Core — architecture (2026-08-08, v3 — post-panel)

Two concepts. One verb. A brain is interconnected by **what exists**, not by what is wired
in code. This spec was attacked by a seven-adversary panel (Orleans reality, concurrency,
behavior mechanism, module author, physics consistency, test harness, self-awareness);
all nine proven contradictions and every accepted fix are incorporated below. The paradigm
survived every lens unchanged — only the law text bled.

---

## 1. The paradigm

- **Neuron** — durable, addressable, owner-scoped actor. Declares the synapse kinds it
  handles. Its life is its journal.
- **Synapse** — data type = data contract = data carrier. Immutable record. Journaled.

DigitalBrain is the **compiler/runtime** — registry, routing, journal, discovery,
inspection, later the self-improvement rail. Not a concept. Only things that handle
synapses are neurons; time, models, transports, storage are services a constructor asks for.

## 2. Interconnection — "when Musk posts do X, when Vlad posts do Y"

No wiring registry, no subscription API, no connection objects. Three mechanisms:

1. **Kinds declare, instances exist.** `IHandle<XPosted>` on a compiled kind puts its
   `"default"` instance in the receiver set. Creating an instance is wiring — and creation
   is itself a **journaled fact**: the registry observes creations, never bare activations.
2. **Selection is domain data.** `XPosted(Author, Text)` carries the author because the
   domain cares. The runtime routes by exact type; the behavior selects by data.
3. **A behavior is a neuron whose definition is durable state.** Compiled behaviors are
   classes; runtime-created behaviors are named instances of **one compiled kind,
   `Behavior`**, created and changed by conversation, not deploys.

```
x:elonmusk fires XPosted("elonmusk", "doge to the moon")      [broadcast fact]
behavior:musk-chart   Where Author == "elonmusk"  → fires ChartPointRequested at chart:btc
behavior:vlad-notes   Where Author == "vlad"      → fires NoteFiled at notes:default
chart:btc answers ChartPointRequested with ChartPointAdded    [directed, typed result]
```

### The behavior mechanism, concretely

The definition is closed, serializable data — never a script (Gate-0 law):

```csharp
record BehaviorDefinition(
    string OnSynapse,                        // alias of the fact kind to react to
    IReadOnlyList<FieldCondition> Where,     // selection over the triggering fact
    IReadOnlyList<ActionStep> Fire);         // steps, in order

record FieldCondition(string Field, ConditionOp Op, string Value);
enum ConditionOp { Equals, NotEquals, Contains, GreaterThan, LessThan }   // closed set

record ActionStep(
    string SynapseAlias,
    string? Target,
    IReadOnlyList<FieldCondition> Where,     // step guard over trigger ∪ prior step results
    IReadOnlyDictionary<string, ValueSource> With);
// ValueSource = literal | field of the triggering fact | field of a prior step's result
```

Step guards make judgment flows expressible: *monitor the dev blog* = clock fact →
step 1 fires `Assess(text)` at the assistant (directed, typed result) → step 2 fires
`IssueProposed` **Where** `steps[1].Relevant == true`. The data tier wires; neurons think.

**Creation and lifecycle are flows, not APIs.** `DefineBehavior(definition)` is a directed
synapse handled by the target instance: it validates (unknown alias, field, target, or an
ambiguous alias ⇒ typed refusal in the answer), persists the definition, and answers
`BehaviorDefined` **only after** the interest `(owner, OnSynapse → instance)` is durably
committed in the registry — write-through, so recovery is the registry replaying its own
journal, and a restart can never leave a behavior silently deaf. Redefining replaces (the
journal keeps every prior definition — versioning for free); `DisableBehavior` withdraws
the interest through the same write-through; listing rides `Discover`. Definitions are
stamped with the field shape of every alias they use — a fire-time mismatch after a
redeploy journals a typed `BehaviorInvalidated` and withdraws the interest until redefined.
After N consecutive retracted turns the runtime journals `BehaviorSuspended` and withdraws
the interest — loud, typed, recoverable. Nothing about a behavior's death is telemetry-only.

**Delivery — and the direct answer to "Orleans streams and implicit subscriptions?": no.**
Memory streams *drop* broadcasts for subscribers not active at publish time — fatal
precisely for behaviors born at runtime — and implicit subscriptions bind per kind, not
per instance interest. Instead the committed outbox entry carries the fact, and **the
drain expands the receiver set at delivery time** from the registry (compiled declarations
∪ journaled instance interests): the registry sits outside the atomic commit path,
retries ride the drain, duplicates are watermark-acked, and a behavior defined between
commit and drain hears even the in-flight fact. The refused alternative — an interpreter
kind declaring a catch-all `IHandle<Synapse>` — would multiply the durable outbox by the
behavior count to deliver facts selection would discard.

### The code tier — how scripting introduces new neurons

When intent needs **new vocabulary or logic** — not composition of existing contracts —
the trust ladder climbs three rungs. Every rung is a journaled flow, and the same
TestBrain harness that proves Stage A is the activation gate for generated code — one
gate, law 10.

1. **Compose** (instant, no code): `BehaviorDefinition` above. Always tried first.
2. **Interpret** (seconds, the running silo): a scripted body executes inside the one
   compiled interpreter machinery, addressable under a new neuron name via runtime
   manifest decoration — proven live in the lineage (new names addressable with zero
   restart; staged → promoted → retired lifecycle with rollback; loop-guard + watchdog
   around untrusted code). New vocabulary at this rung rides the compiled **carrier
   record** (contract id + schema-stamped payload) — a visible seam, priced consciously:
   no `IHandle<HotType>`, schema versioning lives in data.
3. **Compile** (minutes, real types): the Creator authors typed C# — records + neuron +
   **its own scenario** — compiled *with the Orleans source generator* so the new records
   carry their own serializers; the pack loads into a collectible ALC inside a
   **disposable per-candidate quarantine brain** (TestBrain in production clothes — v3's
   proven gate pattern), runs its scenario red → green, and green journals the approval
   fact. Admission into the product is then one of exactly two doc-honest moves: **a new
   silo joins carrying the pack** (heterogeneous silos — the brain grows new tissue as
   new silos, an orchestration Aspire already owns) or **restart-and-load** (the floor —
   durable journals make a restart a pause, never a loss). Orleans has no documented way
   to grow a *running* silo's compiled type set; this spec does not pretend otherwise.

Evidence grades, so nobody inherits theater: manifest-decoration admission and the
Creator lifecycle are PROVEN-LIVE (single silo); the quarantine-gate mechanics are
PROVEN-LIVE except two links — running the Orleans generator over runtime-compiled code,
and asserting the gate ALC actually collects after disposal — open items 6 and 7, each a
one-day spike with a crisp pass/fail. Until both pass, rung 3 ships restart-tier only.

## 3. The ABI

```csharp
// ── The two concepts ────────────────────────────────────────────────────────
public abstract record Synapse;                       // broadcast fact, one-way

public abstract record Synapse<TResult> : Synapse     // directed request; the contract
    where TResult : Synapse;                          // declares its typed result

public interface IHandle<TSynapse> where TSynapse : Synapse
{
    Task HandleAsync(TSynapse synapse, CancellationToken cancellationToken);
}

public interface IHandle<TSynapse, TResult>
    where TSynapse : Synapse<TResult> where TResult : Synapse
{
    Task<TResult> HandleAsync(TSynapse synapse, CancellationToken cancellationToken);
}
// No variance: receiver matching is exact-type, never assignability — declared variance
// would promise conversions the registry will not honor.

// ── The neuron base — one vocabulary member ─────────────────────────────────
public abstract class Neuron : DurableGrain
{
    public NeuronId Id { get; }                       // (Owner, Kind, Name)
}
// The wire is runtime-owned and invisible: INeuron { Task<Synapse?> Deliver(SynapseDelivery, ct); }
// implemented explicitly and SEALED on Neuron — modules cannot intercept or re-implement
// delivery. TResult is compile-checked at the fire and runtime-checked at the wire.
// DurableGrain's inherited surface (ServiceProvider, GrainFactory) is machinery, never
// vocabulary: the architecture guard forbids module code referencing either, or INeuron.

// A neuron that fires declares the dependency like any other service:
//
//   class MuskChart(IDigitalBrain brain) : Neuron, IHandle<XPosted>
//   {
//       public async Task HandleAsync(XPosted post, CancellationToken ct)
//       {
//           if (post.Author != "elonmusk") return;
//           await brain.FireSynapse(new ChartPointRequested(post.Text), ct);
//       }
//   }

// ── The facade — one verb, plus reading the brain ───────────────────────────
public interface IDigitalBrain
{
    OwnerId Owner { get; }

    Task FireSynapse(Synapse synapse, CancellationToken ct = default);
    Task<TResult> FireSynapse<TResult>(Synapse<TResult> synapse,
        TimeSpan? deadline = null, CancellationToken ct = default)   // default deadline lives on the verb
        where TResult : Synapse;

    Task<IReadOnlyList<NeuronContract>> DiscoverAsync(string query, int limit = 8, CancellationToken ct = default);
    Task<JournalRead> ReadJournalAsync(NeuronId subject, JournalKind kind, long after = 0, CancellationToken ct = default);
    Task<CausalChain> TraceAsync(CorrelationId correlation, CancellationToken ct = default);
    Task<JournalRead> RecallAsync(RecallQuery query, CancellationToken ct = default);
    IAsyncEnumerable<JournalRead> WatchAsync(NeuronId subject, JournalKind kind, long after = 0, CancellationToken ct = default);
}

// Directed fire targets instance "default" unless the synapse carries its address:
public interface IAddressed { string Neuron { get; } }
// An addressed fire SELECTS among journaled identities; it never mints one. A directed
// fire at a never-created named instance fails typed ("no such instance") — only
// "default" exists by declaration.
```

**The envelope** — runtime-stamped, journal-visible, never author-touched:
`SynapseDelivery(Id, Correlation, Causation, Source, Origin, Sequence, Timestamp, Synapse)`
where `Origin ∈ { Owner, Neuron }` — edge fires are owner-attributed, so journal exports
carry roles mechanically. An edge fire lands on the **owner's session neuron** — a neuron
like any other; it is the envelope Source and the fact's journal locus.

**Contract cards.** Every synapse contract statically carries its registry card: alias
(derived from the namespace-qualified type name, unique per registry, round-trips to
exactly one type — *identity is the qualified name, the alias is derived, never a lookup
result*), one-line description, example utterances. `NeuronContract` = the card + request
and result field schemas derived from the compiled records by the source-generated
manifest (never hand-written) + observed instance names. Discovery quality is a Stage A
scenario, not a hope.

**Protected fields.** Payloads are journal-visible unless a record field is declared
protected, which journals a **reference**; the plaintext lives behind the payload
protector (runtime machinery folded into Core). Gmail bodies never enter journals or
training exports in the clear.

**External authorization is a flow, not machinery.** An auth neuron owns pending states
and journals `AuthorizationRequired` / `AuthorizationCompleted` as facts; a parkable
request's *result record* carries a typed `AuthorizationRequired` branch (sign-in URL),
with the resume key as caller-supplied domain data on the request record; the caller —
assistant or a behavior selecting on `AuthorizationCompleted` — re-fires the request on
hearing the completion fact. The browser callback is an edge fire like any other.

**A module is:** synapse records + neuron classes + one composition entry (the existing
`IModule` seat) registering the services its neurons' constructors ask for and the
module's host resources. Nothing else exists to write — no verbs, no manifests, no wiring.

## 4. The ten laws

1. **Two concepts.** Not a neuron or a synapse ⇒ runtime machinery, never vocabulary.
2. **One verb, one place.** `FireSynapse` exists only on `IDigitalBrain`; neurons fire
   through their injected brain. Routing lives in the contract: `Synapse` broadcasts,
   `Synapse<TResult>` directs and returns. The registry is invisible.
3. **The DAG law.** Directed calls flow down; results return only as return values; facts
   flow one way. The chain check refuses every cycle it can see (including self-fire)
   before the call; chains are depth-bounded; **the chain dies at commit** — correlation
   and causation persist through the outbox, the chain does not, so a drained broadcast
   roots a fresh chain. Every directed fire carries a deadline; expiry is a typed,
   journaled failure — deadlock degrades to typed failure, never a hang, never interleave.
4. **Broadcast is one-way, journal-first, at-least-once.** Receiver set — one formula:
   the `"default"` instance of every kind compiled with `IHandle<T>` ∪ every named
   instance whose journaled interest names T, matched by exact type. Expansion happens at
   **drain time**, outside the atomic commit path. Instance creation is wiring and is
   itself a journaled fact. Zero receivers is legal; duplicates are silently acked
   (per-source watermark). Streams are refused as carrier.
5. **The journal is the only truth — and truth never truncates silently.** One
   interleaved journal per neuron (`JournalKind` is a read filter, not a second feed);
   directed calls enter the same timeline via the runtime's call filter; every commit also
   appends to a per-owner, day-keyed timeline on the brain (the "last Friday" substrate).
   A feed compacts only behind a journaled, replayable archive — the retained window is a
   cache; `Read`/`Recall` span both. Streaming deltas are an ephemeral, non-durable
   projection of a directed fire in progress; nothing may be asserted from them. The
   journal is the **audit floor**: nothing claimed without an entry — not a claim that the
   world was reversed.
6. **The atomic turn is per neuron.** Staged broadcasts and state commit or retract
   together *at this neuron*; staged facts are invisible to everyone — including directed
   callees — until commit. A directed fire is the **callee's own committed turn**: the
   caller's retraction cannot unmake it and journals the orphaned request as a typed fact.
   External effects are at-least-once under redelivery — idempotency keys are domain data
   on the synapse. A fire is accepted only on the activation's scheduler: in an open turn
   it joins that commit; in grain code outside a turn (activation, reminder, timer) it
   opens its own; off-scheduler it fails typed.
7. **The registry is observed, never computed.** Truth = compiled `IHandle<>`
   declarations + journaled instance interests (written through at define-commit, never
   scanned) + contract cards. The source-generated manifest accelerates; degradation is loud.
8. **Activation identity is declared.** The kind by contract; the name by a journaled
   creation fact; an addressed fire selects among declared identities and never mints one.
   Never correlation-keyed.
9. **Failures are typed and journaled.** No handler, no such instance, ambiguity, denial,
   timeout, cycle, suspension — typed to the caller, facts in the journal. The directed
   plane is **at-most-once per fire**, request and result on one correlation; a caller
   that re-fires after a timeout recalls the receiver's journal first or carries a domain
   attempt key — retries are never the runtime's job. Silent drops do not exist.
10. **Gates must be able to fail.** Unprovable = skipped-with-reason, never green; red
    features enter behind `@ignore("pending: law N")` and each runtime commit's first
    change is the un-ignore — the root gate is never red, and no gate is theater.

## 5. BDD-first — the proof suite is the specification

**Order of work: harness → features red (ignored-with-reason) → runtime green, law by law.**
Framework: **Reqnroll** over a real TestCluster (the lineage standard — `final/`'s
Os.Tests is the reference MTP wiring). Commit #1 pins the stack it stands on:
Reqnroll.xunit.v3 + an xunit.v3 version proven by compiling one generated feature under
the Microsoft.Testing.Platform gates this repo uses, shim carried into TestBrain.

**TestBrain** (shaped like ino's session verbs, implemented over the TestCluster brain):
- `s.Fire(fact)` / `await s.Get(request)` / `s.Journal(neuron)` / `s.Receivers<T>()`
  (registry read) / `s.Quiesced(neuron)` (outbox drained) / `s.Surface(panel)`;
- **three physics seams**: a restart-surviving journal store (process-static provider
  covering feeds, outbox, and watermarks), a delivery gate (an incoming filter holding a
  named synapse kind undelivered — "committed but undelivered" as a stable state), and
  silo restart + reactivation choreography;
- deterministic assertions only: typed results + journal tallies + receiver-set
  enumeration — never wall clocks, never unbounded negatives. Hangs are caught by the
  harness-level test timeout, not Gherkin.

Stage A's surface vocabulary is one record — `SurfacePresented(string Panel, Synapse
Content)` — fired like any fact; the full RFW widget vocabulary stays Stage D. UI changes
are journal assertions with no renderer running.

```gherkin
Feature: Physics
Scenario: A failing handler retracts its whole turn
  Given a Diary neuron whose handler stages Noted and then throws
  When Remember is fired at diary:default
  Then diary:default's outgoing journal has no Noted
  And every receiver of Noted journals zero Noted deliveries

Scenario: A caller that fails after a directed result journals the orphan
  Given a handler that gets a ChartPointAdded result and then throws
  Then chart:btc's turn stays committed
  And the caller journals the orphaned request as a typed fact

Scenario: A broadcast survives a crash between commit and delivery
  Given the delivery gate holds Greeted undelivered
  And the silo restarts
  Then every receiver journals Greeted exactly once after reactivation

Feature: Routing
Scenario: A directed synapse returns its typed result
  When GmailSearch "in:inbox" take 3 is fired
  Then the caller holds a GmailResult with 3 messages
  And gmail:default journals request and result on one correlation

Scenario: A cycle is refused before the callee runs
  Given A's handler fires directed at B, and B's handler fires directed back at A
  When the chain is fired
  Then the fire fails typed as a cycle
  And b:default journals exactly one delivery

Scenario: Cross-chain contention degrades to typed timeout, never a hang
  Given A awaits a directed fire at B while B awaits a directed fire at A
  Then both fires fail typed within their deadlines and both failures are journaled

Feature: Interconnection
Scenario: Two watchers, two behaviors, zero shared code
  Given behavior musk-chart selecting XPosted Where Author Equals "elonmusk"
  And behavior vlad-notes selecting XPosted Where Author Equals "vlad"
  When XPosted "elonmusk" "doge to the moon" is fired
  Then chart:btc journals ChartPointRequested
  And notes:default journals zero NoteFiled deliveries

Scenario: A behavior defined after commit still hears the in-flight fact
  Given the delivery gate holds XPosted undelivered
  When behavior musk-chart is defined and the gate releases
  Then chart:btc journals ChartPointRequested

Scenario: A misfiring behavior is suspended loudly, and a redefine revives it
  Given a behavior whose action alias no longer matches its stamped shape
  Then the instance journals BehaviorInvalidated and its interest is withdrawn
  When DefineBehavior replaces the definition
  Then the next matching fact is handled

Feature: Inspection
Scenario: The brain answers what happened
  Given the musk-chart flow has run once
  Then Trace of the post's correlation lists XPosted, ChartPointRequested, ChartPointAdded in order

Scenario: The brain answers "what did you do on Friday"
  Given facts from three neurons committed on Friday
  Then Recall From Friday To Saturday for the owner lists them across neurons in order

Scenario: Discovery finds the doer from the intent
  Given the Gmail module's contract cards are registered
  When Discover "summarize my last three emails" runs
  Then the GmailSearch contract is the first result

Feature: Surface
Scenario: A UI change is a journal assertion
  When chart:btc handles ChartPointRequested
  Then SurfacePresented for panel "chart:btc" carries the new point
  And no renderer was running
```

Remaining scenarios, named to fix scope: duplicate-delivery ack; ambiguous / absent /
no-such-instance / busy-timeout typed failures; broadcast with zero receivers completes;
journals survive restart byte-for-byte; registry-equals-journal observation; directed call
on the shared timeline; staged-fact invisibility to directed callees; DefineBehavior
refusals (unknown alias/field/target, ambiguous alias, step consuming a prior broadcast);
DisableBehavior; architecture guard (every synapse serializable + aliased + unique alias;
directed kinds declared only via `IHandle<T,R>`; no module symbol references
`GrainFactory`, `ServiceProvider`, or `INeuron`; behavior definitions are closed data).

Stage C acceptance (fixed now): Gmail last-3 summary via the official Gmail MCP and
Salesforce profile via hosted MCP SOQL — driven through discover/fire/journal only
(NeuronContract schemas are the named precondition), plus this Surface feature re-run
against the real shell.

## 6. Stages and demolition

- **A (this pass):** TestBrain (three seams, pinned stack) → features red behind
  `@ignore` → §3 ABI + runtime green beside the existing surface, law by law. Kept
  physics: atomic turn, outbox drain, watermark dedup, session locus, owner filter.
  Includes the per-owner day-keyed timeline (same call-filter write path as Trace).
- **B:** all 12 modules + shell migrate; demolition as green commits — capability
  interfaces, reification filters, second turn machine, poll loops, hand manifests,
  `IEmit`, verb zoo, phantom Neuron surface, Guid-ring dedup, ResourceNames out of
  Abstractions; the Security project **folds into Core** (the payload protector is law-5
  machinery, not demolition). Two named gates: every re-based synapse kind round-trips a
  pre-migration journal fixture (or its feed re-encodes under pinned aliases before
  cutover), and the journal archive tier lands before any feed's window is the only truth.
  Token streaming survives explicitly as law 5's ephemeral delta projection over the
  existing SSE surface. Streams/pubsub resources: removed (no consumer under law 4).
- **C:** the two live flows (A1–A3 above).
- **D:** the code tier's rungs 2–3 (§2), gated on open items 6–7; full RFW surface
  vocabulary; vector-backed discovery over the same contract cards.

## 7. Open items — each resolves against the compiler in Stage A

1. `Synapse<TResult>` closed-generic serialization (leaf `[GenerateSerializer]` + alias
   policy) — commit #2, immediately after the harness.
2. `NeuronContract` / `CausalChain` / `RecallQuery` record shapes — content committed in
   §3, shapes fixed by their first red scenario.
3. Delivery-context access (`CurrentDelivery` / authorization hook): not in the ABI;
   added only if a Stage B consumer proves undeniable, as a handler-parameter overload,
   never ambient state.
4. `BehaviorDefinition` validation completeness: refuse a step consuming state reachable
   only through a prior broadcast (law 6 visibility); invariant-culture ordinal
   comparisons; result-field references only on `Synapse<TResult>` aliases.
5. The xunit.v3 pin that Reqnroll's generator provably compiles against under MTP —
   verified before any feature is written, per §5 commit #1.
6. **Generator-in-pack spike** (gates code-tier rung 3): drive the Orleans source
   generator inside a Roslyn compilation of a `[GenerateSerializer]` record, ALC-load the
   output, fire the record across a 2-silo `InProcessTestCluster`, assert the committed
   journal round-trip.
7. **Unload spike** (gates rung 3 in-process): after quarantine-cluster disposal, a
   WeakReference/GC loop proves the gate ALC collects; failure means an Orleans-held root
   to diagnose before rung 3 ever runs in-process — a leak here is permanent by design.
