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
- Roslyn analyzers enforce the synapse shape and its boundary:
  - `DB0041`: every `[Synapse]` is a gate, reaction or view.
  - `DB0042`: a synapse's `[State]` record is declared in its own assembly.
  - `DB0043` (warning): state carries no status-like member; a synapse says no through a gate.
  - `DB0044`: no `[On]` reactions in a synapse assembly.
  - `DB0045`: no banned APIs: `System.Net`, `System.IO` (except `MemoryStream` and `Stream`), `System.Diagnostics.Process`, `System.Reflection.Emit`, `System.Runtime.Loader`, `Task.Run`, `new Thread`, `Assembly.Load*`. The synapse host checks the compiled assembly's references against the same list and refuses the deploy, so a build that skipped the analyzer does not load.
- External interactions must be scheduled through capabilities or out-of-process sandboxes.
