# Ino Implementation Plan

Status: proposal only. No implementation.
Date: 2026-07-08
Inputs:
- `CLAUDE.md`
- `proposals/ino-system-awareness-proposals.md`
- `proposals/review.md`
- `proposals/planofactions.md`

## Baseline and Process

Required checks completed before this plan:

- Applied the repo 5-step process from `CLAUDE.md`.
- Used Context7 for current Orleans, Aspire, Microsoft.Extensions.AI, MCP, and Qdrant docs.
- Ran `aspire doctor --non-interactive`: 5 passed, 0 warnings, 0 failed. Aspire CLI and AppHost are 13.4.6.
- Inspected repo structure and relevant code paths before recommendations.
- Confirmed the current Aspire app is running and healthy: `kernel` replicas, `mcp`, `flutter-ui`, `ollama`, `qwen`, `embed`, storage, clustering, grainstate, journal, sync, Google/Salesforce parameters, and Whisper.
- Confirmed Qdrant is not currently an AppHost resource.

5-step application:

1. Make requirements less dumb:
   "Native personal AI assistant" becomes testable capabilities: answer what exists, use only cataloged typed capabilities, remember with provenance, explain actions from lineage, optimize LLM use, and self-improve only through the approval rail.
2. Delete first:
   Remove static capability ownership pressure from `InoIntentClassifier`, avoid a new all-knowing Ino object, do not add Qdrant or broad reflection first, and do not create a separate audit/event system beside journals and synapses.
3. Simplify:
   Start from existing `IAgent`, `CapabilityRegistered`, `ContextNeuron`, dual journals, timeline streams, `SelfEvolutionProposal -> SelfEvolutionDecision -> apply handler`, and MCP split.
4. Accelerate:
   Make capability/status answers deterministic and no-LLM; keep the first slice small enough for focused tests and targeted resource restart later.
5. Automate last:
   Add discovery, vector ingestion, reminders, and self-improvement loops only after the catalog and context packet path are trustworthy.

## Current System Summary

Existing strengths to preserve:

- `src/DigitalBrain.Kernel.Abstractions/Neuron.cs` provides durable incoming/outgoing journals, timeline stream subscription, causal stamping, correlation lineage, checkpoints, branching, and OpenTelemetry counters/histograms.
- `src/DigitalBrain.Core/Synapse.cs` already carries `SynapseId`, `CorrelationId`, and `CausationId`.
- `src/DigitalBrain.Core/INeuron.cs` exposes `GetCausalLineageAsync` and `GetTimelineForCorrelationAsync`.
- `src/DigitalBrain.Core/Sdk/IAgent.cs` already exposes compiler-checked agent metadata through static virtual interface members.
- Gmail and Salesforce already implement `IAgent` through `integrations/DigitalBrain.Google/IGmailNeuron.cs` and `integrations/DigitalBrain.Salesforce/ISalesforceCrmNeuron.cs`.
- `src/DigitalBrain.Kernel/Grains/ContextNeuron.cs` already stores and recalls `MemoryStored` entries with embedding fallback to keyword scoring.
- `integrations/DigitalBrain.Ino/Context/*` already contains vector store and document ingestion abstractions.
- `src/DigitalBrain.Kernel/SelfEvolution/SelfEvolutionNeuron.cs` already enforces the proposal, decision, apply-handler rail.
- `src/DigitalBrain.Kernel/Grains/AutomationNeuron.cs` already uses journals and streams for hot reactions.
- `src/DigitalBrain.Mcp/DigitalBrainReadTools.cs` and `src/DigitalBrain.Mcp/DigitalBrainMutationTools.cs` already separate read and mutation surfaces.

Main gaps to close:

- `integrations/DigitalBrain.Ino/InoIntentClassifier.cs` owns a static capability list and is still the practical source of truth.
- `src/DigitalBrain.Kernel/Hosting/KernelStartupWarmupService.cs` warms grains but does not seed `IAgent` metadata into system awareness.
- `integrations/DigitalBrain.Ino/InoNeuron.cs` seeds only current classifier capabilities into `ContextNeuron`.
- `BuildContextAsync` in `InoNeuron` returns a raw string from recent journals, memory summaries, tasks, and automations. It has no provenance, trust levels, token budget, live capability slice, or selected evidence ids.
- `ContextNeuron.RecallAsync` searches journaled `MemoryStored` only. It does not use `IVectorStore` yet.
- MCP read tools do not expose first-class causal lineage or catalog inspection.
- LLM usage is direct from grains via `IChatClient`; there is no job-typed gateway for model routing, validation, cost, caching, and telemetry.

## Target Architecture Summary

Ino should be the conductor, narrator, and policy gate. It must not become a monolithic god object.

Target shape:

