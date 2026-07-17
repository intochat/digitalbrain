# Webhook neurons

Webhook ingress is an architecture **Target**. The repository does not currently expose a provider webhook endpoint or a durable webhook-inbox kind.

## Target contract

The intended public shape follows the direct Orleans contract style reserved for infrastructure entry points:

```csharp
public interface IWebHookNeuron : INeuron
{
    Task<WebHookReceipt> AcceptAsync(WebHookDelivery delivery);
}
```

This is not the same as the current module façade pattern of `INeuronContract` plus `NeuronProxy`. Whether webhook ingress should remain a direct `INeuron` specialization or become another typed façade is a **Decision**.

## Target flow

```text
provider
  → connector endpoint
  → verify signature over exact raw body
  → derive provider delivery identity
  → accept and persist delivery
  → acknowledge quickly
  → process asynchronously through neuron contracts
```

## Required invariants

- Signature verification uses the exact raw request bytes.
- Provider event identity is the deduplication key.
- Duplicate deliveries return the original receipt.
- Acknowledgement does not wait for long-running domain work.
- Secrets never enter facts, journals, or UI projections.
- Inbound data cannot approve or execute an external mutation.

None of these webhook-specific invariants should be treated as implemented until an endpoint, kind, and conformance tests exist.
