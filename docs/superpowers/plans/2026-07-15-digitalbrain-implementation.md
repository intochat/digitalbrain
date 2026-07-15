# DigitalBrain Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make DigitalBrain a dependable product that learns new abilities with its user: the user asks for an outcome, DigitalBrain either completes it with an existing Feature or helps create one, proves it works, explains the access it needs, installs it safely, and returns to finish the original request. The complete experience must feel intentional, understandable, and trustworthy in Flutter while remaining dynamically programmable without a Flutter release for each new Feature.

**Architecture:** Flutter owns the trusted DigitalBrain shell, navigation, accessibility, core views, access review, and typed application state. Owner-scoped gRPC contracts expose Feature drafts, installed Features, Connections, Activity, automations, and Memory; backend-driven surfaces remain limited to Feature-specific forms, progress, artifacts, and results. Existing FeatureBuilder, FeatureHost, Orleans lifecycle grains, signed actions/effects, and provider isolation remain authoritative.

**Tech Stack:** .NET 11, Orleans, Aspire 13.4.6, gRPC/protobuf, Flutter >=3.44.0, Dart >=3.11.0, go_router 17.2.0, grpc 5.1.0, protobuf 6.0.0, FeatureBuilder, FeatureHost, Reqnroll, flutter_test, computer-use.

## Global Constraints

- Work in `E:\brain` on `master`; do not create a worktree unless the user explicitly changes this constraint.
- Preserve all unrelated dirty files and never discard user changes.
- Read `CLAUDE.md`, the two approved product specs, the capability-discovery plan, and this plan before implementation.
- Treat `docs/superpowers/specs/2026-07-15-digitalbrain-ubiquitous-language.md` as authoritative for bounded contexts, domain terms, commands, queries, invariants, Flutter copy, and new code names.
- Use CodeGraph before manual code exploration at the start of each subsystem.
- Use Context7 for current Flutter, go_router, gRPC, Aspire, Orleans, and framework-specific documentation. If Context7 is quota-blocked, record that fact and use repository-pinned APIs plus official primary documentation.
- Apply Elon's five steps in order at each product boundary: question, delete, simplify, accelerate feedback, automate last.
- Use TDD: smallest owning test project for red/green, then affected suites, then the exact root .NET test command before an integrable checkpoint.
- Keep `/features/proposals/:proposalId` as the canonical Studio route. Do not create a duplicate `/studio` route.
- Do not build empty destination pages. Add a destination only when it has a typed contract and a useful working view.
- Derive owner and actor identity from the authenticated session; never accept either from UI request bodies.
- Every mutation uses optimistic revision plus an idempotency identifier.
- Assistant output is a reviewable patch and never silently changes a draft.
- Verification binds an immutable release digest. Approval, grants, installation, and rollback operate on that exact digest.
- Installing a Feature never silently executes the original request. `Return to Chat · Run now` is an explicit user action.
- Flutter owns security-critical copy and grant/effect review. Backend surfaces cannot own global navigation, identity, or authority language.
- FeatureHost receives no provider credentials and cannot grant, approve, install, revoke, or bypass RuntimeHost capability authorization.
- Never log source bodies, original prompts, provider payloads, access tokens, refresh tokens, credentials, or sensitive Memory values.
- External-write acceptance uses synthetic or dedicated sandbox Connector accounts only. Never send real email or mutate real Salesforce data during automated proof.
- Product naming must be coherent: Home, Chat, Features, Connections, Activity, and Memory. Feature Studio uses Behavior, Suggested changes, Test results, Review access, Version, and Automation. Remove residual INO-as-a-destination copy and internal lifecycle terminology from normal UI.
- Follow pragmatic DDD boundaries: domain invariants and pure transitions in Kernel, application orchestration in responsibility-specific services, infrastructure in grains/builders/providers/transports, and presentation mapping in Flutter. Do not introduce generic repositories, event sourcing, or context-per-project structure without a proven need.
- A task is complete only after its focused tests pass, relevant Aspire resources are healthy, and its visible path has been inspected.
- The overall goal remains active until the terminal evidence gate in Task 15 is satisfied.

---

## Product Shape and Boundary

DigitalBrain has one product promise: ask for an outcome; if DigitalBrain does not know how yet, teach it once and continue the same task.

The visible model is deliberately small:

