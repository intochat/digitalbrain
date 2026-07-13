# DigitalBrain Programmable Features v3 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the approved v3 source-first Feature architecture, two shipped Integration package families, durable Orleans execution rail, collectible hot loading, lexical Memory, Email Summarizer, and every v3 deletion and acceptance gate.

**Architecture:** RuntimeHost remains the credential and authority boundary. FeatureBuilder turns bounded source plus BDD into immutable releases, FeatureHost executes those releases through one grant-validating capability dispatcher, and exactly two new grain types own durable catalog/fan-out and installation/run state. Existing conversation, signed-effect, worker-correctness, connector-verification, Flutter chat, and RFW rails remain live until their callers are deliberately migrated.

**Tech Stack:** .NET SDK 11.0.100-preview.5, `net11.0`, Aspire 13.4.6, Orleans 10.2.1 stable family, Reqnroll 3.3.4, xUnit 2.9.3, Azure Tables/Blobs, System.Text.Json, collectible `AssemblyLoadContext`, Flutter.

## Global Constraints

- Apply Elon's five steps in order for every slice: question, delete, simplify, accelerate, automate.
- Use Context7 before package APIs; while its quota is unavailable, use only official Microsoft, Aspire, Reqnroll, and NuGet sources.
- Use CodeGraph before reading or editing indexed source, then `rg` only for textual confirmation.
- Keep at most one slice in progress and use red-green-refactor without `--filter`.
- Run the owning test project for red and green, affected suites next, and `dotnet test --logger "console;verbosity=minimal"` before an integrable checkpoint.
- Use `aspire start --isolated --non-interactive` in the worktree, `aspire wait`, `aspire describe`, logs, and traces; stop Aspire before builds that can hit file locks.
- Add no source or configuration comments. The independent comment-deletion pass is measured separately.
- Provider Contracts reference only approved BCL assemblies and contain no Orleans, Aspire, ASP.NET, provider SDK, credential, filesystem, environment, process, networking, or DI dependency.
- Stable capability and event IDs are explicit data and never derived from CLR names.
- RuntimeHost validates every capability operation against owner, installation, digest, capability/version, provider connection, constraints, and grant revision.
- FeatureHost has no credentials and buffers all writes; one fenced `FeatureRunCommit` persists state, completion, acknowledgment, intents, and the completion ledger.
- External effects remain propose-only until the signed-plan authority, approval evidence, worker fence/outbox, connector apply, and verification rail completes asynchronously.
- Production C# must finish at or below 23,897 lines; public types/methods/properties/fields at or below 400/2,100/1,000/500.
- Final repository shape is 22 platform projects plus Email Summarizer implementation and BDD test projects.

## Baseline Evidence

- Branch/worktree: `codex/programmable-features-v3` from `84ca7d4` in a clean isolated worktree.
- Dirty owner files remained outside the isolated worktree.
- Toolchain: .NET `11.0.100-preview.5.26302.115`; Aspire CLI/AppHost `13.4.6`; Docker `29.6.1` healthy.
- Current graph: 17 C# projects plus `Flutter.proj`; 64 unique direct packages; six storage resources including obsolete `journal`; three RuntimeHost replicas, one MCP/UI Edge, no FeatureHost.
- Production C#: 24,897 lines in 162 tracked files.
- Public API: 483 types, 2,577 methods, 1,182 properties, 644 fields, measured from built assembly metadata.
- Root tests: 408 passed, 0 failed, 0 skipped in 48.1 seconds.
- Current Orleans references are mixed across 10.2.0, 10.2.1-preview.1, and journaling alphas; official NuGet metadata shows 10.2.1 stable across the core family.

---

### Task 1: Shipped Integration Contracts seam

