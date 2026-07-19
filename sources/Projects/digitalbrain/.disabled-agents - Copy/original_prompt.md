## 2026-05-23T01:39:41+02:00

DigitalBrain is transitioning to production-ready architecture. The project aims to consolidate the SDK, enhance the InoLang parser/editor with Roslyn, implement robust in-memory scripting and source generators for tests, and prepare Orleans/Kernel structures for personal private deployments.

Working directory: e:/digitalbrain

## Requirements

### R1. Aspire Production Readiness & SDK Unification
- Ensure the .NET Aspire configuration is ready for production deployments (proper resource configuration, scaling, and production environments).
- Unite all standalone SDK projects (`DigitalBrain.SDK.*`) under a single C# project `DigitalBrain.SDK`.
- The unified SDK project must support neuron-synapse abstraction levels, enabling connector neurons to consume/emit other neurons and synapses seamlessly.

### R2. Roslyn Runtime Scripting & Source Generators
- Implement in-memory compilation, validation, and execution of dynamic scripts at runtime using Microsoft.CodeAnalysis (Roslyn).
- Develop a Roslyn source generator that reads `.ino` files and generates C# test steps.
- The generator must produce mock LLM neuron stubs that generate deterministic fake answers during scenario runs, enabling offline testing.
- Implement test-driven neuron generation: the source generator produces a test from an `.ino` file, then generates the C# neuron code that must satisfy and pass that test.

### R3. InoLang Editor & Syntax Highlighting
- Enhance the Flutter neuron editor to support inline FQNs (like `Google.Auth.New` or `DigitalBrain.SDK.*`) referenced in plain English.
- Implement real-time syntax highlighting and inline signature hover cards driven by the catalog (`.ino-catalog.json`).
- In the neuron editor, display the list of synapses a given neuron handles, its associated signals, and the ability to build it.

### R4. Private Orleans Cluster & Abstractions
- Design a private Orleans cluster and private kernel structure suitable for a single-user personal deployment.
- Define kernel abstractions for `User`, `Settings`, and `Secret`/`Identity`, clearly separating sensitive credentials (secrets stored via encrypted vault) from standard configurations (settings).

## Acceptance Criteria

### Production & SDK Unification
- [ ] A single `DigitalBrain.SDK` assembly/project exists and builds cleanly, referencing the required contract definitions.
- [ ] All previous SDK capabilities (Ai, Aspire, Google, Sqlite, Windows, etc.) are unified inside the main SDK or referenced cleanly.
- [ ] Aspire AppHost runs correctly in a production configuration without orphan processes or unmanaged resources.

### Roslyn Scripting & Testing
- [ ] Tests can be run dynamically against in-memory compiled C# code via Roslyn.
- [ ] Source generator successfully produces C# test steps from `.ino` files with mock LLM stubs.
- [ ] Test-driven loop automatically executes generated neuron code against its scenario and verifies passing status.

### User Experience & Editor
- [ ] The Flutter editor displays syntax highlighting for inline FQNs based on kind (neuron, synapse, signal) from the catalog.
- [ ] The editor renders a visual hover or list of synapses and signals for a selected neuron.

## 2026-05-23T00:10:56Z

Hello DigitalBrain Production & Scripting Team,

The user has reviewed and approved the Milestone 1 Victory Audit! Exceptional work consolidating the SDK and verifying all 434 tests in the suite.

Please commit all currently unstaged Milestone 1 work (use git commit message: "feat(sdk): consolidate SDK and update Aspire AppHost to production configurations") and immediately continue executing the roadmap:
- Progress Milestone 2 (Roslyn Scripting & Mock Stubs) to completion.
- Proceed to Milestone 3 (Roslyn Source Generator & Test-Driven Loop).

Maintain full testing coverage, compile-checks, and liveness reporting. Pushing forward!

## 2026-05-23T16:08:59Z

An architectural planning and implementation sweep to simplify InoLang/Ino Editor boundaries, decouple the "god object" kernel, and prepare the core substrate for open-sourcing.

Working directory: e:\digitalbrain
Integrity mode: benchmark

## Requirements

### R1. Decouple the Kernel (De-godding)
- Review and restructure `BrainOS.Kernel` to ensure it acts strictly as an Orleans-based runtime orchestrator.
- Ensure all heavy integration routines (database operations, AI prompting, OS runtime hooks) are fully decoupled into the SDK layer, leaving the kernel completely service-agnostic.

