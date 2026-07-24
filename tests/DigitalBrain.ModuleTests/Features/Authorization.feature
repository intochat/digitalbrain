Feature: Owner authorization

Scenario: one owner cannot address another owner's neuron
    Given a brain for owner "alice"
    And owner "bob" has the IAnnouncer neuron named "private"
    When owner "alice" sends Announce to owner "bob"'s IAnnouncer neuron named "private"
    Then the request is rejected as unauthorized
