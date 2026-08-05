# DigitalBrain

The language of a single-owner digital brain whose durable behavior emerges from neurons
communicating with immutable facts.

## Language

**Owner**:
The single person or organization whose memory, policies, and behavior belong to a brain.
_Avoid_: Tenant, principal

**Brain**:
The durable body of memory and behavior belonging to one owner.
_Avoid_: Application, agent platform

**Deployment**:
One isolated instance of a brain. Organizations isolate owners with separate deployments.
_Avoid_: Tenant, account

**Core**:
The programming paradigm and invariant runtime physics shared by every neuron and synapse.
_Avoid_: OS, product host

**Kernel**:
The deployable operating system built on Core; it owns behavior creation, behavior lifecycle,
and capability composition.
_Avoid_: Core, framework

**Module**:
An independently shipped vocabulary of synapses and neuron kinds.
_Avoid_: Plugin registration, service collection

**Behavior**:
A neuron created or installed by Kernel to express an owner-requested capability. It is not a
separate execution abstraction.
_Avoid_: Workflow, script, orchestrator

**Broadcast**:
A synapse spoken to every neuron kind that declares it in the current context.
_Avoid_: Emit, publish

**Reply**:
The synapse returned by a neuron to the source of the synapse it handled.
_Avoid_: Answer wrapper, response envelope

**Completed**:
The Core synapse recording that a neuron handled a synapse without producing a reply.
_Avoid_: Null reply, empty result
