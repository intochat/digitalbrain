# Workspace Apps Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Make workspace apps executable neuron aggregates that users can create, publish as user/appname, install under another account, and run independently.

**Architecture:** Extend the existing Apps and creator marketplace modules with portable behavior compositions and workspace app lifecycle. Built-in app types inherit the neuron programming model. IntoChat supplies authenticated HTTP adapters and a creation/marketplace interface; the Flutter shell scopes state to the signed-in account.

**Tech Stack:** C# net11.0, Orleans, Aspire hosted test harness, Flutter, xUnit and Playwright.

**Spec:** docs/superpowers/specs/2026-09-24-workspace-apps-design.md

## Global Constraints

- Work on current delivery/integration branch; no external publication or deployment.
- Preserve grain aliases and existing serialization IDs; append fields only.
- Modules are host-provided; app configurations are per workspace.
- Published versions are immutable and exclude runtime state, credentials and author-specific addresses.
- Derive marketplace namespace from authenticated identity.
- End old subscriptions and isolate local persistence when switching accounts.

## Review Focus

1. Another principal cannot claim a publisher namespace or reach a foreign workspace.
2. Reinstall, retry and reload preserve local state without importing publisher state.
3. Malformed/cyclic/unbounded graphs and missing module requirements fail before activation.
4. Account switching disposes old clients and never displays stale account content.
5. Duplicate version publication cannot replace executable content.

### Task 1: Account sessions and switching

**Files:** Identity/Identity/IdentityEndpoints.cs, Identity/Directory/IdentityDirectoryNeuron.cs, Identity/Contracts/Accounts.cs, corresponding unit tests; Flutter app/core/lib/src/ui_client.dart and shell/lib/auth/, main.dart, workspace/workspace_app.dart plus shell tests.

**Interfaces:** Preserve /identity/session and logout. Establish secure account registration/login with independent default workspaces and a UI switch action. Expose principalId/accountId/workspaceId from the session; local shell storage uses authenticated identity. Parent integrates workspace-scoped app endpoints against the authenticated principal.

- [x] Write tests that separate account ownership, reject invalid authentication and switch account without retaining prior local state.
- [x] Run tests before implementation and record the missing behavior.
- [x] Implement session flow and UI switch with awaited logout and client disposal.
- [x] Run Identity unit and Flutter account tests; record commands and results.

### Task 2: App definitions, runtime and composition

**Files:** Apps/Contracts new AppComposition.cs and AppRuntime.cs; Apps/Apps new Runtime/ classes; AppManifest.cs; ManifestValidator.cs; AppsModule.cs; Apps unit tests; kernel app base/definition registration where needed.

**Interfaces:** AppManifest.Composition holds a portable AppComposition. IWorkspaceApp exposes install/read/configure/activate/deactivate/dispatch. Named parts bind signals without runtime IDs. The runtime resolves only registered behavior kinds and produces persisted output/state; app activation is idempotent. Host registration is AddApp<T> with module validation.

- [x] Write a real-neuron test installing the same two-part composition in two workspaces, executing different configurations, and observing independent outputs.
- [x] Run the test to verify missing runtime support.
- [x] Implement validated definitions, dedicated app base, behavior registry and durable app state. Reject cycles, unknown parts/behaviors, missing config and missing modules.
- [x] Test deactivate/restart/duplicate activation and invalid composition; run Apps unit suite.

### Task 3: Immutable marketplace packages

**Files:** Marketplace/Marketplace/Creators/CreatorPublishingNeuron.cs and certification; Apps catalog integration; IntoChat new app runtime HTTP adapter; marketplace unit tests.

**Interfaces:** GET /marketplace/apps, publication of a workspace app under the authenticated namespace, install by publisher/name plus pinned version. Installation persists the complete composition and validates locally before catalog registration. Commands use operation IDs to make retries idempotent.

