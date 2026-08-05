using System.Collections.Immutable;

namespace DigitalBrain.Core.Tests;

public sealed record PlanDay(DateOnly Date) : Synapse;

public sealed record DayPlanned(DateOnly Date, ImmutableArray<string> Tasks) : Synapse;

public sealed class Planner : Neuron, INeuron<PlanDay>
{
    public Task HandleAsync(PlanDay fact, CancellationToken cancellationToken)
    {
        Emit(new DayPlanned(fact.Date, ["write core", "walk"]));
        return Task.CompletedTask;
    }
}

public sealed class Diary : Neuron, INeuron<DayPlanned>
{
    public Task HandleAsync(DayPlanned fact, CancellationToken cancellationToken)
        => Task.CompletedTask;
}
