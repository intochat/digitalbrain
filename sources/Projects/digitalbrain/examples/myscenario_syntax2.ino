# Tagging metadata context for linking
@tag("DigitalBrain.Examples.Syntax2")

# Inheriting from standard contract models
neuron DigitalBrain.Examples.MySyntax2 inherits Digitalbrain.Core.Neuron
  "Ino Syntax 2.0 demonstration showing simplified SDK references and standard library calls."

  # Variables prefix with @ bind directly to domain neuron instances (Unified Reference System)
  @user = Digitalbrain.User
  @localLlama = Digitalbrain.Ai.LocalLlama
  @openaiGpt = child DB.OpenAI.ChatGPT.Ask (synapse contract)

  # Handlers map via standard contract triggers
  on synapse(DigitalBrain.Examples.ExecuteAsk):
    # Variables and payload are in direct, natural English-like syntax
    let inputPrompt = @synapse.prompt
    
    # Standard library call instead of specialized language keywords
    if @user.settings.localAiMode is "true":
      let response = ask @localLlama to "analyze: {inputPrompt}"
      Kernel.Emit(signal: DigitalBrain.Examples.AskResult, payload: {
        "Answer": response,
        "Provider": "LocalLlama"
      })
    else:
      let response = ask @openaiGpt to "evaluate: {inputPrompt}"
      Kernel.Emit(signal: DigitalBrain.Examples.AskResult, payload: {
        "Answer": response,
        "Provider": "CloudGpt"
      })

# Self-testing scenario assertions inside the same neuron file
scenario "Offline local AI execution"
  given @user.settings.localAiMode returns "true"
  given @localLlama returns "Direct offline local AI result"
  when synapse ExecuteAsk(prompt: "validate system schema")
  then synapse AskResult emitted with Answer == "Direct offline local AI result"
  and  synapse AskResult emitted with Provider == "LocalLlama"
