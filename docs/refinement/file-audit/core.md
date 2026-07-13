# File Audit — core (DigitalBrain.Core)

- **Subsystem**: core — `src/DigitalBrain.Core/` (shared domain / synapse / contract layer)
- **Scope**: all 68 files listed in the core file list (67 `.cs` + 1 `.csproj`)
- **Commit**: `72400e3ebbec27e17af4ae6b5b2c4158c2797fa4` (branch `docs/refinement-audit`)
- **Date**: 2026-07-13
- **Method**: every file read in full (line ranges in the ledger); external references verified with repo-wide word-boundary searches across `src`, `tests`, `app`, `hosts`, `integrations`, `tools`; Orleans serialization and cancellation semantics verified against official Microsoft Learn Orleans docs (Context7 quota was exhausted; documented below).

## Subsystem overview

`DigitalBrain.Core` is the bottom layer of the solution: it defines the actor contract (`INeuron`, `IHandle<T>`), the base message (`Synapse` + ~80 concrete synapse records), the self-evolution rail vocabulary (`SelfEvolution.cs`, `Automations.cs`, `CodeFoundrySynapses.cs`), and a second, newer "v2 runtime" contract family under `DigitalBrain.Core.Runtime` (tenant/workspace/principal identity, session tokens, INO operation/outbox projections, UI surface envelopes, MCP transport guard). It references only `Microsoft.Orleans.Core.Abstractions` and `Microsoft.Orleans.Serialization` and is packable (`DigitalBrain.Core` 0.3.0). Everything else (Kernel, Kernel.Abstractions, Mcp, Ui.Runtime, Ui.Contracts, Pack.Contracts, integrations, hosts) compiles against it.

The dominant structural fact (FACT, verified by reference counts): Core contains **two generations of contracts side by side**. The v2 `Runtime` namespace (RequestContext/TenantId/PrincipalRef/GrainIds/DurableInoContracts/Surface*) is heavily used and disciplined. The older flat namespace (`Synapse.cs` and satellites) mixes the genuinely load-bearing primitives (`Synapse`, `INeuron`, `IHandle`, self-evolution records) with a large residue of dead or speculative contracts from earlier prototypes (chart neuron, closed loops, architect/NuGet loops, system status, Salesforce OAuth callback records, local user grain, etc.).

## Framework verification notes

- **Orleans record serialization** (Microsoft Learn, "Serialization in Orleans", Orleans 10 docs): members of a record's **primary constructor have implicit IDs assigned by parameter order**; parameter order cannot change for deployed types; body members need explicit `[Id]` and do not share identity space with constructor parameters; member IDs are scoped per inheritance level; `[Alias]` values are globally scoped and recommended. This confirms the Core pattern *compiles and round-trips correctly today*, and also grounds REL-001 (order fragility of the many un-annotated records).
- **CancellationToken in grain interface methods** (Microsoft Learn, "Use cancellation tokens in Orleans grains"): direct `CancellationToken` parameters (last position, optional) are the recommended pattern since Orleans 9; all Core grain interfaces follow it correctly.
- **Documentation gap**: Context7 monthly quota was exceeded during this audit; Orleans facts above were verified via `microsoft-learn` MCP (official learn.microsoft.com content) instead. No version-specific behavior of the 10.2.1-preview.1 packages could be checked beyond the stable 10.x docs.

---

## Per-file review

Format per file: purpose → key observations against the 16-point standard → verdict.

### src/DigitalBrain.Core/AssemblyInfo.cs (1-3)
`InternalsVisibleTo("DigitalBrain.Tests")` only. Correct, minimal. **Verdict: retain.**

### src/DigitalBrain.Core/DigitalBrain.Core.csproj (1-19)
Packable core abstractions package, `net11.0`, references only Orleans Core.Abstractions + Serialization (10.2.1-preview.1 via central versions). The description claims "Pure stable layer", yet it pins preview/alpha-line Orleans packages (FRAME-002) and (per CLEAN-001) carries substantial dead surface. `Models/DigitalBrainModelRegistrySnapshot.cs` uses `Microsoft.Extensions.Configuration` without a declared direct PackageReference (FRAME-003). **Verdict: retain; tighten dependency declarations.**

### src/DigitalBrain.Core/Synapse.cs (1-371)
The base message plus ~40 unrelated contracts in one file: auth (LoginRequest with **plaintext password**, LocalUserRegistered with **password hash+salt**), task protocol, LLM prompt/response, self-awareness (SystemStatus/FixProposal/SimulationResult), checkpoints/branching, NuGet/Architect closed loops, context/filter/chart/3D-graph synapses, kernel self-update, Salesforce OAuth callback records.
- `Synapse` itself is coherent: `Type`, `Timestamp`, optional `Sender`/`Receiver`, `IsBroadcast`, `CorrelationId` with explicit `[property: Id(0..5)]`, body `SynapseId`/`CausationId` at `[Id(6)]/[Id(7)]`, and `Stamp()` for causal lineage. Correct per Orleans docs.
- It carries **no tenant, workspace, or principal** (ARCH-002); identity is bolted on per message as `string UserId = "anonymous"` (`ExperienceUsed`, `RunTask`, `CancelTask`, `VisualizeDataRequest`) — SEC-004.
- Verified dead (zero references outside Core, repo-wide): `SystemLaunched`, `FixProposal`, `SimulationResult`, `ISystemStatus`, `NuGetCommand`, `NuGetResult`, `ArchitectRequest/Report/Result`, `ClosedLoopRequest/IClosedLoopNeuron/ClosedLoopCompleted`, `ContextUpdate`, `MemoryStored`, `FilterChanged`, `ChartCommand`, `ChartInteraction`, `IChartNeuron`, `WidgetTreeInspected`, `UIModificationProposed`, `SystemModificationProposed`, `SalesforceOAuthCallback(+Result)`, `IUserGrain`, `UserProfile` (CLEAN-001).
- The `SalesforceOAuthCallback` comment claims it "replaces direct Program.cs store IO" but nothing references it — misleading comment on dead code.
**Verdict: split + heavily prune.** Keep `Synapse`, `SynapseType`, `UserId`, session/task/login lifecycle actually used by `UserSessionNeuron` and kernel; delete the dead blocks; move auth records away from plaintext secrets (SEC-001/002).

### src/DigitalBrain.Core/SelfEvolution.cs (1-120)
The rail vocabulary: `SelfEvolutionProposal` → `Pending/Rejected/Expired` → `SelfEvolutionDecision` → `DecisionRecorded/DecisionRejected` → `SelfEvolutionApplyResult` / `SelfEvolutionRollbackRequired`, plus `ISelfEvolutionNeuron`, apply-via constants, well-known neuron id. Good XML docs; explicit `[Id]`s throughout (the one file family that takes serialization versioning seriously).
Gaps at the type level: decision binds only to `ProposalId`, not to proposal content (PROD-001); `ExpiresAt` optional with no mandatory bound (PROD-002); `RequiresHumanApproval` and `Risk` are proposer-asserted (PROD-003); `Origin`/`DecidedBy` are unconstrained strings with no principal/tenant typing; `RollbackPlan` free text (PROD-004). **Verdict: retain + harden (see findings).**

### src/DigitalBrain.Core/Automations.cs (1-125)
Reactive automation vocabulary (`RegisterScript`, `RegisterReaction`, `AutomationApp`, staged variants, `AutomationRun`, `AuditBypass`, `IAutomationNeuron`). Staging types (`AutomationDefinitionStaged`, `AutomationRemovalStaged`) correctly tie into the rail via `ProposalId`. Defects: `IReadOnlyList<string> DeclaredEmits = null!` defaults on three journaled records (REL-002); `IAutomationNeuron.DefineReactionAsync`/`RemoveReactionAsync` are documented as "trusted/bootstrap convenience" — a documented bypass of the rail that only convention keeps out of user paths. `AuditBypass` carries both a `When` parameter and the inherited `Timestamp` (duplication). **Verdict: retain; fix null! defaults; keep bypass methods under explicit guard review (kernel-side concern).**

### src/DigitalBrain.Core/CodeFoundrySynapses.cs (1-112)
Foundry (codegen/run/deploy) synapses + neuron interfaces. Coherent tiering (`TargetTier.Run/Deploy`), staging (`FoundryApplyStaged` with `ProposalId` + `CheckpointId`), rollback (`FoundryRolledBack`). `FoundryRequest.AutoApply` is a contract-level auto-apply flag — anything that honors it without a rail decision would bypass governance; the flag's authority must live with the kernel allowlist, not the message. All records use explicit `[Id]`s — good. **Verdict: retain; document `AutoApply` trust semantics.**

### src/DigitalBrain.Core/Config/IPackConfigStore.cs (1-7)
Two-method scoped config store abstraction; widely used (12 external files). Scope is a raw string (see `PackConfigScopes`), so tenant separation is by convention. **Verdict: retain.**

### src/DigitalBrain.Core/Conversation.cs (1-92)
INO conversation projection model (`InoConversationIdentity`, `InoConversationStates`, `InoConversationTurn/Operation/Snapshot`, `ToolAction`, `ToolGrounding`). JSON-serialized (not Orleans) — deliberate, since it round-trips through outbox JSON. The legacy `InoConversationOperation` compat constructor is kept "until old snapshots age out" with no removal trigger or version gate (CLEAN-004). `InoConversationStates` string constants + `IsActive` are duplicated in spirit by `InoOperationPhase` (DurableInoContracts) with two hand-maintained mapping functions. **Verdict: retain; plan compat-ctor removal.**

