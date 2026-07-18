neuron DigitalBrain.Ai.Specs.GrokFlows
  "Specs for dynamic DPAPI-protected xAI Grok completions."

  using ask         = synapse(DigitalBrain.Ai.LlmNeuron.Specs.AskRequest)
  using replied     = synapse(DigitalBrain.Ai.LlmNeuron.Specs.Replied)
  using grok        = neuron(DigitalBrain.Ai.Grok["xai-grok-beta"])

  on ask:
    let reply = ask grok to "{ask.prompt}"
    emit replied(text: reply)

scenario "ask flows through Grok"
  given grok returns "Grok here, hi!"
  when synapse ask(prompt: "who are you?")
  then synapse replied emitted with text == "Grok here, hi!"
