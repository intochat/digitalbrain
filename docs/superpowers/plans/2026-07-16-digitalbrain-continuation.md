# DigitalBrain Continuation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Safely complete the DigitalBrain product from checkpoint `5568748a`, preserving the dedicated Flutter Feature Studio while finishing durable Activity/effect semantics, the remaining product areas, product-wide quality, and ten visible dogfood journeys.

**Architecture:** Retain the north-star path `Client -> Edge/Auth -> INO operation -> deterministic function or bounded model workflow -> effect gate -> connector adapter`. Close external effects through one durable terminal decision before projecting a Run, keep Flutter responsible for trusted navigation and governance, and expose owner-scoped typed queries and commands for core product objects. Feature-specific inputs, results, progress, and artifacts remain dynamically backend-driven inside the trusted Flutter shell.

**Tech Stack:** .NET 11 preview, Orleans 10.2.1, Aspire CLI 13.4.6 with AppHost packages currently resolving to 13.5.0-preview.1.26363.3, gRPC/protobuf, Flutter 3.44.6, Dart 3.12.2, go_router 17.2.0, flutter_test, xUnit, and computer-use.

## Global Constraints

- The canonical plan `docs/superpowers/plans/2026-07-15-digitalbrain-implementation.md` remains unchanged and is the historical source for Tasks 1–15.
- Work on `master` in the existing checkout unless the user explicitly changes that constraint.
- Do not replace Feature Studio with Chat, a generic server-rendered page, or another authoring route.
- `/features/proposals/:proposalId` remains the canonical compatibility route and Flutter calls the object a Draft.
- Owner and actor identity come only from the authenticated session.
- Every mutation is revisioned and idempotent; every external effect is exact, signed, actor-bound, and terminally resolved before its Run is called complete.
- Preserve Orleans aliases and field IDs. Additive state migration must read checkpoint state without rewriting history.
- Never retain or expose provider payloads after an effect reaches a terminal state. Never log source bodies, original prompts, credentials, tokens, provider payloads, or sensitive Memory values.
- Automated external writes use only dedicated Gmail and Salesforce sandboxes.
- During TDD run the smallest owning test file/project without `--filter`; before each integrable checkpoint run the exact root .NET command.
- Context7 was quota-blocked during this assessment. At implementation time retry it first, then use pinned APIs and official primary documentation if still blocked.
- Do not complete the persistent product goal until Task 15 and every terminal gate have current evidence.

---

## Answer-First Current State

The repository is exactly at the requested checkpoint: clean `master`, HEAD `5568748a137e977fc50f267aa9075a8a88881cf0`, three commits ahead of `origin/master`, with no divergence. Tasks 2, 3, 5, and 6 are supported by current focused evidence. Tasks 7–9 are substantially implemented but lack a green broad gate and live proof. Task 4 is only partial: its proto declares Features, Connections, Memory, and Home RPCs, but `UiGrpcService` does not override those methods. Task 10 has a substantial Run/Activity implementation, but its external-effect lifecycle is unsafe and Activity does not load the exact Chat context. Tasks 11–13 are mostly absent, Task 14 is partial and dependency-blocked, and Task 15 has no visible-journey evidence.

Feature Studio is real and must be preserved. It already owns Behavior, Suggested changes, Code & changes, Test results, access review, installation success, rollback handoff, responsive disclosures, keyboard behavior, semantics, and return-to-task actions. Its genuine remaining work is test-harness stabilization, live visual proof, integration with the catalog/update/automation flows, and product-wide polish—not replacement.

The next product change must be Task 10 effect safety. A Salesforce decline currently closes only the Feature intent, not the signed effect plan. The same plan can therefore still execute after a later or racing approval. Run status also depends on prunable intents, resolved payloads remain retained, non-success provider outcomes can become Completed, and outcome replay can conflict because a stable input identity carries a changing timestamp.

## Continuation Strategies Considered

| Strategy | Benefit | Cost | Decision |
|---|---|---|---|
| Safety-first vertical slices | Removes the only known path to an external action after decline and gives later UI a trustworthy Run model. | Product breadth advances later. | **Selected.** |
| UI-first completion of Tasks 11–13 | Produces visible breadth quickly. | Builds screens on unsafe and unstable Run/effect semantics. | Rejected. |
| Gate-only cleanup first | Makes CI green before domain work. | Does not reduce external-effect risk. | Use only as a short prerequisite; do not let it displace Task 10. |

Elon's five steps apply as follows: question the unchecked plan claims; delete accidental generated changes and duplicate outcome facts; simplify around one terminal effect resolution; shorten feedback with focused transition tests; automate dogfood only after safety and product flows are stable.