### src/DigitalBrain.Core/ConversationExecutionContracts.cs (1-8)
`ExternalAuthorizationResolution` (Waiting/Ready/Failed + SafeReason). Small, used (4 external files). **Verdict: retain.**

### src/DigitalBrain.Core/ConversationSurfacePayload.cs (1-230)
Builds the bounded INO conversation surface payload (16-turn window, 2 KiB/turn, 64 KiB total), re-validates persisted `ToolAction`s on every render (defense in depth — good), maps phases to projection strings, emits action bindings by state. Well-tested (`ConversationSurfacePayloadTests`). Minor: `TurnKey` = SHA256(CommandId+Role) collides for two turns sharing a command and role (REL-005); trim loop re-serializes per removed message (bounded — acceptable, PERF-002); `ProjectionPhase(string)` maps `Queued→"accepted"` while the enum path maps `Queued→"queued"` — inconsistent phase strings for the same logical state depending on whether `Phase` was populated. **Verdict: retain.**

### src/DigitalBrain.Core/DeploymentPreview.cs (1-34)
Pure topology drift preview (`DeploymentPreviewer.Preview`). Referenced only by `tests/DigitalBrain.Tests/Runtime/ContractsTests.cs` — no production caller (CLEAN-002). Logic is sound (missing/changed resources, blocking iff required). **Verdict: delete or move to the component that will actually call it.**

### src/DigitalBrain.Core/DistributedAppStarted.cs (1-5), StartDistributedApp.cs (1-6), RestartResource.cs (1-10), NeuronActivated.cs (1-5)
One-record synapse files for app lifecycle; all referenced (kernel system neurons / `IAspireNeuron`). Implicit ctor-param IDs (REL-001 applies). `RestartResource.Strategy` defaulting a nullable to `"one-replica-at-a-time"` is odd but harmless. **Verdict: retain (could merge into one lifecycle file).**

### src/DigitalBrain.Core/DurableInoContracts.cs (1-365)
The strongest file in Core: the INO operation state machine vocabulary. `InoOperationPhase` (Orleans-authoritative), `AcceptedCommand` (idempotency key + input hash + schema version), `OperationReceipt`, `ApprovalRecord`, `EffectRecord` (provider idempotency key), `OperationFeedView` (documented as deliberately credential-free), `OperationOutboxRecord` (versioned, self-validating projection with `IsCurrent()`, rolling repair in `TryRead`, fail-closed on unknown payloads), `WorkflowReference` (opaque), `InoAuthorizationRequest` (no tokens), `IInoToolGateway` / `IAgentWorkflowRunner` boundary interfaces with explicit trust documentation. This is the OS model done right: idempotent, replayable, fail-closed, provider-payload-free.
Notes: `StateFor` sends `InoOperationPhase.Failed` through the `_ =>` default (works, but a future new phase silently becomes "failed"); `OperationOutboxRecord.ToPayloadUtf8/TryRead` use default `JsonSerializer` options — enum `Phase` serializes as a number, so renumbering the enum is a wire break (documented nowhere); the parallel approval vocabulary vs `SelfEvolutionDecision*` is noted in PROD-001. **Verdict: retain.**

### src/DigitalBrain.Core/Experience.cs (1-18)
`Experience` (catalog entry with `IReadOnlyDictionary<string, object?> EntryAction`) + `ExperienceStep` synapse. Used by pack/UI layers (4 external files). `object?` bags rely on `SynapsePayloadJson` discipline. **Verdict: retain.**

### src/DigitalBrain.Core/GrpcAuthentication.cs (1-16)
Static helper validating `x-v2-audience` + `x-v2-session` metadata against `SessionTokenService`. **Zero references outside Core repo-wide** (verified) — dead. Either the gRPC services re-implement this inline (drift risk) or it was superseded. **Verdict: delete (or adopt it in `UiGrpcService` and delete the duplicate logic there).**

### src/DigitalBrain.Core/ICheckpointKeyProvider.cs (1-8), INeuronStateProtector.cs (1-9)
Key-source and encrypt-at-rest abstractions; both used by kernel security wiring. Clear comments; correct layer. **Verdict: retain.**

### src/DigitalBrain.Core/IHandle.cs (1-6)
`IHandle<T> where T : Synapse` with `HandleAsync(T, CancellationToken)`. The typed-dispatch keystone; correct. **Verdict: retain.**

### src/DigitalBrain.Core/INeuron.cs (1-36)
The universal grain contract: `FireAsync<T>`, `DeliverAsync`, dual timelines, causal queries, checkpoint/branch/restore, silo identity. Generic method + `CancellationToken` usage is doc-verified valid. Concerns: `GetTimelineAsync` exposes the full journal (including any secret-bearing synapse — amplifies SEC-001/002) with no principal/read-scope parameter; timeline queries return unbounded `IReadOnlyList<Synapse>` (PERF-001); every neuron must implement simulation/branching whether meaningful or not (interface breadth). **Verdict: retain; consider splitting timeline/simulation into optional facets and adding read authorization at the kernel boundary.**

### src/DigitalBrain.Core/JsonElementSurrogate.cs (1-24)
Orleans surrogate + `[RegisterConverter]` for `JsonElement` — matches the documented Orleans surrogate pattern; picked up by codegen (no direct references needed). Note `SynapsePayloadJson.cs`'s comment still claims "Orleans has no codec for JsonElement" (FRAME-004). Tested (`JsonElementSurrogateTests`). **Verdict: retain.**

### src/DigitalBrain.Core/McpContracts.cs (1-21)
`Page<T>`, `OperationStatus`, `McpError`, `Capability`, `IdempotencyConflictException`, `IQueryPort`, `ICommandPort`. Reference check: `IQueryPort`/`ICommandPort`/`OperationStatus`/`McpError`/`IdempotencyConflictException`/`WorkflowState` (in RuntimeContracts) have **zero implementations/usages repo-wide**; `Page<T>` is used once — by `integrations/DigitalBrain.Salesforce/SalesforceApiClient.cs` for an unrelated purpose; `Capability` once by an apply handler. Speculative port layer that never landed (ARCH-007). **Verdict: delete the unused ports/records; relocate `Page<T>`/`Capability` next to their real consumers.**

### src/DigitalBrain.Core/McpGuard.cs (1-92)
`McpRequestGuard`: per-principal fixed-window rate limit + concurrency semaphore + origin/audience/body checks; bounded principal map (4096) with periodic idle eviction. Used by `DigitalBrain.Mcp/Program.cs`. Solid fail-closed shape. Races (REL-004): (a) between `TryGetValue` and `lock(window)` an idle window can be evicted, so the request is counted against an orphaned window; (b) `Count >= MaximumTrackedPrincipals` check then `GetOrAdd` is not atomic, so the cap can be slightly exceeded. **Verdict: retain; minor hardening optional.**

### src/DigitalBrain.Core/ModelRouting.cs (1-33)
`ModelDescriptor/ModelPolicy/ModelSelection/IModelHealth/ModelRouter` — policy-based model selection (privacy class, residency, cost, latency). Referenced only by `ContractsTests` (CLEAN-002); production model selection is `Models/DigitalBrainModelRegistry` (ARCH-005). Two authorities for "which model", one of them dead. **Verdict: delete or merge into the Models registry.**

### src/DigitalBrain.Core/Models/DigitalBrainModelCatalog.cs (1-144)
Provider ids, capability kinds, role enum, `DigitalBrainModelCapabilities` (with clear role-vs-capability doc), `DigitalBrainModelDescriptor` (ServiceKey normalization), `DigitalBrainModelRegistration`, mutable `DigitalBrainModelRegistry` with sensible default-LLM fallback chain (Balanced → Reasoning → Fast → any). `ServiceKey` normalization (`:`/`.`→`-`, lowercase) can collide (`a.b` vs `a:b` vs `a-b`) — theoretical for the curated catalog. **Verdict: retain.**

### src/DigitalBrain.Core/Models/DigitalBrainModelRegistrySnapshot.cs (1-72)
Reads the Aspire-exported indexed config section back into typed entries. Silent-skip on unparsable `Kind` and silent default `Role` on parse failure — a misconfigured registration disappears or gets misfiled without any signal (silent config degradation, Low; folded into CLEAN/FRAME notes). Uses `Microsoft.Extensions.Configuration` transitively (FRAME-003). **Verdict: retain; surface parse failures.**

### src/DigitalBrain.Core/Models/DigitalBrainModels.cs (1-32)
`DigitalBrainModel`/`LlmModel`/`EmbeddingModel` typed markers. `<see cref="DigitalBrainOptions.WithLLM{TModel}"/>` dangles — that type lives in the hosting layer, unresolvable from Core (CLEAN-003). **Verdict: retain.**

### Models/* provider markers (15 files, each read 1-EOF; 3-9 lines each)
`Anthropic/Claude45Haiku|Opus46|Sonnet46`, `AzureOpenAI/Gpt4oMini`, `GitHub/Gpt41Mini|Gpt41Nano|O4Mini|TextEmbedding3Small`, `Ollama/Llama31_8B|MxbaiEmbedLarge|NomicEmbedText`, `OpenAI/Gpt54|Gpt54Mini|Gpt54Nano|TextEmbedding3Small`. Trivially-similar sealed marker classes. Usage: `Llama31_8B`, `MxbaiEmbedLarge`, `Gpt4oMini`, the embedding markers and a few others appear once in hosts/app config; `NomicEmbedText` has **zero** external references. This is a menu, not live code — cheap to keep, but each unused marker asserts a model id and hand-maintained capability flags nobody validates (Opus/Sonnet default `FullyCapable`, Haiku downgraded to `ToolCapable`/no-vision — unverifiable from Core). **Verdict: retain the used ones; prune or generate the rest.**

