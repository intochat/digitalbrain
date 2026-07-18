@neuron:kernel/task-manager
@stage:fast
@telemetry:counter:taskmanager.ticks
@telemetry:counter:taskmanager.active
@telemetry:histogram:taskmanager.edges_per_task
Feature: TaskManagerNeuron projects an active-tasks RFW card from timeline activity

  Scenario: A single synapse opens a correlation
    Given the task manager has no active correlations
    When Observe is called with a synapse correlationId "aaa..." from "kernel/creator" to "data/file-read"
    Then the next Tick broadcasts a TaskManagerCard with 1 active task
    And the row for "aaa..." has OriginNeuron "kernel/creator" and EdgeCount 1

  Scenario: A correlation ages out after IdleTimeout silence
    Given an active correlation "bbb..." last seen at t0
    When Tick is called at t0 + 9s
    Then a TaskManagerCard is broadcast with 0 active tasks
    And Totals.Completed equals 1

  Scenario: LRU eviction at MaxTracked
    Given 200 active correlations all last seen at t0
    When Observe is called with a new correlation "ccc..." at t0 + 100ms
    Then "ccc..." appears in the next broadcast
    And the oldest correlation is no longer tracked

  Scenario: Ticking with no delta skips the broadcast
    Given the last broadcast payload equals the current projection
    When Tick is called
    Then no TaskManagerCard is broadcast

  Scenario: CancelCorrelation flips a row to cancelling
    Given an active correlation "ddd..."
    When CancelCorrelation arrives for "ddd..."
    Then the row's Status becomes "cancelling"
