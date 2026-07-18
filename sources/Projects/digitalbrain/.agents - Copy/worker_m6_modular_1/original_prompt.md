## 2026-05-26T06:42:12Z
You are teamwork_preview_worker.
Your working directory folder is e:\digitalbrain\.agents\worker_m6_modular_1\.
Your identity is "Lead Implementation Worker (Modular) (Worker 2)".

MANDATORY INTEGRITY WARNING:
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A Forensic Auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

Your objective:
Perform the complete implementation sweep for Milestone 6: Domain-Oriented Substrate Reorganization and Tool SDK Unification, following the detailed specifications in your workspace instructions file:
`e:\digitalbrain\.agents\worker_m6_modular_1\instructions.md`

Your tasks:
1. Prune redundant switch-cases from procedural source generators in `kernel/BrainOS.Core.SourceGen/InoNeuronGenerator.cs` (keeping `InoTestGenerator.cs` intact) and consolidate synapse records. Implement a reflection-based dynamic `SynapseFactory` in `BrainOS.Core`.
2. Physically deconstruct monolithic `DigitalBrain.SDK.csproj` and `DigitalBrain.SDK.Contracts.csproj` and reorganize into modular, service-aligned projects under `sdk/` based on their service or vendor (e.g. `sdk/Ai/Llm/Llm.csproj`, `sdk/Collaboration/GitHub/GitHub.csproj`, `sdk/Development/Dotnet/Dotnet.csproj`, `sdk/UI/Flutter/Flutter.csproj`, etc.). Co-locate each neuron's `.ino` spec file directly next to its C# `.cs` sidecar file within its dedicated project folder. Register all 11 new projects in `DigitalBrain.slnx`. Update namespaces and imports throughout the solution.
3. Establish unsealed `LLM : Neuron` (with Microsoft.Extensions.AI chat completions) and `Grok : LLM` (decrypting xai-api-key at runtime via ISecretVault).
4. Introduce core tool neurons `GitHub` (Collaboration), `Dotnet` (Development), and `Flutter` (UI), piping interactive widgets via Remote Flutter Widgets (RFW).
5. Standardize neurons under `INeuron<TState>` / `Neuron<TState>` and introduce a dynamic `NeuronFactory` under `BrainOS.Core` to eliminate Roslyn dynamic compilation overhead.
6. Verify your implementation by compiling the entire solution, updating or adding unit tests for Grok, tool neurons, and state/factory integrations, and running:
   `dotnet test --max-parallel-test-modules 1`
   to ensure all 422+ unified tests pass.
7. Write your completed handoff report to `e:\digitalbrain\.agents\worker_m6_modular_1\handoff.md`.
8. Once complete, call send_message back to parent '58b41f31-e3e4-4b0c-8f2b-adf4991d07eb' to signal completion.
