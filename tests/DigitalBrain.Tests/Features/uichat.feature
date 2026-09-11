Feature: uichat
  A user-facing chat keeps turns and context while an agent answers asynchronously.

  Scenario: A settlement lost before its fire is announced exactly once after the retry
    Given a running brain with AI and UI
    And the chat settlement fire is lost once
    And "uichat:desk" is connected to "alice" for "Responded"
    And the scripted model will say "welcome"
    When chat "desk" sends "hello"
    And chat "desk" waits up to 10 seconds for its turn to be "Completed"
    And "alice" waits up to 10 seconds for an incoming "Responded"
    Then "alice" incoming journal has 1 entries
    And chat "desk" transcript has 2 turns

  Scenario: A send appears in the transcript as a running turn
    Given a running brain with AI and UI
    And the scripted model will pause before its next answer
    When chat "desk" sends "hello"
    And chat "desk" waits up to 10 seconds for its turn to be "Running"
    Then chat "desk" transcript contains user text "hello"

  Scenario: An agent reply completes the turn and reaches a connected session
    Given a running brain with AI and UI
    And "uichat:desk" is connected to "alice" for "Responded"
    And the scripted model will say "welcome"
    When chat "desk" sends "hello"
    And "alice" waits up to 10 seconds for an incoming "Responded"
    And chat "desk" waits up to 10 seconds for its turn to be "Completed"
    Then chat "desk" transcript contains assistant text "welcome"
    And the latest "alice" incoming "Responded" text contains "welcome"
    And chat "desk" turn was answered by "agent:desk"

  Scenario: Instruct chooses the responder agent
    Given a running brain with AI and UI
    And the scripted model will say "chosen answer"
    When session "alice" fires "Instruct" {"agent":"agent:chosen"} at "uichat:desk"
    And chat "desk" sends "hello"
    And chat "desk" waits up to 10 seconds for its turn to be "Completed"
    Then chat "desk" turn was answered by "agent:chosen"
    And the latest "agent:chosen" incoming "Ask" text contains "hello"

  Scenario: A kit signal during a turn rides out as a card
    Given a running brain with AI and UI
    And "uichat:desk" is connected to "alice" for "Responded"
    And the scripted model will call tool "fire" with {"type":"ChartRendered","body":"{\"name\":\"sales\",\"title\":\"Quarterly sales\"}","to":"uichat:desk"} then say "here is your chart"
    When chat "desk" sends "show sales"
    And "alice" waits up to 10 seconds for an incoming "Responded"
    Then the latest "alice" Responded offers "chart" card "sales" titled "Quarterly sales"

  Scenario: Cancelling a running turn produces no response
    Given a running brain with AI and UI
    And "uichat:desk" is connected to "alice" for "Responded"
    And "uichat:desk" is connected to "alice" for "TurnFailed"
    And the scripted model will pause before its next answer
    When chat "desk" sends "keep thinking"
    And chat "desk" waits up to 10 seconds for its turn to be "Running"
    And chat "desk" schedules cancellation of its turn
    And chat "desk" waits up to 10 seconds for its turn to be "Cancelled"
    And "alice" waits up to 10 seconds for an incoming "TurnFailed"
    And chat "desk" schedules cancellation of its turn
    Then "alice" received no "Responded"

  Scenario: Context reaches the responder with the last write for a path
    Given a running brain with AI and UI
    And the scripted model will say "understood"
    When chat "desk" sends "use context" with context
      | Path          | SchemaHash | PayloadJson          |
      | customer.name | text.v1    | {"name":"obsolete"}  |
      | customer.name | text.v1    | {"name":"current"}   |
    And "agent:desk" waits up to 10 seconds for an incoming "Ask"
    Then the latest "agent:desk" incoming "Ask" text contains "current"
    And the latest "agent:desk" incoming "Ask" text does not contain "obsolete"

  Scenario: A later turn inherits context from a related turn
    Given a running brain with AI and UI
    And the scripted model will say "understood"
    When chat "desk" sends "remember this" with context
      | Path          | SchemaHash | PayloadJson          |
      | customer.name | text.v1    | {"name":"inherited"} |
    And chat "desk" waits up to 10 seconds for its turn to be "Completed"
    Given the scripted model will say "remembered"
    When chat "desk" sends "use the earlier context" naming the previous turn
    And chat "desk" waits up to 10 seconds for its turn to be "Completed"
    Then chat "desk" turn inherits the previous turn's context digests
    And the latest "agent:desk" incoming "Ask" text contains "inherited"

  Scenario: A session receives a response without connecting by hand
    Given a running brain with AI and UI
    And the scripted model will say "welcome"
    When session "alice" sends "hello" through chat "desk"
    And "alice" waits up to 10 seconds for an incoming "Responded"

  Scenario: A session receives its accepted turn before the response
    Given a running brain with AI and UI
    And the scripted model will say "welcome"
    When session "alice" sends "hello" through chat "desk"
    Then "alice" receives the accepted turn before its response

  Scenario: A chat turn appears in activities
    Given a running brain with AI and UI
    And the scripted model will say "welcome"
    When session "alice" sends "hello" through chat "desk"
    And chat "desk" waits up to 10 seconds for its turn to be "Completed"
    Then activities contain the completed chat turn
