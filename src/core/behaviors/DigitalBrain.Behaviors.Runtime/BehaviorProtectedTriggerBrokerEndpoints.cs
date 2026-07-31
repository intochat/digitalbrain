using System.Security.Cryptography;
using DigitalBrain.Abstractions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Behaviors;

public static class BehaviorProtectedTriggerBrokerEndpoints
{
    public static IEndpointRouteBuilder MapBehaviorProtectedTriggerBroker(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/v1/behaviors/broker/triggers/store", StoreAsync);
        endpoints.MapPost("/v1/behaviors/broker/triggers/load", LoadAsync);
        return endpoints;
    }

    private static async Task<IResult> StoreAsync(
        StoreTriggerRequest body,
        IBehaviorProtectedTriggerAccess access,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(access);

        try
        {
            var identity = RequireIdentity(body);

            if (string.IsNullOrWhiteSpace(body.ContentBase64))
            {
                return Failure("empty-payload");
            }

            byte[] plaintext;
            try
            {
                plaintext = Convert.FromBase64String(body.ContentBase64);
            }
            catch (FormatException)
            {
                return Failure("invalid-payload-content");
            }

            if (plaintext.Length == 0)
            {
                return Failure("empty-payload");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var reference = await access
                .StoreAsync(
                    identity.Owner,
                    identity.Task,
                    identity.Behavior,
                    identity.Revision,
                    identity.CaseId,
                    plaintext,
                    cancellationToken)
                .ConfigureAwait(false);

            return Results.Ok(new ProtectedReferenceResponse(
                reference.Id.ToString("N"),
                reference.ExpiresAt));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Failure(MapArgumentReason(exception));
        }
        catch (InvalidOperationException exception) when (IsStableReason(exception.Message))
        {
            return Failure(exception.Message);
        }
        catch (CryptographicException)
        {
            return Failure("invalid-protected-reference");
        }
    }

    private static async Task<IResult> LoadAsync(
        LoadTriggerRequest body,
        IBehaviorProtectedTriggerAccess access,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(access);

        try
        {
            var identity = RequireIdentity(body);
            var reference = RequireReference(body.Reference);

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
                return Failure("invalid-payload-content");
            }

            return Results.Ok(new LoadTriggerResponse(Convert.ToBase64String(plaintext.Span)));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            return Failure(MapArgumentReason(exception));
        }
        catch (InvalidOperationException exception) when (IsStableReason(exception.Message))
        {
            return Failure(exception.Message);
        }
        catch (CryptographicException)
        {
            return Failure("invalid-protected-reference");
        }
    }

    private static BoundTriggerIdentity RequireIdentity(ITriggerIdentityBody body)
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
            || string.IsNullOrWhiteSpace(body.CaseId))
        {
            throw new ArgumentException(paramName: null, message: "missing-task-identity");
        }

        try
        {
            return new BoundTriggerIdentity(
                owner,
                new NeuronId(body.TaskType, taskOwner, body.TaskName),
                new BehaviorId(body.Behavior),
                new BehaviorRevisionId(body.Revision),
                body.CaseId);
        }
        catch (ArgumentException)
        {
            throw new ArgumentException(paramName: null, message: "invalid-request");
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

    private static string MapArgumentReason(ArgumentException exception)
    {
        if (exception.ParamName is "plaintext")
        {
            return "empty-payload";
        }

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
            or "empty-payload"
            or "invalid-payload-content"
            or "invalid-protected-reference"
            or "invalid-request";

    private static IResult Failure(string reason)
        => Results.Content(reason, "text/plain", statusCode: StatusCodes.Status400BadRequest);

    private sealed record BoundTriggerIdentity(
        OwnerId Owner,
        NeuronId Task,
        BehaviorId Behavior,
        BehaviorRevisionId Revision,
        string CaseId);

    private interface ITriggerIdentityBody
    {
        string? Owner { get; }
        string? TaskType { get; }
        string? TaskOwner { get; }
        string? TaskName { get; }
        string? Behavior { get; }
        string? Revision { get; }
        string? CaseId { get; }
    }

    internal sealed class StoreTriggerRequest : ITriggerIdentityBody
    {
        public string? Owner { get; set; }
        public string? TaskType { get; set; }
        public string? TaskOwner { get; set; }
        public string? TaskName { get; set; }
        public string? Behavior { get; set; }
        public string? Revision { get; set; }
        public string? CaseId { get; set; }
        public string? ContentBase64 { get; set; }
    }

    internal sealed class LoadTriggerRequest : ITriggerIdentityBody
    {
        public string? Owner { get; set; }
        public string? TaskType { get; set; }
        public string? TaskOwner { get; set; }
        public string? TaskName { get; set; }
        public string? Behavior { get; set; }
        public string? Revision { get; set; }
        public string? CaseId { get; set; }
        public ProtectedReferenceBody? Reference { get; set; }
    }

    internal sealed class ProtectedReferenceBody
    {
        public string? Id { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    internal sealed record ProtectedReferenceResponse(string Id, DateTimeOffset? ExpiresAt);

    internal sealed record LoadTriggerResponse(string ContentBase64);
}