### src/DigitalBrain.Core/NeuronId.cs (1-9), TaskId.cs (1-10)
String-wrapper records. `NeuronId` allows any string incl. empty; `TaskId` adds a **two-way** implicit conversion — `TaskId x = anyString` compiles anywhere, erasing the type's purpose (ARCH-006). **Verdict: retain; drop the string→TaskId implicit conversion and add invariant checks.**

### src/DigitalBrain.Core/NeuronScope.cs (1-54)
`NeuronScope` (UserId + optional ThreadId from grain key), `PackConfigScopes`, `WorkspaceIds`. `WorkspaceIds.VectorCollection` sanitizes via `Safe()` which maps every non-alphanumeric char to `-` and lowercases: distinct principals (`john.doe` vs `john-doe` vs `JOHN_DOE`) map to the **same vector collection name** — cross-principal data collision in the semantic-memory namespace (SEC-003). `NeuronScope.TryParse` accepts any non-whitespace string. This family is the *legacy* identity scheme, parallel to `Runtime.GrainIds` whose `Segment()` uses collision-free base64url (ARCH-001). Tested (`NeuronScopeTests`). **Verdict: replace with the GrainIds scheme; fix Safe() collisions if kept.**

### src/DigitalBrain.Core/OAuthCallbackPaths.cs (1-126)
OAuth start/callback path policy: opaque flow-reference charset+length bounds, structural `ToolAction` validation (re-checked on every render — good), provider authorization URL allowlist (HTTPS, default port, no userinfo/fragment; `accounts.google.com` fixed path; `*.salesforce.com`/`*.site.com` fixed path). Correct fail-closed shape and well tested (`OAuthCallbackPathTests`, `AuthorizationFlowStartProxyTests`). Two notes: any `*.site.com` host passes (Salesforce-operated domain, but broader than `*.my.site.com` — SEC-006); hardcoding provider names/hosts in Core contradicts the "connectors are plugins" model — the allowlist is a deliberate central trust anchor, but its provider entries will grow inside Core with each connector (ARCH-003). **Verdict: retain; plan a registration mechanism for provider entries.**

### src/DigitalBrain.Core/ProtectedCheckpoint.cs (1-10)
Encrypted checkpoint record. Explicit `[Id]`s. Pairs with `INeuronStateProtector`. **Verdict: retain.**

### src/DigitalBrain.Core/RuntimeContracts.cs (1-426)
The v2 identity + session security core: `TenantId`/`WorkspaceId`/`PrincipalRef`/`RequestContext` (with grants set), `SessionAudiences.RequireFixedMcp` (fail-closed audience pinning), `RequestScope` (SHA-256 canonical scope id), `GrainIds` (base64url segments, prefix scope checks — collision-free tenant isolation), `SessionTokenService` (HMAC-SHA256, fixed-time compare, versioned claims, strict bounds on all claim lengths, issued-at skew window, action-capability tokens with domain separation + binding proof), `SessionPair`, `Redaction`, `CommandEnvelope`/`EventEnvelope`, `WorkflowState`, `CommitSeal`, `CapabilityIsolationGate`.
Assessment: `SessionTokenService` is carefully built — length caps before parse, fixed-time signature comparison, audience pinning, `Enum.IsDefined` checks, fail-closed on any malformed input. Findings: the parameterless-audience `TryValidate(token, out context)` overload accepts **any** audience (SEC-005 — no current callers outside Core, but a loaded footgun in a security type); `TryValidateCore` mints a fresh `CorrelationId` per validation (traceability quirk, not a defect); `SessionPair.AccessExpiresAt = default` sentinel (REL-006); `CommitSeal`/`CapabilityIsolationGate`/`EventEnvelope`/`WorkflowState` are test-only or dead (CLEAN-002); `Redaction.SafeSummary` only redacts when classification == Secret and truncates at 256 chars — callers must classify correctly (fail-open for misclassified data — Note); `PersistedActorSnapshot` and `SensitiveValue` have zero references (CLEAN-001). **Verdict: retain (load-bearing); remove the audience-agnostic overload; prune test-only/dead members.**

### src/DigitalBrain.Core/SchemaRegistry.cs (1-26)
Authoritative (type,version)→descriptor registry, conflict-rejecting, fail-closed `Require`. Registered in kernel + MCP hosts. Not thread-safe for post-startup `Register` (plain Dictionary) — fine if populated only at startup, but nothing enforces that. **Verdict: retain; document/enforce startup-only registration.**

### src/DigitalBrain.Core/Sdk/CommandResult.cs (1-14)
Process execution result with a clear non-zero-exit-is-data comment. Used by tools SDK neurons. **Verdict: retain.**

### src/DigitalBrain.Core/Sdk/IAgent.cs (1-40)
`IAgent : INeuron` with static virtual metadata members + `NeuronAgentMetadata.ReadFrom<TContract>()`. Verified: `IAgent` is implemented by SDK/tool contracts, but `NeuronAgentMetadata` itself has **zero external references** — the zero-reflection reader has no reader (CLEAN-001 item). Its only in-Core consumer would have been `IChartNeuron` (dead). The comment references a path outside the repo (`E:\DigitalBrainTech\IAW Core/...`) — provenance note that will rot. **Verdict: keep `IAgent` if the metadata pattern is imminent; otherwise delete `NeuronAgentMetadata` and the unused static members.**

### src/DigitalBrain.Core/SensitiveText.cs (1-31)
Regex secret redactor (assignments, JSON fields, auth headers, bearer tokens). **Zero references repo-wide** — a security control wired to nothing (CLEAN-001; also a trap: its patterns are easy to assume are active). **Verdict: delete or actually wire into logging/journal paths — an unused redactor is false comfort.**

### src/DigitalBrain.Core/Signals.cs (1-68)
`Signal` (name + `object?` prop bag riding `Synapse.Type`), `AskLlm` (+ pack-config routing), `ILlmResponderNeuron` (documented singleton key), `IIngressNeuron` (contract placed in Core to break a would-be circular reference — comment explains why), `UiSignals`, and `GoogleSignals`/`SalesforceSignals` name constants. The provider signal-name constants are Gmail/Salesforce vocabulary in Core (ARCH-003); each is referenced from exactly one integration — they could live with their connector. `Signal`'s untyped prop bag is the escape hatch from the typed-synapse model; acceptable as pack glue but it bypasses every typed invariant (Note). **Verdict: retain core parts; move provider signal names to their integrations.**

### src/DigitalBrain.Core/SurfaceAudience.cs (1-26)
Audience kinds + `PrincipalScope.Id` (hash-based principal audience id, kind-tagged). Sound. **Verdict: retain.**

### src/DigitalBrain.Core/SurfaceContentHash.cs (1-26)
SHA-256 over canonical payload+action-binding projection. Deterministic (anonymous-type property order fixed at compile time). **Verdict: retain.**

### src/DigitalBrain.Core/SurfaceEnvelopeWriter.cs (1-95)
Materializes stored surface records for a recipient: tenant/workspace demand, audience visibility (principal-kind + hashed id / workspace / public), expiry check, protocol version pinning, payload policy re-validation, per-recipient action-token minting, capability negotiation with typed `SurfaceCapabilityException`. Fail-closed at every step — good trust-boundary code. Minor: uses `DateTimeOffset.UtcNow` directly instead of `TimeProvider` (inconsistent with `SessionTokenService`/`McpRequestGuard`, hurts testability). **Verdict: retain.**

### src/DigitalBrain.Core/SurfaceFeedContracts.cs (1-47)
`StoredActionBinding` (token-free — correct), `StoredSurfaceRecord` (tokens deliberately absent, minted per delivery — correct), `FeedCursor`, `FeedPage`. `FeedCursor`/`FeedPage` have zero external references (CLEAN-001). **Verdict: retain records; delete or adopt FeedCursor/FeedPage.**

### src/DigitalBrain.Core/SurfacePayloadPolicy.cs (1-39)
Recursive forbidden-key scan (normalized alphanumeric-lowercase key match catches `access_token`, `accessToken`, `ACCESS-TOKEN`), depth bound 64, primitive-only leaves. Called from `SurfaceEnvelopeWriter` (in-Core). Key-name blacklisting is inherently incomplete (values are not scanned; a secret under key `"data"` passes) — a tripwire, not a guarantee; fine as defense-in-depth given `OperationFeedView` is credential-free by construction. **Verdict: retain.**

### src/DigitalBrain.Core/SynapsePayloadJson.cs (1-43)
JSON options that unwrap `object?` values into Dictionary/array/primitive graphs so Orleans never sees `JsonElement` in prop bags. Correct converter; int64-else-double number handling is standard. Comment "Orleans has no codec for JsonElement" is contradicted by `JsonElementSurrogate.cs` in the same assembly (FRAME-004). Tested (`SynapsePayloadJsonTests`). **Verdict: retain; fix comment.**

### src/DigitalBrain.Core/Synapses/CapabilitySynapses.cs (1-11)
`CapabilityInvocation` — zero external references (CLEAN-001). **Verdict: delete.**

### src/DigitalBrain.Core/Synapses/DbSynapses.cs (1-72)
DB schema model records (`DbSchemaModel/DbTable/DbColumn/DbForeignKey/DbIndex`) + `DbSchemaInspected`. Used by `SqliteSchemaInspector` + tests. Explicit `[Id]`s. Header comment documents a prior deletion (good hygiene). Generic, not provider-specific — appropriate, though arguably kernel-layer. **Verdict: retain (consider moving to a data-contract satellite).**

