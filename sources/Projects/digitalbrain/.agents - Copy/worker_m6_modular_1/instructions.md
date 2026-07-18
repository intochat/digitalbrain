# Implementation Instructions - Milestone 6: Domain-Oriented Substrate Reorganization and Tool SDK Unification (Modular Edition)

You are the Lead Implementation Worker. Your goal is to implement all codebase modifications, modular project deconstruction, and core neuron-factory implementations for Milestone 6 to 100% completion.

## MANDATORY INTEGRITY WARNING
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A Forensic Auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

---

## 1. Scope of Work

### 1.1. Pruning Procedural Source-Generators & Consolidating Synapses (R1)
1. **SourceGen Project**: Locate `kernel/BrainOS.Core.SourceGen/`.
   - Prune redundant procedural switch-cases/boilerplate code from `InoNeuronGenerator.cs` and `NeuronGenerator.cs`.
   - **DO NOT** delete `InoTestGenerator.cs`, `EquatableArray.cs`, `IndexRangePolyfill.cs`, or `IsExternalInit.cs` as they are strictly required for generating scenario-driven test facts in `DigitalBrain.Test`.
   - Update `InoNeuronGenerator.cs` to eliminate the huge hardcoded `switch-case` in `EmitSynapseInstantiation`. Instead, combine `AdditionalTextsProvider` with `CompilationProvider` in `Initialize`. In the output step, retrieve the `INamedTypeSymbol` for the target synapse FQN (`compilation.GetTypeByMetadataName(targetFqn)`), inspect its primary constructor parameters (names, types), and dynamically emit constructor expressions `new global::FQN(...)` with appropriate type conversions/coercion (e.g. `bool` via `IsTrue`, `Guid` via `Guid.TryParse`, `string` via direct assignment, collections deserialized from JSON).
2. **Synapse Record Consolidation**: Ensure all synapse structures are standard, C# records inheriting from `Synapse` co-located directly within their dedicated service-aligned projects under `sdk/` mapped directly from InoLang schemas.
3. **Runtime Symmetrical Synapse Factory**: Implement a clean `SynapseFactory` under `BrainOS.Core.Neurons` (in `kernel/BrainOS.Core/Neurons/SynapseFactory.cs`) that exposes `public static Synapse? CreateSynapse(string fqn, IReadOnlyDictionary<string, string> args)` which uses reflection to inspect the primary constructor of the synapse FQN resolved dynamically across loaded assemblies, coerce string dictionary values into parameter types, and invoke the constructor dynamically, mirroring the compile-time generation logic.

### 1.2. SDK Modularization & Solution Registration (R2 - REDESIGNED)
1. **Monolithic Project Deconstruction**:
   Completely remove the old monolithic `DigitalBrain.SDK.csproj` and `DigitalBrain.SDK.Contracts.csproj` from the workspace and solution.
