# Architecture Convergence Zero Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Do not dispatch subagents unless the user explicitly authorizes parallel agent work.

**Goal:** Delete obsolete and duplicated DigitalBrain systems, converge all external mutation onto the durable INO effect rail, make projects independently testable and loosely coupled, and leave zero comments in tracked source or configuration files.

**Architecture:** Keep one path: `Client -> Edge/Auth -> INO operation -> deterministic function or bounded model workflow -> effect gate -> connector adapter`. Commands and queries use typed grain interfaces. Orleans streams are reserved for progress, fan-out, and observability. The generic Neuron/Synapse runtime, legacy gateway, second auth system, Foundry execution loop, pack runtime, and duplicate UI rail are removed after their remaining behavior is either migrated or explicitly discarded.

**Tech Stack:** .NET 11, C# 14, Orleans, Aspire 13.4, Microsoft Agent Framework, gRPC, Flutter/Dart, xUnit.

## Global Constraints

- COMMENTS ARE FORBIDDEN.
- No tracked C#, Dart, Proto, PowerShell, shell, XML, MSBuild, YAML, or JSON-with-comments file may contain line comments, block comments, documentation comments, commented-out code, generated comments, or explanatory annotations.
- Markdown prose is documentation, not a source-code comment. Only `README.md`, `CLAUDE.md`, and this temporary execution plan may remain while the plan is active.
- Generated source must either be untracked and produced during build or be sanitized before it is tracked. Generated code is not exempt from the zero-comment rule.
- Replace useful comments with names, types, tests, validation, or smaller functions. Delete stale, narrative, redundant, and commented-out code.
- Delete before moving or abstracting. Each task must reduce net production lines unless it is a migration task that enables deletion in the immediately following task.
- Do not introduce a new framework, message bus, generic repository, plugin layer, compatibility facade, or base agent class.
- Do not add an abstraction until two retained production consumers require the same contract.
- No provider name may remain in the runtime core. Provider-specific behavior stays in its integration project.
- No external mutation may bypass `InoEffectPlanAuthority`, durable approval evidence, idempotency, lease/fence checks, and outcome verification.
- No generated or user-provided code may execute inside a trusted Brain process.
- No runtime-library-to-host, runtime-library-to-provider, runtime-library-to-UI, or integration-to-runtime-implementation project reference may remain.
- No task may combine deletion with unrelated formatting or feature work.
- Run builds and tests from the repository root. Do not use test filters.
- Use relative paths in repository files and commands.
- Implementation assumes pre-convergence local durable state may be reset. If production journals or durable state must survive, stop before deleting serialized aliases and replace the reset with an explicit migration approved by the user.

## Target Dependency Direction

```text
DigitalBrain.Core
        ^
DigitalBrain.Kernel.Abstractions
        ^
        +----------------+----------------+
        ^                ^                ^
DigitalBrain.Kernel  DigitalBrain.Google  DigitalBrain.Salesforce
        ^                ^                ^
        +--------- DigitalBrain.RuntimeHost --------+
                           ^
             DigitalBrain.AppHost resource graph

DigitalBrain.Mcp -> DigitalBrain.Kernel.Abstractions
```

The final names may be simplified after deletion, but dependency direction must remain inward. `DigitalBrain.RuntimeHost` is the only process project allowed to compose the runtime with concrete providers. `DigitalBrain.AppHost` models resources and references the RuntimeHost executable without owning its service registrations.

## Measurable Exit Criteria

- One session and identity authority: `RuntimeSessionAuthority` and principal, tenant, and workspace identifiers.
- One external edge contract: V2 UI gRPC plus the retained MCP operation surface.
- One external mutation rail: durable INO effect plans.
- Zero generic Synapse dispatch for connector reads or mutations.
- Zero in-process code generation, compilation, loading, or execution.
- Zero tracked source/configuration comments.
- Zero `ProjectReference` edges from the Kernel runtime library to MCP, integrations, UI runtime, pack contracts, or ServiceDefaults.
- At least 25% fewer non-test C# lines than the 40,801-line baseline.
- At least 25% fewer grain types than the 31-grain baseline.
- No retained handwritten production file above 500 lines.
- `dotnet build Brain.slnx --no-restore --nologo --verbosity:minimal` passes.
- `dotnet test --logger "console;verbosity=minimal"` passes from the repository root.
- `flutter analyze` and `flutter test` pass in `app`.
- `aspire doctor` passes and a minimal AppHost behavior check passes for the V2 UI and one approved Salesforce mutation.