### R2. Refine InoLang & Ino Editor Boundaries
- Simplify the InoLang DSL design to focus exclusively on declarative Neuron declarations, Synapse subscriptions (`on synapse`), and Signal emissions (`emit`).
- Audit the Neuron Creator schema to ensure users only have to specify inputs (Synapses in), outputs (Synapses out), and core behavioral concerns. (Parser-level enforcement for the DSL itself is deferred to a future iteration).

### R3. Open-Source Architectural Split
- Define a clear directory and project boundary separating the core open-source substrate (`DigitalBrain.Core`, including the InoLang compiler, visual parser, and Orleans state machine) from the closed-source proprietary connector modules.
- Create a clean architectural blueprint documenting how third-party developers can build and register new Neurons and Synapses using the open-source SDK.

### R4. Verification & Solution Integrity
- Ensure all 422+ unified tests (including `DigitalBrain.Test`) build cleanly and run green in under 30 seconds using `dotnet test` on the unified `DigitalBrain.slnx` solution.

## Acceptance Criteria

### Kernel & SDK Decoupling
- [ ] `BrainOS.Kernel` contains no references to heavy integration routines (like direct DB providers, concrete AI models/clients, or physical OS hook registries), but instead references abstract interfaces or delegates these to the SDK layer.
- [ ] Direct database operations, AI prompting, and OS hook setups reside in the SDK or extension packages.

### InoLang & Editor boundaries
- [ ] The Neuron Creator schema has been audited and updated to require only input/output synapses and core parameters, with the updated schema definition saved and documented.
- [ ] A design specification or schema update is documented outlining the simplified InoLang DSL focus (declarative neurons, synapses, signals).

### Substrate Split & Blueprint
- [ ] A directory layout separates `DigitalBrain.Core` projects (open-source) from proprietary connector packages/modules.
- [ ] An architectural markdown document `docs/architectural_blueprint.md` exists, detailing step-by-step how to write, register, and distribute new Neurons and Synapses using the open-source SDK.

### Test Automation
- [ ] Running `dotnet test` on `DigitalBrain.slnx` executes all tests (422+) successfully in less than 30 seconds.

## 2026-05-23T18:18:04Z

An architectural planning and implementation sweep to simplify InoLang/Ino Editor boundaries, decouple the "god object" kernel, and prepare the core substrate for open-sourcing, using best Orleans and Aspire practices.

Working directory: e:\digitalbrain
Integrity mode: benchmark

## Requirements

### R1. Decouple the Kernel & Simplify Terminology
- Restructure `BrainOS.Kernel` to ensure it acts strictly as an Orleans-based runtime orchestrator.
- **Simplify Terminology**: Eliminate all references to "Seams" or other complex terms. Everything that processes data is a **Neuron** (virtual actor grain), and all data packages flying between neurons are **Synapses**. Rename classes like `PredicateSeamBinding` or `SeamCatalogInvariantHostedService` accordingly.
- Ensure all heavy integration routines (database operations, AI prompting, OS runtime hooks) are fully decoupled into abstract interfaces, leaving the kernel completely service-agnostic.

### R2. PostgreSQL Multi-Database Scaling & Typed Synapse Table Mapping
- **Named/Keyed DB Connections**: Design the PostgreSQL persistence architecture to support multiple separate PostgreSQL databases (e.g. `users_db`, `analytics_db`) dynamically resolved via Orleans Keyed DI at runtime.
- **Automatic Typed Synapse Mapping**: Create a dynamic `SynapseToPostgresMapper` (or design specification) that automatically maps strongly-typed C# Synapses directly to Postgres SQL tables, performing auto-schema DDL generation and auto-upsert serialization to eliminate boilerplate database schema scripting.

### R3. Neuron Swarm (Collection of Grains) Architecture
- Define the framework for **Neuron Swarms** (a collection of virtual actor grains working together).
- Orchestrated by a `SwarmCoordinatorNeuron` and communicates asynchronously using **Orleans Memory Streams** as virtual synapse channels.
- Worker Neurons register session-based stream subscriptions to run fully in parallel.

### R4. Refine InoLang & Neuron Creator Schema
- Simplify the InoLang DSL design to focus exclusively on declarative Neuron declarations, Synapse subscriptions (`on synapse`), and Signal emissions (`emit`).
- Audit and define the Neuron Creator JSON schema to ensure users only specify inputs (`Synapses in`), outputs (`Synapses out`), and behavioral parameters.

