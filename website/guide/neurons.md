# Neurons

A neuron is an addressable capability with durable identity.

## The rule

Create a neuron when a capability needs at least one of:

- Durable state.
- Independent lifecycle.
- Authorization boundary.
- Concurrent access serialization.
- Observation over time.
- Stable address across processes.

Do not create a neuron for request DTOs, immutable UI blocks, provider response objects, helper services, or temporary calculations.

## Contract shape

`INeuron` is the common identity marker. Useful behavior comes from specialized typed contracts:

```csharp
public interface INeuron : IGrainWithStringKey;

public interface IChatNeuron : INeuron
{
    Task<PostReceipt> PostAsync(PostMessage command);
    Task<ChatSnapshot> ReadAsync();
}
```

The specialized interface is the public programming model. It gives module authors normal C# types, Orleans serialization checks, discoverable APIs, and compile-time compatibility.

## Identity is not user input

Clients do not assert their owner or actor identifier. The edge authenticates a session and injects trusted actor context into the invocation pipeline. Neurons authorize that proven context.

## Neuron granularity

Good neuron boundaries are domain boundaries:

- One chat thread.
- One connected Stripe account.
- One webhook inbox.
- One long-term memory space.
- One workspace destination.
- One external effect plan.

Blocks inside a UI document, individual table rows, and each pixel are values—not neurons.

## State

Stable Orleans persistence is the baseline. Domain state sits behind kernel persistence ports so storage can evolve without leaking provider types into public contracts.

Journaled execution may become an optimization or specialized implementation, but modules cannot require preview persistence APIs to participate.
