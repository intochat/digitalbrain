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
| AI model binding | done — lives in `DigitalBrain.Modules.AI`, not the kernel |
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

**The timeline stream is per neuron, not yet per identity.** A client can now watch a neuron:
`WatchAsync(kind, cursor, observer)` pushes each committed delta to a registered observer, a
reconnecting client resumes from its cursor and receives only what it missed, and a cursor that has
fallen off the retention window receives a reset carrying a snapshot rather than a silent gap.
Nothing polls: the kernel pushes at its commit boundary, and no wait path in the product holds a
timer.

What is still missing is the **per-identity** feed — one object that aggregates everything in an
owner's scope, so a client watches a brain rather than enumerating its neurons. That is a broadcast
subscription question, not an observation question, and it lands with broadcast addressing.

An observer registration is transient: it is held in the watched neuron's memory and is lost when
that neuron deactivates or its silo restarts. This is deliberate — durability lives in the cursor,
not in the subscription, so a client re-watches with the cursor it holds and catches up. A client
that never re-watches silently stops receiving, and nothing yet tells it that happened.

**`AsClient()` can leak a connection string.** The Aspire client projection delegates to the Orleans
hosting integration, which would pass a credentialed storage connection string to a referencing
service if the brain were configured with durable Azure stores. It is inert while the AppHost composes
memory-backed stores, and it must be closed before a production deployment.

**DevUI is unwired.** `Microsoft.Agents.AI.DevUI` is pinned but not integrated, so there is no
interactive surface for talking to a brain.

**An Orleans client is a trusted cluster peer.** The owner boundary is a correctness boundary, not an
authentication boundary. The kernel's call filter constrains neuron-to-neuron and registry traffic,
where the caller's identity is known and unforgeable; it cannot constrain a caller already inside the
cluster's trust boundary. Authenticate users at the edge and never expose an Orleans client endpoint
publicly — a hosted proof now asserts that the `orleans-gateway` and `orleans-silo` endpoints are
host-allocated and never published outside the host.

What changed is narrower than authentication and worth stating exactly. A client no longer reaches a
neuron's state surface without naming an owner: an unattributed caller may invoke only an interface
carrying `[ClientEntryPoint]`, which is `ISessionNeuron` and nothing else the framework ships, and
that session refuses any subject outside its own owner. Everything else — `INeuron`, the subscription
registry, and every capability interface a module will declare — is closed by default. But the owner
a client names is still the owner it chose at `BrainClient` construction, so the boundary keeps a
caller inside the owner it stated; it does not establish that the statement is true.

**Method aliases are part of the wire contract and nothing pins them.** `PinnedAliasesNeverChange`
asserts an exact map of **type** aliases, so renaming `db.session` fails the build. Orleans also
identifies grain *methods* by `[Alias]`, and those are unpinned: renaming a method alias changes the
wire identity of a call and no gate notices. It is inert while every caller is rebuilt from this
repository in lockstep, and it becomes real the moment an external client is version-skewed from the
silo — which is the external hosting mode of Phase 4.7 and the MCP edge of Phase 7.

**Transport authentication is deferred, so identity is self-asserted.** DEC-11 defers authentication
out of the framework: the product MCP edge is a thin client of the client API and owns no identity of
its own. An actor (DEC-12) therefore names who acts, but nothing proves the claim. The consequence is
stated rather than implied — the approval gate that refuses a self-approved behavior is a **safety**
property, preventing accident, not a **security** property preventing attack. Three claims are
therefore not made anywhere on this site or in the suite, instead of being made weakly: that anonymous
calls fail, that an invalid token fails, and that a wrong audience or origin is rejected. The
deferral is revisited when DigitalBrain is exposed beyond a loopback single-operator stack, or when a
second principal must be distrusted rather than merely distinguished.

**No authenticated edge exists.** An imported MCP project was deleted unbuilt — it never compiled and
no gate ever ran it, and `ARCHITECTURE-REVIEW.md` DEC-11 records why in detail. The thin edge is
built at Phase 7.

Until then the only way in is an Orleans cluster client, which the debt above already describes as a
trusted peer — the probe host and both samples are exactly that. So this is not "no way in", it is
"no *authenticated* way in", and the difference matters: the mitigation is the endpoint never being
publicly reachable, not the absence of a door.

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

## Delivery ordering

Directed sends are FIFO **per target** and at-least-once; handlers own idempotency through the
windowed `SynapseId` dedupe set. There is no cross-target ordering: one unreachable receiver does not
stall traffic to other receivers. A broadcast is the same isolation per listener — one failure does
not fail the fact for the rest. The guarantee is DEC-13 in `ARCHITECTURE-REVIEW.md`.

**Hosted proof is driven from inside the cluster.** An external Orleans client cannot complete a
handshake through an Aspire-proxied gateway, because the silo advertises its own address. The hosted
restart proof therefore runs from a probe host inside the cluster rather than from a true external
client.

## Broadcast addressing

A broadcast addresses handler **types**, not instance names. The set of handler types is known at
composition time because the interfaces declare it (`AddBroadcastHandlers`), so the catalog is
populated at startup rather than learned during activation — which makes late subscription for
compiled neurons impossible rather than merely tolerated, and removes the registry write from the
activation path. The instance a broadcast reaches is derived from the firing correlation. Behaviors
are the deliberate exception: a script's handled set cannot be known before the script exists, so
they still register on the durable per-owner registry at install.

## Proofs held red

None. Every written proof that is not explicit for a future phase runs in the default gate.

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
