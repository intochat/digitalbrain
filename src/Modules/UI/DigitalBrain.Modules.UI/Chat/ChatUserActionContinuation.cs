using DigitalBrain.Product.Interactions;
using DigitalBrain.Chat;

namespace DigitalBrain.UI;

internal sealed class ChatUserActionContinuation(IGrainFactory grains) : IUserActionContinuation
{
    public async Task<bool> IsWaitingAsync(AgentTurnContext context, string actionId, CancellationToken cancellationToken)
    {
        var turns = await grains.GetGrain<IChat>(context.Chat.ToGrainId()).ReadTurns().WaitAsync(cancellationToken).ConfigureAwait(false);
        return turns.Any(turn => turn.CommandId == context.CommandId && turn.Status == ChatTurnStatus.WaitingForUser && turn.UserAction?.Id == actionId);
    }

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
