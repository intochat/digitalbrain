using System.ComponentModel;
using DigitalBrain.Abstractions;
using DigitalBrain.Chat;
using DigitalBrain.Client;
using ModelContextProtocol.Server;

namespace DigitalBrain.Mcp;

[McpServerToolType]
internal sealed class IntrospectionTools(IDigitalBrain brain)
{

    internal static readonly TimeSpan ReplyBound = TimeSpan.FromSeconds(90);

    // Matches the old ReadJournalRequest.MaximumMaxEntries bound: worst-case MCP payload
    // stays capped well under the journal's own 512-entry retention.
    private const int MaximumEntries = 200;

    [McpServerTool(Name = McpSurface.ReadNeuronJournal)]
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
        var direction = ParseKind(kind);
        var subject = new NeuronId(grainType, brain.Owner, name);

        var page = await BoundedAsync(
            token => brain.ReadJournalAsync(subject, direction, afterSequence, token),
            nameof(JournalRead),
            cancellationToken);

        JournaledSynapse[] entries =
        [
            .. page.Delta.Take(MaximumEntries).Select(static entry => new JournaledSynapse(
                entry.Sequence,
                entry.Synapse.GetType().Name,
                entry.Caller.ToString(),
                entry.CorrelationId.ToString(),
                entry.Timestamp)),
        ];
        var truncated = page.Delta.Count > entries.Length;

        return new NeuronJournalPage(
            subject.ToString(),
            direction.ToString(),
            truncated ? entries[^1].Sequence : page.ResumeSequence,
            page.ResetSnapshot is not null,
            entries);
    }

    [McpServerTool(Name = McpSurface.ReadChatTranscript)]
    [Description("Read the durable transcript of a conversation as the owner would see it.")]
    public async Task<ChatTranscriptPage> ReadChatTranscriptAsync(
        [Description("Conversation name, for example 'main'")] string chatName = "main",
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(chatName);

        var read = await BoundedAsync(
            token => brain.Get<IChat>().FireAsync(new ReadTranscriptRequest(chatName), token),
            nameof(ReadTranscriptRequest),
            cancellationToken);

        return new ChatTranscriptPage(
            chatName,
            [
                .. read.Transcript.Turns.Select(turn => new ChatTranscriptTurn(
                    turn.FromUser ? "you" : "brain",
                    turn.Text)),
            ]);
    }

    private static JournalKind ParseKind(string? kind)
    {
        if (string.IsNullOrWhiteSpace(kind))
        {
            return JournalKind.Outgoing;
        }

        if (!Enum.TryParse<JournalKind>(kind.Trim(), ignoreCase: true, out var parsed))
        {
            throw new ArgumentException(
                $"Journal direction '{kind}' is not recognised. Use 'incoming' or 'outgoing'.",
                nameof(kind));
        }

        return parsed;
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
                $"'{requestName}' did not answer within {ReplyBound.TotalSeconds} seconds.");
        }
    }
}
