neuron DigitalBrain.Developer.Specs.GitHubFlows
  "Specs for local Git commands and GitHub CLI Pull Request integrations."

  using status_req = synapse(DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer.GitStatusRequest)
  using replied    = synapse(DigitalBrain.Developer.Specs.GitReplied)
  using github     = neuron(DigitalBrain.Developer.GitHub["LeftTwixWand/digitalbrain"])

  on status_req:
    let result = ask github to "status"
    emit replied(success: "true")

scenario "checking git status from workspace"
  when synapse status_req()
  then synapse replied emitted with success == "true"
