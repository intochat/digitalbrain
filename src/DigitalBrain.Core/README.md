# DigitalBrain.Core

`DigitalBrain.Core` is the small programming model used to author a DigitalBrain
module. It is deliberately a deeper module than its public type count suggests:
module authors work with a behavior and a turn, while Hosting owns activation,
durability, routing, serialization, and delivery.

## Module-author surface

A behavior module references `DigitalBrain.Abstractions` and `DigitalBrain.Core`.
Its useful surface is intentionally narrow:

- `NeuronId(kind, name)` identifies the behavior instance that is handling now.
- `Neuron` exposes `Id` and `Emit(Synapse)` while a turn is active.
- `Neuron<TState>` adds one optional state value for that behavior instance.
- `INeuron<TSynapse>` declares and handles a synapse type.

`Emit` stages a produced synapse; it does not deliver it immediately. `Id`,
`Emit`, and `State` are invalid outside `HandleAsync`, so a behavior cannot retain
runtime access after its turn ends.

Behavior modules do not reference Orleans, the Access capability package, or
durability and lifetime APIs. They contain domain vocabulary and behavior only.

## Ownership and seam

| Package | Owns |
| --- | --- |
| `DigitalBrain.Abstractions` | `Synapse`, `INeuron<TSynapse>`, `NeuronId` |
| `DigitalBrain.Core` | The pure behavior facade and public recorded-journal model |
| `DigitalBrain.Access` | Trusted publication and journal-reading capabilities |
| `DigitalBrain.Hosting` | Composition, serialization, the durable Orleans adapter, journal storage, routing, and delivery |

Hosting registers a module vocabulary and its behavior kinds explicitly. It
creates a fresh behavior for each received synapse, binds it to one turn, and
removes that binding when handling returns.

## Recorded truth

Hosting records each successful source publication or behavior turn as one
durable unit. A behavior turn can contain its received synapse, all staged
produced synapses, a touched state value, and its delivery watermark. Delivery
begins only after that record exists.

`JournalReader` returns either a `JournalPage` or `JournalHistoryUnavailable`.
Each `JournalRecord` preserves direction, origin, causation, the delivery-target
snapshot, and raw JSON serialization. Reads are passive: they may load the
durable host, but never run a behavior or start delivery.
The initial journal range starts at position 1, so a cursor before that range is
an unavailable-history outcome.

Publication and reading are trusted process capabilities, not behavior-module
capabilities. Product code can use `SynapsePublisher` and `JournalReader` at an
appropriate boundary without granting them to a module.

## Non-goals

Core does not define product synapses or product modules. It does not provide a
global coordinator, topology management, schedules, request/reply modes, or a
second communication path. Learning, export, and fine-tuning consume recorded
journal truth in product-owned code rather than becoming Core behavior.

## Refactoring bar

Every change must remove complexity or add necessary mechanics with a current
consumer. Keep one top-level type per file unless a small closed vocabulary is
clearer together. Verify the seam mechanically: clean module references,
explicit composition, one recorded turn before delivery, raw journal pages, and
restart-safe delivery.
