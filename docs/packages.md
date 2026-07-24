---
title: Packages
---

# Packages

DigitalBrain separates its domain-neutral runtime from independently shipped domain modules. This
table is the single source of truth for what ships and what each package may depend on — replacing
the twelve separate pages that used to describe them one at a time.

| Package | Contains | Depends on |
| --- | --- | --- |
| `DigitalBrain` | Consumer metapackage | Abstractions, Client, Aspire |
| `DigitalBrain.Abstractions` | Leaf neuron and synapse contracts | nothing |
| `DigitalBrain.Kernel` | Domain-neutral silo runtime | Abstractions |
| `DigitalBrain.Client` | Typed owner-bound client | Abstractions |
| `DigitalBrain.Testing` | Development-only L1 Scenario, thin Gherkin, exclusive L2 hosting | Kernel |
| `DigitalBrain.Aspire` | Client Generic Host integration | Client |
| `DigitalBrain.Aspire.Hosting` | Core AppHost brain composition | Abstractions |
| `DigitalBrain.DevTools` | Development journals and dashboard | nothing |
| `DigitalBrain.Security` | Purpose-bound durable encryption | configuration and dependency-injection abstractions |
| `DigitalBrain.Integrations.Mcp` | Southbound official MCP transport, OAuth, token cache, and session mechanics | Security |
| `DigitalBrain.Integrations.Mcp.Aspire.Hosting` | Shared AppHost projection for MCP-backed providers | Aspire.Hosting |
| `DigitalBrain.Modules.AI.Contracts` | Provider-free AI neuron contracts | Abstractions, Tasks.Contracts |
| `DigitalBrain.Modules.AI` | Typed model neurons and MAF orchestration | AI.Contracts, Abstractions, Kernel, Security |
| `DigitalBrain.Modules.AI.Aspire.Hosting` | AI provider resources and parameters | AI, Aspire.Hosting |
| `DigitalBrain.Modules.Google.Contracts` | Gmail neuron vocabulary | Abstractions |
| `DigitalBrain.Modules.Google` | Gmail neuron over the hosted MCP boundary | Google.Contracts, Integrations.Mcp, Kernel |
| `DigitalBrain.Modules.Google.Aspire.Hosting` | Google module AppHost integration | Google, Aspire.Hosting, Integrations.Mcp.Aspire.Hosting |
| `DigitalBrain.Modules.Salesforce.Contracts` | Account-mutation neuron vocabulary | Abstractions |
| `DigitalBrain.Modules.Salesforce` | Account-mutation neuron over the hosted MCP boundary | Salesforce.Contracts, Integrations.Mcp, Kernel |
| `DigitalBrain.Modules.Salesforce.Aspire.Hosting` | Salesforce module AppHost integration | Salesforce, Aspire.Hosting, Integrations.Mcp.Aspire.Hosting |
| `DigitalBrain.Modules.Tasks.Contracts` | Durable task, worker, attempt, and blocker vocabulary | Abstractions |
| `DigitalBrain.Modules.Tasks` | Durable task lifecycle and worker attempt coordination | Tasks.Contracts, Kernel |

`DigitalBrain.Modules.Tasks` has no `Aspire.Hosting` package — the Tasks module needs no AppHost
resources of its own, unlike AI, Google, and Salesforce.

## Boundary rules

`DigitalBrain` is the consumer metapackage. It carries no assembly of its own. It does **not** reference `DigitalBrain.Kernel` or any domain module: a client chooses only the contract packages it needs, and a
silo chooses runtime modules separately.

`DigitalBrain.Kernel` is the domain-neutral neuron runtime. `AddDigitalBrainJournalStorage` refuses to start
without the durable `journal` connection used by production hosts. `DigitalBrain.DevTools` supplies
the in-memory escape hatch for local development instead of weakening that rule inside the kernel
itself.

`DigitalBrain.Client` is **not** an authentication boundary. An Orleans client is a trusted cluster
peer — authenticate the user at the application edge and bind the resulting principal to the owner
supplied to `Connect`.

Model-provider SDKs live only in `DigitalBrain.Modules.AI`. Model-provider Aspire integrations live
only in `DigitalBrain.Modules.AI.Aspire.Hosting`. Kernel, AI Contracts, and every consumer-path
package remain model-provider-free. Each chat client is keyed by its concrete model neuron type.

The namespace and type name are the model identity.

Ollama models share one Ollama resource. OpenAI models share one OpenAI resource and one secret
`openai-api-key` parameter. Only silo references receive provider endpoints, model names, and that
secret parameter; client references receive none of them.

`DigitalBrain.Security` is the shared purpose-bound durable encryption package. AI uses it for direct
MAF sessions and supervised workflow checkpoints; `DigitalBrain.Integrations.Mcp` uses it for OAuth
tokens. Neither package acquires provider vocabulary.

`DigitalBrain.Integrations.Mcp` is southbound shared mechanics for Gmail and Salesforce. The provider
modules own endpoints, scopes, exact tool admission, arguments, semantic mapping, approval, fencing,
and reconciliation. Their public contracts expose no MCP SDK type or tool dictionary.
`hosts/DigitalBrain.Mcp` is the separate northbound server that exposes selected Neurons through
`IDigitalBrain`; it depends on public client/AI contracts and MCP server packages, never on the
southbound integration package.
