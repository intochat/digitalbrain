Feature: surface
  Scenes and activity views reach the surface through scheduled reactions.

  Background:
    Given a running brain with AI and UI

  Scenario: Opening a surface stores its scene and newly keyed components
    Given "surface:desk" is connected to "alice" for "SurfaceOpened"
    And "surface:desk" is connected to "alice" for "ComponentAdded"
    When surface "desk" opens "home" titled "Home" with button "go" using command "11111111-1111-1111-1111-111111111111"
    And "alice" waits up to 10 seconds for an incoming "SurfaceOpened"
    And "alice" waits up to 10 seconds for 2 incoming "ComponentAdded"
    Then surface "desk" contains the opened scene and receipt
    And the surface receipt lists components "root, go"
    And "alice" received the added components with stable event ids
    When surface "desk" opens "home" titled "Updated home" with button "next" using command "22222222-2222-2222-2222-222222222222"
    And "alice" waits up to 10 seconds for 2 incoming "SurfaceOpened"
    Then surface "desk" contains the opened scene and receipt
    And the surface receipt lists components "next"

  Scenario: Retrying the same command returns its receipt without reopening
    Given "surface:desk" is connected to "alice" for "SurfaceOpened"
    When surface "desk" opens "home" titled "Home" with button "go" using command "11111111-1111-1111-1111-111111111111"
    And "alice" waits up to 10 seconds for an incoming "SurfaceOpened"
    And surface "desk" opens "home" titled "Home" with button "go" using command "11111111-1111-1111-1111-111111111111"
    Then the repeated surface receipt and work are unchanged
    And "alice" incoming journal has 1 entries

  Scenario: Activating an existing button reaches a connected session
    Given "surface:desk" is connected to "alice" for "SurfaceOpened"
    And "surface:desk" is connected to "alice" for "ControlActivated"
    When surface "desk" opens "home" titled "Home" with button "go" using command "11111111-1111-1111-1111-111111111111"
    And "alice" waits up to 10 seconds for an incoming "SurfaceOpened"
    And surface "desk" activates "go" on "home" with intent "continue"
    And "alice" waits up to 10 seconds for an incoming "ControlActivated"
    Then "alice" incoming journal contains "ControlActivated" {"surfaceKey":"home","controlId":"go","intent":"continue"}

  Scenario: A missing control is refused with guidance
    When surface "desk" activates "missing" on "home" with intent "continue"
    Then the command was refused with a message containing "Open a surface"

  Scenario: An activation whose scene was replaced is refused at the session
    Given "surface:desk" is connected to "alice" for "SurfaceOpened"
    And "surface:desk" is connected to "alice" for "ControlRefused"
    When surface "desk" opens "home" titled "Home" with button "go" using command "11111111-1111-1111-1111-111111111111"
    And "alice" waits up to 10 seconds for an incoming "SurfaceOpened"
    And surface "desk" opens "home" titled "Home" with button "next" using command "22222222-2222-2222-2222-222222222222"
    And "alice" waits up to 10 seconds for 2 incoming "SurfaceOpened"
    And "alice" fires "SurfaceActivating" {"id":{"value":"33333333-3333-3333-3333-333333333333"},"surfaceKey":"home","controlId":"go","intent":"continue"} at "surface:desk"
    Then the fire reached 1 neurons
    When "alice" waits up to 10 seconds for an incoming "ControlRefused"
    Then "alice" incoming journal contains "ControlRefused" {"surfaceKey":"home","controlId":"go","intent":"continue","reason":"The button is no longer on the surface."}

  Scenario: An activity fact updates the activities snapshot and surface
    Given "activities:activities" is connected to "surface:desk" for "ActivityChanged"
    And "activities:activities" is connected to "alice" for "ActivityChanged"
    When an activity execution fact is fired at "activities:activities"
    And "alice" waits up to 10 seconds for an incoming "ActivityChanged"
    Then surface "desk" and activities "activities" show the running activity
