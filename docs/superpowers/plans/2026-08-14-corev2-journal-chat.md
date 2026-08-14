# CoreV2 Journal-First Chat Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the temporary CRUD product runtime with a durable journal-first CoreV2 brain that starts headlessly through Aspire and proves through MCP that an AI chat turn invokes Operations and produces live journal and BrainGraph updates.

**Architecture:** The existing Aspire/resource and ProductHost authority mechanics remain, but the production runtime is rebuilt around Orleans `DurableGrain`, a named Azure Blob journal store, durable Neuron turns, BrainGraph, and policy-safe Introspection projections. AI and UI become complete modules under their own folders. Flutter consumes chat, journal, and graph streams; Aspire defaults to the non-visual Dart host.

**Tech Stack:** .NET 11, Aspire 13.4/13.5 preview packages already pinned by the repository, Orleans 10.2 journaling with Azure Blob storage, Microsoft.Extensions.AI, Ollama `gemma4:12b`, ASP.NET Core HTTP/SSE/MCP, Dart, and Flutter.

## Global Constraints

- Work only in `E:\intochat\digitalbrain\.worktrees\corev2-product` on `codex/corev2-product`.
- Use the CoreV2 dictionary in `plans/COREV2-DICTIONARY.md`; V1 contract types never cross into CoreV2.
- AppHost declares resources only; RuntimeHost owns durable brain behavior; ProductHost is a stateless authenticated adapter.
- `journal` is a distinct required Aspire resource and connection, not an alias for `grainstate`.
- A Neuron turn writes state, semantic journal records, and staged outbox entries through one Orleans journaling commit.
- Mining reads journals and BrainGraph between turns and never mutates BrainGraph implicitly.
- Aspire defaults UI hosting to `headless`; `window` and `web` are explicit configuration choices.
- Each module owns `Contracts`, `Runtime`, optional `Aspire.Hosting`, Flutter presentation, and mirrored tests beneath its own folder.
- Every behavior change follows red-green-refactor and is committed as a small independently green slice.
- Start AppHost only with `aspire start --isolated --non-interactive`; use `aspire wait` before resource interaction.

---

### Task 1: Restore the required journal resource

**Files:**
- Modify: `tests/CoreV2/Brain.Aspire.Hosting.Tests/DigitalBrainResourceModelTests.cs`
- Modify: `tests/CoreV2/Brain.Aspire.Tests/DigitalBrainHostingRegistrationTests.cs`
- Modify: `src/CoreV2/Aspire/Brain.Aspire.Hosting/Brain/DigitalBrainNames.cs`
- Modify: `src/CoreV2/Aspire/Brain.Aspire.Hosting/Brain/DigitalBrainBuilder.cs`
- Modify: `src/CoreV2/Aspire/Brain.Aspire.Hosting/Brain/DigitalBrainHostingExtensions.cs`
- Modify: `src/CoreV2/Aspire/Brain.Aspire/DigitalBrainResourceNames.cs`
- Modify: `src/CoreV2/Aspire/Brain.Aspire/DigitalBrainRuntimeHostingExtensions.cs`
- Modify: `src/CoreV2/Aspire/Brain.Aspire/Brain.Aspire.csproj`

**Interfaces:**
- Produces: required Aspire resource and connection name `journal`.
- Produces: `AddDigitalBrainJournalStorage(ISiloBuilder, IConfiguration)` configured from the keyed `BlobServiceClient`.

- [ ] **Step 1: Write failing resource-model tests**

Assert that `AddDigitalBrain` creates a `journal` Azure Blob child, runtime references inject `ConnectionStrings__journal`, and startup waits include it. Assert runtime registration throws when the journal connection is absent.

- [ ] **Step 2: Run focused tests and verify RED**

Run both Aspire test executables. Expected failure: no resource/configuration named `journal` exists.

- [ ] **Step 3: Implement the named journal resource**

