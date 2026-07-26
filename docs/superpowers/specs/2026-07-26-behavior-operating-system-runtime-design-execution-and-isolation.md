# Behavior operating system runtime execution and isolation

**Status:** Approved design; the execution rail remains Designed and unbuilt

**Parent decision and identity model:** [Behavior operating system and runtime design](2026-07-26-behavior-operating-system-runtime-design.md)

## 7. Runtime execution

`BehaviorNeuron` performs only short, durable turns:

1. Accept an event delivery or validated intent.
2. Resolve the active approved revision.
3. Commit `BehaviorExecutionStarted` with a unique `ExecutionId`, trigger lineage, revision, and
   input fingerprint.
4. Enqueue execution through the durable outbox and return from the grain turn.
5. Accept a correlated completion, failure, or timeout later.
6. Commit approved private-state transitions and `BehaviorExecutionCompleted` or
   `BehaviorExecutionFailed`.
7. Make an intent outcome available by `ExecutionId` or emit configured generic lifecycle facts.

The grain never holds a turn open while arbitrary code executes.

The runtime worker receives only the approved assembly, serialized input, deadline, resource
limits, and a scoped IPC connection. It receives no Orleans client or direct infrastructure
credentials.

### Capability broker

All program calls pass through a trusted broker. A call is admitted only when all of these match:

- Owner, Behavior, execution, and revision.
- Declared module contract and method.
- Target neuron identity.
- Approved capability grant.
- Argument schema and request fingerprint.
- Causal execution lineage.

The broker then invokes the real module neuron. Module operations keep their own journals and
domain-specific recovery rules.

Each broker call has a deterministic `(ExecutionId, CallOrdinal)` identity. The first runtime
version permits only sequential capability calls, enforced by the SDK and analyzer, so ordinal
assignment is stable. Ambient time and randomness are forbidden; deterministic values come from
the context. The broker records each request fingerprint and result. If an execution is replayed
after worker loss, an identical call receives the recorded result; a different request at the same
ordinal fails rather than performing an ambiguous second effect. Explicit stable call identities
may later enable parallel calls without weakening replay. Provider uncertainty remains the owning
module's responsibility—this mechanism does not claim exactly-once external effects.

Behavior-private state is read through the context and committed by `BehaviorNeuron` only after a
valid execution transition. A worker cannot write grain storage directly.

## 8. Isolation

Unknown AI and community revisions execute out of process by default.

The Windows-first runtime combines:

- AppContainer or LPAC for privilege isolation.
- A non-breakaway Job Object for process-tree, CPU, memory, and termination limits.
- An explicitly ACL-restricted named pipe for broker IPC.
- No network capability and a read-only view of the selected artifact.
- Deadline enforcement that terminates the entire job, not only a cancellation token.

A Job Object is resource supervision, not a privilege sandbox. Hostile multi-tenant execution uses a
Hyper-V-isolated container or stronger boundary. Equivalent non-Windows isolation must be designed
and proven before that platform is supported.

Minimal source-controlled, signed boot and recovery revisions may use a trusted in-process executor
so the operating system can recover its worker infrastructure. They retain the same revision,
manifest, context, broker, journal, and BDD model. `AssemblyLoadContext` is dependency isolation
only. Provenance and policy select the executor; they do not create a second kind of Behavior.

## 9. AI discovery and composition

The assistant searches two derived indexes:

- Compiled module CLR contracts and synapse aliases.
- Installed Behavior manifests, intent schemas, descriptions, examples, and grants.

Embeddings rank candidates. They never grant authority and never determine a runtime type by
similarity. The assistant must resolve a result back to an exact catalog record before it can invoke
or reference it.

The assistant may:

- Invoke already installed and approved Behavior intents.
- Cause event-driven Behaviors indirectly by calling approved module contracts.
- Compose a new source file, manifest, schemas, and BDD scenarios.
- Submit a revision proposal and explain requested grants.

It may not approve, install, replace, widen grants, or activate a new revision. Those transitions
require the owner.

## 10. BDD and evidence

BDD is part of the revision, not documentation beside it. Approval is impossible unless all
scenarios for the exact artifact are green.

Minimum system scenarios include:

```gherkin
Given DigitalBrain is activated for an owner
And the approved StartUi Behavior subscribes to DigitalBrainActivated
When activation is committed
Then the Behavior journal records the execution
And IShell receives OpenScene for the first screen
And SceneOpened is committed
And Flutter renders from SceneOpened
```

```gherkin
Given an assistant resolves an installed Behavior intent
When its request matches the approved input schema and grants
Then the exact active revision executes
And the result matches the approved output schema
```

```gherkin
Given a worker dies after a capability call completes
When the same execution is recovered
Then the broker returns the recorded call result
And the module effect is not invoked a second time
```

```gherkin
Given a proposed revision passed compilation and BDD
When any source, dependency, manifest, policy, or test input changes
Then its revision hash changes
And the previous approval cannot install it
```

Product proofs assert journals and observable module/edge outcomes. Private-field assertions,
compile-only checks, and mocked grain substitutes do not prove a Behavior works.
