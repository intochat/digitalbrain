namespace DigitalBrain.Conversations;

// D5: Conversations.Contracts owns the provider-neutral responder role; AI implements IAgent.
public interface IConversationResponder
{
    const string RoleName = ConversationRoles.Responder;
}
