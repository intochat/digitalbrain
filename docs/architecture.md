# DigitalBrain Architecture Assessment and Architecture V2

Assessment snapshot: repository branch **master** at commit **faebf08**; live AppHost observed **2026-07-09 23:37 CEST**.

Scope: architecture assessment only. No application code, dependencies, deployment configuration, or runtime state were changed.

## Evidence key

- **[R] Repository evidence** — CodeGraph was used first for architecture, symbols, call paths, dependencies, tests, and blast radius. Targeted source/configuration reads filled gaps that CodeGraph did not return.
- **[L] Live runtime evidence** — read-only Aspire MCP inspection of AppHost discovery, resource health, structured and console logs, and distributed traces.
- **[D] Official documentation** — current Microsoft Learn, Aspire, OpenTelemetry, MCP, Google, Salesforce, and Flutter documentation.
- **[I] Architectural inference** — a conclusion or recommendation derived from the preceding evidence, not a claim made by the code.

## Executive summary

DigitalBrain is a working, differentiated **local-first personal agent operating system**, not merely a chat application. Its core idea is strong: model user and system capabilities as Orleans neurons, record typed synapses in causal journals, let Ino coordinate models and tools, and route durable system evolution through an explicit proposal/approval/apply rail. Flutter is intended to remain a thin renderer for neuron-emitted surfaces, while MCP exposes the same running brain to external agents. **[R]**

The implementation is beyond a paper design. A live three-silo Orleans cluster, dedicated MCP host, Flutter client, Azurite-backed state, journal and sync stores, Ollama chat and embedding models, Whisper, Open WebUI, and Aspire Dashboard were all running and healthy. Recent traces showed real grain traffic, causal attributes, Ino timeline retrieval, and ContextNeuron-to-Ollama embedding activity. **[L]**

The architecture is not yet safe enough to support its broadest product claims:

1. **Secrets can enter durable journals.** Login commands contain plaintext passwords and are journaled before handling. Tool telemetry can also journal raw connector results. The read timeline MCP tool formats synapses without the redaction used by the causal-lineage tool. **[R]**
2. **Workspace isolation is not reliable.** All clients share one Ino grain. Its context and memory-summary paths read recent ino-main journal entries without workspace/client filtering and then label them as belonging to the current workspace. **[R][I]**
3. **The approval rail is durable only on its happy path.** A decision is journaled before an external apply runs, but there is no queued/applying attempt state, durable retry, idempotency key, or resume path. A crash in that window leaves the proposal permanently decided but not safely completed. Rollback is an emitted notification, not an executed workflow. **[R][I]**
4. **Journals are not an outbox.** Sender state is written before cross-grain delivery, but a crash between those steps has no automatic redelivery. Orleans calls are at-most-once by default; adding retries permits duplicates unless the application supplies durable inbox/idempotency semantics. Broadcasts currently use in-memory streams. **[R][D][I]**
5. **Identity and authorization are not system boundaries.** HTTP/gRPC surfaces have no application authentication middleware, client IDs are caller supplied, HTTP MCP exposes mutation and approval tools without application authorization, and an MCP caller can self-assert the approver name. **[R]**
6. **Local and production architectures have separate sources of truth.** Aspire defines the local graph, while a 514-line Pulumi program and imperative Azure CLI steps define production. Production omits dedicated MCP, Ollama, embeddings, and Whisper and currently discards browser telemetry sent through the kernel OTLP proxy. **[R]**

Architecture V2 should **retain Orleans, Aspire, the journaled domain, the model registry, connector adapters, and the Flutter surface model**. It should not be a rewrite or an early microservice split. The target is an incrementally modular actor system with:

- tenant/workspace/principal-aware command and event envelopes;
- per-scope grain identities rather than global main grains;
- explicit command/query separation;
- resumable workflows, durable outbox/inbox records, retries, idempotency, verification, and compensation;
- indexed timeline, causality, memory, workflow, and UI-feed projections;
- one capability contract for tools/connectors and one policy-driven model router;
- authenticated MCP/gRPC/UI adapters that cannot directly manufacture identity or domain events;
- one topology contract with explicit development, test, and production profiles.

## 1. Current product definition

### What DigitalBrain is

DigitalBrain describes itself as a “.NET Aspire + Orleans kernel for [a] self-evolving personal OS,” where neurons are Orleans grains and synapses are typed messages. Its product invariant is that user-visible system evolution is staged, journaled, approved, and then applied. Ino, automations, Foundry, and future marketplace flows are meant to propose changes; the rail is meant to execute them. The Flutter client renders server-driven surfaces rather than owning the product’s behavior. See [README.md](../README.md). **[R]**

A precise current definition is:

> DigitalBrain is a single-brain, local-first personal AI and automation runtime. It combines an Orleans actor kernel, causal event journals, an LLM/tool orchestrator, selected SaaS connectors, an approval-gated self-evolution path, a server-driven Flutter shell, and MCP/telemetry surfaces for operating and inspecting the runtime.

### Who it is for

- A technical individual or power user who wants a private, inspectable personal agent with local models and optional cloud services. **[R][I]**
- A knowledge worker using Gmail and Salesforce data through Ino. Current connector tools are primarily read-oriented. **[R][I]**
- A developer/operator creating automations, inspecting causal history, running Foundry experiments, and operating the Aspire topology. **[R]**
- External coding agents using MCP to inspect or drive the brain. MCP descriptions explicitly target agent/test workflows. **[R]**

There is no evidence that the current runtime is a production multi-tenant SaaS boundary. It has local users and sessions, a default workspace, global main grains, and caller-supplied client IDs; it does not have a tenant aggregate, organization membership model, external identity provider, or consistent authorization layer. **[R][I]**

### Core user journeys

| Journey | Current path | Status |
|---|---|---|
| Sign in and open the workspace | Local login → UserSessionNeuron → neuron-emitted shell/workspace/task surfaces → Flutter | Implemented; local identity only |
| Ask Ino and use a capability | InoRequest → intent/context/model selection → agent/tool call → InoResponse and UiSurface | Implemented; isolation and durability gaps |
| Connect Google or Salesforce | Auth surface → OAuth browser flow → callback → encrypted per-user PackConfig token storage | Implemented; duplicated flows and Salesforce PKCE gap |
| Create an automation | Natural language/MCP → SelfEvolutionProposal → approval → automation apply handler | Implemented happy path |
| Inspect why something happened | Per-neuron timeline/correlation query, Aspire traces, MCP read tools | Implemented locally; global causal traversal is absent |
| Upload and understand data | Excel/SQLite upload → parse/schema inspection → graph/chart/memory surfaces | Implemented |
| Use local AI and voice | Ollama chat + embedding; OpenAI-compatible Whisper/Speaches transcription | Implemented in local AppHost profile |
| Extend the system with packs | Signed pack contracts, capability gates, collectible ALC embodiment primitives | Foundation only; no production NeuroPack construction/install path was found |

### Current boundaries and non-goals

- The trusted product boundary is currently **one personal brain**, not a cross-customer platform. **[I]**
- Google implements Gmail; Drive and Calendar packages are referenced but have no current feature implementation. Gmail and Salesforce Ino tools are read-oriented. X/Twitter is simulation only. **[R]**
- Server-driven UI can compose only the Flutter host’s compiled widget vocabulary; it is not arbitrary remote Flutter code. **[R][D]**
- Self-evolution is allowlisted by exact apply-handler identifiers. Current production handlers cover automation define/remove and Foundry run/deploy; they do not form a general deployment engine. **[R]**
- Local Ollama and Whisper are run-mode resources. Production currently expects Azure/cloud model capabilities and does not deploy those local containers. **[R]**
- Marketplace/publisher trust, pack signing, and collectible execution are architectural foundations, not a fully connected current marketplace journey. **[R]**
- The product is not yet an exactly-once workflow system, a durable event broker, an enterprise identity platform, or an offline-first client. **[I]**

