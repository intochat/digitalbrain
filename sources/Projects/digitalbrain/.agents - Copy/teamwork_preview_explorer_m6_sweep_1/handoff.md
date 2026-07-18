# Handoff Report — Explorer 1 (SourceGen & Synapse)

## 1. Observation
We observed the following files, line numbers, and dependencies:
- **`kernel/BrainOS.Core.SourceGen/InoNeuronGenerator.cs`**:
  - Implements `IIncrementalGenerator` to produce Orleans grain neuron classes from `.ino` files (lines 18-20).
  - Lines 456-591 contain a hardcoded `switch-case` mapping named constructor parameters and defaults:
    ```csharp
    switch (targetFqn)
    {
        case "BrainOS.Domains.Onboarding.Contracts.OnboardingResult":
            parameters.Add(("NeedsAccept", "bool", "false"));
            parameters.Add(("CurrentVersion", "string", "null"));
            break;
    ...
    ```
- **`kernel/BrainOS.Core.SourceGen/NeuronGenerator.cs`**:
  - Emits routing, stream subscription, and constructors for classes carrying `[NeuronAttribute]`.
- **`kernel/BrainOS.Core.SourceGen/InoTestGenerator.cs`**:
  - Discovers classes decorated with `[InoTestTarget]` (line 42) and generates xUnit `[Fact]` test methods from scenario definitions inside matched `.ino` files.
- **`DigitalBrain.slnx`**:
  - Analyzer references to `BrainOS.Core.SourceGen.csproj` exist in `DigitalBrain.Test.csproj`, `BrainOS.Domains.Dynamic.csproj`, `BrainOS.Kernel.csproj`, `DigitalBrain.Platform.Test.csproj`, and `DigitalBrain.SDK.csproj`.
- **`DigitalBrain.Test`**:
  - We found extensive usages of `[InoTestTarget("filename.ino")]` mapping `.ino` scenarios to tests (e.g., `LlmNeuronProjectionTests.cs:10`, `AspireRuntimeProjectionTests.cs:10`, `IdentityProjectionTests.cs:8`, `OnboardingProjectionTests.cs:8`, `TripRadarProjectionTests.cs:13`).
- **`kernel/BrainOS.Kernel/Runtime/AssemblyScanningContractCatalog.cs`**:
  - Automatically registers FQNs to `ContractSchema` and lists fields using assembly reflection on loaded assemblies (lines 178-186) and properties (lines 252-265).
- **`kernel/BrainOS.Kernel/Runtime/SynapseBroadcaster.cs`**:
  - In lines 253-374, `TryConvertBackToSynapse` reflects over a synapse constructor to instantiate typed synapse records from string dictionaries dynamically:
    ```csharp
    var type = GetSynapseType(fqn);
    ...
    var synapse = (Synapse)meta.Constructor.Invoke(ctorArgs);
    ```

---

## 2. Logic Chain
- **Pruning Assessment**:
  1. `InoTestGenerator.cs` generates the tests mapped via `[InoTestTarget]` for multiple critical domains (AI, Aspire, Identity, Onboarding, Travel). (Observation 1)
  2. If `InoTestGenerator.cs` is pruned, the xUnit facts for these tests will never be emitted, causing test coverage to silently drop to zero or build failures due to missing test signatures. (Observation 1, 2)
  3. Therefore, `InoTestGenerator.cs` is strictly required and cannot be pruned.
- **Consolidation Strategy**:
  1. `InoNeuronGenerator.cs` is currently unable to query type signatures because it processes only `AdditionalTextsProvider`, leading to a hardcoded switch-case. (Observation 1)
  2. By combining `AdditionalTextsProvider` with `CompilationProvider`, the generator gains access to the Roslyn `Compilation` semantic model.
  3. Once combined, the generator can dynamically query `compilation.GetTypeByMetadataName(targetFqn)` to obtain constructor parameters, parameter names, and types of the C# synapse record classes. (Observation 1)
  4. The generator can then emit constructor expressions (`new global::FQN(IsTrue(val1), val2)`) without hardcoded mappings, standardizing all synapse instantiations dynamically.
  5. Symmetrically, we can expose a unified runtime `SynapseFactory.CreateSynapse(fqn, args)` matching `SynapseBroadcaster.TryConvertBackToSynapse` to parse string dictionaries into synapse instances for interpreted execution paths. (Observation 1)

---

## 3. Caveats
- We assumed the existing synapse properties mapped in `.ino` files match constructor parameter names on their corresponding C# records. Any casing mismatches are resolved via case-insensitive matching in the compiler and broadcaster.
- No other codebase changes were made since this is a read-only investigation.

---

## 4. Conclusion
1. **Source Generators**: `InoNeuronGenerator.cs` and `NeuronGenerator.cs` are active; `InoTestGenerator.cs` is **highly required** for scenario projection tests and **MUST NOT** be pruned.
2. **Synapses**: All synapses are already declared as record types inheriting from `Synapse` under `sdk/DigitalBrain.SDK.Contracts/` and discovered dynamically via `AssemblyScanningContractCatalog.cs`.
3. **Consolidation**: Consolidating synapse instantiation involves combining `AdditionalTextsProvider` with `CompilationProvider` in `InoNeuronGenerator.cs` to dynamically construct C# instantiation expressions via Roslyn semantic reflection, paired with a symmetrical reflection-based runtime helper `SynapseFactory.CreateSynapse(...)`.

---

## 5. Verification Method
- **Inspect Files**:
  - Confirm the existence of the detailed analysis report at `e:\digitalbrain\.agents\teamwork_preview_explorer_m6_sweep_1\analysis.md`.
  - Confirm the existence of `progress.md` and `BRIEFING.md` heartbeats.
- **Run Build & Test**:
  - Run `dotnet build e:\digitalbrain\DigitalBrain.slnx` to verify clean compilation of all generators.
  - Run `dotnet test e:\digitalbrain\DigitalBrain.slnx` to ensure the generated projection tests pass.
