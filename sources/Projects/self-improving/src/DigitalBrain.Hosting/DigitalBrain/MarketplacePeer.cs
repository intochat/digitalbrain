using DigitalBrain.Os.Application;

namespace DigitalBrain.Hosting.DigitalBrain;

public static class MarketplacePeer
{
    public const string DefaultWorldId = "root";

    public static (string WorldId, string GatewayAddress) Parse(string peer)
    {
        var at = peer.IndexOf('@');
        return at > 0
            ? (peer[..at], peer[(at + 1)..])
            : (DefaultWorldId, peer);
    }

    public static async Task<IDigitalBrainClient> ConnectAsync(string peer, CancellationToken cancellationToken = default)
    {
        var (worldId, gateway) = Parse(peer);
        var client = await DigitalBrainLauncher.LaunchAsync(new DigitalBrainStartOptions
        {
            Mode = DigitalBrainLaunchMode.ConnectExisting,
            WorldId = worldId,
            GatewayAddress = gateway
        }, cancellationToken);

        if (client.ClusterClient is null)
        {
            await client.DisposeAsync();
            throw new InvalidOperationException($"Could not connect to peer brain '{peer}' (world '{worldId}', gateway '{gateway}'). Is its silo running and the gateway reachable?");
        }

        return client;
    }

    public static IMarketplace MarketplaceOf(IDigitalBrainClient client) =>
        client.ClusterClient!.GetGrain<IMarketplace>(Brain.WellKnownKey);
}
