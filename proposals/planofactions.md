# Ino Personal AI Plan of Actions

Status: proposed plan, no implementation.
Date: 2026-07-08
Input: `proposals/ino-system-awareness-proposals.md` and `proposals/review.md`

## Operating Rules

- Keep `CLAUDE.md` as the working rulebook.
- Use Context7 before touching Orleans, Aspire, MCP, Microsoft.Extensions.AI, Qdrant, Google, Salesforce, or other framework APIs.
- Use Aspire inspection before implementation cycles.
- Keep all user-visible mutations on the self-evolution rail.
- Prefer deleting static/duplicated knowledge before adding new machinery.
- Use relative paths in repo docs.

## Phase 0: Decisions and Baseline

Goal: lock the target shape before code changes.

Actions:

1. Approve the architecture direction in `proposals/review.md`.
2. Decide whether the first implementation should create a thin `SystemCatalog` immediately or start with an `IAgent` seeder that feeds `ContextNeuron`.
3. Define the first user-facing acceptance prompts:
   - "What can you do?"
   - "Do you have Google/Gmail?"
   - "Do you have Salesforce?"
   - "What automations exist?"
   - "Why did you do that?"
4. Define initial metrics:
   - capability awareness correctness,
   - grounded answer rate,
   - p95 turn latency,
   - model calls per turn,
   - cost per turn,
   - missing-correlation defects.
5. Document risk gates for self-evolution:
   - `None`: docs, catalog descriptions, telemetry labels.
   - `InProcessCode`: scripts, generated run-tier code, prompt policy.
   - `KernelRestart`: deployed code, AppHost/kernel wiring, integration packages.

Definition of done:

- Approved first slice.
- Acceptance prompts and metrics are written into the implementation issue/PR.
- No runtime behavior changed yet.

## Phase 1: Awareness MVP

Goal: Ino can reliably know existing capabilities without static hand edits.

Primary files likely involved:

- `src/DigitalBrain.Core/Sdk/IAgent.cs`
- `integrations/DigitalBrain.Google/IGmailNeuron.cs`
- `integrations/DigitalBrain.Salesforce/ISalesforceCrmNeuron.cs`
- `integrations/DigitalBrain.Ino/InoIntentClassifier.cs`
- `integrations/DigitalBrain.Ino/InoNeuron.cs`
- `src/DigitalBrain.Kernel/Hosting/KernelStartupWarmupService.cs`
- `src/DigitalBrain.Kernel/Grains/ContextNeuron.cs`

Actions:

1. Add an agent metadata seeding path that reads `NeuronAgentMetadata.ReadFrom<TContract>()` for known `IAgent` contracts.
2. Seed each agent as:
   - `CapabilityRegistered` synapse,
   - `ContextNeuron.RememberAsync` text,
   - catalog-ready structured record, even if the catalog grain is deferred.
3. Stop treating `InoIntentClassifier._caps` as the source of truth. Keep it only as a projection/cache during transition.
4. Add a direct "capability/status" response path for "what can you do?" and "do you have X?".
5. Include retrieved capabilities in generic `BuildContextAsync` until the structured context planner exists.
6. Add tests:
   - Gmail metadata is discoverable from `IAgent`.
   - Salesforce metadata is discoverable from `IAgent`.
   - Ino answers capability questions without requiring a live LLM.
   - A fake test `IAgent` can be registered without editing Ino's static list.

Definition of done:

- Ino can answer Google/Gmail/Salesforce capability questions from seeded metadata.
- Adding a new agent contract requires no Ino classifier edit for basic awareness.
- Existing Ino chat tests remain green.

## Phase 2: Causal Explanation

Goal: "Why did you do that?" becomes a first-class capability.

Primary files likely involved:

- `src/DigitalBrain.Core/INeuron.cs`
- `src/DigitalBrain.Kernel.Abstractions/Neuron.cs`
- `integrations/DigitalBrain.Ino/InoNeuron.cs`
- `src/DigitalBrain.Mcp/DigitalBrainReadTools.cs`

Actions:

1. Add an Ino intent for:
   - "why did you do that?"
   - "explain last action"
   - "explain correlation <id>"
2. Resolve the correlation id from:
   - explicit user-provided id,
   - last `InoResponse`,
   - last task/action/tool synapse.
3. Query `GetCausalLineageAsync` on relevant grains.
4. Summarize timeline as:
   - user request,
   - selected intent,
   - context sources,
   - tool/action calls,
   - outputs,
   - confidence/fallbacks.
5. Extend MCP read tools with safe causal-lineage inspection if useful.
6. Add tests:
   - explanation includes correlation id,
   - explanation includes at least one causal synapse,
   - missing correlation returns a clear "I do not have enough lineage" response.

Definition of done:

- Last-action explanation works without guessing.
- Explanation is based on journals and correlation ids, not LLM memory.

## Phase 3: System Catalog Grain

Goal: create a durable source of truth for system awareness.

New contract/grain shape:

