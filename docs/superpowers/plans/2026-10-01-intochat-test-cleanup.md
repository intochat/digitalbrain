# IntoChat Test Cleanup Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox syntax for tracking.

**Goal:** Keep only host composition and full-stack journeys in IntoChat tests.
**Architecture:** Move behavior coverage to its owning module and delete redundant guards. Packaging gets one parsed composition test.
**Tech Stack:** C#, Orleans, xUnit, ASP.NET Core, Aspire.
**Spec:** `docs/superpowers/specs/2026-10-01-intochat-cleanup-marketplace-design.md`

## Global Constraints

- Execute two phases as separate commit series.
- Preserve existing uncommitted master-key work, including the modified `MasterKeyHostingFacts.cs`.
- Run dotnet test per affected project, never the solution.
- Do not run the 18-minute IntoChat E2E suite; compile it to validate moved/shared support code.
- Use target-module test hosts, real grains, sentence-shaped names, and TestContext.Current.CancellationToken.
- Trust the supplied audit; re-check changed files and target test idioms, rather than repeat the audit.

## Review Focus

- Packaging entries duplicated or omitted: fail the composition test, including an empty module set (task 1).
- Simultaneous draft tests: separate runner instances must never share scripted verdicts (task 3).
- Stale document saves after subsequent edits: saved revision stays bound to the prepared save (task 2).
- Foreign pagination cursor: return BadRequest without leaking another brain's history (task 3).
- Resource relocation: retain current master-key assertions and preserve unstaged unrelated changes (task 2).

## Task 1: Remove obsolete guards and replace packaging coverage

**Files:** `src/Applications/IntoChat/Tests/Unit/{PathTruthFacts.cs,IntoChat.Tests.Unit.csproj,ModuleOptionsFacts.cs}`; delete `HygieneFacts.cs`, `Operations/{HostedDeploymentFacts.cs,HostedSdkModuleFacts.cs}`, `Diagnostics/OtlpParserFacts.cs`, `Mcp/NeuronContractFacts.cs`.
**Interfaces:** Keep `PathTruthFacts.EveryProjectReferenceResolvesToAnExistingFile()`; introduce `ContainerModuleListsAgreeAndResolve()` in that class.

- [x] Replace packaging facts with a test parsing Dockerfile assignments and Container.pubxml environment items. Assert nonempty indexed collections, distinct indices, equal sorted module sets, no developer-profile types, and `Assert.NotNull(Type.GetType(entry))` for every entry. Check parser behavior for actual file formatting; do not silently ignore malformed module entries.
- [x] Run `dotnet test src/Applications/IntoChat/Tests/Unit/IntoChat.Tests.Unit.csproj --filter FullyQualifiedName~PathTruthFacts`. Confirm the new test catches any real packaging mismatch before repairing it.
- [x] Reconcile actual packaging manifests if needed; delete the obsolete facts and linked Otlp parser compile item. Keep ModuleOptions' product assembly reflection sweep and delete the arbitrary count; remove duplicate generic coverage only, preserving the sweep's assertions.
- [x] Run the full IntoChat Unit project; record failures outside this task separately. The final phase must have zero failures.
- [x] Stage exact changed files and commit `test: replace IntoChat packaging guards with composition coverage`.

## Task 2: Move module behavior tests

**Files and ownership:**
- `Unit/Compute/ChaosChargeFacts.cs` → `src/Modules/DigitalBrain/Compute/DigitalBrain.Modules.Compute.Tests.Unit/ChaosChargeFacts.cs`.
- `Unit/Agent/ComputeUsageFacts.cs` → `src/Modules/DigitalBrain/Assistant/DigitalBrain.Modules.Assistant.Tests.Unit/ComputeUsageFacts.cs`.
- `Unit/MasterKeyHostingFacts.cs` → `src/Modules/DigitalBrain/Kernel/DigitalBrain.Core.Tests.Unit/MasterKeyHostingFacts.cs`; add its hosting project reference there.
- `E2E/LocalApps/LocalAppNeuronFacts.cs` → `src/Modules/Files/DigitalBrain.Modules.Files.Tests.Unit/LocalAppNeuronFacts.cs`; retain image editing/save behavior, replace IntoChat lease/upload infrastructure with that module's test setup.
- `E2E/Workspace/ShellPersistenceFacts.cs` HTTP fact → `src/Modules/Google/Flutter/DigitalBrain.Modules.Flutter.Tests.E2E/ShellPersistenceHttpFacts.cs`.
- `E2E/Security/CrossWorkspaceFacts.cs` slider scoping fact → `src/Modules/Google/Flutter/DigitalBrain.Modules.Flutter.Tests.Unit/Slider/SliderFacts.cs`.
- Delete `E2E/Compute/ComputeLimitsFacts.cs`.
**Interfaces:** No production API changes; the source tests' behavior assertions survive with target test-host idioms.

