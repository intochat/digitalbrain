# Architecture

This page describes where DigitalBrain is going and why it is shaped this way.

::: warning What is built
The neuron runtime, the synapse fabric, journals, multi-silo delivery, the client, and the testing
package **exist and are proven by the suite**. The programming model on this page — behaviors,
scripts, capabilities, the approval rail — is **designed and not yet built**. Each section says which
it is. [Status](/status) is the authoritative ledger.
:::

## The idea

> A brain you program by writing ordinary C#, and that can program itself.

Every generation of this system before v2 hit the same wall from one side or the other. Make the
programming model typed and ergonomic, and the interesting work becomes invisible — an ordinary
method call leaves no trace. Make everything a message so it is all observable, and the ergonomics
collapse: request and response split across two handlers with correlation state stitched between
them. One prior generation built exactly that message-based model for inference and **never called
it once** — the typed shortcut won every time.

The resolution is not to choose:

> **The typed interface is the surface. The synapse is the substrate. The generator is the bridge.**

You write a typed call. The call is journaled. The correlation is generated, never hand-written.

## Facts and requests

Two verbs, one substrate. The distinction is semantic, not mechanical, and it is what stops the two
paths from competing.

| | Shape | Direction | Reply |
|---|---|---|---|
| **Fact** | a thin record — `NewMail` | broadcast, undirected | none |
| **Request** | an interface method — `gmail.SendAsync` | directed at a capability | yes |

A behavior reads the same way every time: **facts in, requests out to capabilities, facts out.**

Both are journaled. A fact lands on the rail because it was emitted; a request lands on it because an
incoming grain call filter reifies it, without the caller cooperating and without the ability to opt
out. That filter is what makes the typed surface honest — a call that is not on the feed is a call
that did not go through the rail, which is a testable property rather than a hope.

A synapse is a plain record. Delivery metadata — id, correlation, sequence, timestamps — rides on an
envelope the kernel owns and the author never constructs:

```csharp
public sealed record NewMail(string From, string Subject, string Body);
```

