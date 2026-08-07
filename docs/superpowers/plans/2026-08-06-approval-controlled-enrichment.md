# Approval-Controlled Enrichment Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (- [ ]) syntax for tracking.

**Goal:** Deliver account enrichment where chat or Gmail webhook input creates a frozen proposal and only its approved action binding can invoke the exact prepared Salesforce mutation.

**Architecture:** Core and Hosting already provide workspace-scoped physical identity, source-bound ingress, and durable direct delivery. Hosting first binds narrow provider interfaces to the active workspace while it constructs a behavior. Product modules then remain ordinary Core modules: Approvals owns frozen whole-proposal state, Time owns proposal deadline scheduling, Salesforce owns prepared/invoking/confirmed-or-uncertain mutation state, Memory is optional context, and Account Enrichment owns typed choreography.

**Tech Stack:** .NET 11, C#, Orleans 10 in Hosting only, System.Text.Json, xUnit v3, controlled provider fakes, and a separately opted-in Qdrant contract suite.

## Global constraints

- Keep workspace scope out of NeuronId, product synapses, product state, JSON payloads, and UI action payloads.
- A module references only Abstractions, Core, and other product modules; it never references Access, Hosting, Orleans, raw journal keys, or credentials.
- Keep WorkspaceChannel source-bound. Do not add a raw scope/source parameter to PublishAsync, ReadAsync, or a product action.
- Provider effects use an immutable idempotency identity or reconciliation. An unprovable provider outcome is OutcomeUncertain, never automatic success or a blind retry.
- Freeze proposal evidence, review fields, action binding, deadline, and execution target at first acceptance.
- Record only the approving actor for now; roles and permission policy are later Access work.
- Time is proposal-deadline-only, not generic scheduling. Memory failure is optional context and cannot alter a prepared Salesforce mutation.
- Test journals, output facts, and recorded fake-provider effects. Normal CI has no live Google/Salesforce credentials or Qdrant container.
- Do not commit, push, or alter Git history unless the user requests it.

## Implemented review correction: trusted approval ingress

- [x] Add deny-by-default `RegisterIngress<TSynapse>()` composition policy and a second, source-channel type capability check before any source record is written.
- [x] Expose only the source identity, sequence, and Hosting-stamped occurrence time as `Neuron.Origin`; it contains no physical workspace scope.
- [x] Route a public approval decision through an approval ingress behavior, which derives actor and decision time from `Origin` and directs a non-ingress internal decision to the frozen proposal.
- [x] Route deadline observation through the same pattern, so deadline time is also host-stamped rather than caller-provided.
- [x] Prove forged `ApprovalGranted`, unregistered ingress, capability-overreach, invalid enum input, and post-expiry approval attempts fail closed without a mutation grant.

## Flow

chat command or verified Gmail webhook
  -> enrichment run and typed evidence
  -> prepared Salesforce mutation
  -> frozen approval proposal
  -> pending + deadline scheduled
  -> approved | rejected | expired
  -> invoke exact prepared mutation
  -> confirmed | outcome uncertain

---

### Task 1: Workspace-bound module providers

**Files:**

- Create: src/DigitalBrain.Hosting/Composition/WorkspaceBinding.cs
- Create: src/DigitalBrain.Hosting/Composition/WorkspaceBindingHolder.cs
- Create: src/DigitalBrain.Hosting/Composition/WorkspaceServiceRegistration.cs
- Modify: src/DigitalBrain.Hosting/Composition/DigitalBrainComposition.cs
- Modify: src/DigitalBrain.Hosting/Composition/CompositionCatalog.cs
- Modify: src/DigitalBrain.Hosting/DigitalBrainSiloExtensions.cs
- Modify: src/DigitalBrain.Hosting/Runtime/NeuronHost.cs
- Modify: src/DigitalBrain.Testing/Fixture/DigitalBrainTestBuilder.cs
- Create: src/DigitalBrain.Testing.Mechanics/IWorkspaceMarker.cs
- Create: src/DigitalBrain.Testing.Mechanics/WorkspaceMarkerObserved.cs
- Create: src/DigitalBrain.Testing.Mechanics/WorkspaceMarkerProbe.cs
- Create: src/DigitalBrain.Core.Tests/Mechanics/WorkspaceServiceTests.cs

