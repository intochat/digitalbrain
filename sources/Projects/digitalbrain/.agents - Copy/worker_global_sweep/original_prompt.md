## 2026-05-23T16:23:19Z

You are the Lead Implementation Worker for the comprehensive InoLang/BrainOS Terminology and Architectural Sweep.
Your task is to execute the planned sweep across all 6 milestones:

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A Forensic Auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Here are the detailed specifications for each milestone:

1. **Milestone 1: Terminology Rename (Seam -> Neuron/Synapse) & Kernel Decoupling**
   - Restructure `BrainOS.Kernel` to act strictly as a service-agnostic Orleans-based runtime orchestrator. Ensure it has no direct references to concrete heavy integration routines (like concrete DB providers, AI clients, or physical OS registries) or compile-time dependencies on concrete SDK packages. Use Dependency Injection or Keyed DI to inject abstract interfaces.
   - Eliminate all references to "Seams" or other complex terms. Everything that processes data is a **Neuron** (virtual actor grain), and all data packages flying between neurons are **Synapses**.
   - Systematically rename classes, interfaces, properties, files, and namespaces. Specifically:
     - `ICallSeamTarget` -> `ICallNeuronTarget`
     - `IPredicateSeamTarget` -> `IPredicateNeuronTarget`
     - `IStreamSeamTarget` -> `IStreamNeuronTarget`
     - `ISeamHost` -> `INeuronHost`
     - `StubSeamHost` -> `StubNeuronHost`
     - `ProductionSeamHost` -> `ProductionNeuronHost`
     - `PredicateSeamBinding` -> `PredicateNeuronBinding`
     - `SeamCatalogInvariantHostedService` -> `NeuronCatalogInvariantHostedService`
     - `SeamCatalogInvariantVerifier` -> `NeuronCatalogInvariantVerifier`
     - Rename files correspondingly (e.g. `PredicateSeamBinding.cs` to `PredicateNeuronBinding.cs`, and the corresponding test files).
     - Update all references in code, usings, tests (including `DigitalBrain.Test`), scenarios, and comments across all projects.

2. **Milestone 2: PostgreSQL Persistence Architecture**
   - **Named/Keyed DB Connections**:
     - Design and implement the PostgreSQL persistence layer to support resolving multiple database connection factories dynamically at runtime using Orleans Keyed DI (e.g. `users_db`, `analytics_db`).
     - Grains must resolve connection factories based on a parameter or dynamically via `serviceProvider.GetKeyedService<IDbContextFactory<TripRadarDbContext>>(databaseId)`.
     - Register two keyed DbContextFactories named `"users_db"` and `"analytics_db"` inside `BrainOSPostgresBridge.cs` (or via fallback SQLite configurations for offline tests).
   - **Automatic Typed Synapse Mapping**:
     - Create a dynamic `SynapseToPostgresMapper` (or a helper utility) that automatically maps arbitrary strongly-typed C# Synapses directly to PostgreSQL tables.
     - Implement auto-schema DDL generation (using reflection to inspect properties and construct `CREATE TABLE IF NOT EXISTS` statements) and auto-upsert serialization (e.g. JSONB columns or flat fields upsert on conflict) to eliminate boilerplate SQL scripting.
     - Implement unit/integration tests to verify that synapse auto-mapping and keyed connection resolution function correctly.

3. **Milestone 3: Neuron Swarm (Collection of Grains) stream-based Architecture**
   - Define a framework for **Neuron Swarms** (a collection of virtual actor grains working together).
   - Orchestrated by `SwarmCoordinatorNeuron` (a virtual actor grain) and communicating asynchronously using **Orleans Memory Streams** (under the `"synapse-streams"` provider) as virtual synapse channels.
   - Worker Neurons register session-based stream subscriptions to run fully in parallel.
   - Implement `SwarmCoordinatorNeuron`, its interfaces, stream subscription setup, and write tests to verify parallel execution.

4. **Milestone 4: Simplify InoLang DSL and Neuron Creator Schema**
   - Simplify InoLang DSL and Neuron Creator configurations: restrict options strictly to inputs (`Synapses in`), outputs (`Synapses out`), and behavioral parameters.
   - Save the audited and updated JSON schema definition under `docs/neuron_creator_schema.json`.
   - Update any compiler or linking steps where necessary to match the simplified DSL design (neurons, synapses, signals).

5. **Milestone 5: Open-Source Substrate Split & architectural_blueprint.md**
   - Clearly document the directory and project boundary separating the core open-source substrate (`DigitalBrain.Core` containing the compiler, parser, state machine, base SDK interfaces) from closed-source proprietary connector packages.
   - Create a clean architectural blueprint document at `docs/architectural_blueprint.md` detailing step-by-step how to write, register, and distribute new Neurons and Synapses.

6. **Milestone 6: Verification & Test Automation**
   - Ensure the entire solution (`DigitalBrain.slnx`) builds cleanly.
   - Run the test suite (`dotnet test`) and verify all 422+ unified tests pass cleanly in under 30 seconds.

Create your working directory under `.agents/worker_global_sweep`.
Write `changes.md` detailing every file modified and the exact steps taken, and `handoff.md` summarizing the outcomes and verification commands.
Once done, send a message to the Project Orchestrator with links to your handoff report and test results.
