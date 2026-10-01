# Codebase Hygiene Findings — 2026-09-30

Research only; nothing here is fixed yet. Sources: six parallel audits (per-test tables and full
detail live in `.superpowers/sdd/hygiene/*.md`, local working files). Scope: entire codebase at
`refactor/csharp-module`, ~940 C# test cases + ~185 Dart tests + 1015 production C# files assessed.

## 0. Already done in this pass

- docs/ and 60 markdown files deleted; survivors: CLAUDE.md + functional package content
  (5 `app.spec.md`, agent prompt `.md`s, one EmbeddedResource).
- Doc-tombstone facts deleted with their targets (PathTruth doc check, HostedDeployment runbook,
  Supabase ADR assertion). Known IntoChat failures shrank 3 → 1.

## 1. Broken right now (fix first)

| # | Finding | Where |
|---|---|---|
| 1.1 | **Failing Flutter test**: `ui_gallery_test` "catalog covers registry exactly" — gallery data drifted from the registry (`neuronExample` empty). A real regression, not debt. | src/Modules/Google/Flutter/app/ui |
| 1.2 | **Last known-failing fact**: HostedDeployment `TelemetryCollectorTailSamples...` reads missing `ops/otel/collector.yaml`. Fix the file or delete the fact — an accepted-failure list should be empty. | IntoChat Tests/Unit |
| 1.3 | **`"a\b"` backspace bug in our own theory**: the invalid-brain-id case tests a backspace char, not a backslash, and targets `BrainScope.IsValidId` instead of the handler. | Assistant AgentRouteFacts |

## 2. Merge state vs master (research; resolution is its own task)

