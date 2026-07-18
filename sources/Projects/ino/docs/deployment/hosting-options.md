# Hosting Options: Azure Container Apps vs AKS

## Azure Container Apps (ACA)

### How Orleans works on ACA

ACA does NOT have native Orleans support. Orleans clustering requires an external membership provider (Redis, Table Storage, Cosmos DB). Silos discover each other via the shared membership table, not via ACA's networking.

### Configuration

```csharp
// Silo on ACA
builder.UseOrleans(silo =>
{
    silo.UseAzureStorageClustering(options =>
        options.ConfigureTableServiceClient(connectionString));
    silo.AddAzureTableGrainStorage("Default", options =>
        options.ConfigureTableServiceClient(connectionString));
    silo.Configure<EndpointOptions>(ep =>
    {
        ep.AdvertisedIPAddress = IPAddress.Parse(Environment.GetEnvironmentVariable("CONTAINER_APP_REPLICA_IP"));
        ep.SiloPort = 11111;
        ep.GatewayPort = 30000;
    });
});
```

### Scaling

- Consumption plan: scale to zero, pay per use (but Orleans silos need `minReplicas: 1`)
- Max 300 replicas per container app (KEDA-based)
- No Kubernetes API access -- cannot use `UseKubernetesHosting()`

### Deployment

```bash
# One-command deployment via azd
cd Aspire/ino.AppHost
azd init          # detects Aspire AppHost, generates azure.yaml
azd up            # provisions ACA + ACR + Storage + Redis, deploys all services

# Or via Aspire CLI (preview)
export DOTNET_ASPIRE_ENABLE_DEPLOY_COMMAND=true
aspire deploy --deployment-param location=westeurope
```

### Pros
- Zero cluster management
- Serverless consumption pricing
- Automatic TLS
- `azd up` deploys everything from AppHost in one command
- Built-in Dapr integration (optional)

### Cons
- No Kubernetes API for silo-pod coordination
- Max 300 replicas
- No fine-grained pod scheduling or node affinity
- Orleans silo networking needs manual IP resolution

## Azure Kubernetes Service (AKS)

### How Orleans works on AKS

First-class support via `Microsoft.Orleans.Hosting.Kubernetes`:

```csharp
builder.UseOrleans(silo =>
{
    silo.UseKubernetesHosting();  // Sets SiloName=pod name, IP=pod IP
    silo.UseAzureStorageClustering(options => ...);
});
```

`UseKubernetesHosting()` provides:
- Sets `SiloName` to pod name
- Sets `AdvertisedIPAddress` to pod IP
- Reads `orleans/serviceId` and `orleans/clusterId` from pod labels
- 2 silos per cluster watch Kubernetes API to detect dead pods
- Marks dead silos automatically (supplements membership protocol)

### Deployment manifest (Aspire-generated)

```bash
dotnet add package Aspire.Hosting.Kubernetes  # in AppHost
aspire publish -o ./k8s-manifests             # generates Deployments, Services, ConfigMaps, Helm charts
kubectl apply -f ./k8s-manifests
# or
helm install ino ./k8s-manifests/charts/ino
```

Required RBAC:
```yaml
rules:
- apiGroups: [""]
  resources: ["pods"]
  verbs: ["get", "watch", "list", "delete", "patch"]
```

### Rolling upgrades

```yaml
strategy:
  rollingUpdate:
    maxUnavailable: 0    # never remove a healthy silo before replacement is ready
    maxSurge: 1          # add one new pod before removing old
terminationGracePeriodSeconds: 180  # allow grain drain
```

### Pros
- Full Kubernetes control: node pools, GPU nodes, spot instances
- `UseKubernetesHosting()` for silo-pod coordination
- Unlimited replicas (node pool dependent)
- Rolling upgrades with grain drain
- Multi-region with Azure Traffic Manager

### Cons
- Operational complexity (cluster upgrades, networking, RBAC)
- Minimum 1 node always running (~$70/mo for D2s_v5)
- Still needs external clustering provider

## Comparison

| Factor | ACA | AKS |
|--------|-----|-----|
| Orleans clustering | External provider only | External + `UseKubernetesHosting()` |
| Scale-to-zero | Yes (Consumption) | No |
| Max replicas | 300 per app | Unlimited |
| Dead silo detection | Membership protocol only (90s) | Membership + Kubernetes API (~30s) |
| Cost (<5 silos) | ~$5-50/mo | ~$140+/mo (VM + control plane) |
| Cost (50+ silos) | Higher per-resource | Lower (VM bulk) |
| Deployment | `azd up` (30 seconds) | `aspire publish` + `helm install` (minutes) |
| GPU support | Limited (Dedicated plan) | Full (GPU node pools) |
| Operational effort | Minimal | Significant |

## Recommendation

**Start with Azure Container Apps** via `azd up`. Covers dev through early production. The Synapse project proves this pattern works with Orleans + Pulumi on ACA.

**Migrate to AKS when:**
- You need >300 replicas
- You need GPU node pools for compute-heavy neurons (ML inference)
- You need Kubernetes-native dead silo detection for faster failover
- You need multi-region deployment with traffic routing
- You need custom scheduling (e.g., co-locate travel neurons on nodes with local Postgres)

The migration is mechanical: `aspire publish` generates the Kubernetes manifests from the same AppHost. Add `UseKubernetesHosting()` to the silo configuration. The Orleans clustering/persistence configuration stays identical.
