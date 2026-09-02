using DigitalBrain.Abstractions.Identity;

namespace DigitalBrain.Abstractions.Interactions;

public interface IUserActionSource
{
    UserActionRequest? Find(OwnerId owner, CommandId commandId);
    void Cancel(AgentTurnContext context) { }
}
