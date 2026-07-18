# Simulations — the only testing framework

> A test is a Simulation. A Simulation is a neuron. **Every** Software 2.0 test is a simulation.

## One concept replaces four drivers

v1 had four overlapping ways to drive a neuron under test (`NeuronTestHarness`, `NeuronSimulationContext`, `LiveSiloSimulationBackend`, Reqnroll `*Steps`) plus a `FakeDigitalBrain`. v2 has **one**:

```csharp
public abstract class Simulation : Neuron        // a test IS a neuron in the live silo
{
    protected Task Fire(Synapse s);                       // the only stimulus verb
    protected Task<T> Expect<T>(Func<T,bool>? where = null, int ms = 3000) where T : Synapse;  // assert presence
    protected Task ExpectNone<T>(Func<T,bool>? where = null, int ms = 500) where T : Synapse;   // assert absence
}
```

The model is symmetric and tiny:

- **stimulate = fire a synapse** (`Fire`) — no mocks, the real substrate routes it.
- **assert = await a synapse** on the timeline (`Expect`) — no fakes, you observe what really happened.

```csharp
public sealed class PingSimulation : Simulation
{
    [Fact]
    public async Task Ping_is_echoed_as_broadcast_pong()
    {
        await Fire(new Ping(From: "alice") { Routing = RoutingMode.Broadcast });
        var pong = await Expect<Pong>(p => p.To == "alice");
        Assert.Equal("alice", pong.To);
    }
}
```

## Why "everything is a simulation" matters

An AI-authored neuron has no human to hand-write its xUnit. Its `.ino` carries its own `scenario` block. The closed loop:

```
intent ─► author .ino ─► compile ─► run scenario AS A Simulation ─► green? ─► activate
                                              │ red?
                                              └─► feed diagnostics back, retry
```

The **test framework and the safety gate are the same machine.** A human writing `PingSimulation` and the loop running a generated `scenario` take the identical code path: fire into the live silo, assert on the timeline.

## Three levels, one base

| Level | What `Fire`/`Expect` span | Example |
|---|---|---|
| Unit | one neuron-under-test | ping in, pong out |
| Integration | a chain of real neurons | ping → echo → room announce |
| Authored (gate) | a generated neuron + its `scenario` | the closed loop |

No separate frameworks. The breadth changes; the base class does not.

## Self-contained capsule layout

Each capability ships as a triplet — impl + interface-contracts + simulations — in one folder, so a neuron and its test travel together:

```
Ping/
├─ Ping.Contracts/        IPingNeuron : INeuron, IHandle<Ping>, IEmit<Pong>;  Ping; Pong
├─ Ping/                  PingNeuron : Neuron, IPingNeuron   (+ ping.ino)
└─ Ping.Simulations/      PingSimulation : Simulation
```

## What we deleted

- `FakeDigitalBrain` — the real `Brain` neuron is in the silo; faking it tests the fake.
- `ISimulationBackend` + backend selection — there is one substrate; it *is* the backend.
- The 10-field serializable `SimulationSpec` DSL — a test is code (`Fire`/`Expect`). The serialized spec returns only as the lowered form of an `.ino scenario` when it must cross the grain boundary into the AI loop (~10% added back).
