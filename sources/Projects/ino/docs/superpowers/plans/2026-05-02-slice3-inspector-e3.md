# Slice 3 — Inspector E.3 (Proposals + Routing tabs) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the L1 self-improvement loop user-visible. Two new tabs in the existing inspector drawer: **Routing** (last 20 routing decisions per user, color-coded by source) and **Proposals** (Pending → Approved/Rejected lifecycle). Approval gating uses Option A — `IExperienceRegistry.ApprovalRequired` flag, default `true`.

**Architecture:** Three sub-commits, sequential. (3A) Backend grains + ApprovalRequired flag + CreatorNeuron gating branch. (3B) Three new gRPC RPCs on the existing `Ino` service. (3C) Flutter Routing + Proposals tabs added alongside existing Inspector panels.

**Tech Stack:** Orleans 10, gRPC + protobuf, Flutter + flutter_bloc, Material 3.

**Spec reference:** `docs/superpowers/specs/2026-05-02-phase4-epilogue-design.md` § Slice 3.

---

## Pre-flight

- [ ] **Confirm Slice 2 (tripradar relocation) shipped.** This slice doesn't depend on it, but per the spec ordering, Slice 2 ships first. If you're executing out of order, that's fine — flag it to the user.

- [ ] **Read the canonical state files.** Before writing code:
  - `Read src/Ino.Kernel/MissedIntentTracker.cs` — understand `L1Proposal` shape and broadcast pattern.
  - `Read src/Ino.Kernel/CortexNeuron.cs` and find `RecordRoutingDecisionAsync` — note the existing optimizer feed.
  - `Read domains/genesis/Ino.Domains.Genesis/Neurons/CreatorNeuron.cs` — understand current auto-register flow.
  - `Read src/Ino.Core.Hosting/Registry/IExperienceRegistry.cs` (or wherever `IExperienceRegistry` lives — `Glob src/**/IExperienceRegistry.cs`).
  - `Read src/Ino.Gateway.Grpc/Protos/ino.proto` — see the Chat / FireSynapse / etc. style.
  - `Read clients/ino.flutter/lib/ui/components/inspector_drawer.dart` — understand existing tab structure.

---

## File structure

### Sub-commit 3A — Backend

| File | Action | Responsibility |
|---|---|---|
| `src/Ino.Kernel.Contracts/ProposalDecided.cs` | Create | Synapse broadcast when user approves/rejects |
| `src/Ino.Kernel.Contracts/ProposalStatus.cs` | Create | Enum: Pending, Approved, Rejected |
| `src/Ino.Kernel.Contracts/ProposalEntry.cs` | Create | Read-model for ProposalLog |
| `src/Ino.Kernel.Contracts/RoutingDecision.cs` | Create | Read-model for CortexJournal |
| `src/Ino.Kernel.Contracts/RoutingSource.cs` | Create | Enum: Regex, Ml, Llm, Unrouted |
| `src/Ino.Kernel.Contracts/IProposalLog.cs` | Create | Grain interface |
| `src/Ino.Kernel.Contracts/ICortexJournal.cs` | Create | Grain interface |
| `src/Ino.Kernel/ProposalLog.cs` | Create | `[PinToSilo("kernel")]` reactor grain |
| `src/Ino.Kernel/CortexJournal.cs` | Create | `[PinToSilo("kernel")]` per-user buffer grain |
| `src/Ino.Kernel/CortexNeuron.cs` | Modify | Fork RecordRoutingDecisionAsync to also write CortexJournal |
| `src/Ino.Core.Hosting/Registry/IExperienceRegistry.cs` | Modify | Add ApprovalRequired, ApproveAsync, RejectAsync, StashDraftAsync |
| `src/Ino.Core.Hosting/Registry/ExperienceRegistry.cs` (or impl path — verify) | Modify | Implement the new members + draft stash dictionary |
| `src/Ino.Core.Hosting/Configuration/...` | Modify or add | Bind `Ino:Inspector:ApprovalRequired` from config |
| `domains/genesis/Ino.Domains.Genesis/Neurons/CreatorNeuron.cs` | Modify | Branch on registry.ApprovalRequired |
| `test/Ino.Kernel.Tests/ProposalLogTests.cs` | Create | 4 tests |
| `test/Ino.Kernel.Tests/CortexJournalTests.cs` | Create | 3 tests |
| `domains/genesis/Ino.Domains.Genesis.Tests/CreatorNeuronApprovalGatingTests.cs` | Create | 3 tests |
| `test/Ino.Kernel.Tests/L1LoopTests.cs` | Modify | Adapt to call ApproveAsync after proposal lands |

### Sub-commit 3B — gRPC

| File | Action | Responsibility |
|---|---|---|
| `src/Ino.Gateway.Grpc/Protos/ino.proto` | Modify | Add 3 RPCs + 5 messages + 2 enums |
| `src/Ino.Gateway/IInoGateway.cs` | Modify | Add 3 async methods |
| `src/Ino.Gateway/InoGateway.cs` | Modify | Implement the 3 methods (delegate to grains) |
| `src/Ino.Gateway.Grpc/Services/InoGrpcService.cs` | Modify | Add 3 handler overrides |
| `test/Ino.Hosting.Tests/InoGrpcServiceInspectorRpcsTests.cs` | Create | 3 tests |

### Sub-commit 3C — Flutter

| File | Action | Responsibility |
|---|---|---|
| `clients/ino.flutter/lib/grpc/generated/*.dart` | Regenerate | Auto-derived from proto changes |
| `clients/ino.flutter/lib/state/proposals_bloc.dart` | Create | Polling BLoC for Proposals tab |
| `clients/ino.flutter/lib/state/routing_bloc.dart` | Create | Polling BLoC for Routing tab |
| `clients/ino.flutter/lib/ui/components/inspector_drawer.dart` | Modify | Add 2 new tabs |
| `clients/ino.flutter/lib/main.dart` (or wherever providers live) | Modify | Wire the 2 new BLoCs into provider tree |

---

# Sub-commit 3A — ProposalLog + CortexJournal grains + ApprovalRequired gating

## Task 1 — Create the contracts

**Files:**
- Create: `src/Ino.Kernel.Contracts/ProposalStatus.cs`
- Create: `src/Ino.Kernel.Contracts/RoutingSource.cs`
- Create: `src/Ino.Kernel.Contracts/ProposalEntry.cs`
- Create: `src/Ino.Kernel.Contracts/RoutingDecision.cs`
- Create: `src/Ino.Kernel.Contracts/ProposalDecided.cs`

- [ ] **Step 1: Create the two enums**

`src/Ino.Kernel.Contracts/ProposalStatus.cs`:
```csharp
namespace Ino.Kernel.Contracts;

public enum ProposalStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2,
}
```

`src/Ino.Kernel.Contracts/RoutingSource.cs`:
```csharp
namespace Ino.Kernel.Contracts;

public enum RoutingSource
{
    Unrouted = 0,
    Regex = 1,
    Ml = 2,
    Llm = 3,
}
```

- [ ] **Step 2: Create `ProposalEntry`**

`src/Ino.Kernel.Contracts/ProposalEntry.cs`:
```csharp
using Orleans;

namespace Ino.Kernel.Contracts;

[GenerateSerializer]
public sealed record ProposalEntry(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] string UserId,
    [property: Id(2)] string ClusterKey,
    [property: Id(3)] string ExamplePrompt,
    [property: Id(4)] string[] AllPrompts,            // concrete T[] per <>z__ReadOnlyArray trap
    [property: Id(5)] int Occurrences,
    [property: Id(6)] DateTimeOffset ProposedAt,
    [property: Id(7)] ProposalStatus Status,
    [property: Id(8)] string? ActivatedExperienceId,
    [property: Id(9)] DateTimeOffset? DecidedAt,
    [property: Id(10)] string? DecidedBy);
```

- [ ] **Step 3: Create `RoutingDecision`**

`src/Ino.Kernel.Contracts/RoutingDecision.cs`:
```csharp
using Orleans;

namespace Ino.Kernel.Contracts;

[GenerateSerializer]
public sealed record RoutingDecision(
    [property: Id(0)] string Prompt,
    [property: Id(1)] RoutingSource Source,
    [property: Id(2)] string? ExperienceId,
    [property: Id(3)] double? Confidence,
    [property: Id(4)] DateTimeOffset At,
    [property: Id(5)] double? MlPrediction,
    [property: Id(6)] double? MlConfidence,
    [property: Id(7)] bool LlmCalled,
    [property: Id(8)] int RoutingDurationMs,
    [property: Id(9)] string CorrelationId);
```

