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
Scenario: a broadcast reaches subscribers across the cluster
    Given a brain for owner "everywhere"
    And 12 Listener neurons are registered
    When Ping is sent to the Announcer neuron named "town-crier"
    Then every registered Listener received Noticed
