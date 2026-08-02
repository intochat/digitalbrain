using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;

namespace DigitalBrain.Flutter.Http;

internal static class BehaviorEventFeed
{
    public static async IAsyncEnumerable<SseItem<BehaviorEvent>> WatchBehaviorAsync(
        OwnerSessionJournal sessionJournal,
        string behaviorId,
        long afterSequence,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionJournal);
        ArgumentException.ThrowIfNullOrWhiteSpace(behaviorId);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        await foreach (var batch in sessionJournal.WatchBehaviorOutgoingAsync(
                           behaviorId,
                           afterSequence,
                           cancellationToken))
        {
            foreach (var projected in Project(batch, behaviorId))
            {
                yield return new SseItem<BehaviorEvent>(projected, FlutterHttpContract.BehaviorEvent)
                {
                    EventId = projected.Sequence.ToString(CultureInfo.InvariantCulture),
                };
            }
        }
    }

    private static IEnumerable<BehaviorEvent> Project(JournalRead batch, string behaviorId)
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
                case BehaviorRevisionProposed proposed:
                    yield return Event(
                        delivery,
                        nameof(BehaviorRevisionProposed),
                        behaviorId,
                        proposed.CommandId,
                        proposed.ArtifactHash,
                        detail: null);
                    break;

                case BehaviorCompileSucceeded compileOk:
                    yield return Event(
                        delivery,
                        nameof(BehaviorCompileSucceeded),
                        behaviorId,
                        compileOk.CommandId,
                        compileOk.ArtifactHash,
                        detail: null);
                    break;

                case BehaviorCompileFailed compileFailed:
                    yield return Event(
                        delivery,
                        nameof(BehaviorCompileFailed),
                        behaviorId,
                        compileFailed.CommandId,
                        artifactHash: null,
                        compileFailed.Diagnostics);
                    break;

                case BehaviorTestsPassed testsPassed:
                    yield return Event(
                        delivery,
                        nameof(BehaviorTestsPassed),
                        behaviorId,
                        testsPassed.CommandId,
                        testsPassed.ArtifactHash,
                        $"{testsPassed.ScenarioCount} scenarios");
                    break;

                case BehaviorTestsFailed testsFailed:
                    yield return Event(
                        delivery,
                        nameof(BehaviorTestsFailed),
                        behaviorId,
                        testsFailed.CommandId,
                        testsFailed.ArtifactHash,
                        testsFailed.Failure);
                    break;

                case BehaviorRevisionApproved approved:
                    yield return Event(
                        delivery,
                        nameof(BehaviorRevisionApproved),
                        behaviorId,
                        approved.CommandId,
                        approved.ArtifactHash,
                        detail: null);
                    break;

                case BehaviorRevisionActivated activated:
                    yield return Event(
                        delivery,
                        nameof(BehaviorRevisionActivated),
                        behaviorId,
                        activated.CommandId,
                        activated.ArtifactHash,
                        activated.PriorArtifactHash);
                    break;

                case BehaviorActivationGateClosed gateClosed:
                    yield return Event(
                        delivery,
                        nameof(BehaviorActivationGateClosed),
                        behaviorId,
                        gateClosed.CommandId,
                        artifactHash: null,
                        detail: null);
                    break;

                case BehaviorStopping stopping:
                    yield return Event(
                        delivery,
                        nameof(BehaviorStopping),
                        behaviorId,
                        stopping.CommandId,
                        artifactHash: null,
                        detail: null);
                    break;

                case BehaviorStopped stopped:
                    yield return Event(
                        delivery,
                        nameof(BehaviorStopped),
                        behaviorId,
                        stopped.CommandId,
                        artifactHash: null,
                        detail: null);
                    break;

                case BehaviorStarted started:
                    yield return Event(
                        delivery,
                        nameof(BehaviorStarted),
                        behaviorId,
                        started.CommandId,
                        artifactHash: null,
                        detail: null);
                    break;

                case BehaviorExecuted executed:
                    yield return Event(
                        delivery,
                        nameof(BehaviorExecuted),
                        behaviorId,
                        executed.CommandId,
                        executed.ArtifactHash,
                        executed.Outcome);
                    break;

                case BehaviorRevisionRolledBack rolledBack:
                    yield return Event(
                        delivery,
                        nameof(BehaviorRevisionRolledBack),
                        behaviorId,
                        rolledBack.CommandId,
                        rolledBack.RestoredArtifactHash,
                        rolledBack.DemotedArtifactHash);
                    break;
            }
        }
    }

    private static BehaviorEvent Event(
        SynapseDelivery delivery,
        string kind,
        string behaviorId,
        CommandId commandId,
        string? artifactHash,
        string? detail)
        => new(
            delivery.Sequence,
            kind,
            behaviorId,
            commandId.ToString(),
            artifactHash,
            detail,
            delivery.Timestamp);
}
