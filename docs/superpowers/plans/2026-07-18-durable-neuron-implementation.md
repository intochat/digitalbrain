# Durable Neuron Kernel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the string-routed `Kind` architecture with owner-bound, type-selected Orleans grains built on official Orleans Journaling, then expose the cluster through an IAW-style Aspire `DigitalBrainResource`.

**Architecture:** `INeuron` is a minimal `IGrainWithStringKey` marker. Public leaf interfaces identify capabilities, concrete neurons derive from `Neuron : DurableGrain`, and the authenticated owner ID is the complete grain key. A startup task builds a `Type`-keyed Quadrant catalog and validates the Orleans manifest. Durable neuron state owns DigitalBrain memory, policy, external-operation outcomes, and notification outbox state; external providers remain authoritative for provider records. Streams announce committed changes and are never authoritative.

**Tech Stack:** .NET 11 preview as already targeted by the repository, Orleans Core family `10.2.2-rc.2`, `Microsoft.Orleans.Journaling` and `Microsoft.Orleans.Journaling.AzureStorage` `10.2.2-rc.2.alpha.1`, Aspire `13.4.6`, Microsoft.Extensions.AI `10.8.0`, Azure Storage/Azurite, Orleans streams and reminders, xUnit, and Grok CLI `0.2.101` with `grok-4.5`.

## Global Constraints

- This plan supersedes `docs/superpowers/plans/2026-07-18-digitalbrain-grok-orchestration.md` and every earlier implementation plan where they conflict.
- Work in `E:\brain` on local branch `master`, as requested. Do not create a feature branch or implementation worktree.
- Codex is the integration authority. Grok CLI sessions perform all substantive implementation edits.
- Run one editing Grok session at a time because every session edits the shared `master` worktree.
- Codex, not Grok, reviews, verifies, stages, and commits each task.
- A Grok completion message is never evidence. Only the inspected diff and Codex-run commands are evidence.
- The user's documentation override applies: use Microsoft Learn, official Aspire docs, official NuGet metadata, and `dotnet-inspect`; do not use Context7 for this implementation.
- Use CodeGraph before every source edit or architectural review.
- Use `aspire docs search` and `aspire docs api search --language csharp` before editing unfamiliar AppHost APIs.
- No tracked source or configuration comments.
- No `INeuronKind`, `KindCatalog`, provider-prefixed address, `NeuronAddress` parser, `DispatchProxy`, string contract dispatch, keyed provider DI, generic JSON invocation, compatibility alias, or dual-routing period remains at completion.
- Domain identity and dispatch use `typeof`, generic type parameters, enums, and `nameof`. External protocol names, Aspire resource names, URLs, OAuth scopes, and provider model IDs are allowed only at their boundary.
- Do not introduce `Ask`, `InvokeMcpTool`, copied Gmail/Salesforce operations, generated MCP methods, or any other public MCP callable surface.
- Do not implement a custom journal provider or retain the tracked custom Azure Blob journal provider.
- Never fall back from durable journal storage to volatile storage outside explicitly named unit tests.
- Every behavior change follows red, observed red, minimum green, refactor.
- Never use `dotnet test --filter`.
- The focused verification command is `dotnet test <owning-project> --logger "console;verbosity=minimal"`.
- The root checkpoint command is `dotnet test Brain.slnx --logger "console;verbosity=minimal"`.
- Preserve `sources/**`; it is reference material and is not part of the active product tree.
- Leave `workspace/**` visually unchanged. The rejected generic Flutter backend is removed; reconnecting the workspace requires a separate typed UI design.

## Deliberate Version Set

| Family | Version | Decision |
| --- | --- | --- |
| .NET target | `net11.0` | Keep the repository's existing target and installed SDK `11.0.100-preview.6.26359.118`; changing the target is unrelated architecture churn. |
| Orleans Core packages | `10.2.2-rc.2` | Required by the selected Journaling build. All Orleans Core, client, server, persistence, reminders, streaming, and testing packages use this exact version. |
| Orleans Journaling | `10.2.2-rc.2.alpha.1` | Latest published official Journaling package on 2026-07-18. |
| Orleans Journaling Azure Storage | `10.2.2-rc.2.alpha.1` | Same repository commit and build as Journaling; mandatory official durability provider. |
| Aspire SDK and packages | `13.4.6` | Latest stable release. Remove the repository's unnecessary `13.5.0-preview.1.26363.3` mixture. |
| Microsoft.Extensions.AI | `10.8.0` | Latest stable release. |
| Model Context Protocol | none in active solution | MCP callable design is paused; remove the active generic MCP edge and its package references. |

Do not float versions. Before implementation, Codex re-runs the NuGet index check. If a newer stable version exists, Codex updates this table and revalidates compatibility before launching Grok.

## Codex → Grok Execution Protocol

### Fresh implementation session

For each task, Codex prepares a task-specific prompt containing the task section, exact assigned paths, current CodeGraph blast radius, observed red output, and this contract:

```text
You are the bounded implementation worker for one task in the approved Durable Neuron plan.

Read AGENTS.md and CLAUDE.md completely. The user explicitly overrides their Context7 rule for this work: use Microsoft Learn, official Aspire docs, official NuGet metadata, and dotnet-inspect instead. Use CodeGraph before reading or editing indexed source.

Work directly in E:\brain on the current master worktree. Touch only the assigned paths. Preserve unrelated changes and never touch sources/**.

Apply the repository's five steps in order. Delete rejected code before adding replacement complexity. Do test-driven development: add the specified failing test, run it and report the observed failure, implement the minimum supported solution, rerun the owning test project, and refactor only while green.

Do not add comments. Do not add compatibility adapters, generic JSON invocation, string routing, DispatchProxy, keyed provider DI, a custom journal provider, volatile production fallback, public MCP methods, Ask, InvokeMcpTool, or copied provider operations.

Do not commit, stage, push, merge, rebase, or edit this plan. Codex owns review, verification, and commits.

Before returning, run the owning test project and git diff --check. Return changed paths, exact red and green commands, test totals, unresolved risks, and anything you could not prove.
```

