Feature: Durability across silo restart

@durability
Scenario: journals survive the restart of their host silo
    Given a brain for owner "restart"
    When Ping is sent to the Greeter neuron named "survivor"
    And the silo hosting the Greeter neuron named "survivor" is restarted
    Then the incoming journal of the Greeter neuron named "survivor" contains Ping
    And the outgoing journal of the Greeter neuron named "survivor" contains Pong

@durability
Scenario: a subscription survives the restart of its host silo
    Given a brain for owner "resubscribe"
    And a Listener neuron named "steadfast" is registered
    When the silo hosting the Listener neuron named "steadfast" is restarted
    Then the subscriber count for Noticed has grown by 1
