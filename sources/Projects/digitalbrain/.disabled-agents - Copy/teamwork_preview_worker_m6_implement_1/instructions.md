# Implementation Instructions - Milestone 6: Domain-Oriented Substrate Reorganization and Tool SDK Unification

You are the Lead Implementation Worker. Your goal is to implement all codebase modifications, restructuring, and core neuron-factory implementations for Milestone 6 to 100% completion.

## MANDATORY INTEGRITY WARNING
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A Forensic Auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

---

## 1. Scope of Work

### 1.1. Pruning Procedural Source-Generators & Consolidating Synapses (R1)
1. **SourceGen Project**: Locate `kernel/BrainOS.Core.SourceGen/`.
   - Remove `InoNeuronGenerator.cs` and `NeuronGenerator.cs` (or prune their procedural switch-cases/boilerplate code). 
   - **DO NOT** delete `InoTestGenerator.cs`, `EquatableArray.cs`, `IndexRangePolyfill.cs`, or `IsExternalInit.cs` as they are strictly required for generating scenario-driven test facts in `DigitalBrain.Test`.
   - Update `InoNeuronGenerator.cs` to eliminate the huge hardcoded `switch-case` in `EmitSynapseInstantiation`. Instead, combine `AdditionalTextsProvider` with `CompilationProvider` in `Initialize`. In the output step, retrieve the `INamedTypeSymbol` for the target synapse FQN (`compilation.GetTypeByMetadataName(targetFqn)`), inspect its primary constructor parameters (names, types), and dynamically emit constructor expressions `new global::FQN(...)` with appropriate type conversions/coercion (e.g. `bool` via `IsTrue`, `Guid` via `Guid.TryParse`, `string` via direct assignment, collections deserialized from JSON).
2. **Synapse Record Consolidation**: Ensure all synapse structures are standard, C# records inheriting from `Synapse` under `sdk/DigitalBrain.SDK.Contracts/` mapped directly from InoLang schemas.
3. **Runtime Symmetrical Synapse Factory**: Implement a clean `SynapseFactory` under `BrainOS.Core.Neurons` (in `kernel/BrainOS.Core/Neurons/SynapseFactory.cs`) that exposes `public static Synapse? CreateSynapse(string fqn, IReadOnlyDictionary<string, string> args)` which uses reflection to inspect the primary constructor, coerce string dictionary values into parameter types, and invoke the constructor dynamically, mirroring the compile-time generation logic.

### 1.2. SDK Domain Reorganization (R2)
1. Restructure the directories under `sdk/DigitalBrain.SDK/` and `sdk/DigitalBrain.SDK.Contracts/` to align them under four clean domain paths:
   - **`Ai`**: `Ai`, `XAI` (Grok), `Swarm` (move to `Ai/Swarm`).
   - **`Collaboration`**: `Google`, `Telegram`, `Stripe`, and `Developer/GitHub` (move `GitHub` folder from `Developer/` to `Collaboration/GitHub`).
   - **`Development`**: `Developer` (excluding GitHub), `INO`, `SoftwareEngineering`, `Scripting`, `Testing`, `CodeGraph`, `Windows`, `Aspire`, `Persistence`, `Postgres`, `Sqlite`, `Security`, `Identity`, and `Onboarding` (physically move `Onboarding` to `Development/Onboarding`, but retain namespace `BrainOS.Domains.Onboarding.Onboarding` to prevent breaking serialization).
   - **`UI`**: `Canvas`, `Visuals`.
2. Fix all namespace declarations in the moved files to match their new domain-aligned directories, but be extremely careful with types that are serialized in Orleans streams (or use Orleans `[Alias]` / preserve historical namespaces for serialized messages like `SwarmAgentMessage` and `AppStartedSignal` to prevent stream breakdown).
3. Update all `using` statements, code references, and tests across the entire `DigitalBrain.slnx` solution to match the reorganized namespaces and compiling perfectly.

### 1.3. Cognitive Layer: `LLM : Neuron` & `Grok : LLM` (R3)
1. **LLM Base Neuron**: Establish the baseline class `LLM` in the `Ai` domain, inheriting from `Neuron`. Support `AskAsync` and standard chat completion pathways via `Microsoft.Extensions.AI` (`IChatClient`). Resolve the underlying `IChatClient` dynamically based on primary grain keys.
2. **Grok Concrete Neuron**: Implement `Grok` as a concrete neuron inheriting from `LLM` under `Ai/Grok/`. Ensure dynamic, DPAPI-protected resolution of API keys using `ISecretVault` at runtime (resolving `"xai-api-key"`). Provide a fallback to local configurations during testing if the vault key is missing.

### 1.4. Core Tool Neurons (R4)
1. **GitHub Tool Neuron**: Introduce `GitHub` (Collaboration domain) to automate commits, PRs, issues, and syncs by wrapping `gh` CLI and Octokit via plain-English synaptic triggers.
2. **Dotnet Tool Neuron**: Introduce `Dotnet` (Development domain) to run `dotnet build`, `dotnet test`, `dotnet format`, and `dotnet run` natively, piping telemetry back.
3. **Flutter Tool Neuron**: Introduce `Flutter` (UI domain) to handle composition, hot reloads, and visual component renders via RFW (Remote Flutter Widgets), emitting `RfwCard` synapses to render layouts.

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
   Verify that all 422+ unified tests pass cleanly in under 30 seconds.

Write your final handoff report summarizing:
- Exact list of modified and created files.
- Command lines executed and build/test results.
- Integrity verification attestation.
