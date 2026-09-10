Feature: Announce
  A reaction that saves and then tells the graph cannot lose the message or apply twice.

  Scenario: An announcement survives a lost activation between the save and the head persist
    Given a running brain with durable storage
    And "claude" is connected from announcing "a" for "Pong"
    And announcing "a" loses its activation after the next snapshot save
    When "claude" fires "Ping" at announcing "a"
    And "claude" waits up to 10 seconds for an incoming "Pong"
    Then "a" tally is 1
    And "claude" incoming journal contains exactly 1 "Pong"

  Scenario: A busy receiver keeps the announcement until it can accept
    Given a running brain
    And "claude" is connected from announcing "a" for "Pong"
    And session "claude" pending queue is full
    When "claude" fires "Ping" at announcing "a"
    And session "claude" pending queue drains
    And "claude" waits up to 10 seconds for an incoming "Pong"
    Then "a" has no stored announcements

  Scenario: A transient delivery failure is retried, not answered
    Given a running brain
    And announcing "a" is connected to plain "p" for "Pong"
    And delivery to "p" fails once
    When "claude" fires "Ping" at announcing "a"
    And "p" waits up to 10 seconds for an incoming "Pong"
    Then "a" tally is 1

  Scenario: A reaction that announces without saving fails and is retried
    Given a running brain
    And "claude" is connected from announcing "a" for "Pong"
    And announcing "a" forgets to save its first reaction
    When "claude" fires "Ping" at announcing "a"
    And "claude" waits up to 10 seconds for an incoming "Pong"
    Then "a" tally is 1
    And "claude" incoming journal contains exactly 1 "Pong"
