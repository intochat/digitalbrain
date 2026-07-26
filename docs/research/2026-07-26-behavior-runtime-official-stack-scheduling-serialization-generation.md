## 5. Orleans scheduling: never hold a Behavior turn open

Each activation has a single-concurrency task scheduler. Internally Orleans uses
`ActivationTaskScheduler` and a `WorkItemGroup` queue which schedules itself on the .NET thread
pool, but executes activation tasks one at a time. The thread may change; the serialized activation
context is the invariant:

- [Orleans scheduling overview](https://learn.microsoft.com/en-us/dotnet/orleans/implementation/scheduler)
- [`ActivationTaskScheduler` source](https://github.com/dotnet/orleans/blob/e1e7a281d0de8438bada6d681423c1d4ce990082/src/Orleans.Runtime/Scheduler/ActivationTaskScheduler.cs)
- [`WorkItemGroup` source](https://github.com/dotnet/orleans/blob/e1e7a281d0de8438bada6d681423c1d4ce990082/src/Orleans.Runtime/Scheduler/WorkItemGroup.cs)
- [Default non-reentrant request scheduling](https://learn.microsoft.com/en-us/dotnet/orleans/grains/request-scheduling)

`Task.Run` uses the general thread-pool scheduler, not the activation scheduler. It is not a safe
mechanism for continuing to read or mutate grain fields after a method returns. Conversely,
awaiting worker execution from the grain keeps the request/turn logically open, couples progress to
one activation, and makes the in-memory continuation part of recovery.

### Runtime handoff

The safe sequence is:

1. `BehaviorNeuron` commits `BehaviorExecutionStarted` and an outbox message.
2. Its turn returns.
3. The outbox delivers to a durable execution-queue neuron.
4. A host `BackgroundService` claims a leased queue item and launches the worker outside Orleans.
5. Capability requests reenter Orleans as new broker/Behavior turns.
6. Completion reenters as a new correlated turn.

No grain callback, captured `TaskScheduler`, `TaskCompletionSource`, or in-memory promise is the
source of truth.

Use `System.Threading.Channels` only as a bounded in-process handoff after a durable queue claim. A
channel is not the execution queue and a host restart must reconstruct all work from grain state.

## 6. Timers, reminders, deadlines, and the existing outbox

Orleans distinguishes activation-local timers from durable reminder definitions:

- `RegisterGrainTimer` callbacks run as separate activation turns, do not interleave by default,
  receive deactivation cancellation, and disappear with the activation.
- Reminders are associated with a grain and survive activation/cluster restarts, but an individual
  tick which occurs while the cluster is down is not replayed. They are intended for periods in
  minutes or longer.

Official behavior and APIs are documented in
[Timers and reminders](https://learn.microsoft.com/en-us/dotnet/orleans/grains/timers-and-reminders).

### Implementation consequences

- Keep the current `Neuron` outbox design. Its durable entries plus receiver dedupe provide the
  guarantee; the 50 ms `RegisterGrainTimer` only improves prompt delivery.
- Keep `OutboxWakeup` reminders as recovery signals, not as the outbox state.
- Store `LeaseExpiresAt`, worker deadline, and execution deadline durably. On every activation,
  claim, timer tick, or reminder tick, reconcile against `TimeProvider.GetUtcNow()`.
- Use an activation timer for short active lease/deadline checks and a reminder to reactivate and
  reconcile after failure. A missed reminder tick cannot lose an execution because the timestamp
  and pending record remain.
- Do not introduce Quartz.NET, Hangfire, MassTransit, NServiceBus, or a cloud queue merely to schedule
  or wake Behavior executions.

### Required proof

- Deactivate the queue grain while an item is leased, advance beyond the lease timestamp, deliver a
  later reminder, and prove the item becomes claimable.
- Stop the cluster across a scheduled reminder tick, restart, and prove timestamp reconciliation
  reaches the same result.
- Dispose/cancel an activation timer and prove durable pending work remains recoverable.

## 7. Standalone Orleans serialization outside a silo

Orleans serialization is an independently registered DI service. The public API is:

```csharp
services.AddSerializer(serializer =>
{
    serializer.AddAssembly(typeof(Synapse).Assembly);
    foreach (var contractAssembly in approvedContractAssemblies)
    {
        serializer.AddAssembly(contractAssembly);
    }
});

using var provider = services.BuildServiceProvider();
var serializer = provider.GetRequiredService<Serializer<T>>();
```

The implementation of `AddSerializer` registers `Serializer`, `Serializer<T>`, codecs, copiers,
activators, type resolution, and session pools without requiring a silo or Orleans client. It
scans relevant referenced assemblies when configured and `AddAssembly` explicitly adds generated
metadata:

- [Orleans serialization model and versioning rules](https://learn.microsoft.com/en-us/dotnet/orleans/host/configuration-guide/serialization)
- [`AddSerializer` implementation](https://github.com/dotnet/orleans/blob/e1e7a281d0de8438bada6d681423c1d4ce990082/src/Orleans.Serialization/Hosting/ServiceCollectionExtensions.cs)
- [`SerializerBuilderExtensions.AddAssembly` implementation](https://github.com/dotnet/orleans/blob/e1e7a281d0de8438bada6d681423c1d4ce990082/src/Orleans.Serialization/Hosting/SerializerBuilderExtensions.cs)

### Wire division

Use Protobuf for the fixed IPC control envelope:

- execution/revision/owner identifiers;
- message kind;
- stable contract, method, and schema IDs;
- ordinal, fingerprint, bounded failure data;
- opaque payload byte fields.

Use Orleans serialization for the opaque CLR payload because the module ecosystem already uses
`[GenerateSerializer]`, `[Alias]`, and `[Id]`, and it preserves polymorphism and version-tolerant
CLR shapes.

This is not permission to deserialize arbitrary worker-selected CLR types in the trusted process.
The broker selects the approved contract/method from its catalog, and generated code decodes each
argument as the declared parameter type. After decode, it verifies that the exact method and
canonical bytes match the fingerprint stored by `BehaviorNeuron`.

The worker builds its serializer provider only after loading the exact approved contract
assemblies. Explicit `AddAssembly` calls are required because a process-wide automatic scan should
not decide which community contract assemblies are admitted.

### Package consequence

Add a direct central pin:

```xml
<PackageVersion Include="Microsoft.Orleans.Serialization"
                Version="10.2.2-rc.2" />
```

to projects which use `Serializer<T>` without hosting Orleans. Do not depend accidentally on a
transitive `Microsoft.Orleans.Sdk` asset.

### Required proof

- A standalone service provider, with no silo/client, round-trips every event trigger, module
  argument, module result, private state envelope, and failure envelope used by a fixture Behavior.
- Removing an approved contract assembly makes startup validation fail before execution.
- Unknown aliases, type mismatches, oversized payloads, and a worker-selected unexpected concrete
  type are rejected.
- Renaming a CLR type while retaining its `[Alias]` remains compatible; reusing/changing aliases or
  field IDs fails repository compatibility checks.

## 8. Generated grain proxies and generated capability adapters

Inside Orleans, `IGrainFactory.GetGrain<TInterface>` resolves the grain interface/type and uses a
generated proxy class registered in the Orleans manifest. The runtime maintains a mapping from
`GrainInterfaceType` to generated proxy type:

- [`IGrainFactory` typed APIs](https://github.com/dotnet/orleans/blob/e1e7a281d0de8438bada6d681423c1d4ce990082/src/Orleans.Core.Abstractions/Core/IGrainFactory.cs)
- [`GrainReferenceActivator` generated-proxy mapping](https://github.com/dotnet/orleans/blob/e1e7a281d0de8438bada6d681423c1d4ce990082/src/Orleans.Core/GrainReferences/GrainReferenceActivator.cs)
- [Orleans code generation](https://learn.microsoft.com/en-us/dotnet/orleans/host/configuration-guide/code-generation)

Those proxies must remain inside the trusted broker/silo. Giving a worker `IClusterClient` or an
Orleans grain proxy gives it cluster authority and bypasses the approved context.

For `IBehaviorContext.Get<TModule>()`, extend the existing incremental
`DigitalBrain.SourceGeneration` package to emit two compile-time artifacts per selected module
contract:

1. A worker adapter implementing `TModule` and converting each method to the fixed gRPC capability
   envelope.
2. A trusted broker invoker which exact-type decodes arguments, calls the real Orleans generated
   proxy, and exact-type encodes the declared result.

Both sides use a generated method catalog keyed by stable contract and method IDs. Unsupported
signatures become compile-time diagnostics. This follows the repository's existing source-generated
dispatch/catalog direction.

### Why not `DispatchProxy`

`DispatchProxy` would defer missing methods, overload ambiguity, serialization gaps, and return-type
mistakes to runtime; it also adds reflection and `object[]` invocation in the most security-sensitive
path. Castle DynamicProxy adds another library without solving protocol authorization. Orleans
generated proxies cannot speak the worker IPC protocol. Protobuf-generated gRPC stubs solve
transport only, not module interface adaptation.

No additional proxy library is justified.

### Required proof

- Generator golden tests cover `Task`, `Task<T>`, `ValueTask`, `ValueTask<T>`, cancellation, overload
  policy, and unsupported signatures.
- The build fails when an installed capability has no generated adapter.
- Repository/package search shows no `DispatchProxy`, `Castle.*`, or reflective
  `MethodInfo.Invoke` in the production capability path.
- The worker project has no `Microsoft.Orleans.Client`, `IClusterClient`, `IGrainFactory`, or grain
  proxy reference.
