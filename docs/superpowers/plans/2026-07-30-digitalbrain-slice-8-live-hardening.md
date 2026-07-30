# Slice 8: Integration, Trash Removal, and Live Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Integrate every slice, remove superseded architecture, prove failure/recovery/security paths, and demonstrate the complete DigitalBrain product through its live MCP and Aspire telemetry.

**Architecture:** This slice introduces no new subsystem. It closes migration seams, applies adversarial review findings, adds outcome-focused assembled tests, and collects journal/trace/UI evidence for the approved acceptance criteria.

**Tech Stack:** Full DigitalBrain solution, DigitalBrain.Testing, Aspire testing/product tests, DigitalBrain MCP, Aspire MCP, Flutter Windows, CodeGraph.

## Global Constraints

- Do not paper over failures with retries, sleeps, skipped tests, broader timeouts, or compatibility wrappers.
- Every deletion requires CodeGraph incoming-reference proof and green replacement tests.
- Run only one live integrated AppHost at a time.
- Aspire state/telemetry uses Aspire MCP; Flutter visuals use Computer; product interaction uses DigitalBrain MCP.

---

## Task 1: Run four adversarial read-only reviews

Launch in parallel:

- [ ] Architecture reviewer: compare every approved principle and non-goal to code and public APIs.
- [ ] Security reviewer: OAuth, protected payloads, journals, logs, vector metadata, model/tool prompt injection.
- [ ] Durability reviewer: Tasks, attempt replay, cancellation, crash boundaries, uncertain outcomes, auth continuation.
- [ ] Simplification reviewer: compatibility seams, dead method APIs, duplicate registries, unused loaders/executors, hard-coded tools, excessive abstractions.

Each reviewer must use CodeGraph and return findings with exact file/line, impact, proof, and smallest correction. Reviewers do not edit.

Codex deduplicates and accepts only evidence-backed findings. The Slice 8 writer applies the accepted set.

## Task 2: Delete superseded surfaces

**Candidate files/symbols; delete only when CodeGraph proves unreferenced:**
- Temporary proxy accessor introduced by Slice 1
- Operation methods on `IGmail`, `ISalesforce`, `ITask`, and touched behavior capability interfaces
- `IIntentProgram<TRequest,TResponse>`
- `InProcessBehaviorExecutor`
- In-process behavior host test module if replaced by an explicit fake edge
- Hard-coded `Agent.ToolsFor(...)` override surface
- `CapabilityTool` wrappers replaced by schema materialization
- `EnrichAccountFromEmail` assistant delegate/constants
- Fixed default provider-account constants
- Old method-alias behavior grants

- [ ] Add/retain public-surface tests that fail if these return.
- [ ] Delete in small commits grouped by replacement.
- [ ] After each deletion run the narrow tests and CodeGraph query.
- [ ] Commit: `refactor: remove superseded capability paths`

## Task 3: Add critical assembled BDD/product tests

**Files:**
- Create: `os/tests/DigitalBrain.OS.Bdd.Tests/Features/AutomaticCapabilities.feature`
- Create: `os/tests/DigitalBrain.OS.Bdd.Tests/Steps/CapabilitySteps.cs`
- Create: `os/tests/DigitalBrain.OS.Bdd.Tests/Steps/BehaviorSteps.cs`
- Create: `os/tests/DigitalBrain.OS.Bdd.Tests/Steps/MemorySteps.cs`
- Modify: `os/tests/DigitalBrain.OS.Product.Tests/LiveProduct.cs`
- Create: `os/tests/DigitalBrain.OS.Product.Tests/LiveAutomaticGmail.cs`
- Create: `os/tests/DigitalBrain.OS.Product.Tests/LiveBehaviorStudio.cs`
- Modify: `os/tests/DigitalBrain.OS.Product.Tests/Support/LiveProductAspire.cs`

- [ ] Extend the existing OS BDD project with one thin, outcome-focused feature covering: module discovery without AI edits; Gmail auth continuation on the same Task; exact behavior revision validation; vector-poisoning rejection; stable union-case identity; crash/replay uncertainty; Stop cancellation with retained artifacts/bindings; and scenario-first publication gates.
- [ ] Keep provider/model/vector edges fake in ordinary BDD tests; reserve real providers for explicit live tests.
- [ ] Commit: `test: cover automatic capability product flows`

## Task 4: Full static and test gates

- [ ] `codegraph sync .`
- [ ] `codegraph status .`
- [ ] `git diff --check`
- [ ] `dotnet build DigitalBrain.slnx -c Release`
- [ ] `dotnet test DigitalBrain.slnx -c Release`
- [ ] `cd clients/flutter/core ; dart analyze ; dart test`
- [ ] `cd clients/flutter/shell ; flutter analyze ; flutter test ; flutter build windows`
- [ ] Run targeted searches for prohibited names and inspect every match rather than trusting zero/grep alone.
- [ ] Fix failures through the original responsible slice writer when practical; use the hardening writer only for cross-slice integration defects.