```text
Surfaces
  Flutter, Telegram, MCP, gRPC, future voice
      |
      v
Ino conversation/session grain
  user/workspace scoped conductor and narrator
      |
      +--> System catalog
      |      typed capabilities, integrations, automations, packs, MCP read surfaces
      |
      +--> Context planner
      |      structured context packets with provenance and budgets
      |
      +--> Action planner
      |      deterministic routing first, LLM planning only when needed
      |
      +--> Typed execution
      |      Orleans grains, connectors, automations, read tools
      |
      +--> Explanation path
      |      correlation lineage, context snapshots, evidence ids
      |
      +--> Self-model
             failure/cost/eval observations that can propose through the rail

Shared planes
  Memory plane       journals, summaries, vector recall, preferences, evidence
  Event fabric       synapses, journals, streams, reminders
  Model gateway      job routing, validation, telemetry, caching, cost control
  Governance         permissions, secrets, prompt-injection boundaries, approval rail
```

Core rules:

- Capabilities come from typed catalog records, not LLM guesses.
- Ino can answer capability/status questions without calling an LLM.
- Every side effect routes through typed grains/tools and risk gates.
- Self-improvement never bypasses `SelfEvolutionProposal -> SelfEvolutionDecision -> apply handler`.
- External or user-provided content is evidence, not instruction.
- Qdrant/vector memory is optional acceleration, not the source of truth.

## What To Delete or Simplify First

Delete or demote first:

- Static capability ownership in `InoIntentClassifier._caps`. Keep temporary projection/cache only while the catalog path lands.
- Raw capability text duplication in `InoNeuron.RememberCapabilitiesAsync` once catalog seeding exists.
- Direct capability registration side effects in `AutomationDefinitionApplyHandler` that only update the process-local classifier. Replace with catalog/journal registration.
- Raw `BuildContextAsync` string concatenation as the long-term context mechanism.
- Broad reflection every turn. Discovery should happen during startup/sync and be journaled.
- Early Qdrant AppHost wiring. Add it only after catalog and context packet tests prove the shape.
- New audit infrastructure. Use existing journals, synapses, correlation ids, and OpenTelemetry.

Simplify now:

- Use explicit known `IAgent` contracts for the first slice: Gmail and Salesforce.
- Use existing `CapabilityRegistered` initially instead of inventing a parallel event.
- Use existing `GetCausalLineageAsync` for the first explanation path.
- Keep `ContextNeuron` as memory/recall, not as the owner of every system fact.

## Orleans Design

Use Orleans as the architecture, not just hosting.

- Grains:
  Durable identities: Ino session, system catalog, context/memory, automation, self-evolution, self-model, model gateway workers, integration neurons.
- Journals:
  Primary truth for capability registration, memory, context packet snapshots, tool evidence, proposal/decision/apply results, and explanation lineage.
- Streams:
  Broadcast `CapabilityRegistered`, `SystemComponentRegistered`, tool evidence, automation changes, health signals, and proactive notifications.
- Reminders:
  Use for daily briefs, periodic catalog sync, recurring awareness refresh, stalled-task checks, and repeated-failure aggregation.
- Stateless workers:
  Use for LLM calls, embeddings, reranking, summarization, context compression, validation, and document chunk ingestion. Keep stateful grains short.
- Placement:
  Keep user/workspace stateful grains stable. Scale stateless workers and integration read workers horizontally. Use placement deliberately only after a measured hotspot appears.
- Request scheduling:
  Avoid long LLM waits inside stateful grains. Use cancellation tokens and timeouts for all model/tool work. Keep reentrancy opt-in only when a cycle is understood.
- Correlation lineage:
  Every Ino turn, tool call, context packet, response, automation, and proposal should preserve `CorrelationId` and `CausationId`. Explanation should read lineage, not reconstruct from memory.

## New Grains, Contracts, Synapses, and Services

Add only when the phase needs them.

Phase 1 services:

- `AgentCapabilitySeeder` or equivalent hosted service/helper in `src/DigitalBrain.Kernel/Hosting/`.
- No new grain required for the first slice.
- Use existing `CapabilityRegistered` and `MemoryStored`.

Phase 2 services/tools:

- `InoExplanationService` or internal Ino helper for last-action/correlation explanation.
- MCP read tool such as `get_causal_lineage` in `src/DigitalBrain.Mcp/DigitalBrainReadTools.cs`.
- No new mutation path.

Phase 3 catalog:

- `ISystemCatalogNeuron` in `src/DigitalBrain.Core/`.
- `SystemCatalogNeuron` in `src/DigitalBrain.Kernel/Grains/`.
- Records:
  - `AgentCatalogRecord`
  - `CapabilityCatalogRecord`
  - `SystemComponentRecord`
  - `CatalogSnapshot`
  - `SystemComponentRegistered`
  - optional richer `CapabilityCatalogRegistered`
- APIs:
  - `RegisterAgentAsync(AgentCatalogRecord record)`
  - `RegisterCapabilityAsync(CapabilityCatalogRecord record)`
  - `ListCapabilitiesAsync(CatalogScope scope)`
  - `SearchCapabilitiesAsync(string query, int top, CatalogScope scope)`
  - `GetCapabilityAsync(string id, CatalogScope scope)`
  - `ToPromptCatalogAsync(string query, int budget, CatalogScope scope)`
  - `GetCatalogVersionAsync()`

