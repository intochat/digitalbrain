# DigitalBrain

Neurons, connected by synapses, exchanging signals, within a brain.

This package defines what DigitalBrain *means*, on any runtime. It has no dependencies, and a
test enforces that. A kernel (today: Orleans, under `Kernel/`) is a licensed implementation of
this meaning; changing how a kernel hosts or delivers never changes this package — observers'
object references, channels, watermark tables and leases are kernel vocabulary and must not
appear here.

The model, one role per type:

| Role                | Type               | Job                                          |
| ------------------- | ------------------ | -------------------------------------------- |
| Publishing endpoint | `INeuron`          | holds state; publishes in its own name; where synapses form (`Watch`/`Unwatch`) |
| Receiving endpoint  | `INeuronObserver`  | the dendrite: what a signal arrives at       |
| Connection          | `ISynapse`         | the formed link; carries the signals; dispose to sever |
| Fact                | `Signal`           | what crosses; `Publisher` stamped by the kernel |
| Identity            | `NeuronId`         | a value, never a capability                  |
| Space               | `IDigitalBrain`    | resolves neurons; the brain resolves, it does not switch |

The laws every kernel must honor:

1. **Provenance is inviolable.** `Signal.Publisher` is stamped at publish by the kernel;
   nothing publishes in another neuron's name.
2. **State lives in neurons, never in processes.** Processes are caches; a neuron-held synapse
   is neuron state like any other, which is the whole of what "durable subscription" means.
3. **Delivery is at-least-once.** Neuron-held synapses resume from their watermark; observers
   dedup through neuron state.
4. **Derived state is re-derived wholesale**, never incrementally patched.

Observer and synapse are endpoint and connection, not two mechanisms: push (an observer you
bring) and pull (a synapse's `Signals()` stream) are the same synapse with different holders.
Typed subscription (`brain.On<T>(source)`) is ergonomics the script SDK supplies over `Watch`,
not a concept of the model.

Admission rule: a member enters this package only if changing it changes what DigitalBrain
means on any runtime. If a change only alters how a kernel hosts or delivers, it belongs in
that kernel's ring.
