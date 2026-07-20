using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Kernel;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace DigitalBrain.AI;

[SuppressMessage(
    "Naming",
    "CA1724:Type names should not match namespaces",
    Justification = "Concurrent is the ratified public orchestration vocabulary.")]
public abstract class Concurrent : Neuron, IAgent
{
    protected abstract IReadOnlyList<Participant> Participants { get; }

    protected Participant<TNeuron> Participant<TNeuron>(string? name = null)
        where TNeuron : INeuron
        => new(NeuronId.For<TNeuron>(Id.Owner, name ?? Id.Name));

    public async Task<ChatResponse> RespondAsync(IReadOnlyList<ChatMessage> messages)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var turnScheduler = TaskScheduler.Current;
        var participants = MafParticipantAdapter.CreateAll(GrainFactory, Participants, turnScheduler);
        var workflow = AgentWorkflowBuilder.BuildConcurrent(participants);
        var agent = workflow.AsAIAgent();
        var session = await agent.CreateSessionAsync();
        var response = await agent.RunAsync(messages, session);

        return response.AsChatResponse();
    }
}
