namespace DigitalBrain.Capabilities.Http;

/// <summary>The allow-list check every http@1 implementation applies, so a host is judged the same way everywhere.</summary>
public static class HttpHosts
{
    /// <summary>
    /// Passes when <paramref name="url"/> is http or https and its host equals an entry of <paramref name="allowed"/> or matches
    /// a <c>*.example.org</c> entry (any depth below <c>example.org</c>, never <c>example.org</c> itself). Case-insensitive.
    /// </summary>
    /// <exception cref="HostNotAllowedException">Anything else.</exception>
    public static void Require(Uri url, IReadOnlyList<string> allowed)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(allowed);
        var host = url.IsAbsoluteUri ? url.Host : string.Empty;
        var web = url.IsAbsoluteUri && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);
        if (!web || host.Length == 0 || !allowed.Any(entry => Matches(entry, host)))
        {
            throw new HostNotAllowedException(host.Length == 0 ? url.ToString() : host);
        }
    }

    private static bool Matches(string entry, string host)
    {
        if (entry.StartsWith("*.", StringComparison.Ordinal))
        {
            return host.Length > entry.Length - 1 && host.EndsWith(entry.AsSpan(1), StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(entry, host, StringComparison.OrdinalIgnoreCase);
    }
}
