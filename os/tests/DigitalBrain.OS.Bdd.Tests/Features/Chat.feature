Feature: The operating system answers through a behaviour

  A chat message is a journaled fact. An OS behaviour reacts to it, asks the assistant,
  and posts the answer back into the same conversation. No host code is involved.

  Scenario: the assistant answers a message
    Given a DigitalBrain for the default owner
    And the conversation "main" is observed
    And the assistant will reply "Your account is up to date."
    When the owner sends "how is my account?" to the conversation "main"
    Then the conversation "main" journals the user message "how is my account?"
    And the conversation "main" journals the assistant reply "Your account is up to date."

  Scenario: a greeting stays conversational
    Given a DigitalBrain for the default owner
    And the conversation "main" is observed
    And the assistant will reply "Hello! How can I help?"
    When the owner sends "hi" to the conversation "main"
    Then the conversation "main" journals the assistant reply "Hello! How can I help?"
    And the assistant selects no external capability

  Scenario: the conversation remembers earlier turns
    Given a DigitalBrain for the default owner
    And the conversation "main" is observed
    And the assistant will reply "First answer."
    And the assistant will reply "Second answer."
    When the owner sends "first question" to the conversation "main"
    Then the conversation "main" journals the assistant reply "First answer."
    When the owner sends "second question" to the conversation "main"
    Then the conversation "main" journals the assistant reply "Second answer."
    And the conversation "main" transcript has 4 turns
