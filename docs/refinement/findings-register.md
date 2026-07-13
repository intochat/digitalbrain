# Findings Register

Canonical list of all findings from the file-by-file audit at commit `72400e3ebbec27e17af4ae6b5b2c4158c2797fa4`. **337 findings** total. Full evidence (file:line, current behavior, why it matters, recommendation, effort, rollback) lives in the per-subsystem documents under [`file-audit/`](file-audit/); this register is the authoritative index and cross-reference.

## ID convention

Each finding is referenced as **`<subsystem>:<ID>`** (e.g. `kernel-runtime:SEC-050`). The subsystem names the owning [`file-audit/`](file-audit/) document; the ID is that document's local finding id. This namespacing is required because the parallel audits reused category-numbers across subsystems (e.g. `SEC-100` exists in both `kernel-runtime` and `kernel-hosting` with different meanings). Categories: **SEC** security · **ARCH** architecture · **REL** reliability · **PERF** performance · **PROD** product/correctness · **FRAME** framework/dependency · **CLEAN** cleanup/dead-code · **TEST** test quality.

**Subsystem → document map.** Every subsystem prefix maps to its `file-audit/<name>.md` document of the same name, with one exception: **`connectors`** → [`file-audit/connectors-and-contracts.md`](file-audit/connectors-and-contracts.md). The others are: [core](file-audit/core.md), [kernel-runtime](file-audit/kernel-runtime.md), [kernel-hosting](file-audit/kernel-hosting.md), [foundry](file-audit/foundry.md), [mcp-hosts-build](file-audit/mcp-hosts-build.md), [dotnet-tests](file-audit/dotnet-tests.md), [flutter-runtime](file-audit/flutter-runtime.md), [flutter-ui](file-audit/flutter-ui.md), [flutter-sdk-and-tests](file-audit/flutter-sdk-and-tests.md), [platform-and-skills](file-audit/platform-and-skills.md).

## Reconciliation notes

- **Duplicate-pass corroboration.** kernel-runtime, kernel-hosting, and foundry were each audited twice by independent parallel passes using different id blocks. Both passes' findings are retained (they corroborate and, in several cases, add distinct findings). Where two ids describe the same defect they are cross-noted below; they are **not** double-counted as distinct risks in the executive assessment. Examples of same-defect pairs: `kernel-runtime:SEC-050`≡`kernel-runtime:SEC-100` (forged approval); `foundry:SEC-150`≡`foundry:SEC-300` (reflection-bypassable gate); `foundry:SEC-152`≡`foundry:SEC-302`≡`foundry:SEC-153` (in-process full-trust execution); `kernel-hosting:SEC-100/101`≡`kernel-runtime:*` auth items.
- **Reachability caveat on the Critical cluster.** The Foundry Criticals are *design/control defects* (absent or broken isolation, ungated deploy, forgeable approval). Their live exploitability today is limited by the fact that no external entry point wires untrusted input into `FoundryRequest` (foundry audit PROD/ARCH), and executor grains are reachable only from inside the Orleans cluster. They are rated Critical because the controls that should make self-evolution safe are absent or non-functional, so wiring the product's own stated roadmap ("teach a new behavior") turns them into in-process RCE. Severity reflects control-absence; the caveat scopes present-day reachability. See [06-security-threat-model.md](06-security-threat-model.md).

## Severity summary

| Severity | Count |
|---|---|
| Critical | 8 (≈7 distinct after pair-merge) |
| High | 47 |
| Medium | 116 |
| Low | 119 |
| Note | 47 |
| **Total** | **337** |

## Findings by category

| Category | Count |
|---|---|
| ARCH | 41 |
| CLEAN | 63 |
| FRAME | 32 |
| PERF | 23 |
| PROD | 28 |
| REL | 51 |
| SEC | 71 |
| TEST | 28 |

## All findings (sorted by severity, then subsystem)

