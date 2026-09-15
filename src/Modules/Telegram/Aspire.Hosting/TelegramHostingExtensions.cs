using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Telegram.Aspire.Hosting;

public static class TelegramHostingExtensions
{
    public static IResourceBuilder<ProjectResource> WithTelegramBot<TGateway>(this IResourceBuilder<ProjectResource> kernel)
        where TGateway : IProjectMetadata, new()
    {
        ArgumentNullException.ThrowIfNull(kernel);
        var builder = kernel.ApplicationBuilder;
        if (!builder.Configuration.GetValue("Telegram:Enabled", true)) { return kernel; }
        var token = builder.AddParameter("telegram-bot-token", secret: true)
            .WithDescription("Bot token from Telegram @BotFather. Stored as a secret; never sent to the Mini App.");
        var secret = builder.AddParameter("telegram-webhook-secret", new GenerateParameterDefault
        {
            MinLength = 48, Lower = true, Upper = true, Numeric = true, Special = false, MinSpecial = 0,
        }, secret: true, persist: true);
        kernel.WithEnvironment("DigitalBrain__Telegram__BotToken", token)
            .WithEnvironment("DigitalBrain__Telegram__WebhookSecret", secret)
            .WithEnvironment("DigitalBrain__Telegram__AutoConfigure", "true");

        var gateway = builder.AddProject<TGateway>("telegram-gateway")
            .WithHttpEndpoint(name: "http")
            .WithExternalHttpEndpoints()
            .WithEnvironment("TelegramGateway__KernelUrl", kernel.GetEndpoint("http"));
        // Endpoint references need allocation, not readiness. The gateway must run while the kernel waits for its public origin.
        if (builder.ExecutionContext.IsRunMode)
        {
            var package = Path.GetFullPath(Path.Combine(builder.AppHostDirectory, "../../Modules/UI/Flutter/telegram"));
            var bundle = Path.Combine(package, "build", "web");
            kernel.WithEnvironment("DigitalBrain__Telegram__MiniAppRoot", bundle);
            if (!File.Exists(Path.Combine(bundle, "index.html")) || !File.Exists(Path.Combine(bundle, "main.dart.js")))
            {
                var flutter = builder.AddExecutable("telegram-miniapp-build", TelegramExecutables.Resolve("flutter", builder.Configuration["DigitalBrain:FlutterCommand"]), package,
                    "build", "web", "--release", "--base-href", "/telegram/app/");
                kernel.WaitForCompletion(flutter);
            }
        }

        var publicUrl = builder.Configuration["Telegram:PublicUrl"];
        if (!string.IsNullOrWhiteSpace(publicUrl))
        {
            kernel.WithEnvironment("DigitalBrain__Telegram__PublicUrl", ValidatePublicOrigin(publicUrl));
            return kernel;
        }
        if (!builder.ExecutionContext.IsRunMode)
        {
            throw new InvalidOperationException("Set Telegram:PublicUrl to the gateway's public HTTPS origin before publishing, or set Telegram:Enabled=false.");
        }
        var logDirectory = Path.Combine(Path.GetTempPath(), "digitalbrain-telegram-tunnels");
        Directory.CreateDirectory(logDirectory);
        var logPath = Path.Combine(logDirectory, $"{Guid.NewGuid():N}.log");
        builder.AddExecutable("telegram-tunnel", TelegramExecutables.Resolve("cloudflared", builder.Configuration["Telegram:CloudflaredCommand"]), builder.AppHostDirectory,
                "tunnel", "--no-autoupdate", "--loglevel", "info", "--logfile", logPath)
            .WithArgs(context =>
            {
                context.Args.Add("--url");
                context.Args.Add(gateway.GetEndpoint("http"));
            })
            .WaitForStart(gateway);
        kernel.WithEnvironment(async context =>
        {
            context.EnvironmentVariables["DigitalBrain__Telegram__PublicUrl"] =
                await TelegramTunnelLog.WaitForOriginAsync(logPath, TimeSpan.FromSeconds(90), context.CancellationToken).ConfigureAwait(false);
        });
        return kernel;
    }

    public static string ValidatePublicOrigin(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.IsLoopback ||
            uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0 || uri.UserInfo.Length != 0)
        {
            throw new ArgumentException("Telegram:PublicUrl must be a public HTTPS origin with no path, query, or credentials.", nameof(value));
        }
        return uri.GetLeftPart(UriPartial.Authority);
    }
}
