# What is DigitalBrain?

DigitalBrain is an open-source operating system for software built from **neurons** and **synapses**.

A neuron is an addressable, stateful capability. A chat, memory, connection, approval, webhook receiver, model, workspace, or long-running behavior can be a neuron when it needs durable identity and lifecycle.

A synapse connects neurons. It can describe durable topology, carry an immutable fact, or bind a governed effect to its decision. Commands remain typed calls; synapses do not become a second RPC system.

::: info Architecture status
These pages describe the target architecture we are refining from the kernel outward. The [implementation status](/reference/status) page distinguishes running code from planned contracts.
:::

## Why an operating system?

Traditional applications repeatedly rebuild identity, permissions, background work, provider integrations, observability, and user surfaces. DigitalBrain puts those invariants in a small kernel and lets modules focus on capability.

The goal is not to make every object an actor. The rule is narrower:

> Everything that needs durable identity, state, policy, or lifecycle is a neuron.

Plain values remain plain values. DTOs, block documents, provider payloads, and transient calculations do not become neurons.

## Start here

- [Architecture](/guide/architecture) explains the layers.
- [Neurons](/guide/neurons) defines the unit of identity and behavior.
- [Synapses](/guide/synapses) defines relationships and fact propagation.
- [Modules](/guide/modules) explains how the ecosystem extends the system.
- [Programming model](/guide/programming-model) shows typed C# contracts.
- [Webhook neurons](/guide/webhooks) shows how Stripe-style ingress fits the model.

## Run the repository

```powershell
aspire run
```

The Aspire dashboard starts the kernel, edges, model infrastructure, and this documentation website. Open the `brain-docs` resource to browse the live VitePress site.