## Verified Evidence

All current commands were run on 2026-07-16 in Europe/Prague.

| Command / inspection | Current result | Confidence |
|---|---|---|
| `git rev-parse HEAD` | `5568748a137e977fc50f267aa9075a8a88881cf0` | High |
| `git branch --show-current` | `master` | High |
| `git status --short --branch` | Clean; `ahead 3` | High |
| `git rev-list --left-right --count origin/master...HEAD` | `0 3` | High |
| `git diff --check e9066c02^..5568748a` | Exit 0 | High |
| `flutter --version` | Flutter 3.44.6, Dart 3.12.2 | High |
| `flutter analyze` from `app` | Exit 0; no issues | High |
| `flutter test test/router_test.dart` | 30 passed, 1 failed: missing `chat-activity-context` at line 1773 | High |
| `flutter test test/features/studio/feature_studio_golden_test.dart` | 2 passed, 7 failed; 0.02%–0.11% glyph-focused diffs | High |
| `flutter test` from `app` | 550 passed, 8 failed: seven Studio goldens and Activity→Chat context | High |
| `dotnet test tests/DigitalBrain.OrleansTests/DigitalBrain.OrleansTests.csproj --logger "console;verbosity=minimal"` | 769/769 passed | High |
| `dotnet test --logger "console;verbosity=minimal"` | 977 passed, 1 failed across 978 tests; repository comment policy rejects seven tracked Flutter-generated desktop files | High |
| `aspire doctor` | 5 passed, 0 warnings, 0 failures | High |
| Aspire AppHost/resource inspection | No AppHost running; resources/logs/traces unavailable without starting it | High |
| `dotnet list hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj package --include-transitive` | AppHost and hosting packages resolve to 13.5.0 preview; several solution Aspire packages remain 13.4.6 | High |
| Context7 resolve calls for Aspire, Orleans, Flutter, and gRPC | Monthly quota exceeded; no documentation returned | High |
| CodeGraph Task 10 trace | Run contracts/projection/query/gRPC/Flutter exist; effect safety gaps below are present in current source | High |
| Computer-use journeys | Not run in this planning turn; no repository evidence for the ten required journeys | High |

Inherited evidence is not treated as current unless reproduced above. Live resource health, logs, traces, responsive manual inspection, accessibility assistive-technology checks, and all ten journeys remain unverified.

## Tasks 1–15 Reconciliation

| Task | Classification | Evidence and gap |
|---|---|---|
| 1 Baseline | Implemented but not fully verified | Current CLI baseline exists, but live/visible baseline evidence is not current and broad gates are red. |
| 2 Feature Draft aggregate | Verified complete | Current Orleans suite passes 769/769 and includes revision, bounds, replay, verification invalidation, and compatibility tests. |
| 3 Authoring services | Verified complete | Orleans, IntegrationContract (116/116), and E2E (16/16) projects pass in the root run. |
| 4 Typed product RPCs | Partially implemented | Draft, install, Feature detail, rollback, and Activity are wired. `ListFeatures`, Connections, Memory, and Home are declared in proto but not overridden in `UiGrpcService`. |
| 5 Session ownership | Verified complete | The Login→Chat→Studio→Chat controller/surface test passes in the current router run. |
| 6 Trusted adaptive shell | Verified complete | Shell tests/goldens are not among current failures; trusted-shell rejection and responsive Chat retention pass. |
| 7 Feature Studio | Implemented but not fully verified | Dedicated Studio exists with extensive tests; seven routed goldens currently fail and live visual proof is absent. |
| 8 Testing/access/install/rollback | Implemented but not fully verified | Backend and widget behavior exist; access/install goldens fail and no current live installation proof exists. |
| 9 Discovery/execution | Implemented but not fully verified | Owner catalog and invoker tests pass in Orleans; hot update/pause/rollback behavior is not live-verified. |
| 10 Unified Activity | Partially implemented | Run projection, query, gRPC, Activity list/detail, filters, links, and tests exist; effect lifecycle, retention, outcome status, terminal timing, and exact Chat context are incomplete. |
| 11 Features/direct use/triggers | Partially implemented | Feature detail/source/rollback and Automation reference highlighting exist. Catalog, `/use`, release history, pause/resume UI, and trigger CRUD are absent. |
| 12 Connections/Memory | Not started | Proto messages/RPC declarations exist, but no server overrides, Flutter modules, or routes exist. |
| 13 Home/global command | Not started | `GetHomeSummary` is declared only; Home module, route, and global command do not exist. |
| 14 Product polish | Partially implemented | Studio, shell, and Activity have local responsive/semantic coverage; missing product modules and red goldens block product-wide completion. |
| 15 Dogfood/terminal proof | Blocked by Tasks 10–14 | No ten-Feature visible evidence exists; external writes must not be attempted until Task 10 safety is green. |

