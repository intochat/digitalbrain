# Core research: the communication paradigm of a self-programming OS

Working document, 2026-08-04. Inputs: four evidence-ruled research agents over every
project in E:\intochat (v1 kernel, ino, final, IAW, Projects/digitalbrain, self-improving,
v3, v4, digitalbrain-app), plus two independent external sparring sessions (Grok 0.2.101,
Codex/GPT-5.6) given a self-contained brief of the settled v2 programming model and the
broadcast-vs-direct-response contradiction. Every codebase claim below carries file:line
evidence in the agent transcripts; this file keeps only the load-bearing citations.

## 1. The verdict pool — what the graveyard proves

### Proven right (multiple independent codebases)

- **One verb, routing as metadata.** v3 wrote it down: *"routing is a property of the act
  of firing, carried in synapse metadata — NOT a synapse subtype"*
  (v3/docs/02-ino-and-broadcast.md:5) and shipped Emit/Ask/Reply as one `Fire(synapse,
  routing, receiver)` in a 159-line base class. ino's split (`Fire` vs `FireBroadcast`)
  and final's `RoutingMode` agree.
- **Declaration is subscription.** IAW's `IStreamConsumer<TEvent>` (reflection at
  activation binds the handler to the type-derived channel) is the only routing mechanism
  in that codebase with production callers; its rule-table router and broadcast channel
  are dead wiring. Matches v2's `INeuron<TFact>`-is-the-contract.
- **Subscribe only if you handle; drop before the turn.** v3 Neuron.cs:46-52, final
  Neuron.cs:169-170.
- **Ambient turn context, never context-in-signature.** final's
  `AsyncLocal<Synapse?> Current` + one central `Stamp` (correlation/causation/receiver
  derived from the incoming fact, zero author ceremony) is the recorded antidote to ino's
  `NeuronContext` in grain signatures — which forced a serialization surrogate that
  silently nulls services (*"relying on the rehydrated instance's Fire/Logger is a bug"*,
  NeuronContextSurrogate.cs:18-21) and a rebuild ritual at every hop. v2's
  RequestContext-headers + `handling` field are the same family; keep.
- **The reply envelope worked as a call result.** ino's `Fire<T>` returned
  `Task<NeuronResult>` (closed record: success/error/typed payload/UI payload) — the await
  was the Orleans call, no correlation machinery. It broke for other reasons
  (correlation-keyed grains destroyed actor identity).
- **UI is a synapse, not a subsystem.** final + self-improving: `UiSurface : Synapse`
  over a closed `UiWidget` union; `Button(string Label, Synapse? OnTap)` — a tap IS a
  typed fact; TUI and Flutter as peer renderers of one surface stream; the desktop shell
  itself a module. The LLM designer constrained to the closed widget vocabulary. ino's
  counterexample (opaque RFW `byte[]`) is journal-invisible — rejected.
- **Self-programming loop shape.** digitalbrain/ino lineage: *"no neuron without a green
  test is a RUNTIME invariant, not a CI convention"* (InoGate.cs:6-7); v3's `GateNeuron`:
  parse → Roslyn → collectible AssemblyLoadContext → run the generated test → unload
  (~153 lines); gate-before-persist, hot-register on green; human tap for privileged
  actions (self-improving); honesty ratchet test with tolerant-assert ceiling 0 and
  *"never raise the ceiling"* (final GateHonestyTests).
- **Capability reification.** v1's grain-call filters turn any typed inter-neuron call
  into journaled `CapabilityRequested` → `Completed/Failed/Rejected/Abandoned` with
  correlation threaded automatically and the journaled caller cross-checked against the
  transport identity — zero call-site tax for a complete causal audit.
- **Tallies outlive compaction.** v1 NeuronFeed: per-type permanent counts + bounded
  retained window + `ResetSnapshot` when a cursor falls off — unbounded-history questions
  with bounded-history storage.
- **Cluster-per-composition testing.** v1 testing: N test classes sharing one composition
  boot one real 3-silo cluster; controllable time that refuses to run backwards;
  injectable sticky journal-commit faults that fail the test if armed but unfired.

### Proven wrong (each killed or crippled a predecessor)

- **Kitchen-sink base class.** Projects/digitalbrain Neuron.cs: 604 lines, 5 base
  types, 17 responsibilities including its own DI container, UI rendering, a
  namespace-keyed color palette, and reflection into Orleans private internals. v1's
  12-file partial Neuron with 15 shared mutable fields is the same disease at
  maturity.