**Consumes:** private ScopeKey from the physical host and the existing per-turn behavior-construction seam.

**Produces:** trusted composition API RegisterWorkspaceService<TService>(Func<WorkspaceBinding, TService> factory), where TService is a class. WorkspaceBinding has an internal constructor and public Id string. It is available to trusted composition factories only; clean module assemblies cannot reference Hosting and therefore cannot construct or request it.

- [ ] **Step 1: Write the failing composed-host test**

Create WorkspaceServiceTests.WorkspaceBoundProviderChangesWithThePhysicalWorkspace. A clean WorkspaceMarkerProbe handles MechanicsStart, receives only IWorkspaceMarker, and emits WorkspaceMarkerObserved. Register a trusted factory that returns marker left for workspace/left and right for workspace/right; publish one source-bound fact to each workspace and assert the two journal outputs differ accordingly.

- [ ] **Step 2: Run the focused test and verify RED**

Run: dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore --filter-class DigitalBrain.WorkspaceServiceTests

Expected: the composition API and workspace-bound behavior service do not exist.

- [ ] **Step 3: Implement the narrow Hosting seam**

Store WorkspaceServiceRegistration in the sealed catalog and reject duplicate service contracts. Register a scoped holder and each configured service only on the silo container. In NeuronHost.ReceiveAsync, create a DI scope, bind its holder from private Scope, construct and run the behavior through that scope, then dispose it. Do not add these registrations to AddDigitalBrainSerialization.

- [ ] **Step 4: Verify green**

Run: dotnet test src/DigitalBrain.Core.Tests/DigitalBrain.Core.Tests.csproj --no-restore --filter-class DigitalBrain.WorkspaceServiceTests

Then run: dotnet test DigitalBrain.slnx --no-restore

### Task 2: Frozen whole-proposal Approvals

**Files:**

- Create: src/DigitalBrain.Product.Approvals/DigitalBrain.Product.Approvals.csproj
- Create: src/DigitalBrain.Product.Approvals/ApprovalEvidence.cs
- Create: src/DigitalBrain.Product.Approvals/ApprovalChange.cs
- Create: src/DigitalBrain.Product.Approvals/ApprovalActionBinding.cs
- Create: src/DigitalBrain.Product.Approvals/ApprovalProposal.cs
- Create: src/DigitalBrain.Product.Approvals/ApprovalFingerprint.cs
- Create: src/DigitalBrain.Product.Approvals/ApprovalDecision.cs
- Create: src/DigitalBrain.Product.Approvals/ApprovalStatus.cs
- Create: src/DigitalBrain.Product.Approvals/ApprovalProposed.cs
- Create: src/DigitalBrain.Product.Approvals/ApprovalDecisionSubmitted.cs
- Create: src/DigitalBrain.Product.Approvals/ApprovalDeadlineElapsed.cs
- Create: src/DigitalBrain.Product.Approvals/ApprovalPending.cs
- Create: src/DigitalBrain.Product.Approvals/ApprovalGranted.cs
- Create: src/DigitalBrain.Product.Approvals/ApprovalRejected.cs
- Create: src/DigitalBrain.Product.Approvals/ApprovalExpired.cs
- Create: src/DigitalBrain.Product.Approvals/ApprovalDecisionIgnored.cs
- Create: src/DigitalBrain.Product.Approvals/ApprovalState.cs
- Create: src/DigitalBrain.Product.Approvals/ApprovalNeuron.cs
- Create: src/DigitalBrain.Product.Tests/DigitalBrain.Product.Tests.csproj
- Create: src/DigitalBrain.Product.Tests/Approvals/WholeProposalApprovalTests.cs
- Modify: DigitalBrain.slnx

**Consumes:** direct receiver snapshots from Hosting.

