# Prompt: dissolve the IntoChat E2E monolith into module suites

In `E:\intochat\digitalbrain`, execute Phase 3 of the IntoChat cleanup: migrate the product E2E
suite (`src/Applications/IntoChat/Tests/E2E`, ~2,600 lines) into the owning modules' E2E projects,
so IntoChat keeps only composition facts and the product's golden journeys. Read `CLAUDE.md`
first, then `docs/superpowers/specs/2026-10-01-intochat-cleanup-marketplace-design.md` (Phases 1–2,
already landed — this continues their test-ownership rule: module behavior belongs in the owning
module's test projects; IntoChat owns composition, host auth/CORS/config, shipped content, and the
product's spec journeys).

## Why

The suite's harness — `BrainFact`, `IntoChatHostFixture` (shared composed host with workspace
leases), `ScriptedModelServer` (243 lines), Playwright browser helpers (`People`,
`SignedInPerson`, `WorkspaceBrowser`) — is the repo's only full-stack composed-brain test rig, so
platform guarantees (package sharing, secret canaries, grant scoping, trace budgets, workspace
isolation) were parked in the product repo. CLAUDE.md's own rule says to skip the 18-minute suite,
so these tests protect nothing where they are. The destinations already exist:
`src/Testing/DigitalBrain.Testing.E2E` (shared `SharedBrainFixture`, `ModuleHostProgram`) and
per-module E2E projects (Apps, Flutter, Microsoft.CSharp, AI, Core, Gmail). The migration even
started: `PackageSharingFacts` and `ResearcherPackage` are duplicated in
`DigitalBrain.Modules.Apps.Tests.E2E` already.

## Step 0 — the keystone: promote the harness to the platform

Move the composed-host harness from `IntoChat.Tests.E2E` into `DigitalBrain.Testing.E2E` so
module suites can lease fully composed brains:

- `IntoChatHostFixture` → a platform **reference-composition fixture** (name it for what it is,
  e.g. `ReferenceBrainFixture`), building on the existing `SharedBrainFixture`. The composition it
  boots is the reference module set, not "whatever IntoChat.exe is"; if it currently boots the
  IntoChat host project, parameterize or replicate the composition so `DigitalBrain.Testing.E2E`
  does not reference the product (dependency direction: product → platform, never back).
- `BrainFact` (lease + scripted-model reset + shell-state reset), `ScriptedModelServer`,
  `People`/`SignedInPerson` (cookie sign-in clients), `WorkspaceBrowser`, `WorkspaceUploadFixture`,
  `LeadData`, and the `Diagnostics/` helpers (`OtlpTraceParser`, `TestTelemetryCollector`,
  `TraceAssertions`) all move here. Keep xUnit assembly-fixture wiring intact per consuming
  project (`AssemblyInfo.cs` pattern).
- IntoChat's E2E project then references `DigitalBrain.Testing.E2E` and keeps only thin aliases it
  still needs. Verification runs scripts for every runtime, so the reference composition must
  compose `CSharpModule` (the repo already declares this rule).

## Step 1 — dedup what already moved

Diff `IntoChat.Tests.E2E/Packages/{PackageSharingFacts,ResearcherPackage}.cs` against
`src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Tests.E2E/{PackageSharingFacts,ResearcherPackage}.cs`.
Port any assertions the module copy lacks, then delete the IntoChat copies. If the module copy
tests through grain contracts while the IntoChat copy tests through the product HTTP routes,
keep the HTTP-route half too — in Apps E2E, using the moved cookie-client helpers (`People`),
because `/packages` routes are Apps-owned since Phase 2.

## Step 2 — per-file dispositions

Move each file into the named module's `.Tests.E2E` project (create one only where named and
missing), rewriting namespaces and swapping `IntoChatHostFixture` for the platform fixture. Keep
sentence-shaped fact names, `TestContext.Current.CancellationToken`, and live-model/live-db gates
exactly as they are.

**Stays in IntoChat (the thin product core):**
- `Composition/ApplicationStartupFacts.cs`, `Composition/ProfileCompositionFacts.cs` — the
  composition boots and the packaging manifests (Dockerfile / Container.pubxml module sets) agree.
- `Agent/GoldenJourneyFacts.cs` — product-spec journeys J1/J2a, `DIGITALBRAIN_E2E_LIVE_MODEL`
  gated. Stays, but consumes the moved harness.
- `Apps/BuiltInAppRoutesFacts.cs` — the host's shipped-app route surface.

**→ Apps E2E** (`DigitalBrain.Modules.Apps.Tests.E2E`):
- `Packages/ShareCSharpFacts.cs` — sharing a C# app is Apps + CSharp semantics; put it in Apps
  (it drives `/packages` routes); if its substance is sandbox/script behavior, Microsoft.CSharp
  E2E instead — decide by what it asserts.
