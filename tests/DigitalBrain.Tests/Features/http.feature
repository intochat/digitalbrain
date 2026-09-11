Feature: http
  The silo's HTTP edge is a thin adapter: every endpoint is one typed neuron call or one journal read.

  Scenario: A send reaches the chat and shows in the transcript as the user's turn
    Given a running brain with AI and UI behind HTTP
    And the scripted model will pause before its next answer
    When POST "/chats/desk/send" with {"text":"hello there"}
    Then the response status is 202
    And GET "/chats/desk/transcript" has user text "hello there"

  Scenario: A stream opened on a cursor the journal cannot serve resets with the transcript
    Given a running brain with AI and UI behind HTTP
    And "uichat:desk" is connected to "owner" for "Responded"
    And the scripted model will say "welcome"
    When POST "/chats/desk/send" with {"text":"hello"}
    And "owner" waits up to 10 seconds for an incoming "Responded"
    And the stream "/chats/desk/events?afterSequence=999999" is read up to 10 seconds
    Then the stream reset carries user text "hello"

  Scenario: A live stream carries the agent's answer to the session
    Given a running brain with AI and UI behind HTTP
    And the scripted model will say "welcome"
    When the stream "/chats/desk/events" is opened
    And POST "/chats/desk/send" with {"text":"hello"}
    Then the stream carries a "chat-turn" saying "welcome" within 10 seconds

  Scenario: A new connection restores a completed conversation
    Given a running brain with AI and UI behind HTTP
    And the scripted model will say "welcome back"
    When POST "/chats/desk/send" with {"text":"hello again"}
    And the accepted work on chat "desk" becomes "Completed" within 10 seconds
    And the stream "/chats/desk/events" is read up to 10 seconds
    Then the stream reset carries user text "hello again"

  Scenario: A control activation reaches the surface stream before the next poll
    Given a running brain with AI and UI behind HTTP
    And surface "desk" has opened "home" with button "confirm"
    When the stream "/surfaces/desk/events" is opened
    And POST "/surfaces/desk/controls/confirm/activate" with {"surfaceKey":"home","intent":"do"}
    Then the stream carries a "surface" saying "ControlActivated" within 10 seconds

  Scenario: A running turn is cancelled by its work id
    Given a running brain with AI and UI behind HTTP
    And the scripted model will pause before its next answer
    When POST "/chats/desk/send" with {"text":"keep thinking"}
    And the accepted work on chat "desk" becomes "Running" within 10 seconds
    And POST the cancel of the accepted work on chat "desk"
    Then the response status is 202
    And GET "/chats/desk/turns" reports the accepted work as "Cancelled"

  Scenario: A cancelled turn reaches the stream as cancelled
    Given a running brain with AI and UI behind HTTP
    And "uichat:desk" is connected to "owner" for "TurnFailed"
    And the scripted model will pause before its next answer
    When the stream "/chats/desk/events" is opened
    And POST "/chats/desk/send" with {"text":"keep thinking"}
    And the accepted work on chat "desk" becomes "Running" within 10 seconds
    And POST the cancel of the accepted work on chat "desk"
    Then the stream carries a "chat-turn" with status "Cancelled" within 10 seconds

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
    When GET "/chats/desk/transcript"
    Then the response status is 401
    When GET "/chats/desk/transcript" as "owner" with "secret"
    Then the response status is 200
    And GET "/auth/check" as "owner" with "secret" returns 204