Phase 4 context:

- `ContextPacket`
- `ContextItem`
- `ContextSection`
- `ContextEvidenceRef`
- `ContextSourceKind`
- `ContextTrustLevel`
- `ContextBudget`
- `ContextPacketSelected`
- `IContextPlanner`
- `ContextPlanner`

Phase 6 model gateway:

- `ILlmGateway`
- `LlmJobType`
- `LlmBudget`
- `LlmAttempt`
- `StructuredLlmResult<T>`
- `LlmValidationFailure`
- stateless workers:
  - `ILlmWorkerGrain`
  - `IEmbeddingWorkerGrain`
  - `IContextCompressionWorkerGrain`

Phase 8 self-model:

- `ISelfModelNeuron`
- `SelfModelNeuron`
- `ImprovementOpportunity`
- `EvaluationRun`
- `SelfModelObservation`
- optional proposal generator service that emits `SelfEvolutionProposal` only after evidence thresholds are met.

## Phased Roadmap

### Phase 0: Decision Lock and Baseline

Goal:
Approve the implementation direction and freeze first-slice acceptance prompts.

Code areas likely touched:

- No runtime code.
- `proposals/implementation-plan.md`
- Later, a small tracking issue or PR description.

Actions:

- Confirm the first slice is: `IAgent` awareness, capability answers, and causal explanation.
- Confirm Qdrant is deferred.
- Confirm no broad reflection scanner on the first slice.
- Confirm capability answers must be deterministic and no-LLM.
- Confirm self-improvement uses only the existing approval rail.

Acceptance criteria:

- This plan is approved or revised.
- First-slice prompts are agreed:
  - "What can you do?"
  - "Do you have Gmail?"
  - "Do you have Salesforce?"
  - "Why did you do that?"
  - "Explain last action"
- No runtime behavior changes.

### Phase 1: Awareness MVP From `IAgent`

Goal:
Ino can accurately answer basic capability questions from existing typed contracts.

Code areas likely touched:

- `src/DigitalBrain.Core/Sdk/IAgent.cs`
- `src/DigitalBrain.Core/Synapse.cs`
- `src/DigitalBrain.Core/Synapses/InoSynapses.cs`
- `integrations/DigitalBrain.Google/IGmailNeuron.cs`
- `integrations/DigitalBrain.Salesforce/ISalesforceCrmNeuron.cs`
- `integrations/DigitalBrain.Ino/InoIntentClassifier.cs`
- `integrations/DigitalBrain.Ino/InoNeuron.cs`
- `integrations/DigitalBrain.Ino/InoCapabilityRecall.cs`
- `src/DigitalBrain.Kernel/Hosting/KernelStartupWarmupService.cs`
- `src/DigitalBrain.Kernel/Grains/ContextNeuron.cs`
- `tests/DigitalBrain.Tests/Ino/InoNeuronChatSurfaceTests.cs`
- `tests/DigitalBrain.Ino.Tests/ContextNeuronTests.cs`

Implementation shape:

- Add a small seeding path that reads:
  - `NeuronAgentMetadata.ReadFrom<IGmailNeuron>()`
  - `NeuronAgentMetadata.ReadFrom<ISalesforceCrmNeuron>()`
- Convert metadata into capability records.
- Emit or store:
  - `CapabilityRegistered`
  - `ContextNeuron.RememberAsync("capability:...")`
  - a structured in-memory projection for capability/status responses.
- Add direct Ino handling for capability questions before generic LLM handling.
- Keep `InoIntentClassifier.Capabilities` only as a temporary projection.
- Do not add a `SystemCatalogNeuron` yet unless Phase 1 becomes harder without it.

New services/synapses:

- Add a small seeder/helper only.
- Reuse `CapabilityRegistered`.
- No new grain in this phase.

Tests:

- Gmail metadata is discoverable from `IAgent`.
- Salesforce metadata is discoverable from `IAgent`.
- Ino answers "what can you do?" without requiring `IChatClient`.
- Ino answers "do you have Gmail?" and "do you have Salesforce?" without requiring `IChatClient`.
- A fake test `IAgent` can be seeded without editing `InoIntentClassifier._caps`.
- Existing Gmail/Salesforce intent tests remain green.

Acceptance criteria:

- Capability answers are grounded in typed metadata.
- The LLM cannot invent a new capability in this path.
- Adding a new explicit `IAgent` contract to the seeder does not require an Ino classifier edit.
- First slice does not add Qdrant, broad reflection, or new AppHost resources.

### Phase 2: Causal Explanation MVP

Goal:
Ino can answer "why did you do that?" from journals and correlation lineage.

Code areas likely touched:

