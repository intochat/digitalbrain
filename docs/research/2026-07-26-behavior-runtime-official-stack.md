# Behavior runtime: official Orleans and gRPC implementation stack

**Date:** 2026-07-26

**Scope:** Runtime execution only: Orleans identity, durability, call authorization, scheduling,
recovery, serialization, generated proxies, and local worker IPC. Compiler, artifact-store, and
Windows sandbox-launch details are covered by their own research notes.

**Evidence rule:** A finding is included only when it changes a dependency, an interface seam,
implementation order, or a proof required by the implementation plan.

## Executive decision

Use the runtime which is already deep in this repository instead of adding a second actor,
workflow, persistence, queueing, or RPC stack:

- One `[GrainType("behavior")]` `BehaviorNeuron : Neuron, IBehavior`, keyed by the existing
  `owner/name` convention, hosts every installed Behavior.
- `BehaviorNeuron` commits an execution receipt and sends a queue item through the existing durable
  neuron outbox. It never awaits compilation, process launch, gRPC, or user code.
- An owner-scoped execution-queue neuron durably owns pending work. A .NET `BackgroundService`
  claims work and launches workers outside Orleans turns.
- The trusted host exposes one ASP.NET Core gRPC service over a Kestrel Windows named pipe. A worker
  reuses one `GrpcChannel` and uses simple unary `Claim`, `InvokeCapability`, and
  `CompleteExecution` calls. Bidirectional streaming is unnecessary for the first sequential
  runtime.
- Protobuf is the fixed IPC envelope. Existing CLR synapse and module payloads remain encoded by
  the Orleans serializer, initialized as a standalone DI service in the worker. The trusted side
  decodes worker-supplied arguments only through generated, exact-type adapters.
- Inside the silo, use Orleans' generated grain references from `IGrainFactory`. Outside the silo,
  extend `DigitalBrain.SourceGeneration` to generate module capability adapters. Do not use
  `DispatchProxy`, Castle DynamicProxy, code-first gRPC, or reflection dispatch.
- Extend the existing one-use capability-delegation terminal callback to atomically persist the
  typed result bytes in `BehaviorNeuron` before a successful module call returns to the worker.
  The existing `FinishAsync(delegation, bool)` is insufficient for the approved replay scenario.
- Reuse the current durable-outbox pattern: activation-local `RegisterGrainTimer` for prompt drain
  and durable reminders only as recovery wakeups. Stored timestamps and state, not reminder ticks,
  decide deadlines.

The most important ordering consequence is: **prove and extend the kernel capability terminal
record before building the worker or IPC host**. Otherwise the worker protocol will be built around
a replay guarantee the current kernel cannot provide.

## Repository inventory

The repository targets .NET 10 (`global.json` requests SDK `10.0.100`, rolling to the latest
feature SDK) and centrally pins:

| Existing package family | Current pin |
| --- | --- |
| `Microsoft.Orleans.Sdk`, `Server`, `Client`, `Reminders`, clustering, testing, serialization adapter | `10.2.2-rc.2` |
| `Microsoft.Orleans.Journaling` and `.Journaling.AzureStorage` | `10.2.2-rc.2.alpha.1` |
| `Microsoft.Extensions.*` | predominantly `10.0.10` |

The official NuGet feed contains stable Orleans `10.2.2`, but the latest published
`Microsoft.Orleans.Journaling` is still `10.2.2-rc.2.alpha.1`, and its package metadata requires
Orleans Core, Runtime, Serialization, analyzers, and code generator `10.2.2-rc.2`. Therefore:

1. Keep the complete Orleans runtime family on `10.2.2-rc.2` during this refactor.
2. Add any new direct `Microsoft.Orleans.Serialization` reference at exactly `10.2.2-rc.2`.
3. Do not mix stable `10.2.2` Core/Server/Serialization with the RC journaling package.
4. Add a later explicit upgrade task which moves the entire family together after an official
   compatible Journaling release and reruns durability characterization.

This is not an endorsement of preview risk. It is containment of an already accepted preview
dependency behind `DigitalBrain.Kernel`. The package is Microsoft-authored, but it remains
prerelease and the repository already suppresses `ORLEANSEXP005`.

