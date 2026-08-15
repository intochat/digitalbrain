using DigitalBrain.Abstractions;

namespace DigitalBrain.Execution;

[GenerateSerializer]
[Alias("db.execution.operation-failed")]
public sealed record OperationFailed : Failure
{
    public OperationFailed(string operationKey, string? redactedSummary = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationKey);
        OperationKey = operationKey.Trim();
        RedactedSummary = redactedSummary;
    }

    [Id(0)]
    public string OperationKey { get; init; }

    [Id(1)]
    public string? RedactedSummary { get; init; }
}

