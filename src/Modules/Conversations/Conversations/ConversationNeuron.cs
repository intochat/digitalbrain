using System.Runtime.CompilerServices;
using DigitalBrain.Chat;
using DigitalBrain.AI;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Conversations;

// Seam 5 strangle: grain type "conversation" is the domain-owner HTTP/Fire surface.
// Tip IChat keeps durable storage under the same principal-partitioned instance name
// until Chat dissolves into this module.
[GrainType(IConversation.GrainTypeName)]
public sealed class ConversationNeuron : Neuron, IConversation
{
    private IChat Chat => GrainFactory.GetGrain<IChat>(
        NeuronId.For<IChat>(Id.Owner, Id.Name).ToGrainId());

    public async Task<TurnAccepted> Send(SendConversationMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        await EnsureResponderBoundAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        var accepted = await Chat
            .Send(new SendMessage(message.CommandId, message.Text, message.Actor))
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        return new TurnAccepted(
            new TurnId(accepted.TurnId.Value),
            accepted.CommandId,
            MapStatus(accepted.Status),
            Sequence: 0);
    }

    public async IAsyncEnumerable<ChatResponseUpdate> SendStreaming(
        SendConversationMessage message,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        await EnsureResponderBoundAsync()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await foreach (var update in Chat.SendStreaming(
            new SendMessage(message.CommandId, message.Text, message.Actor),
            cancellationToken))
        {
            yield return update;
        }
    }

    public Task Cancel(CancelConversationTurn command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Chat.Cancel(new CancelTurn(
            command.CommandId,
            new Chat.TurnId(command.TurnId.Value),
            command.Actor,
            command.ExpectedRevision));
    }

    public async Task<ConversationTranscript> Read()
    {
        var snaps = await Chat.ReadTurns()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        var turns = snaps.Select((snap, i) => MapTurn(snap, i + 1L)).ToArray();
        var watermark = turns.Length == 0 ? 0L : turns[^1].Sequence;
        return new ConversationTranscript(turns, watermark);
    }

    public async Task<ConversationTranscript> ReadAfter(long afterSequence, int limit = 64)
    {
        var page = await Read().ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        var bound = limit <= 0 ? 64 : Math.Min(limit, 256);
        var turns = page.Turns.Where(t => t.Sequence > afterSequence).Take(bound).ToArray();
        var watermark = turns.Length == 0 ? page.Watermark : turns[^1].Sequence;
        return new ConversationTranscript(turns, watermark);
    }

    public async Task<IReadOnlyList<ConversationTurnSnapshot>> ReadTurns()
    {
        var snaps = await Chat.ReadTurns()
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        return snaps.Select((snap, index) => MapSnapshot(snap, index + 1L)).ToArray();
    }

    public async Task HandleAsync(ReadConversationTranscript synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        var page = await ReadAfter(synapse.AfterSequence, synapse.Limit)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        await ReplyAsync(page, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    public Task HandleAsync(ConversationNote synapse, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(synapse);
        cancellationToken.ThrowIfCancellationRequested();
        // Strangle: ConversationNote accepted for contract completeness; durable notes stay on tip Chat.
        return Task.CompletedTask;
    }


    // D6 / Seam5 #1: exactly one role:responder per conversation; auto-bind default assistant.
    // Strangle also binds tip IChat (same instance name) so ChatTurnWorker resolves the route.
    private async Task EnsureResponderBoundAsync()
    {
        var graphId = PrincipalGraph.ResolveFor(Id);
        var chatId = NeuronId.For<IChat>(Id.Owner, Id.Name);
        var graph = GrainFactory.GetGrain<ISynapseGraph>(graphId.ToGrainId());

        var chatRoutes = await graph
            .ConnectionsFrom(chatId, ConversationRoles.Responder)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        if (chatRoutes.Count > 1)
        {
            throw new NeuronAuthorizationException(
                $"Conversation '{Id}' has {chatRoutes.Count} role:responder bindings on tip chat '{chatId}' (D6: exactly one).");
        }

        var conversationRoutes = await graph
            .ConnectionsFrom(Id, ConversationRoles.Responder)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        if (conversationRoutes.Count > 1)
        {
            throw new NeuronAuthorizationException(
                $"Conversation '{Id}' has {conversationRoutes.Count} role:responder bindings (D6: exactly one).");
        }

        var assistant = NeuronId.For<IAssistant>(Id.Owner, "assistant");
        if (chatRoutes.Count == 0)
        {
            await SendAsync(
                graphId,
                new Connect(
                    ConversationRoles.ResponderConnectionId(chatId),
                    chatId,
                    ConversationRoles.Responder,
                    assistant,
                    Intent: $"Seam5 auto-bind responder for {chatId}"))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        if (conversationRoutes.Count == 0)
        {
            await SendAsync(
                graphId,
                new Connect(
                    ConversationRoles.ResponderConnectionId(Id),
                    Id,
                    ConversationRoles.Responder,
                    assistant,
                    Intent: $"Seam5 auto-bind responder for {Id}"))
                .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        }

        await FlushOutboxAsync(CancellationToken.None)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
    }

    private static ConversationTurn MapTurn(ChatTurnSnapshot snap, long sequence)
        => new(
            new TurnId(snap.TurnId.Value),
            snap.CommandId,
            Role: "user",
            snap.Text,
            sequence,
            MapStatus(snap.Status));

    private static ConversationTurnSnapshot MapSnapshot(ChatTurnSnapshot snap, long sequence)
        => new(
            new TurnId(snap.TurnId.Value),
            snap.CommandId,
            snap.Text,
            MapStatus(snap.Status),
            sequence,
            snap.ExecutionName);

    private static ConversationTurnStatus MapStatus(ChatTurnStatus status)
        => status switch
        {
            ChatTurnStatus.Pending => ConversationTurnStatus.Queued,
            ChatTurnStatus.Running or ChatTurnStatus.Waiting or ChatTurnStatus.Cancelling
                => ConversationTurnStatus.Running,
            ChatTurnStatus.Completed => ConversationTurnStatus.Completed,
            ChatTurnStatus.Cancelled => ConversationTurnStatus.Cancelled,
            ChatTurnStatus.Failed => ConversationTurnStatus.Failed,
            _ => ConversationTurnStatus.Failed,
        };
}
