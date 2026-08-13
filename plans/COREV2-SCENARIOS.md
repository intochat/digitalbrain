# CoreV2 scenarios

Five BrainActivities. Only dictionary words. If a step needs another noun, the architecture is still wrong.

---

## 1. First ask, no Synapse — the DomainEvent is seen, not delivered

**Workspace** Sales Operations. **Principal** Alice.

Alice writes in chat: “show my Salesforce opportunities.” Chat is ingress only. A **BrainActivity** opens.

`find_capabilities` returns the Salesforce Neuron. No **Wiring**. The BrainGraph has no Synapse for `OpportunitiesObserved`.

The Salesforce Neuron emits `OpportunitiesObserved` (typed **DomainEvent**). **DomainEventMetadata** stamps this firing onto Alice’s BrainActivity.

**Zero receivers.** The firing is journalled on that Neuron and visible on the BrainActivity. No outbox. Alice sees a refusal with a reason: nothing is soldered to carry this DomainEvent.

This is the original “log what happened” instinct, without pretending a pulse is a grain.

---

## 2. Invent the wrong Synapse, then Rewire

Same Workspace. Same Principal. A new **BrainActivity**, or the same one continuing after the refusal.

Alice (or the model via `fire`) installs a **Synapse**: Salesforce Neuron → chat Neuron, contract `OpportunitiesObserved`, no Reshape. Trial expiry set. Provenance: Alice, this BrainActivity, intent “show opportunities.”

Emit again. Chat accepts `OpportunitiesObserved` and dumps a paragraph. Wrong.

Alice: “as a chart, not a paragraph.” That is a **Rewire** DomainEvent on the BrainActivity. It does not move the joint.

Authorized replace at the **same SynapseKey**: source unchanged, target = chart Neuron, **Reshape** `<OpportunitiesObserved, ChartPointsAdded>` (registered, type-checked). Old Synapse stays in BrainGraph history. New Synapse provenance points at the Rewire, not at the old joint.

Emit again. Only the chart Neuron receives `ChartPointsAdded`. Chat does not.

A **Wiring** v1 is written from this BrainActivity: roles (salesforce, chart), contracts, Reshape, trigger. Trial.

Proves: learning is Rewire + replace, never a weight.

---

## 3. Same Principal, next BrainActivity — fire only

Alice asks again: “opportunities as a chart.”

New **BrainActivity**. `find_capabilities` returns Wiring v1 and **already live** synapses on her endpoints.

No invent. No Rewire. The trigger fires. Salesforce emits. BrainGraph resolves the live Synapse. Reshape runs. Chart Neuron accepts.

The BrainGraph is not written. A usage projection may count this later, off the journal, between turns.

Proves: reuse is “synapses still soldered,” not a new design.

---

## 4. Another Principal — Wiring, not Alice’s Entity

**Principal** Bob, same **Workspace**.

Bob asks the same kind of thing. `find_capabilities` returns the published **Wiring**, applicability, and that Bob has compatible Neurons (his Salesforce, his chart). It does not show Alice’s endpoint ids, token, journal, or Entity.

Apply: bind roles to **Bob’s** Neurons. Install **Bob’s** Synapses. Provenance: `AppliedBy = Bob`, this BrainActivity, `BasedOnWiring = v1`. Then fire. If those synapses were already live for Bob, only fire.

**Copied:** contracts, Reshape name, roles, trigger, lineage.  
**Never copied:** Alice’s token, opportunity rows, trip Entity, transcript, private Synapses.

If Bob has no Salesforce authorization, settled refusal with that reason. No borrowed Synapse.

Proves: team memory is Wiring. The BrainGraph of use is not a shared bag of people.

---

## 5. Entity for a trip — the thinker is not the trip

Alice: “I was in Prague in August, spent 1200 EUR.”

**BrainActivity** opens. A memory Neuron accepts `UserSaid`, decides this is a trip, and writes **Entity** `Trip` (city Prague, spend 1200 EUR). The Neuron emits `TripRecorded`. A Synapse may carry that to chat as a note. The Trip Entity does not emit. The Trip Entity does not own synapses.

Later: “analyze my last trips and show a chart of spend.”

New BrainActivity. `find_capabilities` may hit a Wiring (trips → chart) or invent. The memory Neuron reads Alice’s Trip Entities (snapshots), emits `TripsObserved`. A Synapse with Reshape `<TripsObserved, ChartPointsAdded>` reaches the chart Neuron.

A Rewire here is the same as scenario 2: if spend went to chat as a paragraph, Alice Rewires to the chart. Same SynapseKey.

Voice of the same sentence: upload audio → text → same chat ingress → same BrainActivity. No VoiceInput Neuron.

Proves: durable Neuron thinks; Entity holds current belief; Synapse is how the BrainActivity was done; mining is journals + BrainGraph, not a grain per sentence.

---

## Speak-test

If you can tell all five with only Workspace, Principal, Neuron, Synapse, BrainGraph, DomainEvent, DomainEventMetadata, BrainActivity, Entity, Reshape, Rewire, Wiring — the path is right.

If you reach for Recipe, Morph, Correction, Signal, Impulse, InputGrain, or a weight on a Synapse — stop.
