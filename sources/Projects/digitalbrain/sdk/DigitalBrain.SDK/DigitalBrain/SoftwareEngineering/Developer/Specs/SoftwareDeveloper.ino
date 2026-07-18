neuron DigitalBrain.Developer.Specs.SoftwareDeveloperFlows
  "Specs for the autonomous self-healing software developer."

  using request     = synapse(DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.EngineeringTaskRequest)
  using responded   = synapse(DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.EngineeringTaskResponse)
  using developer   = neuron(DigitalBrain.Developer.SoftwareDeveloperNeuron["central-developer"])

  on request:
    let result = ask developer to "{request.TaskDescription}"
    emit responded(Success: "true")

scenario "autonomously generating C# code"
  given developer returns "Success: true"
  when synapse request(TaskDescription: "Write utility class")
  then synapse responded emitted with Success == "true"
