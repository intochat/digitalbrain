name: llm-agent
version: 1.0.0
desc: Ino assistant (LlmAgentNeuron)
triggers: AgentRequest
emits: AgentResponse,UiSurface,ImprovementProposal
region: main
observed-synapses: 0

on: AgentRequest
  show card( "LLM $prompt", column( text( "Thinking..." ), button( "Run", AgentRequest( prompt: "$prompt" ) ) ) )
