# Slice 7: Flutter Behavior Studio Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the approved Behaviors workspace where non-programmers can understand, run, stop, and request test-driven changes while developers can inspect the real single-file C# source, English scenarios, admission evidence, and immutable revisions.

**Architecture:** Flutter core owns HTTP/SSE models and the typed client. Flutter shell owns the six-view workspace. OS UI projects durable BehaviorNeuron/Task/module user-action state; it does not invent a parallel editor state store. Assistant changes propose scenario diffs first, require approval, then generate code/tests, run admission, and publish only when green.

**Tech Stack:** Dart/Flutter Windows, existing `clients/flutter/core`, `clients/flutter/shell`, OS UI minimal APIs/SSE, DigitalBrain Behavior/AI/Tasks synapses, widget tests, existing theme and workspace chrome.

## Global Constraints

- No embedded terminal, package manager, debugger, or full IDE.
- The default view is intent/evidence, not source.
- `Behavior.feature` is authoritative English behavior; generated overview is read-only.
- Stop must be available immediately and must call the durable activation/cancellation path.
- Use Computer only for final live Flutter interaction. Grok uses Dart MCP and automated Flutter tests.

---

## Six Required Views

1. Library — running/draft/stopped behaviors, purpose, triggers, dependencies, health.
2. Overview — generated explanation, triggers, capabilities, health, Run once, Stop/start, Ask assistant to change.
3. Scenarios — readable Gherkin and per-scenario pass/fail evidence.
4. Assistant change — request, scenario diff approval, implementation, verification, publication.
5. Source + tests — real `Behavior.cs`, `Behavior.feature`, compiler/test/compatibility/security results.
6. Revisions — signed immutable history and restore-as-new-revision.

## Task 1: Extend the durable OS UI projection

**Files:**
- Modify: `os/DigitalBrain.OS.Ui/BehaviorEditorModels.cs`
- Modify: `os/DigitalBrain.OS.Ui/BehaviorEndpoints.cs`
- Modify: `os/DigitalBrain.OS.Ui/BehaviorEditorSurface.cs`
- Modify: `os/DigitalBrain.OS.Ui/FlutterHttpContract.cs`
- Create: `os/DigitalBrain.OS.Ui/BehaviorEventFeed.cs`
- Modify: `os/DigitalBrain.OS.Ui/FlutterHttpHost.cs`
- Modify: `os/tests/DigitalBrain.OS.Ui.Tests/BehaviorEditorEndpoints.cs`
- Create: `os/tests/DigitalBrain.OS.Ui.Tests/BehaviorLibraryEndpoints.cs`
- Create: `os/tests/DigitalBrain.OS.Ui.Tests/BehaviorOperations.cs`

- [ ] CodeGraph existing behavior editor endpoints, snapshots, revision facts, task state, auth projection, SSE helpers, and current Flutter consumers.
- [ ] Add RED tests for list/detail/scenarios/source/admission/revisions and live state transitions.
- [ ] Add RED tests for Run once, Stop, start, and restore-as-new-revision.
- [ ] Add RED tests for listing explicit activation bindings and enabling/disabling a binding through directed control synapses.
- [ ] Use BehaviorNeuron/Tasks journals as source of truth. Do not cache mutable behavior state in the UI server.
- [ ] Keep protected action references opaque and render only module-provided display text/action URL.
- [ ] Run:

```powershell
dotnet test os/tests/DigitalBrain.OS.Ui.Tests -c Release --filter "Behavior"
```

- [ ] Implement the narrow endpoints/event feed and re-run tests.
- [ ] Commit: `feat: project behavior studio state`

## Task 2: Add Flutter core models and client

**Files:**
- Create: `clients/flutter/core/lib/src/behavior_models.dart`
- Create: `clients/flutter/core/lib/src/behavior_client.dart`
- Create: `clients/flutter/core/lib/src/sse_behavior_frames.dart`
- Modify: `clients/flutter/core/lib/digitalbrain_flutter.dart`
- Modify: `clients/flutter/core/lib/src/shell_surface.dart`
- Create: `clients/flutter/core/test/behavior_models_test.dart`
- Create: `clients/flutter/core/test/behavior_client_test.dart`
- Modify: `clients/flutter/core/test/wire_contract_golden_test.dart`

- [ ] Add RED serialization/golden tests for the OS UI contracts.
- [ ] Add client tests for list/read/run/stop/start/change proposal/scenario approval/test/publish/revisions.
- [ ] Add cancellation/disposal tests for SSE subscriptions and in-flight HTTP calls.
- [ ] Implement immutable UI models and client; do not put business decisions in Dart.
- [ ] Run:

```powershell
Set-Location clients/flutter/core
dart analyze
dart test
```

- [ ] Commit: `feat: add flutter behavior client`

## Task 3: Build Library, Overview, and Scenarios

**Files:**
- Create: `clients/flutter/shell/lib/behaviors/behavior_workspace.dart`
- Create: `clients/flutter/shell/lib/behaviors/behavior_library.dart`
- Create: `clients/flutter/shell/lib/behaviors/behavior_overview.dart`
- Create: `clients/flutter/shell/lib/behaviors/behavior_scenarios.dart`
- Create: `clients/flutter/shell/lib/behaviors/behavior_view_model.dart`
- Modify: `clients/flutter/shell/lib/chat/workspace_chrome.dart`
- Modify: `clients/flutter/shell/lib/main.dart`
- Create: `clients/flutter/shell/test/behavior_library_test.dart`
- Create: `clients/flutter/shell/test/behavior_overview_test.dart`
- Create: `clients/flutter/shell/test/behavior_scenarios_test.dart`

