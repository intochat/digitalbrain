---
title: DigitalBrain.Abstractions
---

# DigitalBrain.Abstractions

The vocabulary both sides of a brain agree on. It carries no runtime, no provider SDK and no Orleans
server dependency, so a synapse contract library can be shared between a silo and a client without
dragging either into the other.

## Synapses

```csharp
public abstract record Synapse;

public sealed class SynapseDelivery
{
    public Synapse Synapse { get; }
    public SynapseId SynapseId { get; }
    public CorrelationId CorrelationId { get; }
    public SynapseId? CausationId { get; }
    public NeuronId Caller { get; }
    public long Sequence { get; }
    public DateTimeOffset Timestamp { get; }
}
```

A synapse is only the fact its author declared. The kernel takes a serialization-aware snapshot and
wraps it in a `SynapseDelivery`, whose constructor is not public, before the fact can cross a neuron
boundary. That snapshot keeps a later mutation of the author's object from changing either side of
the rail. Handlers receive their own plain typed snapshot; journal readers receive the unchanged
envelope so identity and lineage remain observable without polluting the payload:

| Member | Meaning |
| --- | --- |
| `Synapse` | The plain typed fact |
| `SynapseId` | Identity of this message, used for effectively-once processing |
| `CorrelationId` | The conversation this message belongs to |
| `CausationId` | The synapse that directly caused this one |
| `Caller` | The neuron that emitted it |
| `Sequence` | Its monotonic position in the caller's outgoing feed |
| `Timestamp` | When the kernel created the delivery |

Receiver selection is an outbox decision, not recorded payload metadata. Sending, replying and
broadcasting therefore share one envelope shape while correlation survives a chain of neurons and
causation stays a tree rather than a guess. The envelope's `Sequence` belongs to its caller; a
journal read's `ResumeSequence` is the receiving feed's independent cursor.

## Identity

`OwnerId` is the tenancy boundary. `NeuronId` is `(type, owner, name)` and knows how to become an
Orleans `GrainId`; `NeuronId.For<TNeuron>(owner, name)` is the typed form.

## Declaring behaviour

```csharp
public interface IHandle<TSynapse> { Task HandleAsync(TSynapse synapse, CancellationToken cancellationToken); }
public interface IEmit<TSynapse> { }
```

`IHandle<T>` is the behaviour. `IEmit<T>` is a declaration with no members: it states what a neuron may
produce, which is what makes the wiring of a brain statically inspectable instead of discoverable only
by running it.

## Also here

`INeuron` and `ISessionNeuron` (the grain interfaces a client talks to), `ISubscriptionRegistry`,
`JournalKind`, `ModelTier`, `ModelProviders`, and `NeuronAuthorizationException` — the refusal a caller
sees when it addresses a neuron belonging to another owner.
