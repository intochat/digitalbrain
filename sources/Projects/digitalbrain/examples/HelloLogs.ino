neuron DigitalBrain.Examples.HelloLogs
  "Logs hello when invoked"
  using ask = synapse(DigitalBrain.Examples.AskHello)
  
  on ask:
    log "Hello has been successfully logged"
scenario "successfully logs hello"
  when synapse ask(prompt: "start")
