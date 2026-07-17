# What is DigitalBrain?

DigitalBrain is an open-source capability operating system built from **neurons** and **synapses**.

A neuron is an addressable capability hosted by one universal Orleans grain. Its kind supplies deterministic domain behavior. A synapse is a typed relationship stored with a neuron.

::: info Read labels literally
Documentation uses **Implemented**, **Target**, and **Decision** deliberately. The [implementation status](/reference/status) is the evidence ledger for every broad architecture claim.
:::

## The current model

The v2 path is deliberately small:

```text
typed contract
  → typed client proxy
  → universal neuron envelope
  → NeuronGrain
  → registered INeuronKind
  → journaled events
```

Create a neuron when a capability needs stable identity, state, serialized access, observation, or an independent lifecycle. Keep DTOs, provider payloads, UI blocks, and transient calculations as values.

## Why an operating system?

Applications repeatedly rebuild identity, lifecycle, provider integration, effects, and user surfaces. DigitalBrain concentrates the shared execution rules in a small kernel and lets explicitly composed modules own domain capability.

The ambition is larger than the current implementation. Authentication, durable production storage, richer authorization, module packaging, and community isolation are not presented as finished.

## Follow the evidence

1. [Run the Aspire topology](/getting-started/).
2. [Make the first MCP call](/getting-started/first-call).
3. [Trace the architecture](/guide/architecture).
4. [Build a module against the current model](/build/first-module).
