# Ino System Awareness Proposals

**Status**: For human review and approval only. No implementation yet.

**Date**: 2026-07-08  
**Context**: Investigation of trace `e28b8235b4e538867edff08a005eb2f7` and Ino unawareness of capabilities (Google/Gmail, Salesforce, broader system: neurons, synapses, automations, integrations).  
**Source of truth reference**: `CLAUDE.md` (5 steps, pre-change ritual, self-evolution rail, delete-first, Context7 + Aspire MCP, relative paths only).

## Investigation Summary

Aspire MCP tools + code reads + `E:\IAW` review (explicitly checked per request) were used. Direct dashboard trace fetch was unavailable (localhost/private).

**Key findings**:
- `aspire__doctor`: all green (Aspire 13.4.6, embed `nomic-embed-text`, kernels running, no Qdrant resource present).
- `aspire__list_resources`: embed model + LLM (qwen) + Orleans journaling (Azure) + multiple kernels + Google/Salesforce params + flutter-ui. No persistent vector DB.
- Trace logs yielded minimal signal (mostly unrelated FlutterUiNeuron surface handling).
- Root cause of "Ino unaware":
  - `integrations/DigitalBrain.Ino/InoIntentClassifier.cs`: only 5 static `Capability` entries (gmail, salesforce, ...). `RegisterCapability` + journal `CapabilityRegistered` loading exists but is under-used.
  - `InoNeuron.cs`: `OnActivate` calls `RememberCapabilitiesAsync` (only the static list → `IContextNeuron.RememberAsync`) and `LoadCapabilitiesFromJournal`. `BuildContextAsync` is a thin string of recent journals + MemorySummary + automations. No system catalog, no IAgent harvest, no neuron/synapse/automation enumeration.
  - `InoCapabilityRecall.cs` + `ContextNeuron.cs` (in `src/DigitalBrain.Kernel/Grains/`): recall is journal scan of `MemoryStored` synapses scored with `HybridScorer`. Durable and replayable (good), but seeded only with Ino's own tiny set.
  - `IAgent` metadata already exists and is implemented by integrations:
    - `integrations/DigitalBrain.Google/IGmailNeuron.cs`
    - `integrations/DigitalBrain.Salesforce/ISalesforceCrmNeuron.cs`
    - `src/DigitalBrain.Core/Sdk/IAgent.cs` (static virtuals + `NeuronAgentMetadata.ReadFrom<T>`, explicitly "harvested from IAW").
  - No startup task harvests these (unlike warmup in `src/DigitalBrain.Kernel/Hosting/KernelStartupWarmupService.cs`).
  - `CapabilityRegistered` synapse emitted **only** from automation apply handler.
  - Ino.Context vector plumbing (Qdrant/InMemory + `DocumentIngestor`) exists but is **not wired** into `ContextNeuron` recall.
- `E:\IAW` (previous project) contains the mature pattern: `src/Core/Registry/AgentRegistryGrain.cs` + `AgentRegistrationStartupTask.cs` (reflection discovery of IAgent impls, pre-embed with `IEmbeddingGenerator`, `HybridSearchAsync`, `ToPromptStringAsync` for LLM injection). Brain has partial ports (scorer, chunker, IAgent) but not the registry + seeding loop.
- Strengths already present (leverage these):
  - Dual durable journals via Orleans.Journaling (`NeuronJournals`, `MemoryStored`, `MemorySummary`).
  - Synapse has `SynapseId`, `CorrelationId`, `CausationId`; `StampCurrent` propagates cause. `INeuron.GetCausalLineageAsync(correlationId)` + `GetTimelineForCorrelationAsync` already support backtracking.
  - Self-evolution rail (`SelfEvolutionProposal` / `Decision` / apply handlers) is sacred for mutations.
  - ContextNeuron + embed model already wired for hybrid recall.

