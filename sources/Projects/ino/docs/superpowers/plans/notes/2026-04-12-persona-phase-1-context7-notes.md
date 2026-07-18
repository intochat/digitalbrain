# Orleans 9 API notes — persona phase 1

> Verified 2026-04-12 for tasks 2-10 of `docs/superpowers/plans/2026-04-12-persona-phase-1-foundation.md`.
> If an API below has moved, update this file AND the corresponding code.
>
> **Source caveat:** Context7 quota was exhausted at the start of this task, so
> verification was done against (a) the official Microsoft Learn MCP server for
> `Orleans 10.0` / `Orleans 9.1` reference pages, and (b) cross-reference against
> existing working code in this worktree. The Orleans package version on Microsoft
> Learn is `Microsoft.Orleans.Core.Abstractions v10.0.0`, which is the line that
> follows Orleans 9.x — the members listed below are unchanged across 9.x → 10.x
> on every page consulted. Where the Microsoft Learn page was split between
> `?view=orleans-9.1` and `?view=orleans-10.0`, both versions list the same
> member set. If phase-1 ships against Orleans 9.x specifically, everything
> below still applies.

## IIncomingGrainCallFilter

**Interface shape** — unchanged.

```csharp
// Orleans.IIncomingGrainCallFilter
// Assembly: Orleans.Core.Abstractions.dll
// Package:  Microsoft.Orleans.Core.Abstractions v10.0.0
public interface IIncomingGrainCallFilter
{
    Task Invoke(IIncomingGrainCallContext context);
}
```

Source:
- <https://learn.microsoft.com/dotnet/api/orleans.iincominggraincallfilter.invoke?view=orleans-10.0>
- <https://learn.microsoft.com/dotnet/orleans/grains/interceptors#incoming-call-filters>