## Risk Register

| Order | Risk | Evidence | Required control |
|---|---|---|---|
| 1 | A declined Salesforce action can later execute. | `SalesforceFeatureEffectRail.ApplyAsync(false)` declines only the installation intent; the durable plan remains executable. | Terminally resolve the plan first; execution must replay the declined terminal result without invoking the provider. |
| 2 | Provider failure, expiry, and outcome-unknown can project as Completed. | The rail calls `ApplyIntentAsync` for every returned disposition; `FeatureRunProjection` treats applied as Completed. | Persist a typed terminal kind and map each kind explicitly. |
| 3 | Run history depends on prunable intent records. | `RetainIntentLedger` evicts resolved intents; projection reads current intents to decide Failed/Completed. | Copy bounded safe effect resolutions into the durable completion/Run history before intent pruning. |
| 4 | Resolved payload retention exceeds need. | `ApplyIntent` and `DeclineIntent` leave `PayloadJson`; decline also leaves plan payload intact. | Store a digest plus safe resolution metadata and scrub both intent and plan payloads on every terminal path. |
| 5 | Outcome replay is not content-stable. | Stable `salesforce-outcome-<digest>` ID is combined with `GetUtcNow()` on every publication attempt. | Derive the complete outcome input, including time, from the durable resolution. |
| 6 | Effect decisions lack complete audit identity. | Current plan completion stores only disposition and safe text; intents store applied/declined timestamps. | Persist decision ID, actor binding, terminal kind, and stable resolution time. |
| 7 | Multiple effect terminal timing is inaccurate. | Completion time is base run completion or latest decline only; applied effect times are ignored. | Use the latest durable resolution across all effects. |
| 8 | Activity→Chat proves routing, not context. | URI retains `conversationId`, but the stateful Chat branch does not render the banner and has no context-loading operation. | Add canonical conversation route/context query and assert the originating request content, not only an ID. |
| 9 | Root gate is red from accidental generated artifacts. | Seven desktop registrant files were regenerated with comments in `5568748a`. | Sanitize or restore them and add a generation-policy test/step. |
| 10 | Studio golden evidence is unstable. | Seven non-compact captures omit glyphs while geometry remains stable; compact captures pass. | Pin/load deterministic test fonts before judging or updating baselines. |
| 11 | Aspire version intent is ambiguous. | AppHost resolves 13.5 preview while doctor reports 13.4.6 and testing packages remain 13.4.6. | User confirms stable 13.4.6 or deliberate 13.5 preview; align the entire family atomically. |
| 12 | Declared RPCs can return Unimplemented. | Proto includes six unwired product methods. | Wire methods only with real typed projections; do not expose empty destinations. |

## Dependency-Ordered Path and Stop/Go Gates

1. **Gate repair:** remove accidental generated comments, make Studio goldens deterministic, and align Aspire versions after user confirmation.
2. **Task 10A–D:** close effect decisions, make intent/Run history durable and payload-safe, make outcome publication replay-stable, and load exact Chat context.
3. **Task 10 live gate:** start Aspire safely, verify resources/logs/traces, and exercise only non-external or synthetic effect paths. **STOP** if any decline can execute, any provider payload remains, or any non-success is Completed.
4. **Feature Studio preservation gate:** restore its complete focused/golden suite and inspect 1440/1024/736/320 states. **STOP** if authoring is redirected into Chat or access copy leaves Flutter.
5. **Task 11:** Features catalog, direct use, releases, pause/resume, and triggers on the now-trustworthy Run model.
6. **Task 12:** Connections, then Memory, as separate commits.
7. **Task 13:** operational Home, then global command.
8. **Task 14:** product-wide naming, recovery, responsive, accessibility, and visual polish.
9. **Task 15:** ten visible journeys in deterministic sandboxes. **STOP** external-effect journeys until Gmail/Salesforce sandbox identity is visibly confirmed.
10. **Terminal gate:** broad tests, Aspire, logs/traces, diff, accessibility/responsive evidence, and all journeys. Any failure returns to its owning slice; nothing later is marked complete.

## Slice 0: Restore Trustworthy Verification

**Purpose:** Make failures indicate product behavior rather than accidental generated comments or font drift.