Add `storage.AddBlobs("journal")`, retain it on `DigitalBrainBuilder`, include it in required health dependencies, and reference it only from runtime resources. Register `Microsoft.Orleans.Journaling` and `Microsoft.Orleans.Journaling.AzureStorage`; resolve the Aspire-injected client and call `AddAzureBlobJournalStorage` with JSON journal format.

- [ ] **Step 4: Verify GREEN and commit**

Run focused tests and Release build, then commit `feat(aspire): require durable neuron journal storage`.

---

### Task 2: Make headless UI hosting the product default

**Files:**
- Modify: `tests/CoreV2/Brain.Aspire.Hosting.Tests/FlutterHostingTests.cs`
- Modify: `src/CoreV2/DigitalBrain.AppHost/AppHost.cs`
- Modify: `src/CoreV2/Modules/UI.Aspire.Hosting/ShellHostingExtensions.cs`
- Modify: `src/CoreV2/Modules/UI.Aspire.Hosting/ShellNames.cs`
- Create: `src/CoreV2/UI/Flutter/core/bin/digitalbrain_host.dart`
- Modify: `src/CoreV2/UI/Flutter/core/pubspec.yaml`
- Test: `src/CoreV2/UI/Flutter/core/test/headless_host_test.dart`

**Interfaces:**
- Consumes: `DigitalBrain:UI:HostKind` with `headless`, `window`, or `web`.
- Produces: default headless executable resource which waits for ProductHost and exits unhealthy when protocol bootstrap fails.

- [ ] **Step 1: Write failing C# and Dart tests**

Assert omitted host kind chooses `dart run bin/digitalbrain_host.dart`, invalid values fail during AppHost model construction, and bootstrap validates ProductHost health plus stream connectivity without Flutter bindings.

- [ ] **Step 2: Verify RED**

Run the focused Aspire hosting and Dart tests. Expected failures: AppHost selects window and the headless entry does not exist.

- [ ] **Step 3: Implement configuration selection and headless executable**

Keep `WithWindowHost` and `WithWebHost`; add one AppHost selection function defaulting to `WithHeadlessHost`. The Dart entry uses the pure client and stays alive while its ProductHost subscriptions remain connected.

- [ ] **Step 4: Verify GREEN and commit**

Run focused tests and commit `feat(ui): default Aspire to the headless client`.

---

### Task 3: Put modules in module-owned folders

**Files:**
- Modify: `tests/CoreV2/Brain.Architecture.Tests/ArchitectureTests.cs`
- Move: `src/CoreV2/Brain.Abstractions` -> `src/CoreV2/Kernel/Abstractions`
- Move: `src/CoreV2/Brain.Core` -> `src/CoreV2/Kernel/Runtime`
- Move: `src/CoreV2/Brain.Testing` -> `src/CoreV2/Kernel/Testing`
- Move: `src/CoreV2/Aspire/Brain.Aspire.Hosting` -> `src/CoreV2/Aspire/Hosting`
- Move: `src/CoreV2/Aspire/Brain.Aspire` -> `src/CoreV2/Aspire/Runtime`
- Move: `src/CoreV2/Aspire/Brain.ServiceDefaults` -> `src/CoreV2/Aspire/ServiceDefaults`
- Move: `src/CoreV2/DigitalBrain.AppHost` -> `src/CoreV2/Hosts/AppHost`
- Move: `src/CoreV2/DigitalBrain.RuntimeHost` -> `src/CoreV2/Hosts/RuntimeHost`
- Move: `src/CoreV2/DigitalBrain.ProductHost` -> `src/CoreV2/Hosts/ProductHost`
- Move: `src/CoreV2/Brain.Product.Abstractions` -> `src/CoreV2/Hosts/ProductHost.Contracts`
- Move every `src/CoreV2/Modules/<Name>.Contracts` into `src/CoreV2/Modules/<Name>/Contracts`
- Move every module runtime project into `src/CoreV2/Modules/<Name>/Runtime`
- Move: `src/CoreV2/Modules/UI.Aspire.Hosting` -> `src/CoreV2/Modules/UI/Aspire.Hosting`
- Move: `src/CoreV2/UI/Flutter` -> `src/CoreV2/Modules/UI/Flutter`
- Move existing tests into matching `tests/CoreV2/{Kernel,Aspire,Hosts,Modules}` folders
- Modify: `DigitalBrain.slnx`
- Modify: affected `ProjectReference` paths

