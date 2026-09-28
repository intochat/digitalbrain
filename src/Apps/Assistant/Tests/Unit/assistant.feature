Feature: Assistant

  Scenario: The assistant composes its chat
    Given the assistant is started
    Then its surface is titled "Assistant"
    And its surface shows a chat and voice input

  Scenario: Typed message
    Given the assistant is started
    When I type "hello"
    Then the chat shows my message "hello"
    And the chat shows the assistant reply "You said: hello"

  Scenario: Voice message
    Given the assistant is started
    When I say "hello there" by voice
    Then the chat shows my message "hello there"
    And the chat shows the assistant reply "You said: hello there"
