# Framework Foundation and Time Pilot Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the durable programming kernel with typed calls, persisted module state, live subscriptions, and a reusable test harness; migrate Time as the first real module.

**Architecture:** Keep the existing Contracts, DigitalBrain, and Testing package locations, replacing their contents in an isolated migration checkout. Production and tests use the same neuron/client/registration code; tests replace storage and external dependencies at their actual seams. A temporary foundation solution makes the partial migration explicit while the full solution remains the final product inventory.

**Tech Stack:** .NET SDK `11.0.100-rc.1.26425.128`, `net11.0`, C#, Orleans `10.3.1`, xUnit `xunit.v3.mtp-v2` `4.0.0`, Microsoft.Testing.Platform, genuine SDK file-based apps.

**Spec:** [Approved framework simplification design](../specs/2026-09-19-framework-simplification-design.md).

**Status:** Approved for Native execution and implemented through Tasks 1–8 on `codex/framework-foundation`. Final independent review is recorded in the migration status. See that status for deviations and the intentionally partial build boundary.

## Global Constraints

- Persist module state. Live signals and C# behaviors can restart without replay.
- Make a clean break in contracts and start with fresh stored state.
- Migrate modules incrementally.
- No generic durable command execution, durable signal delivery, workflow checkpointing, replay interpreter, or compatibility facade.
- Production behavior files reference production packages/projects only.
- Use the pinned SDK/package versions above; no unrelated upgrades.
- Keep xUnit assertions in test projects and runner configuration in `Module.Tests.props`.
- Use an isolated migration checkout/branch; preserve the user's existing Flutter generated-file changes in the original checkout.
- Do not merge this partial migration into `master` or deploy it. The full solution will have unmigrated consumers until later stages.
- Do not report the whole migration green while only the foundation solution passes.
- No HTTP server, Aspire launch, credentials, external provider access, or Docker requirement for the default foundation suite.
- Source/API sketches below specify intended implementation; verify their compilation against the pinned SDK during each red/green cycle.

## Review Focus

1. Cancellation during observer registration: release the local object reference even if the remote call completes late; remove the possible remote registration with bounded cleanup. Task 2.
2. Grain deactivation while a client still has a live subscription: an activation identity change must fault the old subscription instead of silently implying continuous observation. Task 2.
3. State write fails and reloading state also fails: reject further work on that activation rather than exposing tentative state. Tasks 4 and 5.
4. A stale reminder arrives after stop/reschedule: it must not complete the new schedule or emit a duplicate fact. Task 6.
5. A behavior faults before subscription readiness, or its read never returns: fail the test within its deadline and clean up resources. Task 3.

## Scope, build boundary, and execution order

Tasks are sequential: 1 -> 2 -> 3 -> 4 -> 5 -> 6 -> 7 -> 8. They share interfaces, so parallel implementation is not recommended. Review gates can be independent.

This plan implements Stage A and the Time portion of Stage B. Memory, AI/MCP separation, integrations, UI, IntoChat, deployment, and deletion of their legacy consumers require follow-on plans. No production HTTP work is needed for this pilot; when introduced later it must be opt-in to testing.

At execution start, use the worktree skill to create an isolated checkout on `codex/framework-foundation`. Bring these approved documents into that checkout explicitly; untracked files do not automatically follow a worktree. Record the original commit in `docs/migration/foundation-status.md`. Do not change `digitalbrainnew`; it is a reference.

Create `DigitalBrain.Foundation.slnx` containing only these projects:

```xml
<Solution>
  <Project Path="src/Modules/DigitalBrain/Contracts/DigitalBrain.Contracts.csproj" />
  <Project Path="src/Modules/DigitalBrain/DigitalBrain/DigitalBrain.csproj" />
  <Project Path="src/Testing/DigitalBrain.Testing/DigitalBrain.Testing.csproj" />
  <Project Path="src/Modules/DigitalBrain/Tests/DigitalBrain.Runtime.Tests.csproj" />
</Solution>
```

Task 5 adds the three Time projects. Keep `DigitalBrain.slnx` as the unmigrated product inventory. Do not add `Compile Remove` entries to hide obsolete sources. Delete replaced kernel sources and their obsolete tests in the migration branch; explicitly list the downstream projects this breaks in the migration-status document. This is a replacement branch, not an old/new interoperability strategy.

Use these commands from the migration checkout; each task identifies the relevant project:

```powershell
dotnet test --project src/Modules/DigitalBrain/Tests/DigitalBrain.Runtime.Tests.csproj -p:CodeGraphRefresh=false
dotnet test --project src/Modules/Time/Tests/DigitalBrain.Modules.Time.Tests.csproj -p:CodeGraphRefresh=false
dotnet build DigitalBrain.Foundation.slnx -c Release -p:CodeGraphRefresh=false
dotnet test --solution DigitalBrain.Foundation.slnx -c Release --no-build
```