- `LocalApps/LocalAppsJourneyFacts.cs` (+ `WorkspaceUploadFixture`) — local app install/upload
  journeys; if the exercised contracts are Files-owned, split accordingly.

**→ Assistant (or AI) E2E** — create `DigitalBrain.Modules.Assistant.Tests.E2E`:
- `Agent/AgentModelsHttpFacts.cs`, `Agent/AgentTableJourneyFacts.cs`, `Agent/AgentWorkflowFacts.cs`,
  `Apps/AssistantNeuronUiFacts.cs`. These drive the assistant's HTTP/UI surface with the scripted
  model server; the server itself lives in Testing.E2E after Step 0.

**→ Flutter E2E** (`DigitalBrain.Modules.Flutter.Tests.E2E`, exists):
- `Workspace/ShellPersistenceFacts.cs` (the browser-journey remnant; `ShellPersistenceHttpFacts`
  is already there — merge, don't duplicate), `Workspace/WorkspaceRestoreFacts.cs`.

**→ Supabase E2E** — create `DigitalBrain.Modules.Supabase.Tests.E2E`:
- `Workspace/SupabaseTableDisplayFacts.cs` + the `LeadData` seed it needs (seed stays with its
  only consumer if nothing else uses it).

**→ Identity / Kernel security E2E** (`DigitalBrain.Core.Tests.E2E` exists; prefer an
`DigitalBrain.Modules.Identity.Tests.E2E` for the identity-owned ones):
- `Security/GrantRevokeFacts.cs` (grants list/revoke HTTP scoping — comment says enforcement is
  Identity's GrantFacts; this is the HTTP edge half → Identity),
- `Security/CrossWorkspaceFacts.cs` (cross-principal 403 journey → Identity/Core),
- `Security/CanarySecretFacts.cs`, `Security/ContentCaptureFacts.cs` (secrets never appear in
  responses/traces/logs; GenAI content capture rules → Kernel/Core E2E, since they are
  platform-ring guarantees over telemetry).

**→ Kernel/Core E2E:**
- `Diagnostics/TraceBudgetFacts.cs` — trace budget from real exported spans (idle shell quiet, a
  grain call is exactly two spans, Orleans activity source registered once). Platform guarantee;
  its helpers moved in Step 0.

**→ Compute (or Assistant) E2E** — by what it asserts:
- `Receipts/ReceiptJourneyFacts.cs` — receipts in the invocation stream; Compute owns metering,
  Assistant owns the surface. Put it with the module whose contracts it drives.

**Delete** anything that, after the moves, duplicates module coverage — and say so per file in
the report rather than silently dropping assertions.

## Rules

- Dependency direction: `DigitalBrain.Testing.E2E` must not reference any Applications project.
  Module E2E projects reference Testing.E2E + their own module. If the reference composition
  needs a module set, it declares its own, mirroring (not importing) the product profile; add a
  fact in IntoChat's `ProfileCompositionFacts` asserting the two stay in sync if drift matters.
- Preserve test semantics byte-for-byte where possible: same scenarios, same gates
  (`DIGITALBRAIN_E2E_LIVE_MODEL`, live-db variables), same timeouts. This is a move, not a
  rewrite; improve only what the move forces (fixture names, namespaces).
- Persisted-state and wire shapes are untouched — this change is tests-only plus Testing.E2E.
- Per CLAUDE.md: tests are facts, real grains over mocks, no test-only methods on production
  interfaces. No module ships IntoChat's publisher identity.
- Commit in reviewable slices: (1) harness promotion, (2) Apps dedup + moves, (3) Assistant/AI,
  (4) Flutter + Supabase, (5) security + diagnostics, (6) IntoChat E2E shrink + csproj pruning.
  Each commit builds and its affected projects' tests compile; run each receiving module's E2E
  project at least in compile+smoke form, and actually execute the fast ones.

## Verification

Per project, never the `.slnx`. For every receiving module: `dotnet test` its unit suite and
compile its E2E; execute E2E projects that don't need live gates. Then `dotnet test
src/Applications/IntoChat/Tests/Unit`, compile the shrunken IntoChat E2E, and smoke with
`aspire run` from `src/Applications/IntoChat/AppHost` until all resources are Healthy. Finish by
updating CLAUDE.md's testing guidance: the 18-minute monolith no longer exists; name the new rule
(module E2E suites run with their module; IntoChat E2E is composition + golden journeys only).
Record per-project results in `docs/superpowers/verification/2026-10-01-intochat-e2e-migration.md`,
including a per-file disposition table (moved-to / merged / deleted-because).

## Out of scope

Demo-app pruning (GroupChat/WordCount and the AppHost modules they drag), folding ServiceDefaults
into an SDK `AddDigitalBrainHost()`, and any production-code refactor beyond what Testing.E2E
needs. Those are separate passes.
