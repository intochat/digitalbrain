# Claude Code Session — IAW Issue Creation

Copy-paste the prompt below into a new Claude Code session opened in the IAW repository clone.

---

## Prompt

```
I need you to create GitHub issues in this repository (InteractiveAgents/IAW) for the v0.1.0 release. These are generic open-source feature requests — no mention of any specific consumer or product.

## Step 1: Check existing state

Run these commands first to understand what already exists:
- `gh issue list --state all --limit 50`
- `gh milestone list --state all`
- `gh label list --limit 50`

## Step 2: Create milestone

Create a milestone if it doesn't exist:
- **Name:** `v0.1.0 Release`
- **Description:** "First stable release — NuGet publishing, consumer documentation, core feature gaps"

## Step 3: Create labels

Create these labels if they don't exist:
- `publishing` — Color: `#0E8A16` — "NuGet packaging and CI/CD"
- `integration` — Color: `#1D76DB` — "External consumer integration patterns"

Reuse existing labels where they match (e.g., `documentation`, `feature`, `enhancement`).

## Step 4: Create issues

Create the following issues. Use `gh issue create` with `--milestone "v0.1.0 Release"` and appropriate labels. Present me the full list with titles and labels FIRST for approval before creating.

### Phase 1 — NuGet Publishing & Consumer Readiness

**IAW-01: Set up CI/CD pipeline for NuGet publishing to nuget.org**
Labels: `publishing`
Body: Set up a GitHub Actions workflow to build, pack, and publish IAW packages to NuGet.org.

Packages to publish:
- `IAW.Core`
- `IAW.Agents`
- `IAW.Agents.CSharp`
- `Aspire.Hosting.IAW`
- `Aspire.IAW.Client`
- `IAW.Testing`

Requirements:
- Trigger on release tag (e.g., `v0.1.0-preview.1`)
- SemVer versioning with pre-release support
- NuGet.org API key as GitHub secret
- Build matrix validates all target frameworks before publishing
- Package metadata: description, license (MIT), repository URL, icon
- Symbol packages (`.snupkg`) for debugging
- Dry-run mode for PRs (pack but don't publish)

**IAW-02: Document custom agent creation for external consumers**
Labels: `documentation`, `integration`
Body: Create a guide for developers building agents in their own projects that reference `IAW.Core` and `IAW.Agents` via NuGet.

Guide should cover:
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

Deliverable: `docs/guide/custom-agents.md` + working example project under `examples/`

**IAW-03: Verify agent discovery works for external NuGet assemblies**
Labels: `integration`
Body: The `AgentDiscovery` in DevUI auto-discovers all `IAgent` subinterfaces from loaded assemblies. Verify and fix:
- Agents in a separate NuGet package are discovered when referenced by silo host
- Agent registry (`IAgentRegistry`) indexes external agents
- DevUI agent selector shows external agents
- ThreadAgent routes to external agents via `IAgentSelector`

Acceptance criteria: Create a test project with a custom agent in a separate assembly, reference from Agents.Host, verify discovery + registry + routing + DevUI all work.

**IAW-04: Stabilize public grain interfaces for consumer compatibility**
Labels: `integration`
Body: External consumers pin IAW versions. Breaking changes to grain interfaces break consumers.
- Audit all public grain interfaces, mark as stable or experimental
- Document versioning/compatibility policy
- Add `[Obsolete]` attributes before removing public APIs
- Consider IAgent v2 pattern if breaking changes needed

### Phase 2 — Features for Agent Consumers

**IAW-05: Add vision model tier support**
Labels: `feature`
Body: Current LLM tier system (Fast/Balanced/Reasoning) only covers text. Agents needing vision (image → text) can't declare this.
- Verify `IChatClient` with image content parts works through tier system
- Add `Vision` tier or `SupportsVision` capability flag on `LLMModel`
- Add `[Llm<Vision>]` attribute for vision-requiring agents
- Fallback: no vision model configured → clear error
- Document which models support vision per provider

**IAW-06: Kafka-to-Orleans stream bridge adapter**
Labels: `feature`, `integration`
Body: Provide a bridge so Orleans agents can subscribe to Kafka topics natively via existing `IStreamConsumer<T>` pattern.
- `KafkaStreamAdapter` consumes Kafka topic → publishes to Orleans stream
- Aspire config: `.WithKafkaBridge(kafka, "topic-name", "stream-namespace")`
- At-least-once delivery (manual Kafka commit after Orleans publish)
- Configurable JSON deserialization to typed events

**IAW-07: HTTP API tool base class with resilience**
Labels: `feature`, `integration`
Body: Agents frequently call HTTP APIs as tools. Provide `HttpApiTool` base class in IAW.Core with:
- Polly resilience: retry, circuit breaker, timeout
- Auth token forwarding from agent context
- OpenTelemetry tracing linked to agent activity
- Configurable base URL via Aspire service discovery
- JSON serialization helpers

**IAW-08: Document Orleans reminder patterns for proactive agents**
Labels: `documentation`
Body: Document the proactive agent pattern:
- Agent schedules periodic checks via reminders
- Evaluates conditions on each tick
- Fires typed events when conditions met
- Events route to notification channels
- Reminder lifecycle management
- Spam avoidance (minimum interval, dedup)

Deliverable: `docs/guide/proactive-agents.md` with working example

**IAW-09: Per-user token usage tracking and budget enforcement**
Labels: `feature`
Body: IAW tracks tokens for observability but doesn't enforce per-user budgets.
- `ITokenBudget` grain: `CheckBudget()`, `RecordUsage()`
- Hook into Agent base class: check before LLM call, record after
- Configurable policies per user/tier
- Budget exceeded → friendly error, no LLM call
- OpenTelemetry usage counters
- Optional callback to external billing systems

**IAW-10: Agent-to-agent response streaming**
Labels: `feature`
Body: `GetResponseStream()` is buffered when ThreadAgent → specialist. For conversational UX, tokens should stream end-to-end.
- ThreadAgent → SpecialistAgent propagates streaming
- Client receives tokens as generated
- Fallback: buffer during tool calls
- Backpressure for slow consumers

### Phase 3 — Nice-to-Have

**IAW-11: Multi-user thread isolation verification**
Labels: `integration`
Body: Verify concurrent users get isolated state with grain keys like `user:{userId}`.
- Load test 50+ concurrent thread grains
- No state leakage
- Durable state per user
- Document recommended grain key patterns

**IAW-12: Embedding and RAG pipeline documentation**
Labels: `documentation`
Body: Document RAG pipeline for external consumers:
- Document ingestion into Qdrant
- RAGContextProvider prompt enrichment
- Embedding model configuration
- Chunking strategies
- Agent vector store queries

## Step 5: Report

After creating all issues, list them with their numbers so I can reference them from the TripRadar repo as cross-repo blockers (e.g., `InteractiveAgents/IAW#XX`).
```
