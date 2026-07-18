namespace DigitalBrain;

public sealed class FastConversationClient(IConversationNeuron conversation)
{
    public Task<ConversationTurnResult> SubmitTurnAsync(ConversationTurnId turnId, string text) =>
        DigitalBrainClientTelemetry.SubmitTurnAsync(
            conversation,
            turnId,
            ConversationRole.Fast,
            text);
}

public sealed class BalancedConversationClient(IConversationNeuron conversation)
{
    public Task<ConversationTurnResult> SubmitTurnAsync(ConversationTurnId turnId, string text) =>
        DigitalBrainClientTelemetry.SubmitTurnAsync(
            conversation,
            turnId,
            ConversationRole.Balanced,
            text);
}

public sealed class ReasoningConversationClient(IConversationNeuron conversation)
{
    public Task<ConversationTurnResult> SubmitTurnAsync(ConversationTurnId turnId, string text) =>
        DigitalBrainClientTelemetry.SubmitTurnAsync(
            conversation,
            turnId,
            ConversationRole.Reasoning,
            text);
}
