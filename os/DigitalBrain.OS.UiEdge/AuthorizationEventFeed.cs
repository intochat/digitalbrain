using System.Net.ServerSentEvents;
using DigitalBrain.Abstractions;
using DigitalBrain.Mcp;

namespace DigitalBrain.OS.UiEdge;

internal static class AuthorizationEventFeed
{
    public static IAsyncEnumerable<SseItem<AuthorizationEvent>> WatchAuthorizationsAsync(
        OwnerSessionJournal sessionJournal,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionJournal);

        return JournalProjection.WatchAsync(
            token => sessionJournal.WatchAuthorizationOutgoingAsync(afterSequence, token),
            UiEdgeContract.AuthorizationEvent,
            ProjectAuthorization,
            cancellationToken);
    }

    private static AuthorizationEvent? ProjectAuthorization(SynapseDelivery delivery)
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
            AuthorizationRequired required => Authorization(
                nameof(AuthorizationRequired),
                required.CommandId,
                required.ServerKey,
                required.ServerDisplayName,
                required.SignInUrl.AbsoluteUri,
                required.State),
            AuthorizationCompleted completed => Authorization(
                nameof(AuthorizationCompleted),
                completed.CommandId,
                completed.ServerKey,
                serverDisplayName: null,
                signInUrl: null,
                completed.State),
            AuthorizationDenied denied => Authorization(
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