### src/DigitalBrain.Core/TabularDataSynapses.cs (1-13)
`TabularDataIngested` — zero external references repo-wide, including the kernel parser its own comment cites (CLEAN-001; the comment references `DigitalBrain.Kernel.TabularData.TabularDataParser`, which no longer references it). **Verdict: delete.**

### src/DigitalBrain.Core/Telemetry.cs (1-52)
`TraceContext`/`MetricPoint`/`ITelemetrySink`/`TelemetryBuffer` (bounded queues, label allowlist + redaction, dropped counter). Registered in kernel + MCP hosts. Design smell: the buffer is append-only with no drain/flush member — once `capacity` is reached every subsequent point increments `Dropped` forever; consumers can only snapshot via copying properties (REL-003). **Verdict: retain; add drain semantics or document snapshot-only intent.**

### src/DigitalBrain.Core/UiActionContracts.cs (1-22)
`ActionSubmission`, `ActionRejection` enum (a good, complete rejection taxonomy), `ActionRejectedException` deriving `UnauthorizedAccessException` with a generic message (no detail leak). **Verdict: retain.**

### src/DigitalBrain.Core/UiProtocol.cs (1-11)
Protocol/schema version constants + action-token/surface lifetimes. Single source of truth used across SessionTokenService, surface writer, payload builders. **Verdict: retain.**

### src/DigitalBrain.Core/CapabilityProfiles.cs (1-3)
`RuntimeProfile` enum (Development/Test/Production) — filename says "CapabilityProfiles", content is a runtime-profile enum (naming mismatch). Used in 7 files. **Verdict: retain; rename file.**

---

## Answers to subsystem questions

**1. Is `Synapse` a coherent base contract, and are the Orleans annotations correct?**
The base `Synapse` record itself is coherent and correctly annotated: explicit `[property: Id(0..5)]` on constructor parameters, `[Id(6)]/[Id(7)]` on body properties (body members have a separate id space per Orleans docs), `[GenerateSerializer]` + globally-unique `[Alias]` on every serializable type, `[Alias]` on grain interfaces and methods, and inheritance-level id scoping used correctly. Verified against the official Orleans serialization docs: records' primary-constructor parameters get **implicit ids by declaration order**, which is what the ~40 un-annotated derived records rely on. That is *valid* but **order-fragile for journaled data** — inserting or reordering a parameter re-maps fields silently where types are compatible (many records are all-strings), corrupting replay without an error (REL-001). The convention is inconsistent: `SelfEvolution.cs`, `Automations.cs`, `CodeFoundrySynapses.cs`, `DbSynapses.cs` annotate everything explicitly; `Synapse.cs`, `Experience.cs`, `Signals.cs` and others mostly don't. Cancellation-token usage in grain interfaces matches the documented Orleans 9+ pattern. Aliases were spot-checked for uniqueness; no duplicates found. (Context7 was quota-blocked; verification used Microsoft Learn Orleans 10 docs.)

**2. Does `SelfEvolution.cs` define a coherent propose→decide→apply→rollback vocabulary?**
The verb set is coherent and the outcome events (`Pending/Rejected/Expired/DecisionRecorded/DecisionRejected/ApplyResult/RollbackRequired`) give the journal a complete narrative. Gaps at the type level: (a) a `SelfEvolutionDecision` references only `ProposalId` — nothing binds the decision to the proposal *content*, so the contract cannot prove the approver saw the `ProposedChange` that gets applied (PROD-001); (b) `ExpiresAt` is optional with no default bound, so unbounded pending proposals are representable and expiry is enforcement-by-convention (PROD-002); (c) `Risk` and `RequiresHumanApproval` are proposer-asserted fields — the risk tier does not derive from `ApplyVia`/`Scope`, so a proposer can under-state risk (PROD-003); (d) `Origin` and `DecidedBy` are untyped strings with no tenant/principal linkage — the rail vocabulary predates the v2 `PrincipalRef` and never adopted it; (e) `RollbackPlan` is prose; only the Foundry path has real checkpoint ids (PROD-004). Separately, `DurableInoContracts` defines a second approval vocabulary (`ApprovalRecord`/`EffectRecord`) for INO mutations — two governance vocabularies for "human approved an effect" that must be kept semantically aligned by hand.

**3. Are `Models/` records domain-appropriate; do provider concerns leak into Core?**
`Models/` itself is clean: provider ids are neutral strings, marker classes are inert metadata, and the registry/descriptor design is provider-agnostic. The real provider leakage is elsewhere: `SalesforceOAuthCallback(+Result)` records (dead) in `Synapse.cs`, `GoogleSignals`/`SalesforceSignals` constants in `Signals.cs`, and google/salesforce hosts+paths hardcoded in `OAuthCallbackPaths.cs` (ARCH-003). The OAuth URL allowlist is defensible as a deliberate central trust anchor, but the pattern means every new connector edits Core. Within `Models/`, the concern is inert drift: hand-maintained capability flags and model ids with no validation, and several markers with zero references (`NomicEmbedText` fully dead).

**4. Dead, speculative, or duplicated type definitions?**
Substantial. Verified-dead (zero references outside Core across `src`, `tests`, `app`, `hosts`, `integrations`, `tools`): the `Synapse.cs` blocks listed in its per-file section, `CapabilityInvocation`, `TabularDataIngested`, `GrpcAuthentication`, `SensitiveText`, `NeuronAgentMetadata`, `FeedCursor`/`FeedPage`, `PersistedActorSnapshot`, `SensitiveValue`, `NomicEmbedText`, and most of `McpContracts.cs`. Test-only production types: `ModelRouter` family, `DeploymentPreviewer` family, `CommitSeal`, `CapabilityIsolationGate`, `EventEnvelope`. Duplicated authority: legacy vs v2 identity (ARCH-001), two model-selection systems (ARCH-005), two approval vocabularies (PROD-001), `InoConversationStates` strings vs `InoOperationPhase` enum with two hand-maintained mappings. Full enumeration in CLEAN-001/CLEAN-002. This is well over the repo's own 10%-deletion target.

**5. Does Core impose the right tenant/principal invariants on synapses?**
No. `Synapse` carries `Sender`/`Receiver` `NeuronId`s (nullable, unvalidated strings) and no tenant/workspace/principal at all. The v2 world (`RequestContext`, `TenantId`, `PrincipalRef`, `GrainIds` scope prefixes, `SurfaceEnvelopeWriter` demand checks) imposes strong identity invariants — but it lives *beside*, not *under*, the synapse rail. On the synapse rail, identity appears only as optional `string UserId = "anonymous"` parameters on a few messages (SEC-004), and tenant isolation for legacy-scoped grains rests on `NeuronScope`/`WorkspaceIds` string conventions with a collision-prone sanitizer (SEC-003). Any neuron-to-neuron message is tenant-blind by construction; isolation depends entirely on grain-key discipline at call sites. This is the single largest architectural gap in Core (ARCH-001/ARCH-002).

---

## Findings

### ARCH-001: Two parallel identity/scoping systems coexist in Core with no bridge
- **Severity**: High
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/Synapse.cs:64-69` (`UserId` + `Anonymous`), `src/DigitalBrain.Core/NeuronScope.cs:3-46` (`NeuronScope`, `WorkspaceIds`, `PackConfigScopes` — raw string keys) vs `src/DigitalBrain.Core/RuntimeContracts.cs:12-99` (`TenantId`, `WorkspaceId`, `PrincipalRef`, `RequestContext`, `GrainIds` with base64url segments and prefix scope checks).
- **Current behavior**: Legacy grains key on `user/thread` strings via `NeuronScope`; v2 grains key on `v2/<tenant>/<workspace>/...` via `GrainIds`. The two id spaces never reference each other; `UserId` has no tenant. (FACT)
- **Why it matters**: (INFERENCE) Every capability, journal, and config store keyed by the legacy scheme is single-tenant by construction; tenant onboarding requires migrating grain keys, journals, and vector collections. Duplicated authority also means isolation checks exist only on the v2 half.
- **OS/product consequence**: Breaks the "auth tenant-isolated" OS invariant for the entire synapse rail; INO/v2 surfaces are isolated while neuron journals are not.
- **Recommendation**: (PROPOSAL) Declare `GrainIds`/`RequestContext` the only identity scheme; migrate `NeuronScope`/`WorkspaceIds`/`PackConfigScopes` consumers onto it; delete the legacy family.
- **Deletion/simplification opportunity**: yes — `NeuronScope.cs` largely deletable after migration.
- **Dependencies**: ARCH-002, SEC-003, SEC-004; kernel + packs subsystems.
- **Tests/measurements required**: cross-tenant grain-key collision tests; journal replay after key migration.
- **Effort**: L
- **Migration/rollback concern**: grain-key migration touches persisted journals — needs a replay/rename plan.

### ARCH-002: `Synapse` carries no tenant/workspace/principal; identity is per-message, optional, and stringly-typed
- **Severity**: High
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/Synapse.cs:11-31` (base record fields), `:44-48`, `:228-239`, `:310-316` (`string UserId = "anonymous"`, `string? SessionId = null` on `ExperienceUsed`, `RunTask`, `CancelTask`, `VisualizeDataRequest`).
- **Current behavior**: A synapse can be constructed and delivered with no principal at all; the messages that do carry identity default it to `"anonymous"`. (FACT)
- **Why it matters**: (INFERENCE) Handlers cannot enforce "who asked" at the message boundary; audit lineage (`Stamp`) records causation but not actor; every authorization decision must be reconstructed out-of-band.
- **OS/product consequence**: The journaled record of "what happened" lacks "on whose behalf" — undermines the approval/journal trust story for anything flowing over the synapse rail.
- **Recommendation**: (PROPOSAL) Add a mandatory actor scope (tenant+principal reference, or the `RequestScope.Id` hash) to `Synapse` as a new trailing `[Id(8)]` body property (additive per Orleans versioning rules), stamped by the kernel ingress, with `"anonymous"` eliminated as a default.
- **Deletion/simplification opportunity**: yes — removes per-message UserId/SessionId parameters.
- **Dependencies**: ARCH-001, SEC-004; kernel `Neuron.FireAsync` stamping.
- **Tests/measurements required**: replay of pre-change journals (missing field must decode as null); handler-level actor assertion tests.
- **Effort**: M
- **Migration/rollback concern**: additive field is rolling-safe; consumers must tolerate null actor on historical synapses.