Do not ignore exit code 8 in the foundation solution: both test projects must contain real tests. A compile failure is an initial red only when a new API genuinely does not exist; after scaffolding, establish behavioral failures before implementing each behavior. Commit only explicitly listed task files after the relevant checks pass.

## File ownership map

All paths are relative to `E:/intochat/digitalbrain` or its isolated migration checkout.

| Files | Responsibility |
| --- | --- |
| `src/Modules/DigitalBrain/Contracts/{Signal,INeuron,INeuronObserver}.cs` | Serializable fact base and observer wire contracts |
| `src/Modules/DigitalBrain/DigitalBrain/{IDigitalBrain,Neuron,BrainClient,SignalSubscription,BrainOptions,BrainHosting}.cs` | Production authoring interface, publication, subscription lifecycle, registration |
| `src/Testing/DigitalBrain.Testing/{BrainTestHost,BrainTestOptions,SignalProbe,BehaviorRun,TestWait}.cs` | Real cluster, typed observations, behavior ownership, bounded waiting |
| `src/Testing/DigitalBrain.Testing/{FileGrainStorage,FileReminderTable,StorageFaults}.cs` | Serialization-backed test persistence and controlled state-storage faults |
| `src/Modules/DigitalBrain/Tests/{TestNeurons,SubscriptionFacts,SubscriptionLifetimeFacts,TestHarnessFacts,PersistenceFacts}.cs` | Foundation behavior tests; fake domain model confined here |
| `src/Modules/Time/Contracts/{ITimer,TimerSnapshot,TimerStatus,TimerResolution,TimerElapsed}.cs` | Typed Time API and facts |
| `src/Modules/Time/Time/{TimerNeuron,TimerState,TimeHosting}.cs` | Timer domain logic, persisted state, direct reminder ownership |
| `src/Modules/Time/Tests/{TimerFacts,TimerReminderFacts,TimerBehaviorFacts,TimerTestSupport}.cs` | Real module and reusable behavior tests |
| `src/Behaviors/{timer-report.cs,TimerReport.cs}` | File-based production entry point and testable behavior |
| `DigitalBrain.Foundation.slnx`, `.github/workflows/foundation.yml`, `docs/migration/foundation-status.md` | Honest partial-build scope and migration tracking |

`IDigitalBrain` and `SignalSubscription<T>` live in the production runtime assembly, so contracts do not acquire a runtime dependency. Typed module clients already reference the runtime for hosting/client creation. Namespace for core wire types: `DigitalBrain.Contracts`; production helpers: `DigitalBrain.Core`; Time retains `DigitalBrain.Time`.

## Task 1: Replace the kernel with one working subscription path

**Files:** Create the foundation solution/status document, the six production files and three wire-contract files above, `BrainTestHost.cs`, `BrainTestOptions.cs`, `TestNeurons.cs`, and `SubscriptionFacts.cs`. Modify existing runtime/contracts/testing/runtime-test project files and `src/Testing/README.md`. Delete replaced legacy kernel files and legacy-only testing files; retain `FileGrainStorage.cs`, `FileReminderTable.cs`, and `TestWait.cs` for later tasks. Delete `BrainSimulationFacts.cs` and `BehaviorMailboxFacts.cs` from this migration runtime test project; record that their old guarantees are intentionally retired.

**Interfaces produced:**

```csharp
// DigitalBrain.Contracts
[GenerateSerializer, Alias("brain.signal")]
public record Signal;
public interface INeuron : IGrainWithStringKey
{
    Task<Guid> Watch(INeuronObserver observer);
    Task Unwatch(INeuronObserver observer);
}
public interface INeuronObserver : IGrainObserver
{
    Task Hear(Signal signal);
}

// DigitalBrain.Core
public interface IDigitalBrain
{
    T Get<T>(string id) where T : class, IGrainWithStringKey;
    Task<SignalSubscription<T>> SubscribeAsync<T>(INeuron source,
        CancellationToken cancellationToken = default) where T : Signal;
}
// SignalSubscription<T> : IAsyncDisposable, produced by BrainClient only
// IAsyncEnumerable<T> ReadAllAsync(CancellationToken cancellationToken = default)
// Task Completion { get; } -- terminal subscription result, not business completion
// Neuron : Grain, INeuron
// protected Task PublishAsync(Signal signal)
// BrainClient(IClusterClient client, IOptions<BrainOptions> options, ILogger<BrainClient> logger)
// BrainClient : IDigitalBrain, IAsyncDisposable
// BrainHosting.AddDigitalBrain(this ISiloBuilder silo) : ISiloBuilder
// BrainHosting.AddDigitalBrain(this IClientBuilder client) : IClientBuilder

// DigitalBrain.Testing
public sealed record BrainTestOptions
{
    public Action<ISiloBuilder>? ConfigureSilo { get; init; }
    public Action<IClientBuilder>? ConfigureClient { get; init; }
    public string? PersistenceDirectory { get; init; }
}
// BrainTestHost : IAsyncDisposable
// static Task<BrainTestHost> StartAsync(BrainTestOptions? options = null,
//     CancellationToken cancellationToken = default)
// IDigitalBrain Brain { get; }
// IGrainFactory Grains { get; }
// BrainTestConnection Connection { get; } -- actual fixture gateway URI(s), ClusterId, ServiceId
// public sealed record BrainTestConnection(Uri[] Gateways, string ClusterId, string ServiceId);
```

