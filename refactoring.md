# DigitalBrain refactoring plan

**Branch:** `stage1-outcome-rail`  
**Date:** 2026-08-12  
**Status:** Plan only — no production code moves in this document.  
**Authority:** Keep the living system green (build 0 warnings + MCP gates). Refactor in small vertical slices.

---

## 1. Goals

1. **One type per file** — every `class` / `record` / `struct` / `enum` / `interface` (and Dart public types) owns its own file. Nested private helpers are allowed only when they are implementation details of that single type.
2. **Correct layering** — domain modules out of Core; Security owns principal/auth; product HTTP out of Kernel host; Aspire hosting matches TripRadar-grade structure.
3. **Grain state hygiene** — stop growing STJ JSON string blobs; prefer Orleans-serialized durable values / dictionaries with append-only `[Id(n)]`.
4. **Security boundary** — ban unauthenticated `VerifiedActor.Enter`; MCP must not spoof `alice|bob|operator` in production paths.
5. **Conversation state** — extract chat from UI god-class into a Conversations module with one canonical event model.
6. **Testability** — introduce module-owned test projects (no central suite revival as a monolith); grain-level and contract tests first.
7. **Keep shippable** — each PR: build `-warnaserror`, smoke MCP, no dual AppHosts, no force-kill.

---

## 2. Quality bar (TripRadar reference)

Reference tree: `E:\intochat\TripRadar\src\Aspire\Hosting`.

| TripRadar pattern | DigitalBrain today | Target |
|---|---|---|
| Per-integration folder (`TripRadar/`, `Bot/`, `Strapi/`) | Mix of fat `*HostingExtensions.cs` + thin OAuth wrappers | One folder per resource family |
| `*Extensions` + `*Options` + `*Names` + `*Resource` + `Constants/*` | Constants scattered (`DigitalBrainResourceNames`, `ProductSurfaceResources`, inline strings) | Names / Options / Constants / Extensions / Resource (or Projection) |
| Parameter secrets with descriptions | Operator defaults + some parameters | Align secret UX with TripRadar parameter pattern |
| Thin AppHost composition | Thin AppHost **but** dual module catalog vs Kernel `ComposedModules` | **Single catalog** generating both |
| Domain / Application / Infrastructure split | Orleans neurons blur domain+infra | Core = interconnect only; Modules = product domains |

**Do not copy TripRadar’s architecture** (Kafka, Hangfire, GraphQL). Copy **hosting hygiene and file discipline**.

---

## 3. Dependency target (after cleanup)

```
Flutter shell → Flutter core → Kernel HTTP (auth cookie)
Flutter kit (standalone widgets)

DigitalBrain.Client → Abstractions
Modules.* → Core → Abstractions
Modules.* → Security (auth helpers) where needed
Sdk/Mcp (rename Modules.Sdk) → Core, Security, Abstractions
Kernel host → composes modules + maps HTTP
AppHost → Aspire.Hosting.* only
```

**Core should retain:** Neuron base, journal/outbox, session, synapse graph, relay, delivery memory, filters, serialization, capability index fabric.  
**Core should lose:** Behavior, Cell, Library, Corpus, Repository, KindRegistry, InstanceRegistry, Grants, Workspace (product domain).

---

## 4. Critical findings (codegraph + audits)

### 4.1 Multi-type files

**~146 source files** violate one-type-per-file (see Appendix A). Worst offenders:

| Path | ~Types | Notes |
|---|---:|---|
| `Abstractions/Synapses/RegistryCommands.cs` | 13 | Split every synapse |
| `Abstractions/Synapses/LibraryCommands.cs` | 12 | Same |
| `Kernel/HttpSurfaceModels.cs` | 10 | DTOs → UI/Http package, one each |
| `Sdk/Mcp/IMcpServer.cs` | 10 | Interface + contracts split |
| `Mcp/ToolModels.cs` | 9 | One DTO/file |
| `Execution/Contracts/AttemptFacts.cs` | 8 | One fact/file |
| `Execution/WorkerDispatchSynapses.cs` | 7 | **Also misplaced in impl** |
| `GrantCommands.cs` / `WorkspaceMembership.cs` | 7–8 | Split |
| `CellNeuron.cs` | 4 | Neuron / state / ICellKind / CalculatorKind |
| `Chat.cs` | 1 top-level + nested state | God file (~1062 lines) — extract collaborators |

### 4.2 Misplacement hotspots

| Current | Suggested | Why |
|---|---|---|
| Core `BehaviorNeuron`, `CellNeuron`, `LibraryNeuron`, `CorpusNeuron`, `RepositoryNeuron` | `Modules/{Behavior,Cell,Library,Corpus,Repository}` | Product domains, not interconnect |
| Core `InstanceRegistryNeuron`, `KindRegistryNeuron` | `Modules/Registry` | Product catalog |
| Core `GrantsNeuron`, `WorkspaceNeuron`, `VerifiedActor` | `Security` or `Modules/Identity` | Authorization surface |
| Kernel `Auth/*` | `DigitalBrain.Security` (+ host adapters) | Security project is only crypto today |
| Kernel `MapChatStreams`, `MapOwnerCommands`, `MapShellStreams` | `Modules/UI.Http` or Conversations host | Product HTTP |
| Kernel `DigitalBrainComposition` vs AppHost `AddModule` | Single catalog | Dual lists will drift |
| Sdk folder vs assembly `DigitalBrain.Modules.Sdk` | Rename/split contracts vs rail | Naming confusion |
| UI → Execution **implementation** reference | Execution.Contracts only | Boundary break (worker dispatch synapses) |
| Chat domain under UI | `Modules/Conversations` | Stage-2 architecture |
| MCP `principalKey` spoof | Real Identity / loopback only | Critical security |
| Flutter `shell/lib/kit/*` | Rename `Shell*` demos | Collides with real kit |
| Flutter dual theme | Kit owns tokens | Duplicate palettes |

### 4.3 Grain state management

| Pattern | Grains | Risk | Target |
|---|---|---|---|
| `IDurableValue<string>` STJ JSON | Grants, Registry, KindRegistry, Library, Behavior, Corpus, Repository | Schema drift, full rewrite, no Orleans versioning | `Serializer<T>` + `IDurableValue<byte[]>` or durable dicts |
| Typed Orleans serialize | Workspace, Cell, Graph list, Webhook, MCP pending | Good | Keep as template |
| Journal window 512/512KB | All Neuron feeds | Not full history | Corpus is projection; document limits |
| Delivery username `"_delivery"` | TurnCoordinator re-entry | Audit/display loss | Carry username on delivery or drop username from hop |

### 4.4 Security

| Severity | Issue | Action |
|---|---|---|
| **Critical** | MCP tools Enter VerifiedActor with hardcoded alice/bob/operator | Gate MCP behind auth or restrict to loopback bootstrap owner only |
| **High** | Kernel product HTTP rarely Enter ambient VerifiedActor; graph falls back to owner-wide | Enter actor from HttpActor on every product map |
| **High** | Broad `[ClientEntryPoint]` on domain grains | Review allow-list; session-only for dangerous verbs |
| **Medium** | Loopback dev auto-owner | Keep Development+loopback only; never expose |
| **Medium** | BehaviorClient has no cookies | Share CookieHttpClient + ensureSession |
| **Low** | Weak password policy | Align with product threat model later |

### 4.5 Conversation state (Chat)

Triple durable model today:

1. `chat.transcript` — UI/LLM turns  
2. `chat.turn-log` — workflow records  
3. `chat.turn-queue` — FIFO head  

**Problems:** god-class `Chat.cs`, `SendStreaming` stub, 15s waiting policy local to Chat, goal/result types split across UI + Execution.

**Target:** Conversations module; one event log; projections for transcript vs workflow; Execution remains attempt authority.

---

## 5. Plan of action (ordered waves)

Each wave is a PR series. **Do not start wave N+1 until build + MCP smoke green.**

### Wave R0 — Guardrails (1–2 days)

- [ ] Adopt **one-type-per-file** as a repo rule (analyzer or PR checklist).
- [ ] Document dual module catalog as bug; no new entries in only one place.
- [ ] MCP: log warning when `principalKey` != authenticated principal; plan removal.
- [ ] Add `Directory.Build.props` note / editorconfig for file-scoped types if desired.

### Wave R1 — One-type-per-file mechanical split (low risk)

Split without moving assemblies:

1. Abstractions `*Commands.cs` bags (Registry, Library, Grant, Workspace, Behavior, Corpus, …)  
2. Sdk `IMcpServer.cs`, `McpAuthorizationVocabulary.cs`  
3. Kernel `HttpSurfaceModels.cs`, `MapAuth` request DTOs  
4. MCP `ToolModels.cs`  
5. Execution contracts bag-files  
6. Time ScheduleCommands (commands vs snapshot vs enums)  
7. Core multi-types in place (CellNeuron kinds → files still under Core until R3)

**Exit:** 0 multi-type product files (nested private OK).

### Wave R2 — Security package expansion

- Move `Kernel/Auth/*` → `DigitalBrain.Security` (or `DigitalBrain.Host.Identity`).
- Move `VerifiedActor`, `PrincipalGraph/Registry/Grants` helpers into Security.
- Kernel host only: middleware registration + maps.
- MCP: either require cookie session or force loopback bootstrap owner; remove free `principalKey` in non-dev.

### Wave R3 — Extract domain modules from Core

Create (Contracts + Impl) for each:

| Module | Types to move |
|---|---|
| Cell | ICell, CellCommands, CellNeuron, CalculatorKind |
| Library | ILibrary, LibraryCommands, LibraryNeuron |
| Corpus | ICorpus, CorpusCommands, CorpusNeuron |
| Repository | IRepository, RepositoryCommands, RepositoryNeuron |
| Behavior | IBehavior, BehaviorCommands, BehaviorNeuron |
| Registry | IRegistry, KindRegistry, InstanceRegistry, commands |
| Identity/Grants | IGrants, IWorkspace, neurons |

Update `ComposedModules` **and** AppHost together.

### Wave R4 — Conversations extraction

- New module: contracts (`IChat`, turn synapses) + impl (`Chat`, `ChatTurnWorker`).
- Move worker dispatch synapses to Execution.Contracts; drop UI→Execution.impl reference.
- Split `Chat.cs` into transcript / queue / execution-bridge collaborators.
- Fix or remove `SendStreaming`.
- Kernel maps → Conversations.Http or UI.Http.

### Wave R5 — Aspire hosting restructure (TripRadar-style)

```
src/Kernel/Aspire/DigitalBrain.Aspire.Hosting/
  Brain/
    DigitalBrainExtensions.cs
    DigitalBrainOptions.cs
    DigitalBrainNames.cs
    DigitalBrainResource.cs (if needed)
    Constants/
  OAuth/
    OAuthProviderHosting.cs
    OAuthOptions.cs
Modules/AI/Aspire.Hosting/
  AIExtensions.cs, AIOptions.cs, AINames.cs, Constants/
… same for Memory, UI, Google, Salesforce
```

Unify module catalog → single C# source generating AppHost modules + Kernel assembly list.

### Wave R6 — Grain state modernization

For each JSON-blob neuron: introduce Orleans-serializable state type with append-only Ids; migrate with dual-read fallback if needed (or accept dev-reset only on stage branch).

Priority order: Registry → Grants → Library → Corpus → Behavior → KindRegistry → Repository.

### Wave R7 — Flutter structure

1. Split `ui_models.dart` / `behavior_models.dart` by domain.  
2. Extract workspace session controller from `brain_workspace.dart`.  
3. Kit owns design tokens; shell consumes.  
4. Rename shell `kit_*` demos.  
5. Shared authenticated HTTP for BehaviorClient.  
6. Behavior host remains owner-gated product work (not silent fixtures forever).

### Wave R8 — Testing framework (module-owned)

See §6. Land skeleton projects without restoring a central suite.

---

## 6. Testing framework proposal

Owner amendment: **no central test project revival**. Use **module-owned** tests.

| Layer | Project | What it proves |
|---|---|---|
| Contracts | `Modules/*/Contracts.Tests` | Serializer round-trip, alias stability, Id append-only |
| Grain (in-memory Orleans) | `Modules/*/Tests` | Neuron handlers with TestCluster / silo fixture |
| Host auth | `Kernel/DigitalBrain.Security.Tests` | Cookie, loopback, principal claims |
| MCP tools | `Kernel/DigitalBrain.Mcp.Tests` | Tool JSON binding; no principal spoof in “strict” mode |
| Flutter | existing package tests | Keep `flutter test` per package when re-enabled for lib only |
| Smoke | `scripts/smoke.ps1` or Scripting probes | MCP gates already used in waves 0–8 |

**Recommended stack (already in ecosystem):**

- xUnit + Microsoft.Orleans.TestingHost (if package policy allows — owner must approve new NuGet; else Scripting-based smoke only)
- Prefer **deterministic grain tests** over full AppHost for PR CI
- Keep Aspire live MCP as pre-merge human/agent gate

**First tests to write (highest ROI):**

1. `PrincipalPartition` parse/own  
2. Schedule catch-up math (CollapsedPeriods=4) without silo  
3. Library content hash immutability  
4. Grants refuse without VerifiedActor  
5. Registry install disabled-by-default  
6. Chat queue FIFO invariant (extracted helper)

---

## 7. Conversation / chat cleanup checklist

