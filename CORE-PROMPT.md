# Prompt: design the DigitalBrain Core by composing the proven prototypes

You are a principal distributed-systems architect. Your job is to design the CORE of
DigitalBrain OS — a self-programming, self-aware digital brain on Microsoft Orleans —
as a **disciplined composition of capabilities already proven across eight prototypes
in E:\intochat**, not another clean-room invention. This requires enormous
architectural thinking: the Core must be small enough that every line has meaning
(the Abstractions project is the quality reference) yet flexible enough to express
arbitrary behaviors by composing neurons. Everything you design must be covered by
BDD tests; a capability without its test does not exist.

## The north-star behavior (acceptance scenario)

> The owner tells the brain: "when there is a new post on X from elonmusk, add a dot
> to the Flutter chart that shows bitcoin prices."

An ingestion module emits `XPost` facts; a behavior neuron connects the account to a
chart neuron; the chart neuron emits a `UiSurface`/projection fact the Flutter module
renders. **You design only the Core** — but the Core's vocabulary must express this
flow naturally, with ingestion and UI as ordinary modules. Orleans Streams may serve
edge projections (Flutter, telemetry, high-volume ingress) but are never
authoritative neuron-to-neuron delivery.

## Read first

- `CORE-RESEARCH.md`, `OS.md`, `FLOWS.md` in the repo root — prior findings, concept,
  flow catalog.
- The v1 delivery engine (the strongest implementation found; port its guarantees,
  not its surface):
  - dedup + rollback: `v1/src/core/kernel/DigitalBrain.Kernel/Neuron/Neuron.Lifecycle.cs:35`
  - staged output, one commit: `.../Neuron.Turns.cs:75`
  - durable outbox, retrying direct calls: `.../Neuron.Outbox.cs:74`
  - recipient resolution + staging: `.../Neuron.Messaging.cs:8`

## The composition (owner-ratified working hypothesis)

| Source | Take | Leave |
|---|---|---|
| v2 scaffold (`src/`) | three-package target, minimal vocabulary | existing Core code is disposable |
| v1 kernel | atomic journals/outbox, receiver-side SynapseId dedup, at-least-once retry, terminal failed-delivery records, causal metadata, ordered delivery, resumable journal cursors | capability machinery, owner scoping, dual-audience broadcast |
| final | compile-time handler manifest, exact-type dispatch, validated reflection fallback | global timeline as authoritative delivery (documented silent loss) |
| Projects/digitalbrain | incoming/outgoing filter pattern; explicit connection intent (`[WireTo]` was conceptually right, string-streams were the wrong body) | 604-line base class, string wiring, parallel routing mechanisms |
| ino | type-driven discovery, causation context | NeuronResult, canonical/reactive split, registry fan-out |
| IAW | proof that declared contracts drive automatic wiring | three competing paradigms in one core |

## Physics that survived every review (do not relitigate)

1. Nothing leaves a neuron before its turn commits — journals must never tell two
   stories (a sync ask lets the answerer durably record answering a question the
   crashed asker never durably asked).
2. Neurons never await neurons; continuations are declared handlers, durable and
   journal-visible. A turn may await its own IO.
3. Journals are append-only, human/model-readable, carry fact bodies, and are the
   only truth — delivery record, audit record, and the owner's fine-tuning corpus in
   one. A path that bypasses them is a kernel bug.
4. Delivery is at-least-once with receiver-side dedup by SynapseId; failed deliveries
   end in terminal journaled records, never silent loss or infinite silent retry.
5. `NeuronId(Kind: string, Name: string)`; no `System.Type`/AQN in addresses,
   journals, or wire data. Kind collisions fail boot loudly.
6. Core mints durable metadata before journaling; `RequestContext` is transport
   convenience, never the source of truth (it does not survive storage/redelivery).
7. UI is a module; a widget's action IS a synapse; renderers are listeners.
8. Never fake a proof: no stub gates, no synthetic observations, no "durable" names
   on volatile things (each killed a predecessor).

## THE OPEN PROBLEMS — your actual design work

