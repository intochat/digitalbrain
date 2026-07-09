# DigitalBrain Architecture V2 Implementation Plan

Planning snapshot: repository branch **master** at commit **2edfe85**, 2026-07-10. The assessed application code is unchanged from the commit evaluated in [architecture.md](architecture.md); the intervening commit only added that assessment.

**Evidence convention and limitations**

- **[R] Repository evidence** — current source, configuration, tests, CodeGraph call paths, dependency edges, and blast radius. CodeGraph was used before targeted reads.
- **[L] Live runtime evidence** — read-only Aspire MCP inspection. No AppHost or resource was started, stopped, restarted, rebuilt, or reconfigured.
- **[D] Official documentation** — current Microsoft Learn, Aspire, Orleans, OpenTelemetry, MCP, Flutter/RFW, Google, and Salesforce sources.
- **[I] Architectural inference** — a V2 recommendation derived from evidence rather than behavior already present.

Context7 was invoked first for Orleans, Aspire, OpenTelemetry .NET, the MCP C# SDK, Flutter/RFW, Microsoft.Extensions.AI, and Microsoft Agent Framework. Every library-resolution request returned “Monthly quota reached,” so no Context7 document payload was available. Current primary documentation is linked as the fallback. DigitalBrain MCP tools were not exposed to this Codex session; live timeline, causal-lineage, Ino-status, proposal, and workbench queries therefore could not be run. Aspire MCP exposes no live metrics query, and no deployed production environment was inspected. No file-based AGENTS.md exists in the repository; the instructions supplied with this task were the applicable AGENTS.md instructions.

This document is an execution plan, not a replacement for the findings in [architecture.md](architecture.md).

Current repository constraints include .NET 11 targets, Aspire 13.4.6, Orleans 10.2 with 10.2.1-preview.1/alpha journaling packages, Microsoft.Extensions.AI 10.7.0, Microsoft.Agents.AI 1.13.0, MCP 1.4.0, and OpenTelemetry 1.15–1.16 pins in [Directory.Packages.props](../Directory.Packages.props). These versions reinforce the historical-byte and API compatibility gates; this plan does not propose a dependency upgrade. **[R]**

## 1. Executive summary

Architecture V2 changes DigitalBrain from a globally addressed, caller-scoped actor application into an authenticated, tenant/workspace-partitioned actor application with explicit commands, immutable versioned events, durable effects, resumable workflows, and purpose-built read models. It adds those boundaries around the existing product rather than replacing it. **[I]**

V2 intentionally keeps Orleans grains and stable grain identities, Aspire orchestration, causal journals, the typed model registry, Google and Salesforce adapters, server-driven Flutter UI, and the current deployable monolith. A microservice split, a new workflow platform, a new event broker, or a full UI rewrite is not on the critical path. **[R][I]**

Recommended execution order:

1. Characterize current behavior and contain secret/authorization hazards.
2. Establish authenticated request context, tenant/workspace membership, and legacy-key resolution.
3. Add versioned command/event contracts and V1 compatibility adapters.
4. Add the durable workflow state machine and grain-owned outbox/inbox.
5. Build replayable timeline, causality, workflow, connector, memory, and feed projections.
6. Extract Ino planning, context, model, and tool boundaries one responsibility at a time.
7. Move OAuth and MCP behind authenticated application/query ports.
8. Version and privatize the UI surface/feed protocol.
9. Converge Development, Test, and Production topology and telemetry.
10. Remove legacy paths only after measured zero-use and replay/rollback gates pass.

The critical path is:

**redaction/characterization → identity decision → RequestContext/resource policy → fail-closed capability-isolation gate → grain-key resolver → aliases/upcasters → sequenced owner enrollment/immutable aggregate commit → outbox/inbox recovery → approval/apply → projections → MCP/UI/connector cutovers → legacy retirement.**

The largest risks are unreadable historical journals, duplicated or unknowable external effects, cross-workspace disclosure, lost OAuth credentials during rotation, and a grain-key cutover that creates parallel logical actors. ADR-001 through ADR-006 block the core durability path; ADR-008 and ADR-014 additionally block authenticated/remote MCP containment, and ADR-013 blocks secret-safe connector/OAuth migration. Projection backend selection, a future service split, and optional Agent Framework durability are non-blocking.

The smallest safe first release is a **V2 containment release**: central secret classification/redaction, authenticated and authorized HTTP/gRPC/MCP ingress, Production HTTP mutation MCP disabled, additive request/actor contracts, characterization tests, and a fail-closed capability-isolation gate. It does not re-key grains, change journal serialization, move Ino, introduce retries, or enable a second user/workspace on global V1 capabilities. It preserves the proved sole-owner journey while unsafe capabilities return typed Unavailable.

## 2. Goals, constraints, and non-goals

### Architectural goals

- Make principal, tenant, workspace, aggregate, command, operation, correlation, and causation explicit at every mutation boundary.
- Guarantee that accepted state transitions and their pending effects share one proved durable transaction boundary.
- Make retries safe through durable idempotency, inbox deduplication, verification, and explicit unknown outcomes.
- Preserve and query causal history without scanning arbitrary grain journals.
- Expose one authorized tool capability model and one policy-owned model-routing boundary.
- Make UI and MCP protocols versioned, structured, scoped, resumable, and independently testable.
- Define one logical Aspire topology with explicit Development, Test, and Production capability profiles.

### Product invariants

- A self-evolution change is never applied without the policy-required approval and an authenticated approver.
- The proved existing sole owner can keep signing in, chatting, connecting supported providers, approving proposals, and rendering addressed V1 surfaces; a new user/workspace receives typed Unavailable until each capability proves isolation.
- Server-driven UI remains constrained by the Flutter host’s compiled vocabulary.
- Journals remain durable causal evidence; they are not silently repurposed as a delivery guarantee.
- Local-first Ollama, embeddings, and Whisper remain supported Development capabilities.

### Compatibility requirements

