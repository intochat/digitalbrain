# Test-Driven Neuron Generation Loop — Architectural Design Handoff

This report details the architectural design for the **Test-Driven Neuron Generation Loop** in DigitalBrain. It covers how a developer authors `.ino` files with mock stubs, how dynamic neuron code is generated, compiled via Roslyn, and validated against sandboxed Orleans environments (`BrainOS.NeuronTesting`) within a robust, self-healing feedback loop.

---

## 1. Observation

Direct observations of the codebase reveal the following key architectural components and files:

### A. Dynamic Scripting & Roslyn Compilation
*   **Dynamic Script Execution**: In `kernel/BrainOS.Core.Hosting/DynamicNeuronGrain.cs`, dynamic C# scripts are compiled at runtime using the `Microsoft.CodeAnalysis.CSharp.Scripting` package. 
    *   **Context Globals**: The script executes inside a custom globals context defined at line 15:
        ```csharp
        public sealed class DynamicNeuronScriptGlobals
        {
            public string PayloadJson { get; init; } = "";
            public string TypeName { get; init; } = "";
            public CorrelationId CorrelationId { get; init; }
            public IServiceProvider Services { get; init; } = null!;
        }
        ```
    *   **Compilation & Imports**: Compilation is handled by `CSharpScript.Create<string>` at lines 63–76, importing assemblies like `Neuron`, `System.Text.Json`, and namespaces like `BrainOS.Core` and `Microsoft.Extensions.DependencyInjection`.
*   **Standalone Compilation**: In `sdk/DigitalBrain.SDK/Scripting/DynamicScriptingService.cs`, the `DynamicScriptingService` implements `IDynamicScriptingService`, which allows compiling and executing in-memory code blocks via `CSharpScript.Create<object>(code, options, globalsType: typeof(DynamicScriptingGlobals))` and captures all compilation diagnostics (errors/warnings) to return a structured `ScriptResult`.

### B. InoLang Compiler & Scenario Interpreter
*   **The InoLang Syntax**: As seen in `samples/BrainOS.Domains.Travel/BrainOS.Domains.Travel/TripRadar/TripPlanner.ino`, `.ino` files express:
    *   The neuron's fully qualified name (`neuron Test.Neuron`)
    *   Inputs/Outputs/Dependencies (`using ask = synapse(...)`, `using replied = signal(...)`, `using groupChat = neuron(...)`)
    *   Behavior rules (`on ask: emit replied(...)`)
    *   BDD scenarios (`scenario "bali 5 days" given ... when ... then ...`)
*   **Compiler Pipeline**: Inside `inolang/DigitalBrain.InoLang/InoCompiler.cs` and `InoAuthoringLoop.cs:87–88`, files are compiled with `InoCompiler.Compile(draft, catalog)` against an active schema contract catalog (`IContractCatalog`), verifying structural validity and returning AST plans.
*   **In-Process Interpreter Runner**: `inolang/DigitalBrain.InoLang/Testing/ScenarioRunner.cs` natively interprets `.ino` scenarios:
    *   **Seam Mocks**: Pins BDD `given [neuron/predicate] returns [value]` steps directly into `StubSeamHost.cs` (`SeamReturns` and `PredicateValues`).
    *   **Execution**: Invokes the `Interpreter` at lines 40-41:
        ```csharp
        var result = await new Interpreter(plan).RunAsync(TriggerKey.Synapse(when.Port), inbound, stub, ct);
        ```
    *   **Assertions**: Validates output events via `ThenSignalEmitted` assertions (lines 47-58).

### C. Orleans Test Sandboxes & Harnesses
*   **Integration Harness**: In `kernel/BrainOS.NeuronTesting/TestBrainOS.cs` and `BrainOSTest.cs`, `TestBrainOS` bootstraps the entire multi-silo application under an Aspire `DistributedApplicationFactory` setup with environment variables customized for headless testing:
    *   Registers environment overrides (`BrainOS__Ai__UseMockClient = true`, `BrainOS__Google__UseStubServices = true`) via `TestBrainOSOptions.cs`.
    *   Emits synapses through the gRPC gateway using `_client.SendAsync` and captures home feed broadcasts / replies correlated by `CorrelationId`.
*   **Shared Instance Caching**: To circumvent the typical 30-second silo startup cost between BDD scenarios, `TestBrainOSBootstrapper.cs` manages a static thread-safe dictionary of lazy tasks (`Boots`) representing active Silo assemblies, allowing sub-second scenario executions.
*   **In-Silo BDD Runner**: In `kernel/BrainOS.Core.Hosting/NeuronTestRunner.cs`, `NeuronTestRunner.RunAsync(DynamicNeuronSpec spec)` runs Gherkin feature blocks against the staged `IDynamicNeuron` by directly invoking `dyn.InvokeAsync(payloadJson, typeName, correlationId)` and asserting JSON outcomes match the expectation.

### D. Automated Creators & Orchestrators
*   **Orleans Orchestrated Creator**: `kernel/BrainOS.Kernel/Creator/CreatorNeuron.cs` stages a dynamic Roslyn script in `INeuronRegistry` under `DynamicNeuronStatus.Staged`, compiles code drafts (`ImplCompileStage.cs`, `StepCompileStage.cs`), runs them against the staged grain via `INeuronTestRunner.RunAsync(spec)`, and promotes them to `Promoted` on success.
*   **In-Process Self-Healing Loop**: `kernel/BrainOS.Kernel/Creator/InoAuthoring/InoAuthoringLoop.cs` performs rapid `chat.GetResponseAsync()` loops directly against the local `ScenarioRunner` to guarantee that an `.ino` file compiles, links, and has 100% green tests *before* writing it to disk.