**IIncomingGrainCallContext members** — all surfaced via the base `IGrainCallContext`
interface. Confirmed member list (from <https://learn.microsoft.com/dotnet/api/orleans.igraincallcontext?view=orleans-10.0>):

| Member | Type | Notes |
|---|---|---|
| `Grain` | `IAddressable` | The grain being invoked. Cast to a typed interface (e.g. `context.Grain is IAgent`) for allowlist checks — already done in `features/timetravel/Timetravel.Core/TimelineCallFilter.cs:44-46`. |
| `InterfaceMethod` | `MethodInfo` | `MethodInfo` of the interface method. Nullable in practice — `TimelineCallFilter.cs:54` uses `context.InterfaceMethod?.Name`. |
| `InterfaceName` | `string` | **Non-null** string name of the interface being invoked. Available since Orleans 7.2.5. **This is the phase-1 recommendation** — no reflection, no `?.` chain, no type confusion. |
| `InterfaceType` | `Orleans.Runtime.GrainInterfaceType` | **Routing-token struct, NOT `System.Type`.** An earlier version of this notes file incorrectly claimed this was a `System.Type` — it's a value-type identifier Orleans uses for internal routing and does NOT expose a `.Name` property that matches the managed interface's simple name. Use `InterfaceName` (above) when you want the string. |
| `MethodName` | `string` | Name of the method being invoked, without reflection. |
| `ImplementationMethod` | `MethodInfo` | `MethodInfo` of the implementation class. May not be in the grain class itself when grain extensions (Streams, CancellationTokens) are involved — see the "grain extensions" note below. |
| `Arguments` | `object[]` (indexer) | Access method arguments positionally. |
| `Request` | `IInvokable` | Lower-level access. Exposes `GetArgumentCount()` / `GetArgument(int)` — used in `TimelineCallFilter.cs:122-135` to pick an `AgentRecord` out of registry calls. |
| `Response` | — | Get/set response (incoming side). |
| `Result` | `object?` | Get/set the result after awaiting `Invoke()`. Modify here to intercept return values. |
| `SourceId` | `GrainId?` | Identity of the caller if available. |
| `TargetId` | `GrainId` | Identity of the target grain. Used in `TimelineCallFilter.cs:51` as `context.TargetId.ToString()`. |
| `TargetContext` | `IGrainContext` (incoming-only) | Grain context of the target — `Orleans.IIncomingGrainCallContext.TargetContext`. Only on the incoming-specific derived interface, not the base. |

**Preferred way to get the target grain's interface name.** Two options, in order of preference:

1. **`context.InterfaceName`** — non-null string shortcut. Zero reflection, zero `?.`, zero type confusion. Available since Orleans 7.2.5 (confirmed via Microsoft Learn for orleans-10.0). **Use this in `PersonaSignalFilter`.** Paired with `context.MethodName` (also non-null string) for the method name.
2. **`context.InterfaceMethod?.DeclaringType?.Name`** — the reflection fallback `TimelineCallFilter.cs:78` currently uses. Still works, but costs an extra indirection and nullable-handling. Existing usage is grandfathered; don't start new usages.

**Do NOT use `context.InterfaceType.Name`** — `InterfaceType` is a `GrainInterfaceType` routing-token struct, not a `System.Type`, so `.Name` doesn't give the managed interface name and `?.` doesn't apply. This was an error in an earlier version of these notes; Task 7's first implementation hit the compile error and had to fall back. The correct answer is the string shortcut on `InterfaceName`.

**Filter ordering and short-circuit semantics** — from
<https://learn.microsoft.com/dotnet/orleans/grains/interceptors#grain-call-filter-ordering>:

1. DI-registered `IIncomingGrainCallFilter` implementations run in registration order.
2. Grain-level filter (if the grain itself implements `IIncomingGrainCallFilter`) runs next.
3. Grain method or grain extension implementation runs last.

Each filter's `Invoke` **MUST `await` or return `context.Invoke()`** to execute the next
filter / the target method. Short-circuit by NOT calling `context.Invoke()` and setting
`context.Result` yourself — the target method never runs. Exceptions thrown from
`context.Invoke()` propagate out of the awaited Task; callers see the exception at the
grain-call site.

**Phase-1 ordering implication:** `PersonaSignalFilter` will be registered alongside
the existing `TimelineCallFilter`. Both are additive (each awaits `context.Invoke()`
first, then captures). Registration order in the silo configurator determines the
observation order but has no functional effect because neither filter mutates
`context.Result`. Registering `PersonaSignalFilter` AFTER `TimelineCallFilter` keeps
the timeline filter closer to the caller (timeline captures every call even if
persona projection throws).

**Registration — both paths are official.**

```csharp
// Preferred — extension method on ISiloBuilder, namespace Orleans.Hosting.
// Source: TimelineSiloExtensions.cs:18 in this worktree.
siloBuilder.AddIncomingGrainCallFilter<PersonaSignalFilter>();

// Alternative — raw DI registration, also documented as official:
siloBuilder.Services.AddSingleton<IIncomingGrainCallFilter, PersonaSignalFilter>();
```

Both are recommended on <https://learn.microsoft.com/dotnet/orleans/grains/interceptors#silo-wide-grain-call-filters>.
**Use `AddIncomingGrainCallFilter<T>()`** to match the existing `TimelineSiloExtensions.AddTimelineCapture` pattern.

**Grain-extension gotcha.** `context.ImplementationMethod` is NOT always a method on
the grain class itself — Orleans uses grain extensions for Streams, CancellationTokens,
and other infrastructure. Filters observe those calls too. For persona filtering we
should match on `context.Grain` / `context.InterfaceName` (narrow allowlist of real
agent interfaces) exactly the way `TimelineCallFilter.cs:44-46` does, otherwise the
persona signal stream floods with infra chatter.

**Known-at-risk vs older docs:** nothing. The Orleans 9 / 10 pages list the same
signature. Older pre-3.x docs referenced an obsolete `IGrainCallFilter` and a
`Method` property marked `[Obsolete("Use InterfaceMethod or ... ImplementationMethod instead")]`
— already long gone; do not resurrect.

## ObserverManager<T>

**Namespace:** `Orleans.Utilities` (not `Orleans.Runtime.Utilities`).
Already in use at `features/timetravel/Timetravel.Core/TimelineGrain.cs:3` — `using Orleans.Utilities;`.

**Two generic forms exist in Orleans 9/10:**

- `Orleans.Utilities.ObserverManager<TObserver>` — identity-by-observer (keyed by the observer itself, `where TObserver : IAddressable`). Used by `TimelineGrain`.
- `Orleans.Utilities.ObserverManager<TIdentity, TObserver>` — explicit identity key, useful when the same observer may want to resubscribe under different client sessions. Phase-1 persona does NOT need this — use the single-parameter form.

**Constructor** — unchanged.

```csharp
// Orleans.Utilities.ObserverManager<TObserver> (single-parameter)
// Assembly: Orleans.Core.dll
// Package:  Microsoft.Orleans.Core v10.0.0
public ObserverManager(TimeSpan expiration, ILogger log);

// ObserverManager<TIdentity, TObserver> has the same constructor signature:
public ObserverManager(TimeSpan expiration, ILogger log);
```

Sources:
- <https://learn.microsoft.com/dotnet/api/orleans.utilities.observermanager-1.-ctor?view=orleans-10.0>
- <https://learn.microsoft.com/dotnet/api/orleans.utilities.observermanager-2.-ctor?view=orleans-10.0>

Existing usage: `TimelineGrain.cs:43` — `new ObserverManager<ITimelineObserver>(TimeSpan.FromMinutes(5), log)`.

**Subscribe** — two-argument form `(id, observer)` for the two-generic variant; the single-generic form has a one-argument overload that uses the observer itself as the key.

```csharp
// Observed in the existing code — ObserverManager<ITimelineObserver>
_observers.Subscribe(observer, observer); // TimelineGrain.cs:190
```

The two-arg form is idempotent: calling `Subscribe` with an existing identity renews the expiration TTL. See <https://learn.microsoft.com/dotnet/api/orleans.utilities.observermanager-2.subscribe?view=orleans-10.0>.

**`Notify` has TWO overloads in Orleans 9/10** — confirmed for both generic forms:

```csharp
// Fire-and-forget sync fanout. Returns void.
public void Notify(Action<TObserver> notification, Func<TObserver, bool>? predicate = default);

// Awaitable fanout — dispatches to each observer and returns a Task that completes
// when all observer calls complete (or fault).
public Task Notify(Func<TObserver, Task> notification, Func<TObserver, bool>? predicate = default);
```

Sources:
- <https://learn.microsoft.com/dotnet/api/orleans.utilities.observermanager-2.notify?view=orleans-10.0>
- Matches existing usage at `TimelineGrain.cs:70`: `_observers.Notify(o => o.OnTimelineEvent(stored));` — void `Action<T>` form because the observer interface method returns `Task` but we don't need to wait on fan-out for the append path.

**Phase-1 persona guidance:** use the `Action<T>` (void) overload to fan signals out to subscribed persona observers from `PersonaGrain.OnSignalAsync`. It decouples the grain call latency from observer latency, matching the existing timeline pattern.

**`CreateObjectReference` API** — unchanged, on `IGrainFactory` (which `IClusterClient`
inherits from).

```csharp
// Orleans.IGrainFactory.CreateObjectReference<TGrainObserverInterface>
// Assembly: Orleans.Core.Abstractions.dll
// Package:  Microsoft.Orleans.Core.Abstractions v10.0.0
public TGrainObserverInterface CreateObjectReference<TGrainObserverInterface>(
    IGrainObserver obj)
    where TGrainObserverInterface : IGrainObserver;
```

Source: <https://learn.microsoft.com/dotnet/api/orleans.igrainfactory.createobjectreference?view=orleans-10.0>

Existing usage:
- `iaw/Telegram/Services/InoService.cs:130` — `clusterClient.CreateObjectReference<ITimelineObserver>(observer)`
- `features/timetravel/Timetravel.Tests/TimelineGrainTests.cs:169` — `_fixture.Cluster.Client.CreateObjectReference<ITimelineObserver>(observer)`

Note: the docs recommend calling `IGrainFactory.DeleteObjectReference(obj)` when the
observer is no longer needed to avoid a client-side memory leak. The client holds
observer instances via `WeakReference<T>` so the object may be GC'd anyway, but
the registration in the internal object manager persists until explicitly deleted.
Phase-1 persona observers should mirror the `InoService.StreamEvents` pattern
(subscribe → try/finally → `UnsubscribeAsync` on the grain side; the client-side
`DeleteObjectReference` is a follow-up if leaks show up).

**Observer lifetime** — confirmed does NOT survive silo restart:
observers use `CancellationToken`-style registrations that exist only in the client's
internal object manager and the silo's `ObserverManager<T>` in-memory dictionary.
Both go away on restart. This is fine for streaming gRPC calls — the gRPC stream
tears down on silo restart anyway and clients reconnect + resubscribe. Docs confirm:
"clients aren't fault-tolerant: a client that fails might never recover" and recommend
periodic resubscription via a client-side timer for long-lived observers.

## IPersistentState<T>

**Attribute-based constructor injection** — unchanged pattern.

```csharp
// Existing in this worktree at features/timetravel/Timetravel.Core/TimelineGrain.cs:37-44
public TimelineGrain(
    [PersistentState("timeline", "Default")] IPersistentState<TimelineState> state,
    ILogger<TimelineGrain> log)
{
    _state = state;
    _log = log;
    // ...
}
```

**Attribute shape:** `[PersistentState(stateName, storageName)]`
- `stateName` — the logical state key within the grain (becomes part of the storage row ID)
- `storageName` — the registered provider name, case-sensitive match against the `AddMemoryGrainStorage("...")` / `WithMemoryGrainStorage("...")` call

**`IPersistentState<T>` interface:** extends `IStorage<TState>` and `IStorage`.
Confirmed members:

```csharp
// Orleans.Runtime.IPersistentState<TState>
public interface IPersistentState<TState> : IStorage<TState> { }

public interface IStorage<TState> : IStorage
{
    TState State { get; set; }
}

public interface IStorage
{
    string Etag { get; }
    bool RecordExists { get; }
    Task ClearStateAsync();
    Task WriteStateAsync();
    Task ReadStateAsync();
}
```

Source: <https://learn.microsoft.com/dotnet/orleans/grains/grain-persistence/#api>

**`WriteStateAsync()` call pattern** — grains are **responsible for explicitly triggering
the write**. Orleans auto-reads on activation but does NOT auto-write. Three options, all
valid:

1. **Write-through**: call `_state.WriteStateAsync()` at the end of every mutating grain
   method. Simplest; highest storage cost.
2. **Write-behind with a timer**: mark a `_dirty` bool on mutation, flush in a
   `RegisterGrainTimer` callback every N ms. Cost-efficient for bursty writes; accept
   eventual consistency within the flush window. Used by `TimelineGrain.cs:48-50` with
   a 500 ms flush interval.
3. **On-deactivate only**: flush in `OnDeactivateAsync` if dirty. Loses data on crash;
   only viable for ephemeral state.

**Phase-1 persona recommendation:** `PersonaGrain` should use **pattern 2 (write-behind)**
because `OnSignalAsync` fires on every grain-to-grain call and write-through would
storm the storage provider. Mirror `TimelineGrain`'s timer pattern — `_dirty` flag,
`RegisterGrainTimer(FlushDirtyAsync, new GrainTimerCreationOptions(interval, interval))`,
flush-and-clear-dirty in both the timer callback AND `OnDeactivateAsync` to catch the
tail of the last burst.

**Etag semantics:** `WriteStateAsync` fails if the Etag does not match what the backing
store has. Set `Etag` to `null` to force overwrite ("always delete" wins). Phase-1
persona is single-writer (one grain activation owns its state), so Etag conflicts
should not occur in practice — but propagate the exception, don't swallow it.

**Storage provider name used in the silo — `"Default"`.**

Confirmed by grep of this worktree:

| File | Line | Call |
|---|---|---|
| `iaw/Aspire.Hosting/IAWHostingExtensions.cs` | 19 | `.WithMemoryGrainStorage("Default")` — production silo via Aspire Orleans integration |
| `iaw/Aspire.Hosting/IAWHostingExtensions.cs` | 20 | `.WithMemoryGrainStorage("PubSubStore")` — stream pubsub state (separate concern) |
| `iaw/Testing/InoTestHost.cs` | 57 | `.AddMemoryGrainStorage("Default")` — E2E `InoTestHost` test silo |
| `iaw/Testing/InoTestHost.cs` | 58 | `.AddMemoryGrainStorage("PubSubStore")` |
| `iaw/Testing/AgentTest.cs` | 19 | `.AddMemoryGrainStorage("Default")` — unit test silo |
| `iaw/Testing/NeuronBddHooks.cs` | 85 | `.AddMemoryGrainStorage("Default")` — BDD test silo |
| `features/ino-new/InoNew.Tests/BehaviorMemorySiloConfigurator.cs` | 24 | `.AddMemoryGrainStorage("Default")` |
| `test/Core.Tests/*.cs`, `test/E2E.Tests/*.cs` | multiple | `.AddMemoryGrainStorage("Default")` |

**`PersonaGrain` MUST use `[PersistentState("persona", "Default")]`** — the string
`"Default"` is verified to match both production (`WithMemoryGrainStorage` via Aspire)
and all test silos (`AddMemoryGrainStorage` directly). Note the production silo uses
`WithMemoryGrainStorage` (Aspire Orleans resource-builder extension) while test silos
use `AddMemoryGrainStorage` (plain `ISiloBuilder`). Both register the same provider name
`"Default"` — the code under test sees no difference.

**Grain base class:** `PersonaGrain` should extend plain `Grain` (not the legacy
`Grain<TGrainState>`) and take `IPersistentState<PersonaBrainState>` via constructor.
Microsoft Learn explicitly marks `Grain<TGrainState>` as "legacy functionality"
(<https://learn.microsoft.com/dotnet/orleans/grains/grain-persistence/#using-grain-tgrainstate-to-add-storage-to-a-grain>).
`TimelineGrain.cs:23` already uses plain `Grain` + `IPersistentState<TimelineState>` — copy the pattern.

## Serialization

**`[GenerateSerializer]` + `[Id(n)]` convention — unchanged.** Required on every
durable state type that Orleans needs to serialize for storage or grain calls. ID values
are scoped to the inheritance level, not the type as a whole (so base + subclass can
both start at `[Id(0)]`). Primary-constructor parameters on `record` types get
implicit IDs in declaration order — do not reorder them after deployment.

Sources:
- <https://learn.microsoft.com/dotnet/orleans/host/configuration-guide/serialization#use-orleans-serialization>
- <https://learn.microsoft.com/dotnet/orleans/host/configuration-guide/serialization#serialization-best-practices>

**`ImmutableList<T>` with `[GenerateSerializer]` — SUPPORTED out of the box.** Orleans 10
ships `Orleans.Serialization.Codecs.ImmutableListCodec<T>` registered via
`[RegisterSerializer]` in `Microsoft.Orleans.Serialization v10.0.0`. Confirmed at
<https://learn.microsoft.com/dotnet/api/orleans.serialization.codecs.immutablelistcodec-1?view=orleans-10.0>.
Orleans 9.x ships the same codec in the same package — the API page is version-pinned
to `orleans-10.0` but the type has existed since the `Orleans.Serialization` NuGet was
introduced with the v7 serializer rewrite and is not a 10.0 addition.

**Practical implication for `PersonaSignal` / `PersonaBrainState`:**

```csharp
[GenerateSerializer]
public sealed class PersonaBrainState
{
    [Id(0)] public ImmutableList<PersonaSignal> RecentSignals { get; set; }
        = ImmutableList<PersonaSignal>.Empty;
    // ... other fields
}
```

This works with no surrogate code and no fallback. `ImmutableArray<T>` is also
supported (via `ImmutableArraySurrogate<T>`); plain `List<T>` works too and is
what `TimelineState.cs:11` currently uses — both are valid, pick by semantics:

- **`ImmutableList<T>`** — when you want structural sharing and cheap appends/concat across
  phases of the persona reasoning pipeline, and you want state mutations to be obviously
  non-destructive (`state = state with { RecentSignals = state.RecentSignals.Add(signal) }`).
- **`ImmutableArray<T>`** — when the collection is small and access is frequent; avoids
  the persistent-data-structure overhead.
- **`List<T>`** — when you mutate in place within a single grain call and write-behind
  immediately after. Matches the existing `TimelineState.Events` pattern. Cheapest.

**Phase-1 recommendation:** start with **`ImmutableList<PersonaSignal>`**. The persona
filter fires one signal per grain call and the grain mutates state by appending. With
Orleans Activities interleaving, holding an immutable snapshot during a notification
fanout is safer than mutating a `List<T>` that another re-entrant path might observe.
The codec is built-in, so there's no cost to choosing immutability. If a benchmark
later shows the persistent-data-structure overhead matters, fall back to `List<T>` and
copy on read.

**Fallback — if immutable collections ever misbehave:** use plain `List<PersonaSignal>`.
`TimelineState.cs:11` proves it round-trips through memory storage fine. That is the
minimum-risk fallback; do not reach for surrogates unless we see a concrete codec
failure (we will not — `ImmutableListCodec<T>` is first-party).

**No surrogate required** for phase 1. If tasks 2-10 hit a serialization error on
`ImmutableList<PersonaSignal>`, re-verify against the codec link above before pivoting
to `List<T>` — the most likely cause would be a project NOT referencing
`Microsoft.Orleans.Serialization` (rare; it's a transitive of `Orleans.Core`).
