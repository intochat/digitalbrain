using System.Net.ServerSentEvents;
using DigitalBrain.Abstractions;
using DigitalBrain.Modules.Sdk.Mcp;
using DigitalBrain.Auth;

namespace DigitalBrain.Kernel;

internal static class AuthorizationStreamsHttpMaps
{
    public static IEndpointRouteBuilder MapAuthorizationStreams(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            HttpSurfacePaths.AuthorizationEventsPath,
            static async Task (
                HttpContext http,
                long? afterSequence,
                OwnerSessionJournal sessionJournal,
                CancellationToken cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(http);
                ArgumentNullException.ThrowIfNull(sessionJournal);
                cancellationToken.ThrowIfCancellationRequested();

                if (!HttpActor.TryGet(http, out var actor))
                {
                    http.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }

                var cursor = afterSequence.GetValueOrDefault();
                if (cursor < 0)
                {
                    http.Response.StatusCode = StatusCodes.Status400BadRequest;
                    return;
                }

                await SseResponse.WriteAsync(
                    http.Response,
                    WatchAuthorizationsAsync(
                        sessionJournal,
                        actor.PrincipalId,
                        cursor,
                        cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            });

        return endpoints;
    }

    private static IAsyncEnumerable<SseItem<AuthorizationEvent>> WatchAuthorizationsAsync(
        OwnerSessionJournal sessionJournal,
        PrincipalId principal,
        long afterSequence,
        CancellationToken cancellationToken)
        => JournalProjection.WatchAsync(
            token => sessionJournal.WatchAuthorizationOutgoingAsync(afterSequence, token),
            HttpSurfacePaths.AuthorizationEvent,
            delivery => ProjectAuthorization(delivery, principal),
            cancellationToken);

    private static AuthorizationEvent? ProjectAuthorization(
        SynapseDelivery delivery,
        PrincipalId principal)
    {
        AuthorizationEvent Authorization(
            string kind,
            CommandId command,
            string serverKey,
            string? serverDisplayName,
            string? signInUrl,
            string state)
            => new(
                delivery.Sequence,
                kind,
                command.ToString(),
                serverKey,
                serverDisplayName,
                signInUrl,
                state,
                delivery.Timestamp);

        return delivery.Synapse switch
        {
            AuthorizationRequired { Actor: { } actor } required
                when actor.PrincipalId == principal => Authorization(
                nameof(AuthorizationRequired),
                required.CommandId,
                required.ServerKey,
                required.ServerDisplayName,
                required.SignInUrl.AbsoluteUri,
                required.State),
            AuthorizationCompleted { Actor: { } actor } completed
                when actor.PrincipalId == principal => Authorization(
                nameof(AuthorizationCompleted),
                completed.CommandId,
                completed.ServerKey,
                serverDisplayName: null,
                signInUrl: null,
                completed.State),
            AuthorizationDenied { Actor: { } actor } denied
                when actor.PrincipalId == principal => Authorization(
                nameof(AuthorizationDenied),
                denied.CommandId,
                denied.ServerKey,
                serverDisplayName: null,
                signInUrl: null,
                denied.State),
            _ => null,
        };
    }
}
