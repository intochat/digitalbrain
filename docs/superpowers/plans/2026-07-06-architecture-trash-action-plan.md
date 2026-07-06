# DigitalBrain Architecture Trash Action Plan

Source: `docs/architecture-trash-analysis-2026-07-06.md`.

Goal: convert the analysis into implementation-ready slices that improve the self-evolution safety rail first, remove proven trash second, and only then refactor large files. The product test for every slice is: does this make user-approved, journaled, rollbackable self-evolution safer and easier to verify?

## Current State Snapshot

- Slice 1 from the analysis is already implemented: `DigitalBrain.Core/SelfEvolution.cs` defines `SelfEvolutionProposal`, `SelfEvolutionDecision`, and `SelfEvolutionRisk`; `DigitalBrain.Tests/Kernel/SelfEvolutionContractTests.cs` pins the contract.
- Slice 2 is already in-flight in the working tree: the four empty `DigitalBrain.Developer` / `DigitalBrain.Windows` project files are staged for deletion, while `Brain.slnx` and `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` contain the expected reference removals as unstaged edits.
- `CheckpointRestoreTrigger` already exists and calls `RestoreCheckpointAsync` through `NeuronResolver`; the rollback work is therefore API exposure, test coverage, and self-evolution integration, not a completely missing executor.
- The active high-risk side doors are still real:
  - `MarketplaceNeuron` installs and immediately delivers `NeuroPackInstalled` to `GeneratedNeuron`.
  - `DigitalBrainMutationTools.define_reaction` registers executable C# scripts directly.
  - `run_code_foundry` defaults `autoApply` to `true`; `CodeFoundryClosedLoopNeuron` currently runs generated code without consulting the approval rail.

## Execution Rules

- Keep slices independently buildable and reviewable.
- Do not mix mechanical project cleanup with self-evolution behavior changes.
- Preserve fast local loops: default verification is targeted `dotnet build` plus focused `dotnet test --filter ...`; full Aspire validation is reserved for approval rail, journal, rollback, or mutation-path changes.
- All self-evolution contracts must be additive once journaled. Do not rename or remove serialized fields from `SelfEvolutionProposal` or `SelfEvolutionDecision`.
- Every mutation path must either use the approval rail or be explicitly categorized as trusted seed/bootstrap behavior with tests proving the exception.

## Phase 0: Stabilize The Current Tree

### 0.1 Finish The Empty-Project Removal

Objective: complete the already-started cleanup of now-empty `DigitalBrain.Developer` and `DigitalBrain.Windows` shells.

Actions:

- Keep the staged deletions of:
  - `DigitalBrain.Developer/DigitalBrain.Developer.csproj`
  - `DigitalBrain.Developer.Tests/DigitalBrain.Developer.Tests.csproj`
  - `DigitalBrain.Windows/DigitalBrain.Windows.csproj`
  - `DigitalBrain.Windows.Tests/DigitalBrain.Windows.Tests.csproj`
- Keep the `Brain.slnx` removals for those four projects.
- Keep the `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` removals for the two runtime project references.
- Search for residual references to `DigitalBrain.Developer` and `DigitalBrain.Windows` in source and project files. Remaining hits should be historical docs only.

Verification:

- `dotnet build Brain.slnx -p:SkipFlutterBuild=true -p:SkipDeployBuild=true`
- `dotnet test DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName~Architecture|FullyQualifiedName~SelfEvolution"`

Acceptance:

- Build has no missing project references.
- `Brain.slnx` has no Developer/Windows project entries.
- `DigitalBrain.Kernel.csproj` has no Developer/Windows project references.

### 0.2 Commit Boundary

Objective: make the cleanup reversible and separate from behavior work.

Actions:

- Stage `Brain.slnx` and `DigitalBrain.Kernel/DigitalBrain.Kernel.csproj` alongside the staged project deletions.
- Commit as a standalone cleanup commit.

Acceptance:

- The commit contains only the four project removals and the six reference removals.

## Phase 1: Make The Approval Rail Enforceable

### 1.1 Add A Self-Evolution Approval Consumer

