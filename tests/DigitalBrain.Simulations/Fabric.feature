Feature: Durable synapse fabric

Scenario: a point-to-point synapse reaches the neuron it is addressed to
    Given a brain for owner "delivery"
    When Ping is sent to the Relay neuron named "asker"
    Then the incoming journal of the Greeter neuron named "helper" contains Ping

Scenario: a reply reaches the neuron that asked
    Given a brain for owner "answers"
    When Ping is sent to the Relay neuron named "asker"
    Then the incoming journal of the Relay neuron named "asker" contains Pong

Scenario: a broadcast reaches every neuron subscribed to that synapse
    Given a brain for owner "broadcast"
    And a Listener neuron named "first" is registered
    And a Listener neuron named "second" is registered
    When Ping is sent to the Announcer neuron named "town-crier"
    Then the incoming journal of the Listener neuron named "first" contains Noticed
    And the incoming journal of the Listener neuron named "second" contains Noticed

Scenario: a neuron registered after the first broadcast receives the next one
    Given a brain for owner "late"
    And a Listener neuron named "early" is registered
    When Ping is sent to the Announcer neuron named "town-crier"
    Then the subscriber count for Noticed has grown by 1
    When a Listener neuron named "late-joiner" is registered
    Then the subscriber count for Noticed has grown by 2
    When Ping is sent to the Announcer neuron named "town-crier"
    Then the incoming journal of the Listener neuron named "late-joiner" contains Noticed