### Killer features and differentiators

| Differentiator | Why it matters | Current maturity |
|---|---|---|
| Causal neuron runtime | The same typed interaction can drive behavior, audit, replay, UI, and explanation | Strong foundation; lineage is local to one neuron/correlation scan |
| Human-approved self-evolution | The assistant can propose new behavior without silently applying it | Real happy path; crash recovery, authorization, retry, and rollback are incomplete |
| Server-driven native UI | Neurons and packs can emit new experiences without rebuilding Flutter | Working; protocol is unversioned and host vocabulary has become monolithic |
| One brain exposed to humans and agents | Flutter, gRPC, Telegram, and MCP can operate the same Orleans state | Working locally; authorization and CQRS separation need hard boundaries |
| Local-first multimodal stack | Ollama, embeddings, and Whisper give a useful private default with cloud provider options | Live locally; production capability parity is explicit rather than automatic |
| Live distributed introspection | Aspire Dashboard, OpenTelemetry, neuron journals, and MCP make behavior inspectable | Strong local experience; gaps in MCP and browser telemetry remain |

The most defensible unique product proposition is not “an assistant with many integrations.” It is **an inspectable agent runtime that can propose new durable behavior and UI through a single audited approval boundary**. **[I]**

## 2. Evidence base and limitations

### Repository snapshot

The solution contains Core, Kernel abstractions, the Kernel host, Aspire hosting helpers, MCP, UI contracts/runtime, Google/Ino/Salesforce/Telegram integrations, Flutter, ServiceDefaults, an AppHost, a TestKit, six test projects, and a separate Pulumi deployment project. Central package pins include Aspire 13.4.6, Orleans 10.2 plus 10.2.1 preview/alpha journaling packages, ModelContextProtocol 1.4.0, Microsoft.Extensions.AI 10.7.0, Microsoft Agent Framework 1.13.0, OpenTelemetry 1.15–1.16, .NET 11 preview, and Flutter 3.41+. **[R]**

### Live inspection

Aspire MCP was used only for read operations. The existing AppHost was not started, stopped, rebuilt, or restarted by this assessment. **[L]**

### Tool limitations

- Context7 was invoked first for Orleans, Aspire, MCP C# SDK, OpenTelemetry, Microsoft.Extensions.AI, Agent Framework, and Flutter. Every request returned **“Monthly quota reached.”** No Context7 documentation payload was available. Microsoft Learn, Aspire MCP documentation, and other primary sources were used as the fallback. **[D]**
- The repository configures DigitalBrain MCP over stdio and HTTP, and the live Aspire graph showed a healthy MCP project. However, no DigitalBrain MCP tools or resources were exposed in this Codex session. Therefore live get_timeline, get_causal_lineage, ino_get_status, and get_workbench_surfaces calls could not be made. This is a connector/tool-registry limitation, not evidence that the server was unhealthy. **[L]**
- Aspire MCP has no live metrics-query tool. Metric registration was assessed from source, while live metric values were not inspected. **[L][R]**
- No deployed Azure production environment was queried.
- No build or test run was needed for this read-only assessment. Test structure and current CI/deployment behavior were assessed from repository evidence.

## 3. Current architecture

### 3.1 Topology

~~~mermaid
flowchart LR
    subgraph Clients
        F[Flutter / RFW]
        T[Telegram]
        A[External MCP agents]
    end

    subgraph Edge
        G[gRPC / gRPC-Web / HTTP<br/>upload and OAuth callbacks]
        M[Dedicated MCP host<br/>Orleans client]
    end

    subgraph Cluster[Orleans kernel cluster]
        I[Global InoNeuron]
        N[Domain neurons]
        E[SelfEvolutionNeuron]
        U[FlutterUiNeuron]
    end

    subgraph Capabilities
        R[Model registry and router]
        L[LLM / embedding / voice]
        C[Google and Salesforce connectors]
    end

    subgraph Data
        J[(Incoming and outgoing<br/>Blob journals)]
        S[(Grain state and reminders)]
        P[(Pack config and sync blobs)]
        B[In-memory Orleans streams]
    end

    F <--> G
    T <--> G
    A <--> M
    G --> Cluster
    M --> Cluster
    I --> R --> L
    I --> C
    Cluster --> J
    Cluster --> S
    Cluster --> P
    Cluster -. broadcast and home feed .-> B
    B --> U --> G
~~~

### 3.2 Project and dependency boundaries

