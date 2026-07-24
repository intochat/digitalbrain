using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
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

        if (participants.Count == 0)
        {
            throw new InvalidOperationException("An orchestration requires at least one participant.");
        }

        foreach (var participant in participants)
        {
            ArgumentNullException.ThrowIfNull(participant);
            Validate(participant.Contract);
        }

        return [.. participants.Select(participant => participant.CreateAgent(grains, turnScheduler))];
    }

    internal static AIAgent[] CreateDelegated(
        IGrainFactory grains,
        IReadOnlyList<OrchestrationParticipant> participants,
        TaskScheduler turnScheduler,
        Func<OrchestrationParticipant, Task<CapabilityDelegation>> authorize)
    {
        ArgumentNullException.ThrowIfNull(grains);
        ArgumentNullException.ThrowIfNull(participants);
        ArgumentNullException.ThrowIfNull(turnScheduler);
        ArgumentNullException.ThrowIfNull(authorize);

        if (participants.Count == 0)
        {
            throw new InvalidOperationException("An orchestration requires at least one participant.");
        }

        return [.. participants.Select(participant => CreateDelegated(
            grains,
            participant,
            turnScheduler,
            authorize))];
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
        var options = Options(typeof(TNeuron), id);

        return participant switch
        {
            ILLM model => new ChatClientAgent(new NeuronChatClient(model, turnScheduler), options),
            IAgent agent => new ChatClientAgent(new NeuronChatClient(agent, turnScheduler), options),
            _ => throw Unsupported(typeof(TNeuron)),
        };
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

    [SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "The delegated adapter owns no disposable resource and lives with its MAF agent.")]
    private static ChatClientAgent CreateDelegated(
        IGrainFactory grains,
        OrchestrationParticipant participant,
        TaskScheduler turnScheduler,
        Func<OrchestrationParticipant, Task<CapabilityDelegation>> authorize)
    {
        var contract = Type.GetType(participant.Contract, throwOnError: true)
            ?? throw new InvalidOperationException(
                $"Participant contract '{participant.Contract}' cannot be resolved.");
        Validate(contract);

        Func<IReadOnlyList<ChatMessage>, Task<ChatResponse>> invoke =
            typeof(ILLM).IsAssignableFrom(contract)
                ? grains.GetGrain<ILLM>(participant.NeuronId.ToGrainId()).Respond
                : grains.GetGrain<IAgent>(participant.NeuronId.ToGrainId()).Respond;
        var client = new NeuronChatClient(
            async request =>
            {
                var delegation = await authorize(participant);

                return await DigitalBrainRuntime.InvokeAsync(
                    delegation,
                    () => invoke(request));
            },
            turnScheduler);

        return new ChatClientAgent(
            client,
            new ChatClientAgentOptions
            {
                Id = participant.AgentId,
                Name = participant.AgentName,
            });
    }

}
