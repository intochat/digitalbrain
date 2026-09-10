Feature: kit
  Named kit neurons keep snapshots and offer cards to connected chats.

  Background:
    Given a running brain with AI and UI

  Scenario: The responder renders a chart that rides out on Responded
    Given "uichat:desk" is connected to "alice" for "Responded"
    And the scripted model will call tool "render_chart" with {"chatName":"uichat:desk","title":"Quarterly sales","chartKind":"line","labels":["Q1"],"values":[42]} then say "here is your chart"
    When session "alice" fires "Instruct" {"tools":["render_chart"]} at "agent:desk"
    And chat "desk" sends "show sales"
    And "alice" waits up to 10 seconds for an incoming "Responded"
    Then the latest "agent:desk" incoming "Ask" text contains "Chat: uichat:desk"
    And the scripted model received a tool result containing "is now showing in the chat as card"
    And the latest "alice" Responded carries a rendered chart titled "Quarterly sales"

  Scenario: A rendered chart is readable and reaches a connected chat as a card
    Given "chart:sales" is connected to "uichat:desk" for "ChartRendered"
    And the scripted model will pause before its next answer
    When chat "desk" sends "show sales"
    And chat "desk" waits up to 10 seconds for its turn to be "Running"
    And chart "sales" renders "Quarterly sales"
    And "uichat:desk" waits up to 10 seconds for an incoming "ChartRendered"
    Then chart "sales" contains "Quarterly sales" with point "Q1" valued 42
    And chat "desk" offers chart "sales" titled "Quarterly sales"

  Scenario: Appending a point twice with the same event id appends it once
    When chart "sales" appends point "Q1" valued 42 with event id "sale-one" twice
    Then chart "sales" contains "Sales" with point "Q1" valued 42

  Scenario: Ensuring a workspace twice keeps one record and refreshes its title
    When workspace "main" is ensured with title "Main"
    And workspace "main" is ensured with title "My workspace"
    Then workspace "main" has one record titled "My workspace" and the same correlation
