neuron DigitalBrain.Developer.Specs.CodeReviewerFlows
  "Specs for the multi-LLM debate review of C# diffs."

  using review_req  = synapse(DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer.ReviewCodeRequest)
  using replied     = synapse(DigitalBrain.Developer.Specs.ReviewReplied)
  using reviewer    = neuron(DigitalBrain.Developer.CodeReviewerNeuron["central-reviewer"])

  on review_req:
    let result = ask reviewer to "{review_req.diff}"
    emit replied(approved: "true")

scenario "requesting code review for file diff"
  when synapse review_req(diff: "diff --git a/file b/file")
  then synapse replied emitted with approved == "true"
