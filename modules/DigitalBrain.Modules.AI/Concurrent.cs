using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

[SuppressMessage(
    "Naming",
    "CA1724:Type names should not match namespaces",
    Justification = "Concurrent is the ratified public orchestration vocabulary.")]
public abstract class Concurrent : Neuron, IAgent
{
    private readonly DirectAgentSession _directSession;

    protected Concurrent()
    {
        _directSession = DirectAgentSession.Create(
            ServiceProvider,
            "ai.concurrent.session",
            () => WriteStateAsync(),
            Id);
    }

    protected abstract IReadOnlyList<Participant> Participants { get; }

    protected Participant<TNeuron> Participant<TNeuron>(string? name = null)
        where TNeuron : INeuron
        => new(NeuronId.For<TNeuron>(Id.Owner, name ?? Id.Name));

    public Task<ChatResponse> Respond(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var snapshot = DirectOrchestrationShape.Snapshot(Id, Participants);
        var shape = DirectOrchestrationShape.CreateConcurrent(GetType(), snapshot);
        var agent = shape.CreateAgent(GrainFactory, TaskScheduler.Current);

        return _directSession.RunAsync(
            agent,
            shape.Definition,
            messages,
            CancellationToken.None);
    }
}
