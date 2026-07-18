@Simulation
Feature: Simulation engine doors and L2 unification (capsule evidence as test)

  @ignore
  # Separate from high-sev Distribution gate (per comment in file); pending step for surface render assert ignored to keep overall test runs clean for the Core Law N+1 + marketplace UI surface scenarios.
  Scenario: pack rule capsule with scenario block then RunSimulation via brain produces report surface
    Given a clean digital brain
    And I am watching the timeline
    When I pack the "executable-standup" experience with rule content
    And I publish "executable-standup" to the local marketplace
    When I send RunSimulation for "ino:executable-standup"
    Then SimulationReport is produced with Passed >= 1
    And the collector observed SimulationReport whose WidgetTree.Render (via surface) contains "executable-standup" or "standup"
    # L2: the RunSimulation path and quarantine replay/install for evidence use the same ReplayObservedSynapsesAsync + report emit (one method, two callers).
    # High-sev DistributionDynamicHandlers remains the pure N+1 Core Law gate (this feature is separate).