**Files:**
- Modify: the seven tracked files under `app/linux/flutter`, `app/macos/Flutter`, and `app/windows/flutter` named by `RepositoryPolicyTests`.
- Modify: `app/test/features/studio/feature_studio_golden_test.dart` only if deterministic font loading is absent.
- Modify together after user version choice: `Directory.Packages.props`, `hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`, `nuget.config`.
- Test: `tests/DigitalBrain.UnitTests/RepositoryPolicyTests.cs` and Studio golden tests.

**Invariants:** Generated tracked source is comment-free; expected goldens are not updated until deterministic fonts render; all Aspire packages intentionally use one compatible release family.

- [ ] Reproduce the unit policy and Studio golden failures independently.
- [ ] Restore/sanitize the seven registrants, then run `dotnet test tests/DigitalBrain.UnitTests/DigitalBrain.UnitTests.csproj --logger "console;verbosity=minimal"`; expect 46/46.
- [ ] Load a deterministic bundled test font in the Studio golden harness, rerun twice, and require identical results before considering baseline updates.
- [ ] Apply the user-confirmed Aspire version policy across AppHost SDK, hosting integrations, client integrations, and testing packages in one dependency commit.
- [ ] Run `aspire doctor`, AppHost tests, Studio goldens, full Flutter tests, and the root .NET command.

**Acceptance:** Unit policy is green; Studio goldens either match or have reviewed intentional diffs; package listing and doctor no longer disagree about intended version family.

**Commit boundary:** `chore: restore deterministic verification baseline`

## Task 10A: Terminally Resolve Effect Decisions

**Purpose:** Make decline, approval, expiry, failure, and outcome-unknown mutually exclusive durable terminal facts.

**Files:**
- Modify: `src/DigitalBrain.Kernel.Contracts/Runtime/InoEffectPlan.cs`
- Modify: `src/DigitalBrain.Kernel.Contracts/Runtime/InoEffectPlanStore.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/InoEffectPlanStore.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/InoEffectPlanNeuron.cs`
- Modify: `integrations/DigitalBrain.Integrations.Salesforce/SalesforceFeatureEffectRail.cs`
- Test: `tests/DigitalBrain.OrleansTests/Legacy/Runtime/InoEffectPlanTransitionsTests.cs`
- Test: `tests/DigitalBrain.OrleansTests/Legacy/Runtime/InoEffectConflictRecoveryTests.cs`
- Test: `tests/DigitalBrain.E2ETests/FeatureEventEffectE2ETests.cs`

**Interfaces:** Add additive serialized `InoEffectDecision` metadata with decision ID, actor scope, terminal kind, and resolved-at time. Add an idempotent decline operation to the plan neuron/store. Existing execute replays any terminal result before provider dispatch.

**Tests to write first:**

```csharp
[Fact]
public async Task Decline_wins_a_later_approval_without_provider_execution();

[Fact]
public async Task Racing_approval_and_decline_produce_one_terminal_decision();

[Theory]
[InlineData(InoEffectTerminalKind.Expired)]
[InlineData(InoEffectTerminalKind.Failed)]
[InlineData(InoEffectTerminalKind.OutcomeUnknown)]
public async Task Every_terminal_path_scrubs_the_plan_payload(InoEffectTerminalKind kind);
```

**Implementation steps:**

- [ ] Add the failing transition tests and verify the Orleans project is red.
- [ ] Add terminal decision types using new Orleans field IDs; keep existing fields readable.
- [ ] Implement compare-and-set resolution in `InoEffectPlanTransitions`; same decision replays, a different terminal decision conflicts.
- [ ] Add `DeclineAsync` to `IInoEffectPlanNeuron`/store and bind decision ID plus actor scope.
- [ ] Make `ExecuteAsync` return an existing terminal result before any handler call.
- [ ] Resolve the durable plan before resolving the Feature intent; on retry reconcile downstream state from the terminal plan.
- [ ] Prove both race orderings and storage outcome-unknown recovery without a provider call after decline.

**Acceptance:** Exactly one terminal decision exists; decline can never be followed by execution; every terminal plan has an empty provider payload; replay is content-identical.

**Verification:** Orleans project, E2E project, exact root .NET command, `aspire doctor`, `git diff --check`.

**Commit boundary:** `fix: make effect decisions durably terminal`

## Task 10B: Durable, Payload-Safe Run Effect History

**Purpose:** Remove Run status dependence on prunable payload-bearing intents.

