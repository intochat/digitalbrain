# Programming model

DigitalBrain offers typed module contracts over one universal kernel envelope.

## Define a typed façade

```csharp
public interface IWeb : INeuronContract
{
    [NeuronContract("web.fetch.v1")]
    Task<WebReply> FetchAsync(WebFetch request);
}
```

The contract marker and method attribute are implemented. A supported method has one argument and returns `Task<TResult>`.

## Create a proxy

```csharp
var web = NeuronProxy.Create<IWeb>(
    clusterClient,
    "local-owner|main|web/example",
    "local-owner|actor/example|session/dev");

var reply = await web.FetchAsync(request);
```

`NeuronProxy` serializes the argument, creates a `NeuronInvocation`, resolves `INeuron`, and deserializes the receipt output. It currently creates a fresh command identifier internally.

## Implement a kind

```csharp
public sealed class ExampleKind : INeuronKind
{
    public string Kind => "example";
    public string[] Contracts => ["example.run.v1"];

    public ValueTask<KindResult> InvokeAsync(
        NeuronContext context,
        NeuronInvocation invocation) => throw new NotImplementedException();

    public string Project(NeuronContext context, string projection) => "{}";
}
```

A kind validates the contract and input, computes output and events, and returns a `KindResult`. `NeuronGrain` owns journaling and receipt replay.

## Edge codecs

MCP and HTTP call `INeuron` with the same `NeuronInvocation` envelope. They do not currently resolve typed CLR interfaces. Moving the generic envelope entirely behind typed codecs is a **Target**.

## Effects

A kind can include an `EffectProposal` in its result. The kernel creates an effect neuron and returns its key. Approval and proof claiming are implemented; production authentication, provider idempotency, execution reconciliation, and unknown-outcome handling remain **Targets**.
