# Centralized Runtime Implementation Plan

**Goal:** Replace copied provider infrastructure and process-local durable-state protection with one Aspire/Orleans/MAF/MCP architecture.

**Architecture:** Aspire owns one brain application model and durable Azure Storage profile. Internal security and MCP modules hide authenticated encryption and the official MCP SDK behind small module-author interfaces; provider modules retain only semantic and provider policy, while MAF remains the only AI execution engine.

**Tech Stack:** .NET 10, Aspire 13.4.6, Orleans 10.2 journaling, Microsoft Agent Framework 1.13, ModelContextProtocol.Core 1.4.1, xUnit v3.

## Global Constraints

- Write each behavioral proof first and observe the expected failure.
- Contract packages remain provider-, Kernel-, MCP-, and security-free.
- No raw MCP client, tool dictionary, OAuth token, MAF session, or checkpoint crosses a public neuron interface.
- External mutations are fenced durably before invocation and are never automatically repeated after uncertainty.
- AppHost secrets reach `WithReference(brain)` only, never `AsClient()`.

---

### Task 1: Stable durable-payload protection

**Files:**
- Create: `src/DigitalBrain.Security/DigitalBrain.Security.csproj`
- Create: `src/DigitalBrain.Security/DurablePayloadProtection.cs`
- Create: `src/DigitalBrain.Security/AssemblyInfo.cs`
- Modify: `DigitalBrain.slnx`
- Modify: `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`
- Test: `tests/DigitalBrain.Tests/DurablePayloadProtectionContracts.cs`

**Interfaces:**
- Produces: `IDurablePayloadProtector.Protect(string, ReadOnlySpan<byte>)` and `Unprotect(string, ReadOnlySpan<byte>)`.

- [x] Write tests proving round-trip across two providers configured with the same 32-byte key and rejection with a different key, purpose, malformed key, or tampered ciphertext.
- [x] Run `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj` and observe missing-type compilation failures.
- [x] Implement versioned AES-256-GCM envelopes with random 96-bit nonces, 128-bit tags, HMAC-SHA256 purpose derivation, and purpose-bound associated data.
- [x] Run the owning test project and keep it green.

### Task 2: One durable Aspire brain profile

**Files:**
- Modify: `src/DigitalBrain.Aspire.Hosting/BrainHosting.cs`
- Modify: `src/DigitalBrain.Aspire.Hosting/DigitalBrain.Aspire.Hosting.csproj`
- Modify: `hosts/DigitalBrain.AppHost/AppHost.cs`
- Modify: `hosts/DigitalBrain.TestingAppHost/AppHost.cs`
- Test: `tests/DigitalBrain.Tests/IntegrationHostingContracts.cs`

**Interfaces:**
- Produces: `BrainService.WithAzureStorage(IResourceBuilder<AzureStorageResource>)` and `BrainModuleHosting.RequireStateProtection(BrainService)`.

- [ ] Add failing application-model tests for Table clustering/reminders, Blob journal projection, `WaitFor` readiness, one secret protection parameter, and client secret exclusion.
- [ ] Run the owning test project and observe the missing-profile failures.
- [ ] Make `BrainService` own the derived resources and lazily create one secret protection parameter.
- [ ] Make `WithReference(brain)` apply Orleans, journal, readiness, modules, module references, and the protection key; keep `AsClient()` Orleans-only.
- [ ] Convert both AppHosts from the mixed development profile to `WithAzureStorage(storage)`.
- [ ] Run the owning test project and keep it green.

### Task 3: One official MCP client module

**Files:**
- Create: `src/DigitalBrain.Integrations.Mcp/DigitalBrain.Integrations.Mcp.csproj`
- Create: `src/DigitalBrain.Integrations.Mcp/McpClient.cs`
- Create: `src/DigitalBrain.Integrations.Mcp/McpToolContract.cs`
- Create: `src/DigitalBrain.Integrations.Mcp/McpOAuth.cs`
- Create: `src/DigitalBrain.Integrations.Mcp/DurableMcpTokenCache.cs`
- Create: `src/DigitalBrain.Integrations.Mcp/AssemblyInfo.cs`
- Modify: `DigitalBrain.slnx`
- Modify: `tests/DigitalBrain.Simulations/DigitalBrain.Simulations.csproj`
- Test: `tests/DigitalBrain.Simulations/OfficialMcpTransportContracts.cs`

**Interfaces:**
- Produces: `IMcpClientFactory`, `IMcpClient`, `McpServerDefinition`, `McpToolContract`, and `McpToolHandle` as internal friend interfaces/types.

- [ ] Replace provider-type assertions with failing shared-interface tests for configuration-only OAuth, durable encrypted token recreation, exact tool admission, order-insensitive fingerprints, structured results, errors, and cancellation.
- [ ] Run the simulation project and observe missing shared types.
- [ ] Implement a single `HttpClientTransport`/`McpClient` adapter that opens and disposes one official SDK session per operation.
- [ ] Implement OAuth options from provider definitions and the SDK token cache; keep the browser callback in one development adapter with redirect-path/state validation.
- [ ] Run the owning test project and keep it green.

### Task 4: Centralized MCP AppHost parameters

