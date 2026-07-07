# Repair and Restore DigitalBrain.Tests Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Repair `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj` (currently 35 compile errors, orphaned from the build graph since 2026-07-06) and re-add it to `Brain.slnx` so CI's `dotnet test Brain.slnx` actually exercises Kernel, Gateway, SelfEvolution, Architecture-guard, and E2E test coverage again.

**Architecture:** No new abstractions. This is a mechanical repair: delete dead test code for features already deleted from production (Demo, Market/Bitcoin), fix stale/renamed `using` directives left over from earlier namespace moves, resolve one assembly-alias conflict, fix one broken test-double usage, then wire the project back into the solution and verify.

**Tech Stack:** .NET 11 / xUnit / Reqnroll / Orleans.TestingHost / MSBuild project references and aliases.

## Global Constraints

- No vacuous `/// <summary>` comments in anything touched. Self-explanatory naming over comments; only add a comment for a genuinely non-obvious WHY.
- Run a code review pass before declaring any task's work done (per this repo owner's global instruction).
- Verify any unfamiliar package/framework API via Context7 before writing code against it — not expected to be needed here (all APIs used already exist elsewhere in this exact repo; copy proven patterns, don't re-derive).
- **Coordinate before starting:** another session was actively committing to this repo during this plan's research (`0f8ddef`, `5154244`, `07ac815` — Context/Ino consolidation and Economics/Market trash removal). Re-run `git status` and `git log --oneline -5` before Task 1 to confirm nothing has moved further, and re-run the Task 1 build if it has.
- Do not touch `docs/archive/**` (existing repo convention for this plan series).
- Commit each task separately — small, reviewable, buildable slices, matching this repo's existing commit history style.

---

## File Structure

**Modify (production code, one latent bug fix):**
- `src/DigitalBrain.Kernel/Sync/CheckpointBackupTrigger.cs` — remove the stale `"market-data-main"` entry from `V1NeuronIds` (the neuron behind it was deleted in `07ac815`; the id now silently falls through `NeuronResolver`'s default arm instead of erroring, which only a test — currently unable to run — would catch).

**Modify (test code — delete dead coverage for already-deleted features):**
- `tests/DigitalBrain.Tests/Architecture/CoreBoundaryTests.cs` — remove 5 Demo-specific `[Fact]`s + 2 helper methods + the dead `using`.
- `tests/DigitalBrain.Tests/Gateway/GatewayServiceTests.cs` — remove the SurfaceDemo test, the bitcoin-price test, both `_marketClient` fields/registrations, fix the `Fire_ThenTimeline_ShowsDemoMessage` assertion, remove dead usings.
- `tests/DigitalBrain.Tests/Spikes/JournalFormatSpikeTests.cs` — replace the deleted `DemoMessageSynapse` with a small local synapse type (this file already defines its own probe neuron; it's meant to be self-contained).
- `tests/DigitalBrain.Tests/Sync/CheckpointBackupTriggerTests.cs`, `CheckpointRestoreTriggerTests.cs` — remove the now-unnecessary `IMarketDataApiClient` DI shim, fix the neuron-count assertion, delete the now-uncompilable `IMarketDataNeuron` assertion.
- Delete outright: `tests/DigitalBrain.Tests/Market/CoinGeckoApiClientTests.cs`, `tests/DigitalBrain.Tests/Market/MarketDataNeuronTests.cs`, `tests/DigitalBrain.Tests/Steps/XBitcoinTelegramDemoSteps.cs`, `tests/DigitalBrain.Tests/TestSupport/FakeMarketDataApiClient.cs`.

**Modify (test code — fix stale/renamed namespace usings):**
- `tests/DigitalBrain.Tests/Auth/UserSessionNeuronTests.cs`, `Gateway/GatewayServiceTests.cs`, `Ino/InoNeuronTabularDataTests.cs`, `Kernel/ExperienceStepDispatchTests.cs`, `Ui/ChatNeuronTests.cs`, `Ui/HomeFeedBusTests.cs`, `Ui/HomeFeedCrossSiloTests.cs` — delete the dead `using DigitalBrain.Core.Ui;` line (namespace no longer exists; every one of these files already has the correct replacement `using` alongside it or via the project's global usings).
- `tests/DigitalBrain.Tests/Kernel/ExperienceStepDispatchTests.cs` — additionally add `using DigitalBrain.Ui.Contracts.Ui;` (for `RfwCard`, which the project's blanket `DigitalBrain.Ui.Contracts` global using does not cover — `RfwCard` lives one namespace deeper).
- `tests/DigitalBrain.Tests/Distribution/BundleManifestTests.cs`, `Domains/KitExperienceTests.cs` — replace `using DigitalBrain.Core.UiKit;` with `using DigitalBrain.Pack.Contracts.UiKit;` (`KitExperience`/`UiExperience` moved there).
- `tests/DigitalBrain.Tests/Ino/InoNeuronTabularDataTests.cs` — also delete `using DigitalBrain.Core.UiKit;` outright (dead weight; nothing in this file needs it).

**Modify (test code — other fixes):**
- `tests/DigitalBrain.Tests/Steps/GoogleOAuthSteps.cs` — remove the broken `InoTestHarness` field/calls (the class is `static`; this file never obtained a real `IInoNeuron` grain to pass it anyway).
- `tests/DigitalBrain.Tests/TestSupport/KernelWebApplicationFactory.cs` — no code change; fixed by the csproj alias below.
- `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj` — add an aliased `ProjectReference` to `DigitalBrain.Mcp.csproj` to resolve the `Program` ambiguity between `DigitalBrain.Kernel` and `DigitalBrain.Mcp` (both use top-level statements, so both generate a `global::Program` — the ambiguity exists because `DigitalBrain.Kernel.csproj:58` unaliased-references Mcp, and that flows transitively into this project).
- `tests/DigitalBrain.Tests/Mcp/DigitalBrainToolsTests.cs`, `Mcp/McpTransportSplitTests.cs`, `Foundry/CodeFoundryApprovalTests.cs` — adjust their `using DigitalBrain.Mcp;` to route through the new alias (only needed if Task 6's chosen fix requires it — see Task 6's verification step).

**Modify (solution wiring):**
- `Brain.slnx` — add `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj` back to the `/tests/` folder.

**Modify (docs, small, dependent on the above landing):**
- `docs/SYSTEM_DESIGN.md:424-428` — the E2E section still describes the removed `RUN_FLUTTER_E2E`/`FAST_UI_E2E` env vars and real browser rendering; update to describe the current `RUN_REAL_STACK_E2E` + `EnableAppHostTests` mechanism.

---

### Task 1: Fix the stale `market-data-main` V1 neuron id (production code)

**Files:**
- Modify: `src/DigitalBrain.Kernel/Sync/CheckpointBackupTrigger.cs:11-17`

**Interfaces:** none — `V1NeuronIds` is a private static array, only consumed within this same file's `BackupAsync`.

- [ ] **Step 1: Re-run git status/log to confirm the baseline hasn't moved**

Run: `git status --short && git log --oneline -5`
Expected: clean working tree, `HEAD` still `07ac815` (or later — if later, re-read whatever changed before continuing).

- [ ] **Step 2: Remove `"market-data-main"` from the V1 id list and fix the stale comment**

Replace:
```csharp
    // V1 fixed neuron-id scope: the nine singleton neurons the kernel warms up at startup (Program.cs), not a
    // general per-user neuron enumeration (no such registry exists yet).
    private static readonly string[] V1NeuronIds =
    [
        "status-main", "ino-main", "context-main",
        "db-main", "chart-main", "session-main", "automation-main", "market-data-main"
    ];
```
with:
```csharp
    // V1 fixed neuron-id scope: the seven singleton neurons the kernel warms up at startup (Program.cs), not a
    // general per-user neuron enumeration (no such registry exists yet).
    private static readonly string[] V1NeuronIds =
    [
        "status-main", "ino-main", "context-main",
        "db-main", "chart-main", "session-main", "automation-main"
    ];
```
(`market-data-main` backed `MarketDataNeuron`, deleted in commit `07ac815` — `NeuronResolver` has no case for this id any more, so it was silently falling through to the generic `IGeneratedNeuron` fallback instead of a dedicated neuron.)

- [ ] **Step 3: Build to confirm this file alone still compiles**

Run: `dotnet build src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj -c Debug --nologo`
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add src/DigitalBrain.Kernel/Sync/CheckpointBackupTrigger.cs
git commit -m "fix: drop stale market-data-main from checkpoint V1 neuron ids

MarketDataNeuron was deleted in 07ac815; this id was silently falling
through NeuronResolver's default arm instead of erroring. Only a test in
DigitalBrain.Tests would have caught this, and that project has been
orphaned from Brain.slnx since 2026-07-06 (fixed later in this series)."
```

---

### Task 2: Delete dead test coverage for the removed Market/Bitcoin-price feature

**Files:**
- Delete: `tests/DigitalBrain.Tests/Market/CoinGeckoApiClientTests.cs`
- Delete: `tests/DigitalBrain.Tests/Market/MarketDataNeuronTests.cs`
- Delete: `tests/DigitalBrain.Tests/Steps/XBitcoinTelegramDemoSteps.cs`
- Delete: `tests/DigitalBrain.Tests/TestSupport/FakeMarketDataApiClient.cs`
- Modify: `tests/DigitalBrain.Tests/Gateway/GatewayServiceTests.cs`
- Modify: `tests/DigitalBrain.Tests/Sync/CheckpointBackupTriggerTests.cs`
- Modify: `tests/DigitalBrain.Tests/Sync/CheckpointRestoreTriggerTests.cs`

**Interfaces:** none — this only removes test code; nothing outside this project references any of it (confirmed via repo-wide grep for `FakeMarketDataApiClient`/`IMarketDataApiClient`).

- [ ] **Step 1: Delete the four whole files**

```bash
git rm tests/DigitalBrain.Tests/Market/CoinGeckoApiClientTests.cs
git rm tests/DigitalBrain.Tests/Market/MarketDataNeuronTests.cs
git rm tests/DigitalBrain.Tests/Steps/XBitcoinTelegramDemoSteps.cs
git rm tests/DigitalBrain.Tests/TestSupport/FakeMarketDataApiClient.cs
```
(`Market/` will be empty after this — `git rm` removes the directory automatically when it has no tracked files left.)

- [ ] **Step 2: Remove the Market pieces from `GatewayServiceTests.cs`**

Remove the using (line 11): delete `using DigitalBrain.Kernel.Market;`

Remove the first `_marketClient` field and its DI registration:
```csharp
    private readonly FakeMarketDataApiClient _marketClient = new();
```
and
```csharp
            services.AddSingleton<IMarketDataApiClient>(_marketClient);
```
(the one inside the first class's `ConfigureSilo`, currently around line 52 — keep every other line of that `ConfigureServices` block unchanged).

Delete the entire `Send_InoRequest_BitcoinPriceIntent_DeliversFormattedPriceSurface` test method (starts around line 319 with `[Fact]` immediately above it, ends at the closing brace of that method — it is the bitcoin-price-specific test that sets `_marketClient.Price = "$42,123.45";` then drives the flow through `InoRequest`/`LoginRequest`; the whole method goes since the intent it tests no longer exists).

Repeat the same two removals (field + DI registration) in the second test class in this file, `GatewayServiceSalesforceViaChatIdentityTests` (field around line 622, registration around line 643) — confirm no `[Fact]` in that class actually exercises market data before removing (it doesn't; the field/registration are only there because `MarketDataNeuron` used to need to activate as one of the "V1 neuron ids" during silo configuration, and that neuron is now gone).

- [ ] **Step 3: Fix `Fire_ThenTimeline_ShowsDemoMessage`'s now-wrong assertion**

This test's compile error is unrelated to Market — it references the deleted `DemoMessageSynapse` type. Before fixing, read `src/DigitalBrain.Kernel/Gateway/GatewayService.cs`'s `Fire` method (around line 177-184): it now does
```csharp
await neuron.FireAsync(new Signal("DemoMessage", new Dictionary<string, object?> { ["text"] = request.Text }));
```
— i.e. it fires a `Signal` named `"DemoMessage"`, not a `DemoMessageSynapse`. Update the assertion from:
```csharp
        Assert.Contains(timeline.Entries, e => e.Type == nameof(DemoMessageSynapse) && e.Text.Contains("ping-123"));
```
to check against whatever `TimelineEntry.Type` actually resolves to for a `Signal`-based synapse (inspect `Signal`'s definition in `DigitalBrain.Core` and how `GatewayService.Timeline` projects synapses into `TimelineEntry` to get the exact string — it is very likely `"DemoMessage"` itself, since `Signal`'s first constructor argument is typically its own `Type`, but confirm by reading `Signal`'s record definition rather than assuming). Run the single test after fixing (Task 7 covers the full-project build first; this test can only actually execute once Task 7's build is green, but get the assertion textually correct now).

- [ ] **Step 4: Fix `CheckpointBackupTriggerTests.cs`**

Remove the using (line 8): delete `using DigitalBrain.Kernel.Market;`

Remove the now-unnecessary `ConfigureSilo` override entirely (lines 29-31):
```csharp
    // MarketDataNeuron (one of the nine V1 ids) needs a real IMarketDataApiClient to activate; a fake stands in
    // here the same way MarketDataNeuronTests.cs does, since this test never actually calls the market API.
    protected override void ConfigureSilo(ISiloBuilder builder) =>
        builder.ConfigureServices(services => services.AddSingleton<IMarketDataApiClient>(new FakeMarketDataApiClient()));
```
(no neuron needs this shim any more — `MarketDataNeuron` is deleted, and Task 1 already removed it from `V1NeuronIds`).

Update the count assertions in `BackupAsync_UploadsOneBlobPerV1NeuronId_AgainstRealGrains` from 8 to 7 (matching Task 1's `V1NeuronIds` change):
```csharp
        Assert.Equal(7, manifest.Entries.Count);
        Assert.Equal(7, syncContainer.Uploads.Count);
```

Delete the `NeuronResolver_Resolves_AutomationAndMarketData_ToTheirOwnInterfaces_NotIDemoNeuronFallback` test method entirely (it asserts `IMarketDataNeuron`, a type that no longer exists anywhere in the codebase — there is nothing left to discriminate; the automation-id half of this regression guard is not worth splitting out on its own since `automation-main`'s correct resolution is already exercised implicitly by every other test that fires against it).

- [ ] **Step 5: Fix `CheckpointRestoreTriggerTests.cs` the same way**

Remove `using DigitalBrain.Kernel.Market;` (line 8) and the `ConfigureSilo` override (lines 26-29, same shape as Task 2 Step 4). Check the rest of the file for any hardcoded neuron-id count (grep for `8` or `nine`/`V1` near assertions) and update to 7 if present, matching Task 1.

- [ ] **Step 6: Build to confirm this task's slice compiles (ignore pre-existing unrelated errors from later tasks)**

Run: `dotnet build tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Debug --nologo 2>&1 | grep -E "error CS"`
Expected: every `Market`, `FakeMarketDataApiClient`, and `IMarketDataApiClient` error from the original 35 is gone. Remaining errors should only be the ones Tasks 3-6 fix (Demo, Core.Ui/UiKit, RfwCard, InoTestHarness, Program).

- [ ] **Step 7: Commit**

```bash
git add tests/DigitalBrain.Tests/Market tests/DigitalBrain.Tests/Steps/XBitcoinTelegramDemoSteps.cs \
  tests/DigitalBrain.Tests/TestSupport/FakeMarketDataApiClient.cs tests/DigitalBrain.Tests/Gateway/GatewayServiceTests.cs \
  tests/DigitalBrain.Tests/Sync/CheckpointBackupTriggerTests.cs tests/DigitalBrain.Tests/Sync/CheckpointRestoreTriggerTests.cs
git commit -m "test: delete dead Market/Bitcoin-price coverage, fix Fire timeline assertion

Market/CoinGeckoApiClient/MarketDataNeuron were deleted from production
code in 07ac815; this project (orphaned from Brain.slnx since
2026-07-06) never got the matching test cleanup until now."
```

---

### Task 3: Delete dead test coverage for the removed Demo/SurfaceDemo feature

**Files:**
- Modify: `tests/DigitalBrain.Tests/Architecture/CoreBoundaryTests.cs`
- Modify: `tests/DigitalBrain.Tests/Gateway/GatewayServiceTests.cs`
- Modify: `tests/DigitalBrain.Tests/Spikes/JournalFormatSpikeTests.cs`

**Interfaces:**
- Produces: `SpikePayloadSynapse(string Text)` in `JournalFormatSpikeTests.cs` (new, local, self-contained — no other file needs to know about it).

- [ ] **Step 1: Remove the 5 Demo-specific facts + 2 helpers + dead using from `CoreBoundaryTests.cs`**

Delete `using DigitalBrain.Demo.Runtime;` (line 3).

Delete these five `[Fact]` methods in full (they assert facts about `DigitalBrain.Demo.Contracts`/`DigitalBrain.Demo.Runtime`, both fully deleted projects — there is nothing left to assert):
- `Demo_Contracts_Depend_On_Core_Not_The_Other_Way_Around` (lines 60-70)
- `Demo_Contracts_Do_Not_Reference_Runtime_Host_Integration_Or_Marketplace_Packages` (lines 72-92)
- `Demo_Runtime_Depends_On_Contract_Packages_Not_Runtime_Host_Or_Integrations` (lines 94-121)
- `Core_Does_Not_Own_Demo_Test_Contracts` (lines 123-132)
- `Demo_Runtime_Owns_Surface_Demo_Request_And_Runtime_Helpers` (lines 134-139)

In `Demo_Runtime_Depends_On_Contract_Packages_Not_Runtime_Host_Or_Integrations`'s neighbor logic there's nothing to preserve — the whole method goes with it.

Delete the two now-unused private helpers:
```csharp
    private static string[] DemoContractsReferenceNames() => ReferenceNames(typeof(DemoMessageSynapse).Assembly);

    private static string[] DemoRuntimeReferenceNames() => ReferenceNames(typeof(SurfaceDemoRuntime).Assembly);
```

Leave every other `[Fact]` in this file untouched (Pack.Contracts, Ui.Contracts, Ui.Runtime, Ino, Marketplace boundary checks are all still valid and still reference real, existing projects).

- [ ] **Step 2: Delete `Send_SurfaceDemoRequested_InstallsPack_And_BroadcastsRenderableSurface` from `GatewayServiceTests.cs`**

Delete the entire method (starts at the `[Fact]` immediately above `public async Task Send_SurfaceDemoRequested_InstallsPack_And_BroadcastsRenderableSurface()`, ends at its closing brace — includes the local `static bool IsSurfaceDemoPackCard(RfwCard card)` helper defined at the bottom of the method body). This test exercises `SurfaceDemoRuntime.RequestType`/`GeneratedNeuronKey`/`ObservabilityNeuronKey`, all from the deleted `DigitalBrain.Demo.Runtime` project.

Also delete `using DigitalBrain.Demo.Runtime;` from this file's using list once this is the only thing that needed it (confirm no other remaining method in the file references a `Demo.Runtime` type first).

- [ ] **Step 3: Give `JournalFormatSpikeTests.cs` its own local synapse type instead of the deleted `DemoMessageSynapse`**

This file already defines its own probe neuron (`JournalFormatProbeNeuron`) purely to spike-test Orleans journal (de)serialization — it doesn't need to depend on the Demo project's synapse type at all. Replace the two usages:
```csharp
        await grain.FireAsync(new DemoMessageSynapse("spike-payload"));
```
```csharp
        Assert.Contains(timeline, s => s is DemoMessageSynapse d && d.Text == "spike-payload");
```
```csharp
        Assert.Contains(timelineAfterReactivation, s => s is DemoMessageSynapse d && d.Text == "spike-payload");
```
with a locally-defined replacement type. Add this record right after the `using` block, before `namespace DigitalBrain.Tests.Spikes;`'s first class:
```csharp
[GenerateSerializer]
[Alias("DigitalBrain.Tests.Spikes.SpikePayloadSynapse")]
public sealed record SpikePayloadSynapse(string Text) : Synapse(nameof(SpikePayloadSynapse), DateTimeOffset.UtcNow);
```
(matches the exact attribute pattern every other `Synapse`-derived record in this codebase uses, e.g. `DigitalBrain.Core/Synapse.cs`'s `NeuronTelemetry`/`WiringOptimizationProposed`.)

Then replace all three `DemoMessageSynapse` usages above with `SpikePayloadSynapse`, and update `JournalFormatProbeNeuron`'s handler:
```csharp
public sealed class JournalFormatProbeNeuron(ILogger<JournalFormatProbeNeuron> logger, NeuronJournals journals)
    : Neuron(logger, journals), IJournalFormatProbeNeuron, IHandle<SpikePayloadSynapse>
{
    ...
    public Task HandleAsync(SpikePayloadSynapse synapse) => Task.CompletedTask;
```

- [ ] **Step 4: Build to confirm this task's slice compiles**

Run: `dotnet build tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Debug --nologo 2>&1 | grep -E "error CS"`
Expected: every `Demo`/`DemoMessageSynapse`/`IDemoNeuron`/`SurfaceDemoRuntime` error from the original 35 is gone.

- [ ] **Step 5: Commit**

```bash
git add tests/DigitalBrain.Tests/Architecture/CoreBoundaryTests.cs tests/DigitalBrain.Tests/Gateway/GatewayServiceTests.cs \
  tests/DigitalBrain.Tests/Spikes/JournalFormatSpikeTests.cs
git commit -m "test: delete dead Demo/SurfaceDemo coverage, give journal spike its own synapse type

DigitalBrain.Demo.Contracts/.Runtime were deleted from production code
in a3b6810; this project (orphaned from Brain.slnx since 2026-07-06)
never got the matching test cleanup until now."
```

---

### Task 4: Fix stale/renamed namespace usings

**Files:**
- Modify: `tests/DigitalBrain.Tests/Auth/UserSessionNeuronTests.cs`
- Modify: `tests/DigitalBrain.Tests/Gateway/GatewayServiceTests.cs`
- Modify: `tests/DigitalBrain.Tests/Ino/InoNeuronTabularDataTests.cs`
- Modify: `tests/DigitalBrain.Tests/Kernel/ExperienceStepDispatchTests.cs`
- Modify: `tests/DigitalBrain.Tests/Ui/ChatNeuronTests.cs`
- Modify: `tests/DigitalBrain.Tests/Ui/HomeFeedBusTests.cs`
- Modify: `tests/DigitalBrain.Tests/Ui/HomeFeedCrossSiloTests.cs`
- Modify: `tests/DigitalBrain.Tests/Distribution/BundleManifestTests.cs`
- Modify: `tests/DigitalBrain.Tests/Domains/KitExperienceTests.cs`

**Interfaces:** none — pure `using`-directive fixes, no behavior change. (`UiSurface` lives in `DigitalBrain.Ui.Contracts`, already a project-wide global using; `HomeFeedBus` lives in `DigitalBrain.Kernel.Ui`; `RfwCard` lives in `DigitalBrain.Ui.Contracts.Ui`; `KitExperience`/`UiExperience` live in `DigitalBrain.Pack.Contracts.UiKit`.)

- [ ] **Step 1: Delete the dead `using DigitalBrain.Core.Ui;` line from 7 files**

In each of `UserSessionNeuronTests.cs`, `GatewayServiceTests.cs`, `InoNeuronTabularDataTests.cs`, `ExperienceStepDispatchTests.cs`, `ChatNeuronTests.cs`, `HomeFeedBusTests.cs`, `HomeFeedCrossSiloTests.cs`, delete the line:
```csharp
using DigitalBrain.Core.Ui;
```
Each of these files either already has `using DigitalBrain.Kernel.Ui;` right below it (for `HomeFeedBus`), or doesn't need anything from that namespace beyond what the project's global `DigitalBrain.Ui.Contracts`/`DigitalBrain.Ui.Runtime` usings already supply.

- [ ] **Step 2: Add the `RfwCard` namespace to `ExperienceStepDispatchTests.cs`**

Add, next to its other usings:
```csharp
using DigitalBrain.Ui.Contracts.Ui;
```
(needed because `RfwCard` lives one namespace deeper than the project-wide `DigitalBrain.Ui.Contracts` global using covers.)

- [ ] **Step 3: Fix `InoNeuronTabularDataTests.cs` — delete both dead usings**

Delete both:
```csharp
using DigitalBrain.Core.Ui;
using DigitalBrain.Core.UiKit;
```
(this file needs neither replacement — confirm at build time in Step 5 that nothing else in the file needed them.)

- [ ] **Step 4: Fix `BundleManifestTests.cs` and `KitExperienceTests.cs` — rename `Core.UiKit` to `Pack.Contracts.UiKit`**

In both files, replace:
```csharp
using DigitalBrain.Core.UiKit;
```
with:
```csharp
using DigitalBrain.Pack.Contracts.UiKit;
```
(`KitExperience`/`UiExperience` moved here.)

- [ ] **Step 5: Build to confirm this task's slice compiles**

Run: `dotnet build tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Debug --nologo 2>&1 | grep -E "error CS"`
Expected: every `Core.Ui`/`Core.UiKit`/`KitExperience`/`UiExperience`/`RfwCard` error from the original 35 is gone. Remaining errors should only be the `InoTestHarness` and `Program` ones (Tasks 5-6).

- [ ] **Step 6: Commit**

```bash
git add tests/DigitalBrain.Tests/Auth/UserSessionNeuronTests.cs tests/DigitalBrain.Tests/Gateway/GatewayServiceTests.cs \
  tests/DigitalBrain.Tests/Ino/InoNeuronTabularDataTests.cs tests/DigitalBrain.Tests/Kernel/ExperienceStepDispatchTests.cs \
  tests/DigitalBrain.Tests/Ui/ChatNeuronTests.cs tests/DigitalBrain.Tests/Ui/HomeFeedBusTests.cs \
  tests/DigitalBrain.Tests/Ui/HomeFeedCrossSiloTests.cs tests/DigitalBrain.Tests/Distribution/BundleManifestTests.cs \
  tests/DigitalBrain.Tests/Domains/KitExperienceTests.cs
git commit -m "fix: update stale namespace usings left over from the Ui.Contracts/Pack.Contracts extraction

DigitalBrain.Core.Ui and DigitalBrain.Core.UiKit haven't existed since
those types moved to DigitalBrain.Ui.Contracts and
DigitalBrain.Pack.Contracts.UiKit — this project (orphaned from
Brain.slnx since 2026-07-06) never got the matching using-directive
fixes until now."
```

---

### Task 5: Fix `InoTestHarness` misuse in `GoogleOAuthSteps.cs`

**Files:**
- Modify: `tests/DigitalBrain.Tests/Steps/GoogleOAuthSteps.cs`

**Interfaces:** none — `InoTestHarness` is `public static class` (`tests/DigitalBrain.TestKit/InoTestHarness.cs:14`); this file never obtained a real `IInoNeuron` grain to pass to its `Interact(IInoNeuron, string, ...)` method, so there's no way to wire it up correctly without adding grain-factory access this Reqnroll binding class doesn't have.

- [ ] **Step 1: Remove the broken field and calls, matching this file's own established no-op pattern**

Replace:
```csharp
public class GoogleOAuthSteps
{
    private readonly InoTestHarness _harness = new();

    [Given("the system is running")]
    public void GivenSystemRunning()
    {
        // No-op for harness
    }

    [When(@"INO receives prompt ""(.*)""")]
    public async Task WhenINOReceivesPrompt(string prompt)
    {
        await _harness.InteractAsync(prompt);
    }
```
with:
```csharp
public class GoogleOAuthSteps
{
    [Given("the system is running")]
    public void GivenSystemRunning()
    {
        // No-op for harness
    }

    [When(@"INO receives prompt ""(.*)""")]
    public void WhenINOReceivesPrompt(string prompt)
    {
        // Delegated to unit test coverage — see InoNeuronChatSurfaceTests.
    }
```
and further down, replace:
```csharp
    [When("INO requests gmail messages")]
    public async Task WhenINORequestsGmail()
    {
        await _harness.InteractAsync("last 5 gmail senders");
    }
```
with:
```csharp
    [When("INO requests gmail messages")]
    public void WhenINORequestsGmail()
    {
        // Delegated to unit test coverage — see InoNeuronChatSurfaceTests.
    }
```
(this matches the "delegated to unit test coverage" convention every other step in this exact file already uses — the class was never actually driving a real `IInoNeuron` grain through Reqnroll, so removing the broken call doesn't reduce real coverage.)

- [ ] **Step 2: Build to confirm this file compiles**

Run: `dotnet build tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Debug --nologo 2>&1 | grep -E "error CS"`
Expected: the `InoTestHarness` error from the original 35 is gone. Only the `Program` ambiguity (Task 6) should remain.

- [ ] **Step 3: Commit**

```bash
git add tests/DigitalBrain.Tests/Steps/GoogleOAuthSteps.cs
git commit -m "fix: remove broken InoTestHarness usage in GoogleOAuthSteps

InoTestHarness is a static class exposing Interact(IInoNeuron, ...) —
this file never obtained a grain to pass it and was never actually
exercising Ino through these steps; the removed calls match the
'delegated to unit test coverage' pattern every other step here uses."
```

---

### Task 6: Fix the `Program` type ambiguity in `KernelWebApplicationFactory.cs`

**Files:**
- Modify: `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`
- Possibly modify: `tests/DigitalBrain.Tests/Mcp/DigitalBrainToolsTests.cs`, `Mcp/McpTransportSplitTests.cs`, `Foundry/CodeFoundryApprovalTests.cs` (only if Step 2's build shows they broke)

**Interfaces:**
- Consumes: the existing `Aliases="AppHostProject"` pattern already used for `DigitalBrain.AppHost` in this exact csproj (`DigitalBrain.Tests.csproj:53`) — this task applies the same technique to `DigitalBrain.Mcp`.

`WebApplicationFactory<Program>` in `KernelWebApplicationFactory.cs:12` is ambiguous because both `DigitalBrain.Kernel` and `DigitalBrain.Mcp` use top-level statements in their `Program.cs`, and top-level-statement `Program` classes always live in the global namespace regardless of `RootNamespace` — so both resolve to the same simple name `Program`. `DigitalBrain.Kernel.csproj:58` has an unaliased `ProjectReference` to `DigitalBrain.Mcp.csproj`, which is how Mcp's `Program` reaches this test project's compilation at all (there is no direct reference to Mcp in `DigitalBrain.Tests.csproj` today).

- [ ] **Step 1: Add an aliased direct reference to Mcp**

In `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`, add (near the existing `AppHostProject` alias, same `ItemGroup`):
```xml
    <!-- Kernel transitively references Mcp unaliased (DigitalBrain.Kernel.csproj:58); both projects use
         top-level statements, so both generate a global::Program, colliding in
         KernelWebApplicationFactory's WebApplicationFactory<Program>. Alias Mcp here so only Kernel's
         Program resolves unqualified. -->
    <ProjectReference Include="..\..\src\DigitalBrain.Mcp\DigitalBrain.Mcp.csproj" Aliases="McpProject" />
```

- [ ] **Step 2: Build and check whether the ambiguity is actually resolved this way**

Run: `dotnet build tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Debug --nologo 2>&1 | grep -E "error CS"`

Two possible outcomes:
- **The `CS0433` ambiguity in `KernelWebApplicationFactory.cs` is gone, but `DigitalBrainToolsTests.cs`/`McpTransportSplitTests.cs`/`CodeFoundryApprovalTests.cs` now show `CS0246` for `DigitalBrainMutationTools`/Mcp types.** This is expected — those 3 files used `using DigitalBrain.Mcp;` to reach the now-aliased assembly. Fix each by moving `extern alias McpProject;` to the very first line of the file (before all other `using`s — this is a hard C# requirement) and replacing `using DigitalBrain.Mcp;` with `using McpProject::DigitalBrain.Mcp;`. Rebuild to confirm.
- **The `CS0433` ambiguity persists.** This means the alias on this project's own direct reference doesn't suppress the still-unaliased transitive path through `DigitalBrain.Kernel.csproj:58`. In that case, revert Step 1 and instead add `Aliases="McpProject"` directly to `DigitalBrain.Kernel.csproj:58`'s existing `ProjectReference` to Mcp, then check whether `DigitalBrain.Kernel`'s own source references any Mcp type unaliased (`grep -rn "DigitalBrain.Mcp" src/DigitalBrain.Kernel --include=*.cs`) — if it does, those call sites need the same `extern alias McpProject;` / `using McpProject::DigitalBrain.Mcp;` treatment as above. Rebuild `Brain.slnx` fully afterward (this now touches production code) to confirm no regression outside this test project.

- [ ] **Step 3: Build to confirm zero remaining errors in the whole project**

Run: `dotnet build tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Debug --nologo 2>&1 | tail -20`
Expected: `Build succeeded.` with 0 errors — this was the last of the original 35.

- [ ] **Step 4: Commit**

```bash
git add tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj
# plus whichever of Mcp/DigitalBrainToolsTests.cs, Mcp/McpTransportSplitTests.cs, Foundry/CodeFoundryApprovalTests.cs,
# or src/DigitalBrain.Kernel/DigitalBrain.Kernel.csproj Step 2 required
git commit -m "fix: resolve Program type ambiguity between Kernel and Mcp in test project

Both projects use top-level statements, generating a colliding
global::Program; KernelWebApplicationFactory<Program> needs Kernel's
specifically. Same aliasing technique already used for AppHost in this
csproj (Aliases=\"AppHostProject\")."
```

---

### Task 7: Fix newly-unmasked missing-using errors (Ui.Contracts.Ui / Ui.Contracts / Pack.Contracts)

**Context:** Task 6 fixed the `Program` ambiguity as scoped, but doing so unmasked 86 previously-hidden compile errors across 19 files — the `CS0433` ambiguity was somehow suppressing further diagnostics in this compilation (confirmed empirically by Task 6's implementer: swapping in an unrelated dummy type instead of the real fix produces the identical 86 errors). Of those 86, this task covers the ~49 that are simple missing-`using` errors for types that still exist — same mechanical pattern as Task 4. The remaining ~37 (genuinely deleted `IDemoNeuron`/`DemoMessageSynapse` types used as generic test infrastructure) are Task 8.

**Files (add the stated `using`, if not already present, to each):**
- `tests/DigitalBrain.Tests/Architecture/CoreBoundaryTests.cs` — add `using DigitalBrain.Ui.Contracts.Ui;` (for `RfwCard`, referenced in `Ui_Contracts_Own_Ui_Schema_Types`)
- `tests/DigitalBrain.Tests/Auth/UserSessionNeuronTests.cs` — add `using DigitalBrain.Ui.Contracts.Ui;` (for `RfwCard` and its `ClientId`/`DataJson` members)
- `tests/DigitalBrain.Tests/Gateway/GatewayServiceTests.cs` — add BOTH `using DigitalBrain.Ui.Contracts.Ui;` (for `RfwCard`) AND `using DigitalBrain.Pack.Contracts;` (for `ConfigurationProvided`/`ConfigFormSurface`)
- `tests/DigitalBrain.Tests/Ui/ChatNeuronTests.cs` — add `using DigitalBrain.Ui.Contracts.Ui;` (for `IChatNeuron`, defined in `src/DigitalBrain.Ui.Contracts/Ui/RfwCard.cs:16`)
- `tests/DigitalBrain.Tests/Ui/HomeFeedBusTests.cs` — add `using DigitalBrain.Ui.Contracts.Ui;` (for `RfwCard`)
- `tests/DigitalBrain.Tests/Ui/HomeFeedCrossSiloTests.cs` — add `using DigitalBrain.Ui.Contracts.Ui;` (for `RfwCard`)
- `tests/DigitalBrain.Tests/UiSurfaceContractTests.cs` — add `using DigitalBrain.Ui.Contracts;` (for `UiWidgetTree`, currently referenced as `DigitalBrain.Core.UiWidgetTree` — also fix those fully-qualified references to just `UiWidgetTree` once the using is added, or to `DigitalBrain.Ui.Contracts.UiWidgetTree` if you prefer leaving them qualified; either compiles, pick whichever matches this file's existing style for other types)
- `tests/DigitalBrain.Tests/Authoring/StarterBundleTests.cs` — add `using DigitalBrain.Ui.Contracts;` (for `UiKitVocabulary`, currently referenced as `DigitalBrain.Core.UiKitVocabulary` — same fully-qualified-reference note as above)
- `tests/DigitalBrain.Tests/Distribution/PackConfigManifestTests.cs` — add `using DigitalBrain.Pack.Contracts;` (for `ConfigurationProvided`)
- `tests/DigitalBrain.Tests/Gateway/PackConfigPullTests.cs` — add `using DigitalBrain.Pack.Contracts;` (for `ConfigurationProvided`, 4 sites)
- `tests/DigitalBrain.Tests/Ino/InoNeuronChatSurfaceTests.cs` — add `using DigitalBrain.Pack.Contracts;` (for `ConfigFormSurface`, 3 sites)
- `tests/DigitalBrain.Tests/Steps/ConfigFormSteps.cs` — add `using DigitalBrain.Pack.Contracts;` (for `ConfigFormSurface` and `ConfigurationProvided`)
- `tests/DigitalBrain.Tests/Steps/TelegramReactiveLoopSteps.cs` — add `using DigitalBrain.Pack.Contracts;` (for `ConfigFormSurface` and `ConfigurationProvided`)

**Interfaces:** none — pure additive `using`-directive fixes. `RfwCard`/`IChatNeuron` are defined in `src/DigitalBrain.Ui.Contracts/Ui/RfwCard.cs` (namespace `DigitalBrain.Ui.Contracts.Ui`). `UiWidgetTree`/`UiKitVocabulary` are defined in `src/DigitalBrain.Ui.Contracts/UiSurfaces.cs` and siblings (namespace `DigitalBrain.Ui.Contracts`). `ConfigurationProvided`/`ConfigFormSurface` are defined in `src/DigitalBrain.Pack.Contracts/Configuration.cs` (namespace `DigitalBrain.Pack.Contracts`).

- [ ] **Step 1: For each file above, check its current `using` block and add only what's missing**

For each file, run `head -20 <file>` (or read it) to see its current usings, then add the stated `using` line(s) in the existing block (alphabetical-ish placement matching the file's own style; exact position doesn't matter to the compiler). Do not add a duplicate if a file somehow already has one of these usings partially.

- [ ] **Step 2: Build to confirm all these errors are gone**

Run: `dotnet build tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Debug --nologo --no-incremental 2>&1 | grep -E "error CS" | sort -u`
Expected: zero `RfwCard`/`IChatNeuron`/`UiWidgetTree`/`UiKitVocabulary`/`ConfigurationProvided`/`ConfigFormSurface` errors remain. Only `IDemoNeuron`/`DemoMessageSynapse` errors (Task 8's scope, in `Kernel/NeuronTests.cs`, `Kernel/NeuronBroadcastTests.cs`, `Kernel/CheckpointSecurityTests.cs`, `Specs/PackSpecDriver.cs`, `Steps/NeuronSteps.cs`, `Steps/PackSpecSteps.cs`) should remain.

- [ ] **Step 3: Commit**

```bash
git add tests/DigitalBrain.Tests/Architecture/CoreBoundaryTests.cs tests/DigitalBrain.Tests/Auth/UserSessionNeuronTests.cs \
  tests/DigitalBrain.Tests/Gateway/GatewayServiceTests.cs tests/DigitalBrain.Tests/Ui/ChatNeuronTests.cs \
  tests/DigitalBrain.Tests/Ui/HomeFeedBusTests.cs tests/DigitalBrain.Tests/Ui/HomeFeedCrossSiloTests.cs \
  tests/DigitalBrain.Tests/UiSurfaceContractTests.cs tests/DigitalBrain.Tests/Authoring/StarterBundleTests.cs \
  tests/DigitalBrain.Tests/Distribution/PackConfigManifestTests.cs tests/DigitalBrain.Tests/Gateway/PackConfigPullTests.cs \
  tests/DigitalBrain.Tests/Ino/InoNeuronChatSurfaceTests.cs tests/DigitalBrain.Tests/Steps/ConfigFormSteps.cs \
  tests/DigitalBrain.Tests/Steps/TelegramReactiveLoopSteps.cs
git commit -m "fix: add missing usings unmasked by the Program ambiguity fix (RfwCard/UiWidgetTree/ConfigurationProvided etc.)

Task 6's Program-ambiguity fix revealed 86 previously-hidden compile
errors in this project — a CS0433 in one file was somehow suppressing
further diagnostics across the whole compilation. Of those, these 13
files just needed a missing using directive for a type that still
exists (Ui.Contracts.Ui, Ui.Contracts, or Pack.Contracts); the
IDemoNeuron/DemoMessageSynapse cluster is handled separately (next task)
since those types are genuinely gone."
```

---

### Task 8: Replace deleted `IDemoNeuron`/`DemoMessageSynapse` test-double with a shared TestKit type

**Context:** `IDemoNeuron` and `DemoMessageSynapse` were defined by the deleted `DigitalBrain.Demo.Contracts`/`DigitalBrain.Demo.Runtime` projects and used across 6 files as a convenient minimal "any simple neuron" test double for exercising generic Neuron/checkpoint/pack-installation infrastructure — these files are NOT testing the Demo feature itself (unlike Tasks 2/3's targets), so deletion is wrong here; a replacement is needed. Neither type exists anywhere in the current tree (confirmed by repo-wide grep).

**Files:**
- Create: a new shared test-double neuron + synapse type in `tests/DigitalBrain.TestKit/` (exact file name/location per the investigation below)
- Modify: `tests/DigitalBrain.Tests/Kernel/NeuronTests.cs`, `Kernel/NeuronBroadcastTests.cs`, `Kernel/CheckpointSecurityTests.cs`, `Specs/PackSpecDriver.cs`, `Steps/NeuronSteps.cs`, `Steps/PackSpecSteps.cs` — replace `IDemoNeuron`/`DemoMessageSynapse` references with the new type(s)

**Before writing any code:** read the investigation findings at `.superpowers/sdd/task8-investigation.md` (written by the controller before this task was dispatched) — it documents, per file, exactly which capabilities (`Grain<T>(id)` resolution, `FireAsync`, `GetOutgoingTimelineAsync`/`GetTimelineAsync`, `BranchAsync`, checkpoint create/restore, broadcast) each of the 6 files actually needs from the test double, and whether any already-compiling type in this test suite (e.g. `IGeneratedNeuron`) could be reused instead of introducing a new one. Follow its recommendation for where the new type should live and what it needs to implement — do not re-derive this from scratch.

- [ ] **Step 1: Implement the replacement type(s) per the investigation's recommendation**

(Exact shape depends on the investigation findings — implement whatever grain interface + concrete `Neuron`-derived class + `Synapse`-derived record combination it specifies, following this codebase's established patterns: `[GenerateSerializer]`/`[Alias(...)]` on the synapse record matching `src/DigitalBrain.Core/Synapse.cs`'s existing examples, `[GrainType("...")]` on the neuron class matching other test/production neuron classes' conventions.)

- [ ] **Step 2: Update all 6 files to use the new type(s) instead of `IDemoNeuron`/`DemoMessageSynapse`**

Replace every `Grain<IDemoNeuron>(id)` → `Grain<TNewInterface>(id)` and every `new DemoMessageSynapse(text)` → `new TNewSynapseType(text)` (or whatever constructor shape the new type has), preserving each test's actual assertions and behavior — this is a mechanical rename once Step 1's type exists with an equivalent shape, not a rewrite of test logic.

- [ ] **Step 3: Build to confirm zero remaining errors in the whole project**

Run: `dotnet build tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Debug --nologo --no-incremental 2>&1 | tail -10`
Expected: `Build succeeded.` with 0 errors — this closes out all 121 errors found across this repair series (the original 35 plus the 86 Task 6 unmasked).

- [ ] **Step 4: Run this project's fast test lane to confirm the renamed tests still pass at runtime, not just compile**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName!~E2E" --nologo`
Expected: all tests pass, including every test in the 6 modified files — a passing compile doesn't prove the new test double actually behaves equivalently to the old one (e.g. supports branching/checkpoint restore correctly), so this run is the real proof.

- [ ] **Step 5: Commit**

```bash
git add tests/DigitalBrain.TestKit/ tests/DigitalBrain.Tests/Kernel/NeuronTests.cs tests/DigitalBrain.Tests/Kernel/NeuronBroadcastTests.cs \
  tests/DigitalBrain.Tests/Kernel/CheckpointSecurityTests.cs tests/DigitalBrain.Tests/Specs/PackSpecDriver.cs \
  tests/DigitalBrain.Tests/Steps/NeuronSteps.cs tests/DigitalBrain.Tests/Steps/PackSpecSteps.cs
git commit -m "test: replace deleted IDemoNeuron/DemoMessageSynapse with a shared TestKit test double

These 6 files used the Demo project's minimal neuron/synapse as generic
test infrastructure, not to test the Demo feature itself — its deletion
broke them as collateral damage. Reintroduces an equivalent, TestKit-owned
test double so this generic Neuron/checkpoint/pack-installation coverage
is restored rather than deleted."
```

---

### Task 9: Fix all 8 pre-existing test failures found by Task 8's test run

**Context:** Task 8's test run (the first real execution of this project's tests since 2026-07-06) surfaced 8 failures, all confirmed pre-existing and unrelated to the Demo→Probe rename. Deep investigation (with empirical repros against the actual built assemblies) found precise root causes and decisive fixes for all 8. Two of the fixes are shared across two failures.

**Files:**
- Modify: `src/DigitalBrain.Kernel/Foundry/CapabilityGate.cs` (shared fix, unblocks failures #1 and #4 below)
- Modify: `tests/DigitalBrain.Tests/Kernel/NeuronTests.cs` (failure #1)
- Modify: `tests/DigitalBrain.Tests/E2E/Packs/TravelPack.cs` (failure #4)
- Modify: `tests/DigitalBrain.Tests/Foundry/PackAlcEmbodierTests.cs` (failure #2)
- Modify: `tests/DigitalBrain.Tests/Authoring/StarterBundleSource.cs` (failure #3)
- Modify: `src/DigitalBrain.Ui.Runtime/UiSurfaceRuntime.cs`, `tests/DigitalBrain.Tests/UiSurfaceContractTests.cs` (failure #5)
- Modify: `tests/DigitalBrain.Tests/Architecture/CoreBoundaryTests.cs` (failure #6)
- Modify: `src/DigitalBrain.Core/Synapse.cs`, `src/DigitalBrain.Core/DigitalBrain.Core.csproj`, `src/DigitalBrain.Kernel.Abstractions/` (failure #7)
- Delete: `tests/DigitalBrain.Tests/Features/XBitcoinTelegramDemo.feature`, `tests/DigitalBrain.Tests/Features/XBitcoinTelegramDemo.feature.cs` (failure #8)

**Interfaces:** `CancelTask` (an existing, real `Synapse`-derived record, `src/DigitalBrain.Core/Synapse.cs:249`) is consumed by failure #5's fix. `IScopedChatClientFactory` (failure #7) is produced in its new home, `DigitalBrain.Kernel.Abstractions`, consumed unchanged by its existing callers (`LlmResponderNeuron.cs`, `InoNeuron.cs`, TestKit/Tests doubles) since they already reference that project.

- [ ] **Step 1: Fix failures #1 (`NeuronTests.Installed_Pack_Handles_Typed_Synapse_And_Emits_Journaled_UiSurface`) and #4 (`ExperienceStepDispatchTests.ExperienceStep_start_emits_intro_surface_to_home_feed`) — shared root cause**

Both fail because `PackAlcEmbodier` compiles pack source standalone (no `DigitalBrain.*` implicit usings), and (a) two embedded/extracted pack sources still reference `UiSurface`-family types as `DigitalBrain.Core.*` instead of their real home `DigitalBrain.Ui.Contracts`, and (b) even once that's fixed, `CapabilityGate` rejects the `DigitalBrain.Ui.Contracts.` namespace outright. Both parts must be fixed together.

In `src/DigitalBrain.Kernel/Foundry/CapabilityGate.cs`, find `AllowedNamespacePrefixes` (around lines 14-18) and add `"DigitalBrain.Ui.Contracts."` to the array — confirmed safe: that namespace contains only DTOs/interfaces (`UiSurface`, `UiWidgetTree`, `RfwCard`, etc.), no I/O/reflection/process capability, and its own `.csproj` references only `DigitalBrain.Core` + Orleans abstractions — the identical dependency footprint as the already-allowed `DigitalBrain.Core.`. Also fix the file's header comment (around line 11) which currently points at `docs/specs/2026-07-02-capability-gate-hardening-followup.md` — that file was deleted in commit `6dfc0a7`; either remove the dangling reference or note the doc no longer exists.

In `tests/DigitalBrain.Tests/Kernel/NeuronTests.cs` (around lines 537-548), change the embedded pack source's fully-qualified references from `DigitalBrain.Core.UiSurfaceKeys`/`UiSurfaceLayouts`/`UiSurfaceKinds`/`UiSurface` to `DigitalBrain.Ui.Contracts.UiSurfaceKeys`/`UiSurfaceLayouts`/`UiSurfaceKinds`/`UiSurface` (or add a `using DigitalBrain.Ui.Contracts;` inside the raw-string pack source and drop the qualification — either compiles).

In `tests/DigitalBrain.Tests/E2E/Packs/TravelPack.cs` (lines 1-9), add `using DigitalBrain.Ui.Contracts;` next to the existing `using DigitalBrain.Core;` / `using DigitalBrain.Core.Distribution;` — this file's `UiSurface`-returning `TravelCards.*` methods only compile today because the *outer test project* has a global using for `DigitalBrain.Ui.Contracts`; `TravelPackSource.Read()` extracts this file's raw text and feeds it to the same standalone `PackAlcEmbodier` compilation, which has no such global using.

- [ ] **Step 2: Fix failure #2 (`PackAlcEmbodierTests.Embodies_Typed_Synapse_Handler`)**

In `tests/DigitalBrain.Tests/Foundry/PackAlcEmbodierTests.cs`, find the embedded pack source referencing `sig.Payload` and change it to `sig.Props` — `Signal` (`src/DigitalBrain.Core/Signals.cs:47`) has no `Payload` member, only `Props` (`IReadOnlyDictionary<string, object?>`).

- [ ] **Step 3: Fix failure #3 (`StarterBundleTests.Starter_ask_hop_renders_input_and_button`)**

In `tests/DigitalBrain.Tests/Authoring/StarterBundleSource.cs`, change `using DigitalBrain.Core.UiKit;` to `using DigitalBrain.Pack.Contracts.UiKit;` — `KitExperience`/`UiExperience` live there (`src/DigitalBrain.Pack.Contracts/UiKit/KitExperience.cs:4`), matching the fix already applied elsewhere in this repair series (Task 4).

- [ ] **Step 4: Fix failure #5 (`UiSurfaceContractTests.Action_Descriptors_Point_To_Existing_Synapse_Types`)**

Neither `"DemoMessageSynapse"` nor `"TestMessageSynapse"` refers to a real type — both are stale string literals from before/around the Demo cleanup. `CancelTask` (`src/DigitalBrain.Core/Synapse.cs:249`) is the real, currently-used type for this exact "cancel task" action elsewhere in the same runtime file (`src/DigitalBrain.Ui.Runtime/UiSurfaceRuntime.cs:665`, `nameof(CancelTask)` inside `TaskManagerFromTasks`).

In `src/DigitalBrain.Ui.Runtime/UiSurfaceRuntime.cs`: delete the `DemoMessageSynapseName = "DemoMessageSynapse"` constant (around line 8), and replace its two use sites — `UiSurfaceSamples.TaskWindow()`'s `"cancel-task"` action (around line 69) and `UiSurfaceSamples.UserInput()`'s `"dismiss-input"` action (around line 104) — with `nameof(CancelTask)`. (The unrelated uses in `Timeline()` around lines 292/299 are a generic message-type placeholder, not an action descriptor asserted by this test — leave those untouched.)

In `tests/DigitalBrain.Tests/UiSurfaceContractTests.cs:62`, change `"TestMessageSynapse"` to `nameof(CancelTask)`, matching every sibling assertion in this same test method's style (`nameof(InoRequest)`, `nameof(LoginRequest)`, `nameof(InstallFromMarketplace)`).

- [ ] **Step 5: Fix failure #6 (`CoreBoundaryTests.Ino_Integration_Owns_Assistant_Reasoning_Not_Kernel_Orleans`)**

This test's blanket "forbid all `DigitalBrain.*`/`Orleans`/`Microsoft.Orleans`" assertion was already wrong when written (Ino legitimately needs `DigitalBrain.Kernel.Abstractions`, which itself needs Orleans grain/journal abstractions, to host itself as a real grain) and predates the deliberate architecture change in commit `4ee79a4` that made Ino a full peer integration (with `Core`/`Google`/`Salesforce`/`Ui.Contracts`/`Ui.Runtime`/`Marketplace.Contracts` references, matching `DigitalBrain.Google`'s identical, untested-for shape).

In `tests/DigitalBrain.Tests/Architecture/CoreBoundaryTests.cs`, rewrite `Ino_Integration_Owns_Assistant_Reasoning_Not_Kernel_Orleans` (around lines 137-153) to match this same file's own established idiom used by every other `X_Depends_On_Y_Not_Z` test: assert an explicit allowlist of expected `DigitalBrain.*` reference names (`Core`, `Kernel.Abstractions`, `Pack.Contracts`, `Ui.Contracts`, `Ui.Runtime`, `Marketplace.Contracts`, `Google`, `Salesforce`) and flag only *unexpected* `DigitalBrain.*` names (this is what would catch a real violation, e.g. an accidental `DigitalBrain.Kernel`/`DigitalBrain.Mcp`/`DigitalBrain.AppHost` reference). Narrow the Orleans ban from a blanket prefix match to specifically the hosting/server packages (exact-match `Microsoft.Orleans.Server`/similar), not the grain-abstraction packages (`Orleans.Core.Abstractions`, `Orleans.Journaling`, `Orleans.Serialization*`) every `DigitalBrain.*` project legitimately needs. Keep the existing `Microsoft.AspNetCore`/`Microsoft.Extensions.Hosting` bans (still genuinely satisfied and meaningful).

- [ ] **Step 6: Fix failure #7 (`CoreBoundaryTests.Core_Does_Not_Reference_Runtime_Host_Or_Integration_Packages`)**

This IS a genuine regression from commit `4ee79a4`, which added `IScopedChatClientFactory` directly to `src/DigitalBrain.Core/Synapse.cs` (around lines 570-573) and a `Microsoft.Extensions.AI` package reference to `DigitalBrain.Core.csproj:18`, violating Core's pre-existing, deliberate "no runtime/host/integration packages" boundary (this exact `Microsoft.Extensions.AI` ban already existed in the forbidden-prefix list before that commit).

Move `IScopedChatClientFactory` from `src/DigitalBrain.Core/Synapse.cs` to a new file in `src/DigitalBrain.Kernel.Abstractions/` (e.g. `IScopedChatClientFactory.cs`), keeping its exact shape:
```csharp
namespace DigitalBrain.Kernel.Abstractions;

public interface IScopedChatClientFactory
{
    Microsoft.Extensions.AI.IChatClient? Create(string provider, string? apiKey);
}
```
(check the exact current namespace convention used by sibling files in `DigitalBrain.Kernel.Abstractions` and match it.) Move the `<PackageReference Include="Microsoft.Extensions.AI" />` line from `src/DigitalBrain.Core/DigitalBrain.Core.csproj` to `src/DigitalBrain.Kernel.Abstractions/DigitalBrain.Kernel.Abstractions.csproj`. This still satisfies the original goal of that commit (letting peer integrations reference this interface without depending on the full `DigitalBrain.Kernel` runtime/host project) since Ino/Google/Salesforce already reference `DigitalBrain.Kernel.Abstractions` directly. Update any consumers (`LlmResponderNeuron.cs`, `InoNeuron.cs`, test doubles in `TestKit`/`Tests`) that reference `IScopedChatClientFactory` via a `using DigitalBrain.Core;` to instead (or also) `using DigitalBrain.Kernel.Abstractions;` — grep for `IScopedChatClientFactory` first to find every call site before editing.

- [ ] **Step 7: Fix failure #8 (Reqnroll scenario `"X post from watched author triggers a Bitcoin price alert on Telegram"`)**

This scenario tests a permanently-deleted feature (the XBitcoin/Telegram demo pack, whose step bindings were already correctly deleted in this repair series' Task 2) — the `.feature`/`.feature.cs` pair was simply missed in that cleanup. Delete both:
```bash
git rm tests/DigitalBrain.Tests/Features/XBitcoinTelegramDemo.feature
git rm tests/DigitalBrain.Tests/Features/XBitcoinTelegramDemo.feature.cs
```
No `.csproj` changes needed (Reqnroll/SDK glob-includes these implicitly, same as Task 2's step-file deletion needed none).

- [ ] **Step 8: Build and run the full test suite to confirm all 8 failures are resolved**

Run: `dotnet build tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Debug --nologo --no-incremental`
Expected: `Build succeeded.` with 0 errors.

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj --filter "FullyQualifiedName!~E2E" --nologo`
Expected: `Passed: 400, Skipped: 2, Total: 402` (0 failures — all 8 fixed, none of Tasks 1-9's other 392 previously-passing tests regressed).

Also run the full solution build once, since Step 6 touches production code across 3 projects (`Core`, `Kernel.Abstractions`, and any consumer files):
Run: `dotnet build Brain.slnx -c Release --nologo` (before this project is re-added to the solution in Task 10 — this confirms the `IScopedChatClientFactory` move doesn't break any *other* already-in-solution project that referenced it)
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 9: Commit**

Consider splitting into 2 commits matching this repo's "commit CI/production-code changes separately from test-suite changes" convention observed elsewhere in this series: one for the `CapabilityGate`/`IScopedChatClientFactory`/`UiSurfaceRuntime.cs` production-code changes (Steps 1's CapabilityGate part, 5's UiSurfaceRuntime part, 6, 7), one for the test-file-only changes (Steps 1's NeuronTests/TravelPack part, 2, 3, 5's UiSurfaceContractTests part, 8). If that split feels artificial given how intertwined the fixes are, one combined commit is acceptable — use judgment, but explain the choice in the commit message(s).

```bash
git commit -m "fix: resolve 8 pre-existing test failures surfaced by restoring DigitalBrain.Tests

<details of which failures, referencing this task's investigation>

These predate this repair series and were undetectable while the
project was orphaned from Brain.slnx (since 2026-07-06) and later
uncompilable (121 accumulated errors, Tasks 1-8). Root causes: stale
DigitalBrain.Core.* qualifications for types that moved to
DigitalBrain.Ui.Contracts (in embedded pack sources compiled standalone
by PackAlcEmbodier, which has no DigitalBrain.* implicit usings);
CapabilityGate's namespace allowlist never having been extended to
Ui.Contracts even though packs legitimately need to emit UiSurface;
two stale string-literal/type mismatches (Signal.Payload, DemoMessageSynapse/
TestMessageSynapse placeholders); a Core boundary regression from an
earlier commit that moved an LLM-client-factory interface into Core
along with a Microsoft.Extensions.AI package reference; an
architecture-guard test that was already wrong when written, predating
a deliberate Ino-as-peer-integration redesign; and one leftover dead
Reqnroll scenario for the already-deleted XBitcoin/Telegram demo pack."
```

---

### Task 10: Re-add the project to `Brain.slnx` and run full verification

**Files:**
- Modify: `Brain.slnx`

**Interfaces:** none.

- [ ] **Step 1: Add the project back to the `/tests/` folder**

In `Brain.slnx`, inside `<Folder Name="/tests/">`, add:
```xml
    <Project Path="tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj" />
```
(anywhere among the existing sibling `<Project>` entries in that folder — order doesn't matter to MSBuild, but alphabetical matches this file's existing style.)

- [ ] **Step 2: Full solution build**

Run: `dotnet build Brain.slnx -c Release --nologo`
Expected: `Build succeeded.` with 0 errors. (First time in this repo's history that `Brain.slnx` has included this project since commit `5eb7448` on 2026-07-06 — if new, unrelated errors surface here that Tasks 1-8 didn't catch building the project standalone, e.g. from a different `Configuration`/`Release` symbol path, treat them as new findings and fix before proceeding — do not skip via `--no-restore`/config flags.)

- [ ] **Step 3: Fast-loop test run (matches what CI does)**

Run: `dotnet test Brain.slnx -c Release --filter "FullyQualifiedName!~E2E" --nologo`
Expected: all tests pass. This is the first time this project's ~30 subfolders of tests (Architecture, Auth, Gateway, Ino, Kernel, Ui, etc.) have run as part of an aggregate solution test command since 2026-07-06 — expect this to take noticeably longer than before and possibly surface a handful of additional failures (as opposed to compile errors) that only show up at runtime; triage and fix each before declaring this task done, per this repo owner's "make sure tests are passing" standing instruction.

- [ ] **Step 4: Confirm CI's exact command also passes**

Run: `dotnet test Brain.slnx --no-restore` (matches `.github/workflows/ci.yml`'s test step exactly, after its preceding `dotnet restore Brain.slnx` / `dotnet build Brain.slnx --no-restore` steps — run those two first if this is a fresh checkout)
Expected: all tests pass, including any `[Trait("Category", "cluster")]`-tagged tests (`JournalFormatSpikeTests`, `SelfEvolutionDurabilityTests`) — CI has no filter, so these now run too; if they're slow/flaky under CI's runner, that's a new, separate finding to raise, not something to silently filter out here.

- [ ] **Step 5: Commit**

```bash
git add Brain.slnx
git commit -m "build: re-add DigitalBrain.Tests to Brain.slnx

Removed in 5eb7448 (2026-07-06, 'Fix fast solution test loop') alongside
DigitalBrain.AppHost. AppHost was restored later (ef2d4ff); this project
never was, so CI's 'dotnet test Brain.slnx' has been silently skipping
Kernel/Gateway/Architecture/SelfEvolution/E2E coverage since. Tasks 1-8
in this series repaired 121 compile errors accumulated in the interim
(35 visible initially, plus 86 unmasked once Task 6's Program-ambiguity
fix stopped suppressing further diagnostics)."
```

---

### Task 11: Update the stale E2E section of `docs/SYSTEM_DESIGN.md`

**Files:**
- Modify: `docs/SYSTEM_DESIGN.md:424-428`

**Interfaces:** none — documentation only.

- [ ] **Step 1: Read the current section and replace it**

Read `docs/SYSTEM_DESIGN.md` around lines 424-428 first to get the exact surrounding text (this plan doesn't reproduce it verbatim since it may have shifted slightly from other concurrent edits — search for `RUN_FLUTTER_E2E` or `FAST_UI_E2E` to locate it precisely). Replace any mention of `RUN_FLUTTER_E2E`/`FAST_UI_E2E` and "real browser rendering" with a description matching current reality:
- The opt-in env var is `RUN_REAL_STACK_E2E` (set via `e2e.runsettings` for VS Test Explorer, or directly for CLI runs), gated by `E2EPrerequisites.OptedIn`/`RequireRealStackE2E()` in `tests/DigitalBrain.Tests/E2E/E2EPrerequisites.cs`.
- E2E tests assert over the real Aspire-hosted stack via real gRPC (`DigitalBrainAppHostFixture`, `WatchHomeFeed`/`Send`), not a browser — Playwright was removed entirely (see `docs/superpowers/specs/2026-07-05-e2e-testing-without-playwright-design.md`).
- Booting the fixture cold (no already-running dev cluster) additionally requires `-p:EnableAppHostTests=true` (`DigitalBrain.Tests.csproj:10`) so the `DigitalBrain.AppHost` project reference is included — `e2e.runsettings` cannot set this itself (it's an MSBuild property, not a runtime env var), so document it as a companion flag next to `RUN_REAL_STACK_E2E`.
- CI does not run E2E tests (no `RUN_REAL_STACK_E2E` set in `.github/workflows/ci.yml`) — they're opt-in for local/manual verification only.

- [ ] **Step 2: Commit**

```bash
git add docs/SYSTEM_DESIGN.md
git commit -m "docs: fix stale E2E section in SYSTEM_DESIGN.md

Still described RUN_FLUTTER_E2E/FAST_UI_E2E and real browser rendering,
both removed in the 2026-07-05 Playwright-removal session. Also
documents the EnableAppHostTests MSBuild flag e2e.runsettings can't set
on its own."
```

---

## Self-Review

**Spec coverage:** every one of the original 35 compile errors from the `dotnet build tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj` run has a task above that names its exact file and fix (Market: Task 2; Demo: Task 3; namespace usings: Task 4; InoTestHarness: Task 5; Program ambiguity: Task 6). Task 6's fix unmasked 86 further errors across 19 files, hidden until then by the `Program` ambiguity itself (confirmed independent of the specific fix via a controlled experiment) — Task 7 covers the ~49 that are simple missing-`using` errors for types that still exist, and Task 8 covers the ~37 that need a real replacement for the deleted `IDemoNeuron`/`DemoMessageSynapse` test-double. Re-solution-wiring (the actual top-priority finding) is Task 9. The one production-code loose end the Market removal left behind (stale `market-data-main` id) is Task 1, sequenced first since Tasks 2/7 depend on the corrected count. The e2e.runsettings documentation follow-up from the original analysis is Task 10, sequenced last since it only makes sense once Task 9 lands.

**Not in this plan (separate, lower-risk follow-up):** `AGENTS.md` restore/purge decision, `.mcp.json`'s stale project path, archiving the five superseded 2026-07-06 trash-analysis docs, refreshing `README.md`, `docs/PRODUCT_VISION.md`'s name/content mismatch, and `docs/demo-sample/`/`deploy/DEPLOY-STATUS.md` cleanup — these are docs/config-only, independent of the test repair, and were intentionally left out per this skill's scope-check guidance (multiple independent subsystems → separate plans). Happy to write a second, much shorter plan for these on request.
