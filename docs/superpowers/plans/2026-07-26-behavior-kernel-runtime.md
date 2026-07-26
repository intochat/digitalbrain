# Behavior Kernel Runtime Implementation Plan

**Status:** Designed — current implementation plan; it does not promote the Behavior installation or execution rail to Built.

**Goal:** Make every installed Behavior a real journaled neuron with durable event/intent execution, exact revision selection, private state, and replay-safe module capabilities.

**Architecture:** `BehaviorNeuron` owns proposal/approval/execution/state history; an owner-scoped `BehaviorCatalogNeuron` atomically selects an installed revision and its complete alias set; an owner-scoped execution queue neuron hands committed work to a hosted pump outside Orleans turns. The existing capability delegation/filter path is extended to commit exact typed results before returning, and a signed in-process executor proves the whole rail before unknown-code execution is enabled.

**Tech Stack:** Existing `Neuron`/durable outbox, Orleans 10.2.2-rc.2 and Journaling 10.2.2-rc.2.alpha.1, Orleans standalone serialization, generated capability adapters, `BackgroundService`, bounded `System.Threading.Channels`, Reqnroll/xUnit v3.

## Global Constraints

- Exactly one `[GrainType("behavior")]` implementation exists in the Orleans manifest.
- `BehaviorNeuron : Neuron, IBehavior`; `BehaviorCapabilityBroker` is deliberately not a `Neuron` so the existing delegated-call filter branch is used.
- Every owner-level durable runtime coordinator is a neuron: catalog and execution queue included.
- A Behavior turn commits a receipt and queue handoff, then returns; it never awaits user code, a process, gRPC, or compilation.
- Catalog installation changes the active revision and complete subscription set in one durable catalog commit.
- Runtime routing uses stable `[Alias]` values; CLR full names never appear in persisted subscription keys.
- Sequential capability calls use `(BehaviorExecutionId, CallOrdinal)` plus canonical request fingerprint.
- An identical replay returns committed result bytes; a changed fingerprint fails; consumed-without-terminal is outcome-uncertain and is never silently repeated.
- Orleans `RequestContext` carries bounded causal metadata only; actual source grain, delegation, target, contract, method, owner, revision, ordinal, and fingerprint checks authorize.
- Behavior-private state commits only with a valid correlated completion.
- The trusted in-process executor is available only for source-controlled, signed boot/recovery artifacts and uses the same artifact, context, broker, journals, and BDD.
- The current Flutter concrete Behavior is removed in the same commit that introduces the sole generic grain and the signed `StartUi` replacement.

## Task order and dependencies

Execute the numbered tasks in order. Tasks 1–3 establish durability, Tasks 4–6 establish the intent/dispatch seam, Task 7 consumes the prior routing, state, queue, and result-aware delegation work, and Task 8 consumes Tasks 1–7. Task 4 explicitly defers the sole control implementation to Task 7 and its runtime receipt/outcome proof to Task 8; Task 5 supplies records for Tasks 6–7.

| Order | Responsibility | Task |
| --- | --- | --- |
| 1–3 | Durability | [Tasks 1–3: durability](./2026-07-26-behavior-kernel-runtime-durability.md) |
| 4–6 | Intent and dispatch | [Tasks 4–6: dispatch](./2026-07-26-behavior-kernel-runtime-dispatch.md) |
| 7–8 | Capabilities and product proof | [Tasks 7–8: capabilities and product proof](./2026-07-26-behavior-kernel-runtime-capabilities-and-product-proof.md) |

The task text, interfaces, paths, commands, rationale, and acceptance expectations remain in the responsibility files. This stable index is the durable navigation surface.