1. **Feature** — an ability DigitalBrain can use. Draft, installed, paused, and needs-attention are states, not separate products.
2. **Connection** — where DigitalBrain receives data or permission to act.
3. **Automation** — a schedule or event attached to a Feature, never a separate top-level product.
4. **Activity** — what DigitalBrain is doing or has done. Each item maps to a durable backend Run.
5. **Memory** — knowledge the user can inspect, correct, export, or forget.

Chat is the primary intent surface. Feature Studio is the place to teach and improve a Feature. Home is an exception-first summary, not a dashboard builder.

Flutter uses typed RPCs for core objects. Backend surfaces render only Feature-specific input controls, results, progress, and artifacts. Signed actions remain available for bounded dynamic interactions.

### Naming contract

| Flutter copy | Code/domain term | Rule |
|---|---|---|
| Feature draft | `FeatureDraft` | Migrate from the legacy `FeatureDraftProposal` name while preserving its serialized alias. Never show “proposal object” or lifecycle jargon after the draft opens. |
| Behavior / Scenario | `FeatureBehavior` / `FeatureScenario` | BDD and Gherkin are advanced technical details, not primary labels. |
| Suggested changes | `FeatureDraftPatch` | Suggestions never apply themselves. |
| Connection | `Connector` / `ProviderConnectionId` | UI describes the relationship; backend names the integration boundary. |
| Review access | `FeatureGrantSpec` / `FeatureGrantSnapshot` | Show plain-language access first and exact constraints on inspection. |
| Version | `FeatureReleaseMetadata` / `ReleaseDigest` | Show the exact digest under technical details. |
| Activity | `FeatureRunSnapshot` | A Run is the durable execution unit; Activity is the user-facing area. |
| Automation | `FeatureScheduleStatus` or event trigger binding | Automations live under their Feature. |

New code must use responsibility-specific names. Avoid new `Product*`, `*Rail`, `*Manager`, `*Helper`, `*Utils`, `*Data`, or `*Info` types when the class can be named for the object and operation it owns.

## Route Hierarchy

```text
/home
/chat
/chat/:conversationId
/features
/features/proposals/:proposalId
/features/:featureId
/features/:featureId/use
/features/:featureId/releases/:releaseDigest
/features/:featureId/triggers/:bindingId
/connections
/connections/:connectorId
/activity
/activity/:runId
/memory
/memory/:memoryItemId
```

Desktop uses persistent navigation. Medium layouts use compact navigation with accessible labels. Compact layouts use a drawer or bottom navigation with the same destinations. The global command layer is shell state, not a separate route.

## Planned File Boundaries

### Backend contracts and state

- Create `src/DigitalBrain.Kernel.Contracts/FeatureAuthoringContracts.cs` for draft, scenario, source, verification, and patch records.
- Create `src/DigitalBrain.Kernel.Contracts/FeatureRunContracts.cs` for Run identity, origin, status, and safe projections.
- Create `src/DigitalBrain.Kernel.Contracts/DigitalBrainQueryContracts.cs` for catalog, Connector, automation, Home, and Memory projections.
- Modify `src/DigitalBrain.Kernel.Contracts/FeatureGrainContracts.cs` only for grain method signatures and durable records that must remain colocated.
- Create `src/DigitalBrain.Kernel/Features/FeatureDraftAuthoringTransitions.cs` for pure revisioned authoring transitions.
- Create `src/DigitalBrain.Kernel/Capabilities/OwnerCapabilityCatalog.cs` for request-scoped platform, Connector, and Feature composition.
- Create `src/DigitalBrain.Kernel/Runtime/FeatureCapabilityInvoker.cs` for selected Feature invocation.
- Create `src/DigitalBrain.Kernel/Features/FeatureRunProjection.cs` for the common Run read model.

### Backend product API

- Create `src/DigitalBrain.Mcp/FeatureAuthoringService.cs` for draft editing, verification, and installation orchestration.
- Create `src/DigitalBrain.Mcp/FeatureSuggestionService.cs` for bounded structured suggested changes.
- Create `src/DigitalBrain.Mcp/DigitalBrainQueryService.cs` for Features, Connections, Activity, Memory, and Home queries.
- Create `src/DigitalBrain.Mcp/DigitalBrainUiEndpoints.cs` for typed gRPC mapping and exception/status conversion.
- Modify `src/DigitalBrain.Mcp/Protos/ui.proto`, `UiGrpcService.cs`, `UiHostingExtensions.cs`, and `Program.cs`.