**Files:**
- Modify: `src/DigitalBrain.Kernel.Contracts/FeatureGrainContracts.cs`
- Modify: `src/DigitalBrain.Kernel/Features/FeatureStateModels.cs`
- Modify: `src/DigitalBrain.Kernel/Features/FeatureInstallationTransitions.cs`
- Modify: `src/DigitalBrain.Kernel/Features/FeatureInstallationGrain.cs`
- Modify: `src/DigitalBrain.Kernel/Features/FeatureRunProjection.cs`
- Test: `tests/DigitalBrain.OrleansTests/Features/FeatureInstallationTransitionTests.cs`
- Test: `tests/DigitalBrain.OrleansTests/Features/FeatureTransitionLimitTests.cs`
- Test: `tests/DigitalBrain.OrleansTests/Features/FeatureGrainTests.cs`

**Invariants:** Pending intents retain bounded payload; resolved intents retain only payload digest and safe terminal metadata; each completion retains bounded effect resolutions even after intent compaction; latest effect resolution defines terminal time.

**Tests to write first:**

```csharp
[Fact]
public void Pruning_resolved_intents_does_not_change_historical_Run_status();

[Fact]
public void Outcome_unknown_projects_Parked_and_provider_failure_projects_Failed();

[Fact]
public void Multiple_effects_use_the_latest_resolution_as_terminal_time();

[Fact]
public void Resolving_an_intent_scrubs_payload_but_preserves_its_digest();
```

**Implementation steps:**

- [ ] Add additive `FeatureEffectResolution` and completion fields with stable identities and bounded safe text.
- [ ] Replace applied/declined-only transitions with one idempotent `ResolveIntent` command while retaining legacy fields for migration reads.
- [ ] Copy the safe resolution into the matching `FeatureCompletion` before intent pruning and scrub `PayloadJson`.
- [ ] Project WaitingForApproval if any effect is pending, Completed only if all effects succeeded, Failed for declined/expired/provider failure, and Parked for outcome-unknown.
- [ ] Set terminal time to the maximum of execution completion and every effect resolution.
- [ ] Bound and test 32 effect resolutions per Run and deterministic compaction.

**Acceptance:** Run status and time survive intent pruning; no resolved provider payload remains; historical release identity is unchanged.

**Verification:** Orleans project, IntegrationContract project, exact root .NET command, `git diff --check`.

**Commit boundary:** `fix: persist safe terminal effect history`

## Task 10C: Replay-Stable Outcome Publication

**Purpose:** Make outcome publication safe across retries and acknowledgement loss.

**Files:**
- Modify: `integrations/DigitalBrain.Integrations.Salesforce/SalesforceFeatureEffectRail.cs`
- Modify if Gmail shares the pattern: `integrations/DigitalBrain.Integrations.Google/GmailCapabilityHandlers.cs`
- Test: `tests/DigitalBrain.E2ETests/FeatureEventEffectE2ETests.cs`
- Test: `tests/DigitalBrain.OrleansTests/Features/FeatureInstallationTransitionTests.cs`

**Invariants:** Stable outcome ID implies byte-identical kind, payload, occurred-at, correlation, trace, causation, origin, and reference.

- [ ] Write a failing test that publishes, simulates lost acknowledgement, advances time, and republishes the same outcome.
- [ ] Build outcome `FeatureInput` only from the durable terminal resolution; never call `GetUtcNow()` while reconstructing a replay.
- [ ] Make a different decision/result under the same outcome ID fail closed.
- [ ] Prove successful, declined, failed, expired, and outcome-unknown outcomes are each stable.

**Acceptance:** Replay returns Duplicate/same receipt, never a content conflict; a changed outcome cannot reuse the ID.

**Verification:** E2E project, Orleans project, root .NET command, `git diff --check`.

**Commit boundary:** `fix: make effect outcomes replay stable`

## Task 10D: Render the Exact Originating Chat Context

**Purpose:** Make Activity navigation load and visibly render the originating conversation/request rather than only changing route metadata.

**Files:**
- Modify: `src/DigitalBrain.Mcp/Protos/ui.proto`
- Modify: `src/DigitalBrain.Mcp/UiGrpcService.cs`
- Modify: `src/DigitalBrain.Mcp/DigitalBrainUiEndpoints.cs`
- Modify: `app/lib/core/session/digitalbrain_client.dart`
- Modify: `app/lib/runtime/grpc_ui_transport.dart`
- Modify: `app/lib/router.dart`
- Modify: `app/lib/runtime/widgets/chat_page.dart`
- Test: `tests/DigitalBrain.OrleansTests/Legacy/Runtime/UiGrpcServiceTests.cs`
- Test: `app/test/router_test.dart`
- Test: `app/test/runtime/runtime_shell_test.dart`

**Invariants:** Context lookup is owner/actor scoped; request text is returned only to its authenticated owner; navigation uses canonical `/chat/:conversationId`; session ownership remains above routes; route IDs alone are not proof.

**Tests to write first:**

