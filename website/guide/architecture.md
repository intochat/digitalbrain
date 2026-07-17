# Architecture

DigitalBrain separates a small trusted kernel from an extensible module ecosystem.

<NeuronGraph />

## Layers

| Layer | Responsibility |
| --- | --- |
| Experiences | Workspace, MCP, behavior tooling, and future clients |
| Modules | Typed capabilities such as AI, memory, Stripe, Google, and Salesforce |
| Kernel | Identity, authorization, scheduling, durable effects, and module boundaries |
| Runtime | Orleans activation and persistence, Aspire composition, telemetry |

## The kernel owns invariants

The kernel should remain small enough to reason about. It owns:

- Durable neuron addresses.
- Authenticated actor context.
- Command identity and replay protection.
- Authorization and grants.
- Revision and persistence rules.
- Effect proposal, decision, execution, and reconciliation.
- Module registration and compatibility checks.

Provider SDKs, product-specific workflows, UI projections, and external-system details stay outside the kernel.

## One address space

A neuron address identifies the logical capability, not its current process or transport:

```text
owner/{ownerId}/space/{spaceId}/neuron/{neuronId}
```

MCP, the workspace, another neuron, or a behavior that resolves the same address reaches the same logical state.

## Commands and facts

The programming model draws a hard line:

```text
typed call       = request work
fact synapse     = announce what became true
topology synapse = describe a governed relationship
effect link      = bind proposal, decision, and outcome
```

This prevents a generic event bus from quietly becoming a second command runtime.

## External effects

External mutation follows one rail:

```text
propose → decide → claim → execute → reconcile
```

Every connector supplies a deterministic provider idempotency key. Execution ends in `Succeeded`, `Failed`, `Declined`, or `OutcomeUnknown`; an unknown provider outcome is never blindly retried.

## Current and target architecture

The repository is being rebuilt from the kernel outward. Some current code still uses a universal invocation envelope internally. The target public programming model is typed neuron contracts, with generic JSON limited to edge codecs such as MCP and HTTP.