**1. Topology: kernel-owned typed connections vs declaration-is-subscription.**
The owner's finding: v1's `Subscribe` leaked complexity (compiled catalog + dynamic
durable registry, every emission merging both, activation-time repair, lookup failure
retracting turns) — evidence that Subscribe is the wrong Core abstraction. The
candidate replacement: kernel-owned typed connections —
`Connect<XPost>(from: XAccountNeuron("elon"), to: behaviorNeuron)` — where
`Broadcast(fact)` snapshots matching connections into the outbox at commit, then uses
ordinary direct Orleans calls. Note the deeper prize: **connections are journalable
facts, so topology becomes runtime-mutable — the brain can rewire itself**, which
declaration-only subscription cannot do without recompiling. Grill hard: do
connections recreate v1's dual-audience split under a new name? Explore the
synthesis: declarations (`INeuron<TFact>`) define what a neuron CAN receive
(compile-time safety, dispatch manifest); connections define what it DOES receive
(runtime data, journaled, mutable). Who creates connections, where do they live, how
are they discovered, what happens on emission with zero connections? Decide with
evidence, and make the north-star scenario ("tell the brain to connect X to a
chart") the test.

**2. The atomicity contract — the hole every prototype has.**
No prototype guarantees `neuron state + incoming fact + outgoing facts commit
together` while modules can call `WriteStateAsync` or mutate arbitrary durable state.
v1 only rolls back what handlers manually enlist. Orleans gives no automatic
app-state rollback after a thrown handler. Therefore: **Core owns all durable
mutation inside a turn; module-visible `WriteStateAsync` must not exist.** Design the
module state story: (a) no module state — state is a fold over the neuron's own
journal; (b) Core-managed typed state slots written only at turn commit; (c)
something better. Whatever you choose, a thrown handler must leave zero durable
trace except a terminal failure record, and the BDD suite must prove it with
injected commit faults (v1's testing package shows how: sticky journal-commit
faults, armed-fault leak detection).

**3. The handler algebra.**
Owner's minimal shape: `Task<Synapse?> HandleAsync(TIncoming, CancellationToken)` —
return = the directed reply (journaled + routed by Core), null = heard, nothing to
say; expected negative outcomes are domain synapses; exceptions mean rollback +
retry. Prior finding in tension: typed pairing (`Synapse<TReply>` +
`Task<TReply>`) gives compile-time correctness and edge inference
(`AskAsync(new Greet(...))` → `Task<Greeted>` requires the question to carry its
reply type). Reconcile: can one interface serve hear/answer without null-noise for
listeners and without losing the typed pairing? Where do continuations land
(`Answer<Q,R>` wrapper vs ambient question vs bare reply + journal causation)? Count
ceremony lines in the demos; zero is the target.

**4. What of v1's engine ports exactly, what simplifies.**
Dedup window semantics (v1's eviction-retraction subtlety at `Neuron.Turns.cs:33` is
a real bug class — steal the shrunk-bound test technique), retry policy (bounded, with
terminal records — not v2's infinite silent retry, not v1's full DeliveryPolicy),
outbox durability (the journal IS the outbox: dispatch resumes from journal
positions), journal cursors/compaction (v1's tallies-outlive-compaction is the best
idea in the archaeology — per-type permanent counts + bounded retained window +
ResetSnapshot).

## BDD coverage — the definition of done

Write Gherkin-style scenarios (implemented as the two test kinds only — NeuronTest /
DigitalBrainTest, public API only) covering at minimum:

- Atomic turn: a thrown handler leaves no journal entries, no outbox entries, no
  state change; the delivery redelivers; a settled failure journals terminally.
- Commit-fault injection: a failed journal commit retracts everything and recovery
  reads committed truth after reactivation.
- Dedup: a redelivered SynapseId does not duplicate the turn; the dedup window
  saturation case (v1's deafness bug) is covered with a shrunk bound.
- Connection routing: connect, emit, deliver; zero-connection emission behavior;
  connection changes take effect without redeploy; connections visible in journals.
- Ask/answer round trip with the reply arriving after the asking call returned.
- Restart survival: journals, outbox backlog, and connections survive deactivation
  and silo restart; pending deliveries resume.
- Ordering: per-sender-per-receiver order preserved across retries.
- The north-star scenario end-to-end with fake ingestion and a fake renderer module.
- Journal readability: a scenario that reconstructs the full causal chain of the
  north-star flow from journal lines alone.

## Method — non-negotiable

- Grill every concept: name the consumer or delete it. Recommendation + strongest
  counterargument, defend or fold. Evidence over prose; test what is testable.
- 2–3 genuinely different options per open problem before deciding.
- Acceptance: express all ten FLOWS.md flows plus the north-star scenario in the
  final vocabulary; write the demo modules in full; a single ceremony line means
  iterate.
- Deliverables: Abstractions file list with COMPLETE code (the ABI must be perfect);
  Core file list with one-line responsibilities; the physics as a numbered contract;
  the BDD feature list; everything considered-and-rejected, with reasons.
