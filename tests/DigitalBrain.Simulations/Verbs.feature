Feature: Neuron verbs

Scenario: a reply is recorded in the replying neuron's outgoing journal
    Given a brain for owner "verbs"
    When Ping is sent to the Greeter neuron named "polite"
    Then the outgoing journal of the Greeter neuron named "polite" contains Pong

Scenario: a synapse sent while handling is recorded in the sender's outgoing journal
    Given a brain for owner "relays"
    When Ping is sent to the Relay neuron named "asker"
    Then the outgoing journal of the Relay neuron named "asker" contains Ping
