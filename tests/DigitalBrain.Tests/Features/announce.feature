Feature: Announce
  A reaction that saves and then tells the graph cannot lose the message or apply twice.

  Scenario: A reaction that saves twice is refused and retried
    Given a running brain
    And reaction failures are observed
    And "claude" is connected from announcing "a" for "Pong"
    And announcing "a" saves twice in its first reaction
    When "claude" fires "Ping" at announcing "a"
    And "claude" waits up to 10 seconds for an incoming "Pong"
    Then reaction "announcing:a" failed with "a reaction saves once, at the end"
    And "a" tally is 1
    And "a" has no stored announcements
    And "claude" incoming journal contains exactly 1 "Pong"

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
    And announcing "a" is connected to plain "announcement-receiver" for "Pong"
    And delivery to "announcement-receiver" fails once
    When "claude" fires "Ping" at announcing "a"
    And "announcement-receiver" waits up to 10 seconds for an incoming "Pong"
    Then "a" tally is 1

  Scenario: A reaction that announces without saving fails and is retried
    Given a running brain
    And "claude" is connected from announcing "a" for "Pong"
    And announcing "a" forgets to save its first reaction
    When "claude" fires "Ping" at announcing "a"
    And "claude" waits up to 10 seconds for an incoming "Pong"
    Then "a" tally is 1
    And "claude" incoming journal contains exactly 1 "Pong"
