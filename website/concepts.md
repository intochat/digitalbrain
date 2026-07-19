# Concepts

DigitalBrain is an open-source .NET framework whose paradigm is **neurons, synapses, and
simulations**. It is a foundation framework: the strong base the full Digital Brain system grows on,
built on Orleans and Aspire.

## Neuron

A neuron is a durable agent: an Orleans journaled grain with two durable journals — one for incoming
synapses, one for outgoing synapses. A neuron has a typed identity, is bound to an owner for
authorization, and recovers its state after a silo restart by replaying its journals.

## Synapse

A synapse is an immutable typed message record. Every synapse carries metadata: a synapse id,
correlation and causation lineage stamped on every hop, the caller, the receiver, the routing mode,
and a timestamp.

Synapses are the programming model. A neuron declares `IHandle<TSynapse>` for what it consumes and
`IEmit<TSynapse>` for what it produces. The wiring is provable at build time through a
source-generated dispatch manifest. Broadcast reaches every subscribed neuron durably; point-to-point
delivery is guaranteed and typed.

## Simulation

A simulation is the testing primitive, shipped as a public dev-only package: fire a synapse into a
real in-process cluster, then expect synapses on the timeline. The framework's own test suite and its
consumers' test suites use the same machine. The executable specifications that drive the framework
are published on this site as they land.

## What the framework ships

The neuron runtime, the synapse fabric, a queryable subscription registry, multi-silo support,
AI model binding with role tiers and provider isolation, a client package, Aspire integration, the
testing package, dev tools, and a quickstart.

Marketplaces, pack signing, runtime code loading, rule engines, UI surfaces, federation, voice, and
MCP servers are deliberately out of scope for the foundation. They come later, on top of it.
