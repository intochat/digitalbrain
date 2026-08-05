namespace DigitalBrain.Core.Tests.Scenarios;

public sealed record CancelableResearchAsked(string Goal) : Synapse;

public sealed record CancelableResearchStarted(int Generation, string Goal) : Synapse;

public sealed record CancelableResearchProgress(int Generation, int Step) : Synapse;

public sealed record CancelableResearchPulse : Synapse;

public sealed record CancelableUserCancel(string Reason) : Synapse;

public sealed record CancelableResearchCancelled(int Generation, string Reason) : Synapse;

public sealed record CancelableReplanStarted(int Generation, string Goal) : Synapse;

// Long research with generation counter: cancel freezes old gen Progress; replan starts new gen.
public sealed class CancelableResearchRunner : Neuron<CancelableResearchRunnerState>,
    INeuron<CancelableResearchAsked>,
    INeuron<CancelableResearchPulse>,
    INeuron<CancelableUserCancel>
{
    public static readonly TimeSpan PulsePeriod = TimeSpan.FromMilliseconds(50);

    public Task HandleAsync(CancelableResearchAsked fact, CancellationToken cancellationToken)
    {
        State.Generation = 1;
        State.Goal = fact.Goal;
        State.Step = 0;
        Emit(new CancelableResearchStarted(State.Generation, fact.Goal));
        Schedule(new CancelableResearchPulse(), PulsePeriod);
        return Task.CompletedTask;
    }

    public Task HandleAsync(CancelableResearchPulse fact, CancellationToken cancellationToken)
    {
        if (State.Generation <= 0 || State.Goal is null)
        {
            return Task.CompletedTask;
        }

        State.Step++;
        Emit(new CancelableResearchProgress(State.Generation, State.Step));
        return Task.CompletedTask;
    }

    public Task HandleAsync(CancelableUserCancel fact, CancellationToken cancellationToken)
    {
        if (State.Generation <= 0)
        {
            return Task.CompletedTask;
        }

        var cancelledGeneration = State.Generation;
        Emit(new CancelableResearchCancelled(cancelledGeneration, fact.Reason));
        State.Generation = cancelledGeneration + 1;
        State.Step = 0;
        Emit(new CancelableReplanStarted(State.Generation, State.Goal ?? string.Empty));
        return Task.CompletedTask;
    }
}

public sealed class CancelableResearchRunnerState
{
    public int Generation { get; set; }
    public string? Goal { get; set; }
    public int Step { get; set; }
}

// Catalog sinks for ambient cancel/replan research facts (distinct from S29 ResearchUiProjector).
public sealed class CancelReplanUiProjector : Neuron,
    INeuron<CancelableResearchStarted>,
    INeuron<CancelableResearchProgress>,
    INeuron<CancelableResearchCancelled>,
    INeuron<CancelableReplanStarted>
{
    public Task HandleAsync(CancelableResearchStarted fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(CancelableResearchProgress fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(CancelableResearchCancelled fact, CancellationToken cancellationToken)
        => Task.CompletedTask;

    public Task HandleAsync(CancelableReplanStarted fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
