# Synapses

Synapses connect neurons, but one word must not hide several incompatible semantics.

DigitalBrain uses three explicit synapse families.

## Topology synapses

Topology synapses describe durable relationships:

```text
Contains
Requires
Grants
UsesModule
ProjectsTo
```

They are revisioned, queryable, and authorized. They change the graph but do not invoke commands.

## Fact synapses

Fact synapses are immutable announcements produced after durable state changes:

```csharp
public sealed record MessagePosted(
    NeuronAddress Chat,
    MessageId Message,
    Revision Revision);
```

A fact says what became true. Consumers may update projections or react through their own typed contracts. Facts carry stable identity, causation, correlation, schema version, and production time.

## Effect links

External mutations need stronger semantics than ordinary topology:

```text
proposal —awaits→ decision
decision —authorizes→ execution
execution —produces→ terminal outcome
```

These relationships are enforced by the kernel effect state machine. They are not free-form records that a module can invent or self-approve.

## Delivery

Fact delivery is at least once. Consumers are idempotent by fact identity. A durable cursor records progress, bounded retries provide recovery, and poison facts become visible rather than disappearing inside an empty catch.

## What synapses are not

- Not generic commands.
- Not arbitrary JSON envelopes.
- Not an excuse to bypass typed neuron interfaces.
- Not an in-memory pub/sub contract for durable business truth.
