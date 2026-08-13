# CoreV2 pseudocode

Dictionary words only. Two BrainActivities that disagree on purpose.

- Scenario 2 — topology only: Neurons, Synapses, Rewire, Reshape, Wiring. No Entity.
- Scenario 5 — a thing is kept: Entity `Trip` is not a Neuron.

Language: [COREV2-DICTIONARY.md](COREV2-DICTIONARY.md). Stories: [COREV2-SCENARIOS.md](COREV2-SCENARIOS.md).

---

## Scenario 2 — wrong Synapse, then Rewire

**On the board:** Workspace `SalesOps`. Principal `Alice`.  
**Neurons:** `salesforce`, `chat`, `chart`.  
**Entities:** none.  
**Reshape:** `OpportunitiesToChartPoints`.  
**SynapseKey:** `alice/opportunities-out`.

```csharp
record OpportunitiesObserved(IReadOnlyList<Opportunity> Rows) : DomainEvent;
record ChartPointsAdded(IReadOnlyList<ChartPoint> Points)     : DomainEvent;
record Rewire(SynapseKey Key, Endpoint NewTarget, ReshapeId? Reshape) : DomainEvent;

class SalesforceNeuron : Neuron, IEmit<OpportunitiesObserved> { }
class ChatNeuron       : Neuron, IAccept<UserSaid>, IAccept<OpportunitiesObserved> { }
class ChartNeuron      : Neuron, IAccept<ChartPointsAdded> { }

// Entity: none. Rows die with the DomainEvent.

class OpportunitiesToChartPoints : Reshape<OpportunitiesObserved, ChartPointsAdded> { }

BrainActivity activity = Open(workspace: SalesOps, principal: Alice);

// --- invent, wrong sink ---
graph.Install(new Synapse(
    Key:      "alice/opportunities-out",
    From:     salesforce,
    Contract: typeof(OpportunitiesObserved),
    To:       chat,
    Reshape:  null,
    Until:    now + 1.day,
    Provenance: (Alice, activity, "show opportunities")));

salesforce.Emit(new OpportunitiesObserved(rows));
// DomainEventMetadata<OpportunitiesObserved> stamped: id, cause, activity, Alice, now
// BrainGraph resolves Synapse → chat. Chat dumps a paragraph.

// --- owner: "as a chart, not a paragraph" ---
activity.Record(new Rewire(
    Key:      "alice/opportunities-out",
    NewTarget: chart,
    Reshape:  typeof(OpportunitiesToChartPoints)));
// Rewire does not move the joint.

graph.Replace("alice/opportunities-out", to: chart, reshape: OpportunitiesToChartPoints,
              provenance: (Alice, activity, causedBy: that Rewire));
// Old Synapse stays in BrainGraph history. Same key, new value.

salesforce.Emit(new OpportunitiesObserved(rows));
// Reshape runs. Only chart receives ChartPointsAdded. Chat does not.

wiring.Publish(v1: roles[salesforce, chart],
               contract: OpportunitiesObserved,
               reshape: OpportunitiesToChartPoints,
               trigger: "query opportunities",
               from: activity);
```

After: 3 Neurons, 1 live Synapse (chart), 1 superseded Synapse (chat) in history, 1 Wiring, 0 Entities.

---

## Scenario 5 — the trip is an Entity

**On the board:** same Workspace / Principal.  
**Neurons:** `memory`, `chat`, `chart`.  
**Entity:** `Trip`.  
**Synapses:** chat → memory on `UserSaid`; memory → chat on `TripRecorded`; later memory → chart on `TripsObserved`.

```csharp
record UserSaid(string Text)                              : DomainEvent;
record TripRecorded(TripId Id, City City, Money Spent)    : DomainEvent;
record TripsObserved(IReadOnlyList<TripSnapshot> Trips)   : DomainEvent;
record ChartPointsAdded(IReadOnlyList<ChartPoint> Points) : DomainEvent;

class MemoryNeuron : Neuron, IAccept<UserSaid>, IEmit<TripRecorded>, IEmit<TripsObserved>
{
    // thinks. writes Entity. emits. is not the trip.
}

class ChatNeuron  : Neuron, IAccept<UserSaid>, IAccept<TripRecorded> { }
class ChartNeuron : Neuron, IAccept<ChartPointsAdded> { }

class Trip : Entity<TripState>
{
    TripState State;   // City, Money, Dates — current belief
    void Remember(City city, Money spent) => State = new(city, spent);
}

class TripsToChartPoints : Reshape<TripsObserved, ChartPointsAdded> { }

// --- BrainActivity A: "I was in Prague, spent 1200 EUR" ---
BrainActivity a = Open(SalesOps, Alice);
chat.Accept(new UserSaid("I was in Prague..."));
// Synapse: chat --UserSaid--> memory   (or Send, directed)

memory.Accept(userSaid);
var trip = Grain<Trip>("alice/prague-2025-08");
trip.Remember(City: Prague, Spent: EUR(1200));
memory.Emit(new TripRecorded(trip.Id, Prague, EUR(1200)));

// Synapse: memory --TripRecorded--> chat
// Trip does not emit. Trip has no Synapse.

// --- BrainActivity B: "chart spend on my trips" ---
BrainActivity b = Open(SalesOps, Alice);

memory.Accept(analyze);
var snapshots = ReadVisibleTrips(Alice);
memory.Emit(new TripsObserved(snapshots));

graph.Install(new Synapse(
    Key:      "alice/trips-out",
    From:     memory,
    Contract: typeof(TripsObserved),
    To:       chart,
    Reshape:  typeof(TripsToChartPoints),
    Provenance: (Alice, b, "chart trip spend")));

// If spend first went to chat: Rewire, same key, target chart — same move as scenario 2.
```

After A: 3 Neurons, 1 Entity (`Trip`), 2 Synapses.  
After B: same Entity (Prague / 1200 EUR), plus Synapse memory → chart with Reshape.

---

## Difference

| | Scenario 2 | Scenario 5 |
|---|---|---|
| Thinks | Salesforce, chat, chart | memory, chat, chart |
| Holds “what is true now” | nothing | `Trip` Entity |
| What is mined | Synapses + journalled DomainEvents | same, plus Entity snapshot |
| Learning | Rewire replaces one Synapse | same move if the chart sink is wrong |

A Trip is never a Neuron. A Synapse never carries JSON. A Rewire never writes the Entity.
