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
            var identity = BehaviorBrokerEndpointCommon.RequireIdentity(
                body.Owner,
                body.TaskType,
                body.TaskOwner,
                body.TaskName,
                body.Attempt);
            var edge = RequireEdge(body.Edge);
            var requestPayload = BehaviorBrokerEndpointCommon.RequireReference(body.RequestPayload);

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
            return BehaviorBrokerEndpointCommon.Failure(MapArgumentReason(exception));
        }
        catch (InvalidOperationException exception)
        {
            return BehaviorBrokerEndpointCommon.Failure(
                IsStableReason(exception.Message) ? exception.Message : "operation-failed");
        }
        catch (NeuronAuthorizationException)
        {
            return BehaviorBrokerEndpointCommon.Failure("unauthorized-operation");
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
            var identity = BehaviorBrokerEndpointCommon.RequireIdentity(
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
            return BehaviorBrokerEndpointCommon.Failure(MapArgumentReason(exception));
        }
        catch (InvalidOperationException exception)
        {
            return BehaviorBrokerEndpointCommon.Failure(
                IsStableReason(exception.Message) ? exception.Message : "operation-failed");
        }
        catch (NeuronAuthorizationException)
        {
            return BehaviorBrokerEndpointCommon.Failure("unauthorized-operation");
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
            var identity = BehaviorBrokerEndpointCommon.RequireIdentity(
                body.Owner,
                body.TaskType,
                body.TaskOwner,
                body.TaskName,
                body.Attempt);

            if (!Enum.IsDefined(typeof(TaskOperationPhase), body.ExpectedPhase)
                || !Enum.IsDefined(typeof(TaskOperationPhase), body.Phase))
            {
                return BehaviorBrokerEndpointCommon.Failure("invalid-phase");
            }

            ProtectedPayloadReference? responsePayload = null;
            if (body.ResponsePayload is not null)
            {
                responsePayload = BehaviorBrokerEndpointCommon.RequireReference(body.ResponsePayload);
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
            return BehaviorBrokerEndpointCommon.Failure(MapArgumentReason(exception));
        }
        catch (InvalidOperationException exception)
        {
            return BehaviorBrokerEndpointCommon.Failure(
                IsStableReason(exception.Message) ? exception.Message : "operation-failed");
        }
        catch (NeuronAuthorizationException)
        {
            return BehaviorBrokerEndpointCommon.Failure("unauthorized-operation");
        }
    }

    private static TaskOperationEdge RequireEdge(EdgeBody? body)
    {
        var fields = BehaviorBrokerEndpointCommon.RequireEdgeFields(body);
        return new TaskOperationEdge(
            fields.Target,
            fields.RequestId,
            fields.RequestVersion,
            fields.ResponseId,
            fields.ResponseVersion);
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