### E. C# Neuron Incremental Source Generator
*   **Source Codegen**: `kernel/BrainOS.Core.SourceGen/NeuronGenerator.cs` is an incremental generator that targets partial classes annotated with `[BrainOS.Core.Neurons.NeuronAttribute]` (`[Neuron]`).
    *   Validates invariants (must be partial, inherit from `Neuron`, and implement `IHandle<T>` with a valid return type).
    *   Generates Orleans stream subscriptions (`[ImplicitStreamSubscriptionAttribute]`), constructor dependency injection bindings, and the low-level `protected override async Task HandleSynapseAsync(Synapse synapse)` routing table (lines 361–407).

---

## 2. Logic Chain

From these observations, we can synthesize the complete end-to-end reasoning chain for the **Test-Driven Neuron Generation Loop**:

1.  **Specification-First Authoring**: Developers (or agent-driven workflows) write a high-level `.ino` spec file defining a neuron contract and its corresponding BDD scenarios (with mock stubs and assertions). Under the hood, this syntax map is fully verified by `InoCompiler` against the workspace's stable, frozen keyword and schema registry (`IContractCatalog`).
2.  **In-Process Guard (InoAuthoringLoop)**: The `InoAuthoringLoop` drives an LLM Planner to generate or refine `.ino` contents. By utilizing `ScenarioRunner` and `StubSeamHost`, the system parses BDD steps and runs them natively in-process. Any syntax diagnostic or failed assertion acts as an immediate self-healing feedback trigger, recycling the loop (up to a configured maximum, e.g., 5 attempts) and detailing compiler errors in a bulleted format.
3.  **Roslyn Execution Context**: Once the `.ino` spec is green, its logical script block is ready for execution as a dynamic Orleans grain. This is handled by `DynamicNeuronGrain`, which parses the logic into a Roslyn `Script<string>` and executes it inside an environment containing `DynamicNeuronScriptGlobals` (giving the script full access to Kestrel/Orleans `IServiceProvider` dependencies, incoming JSON payloads, and correlation IDs).
4.  **Sandbox Integration Gating**: For deeper, end-to-end multi-domain validation, `TestBrainOS` orchestrates an in-memory Orleans cluster using Aspire's `DistributedApplicationFactory`. High-fidelity tests inject simulated gateway synapses, load dynamic specs via `INeuronRegistry.StageAsync()`, and trigger in-silo scenario execution via `NeuronTestRunner` to confirm the neuron functions accurately with real silo middleware, streams, and state providers.
5.  **Static Source Generation Promotion**: After securing green BDD assertions inside the sandbox, the staged dynamic neuron is promoted. The loop persists the final `.ino` file and writes a production-ready C# partial class file annotated with `[Neuron]`. This newly written file is immediately caught by the Roslyn Incremental Compiler (`NeuronGenerator`), which emits the boilerplates for stream routing and Orleans grain integration, producing a compiled, static, high-performance C# neuron.

---

## 3. Caveats

*   **Dynamic Script Threading & Sandbox Isolation**: Dynamic Roslyn script execution within `DynamicNeuronGrain` runs inside the same AppDomain as the Orleans silo. Memory leaks or infinite loops in compiled user scripts could affect the hosting silo. Future designs should consider isolating dynamic execution using separate assembly load contexts or sandboxed containers if untrusted scripts are executed.
*   **Transactional Stubs**: Although `StubSeamHost` handles basic neuron/predicate stubs for simple BDD interpreter runs, it does not simulate advanced Orleans transactional behaviors or custom grain states (like reminders, timers, or durable lists). High-fidelity behaviors must be verified in the full `TestBrainOS` sandbox.

---

## 4. Conclusion

The **Test-Driven Neuron Generation Loop** successfully links abstract spec-driven `.ino` designs with robust, compiled, static C# neurons via a three-tier validation architecture:
1.  **Tier 1 (Syntactic & Mock Interpreter)**: Rapid in-process `InoAuthoringLoop` compilation and `ScenarioRunner` BDD interpretation to self-heal code before disk persistence.
2.  **Tier 2 (Silo Sandbox Integration)**: High-fidelity dynamic grain staging in `INeuronRegistry` and in-silo `NeuronTestRunner` / `TestBrainOS` execution to verify Orleans stream pipelines, state, and middleware dependencies.
3.  **Tier 3 (Static Compiler Promotion)**: Source generation of production-ready `[Neuron]` static C# partial classes using the Incremental Source Generator (`NeuronGenerator`) for native, high-performance compiled execution.

This achieves a production-ready, spec-first, AI-native feedback loop that is completely gated, verifiably self-healing, and performant.

---

## 5. Verification Method

To independently verify this architectural loop and trace the current functional test scenarios, execute the following commands in the workspace:

### A. Run Fast Unit and Compiler Tests
Runs the InoLang keyword parser, Gherkin validation compiler, dynamic compiler stages, and fast in-process loop mock tests:
```powershell
dotnet test --filter "Stage=fast"
```
*Expected Result*: All compiler validation, interpreter, and in-process mock loop tests pass.

### B. Run High-Fidelity Orleans E2E Integration Tests
Runs the full `TestBrainOS` Aspire integration sandboxes, testing dynamic staging, grain stream routing, and static source-generator routing:
```powershell
dotnet test --filter "Stage=e2e"
```
*Expected Result*: The full in-process Orleans silos boot, stage dynamic specifications, run integration scenarios, and pass.

### C. Inspection of Generated Assets
Inspect the generated C# grain class wrappers under the following directory after building the project:
*   `kernel/BrainOS.Core.SourceGen/obj/Debug/netstandard2.0/`
Verify that `NeuronAttribute.g.cs` and `<Namespace>_<ClassName>.g.cs` match the generator specifications defined in `NeuronGenerator.cs`.