**Interfaces:**
- Produces: module roots whose child projects are `Contracts`, `Runtime`, `Aspire.Hosting`, and `Flutter`.

- [ ] **Step 1: Write a failing architecture test**

Enumerate active projects and fail when kernel, host, Aspire, module, Flutter, or test projects sit outside their designated root from the approved design.

- [ ] **Step 2: Verify RED**

Run Architecture tests. Expected failure lists the current flat projects and module siblings.

- [ ] **Step 3: Perform mechanical moves and update project paths**

Perform path-only moves for every active project, including temporary modules that Task 10 later deletes. Keep namespaces and assembly names stable. Update solution/project references atomically so no compatibility project is introduced.

- [ ] **Step 4: Verify GREEN and commit**

Run Architecture tests and Release build, then commit `refactor: group CoreV2 projects by module`.

---

### Task 4: Define the production journal and brain read contracts

**Files:**
- Create: `src/CoreV2/Kernel/Abstractions/Journal/BrainJournalRecord.cs`
- Create: `src/CoreV2/Kernel/Abstractions/Journal/BrainJournalDirection.cs`
- Create: `src/CoreV2/Kernel/Abstractions/Journal/BrainJournalPage.cs`
- Create: `src/CoreV2/Kernel/Abstractions/Graph/BrainSnapshot.cs`
- Create: `src/CoreV2/Kernel/Abstractions/Graph/BrainNeuronView.cs`
- Create: `src/CoreV2/Kernel/Abstractions/Graph/BrainSynapseView.cs`
- Create: `src/CoreV2/Kernel/Abstractions/Runtime/IBrainRuntimeGrain.cs`
- Create: `src/CoreV2/Kernel/Abstractions/Runtime/IBrainActivityGrain.cs`
- Modify: `src/CoreV2/Kernel/Abstractions/Brain.Abstractions.csproj`
- Test: `tests/CoreV2/Kernel/Abstractions.Tests/JournalContractTests.cs`

**Interfaces:**
- Produces: typed, Orleans-serializable contracts for Operation invocation, chat turns, activity journal paging, and brain snapshots.
- Invariant: journal pages are workspace-scoped and monotonically sequenced; payloads are product-safe summaries, not arbitrary CLR serialization.

- [ ] **Step 1: Write failing contract tests**

Construct literal journal/graph fixtures, serialize through Orleans, and verify causal identity, sequence validation, and no credential-bearing fields.

- [ ] **Step 2: Verify RED**

Expected failure: the wished-for production contracts do not exist.

- [ ] **Step 3: Implement minimal immutable contracts**

Use existing strong IDs where they fit. Keep one public type per matching file and validate empty identities and non-positive sequences at construction.

- [ ] **Step 4: Verify GREEN and commit**

Commit `feat(core): define journal and brain projection contracts`.

---

### Task 5: Implement durable BrainGraph and activity journals

**Files:**
- Create: `src/CoreV2/Kernel/Runtime/Journaling/DurableBrainActivityGrain.cs`
- Create: `src/CoreV2/Kernel/Runtime/Journaling/BrainJournalEntry.cs`
- Create: `src/CoreV2/Kernel/Runtime/Graph/DurableBrainGraphGrain.cs`
- Create: `src/CoreV2/Kernel/Runtime/Graph/IBrainGraphGrain.cs`
- Create: `src/CoreV2/Kernel/Runtime/Graph/BrainGraphState.cs`
- Modify: `src/CoreV2/Kernel/Runtime/Brain.Core.csproj`
- Test: `tests/CoreV2/Kernel/Runtime.Tests/DurableJournalTests.cs`
- Test: `tests/CoreV2/Kernel/Runtime.Tests/DurableBrainGraphTests.cs`

