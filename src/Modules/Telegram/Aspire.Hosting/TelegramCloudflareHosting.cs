using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace DigitalBrain.Telegram.Aspire.Hosting;

internal static class TelegramCloudflareHosting
{
    public static void ConfigureNamed(IDistributedApplicationBuilder builder, string? command, IResource parent)
    {
        var token = builder.AddParameter("cloudflare-tunnel-token", secret: true)
            .WithDescription("Token for a remotely managed Cloudflare tunnel. Configure its hostname to forward to the Telegram public port.");
        builder.AddExecutable("telegram-tunnel", TelegramExecutables.Resolve("cloudflared", command ?? builder.Configuration["Telegram:CloudflaredCommand"]),
                builder.AppHostDirectory, "tunnel", "--no-autoupdate", "run")
            .WithEnvironment("TUNNEL_TOKEN", token)
            .WithParentRelationship(parent);
    }

    public static void Configure<TResource>(IResourceBuilder<TResource> kernel, IDistributedApplicationBuilder builder, string? command, IResource parent)
        where TResource : IResourceWithEnvironment, IResourceWithEndpoints
    {
        var directory = Path.Combine(Path.GetTempPath(), "digitalbrain-telegram-tunnels");
        Directory.CreateDirectory(directory);
        var logPath = Path.Combine(directory, $"{Guid.NewGuid():N}.log");
        builder.AddExecutable("telegram-tunnel", TelegramExecutables.Resolve("cloudflared", command ?? builder.Configuration["Telegram:CloudflaredCommand"]), builder.AppHostDirectory,
                "tunnel", "--no-autoupdate", "--loglevel", "info", "--logfile", logPath)
            .WithParentRelationship(parent)
            .WithArgs(context =>
            {
                context.Args.Add("--url");
                context.Args.Add(kernel.GetEndpoint("telegram"));
            });
        // No readiness dependency: cloudflared establishes the tunnel before the kernel listener starts.
        kernel.WithEnvironment(async context =>
        {
            context.EnvironmentVariables["DigitalBrain__Telegram__PublicUrl"] =
                await TelegramTunnelLog.WaitForOriginAsync(logPath, TimeSpan.FromSeconds(90), context.CancellationToken).ConfigureAwait(false);
        });
    }
}
