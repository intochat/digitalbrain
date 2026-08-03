using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using DigitalBrain.Introspection;
using ModelContextProtocol.Server;

namespace DigitalBrain.OS.AgentTools;

[McpServerToolType]
[SuppressMessage(
    "Performance",
    "CA1812:Avoid uninstantiated internal classes",
    Justification = "Constructed by the MCP server DI container via WithTools<DigitalBrainIntrospectionTools>().")]
internal sealed class DigitalBrainIntrospectionTools(IDigitalBrain brain)
{
    // A directed request is fired, handled and replied through the outbox, so the answer can outlive
    // a single delivery attempt. Waiting on the session journal watch is otherwise unbounded: without
    // a deadline an introspection neuron that never answers hangs the MCP request forever.
    internal static readonly TimeSpan ReplyBound = TimeSpan.FromSeconds(90);

    [McpServerTool(Name = AgentToolEndpoints.ListActiveNeuronsToolName)]
    [Description("List the neurons currently activated in the cluster, with their grain type and identity.")]
    public async Task<IReadOnlyList<ActiveNeuron>> ListActiveNeuronsAsync(
        CancellationToken cancellationToken = default)
    {
        var topology = await BoundedAsync(
            token => brain.Get<IIntrospection>().SendAsync(new ReadTopologyRequest(), token),
            nameof(ReadTopologyRequest),
            cancellationToken);
        if (topology.Error is { } refused)
        {
            throw new InvalidOperationException(refused);
        }

        return [.. topology.Neurons.Select(static neuron => new ActiveNeuron(neuron.GrainType, neuron.Identity))];
    }

    [McpServerTool(Name = AgentToolEndpoints.ReadNeuronJournalToolName)]
    [Description(
        "Read a neuron's durable synapse journal. Returns the causal facts the kernel committed, "
        + "never argument or payload content.")]
    public async Task<NeuronJournalPage> ReadNeuronJournalAsync(
        [Description("Grain type of the neuron, for example 'chat' or 'shell'")] string grainType,
        [Description("Instance name of the neuron, for example 'main'")] string name,
        [Description("Journal direction: incoming or outgoing")] string kind = "outgoing",
        [Description("Read entries after this sequence")] long afterSequence = 0,
        CancellationToken cancellationToken = default)
    {
        var request = new ReadJournalRequest(
            grainType,
            name,
            kind,
            afterSequence,
            ReadJournalRequest.MaximumMaxEntries,
            CommandId.New());
        var page = await BoundedAsync(
            token => brain.Get<IIntrospection>().SendAsync(request, token),
            nameof(ReadJournalRequest),
            cancellationToken);
        if (page.Error is { } refused)
        {
            throw new InvalidOperationException(refused);
        }

        return new NeuronJournalPage(
            page.Subject.ToString(),
            JournalDirection.Parse(page.Direction).ToString(),
            page.ResumeSequence,
            page.Compacted,
            [
                .. page.Entries.Select(static entry => new JournaledSynapse(
                    entry.Sequence,
                    entry.Synapse,
                    entry.Caller,
                    entry.Correlation,
                    entry.Timestamp)),
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

    private static async Task<TResponse> BoundedAsync<TResponse>(
        Func<CancellationToken, Task<TResponse>> request,
        string requestName,
        CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(ReplyBound);

        try
        {
            return await request(deadline.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"The introspection neuron did not answer '{requestName}' within "
                + $"{ReplyBound.TotalSeconds} seconds.");
        }
    }
}
