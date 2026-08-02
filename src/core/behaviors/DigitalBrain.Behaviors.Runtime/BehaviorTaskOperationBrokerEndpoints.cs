using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Behaviors.Runtime;

public static class BehaviorTaskOperationBrokerEndpoints
{
    public static IEndpointRouteBuilder MapBehaviorTaskOperationBroker(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/v1/behaviors/broker/operations/prepare", PrepareAsync);
        endpoints.MapPost("/v1/behaviors/broker/operations/read", ReadAsync);
        endpoints.MapPost("/v1/behaviors/broker/operations/transition", TransitionAsync);
        return endpoints;
    }

    private static async Task<IResult> PrepareAsync(
        PrepareOperationRequest body,
        IBehaviorTaskOperationAccess access,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(access);

        try
        {
            var identity = RequireIdentity(
                body.Owner,
                body.TaskType,
                body.TaskOwner,
                body.TaskName,
                body.Attempt);
            var edge = RequireEdge(body.Edge);
            var requestPayload = RequireReference(body.RequestPayload);

            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await access
                .PrepareAsync(
                    identity.Owner,
                    identity.Task,
                    identity.Attempt,
                    body.Sequence,
                    edge,
                    requestPayload,
                    cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(ToWire(snapshot));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Failure(MapArgumentReason(exception));
        }
        catch (InvalidOperationException exception)
        {
            return Failure(IsStableReason(exception.Message) ? exception.Message : "operation-failed");
        }
        catch (NeuronAuthorizationException)
        {
            return Failure("unauthorized-operation");
        }
    }

    private static async Task<IResult> ReadAsync(
        ReadOperationRequest body,
        IBehaviorTaskOperationAccess access,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(access);

        try
        {
            var identity = RequireIdentity(
                body.Owner,
                body.TaskType,
                body.TaskOwner,
                body.TaskName,
                body.Attempt);

            cancellationToken.ThrowIfCancellationRequested();
            var result = await access
                .ReadAsync(
                    identity.Owner,
                    identity.Task,
                    identity.Attempt,
                    body.Sequence,
                    cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(new ReadOperationResponse(
                result.Operation is null ? null : ToWire(result.Operation)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Failure(MapArgumentReason(exception));
        }
        catch (InvalidOperationException exception)
        {
            return Failure(IsStableReason(exception.Message) ? exception.Message : "operation-failed");
        }
        catch (NeuronAuthorizationException)
        {
            return Failure("unauthorized-operation");
        }
    }

    private static async Task<IResult> TransitionAsync(
        TransitionOperationRequest body,
        IBehaviorTaskOperationAccess access,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(access);

        try
        {
            var identity = RequireIdentity(
                body.Owner,
                body.TaskType,
                body.TaskOwner,
                body.TaskName,
                body.Attempt);

            if (!Enum.IsDefined(typeof(TaskOperationPhase), body.ExpectedPhase)
                || !Enum.IsDefined(typeof(TaskOperationPhase), body.Phase))
            {
                return Failure("invalid-phase");
            }

            ProtectedPayloadReference? responsePayload = null;
            if (body.ResponsePayload is not null)
            {
                responsePayload = RequireReference(body.ResponsePayload);
            }

            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = await access
                .TransitionAsync(
                    identity.Owner,
                    identity.Task,
                    identity.Attempt,
                    body.Sequence,
                    (TaskOperationPhase)body.ExpectedPhase,
                    (TaskOperationPhase)body.Phase,
                    responsePayload,
                    body.RedactedSummary,
                    cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(ToWire(snapshot));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Failure(MapArgumentReason(exception));
        }
        catch (InvalidOperationException exception)
        {
            return Failure(IsStableReason(exception.Message) ? exception.Message : "operation-failed");
        }
        catch (NeuronAuthorizationException)
        {
            return Failure("unauthorized-operation");
        }
    }

    private static BoundIdentity RequireIdentity(
        string? ownerValue,
        string? taskType,
        string? taskOwnerValue,
        string? taskName,
        string? attemptValue)
    {
        if (string.IsNullOrWhiteSpace(ownerValue))
        {
            throw new ArgumentException(paramName: null, message: "missing-owner");
        }

        if (string.IsNullOrWhiteSpace(taskOwnerValue))
        {
            throw new ArgumentException(paramName: null, message: "missing-task-owner");
        }

        OwnerId owner;
        OwnerId taskOwner;
        try
        {
            owner = new OwnerId(ownerValue);
            taskOwner = new OwnerId(taskOwnerValue);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException(paramName: null, message: "invalid-request");
        }

        if (owner != taskOwner)
        {
            throw new InvalidOperationException("owner-task-mismatch");
        }

        if (string.IsNullOrWhiteSpace(taskType)
            || string.IsNullOrWhiteSpace(taskName)
            || string.IsNullOrWhiteSpace(attemptValue))
        {
            throw new ArgumentException(paramName: null, message: "missing-task-identity");
        }

        if (!Guid.TryParseExact(attemptValue, "N", out var attemptGuid) || attemptGuid == Guid.Empty)
        {
            throw new ArgumentException(paramName: null, message: "invalid-attempt");
        }

        try
        {
            return new BoundIdentity(
                owner,
                new NeuronId(taskType, taskOwner, taskName),
                new AttemptId(attemptGuid));
        }
        catch (ArgumentException)
        {
            throw new ArgumentException(paramName: null, message: "invalid-request");
        }
    }

    private static TaskOperationEdge RequireEdge(EdgeBody? body)
    {
        if (body is null
            || string.IsNullOrWhiteSpace(body.TargetType)
            || string.IsNullOrWhiteSpace(body.TargetOwner)
            || string.IsNullOrWhiteSpace(body.TargetName)
            || string.IsNullOrWhiteSpace(body.RequestId)
            || string.IsNullOrWhiteSpace(body.ResponseId))
        {
            throw new ArgumentException(paramName: null, message: "invalid-operation-edge");
        }

        if (body.RequestVersion <= 0 || body.ResponseVersion <= 0)
        {
            throw new ArgumentException(paramName: null, message: "invalid-operation-edge");
        }

        try
        {
            var target = new NeuronId(body.TargetType, new OwnerId(body.TargetOwner), body.TargetName);
            return new TaskOperationEdge(
                target,
                body.RequestId,
                body.RequestVersion,
                body.ResponseId,
                body.ResponseVersion);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException(paramName: null, message: "invalid-operation-edge");
        }
    }

    private static ProtectedPayloadReference RequireReference(ProtectedReferenceBody? body)
    {
        if (body is null || string.IsNullOrWhiteSpace(body.Id))
        {
            throw new ArgumentException(paramName: null, message: "invalid-protected-reference");
        }

        if (!Guid.TryParseExact(body.Id, "N", out var id) || id == Guid.Empty)
        {
            throw new ArgumentException(paramName: null, message: "invalid-protected-reference");
        }

        try
        {
            return new ProtectedPayloadReference(id, body.ExpiresAt);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException(paramName: null, message: "invalid-protected-reference");
        }
    }

    private static SnapshotBody ToWire(TaskOperationSnapshot snapshot)
        => new(
            snapshot.Attempt.Value.ToString("N"),
            snapshot.Sequence,
            new EdgeBody
            {
                TargetType = snapshot.Edge.Target.Type,
                TargetOwner = snapshot.Edge.Target.Owner.Value,
                TargetName = snapshot.Edge.Target.Name,
                RequestId = snapshot.Edge.RequestSynapseId,
                RequestVersion = snapshot.Edge.RequestSchemaVersion,
                ResponseId = snapshot.Edge.ResponseSynapseId,
                ResponseVersion = snapshot.Edge.ResponseSchemaVersion,
            },
            new ProtectedReferenceBody
            {
                Id = snapshot.RequestPayload.Id.ToString("N"),
                ExpiresAt = snapshot.RequestPayload.ExpiresAt,
            },
            (int)snapshot.Phase,
            snapshot.ResponsePayload is { } response
                ? new ProtectedReferenceBody
                {
                    Id = response.Id.ToString("N"),
                    ExpiresAt = response.ExpiresAt,
                }
                : null,
            snapshot.RedactedSummary);

    private static string MapArgumentReason(ArgumentException exception)
    {
        if (IsStableReason(exception.Message))
        {
            return exception.Message;
        }

        return "invalid-request";
    }

    private static bool IsStableReason(string? reason)
        => reason is "missing-owner"
            or "missing-task-owner"
            or "owner-task-mismatch"
            or "missing-task-identity"
            or "invalid-task-identity"
            or "invalid-attempt"
            or "invalid-operation-edge"
            or "invalid-protected-reference"
            or "invalid-phase"
            or "invalid-request"
            or "worker-mismatch"
            or "attempt-mismatch"
            or "activation-required"
            or "task-not-started"
            or "unauthorized-operation"
            or "operation-timeout"
            or "operation-failed";

    private static IResult Failure(string reason)
        => Results.Content(reason, "text/plain", statusCode: StatusCodes.Status400BadRequest);

    private sealed record BoundIdentity(OwnerId Owner, NeuronId Task, AttemptId Attempt);

    internal sealed class PrepareOperationRequest
    {
        public string? Owner { get; set; }
        public string? TaskType { get; set; }
        public string? TaskOwner { get; set; }
        public string? TaskName { get; set; }
        public string? Attempt { get; set; }
        public int Sequence { get; set; }
        public EdgeBody? Edge { get; set; }
        public ProtectedReferenceBody? RequestPayload { get; set; }
    }

    internal sealed class ReadOperationRequest
    {
        public string? Owner { get; set; }
        public string? TaskType { get; set; }
        public string? TaskOwner { get; set; }
        public string? TaskName { get; set; }
        public string? Attempt { get; set; }
        public int Sequence { get; set; }
    }

    internal sealed class TransitionOperationRequest
    {
        public string? Owner { get; set; }
        public string? TaskType { get; set; }
        public string? TaskOwner { get; set; }
        public string? TaskName { get; set; }
        public string? Attempt { get; set; }
        public int Sequence { get; set; }
        public int ExpectedPhase { get; set; }
        public int Phase { get; set; }
        public ProtectedReferenceBody? ResponsePayload { get; set; }
        public string? RedactedSummary { get; set; }
    }

    internal sealed class EdgeBody
    {
        public string? TargetType { get; set; }
        public string? TargetOwner { get; set; }
        public string? TargetName { get; set; }
        public string? RequestId { get; set; }
        public int RequestVersion { get; set; }
        public string? ResponseId { get; set; }
        public int ResponseVersion { get; set; }
    }

    internal sealed class ProtectedReferenceBody
    {
        public string? Id { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    internal sealed record SnapshotBody(
        string Attempt,
        int Sequence,
        EdgeBody Edge,
        ProtectedReferenceBody RequestPayload,
        int Phase,
        ProtectedReferenceBody? ResponsePayload,
        string? RedactedSummary);

    internal sealed record ReadOperationResponse(SnapshotBody? Operation);
}
