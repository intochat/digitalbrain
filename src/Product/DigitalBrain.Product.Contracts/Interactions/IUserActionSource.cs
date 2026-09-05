using DigitalBrain.Abstractions.Identity;
using DigitalBrain.Product.Identity;

namespace DigitalBrain.Product.Interactions;

public interface IUserActionSource
{
    UserActionRequest? Find(OwnerId owner, CommandId commandId);
    SpecialistContinuation? ResolveSpecialistContinuation(AgentTurnContext context, string actionId) => null;
    void Cancel(AgentTurnContext context) { }
}