Codex launches the current installed default model:

```powershell
$beforeHead = git rev-parse HEAD
$beforeStatus = git status --porcelain=v1
if ($beforeStatus) { throw "Grok sessions require a clean worktree." }

$schema = '{"type":"object","additionalProperties":false,"properties":{"summary":{"type":"string"},"changedPaths":{"type":"array","items":{"type":"string"}},"redCommand":{"type":"string"},"redResult":{"type":"string"},"greenCommands":{"type":"array","items":{"type":"string"}},"greenResults":{"type":"array","items":{"type":"string"}},"risks":{"type":"array","items":{"type":"string"}}},"required":["summary","changedPaths","redCommand","redResult","greenCommands","greenResults","risks"]}'

$grokJson = grok `
  --cwd E:\brain `
  --model grok-4.5 `
  --reasoning-effort high `
  --permission-mode bypassPermissions `
  --no-subagents `
  --no-memory `
  --check `
  --max-turns 60 `
  --output-format json `
  --json-schema $schema `
  --single $prompt

$grokResult = $grokJson | ConvertFrom-Json
$sessionId = $grokResult.sessionId
if (-not $sessionId) { throw "Grok did not return a session ID." }
```

`bypassPermissions` is intentional and user-authorized. The safety boundary is task scope plus Codex's diff review, not Grok's interactive approval UI.

### Codex review gate

After every Grok session, Codex must:

- [ ] Compare `git status --short` with the pre-session snapshot.
- [ ] Confirm `git rev-parse HEAD` still equals `$beforeHead`; Grok is forbidden to commit.
- [ ] Reject any path outside the task's assigned paths.
- [ ] Inspect `git diff --stat`, `git diff --check`, and the complete diff.
- [ ] Query CodeGraph for the changed symbols and blast radius.
- [ ] Search the changed active product roots for forbidden architecture tokens.
- [ ] Run the owning test project independently.
- [ ] Run the root checkpoint when the task says so.
- [ ] After any task which touches an AppHost or Aspire hosting library, run `aspire doctor --non-interactive` and `aspire ps --non-interactive`.
- [ ] Commit only after all checks pass.

If review finds an out-of-scope edit, Codex does not use a broad reset or clean. The resumed Grok session must restore only the out-of-scope paths to `$beforeHead`, report the paths it restored, and then correct the assigned work. If any user-owned pre-session change appears, Codex stops and asks the user.

If review otherwise fails, Codex resumes the same Grok session with the exact defect list:

```powershell
grok `
  --cwd E:\brain `
  --resume $sessionId `
  --permission-mode bypassPermissions `
  --no-subagents `
  --check `
  --max-turns 20 `
  --output-format json `
  --json-schema $schema `
  --single $correctionPrompt
```

Codex does not silently repair substantive implementation defects. Grok receives a bounded correction order and Codex repeats the full gate.

---

## Task 0: Establish the Recorded Baseline

**Owner:** Codex, read-only.

**Files:** None.

- [x] Run `git status --short --branch`; live baseline is clean `master`, two commits ahead of `origin/master` after the required design and implementation-plan commits.
- [x] Run `grok --version`; recorded `0.2.101`.
- [x] Run `grok models`; recorded `grok-4.5` available and default.
- [x] Run `dotnet --version`; recorded `11.0.100-preview.6.26359.118`.
- [x] Run `aspire --version`; recorded `13.4.6`.
- [x] Run `dotnet test Brain.slnx --no-restore --logger "console;verbosity=minimal"`; recorded 143 passed, 0 failed, and 0 skipped.
- [x] Run `aspire doctor --non-interactive`; recorded 5 passed, 0 warnings, and 0 failed.
- [x] Run `aspire ps --non-interactive`; recorded no running AppHost.
- [x] Re-query the official NuGet flat-container indexes for every package in the deliberate version table; all deliberate versions are present and no newer stable version exists.

No commit is created.

---

## Task 1: Pass the Official Journaling Compatibility and Restart Gate

**Assigned paths:**

- Modify: `Directory.Packages.props`
- Modify: `Brain.slnx`
- Modify: `tests/Brain.FeasibilityTests/Brain.FeasibilityTests.csproj`
- Modify: `tests/DigitalBrain.Tests/Architecture/ProjectTopologyTests.cs`
- Modify: `hosts/Brain.Kernel.Host/Brain.Kernel.Host.csproj`
- Modify: `hosts/Brain.Kernel.Host/Program.cs`
- Modify: `hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`
- Modify: `hosts/DigitalBrain.AppHost/AppHost.cs`
- Delete: `tests/Brain.FeasibilityTests/AzureJournal/AzureBlobJournalStorageTests.cs`
- Create: `tests/Brain.FeasibilityTests/Journaling/JournalRecoveryContracts.cs`
- Create: `tests/Brain.FeasibilityTests/Journaling/JournalRecoveryGrain.cs`
- Create: `tests/Brain.FeasibilityTests/Journaling/OfficialJournalRecoveryTests.cs`
- Delete: `src/Brain.Kernel.Host/JournalStorage/AzureBlobJournalStorage.cs`
- Delete: `src/Brain.Kernel.Host/JournalStorage/AzureBlobJournalStorageOptions.cs`
- Delete: `src/Brain.Kernel.Host/JournalStorage/AzureBlobJournalStorageProvider.cs`

**Required API:** `ISiloBuilder.AddJournalStorage()` plus `ISiloBuilder.AddAzureBlobJournalStorage(options => options.ConfigureBlobServiceClient(connectionString))`.

