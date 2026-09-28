namespace DigitalBrain.AI.OpenAI;

[GenerateSerializer, Alias("ai.openai-inference-options")]
public sealed record OpenAIInferenceOptions([property: Id(0)] bool? ParallelToolCalls = null) : ProviderInferenceOptions;