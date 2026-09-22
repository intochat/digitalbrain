namespace DigitalBrain.AI.Ollama;

[GenerateSerializer, Alias("ai.ollama-inference-options")]
public sealed record OllamaInferenceOptions(
    [property: Id(0)] int? ContextWindowTokens = null,
    [property: Id(1)] bool? Think = null) : ProviderInferenceOptions;