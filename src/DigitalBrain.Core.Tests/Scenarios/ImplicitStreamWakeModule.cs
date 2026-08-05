namespace DigitalBrain.Core.Tests.Scenarios;

// Stage-1: Core has no Orleans implicit streams — prove ingress adapter that hears
// ExternalStreamTick and journals first, then fans domain facts (stream stand-in).

public sealed record ExternalStreamTick(
    string StreamId,
    string EventType,
    string Payload,
    string OwnerContext) : Synapse;

public sealed record SlackReactionAdded(
    string ReactionId,
    string Channel,
    string User,
    string Emoji) : Synapse;

public sealed record NoteCaptured(string NoteId, string Text, string ReactionId) : Synapse;

public sealed record StreamWakeUiToast(string Text, string ReactionId) : Synapse;

// Ingress adapter: first journal line of the "stream" path — maps tick → domain fact.
public sealed class StreamIngressAdapter : Neuron, INeuron<ExternalStreamTick>
{
    public Task HandleAsync(ExternalStreamTick fact, CancellationToken cancellationToken)
    {
        if (!string.Equals(fact.EventType, "slack.reaction_added", StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        // Payload shape: "channel|user|emoji|reactionId"
        var parts = fact.Payload.Split('|');
        if (parts.Length < 4)
        {
            return Task.CompletedTask;
        }

        Emit(new SlackReactionAdded(parts[3], parts[0], parts[1], parts[2]));
        return Task.CompletedTask;
    }
}

// May be dormant until first SlackReactionAdded; journals reception + follow-on emits.
public sealed class GratitudeNotes : Neuron, INeuron<SlackReactionAdded>
{
    public Task HandleAsync(SlackReactionAdded fact, CancellationToken cancellationToken)
    {
        Emit(new NoteCaptured(
            NoteId: $"note-{fact.ReactionId}",
            Text: $"Thanks to @{fact.User} for {fact.Emoji} in #{fact.Channel}",
            ReactionId: fact.ReactionId));
        Emit(new StreamWakeUiToast($"Noted thanks to @{fact.User}", fact.ReactionId));
        return Task.CompletedTask;
    }
}

public sealed class StreamWakeLedger : Neuron,
    INeuron<NoteCaptured>,
    INeuron<StreamWakeUiToast>,
    INeuron<SlackReactionAdded>
{
    public Task HandleAsync(NoteCaptured fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(StreamWakeUiToast fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(SlackReactionAdded fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
