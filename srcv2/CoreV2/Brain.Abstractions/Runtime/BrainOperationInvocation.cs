using Orleans.Concurrency;

namespace Brain.Abstractions.Runtime;

[GenerateSerializer, Immutable]
public sealed record BrainOperationInvocation
{
    public BrainOperationInvocation(
        string operationId,
        string inputJson,
        string workspaceId,
        string principalId,
        string idempotencyKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationId);
        ArgumentException.ThrowIfNullOrWhiteSpace(inputJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(principalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        OperationId = operationId;
        InputJson = inputJson;
        WorkspaceId = workspaceId;
        PrincipalId = principalId;
        IdempotencyKey = idempotencyKey;
    }

    [Id(0)] public string OperationId { get; }
    [Id(1)] public string InputJson { get; }
    [Id(2)] public string WorkspaceId { get; }
    [Id(3)] public string PrincipalId { get; }
    [Id(4)] public string IdempotencyKey { get; }
}
