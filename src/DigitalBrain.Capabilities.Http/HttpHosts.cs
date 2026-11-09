using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace DigitalBrain.Capabilities.Http;

/// <summary>The allow-list check every http@1 implementation applies, so a host is judged the same way everywhere.</summary>
public static class HttpHosts
{
    private static readonly IdnMapping Idn = new();

    /// <summary>
    /// Passes when <paramref name="url"/> is http or https, its host is public (never private, loopback, or link-local),
    /// and its host equals an entry of <paramref name="allowed"/> or matches a <c>*.example.org</c> entry.
    /// Hosts and entries are compared in their IDN (punycode) form, case-insensitively.
    /// </summary>
    /// <exception cref="HostNotAllowedException">Anything else; its message carries the scheme and host only, never the path or query.</exception>
    public static void Require(Uri url, IReadOnlyList<string> allowed)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(allowed);
        var host = url.IsAbsoluteUri ? url.IdnHost : string.Empty;
        var web = url.IsAbsoluteUri && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps);
        if (!web || host.Length == 0)
        {
            throw new HostNotAllowedException(host.Length == 0 ? "(no host)" : host, url.IsAbsoluteUri ? url.Scheme : null);
        }

        if (IsPrivateOrLoopback(host))
        {
            throw new HostNotAllowedException(host, url.IsAbsoluteUri ? url.Scheme : null);
        }

        if (!allowed.Any(entry => Matches(entry, host)))
        {
            throw new HostNotAllowedException(host, url.IsAbsoluteUri ? url.Scheme : null);
        }
    }

    /// <summary>Determines whether a host name or IP string is private, loopback, or link-local.</summary>
    public static bool IsPrivateOrLoopback(string host)
    {
        if (string.IsNullOrWhiteSpace(host))
        {
            return true;
        }

        var clean = host.Trim('[', ']');
        if (string.Equals(clean, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (IPAddress.TryParse(clean, out var ip))
        {
            return !IsPublic(ip);
        }

        return false;
    }

    /// <summary>Determines whether an IP address is private, loopback, or link-local.</summary>
    public static bool IsPrivateOrLoopback(IPAddress address) => !IsPublic(address);

    /// <summary>Determines whether an IP address is routable public Internet space.</summary>
    public static bool IsPublic(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
        {
            return false;
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return !(bytes[0] is 0 or 10 or 127 || bytes[0] >= 224
                || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 100 && (bytes[1] & 0xC0) == 64)
                || (bytes[0] == 198 && (bytes[1] & 0xFE) == 18)
                || (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31)
                || (bytes[0] == 192 && bytes[1] == 168));
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast || (bytes[0] & 0xFE) == 0xFC);
        }

        return false;
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
