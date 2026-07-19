Feature: Neuron verbs

Scenario: a reply is recorded in the replying neuron's outgoing journal
    Given a brain for owner "verbs"
    When Ping is sent to the Greeter neuron named "polite"
    Then the outgoing journal of the Greeter neuron named "polite" contains Pong

Scenario: a synapse sent while handling is recorded in the sender's outgoing journal
    Given a brain for owner "relays"
    When Ping is sent to the Relay neuron named "asker"
    Then the outgoing journal of the Relay neuron named "asker" contains Ping

Scenario: a broadcast synapse is recorded in the emitting neuron's outgoing journal
    Given a brain for owner "emitters"
    When Ping is sent to the Announcer neuron named "town-crier"
    Then the outgoing journal of the Announcer neuron named "town-crier" contains Noticed

Scenario: the same synapse delivered twice is handled once
    Given a brain for owner "dedupe"
    When Ping is sent twice to the Greeter neuron named "once"
    Then the incoming journal of the Greeter neuron named "once" contains Ping exactly once
    And the outgoing journal of the Greeter neuron named "once" contains Pong exactly once
