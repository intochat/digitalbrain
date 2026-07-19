# Status

DigitalBrain v2 is a ground-up rebuild on the `master` branch. The previous implementation was
rejected wholesale and survives only as git history. No packages are published to NuGet yet.

Everything on this site describes behaviour that is proven by the test suite. Where a guarantee is
implemented but not proven, or missing entirely, it is listed under [open debts](#open-debts) rather
than quietly implied.

## Milestones

| Milestone | State |
| --- | --- |
| Demolition and clean skeleton | done |
| Recorded architecture decisions | done |
| Neuron kernel | done |
| Durable synapse fabric | done |
| Multi-silo delivery and recovery | done |
| AI model binding | done |
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

**No timeline stream.** A client can fire synapses and read journals, but cannot observe a brain as it
works. Code that needs to react must poll a journal. This is the most visible missing primitive.

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

**Journals grow without bound.** A neuron's incoming and outgoing journals are never compacted, and
every delivery deserializes the whole incoming journal to check for a duplicate `SynapseId`. A
long-lived, chatty neuron therefore costs progressively more per synapse and progressively more
storage, with no pruning, snapshotting or retention policy yet.

**Subscriptions are never removed.** The registry only grows: a neuron that registers for a synapse
type stays registered forever, even if it is never activated again. Broadcast fan-out therefore grows
monotonically with every neuron instance that has ever existed.

**Hosted proof is driven from inside the cluster.** An external Orleans client cannot complete a
handshake through an Aspire-proxied gateway, because the silo advertises its own address. The hosted
restart proof therefore runs from a probe host inside the cluster rather than from a true external
client.

## Following along

The executable Tier-1 specifications are published on the [Specification](/specification) page,
generated from the feature files at build time so they cannot drift from what actually passes.
