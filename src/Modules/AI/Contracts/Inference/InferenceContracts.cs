using System.Text.Json.Serialization;

namespace DigitalBrain.AI;

[GenerateSerializer, Alias("ai.message")]
public sealed record AiMessage([property: Id(0)] string Role, [property: Id(1)] IReadOnlyList<AiContent> Content);
[GenerateSerializer, Alias("ai.content")]
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$content")]
[JsonDerivedType(typeof(AiText), "text")]
[JsonDerivedType(typeof(AiReasoning), "reasoning")]
[JsonDerivedType(typeof(AiImage), "image")]
[JsonDerivedType(typeof(AiAudio), "audio")]
[JsonDerivedType(typeof(AiToolCall), "tool-call")]
[JsonDerivedType(typeof(AiToolResult), "tool-result")]
public abstract record AiContent;
[GenerateSerializer, Alias("ai.text")]
public sealed record AiText([property: Id(0)] string Text) : AiContent;
[GenerateSerializer, Alias("ai.reasoning")]
public sealed record AiReasoning([property: Id(0)] string Text) : AiContent;
[GenerateSerializer, Alias("ai.image")]
public sealed record AiImage([property: Id(0)] string Uri, [property: Id(1)] string? MediaType = null) : AiContent;
[GenerateSerializer, Alias("ai.audio")]
public sealed record AiAudio([property: Id(0)] string Uri, [property: Id(1)] string? MediaType = null) : AiContent;
[GenerateSerializer, Alias("ai.tool-call")]
public sealed record AiToolCall([property: Id(0)] string CallId, [property: Id(1)] string Name, [property: Id(2)] string ArgumentsJson) : AiContent;
[GenerateSerializer, Alias("ai.tool-result")]
public sealed record AiToolResult([property: Id(0)] string CallId, [property: Id(1)] string ResultJson) : AiContent;
[GenerateSerializer, Alias("ai.tool-definition")]
public sealed record InferenceTool([property: Id(0)] string Name, [property: Id(1)] string Description, [property: Id(2)] string ParametersJson);
[GenerateSerializer, Alias("ai.inference-options")]
public sealed record InferenceOptions(
    [property: Id(0)] float? Temperature = null,
    [property: Id(1)] float? TopP = null,
    [property: Id(2)] int? MaxOutputTokens = null,
    [property: Id(3)] string? Reasoning = null,
    [property: Id(4)] IReadOnlyDictionary<string, string>? ProviderOptions = null,
    [property: Id(5)] string? ResponseSchemaJson = null,
    [property: Id(6)] ProviderInferenceOptions? Provider = null);
[GenerateSerializer, Alias("ai.inference-request")]
public sealed record InferenceRequest(
    [property: Id(0)] IReadOnlyList<AiMessage> Messages,
    [property: Id(1)] AgentModelSelection? Model = null,
    [property: Id(2)] InferenceOptions? Options = null,
    [property: Id(3)] IReadOnlyList<InferenceTool>? Tools = null);
[GenerateSerializer, Alias("ai.inference-usage")]
public sealed record InferenceUsage([property: Id(0)] long? InputTokens, [property: Id(1)] long? OutputTokens, [property: Id(2)] long? TotalTokens);
[GenerateSerializer, Alias("ai.inference-result")]
public sealed record InferenceResult(
    [property: Id(0)] IReadOnlyList<AiMessage> Messages,
    [property: Id(1)] string? FinishReason,
    [property: Id(2)] InferenceUsage? Usage,
    [property: Id(3)] string? ResponseId,
    [property: Id(4)] string Model);
[GenerateSerializer, Alias("ai.inference-update")]
public sealed record InferenceUpdate(
    [property: Id(0)] string? Role,
    [property: Id(1)] IReadOnlyList<AiContent> Content,
    [property: Id(2)] string? FinishReason,
    [property: Id(3)] InferenceUsage? Usage,
    [property: Id(4)] string? ResponseId,
    [property: Id(5)] string? MessageId = null,
    [property: Id(6)] string? Model = null);

public enum CapabilitySupport { Unknown, Supported, Unsupported }
[GenerateSerializer, Alias("ai.model-descriptor")]
public sealed record ModelDescriptor(
    [property: Id(0)] string Provider,
    [property: Id(1)] string Model,
    [property: Id(2)] string? Profile,
    [property: Id(3)] string Revision,
    [property: Id(4)] CapabilitySupport Tools,
    [property: Id(5)] CapabilitySupport Vision,
    [property: Id(6)] CapabilitySupport StructuredOutput,
    [property: Id(7)] long? ContextWindowTokens = null,
    [property: Id(8)] long? MaximumOutputTokens = null,
    [property: Id(9)] CapabilitySupport Temperature = CapabilitySupport.Unknown,
    [property: Id(10)] CapabilitySupport TopP = CapabilitySupport.Unknown,
    [property: Id(11)] IReadOnlyList<string>? AllowedReasoning = null);