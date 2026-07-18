# ino Deployment Architecture

Production deployment documentation for the ino AI-native operating system.

## Documents

| Doc | What it covers |
|-----|---------------|
| [architecture-assessment.md](architecture-assessment.md) | Current neuron/cortex search architecture, scaling analysis, graph database assessment |
| [azure-stack.md](azure-stack.md) | Recommended Azure services per concern: compute, storage, messaging, observability |
| [hosting-options.md](hosting-options.md) | Azure Container Apps vs AKS comparison, deployment paths |
| [domain-architecture.md](domain-architecture.md) | Multi-domain model (system, travel, ...), Orleans heterogeneous silos, placement filtering |
| [cost-analysis.md](cost-analysis.md) | Tier-by-tier cost projections from dev to hyperscale |
| [deployment-guide.md](deployment-guide.md) | Concrete deployment steps using Pulumi + `azd`, adapted from Synapse project |

## Quick reference

```
Dev (current):   Memory clustering + memory grain state + memory streams
Production:      Cosmos DB Serverless (clustering + grain state + reminders)
Scale:           Cosmos DB + NATS JetStream + heterogeneous silos
```

## Decision record

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Compute | Azure Container Apps (start), AKS (scale) | Zero-ops at small scale, full control at large |
| Primary store | Cosmos DB Serverless | One resource for clustering + grain state + reminders + vector search |
| Streams | Azure Queue Storage (start), NATS JetStream (scale) | $0.40/million ops; NATS for sub-ms at volume |
| Graph database | Not now; Apache AGE if needed | Search-based discovery covers runtime dispatch; graph only for visualization |
| Domain isolation | Heterogeneous silos + silo metadata | Single cluster, zero overhead cross-domain calls |
| IaC | Pulumi (C#) | Already proven in Synapse deployment, type-safe, .NET native |

## Deployment

### Prerequisites

```bash
az login                    # Azure CLI
pulumi version              # Pulumi CLI (winget install pulumi)
```

### First-time setup (already done for dev stack)

```bash
cd deployment/ino/Ino.Deployment
pulumi stack init dev
pulumi config set azure:location WestEurope
pulumi config set azure:AZURE_SUBSCRIPTION_ID <your-sub-id>
```

### Deploy infrastructure

```bash
# Set secrets as env vars (optional — stored in Key Vault)
export OPENAI_API_KEY=sk-...
export Telegram__BotToken=...

# Deploy
cd deployment/ino/Ino.Deployment
pulumi up
```

### Build & push container images

```bash
# From repo root (E:\ino)
az acr login --name inodevcontainerregistry

docker build -t inodevcontainerregistry.azurecr.io/ino-silo:latest -f deployment/ino/Dockerfiles/silo.Dockerfile .
docker build -t inodevcontainerregistry.azurecr.io/ino-telegram:latest -f deployment/ino/Dockerfiles/telegram.Dockerfile .
docker build -t inodevcontainerregistry.azurecr.io/ino-mcp:latest -f deployment/ino/Dockerfiles/mcp.Dockerfile .

docker push inodevcontainerregistry.azurecr.io/ino-silo:latest
docker push inodevcontainerregistry.azurecr.io/ino-telegram:latest
docker push inodevcontainerregistry.azurecr.io/ino-mcp:latest

# Update container apps with new images
pulumi up
```

### Iterate

```bash
# Rebuild single image after code changes
docker build -t inodevcontainerregistry.azurecr.io/ino-silo:latest -f deployment/ino/Dockerfiles/silo.Dockerfile .
docker push inodevcontainerregistry.azurecr.io/ino-silo:latest
# ACA picks up :latest on next revision
```
