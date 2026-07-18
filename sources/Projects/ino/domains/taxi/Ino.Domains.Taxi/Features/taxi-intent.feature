Feature: Taxi — intent routing
  Scenarios that train Cortex to route ride/taxi/uber prompts to the
  Taxi domain's find-ride neuron. Each scenario carries an
  `@neuron:taxi.find-ride` tag so the prompt-pattern corpus picks
  it up alongside the BDD mock LLM reply.

  @neuron:taxi.find-ride
  Scenario: Book a taxi
    Given the user says "book a taxi|find a taxi|hail a taxi"
    Then the assistant replies "Hailing a taxi via the Ride specialist."

  @neuron:taxi.find-ride
  Scenario: Call an uber
    Given the user says "call an? uber|ride uber|need an? uber"
    Then the assistant replies "Calling an Uber via the Ride specialist."

  @neuron:taxi.find-ride
  Scenario: Generic ride request
    Given the user says "get me a ride|need a ride|ride to|ride across"
    Then the assistant replies "Looking for a ride."

  # taxi.ride-home — multi-hop plan. The plan reads the user's Location journal
  # for a "home" anchor + current pickup, then fires FindRideRequest with both
  # endpoints. Cortex dispatches via IOrderRideHomePlan when these patterns
  # match.
  @neuron:taxi.ride-home
  Scenario: Take me home
    Given the user says "take me home|ride home|taxi (back )?home|uber home"
    Then the assistant replies "Taking you home — looking up your home address and current location."