- `ISystemCatalogNeuron`
- `SystemCatalogNeuron`
- `AgentRegistered`
- `SystemComponentRegistered`
- `CatalogSnapshot`

Suggested APIs:

- `RegisterAgentAsync(AgentCatalogRecord record)`
- `ListCapabilitiesAsync(scope)`
- `SearchCapabilitiesAsync(query, top)`
- `GetCapabilityAsync(id)`
- `ToPromptCatalogAsync(query, budget)`
- `GetCatalogVersionAsync()`

Actions:

1. Create structured catalog records for:
   - `IAgent` integrations,
   - core neurons,
   - automations/reactions,
   - MCP read tools,
   - packs.
2. Journal registration events in the catalog grain.
3. Build startup registration from explicit known assemblies/contracts first. Avoid broad hot-path reflection.
4. Add signal-driven updates for new automations and packs.
5. Update Ino capability/status responses to read from the catalog.
6. Update classifier retrieval to use catalog search before LLM classification.
7. Add tests:
   - catalog survives grain deactivation/reactivation,
   - duplicate registration is idempotent,
   - automation registration appears in catalog,
   - catalog prompt string is bounded and deterministic.

Definition of done:

- `SystemCatalogNeuron` is the only durable source for system capabilities.
- Static Ino capability ownership is removed or clearly marked as fallback projection.

## Phase 4: Structured Context Planner

Goal: replace raw context concatenation with sourced, budgeted context packets.

New concepts:

- `ContextPacket`
- `ContextItem`
- `ContextSourceKind`
- `ContextTrustLevel`
- `ContextBudget`
- `ContextPlanner`

Actions:

1. Create a context packet model with sections:
   - user request,
   - conversation state,
   - relevant capabilities,
   - permissions/constraints,
   - recent causal history,
   - retrieved memories,
   - live system state,
   - tool evidence,
   - active tasks,
   - response policy.
2. Add token/size estimation and per-model budgets.
3. Add provenance to every item:
   - source type,
   - source id,
   - timestamp,
   - correlation id,
   - trust level.
4. Make Ino call the planner before LLM calls.
5. Journal a compact context snapshot or selected evidence ids for explainability.
6. Add tests:
   - packet respects budget,
   - untrusted document content cannot override system instructions,
   - relevant capability appears for capability questions,
   - recent Gmail/Salesforce summary appears for follow-up questions,
   - missing evidence is represented explicitly.

Definition of done:

- `BuildContextAsync` no longer owns context selection logic.
- Every LLM answer can be traced to selected context items.

## Phase 5: Vector and RAG Integration

Goal: use semantic memory where it helps, without making vectors the source of truth.

Primary files likely involved:

- `integrations/DigitalBrain.Ino/Context/DocumentIngestor.cs`
- `integrations/DigitalBrain.Ino/Context/VectorStore.cs`
- `integrations/DigitalBrain.Ino/Context/QdrantVectorStore.cs`
- `src/DigitalBrain.Kernel/Grains/ContextNeuron.cs`
- `hosts/DigitalBrain.AppHost/AppHost.cs` only if Qdrant is approved.

Actions:

1. Connect `ContextNeuron` to `IVectorStore` for long-term document/system memory recall.
2. Keep journaled `MemoryStored` as a durable fallback and audit trail.
3. Add collections by user/workspace using existing `WorkspaceIds.VectorCollection` style.
4. Ingest system catalog records as vector documents.
5. Ingest user-provided documents and selected memories with provenance.
6. Add Qdrant to Aspire only after the in-memory path proves the API and tests.
7. Add tests:
   - vector recall returns catalog records,
   - keyword fallback still works when embeddings fail,
   - user/workspace isolation is enforced,
   - re-ingesting the same document updates instead of duplicating.

Definition of done:

- Context planner can use semantic recall with provenance.
- The system does not depend on Qdrant for basic awareness.

## Phase 6: Model Gateway and LLM Quality

Goal: turn LLM usage into an observable, budget-aware subsystem.

New or refactored concepts:

- `ILlmGateway`
- `LlmJobType`
- `LlmBudget`
- `LlmAttempt`
- `StructuredLlmResult<T>`
- stateless worker grains for expensive model jobs.

Actions:

1. Define job types:
   - classify,
   - extract structured data,
   - summarize,
   - plan,
   - final response,
   - self-reflection/proposal.
2. Route each job type to a model tier and timeout.
3. Add structured output validators for classification, action plans, automation specs, and self-evolution proposals.
4. Add retry rules that do not hide invalid outputs.
5. Add telemetry:
   - model/provider,
   - job type,
   - latency,
   - token estimate,
   - fallback,
   - validation failure.
6. Add caching:
   - embeddings by content hash,
   - catalog prompt fragments by catalog version,
   - stable deterministic summaries by source hash.
7. Add tests:
   - invalid structured output fails closed,
   - timeout returns deterministic fallback,
   - cheap model path is used for classification,
   - expensive model path is not used for simple capability status.