- **Two buses, one lossy bridge.** Typed streams + direct-call broadcaster bridged via
  `Dictionary<string,string>` reflection; one typo (`ReceiverNeUniformType`) silently
  broke routing forever.
- **Broadcast instances named by correlation GUID.** v1 `NeuronId.BroadcastReceiver`:
  every broadcast handler activation is fresh, empty, unaddressable; and the same Emit
  fans out to durable-named subscribers via a second path — one verb, two incompatible
  addressing semantics.
- **Sync request/response inside the brain.** v1's capability chain: `[AlwaysInterleave]`
  on exactly one method, TaskScheduler capture, `TurnBoundFunction`, 25ms journal
  polling inside a tool call, a per-owner session broker — five mechanisms fighting one
  decision, and the conversation still single-threaded end to end with two
  admitted-undetectable deadlock hazards (ChatNeuron.cs:82-89).
- **Fabricated proofs.** self-improving's high-sev gate: every assertion
  `Task.CompletedTask`; final's `ListSubscribersAsync` computing synthetic NeuronIds
  from arithmetic; `IDurableList` faked by a cast; "DurableTaskCompletionSource" (IAW
  PR 35) never existing in code — an in-memory TCS on volatile storage in production.
  Every predecessor that started lying to itself died of it.
- **Constructor-forwarding facet tax.** IAW: 34 files restating `[AgentState]` through a
  base that exists only to forward; three copy-pasted mappers with hand-typed string
  keys failing at activation, not compile time.
- **Dual derivation.** v1: `IHandle<T>` compiled to a string table by Roslyn AND
  reflected to delegates at runtime — two sources of truth. Module ownership inferred by
  longest-namespace-prefix — renames silently move neurons between manifests.
- **Journaled union cases are forever.** Removing a `[GenerateSerializer]` union case is
  a wire-format break for durable journals (self-improving DELETED.md) — design
  append-only vocabularies from day one.

## 2. External convergence (independent of each other and of the graveyard)

Both sparring partners, given only the brief, rejected the one-turn Ask window **as a
semantic** and converged on the same resolution:

- Grok: *"Primary is neither broadcast nor RPC — it is journaled delivery to resolved
  addresses."* Emit/Send/Reply are address-resolution strategies on one bus; Ask is edge
  sugar = send with reply-to + wait for the matching reply **fact**; same-turn reply on
  the wire is allowed as an optimization only; the reply must be a journaled directed
  synapse either way.
- Codex: *"durable fact delivery is the sole communication semantic; Ask is an edge-side
  observation of the journal."* Five-point attack on wire-riding Ask: commit and return
  are not atomic; a 90s LLM call inside a turn holds the non-reentrant activation
  (latency mistaken for causality); multi-turn work (tool chains, approvals, fan-out) is
  the OS's important work; a scalar result is incompatible with broadcast fan-out; the
  edge's observation is itself missing from the journal. Plus the theorem: *"a
  multi-turn await requires something to remember the continuation — 'no correlation' is
  not a fourth option"* — but correlation can be **by fact and actor identity** instead
  of GUID fields threaded by authors.
- Both: **neurons never await neurons** is core physics (Codex strengthens: never CALL
  synchronously); both: module lifecycle must be journaled facts with content-addressed
  code; both: the biggest risk is any communication path that bypasses the journal
  (Codex names it *"causal sovereignty leaking outside the journal"*; the constitutional
  rule: every externally observable effect crosses the journaled kernel seam under an
  explicit capability).
- Instance rule convergence: Codex's **locus** — address = `(logical kind, locus)`;
  Emit delivers to every subscribing kind **at the current locus**; facts inherit locus;
  the edge chooses the initial locus; globals use an explicit well-known locus. (Grok's
  cardinality declarations are the stricter cousin; locus is simpler and has no
  unresolvable case — virtual actors activate on demand.) v1's correlation-GUID naming is
  the proven wrong answer.

## 3. The synthesized architecture

Six concepts. Everything else — UI, AI, storage, HTTP — is a module.

