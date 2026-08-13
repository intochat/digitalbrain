# CoreV2 ubiquitous dictionary

These words are the language. Do not add synonyms in contracts, code, or design talk. If a sentence needs another noun, the design is still wrong.

**Impulse, Signal, Fact, Case, Document, Recipe, Morph, Correction, Subscription, Notice, Ask, Record, Teaching, Behavior, Pathway, Precedent are not CoreV2 words.**

---

## The dictionary

### Workspace
**What it does.** Draws the team fence. Membership, shared BrainGraph, shared Wirings, and policy live here.  
**What it is.** Aggregate. The tenant. Example: Sales Operations.  
**What it is not.** A person. An OAuth token. Permission to read another member’s private Entity or journal.

### Principal
**What it does.** Names who acted. Stamped on DomainEventMetadata and on synapse provenance.  
**What it is.** Value object. Verified actor inside a Workspace. Alice.  
**What it is not.** “Owner” as a type. A client-supplied string. A credential. Owner is a *role* a Principal may hold, not a noun in this dictionary.

### Neuron
**What it does.** Thinks. Accepts a DomainEvent, decides, journals, may write an Entity, may emit later DomainEvents. One grain turn is one transaction.  
**What it is.** Process aggregate. Durable Orleans grain. Journal + outbox.  
**What it is not.** A trip Entity. A router that owns the product. A grain per row. A UI control.

### Synapse
**What it does.** Carries one DomainEvent type from this Neuron to that Neuron. It is the valuable record of *how this brain is used*. Installing, replacing, and retiring synapses *is* learning.  
**What it is.** Value object owned by BrainGraph. Source, DomainEvent contract, target, optional Reshape, optional expiry, provenance. Addressed by SynapseKey. Same key replaces the joint and keeps the old value in graph history.  
**What it is not.** The packet that travels. A weight. A subscription inferred from “this Neuron has a handler.” Alice’s Salesforce token.

### BrainGraph
**What it does.** Owns every Synapse for a Workspace. Validates them. Resolves which live synapses apply when a Neuron emits. Remembers superseded synapses.  
**What it is.** Topology aggregate. Persistent grain. Not a Neuron: it does not think and has no outbox.  
**What it is not.** A message bus. One Synapse. A prompt.

### DomainEvent
**What it does.** States that something happened, in a typed shape the next Neuron can accept. Travels a Synapse. Is what journals and mining read.  
**What it is.** Domain event. Sealed CLR record. Past tense. `OpportunitiesObserved`, `TimerElapsed`, `UserSaid`, `Rewire`.  
**What it is not.** JSON. `object`. A grain. A Synapse. The occurrence stamp (that is DomainEventMetadata).

### DomainEventMetadata&lt;T&gt;
**What it does.** Identifies *this firing* of a DomainEvent: id, what caused it, which BrainActivity it belongs to, which Principal, when. Lets the kernel dedupe, chain cause, and attach events to one BrainActivity.  
**What it is.** Value object. `DomainEventMetadata<OpportunitiesObserved>`. Plumbing. Authors write `OpportunitiesObserved`; the Neuron’s emit/send wraps it.  
**What it is not.** A product word. Synapse provenance (that is *who soldered the road*). The whole BrainActivity (that is every firing that shares the activity).

### BrainActivity
**What it does.** Holds one stretch of work from open to settled. Alice asked for a chart; this BrainActivity is that handling. DomainEvent firings hang on it. A Rewire is about it. A Wiring may be proposed from it.  
**What it is.** Aggregate. One correlation, one Workspace, at most one active BrainActivity.  
**What it is not.** The reusable how (that is Wiring). A chat transcript. An Entity. Every trip mentioned along the way.

### Entity
**What it does.** Keeps the current belief about one thing that changes as a unit. A Trip’s city and spend. A note.  
**What it is.** Document aggregate. Classic grain + typed state. Neurons write it after they decide. It does not emit. It does not own synapses.  
**What it is not.** A Neuron. A DomainEvent. A bag of JSON. A grain minted per DomainEvent.

### Reshape
**What it does.** Changes one DomainEvent type into another so the target Neuron can accept it. `Reshape<OpportunitiesObserved, ChartPointsAdded>`.  
**What it is.** Domain service. Compiled, registered, pure. Type-checked when the Synapse is installed.  
**What it is not.** The Synapse (the Synapse is the subscription). A JSON `to:…{Y=Amount}` string. Something that rewires the graph.

### Rewire
**What it does.** Says this BrainActivity used the wrong Synapse. “Chart, not a paragraph.” Evidence. After authorization, BrainGraph replaces that Synapse (same SynapseKey). The new synapse’s provenance points at this Rewire.  
**What it is.** DomainEvent on the BrainActivity.  
**What it is not.** A silent weight change. The replace itself (that is a BrainGraph command). A rewrite of history. It cannot move a Synapse by merely existing.

### Wiring
**What it does.** Remembers *how this kind of BrainActivity is handled* so the next Principal does not invent it again. Roles, DomainEvent contracts, Reshape, trigger. `find_capabilities` returns a matching Wiring. Apply binds roles to *this* Principal’s Neurons and installs *their* synapses, or only fires if those synapses are already live.  
**What it is.** Aggregate. Versioned. Publishable inside a Workspace. Lineage: v2 parent v1, reason = Rewire.  
**What it is not.** A live Synapse. Alice’s Entity. Her tokens. Her journal. A chat transcript. Applying a Wiring never copies another Principal’s state.

---

## How they sit together

```text
Principal  in  Workspace
      │
      ▼
BrainActivity                         one stretch of work
      │
      ├── DomainEvent                 what happened
      │     + DomainEventMetadata<T>  this firing (stamp only)
      ├── Rewire                      owner said the route was wrong
      ├── Entity                      current trip / note / thing
      └── Synapse  on  BrainGraph     the road that carried it
              └── Reshape?            only if the next Neuron needs a different type

Wiring                                reusable how, for the next BrainActivity
```

**Emit** consults BrainGraph (visible live Synapses).  
**Send** goes to one Neuron and does not consult the graph.

---

## What may be copied between Principals

| Copied when a Wiring is applied | Never copied |
|---|---|
| DomainEvent contracts, Reshape name, neuron *roles*, trigger, lineage | Tokens, Entity state, raw DomainEvent payloads, journals, transcripts, another Principal’s endpoint identities, their private Synapses |

Bob gets the Wiring. Bob gets new Synapses with `AppliedBy = Bob`. Bob never becomes Alice.

---

## Persistence (so the names do not leak into the wrong grain)

| Name | Orleans |
|---|---|
| Neuron | `DurableGrain` — journal + outbox, one write per turn |
| BrainGraph | Persistent grain — set of Synapses + history. Not a Neuron |
| BrainActivity | Persistent grain — lifecycle of one handling |
| Entity | Persistent grain + typed state, only if the thing mutates as a unit |
| Wiring | Persistent grain — versioned pattern |
| Synapse | Data inside BrainGraph. Not a grain |
| DomainEvent, DomainEventMetadata, Rewire, Reshape | Not grains. Values / registered service |

A Neuron never awaits a delivery it just staged. A refusal is a later DomainEvent with a reason, not a timeout.

---

## Speak-test

A Principal starts a BrainActivity. Neurons fire DomainEvents. DomainEventMetadata is the stamp on each firing. Synapses on the BrainGraph carry them; a Reshape runs only when the type must change. Entities hold what we believe now. If the route is wrong, the owner records a Rewire and the BrainGraph replaces that Synapse. If the handling was good, the Workspace keeps a Wiring so the next BrainActivity of that kind reuses the how, not someone else’s Entity.
