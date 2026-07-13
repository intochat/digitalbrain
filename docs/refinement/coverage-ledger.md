# Coverage Ledger

Authoritative per-file audit status for commit `72400e3ebbec27e17af4ae6b5b2c4158c2797fa4`. Each row: path · subsystem · authored · status · reviewed line ranges · primary responsibility · key callers/deps · finding IDs · follow-up.

## Coverage summary

| Metric | Value |
|---|---|
| Tracked files at commit | 728 |
| Files individually reviewed or classified in ledger rows | 711 |
| iOS icon/launch binary PNGs (excluded-generated, documented as a group below) | 17 |
| **Total accounted for** | **728 (100%)** |
| Human-authored source files reviewed line-by-line | see per-subsystem fragments |

Coverage is 100% of tracked files: every human-authored file was read in full (line ranges per row), and every generated/binary artifact is classified `excluded-generated` with its generator named. The 17 iOS `Assets.xcassets` icon/launch PNGs are non-line-auditable binaries produced by `flutter create`/Xcode asset catalogs; they are checked in correctly and current versus `pubspec.yaml`.

> Note: three subsystems (kernel-runtime, kernel-hosting, foundry) received a redundant second parallel audit during production. Findings from both passes are merged into the single canonical subsystem document and the findings register; this ledger lists each file once.

## Subsystem: core


| path | subsystem | authored | status | line ranges reviewed | primary responsibility | key callers/deps | finding IDs | follow-up |
|---|---|---|---|---|---|---|---|---|
| src/DigitalBrain.Core/AssemblyInfo.cs | core | human | reviewed | 1-3 | InternalsVisibleTo for tests | DigitalBrain.Tests | — | none |
| src/DigitalBrain.Core/Automations.cs | core | human | reviewed | 1-125 | Reactive automation synapse vocabulary + IAutomationNeuron | AutomationNeuron, trigger neurons, apply handlers | REL-002, REL-003, SEC-002 | fix null! defaults |
| src/DigitalBrain.Core/CapabilityProfiles.cs | core | human | reviewed | 1-3 | RuntimeProfile enum (Dev/Test/Prod) | Mcp host security decisions | CLEAN-001 | rename file |
| src/DigitalBrain.Core/CodeFoundrySynapses.cs | core | human | reviewed | 1-112 | Foundry generate/run/deploy/rollback vocabulary | Foundry neurons, apply handlers | — | none |
| src/DigitalBrain.Core/Config/IPackConfigStore.cs | core | human | reviewed | 1-8 | Scoped pack config store contract | Kernel PackConfigStore | — | none |
| src/DigitalBrain.Core/Conversation.cs | core | human | reviewed | 1-92 | INO conversation states/turn/operation/snapshot | Mcp pipeline, Kernel runtime | ARCH-003 | converge state vocabularies |
| src/DigitalBrain.Core/ConversationExecutionContracts.cs | core | human | reviewed | 1-9 | External authorization resolution record | Gmail/Salesforce typed tools | — | none |
| src/DigitalBrain.Core/ConversationSurfacePayload.cs | core | human | reviewed | 1-231 | Bounded INO conversation surface payload builder | ConversationNeuron, outbox dispatcher | REL-004, REL-006 | bound SafeReason |
| src/DigitalBrain.Core/DeploymentPreview.cs | core | human | reviewed | 1-34 | Topology drift preview (unused in prod) | ContractsTests only | PROD-002 | delete or wire |
| src/DigitalBrain.Core/DigitalBrain.Core.csproj | core | human | reviewed | 1-19 | Packable contract assembly definition | all consumers | ARCH-001, FRAME-001, FRAME-003 | align description |
| src/DigitalBrain.Core/DistributedAppStarted.cs | core | human | reviewed | 1-5 | App-started event synapse | AspireOrchestratorNeuron | REL-002 | none |
| src/DigitalBrain.Core/DurableInoContracts.cs | core | human | reviewed | 1-365 | Durable INO operation/approval/effect/outbox contracts | InoOperationWorkerGrain, ConversationNeuron, gateways | ARCH-003 | template for SelfEvolution hardening |
| src/DigitalBrain.Core/Experience.cs | core | human | reviewed | 1-18 | Experience (dead) + ExperienceStep synapse | KitExperience (Pack.Contracts) | PROD-001 | delete Experience record |
| src/DigitalBrain.Core/GrpcAuthentication.cs | core | human | reviewed | 1-16 | gRPC metadata token auth helper (dead) | none | PROD-001 | delete |
| src/DigitalBrain.Core/ICheckpointKeyProvider.cs | core | human | reviewed | 1-8 | Checkpoint encryption key source | Kernel CheckpointKeyProviders | — | none |
| src/DigitalBrain.Core/IHandle.cs | core | human | reviewed | 1-6 | Typed synapse handler contract | all neurons | — | none |
| src/DigitalBrain.Core/INeuron.cs | core | human | reviewed | 1-36 | Universal neuron grain contract (fire/journals/checkpoints) | Neuron base, all callers | ARCH-006, SEC-004 | gate diagnostics/restore |
| src/DigitalBrain.Core/INeuronStateProtector.cs | core | human | reviewed | 1-9 | Encryption-at-rest abstraction | Kernel.Abstractions protectors | — | none |
| src/DigitalBrain.Core/JsonElementSurrogate.cs | core | human | reviewed | 1-24 | Orleans surrogate for JsonElement | Orleans serializer | REL-007 | null guard |
| src/DigitalBrain.Core/McpContracts.cs | core | human | reviewed | 1-21 | Page/OperationStatus/McpError + dead ports | Mcp, Kernel runtime | PROD-002 | delete IQueryPort/ICommandPort |
| src/DigitalBrain.Core/McpGuard.cs | core | human | reviewed | 1-92 | Per-principal MCP rate/concurrency guard | Mcp Program.cs | ARCH-001 | move to host |
| src/DigitalBrain.Core/ModelRouting.cs | core | human | reviewed | 1-33 | Policy model selector (tests-only) | ContractsTests only | PROD-002, CLEAN-001 | delete or unify with Models/ |
| src/DigitalBrain.Core/Models/Anthropic/Claude45Haiku.cs | core | human | reviewed | 1-9 | Anthropic Haiku model marker | Aspire DSL | ARCH-005 | none |
| src/DigitalBrain.Core/Models/Anthropic/Opus46.cs | core | human | reviewed | 1-8 | Anthropic Opus model marker | Aspire DSL | ARCH-005 | none |
| src/DigitalBrain.Core/Models/Anthropic/Sonnet46.cs | core | human | reviewed | 1-8 | Anthropic Sonnet model marker | Aspire DSL | ARCH-005 | none |
| src/DigitalBrain.Core/Models/AzureOpenAI/Gpt4oMini.cs | core | human | reviewed | 1-8 | Azure OpenAI model marker | Aspire DSL | ARCH-005 | none |
| src/DigitalBrain.Core/Models/DigitalBrainModelCatalog.cs | core | human | reviewed | 1-144 | Provider ids, capabilities, roles, mutable registry | Aspire DSL, LlmAttribute | ARCH-005 | none |
| src/DigitalBrain.Core/Models/DigitalBrainModelRegistrySnapshot.cs | core | human | reviewed | 1-72 | Config-section registry reader | Kernel runtime options | FRAME-001, ARCH-005 | move out of Core |
| src/DigitalBrain.Core/Models/DigitalBrainModels.cs | core | human | reviewed | 1-32 | Abstract model marker bases | Aspire DSL | ARCH-005 | none |
| src/DigitalBrain.Core/Models/GitHub/Gpt41Mini.cs | core | human | reviewed | 1-8 | GitHub Models marker | Aspire DSL | ARCH-005 | none |
| src/DigitalBrain.Core/Models/GitHub/Gpt41Nano.cs | core | human | reviewed | 1-9 | GitHub Models marker | Aspire DSL | ARCH-005 | none |
| src/DigitalBrain.Core/Models/GitHub/O4Mini.cs | core | human | reviewed | 1-8 | GitHub Models marker | Aspire DSL | ARCH-005 | none |
| src/DigitalBrain.Core/Models/GitHub/TextEmbedding3Small.cs | core | human | reviewed | 1-8 | GitHub embedding marker | Aspire DSL | ARCH-005, CLEAN-001 | none |
| src/DigitalBrain.Core/Models/Ollama/Llama31_8B.cs | core | human | reviewed | 1-9 | Ollama LLM marker | Aspire DSL | ARCH-005 | none |
| src/DigitalBrain.Core/Models/Ollama/MxbaiEmbedLarge.cs | core | human | reviewed | 1-8 | Ollama embedding marker | Aspire DSL | ARCH-005 | none |
| src/DigitalBrain.Core/Models/Ollama/NomicEmbedText.cs | core | human | reviewed | 1-7 | Ollama embedding marker | Aspire DSL | ARCH-005 | none |
| src/DigitalBrain.Core/Models/OpenAI/Gpt54.cs | core | human | reviewed | 1-8 | OpenAI LLM marker | Aspire DSL | ARCH-005 | none |
| src/DigitalBrain.Core/Models/OpenAI/Gpt54Mini.cs | core | human | reviewed | 1-8 | OpenAI LLM marker | Aspire DSL | ARCH-005 | none |
| src/DigitalBrain.Core/Models/OpenAI/Gpt54Nano.cs | core | human | reviewed | 1-9 | OpenAI LLM marker | Aspire DSL | ARCH-005 | none |
| src/DigitalBrain.Core/Models/OpenAI/TextEmbedding3Small.cs | core | human | reviewed | 1-8 | OpenAI embedding marker | Aspire DSL | ARCH-005, CLEAN-001 | none |
| src/DigitalBrain.Core/NeuronActivated.cs | core | human | reviewed | 1-5 | Activation event synapse | Neuron base | REL-002 | none |
| src/DigitalBrain.Core/NeuronId.cs | core | human | reviewed | 1-9 | Neuron identity wrapper | everywhere | — | none |
| src/DigitalBrain.Core/NeuronScope.cs | core | human | reviewed | 1-54 | v1 user/thread scope + workspace/vector naming | Kernel grains, memory store | ARCH-002, SEC-006, REL-005 | merge into v2 scoping |
| src/DigitalBrain.Core/OAuthCallbackPaths.cs | core | human | reviewed | 1-126 | OAuth path/action/URL validation (2 providers hard-coded) | Mcp proxy, surface projection | ARCH-004 | registration-based providers |
| src/DigitalBrain.Core/ProtectedCheckpoint.cs | core | human | reviewed | 1-10 | Encrypted checkpoint record | Kernel CheckpointProtector | SEC-004 | none |
| src/DigitalBrain.Core/RestartResource.cs | core | human | reviewed | 1-10 | Resource restart command synapse | AspireOrchestratorNeuron | REL-002 | none |
| src/DigitalBrain.Core/RuntimeContracts.cs | core | human | reviewed | 1-427 | v2 identity/tenancy/session tokens/envelopes/behavior | Mcp session authority, Kernel runtime | ARCH-001, ARCH-002, SEC-005, PROD-002 | split behavior out |
| src/DigitalBrain.Core/SchemaRegistry.cs | core | human | reviewed | 1-26 | Fail-closed type/version schema registry | Mcp + Kernel hosting | — | none |
| src/DigitalBrain.Core/Sdk/CommandResult.cs | core | human | reviewed | 1-14 | Process execution result record | SDK integration neurons | — | none |
| src/DigitalBrain.Core/Sdk/IAgent.cs | core | human | reviewed | 1-40 | Typed agent metadata via static virtuals | integration neurons, routing | CLEAN-001 | scrub local-path comment |
| src/DigitalBrain.Core/SelfEvolution.cs | core | human | reviewed | 1-120 | Self-evolution propose/decide/apply/rollback vocabulary | SelfEvolutionNeuron, apply handlers | SEC-002 | add approver identity + content hash |
| src/DigitalBrain.Core/SensitiveText.cs | core | human | reviewed | 1-31 | Regex secret redactor (dead) | none | SEC-007, PROD-001 | wire or delete |
| src/DigitalBrain.Core/Signals.cs | core | human | reviewed | 1-68 | Generic Signal/AskLlm carriers + provider signal names | IngressNeuron, LlmResponderNeuron, packs | ARCH-004, FRAME-002 | move provider names out |
| src/DigitalBrain.Core/StartDistributedApp.cs | core | human | reviewed | 1-6 | App-start command synapse | AspireOrchestratorNeuron | REL-002 | none |
| src/DigitalBrain.Core/SurfaceAudience.cs | core | human | reviewed | 1-26 | Surface audience kinds + principal scope hash | surface feed stack | — | none |
| src/DigitalBrain.Core/SurfaceContentHash.cs | core | human | reviewed | 1-26 | Canonical surface content hash | SurfaceFeedNeuron, feed | — | canonical form note |
| src/DigitalBrain.Core/SurfaceEnvelopeWriter.cs | core | human | reviewed | 1-95 | Recipient-scoped surface materialization + token injection | Mcp RuntimeSurfaceFeed | ARCH-001 | move with runtime split |
| src/DigitalBrain.Core/SurfaceFeedContracts.cs | core | human | reviewed | 1-47 | Token-free stored surface/action records + feed paging | surface feed stack | — | none |
| src/DigitalBrain.Core/SurfacePayloadPolicy.cs | core | human | reviewed | 1-39 | Forbidden-key surface payload scan | SurfaceEnvelopeWriter | SEC-003 | blocklist -> schema allowlist |
| src/DigitalBrain.Core/Synapse.cs | core | human | reviewed | 1-371 | Base Synapse + ~45 mixed vocabularies | everything | REL-001, REL-002, SEC-001, ARCH-002, ARCH-004, PROD-001 | split + delete dead sets |
| src/DigitalBrain.Core/SynapsePayloadJson.cs | core | human | reviewed | 1-43 | JSON->primitive object? converter | TestKit ProbeNeuron, tests | — | verify prod ingestion sites |
| src/DigitalBrain.Core/Synapses/CapabilitySynapses.cs | core | human | reviewed | 1-11 | CapabilityInvocation synapse (dead) | none | PROD-001 | delete |
| src/DigitalBrain.Core/Synapses/DbSynapses.cs | core | human | reviewed | 1-72 | Db schema model + inspected synapse | sqlite uploads, UI graph mapper | — | none |
| src/DigitalBrain.Core/TabularDataSynapses.cs | core | human | reviewed | 1-13 | Parsed tabular file ingestion synapse | Kernel TabularDataParser | REL-002 | none |
| src/DigitalBrain.Core/TaskId.cs | core | human | reviewed | 1-10 | Task identity wrapper | IKernelTask protocol | — | none |
| src/DigitalBrain.Core/Telemetry.cs | core | human | reviewed | 1-52 | Bounded telemetry buffer (registered, never consumed) | DI registrations only | PROD-002 | wire to OTel or delete |
| src/DigitalBrain.Core/UiActionContracts.cs | core | human | reviewed | 1-22 | Action submission + rejection reasons | action pipeline | — | none |
| src/DigitalBrain.Core/UiProtocol.cs | core | human | reviewed | 1-11 | Protocol/schema versions + token lifetimes | surface stack | — | none |

## Subsystem: kernel-runtime

