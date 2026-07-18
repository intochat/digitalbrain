# DigitalBrain v2 — Manifesto

> Start from scratch. Bring only the required minimum. **Everything is a neuron or a synapse.**

## The whole system in one paragraph

There are exactly **two kinds of things**: a **Neuron** (an addressable actor — a grain — that holds state, telemetry, logs, and handlers) and a **Synapse** (an immutable typed message). A neuron **receives** synapses and **fires** synapses. Firing is either a **broadcast** (onto the shared timeline, picked up by every neuron that declares it handles that synapse) or a **point-to-point** ask to one named neuron. Nothing else exists at the core. The brain is a neuron. A test is a neuron. A UI widget is a neuron. The marketplace is a neuron. If a new concept cannot be expressed as a neuron exchanging synapses, it does not belong in the core.

## The five non-negotiables

1. **Two primitives only.** `Neuron` and `Synapse`. Every feature is a composition of these.
2. **One verb.** A neuron's only outward action is *fire a synapse* (`IDigitalBrain.Fire`). Broadcast and ask are two routings of the same verb — see [02](02-ino-and-broadcast.md).
3. **Wiring is metadata, not code.** What a neuron consumes (`IHandle<T>`) and emits (`IEmit<T>`) is declared on its **Contracts interface**, so the entire graph is built by scanning assemblies without loading implementations or running anything.
4. **A test is a Simulation, and a Simulation is a neuron.** Tests fire synapses and assert on the timeline through the live substrate. The same machine gates AI-authored neurons. See [03](03-simulations.md).
5. **Two notations, one shape.** Software 1.0 (C#) and Software 2.0 (`.ino`) describe the *same* capsule. An `.ino` file lowers section-by-section to the C# artifacts. See [02](02-ino-and-broadcast.md).

## What we are deliberately NOT bringing into v2 (yet)

Cut from day one; each returns only when a neuron provably needs it (Elon's algorithm — delete first, add back ~10%):

- ❌ Marketplace, Stripe, licensing, `.bdom` signing
- ❌ Spatial UI polish, glassmorphism, comets (the *model* for widgets is in [02](02-ino-and-broadcast.md); the polish is not)
- ❌ LLM swarm, LoRA/NeMo fine-tuning, deliberation panels
- ❌ Durable-task reminders, cron, journaling persistence, OTel dashboards
- ❌ Multi-cluster federation, gRPC gateway hops
- ❌ The 10-field `SimulationSpec` data-DSL and the 4 overlapping test drivers
- ❌ `FakeDigitalBrain` and `ISimulationBackend` indirection

## What IS the required minimum

The smallest set that lets one neuron broadcast a synapse, another neuron handle it and reply, and a Simulation prove it — running on a real Orleans silo.

| Artifact | Role |
|---|---|
| `Synapse` | immutable record + routing metadata |
| `Neuron` | grain base: receive → dispatch `IHandle<T>` → `Emit`/`Ask` |
| `IHandle<T>` / `IEmit<T>` | the **static wiring manifest** (on Contracts interfaces) |
| `IDigitalBrain` (a neuron) | the one verb: `Fire(synapse)` |
| `Simulation` (a neuron) | fire + assert on the timeline = the test = the gate |
| one example capsule (`Ping`) | proves the loop end-to-end |

See [04](04-minimum-and-roadmap.md) for the build order.