**Produces:** ApprovalActionBinding(action kind, action id, action fingerprint, execution target) and public ApprovalDecisionSubmitted(proposal id, expected proposal fingerprint, decision id, decision). An approval ingress behavior derives actor and decision time from trusted source origin, then directs a non-ingress internal decision to the proposal neuron. ApprovalProposal copies ordered evidence/changes and computes its own fingerprint. ApprovalNeuron is keyed by proposal id. It stores the first proposal, produces ApprovalPending, and emits matching ApprovalGranted only with Dispatch.Direct to the proposal action execution target.

- [ ] **Step 1: Write failing acceptance tests**

Create a proposal with two evidence entries, two changes, a fixed deadline, and test execution target. Assert ApprovalPending serializes the exact ordered review content and computed fingerprint; assert no grant exists before a decision. Submit a matching decision and assert exactly one ApprovalGranted is directed to the declared target and carries the original frozen proposal. In a second test submit a bad fingerprint, duplicate decision, and matching elapsed-deadline fact; assert ignored/expired outcomes and no extra grant.

- [ ] **Step 2: Run RED**

Run: dotnet test src/DigitalBrain.Product.Tests/DigitalBrain.Product.Tests.csproj --no-restore --filter-class DigitalBrain.Product.Tests.Approvals.WholeProposalApprovalTests

- [ ] **Step 3: Implement the state machine**

Reject blank ids/review text/action ids/fingerprints, default deadlines, and default execution targets at construction. Use a parameterless ApprovalState holding proposal, status, decision id, actor, and time. Require every inbound proposal/decision/deadline id to equal Id.Name; preserve state for invalid input and emit a stable ignored reason.

- [ ] **Step 4: Verify green**

Run the focused approval command, then dotnet test DigitalBrain.slnx --no-restore.

### Task 3: Proposal-deadline-only Time

**Files:**

- Create: src/DigitalBrain.Product.Time/DigitalBrain.Product.Time.csproj
- Create: src/DigitalBrain.Product.Time/ProposalDeadline.cs
- Create: src/DigitalBrain.Product.Time/IProposalDeadlineScheduler.cs
- Create: src/DigitalBrain.Product.Time/ProposalDeadlineArmed.cs
- Create: src/DigitalBrain.Product.Time/ProposalDeadlineNeuron.cs
- Create: src/DigitalBrain.Product.Tests/Time/ProposalDeadlineTests.cs
- Modify: DigitalBrain.slnx

**Produces:** IProposalDeadlineScheduler.ScheduleAsync(ProposalDeadline, CancellationToken) and ProposalDeadline(proposal id, proposal fingerprint, due at).

- [ ] **Step 1: Write failing Time tests**

Use a recording scheduler fake. After ApprovalPending, assert one deadline with proposal id/fingerprint/due time. Publish a too-early elapsed fact and assert no expiry; publish matching elapsed at/after deadline and assert one ApprovalExpired; publish a duplicate and assert no second expiry or grant.

- [ ] **Step 2: Run RED, implement, verify**

Run: dotnet test src/DigitalBrain.Product.Tests/DigitalBrain.Product.Tests.csproj --no-restore --filter-class DigitalBrain.Product.Tests.Time.ProposalDeadlineTests

Implement ProposalDeadlineNeuron as an ApprovalPending listener which calls an idempotent scheduler keyed by proposal id plus fingerprint and produces ProposalDeadlineArmed. The real adapter later publishes ApprovalDeadlineElapsed through a scheduler-only trusted workspace channel; its observed time comes from Hosting origin. Re-run the focused test and the approval tests.

### Task 4: Exact Salesforce execution

**Files:**

