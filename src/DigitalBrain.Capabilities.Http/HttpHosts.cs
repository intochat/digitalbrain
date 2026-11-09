using System.Globalization;

namespace DigitalBrain.Capabilities.Http;

/// <summary>The allow-list check every http@1 implementation applies, so a host is judged the same way everywhere.</summary>
public static class HttpHosts
{
    private static readonly IdnMapping Idn = new();

    /// <summary>
    /// Passes when <paramref name="url"/> is http or https and its host equals an entry of <paramref name="allowed"/> or matches
    /// a <c>*.example.org</c> entry (any depth below <c>example.org</c>, never <c>example.org</c> itself). Hosts and entries are
    /// compared in their IDN (punycode) form, case-insensitively. A malformed entry (<c>*.</c> alone, a wildcard over a single label
    /// such as <c>*.org</c>, a second <c>*</c>, an empty label) matches nothing.
    /// </summary>
    /// <exception cref="HostNotAllowedException">Anything else; its message carries the scheme and host only, never the path or query.</exception>
    public static void Require(Uri url, IReadOnlyList<string> allowed)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(allowed);
        var host = url.IsAbsoluteUri ? url.IdnHost : string.Empty;
        var web = url.IsAbsoluteUri && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);
        if (!web || host.Length == 0 || !allowed.Any(entry => Matches(entry, host)))
        {
            throw new HostNotAllowedException(host.Length == 0 ? "(no host)" : host, url.IsAbsoluteUri ? url.Scheme : null);
        }
    }

    private static bool Matches(string entry, string host)
    {
        if (entry is null)
        {
            return false;
        }

        var wildcard = entry.StartsWith("*.", StringComparison.Ordinal);
        if (Ascii(wildcard ? entry[2..] : entry) is not { } name)
        {
            return false;
        }

        if (wildcard)
        {
            // The suffix needs at least two labels: *.org would allow a whole top-level domain.
            return name.Contains('.', StringComparison.Ordinal)
                && host.Length > name.Length + 1
                && host.EndsWith("." + name, StringComparison.OrdinalIgnoreCase);
        }

        return string.Equals(name, host, StringComparison.OrdinalIgnoreCase);
    }

    // The entry in IDN form, or null when it is not a well-formed host name.
    private static string? Ascii(string name)
    {
        if (name.Length == 0 || name.Contains('*', StringComparison.Ordinal) || name.Split('.').Any(label => label.Length == 0))
        {
            return null;
        }

        try
        {
            return Idn.GetAscii(name);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
