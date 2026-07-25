---
title: Packages
---

# Packages

DigitalBrain is an AI-native operating system of ready-to-use neurons and synapses. It separates its
domain-neutral runtime from independently shipped domain modules. This table is the source of truth
for what ships and what each package may depend on.

| Package | Contains | Depends on |
| --- | --- | --- |
| `DigitalBrain` | Consumer metapackage | Abstractions, Client, Aspire |
| `DigitalBrain.Abstractions` | Leaf neuron and synapse contracts | nothing |
| `DigitalBrain.Kernel` | Domain-neutral silo runtime | Abstractions |
| `DigitalBrain.Client` | Typed owner-bound client | Abstractions |
| `DigitalBrain.Testing` | Development-only real three-silo `DigitalBrainFixture`, method-scoped `TestBrain`, and assembly-owned `DigitalBrainAppHostFixture<TAppHost>` with method-scoped `RunningAppHost` | Kernel, Client |
| `DigitalBrain.Aspire` | Client Generic Host integration | Client |
| `DigitalBrain.Aspire.Hosting` | One-call durable AppHost brain composition | Abstractions |
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
| `DigitalBrain.Modules.Time.Contracts` | Built one-shot Countdown vocabulary | Abstractions |
| `DigitalBrain.Modules.Time` | Built durable Countdown runtime; no Reminder or recurrence implementation | Time.Contracts, Kernel |
| `DigitalBrain.Modules.Flutter.Contracts` | Shell/scene UI vocabulary and wire aliases | Abstractions |
| `DigitalBrain.Modules.Flutter` | Shell and scene neurons; golden-backed contract drift pin | Flutter.Contracts, Kernel |
| `DigitalBrain.Quickstart.Contracts` | Built Greeter neuron and greeting facts for external authors | Abstractions |
| `DigitalBrain.Quickstart` | Built compiled Quickstart module | Quickstart.Contracts, Kernel |