- [ ] **Step 4: Create `ProposalDecided` synapse**

`src/Ino.Kernel.Contracts/ProposalDecided.cs`:
```csharp
using Ino.Core;
using Orleans;

namespace Ino.Kernel.Contracts;

[GenerateSerializer]
public sealed record ProposalDecided(
    [property: Id(0)] string ProposalId,
    [property: Id(1)] ProposalStatus Decision,        // Approved | Rejected (Pending invalid)
    [property: Id(2)] string DecidedBy,
    [property: Id(3)] DateTimeOffset DecidedAt) : ISynapse;
```

- [ ] **Step 5: Build and verify**

```
dotnet build src/Ino.Kernel.Contracts/Ino.Kernel.Contracts.csproj
```
Expected: clean.

- [ ] **Step 6: Commit**

```
git add src/Ino.Kernel.Contracts/
git commit -m "feat(poc): add proposal + routing contracts to Ino.Kernel.Contracts"
```

---

## Task 2 — Create `IProposalLog` + `ICortexJournal` interfaces

**Files:**
- Create: `src/Ino.Kernel.Contracts/IProposalLog.cs`
- Create: `src/Ino.Kernel.Contracts/ICortexJournal.cs`

- [ ] **Step 1: `IProposalLog`**

```csharp
using Orleans;

namespace Ino.Kernel.Contracts;

public interface IProposalLog : IGrainWithStringKey
{
    Task<IReadOnlyList<ProposalEntry>> ListAsync(ProposalStatus? filter, int skip, int take);
    Task<ProposalEntry?> GetAsync(string proposalId);
    Task RecordDecisionAsync(string proposalId, ProposalStatus decision, string decidedBy);
}
```

- [ ] **Step 2: `ICortexJournal`**

```csharp
using Orleans;

namespace Ino.Kernel.Contracts;

public interface ICortexJournal : IGrainWithStringKey
{
    Task RecordAsync(string userId, RoutingDecision decision);
    Task<IReadOnlyList<RoutingDecision>> GetRecentAsync(string userId, int count);
}
```

- [ ] **Step 3: Build**

```
dotnet build src/Ino.Kernel.Contracts/Ino.Kernel.Contracts.csproj
```
Expected: clean.

---

## Task 3 — Implement `ProposalLog` grain

**Files:**
- Create: `src/Ino.Kernel/ProposalLog.cs`

- [ ] **Step 1: Find the existing `[PinToSilo]` attribute and reactor pattern**

```
Grep pattern="\\[PinToSilo" path="src/Ino.Kernel" output_mode="content"
Grep pattern="IReactsTo" path="src/Ino.Kernel" output_mode="content"
```
Expected: at least one example each (`Discovery.cs` is a likely model).

- [ ] **Step 2: Write the grain**

`src/Ino.Kernel/ProposalLog.cs`:
```csharp
using Ino.Core;
using Ino.Core.Hosting;
using Ino.Kernel.Contracts;
using Microsoft.Extensions.Logging;
using Orleans;

namespace Ino.Kernel;

[PinToSilo("kernel")]
public sealed class ProposalLog(ILogger<ProposalLog> logger)
    : Grain, IProposalLog,
        IReactsTo<L1Proposal>,
        IReactsTo<NeuronCreated>,
        IReactsTo<ProposalDecided>
{
    private readonly Dictionary<string, ProposalEntry> _entries = new();

    public Task ReactAsync(L1Proposal proposal, NeuronContext ctx, CancellationToken ct)
    {
        if (_entries.ContainsKey(proposal.ProposalId)) return Task.CompletedTask;
        _entries[proposal.ProposalId] = new ProposalEntry(
            ProposalId: proposal.ProposalId,
            UserId: proposal.UserId,
            ClusterKey: proposal.ClusterKey,
            ExamplePrompt: proposal.ExamplePrompt,
            AllPrompts: new[] { proposal.ExamplePrompt },
            Occurrences: proposal.Occurrences,
            ProposedAt: proposal.ProposedAt,
            Status: ProposalStatus.Pending,
            ActivatedExperienceId: null,
            DecidedAt: null,
            DecidedBy: null);
        logger.LogInformation("ProposalLog: recorded pending {ProposalId}", proposal.ProposalId);
        return Task.CompletedTask;
    }

    public Task ReactAsync(NeuronCreated created, NeuronContext ctx, CancellationToken ct)
    {
        if (!_entries.TryGetValue(created.ProposalId, out var existing)) return Task.CompletedTask;
        _entries[created.ProposalId] = existing with
        {
            Status = ProposalStatus.Approved,
            ActivatedExperienceId = created.ExperienceId,
            DecidedAt = created.CreatedAt,
        };
        return Task.CompletedTask;
    }

    public Task ReactAsync(ProposalDecided decided, NeuronContext ctx, CancellationToken ct)
    {
        if (!_entries.TryGetValue(decided.ProposalId, out var existing)) return Task.CompletedTask;
        _entries[decided.ProposalId] = existing with
        {
            Status = decided.Decision,
            DecidedAt = decided.DecidedAt,
            DecidedBy = decided.DecidedBy,
        };
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<ProposalEntry>> ListAsync(ProposalStatus? filter, int skip, int take)
    {
        IEnumerable<ProposalEntry> q = _entries.Values.OrderByDescending(e => e.ProposedAt);
        if (filter is { } f) q = q.Where(e => e.Status == f);
        IReadOnlyList<ProposalEntry> result = q.Skip(skip).Take(take).ToArray();
        return Task.FromResult(result);
    }

    public Task<ProposalEntry?> GetAsync(string proposalId) =>
        Task.FromResult(_entries.GetValueOrDefault(proposalId));

    public Task RecordDecisionAsync(string proposalId, ProposalStatus decision, string decidedBy)
    {
        if (!_entries.TryGetValue(proposalId, out var existing))
            throw new InvalidOperationException($"Unknown proposal {proposalId}");
        _entries[proposalId] = existing with
        {
            Status = decision,
            DecidedAt = DateTimeOffset.UtcNow,
            DecidedBy = decidedBy,
        };
        return Task.CompletedTask;
    }
}
```

> **Note:** if `L1Proposal` doesn't already have `UserId` / `ClusterKey` / `ExamplePrompt` / `Occurrences` / `ProposedAt` fields with those exact names, adapt the `ReactAsync` body to the actual record. Read `MissedIntentTracker.cs` to confirm.
>
> Same for `NeuronCreated` — verify the `ProposalId` / `ExperienceId` / `CreatedAt` field names.

- [ ] **Step 3: Build kernel project**

```
dotnet build src/Ino.Kernel/Ino.Kernel.csproj
```
Expected: clean.

---

## Task 4 — Implement `CortexJournal` grain

**Files:**
- Create: `src/Ino.Kernel/CortexJournal.cs`

- [ ] **Step 1: Write the grain**

```csharp
using Ino.Kernel.Contracts;
using Orleans;

namespace Ino.Kernel;

[PinToSilo("kernel")]
public sealed class CortexJournal : Grain, ICortexJournal
{
    private const int CapPerUser = 20;

    // userId -> circular buffer (newest at index 0 after each Record).
    private readonly Dictionary<string, LinkedList<RoutingDecision>> _byUser = new();

    public Task RecordAsync(string userId, RoutingDecision decision)
    {
        if (!_byUser.TryGetValue(userId, out var buf))
        {
            buf = new LinkedList<RoutingDecision>();
            _byUser[userId] = buf;
        }
        buf.AddFirst(decision);
        while (buf.Count > CapPerUser) buf.RemoveLast();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<RoutingDecision>> GetRecentAsync(string userId, int count)
    {
        if (!_byUser.TryGetValue(userId, out var buf))
            return Task.FromResult<IReadOnlyList<RoutingDecision>>(Array.Empty<RoutingDecision>());
        IReadOnlyList<RoutingDecision> result = buf.Take(count).ToArray();
        return Task.FromResult(result);
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build src/Ino.Kernel/Ino.Kernel.csproj
```
Expected: clean.

---

## Task 5 — Fork CortexNeuron's routing-decision write into the journal

**Files:**
- Modify: `src/Ino.Kernel/CortexNeuron.cs`

- [ ] **Step 1: Locate `RecordRoutingDecisionAsync`**

```
Grep pattern="RecordRoutingDecisionAsync" path="src/Ino.Kernel/CortexNeuron.cs" output_mode="content" -C=10 -n=true
```
Note the existing optimizer-feed call — that path stays intact. We add a second fire-and-forget write into the journal alongside it.

- [ ] **Step 2: Inject `IGrainFactory` if it isn't already**

