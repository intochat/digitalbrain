# Neuron foundations and specialist module refactoring

**Status: implemented and verified. Live Gmail/Salesforce reads require a fresh user login.**

Prepared against `codex/day-zero-scripting` at `d0a09c26360a8bc1b5262efca94bec44bb0c09e2` (`With IAspire`). This plan follows the [architecture assessment](D:/digitalbrain/docs/reviews/2026-09-05-neuron-agent-architecture-assessment.md), CodeGraph inspection, and separate Google, Salesforce, and graph integration reviews. The user approved implementation on 2026-09-05.

## Intended result

Ino remains the conversational assistant. Aspire, Gmail, and Salesforce become consistent specialist neurons, each with its own native MCP tools, module identity, icon, and observable execution. The fundamental request, tool preparation, authentication continuation, and confirmation paths are corrected before the provider migrations.

| Module | Public contract | Implementation | Ino tool | Graph presentation |
|---|---|---|---|---|
| Microsoft | `DigitalBrain.Microsoft.IAspire : IAgent` | `Aspire : Agent` | Existing Aspire delegation | Microsoft / Aspire / Aspire icon |
| Google | `DigitalBrain.Google.IGmail : IAgent` | `Gmail : Agent` | `ask_gmail` | Google / Gmail / Gmail icon |
| Salesforce | `DigitalBrain.Salesforce.ISalesforce : IAgent` | `Salesforce : Agent` | `ask_salesforce` | Salesforce / Salesforce / Salesforce icon |

Keep the current namespaces and assembly/folder conventions. `DigitalBrain.Modules.Google.Contracts` and its peers remain assembly boundaries; this plan does not add a namespace migration.

Example behavior after implementation:

- “How many Aspire resources are healthy?” delegates to Aspire and returns live evidence.
- “Find my recent emails about the release” delegates to Gmail; login, if needed, resumes the intended read in the same conversation.
- “Find open Salesforce opportunities for Acme” delegates to Salesforce, retaining query restrictions.
- Drafting an email or proposing a Salesforce record change produces an exact preview; a fresh user confirmation authorizes that exact operation.
- The graph shows the specialist under its module, native tool activity, useful failure information, and actual synapses. Clicking nodes and connections continues to use the shared inspector.

## Design decisions included in approval

1. **Keep source-owned routing.** `Send`/`Request` are directed; `SubscribeTo` establishes Bound edges; successful delivery can establish Learned edges. Keep current broadcast eligibility over active Bound and Learned edges. Unsubscribe removes the current edge and does not forbid a later explicit send.
2. **Keep one generic specialist signal contract.** `IAgent` handles `AgentRequest` and replies with `AgentReply`. Do not introduce `GetGmailMessages`, `GetSalesforceStatus`, or other MCP operation signals/DTOs.
3. **Use one tool preparation contract.** All agent tool sources use one required asynchronous method with the actual turn context. Ino composes delegation and local capabilities; specialists compose their own provider capabilities.
4. **Keep provider policy while removing schema duplication.** Native tool names and schemas come from MCP. Account validation, query restrictions, consent, and write confirmation remain application-enforced policies around those tools.
5. **Resume login at a trusted fixed specialist.** Extend the existing stored user action and chat worker path. Generic delegation remains prohibited during restricted continuation.
6. **Keep volatile credentials.** OAuth tokens stay outside neuron state, journals, telemetry, and public contracts. Existing connection/card/hosting capabilities remain; no credential persistence project is added.
7. **Use module-owned presentation metadata.** A descriptor supplies type, label, module, and icon key for already-observed neurons. It does not register instances, grant capabilities, or create graph edges.
8. **Clarify observations without adding a lifecycle subsystem.** `AgentActivity` remains bounded diagnostic evidence in this refactor. Document that journal recording does not deliver subscriptions, and offer only meaningful subscription choices in the UI. Existing subscribe/unsubscribe behavior remains fully supported. New automatic lifecycle broadcasts are deferred until a concrete behavior defines their delivery contract; they must not make an agent's reply wait on a busy subscribing caller.

## Phase 0 — Establish the migration baseline