| Family | Contracts | Runtime | Module hosting package | Semantic proof | Status |
| --- | --- | --- | --- | --- | --- |
| Quickstart | yes | yes | no | `DigitalBrain.Quickstart.Tests` + Quickstart AppHost | Built |
| AccountEnrichment (sample) | same package | same package | no | L0 shape + module registration; L1 multi-module composition in Integrations.Tests (Gmail→propose→session approval→AccountEnriched) | Built (opt-in) |
| AI | yes | yes | yes | typed LLM smoke (`ILlama32`); L1 Concurrent/GroupChat Respond multi-participant + session reuse; supervised IWorker unbuilt | Built (direct surface); Designed (supervised) |
| Tasks | yes | yes | no | contracts + runtime package + assembly-boundary pins; `DigitalBrain.Tasks.Tests` L1 closed loop via test-only `IWorker` | Built |
| Time | yes | yes | no | `DigitalBrain.Time.Tests` (Countdown lifecycle and recovery) | Built: Countdown only |
| Google | yes | yes | yes | AppHost selection + package graph; `DigitalBrain.Integrations.Tests` Gmail ReadMessage admit + annotation refusal on scripted MCP edge | Built |
| Salesforce | yes | yes | yes | AppHost selection + package graph; `DigitalBrain.Integrations.Tests` propose / reject / approve→Completed on scripted MCP edge | Built |
| Flutter | yes | yes | no | L0 golden + namespace/boundary pins; L1 journals in `DigitalBrain.Flutter.Tests`; L1 HTTP edge in `DigitalBrain.Ui.Tests`; AppHost selects `FlutterModule` + `digitalbrain-ui` AsClient | Built (vocabulary + C# UI edge); Designed (Dart host at `clients/digitalbrain_flutter`, host-facing edge watch, Aspire.Hosting, full chrome) |

Quickstart, Tasks, Time, and Flutter have no module `Aspire.Hosting` package because they need no
module-specific AppHost resources. AI, Google, and Salesforce do. Flutter packages are
`DigitalBrain.Modules.Flutter.Contracts` and `DigitalBrain.Modules.Flutter` with public namespace
`DigitalBrain.Flutter` (semantic neurons `IShell` / `IScene` — not an `IFlutter` god type). The
Flutter/Dart pixel host remains a Designed northbound HTTP client of `hosts/DigitalBrain.Ui` (path of
record: `clients/digitalbrain_flutter`), not a packable module, not under `modules/`, and not an
Orleans silo.

## Boundary rules

`DigitalBrain` is the consumer metapackage. It carries no assembly of its own. It does **not** reference `DigitalBrain.Kernel` or any domain module: a client chooses only the contract packages it needs, and a
silo chooses runtime modules separately.

`DigitalBrain.Kernel` is the domain-neutral neuron runtime. `AddDigitalBrainJournalStorage` refuses to start
without the durable `journal` connection used by production hosts. There is no separate development
storage package: test hosts use `DigitalBrain.Testing`; production hosts use the durable journal
connection projected by `AddDigitalBrain`.

`DigitalBrain.Testing` owns the L1 and L2 proof surfaces. L0 package-graph and assembly-boundary
checks live in `DigitalBrain.Tests` and do not need a cluster. L1 uses the real multi-silo
`DigitalBrainFixture` and method-scoped `TestBrain` for neuron and module semantics with journal
observation and controllable clock. L2 uses exclusive `DigitalBrainAppHostFixture<TAppHost>` and
method-scoped `RunningAppHost` for composition, health, and graph cleanup via Aspire APIs. Failures
are ordinary exceptions; there is no public diagnostic DTO zoo.

`IDigitalBrain` is the owner-scoped client contract and `DigitalBrainClient` is its implementation.
There is no concrete brain neuron or root-neuron interface: `DigitalBrainBuilder` owns AppHost state,
while the client addresses typed neurons within one owner.
`DigitalBrain.Client` is **not** an authentication boundary. An Orleans client is a trusted cluster
peer — authenticate the user at the application edge and bind the resulting principal to the owner
supplied to `AddDigitalBrainClient` (or `Connect` for Testing/host wiring).

`DigitalBrain.Aspire.Hosting` creates the complete durable profile with one
`AddDigitalBrain(name)` call: brain-scoped Azure Storage provides clustering, reminders, and
Blob-backed journals. Aspire run mode uses Azurite for that resource, while publish mode provisions
Azure Storage. The silo executable remains an explicit project reference because its compilation
generates the typed module catalog. Silo references receive clustering, reminders, journals,
protection material when required, and durable-resource waits. Run mode generates and persists a
secret Base64 256-bit state-protection key for local durability; Publish mode requires that secret
from the deployment environment. The key is projected only to silos, never clients. Client
references receive only the clustering connection Orleans needs for gateway discovery.

Each runtime module compiles to a typed capsule. Startup asks every available capsule to prepare
serializers for its public wire contracts, then activates runtime services and broadcast handlers
only for modules selected by AppHost. There is no string catalog, reflective configuration-method
lookup, or runtime assembly scan.

Model-provider SDKs live only in `DigitalBrain.Modules.AI`. Model-provider Aspire integrations live
only in `DigitalBrain.Modules.AI.Aspire.Hosting`. Kernel, AI Contracts, and every consumer-path
package remain model-provider-free. Each chat client is keyed by its concrete model neuron type.

The namespace and type name are the model identity.

Within one brain, Ollama models share one Ollama resource and OpenAI models share one OpenAI
resource. Each brain owns a secret `<brain-name>-ai-openai-api-key` parameter. Only silo references
receive provider endpoints, model names, and that secret parameter; client references receive none
of them.

`DigitalBrain.Security` is the shared purpose-bound durable encryption package. AI uses it today for
direct MAF sessions; supervised workflow checkpoints are a designed purpose on the same package and
are not built. `DigitalBrain.Integrations.Mcp` uses it for OAuth tokens. Neither package acquires
provider vocabulary.

`DigitalBrain.Integrations.Mcp` is southbound shared mechanics for Gmail and Salesforce. The provider
modules own endpoints, scopes, exact tool admission, arguments, semantic mapping, approval, fencing,
and reconciliation. Their public contracts expose no MCP SDK type or tool dictionary.
`hosts/DigitalBrain.Mcp` is the separate northbound server that exposes selected Neurons through
`IDigitalBrain`; it depends on public client/AI contracts and MCP server packages, never on the
southbound integration package.
