# Architecture decisions

## Accepted direction

### Universal kernel path

Keep one `NeuronGrain` execution path. Domain behavior belongs in registered `INeuronKind` strategies.

### Typed module façade

Use `INeuronContract` and `[NeuronContract]` for discoverable module APIs. The façade translates to the universal envelope through `NeuronProxy`.

### Commands and relationships

Contracts request work. Synapses record relationships. A future fact model must not become a second command runtime.

### Governed effects

External mutations require an explicit proposal and decision. Provider execution must eventually include deterministic idempotency, reconciliation, and an unknown-outcome state.

## Open decisions

### Infrastructure contract shape

Choose whether webhook and similar infrastructure entry points directly specialize `INeuron` or use the same typed façade as modules.

### Module isolation

Choose the first supported boundary for non-first-party runtime code: governed in-process loading, isolated process, or another sandbox.

### Contract compatibility

Define semantic-version rules for contract names, typed request and response shapes, and future fact schemas.

### Persistence baseline

Select a durable journal provider and define backup, recovery, and migration behavior before claiming production durability.

### Approval authority

Specify authenticated identity and grants for proposal, decision, proof claim, and provider execution.
