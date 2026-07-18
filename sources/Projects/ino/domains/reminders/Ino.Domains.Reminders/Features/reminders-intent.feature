Feature: Reminders — intent routing
  Scenarios that train Cortex to route reminder-shaped prompts to the
  Reminders domain. Each scenario carries an `@neuron:reminders.<verb>`
  tag so the prompt-pattern corpus picks it up alongside the BDD mock LLM
  reply.

  @neuron:reminders.set
  Scenario: Set a reminder by description and delay
    Given the user says "remind me to .+ in \d+ ?(m|min|mins|minute|minutes|h|hr|hrs|hour|hours|s|sec|secs|second|seconds)"
    Then the assistant replies "Scheduling your reminder."

  @neuron:reminders.set
  Scenario: Set a reminder via 'set a reminder' phrasing
    Given the user says "set a reminder to .+ in \d+ ?(m|min|mins|minute|minutes|h|hr|hrs|hour|hours)"
    Then the assistant replies "Scheduling your reminder."

  @neuron:reminders.cancel
  Scenario: Cancel a reminder by description
    Given the user says "cancel (my )?reminder|never mind on the .+ reminder|forget the .+ reminder"
    Then the assistant replies "Cancelling that reminder."