| # | Concept | Definition |
|---|---------|------------|
| 1 | **Synapse** | An immutable typed fact plus its envelope: identity (source address, sequence), cause, timestamp, locus. The envelope schema is the OS ABI — frozen small, append-only. |
| 2 | **Neuron** | A durable single-threaded actor addressed by `(kind, locus)`. Author surface: `class X : Neuron, INeuron<TFact>` + handlers. Declaration is address AND subscription. |
| 3 | **Journal** | Append-only, per-neuron, model-readable. Position = sequence = identity; causation = adjacency. The complete causal record — nothing communicates around it. |
| 4 | **Route** | One delivery bus, three resolution modes: `Emit(fact)` → every subscribing kind at the current locus; `Send(address, fact)` → an address learned from a fact; `Reply(fact)` → the turn's source. All deliveries leave after the turn commits. |
| 5 | **Revision** | A module is journaled code: content-addressed source/artifact, lifecycle as facts (`RevisionProposed/Built/Evaluated/Activated/Retired`), activation gated by a green generated test in a collectible ALC, human tap for privileged changes. The grain manifest is a projection of these facts, not the authority. |
| 6 | **Capability** | The journaled seam for every external effect (model calls, storage, network, UI transport). Reified into facts by the kernel (v1's filter mechanism), never author ceremony. |

**The contradiction, resolved:** inside the brain there is only committed fact delivery —
neurons never await neurons. "Direct response" is not a transport semantic: the edge is
itself an address (a session neuron at its own locus); `Ask` = deliver the request with
the edge as source, then **observe the edge's journal until the typed reply fact
arrives**. A same-turn reply may ride back on the delivery call as a fast path, but
correctness is identical if it arrives ten turns later. Broadcasting vs direct response
= two routing modes reading the same journaled facts.

**Turn (unchanged in essence from v2 today):** deliver → handler runs (may await its own
IO, never another neuron) → journal reception + emissions → commit → dispatch emissions.

### Forced consequences (decided by the architecture, pending owner ratification)

1. **Fact bodies must be journaled.** Ask-by-observation reads the reply's body from a
   journal; the journal-as-outbox (Codex: *"the journal is already your durable outbox…
   do not claim the problem disappeared"*) resumes dispatch from journal positions. The
   open fact-body decision is closed by necessity: bodies in the journal (large blobs
   content-addressed, hash in the fact — Grok).
2. **`NeuronId` reverts to string identity.** Address = `(logical kind, locus)`; code
   revision is not part of the durable address (Codex: *"otherwise every upgrade creates
   a new brain"*). `System.Type` in `NeuronId` and AQN strings in journals bind
   addresses to loaded assemblies — hostile to grown modules. The wire already agrees
   (`AddressOf` keeps only the lowercased class name).
3. **`Send<TNeuron>(name, fact)` dies** (type-coupled). Module verbs: `Emit`, `Reply`,
   `Send(address)`. Edge verbs: `Send`, `Ask` (journal observation).
4. **Locus enters the envelope.** Emissions inherit the incoming fact's locus; the edge
   chooses the initial locus; system neurons use an explicit well-known locus.
5. **Duplicate recognition at the receiver** (`source, sequence` against the journal
   tail) becomes necessary the moment dispatch resumes from the journal — the deleted
   dedup machinery returns in its minimal form only when journal-resume lands, not
   before.

### What stays exactly as v2 has it

Turn atomicity semantics, the incoming/outgoing filter pair carrying sender identity in
RequestContext headers, journal-in-ctor via keyed service (no facet tax — IAW's 34-file
lesson), declaration-is-registration via the Orleans manifest (as the projection),
tests as value demos only (NeuronTest/DigitalBrainTest), extreme deletion discipline.

## 4. Sequencing (staged shipping, decided earlier)

1. **Now (v2-core):** verbs Emit/Send/Reply + locus + string NeuronId + journaled
   bodies + edge Ask by journal polling (the tests already poll). No revision
   machinery, no capability filters yet — compiled modules, manifest as registry.
2. **Next:** journal-resume dispatch + minimal receiver dedup; [OneWay] journal
   observers to replace edge polling (v1 mechanism); capability reification filters.
3. **Then:** Revision facts + the activation gate (GateNeuron lineage) + content-
   addressed module store; UI module (UiSurface union, shell-as-module).

## 5. Constitutional rules (adopted from the convergence)

- The journal is the complete causal record; a communication path that bypasses it is a
  kernel bug, not a feature.
- Neurons never await neurons. Multi-step work is fact protocols.
- Deterministic instance resolution; never round-robin, never all-activations, never
  correlation-named instances.
- No concept without a consumer that exists today; a component with green isolated
  tests and no production caller does not exist.
- Never name a thing for a guarantee it does not provide ("durable" means it survives
  the process).
- The proof is never faked: gates run real assertions; tolerant-assert ceiling is zero
  and never rises.
- Journaled vocabularies are append-only.
