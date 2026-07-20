# DigitalBrain

DigitalBrain is a framework for durable, typed, self-programmable brains. This glossary fixes the
language shared by the Kernel, modules, behaviors, and orchestration.

## Core

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
A human-approved runtime C# composition of existing typed vocabulary.
_Avoid_: Dynamic neuron type, script-generated contract

**Registry**:
The generated catalog of exact public neuron contracts and their vocabulary.
_Avoid_: Vector database, runtime assembly scan

## AI

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

## Work

**Task**:
A durable identity for a desired outcome and its lifecycle.
_Avoid_: MAF workflow, prompt, ledger entry

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

**Successor Task**:
A new Task linked to an immutable terminal Task to continue or retry its outcome.
_Avoid_: Reopened Task

## Time

**Countdown**:
A durable one-shot schedule expressed as a duration.
_Avoid_: Timer, reminder

**Reminder**:
A durable absolute or recurring schedule.
_Avoid_: Countdown, job queue

**Interval schedule**:
A recurrence based on elapsed duration from an instant.
_Avoid_: Calendar schedule

**Calendar schedule**:
A wall-clock recurrence interpreted in an IANA time zone.
_Avoid_: Interval schedule, cron string

**Occurrence**:
One due instance of a Countdown or Reminder schedule.
_Avoid_: Task, Orleans tick
