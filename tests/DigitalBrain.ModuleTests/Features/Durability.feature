Feature: Multi-silo durability

Scenario: delivery remains durable when a neuron host restarts
    Given a brain for owner "durability"
    When the client sends CrossSilo to the IAlphaProbe neuron named "alpha"
    Then the outgoing journal of the IBetaProbe neuron named "beta" contains CrossSiloArrived
    When the IAlphaProbe neuron named "alpha" restarts
    And the client sends CrossSilo to the IAlphaProbe neuron named "alpha"
    Then the outgoing journal of the IBetaProbe neuron named "beta" contains CrossSiloArrived exactly 2 times