- [x] Add a recovery test whose test grain derives directly from `DurableGrain` and receives one `IDurableValue<int>`, one `IDurableDictionary<Guid, string>`, one `IDurableQueue<Guid>`, and one `IDurableList<string>`.
- [x] Add a complete-restart test that writes all four structures, stops and disposes the first `TestCluster`, starts a new cluster against the same Azurite container, verifies exact recovery, writes again, restarts again, and verifies the continued sequence.
- [x] Add a failed-intent test in which the test grain awaits `WriteStateAsync()` before incrementing a test-only external-effect probe.
- [x] Stop Azurite before that write and assert the grain call fails and the external-effect probe remains zero.
- [x] Run `dotnet test tests/Brain.FeasibilityTests/Brain.FeasibilityTests.csproj --logger "console;verbosity=minimal"` and observe red because the official Azure Journaling package/configuration is not yet present.
- [x] Pin every Orleans Core-family entry in `Directory.Packages.props` to `10.2.2-rc.2`.
- [x] Pin both Journaling packages to `10.2.2-rc.2.alpha.1`.
- [x] Pin the AppHost SDK and every active Aspire package to stable `13.4.6`.
- [x] Add `Microsoft.Orleans.Journaling.AzureStorage` to the feasibility project.
- [x] Remove the compile link and every reference to the tracked custom provider.
- [x] Add `tests/Brain.FeasibilityTests/Brain.FeasibilityTests.csproj` to `Brain.slnx`.
- [x] Update the active solution topology test to include the feasibility project.
- [x] Replace the active host's `VolatileJournalStorageProvider` with `AddJournalStorage()` and official `AddAzureBlobJournalStorage(options => options.ConfigureBlobServiceClient(connectionString))`.
- [x] In the minimal AppHost, add Azure Storage with Azurite, add a journal blob resource, reference it from the kernel project, wait for storage, and name the kernel resource `kernel`.
- [x] Keep the existing non-durable localhost clustering only as an interim cluster-membership choice; no neuron state may use volatile journal storage.
- [x] Configure the test silo only with the official Journaling package and official Azure Blob provider.
- [x] Let one `AzuriteContainer` remain alive for the complete test; obtain its connection string with `GetConnectionString()`, create a unique journal container, build two separately disposed `TestCluster` instances against that same connection/container, then dispose Azurite in the fixture teardown.
- [x] Run the feasibility project; require all Journaling tests pass with no skips.
- [x] Run the root checkpoint; require 0 failures and 0 skips.
- [x] Run `aspire start --non-interactive`, `aspire wait kernel --non-interactive`, inspect `aspire describe kernel --non-interactive` and kernel logs, run `aspire resource kernel stop --non-interactive`, then stop the AppHost with `aspire stop --non-interactive`.
- [x] Run `rg -n "AzureBlobJournalStorageProvider|VolatileJournalStorageProvider" kernel hosts modules edge -g '*.cs' -g '*.csproj'`; require no active production match.
- [x] Require `Test-Path src/Brain.Kernel.Host/JournalStorage/AzureBlobJournalStorageProvider.cs` to be false. The custom provider existed only in the excluded `src/**` tree; the active `hosts/**` tree used the volatile provider. Both are removed by this task.
- [x] Codex commits as `test: prove official journal recovery`.

**Kill condition:** If the official provider cannot recover all four official durable structures across a complete silo restart, or a failed journal write does not prevent the probe effect, stop implementation. Do not write a replacement provider and do not fall back to volatile storage.

---

## Task 2: Replace the Generic Kernel with Minimal Typed Neurons

**Assigned paths:**

- Modify: `kernel/Brain.Contracts/INeuron.cs`
- Modify: `kernel/Brain.Contracts/BrainErrors.cs`
- Create: `kernel/Brain.Contracts/BrainOwnerId.cs`
- Create: `kernel/Brain.Contracts/ExternalOperation.cs`
- Create: `kernel/Brain.Contracts/NeuronNotification.cs`
- Delete: `kernel/Brain.Contracts/INeuronKind.cs`
- Delete: `kernel/Brain.Contracts/NeuronAddress.cs`
- Delete: `kernel/Brain.Contracts/NeuronContracts.cs`
- Delete: `kernel/Brain.Contracts/NeuronEnvelope.cs`
- Delete: `kernel/Brain.Contracts/Synapses.cs`
- Create: `kernel/Brain.Kernel/Neuron.cs`
- Replace: `kernel/Brain.Kernel/NeuronDurableState.cs`
- Modify: `kernel/Brain.Kernel/NeuronStateAttribute.cs`
- Modify: `kernel/Brain.Kernel/NeuronStateMapper.cs`
- Replace: `kernel/Brain.Kernel/KernelHosting.cs`
- Delete: `kernel/Brain.Kernel/NeuronGrain.cs`
- Delete: `kernel/Brain.Kernel/CatalogKind.cs`
- Delete: `kernel/Brain.Kernel/KindCatalog.cs`
- Delete: `kernel/Brain.Kernel/EffectKind.cs`
- Delete: `kernel/Brain.Kernel/Connections/**`
- Modify: `kernel/Brain.Kernel/NeuronJournalJsonContext.cs`
- Modify: `kernel/Brain.Kernel/Brain.Kernel.csproj`
- Modify: `hosts/Brain.Kernel.Host/Brain.Kernel.Host.csproj`
- Replace: `hosts/Brain.Kernel.Host/Program.cs`
- Modify: `hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`
- Replace: `hosts/DigitalBrain.AppHost/AppHost.cs`
- Modify: `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`
- Modify: `Brain.slnx`
- Delete: `edge/Brain.Mcp` from `Brain.slnx` and both AppHost project/reference declarations; keep its tracked files until Task 9
- Delete: `modules/AI.Contracts/**`
- Delete: `modules/AI/**`
- Delete: `modules/Flutter.Contracts/**`
- Delete: `modules/Flutter/**`
- Delete: `modules/Google.Contracts/**`
- Delete: `modules/Google/**`
- Delete: all existing files under `tests/DigitalBrain.Tests/**` except `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`
- Create: `tests/DigitalBrain.Tests/Kernel/TestNeuron.cs`
- Create: `tests/DigitalBrain.Tests/Kernel/NeuronArchitectureTests.cs`
- Create: `tests/DigitalBrain.Tests/Kernel/NeuronDurableStateTests.cs`
- Create: `tests/DigitalBrain.Tests/Architecture/ProjectTopologyTests.cs`

**Required public shapes:**

