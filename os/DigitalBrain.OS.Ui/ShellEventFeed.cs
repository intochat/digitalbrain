using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Flutter;

namespace DigitalBrain.Flutter.Http;

internal static class ShellEventFeed
{
    public static async IAsyncEnumerable<SseItem<SceneOpenedEvent>> WatchSceneOpenedAsync(
        OwnerSessionJournal sessionJournal,
        string shellName,
        long afterSequence,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionJournal);
        ArgumentException.ThrowIfNullOrWhiteSpace(shellName);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        await foreach (var batch in sessionJournal.WatchShellOutgoingAsync(shellName, afterSequence, cancellationToken))
        {
            foreach (var projected in ProjectSceneOpened(batch))
            {
                yield return new SseItem<SceneOpenedEvent>(projected, FlutterHttpContract.SceneOpenedEvent)
                {
                    EventId = projected.Sequence.ToString(CultureInfo.InvariantCulture),
                };
            }
        }
    }

    private static IEnumerable<SceneOpenedEvent> ProjectSceneOpened(JournalRead batch)
    {
        ArgumentNullException.ThrowIfNull(batch);

        if (batch.ResetSnapshot is not null)
        {
            yield break;
        }

        foreach (var delivery in batch.Delta)
        {
            if (delivery.Synapse is not SceneOpened opened)
            {
                continue;
            }

            yield return new SceneOpenedEvent(
                delivery.Sequence,
                opened.SceneKey,
                opened.Title,
                opened.CommandId.ToString(),
                opened.Shell.ToString());
        }
    }
}
