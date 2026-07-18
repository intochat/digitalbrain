namespace DigitalBrain;

public sealed class ConversationCollection
{
    private readonly IClusterClient _clusterClient;
    private readonly BrainOwnerId _owner;

    internal ConversationCollection(IClusterClient clusterClient, BrainOwnerId owner)
    {
        _clusterClient = clusterClient;
        _owner = owner;
    }

    public IConversationNeuron Open(ConversationId conversationId) =>
        _clusterClient.GetGrain<IConversationNeuron>(ConversationKey.Encode(_owner, conversationId));
}
