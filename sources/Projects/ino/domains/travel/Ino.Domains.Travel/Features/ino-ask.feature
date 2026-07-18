Feature: AskIno — single-method routing boundary
  These scenarios duplicate the canonical routing cases from
  travel-intent.feature but exist as a separate file so a refactor that
  changes how Cortex is invoked can't silently break the gRPC AskIno
  entry while keeping the old chat path green. Loaded as BDD-mock
  corpus by BddScenarioLoader — same step shape as travel-intent.

  The "unrouted" case is verified separately by AskInoRoutingTests
  (no regex pattern can encode "matches nothing in any feature file").

  @neuron:travel.plan-trip
  Scenario: AskIno plans a trip
    Given the user says "plan.*trip.*Bali"
    Then the assistant replies "Planning a trip — fanning out to Flight, Hotel, and Place specialists."

  @neuron:travel.find-flights
  Scenario: AskIno finds flights
    Given the user says "find.*flights?.*Tokyo"
    Then the assistant replies "Searching flights via the FlightSearch neuron."
