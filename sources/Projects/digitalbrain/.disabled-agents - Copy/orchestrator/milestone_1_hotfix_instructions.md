# Milestone 1 Hotfix: Clean Robust Exception Handling for BDD & Constructor Views

You are the fresh Worker subagent spawned to implement a correctness and robustness hotfix for Milestone 1.
Your objective is to fix a critical robustness defect identified during independent review.

## The Bug Description
In `UI/flutter/lib/features/neuron_constructor/neuron_constructor_view.dart`:
- Inside `_runBddTests()`, the loading state `_testsRunning` is set to `true` inside a `setState()` call.
- Then, the `client == null` check is performed and an Exception is thrown **outside/before** the `try` block.
- Because the Exception is thrown outside the `try` block, it is never caught by the `catch (e)` block. As a result, the BDD tests button spinner freezes indefinitely in a loading state, and the SnackBar error is never displayed to the user.

## Required Actionable Changes
1. **Target File**: `UI/flutter/lib/features/neuron_constructor/neuron_constructor_view.dart`
2. **Fix `_runBddTests()`**:
   - Move the `client == null` check and throw block **inside** the `try` block, immediately at the start of the `try` block, so that any null client exceptions are cleanly caught by `catch (e)`.
3. **Audit Other Operations**:
   - Review the other interactive methods:
     - `_showCreateCustomSynapseDialog`
     - `_activateNeuron`
     - `_generateWithAutopilot`
     - `_rollbackNeuron`
   - In all of these methods, make sure that the `client == null` resolution check and throwing of Exceptions is located **inside** their respective `try` blocks. This ensures that any offline or null client connection exceptions are properly intercepted, reset their loading flags, and display descriptive error SnackBars.
4. **Validation**:
   - Run `flutter analyze` inside `UI/flutter` to make sure all modified files are 100% clean and free of lints.
   - Run `dotnet build DigitalBrain.slnx` at the workspace root to ensure the solution compiles cleanly with 0 errors.

---

## MANDATORY INTEGRITY WARNING (ZERO TOLERANCE)
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A Forensic Auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

---

## Handoff Requirements
Write `e:\digitalbrain\.agents\worker_m1_hotfix\handoff.md` detailing the changes you made, explaining why they resolve the robustness issues, documenting that `flutter analyze` and `dotnet build` pass with 0 errors, and send a message back to me (the Project Orchestrator) with the handoff path.