- `src/DigitalBrain.Core/INeuron.cs`
- `src/DigitalBrain.Core/Synapse.cs`
- `src/DigitalBrain.Core/Synapses/InoSynapses.cs`
- `src/DigitalBrain.Kernel.Abstractions/Neuron.cs`
- `integrations/DigitalBrain.Ino/InoNeuron.cs`
- `integrations/DigitalBrain.Ino/InoIntentClassifier.cs`
- `src/DigitalBrain.Mcp/DigitalBrainReadTools.cs`
- `tests/DigitalBrain.Tests/Ino/InoNeuronChatSurfaceTests.cs`
- `tests/DigitalBrain.Tests/Mcp/DigitalBrainToolsTests.cs`

Implementation shape:

- Add deterministic Ino handling for:
  - "why did you do that?"
  - "explain last action"
  - "explain correlation <id>"
- Resolve correlation id from:
  - explicit prompt id,
  - last `InoResponse`,
  - last action/tool/proposal synapse in Ino's journals.
- Call `GetCausalLineageAsync` on relevant grains.
- Produce an explanation with:
  - user request,
  - selected route/intent,
  - selected context sources if available,
  - tool or grain calls,
  - response/result,
  - missing evidence clearly labeled.
- Add read-only MCP lineage inspection.

New services/synapses:

- Optional `InoExplanationGenerated` synapse if explanations need to be journaled.
- Optional `get_causal_lineage` MCP read tool.
- No mutation tools.

Tests:

- Last-action explanation includes a correlation id.
- Explanation includes at least one real synapse from the lineage.
- Missing correlation returns a clear "not enough lineage" response.
- MCP lineage read tool returns only read-only structured data.

Acceptance criteria:

- Explanation never relies on LLM memory.
- It can use an LLM for summarization later, but only over retrieved lineage.
- The answer identifies uncertainty when lineage is incomplete.

### Phase 3: Durable System Catalog

Goal:
Move system awareness out of Ino and into a durable catalog grain.

Code areas likely touched:

- `src/DigitalBrain.Core/Sdk/IAgent.cs`
- `src/DigitalBrain.Core/Synapse.cs`
- `src/DigitalBrain.Core/`
- `src/DigitalBrain.Kernel/Grains/`
- `src/DigitalBrain.Kernel/Hosting/KernelStartupWarmupService.cs`
- `integrations/DigitalBrain.Ino/InoIntentClassifier.cs`
- `integrations/DigitalBrain.Ino/InoNeuron.cs`
- `src/DigitalBrain.Mcp/DigitalBrainReadTools.cs`
- `src/DigitalBrain.Kernel/Grains/AutomationNeuron.cs`
- `src/DigitalBrain.Kernel/AutomationDefinitionApplyHandler.cs`
- `tests/DigitalBrain.Tests/Kernel/`
- `tests/DigitalBrain.Tests/Ino/`
- `tests/DigitalBrain.Tests/Mcp/`

Implementation shape:

- Add `ISystemCatalogNeuron` and `SystemCatalogNeuron`.
- Register explicit `IAgent` contracts at startup first.
- Register automations when they are approved/applied.
- Register MCP read tools as read-only capability records.
- Register pack metadata later through existing pack/config flow.
- Make `InoIntentClassifier` query/search the catalog before LLM classification.
- Make capability/status answers read the catalog.
- Use catalog versioning for cache invalidation and prompt-fragment caching.

New records:

- `AgentCatalogRecord`
- `CapabilityCatalogRecord`
- `SystemComponentRecord`
- `CatalogSnapshot`
- `SystemComponentRegistered`
- optional richer catalog registration synapses.

Tests:

- Catalog survives grain deactivation/reactivation.
- Duplicate registration is idempotent.
- Gmail/Salesforce appear from `IAgent`.
- Automation appears after approved apply.
- MCP read tools appear as read-only capabilities.
- Catalog prompt output is deterministic and budget-bounded.

Acceptance criteria:

- `SystemCatalogNeuron` is the durable source of truth for capabilities.
- Ino does not own the capability registry.
- LLM routing cannot select a capability absent from the catalog.

### Phase 4: Structured Context Packets

Goal:
Replace raw context string assembly with sourced, budgeted context packets.

Code areas likely touched:

- `src/DigitalBrain.Core/Synapses/InoSynapses.cs`
- `src/DigitalBrain.Core/`
- `integrations/DigitalBrain.Ino/InoNeuron.cs`
- `integrations/DigitalBrain.Ino/Context/IContextNeuron.cs`
- `src/DigitalBrain.Kernel/Grains/ContextNeuron.cs`
- `src/DigitalBrain.Kernel/Grains/SystemCatalogNeuron.cs`
- `tests/DigitalBrain.Tests/Ino/`
- `tests/DigitalBrain.Ino.Tests/`

Context packet sections:

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

Each `ContextItem` should include:

- source kind,
- source id,
- trust level,
- user id,
- workspace id,
- timestamp,
- correlation id,
- causation id,
- token/size estimate,
- evidence id,
- whether content is trusted instruction or untrusted evidence.

Implementation shape:

