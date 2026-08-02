using System.Net.ServerSentEvents;
using DigitalBrain.Abstractions;
using DigitalBrain.Behaviors;

namespace DigitalBrain.Flutter.Http;

internal static class BehaviorEventFeed
{
    public static IAsyncEnumerable<SseItem<BehaviorEvent>> WatchBehaviorAsync(
        OwnerSessionJournal sessionJournal,
        string behaviorId,
        long afterSequence,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionJournal);

        return JournalProjection.WatchAsync(
            token => sessionJournal.WatchBehaviorOutgoingAsync(behaviorId, afterSequence, token),
            FlutterHttpContract.BehaviorEvent,
            delivery => ProjectLifecycle(delivery, behaviorId),
            cancellationToken);
    }

    private static BehaviorEvent? ProjectLifecycle(SynapseDelivery delivery, string behaviorId)
    {
        BehaviorEvent Lifecycle(string kind, CommandId command, string? artifactHash, string? detail)
            => new(
                delivery.Sequence,
                kind,
                behaviorId,
                command.ToString(),
                artifactHash,
                detail,
                delivery.Timestamp);

        return delivery.Synapse switch
        {
            BehaviorRevisionProposed proposed => Lifecycle(
                nameof(BehaviorRevisionProposed), proposed.CommandId, proposed.ArtifactHash, detail: null),
            BehaviorCompileSucceeded compiled => Lifecycle(
                nameof(BehaviorCompileSucceeded), compiled.CommandId, compiled.ArtifactHash, detail: null),
            BehaviorCompileFailed compileFailed => Lifecycle(
                nameof(BehaviorCompileFailed), compileFailed.CommandId, artifactHash: null, compileFailed.Diagnostics),
            BehaviorTestsPassed testsPassed => Lifecycle(
                nameof(BehaviorTestsPassed),
                testsPassed.CommandId,
                testsPassed.ArtifactHash,
                $"{testsPassed.ScenarioCount} scenarios"),
            BehaviorTestsFailed testsFailed => Lifecycle(
                nameof(BehaviorTestsFailed), testsFailed.CommandId, testsFailed.ArtifactHash, testsFailed.Failure),
            BehaviorRevisionApproved approved => Lifecycle(
                nameof(BehaviorRevisionApproved), approved.CommandId, approved.ArtifactHash, detail: null),
            BehaviorRevisionActivated activated => Lifecycle(
                nameof(BehaviorRevisionActivated),
                activated.CommandId,
                activated.ArtifactHash,
                activated.PriorArtifactHash),
            BehaviorActivationGateClosed gateClosed => Lifecycle(
                nameof(BehaviorActivationGateClosed), gateClosed.CommandId, artifactHash: null, detail: null),
            BehaviorStopping stopping => Lifecycle(
                nameof(BehaviorStopping), stopping.CommandId, artifactHash: null, detail: null),
            BehaviorStopped stopped => Lifecycle(
                nameof(BehaviorStopped), stopped.CommandId, artifactHash: null, detail: null),
            BehaviorStarted started => Lifecycle(
                nameof(BehaviorStarted), started.CommandId, artifactHash: null, detail: null),
            BehaviorExecuted executed => Lifecycle(
                nameof(BehaviorExecuted), executed.CommandId, executed.ArtifactHash, executed.Outcome),
            BehaviorRevisionRolledBack rolledBack => Lifecycle(
                nameof(BehaviorRevisionRolledBack),
                rolledBack.CommandId,
                rolledBack.RestoredArtifactHash,
                rolledBack.DemotedArtifactHash),
            _ => null,
        };
    }
}
