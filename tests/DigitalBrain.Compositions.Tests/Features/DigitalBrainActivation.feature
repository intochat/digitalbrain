Feature: DigitalBrain neuron activation opens the OS home screen
  As the AI-native operating system
  When the owner DigitalBrain neuron is activated
  It commits DigitalBrainActivated and the first Behavior opens home via IShell

  Scenario: activating DigitalBrain commits activation and opens home without pull BootOnActivation
    Given a DigitalBrain for the default owner
    And the shell neuron named "desk"
    When the owner activates DigitalBrain
    Then the DigitalBrain neuron outgoing journal contains DigitalBrainActivated for the owner
    And the shell neuron "desk" outgoing journal contains SceneOpened with sceneKey "home" and title "Home"

  Scenario: second activate does not re-emit DigitalBrainActivated
    Given a DigitalBrain for the default owner
    And the shell neuron named "desk"
    When the owner activates DigitalBrain
    Then the DigitalBrain neuron outgoing journal contains DigitalBrainActivated for the owner
    And the shell neuron "desk" outgoing journal contains SceneOpened with sceneKey "home" and title "Home"
    When the owner activates DigitalBrain again
    Then the DigitalBrain neuron outgoing journal has exactly 1 DigitalBrainActivated
