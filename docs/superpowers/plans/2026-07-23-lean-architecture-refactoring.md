# Lean Architecture Refactoring — Reviewed Implementation Plan

**Date:** 2026-07-23

**Status:** Reviewed planning artifact; no runtime changes made in this session

**Branch investigated:** `agent/gmail-salesforce-enrichment`

**Baseline HEAD:** `f8f8d5642eeb2ec4fccd1babe5ee8fdc89f3a008`

**Merge-base with `master`:** `312ee5993b2b0c4e3e2a145c6f8205f5c1058465`

## 1. Outcome

This plan replaces the pre-CodeGraph draft at this same path.

The architecture should become leaner in four places:

1. Direct AI orchestrations get one durable MAF-session mechanism, while supervised Task/GroupChat checkpointing remains a separate lifecycle.
2. The shared southbound MCP package becomes one concrete mechanism over the official `ModelContextProtocol` client instead of a provider-client imitation layer.
3. Gmail and Salesforce retain provider authority: account identity, exact tools, arguments, semantic mapping, approval, fences, and reconciliation remain in their modules.
4. The production AppHost gains the official Aspire JavaScript/Vite lifecycle, and every normal AppHost build refreshes the repository CodeGraph.

The plan deliberately does **not** add roles, permissions, tenants, account registries, provider routing frameworks, compatibility shims for undeployed branch artifacts, or a second dependency inventory.

No unresolved architectural choice required a grilling question. The ratified decisions, current consumer graph, compiler probes, and first-party API evidence were sufficient. If implementation uncovers contradictory repository evidence, stop that slice and grill only that newly unresolved decision.

## 2. Investigation record

### 2.1 Required inputs read completely

- `AGENTS.md`
- `CLAUDE.md`
- `docs/architecture.md`
- the previous version of this plan
- the requested Grilling, Codebase Design, Writing Plans, Aspire, and Context7 workflows and their task-relevant references

### 2.2 Git baseline

At investigation start:

```text
git rev-parse HEAD
f8f8d5642eeb2ec4fccd1babe5ee8fdc89f3a008

git merge-base master HEAD
312ee5993b2b0c4e3e2a145c6f8205f5c1058465

git status --porcelain
<empty>
```

Re-run all three commands before implementation starts and again at final handoff. If HEAD or merge-base changed, repeat the CodeGraph and diff audit before executing this plan.

### 2.3 CodeGraph proof

The project MCP is live. `codegraph_explore` returned current, line-numbered source and call paths for the changed runtime graph without a staleness or disabled-sync warning.

CodeGraph was used to map:

- every runtime file changed from `master`;
- project and package dependency direction;
- callers and callees of changed types and deletion candidates;
- public contract consumers versus internal implementation consumers;
- AI-to-Tasks direction;
- the two distinct northbound and southbound MCP roles;
- one-implementation abstractions with real cross-module consumers;
- imitation abstractions and wrappers with no independent production consumer;
- zero-consumer probe artifacts.

Important findings were cross-checked with targeted `rg`, project files, the compiler, isolated solution builds, and isolated tests. Do not create a checked-in dependency inventory: CodeGraph remains the architecture oracle.

### 2.4 Build and test baseline

The active workspace was locked by a user-owned running AppHost. It was not stopped or modified.

An isolated archive of the exact investigated HEAD proved:

- `DigitalBrain.slnx` builds with 0 warnings and 0 errors;
- `DigitalBrain.Tests`: 191 passing;
- `DigitalBrain.HostTests`: 6 passing;
- `DigitalBrain.Simulations`: 167 passing.

The docs baseline proved:

- `npm ci` succeeds;
- `npm run build` succeeds with VitePress 1.6.4;
- `node tools/render-specification.mjs` succeeds;
- `node --test tests/*.test.mjs` currently has 2 failures:
  - the package table omits `DigitalBrain.Security`;
  - the site test expects 47 ratified checklist items while architecture now contains 48.

Those existing docs failures are included in a dedicated documentation slice rather than concealed in the final gate.

### 2.5 Current API evidence

Context7 and compiler probes verified the current packages used by this repository:

| Area | Repository version | Verified fact that constrains this plan |
|---|---:|---|
| Aspire | 13.4.6 | `Aspire.Hosting.JavaScript` 13.4.6 matches the AppHost SDK; `AddViteApp(builder, name, appDirectory, runScriptName = "dev")` exists. |
| Aspire CLI | 13.4.6 | `aspire start` builds by default; `--no-build` skips; `--isolated`, `--format Json`, and `--non-interactive` exist. |
| MAF | 1.13.0 | `AIAgent` supports create, serialize, deserialize, and run with `AgentSession`; concurrent workflows are built by `AgentWorkflowBuilder.BuildConcurrent`; workflows can be exposed as agents. |
| MCP SDK | 1.4.1 | `McpClient.CreateAsync`, `ListToolsAsync`, `McpClientTool.CallAsync`, OAuth discovery/PKCE/token refresh/authenticated retry, and transport ownership APIs compile as proposed. |
| Orleans | 10.2.2-rc.2 | Current grain persistence and interleaving attributes compile; supervised checkpoint state remains an Orleans concern. |
| Vite/VitePress | VitePress 1.6.4 | The existing `dev` script accepts Aspire-provided host/port arguments; the official JavaScript resource owns package installation and process lifecycle. |
| Google hosted MCP | Developer Preview | Gmail endpoint and `get_message` contract are provider facts; Context7 lacked the hosted-MCP reference, so the first-party Google reference was used. |
| Salesforce hosted MCP | Current first-party reference | Endpoint, PKCE, optional blank client secret, scopes, `soqlQuery`, and `updateSobjectRecord` names/arguments constrain provider policy. |

Exact .NET signatures were also compiled in a temporary probe. Repeat a focused compiler probe during implementation if package versions change.

## 3. Concise current-state diagnosis

The branch has the right high-level cuts but several shallow mechanisms.

- `Concurrent` correctly encapsulates MAF concurrent group chat, but direct invocations create a fresh `AgentSession` and do not durably manage it.
- Direct `GroupChat` durably stores a MAF session, but persistence, compatibility, and rollback logic live in orchestration-specific files and are not shared with `Concurrent`.
- Supervised Task/GroupChat uses a different workflow-run/checkpoint lifecycle, correctly, but definition drift, cancellation, and checkpoint-write rollback are incomplete.
- `DigitalBrain.Integrations.Mcp` is the right southbound shared module, but its public client/factory façade, private session wrapper, and generic tool-contract layer mirror the official SDK without creating an authority boundary.
- Gmail and Salesforce correctly expose semantic contracts, but current provider implementations open repeated sessions, list tools repeatedly through the imitation layer, and validate incomplete provider contracts.
- Salesforce has the right approval/fence/reconciliation vocabulary, but durable save rollback, cancellation behavior, official tool naming, and uncertain-outcome handling need hardening.
- Named Neuron identity reaches token protection today, despite a misleading local variable name. The missing behavior is explicit account selection and idempotency in the sample, not a new registry.
- `Directory.Build.targets` initializes CodeGraph once behind a sentinel and explicitly skips AppHost projects. Therefore a normal `aspire start` does not satisfy the required AppHost-build refresh invariant.
- Both configured project MCP entry points already work. The old plan’s CodeGraph-wrapper repair is stale and must not be implemented.
- Production AppHost has no website resource. The docs are already a VitePress app with a lockfile and suitable `dev` script, so the official Aspire JavaScript lifecycle is sufficient unless a clean-source live proof contradicts it.