| Item | Action |
|---|---|
| `Chat.cs` god file | Split collaborators; keep single grain type |
| Triple durable stores | Design one event log + projections |
| `SendStreaming` | Implement or remove from IChat |
| Waiting 15s | Own in Execution policy |
| ChatTurnGoal in UI impl | Move to contracts |
| ChatTurnResult/Failure in Execution.Contracts | Move to Conversations.Contracts |
| UI → Execution.impl | End via dispatch contract lift |
| AuthorizationRequired → principal chat | Already partial; audit all paths |
| Flutter BrainWorkspace hub | WorkspaceSession controller |

---

## 8. Security checklist

| Item | Action |
|---|---|
| MCP principalKey | Dev-only; production uses cookie/bootstrap |
| VerifiedActor.Enter | Only after HttpActor / MCP session |
| Product HTTP maps | Enter VerifiedActor from claims |
| ClientEntryPoint surface | Audit list; shrink |
| Security project | Absorb Auth + ambient principal + grants |
| Payload protector | Stay in Security; MCP tokens keep protect |
| Delivery principal | Consider username on SynapseDelivery if needed |

---

## 9. Success metrics

| Metric | Today (approx) | Target |
|---|---|---|
| Multi-type product files | ~146 | 0 |
| Domain neurons in Core | 8+ | 0 |
| Dual module catalogs | 2 | 1 |
| JSON-string durable grains | 7 | 0 |
| UI→Execution.impl reference | yes | no |
| Module-owned test projects | 0 (deferred) | ≥1 skeleton + critical unit tests |
| Build | 0 warnings | keep |
| MCP smoke waves 0–8 | green | keep |

---

## 10. Explicit non-goals (this cleanup)

- Rewriting Orleans interconnect (journal-is-outbox stays).  
- Replacing Flutter with another UI stack.  
- Full Behavior Studio host (needs design session).  
- Central monolith test suite.  
- Force-kill / Azurite wipe as “fixes”.

---

## 11. Suggested first PR (start here)

**Title:** `refactor: one-type-per-file for Registry + Grant synapses`

- Split `RegistryCommands.cs` and `GrantCommands.cs` only.  
- No behavior change.  
- Build + no AppHost required.  

Then **Security MCP principal gate** as second PR.

---

## Appendix B — Multi-type files (priority split list)

Generated count: files with Types > 1 in product source (lib/cs, excluding tests/bin).