Ino currently works for explicit Gmail/Salesforce flows via keyword + handlers + journal cross-turn memory, but cannot answer "what can you do?", "do you have Google/Salesforce?", "what neurons/automations exist?", or "why did you do X?" reliably. Adding a new integration does not auto-appear in awareness.

## Goals (Challenged per 5 Steps)

- Ino must be **aware** of Google + Salesforce (and future integrations) without manual edits.
- Auto-discover when new integrations/packs/neurons are added.
- Full awareness of system (neurons, synapses, automations, current state, packs).
- **Self-aware** + self-evolve (Ino can explain itself and propose improvements via the rail).
- Backtrack "why did you do that?" using **correlation ID** + short-term (journals) + long-term (MemorySummary + vector) memory.
- **Smart/dynamic context management** for high-quality LLM calls (BuildContextAsync evolution).
- Potential "Jarvis": proactive, explains its reasoning, discovers capabilities, reasons over the whole OS.
- Keep everything journaled, replayable, human-approved for mutations.

**5-step application to the problem itself**:
1. Questioned "full awareness" — many requirements reduce to "surface the IAgent metadata we already have" + "use existing correlation/journal APIs".
2. Delete first: static capability list duplication, unused vector code paths, plans outside living docs.
3. Simplify: one source (IAgent contracts + one registry/seed) feeding Ino's context + recall.
4. Accelerate: targeted `aspire__*` MCP + restarts + background test polling instead of full runs.
5. Automate last: discovery via contracts + signals (after base is clean).

## Approach Comparison

| # | Name | Auto-Discover | Registry/Seed | Vector/Startup Task | Correlation Backtrack | Dynamic Context | Self-Evo Fit | Deletion Opportunity | Effort | Risk |
|---|------|---------------|---------------|---------------------|-----------------------|-----------------|--------------|----------------------|--------|------|
| 1 | Minimal Pragmatic Seeding | Good (via IAgent + seed) | Thin (warmup + Remember) | Existing Remember (embed on fly) | Leverage existing INeuron APIs | Enhance BuildContext | High (Ino can propose) | High (static list, dups) | Low | Low |
| 2 | IAW-Style Full Registry | Excellent (reflection on IAgent + contracts) | Dedicated grain + records | Explicit pre-embed startup task | + registry metadata for traces | ToPromptString + recall layers | Excellent (registry itself versioned via rail) | Med-High | Med | Med (discovery) |
| 3 | Signal-Driven + Correlation-First | Excellent (event-driven) | None (journals + signals) | Minimal (on-activate + listeners) | First-class (always walk corr ids) | Query-time composition | High | High (statics) | Low-Med | Low (eventual) |
| 4 | Qdrant RAG + System Graph | Good (ingest on startup) | Vector collection | Full DocumentIngestor + startup task | Via chunks + corr in payload | RAG-retrieved system + memory | Med (propose ingest updates) | Med (if we keep journal primary) | Med-High | Med (add Qdrant) |
| 5 | MCP + Live Introspection + Self-Model | Excellent (runtime query) | Live + remembered summaries | Periodic sync task | Direct use of GetCausalLineage | Fresh live + memory layers | Highest (Ino introspects & proposes) | High (less static seed) | Med | Low-Med (tool latency) |

## Detailed Proposals

### 1. Minimal/Pragmatic Seeding (Delete + Simplify First)

**Description**  
Seed capabilities and system facts from existing `IAgent` metadata into `ContextNeuron` during warmup / Ino activation. Enhance `BuildContextAsync` and classifier retrieval. Use the already-existing correlation and lineage APIs for "why" questions. No new grains.

**5 Steps Alignment**  
- Delete: static `_caps`, manual examples, some classifier duplication.  
- Simplify: drive from `NeuronAgentMetadata.ReadFrom<T>()`.  
- Accelerate: reuse warmup + existing Remember path.  
- Automate: contract convention.

