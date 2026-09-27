Feature: Assistant
  Answers a question with its system prompt, using the model chosen in its settings.

  Scenario: The assistant answers with its system prompt
    Given the scripted model "helper" replies:
      """
      Paris is the capital of France.
      """
    And the setting "Model" is the scripted model "helper"
    When I ask "What is the capital of France?"
    Then the answer is "Paris is the capital of France."
    And the scripted model "helper" was told "at most three clear sentences"

  @live
  Scenario: The assistant answers a real question
    When I ask "What is the capital of France?"
    Then the answer mentions "Paris"