2. **Modular, Service-Aligned Projects**:
   Deconstruct the subdirectories under `sdk/` into separate, modular `.csproj` projects based on service or vendor:
   - **Ai**:
     - `sdk/Ai/Llm/Llm.csproj` (incorporating Llm, Grok, Chat, Embedding, voice, planning, explanations, intent, SLMs, AI health, and chat)
     - `sdk/Ai/Swarm/Swarm.csproj` (incorporating Orleans streaming multi-agent collaboration)
   - **Collaboration**:
     - `sdk/Collaboration/GitHub/GitHub.csproj` (incorporating GitHub-wrapped tool neuron)
     - `sdk/Collaboration/Google/Google.csproj` (incorporating Gmail, YouTube, etc.)
     - `sdk/Collaboration/Telegram/Telegram.csproj` (incorporating Telegram alert neuron)
     - `sdk/Collaboration/Stripe/Stripe.csproj` (incorporating Stripe billing integration and webhook handlers)
   - **Development**:
     - `sdk/Development/Dotnet/Dotnet.csproj` (incorporating Dotnet build tool neuron, INO transpiler, SoftwareEngineering developer agent, Dynamic C# scripting service, static CodeGraph, Developer file/directory utilities, and Windows/OS bindings)
     - `sdk/Development/Aspire/Aspire.csproj` (incorporating Orleans/Aspire boot orchestration connectors and runtimes)
     - `sdk/Development/Persistence/Persistence.csproj` (incorporating Sqlite, Postgres, DbContexts, connection factories, and durable EF Core synapses)
     - `sdk/Development/Security/Security.csproj` (incorporating security vault DPAPI, settings, identity plan, Azure deployments, and terms onboarding agreement store — retaining onboarding namespace `BrainOS.Domains.Onboarding.Onboarding` to prevent Orleans binary serialization breakdown)
   - **UI**:
     - `sdk/UI/Flutter/Flutter.csproj` (incorporating Flutter UI tool neuron, canvas neurons, visual plan resolvers, and RFW rendering)
3. **Co-location of Specifications**:
   For each platform-access neuron in these projects, ensure that its `.ino` spec file is co-located directly next to its C# `.cs` sidecar file within that dedicated project directory (e.g. `GitHub.ino` and `GitHubNeuron.cs` are in `sdk/Collaboration/GitHub/`). Ensure that the `.csproj` files configure embedded and additional file patterns correctly:
   ```xml
   <ItemGroup>
     <EmbeddedResource Include="**\*.ino" />
     <AdditionalFiles Include="**\*.ino" Exclude="bin\**\*;obj\**\*" />
   </ItemGroup>
   ```
4. **Solution Registration (`DigitalBrain.slnx`)**:
   Register all 11 new individual `.csproj` projects directly in the solution file `DigitalBrain.slnx` under appropriate sections, and remove `DigitalBrain.SDK.csproj` and `DigitalBrain.SDK.Contracts.csproj`.
5. **Namespace and Imports Updates**:
   Update all namespace declarations, dependencies, and `using` statements throughout the workspace (including kernel projects, tests, and mock directories) to ensure they reference the new modular projects cleanly. 
   *Note: For Orleans streamed types (e.g. SwarmAgentMessage, AppStartedSignal) and Onboarding types, preserve their historical namespaces or use Orleans `[Alias]` to avoid breaking serialization compatibility.*

### 1.3. Cognitive Layer: `LLM : Neuron` & `Grok : LLM` (R3)
1. **LLM Base Neuron**: Establish the baseline class `LLM` in the `sdk/Ai/Llm/` project, inheriting from `Neuron`. Support `AskAsync` and standard chat completion pathways via `Microsoft.Extensions.AI` (`IChatClient`). Resolve the underlying `IChatClient` dynamically based on primary grain keys.
2. **Grok Concrete Neuron**: Implement `Grok` as a concrete neuron inheriting from `LLM` under `sdk/Ai/Llm/`. Ensure dynamic, DPAPI-protected resolution of API keys using `ISecretVault` at runtime (resolving `"xai-api-key"`). Provide a fallback to local configurations during testing if the vault key is missing.

### 1.4. Core Tool Neurons (R4)
1. **GitHub Tool Neuron**: Introduce `GitHub` (Collaboration domain - `sdk/Collaboration/GitHub/`) to automate commits, PRs, issues, and syncs by wrapping `gh` CLI and Octokit via plain-English synaptic triggers.
2. **Dotnet Tool Neuron**: Introduce `Dotnet` (Development domain - `sdk/Development/Dotnet/`) to run `dotnet build`, `dotnet test`, `dotnet format`, and `dotnet run` natively, piping telemetry back.
3. **Flutter Tool Neuron**: Introduce `Flutter` (UI domain - `sdk/UI/Flutter/`) to handle composition, hot reloads, and visual component renders via RFW (Remote Flutter Widgets), emitting `RfwCard` synapses to render layouts.

### 1.5. Unified contract `INeuron<TState>` and `NeuronFactory` (R5)
1. **`INeuron<TState>` Interface & `Neuron<TState>` Base Class**:
   Define `INeuron<TState>` interface under `BrainOS.Core.Neurons`:
   ```csharp
   public interface INeuron<TState>
   {
       TState State { get; set; }
       Task OnActivatedAsync();
       Task OnDeactivatedAsync();
       Task<Synapse> OnSynapseReceivedAsync(Synapse synapse);
   }
   ```
   Define the corresponding stateful abstract class `Neuron<TState>` implementing `INeuron<TState>` and inheriting from `Neuron`, supporting Orleans custom state facet resolution and immediate transactional write-backs (`WriteStateAsync`).
2. **`NeuronFactory`**: Implement `NeuronFactory` in `BrainOS.Core` that coordinates Orleans dynamic grain instantiation and fast in-memory mock setups, completely stripping out dynamic Roslyn compilation pipelines.

---

## 2. Execution & Testing

1. Perform sequential compilation and build checks to ensure the entire `DigitalBrain.slnx` solution compiles cleanly with 0 warnings or errors.
2. Update/create unit and integration tests to verify:
   - `Grok` inheritance and secret resolution.
   - `GitHub/Dotnet/Flutter` CLI orchestration pipelines and RFW layout rendering.
   - `INeuron<TState>` statefulness and transactional updates.
   - `NeuronFactory` dynamic activation.
3. Run the full test suite sequentially:
   ```powershell
   dotnet test --max-parallel-test-modules 1
   ```
   Verify that all 422+ unified tests pass cleanly.

Write your final handoff report summarizing:
- Exact list of modified and created files.
- Command lines executed and build/test results.
- Integrity verification attestation.
