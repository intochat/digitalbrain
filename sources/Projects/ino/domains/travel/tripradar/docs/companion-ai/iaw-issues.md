# IAW Issues — Generic Open-Source Feature Requests

These issues should be created in the `InteractiveAgents/IAW` repository using generic open-source language. No mention of TripRadar or any specific consumer.

---

## Milestone: `v0.1.0 Release`

All issues below go under this milestone.

## Labels to Create

- `publishing` — NuGet packaging and CI/CD
- `documentation` — Docs, guides, examples
- `integration` — External consumer integration patterns
- `feature` — New capabilities

---

## Phase 1 — NuGet Publishing & Consumer Readiness

### IAW-01: Set up CI/CD pipeline for NuGet publishing

**Labels:** `publishing`

**Description:**
Set up a GitHub Actions workflow to build, pack, and publish IAW packages to NuGet.org.

**Packages to publish:**
- `IAW.Core`
- `IAW.Agents`
- `IAW.Agents.CSharp`
- `Aspire.Hosting.IAW`
- `Aspire.IAW.Client`
- `IAW.Testing`

**Requirements:**
- Trigger on release tag (e.g., `v0.1.0-preview.1`)
- SemVer versioning with pre-release support
- NuGet.org API key stored as GitHub secret
- Build matrix validates all target frameworks before publishing
- Package metadata: description, license (MIT), repository URL, icon
- Symbol packages (`.snupkg`) for debugging
- Dry-run mode for PRs (pack but don't publish)

### IAW-02: Document custom agent creation for external consumers

**Labels:** `documentation`, `integration`

**Description:**
Create a guide for developers building agents in their own projects that reference `IAW.Core` and `IAW.Agents` via NuGet.

**Guide should cover:**
- Creating a new .NET project referencing IAW.Core
- Defining a grain interface extending `IAgent`
- Implementing an agent extending `Agent<TInterface>`
- Defining tools via `DefineTools()` and method reflection
- Registering custom agents with the Orleans silo
- Using `[Llm<Fast>]` / `[Llm<Balanced>]` / `[Llm<Reasoning>]` attribute injection
- Using durable state (`AgentDurableState`) for persistence
- Publishing and subscribing to typed events via Orleans streams
- Using Orleans reminders for scheduled agent work
- Testing custom agents with `AgentTest<T>` and `MockChatClient`

**Deliverable:** `docs/guide/custom-agents.md` + working example project under `examples/`

### IAW-03: Verify agent discovery works for external NuGet assemblies

**Labels:** `integration`

**Description:**
The `AgentDiscovery` in DevUI auto-discovers all `IAgent` subinterfaces from loaded assemblies. Verify and fix if needed:

- Agents defined in a separate NuGet package are discovered when the package is referenced by the silo host
- Agent registry (`IAgentRegistry`) correctly indexes external agents
- DevUI agent selector dropdown shows external agents
- ThreadAgent can route to external agents via `IAgentSelector`

**Acceptance criteria:**
- Create a test project with a custom agent in a separate assembly
- Reference it from Agents.Host
- Verify discovery, registry, routing, and DevUI all work

### IAW-04: Stabilize public grain interfaces for consumer compatibility

**Labels:** `integration`

**Description:**
External consumers will pin IAW package versions. Breaking changes to grain interfaces (`IAgent`, `IThread`, `IApprover`, etc.) would break all consumers.

**Requirements:**
- Audit all public grain interfaces and mark them as stable or experimental
- Document the versioning/compatibility policy
- Add `[Obsolete]` attributes before removing any public API
- Consider `IAgent` v2 pattern if breaking changes are needed

---

## Phase 2 — Features for Agent Consumers

### IAW-05: Add vision model tier support

**Labels:** `feature`

**Description:**
The current LLM tier system (Fast/Balanced/Reasoning) only covers text models. Agents that need vision capabilities (image → text) have no way to declare this.

**Requirements:**
- Verify `IChatClient` calls with image content parts work through the existing tier system (Claude Opus, GPT-4o support multi-modal)
- Add a `Vision` tier or a `SupportsVision` capability flag on `LLMModel`
- Add `[Llm<Vision>]` attribute for agents that require vision-capable models
- Fallback: if no vision model configured, agent should fail with clear error
- Document which models support vision per provider

### IAW-06: Kafka-to-Orleans stream bridge adapter

**Labels:** `feature`, `integration`

**Description:**
IAW uses Orleans Streams for internal pub/sub. External systems often use Kafka for event streaming. Provide a bridge adapter so Orleans agents can subscribe to Kafka topics natively.

**Requirements:**
- `KafkaStreamAdapter` that consumes a Kafka topic and publishes to an Orleans stream
- Configuration via Aspire: `.WithKafkaBridge(kafka, "topic-name", "stream-namespace")`
- Agents subscribe via existing `IStreamConsumer<T>` pattern
- At-least-once delivery semantics (manual Kafka commit after Orleans publish confirms)
- Configurable deserialization (JSON → typed event)

### IAW-07: HTTP API tool base class with resilience

**Labels:** `feature`, `integration`

**Description:**
Agents frequently need to call external HTTP APIs as tools (REST endpoints, GraphQL, third-party services). Provide a base class with built-in resilience.

**Requirements:**
- `HttpApiTool` base class in `IAW.Core`
- Built-in Polly resilience: retry with exponential backoff, circuit breaker, timeout
- Auth token forwarding from agent context (user's bearer token → API call)
- OpenTelemetry tracing for all HTTP calls (linked to agent activity)
- Configurable base URL via Aspire service discovery
- JSON serialization/deserialization helpers

### IAW-08: Document Orleans reminder patterns for proactive agents

**Labels:** `documentation`

**Description:**
`Agent.Scheduling.cs` supports Orleans reminders for periodic work, but there's no documented pattern for the proactive agent use case:

**Document the pattern for:**
- Agent schedules its own periodic check (e.g., every 6 hours)
- Agent evaluates a condition on each tick (e.g., weather changed)
- Agent fires a typed event when condition is met
- Event is routed to user's notification channel (Telegram, UI, etc.)
- Agent manages reminder lifecycle (create, update frequency, cancel)
- Avoiding spam (minimum interval between alerts, dedup)

**Deliverable:** `docs/guide/proactive-agents.md` with working example

### IAW-09: Per-user token usage tracking and budget enforcement

**Labels:** `feature`

**Description:**
IAW tracks token usage for OpenTelemetry observability, but doesn't enforce per-user budgets. Consumers need to gate LLM usage by user subscription tier.

**Requirements:**
- `ITokenBudget` grain interface: `CheckBudget(userId, estimatedTokens)`, `RecordUsage(userId, actualTokens)`
- Hook into `Agent` base class: before each LLM call, check budget; after, record usage
- Configurable budget policies per user/tier (e.g., 10K tokens/month for free, 100K for paid)
- Budget exceeded → agent returns friendly error, does not call LLM
- Expose usage metrics via OpenTelemetry counters
- Optional: callback to external billing system (e.g., record `UsageEvent`)

### IAW-10: Agent-to-agent response streaming

**Labels:** `feature`

**Description:**
`GetResponseStream()` returns `IAsyncEnumerable<string>` but streaming is buffered when ThreadAgent routes to a specialist agent. For conversational UX, tokens should stream end-to-end.

**Requirements:**
- ThreadAgent → SpecialistAgent call propagates streaming (not buffered)
- Client (Telegram, DevUI) receives tokens as they're generated
- Fallback: if streaming not possible (e.g., tool call in progress), buffer and send complete
- Backpressure handling for slow consumers

---

## Phase 3 — Nice-to-Have

### IAW-11: Multi-user thread isolation verification

**Labels:** `integration`

**Description:**
Verify that multiple concurrent users each get isolated agent state when using grain keys like `telegram:{userId}`.

**Requirements:**
- Load test with 50+ concurrent thread grains
- Verify no state leakage between users
- Verify durable state persistence per user
- Document recommended grain key patterns for multi-user scenarios

### IAW-12: Embedding and RAG pipeline documentation

**Labels:** `documentation`

**Description:**
Document the RAG (Retrieval-Augmented Generation) pipeline for external consumers:

- How to ingest documents into Qdrant via `DocumentIngestion`
- How `RAGContextProvider` enriches agent prompts with relevant context
- How to configure embedding models via `.WithEmbedding<T>()`
- Recommended chunking strategies for different content types
- How agents query the vector store for relevant information
