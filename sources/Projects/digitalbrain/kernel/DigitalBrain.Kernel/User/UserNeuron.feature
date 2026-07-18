@neuron:kernel/user
@stage:fast
Feature: User neuron originates prompts and persists conversation history

  Scenario: SubmitPromptAsync persists the user message and fires UserPromptReceived
    When the gateway calls SubmitPromptAsync(text="hello", correlation_id=cid)
    Then ConversationGrain("default") has 1 message with role user and text "hello"
    And a UserPromptReceived synapse is fired with caller_neuron_type "UserNeuron" and the same correlation id

  Scenario: GetRecentCorrelationIdsAsync filters by time window
    Given the user "default" has prompts at -2h, -25h, -5d from now
    When GetRecentCorrelationIdsAsync is called with TimeSpan of 24 hours
    Then the result contains exactly 1 correlation id
