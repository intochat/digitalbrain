neuron DigitalBrain.Ino
  "The premium personal assistant orchestrator managing chat streams and RFW visually-locked environments."

  using chatRequest  = synapse(DigitalBrain.SDK.INO.InoChatRequest)
  using chatResponse = synapse(DigitalBrain.SDK.INO.InoChatResponse)
  using Ai           = neuron(DigitalBrain.Ai.LlmNeuron)

  state chatHistory: list
  state activeResponse: string

  on chatRequest:
    log "Ino: received user prompt: {chatRequest.UserMessage}"
    
    # Dot-notation call syntax invoking Ai.Chat
    let response = Ai.Chat(chatRequest.UserMessage)
    
    # Save the response into activeResponse state
    save response into activeResponse
    
    # Emit final response synapse
    emit chatResponse(AssistantReply: response)

  ui:
    UiKit.Column:
      UiKit.Card(title: "Ino Assistant", body: activeResponse)
      UiKit.Input(label: "Type a prompt...", action: chatRequest)

scenario "assistant receives a chat request and replies"
  given Ai returns "Hello from AI"
  when synapse chatRequest(UserMessage: "hello")
  then synapse chatResponse emitted with AssistantReply == "Hello from AI"
  and activeResponse has "Hello from AI"
