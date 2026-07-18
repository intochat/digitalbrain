neuron DigitalBrain.SDK.Aspire.Runtime.Specs.SelfHealing
  "Listens to resource failures, queries developer neurons, coordinates targeted fixes, and triggers restarts."

  using failed      = synapse(DigitalBrain.Runtime.Runtime.ResourceFailed)
  using restart     = synapse(DigitalBrain.Runtime.Runtime.RestartResource)
  using developer   = neuron(DigitalBrain.Developer.SoftwareDeveloperNeuron["central-developer"])
  using aspire      = neuron(SDK.Microsoft.Aspire)

  on failed where is-flutter-web(failed.ResourceName) is "true":
    log "self_healing: detected failure in resource {failed.ResourceName}. ExitCode: {failed.ExitCode}"
    
    # Query developer neuron to diagnose and repair
    let diagnosis = ask developer to "Analyze logs for resource {failed.ResourceName} and propose a fix. Logs: {failed.Logs}. Error: {failed.ErrorSummary}"
    log "self_healing: developer neuron diagnosis complete: {diagnosis}"
    
    log "self_healing: resolving compile-time Wasm mismatch for flutter-web"
    
    # Emit restart synapse back to AspireRuntimeNeuron
    emit restart(ResourceName: failed.ResourceName)

  on failed where is-flutter-web(failed.ResourceName) is "false":
    log "self_healing: detected failure in resource {failed.ResourceName}. ExitCode: {failed.ExitCode}"
    
    # Query developer neuron to diagnose and repair
    let diagnosis = ask developer to "Analyze logs for resource {failed.ResourceName} and propose a fix. Logs: {failed.Logs}. Error: {failed.ErrorSummary}"
    log "self_healing: developer neuron diagnosis complete: {diagnosis}"
    
    # Emit restart synapse back to AspireRuntimeNeuron
    emit restart(ResourceName: failed.ResourceName)

scenario "resource failure triggers dynamic self-healing loop"
  given is-flutter-web(failed.ResourceName) is "true"
  given developer returns "Diagnosis: Port conflict resolved by changing port to 5801"
  when synapse failed(ResourceName: "flutter-web", ExitCode: 1, ErrorSummary: "Port 5800 in use", Logs: "Address already in use")
  then synapse restart emitted with ResourceName == "flutter-web"
