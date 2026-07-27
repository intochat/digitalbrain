using DigitalBrain.AI;
using DigitalBrain.Behaviors;
using DigitalBrain.Chat;
using Microsoft.Extensions.AI;

namespace DigitalBrain.Os;

public sealed class ChatResponder : IIntentProgram<UserMessaged, string>
{
    public const string AssistantName = "assistant";

    public async ValueTask<string> ExecuteAsync(
        UserMessaged request,
        IBehaviorContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();

        var answer = await context.Get<IAssistant>(AssistantName).Respond(
            [.. request.Transcript.Select(AsChatMessage)]);

        return answer.Text;
    }

    private static ChatMessage AsChatMessage(ChatTurn turn)
        => new(turn.FromUser ? ChatRole.User : ChatRole.Assistant, turn.Text);
}
