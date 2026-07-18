# v2 Substrate — the two primitives

Everything in the core is one of these two types or a composition of them.

## Synapse — the only message

An immutable `record`. Routing is **metadata on the message**, never a subtype (see [02](02-ino-and-broadcast.md) for why).

```
abstract record Synapse
  Metadata: { SynapseId, CorrelationId, CausationId?, Caller, Receiver, Routing, Timestamp }
```

- **Open hierarchy.** Bundles add their own `: Synapse` records. This openness is why synapses are *not* a closed union — see [04](04-minimum-and-roadmap.md#unions).
- **Causation/correlation are stamped automatically** when a neuron fires while handling an incoming synapse, so the timeline is a connected trace.
- `Caller`/`Receiver` are `NeuronId`s (type + key). `Receiver` is `None` for broadcasts.

## Neuron — the only actor

A grain. It is the *only* place state, telemetry, logs, and behavior live. One neuron = one capsule.

```
abstract class Neuron : Grain, IAsyncObserver<Synapse>
  // receive
  OnNextAsync(Synapse)          // broadcast arrives from the timeline
  DeliverAsync(Synapse)         // point-to-point arrives directly
       └─► dispatch to IHandle<T>.HandleAsync   (expression-compiled, cached)
  // fire (the one verb, two routings)
  Emit(Synapse)                 // broadcast onto the timeline
  Ask(NeuronId target, Synapse) // point-to-point to one neuron
  Reply(Synapse)                // point-to-point back to the incoming Caller
  // capsule facets (inherited, free)
  State / Telemetry / Logger
```

### Receiving: how a broadcast finds its handlers

There is exactly **one global timeline stream** (in-memory provider for the minimum). On activation, a neuron whose interface declares `IHandle<T>` for any `T` subscribes to the timeline and processes only the synapse types it handles. Point-to-point `Ask` bypasses the timeline and calls the target grain directly.

> Minimum choice: timeline-filter routing (every handler-neuron subscribes, filters by handled type). It is the least code that is still correct. The production substrate can later shard this with a subscription registry — the neuron API does not change.

### Firing: dispatch and cycle safety

- `Emit`/`Ask`/`Reply` all funnel through one private `Fire(synapse, routing)` that stamps headers, appends to the outgoing journal, and publishes.
- Depth + visited-set guards (carried on `RequestContext`) stop infinite synapse storms. Default depth cap 10.

## IHandle\<T\> and IEmit\<T\> — the wiring manifest

The keystone of v2. These live on the **Contracts interface**, not the implementation:

```csharp
// Ping.Contracts — pure metadata, zero logic, the only thing the OS must load
public interface IPingNeuron : INeuron, IHandle<Ping>, IEmit<Pong> { }
```

- `IHandle<T>` = an **in-edge** (this neuron consumes `T`).
- `IEmit<T>` = an **out-edge** (this neuron can fire `T`).
- The constellation graph = match every `IEmit<X>` to every `IHandle<X>` across all Contracts assemblies. **No implementation is loaded; nothing executes.**
- The implementation supplies the *bodies*: `class PingNeuron : Neuron, IPingNeuron`. The dispatcher reads the handled-type set from the interface, so discovery and execution share one source of truth.

This is the typed, compiler-checked twin of the `.ino` `using` / `broadcasts` block ([02](02-ino-and-broadcast.md)) and the data behind the UI catalog ([02 §UI](02-ino-and-broadcast.md#ui-is-neurons)).

## IDigitalBrain — a neuron with one job

```csharp
public interface IDigitalBrain : INeuron
{
    Task Fire(Synapse synapse, CancellationToken ct = default);
}
```

The brain is itself a neuron (everything is). `Fire` is the single entry point the outside world (a console, a UI, a Simulation) uses to inject a synapse. Broadcast vs point-to-point is decided by the synapse's `Routing` metadata, set by the calling verb.

## Everything-is-a-neuron, concretely

| Concept | Its neuron |
|---|---|
| The OS coordinator | `Brain : Neuron, IDigitalBrain` |
| A capability | `PingNeuron : Neuron, IPingNeuron` |
| A test | `PingSimulation : Simulation` (and `Simulation : Neuron`) |
| A UI widget | a neuron whose `IEmit`/`IHandle` are tap-events + a `ui:` layout |
| The wiring catalog | built by scanning Contracts (could itself be served by a `CatalogNeuron`) |

If you reach for a non-neuron service, ask first whether it should be a neuron.