Objective: turn proposal records into a real gate.

Design:

- Add a kernel grain such as `SelfEvolutionNeuron`.
- Add a Core interface such as `ISelfEvolutionNeuron : INeuron, IHandle<SelfEvolutionProposal>, IHandle<SelfEvolutionDecision>`.
- The grain owns the projection of:
  - pending proposals
  - accepted decisions
  - rejected decisions
  - expired proposals
  - applied proposals
- The grain must not execute arbitrary `ApplyVia` strings directly. It should route only through allowlisted handlers.

Actions:

- Add `ISelfEvolutionNeuron` to Core near the existing self-evolution records or in a small dedicated Core file.
- Add `SelfEvolutionNeuron` in Kernel.
- On proposal:
  - journal it
  - reject invalid records with empty `ProposalId`, `ApplyVia`, `Origin`, or `RollbackPlan`
  - mark expired proposals inactive when `ExpiresAt <= UtcNow`
  - emit a visible pending signal/surface later, but do not build UI in this slice
- On decision:
  - require non-empty `DecidedBy`
  - require a matching pending proposal
  - reject decisions for expired proposals
  - emit a decision/audit synapse
  - if rejected, stop
  - if approved, enqueue apply through the allowlisted apply handler registry

Tests:

- Proposal is journaled as pending.
- Rejected decision never calls an apply handler.
- Approved decision calls the matching handler exactly once.
- Expired proposal cannot be approved.
- Unknown `ApplyVia` cannot be applied.
- Duplicate decisions do not double-apply.

Acceptance:

- There is one canonical place that consumes `SelfEvolutionProposal`.
- No code path applies a self-evolution proposal just because the proposal exists.
- The tests prove explicit approval is required.

### 1.2 Introduce Apply Handler Registry

Objective: replace stringly-typed apply execution with allowlisted behavior.

Actions:

- Add `ISelfEvolutionApplyHandler` in Kernel, not Core, with:
  - `string ApplyVia`
  - `SelfEvolutionRisk MaxRisk`
  - `Task<SelfEvolutionApplyResult> ApplyAsync(SelfEvolutionProposal proposal, CancellationToken ct)`
- Register handlers through DI.
- Add initial no-op/test handler for unit coverage.
- Add production handlers later per side-door integration:
  - marketplace pack install
  - automation registration
  - foundry run/deploy
  - Aspire kernel restart

Tests:

- Registry rejects missing handler.
- Registry rejects risk mismatch if a handler cannot handle the proposal risk.
- Registry records apply result back to the grain journal.

Acceptance:

- `ApplyVia` is an identifier into trusted code, not executable instruction text.

## Phase 2: Route Side Doors Through The Rail

### 2.1 Marketplace Install Gate

Objective: prevent pack embodiment from bypassing human approval.

Actions:

- In `MarketplaceNeuron.HandleAsync(InstallFromMarketplace)`, split validation from activation:
  - keep ownership, signature, unsigned-pack, license, and commission validation
  - before `NeuroPackInstalled` and generated-neuron delivery, create a `SelfEvolutionProposal`
  - use `SelfEvolutionRisk.PackInstall`
  - set `ApplyVia` to a new marketplace install handler identifier
  - include pack name/version/buyer/session in proposal metadata or a typed command journaled by the handler
- Add a dev/test config switch only if necessary, named explicitly, for trusted local seed installs. Default should favor approval for user-triggered installs.
- Implement the marketplace apply handler so approval performs the current activation steps:
  - fire `NeuroPackInstalled`
  - deliver to `GeneratedNeuron`
  - emit `ExperienceUsed`
  - emit installed-bundles surface
  - trigger kernel self-update if installing the kernel pack

Tests:

- Installing a pack emits a pending proposal and does not embody before approval.
- Rejection leaves no `NeuroPackInstalled`.
- Approval performs the same activation behavior that existed before.
- Trusted seed/dev bypass, if retained, is explicitly covered and disabled for normal user installs.

Acceptance:

- User-triggered marketplace install has the same trust checks as today plus explicit proposal approval before code embodiment.