- [ ] Add the failing happy-path test. `ITestEmitter : INeuron` exposes `Task Emit(int value)`; its grain derives from `Neuron` and emits `[GenerateSerializer] public sealed record Number([property: Id(0)] int Value) : Signal`. Define `IOtherEmitter` with a different grain implementation to test same-key isolation.

```csharp
[Fact]
public async Task ReadySubscribersHearOnePublication()
{
    var ct = TestContext.Current.CancellationToken;
    await using var host = await BrainTestHost.StartAsync(cancellationToken: ct);
    var source = host.Brain.Get<ITestEmitter>("source");
    await using var first = await host.Brain.SubscribeAsync<Number>(source, ct);
    await using var second = await host.Brain.SubscribeAsync<Number>(source, ct);
    await using var a = first.ReadAllAsync(ct).GetAsyncEnumerator(ct);
    await using var b = second.ReadAllAsync(ct).GetAsyncEnumerator(ct);
    await source.Emit(42);
    Assert.True(await a.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct));
    Assert.True(await b.MoveNextAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5), ct));
    Assert.Equal(42, a.Current.Value);
    Assert.Equal(42, b.Current.Value);
}
```

- [ ] Run the runtime test command and capture the expected missing-interface/behavior failure.
- [ ] Replace core contracts/runtime in the isolated checkout. Implement explicit registration, `IClusterClient.GetGrain<T>`, observer registration before returning the subscription, and typed filtering in the local observer. `Watch` returns an activation-scoped GUID and is idempotent per observer. Use Orleans `ObserverManager<INeuronObserver>` for membership, with observed/logged notification failures. Never execute a user's behavior body inside the observer callback; it only queues facts.
- [ ] Use one bounded channel per subscription, with `FullMode = BoundedChannelFullMode.Wait`, `SingleReader = true`, and `TryWrite` in the observer callback. A failed `TryWrite` terminates that subscription with an overflow error. Do not use a drop mode. Finish lease/cleanup behavior in Task 2 before treating this API as ready for module use.
- [ ] Implement `BrainTestHost` using `InProcessTestClusterBuilder(1)` and the same registration methods. Register memory grain storage named `Default` initially. Resolve its production `BrainClient` from client services; dispose the client before the cluster. Run user overrides after defaults. Populate `Connection` from the actual deployed fixture configuration for Task 7's separate client process; never hardcode a port. Startup failure must dispose the partially created cluster. No web host.
- [ ] Remove runtime journaling/reminder/JSON-journal package references, obsolete internals access for Flutter, the BehaviorRuntime project reference from runtime tests, and corresponding obsolete test helpers (`BrainSimulation`, `ReactionWait`, file/fault journal storage). Keep the SDK serializer generator where tests declare grain types. Retain reminder test storage only for the upcoming Time pilot, with an explicit `Microsoft.Orleans.Reminders` reference in Testing rather than relying on the removed runtime dependency.
- [ ] Add same-key/different-grain-type and unrelated-signal tests. Emit a target-type sentinel after unrelated facts; read through the sentinel and assert only expected typed facts. This creates a processing checkpoint instead of an immediate-empty assertion.
- [ ] Run the runtime suite and foundation build. Record unmigrated full-solution failures as scope, not repaired by shims. Commit: `refactor: replace durable kernel with typed live neurons`.

## Task 2: Make subscriptions bounded, renewable, and safely disposable

**Files:** Modify `BrainClient.cs`, `SignalSubscription.cs`, `BrainOptions.cs`, `Neuron.cs`; create `SubscriptionLifetimeFacts.cs`; extend `TestNeurons.cs` with test-only controlled Watch/Unwatch failure grains. No new product grain management API.

**Consumes:** Task 1 APIs. **Produces:** same public surface with complete lifetime semantics. `BrainOptions` has `BufferCapacity = 256`, `ObserverLease = 5 minutes`, `RenewEvery = 1 minute`, `OperationTimeout = 10 seconds`. Validate positive values, `BufferCapacity > 0`, and `RenewEvery + OperationTimeout < ObserverLease`. Both silo and client receive compatible settings; reject invalid local configuration at startup.

