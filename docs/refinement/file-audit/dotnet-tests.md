# Subsystem audit: dotnet-tests

- **Subsystem**: dotnet-tests — all .NET test projects and the test kit
- **Scope**: `tests/DigitalBrain.Tests/**` (Kernel, Runtime, Architecture, Aspire, Auth, Core, Db, Domains, Features, Foundry, Integrations, Llm, Sandbox, Spikes, Steps, TabularData, TestSupport, Ui, Uploads + project root), `tests/DigitalBrain.TestKit/**`, `tests/DigitalBrain.TestKit.Tests/**`, `tests/DigitalBrain.Salesforce.Tests/**` — 125 files total (full lists in the ledger fragment)
- **Commit**: `72400e3ebbec27e17af4ae6b5b2c4158c2797fa4` (branch `docs/refinement-audit`)
- **Date**: 2026-07-13

## Subsystem overview

The .NET test estate is split into four projects:

1. **DigitalBrain.Tests** — the main suite. Mixes true unit tests (transition functions, config binding, crypto envelopes), in-process Orleans cluster tests (via the TestKit's `NeuronTestBase`/`InProcessTestCluster`), ASP.NET host tests (`WebApplicationFactory<Program>` for the kernel), Aspire AppHost model tests (`DistributedApplicationTestingBuilder.CreateAsync` without start), one Reqnroll BDD feature, and two reflection-based architecture suites.
2. **DigitalBrain.TestKit** — shared harness library (`IsTestProject=false`): `NeuronTestBase`, `TestDigitalBrain` (in-proc cluster with in-memory dual journals), probe grains, prototype in-memory journal support.
3. **DigitalBrain.TestKit.Tests** — two smoke tests of the kit plus one misplaced Aspire path test.
4. **DigitalBrain.Salesforce.Tests** — provider tests driving the real `SalesforceApiClient`/`SalesforceConnector`/grains against recording `HttpMessageHandler` fakes (no network).

Overall quality is **substantially better than typical**: the security-heavy areas (OAuth state/PKCE/redirect allowlists, encrypted runtime-state envelopes, action-capability tokens, approval/lease-fenced conversation transitions, Gmail/Salesforce injection and scope hardening) are tested against *real production code* with hand-rolled transport fakes, and many tests assert negative/secret-leak properties (`DoesNotContain` on tokens, snippets, IDs). The main defects are: one wholly-theatrical BDD feature, security assertions made against production classes that are *not wired into any production path*, a sandbox gate whose tests certify a guardrail the code itself says is not a security boundary, and a handful of tests that re-implement the logic they claim to verify.

---

## Per-file review

Line ranges reviewed: every file below was read `1-<EOF>` in full (exact EOFs in the ledger). Verdicts: **retain** unless stated.

### tests/DigitalBrain.Tests/Kernel

- **AuthRequiredAIFunctionTests.cs** (1-67) — Gates an `AIFunction` behind a connectivity predicate: invokes when connected, returns the unauthorized message and never calls inner when not, preserves name/description. Real production decorator, real `AIFunctionFactory`. Meaningful auth-gate coverage (in-process; the predicate itself is faked, which is appropriate at this altitude). Verdict: retain.
- **AzureBlobPackConfigBackingStoreTests.cs** (1-248) — Opaque HMAC-derived blob naming, length-prefix collision resistance, key-material validation, legacy-name migration with copy-verify and fail-closed mismatch. Uses subclass-based Azure SDK fakes (the documented Azure mocking pattern). Strong; one caveat: the expected name is re-derived in the test with the same algorithm as production (TEST-603). Verdict: retain.
- **AzureClientHealthCheckRegistrationTests.cs** (1-47) — Regression for the `/health` 500: replicates Program.cs's Aspire-hosted Azure client wiring on a bare host builder and constructs each health-check factory without executing it. Well-commented, targeted, no network. Fragility: mirrors Program.cs by hand rather than exercising it (accepted trade-off, documented in-file). Verdict: retain.
- **BroadcastReactivityTests.cs** (1-84) — Real cluster broadcast fan-out to two activated handlers. Bounded poll (2s); `WaitForCountAsync` returns silently on timeout but final asserts catch it. Verdict: retain (see TEST-607 on the silent-timeout helper pattern).
- **CheckpointKeyingTests.cs** (1-65) — Checkpoint key provider + `AddKernelSecurity` selection: AES round-trip with key, **Production without key fails fast**, Development falls back to pass-through. Real fail-closed coverage of a crypto default. Verdict: retain.
- **CheckpointSecurityTests.cs** (1-53) — AES protector round-trip + tamper detection (`CryptographicException`), polymorphic snapshot encrypt/restore through real Orleans serializer. Verdict: retain.
- **DigitalBrainChatClientRegistrationTests.cs** (1-181) — Keyed `IChatClient` per registry entry; anthropic/xai/openai/github fail with clear errors when keys missing; unsupported provider does **not** silently fall back to Ollama. Good fail-closed DI coverage. Verdict: retain.
- **DigitalBrainModelRegistrySnapshotTests.cs** (1-52) — Registry snapshot read + capability filter; null on no match. Thin but real. Verdict: retain.
- **FakeGrainContext.cs** (1-34) — Hand-rolled `IGrainContext` fake exposing only `ActivationServices`; documented as reflection-verified against Orleans 10.2.1-preview.1 (TestingHost provides no fake). Correct usage. Verdict: retain.
- **HealthEndpointTests.cs** (1-30) — `/health` and `/alive` return 200 through `WebApplicationFactory<Program>` in test mode. Real host boot. Verdict: retain.
- **KernelStaticServingTests.cs** (1-43) — WEBROOT-configured index serving + SPA fallback; without webroot, `/` is not 200. Uses real temp dirs, cleans up. Verdict: retain.
- **LlmAttributeTests.cs** (1-85) — `[Llm<TModel>]` constructor-parameter mapper resolves the keyed `IChatClient` (including for a real production model type); clear error on wrong parameter type. Verdict: retain.
- **LlmResponderScopedConfigTests.cs** (1-226) — Proves the responder resolves provider/key from `IPackConfigStore` and uses the scoped client (never global); null-factory falls back to global. Cluster test with recording factory; well-documented single-silo pinning rationale. 1s poll window is tight (TEST-607). Verdict: retain.
- **LlmResponderTests.cs** (1-90) — AskLlm broadcast → reply Signal with LLM text via fake `IChatClient`. Real responder path. Verdict: retain.
- **ManagedIdentityStorageSelectionTests.cs** (1-56) — The `useManagedIdentity` branch is **re-implemented inside the test** (`!string.IsNullOrWhiteSpace(config[...])`), so drift in Program.cs cannot fail it; only the env-var flattening half tests something real (TEST-602). Also mutates a process env var (restored in finally). Verdict: simplify/replace — extract the production predicate and test it.
- **NeuronBroadcastTests.cs** (1-39) — Broadcast reaches a *different* grain via implicit channel subscription and lands in its incoming journal; guards the transport swap. Verdict: retain.
- **NeuronTests.cs** (1-110) — Activation journaling, fire/replay persistence, copy-safe JSON payloads (no `JsonElement` leakage), automation register/react including Ino-style `DefineReactionAsync`. Real cluster + real journals. Verdict: retain.
- **PackConfigBackingStoreSelectionTests.cs** (1-107) — Regression for "pack config silently ephemeral in production": Aspire-hosted wiring still selects the Azure backing store despite the keyed-client shadowing; missing stable key ring **fails closed**. High-value; documents the original bug precisely. Verdict: retain.
- **PackConfigStoreTests.cs** (1-78) — Round-trip, **persisted bytes contain no plaintext**, unknown pack empty, scope isolation, pack isolation. This is the core token-isolation unit coverage. Verdict: retain.
- **RollingUpdateRollbackTests.cs** (1-26) — Verify-failure rolls back, never completes, replica ordering asserted — but against `IAspireNeuron`'s *simulated* rolling update (`FailAtReplica`), not any real deployment surface (TEST-609). Verdict: retain, label as simulation.
- **RuntimeStateHostingTests.cs** (1-143) — Purpose-derived exact-AES-256 KEK; hosted+production **fail closed** instead of memory storage; production rejects oversized KEK; namespace-isolated container names; both Azure paths register dedicated containers and metadata-only health (no secret keys in health data — asserted by exact key list). Excellent fail-closed coverage. Verdict: retain.
- **SelfEvolutionContractTests.cs** (1-60) — Pins the proposal/decision/rollback wire vocabulary (approval flag, non-implicit expiry, rollback plan required, decided-by recorded). Contract-pin value. Verdict: retain.
- **SelfEvolutionDurabilityTests.cs** (1-98) — Real journaled cluster (JSON journal format): pending replays after reactivation, decisions are immutable across replay, **applied proposals are not re-applied on replay** (idempotency of the rail). Uses static apply-recording handler with explicit `Clear()`; collection is serialized so safe. Verdict: retain — this is the strongest self-evolution durability evidence.
- **SelfEvolutionNeuronTests.cs** (1-217) — The approval-before-apply invariant: rejected decision never calls handler; approved calls matching handler exactly once and journals result; expired proposals cannot be approved; unknown ApplyVia fails; **handler risk ceiling blocks higher-risk proposals**; failed apply with checkpoint journals `SelfEvolutionRollbackRequired`; duplicate decisions don't double-apply; invalid proposal (empty rollback plan) rejected and never pending. This is genuine, thorough coverage of the rail's decision gate. Gap: no coverage of the *verify* phase after apply (TEST-608). Verdict: retain.
- **SignalTests.cs** (1-80) — Signal/AskLlm construction + Orleans serialization round-trips. Verdict: retain.
- **TimelineStreamTests.cs** (1-28) — Stream provider name + global-namespace stream resolution. Thin. Verdict: retain.

### tests/DigitalBrain.Tests/Runtime

- **AgentFrameworkWorkflowRunnerTests.cs** (1-99) — Prior-workflow reuse requires exact runner/operation/session match; mismatches rejected before any chat call. Real runner, fake chat. Verdict: retain.
- **AuthorizationFlowStartProxyTests.cs** (1-186) — OAuth start proxy: canonical google start redirects only to the allowlisted provider with hardened browser headers (no-store/no-referrer/CSP/nosniff); provider mismatch/untrusted/port-variant redirects rejected 400 without Location; malformed flow rejected **without contacting the internal runtime**; transport failure → hardened 502 with no upstream detail; production requires https internal origin. Strong security-boundary coverage of real MCP code. Verdict: retain.
- **ContractsTests.cs** (1-203) — Grain-id scoping, `CapabilityIsolationGate` fail-closed, commit seal determinism + secret redaction, schema registry fail-closed, **signed action capability bound to session/scope/binding/expiry with tamper rejection**, model-router privacy/residency policy, telemetry label allowlist + drop accounting, deployment preview drift-blocking. The capability-token and telemetry pieces exercise production-wired types (`SessionTokenService`, `TelemetryBuffer`, `SchemaRegistry`); however `CapabilityIsolationGate`, `ModelRouter`, `DeploymentPreviewer`, `CommitSeal` are referenced **only by this test** — see SEC-600 / CLEAN-600. Verdict: split — keep wired-type tests, resolve the unwired ones.
- **ConversationSurfacePayloadTests.cs** (1-126) — Canonical effect phases preserved in projection; structurally invalid persisted action **omitted** (raw provider URL never reaches the payload); valid internal action kept; per-turn and total payload capped by UTF-8 bytes. Real payload-boundary hardening. Verdict: retain.
- **EffectPhaseProjectionTests.cs** (1-107) — Legacy record repair bounded to current event types; approved/applying/terminal phases retained in order in the surface feed. Verdict: retain.
- **EncryptedDomainStateTests.cs** (1-1290) — The heavyweight suite for durable runtime state: failed writes roll back with no mutation; lost-provider-response commits are verified and accepted; envelopes fail closed on tamper/wrong-KEK/wrong-signature and **rewrap with the active KEK**; conversation transitions are idempotent, lease-fenced, archive-compacting with an authenticated segment chain (tamper → `RuntimeStateIntegrityException`); outbox sequences monotonic and replay-stable; inbox receipts survive compaction; pending outbox bounded without discard; authorization resume claims idempotent and non-stealable; approval requests/decisions actor-bound and replay-safe; **approved effect completion rejects any change to immutable intent** (kind/scope/idempotency key); non-canonical authorization continuations rejected; surface-feed projection idempotent with action/ack authority, expiry non-resurrection, session rotation replay detection and version invalidation; synapse converter hides type+content and rejects envelope tamper. This is the deepest and most valuable test file in the repo — real production transition functions, deterministic fakes at the `IPersistentState` seam only. Verdict: retain (consider splitting the 1,290-line file by state kind for maintainability).
- **InoDurabilityRecoveryValidationTests.cs** (1-306) — Accepted command persists and completes without the client; reminder rehydration after triple grain deactivation completes exactly once; worker/dispatcher/presentation share durable trace correlation with no prompt/token/payload tags. Real MCP handler + real grains + fake workflow runner. Caveat: the "client disconnect" is simulated by cancelling an unrelated `Task.Delay` (TEST-604) — durability is still genuinely proven by the rest. Verdict: retain.
- **InoEffectConflictRecoveryTests.cs** (1-289) — Post-effect-result revision conflict reconciled **without re-executing the effect** (effect call count == 1), full approve→apply pipeline against real grains with a barrier `TimeProvider` keyed on the worker activity. Ingenious but tightly coupled to `ino.operation.execute` activity naming and blocking inside `GetUtcNow` (flakiness/fragility hazard, bounded at 10s). Verdict: retain, watch for flakes.
- **InoEffectPlanAuthorityTests.cs** (1-59) — Effect-plan scope HMAC bound to plan/actor/tool/summary; forged/tampered scopes rejected; execution proof bound to effect + provider idempotency key. Real least-privilege coverage. Verdict: retain.
- **InoEffectPlanTransitionsTests.cs** (1-77) — Immutable plan binding, completion **scrubs the provider payload** and is idempotent; payload must be present and bounded (≤64KiB). Verdict: retain.
- **InoMutationGrantTests.cs** (1-26) — `gmail.send`/`salesforce.write` grants demanded before approval of provider mutations; unknown typed tools keep existing policy (i.e. `ui.action` alone suffices — a deliberate looser default worth keeping visible). Verdict: retain.
- **InoReminderCadenceTests.cs** (1-50) — Reflection over private static fields asserts reminder periods ≥ Orleans minimum and timer fields are `IGrainTimer`. Brittle-by-design but guards a real production-only failure mode (FRAME-600). Verdict: retain.
- **InoReminderHandoffTests.cs** (1-511) — Conversation reminder hands off to worker reminder and completes once; non-canonical outbox payload left pending **without reordering later phases**; exact legacy presentation upgraded without rebuilding history; partial legacy upgrade rejected (fail-closed). Real durable-pipeline behavior. Verdict: retain.
- **InoTraceCorrelationTests.cs** (1-156) — Worker trace carries request/operation/grain/workflow/session/tool tags, actor scope forwarded to the runner, and **no tag key contains prompt/token/payload** (no-secret-in-telemetry). Verdict: retain.
- **InoWorkerConflictRecoveryTests.cs** (1-209) — Post-result revision conflict reconciled without a second workflow run (call count == 1); same barrier-TimeProvider pattern as effect-conflict test. Verdict: retain.
- **InoWorkflowFailureTests.cs** (1-153) — Workflow failure and deadline are terminal `NeverRetry` without durable retry; user turn marked failed in terminal projection. Verdict: retain.
- **KernelCompositionTests.cs** (1-145) — Conversation model grain routes prompt through configured chat client with grounded sender metadata rules; production kernel graph has one runtime + shared connector composition, `ClosedInoToolGateway` (not Plan gateway) as default, no legacy stream/gateway endpoints. Real DI-graph regression net. Verdict: retain.
- **LegacyInoPipelineRemovalTests.cs** (1-71) — Asserts legacy types/methods are absent by name via reflection. Legitimate deletion-guard while the migration is fresh; becomes permanent dead weight later (FRAME-600). Verdict: retain now, schedule deletion.
- **OAuthStateProtectorTests.cs** (1-42) — State opaque (owner never appears), tamper-evident, owner-specific, and expires after lifetime (real 100ms delay against 20ms lifetime). Verdict: retain.
- **RuntimeRequestAuthenticatorTests.cs** (1-140) — MCP capability grant demanded and never synthesized; framework-validated external identity mapped with exact scope; missing/malformed/unallowlisted identities rejected (elevated `brain.admin` grant → null). Real authenticator + fixed `IAuthenticationService`. Verdict: retain.
- **RuntimeSurfaceFeedTests.cs** (1-971) — MCP surface feed against hand-rolled grain fakes backed by the **real transition functions**: bounded 16-turn projection, ordered phase pages, one-time home-surface bootstrap, expired-action renewal as authoritative event, consumed action not reissued, legacy binding replacement, v1 presentation upgrade with extra-field rejection, wrong-scope conversation-id rejection, action authorization replay/tamper/stale/expiry semantics, approval rejected for operations not rendered by the signed surface. The fakes delegate to `SurfaceFeedTransitions`/`ConversationTransitions`, so this is not over-mocking. Verdict: retain.
- **RuntimeTransportBoundaryTests.cs** (1-75) — Kestrel body-size feature set even without Content-Length (chunked-body bound); read-only feature untouched. Narrow but real. Verdict: retain.
- **SemanticIntentModelTests.cs** (1-231) — Intent resolution uses separated roles + JSON schema; tenant/workspace/conversation IDs never reach the model input or schema; unknown JSON members rejected; cancellation propagated; mutation extraction strict and typed. Real grain, recording chat fake. Verdict: retain.
- **TypedReadWorkflowRunnerTests.cs** (1-553) — Typed read/mutation workflows: Gmail metadata-only list with explicit windows, internal OAuth start path → bounded authorization request, authorization resume re-selects the original typed read, **Gmail send prepared with exact preview but never executed** (send calls == 0), authorization demanded before mutation extraction, Salesforce single-field preview-without-apply, provider-secret IDs never in output text. Excellent preview/approve separation evidence. Verdict: retain.
- **UiExternalIdentityTests.cs** (1-177) — Production forbids shared bootstrap secret and partial OIDC; JWT validation parameters all enforced (issuer/audience/signature/lifetime/https metadata); claims map to exact scope with allowlisted grants; ambiguous/duplicate/normalized claims rejected; non-https issuer and overlapping claim mappings rejected. Verdict: retain.
- **UiGrpcServiceTests.cs** (1-61) — Action rejection → gRPC status mapping; action-token refresh only on binding-set change. Thin but real. Verdict: retain.

### tests/DigitalBrain.Tests/Architecture

- **AsyncContractArchitectureTests.cs** (1-166) — Real reflection-based enforcement: no `ValueTask` on core grain contract, `IHandle<>` requires optional trailing `CancellationToken`, implementations don't keep 1-arg overloads, CT-last-and-optional across 12 contract types, `.editorconfig` pins CA1068/CA2012/CA2016 as errors. These are genuine boundaries, not theatre. Verdict: retain.
- **CoreBoundaryTests.cs** (1-210) — Core references no other DigitalBrain assemblies and no runtime/host/integration packages (prefix denylist incl. Aspire/Azure/Google/Grpc/MCP); Pack.Contracts and Ui.Contracts depend on Core, never the reverse; ownership of pack/UI schema types pinned; demo/live surface builders kept out of contracts. Real, load-bearing layering enforcement supporting the OS model (provider concerns out of kernel/core). Verdict: retain.

### tests/DigitalBrain.Tests/Aspire

- **AddDigitalBrainExecutionModeTests.cs** (1-304) — Loads the real AppHost via `DistributedApplicationTestingBuilder.CreateAsync` (correct Aspire.Hosting.Testing usage; deliberately never Build/Start): run-mode emulator+Ollama+flutter graph, Test-profile wiring (bootstrap secret is a secret parameter, flutter wired **only** to the MCP transport endpoint, no secret in flutter-web env/args, OIDC grants list), partial-OIDC rejection, production profile omits local flutter/bootstrap, publish mode requires explicit profile and skips containers. High-value topology contract coverage. Verdict: retain.
- **DigitalBrainClusterIdTests.cs** (1-34) — Cluster-id resolution precedence + fresh dev id generation. Verdict: retain.
- **DigitalBrainModelCapabilitiesTests.cs** (1-53) — Model descriptor capabilities + service-key normalization. Verdict: retain.
- **DigitalBrainModelRegistryTests.cs** (1-150) — Typed model registry roles/default/override/embedding separation; all production LLM descriptors are tool-capable (reflection sweep). Verdict: retain.
- **OAuthCallbackPathTests.cs** (1-135) — Canonical callback/start paths shared across AppHost and runtime (full-URL check to catch port drift), internal start-path parse/reject matrix, flow-reference base64url bounds, **exact provider redirect allowlists** (scheme/port/path/host-suffix/userinfo/fragment variants all rejected) bound to the right provider. Core OAuth hardening evidence. Verdict: retain.
- **ResolveDevFlutterAppPathTests.cs** (1-24) — Null when no app folder. Twin lives in TestKit.Tests (CLEAN-602). Verdict: move/merge.

### tests/DigitalBrain.Tests root + Auth/Core/Db/Domains/Features/Steps

- **AssemblyInfo.cs** (1-11) — `MaxParallelThreads = 2` with clear CI rationale. Verdict: retain.
- **Auth/UserSessionNeuronClientIdTests.cs** (1-61) — Session-by-clientId resolution: latest active session, unknown → null, logged-out → null. Real grain. In `kernel-host` collection despite not using the fixture (TEST-610). Verdict: retain.
- **Auth/UserSessionNeuronTests.cs** (1-132) — First login provisions user (password hash journaled, plaintext absent), signed-in surface, invalid password fails without second session, logout ends session, server-driven login form, slash-in-username rejected. Gap: no lockout/rate-limit coverage (see coverage gaps). Verdict: retain.
- **Core/DbSchemaContractTests.cs** (1-38) — Provider-neutral schema DTO field carriage. Constructor-echo test, low value. Verdict: retain (cheap).
- **Core/ExperienceTypesTests.cs** (1-32) — Experience DTO construction. Constructor-echo. Verdict: retain (cheap).
- **Core/JsonElementSurrogateTests.cs** (1-26) — `JsonElement` round-trips through Orleans serialization (regression for the serializer bug fixed in d223eee). Verdict: retain.
- **Core/NeuronScopeTests.cs** (1-47) — Scope parse/format + pack-config scope prefixes. Verdict: retain.
- **Core/SynapsePayloadJsonTests.cs** (1-40) — Payload deserialization never yields `JsonElement`; numeric conventions pinned. Verdict: retain.
- **Db/SqliteSchemaInspectorTests.cs** (1-50) — Real SQLite file → tables/columns/FKs/indexes extraction. Verdict: retain.
- **Db/SqliteTestDatabases.cs** (1-61) — Temp DB builder + quiet delete. Verdict: retain.
- **DigitalBrain.Tests.csproj** (1-65) — Frameworks: Orleans TestingHost/Journaling, Reqnroll 3.x + xUnit adapter, Aspire.Hosting.Testing, Xunit.SkippableFact (only consumed by Reqnroll codegen), aliased AppHost/Mcp project references with clear Program-collision comments. `Grpc.Net.Client.Web` is referenced but used by **no test** (CLEAN-601). Verdict: simplify.
- **Domains/ForExperienceHopTests.cs** (1-42) — Experience-hop marker injection into wire dataJson + top-level props. Verdict: retain.
- **Domains/KitExperienceTests.cs** (1-259) — Pack-authoring DSL: hops render typed widget trees, events stamped with pack/experience, captured state baked into output, full widget vocabulary (inputs/layout/display/nav/overlay/feedback). Real DSL coverage. Verdict: retain.
- **Features/ChatFileAttachment.feature** (1-27) — Two scenarios describing the file-attachment journey. The prose promises end-to-end behavior the step definitions do not deliver (TEST-600). Verdict: replace or delete.
- **Features/ChatFileAttachment.feature.cs** (1-258) — Reqnroll 3-generated scenario driver (`SkippableFact`, skip only on `@ignore` tags — no silent security skips). Generated from the .feature by Reqnroll.Tools.MsBuild.Generation; checked in; regenerates on build so staleness risk is low. Excluded-generated.
- **Steps/ChatFileAttachmentSteps.cs** (1-93) — **Pure theatre**: the "user drops a file" step constructs the expected `TableSurface` inside the step itself; the assistant scenario's Then-steps assert only `NotNull` on that same object. No parser, no chat grain, no timeline, no assistant is exercised (TEST-600). Verdict: replace (wire to `TabularDataParser` + `ChatNeuron`) or delete the feature.

### tests/DigitalBrain.Tests/Foundry, Sandbox, Spikes

- **Foundry/AzureResourceControllerTests.cs** (1-16) — Dry-run restart records intent. Smoke-only; no behavior beyond a property set. Verdict: retain (cheap) but near-vacuous.
- **Foundry/CapabilityGateTests.cs** (1-69) — Gate allows arithmetic/collections, flags Process.Start, rejects System.Net/System.IO. Only happy-path static cases; **no negative tests for the reflection escapes the gate's own header comment concedes** (SEC-601). Verdict: retain + extend.
- **Foundry/CodeRunNeuronWiringTests.cs** (1-28) — CodeRun grain executes generated code end-to-end in cluster. Verdict: retain.
- **Foundry/FoundryFakes.cs** (1-50) — Fake build runner/resource controller **plus tests of the fakes themselves** (TEST-605). Verdict: simplify — keep fakes, delete `FoundryFakesTests`.
- **Foundry/InProcessAlcExecutorTests.cs** (1-48) — Executes, reports compile errors, rejects banned symbol. Same static-gate-only limitation as above. Verdict: retain.
- **Sandbox/OutOfProcessSandboxTests.cs** (1-54) — Pack really runs in a different process (asserts foreign PID), gate rejects before running, compile errors surfaced. Proves process separation; does **not** prove runtime confinement (network/file access from inside the child process is untested) — part of SEC-601. Verdict: retain + extend.
- **Spikes/JournalFormatSpikeTests.cs** (1-64) — JSON journal format round-trips a synapse through real deactivation/reactivation (proves deserialization, not just write). Named "spike" but is a real regression net for the journal format. Verdict: retain, rename out of Spikes.

### tests/DigitalBrain.Tests/Integrations

- **GoogleGmailApiClientTests.cs** (1-605) — Outstanding provider-boundary suite against a recording handler: metadata-only fetches (no snippet/body/raw ever requested or exposed), RFC From-header normalization incl. encoded words, malformed sender not inferred, provider failure propagates for grain classification, **contract reflection pins bounded typed operations (no raw query/messageId params)**, send rejects header-injection/oversized fields *before any provider call*, grain rejects invalid mutation before touching auth (nulls prove no dependency access), unique-tag reconciliation prevents duplicate send, RFC2822 assembly with confidentiality assertions, internal-date ordinal ordering, anchored pagination stability, bounded-window fail-closed, typed list pagination/dedupe/coverage accounting, pinned-page slicing without refetching earlier candidates, thread grouping without content reads, attachment filter → typed capability limitation without provider call, metadata contract has no body/snippet/raw/attachment properties. Verdict: retain — model example for connector testing.
- **IConnectorContractTests.cs** (1-210) — Reusable connector contract base, but the base assertions are largely vacuous (`Assert.NotNull(status)`, health "returns structure"), the TODO admits the security-relevant cases (two-user isolation, PKCE state, callback roundtrip) are deferred (they do exist elsewhere in OAuthConnectorSecurityTests), and `DummyIConnectorContractTests` tests a dummy against itself (TEST-601). Salesforce/Google subclasses add real missing-key and no-leak-health-detail checks. Verdict: simplify — delete dummy, tighten base asserts.
- **OAuthConnectorSecurityTests.cs** (1-696) — The central OAuth security suite, all against real `GoogleConnector`/`SalesforceConnector` with fake stores/handlers: least-privilege Google scopes (readonly+send only, mail.google.com/modify/compose absent), **principal-scoped state and tokens with cross-principal isolation**, tampered/denied/replayed callbacks rejected, live-attempt coalescing, internal-start-reference replay preservation, begin never mutates app config, redirect/secret pinning for started flows, client-rotation rejection mid-flight and supersession on restart, app config authoritative over legacy user values, exchange-timeout terminalization, stale-refresh-token non-certification, completion witness surviving cleanup failure with idempotent replay, terminal/abandoned states never left waiting, Salesforce PKCE roundtrip with token/scope separation (client secret never stored in user scope), denial cannot release waits with stale credentials, expired pending residue terminalized with secret fields purged. Constant-time comparisons for secrets in the test helpers. Verdict: retain — this and the Gmail suite are the strongest security tests in the repo.

### tests/DigitalBrain.Tests/Llm, TabularData, Uploads, Ui

- **Llm/ChatClientRegistrationTests.cs** (1-217) — Provider registration matrix incl. managed-identity fallback; **chat telemetry never captures message content even with the OTel capture env var enabled** (serialized collection for env-var safety). Verdict: retain.
- **Llm/DigitalBrainChatEmbeddingRegistrationTests.cs** (1-46) — Embedding registration or documented no-op fallback. Verdict: retain.
- **Llm/DigitalBrainChatPolicyTests.cs** (1-108) — Concurrency limit enforced via blocking fake; **no retry on failure** (cost-control invariant). Verdict: retain.
- **Llm/DigitalBrainEmbeddingRuntimeOptionsTests.cs** (1-38) — Options binding. Verdict: retain.
- **TabularData/TabularDataParserTests.cs** (1-89) — Real XLSX (ClosedXML) parse: headers/rows, numeric-only stats, UI row cap with full-sheet stats. This is the *real* coverage the BDD feature pretends to have. Verdict: retain.
- **Uploads/ChatUploadClassifierTests.cs** (1-20) — Extension classification. Verdict: retain.
- **Ui/BundleHarness.cs** (1-42) — Compiles shipped pack source via the production ALC path and drives ExperienceSteps. **Referenced by no test** (CLEAN-602). Verdict: delete or adopt.
- **Ui/ChatNeuronTests.cs** (1-21) — Visualize request emits RfwCard into conversation. Verdict: retain.
- **Ui/ExperienceTestHarness.cs** (1-184) — `UiTreeAssertions` extension library (finders, golden snapshot). **Referenced by no test** (CLEAN-602). Verdict: delete or adopt.

### tests/DigitalBrain.Tests/TestSupport

- **AsyncTestWait.cs** (1-55) — Bounded poll helper that *throws* on timeout with description — the correct pattern (several test files hand-roll silent variants instead; TEST-607). Verdict: retain, promote usage.
- **CapturingServerStreamWriter.cs** (1-24) — gRPC stream writer capture with first-write hook. Verdict: retain (verify a caller exists post-gateway-removal).
- **FakeHostEnvironment.cs** (1-15) — Minimal `IHostEnvironment`. Verdict: retain.
- **KernelHostCollection.cs** (1-5) — `kernel-host` collection definition (serialized, shares `KernelWebApplicationFactory`). Verdict: retain.
- **KernelWebApplicationFactory.cs** (1-27) — Test-mode kernel host factory. Verdict: retain.
- **OrleansJournalClusterFixture.cs** (1-76) — Shared journaled cluster (JSON journal format, volatile journal storage) + static `DurableRecordingApplyHandler` (static state, safe under the serialized collection but fragile if reused). Verdict: retain.
- **TestGrainFactory.cs** (1-37) — `IGrainFactory` adapter over `NeuronTestBase` for MCP tool classes. Verdict: retain (verify a caller still exists).
- **TestServerCallContext.cs** (1-46) — Minimal `ServerCallContext`. Verdict: retain (verify a caller still exists).

### tests/DigitalBrain.Salesforce.Tests

- **DigitalBrain.Salesforce.Tests.csproj** (1-25) — Standard test project. Verdict: retain.
- **FakeSalesforceTokenHandler.cs** (1-55) — Token-endpoint fake + query extraction helper. Verdict: retain.
- **SalesforceApiClientTests.cs** (1-168) — Identity endpoint allowlisting (http/foreign-host/non-identity path rejected **without forwarding the token**), profile bounded fields (no photos), fixed-field bounded reads (LIMIT clamped 1..50), no caller SOQL (contract reflection: no `Query` method), CRM describe allowlisted to Account/Contact with write-capability fields stripped, cancellation before provider call. Verdict: retain.
- **SalesforceClientFactoryTests.cs** (1-225) — Endpoint normalization, web-server flow URL (api+refresh_token scopes only, no offline_access), PKCE S256 including the RFC 7636 test vector, exact provider redirect allowlist matrix, app-config rejection of untrusted login/callback origins, canonical internal start URL, credential handling, fake token-exchange roundtrip. Verdict: retain.
- **SalesforceMutationApiClientTests.cs** (1-176) — Preview resolves labels + opaque prepared update with canonicalized value; apply patches exactly one resolved field and **verifies** the write; already-desired reconciles without update; original-value conflict blocks the patch; unpersisted write reported as `VerificationFailed`. The full preview→approve→apply→verify mutation loop against real client code. Verdict: retain.
- **SalesforceOAuthStartNeuronTests.cs** (1-359) — Cluster tests for the OAuth-start grain: opaque local start URL with no provider params/principal leakage, provider redirect idempotent across deactivation, atomic local-start → provider-pending supersession with callback surviving reactivation, late-callback replay after success + expired residue still returns completed, coalesced reads don't replace PKCE state. Constant-time secret comparison. Verdict: retain.
- **SalesforceReadNeuronContinuationTests.cs** (1-210) — Continuations survive cancellation/transient failure for retry, success consumes them (replay → `ContinuationExpired`), survive grain reactivation. Uses reflection to construct the internal provider continuation (acceptable seam). Verdict: retain.
- **SalesforceSemanticReadTests.cs** (1-197) — Semantic reads resolve described labels only; **SOQL/SOSL injection payloads escaped or fail closed before reaching the SDK**; related/aggregate/search/query-more use schema-resolved values only; discovery exposes labels without API names; continuation contract has no string leak surface. Verdict: retain.

### tests/DigitalBrain.TestKit + TestKit.Tests

- **DigitalBrain.TestKit.csproj** (1-30) — `IsTestProject=false` with clear rationale. Verdict: retain.
- **IDigitalBrain.cs** (1-11) — Harness abstraction. Verdict: retain.
- **NeuronTestBase.cs** (1-32) — Per-test in-proc cluster (fresh cluster per test = strong isolation, heavy cost — accepted trade-off given `MaxParallelThreads=2`). Verdict: retain.
- **NeuronTestKernelConfigurator.cs** (1-63) — Shared silo wiring: in-memory dual journals, memory streams/storage, self-evolution apply handlers, no-op scoped chat factory (prevents hidden network deps), automations enabled. Verdict: retain.
- **ProbeContracts.cs** (1-19) — Probe synapse/interface deliberately in `DigitalBrain.Core` namespace to satisfy the CapabilityGate allowlist — a test type impersonating a production namespace; documented, but it means the gate cannot distinguish test probes from Core (minor trust-boundary smell, accepted). Verdict: retain.
- **ProbeNeuron.cs** (1-22) — Probe grain incl. JSON signal firing. Verdict: retain.
- **PrototypeJournalSupport.cs** (1-29) — In-memory `IDurableList` + no-op journaled state manager; test-only, clearly labeled. Verdict: retain.
- **TestDigitalBrain.cs** (1-82) — Cluster bootstrap; sets `DIGITALBRAIN_TEST_MODE=true` **process-wide and never restores it** (TEST-606); 2-minute response timeout; `Cluster` property allocates a new wrapper per call (trivial). Verdict: retain + fix env handling.
- **TestKit.Tests/Aspire/ResolveDevFlutterAppPathTests.cs** (1-30) — Repo-root app resolution; the sibling case lives in DigitalBrain.Tests (CLEAN-602). Verdict: move/merge.
- **DigitalBrain.TestKit.Tests.csproj** (1-26) — Near-empty project (2 smoke tests + 1 misplaced test). Verdict: consider folding into DigitalBrain.Tests.
- **NeuronTestBaseTests.cs** (1-16) / **TestDigitalBrainTests.cs** (1-23) — Harness smoke tests (timeline NotNull). Minimal but acceptable as kit sanity checks. Verdict: retain.

---

## Findings

### SEC-600: Tenant-isolation gate is asserted only on a class no production code uses
- **Severity**: High
- **Confidence**: High
- **Evidence**: `tests/DigitalBrain.Tests/Runtime/ContractsTests.cs:23-33` tests `CapabilityIsolationGate.IsAllowed/Demand` ("Isolation gate fails closed"). Grep across the repo shows `CapabilityIsolationGate` (and `ModelRouter`, `DeploymentPreviewer`, `CommitSeal`) appear only in `src/DigitalBrain.Core/RuntimeContracts.cs` / `ModelRouting.cs` / `DeploymentPreview.cs` (definitions) and this test; the only Core runtime types registered in production are `TelemetryBuffer` and `SchemaRegistry` (`src/DigitalBrain.Mcp/Program.cs:34-37`, `src/DigitalBrain.Kernel/Hosting/DigitalBrainOrleansExtensions.cs:215-216`). (FACT)
- **Current behavior**: The test suite green-lights a "fail-closed isolation gate" that is never constructed, registered, or invoked on any request path. Actual tenant/workspace isolation relies on scope-prefixed grain keys (`GrainIds`, `RuntimeStateKeys`) and per-grant checks (`McpAuthority.DemandGrant`), which are tested separately but only per-component.
- **Why it matters**: (INFERENCE) A reader auditing "is tenant isolation enforced?" finds a passing test and reasonably concludes there is a central enforcement gate. There is not. Any endpoint that forgets to derive the grain key from the authenticated context has no backstop, and no test would notice.
- **OS/product consequence**: Tenant-isolation trust boundary — the invariant "auth must be tenant-isolated, fail-closed at every boundary" has no cross-cutting enforcement point and no end-to-end negative test (tenant A context → tenant B grain).
- **Recommendation**: (PROPOSAL) Either wire `CapabilityIsolationGate` into the MCP/UI request pipeline and add a cluster-level cross-tenant negative test, or delete the class and its test and add an explicit cross-tenant access test against the real `RuntimeSurfaceFeed`/`ConversationStateClient` path.
- **Deletion/simplification opportunity**: Yes — delete the unwired class + test if key-scoping is declared the isolation mechanism.
- **Dependencies**: mcp/runtime subsystem audit (whether isolation-by-key-derivation is complete); CLEAN-600.
- **Tests/measurements required**: A test that authenticates as tenant A and attempts to read/act on tenant B's conversation/surface feed through the public MCP path, asserting rejection.
- **Effort**: M
- **Migration/rollback concern**: none.

### SEC-601: Sandbox/capability-gate tests certify a guardrail the production code itself disclaims as a security boundary; no escape-attempt tests exist
- **Severity**: High
- **Confidence**: High
- **Evidence**: `src/DigitalBrain.Kernel/Foundry/CapabilityGate.cs:7-12` — "CONFIRMED BYPASS … This is a guardrail against accidental misuse, not a security boundary — the followup doc … was deleted in commit 6dfc0a7; the tracked fix it described no longer exists." Tests: `tests/DigitalBrain.Tests/Foundry/CapabilityGateTests.cs:14-67`, `Foundry/InProcessAlcExecutorTests.cs:32-47`, `Sandbox/OutOfProcessSandboxTests.cs:31-44` assert only direct-symbol cases (`Process.Start`, `System.Net`, `System.IO`). No test attempts a reflection route that stays inside the allowlist (e.g. `System.Reflection.MethodInfo.Invoke` — not in `ExcludedWithinSystem` at `CapabilityGate.cs:29-40` — reached via allowed `System.Type.GetMethod`), and no test asserts runtime confinement (network/file access) of the out-of-process child. (FACT)
- **Current behavior**: Sandbox test coverage proves process separation and static-scan rejection of *named* banned symbols only.
- **Why it matters**: (INFERENCE) The OS model runs generated/pack C# through this path ("Packs are signed C# embodied at runtime"; Foundry executes generated code). Green sandbox tests give false assurance that hostile or LLM-generated code is confined; a reflection-shaped payload passes the gate and runs with full process capabilities (in-process ALC executor: full kernel process; out-of-process: full child-process capabilities, no OS-level restriction tested).
- **OS/product consequence**: Self-evolution / Foundry trust boundary — approved-code execution isolation is asserted-but-not-real.
- **Recommendation**: (PROPOSAL) Add pinned "known-bypass" tests that document current behavior (a reflection payload that the gate passes), so the limitation is executable knowledge; then either restore the deleted hardening plan (deny `System.Reflection.` wholesale, run out-of-proc with OS-level restrictions) or downgrade all sandbox language in docs/UI. Tests must include a runtime-confinement check for `OutOfProcessSandbox` (child attempts file/network I/O).
- **Deletion/simplification opportunity**: No.
- **Dependencies**: kernel/Foundry subsystem audit; SelfEvolution rail.
- **Tests/measurements required**: Gate test with `typeof(...).GetMethod(...).Invoke(...)` payload; out-of-process test asserting file write/network connect fails (will currently fail → drives the fix).
- **Effort**: M (tests S; real hardening L)
- **Migration/rollback concern**: none for tests.

### TEST-600: ChatFileAttachment BDD feature is self-referential theatre
- **Severity**: High
- **Confidence**: High
- **Evidence**: `tests/DigitalBrain.Tests/Steps/ChatFileAttachmentSteps.cs:20-34` — the When-step *constructs* the `TableSurface` the Then-steps assert on ("Here we directly produce the TableSurface as the observable outcome"). Second scenario's Then-steps (`:75-91`) assert only `Assert.NotNull(_lastTableSurface)` for "assistant response references the table data" and "no error surfaces are emitted". (FACT)
- **Current behavior**: The feature passes without touching `TabularDataParser`, `ChatUploadClassifier`, any grain, timeline, or assistant. The only production code executed is the `TableSurface` record constructor.
- **Why it matters**: (INFERENCE) The feature file reads like end-to-end coverage of the drag-and-drop journey; anyone consulting the BDD layer for behavior guarantees is misled. The real coverage lives in `TabularData/TabularDataParserTests.cs`, `Uploads/ChatUploadClassifierTests.cs`, `Ui/ChatNeuronTests.cs` — which cover parsing and visualization but not the joined pipeline.
- **OS/product consequence**: Chat file-attachment user journey has zero integrated verification despite an apparently-green scenario suite.
- **Recommendation**: (PROPOSAL) Rewrite the steps to drive the real path (bytes → classifier → parser → chat grain → timeline `TableSurface`), or delete the feature + steps + Reqnroll dependency entirely (3 packages exist solely for this one feature).
- **Deletion/simplification opportunity**: Yes — deleting the feature removes Reqnroll, Reqnroll.Tools.MsBuild.Generation, Reqnroll.xUnit, Xunit.SkippableFact and the generated driver.
- **Dependencies**: none.
- **Tests/measurements required**: A rewritten scenario failing when the parser or chat grain is broken.
- **Effort**: S (delete) / M (rewire)
- **Migration/rollback concern**: none.

### TEST-601: IConnector contract-test base is largely vacuous and its security TODOs are unowned
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `tests/DigitalBrain.Tests/Integrations/IConnectorContractTests.cs:33-41` (`Assert.NotNull(status)` only), `:53-61` (health: "contract ensures no throw and returns structure"), `:63-66` (TODO: full roundtrip, two-user isolation, cross-silo, PKCE state deferred), `:89-92` (`DummyIConnectorContractTests` runs the base against `DummyConnector`, i.e. tests the dummy). (FACT)
- **Current behavior**: Base "contract" verifies nothing beyond non-nullness; the deferred security cases actually exist — in `OAuthConnectorSecurityTests.cs` — but outside the reusable contract, so a third connector inherits no security bar.
- **Why it matters**: (INFERENCE) The connector model is the extension point of the OS ("first two connectors of a general connector model"); the reusable contract is precisely where isolation/PKCE/replay invariants should be forced on every future provider, and today it forces nothing.
- **OS/product consequence**: Connector capability model — new providers can ship with green "contract tests" and no security properties.
- **Recommendation**: (PROPOSAL) Move the provider-agnostic invariants from `OAuthConnectorSecurityTests` (principal-scoped state/tokens, tamper/replay/denial rejection, app-config authority, no-leak health detail) into the abstract base with template-method fakes; delete `DummyConnector`/`DummyIConnectorContractTests`.
- **Deletion/simplification opportunity**: Yes — dummy connector + its test class.
- **Dependencies**: integrations subsystem audit.
- **Tests/measurements required**: Base-class tests failing when a subclass connector leaks tokens across principals.
- **Effort**: M
- **Migration/rollback concern**: none.

### TEST-602: ManagedIdentityStorageSelectionTests re-implements the production branch inside the test
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `tests/DigitalBrain.Tests/Kernel/ManagedIdentityStorageSelectionTests.cs:23-26` and `:44-46` — the test computes `var useManagedIdentity = !string.IsNullOrWhiteSpace(storageAccountName)` itself; the in-file comment admits `useManagedIdentity` is "a local variable inside Program.cs's top-level statements (not a testable static method)". (FACT)
- **Current behavior**: If Program.cs changes its predicate (typo, different key, inverted condition), this test still passes. Only the env-var double-underscore flattening assertion tests real framework behavior — and that is the .NET configuration provider, not repo code.
- **Why it matters**: (INFERENCE) The managed-identity switch selects between credential modes for production storage; a silent drift means either broken prod auth or an unexpected connection-string fallback, and the "coverage" would stay green.
- **OS/product consequence**: Production storage credential selection is effectively untested.
- **Recommendation**: (PROPOSAL) Extract the predicate into a testable static (e.g. `StorageIdentityMode.From(IConfiguration)`) used by Program.cs, and point both tests at it. (`RuntimeStateHostingTests` already exercises the downstream `UseDigitalBrainOrleans` managed-identity branch, which mitigates but does not cover the Program.cs switch itself.)
- **Deletion/simplification opportunity**: Yes — the first test becomes redundant once extracted.
- **Dependencies**: kernel host audit.
- **Tests/measurements required**: Test fails when the production predicate's config key is changed.
- **Effort**: S
- **Migration/rollback concern**: none.

### TEST-603: Opaque blob-name expectation re-derives the production algorithm in the test
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `tests/DigitalBrain.Tests/Kernel/AzureBlobPackConfigBackingStoreTests.cs:136-155` — `ExpectedName` reproduces the purpose-key HMAC + length-prefixed component hashing byte-for-byte. (FACT)
- **Current behavior**: A shared design flaw (or a coordinated change) in the derivation passes both sides. The non-containment assertions (`:30-31`) and the collision test (`:36-45`) are the real property checks.
- **Why it matters**: (INFERENCE) Parallel-implementation tests detect accidental drift but not scheme weaknesses; they also double the maintenance cost of any deliberate scheme change.
- **OS/product consequence**: Minor — pack-config secrecy properties are still asserted independently.
- **Recommendation**: (PROPOSAL) Replace `ExpectedName` with property assertions only (determinism across store instances with the same key ring; difference under a different signing key), or pin a golden literal for one known input.
- **Deletion/simplification opportunity**: Yes — ~20 lines.
- **Dependencies**: none.
- **Tests/measurements required**: Golden-vector test.
- **Effort**: S
- **Migration/rollback concern**: none.

### TEST-604: Fake "client disconnect" in the durability test proves nothing about disconnects
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `tests/DigitalBrain.Tests/Runtime/InoDurabilityRecoveryValidationTests.cs:69-72` — creates a fresh CTS, cancels it, and awaits the resulting `OperationCanceledException` from an unrelated `Task.Delay`; nothing in the pipeline observes this token. (FACT)
- **Current behavior**: The test's genuinely valuable half (accepted command persists; completes via reminders with no further client calls) is unaffected; the disconnect pantomime just decorates the name.
- **Why it matters**: (INFERENCE) Misleading narrative — a reader believes request-abort mid-accept is covered.
- **OS/product consequence**: Durable-acceptance-under-abort is untested at the transport layer.
- **Recommendation**: (PROPOSAL) Delete lines 69-72, or actually pass an aborted `HttpContext.RequestAborted`-style token into the accept path.
- **Deletion/simplification opportunity**: Yes — 4 lines.
- **Dependencies**: none.
- **Effort**: S
- **Migration/rollback concern**: none.

### TEST-605: FoundryFakesTests tests the test fakes
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `tests/DigitalBrain.Tests/Foundry/FoundryFakes.cs:30-49` — `FakeBuildRunnerHonorsConfiguredResult`, `FakeResourceControllerCountsRestarts`. (FACT)
- **Current behavior**: Two tests verifying that hand-written fakes return what they were configured to return.
- **Why it matters**: (INFERENCE) Zero production signal; inflates the passing-test count.
- **Recommendation**: (PROPOSAL) Delete `FoundryFakesTests`; keep the fakes (verify they still have consumers — no test in this audit's scope besides this file references them, so possibly the fakes are dead too).
- **Deletion/simplification opportunity**: Yes.
- **Dependencies**: none.
- **Effort**: S
- **Migration/rollback concern**: none.

### TEST-606: Process-wide `DIGITALBRAIN_TEST_MODE` is set by the cluster harness and never restored
- **Severity**: Medium
- **Confidence**: Medium
- **Evidence**: `tests/DigitalBrain.TestKit/TestDigitalBrain.cs:25` — `Environment.SetEnvironmentVariable("DIGITALBRAIN_TEST_MODE", "true")` in `InitializeAsync`, never cleared. Other env-mutating tests (`ManagedIdentityStorageSelectionTests.cs:39-53`, `ChatClientRegistrationTests.cs:160-192`) restore in `finally`, and the telemetry one further serializes via a `DisableParallelization` collection — this one does neither. (FACT)
- **Current behavior**: Once any cluster test has run, every subsequent test in the same process (xUnit runs collections in parallel, 2 threads) observes test mode. Any test that asserts *non-test-mode production* behavior of code that branches on this variable can silently take the test-mode branch depending on execution order.
- **Why it matters**: (INFERENCE) Order-dependent shared state is a classic source of "passes locally, flakes in CI" and — worse here — of production fail-closed branches being tested only in their test-mode variant.
- **OS/product consequence**: Undermines confidence in every "production fails closed" assertion running in the same process as a cluster test.
- **Recommendation**: (PROPOSAL) Prefer configuration (`DigitalBrain:TestMode` via silo config, which `KernelWebApplicationFactory` already uses) over process env; if env is unavoidable, set it once in a module initializer and document that the whole test process is test-mode by contract.
- **Deletion/simplification opportunity**: Yes — one env-var mechanism instead of two (`DigitalBrain:TestMode` config + `DIGITALBRAIN_TEST_MODE` env).
- **Dependencies**: kernel host audit (who reads the env var).
- **Tests/measurements required**: Grep audit of `DIGITALBRAIN_TEST_MODE` readers; run affected tests in isolation vs. full suite.
- **Effort**: S-M
- **Migration/rollback concern**: none.

### TEST-607: Hand-rolled poll loops with tight/silent timeouts instead of the shared throwing helper
- **Severity**: Low
- **Confidence**: Medium
- **Evidence**: `tests/DigitalBrain.Tests/Kernel/LlmResponderTests.cs:76-82` and `LlmResponderScopedConfigTests.cs:142-147,167-173,210-216` — 20×50ms (1s) budget on a cross-scheduler stream delivery, on a suite whose own `AssemblyInfo.cs:1-10` documents CPU oversubscription risk; `BroadcastReactivityTests.cs:56-67` — `WaitForCountAsync` returns silently on timeout (the following asserts do catch it, but the helper invites silent misuse). `TestSupport/AsyncTestWait.cs` already provides a bounded, *throwing*, described wait that these files don't use. (FACT)
- **Current behavior**: Works today; the 1s windows are the most likely first flake under CI contention.
- **Why it matters**: (INFERENCE) Flaky timing tests erode trust in the suite and get muted.
- **Recommendation**: (PROPOSAL) Standardize on `AsyncTestWait.WaitUntilAsync` with ≥5s budgets for stream-delivery waits.
- **Deletion/simplification opportunity**: Yes — deletes 4 bespoke loops.
- **Dependencies**: none.
- **Effort**: S
- **Migration/rollback concern**: none.

### TEST-608: Self-evolution "verify" phase and pack signing have no tests
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: The rail contract is "propose → diff → risk → validate → human approve → apply → journal → verify → rollback" (CLAUDE.md / audit brief). `SelfEvolutionNeuronTests.cs` + `SelfEvolutionDurabilityTests.cs` cover propose/validate/approve/apply/journal/rollback-required; no test anywhere asserts a post-apply verification step or its failure path, and no test in scope asserts pack signature validation ("Packs are signed C#") — `BundleHarness`/`PackAlcEmbodier` tests embody unsigned source directly. (FACT)
- **Current behavior**: An apply handler returning `Succeeded: true` terminates the happy path; nothing checks the applied effect matches the approved proposal.
- **Why it matters**: (INFERENCE) Verify-after-apply is the difference between "we ran something" and "the approved change is what now exists" — the core promise of the governed rail. If production has no verify phase, that is an ARCH gap for the kernel audit; if it has one, it is untested.
- **OS/product consequence**: Self-evolution rail integrity (verify) and pack provenance (signing) are unproven invariants.
- **Recommendation**: (PROPOSAL) Cross-check with the kernel audit whether verify/signing exist in production; add tests either pinning their behavior or documenting the gap as a NotImplemented guard.
- **Deletion/simplification opportunity**: No.
- **Dependencies**: kernel/SelfEvolution audit; SEC-601.
- **Tests/measurements required**: Verify-failure → rollback-required test; unsigned/tampered pack rejection test.
- **Effort**: M
- **Migration/rollback concern**: none.

### TEST-609: Rolling-update rollback is proven only against an in-grain simulation
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `tests/DigitalBrain.Tests/Kernel/RollingUpdateRollbackTests.cs:11-24` — `PerformKernelSelfUpdate("rollback-test", FailAtReplica: 2)` drives `IAspireNeuron`'s simulated replica sequence. (FACT)
- **Current behavior**: Asserts the orchestration logic (drain ordering, rollback emission, no complete) but no real drain/restart occurs.
- **Why it matters**: (INFERENCE) Fine as a unit of orchestration logic; risky if anyone reads it as deployment-rollback coverage.
- **Recommendation**: (PROPOSAL) None required; keep the "simulation" framing visible in the test name or a comment.
- **Effort**: S
- **Migration/rollback concern**: none.

### TEST-610: `[Collection("kernel-host")]` on cluster tests that never use the fixture
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `tests/DigitalBrain.Tests/Auth/UserSessionNeuronClientIdTests.cs:6-7` and `UserSessionNeuronTests.cs:10-11` derive from `NeuronTestBase` (own cluster) yet sit in the `kernel-host` collection whose only purpose is sharing `KernelWebApplicationFactory`. (FACT)
- **Current behavior**: Membership merely serializes them with the host tests (possibly intentional resource throttling, but undocumented).
- **Recommendation**: (PROPOSAL) Either remove the attribute or add a comment stating the serialization intent.
- **Effort**: S

### CLEAN-600: Speculative Core runtime types kept alive only by tests
- **Severity**: Medium
- **Confidence**: High
- **Evidence**: `ModelRouter` (`src/DigitalBrain.Core/ModelRouting.cs`), `DeploymentPreviewer` (`DeploymentPreview.cs`), `CommitSeal`, `CapabilityIsolationGate` (`RuntimeContracts.cs`) have no production references (grep: definitions + `tests/DigitalBrain.Tests/Runtime/ContractsTests.cs` only). (FACT)
- **Current behavior**: Tests exercise dead code, making it look load-bearing and blocking deletion.
- **Why it matters**: (INFERENCE) Violates the repo's own delete-first rule; each is a "second authority" waiting to diverge from the real mechanism (model selection actually happens in the LLM registry; deployments in deploy/Pulumi; isolation in key scoping).
- **OS/product consequence**: Decision fatigue + false coverage signal.
- **Recommendation**: (PROPOSAL) Wire-or-delete each type; deleting removes ~4 test regions from ContractsTests too.
- **Deletion/simplification opportunity**: Yes — primary point.
- **Dependencies**: SEC-600; core subsystem audit.
- **Effort**: S-M
- **Migration/rollback concern**: none.

### CLEAN-601: Unused `Grpc.Net.Client.Web` package reference
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj:32` references it for "gRPC-Web health coverage"; grep finds no test using `GrpcWeb*`. (FACT)
- **Current behavior**: Dead dependency; the comment describes tests that no longer exist (likely removed with the gateway).
- **Recommendation**: (PROPOSAL) Delete the PackageReference and stale comment.
- **Deletion/simplification opportunity**: Yes.
- **Effort**: S

### CLEAN-602: Dead test harnesses and a split micro-project
- **Severity**: Low
- **Confidence**: High
- **Evidence**: `tests/DigitalBrain.Tests/Ui/BundleHarness.cs` and `Ui/ExperienceTestHarness.cs` (UiTreeAssertions incl. golden-snapshot support) are referenced by no test (grep). `ResolveDevFlutterAppPath` coverage is split across `tests/DigitalBrain.Tests/Aspire/ResolveDevFlutterAppPathTests.cs` (null case) and `tests/DigitalBrain.TestKit.Tests/Aspire/ResolveDevFlutterAppPathTests.cs` (repo-root case); `DigitalBrain.TestKit.Tests` otherwise contains only two smoke tests. (FACT)
- **Current behavior**: Dead helper code compiled on every run; one production function's tests live in two projects.
- **Recommendation**: (PROPOSAL) Delete `BundleHarness`/`ExperienceTestHarness` (or write the pack-bundle tests they were built for); merge the path tests into one file; consider folding `TestKit.Tests` into `DigitalBrain.Tests`.
- **Deletion/simplification opportunity**: Yes — ~230 lines + potentially a whole project.
- **Effort**: S

### FRAME-600: Reflection-based structural tests are brittle by construction
- **Severity**: Note
- **Confidence**: High
- **Evidence**: `tests/DigitalBrain.Tests/Runtime/InoReminderCadenceTests.cs:35-48` (private static field names + private instance timer field types); `LegacyInoPipelineRemovalTests.cs:26-58` (absence of legacy type names — passes forever once types are gone). (FACT)
- **Current behavior**: Cadence tests guard a real Orleans-runtime invariant (reminder ≥ 1 min) that would otherwise only fail in production; the removal tests are a temporary migration guard.
- **Why it matters**: (INFERENCE) Renaming a private field silently detaches the cadence guard (`field?.GetValue(null)` → `Assert.IsType` fails, so at least it fails loudly — acceptable). The removal tests will never fail again and should be retired once the migration is old.
- **Recommendation**: (PROPOSAL) Keep cadence tests; delete `LegacyInoPipelineRemovalTests` in a future cleanup pass (it already guards types deleted several commits ago).
- **Effort**: S

---

## Answers to subsystem questions

**1. Genuinely covered vs. theatre.**
Genuine and strong: OAuth connector security (state opacity, PKCE, replay, tamper, principal-scoped tokens, redirect allowlists, config pinning/rotation — `OAuthConnectorSecurityTests`, `SalesforceOAuthStartNeuronTests`, `OAuthCallbackPathTests`, `AuthorizationFlowStartProxyTests`); encrypted runtime state (tamper/wrong-key fail-closed, KEK rewrap, write rollback, lease fencing, approval actor-binding, immutable-intent enforcement — `EncryptedDomainStateTests`); self-evolution decision gate (approve-before-apply, risk ceilings, idempotent apply across replay — `SelfEvolutionNeuronTests`, `SelfEvolutionDurabilityTests`); provider boundary hardening (Gmail metadata-only + injection rejection, Salesforce SOQL/SOSL escaping + identity-endpoint allowlisting + preview/apply/verify); Ino durable pipeline (reminder handoff, conflict reconciliation without re-execution, trace hygiene).
Theatre: the ChatFileAttachment BDD feature (TEST-600); the `CapabilityIsolationGate`/`ModelRouter`/`DeploymentPreviewer`/`CommitSeal` portions of `ContractsTests` (SEC-600/CLEAN-600); the IConnector contract base + dummy (TEST-601); `ManagedIdentityStorageSelectionTests`' first half (TEST-602); `FoundryFakesTests` (TEST-605); the "client disconnect" pantomime (TEST-604); `AzureResourceControllerTests` (dry-run property echo).

**2. Security-critical invariants — asserted vs. missing.**
Asserted by real tests: approval-before-apply (self-evolution + conversation approval transitions + `InoMutationGrants` provider-write grants); token isolation (pack-config scope isolation, principal-scoped OAuth tokens, cross-principal emptiness); least-privilege scopes (Google readonly+send only; Salesforce api+refresh_token; grants allowlists in OIDC options); no-secret-in-logs/telemetry (chat telemetry content suppression even with OTel env enabled; Ino trace tag denylist; Gmail/Salesforce result-surface non-containment; health-check metadata-only key list); fail-closed crypto/config defaults (production checkpoint key, KEK size, storage connection strings, OAuth internal origin https, UI bootstrap forbidden in production).
**No test exists for**: cross-tenant access through a live request path (isolation is only unit-tested on key derivation and an unwired gate — SEC-600); sandbox runtime confinement and reflection escapes (SEC-601); self-evolution post-apply verification and pack signing (TEST-608); login brute-force/lockout (`UserSessionNeuronTests` covers one failed attempt only); MCP transport rate-limiting behavior beyond body-size (`RuntimeTransportBoundaryTests` covers only the body-size feature, not the concurrency/rate options it constructs).

**3. Framework usage.**
Orleans TestingHost: correct modern usage — `InProcessTestClusterBuilder`/`InProcessTestCluster`, `ISiloConfigurator`, `DeactivateAsync` for reactivation tests, memory streams/storage/reminders, explicit `ResponseTimeout` tuning; the hand-rolled `FakeGrainContext` is documented as reflection-verified against the pinned 10.2.1-preview.1 assembly (TestingHost provides no fake — accurate). Journaling alpha APIs correctly fenced behind `ORLEANSEXP005` pragmas/NoWarn. xUnit 2.9.x: assembly-level `CollectionBehavior(MaxParallelThreads = 2)` with rationale; collection fixtures used correctly; env-mutating telemetry test correctly placed in a `DisableParallelization` collection (unlike TestKit's env write — TEST-606). Aspire.Hosting.Testing: `DistributedApplicationTestingBuilder.CreateAsync(programType, args)` with a deliberate no-Build/no-Start policy — a correct, cheap use of the testing builder for topology assertions. Reqnroll 3.3.4: generation pipeline works; the single feature's content is the problem, not the framework. Xunit.SkippableFact: present only because Reqnroll's xUnit generator emits `SkippableFact`; skips occur only for `@ignore` tags — **no security coverage is silently skipped**. Context7 had no version-specific gaps material to these judgements; Orleans fake-context and Azure SDK subclass-mocking patterns match current vendor guidance (Azure.Core mocking via protected ctors + virtuals, as the tests themselves cite).

**4. Reqnroll scenarios.** One feature exists; both scenarios are vacuous (TEST-600). The BDD layer currently subtracts value (3 packages + generated code for zero coverage). Either invest (drive real grains) or delete.

**5. Architecture tests.** Real and load-bearing. `CoreBoundaryTests` enforces the dependency direction the OS model requires (Core references nothing; contracts depend on Core; no Aspire/Azure/Google/Grpc/MCP leakage into Core/Ui.Contracts/Ui.Runtime) via actual assembly-reference inspection. `AsyncContractArchitectureTests` enforces cancellation-token conventions across 12 public contracts plus analyzer severity pinning in `.editorconfig`. `KernelCompositionTests` and `LegacyInoPipelineRemovalTests` extend this with DI-graph and dead-type guards. None are theatre.

**6. Flakiness/shared-state/over-mocking.**
Hazards: process-wide `DIGITALBRAIN_TEST_MODE` never restored (TEST-606); 1s poll windows (TEST-607); barrier-`TimeProvider` tests blocking inside `GetUtcNow` keyed on activity names (clever, bounded, but implementation-coupled — first place to look when Ino tests flake); static recording handlers (`DurableRecordingApplyHandler`, `WorkflowCalls`, `_workflowCalls`) mitigated by unique IDs/serialized collections but fragile to reuse; `OAuthStateProtectorTests` real-clock expiry (generous margins, low risk). Over-mocking is notably *rare*: fakes sit at transport/provider seams (`HttpMessageHandler`, `IPersistentState`, `IChatClient`) while production logic (transitions, connectors, clients, grains) runs for real; `RuntimeSurfaceFeedTests`' grain fakes delegate to the real transition functions.

**7. Top untested production risks (mapped):**
1. Cross-tenant/workspace access via live MCP/UI request paths — no end-to-end negative test; the one "isolation gate" test targets unwired code (SEC-600).
2. Foundry/sandbox escape via reflection and absent runtime confinement — gate self-documents the bypass; zero escape-attempt tests (SEC-601).
3. Self-evolution verify phase + pack signature validation — unproven (TEST-608).
4. Chat file-attachment end-to-end pipeline — only vacuous BDD plus disjoint unit tests (TEST-600).
5. Program.cs managed-identity storage switch — logic duplicated into the test, drift-blind (TEST-602).
6. Login brute-force/lockout and MCP rate-limit/concurrency enforcement — no coverage.
