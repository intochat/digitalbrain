using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
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
            participant.Id.ToString(),
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
            IAgent agent => new ChatClientAgent(new NeuronAgentChatClient(agent, turnScheduler), options),
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

    private static Task<T> OnTurn<T>(Func<Task<T>> invoke, TaskScheduler turnScheduler)
        => Task.Factory.StartNew(
            invoke,
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            turnScheduler).Unwrap();

    private sealed class NeuronAgentChatClient(IAgent agent, TaskScheduler turnScheduler) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(messages);
            cancellationToken.ThrowIfCancellationRequested();

            var request = messages as IReadOnlyList<ChatMessage> ?? messages.ToArray();
            return OnTurn(() => agent.RespondAsync(request), turnScheduler);
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            var response = await GetResponseAsync(messages, options, cancellationToken);

            foreach (var update in response.ToChatResponseUpdates())
            {
                yield return update;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
            => serviceType.IsInstanceOfType(this) ? this : null;

        public void Dispose()
        {
        }
    }
}