- [ ] Write overflow and cancellation tests; use small test options while retaining production defaults. Failure grain controls are test-owned and must not become public core methods.

```csharp
// In SubscriptionLifetimeFacts; host is configured with BufferCapacity = 1.
await using var stream = await host.Brain.SubscribeAsync<Number>(source, ct);
await source.Emit(1);
await source.Emit(2);
await Assert.ThrowsAsync<InvalidOperationException>(
    () => stream.Completion.WaitAsync(TimeSpan.FromSeconds(5), ct));
```

- [ ] Run the new tests red. Add cases for cancellation before registration, cancellation during held registration, Watch failure, Unwatch failure, double disposal, and disposal while a consumer is blocked.
- [ ] Implement a single idempotent lifetime owner per subscription: observer reference, channel, renewal task, linked cancellation, and terminal exception. Release the object reference in `finally` even if Unwatch fails. Bound Watch/Unwatch waits. If canceled registration completes late, observe its task and attempt bounded Unwatch; the lease limits stale server membership if the server is unreachable. Log cleanup failure without replacing the original subscription failure.
- [ ] Renew with the same observer reference through `Watch`. Compare each returned activation GUID with the initial one. A new GUID or renewal exception faults `Completion` and the reader; do not auto-replay or auto-resume. Remove any newly registered observer after detecting activation change. Cancellation is not reported as a renewal error.
- [ ] After initial registration succeeds, emit a structured `SubscriptionReady` log with source grain identity and signal type, without payloads. This is the production diagnostic event used by Task 7's process smoke test; do not emit it when merely starting a behavior task.
- [ ] Test healthy renewal over more than one full lease using short integration-test lease settings with generous deadlines. Separately test failure deterministically with controlled Watch replies; do not rely only on sleeps or process timing.
- [ ] Test a test-owned grain's deactivation: subscribe, deactivate it through its test-only method, await renewal, and assert the old stream fails. A new subscription can then receive a new publication; the old publication is not replayed.
- [ ] Ensure `BrainClient.DisposeAsync` ends all owned subscriptions and observes renewal tasks. Lifecycle counters in test doubles prove reference cleanup on exceptional paths. Run the runtime suite. Commit: `fix: bound and supervise live subscription lifetimes`.

## Task 3: Add shared observations, behavior ownership, and diagnostic waits

**Files:** Create `SignalProbe.cs`, `BehaviorRun.cs`, `TestHarnessFacts.cs`; modify `BrainTestHost.cs`, `TestWait.cs`, and testing README.

**Consumes:** the real `IDigitalBrain` and subscriptions. **Produces:**

```csharp
// BrainTestHost additions
// Task<SignalProbe<T>> ObserveAsync<T>(INeuron source, CancellationToken ct = default)
//     where T : Signal
// BehaviorRun RunBehavior(Func<IDigitalBrain, CancellationToken, Task> body,
//     CancellationToken ct = default)
// SignalProbe<T> : IAsyncDisposable
// Task<T> NextAsync(Func<T, bool>? predicate = null, CancellationToken ct = default)
// IReadOnlyList<T> Snapshot { get; } -- bounded diagnostic snapshot
// BehaviorRun : IAsyncDisposable
// Task Completion { get; }
// Task WaitForSubscriptionAsync<T>(INeuron source, CancellationToken ct = default)
//     where T : Signal
// TestWait.UntilAsync<T>(Func<CancellationToken, Task<T>> read, Func<T, bool> done,
//     TimeSpan timeout, CancellationToken cancellationToken) : Task<T>
```

- [ ] Write a test that starts a behavior, waits for its real subscription, emits once, and observes one result. Define `ObserveOne` locally in the test so no hidden behavior implementation is required.

```csharp
var observed = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
async Task ObserveOne(IDigitalBrain brain, CancellationToken token)
{
    await using var subscription = await brain.SubscribeAsync<Number>(source, token);
    await foreach (var number in subscription.ReadAllAsync(token))
    {
        observed.TrySetResult(number.Value);
        return;
    }
}
await using var run = host.RunBehavior(ObserveOne, ct);
await run.WaitForSubscriptionAsync<Number>(source, ct);
await source.Emit(7);
Assert.Equal(7, await observed.Task.WaitAsync(TimeSpan.FromSeconds(5), ct));
await run.Completion.WaitAsync(TimeSpan.FromSeconds(5), ct);
```

