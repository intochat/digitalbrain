# Architecture and Terminology Sweep Analysis

This report documents the findings from the read-only architectural investigation and terminology sweep of the DigitalBrain codebase. The goal is to provide a complete and structured map of all targets, configurations, and structures to support subsequent renaming, decoupling, database dynamic resolution, streaming, and DSL simplification.

---

## 1. Seam Terminology & Renaming Targets

Our sweep mapped every interface, class, method, property, and namespace containing the term `Seam` or `Seams`. This terminology is planned to be modernized to `Neuron`/`Synapse` concepts.

### 1.1 Core Contracts and Hosts

| Target Name | File Path | Type | Purpose |
|---|---|---|---|
| `ICallSeamTarget` | `sdk/DigitalBrain.SDK.Contracts/Call/ICallSeamTarget.cs` | Interface | Orleans grain interface that serves as a target for Call seam invocations. |
| `IPredicateSeamTarget` | `sdk/DigitalBrain.SDK.Contracts/Predicate/IPredicateSeamTarget.cs` | Interface | Orleans grain interface serving as a target for Predicate-based (where) seam routing. |
| `IStreamSeamTarget` | `sdk/DigitalBrain.SDK.Contracts/Stream/IStreamSeamTarget.cs` | Interface | Orleans grain interface serving as a target for stream-based virtual synapse channels. |
| `ISeamHost` | `inolang/DigitalBrain.InoLang/Runtime/ISeamHost.cs` | Interface | Runtime interface for evaluating or invoking seam targets in the InoLang interpreter. |
| `StubSeamHost` | `inolang/DigitalBrain.InoLang/Testing/StubSeamHost.cs` | Class | In-memory mock/stub of `ISeamHost` used for unit testing InoLang scripts without Orleans. |
| `ProductionSeamHost` | `kernel/BrainOS.Kernel/Runtime/ProductionSeamHost.cs` | Class | Silo-side production implementation of `ISeamHost`, routing InoLang invocations to grains. |

### 1.2 Key Restructuring Targets

#### `PredicateSeamBinding`
- **Path**: `sdk/DigitalBrain.SDK.Contracts/Predicate/PredicateSeamBinding.cs`
- **Constructor**:
  ```csharp
  public sealed class PredicateSeamBinding(
      string seamName,
      string predicateName,
      string predicateValue)
  ```
- **Usage**: Maps a logic seam to a specific predicate-matching target (e.g. `topic-of` is `"travel"`). It is registered in the contract catalog and resolved at runtime during grain dispatch.

#### `SeamCatalogInvariantHostedService`
- **Path**: `kernel/BrainOS.Kernel/Runtime/SeamCatalogInvariantHostedService.cs`
- **Constructor**:
  ```csharp
  public sealed class SeamCatalogInvariantHostedService(
      IGrainFactory grains,
      SeamCatalogInvariantVerifier verifier,
      ILogger<SeamCatalogInvariantHostedService> logger)
      : IHostedService
  ```
- **Usage**: Standard `IHostedService` executed during Silo startup. It invokes the `SeamCatalogInvariantVerifier` to ensure the registry and actual loaded assemblies do not drift in terms of grain contracts.

#### `SeamCatalogInvariantVerifier`
- **Path**: `kernel/BrainOS.Kernel/Runtime/SeamCatalogInvariantVerifier.cs`
- **Constructor**:
  ```csharp
  public sealed class SeamCatalogInvariantVerifier(ILogger<SeamCatalogInvariantVerifier> logger)
  ```
- **Usage**: Walks all loaded assemblies, scans for grain interfaces carrying `[GrainType]` attributes, compares them against the loaded `IContractCatalog` to ensure that every registered Seam grain matches a loaded `ContractKind.Neuron` entry, and throws a startup invariant failure if drifts or duplicate FQNs are found.

---

## 2. Kernel Integration and DI Setup

`BrainOS.Kernel` operates as the runtime substrate. An audit of `kernel/BrainOS.Kernel/Program.cs` and core services identifies several areas of tight coupling and opportunities for abstract interfaces.

### 2.1 Bootstrapping Lifecycle and Heavy Routines
The boot sequence is orchestrated via:
- `KernelOSBootstrapper` (implements `IStartupTask`): Called by the Orleans silo lifecycle during boot.
- `KernelOSNeuron`: A virtual actor that triggers directory scanning, loads dynamically compiled `.ino` descriptors, starts the `InterpretedNeuronRegistry`, and boots up the gateway listener.