- Add `IContextPlanner`.
- Context planner fetches catalog slice, recent journal slice, memory recall, active task summaries, and live state only when needed.
- `InoNeuron` asks the planner for a packet instead of building a raw string.
- Rendering to prompt text happens at the LLM boundary.
- Journal a compact `ContextPacketSelected` with evidence refs, not full secrets or large untrusted payloads.

Tests:

- Packet respects budget.
- Capability question packet includes relevant catalog item.
- Gmail/Salesforce follow-up packet includes relevant memory summary.
- Prompt-injected document/email content is marked untrusted and cannot become instruction.
- Secrets are excluded.
- Context packet can explain which evidence supported an answer.

Acceptance criteria:

- `BuildContextAsync` no longer owns selection logic.
- Every nontrivial LLM answer can be traced to selected context items.
- Context is scoped to user/workspace.

### Phase 5: Hallucination Resistance and Typed Tool Validation

Goal:
Make groundedness architectural, not just prompt wording.

Code areas likely touched:

- `src/DigitalBrain.Core/`
- `integrations/DigitalBrain.Ino/InoNeuron.cs`
- `src/DigitalBrain.Kernel/Grains/SystemCatalogNeuron.cs`
- `src/DigitalBrain.Mcp/DigitalBrainReadTools.cs`
- `src/DigitalBrain.Mcp/DigitalBrainMutationTools.cs`
- `src/DigitalBrain.Kernel/Foundry/CapabilityGate.cs`
- `src/DigitalBrain.Kernel/Foundry/CapabilityBroker.cs`
- `tests/DigitalBrain.Tests/Mcp/`
- `tests/DigitalBrain.Tests/Foundry/`
- `tests/DigitalBrain.Tests/Ino/`

Strategy:

- Capability claims must resolve to catalog records.
- Live state claims must come from typed grains/tools.
- External facts must come from tool results or user-provided evidence.
- Model inference is internally labeled and cannot authorize actions.
- LLM structured outputs must be parsed and validated against known capabilities and schemas.
- Low confidence classification asks a clarifying question.
- Tool calls fail closed if catalog or permission checks fail.
- MCP tool descriptions and schemas are treated as hints plus validation contracts, not proof of permission.
- Tool outputs are sanitized before entering context.

New records/services:

- `ToolEvidenceRecorded`
- `ValidatedActionPlan`
- `GroundingViolation`
- `CapabilityPermissionDenied`

Tests:

- Unknown capability in an LLM action plan is rejected.
- Prompt-injected email/document cannot override system policy.
- Mutating tool requires risk gate or approval path.
- Capability answer cannot include a made-up integration.
- Tool output sanitization strips secrets and control text from prompt sections.

Acceptance criteria:

- Ino does not invent capabilities.
- Invalid LLM outputs fail closed.
- User-facing answers can distinguish known facts, memories, tool results, and uncertainty.

### Phase 6: LLM Gateway, Performance, and Cost

Goal:
Centralize LLM use as a job-typed, observable, budget-aware subsystem.

Code areas likely touched:

- `src/DigitalBrain.Kernel/Llm/DigitalBrainChat.cs`
- `src/DigitalBrain.Kernel/Llm/ScopedChatClientFactory.cs`
- `src/DigitalBrain.Core/Models/DigitalBrainModelCatalog.cs`
- `src/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs`
- `integrations/DigitalBrain.Ino/InoNeuron.cs`
- `src/DigitalBrain.Kernel/Grains/LlmResponderNeuron.cs`
- `src/DigitalBrain.Kernel/Grains/LlmNeuron.cs`
- `tests/DigitalBrain.Tests/Llm/`
- `tests/DigitalBrain.Tests/Kernel/`
- `tests/DigitalBrain.Tests/Ino/`

Implementation shape:

- Add `ILlmGateway`.
- Define job types:
  - classify,
  - extract,
  - summarize,
  - context-compress,
  - plan,
  - final-response,
  - self-reflection,
  - proposal-draft.
- Route each job type to model role from the existing model registry: fast, balanced, reasoning.
- Use `Microsoft.Extensions.AI` `ChatClientBuilder` middleware for telemetry, function invocation where appropriate, and caching for stable prompts.
- Add timeout, cancellation, retry, and structured validation per job.
- Move expensive stateless work to stateless worker grains.
- Cache:
  - embeddings by content hash,
  - catalog prompt fragments by catalog version,
  - summaries by source/evidence hash,
  - tool reads by freshness policy.

Tests:

- Capability/status prompts do not call LLM.
- Invalid structured output fails closed.
- Timeout returns deterministic fallback.
- Classification uses cheap/fast route where available.
- Expensive model route is not used for simple status.
- LLM attempt telemetry is emitted.

Acceptance criteria:

- LLM calls are measurable by job type.
- Cost and latency are visible per user/workspace.
- Simple prompts avoid expensive calls.

### Phase 7: Vector Memory and Qdrant, After Catalog

