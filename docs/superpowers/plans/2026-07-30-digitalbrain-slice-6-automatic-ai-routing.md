# Slice 6: Automatic AI Capability Routing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the assistant discover active modules and published behaviors automatically, retrieve semantic candidates from VectorMemory, validate them against the exact catalog, materialize only relevant directed-synapse tools, and execute them without provider-specific AI code.

**Architecture:** The exact catalog is authoritative. A reserved MemoryModule projection supplies candidate stable IDs. The router removes inactive/incompatible/unauthorized candidates, converts request synapse schemas into generic model functions, and invokes typed neuron references. Provider modules interpret intent internally. Exact fallback remains available when semantic search is degraded.

**Tech Stack:** DigitalBrain AI module, Microsoft.Extensions.AI function calling, active capability catalog, `IVectorMemory`, request/result synapses, Tasks, xUnit v3.

## Global Constraints

- Delete provider-specific capability lists; do not move them to another switch/dictionary.
- Never trust vector metadata as the contract; resolve stable IDs in the exact active catalog.
- Do not give the model inactive, unauthorized, or incompatible schemas.
- Do not place raw user/provider payloads in capability projection text.
- AI capability execution must run through Tasks when it can suspend, retry, or require user action.

---

## Task 1: Build candidate retrieval plus exact validation

**Files:**
- Create: `src/modules/ai/DigitalBrain.Modules.AI/Capabilities/CapabilityRouter.cs`
- Create: `src/modules/ai/DigitalBrain.Modules.AI/Capabilities/CapabilityCandidate.cs`
- Create: `src/modules/ai/DigitalBrain.Modules.AI/Capabilities/ExactCapabilityValidator.cs`
- Create: `src/modules/ai/DigitalBrain.Modules.AI.Tests/AutomaticCapabilityDiscovery.cs`
- Create: `src/modules/ai/DigitalBrain.Modules.AI.Tests/CapabilityCatalogFallback.cs`
- Modify: `src/modules/ai/DigitalBrain.Modules.AI/DigitalBrain.Modules.AI.csproj`

- [ ] CodeGraph `Agent.ToolsFor`, `CapabilityTool`, active catalog consumers, MemoryModule projection, and all test agent subclasses.
- [ ] Add RED test: selecting an active test module in the fixture makes its request synapse discoverable without changing AI code.
- [ ] Add RED tests for inactive, stale-version, poisoned-vector, owner-inaccessible, and unknown candidates.
- [ ] Add RED fallback test with VectorMemory unavailable: exact lookup still resolves explicit module/neuron/synapse terms.
- [ ] Implement semantic search followed by exact validation and bounded candidate selection.
- [ ] Ensure the router itself has no Google/Salesforce/Memory-specific branches.
- [ ] Commit: `feat: discover exact capabilities semantically`

## Task 2: Materialize generic synapse tools

**Files:**
- Modify: `src/modules/ai/DigitalBrain.Modules.AI/Agent.cs`
- Modify: `src/modules/ai/DigitalBrain.Modules.AI/Tools/CapabilityTool.cs`
- Modify: `src/modules/ai/DigitalBrain.Modules.AI/Tools/TurnBoundFunction.cs`
- Create: `src/modules/ai/DigitalBrain.Modules.AI/Capabilities/SynapseCapabilityTool.cs`
- Modify: `src/modules/ai/DigitalBrain.Modules.AI.Tests/CapabilityToolSeam.cs`
- Modify: `src/modules/ai/DigitalBrain.Modules.AI.Tests/CapabilityToolProbes.cs`

- [ ] Add RED tests that only selected candidate schemas become model tools.
- [ ] Assert tool name derives from stable contract identity, descriptions/examples come from the manifest, and arguments validate against the exact schema.
- [ ] Assert execution sends the request synapse to the exact owner-scoped neuron reference and returns only the correlated result synapse.
- [ ] Assert cancellation reaches model streaming, tool invocation, Task, and provider.
- [ ] Replace abstract/manual `ToolsFor(...)` with the router. Keep truly local model-team orchestration only if it is represented as another discoverable neuron/synapse capability.
- [ ] Delete `CapabilityTool` abstractions that become redundant after all tests migrate.
- [ ] Commit: `feat: materialize directed synapse tools`

## Task 3: Route published behaviors

**Files:**
- Modify: `src/modules/ai/DigitalBrain.Modules.AI/Capabilities/CapabilityRouter.cs`
- Modify: `src/core/kernel/DigitalBrain/Capabilities/ActiveCapabilityCatalog.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Runtime/BehaviorNeuron.cs`
- Create: `src/modules/ai/DigitalBrain.Modules.AI.Tests/PublishedBehaviorDiscovery.cs`
- Modify: `src/core/behaviors/DigitalBrain.Behaviors.Tests/BehaviorRailLifecycle.cs`

- [ ] Add RED tests: publish attaches/replaces the exact active behavior descriptor; Stop or deactivation removes it; restart/reconciliation restores only the approved active revision.
- [ ] Add RED test: publishing a behavior inserts its approved description/scenarios/contract IDs into the behavior projection and makes it discoverable.
- [ ] Draft/private/incompatible/stopped behaviors must follow the approved visibility policy.
- [ ] Exact validation must resolve behavior stable IDs from the exact active catalog and bind the active revision plus explicit activation/run-once path; a vector hit must never execute an arbitrary artifact hash.
- [ ] Rebuild the semantic behavior projection from exact catalog entries so stale vector rows cannot create capabilities.
- [ ] Implement and re-run tests.
- [ ] Commit: `feat: discover published behaviors`

## Task 4: Remove DigitalBrain-specific hard-coded assistant tools

**Files:**
- Modify: `os/DigitalBrain.OS.Behaviors/Assistant.cs`
- Modify: `os/DigitalBrain.OS.Behaviors/OSBehaviorsModule.cs`
- Modify: `os/tests/DigitalBrain.OS.Behaviors.Tests/AssistantModelTeam.cs`
- Modify: `os/tests/DigitalBrain.OS.Behaviors.Tests/ChatTurnUnderBehaviors.cs`
- Modify: `src/modules/ai/DigitalBrain.Modules.AI.Tests/OrchestrationL1.cs`

- [ ] Add a failing test that searches assistant code/tool output for `enrich_account_from_email`, Gmail message IDs, Salesforce Account IDs, or provider tool restrictions.
- [ ] Prove the same enrichment behavior is reached through catalog discovery.
- [ ] Reduce `Assistant` to persona/product policy that is not a duplicate capability registry.
- [ ] Delete obsolete enrichment delegates, fixed default account constants, and `CancellationToken.None` provider calls.
- [ ] Commit: `refactor: remove hard-coded assistant capabilities`

## Slice Verification

- [ ] `dotnet test src/modules/ai/DigitalBrain.Modules.AI.Tests -c Release`
- [ ] `dotnet test os/tests/DigitalBrain.OS.Behaviors.Tests -c Release`
- [ ] `dotnet build DigitalBrain.slnx -c Release`
- [ ] CodeGraph proves no provider-specific tool selection remains in AI/OS assistant code.
- [ ] DigitalBrain MCP proves the Gmail path using a fresh command ID.
- [ ] Journal evidence identifies AI, Task, and Gmail neurons; transcript contains the result or user action.
- [ ] Aspire MCP supplies correlated structured logs and trace.
- [ ] Return the standard handoff.
