---
title: DigitalBrain.Kernel
---

# DigitalBrain.Kernel

The runtime a silo hosts. This is the only package that references provider SDKs, and the only place a
model API key is ever configured.

## Wiring

```csharp
builder.UseOrleans(silo => silo
    .AddDigitalBrain()
    .AddDigitalBrainJournalStorage(builder.Configuration));

builder.Services.AddDigitalBrainModels(builder.Configuration);
```

`AddDigitalBrain()` is the single entry point for the runtime: journal storage plumbing, JSON journal
format, the owner-bound incoming call filter, silo metadata, and `[PinToSilo]` placement.

`AddDigitalBrainModels(configuration)` is separate and goes on the service collection, not the silo
builder, because it binds `IChatClient` instances rather than Orleans components. Without it no model
tier is bound and `AskModelAsync` throws — a silo that skips this line runs neurons perfectly well
right up until one asks a model. This is the exact wiring `hosts/DigitalBrain.Host` uses.

`AddDigitalBrainJournalStorage(configuration)` reads the `journal` connection string, and the host
**refuses to start** without one. That is deliberate. A neuron's journals are its durability; a silo that starts without
durable journal storage is a silo that will quietly lose the thing the framework promises.

## Neuron

```csharp
public abstract class Neuron : DurableGrain, INeuron, IRemindable
{
    protected Task SendAsync(NeuronId receiver, Synapse synapse);
    protected Task ReplyAsync(Synapse synapse);
    protected Task EmitAsync(Synapse synapse);
    protected Task<string> AskModelAsync(ModelTier tier, string prompt, CancellationToken cancellationToken);
}
```

Three verbs address other neurons. `SendAsync` is point-to-point. `ReplyAsync` addresses the caller of
the synapse currently being handled and throws if there is nothing being handled. `EmitAsync` broadcasts
to every handler **type** composed for that synapse via `AddBroadcastHandlers`, minting one instance
per type from the firing correlation, **within the same owner**.

`AskModelAsync` resolves the `IChatClient` bound to a tier and throws if that tier was never bound —
an unbound tier is a configuration error, not a reason to silently degrade.

### The durable turn

Handling a synapse is one atomic unit. Everything a handler sends, replies or emits is buffered and
committed together with the record of having handled the incoming synapse. If the handler throws, the
buffered output is discarded and nothing is committed. There is no window in which a neuron has
answered without having recorded that it was asked.

### Delivery

Sending does not deliver inline — a non-reentrant grain that awaited its own receiver would deadlock on
any reply. Neuron activation rejects Orleans reentrancy and interleaving attributes because journal
order and delivery lineage require serialized turns; its protected grain-timer overloads reject
`Interleave = true`, and its legacy timer overload is unavailable because legacy timers always
interleave. A durable outbox is committed with the turn and drained by a repeating timer, backed by
an Orleans reminder so a drain survives deactivation. Delivery is at-least-once per receiver with
FIFO **per target** (no cross-target ordering), so one unreachable receiver does not stall the rest.
`SynapseId` dedupe at the receiver makes *processing* effectively-once within the remembered window.

## Placement

```csharp
[PinToSilo("gpu")]
internal sealed class Embedder : Neuron { }
```

Pins a neuron type onto silos carrying that label. The label is given to the silo at the same single
wiring point:

```csharp
builder.UseOrleans(silo => silo.AddDigitalBrain(siloLabel: "gpu"));
```

A neuron pinned to a label no silo carries will not be placed at all, so label the silos before you
pin anything to them.

## Model binding

`ModelTier` (`Fast`, `Balanced`, `Reasoning`) is an indirection over concrete models, so a neuron asks
for a *role* and deployment decides the model. `ModelDescriptor` carries the provider, model id and
key; its `ToString()` is overridden to omit the key, and a contract test holds that line.

`ModelTier` declares a fourth member, `Embedding`, which does not work: every tier is registered as an
`IChatClient`, and an embedding model is an `IEmbeddingGenerator<string, Embedding<float>>`. Do not
declare it. See [open debts](/status#open-debts).

## Telemetry

`SynapseTelemetry.ActivitySourceName` is `"DigitalBrain"`. Every handled synapse opens an activity
tagged with the synapse type, receiver and correlation id — which is also how simulations assert on the
timeline without reaching into internals.
