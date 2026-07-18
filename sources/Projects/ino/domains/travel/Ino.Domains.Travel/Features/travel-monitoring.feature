Feature: Travel — reactive monitoring
  Monitor scenarios for the Travel neuron. These cover the reactive flows
  where a neuron fires an update after the initial response: FlightMonitor
  reminders, price drops, weather. They train the BDD mock so Cortex can
  narrate what's happening when a delay/change synapse fires.

  These scenarios are NOT user-routed verbs — Cortex's regex fast-path
  ignores untagged-by-neuron scenarios. The `@reactive` tag is purely
  informational; absence of an `@neuron:` tag is what excludes them.

  @reactive @neuron:travel.monitor-flight
  Scenario: Flight delayed
    Given the user says "flight.*delay(ed)?|is my flight late"
    Then the assistant replies "FlightMonitor detected a delay — pushing a reactive update to the card."

  @reactive
  Scenario: Price dropped
    Given the user says "price.*drop|got cheaper"
    Then the assistant replies "PriceMonitor saw a downward regime — re-ranking cheaper flights."
