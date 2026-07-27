Feature: DigitalBrain activation opens the operating system

  Activation is a journaled fact, not a host startup step. The operating system reacts
  to that fact and presents its first surface.

  Scenario: activating DigitalBrain opens the home scene
    Given a DigitalBrain for the default owner
    And the shell "desk" is observed
    When the owner activates DigitalBrain
    Then the DigitalBrain neuron journals DigitalBrainActivated for that owner
    And the shell "desk" journals SceneOpened for scene "home" titled "Home"

  Scenario: activation is durably idempotent
    Given a DigitalBrain for the default owner
    And the shell "desk" is observed
    When the owner activates DigitalBrain
    Then the DigitalBrain neuron journals DigitalBrainActivated for that owner
    And the shell "desk" journals SceneOpened for scene "home" titled "Home"
    When the owner activates DigitalBrain again
    Then the DigitalBrain neuron has journaled DigitalBrainActivated exactly 1 time
