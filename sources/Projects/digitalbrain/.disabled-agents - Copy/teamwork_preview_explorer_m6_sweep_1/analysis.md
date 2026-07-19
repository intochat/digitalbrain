# Analysis of Source Generators & Synapse Consolidation

This report analyzes the procedural source generators in the `kernel/BrainOS.Core.SourceGen` directory, identifies compilation dependencies, evaluates the necessity of `InoTestGenerator.cs`, investigates synapse definitions/registration, and details how to consolidate synapse creation into standard, Named Data Type records mapped directly from InoLang schemas.

---

## 1. Procedural Source Generators (Scan & References)

Within the `kernel/BrainOS.Core.SourceGen` directory, we analyzed the active source generators:
1. **`InoNeuronGenerator.cs`**:
   - **Purpose**: Discovers and parses `.ino` additional files using the embedded InoLang Lexer and Parser, emitting C# Orleans Grain classes (`partial class <NeuronName> : Neuron, INeuronMetadata, IHandle<TSynapse>`).
   - **Key Finding**: In lines 456-591, it defines `EmitSynapseInstantiation`, which contains a massive, hardcoded `switch-case` block mapping specific synapse FQNs (like `BrainOS.Domains.Onboarding.Contracts.OnboardingResult`, `DigitalBrain.SDK.Identity.Contracts.LoginResult`, etc.) to construct C# record instances:
     ```csharp
     switch (targetFqn)
     {
         case "BrainOS.Domains.Onboarding.Contracts.OnboardingResult":
             parameters.Add(("NeedsAccept", "bool", "false"));
             parameters.Add(("CurrentVersion", "string", "null"));
             break;
         ...
     }
     ```
     This hardcoding is verbose, fragile, and represents redundant procedural boilerplate that breaks when schemas or signatures change.
2. **`NeuronGenerator.cs`**:
   - **Purpose**: An incremental analyzer and generator that targets standard C# classes annotated with `[NeuronAttribute]`. It automatically generates Orleans `[ImplicitStreamSubscription]`, constructors, and the underlying `HandleSynapseAsync` routing method mapping incoming `Synapse` subtypes to the concrete class's `Handle` methods.

### Compilation & Project Dependencies
The source generator project `BrainOS.Core.SourceGen.csproj` compiles the parser code directly via linked source files (lines 22-27 of the `.csproj` file). It is referenced as an analyzer (`OutputItemType="Analyzer" ReferenceOutputAssembly="false"`) across multiple projects in `DigitalBrain.slnx`:
- `DigitalBrain.Test\DigitalBrain.Test.csproj`
- `kernel\BrainOS.Domains.Dynamic\BrainOS.Domains.Dynamic\BrainOS.Domains.Dynamic.csproj`
- `kernel\BrainOS.Kernel\BrainOS.Kernel.csproj`
- `kernel\DigitalBrain.Platform.Test\DigitalBrain.Platform.Test.csproj`
- `sdk\DigitalBrain.SDK\DigitalBrain.SDK.csproj`

---

## 2. Analysis of `InoTestGenerator.cs`

- **Purpose**: Generates xUnit test facts by scanning `.ino` scenario blocks for test classes decorated with `[InoTestTarget("filename.ino")]`.
- **References & Usages**:
  It is extensively used to auto-generate standard xUnit test assertions for several test suites in the `DigitalBrain.Test` project, including:
  - `Ai/LlmNeuronProjectionTests.cs` (targets `LlmNeuron.ino`)
  - `Aspire/AspireRuntimeNeuronProjectionTests.cs` (targets `AspireRuntime.ino`)
  - `Developer/DeveloperNeuronProjectionTests.cs` (targets `FileAndDirectory.ino`, `GitHub.ino`, `CodeReviewer.ino`, `SoftwareDeveloper.ino`)
  - `Identity/IdentityProjectionTests.cs` (targets `identity.ino`, `boot_orchestrator.ino`)
  - `Onboarding/OnboardingProjectionTests.cs` (targets `onboarding.ino`)
  - `Travel/TripRadarProjectionTests.cs` (targets `TripPlanner.ino`)