Check the constructor signature. If `IGrainFactory grainFactory` (or similar) is already a constructor parameter, no change. Otherwise add it.

- [ ] **Step 3: Add the journal write**

Inside `RecordRoutingDecisionAsync`, after the existing optimizer-feed call, add:

```csharp
var journal = grainFactory.GetGrain<ICortexJournal>("singleton");
var decision = new RoutingDecision(
    Prompt: prompt,
    Source: routingSource,
    ExperienceId: routedExperienceId,
    Confidence: confidence,
    At: DateTimeOffset.UtcNow,
    MlPrediction: mlPrediction,
    MlConfidence: mlConfidence,
    LlmCalled: llmCalled,
    RoutingDurationMs: (int)stopwatch.ElapsedMilliseconds,
    CorrelationId: correlationId);
_ = journal.RecordAsync(userId, decision);   // fire-and-forget; don't slow routing
```

The exact local-variable names (`routingSource`, `confidence`, `mlPrediction`, etc.) depend on what CortexNeuron's code already exposes at that point. If a name is missing, derive it from local context — e.g., a `bool routed` may need to be mapped to `RoutingSource.Regex/Ml/Llm/Unrouted`. Use the simplest correct mapping.

If a `Stopwatch` isn't already running over the routing path, start one at method entry: `var stopwatch = Stopwatch.StartNew();`.

- [ ] **Step 4: Build kernel**

```
dotnet build src/Ino.Kernel/Ino.Kernel.csproj
```
Expected: clean.

---

## Task 6 — Extend `IExperienceRegistry` with ApprovalRequired + draft-stash flow

**Files:**
- Modify: `src/Ino.Core.Hosting/Registry/IExperienceRegistry.cs` (or wherever it lives — find via Glob)
- Modify: the implementing class

- [ ] **Step 1: Find the interface and impl**

```
Glob pattern="src/**/IExperienceRegistry.cs"
Glob pattern="src/**/ExperienceRegistry.cs"
```
Expected: one of each. Read both.

- [ ] **Step 2: Extend the interface**

Add the following members alongside existing methods:

```csharp
bool ApprovalRequired { get; }

/// Stash a draft for later approval. Idempotent on proposalId.
Task StashDraftAsync(string proposalId, DraftExperience draft, CancellationToken ct);

/// Promote a stashed draft to a live registered experience. Returns true if an
/// approval actually happened (false if the proposal was unknown or already approved).
Task<bool> ApproveAsync(string proposalId, string approvedBy, CancellationToken ct);

/// Discard a stashed draft. Returns true if a stash existed.
Task<bool> RejectAsync(string proposalId, string rejectedBy, CancellationToken ct);
```

- [ ] **Step 3: Add `DraftExperience` record (nearby)**

If a `DraftExperience` type doesn't exist, create one beside the interface:

```csharp
public sealed record DraftExperience(
    string ExperienceId,
    string ScriptBody,
    string ProposalId,
    DateTimeOffset DraftedAt);
```

- [ ] **Step 4: Implement in the concrete class**

In the `ExperienceRegistry` impl:
- Add a constructor parameter `IConfiguration config` if not present.
- Bind `ApprovalRequired`:
  ```csharp
  public bool ApprovalRequired { get; } = config.GetValue("Ino:Inspector:ApprovalRequired", true);
  ```
- Add a private `Dictionary<string, DraftExperience> _drafts = new();`
- `StashDraftAsync` puts the draft in the dict (no-op if duplicate proposalId).
- `ApproveAsync`: look up draft, call existing `RegisterAsync(draft.ToExperience(), ct)` (or whatever the existing path is), remove from `_drafts`, return true. Return false if not found.
- `RejectAsync`: remove from `_drafts`, return true if was present.

- [ ] **Step 5: Build core hosting**

```
dotnet build src/Ino.Core.Hosting/Ino.Core.Hosting.csproj
```
Expected: clean.

---

## Task 7 — Branch CreatorNeuron on `ApprovalRequired`

**Files:**
- Modify: `domains/genesis/Ino.Domains.Genesis/Neurons/CreatorNeuron.cs`

- [ ] **Step 1: Read the current ReactAsync logic**

```
Read domains/genesis/Ino.Domains.Genesis/Neurons/CreatorNeuron.cs
```
Identify where `registry.RegisterAsync(...)` is called and where `NeuronCreated` is broadcast.

- [ ] **Step 2: Branch on `registry.ApprovalRequired`**

Replace the auto-register block with:

```csharp
public async Task ReactAsync(L1Proposal proposal, NeuronContext ctx, CancellationToken ct)
{
    var experienceId = DraftExperienceId(proposal);
    if (await registry.IsAlreadyRegisteredAsync(experienceId, ct)) return;

    var draft = ComposeDraft(proposal, experienceId);   // existing path; pulls from DraftScriptBody

    if (registry.ApprovalRequired)
    {
        await registry.StashDraftAsync(proposal.ProposalId, draft, ct);
        logger.LogInformation("CreatorNeuron: stashed draft for {ProposalId} (ApprovalRequired=true)",
            proposal.ProposalId);
        return;
    }

    await registry.RegisterAsync(draft.ToExperience(), ct);
    await firePort.FireBroadcast(
        new NeuronCreated(proposal.ProposalId, experienceId, proposal.UserId, DateTimeOffset.UtcNow),
        ctx, ct);
}
```

The `ComposeDraft` helper extraction is one refactoring step away from the existing code — pull the existing draft-construction body into a private method that returns `DraftExperience`. If `IsAlreadyRegisteredAsync` doesn't exist, use whatever the existing dedupe check is (the spec referenced `DraftExperienceId` — the existing code likely already has a check).

- [ ] **Step 3: Add a public `Approve` path**

When the gateway calls `IExperienceRegistry.ApproveAsync(proposalId, ...)`, the registry promotes the draft. To keep `NeuronCreated` broadcast as a single source of truth, do the broadcast from the registry impl OR from the gateway after registry returns true. Pick the cleaner location:

- If the registry can take an `IFirePort` dependency — broadcast there.
- Otherwise — broadcast from the gateway's `DecideProposalAsync` (Sub-commit 3B), which already has access to fire-port via the kernel's existing wiring.

Document the choice in a short code comment so a future reader can find the broadcast.

- [ ] **Step 4: Build genesis**

```
dotnet build domains/genesis/Ino.Domains.Genesis/Ino.Domains.Genesis.csproj
```
Expected: clean.

---

## Task 8 — Tests for ProposalLog

**Files:**
- Create: `test/Ino.Kernel.Tests/ProposalLogTests.cs`

- [ ] **Step 1: Write the four tests**

```csharp
using FluentAssertions;
using Ino.Kernel.Contracts;
using Ino.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Orleans.TestingHost;
using Xunit;

namespace Ino.Kernel.Tests;

[Collection(nameof(InoTestCollection))]
public sealed class ProposalLogTests(InoTestSiloFixture fx)
{
    [Fact]
    public async Task L1Proposal_creates_pending_entry()
    {
        var grain = fx.Cluster.GrainFactory.GetGrain<IProposalLog>("singleton");
        var proposal = new L1Proposal("p1", "u1", "cluster-x", "do thing", 3, DateTimeOffset.UtcNow);
        // Fire via Orleans broadcast (or call ReactAsync directly via test harness).
        await fx.FirePort.FireBroadcastAsync(proposal);

        var list = await grain.ListAsync(ProposalStatus.Pending, 0, 100);
        list.Should().ContainSingle(e => e.ProposalId == "p1" && e.Status == ProposalStatus.Pending);
    }

    [Fact]
    public async Task NeuronCreated_flips_pending_to_approved()
    {
        var grain = fx.Cluster.GrainFactory.GetGrain<IProposalLog>("singleton");
        await fx.FirePort.FireBroadcastAsync(new L1Proposal("p2", "u1", "cluster-y", "x", 3, DateTimeOffset.UtcNow));
        await fx.FirePort.FireBroadcastAsync(new NeuronCreated("p2", "exp.dynamic.123", "u1", DateTimeOffset.UtcNow));

        var entry = await grain.GetAsync("p2");
        entry!.Status.Should().Be(ProposalStatus.Approved);
        entry.ActivatedExperienceId.Should().Be("exp.dynamic.123");
    }

    [Fact]
    public async Task ProposalDecided_Reject_flips_to_rejected()
    {
        var grain = fx.Cluster.GrainFactory.GetGrain<IProposalLog>("singleton");
        await fx.FirePort.FireBroadcastAsync(new L1Proposal("p3", "u1", "z", "x", 3, DateTimeOffset.UtcNow));
        await fx.FirePort.FireBroadcastAsync(new ProposalDecided("p3", ProposalStatus.Rejected, "u1", DateTimeOffset.UtcNow));

        var entry = await grain.GetAsync("p3");
        entry!.Status.Should().Be(ProposalStatus.Rejected);
        entry.DecidedBy.Should().Be("u1");
    }

    [Fact]
    public async Task List_filters_by_status()
    {
        var grain = fx.Cluster.GrainFactory.GetGrain<IProposalLog>("singleton");
        await fx.FirePort.FireBroadcastAsync(new L1Proposal("pa", "u1", "a", "x", 3, DateTimeOffset.UtcNow));
        await fx.FirePort.FireBroadcastAsync(new L1Proposal("pb", "u1", "b", "y", 3, DateTimeOffset.UtcNow));
        await fx.FirePort.FireBroadcastAsync(new ProposalDecided("pa", ProposalStatus.Rejected, "u1", DateTimeOffset.UtcNow));

        var pending = await grain.ListAsync(ProposalStatus.Pending, 0, 100);
        pending.Select(e => e.ProposalId).Should().BeEquivalentTo(new[] { "pb" });
    }
}
```