**Files:**
- Create: `src/DigitalBrain.Integrations.Mcp.Aspire.Hosting/DigitalBrain.Integrations.Mcp.Aspire.Hosting.csproj`
- Create: `src/DigitalBrain.Integrations.Mcp.Aspire.Hosting/McpHosting.cs`
- Modify: `DigitalBrain.slnx`
- Modify: `modules/DigitalBrain.Modules.Google.Aspire.Hosting/GoogleHostingExtensions.cs`
- Modify: `modules/DigitalBrain.Modules.Salesforce.Aspire.Hosting/SalesforceHostingExtensions.cs`
- Test: `tests/DigitalBrain.Tests/IntegrationHostingContracts.cs`

**Interfaces:**
- Consumes: `BrainModuleHosting.RequireStateProtection`.
- Produces: one brain reference that registers provider OAuth parameters and projects them once.

- [ ] Add failing tests for provider-scoped parameter names, exactly one shared protection key, duplicate registration rejection, and silo-only projection.
- [ ] Run the owning test project and observe failures against the copied hosting states.
- [ ] Implement the central conditional brain state and reduce each provider extension to one registration call.
- [ ] Run the owning test project and keep it green.

### Task 5: Thin Google and Salesforce provider modules

**Files:**
- Modify: `modules/DigitalBrain.Modules.Google/Gmail.cs`
- Modify: `modules/DigitalBrain.Modules.Google/GoogleModule.cs`
- Modify: `modules/DigitalBrain.Modules.Google/DigitalBrain.Modules.Google.csproj`
- Delete: copied Google MCP boundary, transport, authorization, token-cache, and snapshot files
- Modify: `modules/DigitalBrain.Modules.Salesforce/Salesforce.cs`
- Modify: `modules/DigitalBrain.Modules.Salesforce/SalesforceModule.cs`
- Modify: `modules/DigitalBrain.Modules.Salesforce/DigitalBrain.Modules.Salesforce.csproj`
- Delete: copied Salesforce MCP boundary, transport, authorization, token-cache, and snapshot files
- Test: `tests/DigitalBrain.Simulations/AccountEnrichmentCompositionContracts.cs`

**Interfaces:**
- Consumes: `IMcpClientFactory` and provider-independent tool contracts.
- Preserves: `IGmail` and `ISalesforce` public interfaces.

- [ ] Add a failing proof that a Salesforce proposal performs zero MCP operations and persists one `AwaitingApproval` receipt.
- [ ] Run the simulation project and observe the current catalog calls during proposal.
- [ ] Define provider endpoints/scopes/tool policies beside semantic mapping; construct the shared client once per neuron activation.
- [ ] Remove the unobservable `Proposed`/`Approved` persistence hops; inspect admitted tools only after exact approval and before the durable `Invoking` fence.
- [ ] Run the integration simulations and shared MCP transport contracts.

### Task 6: MAF adapters and durable MAF state

**Files:**
- Modify: `modules/DigitalBrain.Modules.AI/AIModule.cs`
- Modify: `modules/DigitalBrain.Modules.AI/GroupChat.cs`
- Modify: `modules/DigitalBrain.Modules.AI/WorkflowRunner.cs`
- Modify: `modules/DigitalBrain.Modules.AI/OrleansCheckpointStore.cs`
- Modify: `modules/DigitalBrain.Modules.AI/NeuronChatClient.cs`
- Modify: `modules/DigitalBrain.Modules.AI/MafParticipantAdapter.cs`
- Modify: `modules/DigitalBrain.Modules.AI/DigitalBrain.Modules.AI.csproj`
- Delete: `modules/DigitalBrain.Modules.AI/Agent.cs`
- Delete: `modules/DigitalBrain.Modules.AI/MafAgentFactory.cs`
- Modify: `modules/DigitalBrain.Modules.AI.Aspire.Hosting/AIHostingExtensions.cs`
- Test: `tests/DigitalBrain.Tests/AIContracts.cs`
- Test: `tests/DigitalBrain.Simulations/AIOrchestrationContracts.cs`
- Test: `tests/DigitalBrain.Simulations/AIWorkerContracts.cs`

**Interfaces:**
- Consumes: `IDurablePayloadProtector`.
- Preserves: MAF `AIAgent`, `AgentSession`, workflow, and checkpoint ownership.

- [ ] Add failing tests that AI registers stable protection without ASP.NET Data Protection and that one chat-client adapter serves model, agent, and delegated calls.
- [ ] Run the owning tests and observe the old registrations/adapters.
- [ ] Protect serialized MAF sessions and JSON checkpoints with purpose-bound durable protection; derive purposes from owner/definition/lineage.
- [ ] Make the Orleans checkpoint adapter inherit MAF `JsonCheckpointStore`.
- [ ] Delete the consumerless abstract agent path and consolidate the duplicate `IChatClient` implementations.
- [ ] Run all AI unit and simulation tests.

### Task 7: Architecture and full verification

**Files:**
- Modify: `docs/architecture.md`
- Modify: `tests/DigitalBrain.Tests/PackageBoundaryContracts.cs`
- Modify: `tests/DigitalBrain.Tests/PackableProjects.cs`

**Interfaces:**
- Records: shared infrastructure owns mechanics; providers own vocabulary and policy; authenticated edges own interactive production authorization.

- [ ] Update the ratified module, MCP, AI durability, and hosting rules without adding progress prose.
- [ ] Run `aspire start --isolated --non-interactive`, wait for the silo resource, and inspect the application model/logs.
- [ ] Run `dotnet test --logger "console;verbosity=minimal"` from the repository root.
- [ ] Recheck `git rev-parse HEAD` and `git status --porcelain`, inspect the full diff, and stop if unrelated ground movement appeared.