## Execution Protocol

For every task:

1. Record `git status --short`, relevant file/line counts, and the task start time.
2. Write or update the smallest test that protects retained behavior.
3. Run the test and record the expected failure when behavior changes.
4. Delete first.
5. Add only the minimum migration code required for the retained behavior.
6. Run the owning project build and tests.
7. Run the full root test command.
8. Run `aspire doctor`.
9. Inspect `git diff --stat` and reject a net-additive result unless the next task deletes the temporary migration surface.
10. Commit one independently reversible change.

---

### Task 1: Establish the Baseline and the Non-Negotiable Rules

**Files:**

- Modify: `CLAUDE.md`
- Modify: `README.md`
- Create later in Task 11: `tests/DigitalBrain.Tests/Architecture/NoCommentsTests.cs`
- Create later in Task 6: `tests/DigitalBrain.Tests/Architecture/ProjectDependencyTests.cs`

**Interfaces:**

- Consumes: current repository state at `5a2d6b0`
- Produces: one living architecture rule in `README.md` and one execution rule in `CLAUDE.md`

- [ ] Replace the current permissive comment rule in `CLAUDE.md` with the exact global zero-comment rule from this plan.
- [ ] Replace the self-evolution North Star that requires every concept to be a Neuron or Synapse with the single retained execution path from this plan.
- [ ] Add the target dependency direction and mutation invariant to `README.md` in fewer than 30 lines.
- [ ] Record baseline counts with these commands:

```powershell
git ls-files '*.cs' | Get-Content | Measure-Object -Line
git ls-files '*.dart' | Get-Content | Measure-Object -Line
rg -l '^\s*//|/\*|\*/|<!--|-->' -g '*.cs' -g '*.dart' -g '*.proto' -g '*.ps1' -g '*.xml' -g '*.props' -g '*.targets' -g '*.csproj' -g '*.yml' -g '*.yaml' | Measure-Object
```

Expected baseline: about 40,801 C# lines and at least 227 tracked source/configuration files containing comment markers.

- [ ] Run the full baseline verification:

```powershell
dotnet build Brain.slnx --no-restore --nologo --verbosity:minimal
dotnet test --logger "console;verbosity=minimal"
Push-Location app
flutter analyze
flutter test
Pop-Location
aspire doctor
```

- [ ] Commit the rule change.

```powershell
git add CLAUDE.md README.md
git commit -m "docs: define architecture convergence rules"
```

### Task 2: Delete Historical Planning Debris and Unused Assets

**Files:**

- Delete: `docs/adr/`
- Delete: `docs/refinement/`
- Delete: `docs/architecture-assessment-and-plan.md`
- Delete: `docs/execution-log.md`
- Delete: `docs/execution-plan.md`
- Delete: `docs/grok-prompt.md`
- Retain temporarily: `docs/architecture-convergence-zero.md`
- Delete: `app/assets/lottie/orbit.lottie`
- Delete: `app/assets/rfw/activity_overlay.rfwtxt`
- Delete: `app/assets/rfw/sample_neuron.rfwtxt`
- Delete: obsolete Challenger scripts under `app/tool/` that target deleted UI files
- Modify: `app/pubspec.yaml`

**Interfaces:**

- Consumes: decisions copied into `README.md`, `CLAUDE.md`, and this plan
- Produces: one temporary plan and no historical dossier

- [ ] Verify every retained architecture invariant exists in Task 1 documentation.
- [ ] Remove the historical documents and unused assets with `git rm`.
- [ ] Remove their asset declarations and now-unused Flutter dependencies from `app/pubspec.yaml`.
- [ ] Run `flutter pub get`, `flutter analyze`, and `flutter test` in `app`.
- [ ] Verify that only this plan remains under `docs`.
- [ ] Commit the deletion.

```powershell
git add -A
git commit -m "chore: delete historical plans and unused assets"
```

### Task 3: Delete the Legacy Gateway, V1 Client Rail, and Second Auth Authority

**Files:**

