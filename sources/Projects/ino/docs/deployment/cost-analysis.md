# Cost Analysis

## Storage cost comparison

### Grain state providers

| Provider | $/GB/mo (LRS) | $/10K ops | Max entity size | Best for |
|----------|---------------|-----------|-----------------|---------|
| Azure Table Storage | $0.045 | $0.00036 | 1 MB per row | Small grain state, high read throughput |
| Azure Blob Storage Hot | $0.018 | $0.0054 read, $0.065 write | No limit | Large grain state, binary data |
| Azure Blob Storage Cool | $0.0134 | $0.013 read, $0.13 write | No limit | Cold synapses (decay < 30) |
| Azure Blob Storage Archive | $0.002 | $5.00/10K rehydrate | No limit | Historical synapses (regulatory retention) |
| Redis (Azure Cache Basic C1) | ~$55/mo flat (250MB) | N/A | 512 MB per key | Hot path, sub-ms reads |
| Cosmos DB Serverless | $0.25/GB | $0.25/million RU | 2 MB per doc | Global distribution, complex queries |
| Cosmos DB Provisioned 400 RU/s | $0.25/GB + ~$23/mo base | Included | 2 MB per doc | Predictable workloads |

### Synapse decay -> storage tier mapping

| Decay range | Storage tier | $/GB/mo | Read latency | Write frequency |
|-------------|-------------|---------|--------------|-----------------|
| 50-100 (hot) | Redis or Table Storage | $0.045-$55 | <1ms / ~5ms | High |
| 30-50 (warm) | Table Storage | $0.045 | ~5ms | Low |
| 1-30 (cold) | Blob Storage Cool | $0.0134 | ~50ms | Nightly consolidation only |
| 0 (purged) | Deleted | $0 | N/A | N/A |

The nightly consolidation pass moves synapses between tiers as decay drops. This is a natural cost optimization built into the architecture.

## Messaging cost comparison

| Service | Base cost | Per-op cost | Features |
|---------|-----------|-------------|----------|
| Azure Queue Storage | $0.045/GB | $0.40/million ops | Basic FIFO, 64KB messages, 7-day TTL |
| Service Bus Standard | $10/mo | First 12.5M free, then $0.80/million | Sessions, dead-letter, topics, 256KB |
| Service Bus Premium | ~$668/mo per unit | Included | Dedicated, 100MB messages, geo-DR |
| Event Hubs Standard | ~$11/mo per TU | $0.028/million events | Partitioned log, rewindable, 7-day retention |
| NATS JetStream (self-hosted 3-node) | ~$50-100/mo (3x B2s VMs) | N/A | Sub-ms, at-least-once, exactly-once |

### Cost per 100M synapses/day

| Provider | Monthly cost | Notes |
|----------|-------------|-------|
| Azure Queue Storage | ~$120 | 3B ops/mo (create + read + delete per message) |
| Service Bus Standard | ~$2,410 | (3B - 12.5M free) * $0.80/million |
| Event Hubs (10 TUs) | ~$110 + $84 capture | Cheaper than Service Bus at volume |
| NATS JetStream | ~$100 | Fixed infra cost, no per-message charge |

**Azure Queue Storage wins at low-to-moderate volume. NATS wins at high volume** because there is no per-message cost -- only the infrastructure cost of the cluster.

## Compute cost comparison

### Azure Container Apps

| Tier | vCPU | Memory | $/mo (estimated) |
|------|------|--------|------------------|
| Consumption (free tier) | 180K vCPU-sec/mo | 360K GiB-sec/mo | $0 |
| Consumption (beyond free) | $0.000012/vCPU-sec active | $0.0000015/GiB-sec active | Variable |
| Dedicated D4 (4 vCPU, 16 GiB) | Per-instance | Per-instance | ~$160/mo |

### AKS

| VM SKU | vCPU | Memory | $/mo |
|--------|------|--------|------|
| B2s | 2 | 4 GiB | ~$30 |
| D2s_v5 | 2 | 8 GiB | ~$70 |
| D4s_v5 | 4 | 16 GiB | ~$140 |
| D8s_v5 | 8 | 32 GiB | ~$280 |
| Standard_NC6s_v3 (GPU) | 6 + V100 | 112 GiB | ~$1,100 |

AKS control plane: free (Standard), $0.10/cluster/hour (Premium ~$73/mo).

## Full stack projections