### R5. Open-Source Architectural Split & Blueprint
- Define a clear directory and project boundary separating the core open-source substrate (`DigitalBrain.Core`, including the InoLang compiler, visual parser, and Orleans state machine) from the closed-source proprietary connector modules.
- Create a clean architectural blueprint (`docs/architectural_blueprint.md`) documenting how third-party developers can build, register, and distribute custom Neurons and Synapses.

### R6. Verification & Solution Integrity
- Ensure all 422+ unified tests (including `DigitalBrain.Test`) build cleanly and run green in under 30 seconds using `dotnet test` on the unified `DigitalBrain.slnx` solution.

## Acceptance Criteria

### Kernel & SDK Decoupling
- [ ] `BrainOS.Kernel` contains no compile-time references to concrete SDK packages (`DigitalBrain.SDK`, etc.) or proprietary domain projects.
- [ ] Direct database operations, AI prompting, and OS hook setups reside in the SDK or extension packages and are injected into Grains via standard or keyed Dependency Injection.
- [ ] References to "Seam/Seams" are refactored to use standard "Neuron/Synapse" terminology.

### PostgreSQL & Synapse Mapping
- [ ] The Postgres integration supports resolving named databases dynamically using Orleans Keyed DI based on `DatabaseId` in synapse requests.
- [ ] A design blueprint or mapper utility is documented detailing dynamic auto-schema migration and serialization of arbitrary typed Synapse objects directly into PostgreSQL tables.

### Neuron Swarms & Boundaries
- [ ] The Neuron Swarm communication architecture using Orleans streams and coordinators is designed and documented.
- [ ] The Neuron Creator schema is audited and saved to restrict configurations to inputs, outputs, and parameters.
- [ ] A design specification or schema update is documented outlining the simplified InoLang DSL focus (neurons, synapses, signals).

### Substrate Split & Blueprint
- [ ] A directory layout separates `DigitalBrain.Core` projects (open-source) from proprietary connector packages/modules.
- [ ] An architectural markdown document `docs/architectural_blueprint.md` exists, detailing step-by-step how to write, register, and distribute new Neurons and Synapses using the open-source SDK.

### Test Automation
- [ ] Running `dotnet test` on `DigitalBrain.slnx` executes all tests (422+) successfully in less than 30 seconds.

## 2026-05-24T05:45:33+02:00

Consolidate and clean up all project documentation across the repository to match the newly consolidated SDK directory layout, refined namespaces (Stripe, Telegram, Grok), and disabled test parallelization guidelines, while removing all obsolete and stale documentation.

Working directory: `e:\digitalbrain`
Integrity mode: development

## Requirements

### R1. Stale Documentation & Directory Cleanup
Delete all obsolete, redundant, or stale documentation files and directories that are no longer accurate or needed. Specifically:
- **Delete** the entire `docs/superpowers/` directory (including `plans/`, `specs/`, and `spikes/`).
- **Delete** `ORIGINAL_REQUEST.md` in the repository root.
- **Delete** `TEST_READY.md` in the repository root.

### R2. Document Audit & Layout Updates
Audit all remaining `.md` files in the repository root (e.g., `README.md`, `PROJECT.md`, `CLAUDE.md`, `TEST_INFRA.md`) and under `docs/` (e.g., `docs/v3/VISION.md`, `docs/BRAINOS_RESEARCH.md`). Update all repository layout cheat sheets, folder diagrams, and dependency maps to match the unified layout:
- Reflect `sdk/DigitalBrain.SDK/` and `sdk/DigitalBrain.SDK.Contracts/` as the consolidated SDK core.
- Replace stale references to individual standalone projects (`sdk/DigitalBrain.SDK.Developer/`, `sdk/DigitalBrain.SDK.Google/`, `sdk/DigitalBrain.SDK.Ai/`, `sdk/DigitalBrain.SDK.Aspire/`, `sdk/DigitalBrain.SDK.Windows/`, `sdk/DigitalBrain.SDK.Identity/`).
- Reflect the clean root location for Stripe and Telegram: `sdk/DigitalBrain.SDK/Stripe/` and `sdk/DigitalBrain.SDK/Telegram/`.
- Reflect the location of Grok connectors: `sdk/DigitalBrain.SDK/XAI/Grok/`.

### R3. Namespace Refinement Updates
Identify and update all namespace references in documentation to match the refined modern C# equivalents:
- Stripe contracts: `DigitalBrain.SDK.Stripe.Contracts` (replacing obsolete namespaces like `BrainOS.Domains.Stripe.Contracts`).
- Telegram contracts: `DigitalBrain.SDK.Telegram.Contracts`.
- Grok connectors: `DigitalBrain.SDK.XAI.Grok`.

