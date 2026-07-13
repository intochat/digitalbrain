namespace DigitalBrain.Core.Runtime;

public sealed record Page<T>(IReadOnlyList<T> Items, string? NextCursor, bool HasMore);
public sealed record OperationStatus(string OperationId, WorkflowState State, string? SafeReason, DateTimeOffset UpdatedAt);
public sealed record McpError(string Code, string Message, string CorrelationId);
public sealed record Capability(string Id, int Version, bool Enabled, bool RequiresApproval);

public sealed class IdempotencyConflictException()
    : InvalidOperationException("The idempotency key was already used for a different command input.");

public interface IQueryPort
{
    Task<OperationStatus?> GetOperationAsync(RequestContext context, string operationId, CancellationToken cancellationToken = default);
    Task<Page<OperationStatus>> GetOperationsAsync(RequestContext context, string? cursor, int limit, CancellationToken cancellationToken = default);
    Task<Page<Capability>> GetCapabilitiesAsync(RequestContext context, string? cursor, int limit, CancellationToken cancellationToken = default);
}

public interface ICommandPort
{
    Task<OperationStatus> SubmitAsync(RequestContext context, CommandEnvelope command, CancellationToken cancellationToken = default);
}
