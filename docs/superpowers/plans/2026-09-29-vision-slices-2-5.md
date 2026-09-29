# Vision Slices 2–5 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Finish the programmable-brain consolidation: apps made of several behavior scripts, verification as generated C# tests with the exit code as the publish gate, the step-vocabulary path deleted, Author/Builder compiling free-language specs, and UI-as-neurons groundwork.

**Architecture:** A package's `behaviors/*.cs` files each become one `ICSharpFile` on install. Verification runs the revision's `tests.cs` as an ordinary sandbox script (same trust boundary as any app script: the edge, installed contracts, per-run token); the script installs scratch apps itself, drives them through real contracts (`IApp`, `IScriptedLLM`, `IGroupChat`), and reports one `dbtest:pass|fail` line per scenario; `AppVerificationNeuron` parses those lines and the exit code. The Gherkin/step machinery (AppSteps, ModelSteps, GroupChatSteps, IFeature binding in the apps path) is deleted; the `Specs` module itself stays for the platform's own harnesses.

**Tech Stack:** Existing Orleans/Neuron machinery, `ICSharpFile` + sandbox, xUnit v3, Flutter (Dart) for the two spec screens. No new packages.

**Spec:** `docs/superpowers/specs/2026-09-29-programmable-brain-vision-design.md`. One deliberate deviation, ruled and ledgered: verification runs `tests.cs` as a sandbox script against GUID-scoped scratch installs in the live brain rather than booting an ephemeral `UnitBrain` inside the sandbox. The spec's actual goals — no LLM in the gate, no injection doors, tests play the connectors through real contracts, deterministic exit-code gate — all hold; the ephemeral-brain *form* would push the shipped-apps gate out of the unit suite into docker-only E2E and require the whole platform to compile inside the verification container. Revisit with the permissions slice.

## Global Constraints

- Owner code rules: no meaningless `///` docs; self-explanatory names; sparse inline comments.
- `Arm`/`Trigger` and the durable-subscription machinery from slice 1 stay untouched.
- Persisted state: append `[Id(n)]`, never renumber; arrays over interface lists in stored state (slice-1 lesson).
- Per-project `dotnet test`; skip the 18-minute IntoChat E2E; `aspire run` smoke at the end.
- The 5 pre-existing IntoChat unit failures (HostedDeployment ×2, PathTruth, ModuleConfigurationContract ×2) are not ours and stay.
- Wire shape changes (AppSpecView, AppDraftView, AppSnapshot) must be mirrored in the Flutter Dart screens in the same task that changes them.

## Review Focus

1. A csharp app whose revision has several behaviors must retire *all* of them on uninstall/upgrade — no orphaned running scripts (pinned by `AllBehaviorsRetireOnUninstall`, Task 1).
2. A test run that crashes before printing any scenario line must be red, never vacuously green (pinned by `ANoScenarioRunIsNotGreen`, Task 2).
3. A forged scenario line inside an app's own output must not fake the gate: verdicts come only from the tests file's run, whose logs an app script cannot write to (argue in review; the run is a dedicated `ICSharpFile` keyed under `specs/`).
4. Publishing a revision with tests but no verification (or a red one) must still be refused (pinned by adapted `AFailingScenarioKeepsTheRevisionOutOfTheMarketplace`, Task 2).
5. The deleted `/packages/steps` endpoint and vocabulary must leave no dangling Flutter caller (checked in Task 5 by grep + analyze).

---

### Task 1: Multi-behavior packages

**Files:** `PackageContent.cs`, `Workspace/App.cs`, `Workspace/AppState.cs`, `Workspace/AppSnapshot.cs` (Apps module + contracts); `InstalledPackageView.cs`, `PackageService.cs` (IntoChat); tests `AppFacts.cs`.

**Produces:**
- `PackageContent.BehaviorsPrefix = "behaviors/"`; `IReadOnlyDictionary<string,string> Programs()` — `Source` as `"app.cs"` when non-empty, plus every `Files` entry under `behaviors/` ending in `.cs`, ordered by path.
- `AppState.ScriptPaths` — `[Id(11)] string[] ScriptPaths = []`.
- `AppSnapshot.CSharpFile` (Id 4) replaced by `[property: Id(4), JsonPropertyName("csharpFiles")] IReadOnlyList<string> CSharpFiles` (empty when not a script app).
- `App.Deploy`/`Retire` loop over programs; `FileKey(generation, path)` hashes key + generation + path.
- `InstalledPackageView(AppSnapshot App, IReadOnlyList<CSharpFileSnapshot> Files)`.

Steps: failing facts first (`ARevisionWithSeveralBehaviorsRunsOneFilePerBehavior`, `AllBehaviorsRetireOnUninstall` in AppFacts, mirroring its existing csharp fact style), watch fail, implement, module suite green, commit.

### Task 2: Verification runs tests.cs; step vocabulary leaves the Apps module

**Files:** `PackageContent.cs`, `Verification/IAppVerification.cs` (+ new `AppTestRun`/`AppScenarioVerdict` records), `Verification/AppVerificationNeuron.cs`, `Packages/PackageNeuron.cs`, `AppsModule.cs`, delete `Verification/AppSteps.cs`; Apps.Contracts csproj drops the Specs reference; tests: `AppVerificationFacts.cs` rewrite, `FakeSandbox` gains settable `Logs`.