**Key Mechanisms**
- **Auto-discover**: New integration implements `IAgent` on its `INeuron` contract (already true for Gmail/Salesforce). Explicit small list or limited scan in seeder picks it up. Document the convention.
- **System awareness**: Seed "neuron:...", "synapse:...", "automation:...", "integration:..." strings + full `AgentDescription` / `AgentInstructions`.
- **Backtrack**: New intent/handler in Ino: "explain <correlation-or-last>" calls `GetCausalLineageAsync` + assembles timeline across grains (Ino, GmailNeuron, AutomationNeuron, SelfEvolutionNeuron) then summarizes.
- **Memory & context**: Journals (short) + MemorySummary (long) + recalled capabilities. `BuildContextAsync` becomes richer (recalls + catalog slice + recent causal chains).
- **Jarvis / self-evo**: Ino can surface "I know these because of X remembered at activation" and stage improvements to the seeder itself via rail.

**Implementation Sketch (relative)**
- `src/DigitalBrain.Kernel/Hosting/KernelStartupWarmupService.cs`: add seeding loop after activating context grain. Use known contracts + `NeuronAgentMetadata`.
- `integrations/DigitalBrain.Ino/InoNeuron.cs`: call richer recall in generic path; add `HandleExplainIntentAsync`; improve `BuildContextAsync`.
- `integrations/DigitalBrain.Ino/InoIntentClassifier.cs`: drive more from recalled data; deprecate some hard-coded.
- Google/Salesforce neurons (optional): on activate or Signal, also `Remember` their full description.
- `src/DigitalBrain.Core/Synapse.cs` + `INeuron.cs`: already sufficient.

**Startup / Vector Aspect**  
The `RememberAsync` (which calls embed) + warmup activation = lightweight "startup vector task". No Qdrant needed yet.

**Pros / Deletions / Risks**  
High deletion potential. Fast to green. Risk: still some central list initially (can evolve).

### 2. IAW-Style Full Registry + Startup Task

**Description**  
Port the mature `E:\IAW` registry pattern. Dedicated grain holds structured `NeuronRecord` / `IntegrationRecord` with pre-computed embeddings, caps, instructions. `ToPromptStringAsync()` injects a living catalog. Startup task discovers + embeds.

**5 Steps Alignment**  
- Delete: lots of duplicated knowledge.  
- Simplify: one registry is source for classifier, context, MCP, Ino.  
- Strong match to harvested IAW code (reuse `HybridScorer`, `TextChunker`).

**Key Mechanisms**
- **Auto-discover**: Assembly scan (limited to known assemblies) or explicit manifests + `IAgent` statics at startup. New integration = implement `IAgent` → appears.
- **Awareness**: Full structured view (neurons, their handled synapses from interfaces, automations from automation grain, current packs).
- **Backtrack**: Registry records can carry example correlation patterns; combine with `GetCausalLineageAsync`.
- **Memory & context**: `registry.ToPromptStringAsync()` + vector recall of records + journal slices in every `BuildContext`.
- **Jarvis**: Ino can literally "list all" or "search system for X" via registry.

