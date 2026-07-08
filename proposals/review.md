# Ino Personal AI Architecture Review

Status: refined architecture review, no implementation.
Date: 2026-07-08
Input: `proposals/ino-system-awareness-proposals.md`

## Executive Summary

Ino should not become one huge assistant grain. The system should become a distributed personal AI operating layer where Ino is the conductor, narrator, and policy gate, while knowledge, tools, memories, tasks, automations, and self-evolution live in typed Orleans grains.

The core architecture should be:

- Orleans grains for durable identities: users, workspaces, conversations, integrations, memories, automations, tasks, catalog records, and self-evolution proposals.
- Synapses and journals as the primary truth for actions, state changes, causal lineage, audit, replay, and explanations.
- A system catalog derived from `IAgent` contracts, integration manifests, automations, MCP read tools, and live grain state.
- A context planner that builds small, structured, provenance-carrying context packets instead of concatenating recent journal strings.
- A model gateway that routes each LLM job to the cheapest reliable model tier, validates structured outputs, tracks latency/cost, and falls back deterministically.
- A self-model plus evaluation loop that can detect repeated failures and propose improvements only through the existing human-approved self-evolution rail.

This is how the system gets "Jarvis-like" without becoming unsafe or fake-smart: it must know what exists, know what evidence supports each claim, know which actions it can perform, explain its own decisions through correlation lineage, and improve itself only through journaled proposals.

## Sources Used

Current Orleans documentation was fetched through Context7 before this review. Relevant documentation areas:

- Virtual actors, grain lifecycle, placement, and stateless workers: <https://learn.microsoft.com/en-us/dotnet/orleans>
- Stateless worker grains: <https://learn.microsoft.com/en-us/dotnet/orleans/grains/stateless-worker-grains>
- Grain placement: <https://learn.microsoft.com/en-us/dotnet/orleans/grains/grain-placement>
- Grain persistence: <https://learn.microsoft.com/en-us/dotnet/orleans/grains/grain-persistence>
- Event sourcing: <https://learn.microsoft.com/en-us/dotnet/orleans/grains/event-sourcing>
- Timers and reminders: <https://learn.microsoft.com/en-us/dotnet/orleans/grains/timers-and-reminders>
- Streams: <https://learn.microsoft.com/en-us/dotnet/orleans/streaming/streams-programming-apis>
- Request scheduling and reentrancy: <https://learn.microsoft.com/en-us/dotnet/orleans/grains/request-scheduling>
- Cancellation: <https://learn.microsoft.com/en-us/dotnet/orleans/grains/cancellation-tokens>

Repo/runtime context checked:

- `aspire doctor`: 5 passed, 0 warnings, 0 failed.
- `aspire describe`: running resources include kernel replicas, clustering, grainstate, journal, qwen, embed, Flutter UI, storage, and Whisper.
- No Qdrant resource is wired in `hosts/DigitalBrain.AppHost/AppHost.cs`; the vector abstraction can use Qdrant only when an endpoint/connection string exists.

## Requirement Shaping

"Extremely smart personal AI assistant" should be translated into testable requirements:

- It can answer "what can you do?" from live system facts, not a hard-coded list.
- It can answer "why did you do that?" from `CorrelationId`, `CausationId`, and journaled synapse lineage.
- It can use Gmail, Salesforce, automations, UI, files, and future integrations only through typed, permissioned capabilities.
- It can remember user preferences and prior work across turns while respecting workspace/user boundaries.
- It can distinguish verified facts, remembered facts, live tool results, and model guesses.
- It can propose improvements to itself, but cannot mutate user-visible behavior except through `SelfEvolutionProposal -> SelfEvolutionDecision -> apply handler -> rollback/audit`.

The requirement that should be rejected is unrestricted autonomous self-modification. The system may self-improve by finding gaps, writing proposals, running evaluation in a branch/sandbox, and asking for approval. It should not silently rewrite its own behavior.

## Current System Review

### Existing Strengths