**Produces:**
- `PackageContent.SpecPath = "app.spec.md"`, `PackageContent.TestsPath = "tests.cs"`.
- `[GenerateSerializer] AppScenarioVerdict(string Name, bool Passed, string Message)`; `AppTestRun(AppScenarioVerdict[] Scenarios, int ExitCode)` with `Green => ExitCode == 0 && Scenarios.Length > 0 && all passed`.
- `AppVerification(Revision, AppTestRun Run, VerifiedAt)`; `FeatureKey` deleted.
- Verify(): write `tests.cs` to `ICSharpFile($"specs/{pkg}@{rev}/{guid}/tests")`, Configure `{Package, Revision}`, Start, poll `Read()` (1s, 55-minute deadline) until `ExitCode` is set or status `Exited`, `ReadLogs(2000)`, Delete, parse lines `dbtest:pass <name>` / `dbtest:fail <name>\t<message>`.
- `PackageNeuron.RequireVerified` gates when `TestsPath` **or** `SpecPath` present.
- `AppsModule` stops registering `AppSteps`; file deleted.

Protocol note for every later task: the tests script prints exactly one `dbtest:` line per scenario and exits non-zero when any failed.

### Task 3: IntoChat marketplace rewiring — Author/Builder compile free specs

**Files:** `MarketplaceService.cs`, `AppDraft.cs`, `PackageEndpoints.cs`, delete `AppSpecSteps.cs`, rewrite `Agents/author.md` + `Agents/builder.md`; tests: `AppDraftFacts.cs` rewrite.

**Produces:**
- `AppSpecView(Revision, Runtime, Files, string? Spec, string? Tests, AppVerification? Verification)`; `MarketplaceService.Spec` reads files only; `Vocabulary()` and the `/packages/steps` endpoint deleted; `RequireRunnable`: activation required when runtime is csharp **or** tests are present.
- `AppDraftView(Draft, AppVerification? Verification)`; `Author` drops the vocabulary/binding loop (JSON-retry only); `Build` drops `FullyBound`, requires the Builder to return a `tests.cs` file (a missing one is a failed attempt), stores `SpecPath` from the draft spec; `Failures` renders `AppTestRun`.
- author.md: spec is free markdown, `## Scenario: <name>` headings, settings extracted for accounts/keywords/models, runtimes as before.
- builder.md: emit `files` including `tests.cs` following the scratch-install skeleton (documented in the prompt verbatim, including the `dbtest:` protocol, `brain.Setting("Package"/"Revision")`, `IApp` install/invoke/poll, `IScriptedLLM` scripting, and the group-chat chat key `{appKey}/chat/{invocationId:N}`), behaviors under `behaviors/` for csharp apps.
- `AddPackages` drops the two `StepLibrary` registrations.

### Task 4: Shipped apps migrate; ShippedAppFacts keep native runtime coverage

**Files:** `src/Applications/IntoChat/Apps/{WordCount,GroupChat,Assistant}/` — rename `app.feature` → `app.spec.md` (rewritten as free markdown with scenario headings), add `tests.cs` per app; `ShippedAppFacts.cs` rewrite; `ShippedApps.cs` untouched (files travel automatically).

- WordCount tests: three ask/answer scenarios through `IApp`.
- Assistant tests: scripted model via `IScriptedLLM` + configure `Model` setting + ask; the `@live` scenario stays in the spec text as documentation, not in tests (deterministic gate only).
- GroupChat tests: scripted `luna`/`gemma`, configure both model settings, ask, assert turns/agreement via `IGroupChat` at `{appKey}/chat/{invocationId:N}` and the final answer.
- ShippedAppFacts: `GroupChatAssistantAndWordCountShip` stays; `EveryStepOfTheSpec...` replaced by `EveryShippedAppCarriesSpecAndTests`; `DeterministicScenariosPassAgainstTheRealRuntime` re-implemented natively (no steps engine): install scratch app, script models, invoke, assert — same assertions as the old scenarios, compiled.

### Task 5: Flutter screens follow the wire

**Files:** `app_spec_view.dart` (rewrite: spec text + per-scenario verdict rows matched by name), `app_spec_screen.dart` (new AppSpecView fields), `create_app_screen.dart` (drop `feature` map; show spec text + attempts + verification), `packages_screen.dart` + `csharp_manager.dart` if they read `csharpFile`/steps. Verify with `flutter analyze` and grep for `feature[`/`/packages/steps`.

### Task 6: Renderables groundwork + docs + whole-system verification

- One fact proving a script token can reach a UI neuron contract through `ScriptEdge` (e.g. `IForm.Define` + `Read` with the Flutter contracts assembly in `ScriptContracts`), documenting UI-as-edge-neurons for scripts.
- Update the vision spec's consolidation section: mark shipped slices; note the verification-form ruling.
- Suites: CSharp module, Apps module, IntoChat unit, kernel, Assistant tests (uses AppState/StepLibrary? Assistant's own step files remain — Specs module intact). `aspire run` smoke. Commit.

Then the user-ordered final code review over the whole range, with a fix pass.
