using System.Security.Cryptography;
using System.Text.Json;

namespace DigitalBrain.Kernel.Contracts;

public static class FeatureInstallationReservationDigests
{
    public static string Command(InstallFeatureVersion command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return Digest(command);
    }

    public static string Access(
        FeatureInstallationId installationId,
        ReleaseDigest release,
        IReadOnlyList<FeatureGrantSpec> grants,
        IReadOnlyList<string> subscriptions)
    {
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(subscriptions);
        var payload = new PublicationAccess(
            installationId.Value,
            release.Value,
            grants
                .OrderBy(grant => grant.CapabilityId, StringComparer.Ordinal)
                .ThenBy(grant => grant.CapabilityVersion)
                .Select(grant => new PublicationGrant(
                    grant.CapabilityId,
                    grant.CapabilityVersion,
                    grant.ProviderConnectionId?.Value,
                    grant.ConstraintsJson,
                    grant.Provider))
                .ToArray(),
            subscriptions.Order(StringComparer.Ordinal).ToArray());
        return Digest(payload);
    }

    private static string Digest<T>(T value) =>
        Convert.ToHexStringLower(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));

    private sealed record PublicationAccess(
        string InstallationId,
        string Release,
        PublicationGrant[] Grants,
        string[] Subscriptions);

    private sealed record PublicationGrant(
        string CapabilityId,
        int CapabilityVersion,
        string? ProviderConnectionId,
        string ConstraintsJson,
        string? Provider);
}