| Path | Types | Names |
|---|---:|---|
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/third_party/jni_bindings_generated.dart` | 72 | JniBindings, CallbackResult, ConditionVariable, Dart_FinalizableHandle, Dart_FinalizableHandle_, Glo |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/core_bindings.dart` | 48 | type, JBoolean, type, JByte, type, JCharacter, type, type, type, JCharacter, type, JDouble, type, JF |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/primitive_jarrays.dart` | 32 | _, type, _JBooleanArrayListView, JBooleanArrayToList, _, type, _JByteArrayListView, JByteArrayToList |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/path_provider_windows/lib/src/win32_wrappers.dart` | 29 | BOOL, BYTE, DWORD, UINT, HANDLE, HMODULE, HRESULT, LPCVOID, LPCWSTR, LPDWORD, LPWSTR, LPVOID, PUINT, |
| `src/Modules/UI/Flutter/core/lib/src/ui_models.dart` | 19 | OpenSceneRequest, ActivateControlRequest, SceneOpenedEvent, SendMessageRequest, ChatDelta, ChatDelta |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/errors.dart` | 15 | _ExplainsRelease, UseAfterReleaseError, JNullError, NoSuchMethodError, DoubleReleaseError, JniGeneri |
| `src/Modules/UI/Flutter/shell/lib/windowing/windowing_screen.dart` | 14 | WindowingScreen, _WindowingScreenState, _CanvasBackdrop, _GridPainter, _WindowingToolbar, _PanelFram |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/RegistryCommands.cs` | 13 | RegisterInstance, InstanceRegistered, RetireInstance, InstanceRetired, SetInstanceEnabled, InstanceE |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/LibraryCommands.cs` | 12 | PublishLibraryArtifact, LibraryArtifactPublished, DiscoverLibrary, LibraryDiscoveries, InstallLibrar |
| `src/Kernel/DigitalBrain.Kernel/HttpSurfaceModels.cs` | 10 | OwnerCommandRequest, ChatTurnEvent, SurfaceOpenedEvent, AuthorizationEvent, BrainTopologySnapshot, B |
| `src/Kernel/DigitalBrain.Sdk/Mcp/IMcpServer.cs` | 10 | IMcp, ListMcpTools, McpToolsListed, McpToolDescription, CallMcpTool, McpToolReturned, ListMcpServers |
| `src/Modules/UI/Flutter/core/lib/src/behavior_models.dart` | 10 | BehaviorLibraryItem, BehaviorLibraryDocument, BehaviorScenario, BehaviorBinding, BehaviorRevision, B |
| `src/Kernel/DigitalBrain.Mcp/ToolModels.cs` | 9 | NeuronJournalPage, JournaledSynapse, ActiveNeuron, ChatTranscriptPage, ChatTranscriptTurn, ChatMessa |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/jprimitives.dart` | 9 | jbyteType, jbooleanType, jcharType, jshortType, jintType, jlongType, jfloatType, jdoubleType, jvoidT |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/GrantCommands.cs` | 8 | GrantKind, GrantAccess, AccessGranted, RevokeAccess, AccessRevoked, ListGrants, GrantsListed, GrantR |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/WorkspaceMembership.cs` | 8 | AddMember, MemberAdded, ChangeRole, RoleChanged, RemoveMember, MemberRemoved, ReadMembership, Member |
| `src/Modules/Execution/Contracts/AttemptFacts.cs` | 8 | AttemptFact, AttemptAccepted, AttemptProgressed, AttemptWaiting, AttemptSucceeded, AttemptFailed, At |
| `src/Modules/UI/Flutter/shell/lib/chat/chat_contracts.dart` | 8 | SendMessage, StreamMessage, LoadTopology, OpenUrl, ActivateChatButton, LoadBehaviors, OpenBehavior,  |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/BehaviorCommands.cs` | 7 | StartRepoReview, BehaviorRunStarted, ReadBehaviorRun, BehaviorRunSnapshot, BehaviorRunSummary, FileS |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/CorpusCommands.cs` | 7 | AppendCorpusEntry, CorpusAppended, ReadCorpus, CorpusPage, ReadEpisode, EpisodePage, CorpusEntry |
| `src/Kernel/DigitalBrain.Sdk/Mcp/McpAuthorizationVocabulary.cs` | 7 | BeginMcpAuthorization, BindMcpAuthorizationCompletionTarget, DeliverMcpAuthorizationCallback, McpAut |
| `src/Modules/Execution/Contracts/ExecutionBlockers.cs` | 7 | ExecutionBlocker, InputRequired, ApprovalRequired, DependencyPending, RetryScheduled, OutcomeUncerta |
| `src/Modules/Execution/Contracts/ExecutionCommands.cs` | 7 | ExecutionPolicy, ExecutionApplyCommand, StartExecution, CancelExecution, OperationResolution, Resolv |
| `src/Modules/Execution/Contracts/ExecutionOperationContracts.cs` | 7 | OperationPhase, OperationEdge, OperationSnapshot, PrepareOperation, TransitionOperation, ReadOperati |
| `src/Modules/Execution/Execution/WorkerDispatchSynapses.cs` | 7 | WorkerDispatchRelay, RelayWorkerAccept, RelayWorkerContinue, RelayWorkerCancel, DispatchWorkerAccept |
| `src/Modules/UI/Flutter/shell/lib/brain/brain_inspector.dart` | 7 | TopologyExplorer, SelectionDetails, PulseDetails, ConnectionDetails, NeuronDetails, ModuleDetails, T |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/RepositoryCommands.cs` | 6 | OpenRepository, RepositoryOpened, ListRepositoryFiles, RepositoryFilesListed, ReadRepositoryFile, Re |
| `src/Modules/Execution/Contracts/UserActionRequired.cs` | 6 | UserActionRequired, CompleteUserAction, DenyUserAction, UserActionDenied, UserActionParkReady, Opera |
| `src/Modules/Time/Contracts/ScheduleCommands.cs` | 6 | ArmSchedule, CancelSchedule, ForceScheduleCatchUp, ScheduleSnapshot, ScheduleStatus, ScheduleResolut |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/jclass.dart` | 6 | JClass, type, type, JInstanceMethodId, type, type |
| `src/Kernel/Aspire/DigitalBrain.Aspire.Hosting/OAuthProviderHosting.cs` | 5 | OAuthProviderHostingDefinition, OAuthProviderHosting, OAuthApplicationParameters, OAuthBrainProjecti |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/KindCommands.cs` | 5 | InstallKind, KindInstalled, ListKinds, KindsListed, KindRecord |
| `src/Modules/UI/Flutter/kit/lib/src/models/kit_part.dart` | 5 | KitTimerPart, KitButtonPart, KitChartPoint, KitChartPart, KitCardPart |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/jarray.dart` | 5 | _, type, _JArrayListView, JArrayToList, on |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/jvalues.dart` | 5 | JValueInt, JValueShort, JValueByte, JValueFloat, JValueChar |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/util/jmap.dart` | 5 | JMapToAdapter, _JMapAdapter, _JMapKeySetAdapter, _JMapValueCollectionsAdapter, ToJavaMap |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/CellCommands.cs` | 4 | CellApply, CellReset, CellSnapshot, Datum |
| `src/Kernel/DigitalBrain.Core/Capabilities/CapabilityIndex.cs` | 4 | CapabilityHit, CapabilityIndex, Entry, ContractSignature |
| `src/Kernel/DigitalBrain.Core/Neuron/CellNeuron.cs` | 4 | CellNeuron, CellState, ICellKind, CalculatorKind |
| `src/Kernel/DigitalBrain.Core/Neuron/Neuron.cs` | 4 | Neuron, ClientEntryCorrelationScope, struct, struct |
| `src/Kernel/DigitalBrain.Kernel/Auth/MapAuth.cs` | 4 | AuthHttpMaps, AuthCredentialsRequest, AuthCreateUserRequest, AuthMeResponse |
| `src/Kernel/DigitalBrain.Mcp/RegistryTools.cs` | 4 | RegistryTools, BundleMemberDto, RegistryEntry, BundleInstallResult |
| `src/Kernel/DigitalBrain.Sdk/Mcp/McpAuthorizationNeuron.cs` | 4 | McpAuthorizationNeuron, PendingAuthorization, PendingAuthorizationOutcome, CommandAuthorizationRecor |
| `src/Kernel/DigitalBrain.Sdk/Webhook/WebhookFacts.cs` | 4 | VerifiedWebhookDeliveryReceived, WebhookDeliveryAccepted, WebhookDeliveryDuplicate, WebhookDeliveryC |
| `src/Modules/Execution/Execution/PendingWorkerDispatch.cs` | 4 | PendingWorkerDispatch, AcceptWorkerDispatch, ContinueWorkerDispatch, CancelWorkerDispatch |
| `src/Modules/Introspection/Contracts/IntrospectionRequests.cs` | 4 | IntrospectionIdentity, TallyJournalRequest, ReadJournalRequest, ReadTopologyRequest |
| `src/Modules/Introspection/Contracts/TopologyRead.cs` | 4 | TopologyNeuron, TopologyConnection, TopologyBroadcastRoute, TopologyRead |
| `src/Modules/Time/Contracts/ScheduleFacts.cs` | 4 | ScheduleDue, ScheduleTick, ScheduleArmed, ScheduleCancelled |
| `src/Modules/UI/DigitalBrain.Modules.UI/Chat/Chat.cs` | 4 | Chat, OwnerCommand, DurableTurnRecord, TurnQueueState |
| `src/Modules/UI/Flutter/kit/lib/src/components/clock/kit_clock.dart` | 4 | KitClock, _KitClockState, _CountdownRingPainter, _WallClockPainter |
| `src/Modules/UI/Flutter/kit/lib/src/components/graph/graph_models.dart` | 4 | GraphNodeKind, GraphNode, GraphEdge, GraphPulse |
| `src/Modules/UI/Flutter/shell/lib/activity_screen.dart` | 4 | ActivityScreen, _ActivityHeader, _EmptyActivity, _ActivityEntry |
| `src/Modules/UI/Flutter/shell/lib/behaviors/behavior_library.dart` | 4 | BehaviorLibraryView, _LibraryCard, _Pill, _MessageCard |
| `src/Modules/UI/Flutter/shell/lib/behaviors/behavior_overview.dart` | 4 | BehaviorOverviewView, _BindingRow, _Section, _MetaChip |
| `src/Modules/UI/Flutter/shell/lib/behaviors/behavior_source.dart` | 4 | BehaviorSourceView, _BehaviorSourceViewState, _EditorPane, _Section |
| `src/Modules/UI/Flutter/shell/lib/brain/topology_selection.dart` | 4 | BrainModuleSelection, BrainNeuronSelection, BrainPulseSelection, BrainConnectionSelection |
| `src/Modules/UI/Flutter/shell/lib/chat/brain_chat_screen.dart` | 4 | BrainChatScreen, _BrainChatScreenState, SignInCardRail, SignInCard |
| `src/Modules/UI/Flutter/shell/lib/chat/workspace_chrome.dart` | 4 | WorkspaceRail, WorkspaceNavigationBar, BrainMark, WorkspaceStatusBar |
| `src/Modules/UI/Flutter/shell/lib/kit/kit_chart.dart` | 4 | KitBarChart, KitLineChart, KitTimeChart, KitChartCard |
| `src/Modules/UI/Flutter/shell/lib/windowing/panel_manager.dart` | 4 | WindowPanelState, WindowPanel, WindowPanelKind, PanelManager |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/example/lib/main.dart` | 4 | Example, MyApp, ExampleCard, _ExampleCardState |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/accessors.dart` | 4 | JniResultMethods, JniIdLookupResultMethods, JniClassLookupResultMethods, JThrowableCheckMethod |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/jni.dart` | 4 | ProtectedJniExtensions, InternalJniExtension, AdditionalEnvMethods, StringMethodsForJni |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/jobject.dart` | 4 | CastError, JObject, JThrowable, JObjectUseExtension |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/jreference.dart` | 4 | ProtectedJReference, _JFinalizable, JGlobalReference, _JNullReference |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/lang/jnumber.dart` | 4 | JNumberExtension, IntToJava, DoubleToJava, BoolToJava |
| `src/Kernel/Aspire/DigitalBrain.ServiceDefaults/ServiceDefaultsExtensions.cs` | 3 | ServiceDefaultsExtensions, SuppressAzureStorageSampler, SuppressAzureStorageActivityProcessor |
| `src/Kernel/DigitalBrain.Abstractions/Capabilities/CapabilityManifest.cs` | 3 | CapabilityManifest, NeuronCapabilityDescriptor, SynapseCapabilityDescriptor |
| `src/Kernel/DigitalBrain.Core/BroadcastCatalog.cs` | 3 | BroadcastRoute, BroadcastTopology, BroadcastCatalog |
| `src/Kernel/DigitalBrain.Core/Identity/PrincipalGraph.cs` | 3 | PrincipalGraph, PrincipalRegistry, PrincipalGrants |
| `src/Kernel/DigitalBrain.Core/Neuron/BehaviorNeuron.cs` | 3 | BehaviorNeuron, BehaviorState, StoredRun |
| `src/Kernel/DigitalBrain.Core/SynapseTransform.cs` | 3 | ISynapseTransform, DeclarativeSynapseTransform, SynapseTypeIndex |
| `src/Kernel/DigitalBrain.Kernel/Auth/PrincipalScoped.cs` | 3 | PrincipalScoped, PrincipalChat, PrincipalSurface |
| `src/Kernel/DigitalBrain.Sdk/Mcp/AuthorizationFacts.cs` | 3 | AuthorizationRequired, AuthorizationCompleted, AuthorizationDenied |
| `src/Kernel/DigitalBrain.Sdk/Mcp/McpAuthorizationRail.cs` | 3 | McpAuthorizationRail, IMcpTokenExchanger, IMcpTokenRefresher |
| `src/Modules/AI/AI/Orchestration/OrchestrationDefinition.cs` | 3 | OrchestrationParticipant, OrchestrationDefinition, FingerprintSource |
| `src/Modules/Execution/Execution/WorkerGrainTypeRegistry.cs` | 3 | IWorkerTypeRegistration, WorkerTypeRegistration, WorkerGrainTypeRegistry |
| `src/Modules/Memory/Contracts/SearchVectorMemory.cs` | 3 | SearchVectorMemory, VectorMemoryMatches, VectorMemoryMatch |
| `src/Modules/Time/Contracts/TimerFacts.cs` | 3 | TimerScheduled, TimerElapsed, TimerCancelled |
| `src/Modules/Time/Contracts/TimerSnapshot.cs` | 3 | TimerStatus, TimerResolution, TimerSnapshot |
| `src/Modules/UI/DigitalBrain.Modules.UI.Aspire.Hosting/FlutterHostLaunch.cs` | 3 | FlutterHostKind, FlutterHostLaunch, Result |
| `src/Modules/UI/Flutter/shell/lib/behaviors/behavior_workspace.dart` | 3 | BehaviorWorkspace, _BehaviorWorkspaceState, _DetailChrome |
| `src/Modules/UI/Flutter/shell/lib/brain/brain_panel.dart` | 3 | BrainMetricCard, BrainConnectionNotice, BrainInspectorField |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/util/jlist.dart` | 3 | JListToAdapter, _JListAdapter, ToJavaList |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/util/jset.dart` | 3 | JSetToAdapter, _JSetAdapter, ToJavaSet |
| `src/Kernel/Aspire/DigitalBrain.Aspire.Hosting/DigitalBrainBuilder.cs` | 2 | DigitalBrainBuilder, StateProtectionKeyParameterDefault |
| `src/Kernel/Aspire/DigitalBrain.Aspire.Hosting/OperatorParameterDefaults.cs` | 2 | ConstantParameterDefault, OperatorSuppliedParameterDefault |
| `src/Kernel/DigitalBrain.Abstractions/Integrations/Integration.cs` | 2 | IntegrationScope, Integration |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/Connect.cs` | 2 | Connect, Connected |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/Disconnect.cs` | 2 | Disconnect, Disconnected |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/RouteOutcome.cs` | 2 | RouteOutcomeKind, RouteOutcome |
| `src/Kernel/DigitalBrain.Core/GrainCallerContext.cs` | 2 | GrainCallerContext, CallerScope |
| `src/Kernel/DigitalBrain.Core/Identity/VerifiedActor.cs` | 2 | VerifiedActor, Restore |
| `src/Kernel/DigitalBrain.Core/Neuron/ConnectionRelayNeuron.cs` | 2 | ConnectionRelay, ConnectionRelayNeuron |
| `src/Kernel/DigitalBrain.Core/Neuron/CorpusNeuron.cs` | 2 | CorpusNeuron, CorpusState |
| `src/Kernel/DigitalBrain.Core/Neuron/LibraryNeuron.cs` | 2 | LibraryNeuron, LibraryState |
| `src/Kernel/DigitalBrain.Core/Neuron/NeuronJournal.cs` | 2 | NeuronJournal, Watcher |
| `src/Kernel/DigitalBrain.Core/Neuron/RepositoryNeuron.cs` | 2 | RepositoryNeuron, RepoState |
| `src/Kernel/DigitalBrain.Core/Neuron/WorkspaceNeuron.cs` | 2 | WorkspaceState, WorkspaceNeuron |
| `src/Kernel/DigitalBrain.Kernel/Auth/AuthHostingExtensions.cs` | 2 | AuthHostingExtensions, LoopbackDevAuthOptions |
| `src/Kernel/DigitalBrain.Kernel/Auth/WorkspaceMembershipGateway.cs` | 2 | IWorkspaceMembershipGateway, WorkspaceMembershipGateway |
| `src/Kernel/DigitalBrain.Kernel/DigitalBrainComposition.cs` | 2 | ComposedModules, DigitalBrainHost |
| `src/Kernel/DigitalBrain.Sdk/Mcp/McpAuthorizationCodeHub.cs` | 2 | McpAuthorizationCodeHub, CodeHubOutcome |
| `src/Kernel/DigitalBrain.Sdk/Webhook/WebhookIngressNeuron.cs` | 2 | WebhookIngressNeuron, WebhookIngressState |
| `src/Kernel/DigitalBrain.Security/DurablePayloadProtectionHosting.cs` | 2 | DurablePayloadProtectionHosting, DurablePayloadProtector |
| `src/Modules/AI/AI/Orchestration/DirectAgentSession.cs` | 2 | DirectAgentSession, DirectAgentSessionEnvelope |
| `src/Modules/AI/AI/Orchestration/DirectOrchestrationShape.cs` | 2 | DirectOrchestrationIdentity, DirectOrchestrationShape |
| `src/Modules/AI/AI/Orchestration/Participant.cs` | 2 | Participant, Participant |
| `src/Modules/AI/Aspire.Hosting/AIHostingExtensions.cs` | 2 | AIHostingExtensions, AIHostingState |
| `src/Modules/Execution/Contracts/Failure.cs` | 2 | Failure, ChatTurnFailure |
| `src/Modules/Execution/Contracts/IUserActionCustody.cs` | 2 | IssuedUserAction, IUserActionCustody |
| `src/Modules/Execution/Contracts/Result.cs` | 2 | Result, ChatTurnResult |
| `src/Modules/Execution/Execution/ExecutionRuntime.cs` | 2 | ExecutionRuntime, ExecutionReminders |
| `src/Modules/Introspection/Introspection/OwnerNeuronInventory.cs` | 2 | OwnerNeuronInventory, ActivatedNeuron |
| `src/Modules/Memory/Aspire.Hosting/MemoryHostingExtensions.cs` | 2 | MemoryHostingExtensions, MemoryHostingState |
| `src/Modules/Memory/Contracts/RemoveVectorMemory.cs` | 2 | RemoveVectorMemory, VectorMemoryRemoved |
| `src/Modules/Memory/Contracts/StoreVectorMemory.cs` | 2 | StoreVectorMemory, VectorMemoryStored |
| `src/Modules/Memory/Memory/IVectorMemoryStore.cs` | 2 | IVectorMemoryStore, VectorMemoryEntry |
| `src/Modules/Memory/Memory/Qdrant/QdrantVectorMemoryProvider.cs` | 2 | QdrantVectorMemoryProvider, QdrantVectorMemoryHit |
| `src/Modules/Time/Contracts/TimerCommands.cs` | 2 | StartTimer, CancelTimer |
| `src/Modules/Time/Time/ScheduleNeuron.cs` | 2 | ScheduleState, ScheduleNeuron |
| `src/Modules/Time/Time/TimerNeuron.cs` | 2 | TimerState, TimerNeuron |
| `src/Modules/UI/DigitalBrain.Modules.UI.Aspire.Hosting/ShellHostingExtensions.cs` | 2 | ShellHostingExtensions, ShellHostingState |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/Synapses/ChatChartOffer.cs` | 2 | ChatChartPoint, ChatChartOffer |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/Synapses/ReadTranscriptRequest.cs` | 2 | ChatIdentity, ReadTranscriptRequest |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Diagram/IDiagram.cs` | 2 | DiagramRead, IDiagram |
| `src/Modules/UI/Flutter/core/lib/src/shell_surface.dart` | 2 | SceneViewModel, ShellSurfaceController |
| `src/Modules/UI/Flutter/core/lib/src/ui_client.dart` | 2 | DigitalBrainUiClient, AuthMe |
| `src/Modules/UI/Flutter/kit/lib/src/components/graph/graph_geometry.dart` | 2 | ProjectedGraphNode, ProjectedGraphEdge |
| `src/Modules/UI/Flutter/kit/lib/src/components/graph/kit_graph.dart` | 2 | KitGraph, _KitGraphState |
| `src/Modules/UI/Flutter/kit/lib/src/components/view/kit_view.dart` | 2 | KitView, _CalculatorPad |
| `src/Modules/UI/Flutter/shell/lib/behaviors/behavior_assistant_change.dart` | 2 | BehaviorAssistantChangeView, _BehaviorAssistantChangeViewState |
| `src/Modules/UI/Flutter/shell/lib/behaviors/behavior_revisions.dart` | 2 | BehaviorRevisionsView, _RevisionCard |
| `src/Modules/UI/Flutter/shell/lib/behaviors/behavior_scenarios.dart` | 2 | BehaviorScenariosView, _ScenarioCard |
| `src/Modules/UI/Flutter/shell/lib/behaviors/behavior_view_model.dart` | 2 | BehaviorStudioView, BehaviorStudioController |
| `src/Modules/UI/Flutter/shell/lib/brain/brain_screen.dart` | 2 | BrainScreen, _BrainScreenState |
| `src/Modules/UI/Flutter/shell/lib/chat/brain_workspace.dart` | 2 | BrainWorkspace, _BrainWorkspaceState |
| `src/Modules/UI/Flutter/shell/lib/kit/kit_chat.dart` | 2 | KitChatDemo, _KitChatDemoState |
| `src/Modules/UI/Flutter/shell/lib/user_actions/user_action_card.dart` | 2 | UserActionCardModel, UserActionCard |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/lang/jstring.dart` | 2 | JStringExtension, ToJStringMethod |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/nio/jbyte_buffer.dart` | 2 | type, Uint8ListToJava |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/types.dart` | 2 | JCallable, JAccessible |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/util/jiterator.dart` | 2 | JIteratorToAdapter, JIteratorAdapter |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/path_provider_windows/example/lib/main.dart` | 2 | MyApp, _MyAppState |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/path_provider_windows/lib/src/path_provider_windows_real.dart` | 2 | VersionInfoQuerier, PathProviderWindows |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/path_provider_windows/lib/src/path_provider_windows_stub.dart` | 2 | PathProviderWindows, VersionInfoQuerier |

## Appendix A — Per-file inventory (generated)

Legend: **Belong** = layer fit; **Multi** = type count > 1 violates one-type-per-file rule.

### Kernel/Aspire (12 files)