```dart
testWidgets('Activity opens and renders the exact originating request in Chat', (tester) async {
  // The assertion must match the request content and conversation identity,
  // not only a banner key or URI query parameter.
});
```

- [ ] Reproduce the current 30/1 router result.
- [ ] Add an owner-scoped typed context query or equivalent existing conversation read boundary; reserve owner/actor fields in proto.
- [ ] Add `/chat/:conversationId` and keep `/chat` for the current conversation.
- [ ] Load the referenced request and render its exact safe text/context in Chat.
- [ ] Key routed Chat presentation by context while retaining the shared app-lifetime session controller.
- [ ] Preserve back navigation, focus restoration, deep links, refresh, and authentication recovery.

**Acceptance:** Activity→Chat renders the exact originating request; wrong-owner/missing references fail safely; router test is 31/31; session continuity tests remain green.

**Verification:** backend service tests, `flutter test test/router_test.dart`, runtime-shell tests, full Flutter tests, root .NET command.

**Commit boundary:** `fix: restore exact Activity chat context`

## Preserve and Finish Feature Studio

Feature Studio remains the controlled UI for Behavior, Suggested changes, Code & changes, Test results, access review, installation, rollback handoff, and return-to-task. Do not add these controls to Chat or backend surfaces.

**Genuine remaining gaps:** deterministic routed goldens; live visual inspection; catalog/update entry points; complete version history; automation editing handoff; product-wide copy/recovery consistency; assistive-technology proof; ten visible journeys. The large controller/gateway/page files may be split only when an adjacent change proves a responsibility boundary; file size alone is not authorization for a rewrite.

**Focused preservation gate:**

- [ ] Run all Studio controller, gateway, page, recovery, validation, diff, and golden tests.
- [ ] Inspect saved, saving, conflicted, suggestion, code, verified, access-review, and install-success states at 1440/1024/736/320.
- [ ] Verify Ctrl+S, Ctrl+Enter, Escape, focus order, 200% text, screen-reader announcements, high contrast, reduced motion, and touch targets.
- [ ] Verify install still offers separate Return to Chat and explicit Run now actions.
- [ ] Reject any design that moves trusted access wording or installation authority into a dynamic surface.

**Commit boundary:** `fix: stabilize feature studio verification` only if code/test harness changes are required.

## Task 11 Executable Slices: Features, Direct Use, and Triggers

### 11A — Catalog and Feature Management

**Files:** create `src/DigitalBrain.Kernel.Contracts/DigitalBrainQueryContracts.cs`; extend `DigitalBrainQueryService.cs`, `DigitalBrainUiEndpoints.cs`, `UiGrpcService.cs`, and `ui.proto`; create `app/lib/features/catalog/feature_catalog_models.dart`, `feature_catalog_gateway.dart`, `feature_catalog_controller.dart`, `feature_catalog_page.dart`; modify `router.dart` and `main_destination.dart`.

**Tests first:** owner isolation; installed/draft/paused/needs-attention ordering; pause/resume replay; release history identity; useful empty/degraded states.

- [ ] Wire `ListFeatures` to a real owner-scoped projection and add `/features`.
- [ ] Extend Feature detail with purpose, health, active/previous Versions, Connections, Automations, and recent Activity.
- [ ] Add pause/resume typed commands with revision and idempotency IDs.
- [ ] Keep Feature Studio as the only draft/update authoring surface.

**Acceptance:** catalog is data-backed and all management actions alter the next operation.
**Commit:** `feat: add feature catalog and management`

### 11B — Direct Use

**Files:** create `app/lib/features/direct_use/feature_use_page.dart`, controller/gateway/models/tests; extend router with `/features/:featureId/use`.

- [ ] Write a failing test proving a generated form submits through the bounded backend surface and creates a Direct Run.
- [ ] Embed only Feature-specific input/result surfaces inside the trusted shell.
- [ ] Link the resulting Run to Activity and preserve Feature context/back navigation.

**Acceptance:** direct invocation uses the active exact Version and the same Run model as Chat.
**Commit:** `feat: add direct feature use`

### 11C — Trigger Management

**Files:** add trigger contracts/transitions/endpoints; create `app/lib/features/automations/*`; route `/features/:featureId/automations/:bindingId` while retaining the legacy trigger alias if required.

- [ ] Test schedule/event create, edit, pause, remove, authority review, replay, and revoked-Connection behavior first.
- [ ] Show next run and recent Activity; never create a top-level Automations destination.
- [ ] Prove schedule/event Runs use the common projection.

**Acceptance:** every Automation is inspectable, pausable, authority-reviewed, and linked to its Feature.
**Commit:** `feat: add feature trigger management`