### R4. Test Parallelization & Sequential Execution Guidelines
Update the testing and operational guides (e.g., `README.md`, `TEST_INFRA.md`, `docs/BRAINOS_RESEARCH.md`) to reflect the new global testing configuration:
- Instruct developers to run `dotnet test` sequentially from the root without stage-filtering flags (retire `@stage:fast`, `@stage:integration`, `@stage:e2e` filters for running suites).
- Explicitly document the Orleans port contention fix and state the rule that test parallelization is disabled globally inside assembly configurations using `[assembly: CollectionBehavior(DisableTestParallelization = true)]` inside `DigitalBrain.Test/AssemblyInfo.cs` and `kernel/BrainOS.Kernel.Tests/AssemblyInfo.cs`.

### R5. Verification and Compilation Integrity
Ensure that no modifications or file removals introduce syntax issues, compile errors, or break any active features. Verify by running a clean build and testing the entire solution sequentially from the root.

## Acceptance Criteria

### Documentation Cleanup
- [ ] The `docs/superpowers/` directory is completely removed from the filesystem.
- [ ] `ORIGINAL_REQUEST.md` is deleted from the root.
- [ ] `TEST_READY.md` is deleted from the root.

### Cheat Sheet & Layout Verification
- [ ] `CLAUDE.md` and `README.md` reflect the consolidated `sdk/DigitalBrain.SDK` and `sdk/DigitalBrain.SDK.Contracts` directory layout, with zero references to stale standalone SDK directories (`DigitalBrain.SDK.Ai`, `DigitalBrain.SDK.Aspire`, etc.).
- [ ] `PROJECT.md` correctly outlines the updated `sdk/DigitalBrain.SDK/` structure, including `Stripe/`, `Telegram/`, and `XAI/Grok/` components.

### Namespace Accuracy
- [ ] All remaining markdown documents reference modern unified namespaces: `DigitalBrain.SDK.Stripe.Contracts`, `DigitalBrain.SDK.Telegram.Contracts`, and `DigitalBrain.SDK.XAI.Grok` rather than obsolete namespaces.

### Test Guidelines
- [ ] `README.md`, `TEST_INFRA.md`, and `docs/BRAINOS_RESEARCH.md` are updated to state that `dotnet test` must be run sequentially from the root without stage-filtering flags.
- [ ] Documentation explains the assembly-level parallelization disablement (`DisableTestParallelization = true`) to prevent Orleans silo port contention during concurrent test runs.

### Compilation and Tests
- [ ] `dotnet build` compiles the entire solution with 0 errors.
- [ ] `dotnet test` executed sequentially passes all 440 tests cleanly with 100% green status.

## 2026-05-26T06:36:49Z

Implement the Domain-Oriented Substrate Reorganization and Tool SDK Unification (Milestone 6) to prune procedural bloat, align directories by domain, and establish expressive core neurons and factories.

Working directory: `e:\digitalbrain`
Integrity mode: development

## Requirements