- Delete: `src/DigitalBrain.Kernel/Protos/digitalbrain.proto`
- Delete: `src/DigitalBrain.Kernel/Gateway/IngressNeuron.cs`
- Delete: `src/DigitalBrain.Kernel/Ui/SignalEgressBus.cs`
- Delete: `src/DigitalBrain.Kernel/Ui/SignalEgressStreamSubscriber.cs`
- Delete: `src/DigitalBrain.Kernel/Ui/ChatNeuron.cs`
- Delete: `src/DigitalBrain.Kernel/Auth/UserSessionNeuron.cs`
- Delete: `src/DigitalBrain.Kernel/Auth/DevAuth.cs`
- Delete: `app/lib/grpc/digitalbrain.pb.dart`
- Delete: `app/lib/grpc/digitalbrain.pbgrpc.dart`
- Delete: `app/lib/shell/digitalbrain_client_scope.dart`
- Delete or rewrite: `app/lib/features/brain/voice_input.dart`
- Delete V1 imports and call sites from: `app/lib/rfw_host/digitalbrain_rfw_library.dart`
- Delete: `tests/DigitalBrain.Tests/Ui/ChatNeuronTests.cs`
- Delete: `tests/DigitalBrain.Tests/Auth/UserSessionNeuronTests.cs`
- Delete: `tests/DigitalBrain.Tests/Auth/UserSessionNeuronClientIdTests.cs`
- Modify: `src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`
- Modify: `src/DigitalBrain.Mcp/Program.cs`

**Interfaces:**

- Consumes: `src/DigitalBrain.Mcp/RuntimeSessionAuthority.cs`, `src/DigitalBrain.Mcp/UiGrpcService.cs`, `app/lib/runtime/grpc_ui_transport.dart`
- Produces: V2 UI gRPC as the only interactive client rail

- [ ] Add or strengthen `tests/DigitalBrain.Tests/Runtime/UiGrpcServiceTests.cs` assertions for bootstrap, refresh, feed watch, action submission, and logout through `RuntimeSessionAuthority`.
- [ ] Run the root tests and confirm the retained V2 tests pass before deletion.
- [ ] Delete the legacy server, client, auth, and tests listed above.
- [ ] Remove the `<Protobuf Include="Protos\digitalbrain.proto" GrpcServices="Both" />` item and packages used only by that service.
- [ ] Route retained voice input through the V2 action contract or delete voice input if V2 has no product requirement for it.
- [ ] Remove every `DigitalBrainGateway`, `DigitalBrainClientScope`, `UserSessionNeuron`, and `DevAuth` reference.
- [ ] Run `rg -n "DigitalBrainGateway|DigitalBrainClientScope|UserSessionNeuron|DevAuth|digitalbrain\.proto" .` and require no matches outside this plan.
- [ ] Run full .NET and Flutter verification.
- [ ] Commit the deletion.

```powershell
git add -A
git commit -m "refactor: remove legacy gateway and auth rail"
```

### Task 4: Delete Verified-Dead Core and Kernel Vocabulary

**Files:**

- Delete: `src/DigitalBrain.Core/GrpcAuthentication.cs`
- Delete: `src/DigitalBrain.Core/SensitiveText.cs`
- Delete: `src/DigitalBrain.Core/TabularDataSynapses.cs`
- Delete: `src/DigitalBrain.Core/Synapses/CapabilitySynapses.cs`
- Delete: `src/DigitalBrain.Kernel/Db/SqliteSchemaInspector.cs`
- Delete: `src/DigitalBrain.Kernel/Uploads/ChatUploadClassifier.cs`
- Delete: `src/DigitalBrain.Kernel/Sync/SyncManifest.cs`
- Delete: `src/DigitalBrain.Kernel/TabularData/TabularDataParser.cs`
- Delete owning tests under: `tests/DigitalBrain.Tests/Db/`, `tests/DigitalBrain.Tests/Uploads/`, and `tests/DigitalBrain.Tests/TabularData/`
- Modify: `src/DigitalBrain.Core/Synapse.cs`
- Modify: `src/DigitalBrain.Core/McpContracts.cs`
- Modify: `src/DigitalBrain.Core/RuntimeContracts.cs`
- Modify: `src/DigitalBrain.Core/Models/DigitalBrainModelCatalog.cs`
- Modify: relevant `.csproj` and `Directory.Packages.props` files

**Interfaces:**

