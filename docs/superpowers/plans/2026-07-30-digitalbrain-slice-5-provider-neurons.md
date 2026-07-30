# Slice 5: Google and Salesforce Intent Neurons Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace public Gmail/Salesforce operation mirroring with small intent-level request/result synapses while keeping MCP tool selection, model planning, OAuth/token state, connection ambiguity, and provider DTOs inside their owning modules.

**Architecture:** `IGmail` and `ISalesforce` are marker neuron contracts. The neuron instance name identifies the module-owned connection; the request payload carries intent, not a shared account registry. Each module uses an internal tool-aware `IChatClient` plus live MCP tool catalog, admits only safe tools/results, and replies with typed result synapses. Missing authorization emits `UserActionRequired` to the owning Task and resumes the same request after authorization.

**Tech Stack:** DigitalBrain.Mcp authorization rail, official Google Gmail MCP endpoint, Salesforce MCP/provider seam, Microsoft.Extensions.AI, Tasks blockers, protected durable tokens, DigitalBrain.Testing.

## Global Constraints

- Do not add `ReadRecentMessages`, `search_threads` wrappers, provider tool names, or OAuth fields to public contracts.
- Do not introduce `AccountSelector`, a shared account registry, or kernel account identity.
- Multiple connections are separate owner-scoped neuron instances and remain module-owned.
- Treat prompts and MCP results as untrusted data. Enforce read/write policy after model planning.
- No secret/token/code/raw MCP content in journals, logs, vector memory, manifests, or handoffs.

---

## Task 1: Replace Gmail's public method with intent synapses

**Files:**
- Modify: `src/modules/google/DigitalBrain.Modules.Google.Contracts/Gmail/IGmail.cs`
- Create: `src/modules/google/DigitalBrain.Modules.Google.Contracts/Gmail/GmailRequest.cs`
- Create: `src/modules/google/DigitalBrain.Modules.Google.Contracts/Gmail/GmailResponse.cs`
- Modify: `src/modules/google/DigitalBrain.Modules.Google.Contracts/Gmail/GmailMessage.cs`
- Modify: `src/modules/google/DigitalBrain.Modules.Google/Gmail/Gmail.cs`
- Modify: `src/modules/google/DigitalBrain.Modules.Google/Gmail/Gmail.Admit.cs`
- Create: `src/modules/google/DigitalBrain.Modules.Google/Gmail/GmailPlanner.cs`
- Create: `src/modules/google/DigitalBrain.Modules.Google.Tests/DigitalBrain.Modules.Google.Tests.csproj`
- Create: `src/modules/google/DigitalBrain.Modules.Google.Tests/GmailIntent.cs`
- Create: `src/modules/google/DigitalBrain.Modules.Google.Tests/GmailAuthorization.cs`
- Integrator-only: add the Google test project to `DigitalBrain.slnx`

- [ ] CodeGraph `IGmail.ReadMessage`, Gmail MCP admission, authorization rail, all callers, and current enrichment tests.
- [ ] Add RED public-surface test: `IGmail` declares no operation methods.
- [ ] Add RED real-module tests for `new GmailRequest("Read my last three emails")` producing a bounded typed response through a fake model and fake MCP edge.
- [ ] Make the fake model choose from a live fake MCP tool list; do not hardcode `search_threads` into the request contract or AI caller.
- [ ] Add prompt-injection/provider-shape tests proving only admitted read-only tools and bounded results are accepted.
- [ ] Add cancellation tests proving the caller/attempt token reaches model planning, MCP enumeration/tool invocation, and result admission without a post-cancellation provider call.
- [ ] Convert Gmail to `IHandle<GmailRequest>`/`IEmit<GmailResponse>` and directed reply semantics.
- [ ] Keep existing exact-message helper only internal if required by migration; delete it in this slice when CodeGraph shows no legitimate caller.
- [ ] Run:

```powershell
dotnet test src/modules/google/DigitalBrain.Modules.Google.Tests -c Release
dotnet test src/core/mcp/DigitalBrain.Integrations.Tests -c Release --filter "Authorization"
```

- [ ] Commit: `feat: expose gmail intent synapses`

## Task 2: Make Google auth interrupt and continue its Task

**Files:**
- Modify: `src/modules/google/DigitalBrain.Modules.Google/Gmail/Gmail.cs`
- Modify: `src/modules/google/DigitalBrain.Modules.Google/GoogleModule.cs`
- Modify: `src/modules/google/DigitalBrain.Modules.Google.Aspire.Hosting/GoogleHostingExtensions.cs`
- Modify: `src/modules/google/DigitalBrain.Modules.Google.Tests/GmailAuthorization.cs`

