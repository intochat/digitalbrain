# Terminology and Architectural Sweep Handoff Report

This handoff report summarizes the comprehensive investigation of the DigitalBrain codebase, documenting all Seam targets, dependency injection configuration, PostgreSQL database integration, Orleans memory streams, InoLang compiler components, and solution project layout.

---

## 1. Observation

Direct observations and file facts gathered from the codebase:

### 1.1 Seam Terminology & Renaming Targets
1. **Core Seam interfaces** found in `sdk/DigitalBrain.SDK.Contracts/`:
   - `sdk/DigitalBrain.SDK.Contracts/Call/ICallSeamTarget.cs`: `public interface ICallSeamTarget : IGrainWithGuidKey`
   - `sdk/DigitalBrain.SDK.Contracts/Predicate/IPredicateSeamTarget.cs`: `public interface IPredicateSeamTarget : IGrainWithStringKey`
   - `sdk/DigitalBrain.SDK.Contracts/Stream/IStreamSeamTarget.cs`: `public interface IStreamSeamTarget : IGrainWithGuidKey`
2. **Interpreter Seam Hosts** located:
   - `inolang/DigitalBrain.InoLang/Runtime/ISeamHost.cs`: `public interface ISeamHost`
   - `inolang/DigitalBrain.InoLang/Testing/StubSeamHost.cs`: `public class StubSeamHost : ISeamHost`
   - `kernel/BrainOS.Kernel/Runtime/ProductionSeamHost.cs`: `public sealed class ProductionSeamHost : ISeamHost`
3. **Key Binding Classes & Verifiers**:
   - `sdk/DigitalBrain.SDK.Contracts/Predicate/PredicateSeamBinding.cs`:
     ```csharp
     public sealed class PredicateSeamBinding(
         string seamName,
         string predicateName,
         string predicateValue)
     ```
   - `kernel/BrainOS.Kernel/Runtime/SeamCatalogInvariantHostedService.cs`:
     ```csharp
     public sealed class SeamCatalogInvariantHostedService(
         IGrainFactory grains,
         SeamCatalogInvariantVerifier verifier,
         ILogger<SeamCatalogInvariantHostedService> logger)
         : IHostedService
     ```
   - `kernel/BrainOS.Kernel/Runtime/SeamCatalogInvariantVerifier.cs`:
     ```csharp
     public sealed class SeamCatalogInvariantVerifier(ILogger<SeamCatalogInvariantVerifier> logger)
     ```

### 1.2 Kernel Integration & DI Setup
- `kernel/BrainOS.Kernel/Program.cs` registers core services via:
  ```csharp
  builder.AddBrainOSDomain();
  ```
  which invokes assembly scanning to populate the `IContractCatalog` and boot up `InterpretedNeuronRegistry`.
- Lifecycle bootstrap is managed by `KernelOSBootstrapper` (implements `IStartupTask`) and triggers `KernelOSNeuron` to scan workspace folders, parse `.ino` files, compile dynamic steps/implementations, and register active neurons.

### 1.3 PostgreSQL Persistence & Synapse Entities
1. **DB Configuration**: Configured in `samples/BrainOS.Domains.Travel/BrainOS.Domains.Travel/TripRadar/Persistence/` via `TripRadarDbContext` and registered in `BrainOSPostgresBridge.cs` using:
   ```csharp
   builder.Services.AddPooledDbContextFactory<TripRadarDbContext>(options =>
       options.UseNpgsql(connectionString));
   ```
2. **PostgreSQL Synapse Entities**: Defined in `sdk/DigitalBrain.SDK.Sqlite/DigitalBrain.SDK.Sqlite.Contracts/PostgresNeuronContracts.cs` within namespace `BrainOS.Domains.Data.Postgres.Contracts`:
   - `RunMigrationsRequest` (triggers migration run)
   - `MigrationsApplied` (successful outcome)
   - `MigrationsFailed` (error outcome, contains `ErrorMessage` string)
   - `PgPingRequest` (database availability check)
   - `PgPong` (successful reachability)
   - `PgUnavailable` (unreachable database, containing `ErrorMessage`)

### 1.4 Orleans Stream Configuration
- Mapped in `kernel/BrainOS.Core.Hosting/Streams/StreamProviderConfig.cs` and registered in `AddBrainOSSiloExtensions.cs`:
  - Stream provider registered under `StreamProviderConfig.SynapseProviderName` (maps to `"synapse-streams"`).
  - Wired via Orleans Memory Streams (`AddMemoryStreams<DefaultMemoryMessageBodySerializer>`).
  - Durable value backplane is `AddStateMachineStorage()` which registers a singleton `IStateMachineStorageProvider` backed by `VolatileStateMachineStorageProvider` for local testing/dev environments.

