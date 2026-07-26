# Concepts

DigitalBrain is an AI-native operating system built from ready-to-use neurons, synapses, modules,
and executable tests. Its natural-language behavior rail is designed but not implemented; the
shipped `DigitalBrain.Behaviors` SDK is only an authoring and artifact-identity foundation.

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

## TestBrain

`TestBrain` is the method-scoped development testing primitive. A `DigitalBrainFixture` owns one
real three-silo in-process cluster and permits one active `TestBrain` at a time; tests serialize
within that fixture while separate test assemblies may run in parallel. A test advances deterministic
time, controls only closed durability faults and external edges, and asserts on typed committed-journal
evidence. `TestOwner` supplies the method's isolated owner identity, and `TestNeuron<T>` addresses one
typed neuron while exposing only test evidence and closed controls. The retained executable proofs
are the L0/L1/L2 test projects listed under [Specification](/specification) — authored C#, not
feature files.

## Vocabulary

DigitalBrain is a framework for durable, typed, self-programmable brains. This vocabulary fixes the
language shared by the Kernel, modules, behaviors, and orchestration. Each term's `_Avoid_` line
names the words that must not be used for that concept.

### Core

**Neuron**:
A durable, addressable identity that receives requests and facts and owns its operational state.
_Avoid_: Service, actor, grain

**Synapse**:
A typed fact delivered between neurons without a reply contract.
_Avoid_: Command, request, message

**Capability request**:
A directed typed interface method that may return a result.
_Avoid_: Synapse, event

**Module**:
An independently shipped domain vocabulary and its runtime implementation.
_Avoid_: Plugin, feature flag

**Behavior**:
A human-approved runtime C# composition of existing typed vocabulary. The public Behavior SDK
already defines program, constrained-context, manifest, and identity contracts, but it does not
compile, approve, install, or execute a Behavior; that rail remains designed and unbuilt.
_Avoid_: Dynamic neuron type, script-generated contract

**Registry**:
The generated catalog of exact public neuron contracts and their vocabulary.
_Avoid_: Vector database, runtime assembly scan

### AI

**LLM**:
A typed neuron representing inference by one concrete model.
_Avoid_: Agent, model tier, provider route

**Agent**:
A typed neuron whose model, instructions, and capabilities form one conversational role.
_Avoid_: LLM, universal neuron base

**Orchestration**:
A typed neuron that coordinates Agents or LLMs through a declared collaboration pattern.
_Avoid_: Hand-written agent loop

**Group Chat**:
A stateful orchestration neuron in which multiple Participants share one conversation.
_Avoid_: Shared transcript service

**Participant**:
A typed LLM or Agent neuron identified inside an orchestration.
_Avoid_: Constructor dependency, MAF executor

**Executor**:
A private runtime component used to advance a Workflow without semantic identity of its own.
_Avoid_: Neuron, participant identity

**Capability**:
A semantic typed operation that an agent or behavior may use.
_Avoid_: MCP tool, provider function

### Work

**Task**:
A durable identity for a desired outcome and its lifecycle.
_Avoid_: MAF workflow, prompt, ledger entry

**Goal**:
The immutable typed outcome a Task is intended to achieve.
_Avoid_: Prompt, work dictionary

**Attempt**:
One revision-fenced execution of a Task by one Worker.
_Avoid_: Task, retry counter

**Worker**:
A session-owning neuron that can advance an Attempt and report typed outcome facts.
_Avoid_: Stateless Agent, LLM, MAF executor

**Workflow**:
The MAF execution definition used by one Attempt.
_Avoid_: Task

**Blocker**:
A typed reason a Task cannot currently advance.
_Avoid_: AI run status, free-form error string

**Result**:
The typed outcome accepted when a Task succeeds.
_Avoid_: Transcript, arbitrary JSON

**Successor Task**:
A new Task linked to an immutable terminal Task to continue or retry its outcome.
_Avoid_: Reopened Task

### Time

**Countdown**:
A durable one-shot schedule expressed as a duration.
_Avoid_: Timer, reminder

**Reminder**:
A designed, unbuilt durable absolute or recurring schedule. Only Countdown is implemented.
_Avoid_: Countdown, job queue

**Interval schedule**:
A designed, unbuilt recurrence based on elapsed duration from an instant.
_Avoid_: Calendar schedule

**Calendar schedule**:
A designed, unbuilt wall-clock recurrence interpreted in an IANA time zone; its DST records and
library choice remain open.
_Avoid_: Interval schedule, cron string

**Occurrence**:
One due instance of a Countdown, or of the designed Reminder schedule.
_Avoid_: Task, Orleans tick
