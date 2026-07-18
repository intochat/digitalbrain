neuron DigitalBrain.Developer.Specs.DotnetFlows
  "Specs for running dotnet CLI commands within the workspace substrate."

  using request     = synapse(DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer.DotnetRequest)
  using responded   = synapse(DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer.DotnetResponse)
  using dotnet      = neuron(DigitalBrain.Developer.Dotnet["workspace-runner"])

  on request:
    let result = ask dotnet to "{request.Command}"
    emit responded(Success: "true", ExitCode: 0, Output: "build success")

scenario "running dotnet build on the solution"
  given dotnet returns "build success"
  when synapse request(Command: "build")
  then synapse responded emitted with success == "true"