```csharp
public interface INeuron : IGrainWithStringKey;

public readonly record struct BrainOwnerId(string Value);

public abstract class Neuron : DurableGrain
{
}

public enum ExternalOperationStatus
{
    Pending,
    Succeeded,
    Failed,
    Unknown
}

public enum NeuronFailureKind
{
    AuthenticationRequired,
    AuthorizationDenied,
    ProviderUnavailable,
    OperationFailed,
    OperationUnknown,
    StorageUnavailable
}
```

`NeuronDurableState` contains only:

- `IDurableValue<NeuronStatus> Status`
- `IDurableDictionary<Guid, ExternalOperation> Operations`
- `IDurableDictionary<Guid, NeuronNotification> Outbox`

Each `[FromKeyedServices]` state name is `nameof` a corresponding property. No handwritten state key is accepted.

- [x] Add architecture tests proving `INeuron` declares zero methods and extends `IGrainWithStringKey`.
- [x] Add tests proving `Neuron` derives from the official `DurableGrain`.
- [x] Add tests proving `NeuronDurableState` uses only the three approved universal durable members and every keyed state name is produced by `nameof`.
- [x] Add a test-only `ITestNeuron : INeuron` with direct typed methods for writing and reading durable state.
- [x] Run the owning test project and observe compile-time red because the minimal contracts/base do not exist.
- [x] Delete the generic contracts and generic kernel before implementing their replacements.
- [x] Remove the generic AI, Flutter, Google, and MCP projects from `Brain.slnx`, the kernel host, the AppHost, and the test project before changing `INeuron`; their rejected tracked remnants are deleted here or in Task 9.
- [x] Reduce the active test project to the new kernel/client architecture tests. Do not preserve tests which specify the rejected architecture.
- [x] Recreate the solution topology test immediately, listing the feasibility project plus the remaining active kernel, host, AppHost, and test projects.
- [x] Implement the minimal records with Orleans `[GenerateSerializer]`, `[Id]`, and `[Alias(nameof(...))]` metadata.
- [x] Keep raw credentials and arbitrary provider payloads out of `ExternalOperation`.
- [x] Implement `Neuron` with protected access to the injected `NeuronDurableState` and no public business methods.
- [x] Implement the test neuron with typed methods and explicit `WriteStateAsync()` calls.
- [x] Preserve the Task 1 interim hosting blueprint exactly: AppHost `kernel` -> Azurite journal blob reference; kernel host `UseLocalhostClustering` plus official `AddJournalStorage` and `AddAzureBlobJournalStorage`; no volatile provider. Remove only the deleted module extension calls and project references.
- [x] Run the owning test project; require green.
- [x] Run the root checkpoint; require green.
- [x] Run `rg -n "INeuronKind|KindCatalog|NeuronAddress|NeuronInvocation|NeuronReceipt|NeuronEvent|SynapseRecord" kernel tests/DigitalBrain.Tests -g '*.cs'`; require no match.
- [x] Codex commits as `refactor: replace generic kernel with durable neurons`.

Execution record (2026-07-18): the exact scan had zero matches in the active Task 2 projects. Two matches remained only in the inactive `kernel/Brain.Client/NeuronProxy.cs`, whose deletion is assigned to Task 3. The user approved continuing with that deletion deferred to Task 3 and restoring the rewritten client to the active topology there.

---

## Task 3: Bind the Authenticated Owner in `DigitalBrainClient`

**Assigned paths:**

- Delete: `kernel/Brain.Client/NeuronProxy.cs`
- Delete: `kernel/Brain.Client/BrainCluster.cs`
- Create: `kernel/Brain.Client/DigitalBrainClient.cs`
- Create: `kernel/Brain.Client/DigitalBrainClientExtensions.cs`
- Create: `kernel/Brain.Client/BrainOwnerContext.cs`
- Create: `kernel/Brain.Client/BrainOwnerOutgoingCallFilter.cs`
- Create: `kernel/Brain.Kernel/BrainOwnerIncomingCallFilter.cs`
- Modify: `kernel/Brain.Kernel/KernelHosting.cs`
- Modify: `Brain.slnx`
- Modify: `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`
- Modify: `tests/DigitalBrain.Tests/Architecture/ProjectTopologyTests.cs`
- Modify: `tests/DigitalBrain.Tests/Kernel/NeuronDurableStateTests.cs`
- Delete: `tests/DigitalBrain.Tests/BrainClusterCallerKeyTests.cs`
- Delete: `tests/DigitalBrain.Tests/Kernel/TypedProxyTests.cs`
- Create: `tests/DigitalBrain.Tests/Client/DigitalBrainClientTests.cs`
- Create: `tests/DigitalBrain.Tests/Security/BrainOwnerCallFilterTests.cs`

**Required public shape:**

```csharp
public sealed class DigitalBrainClient
{
    public TNeuron Get<TNeuron>() where TNeuron : INeuron;
}
```

`Get<TNeuron>()` is exactly an owner-bound convenience over `IClusterClient.GetGrain<TNeuron>(owner.Value)`. It returns the real Orleans reference and never accepts a grain key.

- [x] Add a test that constructs a client for `new BrainOwnerId("owner-a")`, calls `Get<ITestNeuron>()`, and asserts the reference key is exactly `owner-a`.
- [x] Add a source guard proving the client contains no `DispatchProxy`, reflection invocation, JSON serializer, address parser, or string provider prefix.
- [x] Add an outgoing-call-filter test proving the authenticated owner travels in Orleans `RequestContext` under a key derived with `nameof(BrainOwnerId)`.
- [x] Add an incoming-call-filter test proving owner A can call owner A's neuron and is rejected when directly obtaining owner B's grain through raw `IClusterClient`.
- [x] Run the owning test project and observe red because the owner-bound client and filters do not exist.
- [x] Delete `NeuronProxy` and the old caller-key/address client.
- [x] Implement a singleton `BrainOwnerContext` backed by `AsyncLocal<BrainOwnerId?>`.
- [x] Register a scoped `DigitalBrainClient` from the authenticated owner accessor and a client outgoing call filter which reads the current typed owner context.
- [x] Register a server incoming call filter which applies only to `Neuron` instances and compares the typed request owner with `GetPrimaryKeyString()`.
- [x] Represent denial with a typed exception/failure enum, not an error-code string switch.
- [x] Map unauthenticated and cross-owner calls to `NeuronFailureKind.AuthenticationRequired` and `NeuronFailureKind.AuthorizationDenied`.
- [x] Run the owning test project; require green.
- [x] Run the root checkpoint; require green.
- [x] Codex commits as `feat: add owner-bound digital brain client`.

