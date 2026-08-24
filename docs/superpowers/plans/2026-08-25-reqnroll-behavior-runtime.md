# Reqnroll Behavior Runtime Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a user-authored Gherkin Behavior runtime, scalable shared event ingress, eight executable examples, Gemma-backed reasoning/generation, and a real Flutter Behaviors IDE.

**Architecture:** Reqnroll attributes and its Gherkin dependency define the language surface; a deterministic compiler turns `@behavior` scenarios into durable Orleans subscription plans and executes paired `@test` scenarios against fakes. One deduplicating ingress and partitioned subscription directory fan shared provider events to owner-scoped Behavior runners.

**Tech Stack:** .NET 11, Orleans 10, Reqnroll 3.3.4/Gherkin 35, Microsoft.Extensions.AI, Ollama Gemma 4 + EmbeddingGemma, Aspire, Flutter.

**Spec:** `docs/superpowers/specs/2026-08-25-reqnroll-behavior-runtime-design.md`

## Global Constraints

- Preserve unrelated changes on the original `feat/ai-model-catalog-foundation` checkout.
- All production paths are owner-scoped and reject blank/oversized input.
- Behavior triggers are at-least-once and actions are idempotent by event id plus revision id.
- Tests run offline with deterministic fakes; real Gemma is exercised only in Aspire validation.
- Write a failing automated test before every production behavior change.
- `dotnet test DigitalBrain.slnx` and Flutter analyze/tests must finish green.

---

