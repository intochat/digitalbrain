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
    When "claude" adds 4 to counter "c" with command id "a1"
    Then the repeated add fails with the same rejection
    And "c" commands journal shows one Rejected record
    When "claude" adds 3 to counter "c" with command id "a1"
    Then both adds return the same work id
    And counter "c" executed 1 time

  Scenario: Oversize arguments are rejected before execution
    Given a running brain
    When "claude" adds a 70000-byte note to counter "c" with command id "big"
    Then the add fails with the membrane message
    And counter "c" executed 0 times
    When "claude" adds a 70000-byte note to counter "c" with command id "big"
    Then the repeated add fails with the same rejection
    And "c" commands journal shows one Rejected record

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

  Scenario: A full pending queue refuses a command before it is attempted
    Given a running brain
    And counter "c" fails every reaction
    When "claude" fires 256 "Counted" signals at counter "c"
    And "claude" adds 1 to counter "c" with command id "f1"
    Then it fails with "pending signals"
    And "c" commands journal is empty
    And counter "c" executed 0 times

  Scenario: A retry while the first attempt is still unresolved reports an unknown outcome
    Given a running brain
    And counter "c" loses its turn after recording Attempted for command id "u1"
    When "claude" adds 3 to counter "c" with command id "u1" and the call fails
    And "claude" adds 3 to counter "c" with command id "u1"
    Then it fails with "outcome is unknown"

  Scenario: Connect, disconnect, deliver, cancel and drain leave the command journal empty
    Given a running brain
    And "claude" is connected to counter "c" for "Counted"
    And "claude" is disconnected from counter "c" for "Counted"
    When "claude" fires "Counted" {"amount":2} at counter "c"
    And "claude" waits up to 5 seconds until counter "c" total is 2
    And "claude" cancels signal id "00" on counter "c"
    Then "c" commands journal is empty

  Scenario: A read-only module method leaves no journal entry
    Given a running brain
    When "claude" adds 2 to counter "c" with command id "q1"
    And "claude" waits up to 5 seconds until counter "c" total is 2
    And counter "c" journal sizes are recorded
    And counter "c" total is read 5 times
    Then counter "c" journal sizes are unchanged
