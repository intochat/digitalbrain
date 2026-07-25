using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Security;
using DigitalBrain.Tasks;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.AI;

public abstract class GroupChat : Neuron, IGroupChat
{
    private const string StateName = "ai.group-chat.session";
    private readonly DirectAgentSession _directSession;

    protected GroupChat()
    {
        _directSession = new DirectAgentSession(
            ServiceProvider.GetRequiredKeyedService<IDurableValue<byte[]>>(StateName),
            ServiceProvider.GetRequiredService<IDurablePayloadProtector>(),
            () => WriteStateAsync(),
            Id);
    }

    protected abstract IReadOnlyList<Participant> Participants { get; }

    protected Participant<TNeuron> Participant<TNeuron>(string? name = null)
        where TNeuron : INeuron
        => new(NeuronId.For<TNeuron>(Id.Owner, name ?? Id.Name));

    public async Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var snapshot = DirectOrchestrationShape.Snapshot(Id, Participants);
        var shape = DirectOrchestrationShape.CreateGroupChat(GetType(), snapshot);
        var agent = shape.CreateAgent(GrainFactory, TaskScheduler.Current);
        return await _directSession.RunAsync(
            agent,
            shape.Definition,
            messages,
            CancellationToken.None);
    }

    public Task Accept(AttemptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        throw new InvalidOperationException(
            $"GroupChat '{Id}' supervised Attempts are not implemented. Use direct {nameof(Respond)}.");
    }

    public Task Continue(AttemptCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        throw new InvalidOperationException(
            $"GroupChat '{Id}' supervised Attempts are not implemented. Use direct {nameof(Respond)}.");
    }

    public Task Cancel(AttemptCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        throw new InvalidOperationException(
            $"GroupChat '{Id}' supervised Attempts are not implemented. Use direct {nameof(Respond)}.");
    }
}
