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
  - `DB0045`: no banned APIs. Banned namespaces and types: `System.Net`, `System.IO`, `System.Diagnostics.Process`, `System.Environment`, `System.Console`, `System.Reflection`, `System.Runtime.Loader`, `System.Activator`, `System.AppDomain`, `Microsoft.CSharp.RuntimeBinder` (so `dynamic`), `System.Runtime.InteropServices`, `System.Runtime.CompilerServices.Unsafe`, `System.Threading.Thread`, `ThreadPool`, `Timer`, `System.Threading.Tasks.Parallel`, `TaskFactory`. Banned members: `Task.Run`, `Task.Factory`, `Type.GetType*`, `Type.InvokeMember`, `Delegate.CreateDelegate`.
    Allowed: `System.IO.MemoryStream` and `Stream`, `Environment.NewLine` and `Environment.TickCount64`, attribute types in `System.Reflection`, `InAttribute`, `typeof` (`Type.GetTypeFromHandle`); `Task`, `ValueTask`, `Task.Delay`, `CancellationToken(Source)`, `Interlocked` and `Volatile` are untouched.
    Units compile with `unsafe` off. The synapse host checks the compiled assembly before it loads: any banned type or member reference, any P/Invoke (`DllImport`) and the unverifiable-code attributes refuse the deploy, so a build that skipped the analyzer does not load.
- External interactions must be scheduled through capabilities or out-of-process sandboxes.
