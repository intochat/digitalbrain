Feature: Cancel
  CancelReaction reaches a neuron during a long reaction and is observed cooperatively.

  Scenario: Cancelling the reacting entry stops it at its next token check
    Given a running brain
    And a slow "s" whose reaction waits for release
    When "claude" fires "Work" at slow "s"
    And "claude" cancels the pending work on "s"
    And "claude" waits up to 10 seconds until "s" pending count is 0
    Then "s" reaction observed cancellation

  Scenario: Cancelling an id that is not pending writes nothing
    Given a running brain
    When "claude" cancels signal id "00" on plain "p"
    Then "p" journal total recorded is 0