### ARCH-003: Provider (Google/Salesforce) vocabulary hardcoded in Core
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/Signals.cs:15-31` (`GoogleSignals`, `SalesforceSignals`), `src/DigitalBrain.Core/Synapse.cs:354-370` (`SalesforceOAuthCallback(+Result)` — dead), `src/DigitalBrain.Core/OAuthCallbackPaths.cs:7-12,104-118` (google/salesforce paths and host allowlists).
- **Current behavior**: Core names two concrete providers in three files; each signal-constant class is consumed by exactly one integration. (FACT)
- **Why it matters**: (INFERENCE) Every new connector must edit Core, inverting the plugin model; dead Salesforce records additionally freeze provider shapes into the serialization contract.
- **OS/product consequence**: Contradicts "Gmail/Salesforce are the first two connectors of a general model — provider concerns must not leak into the kernel".
- **Recommendation**: (PROPOSAL) Move signal-name constants to their integrations; delete the dead callback records; turn the OAuth URL allowlist into data registered by connectors and validated by a Core policy engine (charset/HTTPS/no-fragment rules stay in Core).
- **Deletion/simplification opportunity**: yes — dead records + relocated constants.
- **Dependencies**: CLEAN-001; connectors subsystem.
- **Tests/measurements required**: existing `OAuthCallbackPathTests` re-pointed at the registration API; unknown providers must stay fail-closed.
- **Effort**: M
- **Migration/rollback concern**: none for dead types; allowlist refactor must remain fail-closed.

### ARCH-004: `Synapse.cs` is a grab-bag god-file of ~40 unrelated contracts
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/Synapse.cs:1-371` — auth, sessions, tasks, LLM, checkpoints, charts, NuGet/architect loops, kernel self-update, Salesforce OAuth in one file.
- **Current behavior**: One file owns most of the legacy contract surface with no grouping. (FACT)
- **Why it matters**: (INFERENCE) Ownership and review blur; dead code hides (proven — most dead types live here); merge conflicts concentrate.
- **OS/product consequence**: Slows every audit/change of the message vocabulary — the OS's ABI.
- **Recommendation**: (PROPOSAL) After the CLEAN-001 purge, split the remainder by domain (auth/session, task protocol, checkpointing, LLM).
- **Deletion/simplification opportunity**: yes — the purge is the larger half of the fix.
- **Dependencies**: CLEAN-001.
- **Tests/measurements required**: build + alias-stability check (aliases keep wire names stable across file moves — verified property of `[Alias]` per Orleans docs).
- **Effort**: S
- **Migration/rollback concern**: none (aliases pin wire identity).

### ARCH-005: Two model-selection authorities; one is test-only
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/ModelRouting.cs:12-33` (`ModelRouter` — referenced only by `tests/DigitalBrain.Tests/Runtime/ContractsTests.cs`) vs `src/DigitalBrain.Core/Models/DigitalBrainModelCatalog.cs:82-144` (production registry).
- **Current behavior**: `ModelRouter` implements privacy/residency/cost policy selection that no production code calls; the registry does role-based selection with none of those constraints. (FACT)
- **Why it matters**: (INFERENCE) The policy dimensions the OS story needs (privacy class, residency, cost budget) exist only in dead code; readers assume they are enforced.
- **OS/product consequence**: Model governance (tenant policy → model choice) is aspirational, not wired.
- **Recommendation**: (PROPOSAL) Either fold policy filtering into `DigitalBrainModelRegistry` selection and delete `ModelRouting.cs`, or delete it outright until policy routing is scheduled.
- **Deletion/simplification opportunity**: yes — one file.
- **Dependencies**: CLEAN-002.
- **Tests/measurements required**: registry selection tests incl. policy dimensions if merged.
- **Effort**: S
- **Migration/rollback concern**: none.

### ARCH-006: `TaskId` two-way implicit string conversion erases the type
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/TaskId.cs:7-8` (`implicit operator string` and `implicit operator TaskId(string)`); contrast `src/DigitalBrain.Core/NeuronId.cs:7` (one-way only).
- **Current behavior**: Any string silently becomes a `TaskId` anywhere one is expected; neither type validates non-emptiness. (FACT)
- **Why it matters**: (INFERENCE) The wrapper provides no protection against the key mixups it exists to prevent.
- **OS/product consequence**: Weak typing on the recoverable-task primitive's identity.
- **Recommendation**: (PROPOSAL) Remove the string→TaskId conversion; add `ArgumentException.ThrowIfNullOrWhiteSpace` in both types.
- **Deletion/simplification opportunity**: yes (one operator).
- **Dependencies**: none. **Tests/measurements required**: compile-time check of call sites. **Effort**: S. **Migration/rollback concern**: none (compile-time only).

### ARCH-007: `McpContracts.cs` is a speculative port layer; its one live type is consumed from an integration
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/McpContracts.cs:11-21` (`IQueryPort`/`ICommandPort` — zero implementations repo-wide); `Page<T>` used only by `integrations/DigitalBrain.Salesforce/SalesforceApiClient.cs`; `Capability` once in `src/DigitalBrain.Kernel/AutomationDefinitionApplyHandler.cs`.
- **Current behavior**: Unimplemented interfaces plus records used far from their intended MCP context. (FACT)
- **Why it matters**: (INFERENCE) Dead ports suggest an architecture that exists on paper; `Page<T>`'s stray use couples the Salesforce integration to Core.Runtime for a generic pagination shape.
- **OS/product consequence**: Misleading map of the MCP boundary.
- **Recommendation**: (PROPOSAL) Delete the unused members; move `Page<T>` beside the paging code that uses it, or implement the ports.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: CLEAN-001. **Tests/measurements required**: build. **Effort**: S. **Migration/rollback concern**: none.

### SEC-001: `LoginRequest` carries a plaintext password in the journaled message vocabulary
- **Severity**: High
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/Synapse.cs:71-76` — `LoginRequest(string Username, string Password, string ClientId = "flutter") : Synapse(...)`.
- **Current behavior**: The credential is a `Synapse` — the type family that `INeuron` journals and exposes via `GetTimelineAsync` (`src/DigitalBrain.Core/INeuron.cs:8-17`). Whether it is actually persisted depends on the kernel handler (`UserSessionNeuron`), but the *contract* invites it. (FACT for the type shape; the persistence path is kernel-side.)
- **Why it matters**: (INFERENCE) Any handler that journals its incoming synapses (the default neuron behavior) durably stores plaintext passwords, retrievable by anything able to call the grain's timeline API.
- **OS/product consequence**: Violates fail-closed auth handling; a journal read becomes credential disclosure.
- **Recommendation**: (PROPOSAL) Make login a non-synapse request/response DTO on a dedicated grain method, or a field explicitly excluded from journaling; add a contract test asserting no `Synapse` subtype has a password-named parameter.
- **Deletion/simplification opportunity**: yes — with SEC-002.
- **Dependencies**: kernel `UserSessionNeuron`; TEST-001.
- **Tests/measurements required**: timeline of a session grain after login contains no credential material.
- **Effort**: M
- **Migration/rollback concern**: wire change for the UI login path; stage behind protocol version.

### SEC-002: `LocalUserRegistered` puts password hash + salt on the synapse timeline
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/Synapse.cs:123-131` — `PasswordHashBase64`, `PasswordSaltBase64` parameters on a `Synapse`.
- **Current behavior**: Password verifier material travels and (in journaling neurons) persists as ordinary timeline data readable via `GetTimelineAsync`. (FACT for the contract)
- **Why it matters**: (INFERENCE) Hash+salt exposure enables offline cracking; timeline read access is far broader than credential-store access should be.
- **OS/product consequence**: Same trust-boundary erosion as SEC-001.
- **Recommendation**: (PROPOSAL) Store verifier material only in protected grain state (`INeuronStateProtector` exists for exactly this); emit a credential-free `LocalUserRegistered(UserId, Username, Roles)` event.
- **Deletion/simplification opportunity**: yes (two parameters).
- **Dependencies**: SEC-001, kernel auth. **Tests/measurements required**: as SEC-001. **Effort**: S-M. **Migration/rollback concern**: existing journaled events retain the material — needs a scrub/rotation note.

### SEC-003: `WorkspaceIds.VectorCollection` sanitizer can collide distinct principals into one collection
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/NeuronScope.cs:36-45` — `Safe()` lowercases and maps every non-alphanumeric char to `-`; collection name is `user:{Safe(userId)}:workspace:{Safe(workspace)}:{Safe(collection)}`.
- **Current behavior**: `john.doe`, `john-doe`, `John_Doe` all yield `john-doe`; two distinct user ids can share one vector collection. (FACT)
- **Why it matters**: (INFERENCE) Cross-principal read/write of semantic memory if user-id formats ever allow punctuation variants (emails do).
- **OS/product consequence**: Tenant/principal isolation hole in the memory subsystem's namespace.
- **Recommendation**: (PROPOSAL) Use a collision-free encoding (base64url or hash, as `GrainIds.Segment`/`PrincipalScope.Id` already do).
- **Deletion/simplification opportunity**: yes — reuse `GrainIds.Segment`.
- **Dependencies**: ARCH-001; kernel memory neuron.
- **Tests/measurements required**: property test: distinct inputs ⇒ distinct collection names.
- **Effort**: S
- **Migration/rollback concern**: renames existing collections — migration map needed.

