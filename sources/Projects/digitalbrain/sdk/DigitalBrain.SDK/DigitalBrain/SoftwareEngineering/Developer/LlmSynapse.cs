using DigitalBrain.Runtime.Neurons;

namespace DigitalBrain.SDK.DigitalBrain.SoftwareEngineering.Developer;

/// <summary>
/// A generic, high-performance synapse for invoking reasoning prompt completions.
/// Uses SynapseMetadata directly to avoid constructor sprawl.
/// </summary>
[GenerateSerializer]
public sealed record LlmSynapse([property: Id(1)] string SystemPrompt,
    [property: Id(2)] string UserPrompt,
    [property: Id(3)] string Provider,             // "openai", "anthropic", "grok", "ollama"
    [property: Id(4)] string ModelName,            // e.g. "gpt-4o", "claude-3-5-sonnet", etc.
    [property: Id(5)] float? Temperature = null,
    [property: Id(6)] int? MaxTokens = null,
    [property: Id(7)] int? MaxReasoningTokens = null // Support reasoning models (o1, o3, gemini-2-thinking)
) : Synapse;

/// <summary>
/// A structured response for reasoning completions.
/// </summary>
[GenerateSerializer]
public sealed record LlmResponseSynapse([property: Id(1)] bool Success,
    [property: Id(2)] string ResponseText,
    [property: Id(3)] string? ReasoningText = null, // CoT / Thinking details
    [property: Id(4)] string? ErrorMessage = null,
    [property: Id(5)] string? FinishReason = null,
    [property: Id(6)] long? InputTokens = null,
    [property: Id(7)] long? OutputTokens = null
) : Synapse;
