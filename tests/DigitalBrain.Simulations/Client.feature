Feature: Owner-bound client

Scenario: a client session fires a synapse into its own brain
    Given a brain for owner "clients"
    When the client fires Ping at the Echo neuron named "reached"
    Then the incoming journal of the Echo neuron named "reached" contains Ping

Scenario: a client session cannot reach another owner's neuron
    Given a brain for owner "tenant-x"
    When the client is refused firing Ping at the Echo neuron named "shared" owned by "tenant-y"
    Then the synapse is refused as unauthorized

Scenario: a client reads the journal of a neuron in its own brain
    Given a brain for owner "watchers"
    And a Greeter neuron named "observed" is registered
    Then the client reads the incoming journal of the Greeter neuron named "observed"