- [x] Compare only candidate assertions with existing owning-module coverage; delete duplicates. Keep Chaos' 64 concurrent authorizations, one reservation, and one settlement totaling 90m under a 100m allowance; delete its other two facts.
- [x] Move the remaining facts, rewriting namespace and setup. Retain LocalApp's idempotent edits, stale-revision rejection, save completion bound to revision 1 after revision 2, and other-brain isolation. Retain HTTP shared state/stale-write behavior and browser coverage at the host.
- [x] Move the current edited MasterKeyHostingFacts intact, then adapt namespace/imports. Save its original diff for final comparison; stage no unrelated master-key implementation files. Remove IntoChat Unit's direct Mcp, hosting, and Compute references after checking consumers.
- [x] Run each affected target test project separately, plus IntoChat Unit; build IntoChat E2E. Expect all affected tests to pass and no missing references.
- [x] Commit moves by owning module, with a separate hosting-test move commit identifying preserved pre-existing assertions.

## Task 3: Simplify draft tests and E2E helpers

**Files:** `Unit/Marketplace/{AppDraftFacts.cs,ScriptedTestRunner.cs}`; `E2E/Diagnostics/TraceAssertions.cs` (new); `E2E/{Diagnostics/TraceBudgetFacts.cs,Security/CanarySecretFacts.cs,Security/ContentCaptureFacts.cs,Security/CrossWorkspaceFacts.cs,Security/GrantRevokeFacts.cs,Receipts/ReceiptJourneyFacts.cs}`; owning Identity and CSharp E2E test files; Assistant Unit `ComputeUsageRouteFacts.cs` (new).
**Interfaces:** `ScriptedTestRunner.BySourceMarker` becomes an instance property. `TraceAssertions` owns existing `TextOf`, `WaitForAsync`, and `IsGenAiSpan` signatures; retain current timeout/cancellation behavior. Cookie setup uses `People.SignedIn`.

- [x] Merge the two no-sandbox build facts into one verifying upfront rejection and an unstranded draft state. Register a distinct ScriptedTestRunner instance per test host and set markers through that instance.
- [x] Extract shared trace helpers without adding tests of test infrastructure. Replace copied cookie clients with the existing People helper.
- [x] Move AnotherOwnersSecretIsForbidden into Identity tests and CSharpConsoleIsAvailableOnlyWhenTheHostComposesAuthoring into CSharp E2E using their established fixtures. Remove the second full IntoChat host setup.
- [x] Create Assistant module route coverage for `limit=0` and a cursor from another brain, expecting BadRequest; retain nontrivial page ordering/history behavior in the owner if missing. Strip paging assertions from the host receipt fact, keeping durable usage-to-stream correspondence.
- [x] Run IntoChat Unit and the affected Assistant, Identity, and CSharp test projects per project. Build IntoChat E2E; expect a clean build without removed helper references.
- [x] Commit `test: simplify draft coverage and consolidate journey helpers`.

## Task 4: Phase 1 acceptance

- [x] Run all test projects touched by this phase individually. Require the single packaging test and full IntoChat Unit suite to pass.
- [x] Invoke the Aspire orchestration skill and run `aspire run` from `src/Applications/IntoChat/AppHost`; require every resource Healthy, then stop only processes started for this smoke.
- [x] Update CLAUDE.md's known-failure note only after recording green results. Review the diff for test ownership, removed direct project references, and preservation of existing master-key work; commit the phase verification note and documentation.
- [x] Continue with `2026-10-01-marketplace-extraction.md`.

Execution evidence and documented adaptations: see ../verification/2026-10-01-intochat-test-cleanup.md and ../verification/2026-10-01-marketplace-extraction.md.