**Interfaces:**
- `IBrainActivityGrain.AppendAsync(BrainJournalRecord)` deduplicates by firing/record identity and assigns sequence.
- `IBrainGraphGrain.InstallAsync`, `ReplaceAsync`, `RetireAsync`, and `SnapshotAsync` preserve Synapse history and expose a monotonic graph sequence.

- [ ] **Step 1: Write failing Orleans-cluster tests**

Prove append order, duplicate suppression, workspace isolation, Synapse revision preservation, and recovery after grain deactivation.

- [ ] **Step 2: Verify RED**

Expected failure: durable grains and contracts are absent.

- [ ] **Step 3: Implement with Orleans durable collections**

Derive from `DurableGrain`, inject keyed `IDurableList`/`IDurableDictionary` states, mutate all state for one call, and call `WriteStateAsync` once. Do not use `IPersistentState` for semantic journals.

- [ ] **Step 4: Verify GREEN and commit**

Commit `feat(core): persist activity journals and BrainGraph`.

---

### Task 6: Run Proof Operations through durable Neurons

**Files:**
- Create: `src/CoreV2/Modules/Proof/Contracts/WireProof.cs`
- Create: `src/CoreV2/Modules/Proof/Runtime/ProofEntryNeuron.cs`
- Create: `src/CoreV2/Modules/Proof/Runtime/IProofEntryNeuron.cs`
- Create: `src/CoreV2/Modules/Proof/Runtime/ProofAssessmentNeuron.cs`
- Create: `src/CoreV2/Kernel/Runtime/Runtime/BrainRuntimeGrain.cs`
- Modify: `src/CoreV2/Hosts/RuntimeHost/Program.cs`
- Test: `tests/CoreV2/Modules/Proof.Tests/ProductionProofFlowTests.cs`

**Interfaces:**
- Produces Operations `Proof.Wire@1` and `Proof.Run@1`.
- `Proof.Wire@1` installs or replaces the live `ProofProduced` Synapse to the assessment Neuron.
- `Proof.Run@1` direct-sends to the entry Neuron, which journals and emits `ProofProduced`; delivery journals the assessment receipt.

- [ ] **Step 1: Write failing distributed tests**

Invoke Wire then Run through `IBrainRuntimeGrain`. Assert one BrainActivity journal contains Operation ingress, Neuron firing, Synapse route, delivery, and result in causal order; assert graph snapshot contains the live Synapse and usage count.

- [ ] **Step 2: Verify RED**

Expected failure: the production runtime still dispatches to `IRuntimeProductModule`.

- [ ] **Step 3: Implement the minimal production path**

Use durable Neuron grains and the durable graph/activity grains. Route snapshots are fixed before source commit. Deliver after commit and append outcome records without changing the Synapse revision.

- [ ] **Step 4: Verify GREEN and commit**

Commit `feat(proof): execute operations through journaled neurons`.

---

### Task 7: Add Introspection and ProductHost journal/graph protocols

**Files:**
- Create: `src/CoreV2/Modules/Introspection/Contracts/BrainReadContracts.cs`
- Create: `src/CoreV2/Modules/Introspection/Runtime/BrainIntrospection.cs`
- Modify: `src/CoreV2/Hosts/ProductHost/Protocol/ProductProtocolEndpoints.cs`
- Modify: `src/CoreV2/Hosts/ProductHost/Mcp/ProductMcpTools.cs`
- Test: `tests/CoreV2/Modules/Introspection.Tests/BrainIntrospectionTests.cs`
- Test: `tests/CoreV2/Hosts/ProductHost.Tests/JournalAndGraphProtocolTests.cs`

**Interfaces:**
- Produces HTTP activity journal snapshot/SSE and brain snapshot/SSE endpoints from the design.
- Produces MCP tools `brain_chat`, `brain_activity_journal`, and `brain_snapshot` alongside Operation discovery/invocation.

- [ ] **Step 1: Write failing projection and endpoint tests**