- Consumes: zero-reference inventory in the accepted architecture assessment
- Produces: a smaller serialized contract surface containing only live types

- [ ] Remove the whole dead files first.
- [ ] Remove dead records and interfaces from mixed files: closed-loop and architect synapses, Salesforce OAuth callback synapses, unused query/command ports, dead deployment preview contracts, dead model router, unused feed cursor/page, unused sensitive wrappers, unused capability invocation, and unused model markers.
- [ ] Remove tests that only keep dead production types alive.
- [ ] Remove SQLite, ClosedXML, archive, and parser packages that have no retained consumer.
- [ ] Run `dotnet build` and use compiler errors as the authoritative remaining-reference list.
- [ ] Do not replace deleted contracts with compatibility wrappers.
- [ ] Run full root tests.
- [ ] Commit the deletion.

```powershell
git add -A
git commit -m "refactor: delete dead core and kernel vocabulary"
```

### Task 5: Remove Foundry, Pack Execution, and All Trusted-Process Code Execution

**Files:**

- Delete: `src/DigitalBrain.Kernel/Foundry/`
- Delete: `src/DigitalBrain.Kernel/Sandbox/`
- Delete: `src/DigitalBrain.Kernel/Generated/GeneratedPackRuntime.cs`
- Delete: `src/DigitalBrain.Pack.Contracts/`
- Delete: Foundry, sandbox, pack, and generated-runtime tests under `tests/DigitalBrain.Tests/`
- Delete: pack configuration stores under `src/DigitalBrain.Kernel/Config/`
- Modify: `Brain.slnx`
- Modify: `src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`
- Modify: `src/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj`
- Modify: `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`
- Modify: `Directory.Packages.props`
- Modify: AppHost and service registration files

**Interfaces:**

- Consumes: retained human-approved INO proposal and effect evidence only
- Produces: no compilation, assembly loading, script execution, process execution, or pack installation path

- [ ] Delete the Foundry, sandbox, pack contract project, pack configuration, generated runtime, and their tests.
- [ ] Remove all service registrations and project references.
- [ ] Remove Roslyn scripting/workspace packages when no retained production consumer remains.
- [ ] Remove `System.Reflection.MetadataLoadContext`, collectible `AssemblyLoadContext`, script runner, build runner, and process runner packages when unused.
- [ ] Run `rg -n "CSharpCompilation|CSharpScript|AssemblyLoadContext|ProcessStartInfo|CodeFoundry|GeneratedPack|NeuroPack|TrustedAutoApply" src integrations hosts` and require no matches.
- [ ] Keep approval records only if a retained automation operation consumes them; otherwise delete those records too.
- [ ] Run full root tests and commit.

```powershell
git add -A
git commit -m "refactor: remove foundry and pack execution"
```

### Task 6: Enforce Independent Project Boundaries

**Files:**

- Create: `tests/DigitalBrain.Tests/Architecture/ProjectDependencyTests.cs`
- Create: `hosts/DigitalBrain.RuntimeHost/DigitalBrain.RuntimeHost.csproj`
- Create: `hosts/DigitalBrain.RuntimeHost/Program.cs`
- Modify: `src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj`
- Modify: `src/DigitalBrain.Mcp/DigitalBrain.Mcp.csproj`
- Modify: `hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`
- Modify: `hosts/DigitalBrain.AppHost/AppHost.cs`
- Modify: `src/DigitalBrain.Aspire/DigitalBrainBuilderExtensions.cs`
- Modify: integration registration files under `integrations/DigitalBrain.Google/` and `integrations/DigitalBrain.Salesforce/`

**Interfaces:**

- Consumes: provider-neutral interfaces in `DigitalBrain.Kernel.Abstractions`
- Produces: a library-only Kernel, a thin RuntimeHost composition root, and architecture tests that reject reverse dependencies

- [ ] Write a failing architecture test that loads every tracked `.csproj` with `XDocument` and rejects these edges:

```text
DigitalBrain.Kernel -> DigitalBrain.Mcp
DigitalBrain.Kernel -> DigitalBrain.Google
DigitalBrain.Kernel -> DigitalBrain.Salesforce
DigitalBrain.Kernel -> DigitalBrain.Ui.Contracts
DigitalBrain.Kernel -> DigitalBrain.Ui.Runtime
DigitalBrain.Kernel -> DigitalBrain.ServiceDefaults
DigitalBrain.Google -> DigitalBrain.Kernel
DigitalBrain.Salesforce -> DigitalBrain.Kernel
```

