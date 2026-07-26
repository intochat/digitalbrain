# Behavior operating system ownership, migration, and invariants

**Status:** Approved design; implementation remains unbuilt until the required proofs exist

**Parent decision and identity model:** [Behavior operating system and runtime design](2026-07-26-behavior-operating-system-runtime-design.md)

## 11. Package and ownership target

The exact project names may be simplified during implementation, but these ownership boundaries are
fixed:

| Home | Owns |
| --- | --- |
| `DigitalBrain.Abstractions` | `IBehavior`, stable IDs, generic lifecycle and intent envelopes |
| `DigitalBrain.Kernel` | `BehaviorNeuron`, journaling, revision activation, subscription routing |
| Behavior SDK | program interfaces, safe context surface, manifest/schema contracts |
| Behavior compiler host | isolated restore, compilation, analysis, artifact production |
| Behavior worker host | isolated artifact loading and program execution |
| OS source tree/package | minimal built-in Behavior sources, manifests, and features |
| Modules | compile-time public neuron/synapse vocabulary and runtime implementations |
| Edges | authentication, transport, projections, and pixels; never OS policy |

The Behavior SDK and hosts are framework machinery. The installed revision set is the operating
system.

## 12. Required migration and deletion

Implementation must end with one product path:

| Current artifact | Required outcome |
| --- | --- |
| Flutter-owned `OpenHomeOnActivationBehavior` | Replace with OS-owned `StartUi` revision; Flutter keeps only rendering vocabulary/runtime |
| `ActivateDigitalBrain`, `BootOnActivation`, `OpenHome`, `PostAuthBootstrap`, and overlapping surface helpers | Fold useful logic into Behaviors, preserve BDD outcomes, then delete the redundant pull path |
| Compiled `IAccountEnrichment` process neuron and module capsule | Re-express as a Behavior over Google and Salesforce module contracts; retain private history in `BehaviorNeuron`; delete the sample module after parity |
| `DigitalBrainClient.RequireDomainNeuronContract` blanket rejection of `IBehavior` | Replace with explicit routing rules that admit exact Behavior addresses and intent envelopes |
| `SubscriptionRegistry` keyed by CLR full name | Move to stable aliases, owner scope, atomic revision replacement, and uninstall |
| Source-generated-only private dispatch seam | Add the smallest protected generic dispatch seam needed by `BehaviorNeuron`, preserving base delivery, journal, dedupe, and outbox invariants |
| Documents that call the new model absent, forbid a rail package, or identify Behavior identity with a concrete grain class | Mark historical or update to this approved design; continue to label the rail unbuilt until tests prove otherwise |

No legacy route is retained “just in case.” Git is the recovery mechanism. Deletion happens only
after the replacement product sentence is green at the root gate.

## 13. Repository completion standard

The refactor is not complete merely when the new projects build. Completion requires:

- One activation-to-UI product path.
- One account-enrichment product path.
- No Behavior logic inside module implementations.
- No module vocabulary invented by a Behavior.
- No runtime restore or compilation during an invocation.
- No direct Orleans or infrastructure authority in a program.
- No stale project references, empty folders, checked-in build output, commented-out code, temporary
  artifacts, obsolete samples, duplicate docs, or contradictory status claims.
- Architecture, package, hosting, testing, and contributor docs describing the code that actually
  exists, with Designed and Built explicitly separated.
- Repository searches for retired type/project names returning only intentional migration history.
- Formatting, analyzers, documentation checks, root Release build, and unfiltered root Release tests
  green.

## 14. Rejected alternatives

### Execute `dotnet behavior.cs` on every trigger

Rejected because it couples restore, build, cache invalidation, and execution to the product path.
It also exposes file directives and implicit MSBuild/NuGet inputs at invocation time.

### Load community assemblies inside the silo

Rejected because dependency isolation is not privilege isolation. A crash, loop, memory leak, or
forbidden API would share the authority and availability boundary of the brain.

### Generate one grain class per Behavior

Rejected because installed community logic would require adding CLR grain implementations to a
running cluster. Orleans already provides the correct identity model: one grain implementation with
many keyed instances.

### Make every Behavior a module

Rejected because it collapses logic back into vocabulary, forces rebuilds for user policy, and
prevents owner-scoped installation and revision history.

### Let Behaviors add public CLR intent contracts

Rejected because runtime installation cannot safely change the compiled type universe. Versioned
schemas keep Behavior-local vocabulary dynamic while module contracts remain typed and compiled.

## 15. Strongest counterargument

The recommended design introduces two out-of-process systems, artifact storage, IPC, sandbox
launching, capability mediation, and replay bookkeeping. An in-process compiler and
`AssemblyLoadContext` would be much smaller.

That simpler design is valid only for trusted source-controlled built-ins. It cannot safely support
the approved product claim that an AI and a community may contribute executable Behaviors. The
complexity is therefore accepted at one narrow boundary—the compiler/executor seam—while the rest of
the system remains ordinary neurons, synapses, module contracts, and BDD.

## 16. Ratified invariants

1. Framework equals neuron/synapse mechanics; the installed Behavior set is the operating system.
2. Modules alone add public CLR neuron and synapse vocabulary.
3. `BehaviorNeuron : Neuron, IBehavior`; the single-file program is not a Neuron.
4. Behavior identity is `(OwnerId, BehaviorId)` with immutable approved revisions.
5. One registered Behavior grain implementation hosts all Behavior instances.
6. Event subscriptions and schema-validated intent invocation are equally valid entry points.
7. Broadcast versus directed delivery is routing, not a Behavior taxonomy.
8. Vector search discovers candidates; exact catalogs and grants authorize them.
9. AI may invoke approved Behaviors and propose new ones; humans alone approve installation.
10. Programs execute through a constrained context and trusted capability broker.
11. Unknown code executes outside the silo; single-file source and single-file deployment are not
    security boundaries.
12. Built-ins use the same artifact, revision, journal, capability, and BDD model; only provenance
    may select a trusted executor.
13. The tested and approved artifact hash is the artifact that executes.
14. The migration deletes the dual legacy paths and leaves documentation synchronized with reality.
