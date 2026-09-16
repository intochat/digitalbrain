Feature: Lineage
  Caller, correlation and causation survive hops.

  Scenario: A signal fired by the reaction a command scheduled carries the command as causation
    Given a running brain
    And "claude" is connected to counter "c" for "Counted"
    When "claude" adds 1 to counter "c" with command id "l1"
    And "claude" waits up to 5 seconds for an incoming "Counted"
    Then the latest "claude" incoming entry causation equals the work id of "l1"
    And "c" commands journal record "l1" causation is empty

  Scenario: A grain call from inside a command is refused
    Given a running brain
    When "claude" invokes the misbehaving call-out on counter "c"
    Then it fails with "commands are local"