| Project/area | Current responsibility | Boundary observation |
|---|---|---|
| DigitalBrain.Core | Grain interfaces, Synapse base, identity/session contracts, model descriptors, self-evolution, many feature contracts | Domain nucleus, but mixes unrelated bounded contexts |
| Kernel.Abstractions | Neuron base, journaling, dispatch, Ino tool provider/auth wrappers | Orleans and application abstractions are mixed |
| Kernel | Silo, web/gRPC edge, grains, Foundry, configuration, projections, auth, UI bridges, uploads, MCP read registration | Composition root plus most application/infrastructure logic |
| integrations/* | Ino, Google, Salesforce, Telegram behaviors | Compiled directly into Kernel rather than loaded behind narrow ports |
| DigitalBrain.Mcp | Standalone Orleans client with read and mixed mutation tools | Direct grain access bypasses an application command/query boundary |
| Ui.Contracts / Ui.Runtime | Surface/tree contracts, builders, sample/workbench projections | Contracts, protocol, samples, and application projections overlap |
| DigitalBrain.Aspire | AppHost DSL, storage/Orleans, AI containers, replicas, environment export | Useful platform abstraction; currently a large hosting god module |
| AppHost / ServiceDefaults | Runtime selection and shared health/OTel/service discovery defaults | AppHost is thin; MCP does not consume ServiceDefaults |
| Flutter | Native shell, gRPC, RFW registry/runtime, charts, editor/canvas | Thin in business ownership, but large in protocol/rendering responsibility |
| deploy | Independent Pulumi production graph | Materially diverges from AppHost and is skipped by normal builds |

The largest dependency problem is that **DigitalBrain.Kernel references all integration projects, MCP, UI runtime/contracts, Pack contracts, and ServiceDefaults**. Adding or changing a provider, connector, surface, or MCP contract therefore tends to rebuild and retest the entire kernel. **[R][I]**

### 3.3 Orleans neuron and synapse model

The shared [Neuron](../src/DigitalBrain.Kernel.Abstractions/Neuron.cs#L27) is an Orleans DurableGrain, INeuron, and IAsyncObserver of Synapse. Each activation receives keyed incoming and outgoing durable lists. Missing journal wiring fails fast rather than silently degrading. **[R]**

The [Synapse](../src/DigitalBrain.Core/Synapse.cs#L11) envelope currently contains type, timestamp, sender, receiver, broadcast flag, correlation ID, stable synapse ID, and immediate causation ID. Stamp preserves a supplied correlation, otherwise inherits the cause’s correlation or ID, and sets the immediate cause. **[R]**

~~~mermaid
sequenceDiagram
    participant C as Caller
    participant G as Neuron
    participant O as Outgoing journal
    participant X as Stream or target grain
    participant I as Incoming journal
    participant H as Handler

    C->>G: FireAsync(synapse)
    G->>G: Stamp sender/correlation/causation
    G->>O: Add + WriteStateAsync
    alt broadcast
        G->>X: In-memory stream OnNext
    else explicit receiver
        G->>X: DeliverAsync
    else no receiver
        G->>G: self DeliverAsync
    end
    X->>I: Add + WriteStateAsync
    I->>H: IHandle or fallback dispatch
    H->>G: Fire child synapses
~~~

Key consequences:

- Journal-first ordering preserves recorded intent before dispatch.
- It does **not** atomically guarantee downstream delivery. No durable outbox record is drained after a crash. **[I]**
- External adapters commonly call FireAsync on the target grain, so the target is recorded as the sender and self-delivery duplicates one request across its outgoing/incoming journals.
- Direct DeliverAsync can bypass stamping. Current proposal/approval paths use it, weakening causal continuity.
- Point-to-point and broadcast dispatch use different reflection/caching rules. Broadcast handling does not set the current cause, so child events can lose causal context.
- Broadcast and HomeFeed providers are in-memory streams. Subscriptions resume, but missed stream delivery is not made durable by the provider.
- GetCausalLineageAsync scans one neuron’s two journals for a correlation/synapse ID. It does not traverse causation edges across neurons.
- Checkpoint/branch support snapshots both journals and re-delivers them into a new activation. It is useful experimentation machinery, but replaying commands and facts together can repeat behavior.

### 3.4 Journals and durability

| Concern | Current mechanism | Durability assessment |
|---|---|---|
| Incoming/outgoing neuron history | Orleans experimental IDurableList stored in Azure Blob/Azurite, JSON format | Durable when Aspire-hosted; alpha API risk |
| Grain state | Azure Blob/Azurite default storage | Durable within backing-store lifecycle |
| Membership/reminders | Azure Table/Azurite | Durable; reminders used by schedule/poll features |
| Broadcast/home feed | Orleans memory streams + memory PubSubStore | Not durable delivery |
| Cross-grain effects | Write sender journal, then invoke target | Crash gap; no outbox/inbox |
| Feature projections | Rebuild dictionaries/sets by scanning journals on activation | Replayable but unindexed and increasingly expensive |
| Standalone non-Aspire run | Localhost clustering, memory storage, prototype journals | Development-only, non-durable |
| Local Azurite | Persistent container lifetime with no data volume in the live graph | Survives ordinary stop/start; data is not protected from container recreation |

Journal serialization discovers derived Synapse types and persists CLR full names. Renaming/moving a type or omitting an integration assembly can make historical records unreadable. Unknown types fail rather than degrade. This is fail-safe behavior but a fragile long-term event schema. **[R]**

### 3.5 Ino orchestration and tool calling

Ino is one global grain, ino-main. Its 1,456-line implementation currently owns:

- capability discovery and intent classification;
- special-case and static handler routing;
- journal/context retrieval and memory summaries;
- model selection and provider resolution;
- tool discovery, authentication wrapping, execution, and telemetry;
- automation proposal generation and approval;
- database/schema ingestion;
- conversation persistence and UI surface emission.

The generic path is:

1. Build an Ino context packet from Ino's own recent journal state and MemorySummary events. ContextNeuron receives capability evidence elsewhere, but generic prompt assembly does not recall it.
2. Read the AppHost-exported model registry and any dynamic system LLM configuration.
3. Enumerate every registered IInoToolProvider and build tools for the supplied client ID.
4. Prefer a tool-capable model whenever tools are present.
5. Construct a ChatClientAgent for the request and run it without a persisted agent session.
6. Journal tool started/completed/failed synapses and conversation turns. Authentication denial currently appears as InoToolCallFailed; InoConnectorAuthRequired is exercised in tests but has no production emission path.
7. Emit InoResponse plus a server-driven reply surface and create a memory summary.

Important constraints:

- IInoToolProvider receives clientId but no tenant, workspace, authenticated principal, grants, approval policy, timeout, or idempotency key.
- A typed ToolResult hierarchy exists but the active tool path still largely returns strings.
- Tool telemetry can journal raw result.ToString content, including Gmail IDs/snippets; prompt and response redaction is inconsistent.
- “Thin Ino” application interfaces exist, but their Basic implementations are placeholders and the real flow remains in InoNeuron.
- Context and memory-summary paths select recent global journal events without workspace filtering, then relabel them with the request workspace. Conversation turns are filtered, but the larger context is not.
- All generic prompts are pushed to a tool-capable model if any tools are registered, even when the request needs no tool.
- Tool calls run within the orchestration turn without a durable invocation ledger or independent worker lifecycle.

### 3.6 Self-evolution proposal/approval/apply rail

The current contracts are well named and explicit: proposal, pending/rejected/expired, decision recorded/rejected, apply result, and rollback required. The global SelfEvolutionNeuron rebuilds pending, decided, applied, and expired sets from its two journals. A registry allowlists exact ApplyVia strings and checks handler maximum risk. **[R]**

~~~mermaid
flowchart LR
    P[Proposal] --> V{Validate and deduplicate}
    V -->|valid| W[Pending]
    V -->|invalid/expired| R[Rejected or Expired]
    W --> D[DecisionRecorded]
    D -->|rejected| Z[Terminal]
    D -->|approved| A[Invoke apply handler synchronously]
    A --> S[ApplyResult]
    S -->|success| OK[Applied]
    S -->|failure + checkpoint| RB[RollbackRequired event]
    D -. crash after decision .-> GAP[No resumable apply state]
~~~

The main gap is between DecisionRecorded and ApplyResult. A crash after the decision, during an external effect, or before the result is persisted cannot be resolved deterministically:

- replay marks the proposal decided;
- a repeated decision is rejected;
- no ApplyRequested/Applying attempt exists;
- no durable reminder/lease/retry exists;
- handlers do not receive a universal idempotency key;
- an effect may have succeeded without a recorded result.

RequiresHumanApproval is stored but not consulted; current behavior is always decision-gated. DecidedBy is a caller-supplied string, not an authenticated authorization context. The MCP approval tool defaults it to “mcp-agent.” RollbackRequired is only an event. One closed-loop path proposes an unregistered aspire-mcp ApplyVia value and therefore cannot apply successfully. **[R]**

### 3.7 Model registry and LLM routing

The typed registry is a strong foundation. It models provider, model ID, display name, capability kind, tool/vision/streaming/structured-output flags, service key, and fast/balanced/reasoning/default roles. AppHost exports registrations through environment configuration; the kernel creates keyed IChatClient instances and an unkeyed default. Current AppHost selects Ollama llama3.1:8b as balanced, mxbai-embed-large, and local Whisper. **[R][L]**

Current coupling and gaps:

- Provider construction is duplicated across the default chat factory, keyed registrations, and scoped factory.
- Ino has a separate role-priority policy from the registry.
- Adding a provider touches Core IDs/descriptors, Aspire parameters/env export, several factories/switches, settings commands, and tests.
- llm_key is used both as model/service selection and as an API credential input.
- Runtime set-llm supports only a subset of registry providers.
- Service-key normalization can collide and duplicate keys are not centrally rejected.
- Routing has no explicit tenant policy, residency, cost, latency, health, quota, or fallback budget.
- Agent Framework is a fast-moving dependency exposed directly inside the grain rather than behind an application port.

### 3.8 Google and Salesforce integrations and authentication

Both providers use:

- AppHost parameters for application credentials and redirect URI.
- Per-user auth grains and connector implementations.
- OAuth state validation.
- A PackConfigStore whose individual values are protected with ASP.NET Core Data Protection.
- A shared key ring and encrypted Blob-backed configuration in Aspire-hosted mode.

The current effective path is split: Flutter/gRPC begins authentication through a per-user AuthNeuron, but the HTTP callback resolves IConnector and completes authentication through a second implementation. GoogleAuthNeuron/GoogleConnector and SalesforceAuthNeuron/SalesforceConnector duplicate and have begun to diverge. **[R]**

Specific findings:

- Gateway requires a local session before beginning Google/Salesforce auth.
- OAuth state is stored per user, but the state string embeds the user ID rather than using an opaque server-side handle.
- Salesforce begin-auth stores a PKCE verifier, but the active connector callback builds its token exchange from app values without merging that verifier. The factory sends code_verifier only when it is present. Full callback/PKCE contract testing is marked TODO.
- IGmailNeuron advertises send behavior, while the configured OAuth scope is gmail.readonly. Either the send contract or the granted scope is wrong.
- Google references Drive/Calendar client packages but currently implements Gmail behavior.
- The live AppHost modeled secret parameters, redirect defaults, Salesforce login URL, and API v60.0. Aspire redacts secret values, so their configured presence cannot be inferred from the live listing.
- API v60.0 is materially behind the current Salesforce Summer ’26 API. Keeping it may be deliberate compatibility policy, but it should be explicit and tested.

### 3.9 MCP server and tool surfaces

Repository-defined read tools:

- ping_digitalbrain
- get_timeline
- get_causal_lineage
- get_workbench_surfaces

The mixed command surface contains 19 tools across Ino interaction/status/listing/approval, LLM calls, generic signals, automation staging/removal, UI actions, visualization, DB demo, and simulations. Query-like tools such as ino_get_status and list operations live in the mutation class. **[R]**

Hosting has three modes:

- trusted local stdio standalone MCP: read + mutation tools;
- dedicated Aspire HTTP MCP: read + mutation tools, one replica;
- direct/non-Aspire Kernel MCP on port 8081: read tools only.

The HTTP MCP application has no authentication/authorization middleware and does not use ServiceDefaults/OpenTelemetry. The ReadOnly annotation is metadata, not an enforcement boundary. get_timeline returns Synapse.ToString without redaction, while get_causal_lineage sanitizes its structured output. Direct IGrainFactory access also means MCP invents caller identity and bypasses an application command/query policy layer. **[R]**

### 3.10 Flutter and server-driven UI

FlutterUiNeuron converts UiSurface to an RfwCard and publishes via HomeFeedBus. Flutter watches the feed over gRPC/gRPC-Web, classifies surfaces, renders typed trees or RFW, and sends actions through a unary call because browser gRPC-Web does not support bidirectional streaming. Canvas, editor, and some navigation remain client-native/hybrid. **[R][D]**

Strengths:

- UI behavior is server-owned while rendering remains native.
- The local widget vocabulary constrains remote documents.
- Surface actions and causation can travel through the same domain model.

Risks:

- UiSurface payloads/actions are string/object dictionaries without a protocol version.
- HomeFeed uses caller-supplied client IDs plus a shared unaddressed stream.
- Actions can select synapseType/props instead of presenting an authorized command token.
- UiSurface, UiWidgetTree, clientId, and workspaceId changes have broad server/client/test blast radius.
- The “small host vocabulary” is now a 5,392-line digitalbrain_rfw_library.dart containing registry, rendering, networking, catalog/editor state, compilation/promotion, charts, and simulation.

The official RFW guidance treats networking as out of scope and recommends locally cached binary libraries/data so network loss does not break the interface. DigitalBrain should make that caching/version contract explicit. **[D]**

### 3.11 Aspire AppHost, resources, service discovery, and storage

AddDigitalBrain is the central resource composition point. It currently owns:

- Azure Storage/Azurite, Orleans clustering, grain state, reminders, journals, and sync;
- run-mode Ollama with GPU support, persistent data, OpenWebUI, chat and embedding model pulls;
- run-mode Whisper/Speaches CPU container and model cache;
- model-provider secret parameters and registry export;
- run/publish behavior and construction of DigitalBrainContext.

WireKernelSilo adds resource references, waits, endpoints, replicas, model and voice configuration, and credentials. The default is three replicas. Flutter and MCP receive direct LLM references even though model routing is intended to belong to the kernel, and neither is explicitly sequenced with WaitFor(kernel). Explicit clustering/grain references duplicate relationships already propagated by the Aspire Orleans resource. **[R][D]**

Local and production capability profiles differ:

| Capability | Local AppHost | Current production definition |
|---|---|---|
| Kernel | 3 project replicas | ACA 2–5 replicas |
| Storage | Azurite Table/Blob | Azure StorageV2 Standard_LRS |
| LLM | Ollama llama3.1:8b | Azure OpenAI deployment |
| Embedding | mxbai-embed-large | Not explicitly deployed |
| Voice | Whisper/Speaches | No external voice endpoint configured |
| MCP | Dedicated HTTP project | Dedicated MCP not deployed; kernel read MCP is on a non-ingress port |
| UI | Windows Flutter dev executable | Flutter web on Azure Static Web Apps |
| Telemetry | Aspire Dashboard via OTLP | App Insights/Log Analytics; browser OTLP path is incomplete |

### 3.12 OpenTelemetry, Dashboard, logs, traces, and metrics

ServiceDefaults configures:

- service discovery and standard HTTP resilience;
- /health and /alive;
- OpenTelemetry logs with formatted messages/scopes;
- ASP.NET Core, HttpClient, runtime, Microsoft.Orleans, and DigitalBrain.Neuron meters;
- ASP.NET Core/HTTP plus Microsoft.Orleans.Application and DigitalBrain.Neuron trace sources;
- OTLP and optional Azure Monitor exporters.

The Kernel and Telegram consume these defaults. The dedicated MCP host does not. Microsoft.Orleans.Runtime tracing is commented out. **[R]**

Flutter sends telemetry through the public kernel /otlp proxy. That proxy:

- acknowledges and discards when no OTLP endpoint is configured;
- disables upstream TLS certificate validation;
- returns success even when forwarding fails;
- translates Flutter JSON logs itself.

Current Pulumi supplies APPLICATIONINSIGHTS_CONNECTION_STRING but no OTEL_EXPORTER_OTLP_ENDPOINT, so repository-defined production browser traces/metrics are silently discarded. This bridge is acceptable only as a constrained local convenience unless it gains real trust, buffering/drop metrics, and production routing. **[R][I]**

EnableOrleansDashboard is a dead configuration path: it defaults true and injects a port, but no dashboard package/registration consumes it. The live dashboard is Aspire Dashboard. **[R][L]**

### 3.13 Testing and deployment

Repository test evidence includes approximately 139 C# test-support/source files, 372 Fact/Theory-style declarations, two Reqnroll features, and 15 Flutter test files. TestKit provides in-memory journal and Orleans cluster harnesses. The main suite covers neurons, journals, self-evolution, model routing, connectors, gRPC/gateway, UI, sync/checkpoints, AppHost graph construction, and selected integration contracts. **[R]**

Gaps:

- CI builds Flutter web but does not run flutter test.
- Coverage collection is configured as a package but not run/reported.
- AppHost model tests do not start containers or validate multi-replica/service-discovery behavior.
- The self-evolution “durability” fixture uses volatile journal storage and does not inject failures inside the decision/apply window.
- No full Salesforce callback/PKCE path is tested through the real endpoint.
- Normal builds skip Flutter and deployment projects. PR CI does not build/preview Pulumi.
- README references a tests/DigitalBrain.Tests/E2E area that is not present in the current tree; real-stack behavior lives in gateway/cluster fixtures instead.

Release deployment does run .NET tests, publish SDK containers, execute Pulumi, configure Static Web Apps/domains with Azure CLI, and perform useful health, CORS, endpoint, and frontend smoke tests. However:

- the Pulumi topology duplicates AppHost intent;
- Static Web Apps and domains are partly imperative outside Pulumi;
- region, names, domains, storage account, Docker Hub owner, model/version, and some defaults are hard-coded;
- Dockerfiles duplicate the SDK container-publish path but are not used by release CI;
- managed-identity roles coexist with injected Storage/OpenAI keys and public/shared-key access;
- fallback image tag is latest.

## 4. Live runtime findings

Snapshot: **2026-07-09 23:37:35 +02:00**.

| Finding | Live evidence | Interpretation |
|---|---|---|
| AppHost | DigitalBrain.AppHost running | Live evidence matched repository AppHost |
| Environment health | Aspire doctor 5/5; Aspire/AppHost 13.4.6; .NET 11 preview 5; Docker running; dev cert trusted | Healthy development environment |
| Resource model | 28 resources: 25 Running/Healthy, one Azure environment model without runtime state, two expected rebuilder helpers NotStarted | No unhealthy modeled workload |
| Kernel | Three healthy replicas behind shared web/gRPC/Orleans proxy endpoints | Replica topology is real, not only configured |
| AI | Healthy persistent Ollama 0.13.0, llama3.1:8b, mxbai-embed-large, OpenWebUI; healthy Whisper latest-cpu | Local AI profile is operational |
| Storage | Healthy Azurite, clustering, grainstate, journal, sync | Backing resources are connected |
| Persistence | Ollama/OpenWebUI had named volumes; Whisper had a cache volume; Azurite had persistent lifetime but no volume | Local durable-data lifecycle needs a clearer guarantee |
| Traces | Latest query returned 13 successful traces; 840 earlier traces were omitted by size limit | Active, high-volume telemetry |
| Grain distribution | Successful grain traffic appeared on all three replicas | Cluster routing is active |
| Ino/memory | Ino timeline reads and ContextNeuron → Ollama embedding traces were visible; cold embedding call was about 1.15 s, later calls 19–30 ms | Real context/memory flow; cold-start cost visible |
| Causation | Automation traces included neuron ID, synapse type, correlation ID, and nested internal spans | Current causal instrumentation is useful |
| Slow timeline trace | One GetTimelineAsync was 3.355 s end-to-end while server work was 8 ms | Likely client/routing/activation/instrumentation delay; needs focused trace review |
| Structured logs | No Error/Critical records in sampled current logs; warnings were expected Azurite create conflicts, membership contention, and Kestrel warnings | Healthy, but bootstrap noise pollutes error searches |
| Error-marked traces | 23 startup storage 409/initial 404 spans across replicas | Expected idempotent bootstrap, not service failure |
| Ino log search | No structured log entries matched “Ino,” while traces did | Traces are richer than logs for Ino today |
| MCP telemetry | Dedicated MCP was healthy but not visible as a telemetry resource/query target | Consistent with missing ServiceDefaults/OTel |
| Metrics | No Aspire MCP metrics tool | Live values unavailable; source registration confirmed |
| DigitalBrain MCP | No DigitalBrain tools exposed to this session | Timeline/lineage/status/workbench content not inspected live |

No dashboard login token or secret parameter value is reproduced in this document.

## 5. Architectural strengths

- **A coherent mental model.** Neuron/Synapse is understood across domain behavior, UI, automation, tests, and observability.
- **Durability is treated as a requirement.** Hosted journal wiring fails fast, and causal IDs/checkpoints are first-class.
- **The approval rail exists in code.** It is not merely a prompt convention.
- **Aspire gives excellent local operability.** Resource relationships, health, endpoints, logs, and traces are readily inspectable.
- **The model catalog is typed.** Provider/model roles and capabilities are a better base than scattered model strings.
- **Connector tokens are encrypted at rest.** PackConfig value protection and shared key-ring storage are strong primitives.
- **Server-driven UI is constrained.** The client controls the executable widget vocabulary.
- **Testing breadth is substantial.** There are useful architecture, cluster, gateway, journal, connector, model, UI, and deployment-model seams to support incremental migration.

## 6. Architectural risks

| Priority | Risk | Evidence | Consequence |
|---|---|---|---|
| Critical | Secrets or sensitive results in journals and timeline output | LoginRequest is journaled before handling; tool telemetry can include raw connector results; get_timeline is unredacted | Credential or private-data exposure through storage, logs, MCP, checkpoints, or support tooling |
| Critical | Cross-workspace Ino context leakage | Global ino-main; unfiltered context/memory journal scans | One user/workspace can influence or expose another’s model context |
| Critical | Non-resumable approval/apply window | Decision recorded before synchronous apply; no attempt/retry/idempotency state | Lost, duplicate, or unknowable mutations after crash |
| Critical | Identity/authorization are caller assertions | No edge auth middleware; fixed/caller client IDs; MCP self-asserts approver | Unauthorized read, action, approval, or configuration change |
| High | Journals are not delivery outbox/inbox | Write then call; Orleans at-most-once default; memory broadcasts | Durable intent without effect, or duplicates after retries |
| High | Fragile event schema | CLR full-name discriminators; alpha journaling packages | Historical data can become unreadable during refactor/version upgrades |
| High | OAuth paths are duplicated/divergent | AuthNeuron plus Connector per provider; Salesforce PKCE merge bug | Authentication failures and security behavior that tests do not cover |
| High | Production observability gaps | Browser OTLP fail-open/discard; MCP no OTel; error-noisy bootstrap | Missing evidence during incidents and false error signals |
| High | Local/production topology drift | Aspire vs Pulumi/CLI, materially different capabilities | “Works locally” does not define production behavior |
| High | UI/feed isolation and protocol | Shared feed, caller clientId, dictionary actions, no version | Cross-audience delivery risk and expensive client/server migrations |
| Medium | Model routing duplication | Multiple factories and role policies; overloaded llm_key | Provider changes have broad blast radius and inconsistent behavior |
| Medium | God modules and global coupling | Ino 1,456 lines; RFW registry 5,392; UiSurfaceRuntime 851; Kernel references all integrations | Slow, risky change cycles; hard-to-isolate failures |
| Medium | Platform/runtime volatility | .NET 11 preview, Orleans journaling alpha, Agent Framework fast-moving | Upgrade and production-support risk |
| Medium | Distributed test gaps | No PR Pulumi preview, Flutter tests absent in CI, no crash/failover lane | Core architectural promises regress without detection |

## 7. Architecture V2

### 7.1 Target architecture

~~~mermaid
flowchart LR
    subgraph Adapters
        UI[Flutter/gRPC adapter]
        MCP[MCP adapter]
        OAuth[OAuth callback adapter]
        TG[Telegram adapter]
    end

    subgraph Application
        AUTH[Identity and authorization]
        CMD[Command handlers]
        INO[Ino planning/orchestration]
        POL[Tool and model policies]
        QRY[Query services]
    end

    subgraph Domain
        AGG[Aggregate state machines]
        WF[Workflow state machines]
    end

    subgraph Infrastructure
        ORL[Orleans grain implementations / adapters]
        EV[(Versioned event journal)]
        OUT[(Pending effects / outbox)]
        WORK[Effect workers]
        CONN[Connector adapters]
        AI[Model provider adapters]
        PROJ[Projection workers]
        READ[(Timeline, causality,<br/>memory, workflow, feed indexes)]
    end

    Adapters --> AUTH
    AUTH --> CMD
    CMD --> ORL
    INO --> CMD
    INO --> POL
    POL --> CONN
    POL --> AI
    ORL --> AGG
    ORL --> WF
    ORL --> EV
    ORL --> OUT
    OUT --> WORK
    WORK --> CONN
    WORK --> AI
    EV --> PROJ --> READ
    Adapters --> QRY --> READ
~~~

### 7.2 Recommended layers and ownership

| Boundary | Owns | Must not own |
|---|---|---|
| DigitalBrain.Domain | Versioned command/event types, identities, proposal/workflow state machines, capability/model descriptors, invariants | Orleans, MCP, Flutter, HTTP, provider SDKs |
| DigitalBrain.Application | Command handlers, authorization, Ino pipeline, approval policy, tool/model routing ports, query interfaces | Storage/client construction and UI rendering |
| Infrastructure.Orleans | Grain implementations, durable state, journal adapter, inbox/outbox, reminders, stream publication | Connector protocol rules or MCP tool definitions |
| Infrastructure.Connectors | Google/Salesforce/other provider adapters, OAuth protocol, token refresh, provider error mapping | User authorization policy or model-facing tool selection |
| Infrastructure.AI | Provider client factory, immutable model registry, health/circuit state, Agent Framework adapter | Conversation/workspace ownership |
| DigitalBrain.Projections | Timeline, causal graph, memory, workflow, connector status, task and UI-feed read models | Domain decisions |
| Adapters.Gateway/MCP/Telegram | Authentication, protocol translation, command submission, query formatting | Direct domain mutation or caller-supplied authority |
| UI.Contracts | Versioned SurfaceEnvelope, widget vocabulary, typed action references, compatibility rules | Samples/workbench projections |
| Hosting/Deployment | Resource profiles, service discovery, health, secrets, replicas, exporters | Domain/model/tool policy |

### 7.3 Grain, Synapse, journal, and projection ownership

- Grains own **one domain aggregate or workflow’s write state**. Avoid universal god grains.
- Ino should be scoped by tenant/workspace/conversation or user/workspace, not a global main key.
- Keep Synapse as a compatibility name if valuable, but separate:
  - **CommandEnvelope** — intent to change state.
  - **DomainEventEnvelope** — immutable fact emitted after a decision.
  - **IntegrationEvent/EffectRequest** — durable request to an external boundary.
- The stable envelope should include event/command ID, schema version, payload discriminator, tenant, workspace, authenticated actor, aggregate ID/sequence, correlation, causation, occurred time, data classification, and idempotency key.
- Do not use CLR full names as the durable payload discriminator. Maintain an explicit alias/upcaster registry.
- A journal belongs to the aggregate/workflow that made the decision. Cross-domain queries belong to projections.
- MCP/UI timeline and causality reads must query indexed projections, not scan arbitrary grain journals.
- Existing FireAsync/DeliverAsync remain temporarily as compatibility adapters that construct the new envelopes; they are retired only after callers migrate.

### 7.4 Command/query separation

Command flow:

1. Adapter authenticates the caller and creates an immutable ExecutionContext.
2. Application policy resolves tenant/workspace membership and permission.
3. A typed business command is submitted with CommandId and IdempotencyKey.
4. The owning grain validates current state, records domain events and pending effects, and returns Accepted plus an operation/workflow ID.
5. The caller queries operation status; it does not infer completion from a timeline scan.

Query flow:

1. Adapter authorizes the requested scope.
2. Query service reads a purpose-built projection with cursor pagination and redaction.
3. Query results are structured and versioned. They never expose raw Synapse.ToString output.

MCP should expose separate tool sets/scopes such as brain.read, brain.act, brain.approve, and brain.admin. List/status tools are queries; approval and configuration tools are commands.

### 7.5 Tool invocation and connector capability contracts

**CapabilityDescriptor**

- stable provider/capability/operation ID and version;
- input and output JSON schemas;
- read/write/external-side-effect risk class;
- required connector grants/scopes and application permission;
- approval policy;
- timeout, retry, and rate policy;
- idempotency support and provider operation-key mapping;
- data classification and audit/redaction policy.

**ToolInvocationContext**

- invocation/operation ID;
- tenant, workspace, authenticated user/service principal, conversation/session;
- correlation/causation IDs and trace context;
- granted scopes and policy decision;
- deadline, retry attempt, and idempotency key.

**ToolInvocationResult**

- Success with structured, classified output;
- NeedsAuth with a connector challenge reference;
- Denied;
- RetryableFailure with safe retry metadata;
- PermanentFailure;
- UnknownOutcome for timeouts after a potentially committed external side effect;
- a small redacted audit summary separate from the raw result.

Only authorized and currently connected capabilities are exposed to the model. Authorization is checked again immediately before execution. Secrets and raw connector payloads are not journaled. Side-effecting calls execute through durable workers; fast read calls may use the same ledger but can complete synchronously within bounded deadlines.

### 7.6 Workspace, tenant, identity, and authorization boundaries

- Introduce TenantId even if the initial/default value is personal. This makes the current single-user product explicit while avoiding another key migration later.
- Make grain keys tenant/workspace/aggregate aware. Map legacy global IDs to personal/default during migration.
- Treat clientId as a connection/device routing identifier only. It never proves user identity.
- Authenticate gRPC/HTTP/MCP at the edge. Carry a signed/validated principal into the application layer.
- Model workspace membership and roles. Check authorization both at ingress and at the command handler/grain boundary.
- Bind connector grants to tenant + user + provider, and optionally workspace when a connection is shared.
- Store opaque credential references in domain state; keep encrypted tokens in connector-owned storage with rotation/revocation metadata.
- Use opaque, expiring, single-use OAuth state plus PKCE and replay protection. Do not encode the user ID as the authority.
- Replace arbitrary UI synapseType actions with short-lived signed action tokens bound to principal, tenant/workspace, surface ID/revision, command, and expiry.
- For HTTP MCP, follow the MCP OAuth resource-server model, validate token audience, use per-tool authorization, and never pass the MCP token through to Google/Salesforce.

### 7.7 Durable workflow, approval, retry, idempotency, and outbox strategy

Target self-evolution state:

**Proposed → Validated → AwaitingApproval → Approved → ApplyQueued → Applying(attempt) → Verifying → Applied**

Failure branches:

**RetryScheduled → Applying**, **RollbackQueued → RollingBack → RolledBack**, or **Failed/ManualIntervention**.

Rules:

- Approval records an authenticated principal, policy/version, proposal content hash, reason, and expiry.
- The same durable write that advances workflow state also records a PendingEffect entry. In practice, this can be one journal event containing both the transition and effect intent, or one durable grain state write containing workflow plus pending effects.
- A reminder-driven dispatcher wakes the workflow and drains pending effects. The reminder is a wake-up, not the source of truth.
- Every receiver has an inbox/dedup record keyed by operation/effect ID.
- Every external handler supports validate, plan, apply(idempotency key), verify, and compensate/rollback where feasible.
- Retry only classified transient failures with bounded exponential backoff and jitter.
- Never blindly retry a non-idempotent external call after an unknown outcome. Verify by provider operation ID or enter ManualIntervention.
- Persist attempt number, lease owner/expiry, next attempt, sanitized result, and provider operation ID.
- Emit observability at every transition and measure approval age, queue age, attempts, unknown outcomes, and rollback success.

This strategy is needed for tool calls as well as self-evolution. Orleans transactions may help atomic multi-grain state in narrow cases, but they do not make Google, Salesforce, model, process, or deployment side effects transactional. **[D][I]**

### 7.8 Read models and indexing

Start with ports and the existing Azure/SQLite ecosystem; do not require a new database before the access patterns are proven.

| Projection | Primary keys/indexes | Purpose |
|---|---|---|
| TimelineEntry | tenant, workspace, occurredAt, eventId | Paginated human/MCP timeline |
| CausalEdge | tenant, workspace, correlationId, eventId, causationId | Cross-neuron causal traversal |
| WorkflowStatus | tenant, workspace, workflowId, state, updatedAt | Proposal/tool/task status |
| ToolInvocation | tenant, workspace, invocationId, capability, state | Audit, retries, latency, failures |
| ConnectorStatus | tenant, user, provider, grant version | Connected/auth-required/expired state without tokens |
| SurfaceFeed | tenant, workspace, audience, sequence, surfaceId/revision | Private resumable UI delivery |
| MemoryEvidence | tenant, workspace, subject, time, provenance, classification | Safe recall and retention |
| MemorySearch | text/vector index with evidence IDs | Hybrid recall; raw source remains governed |

Projection workers:

- consume both legacy and V2 events;
- persist a checkpoint per source/partition;
- are idempotent by event ID;
- expose lag and failure metrics;
- can rebuild from a known journal snapshot;
- preserve original event references and redaction classification.

For local mode, Azure Tables/Blobs plus SQLite FTS or the existing vector abstraction are sufficient starting points. For scale, select a search/vector store behind IMemoryIndex only after query volume, retention, and tenancy requirements are known.

### 7.9 Deployment and observability model

- Treat the Aspire AppHost resource graph as the **logical application topology**.
- Define explicit Development, Test, and Production capability profiles.
- Either generate deployment artifacts from that model or snapshot/compare the Aspire and Pulumi graphs in CI. Pulumi may remain the provisioning engine.
- Make absence of MCP, embedding, or voice in production an explicit capability decision surfaced to Ino/UI—not silent configuration drift.
- Keep local Ollama/Whisper data as replaceable caches. Give Azurite a real data volume if local durability is promised.
- Production journal/state/sync storage needs documented redundancy, retention, backup, restore, RPO/RTO, and failure-domain policy.
- Complete managed-identity migration, then remove account/model keys, shared-key auth, and unnecessary public network access.
- Add ServiceDefaults/OTel to MCP and correlate gateway, MCP, grain, tool, connector, model, projection, and workflow spans.
- Configure production browser telemetry through a supported OTLP collector/ACA agent. Remove disabled certificate validation and silent drops; if client requests remain fail-open, count and alert on dropped telemetry.
- Standard resource attributes: service name, service instance, service version, deployment environment, and silo identity. Put tenant/workspace and command/event/workflow/tool identifiers only on access-controlled spans and logs where required for correlation; never use those high-cardinality values as metric labels.
- Required metrics: grain calls/latency/failures, journal write latency/size, outbox queue age, projection lag, tool/model calls and cost/tokens, auth failures, approval age, retries, unknown outcomes, UI feed lag, telemetry drops.
- Keep /alive process-only. Make /health represent readiness for critical Orleans membership and journal/state storage. Optional connector/model health should affect capability status rather than unnecessarily killing the kernel.
- Add a distributed-runtime test lane with Azurite, three silos, replica loss/drain, durable state, MCP sequencing, and telemetry assertions.

## 8. Refactoring roadmap

### Architectural necessities

1. Stop secrets from entering journals/traces/MCP/UI and authenticate mutation/read surfaces.
2. Enforce tenant/workspace/principal boundaries and remove global Ino context sharing.
3. Make proposal/tool effects resumable and idempotent with workflow/outbox/inbox state.
4. Separate commands from events and queries; stabilize schema identifiers.
5. Add indexed causal/timeline/workflow/feed projections.
6. Consolidate connector auth and fix real callback/PKCE/scope behavior.
7. Establish one application topology contract and production telemetry path.

### High-value structural improvements

| Module | Size/current responsibility | Proposed split | Blast radius |
|---|---|---|---|
| InoNeuron.cs | 1,456 lines; intent, context, memory, tools, models, automation, DB, UI | Conversation grain, context providers, planner, tool catalog/executor, model router, memory, proposal service, surface emitter | All chat, connectors, automation, model, UI tests |
| digitalbrain_rfw_library.dart | 5,392 lines | Widget registry, primitives, charts, inspector/editor, simulation/compile features | All server-driven Flutter surfaces and golden tests |
| UiSurfaceRuntime.cs | 851 lines | Surface builders, auth/workspace surfaces, workbench projections, samples | Kernel, MCP, Flutter contracts/tests |
| forui_app_shell.dart | 807 lines | Feed/session controller, chat/upload, navigation, presentation | Main Flutter journey |
| UiSurfaces.cs | 607 lines | Versioned envelope, widget vocabulary, action contracts, chart/automation contracts | 50+ producer/consumer files |
| deploy/Program.cs | 514 lines | Data, AI, observability, runtime, edge/web, outputs | Whole production environment |
| SalesforceClientFactory.cs | 455 lines | OAuth protocol, config merge, token refresh, API client | Salesforce auth/tools/tests |
| DigitalBrainMutationTools.cs | 415 lines | Query tools, Ino commands, automation commands, demo/admin tools | External MCP contract |
| Neuron.cs | 398 lines | Journal adapter, event emitter, command dispatch, checkpoint/query compatibility | Every grain; serializer/test-wide |
| UserSessionNeuron.cs | 382 lines | User store, password auth, session aggregate, shell composition | Login, gateway, connector auth, UI |
| DigitalBrainBuilderExtensions.cs | 386 lines | Storage/Orleans, local AI, external AI, model export, kernel wiring | Entire AppHost graph/all replicas |

### Duplicated, implicit, global, and hard-coded behavior

| Smell | Current examples | Recommended treatment |
|---|---|---|
| Duplicated | AuthNeuron + Connector OAuth flows; three model client factories; AppHost + Pulumi topology; SDK containers + Dockerfiles | Select one owner and retain adapters/tests around it |
| Implicit | Fire means command and event; target becomes sender; RequiresHumanApproval unused; ReadOnly metadata treated as trust; workspace relabeling | Make semantics explicit in contracts and policy |
| Global | ino-main, self-evolution-main, session-main, automation-main, default workspace/app config, shared feed | Partition by tenant/workspace/aggregate |
| Hard-coded product policy | intent phrases/prompts, role priority, provider subset, grain IDs, CORS origins, ports | Move stable policy to typed options/registries; keep deliberate defaults documented |
| Hard-coded deployment | region, resource names, domains, Docker Hub owner, API/model versions, latest fallback | Environment profile/config with validation and immutable release inputs |
| Fragile strings | ApplyVia, synapseType actions, CLR full-name journal types, service-key normalization | Typed/versioned IDs plus validation/upcasters |

### Dependency and migration safety

- **Synapse/Neuron changes have repository-wide blast radius.** Add fields with stable Orleans IDs; use compatibility adapters and upcasters. Do not rename persisted aliases in place.
- **Ino changes touch the main product path.** Extract one responsibility at a time behind characterization tests; do not replace the grain in one step.
- **UiSurface changes cross C# and Dart.** Introduce V2 envelope/version negotiation while continuing to render V1 documents.
- **Model registry env schema connects AppHost and Kernel.** Introduce a new snapshot schema alongside the old keys, migrate readers, then remove legacy keys.
- **Connector consolidation affects stored credentials.** Preserve PackConfig scopes/keys through an adapter and migrate to opaque credential references only after callback tests pass.
- **Deployment changes have production-wide blast radius.** Begin with graph snapshots/preview and managed-identity shadow validation before removing secrets.

### Milestones and acceptance criteria

#### Milestone 0 — Safety containment and characterization

- Passwords, client secrets, refresh/access tokens, and raw tool results never enter new journals, traces, MCP text, checkpoints, or UI properties.
- Existing timeline/query outputs are centrally redacted.
- HTTP/gRPC/MCP reads and mutations require authenticated, authorized principals.
- App-scope configuration is operator-only.
- Tests inject broadcast causation, duplicate delivery, workspace leakage, timeline redaction, and every self-evolution crash boundary.

#### Milestone 1 — Identity and workspace boundary

- Add ExecutionContext with tenant, workspace, actor, session, roles/grants, correlation, and idempotency.
- Map legacy data to tenant personal and workspace default.
- Route new Ino conversations to per-scope grain keys.
- Two-user/two-workspace end-to-end tests prove no cross-memory, feed, connector, proposal, or timeline access.

#### Milestone 2 — Command/event envelope and CQRS adapters

- Typed commands and versioned events coexist with legacy Synapses.
- Gateway, UI actions, and MCP submit commands through application ports.
- Read tools query structured projections/contracts, not raw grains.
- Every accepted mutation returns an operation/workflow ID and records authenticated context.

#### Milestone 3 — Durable effects and self-evolution V2

- New workflow records ApplyQueued/Applying/Verifying/terminal states.
- Pending effects survive process/silo loss and resume by reminder.
- Inbox/outbox and provider idempotency yield one observable external effect for duplicate commands.
- Crash injection at every approval/apply/result boundary resumes or enters explicit ManualIntervention.
- Rollback/compensation is executed and verified where supported.

#### Milestone 4 — Projections

- Timeline and causality work across neurons with cursor pagination.
- Projection backfill consumes legacy journals and V2 events.
- Projection lag, failure, and rebuild are observable.
- MCP/UI switch to projections behind a compatibility response.

#### Milestone 5 — Ino, tool, and model extraction

- InoNeuron delegates conversation, context, planning, tool execution, model routing, memory, proposal, and surface responsibilities.
- Placeholder Basic Ino services are either real or deleted.
- Tools use one typed capability/invocation/result contract with budgets and authorization.
- Adding a model provider requires one adapter plus configuration, not switches across multiple modules.
- Agent Framework is hidden behind an application interface.

#### Milestone 6 — Connector and UI protocol V2

- One OAuth implementation per provider handles start/callback/refresh/revoke.
- Real endpoint tests cover state expiry/replay, PKCE, token refresh, and exact scopes.
- Gmail send is removed or separately approval/scoped.
- SurfaceEnvelope V2 carries version, revision, audience, expiry, causation, and signed typed actions.
- Feed delivery is private, sequenced, resumable, and deduplicated.
- V1 surfaces continue to render during migration.

#### Milestone 7 — Hosting, deployment, and distributed assurance

- Development/test/production capability profiles are explicit.
- AppHost and Pulumi graph drift fails PR CI; Pulumi preview and deploy-project build run before release.
- flutter test and coverage reporting run in CI.
- Kernel, MCP, Telegram, Flutter/browser, tools, models, projections, and workflows emit correlated telemetry.
- Azurite local data survives container recreation; production restore meets documented RPO/RTO.
- Three-silo failure/drain tests preserve state and complete inside verified ACA termination behavior.
- Managed identity replaces Storage/OpenAI keys where supported; shared-key/public access is disabled after validation.

### Optional improvements, not prerequisites

- A full microservice split.
- Replacing Orleans with a separate event-sourcing/workflow platform.
- A new enterprise event bus before durable outbox/inbox needs are proven.
- Durable Agent Framework sessions before conversation/workspace ownership is fixed.
- A dedicated vector database before memory access patterns and retention are measured.
- Separate connector processes or a service mesh.
- Replacing Flutter/RFW rather than versioning and modularizing it.
- Making every broadcast durable; do it only for event classes whose loss is unacceptable.

## 9. Open questions

1. Is the product permanently a single-person brain, or must it support families, teams, or customer tenants?
2. Is workspace isolation a security boundary or only an organizational filter?
3. Which changes always require a human, and which low-risk effects may be policy auto-approved?
4. Are journals authoritative domain event stores, audit logs, debugging timelines, or all three? What are their retention and erasure rules?
5. May prompts, email snippets, Salesforce data, and model responses be retained? For how long and under what user control?
6. Is exactly-once user-visible effect required, or is at-least-once plus idempotency the accepted contract?
7. Should mutation MCP ever be reachable over HTTP, and which identity provider/scopes should protect it?
8. Is AuthNeuron or IConnector the intended OAuth owner?
9. Are Google/Salesforce application credentials operator-managed, or may end users enter their own connected-app secrets?
10. Is Gmail send a real near-term feature? If so, what approval and scope boundary applies?
11. Can any private surface enter a shared HomeFeed stream?
12. What compatibility SLA should SurfaceEnvelope/widget/action versions provide?
13. Is the pack marketplace a current roadmap item or intentionally dormant scaffolding?
14. Should Aspire or Pulumi be the authoritative production topology model?
15. Which capabilities must exist in production when local Ollama/embedding/Whisper are absent?
16. What are the RPO, RTO, SLO, cost, latency, and data-residency requirements?
17. Is .NET 11 preview and alpha Orleans journaling acceptable for the next production milestone?

## 10. Official references

### .NET and Orleans

- [Microsoft Orleans overview](https://learn.microsoft.com/dotnet/orleans/overview)
- [Orleans messaging delivery guarantees](https://learn.microsoft.com/dotnet/orleans/implementation/messaging-delivery-guarantees)
- [Orleans grain persistence](https://learn.microsoft.com/dotnet/orleans/grains/grain-persistence/)
- [Orleans event sourcing](https://learn.microsoft.com/dotnet/orleans/grains/event-sourcing/)
- [Orleans timers and reminders](https://learn.microsoft.com/dotnet/orleans/grains/timers-and-reminders)
- [Orleans transactions](https://learn.microsoft.com/dotnet/orleans/grains/transactions)
- [.NET dependency injection guidelines](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection/guidelines)
- [CQRS pattern](https://learn.microsoft.com/azure/architecture/patterns/cqrs)
- [Transactional outbox guidance](https://learn.microsoft.com/dotnet/architecture/microservices/architect-microservice-container-applications/asynchronous-message-based-communication#resiliently-publishing-to-the-event-bus)

### Aspire, deployment, and observability

- [Aspire AppHost](https://aspire.dev/get-started/app-host/)
- [Aspire Orleans integration](https://aspire.dev/integrations/frameworks/orleans/)
- [Microsoft Learn: Orleans and Aspire](https://learn.microsoft.com/dotnet/orleans/host/aspire-integration)
- [Aspire C# Service Defaults](https://aspire.dev/get-started/csharp-service-defaults/)
- [Aspire service discovery](https://aspire.dev/fundamentals/service-discovery/)
- [Aspire resource lifetimes](https://aspire.dev/app-host/resource-lifetimes/)
- [Aspire volumes and bind mounts](https://aspire.dev/fundamentals/persist-data-volumes/)
- [Aspire MCP server for coding agents](https://aspire.dev/get-started/aspire-mcp-server/)
- [.NET observability with OpenTelemetry](https://learn.microsoft.com/dotnet/core/diagnostics/observability-with-otel)
- [OpenTelemetry .NET](https://opentelemetry.io/docs/languages/dotnet/)
- [Azure Container Apps OpenTelemetry agents](https://learn.microsoft.com/azure/container-apps/opentelemetry-agents)

### AI, MCP, UI, and connectors

- [Microsoft.Extensions.AI](https://learn.microsoft.com/dotnet/ai/microsoft-extensions-ai)
- [IChatClient tool calling and telemetry](https://learn.microsoft.com/dotnet/ai/ichatclient)
- [Microsoft Agent Framework overview](https://learn.microsoft.com/agent-framework/overview/)
- [Agent pipeline architecture](https://learn.microsoft.com/agent-framework/agents/agent-pipeline)
- [Official MCP C# SDK](https://github.com/modelcontextprotocol/csharp-sdk)
- [MCP tool specification](https://modelcontextprotocol.io/specification/2025-11-25/server/tools)
- [MCP authorization](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization)
- [MCP security best practices](https://modelcontextprotocol.io/docs/tutorials/security/security_best_practices)
- [Remote Flutter Widgets](https://pub.dev/documentation/rfw/latest/)
- [ASP.NET Core gRPC authentication and authorization](https://learn.microsoft.com/aspnet/core/grpc/authn-and-authz)
- [Google web-server OAuth](https://developers.google.com/identity/protocols/oauth2/web-server)
- [Gmail OAuth scopes](https://developers.google.com/workspace/gmail/api/auth/scopes)
- [Salesforce OAuth web-server flow](https://help.salesforce.com/s/articleView?id=sf.remoteaccess_oauth_web_server_flow.htm&language=en_US&type=5)
- [Salesforce PKCE](https://help.salesforce.com/s/articleView?id=sf.remoteaccess_pkce.htm&language=en_US&type=5)
- [Salesforce API version support policy](https://developer.salesforce.com/docs/platform/connect-rest-api/guide/intro_api_eol.html)

## Recommended next session

- [ ] Decide the single-user versus tenant-ready target and whether workspace is a security boundary.
- [ ] Write three short ADRs: execution/identity envelope, durable effect/outbox model, and journal versus projection ownership.
- [ ] Add characterization tests for secret journaling, cross-workspace Ino leakage, and every decision/apply crash boundary.
- [ ] Choose the first migration slice: secret-safe command ingress plus authenticated/redacted timeline reads.
- [ ] Connect the DigitalBrain MCP tools in-session and capture one real timeline, cross-neuron causal lineage, Ino status, and workbench snapshot.
- [ ] Export one representative Aspire trace and define the first SLO/metric set for workflow, tool, journal, and projection health.
