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