**Files:**
- Create: `integrations/DigitalBrain.Integrations.Google.Contracts/DigitalBrain.Integrations.Google.Contracts.csproj`
- Create: `integrations/DigitalBrain.Integrations.Google.Contracts/GoogleCapabilities.cs`
- Create: `integrations/DigitalBrain.Integrations.Google.Contracts/GmailContracts.cs`
- Create: `integrations/DigitalBrain.Integrations.Salesforce.Contracts/DigitalBrain.Integrations.Salesforce.Contracts.csproj`
- Create: `integrations/DigitalBrain.Integrations.Salesforce.Contracts/SalesforceCapabilities.cs`
- Create: `integrations/DigitalBrain.Integrations.Salesforce.Contracts/SalesforceContracts.cs`
- Create: `tests/DigitalBrain.IntegrationContractTests/DigitalBrain.IntegrationContractTests.csproj`
- Create: `tests/DigitalBrain.IntegrationContractTests/IntegrationContractBoundaryTests.cs`
- Create: `tests/DigitalBrain.IntegrationContractTests/StableCapabilityIdTests.cs`
- Modify: `Brain.slnx`

**Interfaces:**
- Produces `IGmailMessageReader`, `IGmailMailboxReader`, `IGmailSendProposer`, `ISalesforceRecordReader`, and `ISalesforceUpdateProposer` with `CancellationToken` last and optional.
- Produces explicit IDs `google.gmail.message.read.v1`, `google.gmail.mailbox.read.v1`, `google.gmail.send.propose.v1`, `salesforce.record.read.v1`, and `salesforce.record.update.propose.v1`.
- DTOs expose bounded provider identifiers and requested content but no credentials, host configuration, authority claims, or runtime services.

- [x] Write boundary and stable-ID tests that reference the two empty Contracts projects and fail because the interfaces and IDs do not exist.
- [x] Run `dotnet test tests/DigitalBrain.IntegrationContractTests/DigitalBrain.IntegrationContractTests.csproj --logger "console;verbosity=minimal"` and record the missing-type failure.
- [x] Add the smallest contracts shown by the test API, with no package or project references.
- [x] Re-run the owning project and require all boundary, signature, identifier, and public-surface tests to pass under 10 seconds.
- [x] Add all three projects to `Brain.slnx`, remeasure project/API/production-line counts, and commit `feat: add integration contract packages`.

Task 1 evidence: the missing namespaces/types produced the expected compile-time red; the strengthened boundary suite then produced seven behavioral/public-surface reds before implementation. The final focused suite passed 12 tests in 66 milliseconds, and the exact root suite passed 420 tests with no failures or skips. Both Release Contracts packages packed successfully, and independent review plus re-review found no remaining Critical or Important issues. The additive checkpoint has 20 C# projects and 21 solution entries including Flutter, 25,342 production C# lines in 166 files, and 502 public types / 2,582 methods / 1,220 properties / 649 fields. Later deletion tasks remain responsible for the approved final gates.

### Task 2: Feature SDK, testing kit, and Email Summarizer source

**Files:**
- Create: `src/DigitalBrain.Features.Sdk/DigitalBrain.Features.Sdk.csproj`
- Create: `src/DigitalBrain.Features.Sdk/FeatureContracts.cs`
- Create: `src/DigitalBrain.Features.Sdk/FeatureContext.cs`
- Create: `src/DigitalBrain.Features.Testing/DigitalBrain.Features.Testing.csproj`
- Create: `src/DigitalBrain.Features.Testing/FeatureScenarioContext.cs`
- Create: `features/EmailSummarizer/DigitalBrain.Features.EmailSummarizer.csproj`
- Create: `features/EmailSummarizer/EmailSummarizer.cs`
- Create: `features/EmailSummarizer.Tests/DigitalBrain.Features.EmailSummarizer.Tests.csproj`
- Create: `features/EmailSummarizer.Tests/EmailSummarizer.feature`
- Create: `features/EmailSummarizer.Tests/EmailSummarizerSteps.cs`
- Create: `features/EmailSummarizer.Tests/reqnroll.json`