Heavy operations currently performed in-process:
1. **Dynamic Compilation (Roslyn & InoLang)**: Done in `RoslynCompiler` and `InoCompiler` within the runtime worker.
2. **FileSystem Scanning**: The `KernelOSNeuron` directly reads files from local workspace paths.
3. **Database Migrations**: `PostgresMigrationNeuron` directly executes Entity Framework Core migration runs against PostgreSQL at boot.

### 2.2 Decoupling Opportunities
To cleanly separate the core hosting engine from specific runtime dependencies, we recommend introducing the following abstractions:
- `IFileSystemScanner`: Decouple file scanning from hardcoded physical directory paths.
- `IDatabaseMigrationExecutor`: Abstract migration executions so the kernel doesn't depend on EF Core or Npgsql.
- `ICompiledContractRepository`: Move compilation of scripts and schemas out of the silo's main thread and backing grains into an asynchronous repository pattern.

---

## 3. PostgreSQL Integration & Synapse Entities

### 3.1 Existing PostgreSQL Configuration
PostgreSQL persistence is primarily configured via Entity Framework Core inside `samples/BrainOS.Domains.Travel/BrainOS.Domains.Travel/TripRadar/Persistence/`:
- **TripRadarDbContext**: Defines table schemas for travel domain persistence.
- **TripRadarPostgresBridge** / **BrainOSPostgresBridge**: Wires DB factories.
  ```csharp
  builder.Services.AddPooledDbContextFactory<TripRadarDbContext>(options =>
      options.UseNpgsql(connectionString));
  ```

### 3.2 Dynamic Keyed DB Resolution via Orleans DI
In Orleans 10, grains utilize Keyed DI (`[FromKeyedServices]`) extensively. To support dynamic database connections (e.g., `users_db`, `analytics_db`), we can register multiple DbContextFactories or raw connection builders as keyed services:
```csharp
// In DI Setup:
builder.Services.AddKeyedPooledDbContextFactory<TripRadarDbContext>("users_db", (sp, opts) => 
    opts.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("users_db")));

builder.Services.AddKeyedPooledDbContextFactory<TripRadarDbContext>("analytics_db", (sp, opts) => 
    opts.UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("analytics_db")));
```
Then, inside target Neurons, inject the factory dynamically using:
```csharp
public sealed class DataWarehouseNeuron(
    [FromKeyedServices("analytics_db")] IDbContextFactory<TripRadarDbContext> dbFactory,
    ...) : Neuron(...)
```

### 3.3 Existing Synapse Contracts and Schemas
The PostgreSQL contracts are defined in `sdk/DigitalBrain.SDK.Sqlite/DigitalBrain.SDK.Sqlite.Contracts/PostgresNeuronContracts.cs` under the namespace `BrainOS.Domains.Data.Postgres.Contracts`:
- `RunMigrationsRequest`: Triggers a database migration run.
- `MigrationsApplied`: Fired when migrations successfully complete.
- `MigrationsFailed`: Fired when a migration run encounters an error.
- `PgPingRequest`: Triggers a connection check.
- `PgPong`: Confirms database is reachable.
- `PgUnavailable`: Fired when CanConnect returns false or throws an error.

The persistence mappings in `TripRadarDbContext` include:
- `UserSubscription` (`UserId` PK, `Tier`, `PriceId`, `SubscriptionId`, `IsActive`, `ExpiresAt`)
- `TripVault` (`Id` PK, `Name`, `OwnerUserId`)
- `Trip` (`Id` PK, `Destination`, `OwnerUserId`, `ItineraryJson`, `CreatedAt`)
- `SearchHistory` (`Id` PK, `UserId`, `Query`, `Timestamp`)
- `Feedback` (`Id` PK, `UserId`, `Category`, `Message`, `Timestamp`)

---

## 4. Orleans Stream Providers and Memory Streams

Orleans stream configurations are defined in `kernel/BrainOS.Core.Hosting/Streams/StreamProviderConfig.cs` and `AddBrainOSSiloExtensions.cs`:
- **Provider Name**: `StreamProviderConfig.SynapseProviderName` (maps to `"synapse-streams"`).
- **Default Provider**: Orleans Memory Streams (`AddMemoryStreams<DefaultMemoryMessageBodySerializer>`).
- **PubSub Store**: Memory grain storage named `"PubSubStore"`.

### 4.1 Memory Streams as Synapse Channels
Every neuron in BrainOS subscribes to a memory stream using:
`[ImplicitStreamSubscription(NeuronType)]`
When a synapse is fired via `FireSynapseAsync(synapse)`, the host resolves the stream for `synapse.ReceiverNeuronType` and publishes the event to it. Grains act as virtual handlers listening on their respective stream channels, offering a resilient, decoupled messaging fabric.

