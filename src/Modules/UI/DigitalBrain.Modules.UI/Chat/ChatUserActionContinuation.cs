using DigitalBrain.Abstractions.Interactions;
using DigitalBrain.Chat;

namespace DigitalBrain.UI;

internal sealed class ChatUserActionContinuation(IGrainFactory grains) : IUserActionContinuation
{
    public Task CompleteAsync(
        AgentTurnContext context,
        string actionId,
        bool accepted,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(actionId);
        cancellationToken.ThrowIfCancellationRequested();
        return grains.GetGrain<IChat>(context.Chat.ToGrainId())
            .CompleteUserAction(context, actionId, accepted).WaitAsync(cancellationToken);
    }
}
