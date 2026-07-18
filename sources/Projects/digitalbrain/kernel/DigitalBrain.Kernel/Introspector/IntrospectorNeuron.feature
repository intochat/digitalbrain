@neuron:kernel/introspector
@stage:fast
Feature: Introspector neuron exposes deterministic query primitives

  Scenario: FindNeuronsByFeatureTextAsync returns matching neurons
    Given the catalog has neurons with feature text containing "schedule"
    When FindNeuronsByFeatureTextAsync is called with query "schedule" and limit 10
    Then the result contains refs whose FeatureSnippet includes "schedule"

  Scenario: FindChainsByConversationTextAsync returns distinct correlation ids
    Given the conversation for "default" contains messages matching "calendar"
    When FindChainsByConversationTextAsync is called with text "calendar"
    Then the result contains only distinct correlation ids

  Scenario: TraceCorrelationAsync returns chain ordered by timestamp
    Given a CorrelationChainGrain for a known correlationId has 3 synapses
    When TraceCorrelationAsync is called with that correlationId
    Then the result contains 3 synapses in ascending timestamp order

  Scenario: GetRecentActivityAsync delegates to UserNeuron
    Given UserNeuron "alice" has 2 recent correlations within the last 24 hours
    When GetRecentActivityAsync is called with userId "alice" and since 24h
    Then the result contains exactly 2 correlation ids

  Scenario: FindRootSynapseAsync walks the causation chain to the root
    Given the relay buffer contains a chain: root -> middle -> leaf
    When FindRootSynapseAsync is called with the leaf synapseId
    Then the result is the root synapse

  @stage:fast
  @ignore
  Scenario: ExplainDecisionRequest fires ExplainerRequest; ExplainerResponse fires ExplainDecisionResponse
    Given an Introspector activation
    When IntrospectorNeuron receives ExplainDecisionRequest with query "why did you do X"
    Then an ExplainerRequest is fired at ExplainerNeuron with the same query and correlation id
    Given an ExplainerResponse arrives with text "...<cite>cid</cite>..." and CitedCorrelationIds [cid]
    Then ExplainDecisionResponse is fired with the same text and cited list
