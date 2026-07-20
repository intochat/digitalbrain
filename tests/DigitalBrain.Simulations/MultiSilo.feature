Feature: Multi-silo delivery

@multisilo
Scenario: a point-to-point synapse crosses a silo boundary
    Given a brain for owner "crossing"
    And a Relay neuron named "asker" is registered
    And a Greeter neuron named "helper" is registered
    When Ping is sent to the Relay neuron named "asker"
    Then the incoming journal of the Greeter neuron named "helper" contains Ping
    And the incoming journal of the Relay neuron named "asker" contains Pong

@multisilo
Scenario: a synapse crosses between neurons pinned to different silos
    Given a brain for owner "pinned"
    When Ping is sent to the Alpha neuron named "sender"
    Then the incoming journal of the Beta neuron named "partner" contains Ping
    And the Alpha neuron named "sender" and the Beta neuron named "partner" are hosted on different silos

@multisilo
Scenario: a broadcast reaches subscribers across the cluster
    Given a brain for owner "everywhere"
    When Ping is sent to the Announcer neuron named "town-crier"
    Then the Listener for that broadcast's correlation contains Noticed
