using DigitalBrain.Contracts;

namespace DigitalBrain.AI.Metering;

/// <summary>Which kind of provider call produced the tokens.</summary>
public enum MeterKind { Chat, Embedding }

/// <summary>
/// One provider call's reported usage. Every token class is nullable: null means the provider
/// did not report that class, never zero. <see cref="UsageReported"/> is false when the
/// provider returned no usage at all, so an unreported call is distinguishable from a free one.
/// </summary>
[GenerateSerializer, Alias("ai.token-usage-entry")]
public sealed record TokenUsageEntry(
    [property: Id(0)] MeterKind Meter,
    [property: Id(1)] string Provider,
    [property: Id(2)] string Model,
    [property: Id(3)] long? InputTokens,
    [property: Id(4)] long? CachedInputTokens,
    [property: Id(5)] long? ReasoningTokens,
    [property: Id(6)] long? OutputTokens,
    [property: Id(7)] long? TotalTokens,
    [property: Id(8)] bool UsageReported,
    [property: Id(9)] DateTimeOffset At) : IIntentUsageEntry;

/// <summary>The durable token usage recorded for one intent id.</summary>
[GenerateSerializer, Alias("ai.intent-token-usage")]
public sealed record IntentTokenUsage(
    [property: Id(0)] string IntentId,
    [property: Id(1)] TokenUsageEntry[] Entries);

/// <summary>
/// The durable per-intent usage row. Keyed by intent id and owned by the AI module; P1.4
/// receipts and the P2.2 Compute meter consume it rather than deriving usage from sampled traces.
/// </summary>
[Alias("ai.intent-usage")]
[Orleans.Metadata.DefaultGrainType("ai-intent-usage")]
public interface IIntentUsage : INeuron
{
    Task RecordAsync(TokenUsageEntry entry, CancellationToken cancellationToken = default);
    Task RecordBatchAsync(TokenUsageEntry[] entries, CancellationToken cancellationToken = default);
    Task<IntentTokenUsage> ReadAsync(CancellationToken cancellationToken = default);
}