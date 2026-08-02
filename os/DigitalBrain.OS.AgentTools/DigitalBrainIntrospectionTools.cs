using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using ModelContextProtocol.Server;

namespace DigitalBrain.OS.AgentTools;

[McpServerToolType]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Constructed by the MCP server DI container via WithTools<DigitalBrainIntrospectionTools>().")]
internal sealed class DigitalBrainIntrospectionTools(IDigitalBrain brain, IGrainFactory grains)
{
    [McpServerTool(Name = AgentToolEndpoints.ListActiveNeuronsToolName)]
    [Description("List the neurons currently activated in the cluster, with their grain type and identity.")]
    public async Task<IReadOnlyList<ActiveNeuron>> ListActiveNeuronsAsync()
    {
        var statistics = await grains
            .GetGrain<IManagementGrain>(0)
            .GetDetailedGrainStatistics();

        return
        [
            .. statistics
                .Where(statistic => statistic.GrainId.Key.ToString()!
                    .StartsWith($"{brain.Owner.Value}/", StringComparison.Ordinal))
                .Select(statistic => new ActiveNeuron(
                    statistic.GrainId.Type.ToString()!,
                    statistic.GrainId.Key.ToString()!))
                .OrderBy(neuron => neuron.GrainType, StringComparer.Ordinal)
                .ThenBy(neuron => neuron.Identity, StringComparer.Ordinal),
        ];
    }

    [McpServerTool(Name = AgentToolEndpoints.ReadNeuronJournalToolName)]
    [Description(
        "Read a neuron's durable synapse journal. Returns the causal facts the kernel committed, "
        + "never argument or payload content.")]
    public async Task<NeuronJournalPage> ReadNeuronJournalAsync(
        [Description("Grain type of the neuron, for example 'chat' or 'shell'")] string grainType,
        [Description("Instance name of the neuron, for example 'main'")] string name,
        [Description("Journal direction: incoming or outgoing")] string kind = "outgoing",
        [Description("Read entries after this sequence")] long afterSequence = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grainType);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);

        var direction = Enum.Parse<JournalKind>(kind, ignoreCase: true);
        var neuron = new NeuronId(grainType, brain.Owner, name);
        var session = grains.GetGrain<ISessionNeuron>(ISessionNeuron.ForOwner(brain.Owner).ToGrainId());

        var read = await session.ReadNeuronJournal(neuron, direction, afterSequence);

        return new NeuronJournalPage(
            neuron.ToString(),
            direction.ToString(),
            read.ResumeSequence,
            read.ResetSnapshot is not null,
            [
                .. read.Delta.Select(delivery => new JournaledSynapse(
                    delivery.Sequence,
                    delivery.Synapse.GetType().Name,
                    delivery.Caller.ToString(),
                    delivery.CorrelationId.ToString(),
                    delivery.Timestamp)),
            ]);
    }

    [McpServerTool(Name = AgentToolEndpoints.ReadChatTranscriptToolName)]
    [Description("Read the durable transcript of a conversation as the owner would see it.")]
    public async Task<ChatTranscriptPage> ReadChatTranscriptAsync(
        [Description("Conversation name, for example 'main'")] string chatName = "main")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatName);

        var transcript = await brain.GetGrainProxy<IChat>(chatName).Read();

        return new ChatTranscriptPage(
            chatName,
            [
                .. transcript.Turns.Select(turn => new ChatTranscriptTurn(turn.FromUser ? "you" : "brain", turn.Text)),
            ]);
    }
}
