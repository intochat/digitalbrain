# Architecture decisions

This page tracks choices that must be explicit before the kernel contract becomes stable.

## Accepted direction

### Neuron identity

Every durable capability shares one logical address space. Public behavior is expressed through specialized typed contracts.

### Command versus fact

Commands are typed calls. Durable announcements are fact synapses. Topology and effect relationships have separate schemas.

### Effects

External mutation requires a provider idempotency key and a terminal outcome that includes `OutcomeUnknown`.

### UI granularity

Destinations, windows, feeds, and long-lived surfaces may be neurons. UI blocks are immutable values.

## Open decisions

### Module isolation

Choose the first supported boundary for community runtime code: governed in-process loading, isolated process, or another sandbox.

### Contract compatibility

Define semantic-version rules for typed neuron contracts and serialized fact schemas.

### Persistence baseline

Keep stable Orleans persistence behind ports while evaluating where journaled execution provides enough value to justify its maturity cost.

### Approval authority

Specify how an authenticated human decision differs from the actor that proposed an external effect.
