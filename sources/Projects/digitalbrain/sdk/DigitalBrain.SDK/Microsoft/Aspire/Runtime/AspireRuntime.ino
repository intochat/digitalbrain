neuron DigitalBrain.SDK.Aspire.Runtime.Specs.StatusFlowsThroughNeuron
  "Pins the contract that `ask $aspire to ...` lands on the SDK neuron target and lifts the reply back."

  using ask     = synapse(DigitalBrain.SDK.Aspire.Runtime.Specs.StatusRequest)
  using aspire  = neuron(SDK.Microsoft.Aspire)
  using replied = synapse(DigitalBrain.SDK.Aspire.Runtime.Specs.StatusReplied)

  on ask:
    let reply = ask aspire to "{ask.prompt}"
    emit replied(status: reply)

scenario "status query flows through the SDK neuron"
  given aspire returns "ok"
  when synapse ask(prompt: "status")
  then synapse replied emitted with status == "ok"
