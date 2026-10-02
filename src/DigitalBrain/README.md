# DigitalBrain

Neurons, connected by synapses, exchanging signals, within a brain.

This package is the DigitalBrain model: the handful of types that define what those four words
mean, on any runtime. It depends on nothing — no actor framework, no serializer, no host — and
a test keeps it that way. Kernels implement the model; this package is what everything else
writes against.

## The model

A **neuron** is a stateful, addressable capability — a chart, a timer, a button, an inbox, a
brain. All state lives in neurons, never in a process; processes are caches.

A **signal** is a typed fact a neuron publishes. Its `Publisher` is stamped by the kernel at
publish, so provenance cannot be forged: there is no publish-as-someone-else anywhere.

A **synapse** is the connection a signal travels across. Watching a neuron forms one; disposing
it severs it; the signals that cross it flow through it. The receiving endpoint is an
**observer** — push (the observer's callback) and pull (the synapse's stream) are the same
synapse, not two mechanisms. When the observer is itself a neuron, the synapse lives in that
neuron's state and survives every process: that, and nothing more, is a durable subscription.

A **brain** is the space neurons live in. It resolves, it does not switch — synapses form at
neurons, the brain only finds them by identity.

```csharp
// An observer is the receiving endpoint: implement it on whatever signals arrive at.
sealed class Log : INeuronObserver
{
    public Task OnSignalAsync(Signal signal)
    {
        Console.WriteLine($"{signal.Publisher} published {signal}");
        return Task.CompletedTask;
    }
}

// Resolve a neuron by identity, watch it to form a synapse, sever by disposing.
var button = brain.Get<INeuron>(new NeuronId("button-1"));
await using var synapse = await button.Watch(new Log());

// The same signals, pulled as a stream from the synapse instead of pushed to the observer.
await foreach (var signal in synapse.Signals())
{
    // signal.Publisher is a NeuronId — a value, never a capability;
    // brain.Get turns it back into the neuron it names.
}
```

## The laws

Every kernel implementing this model must honor four laws:

1. **Provenance is inviolable.** `Signal.Publisher` is stamped at publish by the kernel;
   nothing publishes in another neuron's name.
2. **State lives in neurons, never in processes.** Anything a process holds must be
   re-creatable from neuron state.
3. **Delivery is at-least-once.** Neuron-held synapses resume from their watermark; observers
   deduplicate through neuron state.
4. **Derived state is re-derived wholesale,** never incrementally patched.

## What is deliberately absent

Hosting, delivery, serialization, persistence, scheduling — observers' object references,
channels, watermark tables, leases. That is kernel vocabulary. The reference kernel runs on
Microsoft Orleans and lives in `DigitalBrain.Kernel`; a different kernel could honor the same
laws on a different substrate without this package changing.

The admission rule that keeps it so: a member enters this package only if changing it would
change what DigitalBrain *means* on any runtime. A change that only alters how a kernel hosts
or delivers belongs to that kernel.
