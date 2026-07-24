Feature: Client send and broadcast

Scenario: the owner client sends and broadcasts synapses
    Given a brain for owner "client"
    When the client sends Announce to the IAnnouncer neuron named "announcer"
    Then the outgoing journal of the IAnnouncer neuron named "announcer" contains Notice
    When the client broadcasts Notice
    Then the client's outgoing journal contains Notice