Execution record (2026-07-18): Task 3's incoming owner filter intentionally makes the Task 2 durable-state integration test require an authenticated request context. The user directed Codex to keep operating Grok CLI, so that directly affected test is included in Task 3's correction scope; it must set and remove the typed owner in a `finally` block.

---

## Task 4: Populate Quadrant from the Installed Type System

**Assigned paths:**

- Create: `kernel/Brain.Kernel/NeuronRegistration.cs`
- Create: `kernel/Brain.Kernel/Quadrant.cs`
- Create: `kernel/Brain.Kernel/NeuronTypeCatalogBuilder.cs`
- Create: `kernel/Brain.Kernel/QuadrantStartupTask.cs`
- Create: `kernel/Brain.Kernel/OrleansNeuronManifestValidator.cs`
- Modify: `kernel/Brain.Kernel/KernelHosting.cs`
- Create: `tests/DigitalBrain.Tests/Quadrant/QuadrantDiscoveryTests.cs`
- Create: `tests/DigitalBrain.Tests/Quadrant/QuadrantStartupTests.cs`

**Required public shapes:**

```csharp
public sealed record NeuronRegistration(Type Contract, Type Implementation);

public sealed class Quadrant
{
    public IReadOnlyDictionary<Type, Type> Neurons { get; }
    public Type GetImplementation<TNeuron>() where TNeuron : INeuron;
}

public sealed class QuadrantStartupTask : IStartupTask
{
    public Task Execute(CancellationToken cancellationToken);
}
```

- [x] Add discovery tests proving a public non-generic leaf interface assignable to `INeuron` maps to one non-abstract implementation deriving from `Neuron`.
- [x] Add fail-fast tests for a missing implementation, duplicate implementations, a non-`Neuron` implementation, a generic leaf interface, and a mapping absent from the Orleans local grain manifest.
- [x] Add a test proving base capability interfaces are excluded when a more-derived `INeuron` interface exists.
- [x] Add a test proving the resulting dictionary is keyed by `Type` and immutable after startup.
- [x] Run the owning test project and observe red because Quadrant does not exist.
- [x] Implement `NeuronTypeCatalogBuilder` as a pure function over an explicit `IEnumerable<Type>` so discovery rules are deterministic in unit tests.
- [x] Let `QuadrantStartupTask` supply types from loaded application assemblies, following the IAW startup pattern.
- [x] Validate activation metadata through `IClusterManifestProvider.LocalGrainManifest`, using Orleans `WellKnownGrainInterfaceProperties` and `WellKnownGrainTypeProperties` constants rather than handwritten manifest keys.
- [x] Register `Quadrant` as a singleton and `silo.AddStartupTask<QuadrantStartupTask>()`.
- [x] Allow every validation exception to escape so Orleans stops silo startup.
- [x] Run the owning test project; require green.
- [x] Run the root checkpoint; require green.
- [x] Codex commits as `feat: discover neuron types into quadrant`.

---

## Task 5: Add Gmail and Salesforce as Empty Typed Capability Identities

**Assigned paths:**

- Create: `modules/Google.Contracts/Google.Contracts.csproj`
- Create: `modules/Google.Contracts/IGmail.cs`
- Create: `modules/Google/Google.csproj`
- Create: `modules/Google/GmailNeuron.cs`
- Create: `modules/Google/GoogleHosting.cs`
- Create: `modules/Salesforce.Contracts/Salesforce.Contracts.csproj`
- Create: `modules/Salesforce.Contracts/ISalesforce.cs`
- Create: `modules/Salesforce/Salesforce.csproj`
- Create: `modules/Salesforce/SalesforceNeuron.cs`
- Create: `modules/Salesforce/SalesforceHosting.cs`
- Modify: `hosts/Brain.Kernel.Host/Brain.Kernel.Host.csproj`
- Modify: `hosts/Brain.Kernel.Host/Program.cs`
- Modify: `Brain.slnx`
- Create: `tests/DigitalBrain.Tests/Providers/ProviderNeuronIdentityTests.cs`
- Create: `tests/DigitalBrain.Tests/Client/ProviderClientCompilationTests.cs`

**Required shapes:**

```csharp
public interface IGmail : INeuron;
public interface ISalesforce : INeuron;

public sealed class GmailNeuron : Neuron, IGmail
{
}

public sealed class SalesforceNeuron : Neuron, ISalesforce
{
}
```

- [ ] Add tests proving both public interfaces declare zero methods, extend `INeuron`, and each has exactly one `Neuron` implementation in Quadrant.
- [ ] Add tests proving `DigitalBrainClient.Get<IGmail>()` and `.Get<ISalesforce>()` bind the authenticated owner as the complete grain key.
- [ ] Add project-source guards proving no custom Gmail/Salesforce provider interface, copied provider request/response DTO, `Ask`, `InvokeMcpTool`, MCP tool-name constant, or Google/Salesforce SDK remains in these modules.
- [ ] Add a compile-time client test containing only `brain.Get<IGmail>()` and `brain.Get<ISalesforce>()`. This is the complete v1 caller cutover because Task 2 removed every legacy caller and the callable MCP surface is intentionally paused.
- [ ] Run the owning test project and observe red because the new leaf contracts and Salesforce projects do not exist.
- [ ] Implement the two empty durable neurons and hosting extensions which only make their assemblies available to Orleans.
- [ ] Add the new projects to the solution and kernel host.
- [ ] Remove `Google.Apis.*` and `DeveloperForce.Force` package versions when no active project references them.
- [ ] Run the owning test project; require green.
- [ ] Run the root checkpoint; require green.
- [ ] Codex commits as `feat: add typed provider neuron identities`.

MCP connectivity remains deliberately absent. This task establishes durable ownership and type identity without inventing a callable provider API.