**Interfaces:**
- `IFeature.HandleAsync(FeatureInput input, IFeatureContext context, CancellationToken cancellationToken = default)`.
- `IFeatureContext` exposes only constructor-injected Integration Contracts, Memory read/write, bounded model workflow, state, surfaces, events, and external-effect proposal buffering.
- Email Summarizer consumes `IGmailMessageReader`, emits one surface intent, and contains no runtime-host or provider-SDK dependency.

- [ ] Write the Gherkin happy path, duplicate-input scenario, missing-grant scenario, and model-miss scenario first; configure missing/pending steps as errors.
- [ ] Run the Email Summarizer BDD project and observe undefined bindings/types.
- [ ] Implement the minimum SDK/testing APIs and Email Summarizer handler required by the scenarios.
- [ ] Run Feature and Integration Contract suites; assert the SDK public dependency allowlist and no static mutable state.
- [ ] Add four projects to the solution and commit `feat: add source-first email summarizer`.

### Task 3: Owner identity and generic platform contracts

**Files:**
- Create: `src/DigitalBrain.Kernel.Contracts/DigitalBrain.Kernel.Contracts.csproj`
- Create: `src/DigitalBrain.Kernel.Contracts/Identity.cs`
- Create: `src/DigitalBrain.Kernel.Contracts/Capabilities.cs`
- Create: `src/DigitalBrain.Kernel.Contracts/FeatureGrainContracts.cs`
- Modify all retained Core, Kernel, MCP, UI, Flutter, Integration, deployment, and test callers returned by CodeGraph for `TenantId` and `WorkspaceId`.
- Delete after final-caller proof: `TenantId`, `WorkspaceId`, and `WorkspaceIds` declarations.

**Interfaces:**
- `BrainOwnerId`, `ActorId`, `ProviderConnectionId`, `SessionId`, `FeatureInstallationId`, `ReleaseDigest`, and `GrantRevision` are validated immutable IDs with stable Orleans field IDs where they cross grain boundaries.
- `CapabilityRequest` carries owner, actor, installation, digest, input, logical key, capability ID/version, optional provider connection, bounded payload, deadline, correlation, and causation.

- [ ] Add compile-time and serialization tests for the new identity/envelope API and tests proving old IDs are absent from internal contracts and Flutter DTOs.
- [ ] Migrate one authority boundary at a time without aliases or migration state; run the owning suite after each cut.
- [ ] Merge surviving dependency-light Core/Kernel.Abstractions types into Kernel.Contracts only when their last old-project caller is moved.
- [ ] Commit `refactor: adopt owner-scoped platform contracts` after the exact root suite is green.

### Task 4: Coherent Orleans family and ordinary persistence

**Files:**
- Modify: `Directory.Packages.props`
- Modify Orleans registration and state files under `src/DigitalBrain.Kernel` and `hosts/DigitalBrain.RuntimeHost`.
- Modify Orleans tests under the future `tests/DigitalBrain.OrleansTests`.

**Interfaces:**
- Pin all Orleans core, client, server, serialization, clustering, persistence, reminders, and testing packages to stable 10.2.1.
- Use source-generated serialization, awaited ordinary persistent writes, `RegisterGrainTimer`, persistent reminders as wake-up hints, and no journaling API.

- [ ] Add a package-family architecture test that fails on mixed Orleans versions and journaling references.
- [ ] Use `dotnet-inspect diff` for every referenced package moving from preview/stable 10.2.0 to 10.2.1 and address the single observed `ClusterMembershipOptions.MaxDefunctSiloEntries` removal if used.
- [ ] Replace journal-backed behavior only after equivalent ordinary-persistence tests pass.
- [ ] Remove journaling package references, configuration, state, tests, and the AppHost `journal` resource together.
- [ ] Commit `refactor: converge on orleans 10.2.1`.

### Task 5: Shared RuntimeHost capability dispatcher and retained INO migration