### Flutter trusted product

- Create `app/lib/core/session/app_session_scope.dart` and `digitalbrain_client.dart`.
- Create `app/lib/shell/digitalbrain_shell.dart`, `main_destination.dart`, `adaptive_navigation.dart`, and `global_command.dart`.
- Create focused modules under `app/lib/features/home`, `chat`, `studio`, `features`, `connections`, `activity`, and `memory`.
- Keep backend surface parsing/rendering under `app/lib/runtime`; do not move product navigation into the surface protocol.
- Delete `app/lib/runtime/widgets/feature_proposal_placeholder.dart` after the typed Studio route is working.

## Core Interfaces

```csharp
public sealed record FeatureDraftId(string Value);

public sealed record OriginatingRequest(
    string OperationId,
    string ConversationId,
    string Text);

public sealed record FeatureScenario(
    string ScenarioId,
    string Name,
    string Given,
    string When,
    string Then);

public sealed record FeatureBehavior(FeatureScenario[] Scenarios);

public sealed record FeatureSourceFile(string Path, string Content);

public sealed record FeatureSourceSnapshot(
    string ImplementationProjectPath,
    string ScenarioProjectPath,
    FeatureSourceFile[] Files);

public sealed record FeatureVerification(
    ReleaseDigest Release,
    int Total,
    int Passed,
    int Failed,
    int Skipped,
    DateTimeOffset VerifiedAt);

public sealed record FeatureDraft(
    FeatureDraftId DraftId,
    OriginatingRequest OriginatingRequest,
    string Goal,
    string Status,
    FeatureBehavior Behavior,
    FeatureSourceSnapshot Source,
    FeatureVerification? Verification,
    FeatureInstallationId? InstallationId,
    long Revision,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public enum FeatureRunOrigin { Chat, Direct, Schedule, Event }
public enum FeatureRunStatus { Queued, Running, WaitingForApproval, Completed, Failed, Parked }

public sealed record FeatureRunSnapshot(
    string RunId,
    FeatureInstallationId InstallationId,
    ReleaseDigest Release,
    FeatureRunOrigin Origin,
    FeatureRunStatus Status,
    string CorrelationId,
    DateTimeOffset StartedAt,
    DateTimeOffset? CompletedAt,
    string? ResultSurfaceId,
    string? SafeFailure);
```

Typed UI methods added to `DigitalBrainV2Ui`:

```proto
rpc GetFeatureDraft(GetFeatureDraftRequest) returns (FeatureDraftReply);
rpc UpdateFeatureDraft(UpdateFeatureDraftRequest) returns (FeatureDraftReply);
rpc SuggestFeatureDraftPatch(SuggestFeatureDraftPatchRequest) returns (FeatureDraftPatchReply);
rpc VerifyFeatureDraft(VerifyFeatureDraftRequest) returns (FeatureReleaseReviewReply);
rpc InstallFeatureDraft(InstallFeatureDraftRequest) returns (FeatureInstallReply);
rpc ResumeFeatureRequest(ResumeFeatureRequestRequest) returns (ResumeFeatureRequestReply);
rpc ListFeatures(ListFeaturesRequest) returns (ListFeaturesReply);
rpc GetFeature(GetFeatureRequest) returns (FeatureReply);
rpc ListConnectors(ListConnectorsRequest) returns (ListConnectorsReply);
rpc GetConnector(GetConnectorRequest) returns (ConnectorReply);
rpc ListRuns(ListRunsRequest) returns (ListRunsReply);
rpc GetRun(GetRunRequest) returns (RunReply);
rpc ListMemory(ListMemoryRequest) returns (ListMemoryReply);
rpc GetMemoryItem(GetMemoryItemRequest) returns (MemoryItemReply);
rpc GetHome(GetHomeRequest) returns (HomeReply);
```

---

### Task 1: Establish Reproducible Baseline and Product Evidence

**Files:** No production changes. Test and diagnostic output only.

- [x] Re-read canonical docs and record current commit, dirty paths, package versions, and Aspire resource state.
- [x] Run `aspire doctor`; expected result is 5 passed, 0 warnings, 0 failures before product work begins.
- [x] Run `dotnet test --logger "console;verbosity=minimal"`; preserve the full failure list if baseline is not green.
- [x] Run `flutter analyze` and `flutter test` from `app`; preserve baseline failures separately from new failures.
- [x] Use computer-use to capture Login, Chat, the proposal placeholder, and the Chat return/session-loss behavior.
- [x] Do not change an unrelated dirty file to repair a baseline problem without first proving ownership and overlap.

