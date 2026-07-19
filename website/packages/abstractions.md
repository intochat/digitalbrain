---
title: DigitalBrain.Abstractions
---

# DigitalBrain.Abstractions

The vocabulary both sides of a brain agree on. It carries no runtime, no provider SDK and no Orleans
server dependency, so a synapse contract library can be shared between a silo and a client without
dragging either into the other.

## Synapses

```csharp
public abstract record Synapse
{
    public SynapseMetadata? Metadata { get; init; }
    public SynapseMetadata Stamped { get; }
}
```

A synapse is an immutable record. `Metadata` is null until the fabric stamps it; `Stamped` is the
non-null view for code that knows it is handling a delivered synapse. `SynapseMetadata` carries the
identity and lineage of a message:

| Member | Meaning |
| --- | --- |
| `SynapseId` | Identity of this message, used for effectively-once processing |
| `CorrelationId` | The conversation this message belongs to |
| `CausationId` | The synapse that directly caused this one |
| `Caller` / `Receiver` | Who sent it, and who it was addressed to |
| `RoutingMode` | `PointToPoint` or `Broadcast` |
| `Timestamp` | When it was stamped |

`ForSend`, `ForReply` and `ForBroadcast` build metadata from a cause, which is how correlation survives
a chain of neurons and how causation stays a tree rather than a guess.

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
