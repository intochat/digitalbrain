# DigitalBrain

Neurons exchanging signals within a brain.

This package is the DigitalBrain model: the handful of types that define what those words mean,
on any runtime. It depends on nothing — no actor framework, no serializer, no host — and a test
keeps it that way. Kernels implement the model; this package is what everything else writes
against.

## The model

A **neuron** is a stateful, addressable capability — a chart, a timer, a button, an inbox, a
brain. All state lives in neurons, never in a process; processes are caches.

A **signal** is a typed fact a neuron publishes. Its `Publisher` is stamped by the kernel at
publish, so provenance cannot be forged: there is no publish-as-someone-else anywhere.

A **synapse** is the fact of watching, not a type. Iterating `Watch<T>` is the connection:
it forms when the loop starts and severs when the loop ends — by the token, a break, or the
brain disposing. The kernel stamps who watches exactly as it stamps who publishes; when the
watcher is a neuron, its synapse lives in that neuron's state and survives every process —
that, and nothing more, is a durable subscription.

A **brain** is the space neurons live in. It resolves, it does not switch: signals flow at
neurons, the brain only finds them by identity.

```csharp
var button = brain.Get<INeuron>(new NeuronId("button-1"));

await foreach (var click in button.Watch<Clicked>(cancellationToken))
{
    // click.Publisher is a NeuronId — a value, never a capability;
    // brain.Get turns it back into the neuron it names.
}
```

## The laws

Every kernel implementing this model must honor four laws:

1. **Provenance is inviolable.** `Signal.Publisher` is stamped at publish by the kernel;
   nothing publishes in another neuron's name.
2. **State lives in neurons, never in processes.** Anything a process holds must be
   re-creatable from neuron state — a neuron-watcher's synapse included, which is why severing
   it is the watcher's own lifecycle, never a third party's call.
3. **Delivery is at-least-once.** Neuron-held synapses resume from their watermark; watchers
   deduplicate through neuron state.
4. **Derived state is re-derived wholesale,** never incrementally patched.

## What is deliberately absent

Hosting, delivery, serialization, persistence, scheduling — observers, synapse handles,
subscription registries, watermark tables, leases. That is kernel vocabulary. The reference
kernel runs on Microsoft Orleans and lives in `DigitalBrain.Kernel`; a different kernel could
honor the same laws on a different substrate without this package changing.

The admission rule that keeps it so: a member enters this package only if changing it would
change what DigitalBrain *means* on any runtime. A change that only alters how a kernel hosts
or delivers belongs to that kernel.
