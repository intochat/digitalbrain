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

        var responder = DefaultResponder(goal.Chat.Owner);

        var conversationContext = new ChatMessage(
            ChatRole.System,
            $"This conversation lives in chat '{goal.Chat.Name}'. Send cards and notes by targeting 'chat:{goal.Chat.Name}'.");

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

        return (answer.ToString(), "assistant");
    }

    private IAssistant DefaultResponder(OwnerId owner)
        => GrainFactory.GetGrain<IAssistant>(NeuronId.For<IAssistant>(owner, "assistant").ToGrainId());

    private static ChatMessage AsChatMessage(ChatTurn turn)
        => new(turn.FromUser ? ChatRole.User : ChatRole.Assistant, turn.Text);
}
