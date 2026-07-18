# How it works

ino is built from two primitives — **neurons** and **synapses**. Together they form **behaviors**: composable, observable units of intelligence guaranteed by BDD tests.

<HowItWorksDiagram />

## Neurons

A neuron is a small, specialized intelligence unit. Each one is an expert at a single thing — executing shell commands, analyzing code, managing version control. Neurons are created at runtime:

```bash
ino create shell --purpose "execute commands"
ino create git --purpose "version control"
```

Every neuron is an addressable Orleans grain with a stable identity. Once created, its lifecycle is visible on the time-travel timeline.

## Synapses

A synapse is a directed connection between two neurons, tagged with a **verb**. It plays three roles simultaneously:

- **Signal** — a typed, durable message from one neuron to another
- **Memory** — a decay score (0–100) that fades over time, so the system forgets what doesn't matter
- **Thinking** — executable C# code that gives neurons Turing-complete reasoning

Connect neurons and fire signals:

```bash
ino connect shell git commit
ino fire shell git commit "fix: typo in readme"
```

Every fired synapse is captured on the timeline with its verb, payload, and decay state.

## Behavior = Neuron + Synapse

When neurons connect via synapses, **behavior emerges**. A behavior isn't a monolith — it's a composition:

| Behavior | Neurons | Synapse verbs |
|----------|---------|---------------|
| code-review | assistant + roslyn + git | analyze, commit |
| auto-build | assistant + dotnet + roslyn | build, compile |
| smart-commit | assistant + shell + git | delegate, commit |

Behaviors are compositional, not prescribed. Add a neuron, wire a synapse, and a new behavior appears — no code changes to the existing neurons.

## Guaranteed by BDD

Every behavior is backed by a Gherkin `.feature` file. One feature per neuron, one scenario per synapse verb. If the test passes, the behavior is real. If it doesn't, the behavior doesn't ship.

```gherkin
Feature: Runtime neuron lifecycle

  Scenario: Create a neuron from a blueprint records it on the timeline
    Given a running test cluster with timeline capture enabled
    And the neuron registry is available at "global"
    When I create a neuron named "greeter" with purpose "welcomes new users"
    Then the registry lists exactly 1 neuron
    And the timeline contains a NeuronActivated event with verb "create_neuron"

  Scenario: Fire a synapse along a connection records it on the timeline
    Given a neuron named "greeter" exists
    And a neuron named "logger" exists
    And the two neurons are connected with verb "log_greeting"
    When the "greeter" neuron fires that synapse with payload "{\"message\":\"hi\"}"
    Then the returned receipt has a valid timeline sequence number
    And the timeline contains a SynapseFired event with verb "log_greeting"
```

These scenarios are the canonical contract. The system can only do what the tests prove it can do — and every interaction is observable on the [time-travel timeline](/guide/architecture).

## The Brain — 203 Apps, 25 Domains

ino's long-term reach is organized as a brain — a central `ino` core surrounded by 25 domain clusters, each containing the real-world apps it speaks to. Click any domain to expand it into its app neurons. Colors mark readiness: green is wired today, yellow is planned, gray is vision.

<BrainView />

Every app that shows up here becomes a neuron. Every arrow you see above becomes a synapse you can fire, observe, and replay.