## 4. Dependency direction that must survive

```text
DigitalBrain.Modules.AI
  -> DigitalBrain.Modules.AI.Contracts
  -> DigitalBrain.Abstractions
  -> DigitalBrain.Kernel
  -> DigitalBrain.Security
  -> Microsoft Agent Framework / Microsoft.Extensions.AI

DigitalBrain.Modules.AI.Contracts
  -> DigitalBrain.Abstractions
  -> DigitalBrain.Modules.Tasks.Contracts
  -> Microsoft.Extensions.AI.Abstractions

DigitalBrain.Modules.Tasks
  -> DigitalBrain.Modules.Tasks.Contracts
  -> DigitalBrain.Kernel

DigitalBrain.Modules.Tasks.Contracts
  -> DigitalBrain.Abstractions

DigitalBrain.Modules.Google
  -> DigitalBrain.Modules.Google.Contracts
  -> DigitalBrain.Integrations.Mcp
  -> DigitalBrain.Kernel

DigitalBrain.Modules.Salesforce
  -> DigitalBrain.Modules.Salesforce.Contracts
  -> DigitalBrain.Integrations.Mcp
  -> DigitalBrain.Kernel

Provider Aspire.Hosting projects
  -> their provider project
  -> DigitalBrain.Aspire.Hosting
  -> DigitalBrain.Integrations.Mcp.Aspire.Hosting

hosts/DigitalBrain.Mcp
  -> public DigitalBrain/AI contracts
  -> MCP server packages
  (northbound; never depends on southbound provider mechanics)
```

Forbidden reverse edges:

- Tasks or Tasks.Contracts to AI, MAF, MCP, Google, or Salesforce;
- provider contracts to MCP SDK types;
- Google to Salesforce or Salesforce to Google;
- the northbound MCP host to southbound provider implementations;
- shared MCP mechanics to provider semantics or approval policy.

## 5. Keep / delete / deepen / move

### 5.1 Scoped projects

| Project or file | Decision | Evidence and intended boundary |
|---|---|---|
| `modules/DigitalBrain.Modules.AI` | **Deepen** | Real production module. Share direct durable MAF-session mechanics; separately harden supervised lifecycle. |
| `modules/DigitalBrain.Modules.AI.Contracts` | **Keep** | Public MEAI substrate and one-way Tasks.Contracts bridge have real consumers. Keep MAF types internal. |
| `modules/DigitalBrain.Modules.Tasks` | **Keep** | Independent task runtime with real consumers. No AI dependency. |
| `modules/DigitalBrain.Modules.Tasks.Contracts` | **Keep** | Public task vocabulary is consumed by AI contracts and task runtime. |
| `modules/DigitalBrain.Modules.Google` | **Deepen and shrink** | Retain account configuration, exact Gmail policy, arguments, mapping; call official MCP client within the shared runtime callback. |
| `modules/DigitalBrain.Modules.Google.Contracts` | **Keep** | `IGmail` and `GmailMessage` are the semantic boundary. No raw MCP types. |
| `modules/DigitalBrain.Modules.Salesforce` | **Deepen and shrink** | Retain exact tools, semantic mutation, approval, fences, reconciliation; remove fake provider-client layer. |
| `modules/DigitalBrain.Modules.Salesforce.Contracts` | **Keep** | Public semantic mutation and approval boundary. No raw MCP types. |
| `src/DigitalBrain.Integrations.Mcp` | **Deepen and shrink** | Keep southbound shared mechanics, but expose one concrete internal runtime over the official SDK. |
| `src/DigitalBrain.Integrations.Mcp.Aspire.Hosting` | **Keep** | `McpProviderHostingDefinition`/hosting mechanics have two real provider consumers. |
| `src/DigitalBrain.Security` | **Keep** | Shared encrypted durable-payload boundary has five production consumers. |
| `src/DigitalBrain.Aspire.Hosting` | **Keep** | Real shared AppHost/module composition boundary. |
| `hosts/DigitalBrain.AppHost` | **Deepen** | Add official website resource and package reference; remove stray probe artifact. |
| `hosts/DigitalBrain.Host` | **Keep** | Production runtime host. |
| `hosts/DigitalBrain.Mcp` | **Keep** | Separate northbound exposure of Neurons. Do not merge with southbound MCP clients. |
| `hosts/DigitalBrain.ProbeHost` | **Keep** | Current probe host has test/tooling consumers; only validation-owned processes are cleaned up. |
| `hosts/DigitalBrain.TestingAppHost` | **Keep** | Existing integration-test composition. Production website model is tested through the production AppHost, not copied here without a consumer. |
| `samples/DigitalBrain.AccountEnrichment` | **Deepen** | Make Gmail account selection explicit and make process-level execution durable/idempotent. |
| `Directory.Build.targets` | **Replace mechanism** | Remove one-time sentinel and AppHost exclusion; initialize/sync CodeGraph during every normal AppHost build. |
| `.mcp.json` | **Keep unchanged** | Current project MCP is live. |
| `.codex/config.toml` | **Keep unchanged** | Current project MCP is live. |
| `docs` VitePress application | **Keep; host officially** | Existing app, `dev` script, and lockfile fit `AddViteApp`; no custom npm resource. |

### 5.2 Important types

| Type / file | Decision | Consumer-based rationale |
|---|---|---|
| `Concurrent` | **Keep; deepen** | Ratified public behavior. It must durably manage direct MAF `AgentSession`. |
| `GroupChat` | **Keep; deepen** | Direct public behavior with real consumers. Delegate session mechanics to the new internal module. |
| `GroupChatWorkflow` | **Keep** | Centralizes the exact round-robin workflow definition used by direct and supervised paths; deleting it would duplicate definition logic. |
| `MafParticipantAdapter` | **Keep internal** | Required MEAI-to-MAF adapter used by concurrent, group chat, and runner. |
| `NeuronChatClient` | **Keep internal** | Required internal adapter with real AI consumers. |
| `WorkflowRunner` | **Deepen** | Own supervised run cancellation and definition checks; it is not a direct-session wrapper. |
| `OrleansCheckpointStore` | **Deepen** | Add transactional in-memory rollback around failed state writes and definition-bound protection. |
| `SessionCompatibility` | **Delete after move** | Its stable compatibility responsibility moves into the direct-session module; current `ModuleVersionId` component is too volatile. |
| `OrchestrationState` | **Delete after move** | Split stable shared definition records into a definition file; move direct envelope into direct-session ownership. |
| `OrchestrationParticipant` / `OrchestrationDefinition` | **Move** | Shared definition vocabulary belongs in `OrchestrationDefinition.cs`, separate from persistence envelopes. |
| `DirectAgentSession` (new internal type) | **Add, deep module** | One current consumer family—direct `Concurrent` and `GroupChat`—needs a single durable session protocol. |
| `IMcpClientFactory` | **Delete** | Only one production implementation; tests fake the abstraction instead of exercising protocol mechanics. |
| `IMcpClient` | **Delete** | Mirrors official client and leaks a shallower, string-based tool surface. |
| `SdkMcpClientFactory` | **Delete** | No alternate production transport or policy consumer. |
| `SdkMcpClient` | **Delete** | Pass-through wrapper over the official client. |
| private `McpSession` wrapper | **Delete** | Lifetime can be owned by one concrete runtime method with lexical disposal. |
| `McpRuntime` (replacement concrete type) | **Deepen** | Own HTTP, official transport/client, OAuth, durable token cache, authenticated lifecycle, and callback scope. |
| `McpServerDefinition` | **Keep internal** | Cohesive data needed by the concrete runtime. |
| `DurableMcpTokenCache` | **Keep; deepen** | Required official `ITokenCache` adapter; add failed-commit rollback. |
| `McpOAuth` | **Keep private; correct** | Loopback authorization is complex shared mechanics. SDK, not this code, owns discovery, state creation, PKCE, bearer, refresh, and retry. |
| `IMcpAuthorizationRedirect` | **Delete** | Public redirect seam has only reject/local/test-stub consumers and no deployed alternate authority. |
| rejecting redirect implementation | **Delete** | Replace with explicit internal fail-closed selection from provider configuration. |
| `McpToolContract` | **Delete** | Generic provider-policy abstraction has no independent authority consumer. |
| `McpToolFingerprint` (internal function/type) | **Add narrowly** | Canonical shared mechanics for name, schemas, and all safety annotations; providers still own acceptance policy. |
| `McpProviderHostingDefinition` | **Keep** | Shared by Gmail and Salesforce hosting. |
| `IDurablePayloadProtector` | **Keep** | One implementation but many cross-module consumers; it is a real security/DI boundary. |
| `IGmail` / `GmailMessage` | **Keep public** | Semantic provider boundary. |
| `ISalesforce` / mutation / approval types | **Keep public** | Semantic approval and mutation boundary. |
| `IDigitalBrain` | **Keep public** | Real northbound MCP host consumer. |
| `AttemptWaiting` / `AttemptFailed` task vocabulary | **Keep** | Current contract consumers exist; incomplete production producers do not make public vocabulary orphaned. |
| `hosts/DigitalBrain.AppHost/_probe_exec.cs.txt` | **Delete** | Stray zero-consumer probe artifact. |