**Files:**
- Create: `src/DigitalBrain.Kernel/Capabilities/CapabilityDispatcher.cs`
- Create: `src/DigitalBrain.Kernel/Capabilities/CapabilityGrantValidator.cs`
- Create Integration handlers under the renamed Google and Salesforce runtime projects.
- Modify: `src/DigitalBrain.Kernel/Runtime/AgentFrameworkWorkflowRunner.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/InoEffectPlanNeuron.cs`
- Delete after final-caller proof: `PlanInoToolGateway*`, `IInoToolGateway`, `IInoOperationCapability`.

**Interfaces:**
- `ICapabilityDispatcher.ExecuteAsync(CapabilityRequest request, CancellationToken cancellationToken = default)` is the only dispatch seam.
- Handlers register explicitly by stable capability ID; duplicate IDs fail startup.
- Query returns immediately, InternalWrite returns a buffered intent, ExternalEffect returns a proposal intent.

- [ ] Test exact-grant acceptance, wrong digest/connection/revision rejection, pause/revocation on the next call, bounded payload/deadline checks, and duplicate handler registration.
- [ ] Migrate retained INO Gmail and Salesforce reads through the dispatcher while characterization tests keep output stable.
- [ ] Migrate effect proposals through the dispatcher while preserving signed-plan and verifier tests.
- [ ] Prove the final gateway/provider-switch callers are gone, delete them, and commit `refactor: route operations through capability dispatcher`.

### Task 6: FeatureBuilder immutable release pipeline

**Files:**
- Create: `hosts/DigitalBrain.FeatureBuilder/DigitalBrain.FeatureBuilder.csproj`
- Create: `hosts/DigitalBrain.FeatureBuilder/Program.cs`
- Create: `hosts/DigitalBrain.FeatureBuilder/FeatureBuildRequest.cs`
- Create: `hosts/DigitalBrain.FeatureBuilder/FeatureBuildPipeline.cs`
- Create: `hosts/DigitalBrain.FeatureBuilder/FeatureReleaseWriter.cs`
- Create builder tests in `tests/DigitalBrain.IntegrationContractTests` and `tests/DigitalBrain.E2ETests`.

**Interfaces:**
- Input is one bounded source snapshot, offline allowlisted feed, output directory, and deadline.
- Output is implementation assembly/private outputs, compiled-derived manifest, scenario result, source reference, SHA-256 digest, and no custom archive.

- [ ] Test forbidden package, path traversal, oversize/count limits, undefined/pending/ambiguous steps, timeout, nondeterministic input, and successful Email Summarizer release.
- [ ] Implement restore with no unrestricted network and a 10-second budget; compile plus BDD has a 60-second hard ceiling.
- [ ] Verify deterministic digest and release generation under five seconds after build.
- [ ] Commit `feat: build immutable feature releases`.

### Task 7: Feature state machines and exactly two new grain types

**Files:**
- Create pure transition files under `src/DigitalBrain.Kernel/Features`.
- Create: `src/DigitalBrain.Kernel/Features/FeatureHubGrain.cs`
- Create: `src/DigitalBrain.Kernel/Features/FeatureInstallationGrain.cs`
- Create Orleans tests under `tests/DigitalBrain.OrleansTests`.

**Interfaces:**
- Hub key is `BrainOwnerId`; installation key is owner plus installation.
- Installation operations cover append, claim with lease/fence, fail/retry/park, schedule occurrence, commit, list/apply intents, pause/resume, release switch, and rollback.
- `FeatureRunCommit` atomically includes new state, input acknowledgment, completion entry, and at most 32 intents.

- [ ] Write pure transition tests for every 100/1,000/64-KiB/32/60-second/20-read/4-model/5-attempt limit.
- [ ] Write Orleans-host tests for duplicate delivery, ambiguous retry, stale fence, crash/lease expiry, reminder duplication, downtime catch-up, restart, and independent fan-out.
- [ ] Implement the two grains without reentrancy, reflection serialization, provider I/O, or success-before-write.
- [ ] Assert no third new grain type exists and commit `feat: add durable feature grains`.

### Task 8: FeatureHost collectible loading and bounded execution

