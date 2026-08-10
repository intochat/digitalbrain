# DigitalBrain Domain Language

## Workspace

A shared DigitalBrain collaboration scope. A person may belong to a Workspace while retaining private Conversations and Integrations within it.

## Conversation

A durable, ordered exchange between participants. A Conversation owns its messages and lifecycle independently of any user interface or intelligence provider.

## Chat

The user-interface presentation and controls for a Conversation. Chat does not own conversation history or execution state.

## Responder

The single participant selected to produce a response for a Conversation. A Responder may internally coordinate multiple agents.

## Surface

A durable user-created user-interface composition, such as a dashboard. A Surface is personal until explicitly published to a Workspace.

## Connection

A durable relationship between brain parts that expresses topology or routes domain facts. A Connection is neither an external account nor a transient client subscription.

## Integration

A configured relationship with an external system or account, such as Gmail or Salesforce.

## Module

An installable provider of a coherent capability or Integration.

## Behavior

An approved, reusable orchestration of capabilities that responds to a trigger and may produce effects.

## Execution

One durable run of work, such as responding to a Conversation turn or invoking a Behavior.

## Attempt

One try within an Execution. Retrying an Execution creates a new Attempt without changing the Execution's identity.

## Operation

One externally observable effect performed during an Attempt, such as invoking an Integration tool.

## Blocker

The reason an Execution cannot currently proceed, such as required user action, a timer, a dependency, or an uncertain Operation outcome.

## Task

A potential user-visible goal or work item. A Task may be fulfilled by one or more Executions; it is not the durable execution mechanism itself.