---

## Task 6: Implement the Durable External-Operation Ledger and Notification Outbox

**Assigned paths:**

- Modify: `kernel/Brain.Contracts/ExternalOperation.cs`
- Modify: `kernel/Brain.Contracts/NeuronNotification.cs`
- Modify: `kernel/Brain.Kernel/Neuron.cs`
- Modify: `kernel/Brain.Kernel/NeuronDurableState.cs`
- Create: `kernel/Brain.Kernel/NeuronReminder.cs`
- Create: `kernel/Brain.Kernel/NeuronNotificationPublisher.cs`
- Create: `kernel/Brain.Kernel/NeuronOutboxDrainer.cs`
- Modify: `kernel/Brain.Kernel/KernelHosting.cs`
- Modify: `kernel/Brain.Kernel/NeuronJournalJsonContext.cs`
- Modify: `tests/DigitalBrain.Tests/Kernel/TestNeuron.cs`
- Modify: `tests/DigitalBrain.Tests/Kernel/NeuronArchitectureTests.cs`
- Modify: `tests/DigitalBrain.Tests/Kernel/NeuronDurableStateTests.cs`
- Create: `tests/DigitalBrain.Tests/Kernel/ExternalOperationRecoveryTests.cs`
- Create: `tests/DigitalBrain.Tests/Kernel/NeuronOutboxTests.cs`

**Required state machine:**

```text
Pending -> Succeeded
        -> Failed
        -> Unknown
```

- [ ] Add a test proving a pending operation is durable before a test-only external function is invoked.
- [ ] Add a crash-window test which persists `Pending`, records the test effect, prevents outcome persistence, reactivates the grain, and observes `Unknown` unless a typed reconciler proves success.
- [ ] Add an idempotent reconciliation test which transitions `Unknown` to `Succeeded` from a provider receipt.
- [ ] Add an outbox test proving state and typed notification are committed before publishing.
- [ ] Add a stream-failure test proving the outbox record remains pending and is published after retry.
- [ ] Add an at-least-once test proving a consumer deduplicates by operation ID.
- [ ] Add a recovery test proving deleting or losing stream messages cannot delete or contradict durable neuron state.
- [ ] Configure the test silo and client with `AddMemoryStreams(nameof(NeuronNotification), ...)`; this is the explicitly test-only transport and uses the same provider name as production.
- [ ] Run the owning test project and observe red because recovery and draining behavior are absent.
- [ ] Implement transition validation as enum/record logic with no string event switch.
- [ ] Keep the base free of a generic provider invocation method; test neurons call their test function directly around explicit durable writes.
- [ ] Do not add a fourth universal durable member. Task 2's three-member state-shape test remains authoritative.
- [ ] Map journal/storage write failures to `NeuronFailureKind.StorageUnavailable`, ambiguous outcomes to `OperationUnknown`, and provider failures to `ProviderUnavailable` or `OperationFailed` as appropriate.
- [ ] Use a durable reminder only to wake outbox recovery. The outbox remains the source of delivery truth.
- [ ] Publish `NeuronNotification` through one named Orleans stream provider whose provider name is derived with `nameof(NeuronNotification)`.
- [ ] Record delivery attempt and completion durably.
- [ ] Run the owning test project; require green.
- [ ] Run the root checkpoint; require green.
- [ ] Codex commits as `feat: add durable operation and notification recovery`.

---

## Task 7: Reduce AI to Typed Model Roles

**Assigned paths:**

- Create: `modules/AI/AI.csproj`
- Create: `modules/AI/AiHosting.cs`
- Create: `modules/AI/ModelRole.cs`
- Create: `modules/AI/DigitalBrainAiOptions.cs`
- Create: `modules/AI/ChatModel.cs`
- Modify: `hosts/Brain.Kernel.Host/Brain.Kernel.Host.csproj`
- Modify: `hosts/Brain.Kernel.Host/Program.cs`
- Modify: `Brain.slnx`
- Create: `tests/DigitalBrain.Tests/AI/TypedModelRoleTests.cs`

**Required shapes:**

```csharp
public sealed class Fast
{
}

public sealed class Balanced
{
}

public sealed class Reasoning
{
}

public sealed class ChatModel<TRole>(IChatClient client)
    where TRole : class;
```

- [ ] Add tests proving Fast, Balanced, and Reasoning are selected by generic role type, never by a string key.
- [ ] Add startup validation tests proving duplicate role assignment and missing required role assignment fail.
- [ ] Run the owning test project and observe red because the typed role registrations do not exist.
- [ ] Do not restore the deleted LLM `Kind`, capability string IDs, neuron-ID parsing, or string-keyed `IChatClient` registrations.
- [ ] Upgrade Microsoft.Extensions.AI packages to `10.8.0`.
- [ ] Bind external provider/model IDs only in `DigitalBrainAiOptions` at the hosting/configuration boundary.
- [ ] Register `ChatModel<Fast>`, `ChatModel<Balanced>`, and `ChatModel<Reasoning>` as concrete typed services.
- [ ] Do not add an AI neuron or conversational API in this task.
- [ ] Run the owning test project; require green.
- [ ] Run the root checkpoint; require green.
- [ ] Codex commits as `refactor: use typed ai model roles`.

---

## Task 8: Add the IAW-Style Aspire `DigitalBrainResource`

**Assigned paths:**

- Create: `hosts/DigitalBrain.Hosting/DigitalBrain.Hosting.csproj`
- Create: `hosts/DigitalBrain.Hosting/DigitalBrainResource.cs`
- Create: `hosts/DigitalBrain.Hosting/DigitalBrainClientResource.cs`
- Create: `hosts/DigitalBrain.Hosting/DigitalBrainModel.cs`
- Create: `hosts/DigitalBrain.Hosting/DigitalBrainModelBuilder.cs`
- Create: `hosts/DigitalBrain.Hosting/DigitalBrainHostingExtensions.cs`
- Modify: `hosts/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`
- Replace: `hosts/DigitalBrain.AppHost/AppHost.cs`
- Modify: `hosts/Brain.Kernel.Host/Brain.Kernel.Host.csproj`
- Replace: `hosts/Brain.Kernel.Host/Program.cs`
- Modify: `Directory.Packages.props`
- Modify: `Brain.slnx`
- Create: `tests/DigitalBrain.Tests/Aspire/DigitalBrainResourceTests.cs`
- Modify: `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`