**Files:**
- Create: `hosts/DigitalBrain.FeatureHost/DigitalBrain.FeatureHost.csproj`
- Create: `hosts/DigitalBrain.FeatureHost/Program.cs`
- Create: `hosts/DigitalBrain.FeatureHost/FeatureReleaseLoadContext.cs`
- Create: `hosts/DigitalBrain.FeatureHost/FeatureReleaseManager.cs`
- Create: `hosts/DigitalBrain.FeatureHost/FeatureExecutionWorker.cs`
- Create unload/restart tests in `tests/DigitalBrain.UnitTests` and `tests/DigitalBrain.E2ETests`.

**Interfaces:**
- SDK and Integration Contracts always resolve from the default context; only release implementation/private dependencies load collectible.
- New release stages and validates, new claims switch atomically, old claims drain, old context unloads, and failed unload requests proof-host recycle.

- [ ] Test type identity, deterministic resolution, concurrency, no recursive callbacks, deadline cancellation, drain, weak-reference unload, failed unload, restart reload, and rollback.
- [ ] Implement one owned worker loop with bounded cancellation/disposal and no credentials or direct mutation clients.
- [ ] Commit `feat: hot load feature releases`.

### Task 9: Lexical Memory capability

**Files:**
- Create Memory contracts in `DigitalBrain.Features.Sdk` and handlers in `src/DigitalBrain.Kernel/Memory`.
- Add Azure Table wiring and tests in IntegrationContract, Unit, and E2E suites.

**Interfaces:**
- `IMemoryRecall` returns at most 20 facts ranked by exact tags, case-insensitive token overlap, then recency.
- `IMemoryRemember` produces a deterministic InternalWrite intent.
- Owner inspect/export/correct/forget use ETag replacement and physical delete.

- [ ] Test deterministic ties, normalization, 2,000-fact capacity, 2-KiB text, 16 tags, conflict, delete, and audit redaction.
- [ ] Implement `memoryfacts` with no embedding/vector dependency and no `MemoryGrain`.
- [ ] Commit `feat: add bounded lexical memory`.

### Task 10: Aspire topology, AppHost tests, and host composition

**Files:**
- Modify: `hosts/DigitalBrain.AppHost/AppHost.cs`
- Absorb surviving `src/DigitalBrain.Aspire` code into AppHost and delete that project after caller proof.
- Create/modify: `tests/DigitalBrain.AppHostTests`.

**Interfaces:**
- One `AddOrleans` model uses Table clustering, named Blob storage, Table reminders; RuntimeHost gets silo reference and MCP/FeatureHost get `AsClient()`.
- Seven resources are clustering, grainstate, conversationstate, sessionstate, surfacefeedstate, Feature source/releases, and memoryfacts.
- Steady local processes are RuntimeHost x3, MCP/UI Edge x1, FeatureHost x1; FeatureBuilder is transient.

- [ ] Use `aspire docs search` and `aspire docs api search --language csharp` before AppHost edits.
- [ ] Write AppHost model tests for resources, references, waits, health, replicas, and absence of journal/embedding Memory dependency.
- [ ] Start isolated, wait for every required resource, capture describe/log/trace evidence, and commit `feat: compose feature runtime topology`.

### Task 11: MCP/UI authoring, approval, grant, installation, and rollback rail

**Files:**
- Modify MCP handlers and UI contracts/runtime under `src/DigitalBrain.Mcp`, `src/DigitalBrain.Ui.Contracts`, and `src/DigitalBrain.Ui.Runtime`.
- Modify Flutter chat/feed/RFW code only through existing live routes.

**Interfaces:**
- Shipped and runtime-authored source call the same build, source/capability diff, exact-digest approval, grant, install, update, pause, resume, park inspection, and rollback APIs.
- Approval shows digest, requested capabilities, connection, constraints, and revision.

- [ ] Add transport tests proving FeatureHost cache cannot grant, revoke/pause takes effect next operation, and payloads never enter logs/audit.
- [ ] Add Flutter tests for chat-based authoring and approval while preserving `/chat`, `RuntimeShell`, auth, feed, and `SurfaceView`.
- [ ] Commit `feat: expose feature lifecycle rail`.

