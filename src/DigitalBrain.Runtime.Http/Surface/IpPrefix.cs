using System.Net;
using System.Net.Sockets;

namespace DigitalBrain.Runtime.Http.Surface;

// The network a caller's address belongs to (/24 for IPv4, /64 for IPv6), so throttles never keep a full address.
public static class IpPrefix
{
    public const string Unknown = "unknown";

    public static string Truncate(string? address)
    {
        if (!IPAddress.TryParse(address, out var ip))
        {
            return Unknown;
        }

        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        var bytes = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.0/24";
        }

        var masked = new byte[16];
        Array.Copy(bytes, masked, 8);
        return $"{new IPAddress(masked)}/64";
    }
}
