Feature: Admit
  Deliver accepts into a bounded pending queue. Loss is visible as Busy; a repeated signal id is Duplicate.

  Scenario: A full pending queue refuses admission and the emitter sees it
    Given a running brain
    And a slow "s" whose reaction waits for release
    When "claude" fires 257 "Tick" signals at slow "s"
    Then the last fire reports 1 busy target
    And "s" pending count is 256

  Scenario: The same signal id is admitted once
    Given a running brain
    When "claude" delivers signal id "7f" of type "Note" to plain "p" twice
    Then the second delivery is Duplicate
    And "p" incoming journal contains 1 "Note"

  Scenario: Accepted work survives restart and is reacted to without new traffic
    Given a running brain with file-backed storage
    And a throwing "t" whose reaction fails 2 times then succeeds
    When "claude" fires "Ping" at throwing "t"
    And the silo restarts
    And "claude" waits up to 10 seconds for an incoming "Pong"
    And "claude" waits up to 10 seconds until "t" pending count is 0
    Then "t" pending count is 0

  Scenario: Work accepted but never reacted to is resumed after a cold restart
    Given a running brain with file-backed storage
    And a slow "s" whose reaction waits for release
    When "claude" fires "Work" at slow "s"
    And the silo restarts
    And the slow reaction on "s" is released
    And "claude" waits up to 10 seconds for an incoming "Pong"
    And "claude" waits up to 10 seconds until "s" pending count is 0
    Then "s" pending count is 0