- [ ] Consume the shared Tasks/MCP user-action rail from the integrated Slice 3 contract. Do not edit or fork the shared authorization rail in this provider slice.
- [ ] Add RED test: missing token yields the minimal Tasks `UserActionRequired` for Google and no provider call.
- [ ] Prove client ID/secret are module setup configuration, while owner OAuth is requested only when the first operation needs it.
- [ ] Prove callback continues the same Task and deterministic operation; no duplicate Gmail call.
- [ ] Add a module-owned ambiguity response for multiple configured Google neuron instances. Do not push account state into the kernel/request.
- [ ] Preserve existing secure token protection and durable identity.
- [ ] Commit: `feat: continue gmail tasks after authorization`

## Task 3: Replace Salesforce public methods with intent synapses

**Files:**
- Modify: `src/modules/salesforce/DigitalBrain.Modules.Salesforce.Contracts/ISalesforce.cs`
- Create: `src/modules/salesforce/DigitalBrain.Modules.Salesforce.Contracts/SalesforceRequest.cs`
- Create: `src/modules/salesforce/DigitalBrain.Modules.Salesforce.Contracts/SalesforceResponse.cs`
- Modify: `src/modules/salesforce/DigitalBrain.Modules.Salesforce.Contracts/SalesforceMutationApproval.cs`
- Modify: `src/modules/salesforce/DigitalBrain.Modules.Salesforce/Salesforce.cs`
- Modify: `src/modules/salesforce/DigitalBrain.Modules.Salesforce/Propose/Propose.cs`
- Modify: `src/modules/salesforce/DigitalBrain.Modules.Salesforce/MutationApproval/SalesforceApproval.cs`
- Create: `src/modules/salesforce/DigitalBrain.Modules.Salesforce/SalesforcePlanner.cs`
- Create: `src/modules/salesforce/DigitalBrain.Modules.Salesforce.Tests/DigitalBrain.Modules.Salesforce.Tests.csproj`
- Create: `src/modules/salesforce/DigitalBrain.Modules.Salesforce.Tests/SalesforceIntent.cs`
- Create: `src/modules/salesforce/DigitalBrain.Modules.Salesforce.Tests/SalesforceAuthorization.cs`
- Integrator-only: add the Salesforce test project to `DigitalBrain.slnx`

- [ ] Add RED public-surface test: `ISalesforce` declares no operation methods.
- [ ] Add model/MCP contract tests for read requests, approval-gated mutation proposals, explicit approval, duplicate approval, and forbidden unapproved mutation.
- [ ] Add cancellation tests for model planning, MCP calls, proposal persistence, and approval execution; cancellation must not turn an unproven mutation into success.
- [ ] Keep proposal and approval as separate directed synapses with stable IDs.
- [ ] Preserve exact provider mutation state internally; return reader-facing typed results.
- [ ] Implement module-owned auth/connection ambiguity using the same Tasks user-action mechanism.
- [ ] Run focused tests and commit: `feat: expose salesforce intent synapses`

## Task 4: Migrate behavior/sample callers and remove provider wrappers

**Files:**
- Modify: `samples/DigitalBrain.AccountEnrichment/`
- Modify: `src/core/mcp/DigitalBrain.Integrations.Tests/AccountEnrichmentBehaviorRail.cs`
- Modify: `os/DigitalBrain.OS.Behaviors/Assistant.cs`
- Modify: `os/tests/DigitalBrain.OS.Behaviors.Tests/ChatTurnUnderBehaviors.cs`
- Modify: `src/DigitalBrain.PublishGate.Tests/Contracts/GoogleVocabulary.cs`
- Create: `src/DigitalBrain.PublishGate.Tests/Contracts/SalesforceVocabulary.cs`

- [ ] Rewrite the account-enrichment behavior/sample to use `brain.Get<IGmail>()`, `GmailRequest`, `brain.Get<ISalesforce>()`, and Salesforce request/approval synapses.
- [ ] Do not recreate old exact-message/account-description APIs as local AI tools.
- [ ] Delete hard-coded enrichment tool glue from `Assistant` once Slice 6 automatic discovery owns capability materialization; if Slice 6 is not yet integrated, isolate the removal commit for that dependency.
- [ ] CodeGraph old method names and delete all migrated wrappers/callers.
- [ ] Commit: `refactor: migrate provider flows to synapses`

## Slice Verification

- [ ] Google, Salesforce, MCP integration, behavior sample, and publish-gate tests pass.
- [ ] `dotnet build DigitalBrain.slnx -c Release`
- [ ] Public API scan shows marker interfaces and intent synapses only.
- [ ] Module tests prove typed requests, module-owned ambiguity, auth suspension/continuation, cancellation, and redacted journals without requiring the not-yet-integrated automatic AI router.
- [ ] After the Wave 2 composition integrator runs, Aspire MCP proves Google/Salesforce resources are healthy and telemetry contains no sensitive content.
- [ ] Defer the end-user “read my last three emails” DigitalBrain MCP proof to Slices 6 and 8, where automatic routing is present.
- [ ] Return the standard handoff.
