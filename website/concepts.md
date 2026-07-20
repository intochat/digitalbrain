# Concepts

DigitalBrain is built from neurons, synapses, modules, and simulations.

## Neuron

A neuron is a durable Orleans journaled grain. It receives and emits synapses, keeps bounded incoming
and outgoing journals, enforces owner and delivery invariants, and recovers after silo restart.

Domain-specific capability belongs in a module neuron. `Llama32` is an AI neuron; a future
`DigitalBrain.Google.ICalendar` will be a Google neuron. Kernel `Neuron` remains domain-neutral.

## Synapse

A synapse is an immutable typed fact. The kernel carries it in a read-only delivery envelope with
correlation and causation lineage. A neuron declares `IHandle<TSynapse>` for facts it consumes and
`IEmit<TSynapse>` for facts it produces.

A typed neuron method is a directed request that can reply. A synapse is an undirected fact that
does not. Both cross the same owner-aware Orleans boundary.

## Module

A module is a compile-time package family that owns one domain's vocabulary, runtime, dependencies,
and optional Aspire hosting:

```text
DigitalBrain.Modules.AI.Contracts
DigitalBrain.Modules.AI
DigitalBrain.Modules.AI.Aspire.Hosting
```

AppHost selects modules. Source generation composes them into `silo.AddDigitalBrain()`.

## Simulation

A simulation is the dev-only testing primitive. It runs real neurons on a three-silo in-process
cluster and asserts on journals and telemetry. The feature files published under
[Specification](/specification) are executable guarantees, not examples.

## Scope

The current scope is the durable kernel, typed client, generated module composition, and the AI
module. Google, Salesforce, Flutter, Memory, semantic discovery, and runtime behavior installation
are out of scope until their dependencies are ready.
