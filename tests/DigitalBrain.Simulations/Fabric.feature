Feature: Durable synapse fabric

Scenario: a capability request is durable at both neurons before the target runs
    Given a brain for owner "reify"
    When Ping is sent to the CapabilityCaller neuron named "caller"
    Then the outgoing journal of the CapabilityCaller neuron named "caller" contains CapabilityRequested
    And the incoming journal of the Echo neuron named "probe" contains CapabilityRequested
    And the outgoing journal of the Echo neuron named "probe" contains CapabilityObserved
    And the outgoing journal of the CapabilityCaller neuron named "caller" contains CapabilityCompleted

Scenario: a failed capability request records a failed outcome
    Given a brain for owner "failed-capability"
    When FailCapability is sent to the FailingCapabilityCaller neuron named "caller"
    Then the outgoing journal of the FailingCapabilityCaller neuron named "caller" contains CapabilityFailed

Scenario: an owner-rejected capability request records a rejected outcome
    Given a brain for owner "rejected-capability"
    When RejectCapability is sent to the RejectedCapabilityCaller neuron named "caller"
    Then the outgoing journal of the RejectedCapabilityCaller neuron named "caller" contains CapabilityRejected

Scenario: a point-to-point synapse reaches the neuron it is addressed to
    Given a brain for owner "delivery"
    When Ping is sent to the Relay neuron named "asker"
    Then the incoming journal of the Greeter neuron named "helper" contains Ping

Scenario: a reply reaches the neuron that asked
    Given a brain for owner "answers"
    When Ping is sent to the Relay neuron named "asker"
    Then the incoming journal of the Relay neuron named "asker" contains Pong

Scenario: a broadcast reaches every handler type for that synapse
    Given a brain for owner "broadcast"
    When Ping is sent to the Announcer neuron named "town-crier"
    Then the Listener for that broadcast's correlation contains Noticed

Scenario: an unreachable receiver does not block traffic to reachable ones
    Given a brain for owner "head-of-line"
    When Ping is sent to the Splitter neuron named "fan-out"
    Then the incoming journal of the Echo neuron named "reachable" contains Ping

Scenario: outbox redelivers after a receiver outage
    Given a brain for owner "redeliver"
    When Ping is sent to the OutageRelay neuron named "relay"
    Then the incoming journal of the RecoveringReceiver neuron named "target" contains Ping

Scenario: a neuron that has never activated still receives a broadcast
    Given a brain for owner "never-activated"
    When Ping is sent to the Announcer neuron named "town-crier"
    Then the Listener for that broadcast's correlation contains Noticed

Scenario: broadcast fan-out is fixed by composition, not by activation
    Given a brain for owner "late"
    When Ping is sent to the Announcer neuron named "town-crier"
    Then the subscriber count for Noticed is 1
    And the Listener for that broadcast's correlation contains Noticed
    When a Listener neuron named "late-joiner" is registered
    Then the subscriber count for Noticed is 1