### Task 2: Add the Feature Draft Aggregate

**Files:** Create and modify the backend contract/state files listed above. Test in `tests/DigitalBrain.OrleansTests/Features/FeatureDraftTransitionTests.cs` and a new `FeatureDraftAuthoringTests.cs`.

- [x] Write failing tests using the ubiquitous language for owner-local lookup, bounded Behavior/Source, invalid paths, stale Draft Revision, command replay, Verification invalidation, and installed-state immutability.
- [x] Add intent-specific `ReadDraftAsync`, `ReviseBehaviorAsync`, `ReviseSourceAsync`, `RecordVerificationAsync`, and `MarkDraftInstalledAsync` operations to `IFeatureHubGrain`; keep transport mapping out of domain types.
- [x] Replace the domain type name `FeatureDraftProposal` with `FeatureDraft` while preserving the existing Orleans serialization alias and stored-draft compatibility.
- [x] Extend `CreateFeatureDraft` with `ConversationId`; pass it from `AgentFrameworkWorkflowRunner`.
- [x] Seed one editable scenario and a valid bounded runtime-authored source snapshot.
- [x] Preserve all existing Orleans field IDs and add new IDs only.
- [x] Run the Orleans test project; expected result is all tests passing.
- [x] Commit only Task 2 files with `feat: add revisioned feature authoring state`.

### Task 3: Build Feature Authoring Services

**Files:** `FeatureAuthoringService.cs`, `FeatureSuggestionService.cs`, focused Orleans and integration-contract tests.

- [x] Write failing tests for cross-owner access, stale revisions, patch review, edit-after-verify, digest mismatch, grant mismatch, install replay, and no automatic execution.
- [x] Implement Suggested changes with `IChatClient` structured JSON output containing a replacement Behavior/Source patch and summary.
- [x] Make `AcceptSuggestedChange` an explicit revisioned domain command; rejection leaves the Feature Draft unchanged.
- [x] Verify only the stored source snapshot through `FeatureBuildEndpoint`.
- [x] Orchestrate propose, exact decision, grant, install, active publication, and draft-installed state with replay detection at every step.
- [x] Keep partial failures resumable without changing the verified digest.
- [x] Run Orleans, integration-contract, and FeatureBuilder E2E tests.
- [x] Commit with `feat: add feature studio orchestration`.

### Task 4: Expose Owner-Scoped Typed Product RPCs

**Files:** `ui.proto`, `DigitalBrainUiEndpoints.cs`, `UiGrpcService.cs`, hosting composition, generated Dart files, and `UiGrpcServiceTests.cs`.

- [x] Write failing tests for unauthenticated access, missing grants, owner isolation, malformed/oversize values, revision conflicts, replay, and safe error text.
- [x] Add the typed methods listed in Core Interfaces.
- [x] Map invalid input to `InvalidArgument`, missing objects to `NotFound`, stale revision to `Aborted`, missing authority to `PermissionDenied`, stale verification to `FailedPrecondition`, and temporary build/runtime loss to `Unavailable`.
- [x] Never include owner/actor fields in mutation messages.
- [x] Regenerate Dart protobuf clients with the repository-pinned protobuf toolchain.
- [x] Run backend tests plus Flutter analyzer.
- [x] Commit proto, server, tests, and generated clients atomically.

### Task 5: Lift Session Ownership Above Routing

**Files:** `app/lib/app.dart`, `router.dart`, product session files, `runtime_session_owner.dart`, `runtime_shell.dart`, `chat_page.dart`, and router/session tests.

- [x] Write a failing test for Login -> Chat -> Studio -> Chat with the same authenticated controller and surface state.
- [x] Write a failing deep-link test proving sign-in returns to the requested product route.
- [x] Create one app-lifetime `RuntimeSessionOwner` and expose it through `AppSessionScope`.
- [x] Separate authentication/loading/error gating from Chat surface rendering.
- [x] Make route pages consume the shared controller rather than create sessions.
- [x] Run router and runtime-shell tests; expected result is no reauthentication on product navigation.
- [x] Commit with `refactor: preserve product session across routes`.