### SEC-004: Anonymous-by-default principal baked into contracts
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/Synapse.cs:68` (`UserId.Anonymous`), `:47`, `:231`, `:238`, `:315` (`string UserId = "anonymous"` defaults).
- **Current behavior**: Omitting the argument silently attributes the action to `anonymous`. (FACT)
- **Why it matters**: (INFERENCE) Fail-open attribution: forgetting a parameter produces an unattributed-but-valid action rather than a compile/runtime error.
- **OS/product consequence**: Weakens audit and entitlement checks on the task/experience paths.
- **Recommendation**: (PROPOSAL) Remove the defaults (make identity required), or type the parameter as `UserId` with explicit `UserId.Anonymous` only permitted in the Development runtime profile.
- **Deletion/simplification opportunity**: yes (defaults removed).
- **Dependencies**: ARCH-002. **Tests/measurements required**: compile-time. **Effort**: S. **Migration/rollback concern**: none.

### SEC-005: Audience-agnostic `TryValidate` overload on `SessionTokenService`
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/RuntimeContracts.cs:152-153` — `TryValidate(string token, out RequestContext)` calls the core with `expectedAudience: null`, accepting a token minted for any audience.
- **Current behavior**: No callers outside Core today (verified); the audience-pinned overloads are used. (FACT)
- **Why it matters**: (INFERENCE) A future caller can silently accept UI tokens on the MCP surface (audience confusion), defeating `SessionAudiences.RequireFixedMcp`.
- **OS/product consequence**: Latent cross-surface session acceptance.
- **Recommendation**: (PROPOSAL) Delete the overload; make audience mandatory.
- **Deletion/simplification opportunity**: yes. **Dependencies**: none. **Tests/measurements required**: compile. **Effort**: S. **Migration/rollback concern**: none.

### SEC-006: Salesforce OAuth host allowlist covers all of `*.site.com`
- **Severity**: Note
- **Confidence**: Medium
- **Evidence**: `src/DigitalBrain.Core/OAuthCallbackPaths.cs:116-118` — `host.EndsWith(".salesforce.com") || host.EndsWith(".site.com")`.
- **Current behavior**: Any HTTPS host under `site.com` with path `/services/oauth2/authorize` is an allowed authorization URL. (FACT)
- **Why it matters**: (INFERENCE) `site.com` is Salesforce-operated, but Experience Cloud tenants control subdomain content under `*.my.site.com`; the allowlist trusts tenant-controlled hosts as OAuth authorization endpoints — residual phishing surface if a hostile SF tenant serves a lookalike consent page. (Could not verify current Salesforce domain policy against vendor docs — Context7 quota; flagged at Medium confidence.)
- **OS/product consequence**: OAuth trust anchor slightly wider than necessary.
- **Recommendation**: (PROPOSAL) Narrow to `login.salesforce.com`, `test.salesforce.com`, and the configured MyDomain host.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: connectors/salesforce. **Tests/measurements required**: allowlist unit tests incl. sandbox/MyDomain variants. **Effort**: S. **Migration/rollback concern**: sandbox/MyDomain flows must stay reachable.

### PROD-001: Self-evolution decisions are not bound to proposal content (approve-what-you-saw gap); duplicate approval vocabularies
- **Severity**: High
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/SelfEvolution.cs:82-86` — `SelfEvolutionDecision(ProposalId, Approved, DecidedBy, Reason)`; no hash/version of `ProposedChange`. Contrast `src/DigitalBrain.Core/DurableInoContracts.cs:26-36` where `AcceptedCommand` carries `InputHash`, and `:45-55` where `ApprovalRecord` versions its state.
- **Current behavior**: The contract permits approving a proposal id whose content could differ from what was displayed (nothing at the type level ties decision → content). INO effects use a second, separate approval vocabulary (`ApprovalRecord`/`EffectRecord`). (FACT)
- **Why it matters**: (INFERENCE) The rail's core promise — a human approved *this* change — is enforceable only by kernel discipline, not by the contract; two approval vocabularies invite semantic drift between INO-effect approval and self-evolution approval.
- **OS/product consequence**: Weakens the single-governed-rail invariant, the product's north star.
- **Recommendation**: (PROPOSAL) Add `ProposalContentHash` (SHA-256 of Scope+ProposedChange+ApplyVia+Risk) to `SelfEvolutionDecision` and require a match in the rail; longer term unify with `ApprovalRecord`.
- **Deletion/simplification opportunity**: yes (eventual vocabulary merge).
- **Dependencies**: kernel SelfEvolution grain; ino subsystem.
- **Tests/measurements required**: decision-with-stale-hash rejected; journal replay unaffected (additive field).
- **Effort**: M
- **Migration/rollback concern**: additive field; old decisions in journals have null hash — treat as legacy-verified.

### PROD-002: Proposal expiry is optional and unbounded at the type level
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/SelfEvolution.cs:35` — `DateTimeOffset? ExpiresAt = null`; `SelfEvolutionProposalExpired` (`:69-73`) exists but nothing in the contract forces an expiry.
- **Current behavior**: A proposal with `ExpiresAt = null` is representable and, per the vocabulary, never expires. (FACT)
- **Why it matters**: (INFERENCE) Pending-approval state can grow without bound, and a months-old approval can apply a change whose context is gone (stale-state apply hazard).
- **OS/product consequence**: Unbounded proposal state in the governance rail; approve-late hazards.
- **Recommendation**: (PROPOSAL) Make `ExpiresAt` required, or define a documented default TTL the rail stamps on ingest and records in `SelfEvolutionProposalPending`.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: PROD-001; kernel rail. **Tests/measurements required**: expiry enforcement + replay of null-expiry historical proposals. **Effort**: S. **Migration/rollback concern**: additive semantics for historical records.

### PROD-003: Risk tier and human-approval requirement are proposer-asserted
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/SelfEvolution.cs:9-17,31-32` — `SelfEvolutionRisk` (3 values) and `bool RequiresHumanApproval` are plain proposal fields; `ApplyVia` (`:34,42-48`) is a free string with 4 known constants.
- **Current behavior**: The proposer chooses its own risk tier and whether human approval is required; nothing in the vocabulary derives risk from `ApplyVia`/`Scope`. (FACT)
- **Why it matters**: (INFERENCE) A compromised or buggy proposer (Ino, a pack) can mark `FoundryDeploy` as `Risk=None, RequiresHumanApproval=false`; safety then depends wholly on kernel-side re-derivation, which the contract neither requires nor expresses.
- **OS/product consequence**: Governance depends on unstated kernel discipline instead of the contract.
- **Recommendation**: (PROPOSAL) Remove `RequiresHumanApproval` from the proposal (rail computes it from an ApplyVia→policy table) or rename to `ProposerRequestsApproval`; document that `Risk` is advisory and add rail-computed risk to `SelfEvolutionProposalPending`.
- **Deletion/simplification opportunity**: yes (one bool).
- **Dependencies**: PROD-001; kernel rail. **Tests/measurements required**: rail overrides proposer-asserted low risk for deploy-tier ApplyVia. **Effort**: S-M. **Migration/rollback concern**: none (journal-additive).

### PROD-004: Rollback is free-text except on the Foundry path
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/SelfEvolution.cs:33` (`string RollbackPlan`), `:107-112` (`RollbackCheckpointId` optional on apply result), vs `src/DigitalBrain.Core/CodeFoundrySynapses.cs:66-96` (real checkpoint ids).
- **Current behavior**: Only foundry applies produce machine-actionable rollback references. (FACT)
- **Why it matters**: (INFERENCE) "Rollback-capable" is prose for automation/config applies.
- **OS/product consequence**: The rail's recoverability guarantee is uneven across apply kinds.
- **Recommendation**: (PROPOSAL) Require apply handlers to emit a checkpoint/inverse-operation reference in `SelfEvolutionApplyResult`; keep prose as description only.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: kernel apply handlers. **Tests/measurements required**: rollback round-trip per ApplyVia. **Effort**: M. **Migration/rollback concern**: none.