### 2.2 Automation Definition Gate

Objective: prevent MCP-created executable C# reactions from becoming active without approval.

Actions:

- Change `DigitalBrainMutationTools.DefineReaction` and `CreateAutomationFromDescription` to stage a `SelfEvolutionProposal` when the script body is executable C#.
- Use `SelfEvolutionRisk.InProcessCode`.
- Add an automation apply handler that performs the current `RegisterScript` + `RegisterReaction` flow only after approval.
- Decide and document the bootstrap exception for trusted startup seeds in `Program.cs`; either:
  - keep them as trusted built-in seeds with a clear helper/API name, or
  - move them to seed data that the approval rail marks as trusted origin.
- Make `AutomationNeuron.DefineReactionAsync` either internal to trusted code paths or clearly named as a low-level unsafe method with tests around public entry points.

Tests:

- MCP `define_reaction` creates a proposal, not an active reaction.
- Rejected proposal does not register script or reaction.
- Approved proposal registers both script and reaction.
- Startup seed behavior remains deterministic and documented.

Acceptance:

- Local/trusted MCP transport is no longer enough to activate arbitrary C#; approval is required for user-created automations.

### 2.3 Foundry Gate And `autoApply`

Objective: prevent generated code execution from defaulting to apply/run.

Actions:

- Change the MCP tool default `autoApply` to `false`.
- In `CodeFoundryClosedLoopNeuron.HandleAsync`, if `AutoApply == false`, stop after code generation and emit a proposal.
- For `TargetTier.Run`, use `SelfEvolutionRisk.InProcessCode`.
- For `TargetTier.Deploy`, use `SelfEvolutionRisk.KernelRestart`.
- Add foundry apply handlers:
  - run handler invokes `ICodeRunNeuron` after approval
  - deploy handler invokes `ICodeDeployNeuron` after approval
- Preserve checkpoint creation before apply, but do not call completed/applied until the approved handler succeeds.

Tests:

- `run_code_foundry` default request stages a proposal and does not run generated code.
- `autoApply=true` is either rejected without a privileged/trusted origin or covered by an explicit trusted-dev config.
- Approved run invokes `CodeRunNeuron`.
- Approved deploy invokes `CodeDeployNeuron`.
- Failure emits rollback/audit result.

Acceptance:

- Foundry generation can still be fast, but execution and deploy are gated.

## Phase 3: Durable Audit And Rollback

### 3.1 Self-Evolution Audit Durability

Objective: proposal and decision history must survive kernel restart.

Actions:

- Use the self-evolution grain journal as the canonical audit projection.
- Add a test that deactivates/reactivates the grain and proves proposals and decisions replay into the pending/decided projection.
- For local non-Aspire runs, decide whether in-memory prototype journals are acceptable. If not, add a narrow durable local store for the self-evolution grain first, rather than replacing all journaling.
- Keep `PrototypeJournals` name until durable local journal support exists.

Tests:

- Proposal survives grain reactivation in the test harness.
- Decision survives grain reactivation.
- Applied proposal is not re-applied on replay.

Acceptance:

- The self-evolution rail has durable/replayable state semantics before broad side-door enforcement ships.

### 3.2 Rollback Integration

Objective: make rollback a procedure, not prose.

Actions:

- Reuse existing `CheckpointRestoreTrigger` and `RestoreCheckpointAsync`.
- Add a self-evolution rollback handler or command that points to a checkpoint/manifest.
- Ensure every apply handler records the checkpoint id it would use for rollback.
- Add a failure path where apply failure triggers rollback or emits a rollback-required state with enough data for an operator to execute it.
- Expose rollback through a safe command surface only after the apply audit path exists.

Tests:

- Apply handler records checkpoint id.
- Failed apply emits `FoundryRolledBack` or a new self-evolution rollback synapse with checkpoint id.
- Restore path can restore a known checkpoint through `CheckpointRestoreTrigger`.

Acceptance:

- A reviewer can trace proposal -> approval -> checkpoint -> apply -> rollback path in journals.

## Phase 4: Thin Wrapper Project Merges

