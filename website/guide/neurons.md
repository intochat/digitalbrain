# Neurons

A neuron is an addressable capability hosted by `NeuronGrain`.

## Address

The implemented key format is:

```text
owner|space|kind/instance
```

The address is a logical identity. `kind` selects an `INeuronKind`; `instance` identifies one capability within that kind.

## Kernel contract

`INeuron` is the universal Orleans contract:

```csharp
public interface INeuron : IGrainWithStringKey
{
    Task<NeuronDescription> DescribeAsync();
    Task<NeuronSnapshot> ReadAsync(string projection);
    Task<NeuronReceipt> InvokeAsync(NeuronInvocation invocation);
    Task<NeuronEventPage> ReadEventsAsync(long fromRevision, int max);
}
```

`NeuronGrain` owns the shared path. It resolves the address, dispatches to an `INeuronKind`, records returned events, and caches command receipts for replay.

## Typed client façade

Module-facing interfaces implement `INeuronContract` instead of inheriting directly from `INeuron`:

```csharp
public interface IChat : INeuronContract
{
    [NeuronContract("chat.post.v1")]
    Task<ChatPostReply> PostAsync(ChatPost post);
}
```

`NeuronProxy.Create<T>` turns that interface into a client proxy. Its current supported method shape is exactly one argument returning `Task<TResult>`. The proxy serializes the argument and invokes the universal grain contract.

::: warning Retry boundary
The current `NeuronProxy` generates a new command identifier for every call. MCP callers can supply a stable `commandId`; typed proxy callers cannot yet control it. Caller-controlled typed idempotency is a **Target**.
:::

## Kind strategy

An `INeuronKind` declares its kind name and accepted contract names. It receives a `NeuronContext`, returns domain events and output, and projects state for reads.

This split is the implemented model:

```text
NeuronGrain = shared execution rules
INeuronKind = capability-specific behavior
INeuronContract + NeuronProxy = typed client façade
```

## Identity and storage limits

Edges currently inject development callers rather than authenticated sessions. The local host uses volatile journal storage. These are explicit development limits, not properties module authors can assume away.
