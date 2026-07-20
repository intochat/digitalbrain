Feature: Owner-bound client

Scenario: a client session fires a synapse into its own brain
    Given a brain for owner "clients"
    When the client fires Ping at the Echo neuron named "reached"
    Then the incoming journal of the Echo neuron named "reached" contains Ping

Scenario: a client session cannot reach another owner's neuron
    Given a brain for owner "tenant-x"
    When the client is refused firing Ping at the Echo neuron named "shared" owned by "tenant-y"
    Then the synapse is refused as unauthorized

Scenario: a client reads its own session journal without deadlocking
    Given a brain for owner "sessions"
    When the client fires Ping at the Echo neuron named "seen"
    Then the client reads the outgoing journal of its own session

Scenario: a session cannot read across owners even though a client may claim any owner
    Given a brain for owner "tenant-p"
    When the session is refused reading the incoming journal of the Echo neuron named "private" owned by "tenant-q"
    Then the synapse is refused as unauthorized

Scenario: an unattributed caller cannot count another owner's subscribers
    Given a brain for owner "tenant-m"
    When a raw cluster client is refused counting Ping subscribers in owner "tenant-n"'s registry
    Then the synapse is refused as unauthorized

Scenario: an unattributed caller cannot read a neuron's journal directly
    Given a brain for owner "tenant-r"
    When a raw cluster client is refused reading the incoming journal of the Echo neuron named "private" owned by "tenant-s"
    Then the synapse is refused as unauthorized

Scenario: a client reads the journal of a neuron in its own brain
    Given a brain for owner "watchers"
    And a Greeter neuron named "observed" is registered
    Then the client reads the incoming journal of the Greeter neuron named "observed"
