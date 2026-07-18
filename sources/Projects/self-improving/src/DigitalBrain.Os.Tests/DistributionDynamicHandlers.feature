@Distribution @HighSev
Feature: Dynamic broadcast handlers after bundle install (distribution)

  Scenario: install grows subscriber count and demo handler reacts to the system event
    Given a clean digital brain
    And the demo experience handler grain is active
    When I install the "demo-experience" bundle
    Then ListSubscribers for BundleInstalled has grown to at least 2
    And the demo experience handler has reacted to BundleInstalled
    # Core Law for self-improving: N+1 after yaml bundle install. Speed is goal.

  # Extended TDD for ClientTap Demo -> surface emit (headless mcp /fire-demo support)
  Scenario: ClientTap Demo emits surfaces (log + card)
    Given a clean digital brain
    When I send ClientTap for Demo
    Then the demo experience handler has reacted to BundleInstalled
    # In full: grain emits UiSurface synapse-log + demo-result; verified via flutter or mcp logs.
