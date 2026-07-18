Feature: Travel — intent routing
  These scenarios describe how the ino Cortex neuron classifies travel-related
  chat prompts into typed synapses for the Travel neuron. They double as
  the default training fixtures for the BDD-driven mock IChatClient: the
  `Given` quoted text is a regex the client regex-matches against the inbound
  chat prompt, the `Then` quoted text is the assistant reply surfaced in the
  Flutter Reasoning panel.

  Each Scenario is tagged with `@neuron:<id>` so Cortex's regex
  fast-path can group prompt patterns under the neuron they route to.
  An untagged scenario is a non-routing narrative mock (e.g., reactive
  narration in travel-monitoring.feature).

  @neuron:travel.plan-trip
  Scenario: Plan a trip
    Given the user says "plan.*trip"
    Then the assistant replies "Planning a trip — fanning out to Flight, Hotel, and Place specialists."

  @neuron:travel.find-flights
  Scenario: Find flights
    Given the user says "find.*flight|book.*flight|flights? to"
    Then the assistant replies "Searching flights via the FlightSearch neuron."

  @neuron:travel.find-hotels
  Scenario: Find hotels
    Given the user says "find.*hotel|book.*hotel|hotels? in"
    Then the assistant replies "Searching hotels via the HotelSearch neuron."

  @neuron:travel.find-places
  Scenario: Find places
    Given the user says "things to do|places? to (see|visit)|what to do in"
    Then the assistant replies "Finding highlights via the PlaceSearch neuron."

  @neuron:travel.plan-trip
  Scenario: Plan trip to a known city with dates inline
    Given the user says "plan.*trip.*(Tokyo|Paris|NYC|New York).*(next week|this weekend|next month|tomorrow|today|\d{4}-\d{2}-\d{2})"
    Then the assistant replies "Planning your trip — composing flights, hotels, and places."

  @neuron:travel.plan-trip
  Scenario: Plan trip to a known city without dates
    Given the user says "plan.*trip.*(Tokyo|Paris|NYC|New York)$"
    Then the assistant replies "Planning your trip — when are you going?"

  @neuron:travel.plan-trip
  Scenario: Plan trip without destination
    Given the user says "plan.*trip$"
    Then the assistant replies "Planning a trip — where would you like to go?"
