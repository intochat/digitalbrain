# DigitalBrain

DigitalBrain is a programming model for durable module behavior. This glossary
names the concepts shared by its module surface, trusted access boundary, and
Hosting runtime.

## Language

**Synapse**:
A sealed piece of module vocabulary that a source or behavior produces and a
behavior may handle.
_Avoid_: Fact, event, message

**Neuron**:
A module behavior whose useful lifetime is one bound turn.
_Avoid_: Grain, actor, worker

**Turn**:
One handling opportunity for a received synapse, including any staged output
and optional state change.
_Avoid_: Invocation, transaction

**Produced synapse**:
A synapse staged by a source or behavior during a turn for later delivery.
_Avoid_: Emitted fact, command

**Recorded turn**:
The durable unit containing a turn's received and produced synapses, touched
state, and delivery watermark.
_Avoid_: Commit, event batch

**Journal**:
The ordered durable truth of a logical behavior instance.
_Avoid_: Log, event store

**Journal record**:
One received or produced synapse entry in a journal, with origin, causation,
delivery targets, and raw serialization.
_Avoid_: Journal fact, envelope

**Delivery**:
The post-record attempt to present a produced synapse to a receiving behavior.
_Avoid_: Send, dispatch

**NeuronId**:
The logical identity of one behavior instance, made of its registered kind and
its name.
_Avoid_: Grain key, address

**Source**:
A trusted publisher identity that begins a recorded synapse flow.
_Avoid_: Ingress, session

**Module**:
A package of synapse vocabulary and behavior types that depends only on
Abstractions and Core.
_Avoid_: Plugin host, runtime service

**Access**:
Trusted publication and journal-reading capabilities used outside behavior
modules.
_Avoid_: Module API, behavior capability

**Hosting**:
The runtime boundary that composes modules and owns durability, activation,
serialization, routing, and delivery.
_Avoid_: Core, module
