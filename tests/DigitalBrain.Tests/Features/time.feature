Feature: Time
  Timers elapse autonomously and can be stopped before they are due.

  Scenario: A scheduled timer elapses without further traffic
    Given a running brain with file-backed storage
    And "timer:t" is connected to "claude" for "TimerElapsed"
    When "claude" schedules timer "t" for 2 seconds with note "Tea is ready"
    And "claude" waits up to 30 seconds for an incoming "TimerElapsed"

  Scenario: A timer stopped before it is due never elapses
    Given a running brain with file-backed storage
    And "timer:t" is connected to "claude" for "TimerElapsed"
    When "claude" schedules timer "t" for 60 seconds with note "Tea is ready"
    Then "claude" waits up to 5 seconds until timer "t" status is Scheduled
    When "claude" stops timer "t"
    Then "claude" waits up to 5 seconds until timer "t" status is Cancelled
    And "claude" incoming journal is empty

  Scenario: Scheduling a timer that is already scheduled is refused
    Given a running brain with file-backed storage
    When "claude" schedules timer "t" for 60 seconds with note "Tea is ready"
    Then "claude" waits up to 5 seconds until timer "t" status is Scheduled
    When "claude" tries to schedule timer "t" for 60 seconds with note "Another tea"
    Then the timer command fails with "already scheduled"