- [ ] Add failing tests for a behavior throwing before it subscribes, two runs subscribing to the same source, a never-completing read, parent cancellation, a probe timeout, and a behavior that ignores cancellation. Verify the last one fails teardown within a deadline rather than hanging.
- [ ] Implement a private per-run `IDigitalBrain` decorator which delegates every call to the production client and records readiness only after `SubscribeAsync` returns. Key readiness by full grain identity and signal type; each behavior run owns its own readiness set. Race readiness waits against `Completion`, cancellation, and timeout. The decorator is instrumentation, not another signal transport.
- [ ] Implement one consumer loop per probe. Keep at most 256 diagnostic observations; overflow of the underlying subscription still faults. `NextAsync` uses a 5-second default deadline and propagates transport/run errors rather than swallowing them. No resend or domain mutation in waits.
- [ ] Bound each asynchronous predicate read with the linked deadline, not only the delay. Distinguish caller cancellation from timeout. Include operation name, source/type, last observation, and known behavior failure in timeout messages; redact payload bodies by default.
- [ ] Dispose behaviors before probes/client/cluster. Cancel and join each behavior with a 5-second shutdown deadline, ignore only expected cancellation, and report unexpected faults. A noncooperative task cannot be force-killed in-process: report that failure, then continue host cleanup in `finally`.
- [ ] Run the runtime suite. Document usage without a custom base class or assertion DSL. Commit: `feat: add reusable brain observations and behavior fixtures`.

## Task 4: Prove persisted state and controlled storage failures

**Files:** Modify `FileGrainStorage.cs`, `BrainTestHost.cs`, `BrainTestOptions.cs`; create `StorageFaults.cs` and `PersistenceFacts.cs`; extend `TestNeurons.cs` with `ICounter`/`CounterNeuron`.

**Consumes:** Tasks 1–3. **Produces:** `StorageFaults.FailNextWrite(GrainId)`, `FailNextRead(GrainId)`, `HoldNextRead(GrainId) : ReadHold`, with `ReadHold.Entered : Task`, `Release()`, and `Dispose()`; `BrainTestOptions.StorageFaults`; `BrainTestHost.RestartSiloAsync(CancellationToken)`; test counter operations `Task<int> Read()`, `Task Set(int value)`, `Task SetWithoutPublishing(int value)`, `Task Deactivate()`.

- [ ] Write a persistent-store test using two complete host lifetimes and the same explicitly owned temporary directory. Dispose the first host before starting the second; use identical grain identity and storage name. The fixture deletes only its own verified temporary directory at the end.

```csharp
await using (var first = await BrainTestHost.StartAsync(new() { PersistenceDirectory = directory }, ct))
{
    await first.Brain.Get<ICounter>("saved").Set(23);
}
await using (var second = await BrainTestHost.StartAsync(new() { PersistenceDirectory = directory }, ct))
{
    Assert.Equal(23, await second.Brain.Get<ICounter>("saved").Read());
}
```

- [ ] Run red. Add failed-write tests and storage-adapter tests: serialized copies do not share mutable references, missing/cleared state resets the returned state, stale ETag write is rejected, corrupt data fails visibly, and a held read releases during fixture cleanup.
- [ ] Rework file storage as an explicitly single-process test adapter. Serialize reads/writes for a store/key across provider instances, compare ETags before replacement, write a temporary file then atomically replace, and update the caller's ETag only after success. Reject concurrent host ownership of the same store. Do not claim this tests multi-process Azure consistency.
- [ ] Wrap `IGrainStorage`, not journal storage, for faults. Faults target a particular grain so parallel tests cannot steal one another's next failure. Hold objects use asynchronous continuations and are released on teardown. Add one controlled read-failure option for the refused-write/reload-failure test.
- [ ] In the counter, inject `IPersistentState<CounterState>` directly. On write failure, reload; if reload fails mark that activation unavailable, call `DeactivateOnIdle`, and throw. Do not publish a success fact. This is module-level discipline, not a new persistence base class.

```csharp
var previous = await counter.Read();
faults.FailNextWrite(counter.GetGrainId());
await Assert.ThrowsAsync<IOException>(() => counter.Set(previous + 1));
Assert.Equal(previous, await counter.Read());
```

- [ ] Prove the allowed commit/publication gap by committing through `SetWithoutPublishing`, restarting the host, and asserting state survived while a fresh subscription receives only a newly emitted sentinel. Also test deactivation recovery and refused-write/reload-failure behavior. Do not claim this controlled gap is a hard-process-crash test.
- [ ] Run storage and runtime tests, including the existing subscription suite after restart support. Commit: `test: verify module state recovery and storage failures`.

## Task 5: Migrate Time to direct typed operations and state