### Dev / local ($5/mo)

| Resource | Service | Cost |
|----------|---------|------|
| Compute | ACA Consumption (free tier) | $0 |
| Clustering | Memory (local) | $0 |
| Grain state | Memory (local) | $0 |
| Streams | Memory (local) | $0 |
| Tunnel | ngrok free tier | $0 |
| Observability | Aspire dashboard (local) | $0 |
| Secrets | Local user secrets | $0 |
| **Total** | | **~$5** (minor storage) |

### MVP ($80/mo)

| Resource | Service | Cost |
|----------|---------|------|
| Compute | ACA Consumption (1 silo + 2 clients) | ~$10 |
| Clustering | Azure Cache for Redis Basic C1 | $55 |
| Grain state | Azure Table Storage (1 GB) | ~$1 |
| Streams | Azure Queue Storage (1M msgs/day) | ~$1 |
| Observability | Application Insights (5 GB free) | $0 |
| Secrets | Azure Key Vault Standard | ~$1 |
| Container images | ACR Basic | $5 |
| **Total** | | **~$80** |

### Production ($250/mo)

| Resource | Service | Cost |
|----------|---------|------|
| Compute | ACA Consumption (2 silos + 3 clients) | ~$30 |
| Clustering | Azure Cache for Redis Basic C1 | $55 |
| Grain state (hot) | Azure Table Storage (10 GB) | ~$5 |
| Grain state (cold) | Azure Blob Cool (50 GB) | ~$2 |
| Streams | Azure Queue Storage (10M msgs/day) | ~$5 |
| Vector search | Qdrant on B2s VM | ~$30 |
| Observability | App Insights (20 GB) | ~$42 |
| Secrets | Azure Key Vault | ~$1 |
| Container images | ACR Standard | $20 |
| DNS | Azure DNS zone | ~$1 |
| CDN | Azure Front Door Standard | $35 |
| **Total** | | **~$250** |

### Scale ($2,000/mo)

| Resource | Service | Cost |
|----------|---------|------|
| Compute | ACA Dedicated (3 silo groups x 3 replicas) | ~$500 |
| Clustering | Azure Cache for Redis Standard C2 | ~$180 |
| Grain state (hot) | Cosmos DB Autoscale 1000 RU/s | ~$120 |
| Grain state (cold) | Azure Blob Cool (500 GB) | ~$15 |
| Streams | NATS JetStream 3-node | ~$100 |
| Vector search | Qdrant Cloud (managed) | ~$100 |
| Observability | App Insights (100 GB) | ~$200 |
| Secrets | Key Vault | ~$5 |
| Container images | ACR Standard | $20 |
| DNS + CDN | Front Door + DNS | ~$40 |
| Postgres (travel) | Azure Flexible Server GP D2s_v3 | ~$120 |
| Redis (travel) | Azure Cache C1 | ~$55 |
| **Total** | | **~$2,000** |

### Enterprise ($15,000+/mo)

| Resource | Service | Cost |
|----------|---------|------|
| Compute | AKS (20 D4s_v5 nodes + 2 GPU nodes) | ~$5,000 |
| Clustering | Redis Enterprise | ~$500 |
| Grain state | Cosmos DB multi-region (10K RU/s) | ~$3,000 |
| Streams | Event Hubs Premium (4 PUs) | ~$1,400 |
| Vector search | Azure AI Search S1 | ~$250 |
| Observability | App Insights + Log Analytics (1 TB) | ~$2,760 |
| Multi-region | Azure Front Door Premium + Traffic Manager | ~$500 |
| **Total** | | **~$15,000+** |

## Key cost optimizations

1. **Synapse decay = tiered storage.** Hot synapses in Redis/Table, cold in Blob Cool, purged = deleted. The architecture gives you cost optimization for free.

2. **Azure Queue Storage over Service Bus** until you need sessions/dead-letter. 60x cheaper per million operations.

3. **ACA Consumption plan** scales to zero when idle. MVP costs nearly nothing during off-hours.

4. **Redis for everything** at small scale. One $55/mo Redis serves clustering, grain state, and reminders. Split only when you hit the 250MB limit.

5. **NATS JetStream over Event Hubs** at high volume. No per-message charge means predictable cost as synapse volume grows.

6. **Activation shedding** (Orleans 9.0+) prevents memory bloat by deactivating LRU grains under pressure. Keeps compute costs bounded.
