using DigitalBrain.Abstractions.Neurons;
using DigitalBrain.Abstractions.Synapses;

namespace DigitalBrain.Abstractions.Scenarios;

[GenerateSerializer]
[Alias("db.scenario.state")]
public sealed class ScenarioState
{
    [Id(0)]
    public List<Synapse> Synapses { get; set; } = [];

    public void Apply(SynapseBound bound)
    {
        if (Find(bound.Source, bound.SignalType, bound.Target) >= 0)
        {
            return;
        }

        Synapses.Add(new Synapse(bound.Source, bound.Target, bound.SignalType, bound.At));
    }

    public void Apply(SynapseUnbound unbound)
    {
        var index = Find(unbound.Source, unbound.SignalType, unbound.Target);
        if (index >= 0)
        {
            Synapses.RemoveAt(index);
        }
    }

    private int Find(INeuron source, string signalType, INeuron target)
    {
        for (var i = 0; i < Synapses.Count; i++)
        {
            var edge = Synapses[i];
            if (Same(edge.Source, source) && Same(edge.Target, target)
                && string.Equals(edge.SignalType, signalType, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    public static bool Same(INeuron left, INeuron right) => left.GetGrainId() == right.GetGrainId();
}

[GenerateSerializer]
[Alias("db.scenario.bound")]
public sealed record SynapseBound(
    [property: Id(0)] INeuron Source,
    [property: Id(1)] INeuron Target,
    [property: Id(2)] string SignalType,
    [property: Id(3)] DateTimeOffset At);

[GenerateSerializer]
[Alias("db.scenario.unbound")]
public sealed record SynapseUnbound(
    [property: Id(0)] INeuron Source,
    [property: Id(1)] INeuron Target,
    [property: Id(2)] string SignalType);