- Inventory callers and registrations before changing `IGmail` and `ISalesforce`, including test fakes, script references, login continuation tool names, and trusted command handlers.
- Record the currently supported provider operations and policies. Use existing fixtures; add missing policy coverage where migration could otherwise silently remove a capability.
- Preserve configuration roots, `.WithGmail()`, `.WithHostedMcp()`, `.WithAspire()`, OAuth callback routes, and current module registration in AppHost.
- Keep the reviewed architecture as the baseline. Make each following phase independently buildable; do not leave direct and specialist catalogs as permanent parallel implementations.

**Gate:** known callers, supported operations, and relevant baseline test results are recorded before deleting adapters.

## Phase 1 — Correct and simplify neuron request mechanics

Primary files: [Neuron.cs](D:/digitalbrain/src/Kernel/DigitalBrain/Neuron/Neuron.cs), [SignalSender.cs](D:/digitalbrain/src/Kernel/DigitalBrain/Neuron/SignalSender.cs), [NeuronResponse.cs](D:/digitalbrain/src/Kernel/DigitalBrain/Neuron/NeuronResponse.cs), [NeuronRequestPath.cs](D:/digitalbrain/src/Kernel/DigitalBrain/Neuron/NeuronRequestPath.cs), [DigitalBrainClientTransport.cs](D:/digitalbrain/src/Kernel/DigitalBrain.Contracts/DigitalBrainClientTransport.cs).

- Recover from a stale observation cursor by performing a bounded scan of retained entries and matching exact reply identity. Distinguish an available reply from an actually evicted reply; do not loop indefinitely on repeated resets.
- Share response identity, handled/refused/unhandled outcome mapping, and deadline rules between internal and facade requests. Match request causation, expected sender, and response type; correlation remains useful for the whole trace.
- Preserve activation-local source-bound requests and the external root entry point. Do not make a nested request re-enter the serialized owner root.
- Move nested awaited-hop checks to delivery and binding boundaries. Preserve supported self-delivery and detached replies. Keep cancellation/deadlines for cases a propagated call path cannot detect.
- Consolidate duplicated sender mutation logic where it reduces divergence. All journal/synapse mutations remain in the owning activation's turn; no detached state mutation after timeout.
- Review the preexisting targeted `PublishAsync` alias, migrate callers to the clearest existing primitive where practical, and avoid adding another synonym or routing abstraction.

**Gate:** focused substrate/facade/delegation tests cover retention before and after reply, wrong causation/sender, mixed request/send/subscription cycles, cancellation, source ownership, and existing Bound/Learned/unsubscribe behavior. The Aspire request path remains working.

## Phase 2 — One agent tool path and shared MCP mechanics

Primary files: [Agent.cs](D:/digitalbrain/src/Modules/AI/AI/Agent.cs), [IAgentToolSource.cs](D:/digitalbrain/src/Modules/AI/Contracts/IAgentToolSource.cs), [AgentToolContext.cs](D:/digitalbrain/src/Modules/AI/Contracts/AgentToolContext.cs), [McpDiscoveredToolClient.cs](D:/digitalbrain/src/Kernel/DigitalBrain.Sdk/Mcp/McpDiscoveredToolClient.cs), [McpToolClient.cs](D:/digitalbrain/src/Kernel/DigitalBrain.Sdk/Mcp/McpToolClient.cs).

- Converge on a required `GetToolsAsync(turnContext, cancellationToken)` returning prepared `AITool` values, preserving non-function tools where already supported. Ordinary sources complete immediately; MCP sources discover asynchronously.
- Include the actual agent identity in turn context, alongside verified principal and the expiring source-bound request capability. Derive owner from identity; never accept a model-selected owner or principal.
- Give each agent one preparation path. Migrate repository review, behavior/UI/Excel contributions, delegation, and Aspire before removing the old owner-only/default overloads and separate `McpTools` extension path.
- Move MCP discovery observations, generic screening/redaction, native result handling, and safe failure classification to the tool boundary. Keep model streaming, request handling, and turn-bound scheduling in `Agent`.
- Separate transport construction from discovery/session ownership in the SDK. Support existing STDIO Aspire and authenticated HTTP provider connections through shared catalog, invalidation, bounded-result, timeout, and cleanup mechanics. Keep transport/auth differences explicit instead of forcing them into STDIO options.
- Preserve known read-only refresh/retry behavior for authenticated HTTP. Never replay writes or operations with an uncertain outcome. Retain stale schema checks and explicit admitted-tool policy; server annotations alone do not grant access.
- Bind session leases to the verified specialist and current connection revision. Preserve provider account selection semantics and validate the authenticated principal's access to that binding at invocation, not just at preparation. Owner-keyed credential storage alone is not authorization; do not implicitly share a login with another principal using the same owner.
- Preserve cancellation, result limits, native schemas, and telemetry. Emit safe distinctions such as unavailable, timeout, catalog changed, authentication required, and content rejected, retaining trace correlation.

