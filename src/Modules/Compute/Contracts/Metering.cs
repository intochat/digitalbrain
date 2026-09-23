namespace DigitalBrain.Compute;

public static class ComputeUnits
{
    public const decimal PerUsd = 100m;

    public static decimal ToUsd(decimal compute) => compute / PerUsd;
}

public enum MeterSource
{
    ChatClient = 0,
    EmbeddingGenerator = 1,
    CallFilter = 2,
    StorageSampler = 3,
}

// Idempotency key: (IntentId, MeterId, Step). Recurring meters use a synthetic intent id per period.
[GenerateSerializer, Alias("compute.meter-event")]
public sealed record MeterEvent
{
    [Id(0)] public required string IntentId { get; init; }
    [Id(1)] public required string MeterId { get; init; }
    [Id(2)] public required string Step { get; init; }
    [Id(3)] public required string WorkspaceId { get; init; }
    [Id(4)] public required decimal Quantity { get; init; }
    [Id(5)] public required string Unit { get; init; }
    [Id(6)] public required MeterSource Source { get; init; }
    [Id(7)] public string? AppId { get; init; }
    [Id(8)] public string? CostBasis { get; init; }
    [Id(9)] public DateTimeOffset OccurredAt { get; init; }
}

public interface IMeterSink
{
    ValueTask RecordAsync(MeterEvent meterEvent, CancellationToken cancellationToken = default);
}

public interface IPriceBook
{
    string Version { get; }

    decimal PriceInCompute(string meterId, decimal quantity);
}
