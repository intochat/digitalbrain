Feature: Plan Trip → Day-of Travel (canonical complex neuron)
  Scenario 1 of the v0.1 demo strip. Combines Travel + Reminders + Taxi +
  Recall + Location across hours of wall-clock time. Anchors the rest of the
  demo strip — proves anticipation + dependencies + temporal hooks.

  Primary domain: Travel.
  Secondary domains: Reminders, Taxi, Recall, Location.
  Primitive emphasised: anticipation + temporal hooks (`Reminders.set` from
  inside Plan Trip's tool surface; reactive `FlightDelayed` synapse from
  `FlightMonitor`; location-triggered nudge).

  Tags drive both Cortex routing fixtures and the planned
  `BddToolFixtureMiddleware`. `@neuron:` is the routing target.
  `@tool:` is a tool call the LlmNeuron rewrite will make against the
  scenario's data.

  @neuron:travel.plan-trip
  @tool:weather.get_climatology
  @tool:travel.find_flights
  @tool:travel.find_hotels
  @tool:events.find_events
  @tool:travel.find_places
  @tool:reminders.set
  Scenario: Plan a trip to Bali next month and bind day-of travel hooks
    Given the user says "plan a trip to Bali next month"
    And recall remembers "home airport=SFO"
    And recall remembers "diet=vegetarian"

    When the agent calls weather.get_climatology with destination="Bali", month="June"
    Then the bubble emits a WeatherSummaryCard with band="warm-humid"
    And the orb persona switches to "thinking"

    When the user selects a flight
    Then the agent calls travel.find_hotels with destination="Bali", checkin, checkout
    And the bubble emits a HotelCard list with at least 3 options

    When the user selects a hotel
    Then the agent calls events.find_events with destination="Bali", dateRange
    And the bubble emits an EventCard list with a Skip affordance

    When the user picks an event
    Then the agent calls travel.find_places with weather-aware ranking
    And the bubble emits an ActivityCard list with weather-badge swap rule applied

    When the user picks an activity
    Then the agent emits a TripSummaryCard consolidating every selection
    And the agent calls reminders.set with text="check in for outbound flight", at="T-24h"
    And the agent calls reminders.set with text="leave for SFO", trigger="location:home AND time:T-2h"

  @neuron:travel.day-of-travel
  Scenario: Day-of travel notification chain (reactive, no Cortex roundtrip)
    Given the user has an active trip to Bali
    And the outbound flight is in 3 hours

    When FlightMonitor fires FlightDelayed
    Then the orb badges with a soft notification
    And the agent re-computes the leave-for-airport reminder
    And the Taxi binding shifts pickup time by the delay delta

    When the user enters geofence "home_exit"
    Then the agent emits a "leave now" RFW card with one-tap taxi confirmation
