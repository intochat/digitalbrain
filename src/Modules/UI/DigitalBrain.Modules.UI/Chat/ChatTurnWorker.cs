using System.Text;
using DigitalBrain.Abstractions;
using DigitalBrain.AI;
using DigitalBrain.Chat;
using DigitalBrain.Core;
using Microsoft.Extensions.AI;

namespace DigitalBrain.UI;

// One worker instance per chat (instance name = chat name). Runs the AI attempt for a
// durable turn off the chat's own activation, so Chat stays free to serve reads and card
// deliveries while the call is in flight and without the HTTP observer's cancellation token.
[GrainType(GrainTypeName)]
internal sealed class ChatTurnWorker : Neuron, IChatTurnWorker
{
    internal const string GrainTypeName = "chat-turn-worker";

    protected override bool RegistersWithBrain => false;

    public static NeuronId ForChat(NeuronId chat)
        => new(GrainTypeName, chat.Owner, chat.Name);

    public async Task<ChatTurnResult> RunAsync(ChatTurnGoal goal, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(goal);
        cancellationToken.ThrowIfCancellationRequested();

        var (answer, author) = await RunResponderAsync(goal, cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);
        return new ChatTurnResult(answer, author);
    }

    private async Task<(string Answer, string Author)> RunResponderAsync(
        ChatTurnGoal goal,
        CancellationToken cancellationToken)
    {
        var chat = GrainFactory.GetGrain<IChat>(goal.Chat.ToGrainId());
        var transcript = await chat.Read()
            .WaitAsync(cancellationToken)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var (responder, author) = await ResponderAsync(goal.Chat)
            .ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

        var conversationContext = new ChatMessage(
            ChatRole.System,
            $"This conversation lives in neuron {goal.Chat}. Route cards and notes into it by "
            + $"targeting 'chat:{goal.Chat.Name}' or wiring connections whose target is {goal.Chat}.");

        var answer = new StringBuilder();
        using (VerifiedActor.Enter(goal.Actor))
        {
            await foreach (var chunk in responder.RespondStreaming(
                [conversationContext, .. transcript.Turns.Select(AsChatMessage)],
                cancellationToken).ConfigureAwait(true))
            {
                answer.Append(chunk.Text);
            }
        }

        return (answer.ToString(), author);
    }

    private async Task<(IAgent Responder, string Author)> ResponderAsync(NeuronId chatId)
    {
        using var lookup = new CancellationTokenSource(DeliveryPolicy.ConnectionLookupTimeout);
        try
        {
            var routes = await GrainFactory
                .GetGrain<ISynapseGraph>(ISynapseGraph.ForOwner(chatId.Owner).ToGrainId())
                .ConnectionsFrom(chatId, ChatRoles.Responder)
                .WaitAsync(lookup.Token).ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext);

            if (routes.FirstOrDefault() is { } bound)
            {
                return (
                    GrainFactory.GetGrain<IAgent>(bound.Target.ToGrainId()),
                    bound.Target.Name);
            }

            return (DefaultResponder(chatId.Owner), "assistant");
        }
        catch (OperationCanceledException) when (lookup.IsCancellationRequested)
        {
            return (DefaultResponder(chatId.Owner), "assistant");
        }
    }

    private IAssistant DefaultResponder(OwnerId owner)
        => GrainFactory.GetGrain<IAssistant>(NeuronId.For<IAssistant>(owner, "assistant").ToGrainId());

    private static ChatMessage AsChatMessage(ChatTurn turn)
        => new(turn.FromUser ? ChatRole.User : ChatRole.Assistant, turn.Text);
}
