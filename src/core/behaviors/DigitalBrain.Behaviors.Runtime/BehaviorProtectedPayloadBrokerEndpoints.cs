using System.Security.Cryptography;
using DigitalBrain.Abstractions;
using DigitalBrain.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace DigitalBrain.Behaviors.Runtime;

public static class BehaviorProtectedPayloadBrokerEndpoints
{
    public static IEndpointRouteBuilder MapBehaviorProtectedPayloadBroker(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapPost("/v1/behaviors/broker/payloads/store", StoreAsync);
        endpoints.MapPost("/v1/behaviors/broker/payloads/load", LoadAsync);
        return endpoints;
    }

    private static async Task<IResult> StoreAsync(
        StorePayloadRequest body,
        IBehaviorProtectedPayloadAccess access,
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

            if (string.IsNullOrWhiteSpace(body.ContentBase64))
            {
                return BehaviorBrokerEndpointCommon.Failure("empty-payload");
            }

            byte[] plaintext;
            try
            {
                plaintext = Convert.FromBase64String(body.ContentBase64);
            }
            catch (FormatException)
            {
                return BehaviorBrokerEndpointCommon.Failure("invalid-payload-content");
            }

            if (plaintext.Length == 0)
            {
                return BehaviorBrokerEndpointCommon.Failure("empty-payload");
            }

            cancellationToken.ThrowIfCancellationRequested();
            var reference = await access
                .StoreAsync(identity.Owner, identity.Task, identity.Attempt, plaintext, cancellationToken)
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
            return BehaviorBrokerEndpointCommon.Failure(MapArgumentReason(exception));
        }
        catch (InvalidOperationException exception) when (IsStableReason(exception.Message))
        {
            return BehaviorBrokerEndpointCommon.Failure(exception.Message);
        }
        catch (CryptographicException)
        {
            return BehaviorBrokerEndpointCommon.Failure("invalid-protected-reference");
        }
    }

    private static async Task<IResult> LoadAsync(
        LoadPayloadRequest body,
        IBehaviorProtectedPayloadAccess access,
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
            var reference = BehaviorBrokerEndpointCommon.RequireReference(body.Reference);

            cancellationToken.ThrowIfCancellationRequested();
            var plaintext = await access
                .LoadAsync(identity.Owner, identity.Task, identity.Attempt, reference, cancellationToken)
                .ConfigureAwait(false);

            if (plaintext.IsEmpty)
            {
                return BehaviorBrokerEndpointCommon.Failure("invalid-payload-content");
            }

            return Results.Ok(new LoadPayloadResponse(Convert.ToBase64String(plaintext.Span)));
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
            return BehaviorBrokerEndpointCommon.Failure(exception.Message);
        }
        catch (CryptographicException)
        {
            return BehaviorBrokerEndpointCommon.Failure("invalid-protected-reference");
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
            or "invalid-attempt"
            or "empty-payload"
            or "invalid-payload-content"
            or "invalid-protected-reference"
            or "invalid-request";

    internal sealed class StorePayloadRequest
    {
        public string? Owner { get; set; }
        public string? TaskType { get; set; }
        public string? TaskOwner { get; set; }
        public string? TaskName { get; set; }
        public string? Attempt { get; set; }
        public string? ContentBase64 { get; set; }
    }

    internal sealed class LoadPayloadRequest
    {
        public string? Owner { get; set; }
        public string? TaskType { get; set; }
        public string? TaskOwner { get; set; }
        public string? TaskName { get; set; }
        public string? Attempt { get; set; }
        public ProtectedReferenceBody? Reference { get; set; }
    }

    internal sealed record LoadPayloadResponse(string ContentBase64);
}