**Gate:** Aspire runs on the consolidated path; generic/native tool, telemetry, owner/principal, stale catalog, retry, and cancellation tests pass. The generic `Agent` no longer detects a concrete MCP SDK tool to implement provider execution policy.

## Phase 3 — Trusted specialist login continuation and confirmations

This phase must complete before removing Gmail/Salesforce's direct Ino wrappers.

### Login continuation

Today, `GmailLogins` and `SalesforceLogins` resume old flat tool names on Ino, while restricted turns reject delegation. Replace this representation using the existing interaction and chat machinery.

1. A normal delegation carries trusted execution context identifying its fixed specialist and original bounded request text.
2. Missing credentials create the existing browser login card. Application code attaches an optional stored continuation descriptor: exact specialist identity, bounded request text, admitted native read names, and the relevant connection/catalog constraints. Existing state binds chat, actor, originating command, action ID, and expiry.
3. OAuth callbacks continue to supply only the action result. They cannot choose the specialist, request, or permissions. Capture the completed binding/revision from trusted connection state.
4. The chat consumes the continuation once. Its existing turn worker calls the fixed specialist using source-bound `RequestAsync<AgentReply>` and the restricted native read scope, then delivers the result through the normal original-chat response path.
5. The specialist cannot delegate elsewhere, broaden its tool list, start another login loop, or perform a write during that continuation. Missing/revised tools or a changed binding produce a clear result requiring a fresh request.

Store bounded text and kernel identity in Product interaction control data; reconstruct `AgentRequest` in the worker. This avoids adding Product-to-AI contract coupling or a second router.

Use additive serialization IDs for stored fields. Existing pending actions lacking a trustworthy specialist descriptor may finish authentication, but must not be silently upgraded to broad delegation. At final cutover, require a fresh request for such actions and remove the legacy provider wrapper-name resume path. Preserve unrelated existing continuation uses.

Affected paths: [UserActionRequest.cs](D:/digitalbrain/src/Product/DigitalBrain.Product.Contracts/Interactions/UserActionRequest.cs), [AgentTurnContext.cs](D:/digitalbrain/src/Product/DigitalBrain.Product.Contracts/Interactions/AgentTurnContext.cs), [BrowserLogins.cs](D:/digitalbrain/src/Kernel/DigitalBrain.Sdk/OAuth/BrowserLogins.cs), [DurableTurnRecord.cs](D:/digitalbrain/src/Modules/UI/DigitalBrain.Modules.UI/Chat/DurableTurnRecord.cs), [Chat.cs](D:/digitalbrain/src/Modules/UI/DigitalBrain.Modules.UI/Chat/Chat.cs), [ChatTurnWorker.cs](D:/digitalbrain/src/Modules/UI/DigitalBrain.Modules.UI/Chat/ChatTurnWorker.cs).

### Write confirmation

- Keep Gmail's trusted preview/publication/confirmation mechanism. A native draft tool retains its input schema, but its policy wrapper clearly reports that invocation prepares a preview; the trusted confirmation handler performs the actual write.
- Apply the same narrow pattern to Salesforce create/update: capture exact native tool name and immutable arguments, account/binding revision, actor/chat, expiry, and proposal identity. The complete preview must reach the user before confirmation is accepted.
- A fresh authenticated user action confirms that exact proposal. A model-supplied `confirmed=true` is not authorization and will be removed.
- Verify the current binding and schema before submission; consume confirmation before the network attempt. Record success or an uncertain outcome without automatically retrying the mutation.
- Reuse existing trusted user command interfaces and UI response publication. Extract common confirmation mechanics only when both providers actually share them; retain provider-specific preview/policy services.
- Login completion never automatically executes a draft or CRM mutation. A fresh preview/confirmation is required after authentication or account changes.