**Files:** Modify Time project files, `ITimer.cs`, `TimerSnapshot.cs`, `TimerState.cs`, `TimerNeuron.cs`, and the foundation solution. Create `TimerElapsed.cs`, `TimeHosting.cs`, `TimerFacts.cs`, and `TimerTestSupport.cs`. Delete old Time command/JSON/plumbing files: `ScheduleTimer.cs`, `StopTimer.cs`, `SchedulingBody.cs`, `TimerGeneration.cs`, `TimerElapsedBody.cs`, `TimeJson.cs`, `TimeSignals.cs`, `TimeModule.cs`, `TimerAlarmGrain.cs`, `ITimerAlarm.cs`, and `Configuration/TimeOptions.cs`. Removing the alarm source here is required because its referenced legacy contract types are gone; Task 6 restores scheduling through direct reminder ownership.

**Consumes:** production neuron and direct persistence; shared harness. **Produces:**

```csharp
public interface ITimer : INeuron
{
    Task<TimerSnapshot> Schedule(int durationSeconds, string note, long? expectedGeneration = null);
    Task<TimerSnapshot> Stop();
    Task<TimerSnapshot> Read();
}
[GenerateSerializer, Alias("time.elapsed")]
public sealed record TimerElapsed(
    [property: Id(0)] string TimerId,
    [property: Id(1)] long Generation,
    [property: Id(2)] DateTimeOffset DueAt,
    [property: Id(3)] DateTimeOffset ObservedAt,
    [property: Id(4)] TimerResolution Resolution,
    [property: Id(5)] string Note) : Signal;
// TimeHosting.AddTime(this ISiloBuilder silo) : ISiloBuilder
```

Keep `TimerSnapshot`'s existing fields and `TimerStatus`/`TimerResolution` vocabulary. State is a module-owned serializer type passed directly to `IPersistentState<TimerState>`; initialize missing state to Unscheduled. No `SnapshotEnvelope`, command ID, JSON payload, or scheduled internal signal.

- [ ] Add a real Timer grain test and run it red. `TimerTestSupport.StartAsync` returns `BrainTestHost.StartAsync` with `silo.AddTime()` and the appropriate memory/persistent storage/reminder configuration; it never substitutes a different timer grain.

```csharp
await using var host = await TimerTestSupport.StartAsync(ct);
var timer = host.Brain.Get<ITimer>("tea");
var scheduled = await timer.Schedule(60, "tea");
Assert.Equal(TimerStatus.Scheduled, scheduled.Status);
Assert.Equal(1, scheduled.Generation);
Assert.Equal(scheduled, await timer.Read());
var stopped = await timer.Stop();
Assert.Equal(TimerStatus.Cancelled, stopped.Status);
Assert.Equal(stopped, await timer.Stop());
```

- [ ] Add validation tests: duration <= 0, blank note, stale expected generation, active timer reschedule, idempotent stop, and schedule after stop. Use `ArgumentOutOfRangeException`/`ArgumentException` for bad inputs and `InvalidOperationException` for generation mismatch or an already scheduled timer. Validate before changing state or reminder registration.
- [ ] Implement direct state transitions and typed snapshots. Increment generation only for a new accepted schedule. Repeat schedule is not a generic deduplicated command; it is rejected while active. `AddTime` registers domain time. Task 6 adds the module-specific reminder availability check, without adding core-wide reminder requirements.
- [ ] Add persistence refusal and reload-failure tests against the real Timer grain using Task 4's storage seam. Prove read-after-write and recovery on a fresh host. No success result is returned before the required state write.
- [ ] Keep this task's scheduling execution red tests confined to Task 6; do not claim timers fire yet. Compile the migrated Time code without legacy runtime references. Add all Time projects to the foundation solution and run both suites. Commit: `refactor: simplify timer operations and persisted state`.

## Task 6: Let TimerNeuron own reminders and delete the alarm grain

**Files:** Modify `TimerNeuron.cs`, `TimeHosting.cs`, Time project, `FileReminderTable.cs`, and `TimerTestSupport.cs`; create `TimerReminderFacts.cs`. The old alarm files were deleted in Task 5 to keep that task compilable; replace their function here with direct reminder ownership and a module constant for its period.

**Consumes:** Task 5 contract. **Produces:** `TimerNeuron : Neuron, ITimer, IRemindable`; reminder names `due/{generation}`, period one minute; private/module-internal `OnDueAsync(long generation)` shared by `ReceiveReminder` and focused module tests, never exposed in `ITimer`.

- [ ] Write a real-runtime test: subscribe to `TimerElapsed`, schedule once, wait for the fact with a bounded deadline, assert the persisted state is Elapsed and generation matches. Allow a 90-second deadline for the small actual-reminder suite; keep ordinary tests fast. Do not send fake Tick messages in that test.
- [ ] Add focused module tests with controlled domain time for early ticks, an overdue tick, an old generation after stop/reschedule, repeated callbacks, and storage failure while elapsing. Use a test-only `TimerTestDriverGrain : TimerNeuron, ITimerTestDriver` in `TimerTestSupport.cs`; `ITimerTestDriver : ITimer` adds `Task InvokeDue(long generation)` forwarding to the production internal `OnDueAsync`. Forward the real persistent-state constructor facet, leave TimerNeuron internal but inheritable, and grant internals access only to the Time tests assembly. This executes the same production due handler on a real Orleans turn. The actual-reminder and restart tests still resolve production `ITimer`, so this helper cannot substitute for validating its registration. Do not claim controlled domain time advances the Orleans scheduler.