### 1.5 InoLang DSL and Neuron Creator
- Lexer, Parser, and AST definitions exist under `inolang/DigitalBrain.InoLang/`.
- Dynamic code generation (parallel C# triplet path) uses `PlanNeuronResponse` with the following schema:
  - `FeatureText` (Gherkin/InoLang code)
  - `StepsCode` (C# scenario steps)
  - `ImplCode` (C# grain logic)
  - `InvocationSynapseType` / `InvocationPayloadJson`
- The InoLang-first path utilizes `AuthorInoNeuronRequest` containing:
  - `Intent` (natural language request)
  - `SuggestedFqn`
  - `LlmModelKey`
  - `MaxAttempts`

### 1.6 Solution Projects Classification
- Solution projects (`DigitalBrain.slnx`) mapped into **DigitalBrain.Core Substrate** (kernel, hosting, boot, InoLang, base SDK contracts) vs **Proprietary Connector and Domain Modules** (AI, Grok, Sqlite, Google, Canvas UI, and sample Travel/Onboarding applications).

---

## 2. Logic Chain

1. **Terminology Mapping**: Grains are bound via grain interfaces implementing `ICallSeamTarget`, `IPredicateSeamTarget`, etc., and audited at Silo start using `SeamCatalogInvariantVerifier`. Changing `Seam` to `Neuron`/`Synapse` terminology requires renaming these classes, properties (e.g. `SeamName`), and attributes across `DigitalBrain.Core` and the verification hosted service.
2. **Decoupling Dependency Setup**: The kernel is currently coupled with physical filesystem directory scanning and concrete EF Core DB migration executors. By introducing abstract interfaces (`IFileSystemScanner`, `IDatabaseMigrationExecutor`), these heavy routines can be cleanly injected or mocked, allowing the kernel to remain highly portable and decoupled.
3. **Keyed DI for PostgreSQL**: Because Orleans 10 natively supports `[FromKeyedServices]`, we can register multiple database connection factories dynamically keyed under unique identifiers (e.g. `users_db`, `analytics_db`). Grains can then selectively request database contexts at runtime, allowing multitenancy and dynamic resolution without changing grain schemas.
4. **Stream Routing Mechanics**: Orleans Memory Streams serve as the virtual synapse channels. Grains register interest using `[ImplicitStreamSubscription(NeuronType)]`, and synapses are fanned out to matching stream channels by `FireSynapseAsync()`, forming a reactive event-driven mesh.
5. **Schema Simplification**: Merging C# dynamic planning contracts and `AuthorInoNeuronRequest` properties into a unified, consolidated JSON schema will reduce dynamic authoring overhead, simplify frontends (like Flutter), and simplify InoLang compiler link steps by allowing inline FQNs instead of extensive header imports.

---

## 3. Caveats

- **Monetization & Metering**: Throttling rules, license audits, and actual payment integrations (e.g., Stripe) were not investigated in-depth as they lie in downstream monetization milestones and do not affect the terminology or core DI sweep.
- **Orleans Redis Clustering**: Production clustering with Redis stream providers is documented but bypassed in local single-silo runs using the `UseLocalhostClustering()` fallback when Redis connection strings are absent. We assume this fallback remains unaltered.

---

## 4. Conclusion

- **Seam Terminology**: The target renaming path is clear. It requires renaming 6 core contract files, the `SeamCatalogInvariantVerifier` lifecycle logic, and updating namespace usages from `.Seams` to `.Neurons` / `.Synapses`.
- **Database Dynamic Resolution**: Fully achievable using Orleans Keyed DI (`[FromKeyedServices("db_name")]`), registering keyed db factories centrally.
- **Stream Infrastructure**: Fully documented and stable on top of `"synapse-streams"` memory streams.
- **InoLang & Creator**: The schema can be significantly simplified by unifying C# planning triplets and InoLang requests.
- **Baseline Integrity**: The solution builds cleanly on .NET 11 and Orleans 10.1.0, ensuring a stable baseline.

---

## 5. Verification Method

To verify the codebase baseline and test results independently, run the following:

1. **Build the Solution**:
   `dotnet build` in `e:\digitalbrain` should report 0 errors and successfully compile all targets.
2. **Execute Tests**:
   Run `dotnet test` in `e:\digitalbrain` to execute the full unit and integration test suites. We have verified the entire suite: **422 tests passed, 0 failed, 0 skipped**.

3. **Inspect Key Analysis Output**:
   Read `e:\digitalbrain\.agents\teamwork_preview_explorer_sweep_1\analysis.md` to review granular details of paths, constructors, and project classification mapping.