## Task 12 Executable Slices: Connections and Memory

### 12A — Connections

**Files:** create Connection query/command contracts; wire proto/service/endpoints; create `app/lib/features/connections/*`; add `/connections` and `/connections/:connectorId`.

**Tests first:** owner isolation; no credential/token fields; health and supplied abilities; dependent Features/Automations; connect/reconnect/test/revoke; revocation affects next resolution.

- [ ] Reuse Connector authorization boundaries; Flutter receives only safe metadata and typed actions.
- [ ] Add useful loading/empty/degraded/disconnected/recovery states.
- [ ] Verify revoke impact before confirmation and reconnect recovery afterward.

**Acceptance:** no credential material crosses gRPC; actions produce real Connector outcomes.
**Commit:** `feat: add governed connections`

### 12B — Memory

**Files:** create Memory query/command contracts; wire proto/service/endpoints; create `app/lib/features/memory/*`; add `/memory` and `/memory/:memoryItemId`.

**Tests first:** list/detail owner isolation; revision/ETag conflict; correct/export/forget; source/authority/usage; forgotten item absent from next recall.

- [ ] Present provenance, authority, scope, retention, last use, and consuming Runs.
- [ ] Make correction and forget explicit governed commands.
- [ ] Export through a safe artifact, never a logged payload.

**Acceptance:** correction changes the next relevant result and forget removes future recall.
**Commit:** `feat: add governed memory`

## Task 13 Executable Slices: Home and Global Command

### 13A — Operational Home

**Files:** create `HomeSummary` query contracts; wire server methods; create `app/lib/features/home/*`; add `/home`.

**Tests first:** actionable ordering, bounded sections, no decorative metrics, links to exact approvals/Runs/Features/Connections/Automations.

- [ ] Show Needs attention, Active now, Completed today, Upcoming, and bounded Suggestions.
- [ ] Keep routine history in Activity.

**Acceptance:** every card is data-backed and has a real recovery/navigation action.
**Commit:** `feat: add operational home`

### 13B — Global Command

**Files:** create `app/lib/shell/global_command.dart` plus state/tests; modify `digitalbrain_shell.dart`, router, and Chat handoff.

**Tests first:** current-context attachment, simple in-place completion, expansion to Chat without lost destination/conversation, hidden in full Chat and Studio contextual behavior unchanged.

- [ ] Keep command state in the trusted shell, not a route or backend surface.
- [ ] Anchor progress/result presentation without taking over global navigation.

**Acceptance:** context survives expansion and Feature Studio remains contextual and separate.
**Commit:** `feat: add global command`

## Task 14 Executable Slice: Product-Wide Quality

**Files:** only observed owning Flutter modules; centralized theme/copy primitives where repetition is proven; corresponding widget, semantics, and golden tests.

**Tests first:** 1440/1024/736/320, 200% text, keyboard-only, focus restoration, semantics, high contrast, reduced motion, touch targets, stable timestamps/IDs.

- [ ] Remove residual normal-user INO terminology.
- [ ] Make loading, empty, degraded, offline, conflict, expired approval, partial install, parked Run, and disconnected Connection intentional.
- [ ] Verify every visible action has a backend outcome and recovery path.
- [ ] Conduct one coherent review across Home, Chat, Features, Connections, Activity, Memory, and Studio.

**Acceptance:** full Flutter tests/goldens pass twice; manual responsive and accessibility evidence is captured in `.artifacts/task-14/`; no empty destination remains.
**Commit:** `fix: complete digitalbrain product polish`

## Task 15 Executable Slice: Ten-Feature Visible Dogfood

Use `.artifacts/task-15/<timestamp>/` for ignored screenshots, journey logs, resource snapshots, and the evidence manifest. Prepare sandbox data through backend tools only; create, edit, verify, install, invoke, and inspect every Feature through Flutter.

### Shared visible sequence for every row

Unsupported Chat request → durable Draft → open Feature Studio → meaningful Behavior edit → request Suggested changes → inspect additions/removals → deliberately accept or reject → inspect Code & changes → run tests → inspect Test results → review exact access/Version → install → return to exact originating request → explicit Run now where applicable → inspect Activity.

