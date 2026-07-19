---
title: DigitalBrain.Client
---

# DigitalBrain.Client

Talking to a brain from outside the cluster. No provider SDK, no model binding, no secret.

```csharp
var brain = new BrainClient(grainFactory, new OwnerId("acme"));

await brain.FireAsync(nameof(Greeter), "first", new Hello());

var handled = await brain.Neuron<Greeter>("first").ReadJournalAsync(JournalKind.Incoming);
var fired = await brain.Session.ReadJournalAsync(JournalKind.Outgoing);
```

## Everything happens as an owner

A `BrainClient` is constructed for one `OwnerId`, and every call it makes is scoped to that owner.
Firing goes through the owner's session neuron, which checks the target's owner **before** it commits
anything and throws `NeuronAuthorizationException` on a mismatch. A refused fire leaves no trace in the
session journal, because it never happened.

::: danger An Orleans client is a trusted cluster peer
The owner boundary is a correctness boundary, **not** an authentication boundary. Orleans clients are
trusted members of the cluster: any process holding an `IGrainFactory` can address any grain directly,
including another owner's, without going through `BrainClient` at all. The kernel's incoming call
filter constrains neuron-to-neuron traffic, where the caller's identity is known and unforgeable; it
cannot constrain a caller that is already inside the cluster's trust boundary.

Authenticate and authorize your users at the edge — in the service that owns the `BrainClient` — and
never hand a cluster connection to code you would not trust with every tenant's data. Do not expose an
Orleans client endpoint to the public internet.
:::

## The session is a real neuron

`brain.Session` is not a client-side object — it is a durable neuron whose outgoing journal is the
owner's record of what they asked for. It survives process restarts, and it is readable the same way
any other neuron's journal is.

## `NeuronHandle`

`brain.Neuron<TNeuron>(name)` and `brain.Neuron(type, name)` return a handle exposing `Id` and
`ReadJournalAsync(kind)`. Reading a journal is how a client learns what happened.

::: warning Open debt
There is no timeline stream yet, so a client can fire and read but cannot **observe**. Code that wants
to react to a brain must poll a journal. This is the most visible missing primitive in the current
foundation.
:::
