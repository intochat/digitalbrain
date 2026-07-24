Feature: Cycle-safe fabric

Scenario: a self-emitting synapse remains bounded
    Given a brain for owner "cycles"
    When the client sends LoopSignal to the ICycleProbe neuron named "cycle"
    Then the outgoing journal of the ICycleProbe neuron named "cycle" contains LoopSignal
    And the outgoing journal of the ICycleProbe neuron at that correlation contains LoopObserved exactly 15 times