Assert authorization, workspace filtering, SSE resume sequence, graph sequence, and MCP result shapes using real test-server endpoints.

- [ ] **Step 2: Verify RED**

Expected failure: journal/brain endpoints and MCP tools are absent.

- [ ] **Step 3: Implement polling-backed resumable streams**

Read authoritative grains, emit only records after `Last-Event-ID`/`afterSequence`, and stop on cancellation without affecting runtime work.

- [ ] **Step 4: Verify GREEN and commit**

Commit `feat(introspection): stream live journal and BrainGraph projections`.

---

### Task 8: Migrate the AI assistant as a real module

**Files:**
- Create: `src/CoreV2/Modules/AI/Contracts/Brain.Modules.AI.Contracts.csproj`
- Create: `src/CoreV2/Modules/AI/Contracts/AssistantContracts.cs`
- Create: `src/CoreV2/Modules/AI/Runtime/Brain.Modules.AI.csproj`
- Create: `src/CoreV2/Modules/AI/Runtime/AssistantNeuron.cs`
- Create: `src/CoreV2/Modules/AI/Runtime/AssistantTools.cs`
- Create: `src/CoreV2/Modules/AI/Runtime/AIHosting.cs`
- Create: `src/CoreV2/Modules/AI/Aspire.Hosting/Brain.Modules.AI.Aspire.Hosting.csproj`
- Create: `src/CoreV2/Modules/AI/Aspire.Hosting/AIHostingExtensions.cs`
- Modify: `src/CoreV2/Hosts/AppHost/AppHost.cs`
- Modify: `src/CoreV2/Hosts/RuntimeHost/Program.cs`
- Test: `tests/CoreV2/Modules/AI.Tests/AssistantOperationTests.cs`

**Interfaces:**
- `IAssistantNeuron.SendAsync` accepts a verified chat turn and streams/project a terminal assistant response.
- `IAssistantChatModel` is the narrow provider seam; production binds Ollama `gemma4:12b`, tests bind a deterministic tool-calling adapter.
- `AssistantTools` exposes Operation discovery and invocation through `IBrainRuntimeGrain`; it does not address arbitrary grains.

- [ ] **Step 1: Write failing deterministic assistant test**

The deterministic model requests `Proof.Wire@1` then `Proof.Run@1`. Assert the assistant invokes both through the real runtime interface and journals tool selection, invocation, typed proof flow, and terminal response under one BrainActivity.

- [ ] **Step 2: Verify RED**

Expected failure: the AI module does not exist in CoreV2.

- [ ] **Step 3: Implement AI runtime and Aspire Ollama projection**

Port the useful provider/client behavior from master while replacing V1 Synapse and grain contracts. Configure persistent Ollama, GPU support when available, `gemma4:12b`, endpoint/model injection, and runtime health ordering.

- [ ] **Step 4: Verify GREEN and commit**

Commit `feat(ai): drive CoreV2 operations from the assistant`.

---

### Task 9: Build the real UI module and Flutter chat/graph/journal experience

**Files:**
- Create: `src/CoreV2/Modules/UI/Contracts/ChatContracts.cs`
- Replace: `src/CoreV2/Modules/UI/Runtime/UiModule.cs`
- Create: `src/CoreV2/Modules/UI/Runtime/ChatNeuron.cs`
- Move/adapt from master into: `src/CoreV2/Modules/UI/Flutter/kit/lib/src/components/graph/*`
- Create: `src/CoreV2/Modules/UI/Flutter/kit/lib/src/components/journal/*`
- Modify: `src/CoreV2/Modules/UI/Flutter/core/lib/src/product_client.dart`
- Create: `src/CoreV2/Modules/UI/Flutter/core/lib/src/brain_models.dart`
- Replace: `src/CoreV2/Modules/UI/Flutter/shell/lib/main.dart`
- Test: UI module .NET tests plus Dart reducer tests and Flutter widget tests.