Definition of done:

- LLM calls are measurable and job-scoped.
- Simple awareness/status prompts do not require the main chat model.

## Phase 7: Proactive Assistant Loops

Goal: add useful proactivity without noisy autonomy.

Actions:

1. Define permissioned proactive intents:
   - daily brief,
   - stalled task follow-up,
   - repeated failure notice,
   - new integration/capability notice,
   - automation health notice.
2. Use Orleans reminders for schedules.
3. Use Orleans streams/synapses for event-driven triggers.
4. Add deduplication journals so Ino does not repeat the same proactive note.
5. Add user/workspace preferences for quiet hours and allowed proactive domains.
6. Add tests:
   - daily brief reminder fires once per period,
   - repeated signal does not spam,
   - disabled proactive domain stays silent,
   - proactive action requiring side effect asks for confirmation.

Definition of done:

- Ino can proactively inform, not silently act.
- All proactive events are journaled and explainable.

## Phase 8: Self-Model and Self-Evolution

Goal: allow the system to improve itself through measured, approved changes.

New or expanded concepts:

- `SelfModelNeuron`
- `EvaluationRun`
- `ImprovementOpportunity`
- `SelfEvolutionProposal` generator
- eval/sandbox branch runner.

Actions:

1. Track repeated failures:
   - low-confidence classifications,
   - user corrections,
   - tool failures,
   - missing capabilities,
   - hallucination validator failures,
   - high-cost prompts,
   - slow LLM jobs.
2. Convert repeated patterns into `ImprovementOpportunity` records.
3. Generate self-evolution proposals only when evidence threshold is met.
4. Include in every proposal:
   - evidence,
   - hypothesis,
   - proposed change,
   - expected impact,
   - tests/evals,
   - risk,
   - rollback.
5. Use checkpoint/branch/sandbox where feasible before approval.
6. Add apply handlers only for narrow, allowlisted change categories.
7. Add tests:
   - repeated failures create an opportunity,
   - opportunity does not auto-apply,
   - proposal requires approval,
   - duplicate decisions do not double-apply,
   - failed apply emits rollback required.

Definition of done:

- Ino can propose improvements to its own awareness/prompt/catalog/automation behavior.
- No self-improvement path bypasses the rail.

## Phase 9: Security, Privacy, and Governance

Goal: make the assistant safe enough to become more capable.

Actions:

1. Enforce user/workspace scoping for:
   - grains,
   - vector collections,
   - memory summaries,
   - catalog overlays,
   - proactive preferences.
2. Ensure secrets are never included in context packets, memory, vector payloads, or LLM prompts.
3. Mark integration content as untrusted evidence.
4. Add prompt-injection tests using email/document content.
5. Add capability permission checks before tool/action execution.
6. Add policy gates for side effects:
   - read-only,
   - reversible,
   - external side effect,
   - code/system mutation.
7. Add audit views over self-evolution and tool actions.

Definition of done:

- The system can get smarter without widening unsafe mutation paths.
- A user can inspect what Ino did and why.

## Phase 10: Cleanup and Living Docs

Goal: avoid long-term proposal clutter.

Actions:

1. After approval, move the selected architecture decisions into living docs.
2. Delete or archive superseded proposal details.
3. Keep implementation docs close to code:
   - catalog contracts near catalog code,
   - context planner behavior near context code,
   - model gateway behavior near LLM code,
   - self-evolution behavior near rail code.
4. Add a short architecture diagram to the main README only after implementation matches it.

Definition of done:

- The repo has one current source of truth.
- Old plans do not compete with code reality.

## First Vertical Slice Recommendation

Implement this first because it proves the architecture with low risk:

1. Seed `IAgent` metadata for Gmail and Salesforce.
2. Store it in context/capability registration.
3. Make Ino answer "what can you do?" from that source.
4. Add "why did you do that?" for the last response using existing correlation lineage.
5. Add tests for both flows.

This slice uses the current Orleans/journal strengths, deletes static knowledge pressure, and creates user-visible intelligence without adding Qdrant, broad reflection, or autonomous code mutation.

## Do Not Do Yet

- Do not add a large Qdrant dependency before catalog/context planning exists.
- Do not let the LLM invent capabilities.
- Do not scan every assembly on every turn.
- Do not let Ino apply self-evolution directly.
- Do not make `InoNeuron` the owner of all memory, tools, catalog, planning, and evolution.
- Do not optimize prompts before the context data is trustworthy.

## Success Criteria

The architecture is working when:

- A new `IAgent` integration becomes visible to Ino without editing Ino.
- Ino can answer capability questions accurately without an LLM.
- Ino can explain recent actions from correlation lineage.
- LLM answers include only context-backed capabilities and state.
- Simple prompts avoid expensive model calls.
- Repeated failures become proposals, not silent behavior changes.
- User/workspace boundaries are enforced for memory and vectors.
- Cost, latency, and groundedness are visible in telemetry.

