using DigitalBrain.Abstractions;
using Microsoft.Agents.AI;

namespace DigitalBrain.AI;

public abstract record Participant(NeuronId Id)
{
    internal abstract Type Contract { get; }

    internal abstract AIAgent CreateAgent(IGrainFactory grains, TaskScheduler turnScheduler);
}

public sealed record Participant<TNeuron>(NeuronId Id) : Participant(Id)
    where TNeuron : INeuron
{
    internal override Type Contract => typeof(TNeuron);

    internal override AIAgent CreateAgent(IGrainFactory grains, TaskScheduler turnScheduler)
        => MafParticipantAdapter.Create<TNeuron>(grains, Id, turnScheduler);
}