**Gate:** tests cover delegated login from compact/full chat, same-target read resumption once, rejection/cancellation/expiry, callback replay, target or scope substitution, binding changes, no repeated login, and no automatic write. Confirmation tests cover altered arguments, unpublished/expired previews, different actors/chats, duplicate confirmations, and uncertain outcomes.

## Phase 4 — Refactor Google around `IGmail`

Current anchors: [IGmail.cs](D:/digitalbrain/src/Modules/Google/Contracts/IGmail.cs), [GoogleModule.cs](D:/digitalbrain/src/Modules/Google/Google/GoogleModule.cs), [GmailToolSource.cs](D:/digitalbrain/src/Modules/Google/Google/Gmail/GmailToolSource.cs), [GmailMcp.cs](D:/digitalbrain/src/Modules/Google/Google/Gmail/GmailMcp.cs), [GmailDraftPreviews.cs](D:/digitalbrain/src/Modules/Google/Google/Gmail/GmailDraftPreviews.cs).

- Replace the unused JSON-search service shape of `IGmail` with `IGmail : IAgent`, a stable contract alias, and `Gmail : Agent` with grain type `gmail`.
- Add the AI contracts/runtime references at the same boundaries as Microsoft. Use a configured local instance alias partitioned by the verified principal; the model supplies only request text.
- Register one module-owned `ask_gmail` delegation for Ino. Move detailed Gmail instructions from `Assistant` into the Gmail specialist.
- Provide a thin Gmail connection/tool source over shared authenticated MCP discovery. Preserve native schemas for admitted search/thread/label/draft operations and keep bounded reads, plain-text handling, selected-account checks, and consent policy together in Google.
- Preserve a small local connected-account query where useful. Cached identity alone must not claim the provider is currently reachable.
- Keep `GmailConnections`, OAuth configuration/callback handling, login cards, and draft previews. Preserve the current one-selected-account model and volatile credentials; account access must follow the verified binding described in Phase 2.
- Preserve draft previews' exact publication, chat/actor/command binding, account revision, expiry, bounded capacity, and consume-before-write behavior. Keep the existing trusted confirmation command working.
- Remove `McpGmail`, `SearchJsonAsync`, and the old flat `GmailToolSource` after callers migrate. Retain necessary Gmail policy from `GmailMcp` in the thin provider boundary; remove its duplicated generic transport/schema/evidence plumbing.
- Replace `FakeGmail` service substitution with a fake prepared catalog/transport so tests exercise the real Gmail neuron.

**Gate:** Ino-to-Gmail request and login continuation work; the native catalog belongs to Gmail; all draft/read/account policies remain enforced; direct Gmail wrapper tools are absent from Ino.

## Phase 5 — Refactor Salesforce around `ISalesforce`

Current anchors: [ISalesforce.cs](D:/digitalbrain/src/Modules/Salesforce/Contracts/ISalesforce.cs), [SalesforceModule.cs](D:/digitalbrain/src/Modules/Salesforce/Salesforce/SalesforceModule.cs), [SalesforceToolSource.cs](D:/digitalbrain/src/Modules/Salesforce/Salesforce/SalesforceToolSource.cs), [McpSalesforce.cs](D:/digitalbrain/src/Modules/Salesforce/Salesforce/McpSalesforce.cs), [SalesforceQueryGuard.cs](D:/digitalbrain/src/Modules/Salesforce/Salesforce/SalesforceQueryGuard.cs).

- Replace the three-method JSON service contract with `ISalesforce : IAgent`; add a stable alias and `Salesforce : Agent` with grain type `salesforce`.
- Register `ask_salesforce` using the same principal-scoped delegation pattern. Move Salesforce procedures out of Ino and into the specialist.
- Use a thin Salesforce connection/tool source over the shared authenticated MCP path. Initially admit the existing native operations: `getUserInfo`, `soqlQuery`, and confirmation-gated `createRecord`/`updateRecord`.
- Preserve `SalesforceQueryGuard`: outer WHERE, positive LIMIT, bounded query size, and rejection of comments, multiple statements, and locking syntax. MCP discovery does not remove these restrictions.
- Retain OAuth endpoint validation, credentials, login UI, and known read-only credential-refresh behavior. Make connection revision explicit so pending previews/continuations cannot target a silently changed binding.
- Implement Phase 3's exact-proposal confirmation for create/update. Remove the invented `objectType`/`bodyJson`/`confirmed` model schema and manual mapping where native schemas already express the operation. Keep delete unavailable.
- Remove `McpSalesforce` and the old `SalesforceToolSource` after policy and callers migrate. Replace `FakeSalesforce`/`NotImplementedSalesforce` service implementations with controlled fake or unavailable bindings behind the real specialist.
- Ensure configured, fake, and unavailable module branches all expose deliberate capability/state. Missing configuration should produce useful guidance without attempting an external request.

