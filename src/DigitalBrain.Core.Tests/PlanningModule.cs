using System.Collections.Immutable;

namespace DigitalBrain.Core.Tests;

public sealed record PlanDay(DateOnly Date) : Synapse;

public sealed record DayPlanned(DateOnly Date, ImmutableArray<string> Tasks) : Synapse;

public sealed class Planner : Neuron, INeuron<PlanDay>
{
    public void Hear(PlanDay fact) => Emit(new DayPlanned(fact.Date, ["write core", "walk"]));
}

public sealed class Diary : Neuron, INeuron<DayPlanned>;
