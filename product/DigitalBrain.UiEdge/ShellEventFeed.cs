using System.Net.ServerSentEvents;
using DigitalBrain.Abstractions;
using DigitalBrain.Shell;

namespace DigitalBrain.UiEdge;

internal static class ShellEventFeed
{
    public static IAsyncEnumerable<SseItem<SceneOpenedEvent>> WatchSceneOpenedAsync(
        OwnerSessionJournal sessionJournal,
        string shellName,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionJournal);

        return JournalProjection.WatchAsync(
            token => sessionJournal.WatchShellOutgoingAsync(shellName, afterSequence, token),
            UiEdgeContract.SceneOpenedEvent,
            ProjectSceneOpened,
            cancellationToken);
    }

    private static SceneOpenedEvent? ProjectSceneOpened(SynapseDelivery delivery)
        => delivery.Synapse is not SceneOpened opened
            ? null
            : new SceneOpenedEvent(
                delivery.Sequence,
                opened.SceneKey,
                opened.Title,
                opened.CommandId.ToString(),
                opened.Shell.ToString());
}
