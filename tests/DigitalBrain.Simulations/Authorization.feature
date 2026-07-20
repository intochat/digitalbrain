Feature: Owner-bound authorization

Scenario: a neuron rejects a synapse addressed to a different owner's neuron
    Given a brain for owner "tenant-a"
    When Ping is sent to the Echo neuron named "shared" claiming owner "tenant-b"
    Then the synapse is refused as unauthorized
    And the incoming journal of owner "tenant-b"'s Echo neuron named "shared" is empty

Scenario: a neuron cannot subscribe itself into another owner's registry
    Given a brain for owner "tenant-d"
    When the Echo neuron named "listener" subscribes to Ping in owner "tenant-e"'s registry
    Then the synapse is refused as unauthorized

Scenario: a registry refuses to enrol a neuron belonging to another owner
    Given a brain for owner "tenant-f"
    When the Echo neuron named "outsider" owned by "tenant-g" is refused subscription to Ping in this brain's registry
    Then the synapse is refused as unauthorized

Scenario: a neuron accepts a synapse from its own owner
    Given a brain for owner "tenant-c"
    When Ping is sent to the Echo neuron named "shared"
    Then the incoming journal of the Echo neuron named "shared" contains Ping