**Gate:** native reads and guarded queries work; login resumes only the recorded read; create/update require trusted exact confirmation; Ino exposes delegation alone. Add dedicated Salesforce policy/agent coverage, which is currently missing.

## Phase 6 — Module presentation, icons, graph, and scripts

This work can proceed alongside Phases 4–5 once the contracts and shared boundaries are stable.

- Add a neutral static presentation descriptor in Product contracts: observed grain type, display label, module label, and icon key. Register it from Microsoft, Google, and Salesforce implementations; modules do not depend on Silo or Flutter implementation.
- Make `BrainGraphMetadata` consume these registrations for observed nodes. Keep a cached generic fallback for unknown neuron types; remove provider substring guessing. A future Google neuron must not automatically receive Gmail's icon.
- Add an optional `iconKey` through graph HTTP and Dart models. Use a single allowlisted key-to-local-asset map in the UI kit for graph tiles and inspectors. Unknown keys use the generic neuron icon.
- Reuse accurate existing Gmail/Salesforce/Aspire assets; replace prototype provider drawings with appropriate bundled SVG assets where needed, recording their source. No remote icon loading or provider-specific graph widgets.
- Preserve current module grouping, clickable nodes/synapses, status/activity animation, and first-delegation visibility. An observed target can appear before a Learned edge exists; only actual synapses become persistent connections.
- Show module, specialist, connection, safe failure category, and useful observed activity in the inspector. Describe Bound/Learned and unsubscribe accurately; diagnostic journal entries must not appear as promised subscribable events.
- Add Google/Salesforce contract assembly references and imports to the script compiler alongside Microsoft. Scripts address `Brain.Get<IGmail>(...)` and `Brain.Get<ISalesforce>(...)` with the existing generic `AgentRequest`/`AgentReply` API. Do not expose implementation assemblies.

Primary paths: [BrainGraphMetadata.cs](D:/digitalbrain/src/Kernel/DigitalBrain.Silo/BrainGraphMetadata.cs), [BrainGraphModels.cs](D:/digitalbrain/src/Kernel/DigitalBrain.Silo/BrainGraphModels.cs), [BrainGraphProjection.cs](D:/digitalbrain/src/Kernel/DigitalBrain.Silo/BrainGraphProjection.cs), [brain_models.dart](D:/digitalbrain/src/Modules/UI/Flutter/core/lib/src/models/brain_models.dart), [lumen_brain_graph.dart](D:/digitalbrain/src/Modules/UI/Flutter/kit/lib/src/lumen/lumen_brain_graph.dart), [neuron_icon.dart](D:/digitalbrain/src/Modules/UI/Flutter/kit/lib/src/lumen/neuron_icon.dart), [CSharpStartupScriptRunner.cs](D:/digitalbrain/src/Kernel/DigitalBrain.Scripting/Startup/CSharpStartupScriptRunner.cs).

**Gate:** graph/HTTP/Flutter tests show correct module labels and icons, useful click details and failure states, no invented edges, principal visibility isolation, and generic icon fallback. Script tests compile and invoke both specialist contracts.

## Phase 7 — Delete migration seams and verify the whole experience

### Expected simplifications