- [x] Add tests for user/appname, namespace theft and conflicting republish of a version.
- [x] Run failing tests, then extend manifest validation and publisher checks without changing legacy dotted aliases.
- [x] Add workspace-filtered create/configure/run/open/publish/install endpoints. Map validation to 400, absent app to 404, conflict to 409 and authorization to 403.
- [x] Run Marketplace and Apps unit suites.

### Task 4: IntoChat creation and marketplace UI

**Files:** Flutter shell workspace/apps new app studio/marketplace screen, core client app methods, workspace navigation; IntoChat built-in app registration and surface adapters.

**Interfaces:** UI consumes Task 3 routes and session identity from Task 1. Display portable app definitions, installed lifecycle, version and outputs. Enable editing a named composition, publishing it, marketplace install and execution with local configuration.

- [x] Add widget tests for creating and running a composition and installing a listing.
- [x] Implement the screen and navigation, errors and loading state; preserve existing app opening.
- [x] Extract built-in app ownership behind app definitions and registration; keep transport in host.
- [x] Run relevant Flutter tests and analyzer.

### Task 5: Hosted cross-account acceptance and review

**Files:** IntoChat/Tests/E2E/Apps/MarketplaceAppJourneyFacts.cs and focused runtime tests.

- [x] Add hosted Playwright journey: first user creates a two-part app, runs it, publishes first-user/appname; switches account; second user installs, configures and runs it; reload preserves state.
- [x] Assert exact output and independence from creator's configuration/state, plus forbidden foreign workspace access and namespace spoofing.
- [x] Run hosted test against real IntoChat and run affected unit/Flutter suites and build.
- [x] Have a fresh reviewer inspect the integrated change; fix substantive findings and rerun affected tests.

## Ledger

- Planning: design and plan recorded from the approved conversation. Autonomous execution explicitly requested. Existing legacy marketplace test is in-process and does not prove install/run or UI switching.
- Documentation: Context7 Orleans lookup attempted; quota exhausted. Use official Orleans documentation fallback for framework-specific behavior.
- Accounts: password-hashed registration/login, cookie session switching, account-scoped shell persistence and server-authorized workspace creation implemented. Browser transport sends credentials; CORS allows credentials only for validated origins. Identity suite: 19 passed.
- Runtime: AddApp<T> propagates through production, unit and hosted builders, validates module dependencies, and registers built-in Assistant/Settings neuron roots. Portable apps compose host-registered text behaviors with bounded acyclic bindings, local configuration, durable state and operation receipts.
- Lifecycle: on-demand and background activation are explicit; shell connection activates workspace apps. Installation into an already active workspace observes the durable activation generation. Retries preserve state and activation effects.
- Marketplace: immutable complete manifest versions, authenticated publisher namespaces, pinned version reads/installs, no publisher runtime state copied. Marketplace suite: 32 passed.
- UI: App Studio creates compositions, publishes, browses and installs marketplace versions, configures and runs installations. Assistant owns its agent/conversations and neuron surface; Settings owns its profile/theme neuron surface. Existing advanced settings remain accessible through More settings.
- Review: independent review found unauthorized locally-created workspaces, a 1,024-operation lifetime cap, missing shell activation wiring, and exponential fanout despite acyclicity. All four fixed. Added regression coverage for malformed manifests and immediate activation after late installation.
- Verification: Apps 16 passed; kernel 62 passed; Flutter shell 57 passed; focused Flutter analyzer clean. AppHost build passed with zero warnings/errors. Runtime and account regressions were observed failing before their fixes.
- Broader IntoChat unit run: 75 passed, 4 failed because baseline files are absent: BackupPlanFacts.RestoreScriptAndCronJobsCoverEveryTarget (ops/backup/restore.sh), HostedDeploymentFacts.TelemetryCollectorTailSamplesToAPersistentBackend (ops/otel/collector.yaml), HostedDeploymentFacts.RunbookCoversIncidentBackupUpgradeAndSlo (docs/operations/runbook.md), PathTruthFacts.DecisionAndEpicRegistersExist (docs/product/decisions/README.md). git ls-tree HEAD confirms these paths are absent from the starting revision.
- Hosted journey: real browser testing exposed cross-origin cookie transport and Flutter accessibility selector issues; fixes applied. Final real Aspire/IntoChat/Flutter/Playwright run passed (1 test, 1m26s). Proves both UI registrations/account switches, two-part create/run/publish, exact marketplace version installation, independent Bob configuration/execution, browser reload persistence, unchanged Alice revision/configuration/part values/output, forbidden workspace access in both directions, forbidden publisher namespace reuse, and rejected empty/incorrect passwords.
- Focused IntoChat built-in app and CORS tests: 6 passed. Latest workspace activation follow-up: 1 passed. git diff --check clean.
- Existing hosted security regressions also pass: ASignedInPrincipalCannotReachAnotherPrincipalsWorkspace (1 test, 24s) and GrantThenRevokeMakesTheValueLookEmptyAndForeignWorkspacesAreForbidden (1 test, 21s). Run individually using the E2E command below with the corresponding method-name filter. No live model was used. Final changed Flutter UI/test analyzer clean.

