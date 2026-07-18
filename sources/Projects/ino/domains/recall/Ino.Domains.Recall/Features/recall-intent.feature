Feature: Recall — intent routing
  Scenarios that train Cortex to route recall-shaped prompts to the Recall
  domain's `recall.search` neuron. The plan strips the recall verb and
  fires RecallQuestion to RecallNeuron, which calls into IAW's IMemoryLookup
  against the user's Qdrant collection.

  @neuron:recall.search
  Scenario: Recall via 'what did I tell you about'
    Given the user says "what did i (say|tell you) (about )?.+"
    Then the assistant replies "Looking through what you told me."

  @neuron:recall.search
  Scenario: Recall via 'do you remember'
    Given the user says "do you (remember|recall) .+"
    Then the assistant replies "Looking through what you told me."
