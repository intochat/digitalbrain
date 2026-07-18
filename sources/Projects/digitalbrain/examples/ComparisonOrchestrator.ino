neuron DigitalBrain.Custom.ComparisonOrchestrator
  "Aggregates completions from OpenAI GPT and Grok, feeding them into a diff evaluator."

  using gpt      = neuron(DigitalBrain.Ai.LlmNeuron["openai-gpt-5"])
  using grok     = neuron(DigitalBrain.Ai.Grok["xai-grok-beta"])
  using analyzer = neuron(DigitalBrain.Custom.CognitiveDiff)

  using req      = synapse(DigitalBrain.Custom.CompareRequest)
  using done     = synapse(DigitalBrain.Custom.ComparisonCompleted)

  state lastGpt: string
  state lastGrok: string
  state comparisonResult: string

  on req:
    log "Orchestrating comparison for: {req.prompt}"

    let ansGpt = ask gpt to "answer: {req.prompt}"
    let ansGrok = ask grok to "answer: {req.prompt}"

    save ansGpt into lastGpt
    save ansGrok into lastGrok

    let evaluation = ask analyzer to "compare gpt: {ansGpt} with grok: {ansGrok}"
    save evaluation into comparisonResult

    emit done(gptAnswer: ansGpt, grokAnswer: ansGrok, analysis: evaluation)

scenario "successful parallel LLM diff comparison"
  given gpt returns "GPT: 2 + 2 is 4"
  given grok returns "Grok: 4"
  given analyzer returns "GPT was verbose. Grok was highly concise."
  when synapse req(prompt: "2 + 2")
  then synapse done emitted with gptAnswer == "GPT: 2 + 2 is 4"
  and synapse done emitted with grokAnswer == "Grok: 4"
  and synapse done emitted with analysis == "GPT was verbose. Grok was highly concise."
  and lastGpt has "GPT: 2 + 2 is 4"
  and lastGrok has "Grok: 4"
  and comparisonResult has "GPT was verbose. Grok was highly concise."
