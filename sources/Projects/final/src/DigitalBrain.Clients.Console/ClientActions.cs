using DigitalBrain.Os.Application;
using DigitalBrain.Protocol.Domain.Events;
using DigitalBrain.Os.Domain.Events;
using DigitalBrain.Hosting.DigitalBrain;
using Orleans;

namespace DigitalBrain.Clients.ConsoleClient;

public static class ClientActions
{
    public static async Task<(string msg, string id)> PackAsync(IClusterClient cluster, string id, string? inoContent, string? description = null)
    {
        if (cluster is null) return ("not connected", id);
        var packed = await cluster.GetGrain<IPackager>(Brain.WellKnownKey)
            .PackAsync(id, description ?? $"packed via TUI {DateTimeOffset.UtcNow:HH:mm}", "0.1.0", inoContent);
        var producedId = packed.Manifest.Id;
        return ($"packed {producedId} -> {packed.PackagePath}", producedId);
    }

    public static async Task<string> PublishAsync(IDigitalBrain? brain, string id, string? peer)
    {
        if (brain is null) return "no brain";
        await brain.SendAsync(new PublishToMarketplace(id, PeerAddress: peer));
        return $"publishing {id}" + (peer is null ? " on this brain" : $" + push to {peer}");
    }

    public static async Task<(string msg, IReadOnlyList<string> lines)> MarketPeerAsync(IClusterClient? cluster, string? peerAddress, string? lastPeer)
    {
        if (peerAddress is null)
        {
            if (cluster is null) return ("not connected", Array.Empty<string>());
            var listings = await cluster.GetGrain<IMarketplace>(Brain.WellKnownKey).ListAsync();
            var lines = listings.Count == 0
                ? new[] { "(nothing published yet — pack then publish)" }
                : listings.Select(l => $"{l.Manifest.Id} v{l.Manifest.Version} • {l.SizeBytes} bytes • {l.PublishedAt:HH:mm:ss}").ToArray();
            return ($"local: {listings.Count} listings", lines);
        }

        try
        {
            await using var peer = await MarketplacePeer.ConnectAsync(peerAddress);
            var listings = await MarketplacePeer.MarketplaceOf(peer).ListAsync();
            var lines = listings.Count == 0
                ? new[] { $"{peerAddress}: no listings yet" }
                : listings.Select(l => $"{l.Manifest.Id} v{l.Manifest.Version} • {l.Manifest.Description} • by {l.Manifest.Author}").ToArray();
            return ($"peer {peerAddress}: {listings.Count} listings", lines);
        }
        catch (Exception ex)
        {
            return ($"peer {peerAddress} failed: {ex.Message}", new[] { "connect error" });
        }
    }

    public static async Task<string> InstallAsync(IClusterClient cluster, string id, string? explicitPeer, string? lastPeer)
    {
        if (cluster is null) return "not connected";
        var marketplace = cluster.GetGrain<IMarketplace>(Brain.WellKnownKey);
        var peer = explicitPeer ?? lastPeer;
        var downloaded = peer is null
            ? await marketplace.InstallListedAsync(id)
            : await marketplace.InstallFromPeerAsync(peer, id);
        return $"installed {id} (hash verified: {downloaded.HashVerified})";
    }
}