### 4.1 Merge `DigitalBrain.Telegram.Channel` Into `DigitalBrain.Telegram`

Objective: remove one interface-only project without changing runtime behavior.

Actions:

- Move `ITelegramChatNeuron` into `DigitalBrain.Telegram`.
- Change namespace from `DigitalBrain.Telegram.Channel` to `DigitalBrain.Telegram`, or intentionally keep a compatibility namespace if external package compatibility matters.
- Update `TelegramChatNeuron` using statements.
- Update tests that reference `ITelegramChatNeuron`.
- Change `DigitalBrain.Kernel.csproj` reference from `DigitalBrain.Telegram.Channel` to `DigitalBrain.Telegram` if not already referenced.
- Delete `DigitalBrain.Telegram.Channel` project and its `Brain.slnx` entry.
- Merge `DigitalBrain.Telegram.Channel.Tests` into an existing test project, preferably `DigitalBrain.Tests` unless the package boundary needs a separate Telegram test assembly.
- Delete the test project and its `Brain.slnx` entry.

Tests:

- Telegram chat binding tests still pass.
- Architecture tests pass.

Acceptance:

- No production project references `DigitalBrain.Telegram.Channel`.
- No solution entry remains for the deleted project.

### 4.2 Merge `DigitalBrain.UiKit` Into `DigitalBrain.Ui.Contracts`

Objective: remove one interface-only project without changing the UI contract boundary.

Actions:

- Move `IFlutterUiNeuron` into `DigitalBrain.Ui.Contracts`.
- Change namespace to `DigitalBrain.Ui.Contracts` or `DigitalBrain.Core` only if boundary tests justify it. Preferred: `DigitalBrain.Ui.Contracts`.
- Update `FlutterUiNeuron` and tests.
- Remove `DigitalBrain.UiKit` reference from `DigitalBrain.Kernel.csproj`.
- Delete `DigitalBrain.UiKit` and its `Brain.slnx` entry.
- Move `FlutterUiNeuronTests` into `DigitalBrain.Tests` or another surviving test project.
- Delete `DigitalBrain.UiKit.Tests` and its `Brain.slnx` entry.
- Update the comment in `DigitalBrain.Core/Synapse.cs` that currently says the specific contracts live in peer projects.
- Update `CoreBoundaryTests` to assert the new ownership.

Tests:

- `FlutterUiNeuronTests` still pass after relocation.
- `CoreBoundaryTests` pass.

Acceptance:

- No production project references `DigitalBrain.UiKit`.
- UI-facing neuron contracts live with UI contracts.

## Phase 5: Large-File Refactors

These should start only after the approval rail and side doors are handled. They improve maintainability but do not fix the central safety issue by themselves.

### 5.1 Split `InoNeuron`

Objective: keep Ino as orchestrator while making intent behavior reviewable.

Actions:

- Add a small intent handler abstraction inside `DigitalBrain.Kernel/Ino`.
- Extract handlers by current domains:
  - Gmail
  - Salesforce
  - marketplace/install
  - schema/data visualization
  - graph canvas
  - generic LLM response
- Keep existing public grain contract unchanged.
- Preserve ordering and fallback behavior with characterization tests before each extraction.

Tests:

- Existing Ino tests pass.
- Add one routing test per extracted handler.

Acceptance:

- `InoNeuron` contains orchestration and classification, not full handler bodies for every domain.

### 5.2 Continue `GatewayService.Send()` Handler Extraction

Objective: replace the broad request-type if/else with per-domain handlers.

Actions:

- Identify existing extracted helpers and continue that pattern.
- Extract domains in this order:
  - marketplace
  - auth/session
  - config
  - demo
  - Ino
  - Telegram
- Keep payload/auth parsing centralized.
- Avoid changing gRPC wire contracts.

Tests:

- `GatewayServiceTests`
- `GatewayGrpcWireTests`
- targeted tests for each extracted domain

Acceptance:

- Adding a new gateway request type no longer requires editing a 150-line branch.

### 5.3 Extract Marketplace UI Action Generation

