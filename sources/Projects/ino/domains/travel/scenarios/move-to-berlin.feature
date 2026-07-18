Feature: Move to a new city — L1 self-improvement closes a loop on stage
  Scenario 4 of the v0.1 demo strip. The signature L1 demo. ino notices a
  recurring missed intent ("find apartment") that doesn't route to anything,
  the L1 loop drafts a `FindApartmentNeuron`, the user approves it in the
  Inspector, and the new capability materialises live without a silo restart.

  Primary domain: Travel.
  Secondary domains: Location, Recall, Reminders, Genesis (CreatorNeuron).
  Primitive emphasised: closed-loop self-improvement (MissedIntentTracker →
  NeuronOptimizer → CreatorNeuron → RoslynPlan → Discovery dynamic
  registration).

  @neuron:travel.plan-trip
  @tool:travel.find_flights
  @tool:travel.find_hotels
  @tool:reminders.set
  Scenario: Phase 1 — relocation seed and missed intent accumulation
    Given the user says "I'm moving to Berlin in September"
    Then the agent calls travel.find_flights with one_way=true, destination="Berlin"
    And the agent calls travel.find_hotels with destination="Berlin", duration=14
    And the agent calls reminders.set for "visa application", at="2026-06-01"
    And the agent calls reminders.set for "mail forwarding", at="2026-08-15"
    And the agent calls reminders.set for "lease end notice", at="2026-07-01"
    And the agent calls reminders.set for "utility transfers", at="2026-08-25"

    When the user says "find me an apartment in Mitte"
    Then Cortex misses (no neuron for "find apartment")
    And MissedIntentTracker logs prompt_signature="find apartment in $area"

    When the user says "find me a flat near Kreuzberg" three days later
    And the user says "any apartments near a U-Bahn station" five days later
    Then MissedIntentTracker count for prompt_signature reaches the L1 threshold
    And NeuronOptimizer scores it above the materialise threshold

  @neuron:genesis.draft-neuron
  Scenario: Phase 2 — CreatorNeuron drafts FindApartmentNeuron, user approves
    Given MissedIntentTracker has crossed the L1 threshold for "find apartment"

    When CreatorNeuron drafts a FindApartmentNeuron with a Roslyn script body
    Then a Proposal is written to ProposalLog with status=Pending
    And the Inspector → Proposals tab surfaces it
    And the brain visualisation shows a ghost-grey neuron silhouette in the Travel lobe

    When the user approves the proposal
    Then Discovery.RegisterDynamicNeuronAsync registers travel.find-apartment
    And the brain ghost-grey silhouette materialises into a solid neuron with a born-particle burst
    And no silo restart occurs

    When the user says "find me a flat in Prenzlauer Berg"
    Then Cortex routes to travel.find-apartment
    And the new neuron handles the synapse via the Roslyn-compiled body