| Current duplication or obsolete shape | Final state |
|---|---|
| Different request matchers/outcome rules | Shared request policy, distinct legitimate transports |
| Agent `Tools`, owner/context overloads, and MCP-specific path | One asynchronous tool preparation contract |
| Generic MCP execution code inside `Agent` | Shared tool boundary; agent handles its model turn |
| Provider JSON service contracts and handwritten MCP schemas | `IGmail`/`ISalesforce : IAgent` plus native discovered tools |
| Full Gmail/Salesforce catalogs and procedures on Ino | Concise specialist delegation |
| Overlapping provider transport implementations | Shared SDK session/discovery mechanics with explicit transports/auth policy |
| Old wrapper-name login continuation for migrated providers | Stored exact-specialist read continuation |
| Model-supplied Salesforce confirmation boolean | Trusted confirmation of immutable native arguments |
| Service fakes bypassing the neuron path | Real specialist with fake transport/catalog fixtures |
| Provider icon guessing by names | Module-owned presentation and one UI-kit icon map |

- Delete temporary compatibility adapters after their callers move. Keep unrelated extension points only where there are real consumers.
- Update `CONTEXT.md`, relevant architecture/getting-started docs, and provider examples together. Mark the old architecture review's fixes as implemented with evidence once they are actually verified.
- Run the affected substrate, facade, agent, SDK, browser-login, chat, scripting, graph, and hosting suites. Run Flutter analysis and the affected graph, Lumen, and login tests. Broaden testing when failures or new changes justify it.
- Verify live chat/graph read flows for all configured specialists, including the exact Aspire health question. Check traces show chat → actual specialist → model/native tools → reply, with useful correlation and failure identity.
- Preserve current explicit sensitive-content configuration and its tests. With capture enabled, verify model messages and tool arguments/results; with it disabled, verify content stays absent. Keep credentials excluded in both cases.
- Exercise mutations with fixtures and trusted-confirmation tests. Live email drafts or Salesforce writes are not required for acceptance and are not authorized by approval of this refactoring plan.
- If a provider lacks local credentials/configuration, verify its unavailable/login behavior and report live-read validation as outstanding; do not report a fixture result as a successful live provider call.

## Sequencing and approval boundary

```text
Baseline
  → Neuron request correctness
  → Unified agent tools + shared MCP + migrated Aspire
  → Trusted specialist continuation + confirmation mechanics
  → Google migration
  → Salesforce migration
  → Final cleanup and end-to-end verification

Module metadata/icons and script exposure run alongside provider migrations
after their contracts and the shared foundations are stable.
```

Implement as small reviewable changes, with the preceding gate passing before dependent work starts. Google is the first HTTP specialist because its trusted draft flow provides the established confirmation pattern; Salesforce follows on the same foundation.

Approval covers this staged refactoring, removal of the superseded internal service/tool paths after migration, provider icons/presentation, and the stated validation. It does not change Learned/Bound routing semantics, add provider operation DTOs, expand to Google Drive/Calendar or additional Salesforce capabilities, introduce multi-account UX, persist credentials, or build a new orchestration/runtime system.

## Implementation record

Implemented on the approved branch without changing Bound/Learned/unsubscribe semantics.

- Requests share exact sender/causation/type matching, bounded retained-reply recovery, outcomes and deadlines; awaited delivery and binding cycles fail promptly.
- Agents use one asynchronous prepared-tool contract. The shared SDK supports explicit admitted catalogs over STDIO and authenticated HTTP; obsolete service/tool adapters were removed.
- Gmail and Salesforce are real specialist agents with native MCP schemas, principal-bound credentials, exact-target read continuation, trusted write previews and refreshed-schema confirmation.
- Module-owned graph labels/icons flow through the graph API and UI kit. Google and Salesforce contracts are available to scripts.
- Integration review additionally corrected transport-display-name authorization comparisons, trusted preview screening, missing Gmail continuation bindings, and confirmation-time Gmail catalog refresh.

Validation on 2026-09-05: 197 simulation, 95 substrate, 31 scripting, 59 Aspire hosting and 18 graph/HTTP tests passed. The simulation suite includes 13 HTTP MCP and 13 STDIO MCP cases, 11 real-silo continuation cases and sensitive-telemetry tests. Flutter verification includes 34 chat/login/graph tests plus the earlier 16 affected model/kit/graph tests; seven changed Dart files passed analysis. A solution restore refreshed runtime dependency manifests for the new scripting contract references.

Live verification and the remaining user-login requirement are recorded in the [implementation handoff](../reviews/2026-09-05-specialist-refactor-verification.md). No live email drafts or Salesforce writes were performed.
