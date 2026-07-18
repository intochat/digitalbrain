# Multi-Domain Architecture

## The domain model

ino is extensible by design. Each capability area is a **domain** -- a logical group of neurons that share a context and can be deployed, scaled, and billed independently.

### Default domain: `system`

The system domain exists from day 1 and manages ino itself:

| Neuron | Purpose | Grain type |
|--------|---------|-----------|
| `AgentRegistry` | Neuron discovery (compile-time + runtime) | Singleton |
| `NeuronRegistry` | L1 runtime neuron catalog | Singleton |
| `CortexGrain` | Intent routing via LLM | Per-session |
| `SynapseAgent` (ex CodeOrchestrator) | L2 reasoning-time C# execution | Per-task |
| `SceneGraph` | Durable UI state tree | Per-session or singleton |
| `TokenBudget` | Token usage tracking | Per-user |
| `Approver` | Human-in-the-loop gating | Per-task |
| `ShellAgent` | OS shell execution | Per-task |
| `FileSystemAgent` | File operations | Per-task |
| `RoslynAgent` | C# compilation | Per-task |
| `GitAgent` | Git operations | Per-task |

The system domain is the **only domain that can create new domains** (via L1 self-improvement). It manages the `AgentRegistry` that indexes all neurons across all domains.

### First extension domain: `travel`

Already partially implemented via TripRadar integration at `domains/travel/`:

| Neuron | Purpose |
|--------|---------|
| Route planning neurons | Multi-modal route optimization |
| Booking neurons | Airline/hotel booking |
| Price tracking neurons | Fare monitoring + alerts |

Travel has its own infrastructure (Postgres, Redis, Kafka) wired via `AddTravelDomain()` in the AppHost.

## Orleans heterogeneous silos

Orleans natively supports different grain types on different silos within the same cluster. This is the mechanism for domain isolation:

```
Cluster "ino" (single Orleans cluster)
  ├── Silo Group "system" (2 replicas)
  │     Hosts: AgentRegistry, NeuronRegistry, CortexGrain, SynapseAgent, SceneGraph, ...
  │     Metadata: { "domain": "system" }
  │
  ├── Silo Group "travel" (3 replicas)  
  │     Hosts: TripRadar neurons, booking grains, price tracking
  │     Metadata: { "domain": "travel" }
  │
  └── Silo Group "compute" (2 replicas)
        Hosts: RoslynAgent, DotNetAgent, ShellAgent (CPU-heavy)
        Metadata: { "domain": "system", "tier": "compute" }
```

### Silo metadata configuration (Orleans 9.2+)

```csharp
// In silo host startup
builder.UseOrleans(silo =>
{
    silo.UseSiloMetadata(new Dictionary<string, string>
    {
        ["domain"] = "travel",
        ["tier"] = "standard"
    });
});
```

### Placement filtering

```csharp
// Grain-level: only activate on silos with matching domain
[RequiredMatchSiloMetadata("domain")]
[ResourceOptimizedPlacement]
public class TripRadarNeuron : Grain, ITripRadar { }
```

All grains in one cluster can make direct grain-to-grain calls across domains -- zero serialization overhead, no service mesh needed. Orleans handles grain directory, activation, and failover cluster-wide.

### NeuronGrain host -- the universal type

The key design: `NeuronGrain` (the universal runtime neuron host) exists on **ALL** silos. Each neuron's `AgentRecord.Domain` field drives placement filtering:

```csharp
// Creating a travel neuron routes it to travel silos automatically
await registry.CreateAsync(new NeuronBlueprint
{
    Name = "flight-tracker",
    Domain = "travel",  // placement filter matches silo metadata
    SynapseSchema = "interface IFlightTracker { Task<FlightStatus> Track(string flightNumber); }"
});
```

This is L1-compatible -- creating a new neuron with `domain: "travel"` places it on travel silos without any silo restart.

## Aspire AppHost wiring

```csharp
// AppHost.cs
var orleans = builder.AddOrleans("ino")
    .WithClustering(redis)
    .WithGrainStorage("Default", tableStorage);

// System domain silos
builder.AddProject<Projects.SystemSilo>("system-silo")
    .WithReference(orleans.AsServer())
    .WithReplicas(2)
    .WithEnvironment("Orleans__SiloMetadata__domain", "system");

// Travel domain silos  
builder.AddProject<Projects.TravelSilo>("travel-silo")
    .WithReference(orleans.AsServer())
    .WithReference(postgres)
    .WithReference(kafka)
    .WithReplicas(3)
    .WithEnvironment("Orleans__SiloMetadata__domain", "travel");

// Clients (Telegram, MCP) connect to entire cluster
builder.AddProject<Projects.Telegram>("telegram")
    .WithReference(orleans.AsClient());
```

## Cross-domain communication

Within the single cluster, cross-domain grain calls are direct:

```csharp
// Travel neuron calls system registry -- direct grain call, no hop
var registry = GrainFactory.GetGrain<IAgentRegistry>("global");
var results = await registry.HybridSearchAsync("currency converter", embedding);
```

No service mesh, no Dapr sidecar, no HTTP proxy. The Orleans runtime resolves the grain to whatever silo hosts it and makes a direct binary call.

## When to split clusters

Split into separate Orleans clusters only when:
1. **Independent failure domains** -- travel outage must not affect system (regulatory requirement)
2. **Geo-distribution** -- travel neurons in EU, system neurons in US
3. **Different security boundaries** -- domain handles PII that must not cross cluster boundaries

For cross-cluster communication, use Orleans multi-cluster or Dapr service invocation as a bridge. This adds ~1-5ms per cross-cluster call.

## Domain lifecycle

```
1. System domain creates new domain via L1:
   - NeuronRegistry.CreateAsync(blueprint with domain="finance")
   - AgentRegistry.RegisterAsync(record with Domain="finance")

2. Aspire AppHost adds new silo group:
   - New project with WithEnvironment("Orleans__SiloMetadata__domain", "finance")
   - This is an L3 change (requires AppHost restart)

3. Neurons auto-place on matching silos:
   - NeuronGrain activation checks AgentRecord.Domain
   - Placement filter routes to silos with matching metadata

4. Domain scales independently:
   - Add more replicas of finance-silo container
   - Domain-specific infrastructure (DB, cache) scales separately
```

## Scaling domains independently

| Domain | Expected scale | Silo count | Infrastructure |
|--------|---------------|------------|---------------|
| system | 1K-10K neurons (fixed + L1 created) | 2-5 | Redis, Table Storage |
| travel | 100K-1M neurons (routes, bookings, prices) | 5-20 | Postgres, Redis, Kafka |
| finance | 10K-100K neurons (accounts, transactions) | 3-10 | Cosmos DB (multi-region) |
| creative | 1K-10K neurons (design, writing) | 2-5 | Blob Storage (large assets) |

Each domain pays for its own infrastructure. The system domain is always present and always funded.