- Create: src/DigitalBrain.Product.Salesforce/DigitalBrain.Product.Salesforce.csproj
- Create: src/DigitalBrain.Product.Salesforce/PreparedAccountDescriptionMutation.cs
- Create: src/DigitalBrain.Product.Salesforce/SalesforceMutationFingerprint.cs
- Create: src/DigitalBrain.Product.Salesforce/PreparedSalesforceMutation.cs
- Create: src/DigitalBrain.Product.Salesforce/SalesforceInvocationRequested.cs
- Create: src/DigitalBrain.Product.Salesforce/SalesforceChangeConfirmed.cs
- Create: src/DigitalBrain.Product.Salesforce/SalesforceChangeOutcomeUncertain.cs
- Create: src/DigitalBrain.Product.Salesforce/ISalesforceGateway.cs
- Create: src/DigitalBrain.Product.Salesforce/SalesforceGatewayOutcome.cs
- Create: src/DigitalBrain.Product.Salesforce/SalesforceMutationState.cs
- Create: src/DigitalBrain.Product.Salesforce/SalesforceMutationNeuron.cs
- Create: src/DigitalBrain.Product.Salesforce/SalesforceEffectNeuron.cs
- Create: src/DigitalBrain.Product.Tests/Salesforce/ApprovedMutationTests.cs
- Modify: DigitalBrain.slnx

**Produces:** ISalesforceGateway.ApplyOrReconcileAsync(PreparedAccountDescriptionMutation, CancellationToken).

- [ ] **Step 1: Write failing mutation tests**

Use a controlled gateway fake. Assert a prepared mutation creates no gateway call before approval; a matching grant invokes exactly the stored account id/description/fingerprint; a mismatching fingerprint reaches neither the effect behavior nor fake; an uncertain fake result produces SalesforceChangeOutcomeUncertain, not confirmation.

- [ ] **Step 2: Run RED, implement, verify**

Run: dotnet test src/DigitalBrain.Product.Tests/DigitalBrain.Product.Tests.csproj --no-restore --filter-class DigitalBrain.Product.Tests.Salesforce.ApprovedMutationTests

Store a prepared mutation before accepting a grant. Compare action id and fingerprint with stored state, record SalesforceInvocationRequested, then direct it to an effect behavior. The bound gateway uses immutable mutation id as idempotency identity and returns confirmed or uncertain. Re-run the focused command and dotnet test DigitalBrain.slnx --no-restore.

### Task 5: Typed optional Memory

**Files:**

- Create: src/DigitalBrain.Product.Memory/DigitalBrain.Product.Memory.csproj
- Create: src/DigitalBrain.Product.Memory/MemoryEntry.cs
- Create: src/DigitalBrain.Product.Memory/MemoryQuery.cs
- Create: src/DigitalBrain.Product.Memory/MemoryHit.cs
- Create: src/DigitalBrain.Product.Memory/MemoryStoreResult.cs
- Create: src/DigitalBrain.Product.Memory/IMemoryStore.cs
- Create: src/DigitalBrain.Product.Memory/MemoryStoreRequested.cs
- Create: src/DigitalBrain.Product.Memory/MemorySearchRequested.cs
- Create: src/DigitalBrain.Product.Memory/MemoryRemoveRequested.cs
- Create: src/DigitalBrain.Product.Memory/MemorySearchCompleted.cs
- Create: src/DigitalBrain.Product.Memory/MemoryUnavailable.cs
- Create: src/DigitalBrain.Product.Memory/MemoryNeuron.cs
- Create: src/DigitalBrain.Product.Tests/Memory/MemoryContractTests.cs
- Create: src/DigitalBrain.Product.Memory.Qdrant/DigitalBrain.Product.Memory.Qdrant.csproj
- Create: src/DigitalBrain.Product.Memory.Qdrant/QdrantMemoryStore.cs
- Create: src/DigitalBrain.Product.Memory.Qdrant.Tests/DigitalBrain.Product.Memory.Qdrant.Tests.csproj
- Create: src/DigitalBrain.Product.Memory.Qdrant.Tests/QdrantMemoryStoreContractTests.cs
- Modify: DigitalBrain.slnx

**Produces:** IMemoryStore with StoreAsync, SearchAsync, and RemoveAsync typed operations.

- [ ] **Step 1: Write fake-store and optionality tests**

Assert immutable results, stable metadata filtering, idempotent removal, and MemoryUnavailable on provider error. In enrichment composition, make Memory unavailable and assert Gmail/web evidence produces the same prepared mutation and approval proposal.

- [ ] **Step 2: Implement and verify**

