# DigitalBrain Core architecture

**Status:** current and binding for sealed durability.

DigitalBrain Core is a thin programming model for module behavior, backed by a
Hosting-owned durable runtime. The seam is deliberately deep: a module sees a
small behavior interface, while one Hosting adapter absorbs Orleans identity,
activation, state storage, serialization, journal recording, and delivery.

## Package graph

```text
module ──> Abstractions + Core
Access ──> Core
Hosting ──> Abstractions + Core + Access + Orleans
```

Only production Hosting references Orleans; test-host infrastructure references
it only to run the mechanical verification. At composition time Hosting rejects
a behavior or vocabulary assembly that directly references Orleans, Access, or
Hosting. This makes the durable implementation structurally unavailable to a
module author rather than merely discouraged.

## Public seam

The behavior-facing Core interface is:

```csharp
public abstract class Neuron
{
    protected NeuronId Id { get; }
    protected void Emit(Synapse synapse);
}

public abstract class Neuron<TState> : Neuron
    where TState : class, new()
{
    protected TState State { get; set; }
}

public interface INeuron<in TSynapse>
    where TSynapse : Synapse
{
    Task HandleAsync(TSynapse synapse, CancellationToken cancellationToken);
}
```

`Neuron` is a plain behavior facade, not a durable runtime base. Hosting makes a
fresh behavior instance for each received synapse and binds it for the duration
of `HandleAsync`. `Emit` stages a produced synapse; `State` is optional and is
recorded only when touched.

`NeuronId(kind, name)` is the logical identity. Hosting receives the explicit
kind from registration, and canonical synapse kinds are C# full type names.

## Composition

Hosting accepts an explicit vocabulary and explicit behavior registrations:

```csharp
composition.RegisterVocabulary(moduleAssembly)
           .RegisterNeuron<ModuleBehavior>("module.behavior");
```

There is one private durable host grain type. Its native key encodes a logical
`NeuronId`; it is an adapter for one behavior instance, not a global coordinator.
External publication uses `SynapsePublisher` and `SynapseSource`. Hosting maps a
source to a reserved logical identity before recording it.

## One-turn flow

```text
source publication or delivery
  → Hosting stages received work, produced synapses, and optional state
  → Hosting records the complete turn as one durable unit
  → the post-record outbox delivers each recorded target
  → a receiver handles a later turn
```

The journal is the ordered source of truth. A source publication records its
produced synapse before returning. A behavior turn records its received synapse,
all staged outputs, touched state, and watermark together. Failed recording
poisons the current host so it reloads recorded truth. Delivery is at-least-once
and receiver watermarks make a duplicate a successful no-op.

Before a turn with pending delivery is recorded, Hosting arms a durable wakeup.
It then performs an immediate post-record delivery attempt. A wakeup retries
later if necessary; terminal exhaustion produces `DeliveryFailed` as another
recorded synapse. A failure to deliver that terminal outcome is settled without
producing a recursive failure chain.

## Journal reads

`JournalReader.ReadAsync` returns one of two outcomes:

- `JournalPage`: ordered `JournalRecord` values, an exact continuation, and the
  observed journal end.
- `JournalHistoryUnavailable`: the requested continuation predates retained
  journal history.

`JournalRecord` carries direction (`Received` or `Produced`), origin,
causation, a delivery-target snapshot, and raw JSON serialization. The reader
does not rehydrate module objects. A read may activate the host to access its
durable state, but it never invokes behavior, records work, or resumes delivery.
The initial available range begins at position 1; a cursor below that range is
reported as unavailable rather than treated as an untyped argument error.

## Non-goals

This Core does not define product vocabulary, product behavior, product
scheduling, topology mutation, alternate communication modes, or a global
coordinating grain. It does not learn from journals itself. Product code may
export or analyze recorded journal truth for evaluation and fine-tuning without
expanding the Core surface.

## Migration and proof

The sealed runtime uses a new journal schema. An earlier journal must be
exported or migrated outside the runtime before activation; there is no hidden
legacy identity map.

Mechanical proof covers the seam: Core and clean behavior modules have no
Orleans reference, composition rejects prohibited references, behavior instances
are fresh while touched state persists, publication records before delivery, and
journal pages preserve ordered raw serialization.
