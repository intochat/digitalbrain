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
  - `DB0045` (advisory): flags use sites of APIs a unit cannot use (network, disk, processes, environment, console, reflection, expression trees, `dynamic`, native interop, threads, timers and the thread pool), so the author sees the problem at the line.
- The boundary is the synapse host, not the analyzer: before a build loads, every type and member it references must be on the host allow-list, and it may declare no P/Invoke. Units compile with `unsafe` off. Anything else refuses the deploy with a `DB0045` diagnostic naming the first reference outside the list. The allowed surface:
  - Whole namespaces: `System`, `System.Collections`, `System.Collections.Generic`, `System.Collections.Immutable`, `System.Collections.Frozen`, `System.Linq` (not `System.Linq.Expressions`), `System.Text`, `System.Text.Json` (+ `.Serialization`, `.Nodes`), `System.Text.RegularExpressions`, `System.Globalization`, `System.Numerics`, `System.Buffers`.
  - DigitalBrain: `DigitalBrain`, `DigitalBrain.Rules`, `DigitalBrain.Time`, `DigitalBrain.UI`, `DigitalBrain.Capabilities` and the shipped contract namespaces `DigitalBrain.Capabilities.Compute`, `.Connections`, `.Http`, `.Identity`, `.Secrets`, `.Storage`, `.Tools`. The synapse runtime types in `DigitalBrain` are refused (`ISynapseHost`, sources, installs, the catalog, `IViewReader`, `StateNeuron`, policies, `INeuronResolver`, `IBrainSignalLog`, privileged module types), as is everything in `DigitalBrain.Runtime.*` and `DigitalBrain.Spi`. Any other interface marked `[Capability]` is allowed, so a module's or a test's slot contract can be a slot.
  - Refused inside them: `System.Progress<T>`, `Type.GetType*` (but `typeof`), `Type.InvokeMember`, `Activator.*`, `Delegate.CreateDelegate`, `Delegate.DynamicInvoke`, `AppDomain.*`, `AppContext.*`, `Environment.*` (except `NewLine`, `TickCount64`, `CurrentManagedThreadId`), `Console.*`, `GC.*`, `ArrayPool<T>.Shared` (`ArrayPool<T>.Create` is fine).
  - Single types: `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>` (refused: `new Task`, `Task.Run`, `Task.Factory`, `Start`, `RunSynchronously`, `ContinueWith`); `CancellationToken`, `CancellationTokenSource`, `Interlocked`, `Volatile`, `Lock`, `Monitor` (`Enter` and `Exit` only, for `lock`); `ManualResetValueTaskSourceCore`, `IValueTaskSource`; `MemoryStream`, `Stream`, `TextWriter`, `StringWriter`, `StringReader`; `InAttribute`, `CollectionsMarshal`.
  - What the compiler emits: the `System.Runtime.CompilerServices` attributes, builders and awaiters for records, nullable, `required`, `init`, `ref readonly`, collection expressions, inline arrays, `params` spans, interpolated strings, switch expressions, async, iterators and async streams; `RuntimeHelpers` (not `GetUninitializedObject`, `RunClassConstructor` and the other runtime hooks); the `System.Diagnostics` debugger attributes, `SetsRequiredMembersAttribute`, `UnscopedRefAttribute`, `System.Reflection.DefaultMemberAttribute`.
  - Raw memory helpers: `Unsafe.As`, `Unsafe.Add`, `MemoryMarshal.CreateSpan` and `MemoryMarshal.CreateReadOnlySpan` pass only when every call site is inside the compiler's `<PrivateImplementationDetails>` type (the host reads the IL); every other `Unsafe` and `MemoryMarshal` member is refused.
  - The exact list is `AllowedApis` in `DigitalBrain.Runtime.Synapses`.
- External interactions must be scheduled through capabilities or out-of-process sandboxes.