- [ ] Run the root tests and verify the new test fails on current references.
- [ ] Move `Program.cs`, concrete provider registration, service defaults, provider OAuth endpoints, and executable-only configuration from `DigitalBrain.Kernel` into the new `DigitalBrain.RuntimeHost`.
- [ ] Make `DigitalBrain.Kernel` a provider-neutral class library containing retained runtime grains and services.
- [ ] Point the AppHost kernel resource at `DigitalBrain.RuntimeHost.csproj`.
- [ ] Keep only provider-neutral contracts in `DigitalBrain.Kernel.Abstractions`.
- [ ] Remove the forbidden references from `DigitalBrain.Kernel.csproj`.
- [ ] Reduce Aspire wiring to a compact server/client extension with configuration propagation owned by `DigitalBrain.Aspire`.
- [ ] Run the root tests and verify the architecture test passes.
- [ ] Commit the boundary change.

```powershell
git add -A
git commit -m "refactor: enforce independent project boundaries"
```

### Task 7: Migrate Salesforce to Typed INO Operations

**Files:**

- Modify: `src/DigitalBrain.Kernel/Runtime/InoOperationWorkerGrain.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/PlanInoToolGateway.cs`
- Modify: `integrations/DigitalBrain.Salesforce/SalesforceMutationNeuron.cs`
- Modify or replace: `integrations/DigitalBrain.Salesforce/SalesforceReadNeuron.cs`
- Modify: `integrations/DigitalBrain.Salesforce/SalesforceApiClient.cs`
- Modify: `tests/DigitalBrain.Salesforce.Tests/`
- Modify: `tests/DigitalBrain.Tests/Runtime/`

**Interfaces:**

- Consumes: immutable actor scope, operation id, tool id, safe preview, payload hash, idempotency key, and execution proof
- Produces: typed preview, approve, apply, and verify calls with no Synapse dispatch

- [ ] Write failing tests for Salesforce read, update preview, approval binding, duplicate apply, provider timeout, outcome unknown, and post-apply verification.
- [ ] Define the smallest typed interfaces in `DigitalBrain.Kernel.Abstractions`; keep Salesforce DTOs in the Salesforce project.
- [ ] Replace `FireAsync`, `IHandle<T>`, and provider-name branching with direct typed calls selected by capability at AppHost composition.
- [ ] Preserve `InoEffectPlanAuthority`, encrypted state, single-use approval, leases, fences, idempotency, and outcome-unknown semantics unchanged.
- [ ] Delete superseded Salesforce Synapse records and mapping functions immediately after the typed tests pass.
- [ ] Split `SalesforceApiClient.cs` only where retained responsibilities remain above 500 lines.
- [ ] Run Salesforce tests, root tests, and one minimal Aspire approved-mutation scenario.
- [ ] Commit the vertical slice.

```powershell
git add -A
git commit -m "refactor: route salesforce through typed ino operations"
```

### Task 8: Migrate Gmail to Typed INO Operations

**Files:**

- Modify: `integrations/DigitalBrain.Google/GmailNeuron.cs`
- Modify: `integrations/DigitalBrain.Google/IGmailApiClient.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/InoOperationWorkerGrain.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/PlanInoToolGateway.cs`
- Modify: Gmail and runtime tests under `tests/DigitalBrain.Tests/`

**Interfaces:**

- Consumes: the provider-neutral typed operation boundary proven by Salesforce
- Produces: typed Gmail reads and approved sends with no generic dispatch or duplicated runtime DTOs

- [ ] Write failing tests for bounded reads, metadata minimization, send preview, approval binding, duplicate send, timeout, outcome unknown, and verification.
- [ ] Eliminate the duplicate runtime-to-Gmail DTO mapping layer.
- [ ] Keep Gmail DTOs in the Google integration and provider-neutral effect evidence in Kernel abstractions.
- [ ] Replace generic Synapse calls with the typed operation boundary.
- [ ] Delete superseded Gmail Synapse records and aliases immediately after tests pass.
- [ ] Run root tests and one minimal Aspire approved-send scenario.
- [ ] Commit the vertical slice.

