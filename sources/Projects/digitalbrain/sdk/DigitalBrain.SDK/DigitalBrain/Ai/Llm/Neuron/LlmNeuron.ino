neuron DigitalBrain.Ai.LlmNeuron.Specs.AskFlowsThroughChatClient
  "Pins the contract that `ask $gpt to ...` lands on the keyed IChatClient and lifts the reply back."

  using ask     = synapse(DigitalBrain.Ai.LlmNeuron.Specs.AskRequest)
  using gpt     = neuron(DigitalBrain.Ai.LlmNeuron["openai-gpt-5"])
  using replied = synapse(DigitalBrain.Ai.LlmNeuron.Specs.Replied)

  on ask:
    let reply = ask gpt to "{ask.prompt}"
    emit replied(text: reply)

scenario "ask flows through the keyed IChatClient"
  given gpt returns "the LLM said hi"
  when synapse ask(prompt: "hello")
  then synapse replied emitted with text == "the LLM said hi"
