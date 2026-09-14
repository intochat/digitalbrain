# Phase 2: slots — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Two kernel slots behind a gateway, so the coding agent can rebuild the kernel it runs in: an
active-slot lease fences the standby, a YARP gateway on 5080 keeps one address for the shell and every MCP
client, a `slot` neuron builds the standby and promotes it as a durable follow-up, `code_promote` drives the
whole landing from the chat, and the shell survives the swap with at most one reconnect. A deliberately
broken build leaves the live slot untouched with the errors in the card.

**Architecture:** `IActiveSlotLease` (kernel) names the one silo that may react; `SingleSlotLease` keeps
phase 0/1 hosts unchanged, `AzureTableActiveSlotLease` (compare-and-swap on the `DigitalBrainLeases` row)
is the real one, `InMemoryActiveSlotLease` is the one facts drive by hand. The fence is three checks:
`NeuronActivationGuardFilter` refuses every non-`[ReadOnly]` neuron call on a standby with
`StandbySlotException`, `Neuron.Drain` returns before touching pending work, and `Neuron.ReceiveReminder`
returns before the wake. `DigitalBrain.Gateway` owns one `InMemoryConfigProvider` with two clusters and one
catch-all route whose `ClusterId` a `POST /switch/{slot}` rebuilds. The `slot` neuron (`Neuron<SlotState>`)
turns `build` and `promote` into reactions: `ISlotBuilder` shells out `dotnet build -p:ArtifactsPath`,
`ISlotEndpoints` does the health and smoke reads and the gateway switch, `IAspireResourceCommands` starts and
stops the slot resources through the Microsoft module's `AspireConnection`. Commands validate and schedule;
the flip happens in a reaction, never inside a model turn.

**Tech Stack:** .NET 11 rc1, Orleans 10.3.1 neurons, Aspire 13.5.3 hosting, new: `Yarp.ReverseProxy` 2.3.0
and a direct `Azure.Data.Tables` 12.12.0 reference, Microsoft.Extensions.AI 10.9.0, xunit.v3, Flutter
(core/ui/shell), git and `dotnet` CLIs.

**Spec:** `docs/coding/coding-agent-design.md` section 9.2, with 4.4 (amended 2026-09-14), 4.8, 4.9, D2, D3
and research R5. Binding rulings: `.superpowers/sdd/phase2-spikes/plan-rulings.md`. Seams:
`.superpowers/sdd/phase2-spikes/seams-report.md`. Spike results: `S1-S2-results.md`, `S3-results.md`,
`S4-results.md` and `docs/coding/NOTES.md` section "Phase 2 spikes".

## Global Constraints

- Branch `feature/coding-phase2-slots` from `feature/coding-phase1-changesets` at `9fd09c46` (which came
  from `feature/coding-phase0-workspace`, which came from `master`); HEAD of this branch is `c9611543`.
  Commits prefixed `coding:`; one PR, stacked on the phase 1 PR — its base branch is
  `feature/coding-phase1-changesets`, so it must not be retargeted at `master` until phase 1 merges.
- `TreatWarningsAsErrors=true`, `AnalysisLevel=preview-all`, `EnforceCodeStyleInBuild=true`: every build
  warning-free. Services use `.ConfigureAwait(false)`; grain code uses `.ConfigureAwait(true)`.
- No `/// <summary>` comments; a short inline comment only where the reason is not visible in the code.
  Names carry the meaning.
- Every contract DTO: `[GenerateSerializer]`, `[Alias("coding.<kebab-name>")]` for the Coding module and
  `[Alias("db.v3.<kebab-name>")]` for kernel contracts, `[property: Id(n)]` contiguous from 0 and never
  reused, and an entry in `CodingJson`. Every neuron method: `[Alias]`; queries `[ReadOnly]` with at most one
  DTO plus an optional trailing `CancellationToken`; mutators take exactly one DTO deriving from `Command`.
- Design section 2: a neuron never awaits another neuron's answer inside a reaction (calling a singleton
  service such as `ISlotBuilder` or `ISlotEndpoints` inside a reaction is allowed); a reaction `Schedule`s
  before it `SaveAsync`es, saves once, and bumps `Revision`; commands validate and schedule, reactions work;
  every list result is `{items, totalCount, truncated}`; a tool never throws at the model, it returns
  `{ advice }`.
- **The fence.** A standby silo answers `[ReadOnly]` reads and nothing else. A standby refusal is transient
  and is **never journaled**: it is thrown by the incoming grain-call filter before the command body runs, so
  no `CommandRejection` reaches the dedup table and the same `CommandId` still works after promotion.
- **`ArtifactsPath` is always absolute.** MSBuild resolves a relative `ArtifactsPath` per project, which
  scatters outputs under every project folder where the generated sources poison later builds (phase 1 live
  run). Slot outputs live at `<repo>/artifacts/slot-a` and `<repo>/artifacts/slot-b`, and a slot build passes
  `-p:ArtifactsPath=<absolute>` **and** `-p:UseArtifactsOutput=true` — without the second the SDK keeps the
  old `bin`/`obj` layout and the standby's dll is not where the AppHost looks (spike S1).
- **Never build a slot into the output a running slot is loaded from** (R5.4): `kernel-a` runs from the
  project's own output, `kernel-b` from `artifacts/slot-b`, and the live slot refuses to build itself.
- Ports: gateway **5080** (the only address the shell, `.mcp.json` and every client use), `kernel-a`
  **5081**, `kernel-b` **5082**, all unproxied (`isProxied: false`), because a proxied endpoint's
  `GetEndpoint()` resolves to the proxy's own port and the process then collides with its own front door
  (spike S1).
- Packages: `Yarp.ReverseProxy` **2.3.0** (latest stable on nuget.org, verified 2026-09-14 through Context7
  `/dotnet/yarp` and `https://api.nuget.org/v3-flatcontainer/yarp.reverseproxy/index.json`);
  `Azure.Data.Tables` **12.12.0** (latest stable on nuget.org, verified the same way; the repo resolves
  12.11.0 transitively today through `Aspire.Azure.Data.Tables` 13.5.3 and
  `Microsoft.Orleans.Clustering.AzureStorage` 10.3.1, and transitive pinning lifts the whole graph to the
  pinned version — Task 3 carries the fallback if that restore conflicts). Both go in
  `Directory.Packages.props`. `DisableMSBuildAssemblyCopyCheck` is unaffected: the gateway references no
  Roslyn.
- YARP asks for at most one configuration update per 15 s (it rebuilds the whole route table), so the
  gateway debounces a switch through `DigitalBrain:Gateway:MinSwitchInterval` (default `00:00:15`); facts set
  it to zero.
- Dart changes mean the Flutter gate runs in this phase, from `src/Modules/UI/Flutter`:
  `dart format --output=none --set-exit-if-changed core ui shell/lib shell/test`, `flutter analyze` in `ui`
  and in `shell`, `flutter test` in `ui` and in `shell`, and `dart test` in `core`.
- New files use CRLF. Stage by explicit path; never stage `docs/coding/STATUS.md`, `.superpowers/`, or the
  two pre-existing modified files `src/Modules/UI/Flutter/shell/windows/flutter/generated_plugin_registrant.cc`
  and `generated_plugins.cmake`.
- Local gate before every commit:
  `dotnet format whitespace DigitalBrain.slnx --verify-no-changes && dotnet build DigitalBrain.slnx -c Release && dotnet test DigitalBrain.slnx -c Release --no-build`
  (expect the 4 Docker-gated skips plus the coding and slot-lease gated skips). Run one class with
  `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class <full class name>`.
- Never read or write under `C:\Users`; the local NuGet cache is not a documentation source. Every package
  API in this plan was checked on 2026-09-14 against current documentation (Context7 `/dotnet/yarp`:
  `InMemoryConfigProvider(routes, clusters)`, `IProxyConfigProvider`, `RouteConfig`/`RouteMatch`/
  `ClusterConfig`/`DestinationConfig`, `Update(routes, clusters)`, `AddReverseProxy()`, `MapReverseProxy()`;
  Context7 `/microsoft/aspire.dev`: `AddExecutable(name, command, workingDirectory, args)`,
  `WithHttpEndpoint(port, name, isProxied)`, `WithEnvironment(name, EndpointReference)`,
  `WithExplicitStart()`, `WithHttpHealthCheck(path, endpointName:)`, `WithReference` on executable
  resources, `WaitFor`; Context7 `/dotnet/orleans`: `IIncomingGrainCallFilter.Invoke(IIncomingGrainCallContext)`,
  `IIncomingGrainCallContext.InterfaceMethod`, `[ReadOnly]`, `ClusterOptions.ClusterId`/`ServiceId`;
  Microsoft Learn for `Azure.Data.Tables`: `ITableEntity`, `TableServiceClient.GetTableClient`/
  `CreateTableIfNotExistsAsync`, `TableClient.GetEntityAsync<T>`/`AddEntityAsync`/`UpdateEntityAsync(entity,
  ETag, TableUpdateMode.Replace)` with `RequestFailedException.Status` 412 `UpdateConditionNotSatisfied`
  (also proven live by spike S3); `/dotnet/extensions` for `AIFunctionFactory.Create` with
  `AIFunctionFactoryOptions`).

## File structure

```
src/Kernel/DigitalBrain.Contracts/Slots/ActiveSlotNames.cs        table, keys, the slot key, the refresh interval
src/Kernel/DigitalBrain.Contracts/Slots/StandbySlotException.cs   the fence's refusal
src/Kernel/DigitalBrain/Slots/IActiveSlotLease.cs                 the lease seam
src/Kernel/DigitalBrain/Slots/SingleSlotLease.cs                  one silo, always the holder
src/Kernel/DigitalBrain/Hosting/DigitalBrainRuntime.cs            + lease default and the start-up check
src/Kernel/DigitalBrain/Context/NeuronActivationGuardFilter.cs    + the standby check
src/Kernel/DigitalBrain/Neuron/Neuron.cs                          + IsStandby, Drain and ReceiveReminder
src/Kernel/DigitalBrain.Silo/Program.cs                           + ClusterId minting, + MapSlotEndpoints
src/Kernel/DigitalBrain.Silo/Http/SlotEndpoints.cs                GET /slots/{slot}: the slot's snapshot
src/Kernel/DigitalBrain.Silo/Http/AgentRunLedger.cs               what a run already answered
src/Kernel/DigitalBrain.Silo/Http/AgentRunRequest.cs              runId/threadId/resume off the body
src/Kernel/DigitalBrain.Silo/Http/AgentAnswerTap.cs               reads the answer out of the SSE it forwards
src/Kernel/DigitalBrain.Silo/Http/AgentReplayResult.cs            replays a known answer as AG-UI frames
src/Kernel/DigitalBrain.Silo/Http/AgentStreamResult.cs            + the tap
src/Kernel/DigitalBrain.Silo/Http/ConversationalAgentEndpoints.cs + the resume filter and the ledger
src/Aspire/DigitalBrain.Aspire/Slots/ActiveSlotLeaseEntity.cs     the lease row
src/Aspire/DigitalBrain.Aspire/Slots/AzureTableActiveSlotLease.cs compare-and-swap on the ETag
src/Aspire/DigitalBrain.Aspire/Slots/ActiveSlotLeaseRefresher.cs  re-reads the row every two seconds
src/Aspire/DigitalBrain.Aspire/DigitalBrainRuntimeHostingExtensions.cs + the lease registration
src/Aspire/DigitalBrain.Aspire/DigitalBrain.Aspire.csproj         + Azure.Data.Tables
src/Gateway/DigitalBrain.Gateway/DigitalBrain.Gateway.csproj      new project
src/Gateway/DigitalBrain.Gateway/Program.cs                       three lines over GatewayApplication
src/Gateway/DigitalBrain.Gateway/GatewayApplication.cs            composition, /switch/{slot}, /active
src/Gateway/DigitalBrain.Gateway/GatewayOptions.cs                slots, the initial active slot, debounce
src/Gateway/DigitalBrain.Gateway/SlotRouter.cs                    the owned InMemoryConfigProvider
src/Gateway/DigitalBrain.Gateway/ActiveSlotRow.cs                 one startup read of the lease row
src/Aspire/DigitalBrain.AppHost/AppHost.cs                        gateway, kernel-a, kernel-b
src/Aspire/DigitalBrain.AppHost/ProductSurfaceResources.cs        names and the three ports
src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj       + the gateway project reference
src/Modules/Microsoft/Contracts/IAspireResourceCommands.cs        the start|stop|restart seam
src/Modules/Microsoft/Microsoft/AspireConnection.cs               + ExecuteResourceCommandAsync
src/Modules/Microsoft/Microsoft/MicrosoftModule.cs                + the seam registration
src/Modules/Coding/Contracts/ISlot.cs, SlotPhase.cs, SlotSnapshot.cs, SlotReceipt.cs, BuildSlot.cs,
    PromoteSlot.cs, RetireSlot.cs, SlotBuildingBody.cs, SlotPromotionBody.cs
src/Modules/Coding/Contracts/CodingVocabulary.cs                  + SlotType and four signal names
src/Modules/Coding/Contracts/CodingJson.cs                        + every new DTO
src/Modules/Coding/Coding/SlotOptions.cs                          the DigitalBrain:Slots keys
src/Modules/Coding/Coding/ISlotBuilder.cs, SlotBuilder.cs, SlotBuildResult.cs
src/Modules/Coding/Coding/ISlotEndpoints.cs, HttpSlotEndpoints.cs
src/Modules/Coding/Coding/ChangeSetEditor.cs                      + TouchesSerializedStateAsync
src/Modules/Coding/Coding/DotnetRunner.cs                         + UseArtifactsOutput
src/Modules/Coding/Coding/GitRunner.cs                            + HeadCommitAsync (the generation)
src/Modules/Coding/Coding/UnconfiguredAspireResourceCommands.cs   the advice when Aspire is absent
src/Modules/Coding/Coding/SlotState.cs, SlotNeuron.cs
src/Modules/Coding/Coding/CodingNativeTools.cs                    + code_promote and a generic settle
src/Modules/Coding/Coding/CodingModule.cs                         + slot registrations and the tool
src/Modules/Coding/Coding/DigitalBrain.Modules.Coding.csproj       + Microsoft.Contracts, AspNetCore
src/Modules/AI/AI/ConversationalAgent.cs                          + code_promote in the allowlist
src/Testing/DigitalBrain.Testing/InMemoryActiveSlotLease.cs        the lease a fact drives
src/Modules/UI/Flutter/core/lib/src/agent_events.dart              + resume on AgentRunner
src/Modules/UI/Flutter/core/lib/src/ui_client.dart                 + resume in forwardedProps
src/Modules/UI/Flutter/shell/lib/workspace/workspace_chat.dart     one retry of an interrupted run
tests/DigitalBrain.Tests/Features/Slots/ActiveSlotLeaseFacts.cs
tests/DigitalBrain.Tests/Features/Slots/SlotFenceFacts.cs
tests/DigitalBrain.Tests/Features/Slots/AzureTableActiveSlotLeaseFacts.cs   gated
tests/DigitalBrain.Tests/Features/Slots/GatewayFacts.cs
tests/DigitalBrain.Tests/Features/Slots/SlotConfigurationFacts.cs
tests/DigitalBrain.Tests/Features/AspireConnectionFacts.cs
tests/DigitalBrain.Tests/Features/AgentResumeFacts.cs
tests/DigitalBrain.Tests/Features/Coding/SlotServiceFacts.cs
tests/DigitalBrain.Tests/Features/Coding/SlotNeuronFacts.cs
tests/DigitalBrain.Tests/Features/Coding/SlotFakes.cs
tests/DigitalBrain.Tests/Features/Coding/SlotToolFacts.cs
tests/DigitalBrain.Tests/Features/Coding/CodingNativeToolFacts.cs   + the fifteenth tool
tests/DigitalBrain.Tests/Features/Coding/RunnerFacts.cs             + UseArtifactsOutput
tests/DigitalBrain.Tests/Features/Coding/CodingSelfTestFacts.cs     + the gated slot build
tests/DigitalBrain.Tests/Features/Coding/CodeWorkspaceNeuronFacts.cs + ISlot in the contract theory
src/Modules/UI/Flutter/core/test/agent_stream_test.dart             + resume tests
src/Modules/UI/Flutter/shell/test/*.dart                            four files: resume in the fake runner
DigitalBrain.slnx, Directory.Packages.props, tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj
docs/coding/README.md, docs/coding/NOTES.md                       (STATUS.md stays the controller's)
```

Two facts from earlier phases change their expected numbers: `CodingNativeToolFacts.The_fourteen_tools_resolve`
becomes fifteen (Task 9) and `RunnerFacts.Build_errors_are_parsed_with_paths_and_lines` gains
`-p:UseArtifactsOutput=true` in its argument assertion (Task 5).

---

### Task 1: The active-slot lease

**Files:**
- Create: `src/Kernel/DigitalBrain.Contracts/Slots/ActiveSlotNames.cs`, `StandbySlotException.cs`,
  `src/Kernel/DigitalBrain/Slots/IActiveSlotLease.cs`, `SingleSlotLease.cs`,
  `src/Testing/DigitalBrain.Testing/InMemoryActiveSlotLease.cs`,
  `tests/DigitalBrain.Tests/Features/Slots/ActiveSlotLeaseFacts.cs`
- Modify: `src/Kernel/DigitalBrain/Hosting/DigitalBrainRuntime.cs`

**Interfaces:**
- Produces `DigitalBrain.Abstractions.Slots.ActiveSlotNames`: `SlotKey = "DigitalBrain:Slot"`,
  `Table = "DigitalBrainLeases"`, `PartitionKey = "slot"`, `RowKey = "active"`, `Owner = "Owner"`, and
  `RefreshInterval` (2 s) — the one place that says how often a silo re-reads the row, shared by the
  refresher (Task 3) and by the promotion's wait for the standby to see the flip (Task 6).
- Produces `DigitalBrain.Abstractions.Slots.StandbySlotException(string slot)`, `[GenerateSerializer]`
  `[Alias("db.v3.standby-slot")]`, derives `InvalidOperationException`, message
  `standby slot: silo '<slot>' does not hold the active lease`, `[Id(0)] public string Slot { get; }`.
- Produces `DigitalBrain.Core.IActiveSlotLease`: `string Slot { get; }`, `bool HoldsLease { get; }`,
  `long Generation { get; }`, `Task<bool> TryAcquireAsync(string slot, CancellationToken cancellationToken = default)`,
  `Task ReleaseAsync(CancellationToken cancellationToken = default)`,
  `Task RefreshAsync(CancellationToken cancellationToken = default)`. `HoldsLease` is a cached property:
  every grain call reads it, so it must never touch a table.
- Produces `DigitalBrain.Core.SingleSlotLease(string slot = "")` and
  `DigitalBrain.Testing.InMemoryActiveSlotLease(string slot, string? holder = null)` with a settable
  `Holder`, an `Interleave` hook that runs between the read and the write of `TryAcquireAsync`, and a
  `Refreshes` counter.
- `DigitalBrainRuntime.Add` registers `TryAddSingleton<IActiveSlotLease>` = `SingleSlotLease` over
  `ActiveSlotNames.SlotKey`, and its start-up task refuses to start a silo that names a slot but resolved
  the single-slot lease.

- [ ] **Step 1: Write the failing lease facts**

```csharp
// tests/DigitalBrain.Tests/Features/Slots/ActiveSlotLeaseFacts.cs
using DigitalBrain.Abstractions.Slots;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Xunit;

namespace DigitalBrain.Tests.Slots;

public sealed class ActiveSlotLeaseFacts
{
    [Fact]
    public void A_host_without_slots_always_holds_the_lease()
    {
        var lease = new SingleSlotLease();
        Assert.True(lease.HoldsLease);
        Assert.Equal(string.Empty, lease.Slot);
        Assert.Equal(0, lease.Generation);
    }

    [Fact]
    public async Task A_single_slot_lease_cannot_hand_the_lease_to_another_slot()
    {
        var lease = new SingleSlotLease("a");
        Assert.True(await lease.TryAcquireAsync("a", TestContext.Current.CancellationToken));
        Assert.False(await lease.TryAcquireAsync("b", TestContext.Current.CancellationToken));
        Assert.True(lease.HoldsLease);
    }

    [Fact]
    public void The_other_slot_holding_the_lease_makes_this_one_standby()
    {
        var lease = new InMemoryActiveSlotLease("a", holder: "other");
        Assert.False(lease.HoldsLease);
        Assert.Equal("a", lease.Slot);
        lease.Holder = "a";
        Assert.True(lease.HoldsLease);
    }

    [Fact]
    public async Task Acquiring_names_the_new_owner_and_bumps_the_generation()
    {
        var lease = new InMemoryActiveSlotLease("a");
        var generation = lease.Generation;
        Assert.True(await lease.TryAcquireAsync("b", TestContext.Current.CancellationToken));
        Assert.Equal("b", lease.Holder);
        Assert.False(lease.HoldsLease);
        Assert.True(lease.Generation > generation);
    }

    [Fact]
    public async Task A_write_that_races_the_read_loses_the_compare_and_swap()
    {
        var lease = new InMemoryActiveSlotLease("a");
        // The table answers 412 UpdateConditionNotSatisfied here (spike S3); the fake models the same
        // rule with a generation counter, so a fact can lose the race on purpose.
        lease.Interleave = () => lease.Holder = "other";
        Assert.False(await lease.TryAcquireAsync("b", TestContext.Current.CancellationToken));
        Assert.Equal("other", lease.Holder);
    }

    [Fact]
    public async Task Releasing_only_gives_up_a_lease_this_slot_holds()
    {
        var lease = new InMemoryActiveSlotLease("a");
        await lease.ReleaseAsync(TestContext.Current.CancellationToken);
        Assert.Null(lease.Holder);
        lease.Holder = "other";
        await lease.ReleaseAsync(TestContext.Current.CancellationToken);
        Assert.Equal("other", lease.Holder);
    }

    [Fact]
    public async Task A_refresh_is_counted_so_a_fact_can_see_the_refresher_run()
    {
        var lease = new InMemoryActiveSlotLease("a");
        await lease.RefreshAsync(TestContext.Current.CancellationToken);
        await lease.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, lease.Refreshes);
    }

    [Fact]
    public void The_refusal_names_the_standby_slot()
    {
        var error = new StandbySlotException("b");
        Assert.Equal("b", error.Slot);
        Assert.Equal("standby slot: silo 'b' does not hold the active lease", error.Message);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Slots.ActiveSlotLeaseFacts`
Expected: the build fails with CS0246 for `ActiveSlotNames`, `StandbySlotException`, `SingleSlotLease` and
`InMemoryActiveSlotLease` — none of the four types exists yet.

- [ ] **Step 3: Write the names, the refusal and the lease**

```csharp
// src/Kernel/DigitalBrain.Contracts/Slots/ActiveSlotNames.cs
namespace DigitalBrain.Abstractions.Slots;

// The kernel, the Aspire host and the gateway all address one row; these are the only names they share.
public static class ActiveSlotNames
{
    public const string SlotKey = "DigitalBrain:Slot";
    public const string Table = "DigitalBrainLeases";
    public const string PartitionKey = "slot";
    public const string RowKey = "active";
    public const string Owner = "Owner";

    // How often a silo re-reads the row. A promotion cannot switch the gateway before the standby has
    // had one of these, so the two sides read the same number.
    public static TimeSpan RefreshInterval { get; } = TimeSpan.FromSeconds(2);
}
```

```csharp
// src/Kernel/DigitalBrain.Contracts/Slots/StandbySlotException.cs
namespace DigitalBrain.Abstractions.Slots;

// Two silos of one service share grain storage and reminder rows, so only the lease holder may change
// anything. This refusal is transient and is never journaled: after a promotion the same command works.
[GenerateSerializer]
[Alias("db.v3.standby-slot")]
public sealed class StandbySlotException(string slot)
    : InvalidOperationException($"standby slot: silo '{slot}' does not hold the active lease")
{
    [Id(0)] public string Slot { get; } = slot;
}
```

```csharp
// src/Kernel/DigitalBrain/Slots/IActiveSlotLease.cs
namespace DigitalBrain.Core;

// Which slot may react. HoldsLease is answered from a cached read that a hosted refresher renews, because
// every incoming grain call consults it; TryAcquireAsync is the compare-and-swap that hands it over.
public interface IActiveSlotLease
{
    string Slot { get; }

    bool HoldsLease { get; }

    long Generation { get; }

    Task<bool> TryAcquireAsync(string slot, CancellationToken cancellationToken = default);

    Task ReleaseAsync(CancellationToken cancellationToken = default);

    Task RefreshAsync(CancellationToken cancellationToken = default);
}
```

```csharp
// src/Kernel/DigitalBrain/Slots/SingleSlotLease.cs
namespace DigitalBrain.Core;

// One silo, no slots: it always holds the lease, so the fence is invisible to phase 0 and phase 1 hosts.
public sealed class SingleSlotLease(string slot = "") : IActiveSlotLease
{
    public string Slot { get; } = slot;

    public bool HoldsLease => true;

    public long Generation => 0;

    // There is no row to hand over, so only this slot's own name can be acquired.
    public Task<bool> TryAcquireAsync(string slot, CancellationToken cancellationToken = default)
        => Task.FromResult(string.Equals(slot, Slot, StringComparison.OrdinalIgnoreCase));

    public Task ReleaseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RefreshAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
```

```csharp
// src/Testing/DigitalBrain.Testing/InMemoryActiveSlotLease.cs
using DigitalBrain.Core;

namespace DigitalBrain.Testing;

// The lease a fact drives by hand: Holder decides whether this silo is live or standby, so a fact can
// pretend the other slot owns it and watch the fence close. Interleave models the table's 412.
public sealed class InMemoryActiveSlotLease(string slot, string? holder = null) : IActiveSlotLease
{
    private readonly Lock _gate = new();
    private string? _holder = holder ?? slot;
    private long _generation;
    private int _refreshes;

    public string Slot { get; } = slot;

    public string? Holder
    {
        get
        {
            lock (_gate)
            {
                return _holder;
            }
        }

        set
        {
            lock (_gate)
            {
                _holder = value;
                _generation++;
            }
        }
    }

    // Runs between the read and the write of TryAcquireAsync so a fact can make the swap lose its race.
    public Action? Interleave { get; set; }

    public bool HoldsLease
    {
        get
        {
            lock (_gate)
            {
                return string.Equals(_holder, Slot, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    public long Generation
    {
        get
        {
            lock (_gate)
            {
                return _generation;
            }
        }
    }

    public int Refreshes => Volatile.Read(ref _refreshes);

    public Task<bool> TryAcquireAsync(string slot, CancellationToken cancellationToken = default)
    {
        long observed;
        lock (_gate)
        {
            observed = _generation;
        }

        Interleave?.Invoke();
        lock (_gate)
        {
            if (observed != _generation)
            {
                return Task.FromResult(false);
            }

            _holder = slot;
            _generation++;
            return Task.FromResult(true);
        }
    }

    public Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (string.Equals(_holder, Slot, StringComparison.OrdinalIgnoreCase))
            {
                _holder = null;
                _generation++;
            }
        }

        return Task.CompletedTask;
    }

    public Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _refreshes);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 4: Register the default and refuse a slot without a lease store**

In `src/Kernel/DigitalBrain/Hosting/DigitalBrainRuntime.cs` add `using DigitalBrain.Abstractions.Slots;`
and `using Microsoft.Extensions.Configuration;`, extend the existing start-up task, and register the
default lease next to the other `TryAddSingleton`s:

```csharp
        builder.AddStartupTask(static (services, _) =>
        {
            if (services.GetService<Orleans.IReminderTable>() is null)
            {
                throw new InvalidOperationException(
                    "Neurons hold a retry reminder for pending work. Configure UseAzureTableReminderService for a real host or UseInMemoryReminderService for a test host.");
            }

            // A slot that resolved the single-slot lease would react to shared reminders and shared
            // pending work while another slot serves traffic (design section 8, finding 1).
            if (services.GetRequiredService<IConfiguration>()[ActiveSlotNames.SlotKey] is { Length: > 0 } slot
                && services.GetRequiredService<IActiveSlotLease>() is SingleSlotLease)
            {
                throw new InvalidOperationException(
                    $"Slot '{slot}' has no active-slot lease store, so nothing would fence it. Register one (AddDigitalBrain does when the '{DigitalBrainNames.Clustering}' connection string is set) or clear '{ActiveSlotNames.SlotKey}'.");
            }

            // Building the table here turns a grain class descriptor violation into a silo-start failure.
            services.GetRequiredService<DescriptorTable>();
            return Task.CompletedTask;
        });
```

```csharp
        builder.Services.TryAddSingleton<IActiveSlotLease>(static services =>
            new SingleSlotLease(services.GetRequiredService<IConfiguration>()[ActiveSlotNames.SlotKey] ?? string.Empty));
```

`DigitalBrainNames` is already imported through `DigitalBrain.Abstractions` in that file's `using` list; add
the import if the build says otherwise.

- [ ] **Step 5: Run the facts**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Slots.ActiveSlotLeaseFacts`
Expected: 8 passed. Then `dotnet build DigitalBrain.slnx -c Release` warning-free, and
`dotnet test DigitalBrain.slnx -c Release --no-build` unchanged from the phase 1 baseline (no host sets
`DigitalBrain:Slot` yet, so every silo still resolves `SingleSlotLease` and holds the lease).

- [ ] **Step 6: Commit**

```bash
git add src/Kernel/DigitalBrain.Contracts/Slots src/Kernel/DigitalBrain/Slots src/Kernel/DigitalBrain/Hosting/DigitalBrainRuntime.cs src/Testing/DigitalBrain.Testing/InMemoryActiveSlotLease.cs tests/DigitalBrain.Tests/Features/Slots/ActiveSlotLeaseFacts.cs
git commit -m "coding: the active-slot lease, its single-slot default and the standby refusal"
```