```powershell
git add -A
git commit -m "refactor: route gmail through typed ino operations"
```

### Task 9: Delete the Generic Neuron and Synapse Runtime

**Files:**

- Delete: `src/DigitalBrain.Core/INeuron.cs`
- Delete or reduce to retained immutable event records: `src/DigitalBrain.Core/Synapse.cs`
- Delete: `src/DigitalBrain.Kernel.Abstractions/Neuron.cs`
- Delete: `src/DigitalBrain.Kernel.Abstractions/SynapseDispatch.cs`
- Delete generic journal, checkpoint, branch, restore, reflection dispatch, and broadcast tests
- Refactor or delete: `src/DigitalBrain.Kernel/Grains/AutomationNeuron.cs`
- Refactor or delete: `src/DigitalBrain.Kernel/Grains/ScheduleTriggerNeuron.cs`
- Refactor or delete: `src/DigitalBrain.Kernel/Grains/PollTriggerNeuron.cs`
- Refactor or delete: `src/DigitalBrain.Kernel/Grains/LlmResponderNeuron.cs`
- Refactor or delete: `src/DigitalBrain.Kernel/Grains/LlmNeuron.cs`
- Delete: `src/DigitalBrain.Kernel/Grains/GeneratedNeuron.cs`
- Refactor retained runtime grains under: `src/DigitalBrain.Kernel/Runtime/`

**Interfaces:**

- Consumes: typed Orleans grain interfaces and INO operation contracts
- Produces: durable grains with product-specific state and no universal agent base class

- [ ] Use CodeGraph to enumerate every remaining `Neuron` inheritance, `INeuron`, `IHandle<T>`, `FireAsync`, `SynapseDispatch`, checkpoint, branch, and restore caller.
- [ ] Delete unshipped grains with no retained product behavior.
- [ ] Convert retained automation and scheduling behavior into small product-specific grains with typed methods and explicit persistent state.
- [ ] Convert retained model calls into bounded services invoked by INO operations; do not create a base agent class.
- [ ] Delete reflection-based dispatch and universal broadcast behavior.
- [ ] Delete generic dual journals, branch, restore, and checkpoint code. Retain only explicit operation audit state required by the effect rail.
- [ ] Run `rg -n "INeuron|IHandle<|FireAsync|SynapseDispatch|: Neuron" src integrations hosts` and require no matches.
- [ ] Run full root tests and commit.

```powershell
git add -A
git commit -m "refactor: remove generic neuron runtime"
```

### Task 10: Collapse the UI Runtime to One Renderer and One Feed

**Files:**

- Delete: `app/lib/ui_kit/ui_registry.dart`
- Delete: `app/test/ui_kit/ui_registry_test.dart`
- Delete: `app/lib/rfw_host/palette/palette_primitives.dart`
- Remove dead shader, globe, Lottie, and RFW demo paths
- Modify: `src/DigitalBrain.Ui.Runtime/`
- Modify: `src/DigitalBrain.Ui.Contracts/UiSurfaces.cs`
- Modify: `src/DigitalBrain.Mcp/RuntimeSurfaceFeed.cs`
- Modify: `app/lib/runtime/`
- Modify: `app/lib/rfw_host/`
- Modify: `app/pubspec.yaml`

**Interfaces:**

- Consumes: V2 session/action transport and one bounded surface contract
- Produces: one server renderer, one client renderer, and one progress/feed stream

- [ ] Protect the retained runtime shell, action dispatch, feed resume, and acknowledgement behavior with tests.
- [ ] Delete the duplicate widget registry and unused visual primitives.
- [ ] Remove dependencies that only support deleted visuals.
- [ ] Keep one server-side surface builder and one Flutter renderer.
- [ ] Split retained files above 500 lines by product responsibility, not by generic technical layer.
- [ ] Run Flutter analyze/tests, root tests, and a V2 surface-feed Aspire scenario.
- [ ] Commit the UI convergence.

```powershell
git add -A
git commit -m "refactor: collapse ui to one runtime rail"
```

### Task 11: Remove Every Comment and Enforce Zero Comments

**Files:**

- Modify: every retained tracked source and configuration file containing a comment
- Create: `tests/DigitalBrain.Tests/Architecture/NoCommentsTests.cs`
- Modify: `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj` only if an explicit Roslyn test dependency is required

