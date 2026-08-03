using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;
using Microsoft.AspNetCore.Http;

namespace DigitalBrain.Behaviors.Runtime;

internal static class BehaviorBrokerEndpointCommon
{
    internal static IResult Failure(string reason)
        => Results.Content(reason, "text/plain", statusCode: StatusCodes.Status400BadRequest);

    internal static BoundIdentity RequireIdentity(
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

    internal static ProtectedPayloadReference RequireReference(ProtectedReferenceBody? body)
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

    internal static ValidatedEdgeFields RequireEdgeFields(EdgeBody? body)
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
            return new ValidatedEdgeFields(
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
}

internal sealed record BoundIdentity(OwnerId Owner, NeuronId Task, AttemptId Attempt);

internal readonly record struct ValidatedEdgeFields(
    NeuronId Target,
    string RequestId,
    int RequestVersion,
    string ResponseId,
    int ResponseVersion);

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

internal sealed record ProtectedReferenceResponse(string Id, DateTimeOffset? ExpiresAt);
