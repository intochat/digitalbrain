# DigitalBrain

Clean-room rebuild in progress. Code enters this tree file by file, each file
justified by a consumer that exists today. The complete previous system lives
in `v1/` — its product status in `v1/README.md` — and is retired piecewise as
its replacement lands here.

## Built

- `src/DigitalBrain.Abstractions` — the programming model. Four dependency-free
  types: `Synapse` (the fact), `INeuron<in TSynapse>` (the actor, typed by what
  it handles), `NeuronId` (the address), `SynapseMetadata` (the birth
  certificate: source, sequence, timestamp). Namespace `DigitalBrain`.

## Next

- `DigitalBrain.Core` — the runtime library: `Neuron` base, journaling,
  serialization of plain facts, type-safe addressing, lineage via grain-call
  filters. Causation and correlation are derived from journal structure, never
  stored.
- `DigitalBrain.Kernel` — the silo host that boots the brain.