- `src/DigitalBrain.Kernel.Abstractions/Neuron.cs` already provides dual durable incoming/outgoing journals, global timeline stream subscription, causal stamping, correlation lineage, checkpoints, branching, and OpenTelemetry counters.
- `src/DigitalBrain.Core/Synapse.cs` already carries `SynapseId`, `CorrelationId`, and `CausationId`.
- `src/DigitalBrain.Core/INeuron.cs` already exposes `GetCausalLineageAsync` and `GetTimelineForCorrelationAsync`.
- `src/DigitalBrain.Core/Sdk/IAgent.cs` already provides compiler-checked integration metadata through static virtual members.
- Google and Salesforce already have typed integration neurons and connectors.
- `src/DigitalBrain.Kernel/Grains/ContextNeuron.cs` already supports journal-backed semantic recall with embedding fallback to keyword scoring.
- `integrations/DigitalBrain.Ino/Context/DocumentIngestor.cs`, `VectorStore.cs`, and `QdrantVectorStore.cs` already provide a document ingestion/vector abstraction.
- `src/DigitalBrain.Kernel/SelfEvolution/SelfEvolutionNeuron.cs` and `src/DigitalBrain.Core/SelfEvolution.cs` already implement a durable approval rail.
- `src/DigitalBrain.Kernel/Grains/AutomationNeuron.cs` already uses the global timeline and durable journals for reactive automations.
- Orleans/Aspire wiring already has clustering, reminders, streams, Azure-backed state/journal in Aspire, and multiple kernel replicas.

### Main Gaps

- Ino's awareness is still centered on `InoIntentClassifier._caps`, a small static list.
- `IAgent` metadata is not harvested into a first-class system catalog.
- `KernelStartupWarmupService` activates useful grains but does not seed agent/system knowledge.
- `ContextNeuron.RecallAsync` searches `MemoryStored` journal entries only; it does not query `IVectorStore` or document chunks.
- `BuildContextAsync` in `InoNeuron` builds a thin string from recent journals, recent summaries, tasks, and automations. It has no provenance, no token budget, no capability catalog slice, and no live state section.
- The LLM path is not split by job type. Classification, extraction, planning, summarization, and final response generation should have different models, prompts, budgets, validators, and caching policies.
- Hallucination control is mostly prompt-based today. It should become architecture-based: capabilities, tools, facts, and memories must be typed, sourced, and validated.
- Self-evolution exists, but Ino does not yet have a self-model or evaluation loop that can notice repeated failures and propose concrete improvements.

## Target Architecture

```text
Surfaces
  Flutter, Telegram, MCP, gRPC, future voice
      |
      v
Ino Session / Conversation Grains
  user/workspace scoped conductor, narrator, policy gate
      |
      +--> Context Planner
      |      selects and budgets system facts, memories, live state, evidence
      |
      +--> Intent and Action Planner
      |      deterministic router first, LLM planner only when needed
      |
      +--> Tool/Action Execution
      |      typed neurons, connectors, automations, kernel tasks
      |
      +--> Explanation Path
      |      correlation lineage, context snapshot, tool result provenance
      |
      +--> Self-Improvement Path
             self-model, eval results, proposals, approval rail

Shared Planes
  System Catalog        IAgent metadata, integrations, automations, grains, MCP read surfaces
  Memory Plane          working memory, episodic summaries, semantic/vector memory, preferences
  Event Fabric          synapses, journals, streams, reminders
  Model Gateway         model routing, budget tracking, validation, caching, telemetry
  Governance            permissions, secrets, self-evolution rail, rollback, audit
```

## Orleans Leverage

Orleans is not just hosting. It should shape the assistant.

| Orleans capability | Architecture use |
|---|---|
| Virtual actors/grains | Model every durable identity as a grain: user, workspace, conversation, agent, memory collection, automation, task, self-model, proposal. |
| Grain identity | Strong isolation with keys such as `user:{id}:ino`, `workspace:{id}:context`, `agent:gmail`, `catalog:system`, `task:{id}`. |
| Durable state/journaling | Preserve memory, causality, decisions, catalog changes, and evolution history across activations and restarts. |
| Event sourcing/journals | Make "why" and rollback natural: replay the timeline and correlation chain instead of guessing. |
| Streams | Broadcast synapses, capability registrations, tool results, health events, and proactive signals. |
| Reminders | Schedule daily briefs, periodic awareness sync, recurring checks, and delayed follow-ups without external cron. |
| Stateless worker grains | Run stateless CPU/network-heavy work: embeddings, reranking, LLM calls, extraction, summarization, prompt compression. |
| Placement/scaling | Keep user stateful grains stable while scaling stateless workers and high-throughput integration workers horizontally. |
| Request scheduling | Keep stateful grain turns short. Avoid long LLM waits inside stateful grains where possible. Use call-chain reentrancy only for understood cycles. |
| Cancellation/timeouts | Bound LLM/tool calls and propagate cancellation so slow models or integrations cannot jam conversational state. |

## Recommended Component Model

