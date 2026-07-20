Feature: Durability across silo restart

@durability
Scenario: journals survive the restart of their host silo
    Given a brain for owner "restart"
    When Ping is sent to the Greeter neuron named "survivor"
    And the silo hosting the Greeter neuron named "survivor" is restarted
    Then the incoming journal of the Greeter neuron named "survivor" contains Ping
    And the outgoing journal of the Greeter neuron named "survivor" contains Pong

@durability
Scenario: composition-time broadcast addressing survives a silo restart
    Given a brain for owner "resubscribe"
    When Ping is sent to the Announcer neuron named "town-crier"
    And the silo hosting the Announcer neuron named "town-crier" is restarted
    Then the subscriber count for Noticed is 1
    When Ping is sent to the Announcer neuron named "town-crier"
    Then the Listener for that broadcast's correlation contains Noticed