| Path | Types | Type names | Belongs? | Relocate? | Quality | State | Security | Action |
|---|---:|---|---|---|---|---|---|---|
| `src/Kernel/Aspire/DigitalBrain.Aspire.Hosting/ClientDigitalBrainReference.cs` | 1 | ClientDigitalBrainReference | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/Aspire/DigitalBrain.Aspire.Hosting/DigitalBrainBuilder.cs` | 2 | DigitalBrainBuilder, StateProtectionKeyParameterDefault | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Kernel/Aspire/DigitalBrain.Aspire.Hosting/DigitalBrainHostingExtensions.cs` | 1 | DigitalBrainHostingExtensions | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/Aspire/DigitalBrain.Aspire.Hosting/DigitalBrainModuleBuilder.cs` | 1 | DigitalBrainModuleBuilder | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/Aspire/DigitalBrain.Aspire.Hosting/DigitalBrainModuleProjection.cs` | 1 | DigitalBrainModuleProjection | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/Aspire/DigitalBrain.Aspire.Hosting/OAuthProviderHosting.cs` | 5 | OAuthProviderHostingDefinition, OAuthProviderHosting, OAuthApplicationParamet… | OK |  | FAIL: multi-type file (5 types) | — | — | Split 1 type/file |
| `src/Kernel/Aspire/DigitalBrain.Aspire.Hosting/OperatorParameterDefaults.cs` | 2 | ConstantParameterDefault, OperatorSuppliedParameterDefault | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Kernel/Aspire/DigitalBrain.Aspire/DigitalBrainActivationHostedService.cs` | 1 | DigitalBrainActivationHostedService | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/Aspire/DigitalBrain.Aspire/DigitalBrainClientHostingExtensions.cs` | 1 | DigitalBrainClientHostingExtensions | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/Aspire/DigitalBrain.Aspire/DigitalBrainRuntimeHostingExtensions.cs` | 1 | DigitalBrainRuntimeHostingExtensions | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/Aspire/DigitalBrain.Aspire/DigitalBrainScriptHost.cs` | 1 | DigitalBrainScriptHost | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/Aspire/DigitalBrain.ServiceDefaults/ServiceDefaultsExtensions.cs` | 3 | ServiceDefaultsExtensions, SuppressAzureStorageSampler, SuppressAzureStorageA… | OK |  | FAIL: multi-type file (3 types) | — | — | Split 1 type/file |

### Kernel/DigitalBrain.Abstractions (66 files)

| Path | Types | Type names | Belongs? | Relocate? | Quality | State | Security | Action |
|---|---:|---|---|---|---|---|---|---|
| `src/Kernel/DigitalBrain.Abstractions/Capabilities/CapabilityManifest.cs` | 3 | CapabilityManifest, NeuronCapabilityDescriptor, SynapseCapabilityDescriptor | OK |  | FAIL: multi-type file (3 types) | — | — | Split 1 type/file |
| `src/Kernel/DigitalBrain.Abstractions/DigitalBrainResourceNames.cs` | 1 | DigitalBrainResourceNames | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Identity/ActorContext.cs` | 1 | ActorContext | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Identity/CommandId.cs` | 1 | struct | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Identity/CorrelationId.cs` | 1 | struct | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Identity/IdentityPart.cs` | 1 | IdentityPart | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Identity/ModuleId.cs` | 1 | struct | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Identity/NeuronId.cs` | 1 | struct | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Identity/OwnerId.cs` | 1 | struct | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Identity/PrincipalId.cs` | 1 | struct | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Identity/PrincipalPartition.cs` | 1 | PrincipalPartition | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Identity/SynapseId.cs` | 1 | struct | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Integrations/Integration.cs` | 2 | IntegrationScope, Integration | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Kernel/DigitalBrain.Abstractions/Journals/IJournalObserver.cs` | 1 | IJournalObserver | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Journals/JournalKind.cs` | 1 | JournalKind | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Journals/JournalRead.cs` | 1 | JournalRead | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Journals/JournalSnapshot.cs` | 1 | JournalSnapshot | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Journals/JournalTally.cs` | 1 | JournalTally | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Neurons/ClientEntryPointAttribute.cs` | 1 | ClientEntryPointAttribute | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Neurons/IBehavior.cs` | 1 | IBehavior | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Neurons/ICell.cs` | 1 | ICell | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Neurons/ICorpus.cs` | 1 | ICorpus | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Neurons/IDigitalBrainNeuron.cs` | 1 | IDigitalBrainNeuron | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Neurons/IGrants.cs` | 1 | IGrants | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Neurons/IHandle.cs` | 1 | IHandle | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Neurons/IKindRegistry.cs` | 1 | IKindRegistry | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Neurons/ILibrary.cs` | 1 | ILibrary | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Neurons/INeuron.cs` | 1 | INeuron | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Neurons/IRegistry.cs` | 1 | IRegistry | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Neurons/IRepository.cs` | 1 | IRepository | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Neurons/ISessionNeuron.cs` | 1 | ISessionNeuron | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Neurons/ISynapseGraph.cs` | 1 | ISynapseGraph | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Neurons/IWorkspace.cs` | 1 | IWorkspace | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Neurons/NeuronAuthorizationException.cs` | 1 | NeuronAuthorizationException | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Neurons/NeuronCallTimeouts.cs` | 1 | NeuronCallTimeouts | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Neurons/SettledDeliveryFailureAttribute.cs` | 1 | SettledDeliveryFailureAttribute | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Neurons/SynapseConnection.cs` | 1 | SynapseConnection | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Neurons/WorkspaceMember.cs` | 1 | WorkspaceMember | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Neurons/WorkspaceRole.cs` | 1 | WorkspaceRole | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/OAuth/OAuthCallbackPaths.cs` | 1 | OAuthCallbackPaths | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Security/ProtectedPayloadReference.cs` | 1 | struct | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/BehaviorCommands.cs` | 7 | StartRepoReview, BehaviorRunStarted, ReadBehaviorRun, BehaviorRunSnapshot, Be… | OK |  | FAIL: multi-type file (7 types) | — | — | Split synapses one-per-file |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/BroadcastAttribute.cs` | 1 | BroadcastAttribute | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/CapabilityAbandoned.cs` | 1 | CapabilityAbandoned | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/CapabilityCompleted.cs` | 1 | CapabilityCompleted | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/CapabilityFailed.cs` | 1 | CapabilityFailed | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/CapabilityRejected.cs` | 1 | CapabilityRejected | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/CapabilityRequested.cs` | 1 | CapabilityRequested | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/CellCommands.cs` | 4 | CellApply, CellReset, CellSnapshot, Datum | OK |  | FAIL: multi-type file (4 types) | — | — | Split synapses one-per-file |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/Connect.cs` | 2 | Connect, Connected | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/CorpusCommands.cs` | 7 | AppendCorpusEntry, CorpusAppended, ReadCorpus, CorpusPage, ReadEpisode, Episo… | OK |  | FAIL: multi-type file (7 types) | — | — | Split synapses one-per-file |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/DigitalBrainActivated.cs` | 1 | DigitalBrainActivated | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/Disconnect.cs` | 2 | Disconnect, Disconnected | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/GrantCommands.cs` | 8 | GrantKind, GrantAccess, AccessGranted, RevokeAccess, AccessRevoked, ListGrant… | OK |  | FAIL: multi-type file (8 types) | — | — | Split synapses one-per-file |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/JournalProjectionAttribute.cs` | 1 | JournalProjectionAttribute | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/KindCommands.cs` | 5 | InstallKind, KindInstalled, ListKinds, KindsListed, KindRecord | OK |  | FAIL: multi-type file (5 types) | — | — | Split synapses one-per-file |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/LibraryCommands.cs` | 12 | PublishLibraryArtifact, LibraryArtifactPublished, DiscoverLibrary, LibraryDis… | OK |  | FAIL: multi-type file (12 types) | — | — | Split synapses one-per-file |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/Provenance.cs` | 1 | Provenance | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/RegistryCommands.cs` | 13 | RegisterInstance, InstanceRegistered, RetireInstance, InstanceRetired, SetIns… | OK |  | FAIL: multi-type file (13 types) | — | — | Split synapses one-per-file |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/RepositoryCommands.cs` | 6 | OpenRepository, RepositoryOpened, ListRepositoryFiles, RepositoryFilesListed,… | OK |  | FAIL: multi-type file (6 types) | — | — | Split synapses one-per-file |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/RequestSynapse.cs` | 1 | RequestSynapse | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/RouteOutcome.cs` | 2 | RouteOutcomeKind, RouteOutcome | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/Synapse.cs` | 1 | Synapse | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/SynapseDelivery.cs` | 1 | SynapseDelivery | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/Unrouted.cs` | 1 | Unrouted | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Abstractions/Synapses/WorkspaceMembership.cs` | 8 | AddMember, MemberAdded, ChangeRole, RoleChanged, RemoveMember, MemberRemoved,… | OK |  | FAIL: multi-type file (8 types) | — | — | Split 1 type/file |

### Kernel/DigitalBrain.AppHost (2 files)

| Path | Types | Type names | Belongs? | Relocate? | Quality | State | Security | Action |
|---|---:|---|---|---|---|---|---|---|
| `src/Kernel/DigitalBrain.AppHost/AppHost.cs` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.AppHost/ProductSurfaceResources.cs` | 1 | ProductSurfaceResources | OK |  | Acceptable | — | — | Keep |

### Kernel/DigitalBrain.Client (4 files)

| Path | Types | Type names | Belongs? | Relocate? | Quality | State | Security | Action |
|---|---:|---|---|---|---|---|---|---|
| `src/Kernel/DigitalBrain.Client/ChannelJournalObserver.cs` | 1 | ChannelJournalObserver | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Client/DigitalBrainClient.cs` | 1 | DigitalBrainClient | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Client/IDigitalBrain.cs` | 1 | IDigitalBrain | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Client/NeuronReference.cs` | 1 | NeuronReference | OK |  | Acceptable | — | — | Keep |

### Kernel/DigitalBrain.Core (64 files)

