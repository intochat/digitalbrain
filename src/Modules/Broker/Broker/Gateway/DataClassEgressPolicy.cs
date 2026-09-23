using DigitalBrain.Apps;

namespace DigitalBrain.Broker;

// Per-data-class egress: a host may be reached only when a rule for one of the call's data classes
// (or the wildcard rule) allows it. The app's own declared endpoint is always reachable so the
// remote call can happen at all. Network is denied by default.
internal sealed class DataClassEgressPolicy(IEnumerable<EgressRule> rules) : IEgressPolicy
{
    private readonly IReadOnlyList<EgressRule> rules = [.. rules];

    public EgressDecision Evaluate(AppManifest manifest, IReadOnlyList<string> dataClasses, IReadOnlyList<string> hosts)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (hosts.Count == 0)
        {
            return EgressDecision.Allow();
        }

        var allowedHosts = rules
            .Where(rule => dataClasses.Contains(rule.DataClass, StringComparer.Ordinal)
                || string.Equals(rule.DataClass, EgressRule.AnyDataClass, StringComparison.Ordinal))
            .SelectMany(rule => rule.AllowedHosts)
            .ToList();
        if (TryGetHost(manifest.RemoteEndpoint) is { } endpoint)
        {
            allowedHosts.Add(endpoint);
        }

        foreach (var host in hosts)
        {
            if (!allowedHosts.Any(allowed => Matches(allowed, host)))
            {
                return EgressDecision.Deny(
                    $"The app may not reach host '{host}' while carrying data class(es) '{string.Join(", ", dataClasses)}'.");
            }
        }

        return EgressDecision.Allow();
    }

    private static bool Matches(string allowed, string host)
    {
        if (string.Equals(allowed, EgressRule.AnyHost, StringComparison.Ordinal))
        {
            return true;
        }
        if (string.Equals(allowed, host, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        return allowed.StartsWith("*.", StringComparison.Ordinal)
            && host.EndsWith(allowed[1..], StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetHost(string? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }
        return Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ? uri.Host : null;
    }
}