- Every historical journal accepted before V2 remains readable or is quarantined with a deterministic, operator-visible reason; no record is silently dropped.
- Existing Orleans aliases, member IDs, grain types, and keys are not renamed in place. Orleans logical identity is type plus key, so a new key denotes a different grain. See [Orleans grain references](https://learn.microsoft.com/en-us/dotnet/orleans/grains/grain-references). **[D]**
- V1 Synapse, UiSurface, RfwCard, gRPC, and MCP consumers continue through adapters until explicit removal gates pass.
- Legacy global IDs map only to the personal/default scope and never become a wildcard for every tenant.
- Existing encrypted PackConfig credentials remain usable while credential references are introduced.

### Operational constraints

- Use expand/migrate/contract changes, feature flags, shadow reads, parallel projections, and bounded dual-write periods.
- Keep external effects out of storage transactions. Google, Salesforce, model providers, process control, and deployment cannot be atomically committed with Orleans state.
- Treat Orleans reminders as durable wake-ups, not a work queue: missed ticks are not individually persisted. See [timers and reminders](https://learn.microsoft.com/en-us/dotnet/orleans/grains/timers-and-reminders). **[D]**
- Never use tenant, workspace, command, workflow, proposal, operation, or tool-invocation IDs as metric labels.
- Prefer a single deployable kernel until measured isolation, scaling, release cadence, or fault containment requires a split.

### Explicit non-goals

- Replacing Orleans, Aspire, Flutter/RFW, the model registry, or connector SDKs.
- Exactly-once network delivery or rollback of an irreversible external effect.
- A service mesh, enterprise event bus, dedicated vector database, or marketplace implementation before access patterns justify one.
- Replaying historical commands as if they were facts.
- Moving all client navigation, local interaction state, or rendering logic to the server.
- Calendar estimates unsupported by measured throughput or team capacity.

### Assumptions requiring confirmation

| Assumption | Why it matters | Decision owner |
|---|---|---|
| Personal tenant plus default workspace is the migration default, but workspace is a security boundary | Drives keys, authorization, backfill, and isolation tests | Product/security |
| Human approval remains mandatory for self-evolution and connector write effects unless a policy explicitly permits auto-approval | Drives workflow and tool policy | Product/security |
| Journals are authoritative audit/domain evidence with a defined retention policy, not an erasable chat cache | Drives schema, retention, and privacy design | Product/legal/operator |
| At-least-once delivery plus idempotent/verified effects is acceptable | Determines provider contracts and unknown-outcome handling | Product/operator |
| Production HTTP MCP is either disabled or protected by an OAuth resource-server boundary | Blocks remote mutation exposure | Security/operator |
| Aspire is the logical topology authority while Pulumi may remain the provisioning engine | Drives CI drift checks | Platform |
| Production RPO, RTO, SLO, residency, retention, and cost budgets will be supplied before the production cutover | Blocks final gates, not early additive work | Operator/product |

## 3. Current-to-target gap analysis

| Area | Current state **[R]** | Target state **[I]** | Concrete gap and consequence | Difficulty | Dependencies |
|---|---|---|---|---|---|
| Identity and authorization | [LoginRequest](../src/DigitalBrain.Core/Synapse.cs) carries a plaintext password through FireAsync; [GatewayService](../src/DigitalBrain.Kernel/Gateway/GatewayService.cs) resolves sessions from caller-supplied client IDs; the HTTP MCP host in [Program.cs](../src/DigitalBrain.Mcp/Program.cs) has no app authentication middleware | Edge-authenticated principal, server-derived RequestContext, resource authorization at ingress and handler | Caller assertion can become read/action/approval authority; secrets can enter journals | Critical | ADR-001, redaction, auth provider |
| Tenant/workspace isolation | WorkspaceIds is a string helper in [NeuronScope.cs](../src/DigitalBrain.Core/NeuronScope.cs); no tenant aggregate or membership model | Opaque tenant/workspace IDs, membership version, scoped policies and keys | Organizational labels are not security partitions | Critical | ADR-001, ADR-002 |
| Neuron/grain ownership | Global main grains and broad Neuron base behavior; startup warms ino-main, session-main, automation-main | One aggregate/workflow owner per grain; explicit application ports | Global activation state couples users and makes re-keying risky | High | key resolver, envelopes |
| Synapse and journal contracts | [Synapse](../src/DigitalBrain.Core/Synapse.cs) is both command and event; [JournalJson](../src/DigitalBrain.Kernel/Kernel/JournalJson.cs) persists CLR full names | Separate versioned command/event/effect contracts with stable aliases and upcasters | Renames/assembly absence can make history unreadable; replay can repeat commands | Critical | ADR-003, ADR-004 |
| Causation and lineage | [Neuron](../src/DigitalBrain.Kernel.Abstractions/Neuron.cs) stamps immediate cause, but direct DeliverAsync can bypass stamping and lineage scans one neuron | Required correlation/causation on accepted commands/events plus cross-neuron CausalEdge projection | Explanations are incomplete and scans grow without an index | High | envelopes, projections |
| Ino orchestration | [InoNeuron](../integrations/DigitalBrain.Ino/InoNeuron.cs) is 1,456 lines and one ino-main grain | Conversation owner plus application services for context, planning, tools, models, memory, and surfaces | Broad blast radius and mixed durability/security responsibilities | High | identity, tools, router, characterization |
| Tool invocation | [IInoToolProvider](../src/DigitalBrain.Kernel.Abstractions/IInoToolProvider.cs) receives only clientId and returns AIFunctions; tool completion journals result.ToString | Authorized capability catalog and durable typed invocation ledger | Raw connector data can persist; timeout/retry/idempotency semantics are absent | Critical | ADR-006, RequestContext, workflow |
| Self-evolution | [SelfEvolutionNeuron](../src/DigitalBrain.Kernel/SelfEvolution/SelfEvolutionNeuron.cs) records a decision then applies synchronously | Durable proposal/approval/apply/verify/compensate state machine | Crash after decision can lose, duplicate, or obscure an effect; rollback is only an event | Critical | outbox/inbox, workflow ADR |
| Model routing | Typed registry exists, but construction is repeated in [DigitalBrainChat.cs](../src/DigitalBrain.Kernel/Llm/DigitalBrainChat.cs), [DigitalBrainChatClientRegistration.cs](../src/DigitalBrain.Kernel/Llm/DigitalBrainChatClientRegistration.cs), and [ScopedChatClientFactory.cs](../src/DigitalBrain.Kernel/Llm/ScopedChatClientFactory.cs) | One provider factory and policy-owned router | No unified health, privacy, residency, budget, latency, or fallback decision | Medium | ADR-011, capability contract |
| Google/Salesforce | AuthNeuron and IConnector flows duplicate OAuth; state embeds user ID; the active Salesforce connector callback omits its stored PKCE verifier; Gmail exposes send under readonly scope | One OAuth coordinator, provider adapters, opaque state, PKCE, grant validation, rotation/revocation | CSRF/correlation complexity, divergent callbacks, insufficient scopes, credential-loss risk | Critical | identity, secret references |
| MCP | [DigitalBrainToolsBase](../src/DigitalBrain.Mcp/DigitalBrainToolsBase.cs) resolves grains directly; [DigitalBrainMutationTools](../src/DigitalBrain.Mcp/DigitalBrainMutationTools.cs) manufactures Synapses and accepts decided_by | Authenticated query/command/approval/admin ports with structured responses | Bypasses policy and makes caller-supplied identity authoritative | Critical | ADR-008, application/query ports |
| UI surfaces/feed | [UiSurface](../src/DigitalBrain.Ui.Contracts/UiSurfaces.cs) is an unversioned dictionary; [HomeFeedBus](../src/DigitalBrain.Kernel/Ui/HomeFeedBus.cs) uses caller-addressed plus shared in-memory streams | Versioned SurfaceEnvelope, server-resolved actions, private durable sequence feed | Cross-audience delivery, forged actions, no resume after loss | Critical | identity, projections, ADR-009 |
| Read models/indexing | Features rebuild in-memory projections or scan grain journals; there is no enumerable durable commit source | Idempotent checkpointed projections that enumerate registered owners and expose cursor queries | Without durable owner discovery, a crash after commit can strand an event from every projector; latency and coupling also grow with journal size | High | ADR-005/007, stable events, owner directory |
| Aspire/deployment | [AppHost.cs](../hosts/DigitalBrain.AppHost/AppHost.cs) defines local graph; [deploy/Program.cs](../deploy/Program.cs) separately defines production | Explicit profiles and topology snapshot/diff or generated handoff | Local/production capability and secret/telemetry drift | High | ADR-010, ADR-012 |
| Telemetry/operations | [ServiceDefaults](../hosts/DigitalBrain.ServiceDefaults/Extensions.cs) covers Kernel/Telegram, not MCP; browser proxy can acknowledge drops | End-to-end OTel, protected correlated spans/logs, bounded metrics, dashboards/SLOs | Incidents can lack MCP/client evidence; silent telemetry loss | High | telemetry schema, production collector |
| Testing/release | 372 C# Fact/Theory declarations and 15 Flutter tests, but CI does not run Flutter tests; no crash-window/container failover lane | Pyramid including historical replay, crash injection, three-silo/container/deployment/smoke gates | Core V2 guarantees can regress undetected | High | fault seams, test profiles |

## 4. Required architectural decisions

| ADR | Proposed decision and alternatives | Recommendation | Consequences and affected modules | Blocks implementation? |
|---|---|---|---|---|
| ADR-001 Tenant/workspace/principal model | Alternatives: remain single-user; user-only partition; tenant + workspace + principal | Adopt tenant + workspace + principal, initially personal/default. Client/session/connection IDs are routing only. | Adds membership/policy storage and RequestContext; affects Core, Gateway, MCP, Flutter, connectors, all scoped grains | **Yes** for key and auth migration |
| ADR-002 Grain-key format and legacy mapping | Alternatives: reinterpret existing strings; compound key; canonical opaque string plus resolver | Use <code>v2/t/{tenant}/w/{workspace}/a/{type}/{id}</code> with opaque base32/UUID IDs and a persistent LegacyGrainKeyMap. Never reinterpret old keys. | Longer keys and resolver hop; affects NeuronScope, warmup, MCP, Ino, sessions, automations, connector grains | **Yes** for re-keying; additive resolver can start |
| ADR-003 Versioned command/event envelope | Alternatives: extend Synapse indefinitely; replace it; evolve through adapters | Add CommandEnvelope, EventEnvelope, and EffectRequest; keep Synapse as a V1 adapter. | More explicit handlers and dual contracts; affects Core, Neuron, Gateway, MCP, all grains | **Yes** for durable effects |
| ADR-004 Journal schema/versioning | Alternatives: CLR full names; stable discriminator registry; migrate to another event store | Keep current storage initially; introduce stable type IDs, schema versions, upcasters, quarantine, and manifest tests. Orleans aliases remain stable. See [Orleans serialization](https://learn.microsoft.com/en-us/dotnet/orleans/host/configuration-guide/serialization). | Requires a historical corpus and controlled dual-read/write; affects JournalJson and every persisted event | **Yes** for V2 event writes |
| ADR-005 Atomic commit, discovery, and outbox/inbox storage | Alternatives: separate state/outbox writes; Orleans transactions; one grain-owned immutable commit log plus derived index | Use a contiguous CommitSequence and checksum-sealed AggregateCommit containing events, new immutable OutboxRecords, and append-only EffectTransitionRecords; atomically replace a rebuildable PendingEffectIndex in the same proved one-grain write. Before the first V2 commit, append the owner to a sharded CommitOwnerDirectory with atomic RegistrationSequence/epoch; reject if unavailable. High-water recovery scans pull commits by sequence; reminders are hints. Co-locate InboxRecord with receiver state. A shared inbox store or multi-grain transaction requires a later ADR/proof. | Larger commit tails, permanent sequenced directory entries, derived-index rebuild/compaction, and periodic scans; external calls remain outside transaction. Enrollment-before-failed-commit is harmless; commit-before-enrollment is forbidden. Affects Core persistence contracts, aggregate state, workflow/outbox/inbox, projections, TestKit, and storage. See [grain persistence](https://learn.microsoft.com/en-us/dotnet/orleans/grains/grain-persistence/) and [transactions](https://learn.microsoft.com/en-us/dotnet/orleans/grains/transactions). | **Yes** for workflow, projections, and at-least-once claims |
| ADR-006 Workflow execution/compensation | Alternatives: synchronous handler; external workflow engine; Orleans workflow grain + effect workers | Orleans workflow grain owns state; human approval records Approved audit + ApplyQueued/first effect atomically; recovery scan owns due work and reminders are hints; adapters apply/verify/compensate. Unknown outcomes never blind-retry. | Adds attempts, leases, verification, operations UI; affects SelfEvolution, tools, Foundry, connectors | **Yes** for apply migration |
| ADR-007 Projection/index technology | Alternatives: Azure Tables/Blobs + SQLite/vector abstraction; PostgreSQL/search service; new broker | Begin behind projection ports using the CommitOwnerDirectory as the enumerable source and existing Azure/SQLite capabilities as targets; benchmark before selecting a new store. | Avoids premature platform addition; may require later backend swap. Affects Kernel projection workers/query ports, timeline/causal/workflow/feed read models, MCP query tools, Flutter feed queries, AppHost storage, and TestKit replay fixtures | No; source registration, port, and checkpoint work starts now |
| ADR-008 MCP authentication/authorization | Alternatives: local-only; API key; MCP OAuth 2.1 resource server | Trusted stdio only in Development; HTTP uses OAuth resource metadata, audience-bound tokens, ASP.NET principal, and brain.read/act/approve/admin scopes. See [MCP authorization](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization). | Requires identity provider and scope policy; affects MCP host and deployment | **Yes** for Production HTTP mutation |
| ADR-009 Surface/action versioning | Alternatives: dictionary actions; signed self-contained token; opaque server-resolved token | Versioned token-free stored surface plus opaque server-resolved token. Binding-wide use record atomically queues a preidentified command through outbox; signed tokens are limited to offline-safe non-sensitive actions and still use server-side idempotency. | Requires feed/action/use projection, outbox command submission and V1 adapter; affects UI contracts/runtime, Gateway, Flutter | **Yes** for private feed/action cutover |
| ADR-010 Capability profiles | Alternatives: environment conditionals; explicit immutable profile manifest | Define Development, Test, Production profile manifests and propagate them to every resource. Aspire environments are not automatically child runtime environments. See [Aspire environments](https://aspire.dev/deployment/environments/). | Makes unavailable capabilities explicit; affects AppHost, settings UI, tests, deploy | No; profile snapshot can start |
| ADR-011 Model-routing policy ownership | Alternatives: Ino role order; provider factory; application policy service | Application-owned IModelRouter consumes registry, policy, health, data classification, budgets, and provider capabilities; Infrastructure.AI constructs clients. | Central decision audit and fallback; affects Ino, LlmResponder, registries, AppHost | No; interface can be additive |
| ADR-012 Deployment authority | Alternatives: Pulumi only; Aspire deploy only; Aspire logical model with Pulumi projection/diff | Treat Aspire as logical topology; retain Pulumi provisioning until parity, with a normalized graph-diff and preview gate. | Temporary two-model maintenance becomes explicit and testable. Affects AppHost/AppHost.cs, DigitalBrain.Aspire builders, deploy/Program.cs, Pulumi stacks, CI preview/policy workflows, image publishing, and release verification | Blocks production convergence, not feature work |
| ADR-013 Credential ownership | Alternatives: secret-bearing PackConfig; provider vault only; secret reference abstraction over current store | Connector infrastructure owns encrypted secrets; domain events/state carry CredentialRef and grant metadata only. | Requires non-destructive migration and restore/key-ring testing. Affects Google/Salesforce AuthNeuron and connector adapters, PackConfig handlers, Gateway secret retrieval, credential storage/protection, OAuth callbacks, MCP/UI status projections, and deploy key management | **Yes** for OAuth consolidation |
| ADR-014 HTTP MCP mutation exposure | Alternatives: always enabled; permanently disabled; gated staged enablement | Disable in Production until ADR-001/008, application ports, idempotency, audit, and approval checks pass; enable commands then approvals, never admin by default. | Safest immediate containment; local stdio behavior remains. Affects DigitalBrain.Mcp/Program.cs, DigitalBrainMutationTools, application command/approval ports, profile manifests, HTTP ingress, deployment, and MCP contract tests | **Yes** for Production exposure, not internal port work |

## 5. Target technical contracts

The shapes below are language-level pseudocode. Stable type IDs and member IDs are assigned before implementation and never reused.

~~~csharp
record CauseRef(string Kind, string Id);

record CommandEnvelope(
  int EnvelopeVersion, string CommandType, int CommandSchemaVersion,
  CommandId CommandId, OperationId OperationId, IdempotencyKey IdempotencyKey,
  AggregateAddress Target, RequestContext Context,
  CorrelationId CorrelationId, CauseRef? Cause,
  DateTimeOffset IssuedAt, DateTimeOffset? ExpiresAt,
  DataClassification Classification, string PayloadHash, JsonElement Payload);

record EventEnvelope(
  int EnvelopeVersion, string EventType, int EventSchemaVersion,
  EventId EventId, OperationId OperationId, AggregateAddress Source,
  long AggregateSequence, PrincipalRef Actor,
  TenantId TenantId, WorkspaceId WorkspaceId,
  CorrelationId CorrelationId, CauseRef Cause,
  DateTimeOffset OccurredAt, DataClassification Classification,
  string PayloadHash, JsonElement Payload);

record RequestContext(
  int ContextVersion, string AuthenticationScheme,
  PrincipalRef Principal, TenantId TenantId, WorkspaceId WorkspaceId,
  long MembershipVersion, IReadOnlySet<string> Roles, IReadOnlySet<string> Scopes,
  SessionId? SessionId, ConnectionId? ConnectionId,
  AuthAssurance Assurance, DateTimeOffset AuthenticatedAt,
  DateTimeOffset ExpiresAt);

record PersistedActorSnapshot(
  int SchemaVersion, PrincipalRef Principal,
  TenantId TenantId, WorkspaceId WorkspaceId,
  long MembershipVersion, string PolicyVersion,
  AuthAssurance Assurance, string AuthorizationDecisionId);

record AggregateAddress(
  TenantId TenantId, WorkspaceId WorkspaceId,
  string AggregateType, string AggregateId, string GrainKeyVersion = "v2");

record AggregateCommit(
  int SchemaVersion, CommitId CommitId, AggregateAddress Owner,
  long CommitSequence, long ExpectedRevision, long NewRevision,
  IReadOnlyList<EventEnvelope> Events,
  IReadOnlyList<OutboxRecord> NewEffects,
  IReadOnlyList<EffectTransitionRecord> EffectTransitions,
  DateTimeOffset CommittedAt, string CommitHash);

record CommitSourceRegistration(
  int SchemaVersion, AggregateAddress Owner, string DirectoryPartition,
  long DirectoryEpoch, long RegistrationSequence,
  DateTimeOffset EnrolledAt);

record DirectoryScanCursor(
  int SchemaVersion, string Consumer, string DirectoryPartition, long DirectoryEpoch,
  long ScanCycle, long CycleHighWaterSequence,
  long NextRegistrationSequence, DateTimeOffset UpdatedAt);

record OwnerCommitCursor(
  int SchemaVersion, string Consumer, AggregateAddress Owner, long NextCommitSequence,
  CommitId? LastCommitId, DateTimeOffset UpdatedAt);

record WorkflowRecord(
  WorkflowId WorkflowId, AggregateAddress Owner, string WorkflowType,
  OperationId OperationId, CorrelationId CorrelationId, CauseRef Cause,
  PrincipalRef Requester, EffectRisk Risk, string RequiredApproverClass,
  int SchemaVersion, WorkflowState State, long Revision,
  string ProposalContentHash, ApprovalRecord? Approval,
  IReadOnlyList<EffectAttempt> Attempts,
  DateTimeOffset CreatedAt, DateTimeOffset ApprovalExpiresAt,
  DateTimeOffset UpdatedAt,
  DateTimeOffset? NextActionAt, DateTimeOffset? WorkflowDeadline,
  int MaxAttempts, string? FailureCategory);

record ApprovalRecord(
  int SchemaVersion, DecisionId DecisionId, ApprovalDecision Decision,
  PrincipalRef Approver, long MembershipVersion, string PolicyVersion,
  string RequiredApproverClass, string ProposalContentHash,
  DateTimeOffset DecidedAt, string RedactedReason);

record EffectAttempt(
  int SchemaVersion, AttemptId AttemptId, EffectId EffectId,
  OperationId OperationId, IdempotencyKey IdempotencyKey, int Number,
  EffectAttemptState State, Lease Lease, DateTimeOffset StartedAt,
  DateTimeOffset Deadline, DateTimeOffset? CompletedAt,
  string AdapterId, int AdapterVersion, string? ProviderOperationId,
  string? FailureCategory, RetryAdvice? Retry,
  EffectAttemptOutcome? Outcome, VerificationOutcome? Verification,
  string RedactedAuditSummary);

record OutboxRecord(
  int SchemaVersion, EffectId EffectId, OperationId OperationId,
  IdempotencyKey IdempotencyKey, AggregateAddress Owner,
  CorrelationId CorrelationId, CauseRef Cause,
  string Destination, string EffectType,
  JsonElement PayloadOrReference, DataClassification Classification,
  long CommitSequence, long EffectSequence, int EffectOrdinal,
  DateTimeOffset DueAt);

record EffectTransitionRecord(
  int SchemaVersion, TransitionId TransitionId, EffectId EffectId,
  OperationId OperationId, CorrelationId CorrelationId, CauseRef Cause,
  long TransitionSequence, OutboxState From, OutboxState To,
  AttemptId? AttemptId, Lease? Lease, DateTimeOffset? NextDueAt,
  DateTimeOffset OccurredAt, string? OutcomeCode, string? ResultHash);

record PendingEffectIndexEntry(
  int SchemaVersion, EffectId EffectId, long EffectSequence, OutboxState CurrentState,
  int AttemptNumber, long LastTransitionSequence,
  Lease? Lease, DateTimeOffset? NextDueAt);

record InboxRecord(
  int SchemaVersion, EffectId EffectId, OperationId OperationId, AggregateAddress Receiver,
  CorrelationId CorrelationId, CauseRef Cause, string Handler,
  DateTimeOffset FirstSeenAt, DateTimeOffset CompletedAt,
  string Outcome, string ResultHash);

record ToolCapabilityDescriptor(
  string CapabilityId, int Version, string Provider, string Operation,
  JsonSchema InputSchema, JsonSchema OutputSchema, EffectRisk Risk,
  IReadOnlySet<string> RequiredAppScopes, IReadOnlySet<string> RequiredConnectorGrants,
  ApprovalPolicy Approval, RetryPolicy Retry, Duration Timeout,
  IdempotencySupport Idempotency, DataPolicy DataPolicy);

record ToolInvocationRequest(
  int SchemaVersion, InvocationId InvocationId, OperationId OperationId,
  ToolCapabilityDescriptorRef Capability, RequestContext Context,
  CorrelationId CorrelationId, CauseRef Cause,
  DateTimeOffset Deadline, IdempotencyKey IdempotencyKey, JsonElement Arguments);

record ToolInvocationResult(
  int SchemaVersion, InvocationId InvocationId, OperationId OperationId,
  CorrelationId CorrelationId, CauseRef Cause, ToolOutcome Outcome,
  JsonElement? StructuredOutput, CredentialChallengeRef? AuthChallenge,
  string RedactedAuditSummary, string? ProviderOperationId,
  RetryAdvice? Retry, DataClassification Classification);

record OAuthFlowRecord(
  int SchemaVersion, OAuthFlowId FlowId, OperationId OperationId,
  CorrelationId CorrelationId, CauseRef Cause,
  string Provider, PrincipalRef Principal,
  TenantId TenantId, WorkspaceId WorkspaceId,
  string StateKeyVersion, string StateLookupHmac,
  OAuthFlowState State, long Revision,
  SecretRef PkceVerifierRef, Uri RedirectUri, IReadOnlySet<string> RequestedScopes,
  IReadOnlySet<string> RequestedGrants, SecretRef? AuthorizationCodeRef,
  EffectId? ExchangeEffectId, Lease? ExchangeLease,
  CredentialRef? ResultCredentialRef,
  DateTimeOffset StartedAt, DateTimeOffset ExpiresAt,
  DateTimeOffset? CallbackClaimedAt, DateTimeOffset? CompletedAt,
  DateTimeOffset? NextActionAt, string? FailureCategory);

record ConnectorCapabilityDescriptor(
  string Provider, string CapabilityId, int Version,
  IReadOnlySet<string> Operations, IReadOnlySet<string> RequiredScopes,
  IReadOnlySet<string> RequiredGrants, EffectRisk Risk,
  bool SupportsIdempotency, bool SupportsVerification,
  bool SupportsCompensation, CredentialSharingPolicy Sharing,
  DataPolicy DataPolicy);

record ModelRoutingRequest(
  int SchemaVersion, OperationId OperationId, RequestContext Context, ModelRole Role,
  CorrelationId CorrelationId, CauseRef Cause,
  RequiredCapabilities Required, DataClassification Classification,
  ResidencyPolicy Residency, TokenBudget TokenBudget, MoneyBudget CostBudget,
  Duration LatencyBudget, bool AllowFallback);

record ModelPolicyDecision(
  int SchemaVersion, string PolicyVersion, string RegistrySnapshotId,
  string SelectedRegistrationId,
  IReadOnlyList<string> AllowedFallbacks, string ReasonCode,
  IReadOnlyList<string> AppliedConstraints);

record ModelRoutingResult(
  int SchemaVersion, OperationId OperationId,
  CorrelationId CorrelationId, CauseRef Cause,
  ModelPolicyDecision Decision,
  string UsedRegistrationId, ModelOutcome Outcome,
  long InputTokens, long OutputTokens, MoneyAmount? Cost,
  Duration Latency, bool FallbackUsed, string RedactedAuditSummary);

record ProjectionCheckpoint(
  string Projection, string SourcePartition, long SourcePosition,
  EventId? LastEventId, string ProjectionVersion,
  DateTimeOffset UpdatedAt, string StateHash);

record SurfaceEnvelope(
  int ProtocolVersion, string SurfaceSchema, int SurfaceSchemaVersion,
  SurfaceId SurfaceId, long Revision, TenantId TenantId, WorkspaceId WorkspaceId,
  Audience Audience, long FeedSequence, DateTimeOffset CreatedAt,
  DateTimeOffset? ExpiresAt, CorrelationId CorrelationId, CauseRef Cause,
  IReadOnlySet<string> RequiredClientCapabilities, string ContentHash,
  SurfacePayload Payload, IReadOnlyList<UiActionRef> Actions);

record UiActionRef(
  int ActionSchemaVersion, string BindingId, string ActionType, string ActionToken,
  SurfaceId SurfaceId, long SurfaceRevision, DateTimeOffset ExpiresAt);

record StoredSurfaceRecord(
  int ProtocolVersion, string SurfaceSchema, int SurfaceSchemaVersion,
  SurfaceId SurfaceId, long Revision, TenantId TenantId, WorkspaceId WorkspaceId,
  Audience Audience, long FeedSequence, DateTimeOffset CreatedAt,
  DateTimeOffset? ExpiresAt, CorrelationId CorrelationId, CauseRef Cause,
  IReadOnlySet<string> RequiredClientCapabilities, string StableContentHash,
  SurfacePayload Payload, IReadOnlyList<UiActionBindingRef> ActionBindings);

record UiActionBindingRef(
  int ActionSchemaVersion, string BindingId, string ActionType,
  string CommandTemplateId, int CommandTemplateVersion,
  string CommandTemplateHash, string InputSchemaRef,
  string IdempotencyNamespace, DateTimeOffset ExpiresAt, int MaxUses);

record UiActionBindingUsage(
  int SchemaVersion, string BindingId, long Revision, int Uses, int MaxUses,
  OperationId? LastOperationId, DateTimeOffset UpdatedAt);

record UiActionUseRecord(
  int SchemaVersion, UseTransitionId TransitionId,
  string BindingId, int UseOrdinal, long TransitionSequence,
  string TokenHash, string CanonicalInputHash,
  IdempotencyKey IdempotencyKey, OperationId OperationId,
  CorrelationId CorrelationId, CauseRef Cause,
  EffectId CommandSubmissionEffectId, UiActionUseState State,
  DateTimeOffset OccurredAt, string? FailureCode);

record IssuedUiActionToken(
  int SchemaVersion, string TokenHash, string BindingId, PrincipalRef Principal,
  TenantId TenantId, WorkspaceId WorkspaceId,
  SurfaceId SurfaceId, long SurfaceRevision,
  DateTimeOffset IssuedAt, DateTimeOffset ExpiresAt,
  DateTimeOffset? ConsumedAt);

enum McpScope {
  BrainRead,
  BrainAct,
  BrainApprove,
  BrainAdmin
}
~~~

### Contract rules

CauseRef.Kind is a versioned allowlist such as command, event, workflow-transition, effect, tool-invocation, OAuth-flow, model-call, or surface; Id is the corresponding opaque stable identifier. Only a root CommandEnvelope may omit CauseRef. A cause never embeds a payload, provider token, or human-readable secret-bearing description.

| Contract | Ownership and persistence | Validation | Versioning and redaction | Correlation/causation and V1 compatibility |
|---|---|---|---|---|
| CommandEnvelope | Application ingress creates the in-memory message; owning grain persists a receipt with command/idempotency/hash, target, PrincipalRef, membership/policy version, and decision—not the full RequestContext | Authenticated unexpired context; membership; target scope match; schema; payload hash; command/idempotency uniqueness | Envelope and payload versions are independent; secret fields are rejected or converted to SecretRef before persistence; session/connection/claims are excluded from receipts | New root gets correlation = command ID and no CauseRef; child commands preserve correlation and identify the causing command, event, workflow transition, or effect by typed CauseRef. V1 Synapse adapter creates a command with personal/default scope |
| EventEnvelope / Synapse evolution | Owning aggregate emits immutable facts inside AggregateCommit | Server time; sequence exactly previous + 1; actor and scope from accepted command; registered type/schema | Stable explicit event ID, upcaster chain, classification; no raw credentials, tokens, passwords, connector bodies, or action tokens | CauseRef.Kind is a registered bounded discriminator and identifies the command/event/effect/transition that caused the fact. A V1 projection can synthesize Synapse fields without becoming source of truth |
| RequestContext / PersistedActorSnapshot | Edge derives an ephemeral context from ClaimsPrincipal and membership; receipts/events persist the separate allowlisted snapshot | Never accept principal/tenant/roles from payload; supported ContextVersion and AuthenticationScheme; expiry, assurance, membership version, resource policy. Snapshot scope/principal must equal the accepted context | Both schemas are explicit; tokens, full claims, roles/scopes, session/connection IDs, and trace context are excluded from the snapshot | Trace context stays in OpenTelemetry propagation; business correlation is explicit. ClientId maps only to ConnectionId |
| Tenant/workspace-aware identity | Identity/membership application module; mapping store persists old-to-new addresses | Opaque canonical IDs, normalized type, no path traversal/PII; tenant/workspace must match context | Key version prefix; legacy resolver is additive | Existing keys remain existing grains. Only personal/default adapters route old main IDs to scoped owners |
| AggregateCommit/effect state | Owning aggregate persists immutable, checksum-sealed commits plus a replaceable PendingEffectIndex derived from append-only EffectTransitionRecords in the same one-grain state document | CommitSequence is contiguous even for zero-event commits; EventEnvelope, EffectSequence, EffectOrdinal, and transition sequence are deterministic/unique; index must reproduce from transitions | Commit/event/effect/transition schemas upcast independently; a sealed commit is never edited. Snapshot/index compaction is replaceable only from sealed history | All records retain operation/correlation/cause. Current effect state comes from the index/last transition, never by mutating an OutboxRecord inside an old commit |
| Workflow/effect records | Workflow grain owns WorkflowRecord; AggregateCommit owns new immutable effect intents/transitions; attempts are durable | Legal transition, monotonic revision, requester/risk/approver class, content hash, human approval or explicit system actor, bounded attempts/deadline | Independent workflow/effect/approval schema versions; sanitized provider details and SecretRef only | Operation/correlation fixed for workflow lifetime; each attempt/effect gets an ID and typed CauseRef to its prior transition. V1 proposal/result events are projected |
| Commit source registration/cursor | A sharded durable directory appends permanent RegistrationSequence; each consumer repeatedly scans complete registration cycles and owns per-owner commit cursors | Pre-enroll owner; canonical partition; idempotency; epoch/cursor/cycle monotonicity. At cycle start capture high-water; on completion reset next sequence to 1 and increment cycle so old owners are revisited. V2 never deletes registrations | Schemas contain no payloads/secrets; repartitioning increments DirectoryEpoch and requires manifest/cursor migration | Append-only sequence prevents insertion behind a cycle cursor; new entries above captured high-water enter the next cycle. Per-owner cursor pulls only new CommitSequence. V1-only grains require explicit enrollment/backfill |
| OutboxRecord / effect transitions | Immutable OutboxRecord is created inside its AggregateCommit; later commits append EffectTransitionRecord and atomically replace PendingEffectIndexEntry | Unique EffectId, CommitSequence, aggregate-global EffectSequence and per-commit EffectOrdinal; registered destination; lease/state transition rules; immutable input/idempotency hash | Stable effect/transition types and upcasters; payload is schema-valid or encrypted/blob reference; current index is derived and rebuildable | Delivery retains operation, correlation, and typed CauseRef. Legacy FireAsync remains at-most-once until migrated |
| InboxRecord | The receiver grain owns it in the same one-grain state transaction as resulting state/events | Unique receiver + EffectId; payload hash conflict rejects; result hash immutable after success | Compact stable schema; no raw result. A shared inbox store is prohibited until a separate ADR proves atomicity | Duplicate returns recorded outcome without re-running handler. V1 handlers are wrapped only where behavior is idempotent |
| Operation/idempotency IDs | Operation ID is system-wide logical work; idempotency key is caller/business retry identity | Opaque, bounded, scoped to principal + target + command type; payload-hash conflict rejects | IDs are never recycled; not metric labels | Retries reuse both; child effects reuse operation but receive new effect IDs |
| ToolCapabilityDescriptor | Application tool catalog; provider adapter registers descriptors | Stable catalog ID, bounded schemas, risk, grants, timeout, retry, data policy | Descriptor version participates in policy decision; descriptions contain no secrets | Replaces string-only IInoToolProvider exposure through an adapter |
| ToolInvocationRequest/Result | Durable invocation coordinator owns ledger; raw output stays in governed connector storage when needed | Reauthorize at exposure and execution; deadline; schema; budgets; idempotency | Typed outcomes: Success, NeedsAuth, Denied, RetryableFailure, PermanentFailure, OutcomeUnknown, Cancelled. Audit summary is separately redacted | Invocation span links to command/cause. Current string tool results are wrapped as LegacyText and never journaled raw |
| Connector capability/OAuth | OAuth coordinator owns the versioned state-machine record; provider adapter owns protocol; secret store owns code/verifier/tokens/credentials. The OAuth flow grain is keyed by server-HMAC(state), so callback lookup needs no second index | Started → Claimed → ExchangeQueued/Exchanging → Succeeded/Failed/OutcomeUnknown/ReauthorizationRequired/Expired; exact redirect, S256, grant/scope, lease/revision and replay checks | Only HMAC lookup, SecretRefs, CredentialRef, grant version, and sanitized failure persist; raw state/code/token never journal. Flow schema upcasts independently | Callback and exchange retain originating operation/correlation/cause. Existing PackConfig keys are read through credential adapter |
| Model route request/decision/result | Application policy owns decision; Infrastructure.AI owns clients; audit projection stores decision metadata | Registration/capability/health, tenant policy, residency, privacy, budget, latency, tool/structured-output requirements | Contract/policy versions and exact registry snapshot ID recorded; prompts/responses governed separately | Request/result retain operation, correlation, and typed cause; current role resolver delegates behind feature flag |
| ProjectionCheckpoint | Projection worker owns a DirectoryScanCursor plus per-owner OwnerCommitCursor | Monotonic registration/CommitSequence positions, event-id dedup, projection version/state hash; a newly registered owner cannot be skipped | Rebuild uses a new projection version/table then atomic alias switch | Workers enumerate registrations and pull sealed AggregateCommits by CommitSequence. V1 and V2 readers can run in parallel; source event reference is retained |
| SurfaceEnvelope / StoredSurfaceRecord | Surface composer persists token-free StoredSurfaceRecord; delivery materializes a wire-only SurfaceEnvelope | Audience scope, sequence, revision, expiry, capability negotiation, payload schema; stable content hash excludes bearer tokens and covers stable payload/action bindings | Protocol + surface/action schema versions; sensitive props prohibited; RFW binary/text content classified; raw ActionToken is never durable | CauseRef points to producing work. Every initial/catch-up delivery mints fresh short-lived tokens for stored bindings; V1 UiSurface is wrapped by a protocol-v1 adapter |
| UI action | Action owner resolves token to stable template/schema and owns append-only UiActionUseRecords plus derived binding-usage index | In one action-owner commit, consume token, claim binding use ordinal, preassign OperationId/idempotency, append CommandQueued use transition, and add command-submission OutboxRecord. Downstream command reauthorizes and deduplicates. No cross-grain atomicity is claimed | Persist binding, use transitions/index and token hash only; bearer never logs/journals. Template/action/use schemas are stable. Accepted/failed transitions append after downstream receipt | Reissued tokens share MaxUses. Crash before owner commit consumes nothing; crash after it is recovered by outbox; crash after target acceptance returns preassigned OperationId through idempotent receipt/status |
| MCP query/command/approval/admin scopes | MCP adapter maps ClaimsPrincipal to RequestContext and calls ports | brain.read, brain.act, brain.approve, brain.admin are independent; resource/audience and Origin validated; pagination/rate/size limits | StructuredContent schema version and typed errors; redaction by scope; annotations are hints only | MCP request creates/continues operation correlation. Direct IGrainFactory access is removed after parity |

## 6. Durable workflow design

### Proposal/approval/apply state machine

~~~mermaid
stateDiagram-v2
    [*] --> Proposed
    Proposed --> AwaitingApproval: validate and stage
    Proposed --> Rejected: invalid / policy rejects
    Proposed --> Cancelled: authorized withdrawal
    AwaitingApproval --> Approved: authorized approval; same commit continues
    AwaitingApproval --> Rejected: authorized human rejection
    AwaitingApproval --> Expired: trusted system deadline
    AwaitingApproval --> Cancelled: authorized withdrawal
    Approved --> ApplyQueued: same atomic commit + first effect
    ApplyQueued --> Cancelled: cancel before dispatch
    ApplyQueued --> Applying: acquire durable attempt
    Applying --> Cancelled: cancel before provider commit point
    Applying --> Succeeded: effect confirmed
    Applying --> RetryScheduled: classified transient failure
    RetryScheduled --> ApplyQueued: due-work scan
    RetryScheduled --> Failed: attempt/deadline budget exhausted
    RetryScheduled --> Cancelled: verified no effect + authorized cancel
    Applying --> OutcomeUnknown: timeout / ambiguity / cancel after dispatch
    OutcomeUnknown --> Succeeded: verification confirms
    OutcomeUnknown --> RetryScheduled: verification proves no effect
    OutcomeUnknown --> ManualIntervention: cannot verify safely
    Applying --> Failed: permanent failure
    Failed --> CompensationQueued: compensatable partial effect
    OutcomeUnknown --> CompensationQueued: effect confirmed but target outcome rejected
    CompensationQueued --> Compensated: compensation verified
    CompensationQueued --> ManualIntervention: compensation unknown / failed
    Failed --> ManualIntervention: non-compensatable
    Failed --> [*]: closed / no compensation required
    ManualIntervention --> ApplyQueued: operator proves no effect and authorizes retry
    ManualIntervention --> CompensationQueued: operator selects compensating action
    ManualIntervention --> Succeeded: operator verifies desired effect
    ManualIntervention --> Failed: operator closes without desired effect
    Succeeded --> [*]
    Rejected --> [*]
    Expired --> [*]
    Cancelled --> [*]
    Compensated --> [*]
~~~

### Legal transitions

| From | To | Required durable write and guard |
|---|---|---|
| Proposed | AwaitingApproval | Validated proposal, content hash, policy/version, risk, owner scope, expiry, and required approver class |
| Proposed | Rejected | Stable rejection category and redacted reason |
| Proposed/AwaitingApproval | Cancelled | Authenticated requester or policy-authorized operator, expected revision, reason, and proof that no effect has been queued or dispatched |
| AwaitingApproval | Approved then ApplyQueued | One AggregateCommit records authenticated human PrincipalRef, membership/policy version, decision, reason, timestamp, proposal hash match, Approved audit transition, ApplyQueued current state, and first immutable OutboxRecord. New V2 work never rests in Approved |
| AwaitingApproval | Rejected | Authenticated human PrincipalRef, membership/policy version, reason, timestamp, proposal hash match; no effect is created |
| AwaitingApproval | Expired | Trusted system-clock/policy actor records the expired deadline and policy version; no human approver is fabricated |
| Approved | ApplyQueued | Recovery-only for imported/older Approved state: NextActionAt is required and scanner deterministically appends the first effect. Missing/invalid recovery metadata moves to ManualIntervention rather than guessing |
| ApplyQueued | Cancelled | Authorized cancel command consumes the unleased pending effect in the same commit; no provider call has begun |
| ApplyQueued | Applying | Increment attempt; persist worker/activation lease, lease expiry, deadline, adapter version, and idempotency key before calling provider |
| Applying | Cancelled | Cancellation is observed and durably recorded before the adapter's declared provider commit point; otherwise transition to OutcomeUnknown |
| Applying | Succeeded | Persist provider operation reference, verified result hash, sanitized summary, and completion event |
| Applying | RetryScheduled | Only a classified transient failure before a confirmed irreversible effect; persist category and next due time |
| RetryScheduled | Failed | Maximum attempt count or workflow deadline is exhausted; persist RetryBudgetExhausted or DeadlineExceeded and do not dispatch again |
| RetryScheduled | Cancelled | Authorized cancellation plus verification that the prior attempt produced no effect; otherwise use OutcomeUnknown/ManualIntervention |
| Applying | OutcomeUnknown | Timeout, lost response, crash suspicion, or provider ambiguity after the commit point |
| OutcomeUnknown | Succeeded | Provider-specific verification proves the desired effect occurred |
| OutcomeUnknown | RetryScheduled | Verification proves the effect did not occur and retry is safe |
| OutcomeUnknown | ManualIntervention | Verification is unavailable/inconclusive or retry could duplicate an effect |
| Applying | Failed | Permanent validation/auth/business/provider failure; no automatic retry |
| Failed | <code>[*]</code> | No effect occurred or no compensation/manual action is required; closure reason and evidence are durable |
| Failed/OutcomeUnknown | CompensationQueued | Policy permits compensation and evidence identifies the effect to reverse |
| CompensationQueued | Compensated | Compensation call and post-condition verified |
| CompensationQueued | ManualIntervention | Compensation is irreversible, failed, timed out, or is itself unknown |
| ManualIntervention | ApplyQueued/CompensationQueued/Succeeded/Failed | Authenticated brain.admin resolution command, current expected revision, evidence/reference, reason, policy decision, and explicit acknowledgement of external-effect risk |

### Execution rules

- **Durable attempts.** Each attempt records AttemptId, EffectId, operation/idempotency IDs, adapter/version, started/completed times, deadline, lease, error category, retry advice, provider operation reference, outcome, verification, and redacted summary. Attempts are append-only; WorkflowRecord points at the current one.
- **Approval atomicity.** A new approval command records the human ApprovalRecord, Approved audit transition, ApplyQueued current state, first effect intent, and pending-index entry in one AggregateCommit. Approved exists as an auditable transition, not a quiescent V2 current state. Imported legacy Approved state must have NextActionAt and a deterministic precomputed effect template; otherwise it becomes ManualIntervention and alerts.
- **Retry classification.** Adapters map failures to BeforeCommitTransient, RateLimited, AuthRequired, PermanentValidation, PermanentPolicy, AfterCommitUnknown, or Cancelled. Exponential backoff with bounded jitter is configuration, not domain state; the chosen next due time, maximum attempts, and workflow deadline are persisted. Provider Retry-After wins only within operator limits. Exhausted attempt/deadline budget transitions RetryScheduled to Failed; it never leaves immortal due work.
- **Idempotency.** Command retries reuse CommandId/OperationId/IdempotencyKey. Every effect has one stable EffectId and provider idempotency mapping. An input hash conflict on an existing idempotency key is rejected, not treated as a duplicate success.
- **Leases.** Orleans single-activation ownership prevents concurrent grain turns, but a persisted lease still identifies an in-flight attempt across deactivation, worker restart, or future external dispatchers. A new owner may take over only after expiry and must first verify any prior unknown effect.
- **Crash recovery.** On activation, reminder wake, and CommitOwnerDirectory recovery scan, inspect durable non-terminal records by NextActionAt. Recreate reminders as hints. Never infer completion from a missing reminder or an in-memory task.
- **Timeout/cancellation.** Caller cancellation stops waiting, not necessarily the durable workflow. Proposed/AwaitingApproval may be withdrawn by an authorized actor. ApplyQueued or Applying-before-provider-commit may become Cancelled; post-dispatch cancellation records OutcomeUnknown and schedules verification. AwaitingApproval expiry is a system-authored Expired transition, distinct from human Rejected. Deadlines are carried to adapters and end retry eligibility but do not erase durable work.
- **Verification.** Each side-effect adapter defines CanVerify, VerifyAsync, provider lookup key, and positive/negative/inconclusive outcomes. Unknown non-idempotent effects are never blind-retried.
- **Compensation versus rollback.** Rollback restores DigitalBrain-owned state to a checkpoint only when that is causally safe. Compensation creates a new audited external action that attempts a semantic inverse. Neither term claims time reversal; irreversible effects go to ManualIntervention with operator instructions.
- **Approver identity.** The application derives the human approver PrincipalRef and checks tenant/workspace membership, brain.approve scope, risk policy, separation-of-duties rule, and proposal content hash. Free-form DecidedBy becomes display metadata only. Expiry uses a distinct trusted system actor and cannot masquerade as approval/rejection by a person.
- **Audit.** Record every transition, policy version, actor, owner, operation/correlation/causation IDs, attempt, redacted outcome, verification, compensation, and manual resolution. Store no token, password, action token, raw prompt, email body, Salesforce row, or deployment credential.

## 7. Journal, outbox, inbox, and projection strategy

### Transaction boundaries

1. The adapter authenticates and submits a CommandEnvelope.
2. Before an owner can accept its first V2 event/effect-producing command, it idempotently enrolls its canonical AggregateAddress in a durable CommitOwnerDirectory partition. The partition atomically appends a RegistrationSequence within a DirectoryEpoch; it never inserts behind a cursor. Enrollment failure rejects that first V2 command; enrollment success followed by a failed/no-op commit is harmless. V2 registrations are permanent; deletion requires a future archive/retirement ADR and is not part of this plan.
3. The owning grain checks its co-located inbox/idempotency record and expected revision.
4. Domain logic produces the next state, EventEnvelopes, immutable new OutboxRecords, and any append-only EffectTransitionRecords without I/O. It assigns the next CommitSequence, event sequence(s), aggregate-global EffectSequence(s), per-commit EffectOrdinal(s), and transition sequence(s) deterministically.
5. One proved storage operation writes the new revision, checksum-sealed AggregateCommit, co-located inbox changes, and replaceable PendingEffectIndex derived from effect transitions. Old AggregateCommits/OutboxRecords are never mutated. Multiple named state writes are **not** assumed atomic. Orleans persistence explicitly requires WriteStateAsync and may report ETag conflicts. **[D]**
6. Only after the commit succeeds may the caller receive Accepted with OperationId. A best-effort directory dirty/latest-sequence hint may follow, but correctness never depends on that post-commit hint.
7. A recovery dispatcher repeatedly performs complete directory cycles. At cycle start it captures the partition's high-water RegistrationSequence; it processes from DirectoryScanCursor.NextRegistrationSequence through that closed range, persisting progress. At completion it increments ScanCycle, resets NextRegistrationSequence to 1, and captures a new high-water, so every permanent owner is revisited and new registrations above the prior mark enter the next cycle. For each owner it pulls commits after OwnerCommitCursor and the current PendingEffectIndex. To start work, it appends an Applying transition in a new AggregateCommit and updates the index atomically before calling the destination. A reminder may reduce latency but is not the only discovery path.
8. Grain receivers commit InboxRecord and resulting state/events in the same one-grain aggregate write. External receivers return a classified result that is committed to the workflow.
9. Projection workers use the same full-cycle/high-water rule, revisit all owners, pull only sealed AggregateCommits at or after OwnerCommitCursor.NextCommitSequence (including zero-event/effect-only commits), apply EventEnvelopes/relevant EffectTransitionRecords idempotently, and persist both cursors after target writes.

If the current Orleans journaling API cannot prove an atomic unit containing state/events/effects, the first V2 workflow grains use one named persistent AggregateState document containing revision/next sequences, compacted domain state, immutable commit tail, co-located inbox, and replaceable PendingEffectIndex. ADR-005 may later select transactional state, but no cross-store atomicity is claimed. **[I]**

AggregateCommit is an immutable durable journal unit, not a transient projection message. OutboxRecord is immutable intent; EffectTransitionRecord is immutable history; PendingEffectIndex is a replaceable/rebuildable current-state index. CommitOwnerDirectory is only the discovery log: aggregates remain authoritative. Partitions use stable hashing, append registration sequence within an epoch, and are backed up. Consumers repeatedly sweep sequence 1..captured high-water; per-owner cursors make revisits cheap. Repartitioning creates a new epoch plus audited manifest/cursor migration; it never silently renumbers entries. Compaction may replace derived domain state/index only after sealed commits are checksummed into the authoritative archive and required checkpoints have passed them.

### Delivery and ordering

- One aggregate serializes writes and assigns contiguous CommitSequence for every commit, including zero-event/effect-transition commits. Event AggregateSequence and aggregate-global EffectSequence are independently contiguous; EffectOrdinal orders multiple new effects inside one commit.
- The outbox dispatches by EffectSequence, then EffectOrdinal as a deterministic tiebreaker. It may skip a leased/not-due effect only under an explicit per-destination concurrency policy. No global order across grains/partitions is promised.
- Dispatch is at-least-once after a committed effect because every V2 owner was discoverable before commit and its derived index remains non-terminal until a terminal transition/receipt is committed; immutable intent/transition history can rebuild that index. The periodic scanner, not a reminder or post-commit enqueue, closes the crash-after-commit discovery window.
- Receiver inboxes make state transitions effectively-once for a stable EffectId.
- Provider effects are effectively-once only when the provider honors an idempotency key or verification proves the prior outcome. Otherwise an ambiguous result is OutcomeUnknown.
- Projection consumers enumerate registered owners, process CommitSequence in order, then event/effect-transition order, deduplicate EventId, and use cross-source occurred time only for presentation, never correctness.

[Orleans messaging is at-most-once by default without retries; retries can duplicate delivery and Orleans does not durably suppress duplicates](https://learn.microsoft.com/en-us/dotnet/orleans/implementation/messaging-delivery-guarantees). **[D]**

| Path | Guarantee | Explicit exclusions |
|---|---|---|
| Legacy FireAsync/DeliverAsync | At-most-once attempt after journal write | No automatic redelivery; crash can leave recorded intent without delivery |
| V2 aggregate commit | Atomic and ordered only within the selected/proved one-grain storage unit; immutable once sealed | No atomicity with another grain, directory, projection, stream, or external API |
| V2 owner discovery | At-least-once periodic rediscovery after pre-commit durable enrollment | No fixed wake-up latency; directory loss beyond backup RPO invalidates the guarantee and therefore fails readiness/release checks |
| V2 outbox dispatch | At-least-once delivery attempt from owner scan | No global ordering and no exactly-once network delivery |
| V2 inbox handler | Effectively-once state transition by EffectId | Inbox retention expiry can permit very old duplicates; retention must exceed replay window |
| Idempotent provider API | Effectively-once user-visible effect | Depends on documented provider key scope/retention |
| Non-idempotent provider API | At-least-once attempt or OutcomeUnknown | No automatic exactly-once and no blind retry after ambiguity |
| Projection write | At-least-once owner/commit consumption, effectively-once materialization by EventId | Read model can lag write model; no global cross-owner order |
| UI feed | At-least-once resumable delivery within configured retention, client dedup by sequence/surface revision | After retention the contract is ResetRequired/current snapshot; no synchronous guarantee that a device rendered it |
| Telemetry | Best effort | Never a source of truth; drops are measured but cannot be eliminated |

### Failure, replay, and retention behavior

- **Discovery recovery:** persist DirectoryScanCursor(epoch, cycle, cycle high-water, next registration) and per-owner next CommitSequence. Restart resumes the closed interval; completion resets to sequence 1 for the next cycle. Concurrent registrations append above the captured mark and enter the next cycle. Revisit is safe/cheap via owner cursors. Epoch mismatch, registration/commit gap, duplicate, or unreadable commit stops that partition. Directory backup/restore, repartition, and enrollment-vs-commit failpoints are release gates.
- **Duplicate suppression:** receiver + EffectId is the co-located inbox key; command dedup key is principal/target/command type/idempotency key plus payload hash.
- **Poison events/effects:** after bounded retries, quarantine with schema/type, source position, error category, first/last failure, and redacted sample hash. Advance only if the projection’s policy permits skip; authoritative workflows stop and require intervention.
- **Replay:** never invoke side-effect adapters. Replay mode applies facts to state/projections and verifies sequence/hash. V1 records pass through a deterministic adapter/upcaster.
- **Checkpointing:** persist directory epoch/cycle/high-water/next registration, OwnerCommitCursor.NextCommitSequence, last EventId, projection version, state hash, and update time after projection transaction. A checkpoint cannot lead rows. Captured registrations complete before cycle rollover; entries above high-water enter the next cycle; previously registered owners are revisited every cycle.
- **Projection rebuild:** write a new versioned target, replay from snapshot/origin, compare counts/hashes/sampled queries, catch up tail, atomically switch read alias, retain prior target for rollback.
- **Historical schema migration:** freeze a representative encrypted/redacted journal fixture corpus; map CLR full names to stable event aliases; add ordered pure upcasters; quarantine unknown records; never overwrite source blobs during the first migration.
- **Retention/archival:** domain/audit retention, connector raw-data retention, projection retention, inbox dedup horizon, and telemetry retention are separately configured. Archive immutable journal segments with manifest, checksum, encryption/key-version, and restore test. Erasure uses tombstone/redaction projections unless legal/product policy permits journal rewriting.
- **Partial outages:** directory unavailable rejects first V2 commits for unenrolled owners and fails mutation readiness if recovery cannot enumerate; already enrolled owners may commit only while the authoritative aggregate store is healthy and the directory is within the ratified recovery objective. Aggregate storage unavailable rejects commits. Projection outage leaves commands available if bounded lag policy permits; model/connector outage marks capability unavailable; outbox age grows and alerts; feed clients resume by cursor; telemetry outage never blocks domain commits.

## 8. Identity, tenancy, workspace, and authorization migration

### Boundary model

| Boundary | Authentication and context derivation | Authorization |
|---|---|---|
| HTTP | ASP.NET Core handler validates local/Entra/OIDC bearer or secure local session and builds ClaimsPrincipal | Resource policy derives tenant/workspace membership; CSRF/Origin checks for browser mutation |
| gRPC / gRPC-Web | Same ASP.NET pipeline; bearer/call credentials; Flutter stores only renewable client token | Method plus resource policy; no user/session/workspace accepted as authority from protobuf payload |
| MCP HTTP | MCP OAuth 2.1 resource server, Protected Resource Metadata, resource/audience validation | Per-tool brain.read, brain.act, brain.approve, brain.admin; discovery and invocation both filtered |
| MCP stdio | Development-only trusted local process profile; credentials from environment/OS boundary | Explicit local principal and scopes; no automatic Production enablement |
| Flutter | Login exchange produces a signed, expiring session/access token; connection ID remains routing metadata | Workspace selection must be a membership the server resolves; action token binds principal and surface |
| OAuth callback | Opaque state resolves OAuthFlowRecord; provider identity is configured route, not state content | State binds initiating principal/tenant/workspace/provider/redirect/scopes and is single-use |

ASP.NET Core and gRPC use ClaimsPrincipal and policy/resource authorization; the server, not request JSON, derives authority. See [ASP.NET authentication](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/?view=aspnetcore-10.0) and [gRPC authentication/authorization](https://learn.microsoft.com/en-us/aspnet/core/grpc/authn-and-authz?view=aspnetcore-10.0). **[D]**

### Migration sequence

1. Inventory every ingress, clientId, session lookup, global key, app/default config write, and secret-bearing message. Add deny/audit tests before behavior changes.
2. Add TenantId, WorkspaceId, PrincipalRef, membership, authorization decision, and RequestContext contracts without changing routing.
3. Authenticate reads and mutations. Gate first-user admin provisioning behind an operator bootstrap token/local console; make app-scope configuration brain.admin only. Current first successful unauthenticated login can provision admin and app/default config can be written without a resolved session. **[R]**
4. Issue a signed local session token only after password/OIDC reauthentication or presentation of an existing unforgeable server-issued proof; retaining a clientId-only legacy session is insufficient and must trigger reauthentication. Keep clientId only as ConnectionId. Add refresh/revocation and logout invalidation.
5. Create personal tenant/default workspace for each current local user; write membership records and a migration ledger. Do not copy journals yet.
6. Introduce GrainAddressResolver and LegacyGrainKeyMap. Old global IDs resolve only under personal/default compatibility requests; explicit other workspaces reject.
7. Route new conversations/proposals/feeds/connectors to scoped V2 keys behind flags. Shadow-query V1/V2 state and compare.
8. Backfill projections and credential ownership; cut reads first, then commands, then feed delivery. Keep V1 adapters for mixed clients.
9. Remove shared unaddressed feed publication for private surfaces; reject scope-less new writes.
10. Retire legacy resolution only when usage telemetry is zero for the agreed compatibility window and rollback artifacts remain restorable.

### Capability-isolation activation gate

M2 identity/membership creation does **not** authorize a second user or workspace to use a still-global V1 capability. A central CapabilityIsolationGate records capability ID/version, implementation path, allowed scope mode, evidence/test version, and activation status. Unknown or unproved capability/scope combinations return typed Unavailable and are hidden from discovery; they never fall back to a global grain, unfiltered journal scan, shared feed, app credential, or caller-supplied client ID. **[I]**

A legacy global path may remain enabled only when the server proves exactly one active product principal, one personal tenant/default workspace, and sole ownership of the underlying data. Creating or enabling a second principal/workspace automatically disables every unisolated global capability until its gate passes. Connection/session/device IDs do not satisfy sole-ownership proof.

| Capability/data | Multi-user/workspace activation evidence |
|---|---|
| Memory and Ino context | Scoped conversation owner and projection/query filters; section-8 memory/journal isolation and M6 context canaries pass |
| Journals, timelines, proposals | Scoped owner mapping plus M4/M5 workflow/query authorization, cursor, guessed-key, and lineage tests pass |
| Feeds/surfaces/actions | Addressed/token-free V2 feed/action tests pass. V1 fallback is authenticated-addressed only; current shared HomeFeedBus stream is never a private rollback path |
| Credentials/connectors | CredentialRef ownership/grants and Google/Salesforce status/data isolation tests pass; app/default pack is not a wildcard |
| Tools/models/MCP | Catalog/list/invoke and model policy use server context; forged tool/model call and MCP query/command scope tests pass |

Rollout and rollback are fail-closed per capability: keep the last proved isolated implementation, disable the capability, or allow authenticated sole-owner personal/default compatibility. Never restore shared/global behavior merely to keep a second workspace usable.

### Identity and secret ownership

- Users, services, agents, projection workers, and effect workers receive distinct PrincipalKind values and credentials.
- Approvers are principals with current membership and policy grants; an agent cannot self-assert a human identity.
- ClientId, MCP session ID, gRPC peer, device ID, and trace ID are never identities.
- Credentials are owned by tenant + principal + provider, optionally shared to a workspace through a grant. Domain records contain CredentialRef, provider, grant version, scopes, expiry/status, not secret bytes.
- Audit logs include actor, resource, action, decision, policy version, operation/correlation, and result; sensitive payloads are hashed/classified/redacted.

### Required two-user × two-workspace isolation tests

Create users Alice and Bob, workspaces A1/A2 and B1/B2, plus reused/malicious client IDs. For every row, test direct query, guessed ID/key, stale cursor/action, replayed command, MCP, gRPC, and projection path:

| Asset | Setup and assertion |
|---|---|
| Memory | Store distinctive evidence in each workspace; context, semantic recall, summaries, prompts, and embeddings return only authorized evidence |
| Journals/timeline | Events and causal edges are scoped; direct legacy grain ID cannot widen scope |
| Feeds/surfaces | Private surface for A1 never reaches A2/B1/B2 or unaddressed subscribers; reconnect cursor cannot cross workspace |
| Proposals/decisions | Bob cannot list/approve Alice’s proposal; Alice in A2 cannot approve A1 without grant; approver is server-derived |
| Credentials | CredentialRef and connector status cannot be read/used across principal/workspace; PackConfig old scope adapter cannot be confused |
| Tools | Catalog hides unauthorized capabilities and execution rechecks; forged model tool call is denied |
| Connector data | Gmail/Salesforce raw and structured results remain within credential owner/granted workspace |
| Admin/config | App profile/model/pack changes require brain.admin; first-user/bootstrap paths cannot be invoked remotely after bootstrap |

## 9. Ino and tool-calling migration

The current [InoNeuron](../integrations/DigitalBrain.Ino/InoNeuron.cs) remains the compatibility entry point while responsibilities move behind ports. Do not introduce network services or replace the grain in one change.

| Component | Boundary, input/output, dependencies | Recommended form | Extraction order and characterization gate |
|---|---|---|---|
| Request coordinator | CommandEnvelope → accepted conversation operation; identity, conversation owner, cancellation/deadline | Conversation grain | 1. Wrap existing Handle/Interact; pin response, proposal, surface, and conversation tests |
| Intent/capability planner | Prompt + safe context + authorized descriptors → typed plan | Application service, pure where possible | 4. Extract classifiers/special cases; golden intent/plan tests |
| Context assembler | RequestContext + conversation + memory/query ports → classified evidence packet | Application service | 2. Replace unfiltered journal scan; two-workspace leakage tests first |
| Memory query service | Scoped evidence query → ranked citations/provenance | Query service over projection/index | 3. Adapt ContextNeuron; workspace/filter/trust/redaction tests |
| Model-routing policy | ModelRoutingRequest → decision + IChatClient lease | Application policy + infrastructure adapter | 5. Shadow current role selection; capability/fallback/budget tests |
| Tool catalog | RequestContext + intent → authorized ToolCapabilityDescriptors | Application service | 6. Adapt existing IInoToolProvider; catalog exposure/authorization tests |
| Tool authorization policy | Context + descriptor + args → allow/deny/approval requirement | Domain/application policy | 7. Deny forged and stale grants; policy-version tests |
| Durable invocation coordinator | ToolInvocationRequest → operation status/result | Workflow grain + effect worker | 8. Start with one read capability, then side effects; crash/duplicate tests |
| Response/surface composer | Plan/model/tool structured results → response and SurfaceEnvelope | Application service | 9. Snapshot current chat/auth/proposal surfaces and V1 adapter |
| Conversation projection | Events → paged messages/session summaries/status | Projection worker/query | 10. Dual-read compare before moving Interact result |

### Migration behavior

- Keep ino-main as a compatibility facade. Under a feature flag it resolves RequestContext, forwards to a scoped conversation grain, and adapts the response to V1; it never becomes a wildcard reader.
- Characterize every early return and special intent before extraction. The gate includes current conversation, chat-surface, tabular/schema, graph, automation, tool-capable-model, authentication wrapper, and tool telemetry tests.
- Context packets must query only authorized workspace evidence. Current BuildContextAsync includes unfiltered recent incoming/outgoing/task/automation records, and ContextNeuron recall ignores the stored workspace. **[R]**
- Tool results are structured. NeedsAuth returns CredentialChallengeRef and a safe surface; it is not a generic failure string.
- Caller cancellation stops the synchronous wait; durable tool state continues to terminal/unknown outcome where an effect may have started.
- Read-only idempotent tools may complete inline only after ledger receipt, bounded timeout, and audit. All side-effecting tools use durable effect execution.
- Raw provider responses remain in connector-governed storage if product policy permits. Journals contain schema-safe output or redacted summary/reference.
- Persist conversation command accepted, model decision, tool operation IDs, response event, and surface causation. Agent Framework sessions remain optional; the current session-null behavior is not replaced on V2’s critical path because Agent Framework is still public preview. See [Agent Framework](https://learn.microsoft.com/en-us/agent-framework/overview/). **[D][I]**
- Instrument spans for request coordination, context query, model route/call, tool authorize/queue/apply/verify, response, and surface publish. IDs may be protected span attributes, never metric labels.

## 10. Connector and OAuth migration

### Authoritative design

Use one ConnectorAuthApplicationService and one OAuthFlowGrain per flow. AuthNeuron becomes a UI/legacy adapter; IConnector is narrowed into provider protocol/capability adapters. The callback calls the application service, not a second independently stateful implementation.

Provider adapters register ConnectorCapabilityDescriptors. Discovery returns only authorized capabilities and safe Connected, NeedsAuth, InsufficientGrant, Expired, Revoked, or Unavailable status from the ConnectorStatus projection; it never returns credentials or raw provider data.

1. BeginAuth authorizes the principal and requested provider capability/scopes.
2. Create opaque state as a non-secret key-version prefix plus 256-bit randomness and compute a server-secret HMAC with that key-ring version. Key OAuthFlowGrain directly by the HMAC and persist Started state/key version with originating operation/correlation/CauseRef, principal/scope/provider/redirect/expiry, PKCE SecretRef, and requested grants. Return the redirect only after that write succeeds; raw state is never stored/logged and no secondary index is needed. Retain prior HMAC keys beyond maximum flow TTL.
3. Generate S256 PKCE verifier/challenge. Google enforcement is gated by a real provider compatibility test; Salesforce requires it.
4. Redirect with exact allowlisted URI and least scopes.
5. Callback accepts only a configured key version, computes the same HMAC to locate the flow, verifies provider/redirect/expiry, stores the short-lived code as a SecretRef, and atomically transitions Started → Claimed → ExchangeQueued with EffectId. Duplicate claims return existing status and cannot enqueue another exchange.
6. Validate returned identity, scopes/grants, token type, expiry, and provider account binding.
7. Store/rotate secret material in connector-owned encrypted storage; atomically update CredentialRef metadata. Only then transition to Succeeded/Completed and emit ConnectorAuthorized.
8. Serialize refresh per CredentialRef. Preserve an old Google refresh token if exchange omits a replacement. For Salesforce rotation, durably store the replacement before releasing the refresh lease.
9. Revoke at provider, mark credential revoked, delete/retire secret version, invalidate grants, and emit a secret-free event.

Authorization-code exchange and refresh are external effects, not ordinary callback-local I/O. OAuthFlowRecord represents Started, Claimed, ExchangeQueued, Exchanging, Succeeded, RetryScheduled (only before a provider commit point), Failed, OutcomeUnknown, ReauthorizationRequired, Expired, and Revoked with revision, lease, due time, effect, and result credential reference. If the process loses an exchange response, record OutcomeUnknown; verify when the provider offers a safe lookup, otherwise require a new authorization flow rather than blindly replaying a possibly consumed code. Never delete or overwrite the prior usable credential until the replacement secret version and grant metadata are durably committed. Crash tests cover claim-before-exchange, response-before-secret-write, secret-write-before-flow-complete, and refresh-token replacement before lease release. **[I]**

Google recommends non-guessable state, exact redirect matching, offline access for refresh tokens, least privilege, and secure token storage. See [Google OAuth best practices](https://developers.google.com/identity/protocols/oauth2/resources/best-practices), [web-server OAuth](https://developers.google.com/identity/protocols/oauth2/web-server), and [Gmail scopes](https://developers.google.com/workspace/gmail/api/auth/scopes). Salesforce recommends web-server flow with S256 PKCE and documents rotation/revocation behavior. See [Salesforce web-server OAuth](https://help.salesforce.com/s/articleView?id=xcloud.remoteaccess_oauth_web_server_flow_ca.htm&language=en_US&type=5) and [PKCE](https://help.salesforce.com/s/articleView?id=sf.remoteaccess_pkce.htm&language=en_US&type=5). **[D]**

### Provider consolidation

| Provider | Current concrete gap **[R]** | Migration outcome and tests |
|---|---|---|
| Google | AuthNeuron and GoogleConnector duplicate flow; state is user:GUID without expiry; no PKCE; default gmail.readonly while SendMessageAsync exists | One adapter; opaque state; PKCE compatibility test; separate gmail.read and gmail.send descriptors. Read uses gmail.readonly; send separately requests gmail.send and approval. Test exact redirect, offline refresh, missing replacement token retention, scope insufficiency, replay/expiry/revoke |
| Salesforce | AuthNeuron copies verifier but endpoint-used SalesforceConnector drops it; state derives user; password flow still exists; refresh rotation not modeled | Fix via single coordinator, not a second patch in both paths; S256 verifier must appear in real token request. Disable username/password flow by default and migrate only with explicit legacy profile. Test endpoint callback, two users/cross-silo, rotation concurrency, unknown refresh outcome, revoke |

Salesforce rotation replaces the refresh token and revocation also affects associated access tokens; serialized replacement and revoke tests are therefore release requirements. See [refresh-token rotation](https://help.salesforce.com/s/articleView?id=xcloud.shr_api_enable_oauth_settings_enable_refresh_token_rotation.htm&language=en_US&type=5) and [token revocation](https://help.salesforce.com/s/articleView?id=sf.remoteaccess_revoke_token.htm&language=en_US&type=5). **[D]**

### Existing credential compatibility

- Inventory current app/default and user:* packs without logging values. Record provider, owner inference, keys present, scope/grant, encryption key version, and validation status.
- Because DataProtection purpose includes scope/pack/key, moving ciphertext to tenant/workspace scope without decrypting and re-protecting would make it unreadable. Migrate inside the trusted connector store: decrypt old value, write new secret version, verify, write CredentialRef, then retain old blob until cutover. **[R][I]**
- Read-new then read-old during expansion. Never dual-refresh the same credential.
- Backfill metadata without testing a live provider unless the operator approves the controlled validation. Invalid/insufficient credentials move to ReauthorizationRequired, not deletion.
- Compatibility ends only after all active credentials have references, callback/refresh/revoke tests pass, restore is proven, and old-scope reads remain zero.

## 11. MCP migration

### Tool classes and scopes

| Class | Scope | Semantics | Examples |
|---|---|---|---|
| Read/query | brain.read | Side-effect-free projection query; opaque cursor; purpose-based redaction | timeline, causal lineage, Ino status, workflow/proposal status, workbench/feed query |
| Idempotent command | brain.act | Submits CommandEnvelope and returns Accepted/OperationId; retry uses idempotency key | ask Ino, stage automation, request visualization, update allowed user preference |
| Approval | brain.approve | Separate authenticated decision command with proposal hash/policy | approve/reject proposal, approve connector write |
| Administration | brain.admin | Operator-only capability/profile/model/config action; never enabled by ordinary user token | capability profile, app-scope config, projection rebuild, maintenance |

Query-like methods currently located in [DigitalBrainMutationTools](../src/DigitalBrain.Mcp/DigitalBrainMutationTools.cs) move to query tools. Generic fire_synapse, raw fire_ui_action, arbitrary target resolution, and free-form decided_by have no Production V2 equivalent. **[R][I]**

### Authentication and request processing

- HTTP MCP conforms to the [MCP 2025-11-25 authorization model](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization): Protected Resource Metadata, authorization-server discovery, resource indicators, audience validation, least scopes, and proper WWW-Authenticate challenges. The token is never passed to Google or Salesforce. **[D]**
- Validate Origin for Streamable HTTP, bind local-only servers to loopback, and use stdio only in the trusted Development profile. See [MCP transports](https://modelcontextprotocol.io/specification/2025-11-25/basic/transports). **[D]**
- Map ASP.NET ClaimsPrincipal to RequestContext; resolve workspace membership server-side. An MCP session is not an identity.
- Capability listing is filtered, and invocation is authorized again. Tool annotations such as readOnly/idempotent are untrusted hints, not policy.
- Every command accepts clientRequestId/idempotencyKey and returns operationId plus a status query link/tool. MCP experimental Tasks may adapt this ledger later but never become the workflow source of truth.

The official C# SDK propagates the ASP.NET Core ClaimsPrincipal into handlers and supports authorization filters; use that integration rather than inventing identity from tool arguments. See [MCP C# SDK identity and role propagation](https://csharp.sdk.modelcontextprotocol.io/concepts/identity/identity.html). **[D]**

### Structured contract

Every successful tool result uses StructuredContent with:

- schemaVersion, requestId, operationId where applicable, data, nextCursor where applicable;
- classification and redactionApplied metadata;
- warnings as stable codes, not provider exception text.

Expected execution failures return isError with a DigitalBrain error object:

- code, category, retryable, safeMessage, operationId, authChallengeRef, retryAfter, detailsRef;
- no stack trace, raw connector payload, secret, journal ToString, or provider token response.

Protocol misuse (unknown tool, invalid JSON-RPC, unsupported version) remains a JSON-RPC error. List operations use opaque server-generated cursors; clients must not parse them. See [MCP tool results](https://modelcontextprotocol.io/specification/2025-11-25/server/tools) and [pagination](https://modelcontextprotocol.io/specification/2025-11-25/server/utilities/pagination). **[D]**

### Limits and audit

Initial conservative, configurable limits **[I]**:

- tool arguments: 256 KiB; structured result: 1 MiB; raw data is paged or referenced;
- default page: 100, maximum: 500;
- per principal: 120 read calls/minute, 30 command calls/minute, 10 approval calls/minute;
- concurrency: 16 reads, 4 commands, 1 approval mutation per principal/workspace;
- command deadline: adapter-specific and always bounded; cursor/action/auth state is expiring;
- reject, do not truncate, an input that exceeds a schema/size limit; return a stable limit code.

Audit every capability listing decision, invocation, command acceptance, approval, admin action, rate rejection, redaction, and result category with principal/resource/policy/operation. IDs belong in protected logs/spans, not metric labels.

### Routing rule and exposure decision

MCP calls IBrainQueryService, ICommandBus, IApprovalService, and IAdministrationService. Those ports create or query V2 contracts. MCP must not inject IGrainFactory and must not manufacture Synapses.

Production HTTP mutation remains disabled until:

1. OAuth/audience/Origin validation passes contract tests.
2. Query/command/approval/admin scopes are enforced for list and invoke.
3. Tenant/workspace is server-derived and isolation tests pass.
4. Commands are idempotent, audited, and return operation status.
5. Approval records authenticated principal and proposal hash.
6. Rate/size/redaction tests and telemetry are operational.

Enable read tools first, idempotent commands second, approval only after durable workflows, and administration only on a separate operator policy. Development stdio may retain a V1 compatibility tool set with an explicit unsafe-local banner.

## 12. UI protocol and feed migration

### Surface and action protocol

SurfaceEnvelope in section 5 is the wire contract around typed widget-tree or RFW payloads. The durable feed stores the token-free StoredSurfaceRecord with protocol/surface schema versions, tenant/workspace/audience, revision, expiry, feed sequence, required client capabilities, stable content hash, action bindings, and cause. RFW remains a renderer inside this protocol; it is not the network/feed/security contract.

RFW documentation says remote libraries/data should be cached locally, its compatibility is best effort, and binary/screenshot goldens are appropriate. See [RFW API documentation](https://pub.dev/documentation/rfw/latest/index.html). **[D]**

Action processing:

1. Composer registers a token-free UiActionBindingRef with stable CommandTemplateId/version, template hash, InputSchemaRef, IdempotencyNamespace, MaxUses, and expiry; StableContentHash covers that stable payload/binding data and excludes bearer tokens.
2. Initial delivery or catch-up materializes a wire SurfaceEnvelope and creates a fresh cryptographically random, short-lived ActionToken for each authorized binding.
3. Action owner stores issued-token hashes, append-only UiActionUseRecords, and a rebuildable binding-wide UiActionBindingUsage index plus principal/audience, scope, surface/revision, issue/expiry, and policy version. StoredSurfaceRecord never contains bearer values.
4. Flutter submits the wire token plus schema-valid user inputs; it cannot select a Synapse type or target grain.
5. In one action-owner AggregateCommit, server validates/consumes token, claims the next binding-wide use ordinal across **all** tokens, rechecks current policy/revision/schema, preassigns OperationId and IdempotencyKey from binding namespace + ordinal + canonical input hash, appends a CommandQueued UiActionUseRecord, and adds a command-submission OutboxRecord. It may then return Accepted/OperationId. The outbox submits the resolved CommandEnvelope to the target; the command handler reauthorizes current membership and deduplicates by the preassigned key/operation. Accepted/failed receipts append use transitions and update the derived index.
6. Crash before the action-owner commit consumes nothing. Crash after it leaves a discoverable queued effect. Crash after downstream acceptance but before receipt is safe because retry returns the same preassigned OperationId. Same-input retries return status; different input conflicts. Reconnect rotates tokens, but multiple valid tokens cannot exceed binding MaxUses. Replay, expiry, wrong scope/revision, or tamper is denied/audited.

### Durable/resumable feed

- A SurfaceFeed projection assigns a monotonic sequence per tenant/workspace/audience stream and stores token-free StoredSurfaceRecord/content reference. It materializes wire tokens only after authenticating the delivery audience.
- WatchHomeFeedV2 accepts authenticated audience plus afterSequence and supported protocol/widget capabilities.
- Server sends retained entries after the cursor with newly minted action tokens, then live tail. Client deduplicates by feed sequence and SurfaceId/revision and periodically acknowledges.
- Gaps trigger a bounded catch-up query; retention expiry returns ResetRequired plus a full current-surface snapshot.
- Backpressure never silently DropOldest. The server closes with resumable last sequence or spills through durable projection; lag is measured.
- Private surfaces never enter an unaddressed stream. A public/shared audience is an explicit authorized AudienceKind, not null clientId.

Current [HomeFeedBus](../src/DigitalBrain.Kernel/Ui/HomeFeedBus.cs) may serve only its authenticated addressed branch for a gate-approved sole owner during expansion. Its shared private subscription is disabled before a second principal/workspace is enabled; its in-process dedup never claims V2 durability. **[R][I]**

### Capability negotiation and compatibility

- Flutter advertises supported envelope versions, payload kinds, widget vocabulary version, maximum payload, binary RFW support, and native feature flags.
- Server chooses the highest mutually supported version; unsupported required capability returns an upgrade surface or native fallback.
- V1 adapter wraps UiSurface/RfwCard only for an authenticated addressed connection and a CapabilityIsolationGate-approved sole-owner personal/default scope; it converts allowlisted actions server-side and never trusts client workspace/clientId.
- During dual delivery, compare surface ID/revision/content hash and action outcomes. Rollback may select the addressed V1 adapter only under that gate; otherwise the private feed is unavailable. The current shared HomeFeedBus stream is never a rollback path, and V2 rows remain.
- Retire V1 only after supported client telemetry shows zero V1 negotiation, all golden/action/isolation/resume tests pass, and an older client remains in the release smoke matrix.

### Flutter decomposition without rewrite

Keep native shell/navigation/session, canvas/editor, and immediate local interaction state. Split [digitalbrain_rfw_library.dart](../app/lib/rfw_host/digitalbrain_rfw_library.dart) by registry primitives, layout/navigation, input/form, data display/charts, overlays/feedback, domain widgets, event bridge, and diagnostics. Split [forui_app_shell.dart](../app/lib/shell/forui_app_shell.dart) into session/feed controller, navigation state, chat/upload controller, surface store, and presentation widgets. Each extraction preserves public constructors/registry names and adds golden/widget tests before moving the next group.

## 13. Model registry and routing migration

### Ownership

- DigitalBrain.Domain owns model/provider/capability descriptors and data-classification concepts.
- DigitalBrain.Application owns IModelRouter, policy precedence, budgets, fallback rules, and decision audit.
- Infrastructure.AI owns provider client construction, credentials, health/circuit observations, and Microsoft.Extensions.AI adapters.
- AppHost supplies an immutable capability profile/registry snapshot; dynamic policy is tenant/workspace configuration with versioning and authorization.

Microsoft.Extensions.AI provides provider-neutral IChatClient/embedding abstractions and composable telemetry/function middleware. Keep those adapters, but do not let automatic function invocation bypass the DigitalBrain tool coordinator. See [Microsoft.Extensions.AI](https://learn.microsoft.com/en-us/dotnet/ai/microsoft-extensions-ai) and [IChatClient](https://learn.microsoft.com/en-us/dotnet/ai/ichatclient). **[D][I]**

### Routing evaluation order

1. Validate RequestContext, requested role, modality, and required capabilities: tools, vision, streaming, structured output, embeddings, or voice.
2. Apply tenant/workspace allow/deny policy and provider/model pinning.
3. Apply data classification, residency, privacy, retention, and provider-training restrictions.
4. Apply token and monetary budget; reserve budget with OperationId.
5. Exclude unavailable/unhealthy/quota-exhausted/circuit-open registrations.
6. Rank remaining candidates by role fit, capability, latency objective, cost, health, and explicit policy preference.
7. Produce ModelPolicyDecision before the call with policy version, exact RegistrySnapshotId, chosen registration, ordered authorized fallbacks, constraints, and reason code.
8. Execute with bounded timeout; account tokens/cost/latency and classify result.
9. Fallback only to the pre-authorized ordered list and within remaining budget. Never cross residency/privacy/provider restrictions.
10. Persist decision metadata and redacted outcome; prompt/response retention follows data policy.

### Capability and provider model

Registrations declare model role(s), provider, immutable model/deployment ID, supported modalities, tools/parallel tools, structured output dialect, streaming, context/output limits, region/residency, privacy/retention class, price metadata/version, and health endpoint/strategy. Embedding and voice use the same routing request with their own capability kind.

Provider health is a bounded state machine: Unknown, Healthy, Degraded, RateLimited, Unavailable, Misconfigured. It is observed, never manually inferred from a single exception. Dynamic registry/policy updates are validated, versioned, authorized, and activated atomically; in-flight operations retain their decision version.

### Consolidation targets

Replace duplicated switches in [DigitalBrainChat.cs](../src/DigitalBrain.Kernel/Llm/DigitalBrainChat.cs), [DigitalBrainChatClientRegistration.cs](../src/DigitalBrain.Kernel/Llm/DigitalBrainChatClientRegistration.cs), and [ScopedChatClientFactory.cs](../src/DigitalBrain.Kernel/Llm/ScopedChatClientFactory.cs) with one IModelClientFactory keyed by provider adapter. Make Ino and [LlmResponderNeuron](../src/DigitalBrain.Kernel/Grains/LlmResponderNeuron.cs) call IModelRouter. Split the overloaded llm_key into ModelRegistrationId and CredentialRef. Keep old configuration readers during expansion; emit a conflict warning when old and new policies disagree; write only V2 after cutover.

Implement the factory contract first, then independent local, OpenAI/Azure, Anthropic, and xAI adapters, then delegate keyed, scoped, and default/modality facades separately, and only then enable policy routing. This keeps each provider/caller cutover independently reversible and matches V2-MODEL-001 through V2-MODEL-FACADE-DEFAULT-001 in section 16.

Required tests cover policy precedence, capability exclusion, health/circuit behavior, no illegal residency/privacy fallback, budget exhaustion, tool/structured-output requirements, embedding/voice selection, dynamic config versioning, deterministic audit reason, provider construction parity, and old-key compatibility.

## 14. Aspire, deployment, and observability

### Refreshed live evidence

Read-only Aspire MCP observed one in-workspace AppHost during 2026-07-10 00:08–00:14 Europe/Prague. It reported 28 resources: 25 Running/Healthy, two expected rebuilder helpers NotStarted, and one logical Azure environment without runtime state. Three separate Kernel processes were healthy behind shared endpoints; MCP was a separate project resource. Kernel WaitFor edges included clustering, grain state, journal, sync, LLM, and embedding. **[L]**

Azurite had persistent container lifetime but no reported mounted volume; Ollama/OpenWebUI had named volumes and Whisper a named model-cache volume. Thirteen sampled traces contained 71 non-error spans for grain timeline/fire/automation operations, with correlation, neuron, Synapse type, and Orleans RPC attributes. MCP/Flutter had no structured telemetry in the Aspire query. No replica was killed and no recovery was exercised. **[L]**

Sampled structured Kernel logs were Information-level HTTP resilience records with successful responses and no observed retry/error; console startup included expected Azurite ContainerAlreadyExists conflicts while resources finished healthy. This is a narrow sample, not proof of error-free operation. Aspire MCP exposed relationships/endpoints but not an independent service-discovery resolution or backup-status query. **[L]**

### Capability profiles

| Capability | Development | Test | Production |
|---|---|---|---|
| Kernel | Three local project replicas by default; explicit one-replica quick profile allowed | Three silos, container-backed storage, deterministic fake/model option | ACA or selected target with declared min/max and drain behavior |
| Storage | Azurite with named data volume if local durability is promised | Disposable volume plus backup/restore fixtures | Managed redundant storage, versioning/retention/backup/restore and RPO/RTO |
| Models | Ollama chat/embedding and Whisper cache; optional cloud | Deterministic fakes plus optional real local container lane | Explicit approved providers/models; missing embedding/voice surfaced as unavailable |
| MCP | Trusted stdio and authenticated loopback HTTP; unsafe V1 tools opt-in | Auth/scope contract server | Authenticated Streamable HTTP; mutations staged; admin off by default |
| UI | Flutter Windows/web dev plus V1/V2 negotiation | Flutter widget/goldens and web integration | Supported web/client releases with compatibility matrix |
| Connectors | Sandbox/test accounts only; secrets via user-secrets/parameters | Fake token endpoints and provider sandboxes | Managed secret refs, exact redirects, rotation/revocation |
| Telemetry | Aspire Dashboard/OTLP, verbose protected traces | In-memory/collector assertions | Supported collector/ACA agent, App Insights/metrics backend, redaction/sampling |

The profile manifest is validated at startup and exposed as a safe capability status. An absent capability is a typed Unavailable result, not a hidden fallback.

### Topology and deployment convergence

1. Normalize the AppHost resource graph and Pulumi graph into a secret-free topology snapshot: resource kind, logical name, references, endpoints/ingress, profile, replicas, storage role, identity, health, telemetry, and capability.
2. Make CI fail on unexplained drift. Keep an allowlist only for target-specific resources with rationale/owner. A concrete current drift test must fail until [Pulumi deploy](../deploy/Program.cs) injects the shared internal service key into both Telegram and Kernel: today the Telegram container receives <code>DigitalBrain__InternalServiceKey</code>, while the Kernel environment list omits it, so Production secret-returning transport calls fail closed even though [AppHost](../hosts/DigitalBrain.AppHost/AppHost.cs) wires both sides. **[R]**
3. Keep Pulumi as provisioning engine until Aspire-generated/deployed artifacts prove full parity. Always run build, topology test, Pulumi preview, policy checks, and artifact/image digest validation before apply.
4. Replace hard-coded internal URLs with Aspire service discovery references. WithReference injects logical endpoints; see [Aspire service discovery](https://aspire.dev/fundamentals/service-discovery/). **[D]**
5. Sequence only required dependencies with WaitFor/readiness. Optional models/connectors report degraded capability rather than holding Kernel startup.
6. Test replica drain, placement, activation recovery, storage conflicts, and termination grace. WithReplicas proves multiplicity, not recovery.
7. Deploy immutable image digests. Shift traffic only after readiness and production smoke; retain prior revision and compatible schema readers for rollback.

### Durability, identity, and caches

- A persistent container lifetime is not persistent data. Add an Azurite data volume if continuous local journals are promised; otherwise label the profile disposable. See [Aspire persistent data volumes](https://aspire.dev/fundamentals/persist-data-volumes/). **[D]**
- Ollama model blobs and Whisper caches are replaceable caches. Losing them may increase cold-start time but must not lose domain state. Pin image/model versions and expose warm/readiness separately.
- Production grain state, journals, CommitOwnerDirectory/cursors, sync, credential secrets, projection data, and DataProtection keys require documented redundancy, backup, retention, restore drills, RPO/RTO, and failure-domain ownership.
- Complete managed-identity shadow validation for storage/model access, then remove account/model keys and shared-key/public access in a later reversible deployment. Managed identity avoids credentials in the container; see [ACA managed identities](https://learn.microsoft.com/en-us/azure/container-apps/managed-identity). **[D]**

### Health and telemetry

- /alive checks only process/runtime responsiveness.
- /health checks Orleans membership/client readiness, authoritative state/journal storage, DataProtection/credential-store availability, and required projection/outbox initialization. It does not call every external provider.
- Optional provider/model/connector health feeds capability status. Readiness may reject new mutations if authoritative storage or idempotency cannot be committed.
- Add ServiceDefaults-equivalent behavior to MCP and every network host. Register DigitalBrain.Ino, workflow, outbox, projection, tool, model, connector, UI feed, and OAuth sources/meters.
- Replace the TLS-bypassing, fail-open OTLP proxy with a supported authenticated collector path or a sanitized application diagnostics endpoint. Count enqueue, export, retry, overflow, and drop outcomes. ACA’s managed OTel agent buffers/retries for a bounded interval and can drop oldest data, so drops still need visibility. See [ACA OTel agents](https://learn.microsoft.com/en-us/azure/container-apps/opentelemetry-agents). **[D]**

| Signal | Required fields/dimensions |
|---|---|
| Protected spans | trace/span, service/version/environment/silo, operation family, tenant/workspace/command/workflow/invocation IDs only where access-controlled, correlation/causation links, policy/schema versions, outcome |
| Logs | Structured stable event/error codes, actor/resource decision, redaction/classification, operation correlation; no secrets/raw provider data/action tokens |
| Low-cardinality metrics | component, operation family, provider class, capability class, workflow stage, outcome, retry category, environment; never tenant/workspace/command/workflow/proposal/operation/invocation IDs |
| Dashboards | request durability, cluster/replica, journal/storage, outbox, workflow, projection, tool/model, OAuth/connectors, feed, telemetry pipeline, deployment version |

Required instruments:

- command accepted/rejected and durable-commit latency/failures/conflicts;
- journal append latency/bytes/quarantine and historical-read failures;
- commit-owner enrollment failures, directory scan age/coverage and cursor recovery; outbox depth/oldest age/attempts; inbox duplicates; poison count;
- workflow count/age by bounded state/risk class; retries, unknown outcomes, compensation/manual-intervention;
- projection lag/checkpoint age/rebuild/quarantine;
- tool/model calls, latency, tokens/cost bucket, failure/timeout/rate category, fallback;
- OAuth start/callback/replay/expiry/grant/refresh/revoke outcomes;
- feed publish/catch-up/lag/reset/dedup and unsupported protocol;
- telemetry queue/export/retry/drop/cardinality-overflow.

OpenTelemetry warns that high-cardinality attributes such as user IDs can cause unbounded metric combinations and overflow; Views are defense in depth, not permission to record sensitive labels. See [OpenTelemetry metrics](https://opentelemetry.io/docs/concepts/signals/metrics/) and [sensitive-data handling](https://opentelemetry.io/docs/security/handling-sensitive-data/). **[D]**

Provisional release objectives **[I; operator must ratify]**:

- authenticated ingress and authoritative durable commit availability ≥ 99.9%;
- p99 accepted-command durable commit ≤ 1 second excluding requested external/model work;
- p99 projection lag ≤ 60 seconds in normal operation and no unacknowledged quarantine;
- p99 private-feed delivery to a connected client ≤ 10 seconds;
- zero untriaged high-risk OutcomeUnknown or cross-workspace authorization events;
- alert when oldest outbox/workflow/projection checkpoint exceeds its policy deadline, not a universal hard-coded duration;
- telemetry drop/overflow above zero alerts in Test and above the approved budget in Production.

### Release and rollback verification

Each release produces topology diff, database/journal compatibility report, image digests, migration plan, backup/restore evidence, canary capability profile, and smoke script. Verify authenticated login, scoped Ino read, proposal/approval, connector status without secret exposure, MCP read, V1/V2 surface, outbox/projection health, and trace correlation. Rollback keeps V2 readers/upcasters and prior projections; irreversible external effects use forward fix/compensation, never a claim that deployment rollback reverses them.

## 15. Testing strategy

### Pyramid and ownership

| Layer | Tests and location | Required assertions |
|---|---|---|
| Pure domain | Domain/Application test projects | Envelope/CauseRef validation, identity/key canonicalization, authorization policy, workflow legal/illegal transitions including expiry/cancellation/exhaustion, retry classification, idempotency conflicts, model/tool policy |
| Serializer/compatibility | Core/Kernel compatibility suite with immutable fixtures | Every historical Synapse/CLR discriminator, old member defaults, alias/upcaster chain, unknown quarantine, read-old/write-new, mixed-version round trip |
| Grain | TestKit in-process cluster | Sequenced pre-commit owner enrollment, contiguous immutable commits/events/effects/transitions, pending-index rebuild, co-located inbox, atomic approval→queue, recovery scan/reminder, lease takeover, scoped ownership, no replayed effect |
| Adapter/provider | Connector/AI/MCP/UI unit/contract projects | OAuth request/callback/refresh/revoke, provider error mapping, structured MCP, SurfaceEnvelope/action token, model construction |
| Projection | Projection tests with real selected store | directory enumeration/new-owner race, authoritative commit pull, duplicate/out-of-order inputs, directory/per-owner checkpoint atomicity, poison behavior, full rebuild/hash, version alias swap, cursor scope |
| Distributed integration | Three-silo Test profile | service discovery, grain placement, replica loss/restart/drain, activation recovery, reminder/outbox resume, duplicate delivery, storage outage |
| Container-backed Aspire | AppHost testing lane | Azurite volume/recreation, Ollama/Whisper cache semantics, readiness sequencing, MCP/Kernel/Flutter endpoints, OTel export |
| Deployment topology | AppHost/Pulumi snapshot and preview | profile parity, identity/secrets, ingress, replicas, storage/backup, telemetry, no latest tag, expected target-only deltas |
| Client/protocol | Flutter test + server golden suite | V1/V2 rendering, binary RFW/goldens, capability negotiation, action forgery/replay, reconnect/resume/dedup/reset |
| Production smoke | Post-canary read/minimal reversible mutation | auth, workspace isolation canary, operation status, projections/feed, health/traces, rollback readiness |

### Required failure-path suites

- **Domain/state machine:** every legal and illegal transition including human reject vs system expiry, atomic approval→queue and imported Approved recovery, pre-effect cancellation vs post-dispatch unknown, retry exhaustion, compensation, manual resolution, requester/risk/approver class, concurrent decision, stale hash.
- **Journal compatibility:** fixture blobs from current commit and every released schema; missing integration assembly; renamed type mapping; corrupt/unknown record quarantine; alpha Orleans journaling upgrade rehearsal.
- **Crash windows:** before enrollment, after enrollment/before commit, approval commit, after commit/before hint, registration high-water/cursor, effect transition/index write, lease/call/provider response/result, OAuth claim/exchange/secret replacement, compensation, and projection checkpoint.
- **Outbox/inbox:** full-cycle revisit of old owner with new commit, registration above high-water, cycle rollover/restart, epoch/gap, enrollment outage, restore, multi/zero-event commits, immutable commit/index rebuild, rediscovery, duplicate/reorder/lease/poison/outage, inbox retention/hash conflict.
- **Projection replay:** old owner gains commit after prior cycle, registration above high-water, cycle reset, zero-event/no-notification commit, mixed V1/V2, sequence gaps/duplicates, rebuild/live tail, directory/per-owner checkpoint crash, parity/alias rollback.
- **Authorization/isolation:** section 8 matrix across HTTP, gRPC, MCP, grain resolver, projections, feed, connectors, tools, admin config.
- **OAuth callback:** HMAC(state)-keyed flow through real endpoint/fake token server; create-before-redirect, legal states/revision/lease, expiry/replay, PKCE/code SecretRefs, exact redirect/grants, exchange unknown without blind retry, Google retained refresh token, Salesforce replacement concurrency/revoke, legacy credential.
- **MCP:** discovery and invocation scopes, audience/Origin, pagination/cursor tamper, idempotency, structured errors, rate/size, audit/redaction, Production mutation-off, no IGrainFactory in tool constructors.
- **UI:** token-free surface/template binding, wire goldens, V1 adapter, two-token MaxUses race, append-only use/index rebuild, crash before queue/after queue/after target accept, stable OperationId/input conflict, wrong scope/revision/replay, feed gap/backpressure/dedup, old client.
- **Model routing:** precedence, health/fallback, privacy/residency, budgets, tool/structured output, embeddings/voice, dynamic policy and audit.
- **Three-silo:** kill the silo owning workflow/grain during each durable window; restart another; prove one logical state transition and classified external outcome.
- **Telemetry:** trace continuity and links, mandatory redaction, no forbidden metric labels, bounded attributes, exporter failure/drop counter, readiness/liveness semantics.

### Current characterization assets

Reuse and extend:

- [NeuronTests](../tests/DigitalBrain.Tests/Kernel/NeuronTests.cs), [JournalFormatSpikeTests](../tests/DigitalBrain.Tests/Spikes/JournalFormatSpikeTests.cs), broadcast/timeline/checkpoint tests;
- [SelfEvolutionNeuronTests](../tests/DigitalBrain.Tests/Kernel/SelfEvolutionNeuronTests.cs) and [SelfEvolutionDurabilityTests](../tests/DigitalBrain.Tests/Kernel/SelfEvolutionDurabilityTests.cs);
- [InoNeuronConversationMemoryTests](../tests/DigitalBrain.Tests/Ino/InoNeuronConversationMemoryTests.cs), tool-call/model/chat-surface/schema tests;
- user session, Gateway scope/routing, PackConfig encryption/backing-store tests;
- GoogleAuthNeuron, SalesforceAuthNeuron/client/cross-silo/two-user, and IConnector contract tests;
- [DigitalBrainToolsTests](../tests/DigitalBrain.Tests/Mcp/DigitalBrainToolsTests.cs) and [McpTransportSplitTests](../tests/DigitalBrain.Tests/Mcp/McpTransportSplitTests.cs);
- [HomeFeedBusTests](../tests/DigitalBrain.Tests/Ui/HomeFeedBusTests.cs), cross-silo/bridge tests, and Flutter UI-kit/shell tests;
- AppHost execution-mode/model/capability, storage/managed-identity, and health tests.

CI must run dotnet tests, Flutter tests/goldens, compatibility fixtures, topology diff/preview, and the appropriate container/distributed lane. The fast PR lane excludes real provider credentials; scheduled/pre-release lanes use controlled sandbox accounts and never print tokens or provider data.

## 16. Prioritized implementation backlog

Each item is sized for one focused pull request unless its acceptance criteria explicitly call for a follow-up cutover PR. ADRs are decision gates, not excuses to bundle implementation.

### P0 — architectural necessities and immediate safety

#### V2-SAFETY-001 — Sensitive-data taxonomy and validation contract

| Field | Required content |
|---|---|
| ID | V2-SAFETY-001 |
| Priority | P0 |
| Classification | Architectural necessity |
| Outcome | A shared sensitive-data taxonomy, classifier, safe-summary API, durable-schema validator, and synthetic canary corpus define what may cross or persist at every boundary |
| Evidence/rationale | LoginRequest passwords, connector form props, raw tool results, and unredacted timeline payloads currently cross unrelated contracts **[R]** |
| Scope | Core/Application policy contracts; data classifications; schema validator; safe-summary API; synthetic canary fixtures |
| Dependencies | ADR-003 and ADR-013 policy shape; no routing dependency |
| Technical approach | Define allowlist-based ISensitiveDataPolicy, classification annotations, SecretRef conversion rules, safe-summary primitives, and validator errors before adapting any ingress/egress |
| Data migration | None; this card does not scan or rewrite historical records |
| Compatibility | Additive API; V1 behavior is unchanged until boundary adapters adopt it |
| Tests | Classification table, prohibited/allowed durable schemas, nested/unknown JSON, safe-summary determinism, false-positive/false-negative canary corpus |
| Observability | Validator decision counts by bounded classification/rule only; no inspected values |
| Rollout | Library and tests first; consumers adopt in separate cards |
| Rollback | Revert additive registration or disable one false-positive rule; published classification IDs are not reused |
| Blast radius | Shared contract library and future adapters only |
| Risk | Medium |
| Acceptance criteria | Every V2 durable contract field has a classification rule; prohibited secret-bearing shapes fail validation; safe summaries never contain canaries |

#### V2-SAFETY-INGRESS-001 — Secret-safe login, configuration, and OAuth ingress

| Field | Required content |
|---|---|
| ID | V2-SAFETY-INGRESS-001 |
| Priority | P0 |
| Classification | Architectural necessity |
| Outcome | Login, configuration, and OAuth form secrets become transient parameters or SecretRefs before any Synapse/journal write |
| Evidence/rationale | Passwords and connector credentials can enter current FireAsync payloads and journals **[R]** |
| Scope | Gateway send handlers; UserSession/Auth adapters; configuration handlers; Google/Salesforce form/callback adapters |
| Dependencies | V2-SAFETY-001; ADR-013 |
| Technical approach | Split secret inputs from durable intent, store via credential owner, pass only short-lived handle/SecretRef, and reject prohibited durable payloads |
| Data migration | No journal rewrite; new writes only. Existing credentials remain behind the compatibility adapter |
| Compatibility | V1 forms and response shapes remain; adapters execute the same operation without persisting secret bytes |
| Tests | Login/config/OAuth canaries absent from journal and checkpoints; invalid/expired SecretRef; existing happy paths and callback correlation |
| Observability | Ingress rejection/conversion count by bounded adapter/rule; protected audit without value |
| Rollout | One ingress family at a time in audit, Test enforcement, then Production/Development |
| Rollback | Disable a broken adapter and require re-entry; never restore secret journaling |
| Blast radius | Login, connector setup, and configuration submission |
| Risk | Critical |
| Acceptance criteria | Synthetic secret bytes never appear in durable writes for all covered ingress paths; existing user flows pass |

#### V2-SAFETY-EGRESS-001 — Redacted query, UI, telemetry, and export egress

| Field | Required content |
|---|---|
| ID | V2-SAFETY-EGRESS-001 |
| Priority | P0 |
| Classification | Architectural necessity |
| Outcome | Timeline, MCP, UI, logs/spans, tool audit, checkpoint, and export paths share one redaction/safe-summary policy; historical scanning reports hash/location only |
| Evidence/rationale | get_timeline returns raw payloads, Ino records result.ToString, and current output paths apply inconsistent redaction **[R]** |
| Scope | Timeline/query formatting; MCP response mappers; UI composition; Ino/tool audit; logging/OTel enrichers; checkpoint/export readers; scanner |
| Dependencies | V2-SAFETY-001 |
| Technical approach | Apply policy at source and final serializer, prohibit raw connector/model payloads in audit, and add read-only suspected-record scan with no content output |
| Data migration | Do not rewrite historical journals; protect all reads and emit only record hash/location/classification for operator review |
| Compatibility | Preserve response schemas with explicit redacted/omitted markers and typed authorization errors |
| Tests | Canary through every covered egress; nested payload and exception paths; role-aware redaction; scanner does not reveal content |
| Observability | Redaction/omission count by bounded egress/rule and scanner count; no secret/high-cardinality labels |
| Rollout | Shadow compare in Development, enforce Test/Production, then Development; per-egress flag |
| Rollback | Narrow a false-positive rule or egress flag; never restore public raw output |
| Blast radius | Queries, support diagnostics, UI, MCP, and telemetry |
| Risk | High |
| Acceptance criteria | No canary appears in MCP/UI/export/checkpoint/log/trace/metric output; authorized non-sensitive fields remain usable |

#### V2-TEST-CHAR-001 — Current-behavior characterization and fixture manifest

| Field | Required content |
|---|---|
| ID | V2-TEST-CHAR-001 |
| Priority | P0 |
| Classification | Architectural necessity |
| Outcome | Tests-only deterministic fixtures characterize current auth, secret, journal, Ino, OAuth, MCP, UI, topology, and failure-window behavior before migration |
| Evidence/rationale | Existing tests cover many happy paths but omit key crash, isolation, callback, client, and topology failure paths **[R]** |
| Scope | Synthetic fixtures/manifests; current adapters; TestKit failpoint interfaces with no runtime activation; test documentation |
| Dependencies | None |
| Technical approach | Freeze representative input/output and historical-format fixtures, add synthetic canaries and failing-before security/failure assertions, and label intentional current hazards |
| Data migration | Synthetic/redacted fixtures only; no live export or state mutation |
| Compatibility | Tests observe current behavior and do not change runtime registration, dependencies, or generated files |
| Tests | This card is the fixture/characterization suite: auth/bootstrap, secret flow, journal decode, Ino context/routes, OAuth callback, MCP/UI wire, topology snapshot, workflow crash seams |
| Observability | Test duration/flakiness/fixture-version report; artifacts are secret-free |
| Rollout | Add non-mutating test groups incrementally; unsafe-current assertions are explicitly linked to their fixing task |
| Rollback | Revert a nondeterministic fixture/test only with replacement issue/owner; never delete historical compatibility corpus silently |
| Blast radius | CI duration and future migration confidence only |
| Risk | Low |
| Acceptance criteria | Fixtures are deterministic and secret-free; each P0/P1 migration has at least one pre-change characterization; current happy-path suite remains green |

#### V2-AUTH-001 — Authenticated edge and signed session exchange

| Field | Required content |
|---|---|
| ID | V2-AUTH-001 |
| Priority | P0 |
| Classification | Architectural necessity |
| Outcome | Kernel and MCP HTTP, gRPC/gRPC-Web, and Flutter session exchange authenticate credentials and derive a server-owned ClaimsPrincipal consistently |
| Evidence/rationale | No application auth middleware and caller-controlled clientId currently participates in session resolution **[R]** |
| Scope | Kernel/MCP Program and hosting extensions; Gateway/UiGateway; DigitalBrainAppEndpoints; UserSession token exchange; Flutter auth transport |
| Dependencies | ADR-001 and ADR-008; operator selects local/OIDC authority |
| Technical approach | Add authentication middleware/handlers, issuer/audience/expiry validation, HTTP/gRPC parity, signed session issuance/refresh/revoke, and server context factory |
| Data migration | No clientId-only session is upgraded. Existing users reauthenticate with password/OIDC or present an existing unforgeable server-issued proof before receiving a signed token; no user/journal/key move |
| Compatibility | Development may enable an explicit local-auth profile; existing Flutter login UI can perform the reauthentication exchange, while a legacy clientId remains routing metadata only |
| Tests | Unauthenticated/expired/wrong-audience/wrong-issuer/forged-clientId; clientId-only exchange denied; valid reauthentication/server proof accepted; HTTP/gRPC parity; refresh/revoke/logout |
| Observability | Auth challenge/failure/policy decision counts; protected actor/resource audit |
| Rollout | Report-only policy diagnostics, then enforce Production/Test, then Development; HTTP MCP mutation remains off |
| Rollback | Revert to loopback-only trusted Development profile, never reopen remote anonymous mutation |
| Blast radius | All client entry points and active sessions |
| Risk | Critical |
| Acceptance criteria | Anonymous/invalid credentials cannot read or mutate; clientId alone never yields a token or principal; valid principals match across HTTP and gRPC |

#### V2-AUTH-BOOTSTRAP-001 — One-use operator bootstrap and app configuration authority

| Field | Required content |
|---|---|
| ID | V2-AUTH-BOOTSTRAP-001 |
| Priority | P0 |
| Classification | Architectural necessity |
| Outcome | First-admin creation requires one-use operator proof, recovery is explicit/audited, and app/default configuration requires brain.admin |
| Evidence/rationale | First user defaults to admin,user and app-scope ConfigurationProvided can succeed without a resolved session **[R]** |
| Scope | UserSession/bootstrap owner; local console/operator endpoint; configuration command handler/policy; recovery runbook |
| Dependencies | V2-AUTH-001; ADR-001 operator authority decision |
| Technical approach | Store bootstrap proof hash/expiry/use, bind to local/operator channel, atomically consume it, enforce brain.admin at ingress and handler, and audit recovery |
| Data migration | Existing admin mapping is inventoried; no automatic new admin grant. Completed installations record bootstrap consumed |
| Compatibility | Existing legitimate admin reauthenticates; Development exposes an explicit loopback recovery procedure, never remote anonymous setup |
| Tests | Bootstrap use/replay/expiry/remote denial/concurrency; ordinary user vs admin config; consumed-install restart; audited recovery |
| Observability | Bootstrap/recovery outcome and config authorization decisions; no proof or config secret values |
| Rollout | Test/local bootstrap rehearsal, enforce new installs, migrate existing installs, then close old path |
| Rollback | Operator-only loopback recovery with separate proof and audit; never restore first-caller-admin behavior |
| Blast radius | Initial setup, operator recovery, and global configuration |
| Risk | Critical |
| Acceptance criteria | Exactly one authorized bootstrap succeeds; replay/remote use fails; non-admin app/default config fails at both ingress and handler |

#### V2-IDENTITY-001 — Identity and request-context contract package

| Field | Required content |
|---|---|
| ID | V2-IDENTITY-001 |
| Priority | P0 |
| Classification | Architectural necessity |
| Outcome | Additive PrincipalRef, TenantId, WorkspaceId, versioned RequestContext/PersistedActorSnapshot, membership/policy port interfaces, and validators are available without routing callers |
| Evidence/rationale | Workspace is a string helper and clientId/session resolution substitutes for security identity **[R]** |
| Scope | New Domain/Application identity/context/snapshot/policy contracts; validators/serialization aliases; context-factory interfaces; test fixtures |
| Dependencies | ADR-001 and authenticated principal shape from V2-AUTH-001 |
| Technical approach | Opaque IDs, immutable ephemeral context, separate persisted actor snapshot, explicit versions/auth scheme, scope-match validator, and application-port signatures; no membership store or enforcement in this PR |
| Data migration | None; no membership, user, journal, or grain state is created/moved |
| Compatibility | V1 adapter interfaces can later supply personal/default only from an authenticated server principal; no adapter is activated here |
| Tests | Serialization/member-ID uniqueness; context/snapshot redaction; payload spoof; expiry/version/scope mismatch; service/agent/user principal shapes |
| Observability | Contract validation failure codes only; no runtime policy decision or identity metric labels yet |
| Rollout | Additive package and compile-time port adoption in non-routed test doubles |
| Rollback | Remove unused registrations/interfaces while retaining any published alias/member IDs; no security behavior changes |
| Blast radius | Shared contracts and future application-port compilation |
| Risk | Medium |
| Acceptance criteria | Full RequestContext cannot be persisted as actor snapshot; prohibited fields are absent; validators reject payload-owned authority; runtime behavior/topology is unchanged |

#### V2-IDENTITY-AUTHZ-001 — Membership store and resource-policy enforcement

| Field | Required content |
|---|---|
| ID | V2-IDENTITY-AUTHZ-001 |
| Priority | P0 |
| Classification | Architectural necessity |
| Outcome | Versioned membership and resource policies derive tenant/workspace authority server-side and enforce it at initial HTTP, gRPC, MCP, query, command, config, and action boundaries |
| Evidence/rationale | Workspace is currently a string helper and caller/session routing substitutes for resource authorization **[R]** |
| Scope | Membership store/port; Gateway/MCP context factory; resource policy handlers; initial query/command/config/action ports; authorization fixtures |
| Dependencies | V2-AUTH-001, V2-AUTH-BOOTSTRAP-001, V2-IDENTITY-001; ADR-001 |
| Technical approach | Seed operator/new-user personal/default membership, resolve membership/version per resource, check at ingress and handler, deny stale/missing scope, and emit protected decision audit |
| Data migration | Only operator/bootstrap and newly authenticated-user membership; bulk existing-user/legacy-record ownership migration remains V2-IDENTITY-002 |
| Compatibility | An authenticated V1 adapter may use personal/default only when the membership store proves sole ownership; no clientId or legacy key grants authority |
| Tests | Two-user/two-workspace matrix, spoof/stale membership, ingress/handler parity, service/agent principals, config/admin/action policies, disabled-capability fail-closed |
| Observability | Allow/deny/reason and membership-version mismatch in protected audit; bounded policy/outcome metrics without identity IDs |
| Rollout | Shadow decisions in trusted Test, enforce one read/command family, then every boundary; no Production release with audit-only private access |
| Rollback | Disable affected capability or retain authenticated personal/default access for a proven sole owner; never bypass policy or restore caller-derived scope |
| Blast radius | Every private read/mutation boundary |
| Risk | Critical |
| Acceptance criteria | Every initial boundary derives the same server context and denies cross-user/workspace access at ingress and handler; no audit-only or caller-scope fallback remains |

#### V2-ISOLATION-GATE-001 — Fail-closed capability activation by scope evidence

| Field | Required content |
|---|---|
| ID | V2-ISOLATION-GATE-001 |
| Priority | P0 |
| Classification | Architectural necessity |
| Outcome | A versioned CapabilityIsolationGate blocks every unproved global V1 capability when more than one principal/workspace is enabled and exposes typed availability status |
| Evidence/rationale | Identity arrives in M2, while current Ino context and feed remain global/shared until later waves, creating an unsafe intermediate state without a gate **[R][I]** |
| Scope | Application capability policy/ledger; Gateway/MCP/UI discovery; Ino, journal/query, proposal, feed, credential, connector, tool/model adapters; profile status |
| Dependencies | V2-AUTH-001, V2-IDENTITY-AUTHZ-001 |
| Technical approach | Inventory capability path/version/scope; default unknown to Unavailable; allow V1 only for server-proved sole-owner personal/default; require named isolation test evidence before multi-scope activation |
| Data migration | Additive compatibility ledger/status only; no domain data move. Existing single-owner installation is recorded only after ownership validation |
| Compatibility | Current sole owner can keep a proven personal/default path; a second user/workspace sees unavailable capability until its V2 isolated replacement passes |
| Tests | Two principals/workspaces activate each global path; unknown/version mismatch; sole-owner proof/revocation; discovery hides denied capability; shared feed/global Ino never fallback; restart/status persistence |
| Observability | Capability activation/denial and evidence version by bounded capability/scope-mode/profile; protected owner reason, no identity IDs in metrics |
| Rollout | Inventory/report, deny unknown in Test, prove sole-owner compatibility, enforce before enabling any second principal/workspace |
| Rollback | Retain last proved isolated path, disable capability, or sole-owner personal/default only; never restore global/shared multi-user path |
| Blast radius | Availability of every not-yet-isolated legacy capability during migration |
| Risk | Critical |
| Acceptance criteria | Enabling a second principal/workspace cannot expose any unisolated capability; each activation names passing isolation evidence/version; current shared feed and global Ino are fail-closed |

#### V2-KEYS-001 — Versioned grain address resolver and legacy map

| Field | Required content |
|---|---|
| ID | V2-KEYS-001 |
| Priority | P0 |
| Classification | Architectural necessity |
| Outcome | Canonical V2 grain addresses and a persistent, auditable old-key mapping resolve without activating duplicate logical owners |
| Evidence/rationale | Global main IDs and user/thread strings are widespread; changing type/key creates a different Orleans grain **[R][D]** |
| Scope | NeuronScope/Core identities; IGrainAddressResolver; warmup, Gateway, MCP and integration resolution adapters; mapping store |
| Dependencies | ADR-001, ADR-002; V2-IDENTITY-001, V2-IDENTITY-AUTHZ-001, V2-ISOLATION-GATE-001 |
| Technical approach | Parse/validate v2 key; map known legacy key + authenticated personal/default scope; refuse wildcard/ambiguous mappings; no command cutover |
| Data migration | Inventory existing keys from configuration/journals/projections; seed mapping ledger with source, target, status, hash |
| Compatibility | Existing grain references are untouched; resolver returns legacy grain until a later explicit owner cutover |
| Tests | Canonicalization, malformed/PII keys, ambiguity, cross-workspace guess, legacy main mapping, concurrent resolution |
| Observability | Legacy resolution count and ambiguity errors; protected source/target in spans only |
| Rollout | Read-only inventory, shadow resolution, then require resolver in new adapters |
| Rollback | Disable the new adapter or resolve through the authenticated personal/default mapping only; never restore caller-controlled/direct legacy lookup or allow a legacy key to widen workspace scope |
| Blast radius | All grain lookup call sites; no state move yet |
| Risk | High |
| Acceptance criteria | Same authenticated legacy request resolves the same existing grain; another workspace cannot use the mapping; no duplicate activation is created by tests |

#### V2-ENVELOPE-001 — Additive envelope and schema registry contracts

| Field | Required content |
|---|---|
| ID | V2-ENVELOPE-001 |
| Priority | P0 |
| Classification | Architectural necessity |
| Outcome | Command/Event contracts, persisted actor snapshot, ordered immutable AggregateCommit/effect-transition/index and directory-cursor shapes, stable schema registry, validators, and V1 adapters compile without changing dispatch |
| Evidence/rationale | Synapse conflates intent/fact and JournalJson uses CLR full names **[R]** |
| Scope | Core/Domain identity/command/event/commit/effect/directory contracts; schema/upcaster interfaces; serializer tests; Synapse adapter |
| Dependencies | ADR-003, ADR-004, ADR-005; V2-IDENTITY-001 |
| Technical approach | Assign immutable aliases/member IDs; specify Commit/Event/Effect/Registration sequences and immutable-vs-derived records; validate snapshot/payload/classification/correlation; register but do not route |
| Data migration | None; fixture manifest records current CLR type → stable alias candidates |
| Compatibility | Existing Synapse aliases and member IDs unchanged; bidirectional adapter covered for supported messages |
| Tests | Schema/member uniqueness; actor snapshot exclusion; sequence/ordinal invariants; immutable commit/index reconstruction fixtures; invalid payload/context/cause; V1 round trip; registry duplicate |
| Observability | Schema/version validation failures and V1 adapter use |
| Rollout | Library-only additive release behind no behavior flag |
| Rollback | Remove unused consumers; never reuse published aliases/member IDs |
| Blast radius | Contract packages and serializer graph |
| Risk | Medium |
| Acceptance criteria | Current suite passes; stable aliases are unique; V1 behavior bytes/dispatch are unchanged; prohibited secret fields fail validation |

#### V2-MCP-001 — Immediate MCP containment

| Field | Required content |
|---|---|
| ID | V2-MCP-001 |
| Priority | P0 |
| Classification | Architectural necessity |
| Outcome | Production HTTP mutation tools are not registered; timeline output is centrally redacted/structured; transport mode is explicit |
| Evidence/rationale | Dedicated HTTP MCP registers all mutation tools without auth and direct timeline prints Synapse.ToString **[R]** |
| Scope | MCP Program/options; DigitalBrainReadTools; AppHost capability profile; transport split tests |
| Dependencies | V2-SAFETY-001; ADR-014 |
| Technical approach | Profile-controlled registration; fail startup on unsafe Production combination; structured safe timeline compatibility response |
| Data migration | None |
| Compatibility | Trusted Development stdio can retain V1 tools with explicit option; read tool names remain |
| Tests | Production mutation absence, Development opt-in, unredacted canary, invalid profile, existing read surface |
| Observability | Startup capability manifest; denied/disabled tool counts |
| Rollout | Default off for HTTP mutations; explicit Development-only opt-in |
| Rollback | Re-enable only in trusted local profile; never anonymous Production |
| Blast radius | External MCP agent workflows |
| Risk | Medium |
| Acceptance criteria | Production tool list contains no mutation/approval/admin; timeline canary is redacted; unsafe config fails closed |

#### V2-TOPOLOGY-001 — Capability profiles and topology drift report

| Field | Required content |
|---|---|
| ID | V2-TOPOLOGY-001 |
| Priority | P0 |
| Classification | Architectural necessity |
| Outcome | Secret-free Development/Test/Production capability manifests and normalized Aspire/Pulumi topology snapshots are generated and diffed in CI |
| Evidence/rationale | AppHost and 514-line Pulumi graph differ in MCP, models, voice, telemetry, secrets, and resources **[R]** |
| Scope | AppHost options/snapshot test; deploy snapshot extractor; CI report only |
| Dependencies | ADR-010 and ADR-012 |
| Technical approach | Normalize resource/ref/endpoint/replica/storage/identity/health/capability; require rationale allowlist for target-only deltas |
| Data migration | None |
| Compatibility | No deployed resource changes in this PR |
| Tests | Golden profile/snapshot, missing capability, secret-value exclusion, unexplained drift failure |
| Observability | CI artifact and drift count/category |
| Rollout | Warning report first; make unexplained drift blocking after baseline approval |
| Rollback | Return CI to warning while preserving artifact; no runtime rollback |
| Blast radius | Build/release pipeline |
| Risk | Low |
| Acceptance criteria | Snapshots contain no secret values; every current delta is explicit; an injected MCP/storage/replica drift fails the test |

### P1 — durability and boundary enforcement

#### V2-JOURNAL-001 — Historical journal corpus, aliases, and upcasting reader

| Field | Required content |
|---|---|
| ID | V2-JOURNAL-001 |
| Priority | P1 |
| Classification | Architectural necessity |
| Outcome | Immutable current-format fixtures replay through a stable alias/upcaster reader; unknown/corrupt records quarantine deterministically |
| Evidence/rationale | Current discriminator is CLR full name and unknown types fail; Orleans journaling package is alpha **[R]** |
| Scope | JournalJson; new schema manifest/upcaster reader; fixture generator run once on non-secret synthetic records; compatibility tests |
| Dependencies | V2-ENVELOPE-001; ADR-004 |
| Technical approach | Freeze representative bytes; explicit CLR-name → alias map; pure sequential upcasters; source hash; no in-place source rewrite |
| Data migration | Inventory manifests/checksums; dual-read CLR and alias; write-new stable alias only after corpus gate |
| Compatibility | Current reader remains fallback; missing optional integration produces quarantine/report rather than whole-stream loss where safe |
| Tests | Every known type, old null/default member, renamed alias simulation, missing assembly, unknown/corrupt record, mixed V1/V2 |
| Observability | Read/upcast/quarantine counts by bounded type/version; protected source location |
| Rollout | Shadow decode and compare, then prefer V2 reader, then write-new |
| Rollback | Switch read preference to current reader; stable aliases/upcasters remain published |
| Blast radius | All journals/checkpoints/replay |
| Risk | Critical |
| Acceptance criteria | Fixture corpus has 100% expected decode/hash; unknown record is isolated with source position; no historical blob is mutated |

#### V2-WORKFLOW-001 — Durable proposal/approval state machine

| Field | Required content |
|---|---|
| ID | V2-WORKFLOW-001 |
| Priority | P1 |
| Classification | Architectural necessity |
| Outcome | V2 workflow grain persists every section-6 state/transition, proposal hash, authenticated approval, attempts, and manual resolution |
| Evidence/rationale | Current decision is recorded before inline apply and duplicate decisions then block resume **[R]** |
| Scope | Domain workflow types; new workflow grain/interface; approval application service; V1 proposal/result projection adapter |
| Dependencies | V2-IDENTITY-001, V2-IDENTITY-AUTHZ-001, V2-ENVELOPE-001, ADR-006 |
| Technical approach | Pure transition function + optimistic revision; one workflow per scoped proposal; approval produces Approved audit + ApplyQueued current state/first effect atomically; imported Approved requires due metadata; no external calls in this PR |
| Data migration | Project current proposals/decisions/results into V2 status; ambiguous approved-without-result becomes ManualIntervention, never auto-applied |
| Compatibility | SelfEvolutionNeuron remains V1 owner until later flag; V2 shadows and exposes parity status |
| Tests | All legal/illegal transitions; approval cannot strand between Approved/ApplyQueued; imported Approved due/missing metadata; trusted-system expiry distinct from human rejection; pre/post-dispatch cancellation; retry exhaustion; requester/risk/approver class/hash; duplicate/concurrent approval; stale membership; replay |
| Observability | Transition count/age by bounded state/risk; approval denial reason; protected workflow links |
| Rollout | Shadow workflow for new low-risk synthetic proposals; compare V1 status |
| Rollback | Before cutover, stop V2 shadow submission and read V1 only through authenticated/scoped adapter; after any V2 acceptance, preserve workflow and forward-fix rather than replaying V1 |
| Blast radius | Proposal/list/approval journeys |
| Risk | High |
| Acceptance criteria | New approval atomically queues its first effect; no current Approved state lacks NextActionAt/precomputed effect; retry exhaustion terminates; cancellation never hides uncertainty; approver is server-derived; ambiguous legacy records are not applied |

#### V2-OUTBOX-001 — Grain-owned aggregate commit and outbox dispatcher

| Field | Required content |
|---|---|
| ID | V2-OUTBOX-001 |
| Priority | P1 |
| Classification | Architectural necessity |
| Outcome | One proved grain write seals ordered immutable commits/effect transitions and updates a rebuildable pending index; sequenced pre-commit owner enrollment plus recovery scanning resumes every committed effect without relying on a reminder |
| Evidence/rationale | FireAsync writes journal then calls target with no redelivery; separate writes cannot be assumed atomic **[R][D]** |
| Scope | AggregateCommit/OutboxRecord/EffectTransition/PendingEffectIndex persistence; sequenced CommitOwnerDirectory partitions/cursors; recovery scanner/dispatcher/reminder; one test workflow destination |
| Dependencies | ADR-005; V2-ENVELOPE-001; V2-WORKFLOW-001 |
| Technical approach | Append registration before first commit; repeatedly scan full 1..captured-high-water cycles and reset/increment cycle; use per-owner commit cursors; assign/seal commit/event/effect/transition sequences; atomically append transition + replace index; reminder is hint; lease before dispatch |
| Data migration | New effects only; no attempt to transform old FireAsync intent into pending work without explicit operator migration |
| Compatibility | Legacy dispatch remains for unmigrated handlers; V2 workflow feature flag chooses path |
| Tests | Enrollment/failure; old owner receives commits after its first scan and is seen next cycle; concurrent registration above high-water; cycle rollover/restart; epoch/repartition gap; commit-before-hint; multi/zero-event effects; immutable commit/index rebuild; restore/conflict/lease/outage/order |
| Observability | Directory enrollment/scan age/failure and registered-owner count; outbox depth/oldest age/dispatch latency/conflict/recovery; no high-cardinality labels |
| Rollout | One no-op/internal idempotent effect, then low-risk workflow effect; shadow status |
| Rollback | Stop accepting new V2 workflows and pause dispatch; preserve directory, sealed commits, transitions, index, and pending effects for forward recovery. Never replay the same accepted operation through V1 |
| Blast radius | New workflow/effect execution and storage load |
| Risk | Critical |
| Acceptance criteria | Accepted command has one sealed contiguous CommitSequence or neither; old commits never mutate; index rebuild matches transitions; every registration/commit/effect is rediscovered after crash/restart without duplicate confirmed effect |

#### V2-INBOX-001 — Receiver deduplication and effect verification contract

| Field | Required content |
|---|---|
| ID | V2-INBOX-001 |
| Priority | P1 |
| Classification | Architectural necessity |
| Outcome | Grain receivers suppress duplicate EffectIds atomically; external adapters classify apply/verify/compensate outcomes |
| Evidence/rationale | Orleans retries may duplicate and current handlers have no durable receiver receipt **[R][D]** |
| Scope | Inbox persistence/handler middleware; IEffectAdapter; result categories; test adapter |
| Dependencies | V2-OUTBOX-001; ADR-006 |
| Technical approach | Receiver + EffectId key, payload-hash conflict, recorded result; adapter Apply/Verify/Compensate with provider operation key |
| Data migration | None for legacy deliveries; configure inbox retention longer than maximum replay/retention horizon |
| Compatibility | Wrap only handlers proved idempotent; others stay legacy until adapter exists |
| Tests | Duplicate before/after success, payload conflict, reorder, retention, timeout before/after provider commit, verification tri-state |
| Observability | Inbox duplicate/conflict; adapter result/retry/unknown/verify categories |
| Rollout | Internal receiver first; provider adapters opt in separately |
| Rollback | Bypass wrapper for unmigrated path; never delete receipts needed by pending outbox |
| Blast radius | Migrated handler state and provider effects |
| Risk | High |
| Acceptance criteria | Duplicate EffectId changes receiver state once; ambiguous external result never blind-retries and reaches verified or manual state |

#### V2-PROJECTION-001 — Projection runtime and checkpoint store

| Field | Required content |
|---|---|
| ID | V2-PROJECTION-001 |
| Priority | P1 |
| Classification | Architectural necessity |
| Outcome | Idempotent projection worker enumerates sequenced V2 registrations, pulls authoritative commits by CommitSequence, and provides transactional checkpoint, quarantine, rebuild/version switch, and lag contracts against the selected local/Test store |
| Evidence/rationale | Current reads scan journals and no projector/checkpoint subsystem exists **[R]** |
| Scope | New Projections project; CommitOwnerDirectory source adapter plus V1 backfill source; directory/per-owner checkpoint and quarantine store; test host |
| Dependencies | ADR-005/007; V2-JOURNAL-001; V2-OUTBOX-001 registration contract (dispatcher implementation may proceed in parallel) |
| Technical approach | Repeatedly capture epoch/high-water, process full closed cycles, roll cursor to sequence 1/increment cycle, pull each owner's sealed commits from NextCommitSequence (including zero-event commits), dedup EventId, transact rows/checkpoints, then rebuild/alias switch |
| Data migration | Initial full backfill of synthetic/current fixture journals into disposable projection |
| Compatibility | No production reader switches; V1 sources and V2 events both accepted |
| Tests | Existing owner commits after prior cycle; registration above high-water; cycle rollover/restart; epoch/repartition; commit before notification; multi/zero-event commits; duplicate/order/gap; directory/owner checkpoint crash; poison; rebuild/live tail; parity/rollback |
| Observability | Directory scan age/coverage, owner/commit lag, checkpoint age/rate/failure/quarantine/rebuild progress |
| Rollout | Development shadow worker, Test container worker, then production shadow with no serving traffic |
| Rollback | Stop worker; write model/journals unaffected; switch alias to prior projection |
| Blast radius | Storage/read-model load, not mutations |
| Risk | Medium |
| Acceptance criteria | Rebuild is deterministic; checkpoint never leads committed rows; duplicate EventId has no materialized change |

#### V2-PROJECTION-002 — Timeline, causality, workflow, and operation read models

| Field | Required content |
|---|---|
| ID | V2-PROJECTION-002 |
| Priority | P1 |
| Classification | Architectural necessity |
| Outcome | Scoped cursor-paged TimelineEntry, CausalEdge, WorkflowStatus, and OperationStatus projections replace raw grain scans for new query ports |
| Evidence/rationale | Causal lookup is per grain; MCP/UI/Ino scan write journals and one live timeline trace reached 3.355 seconds **[R][L]** |
| Scope | Projection schemas/workers/query ports; V1 event adapter; parity comparator |
| Dependencies | V2-PROJECTION-001; V2-WORKFLOW-001; V2-IDENTITY-001, V2-IDENTITY-AUTHZ-001, V2-ISOLATION-GATE-001 |
| Technical approach | Keys include tenant/workspace and stable event/workflow IDs; opaque scoped cursors; original source reference and classification retained |
| Data migration | Backfill V1 journals; map global records only to personal/default; ambiguous owner quarantined |
| Compatibility | Query service can fall back to redacted V1 scan while projection catches up; response adapter preserves current MCP fields |
| Tests | Cross-neuron lineage, pagination/tamper, workspace isolation, replay/parity, late/duplicate event, redaction |
| Observability | Query latency, lag, fallback, parity mismatch, quarantine |
| Rollout | Shadow compare; read flag by endpoint/workspace; projection-first then remove fallback |
| Rollback | Switch reads to safe V1 fallback; keep projection for diagnosis |
| Blast radius | Timeline, status, proposal and workbench reads |
| Risk | High |
| Acceptance criteria | Cross-neuron chain is complete for fixture; no cross-workspace row/cursor; parity mismatches are zero for approved corpus |

#### V2-IDENTITY-002 — Personal/default data and membership migration

| Field | Required content |
|---|---|
| ID | V2-IDENTITY-002 |
| Priority | P1 |
| Classification | Architectural necessity |
| Outcome | Existing users have personal tenant/default workspace, membership, migration ledger, and scoped query ownership without copying grain state |
| Evidence/rationale | Current data assumes default workspace/global keys; compatibility requires a deterministic default **[R]** |
| Scope | Migration command/tool restricted to admin; identity/membership store; legacy-record owner metadata for later projection backfill; validation report |
| Dependencies | V2-IDENTITY-001, V2-IDENTITY-AUTHZ-001, V2-ISOLATION-GATE-001, V2-KEYS-001 |
| Technical approach | Idempotent expand migration; stable generated IDs; map legacy user/global records; report ambiguous/unowned items |
| Data migration | Users, sessions metadata, proposals, legacy-record ownership metadata, connector metadata; credentials handled by OAuth tasks |
| Compatibility | Legacy IDs remain; resolver and projection adapter attach personal/default only for authenticated owner |
| Tests | Rerun/idempotency, partial failure/resume, two users, ambiguous record, rollback mapping, no cross-owner assignment |
| Observability | Migrated/skipped/ambiguous/failure counts and ledger position |
| Rollout | Dry-run report, operator approval, batch execute, validate, no destructive contract |
| Rollback | Stop migration and disable ambiguous assets; authenticated sole-owner personal/default mappings may remain. Never expose an unscoped V1 read or deactivate membership to widen access |
| Blast radius | Ownership of all legacy reads |
| Risk | Critical |
| Acceptance criteria | Every in-scope legacy record is mapped or explicitly quarantined; rerun is no-op; two users receive disjoint scopes |

#### V2-INO-KEY-001 — Scoped conversation grains behind ino-main facade

| Field | Required content |
|---|---|
| ID | V2-INO-KEY-001 |
| Priority | P1 |
| Classification | Architectural necessity |
| Outcome | New conversations are owned by tenant/workspace/conversation grains; ino-main is an authenticated personal/default facade only |
| Evidence/rationale | One global Ino journal mixes clients/workspaces; broader context is unfiltered **[R]** |
| Scope | IIno facade/new conversation contract; GrainAddressResolver; Gateway/MCP adapters; conversation projection |
| Dependencies | V2-IDENTITY-002, V2-ISOLATION-GATE-001, V2-KEYS-001, V2-ENVELOPE-001 |
| Technical approach | Resolve/create ConversationId server-side; route by canonical key; adapter imports only matching V1 conversation turns; no full Ino extraction yet |
| Data migration | Lazy copy/reference of matching client/workspace turns into projection; do not replay commands/tool effects |
| Compatibility | Existing Ask/Interact and ino-main continue; feature flag per authenticated session |
| Tests | Same client ID across principals/workspaces, restart, old null workspace → default, no global context import, response parity |
| Observability | Facade/V2 route count, legacy reads, mismatch/leak canary |
| Rollout | Shadow conversation write/read, selected Development sessions, Test, staged users |
| Rollback | Stop new V2 conversation commands and serve existing V2 state read-only while forward-fixing; a V1 facade is allowed only before cutover for one proven authenticated personal/default owner and may never route another user/workspace to global ino-main |
| Blast radius | Primary chat journey |
| Risk | Critical |
| Acceptance criteria | Two-user/two-workspace conversations and context cannot observe each other; V1 response/surface contract remains |

#### V2-TOOLS-001 — Authorized capability catalog

| Field | Required content |
|---|---|
| ID | V2-TOOLS-001 |
| Priority | P1 |
| Classification | Architectural necessity |
| Outcome | Versioned ToolCapabilityDescriptors are filtered by principal/workspace/grants/risk before model exposure and rechecked before execution |
| Evidence/rationale | IInoToolProvider has only clientId and returns string-oriented AIFunctions **[R]** |
| Scope | Application tool catalog/policy; descriptor adapters for Gmail/Salesforce; Ino tool exposure shim |
| Dependencies | V2-IDENTITY-001, V2-IDENTITY-AUTHZ-001, V2-ISOLATION-GATE-001, V2-ENVELOPE-001; ADR-006 |
| Technical approach | Register stable schemas/risk/scopes/data/retry/idempotency; descriptor-to-AIFunction shim calls policy, not provider directly |
| Data migration | None; current provider names map to stable capability IDs |
| Compatibility | Existing tools remain callable through shim for authorized personal/default session |
| Tests | Catalog uniqueness/schema, hidden/denied capability, stale grant, model-forged call, Gmail/Salesforce parity |
| Observability | Exposed/denied/executed counts by bounded capability/provider/outcome |
| Rollout | Shadow catalog comparison; then expose only catalog output |
| Rollback | Restore current tool enumeration for trusted Development; keep Production denial |
| Blast radius | Ino tool availability and connector prompts |
| Risk | High |
| Acceptance criteria | Unauthorized tool is neither listed nor executable; authorization is checked twice; descriptors contain no secrets |

#### V2-TOOLS-002 — Durable invocation coordinator

| Field | Required content |
|---|---|
| ID | V2-TOOLS-002 |
| Priority | P1 |
| Classification | Architectural necessity |
| Outcome | Tool invocation ledger returns typed Success/NeedsAuth/Denied/Failure/Unknown results and uses workflow/outbox semantics for side effects |
| Evidence/rationale | Tools execute inline in ChatClientAgent with no durable attempt/idempotency and journal raw results **[R]** |
| Scope | Invocation workflow grain/application service; tool effect adapters; Ino telemetry shim; operation query |
| Dependencies | V2-TOOLS-001, V2-OUTBOX-001, V2-INBOX-001, V2-PROJECTION-002 |
| Technical approach | Persist request/authorization/attempt/deadline; fast read path still writes receipt; side effects queue; raw output reference + safe summary |
| Data migration | New invocations only; V1 tool events project as legacy audit records |
| Compatibility | AIFunction shim awaits bounded result or returns operation/auth challenge; current response composer adapts text |
| Tests | Cancellation, timeout, duplicate key, auth required, retry, unknown outcome, raw-data redaction, operation status |
| Observability | Queue/latency/result/retry/unknown/model-tool link; no invocation ID metric label |
| Rollout | Gmail read first, Salesforce read, then any side-effect tool after approval policy |
| Rollback | Disable durable path per capability and retain ledger; no blind re-execution of pending effects |
| Blast radius | Ino model/tool result behavior |
| Risk | Critical |
| Acceptance criteria | Every exposed invocation has durable status and structured result; duplicate command produces one state transition; raw provider payload is absent from journals |

#### V2-OAUTH-001 — Shared OAuth coordinator and credential references

| Field | Required content |
|---|---|
| ID | V2-OAUTH-001 |
| Priority | P1 |
| Classification | Architectural necessity |
| Outcome | One provider-neutral OAuth state machine, directly keyed by HMAC(state), owns claim/exchange/unknown/completion, PKCE/code SecretRefs, grants, credential references, refresh leases, and revoke |
| Evidence/rationale | AuthNeuron/IConnector flows duplicate state and secret handling; current state embeds user ID and has no expiry/consumed marker **[R]** |
| Scope | ConnectorAuthApplicationService; OAuthFlowGrain/store; CredentialRef/secret adapter over PackConfig; callback endpoint |
| Dependencies | V2-AUTH-001, V2-IDENTITY-001, V2-IDENTITY-AUTHZ-001, V2-ISOLATION-GATE-001, V2-WORKFLOW-001, V2-OUTBOX-001, ADR-013 |
| Technical approach | Key-versioned random state + HMAC grain key/key ring; persist Started before redirect; exact redirect/S256; atomic Started→Claimed→ExchangeQueued with code SecretRef/EffectId; durable effects/leases; OutcomeUnknown/ReauthorizationRequired; versioned secret replacement |
| Data migration | Inventory old PackConfig; no value move in core PR; create metadata adapter/read-old path |
| Compatibility | AuthNeuron and IConnector start/callback delegate to coordinator; existing callback URLs remain |
| Tests | State entropy/HMAC direct lookup/no index, HMAC key rotation/unknown version, create-before-redirect failure, expiry/replay/provider mismatch, redirect, PKCE/code refs, legal states/revision/lease, exchange crash windows, unknown without blind retry, concurrent refresh, replacement-before-release, revoke, secret-free events |
| Observability | Start/callback/claim/expiry/replay/grant/refresh/revoke outcomes; no state/token values |
| Rollout | Provider fake first, then delegate one real provider at a time |
| Rollback | Disable new authorization starts and retain validated existing credentials/read-only status while forward-fixing; never re-enable state-derived identity, replayable callback, or no-PKCE flow |
| Blast radius | All connector authorization |
| Risk | Critical |
| Acceptance criteria | Callback locates exactly one flow by HMAC and enqueues one exchange EffectId; state survives every crash window; raw state/code/verifier/token never enters domain history; existing credential reads remain |

#### V2-OAUTH-GOOGLE-001 — Google flow and Gmail grant migration

| Field | Required content |
|---|---|
| ID | V2-OAUTH-GOOGLE-001 |
| Priority | P1 |
| Classification | Architectural necessity |
| Outcome | Google uses the shared coordinator; Gmail read/send are separate capability/grant contracts; refresh/revoke behavior is verified |
| Evidence/rationale | Google duplicates flow, has no PKCE, and exposes send under gmail.readonly **[R]** |
| Scope | GoogleAuthNeuron/GoogleConnector/GoogleClientFactory/GmailNeuron/provider descriptor; endpoint tests |
| Dependencies | V2-OAUTH-001, V2-TOOLS-001 |
| Technical approach | One Google adapter; offline access; exact redirect; PKCE compatibility gate; readonly for read and gmail.send for approved send |
| Data migration | Decrypt/re-protect old user:* pack into secret version; retain prior refresh token if exchange omits new; record scopes |
| Compatibility | Read-old credentials and current callback path; send stays disabled unless grant/policy present |
| Tests | Real endpoint with fake token server, PKCE provider spike, scope alignment, retained token, two-user/workspace, revoke/reauth |
| Observability | Grant insufficiency, callback/refresh/revoke result, capability availability |
| Rollout | Shadow credential validation; cut auth start, callback, refresh, then tools; read before send |
| Rollback | Restore old read path using retained pack; new credential reference remains unused, never delete working token |
| Blast radius | Google login and Gmail tools |
| Risk | High |
| Acceptance criteria | Endpoint uses one flow; read works only with readonly-or-stronger grant; send is not exposed without gmail.send and approval |

#### V2-OAUTH-SALESFORCE-001 — Salesforce PKCE, rotation, and credential migration

| Field | Required content |
|---|---|
| ID | V2-OAUTH-SALESFORCE-001 |
| Priority | P1 |
| Classification | Architectural necessity |
| Outcome | Endpoint-used Salesforce flow sends stored S256 verifier, validates grants, serializes rotating refresh, and supports revoke |
| Evidence/rationale | SalesforceConnector drops pending verifier while AuthNeuron path includes it; password flow and rotation are not governed **[R]** |
| Scope | SalesforceAuthNeuron/Connector/ClientFactory/CRM adapter; callback endpoint; secret adapter |
| Dependencies | V2-OAUTH-001, V2-TOOLS-001 |
| Technical approach | One adapter; verifier required in exchange; external client/connected app policy; refresh lease and atomic replacement; password flow legacy-only/off |
| Data migration | Validate old credential metadata; re-protect into secret reference; insufficient/invalid → ReauthorizationRequired, not deletion |
| Compatibility | Existing redirect and valid tokens read through adapter; old flow flag retained for rollback only |
| Tests | End-to-end callback verifier, two users/cross-silo, expiry/replay, scope, concurrent rotation, crash after exchange, revoke |
| Observability | PKCE/grant/rotation/revoke/unknown outcome categories; API version registration |
| Rollout | Sandbox shadow; callback cutover; refresh cutover; tool use; remove legacy password default |
| Rollback | Use retained prior credential only if provider still accepts it; rotation unknown requires reauth/manual, not unsafe reuse |
| Blast radius | Salesforce connection and CRM reads |
| Risk | Critical |
| Acceptance criteria | Captured token request contains matching verifier; concurrent refresh cannot lose replacement; revoke makes tool unavailable |

#### V2-MCP-QUERY-001 — Query-port MCP and structured pagination

| Field | Required content |
|---|---|
| ID | V2-MCP-QUERY-001 |
| Priority | P1 |
| Classification | Architectural necessity |
| Outcome | MCP read tools call scoped projection/query ports and return versioned StructuredContent with opaque pagination/errors |
| Evidence/rationale | Current read tools resolve grains and scan/format journals; no auth context/pagination **[R]** |
| Scope | MCP read tools; IBrainQueryService; projection query adapters; schemas/cursors |
| Dependencies | V2-AUTH-001, V2-IDENTITY-AUTHZ-001, V2-ISOLATION-GATE-001, V2-PROJECTION-002, V2-SAFETY-EGRESS-001, ADR-008 |
| Technical approach | Inject RequestContext/query port only; page/cursor limits; structured error/redaction; remove grain resolver from read tools |
| Data migration | None beyond projection backfill |
| Compatibility | Preserve old tool names/essential fields through response adapter; add schemaVersion/data |
| Tests | Auth/scope, cursor tamper/workspace, size/rate, redaction, projection lag/fallback, constructor dependency guard |
| Observability | Query latency/result/page/denial/fallback by tool class |
| Rollout | New read implementation behind tool option; compare output; enable Production read |
| Rollback | Use redacted V1 query adapter, never direct unsafe formatting |
| Blast radius | MCP inspection clients |
| Risk | Medium |
| Acceptance criteria | MCP read tool graph has no IGrainFactory; cross-workspace cursor fails; all responses validate schema |

#### V2-MCP-COMMAND-001 — Command, approval, and admin MCP ports

| Field | Required content |
|---|---|
| ID | V2-MCP-COMMAND-001 |
| Priority | P1 |
| Classification | Architectural necessity |
| Outcome | Idempotent commands, approvals, and administration are separate scoped tool sets calling application ports and returning operation status |
| Evidence/rationale | Current mutation tools fire arbitrary Synapses, trust decided_by/client IDs, and mix queries/admin/demo **[R]** |
| Scope | MCP command/approval/admin tool types; ICommandBus/IApprovalService/IAdministrationService; transport authorization |
| Dependencies | V2-MCP-QUERY-001, V2-WORKFLOW-001, V2-TOOLS-002 |
| Technical approach | Remove generic fire/action target; require idempotency; server-derived approver; scopes at list/invoke; structured status |
| Data migration | Existing MCP client registrations/scopes require re-consent; no domain state move |
| Compatibility | Trusted Development V1 namespace may remain temporarily; Production uses V2 names/contracts |
| Tests | Scope matrix, hidden list/invoke denial, idempotency conflict, approval hash, admin off, audit/rate/size, Origin/audience |
| Observability | Command accepted/denied/status, approval/admin audit, rate rejection |
| Rollout | brain.act allowlist, then brain.approve; brain.admin separate operator profile |
| Rollback | Disable tool class registration; pending operations continue through workflow |
| Blast radius | External automation/agent mutation |
| Risk | Critical |
| Acceptance criteria | No MCP code creates Synapse or accepts approver authority; duplicate command returns same OperationId; Production admin absent by default |

#### V2-UI-001 — Versioned surface protocol and V1 adapter

| Field | Required content |
|---|---|
| ID | V2-UI-001 |
| Priority | P1 |
| Classification | Architectural necessity |
| Outcome | Versioned/audience-bound, token-free StoredSurfaceRecord and wire SurfaceEnvelope schemas coexist with UiSurface through capability negotiation and a V1 adapter |
| Evidence/rationale | Current Kind/Props and synapseType/props actions have no version, expiry, audience, revision, or authorization **[R]** |
| Scope | UI.Contracts/Runtime protocol types and validator; V1 adapter/composer; Flutter envelope decoder and golden fixtures |
| Dependencies | V2-IDENTITY-001, V2-ENVELOPE-001, ADR-009 |
| Technical approach | Define stable protocol/surface/action-binding schemas, audience/revision/expiry/capabilities, token-free persistence shape, stable hash excluding delivery tokens, and deterministic V1 wrapping |
| Data migration | Existing surfaces wrapped as v1; no historical action token is honored as V2 |
| Compatibility | Flutter renders V1 and V2; current producers use adapter without rewrite |
| Tests | Durable-schema token absence; stable content hash; schema/round-trip/goldens; capability negotiation; audience/revision/expiry validation; V1 parity |
| Observability | Version negotiation and unsupported capability/schema outcomes |
| Rollout | Decode-only Flutter, server shadow/dual emit, V2 preferred only for supported clients |
| Rollback | Negotiate V1; token-free V2 records remain inert and readable |
| Blast radius | Surface serialization and rendering, not action execution |
| Risk | High |
| Acceptance criteria | Stored schemas cannot contain ActionToken; stable payloads hash identically; all supported V1 screens remain golden-identical under the adapter |

#### V2-UI-ACTION-001 — Authorized one-use UI action service

| Field | Required content |
|---|---|
| ID | V2-UI-ACTION-001 |
| Priority | P1 |
| Classification | Architectural necessity |
| Outcome | Token-free versioned command bindings become short-lived principal/workspace/surface-bound wire tokens whose binding-wide use counter and idempotency prevent duplicate commands across reconnect-issued tokens |
| Evidence/rationale | Current synapseType/props actions let clients describe mutation without expiry, binding, or replay protection **[R]** |
| Scope | UI action binding/token store and service; Gateway action endpoint; command template resolver; Flutter action submitter |
| Dependencies | V2-UI-001, V2-AUTH-001, V2-IDENTITY-001, V2-IDENTITY-AUTHZ-001, V2-ISOLATION-GATE-001, V2-WORKFLOW-001, V2-OUTBOX-001 |
| Technical approach | Persist template/schema/idempotency binding; mint wire token/hash; atomically consume token + claim ordinal + preassign OperationId/key + append CommandQueued use record + command-submission outbox. Target reauthorizes/dedups; receipts append use transitions |
| Data migration | No V1 dictionary action becomes a trusted token; migrate surface families through allowlisted command templates |
| Compatibility | Unmigrated V1 actions remain behind the V1 adapter/profile; V2 clients receive typed action errors |
| Tests | No bearer persistence; template/schema/forgery/scope/replay; two-token MaxUses race; crash before action commit, after queue before submit, after target accept before receipt; same-input same OperationId, different-input conflict; use-index rebuild |
| Observability | Issued/used/denied/expired/replayed by bounded action type/reason; protected operation link |
| Rollout | One low-risk action family, then approvals and connector writes only after policy tests |
| Rollback | Stop token issuance and drain/preserve queued use/outbox records; never replay through V1. Unmigrated V1 family is allowed only under the capability isolation gate and never via arbitrary raw target/type |
| Blast radius | Every migrated server-driven action/mutation |
| Risk | Critical |
| Acceptance criteria | Action-owner commit always queues use+submission or neither; crash recovery yields one preassigned OperationId; all tokens share MaxUses; input conflict rejects; target reauthorizes/dedups; bearer never persists |

#### V2-FEED-001 — Private durable surface feed projection

| Field | Required content |
|---|---|
| ID | V2-FEED-001 |
| Priority | P1 |
| Classification | Architectural necessity |
| Outcome | A token-free per-audience feed projection/store assigns sequence, retention, dedup, catch-up, and gap/reset semantics without shared null audience |
| Evidence/rationale | HomeFeedBus always subscribes shared stream, DropOldest buffer, in-process dedup, no sequence/resume **[R]** |
| Scope | SurfaceFeed projection/worker/store/query port; sequence/checkpoint/retention model; V1 shadow source |
| Dependencies | V2-PROJECTION-001, V2-UI-001, V2-IDENTITY-002, V2-ISOLATION-GATE-001 |
| Technical approach | Monotonic sequence over token-free StoredSurfaceRecord per canonical audience; idempotent projection; retained range/current snapshot; explicit Public audience |
| Data migration | Backfill current active surfaces where reconstructable; otherwise initial current-snapshot/reset |
| Compatibility | Authenticated addressed V1 may remain authoritative only for gate-approved sole owner while V2 shadows; shared private stream is disabled before any second scope |
| Tests | Two-user/workspace storage isolation; duplicate/event replay; sequence/checkpoint crash; retention reset/snapshot; rebuild; multi-silo publisher |
| Observability | Projection publish/lag/reset/dedup/retention by audience kind only; no audience ID metric label |
| Rollout | Shadow projection, compare active-surface counts/hashes, then mark query port ready |
| Rollback | Stop projector/switch alias; journals remain. V1 private delivery is addressed sole-owner-only under isolation gate; otherwise feed disables; no V2 row enters shared stream |
| Blast radius | Feed storage/projection load, not live delivery |
| Risk | Critical |
| Acceptance criteria | Private rows are inaccessible to every other test audience; replay/rebuild yields identical sequence/content; retention returns explicit ResetRequired |

#### V2-FEED-DELIVERY-001 — Resumable V2 feed transport and client controller

| Field | Required content |
|---|---|
| ID | V2-FEED-DELIVERY-001 |
| Priority | P1 |
| Classification | Architectural necessity |
| Outcome | WatchHomeFeedV2 and Flutter controller resume by sequence, acknowledge, handle backpressure/gaps, and inject fresh action tokens only at authenticated delivery |
| Evidence/rationale | Current HomeFeedBus uses DropOldest, in-process dedup, shared subscription, and no resumable cursor **[R]** |
| Scope | WatchHomeFeedV2 gRPC/service; feed query adapter; V1 bridge/negotiation; Flutter feed controller/store; action-token materializer |
| Dependencies | V2-FEED-001, V2-UI-ACTION-001, V2-AUTH-001 |
| Technical approach | Authenticate audience; afterSequence plus ack; retained catch-up then live tail; mint tokens per delivery; close with resumable cursor instead of silent drop; explicit reset snapshot |
| Data migration | Client cursor starts at current snapshot or V1-derived last known revision; no server record rewrite |
| Compatibility | Negotiation may select addressed V1 only for gate-approved sole owner; otherwise V2 or typed Unavailable. Private V2 rows never bridge to shared V1 |
| Tests | Disconnect/reconnect exact resume; duplicate/gap/reset; slow-client backpressure; multi-silo stream; fresh and prior token race shares binding MaxUses/idempotency; old-token expiry; two-workspace denial |
| Observability | Delivery/catch-up/lag/reset/dedup/slow-client/token-materialization by bounded protocol/audience kind |
| Rollout | Selected Test sessions, dual delivery comparison without duplicate actions, staged client versions, default V2 |
| Rollback | Retain V2 rows/cursors; use only authenticated addressed V1 for a gate-approved sole owner, otherwise disable private feed. Current shared HomeFeedBus is never rollback |
| Blast radius | Login shell, chat, tasks, connectors, and workbench live delivery |
| Risk | Critical |
| Acceptance criteria | Reconnect resumes exactly by sequence; catch-up issues fresh valid tokens; slow clients receive resumable close/reset and no silent DropOldest |

### P2 — scalability, maintainability, and operational maturity

#### V2-INO-CONTEXT-001 — Workspace-safe context and memory query extraction

| Field | Required content |
|---|---|
| ID | V2-INO-CONTEXT-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | Ino context assembler and memory query port use only classified, cited, workspace-authorized projections |
| Evidence/rationale | BuildContextAsync and CreateMemorySummary use unfiltered recent journals; ContextNeuron recall ignores workspace **[R]** |
| Scope | Ino context packet/builder; ContextNeuron adapter; MemoryEvidence/Search projections; redaction/trust policy |
| Dependencies | V2-INO-KEY-001, V2-PROJECTION-001, V2-IDENTITY-002 |
| Technical approach | Extract assembler; query by scope/purpose/classification; include provenance/trust/token budget; no arbitrary journal scan |
| Data migration | Backfill MemoryEvidence from scoped legacy MemoryStored/Summary; ambiguous entries default-only or quarantine |
| Compatibility | Render same context packet shape to current prompt while source changes behind flag |
| Tests | Two users/workspaces, trust ranking, redaction, token budget, citations, legacy null workspace, no current-journal leakage |
| Observability | Evidence count/source/trust, query latency, denied/redacted/ambiguous; no content metrics |
| Rollout | Shadow packet compare with leak canaries; then select V2 per conversation |
| Rollback | Use prior safe default-only assembler, not unfiltered multi-workspace path |
| Blast radius | Ino response quality/privacy |
| Risk | Critical |
| Acceptance criteria | Prompt capture contains only authorized evidence with source IDs; isolation matrix passes for context and semantic recall |

#### V2-INO-PLAN-001 — Intent/capability planner extraction

| Field | Required content |
|---|---|
| ID | V2-INO-PLAN-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | Intent classification, special routes, capability selection, and approval needs produce a typed plan outside InoNeuron |
| Evidence/rationale | InoNeuron mixes static phrases, classifiers, tools, automation, schema, model and UI paths **[R]** |
| Scope | Planner application service; plan contract; adapters for current classifiers/special handlers |
| Dependencies | V2-TOOLS-001, V2-INO-CONTEXT-001 |
| Technical approach | Move one intent family at a time; pure deterministic plan; plan references capabilities, not AIFunction/provider objects |
| Data migration | None |
| Compatibility | InoNeuron invokes planner and existing handler; plan-to-V1 behavior adapter |
| Tests | Golden intent/plan for every current route, confidence/fallback, unauthorized capability, automation approval |
| Observability | Plan route/capability/risk/outcome by bounded IDs |
| Rollout | Shadow plan comparison, then route intent families individually |
| Rollback | Disable planner family flag |
| Blast radius | Ino routing and feature discovery |
| Risk | High |
| Acceptance criteria | Existing characterized prompts choose equivalent route; plan contains no secret/client authority; unauthorized capability never selected |

#### V2-INO-RESPONSE-001 — Request coordination and response/surface composition extraction

| Field | Required content |
|---|---|
| ID | V2-INO-RESPONSE-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | Conversation grain delegates model/tool orchestration and response/surface composition through application ports |
| Evidence/rationale | InoNeuron owns model call, conversation persistence, task/proposal collection, memory summary and many surface builders **[R]** |
| Scope | IRequestCoordinator; IResponseComposer; ISurfaceComposer; conversation projection adapter; InoNeuron facade |
| Dependencies | V2-INO-PLAN-001, V2-TOOLS-002, V2-MODEL-002 |
| Technical approach | Extract response event first, then surfaces, then coordinator; keep grain as state owner/compat facade. Adopt V2-UI-001 only when Milestone 8 enables V2 envelope emission |
| Data migration | New response/conversation events only; projection imports matching V1 turns |
| Compatibility | Same Ask/Interact and V1 surface adapter; no one-shot replacement |
| Tests | Chat/tool/auth/proposal/schema/graph surface goldens, cancellation, restart, response-operation pairing |
| Observability | Coordinator stage latency/failure and response/surface causation |
| Rollout | Per stage/family flags with shadow composition hash |
| Rollback | Use current in-grain composer for affected family |
| Blast radius | Primary Ino and UI journey |
| Risk | High |
| Acceptance criteria | InoNeuron no longer constructs provider clients/tools/surface dictionaries; characterized outputs remain compatible |

#### V2-MODEL-001 — Model-client factory contract and reference adapter

| Field | Required content |
|---|---|
| ID | V2-MODEL-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | Provider-neutral factory/adapter contracts validate registration, CredentialRef, capabilities, and wrapper order through a deterministic reference adapter without routing production calls |
| Evidence/rationale | Provider construction is duplicated across three factories and subsets differ **[R]** |
| Scope | Infrastructure.AI factory/adapter interfaces; immutable options; validation/errors; fake/reference adapter and tests |
| Dependencies | ADR-011; V2-ENVELOPE-001 |
| Technical approach | Resolve adapter by stable provider ID; validate registration/capabilities/auth mode; compose telemetry/resilience once; reject duplicate service keys |
| Data migration | None; no setting reader/writer changes |
| Compatibility | Existing DI/factories remain authoritative; new contract is additive |
| Tests | Fake chat/embedding/voice, missing CredentialRef, duplicate provider/service key, wrapper ordering, deterministic sanitized errors |
| Observability | Reference construction/config outcome by bounded provider/capability class |
| Rollout | Add library, fake adapter, and tests only |
| Rollback | Remove unused registration while retaining published aliases; no runtime calls moved |
| Blast radius | New Infrastructure.AI contract only |
| Risk | Low |
| Acceptance criteria | Reference adapter constructs each capability through one validated path; production factory/call behavior is unchanged |

#### V2-MODEL-LOCAL-001 — Local Ollama, embedding, and Whisper adapters

| Field | Required content |
|---|---|
| ID | V2-MODEL-LOCAL-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | Ollama chat/embedding and local Whisper capability construction use unified adapters with current options and cache behavior |
| Evidence/rationale | Local chat/embedding/voice are defaults but construction/capability paths are split **[R]** |
| Scope | Ollama chat/embedding builders; local Whisper adapter/selection; descriptor mapping; provider tests |
| Dependencies | V2-MODEL-001; current AppHost registration schema |
| Technical approach | Implement stable local provider adapters, map service keys/capabilities, and reuse wrappers; no caller cutover |
| Data migration | None; read current AppHost registrations/cache paths |
| Compatibility | Current local clients remain authoritative until facade cutover; descriptors compare equal |
| Tests | Construction parity, capability mismatch, missing model, cache cold/unavailable, service-key collision |
| Observability | Construction/capability health by bounded local provider/capability |
| Rollout | Shadow construction/parity in Development/Test |
| Rollback | Stop shadow adapter; current local path unchanged |
| Blast radius | Local model startup tests only until facade cutover |
| Risk | Medium |
| Acceptance criteria | Shadow local clients/descriptors match current configuration and typed unavailable behavior |

#### V2-MODEL-OPENAI-001 — OpenAI and Azure OpenAI adapters

| Field | Required content |
|---|---|
| ID | V2-MODEL-OPENAI-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | OpenAI/Azure OpenAI construction uses adapters with explicit API-key versus managed-identity authentication and capability parity |
| Evidence/rationale | OpenAI/Azure construction and credential branches are duplicated **[R]** |
| Scope | OpenAI/Azure builders; CredentialRef/managed-identity options; descriptors and tests |
| Dependencies | V2-MODEL-001; ADR-011 |
| Technical approach | Separate stable provider/auth modes; validate endpoint/deployment/model; reuse wrappers; shadow-compare without exposing credentials |
| Data migration | Read current registry/env keys only; no writes or key removal |
| Compatibility | Existing DI/options and characterized auth precedence remain until cutover |
| Tests | Key/managed identity, endpoint/deployment/model, mixed credential, tool/structured output, construction parity |
| Observability | Safe construction/auth-mode/health outcome by bounded provider class |
| Rollout | Shadow in Test, then expose to facade task |
| Rollback | Disable adapter; current construction remains |
| Blast radius | Cloud OpenAI construction after facade cutover |
| Risk | High |
| Acceptance criteria | Shadow clients match current registration/capabilities/auth selection with no secret diagnostics |

#### V2-MODEL-ANTHROPIC-001 — Anthropic adapter

| Field | Required content |
|---|---|
| ID | V2-MODEL-ANTHROPIC-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | Anthropic construction and capability mapping use one provider adapter with current behavior characterized |
| Evidence/rationale | Anthropic exists in only a subset of duplicated provider switches **[R]** |
| Scope | Anthropic builder/options/CredentialRef; descriptor mapping; tests |
| Dependencies | V2-MODEL-001 |
| Technical approach | Implement one adapter, typed validation, common wrappers, and shadow comparison; no call routing |
| Data migration | Read current compatibility options only |
| Compatibility | Existing Anthropic path remains until facade cutover |
| Tests | Construction parity, missing credential/model, capability truth, sanitized provider errors |
| Observability | Safe construction/health outcome by Anthropic capability class |
| Rollout | Test shadow construction |
| Rollback | Disable adapter; current path unchanged |
| Blast radius | Anthropic construction after facade cutover |
| Risk | Medium |
| Acceptance criteria | Adapter matches characterized options/capabilities and emits no secret/raw provider error |

#### V2-MODEL-XAI-001 — xAI/OpenAI-compatible adapter

| Field | Required content |
|---|---|
| ID | V2-MODEL-XAI-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | xAI construction uses an explicit adapter over the OpenAI-compatible client without inheriting unproved capabilities |
| Evidence/rationale | Current xAI switch reuses an OpenAI-compatible builder without centralized capability parity **[R]** |
| Scope | xAI builder/options/CredentialRef; endpoint/model/capability mapping; tests |
| Dependencies | V2-MODEL-001 |
| Technical approach | Stable provider ID, allowlisted endpoint/config, declared capabilities, common wrappers, and shadow comparison |
| Data migration | Read current compatibility options only |
| Compatibility | Existing xAI path remains until facade cutover |
| Tests | Endpoint/model/key validation, unsupported capability exclusion, sanitized failures, construction parity |
| Observability | Safe construction/health outcome by xAI capability class |
| Rollout | Test shadow construction |
| Rollback | Disable adapter; current path unchanged |
| Blast radius | xAI construction after facade cutover |
| Risk | Medium |
| Acceptance criteria | Adapter matches characterized construction and never advertises an unproved capability |

#### V2-MODEL-FACADE-001 — Delegate keyed chat-client registration

| Field | Required content |
|---|---|
| ID | V2-MODEL-FACADE-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | DigitalBrainChatClientRegistration delegates keyed chat-client construction to unified adapters while preserving service keys |
| Evidence/rationale | Three construction paths duplicate switches and provider subsets **[R]** |
| Scope | DigitalBrainChatClientRegistration; keyed DI registration; compatibility parser and tests |
| Dependencies | V2-MODEL-LOCAL-001, V2-MODEL-OPENAI-001, V2-MODEL-ANTHROPIC-001, V2-MODEL-XAI-001 |
| Technical approach | Replace keyed registration switch with factory delegation, compare descriptors/wrappers, and preserve every service key |
| Data migration | Read old env registry/llm_key through compatibility parser; no setting writes |
| Compatibility | Existing keyed DI registrations and old config parser remain; scoped/unkeyed callers are untouched |
| Tests | Each provider through keyed registration, DI/service-key parity, duplicate key, startup, old configuration |
| Observability | Legacy/new construction route and mismatch by bounded provider/capability |
| Rollout | Shadow descriptor comparison, then keyed registration flag |
| Rollback | Repoint keyed registration to old implementation; registrations/config remain |
| Blast radius | Keyed model registration and startup |
| Risk | High |
| Acceptance criteria | Every keyed provider resolves with parity and stable service key; no provider switch remains in keyed registration |

#### V2-MODEL-FACADE-SCOPED-001 — Delegate scoped chat-client factory

| Field | Required content |
|---|---|
| ID | V2-MODEL-FACADE-SCOPED-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | ScopedChatClientFactory delegates scoped/provider-specific construction to unified adapters with current precedence |
| Evidence/rationale | Scoped factory duplicates provider switches and can diverge from keyed registration **[R]** |
| Scope | ScopedChatClientFactory; scoped options/CredentialRef resolution; tests |
| Dependencies | V2-MODEL-FACADE-001 |
| Technical approach | Resolve canonical registration then delegate factory; compare client descriptor/wrappers and preserve scoped lifetime/caching |
| Data migration | Read old scoped options only; no settings writes |
| Compatibility | Existing scoped interface and precedence remain |
| Tests | Every provider, scope isolation, lifetime/cache, old options, parity with keyed construction, no direct builder |
| Observability | Scoped legacy/new construction and mismatch by bounded provider/capability |
| Rollout | Shadow then scoped-factory feature flag |
| Rollback | Repoint scoped facade to old implementation; registrations unchanged |
| Blast radius | Scoped model callers |
| Risk | High |
| Acceptance criteria | Scoped factory has no provider construction switch and matches characterized lifetime/options for every provider |

#### V2-MODEL-FACADE-DEFAULT-001 — Delegate unkeyed default and modality selectors

| Field | Required content |
|---|---|
| ID | V2-MODEL-FACADE-DEFAULT-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | DigitalBrainChat unkeyed default plus embedding/voice selectors delegate to unified adapters with current fallback precedence |
| Evidence/rationale | Unkeyed/default and modality construction retain independent switches/configuration **[R]** |
| Scope | DigitalBrainChat; default DI registration; embedding/voice selectors; compatibility parser; tests |
| Dependencies | V2-MODEL-FACADE-001, V2-MODEL-FACADE-SCOPED-001 |
| Technical approach | Resolve canonical default registrations, delegate each modality, compare wrappers/capabilities, and add static no-bypass guard |
| Data migration | Read old env registry/llm_key only; no setting writes |
| Compatibility | Existing unkeyed/default interface and precedence remain until policy router cutover |
| Tests | Default provider precedence, all modalities, missing optional capability, old config conflict, startup, no direct builder guard |
| Observability | Default/modality legacy-new route and mismatch by bounded capability/provider |
| Rollout | Embedding, voice, then unkeyed chat flags after shadow parity |
| Rollback | Repoint affected selector/default to old implementation; no registry change |
| Blast radius | Default LLM, embedding, and voice callers |
| Risk | High |
| Acceptance criteria | All default/modality construction delegates with parity; static guard finds no provider builder outside adapters |

#### V2-MODEL-002 — Policy-driven model router

| Field | Required content |
|---|---|
| ID | V2-MODEL-002 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | One IModelRouter enforces capability, tenant/workspace, privacy/residency, budget, health, latency, and bounded fallback with audit |
| Evidence/rationale | Ino and LlmResponder own separate role/config selection and no policy decision is recorded **[R]** |
| Scope | Application routing policy/store; health/budget ports; Ino/LlmResponder adapters; decision projection |
| Dependencies | V2-MODEL-FACADE-DEFAULT-001, V2-IDENTITY-001, V2-IDENTITY-AUTHZ-001, V2-ISOLATION-GATE-001, V2-PROJECTION-001 |
| Technical approach | Deterministic policy precedence; reserve budget; pre-authorized fallback list; immutable decision version |
| Data migration | Map system llm_provider/llm_key to default policy/registration; write V2 only after cutover |
| Compatibility | Shadow current selection; honor old override when policy allows and report conflicts |
| Tests | Role/capability, tools/structured output, health/rate, privacy/residency, token/cost, latency/fallback, dynamic version |
| Observability | Decision reason, provider/model role, latency/tokens/cost bucket/fallback/outcome; protected operation link |
| Rollout | Shadow decisions, selected Development conversations, Test, staged tenants |
| Rollback | Route through compatibility policy matching current precedence |
| Blast radius | Response quality, cost, privacy, provider availability |
| Risk | Critical |
| Acceptance criteria | No call violates an enforced privacy/residency/budget constraint; every call has one recorded decision/version |

#### V2-FLUTTER-001 — RFW registry modularization

| Field | Required content |
|---|---|
| ID | V2-FLUTTER-001 |
| Priority | P2 |
| Classification | Optional improvement |
| Outcome | Extract RFW registry responsibility groups without changing exports, widget names, constructors, cached format, or goldens |
| Evidence/rationale | RFW library is 5,392 LOC; shell 807; protocol work otherwise has a large client blast radius **[R]** |
| Scope | digitalbrain_rfw_library.dart; new registry group files and barrel exports; Flutter registry/widget tests |
| Dependencies | V2-UI-001 interfaces stable |
| Technical approach | Extract one registry family per commit behind existing public exports; assert unique stable widget names; no protocol/generated/shell edits |
| Data migration | None; cached surface format unchanged |
| Compatibility | Stable widget vocabulary and public host APIs; V1/V2 decoders both supported |
| Tests | Existing widget tests/goldens plus registry uniqueness and public-import compatibility |
| Observability | Build/test size and registry collision diagnostics only |
| Rollout | One registry family per pure-refactor PR under identical tests |
| Rollback | Revert the independent extraction commit; protocol/cache data unchanged |
| Blast radius | Flutter RFW widget registration/rendering |
| Risk | Medium |
| Acceptance criteria | No widget registration/name/export/golden change; registry families have documented one-way dependencies and no duplicate name |

#### V2-FLUTTER-HOST-001 — Flutter shell, session, feed, and action controllers

| Field | Required content |
|---|---|
| ID | V2-FLUTTER-HOST-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | Shell/session/feed/action controllers are extracted and adopt V2 decoder/feed interfaces behind the current runtime-host API without a UI rewrite |
| Evidence/rationale | The Flutter shell is 807 LOC and currently mixes host lifecycle, transport, session, feed, actions, and rendering **[R]** |
| Scope | forui_app_shell.dart; runtime host; new session/feed/action controllers; local cursor store; Flutter integration/golden tests |
| Dependencies | V2-FEED-DELIVERY-001, V2-UI-ACTION-001 |
| Technical approach | Characterize shell states, extract controllers one at a time through existing facade, then enable V2 negotiation/cursor/action submission per feature flag |
| Data migration | Migrate only client cursor/session metadata with versioned local keys; RFW cache remains readable |
| Compatibility | Existing host constructors/routes/widget vocabulary remain; V1 and V2 decoders/transports coexist |
| Tests | Login/logout/refresh; offline/reconnect/reset; V1/V2 negotiation; action success/expiry; shell navigation and visual goldens |
| Observability | Safe client protocol/session/feed/action error categories and app version; no token or raw surface data |
| Rollout | Pure extraction first, then selected V2 sessions by platform/version |
| Rollback | Disable V2 controller/retain facade and V1 transport; keep compatible local metadata decoder |
| Blast radius | Flutter application shell and all live user journeys |
| Risk | High |
| Acceptance criteria | Shell behavior/goldens remain; V2 reconnect/action tests pass; switching negotiation back to V1 requires no cache or UI rewrite |

#### V2-ASPIRE-001 — Explicit capability profiles and service discovery

| Field | Required content |
|---|---|
| ID | V2-ASPIRE-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | Development, Test, and Production capability manifests drive the AppHost graph, required WaitFor edges, and logical service-discovery references consistently |
| Evidence/rationale | MCP lacks ServiceDefaults; /health is self-only; Flutter/MCP references and Azurite durability are ambiguous **[R][L]** |
| Scope | AppHost and DigitalBrain.Aspire profile/options; resource references/WaitFor; host capability configuration; topology snapshot tests |
| Dependencies | V2-TOPOLOGY-001; ADR-010 |
| Technical approach | Define immutable validated profile manifest, construct resources/references from it, replace hard-coded internal URLs, and distinguish required startup dependencies from optional capabilities |
| Data migration | None; this card does not change volumes, probes, or stored paths |
| Compatibility | Existing resource names/endpoints retained where possible |
| Tests | Run/publish/Test graph snapshots, reference resolution, required/optional WaitFor, missing optional model, invalid profile startup |
| Observability | Safe profile/resource/capability graph status and drift reason |
| Rollout | Development/Test first; production topology unchanged until deploy task |
| Rollback | Select the prior explicit profile/graph snapshot; do not fall back to implicit environment defaults or hard-coded URLs |
| Blast radius | AppHost graph and local/Test startup ordering |
| Risk | High |
| Acceptance criteria | Each profile produces its approved resource/reference/capability snapshot; required dependencies sequence startup; optional absence returns typed Unavailable rather than blocking Kernel |

#### V2-ASPIRE-HEALTH-001 — ServiceDefaults, readiness, and liveness parity

| Field | Required content |
|---|---|
| ID | V2-ASPIRE-HEALTH-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | MCP and every network host adopt common ServiceDefaults/OTel and expose distinct dependency-aware readiness versus process liveness |
| Evidence/rationale | MCP lacks ServiceDefaults and current /health is primarily self-health; sampled MCP/Flutter telemetry was absent **[R][L]** |
| Scope | ServiceDefaults; MCP/Kernel/Telegram/other host Program files; health-check registrations/endpoints; AppHost probe wiring |
| Dependencies | V2-ASPIRE-001, V2-OBS-001 |
| Technical approach | Add common host defaults; /alive process-only; /health checks Orleans and authoritative state/journal/directory/credential initialization; optional providers feed capability health only |
| Data migration | None |
| Compatibility | Preserve endpoint paths; make stronger readiness visible in Test before it gates Production traffic |
| Tests | MCP trace/health, authoritative storage/directory loss, Orleans unavailable, optional model absent, /alive vs /health, startup/recovery transitions |
| Observability | Readiness/liveness state and bounded reason, initialization duration, host instrumentation presence |
| Rollout | MCP/Test hosts first, Kernel Test, then production probe gate with drain observation |
| Rollback | Remove a faulty dependency check while returning typed degraded/unavailable; never make /health an unconditional self-only success |
| Blast radius | Host startup, probes, and traffic admission |
| Risk | High |
| Acceptance criteria | MCP emits common OTel/health; /health fails on authoritative dependency loss while /alive stays healthy; optional provider loss does not falsely kill Kernel |

#### V2-ASPIRE-STORAGE-001 — Local durable storage and replaceable cache semantics

| Field | Required content |
|---|---|
| ID | V2-ASPIRE-STORAGE-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | Each Development/Test storage and model-cache resource declares named-volume, disposable, backup/export, and recreation semantics without silently moving data |
| Evidence/rationale | Live Azurite reported persistent lifetime without a mounted volume, while Ollama/OpenWebUI/Whisper used named volumes with different durability roles **[L]** |
| Scope | Azurite/Ollama/OpenWebUI/Whisper AppHost declarations; profile manifest storage roles; local migration/export instructions; recreation tests |
| Dependencies | V2-ASPIRE-001; operator decision whether default local state is durable or disposable |
| Technical approach | Label authoritative vs replaceable data; add named Azurite volume only through opt-in export/import migration; pin cache/model versions; expose cold/warm capability separately |
| Data migration | Optional explicit Azurite export/import to named volume with checksum and rollback copy; caches may repopulate and never contain domain source of truth |
| Compatibility | Existing disposable profile/path remains named; no silent default path change. Cache loss changes latency only |
| Tests | Container recreation with/without volume, export/import checksum, cache deletion/cold start, model version pin, domain state never stored in cache volume |
| Observability | Storage role/volume mode, persistence warning, cache cold/warm/download failure; no local paths/secrets in metrics |
| Rollout | Add status/labels, Test recreation, opt-in durable Development profile, then decide default |
| Rollback | Select prior explicit disposable profile or restore verified export; never claim persistence without mounted durable storage |
| Blast radius | Local/Test data retention and model cold-start behavior |
| Risk | High |
| Acceptance criteria | Authoritative local profile survives recreation with verified volume or is visibly disposable; deleting model caches never loses journals/grain state/credentials |

#### V2-DEPLOY-001 — Production topology preview and drift gate

| Field | Required content |
|---|---|
| ID | V2-DEPLOY-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | A normalized AppHost/Pulumi topology model and read-only preview/policy gate reject every unexplained production drift |
| Evidence/rationale | Production differs from AppHost, keeps shared/model keys with identity, omits dedicated MCP/embedding/voice, and has imperative pieces **[R]** |
| Scope | deploy/Program.cs topology DTO/extraction; AppHost snapshot normalizer; Pulumi preview/policy CI; approved target-only differences |
| Dependencies | V2-TOPOLOGY-001, V2-ASPIRE-001; ADR-012 |
| Technical approach | Produce secret-free canonical graphs, compare resources/references/ingress/replicas/storage/identity/telemetry/capabilities, and require approved rationale for target-only nodes |
| Data migration | None; preview is non-mutating and this card performs no key/storage replacement |
| Compatibility | Preserve Pulumi logical names/parents/URNs and current endpoints while decomposing pure topology code |
| Tests | Snapshot determinism; Pulumi unit/policy/preview; expected target-only allowlist; internal-key parity; resource-replacement rejection |
| Observability | Preview artifact/diff count/policy outcome and deployment graph version; no secret values |
| Rollout | Non-blocking CI preview, ratify baseline, then require no-unexplained-drift on deployment PRs |
| Rollback | Make gate temporarily advisory with owner/deadline; do not apply an unexplained preview |
| Blast radius | Deployment review/CI, not running infrastructure |
| Risk | High |
| Acceptance criteria | Preview is deterministic and secret-free; no unexplained drift or unintended resource replacement; current Telegram/Kernel internal-key mismatch is detected |

#### V2-DEPLOY-IDENTITY-001 — Managed-identity shadow and staged key removal

| Field | Required content |
|---|---|
| ID | V2-DEPLOY-IDENTITY-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | Each eligible Production storage/model access path proves managed identity in shadow before its corresponding shared key is removed in a separate staged deployment |
| Evidence/rationale | Production grants identities while retaining storage/model/shared keys, leaving two authority paths **[R]** |
| Scope | Kernel/host credential selection; Pulumi identities/RBAC/secrets/network; one resource family per deployment |
| Dependencies | V2-DEPLOY-001, V2-OBS-PIPELINE-001; operator-approved identity/RBAC matrix |
| Technical approach | Record identity-vs-key path safely, grant least privilege, shadow access, switch preferred path, verify, then remove one key/local-auth capability and tighten network |
| Data migration | Secret reference deletion only after verified no-use; no domain data move |
| Compatibility | Prior compatible revision retains bounded rollback access during validation; local Development keeps explicit non-Production credentials |
| Tests | Role allow/deny, shadow parity, key-absence startup/operations, rotation, wrong identity, network restriction, prior-revision compatibility |
| Observability | Auth mechanism class, RBAC denial, fallback use, secret-reference presence policy; never key value/identity IDs in metrics |
| Rollout | One resource family: grant → shadow → prefer identity → remove key → restrict network; observe between steps |
| Rollback | Restore prior revision/path using protected deployment secret only within rollback window; otherwise forward-fix RBAC |
| Blast radius | Production access to state, journals, sync, models, and credential infrastructure by selected family |
| Risk | Critical |
| Acceptance criteria | Selected workload succeeds with key absent; fallback-use telemetry is zero for window; least-privilege denial tests pass before key removal |

#### V2-DEPLOY-RECOVERY-001 — Production backup, replica, traffic, and rollback proof

| Field | Required content |
|---|---|
| ID | V2-DEPLOY-RECOVERY-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | Backup/restore, readiness traffic gates, immutable digests/revisions, replica drain/loss, and prior-revision rollback are rehearsed against Production-shaped infrastructure |
| Evidence/rationale | Live inspection did not expose backup status or exercise replica loss, and current topology has separate durability assumptions **[L][R]** |
| Scope | Storage backup/restore runbooks/jobs; ACA revision/traffic/probes; image digest policy; replica failover test; release smoke/rollback |
| Dependencies | V2-DEPLOY-001, V2-OBS-PIPELINE-001; ratified RPO/RTO and storage ownership |
| Technical approach | Restore isolated copy, verify manifests/checksums/credentials/projections, deploy immutable candidate, gate traffic on readiness/smoke, kill/drain replica, and shift back to compatible prior revision |
| Data migration | Restore rehearsal uses isolated targets; live schema remains expand/migrate/contract and readable by rollback revision |
| Compatibility | Retain prior image/revision and V2 readers/upcasters; external effects are never undone by infrastructure rollback |
| Tests | Backup corruption/missing key, restore hash/read tests, probe dependency loss, replica drain/kill, traffic shift, prior-version read, rollback smoke |
| Observability | Backup age/result, restore duration/hash, image digest/revision/replica/probe/traffic, rollback marker |
| Rollout | Test environment, Production isolated restore, no-traffic candidate, canary traffic, then full release |
| Rollback | Shift traffic to prior compatible revision; restore storage only for proven loss and from verified backup; compensate/forward-fix external effects |
| Blast radius | Entire Production availability and recoverability |
| Risk | Critical |
| Acceptance criteria | Restore meets ratified RPO/RTO with readable journals/credentials/projections; replica loss recovers; prior-revision rollback smoke passes without schema reversal |

#### V2-OBS-001 — Telemetry schema, propagation, redaction, and cardinality

| Field | Required content |
|---|---|
| ID | V2-OBS-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | One semantic telemetry schema defines trace/correlation propagation, protected fields, redaction, bounded metric dimensions, and invariant tests across V2 boundaries |
| Evidence/rationale | MCP/Flutter absent from structured telemetry; Ino source unregistered; proxy can silently drop and disables TLS validation **[R][L]** |
| Scope | Telemetry contract/helper library; activity sources/meters/log event IDs; propagation middleware; redaction/cardinality invariant tests |
| Dependencies | Stable envelope/workflow/effect/projection identifier shapes; V2-SAFETY-001 |
| Technical approach | Define span/link/event names, protected ID placement, low-cardinality Views, source redaction, baggage/trace-context rules, and schema version |
| Data migration | Dashboard/alert configuration only; no domain data |
| Compatibility | Existing DigitalBrain.Neuron source retained; add links/attributes without renaming active metrics until migration |
| Tests | Reference-operation trace continuity; redaction; forbidden metric labels/exemplars; cardinality overflow; schema compatibility |
| Observability | Schema self-version and rejected attribute/label counts by bounded rule |
| Rollout | Additive helpers and one reference operation, then component adoption |
| Rollback | Disable individual enrichment/source; retain safe existing telemetry and stable published names |
| Blast radius | Instrumentation overhead and protected diagnostic data |
| Risk | Medium |
| Acceptance criteria | Reference operation crosses ingress/grain/effect with correlation; canary absent; tenant/workspace/command/workflow/tool invocation IDs cannot become metric labels |

#### V2-OBS-PIPELINE-001 — Supported telemetry collection and drop accounting

| Field | Required content |
|---|---|
| ID | V2-OBS-PIPELINE-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | Every network host, including MCP, exports through a supported authenticated collector path with explicit enqueue/export/retry/overflow/drop accounting |
| Evidence/rationale | MCP/Flutter are absent from sampled structured telemetry and the browser proxy can disable TLS validation and acknowledge dropped telemetry **[R][L]** |
| Scope | ServiceDefaults and MCP host; collector/ACA OTel configuration; browser diagnostics boundary; removal/replacement of OTLP proxy; pipeline tests |
| Dependencies | V2-OBS-001, V2-ASPIRE-001, V2-ASPIRE-HEALTH-001 |
| Technical approach | Adopt common host instrumentation, authenticated TLS collector/agent path, bounded queues, health/capability status, and drop/failure counters |
| Data migration | None; telemetry backend/retention configuration only |
| Compatibility | Existing safe source names and App Insights correlation remain during dual export; browser gets typed unavailable/error response |
| Tests | MCP trace/health, TLS/certificate failure, collector outage/backpressure/recovery, queue overflow/drop signal, no-domain-blocking behavior |
| Observability | Pipeline queue/export/retry/drop/last-success and collector health are this card's outputs |
| Rollout | Test collector, dual export shadow, compare, cut over, then remove insecure/fail-open path |
| Rollback | Return to safe existing exporter with reduced capability; never restore TLS bypass or silent success on drop |
| Blast radius | Telemetry availability and modest service performance overhead |
| Risk | High |
| Acceptance criteria | MCP participates in a correlated trace; exporter outage increments visible failure/drop signals; no TLS bypass or silent telemetry loss path remains |

#### V2-OBS-OPS-001 — Dashboards, SLOs, alerts, and incident queries

| Field | Required content |
|---|---|
| ID | V2-OBS-OPS-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | Versioned dashboards, provisional-to-ratified SLOs, alerts, and runbook queries cover ingress, durability, workflow, projections, tools/models/connectors, feed, cluster, deployment, and telemetry pipeline |
| Evidence/rationale | Current live evidence is a narrow log/trace sample with no metrics query, alert, backup, or failure exercise **[L]** |
| Scope | Dashboard/alert definitions; SLO configuration; synthetic incident fixtures; operator runbooks and protected trace-query links |
| Dependencies | V2-OBS-001, V2-OBS-PIPELINE-001; each component emits section-14 signals |
| Technical approach | Build low-cardinality queries, notify-only thresholds, synthetic lag/unknown/drop/replica incidents, ratification workflow, then release gates |
| Data migration | Dashboard/alert configuration only; no domain data |
| Compatibility | Existing dashboards remain until parity; links use protected logs/spans for high-cardinality drill-down |
| Tests | Dashboard query fixtures, missing-data behavior, alert fire/recover/dedup, forbidden-label lint, synthetic incident/runbook validation |
| Observability | Alert evaluation/delivery/drop, SLO burn, dashboard freshness, runbook exercise result |
| Rollout | Shadow dashboards, notify-only alerts, operator ratification, then selected release/paging gates |
| Rollback | Disable noisy alert/gate with owner/deadline; retain data and dashboard for diagnosis |
| Blast radius | Operator response and release gating, not domain execution |
| Risk | Medium |
| Acceptance criteria | Every required section-14 signal has a bounded query/owner; synthetic incidents fire and recover expected alerts; ratified SLOs link to runbooks |

#### V2-TEST-001 — Three-silo and container-backed failure lane

| Field | Required content |
|---|---|
| ID | V2-TEST-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | A container-backed Aspire lane runs three silos and deterministic workflow/outbox/projection crash/failover windows with correlated evidence |
| Evidence/rationale | CI omits Flutter tests/Pulumi preview and current cross-silo tests do not kill/restart a three-silo owner **[R]** |
| Scope | TestKit fault controls; AppHost Test profile; three Kernel processes; container storage; workflow/outbox/projection test adapters and reports |
| Dependencies | V2-OUTBOX-001, V2-PROJECTION-001, V2-ASPIRE-001, V2-ASPIRE-HEALTH-001, V2-ASPIRE-STORAGE-001, V2-OBS-001, V2-OBS-PIPELINE-001 |
| Technical approach | Deterministic barriers/failpoints around enrollment/commit/lease/dispatch/provider/result/checkpoint; kill/deactivate one owner/silo and verify scanner/lease/checkpoint recovery |
| Data migration | Synthetic fixtures and disposable volumes only |
| Compatibility | Existing fast/unit workflow retained; distributed lane is additive and uses fake/sandbox external effects |
| Tests | Three-silo owner kill at each crash window; directory scanner restart; missed reminder; lease takeover; duplicate delivery; projection checkpoint crash; storage outage/recovery |
| Observability | Test duration/flakiness/failpoint stage, protected trace bundle, outbox/projection recovery assertions; no live credentials |
| Rollout | Developer opt-in, scheduled CI, stabilize, then required for durability PRs |
| Rollback | Temporarily non-block flaky infrastructure lane with owner/deadline; never delete correctness tests |
| Blast radius | CI resource/time and distributed durability confidence |
| Risk | High |
| Acceptance criteria | Killing any one of three silos at every defined window yields one classified terminal/pending/unknown state, no lost commit, bounded duplicate handling, and correlated trace evidence |

#### V2-TEST-RELEASE-001 — Release assurance and production smoke orchestration

| Field | Required content |
|---|---|
| ID | V2-TEST-RELEASE-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | CI/release orchestration runs Flutter, distributed failure, topology preview, telemetry assertions, backup/rollback proof, and credential-free Production smoke gates by risk |
| Evidence/rationale | Current CI omits Flutter tests/Pulumi preview and has no integrated topology/telemetry/recovery release gate **[R]** |
| Scope | CI/deploy workflows; test selection/policies; artifact redaction; Production smoke identity; release report |
| Dependencies | V2-TEST-001, V2-OBS-OPS-001, V2-DEPLOY-001, V2-DEPLOY-RECOVERY-001, V2-FLUTTER-HOST-001 |
| Technical approach | Compose existing fast lanes and new risk-specific gates, run deliberate control faults, publish secret-free evidence, and define which failure blocks PR vs release |
| Data migration | Synthetic/disposable fixtures; Production smoke uses isolated non-destructive records and cleanup/retention policy |
| Compatibility | Existing root dotnet lane remains; new gates become required only after stability thresholds and owner assignment |
| Tests | Gate self-test deliberately fails each lane; Flutter tests; topology unexpected-diff; telemetry canary/drop; backup/rollback report; read-only/low-risk Production smoke |
| Observability | Lane duration/flakiness/queue/failure stage, artifact completeness, release version, smoke/rollback outcome |
| Rollout | Scheduled/non-blocking, measure flakiness, required on affected PRs, then release gate |
| Rollback | Temporarily make flaky infrastructure gate non-blocking with owner/deadline and retained correctness run; never delete correctness tests |
| Blast radius | CI throughput and release availability |
| Risk | Medium |
| Acceptance criteria | A deliberate fault fails each intended gate; all reports are secret-free; successful release has traceable topology, test, restore, telemetry, smoke, and rollback evidence |

#### V2-LEGACY-001 — Legacy retirement manifest and gate automation

| Field | Required content |
|---|---|
| ID | V2-LEGACY-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | A shared retirement manifest, zero-use telemetry, compatibility-window approval, and disable-observe-remove gate exist before any V1 implementation is deleted |
| Evidence/rationale | Compatibility layers are required for migration but permanent dual paths recreate divergence **[I]** |
| Scope | Retirement manifest/schema; usage/fallback telemetry contract; compatibility approvals; release-gate automation/template |
| Dependencies | V2-OBS-OPS-001, V2-TEST-RELEASE-001; product/operator compatibility-window policy |
| Technical approach | Require per-path owner, replacement, usage query, data validation, disable interval, rollback artifact, reader-retention rule, and removal release marker |
| Data migration | None; this card records evidence requirements and does not remove or backfill data |
| Compatibility | Published aliases/upcasters/readers are classified separately from writable/routable legacy paths and cannot be silently scheduled for deletion |
| Tests | Manifest schema/lint, missing evidence fails gate, false zero-use data, expired approval, rollback artifact presence, alias/member-ID protection |
| Observability | Manifest status, legacy-use query freshness, gate pass/fail by bounded path class |
| Rollout | Advisory report, ratify owners/windows, then required on every legacy-removal PR |
| Rollback | Make automation advisory for a tooling defect with owner/deadline; no legacy path is deleted by automation itself |
| Blast radius | Release governance only |
| Risk | Medium |
| Acceptance criteria | No removal can pass without path-specific zero use, migration validation, disable-observe evidence, compatible readers, owner approval, and rollback artifact |

#### V2-LEGACY-MCP-001 — Retire direct-grain MCP mutation

| Field | Required content |
|---|---|
| ID | V2-LEGACY-MCP-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | Direct IGrainFactory/generic-Synapse MCP mutation and caller-supplied decision authority are removed after application command/approval ports reach parity |
| Evidence/rationale | DigitalBrainMutationTools currently manufactures Synapses and accepts decided_by **[R]** |
| Scope | DigitalBrainMutationTools/ToolsBase registration; direct resolver usage; MCP compatibility responses and docs |
| Dependencies | V2-LEGACY-001, V2-MCP-COMMAND-001 |
| Technical approach | Prove port path usage/parity, disable old tool registrations, observe, remove direct mutation code, retain explicit unsupported-version response |
| Data migration | Pending V2 operations remain in workflow; no journal rewrite |
| Compatibility | Read tools and supported command schemas remain; old mutation clients receive typed unsupported/upgrade response |
| Tests | No direct mutation registration/reference; old-client response; command idempotency/status; approval identity; mixed client versions |
| Observability | Legacy MCP tool discovery/invoke zero-use and unsupported-client count |
| Rollout | Warn/discovery hide → disable invocation → observe → remove one tool family per PR |
| Rollback | Re-enable prior compatibility binary only while auth/policy remains enforced; pending operations are unaffected |
| Blast radius | External MCP automation mutations |
| Risk | High |
| Acceptance criteria | No MCP mutation code resolves grains or creates Synapse; zero old-tool use over window; supported port contract remains green |

#### V2-LEGACY-OAUTH-001 — Retire duplicate connector OAuth ownership

| Field | Required content |
|---|---|
| ID | V2-LEGACY-OAUTH-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | Duplicate Google/Salesforce AuthNeuron/Connector OAuth start/callback/refresh ownership is removed after authoritative coordinator cutover |
| Evidence/rationale | Providers have duplicated OAuth paths and incompatible state/PKCE behavior **[R]** |
| Scope | Google/Salesforce AuthNeuron and connector OAuth methods/endpoints; old state routing; credential compatibility adapter |
| Dependencies | V2-LEGACY-001, V2-OAUTH-GOOGLE-001, V2-OAUTH-SALESFORCE-001 |
| Technical approach | Validate every stored credential/flow owner, stop old starts, route callbacks by version, observe, then remove one provider's duplicate owner per PR |
| Data migration | Preserve existing credential ciphertext/references and grant metadata; unresolved flows expire/restart, never guess verifier/state |
| Compatibility | Rollback binary/adapter can read old credential records; existing valid credentials need no forced re-consent unless grants require it |
| Tests | Old/new callback versions, stored credential read/refresh/revoke, expired in-flight flow, provider sandbox, rollback reader |
| Observability | Legacy OAuth start/callback/refresh zero-use, credential validation, re-consent, callback mismatch |
| Rollout | Stop old starts → drain/expire flows → disable callback route → one-provider removal |
| Rollback | Re-enable old read/callback adapter while records remain readable; never restore insecure state/no-PKCE start |
| Blast radius | Connector authorization and existing credentials |
| Risk | Critical |
| Acceptance criteria | All active credentials validate through authoritative owner; no live old flow remains; removed provider path has zero use and rollback-read proof |

#### V2-LEGACY-UI-001 — Retire shared private feed and V1 private delivery

| Field | Required content |
|---|---|
| ID | V2-LEGACY-UI-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | Shared/unaddressed private publication and V1 private delivery are removed after supported clients use the isolated resumable feed |
| Evidence/rationale | Current HomeFeedBus subscribes a shared stream and cannot prove audience isolation/resume **[R]** |
| Scope | HomeFeedBus shared path; WatchHomeFeed V1 private routing; client negotiation/compatibility policy |
| Dependencies | V2-LEGACY-001, V2-FEED-DELIVERY-001, V2-FLUTTER-HOST-001 |
| Technical approach | Classify public vs private, measure V1 private use, stop new private V1 publish, return upgrade response to unsupported clients, observe, remove shared path |
| Data migration | V2 feed rows/cursors remain; no private V2 row is copied to shared V1. Public surfaces get explicit AudienceKind |
| Compatibility | V1 may remain for explicit public/non-sensitive surfaces through stated SLA; private old clients receive upgrade path |
| Tests | Zero cross-workspace/shared publish; unsupported-client response; explicit public audience; rollback negotiation; V2 reconnect |
| Observability | V1 private/public publish/subscription and unsupported-client counts by bounded version/audience kind |
| Rollout | Warn clients → stop private V1 writes → disable private reads → observe → remove shared private path |
| Rollback | Re-enable only addressed V1 compatibility for supported clients; never republish private data to shared stream |
| Blast radius | Legacy Flutter/client feed delivery |
| Risk | Critical |
| Acceptance criteria | Zero measured private V1/shared-feed use; private V2 isolation remains; public audience is explicit; old-client response is deterministic |

#### V2-LEGACY-MODEL-001 — Retire duplicate model construction and configuration

| Field | Required content |
|---|---|
| ID | V2-LEGACY-MODEL-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | Old provider constructors, role precedence, and legacy configuration keys are removed after all model calls traverse the policy router/factory |
| Evidence/rationale | Provider construction/configuration is duplicated across DigitalBrainChat, registration, and ScopedChatClientFactory **[R]** |
| Scope | DigitalBrainChat; DigitalBrainChatClientRegistration; ScopedChatClientFactory; legacy options/env keys; AppHost model config |
| Dependencies | V2-LEGACY-001, V2-MODEL-002 |
| Technical approach | Static/runtime bypass inventory, shadow policy matching current precedence, stop legacy config writes, observe fallback use, then remove one constructor/key family per PR |
| Data migration | Translate registry/settings to canonical snapshot; retain read-old mapping through rollback window |
| Compatibility | Compatibility policy reproduces current provider/model precedence when no explicit V2 tenant policy exists |
| Tests | No bypass construction; every call has decision; legacy config translation; fallback/privacy/budget; mixed-version startup |
| Observability | Legacy factory/config fallback zero-use and routing decision reason/version |
| Rollout | Shadow → preferred router → stop old config writes → disable fallback → remove family |
| Rollback | Re-enable compatibility policy/key reader without restoring duplicate writers |
| Blast radius | All LLM/embedding/voice selection and startup |
| Risk | High |
| Acceptance criteria | Static analysis and runtime telemetry find no model-call bypass; legacy fallback is zero; compatibility policy passes characterized routing |

#### V2-LEGACY-SYNAPSE-001 — Stop V1 domain writes while preserving history

| Field | Required content |
|---|---|
| ID | V2-LEGACY-SYNAPSE-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | V1 Synapse domain writes stop only after every writer migrates; aliases, member IDs, upcasters, readers, and historical fixtures remain |
| Evidence/rationale | Synapse conflates command/event and JournalJson uses CLR full names, making premature deletion capable of orphaning history **[R]** |
| Scope | V1 writer inventory/registrations/adapters; envelope dispatch; journal readers/upcasters; compatibility corpus |
| Dependencies | V2-LEGACY-001, V2-ENVELOPE-001, V2-JOURNAL-001, V2-OUTBOX-001, V2-INBOX-001, V2-PROJECTION-002 |
| Technical approach | Migrate one writer family, compare dual projections, stop its V1 writes, observe, then remove write-only code; permanently retain required historical identities/readers |
| Data migration | Backfill projections only; never rewrite source journals during retirement. Unknown records remain quarantined/operator-visible |
| Compatibility | Mixed rolling versions and old journal/state reads pass; V1 client adapter may synthesize responses from V2 facts |
| Tests | Writer inventory lint, zero V1 writes, historical corpus, mixed-version rolling read/write, upcaster quarantine, alias/member-ID reuse prohibition |
| Observability | V1 write/read/upcast/quarantine count by stable type family and release marker |
| Rollout | Per writer family: dual project → stop write → observe → remove writer code |
| Rollback | Re-enable adapter writer only if it cannot duplicate effects and schemas remain compatible; otherwise forward-fix while readers stay |
| Blast radius | Repository-wide event handling and historical journals |
| Risk | Critical |
| Acceptance criteria | All inventoried writers use V2; historical corpus and mixed versions read; no alias/member ID/type mapping is deleted or reused |

#### V2-LEGACY-KEYS-001 — Retire legacy command routing without duplicate grains

| Field | Required content |
|---|---|
| ID | V2-LEGACY-KEYS-001 |
| Priority | P2 |
| Classification | Architectural necessity |
| Outcome | Legacy command routing ends only after every owner has an explicit cutover ledger and zero ambiguous mapping; old keys remain read-resolvable as required |
| Evidence/rationale | Orleans grain identity is type plus key, so an unsafe re-key creates a second logical actor **[D][R]** |
| Scope | GrainAddressResolver/LegacyGrainKeyMap; warmup/global key calls; Ino/session/automation/connector command routing; cutover ledger |
| Dependencies | V2-LEGACY-001, V2-KEYS-001, V2-IDENTITY-002, V2-INO-KEY-001 |
| Technical approach | Inventory each legacy owner, freeze old writes, verify state/projection hash and no active lease, atomically mark command owner, observe, then remove command fallback per grain family |
| Data migration | State remains at old key or follows an explicit export/import protocol; mapping is permanent/audited and never inferred from string shape |
| Compatibility | Old key/state stays read-only through resolver for stated retention; old clients route through scoped adapter until window ends |
| Tests | Concurrent cutover, stale activation/lease, ambiguous/missing map, mixed versions, rollback owner, no duplicate logical mutation |
| Observability | Legacy route/read, ambiguous map, duplicate-owner guard, cutover status by bounded grain family |
| Rollout | One low-risk grain family; freeze/verify/flip/observe; global/main owners last |
| Rollback | Flip command owner back only before V2 mutations or with proved reconciliation; retain both state copies and mapping for forward repair |
| Blast radius | Grain identity, activation placement, and all journeys owned by migrated family |
| Risk | Critical |
| Acceptance criteria | No family has two writable logical owners; every cutover has verified ledger/hash/rollback boundary; legacy command fallback use is zero |

### P3 — optional product or developer-experience improvements

#### V2-SEARCH-SPIKE-001 — Projection/search/vector backend benchmark

| Field | Required content |
|---|---|
| ID | V2-SEARCH-SPIKE-001 |
| Priority | P3 |
| Classification | Optional improvement |
| Outcome | Reproducible benchmark and ADR compare current Azure/SQLite/vector ports with candidate scalable stores |
| Evidence/rationale | No measured query volume/retention/tenancy evidence justifies a new database yet **[R][I]** |
| Scope | Disposable benchmark harness, synthetic scoped corpus, cost/ops/security comparison |
| Dependencies | V2-PROJECTION-001 query contracts |
| Technical approach | Measure ingest/replay/query/lag/restore/isolation and operational burden; no production integration |
| Data migration | Synthetic only |
| Compatibility | Port contract is the benchmark interface |
| Tests | Correctness/isolation before performance; repeatability |
| Observability | Benchmark throughput/latency/storage/cost and failure recovery |
| Rollout | None; ADR result only |
| Rollback | Delete disposable environment/harness if not retained |
| Blast radius | None to runtime |
| Risk | Low |
| Acceptance criteria | Decision has measured thresholds, total-cost and migration/rollback analysis; “stay current” is a valid result |

#### V2-AGENT-SPIKE-001 — Agent Framework sessions and MCP Tasks adapter

| Field | Required content |
|---|---|
| ID | V2-AGENT-SPIKE-001 |
| Priority | P3 |
| Classification | Optional improvement |
| Outcome | Spike proves or rejects adapting Agent Framework sessions/MCP experimental Tasks over DigitalBrain-owned workflows |
| Evidence/rationale | Current ChatClientAgent uses no session; Agent Framework and MCP Tasks are fast-moving/preview and must not own V2 durability **[R][D]** |
| Scope | Isolated adapter/spike project; no production routing |
| Dependencies | V2-WORKFLOW-001, V2-TOOLS-002, V2-MODEL-002 |
| Technical approach | Map identity, operation status, checkpoint/replay/cancel/TTL without duplicating source of truth |
| Data migration | None |
| Compatibility | DigitalBrain workflow remains authoritative |
| Tests | Crash/replay, identity binding, cancellation, version negotiation, duplicate status |
| Observability | Adapter overhead/state divergence |
| Rollout | Development experiment only behind compile/profile flag |
| Rollback | Remove adapter; no durable state ownership |
| Blast radius | None unless later promoted |
| Risk | Low |
| Acceptance criteria | ADR documents coexistence and shows zero duplicate effects/state ownership, or explicitly rejects adoption |

#### V2-RFW-DX-001 — Generated registry metadata and protocol fixture tooling

| Field | Required content |
|---|---|
| ID | V2-RFW-DX-001 |
| Priority | P3 |
| Classification | Optional improvement |
| Outcome | Generate widget metadata/schema/fixture indexes from the modular Flutter registry to reduce manual drift |
| Evidence/rationale | Large manual registry and cross-language protocol make changes expensive **[R]** |
| Scope | Flutter dev tooling and test fixtures only; no generated runtime files committed unless policy chooses |
| Dependencies | V2-FLUTTER-001, V2-UI-001 |
| Technical approach | Source annotations/registry introspection → JSON schema/capability manifest/golden fixture list |
| Data migration | None |
| Compatibility | Stable widget names and SurfaceEnvelope schemas remain authoritative |
| Tests | Deterministic generation, duplicate names, schema/golden parity |
| Observability | CI drift report |
| Rollout | Non-blocking report, then generated artifact verification |
| Rollback | Return to manual metadata; runtime unaffected |
| Blast radius | Developer workflow |
| Risk | Low |
| Acceptance criteria | Registry/schema drift is detected automatically and output is deterministic/secret-free |

## 17. Milestones and critical path

### Dependency graph

~~~mermaid
flowchart TD
    M1[M1 Characterization and safety]
    M2[M2 Identity and workspace]
    M3[M3 Envelopes and compatibility]
    M4[M4 Workflow plus outbox/inbox]
    M5[M5 Projections and causal indexes]
    M6[M6 Ino, tools, and model routing]
    M7[M7 Connectors and MCP]
    M8[M8 UI protocol and feed]
    M9[M9 Topology, observability, production assurance]
    M10[M10 Legacy removal]

    M1 --> M2 --> M3 --> M4 --> M5 --> M6 --> M7 --> M8 --> M9 --> M10
    M4 --> P5[Projection runtime can start] --> M5
    M4 --> O7[OAuth coordinator can start] --> M7
    M3 --> U8[Surface protocol can start] --> M8
    M4 --> A8[Authorized action service] --> M8
    M1 --> T9[Profile/topology and test harness] --> M9
    M3 --> R6[Model factory consolidation] --> M6
    M4 --> X7[MCP command/approval] --> M7

    classDef critical fill:#5b2333,color:#fff,stroke:#ffb3c6,stroke-width:2px;
    class M1,M2,M3,M4,M5,M6,M7,M8,M9,M10 critical;
~~~

The horizontal chain is the release critical path. The side branches are deliberately parallelizable but merge only after their required security/durability gate.

### Milestone 1 — Characterization and safety guardrails

- **Objective:** stop new secret leakage, close anonymous dangerous surfaces, and make topology drift visible without changing grain ownership.
- **Included IDs:** V2-SAFETY-001, V2-SAFETY-INGRESS-001, V2-SAFETY-EGRESS-001, V2-TEST-CHAR-001, V2-MCP-001, V2-TOPOLOGY-001.
- **Prerequisites:** approve ADR-003/013/014 enough to define prohibited data and HTTP exposure.
- **Parallel workstreams:** fixture manifest/taxonomy; ingress secret conversion; egress redaction; MCP profile gate; topology snapshot; crash-failpoint test design.
- **Release gate:** all secret canaries absent from durable/egress artifacts; Production HTTP mutation MCP absent; topology snapshots contain no secrets.
- **Acceptance criteria:** current user journeys remain green; unsafe admin/config/bootstrap behavior is captured by failing-before/fixed-after tests.
- **Rollback point:** flags restore specific V1 adapters only in trusted Development; never restore unredacted or anonymous remote mutation.
- **Risks:** false-positive redaction, local-agent workflow disruption, incomplete secret inventory.

### Milestone 2 — Identity, workspace isolation, and secret-safe authority

- **Objective:** authenticate every boundary and make personal/default tenant/workspace an enforceable resource boundary.
- **Included IDs:** V2-AUTH-001, V2-AUTH-BOOTSTRAP-001, V2-IDENTITY-001, V2-IDENTITY-AUTHZ-001, V2-ISOLATION-GATE-001, V2-KEYS-001, V2-IDENTITY-002.
- **Prerequisites:** ADR-001, ADR-002, ADR-008; operator identity/bootstrap choice.
- **Parallel workstreams:** auth/session; bootstrap/admin; identity contracts; membership/resource enforcement; capability isolation ledger/gate; key resolver; dry-run migration.
- **Release gate:** two-user/two-workspace ingress/query/config policy passes; every not-yet-isolated global capability is unavailable for a second principal/workspace; no grain state is re-keyed yet.
- **Acceptance criteria:** payload/clientId cannot override authority; app config is admin-only; ownership is mapped/quarantined; only proved isolated or sole-owner personal/default capabilities can be active.
- **Rollback point:** disable the affected capability or retain authenticated personal/default-only access for a proven sole owner; authorization never returns to audit-only and direct legacy lookup never becomes a workspace bypass. Mappings are additive.
- **Risks:** locked-out local user, ambiguous legacy ownership, accidental duplicate grain activation.

### Milestone 3 — Versioned envelopes and compatibility adapters

- **Objective:** establish durable schema identity and separate intent/fact/effect without breaking old journals or clients.
- **Included IDs:** V2-ENVELOPE-001, V2-JOURNAL-001.
- **Prerequisites:** ADR-003, ADR-004, ADR-005; RequestContext contract.
- **Parallel workstreams:** envelope validation/aliases; fixture corpus/upcasters; V1 adapters.
- **Release gate:** historical corpus decodes deterministically; aliases/member IDs unique; shadow V1/V2 adapter parity.
- **Acceptance criteria:** new additive contracts publish; current dispatch remains; unknown record quarantines rather than silently disappears.
- **Rollback point:** prefer current reader/write path while retaining published aliases/upcasters.
- **Risks:** incomplete fixture coverage, alpha journaling format change, integration assembly missing during replay.

### Milestone 4 — Durable workflow plus outbox/inbox

- **Objective:** remove the decision/apply crash gap and make duplicate/retry/unknown outcomes explicit.
- **Included IDs:** V2-WORKFLOW-001, V2-OUTBOX-001, V2-INBOX-001.
- **Prerequisites:** ADR-005/006; V2 envelopes and authenticated approver.
- **Parallel workstreams:** pure workflow state machine; storage atomicity and owner-directory proof; recovery dispatcher; receiver inbox/effect adapter.
- **Release gate:** failpoints cover sequenced enrollment/high-water scans, atomic approval→queue, immutable commit/transition/index reconstruction, commit-before-hint, scanner restart, dispatch/provider/result/compensation, and leases on three silos or the closest available lane.
- **Acceptance criteria:** approved work always has terminal/queued/unknown/manual state; every committed V2 owner/effect is rediscovered without a wake notification; duplicate EffectId mutates once; no ambiguous effect is blind-retried.
- **Rollback point:** stop accepting new V2 workflow submissions and drain/preserve pending records; V1 remains for unmigrated operations.
- **Risks:** incorrect atomicity assumption, lease takeover duplicate, provider cannot verify outcome.

### Milestone 5 — Query projections and causal indexes

- **Objective:** serve scoped timeline, lineage, workflow, and operation queries without arbitrary journal scans.
- **Included IDs:** V2-PROJECTION-001, V2-PROJECTION-002.
- **Prerequisites:** stable reader/upcasters, identity ownership mapping, capability-isolation gate, and durable directory/commit cursor contracts from V2-OUTBOX-001.
- **Parallel workstreams:** runtime/checkpoints; schema/query ports; legacy backfill/parity.
- **Release gate:** directory new-owner/commit-without-notification, per-owner checkpoint crash, full replay/rebuild/live-tail, parity, and isolation tests pass deterministically.
- **Acceptance criteria:** cross-neuron causal chain works; scoped opaque cursors cannot cross workspaces; lag/quarantine visible.
- **Rollback point:** atomic read alias to prior projection or redacted V1 fallback; journals unchanged.
- **Risks:** ambiguous legacy scope, projection lag, poison record blocks authoritative read.

### Milestone 6 — Ino/tool decomposition and policy model routing

- **Objective:** make Ino workspace-safe and delegate context, planning, tool execution, model policy, response, and surfaces behind ports.
- **Included IDs:** V2-INO-KEY-001, V2-TOOLS-001, V2-TOOLS-002, V2-INO-CONTEXT-001, V2-INO-PLAN-001, V2-INO-RESPONSE-001, V2-MODEL-001, V2-MODEL-LOCAL-001, V2-MODEL-OPENAI-001, V2-MODEL-ANTHROPIC-001, V2-MODEL-XAI-001, V2-MODEL-FACADE-001, V2-MODEL-FACADE-SCOPED-001, V2-MODEL-FACADE-DEFAULT-001, V2-MODEL-002.
- **Prerequisites:** scoped identity, enforced capability-isolation gate, projections, durable invocation, and a surface-composition port whose V1 output is addressed/sole-owner-only.
- **Parallel workstreams:** context/memory; tool catalog/coordinator; model factory/router; planner/composer extraction.
- **Release gate:** context/tool/model isolation, budget/privacy/fallback, raw-result redaction, cancellation/restart, and characterization goldens pass.
- **Acceptance criteria:** InoNeuron is a compatibility/state coordinator rather than provider/tool/UI god module; every model/tool call has policy/operation evidence.
- **Rollback point:** per-stage/family feature flags return to characterized behavior, never to cross-workspace context.
- **Risks:** response quality drift, model tool-call differences, latent special routes, conversation migration mismatch.

### Milestone 7 — Connector and MCP boundary migration

- **Objective:** make OAuth and external-agent access use authenticated application/query ports with durable operations.
- **Included IDs:** V2-OAUTH-001, V2-OAUTH-GOOGLE-001, V2-OAUTH-SALESFORCE-001, V2-MCP-QUERY-001, V2-MCP-COMMAND-001.
- **Prerequisites:** identity/resource authorization, capability-isolation gate, workflows, tools, projections, CredentialRef decision.
- **Parallel workstreams:** OAuth core/provider adapters; MCP read port; MCP command/approval after workflow.
- **Release gate:** endpoint callback PKCE/state/rotation/revoke suites; MCP audience/Origin/scope/idempotency/redaction/rate suites.
- **Acceptance criteria:** one OAuth owner per provider; Gmail scopes match capability; Salesforce verifier reaches token call; MCP code has no direct grain mutation.
- **Rollback point:** retained old encrypted credentials/read-only adapters; disable MCP tool class; pending operations stay queryable.
- **Risks:** credential loss, provider sandbox mismatch, client re-consent, agent contract break.

### Milestone 8 — UI protocol and feed migration

- **Objective:** version surfaces, authorize actions, and deliver private resumable feeds without a Flutter rewrite.
- **Included IDs:** V2-UI-001, V2-UI-ACTION-001, V2-FEED-001, V2-FEED-DELIVERY-001, V2-FLUTTER-HOST-001.
- **Optional non-gating companion:** V2-FLUTTER-001 may reduce registry blast radius but is not required to release the V2 protocol/feed.
- **Prerequisites:** identity/resource authorization, enforced capability-isolation gate, projection runtime, stable action/command port.
- **Parallel workstreams:** surface protocol/V1 adapter; action service; feed projection; feed transport; Flutter registry; Flutter host/controllers.
- **Release gate:** V1/V2 goldens, old-client negotiation, action forgery/replay, reconnect/backpressure, and cross-workspace feed tests.
- **Acceptance criteria:** client cannot choose a command/grain; private surfaces never use shared null audience; disconnect resumes by sequence.
- **Rollback point:** keep V2 rows; addressed V1 is allowed only for gate-approved sole-owner personal/default, otherwise disable private feed. Never use the current shared V1 stream.
- **Risks:** client/server version skew, cache corruption, feed storage growth, UI golden drift.

### Milestone 9 — Topology, observability, and production convergence

- **Objective:** prove Development/Test/Production profiles, recoverability, telemetry, replica behavior, managed identity, and deployment rollback.
- **Included IDs:** V2-ASPIRE-001, V2-ASPIRE-HEALTH-001, V2-ASPIRE-STORAGE-001, V2-OBS-001, V2-OBS-PIPELINE-001, V2-OBS-OPS-001, V2-DEPLOY-001, V2-DEPLOY-IDENTITY-001, V2-DEPLOY-RECOVERY-001, V2-TEST-001, V2-TEST-RELEASE-001; completion of V2-TOPOLOGY-001.
- **Prerequisites:** operational contracts/states/metrics stable enough to alert.
- **Parallel workstreams:** AppHost/Test profile; telemetry schema/pipeline/operations; topology preview; identity; backup/rollback; distributed/release CI.
- **Release gate:** topology preview/diff, three-silo loss/restart, storage backup/restore, readiness/liveness, telemetry drop/redaction, canary/rollback smoke.
- **Acceptance criteria:** no unexplained topology drift; eligible keys removed after shadow validation; ratified RPO/RTO/SLO evidence; rollback-compatible prior revision.
- **Rollback point:** traffic to prior revision with V2 readers/upcasters retained; storage restored only from verified backup.
- **Risks:** local/production mismatch, hidden platform limits, missing telemetry during failure, irreversible external effects.

### Milestone 10 — Removal of legacy paths

- **Objective:** contract dual paths one at a time after proven replacement and compatibility windows.
- **Included IDs:** V2-LEGACY-001, V2-LEGACY-MCP-001, V2-LEGACY-OAUTH-001, V2-LEGACY-UI-001, V2-LEGACY-MODEL-001, V2-LEGACY-SYNAPSE-001, V2-LEGACY-KEYS-001.
- **Prerequisites:** all relevant replacement milestones, zero-use telemetry, operator/product compatibility approval.
- **Parallel workstreams:** only independent paths; never remove journal/key/UI/OAuth paths in one PR.
- **Release gate:** path-specific backfill validation, old-state/client read, mixed rolling version, backup, disable-observe interval, rollback artifact.
- **Acceptance criteria:** no new V1 writes/direct grain MCP/shared private feed/duplicate OAuth/old model construction; historical aliases/upcasters remain.
- **Rollback point:** compatible binary/config can re-enable adapter without schema reversal; otherwise forward-fix.
- **Risks:** hidden client, stale credential/key, rollback version cannot read new state, code removal masks historical type.

P3 spikes run only after their prerequisite ports are stable and never gate the critical path.

## 18. Large-module decomposition plan

| Current module | Mixed responsibilities **[R]** | Destination modules/types and dependency direction **[I]** | Characterization and extraction sequence | Compatibility boundary and blast radius |
|---|---|---|---|---|
| [InoNeuron.cs](../integrations/DigitalBrain.Ino/InoNeuron.cs), 1,456 LOC | Activation/capabilities, intents, global context/memory, models, tools, automation/approval, DB/schema, conversation, UI | Conversation grain → Application request coordinator → context/memory/planner/tool/model ports → response/surface composers. Domain/Application never depends on Agent Framework/provider/UI implementation | Freeze conversation/tool/model/surface/schema tests; extract context; planner; catalog/authorization; router; invocation; response/surfaces; projection. One responsibility/family per PR | ino-main facade and current Ask/Interact/Surface adapters. Blast: all chat, connector tools, automation, model and UI journeys |
| [Neuron.cs](../src/DigitalBrain.Kernel.Abstractions/Neuron.cs), 398 LOC | Journal resolution/write, Synapse stamp/emit/deliver, stream subscription, reflection dispatch, timeline query, checkpoint/branch, tracing/metrics | Infrastructure.Orleans AggregateGrainBase; IAggregateCommitter; LegacySynapseEmitter; CausalContext; ICheckpointService; projection query ports. Domain contracts do not depend on Orleans | Freeze Fire/Deliver/broadcast/causation/checkpoint/serializer tests; add opt-in V2 base beside old; migrate one low-risk grain; compare; never edit every grain in one PR | Old Neuron remains for V1 grains; stable interfaces/aliases. Blast: every grain and historical journal |
| [DigitalBrainBuilderExtensions.cs](../src/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs) AddDigitalBrain, 386 total LOC | Profile selection, Azure/Azurite storage, Orleans, local Ollama/models/OpenWebUI, Whisper/cache, provider secret parameters, model export/context | Hosting.Profile; AddDigitalBrainStorage; AddDigitalBrainOrleans; AddDigitalBrainModelResources; AddDigitalBrainVoice; CapabilityManifest. AppHost composes these; features do not depend on hosting | Snapshot current Run/Publish graphs; extract pure option/profile validation; storage; models; voice; context facade; compare normalized graph after each | Preserve AddDigitalBrain signature/DigitalBrainContext until callers migrate. Blast: whole local/publish resource graph |
| WireKernelSilo in [DigitalBrainBuilderExtensions.cs](../src/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs) | Orleans/storage/model references, waits, endpoints, replicas, surface/model/voice env, credentials/dashboard option | WithKernelCore; WithKernelStorage; WithKernelModels; WithKernelVoice; WithKernelTelemetry; WithKernelProfile. Resource extension depends on profile/context, not domain policy | AppHost model tests for refs/waits/env/endpoints/replicas; extract one group; assert graph equivalence; then remove dead dashboard option | WireKernelSilo facade chains new extensions. Blast: all Kernel replicas/startup/config |
| [DigitalBrainOrleansExtensions.cs](../src/DigitalBrain.Kernel/Hosting/DigitalBrainOrleansExtensions.cs), 330 LOC | Silo/client/storage/journal/streams, AI clients, connector/tool DI, MCP reads, static files/endpoints | Hosting.Orleans; Hosting.Storage/Journals; Hosting.Application; Hosting.Connectors; Hosting.Protocols. Composition depends inward on ports; connector projects are adapters | Freeze service-registration and direct/Aspire host tests; extract storage/journal; app services; integrations; protocols; reject circular references | Existing UseDigitalBrainOrleans/AddDigitalBrainClients facade. Blast: Kernel boot, DI, tests and every integration |
| [deploy/Program.cs](../deploy/Program.cs), 514 LOC | Configuration, storage, AI, Log Analytics/App Insights, ACA environment/apps, identities/RBAC, ingress, secrets, outputs/tags | Deploy.Config; DataComponent; AiComponent; ObservabilityComponent; RuntimeComponent; EdgeComponent; Outputs. Components consume approved topology/profile DTO | Capture Pulumi preview/resource URNs; pure refactor preserving logical names/parents; extract config; components one by one; preview/diff each | Preserve Pulumi URNs/names/stack outputs. Blast: entire production; no resource replacement accepted from refactor |
| [DigitalBrainMutationTools.cs](../src/DigitalBrain.Mcp/DigitalBrainMutationTools.cs), 415 LOC | LLM/Ino, status/list queries, approval, generic signal, automations, demo/admin/UI actions | Mcp.QueryTools; CommandTools; ApprovalTools; AdministrationTools → IBrainQueryService/ICommandBus/IApprovalService/IAdministrationService | Freeze names/responses with current tests; extract read/status; add ports; replace each mutation family; delete arbitrary grain/action tools last | V1 trusted-local namespace/adapter; Production V2 names. Blast: external agents and approval/automation workflows |
| [UiSurfaceRuntime.cs](../src/DigitalBrain.Ui.Runtime/UiSurfaceRuntime.cs), 851 LOC + [UiSurfaces.cs](../src/DigitalBrain.Ui.Contracts/UiSurfaces.cs), 607 LOC | Protocol contracts/keys/actions, vocabulary, sample/auth/shell builders, charts/tasks/workbench projections, timeline transforms | Ui.Protocol (SurfaceEnvelope/action/widget vocabulary); Ui.Composition; Ui.Projections; Ui.Samples/TestFixtures. Contracts depend only on Domain IDs; projections depend on query models | Freeze C#/Dart wire/golden fixtures; add V2 protocol; move samples; move workbench/timeline projections; move builders by surface family | UiSurface/V1 key/action adapter. Blast: 50+ producers/consumers, MCP workbench, Flutter |
| [digitalbrain_rfw_library.dart](../app/lib/rfw_host/digitalbrain_rfw_library.dart), 5,392 LOC | Registry, primitive decoding/rendering, inputs/layout/charts/domain widgets, networking/catalog/editor/promotion/simulation | rfw_registry; primitives/layout/input/display/charts/domain libraries; event_bridge; diagnostics. Registry imports leaf modules; leaves do not import shell/network | Registry-name/argument/golden tests first; extract stateless groups; event bridge; stateful catalog/editor; networking last | Stable local widget names/builders and combined export. Blast: every RFW surface and golden |
| Flutter shell [forui_app_shell.dart](../app/lib/shell/forui_app_shell.dart), 807 LOC and runtime host [rfw_runtime_host.dart](../app/lib/rfw_host/rfw_runtime_host.dart), 784 LOC | Session/feed lifecycle, endpoint/channel, surface classification/store, navigation, chat/upload, action dispatch, rendering/runtime/cache | SessionController; FeedController; SurfaceStore; NavigationController; ChatUploadController; ActionClient; RfwRuntimeAdapter; presentation widgets | Freeze shell/widget tests; extract pure surface classifier/store; channel/feed; actions; chat/upload; navigation; presentation. Add reconnect/version tests before V2 enable | Preserve top-level widget/constructor and V1 feed/action adapter. Blast: login, home, chat, navigation, every surface |
| Google/Salesforce AuthNeuron + Connector duplication | UI signals, connected-app config, state/PKCE, callback exchange, token storage/refresh, provider notifications duplicated and divergent | ConnectorAuthApplicationService + OAuthFlowGrain; IProviderOAuthAdapter; ICredentialSecretStore; AuthNeuron as V1 UI adapter; IConnector as capability adapter | Freeze provider/cross-silo/two-user tests; build fake-provider coordinator; delegate start; endpoint callback; refresh/revoke; remove duplicate logic only after parity | Existing callback URLs, Auth signals, PackConfig read-old adapter. Blast: account connection and all connector tools |
| Model-provider construction paths | Unkeyed/keyed/scoped client creation, runtime config parsing, role selection/caching, provider switches across Kernel/Ino | Infrastructure.AI IModelClientFactory/adapters; Application IModelRouter/policy/budget/health; immutable registry/profile snapshot | Freeze all provider/registry/Ino selection tests; factory facade; shadow router; move Ino; move LlmResponder; stop old config writes | Existing DI service keys and env parser until cutover. Blast: every LLM, embedding, voice and tool-capable request |

The extraction rule is consistent: add a port/facade, characterize current behavior, move one responsibility with graph/golden parity, activate behind a flag, then contract the old module. File-size reduction alone is not an acceptance criterion.

## 19. Data and compatibility migration

| Item | Read-old / write-new behavior | Adapters/upcasters and dual-write/backfill | Validation and cutover | Rollback and legacy removal criteria |
|---|---|---|---|---|
| Current Synapses and journals | Read CLR-discriminated Synapse and stable V2 EventEnvelope; after gate write V2 facts and only compatibility V1 events needed by active consumers | CLR-name → stable alias registry; pure upcasters; parallel projection. Avoid domain dual-write where one AggregateCommit can be projected into V1 | Fixture corpus, per-source counts/hashes, replay with no effects; switch writer per grain/command family | Prefer old reader/writer while schemas remain additive; remove V1 writes after consumer zero-use, never remove historical reader |
| CLR type-name serialization | Preserve current FullName mapping forever for released types; new records use explicit type ID/schema | Manifest maps old FullName; aliases never reused; unknown quarantine | Missing assembly/renamed/corrupt fixtures and restore rehearsal | Revert read preference; stable map/upcasters stay. Remove reflection discovery only after all stored types are manifested |
| Existing grain IDs | Direct old references remain valid; new commands use GrainAddressResolver/canonical key | Persistent legacy map, no implicit reinterpretation or copying | Inventory, ambiguity report, activation/key collision tests, shadow resolver | Disable resolver route; old grain remains. Remove old route only after zero use and state/projection validation |
| Global main grains | Compatibility facade may use old owner only while CapabilityIsolationGate proves one product principal/personal-default; enabling another scope disables that path until scoped replacement | ino-main/session/self-evolution/automation adapters; lazy scoped fact import, not command replay | Sole-owner revocation and two-user/workspace suite; state/projection parity; per-grain cutover | Preserve scoped V2/read-only evidence or disable capability; never route multiple principals back to a global writer |
| Commit owner directory/outbox | V1 owners remain undiscovered/at-most-once; before any first V2 commit, append a sequenced canonical owner registration then write ordered AggregateCommit/effect intent | Idempotent sharded registration log; immutable effect transitions + rebuildable pending index; no blind conversion of historical FireAsync intent into pending effects | Enrollment/high-water/epoch failpoints, sequence/index reconstruction, directory/aggregate reconciliation, scanner restart, backup/restore, no orphan owner | Stop new V2 commits and preserve directory/aggregate records; rebuild only from verified inventory while dispatch is paused. V2 never deletes a registration; a future retirement ADR must prove archive/discovery safety |
| Default workspace | Null/empty/old values read as default only for authenticated legacy owner; new writes require explicit WorkspaceId | Personal tenant/default workspace seed and membership ledger; projection owner backfill | Every record mapped/quarantined; rerun idempotent; no wildcard | Disable mapping and use old view; do not delete membership. Remove null-write support after clients zero-use |
| Proposals and decisions | V1 proposal/pending/decision/result remain readable; new proposals use WorkflowRecord/EventEnvelope | Project V1 states; approved with no result becomes ManualIntervention; never auto-apply | Proposal hash, approver mapping, state legality, crash-window replay | Read V1 status; disable V2 submission. Remove V1 apply only after pending/ambiguous inventory is zero |
| PackConfig credentials | Credential adapter reads CredentialRef first then old app/user pack; writes new secret version/reference only | Trusted decrypt + re-protect because scope is in DataProtection purpose; no ciphertext copy; no dual refresh | Validate owner/grants/refreshability without logging; backup/key-ring restore; controlled provider probe | Retain old encrypted pack until new credential works and restore passes; reauthorization for unknown rotation; remove old read after zero active refs |
| UI surfaces/actions | Client reads V1 and wire V2; feed writes token-free surface/binding; action owner writes append-only use + command-submission outbox with preassigned OperationId | V1 wrapper; allowlisted binding/template/schema; delivery token hash; binding-wide usage index rebuilt from use transitions; downstream command idempotency | C#/Dart goldens, token absence/hash stability, two-token/crash-window/status tests, old client, isolation/replay | Keep token-free V2/use/outbox data and drain queued commands. V1 only under isolation gate; never persist bearer or replay accepted action through V1 |
| MCP clients | Existing names/read fields available through compatibility schema; new commands require OAuth scopes/idempotency | Trusted-local V1 namespace; HTTP V2 tool groups; client re-consent for scopes | Protocol/schema contract, audience/Origin, list/invoke scope, rate/size | Disable command/approval groups; reads use safe adapter. Remove V1 remote namespace immediately; local removal after telemetry/SLA |
| Model registry settings | Read AppHost env snapshot and old system llm_provider/llm_key; new policy references RegistrationId + CredentialRef | Compatibility parser and shadow decisions; avoid dual provider construction | Unique service keys, construction parity, policy conflict report, privacy/budget/fallback tests | Compatibility policy matches old precedence. Remove old writes after no conflicts/use; keep read adapter for rollback window |
| Projections | Write new versioned projection beside old/no projection; queries shadow/compare before serving | Enumerate registration sequence and pull each owner's commits by CommitSequence; separately full-backfill V1; idempotent tail, versioned target and read alias | Directory epoch/high-water coverage, per-owner cursors, counts/hashes/semantics, lag, quarantine, scoped cursor tests | Atomic alias to prior projection or redacted V1 scan; source directory/journals unchanged; delete old projection only after backup/retention approval |

## 20. Risk register

| Risk | Probability | Impact | Affected milestone | Early warning signal | Mitigation | Contingency | Owner/boundary |
|---|---|---|---|---|---|---|---|
| Historical journals become unreadable after type/package change | Likely without action | Critical | M3, M10 | Fixture/upcaster failure, unknown discriminator, activation/read exception | Immutable corpus, stable aliases, upcasters, missing-assembly tests, no in-place rewrite | Prefer old reader/binary; quarantine position; restore source blob; forward upcaster | Domain schema + Infrastructure.Orleans |
| Duplicate external effect after timeout/retry/failover | Possible | Critical | M4, M6, M7 | Same EffectId/provider operation repeated; inbox conflict; unknown count | Stable idempotency, inbox, lease, provider verification, no blind retry | Stop workflow, verify provider, compensate or manual intervention | Workflow + provider adapter |
| Approved proposal is lost/stranded | Likely in V1 crash window | Critical | M4 | Approved/decision event with no queued/terminal workflow | New approval records Approved audit + ApplyQueued/first effect atomically; imported Approved requires due metadata and scanner | Import ambiguous state as ManualIntervention; never silently reapprove/apply | Self-evolution workflow |
| Committed effect/event owner is not discoverable after crash | Possible without sequencing | Critical | M4, M5 | Commit advances but scanner never observes owner; registration/commit gap or epoch mismatch | Pre-commit append-only RegistrationSequence; captured high-water scan; contiguous CommitSequence; backup/restore/repartition failpoints | Fail mutation readiness, restore directory, reconcile inventory while dispatch paused | Persistence + workflow/projection runtime |
| Cross-workspace memory/journal/feed/tool/connector disclosure | Likely in current global paths | Critical | M2, M5, M6, M8 | Isolation canary, global fallback, shared private surface, gate/evidence mismatch | Server scope/keys plus fail-closed capability gate; no second workspace on unisolated path; scoped queries/catalog/feed and two-by-two suite | Disable capability/workspace, revoke tokens, incident response/rebuild; never restore global fallback | Identity/security + every adapter |
| OAuth credential loss during scope move or refresh rotation | Possible | Critical | M7 | Decrypt failure, missing replacement token, refresh invalid_grant spike | Read-old, trusted decrypt/re-protect, backup/key-ring test, serialized rotation, retain Google prior token | Retain old pack/version where valid; ReauthorizationRequired; manual recovery | Connector secret store |
| OAuth state/PKCE replay, stranded exchange, or mis-correlation | Likely in current duplicate paths | Critical | M7 | Reused state, HMAC lookup mismatch, missing verifier/code ref, ExchangeQueued/OutcomeUnknown age | HMAC(state)-keyed versioned flow, atomic claim/effect, durable lease/attempt, exact redirect/S256, no blind exchange retry | Reject/expire or mark ReauthorizationRequired; retain prior credential; never infer success | OAuth coordinator |
| V1/V2 UI incompatibility or blank client | Possible | High | M8 | Negotiation failure, golden mismatch, unsupported widget, reset loop | Dual decoder, capability negotiation, old-client matrix, cached last-known-good, V1 adapter | Negotiate V1, disable V2 per client, retain feed rows and prior app | UI protocol/Flutter |
| Forged/replayed/reissued UI token executes duplicate or wrong command | Likely in V1 raw action path | Critical | M2, M8 | Raw action use, MaxUses conflict, claimed slot without operation, multiple operations per use | Versioned template, atomic use+outbox queue, preassigned operation/idempotency, binding-wide ordinal, target reauthorization/dedup | Stop issuance/drain queue, invalidate binding, inspect operation and compensate if required | UI action service/security |
| Orleans grain-key migration creates two logical owners | Possible | Critical | M2, M6, M10 | Same business ID active at legacy and V2 keys; state divergence | Resolver/mapping ledger, no reinterpretation, lazy explicit cutover, collision tests | Freeze new route, direct to legacy, reconcile facts without command replay | Identity + Orleans infrastructure |
| Projection corruption, lag, or wrong-scope backfill | Possible | High | M5, M8 | Hash/parity mismatch, lag/quarantine growth, isolation canary | Versioned rebuild, atomic checkpoint/alias, source refs, owner quarantine | Alias prior projection/redacted V1 query; rebuild from immutable source | Projections |
| Local/Production capability or topology drift | Likely today | High | M9 | Snapshot diff, capability unavailable after deploy, hard-coded endpoint | Explicit profiles, normalized graph diff, preview, service discovery, smoke | Block/rollback revision; expose capability unavailable rather than fallback | Platform/deployment |
| Missing or sensitive telemetry during incident | Likely today | High | M9 | No MCP/client spans, drop count, secret canary, metric overflow | ServiceDefaults, supported collector, source redaction, bounded metrics, drop accounting | Use domain journals/operation projection; disable unsafe exporter; incident instrumentation hotfix | Observability/security |
| Deployment rollback cannot reverse external effects | Certain for some effects | Critical | M4, M9 | Effect succeeded while app revision rolls back | Keep effect schemas/readers compatible; compensation/verification; release boundary avoids in-flight breaking change | Forward fix/manual compensation; never claim infrastructure rollback reversed provider action | Workflow + release management |
| Auth rollout locks out owner or leaves bootstrap open | Possible | Critical | M2 | Bootstrap reuse, no valid admin, rising auth failures | One-use operator bootstrap, recovery procedure, shadow policy, local console break-glass audit | Loopback-only break-glass with rotation and incident audit; no remote anonymous mode | Identity/operator |
| Model fallback violates privacy/residency/budget | Possible | Critical | M6 | Decision conflict, unexpected provider/region, cost/token breach | Pre-authorized candidate set, classification policy, budget reservation, audited decision | Disable fallback/provider; return typed unavailable; rotate exposed credentials if needed | Model policy/security |
| Alpha/preview Orleans journaling or Agent/MCP API breaks compatibility | Possible | High | M3, M6, M7 | Restore/compile/contract fixture failure on dependency update | Pin versions, compatibility lane, adapter boundaries, no routine blind bump | Stay pinned; roll back package; upcast/adapter forward fix | Platform/dependency owners |
| Feed retention/backpressure loses user-visible surface | Possible | High | M8 | Sequence gap/reset rate, slow-client lag, storage age | Durable sequence, explicit reset/current snapshot, close/resume, retention policy | Rebuild current-surface projection; notify client; V1 fallback for compatible user | UI feed |

## 21. Recommended first pull requests

### PR 1 — Characterization and sensitive-data canaries

- **Purpose/scope:** tests and synthetic fixtures only: login/config/OAuth/tool/timeline/checkpoint/MCP/UI secret canaries; workspace context leak; self-evolution failpoint interfaces; historical journal fixture manifest. Maps to V2-TEST-CHAR-001 and characterization prerequisites for V2-SAFETY-001/V2-JOURNAL-001.
- **Dependencies:** none; agree on synthetic canary strings and fixture retention.
- **Acceptance criteria:** tests demonstrate current unsafe paths without printing real data; current happy-path suite remains; fixtures are deterministic and secret-free.
- **Why safe:** no production behavior, dependency, runtime, topology, or persisted data change.
- **Must not bundle:** redaction implementation, auth middleware, schema/type rename, grain re-key, package upgrade, or workflow code.

### PR 2 — Redaction boundary and Production MCP mutation-off gate

- **Purpose/scope:** implement the shared classification/safe-summary primitives and apply them to get_timeline/MCP/log output; profile-gate mutation registration in [MCP Program](../src/DigitalBrain.Mcp/Program.cs). Maps to V2-SAFETY-001, the first bounded slice of V2-SAFETY-EGRESS-001, and V2-MCP-001.
- **Dependencies:** PR 1 canaries; ADR-014.
- **Acceptance criteria:** canaries never leave/persist through covered paths; Production HTTP MCP tool list is read-only; trusted Development stdio remains explicitly opt-in; unsafe profile fails startup.
- **Why safe:** behavior is narrowed at security boundaries while existing local read and application flows remain.
- **Must not bundle:** full OAuth MCP implementation, tool renaming, IGrainFactory removal, auth-provider choice, connector migration, or journal rewrite.

### PR 3 — Authenticated ingress and signed session exchange

- **Purpose/scope:** add ASP.NET auth/policy pipeline to Kernel/MCP and local session-token exchange that requires password/OIDC reauthentication or an existing unforgeable server proof; keep bootstrap and app/default configuration behavior unchanged but remotely inaccessible until the next PR. Maps to V2-AUTH-001.
- **Dependencies:** PR 1 auth/privilege tests; ADR-001/008 auth authority selected.
- **Acceptance criteria:** anonymous private read/mutation rejected; clientId-only exchange and spoof both fail; valid reauthentication/server proof yields a bounded signed token; HTTP/gRPC/MCP derive the same principal; legacy bootstrap/config endpoints are not remotely reachable.
- **Why safe:** closes concrete critical exposure before grain/data migration; feature/profile flags allow staged client adoption.
- **Must not bundle:** operator bootstrap redesign, app-config policy migration, tenant data backfill, grain-key changes, OAuth provider tokens, durable workflow, or UI protocol.

### PR 4 — One-use bootstrap and app-config authorization

- **Purpose/scope:** replace first-caller-admin with a one-use operator proof, add explicit loopback recovery, and require brain.admin for app/default configuration at ingress and handler. Maps to V2-AUTH-BOOTSTRAP-001.
- **Dependencies:** PR 3 principal/session pipeline; ADR-001 operator/bootstrap authority selected; PR 1 privilege fixtures.
- **Acceptance criteria:** exactly one authorized bootstrap succeeds; replay/expiry/remote use fail; non-admin config fails at both layers; consumed-install restart and audited recovery pass.
- **Why safe:** narrows two concrete privilege-escalation paths without moving users, journals, keys, or application routing.
- **Must not bundle:** tenant/workspace membership, connector OAuth, grain keys, model settings migration, workflow, or UI protocol.

### PR 5 — Additive identity and V2 contract package

- **Purpose/scope:** add TenantId/WorkspaceId/PrincipalRef, ephemeral RequestContext plus persisted actor snapshot, typed CauseRef, ordered immutable Command/Event/Commit/Outbox/EffectTransition/index contracts, sequenced CommitSourceRegistration/directory/owner cursors, stable schema registry, validators, and V1 adapter tests; no caller routes to them. Maps to V2-IDENTITY-001 and V2-ENVELOPE-001.
- **Dependencies:** ADR-001/003/004/005 field-level decisions; may proceed parallel to PR 4 after PR 3 fixes the principal shape.
- **Acceptance criteria:** aliases/member IDs unique/frozen; prohibited context fields cannot enter actor snapshot; sequence/ordinal/index-rebuild invariants pass; V1 round trips; runtime graph/tests unchanged.
- **Why safe:** additive library seam establishes vocabulary without moving state or behavior.
- **Must not bundle:** new journal writer, domain dual-write, grain base edit, workflow dispatcher, projection store, or package bump.

PR 1 precedes PR 2/3. PR 4 follows PR 3; PR 5 may develop in parallel with PR 4 after the principal shape and ADR fields are approved. Do not enable a second product principal/workspace in this batch; V2-IDENTITY-AUTHZ-001 and V2-ISOLATION-GATE-001 must land first. The historical reader/upcaster follows and must precede V2 writes; workflow/outbox starts only after relevant M2/M3 gates.

## 22. Open decisions and next-session checklist

### Decisions requiring product, security, or operator input

| Decision | Required answer before |
|---|---|
| Is personal-only permanent, or must families/teams/customer tenants be supported; is workspace a hard security boundary? | ADR-001, key/backfill implementation |
| Which identity authority protects local, Production web/gRPC, and HTTP MCP; what is the break-glass/bootstrap policy? | V2-AUTH-001 / V2-AUTH-BOOTSTRAP-001 |
| Which actions require human approval, who may approve which risk, and is separation of duties required? | ADR-006/workflow acceptance |
| Are journals domain source, audit log, debugging trace, or all; what retention/erasure/legal rules apply? | ADR-004 and archive design |
| Is at-least-once plus idempotency/verification accepted; which external effects cannot be retried? | ADR-005/006 provider contracts |
| Should HTTP mutation MCP ever be public, and which issuer/audiences/scopes/client types are supported? | ADR-008/014 |
| Are Google/Salesforce app credentials operator-owned; can a user supply a connected app; who owns shared workspace grants? | ADR-013/OAuth migration |
| Is Gmail send a supported near-term capability and what approval/retention policy applies? | Google capability migration |
| May any surface be genuinely public/shared; what V1 client compatibility SLA applies? | ADR-009/feed retention |
| Is Aspire logical topology with Pulumi projection/diff approved, or should one engine replace the other? | ADR-012/M9 |
| Which model/embedding/voice/MCP/connector capabilities are mandatory in Production? | ADR-010/011 |
| What RPO, RTO, SLO, alert, residency, privacy, token/cost, journal/inbox/feed retention, and restore requirements apply? | M9 production gate |
| Is .NET 11 preview plus Orleans alpha journaling acceptable through the next release, and what upgrade window/gate is required? | Dependency/release policy |

### Required experiments/spikes

1. Prove or reject one-write atomic AggregateCommit using the exact current Orleans journaling/persistence provider; inject ETag/storage failures.
2. Prove repeatable full-cycle RegistrationSequence/epoch/high-water scanning (including an old owner gaining a commit after its prior visit), contiguous Commit/Event/Effect ordering, immutable transition/index reconstruction, commit-before-hint, concurrent registration, outage/repartition, and restore; do not claim at-least-once until it passes.
3. Restore and decode a copy of representative current journal blobs with an integration assembly intentionally absent; no live source mutation.
4. Exercise Google S256 PKCE against the configured client type/test project before mandatory enforcement.
5. Exercise Salesforce endpoint → coordinator → token request, refresh rotation, crash-after-exchange, and revoke in a sandbox.
6. Prototype projection checkpoint + row atomicity and rebuild/live-tail alias switch using the initial selected store.
7. Validate supported Flutter web/mobile telemetry transport and authentication; do not assume browser JavaScript instrumentation applies unchanged.
8. Kill/restart the owning silo at every workflow/outbox boundary and validate ACA termination/drain behavior.
9. Run P3 backend/Agent/RFW tooling spikes only after their ports are stable.

### Evidence still missing

- Live DigitalBrain MCP timeline, cross-neuron lineage, Ino status, proposal, and workbench responses; no DigitalBrain MCP tools/resources were connected to this session.
- Live metric values, projection lag, outbox age, workflow age, telemetry drops; Aspire MCP has no metric query and these V2 components do not yet exist.
- Production Azure topology/health/logs/traces, managed-identity access, backup/restore status, RPO/RTO, and rollback evidence.
- Real journal volume/type distribution, global grain inventory, active users/workspaces, V1 client versions, PackConfig credential counts/grants, feed retention needs, model cost/latency.
- Provider-sandbox evidence for Google PKCE and Salesforce rotation/revoke.
- Product/legal decisions on retention, erasure, prompt/connector-data storage, approval, public/shared surfaces, and tenant target.
- Context7 documentation payloads; the current quota blocked every resolution request.

### Exact next planning/implementation session

Run an **ADR and characterization kickoff** with product, security, platform, and domain owners:

1. Ratify ADR-001, ADR-003, ADR-004, ADR-005, ADR-006, ADR-008, ADR-013, and ADR-014, recording dissent and explicit blockers.
2. Approve the personal/default migration rule, prohibited-data matrix, approver policy, HTTP MCP exposure, and one-grain atomicity spike.
3. Review PR 1’s exact synthetic fixtures/failpoint matrix and assign schema, identity, workflow, connector, UI, and platform owners.
4. Connect DigitalBrain MCP read tools and capture a redacted representative timeline/lineage/Ino/proposal/workbench baseline.
5. Run the read-only inventory for journal types/keys/scopes/clients/capabilities and design the storage atomicity plus owner-directory discovery spikes against a disposable Test profile.
6. Open **PR 1 — Characterization and sensitive-data canaries** only. Do not enable a second product principal/workspace or begin grain re-keying, journal write-new, workflow execution, credential migration, or deployment changes in that session.

The exit artifacts are signed ADRs, a secret-free evidence inventory, the accepted PR 1 test matrix, named owners, and explicit go/no-go criteria for PRs 2–5.
