namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record OpportunityStageChanged(
    string OppId,
    string FromStage,
    string ToStage,
    double Amount,
    string ChampionEmail) : Synapse;

public sealed record WinSequenceStarted(
    string SequenceId,
    string OppId,
    double Amount) : Synapse;

public sealed record WinThankYouEmailSent(
    string SequenceId,
    string OppId,
    string To,
    string Subject) : Synapse;

public sealed record InternalWinNotified(
    string SequenceId,
    string OppId,
    double Amount) : Synapse;

public sealed record WinCsTaskCreated(
    string SequenceId,
    string OppId,
    string Title) : Synapse;

public sealed record WinKickoffCalendarCreated(
    string SequenceId,
    string OppId,
    string Title) : Synapse;

public sealed record WinSequenceCancelled(
    string SequenceId,
    string OppId,
    string Reason) : Synapse;

public sealed record WinSequenceCompleted(
    string SequenceId,
    string OppId) : Synapse;

// Closed-won drives coordinated fan-out; stage flip cancels before completion.
public sealed class WinSequenceRunner : Neuron<WinSequenceState>,
    INeuron<OpportunityStageChanged>
{
    public Task HandleAsync(OpportunityStageChanged fact, CancellationToken cancellationToken)
    {
        if (string.Equals(fact.ToStage, "ClosedWon", StringComparison.Ordinal))
        {
            // Same opp already started or completed — duplicate webhook is a no-op.
            if (string.Equals(State.OppId, fact.OppId, StringComparison.Ordinal)
                && (State.Active || State.Completed))
            {
                return Task.CompletedTask;
            }

            var sequenceId = $"win-{fact.OppId}";
            State.SequenceId = sequenceId;
            State.OppId = fact.OppId;
            State.Active = true;
            State.Completed = false;

            Emit(new WinSequenceStarted(sequenceId, fact.OppId, fact.Amount));
            Emit(new WinThankYouEmailSent(
                sequenceId,
                fact.OppId,
                To: fact.ChampionEmail,
                Subject: $"Thank you — {fact.OppId} won"));
            Emit(new InternalWinNotified(sequenceId, fact.OppId, fact.Amount));
            Emit(new WinCsTaskCreated(
                sequenceId,
                fact.OppId,
                Title: $"CS onboarding: {fact.OppId}"));
            Emit(new WinKickoffCalendarCreated(
                sequenceId,
                fact.OppId,
                Title: $"Kickoff {fact.OppId}"));
            State.Completed = true;
            State.Active = false;
            Emit(new WinSequenceCompleted(sequenceId, fact.OppId));
            return Task.CompletedTask;
        }

        if (State.Active
            && string.Equals(State.OppId, fact.OppId, StringComparison.Ordinal)
            && !string.Equals(fact.ToStage, "ClosedWon", StringComparison.Ordinal))
        {
            Emit(new WinSequenceCancelled(
                State.SequenceId ?? $"win-{fact.OppId}",
                fact.OppId,
                Reason: $"stage-reverted-to-{fact.ToStage}"));
            State.Active = false;
            State.SequenceId = null;
        }

        return Task.CompletedTask;
    }
}

public sealed class WinSequenceState
{
    public string? SequenceId { get; set; }
    public string? OppId { get; set; }
    public bool Active { get; set; }
    public bool Completed { get; set; }
}

// Variant runner that leaves sequence open so cancel can fire (for deferred step proof).
public sealed class WinSequenceOpenRunner : Neuron<WinSequenceOpenState>,
    INeuron<OpportunityStageChanged>
{
    public Task HandleAsync(OpportunityStageChanged fact, CancellationToken cancellationToken)
    {
        if (string.Equals(fact.ToStage, "ClosedWon", StringComparison.Ordinal))
        {
            if (State.Active && string.Equals(State.OppId, fact.OppId, StringComparison.Ordinal))
            {
                return Task.CompletedTask;
            }

            var sequenceId = $"win-open-{fact.OppId}";
            State.SequenceId = sequenceId;
            State.OppId = fact.OppId;
            State.Active = true;
            Emit(new WinSequenceStarted(sequenceId, fact.OppId, fact.Amount));
            Emit(new WinThankYouEmailSent(
                sequenceId,
                fact.OppId,
                To: fact.ChampionEmail,
                Subject: $"Thank you — {fact.OppId}"));
            Emit(new InternalWinNotified(sequenceId, fact.OppId, fact.Amount));
            // Deliberately omit complete/calendar so cancel path remains open.
            return Task.CompletedTask;
        }

        if (State.Active
            && string.Equals(State.OppId, fact.OppId, StringComparison.Ordinal))
        {
            Emit(new WinSequenceCancelled(
                State.SequenceId!,
                fact.OppId,
                Reason: $"stage-reverted-to-{fact.ToStage}"));
            State.Active = false;
        }

        return Task.CompletedTask;
    }
}

public sealed class WinSequenceOpenState
{
    public string? SequenceId { get; set; }
    public string? OppId { get; set; }
    public bool Active { get; set; }
}

// Catalog sinks for win-sequence ambient fan-out.
public sealed class WinSequenceLedger : Neuron,
    INeuron<WinSequenceStarted>,
    INeuron<WinThankYouEmailSent>,
    INeuron<InternalWinNotified>,
    INeuron<WinCsTaskCreated>,
    INeuron<WinKickoffCalendarCreated>,
    INeuron<WinSequenceCancelled>,
    INeuron<WinSequenceCompleted>
{
    public Task HandleAsync(WinSequenceStarted fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(WinThankYouEmailSent fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(InternalWinNotified fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(WinCsTaskCreated fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(WinKickoffCalendarCreated fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(WinSequenceCancelled fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(WinSequenceCompleted fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
