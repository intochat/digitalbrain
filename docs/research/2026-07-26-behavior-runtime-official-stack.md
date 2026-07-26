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

## Evidence sections

- [Identity, durability, and capabilities](./2026-07-26-behavior-runtime-official-stack-identity-durability-capabilities.md) — sections 1–4.
- [Scheduling, serialization, and generation](./2026-07-26-behavior-runtime-official-stack-scheduling-serialization-generation.md) — sections 5–8.
- [Transport and ordering](./2026-07-26-behavior-runtime-official-stack-transport-and-ordering.md) — sections 9–11.

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
