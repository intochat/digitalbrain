using Microsoft.Extensions.Hosting;

namespace DigitalBrain.Telegram;

/// <summary>Reconciles provider setup only after the kernel's HTTP listener is available.</summary>
public sealed class TelegramBotConnection(
    TelegramOptions options, TelegramBotApi api, TelegramConnectionStatus status,
    IHostApplicationLifetime lifetime) : BackgroundService
{
    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (!options.AutoConfigure)
        {
            status.Update(options.Enabled ? "Manual" : "Disabled", null, null, null);
            return;
        }
        if (string.IsNullOrWhiteSpace(options.BotToken))
        {
            status.Update("MissingToken", null, null, "Enter telegram-bot-token in the Aspire parameter prompt.");
            return;
        }
        if (!options.Enabled)
        {
            status.Update("Failed", null, null, "Telegram webhook secret or timezone is not configured correctly.");
            return;
        }
        if (!TryPublicOrigin(options.PublicUrl, out var origin))
        {
            status.Update("WaitingForPublicUrl", null, null, "Provide a public HTTPS origin or start the Telegram Cloudflare tunnel.");
            return;
        }
        status.Update("Connecting", null, origin!.AbsoluteUri, null);
        try
        {
            var me = await api.GetMeAsync(options.BotToken, cancellationToken).ConfigureAwait(false);
            if (me is null || !me.IsBot || string.IsNullOrWhiteSpace(me.Username))
            {
                throw new InvalidOperationException("The supplied credential did not identify a Telegram bot.");
            }
            var username = me.Username;
            await api.VerifyIngressAsync(origin, options.WebhookSecret, status.InstanceId, cancellationToken).ConfigureAwait(false);
            var webhook = new Uri(origin, "/telegram/webhook").AbsoluteUri;
            var miniApp = new Uri(origin, "/telegram/app/").AbsoluteUri;
            options.MiniAppUrl = miniApp;
            var registered = await api.SetWebhookAsync(options.BotToken, webhook, options.WebhookSecret, cancellationToken).ConfigureAwait(false);
            if (!registered)
            {
                throw new InvalidOperationException("Telegram did not confirm webhook registration.");
            }
            var menu = await api.SetMenuButtonAsync(options.BotToken, miniApp, cancellationToken).ConfigureAwait(false);
            if (!menu)
            {
                throw new InvalidOperationException("Telegram did not confirm the Mini App menu button.");
            }
            await VerifyWebhookAsync(webhook, cancellationToken).ConfigureAwait(false);
            status.Update("Connected", username, origin.AbsoluteUri, null, DateTimeOffset.UtcNow);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            status.Update("Failed", null, origin.AbsoluteUri, "Telegram setup timed out; it will retry.");
        }
        catch (Exception error) when (error is InvalidOperationException or ArgumentException)
        {
            // Only our sanitized errors cross the status boundary; no provider body or token-bearing URI.
            status.Update("Failed", null, origin.AbsoluteUri, error is ArgumentException ? "Telegram returned malformed setup data." : error.Message);
        }
    }

    private async Task VerifyWebhookAsync(string expected, CancellationToken cancellationToken)
    {
        var info = await api.GetWebhookInfoAsync(options.BotToken, cancellationToken).ConfigureAwait(false);
        if (info?.Url != expected)
        {
            throw new InvalidOperationException("Telegram's webhook does not point to this instance. Check whether another application uses the same bot.");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.AutoConfigure)
        {
            await ConnectAsync(stoppingToken).ConfigureAwait(false);
            return;
        }
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = lifetime.ApplicationStarted.Register(() => started.TrySetResult());
        await started.Task.WaitAsync(stoppingToken).ConfigureAwait(false);
        while (!stoppingToken.IsCancellationRequested)
        {
            await ConnectAsync(stoppingToken).ConfigureAwait(false);
            if (status.Read().State == "Connected")
            {
                return;
            }
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken).ConfigureAwait(false);
        }
    }

    public static bool TryPublicOrigin(string value, out Uri? origin)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out origin) && origin.Scheme == "https" && !origin.IsLoopback &&
            string.IsNullOrEmpty(origin.UserInfo) && string.IsNullOrEmpty(origin.Query) && string.IsNullOrEmpty(origin.Fragment) &&
            origin.AbsolutePath == "/")
        {
            return true;
        }
        origin = null;
        return false;
    }
}