Primary package evidence:

- [`Microsoft.Orleans.Journaling` 10.2.2-rc.2.alpha.1](https://www.nuget.org/packages/Microsoft.Orleans.Journaling/10.2.2-rc.2.alpha.1)
- [`Microsoft.Orleans.Server` 10.2.2-rc.2](https://www.nuget.org/packages/Microsoft.Orleans.Server/10.2.2-rc.2)
- [Exact Journaling source commit recorded by the package](https://github.com/dotnet/orleans/tree/e1e7a281d0de8438bada6d681423c1d4ce990082/src/Orleans.Journaling)

The relevant current implementation is:

- `INeuron : IGrainWithStringKey`.
- `NeuronId` maps a stable type plus the string key `OwnerId/Name` to `GrainId`.
- `Neuron : DurableGrain` owns durable incoming/outgoing feeds, handled-delivery dedupe, outbox,
  one-use capability delegations, and terminal capability facts.
- `Neuron.Deliver` snapshots the synapse, dispatches it, stages incoming/outgoing/outbox state, and
  performs one `WriteStateAsync` commit with rollback to a turn checkpoint on failure.
- The outbox uses an activation timer for fast drain and `OutboxWakeup`, backed by an Orleans
  reminder, for recovery after activation/process loss.
- `IncomingReificationFilter`, `OutgoingReificationFilter`, and `OwnerBoundCallFilter` already
  enforce semantic capability causation and owner-bound Orleans source identities.

## 1. Grain identity: one implementation, keyed Behavior instances

Orleans defines a grain identity as grain type plus user key. A stable logical identity can be
activated, deactivated, and moved without changing what callers address. The grain directory maps
that identity to its current activation. Orleans' normal programming model is therefore already
the required “one CLR implementation, many owner-scoped Behavior instances” model:

- [Orleans overview and grain keys](https://learn.microsoft.com/en-us/dotnet/orleans/overview)
- [Grain identity](https://learn.microsoft.com/en-us/dotnet/orleans/grains/grain-identity)
- [Grain directory and activations](https://learn.microsoft.com/en-us/dotnet/orleans/host/grain-directory)

### Implementation consequence

Use:

```csharp
[GrainType("behavior")]
internal sealed class BehaviorNeuron : Neuron, IBehavior
{
}
```

and retain the existing key:

```text
Behavior grain id = behavior/{OwnerId}/{BehaviorId}
```

No generic CLR grain type, dynamic Orleans application part, or generated class per Behavior is
needed. `BehaviorRevisionId` is durable state inside the grain, not part of the Orleans grain type.

The existing Flutter-owned `OpenHomeOnActivationBehavior` already claims `[GrainType("behavior")]`.
Two implementations must not compete for the same stable grain type. The implementation task which
adds `BehaviorNeuron` must remove that concrete class in the same coherent change after its product
scenario is captured. It cannot be retained as a fallback grain.

### Required proof

- Two Behavior IDs for one owner resolve to two logical grains of the same implementation and keep
  independent state.
- The same `(OwnerId, BehaviorId)` resolves to the same state after deactivation/reactivation.
- The same `BehaviorId` under two owners remains isolated by `OwnerBoundCallFilter`.
- The compiled Orleans manifest contains exactly one implementation for grain type `behavior`.

## 2. Persistence: retain `DurableGrain`, but characterize the prerelease boundary

The official Journaling implementation supplies `DurableGrain`, keyed durable collections, and
`WriteStateAsync`. Its state manager participates at `GrainLifecycleStage.SetupState`, recovers the
journal before the activation handles requests, coalesces pending writes, appends journal entries,
and replaces storage with a snapshot when migration or storage compaction requests it:

- [`DurableGrain` source](https://github.com/dotnet/orleans/blob/e1e7a281d0de8438bada6d681423c1d4ce990082/src/Orleans.Journaling/DurableGrain.cs)
- [`JournaledStateManager` recovery/write source](https://github.com/dotnet/orleans/blob/e1e7a281d0de8438bada6d681423c1d4ce990082/src/Orleans.Journaling/JournaledStateManager.cs)
- [Package README and JSON Lines storage model](https://github.com/dotnet/orleans/blob/e1e7a281d0de8438bada6d681423c1d4ce990082/src/Orleans.Journaling/README.md)

`Neuron` already adds the application-level atomicity which DigitalBrain needs: it stages all
durable structures, performs one commit, and restores its in-memory checkpoint if the storage write
fails. Behavior runtime state should use that same mechanism rather than introduce
`IPersistentState<T>`, event-sourcing libraries, or another database abstraction.

### Durable state shape

Keep Behavior runtime data in a small number of named durable structures owned by `BehaviorNeuron`:

- installation and selected immutable revision;
- execution records keyed by `ExecutionId`;
- deterministic capability call records keyed by `(ExecutionId, CallOrdinal)`;
- Behavior-private committed state;
- bounded terminal/history indexes.

Continue the repository's established pattern of Orleans-serializing typed records to `byte[]`
inside durable lists/dictionaries when that prevents every private CLR record from becoming part of
the JSON journal context. Stable `[Alias]` and `[Id]` rules still apply to those records.

### Do not overclaim transactions

One `Neuron.WriteStateAsync` is the DigitalBrain commit boundary for one activation. It is not a
distributed transaction with the invoked module grain or an external provider. Delivery is
at-least-once plus deduplication. Provider uncertainty remains inside the module which owns that
provider.

### Required proof before feature work

Add characterization tests around the current prerelease package before changing `Neuron`:

- activation recovery restores incoming/outgoing journals, handled IDs, outbox, delegations, and
  Behavior execution/call records;
- an injected journal append failure restores every staged structure;
- compaction/snapshot recovery preserves the same state;
- a process restart with a non-empty outbox resumes delivery;
- `dotnet list package --include-transitive` proves every Orleans Core/Runtime/Serialization
  assembly resolves to the same RC family.

## 3. Request context and call filters: causal metadata, not authentication

`RequestContext` is an AsyncLocal-backed property bag copied into outgoing Orleans messages. It
flows forward to called grains and is intended for request metadata. It does not flow back with a
response. Values must be serializable:

- [Request context documentation](https://learn.microsoft.com/en-us/dotnet/orleans/grains/request-context)
- [`RequestContext` source: AsyncLocal storage and copy-on-write mutation](https://github.com/dotnet/orleans/blob/e1e7a281d0de8438bada6d681423c1d4ce990082/src/Orleans.Core.Abstractions/Runtime/RequestContext.cs)

Global incoming and outgoing filters can inspect source/target IDs, interface/method metadata,
arguments, `Result`, and exceptions around `context.Invoke()`:

- [Orleans grain call filters](https://learn.microsoft.com/en-us/dotnet/orleans/grains/interceptors)
- [`IGrainCallContext` public result seam](https://github.com/dotnet/orleans/blob/e1e7a281d0de8438bada6d681423c1d4ce990082/src/Orleans.Core.Abstractions/Core/IGrainCallContext.cs)

The current filters use those APIs correctly in one important respect: authority is checked against
the actual `SourceId`, target, interface, and method, not merely a caller-supplied context value.

### Implementation consequences

1. Keep `RequestContext` payloads small: correlation, causation, and the existing committed
   `SynapseDelivery`/redeemed delegation only.
2. Never treat a worker ID, owner ID, execution ID, revision ID, or gRPC header in
   `RequestContext` as proof of authority. A caller can set application request-context values.
3. A gRPC call is not an Orleans call and does not pass through grain call filters. The ASP.NET
   service must never resolve a module grain and invoke it directly.
4. The gRPC service may only:
   - validate the local worker channel and bounded protobuf envelope;
   - ask `BehaviorNeuron` to prepare/replay an exact call;
   - invoke the internal owner-bound broker grain using the returned delegation;
   - return the already committed result.

## 4. The capability seam must record results atomically

The repository's current delegation is a good base, but it cannot satisfy the approved replay
scenario unchanged.

Current behavior:

1. `BehaviorNeuron`/another `Neuron` can durably issue a one-use `CapabilityDelegation` for an exact
   delegate source grain, target, interface, and method.
2. `OutgoingReificationFilter` on a non-`Neuron` delegate grain redeems it with the causal
   authority.
3. The module call runs.
4. `ICapabilityDelegationAuthority.FinishAsync(delegation, succeeded)` commits only
   completed/failed status and a generic fact.
5. The actual returned value is not persisted with that terminal transition.

If the worker dies after receiving the effect but before its result is durably recorded, there is no
value to replay. More subtly, the current outgoing filter has access to `IGrainCallContext.Result`
after `context.Invoke()` succeeds, so Orleans already exposes the official interception seam needed
to fix this without a second proxy framework.

### Required kernel change

Replace the boolean terminal callback with an internal typed terminal envelope, conceptually:

```csharp
Task FinishAsync(
    CapabilityDelegation delegation,
    CapabilityTerminal terminal);
```

For a Behavior delegation, the durable issued record must also contain:

- `ExecutionId`;
- `CallOrdinal`;
- canonical request fingerprint;
- exact approved contract/method/target identity.

`CapabilityTerminal` contains either:

- success plus the exact generated-codec result bytes and stable result alias; or
- a bounded normalized failure code/details.

After the target call succeeds, `OutgoingReificationFilter` obtains `context.Result`, uses the
generated method codec catalog to encode the declared result, and calls `FinishAsync`. The
authority commits delegation terminal status and the Behavior call result in the same
`BehaviorNeuron.WriteStateAsync` boundary. Only then does the module call return through the broker
to gRPC.

On replay:

- same `(ExecutionId, CallOrdinal)` plus same fingerprint returns the recorded bytes without
  invoking the module;
- a different fingerprint at the same ordinal is rejected;
- a consumed delegation with no terminal result is reported as outcome-uncertain and is never
  silently reissued.

The generated codec is important. Do not use `Serializer<object>` plus an untrusted runtime type
name for worker-supplied values.

### Broker grain shape

The existing `OutgoingReificationFilter` treats a `Neuron` caller as a direct semantic caller and a
non-`Neuron` grain as a delegated runner. Therefore the exact reusable shape is:

```text
BehaviorNeuron             durable authority and result journal
BehaviorCapabilityBroker   internal owner-bound Orleans grain, not Neuron
ASP.NET gRPC service       transport edge only
```

`BehaviorNeuron` mints a one-use delegation whose `DelegateSource` is the broker grain's actual
owner-bound `GrainId`. The broker executes the generated typed module adapter inside
`CapabilityRequestContext.InvokeAsync(delegation, ...)`. Existing filters then redeem, invoke, and
finish against the real Orleans source grain.

Making the broker another `Neuron` would bypass the delegated branch and create a new direct
capability request instead. Invoking a module from the ASP.NET service would have no source
activation at all. Both shapes are wrong for the current kernel.

### Required proof

Kill only the runtime worker after a module call returns but before the worker receives the gRPC
response. Restart the same `ExecutionId`; the same call must return the committed result and the
module invocation counter must remain one.

Also prove:

- forged `RequestContext` values do not authorize a call;
- a delegation cannot change owner, source grain, target, interface, method, ordinal, or request
  fingerprint;
- a second redemption fails;
- a terminal-storage failure never results in an unrecorded success being reported to the worker.

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

## 9. ASP.NET Core gRPC over a Kestrel Windows named pipe

ASP.NET Core has first-party named-pipe support in the `Microsoft.AspNetCore.App` shared framework
on .NET 8+. Kestrel listens with `ListenNamedPipe`; gRPC requires HTTP/2. The client uses
`GrpcChannel.ForAddress` with a `SocketsHttpHandler.ConnectCallback` which opens
`NamedPipeClientStream`:

- [gRPC over named pipes, server and client configuration](https://learn.microsoft.com/en-us/aspnet/core/grpc/interprocess-namedpipes?view=aspnetcore-10.0)
- [IPC security, server-owner validation, and impersonation guidance](https://learn.microsoft.com/en-us/aspnet/core/grpc/interprocess?view=aspnetcore-10.0)
- [ASP.NET Core gRPC service setup](https://learn.microsoft.com/en-us/aspnet/core/grpc/aspnetcore?view=aspnetcore-10.0)
- [gRPC .NET client](https://learn.microsoft.com/en-us/aspnet/core/grpc/client?view=aspnetcore-10.0)

The official Kestrel transport already exposes:

- `ListenNamedPipe(pipeName, listen => listen.Protocols = HttpProtocols.Http2)`;
- `NamedPipeTransportOptions.CurrentUserOnly`;
- `NamedPipeTransportOptions.PipeSecurity`;
- `NamedPipeTransportOptions.CreateNamedPipeServerStream` for per-endpoint customization.

Do not add a `Microsoft.AspNetCore.Server.Kestrel.Transport.NamedPipes` NuGet reference: the assembly
ships in `Microsoft.AspNetCore.App`. Do not add a community named-pipe gRPC transport.

### Initial protocol: unary, one reused channel

The first Behavior runtime permits sequential capability calls and one worker is scoped to one
execution. Use:

```proto
service BehaviorBroker {
  rpc ClaimExecution(ClaimExecutionRequest) returns (ExecutionStart);
  rpc InvokeCapability(CapabilityCall) returns (CapabilityResult);
  rpc CompleteExecution(ExecutionCompletion) returns (ExecutionAck);
}
```

The worker receives only the pipe name plus an opaque one-time execution bootstrap credential,
connects, claims its committed input, reuses the channel for capability calls, and completes.

Microsoft recommends reusing channels. Bidirectional streaming can reduce repeated HTTP/2 request
overhead, but Microsoft's guidance calls it an advanced optimization with restart, ordering, and
single-writer complexity. It is not justified until measurement shows unary calls are a bottleneck:

- [gRPC performance guidance and channel reuse](https://learn.microsoft.com/en-us/aspnet/core/grpc/performance?view=aspnetcore-10.0)
- [Streaming reader/writer concurrency rules](https://learn.microsoft.com/en-us/aspnet/core/grpc/services?view=aspnetcore-10.0)

### Exact packages

Pin the current official stable gRPC family together:

| Project role | Package | Version |
| --- | --- | --- |
| Shared `.proto` contract | `Grpc.Tools` (`PrivateAssets="all"`) | `2.80.0` |
| Shared `.proto` contract | `Google.Protobuf` | `3.31.1` |
| Shared generated service types if needed | `Grpc.Core.Api` | `2.80.0` |
| Trusted ASP.NET Core broker host | `Grpc.AspNetCore` | `2.80.0` |
| Runtime worker client | `Grpc.Net.Client` | `2.80.0` |

`Grpc.AspNetCore` 2.80.0 itself pins `Grpc.Tools` 2.80.0 and `Google.Protobuf` 3.31.1 for `net10.0`,
so these versions avoid an unnecessary independent upgrade:

- [`Grpc.AspNetCore` 2.80.0](https://www.nuget.org/packages/Grpc.AspNetCore/2.80.0)
- [`Grpc.Net.Client` 2.80.0](https://www.nuget.org/packages/Grpc.Net.Client/2.80.0)
- [`Grpc.Tools` 2.80.0](https://www.nuget.org/packages/Grpc.Tools/2.80.0)
- [`Google.Protobuf` 3.31.1](https://www.nuget.org/packages/Google.Protobuf/3.31.1)

### Transport configuration obligations

- Set HTTP/2 explicitly.
- Bind no TCP endpoint in the worker-broker host.
- Restrict the pipe with `CurrentUserOnly` and an explicit least-privilege `PipeSecurity`; do not
  copy the documentation's broad `Users` example.
- The client uses `TokenImpersonationLevel.Anonymous` or `None`.
- The client validates the pipe server owner before sending the bootstrap credential.
- Bound gRPC send/receive message sizes, execution/call deadlines, and pending calls.
- Dispose the channel/process on execution termination.
- Do not depend on gRPC client-side load balancing or channel connectivity-state APIs: Microsoft
  documents that channels using `ConnectCallback` do not support them.

### Required proof

- The correct restricted worker can connect; a process under an unapproved identity cannot.
- The client rejects a pipe server owned by an unexpected SID.
- Endpoint enumeration proves the host opened no TCP listener.
- Oversized messages, unknown `oneof`/message versions, expired credentials, wrong execution IDs,
  duplicate ordinals, and late completion are rejected.
- Killing the pipe/worker cancels in-flight RPC and releases the execution lease without losing the
  durable execution record.

## 10. Exact task ordering for the implementation plan

1. **Orleans dependency gate**
   - Add resolved-package and durability characterization tests.
   - Keep the coherent RC family; directly pin standalone serialization.
2. **Kernel result-aware delegation**
   - Add execution/ordinal/fingerprint metadata.
   - Add generated method result codecs.
   - Change terminal authority/filter so result recording commits before return.
   - Prove worker-loss replay and terminal-write failure.
3. **Generic Behavior identity**
   - Add the sole `[GrainType("behavior")]` implementation.
   - Replace/remove the Flutter concrete Behavior in the same change.
   - Add the minimal protected generic dispatch seam while retaining base delivery, journal,
     dedupe, filters, and outbox.
4. **Durable execution queue**
   - Add execution receipt/state and outbox handoff.
   - Add owner-scoped queue/lease state plus reminder reconciliation.
   - Add the host `BackgroundService`; no grain `Task.Run`.
5. **Generated capability catalogs/adapters**
   - Extend `DigitalBrain.SourceGeneration`.
   - Add standalone serializer bootstrapping from the exact approved contract catalog.
6. **Fixed Protobuf IPC contract**
   - Add the shared protocol project and the exact gRPC pins.
   - Keep CLR payloads opaque and exact-type decoded by generated adapters.
7. **Named-pipe broker host and worker client**
   - Add Kestrel HTTP/2 named-pipe endpoint, unary service, channel reuse, limits, identity
     validation, and cancellation.
8. **Runtime worker**
   - Claim committed execution, load the approved revision, execute sequential calls, and submit a
     correlated completion.
9. **Recovery and chaos proofs**
   - Worker death, broker/host restart, silo restart, reminder tick loss, terminal storage failure,
     duplicated RPC, and replay mismatch.
10. **Product migration and deletion**
    - Move boot/UI and enrichment behavior to this rail.
    - Delete the old concrete Behavior, pull compositions, redundant docs, projects, and references
      only after root BDD parity is green.

## 11. Libraries explicitly rejected

| Library/approach | Reason |
| --- | --- |
| Akka.NET, Proto.Actor, Dapr Actors | Duplicates Orleans identity, placement, calls, and lifecycle |
| Marten/EventStoreDB/custom event-sourcing package | Duplicates the existing `DurableGrain` journal and commit model |
| Hangfire, Quartz.NET | Duplicates durable queue/lease/reminder reconciliation |
| MassTransit, NServiceBus, cloud queue for local handoff | Adds a second message fabric before the current durable outbox is shown insufficient |
| `DispatchProxy`, Castle DynamicProxy | Runtime reflection and late failures where source generation can prove the contract |
| protobuf-net.Grpc, MagicOnion | Duplicates official `.proto`/`Grpc.Tools` generated contracts and weakens language-neutral wire ownership |
| Community named-pipe gRPC transports | Kestrel and `SocketsHttpHandler.ConnectCallback` ship the required transport |
| Orleans client inside the worker | Gives untrusted code cluster authority and generated grain proxies |
| `Task.Run`/in-memory `TaskCompletionSource` from a grain | Escapes durable activation state and cannot be recovered |
| Orleans Streams for the first execution queue | Adds provider/checkpoint semantics while a single durable queue neuron and current outbox suffice |

## Final plan contract

The professional implementation is not “a script calls some proxy.” It is a chain of independently
provable official seams:

```text
BehaviorNeuron durable receipt
  -> existing neuron outbox
  -> durable execution queue
  -> .NET BackgroundService launches worker
  -> generated gRPC client over Kestrel named pipe
  -> exact catalog + generated typed adapter
  -> one-use Orleans delegation from the actual broker grain
  -> Orleans generated module proxy
  -> result captured by outgoing call filter
  -> BehaviorNeuron atomically records terminal result
  -> worker receives only the committed result
```

That shape preserves the approved principles: the Behavior is a journaled neuron, its program has
no Orleans authority, all effects cross exact module contracts, a worker crash is replayable, and
the implementation relies on shipped Orleans, ASP.NET Core, gRPC, Protobuf, Generic Host, and
Windows named-pipe APIs rather than an LLM-invented runtime.