## Task 5: Start/select the integrated product and prove resource health

- [ ] Use `aspire__list_apphosts`; select the integrated AppHost.
- [ ] If none runs, use the single reported CLI bootstrap exception from the umbrella plan.
- [ ] `aspire__refresh_tools`.
- [ ] `aspire__list_resources`; require storage, silo, Tasks/Behavior Host dependencies, UI, MCP host, Qdrant, and model/provider resources needed by the scenario.
- [ ] Restart only changed resources through `aspire__execute_resource_command`.
- [ ] Poll `aspire__list_resources` to healthy.
- [ ] Capture initial structured-log cursors/search window.

## Task 6: Live Gmail and auth continuation proof

- [ ] Generate a fresh command GUID.
- [ ] Call `digitalbrain-mcp__send_chat_message` with “Read my last three emails.”
- [ ] If authorization is required, record the typed user action and use Computer to verify the Flutter button. The owner completes real auth; do not fake or expose credentials.
- [ ] Complete authorization and prove the same durable Task continues. Reuse the original command ID only if the implemented and documented command semantics explicitly require it; do not invent idempotency behavior in the test.
- [ ] `digitalbrain-mcp__list_active_neurons`; identify exact chat, assistant/agent, Task, Gmail, and authorization neurons.
- [ ] Read incoming/outgoing journals with cursors and confirm the directed request/result/user-action chain.
- [ ] Read chat transcript and confirm three results are owner-visible.
- [ ] Use Aspire MCP structured logs/traces and trace-correlated logs. Record trace ID, command ID, Task identity, journal sequence range, and resource names.
- [ ] Confirm no secret or message body appears in redacted journals/telemetry where policy forbids it.

## Task 7: Live behavior authoring and execution proof

- [ ] Use `digitalbrain-mcp__read_behavior` for the selected behavior.
- [ ] Propose a scenario-first change through the Flutter flow.
- [ ] Use DigitalBrain MCP `propose_behavior_revision`, `run_behavior_tests`, and `approve_behavior_revision` with fresh IDs to validate the durable rail.
- [ ] Execute Run once, identify the exact behavior and Task neurons, and read their journals.
- [ ] Stop during an active test behavior and verify `Running → Stopping → Stopped`, activation gate closure, cooperative cancellation, and retained bindings/revisions.
- [ ] Use Aspire MCP to correlate silo/behavior-host traces.

## Task 8: Live vector-memory and discovery proof

- [ ] Store/search user-owned data through a product/community-facing `IVectorMemory` flow.
- [ ] Verify an owner cannot access another owner's namespace.
- [ ] Verify a user/community write to the reserved capability namespace is rejected.
- [ ] Publish a behavior and prove it becomes semantically discoverable.
- [ ] Disable/restart Qdrant resource through Aspire MCP and prove exact catalog fallback remains available while semantic discovery reports degraded state.
- [ ] Restart Qdrant and prove projection reconciliation restores search.

## Task 9: Flutter visual acceptance

Codex, not Grok, uses Computer:

- [ ] Library
- [ ] Overview
- [ ] Scenarios
- [ ] Assistant change and scenario approval
- [ ] Source + tests
- [ ] Revisions
- [ ] Activation binding inspection and enable/disable
- [ ] Stop/start lifecycle
- [ ] Google/Salesforce module setup and user-action card
- [ ] Loading, empty, failure, auth-required, stopping, stopped, and green publication states

Record concrete defects and send them to the responsible Grok writer. Re-run widget and Windows build gates after fixes.

## Task 10: Explicit live oracle and final acceptance matrix

- [ ] `dotnet test os/tests/DigitalBrain.OS.Product.Tests -c Release -- -explicit only`
- [ ] Map evidence to all nine approved acceptance criteria.
- [ ] Run final CodeGraph queries for `ToolsFor`, `ReadRecentMessages`, `KernelTask`, `WorkId`, authored assembly loading, public Qdrant types, and operation methods on touched neuron interfaces.
- [ ] Confirm clean worktrees and list final commits.
- [ ] Commit: `test: harden neuron synapse product flow`

## Completion Evidence

Return:

- Root/Flutter command outputs and counts
- Live DigitalBrain MCP command IDs, neuron IDs, journal cursors/sequences, and transcript outcome
- Aspire MCP resource health, trace IDs, and redacted structured-log findings
- Computer visual acceptance summary
- Deleted trash with CodeGraph proof
- Remaining limitations, if any, stated as observable product behavior rather than vague risk
