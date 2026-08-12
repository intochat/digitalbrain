using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Conversations;
using DigitalBrain.Client;
using DigitalBrain.Core;
using DigitalBrain.Introspection;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

[McpServerToolType]
internal sealed class IntrospectionTools(IDigitalBrain brain, IHttpContextAccessor httpContextAccessor)
{
    internal static readonly TimeSpan ReplyBound = TimeSpan.FromSeconds(90);

    [McpServerTool(Name = McpSurface.ListActiveNeurons)]
    [Description("List the neurons currently activated in the cluster, with their grain type and identity.")]
    public async Task<IReadOnlyList<ActiveNeuron>> ListActiveNeuronsAsync(
        CancellationToken cancellationToken = default)
    {
        var actor = McpActor.Require(httpContextAccessor);
        using var _ = VerifiedActor.Enter(actor);

        var topology = await BoundedAsync(
            token => brain.Get<IIntrospection>().FireAsync(new ReadTopologyRequest(), token),
            nameof(ReadTopologyRequest),
            cancellationToken);

        return [.. topology.Neurons.Select(static neuron => new ActiveNeuron(neuron.GrainType, neuron.Identity))];
    }

    [McpServerTool(Name = McpSurface.ReadNeuronJournal)]
    [Description(
        "Read a neuron's durable synapse journal. Returns the causal facts the kernel committed, "
        + "never argument or payload content.")]
    public async Task<NeuronJournalPage> ReadNeuronJournalAsync(
        [Description("Grain type of the neuron, for example 'chat' or 'shell'")] string grainType,
        [Description("Instance name of the neuron, for example a principal-partitioned chat id")] string name,
        [Description("Journal direction: incoming or outgoing")] string kind = "outgoing",
        [Description("Read entries after this sequence")] long afterSequence = 0,
        CancellationToken cancellationToken = default)
    {
        var actor = McpActor.Require(httpContextAccessor);
        using var _ = VerifiedActor.Enter(actor);

        var request = new ReadJournalRequest(
            grainType,
            name,
            kind,
            afterSequence,
            ReadJournalRequest.MaximumMaxEntries,
            CommandId.New());
        var page = await BoundedAsync(
            token => brain.Get<IIntrospection>().FireAsync(request, token),
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

    [McpServerTool(Name = McpSurface.ReadChatTranscript)]
    [Description("Read the durable transcript of a conversation owned by the authenticated caller.")]
    public async Task<ChatTranscriptPage> ReadChatTranscriptAsync(
        [Description("Conversation local name, for example 'main'")] string chatName = "main",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatName);

        var actor = McpActor.Require(httpContextAccessor);
        using var _ = VerifiedActor.Enter(actor);
        var conversationInstance = McpActor.Partition(actor, chatName);

        var page = await BoundedAsync(
            async token =>
            {
                token.ThrowIfCancellationRequested();
                return await brain.GetGrainProxy<IConversation>(conversationInstance).Read();
            },
            nameof(IConversation.Read),
            cancellationToken);

        return new ChatTranscriptPage(
            chatName,
            [
                .. page.Turns.Select(turn => new ChatTranscriptTurn(
                    string.Equals(turn.Role, "user", StringComparison.OrdinalIgnoreCase) ? "you" : "brain",
                    turn.Text)),
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
