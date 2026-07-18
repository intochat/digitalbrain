neuron Ping.Echo
  "Answers every 'ping' (console input) with a chat response (demo of reactor/broadcast in digitalbraintech new split structure, updated for Ino 2.0 + InoLang/InterpretedNeuron)."

  using ping = synapse(DigitalBrain.Ino.Contracts.ConsoleInput)     # handles -> IHandle<ConsoleInput>
  using pong = synapse(DigitalBrain.Ino.Contracts.ChatResponse)      # broadcasts -> IEmit<ChatResponse>

  broadcasts pong
  handles    ping

  state lastSeen: text

  on ping:
    set lastSeen = ping.text
    emit pong(text: "pong:" + ping.text)                      # broadcast (reactor style)

scenario "a ping is echoed as a broadcast response"
  when  emit ping(text: "alice")
  then  broadcast pong observed with text == "pong:alice"