### Ino Conversation Grain

Scope: user/workspace/session.

Responsibilities:

- Accept user turns from UI/Telegram/MCP/gRPC.
- Maintain immediate conversation state and active task references.
- Ask `ContextPlanner` for a context packet.
- Ask intent/action planner for next step.
- Route execution to typed grains or safe automations.
- Emit `InoResponse`, surfaces, memory summaries, and correlation metadata.
- Never be the long-term source of all system facts.

### System Catalog Grain

Scope: mostly singleton, with user/workspace overlays where permissions differ.

Responsibilities:

- Register `IAgent` metadata from integration contracts.
- Register automation definitions, MCP read tools, pack metadata, and known core neurons.
- Store catalog records as journaled synapses, not static process memory.
- Provide `ListCapabilitiesAsync`, `SearchCapabilitiesAsync`, `ToPromptCatalogAsync`, and `GetCapabilityByIdAsync`.
- Emit `CapabilityRegistered` and future `SystemComponentRegistered` events.

This should replace `InoIntentClassifier._caps` as the source of truth. The classifier can keep a local projection/cache, but not ownership.

### Context Planner

Scope: user/workspace.

Responsibilities:

- Build a structured `ContextPacket`, not a raw string.
- Assign every context item a source, trust level, timestamp, workspace, user scope, token estimate, and correlation/evidence id when possible.
- Merge layers: current request, active conversation, recent journals, episodic summaries, semantic recall, system catalog, live tool state, active tasks, and policy constraints.
- Enforce token budgets per model tier.
- Journal enough of the selected context to explain later why a response was produced.

### Memory Plane

Recommended layers:

- Working memory: current turn and last few turns in the conversation grain.
- Episodic memory: summarized journal chunks such as "last Gmail result" or "last Salesforce query".
- Semantic memory: `MemoryStored` and vector-backed chunks for long-term recall.
- System memory: catalog records from agents, automations, packs, and MCP read tools.
- Preference memory: user-specific durable settings, constraints, tone preferences, defaults, permissions.
- Evidence memory: tool results and documents with provenance.

### Model Gateway

Responsibilities:

- Route by job type: classification, extraction, summarization, planning, reflection, final answer.
- Prefer deterministic code or small/local models when sufficient.
- Use larger/paid models only for tasks that need reasoning quality.
- Track latency, token estimates, cost, failure rate, timeout rate, and fallback rate.
- Support structured output validators and retry policies.
- Centralize prompt templates and model-specific limits.

This prevents every grain from inventing its own prompt and timeout behavior.

### Action Planner and Tool Execution

Responsibilities:

- Convert user intent into typed actions only if the catalog says the capability exists and the user has permission.
- Prefer direct typed grain calls over free-form tool invocation.
- Require confirmation or self-evolution approval for side effects based on risk.
- Store tool results as evidence-linked synapses.
- Feed outputs back into memory summaries and the context planner.

### Self-Model and Self-Improvement

Responsibilities:

- Maintain a journaled model of known capabilities, failures, repeated user corrections, cost anomalies, latency issues, missing integrations, and evaluation regressions.
- Convert repeated problems into `SelfEvolutionProposal` records with:
  - observed evidence,
  - proposed change,
  - expected benefit,
  - tests/evals to run,
  - risk tier,
  - rollback plan.
- Use checkpoint/branch/sandbox patterns before applying risky changes.
- Never bypass `SelfEvolutionDecision`.

## Context Management

The current `BuildContextAsync` should evolve into a query-time context assembly pipeline:

1. Interpret the user turn and classify the needed context domains.
2. Fetch a small live system catalog slice for relevant capabilities.
3. Retrieve recent conversation and relevant journal synapses.
4. Retrieve semantic memories from `ContextNeuron` and, later, `IVectorStore`.
5. Fetch live state only for necessary tools, for example "is Google connected?" or "what automations are active?"
6. Rank items by relevance, recency, trust, user scope, and cost.
7. Compress low-priority sections using a cheaper summarizer if needed.
8. Produce a `ContextPacket` with structured sections and provenance.
9. Render model-specific prompt text only at the edge of the LLM call.
10. Journal the selected context summary and evidence ids.

Suggested packet sections:

- `UserRequest`
- `ConversationState`
- `RelevantCapabilities`
- `PermissionsAndConstraints`
- `RecentCausalHistory`
- `RetrievedMemories`
- `LiveSystemState`
- `ToolEvidence`
- `ActiveTasks`
- `ResponsePolicy`