### Task 6: Implement the Trusted Adaptive DigitalBrain Shell

**Files:** `digitalbrain_shell.dart`, `main_destination.dart`, `adaptive_navigation.dart`, router, navigation tests, and shell goldens.

- [x] Implement Home, Chat, Features, Connections, Activity, and Memory destinations only as their typed data becomes available in later tasks; keep unavailable destinations hidden until then.
- [x] Desktop: persistent labeled left rail. Medium: collapsed rail with accessible labels. Compact: drawer or bottom navigation.
- [x] Keep the active destination, deep link, back stack, keyboard focus, and screen-reader announcement coherent.
- [x] Add global sign-out and current product context without rendering internal owner identifiers.
- [x] Ensure backend widget-tree `app-shell` cannot replace or nest the trusted product navigation.
- [x] Run shell widget, semantics, and responsive golden tests.
- [x] Commit with `feat: add trusted adaptive digitalbrain shell`.

### Task 7: Replace the Proposal Dead End with Feature Studio

**Files:** `app/lib/features/studio/**`, Studio tests and goldens, removal of `feature_proposal_placeholder.dart`.

- [x] Implement the approved Studio canvas: origin request bar, section navigation, editable Behavior canvas, Suggested changes panel, Code & changes drawer, Test results, and one clear next action.
- [x] Autosave with one in-flight mutation, coalesced local edits, visible save state, and explicit conflict recovery.
- [x] Add `Ctrl+S`, `Ctrl+Enter`, Escape, logical focus order, text scaling, and screen-reader state announcements.
- [x] Show assistant changes as an addition/removal patch with Accept and Reject.
- [x] Disable Verify while dirty, saving, conflicted, or missing required source.
- [x] Adapt assistant and source/diff into modal/full-screen disclosures on compact layouts.
- [x] Delete the old placeholder only after route, load, and error tests pass.
- [x] Commit with `feat: replace proposal placeholder with living canvas studio`.

### Task 8: Complete Testing, Access Review, Installation, and Rollback

**Files:** Studio verification/grant widgets, controller tests, backend orchestration tests.

- [x] Present scenario totals, individual safe failures, artifacts, current source digest, and diff from the previous installed release.
- [x] Display every requested capability, provider connection, constraint summary, and trigger binding.
- [x] Use one explicit `Approve & install` action after exact review.
- [x] Clear Test results and Version review after any accepted code or Behavior edit.
- [x] Show retryable partial-install state without duplicating approvals or grants.
- [x] On success, show release identity, rollback availability, original request, and `Return to Chat · Run now`.
- [x] Add rollback from the Feature release detail and prove exact previous-release restoration.
- [x] Commit with `feat: complete governed feature installation`.

### Task 9: Make Installed Features Discoverable and Executable

**Files:** owner catalog, capability parameter model, workflow runner, invoker, capability tests.

- [x] Compose platform, healthy Connector, and active unpaused installed Feature descriptors for the authenticated owner on each resolution.
- [x] Use the same owner-scoped catalog for selection and parameter extraction.
- [x] Exclude paused, revoked, missing-connection, missing-grant, and wrong-owner Features.
- [x] Replace the current non-assistant acknowledgment with `FeatureCapabilityInvoker` for `CapabilityOrigin.Feature`.
- [x] Convert extracted arguments into one bounded Feature input and append it through the existing installation grain.
- [x] Keep external effects behind the existing signed approval rail.
- [x] Prove installation/update/pause/resume/rollback affects the next resolution without restarting Flutter, RuntimeHost, MCP, or FeatureHost.
- [x] Commit with `feat: execute installed features from capability discovery`.

### Task 10: Unify Activity Across Chat, Direct, Schedule, and Event Origins

**Files:** Run contracts/projection, `DigitalBrainQueryService`, Flutter Activity module, backend and Flutter tests.

- [ ] Use one stable Run ID and projection for all four origins.
- [ ] Project Queued, Running, WaitingForApproval, Completed, Failed, and Parked from durable installation state.
- [ ] Expose safe timing, attempts, Feature/release identity, authority state, result surface reference, and failure guidance.
- [ ] Build Activity list/detail, filters by status/origin/Feature, and links back to originating Chat/Feature/automation.
- [ ] Make technical detail progressively inspectable without exposing provider payloads or secrets.
- [ ] Prove a Feature update does not rewrite historical Run release identity.
- [ ] Commit with `feat: add unified product runs`.

