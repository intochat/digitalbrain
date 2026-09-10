Feature: Command
  A typed command is local, synchronous, audited, and deduplicated.

  Scenario: An accepted command schedules work and the reaction saves the snapshot
    Given a running brain
    When "claude" adds 3 to counter "c" with command id "a1"
    Then the add is accepted with a work id
    And "c" commands journal shows "a1" as Attempted then Completed
    And "claude" waits up to 5 seconds until counter "c" total is 3

  Scenario: A resolved command id returns the stored outcome without executing
    Given a running brain
    When "claude" adds 3 to counter "c" with command id "a1"
    And "claude" adds 3 to counter "c" with command id "a1"
    Then counter "c" executed 1 time
    And both adds return the same work id

  Scenario: A command id reused with different arguments is rejected
    Given a running brain
    When "claude" adds 3 to counter "c" with command id "a1"
    And "claude" adds 4 to counter "c" with command id "a1"
    Then the second add fails with "command id reused"
    And "c" commands journal shows one Rejected record

  Scenario: Oversize arguments are rejected before execution
    Given a running brain
    When "claude" adds a 70000-byte note to counter "c" with command id "big"
    Then the add fails with the membrane message
    And counter "c" executed 0 times

  Scenario: Saving a snapshot inside a command is refused
    Given a running brain
    When "claude" invokes the misbehaving save on counter "c"
    Then it fails with "save state from a reaction, not a command"

  Scenario: A crash after Attempted recovers as Unknown and a retry re-executes once
    Given a running brain with file-backed storage
    And counter "c" crashes after recording Attempted
    When "claude" adds 3 to counter "c" with command id "x9" and the call fails
    And the silo restarts
    Then "c" commands journal shows "x9" as Attempted then Unknown
    When "claude" adds 3 to counter "c" with command id "x9"
    Then "c" commands journal shows "x9" incarnation 2 as Attempted then Completed
    And "claude" waits up to 5 seconds until counter "c" total is 3
