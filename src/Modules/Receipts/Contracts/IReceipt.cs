using DigitalBrain.Contracts;

namespace DigitalBrain.Receipts;

public enum ReceiptOutcome
{
    Succeeded = 0,
    Failed = 1,
    Cancelled = 2,
    AwaitingApproval = 3,
}

[GenerateSerializer, Alias("receipts.touched-data")]
public sealed record TouchedData
{
    [Id(0)] public required string Source { get; init; }
    [Id(1)] public required string SemanticTypeId { get; init; }
    [Id(2)] public bool ReadOnly { get; init; } = true;
    [Id(3)] public long RowsRead { get; init; }
}

[GenerateSerializer, Alias("receipts.app-call")]
public sealed record AppCall
{
    [Id(0)] public required string AppId { get; init; }
    [Id(1)] public required string Operation { get; init; }
    [Id(2)] public bool Discovered { get; init; }
    [Id(3)] public bool Succeeded { get; init; }
}

[GenerateSerializer, Alias("receipts.draft")]
public sealed record ReceiptDraft
{
    [Id(0)] public required string WorkspaceId { get; init; }
    [Id(1)] public required string ConversationId { get; init; }
    [Id(2)] public required ReceiptOutcome Outcome { get; init; }
    [Id(3)] public required string Summary { get; init; }
    [Id(4)] public IReadOnlyList<AppCall> Calls { get; init; } = [];
    [Id(5)] public IReadOnlyList<TouchedData> Touched { get; init; } = [];
    [Id(6)] public int ModelCalls { get; init; }
    [Id(7)] public decimal EstimatedCompute { get; init; }
    [Id(8)] public decimal ApprovedComputeLimit { get; init; }
    [Id(9)] public decimal ActualCompute { get; init; }
    [Id(10)] public bool ShadowPriced { get; init; } = true;
    [Id(11)] public bool FirstTry { get; init; }
    [Id(12)] public int Retries { get; init; }
    [Id(13)] public int Repairs { get; init; }
    [Id(14)] public string? FailureExplanation { get; init; }
}

[GenerateSerializer, Alias("receipts.receipt")]
public sealed record Receipt
{
    [Id(0)] public required string IntentId { get; init; }
    [Id(1)] public required ReceiptDraft Content { get; init; }
    [Id(2)] public required DateTimeOffset WrittenAt { get; init; }
    [Id(3)] public bool Kept { get; init; }
}

// Keyed by intent id: one durable row per intent, outside the workspace blob.
[Alias("receipt")]
[Orleans.Metadata.DefaultGrainType("receipt")]
public interface IReceipt : INeuron
{
    Task Write(ReceiptDraft draft);

    Task<Receipt?> Read();

    Task MarkKept();
}
