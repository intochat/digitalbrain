# Implementation status

This page is the evidence ledger for the documentation.

| Label | Meaning |
| --- | --- |
| Running | Present in the current Aspire topology |
| Implemented | Backed by repository code and automated tests |
| Target | Accepted direction, not fully delivered |
| Decision | Architecture is intentionally unresolved |

## Running locally

| Resource | Evidence |
| --- | --- |
| Kernel host | `Brain.Kernel.Host` in the AppHost |
| MCP edge | HTTP MCP server with describe, read, and invoke tools |
| UI gateway | HTTP and WebSocket edge |
| Ollama | Local model resource with `llama3.1:8b` |
| VitePress documentation | `brain-docs` external HTTP resource |

## Implemented

| Area | Evidence and limit |
| --- | --- |
| Universal neuron contract | `INeuron` exposes describe, read, invoke, and event reads |
| Universal grain | `NeuronGrain` dispatches all registered address kinds |
| Kind strategies | `INeuronKind` supplies capability behavior and projection |
| Typed client façade | `INeuronContract`, `[NeuronContract]`, and `NeuronProxy` |
| Addressing | `owner\|space\|kind/instance` grain keys |
| Command replay | Receipts replay for a repeated command identifier |
| Journaled state | Events reconstruct state during the running process |
| Synapse record | A closed relation enum and one thin relationship record |
| Effect decision gate | Propose, approve or decline, then claim approval proof |
| Explicit module composition | First-party modules register in `Brain.Kernel.Host` |

## Development limits

| Area | Current limit |
| --- | --- |
| Authentication | MCP and UI use a hard-coded development caller |
| Persistence | `VolatileJournalStorageProvider` loses data on process restart |
| Typed retries | `NeuronProxy` creates a new command identifier for each call |
| Authorization | Owner and effect-state checks are not a complete grant system |
| Effect execution | No complete provider idempotency key and reconciliation lifecycle |
| Webhooks | No implemented provider endpoint or durable inbox kind |
| Module ecosystem | No manifests, compatibility resolver, dynamic loading, or sandbox |

## Targets

- Authenticated caller context and production authorization.
- Durable production journal storage.
- Caller-controlled idempotency for typed contracts.
- A complete effect executor with provider idempotency key, terminal outcomes, and reconciliation.
- Versioned fact propagation and subscription cursors.
- Implemented webhook ingress with deduplication conformance tests.

## Open decisions

- Module compatibility metadata and packaging.
- Community runtime isolation.
- Direct `INeuron` specialization versus `INeuronContract` façade for infrastructure entry points.
- Long-term persistence provider and operational recovery model.

A homepage phrase is not evidence. When code and this page disagree, update the claim or the implementation before merging.