## Verification commands

Run from repository root unless noted:

```text
dotnet test --project src/Modules/Apps/Tests/Unit -p:CodeGraphRefresh=false
dotnet test --project src/Modules/DigitalBrain/Kernel/Tests/Unit -p:CodeGraphRefresh=false
dotnet test --project src/Modules/Identity/Tests/Unit -p:CodeGraphRefresh=false
dotnet test --project src/Modules/Marketplace/Tests/Unit -p:CodeGraphRefresh=false
dotnet test --project src/Applications/IntoChat/Tests/Unit -c Release -p:CodeGraphRefresh=false
dotnet test --project src/Applications/IntoChat/Tests/E2E -p:CodeGraphRefresh=false -- --filter-method '*CreatedAppRunsIndependentlyAfterPublishingAndSwitchingAccounts*'
flutter test  (working directory: src/Modules/Google/Flutter/app/shell)
```

## Standalone startup and native acceptance follow-up

- Reproduced plain `aspire start --non-interactive` failing after a successful build with `FileNotFoundException: IntoChat`. The Aspire SDK defaults resource project references to `ExcludeAssets=all` and `Private=false`; `ReferenceOutputAssembly=true` alone only fixed compilation. The hosted test process had masked the missing standalone runtime dependency.
- Fixed the IntoChat AppHost reference with explicit `ExcludeAssets=none` and `Private=true`, preserving Aspire resource generation and the typed built-in app registrations.
- Verified plain `aspire start --non-interactive` succeeds. `aspire wait IntoChat` and `aspire wait FlutterShell` report healthy; final resource inspection has no unhealthy, waiting, exited or failed resources.
- Used the Computer Use plugin against the actual Flutter Windows app launched by Aspire, not browser automation or direct HTTP calls: switched from the existing session; registered local test account alice0924; created and ran greeting (HELLO WORLD); published alice0924/greeting version 1.0.0; switched and registered bob0924; observed an empty installed list; installed Alice's marketplace version; changed Bob's prefix to `bob ` and ran it (BOB WORLD); signed back into Alice and verified the original `Hello ` configuration and HELLO WORLD output were unchanged.
- Native test accounts and the local marketplace test app remain available. Aspire and the native app were left running for the user. `git diff --check` passes.

## First implementation boundaries

- Creator apps use registered deterministic text behaviors and a composition JSON editor; arbitrary uploaded code, workers and a visual graph editor are not part of this slice.
- Studio creates version 1.0.0. Marketplace storage supports immutable versions and pinned installation, but editing installed definitions and upgrading installations need a separate explicit migration workflow.
- Composed apps are managed in App Studio. Existing saved-window apps and their launcher remain available through their current interface.
- Assistant and Settings have dedicated neuron roots. IntoChat still contains transport adapters and legacy app-specific endpoints; this change establishes the app boundary without claiming every existing endpoint has moved out of the host.
