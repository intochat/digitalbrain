using Brain.Contracts;

namespace Brain.Client;

public sealed class DigitalBrainClient(IClusterClient clusterClient, BrainOwnerId owner)
{
    public TNeuron Get<TNeuron>() where TNeuron : INeuron =>
        clusterClient.GetGrain<TNeuron>(owner.Value);
}