This is the main path to better answers. More model power without better context will mostly raise cost.

## Hallucination Reduction

Hallucinations should be handled by system design:

- Capabilities come from `SystemCatalog`, not the LLM.
- Live state comes from typed grains/tools, not memory guesses.
- External facts require tool results or explicit user-provided content.
- Every nontrivial claim should map to one of: catalog fact, journal fact, tool result, user input, memory summary, or model inference.
- Model inference must be labeled internally and should not be used as source of truth for actions.
- Structured outputs from the LLM must be parsed and validated. Invalid outputs should fail closed or retry with a smaller schema.
- Intent classification should return confidence and required evidence. Low confidence should ask a clarifying question.
- Tool actions should be impossible if the catalog says the capability is missing or permissions are absent.
- Prompt-injected text from emails/documents should be stored as untrusted evidence and isolated from system instructions.

The user-facing assistant can stay conversational, but the internal path must be evidence-driven.

## LLM Performance

Use a tiered model strategy:

- No LLM: deterministic routing, simple status answers, capability lists, auth checks, known summaries.
- Small/local model: intent fallback, summarization, extraction, context compression.
- Mid model: multi-step planning, ambiguous intent resolution, natural language synthesis.
- High-quality model: complex reasoning, code/design proposals, self-evolution proposal drafting.

Performance rules:

- Do not block stateful grains on long model calls when a stateless worker can do the work.
- Use cancellation and bounded timeouts for every model/tool call.
- Batch embeddings during startup/catalog ingestion.
- Cache embeddings by content hash.
- Cache stable system prompt/catalog fragments by catalog version.
- Avoid sending raw journals; send selected, summarized, sourced context.
- Stream final responses where UI supports it, but keep tool decisions structured and journaled.
- Track p50/p95 latency by job type and model provider.

## Cost Optimization

The cheapest architecture is not always "use the smallest model." It is "avoid model calls unless the model adds value."

Cost controls:

- Capability and status answers should be catalog lookups.
- Repeated summaries should reuse journaled `MemorySummary` unless source evidence changed.
- Embeddings should be deduplicated by content hash and batched.
- Context should be reduced before expensive model calls.
- Model choice should be job-based and budget-aware.
- Paid model usage should be per-user/workspace budgeted.
- Tool results should be cached with explicit freshness rules.
- Self-evolution proposals should include cost impact when they change model use.

## Proactivity

Jarvis-like behavior needs proactivity, but proactivity must be permissioned.

Good proactive loops:

- Daily brief from calendar/email/tasks if connected and allowed.
- Reminder follow-up when a task or automation stalls.
- "I noticed this failed three times. Should I propose a fix?"
- "A new integration was added. I can now do X."
- "This automation has not run successfully in N days."

Use Orleans reminders for schedule, streams for event-driven triggers, and journals for deduplication. Avoid constant polling.

## Security and Privacy

Required boundaries:

- User and workspace scoped grain identities.
- User/workspace vector collections.
- Secrets never enter prompts or memory summaries.
- Integration content stored as untrusted evidence.
- Side effects require capability checks and risk gates.
- Self-evolution is always journaled and approval-gated.
- MCP mutation tools must continue to use the same rail as Ino.

## Observability

The assistant should be measurable as a distributed system:

- Context packet size and selected sources per turn.
- Retrieval hit rate and source type.
- LLM latency, timeout, provider/model, and fallback path.
- Tool call latency, success/failure, and permission denials.
- Hallucination/evidence violations caught by validators.
- Self-evolution proposal count, approval rate, rollback rate.
- Cost per user/workspace/day and cost by job type.
- Correlation lineage length and missing-correlation defects.

The existing `CorrelationId`, `CausationId`, `GetCausalLineageAsync`, and OpenTelemetry instrumentation are the foundation.

## Recommended Path

The best path is a hybrid of the proposal's approaches:

1. Start with minimal seeding from `IAgent` metadata into context and capability registration.
2. Add signal-driven updates so new integrations and automations publish awareness events.
3. Introduce a first-class `SystemCatalog` grain as the source of truth.
4. Replace `BuildContextAsync` with structured context planning.
5. Connect vector/document ingestion only after catalog and context planning prove the need.
6. Add live MCP/grain introspection for freshness.
7. Add self-model and evaluation-driven self-evolution.

Do not start with a giant Qdrant RAG system or broad reflection scanner. The immediate problem is not lack of vectors. It is lack of a reliable source of truth and context assembly pipeline.

