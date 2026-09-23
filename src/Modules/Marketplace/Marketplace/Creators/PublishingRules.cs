using System.Security.Cryptography;
using System.Text;
using DigitalBrain.Apps;

namespace DigitalBrain.Marketplace.Creators;

// Re-consent is a pure comparison between two published versions: only growth counts. Removing a
// permission or lowering a price never asks the publisher again.
public static class PublishingRules
{
    public static ConsentChange? DetectGrowth(AppManifest previous, AppManifest next)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);

        var previousPermissions = previous.Permissions
            .Select(PermissionKey)
            .ToHashSet(StringComparer.Ordinal);
        var added = next.Permissions
            .Where(permission => !previousPermissions.Contains(PermissionKey(permission)))
            .Select(permission => permission.SemanticTypeId)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(typeId => typeId, StringComparer.Ordinal)
            .ToArray();

        var previousPrices = previous.Meters.ToDictionary(
            meter => meter.MeterId,
            meter => meter.ProposedPriceInCompute ?? 0m,
            StringComparer.Ordinal);
        var increases = next.Meters
            .Select(meter => new PriceIncrease
            {
                MeterId = meter.MeterId,
                From = previousPrices.TryGetValue(meter.MeterId, out var before) ? before : 0m,
                To = meter.ProposedPriceInCompute ?? 0m,
            })
            .Where(increase => increase.To > increase.From)
            .OrderBy(increase => increase.MeterId, StringComparer.Ordinal)
            .ToArray();

        if (added.Length == 0 && increases.Length == 0) { return null; }
        return new ConsentChange
        {
            AddedPermissions = added,
            PriceIncreases = increases,
            Fingerprint = Fingerprint(added, increases),
        };
    }

    public static bool Accepts(ConsentChange change, string? fingerprint)
    {
        ArgumentNullException.ThrowIfNull(change);
        return fingerprint is not null && string.Equals(fingerprint, change.Fingerprint, StringComparison.Ordinal);
    }

    public static string Fingerprint(IReadOnlyList<string> addedPermissions, IReadOnlyList<PriceIncrease> increases)
    {
        var canonical = new StringBuilder();
        foreach (var permission in addedPermissions) { canonical.Append("perm:").Append(permission).Append(';'); }
        foreach (var increase in increases)
        {
            canonical.Append("meter:").Append(increase.MeterId).Append('=')
                .Append(increase.From.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append("->")
                .Append(increase.To.ToString(System.Globalization.CultureInfo.InvariantCulture)).Append(';');
        }
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString())));
    }

    // A permission is identified by its type and read/write direction; adding write to a type that
    // was read-only is growth.
    internal static string PermissionKey(AppPermission permission) =>
        permission.SemanticTypeId + (permission.Write ? ":write" : ":read");
}
