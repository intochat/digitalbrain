# Experience Learning MCP Fakes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the chat assistant run and explicitly correct a Salesforce Account Enrichment Experience using immutable Gherkin revisions and protocol-real Gmail/Salesforce MCP fakes.

**Architecture:** Keep Experience as the sole product concept while extending the existing Behavior entity with immutable revisions and learning evidence. Aspire starts provider-shaped fake MCP servers; the Integration module crosses their Streamable HTTP boundary with the official `McpClient`, and Experience actions use the existing transport seams.

**Tech Stack:** .NET 11, Orleans 10, Reqnroll/Gherkin 35, ModelContextProtocol 2.2, ASP.NET Core, Microsoft.Extensions.AI, Aspire 13.5, xUnit v3/Microsoft Testing Platform.

**Spec:** `docs/superpowers/specs/2026-08-28-experience-learning-mcp-fakes-design.md`

## Global Constraints

- Experience is the only user-facing persisted concept; Behavior names remain compatibility internals.
- Each Experience revision is immutable Gherkin and activates only after deterministic green tests.
- Learning is triggered only by an explicit correction and records immutable evidence.
- MCP tools are discovered from the server; do not create Salesforce object DTO catalogs.
- Development uses official Gmail/Salesforce tool names and protocol-real Streamable HTTP.
- Preserve every pre-existing dirty file and unrelated user change.

---

### Task 1: Provider-shaped fake MCP resources and server

**Files:**
- Create: `src/Testing/DigitalBrain.Integrations.Fakes/DigitalBrain.Integrations.Fakes.csproj`
- Create: `src/Testing/DigitalBrain.Integrations.Fakes/Program.cs`
- Create: `src/Testing/DigitalBrain.Integrations.Fakes/GmailFakeTools.cs`
- Create: `src/Testing/DigitalBrain.Integrations.Fakes/SalesforceFakeTools.cs`
- Create: `tests/DigitalBrain.E2E.Tests/FakeIntegrationMcpTests.cs`
- Modify: `tests/DigitalBrain.E2E.Tests/DigitalBrain.E2E.Tests.csproj`
- Modify: `src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.cs`
- Modify: `src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`
- Modify: `tests/DigitalBrain.Aspire.Tests/AppHostContractTests.cs`

**Interfaces:**
- Produces: Streamable HTTP `/mcp` server selected by `FakeMcp:Provider`.
- Produces: official Gmail and Salesforce tool catalogs with deterministic `StructuredContent`.
- Produces: `fake-gmail-mcp` and `fake-salesforce-mcp` Aspire resources in Development.

- [ ] Write an AppHost contract test asserting both fake MCP resources and run it red.
- [ ] Add the fake project, provider-selected MCP server shell, both AppHost resources, HTTP health checks, and Kernel waits; run the AppHost contract green.
- [ ] Write E2E tests that create real `McpClient` sessions, list both catalogs, and call `search_threads`, `soqlQuery`, and `updateRecord` with hand-authored expected JSON; run them red on the empty catalog.
- [ ] Implement the smallest provider tool classes and structured responses.
- [ ] Run the focused tests and verify both real protocol sessions pass.

### Task 2: Generic MCP client and Integration adapters

**Files:**
- Create: `src/Modules/Integrations/Mcp/IMcpIntegrationClient.cs`
- Create: `src/Modules/Integrations/Mcp/McpIntegrationClient.cs`
- Create: `src/Modules/Integrations/Mcp/McpIntegrationOptions.cs`
- Create: `src/Modules/Integrations/Gmail/McpGmailTransport.cs`
- Create: `src/Modules/Integrations/Salesforce/McpSalesforceTransport.cs`
- Modify: `src/Modules/Integrations/DigitalBrain.Modules.Integrations.csproj`
- Modify: `src/Modules/Integrations/IntegrationsModule.cs`
- Create: `tests/DigitalBrain.Simulation.Tests/Integrations/McpIntegrationClientTests.cs`

**Interfaces:**
- Produces: `Task<JsonElement> CallAsync(string server, string tool, IReadOnlyDictionary<string, object?> arguments, CancellationToken)`.
- Consumes: `DigitalBrain:Integrations:{Provider}:Mcp:Endpoint`.

- [ ] Write focused tests proving live discovery rejects an unknown tool and structured content is returned unchanged.
- [ ] Run the tests and verify the missing client/adapters cause the expected failure.
- [ ] Implement one-session-per-call Streamable HTTP transport with deterministic disposal.
- [ ] Register MCP adapters only when endpoints are configured; retain in-process fakes for isolated simulation tests.
- [ ] Run focused and Integration tests green.

### Task 3: Aspire endpoint projection into Integration clients

**Files:**
- Modify: `src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.cs`
- Modify: `src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`
- Modify: `tests/DigitalBrain.Aspire.Tests/AppHostContractTests.cs`

**Interfaces:**
- Produces: endpoint configuration projected into the Kernel resource.