### R1. Prune Redundant Source-Generators and Bloat
- Prune redundant procedural source-generators from the [BrainOS.Core.SourceGen](file:///e:/digitalbrain/kernel/BrainOS.Core.SourceGen/) directory (e.g. InoNeuronGenerator, NeuronGenerator) and old procedural trash/unused files in the repository.
- Consolidate synapse creation: all synapses become standard C# record classes representing Named Data Types mapped directly from InoLang schemas.

### R2. Restructure SDK into Domain-Aligned Paths
- Restructure the subdirectories under `sdk/DigitalBrain.SDK/` into four clear, clean domain-aligned paths:
  1. **Ai** (incorporating Llm, Grok, Chat, Embedding, etc.)
  2. **Collaboration** (incorporating GitHub, Google, Telegram, Stripe, etc.)
  3. **Development** (incorporating Dotnet, INO, SoftwareEngineering, Scripting, Testing, etc.)
  4. **UI** (incorporating Flutter, Canvas, Visuals, etc.)
- Fix all namespace declarations, project file references (`.csproj`), and `using` statements to ensure clean solution builds.

### R3. Implement `LLM : Neuron` and `Grok : LLM`
- Establish the baseline class `LLM` in the AI Domain, inheriting from the base `Neuron` class. It must support `AskAsync` and standard chat completion pathways via `Microsoft.Extensions.AI`.
- Implement `Grok` as a concrete neuron inheriting from `LLM`. Ensure dynamic, DPAPI-protected resolution of API keys using `ISecretVault` at runtime (resolving `"xai-api-key"`).

### R4. Extend SDK with Core Tool Neurons
- Introduce **`GitHub`** (Collaboration domain) to automate repository commits, PRs, issues, and syncs by wrapping `gh` CLI and Octokit via plain-English synaptic triggers.
- Introduce **`Dotnet`** (Development domain) to run `dotnet build`, `dotnet test`, `dotnet format`, and `dotnet run` natively, piping telemetry back.
- Introduce **`Flutter`** (UI domain) to handle composition, hot reloads, and visual component renders via RFW.

### R5. Standardize Neurons under `INeuron<TState>` and `NeuronFactory`
- Standardize all dynamic neurons under the unified `INeuron<TState>` contract:
  ```csharp
  public interface INeuron<TState>
  {
      TState State { get; set; }
      Task OnActivatedAsync();
      TaskOnDeactivatedAsync();
      Task<Synapse> OnSynapseReceivedAsync(Synapse synapse);
  }
  ```
- Introduce a unified `NeuronFactory` in `BrainOS.Core` that coordinates Orleans dynamic grain instantiation, stripping out Roslyn code-generation boilerplate templates and delegating execution safely.

## Acceptance Criteria

### Restructuring & Cleanup
- [ ] No compilation errors remain in the entire `DigitalBrain.slnx` solution.
- [ ] Redundant source-generator files have been pruned and removed from `BrainOS.Core.SourceGen`.
- [ ] Subdirectories of `DigitalBrain.SDK` are successfully organized under the domain-aligned paths (`Ai`, `Collaboration`, `Development`, `UI`).

### Neuron Taxonomy & Functionality
- [ ] `LLM` base neuron class compiles and correctly references `Microsoft.Extensions.AI`.
- [ ] `Grok` successfully inherits from `LLM` and uses `ISecretVault` to dynamically resolve `"xai-api-key"`.
- [ ] `GitHub`, `Dotnet`, and `Flutter` neurons compile and provide native orchestration pathways for `gh` CLI, `dotnet CLI`, and RFW composition respectively.

### Unified Contract & Factory
- [ ] The `INeuron<TState>` interface is defined under `BrainOS.Core.Neurons`.
- [ ] `NeuronFactory` compiles, instantiates Orleans dynamic grain types, and handles their activations using standard dynamic proxy routing.

### Test Green
- [ ] `dotnet test` executes successfully across the solution with all tests passing.
- [ ] Unit tests are created/updated to verify `Grok` inheritance and secret resolution, `GitHub/Dotnet/Flutter` CLI orchestration pipelines, and `NeuronFactory` dynamic activation.

## 2026-05-26T06:40:34Z

Hello Substrate Reorganizer Team,

The user has revised the design requirement for the SDK reorganization (R2)! Please immediately halt the reorganization of the monolithic DigitalBrain.SDK folders and adapt the implementation plan.

### Revised SDK Architecture (R2):
1. **Modular, Service-Aligned Projects**: Instead of a monolithic `DigitalBrain.SDK` assembly organized into folders, deconstruct the SDK into separate, modular `.csproj` projects under `sdk/` based on their service or vendor (e.g., `sdk/Ai/Llm/Llm.csproj`, `sdk/Collaboration/GitHub/GitHub.csproj`, `sdk/Development/Dotnet/Dotnet.csproj`, `sdk/UI/Flutter/Flutter.csproj`, etc.).
2. **Co-located .ino and .cs**: Ensure that each platform-access neuron has its `.ino` spec file co-located directly next to its `.cs` sidecar file inside that dedicated project folder.
3. **Register in Solution**: Register all these new individual `.csproj` projects directly in the solution file `DigitalBrain.slnx` and remove the old monolithic `DigitalBrain.SDK.csproj`.
4. **Update Namespaces**: Update all namespace declarations, dependencies, and imports to ensure everything compiles cleanly.

## 2026-05-26T06:45:09Z

Hello Sentinel and Swarm,

We have a critical, urgent course correction directly from the user! Please immediately halt the physical directory re-organization and the splitting of the SDK into 11 separate projects.

### New Architectural Directive:
1. **DO NOT Physically Re-organize or Split the SDK**: Keep the existing `sdk/DigitalBrain.SDK/` and its subdirectories/projects structurally as-is. Do not split it into 11 individual projects. 
2. **Keep the CompanyName.* Namespace Pattern**: Retain the standard `DigitalBrain.SDK` and related namespaces (i.e. `CompanyName.*` / `CompanyNamespace.*` where `CompanyNamespace` represents the domain or username namespace).
3. **Co-locate .ino Files Directly inside the SDK**: 
   - Add `.ino` files next to the C# sidecar files inside the existing `sdk/` directory.
   - These `.ino` files will define the neuron contract, synapse records, RFW layout, and scenarios for the SDK neurons.
   - This allows them to be scanned and registered by the runtime, and referenced by FQNs like `SDK.Ai.Llm`, `SDK.Collaboration.GitHub`, `SDK.Development.Dotnet`, `SDK.UI.Flutter`, and `SDK.DigitalBrain.Brain` (e.g. referencing them like `SDK.DigitalBrain.Brain.Start()`).
4. **Implement All Milestone 6 Core Requirements**:
   - **R1**: Prune redundant source-generators (e.g., `NeuronGenerator.cs` in `BrainOS.Core.SourceGen`) and trash files, consolidating synapses as standard C# record classes representing Named Data Types.
   - **R3**: Implement the `LLM : Neuron` base and `Grok : LLM` concrete neuron with dynamic `ISecretVault` resolution of `"xai-api-key"`.
   - **R4**: Implement the core C# tool neurons (`GitHub`, `Dotnet`, `Flutter`) with co-located `.ino` files.
   - **R5**: Standardize neurons under the generic `INeuron<TState>` contract and implement the unified dynamic `NeuronFactory`.

## 2026-05-26T06:48:00Z

Hello Sentinel and Swarm,

The user has officially given the green light: "do it!"

Please coordinate the swarm to execute Milestone 6 with maximum thoroughness and speed. Complete all deliverables on the stable SDK structural layout:
1. Pruning redundant source-generators and old procedural files in `kernel/BrainOS.Core.SourceGen/`.
2. Co-locating base `.ino` files directly inside `sdk/DigitalBrain.SDK/` next to C# sidecars.
3. Implementing `LLM : Neuron` base and `Grok : LLM` with dynamic `ISecretVault` key resolution.
4. Implementing the core tool neurons (`GitHub`, `Dotnet`, `Flutter`) with co-located `.ino` files.
5. Standardizing dynamic neurons under `INeuron<TState>` and introducing `NeuronFactory` for boilerplate-free dynamic Orleans grain activations.
6. Running the unified test suite `dotnet test` to confirm 100% green passage of all 422+ tests.

## 2026-05-26T08:47:16Z

# DigitalBrain Unified Platform & Neuronic Boot Refactoring

Refactor the platform under the unified name `DigitalBrain` (replacing all occurrences of `BrainOS`), and simplify the operating system boot process so that the entrypoint scripts and test suites initialize a minimal hosting floor and emit a bootstrap synapse to boot the entire system dynamically from neurons and synapses themselves.

Working directory: E:\digitalbrain
Integrity mode: development

## Requirements

### R1. Deep Platform Rename (BrainOS -> DigitalBrain)
- Rename all physical directory paths on the filesystem (e.g., change `kernel/BrainOS.Core` to `kernel/DigitalBrain.Core`, `kernel/BrainOS.Kernel` to `kernel/DigitalBrain.Kernel`).
- Rename all `.csproj` project files, C# namespaces, using directives, configuration keys, environment variables, documentation, and comments containing `BrainOS` to `DigitalBrain`.
- Update the central solution file `DigitalBrain.slnx` and all project references to compile and bind cleanly.

### R2. Pure Neuronic Bootstrap Flow
- Refactor the startup entry point `digitalbrain.cs` and the test launcher `testdigitalbrain.cs` to eliminate procedural C# resource and neuron builder wire-ups.
- The entrypoint scripts should only initialize a minimal runtime host (Orleans Silo + gRPC endpoint wrapper) and immediately emit a bootstrap synapse (e.g. `BootSystem` or `InitializeGenesis`) to a system `GenesisNeuron`.
- The `GenesisNeuron` will handle reading the topology specification and dynamically activating the other core system neurons (such as `AspireNeuron`, `TimelineNeuron`, and domain neurons) by dispatching synapses.

### R3. Aspire Orchestration as a Neuron
- Represent the .NET Aspire distributed application builder itself as a neuron (e.g., `AspireNeuron` or `OrchestratorNeuron`).
- Instead of procedurally configuring Aspire resources in C# code within `digitalbrain.cs`, represent the Aspire app host topology as data. The bootstrap flow will activate `AspireNeuron` with a configuration synapse, and the neuron will invoke the Aspire developer dashboard and child resources dynamically.

### R4. xAI MCP Live Integration & Verification
- Ensure that the LLM provider configurations (in `digitalbrain.cs` and the kernel settings) support reading the `XAI_API_KEY` (or `Grok` API credentials) from the environment.
- Populate xAI settings dynamically on startup so that the Grok/LLM neurons can query live models and be fully tested and verified via the Model Context Protocol (MCP) tool gateway.

## Acceptance Criteria

### Compilation & Solution Integrity
- [ ] The entire solution compiles with zero errors and zero warnings under the new `DigitalBrain` naming system.
- [ ] No directory paths on the file system or configuration schemas contain the word `BrainOS` (except where required by legacy external dependencies, if any).

### Unified Test Runner Success
- [ ] Running `dotnet run testdigitalbrain.cs` builds and runs the entire suite of 121 tests successfully under the new `DigitalBrain` namespace with 100% green passes.

### Dynamic Neuronic Boot
- [ ] The `digitalbrain.cs` script successfully launches the platform by executing a single bootstrap synapse emission rather than procedural hosting builds.
- [ ] The .NET Aspire dashboard boots up cleanly using `dotnet run digitalbrain.cs` or `aspire start` under the `DigitalBrain` runtime.
- [ ] Grok and tool neurons resolve and route live MCP tool invocations successfully when configured with a valid `XAI_API_KEY` in the environment.

## 2026-05-27T15:07:30Z

Ruthlessly debug the blank/white home screen issue in the Flutter UI, continue codebase simplification, and remake the Flutter UI under the premium Ino Constructor and Ino Code Editor vision.

Working directory: `e:\digitalbrain`
Integrity mode: `development`

## Requirements

### R1. Diagnose & Fix the Blank UI Screen
- Investigate and resolve the blank/white screen on load.
- Avoid external `GoogleFonts` downloads if running in an offline environment (fall back to safe system sans-serif fonts).
- Ensure no null/unhandled exceptions on cold activation of `BrainCanvas` or `NeuronConstructorView`.

### R2. Remake UI with Ino Constructor & Ino Code Editor Vision
- **Visual Neuron Constructor (Left Pane):** Implement a pure custom interactive visual node-based editor representing Neurons and Synapses using `GestureDetector`s, `CustomPainter` for connections, and custom state (keeping it robust, lightweight, and zero external dependency). Users must be able to click-to-spawn nodes and drag lines to connect them.
- **Ino Code Editor (Right Pane):** Simulated/real syntax highlighting for InoLang keywords (`neuron`, `synapse`, `on`, `emit`, `ask`, `scenario`, `given`, `when`, `then`), dynamically synchronized with selected nodes.
- **Brain Canvas Chat & Visualizer (Bottom/Overlay):** Dynamic, animated 2D neural graph canvas drawing when the user types "visualize [concept]".
- **Stunning Navigation:** Floating HUD button "3D Constellation View" that transitions smoothly to `/brain/digitalbrain`.

### R3. Continuous Codebase Simplification
- Audit `UI/flutter/lib/` and delete stale, deprecated, or duplicate widgets/controllers.

## Acceptance Criteria

### UI Functional Verification
- [ ] No blank/white screen on cold boot; page renders instantly and gracefully.
- [ ] Visual Constructor successfully allows interactive node creation and line dragging.
- [ ] Ino Code Editor displays syntax-highlighted code that syncs with selected nodes.
- [ ] Typing "visualize" in the chat area animates a custom drawn canvas representation.
- [ ] Solution compiles with zero errors across C# and Flutter builds.

### Verification Mechanism
- [ ] Playwright E2E browser checks or standard Flutter integration tests confirm clean UI loading and basic click/drag capability without throwing unhandled exceptions.

## 2026-05-27T18:15:41+02:00

Unify the InoLang runtime by simplifying compilation and execution, enabling live dynamic Orleans grain registration, and supporting bidirectional hot-reload synchronization with the UI and file saves.

Working directory: e:\digitalbrain
Integrity mode: development

## Requirements

### R1. Ruthless AST and Compiler Simplification
- Simplify `DigitalBrain.InoLang` by pruning redundant parsers, legacy tokenizers, and obsolete AST definitions while keeping the language spec simple and declarative.
- Compile `.ino` declarations directly into Roslyn-executable C# scripts containing the neuron's behavior, signals, routing rules, and UI blocks.

### R2. Live Compiler & Orleans Dynamic Grain Registration
- Implement dynamic, reflection-less grain registration within Orleans. 
- When a new `neuron` is declared or saved in a `.ino` file, Orleans must dynamically activate a generic `DynamicNeuronGrain` executing the lowered Roslyn C# script.
- Support behavioral keywords (`ask`, `emit`, `on`):
  - `ask` to invoke downstream/SDK/AI neurons dynamically.
  - `emit` to route synapses to connected targets.
  - `on` to trigger logic when incoming matching synapses are received.

### R3. Bidirectional Hot-Reload & UI Synchronization
- Connect the C# backend with the UI Visual Constructor and Ino Code Editor.
- Hot-reload active Orleans neuron topologies and recompilation must trigger automatically on both file-system saves (via directory watcher) and UI visual edits.
- Feed real-time activation logs or results to the Chat visualizer pane when a synapse fires.

### R4. Test Coverage & Clean Sweeps
- Verify all implementations with automated tests, keeping the entire suite (`dotnet test`) green.
- Prune obsolete boilerplate, redundant grain interfaces, and unused routing catalog tables across the codebase.

## Acceptance Criteria

### InoLang Parser & Code Generator
- [ ] Obsolete compiler, parser, and lexer code is cleaned up and deleted from `DigitalBrain.InoLang`.
- [ ] Compiler successfully generates clean Roslyn C# script strings from `.ino` definitions.

### Dynamic Orleans Runtime
- [ ] Saving an `.ino` file compiles it and dynamically registers and activates the neuron in the Orleans cluster without Silo restarts.
- [ ] Active neurons route synapses (`emit` and `ask` signals) dynamically between themselves based on InoLang rules.

### Synchronization & Hot-Reload
- [ ] Backend monitors `.ino` files for changes and automatically recompiles and hot-reloads the active Orleans grain topology.
- [ ] dragging new visual connections in the UI successfully triggers recompilation and hot-reload.

### Project Health
- [ ] All C# and Orleans tests in the solution compile and pass cleanly via `dotnet test`.

## 2026-05-29T23:08:44Z

Implement the Living Canvas UI Unification & Simplification Slice 1 (S1) in DigitalBrain, which replaces legacy screens (~14,400 lines) with one clean, unified canvas and sweeps the orphaned files.

Working directory: E:\digitalbrain
Integrity mode: development

## Requirements

### R1. Implement LivingCanvasScreen
Create the new `LivingCanvasScreen` in `UI/flutter/lib/features/canvas/living_canvas_screen.dart` using a full-bleed `LiveScreen` graph widget and a `FloatingPromptDock` as specified in [2026-05-29-flutter-cut-living-canvas-s1.md](file:///E:/digitalbrain/docs/superpowers/plans/2026-05-29-flutter-cut-living-canvas-s1.md).

### R2. Re-Route Root to LivingCanvasScreen
Modify `UI/flutter/lib/router.dart` to mount `LivingCanvasScreen` as the root (`/`) route and remove legacy `/constellation` and `/brain/:brainId` routes, as well as the unused `BrainScenePlaceholder`.

### R3. Delete Legacy Screens and Features
Delete the following files and directories cleanly:
- `UI/flutter/lib/features/brain/brain_scene_screen.dart`
- `UI/flutter/lib/features/constellation/` (entire directory)
- `UI/flutter/lib/features/home/constructor_editor_home_page.dart`
- `UI/flutter/lib/features/neuron_constructor/neuron_constructor_view.dart`
- `UI/flutter/lib/features/neuron_constructor/liquid_glass_3d_brain.dart`

### R4. Sweep Orphaned/Unused Files
Identify and sweep all remaining unused files inside `UI/flutter/lib/` using `flutter analyze` diagnostics to ensure clean deletion of dead code.

### R5. Verify Build and Test Suite
Ensure that the Flutter web release builds successfully and the entire C# backend/E2E test suite remains perfectly green via `dotnet test`.

## Acceptance Criteria

### Compilation & Diagnostics
- Running `flutter analyze` inside `UI/flutter` finishes with no errors or warnings introduced by this cut.
- Running `flutter build web --release` completes successfully with zero compile-time errors.

### Code Reduction
- The total number of `.dart` files in `UI/flutter/lib/` is significantly reduced from the baseline of 120 (measured at Step 1).
- Over 14,000 lines of legacy screen code are deleted.

### Backend Integrity
- Running `dotnet test` from `E:\digitalbrain` passes successfully, verifying that all E2E gRPC and RFW contracts remain fully intact.