**Required AppHost surface:**

```csharp
var brain = builder.AddDigitalBrain(nameof(brain))
    .WithLLM<GptFast>().AsFast()
    .WithLLM<ClaudeBalanced>().AsBalanced()
    .WithLLM<GptReasoning>().AsReasoning()
    .WithEmbedding<TextEmbedding>();

builder.AddProject<Projects.Brain_Kernel_Host>("kernel")
    .WithReference(brain);

clientProject.WithReference(brain.AsClient());
```

`DigitalBrainResource` is an IAW-style composite wrapper around the official `OrleansService`, not a replacement Orleans resource implementation.

- [ ] Add AppHost model tests proving `AddDigitalBrain` creates Azure Storage tables for clustering/reminders, Azure Blob storage for official Journaling, Azure Queues for streams, and the official Orleans resource.
- [ ] Add tests proving `WithReference(brain)` gives the kernel Orleans silo configuration, journal blob reference, stream/reminder dependencies, typed AI settings, and protected secret parameters.
- [ ] Add tests proving `WithReference(brain.AsClient())` gives only the official Orleans client reference and no journal blob, provider secret, or model credential.
- [ ] Add tests proving duplicate/missing Fast, Balanced, Reasoning, or embedding assignments fail during AppHost model construction.
- [ ] Run the owning test project and observe red because `DigitalBrainResource` does not exist.
- [ ] Pin every Aspire SDK/package to stable `13.4.6`; remove the mixed 13.5 preview entries.
- [ ] Implement `DigitalBrainResource` following IAW's `IAWService`/`IAWClientService` shape, while keeping privileged dependencies out of the client overload.
- [ ] Configure the Orleans resource with `.WithClustering(tables)`, `.WithReminders(tables)`, and `.WithStreaming(nameof(NeuronNotification), queues)`.
- [ ] Reference journal blobs separately from the Orleans resource because official Journaling storage is configured in the silo, not through `.WithGrainStorage`.
- [ ] Configure local Azure Storage with Azurite and a persistent data volume.
- [ ] In the kernel host, register the Aspire-keyed Azure clients required by clustering, reminders, and streams; call `UseOrleans(silo => ...)`; then call `AddJournalStorage`, configure `AddAzureBlobJournalStorage` from the injected journal connection string, call `AddBrainKernel`, provider hosting extensions, and typed AI hosting.
- [ ] Remove all localhost clustering and all volatile journal setup from production host code.
- [ ] Use `nameof`-derived configuration paths wherever the API permits; contain unavoidable Aspire resource names and external model IDs inside this hosting project.
- [ ] Run the owning test project; require green.
- [ ] Run the root checkpoint; require green.
- [ ] Run `aspire doctor --non-interactive`; require no blocking diagnostic.
- [ ] Codex commits as `feat: add digital brain aspire resource`.

---

## Task 9: Remove the Rejected Product Trees and Generic Edges

**Assigned paths:**

- Delete: `edge/Brain.Mcp/**`
- Delete: `modules/Brain.Modules.Behaviors/**`
- Delete: `modules/Brain.Modules.Web/**`
- Delete: `src/**`
- Delete: `tests/Brain.Tests/**`
- Delete: `tests/Brain.FeasibilityTests/AgentFramework/**`
- Delete: `tests/Brain.FeasibilityTests/TypedReferences/**`
- Delete: every obsolete test in `tests/DigitalBrain.Tests/**` which targets a removed project or generic route
- Modify: `Brain.slnx`
- Modify: `Directory.Packages.props`
- Modify: `tests/DigitalBrain.Tests/Architecture/ProjectTopologyTests.cs`
- Create: `tests/DigitalBrain.Tests/Architecture/DurableNeuronGuardTests.cs`

`workspace/**` and `sources/**` are forbidden paths for this task.

- [ ] Add architecture guards which scan only active product roots and reject `INeuronKind`, `KindCatalog`, `NeuronAddress`, `DispatchProxy`, `INeuronContract`, `NeuronContractAttribute`, `AddBrainKind`, `VolatileJournalStorageProvider`, custom `IJournalStorageProvider` implementations, string contract switches, keyed provider services, `Ask`, and `InvokeMcpTool`.
- [ ] Add reflection guards proving provider neuron implementations expose no public callable method which is absent from their leaf interface and `DigitalBrainClient` exposes no provider-specific or string-invocation method.
- [ ] Add a solution topology test listing the final active projects exactly.
- [ ] Run the owning test project and observe red because rejected files still exist.
- [ ] Delete the excluded `src/**` implementation, its excluded tests, and its custom journal provider remnants.
- [ ] Delete the generic MCP edge because the public MCP callable design is paused.
- [ ] Verify the generic Flutter backend was deleted during Task 2, then delete the still-tracked unused behavior/web modules without changing `workspace/**`.
- [ ] Remove every now-unused package version, including MCP SDK, Google APIs, Salesforce SDK, Agent Framework, gRPC packages used only by deleted projects, and obsolete Aspire preview packages.
- [ ] Keep only package entries referenced by an active project.
- [ ] Run `dotnet restore Brain.slnx`; require success without unresolved or downgraded packages.
- [ ] Run the owning test project; require green.
- [ ] Run the root checkpoint; require green with no skips.
- [ ] Run `git diff --check`; require no errors.
- [ ] Run `rg -n "INeuronKind|KindCatalog|NeuronAddress|DispatchProxy|INeuronContract|NeuronContractAttribute|AddBrainKind|VolatileJournalStorageProvider" kernel modules hosts tests -g '*.cs' -g '*.csproj'`; require no match.
- [ ] Codex commits as `refactor: delete rejected routing architectures`.

---

## Task 10: Prove the Live Aspire Recovery Path

**Assigned paths:**

