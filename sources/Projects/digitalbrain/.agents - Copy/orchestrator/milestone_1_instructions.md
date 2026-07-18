# Milestone 1: Diagnose & Offline Fix Implementation Instructions

You are the Worker subagent for Milestone 1 of the DigitalBrain Flutter UI sweep.
Your objective is to safely and cleanly implement the GoogleFonts offline fallback, gRPC Client Scope injection, and crash-resilient canvas and constructor enhancements.

## Detailed Requirements

### 1. Offline Font Fallback in `UI/flutter/lib/main.dart`
- Import `package:google_fonts/google_fonts.dart`.
- Set `GoogleFonts.config.allowRuntimeFetching = false;` immediately after `WidgetsFlutterBinding.ensureInitialized();` inside the `main()` function. This forces GoogleFonts to bypass network calls and fall back to local system fonts if running offline.

### 2. Robust Canvas Initialization
Audit and modify both:
- `UI/flutter/lib/widgets/brain_canvas.dart`
- `UI/flutter/lib/rfw_kit/lib/widgets/brain_canvas.dart`
- In `initState()`, wrap the `widget.content` loading process (`_controller.loadFromContent(...)`) inside a robust `try-catch` block.
- On catch, log the error safely with `debugPrint` and load a clean fallback canvas block (e.g. `_controller.loadFromContent('# Canvas\n*Visual drawing unavailable*', 'fallback.markdraw');`) instead of letting the exception crash widget compilation on cold boot.

### 3. Client Scope Provider Injection
Provide Orleans gateway client propagation inside parent screens:
- **`UI/flutter/lib/features/home/constructor_editor_home_page.dart`**:
  - In `_ConstructorEditorHomePageState`, instantiate a `DigitalBrainGatewayClient` client resolving the endpoint via `resolveKernelEndpoint()` and `createKernelChannel()`. Ensure it is shut down on `dispose()`.
  - Wrap the body of `build()` inside `DigitalBrainClientScope` if the client is active.
- **`UI/flutter/lib/features/brain/brain_scene_screen.dart`**:
  - Locate `NeuronConstructorView` mount (inside the `_constructorOpen` surface).
  - Wrap `NeuronConstructorView` with `DigitalBrainClientScope` using the screen's active `_client`.

### 4. Enhance `NeuronConstructorView` Cold-Activation Resilience
In `UI/flutter/lib/features/neuron_constructor/neuron_constructor_view.dart`:
- Access the `client` via `DigitalBrainClientScope.of(context)` (it may be null or Orleans cluster could be offline).
- If the resolved client is null, display a subtle orange diagnostic warning banner at the top of the pane: `"Orleans Connection Offline - Running in Diagnostic Mockup Mode"`.
- Enhance the try-catch blocks in the following operations:
  - `_runBddTests`
  - `_showCreateCustomSynapseDialog`
  - `_activateNeuron`
  - `_generateWithAutopilot`
  - `_rollbackNeuron`
- Inside each catch block:
  - Call `ScaffoldMessenger.of(context).showSnackBar` to display the verbatim connection error message to the user.
  - Reset the corresponding state loading/running variables (e.g. `_testsRunning = false`, reset scenario statuses, etc.) so that UI controls are immediately re-enabled instead of remaining locked or frozen.

---

## MANDATORY INTEGRITY WARNING (ZERO TOLERANCE)
DO NOT CHEAT. All implementations must be genuine. DO NOT hardcode test results, create dummy/facade implementations, or circumvent the intended task. A Forensic Auditor will independently verify your work. Integrity violations WILL be detected and your work WILL be rejected.

---

## Handoff Requirements
Upon completion, compile your changes, verify that the application compiles cleanly (`flutter pub get` and `flutter build` or run static tools), and write a standard handoff report describing:
- What changes were made and their rationale.
- Complete output showing successful compilation and lack of exceptions.
- Send a completion message back to the Project Orchestrator with the path to your handoff report.