| path | subsystem | authored | status | line ranges reviewed | primary responsibility | key callers/deps | finding IDs | follow-up |
|---|---|---|---|---|---|---|---|---|
| src/DigitalBrain.Kernel.Abstractions/Neuron.cs | kernel-runtime | human | reviewed | 1-397 | Journaled durable-actor base (fire/journal/checkpoint) | Orleans Journaling, all Grains/ neurons | REL-100, REL-101, REL-102, PERF-100, PERF-101, FRAME-100 | Core; bound journals + out-of-band checkpoints |
| src/DigitalBrain.Kernel/SelfEvolution/SelfEvolutionNeuron.cs | kernel-runtime | human | reviewed | 1-183 | Governed self-evolution rail (decisions→apply) | SelfEvolutionApplyRegistry, Neuron | SEC-100, SEC-101, REL-103, TEST-100 | Add approver auth + retriable apply |
| src/DigitalBrain.Kernel/SelfEvolution/SelfEvolutionApplyHandler.cs | kernel-runtime | human | reviewed | 1-73 | Allowlisted fail-closed apply registry | SelfEvolutionNeuron, apply handlers | (positive) | Add compensation/rollback contract |
| src/DigitalBrain.Kernel/AutomationDefinitionApplyHandler.cs | kernel-runtime | human | reviewed | 1-93 | Apply handlers: define/remove automation | IAutomationNeuron, registry | REL-104 | Make define atomic/idempotent |
| src/DigitalBrain.Kernel/Grains/AutomationNeuron.cs | kernel-runtime | human | reviewed | 1-366 | Reactive automation host (scripts+reactions) | Foundry.ScriptRunner, timeline | SEC-102, PERF-100 | Gate DefineReactionAsync; tighten match |
| src/DigitalBrain.Kernel/Grains/GeneratedNeuron.cs | kernel-runtime | human | reviewed | 1-368 | Dynamic embodied-pack host + Gmail demo | GeneratedPackRuntime, IChatClient | PROD-100, CLEAN-100 | Delete dead installed-pack path |
| src/DigitalBrain.Kernel/Grains/SystemNeurons.cs | kernel-runtime | human | reviewed | 1-178 | Aspire/observability/optimizer neurons | Neuron, UiSurface, IChatClient | PROD-101, CLEAN-101, CLEAN-102 | Quarantine rolling-update simulation |
| src/DigitalBrain.Kernel/Grains/SystemRollingSurfaces.cs | kernel-runtime | human | reviewed | 1-83 | UiSurface builders for rolling demo | UiSurface | PROD-101 | Delete with PerformKernelSelfUpdate |
| src/DigitalBrain.Kernel/Grains/LlmNeuron.cs | kernel-runtime | human | reviewed | 1-25 | LLM prompt→response neuron | IChatClient | (none) | none |
| src/DigitalBrain.Kernel/Grains/LlmResponderNeuron.cs | kernel-runtime | human | reviewed | 1-93 | Scoped-client LLM responder | IScopedChatClientFactory, IPackConfigStore | (none) | Swallowed config catch is intentional |
| src/DigitalBrain.Kernel/Grains/PollTriggerNeuron.cs | kernel-runtime | human | reviewed | 1-113 | Reminder-driven poll trigger | ICapabilityBroker, reminders | REL-105 | Persist dedup cursor |
| src/DigitalBrain.Kernel/Grains/ScheduleTriggerNeuron.cs | kernel-runtime | human | reviewed | 1-99 | Reminder-driven schedule trigger | reminders, AutomationNeuron | REL-106 | Implement cron or rename |
| src/DigitalBrain.Kernel/Kernel/CheckpointKeyProviders.cs | kernel-runtime | human | reviewed | 1-13 | Config-sourced AES checkpoint key | IConfiguration | (none) | none |
| src/DigitalBrain.Kernel/Kernel/CheckpointProtector.cs | kernel-runtime | human | reviewed | 1-22 | Encrypt/decrypt checkpoint at rest | Orleans Serializer, INeuronStateProtector | (none) | Base class should use this (REL-101) |
| src/DigitalBrain.Kernel/Kernel/JournalJson.cs | kernel-runtime | human | reviewed | 1-88 | STJ Synapse polymorphism (fail-closed) | Orleans.Journaling.Json | CLEAN-103 | Shadowed by encrypted converter |
| src/DigitalBrain.Kernel/Kernel/KernelServices.cs | kernel-runtime | human | reviewed | 1-41 | Checkpoint encryption wiring (prod fail-fast) | ICheckpointKeyProvider | (none) | none |
| src/DigitalBrain.Kernel/Kernel/KernelTaskSynapses.cs | kernel-runtime | human | reviewed | 1-13 | IKernelTask grain contract | (none — no impl) | CLEAN-104 | Dead contract; delete |
| src/DigitalBrain.Kernel/Program.cs | kernel-runtime | human | reviewed | 1-35 | Web host bootstrap + forwarded headers | ServiceDefaults, Orleans hosting | (none) | none |
| src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj | kernel-runtime | human | reviewed | 1-80 | Kernel project (net11, Orleans preview) | Directory.Packages.props | (none) | Large dep surface in trusted core (note) |
| src/DigitalBrain.Kernel/Dockerfile | kernel-runtime | human | reviewed | 1-27 | Container image (net11 preview) | .NET SDK/aspnet images | (none) | COPY . . before restore (note) |
| src/DigitalBrain.Kernel/appsettings.json | kernel-runtime | human | reviewed | 1-42 | Logging/CORS/Aspire config | — | (none) | none |
| src/DigitalBrain.Kernel/appsettings.Development.json | kernel-runtime | human | reviewed | 1-34 | Dev logging/Aspire config | — | (none) | none |
| src/DigitalBrain.Kernel/Runtime/EncryptedPersistentState.cs | kernel-runtime | human | reviewed | 1-478 | AES-GCM encrypted persistent state engine | IPersistentState, PersistedStateReconciliation | (positive) | Reference-quality crypto |
| src/DigitalBrain.Kernel/Runtime/PersistedStateReconciliation.cs | kernel-runtime | human | reviewed | 1-55 | Write-with-recovery outcome-unknown handling | IPersistentState | (positive) | Exemplary partial-write handling |
| src/DigitalBrain.Kernel/Runtime/EncryptedSynapseJsonConverter.cs | kernel-runtime | human | reviewed | 1-143 | Per-synapse AES journal converter (allowlist) | EncryptedRuntimeStateProtector | CLEAN-103 | Shadows JournalJson polymorphism |
| src/DigitalBrain.Kernel/Runtime/ConversationNeuron.cs | kernel-runtime | human | reviewed | 1-388 | Durable conversation aggregate + outbox | EncryptedPersistentState, worker/dispatcher grains | (none) | Well-constructed |
| src/DigitalBrain.Kernel/Runtime/ConversationArchiveNeuron.cs | kernel-runtime | human | reviewed | 1-55 | Immutable conversation archive segment | EncryptedPersistentState | (none) | none |
| src/DigitalBrain.Kernel/Runtime/ConversationModelGrain.cs | kernel-runtime | human | reviewed | 1-149 | Intent/mutation extraction via chat model | IChatClient | (none) | Prompt-injection guidance is the boundary |
| src/DigitalBrain.Kernel/Runtime/InoEffectPlanAuthority.cs | kernel-runtime | human | reviewed | 1-206 | HMAC plan tokens + execution proofs | IRuntimeStateKeyRing | (positive) | Strong |
| src/DigitalBrain.Kernel/Runtime/InoEffectPlanNeuron.cs | kernel-runtime | human | reviewed | 1-276 | Single-shot external-mutation executor (allowlist) | EncryptedPersistentState, Gmail/SF tool grains | (positive) | Correct mutation rail |
| src/DigitalBrain.Kernel/Runtime/InoEffectPlanStore.cs | kernel-runtime | human | reviewed | 1-49 | Mints/persists signed effect plans | InoEffectPlanNeuron, authority | (none) | none |
| src/DigitalBrain.Kernel/Runtime/InoConversationOutboxDispatcherGrain.cs | kernel-runtime | human | reviewed | 1-273 | Durable outbox→surface-feed projection | ConversationNeuron, SurfaceFeedNeuron | (positive) | Real outbox (contrast Neuron) |
| src/DigitalBrain.Kernel/Runtime/InoOperationWorkerGrain.cs | kernel-runtime | human | reviewed | 1-1233 | INO operation state machine (lease/retry/effect) | ConversationNeuron, workflow runner, tool gateway | (none) | `RequiredOperation` unused (note) |
| src/DigitalBrain.Kernel/Runtime/ClosedInoToolGateway.cs | kernel-runtime | human | reviewed | 1-23 | Fail-closed default tool gateway (deny all) | IInoToolGateway | (positive) | Default when Tools disabled |
| src/DigitalBrain.Kernel/Runtime/PlanInoToolGateway.cs | kernel-runtime | human | reviewed | 1-57 | Signed-plan mutation gateway (allowlist) | InoEffectPlanAuthority, InoEffectPlanNeuron | (positive) | none |
| src/DigitalBrain.Kernel/Runtime/AgentFrameworkWorkflowRunner.cs | kernel-runtime | human | reviewed | 1-778 | Agent Framework adapter + typed read/mutation routing | Microsoft.Agents.AI, model/tool grains, plan store | (none) | Heavy output sanitization; solid |
| src/DigitalBrain.Kernel/Runtime/SessionNeuron.cs | kernel-runtime | human | reviewed | 1-72 | Encrypted session aggregate | EncryptedPersistentState, SessionTransitions | (none) | none |
| src/DigitalBrain.Kernel/Runtime/SurfaceFeedNeuron.cs | kernel-runtime | human | reviewed | 1-108 | Encrypted surface-feed aggregate | EncryptedPersistentState, SurfaceFeedTransitions | (none) | none |

## Subsystem: kernel-hosting