The exact fixture API (`fx.FirePort.FireBroadcastAsync` etc.) depends on what `Ino.Testing` exposes. Read `src/Ino.Testing/InoTestSiloFixture.cs` (or similar) and adapt names. If broadcasting from a test isn't directly supported, call `grain.ReactAsync(proposal, ctx, ct)` via a cast or expose a test-only entry point.

- [ ] **Step 2: Run the tests**

```
dotnet test test/Ino.Kernel.Tests --filter "FullyQualifiedName~ProposalLogTests"
```
Expected: 4 passed.

---

## Task 9 — Tests for CortexJournal

**Files:**
- Create: `test/Ino.Kernel.Tests/CortexJournalTests.cs`

- [ ] **Step 1: Write the three tests**

```csharp
using FluentAssertions;
using Ino.Kernel.Contracts;
using Ino.Testing;
using Xunit;

namespace Ino.Kernel.Tests;

[Collection(nameof(InoTestCollection))]
public sealed class CortexJournalTests(InoTestSiloFixture fx)
{
    [Fact]
    public async Task Buffer_caps_at_20_per_user()
    {
        var journal = fx.Cluster.GrainFactory.GetGrain<ICortexJournal>("singleton");
        for (int i = 0; i < 25; i++)
        {
            await journal.RecordAsync("u1", FakeDecision($"prompt-{i}"));
        }
        var recent = await journal.GetRecentAsync("u1", 100);
        recent.Should().HaveCount(20);
    }

    [Fact]
    public async Task GetRecent_returns_newest_first()
    {
        var journal = fx.Cluster.GrainFactory.GetGrain<ICortexJournal>("singleton");
        await journal.RecordAsync("u2", FakeDecision("first"));
        await journal.RecordAsync("u2", FakeDecision("second"));
        await journal.RecordAsync("u2", FakeDecision("third"));
        var recent = await journal.GetRecentAsync("u2", 10);
        recent.Select(d => d.Prompt).Should().ContainInOrder("third", "second", "first");
    }

    [Fact]
    public async Task Multi_user_isolation()
    {
        var journal = fx.Cluster.GrainFactory.GetGrain<ICortexJournal>("singleton");
        await journal.RecordAsync("ua", FakeDecision("a-prompt"));
        await journal.RecordAsync("ub", FakeDecision("b-prompt"));
        var ua = await journal.GetRecentAsync("ua", 10);
        var ub = await journal.GetRecentAsync("ub", 10);
        ua.Should().ContainSingle(d => d.Prompt == "a-prompt");
        ub.Should().ContainSingle(d => d.Prompt == "b-prompt");
    }

    private static RoutingDecision FakeDecision(string prompt) => new(
        Prompt: prompt,
        Source: RoutingSource.Regex,
        ExperienceId: "test.exp",
        Confidence: 1.0,
        At: DateTimeOffset.UtcNow,
        MlPrediction: null,
        MlConfidence: null,
        LlmCalled: false,
        RoutingDurationMs: 1,
        CorrelationId: "corr-1");
}
```

- [ ] **Step 2: Run**

```
dotnet test test/Ino.Kernel.Tests --filter "FullyQualifiedName~CortexJournalTests"
```
Expected: 3 passed.

---

## Task 10 — Tests for CreatorNeuron approval gating

**Files:**
- Create: `domains/genesis/Ino.Domains.Genesis.Tests/CreatorNeuronApprovalGatingTests.cs`

- [ ] **Step 1: Write the three tests**

```csharp
using FluentAssertions;
using Ino.Core.Hosting.Registry;
using Ino.Kernel.Contracts;
using Ino.Testing;
using NSubstitute;
using Xunit;

namespace Ino.Domains.Genesis.Tests;

[Collection(nameof(InoTestCollection))]
public sealed class CreatorNeuronApprovalGatingTests(InoTestSiloFixture fx)
{
    [Fact]
    public async Task Gating_on_stashes_draft_and_suppresses_NeuronCreated()
    {
        var registry = Substitute.For<IExperienceRegistry>();
        registry.ApprovalRequired.Returns(true);

        var firePort = Substitute.For<IFirePort>();
        var sut = new CreatorNeuron(registry, firePort, NullLogger<CreatorNeuron>.Instance);

        var proposal = new L1Proposal("p1", "u1", "cluster", "x", 3, DateTimeOffset.UtcNow);
        await sut.ReactAsync(proposal, fx.NewContext(), CancellationToken.None);

        await registry.Received(1).StashDraftAsync("p1", Arg.Any<DraftExperience>(), Arg.Any<CancellationToken>());
        await registry.DidNotReceive().RegisterAsync(Arg.Any<DraftExperience>(), Arg.Any<CancellationToken>());
        await firePort.DidNotReceive().FireBroadcast(Arg.Any<NeuronCreated>(), Arg.Any<NeuronContext>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ApproveAsync_registers_and_broadcasts_NeuronCreated()
    {
        // Through the registry: stash → approve → registered + broadcast.
        // Use the real ExperienceRegistry impl in fx for an integration-style test.
        var grain = fx.Cluster.GrainFactory.GetGrain<ICreatorNeuron>("genesis");
        var proposal = new L1Proposal("p2", "u1", "cluster", "x", 3, DateTimeOffset.UtcNow);
        await fx.FirePort.FireBroadcastAsync(proposal);

        var approved = await fx.Registry.ApproveAsync("p2", "admin-u1", CancellationToken.None);
        approved.Should().BeTrue();

        // Wait briefly for the broadcast to land in ProposalLog.
        await Task.Delay(200);
        var log = fx.Cluster.GrainFactory.GetGrain<IProposalLog>("singleton");
        var entry = await log.GetAsync("p2");
        entry!.Status.Should().Be(ProposalStatus.Approved);
        entry.ActivatedExperienceId.Should().NotBeNull();
    }

    [Fact]
    public async Task RejectAsync_discards_stash()
    {
        var proposal = new L1Proposal("p3", "u1", "cluster", "x", 3, DateTimeOffset.UtcNow);
        await fx.FirePort.FireBroadcastAsync(proposal);

        var rejected = await fx.Registry.RejectAsync("p3", "admin-u1", CancellationToken.None);
        rejected.Should().BeTrue();

        // Subsequent approve should fail (no stash).
        var approved = await fx.Registry.ApproveAsync("p3", "admin-u1", CancellationToken.None);
        approved.Should().BeFalse();
    }
}
```

The `fx.Registry` accessor is hypothetical — adapt to whatever `InoTestSiloFixture` exposes. If it doesn't expose the registry, resolve it via DI: `fx.Cluster.ServiceProvider.GetRequiredService<IExperienceRegistry>()`.

- [ ] **Step 2: Run**

```
dotnet test domains/genesis/Ino.Domains.Genesis.Tests
```
Expected: all tests pass (including any pre-existing).

---

## Task 11 — Adapt the existing E.2 L1 acceptance test

**Files:**
- Modify: whichever test file in `test/Ino.Kernel.Tests/` covers the L1 loop end-to-end (find via `Grep pattern="L1Loop|MissedIntent" path="test/" type="cs"`)

- [ ] **Step 1: Find the test**

Look for the acceptance test from Slice E.2 that sends 3 unrouted prompts and asserts the 4th routes successfully.

- [ ] **Step 2: Insert an explicit Approve step**

Today the test relies on auto-registration. After Task 7 lands, `ApprovalRequired=true` is the default, so the 4th prompt won't route until approval. Add this step right after the 3rd prompt (where the proposal is broadcast) and before the 4th:

