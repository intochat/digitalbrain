# CoreV2 — direction and vision

Language: [COREV2-DICTIONARY.md](COREV2-DICTIONARY.md). Those words only.

CoreV2 is a new kernel (`src/CoreV2`, `Brain.*`). It does not migrate V1 or share types with `DigitalBrain.*`. V1 stays the running product until this kernel can host a proof.

## Verified status

The root solution now compiles only `Brain.Abstractions`, `Brain.Core`, `Brain.Testing`, the Proof contracts and module, and their CoreV2 test projects. Architecture tests enforce that this compiled graph has no project references into `src/Kernel` or `src/Modules` and that `src/CoreV2` contains no reflection-based type discovery through `Assembly.GetTypes` or `GetCustomAttributes`.

The CI gate builds `DigitalBrain.slnx` in Release with warnings treated as errors, then runs the Architecture, Abstractions, Core, and Proof test suites. V1 source remains in the repository temporarily but is excluded from the solution and compiled project graph.

## Thesis

MCP and Flutter are equal adapters. Operations are the public boundary: they name the versioned intents an authenticated caller may invoke, while Capabilities let Neurons use typed module-published facilities. The proof contains no provider integration.

Product callers discover eligible Operations, invoke one explicit Operation, and observe its policy-filtered BrainActivity. They do not fire DomainEvents, select Neurons, inspect topology, or mutate BrainGraph directly.

The BrainGraph and its Synapses remember how work is handled. Installing, replacing, and retiring Synapses is learning. A Wiring lets a Workspace reuse that pattern without copying another Principal’s Entity, authority, or journal. A DomainEvent is what happened. A Neuron is the only thing that thinks.

## Direction

1. **New project, clean ideology.** `Brain.Abstractions` + `Brain.Core`; zero references to V1.
2. **Equal adapters.** MCP and Flutter discover, invoke, and observe through the same Operation contracts. Neither owns product behavior.
3. **Operations are the public boundary.** A caller discovers eligible Operations, invokes one explicit Operation, and observes policy-filtered BrainActivity. DomainEvents, Neurons, and BrainGraph remain internal.
4. **Type-safe internal bus.** Every DomainEvent is a sealed CLR record. Provider schemas and JSON do not enter the CoreV2 bus. No `object` or `JsonElement` on the bus.
5. **Capabilities are module facilities.** A Neuron resolves a typed Capability through a module manifest while preserving ActivityContext and delegated authority. A Capability is not an Operation or ambient service-provider access.
6. **No Hebbian anything.** Delivery never writes BrainGraph. Usage counts are projections off journals, between turns.
7. **First code is a proof, not a product.** Two Neurons, one DomainEvent, one BrainGraph, one Rewire, one Wiring, and Operations. No provider integration or presentation scenario.

## Picture

```text
Authenticated caller
      │ discover / invoke / observe
      ▼
Operation ──direct send──> entry Neuron ──emit──> DomainEvent
      │                                      │
      ▼                                      ▼
BrainActivity <──────────────────────── BrainGraph / Synapse
      │                                      │
      ├── Rewire                              └── Reshape?
      └── Entity

Wiring: reusable roles and public contracts for another Principal
```

**Send** directs an Operation to its entry role. **Emit** consults BrainGraph. Neither adapter calls BrainGraph directly.

## Invariants

1. `Operation` is the sole public product boundary. Product callers discover, invoke, and observe; they do not fire DomainEvents or access topology.
2. `Synapse` is only a junction. `DomainEvent` is only a typed happening. Never swap them.
3. A handler does not subscribe. Only a live Synapse routes an emit.
4. Same opaque `SynapseKey` replaces the joint and keeps the old value in BrainGraph history. Provenance of the old joint does not change.
5. Retiring a Synapse prevents later emissions from resolving it. Delivery does not change a Synapse.
6. `DomainEventMetadata<T>` stays generic through journal and outbox. Serialization does not erase `T`.
7. A Synapse without Reshape has the same DomainEvent type on both ends. A Synapse with Reshape names a registered `Reshape<TFrom,TTo>` checked at install.
8. Zero receivers are journalled and visible on the BrainActivity, with no outbox. This is not silent loss.
9. One Neuron turn writes inbound event, journal, state, and staged outbox together. The Neuron does not await that outbox.
10. A refusal is a later DomainEvent with a reason, not a timeout.
11. One correlation has one active BrainActivity in a Workspace.
12. A Rewire cannot move a Synapse by existing. Only an authorized BrainGraph replace can.
13. Wiring versions are append-only. v2 names v1 and the Rewire.
14. Applying a Wiring copies patterns only: never Entity, authority, tokens, journal, transcript, or another Principal’s endpoints.

## Orleans

| Name | Shape |
|---|---|
| Neuron | `DurableGrain` — journal + outbox |
| BrainGraph | Persistent grain — Synapses + history. Not a Neuron |
| BrainActivity | Persistent grain |
| Entity | Persistent grain + typed state, only if the thing mutates as a unit |
| Wiring | Persistent grain — versioned pattern |
| Synapse | Data in BrainGraph |
| Operation, Capability, DomainEvent, DomainEventMetadata, Rewire, Reshape | Contracts / values / registered services. Not grains |

## First slice

1. Discover `Proof.Run@1`, invoke it with caller idempotency, and observe its policy-filtered BrainActivity through either adapter.
2. Direct-send the Operation to a proof entry role; the source emits `ProofProduced`.
3. Install, replace, resolve, and retire opaque-key Synapses in the Workspace BrainGraph.
4. Route the first revision to summary; use Rewire evidence and an authorized replace to route later emissions to assessment.
5. Stage and activate a Wiring for another Principal using roles and public contracts only.

Exit: an Operation starts a typed proof, an authorized Rewire changes one Synapse target, retirement prevents resolution, and a Wiring reuses roles and public contracts without copying private state.

Scenarios that must stay speakable: [COREV2-SCENARIOS.md](COREV2-SCENARIOS.md).  
Pseudocode for the proof boundary: [pseudocode.md](pseudocode.md).