| path | subsystem | authored | status | line ranges reviewed | primary responsibility | key callers/deps | finding IDs | follow-up |
|---|---|---|---|---|---|---|---|---|
| src/DigitalBrain.Kernel/Auth/DevAuth.cs | kernel-hosting | human | reviewed | 1-23 | Seeded dev admin/admin credential gate | UserSessionNeuron | SEC-203, ARCH-202 | delete with legacy auth stack |
| src/DigitalBrain.Kernel/Auth/UserSessionNeuron.cs | kernel-hosting | human | reviewed | 1-364 | Legacy journal-sourced login/session grain | tests only; DevAuth, NeuronJournals | ARCH-202, SEC-201, SEC-202, REL-200, CLEAN-202 | delete; no production caller |
| src/DigitalBrain.Kernel/Config/AzureBlobPackConfigBackingStore.cs | kernel-hosting | human | reviewed | 1-159 | Opaque-named blob store for encrypted pack config | PackConfigServices, RuntimeStateKeyRing | REL-201 (write path) | none |
| src/DigitalBrain.Kernel/Config/DataProtectionOAuthStateProtector.cs | kernel-hosting | human | reviewed | 1-62 | Time-limited OAuth state protect/unprotect | DigitalBrainAppEndpoints, connectors | SEC-205 | none |
| src/DigitalBrain.Kernel/Config/IPackConfigBackingStore.cs | kernel-hosting | human | reviewed | 1-9 | Byte-mover contract for pack-config blobs | PackConfigStore | — | none |
| src/DigitalBrain.Kernel/Config/InMemoryPackConfigBackingStore.cs | kernel-hosting | human | reviewed | 1-24 | Non-durable pack-config store for tests/local | PackConfigServices, tests | — | none |
| src/DigitalBrain.Kernel/Config/PackConfigServices.cs | kernel-hosting | human | reviewed | 1-49 | DataProtection + pack-config DI registration | AddDigitalBrainClients | SEC-200, FRAME-200 | add ProtectKeysWith* |
| src/DigitalBrain.Kernel/Config/PackConfigStore.cs | kernel-hosting | human | reviewed | 1-64 | Per-value DataProtection pack-config store | connectors, seeders | REL-201 | harden read-modify-write |
| src/DigitalBrain.Kernel/Db/SqliteSchemaInspector.cs | kernel-hosting | human | reviewed | 1-382 | Read-only SQLite schema reflection | DI-registered, tests only | CLEAN-200, TEST-200 | dead: no prod consumer |
| src/DigitalBrain.Kernel/Gateway/IngressNeuron.cs | kernel-hosting | human | reviewed | 1-11 | Arbitrary Signal broadcast grain (legacy gateway) | none (dead) | CLEAN-200 | delete |
| src/DigitalBrain.Kernel/Generated/GeneratedPackRuntime.cs | kernel-hosting | human (NOT generated despite folder) | reviewed | 1-53 | Embodied-pack lifecycle for GeneratedNeuron | GeneratedNeuron, IPackEmbodiment | CLEAN-203, CLEAN-202 | move out of Generated/ |
| src/DigitalBrain.Kernel/Hosting/DigitalBrainAppEndpoints.cs | kernel-hosting | human | reviewed | 1-133 | OAuth start/callback HTTP endpoints | Program.cs; OAuthStateProtector, connector grains | ARCH-201 | genericize provider dispatch |
| src/DigitalBrain.Kernel/Hosting/DigitalBrainHostEnvironment.cs | kernel-hosting | human | reviewed | 1-19 | Aspire-hosted mode detection | UseDigitalBrainOrleans, AddDigitalBrainClients | CLEAN-204 | config-only check |
| src/DigitalBrain.Kernel/Hosting/DigitalBrainOrleansExtensions.cs | kernel-hosting | human | reviewed | 1-361 | Kernel composition root (silo, DI, pipeline, Kestrel) | Program.cs; AppHost-injected config | ARCH-200, ARCH-201, FRAME-200, PROD-200, REL-202, CLEAN-202 | split; delete gateway remnants |
| src/DigitalBrain.Kernel/Hosting/OAuthTransportBoundary.cs | kernel-hosting | human | reviewed | 1-89 | Transport clamps for /oauth (method/TLS/rate/timeout) | Program.cs middleware | REL-203, SEC-204 | per-client rate partition |
| src/DigitalBrain.Kernel/Hosting/PrototypeJournals.cs | kernel-hosting | human | reviewed | 1-34 | In-memory journal + no-op state manager (prototype mode) | UseDigitalBrainOrleans (non-durable branch) | CLEAN-201, FRAME-201 | fix namespace |
| src/DigitalBrain.Kernel/Hosting/RuntimeStateHosting.cs | kernel-hosting | human | reviewed | 1-157 | Runtime-state KEK/signing key ring loading (fail-closed) | UseDigitalBrainOrleans, EncryptedRuntimeStateProtector | REL-202 | real health probe |
| src/DigitalBrain.Kernel/Properties/launchSettings.json | kernel-hosting | human | reviewed | 1-12 | Dev launch profile (DOTNET_ENVIRONMENT) | dotnet run | — | none |
| src/DigitalBrain.Kernel/Protos/digitalbrain.proto | kernel-hosting | human (generated C# in obj/ via Grpc.Tools, not checked in) | reviewed | 1-93 | Dead DigitalBrainGateway gRPC contract | csproj Protobuf item; no server maps it | ARCH-200, CLEAN-200 | delete + stale Dart stubs |
| src/DigitalBrain.Kernel/Sync/SyncManifest.cs | kernel-hosting | human | reviewed | 1-5 | Orphaned sync manifest records | none (zero refs) | CLEAN-200 | delete |
| src/DigitalBrain.Kernel/TabularData/TabularDataParser.cs | kernel-hosting | human | reviewed | 1-82 | xlsx -> headers/rows/stats (ClosedXML) | tests only | REL-204, PERF-200, CLEAN-200 | dead path; fix or delete |
| src/DigitalBrain.Kernel/Ui/ChatNeuron.cs | kernel-hosting | human | reviewed | 1-31 | Legacy RfwCard chat grain | tests only | CLEAN-200 | delete |
| src/DigitalBrain.Kernel/Ui/SignalEgressBus.cs | kernel-hosting | human | reviewed | 1-62 | Bounded fan-out for WatchSynapses (never mapped) | TestKit only | CLEAN-200 | delete |
| src/DigitalBrain.Kernel/Ui/SignalEgressStreamSubscriber.cs | kernel-hosting | human | reviewed | 1-60 | Silo lifecycle pump timeline->egress bus | registration extension never called | CLEAN-200 | delete |
| src/DigitalBrain.Kernel/Uploads/ChatUploadClassifier.cs | kernel-hosting | human | reviewed | 1-23 | Upload kind by file extension | tests only | CLEAN-200, TEST-200 | delete with upload path |

## Subsystem: foundry

| path | subsystem | authored | status | line ranges reviewed | primary responsibility | key callers/deps | finding IDs | follow-up |
|---|---|---|---|---|---|---|---|---|
| src/DigitalBrain.Kernel/Foundry/AzureResourceController.cs | foundry | human | reviewed | 1-25 | Cloud kernel-restart controller (TODO no-op) | CodeDeployNeuron, FoundryServices | PROD-301 | Deploy tier non-functional in cloud |
| src/DigitalBrain.Kernel/Foundry/CapabilityBroker.cs | foundry | human | reviewed | 1-48 | Capability facade for scripts (mostly stubs) | AutomationNeuron, PollTriggerNeuron | SEC-307, PROD-300 | SSRF + placeholder methods |
| src/DigitalBrain.Kernel/Foundry/CapabilityGate.cs | foundry | human | reviewed | 1-110 | Static-analysis API ban screen (guardrail) | InProcessAlcExecutor, ScriptRunner, sandbox, PackAlcEmbodier | SEC-300, SEC-305, CLEAN-300, TEST-300 | reflection-bypassable, not a boundary |
| src/DigitalBrain.Kernel/Foundry/CodeDeployNeuron.cs | foundry | human | reviewed | 1-48 | Deploy tier: build-verify, commit source, restart | CodeFoundryClosedLoopNeuron, FoundryApplyHandlers | SEC-308, SEC-304, PROD-301 | NO gate on deploy path |
| src/DigitalBrain.Kernel/Foundry/CodeFoundryClosedLoopNeuron.cs | foundry | human | reviewed | 1-134 | Orchestrates gen→stage/apply loop | ISelfEvolutionNeuron, CodeGen/Run/Deploy neurons | SEC-303, REL-302, TEST-301 | TrustedAutoApply rail bypass |
| src/DigitalBrain.Kernel/Foundry/CodeGenNeuron.cs | foundry | human | reviewed | 1-58 | LLM → C# source generation | IChatClient, CodeFoundryClosedLoopNeuron | SEC-308 | prompt-injection into codegen |
| src/DigitalBrain.Kernel/Foundry/CodeRunNeuron.cs | foundry | human | reviewed | 1-15 | Grain that invokes ICodeExecutor | ICodeExecutor (InProcessAlcExecutor) | SEC-304, SEC-302 | directly fireable, bypasses rail |
| src/DigitalBrain.Kernel/Foundry/FoundryApplyHandlers.cs | foundry | human | reviewed | 1-98 | Rail apply handlers (Run/Deploy) | ISelfEvolutionApplyHandler, executor grains | CLEAN-301, REL-302 | dedupe FindStagedAsync |
| src/DigitalBrain.Kernel/Foundry/FoundryCompilation.cs | foundry | human | reviewed | 1-82 | Roslyn compilation + reference sets | InProcessAlcExecutor, sandbox, ScriptRunner | PERF-300, FRAME-300 | prelude adds banned namespaces; per-call refs |
| src/DigitalBrain.Kernel/Foundry/FoundryServices.cs | foundry | human | reviewed | 1-27 | DI registration for foundry services | AddFoundry (silo builder) | SEC-302 | sandbox registered, never resolved |
| src/DigitalBrain.Kernel/Foundry/IBuildRunner.cs | foundry | human | reviewed | 1-60 | ProcessBuildRunner: shell dotnet build verify | CodeDeployNeuron | SEC-308, REL-303 | compile!=safe; fragile paths |
| src/DigitalBrain.Kernel/Foundry/ICodeExecutor.cs | foundry | human | reviewed | 1-8 | Executor interface (sync, no token) | CodeRunNeuron, InProcessAlcExecutor | REL-300 | no cancellation/timeout in contract |
| src/DigitalBrain.Kernel/Foundry/IResourceController.cs | foundry | human | reviewed | 1-21 | Aspire kernel-restart controller (log-only) | CodeDeployNeuron, FoundryServices | none | restart is out-of-band via MCP |
| src/DigitalBrain.Kernel/Foundry/InProcessAlcExecutor.cs | foundry | human | reviewed | 1-83 | In-process ALC executor (the real Run path) | CodeRunNeuron; CapabilityGate, FoundryCompilation | SEC-302, REL-300, REL-301, TEST-300 | full-trust in-process, no limits |
| src/DigitalBrain.Kernel/Foundry/PackAlcEmbodier.cs | foundry | human | reviewed | 1-115 | Compile pack → gate → collectible ALC → IPackBehavior | GeneratedPackRuntime | SEC-300 | in-process guardrail, host asm unify |
| src/DigitalBrain.Kernel/Foundry/ScriptRunner.cs | foundry | human | reviewed | 1-113 | CSharpScript executor for automations | AutomationNeuron | SEC-301, CLEAN-300 | gate is a no-op (zero refs) |
| src/DigitalBrain.Kernel/Llm/DigitalBrainChat.cs | foundry | human | reviewed | 1-99 | Provider fan-out IChatClient registration | AddDigitalBrainChat (DI) | none | reasonable |
| src/DigitalBrain.Kernel/Llm/DigitalBrainChatClientRegistration.cs | foundry | human | reviewed | 1-118 | Keyed IChatClient per registered model | model registry snapshot, DI | FRAME-301 | experimental MEAI suppressed |
| src/DigitalBrain.Kernel/Llm/DigitalBrainChatPolicy.cs | foundry | human | reviewed | 1-75 | Bounded concurrency + timeout delegating client | DigitalBrainChatTelemetry.Wrap | none | solid |
| src/DigitalBrain.Kernel/Llm/DigitalBrainEmbeddingRuntimeOptions.cs | foundry | human | reviewed | 1-22 | Embedding config binding | DigitalBrainChat | none | fine |
| src/DigitalBrain.Kernel/Llm/DigitalBrainLlmRuntimeOptions.cs | foundry | human | reviewed | 1-83 | LLM runtime config binding | DigitalBrainChat, factories | CLEAN-303 | strip vacuous summaries |
| src/DigitalBrain.Kernel/Llm/NoOpEmbeddingGenerator.cs | foundry | human | reviewed | 1-22 | Zero-vector fail-soft embedding generator | DigitalBrainChat, HybridScorer | none | fine |
| src/DigitalBrain.Kernel/Llm/ScopedChatClientFactory.cs | foundry | human | reviewed | 1-91 | Per-scope client construction + shared builders | IScopedChatClientFactory consumers | none | key never logged |
| src/DigitalBrain.Kernel/Sandbox/ISandboxedExecutor.cs | foundry | human | reviewed | 1-22 | Sandbox tiers + executor interface | OutOfProcessSandbox, FoundryServices | CLEAN-302 | Wasm tier aspirational |
| src/DigitalBrain.Kernel/Sandbox/OutOfProcessSandbox.cs | foundry | human | reviewed | 1-88 | Child-process code executor (unused) | FoundryServices (registered only); ProcessRunner | SEC-302, SEC-306 | never invoked; no resource caps |
| src/DigitalBrain.Kernel/Sandbox/ProcessRunner.cs | foundry | human | reviewed | 1-135 | Shared process-exec core (timeout/kill/denylist) | OutOfProcessSandbox, SDK neurons | none | denylist is not security |

## Subsystem: connectors-and-contracts


| path | subsystem | authored | status | line ranges reviewed | primary responsibility | key callers/deps | finding IDs | follow-up |
|---|---|---|---|---|---|---|---|---|
| integrations/DigitalBrain.Google/AssemblyInfo.cs | connectors | human | reviewed | 1-3 | InternalsVisibleTo for tests | DigitalBrain.Tests | — | none |
| integrations/DigitalBrain.Google/DigitalBrain.Google.csproj | connectors | human | reviewed | 1-19 | Google integration project definition | Core, Kernel.Abstractions, Google.Apis | — | none |
| integrations/DigitalBrain.Google/GmailApiClientFactory.cs | connectors | human | reviewed | 1-28 | Build Gmail client from scoped encrypted config | GmailNeuron, IPackConfigStore | PERF-400 | none |
| integrations/DigitalBrain.Google/GmailNeuron.cs | connectors | human | reviewed | 1-569 | Gmail read/metadata/send tool grain, auth gating | INO tools, GoogleConnector, IOAuthStateProtector | ARCH-402, REL-401 | none |
| integrations/DigitalBrain.Google/GoogleAppConfigSeeder.cs | connectors | human | reviewed | 1-74 | Seed app-scope Google OAuth config at startup | IConfiguration, IPackConfigStore | PROD-402 (context) | none |
| integrations/DigitalBrain.Google/GoogleClientFactory.cs | connectors | human | reviewed | 1-422 | Google OAuth keys, URL builder, exchange, flow state machine | GoogleConnector, GmailNeuron | SEC-400, SEC-402, ARCH-403, ARCH-404, PERF-403, CLEAN-402 | none |
| integrations/DigitalBrain.Google/GoogleConnector.cs | connectors | human | reviewed | 1-469 | IConnector impl: Google auth lifecycle + health probe | GmailNeuron (keyed DI), IPackConfigStore | SEC-401, PROD-402, PROD-404, CLEAN-400 | none |
| integrations/DigitalBrain.Google/GoogleCredentialFactory.cs | connectors | human | reviewed | 1-19 | UserCredential from refresh token | GmailApiClientFactory, GoogleConnector | PERF-400, FRAME-400 | verify vs Google.Apis.Auth 1.75.0 docs |
| integrations/DigitalBrain.Google/GoogleGmailApiClient.cs | connectors | human | reviewed | 1-610 | Gmail wire client: metadata reads, idempotent-ish send | GmailNeuron via factory | PROD-400, PERF-401, REL-400 | none |
| integrations/DigitalBrain.Google/IGmailApiClient.cs | connectors | human | reviewed | 1-121 | Provider-side Gmail read model + client interface | GoogleGmailApiClient, GmailNeuron | ARCH-402 | none |
| integrations/DigitalBrain.Google/IGmailApiClientFactory.cs | connectors | human | reviewed | 1-9 | Gmail client factory interface | GmailNeuron | — | none |
| integrations/DigitalBrain.Salesforce/DigitalBrain.Salesforce.csproj | connectors | human | reviewed | 1-19 | Salesforce integration project definition | DeveloperForce.Force, Newtonsoft | FRAME-402 | none |
| integrations/DigitalBrain.Salesforce/ISalesforceApiClient.cs | connectors | human | reviewed | 1-41 | Salesforce client interface w/ DIM unsupported defaults | SalesforceReadNeuron, MutationNeuron | CLEAN-404 | none |
| integrations/DigitalBrain.Salesforce/ISalesforceApiClientFactory.cs | connectors | human | reviewed | 1-9 | Salesforce client factory interface | Read/Mutation neurons | — | none |
| integrations/DigitalBrain.Salesforce/SalesforceApiClient.cs | connectors | human | reviewed | 1-937 | SOQL/SOSL reads, describe resolution, preview/apply/verify mutations | Read/Mutation neurons via factory | PROD-401, REL-401, REL-402, PERF-402, SEC-403 (identity allowlist ref) | none |
| integrations/DigitalBrain.Salesforce/SalesforceApiClientFactory.cs | connectors | human | reviewed | 1-15 | Create session per scope | neurons, SalesforceClientFactory | PERF-400 | none |
| integrations/DigitalBrain.Salesforce/SalesforceAppConfigSeeder.cs | connectors | human | reviewed | 1-105 | Seed app-scope Salesforce Connected App config | IConfiguration, IPackConfigStore | — | dedup with Google seeder |
| integrations/DigitalBrain.Salesforce/SalesforceClientFactory.cs | connectors | human | reviewed | 1-777 | SF OAuth keys, PKCE, exchange, session creation, flow state machine | SalesforceConnector, neurons | ARCH-403, ARCH-404, SEC-402, SEC-403, PERF-403, CLEAN-401, CLEAN-402 | none |
| integrations/DigitalBrain.Salesforce/SalesforceConnector.cs | connectors | human | reviewed | 1-451 | IConnector impl: SF auth lifecycle (PKCE) + health probe | neurons (keyed DI), IPackConfigStore | PROD-403, SEC-401 | none |
| integrations/DigitalBrain.Salesforce/SalesforceMutationNeuron.cs | connectors | human | reviewed | 1-94 | Mutation tool grain: config/credential gate + delegate | INO tools, connector, factory | — | none |
| integrations/DigitalBrain.Salesforce/SalesforceReadContracts.cs | connectors | human | reviewed | 1-56 | Failure enum, safe exception, scope/continuation records | SalesforceApiClient, ReadNeuron | — | none |
| integrations/DigitalBrain.Salesforce/SalesforceReadNeuron.cs | connectors | human | reviewed | 1-722 | Read tool grain: persisted continuations, OAuth start tokens | INO tools, connector, factory | SEC-401, REL-401 | none |
| src/DigitalBrain.Aspire/AssemblyInfo.cs | contracts | human | reviewed | 1-3 | InternalsVisibleTo for tests | DigitalBrain.Tests | — | none |
| src/DigitalBrain.Aspire/DigitalBrain.Aspire.csproj | contracts | human | reviewed | 1-22 | Aspire hosting package definition | Aspire.Hosting.* | — | none |
| src/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs | contracts | human | reviewed | 1-361 | Storage/Orleans/LLM composition + kernel wiring | AppHost, DigitalBrainContext | CLEAN-402 (WithOptionalEnvironment) | none |
| src/DigitalBrain.Aspire/DigitalBrainContext.cs | contracts | human | reviewed | 1-40 | Wiring context record | builder extensions | — | none |
| src/DigitalBrain.Aspire/DigitalBrainOptions.cs | contracts | human | reviewed | 1-89 | Model registry + LLM selection options | AddDigitalBrain | — | none |
| src/DigitalBrain.Aspire/FlutterAspireExtensions.cs | contracts | human | reviewed | 1-192 | Flutter desktop/web client resources; bootstrap secret vs OIDC | AppHost | — | none |
| src/DigitalBrain.Aspire/GoogleAspireExtensions.cs | contracts | human | reviewed | 1-82 | Google secret parameters + operator guidance | AppHost, GoogleAppConfigSeeder | — | none |
| src/DigitalBrain.Aspire/SalesforceAspireExtensions.cs | contracts | human | reviewed | 1-104 | Salesforce secret parameters + operator guidance | AppHost, SalesforceAppConfigSeeder | — | none |
| src/DigitalBrain.Kernel.Abstractions/AuthRequiredAIFunction.cs | contracts | human | reviewed | 1-57 | Auth gate wrapper for AIFunction tools | INO tool composer | — | none |
| src/DigitalBrain.Kernel.Abstractions/ConversationArchive.cs | contracts | human | reviewed | 1-240 | Hash-chained conversation archive segments | ConversationNeuron impls | — | none |
| src/DigitalBrain.Kernel.Abstractions/ConversationModel.cs | contracts | human | reviewed | 1-66 | LLM intent/mutation proposal contracts | INO, model grain | ARCH-401 | none |
| src/DigitalBrain.Kernel.Abstractions/ConversationNeuron.cs | contracts | human | reviewed | 1-1419 | Conversation state machine: idempotency, leases, approvals, effects | kernel conversation grain, INO worker | ARCH-401 | none |
| src/DigitalBrain.Kernel.Abstractions/DigitalBrain.Kernel.Abstractions.csproj | contracts | human | reviewed | 1-21 | Kernel abstractions project | Orleans, M.E.AI | ARCH-405 | none |
| src/DigitalBrain.Kernel.Abstractions/EncryptedRuntimeStateContracts.cs | contracts | human | reviewed | 1-181 | Envelope encryption contracts, scope-hash keys | runtime storage providers | — | none |
| src/DigitalBrain.Kernel.Abstractions/GmailTool.cs | contracts | human | reviewed | 1-249 | Gmail tool grain contracts + bounds in kernel | GmailNeuron, INO | ARCH-400, ARCH-401, ARCH-402 | none |
| src/DigitalBrain.Kernel.Abstractions/IConnector.cs | contracts | human | reviewed | 1-55 | Connector auth-lifecycle contract + descriptor records | Google/Salesforce connectors, neurons | ARCH-400 | none |
| src/DigitalBrain.Kernel.Abstractions/IScopedChatClientFactory.cs | contracts | human | reviewed | 1-13 | Per-pack scoped chat client factory | Ino/packs | — | none |
| src/DigitalBrain.Kernel.Abstractions/InoEffectPlan.cs | contracts | human | reviewed | 1-157 | Immutable effect plan + mutation grants | INO effect rail | ARCH-401 | fail-closed grant default |
| src/DigitalBrain.Kernel.Abstractions/LlmAttribute.cs | contracts | human | reviewed | 1-55 | Orleans facet for keyed IChatClient injection | grains, DI | — | none |
| src/DigitalBrain.Kernel.Abstractions/Neuron.cs | contracts | human | reviewed | 1-397 | Durable grain base: journals, timeline, checkpoints | all neurons | ARCH-405 | none |
| src/DigitalBrain.Kernel.Abstractions/NeuronStateProtectors.cs | contracts | human | reviewed | 1-64 | AES-GCM + passthrough state protectors | kernel state protection | — | consider AAD binding |
| src/DigitalBrain.Kernel.Abstractions/SalesforceTool.cs | contracts | human | reviewed | 1-236 | Salesforce tool grain contracts in kernel | Salesforce neurons, INO | ARCH-400, ARCH-401 | none |
| src/DigitalBrain.Kernel.Abstractions/SemanticIntent.cs | contracts | human | reviewed | 1-149 | Semantic intent enums/records | INO intent resolution | ARCH-401, CLEAN-402 (Set op) | none |
| src/DigitalBrain.Kernel.Abstractions/SessionNeuron.cs | contracts | human | reviewed | 1-258 | Session state machine: rotation, replay ledger, revocation | kernel auth transport | — | none |
| src/DigitalBrain.Kernel.Abstractions/SurfaceFeedNeuron.cs | contracts | human | reviewed | 1-749 | Surface feed state machine + one-shot action bindings | kernel surface grain, UI transport | — | none |
| src/DigitalBrain.Kernel.Abstractions/SynapseDispatch.cs | contracts | human | reviewed | 1-47 | Frozen IHandle<> reflection dispatch cache | Neuron base | — | none |
| src/DigitalBrain.Kernel.Abstractions/SynapseStream.cs | contracts | human | reviewed | 1-13 | Global timeline stream id helper | Neuron base | — | tenancy filtering is subscriber-side |
| src/DigitalBrain.Pack.Contracts/AssemblyInfo.cs | contracts | human | reviewed | 1-3 | InternalsVisibleTo for tests | tests | — | none |
| src/DigitalBrain.Pack.Contracts/Configuration.cs | contracts | human | reviewed | 1-95 | ConfigurationProvided synapse + config-form UI mapping | host UI layer, packs | — | none |
| src/DigitalBrain.Pack.Contracts/DigitalBrain.Pack.Contracts.csproj | contracts | human | reviewed | 1-22 | Packable pack-protocol assembly | Core, Ui.Contracts | — | none |
| src/DigitalBrain.Pack.Contracts/Distribution/BundleManifest.cs | contracts | human | reviewed | 1-33 | Bundle tier/channel/dependency metadata | catalog, packs | — | none |
| src/DigitalBrain.Pack.Contracts/Distribution/IPackBehavior.cs | contracts | human | reviewed | 1-47 | Behavior-pack contract + PackManifest | Foundry GeneratedPackRuntime/PackAlcEmbodier | ARCH-400 (capability depth) | none |
| src/DigitalBrain.Pack.Contracts/Distribution/NeuroPack.cs | contracts | human | reviewed | 1-21 | Pack record w/ signature fields | Foundry, marketplace | SEC-405 | none |
| src/DigitalBrain.Pack.Contracts/Trust/PackSignatureVerifier.cs | contracts | human | reviewed | 1-75 | ECDSA pack signing/verification | PublisherTrust (only) | SEC-405 | wire into embodiment |
| src/DigitalBrain.Pack.Contracts/Trust/PublisherTrust.cs | contracts | human | reviewed | 1-21 | Integrity + publisher allowlist decision | none (unenforced) | SEC-405 | wire into embodiment |
| src/DigitalBrain.Pack.Contracts/UiKit/KitExperience.cs | contracts | human | reviewed | 1-93 | Experience state machine base for UI packs | packs, GeneratedNeuron | — | none |
| src/DigitalBrain.Pack.Contracts/UiKit/UiExperience.cs | contracts | human | reviewed | 1-320 | Fluent hop/widget builder | KitExperience subclasses | — | none |
| src/DigitalBrain.Ui.Contracts/DigitalBrain.Ui.Contracts.csproj | contracts | human | reviewed | 1-21 | Packable UI-protocol assembly | Core | — | none |
| src/DigitalBrain.Ui.Contracts/Ui/RfwCard.cs | contracts | human | reviewed | 1-21 | Legacy RFW payload + IChatNeuron | Flutter feed (legacy) | CLEAN-405 | merge onto UiSurface.ForRfw |
| src/DigitalBrain.Ui.Contracts/UiNeuronContracts.cs | contracts | human | reviewed | 1-28 | Session/observability neuron contracts, chart synapses | kernel, UI | — | none |
| src/DigitalBrain.Ui.Contracts/UiSurfaces.cs | contracts | human | reviewed | 1-608 | UiSurface/widget-tree SDUI vocabulary + specs | all UI emitters, Flutter | CLEAN-405 | converge vocabularies |
| src/DigitalBrain.Ui.Runtime/DigitalBrain.Ui.Runtime.csproj | contracts | human | reviewed | 1-17 | Packable UI runtime/sample assembly | Core, Ui.Contracts | CLEAN-403 | none |
| src/DigitalBrain.Ui.Runtime/UiSurfaceRuntime.cs | contracts | human | reviewed | 1-850 | Sample + live surface projection builders | UserSessionNeuron, SystemNeurons | SEC-404, CLEAN-403 | remove defaultPassword |

## Subsystem: mcp-hosts-build


| path | subsystem | authored | status | line ranges reviewed | primary responsibility | key callers/deps | finding IDs | follow-up |
|---|---|---|---|---|---|---|---|---|
| src/DigitalBrain.Mcp/Program.cs | mcp-hosts-build | human | reviewed | 1-123 | MCP host composition, auth middleware, Orleans client | AppHost, ServiceDefaults, McpTools | PROD-500, CLEAN-502, CLEAN-503, TEST-500 | none |
| src/DigitalBrain.Mcp/McpTools.cs | mcp-hosts-build | human | reviewed | 1-65 | ino_interact tool + per-call authority | Program.cs, McpConversationPipeline | ARCH-501, PERF-502, TEST-500 | add read tool |
| src/DigitalBrain.Mcp/McpConversationPipeline.cs | mcp-hosts-build | human | reviewed | 1-51 | Durable INO command acceptance handler | McpTools, UiGrpcService | none | none |
| src/DigitalBrain.Mcp/RuntimeRequestAuthenticator.cs | mcp-hosts-build | human | reviewed | 1-36 | MCP bearer auth (session or OIDC) | Program.cs middleware, McpAuthority | ARCH-502 | resolve dead session branch |
| src/DigitalBrain.Mcp/RuntimeSessionAuthority.cs | mcp-hosts-build | human | reviewed | 1-252 | Session create/rotate/revoke/validate over ISessionNeuron | UiGrpcService, RuntimeRequestAuthenticator | none | none |
| src/DigitalBrain.Mcp/RuntimeTransportBoundary.cs | mcp-hosts-build | human | reviewed | 1-124 | Edge HTTPS/body/rate/concurrency/timeout middleware | Program.cs | PERF-501, REL-502, REL-504 | split stream/unary budgets |
| src/DigitalBrain.Mcp/ConversationStateClient.cs | mcp-hosts-build | human | reviewed | 1-413 | Conversation grain adapter (begin, approvals, snapshot) | McpConversationPipeline, UiGrpcService | ARCH-503 | move to transport lib |
| src/DigitalBrain.Mcp/RuntimeSurfaceFeed.cs | mcp-hosts-build | human | reviewed | 1-758 | Surface-feed adapter + UI action authorization | UiGrpcService | PERF-500, ARCH-503 | split authorization vs paging |
| src/DigitalBrain.Mcp/UiGrpcService.cs | mcp-hosts-build | human | reviewed | 1-561 | gRPC UI transport (sessions, feed, actions) | Flutter client, RuntimeSurfaceFeed | ARCH-504, PERF-500 | make grant escalation explicit |
| src/DigitalBrain.Mcp/UiExternalIdentity.cs | mcp-hosts-build | human | reviewed | 1-214 | OIDC options + claim-to-context mapping | UiGrpcService, RuntimeRequestAuthenticator | SEC-501 | tenant binding |
| src/DigitalBrain.Mcp/UiHostingExtensions.cs | mcp-hosts-build | human | reviewed | 1-113 | gRPC/CORS/forwarded-headers/health wiring | Program.cs | SEC-502, REL-500 | real readiness check |
| src/DigitalBrain.Mcp/AuthorizationFlowStartProxy.cs | mcp-hosts-build | human | reviewed | 1-82 | Bounded OAuth-start reverse proxy to kernel | Program.cs route | SEC-503 | none |
| src/DigitalBrain.Mcp/BoundedOrleansClientConnectionRetryFilter.cs | mcp-hosts-build | human | reviewed | 1-26 | Bounded Orleans gateway connect retry | Program.cs | REL-503 | none |
| src/DigitalBrain.Mcp/InoTelemetry.cs | mcp-hosts-build | human | reviewed | 1-8 | ActivitySource holder | McpConversationPipeline | none | dedupe with UiGrpcService source |
| src/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj | mcp-hosts-build | human | reviewed | 1-51 | MCP project + container config | AppHost, Kernel (dead ref) | PROD-500, ARCH-500 | none |
| src/DigitalBrain.Mcp/Protos/ui.proto | mcp-hosts-build | human | reviewed | 1-87 | UI gRPC contract | UiGrpcService, Flutter stubs | none | none |
| src/DigitalBrain.Mcp/Properties/AssemblyInfo.cs | mcp-hosts-build | human | reviewed | 1-3 | InternalsVisibleTo tests | tests | none | none |
| src/DigitalBrain.Mcp/Properties/launchSettings.json | mcp-hosts-build | human | reviewed | 1-11 | Local launch profile (bypassed by AppHost) | dotnet run | none | none |
| hosts/DigitalBrain.AppHost/AppHost.cs | mcp-hosts-build | human | reviewed | 1-186 | Aspire composition root (kernel, MCP, Flutter, secrets) | DigitalBrain.Aspire, Projects | FRAME-503 | none |
| hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj | mcp-hosts-build | human | reviewed | 1-23 | AppHost project (Aspire SDK 13.4.6) | Aspire | FRAME-504 | drop dead condition |
| hosts/DigitalBrain.AppHost/Properties/launchSettings.json | mcp-hosts-build | human | reviewed | 1-29 | Dashboard/OTLP local ports | aspire run | none | none |
| hosts/DigitalBrain.AppHost/appsettings.json | mcp-hosts-build | human | reviewed | 1-11 | AppHost logging config | AppHost | CLEAN-504 | none |
| hosts/DigitalBrain.AppHost/appsettings.Development.json | mcp-hosts-build | human | reviewed | 1-11 | Duplicate of appsettings.json | AppHost | CLEAN-504 | delete or differentiate |
| hosts/DigitalBrain.ServiceDefaults/Extensions.cs | mcp-hosts-build | human | reviewed | 1-166 | OTEL/health/service-discovery defaults | Kernel, Mcp | REL-500 (adjunct) | none |
| hosts/DigitalBrain.ServiceDefaults/DigitalBrain.ServiceDefaults.csproj | mcp-hosts-build | human | reviewed | 1-23 | Shared defaults project | Kernel, Mcp | none | none |
| deploy/Program.cs | mcp-hosts-build | human | reviewed | 1-561 | Pulumi ACA/Storage/OpenAI/monitoring provisioning | deploy.yml | PROD-501, PROD-502, SEC-501 (grants) | MI-only OpenAI follow-up |
| deploy/DigitalBrain.Deploy.csproj | mcp-hosts-build | human | reviewed | 1-23 | Standalone deploy project | deploy.yml | CLEAN-505 | drop unused OTEL pkgs |
| deploy/Pulumi.yaml | mcp-hosts-build | human | reviewed | 1-5 | Pulumi project manifest | pulumi | none | none |
| deploy/Pulumi.dev.yaml | mcp-hosts-build | human | reviewed | 1-12 | Non-secret stack config | pulumi | PROD-502, CLEAN-505 | stale imageTag |
| deploy/.gitignore | mcp-hosts-build | human | reviewed | 1-2 | bin/obj ignore | git | none | none |
| .github/workflows/ci.yml | mcp-hosts-build | human | reviewed | 1-168 | PR/push gate: policy, tests, publish-graph, Flutter | GitHub Actions | PROD-500, SEC-500, SEC-504 | add dep scanning |
| .github/workflows/deploy.yml | mcp-hosts-build | human | reviewed | 1-382 | Release-gated build/publish/pulumi/domains/smoke | GitHub Actions, deploy/ | PROD-502, PROD-503, REL-500 | move domains into Pulumi |
| docs/adr/0001-durable-ino-operations.md | mcp-hosts-build | human | reviewed | 1-187 | Authority/invariant ADR for durable INO ops | engineers/agents | none | keep current |
| docs/architecture-assessment-and-plan.md | mcp-hosts-build | human | reviewed | 1-150 | Point-in-time repo assessment (partially stale) | execution-plan | CLEAN-500 | trim to retro |
| docs/execution-log.md | mcp-hosts-build | human | reviewed | 1-62 | Agent run log for shape-v3 | execution-plan | CLEAN-500 | archive |
| docs/execution-plan.md | mcp-hosts-build | human | reviewed | 1-203 | One-shot agent execution playbook (stale) | grok-prompt | CLEAN-500, ARCH-503 | delete after extracting open items |
| docs/grok-prompt.md | mcp-hosts-build | human | reviewed | 1-19 | Copy-paste prompt for Grok CLI | none | CLEAN-500 | delete |
| .editorconfig | mcp-hosts-build | human | reviewed | 1-250 | Style/analyzer configuration | dotnet format/build | CLEAN-504 | none |
| .gitattributes | mcp-hosts-build | human | reviewed | 1-14 | EOL normalization | git | none | none |
| .gitignore | mcp-hosts-build | human | reviewed | 1-460 | Ignore rules (VS template + repo) | git | REL-501 | untrack sentinel |
| .lsp.json | mcp-hosts-build | human | reviewed | 1-20 | LSP config (csharp-ls, dart) | agent tooling | REL-501 (dep) | none |
| .mcp.json | mcp-hosts-build | human | reviewed | 1-24 | Dev-agent MCP server wiring | Claude/agents | SEC-500 | pin npm versions |
| AGENTS.md | mcp-hosts-build | human | reviewed | 1-4 | Pointer to CLAUDE.md | agents | none | none |
| Brain.slnx | mcp-hosts-build | human | reviewed | 1-42 | Curated solution layout | dotnet build | CLEAN-504 | none |
| CLAUDE.md | mcp-hosts-build | human | reviewed | 1-116 | Canonical way-of-working doc | agents/humans | CLEAN-501 | fix dead MCP run hack |
| Directory.Build.props | mcp-hosts-build | human | reviewed | 1-18 | Build skip flags + code style enforcement | MSBuild | none | none |
| Directory.Build.targets | mcp-hosts-build | human | reviewed | 1-27 | Tool-restore + codegraph-init sentinel targets | MSBuild | SEC-500, REL-501 | pin + fix sentinel |
| Directory.Packages.props | mcp-hosts-build | human | reviewed | 1-124 | Central package pins | all csproj | PROD-500, FRAME-500, FRAME-501, FRAME-502 | align Orleans pins |
| LICENSE | mcp-hosts-build | human | reviewed | 1-21 | MIT license | none | none | none |
| README.md | mcp-hosts-build | human | reviewed | 1-86 | Repo overview + quickstart | humans/agents | CLEAN-500 (adjunct) | write user promise |
| aspire.config.json | mcp-hosts-build | human | reviewed | 1-5 | Aspire CLI apphost pointer | aspire CLI | none | none |
| .codegraph/.gitignore | mcp-hosts-build | human | reviewed | 1-5 | Ignore local codegraph index | git | none | none |
| .codex/config.toml | mcp-hosts-build | human | reviewed | 1-52 | Codex-CLI MCP mirror of .mcp.json | Codex CLI | SEC-500 | drift risk with .mcp.json |
| .config/dotnet-tools.json | mcp-hosts-build | human | reviewed | 1-11 | Pinned csharp-ls tool manifest | dotnet tool restore | none | none |
| .config/.tools-restored | mcp-hosts-build | generated | reviewed | empty (0 lines) | Build sentinel (wrongly tracked) | Directory.Build.targets | REL-501 | git rm --cached |

## Subsystem: dotnet-tests


| path | subsystem | authored | status | line ranges reviewed | primary responsibility | key callers/deps | finding IDs | follow-up |
|---|---|---|---|---|---|---|---|---|
| tests/DigitalBrain.Tests/Kernel/AuthRequiredAIFunctionTests.cs | dotnet-tests | human | reviewed | 1-67 | Auth-gated AIFunction decorator behavior | AuthRequiredAIFunction, M.E.AI | — | none |
| tests/DigitalBrain.Tests/Kernel/AzureBlobPackConfigBackingStoreTests.cs | dotnet-tests | human | reviewed | 1-248 | Opaque blob naming, legacy migration, fail-closed key material | AzureBlobPackConfigBackingStore, Azure SDK fakes | TEST-603 | none |
| tests/DigitalBrain.Tests/Kernel/AzureClientHealthCheckRegistrationTests.cs | dotnet-tests | human | reviewed | 1-47 | Regression: /health 500 keyed-client DI bug | Aspire Azure client extensions | — | mirrors Program.cs by hand |
| tests/DigitalBrain.Tests/Kernel/BroadcastReactivityTests.cs | dotnet-tests | human | reviewed | 1-84 | Broadcast fan-out to activated handlers | NeuronTestBase, Neuron.Broadcast | TEST-607 | none |
| tests/DigitalBrain.Tests/Kernel/CheckpointKeyingTests.cs | dotnet-tests | human | reviewed | 1-65 | Checkpoint key config; production fail-fast | AddKernelSecurity, protectors | — | none |
| tests/DigitalBrain.Tests/Kernel/CheckpointSecurityTests.cs | dotnet-tests | human | reviewed | 1-53 | AES protector tamper detection + snapshot round-trip | AesNeuronStateProtector, CheckpointProtector | — | none |
| tests/DigitalBrain.Tests/Kernel/DigitalBrainChatClientRegistrationTests.cs | dotnet-tests | human | reviewed | 1-181 | Keyed chat client registration + fail-closed key errors | AddDigitalBrainChatClients | — | none |
| tests/DigitalBrain.Tests/Kernel/DigitalBrainModelRegistrySnapshotTests.cs | dotnet-tests | human | reviewed | 1-52 | Model registry snapshot read/filter | DigitalBrainModelRegistrySnapshot | — | none |
| tests/DigitalBrain.Tests/Kernel/FakeGrainContext.cs | dotnet-tests | human | reviewed | 1-34 | Minimal IGrainContext fake for mapper tests | LlmAttributeTests | — | none |
| tests/DigitalBrain.Tests/Kernel/HealthEndpointTests.cs | dotnet-tests | human | reviewed | 1-30 | /health and /alive 200 through real host | KernelWebApplicationFactory | — | none |
| tests/DigitalBrain.Tests/Kernel/KernelStaticServingTests.cs | dotnet-tests | human | reviewed | 1-43 | WEBROOT static serving + SPA fallback | KernelWebApplicationFactory | — | none |
| tests/DigitalBrain.Tests/Kernel/LlmAttributeTests.cs | dotnet-tests | human | reviewed | 1-85 | [Llm<T>] keyed chat client mapping | LlmAttributeMapper, FakeGrainContext | — | none |
| tests/DigitalBrain.Tests/Kernel/LlmResponderScopedConfigTests.cs | dotnet-tests | human | reviewed | 1-226 | Scoped-config chat client selection vs global | LlmResponderNeuron, IPackConfigStore | TEST-607 | none |
| tests/DigitalBrain.Tests/Kernel/LlmResponderTests.cs | dotnet-tests | human | reviewed | 1-90 | AskLlm broadcast → reply Signal | LlmResponderNeuron | TEST-607 | none |
| tests/DigitalBrain.Tests/Kernel/ManagedIdentityStorageSelectionTests.cs | dotnet-tests | human | reviewed | 1-56 | Managed-identity switch (logic duplicated in test) | Program.cs (mirrored) | TEST-602 | extract production predicate |
| tests/DigitalBrain.Tests/Kernel/NeuronBroadcastTests.cs | dotnet-tests | human | reviewed | 1-39 | Implicit channel subscription broadcast contract | ProbeNeuron | — | none |
| tests/DigitalBrain.Tests/Kernel/NeuronTests.cs | dotnet-tests | human | reviewed | 1-110 | Activation journaling, JSON payload safety, automations | ProbeNeuron, AutomationNeuron | — | none |
| tests/DigitalBrain.Tests/Kernel/PackConfigBackingStoreSelectionTests.cs | dotnet-tests | human | reviewed | 1-107 | Regression: ephemeral pack-config in prod; fail-closed key ring | AddPackConfigStore | — | none |
| tests/DigitalBrain.Tests/Kernel/PackConfigStoreTests.cs | dotnet-tests | human | reviewed | 1-78 | Token encryption at rest + scope/pack isolation | PackConfigStore, DataProtection | — | none |
| tests/DigitalBrain.Tests/Kernel/RollingUpdateRollbackTests.cs | dotnet-tests | human | reviewed | 1-26 | Simulated rolling-update rollback ordering | IAspireNeuron | TEST-609 | simulation only |
| tests/DigitalBrain.Tests/Kernel/RuntimeStateHostingTests.cs | dotnet-tests | human | reviewed | 1-143 | KEK derivation, fail-closed hosting, metadata-only health | UseDigitalBrainOrleans, RuntimeStateKeyRing | — | none |
| tests/DigitalBrain.Tests/Kernel/SelfEvolutionContractTests.cs | dotnet-tests | human | reviewed | 1-60 | Rail wire-contract pinning | SelfEvolutionProposal/Decision | — | none |
| tests/DigitalBrain.Tests/Kernel/SelfEvolutionDurabilityTests.cs | dotnet-tests | human | reviewed | 1-98 | Rail replay durability + no re-apply | OrleansJournalClusterFixture, SelfEvolutionNeuron | TEST-608 | verify phase untested |
| tests/DigitalBrain.Tests/Kernel/SelfEvolutionNeuronTests.cs | dotnet-tests | human | reviewed | 1-217 | Approve-before-apply, risk ceiling, rollback-required | SelfEvolutionNeuron, apply handlers | TEST-608 | none |
| tests/DigitalBrain.Tests/Kernel/SignalTests.cs | dotnet-tests | human | reviewed | 1-80 | Signal/AskLlm construction + Orleans serialization | Core synapses | — | none |
| tests/DigitalBrain.Tests/Kernel/TimelineStreamTests.cs | dotnet-tests | human | reviewed | 1-28 | Stream provider name + global stream | SynapseStream | — | none |
| tests/DigitalBrain.Tests/Runtime/AgentFrameworkWorkflowRunnerTests.cs | dotnet-tests | human | reviewed | 1-99 | Prior-workflow ownership validation | AgentFrameworkWorkflowRunner | — | none |
| tests/DigitalBrain.Tests/Runtime/AuthorizationFlowStartProxyTests.cs | dotnet-tests | human | reviewed | 1-186 | OAuth start proxy allowlist + hardened responses | AuthorizationFlowStartProxy (Mcp) | — | none |
| tests/DigitalBrain.Tests/Runtime/ContractsTests.cs | dotnet-tests | human | reviewed | 1-203 | Core runtime contract primitives (some unwired) | SessionTokenService, CapabilityIsolationGate | SEC-600, CLEAN-600 | wire-or-delete unwired types |
| tests/DigitalBrain.Tests/Runtime/ConversationSurfacePayloadTests.cs | dotnet-tests | human | reviewed | 1-126 | Payload phase mapping, action validation, byte caps | ConversationSurfacePayload | — | none |
| tests/DigitalBrain.Tests/Runtime/EffectPhaseProjectionTests.cs | dotnet-tests | human | reviewed | 1-107 | Legacy record repair + phase ordering | OperationOutboxRecord, SurfaceFeedTransitions | — | none |
| tests/DigitalBrain.Tests/Runtime/EncryptedDomainStateTests.cs | dotnet-tests | human | reviewed | 1-1290 | Encrypted state, transitions, leases, approvals, archives | ConversationTransitions, EncryptedRuntimeStateProtector | — | consider splitting file |
| tests/DigitalBrain.Tests/Runtime/InoDurabilityRecoveryValidationTests.cs | dotnet-tests | human | reviewed | 1-306 | Durable acceptance, reminder rehydration, trace correlation | McpInoCommandHandler, ConversationNeuron | TEST-604 | none |
| tests/DigitalBrain.Tests/Runtime/InoEffectConflictRecoveryTests.cs | dotnet-tests | human | reviewed | 1-289 | Effect-conflict reconciliation without re-execution | InoOperationWorkerGrain, barrier TimeProvider | — | flake watch |
| tests/DigitalBrain.Tests/Runtime/InoEffectPlanAuthorityTests.cs | dotnet-tests | human | reviewed | 1-59 | Plan scope/proof HMAC binding | InoEffectPlanAuthority | — | none |
| tests/DigitalBrain.Tests/Runtime/InoEffectPlanTransitionsTests.cs | dotnet-tests | human | reviewed | 1-77 | Immutable plan, payload scrub, bounds | InoEffectPlanTransitions | — | none |
| tests/DigitalBrain.Tests/Runtime/InoMutationGrantTests.cs | dotnet-tests | human | reviewed | 1-26 | Provider-write grants before approval | InoMutationGrants | — | none |
| tests/DigitalBrain.Tests/Runtime/InoReminderCadenceTests.cs | dotnet-tests | human | reviewed | 1-50 | Reminder periods ≥ Orleans minimum (reflection) | ConversationNeuron, worker/dispatcher grains | FRAME-600 | none |
| tests/DigitalBrain.Tests/Runtime/InoReminderHandoffTests.cs | dotnet-tests | human | reviewed | 1-511 | Reminder handoff, outbox ordering, legacy upgrades | ConversationNeuron, dispatcher grain | — | none |
| tests/DigitalBrain.Tests/Runtime/InoTraceCorrelationTests.cs | dotnet-tests | human | reviewed | 1-156 | Trace tags + no prompt/token/payload leakage | InoOperationWorkerGrain, ActivityListener | — | none |
| tests/DigitalBrain.Tests/Runtime/InoWorkerConflictRecoveryTests.cs | dotnet-tests | human | reviewed | 1-209 | Post-result conflict, single workflow run | InoOperationWorkerGrain | — | flake watch |
| tests/DigitalBrain.Tests/Runtime/InoWorkflowFailureTests.cs | dotnet-tests | human | reviewed | 1-153 | Workflow failure/deadline terminal NeverRetry | ConversationNeuron | — | none |
| tests/DigitalBrain.Tests/Runtime/KernelCompositionTests.cs | dotnet-tests | human | reviewed | 1-145 | Production DI-graph + endpoint regression net | UseDigitalBrainOrleans, AddDigitalBrainClients | — | none |
| tests/DigitalBrain.Tests/Runtime/LegacyInoPipelineRemovalTests.cs | dotnet-tests | human | reviewed | 1-71 | Absence of legacy pipeline types | reflection over assemblies | FRAME-600 | retire later |
| tests/DigitalBrain.Tests/Runtime/OAuthStateProtectorTests.cs | dotnet-tests | human | reviewed | 1-42 | OAuth state opacity/tamper/expiry | DataProtectionOAuthStateProtector | — | none |
| tests/DigitalBrain.Tests/Runtime/RuntimeRequestAuthenticatorTests.cs | dotnet-tests | human | reviewed | 1-140 | MCP grant demand + external identity mapping | RuntimeRequestAuthenticator (Mcp) | — | none |
| tests/DigitalBrain.Tests/Runtime/RuntimeSurfaceFeedTests.cs | dotnet-tests | human | reviewed | 1-971 | Surface feed session/action/authorization semantics | RuntimeSurfaceFeed (Mcp), real transitions | — | none |
| tests/DigitalBrain.Tests/Runtime/RuntimeTransportBoundaryTests.cs | dotnet-tests | human | reviewed | 1-75 | Kestrel body-size bound without Content-Length | RuntimeTransportBoundary (Mcp) | — | rate-limit paths untested |
| tests/DigitalBrain.Tests/Runtime/SemanticIntentModelTests.cs | dotnet-tests | human | reviewed | 1-231 | Intent/mutation extraction, ID non-leakage, strict JSON | ConversationModelGrain | — | none |
| tests/DigitalBrain.Tests/Runtime/TypedReadWorkflowRunnerTests.cs | dotnet-tests | human | reviewed | 1-553 | Typed reads, auth handoff, preview-without-execute | AgentFrameworkWorkflowRunner, grain fakes | — | none |
| tests/DigitalBrain.Tests/Runtime/UiExternalIdentityTests.cs | dotnet-tests | human | reviewed | 1-177 | UI OIDC config + claim mapping fail-closed | UiExternalIdentityOptions (Mcp) | — | none |
| tests/DigitalBrain.Tests/Runtime/UiGrpcServiceTests.cs | dotnet-tests | human | reviewed | 1-61 | Rejection→status mapping, token refresh condition | UiGrpcService (Mcp) | — | none |
| tests/DigitalBrain.Tests/Architecture/AsyncContractArchitectureTests.cs | dotnet-tests | human | reviewed | 1-166 | CT-last conventions + analyzer pinning | reflection, .editorconfig | — | none |
| tests/DigitalBrain.Tests/Architecture/CoreBoundaryTests.cs | dotnet-tests | human | reviewed | 1-210 | Assembly-reference layering enforcement | Core/Contracts/Ui.Runtime assemblies | — | none |
| tests/DigitalBrain.Tests/Aspire/AddDigitalBrainExecutionModeTests.cs | dotnet-tests | human | reviewed | 1-304 | AppHost topology per run/test/prod/publish profile | Aspire.Hosting.Testing, AppHost | — | none |
| tests/DigitalBrain.Tests/Aspire/DigitalBrainClusterIdTests.cs | dotnet-tests | human | reviewed | 1-34 | Cluster-id resolution precedence | DigitalBrainBuilderExtensions | — | none |
| tests/DigitalBrain.Tests/Aspire/DigitalBrainModelCapabilitiesTests.cs | dotnet-tests | human | reviewed | 1-53 | Model capability descriptors + service-key normalization | LlmModel descriptors | — | none |
| tests/DigitalBrain.Tests/Aspire/DigitalBrainModelRegistryTests.cs | dotnet-tests | human | reviewed | 1-150 | Registry roles/defaults; production models tool-capable | DigitalBrainOptions | — | none |
| tests/DigitalBrain.Tests/Aspire/OAuthCallbackPathTests.cs | dotnet-tests | human | reviewed | 1-135 | Canonical OAuth paths + exact redirect allowlists | OAuthCallbackPaths, client factories | — | none |
| tests/DigitalBrain.Tests/Aspire/ResolveDevFlutterAppPathTests.cs | dotnet-tests | human | reviewed | 1-24 | Flutter path resolution (null case) | FlutterAspireExtensions | CLEAN-602 | merge with TestKit.Tests twin |
| tests/DigitalBrain.Tests/AssemblyInfo.cs | dotnet-tests | human | reviewed | 1-11 | MaxParallelThreads=2 pinning | xUnit | — | none |
| tests/DigitalBrain.Tests/Auth/UserSessionNeuronClientIdTests.cs | dotnet-tests | human | reviewed | 1-61 | Session-by-clientId lifecycle | UserSessionNeuron | TEST-610 | none |
| tests/DigitalBrain.Tests/Auth/UserSessionNeuronTests.cs | dotnet-tests | human | reviewed | 1-132 | Login/logout/registration + hash non-leak | UserSessionNeuron | TEST-610 | no lockout coverage |
| tests/DigitalBrain.Tests/Core/DbSchemaContractTests.cs | dotnet-tests | human | reviewed | 1-38 | Schema DTO carriage | DbSchemaModel | — | constructor-echo |
| tests/DigitalBrain.Tests/Core/ExperienceTypesTests.cs | dotnet-tests | human | reviewed | 1-32 | Experience DTO construction | ExperienceStep | — | constructor-echo |
| tests/DigitalBrain.Tests/Core/JsonElementSurrogateTests.cs | dotnet-tests | human | reviewed | 1-26 | JsonElement Orleans serialization regression | Orleans serializer | — | none |
| tests/DigitalBrain.Tests/Core/NeuronScopeTests.cs | dotnet-tests | human | reviewed | 1-47 | Scope parse/format + config scope prefixes | NeuronScope, PackConfigScopes | — | none |
| tests/DigitalBrain.Tests/Core/SynapsePayloadJsonTests.cs | dotnet-tests | human | reviewed | 1-40 | Payload JSON conventions (no JsonElement) | SynapsePayloadJson | — | none |
| tests/DigitalBrain.Tests/Db/SqliteSchemaInspectorTests.cs | dotnet-tests | human | reviewed | 1-50 | SQLite schema extraction | SqliteSchemaInspector | — | none |
| tests/DigitalBrain.Tests/Db/SqliteTestDatabases.cs | dotnet-tests | human | reviewed | 1-61 | Temp SQLite fixture builder | Microsoft.Data.Sqlite | — | none |
| tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj | dotnet-tests | human | reviewed | 1-65 | Main test project wiring + aliases | Orleans/Reqnroll/Aspire testing pkgs | CLEAN-601 | drop Grpc.Net.Client.Web |
| tests/DigitalBrain.Tests/Domains/ForExperienceHopTests.cs | dotnet-tests | human | reviewed | 1-42 | Experience-hop marker injection | UiSurface.ForExperienceHop | — | none |
| tests/DigitalBrain.Tests/Domains/KitExperienceTests.cs | dotnet-tests | human | reviewed | 1-259 | Pack DSL widget vocabulary + event stamping | KitExperience, UiHop | — | none |
| tests/DigitalBrain.Tests/Features/ChatFileAttachment.feature | dotnet-tests | human | reviewed | 1-27 | BDD file-attachment scenarios (vacuous) | ChatFileAttachmentSteps | TEST-600 | rewrite or delete |
| tests/DigitalBrain.Tests/Features/ChatFileAttachment.feature.cs | dotnet-tests | generated (Reqnroll 3 from .feature) | excluded-generated | spot-checked 1-258 | Generated scenario driver | Reqnroll runtime | TEST-600 | regenerated on build; low staleness risk |
| tests/DigitalBrain.Tests/Foundry/AzureResourceControllerTests.cs | dotnet-tests | human | reviewed | 1-16 | Dry-run restart intent recording | AzureResourceController | — | near-vacuous |
| tests/DigitalBrain.Tests/Foundry/CapabilityGateTests.cs | dotnet-tests | human | reviewed | 1-69 | Static gate allow/deny happy paths | CapabilityGate, FoundryCompilation | SEC-601 | add escape-attempt tests |
| tests/DigitalBrain.Tests/Foundry/CodeRunNeuronWiringTests.cs | dotnet-tests | human | reviewed | 1-28 | CodeRun grain executes generated code | ICodeRunNeuron | — | none |
| tests/DigitalBrain.Tests/Foundry/FoundryFakes.cs | dotnet-tests | human | reviewed | 1-50 | Fakes + tests of the fakes | IBuildRunner, IResourceController | TEST-605 | delete FoundryFakesTests |
| tests/DigitalBrain.Tests/Foundry/InProcessAlcExecutorTests.cs | dotnet-tests | human | reviewed | 1-48 | ALC executor run/compile-error/banned-symbol | InProcessAlcExecutor | SEC-601 | none |
| tests/DigitalBrain.Tests/Integrations/GoogleGmailApiClientTests.cs | dotnet-tests | human | reviewed | 1-605 | Gmail metadata-only, injection, send hardening | GoogleGmailApiClient, recording handler | — | none |
| tests/DigitalBrain.Tests/Integrations/IConnectorContractTests.cs | dotnet-tests | human | reviewed | 1-210 | Reusable connector contract (weak base) | IConnector, Salesforce/Google connectors | TEST-601 | tighten base, delete dummy |
| tests/DigitalBrain.Tests/Integrations/OAuthConnectorSecurityTests.cs | dotnet-tests | human | reviewed | 1-696 | OAuth security suite (scopes, isolation, replay, pinning) | GoogleConnector, SalesforceConnector | — | none |
| tests/DigitalBrain.Tests/Llm/ChatClientRegistrationTests.cs | dotnet-tests | human | reviewed | 1-217 | Provider registration + telemetry content suppression | AddDigitalBrainChat, DigitalBrainChatTelemetry | — | none |
| tests/DigitalBrain.Tests/Llm/DigitalBrainChatEmbeddingRegistrationTests.cs | dotnet-tests | human | reviewed | 1-46 | Embedding registration / no-op fallback | AddDigitalBrainChat | — | none |
| tests/DigitalBrain.Tests/Llm/DigitalBrainChatPolicyTests.cs | dotnet-tests | human | reviewed | 1-108 | Concurrency cap + no-retry policy | DigitalBrainChatTelemetry.Wrap | — | none |
| tests/DigitalBrain.Tests/Llm/DigitalBrainEmbeddingRuntimeOptionsTests.cs | dotnet-tests | human | reviewed | 1-38 | Embedding options binding | DigitalBrainEmbeddingRuntimeOptions | — | none |
| tests/DigitalBrain.Tests/Sandbox/OutOfProcessSandboxTests.cs | dotnet-tests | human | reviewed | 1-54 | Process separation + gate rejection | OutOfProcessSandbox | SEC-601 | no runtime confinement test |
| tests/DigitalBrain.Tests/Spikes/JournalFormatSpikeTests.cs | dotnet-tests | human | reviewed | 1-64 | JSON journal format replay round-trip | OrleansJournalClusterFixture | — | rename out of Spikes |
| tests/DigitalBrain.Tests/Steps/ChatFileAttachmentSteps.cs | dotnet-tests | human | reviewed | 1-93 | BDD steps constructing their own expectations | TableSurface only | TEST-600 | rewrite or delete |
| tests/DigitalBrain.Tests/TabularData/TabularDataParserTests.cs | dotnet-tests | human | reviewed | 1-89 | Real XLSX parse, stats, UI row cap | TabularDataParser, ClosedXML | — | none |
| tests/DigitalBrain.Tests/TestSupport/AsyncTestWait.cs | dotnet-tests | human | reviewed | 1-55 | Bounded throwing wait helper | test files | TEST-607 | promote usage |
| tests/DigitalBrain.Tests/TestSupport/CapturingServerStreamWriter.cs | dotnet-tests | human | reviewed | 1-24 | gRPC stream capture fake | Grpc.Core | — | verify still used |
| tests/DigitalBrain.Tests/TestSupport/FakeHostEnvironment.cs | dotnet-tests | human | reviewed | 1-15 | Minimal IHostEnvironment | CheckpointKeyingTests | — | none |
| tests/DigitalBrain.Tests/TestSupport/KernelHostCollection.cs | dotnet-tests | human | reviewed | 1-5 | kernel-host collection definition | KernelWebApplicationFactory | TEST-610 | none |
| tests/DigitalBrain.Tests/TestSupport/KernelWebApplicationFactory.cs | dotnet-tests | human | reviewed | 1-27 | Test-mode kernel host factory | WebApplicationFactory<Program> | — | none |
| tests/DigitalBrain.Tests/TestSupport/OrleansJournalClusterFixture.cs | dotnet-tests | human | reviewed | 1-76 | Shared journaled cluster + static apply recorder | Orleans.Journaling, InProcessTestCluster | — | static state, serialized collection |
| tests/DigitalBrain.Tests/TestSupport/TestGrainFactory.cs | dotnet-tests | human | reviewed | 1-37 | IGrainFactory adapter over NeuronTestBase | NeuronTestBase | — | verify still used |
| tests/DigitalBrain.Tests/TestSupport/TestServerCallContext.cs | dotnet-tests | human | reviewed | 1-46 | Minimal ServerCallContext | Grpc.Core | — | verify still used |
| tests/DigitalBrain.Tests/Ui/BundleHarness.cs | dotnet-tests | human | reviewed | 1-42 | Pack-bundle compile+drive harness (unused) | PackAlcEmbodier | CLEAN-602 | delete or adopt |
| tests/DigitalBrain.Tests/Ui/ChatNeuronTests.cs | dotnet-tests | human | reviewed | 1-21 | Visualize request → RfwCard | IChatNeuron | — | none |
| tests/DigitalBrain.Tests/Ui/ExperienceTestHarness.cs | dotnet-tests | human | reviewed | 1-184 | UiWidgetTree assertion library (unused) | UiWidgetTree | CLEAN-602 | delete or adopt |
| tests/DigitalBrain.Tests/Uploads/ChatUploadClassifierTests.cs | dotnet-tests | human | reviewed | 1-20 | Upload extension classification | ChatUploadClassifier | — | none |
| tests/DigitalBrain.Salesforce.Tests/DigitalBrain.Salesforce.Tests.csproj | dotnet-tests | human | reviewed | 1-25 | Salesforce test project wiring | Salesforce, Kernel, TestKit | — | none |
| tests/DigitalBrain.Salesforce.Tests/FakeSalesforceTokenHandler.cs | dotnet-tests | human | reviewed | 1-55 | Token-endpoint fake + query helper | HttpMessageHandler | — | none |
| tests/DigitalBrain.Salesforce.Tests/SalesforceApiClientTests.cs | dotnet-tests | human | reviewed | 1-168 | Identity allowlist, bounded reads, no caller SOQL | SalesforceApiClient, ForceClient | — | none |
| tests/DigitalBrain.Salesforce.Tests/SalesforceClientFactoryTests.cs | dotnet-tests | human | reviewed | 1-225 | Endpoints, PKCE (RFC vector), redirect allowlist, config validation | SalesforceClientFactory | — | none |
| tests/DigitalBrain.Salesforce.Tests/SalesforceMutationApiClientTests.cs | dotnet-tests | human | reviewed | 1-176 | Preview→apply→verify mutation loop, conflict/verification failure | SalesforceApiClient | — | none |
| tests/DigitalBrain.Salesforce.Tests/SalesforceOAuthStartNeuronTests.cs | dotnet-tests | human | reviewed | 1-359 | Persistent OAuth start, idempotent redirects, callback replay | SalesforceReadNeuron grain, SalesforceConnector | — | none |
| tests/DigitalBrain.Salesforce.Tests/SalesforceReadNeuronContinuationTests.cs | dotnet-tests | human | reviewed | 1-210 | Continuation retry/consume/reactivation semantics | SalesforceReadNeuron grain | — | none |
| tests/DigitalBrain.Salesforce.Tests/SalesforceSemanticReadTests.cs | dotnet-tests | human | reviewed | 1-197 | Semantic reads, SOQL/SOSL injection fail-closed | SalesforceApiClient | — | none |
| tests/DigitalBrain.TestKit.Tests/Aspire/ResolveDevFlutterAppPathTests.cs | dotnet-tests | human | reviewed | 1-30 | Flutter path resolution (repo-root case) | FlutterAspireExtensions | CLEAN-602 | merge with main-suite twin |
| tests/DigitalBrain.TestKit.Tests/DigitalBrain.TestKit.Tests.csproj | dotnet-tests | human | reviewed | 1-26 | Near-empty smoke project | TestKit, Aspire | CLEAN-602 | consider folding |
| tests/DigitalBrain.TestKit.Tests/NeuronTestBaseTests.cs | dotnet-tests | human | reviewed | 1-16 | Harness smoke (grain resolves) | NeuronTestBase | — | none |
| tests/DigitalBrain.TestKit.Tests/TestDigitalBrainTests.cs | dotnet-tests | human | reviewed | 1-23 | Harness smoke (timeline NotNull) | TestDigitalBrain | — | none |
| tests/DigitalBrain.TestKit/DigitalBrain.TestKit.csproj | dotnet-tests | human | reviewed | 1-30 | Harness library project (IsTestProject=false) | Core, Kernel, Mcp, TestingHost | — | none |
| tests/DigitalBrain.TestKit/IDigitalBrain.cs | dotnet-tests | human | reviewed | 1-11 | Harness abstraction | TestDigitalBrain | — | none |
| tests/DigitalBrain.TestKit/NeuronTestBase.cs | dotnet-tests | human | reviewed | 1-32 | Per-test in-proc cluster base | TestDigitalBrain | — | none |
| tests/DigitalBrain.TestKit/NeuronTestKernelConfigurator.cs | dotnet-tests | human | reviewed | 1-63 | Shared silo wiring (journals, handlers, no-op factories) | Orleans TestingHost, Kernel services | — | none |
| tests/DigitalBrain.TestKit/ProbeContracts.cs | dotnet-tests | human | reviewed | 1-19 | Probe synapse/interface in Core namespace | CapabilityGate allowlist | — | namespace impersonation documented |
| tests/DigitalBrain.TestKit/ProbeNeuron.cs | dotnet-tests | human | reviewed | 1-22 | Probe grain + JSON signal firing | Neuron base | — | none |
| tests/DigitalBrain.TestKit/PrototypeJournalSupport.cs | dotnet-tests | human | reviewed | 1-29 | In-memory durable list + no-op state manager | Orleans.Journaling | — | none |
| tests/DigitalBrain.TestKit/TestDigitalBrain.cs | dotnet-tests | human | reviewed | 1-82 | Cluster bootstrap; sets DIGITALBRAIN_TEST_MODE process-wide | InProcessTestClusterBuilder | TEST-606 | fix env handling |

## Subsystem: flutter-runtime


| path | subsystem | authored | status | line ranges reviewed | primary responsibility | key callers/deps | finding IDs | follow-up |
|---|---|---|---|---|---|---|---|---|
| app/lib/app.dart | flutter-runtime | human | reviewed | 1-42 | MaterialApp.router root with forui theming | main.dart; router.dart | — | none |
| app/lib/features/brain/voice_input.dart | flutter-runtime | human | reviewed | 1-267 | Voice record + v1 gateway transcription widget | rfw_host library (unreachable) | ARCH-700, SEC-702, TEST-701 | delete or port to v2 rail |
| app/lib/features/live/graph/brain_painter.dart | flutter-runtime | human | reviewed | 1-913 | Legacy 3D brain graph painter | none (dead) | CLEAN-700, TEST-701 | delete |
| app/lib/features/live/graph/cluster_layout.dart | flutter-runtime | human | reviewed | 1-115 | Graph node/edge models + force layout | rfw_host/synapse_stream_scope (models only) | CLEAN-700, CLEAN-704 | delete stepLayout/sphericalSeed |
| app/lib/features/live/graph/comet.dart | flutter-runtime | human | reviewed | 1-165 | Comet animation for brain graph | brain_painter only (dead) | CLEAN-700 | delete |
| app/lib/features/live/graph/domain_palette.dart | flutter-runtime | human | reviewed | 1-110 | Domain colors/anchors + synapse color map | rfw_host library; cluster_layout | — | keep while RFW palette lives |
| app/lib/grpc/digitalbrain.pb.dart | flutter-runtime | generated | excluded-generated | headers only | protoc-gen-dart output of digitalbrain.proto (v1 gateway) | rfw_host, voice_input, interceptor | ARCH-700 | checked-in; regen procedure undocumented |
| app/lib/grpc/digitalbrain.pbgrpc.dart | flutter-runtime | generated | excluded-generated | headers only | v1 DigitalBrainGateway client stubs | rfw_host, voice_input | ARCH-700 | deletable with v1 rail |
| app/lib/grpc/endpoint.dart | flutter-runtime | human | reviewed | 1-143 | Legacy kernel endpoint resolution (Aspire env/URL) | test only — no prod callers | CLEAN-700, SEC-703, TEST-701 | delete with test |
| app/lib/grpc/grpc_channel.dart | flutter-runtime | human | reviewed | 1-21 | Legacy kernel channel factory + interceptor list | none (dead) | CLEAN-700 | delete |
| app/lib/grpc/ui.pb.dart | flutter-runtime | generated | excluded-generated | headers only | protoc output of ui.proto (v2 runtime) | grpc_ui_transport | — | checked-in; staleness risk vs kernel proto |
| app/lib/grpc/ui.pbenum.dart | flutter-runtime | generated | excluded-generated | headers only | ui.proto enum output | ui.pb.dart | — | none |
| app/lib/grpc/ui.pbgrpc.dart | flutter-runtime | generated | excluded-generated | headers only | DigitalBrainV2Ui client stubs | grpc_ui_transport | — | none |
| app/lib/main.dart | flutter-runtime | human | reviewed | 1-46 | App bootstrap: telemetry, fonts, perf scope, runApp | app.dart, telemetry | CLEAN-701 | none |
| app/lib/router.dart | flutter-runtime | human | reviewed | 1-15 | go_router config: / → /chat → RuntimeShell | app.dart | — | none |
| app/lib/runtime/buses/ino_editor_bus.dart | flutter-runtime | human | reviewed | 1-21 | Global editor↔subscription link (never assigned) | rfw_host (reads null) | ARCH-701, CLEAN-704 | delete |
| app/lib/runtime/buses/ino_source_subscription.dart | flutter-runtime | human | reviewed | 1-45 | Chunk accumulator for INO source cards | ino_editor_bus (dead) | ARCH-701, CLEAN-704 | delete |
| app/lib/runtime/buses/llm_settings_bus.dart | flutter-runtime | human | reviewed | 1-51 | Global LLM settings side channel | rfw_host library | ARCH-701 | scope to session or delete |
| app/lib/runtime/buses/prompt_input_bus.dart | flutter-runtime | human | reviewed | 1-27 | Global prompt-text side channel for RFW | rfw_host library | ARCH-701, CLEAN-704 | scope to session or delete |
| app/lib/runtime/buses/state_editor_bus.dart | flutter-runtime | human | reviewed | 1-29 | Global state-editor value side channel | rfw_host library | ARCH-701 | delete |
| app/lib/runtime/buses/typewriter_controller.dart | flutter-runtime | human | reviewed | 1-55 | Timer-driven typewriter text reveal | rfw_host library | REL-703 | none |
| app/lib/runtime/external_identity.dart | flutter-runtime | human | reviewed | 1-11 | Conditional-import facade for OIDC source | runtime_session_owner | — | none |
| app/lib/runtime/external_identity_contract.dart | flutter-runtime | human | reviewed | 1-17 | OIDC config + token source interface | runtime_configuration, web/stub impls | — | none |
| app/lib/runtime/external_identity_stub.dart | flutter-runtime | human | reviewed | 1-19 | Non-web unsupported stub | external_identity | — | none |
| app/lib/runtime/external_identity_web.dart | flutter-runtime | human | reviewed | 1-50 | Browser OIDC via openid_client Authenticator | external_identity; openid_client | SEC-700 | switch to code+PKCE flow |
| app/lib/runtime/feed_state.dart | flutter-runtime | human | reviewed | 1-219 | Feed contracts + sequence/scope-enforcing controller | runtime.dart; session_state | — | none |
| app/lib/runtime/grpc_ui_transport.dart | flutter-runtime | human | reviewed | 1-563 | gRPC UiTransport: TLS, auth metadata, safe errors | RuntimeShell default factory; ui.pbgrpc | — | none |
| app/lib/runtime/protocol/surface_protocol.dart | flutter-runtime | human | reviewed | 1-1246 | Defensive surface envelope/payload/action decoder | runtime.dart, feed_state, views | — | none |
| app/lib/runtime/runtime.dart | flutter-runtime | human | reviewed | 1-453 | RuntimeController: auth + feed loop + actions | runtime_session_owner; transport, feed | REL-700, PERF-700 | none |
| app/lib/runtime/runtime_configuration.dart | flutter-runtime | human | reviewed | 1-124 | Env-derived endpoint/secret/OIDC config, fail-closed | runtime_session_owner | — | none |
| app/lib/runtime/runtime_errors.dart | flutter-runtime | human | reviewed | 1-48 | Safe-message transport error taxonomy | all runtime files | — | none |
| app/lib/runtime/runtime_session_owner.dart | flutter-runtime | human | reviewed | 1-172 | Non-visual session lifecycle owner | runtime_shell | — | none |
| app/lib/runtime/session_state.dart | flutter-runtime | human | reviewed | 1-299 | Session controller: bootstrap/refresh/sign-out, redacted creds | runtime.dart; grpc_ui_transport | — | none |
| app/lib/runtime/widgets/ino_composer.dart | flutter-runtime | human | reviewed | 1-73 | Bounded INO message composer | ino_conversation_view | — | none |
| app/lib/runtime/widgets/ino_conversation_view.dart | flutter-runtime | human | reviewed | 1-888 | INO chat: optimistic turns, delivery certainty, approvals | surface_view | PROD-702 | none |
| app/lib/runtime/widgets/runtime_shell.dart | flutter-runtime | human | reviewed | 1-291 | Root shell: sign-in, states, scope-epoch isolation | router.dart | — | none |
| app/lib/runtime/widgets/surface_view.dart | flutter-runtime | human | reviewed | 1-235 | Payload dispatch + action submission UI | runtime_shell; rfw_host | PROD-701, CLEAN-703 | none |
| app/lib/shell/digitalbrain_client_scope.dart | flutter-runtime | human | reviewed | 1-27 | InheritedWidget for v1 gateway client (never mounted) | rfw_host library (gets null) | ARCH-700, CLEAN-704 | delete |
| app/lib/telemetry/bloc_observer.dart | flutter-runtime | human | reviewed | 1-55 | Bloc telemetry observer (no blocs exist) | main.dart | CLEAN-701, SEC-702 | delete with flutter_bloc |
| app/lib/telemetry/export_circuit_breaker.dart | flutter-runtime | human | reviewed | 1-19 | Permanent-trip export circuit breaker | otlp exporters | REL-701 | add half-open |
| app/lib/telemetry/grpc_interceptor.dart | flutter-runtime | human | reviewed | 1-168 | OTel spans + client-asserted identity headers (v1, dead) | grpc_channel (uncalled) | SEC-701, SEC-702, FRAME-702 | delete with v1 rail |
| app/lib/telemetry/otlp_log_exporter.dart | flutter-runtime | human | reviewed | 1-148 | Hand-rolled OTLP/JSON log exporter | telemetry.dart | REL-702, TEST-700 | add golden tests |
| app/lib/telemetry/otlp_metric_exporter.dart | flutter-runtime | human | reviewed | 1-214 | Hand-rolled OTLP/JSON metric exporter | telemetry.dart | FRAME-701, TEST-700 | add golden tests |
| app/lib/telemetry/platform_env.dart | flutter-runtime | human | reviewed | 1-3 | getEnv conditional-import selector | endpoint, runtime_configuration, telemetry | FRAME-700 | switch to js_interop key |
| app/lib/telemetry/platform_env_io.dart | flutter-runtime | human | reviewed | 1-3 | Platform.environment lookup | platform_env | — | none |
| app/lib/telemetry/platform_env_stub.dart | flutter-runtime | human | reviewed | 1-1 | Null env stub | platform_env | — | none |
| app/lib/telemetry/platform_env_web.dart | flutter-runtime | human | reviewed | 1-14 | JS-global KERNEL_PORT lookup | platform_env | FRAME-700 | none |
| app/lib/telemetry/telemetry.dart | flutter-runtime | human | reviewed | 1-131 | Telemetry singleton wiring (traces+logs+metrics) | main.dart, interceptor, observer | TEST-700 | shutdown never called |
| app/lib/widgetbook.dart | flutter-runtime | human | reviewed | 1-109 | Dev RFW palette catalog entrypoint | standalone -t target; rfw_host | CLEAN-702 | move widgetbook to dev_dependencies |
| app/lib/widgets/canvas_3d.dart | flutter-runtime | human | reviewed | 1-359 | 3D atom/bond viewer (no callers) | none (dead) | CLEAN-700, TEST-701 | delete or register in palette |
| app/lib/widgets/neuron_vector_logo.dart | flutter-runtime | human | reviewed | 1-666 | Neuron category icon painter | ui_kit/ui_button, brain_painter, tests | — | none |

## Subsystem: flutter-ui


| path | subsystem | authored | status | line ranges reviewed | primary responsibility (<=10 words) | key callers/deps (<=3) | finding IDs | follow-up |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| app/lib/rfw_host/rfw_runtime_host.dart | flutter-ui/rfw_host | human | reviewed | 1-827 | RFW runtime wrapper + JSON-tree renderer | rfw.Runtime, ui_registry, surface_view | SEC-800, ARCH-801, REL-803 | Split god-method; harden event surface |
| app/lib/rfw_host/digitalbrain_rfw_library.dart | flutter-ui/rfw_host | human | reviewed | 1-3218 | RFW widget dictionary + embedded editor/catalog | rfw, google_fonts, grpc client | SEC-801, SEC-803, REL-800, REL-801, PERF-801, PROD-800 | Split into 3 files (dict/editor/catalog) |
| app/lib/rfw_host/library/basic.dart | flutter-ui/rfw_host | human | reviewed | 1-255 | Panel/Text/Button/Badge/Progress/Avatar/TaskRow builders | digitalbrain_rfw_library (part) | none | none |
| app/lib/rfw_host/library/chat.dart | flutter-ui/rfw_host | human | reviewed | 1-36 | SynapseStream RFW builder | SynapseStreamScope | none | Stale "will be moved" comment |
| app/lib/rfw_host/library/data.dart | flutter-ui/rfw_host | human | reviewed | 1-90 | Timeline RFW builder | digitalbrain_rfw_library (part) | none | Stale comment |
| app/lib/rfw_host/library/helpers.dart | flutter-ui/rfw_host | human | reviewed | 1-85 | DataSource readers + tone/variant helpers | DigitalBrainColors | FRAME-800 | none |
| app/lib/rfw_host/library/layout.dart | flutter-ui/rfw_host | human | reviewed | 1-74 | Divider/Stack/Pad layout builders | digitalbrain_rfw_library (part) | none | none |
| app/lib/rfw_host/palette/palette_primitives.dart | flutter-ui/rfw_host | human | reviewed | 1-810 | Unregistered Lottie/globe/clock RFW primitives | lottie, flutter_earth_globe, rfw | ARCH-800, CLEAN-801 | DEAD CODE — delete or register |
| app/lib/rfw_host/synapse_stream_scope.dart | flutter-ui/rfw_host | human | reviewed | 1-28 | Synapse-edge feed InheritedNotifier | cluster_layout GraphEdge | none | none |
| app/lib/theme/digitalbrain_theme.dart | flutter-ui/theme | human | reviewed | 1-254 | Color tokens, theme, GlassBorder painter | google_fonts, Material 3 | FRAME-800 | Rename misleading color tokens |
| app/lib/ui_kit/ui_registry.dart | flutter-ui/ui_kit | human | reviewed | 1-237 | ui:* node -> widget switchboard | all ui_kit widgets, rfw | ARCH-801 | Consolidate with tree renderer |
| app/lib/ui_kit/ui_graph_canvas.dart | flutter-ui/ui_kit | human | reviewed | 1-538 | Bespoke schema/grid graph canvas | forui FTheme | CLEAN-802 | Rename "force" layout |
| app/lib/ui_kit/ui_alert.dart | flutter-ui/ui_kit | human | reviewed | 1-14 | FAlert wrapper | forui | none | none |
| app/lib/ui_kit/ui_avatar.dart | flutter-ui/ui_kit | human | reviewed | 1-13 | FAvatar wrapper | forui | none | none |
| app/lib/ui_kit/ui_badge.dart | flutter-ui/ui_kit | human | reviewed | 1-10 | FBadge wrapper | forui | none | none |
| app/lib/ui_kit/ui_bottom_nav.dart | flutter-ui/ui_kit | human | reviewed | 1-47 | FBottomNavigationBar wrapper | ui_nav_item, ui_form_scope | none | none |
| app/lib/ui_kit/ui_breadcrumb.dart | flutter-ui/ui_kit | human | reviewed | 1-42 | FBreadcrumb wrapper | ui_nav_item, ui_form_scope | none | none |
| app/lib/ui_kit/ui_button.dart | flutter-ui/ui_kit | human | reviewed | 1-74 | FButton with event forwarding | ui_form_scope, NeuronVectorLogo | SEC-800, ARCH-802 | Decouple from features/ |
| app/lib/ui_kit/ui_checkbox.dart | flutter-ui/ui_kit | human | reviewed | 1-25 | FCheckbox bound to form scope | ui_form_scope | none | none |
| app/lib/ui_kit/ui_column.dart | flutter-ui/ui_kit | human | reviewed | 1-16 | Column layout wrapper | flutter | none | none |
| app/lib/ui_kit/ui_date_field.dart | flutter-ui/ui_kit | human | reviewed | 1-46 | FDateField bound to form scope | forui, ui_form_scope | none | none |
| app/lib/ui_kit/ui_dialog.dart | flutter-ui/ui_kit | human | reviewed | 1-38 | FDialog present-once wrapper | ui_overlay_host, forui | none | none |
| app/lib/ui_kit/ui_divider.dart | flutter-ui/ui_kit | human | reviewed | 1-9 | FDivider wrapper | forui | none | none |
| app/lib/ui_kit/ui_form_scope.dart | flutter-ui/ui_kit | human | reviewed | 1-23 | Form value controller + InheritedNotifier | flutter | none | none |
| app/lib/ui_kit/ui_gap.dart | flutter-ui/ui_kit | human | reviewed | 1-9 | SizedBox spacer | flutter | none | none |
| app/lib/ui_kit/ui_header.dart | flutter-ui/ui_kit | human | reviewed | 1-10 | FHeader wrapper | forui | none | none |
| app/lib/ui_kit/ui_heading.dart | flutter-ui/ui_kit | human | reviewed | 1-13 | Heading text via FTheme typography | forui | none | none |
| app/lib/ui_kit/ui_icon.dart | flutter-ui/ui_kit | human | reviewed | 1-23 | Icon-name -> FIcons map, safe fallback | forui | none | none |
| app/lib/ui_kit/ui_link.dart | flutter-ui/ui_kit | human | reviewed | 1-28 | Launch URL via url_launcher | url_launcher | SEC-802 | Add scheme allowlist |
| app/lib/ui_kit/ui_list.dart | flutter-ui/ui_kit | human | reviewed | 1-36 | FTileGroup from tile descriptors | ui_tile, ui_form_scope | SEC-800 | none |
| app/lib/ui_kit/ui_nav_item.dart | flutter-ui/ui_kit | human | reviewed | 1-35 | Nav-item parsing + fireNav event | rfw RemoteEventHandler | SEC-800 | none |
| app/lib/ui_kit/ui_overlay_host.dart | flutter-ui/ui_kit | human | reviewed | 1-17 | PresentOnce mixin for overlays | flutter | REL-802 | none |
| app/lib/ui_kit/ui_pagination.dart | flutter-ui/ui_kit | human | reviewed | 1-48 | FButton pagination row | ui_nav_item, ui_form_scope | none | none |
| app/lib/ui_kit/ui_panel.dart | flutter-ui/ui_kit | human | reviewed | 1-18 | FCard column wrapper | forui | none | none |
| app/lib/ui_kit/ui_progress.dart | flutter-ui/ui_kit | human | reviewed | 1-10 | FDeterminateProgress wrapper | forui | none | none |
| app/lib/ui_kit/ui_radio_group.dart | flutter-ui/ui_kit | human | reviewed | 1-46 | FRadio group bound to form scope | forui, ui_form_scope | none | none |
| app/lib/ui_kit/ui_row.dart | flutter-ui/ui_kit | human | reviewed | 1-15 | Row layout wrapper | flutter | none | none |
| app/lib/ui_kit/ui_screen.dart | flutter-ui/ui_kit | human | reviewed | 1-64 | Screen scaffold w/ sidebar extraction + form scope | ui_form_scope, ui_sidebar | none | none |
| app/lib/ui_kit/ui_select.dart | flutter-ui/ui_kit | human | reviewed | 1-52 | FSelect bound to form scope | forui, ui_form_scope | none | none |
| app/lib/ui_kit/ui_sheet.dart | flutter-ui/ui_kit | human | reviewed | 1-38 | FSheet present-once wrapper | ui_overlay_host, forui | none | none |
| app/lib/ui_kit/ui_sidebar.dart | flutter-ui/ui_kit | human | reviewed | 1-46 | FSidebar nav wrapper | ui_nav_item, ui_form_scope | SEC-800 | none |
| app/lib/ui_kit/ui_slider.dart | flutter-ui/ui_kit | human | reviewed | 1-26 | FSlider bound to form scope | forui, ui_form_scope | none | none |
| app/lib/ui_kit/ui_spinner.dart | flutter-ui/ui_kit | human | reviewed | 1-9 | FCircularProgress wrapper | forui | none | none |
| app/lib/ui_kit/ui_switch.dart | flutter-ui/ui_kit | human | reviewed | 1-25 | FSwitch bound to form scope | forui, ui_form_scope | none | none |
| app/lib/ui_kit/ui_table.dart | flutter-ui/ui_kit | human | reviewed | 1-40 | FCard-based table | forui | none | none |
| app/lib/ui_kit/ui_tabs.dart | flutter-ui/ui_kit | human | reviewed | 1-44 | FTabs nav wrapper | ui_nav_item, ui_form_scope | SEC-800 | none |
| app/lib/ui_kit/ui_text.dart | flutter-ui/ui_kit | human | reviewed | 1-10 | Plain Text wrapper | flutter | none | none |
| app/lib/ui_kit/ui_text_area.dart | flutter-ui/ui_kit | human | reviewed | 1-28 | FTextField.multiline bound to form scope | forui, ui_form_scope | none | none |
| app/lib/ui_kit/ui_text_field.dart | flutter-ui/ui_kit | human | reviewed | 1-37 | FTextField bound to form scope | forui, ui_form_scope | none | none |
| app/lib/ui_kit/ui_tile.dart | flutter-ui/ui_kit | human | reviewed | 1-66 | FTile builder w/ experience event | forui, ui_form_scope | SEC-800 | none |
| app/lib/ui_kit/ui_toast.dart | flutter-ui/ui_kit | human | reviewed | 1-22 | FToast present-once wrapper | ui_overlay_host, forui | REL-802 | Dedupe on rebuild |
| app/lib/ui_kit/ui_tooltip.dart | flutter-ui/ui_kit | human | reviewed | 1-12 | FTooltip wrapper | forui | none | none |
| app/lib/digital_brain_ui/digital_brain_ui.dart | flutter-ui/digital_brain_ui | human | reviewed | 1-26 | Package barrel export | (exports) | none | none |
| app/lib/digital_brain_ui/adaptive/adaptive_dialog.dart | flutter-ui/digital_brain_ui | human | reviewed | 1-53 | Platform-adaptive centered dialog | glass_material | none | Doc drift (Cupertino claim) |
| app/lib/digital_brain_ui/adaptive/adaptive_sheet.dart | flutter-ui/digital_brain_ui | human | reviewed | 1-123 | Platform-adaptive bottom sheet | Cupertino/Material | none | none |
| app/lib/digital_brain_ui/adaptive/adaptive_side_sheet.dart | flutter-ui/digital_brain_ui | human | reviewed | 1-53 | Right-docked side sheet | glass_material | none | none |
| app/lib/digital_brain_ui/adaptive/adaptive_surface.dart | flutter-ui/digital_brain_ui | human | reviewed | 1-167 | Weight x WindowSize overlay dispatch | window_size, sheets/dialogs | none | none |
| app/lib/digital_brain_ui/breakpoints/input_mode.dart | flutter-ui/digital_brain_ui | human | reviewed | 1-80 | Input-modality scope + tracker | flutter gestures | none | none |
| app/lib/digital_brain_ui/breakpoints/window_size.dart | flutter-ui/digital_brain_ui | human | reviewed | 1-4 | Material 3 window-size enum | (none) | none | none |
| app/lib/digital_brain_ui/breakpoints/window_size_scope.dart | flutter-ui/digital_brain_ui | human | reviewed | 1-50 | WindowSize InheritedWidget scope | window_size | none | none |
| app/lib/digital_brain_ui/debug/debug_brain_stats.dart | flutter-ui/digital_brain_ui | human | reviewed | 1-150 | Glass debug HUD (neuron/synapse counts) | glass_material | FRAME-801 | Use google_fonts / gate debug |
| app/lib/digital_brain_ui/density/adaptive_density.dart | flutter-ui/digital_brain_ui | human | reviewed | 1-42 | Adaptive spacing + visual density tokens | window_size, input_mode | CLEAN-803 | Wire in or fix comment |
| app/lib/digital_brain_ui/effects/brain_scene_effects.dart | flutter-ui/digital_brain_ui | human | reviewed | 1-54 | Scene-effect pulses notifier + scope | effects_pulse | PERF-802 | none |
| app/lib/digital_brain_ui/effects/effects_pulse.dart | flutter-ui/digital_brain_ui | human | reviewed | 1-38 | Sealed pulse effect types | dart:ui | none | none |
| app/lib/digital_brain_ui/glass/glass_material.dart | flutter-ui/digital_brain_ui | human | reviewed | 1-219 | Glass surface (dead shader path) | theme, dart:ui shaders | PERF-800, CLEAN-804 | Delete shader/ticker path |
| app/lib/digital_brain_ui/glass/liquid_glass_surface.dart | flutter-ui/digital_brain_ui | human | reviewed | 1-136 | Morph-in/collapse glass animation | glass_material | none | none |
| app/lib/digital_brain_ui/glow/glow_icon.dart | flutter-ui/digital_brain_ui | human | reviewed | 1-68 | Raster-cached glow-icon widget + prewarm | glow_painter, SDK perf tier | PERF-803 | none |
| app/lib/digital_brain_ui/glow/glow_icon_spec.dart | flutter-ui/digital_brain_ui | human | reviewed | 1-26 | Value-equal glow-icon spec | flutter | none | none |
| app/lib/digital_brain_ui/glow/glow_painter.dart | flutter-ui/digital_brain_ui | human | reviewed | 1-75 | Heatmap glow CustomPainter | glow_icon_spec | none | none |

Cross-cutting (not single-file): ARCH-802 (ui_kit->features coupling), PROD-800 (fake data in library panels), CLEAN-800 (unused graphic dep in pubspec), TEST-800 (rfw_host test gap).

## Subsystem: flutter-sdk-and-tests


| path | subsystem | authored | status | line ranges reviewed | primary responsibility | key callers/deps | finding IDs | follow-up |
|---|---|---|---|---|---|---|---|---|
| app/assets/ino-catalog.json | flutter-sdk-and-tests | human | reviewed | 1-47 | Static contract catalog for Creator prompt autocomplete | lib/rfw_host/digitalbrain_rfw_library.dart | SEC-901, CLEAN-904 | prune Acme entries; move server-side |
| app/assets/lottie/orbit.lottie | flutter-sdk-and-tests | vendored | excluded-generated | binary (13.3 MB, not line-auditable) | Lottie animation binary, referenced nowhere | pubspec assets bundle only | CLEAN-900 | delete (dead 13.3 MB asset) |
| app/assets/rfw/activity_overlay.rfwtxt | flutter-sdk-and-tests | human | reviewed | 1-1408 | Demo RFW overlay library (unused) | none (no loader in repo) | CLEAN-900 | delete |
| app/assets/rfw/sample_neuron.rfwtxt | flutter-sdk-and-tests | human | reviewed | 1-54 | Demo RFW neuron card (unused) | none (no loader in repo) | CLEAN-900 | delete |
| app/assets/shaders/glass_refract.frag | flutter-sdk-and-tests | human | reviewed | 1-44 | Glass material runtime-effect shader | lib/digital_brain_ui/glass/glass_material.dart | none | none |
| app/packages/digital_brain_sdk_flutter/.gitignore | flutter-sdk-and-tests | human | reviewed | 1-2 | Ignore build artifacts | git | none | none |
| app/packages/digital_brain_sdk_flutter/analysis_options.yaml | flutter-sdk-and-tests | human | reviewed | 1-28 | Stock flutter_lints analyzer config | dart analyzer | FRAME-902 | adopt stricter shared config |
| app/packages/digital_brain_sdk_flutter/lib/digital_brain_sdk_flutter.dart | flutter-sdk-and-tests | human | reviewed | 1-11 | Barrel export of SDK public API | app/lib/main.dart, glow_icon.dart | CLEAN-903 | drop dead export |
| app/packages/digital_brain_sdk_flutter/lib/src/gateway/perf_gateway_client.dart | flutter-sdk-and-tests | human | reviewed | 1-13 | Closure-injected gateway adapter (no gRPC dep) | main.dart (no-op closures), perf_stream.dart | ARCH-900 | specify contract when real gateway lands |
| app/packages/digital_brain_sdk_flutter/lib/src/gateway/perf_tier_hint.dart | flutter-sdk-and-tests | human | reviewed | 1-7 | Tier hint DTO | perf_stream.dart | none | none |
| app/packages/digital_brain_sdk_flutter/lib/src/perf/perf_probe.dart | flutter-sdk-and-tests | human | reviewed | 1-105 | Frame-timing sampler widget, debug/profile only | main.dart; perf_stream, widget_census | PROD-900, PROD-901 | fix rebuilds metric; parameterize jank budget |
| app/packages/digital_brain_sdk_flutter/lib/src/perf/perf_sample.dart | flutter-sdk-and-tests | human | reviewed | 1-26 | Immutable perf sample DTO | perf_probe, gateway adapter | PROD-900 | rename misnamed field |
| app/packages/digital_brain_sdk_flutter/lib/src/perf/perf_stream.dart | flutter-sdk-and-tests | human | reviewed | 1-75 | Outbox + retrying push/watch pumps | main.dart bootstrap | PERF-900, REL-900, ARCH-900 | reset/clamp backoff; log errors |
| app/packages/digital_brain_sdk_flutter/lib/src/perf/perf_tier.dart | flutter-sdk-and-tests | human | reviewed | 1-7 | Tier enum + lenient parser | throttle.dart, controller | none | none |
| app/packages/digital_brain_sdk_flutter/lib/src/perf/perf_tier_controller.dart | flutter-sdk-and-tests | human | reviewed | 1-13 | ChangeNotifier holding current tier | perf_tier_scope, perf_stream | none | none |
| app/packages/digital_brain_sdk_flutter/lib/src/perf/perf_tier_scope.dart | flutter-sdk-and-tests | human | reviewed | 1-19 | InheritedNotifier exposing tier to widgets | main.dart, glow_icon.dart | none | none |
| app/packages/digital_brain_sdk_flutter/lib/src/perf/perf_tier_thresholds.dart | flutter-sdk-and-tests | human | reviewed | 1-5 | Threshold config (dead, no consumers) | none | CLEAN-903, PROD-901 | delete or adopt in PerfProbe |
| app/packages/digital_brain_sdk_flutter/lib/src/perf/widget_census.dart | flutter-sdk-and-tests | human | reviewed | 1-46 | Element-tree DFS widget/GlowIcon counter | perf_probe; main.dart registers type | ARCH-901, PERF-901 | generalize away GlowIcon coupling |
| app/packages/digital_brain_sdk_flutter/lib/src/tier_throttle/throttle.dart | flutter-sdk-and-tests | human | reviewed | 1-23 | Tier→render tuning constants (app-specific) | glow_icon.dart, live screen | ARCH-901 | move into app design layer |
| app/packages/digital_brain_sdk_flutter/pubspec.yaml | flutter-sdk-and-tests | human | reviewed | 1-16 | Perf SDK package manifest | flutter, uuid | ARCH-902, TEST-901 | add test framework + tests |
| app/test/grpc/endpoint_test.dart | flutter-sdk-and-tests | human | reviewed | 1-38 | Endpoint resolution tests (web only) | lib/grpc/endpoint.dart | SEC-900 | synthetic hosts; add non-web cases |
| app/test/runtime/grpc_ui_transport_test.dart | flutter-sdk-and-tests | human | reviewed | 1-473 | Transport metadata/TLS/error-mapping tests | lib/runtime/grpc_ui_transport.dart | none | global isTimelineLoggingEnabled mutation note |
| app/test/runtime/runtime_configuration_test.dart | flutter-sdk-and-tests | human | reviewed | 1-82 | Endpoint + OIDC config parsing tests | lib/runtime/runtime_configuration.dart | none | none |
| app/test/runtime/runtime_controller_test.dart | flutter-sdk-and-tests | human | reviewed | 1-963 | Runtime lifecycle/race/reset/scope tests | lib/runtime/runtime.dart | none | none |
| app/test/runtime/runtime_shell_test.dart | flutter-sdk-and-tests | human | reviewed | 1-396 | Shell widget tests (errors, drafts, expiry) | lib/runtime/widgets/runtime_shell.dart | none | direct status mutation couples to internals |
| app/test/runtime/session_state_test.dart | flutter-sdk-and-tests | human | reviewed | 1-250 | Session race-hardening tests | lib/runtime/session_state.dart | none | none |
| app/test/runtime/surface_protocol_test.dart | flutter-sdk-and-tests | human | reviewed | 1-449 | Envelope decoder / OAuth target / PII-key tests | lib/runtime/protocol/surface_protocol.dart | none | none |
| app/test/runtime/surface_view_test.dart | flutter-sdk-and-tests | human | reviewed | 1-1205 | Surface render + INO interaction tests | lib/runtime/widgets/surface_view.dart | none | none |
| app/test/runtime/test_fixtures.dart | flutter-sdk-and-tests | human | reviewed | 1-221 | Shared protocol/session test builders | all runtime tests | none | none |
| app/test/runtime_test.dart | flutter-sdk-and-tests | human | reviewed | 1-246 | SessionController + FeedController unit tests | lib/runtime/runtime.dart | none | none |
| app/test/ui_kit/ui_display_a_test.dart | flutter-sdk-and-tests | human | reviewed | 1-21 | Heading/Badge smoke tests | lib/ui_kit | TEST-900 | none |
| app/test/ui_kit/ui_display_b_test.dart | flutter-sdk-and-tests | human | reviewed | 1-58 | Tile/List event + form-scope tests | lib/ui_kit | TEST-900 | none |
| app/test/ui_kit/ui_feedback_test.dart | flutter-sdk-and-tests | human | reviewed | 1-34 | Alert/Progress/Spinner/Tooltip smoke tests | lib/ui_kit | TEST-900 | none |
| app/test/ui_kit/ui_gallery_hop_render_test.dart | flutter-sdk-and-tests | human | reviewed | 1-72 | Sidebar+panels layout regression test | lib/ui_kit | none | none |
| app/test/ui_kit/ui_inputs_a_test.dart | flutter-sdk-and-tests | human | reviewed | 1-46 | Checkbox/Switch/TextArea capture tests | lib/ui_kit | TEST-900 | none |
| app/test/ui_kit/ui_inputs_b_test.dart | flutter-sdk-and-tests | human | reviewed | 1-130 | Select/Slider/Radio/DateField capture tests | lib/ui_kit | TEST-900 | slider drag geometry brittle to forui bump |
| app/test/ui_kit/ui_kit_widgets_test.dart | flutter-sdk-and-tests | human | reviewed | 1-225 | Form scope + button payload tests | lib/ui_kit | none | none |
| app/test/ui_kit/ui_layout_test.dart | flutter-sdk-and-tests | human | reviewed | 1-41 | Divider/Header/Row mapping tests | lib/ui_kit, rfw_host | TEST-900 | none |
| app/test/ui_kit/ui_nav_a_test.dart | flutter-sdk-and-tests | human | reviewed | 1-51 | Tabs/Breadcrumb/Pagination event tests | lib/ui_kit | TEST-900 | none |
| app/test/ui_kit/ui_nav_b_test.dart | flutter-sdk-and-tests | human | reviewed | 1-35 | BottomNav/Sidebar event tests | lib/ui_kit | TEST-900 | none |
| app/test/ui_kit/ui_overlays_test.dart | flutter-sdk-and-tests | human | reviewed | 1-89 | Dialog/Sheet/Toast present-once tests | lib/ui_kit | none | none |
| app/test/ui_kit/ui_registry_test.dart | flutter-sdk-and-tests | human | reviewed | 1-304 | buildUiNode mapping + renderer tests | lib/ui_kit/ui_registry.dart | none | unknown-type silent-drop noted |
| app/tool/breaker_smoke.dart | flutter-sdk-and-tests | human | reviewed | 1-25 | Manual circuit-breaker smoke script | lib/telemetry/export_circuit_breaker.dart | CLEAN-902 | convert to unit test, delete script |
| app/tool/challenger_m2_3_stress_test.dart | flutter-sdk-and-tests | human | reviewed | 1-345 | Demo perf-grep script; targets deleted files | (missing lib files) | CLEAN-901 | delete (always fails) |
| app/tool/challenger_m4_stress_test.dart | flutter-sdk-and-tests | human | reviewed | 1-255 | Demo script testing copied production logic | assets/ino-catalog.json | CLEAN-901 | delete; port cases to real tests |
| app/tool/check_ui_imports.dart | flutter-sdk-and-tests | human | reviewed | 1-73 | UI-package import boundary checker | lib/digital_brain_ui | FRAME-903 | wire into CI; prune speculative prefixes |
| app/.gitignore | flutter-sdk-and-tests | human | reviewed | 1-60 | Flutter/Windows ignore rules | git | none | none |
| app/.metadata | flutter-sdk-and-tests | generated | excluded-generated | 1-45 (scanned) | Flutter tool migrate metadata (checked in, correct) | flutter CLI | none | none |
| app/Flutter.proj | flutter-sdk-and-tests | human | reviewed | 1-101 | MSBuild coordination project for Flutter client | Brain.slnx, Aspire AddFlutterClient | REL-901 | fail on nonzero exit or delete targets |
| app/analysis_options.yaml | flutter-sdk-and-tests | human | reviewed | 1-28 | Stock flutter_lints analyzer config | dart analyzer | FRAME-902 | adopt stricter shared config |
| app/devtools_options.yaml | flutter-sdk-and-tests | generated | reviewed | 1-3 | DevTools extension settings (empty) | flutter devtools | none | none |
| app/pubspec.lock | flutter-sdk-and-tests | generated | excluded-generated | n/a (1574 lines; generator: flutter pub get) | Dependency lockfile (checked in, correct for app) | pubspec.yaml | FRAME-900 | any-constraint makes refresh nondeterministic |
| app/pubspec.yaml | flutter-sdk-and-tests | human | reviewed | 1-69 | App manifest, deps, assets | flutter tooling | FRAME-900, FRAME-901, TEST-902, CLEAN-900 | delete dead deps; fix widgetbook |

## Subsystem: platform-and-skills


| path | subsystem | authored | status | line ranges reviewed | primary responsibility | key callers/deps | finding IDs | follow-up |
|---|---|---|---|---|---|---|---|---|
| app/android/.gitignore | platform-and-skills | generated (flutter create) | reviewed | 1-14 | Ignore Gradle/keystore/local files | git | — | none |
| app/android/app/build.gradle.kts | platform-and-skills | generated (flutter create) | reviewed | 1-45 | Android app module build config | flutter gradle plugin | SEC-1000 | add real release signing pre-ship |
| app/android/app/src/main/AndroidManifest.xml | platform-and-skills | generated (flutter create) | reviewed | 1-45 | App manifest: activity, permissions | Android OS, MainActivity | PROD-1001, PROD-1002, CLEAN-1000 | add INTERNET+RECORD_AUDIO |
| app/android/app/src/main/kotlin/io/digitalbrain/app/MainActivity.kt | platform-and-skills | generated (flutter create) | reviewed | 1-5 | FlutterActivity entry point | Flutter embedding | — | none |
| app/android/app/src/main/res/drawable-v21/launch_background.xml | platform-and-skills | generated (flutter create) | reviewed | 1-13 | Splash background v21 | LaunchTheme | — | none |
| app/android/app/src/main/res/drawable/launch_background.xml | platform-and-skills | generated (flutter create) | reviewed | 1-13 | Splash background legacy | LaunchTheme | — | none |
| app/android/app/src/main/res/mipmap-hdpi/ic_launcher.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default launcher icon asset | manifest | CLEAN-1000 | rebrand icon |
| app/android/app/src/main/res/mipmap-mdpi/ic_launcher.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default launcher icon asset | manifest | CLEAN-1000 | rebrand icon |
| app/android/app/src/main/res/mipmap-xhdpi/ic_launcher.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default launcher icon asset | manifest | CLEAN-1000 | rebrand icon |
| app/android/app/src/main/res/mipmap-xxhdpi/ic_launcher.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default launcher icon asset | manifest | CLEAN-1000 | rebrand icon |
| app/android/app/src/main/res/mipmap-xxxhdpi/ic_launcher.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default launcher icon asset | manifest | CLEAN-1000 | rebrand icon |
| app/android/app/src/main/res/values-night/styles.xml | platform-and-skills | generated (flutter create) | reviewed | 1-18 | Dark launch/normal themes | manifest | — | none |
| app/android/app/src/main/res/values/styles.xml | platform-and-skills | generated (flutter create) | reviewed | 1-18 | Light launch/normal themes | manifest | — | none |
| app/android/app/src/profile/AndroidManifest.xml | platform-and-skills | generated (flutter create) | reviewed | 1-7 | Profile-build INTERNET grant | flutter tool | PROD-1001 | debug overlay missing |
| app/android/build.gradle.kts | platform-and-skills | generated (flutter create) | reviewed | 1-25 | Root Gradle repos/build-dir setup | settings.gradle.kts | — | none |
| app/android/gradle.properties | platform-and-skills | generated (flutter create) | reviewed | 1-2 | Gradle JVM args, AndroidX | gradle | — | none |
| app/android/gradle/wrapper/gradle-wrapper.properties | platform-and-skills | generated (flutter create) | reviewed | 1-5 | Gradle 8.14 wrapper pin | gradlew | — | none |
| app/android/settings.gradle.kts | platform-and-skills | generated (flutter create) | reviewed | 1-27 | Plugin management, flutter SDK include | local.properties | — | none |
| app/ios/.gitignore | platform-and-skills | generated (flutter create) | reviewed | 1-35 | Ignore Pods/ephemeral/generated | git | — | none |
| app/ios/Flutter/AppFrameworkInfo.plist | platform-and-skills | generated (flutter create) | reviewed | 1-25 | App.framework bundle metadata | xcode build | — | none |
| app/ios/Flutter/Debug.xcconfig | platform-and-skills | generated (flutter create) | reviewed | 1-1 | Include generated debug config | xcodeproj | — | none |
| app/ios/Flutter/Release.xcconfig | platform-and-skills | generated (flutter create) | reviewed | 1-1 | Include generated release config | xcodeproj | — | none |
| app/ios/Runner.xcodeproj/project.pbxproj | platform-and-skills | generated (flutter create/Xcode) | excluded-generated | grep-scanned | Xcode project definition | xcodebuild | — | none |
| app/ios/Runner.xcodeproj/project.xcworkspace/contents.xcworkspacedata | platform-and-skills | generated (Xcode) | excluded-generated | - | Workspace pointer | Xcode | — | none |
| app/ios/Runner.xcodeproj/project.xcworkspace/xcshareddata/IDEWorkspaceChecks.plist | platform-and-skills | generated (Xcode) | excluded-generated | - | IDE check marker | Xcode | — | none |
| app/ios/Runner.xcodeproj/project.xcworkspace/xcshareddata/WorkspaceSettings.xcsettings | platform-and-skills | generated (Xcode) | excluded-generated | - | Workspace settings | Xcode | — | none |
| app/ios/Runner.xcodeproj/xcshareddata/xcschemes/Runner.xcscheme | platform-and-skills | generated (flutter create) | excluded-generated | - | Build/run scheme | Xcode | — | none |
| app/ios/Runner.xcworkspace/contents.xcworkspacedata | platform-and-skills | generated (flutter create) | excluded-generated | - | Workspace container | Xcode | — | none |
| app/ios/Runner.xcworkspace/xcshareddata/IDEWorkspaceChecks.plist | platform-and-skills | generated (Xcode) | excluded-generated | - | IDE check marker | Xcode | — | none |
| app/ios/Runner.xcworkspace/xcshareddata/WorkspaceSettings.xcsettings | platform-and-skills | generated (Xcode) | excluded-generated | - | Workspace settings | Xcode | — | none |
| app/ios/Runner/AppDelegate.swift | platform-and-skills | generated (flutter create) | reviewed | 1-17 | App delegate, plugin registration | Flutter engine | — | none |
| app/ios/Runner/Assets.xcassets/AppIcon.appiconset/Contents.json | platform-and-skills | generated (flutter create) | excluded-generated | - | Icon catalog manifest | Xcode | — | none |
| app/ios/Runner/Assets.xcassets/AppIcon.appiconset/Icon-App-1024x1024@1x.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | asset catalog | CLEAN-1000 | rebrand |
| app/ios/Runner/Assets.xcassets/AppIcon.appiconset/Icon-App-20x20@1x.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | asset catalog | CLEAN-1000 | rebrand |
| app/ios/Runner/Assets.xcassets/AppIcon.appiconset/Icon-App-20x20@2x.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | asset catalog | CLEAN-1000 | rebrand |
| app/ios/Runner/Assets.xcassets/AppIcon.appiconset/Icon-App-20x20@3x.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | asset catalog | CLEAN-1000 | rebrand |
| app/ios/Runner/Assets.xcassets/AppIcon.appiconset/Icon-App-29x29@1x.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | asset catalog | CLEAN-1000 | rebrand |
| app/ios/Runner/Assets.xcassets/AppIcon.appiconset/Icon-App-29x29@2x.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | asset catalog | CLEAN-1000 | rebrand |
| app/ios/Runner/Assets.xcassets/AppIcon.appiconset/Icon-App-29x29@3x.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | asset catalog | CLEAN-1000 | rebrand |
| app/ios/Runner/Assets.xcassets/AppIcon.appiconset/Icon-App-40x40@1x.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | asset catalog | CLEAN-1000 | rebrand |
| app/ios/Runner/Assets.xcassets/AppIcon.appiconset/Icon-App-40x40@2x.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | asset catalog | CLEAN-1000 | rebrand |
| app/ios/Runner/Assets.xcassets/AppIcon.appiconset/Icon-App-40x40@3x.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | asset catalog | CLEAN-1000 | rebrand |
| app/ios/Runner/Assets.xcassets/AppIcon.appiconset/Icon-App-60x60@2x.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | asset catalog | CLEAN-1000 | rebrand |
| app/ios/Runner/Assets.xcassets/AppIcon.appiconset/Icon-App-60x60@3x.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | asset catalog | CLEAN-1000 | rebrand |
| app/ios/Runner/Assets.xcassets/AppIcon.appiconset/Icon-App-76x76@1x.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | asset catalog | CLEAN-1000 | rebrand |
| app/ios/Runner/Assets.xcassets/AppIcon.appiconset/Icon-App-76x76@2x.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | asset catalog | CLEAN-1000 | rebrand |
| app/ios/Runner/Assets.xcassets/AppIcon.appiconset/Icon-App-83.5x83.5@2x.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | asset catalog | CLEAN-1000 | rebrand |
| app/ios/Runner/Assets.xcassets/LaunchImage.imageset/Contents.json | platform-and-skills | generated (flutter create) | excluded-generated | - | Launch image manifest | LaunchScreen | — | none |
| app/ios/Runner/Assets.xcassets/LaunchImage.imageset/LaunchImage.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Placeholder launch image | LaunchScreen | — | none |
| app/ios/Runner/Assets.xcassets/LaunchImage.imageset/LaunchImage@2x.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Placeholder launch image | LaunchScreen | — | none |
| app/ios/Runner/Assets.xcassets/LaunchImage.imageset/LaunchImage@3x.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Placeholder launch image | LaunchScreen | — | none |
| app/ios/Runner/Base.lproj/LaunchScreen.storyboard | platform-and-skills | generated (flutter create) | reviewed | 1-38 | Launch screen UI | Info.plist | — | none |
| app/ios/Runner/Base.lproj/Main.storyboard | platform-and-skills | generated (flutter create) | reviewed | 1-27 | FlutterViewController scene | Info.plist | — | none |
| app/ios/Runner/Info.plist | platform-and-skills | generated (flutter create) | reviewed | 1-71 | iOS app metadata/permissions | iOS, record plugin | PROD-1002 | add NSMicrophoneUsageDescription |
| app/ios/Runner/Runner-Bridging-Header.h | platform-and-skills | generated (flutter create) | reviewed | 1-1 | ObjC bridging header | GeneratedPluginRegistrant | — | none |
| app/ios/Runner/SceneDelegate.swift | platform-and-skills | generated (flutter create) | reviewed | 1-7 | Empty FlutterSceneDelegate | AppDelegate | — | none |
| app/ios/RunnerTests/RunnerTests.swift | platform-and-skills | generated (flutter create) | reviewed | 1-13 | Placeholder XCTest | xcodebuild test | TEST-1000 | none |
| app/linux/.gitignore | platform-and-skills | generated (flutter create) | reviewed | 1-1 | Ignore ephemeral | git | — | none |
| app/linux/CMakeLists.txt | platform-and-skills | generated (flutter create) | reviewed | 1-129 | Linux project build/install rules | cmake, flutter tool | CLEAN-1000 | align APPLICATION_ID |
| app/linux/flutter/CMakeLists.txt | platform-and-skills | generated (flutter tool) | reviewed | 1-89 | Flutter engine/tool build glue | tool_backend.sh | — | none |
| app/linux/flutter/generated_plugin_registrant.cc | platform-and-skills | generated (flutter tool) | excluded-generated | glanced 1-40 | Register 7 Linux plugins | my_application.cc | — | none; consistent with pubspec |
| app/linux/flutter/generated_plugin_registrant.h | platform-and-skills | generated (flutter tool) | excluded-generated | - | Registrant header | my_application.cc | — | none |
| app/linux/flutter/generated_plugins.cmake | platform-and-skills | generated (flutter tool) | excluded-generated | glanced 1-32 | Plugin build list | CMakeLists | — | none |
| app/linux/runner/CMakeLists.txt | platform-and-skills | generated (flutter create) | reviewed | 1-27 | Runner executable target | root CMakeLists | — | none |
| app/linux/runner/main.cc | platform-and-skills | generated (flutter create) | reviewed | 1-7 | GTK app entry | my_application | — | none |
| app/linux/runner/my_application.cc | platform-and-skills | generated (flutter create) | reviewed | 1-149 | GTK window + Flutter view host | flutter_linux, GTK | CLEAN-1000 | window title |
| app/linux/runner/my_application.h | platform-and-skills | generated (flutter create) | reviewed | 1-22 | MyApplication declaration | main.cc | — | none |
| app/macos/.gitignore | platform-and-skills | generated (flutter create) | reviewed | 1-7 | Ignore ephemeral/Pods/xcuserdata | git | — | none |
| app/macos/Flutter/Flutter-Debug.xcconfig | platform-and-skills | generated (flutter create) | reviewed | 1-1 | Include generated debug config | Debug.xcconfig | — | none |
| app/macos/Flutter/Flutter-Release.xcconfig | platform-and-skills | generated (flutter create) | reviewed | 1-1 | Include generated release config | Release.xcconfig | — | none |
| app/macos/Flutter/GeneratedPluginRegistrant.swift | platform-and-skills | generated (flutter tool) | excluded-generated | glanced 1-37 | Register 13 macOS plugins | MainFlutterWindow | — | none; consistent with pubspec |
| app/macos/Runner.xcodeproj/project.pbxproj | platform-and-skills | generated (flutter create/Xcode) | excluded-generated | grep-scanned | Xcode project definition | xcodebuild | — | entitlements per-config binding verified |
| app/macos/Runner.xcodeproj/project.xcworkspace/xcshareddata/IDEWorkspaceChecks.plist | platform-and-skills | generated (Xcode) | excluded-generated | - | IDE check marker | Xcode | — | none |
| app/macos/Runner.xcodeproj/xcshareddata/xcschemes/Runner.xcscheme | platform-and-skills | generated (flutter create) | excluded-generated | - | Build/run scheme | Xcode | — | none |
| app/macos/Runner.xcworkspace/contents.xcworkspacedata | platform-and-skills | generated (flutter create) | excluded-generated | - | Workspace container | Xcode | — | none |
| app/macos/Runner.xcworkspace/xcshareddata/IDEWorkspaceChecks.plist | platform-and-skills | generated (Xcode) | excluded-generated | - | IDE check marker | Xcode | — | none |
| app/macos/Runner/AppDelegate.swift | platform-and-skills | generated (flutter create) | reviewed | 1-14 | App delegate | FlutterMacOS | — | none |
| app/macos/Runner/Assets.xcassets/AppIcon.appiconset/Contents.json | platform-and-skills | generated (flutter create) | excluded-generated | - | Icon catalog manifest | Xcode | — | none |
| app/macos/Runner/Assets.xcassets/AppIcon.appiconset/app_icon_1024.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | asset catalog | CLEAN-1000 | rebrand |
| app/macos/Runner/Assets.xcassets/AppIcon.appiconset/app_icon_128.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | asset catalog | CLEAN-1000 | rebrand |
| app/macos/Runner/Assets.xcassets/AppIcon.appiconset/app_icon_16.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | asset catalog | CLEAN-1000 | rebrand |
| app/macos/Runner/Assets.xcassets/AppIcon.appiconset/app_icon_256.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | asset catalog | CLEAN-1000 | rebrand |
| app/macos/Runner/Assets.xcassets/AppIcon.appiconset/app_icon_32.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | asset catalog | CLEAN-1000 | rebrand |
| app/macos/Runner/Assets.xcassets/AppIcon.appiconset/app_icon_512.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | asset catalog | CLEAN-1000 | rebrand |
| app/macos/Runner/Assets.xcassets/AppIcon.appiconset/app_icon_64.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | asset catalog | CLEAN-1000 | rebrand |
| app/macos/Runner/Base.lproj/MainMenu.xib | platform-and-skills | generated (flutter create) | excluded-generated | - | Main menu nib | AppDelegate | — | none |
| app/macos/Runner/Configs/AppInfo.xcconfig | platform-and-skills | generated (flutter create) | reviewed | 1-15 | Product name/bundle id/copyright | Info.plist | CLEAN-1000 | product name cosmetic |
| app/macos/Runner/Configs/Debug.xcconfig | platform-and-skills | generated (flutter create) | reviewed | 1-2 | Debug config includes | xcodeproj | — | none |
| app/macos/Runner/Configs/Release.xcconfig | platform-and-skills | generated (flutter create) | reviewed | 1-2 | Release config includes | xcodeproj | — | none |
| app/macos/Runner/Configs/Warnings.xcconfig | platform-and-skills | generated (flutter create) | reviewed | 1-13 | Compiler warning flags | xcodeproj | — | none |
| app/macos/Runner/DebugProfile.entitlements | platform-and-skills | generated (flutter create) | reviewed | 1-13 | Debug sandbox/JIT/network entitlements | codesign | PROD-1000, PROD-1002 | add network.client, audio-input |
| app/macos/Runner/Info.plist | platform-and-skills | generated (flutter create) | reviewed | 1-33 | macOS app metadata | AppKit | PROD-1002 | add NSMicrophoneUsageDescription |
| app/macos/Runner/MainFlutterWindow.swift | platform-and-skills | generated (flutter create) | reviewed | 1-16 | Host FlutterViewController window | GeneratedPluginRegistrant | — | none |
| app/macos/Runner/Release.entitlements | platform-and-skills | generated (flutter create) | reviewed | 1-9 | Release sandbox entitlements | codesign | PROD-1000, PROD-1002 | add network.client, audio-input, user-selected files |
| app/macos/RunnerTests/RunnerTests.swift | platform-and-skills | generated (flutter create) | reviewed | 1-13 | Placeholder XCTest | xcodebuild test | TEST-1000 | none |
| app/web/favicon.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Default favicon asset | index.html | CLEAN-1000 | rebrand |
| app/web/icons/Icon-192.png | platform-and-skills | generated (flutter create) | excluded-generated | - | PWA icon asset | manifest.json | CLEAN-1000 | rebrand |
| app/web/icons/Icon-512.png | platform-and-skills | generated (flutter create) | excluded-generated | - | PWA icon asset | manifest.json | CLEAN-1000 | rebrand |
| app/web/icons/Icon-maskable-192.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Maskable PWA icon asset | manifest.json | CLEAN-1000 | rebrand |
| app/web/icons/Icon-maskable-512.png | platform-and-skills | generated (flutter create) | excluded-generated | - | Maskable PWA icon asset | manifest.json | CLEAN-1000 | rebrand |
| app/web/index.html | platform-and-skills | human (on flutter template) | reviewed | 1-89 | Web shell: redirect, SEO, bootstrap | flutter_bootstrap.js, endpoint.dart | REL-1000, SEC-1001 | delete dead kernel_port gate |
| app/web/manifest.json | platform-and-skills | human (on flutter template) | reviewed | 1-36 | PWA manifest | browsers | — | none |
| app/windows/.gitignore | platform-and-skills | generated (flutter create) | reviewed | 1-17 | Ignore VS/ephemeral files | git | — | none |
| app/windows/CMakeLists.txt | platform-and-skills | generated (flutter create) | reviewed | 1-109 | Windows project build/install rules | cmake, flutter tool | — | none |
| app/windows/flutter/CMakeLists.txt | platform-and-skills | generated (flutter tool) | excluded-generated | - | Flutter engine build glue | tool_backend | — | none |
| app/windows/flutter/generated_plugin_registrant.cc | platform-and-skills | generated (flutter tool) | excluded-generated | glanced 1-33 | Register 7 Windows plugins | flutter_window.cpp | — | none; consistent with pubspec |
| app/windows/flutter/generated_plugin_registrant.h | platform-and-skills | generated (flutter tool) | excluded-generated | - | Registrant header | flutter_window.cpp | — | none |
| app/windows/flutter/generated_plugins.cmake | platform-and-skills | generated (flutter tool) | excluded-generated | glanced 1-32 | Plugin build list | CMakeLists | — | none |
| app/windows/runner/CMakeLists.txt | platform-and-skills | generated (flutter create) | reviewed | 1-41 | Runner executable target | root CMakeLists | — | none |
| app/windows/runner/Runner.rc | platform-and-skills | generated (flutter create) | reviewed | 1-122 | Icon + version resource | resource.h | — | none |
| app/windows/runner/flutter_window.cpp | platform-and-skills | generated (flutter create) | reviewed | 1-72 | Flutter view controller host window | win32_window, registrant | — | none |
| app/windows/runner/flutter_window.h | platform-and-skills | generated (flutter create) | reviewed | 1-34 | FlutterWindow declaration | main.cpp | — | none |
| app/windows/runner/main.cpp | platform-and-skills | generated (flutter create) | reviewed | 1-44 | wWinMain entry, message loop | FlutterWindow | CLEAN-1000 | window title |
| app/windows/runner/resource.h | platform-and-skills | generated (VC++) | reviewed | 1-17 | Resource IDs | Runner.rc | — | none |
| app/windows/runner/resources/app_icon.ico | platform-and-skills | generated (flutter create) | excluded-generated | - | Default app icon asset | Runner.rc | CLEAN-1000 | rebrand |
| app/windows/runner/runner.exe.manifest | platform-and-skills | generated (flutter create) | reviewed | 1-15 | DPI awareness + supportedOS manifest | linker | — | none |
| app/windows/runner/utils.cpp | platform-and-skills | generated (flutter create) | reviewed | 1-66 | Console attach, UTF conversion | main.cpp | — | none |
| app/windows/runner/utils.h | platform-and-skills | generated (flutter create) | reviewed | 1-20 | Utils declarations | main.cpp | — | none |
| app/windows/runner/win32_window.cpp | platform-and-skills | generated (flutter create) | reviewed | 1-289 | DPI-aware Win32 window base | flutter_window | — | none |
| app/windows/runner/win32_window.h | platform-and-skills | generated (flutter create) | reviewed | 1-103 | Win32Window declaration | flutter_window | — | none |
| .agents/skills/aspire/SKILL.md | platform-and-skills | vendored (Microsoft, aspire agent init) | reviewed | 1-159 | Aspire router skill for agents | agent runtimes, sub-skills | ARCH-1000 | reconcile with CLAUDE.md |
| .agents/skills/aspire/references/aspire-13-3-breaking-changes.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-110 | 13.3 breaking-change scrub list | aspire SKILL.md | — | staleness watch |
| .agents/skills/aspire-init/SKILL.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-147 | Skeleton-drop first-run skill | aspireify handoff | ARCH-1000 | inert for this repo; deletable |
| .agents/skills/aspire-init/references/init-workflow.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-124 | aspire init flow reference | aspire-init SKILL.md | ARCH-1000 | inert |
| .agents/skills/aspire-init/references/templates.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-93 | aspire new template catalog | aspire-init SKILL.md | ARCH-1000 | inert |
| .agents/skills/aspire-monitoring/SKILL.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-198 | Observability routing skill | aspire CLI | CLEAN-1001 | none |
| .agents/skills/aspire-monitoring/references/diagnostics-bridge.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-210 | Local vs deployed diagnostics routing | monitoring SKILL.md | CLEAN-1001 | none |
| .agents/skills/aspire-monitoring/references/monitoring.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-162 | Telemetry inspection patterns | monitoring SKILL.md | CLEAN-1001 | none |
| .agents/skills/aspire-monitoring/references/playwright-handoff.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-22 | Endpoint discovery for Playwright | monitoring SKILL.md | — | none |
| .agents/skills/aspire-orchestration/SKILL.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-205 | Lifecycle + safety guardrails skill | aspire CLI | ARCH-1000 | conflicts with CLAUDE.md loop |
| .agents/skills/aspire-orchestration/references/agent-workflows.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-120 | Common agent workflow patterns | orchestration SKILL.md | — | none |
| .agents/skills/aspire-orchestration/references/app-commands.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-124 | App lifecycle/bootstrap commands | orchestration SKILL.md | ARCH-1000 | none |
| .agents/skills/aspire-orchestration/references/detection.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-161 | Aspire project fingerprinting | orchestration SKILL.md | — | none |
| .agents/skills/aspire-orchestration/references/resource-management.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-39 | Resource wait/command guidance | orchestration SKILL.md | — | none |
| .agents/skills/aspire-orchestration/references/safety-guardrails.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-273 | Guardrail rationale + recovery | orchestration SKILL.md | — | none |
| .agents/skills/aspire-deployment/SKILL.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-223 | Deployment routing skill | aspire CLI, cloud CLIs | — | none |
| .agents/skills/aspire-deployment/references/aws.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-177 | AWS CDK deployment reference | deployment SKILL.md | — | none |
| .agents/skills/aspire-deployment/references/azure.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-317 | Azure targets deployment reference | deployment SKILL.md | — | none |
| .agents/skills/aspire-deployment/references/cicd.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-343 | CI/CD + GitHub Actions guidance | deployment SKILL.md | — | none |
| .agents/skills/aspire-deployment/references/docker-compose.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-156 | Compose deployment reference | deployment SKILL.md | — | none |
| .agents/skills/aspire-deployment/references/github-actions-azure-csharp.yml | platform-and-skills | vendored (Microsoft) | reviewed | 1-54 | Template Azure deploy workflow (C#) | cicd.md | — | template only, not live CI |
| .agents/skills/aspire-deployment/references/github-actions-azure-typescript.yml | platform-and-skills | vendored (Microsoft) | reviewed | 1-54 | Template Azure deploy workflow (TS) | cicd.md | — | template only, not live CI |
| .agents/skills/aspire-deployment/references/javascript.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-127 | JS app deployment models | deployment SKILL.md | — | none |
| .agents/skills/aspire-deployment/references/kubernetes.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-237 | Kubernetes/AKS deployment reference | deployment SKILL.md | — | none |
| .agents/skills/aspire-deployment/references/preflight.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-190 | Common deployment preflight checklist | deployment SKILL.md | — | none |
| .agents/skills/aspireify/SKILL.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-330 | AppHost wiring skill | aspire CLI | — | mostly inert (AppHost wired) |
| .agents/skills/aspireify/references/apphost-wiring.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-394 | AppHost wiring/API lookup patterns | aspireify SKILL.md | — | none |
| .agents/skills/aspireify/references/csharp-authoring.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-180 | C# AppHost authoring patterns | aspireify SKILL.md | — | none |
| .agents/skills/aspireify/references/docker-compose.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-215 | Compose→Aspire migration patterns | aspireify SKILL.md | — | none |
| .agents/skills/aspireify/references/full-solution-apphosts.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-333 | Large-solution AppHost triage | aspireify SKILL.md | — | none |
| .agents/skills/aspireify/references/javascript-apps.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-151 | JS resource wiring patterns | aspireify SKILL.md | — | none |
| .agents/skills/aspireify/references/opentelemetry.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-113 | Non-.NET OTel setup snippets | aspireify SKILL.md | — | none |
| .agents/skills/aspireify/references/scan-and-propose.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-122 | Repo scan heuristics + catalog | aspireify SKILL.md | — | none |
| .agents/skills/aspireify/references/service-defaults.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-115 | ServiceDefaults wiring checklist | aspireify SKILL.md | — | none |
| .agents/skills/aspireify/references/typescript-authoring.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-247 | TS AppHost authoring patterns | aspireify SKILL.md | — | none |
| .agents/skills/aspireify/references/validation.md | platform-and-skills | vendored (Microsoft) | reviewed | 1-98 | Post-wiring validation flow | aspireify SKILL.md | — | none |
