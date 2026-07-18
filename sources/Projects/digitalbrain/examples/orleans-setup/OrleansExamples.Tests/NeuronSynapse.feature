Feature: Neuron Synapse Behaviors and Filesystem Mocking
  As a developer using DigitalBrain
  I want to verify that neurons can receive, handle, and fire synapses
  And that mock filesystem neurons can read and write files with mock data

  Scenario: Neuron receives a synapse successfully
    Given a TestNeuron is initialized
    When it receives a TestSynapse containing "Hello Orleans!"
    Then the TestNeuron should mark the synapse as received
    And the last received content should be "Hello Orleans!"
    And the synapse should be recorded in the incoming journal

  Scenario: Neuron fires a synapse successfully
    Given a TestNeuron is initialized
    When it fires a TestSynapse containing "Outgoing message"
    Then the synapse should be recorded in the outgoing journal

  Scenario: Synapse broadcast routing mode propagates properly
    Given a TestNeuron is initialized
    When it fires a TestSynapse containing "Broadcast message" with broadcast routing mode
    Then the fired synapse should have broadcast routing mode

  Scenario: Mock Windows Filesystem Neuron performs read and write operations
    Given the filesystem is cleared
    And a MockWindowsFileSystemNeuron is initialized
    When a WriteFileSynapse is sent to write "fake content" to "C:\test.txt"
    Then a file response should be received confirming the write operation
    When a ReadFileSynapse is sent to read from "C:\test.txt"
    Then a file response should be received containing "fake content" and confirming success
