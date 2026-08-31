namespace DigitalBrain.Abstractions.Interactions;

public interface IUserActionContinuation
{
    Task CompleteAsync(AgentTurnContext context, string actionId, bool accepted, CancellationToken cancellationToken);
}