## 6. Concrete expected deletions

Delete these branch artifacts once their replacement tests are green:

- `src/DigitalBrain.Integrations.Mcp/McpClient.cs` in its current factory/interface/wrapper form; replace it with the concrete runtime implementation rather than preserving the public façade.
- `src/DigitalBrain.Integrations.Mcp/McpToolContract.cs`.
- the public `IMcpAuthorizationRedirect` and rejecting redirect implementation currently housed in `src/DigitalBrain.Integrations.Mcp/McpOAuth.cs`.
- private session-wrapper code made redundant by lexical lifetime ownership.
- `modules/DigitalBrain.Modules.AI/SessionCompatibility.cs`.
- `modules/DigitalBrain.Modules.AI/OrchestrationState.cs`, after moving only the live definition vocabulary and direct envelope.
- test doubles in `tests/DigitalBrain.Simulations/McpTestDoubles.cs` that implement the deleted client/factory surface, including recording Gmail/Salesforce client façades.
- `hosts/DigitalBrain.AppHost/_probe_exec.cs.txt`.
- obsolete tests that assert imitation abstractions rather than public behavior or the official MCP protocol boundary.

Already-deleted branch artifacts such as `Agent.cs`, `MafAgentFactory.cs`, and obsolete PublicAPI baselines are not work items. Do not recreate or “delete” them again.

## 7. Resulting shapes from a consumer’s perspective

These are shape constraints, not copy-paste implementations. Verify exact accessibility and signatures with the compiler in each slice.

### 7.1 Public AI substrate

Public callers continue to use the existing `IAgent` contract and Microsoft.Extensions.AI messages:

```csharp
public partial interface IAgent : INeuron
{
    Task<ChatResponse> Respond(
        IReadOnlyList<ChatMessage> messages);
}
```

`Concurrent : Neuron, IAgent` and `GroupChat : Neuron, IGroupChat` retain their current public abstract-class roles. `IGroupChat` remains the composition of `IAgent` and `IWorker`; do not invent a parallel `IConcurrent` contract.

Do not expose `AgentSession`, `AIAgent`, `Workflow`, checkpoints, or other MAF types publicly. Do not broaden these public contracts solely to add cancellation in this plan.

Internally, both direct orchestrations use one durable module:

```csharp
internal sealed class DirectAgentSession
{
    internal Task<IReadOnlyList<ChatMessage>> RunAsync(
        OrchestrationDefinition definition,
        Func<AgentSession?, CancellationToken, Task<DirectAgentRunResult>> run,
        CancellationToken cancellationToken);
}
```

The exact callback/result form may be simplified during TDD, but the module must own:

- the one protected durable value;
- stable definition fingerprinting;
- create versus deserialize;
- serialize-after-success;
- state write and rollback;
- an explicit incompatible-state failure.

`Concurrent` owns the concurrent workflow definition. `GroupChatWorkflow` owns the round-robin definition. Neither class owns encryption or persistence choreography.

### 7.2 Shared southbound MCP mechanics

Provider modules should see the official client only inside a bounded callback:

```csharp
internal sealed class McpRuntime
{
    internal ValueTask<TResult> RunAsync<TResult>(
        McpServerDefinition server,
        IDurableValue<byte[]> tokenState,
        Func<ValueTask> commit,
        string durableIdentity,
        Func<ModelContextProtocol.Client.McpClient,
            CancellationToken,
            ValueTask<TResult>> operation,
        CancellationToken cancellationToken);
}
```

Shape constraints:

- no `IMcpClient`;
- no `IMcpClientFactory`;
- no provider-specific fake client;
- no returned session/connection wrapper;
- one `HttpClient` owned lexically by the runtime;
- `HttpClientTransport` constructed with explicit external-client ownership;
- one official `McpClient`, asynchronously disposed;
- SDK-owned OAuth discovery, authorization URI/state, PKCE, bearer injection, refresh, authenticated retry, list, and call;
- durable token cache scoped by the full named Neuron identity;
- official client cannot escape the callback.

`McpToolFingerprint` is a pure canonicalization mechanic over:

- tool name;
- input schema;
- output schema;
- read-only, destructive, idempotent, and open-world annotations.

It does not decide whether Gmail or Salesforce accepts a tool.

### 7.3 Gmail consumer

Public shape stays semantic:

```csharp
IGmail gmail = GrainFactory.GetGrain<IGmail>(
    NeuronId.For<IGmail>(owner, "myemail@gmail.com").ToGrainId());
GmailMessage message =
    await gmail.ReadMessage(messageId, cancellationToken);
```

The Gmail implementation:

1. opens one official-client callback;
2. lists tools;
3. admits exactly `get_message` with the required schema/output/annotations;
4. calls the selected `McpClientTool` with `messageId` and `messageFormat = "FULL_CONTENT"`;
5. rejects protocol errors and malformed structured output;
6. maps only to `GmailMessage`.

Raw MCP results, schemas, annotations, and dictionaries never cross `IGmail`.

### 7.4 Salesforce consumer

Public proposal and approval remain semantic and separate:

```csharp
SalesforceAccountDescriptionMutation proposal =
    await salesforce.ProposeAccountDescription(
        commandId,
        requester,
        accountId,
        description,
        cancellationToken);

SalesforceAccountDescriptionMutation receipt =
    await salesforce.ApproveAccountDescription(
        approval,
        approvalEvidence,
        cancellationToken);
```

Provider facts:

