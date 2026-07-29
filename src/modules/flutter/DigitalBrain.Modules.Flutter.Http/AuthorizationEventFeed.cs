using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Mcp;

namespace DigitalBrain.UI;

internal static class AuthorizationEventFeed
{
    public static async IAsyncEnumerable<SseItem<AuthorizationEvent>> WatchAuthorizationsAsync(
        OwnerSessionJournal sessionJournal,
        long afterSequence,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionJournal);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        await foreach (var batch in sessionJournal.WatchAuthorizationOutgoingAsync(afterSequence, cancellationToken))
        {
            foreach (var projected in ProjectAuthorizations(batch))
            {
                yield return new SseItem<AuthorizationEvent>(projected, UiHttpContract.AuthorizationEvent)
                {
                    EventId = projected.Sequence.ToString(CultureInfo.InvariantCulture),
                };
            }
        }
    }

    private static IEnumerable<AuthorizationEvent> ProjectAuthorizations(JournalRead batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.ResetSnapshot is not null)
        {
            yield break;
        }

        foreach (var delivery in batch.Delta)
        {
            switch (delivery.Synapse)
            {
                case AuthorizationRequired required:
                    yield return new AuthorizationEvent(
                        delivery.Sequence,
                        nameof(AuthorizationRequired),
                        required.CommandId.ToString(),
                        required.ServerKey,
                        required.ServerDisplayName,
                        required.SignInUrl.AbsoluteUri,
                        required.State,
                        delivery.Timestamp);
                    break;

                case AuthorizationCompleted completed:
                    yield return new AuthorizationEvent(
                        delivery.Sequence,
                        nameof(AuthorizationCompleted),
                        completed.CommandId.ToString(),
                        completed.ServerKey,
                        ServerDisplayName: null,
                        SignInUrl: null,
                        completed.State,
                        delivery.Timestamp);
                    break;

                case AuthorizationDenied denied:
                    yield return new AuthorizationEvent(
                        delivery.Sequence,
                        nameof(AuthorizationDenied),
                        denied.CommandId.ToString(),
                        denied.ServerKey,
                        ServerDisplayName: null,
                        SignInUrl: null,
                        denied.State,
                        delivery.Timestamp);
                    break;
            }
        }
    }
}
