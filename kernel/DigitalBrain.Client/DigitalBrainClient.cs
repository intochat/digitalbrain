using DigitalBrain;

namespace DigitalBrain;

public sealed class DigitalBrainClient(IClusterClient clusterClient, BrainOwnerId owner)
{
    public TNeuron Get<TNeuron>() where TNeuron : INeuron =>
        clusterClient.GetGrain<TNeuron>(owner.Value);
}