- mutation tool: `updateSobjectRecord`;
- query tool: `soqlQuery`;
- update arguments: `sobject-name`, `id`, and `body`;
- OAuth scopes: `mcp_api refresh_token`;
- configured client secret is optional for PKCE/public-client scenarios.

The first update connection only admits tools and records an `Invoking` fence. A fresh connection re-lists, matches the stored fingerprint, and invokes the mutation. Any outcome not proven successful is reconciled through one fresh, bounded query connection; the update is never retried.

### 7.5 Account-enrichment sample

The command becomes account-explicit:

```csharp
public sealed record EnrichAccountFromEmail(
    CommandId CommandId,
    string MessageId,
    string AccountId,
    string GmailAccount) : Synapse;
```

Append `GmailAccount` as Orleans field ID 3 so the existing field IDs 0–2 remain stable. The sample resolves exactly `IGmail(GmailAccount)`. Its durable request fingerprint includes all four values. Exact replay is a no-op; a reused command ID with different input fails before provider calls.

No provider-account registry or routing service is introduced.

## 8. Public API and durable-state effects

### 8.1 Public API effects

Intended public change:

- add `GmailAccount` to the sample command.

Intended unchanged public surfaces:

- `IGmail` returns `GmailMessage`;
- `ISalesforce` returns semantic proposal/receipt types;
- AI public messages remain MEAI `ChatMessage`;
- MAF and raw MCP types remain internal;
- Tasks remains independent;
- the northbound `DigitalBrain.Mcp` host remains separate.

Intended removals are internal or undeployed PR artifacts:

- MCP client/factory/redirect seams;
- internal compatibility/session wrappers;
- generic tool-contract abstraction.

Before each slice goes green, run the repository’s public API and package-boundary tests. If a proposed deletion is actually public in a shipped package baseline, stop and reconcile that evidence rather than silently editing the baseline.

### 8.2 Durable-state migration

This branch is undeployed, so do not write compatibility decoders for its consumerless state formats.

Direct AI sessions:

- use a new protection purpose/version tied to a stable definition fingerprint;
- reject old or incompatible direct-session payloads with a clear reset/migration message;
- do not use `ModuleVersionId`, which changes across byte-identical rebuilds;
- bind compatibility to orchestration kind, ordered participants, MAF assembly identity, execution environment, and manager/aggregator settings.

Supervised workflow checkpoints:

- remain a separate state format and lifecycle;
- bind protected checkpoint payloads to the concrete workflow-definition fingerprint;
- reject definition drift before checkpoint read/adoption/invocation;
- preserve cancellation fences across reminders/restarts.

MCP OAuth tokens:

- retain the current protection-purpose identity format because it already includes the full Neuron ID;
- rename misleading local variables and add separate-name isolation tests;
- roll in-memory token state back if the durable commit fails;
- do not migrate tokens just to satisfy a rename.

Salesforce ledger:

- version the new fence/fingerprint/uncertain-outcome shape;
- no compatibility decoder for undeployed prior branch entries;
- roll in-memory state back on failed writes.

Sample process:

- introduce a versioned request fingerprint and terminal receipt;
- old branch-only entries may fail with an explicit reset/migration message;
- exact replay must neither call providers nor emit completion twice.

## 9. Failure and authority invariants

### 9.1 Cross-cutting

- A failed durable write restores the prior in-memory state before the exception escapes.
- No success is acknowledged before the relevant durable fence or terminal fact is committed.
- Cancellation does not erase uncertainty after an external mutation may have occurred.
- Exact tool schemas and safety annotations are authority inputs, not documentation.
- A tool contract change fails closed before invocation.
- Named Neurons have distinct protected OAuth state even when they target the same provider endpoint.
- One DigitalBrain remains one trust boundary; no new authorization model is implied.

### 9.2 Direct MAF sessions

- Direct `Concurrent` and direct `GroupChat` persist and resume a MAF `AgentSession`.
- A failed session-state write cannot leave the activation believing uncommitted state is durable.
- Direct `GroupChat` remains forbidden while a supervised attempt is active.
- A definition mismatch is detected before deserializing or invoking the stored session.
- MAF types do not cross public contracts.

### 9.3 Supervised Task/GroupChat

- A concrete definition is checked before continue, reminder redispatch, checkpoint adoption, and completion.
- One active run has one owned cancellation source.
- cancellation is persisted/fenced before runner notification;
- late completion or checkpoint adoption after cancellation is rejected;
- restart/redispatch does not duplicate checkpoint adoption;
- a checkpoint-grain write failure leaves no ghost payload, parent, or order entry.

### 9.4 MCP OAuth and transport

- The official SDK owns discovery, state generation, PKCE, bearer injection, refresh, and authenticated retry.
- The redirect callback receives the SDK-created authorization URI and validates the returned state; it does not create a second state/PKCE flow.
- Default authorization fails closed.
- Local loopback is enabled only by explicit development configuration, accepts only loopback HTTP, enforces the exact callback path, and validates state.
- Google requires its configured client secret; Salesforce configuration permits an omitted secret where the provider supports the PKCE public-client flow.
- The runtime owns and disposes its `HttpClient` and official client exactly once.

### 9.5 Gmail

- Only exact `get_message` is callable.
- Input admits `messageId` and `messageFormat = FULL_CONTENT` as required by the module.
- Output fields consumed by the mapper must be present and typed.
- Tool annotations must be read-only, non-destructive, idempotent, and closed-world as expected.
- Protocol `IsError`, malformed structured content, or drift fails without leaking MCP types.

### 9.6 Salesforce

- Proposal creation remains provider-free and cannot mutate Salesforce.
- Approval proves exact proposal evidence.
- The durable `Invoking` fence is written before the external mutation.
- A newly listed `updateSobjectRecord` with the stored exact fingerprint is used for mutation.
- After the fence, cancellation or ambiguous transport/protocol failure triggers one independent bounded reconciliation.
- The update tool is never retried.
- Reconciliation uses a freshly listed, exact-fingerprint `soqlQuery`.
- Inability to reconcile, query drift, or an ambiguous result commits `OutcomeUncertain`.
- Terminal replay performs no MCP work.
- Account ID validation makes the reconciliation query non-injectable.

### 9.7 Website and CodeGraph

- Production AppHost contains the exact equivalent of:

  ```csharp
  builder.AddViteApp("website", "../../docs")
      .WithExternalHttpEndpoints();
  ```

- Resource name is `website`; the path is `../../docs`, never `../../website`.
- `Aspire.Hosting.JavaScript` is centrally pinned to 13.4.6 and referenced only where consumed.
- Aspire’s installer resource owns dependency installation; add no custom npm resource without a failing clean-source live proof.
- Every normal `aspire start`/`aspire run` performs the AppHost build.
- That AppHost build initializes CodeGraph when absent and synchronizes it on subsequent builds.
- CodeGraph failure fails the AppHost build rather than being hidden by `ContinueOnError`.
- The same graph is queryable through the configured project MCP.

## 10. Implementation protocol for every slice

Use test-first red/green slices. Do not use `dotnet test --filter`.

For each slice:

1. Re-run `git status --porcelain` and inspect overlap with user changes.
2. Add the smallest failing test or behavioral proof.
3. Run the smallest owning test project and record the expected red.
4. Make only the files named by the slice green.
5. Re-run the owning test project.
6. Run the repository root gate:

   ```powershell
   dotnet test --logger "console;verbosity=minimal"
   ```