### REL-001: ~40 journaled synapse records rely on implicit positional Orleans field ids; annotation convention is inconsistent
- **Severity**: High
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/Synapse.cs:36-348` (most records un-annotated, e.g. `LoginSucceeded`, `MemoryStored` — five consecutive string/optional-string params), `src/DigitalBrain.Core/Experience.cs:5-18`, `src/DigitalBrain.Core/Signals.cs:35-49` vs fully-annotated `SelfEvolution.cs`/`Automations.cs`/`CodeFoundrySynapses.cs`/`Synapses/DbSynapses.cs`. Orleans docs ("Serialization in Orleans", learn.microsoft.com): record primary-constructor members "have implicit IDs by default… you cannot change the parameter order for an already deployed type".
- **Current behavior**: Serialization is correct today; ids are positional. Journals persist these records (`src/DigitalBrain.Core/SelfEvolution.cs:6` — "downstream journals persist these synapses"). (FACT)
- **Why it matters**: (INFERENCE) Inserting/reordering a parameter in an un-annotated record silently re-maps stored fields; with many all-string records the decode *succeeds with wrong data* — journal replay corruption with no error. The mixed convention means an editor cannot tell which records are order-frozen.
- **OS/product consequence**: Durable journal replay — the OS's recovery primitive — is one innocent refactor away from silent corruption.
- **Recommendation**: (PROPOSAL) Adopt explicit `[property: Id(n)]` on every persisted synapse (mechanical, ids matching current positions so the wire format is unchanged); add an analyzer or contract-freeze test (TEST-001).
- **Deletion/simplification opportunity**: no (adds annotations), but pairs with CLEAN-001 to shrink the set first.
- **Dependencies**: CLEAN-001, TEST-001.
- **Tests/measurements required**: golden-bytes round-trip per record type.
- **Effort**: M
- **Migration/rollback concern**: none if ids mirror current positions exactly.

### REL-002: `= null!` list defaults on journaled automation records
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/Automations.cs:13,24,34-35` — `IReadOnlyList<string> DeclaredEmits = null!`, `IReadOnlyList<RegisterScript> Scripts = null!`, `IReadOnlyList<RegisterReaction> Reactions = null!`.
- **Current behavior**: Callers omitting the argument produce records whose non-nullable list properties are null; any `foreach`/LINQ downstream throws NRE. (FACT)
- **Why it matters**: (INFERENCE) A journaled message can be legitimately constructed in an invalid state; replay of such a record re-throws forever (poison message).
- **OS/product consequence**: Automation journal replay reliability.
- **Recommendation**: (PROPOSAL) Default to `[]` or make the parameters required.
- **Deletion/simplification opportunity**: yes (simpler defaults).
- **Dependencies**: kernel automation grain. **Tests/measurements required**: construct-with-defaults + handler smoke test. **Effort**: S. **Migration/rollback concern**: none.

