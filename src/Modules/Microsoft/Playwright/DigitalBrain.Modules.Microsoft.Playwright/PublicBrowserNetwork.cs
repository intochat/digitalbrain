using System.Net;
using System.Net.Sockets;
namespace DigitalBrain.Microsoft.Playwright;
internal static class PublicBrowserNetwork
{
    internal static Uri PublicUri(string url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 2048 || !Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)
            || !uri.IsDefaultPort || uri.IsLoopback || string.IsNullOrWhiteSpace(uri.IdnHost))
        {
            throw new ArgumentException("Web browsing accepts only public HTTP(S) URLs on their standard ports, without credentials.", nameof(url));
        }

        var host = uri.IdnHost.TrimEnd('.');
        if (host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || !host.Contains('.', StringComparison.Ordinal) && !IPAddress.TryParse(host, out _)
            || IPAddress.TryParse(host, out var address) && !IsPublic(address))
            { throw new ArgumentException("Private network addresses are unavailable.", nameof(url)); }
        return uri;
    }

    internal static async Task<IPAddress[]> ResolvePublicAsync(string host, CancellationToken cancellationToken)
    {
        var normalized = host.TrimEnd('.');
        if (normalized.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || !normalized.Contains('.', StringComparison.Ordinal) && !IPAddress.TryParse(normalized, out _))
        {
            throw new ArgumentException("Local network names are unavailable to web browsing.", nameof(host));
        }

        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken).ConfigureAwait(false);
        if (addresses.Length == 0 || addresses.Any(static address => !IsPublic(address)))
        {
            throw new ArgumentException("Web browsing cannot access private, loopback, link-local, multicast, or reserved network addresses.", nameof(host));
        }

        return addresses;
    }

    internal static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            return bytes[0] is not (0 or 10 or 127) && bytes[0] < 224
                && !(bytes[0] == 100 && bytes[1] is >= 64 and <= 127)
                && !(bytes[0] == 169 && bytes[1] == 254)
                && !(bytes[0] == 172 && bytes[1] is >= 16 and <= 31)
                && !(bytes[0] == 192 && (bytes[1] == 168 || bytes[1] == 0 && bytes[2] is 0 or 2
                    || bytes[1] == 88 && bytes[2] == 99))
                && !(bytes[0] == 198 && (bytes[1] is 18 or 19 || bytes[1] == 51 && bytes[2] == 100))
                && !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
        }

        // Only globally routed IPv6 unicast; exclude transition/documentation allocations.
        return address.AddressFamily == AddressFamily.InterNetworkV6 && (bytes[0] & 0xe0) == 0x20
            && !(bytes[0] == 0x20 && bytes[1] == 0x02)
            && !(bytes[0] == 0x20 && bytes[1] == 0x01 && (bytes[2] < 2 || bytes[2] == 0x0d && bytes[3] == 0xb8))
            && !(bytes[0] == 0x3f && bytes[1] == 0xff && (bytes[2] & 0xf0) == 0);
    }

    internal static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var addresses = await ResolvePublicAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);
        SocketException? lastError = null;
        foreach (var address in addresses)
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                // Connect to the validated address itself, so DNS cannot change between validation and connection.
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException exception)
            {
                socket.Dispose();
                lastError = exception;
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        }

        throw new HttpRequestException("The public website could not be reached.", lastError);
    }


}
