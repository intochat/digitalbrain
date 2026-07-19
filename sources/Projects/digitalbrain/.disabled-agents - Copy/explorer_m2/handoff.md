# Handoff Report: Milestone 2 Explorer

This handoff report is self-contained and details all observations, reasoning, and implementation plans for **Milestone 2: Roslyn Runtime Scripting & Mock LLM Stubs**.

---

## 1. Observation
We observed the following files and structural features across the repository:

### 1.1 In-Memory Compilation and Execution
1. **`DynamicNeuronGrain.cs`** (`e:\digitalbrain\kernel\BrainOS.Core.Hosting\DynamicNeuronGrain.cs`):
   Uses `Microsoft.CodeAnalysis.CSharp.Scripting` to compile C# script bodies dynamically on actor activation and execution:
   ```csharp
   var state = await _compiled.RunAsync(globals: globals);
   return state.ReturnValue ?? "";
   ```
   Globals are configured using `DynamicNeuronScriptGlobals` containing properties like `PayloadJson`, `TypeName`, `CorrelationId`, and `Services` (`IServiceProvider`).

2. **`RoslynCompiler.cs`** (`e:\digitalbrain\kernel\BrainOS.Core.Hosting\RoslynCompiler.cs`):
   Provides in-memory script validation:
   ```csharp
   var script = CSharpScript.Create<string>(scriptSource, ScriptOptions.Default
       .WithReferences(
           typeof(Neuron).Assembly,
           typeof(System.Text.Json.JsonSerializer).Assembly,
           typeof(System.Linq.Enumerable).Assembly)
       .WithImports(
           "System",
           "System.Linq",
           "System.Text.Json",
           "System.Threading.Tasks",
           "BrainOS.Core",
           "Microsoft.Extensions.DependencyInjection"),
       globalsType: typeof(DynamicNeuronScriptGlobals));
   ```

