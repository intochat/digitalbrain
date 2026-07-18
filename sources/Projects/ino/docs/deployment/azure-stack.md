# Recommended Azure Stack

## Per-concern recommendations

### Compute

| Scale | Recommendation | Why |
|-------|---------------|-----|
| Dev / MVP | Azure Container Apps (Consumption) | Free tier: 180K vCPU-sec + 360K GiB-sec/mo. Zero cluster management. |
| Production (<10 silos) | Azure Container Apps (Consumption) | ~$5-50/mo. `azd up` deploys everything. Orleans clustering via external provider (Redis/Table). |
| Scale (10-50 silos) | Azure Container Apps (Dedicated) | Workload profiles for CPU-heavy neurons (Roslyn, code execution). ~$200-800/mo. |
| Large (50+ silos) | AKS with `UseKubernetesHosting()` | Full control: pod scheduling, node pools, GPU nodes, rolling upgrades with grain drain. |

### Orleans clustering

| Option | Latency | Cost | Operational complexity |
|--------|---------|------|----------------------|
| **Redis (recommended)** | <1ms | ~$55/mo (Azure Cache Basic C1) | Lowest -- one resource for clustering + persistence + reminders |
| Azure Table Storage | ~5-15ms | ~$1/mo | Very cheap but higher latency |
| Cosmos DB | ~5ms | ~$25/mo (400 RU/s) | Best for global distribution |
| ADO.NET (PostgreSQL) | ~3-10ms | ~$50/mo (Flexible Server B1ms) | Good if already running Postgres |

**Aspire wiring:**
```csharp
var redis = builder.AddRedis("redis");
var orleans = builder.AddOrleans("cluster")
    .WithClustering(redis)
    .WithGrainStorage("Default", redis)
    .WithReminders(redis);
```

### Grain state persistence

| Data type | Provider | Cost | Rationale |
|-----------|----------|------|-----------|
| Hot synapse state (decay 50-100) | Redis | Included in clustering Redis | Sub-ms reads, already provisioned |
| Warm grain state (general) | Azure Table Storage | $0.045/GB + $0.00036/10K ops | Cheapest durable store, 1MB entities cover 95% of grains |
| Cold synapses (decay 1-30) | Azure Blob Storage Cool | $0.0134/GB + $0.013/10K reads | 70% cheaper than Table, acceptable latency for recall |
| Neuron scripts / large artifacts | Azure Blob Storage Hot | $0.018/GB | No size limit, good for Roslyn script source |
| Purged synapses (decay 0) | Hard delete | $0 | Gone is gone |

**Tiered storage maps naturally to synapse decay.** The nightly consolidation pass can migrate blobs between tiers as decay drops.

### Orleans Streams (synapse signal delivery)

| Provider | Throughput | Latency | Cost | When to use |
|----------|-----------|---------|------|-------------|
| **Azure Queue Storage** (recommended start) | Moderate (pulling agents) | ~50-200ms | $0.40/million ops | Day 1 through ~100M synapses/day |
| Azure Service Bus Standard | Good | ~10-50ms | $10/mo base + $0.80/million ops after 12.5M free | When you need dead-letter queues, sessions |
| Azure Event Hubs Standard | Very high (millions/sec) | ~5-20ms | ~$11/mo per throughput unit | Time-travel replay, analytics, ML training |
| **NATS JetStream** (recommended scale) | 500K msg/s per node | Sub-ms | Self-hosted (~$50/mo for 3-node cluster) | When Azure Queue latency is too high |

**Aspire wiring:**
```csharp
var storage = builder.AddAzureStorage("storage");
var queues = storage.AddQueues("streaming");
var orleans = builder.AddOrleans("cluster")
    .WithStreaming("AzureQueueProvider", queues);
```

### Observability

Already in place via OpenTelemetry + Aspire dashboard. For production:

| Service | Purpose | Cost |
|---------|---------|------|
| Application Insights | Traces, metrics, logs from .NET silos | Free up to 5GB/mo, then $2.76/GB |
| Log Analytics Workspace | Centralized log aggregation | $2.76/GB ingested |
| Azure Monitor Alerts | SLA monitoring, neuron failures | ~$0.10/alert rule/month |

The Flutter OTLP bridge (`src/Telegram/Program.cs`) already forwards browser telemetry to Aspire's OTLP endpoints. In production, point the same bridge at Application Insights.

### Vector search

| Option | When | Cost |
|--------|------|------|
| In-memory (current) | <10K neurons | $0 (in-process) |
| Qdrant (planned) | 10K-10M neurons | ~$30/mo (Azure VM B2s + Docker) or Qdrant Cloud |
| Azure AI Search | 10K-10M, want managed | ~$75/mo (Basic tier) |
| Cosmos DB vector search | Already on Cosmos | Included in RU cost |

### Secrets

| Service | Cost | Features |
|---------|------|----------|
| Azure Key Vault Standard | $0.03/10K operations | RBAC, soft-delete, rotation policies |
| Managed Identity | Free | Passwordless auth to all Azure services |

**Every container app gets a Managed Identity with:**
- `Key Vault Secrets User` -- read secrets
- `AcrPull` -- pull container images
- `Storage Blob Data Contributor` -- grain state read/write
- `Storage Table Data Contributor` -- clustering read/write

### Container images

| Service | Cost | Features |
|---------|------|----------|
| Azure Container Registry (Basic) | ~$5/mo | 10GB storage, sufficient for dev |
| Azure Container Registry (Standard) | ~$20/mo | 100GB, geo-replication, webhooks |

### DNS & networking

| Service | Purpose | Cost |
|---------|---------|------|
| Azure DNS | Custom domain for ino API | ~$0.50/zone/mo + $0.40/million queries |
| Azure Front Door | CDN + WAF for Flutter web app | ~$35/mo (Standard) |
| ngrok (dev only) | Telegram webhook tunnel | Free tier or $8/mo |

## Full stack cost summary

| Tier | Monthly estimate | What you get |
|------|-----------------|-------------|
| **Dev** | ~$5 | ACA Consumption free tier, memory Orleans, ngrok tunnel |
| **MVP** | ~$80 | ACA + Redis (C1) + Table Storage + Queue Storage + App Insights (5GB) + Key Vault |
| **Production** | ~$250 | Above + Qdrant + Blob Cool tier + Service Bus Standard + DNS |
| **Scale** | ~$2,000 | ACA Dedicated + Cosmos DB + NATS cluster + multiple silo groups |
| **Enterprise** | ~$15,000+ | AKS + Cosmos DB multi-region + Event Hubs + GPU nodes |
