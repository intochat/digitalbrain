using System.Security.Cryptography;
using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Behaviors.Runtime;

public static class BehaviorProtectedTriggerBrokerEndpoints
{
    public static IEndpointRouteBuilder MapBehaviorProtectedTriggerBroker(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/v1/behaviors/broker/triggers/load", LoadAsync);
        return endpoints;
    }

    private static async Task<IResult> LoadAsync(
        LoadTriggerRequest body,
        IBehaviorProtectedTriggerAccess access,
        IGrainFactory grains,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(grains);

        try
        {
            var identity = RequireIdentity(body);
            var reference = BehaviorBrokerEndpointCommon.RequireReference(body.Reference);

            cancellationToken.ThrowIfCancellationRequested();
            await RequireActiveTaskAuthorityAsync(grains, identity, cancellationToken).ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            var plaintext = await access
                .LoadAsync(
                    identity.Owner,
                    identity.Task,
                    identity.Behavior,
                    identity.Revision,
                    identity.CaseId,
                    reference,
                    cancellationToken)
                .ConfigureAwait(false);

            if (plaintext.IsEmpty)
            {
                return BehaviorBrokerEndpointCommon.Failure(BehaviorExecutionCodes.TriggerMissing);
            }

            return Results.Ok(new LoadTriggerResponse(Convert.ToBase64String(plaintext.Span)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return BehaviorBrokerEndpointCommon.Failure(MapArgumentReason(exception));
        }
        catch (InvalidOperationException exception) when (IsStableReason(exception.Message))
        {
            return BehaviorBrokerEndpointCommon.Failure(BehaviorExecutionCodes.MapHostFailure(exception.Message));
        }
        catch (CryptographicException)
        {
            return BehaviorBrokerEndpointCommon.Failure(BehaviorExecutionCodes.TriggerMissing);
        }
    }

    private static async Task RequireActiveTaskAuthorityAsync(
        IGrainFactory grains,
        BoundTriggerIdentity identity,
        CancellationToken cancellationToken)
    {
        var authority = grains.GetGrain<IBehaviorTaskAuthority>(
            BehaviorTaskAuthority.ForOwner(identity.Owner).ToGrainId());
        var snapshot = await authority
            .ReadValidatedTask(identity.Task, identity.Attempt, requireActivation: true, cancellationToken)
            .ConfigureAwait(false);

        if (snapshot.Worker != identity.Worker)
        {
            throw new InvalidOperationException("worker-mismatch");
        }

        if (snapshot.Activation is null)
        {
            throw new InvalidOperationException("activation-required");
        }

        var activation = snapshot.Activation;
        if (activation.BehaviorId != identity.Behavior
            || activation.Revision != identity.Revision
            || !string.Equals(activation.CaseId, identity.CaseId, StringComparison.Ordinal)
            || activation.ProtectedPayload.Id != identity.ReferenceId)
        {
            throw new InvalidOperationException("activation-mismatch");
        }
    }

    private static BoundTriggerIdentity RequireIdentity(LoadTriggerRequest body)
    {
        if (string.IsNullOrWhiteSpace(body.Owner))
        {
            throw new ArgumentException(paramName: null, message: "missing-owner");
        }

        if (string.IsNullOrWhiteSpace(body.TaskOwner))
        {
            throw new ArgumentException(paramName: null, message: "missing-task-owner");
        }

        OwnerId owner;
        OwnerId taskOwner;
        try
        {
            owner = new OwnerId(body.Owner);
            taskOwner = new OwnerId(body.TaskOwner);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException(paramName: null, message: "invalid-request");
        }

        if (owner != taskOwner)
        {
            throw new InvalidOperationException("owner-task-mismatch");
        }

        if (string.IsNullOrWhiteSpace(body.TaskType)
            || string.IsNullOrWhiteSpace(body.TaskName)
            || string.IsNullOrWhiteSpace(body.Behavior)
            || string.IsNullOrWhiteSpace(body.Revision)
            || string.IsNullOrWhiteSpace(body.CaseId)
            || string.IsNullOrWhiteSpace(body.Attempt)
            || string.IsNullOrWhiteSpace(body.WorkerType)
            || string.IsNullOrWhiteSpace(body.WorkerOwner)
            || string.IsNullOrWhiteSpace(body.WorkerName))
        {
            throw new ArgumentException(paramName: null, message: "missing-task-identity");
        }

        if (!Guid.TryParseExact(body.Attempt, "N", out var attemptValue) || attemptValue == Guid.Empty)
        {
            throw new ArgumentException(paramName: null, message: "invalid-attempt");
        }

        OwnerId workerOwner;
        try
        {
            workerOwner = new OwnerId(body.WorkerOwner);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException(paramName: null, message: "invalid-request");
        }

        if (workerOwner != owner)
        {
            throw new InvalidOperationException("worker-mismatch");
        }

        try
        {
            var task = new NeuronId(body.TaskType, taskOwner, body.TaskName);
            var worker = new NeuronId(body.WorkerType, workerOwner, body.WorkerName);
            if (!string.Equals(
                    worker.Type,
                    NeuronId.GrainTypeNameOf(typeof(IWorker)),
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("worker-mismatch");
            }

            if (body.Reference is null || string.IsNullOrWhiteSpace(body.Reference.Id)
                || !Guid.TryParseExact(body.Reference.Id, "N", out var referenceId)
                || referenceId == Guid.Empty)
            {
                throw new ArgumentException(paramName: null, message: "invalid-protected-reference");
            }

            return new BoundTriggerIdentity(
                owner,
                task,
                worker,
                new AttemptId(attemptValue),
                new BehaviorId(body.Behavior),
                new BehaviorRevisionId(body.Revision),
                body.CaseId,
                referenceId);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException(paramName: null, message: "invalid-request");
        }
    }

    private static string MapArgumentReason(ArgumentException exception)
    {
        if (IsStableReason(exception.Message))
        {
            return BehaviorExecutionCodes.MapHostFailure(exception.Message);
        }

        return BehaviorExecutionCodes.TriggerUnauthorized;
    }

    private static bool IsStableReason(string? reason)
        => reason is "missing-owner"
            or "missing-task-owner"
            or "owner-task-mismatch"
            or "missing-task-identity"
            or "empty-payload"
            or "invalid-payload-content"
            or "invalid-protected-reference"
            or "invalid-request"
            or "invalid-attempt"
            or "worker-mismatch"
            or "attempt-mismatch"
            or "activation-required"
            or "activation-mismatch"
            or "task-not-started"
            or "invalid-task-identity";

    private sealed record BoundTriggerIdentity(
        OwnerId Owner,
        NeuronId Task,
        NeuronId Worker,
        AttemptId Attempt,
        BehaviorId Behavior,
        BehaviorRevisionId Revision,
        string CaseId,
        Guid ReferenceId);

    internal sealed class LoadTriggerRequest
    {
        public string? Owner { get; set; }
        public string? TaskType { get; set; }
        public string? TaskOwner { get; set; }
        public string? TaskName { get; set; }
        public string? Attempt { get; set; }
        public string? WorkerType { get; set; }
        public string? WorkerOwner { get; set; }
        public string? WorkerName { get; set; }
        public string? Behavior { get; set; }
        public string? Revision { get; set; }
        public string? CaseId { get; set; }
        public ProtectedReferenceBody? Reference { get; set; }
    }

    internal sealed record LoadTriggerResponse(string ContentBase64);
}
