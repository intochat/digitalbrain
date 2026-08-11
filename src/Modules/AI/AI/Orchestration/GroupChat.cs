using System.Runtime.CompilerServices;
using DigitalBrain.Abstractions;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

public abstract class GroupChat : Neuron, IGroupChat
{
    private readonly DirectAgentSession _directSession;

    protected GroupChat()
    {
        _directSession = DirectAgentSession.Create(ServiceProvider, "ai.group-chat.session", () => WriteStateAsync(), Id);
    }

    protected abstract IReadOnlyList<Participant> Participants { get; }

    protected bool HasDurableSession => _directSession.HasDurableSession;

    protected Participant<TNeuron> Participant<TNeuron>(string? name = null)
        where TNeuron : INeuron
        => new(NeuronId.For<TNeuron>(Id.Owner, name ?? Id.Name));

    public Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        return RespondStreaming(messages).ToChatResponseAsync();
    }

    public async IAsyncEnumerable<ChatResponseUpdate> RespondStreaming(
        IReadOnlyList<ChatMessage> messages,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var snapshot = DirectOrchestrationShape.Snapshot(Id, Participants);
        var shape = DirectOrchestrationShape.CreateGroupChat(GetType(), snapshot);
        var agent = shape.CreateAgent(GrainFactory, TaskScheduler.Current);

        await foreach (var update in _directSession
            .RunStreamingAsync(agent, shape.Definition, shape.Invocations, messages, cancellationToken).ConfigureAwait(true))
        {
            yield return update;
        }
    }
}