---

### Task 2: The fence, and a fresh ClusterId per slot start

**Files:**
- Modify: `src/Kernel/DigitalBrain/Context/NeuronActivationGuardFilter.cs`,
  `src/Kernel/DigitalBrain/Neuron/Neuron.cs`, `src/Kernel/DigitalBrain.Silo/Program.cs`
- Create: `tests/DigitalBrain.Tests/Features/Slots/SlotFenceFacts.cs`

**Interfaces:**
- Consumes `IActiveSlotLease`, `StandbySlotException`, `ActiveSlotNames` (Task 1) and the test fixtures
  `ICounter`/`AddCount`/`CounterFixtureState`/`FlakyNeuron`/`FixtureSwitches` from
  `tests/DigitalBrain.Tests/Features/Fixtures.cs`.
- Produces the three fence checks: the filter refuses every non-`[ReadOnly]` call on a `Neuron` whose
  declaring interface is neither `INeuronInbox` nor `IRemindable`; `Neuron.Drain` returns at its top;
  `Neuron.ReceiveReminder` returns before `NoteTick`/`Wake`. `TimerAlarmGrain.Deliver` is covered by the
  filter because it calls `INeuron.Deliver`, which is not `[ReadOnly]`.
- Produces the silo's ClusterId minting: `Orleans:ClusterId = "{slot}-{yyyyMMddHHmmssfff}"` when the
  variable is empty and `DigitalBrain:Slot` is set (spike S2). The AppHost stops setting
  `Orleans__ClusterId` for slot resources in Task 8.

- [ ] **Step 1: Write the failing fence facts**

```csharp
// tests/DigitalBrain.Tests/Features/Slots/SlotFenceFacts.cs
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Core;
using DigitalBrain.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Slots;

// Design 4.4: a silo whose slot does not hold the lease answers reads, refuses commands, ignores reminder
// ticks and does not drain. The pending work it was holding is drained by whoever holds the lease next.
public sealed class SlotFenceFacts
{
    private static Task<BrainSimulation> StartAsync(InMemoryActiveSlotLease lease) => BrainSimulation.StartAsync(new()
    {
        Modules = new([]),
        Configuration = new Dictionary<string, string?> { ["DigitalBrain:Slot"] = lease.Slot },
        ConfigureSilo = silo =>
        {
            silo.Services.AddSingleton(new CounterFixtureState());
            silo.Services.AddSingleton<IActiveSlotLease>(lease);
        },
    });

    private static ICounter Counter(BrainSimulation brain, string name)
        => brain.Grains.GetGrain<ICounter>(new NeuronId("counter", name).ToGrainId());

    [Fact]
    public async Task A_command_on_a_standby_slot_is_refused_and_nothing_is_journaled()
    {
        var lease = new InMemoryActiveSlotLease("a", holder: "other");
        await using var brain = await StartAsync(lease);
        var counter = Counter(brain, "fenced");

        var error = await Assert.ThrowsAnyAsync<Exception>(() => counter.Add(new AddCount(CommandId.New(), 1)));
        Assert.Contains("standby slot", error.Message, StringComparison.Ordinal);
        Assert.Contains("'a'", error.Message, StringComparison.Ordinal);

        // The refusal is transient, so it must not enter the command journal: the same command id has to
        // work after the promotion, and a journaled rejection would replay as a rejection forever.
        Assert.Empty((await counter.ReadCommands(0)).Delta);
    }

    [Fact]
    public async Task A_read_answers_on_a_standby_slot()
    {
        var lease = new InMemoryActiveSlotLease("a", holder: "other");
        await using var brain = await StartAsync(lease);
        var counter = Counter(brain, "readable");

        // The promotion's smoke check is exactly this: a [ReadOnly] read against a fenced silo.
        Assert.Equal(0, await counter.ReadTotal());
        Assert.Empty(await counter.ReadSynapses());
        Assert.Equal(0, await counter.ReadPendingCount());
    }

    [Fact]
    public async Task A_standby_slot_neither_drains_nor_wakes_until_it_holds_the_lease()
    {
        var lease = new InMemoryActiveSlotLease("a");
        await using var brain = await StartAsync(lease);
        // Every reaction fails while this counter is above zero, so the pending head stays and each attempt
        // is counted: that is how a fact can see a drain happen, or not happen.
        FixtureSwitches.FlakyFailuresLeft["fenced"] = 50;
        var flaky = new NeuronId("flaky", "fenced");
        var source = brain.Grains.GetGrain<INeuron>(NeuronId.Plain("caller").ToGrainId());
        await source.Connect(flaky, "Ping");
        await source.Fire(Signal.Create("Ping", "{}"), null, null);
        await TestWait.UntilAsync(() => Task.FromResult(Attempts()), attempts => attempts >= 1,
            TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        var before = Attempts();
        lease.Holder = "other";
        var inbox = brain.Grains.GetGrain<INeuronInbox>(flaky.ToGrainId());
        await inbox.Drain();
        await Task.Delay(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);

        var neuron = brain.Grains.GetGrain<INeuron>(flaky.ToGrainId());
        Assert.Equal(before, Attempts());
        Assert.Equal(1, await neuron.ReadPendingCount());

        lease.Holder = "a";
        await inbox.Drain();
        var after = await TestWait.UntilAsync(() => Task.FromResult(Attempts()), attempts => attempts > before,
            TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
        Assert.True(after > before, $"the promoted slot drained {after} times, the standby {before}");
        FixtureSwitches.FlakyFailuresLeft.TryRemove("fenced", out _);
    }

    private static int Attempts() => FixtureSwitches.Reactions
        .Where(entry => entry.Key.StartsWith("fenced:", StringComparison.Ordinal))
        .Sum(entry => entry.Value);
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Slots.SlotFenceFacts`
Expected: 1 passed, 2 failed. The silo starts (the fact's `AddSingleton<IActiveSlotLease>` runs after the
module wiring, so it wins over Task 1's `TryAddSingleton` and Task 1's start-up check is satisfied).
`A_read_answers_on_a_standby_slot` passes already — nothing refuses reads.
`A_command_on_a_standby_slot_is_refused_and_nothing_is_journaled` fails at `ThrowsAnyAsync` because `Add`
succeeds, and `A_standby_slot_neither_drains_nor_wakes_until_it_holds_the_lease` fails at
`Assert.Equal(before, Attempts())` because the standby drained anyway.

- [ ] **Step 3: Put the standby check in the filter**

```csharp
// src/Kernel/DigitalBrain/Context/NeuronActivationGuardFilter.cs
using DigitalBrain.Abstractions.Slots;
using Orleans.Concurrency;

namespace DigitalBrain.Core;

internal sealed class NeuronActivationGuardFilter(IActiveSlotLease lease) : IIncomingGrainCallFilter
{
    public async Task Invoke(IIncomingGrainCallContext context)
    {
        if (context.Grain is Neuron neuron)
        {
            // Drain and ReceiveReminder already handle the fence themselves.
            var declaringInterface = context.InterfaceMethod.DeclaringType;
            if (declaringInterface != typeof(INeuronInbox)
                && declaringInterface != typeof(IRemindable))
            {
                // A standby silo shares this neuron's storage and reminders with the live one, so it may
                // answer reads (the promotion smoke-checks it through them) and change nothing. Throwing
                // here, before the method body, is what keeps the refusal out of the command journal.
                if (!lease.HoldsLease && !context.InterfaceMethod.IsDefined(typeof(ReadOnlyAttribute)))
                {
                    throw new StandbySlotException(lease.Slot);
                }

                await neuron.GuardActivationAsync().ConfigureAwait(true);
            }
        }

        await context.Invoke().ConfigureAwait(true);
    }
}
```

- [ ] **Step 4: Put the standby check in `Drain` and `ReceiveReminder`**

In `src/Kernel/DigitalBrain/Neuron/Neuron.cs`, resolve the lease in the constructor next to the other
optional services (a bare test host that registers none still works):

```csharp
    private readonly ILogger? _logger;
    private readonly StreamWake? _streamWake;
    private readonly IActiveSlotLease? _lease;
```

```csharp
        _logger = ServiceProvider.GetService<ILogger<Neuron>>();
        _streamWake = ServiceProvider.GetService<StreamWake>();
        _lease = ServiceProvider.GetService<IActiveSlotLease>();
```

Add the property next to `GuardActivationAsync`:

```csharp
    // A standby slot reacts to nothing: its pending work stays queued and the slot that holds the lease
    // next drains it (design 4.4).
    private bool IsStandby => _lease is { HoldsLease: false };
```

Then the two early returns:

```csharp
    async Task INeuronInbox.Drain()
    {
        Guard();
        if (IsStandby)
        {
            return;
        }

        RequestContext.Clear();
```

```csharp
    Task IRemindable.ReceiveReminder(string reminderName, TickStatus status)
    {
        if (reminderName != RetryScheduler.ReminderName || IsStandby)
        {
            return Task.CompletedTask;
        }

        _retry.NoteTick();
        Wake();
        return Task.CompletedTask;
    }
```

- [ ] **Step 5: Mint a ClusterId per slot start**

In `src/Kernel/DigitalBrain.Silo/Program.cs`, between `CreateBuilder` and `AddDigitalBrain`:

```csharp
using System.Globalization;
using DigitalBrain.Abstractions.Slots;
```

```csharp
var builder = WebApplication.CreateBuilder(args);

// Both slots share a ServiceId so grain state, journals and reminders survive a swap; membership must be
// fresh or the new silo stalls on the previous one's dead row (R5.3). A slot that was given no ClusterId
// mints one per start; the in-memory source is added last, so it wins precedence (spike S2).
if (string.IsNullOrWhiteSpace(builder.Configuration["Orleans:ClusterId"])
    && builder.Configuration[ActiveSlotNames.SlotKey] is { Length: > 0 } slot)
{
    var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture);
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["Orleans:ClusterId"] = slot + "-" + stamp,
    });
}

builder.AddDigitalBrain();
```

- [ ] **Step 6: Run the facts and the whole suite**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Slots.SlotFenceFacts`
Expected: 3 passed. Then the full gate:
`dotnet format whitespace DigitalBrain.slnx --verify-no-changes && dotnet build DigitalBrain.slnx -c Release && dotnet test DigitalBrain.slnx -c Release --no-build`
Expected: unchanged from the phase 1 baseline apart from the new facts — every existing silo resolves
`SingleSlotLease`, whose `HoldsLease` is always true, so no existing fact sees the fence. If a Coding or UI
fact starts failing with "standby slot", a fact registered a lease whose holder is another slot; that is a
real bug in the fact, not in the fence.

- [ ] **Step 7: Commit**

```bash
git add src/Kernel/DigitalBrain/Context/NeuronActivationGuardFilter.cs src/Kernel/DigitalBrain/Neuron/Neuron.cs src/Kernel/DigitalBrain.Silo/Program.cs tests/DigitalBrain.Tests/Features/Slots/SlotFenceFacts.cs
git commit -m "coding: fence a standby slot in the call filter, the drain and the reminder"
```

---

### Task 3: The lease row in Azure Table storage

**Files:**
- Create: `src/Aspire/DigitalBrain.Aspire/Slots/ActiveSlotLeaseEntity.cs`,
  `src/Aspire/DigitalBrain.Aspire/Slots/AzureTableActiveSlotLease.cs`,
  `src/Aspire/DigitalBrain.Aspire/Slots/ActiveSlotLeaseRefresher.cs`,
  `tests/DigitalBrain.Tests/Features/Slots/AzureTableActiveSlotLeaseFacts.cs`
- Modify: `src/Aspire/DigitalBrain.Aspire/DigitalBrainRuntimeHostingExtensions.cs`,
  `src/Aspire/DigitalBrain.Aspire/DigitalBrain.Aspire.csproj`, `Directory.Packages.props`,
  `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj` (the direct `Azure.Data.Tables` reference for the gated fact)

**Interfaces:**
- Produces `DigitalBrain.Aspire.AzureTableActiveSlotLease(string slot, TableServiceClient tables, TimeProvider clock, ILogger<AzureTableActiveSlotLease> logger)`
  implementing `IActiveSlotLease`, plus `Task BootstrapAsync(CancellationToken)` which takes the row when it
  does not exist yet. CAS = `GetEntityAsync` then `UpdateEntityAsync(entity, etag, TableUpdateMode.Replace)`;
  412 means the race was lost, 404 means bootstrap (spike S3).
- Produces `ActiveSlotLeaseRefresher`, an `IHostedService` that reads the row once before the host serves
  calls and then every two seconds, so `HoldsLease` is never a table read on the hot path.
- `AddDigitalBrain` registers both when `DigitalBrain:Slot` is set **and** the `clustering` connection
  string exists; otherwise the kernel's `SingleSlotLease` default stands.
- Adds `<PackageVersion Include="Azure.Data.Tables" Version="12.12.0" />` to `Directory.Packages.props`
  and `<PackageReference Include="Azure.Data.Tables" />` to `DigitalBrain.Aspire.csproj` and the test
  project, with 12.11.0 as the recorded fallback if transitive pinning conflicts at restore (Step 3).

- [ ] **Step 1: Write the failing gated fact**

```csharp
// tests/DigitalBrain.Tests/Features/Slots/AzureTableActiveSlotLeaseFacts.cs
using Azure;
using Azure.Data.Tables;
using DigitalBrain.Abstractions.Slots;
using DigitalBrain.Aspire;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Tests.Slots;

// The real lease against Azurite. Needs table storage, so it runs only when
// DIGITALBRAIN_SLOT_LEASE_TESTS=1; DIGITALBRAIN_SLOT_LEASE_TABLES overrides the connection string when
// Azurite runs on the AppHost's random ports instead of the development-storage defaults.
public sealed class AzureTableActiveSlotLeaseFacts : IAsyncDisposable
{
    private const string Skip = "Set DIGITALBRAIN_SLOT_LEASE_TESTS=1 (and DIGITALBRAIN_SLOT_LEASE_TABLES for a non-default Azurite) to exercise the real lease row.";

    public static bool LeaseTestsEnabled => Environment.GetEnvironmentVariable("DIGITALBRAIN_SLOT_LEASE_TESTS") == "1";

    private static string ConnectionString => Environment.GetEnvironmentVariable("DIGITALBRAIN_SLOT_LEASE_TABLES") is { Length: > 0 } configured
        ? configured
        : "UseDevelopmentStorage=true";

    private readonly TableServiceClient _tables = new(ConnectionString);

    [Fact(Skip = Skip, SkipUnless = nameof(LeaseTestsEnabled))]
    public async Task The_first_slot_to_bootstrap_owns_the_row()
    {
        await ResetAsync();
        var first = Lease("a");
        var second = Lease("b");
        await first.BootstrapAsync(TestContext.Current.CancellationToken);
        await second.BootstrapAsync(TestContext.Current.CancellationToken);

        Assert.True(first.HoldsLease);
        Assert.False(second.HoldsLease);
        Assert.Equal("a", await OwnerAsync());
    }

    [Fact(Skip = Skip, SkipUnless = nameof(LeaseTestsEnabled))]
    public async Task The_holder_hands_the_lease_to_the_other_slot()
    {
        await ResetAsync();
        var live = Lease("a");
        var standby = Lease("b");
        await live.BootstrapAsync(TestContext.Current.CancellationToken);
        await standby.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.False(standby.HoldsLease);

        // Promotion: the party that holds the lease writes the new owner (design 4.4).
        Assert.True(await live.TryAcquireAsync("b", TestContext.Current.CancellationToken));
        Assert.False(live.HoldsLease);
        Assert.Equal("b", await OwnerAsync());

        await standby.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.True(standby.HoldsLease);
        Assert.True(standby.Generation > 0);
    }

    [Fact(Skip = Skip, SkipUnless = nameof(LeaseTestsEnabled))]
    public async Task Releasing_gives_the_row_up_only_for_this_slot()
    {
        await ResetAsync();
        var live = Lease("a");
        var other = Lease("b");
        await live.BootstrapAsync(TestContext.Current.CancellationToken);

        // b does not hold it, so its release must leave a's ownership alone.
        await other.ReleaseAsync(TestContext.Current.CancellationToken);
        Assert.Equal("a", await OwnerAsync());

        await live.ReleaseAsync(TestContext.Current.CancellationToken);
        Assert.Equal(string.Empty, await OwnerAsync());
        Assert.False(live.HoldsLease);
    }

    [Fact(Skip = Skip, SkipUnless = nameof(LeaseTestsEnabled))]
    public async Task A_missing_table_reads_as_nobody_holding_the_lease()
    {
        await ResetAsync(create: false);
        var lease = Lease("a");
        await lease.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.False(lease.HoldsLease);
        Assert.Equal(0, lease.Generation);
    }

    private AzureTableActiveSlotLease Lease(string slot)
        => new(slot, _tables, TimeProvider.System, NullLogger<AzureTableActiveSlotLease>.Instance);

    private async Task<string?> OwnerAsync()
    {
        var row = await _tables.GetTableClient(ActiveSlotNames.Table)
            .GetEntityAsync<ActiveSlotLeaseEntity>(ActiveSlotNames.PartitionKey, ActiveSlotNames.RowKey,
                cancellationToken: TestContext.Current.CancellationToken);
        return row.Value.Owner;
    }

    private async Task ResetAsync(bool create = true)
    {
        await DropAsync(TestContext.Current.CancellationToken);
        if (create)
        {
            await _tables.CreateTableIfNotExistsAsync(ActiveSlotNames.Table, TestContext.Current.CancellationToken);
        }
    }