| Path | Types | Type names | Belongs? | Relocate? | Quality | State | Security | Action |
|---|---:|---|---|---|---|---|---|---|
| `src/Kernel/DigitalBrain.Core/BroadcastCatalog.cs` | 3 | BroadcastRoute, BroadcastTopology, BroadcastCatalog | OK |  | FAIL: multi-type file (3 types) | — | — | Split 1 type/file |
| `src/Kernel/DigitalBrain.Core/Capabilities/ActiveCapabilityCatalog.cs` | 1 | ActiveCapabilityCatalog | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Capabilities/ActiveModuleContractTypeMap.cs` | 1 | ActiveModuleContractTypeMap | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Capabilities/CapabilityIndex.cs` | 4 | CapabilityHit, CapabilityIndex, Entry, ContractSignature | OK |  | FAIL: multi-type file (4 types) | — | — | Split 1 type/file |
| `src/Kernel/DigitalBrain.Core/Capabilities/CapabilityInvocation.cs` | 1 | CapabilityInvocation | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Capabilities/CapabilityOutcome.cs` | 1 | CapabilityOutcome | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Capabilities/CapabilityRequestContext.cs` | 1 | CapabilityRequestContext | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Capabilities/CapabilitySchema.cs` | 1 | CapabilitySchema | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Capabilities/ExternalServerCapability.cs` | 1 | ExternalServerCapability | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Capabilities/ModuleReflection.cs` | 1 | ModuleReflection | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/DeliveryPolicy.cs` | 1 | DeliveryPolicy | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Filters/IncomingReificationFilter.cs` | 1 | IncomingReificationFilter | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Filters/OutgoingReificationFilter.cs` | 1 | OutgoingReificationFilter | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Filters/OwnerBoundCallFilter.cs` | 1 | OwnerBoundCallFilter | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/GrainCallerContext.cs` | 2 | GrainCallerContext, CallerScope | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Kernel/DigitalBrain.Core/GrainOwnership.cs` | 1 | GrainOwnership | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Hosting/AssemblyBroadcastHandlers.cs` | 1 | AssemblyBroadcastHandlers | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Hosting/DigitalBrainRuntime.cs` | 1 | DigitalBrainRuntime | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Hosting/IConfigureBroadcastCatalog.cs` | 1 | IConfigureBroadcastCatalog | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Hosting/JournalStorageHosting.cs` | 1 | JournalStorageHosting | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Hosting/ModelPayloadSerialization.cs` | 1 | ModelPayloadSerialization | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Hosting/ModuleAssemblies.cs` | 1 | ModuleAssemblies | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Identity/PrincipalGraph.cs` | 3 | PrincipalGraph, PrincipalRegistry, PrincipalGrants | Misplaced | DigitalBrain.Security or Identity module | FAIL: multi-type file (3 types) | — | High — ambient principal | Move to Security |
| `src/Kernel/DigitalBrain.Core/Identity/VerifiedActor.cs` | 2 | VerifiedActor, Restore | Misplaced | DigitalBrain.Security or Identity module | FAIL: multi-type file (2 types) | — | High — ambient principal | Move to Security |
| `src/Kernel/DigitalBrain.Core/IModule.cs` | 1 | IModule | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Neuron/BehaviorNeuron.cs` | 3 | BehaviorNeuron, BehaviorState, StoredRun | Misplaced | Modules/* domain package | FAIL: multi-type file (3 types) | Review durable JSON/binary | — | Split 1 type/file |
| `src/Kernel/DigitalBrain.Core/Neuron/CellNeuron.cs` | 4 | CellNeuron, CellState, ICellKind, CalculatorKind | Misplaced | Modules/* domain package | FAIL: multi-type file (4 types) | Review durable JSON/binary | — | Split 1 type/file |
| `src/Kernel/DigitalBrain.Core/Neuron/ConnectionRelayNeuron.cs` | 2 | ConnectionRelay, ConnectionRelayNeuron | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Kernel/DigitalBrain.Core/Neuron/CorpusNeuron.cs` | 2 | CorpusNeuron, CorpusState | Misplaced | Modules/* domain package | FAIL: multi-type file (2 types) | Review durable JSON/binary | — | Split 1 type/file |
| `src/Kernel/DigitalBrain.Core/Neuron/DigitalBrainNeuron.cs` | 1 | DigitalBrainNeuron | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Neuron/GrantsNeuron.cs` | 1 | GrantsNeuron | Misplaced | Modules/* domain package | Acceptable | Review durable JSON/binary | — | Extract module |
| `src/Kernel/DigitalBrain.Core/Neuron/InstanceRegistryNeuron.cs` | 1 | InstanceRegistryNeuron | Misplaced | Modules/* domain package | Acceptable | Review durable JSON/binary | — | Extract module |
| `src/Kernel/DigitalBrain.Core/Neuron/JournalEntry.cs` | 1 | JournalEntry | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Neuron/KindRegistryNeuron.cs` | 1 | KindRegistryNeuron | Misplaced | Modules/* domain package | Acceptable | Review durable JSON/binary | — | Extract module |
| `src/Kernel/DigitalBrain.Core/Neuron/LibraryNeuron.cs` | 2 | LibraryNeuron, LibraryState | Misplaced | Modules/* domain package | FAIL: multi-type file (2 types) | Review durable JSON/binary | — | Split 1 type/file |
| `src/Kernel/DigitalBrain.Core/Neuron/Neuron.cs` | 4 | Neuron, ClientEntryCorrelationScope, struct, struct | OK |  | FAIL: multi-type file (4 types) | — | — | Split 1 type/file |
| `src/Kernel/DigitalBrain.Core/Neuron/NeuronCapabilityCoordinator.cs` | 1 | NeuronCapabilityCoordinator | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Neuron/NeuronConcurrency.cs` | 1 | NeuronConcurrency | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Neuron/NeuronDeliveryMemory.cs` | 1 | NeuronDeliveryMemory | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Neuron/NeuronFeed.cs` | 1 | NeuronFeed | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Neuron/NeuronFeedCheckpoint.cs` | 1 | struct | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Neuron/NeuronJournal.cs` | 2 | NeuronJournal, Watcher | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Kernel/DigitalBrain.Core/Neuron/NeuronMessagePipeline.cs` | 1 | NeuronMessagePipeline | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Neuron/NeuronOutbox.cs` | 1 | NeuronOutbox | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Neuron/NeuronStreamRegistry.cs` | 1 | NeuronStreamRegistry | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Neuron/NeuronTime.cs` | 1 | NeuronTime | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Neuron/NeuronTurnCoordinator.cs` | 1 | NeuronTurnCoordinator | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Neuron/RepositoryNeuron.cs` | 2 | RepositoryNeuron, RepoState | Misplaced | Modules/* domain package | FAIL: multi-type file (2 types) | Review durable JSON/binary | — | Split 1 type/file |
| `src/Kernel/DigitalBrain.Core/Neuron/SessionNeuron.cs` | 1 | SessionNeuron | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Neuron/SynapseGraphNeuron.cs` | 1 | SynapseGraphNeuron | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Neuron/WorkspaceNeuron.cs` | 2 | WorkspaceState, WorkspaceNeuron | Misplaced | Modules/* domain package | FAIL: multi-type file (2 types) | Review durable JSON/binary | — | Split 1 type/file |
| `src/Kernel/DigitalBrain.Core/Outbox/IOutboxDrain.cs` | 1 | IOutboxDrain | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Outbox/IOutboxWakeup.cs` | 1 | IOutboxWakeup | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Outbox/OutboxEntry.cs` | 1 | OutboxEntry | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Outbox/OutboxWakeup.cs` | 1 | OutboxWakeup | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/ReminderSourceAllowlist.cs` | 1 | ReminderSourceAllowlist | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Serialization/DispatchManifest.cs` | 1 | DispatchManifest | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Serialization/JournalJson.cs` | 1 | JournalJson | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Serialization/SynapseDispatch.cs` | 1 | SynapseDispatch | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Serialization/SynapseTelemetry.cs` | 1 | SynapseTelemetry | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Serialization/SynapseWiring.cs` | 1 | SynapseWiring | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/Serialization/SynapseWiringEntry.cs` | 1 | SynapseWiringEntry | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/SynapseAlias.cs` | 1 | SynapseAlias | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Core/SynapseTransform.cs` | 3 | ISynapseTransform, DeclarativeSynapseTransform, SynapseTypeIndex | OK |  | FAIL: multi-type file (3 types) | — | — | Split 1 type/file |

### Kernel/DigitalBrain.Kernel (30 files)

| Path | Types | Type names | Belongs? | Relocate? | Quality | State | Security | Action |
|---|---:|---|---|---|---|---|---|---|
| `src/Kernel/DigitalBrain.Kernel/Auth/AuthHostingExtensions.cs` | 2 | AuthHostingExtensions, LoopbackDevAuthOptions | Misplaced | DigitalBrain.Security / Host.Auth | FAIL: multi-type file (2 types) | — | Host auth boundary | Extract auth package |
| `src/Kernel/DigitalBrain.Kernel/Auth/AuthOptions.cs` | 1 | AuthOptions | Misplaced | DigitalBrain.Security / Host.Auth | Acceptable | — | Host auth boundary | Extract auth package |
| `src/Kernel/DigitalBrain.Kernel/Auth/DevelopmentBootstrapSeeder.cs` | 1 | DevelopmentBootstrapSeeder | Misplaced | DigitalBrain.Security / Host.Auth | Acceptable | — | Host auth boundary | Extract auth package |
| `src/Kernel/DigitalBrain.Kernel/Auth/DigitalBrainClaimsPrincipalFactory.cs` | 1 | DigitalBrainClaimsPrincipalFactory | Misplaced | DigitalBrain.Security / Host.Auth | Acceptable | — | Host auth boundary | Extract auth package |
| `src/Kernel/DigitalBrain.Kernel/Auth/DigitalBrainUser.cs` | 1 | DigitalBrainUser | Misplaced | DigitalBrain.Security / Host.Auth | Acceptable | — | Host auth boundary | Extract auth package |
| `src/Kernel/DigitalBrain.Kernel/Auth/DigitalBrainUserStore.cs` | 1 | DigitalBrainUserStore | Misplaced | DigitalBrain.Security / Host.Auth | Acceptable | — | Host auth boundary | Extract auth package |
| `src/Kernel/DigitalBrain.Kernel/Auth/HttpActor.cs` | 1 | HttpActor | Misplaced | DigitalBrain.Security / Host.Auth | Acceptable | — | Host auth boundary | Extract auth package |
| `src/Kernel/DigitalBrain.Kernel/Auth/HttpsStanceMiddleware.cs` | 1 | HttpsStanceMiddleware | Misplaced | DigitalBrain.Security / Host.Auth | Acceptable | — | Host auth boundary | Extract auth package |
| `src/Kernel/DigitalBrain.Kernel/Auth/IAccountDirectory.cs` | 1 | IAccountDirectory | Misplaced | DigitalBrain.Security / Host.Auth | Acceptable | — | Host auth boundary | Extract auth package |
| `src/Kernel/DigitalBrain.Kernel/Auth/LoopbackDevAuthMiddleware.cs` | 1 | LoopbackDevAuthMiddleware | Misplaced | DigitalBrain.Security / Host.Auth | Acceptable | — | Host auth boundary | Extract auth package |
| `src/Kernel/DigitalBrain.Kernel/Auth/MapAuth.cs` | 4 | AuthHttpMaps, AuthCredentialsRequest, AuthCreateUserRequest, AuthMeResponse | Misplaced | DigitalBrain.Security / Host.Auth | FAIL: multi-type file (4 types) | — | Host auth boundary | Extract auth package |
| `src/Kernel/DigitalBrain.Kernel/Auth/MemoryAccountDirectory.cs` | 1 | MemoryAccountDirectory | Misplaced | DigitalBrain.Security / Host.Auth | Acceptable | — | Host auth boundary | Extract auth package |
| `src/Kernel/DigitalBrain.Kernel/Auth/PrincipalScoped.cs` | 3 | PrincipalScoped, PrincipalChat, PrincipalSurface | Misplaced | DigitalBrain.Security / Host.Auth | FAIL: multi-type file (3 types) | — | Host auth boundary | Extract auth package |
| `src/Kernel/DigitalBrain.Kernel/Auth/RequestNetwork.cs` | 1 | RequestNetwork | Misplaced | DigitalBrain.Security / Host.Auth | Acceptable | — | Host auth boundary | Extract auth package |
| `src/Kernel/DigitalBrain.Kernel/Auth/TableAccountDirectory.cs` | 1 | TableAccountDirectory | Misplaced | DigitalBrain.Security / Host.Auth | Acceptable | — | Host auth boundary | Extract auth package |
| `src/Kernel/DigitalBrain.Kernel/Auth/WorkspaceMembershipGateway.cs` | 2 | IWorkspaceMembershipGateway, WorkspaceMembershipGateway | Misplaced | DigitalBrain.Security / Host.Auth | FAIL: multi-type file (2 types) | — | Host auth boundary | Extract auth package |
| `src/Kernel/DigitalBrain.Kernel/DigitalBrainComposition.cs` | 2 | ComposedModules, DigitalBrainHost | Duplicate catalog | Single source with AppHost | FAIL: multi-type file (2 types) | — | — | Unify module catalog |
| `src/Kernel/DigitalBrain.Kernel/HttpSurfaceModels.cs` | 10 | OwnerCommandRequest, ChatTurnEvent, SurfaceOpenedEvent, AuthorizationEvent, B… | Misplaced | UI.Http host package | FAIL: multi-type file (10 types) | — | — | Move product HTTP maps |
| `src/Kernel/DigitalBrain.Kernel/HttpSurfacePaths.cs` | 1 | HttpSurfacePaths | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Kernel/JournalProjection.cs` | 1 | JournalProjection | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Kernel/MapAuthorizationStreams.cs` | 1 | AuthorizationStreamsHttpMaps | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Kernel/MapBrainTopology.cs` | 1 | BrainTopologyHttpMaps | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Kernel/MapChatStreams.cs` | 1 | ChatStreamsHttpMaps | Misplaced | UI.Http host package | Acceptable | — | — | Move product HTTP maps |
| `src/Kernel/DigitalBrain.Kernel/MapGraphStreams.cs` | 1 | GraphStreamsHttpMaps | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Kernel/MapOAuthCallback.cs` | 1 | OAuthCallbackHttpMaps | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Kernel/MapOwnerCommands.cs` | 1 | OwnerCommandsHttpMaps | Misplaced | UI.Http host package | Acceptable | — | — | Move product HTTP maps |
| `src/Kernel/DigitalBrain.Kernel/MapShellStreams.cs` | 1 | SurfaceStreamsHttpMaps | Misplaced | UI.Http host package | Acceptable | — | — | Move product HTTP maps |
| `src/Kernel/DigitalBrain.Kernel/OwnerSessionJournal.cs` | 1 | OwnerSessionJournal | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Kernel/Program.cs` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Kernel/SseResponse.cs` | 1 | SseResponse | OK |  | Acceptable | — | — | Keep |

### Kernel/DigitalBrain.Mcp (8 files)

| Path | Types | Type names | Belongs? | Relocate? | Quality | State | Security | Action |
|---|---:|---|---|---|---|---|---|---|
| `src/Kernel/DigitalBrain.Mcp/ChatTools.cs` | 1 | ChatTools | OK |  | Needs auth binding | — | CRITICAL: spoofable principalKey | Bind real principal / drop spoof keys |
| `src/Kernel/DigitalBrain.Mcp/IntrospectionTools.cs` | 1 | IntrospectionTools | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Mcp/LibraryBehaviorTools.cs` | 1 | LibraryBehaviorTools | OK |  | Needs auth binding | — | CRITICAL: spoofable principalKey | Bind real principal / drop spoof keys |
| `src/Kernel/DigitalBrain.Mcp/McpSurface.cs` | 1 | McpSurface | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Mcp/Program.cs` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Mcp/RegistryTools.cs` | 4 | RegistryTools, BundleMemberDto, RegistryEntry, BundleInstallResult | OK |  | FAIL: multi-type file (4 types) | — | CRITICAL: spoofable principalKey | Bind real principal / drop spoof keys |
| `src/Kernel/DigitalBrain.Mcp/TimeTools.cs` | 1 | TimeTools | OK |  | Needs auth binding | — | CRITICAL: spoofable principalKey | Bind real principal / drop spoof keys |
| `src/Kernel/DigitalBrain.Mcp/ToolModels.cs` | 9 | NeuronJournalPage, JournaledSynapse, ActiveNeuron, ChatTranscriptPage, ChatTr… | OK |  | FAIL: multi-type file (9 types) | — | — | Split 1 type/file |

### Kernel/DigitalBrain.Scripting (7 files)

| Path | Types | Type names | Belongs? | Relocate? | Quality | State | Security | Action |
|---|---:|---|---|---|---|---|---|---|
| `src/Kernel/DigitalBrain.Scripting/chart-point.cs` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Scripting/chat-probe.cs` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Scripting/connect-chat-responder.cs` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Scripting/outcome-probe.cs` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Scripting/Program.cs` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Scripting/prune-membership.cs` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Kernel/DigitalBrain.Scripting/wave2-registry-probe.cs` | 0 | — | OK |  | Acceptable | — | — | Keep |

### Kernel/DigitalBrain.Sdk (26 files)

| Path | Types | Type names | Belongs? | Relocate? | Quality | State | Security | Action |
|---|---:|---|---|---|---|---|---|---|
| `src/Kernel/DigitalBrain.Sdk/Mcp/AuthorizationFacts.cs` | 3 | AuthorizationRequired, AuthorizationCompleted, AuthorizationDenied | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | FAIL: multi-type file (3 types) | — | OAuth rail | Split 1 type/file |
| `src/Kernel/DigitalBrain.Sdk/Mcp/DurableMcpTokenCache.cs` | 1 | DurableMcpTokenCache | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | Acceptable | — | OAuth rail | Keep |
| `src/Kernel/DigitalBrain.Sdk/Mcp/IMcpAuthorization.cs` | 1 | IMcpAuthorization | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | Acceptable | — | OAuth rail | Keep |
| `src/Kernel/DigitalBrain.Sdk/Mcp/IMcpAuthorizationCodes.cs` | 1 | IMcpAuthorizationCodes | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | Acceptable | — | OAuth rail | Keep |
| `src/Kernel/DigitalBrain.Sdk/Mcp/IMcpServer.cs` | 10 | IMcp, ListMcpTools, McpToolsListed, McpToolDescription, CallMcpTool, McpToolR… | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | FAIL: multi-type file (10 types) | — | OAuth rail | Split 1 type/file |
| `src/Kernel/DigitalBrain.Sdk/Mcp/McpAuthorizationCodeHub.cs` | 2 | McpAuthorizationCodeHub, CodeHubOutcome | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | FAIL: multi-type file (2 types) | — | OAuth rail | Split 1 type/file |
| `src/Kernel/DigitalBrain.Sdk/Mcp/McpAuthorizationDeniedException.cs` | 1 | McpAuthorizationDeniedException | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | Acceptable | — | OAuth rail | Keep |
| `src/Kernel/DigitalBrain.Sdk/Mcp/McpAuthorizationElicitation.cs` | 1 | McpAuthorizationElicitation | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | Acceptable | — | OAuth rail | Keep |
| `src/Kernel/DigitalBrain.Sdk/Mcp/McpAuthorizationNeuron.cs` | 4 | McpAuthorizationNeuron, PendingAuthorization, PendingAuthorizationOutcome, Co… | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | FAIL: multi-type file (4 types) | — | OAuth rail | Split 1 type/file |
| `src/Kernel/DigitalBrain.Sdk/Mcp/McpAuthorizationRail.cs` | 3 | McpAuthorizationRail, IMcpTokenExchanger, IMcpTokenRefresher | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | FAIL: multi-type file (3 types) | — | OAuth rail | Split 1 type/file |
| `src/Kernel/DigitalBrain.Sdk/Mcp/McpAuthorizationRequiredException.cs` | 1 | McpAuthorizationRequiredException | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | Acceptable | — | OAuth rail | Keep |
| `src/Kernel/DigitalBrain.Sdk/Mcp/McpAuthorizationVocabulary.cs` | 7 | BeginMcpAuthorization, BindMcpAuthorizationCompletionTarget, DeliverMcpAuthor… | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | FAIL: multi-type file (7 types) | — | OAuth rail | Split 1 type/file |
| `src/Kernel/DigitalBrain.Sdk/Mcp/McpClientSessions.cs` | 1 | McpClientSessions | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | Acceptable | — | OAuth rail | Keep |
| `src/Kernel/DigitalBrain.Sdk/Mcp/McpOAuthCallback.cs` | 1 | McpOAuthCallback | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | Acceptable | — | OAuth rail | Keep |
| `src/Kernel/DigitalBrain.Sdk/Mcp/McpOAuthOptions.cs` | 1 | McpOAuthOptions | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | Acceptable | — | OAuth rail | Keep |
| `src/Kernel/DigitalBrain.Sdk/Mcp/McpOAuthSession.cs` | 1 | McpOAuthSession | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | Acceptable | — | OAuth rail | Keep |
| `src/Kernel/DigitalBrain.Sdk/Mcp/McpRuntimeHosting.cs` | 1 | McpRuntimeHosting | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | Acceptable | — | OAuth rail | Keep |
| `src/Kernel/DigitalBrain.Sdk/Mcp/McpServerDefinition.cs` | 1 | McpServerDefinition | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | Acceptable | — | OAuth rail | Keep |
| `src/Kernel/DigitalBrain.Sdk/Mcp/McpServerNeuron.cs` | 1 | McpServerNeuron | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | Acceptable | — | OAuth rail | Keep |
| `src/Kernel/DigitalBrain.Sdk/Mcp/McpTokenExchange.cs` | 1 | McpTokenExchange | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | Acceptable | — | OAuth rail | Keep |
| `src/Kernel/DigitalBrain.Sdk/Mcp/McpTokenPresence.cs` | 1 | McpTokenPresence | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | Acceptable | — | OAuth rail | Keep |
| `src/Kernel/DigitalBrain.Sdk/Mcp/McpToolFingerprint.cs` | 1 | McpToolFingerprint | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | Acceptable | — | OAuth rail | Keep |
| `src/Kernel/DigitalBrain.Sdk/Mcp/OAuthPkce.cs` | 1 | OAuthPkce | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | Acceptable | — | OAuth rail | Keep |
| `src/Kernel/DigitalBrain.Sdk/Mcp/PrincipalTokenSlot.cs` | 1 | PrincipalTokenSlot | OK (rename) | Modules.Sdk.Mcp; folder matches assembly | Acceptable | — | OAuth rail | Keep |
| `src/Kernel/DigitalBrain.Sdk/Webhook/WebhookFacts.cs` | 4 | VerifiedWebhookDeliveryReceived, WebhookDeliveryAccepted, WebhookDeliveryDupl… | OK |  | FAIL: multi-type file (4 types) | — | — | Split 1 type/file |
| `src/Kernel/DigitalBrain.Sdk/Webhook/WebhookIngressNeuron.cs` | 2 | WebhookIngressNeuron, WebhookIngressState | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |

### Kernel/DigitalBrain.Security (2 files)

| Path | Types | Type names | Belongs? | Relocate? | Quality | State | Security | Action |
|---|---:|---|---|---|---|---|---|---|
| `src/Kernel/DigitalBrain.Security/DurablePayloadProtectionHosting.cs` | 2 | DurablePayloadProtectionHosting, DurablePayloadProtector | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Kernel/DigitalBrain.Security/IDurablePayloadProtector.cs` | 1 | IDurablePayloadProtector | OK |  | Acceptable | — | — | Keep |

### Modules/AI (43 files)

| Path | Types | Type names | Belongs? | Relocate? | Quality | State | Security | Action |
|---|---:|---|---|---|---|---|---|---|
| `src/Modules/AI/AI/Agent.cs` | 1 | Agent | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/AIModule.cs` | 1 | AIModule | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/Assistant.cs` | 1 | Assistant | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/Capabilities/SynapseCapabilityTool.cs` | 1 | SynapseCapabilityTool | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/Capabilities/SystemTools.cs` | 1 | SystemTools | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/Clients/AIClients.cs` | 1 | AIClients | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/Clients/LlmWarmupHostedService.cs` | 1 | LlmWarmupHostedService | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/Clients/StreamingUsageChatClientExtensions.cs` | 1 | StreamingUsageChatClientExtensions | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/LLM/LLM.cs` | 1 | LLM | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/LLM/LlmAttribute.cs` | 1 | LlmAttribute | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/Ollama/Gemma4.cs` | 1 | Gemma4 | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/Ollama/Granite41.cs` | 1 | Granite41 | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/Ollama/Llama32.cs` | 1 | Llama32 | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/Ollama/Qwen35.cs` | 1 | Qwen35 | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/OpenAI/Gpt56.cs` | 1 | Gpt56 | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/Orchestration/Concurrent.cs` | 1 | Concurrent | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/Orchestration/DirectAgentSession.cs` | 2 | DirectAgentSession, DirectAgentSessionEnvelope | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/AI/AI/Orchestration/DirectOrchestrationShape.cs` | 2 | DirectOrchestrationIdentity, DirectOrchestrationShape | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/AI/AI/Orchestration/GroupChat.cs` | 1 | GroupChat | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/Orchestration/MafParticipantAdapter.cs` | 1 | MafParticipantAdapter | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/Orchestration/ModelContracts.cs` | 1 | ModelContracts | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/Orchestration/ModelMentions.cs` | 1 | ModelMentions | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/Orchestration/NeuronChatClient.cs` | 1 | NeuronChatClient | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/Orchestration/OrchestrationDefinition.cs` | 3 | OrchestrationParticipant, OrchestrationDefinition, FingerprintSource | OK |  | FAIL: multi-type file (3 types) | — | — | Split 1 type/file |
| `src/Modules/AI/AI/Orchestration/Participant.cs` | 2 | Participant, Participant | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/AI/AI/Orchestration/ParticipantInvocations.cs` | 1 | ParticipantInvocations | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/Orchestration/Team.cs` | 1 | Team | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/Orchestration/TeamLineUp.cs` | 1 | TeamLineUp | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/AI/Tools/TurnBoundFunction.cs` | 1 | TurnBoundFunction | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/Aspire.Hosting/AIHostingExtensions.cs` | 2 | AIHostingExtensions, AIHostingState | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/AI/Contracts/CapabilityToolSelected.cs` | 1 | CapabilityToolSelected | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/Contracts/IAgent.cs` | 1 | IAgent | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/Contracts/IAssistant.cs` | 1 | IAssistant | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/Contracts/IGroupChat.cs` | 1 | IGroupChat | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/Contracts/ILLM.cs` | 1 | ILLM | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/Contracts/ITeam.cs` | 1 | ITeam | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/Contracts/Ollama/IGemma4.cs` | 1 | IGemma4 | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/Contracts/Ollama/IGranite41.cs` | 1 | IGranite41 | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/Contracts/Ollama/ILlama32.cs` | 1 | ILlama32 | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/Contracts/Ollama/IQwen35.cs` | 1 | IQwen35 | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/Contracts/OpenAI/IGpt56.cs` | 1 | IGpt56 | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/Contracts/OrchestrationRefusedException.cs` | 1 | OrchestrationRefusedException | OK |  | Acceptable | — | — | Keep |
| `src/Modules/AI/Contracts/TeamFormation.cs` | 1 | TeamFormation | OK |  | Acceptable | — | — | Keep |

### Modules/Execution (45 files)

| Path | Types | Type names | Belongs? | Relocate? | Quality | State | Security | Action |
|---|---:|---|---|---|---|---|---|---|
| `src/Modules/Execution/Contracts/AttemptCursor.cs` | 1 | AttemptCursor | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Contracts/AttemptFacts.cs` | 8 | AttemptFact, AttemptAccepted, AttemptProgressed, AttemptWaiting, AttemptSucce… | OK |  | FAIL: multi-type file (8 types) | — | — | Split 1 type/file |
| `src/Modules/Execution/Contracts/AttemptId.cs` | 1 | struct | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Contracts/AttemptRequest.cs` | 1 | AttemptRequest | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Contracts/BlockerId.cs` | 1 | struct | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Contracts/ExecutionBlockers.cs` | 7 | ExecutionBlocker, InputRequired, ApprovalRequired, DependencyPending, RetrySc… | OK |  | FAIL: multi-type file (7 types) | — | — | Split 1 type/file |
| `src/Modules/Execution/Contracts/ExecutionCommands.cs` | 7 | ExecutionPolicy, ExecutionApplyCommand, StartExecution, CancelExecution, Oper… | OK |  | FAIL: multi-type file (7 types) | — | — | Split 1 type/file |
| `src/Modules/Execution/Contracts/ExecutionLiveness.cs` | 1 | ExecutionLiveness | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Contracts/ExecutionOperationContracts.cs` | 7 | OperationPhase, OperationEdge, OperationSnapshot, PrepareOperation, Transitio… | OK |  | FAIL: multi-type file (7 types) | — | — | Split 1 type/file |
| `src/Modules/Execution/Contracts/ExecutionSnapshot.cs` | 1 | ExecutionSnapshot | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Contracts/ExecutionState.cs` | 1 | ExecutionState | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Contracts/ExecutionTerminal.cs` | 1 | ExecutionTerminal | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Contracts/FactReference.cs` | 1 | struct | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Contracts/Failure.cs` | 2 | Failure, ChatTurnFailure | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/Execution/Contracts/Goal.cs` | 1 | Goal | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Contracts/IExecution.cs` | 1 | IExecution | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Contracts/IExecutionWorkerLease.cs` | 1 | IExecutionWorkerLease | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Contracts/IUserActionCustody.cs` | 2 | IssuedUserAction, IUserActionCustody | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/Execution/Contracts/IWorker.cs` | 1 | IWorker | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Contracts/Result.cs` | 2 | Result, ChatTurnResult | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/Execution/Contracts/UserActionRequired.cs` | 6 | UserActionRequired, CompleteUserAction, DenyUserAction, UserActionDenied, Use… | OK |  | FAIL: multi-type file (6 types) | — | — | Split 1 type/file |
| `src/Modules/Execution/Contracts/WorkerAbandoned.cs` | 1 | WorkerAbandoned | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Execution/ExecutionAttemptHandler.cs` | 1 | ExecutionAttemptHandler | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Execution/ExecutionCanceller.cs` | 1 | ExecutionCanceller | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Execution/ExecutionCommandHandler.cs` | 1 | ExecutionCommandHandler | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Execution/ExecutionData.cs` | 1 | ExecutionData | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Execution/ExecutionDeliveryAuthorizer.cs` | 1 | ExecutionDeliveryAuthorizer | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Execution/ExecutionDispatcher.cs` | 1 | ExecutionDispatcher | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Execution/ExecutionDispatchQueue.cs` | 1 | ExecutionDispatchQueue | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Execution/ExecutionModel.cs` | 1 | ExecutionModel | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Execution/ExecutionModule.cs` | 1 | ExecutionModule | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Execution/ExecutionNeuron.cs` | 1 | ExecutionNeuron | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Execution/ExecutionOperationHandler.cs` | 1 | ExecutionOperationHandler | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Execution/ExecutionOperationLedger.cs` | 1 | ExecutionOperationLedger | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Execution/ExecutionOperationResolver.cs` | 1 | ExecutionOperationResolver | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Execution/ExecutionRecoveryHandler.cs` | 1 | ExecutionRecoveryHandler | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Execution/ExecutionRuntime.cs` | 2 | ExecutionRuntime, ExecutionReminders | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/Execution/Execution/ExecutionStarter.cs` | 1 | ExecutionStarter | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Execution/ExecutionStateStore.cs` | 1 | ExecutionStateStore | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Execution/ExecutionUserActionHandler.cs` | 1 | ExecutionUserActionHandler | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Execution/PendingWorkerDispatch.cs` | 4 | PendingWorkerDispatch, AcceptWorkerDispatch, ContinueWorkerDispatch, CancelWo… | OK |  | FAIL: multi-type file (4 types) | — | — | Split 1 type/file |
| `src/Modules/Execution/Execution/WorkerDispatchRelayNeuron.cs` | 1 | WorkerDispatchRelayNeuron | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Execution/Execution/WorkerDispatchSynapses.cs` | 7 | WorkerDispatchRelay, RelayWorkerAccept, RelayWorkerContinue, RelayWorkerCance… | OK |  | FAIL: multi-type file (7 types) | — | — | Split 1 type/file |
| `src/Modules/Execution/Execution/WorkerGrainTypeRegistry.cs` | 3 | IWorkerTypeRegistration, WorkerTypeRegistration, WorkerGrainTypeRegistry | OK |  | FAIL: multi-type file (3 types) | — | — | Split 1 type/file |
| `src/Modules/Execution/Execution/WorkerNeuron.cs` | 1 | WorkerNeuron | OK |  | Acceptable | — | — | Keep |

### Modules/Google (2 files)

| Path | Types | Type names | Belongs? | Relocate? | Quality | State | Security | Action |
|---|---:|---|---|---|---|---|---|---|
| `src/Modules/Google/Aspire.Hosting/GoogleHostingExtensions.cs` | 1 | GoogleHostingExtensions | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Google/Google/GoogleModule.cs` | 1 | GoogleModule | OK |  | Acceptable | — | — | Keep |

### Modules/Introspection (11 files)

| Path | Types | Type names | Belongs? | Relocate? | Quality | State | Security | Action |
|---|---:|---|---|---|---|---|---|---|
| `src/Modules/Introspection/Contracts/IIntrospection.cs` | 1 | IIntrospection | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Introspection/Contracts/IntrospectionRequests.cs` | 4 | IntrospectionIdentity, TallyJournalRequest, ReadJournalRequest, ReadTopologyR… | OK |  | FAIL: multi-type file (4 types) | — | — | Split 1 type/file |
| `src/Modules/Introspection/Contracts/JournalDirection.cs` | 1 | JournalDirection | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Introspection/Contracts/JournaledFact.cs` | 1 | JournaledFact | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Introspection/Contracts/JournalPageRead.cs` | 1 | JournalPageRead | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Introspection/Contracts/JournalTallied.cs` | 1 | JournalTallied | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Introspection/Contracts/TopologyRead.cs` | 4 | TopologyNeuron, TopologyConnection, TopologyBroadcastRoute, TopologyRead | OK |  | FAIL: multi-type file (4 types) | — | — | Split 1 type/file |
| `src/Modules/Introspection/Introspection/IntrospectionJournalReader.cs` | 1 | IntrospectionJournalReader | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Introspection/Introspection/IntrospectionNeuron.cs` | 1 | IntrospectionNeuron | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Introspection/Introspection/IntrospectionTopologyReader.cs` | 1 | IntrospectionTopologyReader | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Introspection/Introspection/OwnerNeuronInventory.cs` | 2 | OwnerNeuronInventory, ActivatedNeuron | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |

### Modules/Memory (14 files)

| Path | Types | Type names | Belongs? | Relocate? | Quality | State | Security | Action |
|---|---:|---|---|---|---|---|---|---|
| `src/Modules/Memory/Aspire.Hosting/MemoryHostingExtensions.cs` | 2 | MemoryHostingExtensions, MemoryHostingState | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/Memory/Contracts/IVectorMemory.cs` | 1 | IVectorMemory | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Memory/Contracts/RemoveVectorMemory.cs` | 2 | RemoveVectorMemory, VectorMemoryRemoved | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/Memory/Contracts/SearchVectorMemory.cs` | 3 | SearchVectorMemory, VectorMemoryMatches, VectorMemoryMatch | OK |  | FAIL: multi-type file (3 types) | — | — | Split 1 type/file |
| `src/Modules/Memory/Contracts/StoreVectorMemory.cs` | 2 | StoreVectorMemory, VectorMemoryStored | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/Memory/Contracts/VectorMemoryNamespace.cs` | 1 | VectorMemoryNamespace | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Memory/Contracts/VectorMemoryStoreStatus.cs` | 1 | VectorMemoryStoreStatus | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Memory/Memory/InMemoryVectorMemoryStore.cs` | 1 | InMemoryVectorMemoryStore | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Memory/Memory/IVectorMemoryStore.cs` | 2 | IVectorMemoryStore, VectorMemoryEntry | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/Memory/Memory/MemoryModule.cs` | 1 | MemoryModule | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Memory/Memory/Qdrant/QdrantVectorMemoryProvider.cs` | 2 | QdrantVectorMemoryProvider, QdrantVectorMemoryHit | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/Memory/Memory/Qdrant/QdrantVectorMemoryRegistration.cs` | 1 | QdrantVectorMemoryRegistration | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Memory/Memory/QdrantVectorMemoryStore.cs` | 1 | QdrantVectorMemoryStore | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Memory/Memory/VectorMemoryNeuron.cs` | 1 | VectorMemoryNeuron | OK |  | Acceptable | — | — | Keep |

### Modules/SalesForce (2 files)

| Path | Types | Type names | Belongs? | Relocate? | Quality | State | Security | Action |
|---|---:|---|---|---|---|---|---|---|
| `src/Modules/SalesForce/Aspire.Hosting/SalesforceHostingExtensions.cs` | 1 | SalesforceHostingExtensions | OK |  | Acceptable | — | — | Keep |
| `src/Modules/SalesForce/Salesforce/SalesforceModule.cs` | 1 | SalesforceModule | OK |  | Acceptable | — | — | Keep |

### Modules/Time (9 files)

| Path | Types | Type names | Belongs? | Relocate? | Quality | State | Security | Action |
|---|---:|---|---|---|---|---|---|---|
| `src/Modules/Time/Contracts/ISchedule.cs` | 1 | ISchedule | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Time/Contracts/ITimer.cs` | 1 | ITimer | OK |  | Acceptable | — | — | Keep |
| `src/Modules/Time/Contracts/ScheduleCommands.cs` | 6 | ArmSchedule, CancelSchedule, ForceScheduleCatchUp, ScheduleSnapshot, Schedule… | OK |  | FAIL: multi-type file (6 types) | — | — | Split 1 type/file |
| `src/Modules/Time/Contracts/ScheduleFacts.cs` | 4 | ScheduleDue, ScheduleTick, ScheduleArmed, ScheduleCancelled | OK |  | FAIL: multi-type file (4 types) | — | — | Split 1 type/file |
| `src/Modules/Time/Contracts/TimerCommands.cs` | 2 | StartTimer, CancelTimer | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/Time/Contracts/TimerFacts.cs` | 3 | TimerScheduled, TimerElapsed, TimerCancelled | OK |  | FAIL: multi-type file (3 types) | — | — | Split 1 type/file |
| `src/Modules/Time/Contracts/TimerSnapshot.cs` | 3 | TimerStatus, TimerResolution, TimerSnapshot | OK |  | FAIL: multi-type file (3 types) | — | — | Split 1 type/file |
| `src/Modules/Time/Time/ScheduleNeuron.cs` | 2 | ScheduleState, ScheduleNeuron | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/Time/Time/TimerNeuron.cs` | 2 | TimerState, TimerNeuron | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |

### Modules/UI (164 files)

| Path | Types | Type names | Belongs? | Relocate? | Quality | State | Security | Action |
|---|---:|---|---|---|---|---|---|---|
| `src/Modules/UI/DigitalBrain.Modules.UI.Aspire.Hosting/FlutterHostLaunch.cs` | 3 | FlutterHostKind, FlutterHostLaunch, Result | OK |  | FAIL: multi-type file (3 types) | — | — | Split 1 type/file |
| `src/Modules/UI/DigitalBrain.Modules.UI.Aspire.Hosting/FlutterHostOptions.cs` | 1 | FlutterHostOptions | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/DigitalBrain.Modules.UI.Aspire.Hosting/ShellHostingExtensions.cs` | 2 | ShellHostingExtensions, ShellHostingState | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Button/ChatButtons.cs` | 1 | ChatButtons | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Button/IButton.cs` | 1 | IButton | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Button/Synapses/ButtonActivated.cs` | 1 | ButtonActivated | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Button/Synapses/ButtonClicked.cs` | 1 | ButtonClicked | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Button/Synapses/ChatButtonOffer.cs` | 1 | ChatButtonOffer | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chart/IChart.cs` | 1 | IChart | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chart/Synapses/ChartPoint.cs` | 1 | ChartPoint | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/ChatRoles.cs` | 1 | ChatRoles | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/ChatTimerOffer.cs` | 1 | ChatTimerOffer | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/ChatTranscript.cs` | 1 | ChatTranscript | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/ChatTurn.cs` | 1 | ChatTurn | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/ChatTurnSnapshot.cs` | 1 | ChatTurnSnapshot | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/ChatTurnStatus.cs` | 1 | ChatTurnStatus | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/IChat.cs` | 1 | IChat | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/Synapses/CancelTurn.cs` | 1 | CancelTurn | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/Synapses/ChatChartOffer.cs` | 2 | ChatChartPoint, ChatChartOffer | Stage-2 → Conversations | Modules/Conversations | FAIL: multi-type file (2 types) | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/Synapses/Note.cs` | 1 | Note | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/Synapses/ReadTranscriptRequest.cs` | 2 | ChatIdentity, ReadTranscriptRequest | Stage-2 → Conversations | Modules/Conversations | FAIL: multi-type file (2 types) | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/Synapses/Responded.cs` | 1 | Responded | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/Synapses/SendMessage.cs` | 1 | SendMessage | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/Synapses/TimerCard.cs` | 1 | TimerCard | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/Synapses/TranscriptRead.cs` | 1 | TranscriptRead | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/Synapses/TurnLifecycle.cs` | 1 | TurnLifecycle | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/Synapses/UserMessaged.cs` | 1 | UserMessaged | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/TurnAccepted.cs` | 1 | TurnAccepted | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Chat/TurnId.cs` | 1 | struct | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Diagram/IDiagram.cs` | 2 | DiagramRead, IDiagram | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Diagram/Synapses/Edge.cs` | 1 | Edge | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Diagram/Synapses/Node.cs` | 1 | Node | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Surface/ISurface.cs` | 1 | ISurface | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Surface/Synapses/ControlActivated.cs` | 1 | ControlActivated | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Surface/Synapses/OpenSurface.cs` | 1 | OpenSurface | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/DigitalBrain.Modules.UI.Contracts/Surface/Synapses/SurfaceOpened.cs` | 1 | SurfaceOpened | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/DigitalBrain.Modules.UI/Button/Button.cs` | 1 | Button | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/DigitalBrain.Modules.UI/Chart/ChartNeuron.cs` | 1 | ChartNeuron | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/DigitalBrain.Modules.UI/Chat/Chat.cs` | 4 | Chat, OwnerCommand, DurableTurnRecord, TurnQueueState | Stage-2 → Conversations | Modules/Conversations | FAIL: multi-type file (4 types) | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/DigitalBrain.Modules.UI/Chat/ChatTurnGoal.cs` | 1 | ChatTurnGoal | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/DigitalBrain.Modules.UI/Chat/ChatTurnWorker.cs` | 1 | ChatTurnWorker | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/DigitalBrain.Modules.UI/Diagram/DiagramNeuron.cs` | 1 | DiagramNeuron | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/DigitalBrain.Modules.UI/Surface/Surface.cs` | 1 | Surface | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/DigitalBrain.Modules.UI/Surface/SurfaceBoot.cs` | 1 | SurfaceBoot | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/DigitalBrain.Modules.UI/UiModule.cs` | 1 | UiModule | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/core/lib/digitalbrain_flutter.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/core/lib/src/behavior_client.dart` | 1 | BehaviorClient | OK |  | Acceptable | — | No cookie/session | Share auth transport |
| `src/Modules/UI/Flutter/core/lib/src/behavior_models.dart` | 10 | BehaviorLibraryItem, BehaviorLibraryDocument, BehaviorScenario, BehaviorBindi… | OK |  | FAIL: multi-type file (10 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/core/lib/src/cookie_http_client.dart` | 1 | CookieHttpClient | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/core/lib/src/host_environment.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/core/lib/src/process_environment_io.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/core/lib/src/process_environment_stub.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/core/lib/src/runtime_surface_io.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/core/lib/src/runtime_surface_web.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/core/lib/src/shell_surface.dart` | 2 | SceneViewModel, ShellSurfaceController | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/core/lib/src/sse_authorization_frames.dart` | 1 | SseAuthorizationParser | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/core/lib/src/sse_behavior_frames.dart` | 1 | SseBehaviorParser | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/core/lib/src/sse_chat_delta_frames.dart` | 1 | SseChatDeltaParser | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/core/lib/src/sse_chat_frames.dart` | 1 | SseChatTurnParser | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/core/lib/src/sse_frames.dart` | 1 | SseSceneOpenedParser | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/core/lib/src/sse_graph_frames.dart` | 1 | SseGraphChangeParser | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/core/lib/src/ui_client.dart` | 2 | DigitalBrainUiClient, AuthMe | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/core/lib/src/ui_models.dart` | 19 | OpenSceneRequest, ActivateControlRequest, SceneOpenedEvent, SendMessageReques… | OK |  | FAIL: model grab-bag | — | — | Split by domain |
| `src/Modules/UI/Flutter/kit/lib/digitalbrain_ui_kit.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/kit/lib/src/chat/kit_chat_builders.dart` | 1 | KitButtonPressed | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/Flutter/kit/lib/src/chat/kit_message_factory.dart` | 0 | — | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/Flutter/kit/lib/src/components/button/kit_button.dart` | 1 | KitButton | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/kit/lib/src/components/card/kit_card.dart` | 1 | KitCard | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/kit/lib/src/components/chart/kit_chart.dart` | 1 | KitChart | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/kit/lib/src/components/clock/kit_clock.dart` | 4 | KitClock, _KitClockState, _CountdownRingPainter, _WallClockPainter | OK |  | FAIL: multi-type file (4 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/kit/lib/src/components/graph/graph_geometry.dart` | 2 | ProjectedGraphNode, ProjectedGraphEdge | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/kit/lib/src/components/graph/graph_models.dart` | 4 | GraphNodeKind, GraphNode, GraphEdge, GraphPulse | OK |  | FAIL: multi-type file (4 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/kit/lib/src/components/graph/graph_painter.dart` | 1 | GraphPainter | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/kit/lib/src/components/graph/kit_graph.dart` | 2 | KitGraph, _KitGraphState | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/kit/lib/src/components/view/kit_view.dart` | 2 | KitView, _CalculatorPad | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/kit/lib/src/gallery/kit_gallery_screen.dart` | 1 | KitGalleryScreen | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/kit/lib/src/models/kit_part.dart` | 5 | KitTimerPart, KitButtonPart, KitChartPoint, KitChartPart, KitCardPart | OK |  | FAIL: multi-type file (5 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/kit/lib/src/theme/kit_theme.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/lib/activity_screen.dart` | 4 | ActivityScreen, _ActivityHeader, _EmptyActivity, _ActivityEntry | OK |  | FAIL: multi-type file (4 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/lib/behaviors/behavior_assistant_change.dart` | 2 | BehaviorAssistantChangeView, _BehaviorAssistantChangeViewState | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/lib/behaviors/behavior_demo_fixtures.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/lib/behaviors/behavior_evidence.dart` | 1 | BehaviorEvidencePanel | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/lib/behaviors/behavior_library.dart` | 4 | BehaviorLibraryView, _LibraryCard, _Pill, _MessageCard | OK |  | FAIL: multi-type file (4 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/lib/behaviors/behavior_overview.dart` | 4 | BehaviorOverviewView, _BindingRow, _Section, _MetaChip | OK |  | FAIL: multi-type file (4 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/lib/behaviors/behavior_revisions.dart` | 2 | BehaviorRevisionsView, _RevisionCard | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/lib/behaviors/behavior_scenarios.dart` | 2 | BehaviorScenariosView, _ScenarioCard | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/lib/behaviors/behavior_source.dart` | 4 | BehaviorSourceView, _BehaviorSourceViewState, _EditorPane, _Section | OK |  | FAIL: multi-type file (4 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/lib/behaviors/behavior_view_model.dart` | 2 | BehaviorStudioView, BehaviorStudioController | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/lib/behaviors/behavior_workspace.dart` | 3 | BehaviorWorkspace, _BehaviorWorkspaceState, _DetailChrome | OK |  | FAIL: multi-type file (3 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/lib/brain_screen.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/lib/brain_theme.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/lib/brain_topology_canvas.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/lib/brain/brain_inspector.dart` | 7 | TopologyExplorer, SelectionDetails, PulseDetails, ConnectionDetails, NeuronDe… | OK |  | FAIL: multi-type file (7 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/lib/brain/brain_panel.dart` | 3 | BrainMetricCard, BrainConnectionNotice, BrainInspectorField | OK |  | FAIL: multi-type file (3 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/lib/brain/brain_screen.dart` | 2 | BrainScreen, _BrainScreenState | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/lib/brain/topology_canvas.dart` | 1 | BrainTopologyCanvas | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/lib/brain/topology_graph.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/lib/brain/topology_selection.dart` | 4 | BrainModuleSelection, BrainNeuronSelection, BrainPulseSelection, BrainConnect… | OK |  | FAIL: multi-type file (4 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/lib/chat_screen.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/lib/chat/brain_chat_app.dart` | 1 | BrainChatApp | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/Flutter/shell/lib/chat/brain_chat_screen.dart` | 4 | BrainChatScreen, _BrainChatScreenState, SignInCardRail, SignInCard | Stage-2 → Conversations | Modules/Conversations | FAIL: multi-type file (4 types) | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/Flutter/shell/lib/chat/brain_workspace.dart` | 2 | BrainWorkspace, _BrainWorkspaceState | Stage-2 → Conversations | Modules/Conversations | FAIL: multi-type file (2 types) | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/Flutter/shell/lib/chat/chat_contracts.dart` | 8 | SendMessage, StreamMessage, LoadTopology, OpenUrl, ActivateChatButton, LoadBe… | Stage-2 → Conversations | Modules/Conversations | FAIL: multi-type file (8 types) | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/Flutter/shell/lib/chat/stream_state_store.dart` | 1 | StreamStateStore | Stage-2 → Conversations | Modules/Conversations | Acceptable | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/Flutter/shell/lib/chat/workspace_chrome.dart` | 4 | WorkspaceRail, WorkspaceNavigationBar, BrainMark, WorkspaceStatusBar | Stage-2 → Conversations | Modules/Conversations | FAIL: multi-type file (4 types) | Transcript + turn-log + queue (triple) | — | Extract Conversations |
| `src/Modules/UI/Flutter/shell/lib/kit/kit_chart.dart` | 4 | KitBarChart, KitLineChart, KitTimeChart, KitChartCard | Misnamed | shell/demo or delete vs kit | FAIL: multi-type file (4 types) | — | — | Rename Shell* demos |
| `src/Modules/UI/Flutter/shell/lib/kit/kit_chat.dart` | 2 | KitChatDemo, _KitChatDemoState | Misnamed | shell/demo or delete vs kit | FAIL: multi-type file (2 types) | — | — | Rename Shell* demos |
| `src/Modules/UI/Flutter/shell/lib/kit/kit_screen.dart` | 0 | — | Misnamed | shell/demo or delete vs kit | Acceptable | — | — | Rename Shell* demos |
| `src/Modules/UI/Flutter/shell/lib/main.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/lib/open_url_io.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/lib/open_url_web.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/lib/user_actions/user_action_card.dart` | 2 | UserActionCardModel, UserActionCard | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/lib/windowing/panel_manager.dart` | 4 | WindowPanelState, WindowPanel, WindowPanelKind, PanelManager | OK |  | FAIL: multi-type file (4 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/lib/windowing/windowing_screen.dart` | 14 | WindowingScreen, _WindowingScreenState, _CanvasBackdrop, _GridPainter, _Windo… | OK |  | FAIL: multi-type file (14 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/example/lib/main.dart` | 4 | Example, MyApp, ExampleCard, _ExampleCardState | OK |  | FAIL: multi-type file (4 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/_internal.dart` | 1 | Int32 | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/jni.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/accessors.dart` | 4 | JniResultMethods, JniIdLookupResultMethods, JniClassLookupResultMethods, JThr… | OK |  | FAIL: multi-type file (4 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/build_util/build_util.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/core_bindings.dart` | 48 | type, JBoolean, type, JByte, type, JCharacter, type, type, type, JCharacter, … | OK |  | FAIL: multi-type file (48 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/errors.dart` | 15 | _ExplainsRelease, UseAfterReleaseError, JNullError, NoSuchMethodError, Double… | OK |  | FAIL: multi-type file (15 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/jarray.dart` | 5 | _, type, _JArrayListView, JArrayToList, on | OK |  | FAIL: multi-type file (5 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/jclass.dart` | 6 | JClass, type, type, JInstanceMethodId, type, type | OK |  | FAIL: multi-type file (6 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/jimplementer.dart` | 1 | JImplementer | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/jni.dart` | 4 | ProtectedJniExtensions, InternalJniExtension, AdditionalEnvMethods, StringMet… | OK |  | FAIL: multi-type file (4 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/jobject.dart` | 4 | CastError, JObject, JThrowable, JObjectUseExtension | OK |  | FAIL: multi-type file (4 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/jprimitives.dart` | 9 | jbyteType, jbooleanType, jcharType, jshortType, jintType, jlongType, jfloatTy… | OK |  | FAIL: multi-type file (9 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/jreference.dart` | 4 | ProtectedJReference, _JFinalizable, JGlobalReference, _JNullReference | OK |  | FAIL: multi-type file (4 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/jvalues.dart` | 5 | JValueInt, JValueShort, JValueByte, JValueFloat, JValueChar | OK |  | FAIL: multi-type file (5 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/kotlin.dart` | 1 | KotlinContinuation | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/lang/jboolean.dart` | 1 | JBooleanExtension | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/lang/jbyte.dart` | 1 | JByteExtension | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/lang/jcharacter.dart` | 1 | JCharacterExtension | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/lang/jdouble.dart` | 1 | JDoubleExtension | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/lang/jfloat.dart` | 1 | JFloatExtension | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/lang/jinteger.dart` | 1 | JIntegerExtension | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/lang/jlong.dart` | 1 | JLongExtension | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/lang/jnumber.dart` | 4 | JNumberExtension, IntToJava, DoubleToJava, BoolToJava | OK |  | FAIL: multi-type file (4 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/lang/jshort.dart` | 1 | JShortExtension | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/lang/jstring.dart` | 2 | JStringExtension, ToJStringMethod | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/lang/lang.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/method_invocation.dart` | 1 | MethodInvocation | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/nio/jbuffer.dart` | 1 | type | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/nio/jbyte_buffer.dart` | 2 | type, Uint8ListToJava | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/nio/nio.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/primitive_jarrays.dart` | 32 | _, type, _JBooleanArrayListView, JBooleanArrayToList, _, type, _JByteArrayLis… | OK |  | FAIL: multi-type file (32 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/third_party/generated_bindings.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/third_party/global_env_extensions.dart` | 1 | GlobalJniEnv | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/third_party/jni_bindings_generated.dart` | 72 | JniBindings, CallbackResult, ConditionVariable, Dart_FinalizableHandle, Dart_… | OK |  | FAIL: multi-type file (72 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/types.dart` | 2 | JCallable, JAccessible | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/util/jiterator.dart` | 2 | JIteratorToAdapter, JIteratorAdapter | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/util/jlist.dart` | 3 | JListToAdapter, _JListAdapter, ToJavaList | OK |  | FAIL: multi-type file (3 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/util/jmap.dart` | 5 | JMapToAdapter, _JMapAdapter, _JMapKeySetAdapter, _JMapValueCollectionsAdapter… | OK |  | FAIL: multi-type file (5 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/util/jset.dart` | 3 | JSetToAdapter, _JSetAdapter, ToJavaSet | OK |  | FAIL: multi-type file (3 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/util/util.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/jni/lib/src/version_check.dart` | 1 | JniVersionCheck | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/path_provider_windows/example/lib/main.dart` | 2 | MyApp, _MyAppState | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/path_provider_windows/lib/path_provider_windows.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/path_provider_windows/lib/src/folders_stub.dart` | 1 | WindowsKnownFolder | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/path_provider_windows/lib/src/folders.dart` | 1 | WindowsKnownFolder | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/path_provider_windows/lib/src/guid.dart` | 0 | — | OK |  | Acceptable | — | — | Keep |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/path_provider_windows/lib/src/path_provider_windows_real.dart` | 2 | VersionInfoQuerier, PathProviderWindows | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/path_provider_windows/lib/src/path_provider_windows_stub.dart` | 2 | PathProviderWindows, VersionInfoQuerier | OK |  | FAIL: multi-type file (2 types) | — | — | Split 1 type/file |
| `src/Modules/UI/Flutter/shell/windows/flutter/ephemeral/.plugin_symlinks/path_provider_windows/lib/src/win32_wrappers.dart` | 29 | BOOL, BYTE, DWORD, UINT, HANDLE, HMODULE, HRESULT, LPCVOID, LPCWSTR, LPDWORD,… | OK |  | FAIL: multi-type file (29 types) | — | — | Split 1 type/file |


