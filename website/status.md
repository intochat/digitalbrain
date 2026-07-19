# Status

DigitalBrain v2 is a ground-up rebuild on the `master` branch. The previous implementation was
rejected wholesale and survives only as git history. No packages are published to NuGet yet.

Everything on this site describes behaviour that is proven by the test suite, with one class of
exception that is named rather than hidden: some proofs are written and deliberately held red,
because they assert behaviour the foundation does not yet have. Those are listed under
[proofs held red](#proofs-held-red). Where a guarantee is implemented but not proven, or missing
entirely, it is listed under [open debts](#open-debts) rather than quietly implied.

## Milestones

| Milestone | State |
| --- | --- |
| Demolition and clean skeleton | done |
| Recorded architecture decisions | done |
| Neuron kernel | done |
| Durable synapse fabric | done |
| Multi-silo delivery and recovery | done |
| AI model binding | done, except the `Embedding` tier, which cannot work |
| Client package | done |
| Aspire integration | done |
| Hosts, dev tools, quickstart | done |
| Hosted restart proof | done |
| Release engineering | done |
| Final verification and docs | done |

## Gates

Every commit keeps all of these green:

| Gate | What it proves |
| --- | --- |
| `dotnet test .\DigitalBrain.slnx -c Release` | Contract, simulation and hosted tiers |
| `.\eng\pack.ps1` | Every package builds, ships symbols, and respects the security boundary |
| `.\eng\verify-consumer.ps1` | The samples restore, build and run from an **empty** package cache |
| `.\eng\verify-dependencies.ps1` | No vulnerable, deprecated or floating dependencies |
| `npm test` and `npm run build` | This site is accurate and builds |

## Open debts

These are real gaps in the foundation. They are tracked, not hidden.

**No timeline stream.** A client can fire synapses and catch up through cursor-based journal reads,
but cannot observe a brain as it works. Code that needs to react must poll a journal. This is the most
visible missing primitive.

The kernel does emit an OpenTelemetry activity on every delivery, and the testing package listens to
it, so in-process observation exists and the simulation suite uses it. What is missing is the durable,
per-identity, catch-up-after-disconnect feed that an out-of-process client needs.

**Outbox redelivery is unproven.** Delivery retries across a receiver outage are implemented, but no
scenario yet drives an outage and asserts the redelivery. It is code without a proof, which is not the
same as a guarantee.

**`AsClient()` can leak a connection string.** The Aspire client projection delegates to the Orleans
hosting integration, which would pass a credentialed storage connection string to a referencing
service if the brain were configured with durable Azure stores. It is inert while the AppHost composes
memory-backed stores, and it must be closed before a production deployment.

**DevUI is unwired.** `Microsoft.Agents.AI.DevUI` is pinned but not integrated, so there is no
interactive surface for talking to a brain.

**An Orleans client is a trusted cluster peer.** The owner boundary is a correctness boundary, not an
authentication boundary. Any process holding an `IGrainFactory` can address any grain directly,
including another owner's, without going through `BrainClient`. The kernel's call filter constrains
neuron-to-neuron and registry traffic, where the caller's identity is known and unforgeable; it cannot
constrain a caller already inside the cluster's trust boundary. Authenticate users at the edge and
never expose an Orleans client endpoint publicly.

**A journal is a summary plus a recent window, so history is lost.** Journals used to grow without
bound; they are now bounded by both record count and total bytes. What survives compaction is a
durable tally — how many of each synapse type the neuron has recorded, the last sequence, and the
window still retained — not the synapses themselves. A consumer can still ask *what has this neuron
done and how much*, but it cannot read a synapse older than the window. Reads carry a resume sequence;
a stale cursor receives the complete summary as a reset rather than a silently gapped tail. That is
the deliberate trade of DEC-1: the journal stopped being an audit log, and the audit log it used to
pretend to be is provided separately by the governance ledger, which is not built yet.

**Effectively-once processing is windowed, not eternal.** A neuron remembers the last 4,096
`SynapseId`s it has handled, in a durable ring that survives restart. A redelivery of a synapse
older than that window would be handled a second time. This is a deliberate trade: the previous
design remembered every synapse forever and paid a whole-journal scan on every delivery to do it.

**The `Embedding` model tier cannot work.** Every declared tier is registered as an `IChatClient`. An
embedding model is not an `IChatClient` but an `IEmbeddingGenerator<string, Embedding<float>>`, so
the `Embedding` member of `ModelTier` binds to a client type that cannot serve it. It shipped
documented but never exercised — `Embedding` appears exactly once in the codebase, in the enum that
declares it — which is how it survived.

**One unreachable receiver blocks a neuron's whole outbox.** The outbox drains strictly in order and
stops at the first entry with an undelivered receiver. A single unreachable neuron therefore stalls
*all* outgoing traffic from the sender — including traffic to receivers that are perfectly
reachable — until that entry exhausts its attempts or the 30-minute retry horizon expires.

**Subscriptions are never removed.** The registry only grows: a neuron that registers for a synapse
type stays registered forever, even if it is never activated again. Broadcast fan-out therefore grows
monotonically with every neuron instance that has ever existed.

**A neuron that has never activated does not receive broadcasts.** Subscription is registered during
`OnActivateAsync`, and `EmitAsync` reads the subscriber set at emit time. A neuron that exists in
code but has never been activated is not a subscriber and is silently skipped. The multiagent sample
works around this by reading each neuron's journal purely to force activation before broadcasting.

**Hosted proof is driven from inside the cluster.** An external Orleans client cannot complete a
handshake through an Aspire-proxied gateway, because the silo advertises its own address. The hosted
restart proof therefore runs from a probe host inside the cluster rather than from a true external
client.

## Proofs held red

These proofs are written and checked in, and they fail. They assert the behaviour the foundation is
meant to have, not the behaviour it has, so each one is excluded from the default run and turns green
when the debt above it is paid. They are listed here because a proof that nobody runs is worth
nothing unless its existence and its state are both public.

| Proof | Asserts | Goes green when |
| --- | --- | --- |
| `the kernel assembly reaches no vendor model SDK` | `DigitalBrain.Kernel` has no transitive reference to Anthropic, OpenAI or `Microsoft.Extensions.AI` | AI becomes an ordinary module and leaves the kernel |
| `an unreachable receiver does not block traffic to reachable ones` | Outbox progress is per-entry, not head-first | The outbox stops draining strictly in order |
| `a neuron that has never activated still receives a broadcast` | Subscription does not depend on prior activation | Broadcast addressing is decided — see below |

The last of these was, until recently, not merely unimplemented but **unsatisfiable**, and the reason
is worth keeping. The registry maps a synapse type to an array of `NeuronId`s, and each `NeuronId`
names a specific instance. Orleans grains are virtual, so every possible name of every neuron type
already exists. There is therefore no set of "all neurons that would handle this synapse" for a
broadcast to reach — only the set that has registered.

**That is now decided, and the proof has a target.** A broadcast addresses handler **types**, not
instance names. The set of handler types is known at composition time, because the interfaces declare
it, so the registry is populated at startup rather than learned during activation — which makes late
subscription impossible rather than merely tolerated, and removes the registry write from the
activation path. The instance a broadcast reaches is derived from the firing correlation rather than
enumerated. Two prior generations converged on this shape independently.

Behaviors are the exception, and deliberately so: a script's handled set cannot be known before the
script exists, so behaviors register when they are installed. That is a small, bounded, explicit
registry — not the per-activation write that produced the debt above it.

## Where this is going

The foundation exists to carry a programming model that is **designed and not yet built**. Nothing in
this section is implemented; it is stated here so the gap between the design and the code is public
rather than implied.

| | Contributes | When | Requires |
| --- | --- | --- | --- |
| **Module** | Vocabulary — synapse records, neuron interfaces | Compile time | A rebuild |
| **Behavior** | Logic over existing vocabulary | Runtime | Approval only |

A **behavior** is a single-file C# script carried as durable state by one registered grain type, so it
adds no grain type and the Orleans manifest never changes. The client API is the programming model,
which means the same file runs as a script against a cluster and installs as a behavior inside one.
Capability is the set of contracts packages a script compiles against, enforced where it resolves one,
and every install is a human-approved proposal that is journaled and reversible.

[Architecture](/architecture) describes the design and marks each part as built or designed.
`ARCHITECTURE-REVIEW.md` in the repository is the plan of record.

One assumption underneath it is **load-bearing and unmeasured**: that a language model can reliably
emit these scripts. It is called out here rather than left implicit, because a prior generation in
this lineage gated a language on a benchmark whose score came from a deterministic stub rather than a
model, and shipped the interpreter after formally demoting the language.

## Following along

The executable Tier-1 specifications are published on the [Specification](/specification) page,
generated from the feature files at build time so they cannot drift from what actually passes.