### Task 1: Language contracts and Reqnroll-compatible compiler

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/Modules/SmartPrompt/Contracts/DigitalBrain.Modules.SmartPrompt.Contracts.csproj`
- Create: `src/Modules/SmartPrompt/Contracts/BehaviorModels.cs`
- Modify: `src/Modules/SmartPrompt/SmartPrompt/DigitalBrain.Modules.SmartPrompt.csproj`
- Create: `src/Modules/SmartPrompt/SmartPrompt/Runtime/BehaviorCompiler.cs`
- Create: `src/Modules/SmartPrompt/SmartPrompt/Runtime/BehaviorStepCatalog.cs`
- Test: `tests/DigitalBrain.Simulation.Tests/SmartPrompt/BehaviorCompilerTests.cs`

**Interfaces:**
- Produces `IBehaviorCompiler.Compile(string)` -> `BehaviorCompilation`.
- Produces immutable `BehaviorPlan`, `BehaviorScenarioPlan`, `BehaviorStepCall`, `BehaviorDiagnostic`, and `BehaviorStepSuggestion` contracts.

- [ ] Write compiler tests for valid paired scenarios, missing `@behavior`/`@test`, unknown and ambiguous steps, line diagnostics, and source hash.
- [ ] Run the focused tests and confirm they fail because the contracts/compiler do not exist.
- [ ] Add Reqnroll 3.3.4 references, Gherkin parsing, reflected binding discovery, limits, and deterministic compilation.
- [ ] Run the focused tests and refactor only while green.
- [ ] Commit the green compiler slice.

### Task 2: Built-in step catalog and eight feature examples

**Files:**
- Create: `src/Modules/SmartPrompt/SmartPrompt/Runtime/BuiltInBehaviorSteps.cs`
- Create: `src/Modules/SmartPrompt/SmartPrompt/Examples/BehaviorExamples.cs`
- Test: `tests/DigitalBrain.Simulation.Tests/SmartPrompt/BehaviorExamplesTests.cs`

**Interfaces:**
- Consumes `BehaviorStepCatalog` and `IBehaviorCompiler`.
- Produces trigger keys, filters, actions, fake event builders, assertions, and eight named example sources.

- [ ] Write a theory requiring all eight examples to compile, contain both tags, bind every step, and expose distinct trigger kinds.
- [ ] Run it and confirm the missing catalog/examples failure.
- [ ] Implement the minimal trusted step metadata and feature sources, including X-to-chart and seven diverse scenarios.
- [ ] Run the focused tests and refactor while green.
- [ ] Commit the examples slice.

### Task 3: Durable definitions, subscription routing, and idempotent execution

**Files:**
- Create: `src/Modules/SmartPrompt/Contracts/IBehavior.cs`
- Create: `src/Modules/SmartPrompt/Contracts/IBehaviorIngress.cs`
- Create: `src/Modules/SmartPrompt/Contracts/BehaviorSynapses.cs`
- Create: `src/Modules/SmartPrompt/SmartPrompt/Runtime/BehaviorEntity.cs`
- Create: `src/Modules/SmartPrompt/SmartPrompt/Runtime/BehaviorIngress.cs`
- Create: `src/Modules/SmartPrompt/SmartPrompt/Runtime/BehaviorTriggerDirectory.cs`
- Create: `src/Modules/SmartPrompt/SmartPrompt/Runtime/BehaviorSubscriptionPartition.cs`
- Create: `src/Modules/SmartPrompt/SmartPrompt/Runtime/BehaviorRunner.cs`
- Modify: `src/Modules/SmartPrompt/SmartPrompt/SmartPromptModule.cs`
- Test: `tests/DigitalBrain.Simulation.Tests/SmartPrompt/BehaviorRuntimeTests.cs`
- Test: `tests/DigitalBrain.Simulation.Tests/SmartPrompt/BehaviorScaleTests.cs`

**Interfaces:**
- `IBehaviorDefinition.Save/Read/Activate/Test` persists immutable revisions.
- `IBehaviorIngress.Publish(BehaviorEvent)` journals/deduplicates and routes.
- Directory/partitions add and remove owner-scoped `BehaviorSubscription` values.
- Runner executes one event/revision idempotently through typed action services.

- [ ] Write failing tests for activation, matching/nonmatching routing, duplicate events, owner isolation, and 5,000 subscriptions sharing one ingress.
- [ ] Run them and confirm missing grain contracts are the failure.
- [ ] Implement entities/neurons, 64 stable partitions, bounded fan-out, revision activation, and deduplication.
- [ ] Run the focused tests and refactor while green.
- [ ] Commit the routing slice.

### Task 4: Chart, chat, fake events, and Gemma reasoning actions

**Files:**
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chart/ChartState.cs`
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chart/IChart.cs`
- Modify: `src/Modules/UI/DigitalBrain.Modules.UI/Chart/ChartEntity.cs`
- Create: `src/Modules/SmartPrompt/SmartPrompt/Runtime/IBehaviorReasoner.cs`
- Create: `src/Modules/SmartPrompt/SmartPrompt/Runtime/GemmaBehaviorReasoner.cs`
- Create: `src/Modules/SmartPrompt/SmartPrompt/Runtime/BehaviorActionExecutor.cs`
- Create: `src/Modules/SmartPrompt/SmartPrompt/Runtime/FakeBehaviorEventCatalog.cs`
- Test: `tests/DigitalBrain.Simulation.Tests/SmartPrompt/BehaviorActionTests.cs`

**Interfaces:**
- Chart gains idempotent `Append(ChartPoint)` and `ChartPoint.Description`/`SourceUri`.
- `IBehaviorReasoner.AnalyzeAsync` uses keyed `IGemma4`; testing mode provides deterministic output.
- `IBehaviorActionExecutor` performs owner-scoped chart/chat actions.

- [ ] Write failing action tests for linked X chart points, duplicate suppression, chat notifications, and deterministic analysis.
- [ ] Run them and confirm the new operations are absent.
- [ ] Implement the minimal action/reasoning/fake infrastructure and register it.
- [ ] Run focused and existing kit tests; refactor while green.
- [ ] Commit the action slice.

### Task 5: Behavior HTTP API and assistant tools

**Files:**
- Create: `src/Kernel/DigitalBrain.Kernel/MapBehaviors.cs`
- Modify: `src/Kernel/DigitalBrain.Kernel/Program.cs`
- Modify: `src/Kernel/DigitalBrain.Kernel/HttpSurfacePaths.cs`
- Create: `src/Modules/SmartPrompt/SmartPrompt/BehaviorToolSource.cs`
- Create: `src/Modules/SmartPrompt/SmartPrompt/GemmaFeatureGenerator.cs`
- Modify: `src/Modules/AI/AI/Assistant.cs`
- Test: `tests/DigitalBrain.E2E.Tests/BehaviorSurfaceTests.cs`
- Test: `tests/DigitalBrain.Simulation.Tests/SmartPrompt/BehaviorAssistantTests.cs`

**Interfaces:**
- Owner API: `GET /behaviors`, `GET/PUT /behaviors/{name}`, `POST .../test`, `POST .../fake`, `GET /behaviors/steps`, `POST /behaviors/generate`.
- Assistant tools: `generate_behavior_feature` and `run_behavior_example`.

- [ ] Write failing HTTP/tool tests for catalog, save validation, test activation, fake execution, generation validation, and owner guards.
- [ ] Run them and confirm endpoints/tools are absent.
- [ ] Implement endpoints, Gemma generator constrained by the catalog, tool source, and behavior-aware assistant instructions.
- [ ] Run focused tests and refactor while green.
- [ ] Commit the API/tool slice.

### Task 6: Flutter client and Behaviors IDE

**Files:**
- Modify: `src/Modules/UI/Flutter/core/lib/src/ui_client.dart`
- Create: `src/Modules/UI/Flutter/core/lib/src/behavior_models.dart`
- Replace: `src/Modules/UI/Flutter/shell/lib/behaviors/behavior_workspace.dart`
- Create: `src/Modules/UI/Flutter/shell/lib/behaviors/gherkin_editor.dart`
- Modify: `src/Modules/UI/Flutter/shell/lib/chat/brain_workspace.dart`
- Modify: `src/Modules/UI/Flutter/shell/lib/chat/brain_chat_screen.dart`
- Modify: `src/Modules/UI/Flutter/shell/lib/chat/brain_chat_composer.dart`
- Test: `src/Modules/UI/Flutter/core/test/ui_client_test.dart`
- Test: `src/Modules/UI/Flutter/shell/test/workspace_test.dart`
- Test: `src/Modules/UI/Flutter/shell/test/chat_surface_test.dart`

**Interfaces:**
- Client methods mirror the owner Behavior API.
- IDE supports select/edit/highlight/complete/save/test/fake-run and displays diagnostics/output.
- Chat composer exposes three sendable Behavior hint chips.

- [ ] Write failing Dart client and widget tests for all controls, syntax spans, suggestions, diagnostics, and hint submission.
- [ ] Run Flutter tests and confirm the static preview/current composer fails expectations.
- [ ] Implement models, client calls, highlighted editor, completions, functional workspace, wiring, and hint chips.
- [ ] Run `dart format`, `flutter analyze`, and focused tests; refactor while green.
- [ ] Commit the Flutter slice.

### Task 7: Aspire configuration and full automated verification

**Files:**
- Modify: `src/Aspire/DigitalBrain.AppHost/AppHost.cs`
- Modify: `tests/DigitalBrain.Aspire.Tests/TopologyConformanceTests.cs`
- Modify: `tests/DigitalBrain.Simulation.Tests/ProductionLlmRegistrationTests.cs`
- Create: `tests/DigitalBrain.E2E.Tests/BehaviorGemmaTests.cs`

**Interfaces:**
- Development defaults pin behavior generation/reasoning to `IGemma4` and embeddings to `IEmbeddingGemma` while retaining the declared model catalog.

- [ ] Write failing conformance tests for explicit local defaults and Gemma-backed behavior services.
- [ ] Run them and confirm configuration/registration is missing.
- [ ] Add the exact AppHost configuration and environment wiring required by the tests.
- [ ] Run `dotnet test DigitalBrain.slnx`, Flutter analyze, and all Flutter tests.
- [ ] Commit the Aspire verification slice.

### Task 8: Real Aspire and Windows Flutter acceptance

**Files:**
- Modify only files required by test-first fixes discovered during acceptance.

**Interfaces:**
- Aspire CLI owns AppHost lifecycle and endpoint discovery.
- Computer Use drives the real Flutter Windows app.

- [ ] Read Computer Use guidance/API/confirmation docs before controlling Windows.
- [ ] Start the AppHost with `aspire start --isolated --apphost src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj` and wait for Ollama, Qdrant, kernel, and Flutter.
- [ ] Verify via Aspire logs/traces that Gemma 4 and EmbeddingGemma are selected.
- [ ] Use Computer Use to open Behaviors, edit/compile/test the X feature, run fake data, and inspect the linked chart.
- [ ] Ask the assistant to generate a ninth feature; verify syntax, compile/test, and activate it.
- [ ] Exercise all eight fake examples and capture failures as failing automated tests before fixes.
- [ ] Re-run the complete automated suite and commit any acceptance fixes.