7. Run:

   ```powershell
   git diff --check
   git status --short
   git diff --stat
   ```

8. Before committing, answer in the commit message body:

   ```text
   Added without a current consumer: None.
   Claimed without verification: None; verified by <commands/evidence>.
   Changed outside the intended slice: None.
   ```

   If any answer is not “None,” either remove the work, supply the missing verification, or split the commit before proceeding.

9. Create exactly one green commit for the slice.

The root test gate is mandatory after each slice even when the focused test is green.

## 11. Test-first implementation slices

### Slice 1 — Make AppHost builds refresh CodeGraph

**Goal:** Replace the stale one-time initialization target without changing the already-working project MCP configuration.

**Red proof**

In a disposable copy with `.codegraph` absent, build the production AppHost and prove the current target neither creates a graph nor refreshes through an AppHost build. On a second build after a source edit, prove status remains stale. Capture this outside the repository.

**Files**

- modify `Directory.Build.targets`
- add/update an appropriate build-contract assertion in `tests/DigitalBrain.Tests/IntegrationHostingContracts.cs`
- do **not** modify `.mcp.json`
- do **not** modify `.codex/config.toml`

**Green implementation**

- gate the target on `$(IsAspireHost) == 'true'`;
- resolve the repository root deterministically;
- if `.codegraph/codegraph.db` is absent, run `npx -y @colbymchenry/codegraph@latest init <root>`;
- otherwise run `npx -y @colbymchenry/codegraph@latest sync <root>`;
- remove `.config/.codegraph-initialized` sentinel behavior;
- remove the AppHost exclusion;
- do not use `ContinueOnError`;
- avoid recursively triggering another build;
- keep the existing project MCP commands unchanged.

**Focused green**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj --logger "console;verbosity=minimal"
dotnet build hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj -v:minimal
npx -y @colbymchenry/codegraph@latest status -j .
dotnet test --logger "console;verbosity=minimal"
```

The clean-source `aspire start` and project-MCP proof remain mandatory final behavioral gates; a build-contract test alone is insufficient.

**Commit**

```text
build: refresh CodeGraph on every AppHost build
```

### Slice 2 — Host VitePress through official Aspire JavaScript lifecycle

**Goal:** Add the production `website` resource and prove its application model.

**Red test**

Add a production-AppHost model test that fails because `website` does not exist.

**Files**

- modify `Directory.Packages.props`
- modify `hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`
- modify `hosts/DigitalBrain.AppHost/AppHost.cs`
- modify `tests/DigitalBrain.HostTests/DigitalBrain.HostTests.csproj`
- add `tests/DigitalBrain.HostTests/ProductionAppHost.cs`
- do not add a custom npm installer or copy the site into TestingAppHost

**Green implementation**

- centrally pin `Aspire.Hosting.JavaScript` to `13.4.6`;
- reference it from `hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`;
- add exactly the equivalent of:

  ```csharp
  builder.AddViteApp("website", "../../docs")
      .WithExternalHttpEndpoints();
  ```

- reference the production AppHost from HostTests;
- use `DistributedApplicationTestingBuilder.CreateAsync<Projects.DigitalBrain_AppHost>()`;
- assert exactly one resource named/displayed `website`;
- assert its working directory resolves to repository `docs`;
- assert it has an external HTTP endpoint.

**Focused green**

```powershell
dotnet test tests/DigitalBrain.HostTests/DigitalBrain.HostTests.csproj --logger "console;verbosity=minimal"
dotnet test --logger "console;verbosity=minimal"
```

Do not claim runtime health in this slice; the final clean-clone start supplies that proof.

**Commit**

```text
feat(apphost): expose the docs as the website resource
```

### Slice 3 — Make OAuth private, durable, and account-correct

**Goal:** Remove the public redirect seam, preserve official SDK ownership, support Salesforce PKCE without a secret, and make token commits transactional.

**Red tests**

Add tests for:

- two Gmail names producing separate protected token state;
- the full Neuron ID remaining in the protection purpose;
- token-cache state rolling back when commit throws;
- default redirect behavior failing closed;
- explicit loopback rejecting a non-loopback URI, wrong path, and wrong returned state;
- loopback accepting the SDK-supplied state without generating a replacement;
- Salesforce configuration/hosting accepting no client secret;
- Google configuration still requiring its secret.

**Files**

- modify `src/DigitalBrain.Integrations.Mcp/DurableMcpTokenCache.cs`
- modify `src/DigitalBrain.Integrations.Mcp/McpOAuth.cs`
- modify `src/DigitalBrain.Integrations.Mcp/McpClient.cs` only as needed before its Slice 4 replacement
- modify `src/DigitalBrain.Integrations.Mcp.Aspire.Hosting/McpHosting.cs`
- modify `modules/DigitalBrain.Modules.Google.Aspire.Hosting/GoogleHostingExtensions.cs`
- modify `modules/DigitalBrain.Modules.Salesforce.Aspire.Hosting/SalesforceHostingExtensions.cs`
- modify provider AppHost wiring in `hosts/DigitalBrain.AppHost/AppHost.cs` if it currently projects a mandatory Salesforce secret
- modify `tests/DigitalBrain.Tests/IntegrationContracts.cs`
- modify `tests/DigitalBrain.Tests/IntegrationHostingContracts.cs`
- modify `tests/DigitalBrain.Simulations/McpTestDoubles.cs` only for the protocol-boundary tests needed before Slice 4

**Green implementation**

- remove `IMcpAuthorizationRedirect`, the rejecting implementation, and its DI override seam;
- select internal fail-closed or explicit development loopback behavior from configuration;
- let the SDK construct discovery, authorization URI, state, PKCE, token exchange, refresh, and authenticated retry;
- validate the returned state against the SDK authorization URI;
- retain the current token-purpose format, but name the input `durableIdentity`;
- stage previous token bytes and restore them if commit fails;
- make MCP client secret nullable in the shared server definition;
- stop declaring/projecting a required Salesforce client-secret resource;
- retain Google’s secret requirement.

Do not add Google-specific `login_hint`, `access_type`, or other authorization extras: current hosted-MCP evidence does not establish them.

**Focused green**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.Simulations/DigitalBrain.Simulations.csproj --logger "console;verbosity=minimal"
dotnet test --logger "console;verbosity=minimal"
```

**Commit**

```text
refactor(mcp): make OAuth private and account-durable
```

### Slice 4 — Replace imitation MCP clients with one concrete runtime

**Goal:** Exercise the official protocol boundary while leaving provider policy in provider modules.

**Red tests**

Replace client/factory test doubles with one configurable fake HTTP handler/server speaking the official MCP protocol. Prove:

- the shared runtime sends a preloaded bearer token;
- one callback has one client lifetime and does not escape it;
- Gmail lists, admits, and directly calls the selected official tool;
- Gmail rejects wrong name, schema, output schema, or safety annotations;
- Gmail maps structured `FULL_CONTENT` output to `GmailMessage`;
- no raw MCP type appears in Google/Salesforce public assemblies;
- provider projects do not expose or consume `IMcpClient`/`IMcpClientFactory`.

The provider-composition fake endpoint may accept unauthenticated official MCP requests. Keep the bearer proof in a separate shared-runtime test.

**Files**