### Task 12: Gmail events and Salesforce external-effect scenario

**Files:**
- Add Google event/watch handling in the Google runtime package.
- Add Salesforce proposal/apply/verify handlers in the Salesforce runtime package.
- Add E2E scenarios under `tests/DigitalBrain.E2ETests`.

**Interfaces:**
- `gmail.message.received.v1` carries only owner, IDs, correlation/causation, occurrence, and bounded minimal facts.
- Salesforce updates are buffered proposals keyed by installation + input + logical operation key and applied only after signed approval/policy evidence.

- [ ] Test watch replay, duplicate event, slow-installation isolation, full-inbox pause/alert, proposal idempotency, approval delay, connector verification, and outcome event.
- [ ] Verify FeatureHost receives no provider credential and does not block for approval.
- [ ] Commit `feat: connect feature event and effect rails`.

### Task 13: Deletion and target-project convergence

**Files:**
- Delete final-caller-proved generic Neuron/Synapse, reflection dispatch, checkpoint/schema/pack runtime, old TestKit, gateways, journal, superseded projects/docs, and dead Flutter `brain_painter.dart`/`comet.dart`.
- Move shared palette/layout types still used by RFW before deleting visualization owners.
- Rename Integration runtime projects to `DigitalBrain.Integrations.Google` and `DigitalBrain.Integrations.Salesforce`.
- Split tests into Unit, IntegrationContract, Orleans, AppHost, and E2E projects.

- [ ] Re-run CodeGraph immediately before each deletion and remove code, tests, registrations, references, configuration, and storage together.
- [ ] Run the separate comment-deletion pass and verify tracked source/config contains no forbidden comment marker.
- [ ] Assert exactly 24 projects, no deleted namespace/type/reference, Kernel has no provider type, and all preserved rails still have characterization coverage.
- [ ] Remeasure production C# and public API; delete or internalize until every v3 gate passes.
- [ ] Commit `refactor: converge on programmable features architecture`.

### Task 14: Full acceptance, budgets, and integration handoff

**Files:**
- Modify only code/tests needed to close observed acceptance failures.

- [ ] Run UnitTests under 5s, IntegrationContractTests under 10s, OrleansTests under 60s, AppHostTests under 90s, E2ETests under 5m, Flutter tests under 90s, and exact root .NET tests under 60s.
- [ ] Exercise shipped/runtime source parity, hot update without host restart, rollback, restart reload, duplicate delivery, stale lease, revocation, backpressure, failed unload recycle, Memory lifecycle, and external effect verification.
- [ ] Run `aspire doctor --non-interactive`, isolated start/waits, describe, structured logs, traces, and stop.
- [ ] Run clean diff, secret/payload log scan, dependency graph, production-line/API metrics, project/storage/process counts, and deletion ledger audit.
- [ ] Request independent review, address only evidence-backed findings, create intentional commits, and report exact acceptance evidence without claiming deferred non-goals.

## Plan Self-Review

- Spec coverage: every v3 section maps to Tasks 1–14; no acceptance gate is deferred.
- Big-bang check: identity, dispatcher migration, journal deletion, host composition, and project consolidation have separate caller-proof checkpoints.
- Migration check: local/dev data is disposable; no state, identity, Feature-schema, or compatibility migration is introduced.
- Abstraction check: only explicitly required SDK contracts, dispatcher, two grains, builder, loader, and Memory capability are added.
- Failure coverage: duplicates, ambiguity, timeout, stale fences, restart, unload, revoke, capacity, and effect verification are explicit red tests.
- Version check: Aspire and Reqnroll stay on deliberate stable versions; Orleans converges to the current stable 10.2.1 family after API diff evidence.
- Placeholder scan: no task contains TBD/TODO/later-without-owner language; every deferred item is a v3 non-goal rather than an implementation gap.