| Severity | Ref | Category | Title |
|---|---|---|---|
| Critical | `kernel-runtime:SEC-050` | SEC | Self-evolution approval identity (DecidedBy) is an unauthenticated free string |
| Critical | `kernel-runtime:SEC-100` | SEC | Self-evolution decisions are unauthenticated and forgeable |
| Critical | `foundry:SEC-150` | SEC | CapabilityGate bypassable via typeof(bannedType) + reflection Invoke |
| Critical | `foundry:SEC-151` | SEC | Generated code can read the credential store / env vars and kill the host (gate gaps) |
| Critical | `foundry:SEC-301` | SEC | ScriptRunner gates against a zero-reference compilation, making the capability gate a no-op for automations |
| Critical | `foundry:SEC-302` | SEC | The only real isolation tier (OutOfProcessSandbox) is registered but never invoked; generated code runs in-process at full trust |
| Critical | `foundry:SEC-304` | SEC | Executor grains (CodeRunNeuron, CodeDeployNeuron) are directly fireable, bypassing the approval rail |
| Critical | `foundry:SEC-308` | SEC | Deploy tier compiles generated code into the kernel with NO capability gate |
| High | `core:ARCH-001` | ARCH | Two parallel identity/scoping systems coexist in Core with no bridge |
| High | `core:ARCH-002` | ARCH | Synapse carries no tenant/workspace/principal; identity is per-message, optional, and stringly-typed |
| High | `kernel-runtime:ARCH-050` | ARCH | PerformKernelSelfUpdate rolling update is simulated, not real |
| High | `kernel-runtime:ARCH-051` | ARCH | Checkpoint/restore is an additive journal snapshot, not a state restore |
| High | `kernel-hosting:ARCH-100` | ARCH | digitalbrain.proto is a dead, divergent contract still generated on both sides |
| High | `kernel-hosting:ARCH-101` | ARCH | Two parallel, unreconciled session/auth systems |
| High | `foundry:ARCH-163` | ARCH | Approval not bound to generated source (human approves prose, not code) |
| High | `connectors:ARCH-400` | ARCH | IConnector is an auth-lifecycle contract, not a capability model — Nth connector cannot be added without kernel edits |
| High | `connectors:ARCH-401` | ARCH | Provider names/prefixes hardcoded inside kernel state validation and grant checks |
| High | `flutter-runtime:ARCH-700` | ARCH | Orphaned legacy v1 gateway rail silently no-ops inside live RFW surfaces |
| High | `core:CLEAN-001` | CLEAN | ~30 verified-dead types across 8+ files (large deletion opportunity) |
| High | `flutter-sdk-and-tests:CLEAN-900` | CLEAN | Dead bundled assets, including a 13.3 MB Lottie binary |
| High | `kernel-runtime:FRAME-050` | FRAME | Trust substrate built on alpha Orleans Journaling (10.2.1-preview.1.alpha.1, ORLEANSEXP005) |
| High | `flutter-sdk-and-tests:FRAME-900` | FRAME | widgetbook: any — unpinned dev tool declared as a production dependency |
| High | `kernel-runtime:PERF-100` | PERF | Neuron journals grow unboundedly; projections are O(journal) per message |
| High | `core:PROD-001` | PROD | Self-evolution decisions are not bound to proposal content (approve-what-you-saw gap); duplicate approval vocabularies |
| High | `platform-and-skills:PROD-1000` | PROD | macOS entitlements lack com.apple.security.network.client — desktop client cannot reach the kernel |
| High | `mcp-hosts-build:PROD-500` | PROD | Production runs on a fully preview/alpha toolchain |
| High | `core:REL-001` | REL | ~40 journaled synapse records rely on implicit positional Orleans field ids; annotation convention is inconsistent |
| High | `kernel-runtime:REL-050` | REL | Neuron journals grow unboundedly — no compaction, truncation, or archival |
| High | `kernel-runtime:REL-051` | REL | Approved proposal can be recorded but never applied after a crash (no retry) |
| High | `kernel-runtime:REL-100` | REL | FireAsync is at-most-once — journaled but possibly undelivered |
| High | `kernel-runtime:REL-101` | REL | Checkpoints embed the full timeline into the journal (superlinear growth) |
| High | `kernel-runtime:REL-103` | REL | Failed self-evolution apply is non-retriable and leaves partial side effects |
| High | `foundry:REL-300` | REL | In-process executor has no timeout, cancellation, or resource cap |
| High | `core:SEC-001` | SEC | LoginRequest carries a plaintext password in the journaled message vocabulary |
| High | `kernel-runtime:SEC-051` | SEC | RequiresHumanApproval is set but never enforced |
| High | `kernel-runtime:SEC-052` | SEC | Apply-risk gate trusts the proposer-supplied Risk |
| High | `kernel-runtime:SEC-056` | SEC | No tenant/principal boundary inside the neuron self-evolution + automation rail |
| High | `kernel-hosting:SEC-100` | SEC | Config flag enables admin/admin dev credentials in any environment and bypasses existing-account passwords |
| High | `kernel-hosting:SEC-101` | SEC | First-login user provisioning is fail-open by default and grants admin |
| High | `kernel-runtime:SEC-101` | SEC | RequiresHumanApproval is never enforced |
| High | `foundry:SEC-152` | SEC | The only real isolation boundary (OutOfProcessSandbox) has zero production consumers |
| High | `foundry:SEC-153` | SEC | AssemblyLoadContext / CSharpScript run generated code fully trusted in-process |
| High | `foundry:SEC-154` | SEC | CapabilityBroker allows HTTP to any host despite "allowlisted domains" contract |
| High | `kernel-hosting:SEC-200` | SEC | DataProtection key ring stored unencrypted in the same blob container as the secrets it protects |
| High | `foundry:SEC-300` | SEC | CapabilityGate is a reflection-bypassable, allow-broad static guardrail presented as a boundary |
| High | `foundry:SEC-303` | SEC | TrustedAutoApply config flag fully bypasses the human-approval rail |
| High | `foundry:SEC-307` | SEC | CapabilityBroker.HttpGetAsync fetches any host despite documented allowlist (SSRF) |
| High | `connectors:SEC-405` | SEC | Pack signing and publisher trust exist but are enforced nowhere |
| High | `dotnet-tests:SEC-600` | SEC | Tenant-isolation gate is asserted only on a class no production code uses |
| High | `dotnet-tests:SEC-601` | SEC | Sandbox/capability-gate tests certify a guardrail the production code itself disclaims as a security boundary; no escape-attempt tests exist |
| High | `flutter-ui:SEC-800` | SEC | Server-emitted UI forwards attacker-controlled synapseType/props into the client's synapse dispatch |
| High | `flutter-ui:SEC-801` | SEC | _SynapseRowWidget builds and sends arbitrary synapse envelopes straight to the gRPC client from server-provided type |
| High | `flutter-ui:SEC-802` | SEC | UiKitLink launches arbitrary server-supplied URIs with no scheme validation |
| High | `foundry:TEST-300` | TEST | No test proves the capability gate blocks reflection, the Deploy path, or the ScriptRunner path |
| High | `dotnet-tests:TEST-600` | TEST | ChatFileAttachment BDD feature is self-referential theatre |
| Medium | `core:ARCH-003` | ARCH | Provider (Google/Salesforce) vocabulary hardcoded in Core |
| Medium | `core:ARCH-004` | ARCH | Synapse.cs is a grab-bag god-file of ~40 unrelated contracts |
| Medium | `core:ARCH-005` | ARCH | Two model-selection authorities; one is test-only |
| Medium | `kernel-runtime:ARCH-053` | ARCH | self-evolution-main singleton assumption is unenforced across multi-key/multi-silo |
| Medium | `kernel-hosting:ARCH-102` | ARCH | Provider concerns hardcoded in kernel hosting |
| Medium | `kernel-hosting:ARCH-103` | ARCH | Login handler triggers Aspire distributed-app orchestration |
| Medium | `foundry:ARCH-164` | ARCH | Artifact identity keyed on spec, not source; no signing |
| Medium | `kernel-hosting:ARCH-200` | ARCH | Kernel gRPC gateway is dead server-side with three-way proto/stub drift |
| Medium | `kernel-hosting:ARCH-201` | ARCH | Google/Salesforce provider concerns hard-wired into kernel hosting and OAuth endpoints |
| Medium | `kernel-hosting:ARCH-202` | ARCH | Orphaned legacy session authority (UserSessionNeuron/DevAuth) parallel to the v2 session gate |
| Medium | `foundry:ARCH-300` | ARCH | Four overlapping code-execution mechanisms with inconsistent gating and duplicated authority |
| Medium | `connectors:ARCH-402` | ARCH | Gmail read model duplicated between integration and kernel with unchecked enum-cast mapping |
| Medium | `connectors:ARCH-403` | ARCH | Security-critical OAuth pending-state machine duplicated and divergent between Google and Salesforce |
| Medium | `connectors:ARCH-404` | ARCH | OAuth flow state persisted as magic-key string dictionaries in the pack-config KV store |
| Medium | `mcp-hosts-build:ARCH-500` | ARCH | Kernel still references the MCP Exe project with zero remaining code usage |
| Medium | `mcp-hosts-build:ARCH-501` | ARCH | MCP surface is write-only — no way for a machine client to observe an operation outcome |
| Medium | `mcp-hosts-build:ARCH-502` | ARCH | Durable MCP-audience sessions are validated but never issued |
| Medium | `mcp-hosts-build:ARCH-504` | ARCH | UI transport silently escalates ui.action sessions with brain.interact |
| Medium | `flutter-runtime:ARCH-701` | ARCH | Global singleton buses with stale ownership story |
| Medium | `flutter-ui:ARCH-800` | ARCH | palette/palette_primitives.dart is unregistered dead code (~810 lines) that anchors two heavy dependencies |
| Medium | `flutter-ui:ARCH-801` | ARCH | Duplicated widget-dispatch authority between ui_registry.dart and UiSurfaceTreeRenderer.build |
| Medium | `flutter-sdk-and-tests:ARCH-900` | ARCH | Perf SDK gateway path is a production no-op end to end |
| Medium | `flutter-sdk-and-tests:ARCH-901` | ARCH | SDK leaks app internals (GlowIcon name, app render tuning) — reverse-direction abstraction leak |
| Medium | `core:CLEAN-002` | CLEAN | Test-only production machinery in Core |
| Medium | `kernel-hosting:CLEAN-101` | CLEAN | A production-dead feature stratum ships inside the kernel (Db, TabularData, Uploads, ChatNeuron, SignalEgress, IngressNeuron) |
| Medium | `kernel-hosting:CLEAN-200` | CLEAN | Nine dead components — the pre-v2 gateway stratum has no production callers |
| Medium | `mcp-hosts-build:CLEAN-500` | CLEAN | docs/ dominated by stale one-shot agent artifacts contradicting the repo's own doc policy |
| Medium | `dotnet-tests:CLEAN-600` | CLEAN | Speculative Core runtime types kept alive only by tests |
| Medium | `flutter-runtime:CLEAN-700` | CLEAN | Dead legacy transport and visualization files |
| Medium | `flutter-sdk-and-tests:CLEAN-901` | CLEAN | "Challenger" tool scripts are dead — one targets deleted files and always fails |
| Medium | `kernel-runtime:FRAME-100` | FRAME | Journal-writer detection relies on a framework message substring |
| Medium | `kernel-hosting:FRAME-200` | FRAME | Dual Orleans provider configuration — Aspire config-driven and manual explicit clients for the same providers |
| Medium | `foundry:FRAME-300` | FRAME | Roslyn compilation/scripting used as an isolation mechanism it is not designed to provide |
| Medium | `mcp-hosts-build:FRAME-500` | FRAME | Orleans stable/preview version skew across one runtime family |
| Medium | `mcp-hosts-build:FRAME-502` | FRAME | DeveloperForce.Force 2.1.0 — effectively unmaintained client on the external-write path |
| Medium | `flutter-ui:FRAME-800` | FRAME | Color tokens are misnamed — teal/gold/violet/indigo all resolve to near-identical silver |
| Medium | `flutter-sdk-and-tests:FRAME-901` | FRAME | Nine dead dependencies (plus a pin justified by a dead package) |
| Medium | `kernel-hosting:PERF-100` | PERF | TabularDataParser has no input-size bound (zip-bomb / memory exhaustion if wired) |
| Medium | `connectors:PERF-400` | PERF | Token-endpoint round trip on (nearly) every provider operation |
| Medium | `connectors:PERF-401` | PERF | Gmail metadata window issues sequential N+1 message gets with client-side filtering |
| Medium | `connectors:PERF-402` | PERF | Salesforce global describe + object describe on every request, twice per mutation |
| Medium | `mcp-hosts-build:PERF-500` | PERF | Feed delivery is poll-per-client with grain-write-per-item |
| Medium | `mcp-hosts-build:PERF-501` | PERF | Long-lived feed streams share the 32-slot concurrency budget with all edge traffic |
| Medium | `flutter-ui:PERF-800` | PERF | GlassMaterial runs a per-frame ticker + async shader load that are permanently dead |
| Medium | `flutter-ui:PERF-801` | PERF | Ino editor re-runs whole-document regex highlighting/autocomplete on every keystroke and build |
| Medium | `core:PROD-002` | PROD | Proposal expiry is optional and unbounded at the type level |
| Medium | `core:PROD-003` | PROD | Risk tier and human-approval requirement are proposer-asserted |
| Medium | `kernel-runtime:PROD-100` | PROD | GeneratedNeuron Gmail insights ships fabricated sample data as "analyzed locally" |
| Medium | `platform-and-skills:PROD-1001` | PROD | Android has no INTERNET permission in any checked-in manifest |
| Medium | `platform-and-skills:PROD-1002` | PROD | Microphone capability undeclared on Android, iOS and macOS despite shipping voice input |
| Medium | `kernel-runtime:PROD-101` | PROD | PerformKernelSelfUpdate is a simulation presented as rolling-update capability |
| Medium | `foundry:PROD-300` | PROD | CapabilityBroker capabilities are placeholders presented as real |
| Medium | `foundry:PROD-301` | PROD | AzureResourceController.RestartKernelAsync is a TODO no-op |
| Medium | `connectors:PROD-400` | PROD | Gmail send duplicate suppression is non-atomic and subject to search-indexing lag |
| Medium | `connectors:PROD-402` | PROD | GoogleConnector.ValidateConfigAsync demands redirect_uri that every other path defaults |
| Medium | `connectors:PROD-404` | PROD | Swallowed store-read failure turns outages into "credential form needed" |
| Medium | `mcp-hosts-build:PROD-501` | PROD | MCP edge is a pinned single replica, single revision |
| Medium | `flutter-sdk-and-tests:PROD-900` | PROD | rebuildsPerSecond actually reports frames per second |
| Medium | `core:REL-002` | REL | = null! list defaults on journaled automation records |
| Medium | `kernel-runtime:REL-052` | REL | Transient apply failure permanently blocks the proposal |
| Medium | `kernel-runtime:REL-053` | REL | Full-journal replay on every activation / projection rebuild |
| Medium | `kernel-hosting:REL-100` | REL | UserSessionNeuron derives all state by full-journal scans; unbounded growth |
| Medium | `platform-and-skills:REL-1000` | REL | Production web bootstrap is gated behind a fetch of kernel_port.txt that nothing produces |
| Medium | `kernel-hosting:REL-101` | REL | Pack-config blob addressing breaks silently on signing-key rotation |
| Medium | `kernel-runtime:REL-102` | REL | RestoreCheckpointAsync appends without truncating |
| Medium | `kernel-runtime:REL-104` | REL | AutomationDefinitionApplyHandler define is non-atomic |
| Medium | `kernel-runtime:REL-105` | REL | PollTriggerNeuron dedup is not durable |
| Medium | `kernel-runtime:REL-106` | REL | ScheduleTriggerNeuron ignores the cron schedule |
| Medium | `foundry:REL-165` | REL | Fragile CWD-relative path resolution + non-atomic overwrite in deploy |
| Medium | `foundry:REL-166` | REL | InProcessAlcExecutor mutates process-global Console.Out under concurrency |
| Medium | `foundry:REL-167` | REL | ScriptRunner unbounded compile cache + swallowed gate errors |
| Medium | `kernel-hosting:REL-200` | REL | UserSessionNeuron journal grows unbounded; every session/user lookup is a full-journal scan |
| Medium | `kernel-hosting:REL-201` | REL | PackConfigStore silently drops undecryptable values and rewrites whole dictionaries — transient decrypt failure can become permanent credential loss |
| Medium | `foundry:REL-301` | REL | Console.SetOut swap in the executor is process-global and not thread-safe |
| Medium | `foundry:REL-302` | REL | Orchestrator reads grain timelines immediately after firing, assuming synchronous same-turn ordering |
| Medium | `connectors:REL-400` | REL | No retry/backoff/rate-limit strategy in either integration |
| Medium | `connectors:REL-401` | REL | Auth/permission failure classification by exception-message substring matching |
| Medium | `mcp-hosts-build:REL-500` | REL | MCP readiness never reflects Orleans connectivity; health-check comment is false |
| Medium | `mcp-hosts-build:REL-501` | REL | Tracked .config/.tools-restored sentinel defeats tool restore and its own clean-up story |
| Medium | `flutter-runtime:REL-700` | REL | Reconnect loop can bypass backoff indefinitely |
| Medium | `flutter-ui:REL-800` | REL | DigitalBrainCatalogManager singleton never invalidates _loaded |
| Medium | `core:SEC-002` | SEC | LocalUserRegistered puts password hash + salt on the synapse timeline |
| Medium | `core:SEC-003` | SEC | WorkspaceIds.VectorCollection sanitizer can collide distinct principals into one collection |
| Medium | `core:SEC-004` | SEC | Anonymous-by-default principal baked into contracts |
| Medium | `kernel-runtime:SEC-053` | SEC | Foundry TrustedAutoApply is a config-gated bypass of the rail |
| Medium | `kernel-runtime:SEC-054` | SEC | AutomationNeuron.DefineReactionAsync bypasses the approval rail |
| Medium | `kernel-runtime:SEC-055` | SEC | GeneratedNeuron executes journal-sourced pack code with only a try/catch |
| Medium | `kernel-hosting:SEC-102` | SEC | DataProtection key ring persisted to blob without at-rest key encryption |
| Medium | `kernel-runtime:SEC-102` | SEC | IAutomationNeuron.DefineReactionAsync bypasses the approval rail |
| Medium | `foundry:SEC-155` | SEC | TrustedAutoApply config bypasses human approval for the highest-risk surface |
| Medium | `foundry:SEC-156` | SEC | CodeGen prompt built from unsanitized Spec/Hints (prompt-injection vector) |
| Medium | `foundry:SEC-157` | SEC | ProcessRunner command blocklist is incomplete security theater |
| Medium | `kernel-hosting:SEC-201` | SEC | Plaintext password persisted into the durable synapse journal by the legacy login path |
| Medium | `kernel-hosting:SEC-202` | SEC | First-user provisioning is fail-open — first login creates an admin account with arbitrary credentials |
| Medium | `foundry:SEC-306` | SEC | OutOfProcessSandbox has process isolation but no resource/filesystem/network limits |
| Medium | `connectors:SEC-400` | SEC | Google OAuth flow has no PKCE (Salesforce does) |
| Medium | `connectors:SEC-401` | SEC | No provider-side token revocation anywhere; credential wipes orphan live refresh tokens |
| Medium | `connectors:SEC-404` | SEC | Plaintext default password embedded in a journal-eligible UiSurface synapse |
| Medium | `mcp-hosts-build:SEC-500` | SEC | Build- and agent-time execution of unpinned third-party npm packages |
| Medium | `mcp-hosts-build:SEC-501` | SEC | Tenant/workspace isolation delegated entirely to the external IdP's claims |
| Medium | `flutter-runtime:SEC-700` | SEC | Web OIDC sign-in uses the deprecated implicit flow (no PKCE) |
| Medium | `flutter-runtime:SEC-701` | SEC | Legacy interceptor injects client-asserted identity headers |
| Medium | `flutter-ui:SEC-803` | SEC | _CodeEditorBody._runCompileAndStage presents a fabricated "compiled successfully" result on the self-evolution mutation path |
| Medium | `core:TEST-001` | TEST | No serialization contract-freeze tests for the journaled vocabulary |
| Medium | `kernel-runtime:TEST-100` | TEST | Self-evolution authorization and partial-apply recovery are untested |
| Medium | `foundry:TEST-170` | TEST | No coverage for the foundry loop or the gate bypasses |
| Medium | `foundry:TEST-301` | TEST | No test covers the TrustedAutoApply bypass or the foundry→rail→apply integration |
| Medium | `connectors:TEST-400` | TEST | Strong OAuth/contract test coverage; identified untested hazards |
| Medium | `mcp-hosts-build:TEST-500` | TEST | No direct tests for the MCP tool surface and host pipeline composition |
| Medium | `dotnet-tests:TEST-601` | TEST | IConnector contract-test base is largely vacuous and its security TODOs are unowned |
| Medium | `dotnet-tests:TEST-602` | TEST | ManagedIdentityStorageSelectionTests re-implements the production branch inside the test |
| Medium | `dotnet-tests:TEST-606` | TEST | Process-wide DIGITALBRAIN_TEST_MODE is set by the cluster harness and never restored |
| Medium | `dotnet-tests:TEST-608` | TEST | Self-evolution "verify" phase and pack signing have no tests |
| Medium | `flutter-runtime:TEST-700` | TEST | Telemetry subsystem has zero tests |
| Medium | `flutter-sdk-and-tests:TEST-900` | TEST | Coverage is siloed — runtime excellent, everything else untested |
| Medium | `flutter-sdk-and-tests:TEST-901` | TEST | SDK package has no tests at all |
| Low | `core:ARCH-006` | ARCH | TaskId two-way implicit string conversion erases the type |
| Low | `core:ARCH-007` | ARCH | McpContracts.cs is a speculative port layer; its one live type is consumed from an integration |
| Low | `kernel-runtime:ARCH-052` | ARCH | MetaOptimizer emits LLM-generated WiringOptimizationProposed that dead-ends (latent injection vector) |
| Low | `connectors:ARCH-405` | ARCH | Kernel.Abstractions is a grab-bag; Neuron is a full implementation living in an "Abstractions" assembly |
| Low | `mcp-hosts-build:ARCH-503` | ARCH | Shared runtime transport lives inside the MCP Exe project |
| Low | `flutter-ui:ARCH-802` | ARCH | ui_kit (the extractable design system) depends on app features/ code |
| Low | `core:CLEAN-003` | CLEAN | Dangling <see cref> to hosting-layer type |
| Low | `core:CLEAN-004` | CLEAN | Legacy compat constructor with no retirement trigger |
| Low | `kernel-runtime:CLEAN-050` | CLEAN | Dead code in GeneratedNeuron (EmitConfigFormIfRequiredAsync, LastInstalledPack) |
| Low | `kernel-runtime:CLEAN-051` | CLEAN | Empty catch { } swallows pack-config-store faults |
| Low | `kernel-runtime:CLEAN-052` | CLEAN | ScheduleTriggerNeuron ignores the cron Schedule; fixes 1-minute period |
| Low | `kernel-hosting:CLEAN-100` | CLEAN | SyncManifest is dead code |
| Low | `kernel-runtime:CLEAN-100` | CLEAN | Dead installed-pack path in GeneratedNeuron |
| Low | `platform-and-skills:CLEAN-1000` | CLEAN | Inconsistent application identity across platforms; default project-name branding in user-visible strings |
| Low | `kernel-runtime:CLEAN-101` | CLEAN | Test-only FailAtReplica field in a production handler |
| Low | `kernel-hosting:CLEAN-102` | CLEAN | Kernel registers gRPC + grpc-web + CORS with no gRPC service mapped |
| Low | `kernel-runtime:CLEAN-102` | CLEAN | WiringOptimizationProposed is produced but never routed to the rail |
| Low | `kernel-hosting:CLEAN-103` | CLEAN | GeneratedPackRuntime.Ensure ignores its journal parameter; Generated/ folder misnames human code |
| Low | `kernel-runtime:CLEAN-103` | CLEAN | JournalJson polymorphism is shadowed by EncryptedSynapseJsonConverter in production |
| Low | `kernel-hosting:CLEAN-104` | CLEAN | Vacuous health checks, vacuous override, duplicated registrations |
| Low | `kernel-runtime:CLEAN-104` | CLEAN | IKernelTask is a dead contract |
| Low | `foundry:CLEAN-158` | CLEAN | CapabilityGate class comment is stale and self-contradicting |
| Low | `foundry:CLEAN-159` | CLEAN | CapabilityBroker methods are fabricating stubs behind a real interface |
| Low | `foundry:CLEAN-160` | CLEAN | Kernel restart is a no-op/TODO in both local and cloud |
| Low | `foundry:CLEAN-161` | CLEAN | Two divergent reference strategies (whole runtime dir vs TPA) |
| Low | `kernel-hosting:CLEAN-201` | CLEAN | PrototypeJournals types live in the global namespace |
| Low | `kernel-hosting:CLEAN-202` | CLEAN | Vacuous overrides and duplicated blocks |
| Low | `kernel-hosting:CLEAN-203` | CLEAN | Hand-written code under Generated/ |
| Low | `kernel-hosting:CLEAN-204` | CLEAN | Duplicated Aspire-detection logic reading raw environment variables |
| Low | `foundry:CLEAN-300` | CLEAN | ScriptRunner and CapabilityGate carry dead/misleading comment blocks |
| Low | `foundry:CLEAN-301` | CLEAN | Duplicated FindStagedAsync/Failed across both apply handlers |
| Low | `connectors:CLEAN-400` | CLEAN | Dead null-store scaffolding and speculative nullability in GoogleConnector |
| Low | `connectors:CLEAN-401` | CLEAN | Unreachable final branch in SalesforceClientFactory.CreateOAuthSessionAsync |
| Low | `connectors:CLEAN-402` | CLEAN | Unused/vestigial API surface across the factories |
| Low | `connectors:CLEAN-403` | CLEAN | Demo/sample surfaces shipped inside the packable Ui.Runtime; duplicated helper |
| Low | `connectors:CLEAN-404` | CLEAN | ISalesforceApiClient default interface methods silently report capabilities as unavailable |
| Low | `mcp-hosts-build:CLEAN-501` | CLEAN | CLAUDE.md's MCP standalone-run instruction is dead |
| Low | `mcp-hosts-build:CLEAN-502` | CLEAN | Dead DI registrations in the MCP host |
| Low | `dotnet-tests:CLEAN-601` | CLEAN | Unused Grpc.Net.Client.Web package reference |
| Low | `dotnet-tests:CLEAN-602` | CLEAN | Dead test harnesses and a split micro-project |
| Low | `flutter-runtime:CLEAN-701` | CLEAN | flutter_bloc + TelemetryBlocObserver + bloc_test with zero blocs |
| Low | `flutter-runtime:CLEAN-702` | CLEAN | Dependency hygiene — unused shared_preferences; widgetbook in prod deps with any |
| Low | `flutter-ui:CLEAN-800` | CLEAN | graphic ^2.7.0 dependency is unused |
| Low | `flutter-ui:CLEAN-801` | CLEAN | Duplicated DataSource reader helpers across the RFW library |
| Low | `flutter-ui:CLEAN-802` | CLEAN | ui_graph_canvas "force" layout is actually a grid |
| Low | `flutter-ui:CLEAN-803` | CLEAN | adaptiveVisualDensity claims to replace adaptivePlatformDensity but isn't wired in |
| Low | `flutter-ui:CLEAN-804` | CLEAN | LiquidGlassShaderPainter + shader asset are dead |
| Low | `flutter-sdk-and-tests:CLEAN-902` | CLEAN | Circuit-breaker coverage lives in a tool script instead of a test |
| Low | `flutter-sdk-and-tests:CLEAN-903` | CLEAN | PerfTierThresholds is exported dead code |
| Low | `flutter-sdk-and-tests:CLEAN-904` | CLEAN | Demo entries and undocumented magic numbers in ino-catalog.json |
| Low | `core:FRAME-002` | FRAME | "Pure stable layer" package pinned to preview/alpha Orleans line |
| Low | `core:FRAME-003` | FRAME | Undeclared direct dependency on Microsoft.Extensions.Configuration |
| Low | `foundry:FRAME-168` | FRAME | No retry, no token/cost budget on LLM calls |
| Low | `foundry:FRAME-169` | FRAME | CodeGen uses string prompt + regex extraction, silent stub fallback |
| Low | `connectors:FRAME-401` | FRAME | Hand-rolled Google token exchange bypasses Google.Apis.Auth's flow machinery |
| Low | `mcp-hosts-build:FRAME-501` | FRAME | Four overlapping AI SDK stacks pinned; stale rationale comments |
| Low | `flutter-runtime:FRAME-700` | FRAME | Mixed conditional-import keys break wasm web builds' env access |
| Low | `flutter-runtime:FRAME-702` | FRAME | Manual traceparent with hard-coded sampled flag; spans have no parents |
| Low | `flutter-ui:FRAME-801` | FRAME | DebugBrainStats uses raw fontFamily strings while the app uses google_fonts |
| Low | `flutter-sdk-and-tests:FRAME-902` | FRAME | Analyzer config is the untouched template in both packages |
| Low | `flutter-sdk-and-tests:FRAME-903` | FRAME | Import-boundary allowlist includes apparently nonexistent packages |
| Low | `core:PERF-001` | PERF | Checkpoint and timeline APIs move entire journals as single messages |
| Low | `kernel-runtime:PERF-050` | PERF | Repeated Concat + ToArray snapshots of full journals per query |
| Low | `kernel-runtime:PERF-051` | PERF | Reflection assembly scan for synapse types at silo start |
| Low | `kernel-runtime:PERF-101` | PERF | Reflection dispatch on every delivery for non-static-dispatch grains |
| Low | `kernel-hosting:PERF-200` | PERF | TabularDataParser computes stats over the entire workbook with no row/cell cap |
| Low | `foundry:PERF-300` | PERF | DefaultReferences() rebuilds the full runtime metadata-reference set per execution |
| Low | `connectors:PERF-403` | PERF | New HttpClient per token exchange |
| Low | `flutter-runtime:PERF-700` | PERF | One acknowledgement RPC per accepted feed event |
| Low | `flutter-ui:PERF-802` | PERF | BrainSceneEffects.pulses allocates an unmodifiable copy on every read |
| Low | `flutter-ui:PERF-803` | PERF | GlowIcon cache eviction is insertion-order, not LRU |
| Low | `flutter-sdk-and-tests:PERF-900` | PERF | PerfStream retry pump idles forever; backoff cap overshoots and never resets |
| Low | `kernel-hosting:PROD-100` | PROD | TabularDataParser misaligns headers/data when the header row has blank cells |
| Low | `kernel-hosting:PROD-101` | PROD | Post-login TaskManager surface is built from the wrong journal (always empty) |
| Low | `kernel-hosting:PROD-200` | PROD | Kernel exposes an external h2c "grpc" endpoint that serves nothing but the SPA fallback |
| Low | `connectors:PROD-401` | PROD | Salesforce apply is check-then-update without a conditional write |
| Low | `connectors:PROD-403` | PROD | SalesforceConnector replays an auth challenge even when the credential is already Ready |
| Low | `mcp-hosts-build:PROD-502` | PROD | Deploy authority split between Pulumi and bash with duplicated literals; prod stack named "dev" |
| Low | `flutter-runtime:PROD-701` | PROD | Single-action fallback fires on any unnamed RFW event |
| Low | `flutter-runtime:PROD-702` | PROD | Legacy submission reconciliation matches by prompt text |
| Low | `flutter-ui:PROD-800` | PROD | LlmSettingsPanel and TelemetryPanel ship hardcoded/fake data |
| Low | `flutter-sdk-and-tests:PROD-901` | PROD | Jank threshold hard-coded at 16 ms; PerfTierThresholds unused |
| Low | `core:REL-003` | REL | TelemetryBuffer has no drain — after capacity, everything is dropped forever |
| Low | `core:REL-004` | REL | McpRequestGuard eviction/creation races |
| Low | `core:REL-005` | REL | ConversationSurfacePayload.TurnKey collides for repeated (CommandId, Role) pairs |
| Low | `kernel-hosting:REL-202` | REL | Vacuous health checks report Healthy without probing anything |
| Low | `kernel-hosting:REL-203` | REL | OAuth boundary rate limit is a single global fixed window per replica |
| Low | `kernel-hosting:REL-204` | REL | TabularDataParser misaligns columns when the header row contains blank cells |
| Low | `foundry:REL-303` | REL | ProcessBuildRunner project/path resolution is fragile |
| Low | `mcp-hosts-build:REL-502` | REL | Edge exception handler logs only the exception type name |
| Low | `flutter-runtime:REL-701` | REL | Telemetry circuit breaker trips permanently with no recovery |
| Low | `flutter-ui:REL-801` | REL | Catalog load failures are swallowed to debugPrint |
| Low | `flutter-ui:REL-802` | REL | UiKitToast re-presents on every remount via presentOnce(true, …) |
| Low | `flutter-ui:REL-803` | REL | Inline-RFW documents are keyed by source.hashCode |
| Low | `flutter-sdk-and-tests:REL-900` | REL | PerfStream swallows all pump errors with bare catch (_) |
| Low | `flutter-sdk-and-tests:REL-901` | REL | Flutter.proj build targets are skipped by default and swallow exit codes when enabled |
| Low | `platform-and-skills:SEC-1000` | SEC | Android release build signs with the debug keystore |
| Low | `kernel-hosting:SEC-103` | SEC | Production HTTPS enforcement depends on spoofable forwarded headers when ACA trust flag is on |
| Low | `kernel-hosting:SEC-104` | SEC | PBKDF2 iteration count below current guidance; no password policy |
| Low | `kernel-hosting:SEC-105` | SEC | IngressNeuron is an unauthenticated arbitrary-signal broadcast (currently unreachable) |
| Low | `kernel-hosting:SEC-203` | SEC | Seeded admin/admin credentials can be enabled in Production by one config flag |
| Low | `kernel-hosting:SEC-204` | SEC | TrustAzureContainerAppsIngress clears all forwarded-header validation; OAuth HTTPS gate becomes spoofable if the pod is directly reachable |
| Low | `foundry:SEC-305` | SEC | CapabilityGate header comment is stale/misleading about the reflection bypass and a deleted doc |
| Low | `connectors:SEC-402` | SEC | Raw token-endpoint response bodies embedded in exception messages |
| Low | `connectors:SEC-403` | SEC | Salesforce login-host allowlist accepts any *.salesforce.com and *.site.com |
| Low | `mcp-hosts-build:SEC-502` | SEC | ACA forwarded-headers trust clears all proxy allowlists |
| Low | `mcp-hosts-build:SEC-504` | SEC | No dependency/SAST scanning in CI |
| Low | `flutter-runtime:SEC-702` | SEC | Raw error.toString() exported to telemetry backends |
| Low | `flutter-runtime:SEC-703` | SEC | Web ?port= query parameter retargets the kernel endpoint (dead path) |
| Low | `flutter-sdk-and-tests:SEC-900` | SEC | Real deployment hostnames embedded in test code |
| Low | `core:TEST-002` | TEST | Security helpers with zero callers also have zero meaningful tests |
| Low | `kernel-hosting:TEST-200` | TEST | Tests lock in dead components, signaling false liveness |
| Low | `kernel-hosting:TEST-201` | TEST | No tests for the hosting composition's riskiest behaviors |
| Low | `dotnet-tests:TEST-603` | TEST | Opaque blob-name expectation re-derives the production algorithm in the test |
| Low | `dotnet-tests:TEST-604` | TEST | Fake "client disconnect" in the durability test proves nothing about disconnects |
| Low | `dotnet-tests:TEST-605` | TEST | FoundryFakesTests tests the test fakes |
| Low | `dotnet-tests:TEST-607` | TEST | Hand-rolled poll loops with tight/silent timeouts instead of the shared throwing helper |
| Low | `flutter-ui:TEST-800` | TEST | Large RFW stateful widgets (editor, synapse-row, catalog manager) lack focused tests |
| Low | `flutter-sdk-and-tests:TEST-902` | TEST | bloc_test declared but never used |
| Note | `platform-and-skills:ARCH-1000` | ARCH | Vendored Aspire skills duplicate/conflict with CLAUDE.md's way-of-working and have no refresh process |
| Note | `flutter-sdk-and-tests:ARCH-902` | ARCH | Package named "SDK" is a perf-only helper, not a client SDK |
| Note | `kernel-runtime:CLEAN-053` | CLEAN | Dockerfile copies whole context on a preview base |
| Note | `platform-and-skills:CLEAN-1001` | CLEAN | Heavy duplication inside vendored monitoring skill content |
| Note | `foundry:CLEAN-302` | CLEAN | SandboxTier.Wasm is an aspirational enum member with no implementation |
| Note | `foundry:CLEAN-303` | CLEAN | DigitalBrainLlmRuntimeOptions carries vacuous /// <summary> blocks against the repo rule |
| Note | `connectors:CLEAN-405` | CLEAN | Legacy RfwCard/IChatNeuron duplicate the UiSurface RFW path; UI vocabulary sprawl |
| Note | `mcp-hosts-build:CLEAN-503` | CLEAN | Redundant per-call/config re-validation in the MCP auth path |
| Note | `mcp-hosts-build:CLEAN-504` | CLEAN | Cosmetic config contradictions |
| Note | `mcp-hosts-build:CLEAN-505` | CLEAN | Stale deploy metadata and legacy naming |
| Note | `flutter-runtime:CLEAN-703` | CLEAN | SurfaceView._actionLabel ignores its parameter |
| Note | `flutter-runtime:CLEAN-704` | CLEAN | Stale comments reference deleted screens |
| Note | `core:FRAME-001` | FRAME | Orleans annotation usage verified correct (positive finding) |
| Note | `core:FRAME-004` | FRAME | Stale comment contradicts JsonElementSurrogate |
| Note | `kernel-hosting:FRAME-100` | FRAME | ClosedXML usage could not be doc-verified (Context7 quota exhausted) |
| Note | `kernel-hosting:FRAME-101` | FRAME | Preview/alpha framework stack in the trusted kernel |
| Note | `kernel-hosting:FRAME-201` | FRAME | Alpha journaling APIs (ORLEANSEXP005) with no public documentation — recorded gap |
| Note | `foundry:FRAME-301` | FRAME | Anthropic/MEAI experimental API suppressed repo-wide |
| Note | `connectors:FRAME-400` | FRAME | Pinned SDK usage could not be verified against current docs (Context7 quota exhausted) |
| Note | `connectors:FRAME-402` | FRAME | Mutation-path dependency on dormant DeveloperForce.Force 2.1.0 |
| Note | `mcp-hosts-build:FRAME-503` | FRAME | Framework-usage verification status and documentation gaps |
| Note | `mcp-hosts-build:FRAME-504` | FRAME | Vestigial always-true TFM condition in AppHost csproj |
| Note | `dotnet-tests:FRAME-600` | FRAME | Reflection-based structural tests are brittle by construction |
| Note | `flutter-runtime:FRAME-701` | FRAME | Hand-rolled OTLP metric export emits histograms without buckets |
| Note | `core:PERF-002` | PERF | Payload trim loop re-serializes per removed message (accepted) |
| Note | `mcp-hosts-build:PERF-502` | PERF | Double full authentication per MCP tool call |
| Note | `flutter-sdk-and-tests:PERF-901` | PERF | Widget census walks the full element tree on the UI isolate |
| Note | `core:PROD-004` | PROD | Rollback is free-text except on the Foundry path |
| Note | `mcp-hosts-build:PROD-503` | PROD | Production images pulled from a private personal Docker Hub account |
| Note | `core:REL-006` | REL | SessionPair.AccessExpiresAt = default sentinel |
| Note | `platform-and-skills:REL-1001` | REL | No Podfile checked in for iOS/macOS despite native-plugin dependencies (dependency-manager path unverified) |
| Note | `connectors:REL-402` | REL | ForceClient calls are not cancellable; DeveloperForce.Force is dormant |
| Note | `mcp-hosts-build:REL-503` | REL | Cold-start coupling — MCP process dies if the kernel gateway is absent > 2 minutes |
| Note | `mcp-hosts-build:REL-504` | REL | In-memory fixed-window rate limiter is per-replica and reset-on-restart |
| Note | `flutter-runtime:REL-702` | REL | Log batches dropped silently on export failure |
| Note | `flutter-runtime:REL-703` | REL | Typewriter catch-up time unbounded relative to text size |
| Note | `core:SEC-005` | SEC | Audience-agnostic TryValidate overload on SessionTokenService |
| Note | `core:SEC-006` | SEC | Salesforce OAuth host allowlist covers all of *.site.com |
| Note | `platform-and-skills:SEC-1001` | SEC | No Content-Security-Policy on the production web page |
| Note | `kernel-hosting:SEC-106` | SEC | Session lookup by guessable clientId returns live session state |
| Note | `kernel-hosting:SEC-205` | SEC | OAuth state tokens are replayable within their lifetime (nonce generated but never checked) |
| Note | `mcp-hosts-build:SEC-503` | SEC | /oauth/start/{provider} is unauthenticated (accepted, bounded) |
| Note | `flutter-sdk-and-tests:SEC-901` | SEC | ino-catalog.json models a raw token as an ordinary catalog field |
| Note | `platform-and-skills:TEST-1000` | TEST | iOS and macOS Runner test targets are empty placeholders |
| Note | `dotnet-tests:TEST-609` | TEST | Rolling-update rollback is proven only against an in-grain simulation |
| Note | `dotnet-tests:TEST-610` | TEST | [Collection("kernel-host")] on cluster tests that never use the fixture |
| Note | `flutter-runtime:TEST-701` | TEST | Dead/legacy feature code carries no tests (expected, but confirms deletability) |
