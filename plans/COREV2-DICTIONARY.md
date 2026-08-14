# CoreV2 ubiquitous dictionary

These words are the language. Do not add synonyms in contracts, code, or design talk. If a sentence needs another noun, the design is still wrong.

**Impulse, Signal, Fact, Case, Document, Recipe, Morph, Correction, Subscription, Notice, Ask, Record, Teaching, Behavior, Pathway, Precedent are not CoreV2 words.**

---

## The dictionary

### Operation
What it does. Names one versioned public intent that an authenticated caller may invoke.
What it is. A sealed input/result/progress contract plus manifest descriptor, authorization requirement, idempotency scope, owning module, and entry Neuron role.
What it is not. A DomainEvent, an endpoint, a graph command, a provider tool, or a second message bus.

### Capability
What it does. Lets a Neuron use a typed module-published facility while preserving the current ActivityContext and delegated authority.
What it is. A versioned request/result contract resolved by CoreV2 through an explicit module manifest.
What it is not. A public product operation, a Neuron identity, a provider SDK object, or ambient service-provider access.

### Workspace
What it does. Draws the team fence. Membership, shared BrainGraph, shared Wirings, and policy live here.
What it is. Aggregate. The tenant.
What it is not. A person, credential, or permission to read another Principal’s private Entity or journal.

### Principal
What it does. Names who acted. Stamped on DomainEventMetadata and Synapse provenance.
What it is. Value object. Verified actor inside a Workspace.
What it is not. A client-supplied string or credential.

### Neuron
What it does. Thinks. Accepts a DomainEvent, decides, journals, may write an Entity, may emit later DomainEvents. One grain turn is one transaction.
What it is. Process aggregate. Durable Orleans grain. Journal + outbox.
What it is not. A router that owns the product, a grain per row, or a UI control.

### Synapse
What it does. Carries one DomainEvent type from one Neuron to another. Installing, replacing, and retiring Synapses is learning.
What it is. Value object owned by BrainGraph. Source, DomainEvent contract, target, optional Reshape, and provenance. Addressed by an opaque SynapseKey. Same key replaces the joint and keeps the old value in graph history.
What it is not. The packet that travels, a subscription inferred from a handler, or a provider credential.

### BrainGraph
What it does. Owns every Synapse for a Workspace. Validates them, resolves which live Synapses apply when a Neuron emits, and remembers superseded Synapses.
What it is. Topology aggregate. Persistent grain. Not a Neuron: it does not think and has no outbox.
What it is not. A message bus, public caller surface, or prompt.

### DomainEvent
What it does. States that something happened in a typed shape the next Neuron can accept. Travels a Synapse. Is what journals and mining read.
What it is. Domain event. Sealed CLR record. Past tense. `ProofProduced`, `Rewire`.
What it is not. JSON, `object`, a grain, a Synapse, or the occurrence stamp.

### DomainEventMetadata&lt;T&gt;
What it does. Identifies this firing of a DomainEvent: id, cause, BrainActivity, Principal, and time. Lets the kernel dedupe, chain cause, and attach events to one BrainActivity.
What it is. Value object. Plumbing; authors write `ProofProduced` and the Neuron’s emit/send wraps it.
What it is not. A product word, Synapse provenance, or the whole BrainActivity.

### BrainActivity
What it does. Holds one stretch of work from open to settled. DomainEvent firings hang on it. A Rewire is about it. A Wiring may be proposed from it.
What it is. Aggregate. One correlation, one Workspace, at most one active BrainActivity.
What it is not. The reusable how, a chat transcript, or an Entity.

### Entity
What it does. Keeps the current belief about one thing that changes as a unit.
What it is. Typed-state aggregate. Neurons write it after they decide. It does not emit or own Synapses.
What it is not. A Neuron, DomainEvent, or bag of JSON.

### Reshape
What it does. Changes one DomainEvent type into another so the target Neuron can accept it.
What it is. Domain service. Compiled, registered, pure, and type-checked when the Synapse is installed.
What it is not. A Synapse, a JSON mapping, or something that rewires BrainGraph.

### Rewire
What it does. Says this BrainActivity used the wrong Synapse. It is evidence; after authorization, BrainGraph replaces that Synapse using the same SynapseKey. The new Synapse provenance points at this Rewire.
What it is. DomainEvent on the BrainActivity.
What it is not. A silent topology change, the replace itself, or a rewrite of history.

### Wiring
What it does. Remembers how this kind of BrainActivity is handled so the next Principal does not invent it again. It uses roles, DomainEvent contracts, Reshape, and an Operation trigger.
What it is. Aggregate. Versioned. Publishable inside a Workspace. Lineage: v2 parent v1, reason = Rewire.
What it is not. A live Synapse, another Principal’s Entity, authority, journal, or transcript. Applying a Wiring never copies private state.

---

## Public boundary

MCP and Flutter are equal adapters. They discover eligible Operations, invoke one explicit Operation, and observe its policy-filtered BrainActivity. Operations direct-send to entry roles; only Neurons emit DomainEvents and only authorized CoreV2 behavior changes BrainGraph.

## How they sit together

```text
Principal in Workspace
      │ invokes Operation
      ▼
BrainActivity ──> entry Neuron ──emits──> DomainEvent ──Synapse──> target Neuron
      │                                  │
      ├── Rewire                          └── Reshape?
      └── Wiring proposal
```

**Send** directs an Operation to an entry role and does not consult BrainGraph. **Emit** consults BrainGraph. An outbox carries the route snapshot staged by the Neuron turn.

## What may be copied between Principals

| Copied when a Wiring is activated | Never copied |
|---|---|
| Operation trigger, DomainEvent contracts, Reshape name, Neuron roles, lineage | Authority, Entity state, raw payloads, journals, transcripts, endpoint identities, private Synapses |

## Persistence

| Name | Orleans |
|---|---|
| Neuron | `DurableGrain` — journal + outbox, one write per turn |
| BrainGraph | Persistent grain — Synapses + history. Not a Neuron |
| BrainActivity | Persistent grain — lifecycle of one handling |
| Entity | Persistent grain + typed state, only if the thing mutates as a unit |
| Wiring | Persistent grain — versioned pattern |
| Synapse | Data inside BrainGraph. Not a grain |
| Operation, Capability, DomainEvent, DomainEventMetadata, Rewire, Reshape | Not grains. Contracts, values, or registered services |

## Speak-test

A Principal invokes an Operation. CoreV2 opens a BrainActivity and direct-sends it to the entry Neuron role. Neurons emit DomainEvents. DomainEventMetadata stamps each firing. Synapses on BrainGraph carry them, and Reshape runs only when the type must change. If the route is wrong, a Rewire is recorded and authorized BrainGraph behavior replaces that Synapse. A Wiring stages and activates roles and public contracts for another Principal without copying private state.
