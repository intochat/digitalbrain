using System.Text;
using System.Text.Json;

namespace DigitalBrain.Kernel.Contracts;

internal static class FeaturePublicationManifestCodec
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static byte[] Serialize(BrainOwnerId ownerId, FeaturePublicationTicket ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        var connections = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var grant in ticket.ActiveGrants)
        {
            if (grant.ProviderConnectionId is not { } connection) continue;
            var provider = grant.Provider ?? throw new InvalidOperationException("A provider connection requires a provider key.");
            if (!connections.TryAdd(provider, connection.Value) &&
                !string.Equals(connections[provider], connection.Value, StringComparison.Ordinal))
                throw new InvalidOperationException("One installation cannot bind a provider to multiple connections.");
        }
        return JsonSerializer.SerializeToUtf8Bytes(new FeaturePublicationManifest(
            ownerId.Value,
            ticket.ActorId.Value,
            ticket.InstallationId.Value,
            ticket.Release.Value,
            ticket.GrantRevision.Value,
            connections,
            ticket.PublicationFence,
            ticket.AuthorityDigest,
            ticket.AccessDigest), Json);
    }

    public static string Path(BrainOwnerId ownerId, FeatureInstallationId installationId) =>
        $"active/{Segment(ownerId.Value)}/{Segment(installationId.Value)}.json";

    private static string Segment(string value) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(value)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed record FeaturePublicationManifest(
        string OwnerId,
        string ActorId,
        string InstallationId,
        string ReleaseDigest,
        long GrantRevision,
        IReadOnlyDictionary<string, string> ProviderConnections,
        long PublicationFence,
        string AuthorityDigest,
        string AccessDigest);
}
