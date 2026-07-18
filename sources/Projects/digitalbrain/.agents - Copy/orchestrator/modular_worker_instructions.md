# Lead Worker Instructions - Milestone 6: Substrate Reorganization and Tool SDK Unification (Co-located Spec Edition)

You are the Lead Implementation Worker. Your goal is to implement all codebase modifications and spec co-locations for Milestone 6 to 100% completion under the New Architectural Directive.

## MANDATORY INTEGRITY WARNING
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A Forensic Auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

---

## 1. Scope of Work (New Architectural Directive Alignment)

### 1.1. Pruning Redundant Generators and Consolidating Synapses (R1)
- You have already successfully completed Step 1 (SourceGen build fix) and verified baseline tests. No further files need pruning or modifying in the generator itself.
- All synapses remain standard C# record classes inside `DigitalBrain.SDK.Contracts` mapped to their FQNs.

### 1.2. SDK Architecture: Specifications Co-location (R2 - HALTED & REDESIGNED)
- **HALT & CANCEL the splitting of the SDK into 11 separate projects!**
- Do **NOT** physically re-organize the directories or projects of the SDK. Keep the existing `sdk/DigitalBrain.SDK/` and `sdk/DigitalBrain.SDK.Contracts/` structurally as-is!
- Keep the `CompanyName.*` namespace pattern (e.g. `DigitalBrain.SDK.*`) as-is.
- **Co-locate `.ino` files directly inside `sdk/DigitalBrain.SDK/` next to their C# sidecar files**:
  1. Move/place the existing `sdk/DigitalBrain.SDK/Developer/Specs/GitHub.ino` next to `GitHubNeuron.cs` at `sdk/DigitalBrain.SDK/Developer/GitHub/GitHub.ino`. Delete the old file at `sdk/DigitalBrain.SDK/Developer/Specs/GitHub.ino` if appropriate.
  2. Create/place `DotnetNeuron.ino` next to `DotnetNeuron.cs` under `sdk/DigitalBrain.SDK/Developer/DotnetNeuron.ino` (in the `sdk/DigitalBrain.SDK/Developer/` folder) with the following content:
     ```
     neuron BrainOS.Developer.Specs.DotnetFlows
       "Specs for running dotnet CLI commands within the workspace substrate."

       using request     = synapse(DigitalBrain.SDK.Developer.Contracts.DotnetRequest)
       using responded   = synapse(DigitalBrain.SDK.Developer.Contracts.DotnetResponse)
       using dotnet      = neuron(BrainOS.Developer.DotnetNeuron["workspace-runner"])

       on request:
         let result = ask dotnet to "{request.Command}"
         emit responded(Success: "true", ExitCode: 0, Output: "build success")

     scenario "running dotnet build on the solution"
       given dotnet returns "build success"
       when synapse request(Command: "build")
       then synapse responded emitted with success == "true"
     ```
  3. Create/place `FlutterNeuron.ino` next to `FlutterNeuron.cs` under `sdk/DigitalBrain.SDK/Visuals/FlutterNeuron.ino` with the following content:
     ```
     neuron BrainOS.UI.Specs.FlutterFlows
       "Specs for Remote Flutter Widgets UI layout composition and rendering."

       using rfw_card    = synapse(BrainOS.Kernel.Contracts.Ui.RfwCard)
       using flutter     = neuron(BrainOS.UI.FlutterNeuron["canvas-ui"])

       scenario "rendering dynamic RFW component card"
         given flutter returns "ok"
         when synapse rfw_card(LibraryName: "my_widgets", RootWidget: "MainView", DataJson: "{}")
     ```
  4. Create/place `Grok.ino` next to `Grok.cs` under `sdk/DigitalBrain.SDK/Ai/Llm/Neuron/Grok.ino` with the following content:
     ```
     neuron BrainOS.Ai.Specs.GrokFlows
       "Specs for dynamic DPAPI-protected xAI Grok completions."

       using ask         = synapse(BrainOS.Ai.LlmNeuron.Specs.AskRequest)
       using replied     = synapse(BrainOS.Ai.LlmNeuron.Specs.Replied)
       using grok        = neuron(BrainOS.Ai.GrokNeuron["xai-grok-beta"])

       on ask:
         let reply = ask grok to "{ask.prompt}"
         emit replied(text: reply)

     scenario "ask flows through Grok"
       given grok returns "Grok here, hi!"
       when synapse ask(prompt: "who are you?")
       then synapse replied emitted with text == "Grok here, hi!"
     ```
  5. Verify that `LlmNeuron.ino` is already correctly co-located next to `Llm.cs` under `sdk/DigitalBrain.SDK/Ai/Llm/Neuron/LlmNeuron.ino`.

### 1.3. Cognitive Layer (R3) & Core Tool Neurons (R4) & Unified Contracts (R5)
- All the C# classes for `Llm.cs`, `Grok.cs`, `GitHubNeuron.cs`, `DotnetNeuron.cs`, `FlutterNeuron.cs`, `INeuron<TState>`, `Neuron<TState>`, and `NeuronFactory.cs` are already fully implemented, compiling, and tested! No additional implementation code is required.

---

## 2. Compilation and Verification

1. Perform a solution build using `dotnet build` to ensure everything compiles with 0 errors or warnings.
2. Run the full test suite sequentially:
   ```powershell
   dotnet test --max-parallel-test-modules 1
   ```
   Ensure that all 481+ unified tests pass cleanly.
3. Write your final handoff report (`handoff.md`) in your working directory summarizing:
   - Command lines executed.
   - Build and test results.
   - Attestation of clean integrity verification.