### Task 11: Build Features Catalog, Direct Use, and Trigger Bindings

**Files:** Flutter Features module, trigger contracts/endpoints, tests and goldens.

- [ ] List installed, draft, paused, and needs-attention Features with purpose, health, active Version, Connections, Automations, and recent Activity.
- [ ] Add Feature detail, direct-use route, releases, pause/resume, rollback, and trigger bindings.
- [ ] Render Feature-specific direct-use inputs and results through bounded backend surfaces inside the trusted shell.
- [ ] Add schedule and event trigger creation/editing with exact authority review.
- [ ] Prove direct, scheduled, and event invocations produce the same Run view as Chat.
- [ ] Commit with `feat: add feature catalog and trigger management`.

### Task 12: Build Connections and Governed Memory

**Files:** typed Connector/Memory contracts, product query rail, Flutter modules, authorization and accessibility tests.

- [ ] List Connections by health and supplied ability, with access, dependent Features, Automations, and recent Activity.
- [ ] Keep OAuth and credential material entirely outside Flutter responses.
- [ ] Support connect/reconnect/test/revoke through trusted typed actions and existing Connector boundaries.
- [ ] List, inspect, correct, export, and forget Memory items with revision/ETag protection.
- [ ] Make Memory authority, source, usage, and promotion visible.
- [ ] Prove Connector revocation and Memory correction affect the next relevant capability operation.
- [ ] Commit Connector and Memory modules independently.

### Task 13: Build Operational Home and Global Command

**Files:** Home/command contracts, Flutter Home module, shell command component, tests and goldens.

- [ ] Home shows only actionable approval decisions, active work, completed outcomes, failures, and upcoming triggers.
- [ ] Keep routine history in Activity; do not create a customizable analytics dashboard.
- [ ] Global command operates in current product context and can expand into Chat with that context attached.
- [ ] Prove command expansion preserves destination state and conversation continuity.
- [ ] Commit with `feat: add operational home and global command`.

### Task 14: Product Polish, Naming, Accessibility, and Failure Recovery

**Files:** all Flutter modules as justified by observed issues; centralized copy/theme primitives; golden and semantics tests.

- [ ] Remove residual `INO` destination/role copy unless referring to an internal diagnostic unavailable to normal users.
- [ ] Use consistent nouns and verbs: Feature draft, Verify, Approve & install, Run now, Pause, Resume, Roll back.
- [ ] Add empty, loading, degraded, offline, conflict, expired-approval, partial-install, parked-Run, and disconnected-Connector states.
- [ ] Verify keyboard-only operation, focus restoration, screen-reader labels, 200% text scaling, high contrast, reduced motion, and touch targets.
- [ ] Verify layouts at 1440, 1024, 736, and 320 logical pixels.
- [ ] Add stable goldens for every core state and prevent dynamic timestamps/IDs from destabilizing screenshots.
- [ ] Conduct a product-quality review: hierarchy, density, naming, consistency, recovery, trust, and whether every visible action has a real outcome.
- [ ] Refactor only where it improves clear ownership, naming, testability, or user coherence; keep unrelated refactors out.

### Task 15: Ten-Feature Computer-Use Dogfood and Terminal Verification

**Files:** test fixtures or example prompts only when needed for deterministic sandbox proof. Store screenshots/log references under the repository's established test-artifact convention, not in production assets.

Create, edit, verify, install, invoke, and inspect these ten distinct Features through the running Flutter UI using computer-use. Do not seed them directly through MCP, grains, files, or test APIs; those may prepare sandbox Connector data but cannot substitute for user interaction.

| # | Feature | Capability/risk coverage | Required user proof |
|---|---|---|---|
| 1 | Company Research Brief | model/query, cited result | Missing request -> Studio -> install -> Chat Run |
| 2 | Inbox Triage Digest | Gmail read + model | Connector selection, read-only grant, direct Run |
| 3 | Customer Reply Draft | Gmail read + model | Behavior edit, Suggested changes review, Code & changes, draft result |
| 4 | Approved Email Follow-up | Gmail read/send external effect | exact send approval against sandbox mailbox |
| 5 | Salesforce Account Brief | Salesforce read + model | Connector health/dependency and Chat invocation |
| 6 | Opportunity Next-Step Advisor | Salesforce read + model | direct Feature form and Run inspection |
| 7 | Approved Opportunity Update | Salesforce write external effect | signed approval and sandbox read-after-write proof |
| 8 | Memory-Backed Meeting Prep | Memory recall + model | governed Memory dependency and cited Memory use |
| 9 | Decision Capture | Memory remember internal write | explicit authority, Memory item inspection/correction |
| 10 | Weekly Risk Digest | Salesforce read + model + schedule | trigger creation, scheduled Run, next-run visibility |