Objective: make `MarketplaceUiSurfaces` smaller and separate action generation from surface templates.

Actions:

- Extract pack-to-action mapping into a dedicated action generator.
- Preserve Salesforce special cases behind focused tests.
- Keep wire strings and action ids stable.

Tests:

- Marketplace facet/filter/launcher tests.
- Any Salesforce-specific marketplace UI tests.

Acceptance:

- Surface rendering and action generation can be reviewed independently.

## Phase 6: Test Reliability

### 6.1 Shared Async Wait Helper

Objective: remove brittle timing sleeps from fast tests.

Actions:

- Add `WaitUntilAsync` to a shared test helper namespace.
- Replace poll loops and fixed delays in:
  - `GatewayServiceTests`
  - `LoginRendersE2ETests`
- Keep spike tests quarantined if they intentionally use longer polling.

Tests:

- Run affected tests repeatedly if practical.

Acceptance:

- Fast tests do not depend on fixed sleeps for eventual state.

### 6.2 Remove Static Shared LLM Test Factory

Objective: avoid singleton test state that is safe only because parallelization is disabled.

Actions:

- Replace static factory in `LlmResponderScopedConfigTests` with per-test or per-fixture state.
- Prefer test-local state unless fixture construction is expensive.

Tests:

- `LlmResponderScopedConfigTests`

Acceptance:

- Tests do not rely on `.Clear()` against static mutable state.

## Phase 7: Validate Suspicious But Not Proven Trash

### 7.1 `SystemRollingSurfaces`

Objective: decide whether to keep, integrate, or delete.

Actions:

- Search for runtime production paths that emit or consume rolling-update surfaces.
- If only tests reference it:
  - either delete it and the test
  - or wire it into the rollback/self-evolution status surface if it is useful
- Do not delete in the same commit as approval rail changes.

Acceptance:

- The file is either live by a production path or removed with tests adjusted.

### 7.2 `PrototypeJournals`

Objective: keep the name honest until durability changes.

Actions:

- Do not rename it before durable local journal support exists.
- If self-evolution gets a durable local store, rename only the affected configuration path.

Acceptance:

- Naming reflects actual durability.

## Recommended Commit Order

1. Complete empty-project removal.
2. Add self-evolution consumer and apply-handler registry, with tests and no side-door changes.
3. Gate marketplace install through approval.
4. Gate automation definitions through approval.
5. Gate foundry run/deploy and flip `autoApply` default.
6. Add durable replay tests and rollback integration.
7. Merge Telegram.Channel into Telegram.
8. Merge UiKit into Ui.Contracts.
9. De-flake timing tests and remove static LLM test factory.
10. Refactor Ino, Gateway, and Marketplace UI surfaces in separate commits.

## Verification Matrix

| Slice | Minimum verification |
|---|---|
| Empty projects | `dotnet build Brain.slnx -p:SkipFlutterBuild=true -p:SkipDeployBuild=true`; Architecture tests |
| Approval consumer | SelfEvolution tests; Kernel build |
| Marketplace gate | Marketplace install tests; GeneratedNeuron embodiment tests |
| Automation gate | MCP mutation tests; AutomationNeuron tests |
| Foundry gate | Foundry tests; MCP `run_code_foundry` tests |
| Durable audit | journal replay tests; targeted Aspire run if journal configuration changes |
| Rollback | checkpoint restore tests; foundry rollback tests |
| Project merges | full build; architecture tests; moved interface tests |
| Test de-flake | affected tests repeated locally |
| Large-file refactors | affected domain tests plus full non-E2E suite |

## Done Criteria For The Whole Plan

- All user-created mutation paths stage `SelfEvolutionProposal` before executing generated or installed code.
- `SelfEvolutionDecision` is the only way to approve non-trusted self-evolution.
- Proposals, decisions, apply results, checkpoints, and rollback outcomes are visible in journals.
- Empty and wrapper projects are removed without boundary regressions.
- `InoNeuron` and `GatewayService` become easier to extend without editing high-risk monolith branches.
- Fast test lane is less timing-sensitive.
