using DigitalBrain.Abstractions;
using Microsoft.Agents.AI;

namespace DigitalBrain.AI;

public abstract record Participant(NeuronId Id)
{
    internal abstract Type Contract { get; }

    internal abstract AIAgent CreateAgent(IGrainFactory grains, TaskScheduler turnScheduler);

    internal static Participant Of(Type contract, NeuronId id)
    {
        ArgumentNullException.ThrowIfNull(contract);
        MafParticipantAdapter.Validate(contract);

        return (Participant)Activator.CreateInstance(typeof(Participant<>).MakeGenericType(contract), id)!;
    }
}

public sealed record Participant<TNeuron>(NeuronId Id) : Participant(Id)
    where TNeuron : INeuron
{
    internal override Type Contract => typeof(TNeuron);

    internal override AIAgent CreateAgent(IGrainFactory grains, TaskScheduler turnScheduler)
        => MafParticipantAdapter.Create<TNeuron>(grains, Id, turnScheduler);
}
