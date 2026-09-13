Feature: http
  The silo's HTTP edge is a thin adapter: every endpoint is one typed neuron call or one journal read.

  Scenario: A control activation reaches the surface stream before the next poll
    Given a running brain with AI and UI behind HTTP
    And surface "desk" has opened "home" with button "confirm"
    When the stream "/surfaces/desk/events" is opened
    And POST "/surfaces/desk/controls/confirm/activate" with {"surfaceKey":"home","intent":"do"}
    Then the stream carries a "surface" saying "ControlActivated" within 10 seconds

  Scenario: A ui read is not found before the chart exists and found after
    Given a running brain with AI and UI behind HTTP
    When GET "/ui/charts/sales"
    Then the response status is 404
    When chart "sales" renders "Quarterly sales"
    And GET "/ui/charts/sales"
    Then the response status is 200
    And the response body has "title" of "Quarterly sales"

  Scenario: Activating a button that is not on the surface is not found
    Given a running brain with AI and UI behind HTTP
    And surface "desk" has opened "home" with button "confirm"
    When POST "/surfaces/desk/controls/ghost/activate" with {"surfaceKey":"home","intent":"do"}
    Then the response status is 404
    When POST "/surfaces/desk/controls/confirm/activate" with {"surfaceKey":"home","intent":"do"}
    Then the response status is 202

  Scenario: The gate refuses a request that carries no credentials
    Given a running brain with AI and UI behind HTTP requiring "owner" and "secret"
    And chart "sales" renders "Quarterly sales"
    When GET "/ui/charts/sales"
    Then the response status is 401
    When GET "/ui/charts/sales" as "owner" with "secret"
    Then the response status is 200
    And GET "/auth/check" as "owner" with "secret" returns 204
