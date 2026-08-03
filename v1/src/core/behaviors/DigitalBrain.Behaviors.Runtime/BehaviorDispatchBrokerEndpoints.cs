using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Behaviors.Runtime;

public static class BehaviorDispatchBrokerEndpoints
{
    public static IEndpointRouteBuilder MapBehaviorDispatchBroker(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapPost("/v1/behaviors/broker/dispatch", DispatchAsync);
        endpoints.MapPost("/v1/behaviors/broker/emit", EmitFactAsync);
        return endpoints;
    }

    private static async Task<IResult> DispatchAsync(
        DispatchRequest body,
        IBehaviorCapabilityDispatchAccess access,
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
            var response = await access
                .DispatchAsync(
                    identity.Owner,
                    identity.Task,
                    identity.Attempt,
                    edge,
                    requestPayload,
                    cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(new ProtectedReferenceResponse(
                response.Id.ToString("N"),
                response.ExpiresAt));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (BehaviorUserActionRequiredException userAction)
            when (userAction.Requirement is { } requirement)
        {
            return Results.Json(
                new UserActionRequiredResponse(
                    BehaviorExecutionCodes.UserActionRequired,
                    requirement.Task.Type,
                    requirement.Task.Owner.Value,
                    requirement.Task.Name,
                    requirement.Attempt.Value.ToString("N"),
                    requirement.Module.Type,
                    requirement.Module.Owner.Value,
                    requirement.Module.Name,
                    requirement.ModuleId,
                    requirement.DisplayText,
                    requirement.ActionReference.Id.ToString("N"),
                    requirement.ActionReference.ExpiresAt,
                    requirement.ActionEpoch.ToString("N"),
                    requirement.ParkRevision,
                    requirement.ExpiresAt,
                    requirement.Completer.Type,
                    requirement.Completer.Owner.Value,
                    requirement.Completer.Name),
                statusCode: StatusCodes.Status409Conflict);
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

    private static async Task<IResult> EmitFactAsync(
        EmitFactRequest body,
        IBehaviorCapabilityDispatchAccess access,
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

            if (string.IsNullOrWhiteSpace(body.Behavior)
                || string.IsNullOrWhiteSpace(body.EmitAlias)
                || string.IsNullOrWhiteSpace(body.FactJson))
            {
                throw new ArgumentException(paramName: null, message: "invalid-request");
            }

            var outcome = await access
                .EmitFactAsync(
                    identity.Owner,
                    identity.Task,
                    identity.Attempt,
                    new BehaviorId(body.Behavior),
                    body.EmitAlias,
                    body.FactJson,
                    body.Hops,
                    cancellationToken)
                .ConfigureAwait(false);

            return string.Equals(outcome, BehaviorFactEmission.Emitted, StringComparison.Ordinal)
                ? Results.Ok(new EmitFactResponse(outcome))
                : BehaviorBrokerEndpointCommon.Failure(outcome);
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
        catch (NeuronAuthorizationException exception)
        {
            return BehaviorBrokerEndpointCommon.Failure(
                IsStableReason(exception.Message) ? exception.Message : "unauthorized-operation");
        }
    }

    private static BehaviorCapabilityEdge RequireEdge(EdgeBody? body)
    {
        var fields = BehaviorBrokerEndpointCommon.RequireEdgeFields(body);
        return new BehaviorCapabilityEdge(
            fields.Target,
            fields.RequestId,
            fields.RequestVersion,
            fields.ResponseId,
            fields.ResponseVersion);
    }

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
            or "invalid-request"
            or "worker-mismatch"
            or "attempt-mismatch"
            or "activation-required"
            or "task-not-started"
            or "unauthorized-operation"
            or "operation-timeout"
            or "operation-failed"
            or "unknown-target-neuron"
            or "unknown-request-synapse"
            or "incompatible-request-version"
            or "unknown-response-synapse"
            or "incompatible-response-version"
            or "unknown-target-neuron-type"
            or "unknown-request-type"
            or "unknown-response-type"
            or "request-response-type-mismatch"
            or "method-shaped-edge"
            or "foreign-target-owner"
            or "invalid-payload-content"
            or "invalid-request-payload"
            or "empty-response-payload"
            or "contract-type-map-unavailable"
            or "behavior-activation-mismatch"
            or BehaviorFactEmission.UndeclaredAlias
            or BehaviorFactEmission.NotRunning
            or BehaviorFactEmission.UnknownSynapse
            or BehaviorFactEmission.HopBudgetExhausted;

    internal sealed class DispatchRequest
    {
        public string? Owner { get; set; }
        public string? TaskType { get; set; }
        public string? TaskOwner { get; set; }
        public string? TaskName { get; set; }
        public string? Attempt { get; set; }
        public EdgeBody? Edge { get; set; }
        public ProtectedReferenceBody? RequestPayload { get; set; }
    }

    internal sealed class EmitFactRequest
    {
        public string? Owner { get; set; }
        public string? TaskType { get; set; }
        public string? TaskOwner { get; set; }
        public string? TaskName { get; set; }
        public string? Attempt { get; set; }
        public string? Behavior { get; set; }
        public string? EmitAlias { get; set; }
        public string? FactJson { get; set; }
        public int? Hops { get; set; }
    }

    internal sealed record EmitFactResponse(string Outcome);

    internal sealed record UserActionRequiredResponse(
        string Outcome,
        string TaskType,
        string TaskOwner,
        string TaskName,
        string Attempt,
        string ModuleType,
        string ModuleOwner,
        string ModuleName,
        string ModuleId,
        string DisplayText,
        string ActionReferenceId,
        DateTimeOffset? ActionReferenceExpiresAt,
        string ActionEpoch,
        long ParkRevision,
        DateTimeOffset ExpiresAt,
        string CompleterType,
        string CompleterOwner,
        string CompleterName);
}
