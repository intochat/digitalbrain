using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Tasks;
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

    protected Participant<TNeuron> Participant<TNeuron>(string? name = null)
        where TNeuron : INeuron
        => new(NeuronId.For<TNeuron>(Id.Owner, name ?? Id.Name));

    public Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var snapshot = DirectOrchestrationShape.Snapshot(Id, Participants);
        var shape = DirectOrchestrationShape.CreateGroupChat(GetType(), snapshot);
        var agent = shape.CreateAgent(GrainFactory, TaskScheduler.Current);

        return _directSession.RunAsync(agent, shape.Definition, messages, CancellationToken.None);
    }

    public Task Accept(AttemptRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return SupervisedNotImplemented();
    }

    public Task Continue(AttemptCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        return SupervisedNotImplemented();
    }

    public Task Cancel(AttemptCursor cursor)
    {
        ArgumentNullException.ThrowIfNull(cursor);
        return SupervisedNotImplemented();
    }

    private Task SupervisedNotImplemented()
        => throw new InvalidOperationException(
            $"GroupChat '{Id}' supervised Attempts are not implemented. Use direct {nameof(Respond)}.");
}