```csharp
// Phase 4 epilogue Slice 3: registry now defaults to ApprovalRequired=true.
// Approve the proposal so the 4th prompt actually routes.
var log = cluster.GrainFactory.GetGrain<IProposalLog>("singleton");
var pending = await log.ListAsync(ProposalStatus.Pending, 0, 1);
pending.Should().HaveCount(1);
var approved = await registry.ApproveAsync(pending[0].ProposalId, "test-user", CancellationToken.None);
approved.Should().BeTrue();
```

- [ ] **Step 3: Run the adapted test**

```
dotnet test test/Ino.Kernel.Tests --filter "FullyQualifiedName~L1"
```
Expected: pass.

---

## Task 12 — Build full solution and commit 3A

- [ ] **Step 1: Full clean build**

```
cd D:\ino
dotnet build ino.slnx --no-incremental
```
The `--no-incremental` is mandatory after `[GenerateSerializer]` record changes (memory rule from E.2 ship — otherwise spurious `CodecNotFoundException`).

Expected: clean.

- [ ] **Step 2: Full test pass**

```
dotnet test ino.slnx --no-build
```
Expected: all green.

- [ ] **Step 3: Commit**

```
git add src/Ino.Kernel.Contracts/ src/Ino.Kernel/ProposalLog.cs src/Ino.Kernel/CortexJournal.cs src/Ino.Kernel/CortexNeuron.cs src/Ino.Core.Hosting/Registry/ domains/genesis/Ino.Domains.Genesis/Neurons/CreatorNeuron.cs test/Ino.Kernel.Tests/ProposalLogTests.cs test/Ino.Kernel.Tests/CortexJournalTests.cs test/Ino.Kernel.Tests/L1LoopTests.cs domains/genesis/Ino.Domains.Genesis.Tests/CreatorNeuronApprovalGatingTests.cs

git commit -m "$(cat <<'EOF'
feat(poc): ProposalLog + CortexJournal grains + ApprovalRequired gating

Backend half of Inspector E.3. New kernel-pinned grains capture L1
proposal lifecycle (Pending → Approved/Rejected) and per-user routing
decisions (last 20). IExperienceRegistry gains an ApprovalRequired
flag (default true) plus a draft-stash flow; CreatorNeuron now stashes
drafts until the user approves via the inspector.

E.2 L1LoopTests adapted to call ApproveAsync after the proposal lands —
exercising the gating path that's now production behavior.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
git push
```

---

# Sub-commit 3B — gRPC RPCs

## Task 13 — Extend `ino.proto`

**Files:**
- Modify: `src/Ino.Gateway.Grpc/Protos/ino.proto`

- [ ] **Step 1: Read the current proto**

```
Read src/Ino.Gateway.Grpc/Protos/ino.proto
```
Note the import block, the `service Ino` declaration, and existing message styles.

- [ ] **Step 2: Add the imports if missing**

At the top of the file, ensure these are imported:
```proto
import "google/protobuf/timestamp.proto";
```

- [ ] **Step 3: Add the three RPCs to the existing `service Ino` block**

Inside the service body (alongside `Chat`, `FireSynapse`, etc.), add:

```proto
rpc ListProposals(ListProposalsRequest) returns (ListProposalsResponse);
rpc DecideProposal(DecideProposalRequest) returns (DecideProposalResponse);
rpc ListRoutingDecisions(ListRoutingDecisionsRequest) returns (ListRoutingDecisionsResponse);
```

- [ ] **Step 4: Add the message and enum types at the bottom of the file**

```proto
enum ProposalStatusProto {
  PROPOSAL_STATUS_PENDING = 0;
  PROPOSAL_STATUS_APPROVED = 1;
  PROPOSAL_STATUS_REJECTED = 2;
}

enum RoutingSourceProto {
  ROUTING_SOURCE_UNROUTED = 0;
  ROUTING_SOURCE_REGEX = 1;
  ROUTING_SOURCE_ML = 2;
  ROUTING_SOURCE_LLM = 3;
}

message ListProposalsRequest {
  optional ProposalStatusProto filter = 1;
  int32 skip = 2;
  int32 take = 3;
}

message ProposalView {
  string proposal_id = 1;
  string cluster_key = 2;
  string example_prompt = 3;
  int32 occurrences = 4;
  google.protobuf.Timestamp proposed_at = 5;
  ProposalStatusProto status = 6;
  optional string activated_experience_id = 7;
  optional google.protobuf.Timestamp decided_at = 8;
}

message ListProposalsResponse {
  repeated ProposalView entries = 1;
}

message DecideProposalRequest {
  string proposal_id = 1;
  ProposalStatusProto decision = 2;       // APPROVED or REJECTED
  optional string override_script_body = 3;  // reserved for future UI
}

message DecideProposalResponse {
  bool accepted = 1;
}

message ListRoutingDecisionsRequest {
  int32 count = 1;     // server caps at 20
}

message RoutingDecisionView {
  string prompt = 1;
  RoutingSourceProto source = 2;
  optional string experience_id = 3;
  optional double confidence = 4;
  google.protobuf.Timestamp at = 5;
  optional double ml_prediction = 6;
  optional double ml_confidence = 7;
  bool llm_called = 8;
  int32 routing_duration_ms = 9;
  string correlation_id = 10;
}

message ListRoutingDecisionsResponse {
  repeated RoutingDecisionView entries = 1;
}
```

- [ ] **Step 5: Build the gRPC project — confirms protoc generates clean C# stubs**

```
dotnet build src/Ino.Gateway.Grpc/Ino.Gateway.Grpc.csproj
```
Expected: clean. If errors, the proto syntax is the issue — re-check imports and field IDs.

---

## Task 14 — Extend `IInoGateway`

**Files:**
- Modify: `src/Ino.Gateway/IInoGateway.cs`
- Modify: `src/Ino.Gateway/InoGateway.cs`

- [ ] **Step 1: Add three async methods to `IInoGateway`**

```csharp
Task<IReadOnlyList<ProposalEntry>> ListProposalsAsync(string userId, ProposalStatus? filter, int skip, int take, CancellationToken ct);
Task DecideProposalAsync(string userId, string proposalId, ProposalStatus decision, CancellationToken ct);
Task<IReadOnlyList<RoutingDecision>> ListRoutingDecisionsAsync(string userId, int count, CancellationToken ct);
```

- [ ] **Step 2: Implement in `InoGateway.cs`**

```csharp
public async Task<IReadOnlyList<ProposalEntry>> ListProposalsAsync(
    string userId, ProposalStatus? filter, int skip, int take, CancellationToken ct)
{
    var grain = grainFactory.GetGrain<IProposalLog>("singleton");
    var entries = await grain.ListAsync(filter, skip, take);
    // Filter by userId for the per-user view.
    return entries.Where(e => e.UserId == userId).ToArray();
}

public async Task DecideProposalAsync(
    string userId, string proposalId, ProposalStatus decision, CancellationToken ct)
{
    if (decision == ProposalStatus.Pending)
        throw new ArgumentException("Pending is not a decision", nameof(decision));

    var ok = decision == ProposalStatus.Approved
        ? await registry.ApproveAsync(proposalId, userId, ct)
        : await registry.RejectAsync(proposalId, userId, ct);

    if (!ok) return;  // unknown proposal; no broadcast.

    await firePort.FireBroadcast(
        new ProposalDecided(proposalId, decision, userId, DateTimeOffset.UtcNow),
        NeuronContext.System,  // or the existing system-context idiom
        ct);
}

public async Task<IReadOnlyList<RoutingDecision>> ListRoutingDecisionsAsync(
    string userId, int count, CancellationToken ct)
{
    var grain = grainFactory.GetGrain<ICortexJournal>("singleton");
    return await grain.GetRecentAsync(userId, Math.Min(count, 20));
}
```

The `firePort` and `registry` constructor parameters are likely already injected — if not, add them. `NeuronContext.System` is hypothetical; use the gateway's existing context-construction pattern.

- [ ] **Step 3: Build**

```
dotnet build src/Ino.Gateway/Ino.Gateway.csproj
```
Expected: clean.

---

## Task 15 — Implement the three gRPC handlers

**Files:**
- Modify: `src/Ino.Gateway.Grpc/Services/InoGrpcService.cs`

- [ ] **Step 1: Add three handler overrides**

