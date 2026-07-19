---
title: DigitalBrain.Testing
---

# DigitalBrain.Testing

Simulations: scenarios fired into a real Orleans cluster and asserted against real journals. No mocked
grains, no fake fabric. If a simulation passes, the behaviour works on a cluster.

## The cluster

`SimulationCluster` starts **one** three-silo `InProcessTestCluster` for the whole test run, with silos
labelled `alpha`, `beta` and `gamma` so placement is deterministic and cross-silo behaviour is testable
rather than incidental. Starting a cluster per scenario would make the suite unusably slow, so the
cluster is shared and scenarios isolate by owner instead.

`SimulationCluster.RestartHostOfAsync(neuron)` restarts only the silo hosting a named neuron — which is
how recovery is proven without stranding the client's directory cache.

## The vocabulary

Scenarios are written in Gherkin over a shared step vocabulary in `NeuronSteps`:

```gherkin
Scenario: a reply is recorded in the replying neuron's outgoing journal
    Given a brain for owner "verbs"
    When Ping is sent to the Greeter neuron named "polite"
    Then the outgoing journal of the Greeter neuron named "polite" contains Pong
```

The steps read and assert through the same public surface a consumer uses. See the full corpus on the
[Specification](/specification) page.

## Waiting without sleeping

`SynapseObserver` listens to the `DigitalBrain` activity source, so a scenario waits for a synapse to
be *handled* rather than sleeping and hoping. Assertions that cannot be made deterministic are deleted
rather than retried — a flaky simulation is a lie about a guarantee.

## The scripted model

`ScriptedModel` is an `IChatClient` that answers exactly what a scenario scripted and throws
`UnscriptedPromptException` on anything else. It never invents a plausible answer, because a test that
passes on a fabricated model response is testing nothing.

```gherkin
Given the balanced model answers "summarise" with "done"
```

::: warning Test-only
This package references `DigitalBrain.Kernel` because it hosts real silos. It is not a production
dependency, and a release gate fails the build if any production package references it.
:::
