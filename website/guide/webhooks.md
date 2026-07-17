# Webhook neurons

Webhooks are ingress, not a shortcut around the neuron model.

## Contract

A connector may expose a specialized webhook neuron:

```csharp
public interface IWebhookNeuron : INeuron
{
    Task<WebhookReceipt> AcceptAsync(WebhookDelivery delivery);
}
```

`AcceptAsync` has one job: authenticate, deduplicate, persist, and acknowledge the delivery quickly.

## Stripe example

```text
Stripe
  → connector endpoint
  → verify signature against raw request body
  → derive delivery identity from Stripe event id
  → WebhookInboxNeuron.AcceptAsync
  → persist accepted fact
  → return 2xx
  → process asynchronously through typed neurons
```

The connector owns the HTTP details and secret material. The webhook neuron owns durable delivery identity and processing state.

## Required invariants

- Signature verification uses the exact raw request bytes.
- Provider event identity is the deduplication key.
- Duplicate deliveries return the original receipt.
- Acknowledgement does not wait for long-running domain work.
- Secrets never enter fact payloads, journals, or UI projections.
- Inbound facts cannot execute an external mutation directly.

## Effects remain governed

A webhook may trigger internal processing or propose a new effect. It cannot approve or execute that effect. External mutation still follows the kernel effect rail.