- [ ] Add Behaviors beside Chat, Activity, and Brain using existing chrome/theme.
- [ ] Add RED widget tests for empty/loading/error/running/stopping/stopped/draft states.
- [ ] Add tests that purpose/scenarios/capabilities are understandable without opening source.
- [ ] Show each activation binding's source module/synapse, target behavior case/version, enabled state, and opaque source configuration; allow enable/disable without deleting the behavior or binding.
- [ ] Add tests that Stop is visible and confirmation text explains active Task cancellation without deleting the behavior.
- [ ] Implement responsive desktop layouts consistent with the approved prototype direction.
- [ ] Commit: `feat: add behavior library and overview`

## Slice 7A Interim Verification

- [ ] `dotnet test os/tests/DigitalBrain.OS.Ui.Tests -c Release --filter "Behavior"`
- [ ] `cd clients/flutter/core ; dart analyze ; dart test`
- [ ] `cd clients/flutter/shell ; flutter analyze ; flutter test`
- [ ] Verify Library, Overview, Scenarios, activation bindings, and Run/Stop/start wire contracts. Do not attempt Tasks 4–6 or the complete six-view visual gate in the 7A lane.
- [ ] Return the standard handoff so the Wave 2 integrator can accept Slice 7A independently.

## Task 4: Build Assistant change with scenario-first approval

**Files:**
- Create: `src/modules/ai/DigitalBrain.Modules.AI.Contracts/BehaviorAuthoring/IBehaviorAuthor.cs`
- Create: `src/modules/ai/DigitalBrain.Modules.AI.Contracts/BehaviorAuthoring/BehaviorChangeRequest.cs`
- Create: `src/modules/ai/DigitalBrain.Modules.AI.Contracts/BehaviorAuthoring/BehaviorScenarioProposal.cs`
- Create: `src/modules/ai/DigitalBrain.Modules.AI.Contracts/BehaviorAuthoring/BehaviorChangeResult.cs`
- Create: `src/modules/ai/DigitalBrain.Modules.AI/BehaviorAuthoring/BehaviorAuthor.cs`
- Create: `src/modules/ai/DigitalBrain.Modules.AI.Tests/BehaviorAuthoring.cs`
- Modify: `os/DigitalBrain.OS.Ui/BehaviorEndpoints.cs`
- Create: `clients/flutter/shell/lib/behaviors/behavior_assistant_change.dart`
- Create: `clients/flutter/shell/test/behavior_assistant_change_test.dart`

- [ ] Add RED BDD/AI tests: natural-language request returns a feature/scenario diff before source code changes.
- [ ] Add RED UI tests: user can approve/reject scenario changes; code generation cannot start before approval.
- [ ] After approval, use a directed AI behavior-authoring synapse to produce `Behavior.cs`/`Behavior.feature`, then existing propose/test/approve rail.
- [ ] Display compile, scenario, compatibility, capability, and security evidence. Never auto-publish red evidence.
- [ ] Keep model prompts/provider details outside Flutter.
- [ ] Commit: `feat: add scenario-first behavior changes`

## Task 5: Build Source + tests and Revisions

**Files:**
- Create: `clients/flutter/shell/lib/behaviors/behavior_source.dart`
- Create: `clients/flutter/shell/lib/behaviors/behavior_revisions.dart`
- Create: `clients/flutter/shell/lib/behaviors/behavior_evidence.dart`
- Create: `clients/flutter/shell/test/behavior_source_test.dart`
- Create: `clients/flutter/shell/test/behavior_revisions_test.dart`
- Modify: `clients/flutter/shell/test/workspace_test.dart`

- [ ] Show read-only generated overview separately from editable source/feature.
- [ ] Provide deliberate edit mode for the two authored files only.
- [ ] Show stable revision hash/signature/time/status and restore by creating a new verified revision.
- [ ] Add syntax/readability without building an IDE or terminal.
- [ ] Prove failed compile/test leaves active revision unchanged.
- [ ] Commit: `feat: add behavior source and revisions`

## Task 6: Render module user actions

**Files:**
- Modify: `clients/flutter/core/lib/src/sse_authorization_frames.dart`
- Modify: `clients/flutter/shell/lib/activity_screen.dart`
- Modify: `clients/flutter/shell/lib/behaviors/behavior_overview.dart`
- Create: `clients/flutter/shell/lib/user_actions/user_action_card.dart`
- Create: `clients/flutter/shell/test/user_action_card_test.dart`
- Modify: `os/tests/DigitalBrain.OS.Ui.Tests/AuthorizationProjection.cs`

- [ ] Render Google/Salesforce module text and Connect/Authorize button from `UserActionRequired`.
- [ ] Do not expose client secrets, tokens, authorization codes, or raw protected references.
- [ ] Show the owning Task and continuation state.
- [ ] Prove the card disappears/changes state after callback and the same Task resumes.
- [ ] Commit: `feat: surface module user actions`

## Slice Verification

- [ ] `dotnet test os/tests/DigitalBrain.OS.Ui.Tests -c Release`
- [ ] `dotnet test src/modules/ai/DigitalBrain.Modules.AI.Tests -c Release --filter "BehaviorAuthoring"`
- [ ] `cd clients/flutter/core ; dart analyze ; dart test`
- [ ] `cd clients/flutter/shell ; flutter analyze ; flutter test ; flutter build windows`
- [ ] Codex uses Computer to inspect all six views, Stop/start, scenario approval, source/tests, revisions, and auth action.
- [ ] Aspire MCP supplies backend/UI resource health, traces, and logs.
- [ ] Return the standard handoff.