Goal:
Use vector recall to improve long-term memory and RAG without making vectors the source of truth.

Code areas likely touched:

- `integrations/DigitalBrain.Ino/Context/VectorStore.cs`
- `integrations/DigitalBrain.Ino/Context/QdrantVectorStore.cs`
- `integrations/DigitalBrain.Ino/Context/DocumentIngestor.cs`
- `integrations/DigitalBrain.Ino/Context/ContextServices.cs`
- `src/DigitalBrain.Kernel/Grains/ContextNeuron.cs`
- `src/DigitalBrain.Kernel/Grains/SystemCatalogNeuron.cs`
- `hosts/DigitalBrain.AppHost/AppHost.cs`
- `src/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs`
- `tests/DigitalBrain.Tests/Context/`
- `tests/DigitalBrain.Ino.Tests/`

Implementation shape:

- First integrate `IVectorStore` behind context planner using the in-memory implementation.
- Store provenance payloads:
  - `docId`,
  - source kind,
  - user id,
  - workspace id,
  - catalog version,
  - evidence id,
  - trust level,
  - timestamp.
- Add user/workspace filters before Qdrant production use.
- Use deterministic ids so re-ingestion updates instead of duplicates.
- Add Qdrant to AppHost only after in-memory tests pass and collection shape is stable.

Tests:

- Vector recall returns catalog records with provenance.
- Keyword fallback works when embeddings fail.
- User/workspace isolation is enforced.
- Re-ingesting same document updates existing chunks.
- Qdrant path is behind configuration and not required for basic awareness.

Acceptance criteria:

- Basic awareness works without Qdrant.
- Vector recall is a ranked evidence source, not a truth source.
- Qdrant rollout has a rollback path: unset connection/AppHost resource and fall back to in-memory/journal recall.

### Phase 8: Proactive Loops With Reminders and Streams

Goal:
Add useful proactivity without silent autonomy.

Code areas likely touched:

- `src/DigitalBrain.Kernel/Grains/ScheduleTriggerNeuron.cs`
- `src/DigitalBrain.Kernel/Grains/PollTriggerNeuron.cs`
- `src/DigitalBrain.Kernel/Grains/AutomationNeuron.cs`
- `integrations/DigitalBrain.Ino/InoNeuron.cs`
- `src/DigitalBrain.Core/Automations.cs`
- `src/DigitalBrain.Core/Synapse.cs`
- `tests/DigitalBrain.Tests/Kernel/`
- `tests/DigitalBrain.Tests/Ino/`

Implementation shape:

- Use reminders for daily brief and recurring checks.
- Use streams for event-driven triggers such as new capability, repeated failure, or automation health.
- Journal dedupe records so Ino does not repeat notifications.
- Add user/workspace preferences for quiet hours and proactive domains.
- Proactivity can inform or ask. It cannot silently perform side effects.

Tests:

- Daily brief reminder fires once per period.
- Duplicate signals do not spam.
- Disabled proactive domain stays silent.
- Side-effecting proactive recommendation asks for confirmation or rail approval.

Acceptance criteria:

- Proactive behavior is permissioned, scoped, journaled, and explainable.

### Phase 9: Self-Model and Safe Self-Improvement

Goal:
Let Ino notice repeated failures and propose improvements without applying them directly.

Code areas likely touched:

- `src/DigitalBrain.Core/SelfEvolution.cs`
- `src/DigitalBrain.Kernel/SelfEvolution/SelfEvolutionNeuron.cs`
- `src/DigitalBrain.Kernel/SelfEvolution/SelfEvolutionApplyHandler.cs`
- `src/DigitalBrain.Kernel/Foundry/*`
- `integrations/DigitalBrain.Ino/InoNeuron.cs`
- `src/DigitalBrain.Kernel/Grains/`
- `tests/DigitalBrain.Tests/Kernel/SelfEvolution*`
- `tests/DigitalBrain.Tests/Foundry/`
- `tests/DigitalBrain.Tests/Ino/`

Implementation shape:

- Add `SelfModelNeuron`.
- Track observations:
  - low-confidence classifications,
  - user corrections,
  - hallucination validation failures,
  - missing capability requests,
  - tool failures,
  - high cost/latency,
  - repeated prompt fallbacks,
  - failed automation applies.
- Aggregate observations into `ImprovementOpportunity`.
- Generate `SelfEvolutionProposal` only when evidence thresholds are met.
- Include in every proposal:
  - evidence ids,
  - hypothesis,
  - proposed change,
  - expected benefit,
  - test/eval plan,
  - risk,
  - rollback plan,
  - apply handler.
- Apply only after `SelfEvolutionDecision.Approved`.

Tests:

- Repeated failures create an opportunity.
- Opportunity does not apply anything.
- Proposal requires explicit approval.
- Duplicate decisions do not double apply.
- Failed apply emits rollback-required when a checkpoint exists.

Acceptance criteria:

- Ino can propose improvements to awareness, prompts, catalog descriptions, model routing, or automations.
- No self-improvement path bypasses the existing approval rail.

