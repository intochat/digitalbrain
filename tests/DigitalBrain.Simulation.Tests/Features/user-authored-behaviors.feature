Feature: User-authored behaviors wire the graph
  After the owner brain activates, a user request admits a C# behavior.
  That behavior links an X account's posts to a dashboard chart.

  Scenario: A user request charts Elon posts on the dashboard
    Given a running brain
    And DigitalBrain is activated
    When the user requests a behavior that charts new posts from X account "elon" onto chart "elon-activity"
    And X account "elon" publishes "starship"
    Then chart "elon-activity" has a point labeled "starship"
    And the dashboard includes chart "elon-activity"