**Implementation Sketch**
- New: `src/DigitalBrain.Kernel/Grains/NeuronRegistryGrain.cs` (modeled on IAW `AgentRegistryGrain`).
- New: `src/DigitalBrain.Kernel/Hosting/NeuronRegistryRegistrationStartupTask.cs` (like IAW's task; uses `IEmbeddingGenerator`).
- Wire in AppHost / kernel Program.
- `InoNeuron.cs` + `ContextNeuron.cs`: consume registry.
- Update `InoServiceRegistration.cs` or kernel DI for the grain.

**Startup / Vector Aspect**  
Explicit pre-embed step in the registration task (exactly the "startup task with vector search" idea). Records get `DescriptionEmbedding`.

**Pros / Deletions / Risks**  
Best long-term catalog. Risk around discovery robustness (mitigate with limited scan + tests). Delete the static classifier list over time.

### 3. Signal-Driven + Correlation-First

**Description**  
Everything flows as signals/synapses. Integrations and core components emit rich `CapabilityRegistered` / `SystemComponentRegistered` on activation and pack config. Ino (and a thin aggregator) listens and populates context/memory. "Why" is first-class citizen using correlation propagation that already exists.

**5 Steps Alignment**  
- Delete: most static data structures.  
- Accelerate: pure event-driven, matches the Neuron/Synapse model perfectly.  
- Automate: discovery is the emission.

**Key Mechanisms**
- **Auto-discover**: Any new neuron/pack/integration fires the signal on ready → Ino journal + recall updates immediately (or on next activation).
- **Awareness**: Live + historical via journals.
- **Backtrack**: Default behavior. Every Ino response/action gets a `CorrelationId`; user or Ino can say "why <id>" and walk `GetCausalLineageAsync` + full causal chain.
- **Memory & context**: Journals are the memory. BuildContext composes recent signals + recalled summaries + explicit correlation sub-chains.
- **Jarvis**: Ino becomes an observer that can explain the entire causal history of the OS.

**Implementation Sketch**
- Add emission points in Google/Salesforce neurons, automation, core singletons (small).
- Enhance Ino to subscribe to capability/system signals.
- Add first-class "explain" path that uses existing INeuron correlation methods.
- Optional thin `AwarenessAggregatorNeuron`.

**Startup / Vector Aspect**  
Light. On-activate Remember + signal listeners. Embed happens naturally via Remember.

**Pros / Deletions / Risks**  
Most "pure" to the architecture. Eventual consistency on first boot (acceptable). Highest deletion of statics.

### 4. Qdrant RAG + Full System Graph Ingestion (New)

**Description**  
Add a real persistent vector store. A dedicated ingestion startup task walks contracts, IAgent metadata, synapse definitions, automation scripts, neuron summaries, and even selected source comments. Uses `DocumentIngestor` + chunking to create rich chunks. Ino performs semantic RAG over "the whole brain" + its personal memory.

**5 Steps Alignment**  
- Question: do we need Qdrant today? (maybe later; current journal replay is surprisingly effective).  
- Delete: only after proving value over journal approach.  
- Accelerate: RAG gives high-signal context with fewer LLM turns.

**Key Mechanisms**
- **Auto-discover**: Ingestion re-runs on startup or on "SystemGraphRefresh" signal. New integration's IAgent + any attached docs get chunked automatically.
- **Awareness**: Semantic search over full descriptions, instructions, examples, "synapse: Foo means...", current state summaries.
- **Backtrack**: Store correlation ids inside chunk payloads. Recall can surface relevant causal snippets.
- **Memory & context**: Hybrid: Qdrant RAG for system knowledge + journal `MemoryStored` + `MemorySummary` for personal/short-term. `BuildContextAsync` does multi-source retrieval.
- **Jarvis**: "Search the system for anything related to email automation" becomes a first-class powerful query.

**Implementation Sketch**
- Add Qdrant via Aspire in `hosts/DigitalBrain.AppHost/AppHost.cs` (and wire connection).
- Extend / activate `integrations/DigitalBrain.Ino/Context/` (DocumentIngestor, QdrantVectorStore).
- New or extended startup task that builds "system graph" documents (use `NeuronAgentMetadata` + reflection on known contracts + automation grain export + synapse records).
- Update `ContextNeuron.Remember/Recall` (or add RAG path) and Ino to prefer Qdrant when available.
- `src/DigitalBrain.Aspire/` extensions for Qdrant.

**Startup / Vector Aspect**  
This **is** the full "startup task with vector search". Pre-compute + upsert at boot (or delta).

**Pros / Deletions / Risks**  
Powerful semantic understanding. Requires adding Qdrant (new container/resource). Risk of over-engineering if journal+hybrid is sufficient for personal scale. Delete some journal-only paths later.

### 5. MCP + Live Introspection + Self-Modeling Ino (New)

**Description**  
Ino is not just a consumer of static data — it actively introspects the live system using the existing MCP surface (`src/DigitalBrain.Mcp/`) and grain queries. It maintains a "self-model" (its current understanding of capabilities, recent causal graphs, system state) that it can evolve. Periodic or demand-driven "awareness sync" tasks use tools to discover.

**5 Steps Alignment**  
- Delete: heavy upfront seeding in favor of query-time freshness.  
- Accelerate: reuse MCP tools (already built for exactly this kind of inspection).  
- Automate: Ino itself drives discovery.

**Key Mechanisms**
- **Auto-discover**: Ino (or a helper) calls MCP read tools or grain methods to enumerate current resources, active neurons, registered automations, pack configs. New integration appears because the MCP/AppHost surface sees it.
- **Awareness**: Live + historical. "What neurons are currently active?" is a live query.
- **Backtrack**: Direct calls to `GetCausalLineageAsync(correlationId)` on any grain the tool can reach. Ino can present the full chain.
- **Memory & context**: Ino builds rich context on the fly: live snapshot (via MCP/grains) + vector/journal recall + its own self-model (stored as special MemoryStored entries). Self-model can be updated by Ino proposing changes to itself.
- **Jarvis + self-evolve**: Ino can literally answer "show me the current system graph" or "why did the last automation fire?" by using tools + correlation. It can self-propose improvements to its own awareness model through the rail.

**Implementation Sketch**
- Expose more read-only introspection via existing `DigitalBrainReadTools.cs` or new safe surfaces (no mutation without rail).
- Add `AwarenessSync` logic in Ino (can be a `KernelTask` or on certain intents).
- Ino gains ability to call back into MCP-style tools or direct grains for "list all IAgent implementations", "current automations", "recent synapses by correlation".
- Enhance `BuildContextAsync` to include live sections when appropriate.
- Wire Ino to the Mcp host when enabled.

**Startup / Vector Aspect**  
Light startup (activate Ino + context). "Vector search" is on-demand recall. Periodic sync can re-embed fresh summaries.

**Pros / Deletions / Risks**  
Very dynamic and "alive". Low static data. Depends on MCP/tool reliability and latency (mitigate with caching in context). Highest self-evolution potential — Ino literally uses the system to understand the system.

## Recommendation

**Preferred path for first slice**: Start with **Approach 1 (Minimal)** + elements of **3 (signals + correlation)**. This gives immediate value ("Ino knows its integrations", "explain last action" works), deletes waste, and uses only existing primitives.

Evolve to **2** (full registry) for the structured catalog and excellent prompt injection.

Consider **5** (MCP live) in parallel for the "intelligent living" feeling.

**4** (full Qdrant RAG) is powerful but higher cost — do after proving need (current journal replay + embed is already strong and durable).

All approaches can (and should) make Ino itself use the self-evolution rail when it wants to change its own awareness logic or propose new integrations.

## Next Steps (Approval Required Before Any Code)

1. Review this document.
2. Select 1 primary + any supporting ideas (or request refinements).
3. On approval:
   - Update this file or move key decisions into `README.md` / `CLAUDE.md` (living docs only).
   - Full ritual: Context7 for every touched API (Orleans journaling, Microsoft.Extensions.AI, Aspire resources, etc.), `aspire__doctor`, `aspire__list_resources`, `todo_write`.
   - Small vertical slice + high-severity tests (`dotnet test --logger "console;verbosity=minimal"` from repo root).
   - Targeted resource restart via MCP + log/trace inspection.
   - Self-evolution proposal if the change affects user-visible behavior.

**Do not implement any of the above until explicit approval.**

This file will be treated as a proposal artifact. After decision it should be pruned or summarized into living documentation per `CLAUDE.md`.

---

*Generated from investigation using Aspire MCP, Context7, code reads, and IAW review. All paths are relative to repo root.*