- replace `src/DigitalBrain.Integrations.Mcp/McpClient.cs` with the concrete `McpRuntime`/`McpServerDefinition` implementation
- delete `src/DigitalBrain.Integrations.Mcp/McpToolContract.cs`
- add `src/DigitalBrain.Integrations.Mcp/McpToolFingerprint.cs`
- modify `src/DigitalBrain.Integrations.Mcp/DigitalBrain.Integrations.Mcp.csproj` only if package/item cleanup is required
- modify `modules/DigitalBrain.Modules.Google/Gmail.cs`
- modify `modules/DigitalBrain.Modules.Google/GoogleModule.cs`
- modify `modules/DigitalBrain.Modules.Salesforce/Salesforce.cs`
- modify `modules/DigitalBrain.Modules.Salesforce/SalesforceModule.cs`
- rewrite `tests/DigitalBrain.Simulations/McpTestDoubles.cs` around the official protocol boundary
- modify `tests/DigitalBrain.Simulations/AccountEnrichmentCompositionContracts.cs`
- modify `tests/DigitalBrain.Tests/IntegrationContracts.cs`
- modify `tests/DigitalBrain.Tests/PackageBoundaryContracts.cs`

**Green implementation**

- delete `IMcpClient`, `IMcpClientFactory`, `SdkMcpClient`, `SdkMcpClientFactory`, and private session wrapper;
- implement one concrete callback-scoped runtime;
- own `HttpClient` lexically;
- construct `HttpClientTransport` with explicit external-client ownership;
- create and asynchronously dispose the official `McpClient`;
- use `ListToolsAsync` and `McpClientTool.CallAsync` directly;
- canonicalize exact fingerprints centrally without centralizing provider acceptance policy;
- require structured content and reject `IsError`;
- keep all raw SDK types internal.

Disposal claims must match the compiler/package-source proof: the runtime owns the outer `HttpClient`; the official client owns its session. Do not assert unverified ownership behavior for an independently retained transport object—do not retain one.