*Status: facts, thin records, kernel-owned delivery envelopes, journals, delivery, and call
reification (outgoing filter onto the caller's feed for non-framework capability interfaces) are
built.*

## Vocabulary and behavior

The line that makes live programming possible at all.

Orleans builds its grain type manifest at silo startup and exchanges it between peers at cluster
join. A grain type introduced at runtime is invisible to every other silo. That constraint is real,
it is verified, and it is why runtime module installation is rejected outright.

But it is a constraint on **types**, not on **instances** — and Orleans grains are virtual, so every
instance name already exists. So:

| | Contributes | When | Requires |
|---|---|---|---|
| **Module** | Vocabulary — synapse records, neuron interfaces | Compile time | A rebuild |
| **Behavior** | Logic over existing vocabulary | Runtime | Approval only |

**New nouns need a rebuild. New verbs do not.** Behaviour is the common case; vocabulary is rare.

A behavior is an instance of **one** registered grain type that carries a script as durable state.
This is why no second kind of neuron is ever created — the thing that defeated every prior attempt at
live code, each of which ended up with a first-class typed neuron and a second-class dynamic one that
was never unified with it.

*Status: designed. Modules and behaviors are both unbuilt.*

## The programming model

The client API **is** the programming model. There is one surface to learn, and it is the same
whether the code runs on a laptop or inside the cluster.

```csharp
var brain = DigitalBrainClient.Connect();
var gpt   = brain.Get<IGpt56>();

await brain.On<NewMail>(async mail =>
{
    var verdict = await gpt.AskAsync($"Is this urgent? {mail.Body}");
    if (verdict.IsUrgent) await brain.Emit(new Escalation(mail.From));
});
```

| | Outside the cluster | Installed as a behavior |
|---|---|---|
| `Connect()` | opens a cluster connection | the ambient context, bound to this behavior |
| `Get<T>()` | a typed proxy over the wire | a typed proxy over the local grain factory |
| `On<T>()` | a subscription | handler registration |
| Lifetime | the process | durable — journal, state, survives restart |

`Get<T>()` resolves the owner's default instance; `Get<T>(name)` reaches a named sibling. **Owner is
always ambient and never a parameter**, so a script cannot address another owner's neuron — the
boundary is a property of the API's shape rather than a check that can be forgotten.

A UI component is not a special case. It is a neuron interface contributed by a UI module, resolved
like anything else:

```csharp
var calendar = brain.Get<ICalendar>();
var chosen   = await calendar.PickDateAsync("When shall we meet?");
```

*Status: designed. `DigitalBrain.Client` today has two verbs — fire and read a journal.*

## Capability and approval

A behavior gets **any behaviour**. It does not get **any capability**.

Capability is exactly the set of typed interfaces a script can resolve, which is the set of contracts
packages it compiles against. That makes a module's `.Contracts` package the unit in which capability
is granted, and it is why contracts are separated from implementations on day one rather than as
hygiene.

Installing a behavior runs its script once with a **recording context**: `On<T>` and `Get<T>` record
instead of acting. What comes back is the manifest — facts handled, capabilities requested, facts
emitted — and that is the approval screen. It is derived from the code, so it cannot drift from it,
and a script cannot hold a capability it did not ask for, because asking is how it is recorded.

**Every install is a human-approved proposal**, including a behavior authoring or modifying another
behavior. The script *is* the behavior's state, so an approval is a journal entry and a rollback is
reverting one.

Enforcement applies to **scripts, not to compiled code**. A compiled module is C# in the silo process:
it can read any static and open any socket, and gating it would be theatre. A prior generation proved
this by shipping a grant check that was a hardcoded `return false`, and whose own audit found grants
evaluated *after* the privileged call had already fired. The boundary here is script versus compiled
code, stated plainly so it cannot decay into the same pretence.

*Status: designed. The governance ledger is unbuilt.*

## Observation

One durable feed per identity, not per connection. A module mutating state and an external client
mutating state converge on the same feed, so a client watching that feed sees both.

The feed is **a neuron**, not a subsystem — it reuses the snapshot, bounded delta log, and monotonic
cursor that every neuron's journal already has. Watching is one verb on every neuron rather than a
bespoke protocol. A reader whose cursor has fallen off the log receives a reset carrying a full
snapshot and a resume sequence — never a gap, never silence.

Two rails, deliberately distinct:

- **Domain facts are feed-worthy** — durable, replayable, bounded.
- **Call traffic is traced** — OpenTelemetry activities, already emitted on every delivery today.

Because subscription for compiled neurons is resolved at composition time, the kernel knows at startup
whether anything handles a given fact and **never constructs one nothing handles**. A dynamic registry
could not do that, since something might subscribe later. That is what makes "everything is a fact"
affordable rather than ruinous.

*Status: journals, bounded compaction, and trace emission are built. The per-identity feed and the
watch verb are designed.*

## What is deliberately rejected

Recorded so it is not silently reversed. The current decisions and deletion manifest are in
`REFINED-ARCHITECTURE-AND-NEXT-STEPS.md`.

| Rejected | Why |
|---|---|
| Runtime module install | Orleans and Aspire both freeze topology at startup; every attempt produced a second-class neuron kind |
| Module sandboxing | Compile-time modules run in-process; gating them is theatre, and the prior art documents its own escape hatches |
| A generic UI renderer | Built and shipped by an earlier generation, which is now deleting it |
| Inference as a message hop | Built in a prior generation and invoked zero times; splits handler logic in two |
| Unbounded journals | Quadratic dedupe cost for a replay capability nothing used |
| Bounded journals without a snapshot | Silently destroys the only data a client can read |
| A new DSL | Attempted three times in this lineage and abandoned three times. Real C# with a real compiler is the correction |

## The assumption this rests on

The scripting rail assumes a language model can reliably emit these scripts. **That is unmeasured.**

The design makes it plausible — real C#, the compiler as the gate, the smallest surface that can
express a behavior — but plausible is not measured. A prior generation gated a language on a
twenty-prompt benchmark, scored it at 60%, and formally demoted the language; its own documents record
that the score came from a **deterministic stub rather than a model**, and that the interpreter shipped
afterward regardless.

So the standard, with both corrections that failure earned: the benchmark runs against a real model or
it does not count, and demotion is enforced against the codebase rather than the specification.