```csharp
// Expected overdue policy; observedAt is strictly more than one minute late.
Assert.Equal(TimerStatus.Elapsed, (await timer.Read()).Status);
Assert.Equal(TimerResolution.Recovered, elapsed.Resolution);
Assert.Equal(scheduled.Generation, elapsed.Generation);
```

- [ ] Run red. Add an explicit `Microsoft.Orleans.Reminders` package reference to the Time implementation, using the existing central version. Move reminder ownership directly into the non-reentrant timer grain. Schedule registers a generation-specific reminder before committing Scheduled state; refused persistence attempts bounded cleanup and reload. An orphan reminder sees absent/nonmatching state and unregisters itself. Do not make reminder registration plus state persistence look transactional.
- [ ] Stop persists Cancelled before unregistering. A late callback checks persisted status/generation before doing work. If unregister fails after the state commit, report the failure; a later tick performs cleanup without firing. Register/reconcile the active reminder on activation when the stored timer is still Scheduled.
- [ ] On due: if early, retain the schedule; if due, persist Elapsed, publish `TimerElapsed`, and retire the reminder. Save-before-publish permits loss of the live fact on a crash; repeated callbacks see terminal state and do not republish. More than one minute late is Recovered; otherwise OnTime. Missed reminder ticks are not individually replayed.
- [ ] Fresh-host tests reuse state and reminder stores: schedule, stop the first host, restart, and prove the due callback eventually resolves state. Faults in register/unregister belong to module-local support around the real Orleans reminder seam, not a new framework scheduler. Keep one unmodified-provider reminder integration test to validate actual wiring.
- [ ] Verify there are no remaining alarm grain or JSON/edge-delivery dependencies. Test missing reminder configuration fails startup meaningfully. Run Time and runtime suites. Commit: `refactor: own timer reminders in the timer neuron`.

## Task 7: Prove a production file-based behavior uses the same framework

**Files:** Create `src/Behaviors/timer-report.cs`, `src/Behaviors/TimerReport.cs`, `TimerBehaviorFacts.cs`; modify the Time test project to link `TimerReport.cs` only; update testing README and migration status.

**Consumes:** Tasks 1–6. **Produces:** `TimerReport.RunAsync(IDigitalBrain brain, string timerId, Func<TimerElapsed, Task> report, CancellationToken cancellationToken) : Task` and one genuine SDK file-based entry point.

- [ ] Write the behavior test against the real Time module. Run through `RunBehavior`, await the `TimerElapsed` subscription for the selected timer, schedule once, and assert the callback receives the matching ID/generation. Cancellation must release its subscription. A different timer's fact must not invoke the callback.
- [ ] Run red. Implement the reusable behavior without starting a host:

```csharp
public static class TimerReport
{
    public static async Task RunAsync(IDigitalBrain brain, string timerId,
        Func<TimerElapsed, Task> report, CancellationToken cancellationToken)
    {
        var timer = brain.Get<ITimer>(timerId);
        await using var elapsed = await brain.SubscribeAsync<TimerElapsed>(timer, cancellationToken);
        await foreach (var fact in elapsed.ReadAllAsync(cancellationToken))
            await report(fact);
    }
}
```

- [ ] Add the file entry point with these build directives and ordinary host/client composition. It connects to a separately running development silo through explicit configuration; production behavior code does not start a test cluster.

```csharp
#:project ../Modules/DigitalBrain/DigitalBrain/DigitalBrain.csproj
#:project ../Modules/Time/Contracts/DigitalBrain.Modules.Time.Contracts.csproj
#:include TimerReport.cs
#:property PublishAot=false
```

- [ ] Include `using DigitalBrain.Core;` and `using DigitalBrain.Time;` in the reusable source. In the entry point use `Host.CreateApplicationBuilder(args)`, `UseOrleansClient`, `AddDigitalBrain`, start the host, resolve `IDigitalBrain`, and pass `IHostApplicationLifetime.ApplicationStopping` to `TimerReport.RunAsync`. Use `UseLocalhostClustering` only for the explicitly documented local example. When gateway URIs are configured, use static gateway configuration and the supplied cluster/service IDs instead. Report exceptions through host logging and a nonzero process exit; no automatic behavior retries.
- [ ] Add a process smoke test mode selected by `--smoke true`: report one fact, cancel through a linked token, and treat that expected cancellation as a successful exit. Keep this as entry-point plumbing around the same behavior, not alternate domain logic. The test starts the production client process against `host.Connection`, waits for the real `SubscriptionReady` log introduced in Task 2, schedules once, asserts output/exit, and always stops the child process. Supply timer ID, gateway URIs, and cluster/service IDs through configuration. It must not rely on a fixed localhost port or credentials.
- [ ] Build with `dotnet build src/Behaviors/timer-report.cs -p:CodeGraphRefresh=false`; execute explicitly with `dotnet run --file src/Behaviors/timer-report.cs`. Link reusable source in the Time test project, leaving top-level startup out of its compile items. Verify no TestingHost/Testing dependency in the entry point's project closure.
- [ ] Run Time suite and the bounded process smoke test. Commit: `feat: demonstrate production file-based timer behavior`.

