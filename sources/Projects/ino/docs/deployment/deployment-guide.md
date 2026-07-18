# Deployment Guide

## Architecture

Adapted from the Synapse project at `deployment/Synapse/`, using:
- **Pulumi (C#)** for infrastructure-as-code (version-controlled, type-safe)
- **Azure Container Apps** for compute (serverless, zero-ops)
- **Cosmos DB Serverless** for Orleans clustering + grain state + reminders (one resource)
- **Key Vault** for secrets
- **Managed Identity** for passwordless auth
- **GitHub Actions** for CI/CD

## Pulumi deployment

### Directory structure

```
deployment/ino/
├── Ino.Deployment/
│   ├── Program.cs                    # Entry: Deployment.RunAsync<InoStack>()
│   ├── InoStack.cs                   # Main infrastructure definition
│   ├── Pulumi.yaml                   # Stack metadata
│   ├── Pulumi.dev.yaml               # Dev environment config
│   ├── Configuration/
│   │   ├── ContainerAppDefinition.cs # App spec model (from Synapse)
│   │   ├── ContainerAppDefinitions.cs # ino-specific app definitions
│   │   └── SecretToEnvironmentMapping.cs
│   ├── Constants/
│   │   ├── DeploymentConstants.cs    # Resource names, region, tags
│   │   ├── ConnectionStringNames.cs  # Orleans connection names
│   │   ├── KeyVaultSecretNames.cs    # Secret names
│   │   └── EnvironmentVariables.cs
│   └── Resources/
│       ├── ContainerAppFactory.cs    # Reuse from Synapse
│       ├── ContainerAppsEnvironmentFactory.cs
│       ├── ContainerRegistryFactory.cs
│       ├── KeyVaultFactory.cs
│       ├── LogAnalyticsFactory.cs
│       ├── ManagedIdentityFactory.cs
│       ├── ResourceGroupFactory.cs
│       ├── RoleAssignmentFactory.cs
│       └── StorageAccountFactory.cs
└── Dockerfiles/
    ├── system-silo.Dockerfile
    ├── travel-silo.Dockerfile
    ├── telegram-client.Dockerfile
    └── mcp-client.Dockerfile
```

### Container app definitions

```csharp
// Configuration/ContainerAppDefinitions.cs
public static class ContainerAppDefinitions
{
    public static ContainerAppDefinition SystemSilo => new()
    {
        Name = "ino-system-silo",
        Image = "ino-system-silo",
        Cpu = 1.0,
        Memory = "2.0Gi",
        MinReplicas = 1,
        MaxReplicas = 5,
        Secrets = new[]
        {
            KeyVaultSecretNames.StorageConnection,
            KeyVaultSecretNames.RedisConnection,
            KeyVaultSecretNames.AppInsightsConnection,
            KeyVaultSecretNames.OpenAiApiKey,
        },
        EnvironmentVariables = new Dictionary<string, string>
        {
            ["Orleans__ClusterId"] = "ino",
            ["Orleans__ServiceId"] = "ino",
            ["Orleans__SiloMetadata__domain"] = "system",
        }
    };

    public static ContainerAppDefinition TravelSilo => new()
    {
        Name = "ino-travel-silo",
        Image = "ino-travel-silo",
        Cpu = 0.5,
        Memory = "1.0Gi",
        MinReplicas = 1,
        MaxReplicas = 10,
        Secrets = new[]
        {
            KeyVaultSecretNames.StorageConnection,
            KeyVaultSecretNames.RedisConnection,
            KeyVaultSecretNames.PostgresConnection,
        },
        EnvironmentVariables = new Dictionary<string, string>
        {
            ["Orleans__ClusterId"] = "ino",
            ["Orleans__ServiceId"] = "ino",
            ["Orleans__SiloMetadata__domain"] = "travel",
        }
    };

    public static ContainerAppDefinition TelegramClient => new()
    {
        Name = "ino-telegram",
        Image = "ino-telegram",
        Cpu = 0.25,
        Memory = "0.5Gi",
        MinReplicas = 1,
        MaxReplicas = 3,
        Secrets = new[]
        {
            KeyVaultSecretNames.TelegramToken,
            KeyVaultSecretNames.StorageConnection,
        }
    };
}
```

### Deploy

```bash
cd deployment/ino/Ino.Deployment
pulumi up --stack dev
```

### CI/CD (GitHub Actions)

```yaml
# .github/workflows/deploy.yml
name: Deploy ino
on:
  workflow_dispatch:
    inputs:
      environment:
        type: choice
        options: [dev, staging, production]

jobs:
  deploy:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: '10.0.x' }
      - name: Build & test
        run: dotnet build ino.slnx && dotnet test ino.slnx
      - name: Azure login
        uses: azure/login@v2
        with:
          client-id: ${{ secrets.AZURE_CLIENT_ID }}
          tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
      - name: Build & push images
        run: |
          az acr login --name inocontainerregistry
          TAG=$(date +%Y%m%d)-${{ github.run_number }}
          docker build -t inocontainerregistry.azurecr.io/ino-system-silo:$TAG -f deployment/ino/Dockerfiles/system-silo.Dockerfile .
          docker push inocontainerregistry.azurecr.io/ino-system-silo:$TAG
          # repeat for travel-silo, telegram, mcp
      - name: Pulumi deploy
        uses: pulumi/actions@v6
        with:
          command: up
          stack-name: ${{ inputs.environment }}
          work-dir: deployment/ino/Ino.Deployment
        env:
          PULUMI_ACCESS_TOKEN: ${{ secrets.PULUMI_ACCESS_TOKEN }}
          IMAGE_TAG: $(date +%Y%m%d)-${{ github.run_number }}
```

## Orleans production configuration

### How it works

The silo (`Aspire/ino.Client/IAWSiloExtensions.cs`) auto-detects production mode via `ConnectionStrings__cosmos` env var:

- **Dev (no cosmos connection):** Aspire AppHost provides memory clustering/storage
- **Production (cosmos connection present):** Configures Cosmos DB for everything

```csharp
// Automatic — no code changes needed between dev and production
// Dev:  aspire start (memory providers from AppHost)
// Prod: ConnectionStrings__cosmos=AccountEndpoint=...;AccountKey=...;
//       → UseCosmosClustering + AddCosmosGrainStorage + UseCosmosReminderService
```

Orleans auto-creates the database and containers on first startup (`IsResourceCreationEnabled = true`).

Clients (`Aspire/ino.Client/IAWClientExtensions.cs`) use `Orleans__SiloGateway` env var to find the silo gateway in production via static clustering.

### Health checks

```csharp
// Every container app exposes:
// GET /health  -> general health
// GET /alive   -> liveness (Orleans silo status)
// GET /ready   -> readiness (silo joined cluster + can accept grains)

builder.Services.AddHealthChecks()
    .AddCheck<OrleansHealthCheck>("orleans-silo")
    .AddCheck<RedisHealthCheck>("redis-clustering");
```

## Monitoring checklist

After deployment, verify in the Aspire dashboard or Application Insights:

- [ ] All container apps show `Running` status
- [ ] Orleans silos show `Active` membership status (check `/alive` endpoint)
- [ ] Redis clustering table has entries for all silos
- [ ] Grain state reads/writes succeed (check Table/Blob Storage metrics)
- [ ] Stream messages flowing (check Queue Storage metrics)
- [ ] Application Insights receiving traces from all services
- [ ] Telegram webhook registered and responding
- [ ] MCP server accessible on configured port

## Industry patterns referenced

### From Halo/Xbox Orleans deployment
- Game grains + player grains pattern maps to session neurons + domain neurons
- Azure Service Bus saga triggers map to synapse firing via Orleans Streams
- Azure Table Storage for grain state proven at 1.5B games, 11.6M players
- "Relatively small engineering team" -- same constraint as ino

### From Uber/Temporal
- Durable execution model validates synapse-as-thought (L2) concept
- Saga pattern with compensation for multi-neuron rollback
- 12B workflow executions/mo proves the scale ceiling is far away

### From CNCF landscape
- NATS JetStream (CNCF Incubating) for sub-ms synapse delivery at scale
- OpenTelemetry (CNCF Graduated) already fully integrated
- Dapr actors conceptually similar but Orleans is more capable for ino's needs
- TiKV/Vitess only relevant if self-hosting (not Azure-native path)
