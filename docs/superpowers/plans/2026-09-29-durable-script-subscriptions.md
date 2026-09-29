# Durable Script Subscriptions Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `brain.On<T>(source)` inside a sandbox script registers a durable, grain-side subscription that survives the process: signals published while no run exists are queued on the file neuron and wake a fresh run, which drains them through the same stream API.

**Architecture:** The file neuron (`CSharpFileNeuron`) becomes the durable end of every script subscription. When a script opens a signal stream, the edge records the subscription on the file; the file watches the source neuron (the same observer machinery `Arm` uses today) and buffers matching signals in persisted state. An open stream drains the buffer through the grain; a signal with no live run starts one. The client API (`brain.On<T>`) is unchanged — only its durability semantics upgrade.

**Tech Stack:** .NET/Orleans grains (Neuron base), xUnit v3 (`TestContext.Current.CancellationToken`), existing test harnesses `SandboxBrain`/`FakeSandbox`/`UnitBrain`. No new packages.

**Spec:** `docs/superpowers/specs/2026-09-29-programmable-brain-vision-design.md` (section "Execution: durable subscriptions, processes as cache"). This plan is slice 1 of that spec; multi-behavior packages, verification rewiring, Author/Builder changes and the `Arm`/`Trigger` deletion are later slices.

## Global Constraints

- Never add meaningless `/// <summary>` docs; small inline comments only where the code cannot say it (owner rule). Names must carry the meaning.
- Signals are published only by their source neuron — no API may let anything publish "as" another neuron (spec: provenance is inviolable).
- Run-to-run state lives in grain state, never in the sandbox process (spec).
- `CSharpFileState` is persisted: only append new `[Id(n)]` fields, never renumber (Orleans serialization).
- The existing `Arm`/`Trigger` path must stay green and untouched in behavior — its deletion is a later slice. `CSharpTriggerFacts` must pass unmodified.
- Tests run per-project (`dotnet test <project>`), not via the `.slnx` (known Windows+slnx handshake bug). Skip the 18-minute IntoChat E2E suite (owner rule); verify with unit tests plus an `aspire run` smoke at the end.
- Test style: copy the repo's facts style — `TestContext.Current.CancellationToken`, `await using var brain = await SandboxBrain.StartAsync(...)`, `Eventually` polling, one behavior per fact with a sentence-shaped name.

## Review Focus