**Focused green**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.Simulations/DigitalBrain.Simulations.csproj --logger "console;verbosity=minimal"
dotnet test --logger "console;verbosity=minimal"
```

**Commit**

```text
refactor(mcp): use the official client as the transport boundary
```

### Slice 5 — Harden Salesforce approval, fencing, and reconciliation

**Goal:** Ensure at-most-once mutation intent and honest uncertain outcomes.

**Red tests**

At the official MCP protocol boundary, prove:

- proposal creation makes no provider request;
- approval evidence must match exactly;
- official tool names are `updateSobjectRecord` and `soqlQuery`;
- wrong input/output schema or any safety-annotation drift fails closed;
- `Invoking` is durable before mutation;
- a fresh connection re-lists and matches the stored update fingerprint;
- cancellation before the fence makes no provider request;
- cancellation/transport/protocol ambiguity after the fence performs one fresh bounded query reconciliation;
- the update is never retried;
- reconciliation drift or timeout stores `OutcomeUncertain`;
- terminal replay performs no MCP request;
- state rolls back after a failed save;
- invalid account IDs fail before SOQL construction;
- `IsError` or malformed structured output cannot become success.

**Files**

- modify `modules/DigitalBrain.Modules.Salesforce/Salesforce.cs`
- modify `modules/DigitalBrain.Modules.Salesforce/SalesforceModule.cs` only if activation dependencies change
- modify `tests/DigitalBrain.Simulations/AccountEnrichmentCompositionContracts.cs`
- modify `tests/DigitalBrain.Simulations/McpTestDoubles.cs`
- modify `tests/DigitalBrain.Tests/IntegrationContracts.cs`

**Green implementation**

- validate exact first-party tool contracts locally;
- correct the old snake-case mutation name to `updateSobjectRecord`;
- save tool fingerprints and `Invoking` before mutation;
- open a fresh official-client callback for mutation and re-list;
- after the fence, use one internal timeout/cancellation source for a fresh reconciliation callback;
- never pass `CancellationToken.None`;
- never retry the update;
- write terminal success or `OutcomeUncertain`;
- restore previous in-memory ledger state on write failure.

**Focused green**

```powershell
dotnet test tests/DigitalBrain.Simulations/DigitalBrain.Simulations.csproj --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj --logger "console;verbosity=minimal"
dotnet test --logger "console;verbosity=minimal"
```

**Commit**

```text
fix(salesforce): fence mutations and reconcile uncertain outcomes
```

### Slice 6 — Give direct Concurrent and GroupChat one durable MAF session module

**Goal:** Persist direct MAF sessions without conflating them with supervised checkpoints.

**Red tests**

Add direct-orchestration tests for:

- `Concurrent` serializing after a successful call and deserializing on the next call;
- direct `GroupChat` doing the same through the shared module;
- stable compatibility across a byte-identical rebuild;
- rejection when workflow kind, participant order/identity, manager, aggregator, MAF assembly identity, or execution environment changes;
- state rollback when the durable write fails;
- no state advance when MAF execution fails;
- direct GroupChat rejection while a supervised attempt is active;
- public assemblies exposing MEAI but not MAF session/workflow types.

**Files**

- add `modules/DigitalBrain.Modules.AI/DirectAgentSession.cs`
- add `modules/DigitalBrain.Modules.AI/OrchestrationDefinition.cs`
- modify `modules/DigitalBrain.Modules.AI/Concurrent.cs`
- modify `modules/DigitalBrain.Modules.AI/GroupChat.cs`
- keep and, only if necessary, modify `modules/DigitalBrain.Modules.AI/GroupChatWorkflow.cs`
- delete `modules/DigitalBrain.Modules.AI/SessionCompatibility.cs`
- delete `modules/DigitalBrain.Modules.AI/OrchestrationState.cs`
- modify `tests/DigitalBrain.Simulations/AIOrchestrationContracts.cs`
- modify `tests/DigitalBrain.Simulations/AIAgentContracts.cs`
- modify `tests/DigitalBrain.Tests/AIContracts.cs`
- modify `tests/DigitalBrain.Tests/AssemblyBoundaryContracts.cs`

**Green implementation**

- move shared definition records out of persistence state;
- centralize protected durable direct-session load/create/run/serialize/save;
- use a stable fingerprint, never `ModuleVersionId`;
- version the protection purpose/envelope;
- reject incompatible undeployed branch state explicitly;
- make `Concurrent` and direct `GroupChat` thin definition/execution clients;
- retain `GroupChatWorkflow` as the single exact round-robin workflow builder;
- keep supervised checkpoint storage untouched in this slice.

**Focused green**

```powershell
dotnet test tests/DigitalBrain.Simulations/DigitalBrain.Simulations.csproj --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj --logger "console;verbosity=minimal"
dotnet test --logger "console;verbosity=minimal"
```

**Commit**

```text
refactor(ai): persist direct MAF sessions behind one module
```

### Slice 7 — Harden supervised workflow lifecycle

**Goal:** Detect definition drift, propagate cancellation, and make checkpoint writes transactional.

**Red tests**

Add supervised tests for:

- definition drift before continue, reminder redispatch, checkpoint adoption, invocation, and completion;
- checkpoint protection bound to the definition fingerprint;
- one active runner cancellation source passed through start/resume/send/watch/dispose paths;
- cancellation fence committed before runner signal;
- late completion/checkpoint adoption rejected;
- silo restart plus redispatch causing no duplicate checkpoint adoption;
- checkpoint write failure leaving no ghost payload, parent, or order entry;
- cross-definition protected checkpoint read failing;
- no `CancellationToken.None` in the owned long-running MAF path.

**Files**

- modify `modules/DigitalBrain.Modules.AI/AIWorkerState.cs`
- modify `modules/DigitalBrain.Modules.AI/WorkflowRun.cs`
- modify `modules/DigitalBrain.Modules.AI/WorkflowRunner.cs`
- modify `modules/DigitalBrain.Modules.AI/OrleansCheckpointStore.cs`
- modify `modules/DigitalBrain.Modules.AI/GroupChat.cs` only for the supervised/direct exclusion and dispatch boundary
- modify `tests/DigitalBrain.Simulations/AIWorkerContracts.cs`
- modify `tests/DigitalBrain.Simulations/AIOrchestrationContracts.cs`
- modify `tests/DigitalBrain.HostTests/HostedRestart.cs`

**Green implementation**

- describe the current concrete workflow definition at every re-entry boundary;
- compare it with the durable definition before state read/adoption/invocation;
- bind checkpoint protection to the fingerprint;
- add `[AlwaysInterleave] IWorkflowRunner.CancelAsync(Guid runId)`;
- own one cancellation source per active run;
- pass the owned token through MAF operations and clean disposal;
- persist cancellation before signaling;
- reject late events;
- snapshot and restore checkpoint-grain collections if `WriteStateAsync` fails.

**Focused green**

```powershell
dotnet test tests/DigitalBrain.Simulations/DigitalBrain.Simulations.csproj --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.HostTests/DigitalBrain.HostTests.csproj --logger "console;verbosity=minimal"
dotnet test --logger "console;verbosity=minimal"
```

**Commit**

```text
fix(ai): fence supervised workflow state and cancellation
```

### Slice 8 — Make account enrichment named and idempotent

**Goal:** Express provider-account identity at the sample boundary and prevent duplicate work or facts.

**Red tests**

Add tests for:

- command requiring a non-empty Gmail account name;
- exact named `IGmail` resolution;
- two account names using distinct provider identity/state;
- durable request fence written before Gmail or Salesforce work;
- exact command replay making no provider calls and no duplicate `AccountEnriched` emission;
- same command ID with different Gmail account, message ID, or account ID failing before providers;
- replay after proposal and after terminal completion;
- approval remaining a separate exact human fact;
- failed state write restoring prior in-memory process state.

**Files**

- modify `samples/DigitalBrain.AccountEnrichment/AccountEnrichmentFacts.cs`
- modify `samples/DigitalBrain.AccountEnrichment/AccountEnrichmentProcess.cs`
- modify `tests/DigitalBrain.Simulations/AccountEnrichmentCompositionContracts.cs`
- modify `tests/DigitalBrain.Tests/SerializationContracts.cs` if the public command serialization contract is asserted there

**Green implementation**

- add `GmailAccount` to the public command;
- validate it and resolve exactly that named Neuron;
- protect/store a fingerprint over command ID, Gmail account, message ID, and Salesforce account ID;
- persist a request fence before provider calls;
- persist proposal/completion/receipt phases;
- make terminal exact replay a no-op;
- reject changed-input replay;
- emit completion once.

No account registry, tenant, role, permission, or provider router belongs in this slice.

**Focused green**

```powershell
dotnet test tests/DigitalBrain.Simulations/DigitalBrain.Simulations.csproj --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj --logger "console;verbosity=minimal"
dotnet test --logger "console;verbosity=minimal"
```

**Commit**

```text
fix(sample): make account enrichment named and idempotent
```

### Slice 9 — Remove proven debris and lock boundaries

**Goal:** Finish deletion only after replacement behavior is green.

**Red tests**

Strengthen reflection/project-reference assertions so they fail if:

- removed MCP interfaces/factories/redirect seams remain;
- provider public APIs expose raw MCP types;
- Tasks gains an AI/provider edge;
- northbound `hosts/DigitalBrain.Mcp` gains a southbound provider-mechanics dependency;
- production packages expose MAF implementation types;
- provider tests reintroduce fake provider-client layers.

**Files**

- delete `hosts/DigitalBrain.AppHost/_probe_exec.cs.txt`
- finish deletions listed in Section 6
- modify `tests/DigitalBrain.Tests/ArchitectureCutContracts.cs`
- modify `tests/DigitalBrain.Tests/AssemblyBoundaryContracts.cs`
- modify `tests/DigitalBrain.Tests/PackageBoundaryContracts.cs`
- modify `tests/DigitalBrain.Tests/IntegrationContracts.cs`
- modify `tests/DigitalBrain.Simulations/CentralMcpContracts.cs`
- modify project files only to remove now-unused references

**Green implementation**

- remove only CodeGraph-proven zero-consumer or replaced artifacts;
- re-run CodeGraph for every deletion candidate immediately before deleting;
- use `rg` and compiler failures as cross-checks;
- keep `IDurablePayloadProtector`, `McpProviderHostingDefinition`, `GroupChatWorkflow`, task vocabulary, all hosts, and northbound MCP.

**Focused green**

```powershell
dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj --logger "console;verbosity=minimal"
dotnet test tests/DigitalBrain.Simulations/DigitalBrain.Simulations.csproj --logger "console;verbosity=minimal"
dotnet test --logger "console;verbosity=minimal"
git diff --check
```

**Commit**

```text
refactor: remove replaced architecture debris
```

### Slice 10 — Align architecture and site documentation

**Goal:** Make documentation describe the resulting graph and clear both known docs failures.

**Red proof**

Before edits, record the two known failures:

```powershell
Push-Location docs
node tools/render-specification.mjs
node --test tests/*.test.mjs
Pop-Location
```

**Files**

- modify `docs/architecture.md`
- modify `docs/packages.md`
- modify `docs/tests/site.test.mjs`
- modify `docs/.vitepress/theme/architecture-data.js` only if the resulting graph changes its source data
- modify other docs pages only when a concrete assertion becomes stale
- do not commit generated `docs/specification.md` if it remains ignored/generated

**Green documentation**

Document:

- direct versus supervised MAF state ownership;
- MEAI public/MAF internal boundary;
- one-way AI-to-Tasks.Contracts bridge;
- southbound MCP mechanics versus northbound MCP host;
- provider-owned tool policy and authority;
- named account/token isolation;
- official production `website` resource;
- AppHost-build CodeGraph refresh invariant;
- `DigitalBrain.Security` in the package table;
- the current ratified-checklist count without hard-coding stale architecture.

**Focused green**

```powershell
Push-Location docs
node tools/render-specification.mjs
node --test tests/*.test.mjs
npm run build
Pop-Location
dotnet test --logger "console;verbosity=minimal"
```

**Commit**

```text
docs: align the architecture with the lean runtime
```

## 12. Clean-clone Aspire, VitePress, and CodeGraph validation

Run this only after all slices are green. Use a new temporary validation root so the existing user-owned AppHost is untouched.

### 12.1 Preflight

Record:

```powershell
git rev-parse HEAD
git status --porcelain
git merge-base master HEAD
aspire --version
```

Require a clean worktree before cloning. Record the exact bytes/hash of repository `aspire.config.json`.

Create a unique temporary directory with PowerShell `New-Item`, clone the local repository into it, and check out the implementation HEAD. Do not reuse the active workspace’s build outputs, `node_modules`, or `.codegraph`. Verify those are absent in the clone.

### 12.2 Start the real production AppHost

From outside the active workspace, run the clone’s AppHost:

```powershell
aspire start `
  --apphost <validation-root>\hosts\DigitalBrain.AppHost\DigitalBrain.AppHost.csproj `
  --isolated `
  --format Json `
  --non-interactive
```

Do not pass `--no-build`. The normal AppHost build must execute Slice 1’s CodeGraph target.

If the command remains attached, use the CLI-supported background/session mechanism while retaining its process identity and output. Never stop the user’s separate active instance.

### 12.3 Inspect state and logs

Use the exact validation AppHost path:

```powershell
aspire wait website `
  --apphost <validation-root>\hosts\DigitalBrain.AppHost\DigitalBrain.AppHost.csproj `
  --status up `
  --timeout 180

aspire describe `
  --apphost <validation-root>\hosts\DigitalBrain.AppHost\DigitalBrain.AppHost.csproj `
  --format Json

aspire logs website `
  --apphost <validation-root>\hosts\DigitalBrain.AppHost\DigitalBrain.AppHost.csproj
```

Write any JSON/log capture only under the temporary validation root or OS temp directory. `aspire describe` can contain environment values and secrets; never commit or echo unnecessary values.

Prove from the application model/state:

- exactly one resource named `website`;
- the official installer resource completed successfully;
- the website process is up/healthy as represented by the model;
- the working directory is the clone’s `docs`;
- the endpoint is externally exposed;
- Aspire allocated the port.

### 12.4 HTTP proof

Extract the website URL from `resources[].urls[].url` in the validation `aspire describe` JSON. Do not assume a port.

Perform an HTTP GET with a bounded timeout and require:

- a successful 2xx response;
- HTML content;
- the expected DigitalBrain/VitePress title or stable page marker.

This is the proof that the official installer plus existing `dev` script is sufficient. Add custom npm installation only if this clean-source test fails specifically because Aspire’s official installer cannot install the lockfile dependencies, and document that contradictory proof before changing design.

### 12.5 CodeGraph proof

In the clone:

```powershell
npx -y @colbymchenry/codegraph@latest status -j .
```

Require:

- `.codegraph/codegraph.db` was created by the normal AppHost build;
- status reports the clone’s current source state;
- a second normal AppHost build after a harmless, temporary source-only validation edit advances/synchronizes the graph;
- the temporary edit is reverted before any other gate.

Then point a project-MCP process at the clone using the unchanged configured command and issue `codegraph_explore` for a symbol introduced by this implementation. Require current source and callers with no stale/disabled warning.

Do not “prove” only that the standalone CLI can read a graph; both build refresh and project MCP availability are required.

### 12.6 Stop and clean up safely

Stop only the validation AppHost:

```powershell
aspire stop `
  --apphost <validation-root>\hosts\DigitalBrain.AppHost\DigitalBrain.AppHost.csproj
```

Do not use `aspire stop --all`.

Before start, record existing Aspire/AppHost/Host/ProbeHost/node/Vite processes and command lines. During validation, record all resource PIDs returned by the validation application model. After stop:

- wait with a bounded timeout;
- assert every validation-owned PID exited;
- assert no process whose command line contains the validation-root path remains;
- do not kill unrelated pre-existing processes;
- if a validation-owned process remains, terminate that exact PID and record it as a failed lifecycle gate until re-run cleanly.

If Aspire changed the clone’s `aspire.config.json`, restore its exact preflight bytes and explain the CLI mutation. The active repository file must remain unchanged.

Delete the temporary validation root only after resolving its absolute path and proving it is the intended unique temp directory. Use one PowerShell shell end-to-end.

## 13. Final unfiltered gates

Run in this order from the implementation worktree:

```powershell
Set-Location docs
node tools/render-specification.mjs
node --test tests/*.test.mjs
Set-Location ..
dotnet test --logger "console;verbosity=minimal"
git diff --check
```

Then require:

1. the full clean-clone Aspire/VitePress validation in Section 12;
2. healthy external website HTTP response on the allocated port;
3. successful CodeGraph initialize/sync through normal AppHost builds;
4. successful CodeGraph query through the configured project MCP;
5. no Aspire, AppHost, Host, ProbeHost, Vite, node, or website process owned by validation remains;
6. `aspire.config.json` is byte-identical to preflight unless an intentional, separately reviewed change was made;
7. all ten slice commits contain the three required pre-commit answers;
8. one final CodeGraph dependency/deletion audit;
9. final:

   ```powershell
   git rev-parse HEAD
   git status --porcelain
   git merge-base master HEAD
   ```

10. clean worktree;
11. HEAD descends from the expected implementation baseline or any deviation is explained;
12. merge-base is understood and recorded;
13. honest remaining findings are reported instead of converted into passing claims.

No filtered test command can substitute for the root gate.

## 14. Honest expected remaining findings

These are not reasons to weaken the plan:

- Google hosted Gmail MCP is Developer Preview and requires external preview access/account authorization for a real live provider proof.
- Salesforce live authorization/tool listing requires an actual configured Salesforce account and hosted-MCP access.
- Until those credentials/access exist, protocol-boundary tests and first-party contract checks are the strongest repository-owned proof; label live-provider validation blocked rather than faking it.
- The docs baseline `npm ci` reports 3 dependency vulnerabilities (2 moderate, 1 high) and an esbuild allow-scripts warning. Record them in handoff; do not expand this refactor into an unplanned dependency upgrade.
- The repository currently uses prerelease Orleans journaling packages. Do not infer production guarantees beyond the compiled APIs and repository restart tests.
- The user-owned AppHost observed during investigation is outside implementation authority. Validation must remain isolated and must not stop it.

## 15. Disposition of the pre-CodeGraph draft

| Earlier draft direction | Reviewed disposition |
|---|---|
| Repair CodeGraph wrapper/configuration | **Remove as stale.** Project MCP is live. Change only the proven AppHost-build refresh defect. |
| Add VitePress to production AppHost | **Keep and sharpen.** Use official `Aspire.Hosting.JavaScript`, resource `website`, path `../../docs`, app-model and live proofs. |
| Add custom npm lifecycle | **Reject unless clean-source proof fails.** Official installer owns it. |
| Add token rollback | **Keep.** Fold into the private OAuth/durable-token slice. |
| Add runtime plus connection/session wrapper | **Simplify.** One concrete callback-scoped `McpRuntime`; no returned connection wrapper. |
| Own OAuth state/PKCE in redirect seam | **Correct.** SDK owns state and PKCE; private loopback only validates SDK-provided state. |
| Add Google OAuth extras | **Remove as unverified.** No current hosted-MCP evidence for them. |
| Add shared generic tool-policy abstraction | **Remove.** Share only exact fingerprint mechanics; providers own policy. |
| Harden Salesforce fencing/reconciliation | **Keep and correct.** Use official camelCase tool names and bounded fresh reconciliation. |
| Add durable direct MAF sessions | **Keep.** Share one internal module across Concurrent and direct GroupChat. |
| Delete `GroupChatWorkflow` | **Reject.** It prevents direct/supervised round-robin definition drift. |
| Harden supervised workflow lifecycle | **Keep as a separate slice.** Do not merge with direct sessions. |
| Make sample named/idempotent | **Keep.** No registry/router. |
| Broad cleanup | **Constrain.** Delete only CodeGraph-proven debris after replacements are green. |
| Docs finalization | **Keep and expand.** Fix the two already-red docs tests and document resulting boundaries. |

This reviewed plan is the implementation authority unless later repository evidence contradicts it.