**Interfaces:**
- `Chat.Send@1` appends the user turn, asks `AssistantNeuron`, and returns/streams the journalled response.
- Flutter core exposes chat, journal, brain snapshot, and SSE subscriptions.
- Flutter shell coordinates transcript selection, graph selection, journal filters, and live pulses.

- [ ] **Step 1: Write failing runtime, reducer, and widget tests**

Assert Chat delegates to AI through its typed contract, graph SSE installs an edge without refresh, journal SSE appends in sequence, selection filters coordinate, and the main screen contains transcript, graph, journal, and composer.

- [ ] **Step 2: Verify RED**

Expected failures: `UiModule` is empty, Flutter has no kit package, and the streams/models do not exist.

- [ ] **Step 3: Implement the module and UI**

Port only the master graph geometry/painter interaction. Bind it to CoreV2 projection models. Split shell files by chat, brain, journal, and workspace responsibilities rather than retaining one monolithic `main.dart`.

- [ ] **Step 4: Verify GREEN and commit**

Commit `feat(ui): show journal-driven chat and live BrainGraph`.

---

### Task 10: Remove the parallel runtime and temporary CRUD modules

**Files:**
- Delete: `src/CoreV2/Brain.Runtime*`
- Delete: temporary `Conversation`, `Scheduling`, `Behavior`, and `Memory` CoreV2 product projects
- Delete: corresponding temporary tests
- Modify: `DigitalBrain.slnx`
- Modify: active project references and RuntimeHost registrations
- Modify: `tests/CoreV2/Brain.Architecture.Tests/ArchitectureTests.cs`

**Interfaces:**
- Produces: one active production runtime path with no `IRuntimeProductModule`, `ProductActivityGrain`, or module-local CRUD state.

- [ ] **Step 1: Strengthen architecture tests and verify RED**

Fail when active source contains the parallel runtime interfaces or flat module projects.

- [ ] **Step 2: Delete obsolete projects and references**

Delete only after Tasks 6–9 are green. Preserve V1 source as an uncompiled reference until the user separately authorizes physical removal.

- [ ] **Step 3: Run full build/test/analyzer matrix**

Expected: zero warnings/errors and all .NET, Dart, and Flutter tests green.

- [ ] **Step 4: Commit**

Commit `refactor: cut over to the journal-first CoreV2 runtime`.

---

### Task 11: Prove the product through live headless Aspire and MCP

**Files:**
- Create: `scripts/accept-corev2-journal-chat.ps1`
- Modify: `status.md`
- Modify: `README.md`
- Modify: `CLAUDE.md`

**Interfaces:**
- Produces: repeatable acceptance evidence using only Aspire resource commands and ProductHost MCP/HTTP adapters.

- [ ] **Step 1: Start isolated Aspire and wait for every required resource**

Run `aspire start --isolated --non-interactive`, then wait for storage, journal, runtime, product, Ollama/model, and headless UI. Verify no Flutter window process/resource is present.

- [ ] **Step 2: Execute the MCP scenario**

Capture the initial brain snapshot. Through MCP, send: “Wire Proof to assessment, run value journal-live, and tell me the route.” Wait for the assistant terminal response.

- [ ] **Step 3: Verify causal journal and graph evidence**

Through MCP, read the activity journal and brain snapshot. Require records for user chat, assistant tool choice, `Proof.Wire@1`, `Proof.Run@1`, `ProofProduced`, Synapse delivery, assessment, and assistant response. Require a new live Synapse with usage greater than zero.

- [ ] **Step 4: Verify stream and restart recovery**

Observe journal/brain SSE sequence advancement, restart RuntimeHost, wait healthy, and re-read the same chat/activity/Synapse revision.

- [ ] **Step 5: Verify optional window UI from the recorded fixture**

Run Dart/Flutter analyzer and tests, including the complete chat/graph/journal screen and live-update reducers. Window launch remains opt-in and is not used by automated acceptance.

- [ ] **Step 6: Stop Aspire, update status, and commit**

Require `aspire ps --format json` to return `[]`, update status with exact evidence, and commit `docs: record journal-first product acceptance`.