- [ ] Extend the AppHost contract test to assert Kernel endpoint environment variables and run it red.
- [ ] Add endpoint references and environment projection from both existing fake resources to Kernel.
- [ ] Run Aspire contract tests green.

### Task 4: Immutable Experience revisions and learning evidence

**Files:**
- Modify: `src/Modules/SmartPrompt/Contracts/BehaviorModels.cs`
- Modify: `src/Modules/SmartPrompt/Contracts/IBehavior.cs`
- Modify: `src/Modules/SmartPrompt/SmartPrompt/Runtime/BehaviorDefinitionEntity.cs`
- Modify: `src/Modules/SmartPrompt/SmartPrompt/Runtime/BehaviorRunner.cs`
- Modify: `src/Modules/SmartPrompt/SmartPrompt/Runtime/BehaviorTestInterpreter.cs`
- Create: `tests/DigitalBrain.Simulation.Tests/SmartPrompt/ExperienceLearningTests.cs`

**Interfaces:**
- Produces: immutable `BehaviorRevision` and `LearningEvidence` records.
- Produces: `Learn(string correction, string candidateSource)` and `Undo()` compatibility operations.

- [ ] Write simulation tests proving Save cannot interrupt an active revision, failed candidates remain inactive, activation selects an immutable revision, Undo restores its parent, and evidence is owner-scoped.
- [ ] Run the tests and verify the missing revision/evidence behavior fails.
- [ ] Implement candidate append, candidate testing, hash-addressed activation/subscriptions, rollback, and legacy-state fallback.
- [ ] Add candidate-vs-parent regression validation and require red-before-green for learning.
- [ ] Run compiler/runtime/learning tests green.

### Task 5: Salesforce Account Enrichment BDD execution

**Files:**
- Modify: `src/Modules/SmartPrompt/SmartPrompt/Runtime/BuiltInBehaviorSteps.cs`
- Modify: `src/Modules/SmartPrompt/SmartPrompt/Runtime/BehaviorActions.cs`
- Modify: `src/Modules/SmartPrompt/SmartPrompt/Runtime/BehaviorTestInterpreter.cs`
- Modify: `src/Modules/SmartPrompt/SmartPrompt/Examples/BehaviorExamples.cs`
- Modify: `src/Modules/SmartPrompt/SmartPrompt/Examples/FakeBehaviorEvents.cs`
- Modify: `tests/DigitalBrain.Simulation.Tests/SmartPrompt/BehaviorCompilerTests.cs`
- Modify: `tests/DigitalBrain.Simulation.Tests/SmartPrompt/BehaviorRuntimeTests.cs`

**Interfaces:**
- Produces: built-in `salesforce-account-enrichment` Experience and its fake email event.
- Consumes: `IWebSearchTransport` and `ISalesforceTransport` through the existing Integration module.

- [ ] Write compiler tests for enrichment and preservation bindings.
- [ ] Write a runtime test proving an email event creates a chat-visible Salesforce enrichment result.
- [ ] Run both tests red.
- [ ] Add the minimal bindings, example, fake event, and action executor behavior.
- [ ] Run the focused tests green.

### Task 6: Chat Experience tools and transfer proof

**Files:**
- Modify: `src/Modules/SmartPrompt/SmartPrompt/Runtime/BehaviorToolSource.cs`
- Modify: `src/Modules/AI/AI/Assistant.cs`
- Modify: `src/Modules/AI/AI/Testing/TestChatClient.cs`
- Create: `tests/DigitalBrain.Simulation.Tests/SmartPrompt/ExperienceChatTests.cs`

**Interfaces:**
- Produces: `list_experiences`, `run_experience`, `learn_experience`, and `undo_experience_learning` AI tools.
- Consumes: immutable revision and evidence operations from Task 4.

- [ ] Write chat tests for listing, running, explicit correction learning, Undo, and applying the learned preservation rule to a second company.
- [ ] Run tests and verify the new tool names/behavior are absent.
- [ ] Implement the tools and assistant instructions; extend the deterministic chat client only for exact test prompts.
- [ ] Run the chat and transfer tests green.

### Task 7: Full verification

**Files:**
- Modify: `tests/DigitalBrain.E2E.Tests/FakeIntegrationMcpTests.cs`
- Modify: `tests/DigitalBrain.E2E.Tests/McpSurfaceTests.cs`

**Interfaces:**
- Consumes: the complete Aspire, chat, Experience, and MCP path.

- [ ] Start the AppHost with `aspire start` and wait for Kernel, fake Gmail MCP, fake Salesforce MCP, and northbound MCP resources.
- [ ] Run an Experience chat through the real northbound MCP `send_chat_message` tool and assert the fake Salesforce result appears.
- [ ] Inspect Aspire resource state and traces for the cross-resource MCP call.
- [ ] Run `dotnet test DigitalBrain.slnx --no-restore --nologo` and require 0 failures.
- [ ] Re-run focused red-green tests and inspect `git diff --check` plus `git status --short` before handoff.