### Phase 10: Security, Privacy, and Isolation

Goal:
Make increased capability safe across users, workspaces, prompts, and tools.

Code areas likely touched:

- `src/DigitalBrain.Core/NeuronScope.cs`
- `src/DigitalBrain.Core/Config/`
- `src/DigitalBrain.Kernel/Config/`
- `src/DigitalBrain.Kernel/Auth/`
- `src/DigitalBrain.Kernel/Foundry/CapabilityGate.cs`
- `integrations/DigitalBrain.Ino/Context/*`
- `src/DigitalBrain.Mcp/*`
- `tests/DigitalBrain.Tests/Auth/`
- `tests/DigitalBrain.Tests/Kernel/`
- `tests/DigitalBrain.Tests/Mcp/`
- `tests/DigitalBrain.Tests/Context/`

Controls:

- Scope grain ids by user/workspace where data differs.
- Scope vector collections and payload filters by user/workspace.
- Never include secrets in context packets, summaries, vector payloads, or prompts.
- Treat emails, documents, tool results, and web content as untrusted evidence.
- Separate trusted system/catalog instructions from untrusted evidence text.
- Mutations require typed capability checks and risk gates.
- MCP mutation tools must either stay read-only, ask confirmation, or use the approval rail depending on risk.
- External side effects require explicit confirmation.
- System/code mutations require self-evolution approval.

Tests:

- User A cannot retrieve User B memories.
- Workspace A cannot retrieve Workspace B vector hits.
- Secrets are redacted from context and evidence.
- Prompt-injection content cannot alter system instructions.
- Mutating tool without permission fails closed.

Acceptance criteria:

- Better context does not weaken isolation.
- Prompt injection is handled by data boundaries, not only model instruction.

### Phase 11: Observability and Cleanup

Goal:
Make assistant behavior measurable and keep docs/code reality aligned.

Code areas likely touched:

- `src/DigitalBrain.Kernel.Abstractions/Neuron.cs`
- `src/DigitalBrain.Kernel/Llm/`
- `src/DigitalBrain.Kernel/Grains/`
- `integrations/DigitalBrain.Ino/`
- `src/DigitalBrain.Mcp/`
- `README.md`
- `CLAUDE.md` only through approved self-evolution if the workflow itself changes.

Metrics:

- context packet size,
- context selected source counts,
- retrieval hit rate by source kind,
- catalog version per response,
- LLM calls by job type,
- LLM latency p50/p95,
- timeout/fallback rate,
- estimated token/cost per turn,
- tool call latency and failure rate,
- validation failure rate,
- hallucination/evidence violation count,
- permission denial count,
- correlation lineage length,
- missing correlation defects,
- self-evolution proposal count,
- approval/rejection/apply/rollback counts.

Cleanup:

- Once the implementation lands, move stable decisions into living docs.
- Delete or archive superseded proposal details.
- Keep implementation details near code and tests.

Acceptance criteria:

- Operators can answer why an answer was produced, what evidence it used, what it cost, and which model/tool path ran.
- Old plans do not compete with code reality.

## Context Management Design

Context is not a prompt string. It is a structured packet selected at query time.

Packet assembly flow:

1. Read user request and current conversation state.
2. Determine required domains: capability, memory, tool, document, automation, self-evolution, explanation.
3. Fetch relevant catalog slice.
4. Fetch recent causal history for the active correlation.
5. Fetch scoped memory summaries and semantic recall.
6. Fetch live state only when needed.
7. Attach evidence refs and trust levels.
8. Rank by relevance, recency, trust, and cost.
9. Compress low-priority context with a cheap summarizer if needed.
10. Render model-specific prompt text only at the final LLM boundary.
11. Journal selected evidence ids for later explanation.

Trust levels:

- `System`: repo code, typed catalog records, self-evolution policy.
- `VerifiedToolResult`: typed grain/tool output from a successful call.
- `JournalFact`: durable synapse/journal entry.
- `UserInput`: direct user request or preference.
- `MemorySummary`: generated summary with source refs.
- `UntrustedEvidence`: email/document/web/tool text that may contain prompt injection.
- `ModelInference`: generated reasoning, never a source of truth for actions.

Prompt rendering rules:

- Trusted instructions and untrusted evidence must be separate sections.
- Untrusted evidence must be quoted or delimited as data, not instructions.
- Secrets never render.
- Missing evidence renders as missing, not as a guessed fact.

## Hallucination Reduction Strategy

Use evidence and validation instead of hoping the model behaves.

- Catalog is the source of capability truth.
- Typed grains/tools are the source of action truth.
- Journals are the source of history truth.
- Context packets are the source of prompt truth.
- LLM outputs are proposals or language, not authority.
- Structured LLM outputs must validate against catalog ids, schemas, user/workspace permissions, and risk policy.
- All action plans include required evidence ids.
- Capability questions use deterministic catalog answers.
- "I do not know" is valid when no evidence exists.
- Claims about external systems require fresh tool evidence or a clear stale-memory label.

