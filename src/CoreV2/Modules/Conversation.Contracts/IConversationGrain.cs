namespace Brain.Modules.Conversation.Contracts;

public interface IConversationGrain : IGrainWithStringKey
{
    Task<ConversationSnapshot> AppendAsync(ConversationAppendRequest request);

    Task<ConversationSnapshot> ReadAsync();
}
