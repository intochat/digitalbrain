namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record PostMeetingSummary(string MeetingId, string SummaryText) : Synapse;

public sealed record SummaryReady(string MeetingId, string SummaryText) : Synapse;

public sealed record PostSlackSummary(string MeetingId, string SummaryText) : Synapse;

public sealed record SlackAck(string MeetingId) : Synapse;

public sealed record RecoveryAttempted(SynapseRef FailedFact, string AlternateRoute) : Synapse;

public sealed record EmailSummaryReady(string MeetingId, string SummaryText, SynapseRef HealedFrom) : Synapse;

public sealed record EmailDispatched(string MeetingId, string Channel) : Synapse;

public sealed record RouteHealed(string MeetingId, string Via) : Synapse;

// Ambient catalog listeners so SummaryReady / RecoveryAttempted can be emitted without throw.
public sealed class SummaryLedger : Neuron, INeuron<SummaryReady>
{
    public Task HandleAsync(SummaryReady fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class RecoveryLedger : Neuron, INeuron<RecoveryAttempted>, INeuron<RouteHealed>
{
    public Task HandleAsync(RecoveryAttempted fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(RouteHealed fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

// Intended primary path: announce summary, Ask Slack (no answerer in this composition → DeliveryFailed).
public sealed class MeetingSummarizer : Neuron<MeetingSummarizerState>,
    INeuron<PostMeetingSummary>,
    INeuron<SlackAck>
{
    public Task HandleAsync(PostMeetingSummary fact, CancellationToken cancellationToken)
    {
        State.PendingMeetingId = fact.MeetingId;
        State.PendingSummary = fact.SummaryText;
        Emit(new SummaryReady(fact.MeetingId, fact.SummaryText));
        Ask<SlackAck>(new PostSlackSummary(fact.MeetingId, fact.SummaryText));
        return Task.CompletedTask;
    }

    public Task HandleAsync(SlackAck fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

public sealed class MeetingSummarizerState
{
    public string? PendingMeetingId { get; set; }
    public string? PendingSummary { get; set; }
}

// Listens to Core DeliveryFailed on the same context name; routes to email fallback.
public sealed class HealRouter : Neuron, INeuron<DeliveryFailed>
{
    public Task HandleAsync(DeliveryFailed fact, CancellationToken cancellationToken)
    {
        if (!IsHealable(fact))
        {
            return Task.CompletedTask;
        }

        Emit(new RecoveryAttempted(fact.Fact, "email-fallback"));
        // Payload for the alternate channel is not in DeliveryFailed; heal carries a stable marker
        // keyed by the failed ask ref so journals prove the chain without replaying Slack.
        Emit(new EmailSummaryReady(
            MeetingId: $"healed-{fact.Fact.Sequence}",
            SummaryText: $"alternate-for-{fact.Reason}",
            HealedFrom: fact.Fact));
        return Task.CompletedTask;
    }

    private static bool IsHealable(DeliveryFailed fact)
        => fact.Reason is "no-answerer"
            || fact.Reason.Contains("catalog", StringComparison.OrdinalIgnoreCase);
}

public sealed class EmailFallback : Neuron, INeuron<EmailSummaryReady>
{
    public Task HandleAsync(EmailSummaryReady fact, CancellationToken cancellationToken)
    {
        Emit(new EmailDispatched(fact.MeetingId, "email"));
        Emit(new RouteHealed(fact.MeetingId, "email-fallback"));
        return Task.CompletedTask;
    }
}

// Registers PostSlackSummary in the catalog without answering it — Ask then journals no-answerer DeliveryFailed.
public sealed class SlackUnavailable : Neuron, INeuron<PostSlackSummary>
{
    public Task HandleAsync(PostSlackSummary fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}

// Catalog sink for EmailDispatched ambient emit.
public sealed class DispatchLedger : Neuron, INeuron<EmailDispatched>
{
    public Task HandleAsync(EmailDispatched fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
