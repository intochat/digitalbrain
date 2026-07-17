# Programming model

DigitalBrain should feel like ordinary typed C#, not a stringly typed message broker.

## Resolve a neuron

```csharp
var chat = brain.Get<IChatNeuron>(
    "owner/local/space/main/neuron/chat");

var receipt = await chat.PostAsync(new PostMessage(
    CommandId: commandId,
    Text: "Summarize today's work."));
```

`Get<TNeuron>` resolves a typed Orleans reference for the address. The command carries a stable identifier so a caller can safely retry the same intent.

## Publish facts after persistence

```csharp
public async Task<PostReceipt> PostAsync(PostMessage command)
{
    var receipt = state.Post(command);
    await persistence.SaveAsync(state);
    await facts.PublishAsync(new MessagePosted(
        this.GetPrimaryKeyString(),
        receipt.MessageId,
        state.Revision));
    return receipt;
}
```

The domain method persists before announcing the fact. Projection failure does not erase the fact; consumers resume from durable cursors.

## Propose external effects

```csharp
var effect = await stripe.ProposeRefundAsync(new RefundRequest(
    CommandId: commandId,
    PaymentId: paymentId,
    Amount: amount));
```

The proposal contains a payload digest and deterministic provider idempotency key. It does not call Stripe. A separate authorized decision advances the kernel-owned effect plan.

## Edge codecs

MCP and HTTP cannot expose every CLR interface directly. They translate versioned external envelopes into typed neuron calls:

```text
MCP JSON → validated codec → typed neuron call
HTTP JSON → validated codec → typed neuron call
```

The generic envelope ends at the edge.
