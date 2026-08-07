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
A trusted publisher identity that begins a recorded synapse flow. Its channel
is capability-bound to an explicit set of admissible ingress synapse types.
_Avoid_: Ingress, session

**Ingress**:
An explicitly registered synapse type that may cross from a trusted source into
the durable product graph. Product outputs and Hosting outcomes are not ingress.
_Avoid_: Any public synapse, generic event endpoint

**Origin**:
The source identity, sequence, and Hosting-stamped occurrence time of the
synapse currently bound to a behavior turn. It never carries workspace scope.
_Avoid_: Caller-provided audit timestamp, tenant context

**Module**:
A package of synapse vocabulary and behavior types that depends only on
Abstractions and Core.
_Avoid_: Plugin host, runtime service

**Access**:
Trusted publication and journal-reading capabilities used outside behavior
modules.
_Avoid_: Module API, behavior capability

**Workspace**:
One tenant's durable isolation boundary. Its scope is owned by Hosting and
Access; it is never a product fact field or a user-supplied routing value.
_Avoid_: Owner, tenant string, namespace

**Workspace binding**:
An opaque Hosting value available only while trusted composition creates a
workspace-local provider adapter. A behavior receives its narrow module
interface, never a workspace scope.
_Avoid_: Ambient tenant context, module scope token

**Proposal**:
A frozen semantic review of intended external work: its evidence, exact action
binding, presentation fields, and deadline. It is not a mutable draft or a UI
card.
_Avoid_: Task, pending action, approval request

**Approval decision**:
A durable approve, reject, or expiry outcome for one frozen proposal, audited
with the deciding actor when there is one.
_Avoid_: Button click, modal result

**Action binding**:
The opaque, exact reference and fingerprint that link a proposal to one
prepared provider operation. It prevents a decision from authorizing a
different operation.
_Avoid_: Generic command, mutable callback

**Prepared mutation**:
An immutable provider change that has been validated and is eligible for
approval, but has not been invoked.
_Avoid_: Executed change, draft

**Outcome uncertain**:
A terminal provider result in which DigitalBrain cannot prove whether an
external operation took effect. It is distinct from success and is not blindly
retried.
_Avoid_: Success with warning, transient failure

**Sales query**:
A correlated request for a named sales measure over an explicit reporting date
range and currency. It is resolved before it enters the durable product graph;
it is not free-text provider intent.
_Avoid_: SOQL string, analyst prompt, chart request

**Sales insight**:
An immutable, typed result of a sales query: dated measures, total, count, and
the opaque context in which to show it. It is domain data, not a chart widget.
_Avoid_: Dashboard, visualization payload, Salesforce response text

**Semantic surface**:
A renderer-neutral product fact that declares the meaning and available
placements of a user-facing result. A renderer may choose a Base UI Kit layout
but cannot change the product data or invent an action.
_Avoid_: Screen, Flutter widget, chart configuration

**Hosting**:
The runtime boundary that composes modules and owns durability, activation,
serialization, routing, and delivery.
_Avoid_: Core, module
