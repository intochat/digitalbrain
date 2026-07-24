Feature: Durable neuron journals

Scenario: a handled synapse remains visible in both directions
    Given a brain for owner "journals"
    When the client sends ProbePing to the IProbeTarget neuron named "durable" with
      | Field | Value    |
      | Value | retained |
    Then the incoming journal of the IProbeTarget neuron named "durable" contains ProbePing
    And the outgoing journal of the IProbeTarget neuron named "durable" contains ProbePong