## LLM Performance and Cost Strategy

Default rule:
Avoid the model unless it adds value.

Routing:

- No LLM:
  capability lists, status checks, auth state, simple timeline explanations, approval state, catalog lookup.
- Fast/local model:
  fallback classification, extraction, small summaries, context compression.
- Balanced model:
  ambiguous multi-step planning and final natural language synthesis.
- Reasoning model:
  complex architecture/design proposals and self-evolution proposal drafting.

Cost controls:

- Cache catalog prompt fragments by catalog version.
- Cache embeddings by content hash.
- Batch embeddings during startup/catalog ingestion.
- Reuse journaled summaries when evidence has not changed.
- Keep context packets small before final answer generation.
- Track model attempts by user/workspace/job type.
- Add per-user/workspace budget ceilings before paid model use expands.

## Security and Prompt-Injection Controls

Controls:

- User/workspace scoped grain keys for user-specific state.
- User/workspace scoped vector collections or mandatory Qdrant filters.
- No secrets in LLM prompts, memory summaries, vector payloads, telemetry, or context packet journals.
- Read-only MCP tools stay read-only.
- Mutating MCP tools validate input, sanitize output, and route risky changes through the rail.
- Tool outputs are untrusted until validated.
- Emails/documents can provide evidence but cannot override system or developer policy.
- External side effects require confirmation.
- Self-evolution and code/system changes require proposal approval.

MCP-specific:

- Tool descriptions help models understand capabilities but do not grant permission.
- Input schemas validate shape.
- Server-side access control validates authority.
- Output sanitization prevents leaking secrets or carrying hidden instructions into trusted context.

## Risks, Dependencies, and Rollback

Risks:

- Catalog drift if startup seeding and event updates disagree.
- Over-broad discovery may register unintended capabilities.
- Context packet complexity may slow the first slice.
- LLM gateway may become a bottleneck if introduced too early.
- Vector memory may leak across users/workspaces if filters are not mandatory.
- Explanation quality may be poor when older flows lack correlation ids.
- Existing MCP mutation tools include some direct mutation paths; do not expand them without policy review.

Dependencies:

- Current Aspire 13.4.6 AppHost.
- Current Orleans journaling preview package behavior.
- Existing `IAgent` metadata on integrations.
- Existing `Microsoft.Extensions.AI` registrations.
- Existing MCP server/tool registration.
- Existing self-evolution apply handlers.
- Optional future Qdrant AppHost resource.

Rollback strategy:

- Phase 1 can fall back to existing classifier capabilities.
- Phase 2 can disable explanation intent without touching core journals.
- Phase 3 catalog can run side-by-side as a projection before replacing classifier ownership.
- Phase 4 context planner can be feature-flagged with old `BuildContextAsync` fallback until tests pass.
- Phase 6 LLM gateway can wrap current `IChatClient` path before replacing callers.
- Phase 7 Qdrant can be removed from AppHost/config and fall back to in-memory/vector-off plus journal recall.
- Self-evolution changes already use proposal/apply result/rollback records.

## Recommended First Vertical Slice

Keep this small:

1. Add a tiny `IAgent` metadata seeding helper for Gmail and Salesforce.
2. Seed metadata into `CapabilityRegistered` and `ContextNeuron.RememberAsync`.
3. Add deterministic capability answer handling in Ino:
   - "what can you do?"
   - "do you have Gmail?"
   - "do you have Salesforce?"
4. Add deterministic causal explanation handling:
   - "why did you do that?"
   - "explain last action"
5. Use existing `GetCausalLineageAsync` and the last `InoResponse` correlation.
6. Add tests:
   - no-LLM capability answer,
   - Gmail/Salesforce discovered from `IAgent`,
   - fake agent can be seeded without editing static Ino list,
   - last-action explanation includes real lineage,
   - missing lineage is explicit.
7. Verify with:
   - `dotnet test --logger "console;verbosity=minimal"` from repo root,
   - `aspire doctor --non-interactive`,
   - targeted Aspire resource inspection/restart only if implementation changes require it.

Do not include in the first slice:

- Qdrant AppHost resource.
- Broad assembly scanning.
- Full `SystemCatalogNeuron`.
- Full `ContextPlanner`.
- LLM gateway replacement.
- New self-evolution apply handlers.
- Autonomous self-modification.

## Approval Needed Before Implementation

Implementation must not start until this plan, or a revised smaller slice, is explicitly approved.

Approval should confirm:

- First slice is limited to `IAgent` awareness, capability answers, and causal explanation.
- Qdrant is deferred.
- Ino will not become a monolithic owner of catalog, memory, tools, models, and self-evolution.
- The LLM will not be allowed to invent capabilities.
- All self-improvement remains on `SelfEvolutionProposal -> SelfEvolutionDecision -> apply handler`.
- Tests and `aspire doctor` are required before claiming implementation complete.
