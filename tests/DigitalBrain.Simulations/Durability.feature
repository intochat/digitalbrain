Feature: Durability across silo restart

@durability
Scenario: journals survive a silo restart
    Given a brain for owner "restart"
    When Ping is sent to the Greeter neuron named "survivor"
    And the cluster is restarted
    Then the incoming journal of the Greeter neuron named "survivor" contains Ping
    And the outgoing journal of the Greeter neuron named "survivor" contains Pong

@durability
Scenario: a subscription survives a silo restart
    Given a brain for owner "resubscribe"
    And a Listener neuron named "steadfast" is registered
    When the cluster is restarted
    Then the subscriber count for Noticed has grown by 1
