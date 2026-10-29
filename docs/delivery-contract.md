# Signal Delivery Contract

## Guarantees
Signals emitted by a neuron via `Save(state, signal)` are delivered **at-least-once** to every registered reaction subscriber in `ISynapseRegistryIndex`.

## Payload
Each delivered signal envelope carries the strongly typed `Signal` record, sequence number, source neuron address, and trace context.

## Ordering and dedupe
Within a single source neuron and signal type, signals are ordered monotonically by sequence number. Subscribers deduplicate replayed deliveries using their last acknowledged watermark.

## Catch-up
When a subscriber restarts or reconnects, `ResumeAsync` replays retained signals from the subscriber's watermark forward.

## Retention floor and Gap
If a subscriber's watermark falls behind the stream's retention floor, the runtime emits `Gap(FromSequence, ToSequence)` before delivering newer signals.

## Dead subscribers
Unreachable or retired subscribers are pruned after bounded retry windows so retention floors advance cleanly.

## Write-then-crash window
State and outbox entries are committed together before stream publication; if a crash occurs before publication completes, recovery publishes the committed outbox entry on activation.

## Authorization
Every synapse admission checks `AdmitSynapse` against the caller and target scope before any subscription is recorded in `ISynapseRegistryIndex`.

## Topologies
Both `TestOS.Local()` (deterministic in-process queue) and `SiloHost` (distributed Orleans streams) honor this exact contract.

## Stream identity
Each stream is uniquely identified by `(BrainId, NeuronType, NeuronKey, SignalName)`.
