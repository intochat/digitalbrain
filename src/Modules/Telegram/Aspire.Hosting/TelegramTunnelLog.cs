using System.Text.RegularExpressions;

namespace DigitalBrain.Telegram.Aspire.Hosting;

/// <summary>Each AppHost run gets a new log; tunnel failure never becomes a localhost webhook.</summary>
public static partial class TelegramTunnelLog
{
    [GeneratedRegex(@"https://[a-z0-9](?:[a-z0-9-]*[a-z0-9])?\.trycloudflare\.com(?![a-zA-Z0-9.:-])", RegexOptions.CultureInvariant)]
    private static partial Regex OriginPattern();

    public static string? ReadOrigin(string content)
    {
        var matches = OriginPattern().Matches(content);
        for (var i = matches.Count - 1; i >= 0; i--)
        {
            var origin = matches[i].Value;
            if (origin is not ("https://api.trycloudflare.com" or "https://www.trycloudflare.com")) { return origin; }
        }
        return null;
    }

    public static async Task<string> WaitForOriginAsync(string logPath, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.Exists(logPath))
                {
                    using var stream = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (stream.Length > 65536) { stream.Seek(-65536, SeekOrigin.End); }
                    using var reader = new StreamReader(stream);
                    var content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
                    if (ReadOrigin(content) is { } origin) { return origin; }
                }
            }
            catch (IOException) { /* cloudflared may be opening its fresh log */ }
            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken).ConfigureAwait(false);
        }
        throw new TimeoutException("Telegram tunnel did not announce a public HTTPS origin within 90 seconds. Check telegram-tunnel, then restart the AppHost; or configure Telegram:PublicUrl.");
    }
}
