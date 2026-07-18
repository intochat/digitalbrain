# Synapses

A synapse is currently a typed relationship record attached to neuron state.

## Implemented record

`SynapseRecord` contains:

| Field | Meaning |
| --- | --- |
| `Relation` | A closed `SynapseRelation` value |
| `TargetKey` | Address of the related neuron |
| `Constraint` | Relationship-specific text |
| `Revision` | Revision at which the relationship was recorded |

The current relation vocabulary includes `Contains`, `Requires`, `Grants`, `BackedBy`, `Projects`, `CausedBy`, `Awaits`, `Approves`, `EmitsTo`, and `UsesModule`.

An `INeuronKind` may return one synapse mutation in a `KindResult`; `NeuronGrain` journals that change with the capability state.

## What synapses are not

Synapses are not a generic command bus. Work is requested through a neuron contract. A synapse records durable topology or relationship state.

## Target taxonomy

Separating topology, immutable facts, and effect relationships into independently versioned schemas is a **Target**. Durable subscriptions, cursors, and general fact propagation are also not implemented by the current thin `SynapseRecord`.
