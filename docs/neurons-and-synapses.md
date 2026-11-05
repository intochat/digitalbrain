# Neurons and Synapses

Neurons and synapses form the core state and event machinery of DigitalBrain.

## Neurons

A **Neuron** is a stateful actor addressable by `(BrainId, NeuronType, NeuronKey)`.
- Neurons process messages and signals sequentially.
- State transitions occur via `Save(state, signal)`.
- Emitted signals are committed with state and routed to subscribers according to the [delivery contract](delivery-contract.md).

## Synapses

A **Synapse** is an in-process reaction unit marked with `[Synapse]` that reacts to signals emitted by neurons.
- Synapses execute inside isolated load contexts.
- Roslyn analyzers (`DB0041`–`DB0044`) enforce pure state updates: no raw disk I/O, no network calls, no thread pool spawning.
- External interactions must be scheduled through capabilities or out-of-process sandboxes.