1. A signal arriving while a wake-run is still starting (sandbox reports the run before `Running` is visible) must queue, never start a second run or vanish — pinned by `ASecondSignalDuringAWakeRunQueuesInsteadOfStartingAnotherRun` (Task 2).
2. A file deactivated (activation collected) with subscriptions must still queue and wake on the next signal — pinned by `ASubscribedFileStillWakesAfterItsActivationIsCollected` (Task 2).
3. A file with two subscriptions must never deliver one subscription's signals through the other's stream — pinned by `TwoSubscriptionsKeepTheirSignalsApart` (Task 1).
4. A forged or stopped-run token must not drain pending signals (they are another owner's data) — pinned by `DrainRefusesTokensThatNoLongerSpeakForTheFile` (Task 3).
5. A signal burst while no run exists must not grow grain state without bound — pinned by `PendingSignalsAreBoundedAndTheOldestAreDropped` (Task 1).

Known, accepted limitation to state in code comments (owner may grill later): delivery removes a signal from the buffer when it is written to the stream, so a script crashing between receipt and handling loses that one delivery. True end-to-end acknowledgement needs script-side acks; the spec's answer today is idempotent handlers over neuron state.

---

### Task 1: The file neuron records subscriptions and buffers matching signals

**Files:**
- Create: `src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp/Files/ScriptSubscription.cs`
- Modify: `src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp/Files/CSharpFileState.cs`
- Modify: `src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp/Edge/ICSharpFileEdge.cs`
- Modify: `src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp/Files/CSharpFileNeuron.cs`
- Test: `src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit/CSharpSubscriptionFacts.cs` (new)

**Interfaces:**
- Consumes: `Neuron<CSharpFileState>` base (`Snapshot`, `Save(state, signal)`), the existing observer machinery (`INeuronObserver.OnSignalAsync`, `source.Watch(...)`, the `RenewTriggerWatch`/`UnwatchTriggerAsync` pattern at `CSharpFileNeuron.cs:179-204`), `SandboxBrain.StartAsync(FakeSandbox, ct)` and the test `IPinger`/`Pinged` fixture used by `CSharpTriggerFacts`.
- Produces (Tasks 2 and 3 rely on these exact members):
  - `internal sealed record ScriptSubscription([property: Id(0)] string Neuron, [property: Id(1)] string Signal, [property: Id(2)] IReadOnlyList<string> Pending)` with `[GenerateSerializer, Alias("microsoft.csharp.script-subscription")]`.
  - `CSharpFileState.Subscriptions` — `[Id(8)] public IReadOnlyList<ScriptSubscription> Subscriptions { get; init; } = [];`
  - On `ICSharpFileEdge`:
    - `Task Subscribed(string runId, string neuron, string signal);`
    - `Task<IReadOnlyList<string>> DrainPending(string runId, string neuron, string signal);`
  - `internal sealed record CSharpPendingArrived([property: Id(0)] string FileId) : Signal` with `[GenerateSerializer, Alias("microsoft.csharp.pending-arrived")]` — the doorbell Task 3's stream loop listens for. Lives in `ScriptSubscription.cs`.
  - `CSharpFileNeuron.MaximumPending = 64` (internal const).

- [ ] **Step 1: Write the failing facts**

Create `CSharpSubscriptionFacts.cs`. Follow `CSharpTriggerFacts` exactly for harness and helpers (`SandboxBrain.StartAsync`, `Eventually`, the pinger fixture). Reach `Subscribed`/`DrainPending` the way the neuron's own tests reach the reminder: `file.AsReference<ICSharpFileEdge>()` — the interface is internal to the module and the test project already sees module internals (it tests `CSharpFileNeuron` and `SandboxRuns` directly).

```csharp
using DigitalBrain.Microsoft.CSharp;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class CSharpSubscriptionFacts
{
    [Fact]
    public async Task ASubscriptionIsRecordedOnceAndBuffersOnlyMatchingSignals()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = await Subscribed(brain, "workspace/buffering", pinger, ct);
        var edge = file.AsReference<ICSharpFileEdge>();

        // Registering the same subscription again is a no-op.
        await edge.Subscribed(RunOf(sandbox), pinger.GetGrainId().ToString(), nameof(Pinged));
        await pinger.Ping(7);

        await Eventually(async () => (await edge.DrainPending(RunOf(sandbox), pinger.GetGrainId().ToString(), nameof(Pinged))) is { Count: > 0 }, ct);
    }

    [Fact]
    public async Task DrainReturnsPendingSignalsOnceInArrivalOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = await Subscribed(brain, "workspace/draining", pinger, ct);
        var edge = file.AsReference<ICSharpFileEdge>();

        await pinger.Ping(1);
        await pinger.Ping(2);
        await Eventually(async () => (await file.Read(ct)).Subscriptions.Single().Pending == 2, ct);

        var drained = await edge.DrainPending(RunOf(sandbox), pinger.GetGrainId().ToString(), nameof(Pinged));
        Assert.Equal(2, drained.Count);
        Assert.Contains("\"number\":1", drained[0], StringComparison.Ordinal);
        Assert.Contains("\"number\":2", drained[1], StringComparison.Ordinal);
        Assert.Empty(await edge.DrainPending(RunOf(sandbox), pinger.GetGrainId().ToString(), nameof(Pinged)));
    }

    [Fact]
    public async Task TwoSubscriptionsKeepTheirSignalsApart()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var one = brain.Get<IPinger>("one");
        var two = brain.Get<IPinger>("two");
        var file = await Subscribed(brain, "workspace/apart", one, ct);
        var edge = file.AsReference<ICSharpFileEdge>();
        await edge.Subscribed(RunOf(sandbox), two.GetGrainId().ToString(), nameof(Pinged));

        await two.Ping(9);
        await Eventually(async () => (await file.Read(ct)).Subscriptions.Any(s => s.Pending > 0), ct);

        Assert.Empty(await edge.DrainPending(RunOf(sandbox), one.GetGrainId().ToString(), nameof(Pinged)));
        Assert.Single(await edge.DrainPending(RunOf(sandbox), two.GetGrainId().ToString(), nameof(Pinged)));
    }

    [Fact]
    public async Task PendingSignalsAreBoundedAndTheOldestAreDropped()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = await Subscribed(brain, "workspace/bounded", pinger, ct);
        var edge = file.AsReference<ICSharpFileEdge>();

        for (var number = 0; number < CSharpFileNeuron.MaximumPending + 5; number++) { await pinger.Ping(number); }
        await Eventually(async () => (await file.Read(ct)).Subscriptions.Single().Pending == CSharpFileNeuron.MaximumPending, ct);

        var drained = await edge.DrainPending(RunOf(sandbox), pinger.GetGrainId().ToString(), nameof(Pinged));
        Assert.Equal(CSharpFileNeuron.MaximumPending, drained.Count);
        Assert.Contains("\"number\":5", drained[0], StringComparison.Ordinal); // 0..4 were dropped
    }

    [Fact]
    public async Task SubscribingRequiresARunThatSpeaksForTheFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = brain.Get<ICSharpFile>("workspace/unauthorized");
        await file.Write("Console.WriteLine(1);", ct);
        // Never started: no run id speaks for this file.
        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => file.AsReference<ICSharpFileEdge>().Subscribed("not-a-run", pinger.GetGrainId().ToString(), nameof(Pinged)));
    }

    // Starts the file (always-on) so a real run id exists, then registers the subscription as that run.
    internal static async Task<ICSharpFile> Subscribed(UnitBrain brain, string id, IPinger source, CancellationToken ct)
    {
        var file = brain.Get<ICSharpFile>(id);
        await file.Write("Console.WriteLine(1);", ct);
        await file.Start(ct);
        await file.AsReference<ICSharpFileEdge>().Subscribed(RunOf(brain, id), source.GetGrainId().ToString(), nameof(Pinged));
        return file;
    }

    // FakeSandbox records every start; the latest start's run id is the current one.
    private static string RunOf(FakeSandbox sandbox) => sandbox.LatestRunId;

    private static Task Eventually(Func<Task<bool>> condition, CancellationToken ct) { /* copy verbatim from CSharpTriggerFacts */ }
}
```

Notes for the implementer:
- `RunOf`: check `FakeSandbox` first — if it does not already expose the latest run id, add an internal `LatestRunId` property that remembers the identifier of the last start request it served (it already parses start requests for `IsStart`). If `CSharpFileSnapshot` turns out to already expose `RunId`... it does not — do not add it; use the sandbox.
- `(await file.Read(ct)).Subscriptions` requires the snapshot change in Step 3; write the test against it now.
- Adjust the `Subscribed` helper if `Start` on `FakeSandbox` needs an exit arranged; mirror how `CSharpFileNeuronFacts.Started` arranges a running file.

- [ ] **Step 2: Run the facts to verify they fail**

```bash
dotnet test src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit --filter "CSharpSubscriptionFacts"
```
Expected: compile failure — `Subscribed`, `DrainPending`, `Subscriptions`, `MaximumPending` do not exist.

- [ ] **Step 3: Implement state, contract and neuron behavior**

`ScriptSubscription.cs`:

```csharp
namespace DigitalBrain.Microsoft.CSharp;

// One durable subscription a script declared by opening a signal stream. Pending holds the JSON of
// signals published while nothing was draining; delivery removes them, so handlers must be idempotent.
[GenerateSerializer, Alias("microsoft.csharp.script-subscription")]
internal sealed record ScriptSubscription(
    [property: Id(0)] string Neuron,
    [property: Id(1)] string Signal,
    [property: Id(2)] IReadOnlyList<string> Pending);

[GenerateSerializer, Alias("microsoft.csharp.pending-arrived")]
internal sealed record CSharpPendingArrived([property: Id(0)] string FileId) : Signal;
```

(`Signal` comes from `DigitalBrain.Contracts` — same import as `CSharpFileChanged`, which lives beside the neuron; put `CSharpPendingArrived` next to whichever file declares `CSharpFileChanged` if that reads better.)

`CSharpFileState.cs` — append only:

```csharp
    // Durable subscriptions the current script registered by streaming; they outlive its runs.
    [Id(8)] public IReadOnlyList<ScriptSubscription> Subscriptions { get; init; } = [];
```

`ICSharpFileEdge.cs`:

```csharp
internal interface ICSharpFileEdge : IGrainWithStringKey
{
    Task<RunAuthorization> Authorize(string runId);
    Task Subscribed(string runId, string neuron, string signal);
    Task<IReadOnlyList<string>> DrainPending(string runId, string neuron, string signal);
}
```

`CSharpFileNeuron.cs` — new members (adapt the trigger-watch pattern already in the file; do not duplicate it — extract a shared helper if the two stay side by side cleanly):

```csharp
    internal const int MaximumPending = 64;

    public async Task Subscribed(string runId, string neuron, string signal)
    {
        await RequireSpeakingRun(runId);
        if (!GrainId.TryParse(neuron, out _)) { throw new ArgumentException($"'{neuron}' is not a neuron id.", nameof(neuron)); }
        ArgumentException.ThrowIfNullOrWhiteSpace(signal);
        if (Snapshot.Subscriptions.Any(existing => existing.Neuron == neuron && existing.Signal == signal))
        {
            await WatchSubscribedSourcesAsync();
            return;
        }
        await Save(Snapshot with { Subscriptions = [.. Snapshot.Subscriptions, new(neuron, signal, [])] }, new CSharpFileChanged(FileId));
        await WatchSubscribedSourcesAsync();
    }

    public async Task<IReadOnlyList<string>> DrainPending(string runId, string neuron, string signal)
    {
        await RequireSpeakingRun(runId);
        var subscription = Snapshot.Subscriptions.FirstOrDefault(s => s.Neuron == neuron && s.Signal == signal);
        if (subscription is null or { Pending.Count: 0 }) { return []; }
        var drained = subscription.Pending;
        await Save(Snapshot with
        {
            Subscriptions = [.. Snapshot.Subscriptions.Select(s => s == subscription ? s with { Pending = [] } : s)],
        }, new CSharpFileChanged(FileId));
        return drained;
    }

    private async Task RequireSpeakingRun(string runId)
    {
        var authorization = await Authorize(runId);
        if (!authorization.Active) { throw new UnauthorizedAccessException("This run no longer speaks for its file."); }
    }
```

Buffering happens in the observer path. Extend `INeuronObserver.OnSignalAsync` (currently trigger-only, `CSharpFileNeuron.cs:108`) so a matching subscription enqueues via a one-way self-reference, mirroring how the trigger calls `Fire`:

```csharp
    Task INeuronObserver.OnSignalAsync(Signal signal)
    {
        if (IsArmed && signal.GetType().Name == Snapshot.Trigger!.Signal)
        { return this.AsReference<ICSharpFileTrigger>().Fire(signal); }
        return Snapshot.Subscriptions.Any(s => s.Signal == signal.GetType().Name)
            ? this.AsReference<ICSharpFileTrigger>().Enqueue(signal)
            : Task.CompletedTask;
    }

    // Buffers the signal on every subscription it matches; Task 2 adds waking a run from here.
    public async Task Enqueue(Signal signal)
    {
        var json = JsonSerializer.Serialize(signal, signal.GetType(), ScriptEdgeProtocol.Json);
        var name = signal.GetType().Name;
        var changed = false;
        var next = Snapshot.Subscriptions.Select(s =>
        {
            if (s.Signal != name) { return s; }
            changed = true;
            var pending = s.Pending.Count >= MaximumPending ? s.Pending.Skip(1).Append(json) : s.Pending.Append(json);
            return s with { Pending = [.. pending] };
        }).ToArray();
        if (!changed) { return; }
        await Save(Snapshot with { Subscriptions = next }, new CSharpPendingArrived(FileId));
    }
```

Add `Task Enqueue(Signal signal);` to the internal `ICSharpFileTrigger` interface next to `Fire`.

Source filtering — **confirmed kernel fact and the one owner decision in this plan**: `Signal` is `public record Signal;` (no source field) and `INeuronObserver.OnSignalAsync(Signal signal)` passes no source, so a grain watching two sources cannot tell their same-typed signals apart at delivery time. The single-`Trigger` path never hits this (one watched source); multiple subscriptions do, and `TwoSubscriptionsKeepTheirSignalsApart` exposes it. Two resolutions:

- **Recommended: stamp the source in the kernel.** Add `[Id(0)] public string Source { get; init; } = "";` to `Signal` (append-only, default keeps every existing signal serializable) and have the base `Neuron.PublishAsync` fill it with `this.GetGrainId().ToString()` before notifying observers. Then `Enqueue` credits only subscriptions where `s.Neuron == signal.Source && s.Signal == name`. This also serves the vision spec directly (provenance is inviolable; the future permissions slice reads a signal's source).
- Fallback if the kernel change is declined: buffer per signal type only (every same-typed subscription gets a copy), keep `DrainPending` keyed by (neuron, signal), and weaken `TwoSubscriptionsKeepTheirSignalsApart` to assert only that `two`'s queue received the signal — accepting duplicate delivery across same-typed subscriptions as a documented limitation.

The owner chooses at plan review; the executor implements whichever was chosen and does not decide alone.

- [ ] **Step 4: Run the facts to verify they pass**

```bash
dotnet test src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit --filter "CSharpSubscriptionFacts"
```
Expected: PASS (all five).

- [ ] **Step 5: Run the whole module's unit tests — the trigger path must be untouched**

```bash
dotnet test src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit
```
Expected: PASS, including `CSharpTriggerFacts`, `CSharpFileNeuronFacts`, `ScriptEdgeFacts` unmodified.

- [ ] **Step 6: Commit**

```bash
git add -A src/Modules/Microsoft/CSharp
git commit -m "Record durable script subscriptions on the file neuron and buffer their signals."
```

---

### Task 2: A buffered signal wakes a run; lifecycle keeps subscribed files waiting, not finished

**Files:**
- Modify: `src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp/Files/CSharpFileNeuron.cs`
- Modify: `src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp.Contracts/CSharpFileSnapshot.cs`
- Test: `src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit/CSharpSubscriptionFacts.cs`

**Interfaces:**
- Consumes: Task 1's `ScriptSubscription`, `Enqueue`, `Subscribed`, `DrainPending`, `MaximumPending`.
- Produces:
  - `CSharpFileSnapshot` gains `[property: Id(n)] IReadOnlyList<CSharpSubscriptionView> Subscriptions` (next free Id), with `[GenerateSerializer, Alias("microsoft.csharp.subscription-view")] public sealed record CSharpSubscriptionView([property: Id(0)] string Neuron, [property: Id(1)] string Signal, [property: Id(2)] int Pending);` in the Contracts project. (Task 1's tests read `.Subscriptions.Single().Pending` — if Task 1 is implemented first in isolation, its implementer adds this snapshot field then; whoever gets there first owns it, the shape is fixed here.)
  - Wake semantics relied on by Task 3: after `Enqueue`, if `Snapshot.ShouldRun` and no run is `Running`, a new run starts (fresh `RunId`, no trigger environment).

- [ ] **Step 1: Write the failing facts** (append to `CSharpSubscriptionFacts`)

```csharp
    [Fact]
    public async Task ASignalWithNoLiveRunWakesOne()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = await Subscribed(brain, "workspace/waking", pinger, ct);

        sandbox.ExitLatest(0);                       // the registering run ends cleanly
        await Reconcile(file);                       // reconcile observes the exit
        Assert.True((await file.Read(ct)).ShouldRun); // ...but a subscribed file is waiting, not finished

        await pinger.Ping(1);
        await Eventually(() => sandbox.Started == 2, ct);   // a wake-run started
    }

    [Fact]
    public async Task ASecondSignalDuringAWakeRunQueuesInsteadOfStartingAnotherRun()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = await Subscribed(brain, "workspace/queueing", pinger, ct);
        sandbox.ExitLatest(0);
        await Reconcile(file);

        await pinger.Ping(1);
        await Eventually(() => sandbox.Started == 2, ct);
        await pinger.Ping(2);                        // wake-run still running
        await Task.Delay(TimeSpan.FromMilliseconds(300), ct);

        Assert.Equal(2, sandbox.Started);            // no third run
        Assert.Equal(2, (await file.Read(ct)).Subscriptions.Single().Pending);
    }

    [Fact]
    public async Task ASubscribedFileStillWakesAfterItsActivationIsCollected()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = await Subscribed(brain, "workspace/collected-sub", pinger, ct);
        sandbox.ExitLatest(0);
        await Reconcile(file);

        await brain.DeactivateAsync(file, ct);
        await pinger.Ping(1);

        await Eventually(() => sandbox.Started == 2, ct);
    }

    [Fact]
    public async Task FailingWakeRunsClearTheSubscriptionsAndDisarm()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = await Subscribed(brain, "workspace/failing-sub", pinger, ct);
        sandbox.ExitLatest(0);
        await Reconcile(file);

        for (var attempt = 1; attempt <= CSharpFileNeuron.MaximumFailures; attempt++)
        {
            await pinger.Ping(attempt);
            await Eventually(() => sandbox.Started == attempt + 1, ct);
            sandbox.ExitLatest(1);
        }
        await pinger.Ping(0);
        await Eventually(async () => !(await file.Read(ct)).ShouldRun, ct);

        Assert.Empty((await file.Read(ct)).Subscriptions);
        Assert.Equal(CSharpFileNeuron.MaximumFailures + 1, sandbox.Started);
    }

    [Fact]
    public async Task StoppingClearsSubscriptionsAndNothingWakes()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await SandboxBrain.StartAsync(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var file = await Subscribed(brain, "workspace/stopped-sub", pinger, ct);

        await file.Stop(ct);
        await pinger.Ping(1);
        await Task.Delay(TimeSpan.FromMilliseconds(300), ct);

        Assert.Empty((await file.Read(ct)).Subscriptions);
        Assert.Equal(1, sandbox.Started);            // only the original registering run ever started
    }
```

`Reconcile` — copy the private helper from `CSharpFileNeuronFacts` (`file.AsReference<IRemindable>().ReceiveReminder(CSharpFileNeuron.ReconcileReminder, default)`).

- [ ] **Step 2: Run to verify the new facts fail**

```bash
dotnet test src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit --filter "CSharpSubscriptionFacts"
```
Expected: the five new facts FAIL (clean exit currently finishes the file; nothing wakes), Task 1's still pass.

- [ ] **Step 3: Implement wake and lifecycle**

All in `CSharpFileNeuron.cs`:

1. **Wake from `Enqueue`** — after buffering, mirror `Fire`'s pattern (inspect previous run, count failures, start or disarm):

```csharp
    public async Task Enqueue(Signal signal)
    {
        // ... Task 1 buffering, then:
        if (!Snapshot.ShouldRun) { return; }
        var current = await runner.InspectAsync(Snapshot.Owner, Snapshot.RunId, CancellationToken.None);
        if (current.Status == CSharpFileStatus.Running) { return; }
        var failures = current is { Status: CSharpFileStatus.Exited, ExitCode: not 0 } ? Snapshot.Failures + 1 : Snapshot.Failures;
        if (failures >= MaximumFailures)
        {
            await StopReconcilingAsync();
            await UnwatchAllAsync();
            await Save(Snapshot with { ShouldRun = false, Failures = failures, Subscriptions = [] }, new CSharpFileChanged(FileId));
            return;
        }
        await StartRunAsync(Snapshot with { Failures = failures }, trigger: null, CancellationToken.None);
    }
```

2. **Clean exit of a subscribed file waits instead of finishing** — in `ReceiveReminder`'s switch (`CSharpFileNeuron.cs:149-168`), guard the two terminal `Exited` cases:

```csharp
            case CSharpFileStatus.Exited when run.ExitCode == 0 && Snapshot.Subscriptions.Count > 0:
                return;   // waiting for the next signal; the reminder keeps the watches alive
            case CSharpFileStatus.Exited when run.ExitCode == 0:
                // existing finish branch unchanged
```

Also: an `Exited` non-zero run of a subscribed file must not be auto-restarted by the reconcile (the next signal is the next attempt, like triggers) — add `&& Snapshot.Subscriptions.Count == 0` to the restart branch, and let the reconcile re-watch sources when subscriptions exist (mirror the `Snapshot.Trigger is not null` branch at line 143).

3. **Watching** — generalize the single `_triggerSource` to the union of the trigger's neuron and every subscription's neuron. Keep one renewal timer. `OnActivateAsync` (line ~33) re-arms when `IsArmed || Snapshot.Subscriptions.Count > 0`. `UnwatchAllAsync` replaces `UnwatchTriggerAsync` internally (keep the old name delegating if it reads better); `Stop` and `Delete` call it and clear `Subscriptions = []` in their `Save`.

4. **Authorize** — a file between wake-runs must still honor its subscriptions' next run token; extend line 208:

```csharp
    public Task<RunAuthorization> Authorize(string runId)
        => Task.FromResult(new RunAuthorization(
            runId.Length > 0 && Snapshot.ShouldRun
                && (runId == Snapshot.RunId || Snapshot.Trigger is not null || Snapshot.Subscriptions.Count > 0),
            Snapshot.OwnerContext));
```

5. **Describe** — a waiting subscribed file is not `Restarting`; extend the condition at line 232 with `&& Snapshot.Subscriptions.Count == 0`, and populate the snapshot's new `Subscriptions` views.

- [ ] **Step 4: Run the module tests**

```bash
dotnet test src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit
```
Expected: PASS — all subscription facts, and the pre-existing trigger/file facts untouched.

- [ ] **Step 5: Commit**

```bash
git add -A src/Modules/Microsoft/CSharp
git commit -m "Wake a run per buffered signal and keep subscribed files waiting across exits."
```

---

### Task 3: The script edge streams durably: register, drain, doorbell

**Files:**
- Modify: `src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp/Edge/ScriptEdge.cs`
- Test: `src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit/ScriptEdgeFacts.cs`

**Interfaces:**
- Consumes: Task 1's `Subscribed`/`DrainPending`/`CSharpPendingArrived`, Task 2's wake semantics. Existing: `AuthorizeAsync`, `contracts.Find`, `brain.SubscribeAsync<Signal>` (in-memory doorbell only), `ScriptEdgeProtocol.Json`.
- Produces: `OpenSignalsAsync(token, contract, key, signal, ct)` — same signature as today (`Task<IAsyncEnumerable<string>>`), new semantics: durable. The client (`DigitalBrainConnection.On<T>` / `EdgeSignalSubscription`) is **not modified** — the SSE wire shape is unchanged.

- [ ] **Step 1: Write the failing facts** (append to `ScriptEdgeFacts`, using its existing `Brain`, `StartAsAlice` and pinger fixtures)

```csharp
    [Fact]
    public async Task SignalsPublishedBetweenRunsArriveWhenTheNextRunSubscribes()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await Brain(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var edge = brain.SiloServices.GetRequiredService<ScriptEdge>();
        var token = await StartAsAlice(brain, sandbox, "workspace-a/durable", ct);

        // First run subscribes, which registers durably, then its stream closes with the run.
        using var first = new CancellationTokenSource();
        var stream = await edge.OpenSignalsAsync(token, typeof(IPinger).FullName!, "pinger", nameof(Pinged), first.Token);
        first.Cancel();

        await pinger.Ping(41);                       // nobody is streaming: buffered, wakes a run
        await Eventually(() => sandbox.Started == 2, ct);
        var next = await NextToken(brain, sandbox, "workspace-a/durable");   // the wake-run's token

        var resumed = await edge.OpenSignalsAsync(next, typeof(IPinger).FullName!, "pinger", nameof(Pinged), ct);
        await foreach (var json in resumed.WithCancellation(ct))
        {
            Assert.Contains("\"number\":41", json, StringComparison.Ordinal);
            break;                                   // the buffered signal came first
        }
    }

    [Fact]
    public async Task ALiveStreamReceivesASignalPromptlyThroughTheDoorbell()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await Brain(sandbox, ct);
        var pinger = brain.Get<IPinger>("pinger");
        var edge = brain.SiloServices.GetRequiredService<ScriptEdge>();
        var token = await StartAsAlice(brain, sandbox, "workspace-a/live", ct);

        var stream = await edge.OpenSignalsAsync(token, typeof(IPinger).FullName!, "pinger", nameof(Pinged), ct);
        var reading = ReadOneAsync(stream, ct);
        await pinger.Ping(7);

        Assert.Contains("\"number\":7", await reading.WaitAsync(TimeSpan.FromSeconds(5), ct), StringComparison.Ordinal);
    }

    [Fact]
    public async Task DrainRefusesTokensThatNoLongerSpeakForTheFile()
    {
        var ct = TestContext.Current.CancellationToken;
        var sandbox = new FakeSandbox();
        await using var brain = await Brain(sandbox, ct);
        var edge = brain.SiloServices.GetRequiredService<ScriptEdge>();
        var token = await StartAsAlice(brain, sandbox, "workspace-a/revoked", ct);
        await edge.OpenSignalsAsync(token, typeof(IPinger).FullName!, "pinger", nameof(Pinged), ct);

        await brain.Get<ICSharpFile>("workspace-a/revoked").Stop(ct);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => edge.OpenSignalsAsync(token, typeof(IPinger).FullName!, "pinger", nameof(Pinged), ct));
    }

    private static async Task<string> ReadOneAsync(IAsyncEnumerable<string> stream, CancellationToken ct)
    {
        await foreach (var json in stream.WithCancellation(ct)) { return json; }
        throw new InvalidOperationException("The stream ended without a signal.");
    }
```

`NextToken`: the run token reaches a real script through its environment (`ScriptRunEnvironment.Create` puts it there via `RunTokens.Issue`). In the fake sandbox, read the latest start request's environment the same way `CSharpTriggerFacts` reads `CSharpFile__Trigger` from `sandbox.Requests` (`runs[i].Body!["environment"]!["DigitalBrain__Token"]` — check the exact environment key in `ScriptRunEnvironment.Create` and `ScriptEdgeProtocol.TokenSetting`, which spells it `DigitalBrain:Token`, so the variable is `DigitalBrain__Token`). Extract a small helper; if `StartAsAlice` already does this, reuse its mechanism.

- [ ] **Step 2: Run to verify they fail**

```bash
dotnet test src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit --filter "ScriptEdgeFacts"
```
Expected: the three new facts FAIL — today's `OpenSignalsAsync` is purely in-memory (nothing buffered between runs) and never registers on the file. The pre-existing `SignalsReachTheScriptAsJsonOfTheRequestedTypeOnly` must KEEP PASSING throughout this task — it defines the wire behavior we are preserving.

- [ ] **Step 3: Rewrite `OpenSignalsAsync` over the durable path**

Replace `ScriptEdge.OpenSignalsAsync` + `Matching` (lines 47-66):

```csharp
    // Authorizes and registers the durable subscription before returning, so a refusal surfaces
    // before the stream starts. Delivery drains the file's buffer; an in-memory doorbell on the
    // file keeps latency low, with a timeout sweep as the correctness fallback.
    public async Task<IAsyncEnumerable<string>> OpenSignalsAsync(string? token, string contract, string key, string signal, CancellationToken cancellationToken)
    {
        var (claims, owner) = await AuthorizeAsync(token).ConfigureAwait(false);
        if (grains.GetGrain(contracts.Find(contract), key) is not INeuron source) { throw new ArgumentException($"{contract} is not a neuron."); }
        Stamp(owner, claims);
        var file = grains.GetGrain<ICSharpFileEdge>(claims.File);
        await file.Subscribed(claims.Run, source.GetGrainId().ToString(), signal).ConfigureAwait(false);
        var doorbell = await brain.SubscribeAsync<Signal>(grains.GetGrain<ICSharpFile>(claims.File), cancellationToken).ConfigureAwait(false);
        return Draining(file, claims.Run, source.GetGrainId().ToString(), signal, doorbell, cancellationToken);
    }

    private static async IAsyncEnumerable<string> Draining(
        ICSharpFileEdge file, string run, string neuron, string signal,
        ISignalSubscription<Signal> doorbell, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using (doorbell)
        {
            var rings = doorbell.ReadAllAsync(cancellationToken).GetAsyncEnumerator(cancellationToken);
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    foreach (var json in await file.DrainPending(run, neuron, signal).ConfigureAwait(false)) { yield return json; }
                    var ring = rings.MoveNextAsync().AsTask();
                    // The doorbell is best-effort; the sweep guarantees progress if a ring is missed.
                    await Task.WhenAny(ring, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken)).ConfigureAwait(false);
                }
            }
            finally { await rings.DisposeAsync().ConfigureAwait(false); }
        }
    }
```

Notes:
- The doorbell subscribes to the **file** neuron and reacts to any of its signals (`CSharpPendingArrived` and `CSharpFileChanged` both fine — a spurious drain returns empty). No filtering needed.
- `ICSharpFile` is public and `INeuron`, so `brain.SubscribeAsync<Signal>` accepts it.
- The `ring` task may complete after cancellation — swallow nothing; `MoveNextAsync` honors the token via `ReadAllAsync`.
- Keep `DrainPending`'s per-call authorization: a `Stop` mid-stream turns the next drain into `UnauthorizedAccessException`, ending the stream — that is the behavior `TokensStopSpeakingForAFileOnceItStops` establishes for invokes; the new fact pins it for streams at open time.
- Verify `SignalsReachTheScriptAsJsonOfTheRequestedTypeOnly` still passes: same JSON per signal, same requested-type-only filter (now enforced by the per-(neuron, signal) buffer instead of `Matching`).

- [ ] **Step 4: Run the full module test project**

```bash
dotnet test src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit
```
Expected: PASS across all facts, old and new.

- [ ] **Step 5: Commit**

```bash
git add -A src/Modules/Microsoft/CSharp
git commit -m "Serve script signal streams from the durable subscription buffer with a doorbell."
```

---

### Task 4: Whole-system verification and the substrate's README

**Files:**
- Modify: `src/Modules/Microsoft/CSharp/README.md` (if present — `Glob src/Modules/Microsoft/CSharp/*.md`; create it only if the sibling modules' README convention says so, e.g. `src/Modules/Time/README.md` exists)

**Interfaces:**
- Consumes: everything above.
- Produces: a green branch the next slice (multi-behavior packages) builds on.

- [ ] **Step 1: Run every test project the change can reach, per-project**

```bash
dotnet test src/Modules/Microsoft/CSharp/DigitalBrain.Modules.Microsoft.CSharp.Tests.Unit
```
```bash
dotnet test src/Modules/DigitalBrain/Apps/DigitalBrain.Modules.Apps.Tests.Unit
```
```bash
dotnet test src/Applications/IntoChat/Tests/Unit
```
Expected: PASS. (`Apps` and `IntoChat` unit tests consume `ICSharpFile`/`CSharpFileSnapshot` — the snapshot gained a field, which is additive, but run them to prove it.)

- [ ] **Step 2: Build and run the AppHost as a smoke test** (owner rule: aspire build, run, test after changes; use the aspire MCP tools to start it and check resources are healthy, then stop it)

Expected: all resources healthy; no startup regressions from the module change.

- [ ] **Step 3: Update the module README's behavior description**

Document, in the README's existing voice, the two subscription modes the file now has: `Arm` (run-per-signal with `brain.Trigger<T>()`, unchanged) and durable script subscriptions (`brain.On<T>(source)` registers on the file; signals buffer up to 64 per subscription and wake runs; delivery is at-least-once down to the stream write, so handlers dedup through neuron state). Note that `Arm`'s replacement by subscriptions is planned for the consolidation slice of the vision spec.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "Verify durable subscriptions across dependent suites and document the two modes."
```

---

## Self-review notes (already applied)

- Spec coverage for this slice: registration (`On<>` → durable), buffering with watermark-as-queue, wake-on-signal, restart-from-top compatibility (a woken run re-subscribes and drains — Tasks 1+3 together), lifecycle (waiting/disarm/stop), unchanged client API. Deliberately out of slice: multi-file apps, `Arm` deletion, verification rewiring, hot/cold policy tuning (the doorbell + wake already give hot-while-streaming behavior).
- The one open kernel question is flagged inside Task 1 Step 3 (does `Signal` carry its source?) with an explicit stop-and-ask instruction rather than a silent design choice.
- Type consistency: `Subscribed(string runId, string neuron, string signal)`, `DrainPending(string runId, string neuron, string signal) → IReadOnlyList<string>`, `ScriptSubscription(Neuron, Signal, Pending)`, `CSharpSubscriptionView(Neuron, Signal, Pending:int)`, `MaximumPending = 64` — used identically in all four tasks.