```csharp
public override async Task<ListProposalsResponse> ListProposals(
    ListProposalsRequest request, ServerCallContext context)
{
    var userId = context.GetCallerUserId();   // existing helper
    ProposalStatus? filter = request.HasFilter ? (ProposalStatus)request.Filter : null;
    var entries = await gateway.ListProposalsAsync(userId, filter, request.Skip, request.Take, context.CancellationToken);
    var resp = new ListProposalsResponse();
    resp.Entries.AddRange(entries.Select(ToView));
    return resp;
}

public override async Task<DecideProposalResponse> DecideProposal(
    DecideProposalRequest request, ServerCallContext context)
{
    var userId = context.GetCallerUserId();
    await gateway.DecideProposalAsync(userId, request.ProposalId, (ProposalStatus)request.Decision, context.CancellationToken);
    return new DecideProposalResponse { Accepted = true };
}

public override async Task<ListRoutingDecisionsResponse> ListRoutingDecisions(
    ListRoutingDecisionsRequest request, ServerCallContext context)
{
    var userId = context.GetCallerUserId();
    var count = request.Count <= 0 ? 20 : Math.Min(request.Count, 20);
    var entries = await gateway.ListRoutingDecisionsAsync(userId, count, context.CancellationToken);
    var resp = new ListRoutingDecisionsResponse();
    resp.Entries.AddRange(entries.Select(ToView));
    return resp;
}
```

Plus two private converters:
```csharp
private static ProposalView ToView(ProposalEntry e) => new()
{
    ProposalId = e.ProposalId,
    ClusterKey = e.ClusterKey,
    ExamplePrompt = e.ExamplePrompt,
    Occurrences = e.Occurrences,
    ProposedAt = Timestamp.FromDateTimeOffset(e.ProposedAt),
    Status = (ProposalStatusProto)e.Status,
    ActivatedExperienceId = e.ActivatedExperienceId,
    DecidedAt = e.DecidedAt is { } d ? Timestamp.FromDateTimeOffset(d) : null,
};

private static RoutingDecisionView ToView(RoutingDecision d) => new()
{
    Prompt = d.Prompt,
    Source = (RoutingSourceProto)d.Source,
    ExperienceId = d.ExperienceId,
    Confidence = d.Confidence,
    At = Timestamp.FromDateTimeOffset(d.At),
    MlPrediction = d.MlPrediction,
    MlConfidence = d.MlConfidence,
    LlmCalled = d.LlmCalled,
    RoutingDurationMs = d.RoutingDurationMs,
    CorrelationId = d.CorrelationId,
};
```

The `context.GetCallerUserId()` is a hypothetical extension method — find the existing user-id-extraction helper in the gRPC service (likely an extension on `ServerCallContext` or `Caller.FromMetadata(...)`). Reuse it.

- [ ] **Step 2: Build**

```
dotnet build src/Ino.Gateway.Grpc/Ino.Gateway.Grpc.csproj
```
Expected: clean.

---

## Task 16 — Tests for the gRPC handlers

**Files:**
- Create: `test/Ino.Hosting.Tests/InoGrpcServiceInspectorRpcsTests.cs`

- [ ] **Step 1: Write the three tests**

```csharp
using FluentAssertions;
using Grpc.Core;
using Ino.Kernel.Contracts;
using NSubstitute;
using Xunit;

namespace Ino.Hosting.Tests;

public sealed class InoGrpcServiceInspectorRpcsTests
{
    [Fact]
    public async Task ListProposals_passes_filter_through()
    {
        var gateway = Substitute.For<IInoGateway>();
        gateway.ListProposalsAsync("u1", ProposalStatus.Pending, 0, 50, Arg.Any<CancellationToken>())
            .Returns(new[]
            {
                new ProposalEntry("p1", "u1", "c", "x", new[] { "x" }, 3, DateTimeOffset.UtcNow,
                    ProposalStatus.Pending, null, null, null),
            });

        var sut = new InoGrpcService(gateway, /* deps */);
        var ctx = TestContext.For("u1");
        var resp = await sut.ListProposals(
            new ListProposalsRequest { Filter = ProposalStatusProto.ProposalStatusPending, Skip = 0, Take = 50 },
            ctx);

        resp.Entries.Should().ContainSingle(e => e.ProposalId == "p1");
    }

    [Fact]
    public async Task DecideProposal_Approve_calls_gateway_with_Approved()
    {
        var gateway = Substitute.For<IInoGateway>();
        var sut = new InoGrpcService(gateway, /* deps */);
        var ctx = TestContext.For("u1");

        var resp = await sut.DecideProposal(
            new DecideProposalRequest
            {
                ProposalId = "p1",
                Decision = ProposalStatusProto.ProposalStatusApproved,
            },
            ctx);

        resp.Accepted.Should().BeTrue();
        await gateway.Received(1).DecideProposalAsync("u1", "p1", ProposalStatus.Approved, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ListRoutingDecisions_caps_count_at_20()
    {
        var gateway = Substitute.For<IInoGateway>();
        gateway.ListRoutingDecisionsAsync("u1", 20, Arg.Any<CancellationToken>())
            .Returns(Array.Empty<RoutingDecision>());

        var sut = new InoGrpcService(gateway, /* deps */);
        var ctx = TestContext.For("u1");

        await sut.ListRoutingDecisions(new ListRoutingDecisionsRequest { Count = 100 }, ctx);

        await gateway.Received(1).ListRoutingDecisionsAsync("u1", 20, Arg.Any<CancellationToken>());
    }
}
```