- **Pruning Decision**: **`InoTestGenerator.cs` is STILL highly required.** Pruning it would break compilation or completely disable the generation of dynamic BDD tests, resulting in zero-coverage silent passes. It must be kept intact.

---

## 3. Current Status of Synapse Creation

### Definitions
Synapses are standard C# record classes that inherit from the abstract base class `Synapse` (defined in `kernel/BrainOS.Core/Neurons/Synapse.cs`). They are annotated with Orleans serialization metadata:
```csharp
[Orleans.GenerateSerializer]
public sealed record LoginResult([property: Orleans.Id(1)] bool Success,
    [property: Orleans.Id(2)] string UserId,
    [property: Orleans.Id(3)] string? ErrorMessage,
    [property: Orleans.Id(4)] string? SessionToken
) : Synapse;
```
Synapse definitions are predominantly mapped inside `sdk/DigitalBrain.SDK.Contracts/` across domain-oriented contract folders:
- `Identity/LoginResult.cs`
- `Identity/RequestLogin.cs`
- `Identity/AzureResourceGroupSynapses.cs`
- `Onboarding/OnboardingResult.cs`

### Assembly Registration & Discovery
At runtime, schemas are dynamically discovered via `AssemblyScanningContractCatalog.cs`. This catalog:
1. Walks Assemblies marked with `[assembly: ContractAssembly]`.
2. Registers concrete classes inheriting from `Synapse` as `ContractKind.Synapse` by scanning their public properties via reflection (excluding inherited `Synapse` header properties).
3. Registers concrete classes decorated with `[Signal("FQN")]` as `ContractKind.Synapse`.
4. Registers classes implementing neuron-target interfaces (like `ICallNeuronTarget`) as `ContractKind.Neuron`.

---

## 4. How to Consolidate Synapse Creation

To consolidate synapse creation and eliminate the hardcoded FQN switch-case, we propose a two-phase architecture mapping synapses directly from InoLang schemas and C# record signatures:

### Phase 1: Compile-Time Generation Simplification
Instead of hardcoding synapse parameters in `InoNeuronGenerator.cs`, we should let the generator inspect the C# compilation's types.

1. **Combine Providers**: Update `InoNeuronGenerator.Initialize` to combine `AdditionalTextsProvider` with `CompilationProvider`:
   ```csharp
   var combined = context.AdditionalTextsProvider
       .Where(static file => file.Path.EndsWith(".ino", StringComparison.OrdinalIgnoreCase))
       .Combine(context.CompilationProvider);
   ```
2. **Roslyn Type Inspection**: In the generator output step, retrieve the `INamedTypeSymbol` for the target synapse FQN:
   ```csharp
   var synapseSymbol = compilation.GetTypeByMetadataName(targetFqn);
   ```
3. **Dynamic Constructor Mapping**: Read the parameters of `synapseSymbol`'s primary constructor:
   - Match parameter names (case-insensitive) to InoLang arguments.
   - For each parameter, read its type (`bool`, `string`, `Guid`, collections, etc.) and generate the appropriate C# coercion code:
     - For `bool`: `IsTrue(valStr)`
     - For `Guid`: `Guid.TryParse(valStr, out var g) ? g : Guid.Empty`
     - For collections: `System.Text.Json.JsonSerializer.Deserialize<...>(valStr) ?? new()`
     - For `string`: `valStr`
     - Omitted arguments: emit default parameter values or the type's default value (`null`, `false`, `0`).

This completely removes the switch-case, making the source generator 100% dry and resilient to future schema additions.

### Phase 2: Symmetrical Runtime Factory
For dynamic/interpreted execution paths, implement a symmetrical, reflection-based synapse factory in `BrainOS.Core.Neurons.SynapseFactory` matching the conversion logic in `SynapseBroadcaster.TryConvertBackToSynapse`:
```csharp
public static class SynapseFactory
{
    public static Synapse? CreateSynapse(string fqn, IReadOnlyDictionary<string, string> args)
    {
        // 1. Find Type in loaded assemblies.
        // 2. Reflect on primary constructor parameters.
        // 3. Coerce dictionary strings to parameter types.
        // 4. Invoke constructor and attach headers.
    }
}
```
This guarantees that static (source-generated) and dynamic (interpreted) neurons execute synapse creation identically.