### REL-003: `TelemetryBuffer` has no drain — after capacity, everything is dropped forever
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/Telemetry.cs:23-44` — enqueue-if-below-capacity, increment `Dropped` otherwise; no dequeue/flush member exists; `Metrics`/`Traces` copy without clearing.
- **Current behavior**: The 2048-entry queues fill once and then silently reject all subsequent telemetry. Registered as the `ITelemetrySink` in kernel and MCP hosts. (FACT)
- **Why it matters**: (INFERENCE) Long-running silos lose all custom metrics/traces after warm-up.
- **OS/product consequence**: Self-awareness/diagnosis primitives degrade silently.
- **Recommendation**: (PROPOSAL) Add drain methods and have an exporter drain; or replace with OpenTelemetry instruments and delete this type.
- **Deletion/simplification opportunity**: yes (possibly the whole type).
- **Dependencies**: kernel/MCP hosting. **Tests/measurements required**: fill-past-capacity then drain restores acceptance. **Effort**: S. **Migration/rollback concern**: none.

### REL-004: `McpRequestGuard` eviction/creation races
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/McpGuard.cs:34-42` (count check then `GetOrAdd` non-atomic; window fetched via `TryGetValue` can be evicted by `RemoveIdle` before `lock (window)` at `:41-52`).
- **Current behavior**: Rare interleavings allow counting against an orphaned window (that principal's limits effectively reset) or slightly exceeding `MaximumTrackedPrincipals`. (FACT)
- **Why it matters**: (INFERENCE) Marginal rate-limit under-enforcement under adversarial timing; requires a 2-minute-idle window plus a precise race — not exploitable at scale.
- **OS/product consequence**: MCP transport abuse margin slightly wider than configured.
- **Recommendation**: (PROPOSAL) Re-fetch via `GetOrAdd` after locking, or tag windows with an `Evicted` flag checked under the lock.
- **Deletion/simplification opportunity**: no. **Dependencies**: none. **Tests/measurements required**: concurrent stress test with fake TimeProvider. **Effort**: S. **Migration/rollback concern**: none.

### REL-005: `ConversationSurfacePayload.TurnKey` collides for repeated (CommandId, Role) pairs
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/ConversationSurfacePayload.cs:163-168` — key is SHA-256 of `CommandId + "\0" + Role`.
- **Current behavior**: Two turns from the same command with the same role (e.g. multi-part assistant output) get identical `turnKey`s in the feed payload. (FACT)
- **Why it matters**: (INFERENCE) Clients keying UI list items on `turnKey` will drop/merge turns.
- **OS/product consequence**: Conversation feed fidelity.
- **Recommendation**: (PROPOSAL) Include the turn's index or text hash in the key material.
- **Deletion/simplification opportunity**: no. **Dependencies**: flutter-ui rendering. **Tests/measurements required**: duplicate-role turn snapshot test. **Effort**: S. **Migration/rollback concern**: key change re-renders the feed once.

### REL-006: `SessionPair.AccessExpiresAt = default` sentinel
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/RuntimeContracts.cs:389-394` — `DateTimeOffset AccessExpiresAt = default`.
- **Current behavior**: An unset access-token expiry is year-0001 rather than absent. (FACT)
- **Why it matters**: (INFERENCE) `now < AccessExpiresAt` reads unset as always-expired (safe), but code reading it as "no expiry" would fail open; the ambiguity invites both readings.
- **OS/product consequence**: Session refresh path clarity.
- **Recommendation**: (PROPOSAL) Make it `DateTimeOffset?` or required.
- **Deletion/simplification opportunity**: no. **Dependencies**: MCP session authority. **Tests/measurements required**: token refresh path. **Effort**: S. **Migration/rollback concern**: none.

### PERF-001: `Checkpoint` and timeline APIs move entire journals as single messages
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/Synapse.cs:196-198` (`Checkpoint(…, IReadOnlyList<Synapse> Snapshot, …)` — itself a `Synapse`), `src/DigitalBrain.Core/INeuron.cs:8-23` (all timeline queries return full `IReadOnlyList<Synapse>`, no paging).
- **Current behavior**: A checkpoint serializes the whole snapshot in one Orleans message; timelines return unbounded lists. (FACT)
- **Why it matters**: (INFERENCE) Long-lived neurons make checkpoints/timeline reads O(journal) in memory and message size; Orleans message-size limits will eventually reject them — on exactly the busiest neurons.
- **OS/product consequence**: The time-travel/simulation primitive fails at scale.
- **Recommendation**: (PROPOSAL) Page timeline APIs (cursor + limit); reference checkpoints by id (`ProtectedCheckpoint` blob in storage) instead of embedding the list in a synapse.
- **Deletion/simplification opportunity**: yes — `Checkpoint`-as-Synapse likely simplifiable to a stored artifact + reference event.
- **Dependencies**: kernel Neuron implementation. **Tests/measurements required**: checkpoint of a 100k-entry journal. **Effort**: M-L. **Migration/rollback concern**: interface change across all neurons.

### PERF-002: Payload trim loop re-serializes per removed message (accepted)
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/ConversationSurfacePayload.cs:64-71`.
- **Current behavior**: Up to 16 full serializations + UTF-8 counts when over budget. Bounded and small. (FACT)
- **Why it matters**: (INFERENCE) Negligible; recorded to show it was considered and deliberately not flagged as a defect.
- **OS/product consequence**: none.
- **Recommendation**: (PROPOSAL) none needed.
- **Deletion/simplification opportunity**: no. **Dependencies**: none. **Tests/measurements required**: existing tests. **Effort**: —. **Migration/rollback concern**: none.

### FRAME-001: Orleans annotation usage verified correct (positive finding)
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/Synapse.cs:5-31` (explicit ids on base ctor + body members), `src/DigitalBrain.Core/JsonElementSurrogate.cs:5-24` (documented surrogate pattern), grain interfaces (`src/DigitalBrain.Core/INeuron.cs`, `Automations.cs:89-114`) with `[Alias]` on interfaces and methods and trailing optional `CancellationToken`s — all matching current Microsoft Learn Orleans guidance (records' implicit ctor-param ids, per-inheritance-level id scoping, globally-unique aliases, native CancellationToken since Orleans 9).
- **Current behavior**: Compiles and round-trips per documented semantics. (FACT)
- **Why it matters**: (INFERENCE) The framework layer is sound; the residual risk is versioning discipline (REL-001), not current correctness.
- **OS/product consequence**: none (supports the OS model).
- **Recommendation**: none beyond REL-001.
- **Deletion/simplification opportunity**: no. **Dependencies**: REL-001. **Tests/measurements required**: TEST-001. **Effort**: —. **Migration/rollback concern**: none.

### FRAME-002: "Pure stable layer" package pinned to preview/alpha Orleans line
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/DigitalBrain.Core.csproj:8-12` (IsPackable, "stable primitive layer"), `Directory.Packages.props:31-40` (Orleans 10.2.1-preview.1; Journaling 10.2.1-preview.1.alpha.1).
- **Current behavior**: The packable contract assembly's serialization behavior is defined by preview packages. (FACT)
- **Why it matters**: (INFERENCE) Preview serializer changes can alter wire behavior under the "stable" package; consumers pinning DigitalBrain.Core 0.3.0 inherit alpha-line risk silently.
- **OS/product consequence**: Packaged contract stability claim overstated.
- **Recommendation**: (PROPOSAL) Drop the stability claim or gate the package on stable Orleans; record the journaling-alpha dependency as a known risk.
- **Deletion/simplification opportunity**: no. **Dependencies**: repo-wide package pins. **Tests/measurements required**: none. **Effort**: S. **Migration/rollback concern**: none.

### FRAME-003: Undeclared direct dependency on `Microsoft.Extensions.Configuration`
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/Models/DigitalBrainModelRegistrySnapshot.cs:3` (`using Microsoft.Extensions.Configuration;`) with no matching PackageReference in `src/DigitalBrain.Core/DigitalBrain.Core.csproj:15-18` — resolves transitively through Orleans.
- **Current behavior**: Builds today via the transitive graph. (FACT)
- **Why it matters**: (INFERENCE) An Orleans dependency-graph change breaks Core's build; NuGet consumers of the package may not receive the assembly.
- **OS/product consequence**: Package consumers' build fragility.
- **Recommendation**: (PROPOSAL) Add the explicit `Microsoft.Extensions.Configuration.Abstractions` reference or move this reader to the hosting layer (it is host-config plumbing, arguably not a core abstraction).
- **Deletion/simplification opportunity**: possible move. **Dependencies**: none. **Tests/measurements required**: build. **Effort**: S. **Migration/rollback concern**: none.

### FRAME-004: Stale comment contradicts `JsonElementSurrogate`
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/SynapsePayloadJson.cs:7-10` — "Orleans has no codec for JsonElement" vs `src/DigitalBrain.Core/JsonElementSurrogate.cs:13-24` providing exactly that converter in the same assembly.
- **Current behavior**: Both mechanisms coexist; the comment misstates why the converter exists. (FACT)
- **Why it matters**: (INFERENCE) Misleading comments drive wrong design decisions (e.g. "fixing" prop bags to avoid a nonexistent limitation).
- **OS/product consequence**: none direct.
- **Recommendation**: (PROPOSAL) Reword: the surrogate covers typed `JsonElement` members; `SynapsePayloadJson` normalizes untyped `object?` bags.
- **Deletion/simplification opportunity**: no. **Dependencies**: none. **Tests/measurements required**: none. **Effort**: S. **Migration/rollback concern**: none.

### CLEAN-001: ~30 verified-dead types across 8+ files (large deletion opportunity)
- **Severity**: High
- **Confidence**: High
- **Evidence**: Repo-wide word-boundary reference search across `src`, `tests`, `app`, `hosts`, `integrations`, `tools` at commit `72400e3` found **zero references outside Core** for: in `src/DigitalBrain.Core/Synapse.cs` — `SystemLaunched`, `FixProposal`, `SimulationResult`, `ISystemStatus`, `NuGetCommand`, `NuGetResult`, `ArchitectRequest`, `ArchitectReport`, `ArchitectResult`, `ClosedLoopRequest`, `IClosedLoopNeuron`, `ClosedLoopCompleted`, `ContextUpdate`, `MemoryStored`, `FilterChanged`, `ChartCommand`, `ChartInteraction`, `IChartNeuron`, `WidgetTreeInspected`, `UIModificationProposed`, `SystemModificationProposed`, `SalesforceOAuthCallback`, `SalesforceOAuthCallbackResult`, `IUserGrain`, `UserProfile`; whole files `src/DigitalBrain.Core/GrpcAuthentication.cs`, `src/DigitalBrain.Core/SensitiveText.cs`, `src/DigitalBrain.Core/Synapses/CapabilitySynapses.cs`, `src/DigitalBrain.Core/TabularDataSynapses.cs`; plus `NeuronAgentMetadata` (`Sdk/IAgent.cs`), `FeedCursor`/`FeedPage` (`SurfaceFeedContracts.cs`), `PersistedActorSnapshot`, `SensitiveValue` (`RuntimeContracts.cs`), `IQueryPort`/`ICommandPort`/`OperationStatus`/`McpError`/`IdempotencyConflictException` (`McpContracts.cs`), `WorkflowState` (`RuntimeContracts.cs`), `src/DigitalBrain.Core/Models/Ollama/NomicEmbedText.cs`.
- **Current behavior**: Dead contract surface ships in the packable core and participates in serializer codegen. (FACT)
- **Why it matters**: (INFERENCE) Reading/auditing cost, false affordances (an unused redactor, an unused chart pipeline, an unused login-helper), codegen bloat, and frozen wire shapes for messages nobody sends.
- **OS/product consequence**: Directly violates the repo's own delete-first rule; obscures the real ABI of the OS.
- **Recommendation**: (PROPOSAL) Delete in one commit. Caution: removing types that *were previously emitted into journals* breaks deserialization of historical entries — scan stored journals (or accept environment reset in pre-GA) before deleting the Chart/Closed-loop/Architect families; the rest were never wired.
- **Deletion/simplification opportunity**: yes — this is the finding.
- **Dependencies**: ARCH-003, ARCH-004, ARCH-007, CLEAN-002.
- **Tests/measurements required**: full solution build + `dotnet test` from root; journal-replay smoke on an environment with historical data.
- **Effort**: M (verification is the work; deletion is trivial)
- **Migration/rollback concern**: deserializing a journal containing a deleted type fails — needs a historical-journal scan or tombstone types for previously-emitted synapses.

### CLEAN-002: Test-only production machinery in Core
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: Only caller is `tests/DigitalBrain.Tests/Runtime/ContractsTests.cs` for: `src/DigitalBrain.Core/ModelRouting.cs` (whole file), `src/DigitalBrain.Core/DeploymentPreview.cs` (whole file), `CommitSeal`, `CapabilityIsolationGate`, `EventEnvelope` (`RuntimeContracts.cs:406-426`), `MetricPoint`-as-public-API, `Redaction` (in-Core use plus tests).
- **Current behavior**: Production-looking types whose only executions are their own unit tests. (FACT)
- **Why it matters**: (INFERENCE) Tests assert behavior nobody uses — false coverage confidence; the types read as implemented OS capabilities (commit seals, capability gates, deployment previews) that are not wired.
- **OS/product consequence**: The codebase misrepresents which OS safety controls actually run.
- **Recommendation**: (PROPOSAL) Wire them (`CapabilityIsolationGate` belongs in the kernel authorization path; `CommitSeal` in the journal commit path) or delete with their tests.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: ARCH-005, CLEAN-001, kernel subsystem decisions.
- **Tests/measurements required**: n/a. **Effort**: S-M. **Migration/rollback concern**: none.

### CLEAN-003: Dangling `<see cref>` to hosting-layer type
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/Models/DigitalBrainModels.cs:19,27` — cref `DigitalBrainOptions.WithLLM{TModel}`; `DigitalBrainOptions` is not in Core or its references.
- **Current behavior**: Unresolvable cref (compiles because XML-doc validation is not an error). (FACT)
- **Why it matters**: (INFERENCE) Trivial doc rot; misleads package consumers.
- **OS/product consequence**: none.
- **Recommendation**: (PROPOSAL) Reword to plain text naming the hosting method.
- **Deletion/simplification opportunity**: no. **Dependencies**: none. **Tests/measurements required**: none. **Effort**: S. **Migration/rollback concern**: none.

### CLEAN-004: Legacy compat constructor with no retirement trigger
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/Conversation.cs:52-76` — pre-operation-id constructor kept "until old snapshots age out"; no version gate, metric, or date.
- **Current behavior**: Permanent dual construction path. (FACT)
- **Why it matters**: (INFERENCE) "Temporary" compat becomes load-bearing; nobody will know when deletion is safe.
- **OS/product consequence**: Rolling-upgrade debt accumulates in the conversation projection.
- **Recommendation**: (PROPOSAL) Add a metric/counter for legacy-ctor hits; delete at sustained zero.
- **Deletion/simplification opportunity**: yes (eventually).
- **Dependencies**: ino subsystem. **Tests/measurements required**: legacy snapshot replay until removal. **Effort**: S. **Migration/rollback concern**: removal is one-way for pre-v2 snapshots.

### TEST-001: No serialization contract-freeze tests for the journaled vocabulary
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `tests/DigitalBrain.Tests/Core/` covers `JsonElementSurrogate`, `NeuronScope`, `SynapsePayloadJson`, DB/Experience shapes; `tests/DigitalBrain.Tests/Runtime/ContractsTests.cs` covers pure helpers. No test freezes the Orleans wire shape (field ids / aliases / round-trip bytes) of any `Synapse` subtype or `SelfEvolution*` record. (FACT — directory listing + file survey)
- **Current behavior**: A parameter reorder in an un-annotated record (REL-001) or an alias change passes the full test suite. (FACT)
- **Why it matters**: (INFERENCE) The only guard on journal-replay compatibility is reviewer memory.
- **OS/product consequence**: Silent journal corruption is undetectable pre-deploy — breaks the durable/replayable OS promise.
- **Recommendation**: (PROPOSAL) Add a golden-bytes round-trip test: serialize one canonical instance of every `[GenerateSerializer]` type in Core with Orleans' `Serializer`, compare to checked-in base64; reflectively assert alias uniqueness and explicit-Id coverage.
- **Deletion/simplification opportunity**: no.
- **Dependencies**: REL-001. **Tests/measurements required**: the test itself. **Effort**: M. **Migration/rollback concern**: none.

### TEST-002: Security helpers with zero callers also have zero meaningful tests
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Core/SensitiveText.cs` (no callers, no tests); `Redaction.SafeSummary` exercised only by `ContractsTests`.
- **Current behavior**: Redaction behavior (regex coverage, truncation) is essentially unverified and unused. (FACT)
- **Why it matters**: (INFERENCE) If wired in later under incident pressure, untested redaction regexes are exactly where secrets leak.
- **OS/product consequence**: False sense of a leak-prevention control.
- **Recommendation**: (PROPOSAL) Resolve with CLEAN-001 (delete), or add adversarial redaction tests when wiring in.
- **Deletion/simplification opportunity**: yes.
- **Dependencies**: CLEAN-001. **Tests/measurements required**: as described. **Effort**: S. **Migration/rollback concern**: none.
