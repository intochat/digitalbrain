Feature: AI model binding

Scenario: a neuron answers using the model tier it declares
    Given a brain for owner "models"
    And the balanced model answers "ping" with "pong from the model"
    When Asked is sent to the Thinker neuron named "sage"
    Then the outgoing journal of the Thinker neuron named "sage" contains Answered
    And the Thinker neuron named "sage" answered "pong from the model"

Scenario: an unscripted prompt fails loudly instead of inventing an answer
    Given a brain for owner "unscripted"
    When Asked is sent to the Thinker neuron named "silent"
    Then the synapse is refused as unscripted
