namespace DigitalBrain;

public sealed class FastConversationClient(IConversationNeuron conversation)
{
    public Task<ConversationTurnResult> SubmitTurnAsync(ConversationTurnId turnId, string text) =>
        conversation.SubmitTurnAsync(new ConversationTurnRequest(turnId, ConversationRole.Fast, text));
}

public sealed class BalancedConversationClient(IConversationNeuron conversation)
{
    public Task<ConversationTurnResult> SubmitTurnAsync(ConversationTurnId turnId, string text) =>
        conversation.SubmitTurnAsync(new ConversationTurnRequest(turnId, ConversationRole.Balanced, text));
}

public sealed class ReasoningConversationClient(IConversationNeuron conversation)
{
    public Task<ConversationTurnResult> SubmitTurnAsync(ConversationTurnId turnId, string text) =>
        conversation.SubmitTurnAsync(new ConversationTurnRequest(turnId, ConversationRole.Reasoning, text));
}