    private async Task DropAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _tables.DeleteTableAsync(ActiveSlotNames.Table, cancellationToken);
        }
        catch (RequestFailedException error) when (error.Status == 404)
        {
            // Nothing to drop.
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (LeaseTestsEnabled)
        {
            await DropAsync(CancellationToken.None);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Slots.AzureTableActiveSlotLeaseFacts`
Expected: the build fails with CS0246 for `AzureTableActiveSlotLease` and `ActiveSlotLeaseEntity`, plus
CS0246 for `Azure.Data.Tables` in the test project until Step 3 adds the package reference there too.

- [ ] **Step 3: Package references**

`Directory.Packages.props`, in the first `ItemGroup` next to the other Azure entries:

```xml
    <PackageVersion Include="Azure.Data.Tables" Version="12.12.0" />
```

`src/Aspire/DigitalBrain.Aspire/DigitalBrain.Aspire.csproj` and
`tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`, in their `PackageReference` group:

```xml
    <PackageReference Include="Azure.Data.Tables" />
```

12.12.0 is the latest stable on nuget.org. `CentralPackageTransitivePinningEnabled` is on, so this pins the
whole graph — including the copy `Aspire.Azure.Data.Tables` 13.5.3 and
`Microsoft.Orleans.Clustering.AzureStorage` 10.3.1 bring in, which resolves to 12.11.0 today. Restore and
build must stay clean; if either package constrains the version and restore fails (NU1605 or an
`Aspire.Azure.Data.Tables` version conflict), pin `12.11.0` instead — the version the repo already
resolves, which changes no other project's graph — and record the downgrade and its NuGet error in
`docs/coding/NOTES.md` as a phase 2 deviation. Nothing in this plan's code depends on an API newer than
12.11.0 (spike S3 ran against 12.12.0 and used only `GetEntityAsync`, `AddEntityAsync`,
`UpdateEntityAsync`, `CreateTableIfNotExistsAsync` and `DeleteTableAsync`).

- [ ] **Step 4: Write the row, the lease and the refresher**

```csharp
// src/Aspire/DigitalBrain.Aspire/Slots/ActiveSlotLeaseEntity.cs
using Azure;
using Azure.Data.Tables;
using DigitalBrain.Abstractions.Slots;

namespace DigitalBrain.Aspire;

// One row in DigitalBrainLeases: which slot may react, and when it last said so. The table is ours, not
// Orleans' (spike S3: the clustering table's layout is Orleans' private schema).
public sealed class ActiveSlotLeaseEntity : ITableEntity
{
    public string PartitionKey { get; set; } = ActiveSlotNames.PartitionKey;

    public string RowKey { get; set; } = ActiveSlotNames.RowKey;

    public DateTimeOffset? Timestamp { get; set; }

    public ETag ETag { get; set; }

    public string Owner { get; set; } = string.Empty;

    public long Generation { get; set; }

    public DateTimeOffset Fenced { get; set; }
}
```

```csharp
// src/Aspire/DigitalBrain.Aspire/Slots/AzureTableActiveSlotLease.cs
using Azure;
using Azure.Data.Tables;
using DigitalBrain.Abstractions.Slots;
using DigitalBrain.Core;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Aspire;

// The lease every grain call consults. Reads are answered from the cache the refresher renews; a hand-over
// is one compare-and-swap on the row's ETag, which is what fences a silo that still believes it is live.
public sealed class AzureTableActiveSlotLease : IActiveSlotLease
{
    private readonly TableServiceClient _tables;
    private readonly TableClient _table;
    private readonly TimeProvider _clock;
    private readonly ILogger<AzureTableActiveSlotLease> _logger;
    private Cached _cached = new(false, 0);
    private bool _tableReady;

    public AzureTableActiveSlotLease(string slot, TableServiceClient tables, TimeProvider clock, ILogger<AzureTableActiveSlotLease> logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        Slot = slot;
        _tables = tables;
        _table = tables.GetTableClient(ActiveSlotNames.Table);
        _clock = clock;
        _logger = logger;
    }

    public string Slot { get; }

    public bool HoldsLease => Volatile.Read(ref _cached).Holds;

    public long Generation => Volatile.Read(ref _cached).Generation;

    // A slot that starts against an empty table takes the row; a second slot then reads an owner and stays
    // standby. Never a hand-over: that is TryAcquireAsync's job.
    public async Task BootstrapAsync(CancellationToken cancellationToken = default)
    {
        await EnsureTableAsync(cancellationToken).ConfigureAwait(false);
        if (await ReadAsync(cancellationToken).ConfigureAwait(false) is { } existing)
        {
            Cache(existing.Owner, existing.Generation);
            return;
        }

        try
        {
            await _table.AddEntityAsync(
                new ActiveSlotLeaseEntity { Owner = Slot, Generation = 1, Fenced = _clock.GetUtcNow() },
                cancellationToken).ConfigureAwait(false);
            Cache(Slot, 1);
        }
        catch (RequestFailedException error) when (error.Status == 409)
        {
            // Another slot inserted the row first; its owner decides.
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<bool> TryAcquireAsync(string slot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        await EnsureTableAsync(cancellationToken).ConfigureAwait(false);
        if (await ReadAsync(cancellationToken).ConfigureAwait(false) is not { } row)
        {
            await BootstrapAsync(cancellationToken).ConfigureAwait(false);
            return string.Equals(await OwnerAsync(cancellationToken).ConfigureAwait(false), slot, StringComparison.OrdinalIgnoreCase);
        }

        var etag = row.ETag;
        var generation = row.Generation + 1;
        row.Owner = slot;
        row.Generation = generation;
        row.Fenced = _clock.GetUtcNow();
        try
        {
            await _table.UpdateEntityAsync(row, etag, TableUpdateMode.Replace, cancellationToken).ConfigureAwait(false);
            Cache(slot, generation);
            return true;
        }
        catch (RequestFailedException error) when (error.Status == 412)
        {
            // Somebody wrote the row between the read and the write: this silo is fenced, not the winner.
            _logger.LogWarning("Slot '{Slot}' lost the active-slot compare-and-swap to '{Candidate}'.", Slot, slot);
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
            return false;
        }
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken = default)
    {
        if (await ReadAsync(cancellationToken).ConfigureAwait(false) is not { } row
            || !string.Equals(row.Owner, Slot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var etag = row.ETag;
        var generation = row.Generation + 1;
        row.Owner = string.Empty;
        row.Generation = generation;
        row.Fenced = _clock.GetUtcNow();
        try
        {
            await _table.UpdateEntityAsync(row, etag, TableUpdateMode.Replace, cancellationToken).ConfigureAwait(false);
            Cache(string.Empty, generation);
        }
        catch (RequestFailedException error) when (error.Status == 412)
        {
            await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        var row = await ReadAsync(cancellationToken).ConfigureAwait(false);
        Cache(row?.Owner, row?.Generation ?? 0);
    }

    private async Task<ActiveSlotLeaseEntity?> ReadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var response = await _table.GetEntityAsync<ActiveSlotLeaseEntity>(
                ActiveSlotNames.PartitionKey, ActiveSlotNames.RowKey, cancellationToken: cancellationToken).ConfigureAwait(false);
            return response.Value;
        }
        catch (RequestFailedException error) when (error.Status == 404)
        {
            // No table or no row yet: nobody holds the lease.
            return null;
        }
    }

    private async Task<string?> OwnerAsync(CancellationToken cancellationToken)
        => (await ReadAsync(cancellationToken).ConfigureAwait(false))?.Owner;

    private async Task EnsureTableAsync(CancellationToken cancellationToken)
    {
        if (_tableReady)
        {
            return;
        }

        await _tables.CreateTableIfNotExistsAsync(ActiveSlotNames.Table, cancellationToken).ConfigureAwait(false);
        _tableReady = true;
    }

    private void Cache(string? owner, long generation)
        => Volatile.Write(ref _cached, new Cached(string.Equals(owner, Slot, StringComparison.OrdinalIgnoreCase), generation));

    private sealed record Cached(bool Holds, long Generation);
}
```

```csharp
// src/Aspire/DigitalBrain.Aspire/Slots/ActiveSlotLeaseRefresher.cs
using DigitalBrain.Abstractions.Slots;
using DigitalBrain.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Aspire;

// HoldsLease is read on every grain call, so the row is read out of band. The first read happens before
// the host serves anything: a live slot must not be fenced while it boots.
internal sealed class ActiveSlotLeaseRefresher(AzureTableActiveSlotLease lease, ILogger<ActiveSlotLeaseRefresher> logger) : IHostedService
{
    private readonly CancellationTokenSource _stopping = new();
    private Task? _loop;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await lease.BootstrapAsync(cancellationToken).ConfigureAwait(false);
        _loop = RefreshAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _stopping.CancelAsync().ConfigureAwait(false);
        if (_loop is { } loop)
        {
            await loop.WaitAsync(cancellationToken).ConfigureAwait(false);
        }

        _stopping.Dispose();
    }

    private async Task RefreshAsync()
    {
        // The one interval both sides of a promotion agree on (Task 1).
        using var timer = new PeriodicTimer(ActiveSlotNames.RefreshInterval);
        while (await SafeWaitAsync(timer).ConfigureAwait(false))
        {
            try
            {
                await lease.RefreshAsync(_stopping.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
                return;
            }
#pragma warning disable CA1031 // a storage hiccup must not tear the silo down; the cached verdict stands until the next read
            catch (Exception error)
#pragma warning restore CA1031
            {
                logger.LogWarning(error, "Re-reading the active-slot lease failed; slot '{Slot}' keeps its last verdict.", lease.Slot);
            }
        }
    }

    private async Task<bool> SafeWaitAsync(PeriodicTimer timer)
    {
        try
        {
            return await timer.WaitForNextTickAsync(_stopping.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
```

- [ ] **Step 5: Register the lease when a slot names itself**

In `src/Aspire/DigitalBrain.Aspire/DigitalBrainRuntimeHostingExtensions.cs` add
`using DigitalBrain.Abstractions.Slots;`, `using Microsoft.Extensions.Logging;`, call the new configurator
from inside `UseOrleans` right after `ConfigureStandaloneAzureClustering`, and add the method:

```csharp
        builder.UseOrleans(silo =>
        {
            ConfigureStandaloneAzureClustering(silo, builder.Configuration);
            ConfigureActiveSlotLease(silo, builder.Configuration);
```

```csharp
    // A slot needs the lease row to know whether it may react. Without a slot name (phase 0 and 1 hosts,
    // every test host) the kernel's SingleSlotLease stands and nothing is fenced.
    private static void ConfigureActiveSlotLease(ISiloBuilder silo, IConfiguration configuration)
    {
        var slot = configuration[ActiveSlotNames.SlotKey];
        if (string.IsNullOrWhiteSpace(slot)
            || string.IsNullOrWhiteSpace(configuration.GetConnectionString(DigitalBrainNames.Clustering)))
        {
            return;
        }

        silo.Services.AddSingleton(services => new AzureTableActiveSlotLease(
            slot,
            services.GetRequiredKeyedService<TableServiceClient>(DigitalBrainNames.Clustering),
            services.GetRequiredService<TimeProvider>(),
            services.GetRequiredService<ILogger<AzureTableActiveSlotLease>>()));
        silo.Services.AddSingleton<IActiveSlotLease>(static services => services.GetRequiredService<AzureTableActiveSlotLease>());
        silo.Services.AddHostedService<ActiveSlotLeaseRefresher>();
    }
```

`AddKeyedAzureTableServiceClient(DigitalBrainNames.Clustering)` is already called above, so the keyed
`TableServiceClient` exists; `TimeProvider` is registered by `DigitalBrainRuntime.Add`, which runs inside the
same `UseOrleans` delegate before the host resolves anything.

- [ ] **Step 6: Run the gated fact**

Run (with Azurite reachable — the AppHost's emulator or a standalone one; take the table connection string
from `mcp__aspire__list_resources` when the AppHost is up):
`DIGITALBRAIN_SLOT_LEASE_TESTS=1 DIGITALBRAIN_SLOT_LEASE_TABLES="<table connection string>" dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Slots.AzureTableActiveSlotLeaseFacts`
Expected: 4 passed. Without the variables: 4 skipped.
Then `dotnet build DigitalBrain.slnx -c Release` warning-free and
`dotnet test DigitalBrain.slnx -c Release --no-build` with four more skips than the phase 1 baseline.

- [ ] **Step 7: Commit**

```bash
git add Directory.Packages.props src/Aspire/DigitalBrain.Aspire tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj tests/DigitalBrain.Tests/Features/Slots/AzureTableActiveSlotLeaseFacts.cs
git commit -m "coding: the active-slot lease row with compare-and-swap on the table ETag"
```

---

### Task 4: Aspire resource commands

**Files:**
- Create: `src/Modules/Microsoft/Contracts/IAspireResourceCommands.cs`,
  `tests/DigitalBrain.Tests/Features/AspireConnectionFacts.cs`
- Modify: `src/Modules/Microsoft/Microsoft/AspireConnection.cs`,
  `src/Modules/Microsoft/Microsoft/MicrosoftModule.cs`

**Interfaces:**
- Produces `DigitalBrain.Microsoft.IAspireResourceCommands`:
  `Task<JsonElement> ExecuteAsync(string resourceName, string command, CancellationToken cancellationToken = default)`.
  The Coding module consumes this seam; the Microsoft module implements it with `AspireConnection`.
- Produces `AspireConnection.ExecuteResourceCommandAsync(string resourceName, string command, CancellationToken)`
  over the Aspire MCP tool `execute_resource_command` (`{resourceName, commandName}`, R5.1) with the
  allowlist `start|stop|restart`; `AspireConnection` now implements `IAspireResourceCommands`.
- The read allowlist is unchanged, and the "configure the AppHost project" check stays **in front of** it,
  so an unconfigured host still answers the configure advice for a read it would otherwise allow. Both
  paths share one private `CallAsync` over the resolved settings; the advice now reads "before reading or
  commanding its resources".

- [ ] **Step 1: Write the failing allowlist facts**

```csharp
// tests/DigitalBrain.Tests/Features/AspireConnectionFacts.cs
using DigitalBrain.Microsoft;
using Xunit;

namespace DigitalBrain.Tests;

// The Aspire client is a stdio process, so these facts assert the two things that are decided before any
// process starts: what may be executed, and what an unconfigured AppHost answers.
public sealed class AspireConnectionFacts
{
    [Theory]
    [InlineData("delete")]
    [InlineData("rebuild")]
    [InlineData("")]
    public async Task Only_start_stop_and_restart_may_be_executed(string command)
    {
        var connection = new AspireConnection(new AspireConnectionSettings("E:/repo/AppHost.csproj", "DigitalBrain", "aspire"));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connection.ExecuteResourceCommandAsync("kernel-b", command, TestContext.Current.CancellationToken));
        Assert.Contains("start, stop or restart", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unconfigured_aspire_application_refuses_a_command_with_advice()
    {
        var connection = new AspireConnection(settings: null);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connection.ExecuteResourceCommandAsync("kernel-b", "start", TestContext.Current.CancellationToken));
        Assert.Equal("Configure the Aspire AppHost project before reading or commanding its resources.", error.Message);
    }

    [Fact]
    public async Task An_unconfigured_aspire_application_refuses_a_read_with_the_same_advice()
    {
        var connection = new AspireConnection(settings: null);
        // The check is in front of the allowlist, so an allowed read on an unconfigured host says what to
        // do rather than trying to spawn the CLI.
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            connection.ReadAsync("list_resources", new Dictionary<string, object?>(), TestContext.Current.CancellationToken));
        Assert.Equal("Configure the Aspire AppHost project before reading or commanding its resources.", error.Message);
    }

    [Fact]
    public async Task A_resource_name_is_required()
    {
        var connection = new AspireConnection(new AspireConnectionSettings("E:/repo/AppHost.csproj", "DigitalBrain", "aspire"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            connection.ExecuteResourceCommandAsync("   ", "start", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void The_connection_is_the_resource_command_seam()
    {
        Assert.IsAssignableFrom<IAspireResourceCommands>(new AspireConnection(settings: null));
    }
}
```

`AspireConnection`'s constructor is `internal` and the tests project already has `InternalsVisibleTo` from
the Microsoft module, so the facts construct it directly.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.AspireConnectionFacts`
Expected: the build fails with CS1061 (`AspireConnection` has no `ExecuteResourceCommandAsync`) and CS0246
for `IAspireResourceCommands`.

- [ ] **Step 3: Write the seam and the command path**

```csharp
// src/Modules/Microsoft/Contracts/IAspireResourceCommands.cs
using System.Text.Json;

namespace DigitalBrain.Microsoft;

// Starting and stopping a resource is a capability the Microsoft module owns; the Coding module's slot
// neuron consumes it through this seam so a fact can promote a slot with no Aspire process in sight.
public interface IAspireResourceCommands
{
    Task<JsonElement> ExecuteAsync(string resourceName, string command, CancellationToken cancellationToken = default);
}
```

In `src/Modules/Microsoft/Microsoft/AspireConnection.cs`: declare the interface, keep `ReadAsync`'s
allowlist where it is, add the command path, and move the transport into one `CallAsync`.

```csharp
public sealed class AspireConnection : IAspireResourceCommands
{
    private readonly AspireConnectionSettings? _settings;

    internal AspireConnection(AspireConnectionSettings? settings) => _settings = settings;

    public string? ApplicationName => _settings?.ApplicationName;

    public Task<JsonElement> ReadAsync(string tool, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken = default)
    {
        // Configuration first: an unconfigured host answers what to do about it, not "not allowed".
        var settings = RequireSettings();
        if (tool is not ("list_resources" or "list_console_logs" or "list_structured_logs" or "list_traces" or "list_trace_structured_logs"))
        {
            throw new InvalidOperationException("This Aspire operation is not allowed.");
        }

        return CallAsync(settings, tool, arguments, cancellationToken);
    }

    // Promotion starts the standby and stops the retired slot (R5.2). Nothing else is executable: an
    // arbitrary command name would let a model reshape the running application.
    public Task<JsonElement> ExecuteAsync(string resourceName, string command, CancellationToken cancellationToken = default)
        => ExecuteResourceCommandAsync(resourceName, command, cancellationToken);

    public Task<JsonElement> ExecuteResourceCommandAsync(string resourceName, string command, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceName);
        var settings = RequireSettings();
        if (command is not ("start" or "stop" or "restart"))
        {
            throw new InvalidOperationException("An Aspire resource takes start, stop or restart.");
        }

        return CallAsync(settings, "execute_resource_command",
            new Dictionary<string, object?> { ["resourceName"] = resourceName, ["commandName"] = command },
            cancellationToken);
    }

    private AspireConnectionSettings RequireSettings()
        => _settings ?? throw new InvalidOperationException("Configure the Aspire AppHost project before reading or commanding its resources.");

    private async Task<JsonElement> CallAsync(AspireConnectionSettings settings, string tool, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Name = "aspire",
                Command = settings.Command,
                Arguments = ["agent", "mcp", "--non-interactive", "--log-level", "Error"],
                WorkingDirectory = Path.GetDirectoryName(settings.ProjectPath),
            });
            await using var client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token).ConfigureAwait(false);
            await BindApplicationAsync(client, settings, timeout.Token).ConfigureAwait(false);
            var result = await client.CallToolAsync(tool, arguments.ToDictionary(), cancellationToken: timeout.Token).ConfigureAwait(false);
            if (result.IsError == true)
            {
                throw new InvalidOperationException("Aspire did not return successful evidence.");
            }
            var envelope = JsonSerializer.SerializeToElement(result, McpJsonUtilities.DefaultOptions);
            if (Encoding.UTF8.GetByteCount(envelope.GetRawText()) > 128 * 1024)
            {
                throw new InvalidOperationException("Aspire evidence exceeds the response budget.");
            }
            var content = JsonNode.Parse(envelope.GetRawText())!.AsObject();
            // screened at the NativeTools boundary (AI module)
            content["untrustedData"] = true;
            return JsonSerializer.SerializeToElement(content);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception)
        {
            throw new InvalidOperationException("The configured Aspire application is unavailable. Check its AppHost and try again.");
        }
    }
```

`BindApplicationAsync` and the `AspireNativeTools` read tool stay exactly as they are: the tool surface does
not grow, only the allowlist does.

- [ ] **Step 4: Register the seam**

In `src/Modules/Microsoft/Microsoft/MicrosoftModule.cs`, after `builder.Services.AddSingleton(new AspireConnection(settings));`:

```csharp
        builder.Services.AddSingleton<IAspireResourceCommands>(static services => services.GetRequiredService<AspireConnection>());
```

The connection is registered even without settings, so the seam always resolves and an unconfigured AppHost
answers with the same advice the read path gives (design 4.9: "Aspire unreachable: `slot` commands refuse
with the Aspire error verbatim").

- [ ] **Step 5: Run the facts**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.AspireConnectionFacts`
Expected: 7 passed (three theory cases plus four facts). The unconfigured cases throw before any process is
spawned, so no `aspire` binary is needed.

- [ ] **Step 6: Commit**

```bash
git add src/Modules/Microsoft tests/DigitalBrain.Tests/Features/AspireConnectionFacts.cs
git commit -m "coding: Aspire start, stop and restart behind a resource-command seam"
```

---

### Task 5: The slot contract and the services behind it

**Files:**
- Create: `src/Modules/Coding/Contracts/ISlot.cs`, `SlotPhase.cs`, `SlotSnapshot.cs`, `SlotReceipt.cs`,
  `BuildSlot.cs`, `PromoteSlot.cs`, `RetireSlot.cs`, `SlotBuildingBody.cs`, `SlotPromotionBody.cs`;
  `src/Modules/Coding/Coding/SlotOptions.cs`, `ISlotBuilder.cs`, `SlotBuilder.cs`, `SlotBuildResult.cs`,
  `ISlotEndpoints.cs`, `HttpSlotEndpoints.cs`;
  `tests/DigitalBrain.Tests/Features/Coding/SlotServiceFacts.cs`
- Create: `src/Modules/Coding/Coding/UnconfiguredAspireResourceCommands.cs`,
  `src/Kernel/DigitalBrain.Silo/Http/SlotEndpoints.cs`
- Modify: `src/Modules/Coding/Contracts/CodingVocabulary.cs`, `CodingJson.cs`,
  `src/Modules/Coding/Coding/ChangeSetEditor.cs`, `DotnetRunner.cs`, `GitRunner.cs`, `CodingModule.cs`,
  `DigitalBrain.Modules.Coding.csproj`, `src/Kernel/DigitalBrain.Silo/Program.cs`,
  `tests/DigitalBrain.Tests/Features/Coding/RunnerFacts.cs`, `CodeWorkspaceNeuronFacts.cs`,
  `CodingSelfTestFacts.cs`

**Two commits:** the contracts and `SlotOptions` first (Steps 1-3 plus `SlotOptions` from Step 4), then the
builder, the endpoints, the state scan, the git generation and the registrations (the rest of Steps 4-7),
with the facts that cover each half green before its commit.

**Interfaces:**
- Produces `ISlot` (`[Alias("slot")]`, grain type `slot`, key `a` or `b`): `Build(BuildSlot)`,
  `Promote(PromoteSlot)`, `Retire(RetireSlot)`, `[ReadOnly] Read()` → `SlotSnapshot`.
- **`Generation` is the repository's HEAD commit hash** the slot is built from (D3: "the slot built from a
  recorded git generation"), or null when the tree is not a git repository. The same string travels through
  `BuildSlot.Generation`, `SlotState.Generation` and `SlotSnapshot.Generation`; it is never the change set's
  numeric `ChangeSetSnapshot.Generation` (that one counts workspace commits) and never a change-set id. A
  `changeId` is used for one thing only: the file list the rollback rule reads.
- Produces the DTOs, each `[GenerateSerializer]` with a `coding.` alias and an entry in `CodingJson`:
  `BuildSlot(CommandId Id, string? Generation = null, IReadOnlyList<string>? ChangedFiles = null) : Command(Id)`,
  `PromoteSlot(CommandId Id) : Command(Id)`, `RetireSlot(CommandId Id) : Command(Id)`,
  `SlotReceipt(string Slot, SlotPhase Phase, int Revision)`,
  `SlotSnapshot(string Slot, SlotPhase Phase, string? Generation, string? ArtifactsPath, bool Healthy, bool HoldsLease, IReadOnlyList<DiagnosticHit> Errors, string? Detail, bool RollbackAllowed, int Revision)`,
  `SlotPhase { Idle, Building, Built, Starting, Smoking, Live, Draining, Retired, Failed }` (`Draining`
  is reserved: phase 2 never assigns it, because the retiring slot drains inside the promoting reaction),
  `SlotBuildingBody(string? Generation, IReadOnlyList<string> ChangedFiles)`,
  `SlotPromotionBody(string FromSlot, int Attempt)`.
- Produces `CodingVocabulary.SlotType = "slot"` and the reaction signal names `SlotBuilding`,
  `SlotPromoting`, `SlotSwitching`, `SlotRetiring`.
- Produces `SlotOptions` with the configuration keys (defaults in brackets): `DigitalBrain:Slot` [unset],
  `DigitalBrain:Slots:ArtifactsRoot` [`<solution directory>/artifacts`; the AppHost sets it to
  `<repo>/artifacts` so the standby dll path and the build output cannot diverge],
  `DigitalBrain:Slots:Gateway` [`http://localhost:5080`], `DigitalBrain:Slots:Grace` [`00:00:10`],
  `DigitalBrain:Slots:LeaseSettle` [`00:00:10`, five `ActiveSlotNames.RefreshInterval`s],
  `DigitalBrain:Slots:SmokePath` [`/chats/slot-smoke/brain`], `DigitalBrain:Slots:HealthAttempts` [`120`],
  `DigitalBrain:Slots:PromoteWait` [`00:20:00`], `DigitalBrain:Slots:a:Url` [`http://localhost:5081`],
  `DigitalBrain:Slots:b:Url` [`http://localhost:5082`], `DigitalBrain:Slots:a:Resource` [`kernel-a`],
  `DigitalBrain:Slots:b:Resource` [`kernel-b`]; members `Names` (`["a", "b"]`), `UrlFor(slot)`,
  `ResourceFor(slot)`, `ArtifactsFor(slot, solutionDirectory)`.
- Produces `ISlotBuilder.BuildAsync(string slot, IReadOnlyList<string> changedFiles, CancellationToken)`
  → `SlotBuildResult(BuildOutcome Build, string ArtifactsPath, bool TouchesSerializedState)`, implemented by
  `SlotBuilder`.
- Produces `ISlotEndpoints`: `HealthyAsync(slotUrl, ct)`, `SmokeAsync(slotUrl, path, ct)` (null when the
  read answered, otherwise the failure), `HoldsLeaseAsync(slotUrl, slot, ct)` (does that silo itself say it
  holds the lease yet), `SwitchAsync(gatewayUrl, slot, ct)` (null when the switch was accepted, otherwise
  the gateway's `Retry-After`), `ActiveAsync(gatewayUrl, ct)`; implemented by `HttpSlotEndpoints` over the
  named HTTP client `digitalbrain-slots`, whose timeout is 60 s because a debounced switch can wait out the
  gateway's minimum interval.
- Produces the silo route `GET /slots/{slot}` (`SlotEndpoints.MapSlotEndpoints`, ungated, next to the other
  silo reads): `ISlot.Read()` as JSON. It is how one slot asks the other whether the lease flip has reached
  it, and it is a `[ReadOnly]` neuron read, so the fence lets a standby answer it.
- Produces `ChangeSetEditor.TouchesSerializedStateAsync(Solution, IReadOnlyList<string> files, CancellationToken)`
  and `GitRunner.HeadCommitAsync(repository, ct)` → the HEAD commit hash, or null when git says no.
- `DotnetRunner` now passes `-p:UseArtifactsOutput=true` alongside `-p:ArtifactsPath=` (spike S1: without it
  the SDK keeps the old layout and `<artifacts>/bin/<Project>/release/<Project>.dll` never appears).

- [ ] **Step 1: Write the failing service facts**

```csharp
// tests/DigitalBrain.Tests/Features/Coding/SlotServiceFacts.cs
using DigitalBrain.Abstractions.Slots;
using DigitalBrain.Coding;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DigitalBrain.Tests.Coding;

public sealed class SlotServiceFacts
{
    private const string StatePath = "E:/fixture/Delta/SlotState.cs";
    private const string PlainPath = "E:/fixture/Delta/Plain.cs";

    private const string StateSource = """
        namespace Delta;

        [GenerateSerializer]
        [Alias("delta.state")]
        public sealed record Counted(int Total);
        """;

    private const string PlainSource = """
        namespace Delta;

        public static class Plain
        {
            public static int Twice(int value) => value * 2;
        }
        """;

    private static SlotOptions Options(params (string Key, string Value)[] configured)
        => SlotOptions.From(new ConfigurationBuilder()
            .AddInMemoryCollection(configured.Select(entry => new KeyValuePair<string, string?>(entry.Key, entry.Value)))
            .Build());

    [Fact]
    public void Slot_options_default_to_the_gateway_and_the_two_kernel_ports()
    {
        var options = Options();
        Assert.Equal(["a", "b"], SlotOptions.Names);
        Assert.Equal("http://localhost:5080", options.GatewayUrl);
        Assert.Equal("http://localhost:5081", options.UrlFor("a"));
        Assert.Equal("http://localhost:5082", options.UrlFor("b"));
        Assert.Equal("kernel-a", options.ResourceFor("a"));
        Assert.Equal("kernel-b", options.ResourceFor("b"));
        Assert.Equal(TimeSpan.FromSeconds(10), options.Grace);
        Assert.Equal(TimeSpan.FromMinutes(20), options.PromoteWait);
        // Long enough for several of the interval the lease refresher polls on.
        Assert.Equal(TimeSpan.FromSeconds(10), options.LeaseSettle);
        Assert.True(options.LeaseSettle >= ActiveSlotNames.RefreshInterval * 2);
        Assert.Equal("/chats/slot-smoke/brain", options.SmokePath);
        Assert.Equal(120, options.HealthAttempts);
        Assert.Null(options.Slot);
    }

    [Fact]
    public void Configured_slots_win_over_the_defaults()
    {
        var options = Options(
            ("DigitalBrain:Slot", "b"),
            ("DigitalBrain:Slots:Gateway", "http://gateway:9000"),
            ("DigitalBrain:Slots:Grace", "00:00:02"),
            ("DigitalBrain:Slots:HealthAttempts", "7"),
            ("DigitalBrain:Slots:b:Url", "http://kernel-b:6000"),
            ("DigitalBrain:Slots:b:Resource", "silo-b"));
        Assert.Equal("b", options.Slot);
        Assert.Equal("http://gateway:9000", options.GatewayUrl);
        Assert.Equal(TimeSpan.FromSeconds(2), options.Grace);
        Assert.Equal(7, options.HealthAttempts);
        Assert.Equal("http://kernel-b:6000", options.UrlFor("b"));
        Assert.Equal("silo-b", options.ResourceFor("b"));
        Assert.Equal("http://localhost:5081", options.UrlFor("a"));
    }

    [Fact]
    public void An_unknown_slot_is_named_in_the_refusal()
    {
        var error = Assert.Throws<InvalidOperationException>(() => Options().UrlFor("c"));
        Assert.Contains("DigitalBrain:Slots:c:Url", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_artifacts_path_is_absolute_under_the_solution()
    {
        var artifacts = Options().ArtifactsFor("b", "E:/repo");
        Assert.Equal("E:/repo/artifacts/slot-b", artifacts.Replace('\\', '/'));
        var configured = Options(("DigitalBrain:Slots:ArtifactsRoot", "out")).ArtifactsFor("b", "E:/repo");
        Assert.Equal("E:/repo/out/slot-b", configured.Replace('\\', '/'));
    }

    [Fact]
    public async Task A_slot_build_asks_dotnet_for_the_artifacts_layout()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(0, "Build succeeded.\n    0 Warning(s)\n    0 Error(s)");
        var builder = await BuilderAsync(processes);

        var result = await builder.BuildAsync("b", [], TestContext.Current.CancellationToken);

        Assert.True(result.Build.Succeeded);
        Assert.Equal("E:/fixture/artifacts/slot-b", result.ArtifactsPath.Replace('\\', '/'));
        Assert.False(result.TouchesSerializedState);
        var call = Assert.Single(processes.Calls);
        var arguments = call.Arguments.Select(static argument => argument.Replace('\\', '/')).ToArray();
        Assert.Contains("-p:ArtifactsPath=E:/fixture/artifacts/slot-b", arguments);
        // Without this the SDK keeps bin/obj where they are and the slot's dll is not where the AppHost
        // looks for it (spike S1).
        Assert.Contains("-p:UseArtifactsOutput=true", arguments);
    }

    [Fact]
    public async Task A_broken_slot_build_carries_the_errors()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(1, "E:\\repo\\src\\A\\Thing.cs(12,9): error CS0103: The name 'Nope' does not exist [E:\\repo\\src\\A\\A.csproj]");
        var builder = await BuilderAsync(processes);

        var result = await builder.BuildAsync("b", [], TestContext.Current.CancellationToken);

        Assert.False(result.Build.Succeeded);
        Assert.Equal("CS0103", Assert.Single(result.Build.Errors).Id);
    }

    [Fact]
    public async Task A_written_file_that_declares_a_serialized_state_type_forbids_rollback()
    {
        using var workspace = new AdhocWorkspace();
        var project = ProjectId.CreateNewId("Delta");
        var solution = workspace.CurrentSolution
            .AddProject(ProjectInfo.Create(project, VersionStamp.Create(), "Delta", "Delta", LanguageNames.CSharp,
                filePath: "E:/fixture/Delta/Delta.csproj"))
            .AddDocument(DocumentId.CreateNewId(project), "SlotState.cs", SourceText.From(StateSource), filePath: StatePath)
            .AddDocument(DocumentId.CreateNewId(project), "Plain.cs", SourceText.From(PlainSource), filePath: PlainPath);
        Assert.True(workspace.TryApplyChanges(solution));
        var editor = new ChangeSetEditor(new CodeFixCatalog());

        Assert.True(await editor.TouchesSerializedStateAsync(workspace.CurrentSolution, [StatePath], TestContext.Current.CancellationToken));
        Assert.False(await editor.TouchesSerializedStateAsync(workspace.CurrentSolution, [PlainPath], TestContext.Current.CancellationToken));
        // A path the snapshot does not know (deleted, or outside the solution) is not evidence of a state change.
        Assert.False(await editor.TouchesSerializedStateAsync(workspace.CurrentSolution, ["E:/fixture/Delta/Gone.cs"], TestContext.Current.CancellationToken));
        Assert.False(await editor.TouchesSerializedStateAsync(workspace.CurrentSolution, [], TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task A_slot_build_reports_the_rollback_verdict_of_the_files_it_landed()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(0, "Build succeeded.\n    0 Warning(s)\n    0 Error(s)");
        var builder = await BuilderAsync(processes);

        // Greeter.cs declares no [GenerateSerializer] type, so a landing that wrote it may be rolled back.
        var result = await builder.BuildAsync("b", [FixtureSolutions.GreeterPath], TestContext.Current.CancellationToken);

        Assert.False(result.TouchesSerializedState);
    }

    [Fact]
    public async Task The_generation_a_slot_is_built_from_is_the_head_commit()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(0, "c9611543f0a1b2c3d4e5f60718293a4b5c6d7e8f\n");
        var git = new GitRunner(processes);

        var head = await git.HeadCommitAsync("E:/repo", TestContext.Current.CancellationToken);

        Assert.Equal("c9611543f0a1b2c3d4e5f60718293a4b5c6d7e8f", head);
        Assert.Equal(["--no-pager", "-c", "core.fsmonitor=false", "-c", "core.quotepath=false", "rev-parse", "HEAD"],
            Assert.Single(processes.Calls).Arguments);
    }

    [Fact]
    public async Task A_tree_that_is_not_a_repository_has_no_generation()
    {
        var processes = new FakeProcessRunner();
        processes.Enqueue(128, string.Empty, "fatal: not a git repository");
        var git = new GitRunner(processes);

        // Not an error: the slot is then built from an unrecorded generation.
        Assert.Null(await git.HeadCommitAsync("E:/repo", TestContext.Current.CancellationToken));
    }

    private static async Task<SlotBuilder> BuilderAsync(FakeProcessRunner processes)
    {
        var workspace = new SolutionWorkspace(new AdhocSolutionLoader(FixtureSolutions.TwoProjects), NullLogger<SolutionWorkspace>.Instance);
        await workspace.BeginOpenAsync("E:/fixture/Fixture.slnx");
        await workspace.WhenReadyAsync(TestContext.Current.CancellationToken);
        return new SlotBuilder(new DotnetRunner(processes), workspace, new ChangeSetEditor(new CodeFixCatalog()), Options());
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.SlotServiceFacts`
Expected: the build fails with CS0246 for `SlotOptions`, `SlotBuilder`, and CS1061 for
`ChangeSetEditor.TouchesSerializedStateAsync`.

- [ ] **Step 3: Write the contracts**

```csharp
// src/Modules/Coding/Contracts/SlotPhase.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.slot-phase")]
public enum SlotPhase
{
    Idle = 0,
    Building = 1,
    Built = 2,
    Starting = 3,
    Smoking = 4,
    Live = 5,
    // Reserved and never assigned in phase 2: the retiring slot drains inside the promoting reaction's
    // grace wait, so no snapshot is saved for that moment. A phase that drains on its own would use it.
    Draining = 6,
    Retired = 7,
    Failed = 8,
}
```

```csharp
// src/Modules/Coding/Contracts/SlotSnapshot.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.slot-snapshot")]
public sealed record SlotSnapshot(
    [property: Id(0)] string Slot,
    [property: Id(1)] SlotPhase Phase,
    [property: Id(2)] string? Generation,
    [property: Id(3)] string? ArtifactsPath,
    [property: Id(4)] bool Healthy,
    // A silo knows whether it itself holds the lease, never who else does, so reading the other slot's
    // neuron through this one always answers false.
    [property: Id(5)] bool HoldsLease,
    [property: Id(6)] IReadOnlyList<DiagnosticHit> Errors,
    [property: Id(7)] string? Detail,
    // True when the landing this slot was built for touched no [GenerateSerializer] type, so promoting the
    // previous slot back is still allowed: Orleans versions interfaces, not state (R5.3).
    [property: Id(8)] bool RollbackAllowed,
    [property: Id(9)] int Revision);
```

```csharp
// src/Modules/Coding/Contracts/SlotReceipt.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.slot-receipt")]
public sealed record SlotReceipt(
    [property: Id(0)] string Slot,
    [property: Id(1)] SlotPhase Phase,
    [property: Id(2)] int Revision);
```

```csharp
// src/Modules/Coding/Contracts/BuildSlot.cs
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Coding;

// Generation is the git generation the slot is built from (D3); ChangedFiles are the files the landing
// wrote, and they decide whether a rollback to the previous slot stays allowed.
[GenerateSerializer]
[Alias("coding.build-slot")]
public sealed record BuildSlot(
    CommandId Id,
    [property: Id(0)] string? Generation = null,
    [property: Id(1)] IReadOnlyList<string>? ChangedFiles = null) : Command(Id);
```

```csharp
// src/Modules/Coding/Contracts/PromoteSlot.cs
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.promote-slot")]
public sealed record PromoteSlot(CommandId Id) : Command(Id);
```

```csharp
// src/Modules/Coding/Contracts/RetireSlot.cs
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.retire-slot")]
public sealed record RetireSlot(CommandId Id) : Command(Id);
```

```csharp
// src/Modules/Coding/Contracts/SlotBuildingBody.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.slot-building-body")]
public sealed record SlotBuildingBody(
    [property: Id(0)] string? Generation,
    [property: Id(1)] IReadOnlyList<string> ChangedFiles);
```

```csharp
// src/Modules/Coding/Contracts/SlotPromotionBody.cs
namespace DigitalBrain.Coding;

// FromSlot is the slot that is live when the promotion starts: the one whose traffic drains and whose
// resource is stopped for a forward-only landing. Attempt counts the health probes.
[GenerateSerializer]
[Alias("coding.slot-promotion-body")]
public sealed record SlotPromotionBody(
    [property: Id(0)] string FromSlot,
    [property: Id(1)] int Attempt);
```

```csharp
// src/Modules/Coding/Contracts/ISlot.cs
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Neurons;
using Orleans.Concurrency;

namespace DigitalBrain.Coding;

[Alias("slot")]
public interface ISlot : INeuron
{
    [Alias("build")]
    Task<Accepted<SlotReceipt>> Build(BuildSlot command);

    [Alias("promote")]
    Task<Accepted<SlotReceipt>> Promote(PromoteSlot command);

    [Alias("retire")]
    Task<Accepted<SlotReceipt>> Retire(RetireSlot command);

    [ReadOnly]
    [Alias("read")]
    Task<SlotSnapshot> Read();
}
```

`CodingVocabulary` gains:

```csharp
    public const string SlotType = "slot";
```

```csharp
    public const string SlotBuilding = "SlotBuilding";
    public const string SlotPromoting = "SlotPromoting";
    public const string SlotSwitching = "SlotSwitching";
    public const string SlotRetiring = "SlotRetiring";
```

`CodingJson` gains nine `[JsonSerializable]` entries:

```csharp
[JsonSerializable(typeof(BuildSlot))]
[JsonSerializable(typeof(PromoteSlot))]
[JsonSerializable(typeof(RetireSlot))]
[JsonSerializable(typeof(SlotReceipt))]
[JsonSerializable(typeof(Accepted<SlotReceipt>))]
[JsonSerializable(typeof(SlotPhase))]
[JsonSerializable(typeof(SlotSnapshot))]
[JsonSerializable(typeof(SlotBuildingBody))]
[JsonSerializable(typeof(SlotPromotionBody))]
```

- [ ] **Step 4: Write the options, the builder and the endpoints**

```csharp
// src/Modules/Coding/Coding/SlotOptions.cs
using System.Globalization;
using DigitalBrain.Abstractions.Slots;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Coding;

// Everything the slot neuron needs to address the two kernels and the gateway. Two slots is the shape
// phase 2 proves (D2); a third would only need three configuration entries and a longer Names list.
public sealed record SlotOptions(
    string? Slot,
    string? ArtifactsRoot,
    string GatewayUrl,
    TimeSpan Grace,
    TimeSpan LeaseSettle,
    TimeSpan PromoteWait,
    string SmokePath,
    int HealthAttempts,
    IReadOnlyDictionary<string, string> Urls,
    IReadOnlyDictionary<string, string> Resources)
{
    public const string Section = "DigitalBrain:Slots";

    private const string ArtifactsDirectoryName = "artifacts";

    public static IReadOnlyList<string> Names { get; } = ["a", "b"];

    public static SlotOptions From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var slots = configuration.GetSection(Section);
        return new SlotOptions(
            configuration[ActiveSlotNames.SlotKey],
            slots["ArtifactsRoot"],
            Text(slots["Gateway"], "http://localhost:5080"),
            Duration(slots["Grace"], TimeSpan.FromSeconds(10)),
            // Five refresher intervals: the standby learns of a lease flip through its own poll.
            Duration(slots["LeaseSettle"], ActiveSlotNames.RefreshInterval * 5),
            Duration(slots["PromoteWait"], TimeSpan.FromMinutes(20)),
            Text(slots["SmokePath"], "/chats/slot-smoke/brain"),
            Count(slots["HealthAttempts"], 120),
            Names.ToDictionary(name => name, name => Text(slots[$"{name}:Url"], DefaultUrl(name)), StringComparer.OrdinalIgnoreCase),
            Names.ToDictionary(name => name, name => Text(slots[$"{name}:Resource"], "kernel-" + name), StringComparer.OrdinalIgnoreCase));
    }

    public string UrlFor(string slot)
        => Urls.TryGetValue(slot, out var url)
            ? url
            : throw new InvalidOperationException($"Slot '{slot}' has no address. Set '{Section}:{slot}:Url'.");

    public string ResourceFor(string slot)
        => Resources.TryGetValue(slot, out var resource)
            ? resource
            : throw new InvalidOperationException($"Slot '{slot}' has no Aspire resource. Set '{Section}:{slot}:Resource'.");

    // Always absolute: MSBuild resolves a relative ArtifactsPath against each project's own directory,
    // which scatters outputs under every project folder and poisons the next build (phase 1 live run).
    public string ArtifactsFor(string slot, string solutionDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentException.ThrowIfNullOrWhiteSpace(solutionDirectory);
        var root = string.IsNullOrWhiteSpace(ArtifactsRoot)
            ? Path.Combine(solutionDirectory, ArtifactsDirectoryName)
            : Path.GetFullPath(ArtifactsRoot, solutionDirectory);
        return Path.Combine(root, "slot-" + slot);
    }

    private static string DefaultUrl(string slot) => slot switch
    {
        "a" => "http://localhost:5081",
        "b" => "http://localhost:5082",
        _ => throw new InvalidOperationException($"Slot '{slot}' has no default address. Set '{Section}:{slot}:Url'."),
    };

    private static string Text(string? configured, string fallback)
        => configured is { Length: > 0 } value ? value : fallback;

    private static TimeSpan Duration(string? configured, TimeSpan fallback)
        => TimeSpan.TryParse(configured, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;

    private static int Count(string? configured, int fallback)
        => int.TryParse(configured, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : fallback;
}
```

```csharp
// src/Modules/Coding/Coding/SlotBuildResult.cs
namespace DigitalBrain.Coding;

public sealed record SlotBuildResult(BuildOutcome Build, string ArtifactsPath, bool TouchesSerializedState);
```

```csharp
// src/Modules/Coding/Coding/ISlotBuilder.cs
namespace DigitalBrain.Coding;

// A seam, so a slot fact can build a slot without a solution on disk and without dotnet.
public interface ISlotBuilder
{
    Task<SlotBuildResult> BuildAsync(string slot, IReadOnlyList<string> changedFiles, CancellationToken cancellationToken = default);
}
```

```csharp
// src/Modules/Coding/Coding/SlotBuilder.cs
namespace DigitalBrain.Coding;

// dotnet build of the loaded solution into the slot's own output root, plus the verdict a rollback needs:
// did this landing write a file that declares a [GenerateSerializer] type?
public sealed class SlotBuilder(DotnetRunner dotnet, SolutionWorkspace workspace, ChangeSetEditor editor, SlotOptions options) : ISlotBuilder
{
    public async Task<SlotBuildResult> BuildAsync(string slot, IReadOnlyList<string> changedFiles, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        ArgumentNullException.ThrowIfNull(changedFiles);
        var status = workspace.Status;
        var solutionPath = status.SolutionPath ?? throw new WorkspaceNotReadyException(status);
        var artifacts = options.ArtifactsFor(slot, Path.GetDirectoryName(Path.GetFullPath(solutionPath))!);
        var build = await dotnet.BuildAsync(solutionPath, artifacts, cancellationToken).ConfigureAwait(false);
        var touches = changedFiles.Count > 0
            && await workspace.QueryAsync(
                (solution, token) => editor.TouchesSerializedStateAsync(solution, changedFiles, token),
                cancellationToken).ConfigureAwait(false);
        return new SlotBuildResult(build, artifacts, touches);
    }
}
```

```csharp
// src/Modules/Coding/Coding/ISlotEndpoints.cs
namespace DigitalBrain.Coding;

// The HTTP a promotion needs: the standby's health, one read-only smoke read against it, and the
// gateway's switch. A seam, so a slot fact can promote with no kernel and no gateway running.
public interface ISlotEndpoints
{
    Task<bool> HealthyAsync(string slotUrl, CancellationToken cancellationToken = default);

    Task<string?> SmokeAsync(string slotUrl, string path, CancellationToken cancellationToken = default);

    // A silo learns of a lease flip through its own refresher, so the promotion asks the standby whether
    // the flip has reached it before it moves any traffic there.
    Task<bool> HoldsLeaseAsync(string slotUrl, string slot, CancellationToken cancellationToken = default);

    // Null when the switch was accepted; otherwise how long the gateway asks the caller to wait, because it
    // rebuilds its whole route table per update and debounces.
    Task<TimeSpan?> SwitchAsync(string gatewayUrl, string slot, CancellationToken cancellationToken = default);

    Task<string?> ActiveAsync(string gatewayUrl, CancellationToken cancellationToken = default);
}
```

```csharp
// src/Modules/Coding/Coding/HttpSlotEndpoints.cs
using System.Net;
using System.Text.Json;

namespace DigitalBrain.Coding;

public sealed class HttpSlotEndpoints(IHttpClientFactory clients) : ISlotEndpoints
{
    public const string HttpClientName = "digitalbrain-slots";

    public async Task<bool> HealthyAsync(string slotUrl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotUrl);
        using var client = clients.CreateClient(HttpClientName);
        try
        {
            using var response = await client.GetAsync(Address(slotUrl, "/health"), cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            // A slot that is not listening yet is not a failure; the reaction looks again.
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    // The smoke read is a [ReadOnly] neuron read over HTTP, which is exactly what the fence lets a standby
    // answer: it proves the new slot activated its brain without writing anything.
    public async Task<string?> SmokeAsync(string slotUrl, string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var client = clients.CreateClient(HttpClientName);
        try
        {
            using var response = await client.GetAsync(Address(slotUrl, path), cancellationToken).ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? null
                : $"the smoke read {path} on {slotUrl} answered {(int)response.StatusCode}";
        }
        catch (HttpRequestException error)
        {
            return $"the smoke read {path} on {slotUrl} failed: {error.Message}";
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return $"the smoke read {path} on {slotUrl} timed out";
        }
    }

    public async Task<bool> HoldsLeaseAsync(string slotUrl, string slot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slotUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        using var client = clients.CreateClient(HttpClientName);
        try
        {
            using var response = await client.GetAsync(Address(slotUrl, "/slots/" + Uri.EscapeDataString(slot)), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
            return document.RootElement.TryGetProperty("holdsLease", out var holds) && holds.ValueKind == JsonValueKind.True;
        }
        catch (HttpRequestException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    // One failure type, so the promotion's reaction has one thing to catch and one thing to report; a 429
    // is not a failure but a wait the gateway is asking for.
    public async Task<TimeSpan?> SwitchAsync(string gatewayUrl, string slot, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(slot);
        using var client = clients.CreateClient(HttpClientName);
        try
        {
            using var response = await client.PostAsync(Address(gatewayUrl, "/switch/" + Uri.EscapeDataString(slot)), content: null, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return response.Headers.RetryAfter?.Delta
                    ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : null)
                    ?? TimeSpan.FromSeconds(1);
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"the gateway at {gatewayUrl} refused the switch to '{slot}' with {(int)response.StatusCode}");
            }

            return null;
        }
        catch (HttpRequestException error)
        {
            throw new InvalidOperationException($"the gateway at {gatewayUrl} could not be reached: {error.Message}", error);
        }
        catch (TaskCanceledException error) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"the gateway at {gatewayUrl} did not answer the switch to '{slot}' in time", error);
        }
    }

    public async Task<string?> ActiveAsync(string gatewayUrl, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gatewayUrl);
        using var client = clients.CreateClient(HttpClientName);
        try
        {
            using var response = await client.GetAsync(Address(gatewayUrl, "/active"), cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            await using var body = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken).ConfigureAwait(false);
            return document.RootElement.TryGetProperty("active", out var active) ? active.GetString() : null;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static Uri Address(string root, string path) => new(new Uri(root, UriKind.Absolute), path);
}
```

- [ ] **Step 5: The serialized-state scan and the artifacts layout**

`ChangeSetEditor` gains one public method (it already imports `Microsoft.CodeAnalysis` and
`Microsoft.CodeAnalysis.CSharp.Syntax`):

```csharp
    // Rollback is only safe when nothing persisted changed shape (R5.3), and a state type is a type
    // declaration carrying [GenerateSerializer]. A syntax scan of the written files is enough and needs no
    // compilation: an unresolved attribute name still reads as that attribute.
    public async Task<bool> TouchesSerializedStateAsync(Solution solution, IReadOnlyList<string> files, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(solution);
        ArgumentNullException.ThrowIfNull(files);
        foreach (var path in files)
        {
            var document = solution.Projects
                .SelectMany(static project => project.Documents)
                .FirstOrDefault(candidate => string.Equals(candidate.FilePath, path, StringComparison.OrdinalIgnoreCase));
            if (document is null)
            {
                continue;
            }

            if (await document.GetSyntaxRootAsync(cancellationToken).ConfigureAwait(false) is not { } root)
            {
                continue;
            }

            if (root.DescendantNodes().OfType<BaseTypeDeclarationSyntax>()
                .SelectMany(static declaration => declaration.AttributeLists)
                .SelectMany(static list => list.Attributes)
                .Any(IsGenerateSerializer))
            {
                return true;
            }
        }

        return false;

        static bool IsGenerateSerializer(AttributeSyntax attribute)
        {
            var name = attribute.Name switch
            {
                QualifiedNameSyntax qualified => qualified.Right.Identifier.ValueText,
                SimpleNameSyntax simple => simple.Identifier.ValueText,
                _ => attribute.Name.ToString(),
            };
            return name is "GenerateSerializer" or "GenerateSerializerAttribute";
        }
    }
```

`DotnetRunner.RunDirectory` asks for the artifacts layout as well as the path:

```csharp
        if (!string.IsNullOrWhiteSpace(artifactsPath))
        {
            arguments.Add("-p:ArtifactsPath=" + Path.GetFullPath(artifactsPath, directory));
            // ArtifactsPath alone does not switch the SDK to the artifacts layout, so the slot's dll would
            // stay under each project's own bin (spike S1).
            arguments.Add("-p:UseArtifactsOutput=true");
        }
```

`GitRunner` gains the generation read, next to `CurrentBranchAsync` (`RunGitAsync` already throws on a
timeout and returns the result otherwise, so a non-zero exit is a "no" rather than an error):

```csharp
    // The generation a slot is built from is the repository's HEAD commit (D3). A tree that is not a
    // repository is not an error here: the slot is then built from an unrecorded generation.
    public async Task<string?> HeadCommitAsync(string repository, CancellationToken cancellationToken)
    {
        var result = await RunGitAsync(repository, ["rev-parse", "HEAD"], cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 && result.Output.Trim() is { Length: > 0 } hash ? hash : null;
    }
```

`tests/DigitalBrain.Tests/Features/Coding/RunnerFacts.cs`, in
`Build_errors_are_parsed_with_paths_and_lines`, extend the argument assertion:

```csharp
        Assert.Equal(["build", "E:/repo/Repo.slnx", "-c", "Release", "--nologo", "-p:ArtifactsPath=E:/repo/artifacts/slot-b", "-p:UseArtifactsOutput=true"],
            call.Arguments.Select(static argument => argument.Replace('\\', '/')));
```

- [ ] **Step 6: Register the services, the fallback seam and the slot route**

`src/Modules/Coding/Coding/DigitalBrain.Modules.Coding.csproj`:

```xml
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
```

```xml
    <ProjectReference Include="../../Microsoft/Contracts/DigitalBrain.Modules.Microsoft.Contracts.csproj" />
```

```csharp
// src/Modules/Coding/Coding/UnconfiguredAspireResourceCommands.cs
using System.Text.Json;
using DigitalBrain.Microsoft;

namespace DigitalBrain.Coding;

// The Coding module consumes the Microsoft module's capability but does not depend on it being composed:
// a silo without it refuses a promotion with advice instead of failing to resolve a service.
internal sealed class UnconfiguredAspireResourceCommands : IAspireResourceCommands
{
    public Task<JsonElement> ExecuteAsync(string resourceName, string command, CancellationToken cancellationToken = default)
        => throw new InvalidOperationException(
            $"Aspire is not configured in this silo, so resource '{resourceName}' cannot be sent '{command}'. Compose the Microsoft module with its AppHost project path.");
}
```

`src/Modules/Coding/Coding/CodingModule.cs`, in `Configure`:

```csharp
        builder.Services.TryAddSingleton(SlotOptions.From(builder.Configuration));
        builder.Services.TryAddSingleton<ISlotBuilder, SlotBuilder>();
        // 60s: a debounced gateway switch can wait out the gateway's minimum interval before answering.
        builder.Services.AddHttpClient(HttpSlotEndpoints.HttpClientName, static client => client.Timeout = TimeSpan.FromSeconds(60));
        builder.Services.TryAddSingleton<ISlotEndpoints, HttpSlotEndpoints>();
        // The Microsoft module registers the real one with AddSingleton, which wins in either composition
        // order: it is skipped by this TryAdd when it ran first, and resolved last when it runs after.
        builder.Services.TryAddSingleton<IAspireResourceCommands, UnconfiguredAspireResourceCommands>();
```

```csharp
// src/Kernel/DigitalBrain.Silo/Http/SlotEndpoints.cs
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Coding;
using Orleans.Runtime;

namespace DigitalBrain.Kernel;

internal static class SlotEndpoints
{
    // How one slot asks the other whether a lease flip has reached it, and how an operator reads a slot
    // without the dashboard. ISlot.Read is [ReadOnly], so a standby answers it through the fence.
    public static IEndpointRouteBuilder MapSlotEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/slots/{slot}",
            static async Task<IResult> (string slot, IGrainFactory grains, CancellationToken cancellationToken) =>
            {
                var neuron = grains.GetGrain<ISlot>(new NeuronId(CodingVocabulary.SlotType, slot).ToGrainId());
                return Results.Ok(await neuron.Read().WaitAsync(cancellationToken));
            }).AddEndpointFilter(new NeuronNameFilter("slot"));

        return endpoints;
    }
}
```

`src/Kernel/DigitalBrain.Silo/Program.cs`, next to the other ungated reads:

```csharp
app.MapWorkspaceEndpoints();
app.MapUiEndpoints();
app.MapSlotEndpoints();
app.MapBrainObservationEndpoints();
```

- [ ] **Step 7: The contract theory and the gated slot build**

`tests/DigitalBrain.Tests/Features/Coding/CodeWorkspaceNeuronFacts.cs`, one more theory row:

```csharp
    [InlineData(typeof(ISlot), 4)]
```

`tests/DigitalBrain.Tests/Features/Coding/CodingSelfTestFacts.cs`, one more gated fact:

```csharp
    [Fact(Skip = Skip, SkipUnless = nameof(SelfTestsEnabled))]
    public async Task The_real_solution_builds_into_the_standby_slot_output()
    {
        var repository = Path.GetDirectoryName(SolutionPath)!;
        var options = SlotOptions.From(new ConfigurationBuilder().Build());
        var artifacts = options.ArtifactsFor("b", repository);
        Assert.True(Path.IsPathFullyQualified(artifacts), artifacts);
        Assert.Equal(Path.Combine(repository, "artifacts", "slot-b"), artifacts);

        var runner = new DotnetRunner(new ProcessRunner());
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(20));
        var build = await runner.BuildAsync(SolutionPath, artifacts, timeout.Token);

        Assert.True(build.Succeeded, build.Detail ?? string.Join("; ", build.Errors.Select(error => $"{error.Path}:{error.Line} {error.Id}")));
        // The AppHost starts kernel-b from exactly this path (spike S1).
        Assert.True(File.Exists(Path.Combine(artifacts, "bin", "DigitalBrain.Silo", "release", "DigitalBrain.Silo.dll")),
            $"the standby dll is missing under {artifacts}");
    }
```

Add `using DigitalBrain.Coding;` (already present) and `using Microsoft.Extensions.Configuration;` to that
file.

- [ ] **Step 8: Run the facts**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.SlotServiceFacts`
Expected: 10 passed. Then `-- --filter-class DigitalBrain.Tests.Coding.RunnerFacts` (all pass with the new
argument) and `-- --filter-class DigitalBrain.Tests.Coding.CodeWorkspaceNeuronFacts` (the theory now has
three rows; `ISlot` has four methods and every DTO resolves through `CodingJson`). Then the full gate.
Expected: warning-free build, suite green with one more gated skip.

- [ ] **Step 9: Commit**

This task is the largest of the phase, so it lands as two commits, and `SlotServiceFacts` is authored in two
halves to match: for the first commit write only the four `SlotOptions` facts from Step 1's file (the test
project must compile, and the other facts reference types Steps 4 to 7 create), run the class green, commit;
then append the remaining facts from Step 1, implement Steps 4 to 7, run the whole class green, commit.

```bash
git add src/Modules/Coding/Contracts src/Modules/Coding/Coding/SlotOptions.cs tests/DigitalBrain.Tests/Features/Coding/SlotServiceFacts.cs
git commit -m "coding: the slot contract and the configuration keys behind it"
```

```bash
git add src/Modules/Coding src/Kernel/DigitalBrain.Silo tests/DigitalBrain.Tests/Features/Coding
git commit -m "coding: the slot builder, its endpoints, the state scan and the git generation"
```

---

### Task 6: The `slot` neuron

**Files:**
- Create: `src/Modules/Coding/Coding/SlotState.cs`, `SlotNeuron.cs`,
  `tests/DigitalBrain.Tests/Features/Coding/SlotFakes.cs`,
  `tests/DigitalBrain.Tests/Features/Coding/SlotNeuronFacts.cs`
- Modify: `src/Modules/Coding/Coding/CodingModule.cs` (nothing new to register: Orleans finds the grain
  class; the modify is only the `using` if the build asks for one)

**Interfaces:**
- Consumes `ISlot` and its DTOs (Task 5), `ISlotBuilder`, `ISlotEndpoints`, `SlotOptions` (Task 5),
  `IAspireResourceCommands` (Task 4), `IActiveSlotLease` (Task 1).
- **Module dependency:** starting and stopping a slot resource is the Microsoft module's capability, so a
  silo that promotes slots composes `MicrosoftModule` with its AppHost path (the dev AppHost does). A silo
  without it resolves Task 5's `UnconfiguredAspireResourceCommands`, and the promote reaction then settles
  as `Failed` with that advice instead of failing to construct the grain.
- Produces `[GrainType(CodingVocabulary.SlotType)] SlotNeuron : Neuron<SlotState>, ISlot` and
  `SlotState(SlotPhase Phase, string? Generation, string? ArtifactsPath, IReadOnlyList<DiagnosticHit> Errors, string? Detail, bool RollbackAllowed, bool Healthy, int Revision)`
  with `SlotState.Idle`.
- Produces the four reactions: `SlotBuilding` (build, then `Built` or `Failed`), `SlotPromoting` (start the
  resource once, then poll `/health`, then the smoke read), `SlotSwitching` (flip the lease, wait until the
  standby itself reports the flip, switch the gateway — treating the gateway's `Retry-After` as a bounded
  wait — drain for the grace period, save `Live`, and stop the old resource only for a forward-only
  landing), `SlotRetiring` (stop this slot's resource).
- The lease flip only changes a row; the standby learns of it through its own refresher
  (`ActiveSlotNames.RefreshInterval`). Moving traffic before that would meet a silo whose cached
  `HoldsLease` is still false, which refuses every mutation — so the switch waits for the standby's own
  `GET /slots/{slot}` to say `holdsLease`, bounded by `SlotOptions.LeaseSettle`, and hands the lease back
  when it never does.
- Produces the test fakes `FakeSlotBuilder`, `FakeSlotEndpoints`, `FakeAspireResourceCommands`.
- The rollback *policy* lives in the tool (Task 9), not in the neuron: a slot's state can only answer for
  its own landing, and a neuron never reads another neuron inside a command.

- [ ] **Step 1: Write the fakes and the failing neuron facts**

```csharp
// tests/DigitalBrain.Tests/Features/Coding/SlotFakes.cs
using System.Text.Json;
using DigitalBrain.Coding;
using DigitalBrain.Microsoft;

namespace DigitalBrain.Tests.Coding;

internal sealed class FakeSlotBuilder : ISlotBuilder
{
    public List<(string Slot, IReadOnlyList<string> ChangedFiles)> Builds { get; } = [];

    public BuildOutcome Outcome { get; set; } = new(true, [], 0, 1.5, "dotnet build", null);

    public bool TouchesSerializedState { get; set; }

    public string ArtifactsPath { get; set; } = "E:/repo/artifacts/slot-b";

    public Task<SlotBuildResult> BuildAsync(string slot, IReadOnlyList<string> changedFiles, CancellationToken cancellationToken = default)
    {
        Builds.Add((slot, changedFiles));
        return Task.FromResult(new SlotBuildResult(Outcome, ArtifactsPath, TouchesSerializedState));
    }
}

internal sealed class FakeSlotEndpoints : ISlotEndpoints
{
    private int _healthProbes;
    private int _leaseProbes;

    // Answer "not healthy" for this many probes, then healthy: that is a standby that must be started first.
    public int UnhealthyProbes { get; set; }

    // Answer "does not hold the lease yet" for this many probes: that is the standby's refresher lagging.
    public int LeaseProbesBeforeFlip { get; set; }

    public bool NeverSeesTheLease { get; set; }

    public string? SmokeFailure { get; set; }

    public string? ActiveSlot { get; set; }

    // Each entry answers one switch attempt with the gateway's Retry-After instead of accepting it.
    public Queue<TimeSpan> SwitchRetryAfter { get; } = new();

    public List<string> Switches { get; } = [];

    public List<string> Smokes { get; } = [];

    public List<string> LeaseProbes { get; } = [];

    public Task<bool> HealthyAsync(string slotUrl, CancellationToken cancellationToken = default)
        => Task.FromResult(Interlocked.Increment(ref _healthProbes) > UnhealthyProbes);

    public Task<string?> SmokeAsync(string slotUrl, string path, CancellationToken cancellationToken = default)
    {
        Smokes.Add(slotUrl + path);
        return Task.FromResult(SmokeFailure);
    }

    public Task<bool> HoldsLeaseAsync(string slotUrl, string slot, CancellationToken cancellationToken = default)
    {
        LeaseProbes.Add(slot);
        return Task.FromResult(!NeverSeesTheLease && Interlocked.Increment(ref _leaseProbes) > LeaseProbesBeforeFlip);
    }

    public Task<TimeSpan?> SwitchAsync(string gatewayUrl, string slot, CancellationToken cancellationToken = default)
    {
        if (SwitchRetryAfter.TryDequeue(out var retryAfter))
        {
            return Task.FromResult<TimeSpan?>(retryAfter);
        }

        Switches.Add(slot);
        ActiveSlot = slot;
        return Task.FromResult<TimeSpan?>(null);
    }

    public Task<string?> ActiveAsync(string gatewayUrl, CancellationToken cancellationToken = default)
        => Task.FromResult(ActiveSlot);
}

internal sealed class FakeAspireResourceCommands : IAspireResourceCommands
{
    public List<(string Resource, string Command)> Executed { get; } = [];

    public string? Failure { get; set; }

    public Task<JsonElement> ExecuteAsync(string resourceName, string command, CancellationToken cancellationToken = default)
    {
        Executed.Add((resourceName, command));
        return Failure is { Length: > 0 } failure
            ? Task.FromException<JsonElement>(new InvalidOperationException(failure))
            : Task.FromResult(JsonSerializer.SerializeToElement(new { resourceName, command }));
    }
}
```

```csharp
// tests/DigitalBrain.Tests/Features/Coding/SlotNeuronFacts.cs
using System.Text.Json;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Coding;
using DigitalBrain.Core;
using DigitalBrain.Kernel;
using DigitalBrain.Microsoft;
using DigitalBrain.Testing;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DigitalBrain.Tests.Coding;

// Design 4.4: build the standby, start it, smoke it read-only, flip the lease, wait for the standby to see
// the flip, switch the gateway, let the old slot drain. The commands only validate and schedule; every
// step above is a reaction.
public sealed class SlotNeuronFacts
{
    private sealed record World(
        BrainSimulation Brain,
        FakeSlotBuilder Builder,
        FakeSlotEndpoints Endpoints,
        FakeAspireResourceCommands Aspire,
        InMemoryActiveSlotLease Lease) : IAsyncDisposable
    {
        public ISlot Slot(string name) => Brain.Grains.GetGrain<ISlot>(new NeuronId(CodingVocabulary.SlotType, name).ToGrainId());

        public Task<SlotSnapshot> UntilAsync(string name, Func<SlotSnapshot, bool> done)
            => TestWait.UntilAsync(Slot(name).Read, done, TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);

        public ValueTask DisposeAsync() => Brain.DisposeAsync();
    }

    private static async Task<World> StartAsync(string liveSlot = "a", string leaseSettle = "00:00:10")
    {
        var builder = new FakeSlotBuilder();
        var endpoints = new FakeSlotEndpoints { ActiveSlot = liveSlot };
        var aspire = new FakeAspireResourceCommands();
        var lease = new InMemoryActiveSlotLease(liveSlot);
        var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(CodingModule)]),
            Configuration = new Dictionary<string, string?>
            {
                ["DigitalBrain:Slot"] = liveSlot,
                ["DigitalBrain:Slots:Grace"] = "00:00:00",
                ["DigitalBrain:Slots:LeaseSettle"] = leaseSettle,
            },
            ConfigureSilo = silo =>
            {
                silo.Services.AddSingleton<ISolutionLoader>(new AdhocSolutionLoader(FixtureSolutions.TwoProjects));
                silo.Services.AddSingleton<ISlotBuilder>(builder);
                silo.Services.AddSingleton<ISlotEndpoints>(endpoints);
                silo.Services.AddSingleton<IAspireResourceCommands>(aspire);
                silo.Services.AddSingleton<IActiveSlotLease>(lease);
            },
        });
        return new World(brain, builder, endpoints, aspire, lease);
    }

    [Fact]
    public async Task A_build_records_the_output_root_and_allows_a_rollback()
    {
        await using var world = await StartAsync();
        var accepted = await world.Slot("b").Build(new BuildSlot(CommandId.New(), "c9611543"));
        Assert.Equal("b", accepted.Receipt.Slot);

        var built = await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);
        Assert.Equal("c9611543", built.Generation);
        Assert.Equal("E:/repo/artifacts/slot-b", built.ArtifactsPath);
        Assert.True(built.RollbackAllowed);
        Assert.Empty(built.Errors);
        Assert.False(built.HoldsLease);
        Assert.Equal(("b", 0), (Assert.Single(world.Builder.Builds).Slot, Assert.Single(world.Builder.Builds).ChangedFiles.Count));
        Assert.Empty(world.Aspire.Executed);
    }

    [Fact]
    public async Task A_broken_build_fails_the_slot_and_leaves_the_live_one_alone()
    {
        await using var world = await StartAsync();
        world.Builder.Outcome = new BuildOutcome(false,
            [new DiagnosticHit("CS0103", "Error", "The name 'Nope' does not exist", "E:/repo/src/A/Thing.cs", 12)],
            0, 2.5, "dotnet build", null);

        await world.Slot("b").Build(new BuildSlot(CommandId.New()));

        var failed = await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Failed);
        Assert.Equal("CS0103", Assert.Single(failed.Errors).Id);
        Assert.Contains("the live slot is untouched", failed.Detail, StringComparison.Ordinal);
        Assert.Empty(world.Aspire.Executed);
        Assert.Empty(world.Endpoints.Switches);
        Assert.Equal("a", world.Lease.Holder);
    }

    [Fact]
    public async Task A_landing_that_changed_persisted_state_forbids_a_rollback()
    {
        await using var world = await StartAsync();
        world.Builder.TouchesSerializedState = true;

        await world.Slot("b").Build(new BuildSlot(CommandId.New(), ChangedFiles: ["E:/repo/src/Kernel/DigitalBrain/Neuron/NeuronState.cs"]));

        var built = await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);
        Assert.False(built.RollbackAllowed);
        Assert.Single(Assert.Single(world.Builder.Builds).ChangedFiles);
    }

    [Fact]
    public async Task Building_the_live_slot_is_refused()
    {
        await using var world = await StartAsync();
        var error = await Assert.ThrowsAnyAsync<Exception>(() => world.Slot("a").Build(new BuildSlot(CommandId.New())));
        // Windows cannot overwrite the assemblies this process has loaded (R5.4).
        Assert.Contains("serving traffic", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_promotion_starts_the_standby_smokes_it_and_flips_the_lease()
    {
        await using var world = await StartAsync();
        world.Endpoints.UnhealthyProbes = 1;
        await world.Slot("b").Build(new BuildSlot(CommandId.New()));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);

        await world.Slot("b").Promote(new PromoteSlot(CommandId.New()));

        var live = await world.UntilAsync("b", snapshot => snapshot.Phase is SlotPhase.Live or SlotPhase.Failed);
        Assert.Equal(SlotPhase.Live, live.Phase);
        Assert.True(live.Healthy);
        Assert.Equal(("kernel-b", "start"), Assert.Single(world.Aspire.Executed));
        Assert.Equal("http://localhost:5082/chats/slot-smoke/brain", Assert.Single(world.Endpoints.Smokes));
        Assert.Equal("b", Assert.Single(world.Endpoints.Switches));
        Assert.Equal("b", world.Lease.Holder);
        // Rollback stays open, so the old slot keeps running behind the fence.
        Assert.Contains("still running", live.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(world.Aspire.Executed, entry => entry.Command == "stop");
    }

    [Fact]
    public async Task A_forward_only_promotion_stops_the_slot_it_replaced()
    {
        await using var world = await StartAsync();
        world.Builder.TouchesSerializedState = true;
        await world.Slot("b").Build(new BuildSlot(CommandId.New(), ChangedFiles: ["E:/repo/src/Kernel/DigitalBrain/Neuron/NeuronState.cs"]));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);

        await world.Slot("b").Promote(new PromoteSlot(CommandId.New()));

        var live = await world.UntilAsync("b", snapshot => snapshot.Phase is SlotPhase.Live or SlotPhase.Failed);
        Assert.Equal(SlotPhase.Live, live.Phase);
        Assert.Contains("forward-only", live.Detail, StringComparison.Ordinal);
        Assert.Contains(("kernel-a", "stop"), world.Aspire.Executed);
    }

    [Fact]
    public async Task A_promotion_waits_for_the_standby_to_report_the_lease_before_moving_traffic()
    {
        await using var world = await StartAsync();
        world.Endpoints.UnhealthyProbes = 1;
        // The row is written, but the standby only learns of it on its own next refresh.
        world.Endpoints.LeaseProbesBeforeFlip = 1;
        await world.Slot("b").Build(new BuildSlot(CommandId.New()));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);

        await world.Slot("b").Promote(new PromoteSlot(CommandId.New()));

        var live = await world.UntilAsync("b", snapshot => snapshot.Phase is SlotPhase.Live or SlotPhase.Failed);
        Assert.Equal(SlotPhase.Live, live.Phase);
        Assert.Equal(["b", "b"], world.Endpoints.LeaseProbes);
        Assert.Equal("b", Assert.Single(world.Endpoints.Switches));
    }

    [Fact]
    public async Task A_standby_that_never_reports_the_lease_gets_it_taken_back()
    {
        await using var world = await StartAsync(leaseSettle: "00:00:00");
        world.Endpoints.UnhealthyProbes = 1;
        world.Endpoints.NeverSeesTheLease = true;
        await world.Slot("b").Build(new BuildSlot(CommandId.New()));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);

        await world.Slot("b").Promote(new PromoteSlot(CommandId.New()));

        var failed = await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Failed);
        Assert.Contains("did not report the active lease", failed.Detail, StringComparison.Ordinal);
        Assert.Empty(world.Endpoints.Switches);
        Assert.Equal("a", world.Lease.Holder);
    }

    [Fact]
    public async Task A_debounced_gateway_is_waited_out_not_failed()
    {
        await using var world = await StartAsync();
        world.Endpoints.UnhealthyProbes = 1;
        // The gateway answers 429 with "no wait left" once, then accepts.
        world.Endpoints.SwitchRetryAfter.Enqueue(TimeSpan.Zero);
        await world.Slot("b").Build(new BuildSlot(CommandId.New()));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);

        await world.Slot("b").Promote(new PromoteSlot(CommandId.New()));

        var live = await world.UntilAsync("b", snapshot => snapshot.Phase is SlotPhase.Live or SlotPhase.Failed);
        Assert.Equal(SlotPhase.Live, live.Phase);
        Assert.Equal("b", Assert.Single(world.Endpoints.Switches));
        Assert.Equal("b", world.Lease.Holder);
    }

    [Fact]
    public async Task A_gateway_that_keeps_debouncing_hands_the_lease_back()
    {
        await using var world = await StartAsync();
        world.Endpoints.UnhealthyProbes = 1;
        world.Endpoints.SwitchRetryAfter.Enqueue(TimeSpan.Zero);
        world.Endpoints.SwitchRetryAfter.Enqueue(TimeSpan.Zero);
        world.Endpoints.SwitchRetryAfter.Enqueue(TimeSpan.Zero);
        await world.Slot("b").Build(new BuildSlot(CommandId.New()));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);

        await world.Slot("b").Promote(new PromoteSlot(CommandId.New()));

        var failed = await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Failed);
        Assert.Contains("still debouncing switches", failed.Detail, StringComparison.Ordinal);
        Assert.Empty(world.Endpoints.Switches);
        Assert.Equal("a", world.Lease.Holder);
    }

    [Fact]
    public async Task The_slot_route_answers_the_snapshot_over_http()
    {
        await using var world = await StartAsync();
        await world.Slot("b").Build(new BuildSlot(CommandId.New(), "c9611543"));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(world.Brain.Grains);
        await using var app = builder.Build();
        app.MapSlotEndpoints();
        await app.StartAsync(TestContext.Current.CancellationToken);
        using var client = app.GetTestClient();

        // This is the read one slot uses to ask the other whether a lease flip has reached it.
        using var document = JsonDocument.Parse(await client.GetStringAsync("/slots/b", TestContext.Current.CancellationToken));
        Assert.Equal("Built", document.RootElement.GetProperty("phase").GetString());
        Assert.Equal("c9611543", document.RootElement.GetProperty("generation").GetString());
        Assert.False(document.RootElement.GetProperty("holdsLease").GetBoolean());
    }

    [Fact]
    public async Task A_smoke_read_that_fails_switches_nothing()
    {
        await using var world = await StartAsync();
        world.Endpoints.SmokeFailure = "the smoke read /chats/slot-smoke/brain on http://localhost:5082 answered 500";
        await world.Slot("b").Build(new BuildSlot(CommandId.New()));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);

        await world.Slot("b").Promote(new PromoteSlot(CommandId.New()));

        var failed = await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Failed);
        Assert.Contains("answered 500", failed.Detail, StringComparison.Ordinal);
        Assert.Empty(world.Endpoints.Switches);
        Assert.Equal("a", world.Lease.Holder);
    }

    [Fact]
    public async Task An_aspire_failure_is_reported_verbatim()
    {
        await using var world = await StartAsync();
        world.Endpoints.UnhealthyProbes = 1;
        world.Aspire.Failure = "The configured Aspire application is unavailable. Check its AppHost and try again.";
        await world.Slot("b").Build(new BuildSlot(CommandId.New()));
        await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Built);

        await world.Slot("b").Promote(new PromoteSlot(CommandId.New()));

        var failed = await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Failed);
        Assert.Equal("The configured Aspire application is unavailable. Check its AppHost and try again.", failed.Detail);
        Assert.Empty(world.Endpoints.Switches);
    }

    [Fact]
    public async Task Retiring_stops_the_resource_and_promoting_the_live_slot_is_refused()
    {
        await using var world = await StartAsync();
        await world.Slot("b").Retire(new RetireSlot(CommandId.New()));

        var retired = await world.UntilAsync("b", snapshot => snapshot.Phase == SlotPhase.Retired);
        Assert.Equal(("kernel-b", "stop"), Assert.Single(world.Aspire.Executed));
        Assert.False(retired.Healthy);

        var error = await Assert.ThrowsAnyAsync<Exception>(() => world.Slot("a").Promote(new PromoteSlot(CommandId.New())));
        Assert.Contains("already holds the active lease", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unknown_slot_name_is_refused()
    {
        await using var world = await StartAsync();
        var error = await Assert.ThrowsAnyAsync<Exception>(() => world.Slot("c").Build(new BuildSlot(CommandId.New())));
        Assert.Contains("'a' or 'b'", error.Message, StringComparison.Ordinal);
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.SlotNeuronFacts`
Expected: every fact fails with Orleans reporting no implementation for the `slot` grain type
(`ISlot` exists, `SlotNeuron` does not).

- [ ] **Step 3: Write the state and the neuron**

```csharp
// src/Modules/Coding/Coding/SlotState.cs
namespace DigitalBrain.Coding;

[GenerateSerializer]
[Alias("coding.slot-state")]
internal sealed record SlotState(
    [property: Id(0)] SlotPhase Phase,
    [property: Id(1)] string? Generation,
    [property: Id(2)] string? ArtifactsPath,
    [property: Id(3)] IReadOnlyList<DiagnosticHit> Errors,
    [property: Id(4)] string? Detail,
    [property: Id(5)] bool RollbackAllowed,
    [property: Id(6)] bool Healthy,
    [property: Id(7)] int Revision)
{
    public static readonly SlotState Idle = new(SlotPhase.Idle, null, null, [], null, true, false, 0);
}
```

```csharp
// src/Modules/Coding/Coding/SlotNeuron.cs
using DigitalBrain.Abstractions;
using DigitalBrain.Abstractions.Commands;
using DigitalBrain.Abstractions.Signals;
using DigitalBrain.Abstractions.Slots;
using DigitalBrain.Core;
using DigitalBrain.Microsoft;
using Microsoft.Extensions.Logging;
using Orleans.Concurrency;
using Orleans.Runtime;

namespace DigitalBrain.Coding;

[GrainType(CodingVocabulary.SlotType)]
internal sealed class SlotNeuron(
    NeuronRuntime runtime,
    [PersistentState("state", DigitalBrainNames.DefaultGrainStorage)] IPersistentState<SnapshotEnvelope<SlotState>> state,
    ISlotBuilder builder,
    ISlotEndpoints endpoints,
    IAspireResourceCommands aspire,
    IActiveSlotLease lease,
    SlotOptions options)
    : Neuron<SlotState>(runtime, state), ISlot
{
    private const int MaxSwitchAttempts = 3;

    private static readonly TimeSpan HealthPoll = TimeSpan.FromSeconds(1);

    private static readonly TimeSpan MaxSwitchWait = TimeSpan.FromSeconds(20);

    private SlotState Current => State ?? SlotState.Idle;

    private bool HoldsLease => lease.HoldsLease && string.Equals(lease.Slot, Id.Name, StringComparison.OrdinalIgnoreCase);

    public Task<Accepted<SlotReceipt>> Build(BuildSlot command) => ExecuteCommandAsync(
        Descriptor("build"), command, CodingJson.Default.BuildSlot, CodingJson.Default.AcceptedSlotReceipt, arguments =>
        {
            RejectUnknownSlot(arguments.Id);
            RejectWhenBusy(arguments.Id);
            if (HoldsLease)
            {
                // Windows cannot overwrite the assemblies this process has loaded (R5.4).
                throw new CommandRejectedException(arguments.Id, $"slot '{Id.Name}' is serving traffic",
                    $"Build the standby slot instead; '{Id.Name}' is serving traffic.");
            }

            var work = Schedule(Signal.FromJson(CodingVocabulary.SlotBuilding,
                new SlotBuildingBody(arguments.Generation, arguments.ChangedFiles ?? []), CodingJson.Default.SlotBuildingBody));
            return new Accepted<SlotReceipt>(Receipt(), work);
        });

    public Task<Accepted<SlotReceipt>> Promote(PromoteSlot command) => ExecuteCommandAsync(
        Descriptor("promote"), command, CodingJson.Default.PromoteSlot, CodingJson.Default.AcceptedSlotReceipt, arguments =>
        {
            RejectUnknownSlot(arguments.Id);
            RejectWhenBusy(arguments.Id);
            if (HoldsLease)
            {
                throw new CommandRejectedException(arguments.Id, $"slot '{Id.Name}' already holds the active lease",
                    "Read the slot; there is nothing to promote.");
            }

            // The slot this silo runs is the one that drains and, for a forward-only landing, stops.
            var work = Schedule(Signal.FromJson(CodingVocabulary.SlotPromoting,
                new SlotPromotionBody(lease.Slot, 0), CodingJson.Default.SlotPromotionBody));
            return new Accepted<SlotReceipt>(Receipt(), work);
        });

    public Task<Accepted<SlotReceipt>> Retire(RetireSlot command) => ExecuteCommandAsync(
        Descriptor("retire"), command, CodingJson.Default.RetireSlot, CodingJson.Default.AcceptedSlotReceipt, arguments =>
        {
            RejectUnknownSlot(arguments.Id);
            RejectWhenBusy(arguments.Id);
            if (HoldsLease)
            {
                throw new CommandRejectedException(arguments.Id, $"slot '{Id.Name}' holds the active lease",
                    "Promote the other slot first; the live slot is never retired.");
            }

            var work = Schedule(Signal.Create(CodingVocabulary.SlotRetiring, "{}"));
            return new Accepted<SlotReceipt>(Receipt(), work);
        });

    [ReadOnly]
    public Task<SlotSnapshot> Read()
    {
        var current = Current;
        return Task.FromResult(new SlotSnapshot(Id.Name, current.Phase, current.Generation, current.ArtifactsPath,
            current.Healthy, HoldsLease, current.Errors, current.Detail, current.RollbackAllowed, current.Revision));
    }

    protected override async Task ReceiveAsync(SignalDelivery delivery, CancellationToken cancellationToken)
    {
        var current = Current;
        switch (delivery.Signal.Type)
        {
            case CodingVocabulary.SlotBuilding:
                {
                    if (Body(delivery, CodingJson.Default.SlotBuildingBody) is not { } body)
                    {
                        return;
                    }

                    SlotState next;
                    try
                    {
                        var result = await builder.BuildAsync(Id.Name, body.ChangedFiles, cancellationToken).ConfigureAwait(true);
                        next = result.Build.Succeeded
                            ? current with
                            {
                                Phase = SlotPhase.Built,
                                Generation = body.Generation,
                                ArtifactsPath = result.ArtifactsPath,
                                Errors = [],
                                Detail = result.Build.Detail,
                                RollbackAllowed = !result.TouchesSerializedState,
                                Healthy = false,
                            }
                            : current with
                            {
                                Phase = SlotPhase.Failed,
                                Generation = body.Generation,
                                ArtifactsPath = result.ArtifactsPath,
                                Errors = result.Build.Errors,
                                Detail = $"{result.Build.Detail ?? $"the build left {result.Build.Errors.Count} error(s)"}; the live slot is untouched",
                                Healthy = false,
                            };
                    }
                    catch (InvalidOperationException error)
                    {
                        // The workspace is not ready, or the build could not start: the message is the advice.
                        next = current with { Phase = SlotPhase.Failed, Detail = error.Message + "; the live slot is untouched" };
                    }

                    await SaveAsync(next with { Revision = current.Revision + 1 }, cancellationToken).ConfigureAwait(true);
                    break;
                }
            case CodingVocabulary.SlotPromoting:
                {
                    if (Body(delivery, CodingJson.Default.SlotPromotionBody) is not { } body)
                    {
                        return;
                    }

                    await PromotingAsync(current, body, cancellationToken).ConfigureAwait(true);
                    break;
                }
            case CodingVocabulary.SlotSwitching:
                {
                    if (Body(delivery, CodingJson.Default.SlotPromotionBody) is not { } body)
                    {
                        return;
                    }

                    await SwitchingAsync(current, body, cancellationToken).ConfigureAwait(true);
                    break;
                }
            case CodingVocabulary.SlotRetiring:
                {
                    var detail = await StopAsync(Id.Name, cancellationToken).ConfigureAwait(true);
                    await SaveAsync(current with { Phase = SlotPhase.Retired, Healthy = false, Detail = detail, Revision = current.Revision + 1 },
                        cancellationToken).ConfigureAwait(true);
                    break;
                }
            default:
                return;
        }
    }

    private async Task PromotingAsync(SlotState current, SlotPromotionBody body, CancellationToken cancellationToken)
    {
        var url = options.UrlFor(Id.Name);
        var healthy = await endpoints.HealthyAsync(url, cancellationToken).ConfigureAwait(true);
        if (!healthy && body.Attempt == 0)
        {
            // A stopped standby needs starting; one that already answers (a rollback) is left alone.
            try
            {
                await aspire.ExecuteAsync(options.ResourceFor(Id.Name), "start", cancellationToken).ConfigureAwait(true);
            }
            catch (InvalidOperationException error)
            {
                await SaveAsync(current with { Phase = SlotPhase.Failed, Detail = error.Message, Revision = current.Revision + 1 },
                    cancellationToken).ConfigureAwait(true);
                return;
            }

            Schedule(Signal.FromJson(CodingVocabulary.SlotPromoting, body with { Attempt = 1 }, CodingJson.Default.SlotPromotionBody));
            await SaveAsync(current with { Phase = SlotPhase.Starting, Healthy = false, Detail = "starting " + options.ResourceFor(Id.Name), Revision = current.Revision + 1 },
                cancellationToken).ConfigureAwait(true);
            return;
        }

        if (!healthy)
        {
            if (body.Attempt >= options.HealthAttempts)
            {
                await SaveAsync(current with { Phase = SlotPhase.Failed, Detail = $"slot '{Id.Name}' did not answer /health at {url} after {body.Attempt} attempts", Revision = current.Revision + 1 },
                    cancellationToken).ConfigureAwait(true);
                return;
            }

            // Reads queue behind this bounded wait, so it stays one second: the next reaction looks again.
            await Task.Delay(HealthPoll, cancellationToken).ConfigureAwait(true);
            Schedule(Signal.FromJson(CodingVocabulary.SlotPromoting, body with { Attempt = body.Attempt + 1 }, CodingJson.Default.SlotPromotionBody));
            return;
        }

        // One read-only read against the fenced standby: it proves the new slot activated its brain and
        // wrote nothing (the fence lets a standby answer exactly this).
        if (await endpoints.SmokeAsync(url, options.SmokePath, cancellationToken).ConfigureAwait(true) is { } failure)
        {
            await SaveAsync(current with { Phase = SlotPhase.Failed, Healthy = true, Detail = failure, Revision = current.Revision + 1 },
                cancellationToken).ConfigureAwait(true);
            return;
        }

        Schedule(Signal.FromJson(CodingVocabulary.SlotSwitching, body, CodingJson.Default.SlotPromotionBody));
        await SaveAsync(current with { Phase = SlotPhase.Smoking, Healthy = true, Detail = null, Revision = current.Revision + 1 },
            cancellationToken).ConfigureAwait(true);
    }

    // Everything after the flip happens in this one turn on purpose: the moment the lease moves, this silo
    // is the standby, so nothing it schedules would ever drain here (design 4.4 and the fence).
    private async Task SwitchingAsync(SlotState current, SlotPromotionBody body, CancellationToken cancellationToken)
    {
        if (!await lease.TryAcquireAsync(Id.Name, cancellationToken).ConfigureAwait(true))
        {
            await SaveAsync(current with { Phase = SlotPhase.Failed, Detail = $"another silo changed the active-slot lease while promoting '{Id.Name}'; nothing was switched", Revision = current.Revision + 1 },
                cancellationToken).ConfigureAwait(true);
            return;
        }

        // The flip is a row; the new slot learns of it through its own refresher. Moving traffic before it
        // does would meet a silo that still refuses every mutation, so wait for the standby to say so.
        if (!await SettledAsync(cancellationToken).ConfigureAwait(true))
        {
            await HandBackAsync(body.FromSlot, cancellationToken).ConfigureAwait(true);
            await SaveAsync(current with
            {
                Phase = SlotPhase.Failed,
                Detail = $"slot '{Id.Name}' did not report the active lease within {options.LeaseSettle.TotalSeconds:0}s; the lease is back with '{body.FromSlot}' and nothing was switched",
                Revision = current.Revision + 1,
            }, cancellationToken).ConfigureAwait(true);
            return;
        }

        // A bounded retry inside this turn, never a scheduled follow-up: the lease has already moved, so
        // anything scheduled here would wait for a drain this silo will not do.
        for (var attempt = 0; ; attempt++)
        {
            TimeSpan? retryAfter;
            try
            {
                retryAfter = await endpoints.SwitchAsync(options.GatewayUrl, Id.Name, cancellationToken).ConfigureAwait(true);
            }
            catch (InvalidOperationException error)
            {
                // Traffic never moved, so the lease goes back to the slot that is still serving it.
                await HandBackAsync(body.FromSlot, cancellationToken).ConfigureAwait(true);
                await SaveAsync(current with { Phase = SlotPhase.Failed, Detail = error.Message, Revision = current.Revision + 1 },
                    cancellationToken).ConfigureAwait(true);
                return;
            }

            if (retryAfter is not { } wait)
            {
                break;
            }

            if (attempt + 1 >= MaxSwitchAttempts)
            {
                await HandBackAsync(body.FromSlot, cancellationToken).ConfigureAwait(true);
                await SaveAsync(current with
                {
                    Phase = SlotPhase.Failed,
                    Detail = $"the gateway at {options.GatewayUrl} is still debouncing switches after {attempt + 1} attempts; the lease is back with '{body.FromSlot}'",
                    Revision = current.Revision + 1,
                }, cancellationToken).ConfigureAwait(true);
                return;
            }

            await Task.Delay(wait < MaxSwitchWait ? wait : MaxSwitchWait, cancellationToken).ConfigureAwait(true);
        }

        // The retiring slot finishes the HTTP it already accepted; YARP keeps in-flight requests on the
        // snapshot they were routed with (spike S4).
        if (options.Grace > TimeSpan.Zero)
        {
            await Task.Delay(options.Grace, cancellationToken).ConfigureAwait(true);
        }

        var confirmed = await endpoints.ActiveAsync(options.GatewayUrl, cancellationToken).ConfigureAwait(true);
        var forwardOnly = !current.RollbackAllowed;
        var detail = body.FromSlot.Length == 0
            ? null
            : forwardOnly
                ? $"slot '{body.FromSlot}' is stopped: this landing changed persisted state, so it is forward-only"
                : $"slot '{body.FromSlot}' is fenced and still running; promote it back to roll back, or retire it";
        if (!string.Equals(confirmed, Id.Name, StringComparison.OrdinalIgnoreCase))
        {
            detail = $"the gateway reports '{confirmed ?? "unknown"}' as active, not '{Id.Name}'. {detail}";
        }

        await SaveAsync(current with { Phase = SlotPhase.Live, Healthy = true, Detail = detail, Revision = current.Revision + 1 },
            cancellationToken).ConfigureAwait(true);

        // Last, and only for a forward-only landing: stopping the retired slot kills the very process this
        // reaction runs in, so the snapshot must be durable first. A crash here leaves the old slot running
        // behind the fence, which is safe, and `retire` stops it.
        if (forwardOnly && body.FromSlot.Length > 0)
        {
            await StopAsync(body.FromSlot, cancellationToken).ConfigureAwait(true);
        }
    }

    // Poll the standby's own read, at the interval its refresher runs on, until it agrees it holds the
    // lease. The first probe usually answers false: the row was written moments ago.
    private async Task<bool> SettledAsync(CancellationToken cancellationToken)
    {
        var url = options.UrlFor(Id.Name);
        var deadline = TimeProvider.GetUtcNow() + options.LeaseSettle;
        while (true)
        {
            if (await endpoints.HoldsLeaseAsync(url, Id.Name, cancellationToken).ConfigureAwait(true))
            {
                return true;
            }

            if (TimeProvider.GetUtcNow() >= deadline)
            {
                return false;
            }

            await Task.Delay(ActiveSlotNames.RefreshInterval, cancellationToken).ConfigureAwait(true);
        }
    }

    private async Task HandBackAsync(string slot, CancellationToken cancellationToken)
    {
        if (slot.Length > 0)
        {
            await lease.TryAcquireAsync(slot, cancellationToken).ConfigureAwait(true);
        }
    }

    private async Task<string?> StopAsync(string slot, CancellationToken cancellationToken)
    {
        try
        {
            await aspire.ExecuteAsync(options.ResourceFor(slot), "stop", cancellationToken).ConfigureAwait(true);
            return null;
        }
        catch (InvalidOperationException error)
        {
            ServiceProvider.GetService<ILogger<SlotNeuron>>()?.LogWarning(error, "Stopping slot {Slot} failed.", slot);
            return error.Message;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The stop killed this process, which is the point of it; there is nothing left to record.
            return null;
        }
    }

    private SlotReceipt Receipt() => new(Id.Name, Current.Phase, Current.Revision);

    private void RejectUnknownSlot(CommandId id)
    {
        if (!SlotOptions.Names.Contains(Id.Name, StringComparer.OrdinalIgnoreCase))
        {
            throw new CommandRejectedException(id, $"'{Id.Name}' is not a slot", "Name the slot 'a' or 'b'.");
        }
    }

    private void RejectWhenBusy(CommandId id)
    {
        if (Current.Phase is SlotPhase.Building or SlotPhase.Starting or SlotPhase.Smoking)
        {
            throw new CommandRejectedException(id, $"slot '{Id.Name}' is {Current.Phase.ToString().ToLowerInvariant()}",
                "Wait for the slot to settle and read it again.");
        }
    }
}
```

- [ ] **Step 4: Run the facts**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.SlotNeuronFacts`
Expected: 15 passed. If a silo start fails with a descriptor message, it names the method and the rule
(`Read` must be `[ReadOnly]` with no arguments; each mutator takes one `Command`-derived DTO). If
`A_promotion_starts_the_standby_smokes_it_and_flips_the_lease` times out at `Smoking`, the `SlotSwitching`
signal name is missing from `CodingVocabulary` or from the reaction switch. If it fails at `Failed` with
"did not report the active lease", the fake's `LeaseProbesBeforeFlip` is higher than the fact set it to.
Then the full gate.

- [ ] **Step 5: Commit**

```bash
git add src/Modules/Coding tests/DigitalBrain.Tests/Features/Coding
git commit -m "coding: the slot neuron builds, starts, smokes and promotes a standby kernel"
```

---

### Task 7: The gateway

**Files:**
- Create: `src/Gateway/DigitalBrain.Gateway/DigitalBrain.Gateway.csproj`, `Program.cs`,
  `GatewayApplication.cs`, `GatewayOptions.cs`, `SlotRouter.cs`, `ActiveSlotRow.cs`,
  `tests/DigitalBrain.Tests/Features/Slots/GatewayFacts.cs`
- Modify: `Directory.Packages.props`, `DigitalBrain.slnx`,
  `tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`

**Interfaces:**
- Produces `DigitalBrain.Gateway.GatewayApplication.CreateAsync(string[] args)` → `Task<WebApplication>`
  (internal, `InternalsVisibleTo DigitalBrain.Tests`), which the three-line `Program.cs` runs and a fact
  hosts on a loopback port.
- Produces `GatewayOptions(IReadOnlyDictionary<string, string> Slots, string Active, TimeSpan MinSwitchInterval)`
  from `DigitalBrain:Gateway:Slots:a`, `:b` (defaults `http://localhost:5081`, `http://localhost:5082`),
  `DigitalBrain:Gateway:Active` (default `a`) and `DigitalBrain:Gateway:MinSwitchInterval` (default
  `00:00:15`).
- Produces `SlotRouter` with `InMemoryConfigProvider Provider`, `string Active`,
  `SwitchResult Switch(string slot)` and `void Adopt(string slot)` for the startup read; registered as the
  `IProxyConfigProvider` singleton through `Provider`. `SwitchResult(SwitchVerdict Verdict, TimeSpan RetryAfter)`
  with `SwitchVerdict { Switched, Unknown, TooSoon }`.
- **The switch never sleeps.** A second switch inside `MinSwitchInterval` answers `429` with a `Retry-After`
  header instead of holding the request open: the gateway must stay answerable, and the caller (the slot
  neuron) is the one that decides how long to wait.
- Produces `ActiveSlotRow.ReadOwnerAsync(TableServiceClient, ILogger, CancellationToken)` → the `Owner` of
  the `DigitalBrainLeases` row, or null when the table, the row or the connection is not there.
- Routes: `POST /switch/{slot}` and `GET /active` are local; everything else, `/health` included, is
  proxied to the active slot's cluster. `/switch` and `/active` win over YARP's `{**catch-all}` because
  ASP.NET Core prefers literal segments, whatever the registration order (spike S4).
- Adds `<PackageVersion Include="Yarp.ReverseProxy" Version="2.3.0" />` and the project to `DigitalBrain.slnx`
  under a new `/Gateway/` folder.

- [ ] **Step 1: Write the failing gateway facts**

```csharp
// tests/DigitalBrain.Tests/Features/Slots/GatewayFacts.cs
using System.Net;
using System.Text.Json;
using DigitalBrain.Gateway;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Xunit;

namespace DigitalBrain.Tests.Slots;

// Two slot backends and the gateway, all in this process on loopback ports: the switch, /active, the
// health passthrough and an SSE stream that must arrive event by event rather than at the end.
public sealed class GatewayFacts
{
    [Fact]
    public async Task The_switch_moves_every_later_request_to_the_other_slot()
    {
        await using var slotA = await BackendAsync("a");
        await using var slotB = await BackendAsync("b");
        await using var gateway = await GatewayAsync(slotA, slotB);
        using var client = new HttpClient { BaseAddress = new Uri(Address(gateway)) };

        Assert.Equal("a", await ActiveAsync(client));
        Assert.Equal("ok:a", await client.GetStringAsync("/health", TestContext.Current.CancellationToken));

        using var switched = await client.PostAsync("/switch/b", content: null, TestContext.Current.CancellationToken);
        switched.EnsureSuccessStatusCode();

        Assert.Equal("b", await ActiveAsync(client));
        Assert.Equal("ok:b", await client.GetStringAsync("/health", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Switching_to_the_slot_that_is_already_active_is_accepted_and_changes_nothing()
    {
        await using var slotA = await BackendAsync("a");
        await using var slotB = await BackendAsync("b");
        await using var gateway = await GatewayAsync(slotA, slotB);
        using var client = new HttpClient { BaseAddress = new Uri(Address(gateway)) };

        using var switched = await client.PostAsync("/switch/a", content: null, TestContext.Current.CancellationToken);
        switched.EnsureSuccessStatusCode();
        Assert.Equal("a", await ActiveAsync(client));
    }

    [Fact]
    public async Task An_unknown_slot_is_refused()
    {
        await using var slotA = await BackendAsync("a");
        await using var slotB = await BackendAsync("b");
        await using var gateway = await GatewayAsync(slotA, slotB);
        using var client = new HttpClient { BaseAddress = new Uri(Address(gateway)) };

        using var refused = await client.PostAsync("/switch/c", content: null, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
        Assert.Equal("a", await ActiveAsync(client));
    }

    [Fact]
    public async Task The_configured_active_slot_decides_the_first_route()
    {
        await using var slotA = await BackendAsync("a");
        await using var slotB = await BackendAsync("b");
        await using var gateway = await GatewayAsync(slotA, slotB, active: "b");
        using var client = new HttpClient { BaseAddress = new Uri(Address(gateway)) };

        Assert.Equal("b", await ActiveAsync(client));
        Assert.Equal("ok:b", await client.GetStringAsync("/health", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task An_early_re_switch_is_answered_with_the_wait_it_owes()
    {
        await using var slotA = await BackendAsync("a");
        await using var slotB = await BackendAsync("b");
        // The product's own debounce, not the zero the other facts use.
        await using var gateway = await GatewayAsync(slotA, slotB, minSwitchInterval: "00:00:15");
        using var client = new HttpClient { BaseAddress = new Uri(Address(gateway)) };

        using var first = await client.PostAsync("/switch/b", content: null, TestContext.Current.CancellationToken);
        first.EnsureSuccessStatusCode();

        var clock = System.Diagnostics.Stopwatch.StartNew();
        using var early = await client.PostAsync("/switch/a", content: null, TestContext.Current.CancellationToken);
        clock.Stop();

        Assert.Equal(HttpStatusCode.TooManyRequests, early.StatusCode);
        // Answered, not held open: the caller owns the wait.
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(5), $"the gateway held the switch for {clock.Elapsed}");
        var retryAfter = Assert.Single(early.Headers.GetValues("Retry-After"));
        Assert.InRange(int.Parse(retryAfter, System.Globalization.CultureInfo.InvariantCulture), 1, 15);
        Assert.Equal("b", await ActiveAsync(client));
    }

    [Fact]
    public async Task An_event_stream_arrives_event_by_event_through_the_gateway()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var slotA = await BackendAsync("a", release);
        await using var slotB = await BackendAsync("b");
        await using var gateway = await GatewayAsync(slotA, slotB);
        using var client = new HttpClient { BaseAddress = new Uri(Address(gateway)), Timeout = TimeSpan.FromSeconds(30) };

        using var request = new HttpRequestMessage(HttpMethod.Get, "/stream");
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        response.EnsureSuccessStatusCode();
        await using var body = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        using var reader = new StreamReader(body);

        // The backend holds the rest of the stream until this line is read, so a gateway that buffered the
        // response would deadlock here instead of answering.
        Assert.Equal("data: {\"slot\":\"a\",\"seq\":1}", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
        Assert.Equal(string.Empty, await reader.ReadLineAsync(TestContext.Current.CancellationToken));
        release.SetResult();
        Assert.Equal("data: {\"slot\":\"a\",\"seq\":2}", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
        Assert.Equal(string.Empty, await reader.ReadLineAsync(TestContext.Current.CancellationToken));
        Assert.Equal("data: [DONE]", await reader.ReadLineAsync(TestContext.Current.CancellationToken));
    }

    private static async Task<string> ActiveAsync(HttpClient client)
    {
        using var document = JsonDocument.Parse(await client.GetStringAsync("/active", TestContext.Current.CancellationToken));
        return document.RootElement.GetProperty("active").GetString()!;
    }

    private static string Address(WebApplication app) => app.Urls.First();

    // The stream endpoint holds its second half until the fact that owns this source releases it, so
    // "the bytes arrived before the response ended" is an assertion rather than a timing guess.
    private static async Task<WebApplication> BackendAsync(string slot, TaskCompletionSource? release = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        var app = builder.Build();
        app.MapGet("/health", () => Results.Text("ok:" + slot));
        app.MapGet("/stream", async (HttpContext http) =>
        {
            http.Response.ContentType = "text/event-stream";
            await http.Response.WriteAsync($"data: {{\"slot\":\"{slot}\",\"seq\":1}}\n\n", http.RequestAborted);
            await http.Response.Body.FlushAsync(http.RequestAborted);
            if (release is not null)
            {
                await release.Task.WaitAsync(TimeSpan.FromSeconds(20), http.RequestAborted);
            }

            await http.Response.WriteAsync($"data: {{\"slot\":\"{slot}\",\"seq\":2}}\n\n", http.RequestAborted);
            await http.Response.WriteAsync("data: [DONE]\n\n", http.RequestAborted);
            await http.Response.Body.FlushAsync(http.RequestAborted);
        });
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static async Task<WebApplication> GatewayAsync(
        WebApplication slotA, WebApplication slotB, string active = "a", string minSwitchInterval = "00:00:00")
    {
        var app = await GatewayApplication.CreateAsync(
        [
            "--urls=http://127.0.0.1:0",
            "--DigitalBrain:Gateway:Slots:a=" + Address(slotA),
            "--DigitalBrain:Gateway:Slots:b=" + Address(slotB),
            "--DigitalBrain:Gateway:Active=" + active,
            "--DigitalBrain:Gateway:MinSwitchInterval=" + minSwitchInterval,
        ]);
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Slots.GatewayFacts`
Expected: the build fails with CS0246 for `DigitalBrain.Gateway` — the project does not exist yet.

- [ ] **Step 3: The project**

`Directory.Packages.props`, in the Aspire/runtime `ItemGroup`:

```xml
    <PackageVersion Include="Yarp.ReverseProxy" Version="2.3.0" />
```

```xml
<!-- src/Gateway/DigitalBrain.Gateway/DigitalBrain.Gateway.csproj -->
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <!-- Endpoint lambdas have no synchronization context to capture, as in the silo. -->
    <NoWarn>$(NoWarn);CA2007;CA1812</NoWarn>
    <RootNamespace>DigitalBrain.Gateway</RootNamespace>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Yarp.ReverseProxy" />
    <PackageReference Include="Aspire.Azure.Data.Tables" />
    <PackageReference Include="Azure.Data.Tables" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../Aspire/DigitalBrain.ServiceDefaults/DigitalBrain.ServiceDefaults.csproj" />
    <ProjectReference Include="../../Kernel/DigitalBrain.Contracts/DigitalBrain.Contracts.csproj" />
  </ItemGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="DigitalBrain.Tests" />
  </ItemGroup>
</Project>
```

`DigitalBrain.slnx`, a new folder next to `/Testing/`:

```xml
  <Folder Name="/Gateway/">
    <Project Path="src/Gateway/DigitalBrain.Gateway/DigitalBrain.Gateway.csproj" />
  </Folder>
```

`tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj`, one more project reference:

```xml
    <ProjectReference Include="../../src/Gateway/DigitalBrain.Gateway/DigitalBrain.Gateway.csproj" />
```

- [ ] **Step 4: The router, the options, the row and the composition**

```csharp
// src/Gateway/DigitalBrain.Gateway/GatewayOptions.cs
using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Gateway;

internal sealed record GatewayOptions(
    IReadOnlyDictionary<string, string> Slots,
    string Active,
    TimeSpan MinSwitchInterval)
{
    private const string Section = "DigitalBrain:Gateway";

    internal static GatewayOptions From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(Section);
        var slots = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["a"] = Text(section["Slots:a"], "http://localhost:5081"),
            ["b"] = Text(section["Slots:b"], "http://localhost:5082"),
        };
        var active = Text(section["Active"], "a");
        return new GatewayOptions(
            slots,
            slots.ContainsKey(active) ? active : "a",
            TimeSpan.TryParse(section["MinSwitchInterval"], CultureInfo.InvariantCulture, out var interval)
                ? interval
                : TimeSpan.FromSeconds(15));
    }

    private static string Text(string? configured, string fallback)
        => configured is { Length: > 0 } value ? value : fallback;
}
```

```csharp
// src/Gateway/DigitalBrain.Gateway/SlotRouter.cs
using Microsoft.Extensions.Logging;
using Yarp.ReverseProxy.Configuration;

namespace DigitalBrain.Gateway;

// One route, two clusters. A switch rebuilds the route with the other cluster's id and hands YARP a new
// snapshot: requests already routed finish against the destination they were routed to, and new ones go to
// the new slot (spike S4). YARP rebuilds its whole route table per update, so switches are debounced.
internal sealed class SlotRouter
{
    private const string RouteId = "active-slot";

    private readonly GatewayOptions _options;
    private readonly TimeProvider _clock;
    private readonly ILogger<SlotRouter> _logger;
    private readonly ClusterConfig[] _clusters;
    private readonly Lock _gate = new();
    private string _active;
    private DateTimeOffset _switchedAt = DateTimeOffset.MinValue;

    public SlotRouter(GatewayOptions options, TimeProvider clock, ILogger<SlotRouter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(logger);
        _options = options;
        _clock = clock;
        _logger = logger;
        _clusters = [.. options.Slots.Select(static slot => new ClusterConfig
        {
            ClusterId = ClusterFor(slot.Key),
            Destinations = new Dictionary<string, DestinationConfig>(StringComparer.OrdinalIgnoreCase)
            {
                ["slot"] = new DestinationConfig { Address = slot.Value },
            },
        })];
        _active = options.Active;
        Provider = new InMemoryConfigProvider([RouteFor(_active)], _clusters);
    }

    public InMemoryConfigProvider Provider { get; }

    public string Active => Volatile.Read(ref _active);

    public TimeSpan MinSwitchInterval => _options.MinSwitchInterval;

    public bool Knows(string slot) => _options.Slots.ContainsKey(slot);

    // Used once at startup, before the server accepts anything, so it needs no debounce and no lock.
    public void Adopt(string slot)
    {
        if (!Knows(slot) || string.Equals(_active, slot, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Volatile.Write(ref _active, slot);
        Provider.Update([RouteFor(slot)], _clusters);
    }

    // Nothing here waits: a gateway that sleeps inside a request is a gateway that stops answering. An
    // early re-switch is reported back with the time left, and the caller decides what to do with it.
    public SwitchResult Switch(string slot)
    {
        if (!Knows(slot))
        {
            return new SwitchResult(SwitchVerdict.Unknown, TimeSpan.Zero);
        }

        lock (_gate)
        {
            if (string.Equals(_active, slot, StringComparison.OrdinalIgnoreCase))
            {
                return new SwitchResult(SwitchVerdict.Switched, TimeSpan.Zero);
            }

            var since = _clock.GetUtcNow() - _switchedAt;
            if (since < _options.MinSwitchInterval)
            {
                // YARP rebuilds its whole route table per update and documents one update per 15s as the
                // ceiling (spike S4).
                return new SwitchResult(SwitchVerdict.TooSoon, _options.MinSwitchInterval - since);
            }

            Volatile.Write(ref _active, slot);
            _switchedAt = _clock.GetUtcNow();
            Provider.Update([RouteFor(slot)], _clusters);
            _logger.LogInformation("The active slot is now {Slot}.", slot);
            return new SwitchResult(SwitchVerdict.Switched, TimeSpan.Zero);
        }
    }

    private static RouteConfig RouteFor(string slot) => new()
    {
        RouteId = RouteId,
        ClusterId = ClusterFor(slot),
        Match = new RouteMatch { Path = "{**catch-all}" },
    };

    private static string ClusterFor(string slot) => "slot-" + slot;
}

internal enum SwitchVerdict
{
    Switched = 0,
    Unknown = 1,
    TooSoon = 2,
}

internal sealed record SwitchResult(SwitchVerdict Verdict, TimeSpan RetryAfter);
```

```csharp
// src/Gateway/DigitalBrain.Gateway/ActiveSlotRow.cs
using Azure;
using Azure.Data.Tables;
using DigitalBrain.Abstractions.Slots;
using Microsoft.Extensions.Logging;

namespace DigitalBrain.Gateway;

// The kernel's lease row is the truth about which slot is live; the gateway reads it once at startup so a
// restart of the gateway alone does not send traffic to the slot that was replaced.
internal static class ActiveSlotRow
{
    internal static async Task<string?> ReadOwnerAsync(TableServiceClient tables, ILogger logger, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(tables);
        ArgumentNullException.ThrowIfNull(logger);
        try
        {
            var response = await tables.GetTableClient(ActiveSlotNames.Table)
                .GetEntityAsync<TableEntity>(ActiveSlotNames.PartitionKey, ActiveSlotNames.RowKey, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            return response.Value.GetString(ActiveSlotNames.Owner);
        }
        catch (RequestFailedException error) when (error.Status == 404)
        {
            // Nothing has been promoted yet; configuration decides.
            return null;
        }
        catch (RequestFailedException error)
        {
            logger.LogWarning(error, "The active-slot lease row could not be read; the configured slot stays active.");
            return null;
        }
    }
}
```

```csharp
// src/Gateway/DigitalBrain.Gateway/GatewayApplication.cs
using System.Globalization;
using Azure.Data.Tables;
using DigitalBrain.Abstractions;
using DigitalBrain.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Yarp.ReverseProxy.Configuration;

namespace DigitalBrain.Gateway;

internal static class GatewayApplication
{
    internal static async Task<WebApplication> CreateAsync(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.AddServiceDefaults();
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddSingleton(GatewayOptions.From(builder.Configuration));
        builder.Services.AddSingleton<SlotRouter>();
        builder.Services.AddSingleton<IProxyConfigProvider>(static services => services.GetRequiredService<SlotRouter>().Provider);
        builder.Services.AddReverseProxy();
        var clustering = builder.Configuration.GetConnectionString(DigitalBrainNames.Clustering);
        if (!string.IsNullOrWhiteSpace(clustering))
        {
            builder.AddKeyedAzureTableServiceClient(DigitalBrainNames.Clustering);
        }

        var app = builder.Build();
        var router = app.Services.GetRequiredService<SlotRouter>();
        if (!string.IsNullOrWhiteSpace(clustering)
            && app.Services.GetKeyedService<TableServiceClient>(DigitalBrainNames.Clustering) is { } tables
            && await ActiveSlotRow.ReadOwnerAsync(tables, app.Logger, app.Lifetime.ApplicationStopping).ConfigureAwait(false) is { Length: > 0 } owner)
        {
            router.Adopt(owner);
        }

        // /health belongs to the active slot, so the gateway maps no health endpoint of its own: a literal
        // route would shadow the kernel's and report the proxy instead of the product.
        // [FromServices] on purpose: a complex parameter on a POST would otherwise be a candidate for the
        // request body.
        app.MapPost("/switch/{slot}", static (string slot, [FromServices] SlotRouter slots, HttpResponse response) =>
        {
            var result = slots.Switch(slot);
            switch (result.Verdict)
            {
                case SwitchVerdict.Unknown:
                    return Results.BadRequest(new { error = $"'{slot}' is not a configured slot." });
                case SwitchVerdict.TooSoon:
                    // Answer at once with the wait the caller owes; never hold the request open.
                    var seconds = Math.Max(1, (int)Math.Ceiling(result.RetryAfter.TotalSeconds));
                    response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
                    return Results.Json(
                        new { error = $"the active slot changed less than {slots.MinSwitchInterval.TotalSeconds:0}s ago", retryAfterSeconds = seconds },
                        statusCode: StatusCodes.Status429TooManyRequests);
                default:
                    return Results.Ok(new { active = slots.Active });
            }
        });
        app.MapGet("/active", static ([FromServices] SlotRouter slots) => Results.Ok(new { active = slots.Active }));
        app.MapReverseProxy();
        return app;
    }
}
```

```csharp
// src/Gateway/DigitalBrain.Gateway/Program.cs
using DigitalBrain.Gateway;

var app = await GatewayApplication.CreateAsync(args);
await app.RunAsync();
```

- [ ] **Step 5: Run the gateway facts**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Slots.GatewayFacts`
Expected: 6 passed. If `An_event_stream_arrives_event_by_event_through_the_gateway` hangs until the 30 s
client timeout, YARP is buffering the response: check that no route sets `AllowResponseBuffering` (nothing
here does; YARP disables buffering by default, spike S4). If `/switch/c` answers 404 instead of 400, the
literal route lost to the catch-all — assert the route order in `GatewayApplication`. If
`An_early_re_switch_is_answered_with_the_wait_it_owes` takes fifteen seconds, the handler is waiting the
interval out instead of reporting it.
Then the full gate.

- [ ] **Step 6: Commit**

```bash
git add Directory.Packages.props DigitalBrain.slnx src/Gateway tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj tests/DigitalBrain.Tests/Features/Slots/GatewayFacts.cs
git commit -m "coding: the slot gateway with a switchable catch-all route on 5080"
```

---

### Task 8: The AppHost: a gateway on 5080 and two kernels behind it

**Files:**
- Modify: `src/Aspire/DigitalBrain.AppHost/AppHost.cs`,
  `src/Aspire/DigitalBrain.AppHost/ProductSurfaceResources.cs`,
  `src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`
- Create: `tests/DigitalBrain.Tests/Features/Slots/SlotConfigurationFacts.cs`

**Interfaces:**
- Produces `ProductSurfaceResources`: `Brain = "brain"`, `Gateway = "gateway"`, `KernelA = "kernel-a"`,
  `KernelB = "kernel-b"`, `GatewayHttpPort = 5080`, `KernelAHttpPort = 5081`, `KernelBHttpPort = 5082`,
  `GatewayUrl`, `KernelAUrl`, `KernelBUrl`. `Kernel` and `UiHttpPort` are gone; nothing outside this
  AppHost used them.
- Produces the environment contract between the AppHost and the silo (double underscores on the AppHost
  side, colons in configuration): `DigitalBrain__Slot`, `DigitalBrain__Slots__Gateway`,
  `DigitalBrain__Slots__ArtifactsRoot`, `DigitalBrain__Slots__a__Url`, `DigitalBrain__Slots__b__Url`,
  `DigitalBrain__Gateway__Slots__a`, `DigitalBrain__Gateway__Slots__b`, `DigitalBrain__Gateway__Active`.
  No `Orleans__ClusterId`: each slot mints its own per start (Task 2). `ArtifactsRoot` is the AppHost's own
  repository root plus `artifacts`, so the path the AppHost starts `kernel-b` from and the path the slot
  builder writes to cannot drift apart.
- Produces the gateway's presence in the AppHost's generated `Projects` class, which exists only because
  the AppHost project references the gateway project.
- The gateway resource is declared **before** the kernels because the UI module's projection binds
  `DIGITALBRAIN_UI_BASE` to the first resource that takes `WithReference(brain)`, and the shell must
  address 5080.

- [ ] **Step 1: Write the failing configuration facts**

```csharp
// tests/DigitalBrain.Tests/Features/Slots/SlotConfigurationFacts.cs
using DigitalBrain.Coding;
using DigitalBrain.Gateway;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace DigitalBrain.Tests.Slots;

// The AppHost sets environment variables; the silo and the gateway read configuration keys. These facts
// pin that translation, so renaming a key on one side fails here instead of at a promotion.
public sealed class SlotConfigurationFacts
{
    // Exactly the names src/Aspire/DigitalBrain.AppHost/AppHost.cs sets on kernel-a, kernel-b and gateway.
    private static readonly (string Variable, string Value)[] AppHostEnvironment =
    [
        ("DigitalBrain__Slot", "b"),
        ("DigitalBrain__Slots__Gateway", "http://localhost:5080"),
        ("DigitalBrain__Slots__ArtifactsRoot", "E:/repo/artifacts"),
        ("DigitalBrain__Slots__a__Url", "http://localhost:5081"),
        ("DigitalBrain__Slots__b__Url", "http://localhost:5082"),
        ("DigitalBrain__Gateway__Slots__a", "http://localhost:5081"),
        ("DigitalBrain__Gateway__Slots__b", "http://localhost:5082"),
        ("DigitalBrain__Gateway__Active", "a"),
    ];

    private static IConfiguration Configuration() => new ConfigurationBuilder()
        .AddInMemoryCollection(AppHostEnvironment.Select(entry =>
            new KeyValuePair<string, string?>(entry.Variable.Replace("__", ":", StringComparison.Ordinal), entry.Value)))
        .Build();

    [Fact]
    public void The_silo_reads_the_slot_the_app_host_gave_it()
    {
        var options = SlotOptions.From(Configuration());
        Assert.Equal("b", options.Slot);
        Assert.Equal("http://localhost:5080", options.GatewayUrl);
        Assert.Equal("http://localhost:5081", options.UrlFor("a"));
        Assert.Equal("http://localhost:5082", options.UrlFor("b"));
        Assert.Equal("kernel-a", options.ResourceFor("a"));
        Assert.Equal("kernel-b", options.ResourceFor("b"));
        // The AppHost names the output root, so the standby dll it starts and the tree the builder writes
        // are the same place whatever directory the silo happens to run in.
        Assert.Equal("E:/repo/artifacts/slot-b", options.ArtifactsFor("b", "E:/somewhere/else").Replace('\\', '/'));
    }

    [Fact]
    public void The_gateway_reads_both_slot_addresses_and_the_first_active_slot()
    {
        var options = GatewayOptions.From(Configuration());
        Assert.Equal("http://localhost:5081", options.Slots["a"]);
        Assert.Equal("http://localhost:5082", options.Slots["b"]);
        Assert.Equal("a", options.Active);
        Assert.Equal(TimeSpan.FromSeconds(15), options.MinSwitchInterval);
    }

    [Fact]
    public void The_slot_addresses_the_two_sides_agree_on_are_the_same()
    {
        var silo = SlotOptions.From(Configuration());
        var gateway = GatewayOptions.From(Configuration());
        foreach (var slot in SlotOptions.Names)
        {
            Assert.Equal(silo.UrlFor(slot), gateway.Slots[slot]);
        }
    }
}
```

- [ ] **Step 2: Run them**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Slots.SlotConfigurationFacts`
Expected: 3 passed. This is the one task whose facts cannot fail first: they read the options types Tasks 5
and 7 already produced, and their job is to pin the environment-variable names the AppHost must use, so a
later rename on either side fails here. A failure now means the key translation in `SlotOptions.From` or
`GatewayOptions.From` disagrees with the double-underscore names above — fix that before touching the
AppHost. The AppHost side is verified by the build in Step 6 and by the controller's live run.

- [ ] **Step 3: Reference the gateway from the AppHost**

`Projects.DigitalBrain_Gateway` is generated only for projects the AppHost project references; solution
membership is not enough. In `src/Aspire/DigitalBrain.AppHost/DigitalBrain.AppHost.csproj`, next to the
silo's reference:

```xml
    <ProjectReference Include="../../Gateway/DigitalBrain.Gateway/DigitalBrain.Gateway.csproj" />
```

No `IsAspireProjectResource="false"`: this reference exists precisely so the gateway becomes an Aspire
project resource.

- [ ] **Step 4: The resource names and ports**

```csharp
// src/Aspire/DigitalBrain.AppHost/ProductSurfaceResources.cs
internal static class ProductSurfaceResources
{
    public const string Brain = "brain";

    // The gateway owns the one address the shell, .mcp.json and every MCP client use; the two slots of the
    // silo sit behind it and are never addressed directly.
    public const string Gateway = "gateway";

    public const string KernelA = "kernel-a";

    public const string KernelB = "kernel-b";

    public const int GatewayHttpPort = 5080;

    public const int KernelAHttpPort = 5081;

    public const int KernelBHttpPort = 5082;

    public const string GatewayUrl = "http://localhost:5080";

    public const string KernelAUrl = "http://localhost:5081";

    public const string KernelBUrl = "http://localhost:5082";
}
```

- [ ] **Step 5: The three resources**

In `src/Aspire/DigitalBrain.AppHost/AppHost.cs`, replace everything from the `developmentClusterId`
comment to `builder.Build().Run();` with:

```csharp
// Both slots run the same silo against one ServiceId, so grain state, journals and reminders survive a
// swap. Neither gets an Orleans__ClusterId: the silo mints "{slot}-{timestamp}" per start, because a
// reused cluster id makes the new silo stall on the previous one's dead membership row (R5.3, spike S2).
var repositoryRoot = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "..", "..", ".."));
var standbyDll = Path.Combine(repositoryRoot, "artifacts", "slot-b", "bin", "DigitalBrain.Silo", "release", "DigitalBrain.Silo.dll");

// Declared before the kernels on purpose: the UI module's projection binds DIGITALBRAIN_UI_BASE to the
// first resource that takes the brain reference, and the shell must talk to the gateway, never to a slot.
var gateway = builder.AddProject<Projects.DigitalBrain_Gateway>(ProductSurfaceResources.Gateway)
    .WithReference(brain)
    .WithHttpEndpoint(
        port: ProductSurfaceResources.GatewayHttpPort,
        name: "http",
        isProxied: false)
    .WithEnvironment("DigitalBrain__Gateway__Slots__a", ProductSurfaceResources.KernelAUrl)
    .WithEnvironment("DigitalBrain__Gateway__Slots__b", ProductSurfaceResources.KernelBUrl)
    .WithEnvironment("DigitalBrain__Gateway__Active", "a")
    // Its own liveness, not the product's: /health is proxied to whichever slot is active.
    .WithHttpHealthCheck("/active", endpointName: "http");

var kernelA = builder.AddProject<Projects.DigitalBrain_Silo>(ProductSurfaceResources.KernelA)
    .WithReference(brain)
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION", "false")
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION", "false")
    .WithEnvironment("DigitalBrain__Slot", "a")
    .WithEnvironment("DigitalBrain__Slots__Gateway", ProductSurfaceResources.GatewayUrl)
    // The same root the standby dll above is taken from: the builder and the AppHost must not drift.
    .WithEnvironment("DigitalBrain__Slots__ArtifactsRoot", Path.Combine(repositoryRoot, "artifacts"))
    .WithEnvironment("DigitalBrain__Slots__a__Url", ProductSurfaceResources.KernelAUrl)
    .WithEnvironment("DigitalBrain__Slots__b__Url", ProductSurfaceResources.KernelBUrl)
    .WithHttpEndpoint(
        port: ProductSurfaceResources.KernelAHttpPort,
        name: "http",
        isProxied: false)
    // Without this, "kernel healthy" means only "process launched": Kestrel binds AFTER the
    // Orleans silo and brain activation finish, so waiters would proceed while the port still
    // refuses connections (observed on loaded CI runners).
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithUrlForEndpoint(
        "http",
        endpoint => new ResourceUrlAnnotation
        {
            Url = "/orleans",
            DisplayText = "Orleans Dashboard",
            Endpoint = endpoint,
        })
    .WithEnvironment(context =>
    {
        // The typed neuron surface (/mcp describe and call) is how Claude Code and Codex reach the coding tools.
        if (builder.ExecutionContext.IsRunMode)
        {
            context.EnvironmentVariables["DigitalBrain__Graph__Enabled"] = "true";
        }
    });

// The standby is an executable over its own build output: two AddProject resources from one project share
// one output, so they cannot differ by ArtifactsPath (spike S1). Build it with
// dotnet build DigitalBrain.slnx -c Release -p:ArtifactsPath=<repo>/artifacts/slot-b -p:UseArtifactsOutput=true
// before starting it; the slot neuron does exactly that.
var kernelB = builder.AddExecutable(ProductSurfaceResources.KernelB, "dotnet", repositoryRoot, standbyDll)
    .WithReference(brain)
    .WithExplicitStart()
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION", "false")
    .WithEnvironment("OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION", "false")
    .WithEnvironment("DigitalBrain__Slot", "b")
    .WithEnvironment("DigitalBrain__Slots__Gateway", ProductSurfaceResources.GatewayUrl)
    .WithEnvironment("DigitalBrain__Slots__ArtifactsRoot", Path.Combine(repositoryRoot, "artifacts"))
    .WithEnvironment("DigitalBrain__Slots__a__Url", ProductSurfaceResources.KernelAUrl)
    .WithEnvironment("DigitalBrain__Slots__b__Url", ProductSurfaceResources.KernelBUrl)
    .WithHttpEndpoint(
        port: ProductSurfaceResources.KernelBHttpPort,
        name: "http",
        isProxied: false)
    .WithHttpHealthCheck("/health", endpointName: "http")
    .WithEnvironment(context =>
    {
        if (builder.ExecutionContext.IsRunMode)
        {
            context.EnvironmentVariables["DigitalBrain__Graph__Enabled"] = "true";
        }
    });

// An executable gets no ASPNETCORE_URLS from WithHttpEndpoint, and a proxied endpoint's address is the
// proxy's own, which the process then collides with: both halves of spike S1's fix.
kernelB.WithEnvironment("ASPNETCORE_URLS", kernelB.GetEndpoint("http"));

// The gateway proxies to kernel-a first, so it waits for it rather than answering 502 to the shell.
gateway.WaitFor(kernelA);

builder.Build().Run();
```

If `WithReference(brain)` does not compile for `IResourceBuilder<ExecutableResource>`, relax the
constraint in `src/Aspire/DigitalBrain.Aspire.Hosting/Brain/DigitalBrainHostingExtensions.cs` from
`where TResource : IResourceWithEnvironment, IResourceWithEndpoints` to
`where TResource : IResourceWithEnvironment` on that one overload: its body only sets environment
variables and annotations. Aspire's own docs show `AddExecutable(...).WithReference(resource)`, and
`WithHttpEndpoint` on an executable already requires `IResourceWithEndpoints`, so the compile is expected
to pass unchanged.

- [ ] **Step 6: Run the facts and build the AppHost**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Slots.SlotConfigurationFacts`
Expected: 3 passed. Then `dotnet build DigitalBrain.slnx -c Release`.
Expected: warning-free, including the AppHost. A CS0117 "`Projects` does not contain
`DigitalBrain_Gateway`" means Step 3's project reference is missing or was added with
`IsAspireProjectResource="false"`, which suppresses the generated type.
Then the full gate.

- [ ] **Step 7: Commit**

```bash
git add src/Aspire/DigitalBrain.AppHost tests/DigitalBrain.Tests/Features/Slots/SlotConfigurationFacts.cs
git commit -m "coding: the gateway on 5080 with kernel-a and an explicit-start kernel-b behind it"
```

---

### Task 9: `code_promote`

**Files:**
- Create: `tests/DigitalBrain.Tests/Features/Coding/SlotToolFacts.cs`
- Modify: `src/Modules/Coding/Coding/CodingNativeTools.cs`, `CodingModule.cs`,
  `src/Modules/AI/AI/ConversationalAgent.cs`,
  `tests/DigitalBrain.Tests/Features/Coding/CodingNativeToolFacts.cs`

**Interfaces:**
- Produces the tool `code_promote(slot = "b", changeId = null, promoteOnly = false)`: it reads the
  repository's HEAD commit as the generation the slot is built from, builds the named slot (unless
  `promoteOnly`), waits for the build to settle, promotes on success, and returns
  `{ slot, status, generation, artifactsPath, healthy, holdsLease, rollbackAllowed, errors, detail, advice }`.
  A broken build returns `status: "Failed"` with the errors and the live slot untouched.
- Produces `CodingNativeTools.SettleAsync<TSnapshot>(Func<Task<TSnapshot>> read, Func<TSnapshot, bool> settled, string subject, Func<TSnapshot, string> describe, TimeSpan budget, CancellationToken)`;
  the three change-set call sites keep using a one-line `WaitAsync` wrapper over it. The slot calls use
  `SlotOptions.PromoteWait` (20 minutes) because a solution build is not a two-minute reaction.
- `CodingNativeTools`'s constructor gains `SlotOptions slots`; `CodingModule` registers `code_promote`;
  `ConversationalAgent`'s allowlist grows to fifteen `code_*` tools.
- `BuildSlot.Generation` is filled from `GitRunner.HeadCommitAsync` (Task 5) over the solution's directory
  — the commit the landing produced, since `code_commit` ran before this tool — and is null when the tree
  is not a git repository. `changeId` never reaches `Generation`: it is read only for
  `ChangeSetSnapshot.Files`, the list the rollback rule needs.
- The rollback policy lives here: promoting a slot that is still running is refused while the slot that
  holds the lease reports `Phase == Live` with `RollbackAllowed == false`.

- [ ] **Step 1: Write the failing tool facts**

```csharp
// tests/DigitalBrain.Tests/Features/Coding/SlotToolFacts.cs
using System.Text.Json;
using DigitalBrain.AI;
using DigitalBrain.AI.Interactions;
using DigitalBrain.Coding;
using DigitalBrain.Core;
using DigitalBrain.Microsoft;
using DigitalBrain.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.Tests.Coding;

// code_promote is the whole landing in one tool call: build the standby, promote it, report. The model
// never sees an exception, only a status and, on a broken build, the errors.
public sealed class SlotToolFacts
{
    private sealed record World(
        BrainSimulation Brain,
        NativeTools Tools,
        FakeSlotBuilder Builder,
        FakeSlotEndpoints Endpoints,
        FakeAspireResourceCommands Aspire,
        InMemoryActiveSlotLease Lease) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Brain.DisposeAsync();
    }

    private static async Task<World> StartAsync()
    {
        var builder = new FakeSlotBuilder();
        var endpoints = new FakeSlotEndpoints { ActiveSlot = "a" };
        var aspire = new FakeAspireResourceCommands();
        var lease = new InMemoryActiveSlotLease("a");
        var brain = await BrainSimulation.StartAsync(new()
        {
            Modules = new([typeof(AIModule), typeof(CodingModule)]),
            Configuration = new Dictionary<string, string?>
            {
                ["DigitalBrain:Slot"] = "a",
                ["DigitalBrain:Slots:Grace"] = "00:00:00",
            },
            ConfigureSilo = silo =>
            {
                silo.Services.AddSingleton<ISolutionLoader>(new AdhocSolutionLoader(FixtureSolutions.TwoProjects));
                silo.Services.AddSingleton<IUntrustedContentScreen, ScriptedContentScreen>();
                silo.Services.AddSingleton<ISlotBuilder>(builder);
                silo.Services.AddSingleton<ISlotEndpoints>(endpoints);
                silo.Services.AddSingleton<IAspireResourceCommands>(aspire);
                silo.Services.AddSingleton<IActiveSlotLease>(lease);
            },
        });
        return new World(brain, brain.SiloServices.GetRequiredService<NativeTools>(), builder, endpoints, aspire, lease);
    }

    private static async Task<JsonElement> PromoteAsync(World world, Dictionary<string, object?> arguments)
    {
        var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(world.Tools.Resolve(["code_promote"])));
        var result = await tool.InvokeAsync(new AIFunctionArguments(arguments), TestContext.Current.CancellationToken);
        return JsonSerializer.SerializeToElement(result);
    }

    [Fact]
    public async Task A_promote_builds_the_standby_switches_the_gateway_and_reports_live()
    {
        await using var world = await StartAsync();
        world.Endpoints.UnhealthyProbes = 1;

        var result = await PromoteAsync(world, new() { ["slot"] = "b" });

        Assert.Equal("Live", result.GetProperty("status").GetString());
        Assert.Equal("b", result.GetProperty("slot").GetString());
        Assert.True(result.GetProperty("rollbackAllowed").GetBoolean());
        Assert.Equal(JsonValueKind.Null, result.GetProperty("advice").ValueKind);
        // No solution is open in this silo, so there is no repository to read a generation from; the
        // promotion is not blocked by that (GitRunner.HeadCommitAsync has its own facts in Task 5).
        Assert.Equal(JsonValueKind.Null, result.GetProperty("generation").ValueKind);
        Assert.Equal("b", Assert.Single(world.Endpoints.Switches));
        Assert.Equal("b", world.Lease.Holder);
        Assert.Equal(("kernel-b", "start"), Assert.Single(world.Aspire.Executed));
    }

    [Fact]
    public async Task A_broken_build_returns_the_errors_and_leaves_the_live_slot_alone()
    {
        await using var world = await StartAsync();
        world.Builder.Outcome = new BuildOutcome(false,
            [new DiagnosticHit("CS0103", "Error", "The name 'Nope' does not exist", "E:/repo/src/A/Thing.cs", 12)],
            0, 3.0, "dotnet build", null);

        var result = await PromoteAsync(world, new() { ["slot"] = "b" });

        Assert.Equal("Failed", result.GetProperty("status").GetString());
        Assert.Equal("CS0103", result.GetProperty("errors")[0].GetProperty("id").GetString());
        Assert.Contains("the live slot is untouched", result.GetProperty("advice").GetString(), StringComparison.Ordinal);
        Assert.Empty(world.Endpoints.Switches);
        Assert.Equal("a", world.Lease.Holder);
    }

    [Fact]
    public async Task A_promote_with_an_uncommitted_change_set_is_advice_not_a_build()
    {
        await using var world = await StartAsync();

        var result = await PromoteAsync(world, new() { ["slot"] = "b", ["changeId"] = "never-committed" });

        Assert.Contains("commit it before promoting", result.GetProperty("advice").GetString(), StringComparison.Ordinal);
        Assert.Empty(world.Builder.Builds);
    }

    [Fact]
    public async Task A_rollback_is_refused_after_a_landing_that_changed_persisted_state()
    {
        await using var world = await StartAsync();
        world.Endpoints.UnhealthyProbes = 1;
        world.Builder.TouchesSerializedState = true;

        var promoted = await PromoteAsync(world, new() { ["slot"] = "b" });
        Assert.Equal("Live", promoted.GetProperty("status").GetString());
        Assert.False(promoted.GetProperty("rollbackAllowed").GetBoolean());

        var refused = await PromoteAsync(world, new() { ["slot"] = "a", ["promoteOnly"] = true });

        Assert.Contains("forward-only", refused.GetProperty("advice").GetString(), StringComparison.Ordinal);
        Assert.Equal("b", world.Lease.Holder);
        Assert.Equal(["b"], world.Endpoints.Switches);
    }

    [Fact]
    public async Task An_unknown_slot_is_advice()
    {
        await using var world = await StartAsync();
        var result = await PromoteAsync(world, new() { ["slot"] = "c" });
        Assert.Contains("is not a slot", result.GetProperty("advice").GetString(), StringComparison.Ordinal);
        Assert.Empty(world.Builder.Builds);
    }
}
```

In `tests/DigitalBrain.Tests/Features/Coding/CodingNativeToolFacts.cs`, rename and extend the count fact:

```csharp
    [Fact]
    public async Task The_fifteen_tools_resolve()
    {
        var (brain, tools, fixture, _) = await StartAsync();
        using var fixtureScope = fixture;
        await using var brainScope = brain;
        Assert.Equal(15, tools.Resolve(["code_find_symbols", "code_references", "code_diagnostics", "code_map", "code_skeleton", "code_member", "code_callers",
            "code_implementations", "code_derived", "code_propose_edit", "code_check", "code_commit", "code_build", "code_test", "code_promote"]).Count());
    }
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.SlotToolFacts`
Expected: every fact fails at `Assert.Single(world.Tools.Resolve(["code_promote"]))` — the tool does not
exist, so `Resolve` yields nothing. `The_fifteen_tools_resolve` fails with 14.

- [ ] **Step 3: The generic settle**

In `src/Modules/Coding/Coding/CodingNativeTools.cs`, replace the `WaitAsync` method with the generic
settle plus a wrapper, so the three change-set call sites are untouched:

```csharp
    // Commands return at once; the reaction that does the work saves a new snapshot, which is what the
    // model needs. Every reaction that saves bumps Revision, so waiting for Revision to move past the
    // pre-command snapshot always recognizes this command's own settle, never a leftover Detail/Status a
    // previous command already produced (Detail is a pure function of the edit list and can repeat verbatim
    // across two otherwise-unrelated reactions).
    // The deadline is internal bookkeeping, not a real cancellation: when it fires (and the caller did not
    // itself cancel), it becomes advice instead of an OperationCanceledException escaping GuardedAsync.
    private async Task<TSnapshot> SettleAsync<TSnapshot>(
        Func<Task<TSnapshot>> read,
        Func<TSnapshot, bool> settled,
        string subject,
        Func<TSnapshot, string> describe,
        TimeSpan budget,
        CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(budget);
        TSnapshot? last = default;
        // A reaction that settles at once is usually caught by the first or second poll; the backoff keeps a
        // long check, commit or build from costing hundreds of grain reads while it runs.
        var poll = TimeSpan.FromMilliseconds(20);
        try
        {
            while (true)
            {
                // Read() takes no token of its own, and a genuinely stuck reaction holds the grain's one turn,
                // queuing Read() behind it too - bounding the call from our own side is what lets the deadline
                // fire at all in that case, instead of Orleans' own much longer request timeout dominating it.
                last = await read().WaitAsync(timeout.Token).ConfigureAwait(false);
                if (settled(last))
                {
                    return last;
                }

                await Task.Delay(poll, timeout.Token).ConfigureAwait(false);
                poll = TimeSpan.FromMilliseconds(Math.Min(500, poll.TotalMilliseconds * 2));
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(
                $"{subject} did not settle within {budget.TotalSeconds:0}s; {(last is null ? "it answered nothing" : describe(last))}. Read it again or start over.");
        }
    }

    private Task<ChangeSetSnapshot> WaitAsync(IChangeSet changeSet, ChangeSetSnapshot before, CancellationToken cancellationToken)
        => SettleAsync(changeSet.Read, snapshot => snapshot.Revision > before.Revision, "the change set",
            static snapshot => $"its status is still {snapshot.Status}", _options.ReactionWait, cancellationToken);
```

- [ ] **Step 4: The tool**

Constructor and field:

```csharp
    private readonly CodingToolOptions _options;
    private readonly SlotOptions _slots;
```

```csharp
    public CodingNativeTools(SolutionWorkspace workspace, IGrainFactory grains, DotnetRunner dotnet, GitRunner git, IConfiguration configuration, CodingToolOptions options, SlotOptions slots)
    {
        _workspace = workspace;
        _grains = grains;
        _dotnet = dotnet;
        _git = git;
        _configuration = configuration;
        _options = options;
        _slots = slots;
        _functions = new(Build);
    }
```

One more entry at the end of `Build()`:

```csharp
        AIFunctionFactory.Create(Promote, new AIFunctionFactoryOptions
        {
            Name = "code_promote",
            Description = "Build the standby kernel slot and promote it: the gateway switches to the new slot and the old one is fenced. "
                + "A broken build returns its errors and leaves the live slot untouched. Run code_commit first so the landing is on disk.",
        }),
```

The grain accessor and the tool:

```csharp
    private ISlot Slot(string slot)
        => _grains.GetGrain<ISlot>(new NeuronId(CodingVocabulary.SlotType, slot).ToGrainId());

    private Task<JsonElement> Promote(
        [Description("The slot to build and promote: a or b. Defaults to b, the standby")] string slot = "b",
        [Description("The change set this landing committed; its files decide whether a rollback stays allowed")] string? changeId = null,
        [Description("Promote a slot that is already running without building it first (a rollback)")] bool promoteOnly = false,
        CancellationToken cancellationToken = default)
        => GuardedAsync(async () =>
        {
            if (!SlotOptions.Names.Contains(slot, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"'{slot}' is not a slot. Name '{string.Join("' or '", SlotOptions.Names)}'.");
            }

            var target = Slot(slot);
            var snapshot = await target.Read().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (promoteOnly)
            {
                await RefuseForwardOnlyRollbackAsync(slot, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                IReadOnlyList<string> files = [];
                if (changeId is { Length: > 0 })
                {
                    var changeSet = await ChangeSet(changeId).Read().WaitAsync(cancellationToken).ConfigureAwait(false);
                    if (changeSet.Status != ChangeSetStatus.Committed)
                    {
                        throw new InvalidOperationException($"Change set '{changeId}' is {changeSet.Status}; commit it before promoting a slot.");
                    }

                    files = changeSet.Files;
                }

                // The generation is the commit the slot is built from (D3), which after code_commit is the
                // repository's HEAD; it is not the change set's id and not its numeric generation.
                var generation = await GenerationAsync(cancellationToken).ConfigureAwait(false);
                var beforeBuild = snapshot.Revision;
                await target.Build(new BuildSlot(CommandId.New(), generation, files)).ConfigureAwait(false);
                snapshot = await SettleAsync(target.Read, current => current.Revision > beforeBuild, $"slot '{slot}'",
                    static current => $"it is {current.Phase}", _slots.PromoteWait, cancellationToken).ConfigureAwait(false);
                if (snapshot.Phase != SlotPhase.Built)
                {
                    return Result(snapshot, snapshot.Detail ?? "the slot did not build");
                }
            }

            var beforePromote = snapshot.Revision;
            await target.Promote(new PromoteSlot(CommandId.New())).ConfigureAwait(false);
            var promoted = await SettleAsync(target.Read,
                current => current.Revision > beforePromote && current.Phase is SlotPhase.Live or SlotPhase.Failed,
                $"slot '{slot}'", static current => $"it is {current.Phase}", _slots.PromoteWait, cancellationToken).ConfigureAwait(false);
            return Result(promoted, promoted.Phase == SlotPhase.Live ? null : promoted.Detail ?? "the promotion failed");

            static object Result(SlotSnapshot snapshot, string? advice) => new
            {
                slot = snapshot.Slot,
                status = snapshot.Phase,
                generation = snapshot.Generation,
                artifactsPath = snapshot.ArtifactsPath,
                healthy = snapshot.Healthy,
                holdsLease = snapshot.HoldsLease,
                rollbackAllowed = snapshot.RollbackAllowed,
                errors = snapshot.Errors,
                detail = snapshot.Detail,
                advice,
            };
        });

    // Best effort: the generation records what was built, it never gates building it. A workspace that is
    // not open, a tree that is not a repository, or a missing git binary leaves it null.
    private async Task<string?> GenerationAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _git.HeadCommitAsync(Path.GetDirectoryName(SolutionPath())!, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // the generation is a record, so no failure of it may stop a promotion
        catch (Exception error) when (error is not OperationCanceledException)
#pragma warning restore CA1031
        {
            return null;
        }
    }

    // Rolling back is promoting the slot that is still running, and only the slot that is live now knows
    // whether its own landing changed a [GenerateSerializer] type (design 4.4). A neuron may not read
    // another neuron, but a tool may.
    private async Task RefuseForwardOnlyRollbackAsync(string slot, CancellationToken cancellationToken)
    {
        foreach (var other in SlotOptions.Names.Where(name => !string.Equals(name, slot, StringComparison.OrdinalIgnoreCase)))
        {
            var live = await Slot(other).Read().WaitAsync(cancellationToken).ConfigureAwait(false);
            if (live.Phase == SlotPhase.Live && !live.RollbackAllowed)
            {
                throw new InvalidOperationException(
                    $"Slot '{other}' is live with a landing that changed persisted state, so it is forward-only: build slot '{slot}' again instead of promoting it back.");
            }
        }
    }
```

- [ ] **Step 5: Register the tool and widen the agent's allowlist**

`src/Modules/Coding/Coding/CodingModule.cs`, in the tool-name array, after `"code_test"`:

```csharp
            "code_promote",
```

`src/Modules/AI/AI/ConversationalAgent.cs`, in the `Tools` allowlist, after `"code_test"`:

```csharp
"code_promote",
```

and one sentence in the instructions, right after the code-change paragraph:

```
                        To land a change in the running kernel, call code_promote after code_commit: it
                        builds the standby slot and switches the gateway to it. A Failed status carries the
                        build errors and the live kernel is untouched; never say a slot was promoted without
                        a Live status.
```

- [ ] **Step 6: Run the facts**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.SlotToolFacts`
Expected: 5 passed. Then `-- --filter-class DigitalBrain.Tests.Coding.CodingNativeToolFacts` (all pass,
fifteen tools) and the full gate. If `A_promote_builds_the_standby_switches_the_gateway_and_reports_live`
reports `Built` instead of `Live`, the second settle returned on the build's own revision: check that
`beforePromote` is read from the settled build snapshot, not from the pre-build one.

- [ ] **Step 7: Commit**

```bash
git add src/Modules/Coding src/Modules/AI/AI/ConversationalAgent.cs tests/DigitalBrain.Tests/Features/Coding
git commit -m "coding: code_promote builds and promotes a slot from the chat"
```

---

### Task 10: The shell survives the swap

**Files:**
- Create: `src/Kernel/DigitalBrain.Silo/Http/AgentRunLedger.cs`, `AgentRunRequest.cs`,
  `AgentAnswerTap.cs`, `AgentReplayResult.cs`,
  `tests/DigitalBrain.Tests/Features/AgentResumeFacts.cs`,
  `src/Modules/UI/Flutter/shell/test/workspace_chat_resume_test.dart`
- Modify: `src/Kernel/DigitalBrain.Silo/Http/AgentStreamResult.cs`,
  `ConversationalAgentEndpoints.cs`, `src/Modules/UI/Flutter/core/lib/src/agent_events.dart`,
  `core/lib/src/ui_client.dart`, `shell/lib/workspace/workspace_chat.dart`,
  `core/test/agent_stream_test.dart`, and the four shell test files that fake an `AgentRunner`:
  `shell/test/workspace_app_test.dart`, `workspace_attachment_draft_test.dart`,
  `workspace_chat_design_test.dart`, `workspace_tool_results_test.dart`

**Two commits:** the kernel half first (Steps 1-5, `AgentResumeFacts` green), then the Dart half
(Steps 6-9, the Flutter gate green), so a failure in one language does not hold the other hostage.

**Interfaces:**
- Produces `AgentRunLedger` (bounded: the last 16 runs, answers up to 64 KB) with
  `bool TryGetAnswer(string runId, out string? answer)` and `void Record(string runId, string answer)`,
  registered as a singleton by `AddConversationalAgent`.
- Produces `AgentRunRequest(string? ThreadId, string? RunId, bool Resume)` with
  `ReadAsync(HttpContext)`, which buffers the request body, reads the three fields and rewinds it for the
  AG-UI handler.
- Produces `AgentAnswerTap`, the write-through stream that collects `TEXT_MESSAGE_CONTENT` deltas and
  notices `RUN_FINISHED`, and `AgentReplayResult(threadId, runId, answer)`, which writes the minimal AG-UI
  sequence the shell consumes: `RUN_STARTED`, `TEXT_MESSAGE_START`, `TEXT_MESSAGE_CONTENT`,
  `TEXT_MESSAGE_END`, `RUN_FINISHED`.
- Produces the Dart contract change: `AgentRunner` and `DigitalBrainUiClient.runAgent` gain
  `bool resume` (default false), which becomes `forwardedProps: {"resume": true}`.
- The shell retries an interrupted run **once**: same `runId`, same prompt, `resume: true`, after 750 ms,
  having dropped the entries the failed attempt streamed.
- Not in this task, on purpose: the brain-observation stream already reconnects on its own
  (`BrainGraphStore._disconnected` retries every 2 s with a fresh whole snapshot), and the shell never
  needs `GET /active` because its base address is the gateway's, which is what routes to the live slot.

- [ ] **Step 1: Write the failing resume facts**

```csharp
// tests/DigitalBrain.Tests/Features/AgentResumeFacts.cs
using System.Net.Http.Json;
using System.Text.Json;
using DigitalBrain.Testing;
using DigitalBrain.UI;
using Microsoft.AspNetCore.TestHost;
using Xunit;

namespace DigitalBrain.Tests;

// Phase 2 spike S4: a slot that stops mid-answer leaves the client with a transport error and no terminal
// frame. The shell re-issues that run; this is the kernel's half of it.
public sealed class AgentResumeFacts
{
    [Fact]
    public async Task A_resumed_run_replays_the_answer_without_a_second_model_call()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(UIModule)]) });
        using var model = new ScriptedChatClient();
        model.Say("The rename landed on coding/rename-1.");
        await using var app = await TableAgentFacts.StartAsync(brain, model);
        using var client = app.GetTestClient();
        var threadId = Guid.NewGuid().ToString();
        var runId = Guid.NewGuid().ToString();

        var first = await PostAsync(client, threadId, runId, "Rename Greet to Hello", resume: false);
        Assert.Equal("The rename landed on coding/rename-1.", Answer(first));

        var resumed = await PostAsync(client, threadId, runId, "Rename Greet to Hello", resume: true);

        Assert.Equal(Answer(first), Answer(resumed));
        Assert.Equal("RUN_FINISHED", resumed[^1].GetProperty("type").GetString());
        // One model call for two posts: the reconnect was answered from the ledger.
        Assert.Single(model.Calls);
    }

    [Fact]
    public async Task A_resume_of_a_run_this_silo_never_saw_is_answered_as_a_fresh_run()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(UIModule)]) });
        using var model = new ScriptedChatClient();
        model.Say("Promoted slot b.");
        await using var app = await TableAgentFacts.StartAsync(brain, model);
        using var client = app.GetTestClient();

        // This is the promotion case: the new slot has neither the ledger nor the session of the old one.
        var resumed = await PostAsync(client, Guid.NewGuid().ToString(), Guid.NewGuid().ToString(), "Rebuild and promote", resume: true);

        Assert.Equal("Promoted slot b.", Answer(resumed));
        Assert.Single(model.Calls);
    }

    [Fact]
    public async Task A_repeated_run_without_the_resume_flag_is_answered_again_by_the_model()
    {
        await using var brain = await BrainSimulation.StartAsync(new() { Modules = new([typeof(UIModule)]) });
        using var model = new ScriptedChatClient();
        model.Say("first");
        model.Say("second");
        await using var app = await TableAgentFacts.StartAsync(brain, model);
        using var client = app.GetTestClient();
        var threadId = Guid.NewGuid().ToString();
        var runId = Guid.NewGuid().ToString();

        var first = await PostAsync(client, threadId, runId, "Say something", resume: false);
        var second = await PostAsync(client, threadId, runId, "Say something", resume: false);

        Assert.Equal("first", Answer(first));
        Assert.Equal("second", Answer(second));
        Assert.Equal(2, model.Calls.Count);
    }

    private static string Answer(List<JsonElement> events) => string.Concat(events
        .Where(item => item.GetProperty("type").GetString() == "TEXT_MESSAGE_CONTENT")
        .Select(item => item.GetProperty("delta").GetString()));

    private static async Task<List<JsonElement>> PostAsync(HttpClient client, string threadId, string runId, string text, bool resume)
    {
        using var response = await client.PostAsJsonAsync("/agent", new
        {
            threadId,
            runId,
            messages = new[] { new { id = Guid.NewGuid().ToString(), role = "user", content = text } },
            tools = Array.Empty<object>(),
            context = Array.Empty<object>(),
            state = new { },
            forwardedProps = resume
                ? new Dictionary<string, object> { ["resume"] = true }
                : new Dictionary<string, object>(),
        }, TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        Assert.True(response.IsSuccessStatusCode, body);
        return body.Split('\n').Where(line => line.StartsWith("data:", StringComparison.Ordinal))
            .Select(line => JsonSerializer.Deserialize<JsonElement>(line[5..])).ToList();
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.AgentResumeFacts`
Expected: `A_resumed_run_replays_the_answer_without_a_second_model_call` fails at `Assert.Single(model.Calls)`
with 2 calls (nothing replays yet) and its `Assert.Equal(Answer(first), Answer(resumed))` fails because the
scripted model has no second line; the other two facts pass already.

- [ ] **Step 3: The ledger, the request, the tap and the replay**

```csharp
// src/Kernel/DigitalBrain.Silo/Http/AgentRunLedger.cs
using System.Diagnostics.CodeAnalysis;

namespace DigitalBrain.Kernel;

// What a run already answered, so the shell's one reconnect costs no second model call and appends no
// second copy of the turn to the session. In-process and bounded on purpose: after a promotion the chat
// lands on a silo that never saw this run, and there the resume is simply a fresh run.
internal sealed class AgentRunLedger
{
    private const int Capacity = 16;
    private const int MaxAnswer = 64 * 1024;

    private readonly Lock _gate = new();
    private readonly Dictionary<string, string> _answers = new(StringComparer.Ordinal);
    private readonly Queue<string> _order = new();

    public bool TryGetAnswer(string runId, [NotNullWhen(true)] out string? answer)
    {
        lock (_gate)
        {
            return _answers.TryGetValue(runId, out answer);
        }
    }

    public void Record(string runId, string answer)
    {
        if (runId.Length == 0 || answer.Length == 0 || answer.Length > MaxAnswer)
        {
            return;
        }

        lock (_gate)
        {
            if (!_answers.TryAdd(runId, answer))
            {
                return;
            }

            _order.Enqueue(runId);
            while (_order.Count > Capacity)
            {
                _answers.Remove(_order.Dequeue());
            }
        }
    }
}
```

```csharp
// src/Kernel/DigitalBrain.Silo/Http/AgentRunRequest.cs
using System.Text.Json;

namespace DigitalBrain.Kernel;

internal sealed record AgentRunRequest(string? ThreadId, string? RunId, bool Resume)
{
    // The AG-UI handler reads the body itself, so this buffers and rewinds it.
    internal static async Task<AgentRunRequest?> ReadAsync(HttpContext http)
    {
        if (!HttpMethods.IsPost(http.Request.Method))
        {
            return null;
        }

        http.Request.EnableBuffering();
        try
        {
            using var document = await JsonDocument.ParseAsync(http.Request.Body, cancellationToken: http.RequestAborted);
            var root = document.RootElement;
            var resume = root.TryGetProperty("forwardedProps", out var forwarded)
                && forwarded.ValueKind == JsonValueKind.Object
                && forwarded.TryGetProperty("resume", out var flag)
                && flag.ValueKind == JsonValueKind.True;
            return new AgentRunRequest(Text(root, "threadId"), Text(root, "runId"), resume);
        }
        catch (JsonException)
        {
            // Not an AG-UI run body; let the handler answer it.
            return null;
        }
        finally
        {
            http.Request.Body.Position = 0;
        }
    }

    private static string? Text(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
```

```csharp
// src/Kernel/DigitalBrain.Silo/Http/AgentAnswerTap.cs
using System.Text;
using System.Text.Json;

namespace DigitalBrain.Kernel;

// A write-through stream that reads the assistant's text out of the AG-UI frames it forwards. Framing is
// done on bytes, because a chunk boundary can fall inside a UTF-8 rune.
internal sealed class AgentAnswerTap(Stream inner, AgentRunLedger ledger, string runId) : Stream
{
    private readonly StringBuilder _answer = new();
    private readonly List<byte> _line = [];
    private bool _finished;

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => inner.Flush();

    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count)
    {
        Observe(new ReadOnlySpan<byte>(buffer, offset, count));
        inner.Write(buffer, offset, count);
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken).AsTask();

    public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Observe(buffer.Span);
        await inner.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    internal void Complete()
    {
        if (_finished)
        {
            ledger.Record(runId, _answer.ToString());
        }
    }

    // The response body belongs to the pipeline, not to this wrapper.
    protected override void Dispose(bool disposing)
    {
    }

    private void Observe(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
        {
            if (value == (byte)'\n')
            {
                ReadLine();
                _line.Clear();
            }
            else if (value != (byte)'\r')
            {
                _line.Add(value);
            }
        }
    }

    private void ReadLine()
    {
        const string prefix = "data:";
        if (_line.Count <= prefix.Length)
        {
            return;
        }

        var text = Encoding.UTF8.GetString([.. _line]);
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
        {
            return;
        }

        try
        {
            using var frame = JsonDocument.Parse(text[prefix.Length..]);
            if (!frame.RootElement.TryGetProperty("type", out var type))
            {
                return;
            }

            switch (type.GetString())
            {
                case "TEXT_MESSAGE_CONTENT" when frame.RootElement.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.String:
                    _answer.Append(delta.GetString());
                    break;
                case "RUN_FINISHED":
                    _finished = true;
                    break;
                default:
                    break;
            }
        }
        catch (JsonException)
        {
            // A keep-alive comment or a frame this kernel does not model: nothing to record.
        }
    }
}
```

```csharp
// src/Kernel/DigitalBrain.Silo/Http/AgentReplayResult.cs
using System.Text.Json;

namespace DigitalBrain.Kernel;

internal sealed class AgentReplayResult(string threadId, string runId, string answer) : IResult
{
    private static readonly JsonSerializerOptions Frames = new(JsonSerializerDefaults.Web);

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        var response = httpContext.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        var messageId = Guid.NewGuid().ToString("N");
        await WriteAsync(response, new { type = "RUN_STARTED", threadId, runId });
        await WriteAsync(response, new { type = "TEXT_MESSAGE_START", messageId, role = "assistant" });
        await WriteAsync(response, new { type = "TEXT_MESSAGE_CONTENT", messageId, delta = answer });
        await WriteAsync(response, new { type = "TEXT_MESSAGE_END", messageId });
        await WriteAsync(response, new { type = "RUN_FINISHED", threadId, runId });
    }

    private static async Task WriteAsync<TFrame>(HttpResponse response, TFrame frame)
    {
        await response.WriteAsync($"data: {JsonSerializer.Serialize(frame, Frames)}\n\n", response.HttpContext.RequestAborted);
        await response.Body.FlushAsync(response.HttpContext.RequestAborted);
    }
}
```

- [ ] **Step 4: Tap the stream and answer a resume**

`src/Kernel/DigitalBrain.Silo/Http/AgentStreamResult.cs`: the result learns the run id and taps the body.

```csharp
internal sealed class AgentStreamResult : IResult
{
    private readonly IResult? _response;
    private readonly Exception? _error;
    private readonly string? _runId;

    public AgentStreamResult(IResult response, string? runId = null)
    {
        _response = response;
        _runId = runId;
    }

    public AgentStreamResult(Exception error) => _error = error;

    public async Task ExecuteAsync(HttpContext httpContext)
    {
        try
        {
            if (_error is { } error)
            {
                await WriteFailureAsync(httpContext, error);
                return;
            }

            if (_runId is null)
            {
                await _response!.ExecuteAsync(httpContext);
                return;
            }

            // Watch the frames on their way out, so a reconnect for this run id can be answered from what
            // the model already produced.
            var tap = new AgentAnswerTap(httpContext.Response.Body,
                httpContext.RequestServices.GetRequiredService<AgentRunLedger>(), _runId);
            var body = httpContext.Response.Body;
            httpContext.Response.Body = tap;
            try
            {
                await _response!.ExecuteAsync(httpContext);
            }
            finally
            {
                httpContext.Response.Body = body;
                tap.Complete();
            }
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            // The user stopped or disconnected; there is no client to notify.
        }
        catch (Exception error) when (!httpContext.RequestAborted.IsCancellationRequested)
        {
            await WriteFailureAsync(httpContext, error);
        }
    }
```

`WriteFailureAsync` stays as it is.

`src/Kernel/DigitalBrain.Silo/Http/ConversationalAgentEndpoints.cs`: register the ledger and branch on a
resume the ledger knows.

```csharp
    public static void AddConversationalAgent(this IHostApplicationBuilder builder)
    {
        builder.Services.AddAGUIServer();
        builder.Services.AddSingleton<WorkspaceArtifactStore>();
        builder.Services.AddSingleton<AgentRunLedger>();
```

```csharp
    public static IEndpointConventionBuilder MapConversationalAgent(this IEndpointRouteBuilder endpoints)
        => endpoints.MapAGUIServer(AgentName, "/agent")
            .AddEndpointFilter(static async (context, next) =>
            {
                var run = await AgentRunRequest.ReadAsync(context.HttpContext);
                if (run is { Resume: true, RunId: { Length: > 0 } runId }
                    && context.HttpContext.RequestServices.GetRequiredService<AgentRunLedger>().TryGetAnswer(runId, out var answer))
                {
                    // This silo already answered that run: replay it rather than paying for the model again
                    // and appending the same turn to the session a second time.
                    return new AgentReplayResult(run.ThreadId ?? string.Empty, runId, answer);
                }

                try
                {
                    var result = await next(context);
                    return result is IResult response ? new AgentStreamResult(response, run?.RunId) : result;
                }
                catch (Exception error) when (!context.HttpContext.RequestAborted.IsCancellationRequested)
                {
                    return new AgentStreamResult(error);
                }
            });
```

- [ ] **Step 5: Run the resume facts**

Run: `dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.AgentResumeFacts`
Expected: 3 passed. Then `-- --filter-class DigitalBrain.Tests.TableAgentFacts` and
`-- --filter-class DigitalBrain.Tests.Coding.CodingChatFacts` (the tap sits in front of every `/agent`
response, so both must be unchanged). Then the full gate.

- [ ] **Step 6: The Dart client**

`src/Modules/UI/Flutter/core/lib/src/agent_events.dart`:

```dart
/// One AG-UI run: the shell asks for it and consumes the event stream. `resume` re-issues a run whose
/// stream broke, so the kernel can answer it from what it already produced instead of running it twice.
typedef AgentRunner = Stream<AgentEvent> Function({
  required String threadId,
  required String runId,
  String? parentRunId,
  required String text,
  bool resume,
});
```

`src/Modules/UI/Flutter/core/lib/src/ui_client.dart`, in `runAgent`:

```dart
  Stream<AgentEvent> runAgent({
    required String threadId,
    required String runId,
    String? parentRunId,
    required String text,
    bool resume = false,
  }) {
```

```dart
                  'forwardedProps': resume
                      ? <String, Object>{'resume': true}
                      : <String, Object>{},
```

`src/Modules/UI/Flutter/core/test/agent_stream_test.dart`, two more tests at the end of `main`:

```dart
  test('a first attempt carries no resume flag', () async {
    final transport = CaptureClient();
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://localhost'),
      httpClient: transport,
    );
    final subscription = client
        .runAgent(threadId: 't', runId: 'r', text: 'Hello')
        .listen((_) {});
    final request = await transport.request.future as http.Request;
    final body = jsonDecode(request.body) as Map<String, Object?>;
    expect(body['runId'], 'r');
    expect(body['forwardedProps'], isEmpty);
    await subscription.cancel();
  });

  test('a resumed run asks the kernel to replay the same run id', () async {
    final transport = CaptureClient();
    final client = DigitalBrainUiClient(
      baseUri: Uri.parse('http://localhost'),
      httpClient: transport,
    );
    final subscription = client
        .runAgent(threadId: 't', runId: 'r', text: 'Hello', resume: true)
        .listen((_) {});
    final request = await transport.request.future as http.Request;
    final body = jsonDecode(request.body) as Map<String, Object?>;
    expect(body['runId'], 'r');
    expect((body['forwardedProps'] as Map)['resume'], isTrue);
    await subscription.cancel();
  });
```

- [ ] **Step 7: The shell's one retry**

`src/Modules/UI/Flutter/shell/lib/workspace/workspace_chat.dart`: three new fields next to the existing
ones, `_send` ending in a call to `_start`, and the two new methods.

```dart
  StreamSubscription<AgentEvent>? _subscription;
  bool _running = false, _finished = false;
  String? _runId, _notice;
  String? _prompt;
  bool _retried = false;
  int _entriesBeforeRun = 0;
```

Replace the `try { _subscription = run(...).listen(...); } catch ...` block at the end of `_send` with:

```dart
    _start(prompt, runId);
  }

  // The shell owns the retry, because it owns the prompt and the run id. A stream that ended without
  // RUN_FINISHED is a broken stream, not a finished answer: the slot it was attached to was stopped
  // mid-answer (phase 2 spike S4). One retry with resume: true is answered by the kernel from what it
  // already produced, or run once in the slot that is live now.
  void _start(String prompt, String runId, {bool resume = false}) {
    final run = widget.onRun;
    if (run == null) return;
    _prompt = prompt;
    _runId = runId;
    _retried = resume;
    _entriesBeforeRun = _entries.length;
    try {
      _subscription =
          run(
            threadId: widget.conversation.threadId!,
            runId: runId,
            parentRunId: widget.conversation.parentRunId,
            text: prompt,
            resume: resume,
          ).listen(
            _event,
            onError: (Object e) =>
                _broken('The response could not be completed. $e'),
            onDone: () {
              if (!mounted) return;
              if (_finished) {
                widget.conversation.parentRunId = _runId;
                widget.store.save();
                _finish(null);
              } else {
                _broken('The connection ended before the response completed.');
              }
            },
          );
    } catch (e) {
      _finish('The response could not be started. $e');
    }
  }

  void _broken(String notice) {
    if (!mounted) return;
    final prompt = _prompt, runId = _runId;
    if (_retried || prompt == null || runId == null) {
      _finish(notice);
      return;
    }

    _subscription?.cancel();
    _subscription = null;
    // Drop what the broken attempt streamed: the resumed run sends the whole answer again.
    if (_entries.length > _entriesBeforeRun) {
      _entries.removeRange(_entriesBeforeRun, _entries.length);
    }
    _finished = false;
    _sync(persist: false);
    Future<void>.delayed(const Duration(milliseconds: 750), () {
      if (!mounted || !_running) return;
      _start(prompt, runId, resume: true);
    });
  }
```

The four shell test files fake an `AgentRunner` with an explicit named-parameter closure, so each of the
eight closures gains the new parameter. Every one of them has a `required text,` line followed by the
closing `}`; insert `resume = false,` after it (a non-nullable optional named parameter needs a default in Dart). For example, in `shell/test/workspace_tool_results_test.dart`:

```dart
      onRun: ({
        required threadId,
        required runId,
        parentRunId,
        required text,
        resume = false,
      }) => events.stream,
```

The eight closures are at `workspace_app_test.dart` (two), `workspace_attachment_draft_test.dart` (one),
`workspace_chat_design_test.dart` (four) and `workspace_tool_results_test.dart` (one). `shell/lib/main.dart`
needs no change: `onRun: edge?.runAgent` is a tear-off of the method that just gained the parameter.

- [ ] **Step 8: The shell's retry test**

```dart
// src/Modules/UI/Flutter/shell/test/workspace_chat_resume_test.dart
import 'dart:async';

import 'package:digitalbrain_flutter/digitalbrain_flutter.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_app.dart';
import 'package:digitalbrain_flutter_shell/workspace/workspace_store.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

import 'support/memory_workspace.dart';

void main() {
  testWidgets('an interrupted run is retried once, then gives up', (
    tester,
  ) async {
    final store = WorkspaceStore(persistence: MemoryWorkspacePersistence());
    final attempts = <bool>[];
    final runs = <String>[];
    final controllers = <StreamController<AgentEvent>>[];
    await tester.pumpWidget(
      WorkspaceApp(
        store: store,
        onRun:
            ({
              required threadId,
              required runId,
              parentRunId,
              required text,
              resume = false,
            }) {
              attempts.add(resume as bool);
              runs.add(runId as String);
              final controller = StreamController<AgentEvent>();
              controllers.add(controller);
              return controller.stream;
            },
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('My project'));
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('New conversation'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField), 'Rebuild and promote');
    await tester.pump();
    await tester.tap(find.byTooltip('Send message'));
    await tester.pumpAndSettle();

    // The first attempt streams a partial answer and then the slot it was attached to stops: no
    // RUN_FINISHED, no clean end (phase 2 spike S4).
    controllers[0].add(
      AgentEvent({
        'type': 'TEXT_MESSAGE_CONTENT',
        'messageId': 'm1',
        'delta': 'Promot',
      }),
    );
    await tester.pump();
    await controllers[0].close();
    await tester.pump(const Duration(milliseconds: 900));
    await tester.pumpAndSettle();

    expect(attempts, [false, true]);
    expect(runs[1], runs[0]);
    // The partial answer is gone; the resumed run sends the whole one.
    expect(find.textContaining('Promot'), findsNothing);

    controllers[1].add(
      AgentEvent({
        'type': 'TEXT_MESSAGE_CONTENT',
        'messageId': 'm2',
        'delta': 'Promoted slot b.',
      }),
    );
    controllers[1].add(AgentEvent({'type': 'RUN_FINISHED'}));
    await controllers[1].close();
    await tester.pumpAndSettle();
    await tester.pump(const Duration(seconds: 1));

    expect(find.textContaining('Promoted slot b.'), findsOneWidget);
    expect(attempts.length, 2);
  });
}
```

- [ ] **Step 9: Run the Flutter gate**

From `src/Modules/UI/Flutter`:
`dart format --output=none --set-exit-if-changed core ui shell/lib shell/test`, then `dart test` in `core`,
then `flutter analyze` and `flutter test` in `ui` and in `shell`.
Expected: format clean on the four source folders (the git-ignored `shell/build` tree is excluded on
purpose: one vendored file there is not formatted, recorded in NOTES for phase 0), `dart test` in `core`
green including the two new resume tests, `flutter analyze` with no issues, `flutter test` green in both
packages with one more test in `shell`. If the shell tests fail to compile with "the parameter 'resume' is
not defined", one of the eight fake-runner closures was missed.

- [ ] **Step 10: Commit, in two halves**

```bash
git add src/Kernel/DigitalBrain.Silo/Http tests/DigitalBrain.Tests/Features/AgentResumeFacts.cs
git commit -m "coding: replay a resumed agent run from what the silo already answered"
```

```bash
git add src/Modules/UI/Flutter/core src/Modules/UI/Flutter/shell/lib src/Modules/UI/Flutter/shell/test
git commit -m "coding: the shell retries an interrupted run once with the same run id"
```

---

### Task 11: The docs and the phase gate

**Files:**
- Modify: `docs/coding/README.md`, `docs/coding/NOTES.md` (`docs/coding/STATUS.md` belongs to the
  controller and is never staged by a task)

**Interfaces:**
- Consumes everything above. Produces no code.

- [ ] **Step 1: README**

Add to `docs/coding/README.md`:

- **The `slot` neuron.** Grain type `slot`, key `a` or `b`; `build` (with the repository HEAD commit as the
  generation and the change set's files), `promote`, `retire`, `read`. The phases
  `Idle → Building → Built → Starting → Smoking → Live`, plus `Failed` and `Retired`, and what each
  reaction does; `Draining` is reserved and never assigned in phase 2, because the retiring slot drains
  inside the promoting reaction's grace wait. Building the slot that holds the lease is refused (Windows
  file locks); promoting the slot that holds it is refused (nothing to do). A promotion flips the lease,
  waits until the standby's own `GET /slots/{slot}` reports the flip, then switches the gateway.
- **The fence.** A silo whose slot does not hold the `DigitalBrainLeases` lease answers `[ReadOnly]` reads
  and refuses everything else with `standby slot: silo '<slot>' does not hold the active lease`; its
  reminder ticks are ignored and its pending work stays queued for whoever holds the lease next. The
  refusal is never journaled. The lease row is `PartitionKey=slot`, `RowKey=active`, properties `Owner`,
  `Generation`, `Fenced`, in the clustering storage account, and a hosted refresher re-reads it every two
  seconds.
- **The gateway.** `src/Gateway/DigitalBrain.Gateway` on 5080: one catch-all route over two clusters,
  `POST /switch/{slot}`, `GET /active`, everything else proxied, `/health` included. Switches are debounced
  to one per `DigitalBrain:Gateway:MinSwitchInterval` (15 s) because YARP rebuilds its route table per
  update; an earlier one answers `429` with `Retry-After` rather than holding the request open, and the
  promoting reaction waits that out (three attempts). The initial active slot comes from the lease row,
  falling back to `DigitalBrain:Gateway:Active`.
- **The slot read.** `GET /slots/{slot}` on a kernel answers that slot's `SlotSnapshot`. It is a
  `[ReadOnly]` neuron read, so a standby answers it through the fence; it is how a promotion knows the
  lease flip has reached the new slot, and how an operator reads a slot without the dashboard.
- **The slots in Aspire.** `kernel-a` is the silo project on 5081; `kernel-b` is an executable over
  `artifacts/slot-b/bin/DigitalBrain.Silo/release/DigitalBrain.Silo.dll` on 5082 with `WithExplicitStart`;
  both unproxied, both fed `DigitalBrain__Slot`, neither given an `Orleans__ClusterId` (each mints
  `{slot}-{timestamp}` per start). A slot build is
  `dotnet build DigitalBrain.slnx -c Release -p:ArtifactsPath=<repo>/artifacts/slot-b -p:UseArtifactsOutput=true`;
  the path is always absolute. Rebuilding slot a from b is not built in phase 2.
- **Configuration keys**, with defaults: `DigitalBrain:Slot`, `DigitalBrain:Slots:ArtifactsRoot` (the
  AppHost sets it to `<repo>/artifacts` for both kernels), `:Gateway`, `:Grace`, `:LeaseSettle`,
  `:PromoteWait`, `:SmokePath`, `:HealthAttempts`, `:a:Url`, `:b:Url`, `:a:Resource`,
  `:b:Resource`, `DigitalBrain:Gateway:Slots:a`, `:b`, `:Active`, `:MinSwitchInterval`. Note that
  `:<slot>:Resource` is the Aspire resource name `execute_resource_command` is given, so it is the knob to
  turn if the running AppHost exposes a different name.
- **`code_promote`** (the fifteenth tool): what it does, its three arguments, the result shape, and the two
  refusals (an uncommitted change set; a rollback after a landing that changed a `[GenerateSerializer]`
  type).
- **The reconnect.** `/agent` accepts `forwardedProps.resume` with the original `runId`; the kernel replays
  a run it already answered from a bounded in-process ledger, and the shell retries an interrupted run
  once. The brain-observation stream reconnects on its own and re-reads a whole snapshot.
- **Tests**: the new fact classes (`ActiveSlotLeaseFacts`, `SlotFenceFacts`, `AzureTableActiveSlotLeaseFacts`
  (gated), `GatewayFacts`, `SlotConfigurationFacts`, `AspireConnectionFacts`, `SlotServiceFacts`,
  `SlotNeuronFacts`, `SlotToolFacts`, `AgentResumeFacts`, `workspace_chat_resume_test.dart`), the two gated
  gates (`DIGITALBRAIN_CODING_SELF_TESTS=1` for the real slot build, `DIGITALBRAIN_SLOT_LEASE_TESTS=1` plus
  `DIGITALBRAIN_SLOT_LEASE_TABLES` for the lease row), and "What phase 3 adds" (the swarm).

- [ ] **Step 2: NOTES**

Add to `docs/coding/NOTES.md`:

- `## Phase 2 outcome (2026-09-14)`: the fact classes and their counts at the end of Task 11, the two gated
  gates and how to run them, and the exit criterion.
- `## Phase 2 deviations`: the eight the plan already knows, each with its reason —
  (1) `RollbackAllowed` is recorded on the slot that was built and the rollback refusal lives in
  `code_promote`, not in the `slot` neuron, because a neuron may not read another neuron inside a command;
  (2) the retired slot is stopped only for a forward-only landing, so a rollback has something to go back
  to, and `retire` stops it otherwise;
  (3) the whole post-flip sequence (wait for the standby to see the lease, gateway switch with its bounded
  `Retry-After` retry, grace, save, stop) runs in one reaction, because the moment the lease moves this silo
  is the standby and nothing it schedules would drain here; `SlotPhase.Draining` is therefore reserved and
  never assigned in phase 2 — the retiring slot drains inside that reaction's grace wait;
  (4) the shell's reconnect work is `/agent` only: the brain-observation stream already reconnects with a
  fresh snapshot every two seconds, and the shell never needs `GET /active` because the gateway owns the
  address — `/active` is for the `slot` neuron and for operators;
  (5) `DotnetRunner` now also passes `-p:UseArtifactsOutput=true` whenever an artifacts path is given
  (spike S1), which changes `code_build` and `code_test` as well;
  (6) `/agent` resume is answered from a bounded in-process ledger, so after a promotion the resumed run is
  a fresh run in the new slot: the pre-swap AG-UI session does not move with it;
  (7) the smoke check is `GET /health` plus a read-only neuron read (`DigitalBrain:Slots:SmokePath`,
  default `/chats/slot-smoke/brain`) rather than an `/mcp` call, because that read goes through the fence
  and so proves the standby answers reads and nothing else;
  (8) two Aspire resources per slot name, not a `WithReplicas` pair, and rebuilding slot a from b is a
  recorded phase 2 limitation;
  (9) the silo gained one read route, `GET /slots/{slot}`: a lease flip is a table row, and the standby
  learns of it through its own refresher, so the promotion needs a way to ask it whether the flip arrived
  before it moves any traffic there;
  (10) the gateway answers an early re-switch with `429` and `Retry-After` instead of waiting the debounce
  out inside the request, and the promoting reaction owns that wait (three attempts, each capped at 20 s);
  and, if it happened, (11) the `Azure.Data.Tables` downgrade from 12.12.0 to 12.11.0 with the NuGet error
  that forced it (Task 3).
- `## Phase 2 observations`: whatever the execution turns up, and in particular whether
  `execute_resource_command` accepted the logical resource names `kernel-a`/`kernel-b` or needed the
  dashboard's suffixed instance names (the `:Resource` keys exist for that).
- `## Phase 2 live run`: the controller records the transcript of the live exit check here.

- [ ] **Step 3: Gates (controller)**

The controller runs, in this order:

1. `dotnet format whitespace DigitalBrain.slnx --verify-no-changes`
2. `dotnet build DigitalBrain.slnx -c Release`
3. `dotnet test DigitalBrain.slnx -c Release --no-build` (the 4 Docker-gated skips, the coding self-tests
   and the slot-lease facts skipped)
4. Gated: `DIGITALBRAIN_CODING_SELF_TESTS=1 dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Coding.CodingSelfTestFacts`
   (three facts now, including the slot build into `artifacts/slot-b`)
5. Gated, with Azurite reachable:
   `DIGITALBRAIN_SLOT_LEASE_TESTS=1 DIGITALBRAIN_SLOT_LEASE_TABLES=<table connection string> dotnet test tests/DigitalBrain.Tests/DigitalBrain.Tests.csproj -c Release -- --filter-class DigitalBrain.Tests.Slots.AzureTableActiveSlotLeaseFacts`
6. The Flutter gate from `src/Modules/UI/Flutter`:
   `dart format --output=none --set-exit-if-changed core ui shell/lib shell/test`, `dart test` in `core`,
   `flutter analyze` and `flutter test` in `ui` and in `shell`
7. The live exit check with `aspire run --detach` (and the Aspire MCP tools for the resource commands):
   - the dashboard shows `gateway` (5080), `kernel-a` (5081) healthy and `kernel-b` not started;
   - the shell reaches the product through 5080;
   - `code_promote` from the chat builds `artifacts/slot-b`, starts `kernel-b`, smokes it, flips the lease,
     switches the gateway, and the chat continues with at most one reconnect;
   - a deliberately broken build (an edit that does not compile, committed through `code_commit`, then
     `code_promote`) answers `Failed` with the errors in the card and leaves `kernel-a` serving;
   - a rollback: `code_promote(slot: "a", promoteOnly: true)` moves the lease and the gateway back while
     `kernel-a` is still running;
   - `GET http://localhost:5080/active` agrees with the `slot` neuron's `read` at each step.
   The transcript, the timings and any Aspire resource-name surprise go into NOTES; STATUS is updated and
   the PR opened.

- [ ] **Step 4: Commit**

```bash
git add docs/coding/README.md docs/coding/NOTES.md
git commit -m "coding: phase 2 docs for the slots, the gateway and the fence"
```

## Self-review

- **Spec coverage (design 9.2).** Spikes S1-S4: run before this plan, recorded in
  `docs/coding/NOTES.md` and `.superpowers/sdd/phase2-spikes/`, and every one of their verdicts is used
  (S1 in Tasks 5 and 8, S2 in Tasks 2 and 8, S3 in Tasks 1 and 3, S4 in Tasks 7 and 10).
  Deliverables: `src/Gateway/DigitalBrain.Gateway` with `LoadFromMemory`-style in-memory configuration,
  `POST /switch/{slot}`, `GET /active` and health passthrough — Task 7; AppHost `kernel-a` and `kernel-b`
  with `WithExplicitStart` on b, slot and artifacts environment, behind the gateway on 5080 — Task 8;
  `ActiveSlotLease` in the kernel with the lease row, the fence in the grain-call filter and the reminder
  handler, and the "standby slot" refusal text — Tasks 1, 2 and 3; `ISlot` with
  `build`/`promote`/`retire`/`read` and `SlotSnapshot(Slot, Phase, Generation, ArtifactsPath, Healthy,
  HoldsLease, …)` — Tasks 5 and 6; `SlotBuilder` — Task 5; `AspireConnection` widened to
  `execute_resource_command` with `start|stop|restart` — Task 4; the shell's reconnect by run id — Task 10;
  the tool `code_promote` — Task 9. Tests: lease CAS facts — Task 1; the fence refusing a command, ignoring
  a tick and answering a read on a standby — Task 2; `SlotBuilder` parsing build errors through the fake
  runner — Task 5; the gated slot build of the real solution into `artifacts/slot-b` — Task 5; the gated
  Azurite lease — Task 3; gateway facts on in-process hosts — Task 7; Dart tests for the reconnect —
  Task 10; the live promote, reconnect and rollback — Task 11 step 3, recorded in NOTES, not automated.
  Exit ("rebuild" promotes the standby with at most one reconnect; a deliberately broken build leaves the
  live slot untouched with the errors in a card): the live check in Task 11 step 3, with
  `SlotToolFacts.A_broken_build_returns_the_errors_and_leaves_the_live_slot_alone` as its automated half.
  Deviations from the letter of 9.2 are listed in Task 11 step 2 with their reasons; the three that change
  design 4.4's wording are the stop-only-when-forward-only rule, the one-reaction post-flip sequence, and
  the wait for the standby to observe the lease before the gateway moves.
- **Placeholders.** None: every step carries its code, its command and its expected output. The only text a
  person still writes is `docs/coding/NOTES.md`'s outcome, observations and live transcript, which are
  observations of the execution and belong to the controller.
- **Type consistency across tasks.** `IActiveSlotLease(Slot, HoldsLease, Generation, TryAcquireAsync,
  ReleaseAsync, RefreshAsync)` — Tasks 1, 2, 3, 6; `ActiveSlotNames(SlotKey, Table, PartitionKey, RowKey,
  Owner, RefreshInterval)` — Tasks 1, 2, 3, 5, 6, 7; `StandbySlotException(slot)` — Tasks 1, 2;
  `IAspireResourceCommands.ExecuteAsync(resourceName, command, ct)` — Tasks 4, 5 (the unconfigured
  default), 6, 9; `SlotOptions(Slot, ArtifactsRoot, GatewayUrl, Grace, LeaseSettle, PromoteWait, SmokePath,
  HealthAttempts, Urls, Resources)` with `Names`, `UrlFor`, `ResourceFor`, `ArtifactsFor(slot, solutionDirectory)`
  — Tasks 5, 6, 8, 9; `ISlotBuilder.BuildAsync(slot, changedFiles, ct)` → `SlotBuildResult(Build,
  ArtifactsPath, TouchesSerializedState)` — Tasks 5, 6, 9; `ISlotEndpoints(HealthyAsync, SmokeAsync,
  HoldsLeaseAsync, SwitchAsync → TimeSpan?, ActiveAsync)` — Tasks 5, 6, 9; `GET /slots/{slot}`
  (`SlotEndpoints.MapSlotEndpoints`) — Tasks 5, 6, 7 (proxied), 11; `GitRunner.HeadCommitAsync(repository, ct)`
  — Tasks 5, 9; `SlotSnapshot(Slot, Phase, Generation, ArtifactsPath, Healthy, HoldsLease, Errors, Detail,
  RollbackAllowed, Revision)` with `Generation` = the HEAD commit hash — Tasks 5, 6, 9;
  `BuildSlot(Id, Generation, ChangedFiles)` / `PromoteSlot(Id)` / `RetireSlot(Id)` — Tasks 5, 6, 9;
  `SlotPromotionBody(FromSlot, Attempt)` and `SlotBuildingBody(Generation, ChangedFiles)` — Tasks 5, 6;
  `CodingNativeTools.SettleAsync(read, settled, subject, describe, budget, ct)` — Task 9 (and the existing
  change-set callers through the `WaitAsync` wrapper); `GatewayOptions(Slots, Active, MinSwitchInterval)`,
  `SlotRouter(Provider, Active, MinSwitchInterval, Knows, Adopt, Switch)` and
  `SwitchResult(Verdict, RetryAfter)` over `SwitchVerdict { Switched, Unknown, TooSoon }` — Tasks 7, 8;
  `AgentRunLedger(TryGetAnswer, Record)`, `AgentRunRequest(ThreadId, RunId, Resume)`,
  `AgentAnswerTap(inner, ledger, runId)` and `AgentReplayResult(threadId, runId, answer)` — Task 10;
  `AgentRunner`/`runAgent` with `resume` — Task 10 and the eight fake-runner closures it names.
  `FakeSlotBuilder`, `FakeSlotEndpoints` and `FakeAspireResourceCommands` are produced once in Task 6
  (`SlotFakes.cs`) and reused by Task 9.