## Task 8: Add honest CI gates, document reuse, and audit deletion

**Files:** Create `.github/workflows/foundation.yml`; modify `src/Testing/README.md`, `docs/migration/foundation-status.md`, and project/package references proven unused within this scope. Do not delete central versions still used by unmigrated projects or replace the existing full-product CI job with the partial one.

**Consumes:** all previous deliverables. **Produces:** reproducible foundation validation and a migration handoff with known full-product gaps.

- [ ] Add CI using the repository's existing checkout/setup-dotnet action versions and `global.json`. Name the job `Foundation and Time only — partial migration`. Run the following; retain a 15-minute outer job deadline for slow reminder/process cases:

```powershell
dotnet restore DigitalBrain.Foundation.slnx
dotnet build DigitalBrain.Foundation.slnx -c Release --no-restore -p:CodeGraphRefresh=false
dotnet test --solution DigitalBrain.Foundation.slnx -c Release --no-build
dotnet build src/Behaviors/timer-report.cs -c Release -p:CodeGraphRefresh=false
dotnet format whitespace DigitalBrain.Foundation.slnx --verify-no-changes --no-restore
```

- [ ] Document one complete module-test example: configure real module, replace only provider dependencies, subscribe/await readiness, trigger once, assert typed result/fact, dispose. Explain live-event loss, state recovery, short default tests versus actual reminder tests, and how to run the file-based example.
- [ ] Inspect the project-reference graph for production -> Testing and Time -> legacy runtime/BehaviorRuntime dependencies. Inspect new contracts for JSON strings, command envelopes, hosting types, and test controls. Review `git diff --stat` alongside the source inventory; no `Compile Remove` graveyard.
- [ ] Run the entire foundation command set once. A failed check requires correction and rerunning affected checks; no repeated broad testing without a reason. Capture counts, durations, SDK, and any limitations in migration status.
- [ ] Run a full-solution build once as an inventory check and record the still-unmigrated projects; it is expected to fail until later migration stages. Do not patch all modules opportunistically, suppress these failures, or claim the original application builds.
- [ ] Commit: `build: validate foundation and document module migration gates`. Leave the branch unmerged. Hand off Memory as the next design scope, followed by AI/MCP dependency removal.

## Plan self-review and acceptance

| Approved requirement | Covered by |
| --- | --- |
| Typed live programming surface, production client outside tests | Tasks 1–2, 7 |
| Explicit readiness, cancellation, renewal, bounded buffers | Tasks 1–3 |
| State persistence without replay/outbox guarantees | Tasks 4–6 |
| Real module tests and reusable harness; no domain fakes in common testing | Tasks 3–6 |
| State-provider failure and pilot recovery | Tasks 4–6 |
| File-based build and production client connection | Task 7 |
| Actual deletion and honest partial migration reporting | Tasks 1, 5–6, 8 |
| HTTP/provider/application adapters | Deferred explicitly to follow-on module/application plans |

No product source is changed by writing this plan. A task is done only when its named checks have run and its resulting behavior is inspectable. This plan finishes the foundation and Time pilot, not the entire migration.

## Primary documentation checked

Context7 remains unavailable due to its quota response from the design investigation. Microsoft documentation was checked for planning; consult pinned package APIs when implementing overloads:

- [Observers](https://learn.microsoft.com/en-us/dotnet/orleans/grains/observers).
- [Grain persistence](https://learn.microsoft.com/en-us/dotnet/orleans/grains/grain-persistence/).
- [Timers and reminders](https://learn.microsoft.com/en-us/dotnet/orleans/grains/timers-and-reminders).
- [File-based apps](https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps).

## Execution choice

Recommended: **Native execution** in the current task, with implementation in an isolated checkout and one independent final review. These eight tasks depend tightly on the same subscription, host, and state semantics, so maintaining one implementation context is useful.

Alternative: **Subagent-driven execution**, with a fresh implementer and reviewer for each sequential task. This adds intermediate independent review at a higher context cost. No subagent is dispatched merely to write or self-review this plan.
