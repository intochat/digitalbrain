Feature: Recovery
  A neuron never serves state that storage does not hold.

  Scenario: A read issued while the neuron is recovering is refused
    Given a running brain with faulting storage
    And "claude" is connected to "p" for "Existing"
    And storage fails the next write
    When "claude" connects "claude" to plain "p" for "Note" and a read interleaves during the recovery
    Then the interleaving read of "claude" threw NeuronRecovering
    And the connect failed

  Scenario: A failed write leaves no synapse behind
    Given a running brain with faulting storage
    And storage fails the next write
    When "claude" connects "claude" to plain "p" for "Note" and the call fails
    Then after storage recovers, "claude" synapses do not include "Note" to "p"

  Scenario: Storage cancellation with a live activation is a fault, not a shutdown
    Given a running brain with faulting storage
    And storage cancels the next write
    When "claude" fires "Note" at plain "p" and the call fails
    Then "p" state does not contain "Note"

  Scenario: Reconciliation runs after in-place recovery
    Given a running brain with faulting storage
    And storage fails the write after counter "c" records Attempted
    When "claude" adds 3 to counter "c" with command id "r1" and the call fails
    Then "c" commands journal shows "r1" as Attempted then Unknown
    When "claude" adds 3 to counter "c" with command id "r1"
    Then "c" commands journal shows "r1" incarnation 2 as Attempted then Completed
