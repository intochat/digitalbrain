using System.Net;

namespace DigitalBrain.Auth;

public static class RequestNetwork
{
    public static bool IsLoopback(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var remote = context.Connection.RemoteIpAddress;
        if (remote is null)
        {
            return false;
        }

        if (remote.IsIPv4MappedToIPv6)
        {
            remote = remote.MapToIPv4();
        }

        return IPAddress.IsLoopback(remote);
    }

    public static bool IsSecureTransport(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Request.IsHttps)
        {
            return true;
        }

        var forwarded = context.Request.Headers["X-Forwarded-Proto"].ToString();
        if (string.IsNullOrWhiteSpace(forwarded))
        {
            return false;
        }

        var first = forwarded.Split(',', 2)[0].Trim();
        return string.Equals(first, "https", StringComparison.OrdinalIgnoreCase);
    }
}
