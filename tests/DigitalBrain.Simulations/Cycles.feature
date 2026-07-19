Feature: Bounded synapse cycles

Scenario: a neuron that re-emits what it handles stops at the depth limit
    Given a brain for owner "cycles"
    And a Chatter neuron named "loop" is registered
    When Echoed is sent to the Chatter neuron named "loop"
    Then the incoming journal of the Chatter neuron named "loop" settles below 20 synapses
