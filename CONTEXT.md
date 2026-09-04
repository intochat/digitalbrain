# DigitalBrain

A personal assistant whose durable graph a user (or the assistant) programs with typed C#.

The sentence that settles naming: **a neuron fires a signal along a synapse**.

## Graph

**Neuron**
A durable actor. It receives and emits typed Signals, owns its Synapses and journals, and keeps its own state.
_Avoid_: agent, service, grain (product language)

**Signal**
A typed, immutable message. Identity, causation, correlation, and ownership ride the delivery envelope, not the payload.
_Avoid_: event, bus message, “synapse” as a message

**Synapse**
A directed, typed, weighted edge between two Neurons. Lives on the source. Strengthens when the receiver **handles** the signal; decays when unused. Anatomy, not traffic.
_Avoid_: message, subscription grain, journal entry

**Journal**
A bounded window over a Neuron’s incoming or outgoing Signals. How scripts notice that something happened.
_Avoid_: event store, execution history, a record of Synapses

**Entity**
A live snapshot (Chart, Surface). Direct typed reads/writes. Not on the graph: no journal, no synapses, not a signal target.
_Avoid_: neuron, run history

**IDigitalBrain**
The owner’s typed handle: `Get<TNeuron>`, `GetEntity<TEntity>`, journals. The assistant and scripts use this, not Orleans.

## Programming

**Script**
User- or assistant-authored C#, compiled against module contracts, executed outside the silo.

**Behavior**
An admitted script that keeps running. It watches journals and sends typed Signals / writes Entities.

Trigger is type-safe: you may `Send` `TSignal` only to a neuron that `IHandle<TSignal>`s it.

```csharp
await Brain.Get<IBehaviors>().SendAsync(new AdmitBehavior("elon-chart", source));
await Brain.Get<IXAccount>("elon").SendAsync(new PublishPost("starship"));
await Brain.GetEntity<IChart>("elon-activity").Append(point, title);
```

English is how the owner asks. A compiled script is what they get. There is no second runtime, grant catalog, or JSON capability bus for this path.