Branch: 264 ahead / **15 behind** origin/master; local = remote (+ this session's commits).
~82 predicted conflict paths, three root causes:

1. **Qdrant vs Memory rename** (~35 paths): master renamed Memory→Qdrant; the branch re-laid Memory
   under `Qdrant/DigitalBrain.Modules.Memory.*`. Pick one scheme, then fix csproj/slnx/AppHost.
2. **Gmail watch (PR #105)** (~10 paths): master's watch feature edits files the integrations
   refactor replaced (GmailNeuron/State, OAuth options → registration neuron). Must be
   **re-implemented on the branch's model**, not merged textually.
3. **Host/CI drift** (~20 paths): ci.yml, AppHost.cs, IntoChat.csproj, Dockerfile, docker-compose
   content conflicts; Behaviors/Coding modify/delete (keep the deletions; verify the CSharp module
   covers master's behaviors-in-sandbox work).

Repo itself is healthy: clean tree, 62.7 MiB pack, no tracked artifacts/secrets, descriptive
commit messages (merge, don't squash; optionally fold "Fix Task N" commits).

## 3. Test quality (~940 C# cases assessed individually; tables in hygiene/tests-*.md)

Aggregate: ~72% KEEP, ~20% WEAK, ~4% DUPLICATE, ~3% DELETE, rest gaps.

**Systemic diseases (each appears in 3+ projects):**
- **Loose denial checks**: `ThrowsAnyAsync<Exception>` accepts ANY failure as the expected denial
  (Secrets, AccountFacts, SubscriptionFailure…). A typo'd grain key would "pass" a security test.
- **Negative assertions with no wait**: pass if the signal is merely late
  (`FailedActivationIsNotReportedAsActive`, `ModuleAnnouncementsDoNotChangeTheSelectedComposition`).
- **Testing the fake, not the product**: Qdrant's 5 behavior facts hit an in-memory fake; Memory's
  index fake has no search so Recall ranking/limit are never exercised; Ledger/Meter concurrency
  facts run on in-memory stores; `FakeInvoicingProvider` has its own test.
- **Templated duplication**: ~45 Flutter facts follow one Set/Read round-trip template, E2E copies
  duplicating Unit; route-snapshot scaffolding repeated 3×; loopback HttpListener copied 4×;
  IChatClient stubs ~10×; tool-policy tests exist in both AI and Assistant.
- **Silently-skipped only-coverage**: Postgres live-provider tests and `PostgresResearchFacts`
  (the ONLY persistence coverage) skip without an env var — green ≠ ran.
- **Green-under-bug singletons**: `DefaultAllowlist` compares a constant to itself;
  `RealPeriodicTimerCanBeStopped` never checks ticks stop; Media oversize test never checks the
  provider wasn't called; Identity `ConsumingAOnceGrant...` is misnamed (calls Revoke — the real
  consume path is untested).
- **Results-hiding DSL**: Assistant `*Steps` runs 16 stream scenarios inside ONE aggregate fact —
  16 results collapse to 1; the DSL doesn't earn its indirection for `assistant-stream.feature`.
- **Tombstone/lint facts**: repo-layout lints as unit tests (`JournalingPackageIsNotReferenced`,
  `ExactlyOnePriceBookFileExists`, `LiveTableArchitectureFacts` source-tree scan, Gmail
  `TestAssemblyModuleFacts`); ~11 more file-absence facts in HygieneFacts/HostedDeployment.
- **Oversized fact files**: RegistrationFacts 422 lines, AllowanceFacts 394 (mixes rules, grains,
  billing), AccountFacts kitchen-sink 9-concern fact + duplicate share test.

**E2E triage**: security/agent-journey/persistence facts pay rent; ChaosCharge ×3,
BuiltInAppRoutes, profile + WorkerGateway composition, OtlpParserFacts don't need Aspire → move to
Unit or delete; `ServerRestartRestoresDurableState` permanently skipped.

**Framework gap**: src/Testing has ZERO self-tests (SignalProbe, BehaviorRun, TestWait, session
lifetime untested); TwitterFakes' Bitcoin fake keeps state in a plain field.

**Coverage gaps worth closing** (full lists per area file): grant consume path; Recall ranking;
concurrency on production stores; hosting env projections (anthropic/google/xai/github never
runtime-verified); "only Endpoint set → Partial" registration case; Dart core package (0 tests).

## 4. Structure & organization (detail in hygiene/structure.md)

- **Multi-type files**: 248/1015 C# files hold 2+ types; ~20 clear violations — worst:
  `UiKitEndpoints.cs` with **32 types**; `ShellPersistence.cs`, `GrainDocumentStore.cs`,
  `ComputeRecordsNeuron.cs` 7–9 each. Dart: 58 multi-type files, 17 files >400 lines.
- **Namespace rule missing**: 482 files match project+folder, 451 flatten to module root,
  67 differ outright; kernel project `DigitalBrain` uses namespace `DigitalBrain.Core` (27 files).
  **Decision needed**: adopt either folder-mirroring or module-root-flat as THE rule, then sweep.
  (Recommendation: module-root namespaces `DigitalBrain.<Module>` — matches the majority and the
  module-contract mental model; folders become organization, not identity.)
- **Dead code**: ~45 zero-reference types (SaidBody, TextBody, LlmAttribute, DraftSource,
  GitHubReviewPolicy, 8 unimplemented AI model markers IWhisper*/IGpt4o*/IGptImage1…),
  12 never-called members, 6 test-only-reachable types, 4 unreferenced `*.Deployment` projects to
  verify, `IAgentToolSource` seam with no production implementation, 2 dead Dart files.
- **Comments**: 78 boilerplate `/// <summary>` in 48 files (style forbids). No commented-out code,
  no TODO/FIXME (good).
- **Naming**: 8 file-name/type mismatches.

## 5. Cleanup workstreams — status 2026-09-30 (late)

1. ~~Fix the broken~~ **DONE**: gallery drift fixed (registry gained webbrowser, gallery data
   didn't), collector.yaml fact deleted, backspace theory fixed (`@"a\b"`) + retargeted at the
   real handler. **Known-failure set is now EMPTY** — IntoChat Unit 60/60.
2. ~~Merge with master~~ **DONE by supersession**: owner chose `merge -s ours` — branch is the new
   version, master's 15 commits content-discarded. Follow-ups: re-implement Gmail watch (PR #105)
   on the registration model; master's Qdrant rename discarded (branch layout stands).
3. ~~Test purge + hardening~~ **DONE** (commits 67a630230..13b3acb36): ~35 facts deleted
   (tombstones/duplicates/fake-tests), 18 templated Flutter E2E files collapsed to representatives,
   Steps DSL unrolled (15 individual facts), green-under-bug set fixed, ThrowsAny → typed
   (exposed+fixed a real bug: UntrustedCallerException wasn't Orleans-serializable), shared test
   plumbing extracted, 7 route facts hardened to exact method+pattern sets, UiKit's 64 routes
   pinned exactly (caught+fixed a dead `imagecanvass` route typo), table 409/403 HTTP facts
   restored, ChaosCharge/OTLP moved E2E→Unit. Remaining accepted gaps: Qdrant has no real-server
   test; Flutter E2E environment fails 10/12 on baseline (pre-existing, needs its own fix);
   src/Testing has no self-tests; Dart core package untested.
4. ~~Dead-code deletion~~ **DONE** (51b63427b, review-verified): 10 files + 3 co-located types + 10 members deleted, 3 file-type name mismatches renamed; most audit items were FALSE POSITIVES on re-verify (AI markers live in registration tables, Deployment projects reflection-discovered by design, IAgentToolSource has a production impl) — kept with documented reasons in structure.md DELETION RESULT.
5. ~~File/namespace normalization~~ **DONE** (e90d2cf65..4b3b5fe43): namespace rule ratified (module-root; sub-namespaces mirror folders) — 0 violations across 1275 files; Platform owns DigitalBrain.Platform.* namespaces; 22 multi-type files split (UiKitEndpoints 32→27 files, route fact green throughout); all 78 boilerplate summaries gone (converted-to-// where substantive, residuals deleted after review); Aspire signal fact fixed — ZERO failing facts repo-wide.
   split the worst multi-type files (UiKitEndpoints first), kill the 78 summaries.
6. **Framework self-tests + coverage gaps**: last, once the suites are lean.

Kernel redesign (was discussed separately): phases 1+2 DONE — brain in Core, Core/Kernel naming,
DigitalBrain.Platform credential ring script-invisible by assembly identity.