The `TestContext.For("u1")` helper is hypothetical — find the existing pattern for fabricating a `ServerCallContext` with user-id metadata in `test/Ino.Hosting.Tests/` (likely there's already one).

- [ ] **Step 2: Run**

```
dotnet test test/Ino.Hosting.Tests --filter "FullyQualifiedName~Inspector"
```
Expected: 3 passed.

---

## Task 17 — Build, full test pass, commit 3B

- [ ] **Step 1: Clean rebuild**

```
dotnet build ino.slnx --no-incremental
dotnet test ino.slnx --no-build
```
Expected: clean + green.

- [ ] **Step 2: Commit**

```
git add src/Ino.Gateway.Grpc/Protos/ino.proto src/Ino.Gateway/IInoGateway.cs src/Ino.Gateway/InoGateway.cs src/Ino.Gateway.Grpc/Services/InoGrpcService.cs test/Ino.Hosting.Tests/InoGrpcServiceInspectorRpcsTests.cs

git commit -m "$(cat <<'EOF'
feat(poc): Inspector gRPC RPCs (proposals + routing decisions)

Three new RPCs on the existing Ino service: ListProposals,
DecideProposal, ListRoutingDecisions. Server-side filtering by user
id from gRPC metadata; routing-decision count capped at 20.
DecideProposal fires a ProposalDecided broadcast so ProposalLog
updates its state.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
git push
```

---

# Sub-commit 3C — Flutter Routing + Proposals tabs

## Task 18 — Regenerate Dart gRPC stubs

- [ ] **Step 1: Find the regen tooling**

```
Read clients/ino.flutter/pubspec.yaml
ls clients/ino.flutter/tool 2>$null
```
Look for a script under `tool/` named `protoc.dart` or similar, or a section in `pubspec.yaml` under `scripts:`.

- [ ] **Step 2: Run the regen**

If a `tool/protoc.dart` exists:
```
cd clients/ino.flutter
dart run tool/protoc.dart
```

Otherwise the typical Dart gRPC plugin invocation (Windows-friendly):
```
cd clients/ino.flutter
flutter pub get
flutter pub run grpc:protoc_plugin --dart_out=grpc:lib/grpc/generated -I../../src/Ino.Gateway.Grpc/Protos ../../src/Ino.Gateway.Grpc/Protos/ino.proto
```
The exact protoc arguments may need to mirror what the project already does. If the tooling pattern is unclear, ask the user — don't guess and produce malformed stubs.

- [ ] **Step 3: VERIFY timestamps changed**

```
ls clients/ino.flutter/lib/grpc/generated/
```
Confirm `ino.pb.dart`, `ino.pbgrpc.dart`, etc. all have a fresh modification time (within the last few minutes). **If timestamps are unchanged, the regen failed silently** — fix the tooling before continuing. Stale stubs return `12 UNIMPLEMENTED` from the new RPCs at runtime, which looks like a backend bug.

- [ ] **Step 4: Verify analyzer is clean on the generated files**

```
flutter analyze lib/grpc/generated/
```
Expected: no issues. (Generated code is excluded from lints in most projects, so this should always be clean.)

---

## Task 19 — Create `proposals_bloc.dart`

**Files:**
- Create: `clients/ino.flutter/lib/state/proposals_bloc.dart`

- [ ] **Step 1: Write the BLoC**

```dart
import 'dart:async';

import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/grpc/ino_client.dart';
import 'package:ino_flutter/grpc/generated/ino.pb.dart';

sealed class ProposalsEvent {}

class ProposalsRefreshRequested extends ProposalsEvent {}

class ProposalApproved extends ProposalsEvent {
  ProposalApproved(this.proposalId);
  final String proposalId;
}

class ProposalRejected extends ProposalsEvent {
  ProposalRejected(this.proposalId);
  final String proposalId;
}

sealed class ProposalsState {}

class ProposalsLoading extends ProposalsState {}

class ProposalsLoaded extends ProposalsState {
  ProposalsLoaded({required this.pending, required this.approved, required this.rejected});
  final List<ProposalView> pending;
  final List<ProposalView> approved;
  final List<ProposalView> rejected;
}

class ProposalsError extends ProposalsState {
  ProposalsError(this.message);
  final String message;
}

class ProposalsBloc extends Bloc<ProposalsEvent, ProposalsState> {
  ProposalsBloc(this._client) : super(ProposalsLoading()) {
    on<ProposalsRefreshRequested>(_onRefresh);
    on<ProposalApproved>(_onApprove);
    on<ProposalRejected>(_onReject);
    _timer = Timer.periodic(const Duration(seconds: 5), (_) {
      add(ProposalsRefreshRequested());
    });
    add(ProposalsRefreshRequested());
  }

  final InoClient _client;
  Timer? _timer;

  Future<void> _onRefresh(ProposalsRefreshRequested e, Emitter<ProposalsState> emit) async {
    try {
      final resp = await _client.listProposals(ListProposalsRequest()..take = 100);
      final pending = resp.entries.where((p) => p.status == ProposalStatusProto.PROPOSAL_STATUS_PENDING).toList();
      final approved = resp.entries.where((p) => p.status == ProposalStatusProto.PROPOSAL_STATUS_APPROVED).toList();
      final rejected = resp.entries.where((p) => p.status == ProposalStatusProto.PROPOSAL_STATUS_REJECTED).toList();
      emit(ProposalsLoaded(pending: pending, approved: approved, rejected: rejected));
    } catch (ex) {
      emit(ProposalsError(ex.toString()));
    }
  }

  Future<void> _onApprove(ProposalApproved e, Emitter<ProposalsState> emit) async {
    await _client.decideProposal(DecideProposalRequest()
      ..proposalId = e.proposalId
      ..decision = ProposalStatusProto.PROPOSAL_STATUS_APPROVED);
    add(ProposalsRefreshRequested());
  }

  Future<void> _onReject(ProposalRejected e, Emitter<ProposalsState> emit) async {
    await _client.decideProposal(DecideProposalRequest()
      ..proposalId = e.proposalId
      ..decision = ProposalStatusProto.PROPOSAL_STATUS_REJECTED);
    add(ProposalsRefreshRequested());
  }

  @override
  Future<void> close() {
    _timer?.cancel();
    return super.close();
  }
}
```

`InoClient.listProposals` and `decideProposal` come from the regenerated `ino.pbgrpc.dart`. The exact method names depend on what protoc-dart produces — they should mirror the proto rpc names in lowerCamel.

- [ ] **Step 2: Verify analyzer**

```
flutter analyze lib/state/proposals_bloc.dart
```
Expected: clean.

---

## Task 20 — Create `routing_bloc.dart`

**Files:**
- Create: `clients/ino.flutter/lib/state/routing_bloc.dart`

- [ ] **Step 1: Write the BLoC**

```dart
import 'dart:async';

import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/grpc/ino_client.dart';
import 'package:ino_flutter/grpc/generated/ino.pb.dart';

sealed class RoutingEvent {}

class RoutingRefreshRequested extends RoutingEvent {}

sealed class RoutingState {}

class RoutingLoading extends RoutingState {}

class RoutingLoaded extends RoutingState {
  RoutingLoaded(this.entries);
  final List<RoutingDecisionView> entries;
}

class RoutingError extends RoutingState {
  RoutingError(this.message);
  final String message;
}

class RoutingBloc extends Bloc<RoutingEvent, RoutingState> {
  RoutingBloc(this._client) : super(RoutingLoading()) {
    on<RoutingRefreshRequested>(_onRefresh);
    _timer = Timer.periodic(const Duration(seconds: 2), (_) {
      add(RoutingRefreshRequested());
    });
    add(RoutingRefreshRequested());
  }

  final InoClient _client;
  Timer? _timer;

  Future<void> _onRefresh(RoutingRefreshRequested e, Emitter<RoutingState> emit) async {
    try {
      final resp = await _client.listRoutingDecisions(
          ListRoutingDecisionsRequest()..count = 20);
      emit(RoutingLoaded(resp.entries));
    } catch (ex) {
      emit(RoutingError(ex.toString()));
    }
  }

  @override
  Future<void> close() {
    _timer?.cancel();
    return super.close();
  }
}
```

- [ ] **Step 2: Verify analyzer**

```
flutter analyze lib/state/routing_bloc.dart
```
Expected: clean.

---

## Task 21 — Wire BLoCs into the provider tree

**Files:**
- Modify: `clients/ino.flutter/lib/main.dart` (or wherever `MultiBlocProvider` lives)

- [ ] **Step 1: Find the provider tree**

```
Grep pattern="MultiBlocProvider|BlocProvider" path="clients/ino.flutter/lib" output_mode="files_with_matches"
```
Expected: one or two files. The existing `InoBloc` should already be in the tree.

- [ ] **Step 2: Add the two new BLoCs alongside `InoBloc`**

Inside the `MultiBlocProvider.providers` list:

```dart
BlocProvider<ProposalsBloc>(
  create: (ctx) => ProposalsBloc(ctx.read<InoClient>()),
  lazy: false,
),
BlocProvider<RoutingBloc>(
  create: (ctx) => RoutingBloc(ctx.read<InoClient>()),
  lazy: false,
),
```

`lazy: false` so the polling timers start as soon as the app activates — matches user expectation that opening the inspector shows up-to-date data.

If `InoClient` isn't currently exposed via Provider, change to whatever DI pattern the app uses (e.g. `GetIt.I<InoClient>()`).

- [ ] **Step 3: Add imports**

```dart
import 'package:ino_flutter/state/proposals_bloc.dart';
import 'package:ino_flutter/state/routing_bloc.dart';
```

---

## Task 22 — Add Routing + Proposals tabs to inspector_drawer.dart

**Files:**
- Modify: `clients/ino.flutter/lib/ui/components/inspector_drawer.dart`

- [ ] **Step 1: Read the existing drawer end-to-end**

```
Read clients/ino.flutter/lib/ui/components/inspector_drawer.dart
```
Identify the existing tab/panel structure. The drawer has active panels (Identity, State, Reasoning, Metrics) plus three stub panels (Actions, Scheduling, Integrations). We add **two new active tabs**: Routing and Proposals.

- [ ] **Step 2: Add the Routing tab content**

If the drawer uses a `TabBar`/`TabBarView` pattern, add to both:
- `Tab(icon: Icon(Icons.alt_route), text: 'Routing')`
- The matching view child (a `_RoutingTab()` widget — defined below).

If it uses a `ListView` of `ExpansionTile`s, add a tile labeled "Routing" with the body shown below.

`_RoutingTab` widget body:

```dart
class _RoutingTab extends StatelessWidget {
  const _RoutingTab();

  @override
  Widget build(BuildContext context) {
    return BlocBuilder<RoutingBloc, RoutingState>(
      builder: (context, state) {
        if (state is RoutingLoading) return const Center(child: CircularProgressIndicator());
        if (state is RoutingError) return Center(child: Text('Error: ${state.message}'));
        final loaded = state as RoutingLoaded;
        if (loaded.entries.isEmpty) {
          return const Center(child: Text('No routing decisions yet — send a chat first.'));
        }
        return ListView.builder(
          itemCount: loaded.entries.length,
          itemBuilder: (ctx, i) => _RoutingCard(entry: loaded.entries[i]),
        );
      },
    );
  }
}

class _RoutingCard extends StatelessWidget {
  const _RoutingCard({required this.entry});
  final RoutingDecisionView entry;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final color = switch (entry.source) {
      RoutingSourceProto.ROUTING_SOURCE_REGEX => scheme.primary,
      RoutingSourceProto.ROUTING_SOURCE_ML => scheme.tertiary,
      RoutingSourceProto.ROUTING_SOURCE_LLM => scheme.secondary,
      RoutingSourceProto.ROUTING_SOURCE_UNROUTED => scheme.error,
      _ => scheme.onSurface.withAlpha(120),
    };
    return Card(
      child: ExpansionTile(
        leading: CircleAvatar(backgroundColor: color, radius: 6),
        title: Text(entry.prompt, maxLines: 1, overflow: TextOverflow.ellipsis),
        subtitle: Text('${entry.source.name} · ${entry.routingDurationMs}ms'),
        children: [
          Padding(
            padding: const EdgeInsets.all(12),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                if (entry.hasExperienceId()) Text('experience: ${entry.experienceId}'),
                if (entry.hasConfidence()) Text('confidence: ${entry.confidence.toStringAsFixed(3)}'),
                if (entry.hasMlConfidence()) Text('ml.confidence: ${entry.mlConfidence.toStringAsFixed(3)}'),
                Text('llm called: ${entry.llmCalled}'),
                Text('correlation: ${entry.correlationId}', style: const TextStyle(fontFamily: 'monospace', fontSize: 11)),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
```

- [ ] **Step 3: Add the Proposals tab content**

`_ProposalsTab` widget body:

```dart
class _ProposalsTab extends StatelessWidget {
  const _ProposalsTab();

  @override
  Widget build(BuildContext context) {
    return BlocBuilder<ProposalsBloc, ProposalsState>(
      builder: (context, state) {
        if (state is ProposalsLoading) return const Center(child: CircularProgressIndicator());
        if (state is ProposalsError) return Center(child: Text('Error: ${state.message}'));
        final loaded = state as ProposalsLoaded;
        return ListView(
          children: [
            _ProposalSection(title: 'Pending', items: loaded.pending, builder: _pendingTile),
            _ProposalSection(title: 'Approved', items: loaded.approved, builder: _approvedTile, initiallyExpanded: false),
            _ProposalSection(title: 'Rejected', items: loaded.rejected, builder: _rejectedTile, initiallyExpanded: false),
          ],
        );
      },
    );
  }

  Widget _pendingTile(BuildContext context, ProposalView p) {
    return Card(
      child: Padding(
        padding: const EdgeInsets.all(12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(p.exampePrompt, style: const TextStyle(fontWeight: FontWeight.bold)),
            const SizedBox(height: 4),
            Text('cluster: ${p.clusterKey} · ${p.occurrences}×'),
            const SizedBox(height: 8),
            Row(
              children: [
                FilledButton(
                  onPressed: () => context.read<ProposalsBloc>().add(ProposalApproved(p.proposalId)),
                  child: const Text('Approve'),
                ),
                const SizedBox(width: 8),
                OutlinedButton(
                  onPressed: () => context.read<ProposalsBloc>().add(ProposalRejected(p.proposalId)),
                  child: const Text('Reject'),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  Widget _approvedTile(BuildContext context, ProposalView p) {
    return ListTile(
      title: Text(p.examplePrompt),
      subtitle: Text('→ ${p.activatedExperienceId}'),
      trailing: TextButton(
        onPressed: () => context.read<InoBloc>().add(SendMessage(p.examplePrompt)),
        child: const Text('test it now'),
      ),
    );
  }

  Widget _rejectedTile(BuildContext context, ProposalView p) {
    return ListTile(
      title: Text(p.examplePrompt, style: TextStyle(color: Theme.of(context).disabledColor)),
      subtitle: Text('cluster: ${p.clusterKey}'),
    );
  }
}

class _ProposalSection extends StatelessWidget {
  const _ProposalSection({
    required this.title,
    required this.items,
    required this.builder,
    this.initiallyExpanded = true,
  });
  final String title;
  final List<ProposalView> items;
  final Widget Function(BuildContext, ProposalView) builder;
  final bool initiallyExpanded;

  @override
  Widget build(BuildContext context) {
    return ExpansionTile(
      title: Text('$title (${items.length})'),
      initiallyExpanded: initiallyExpanded && items.isNotEmpty,
      children: items.map((p) => builder(context, p)).toList(),
    );
  }
}
```

Imports to add at the top of `inspector_drawer.dart`:
```dart
import 'package:flutter_bloc/flutter_bloc.dart';
import 'package:ino_flutter/grpc/generated/ino.pb.dart';
import 'package:ino_flutter/state/proposals_bloc.dart';
import 'package:ino_flutter/state/routing_bloc.dart';
import 'package:ino_flutter/state/ino_bloc.dart';
```

- [ ] **Step 4: Wire the new tabs into the existing tab structure**

This depends on the drawer's existing layout. Two common cases:

**Case A — TabBar/TabBarView.** Add two `Tab` entries and two view children matching their indexes.

**Case B — ListView of ExpansionTile (per the survey).** Add two new tiles, one for Routing and one for Proposals, with the panel widgets above as their children.

Match the surrounding pattern; don't restructure the drawer.

- [ ] **Step 5: Verify analyzer is clean**

```
flutter analyze lib/ui/components/inspector_drawer.dart lib/state/proposals_bloc.dart lib/state/routing_bloc.dart
```
Expected: no issues.

---

## Task 23 — Build, rebuild kernel, browser verification

- [ ] **Step 1: Build Flutter web**

```
cd clients/ino.flutter
flutter build web --no-tree-shake-icons
```
Expected: build success.

- [ ] **Step 2: Hot-rebuild the kernel silo**

```
mcp__aspire__execute_resource_command(resourceName="kernel", commandName="rebuild")
```
Expected: kernel transitions Running → Building → Running.

- [ ] **Step 3: Open kernel HTTPS URL in Chrome via DevTools MCP**

```
mcp__chrome-devtools__navigate_page(url="https://localhost:<kernel-port>/")
```

- [ ] **Step 4: Drive the acceptance scenario**

1. Send a unique unrouted prompt 3× via the chat composer (or the demo strip's "Trigger L1" if Slice 1 shipped). Use a sentence like `"frobnicate the gizmo"` or whatever the demo strip generates.
2. Open the inspector drawer (top-right icon, or the demo strip's "Show last routing" chip).
3. Switch to the **Proposals** tab. Expected: one Pending entry with `Occurrences = 3` and the cluster-key/example-prompt visible. Take screenshot.
4. Click **Approve**. Expected: within 5 s the entry moves to the Approved section, and `ActivatedExperienceId` is shown. Take screenshot.
5. Send `"frobnicate the gizmo"` a 4th time via the chat composer. Expected: the response is the auto-generated stub text (`"Got it — I'll help with 'frobnicate the gizmo'. (Auto-generated from 3 unrouted prompts.)"`).
6. Switch to the **Routing** tab. Expected: at least 4 entries — first 3 marked Unrouted (red), 4th marked ML or LLM (green/amber) with the new `ExperienceId`. Take screenshot.
7. Aspire structured logs (filter by `kernel`) include lines matching `MissedIntentTracker: emitted L1Proposal`, `CreatorNeuron: stashed draft`, `CreatorNeuron: registered dynamic experience` (last one fires after Approve).

If any step fails, drill into the failure before proceeding. The most likely culprits in order: (a) stale Dart stubs, (b) a missed `userId` extraction in the gateway, (c) ProposalLog grain not getting the broadcast.

---

## Task 24 — Final commit

- [ ] **Step 1: Commit 3C**

```
git add clients/ino.flutter/lib/grpc/generated/ clients/ino.flutter/lib/state/proposals_bloc.dart clients/ino.flutter/lib/state/routing_bloc.dart clients/ino.flutter/lib/ui/components/inspector_drawer.dart clients/ino.flutter/lib/main.dart

git commit -m "$(cat <<'EOF'
feat(poc): Flutter inspector drawer — Proposals + Routing tabs

Two new tabs in the existing inspector drawer. Proposals tab shows
Pending → Approved/Rejected lifecycle with Approve/Reject buttons and
a "test it now" shortcut on Approved entries. Routing tab shows the
last 20 routing decisions per user, color-coded by source (regex /
ml / llm / unrouted), with expandable per-entry detail.

Polling — 5 s for Proposals, 2 s for Routing — drives both BLoCs.
Server-streaming RPCs deferred until v0.2.

Co-Authored-By: Claude Opus 4.7 (1M context) <noreply@anthropic.com>
EOF
)"
git push
```

---

## Done when

1. `dotnet build ino.slnx --no-incremental` clean.
2. `dotnet test ino.slnx --no-build` clean (including the adapted L1 acceptance test).
3. Browser acceptance scenario in Task 23 passes end-to-end with screenshots.
4. Three commits on `master`, pushed: 3A (backend), 3B (gRPC), 3C (Flutter).

## Out of scope

- Inspector ML pane (per-user optimizer histogram).
- Editing draft script body before approving (`override_script_body` field reserved).
- Server-streaming RPCs.
- Persistence of ProposalLog / CortexJournal.
- Cross-user proposal aggregation.
