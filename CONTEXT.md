# DigitalBrain

DigitalBrain is a personal assistant whose durable actors, user-authored automations, and typed capabilities cooperate on behalf of an identified owner.

## Runtime language

**Neuron**:
A durable participant that receives and emits typed Signals along Synapses while preserving its own state and observable traffic.
_Avoid_: Agent, service, grain

**Signal**:
A typed message exchanged between Neurons with preserved identity, causation, correlation, and ownership. Crosses a Synapse.
_Avoid_: Event payload, bus message, Synapse (the message sense is retired — see Synapse below)

**Synapse**:
A durable, typed, weighted edge between two Neurons — anatomy, not traffic. Lives in the source Neuron's own state, strengthens on a successful fire, decays by read-time half-life, and is not a journal entry.
_Avoid_: Message, event, a grain of its own

**Traffic Journal**:
A bounded observation window over a Neuron's incoming or outgoing Signals.
_Avoid_: Event store, audit log, execution history, a record of Synapses (the edges are anatomy, not journaled traffic)

**Entity**:
A live addressable resource holding one persisted current typed state snapshot, without transition history or Synapse participation.
_Avoid_: Neuron, event stream, Run history

**Entity Reference**:
A typed owner-scoped identity for an Entity. It identifies current state but is not a snapshot, write endpoint, or authority; governed use requires admitted lineage or a typed grant rule.
_Avoid_: Entity value, capability, mutable handle

## Automation and execution language

**Automation**:
A user-authored automation: a generated C# neuron, compiled against module contracts and admitted to the Execution runtime. There is no plain-English interpreter and no separate runtime — English is how the user asks; a compiled neuron is what they get. Smart Prompts and the internal "Behavior" synonym are retired; see the design spec §9.3, §11, §12.
_Avoid_: Smart Prompt, Behavior, Behavior Revision (all retired product/internal names), recipe, raw script

**Automation Revision**:
An immutable, content-addressed compiled version of an automation neuron's source, including its script artifact, contract lock, and requested Input and Capability grants.
_Avoid_: Smart Prompt / Behavior Revision (retired name), script version, current code

**Execution**:
The durable Run aggregate (Neuron) that realizes chat turns and automation fires on one spine.
_Avoid_: Task, job, session

**ExecutionContext**:
The per-Execution Entity holding operation-specific working memory (schema-shaped ContextDelta slots, not hop DTOs).
_Avoid_: Global memory, chat transcript, Run event store

**ActiveExecutionId**:
The Chat Neuron's switchable pointer to which ExecutionContext assistant tools currently bind.
_Avoid_: Single global context

**ContextDelta**:
A typed merge into ExecutionContext: path, schema hash, payload/ref — MCP-shaped without hand-written hop DTOs.
_Avoid_: CompanyResearch DTO, Dictionary bag without schema

**Workload Revision**:
An immutable executable definition admitted to the generic Execution runtime; an Automation Revision is its primary product form.
_Avoid_: Mutable job definition, latest code

**Trigger**:
A typed fact admitted by an automation's subscription that can request Input admission and then start at most one Run per selected revision-scoped subscription.
_Avoid_: Prompt, callback, event name

**Input Grant**:
Authority for one immutable Workload Revision to receive a declared redacted Trigger view within an owner, principal, source, field/classification, and policy scope. Runtime disclosure still requires a current fenced Input permit.
_Avoid_: Subscription, Capability Grant, permanent data access

**Input Admission**:
A durable policy-fenced decision over one frozen redacted Trigger view and one exact Workload Revision. A permitted admission creates at most one Run; a denial discloses no Input to generated code.
_Avoid_: Trigger match, activation, implicit read

**Run**:
One durable execution of exactly one immutable Workload Revision for exactly one admitted typed input; an automation Run is the primary product case.
_Avoid_: Task, job, session

**Run Event**:
An immutable fact recording a state transition within a Run's authoritative history.
_Avoid_: Log line, notification, status string

**Effect**:
A typed request from a Run to invoke a Capability, paired with a recorded policy decision and outcome.
_Avoid_: Tool call, side effect

**Capability**:
A typed operation that a module makes available to authorized Runs.
_Avoid_: Tool, function, integration

**Capability Grant**:
Authority for a specific immutable Workload Revision to request a constrained Capability.
_Avoid_: Permission flag, tool availability

**Approval**:
A one-time owner decision that resolves one pending Effect without widening its Capability Grant.
_Avoid_: Confirmation prompt, permission

**Projection**:
A rebuildable read model derived from authoritative domain facts for clients and operators.
_Avoid_: State of record, event snapshot

**Learning Evidence**:
A recorded correction, preference, validation result, or Run outcome used to propose a new Automation Revision.
_Avoid_: Self-modification, model training

**Outcome Uncertain**:
A Run condition in which an Effect may have occurred externally but no authoritative result is known.
_Avoid_: Failure, timeout

**Scripting Supervisor**:
One of two separately identified restricted roles: Build accepts and attests artifacts; Run verifies accepted artifacts and brokers leased sandbox execution. Neither possesses Capability authority or provider secrets.
_Avoid_: Kernel script engine, agent, shared worker

**Sandbox Child**:
An ephemeral, one-build or one-Run restricted process/container launched by the matching Scripting Supervisor with only its exact inputs and authenticated channel.
_Avoid_: Long-lived worker, trusted module, Capability endpoint
