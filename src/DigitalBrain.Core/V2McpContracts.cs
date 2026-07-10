namespace DigitalBrain.Core.V2;

public sealed record V2Page<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore);
public sealed record V2OperationStatus(string OperationId, WorkflowState State, string? SafeReason, DateTimeOffset UpdatedAt);
public sealed record V2McpError(string Code, string Message, string CorrelationId);
public sealed record V2Capability(string Id, int Version, bool Enabled, bool RequiresApproval);

public interface IV2QueryPort
{
    Task<V2OperationStatus?> GetOperationAsync(RequestContext context, string operationId, CancellationToken cancellationToken = default);
    Task<V2Page<V2OperationStatus>> GetOperationsAsync(RequestContext context, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task<V2Page<V2Capability>> GetCapabilitiesAsync(RequestContext context, string? cursor, int limit, CancellationToken cancellationToken = default);
}

public interface IV2CommandPort
{
    Task<V2OperationStatus> SubmitAsync(RequestContext context, V2CommandEnvelope command, CancellationToken cancellationToken = default);
}