- Modify only if a verified defect is found: `hosts/DigitalBrain.AppHost/**`
- Modify only if a verified defect is found: `hosts/DigitalBrain.Hosting/**`
- Modify only if a verified defect is found: `hosts/Brain.Kernel.Host/**`
- Modify only if a verified defect is found: `tests/Brain.FeasibilityTests/Journaling/**`

- [ ] Run `aspire docs search "Orleans Azure Storage client reference journal storage" --non-interactive`.
- [ ] Run `aspire docs api search "OrleansService AsClient WithReference WithStreaming" --language csharp --non-interactive`.
- [ ] Run `aspire start --non-interactive`.
- [ ] Run `aspire wait kernel --non-interactive`.
- [ ] Inspect `aspire describe --non-interactive`, structured logs, console logs, and Orleans traces.
- [ ] Stop only the `kernel` resource with `aspire resource kernel stop --non-interactive`.
- [ ] Start only the `kernel` resource with `aspire resource kernel start --non-interactive`.
- [ ] Wait for `kernel` and verify logs show the same persistent Azurite journal resource is reopened without journal replay errors.
- [ ] Re-run `OfficialJournalRecoveryTests` after the live host restart to prove exact recovered state and a subsequent successful write against the same official provider API.
- [ ] Verify the client-only resource environment contains no journal/blob/model/provider secret.
- [ ] Verify a cross-owner raw grain call is denied in the running cluster.
- [ ] Verify a stream delivery failure leaves the durable outbox recoverable.
- [ ] Run the feasibility project, owning tests, and root checkpoint again.
- [ ] Run `aspire doctor --non-interactive`; require no blocking diagnostic.
- [ ] Stop the AppHost with `aspire stop --non-interactive`.
- [ ] Codex commits a correction only if files changed, using `fix: complete live durable brain recovery`.

---

## Task 11: Final Documentation and Adversarial Verification

**Assigned paths:**

- Modify: `README.md`
- Delete: `docs/superpowers/plans/2026-07-18-digitalbrain-grok-orchestration.md`
- Delete: older conflicting files under `docs/superpowers/plans/**`
- Delete after README captures the lasting decisions: `docs/superpowers/specs/2026-07-18-durable-neuron-architecture-design.md`
- Retain until Codex declares execution complete: `docs/superpowers/plans/2026-07-18-durable-neuron-implementation.md`

- [ ] Launch a fresh read-only Grok review session with the approved specification, final diff range, and explicit instructions to find correctness, durability, security, package, and architectural violations.
- [ ] Launch a second fresh read-only Grok review session focused only on deletion completeness, string routing, MCP duplication, secret propagation, and stream-as-truth regressions.
- [ ] Send every actionable finding to bounded Grok correction sessions and repeat the owning gates.
- [ ] Update README with the final developer experience: `brain.Get<IGmail>()`, `brain.Get<ISalesforce>()`, authenticated owner binding, DurableGrain truth, Quadrant discovery, notification-only streams, and Aspire kernel/client reference separation.
- [ ] Document that provider callable APIs remain intentionally absent pending a separately approved type-safe MCP design.
- [ ] Delete conflicting historical plans after their lasting decisions are represented in README.
- [ ] Run `dotnet restore Brain.slnx`.
- [ ] Run `dotnet build Brain.slnx --no-restore`.
- [ ] Run `dotnet test tests/Brain.FeasibilityTests/Brain.FeasibilityTests.csproj --no-build --logger "console;verbosity=minimal"`.
- [ ] Run `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj --no-build --logger "console;verbosity=minimal"`.
- [ ] Run `dotnet test Brain.slnx --no-build --logger "console;verbosity=minimal"`.
- [ ] Require 0 failed and 0 skipped tests in every project.
- [ ] Run `git diff --check`.
- [ ] Run the complete forbidden-token scan over active product roots.
- [ ] Run `aspire doctor --non-interactive`.
- [ ] Inspect `git status --short`; require only the intentional final documentation changes before commit.
- [ ] Codex commits as `docs: publish durable neuron architecture`.

## Completion Criteria

Implementation is complete only when all of the following are simultaneously true:

- Official Azure Blob Journaling survives a complete silo restart and continued writes.
- Failed journal intent persistence prevents an external effect.
- `INeuron` is a zero-method marker.
- Every concrete production neuron derives from `Neuron : DurableGrain`.
- One owner plus one leaf interface selects one grain; no provider address syntax exists.
- `DigitalBrainClient.Get<TNeuron>()` returns a real Orleans reference and accepts no owner/key argument.
- Cross-owner calls fail on the server.
- Quadrant is populated once by an Orleans startup task and keyed only by `Type`.
- Missing, duplicate, invalid, or Orleans-unregistered neuron implementations stop startup.
- `IGmail` and `ISalesforce` are empty typed capability identities with one implementation each.
- No custom or copied Gmail/Salesforce operations remain.
- External-operation states recover as Pending, Succeeded, Failed, or Unknown without pretending ambiguous success.
- Streams are notification-only and durable outbox state survives stream failure.
- `DigitalBrainResource` owns Orleans, official Journaling storage, reminders, streams, model roles, embedding configuration, and protected credentials.
- `brain.AsClient()` exposes Orleans client connectivity without journal storage or secrets.
- The rejected `Kind`, proxy, generic MCP, custom journal, reactive, and generic Flutter backend trees are deleted.
- Root restore, build, tests, live Aspire restart proof, and adversarial reviews are green.

## Deferred Type-Safe MCP and Single-File Programming Design

The type-safe interface model is the operating system's primary product feature. This plan deliberately stops at:

```csharp
var gmail = brain.Get<IGmail>();
var salesforce = brain.Get<ISalesforce>();
```

The next design must decide how official MCP schemas become stable C# callable surfaces without:

- copying provider implementations;
- introducing a generic `Ask`;
- exposing string tool names;
- creating an internal invocation protocol;
- coupling generated single-file applications to provider transport details.

That follow-on design must preserve ordinary compiler checking so plain English can generate stable single-file C# programs against DigitalBrain interfaces. It is a separate approval gate, not unfinished implementation work in this plan.
