Feature: Neuron journals

Scenario: a sent synapse is recorded in the receiving neuron's incoming journal
    Given a brain for owner "journals"
    When Ping is sent to the Echo neuron named "first"
    Then the incoming journal of the Echo neuron named "first" contains Ping
