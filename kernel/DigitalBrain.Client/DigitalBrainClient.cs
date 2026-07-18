namespace DigitalBrain;

public sealed class DigitalBrainClient(IClusterClient clusterClient, BrainOwnerId owner)
{
    public TNeuron Get<TNeuron>() where TNeuron : INeuron =>
        clusterClient.GetGrain<TNeuron>(owner.Value);

    public ConversationCollection Conversations { get; } = new(clusterClient, owner);
}
