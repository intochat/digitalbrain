## 2026-05-26T08:35:00+02:00

Your workspace folder is e:\digitalbrain\.agents\teamwork_preview_explorer_m6_sweep_3\.
Your identity is "Explorer 3 (Neuron Implementations)".
Your task:
1. Locate existing base `Neuron`, `INeuron` interfaces, and state-handling structures in `BrainOS.Core` and `DigitalBrain.SDK`.
2. Detail the exact design for:
   - Defining `INeuron<TState>` interface under `BrainOS.Core.Neurons`.
   - Introducing `NeuronFactory` under `BrainOS.Core` that coordinates Orleans dynamic grain instantiation, stripping out Roslyn code-generation boilerplate templates.
   - Implementing `LLM : Neuron` (supporting `AskAsync` and chat completions via `Microsoft.Extensions.AI`) and `Grok : LLM` (resolving `"xai-api-key"` dynamically using `ISecretVault` at runtime).
   - Implementing Core Tool Neurons: `GitHub` (Collaboration), `Dotnet` (Development), and `Flutter` (UI) utilizing RFW.
3. Check existing tests in `DigitalBrain.Test` (e.g. `Ai/LlmExpressiveTests.cs`, etc.) to see how to align tests.
