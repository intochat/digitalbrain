@tag("DigitalBrain.Examples.myscenario2")
neuron DigitalBrain.Examples.MyScenario2
  "Demonstrates simplified variable bindings, direct ADK/SDK usage, and neuron tagging."

  # Direct system bindings (no verbose 'using' schemas required)
  @user = Digitalbrain.User
  @documentManager = Digitalbrain.System.DocumentManager

  using #trigger = synapse(DigitalBrain.Examples.StartWorkflow)
  using !processed = synapse(DigitalBrain.Examples.WorkflowCompleted)

  on #trigger:
    # Access nested documents collection using the SDK direct handle
    let resumeName = "john_doe_resume.pdf"
    let resume = @user.documents.get(resumeName)
    
    # Analyze document using local AI semantic search
    let text = ask @documentManager to "extract-text {resume}"
    let score = ask neuron "LocalLlamaNeuron" to "evaluate-fit: {text}"
    
    emit processed(ResumeName: resumeName, Score: score, Status: "qualified")

scenario "Retrieve and process John Doe resume"
  given @user.documents.get("john_doe_resume.pdf") returns "John Doe, software engineer with 5 years experience..."
  given @documentManager returns "John Doe, software engineer with 5 years experience..."
  given neuron "LocalLlamaNeuron" returns "95"
  when synapse #trigger()
  then synapse !processed emitted with Score == "95"
  and  synapse !processed emitted with ResumeName == "john_doe_resume.pdf"