---

## 5. InoLang DSL and Neuron Creator Schema

The InoLang engine compiles structured English specifications into deterministic execution gates:
- **Lexer/Parser**: Line-oriented, indentation-based grammar parsing fields, usings, scenarios, and behaviors.
- **Linker**: Maps using statements with sigils (`#` Inbound, `!` Signal, `$` Call, `~` Resource) to contract FQNs from the catalog.

### 5.1 Neuron Creator Input/Output Schema
The dynamic authoring loop is orchestrated by `CreatorNeuron` and `InoCreatorNeuron`.
- The Parallel C# path uses `PlanNeuronResponse`:
  - `FeatureText` (Gherkin/InoLang spec)
  - `StepsCode` (C# Scenario steps)
  - `ImplCode` (C# Grain logic)
  - `InvocationPayloadJson` / `InvocationSynapseType`
- The InoLang-first path uses `AuthorInoNeuronRequest`:
  - `Intent` (Natural language description)
  - `SuggestedFqn`
  - `LlmModelKey`
  - `MaxAttempts`

### 5.2 Schema and DSL Simplification
To streamline inputs/outputs:
1. **Unify Schemas**: Merge C# triplet specification formats and InoLang specs into a single JSON schema.
2. **Compact InoLang**: Enable inline FQNs in `onboarding.ino` rather than requiring extensive alias headers.
3. **Auto-Generate Scenarios**: Generate scenario files directly from structural ports rather than requiring developers to write verbose Given-When-Then blocks for simple CRUD operations.

---

## 6. Core vs Connector Classification

The projects in `DigitalBrain.slnx` are classified as follows:

```
DigitalBrain
├── DigitalBrain.Core (Substrate)
│   ├── kernel/BrainOS.Core (Contracts and Base Actor Types)
│   ├── kernel/BrainOS.Core.SourceGen (Neuron Source Generation)
│   ├── kernel/BrainOS.Core.Hosting (Silo Setup and Middleware)
│   ├── kernel/BrainOS.Boot (Startup Tasks)
│   ├── kernel/BrainOS.Kernel (Runtime Engine & Core Neurons)
│   ├── kernel/BrainOS.Kernel.Contracts (Kernel Interfaces)
│   ├── kernel/BrainOS.NeuronTesting (Testing Framework Utilities)
│   ├── kernel/BrainOS.Domains.Dynamic (Dynamic Neuron Host)
│   ├── inolang/DigitalBrain.InoLang (DSL Parser & Linker)
│   └── sdk/DigitalBrain.SDK (Base Developer SDK)
│
└── Proprietary Connector & Domain Modules (Integrations)
    ├── sdk/DigitalBrain.SDK.Ai (LLM Interfaces and Clients)
    ├── sdk/DigitalBrain.SDK.Grok (xAI Grok Connector)
    ├── sdk/DigitalBrain.SDK.Sqlite (Filesystem & SQLite Access)
    ├── sdk/DigitalBrain.SDK.Google (Gmail, YouTube, OAuth Integrations)
    ├── sdk/DigitalBrain.SDK.Identity (Authentication Store)
    ├── sdk/DigitalBrain.SDK.Canvas (Dynamic RFW Canvas Backend)
    ├── sdk/DigitalBrain.SDK.Visuals (UI Visualization Layouts)
    ├── sdk/DigitalBrain.SDK.Mcp (Model Context Protocol Integration)
    ├── sdk/DigitalBrain.SDK.Windows (Windows-specific OS controls)
    ├── samples/BrainOS.Domains.Travel (TripRadar Reference App)
    ├── samples/BrainOS.Domains.Engineering (Dev Tools Integration)
    └── samples/BrainOS.Domains.Onboarding (Interpreted Onboarding Flow)
```

---

## 7. Verification Results

We verified the build and test baseline stability of the DigitalBrain solution:
- **Build Status**: Succeeded with 0 Warnings and 0 Errors (`dotnet build`).
- **Test Baseline**: Succeeded with **422 passed, 0 failed, 0 skipped** (`dotnet test --no-build`).
  - `BrainOS.Boot.Tests`: Passed (495ms)
  - `DigitalBrain.InoLang.TestRunner.Tests`: Passed (574ms)
  - `DigitalBrain.InoLang.Tests`: Passed (577ms)
  - `BrainOS.Core.Tests`: Passed (1.5s)
  - `BrainOS.Core.Hosting.Tests`: Passed (2.5s)
  - `BrainOS.Kernel.Tests`: Passed (11.3s)
  - `DigitalBrain.Test` (Integration Tests): Passed (27.2s)