**Interfaces:**

- Consumes: the reduced post-deletion repository
- Produces: a language-aware repository test that reports file, line, and comment kind for every violation

- [ ] Remove all comments from retained handwritten and generated tracked files.
- [ ] For each comment that describes an invariant, add or strengthen a test before deleting the comment.
- [ ] For each comment needed to explain control flow, rename or extract code until the flow is self-explanatory, then delete the comment.
- [ ] For each commented-out block, delete it without replacement.
- [ ] For generated sources, move generation out of tracked paths or add a deterministic sanitization step to the existing generation command. Do not hand-maintain generated comments.
- [ ] Implement `NoCommentsTests.cs` using Roslyn syntax trivia for C# and a string-aware lexical scan for Dart, Proto, PowerShell, shell, XML, MSBuild, YAML, and JSON-with-comments.
- [ ] The test must scan `git ls-files`, ignore Markdown and binary files, ignore build output, and have no allowlist.
- [ ] The test must reject `//`, `///`, `/* */`, `<!-- -->`, and language-specific line comments when they are lexical comments rather than string content.
- [ ] Run the test through the required unfiltered root command and require zero violations.
- [ ] Run the marker inventory command and manually inspect any remaining matches inside strings.
- [ ] Commit the removal and enforcement together.

```powershell
git add -A
git commit -m "style: remove and forbid source comments"
```

### Task 12: Split Only the Oversized Retained Components

**Files:**

- Modify: `src/DigitalBrain.Kernel/Runtime/InoOperationWorkerGrain.cs`
- Modify: `src/DigitalBrain.Kernel/Runtime/ConversationNeuron.cs` if retained under a product-specific name
- Modify: `src/DigitalBrain.Mcp/RuntimeSurfaceFeed.cs`
- Modify: `src/DigitalBrain.Ui.Runtime/`
- Modify: retained integration clients above 500 lines

**Interfaces:**

- Consumes: stable typed boundaries from Tasks 6 through 10
- Produces: files below 500 lines with one responsibility and no new framework layer

- [ ] Measure retained production files and list those above 500 lines.
- [ ] Split only files still above the limit.
- [ ] Extract pure validation, state transitions, provider transport, and mapping into sealed focused types next to their owning feature.
- [ ] Keep Orleans grains as thin durable coordinators.
- [ ] Do not introduce manager, helper, utility, base, common, or shared types without a precise product responsibility.
- [ ] Run owning tests after each split and full root tests before commit.
- [ ] Commit the simplification.

```powershell
git add -A
git commit -m "refactor: split retained runtime responsibilities"
```

### Task 13: Final Verification and Self-Delete the Plan

**Files:**

- Delete: `docs/architecture-convergence-zero.md`
- Final review: `README.md`
- Final review: `CLAUDE.md`

**Interfaces:**

- Consumes: completed convergence work
- Produces: only two living Markdown documents and no temporary plan

- [ ] Recompute every exit metric and compare it with the baseline.
- [ ] Run `git ls-files docs` and require only this plan before its deletion.
- [ ] Run the no-comments test and require zero violations.
- [ ] Run full .NET build/tests, Flutter analyze/tests, and `aspire doctor`.
- [ ] Start the minimal AppHost through Aspire, wait for required resources, and verify V2 bootstrap/feed/action plus one approved Salesforce mutation.
- [ ] Verify the worktree contains no build output, generated debris, logs, snapshots, or temporary reports.
- [ ] Delete this plan.
- [ ] Run `git ls-files '*.md'` and require only `README.md`, `CLAUDE.md`, and instruction files required by the development environment.
- [ ] Commit final cleanup.

```powershell
git add -A
git commit -m "chore: finish architecture convergence zero"
```

## Stop Conditions

Stop and request a user decision only when one of these is proven:

- Production durable state must survive deletion of a serialized alias.
- A retained user-visible feature has no V2 or typed INO replacement and deleting it would remove an explicitly required product capability.
- The INO effect rail fails an existing security invariant and preserving it would authorize an unsafe mutation.
- A required provider cannot express idempotency or verification, forcing a product-level choice between read-only behavior and outcome-unknown semantics.

Everything else is an implementation problem, not a reason to preserve duplicate architecture.
