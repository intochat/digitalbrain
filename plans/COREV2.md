# CoreV2 — direction and vision

Language: [COREV2-DICTIONARY.md](COREV2-DICTIONARY.md). Those words only.

CoreV2 is a new kernel (`src/CoreV2`, `Brain.*`). It does not migrate V1. It does not share types with `DigitalBrain.*`. V1 stays the running product until this kernel can host a proof.

## Where this came from

The first conversation wanted a digital brain: Orleans grains as neurons, a journal of everything useful, later mining of how the system had been used. It mixed three things into one grain — the thinker, the pulse, and the trip — and named the **packet** `Synapse`. That name is why every later talk collapsed.

The rewire plan fixed the learning rule: synapses do not grow weights. They are **soldered**. Same key replaces a joint. A hint is evidence; the graph change is explicit. Journals are the mine. A reusable how is not the chat transcript.

The first CoreV2 vision kept that rule and added Workspace / Principal / typed contracts. Its extra nouns (Fact, Case, Document, Recipe, Morph, Correction) were still abstract. The dictionary replaced them with **DomainEvent**, **BrainActivity**, **Entity**, **Wiring**, **Reshape**, **Rewire**.

This file is the single direction. Those three sources are retired.

## Thesis

The BrainGraph and its Synapses are the memory of use. Installing, replacing, and retiring synapses is learning. A Wiring is how a Workspace reuses that how without copying another Principal’s Entity, tokens, or journal. A DomainEvent is what happened. A Neuron is the only thing that thinks.

## Direction

1. **New project, clean ideology.** `Brain.Abstractions` + `Brain.Core`. Zero references to V1.
2. **Type-safe bus.** Every DomainEvent is a sealed CLR record. JSON exists only at chat/HTTP/model, then dies. No `object`, no `JsonElement` on the bus.
3. **No second front door.** Chat is text ingress. Voice becomes text, then chat. No TextInput / VoiceInput grain.
4. **No Hebbian anything.** Delivery never writes the BrainGraph. Usage counts are projections off journals, between turns.
5. **Three model tools.** `find_capabilities` (including matching Wirings and live synapses), `get_neurons`, `fire`. Graph changes are ordinary typed fires.
6. **First code** is a proof, not a product: two Neurons, one DomainEvent, one BrainGraph, one Rewire, one Wiring. No UI. No model.

## Picture

```text
Principal  in  Workspace
      │
      ▼
BrainActivity                         one stretch of work
      │
      ├── DomainEvent                 what happened
      │     + DomainEventMetadata<T>  this firing (stamp only)
      ├── Rewire                      the Synapse was wrong
      ├── Entity                      current trip / note / thing
      └── Synapse  on  BrainGraph     the road
              └── Reshape?            only if the next Neuron needs another type

Wiring                                reusable how, next BrainActivity
```

**Emit** consults the BrainGraph. **Send** does not.

## Invariants

1. `Synapse` is only a junction. `DomainEvent` is only a typed happening. Never swap them.
2. A handler does not subscribe. Only a live Synapse routes an emit.
3. Same `SynapseKey` replaces the joint and keeps the old value in BrainGraph history. Provenance of the old joint does not change.
4. Expired synapses do not route. Clearing expiry is another explicit install.
5. Delivering a DomainEvent does not change a Synapse.
6. `DomainEventMetadata<T>` stays generic through journal and outbox. Serialization does not erase `T`.
7. A Synapse without Reshape has the same DomainEvent type on both ends. A Synapse with Reshape names a registered `Reshape<TFrom,TTo>` checked at install.
8. Zero receivers: journalled, visible on the BrainActivity, no outbox. Not silent loss.
9. One Neuron turn writes inbound event, journal, state, and staged outbox together. The Neuron does not await that outbox.
10. A refusal is a later DomainEvent with a reason, not a timeout.
11. One correlation, one active BrainActivity in a Workspace.
12. A Rewire cannot move a Synapse by existing. Only an authorized BrainGraph replace can.
13. Wiring versions are append-only. v2 names v1 and the Rewire.
14. Applying a Wiring copies pattern only. Never Entity, tokens, journal, transcript, or another Principal’s endpoints.
15. Chat is the only text ingress.

## Orleans

| Name | Shape |
|---|---|
| Neuron | `DurableGrain` — journal + outbox |
| BrainGraph | Persistent grain — synapses + history. Not a Neuron |
| BrainActivity | Persistent grain |
| Entity | Persistent grain + typed state, only if the thing mutates as a unit |
| Wiring | Persistent grain — versioned pattern |
| Synapse | Data in BrainGraph |
| DomainEvent, DomainEventMetadata, Rewire, Reshape | Values / registered service. Not grains |

## What V1 must not come across

Packet named Synapse. Broadcast ghosts from `IHandle<T>`. String morphs. Weights on junctions. Input grains. Router god. Recipe/Impulse/Fact as kernel words. Remembering how only in chat. Four model tools.

## First slice

1. One `IDomainEvent`, two Neurons, optional identity Reshape.
2. Workspace BrainGraph: install, replace, expire, resolve.
3. Neuron emit through synapses; Send bypasses the graph.
4. Zero receivers visible; refusal has a reason.
5. Rewire → same SynapseKey, new target; only the new target receives; history keeps the old.
6. Then one Wiring and “already wired?” on find.

Exit: a typed Synapse can be installed, rewired, found as a Wiring, and applied for another Principal without copying private state.

Scenarios that must stay speakable: [COREV2-SCENARIOS.md](COREV2-SCENARIOS.md).  
Pseudocode for 2 and 5: [pseudocode.md](pseudocode.md).