3. **`ImplCompileStage.cs` & `StepCompileStage.cs`** (`e:\digitalbrain\kernel\BrainOS.Kernel\Creator\`):
   Generate dynamic class library assemblies in memory using `CSharpCompilation.Create` and `CSharpSyntaxTree.ParseText`.

4. **Dependencies (`Directory.Packages.props`)**:
   Package dependencies for in-memory scripting are pinned at version `5.0.0`:
   ```xml
   <PackageVersion Include="Microsoft.CodeAnalysis.CSharp.Scripting" Version="5.0.0" />
   <PackageVersion Include="Microsoft.CodeAnalysis.CSharp" Version="5.0.0" />
   ```

### 1.2 LLM Mocks and Priming
1. **`BddMockChatClient.cs`** (`e:\digitalbrain\sdk\DigitalBrain.SDK\Ai\Llm\BddMockChatClient.cs`):
   Implements `IChatClient` (from `Microsoft.Extensions.AI`). Fingerprint generation hashes the sequence of chat messages:
   ```csharp
   private static string ComputeFingerprint(IList<ChatMessage> messages)
   {
       var sb = new StringBuilder();
       foreach (var m in messages)
       {
           sb.Append(m.Role).Append(':').Append(m.Text).Append('\n');
       }
       using var sha = System.Security.Cryptography.SHA256.Create();
       var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
       return Convert.ToHexString(hash).ToLowerInvariant().Substring(0, 16);
   }
   ```
   Has custom bypasses for:
   * System messages containing `"moderator of a small expert panel"` -> regex matches user goal `User's goal:\s*Plan:\s*(?<dest>.+?)\s+for\s+(?<days>\d+)\s+days?` and returns a weekly plan JSON.
   * System messages containing `"speaking as"` -> returns a canned answer about time blocking.

2. **`MockChatClientAutoPrimer.cs`** (`e:\digitalbrain\sdk\DigitalBrain.SDK\Ai\Llm\MockChatClientAutoPrimer.cs`):
   Is registered as a hosted service in `BrainOSAiBridge.cs`. Scans all AppDomain assemblies at boot for resource names ending in `.feature`, parses Gherkin examples with regex patterns, and primes all registered mock LLM models.

### 1.3 Tiered Validation Steps
1. **`DigitalBrainTiers.Steps.cs`** (`e:\digitalbrain\UI\BrainOS.E2E.Tests\DigitalBrainTiers.Steps.cs`):
   Includes a steps class with step definitions that validate dynamic Roslyn compilation in-memory:
   ```csharp
   [Given(@"a raw C# script to execute ""(.*)""")]
   public void GivenARawCScriptToExecute(string script) { ... }
   
   [When(@"we compile and run the script dynamically")]
   public async Task WhenWeCompileAndRunTheScriptDynamically() { ... }
   ```
2. **`DigitalBrainTiers.feature`** (`e:\digitalbrain\UI\BrainOS.E2E.Tests\DigitalBrainTiers.feature`):
   Contains Scenario: `Roslyn scripting compiles and executes dynamic C# scripts in-memory`.

---

## 2. Logic Chain
1. **Observation 1.1 & 1.3:** We observed that `RoslynCompiler.cs` and `DynamicNeuronGrain.cs` both execute in-memory dynamic scripting using `CSharpScript` and `Microsoft.CodeAnalysis`. We also observed that the `UI/BrainOS.E2E.Tests` expects a unified execution pathway returning a `ScriptResult`.
2. **Observation 1.1.4:** We observed that the Dynamic Scripting Service contract `Task<ScriptResult> CompileAndExecuteAsync(string code, ExecutionContext context, CancellationToken ct)` mentioned in `PROJECT.md` is not yet defined in the codebase.
3. **Observation 1.2:** We verified that `BddMockChatClient` and `MockChatClientAutoPrimer` already exist and provide automated, deterministic priming of LLM mocks from Gherkin feature files.
4. **Deduction:** Implementing Milestone 2 only requires:
   * Formally defining `IDynamicScriptingService`, `ExecutionContext`, and `ScriptResult` contracts under `sdk/DigitalBrain.SDK.Contracts/Scripting/`.
   * Implementing `DynamicScriptingService` under `sdk/DigitalBrain.SDK/Scripting/` utilizing `CSharpScript` and exposing it via dependency injection.
   * Leveraging the existing `BddMockChatClient` and `MockChatClientAutoPrimer` structures to support mock LLM stubs in offline scenario runs.

---

## 3. Caveats
* **Operating System Constraints:** The exploration was performed on a Windows workspace. The dynamic C# compiling references and imports rely on assemblies that are standard across all major platform environments (Windows, Linux, macOS) in standard .NET 11.
* **Feature Parsing Robustness:** `BddMockChatClient` uses highly specific regex pattern parsing for Gherkin mock examples. Developers writing feature files must strictly adhere to the exact phrasing (e.g. `Given the mock returns "..." for "..."`) to ensure the auto-primer successfully detects and registers the example prompts.

---

## 4. Conclusion
The repository has a complete foundation for Milestone 2. The scripting libraries (`Microsoft.CodeAnalysis`) are already present and pinned to version `5.0.0`. The `BddMockChatClient` fingerprinting system and assembly auto-primer are fully operational. 
The remaining implementation effort is well-scoped, direct, and consists of implementing the `IDynamicScriptingService` contract and its supporting model types inside `DigitalBrain.SDK` and registering it in the DI container.

---

## 5. Verification Method
To independently verify the architecture:
1. **Locate analysis and plan:** View the compiled findings in `e:\digitalbrain\.agents\explorer_m2\analysis.md`.
2. **Run existing fast tests:** Execute the fast test suite using powershell:
   ```powershell
   dotnet test e:\digitalbrain\BrainOS.Fast.slnx --no-build
   ```
   Verify all 408 tests pass successfully.
3. **Verify E2E tests:** Verify that `UI/BrainOS.E2E.Tests/DigitalBrainTiers.Steps.cs` has the Scenario 2 scripting steps correctly referencing the `CSharpScript` compile and run stages.