- [ ] For each Feature, begin from a natural unsupported request in Chat and confirm a durable proposal is created.
- [ ] Open Studio, make at least one meaningful Behavior edit, request and review Suggested changes, inspect Code & changes, and run the tests.
- [ ] Review the exact digest and grants, install, return to the originating request, and explicitly run when the origin is Chat.
- [ ] Inspect the resulting Run and verify its origin, Feature release, status, timing, result/failure, and navigation links.
- [ ] For Features 4 and 7, use only sandbox providers and prove the existing approval/effect rail; capture read-after-write evidence.
- [ ] For Feature 10, wait for or safely advance a sandbox schedule and prove it creates the same Run model.
- [ ] Update at least two installed Features, prove the next Run uses the new release without a Flutter release/restart, then roll one back and prove the following Run uses the retained previous release.
- [ ] Pause and resume one Feature; revoke one Connector; correct one Memory item; verify each change affects the next operation.
- [ ] Complete the entire suite using keyboard-only navigation for at least one Feature and compact layout for at least one different Feature.
- [ ] Capture screenshots for Login, shell/Home, Chat draft, Studio Behavior, Suggested changes, Code & changes, Test results, Review access, install success, each major Feature/Connection/Activity/Memory view, external-effect approval, and return-to-task.
- [ ] Inspect Aspire resources, targeted logs, and traces during the proof; confirm no secrets, source bodies, original prompts, or provider payloads leaked.
- [ ] Run from `app`: `flutter analyze` and `flutter test`; both must pass.
- [ ] Stop manually running Aspire before the root suite, then run `dotnet test --logger "console;verbosity=minimal"`; it must pass.
- [ ] Run `aspire doctor`; it must report 5 passed, 0 warnings, 0 failures.
- [ ] Run `git diff --check`; it must be clean.
- [ ] Review the final diff for unrelated dirty-file overlap and preserve user-owned changes.
- [ ] Do not mark the persistent goal complete until all ten UI journeys and every terminal verification above have current evidence.

## Completion Evidence

The implementation is complete only when all of the following are true:

1. All ten product UX acceptance criteria in `docs/superpowers/specs/2026-07-14-digitalbrain-capability-os-product-ux-design.md` pass.
2. Flutter provides a coherent adaptive shell with Home, Chat, Features, Connections, Activity, and Memory.
3. Feature Studio replaces the proposal dead end and supports the complete authoring, testing, access review, installation, and return-to-task journey.
4. Installed Features join owner-scoped discovery and execute safely from Chat, direct UI, schedules, and events.
5. Feature-specific inputs/results can evolve through backend surfaces without giving the backend control of trusted shell/governance UI.
6. Sessions and route state survive Chat, Studio, Run, and back navigation.
7. Authorization, revision, idempotency, exact-digest, pause/revoke, and effect-approval tests pass.
8. Responsive, accessibility, widget, router, state, and golden coverage passes.
9. The exact .NET root suite, Flutter analyzer/tests, Aspire doctor, and diff checks pass.
10. Computer-use has created and exercised the ten Features above through the visible Flutter product, with screenshots and runtime evidence.

## Execution Discipline

- Keep the persistent goal active across context compaction and continuation turns.
- At every checkpoint, state what is proven, what remains, and the next smallest testable slice.
- If a test fails, use systematic debugging before changing implementation.
- If design evidence invalidates an assumption, refine the spec and this plan, explain the decision, and continue; do not protect obsolete code or terminology.
- Safe refactoring and product-quality improvements are authorized when they directly improve this goal and remain covered by tests.
- Do not stop because one milestone works, because the UI looks plausible, because tests are mostly green, or because the context is long.
- Stop only for a genuine external blocker requiring new authority or unavailable credentials/sandbox infrastructure. Record the exact blocker and continue every other independent path first.