| # / Feature | Prerequisites and sandbox data | Distinct visible proof | Expected evidence and screenshots | Cleanup |
|---|---|---|---|---|
| 1 Company Research Brief | Deterministic model/query fixture and synthetic Acme corpus | Chat origin, cited text artifact | Chat request, Studio sections, install, Run result/artifact | Delete generated artifact and Draft test data |
| 2 Inbox Triage Digest | Sandbox Gmail with fixed unread/read/priority messages | Read-only Connection selection and Direct origin | Gmail access review, direct form, Activity origin=Direct | Remove seeded messages and revoke test grant |
| 3 Customer Reply Draft | Sandbox Gmail thread with no send authority | Suggestion review and draft-only result | Behavior diff, Code, tests, unsent draft result | Delete sandbox draft/thread |
| 4 Approved Email Follow-up | Dedicated sink mailbox and read-after-send API | Exact signed approval; no send before approval | Approval card, decision ID, sent sandbox message, Activity Completed | Delete message, revoke mailbox Connection |
| 5 Salesforce Account Brief | Sandbox account with fixed fields | Connection health/dependency and Chat origin | Connection detail, cited account result, Activity | Restore/delete sandbox account fixture |
| 6 Opportunity Next-Step Advisor | Sandbox opportunity with staged history | Direct Feature form and result | Form inputs, direct Run, version identity | Restore opportunity fixture |
| 7 Approved Opportunity Update | Sandbox-only writable custom field | Decline-first non-execution, then fresh approved decision and read-after-write | Decline evidence, unchanged record, new approval, changed field, Activity | Restore field and revoke grant |
| 8 Memory-Backed Meeting Prep | Governed sandbox Memory Item and meeting fixture | Memory usage disclosure and cited source | Memory detail, Run usage link, generated prep | Forget fixture Memory and delete artifact |
| 9 Decision Capture | Sandbox conversation and Memory write authority | Explicit remember, inspect, correct, rerun changes result | Access review, created Memory, correction, changed Run | Forget decision Memory item |
| 10 Weekly Risk Digest | Salesforce risk fixtures plus controllable sandbox clock/scheduler | Automation creation and Schedule origin | Automation detail/next run, scheduled Activity, digest artifact | Remove Automation and restore clock/fixtures |

### Cross-journey proof

- [ ] Update Features 1 and 3; prove the next Runs use new digests without service or Flutter restart.
- [ ] Roll Feature 3 back; prove the following Run uses the retained prior digest.
- [ ] Pause/resume Feature 6 and verify next-operation eligibility.
- [ ] Revoke/reconnect the Gmail Connection and verify availability changes.
- [ ] Correct Feature 8's Memory Item and prove the next result changes.
- [ ] Complete Feature 9 keyboard-only and Feature 6 at 320 logical pixels.
- [ ] Exercise Chat, Direct, Schedule, and Event origins.
- [ ] Inspect resources, logs, and traces after each external-effect journey for leaks.

**Acceptance:** the evidence manifest names every screenshot, Run ID, release digest, decision ID, origin, sandbox cleanup receipt, and Aspire trace reference; all cleanup succeeds.
**Commit:** `test: add digitalbrain dogfood fixtures` only if deterministic non-secret fixtures are required; ignored evidence is not committed.

## Final Terminal Gates

- [ ] `flutter analyze` from `app`: exit 0.
- [ ] `flutter test` from `app`: all tests pass; run twice if golden/font stabilization changed.
- [ ] `dotnet test --logger "console;verbosity=minimal"` from repository root: all projects pass.
- [ ] `aspire doctor`: exactly 5 passed, 0 warnings, 0 failures.
- [ ] Live Aspire resources are healthy; relevant console logs, structured logs, and traces are inspected.
- [ ] Leak review finds no source bodies, original prompts, credentials, tokens, provider payloads, or sensitive Memory values.
- [ ] `git diff --check`: exit 0.
- [ ] Responsive evidence exists for 1440, 1024, 736, and 320 logical pixels.
- [ ] Accessibility evidence covers keyboard-only, focus restoration, screen reader, 200% text, high contrast, reduced motion, and touch targets.
- [ ] All ten visible journeys and cross-journey update/rollback/pause/revoke/Memory checks pass.
- [ ] Final diff contains no unrelated/generated artifacts and preserves user-owned changes.

## Assumptions Requiring User Confirmation

1. Choose the Aspire dependency policy: return the whole family to stable 13.4.6, or deliberately adopt the 13.5 preview family. The current mixed state is not a safe implied choice.
2. Confirm dedicated Gmail and Salesforce sandbox identities before Task 15. No real mailbox or Salesforce tenant may be used.
3. Confirm whether ignored `.artifacts/task-15/` evidence is sufficient or whether a separate durable evidence store is required.

## Recommended First Implementation Slice

After the short verification-hygiene cleanup, implement **Task 10A: Terminally Resolve Effect Decisions**. It removes the highest-severity risk, has a bounded file set, can be proven without external data, and creates the invariant required by every later Activity, Connection, Home, and dogfood flow.
