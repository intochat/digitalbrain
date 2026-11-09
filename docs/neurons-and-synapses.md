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
  - DigitalBrain: `DigitalBrain`, `DigitalBrain.Rules`, `DigitalBrain.Time`, `DigitalBrain.UI`, `DigitalBrain.Capabilities` and the shipped contract namespaces `DigitalBrain.Capabilities.Compute`, `.Connections`, `.Http`, `.Identity`, `.Secrets`, `.Storage`, `.Tools`. The synapse runtime types in `DigitalBrain` are refused (`ISynapseHost`, sources, installs, the catalog, `IViewReader`, `StateNeuron`, policies, `INeuronResolver`, `IBrainSignalLog`, privileged module types, app and module declarations, the neuron base classes and grain-shape attributes), as is everything in `DigitalBrain.Runtime.*` and `DigitalBrain.Spi`. Any other interface marked `[Capability]` is allowed, so a module's or a test's slot contract can be a slot. Every public type in the `DigitalBrain` namespace is ruled one way or the other (`UnitFacingDigitalBrainTypes` or `RefusedDigitalBrainTypes`); an architecture fact fails on a new one that is on neither.
  - The principle: allow members, never types with member deny-lists. A pure data type is allowed whole; every other type that stays allowed (`RuntimeHelpers`, `Type`, `Task`/`ValueTask` and their awaiters and builders, `CancellationToken`, `CancellationTokenSource`) is allowed member by member (`AllowedApis.MemberAllowList`), so a member a later .NET adds is refused until it is ruled on.
  - Refused inside them: `System.Progress<T>`, `Activator.*`, `Delegate.CreateDelegate`, `Delegate.DynamicInvoke`, `AppDomain.*`, `AppContext.*`, `Environment.*` (except `NewLine`, `TickCount64`, `CurrentManagedThreadId`), `Console.*`, `GC.*`, `ArrayPool<T>.Shared` (`ArrayPool<T>.Create` is fine).
  - `Type`, member by member: `typeof`, `==`/`!=`, `FullName`, `Namespace`, `Equals`, `GetHashCode`, `ToString`, `IsAssignableFrom`/`To`, `IsValueType`, `IsEnum`, `IsGenericType`; no `TypeHandle`, `GetType*`, members, interfaces or generics. `System.Text.Json` members that take a `Type` and `JsonSerializerOptions.TypeInfoResolver*` are refused, and a `typeof` argument in a custom attribute must name the unit's own type or one on the allowed surface.
  - Single types, member by member: `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>` (awaiting, `Delay`, `FromResult`, `WhenAll`/`WhenAny`, status and result; refused: `new Task`, `Run`, `Factory`, `Start`, `ContinueWith`, `ConfigureAwait`, `Yield`, `Wait`); `CancellationToken` (no `Register`), `CancellationTokenSource` (`new()`, `Cancel()`, `Token`, `CreateLinkedTokenSource`, `Dispose`, `TryReset`; no `CancelAfter`, timed constructors or `Cancel(bool)`): a unit's code never leaves the activation's turn; `Interlocked`, `Volatile`, `Lock` (`lock` on a `Lock` field; `System.Threading.Monitor`, so `lock` on a plain object, is refused); `ManualResetValueTaskSourceCore`, `IValueTaskSource`; `MemoryStream`, `Stream`, `TextWriter`, `StringWriter`, `StringReader`; `InAttribute`, `CollectionsMarshal`.
  - What the compiler emits: the `System.Runtime.CompilerServices` attributes, builders and awaiters for records, nullable, `required`, `init`, `ref readonly`, collection expressions, inline arrays, `params` spans, interpolated strings, switch expressions, async, iterators and async streams; `RuntimeHelpers` only for `InitializeArray`, `CreateSpan`, `GetSubArray` and `EnsureSufficientExecutionStack` (`Box`, `GetUninitializedObject` and every other member refused); the awaiter `OnCompleted` and builder `AwaitOnCompleted` members only from compiler-generated state machines; the `System.Diagnostics` debugger attributes, `SetsRequiredMembersAttribute`, `UnscopedRefAttribute`, `System.Reflection.DefaultMemberAttribute`.
  - Raw memory helpers: `Unsafe.As`, `Unsafe.Add`, `MemoryMarshal.CreateSpan` and `MemoryMarshal.CreateReadOnlySpan` pass only when every call site is inside the compiler's `<PrivateImplementationDetails>` type (the host reads the IL); every other `Unsafe` and `MemoryMarshal` member is refused.
  - No explicit layout: a type with `LayoutKind.Explicit` or any `[FieldOffset]` is refused (overlapping fields are type confusion); only the compiler's fieldless sizing structs inside `<PrivateImplementationDetails>` pass.
  - The exact list is `AllowedApis` in `DigitalBrain.Runtime.Synapses`.
- External interactions must be scheduled through capabilities or out-of-process sandboxes.
