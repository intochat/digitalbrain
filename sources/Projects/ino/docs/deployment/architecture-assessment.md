# Architecture Assessment: Neuron Search & Cortex Discovery

## Current implementation

### Agent registry (compile-time neurons)

**Files:** `src/Core/Registry/AgentRegistryGrain.cs`, `AgentRecord.cs`, `AgentRegistrationStartupTask.cs`

The registry is a **singleton Orleans grain** keyed `"global"`. At silo startup, `AgentRegistrationStartupTask` reflects over all assemblies, finds classes inheriting `IAW.Core.Agent`, extracts metadata (display name, description, capabilities, routing examples), generates a 1536-dim OpenAI embedding, and calls `RegisterAsync()`.

**HybridSearchAsync** (`AgentRegistryGrain.cs:76-108`) combines:
- **Vector score** (60% weight): cosine similarity between query embedding and `AgentRecord.DescriptionEmbedding` (1536-dim, `text-embedding-3-small`)
- **Keyword score** (40% weight): term overlap on `Description + Capabilities + RoutingExamples + DisplayName + InterfaceName + AgentType`
- Falls back to keyword-only when embeddings are zero vectors
- Returns top-k (default 5) candidates with score > 0

**AgentRecord fields** (all Orleans-serializable):
`Id`, `Namespace`, `AgentType`, `DisplayName`, `Description`, `Capabilities[]`, `InterfaceName`, `RoutingExamples[]`, `DescriptionEmbedding` (1536-dim), `Domain`, `Origin` (CompileTime/Runtime), `UISchema?`, `ScriptSource?`, `SynapseSchema?`, `InstalledAt?`

### Neuron registry (runtime neurons)

**File:** `features/ino-new/InoNew.Core/NeuronRegistryGrain.cs`

Separate singleton grain with `Dictionary<string, Neuron>` + `List<Synapse>`. Created via `CreateAsync(NeuronBlueprint)`. Not part of `IAgentRegistry` -- intentional split per known-problem #7.

### Cortex routing

**File:** `features/ino-new/InoNew.Core/CortexGrain.cs`

Queries `INeuronRegistry.ListNeuronsAsync()`, filters specialists with non-null `SynapseSchema`, builds LLM system prompt with all specialist IDs + schemas, LLM outputs routing decision JSON `{ specialistId, verb, payload }`, fires synapse.

### Behavior memory (vector store)

**File:** `features/ino-new/InoNew.Core/InMemoryVectorStore.cs`

Thread-safe in-memory cosine similarity scan. Snapshot isolation under lock. Future: Qdrant-backed adapter.

## Scaling analysis

| Component | Current | At 10K neurons | At 1M neurons | At 1B neurons |
|-----------|---------|----------------|----------------|----------------|
| AgentRegistry | O(n) scan, ~100 agents, singleton grain | Fine | O(n) scan is 50ms+ | Breaks -- must partition |
| NeuronRegistry | O(n) scan, linear synapse list | Fine | Need secondary index on synapses | Must shard by domain |
| HybridSearch | Full scan with cosine similarity | Fine | 1M cosine comparisons per query (~500ms) | Needs approximate nearest neighbor (ANN) index |
| Cortex routing | Prompt with all specialist schemas | Fine | LLM prompt too long | Must pre-filter by domain |

### Bottleneck transitions

**10K-100K neurons:** The singleton registry grain becomes a read hotspot. **Fix:** Cache the full catalog on each silo with a `TypeManagementOptions.TypeMapRefreshInterval`-like TTL. Reads hit local cache; writes go to the singleton.

**100K-1M neurons:** Linear cosine similarity scan is too slow. **Fix:** Move vector search to Qdrant (already planned). Qdrant's HNSW index gives O(log n) approximate nearest neighbor search.

**1M-100M neurons:** Single registry grain state exceeds practical size. **Fix:** Partition registry by domain. Each domain gets its own `AgentRegistryGrain` keyed by domain name. Cross-domain search fans out to all domain registries.

**100M+ neurons:** Even partitioned registry state is large. **Fix:** External storage-backed grain directory (Azure Table or Redis). The grain directory itself handles activation lookup; the search layer uses Qdrant with domain-scoped collections.

## Graph database assessment

### Do we need one?

**The core discovery pattern is search, not traversal.** When a neuron asks "who handles X?", it runs vector+keyword search over `AgentRecord` -- a nearest-neighbor lookup, not a graph walk. The `HybridSearchAsync` already solves this cleanly.

Synapse relationships are 1-hop (sender -> receiver). The decay-tagged `SynapseStoreGrain` partitioned per receiver is a key-value pattern. No multi-hop traversal needed for runtime dispatch.

### When graph would matter

1. **Neural map visualization (#6):** "Show all neurons transitively connected to X within 3 hops" -- graph traversal.
2. **Cortical atlas (#9):** "Shortest path between neuron A and neuron Z" -- graph algorithm.
3. **Impact analysis:** "If neuron X fails, what downstream neurons are affected?" -- reachability query.
4. **Synapse pattern mining:** "Find common firing patterns across the network" -- subgraph matching.

These are all **read-time analytical** queries, not **write-time dispatch** operations. They can be served by a separate materialized graph view without touching the hot path.

### Candidates if we add graph later

| Database | Strengths | Cost (10M neurons, 100M synapses) | Fit for ino |
|----------|-----------|-------------------------------------|-------------|
| **Apache AGE** | PostgreSQL extension, Cypher, zero new infra | ~$250/mo (Azure PG 4 vCore) | Best -- no new dependency, SQL+Cypher in one DB |
| **Cosmos DB Gremlin** | Global distribution, serverless, Azure-native | ~$78/mo (serverless) | Good if already on Cosmos for grain state |
| **Neo4j Aura** | Best graph performance, $100M AI investment | ~$5,400/mo minimum | Overkill for ino's visualization use case |
| **TigerGraph** | Massively parallel, billions of vertices | $100K+/year enterprise | Way overkill |

### Recommendation

**Do not add a graph database now.** The current architecture scales to millions of neurons with partitioned registries + Qdrant vector search. If neural-map or cortical-atlas visualization demands multi-hop traversal, add Apache AGE as a PostgreSQL extension -- ino already needs Postgres for TripRadar, so it is zero new infrastructure.

The Orleans grain directory IS a distributed graph at runtime (grain -> silo mappings, activation references). Adding another graph on top would be redundant for the dispatch path.

## Scaling roadmap

```
Phase 1 (now - 10K neurons):
  - Singleton AgentRegistry + in-memory vector search
  - Fine as-is

Phase 2 (10K - 100K):
  - Add silo-local registry cache with TTL refresh
  - Swap InMemoryVectorStore for Qdrant

Phase 3 (100K - 1M):
  - Partition registry by domain (one grain per domain)
  - Qdrant collections per domain
  - Pre-filter cortex routing by domain

Phase 4 (1M - 100M):
  - External grain directory (Redis or Azure Table)
  - Memory-based activation shedding (Orleans 9.0+)
  - Heterogeneous silos with placement filtering per domain

Phase 5 (100M+):
  - Multi-cluster federation per region
  - Apache AGE for graph analytics (visualization only)
  - NATS JetStream for synapse delivery at volume
```
