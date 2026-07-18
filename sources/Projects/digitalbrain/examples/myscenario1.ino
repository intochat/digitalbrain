neuron DigitalBrain.Examples.MyScenario1
  "Gates external requests using a sole local offline AI neuron when Local AI Mode is enabled."

  using #query = synapse(DigitalBrain.Examples.RequestQuery)
  using !reply = synapse(DigitalBrain.Examples.ResponseQuery)
  using settings = neuron(DigitalBrain.Kernel.Settings.SettingsStore)

  on #query:
    let localAi = ask settings to "get user:local-ai-mode"
    
    if localAi is "true":
      let answer = ask neuron "LocalLlamaNeuron" to "answer: {#query.prompt}"
      emit reply(Answer: answer, Provider: "LocalLlama", Status: "success")
    else:
      let answer = ask neuron "OpenAiNeuron" to "answer: {#query.prompt}"
      emit reply(Answer: answer, Provider: "OpenAiGpt5", Status: "success")

scenario "Local AI Toggle is Enabled"
  given settings returns "true"
  given neuron "LocalLlamaNeuron" returns "Local offline summary."
  when synapse #query(prompt: "summarize my emails")
  then synapse !reply emitted with Answer == "Local offline summary."
  and  synapse !reply emitted with Provider == "LocalLlama"

scenario "Local AI Toggle is Disabled"
  given settings returns "false"
  given neuron "OpenAiNeuron" returns "Cloud intelligence response."
  when synapse #query(prompt: "summarize my emails")
  then synapse !reply emitted with Answer == "Cloud intelligence response."
  and  synapse !reply emitted with Provider == "OpenAiGpt5"