Implement the fake contract and MemoryNeuron; catch provider errors only to emit MemoryUnavailable. Register the store through workspace-service composition. Add an explicit Qdrant suite covering store/search/filter/remove and cross-workspace isolation. Normal test runs stay container-free; run the Qdrant suite only with --explicit.

### Task 6: Enrichment, reusable webhooks, conversation, and Base UI Kit semantics

**Files:**

- Create: src/DigitalBrain.Product.Enrichment/DigitalBrain.Product.Enrichment.csproj
- Create: src/DigitalBrain.Product.Enrichment/AccountEnrichmentStarted.cs
- Create: src/DigitalBrain.Product.Enrichment/GmailEvidenceCollected.cs
- Create: src/DigitalBrain.Product.Enrichment/WebEvidenceCollected.cs
- Create: src/DigitalBrain.Product.Enrichment/AccountEnrichmentState.cs
- Create: src/DigitalBrain.Product.Enrichment/AccountEnrichmentNeuron.cs
- Create: src/DigitalBrain.Product.Conversation/DigitalBrain.Product.Conversation.csproj
- Create: src/DigitalBrain.Product.Conversation/ChatEnrichmentRequested.cs
- Create: src/DigitalBrain.Product.Conversation/ConversationIngressNeuron.cs
- Create: src/DigitalBrain.Product.Webhooks/DigitalBrain.Product.Webhooks.csproj
- Create: src/DigitalBrain.Product.Webhooks/WebhookDeliveryAccepted.cs
- Create: src/DigitalBrain.Product.Webhooks/WebhookDeliveryDuplicate.cs
- Create: src/DigitalBrain.Product.Webhooks/GmailMessageObserved.cs
- Create: src/DigitalBrain.Product.Webhooks/WebhookIngressNeuron.cs
- Create: src/DigitalBrain.Product.Presentation/DigitalBrain.Product.Presentation.csproj
- Create: src/DigitalBrain.Product.Presentation/ApprovalReviewSurfaceRequested.cs
- Create: src/DigitalBrain.Product.Presentation/ApprovalInboxItemChanged.cs
- Create: src/DigitalBrain.Product.Tests/Enrichment/AccountEnrichmentAcceptanceTests.cs
- Modify: DigitalBrain.slnx

- [ ] **Step 1: Write failing end-to-end tests**

Compose controlled Gmail, web-research, Memory, Time, and Salesforce adapters. Prove chat and one verified/deduplicated Gmail webhook both reach typed run choreography; prove evidence and exact prepared mutation appear in the frozen proposal; prove zero Salesforce effects before approval; prove confirmation yields EnrichmentCompleted and uncertainty yields EnrichmentOutcomeUncertain. A duplicate provider delivery id must create one run and one pending inbox item. ApprovalPending must yield declarative review-surface/inbox facts without scope, credential, or renderer-authored mutation.

- [ ] **Step 2: Implement and verify full slice**

Key the enrichment behavior by typed run id; use direct delivery for provider/approval transitions and keep typed run ids in facts for audit. Webhooks accepts an already verified subscription-bound delivery from trusted HTTP edge and owns provider delivery deduplication. Presentation only emits Base UI Kit semantic surfaces for card/drawer/inbox choices; it never decides or invokes approval/Salesforce work.

Run: dotnet test src/DigitalBrain.Product.Tests/DigitalBrain.Product.Tests.csproj --no-restore

Then run: dotnet test DigitalBrain.slnx --no-restore

Then run: git diff --check

Then run: git status --short

## Self-review

- Task 1 closes the provider tenant-binding gap before Gmail, Salesforce, Time, or Memory adapters enter a multi-workspace runtime.
- Tasks 2 and 4 cover frozen whole-proposal approval, exact action binding, stale/duplicate/expiry fencing, and no provider call before approval.
- Tasks 3 and 5 preserve the approved narrow Time and Memory boundaries without expanding Core into a scheduler or vector database.
- Task 6 covers chat, reusable webhook ingress, context-preserving dynamic Base UI Kit semantics, pending-inbox projection, and a complete enrichment acceptance path.
- Marketplace stays static trusted composition: every product module passes the existing assembly boundary and no task introduces untrusted in-process execution or runtime catalog activation.
