Feature: Group chat
  Luna and Gemma brainstorm your question in turns, each building on what was said,
  and stop as soon as both agree. Luna then writes the answer they converged on.

  Scenario: The models take turns and stop once they agree
    Given the scripted model "luna" replies:
      """
      A smart dog bowl.
      ---
      AGREE: the bowl with a feeding log.
      ---
      A smart dog bowl that logs every meal.
      """
    And the scripted model "gemma" replies:
      """
      Add a feeding log to the bowl.
      ---
      AGREE: bowl plus log.
      """
    And the setting "LunaModel" is the scripted model "luna"
    And the setting "GemmaModel" is the scripted model "gemma"
    When I ask "Name one product idea for dog owners"
    Then "Luna" and "Gemma" take turns, starting with "Luna"
    And the scripted model "gemma" was told "Luna: A smart dog bowl."
    And the discussion ends after 2 rounds
    And the participants agree
    And the answer is "A smart dog bowl that logs every meal."

  Scenario: Without agreement the discussion stops at the round limit
    Given the scripted model "luna" replies:
      """
      A leash.
      ---
      A collar.
      ---
      A leash with a collar.
      """
    And the scripted model "gemma" replies:
      """
      Not a leash.
      ---
      Not a collar.
      """
    And the setting "LunaModel" is the scripted model "luna"
    And the setting "GemmaModel" is the scripted model "gemma"
    And the setting "MaxRounds" is "2"
    When I ask "Pick one accessory"
    Then the discussion ends after 2 rounds
    And the participants do not agree
    And the answer is "A leash with a collar."

  @live
  Scenario: Luna and Gemma converge on a real answer
    When I ask "Name one product idea for dog owners"
    Then "Luna" and "Gemma" take turns, starting with "Luna"
    And the discussion ends within 3 rounds
    And the answer satisfies "It proposes one concrete product for dog owners"
