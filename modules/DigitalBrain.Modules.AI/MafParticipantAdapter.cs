using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

internal static class MafParticipantAdapter
{
    internal static OrchestrationParticipant Describe(Participant participant)
    {
        ArgumentNullException.ThrowIfNull(participant);
        Validate(participant.Contract);

        var (agentId, agentName) = AgentIdentity(participant.Contract, participant.Id);

        return new OrchestrationParticipant(
            participant.Contract.AssemblyQualifiedName
                ?? throw new InvalidOperationException("A participant contract has no assembly-qualified identity."),
            participant.Id,
            agentId,
            agentName);
    }

    internal static AIAgent[] CreateAll(
        IGrainFactory grains,
        IReadOnlyList<Participant> participants,
        TaskScheduler turnScheduler)
    {
        ArgumentNullException.ThrowIfNull(grains);
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(turnScheduler);

        return [.. participants.Select(participant => participant.CreateAgent(grains, turnScheduler))];
    }

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "Participant adapters own no disposable resource and live with their MAF agents.")]
    internal static AIAgent Create<TNeuron>(
        IGrainFactory grains,
        NeuronId id,
        TaskScheduler turnScheduler)
        where TNeuron : INeuron
    {
        ArgumentNullException.ThrowIfNull(turnScheduler);
        Validate(typeof(TNeuron));

        var participant = grains.GetGrain<TNeuron>(id.ToGrainId());
        return new ChatClientAgent(
            new NeuronChatClient(participant, turnScheduler),
            Options(typeof(TNeuron), id));
    }

    private static ChatClientAgentOptions Options(Type contract, NeuronId id)
    {
        var (agentId, agentName) = AgentIdentity(contract, id);

        return new ChatClientAgentOptions
        {
            Id = agentId,
            Name = agentName,
        };
    }

    private static (string Id, string Name) AgentIdentity(Type contract, NeuronId id)
    {
        var source = Encoding.UTF8.GetBytes($"{contract.AssemblyQualifiedName}\n{id}");
        var identity = Convert.ToHexStringLower(SHA256.HashData(source));

        return ($"dbp_{identity}", $"participant_{identity}");
    }

    private static void Validate(Type contract)
    {
        if (!typeof(ILLM).IsAssignableFrom(contract) && !typeof(IAgent).IsAssignableFrom(contract))
        {
            throw Unsupported(contract);
        }
    }

    private static InvalidOperationException Unsupported(Type contract)
        => new($"Participant contract '{contract.FullName}' must implement {nameof(ILLM)} or {nameof(IAgent)}.");
}
