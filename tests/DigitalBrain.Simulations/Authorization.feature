Feature: Owner-bound authorization

Scenario: a neuron rejects a synapse addressed to a different owner's neuron
    Given a brain for owner "tenant-a"
    When Ping is sent to the Echo neuron named "shared" claiming owner "tenant-b"
    Then the synapse is refused as unauthorized
    And the incoming journal of the Echo neuron named "shared" is empty

Scenario: a neuron accepts a synapse from its own owner
    Given a brain for owner "tenant-c"
    When Ping is sent to the Echo neuron named "shared"
    Then the incoming journal of the Echo neuron named "shared" contains Ping
