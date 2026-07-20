# Concepts

DigitalBrain is an open-source .NET framework whose paradigm is **neurons, synapses, and
simulations**. It is a foundation framework: the strong base the full Digital Brain system grows on,
built on Orleans and Aspire.

This page describes the three primitives as they exist today. For where they are going — the
programming model, behaviors, and capabilities — see [Architecture](/architecture).

## Neuron

A neuron is a durable agent: an Orleans journaled grain with two durable journals — one for incoming
synapses, one for outgoing synapses. A neuron has a typed identity, is bound to an owner for
authorization, and recovers its state after a silo restart by replaying its journals.

Journals are bounded. A neuron keeps a summary that survives compaction, so a consumer reading a
neuron still sees a truthful account of it after the delta log has evicted old entries.

## Synapse

A synapse is an immutable typed fact record. Before it crosses a neuron boundary, the kernel snapshots
it into a read-only delivery envelope carrying a synapse id, correlation and causation lineage, the
caller, an origin sequence, and a timestamp. Receiver selection remains an outbox decision and is not
fact metadata.

A neuron declares `IHandle<TSynapse>` for what it consumes and `IEmit<TSynapse>` for what it
produces. Those declarations live on the interface, so the wiring graph can be read without loading
or executing an implementation, and it is provable at build time through a source-generated dispatch
manifest. Broadcast reaches every subscribed neuron durably; point-to-point delivery is guaranteed
and typed.

**A synapse is a fact**: something happened, announced to whoever cares, with no reply. It is one of
two verbs. The other is a **request** — a typed method on a neuron interface, directed at a specific
capability, which replies. Facts are emitted; requests are called. Both belong on the durable rail,
and [Architecture](/architecture) explains how requests get there without the caller cooperating.

## Simulation

A simulation is the testing primitive, shipped as a public dev-only package: fire a synapse into a
real in-process cluster, then expect synapses on the timeline. The framework's own test suite and its
consumers' test suites use the same machine. The executable specifications that drive the framework
are published on this site as they land.

Simulations observe rather than poll. The testing package listens to the activity stream the kernel
emits on every delivery, so a scenario waits on an event instead of sleeping.

## What the framework ships

The neuron runtime, the synapse fabric, a queryable subscription registry, multi-silo support,
AI model binding with role tiers and provider isolation, a client package, Aspire integration, the
testing package, dev tools, and a quickstart.

The model-tier abstraction is scheduled for removal. It is the reason the kernel references two
vendor SDKs today, which means a neuron that renders a button ships an LLM client. AI becomes an
ordinary module; see [Architecture](/architecture) and [Status](/status).

Marketplaces, pack signing, runtime code loading of new grain types, rule engines, federation, and
voice are deliberately out of scope. Runtime loading in particular is rejected rather than deferred:
Orleans fixes its grain type manifest at silo startup, so a type introduced at runtime is invisible
to every peer silo. Behaviors are the answer to the need behind it, and they add no grain types.
