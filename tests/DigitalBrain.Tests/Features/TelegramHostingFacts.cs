using DigitalBrain.Telegram.Aspire.Hosting;
using Xunit;

namespace DigitalBrain.Tests;

public sealed class TelegramHostingFacts
{
    [Fact]
    public void Tunnel_origin_uses_latest_announcement_and_never_uses_localhost_or_deceptive_hosts()
    {
        Assert.Equal("https://new-public.trycloudflare.com", TelegramTunnelLog.ReadOrigin("https://old-public.trycloudflare.com\nhttps://new-public.trycloudflare.com |"));
        Assert.Null(TelegramTunnelLog.ReadOrigin("https://api.trycloudflare.com\nhttp://localhost:5080\nhttps://safe.trycloudflare.com.evil.test"));
        Assert.Equal("https://bot.example.test", TelegramHostingExtensions.ValidatePublicOrigin("https://bot.example.test/"));
        Assert.Throws<ArgumentException>(() => TelegramHostingExtensions.ValidatePublicOrigin("https://localhost:5080"));
        Assert.Throws<ArgumentException>(() => TelegramHostingExtensions.ValidatePublicOrigin("https://bot.example.test/private"));
    }

    [Fact]
    public async Task Missing_tunnel_log_times_out_instead_of_falling_back_to_localhost()
    {
        var log = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".log");
        await Assert.ThrowsAsync<TimeoutException>(() => TelegramTunnelLog.WaitForOriginAsync(log, TimeSpan.FromMilliseconds(1), TestContext.Current.CancellationToken));
    }

}
