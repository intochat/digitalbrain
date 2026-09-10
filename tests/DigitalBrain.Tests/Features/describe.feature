Feature: Describe
  An agent reads a neuron's interfaces and calls typed methods through the same table.

  Scenario: Describe lists a module interface with its methods and schemas
    Given a running brain
    When "claude" describes counter "c"
    Then the description lists interface "test.counter" with methods "add" and "total"
    And method "add" args schema requires "id" and "count"
    And method "total" is read-only

  Scenario: Call invokes a typed method with JSON arguments
    Given a running brain
    When "claude" calls "test.counter" "add" on counter "c" with {"id":"c1","count":2}
    Then the call returns a receipt and a work id
    And "claude" waits up to 5 seconds until counter "c" total is 2

  Scenario: Calling an interface the neuron does not implement names the valid ones
    Given a running brain
    When "claude" calls "test.counter" "add" on plain "p" with {"id":"c2","count":1}
    Then the call fails naming "db.v3.neuron"

  Scenario: A read past the retained window reports a gap
    Given a running brain
    When "claude" fires 600 "Tick" signals at plain "p"
    And "claude" reads "p" incoming journal after sequence 0
    Then the read reports a gap and an earliest retained sequence above 1
