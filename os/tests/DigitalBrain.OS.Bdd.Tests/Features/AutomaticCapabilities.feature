Feature: Automatic capability product flows

  Capabilities are discovered from the active catalog and exact validation, not from
  hard-coded assistant tool lists. Reserved vector namespaces reject community writes.
  Provider neurons stay marker interfaces that speak intent synapses.

  Scenario: a greeting stays free of external capability tools
    Given a DigitalBrain for the default owner
    And the conversation "main" is observed
    And the assistant will reply "Hello! How can I help?"
    When the owner sends "hi" to the conversation "main"
    Then the conversation "main" journals the assistant reply "Hello! How can I help?"
    And the assistant selects no external capability

  Scenario: reserved capability namespace rejects community writes
    Given a DigitalBrain for the default owner
    And vector memory "memory" is observed
    When the owner stores into the reserved capability namespace under key "forged.capability"
    Then the store is refused as a reserved namespace

  Scenario: assistant product surface does not hard-code provider tools
    Given the product assistant type
    Then the assistant declares no hard-coded Gmail or Salesforce tool surface

  Scenario: behavior approval remains a scenario-first gate
    Then behavior contracts require scenario evidence before approval
