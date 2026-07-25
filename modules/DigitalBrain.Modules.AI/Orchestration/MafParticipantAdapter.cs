using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

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

internal static class MafParticipantAdapter
{
    internal static OrchestrationParticipant Describe(Participant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        Validate(participant.Contract);

        return new OrchestrationParticipant(
            participant.Contract.AssemblyQualifiedName
                ?? throw new InvalidOperationException("A participant contract has no assembly-qualified identity."),
            participant.Id);
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "NeuronChatClient has empty disposal and is owned by the ChatClientAgent for the agent lifetime.")]
    internal static AIAgent Create<TNeuron>(
        IGrainFactory grains,
        NeuronId id,
        TaskScheduler turnScheduler)
        where TNeuron : INeuron
    {
        ArgumentNullException.ThrowIfNull(turnScheduler);
        Validate(typeof(TNeuron));

        return new ChatClientAgent(
            new NeuronChatClient(grains.GetGrain<TNeuron>(id.ToGrainId()), turnScheduler),
            new ChatClientAgentOptions
            {
                Id = AgentId(typeof(TNeuron), id),
            });
    }

    private static string AgentId(Type contract, NeuronId id)
    {
        var source = Encoding.UTF8.GetBytes($"{contract.AssemblyQualifiedName}\n{id}");
        return $"dbp_{Convert.ToHexStringLower(SHA256.HashData(source))}";
    }

    private static void Validate(Type contract)
    {
        if (!typeof(ILLM).IsAssignableFrom(contract) && !typeof(IAgent).IsAssignableFrom(contract))
        {
            throw new InvalidOperationException(
                $"Participant contract '{contract.FullName}' must implement {nameof(ILLM)} or {nameof(IAgent)}.");
        }
    }
}
