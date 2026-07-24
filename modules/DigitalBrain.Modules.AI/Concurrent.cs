using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using DigitalBrain.Security;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Journaling;

namespace DigitalBrain.AI;

[SuppressMessage(
    "Naming",
    "CA1724:Type names should not match namespaces",
    Justification = "Concurrent is the ratified public orchestration vocabulary.")]
public abstract class Concurrent : Neuron, IAgent
{
    private const string StateName = "ai.concurrent.session";
    private readonly DirectAgentSession _directSession;

    protected Concurrent()
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

    public async Task<ChatResponse> RespondAsync(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var snapshot = OrchestrationParticipants.Snapshot(Id, Participants);
        var shape = DirectOrchestrationShape.CreateConcurrent(GetType(), snapshot);
        var agent = shape.CreateAgent(GrainFactory, TaskScheduler.Current);

        return await _directSession.RunAsync(
            agent,
            shape.Definition,
            messages,
            CancellationToken.None);
    }
}
