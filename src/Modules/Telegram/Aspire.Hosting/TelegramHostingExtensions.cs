using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using DigitalBrain.Aspire.Hosting;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Telegram.Aspire.Hosting;

public static class TelegramHostingExtensions
{
    public static DigitalBrainModuleBuilder<TelegramModule> WithBot(
        this DigitalBrainModuleBuilder<TelegramModule> module, Action<TelegramHostingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(module);
        var options = new TelegramHostingOptions();
        configure?.Invoke(options);
        module.AddProjection(new TelegramHostingProjection(module.Brain, options));
        return module;
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

public sealed class TelegramHostingOptions
{
    public string? PublicUrl { get; set; }
    public string? MiniAppDirectory { get; set; }
    public string? CloudflaredCommand { get; set; }
}

internal sealed class TelegramHostingProjection(DigitalBrainBuilder brain, TelegramHostingOptions options) : DigitalBrainModuleProjection
{
    private bool _bound;

    public override void Apply<TResource>(IResourceBuilder<TResource> kernel)
    {
        if (_bound) { throw new InvalidOperationException("A Telegram bot must be hosted by exactly one kernel."); }
        _bound = true;
        var builder = brain.ApplicationBuilder;
        if (!builder.Configuration.GetValue("Telegram:Enabled", true)) { return; }
        var token = builder.AddParameter("telegram-bot-token", secret: true)
            .WithDescription("Bot token from Telegram @BotFather. Never sent to the Mini App.");
        var secret = builder.AddParameter("telegram-webhook-secret", new GenerateParameterDefault
        {
            MinLength = 48, Lower = true, Upper = true, Numeric = true, Special = false, MinSpecial = 0,
        }, secret: true, persist: true);
        kernel.WithHttpEndpoint(name: "telegram")
            .WithEnvironment("DigitalBrain__Telegram__PublicPort", kernel.GetEndpoint("telegram").Property(EndpointProperty.TargetPort))
            .WithEnvironment("DigitalBrain__Telegram__BotToken", token)
            .WithEnvironment("DigitalBrain__Telegram__WebhookSecret", secret)
            .WithEnvironment("DigitalBrain__Telegram__AutoConfigure", "true");
        TelegramMiniAppHosting.Configure(kernel, builder, options.MiniAppDirectory);
        var publicUrl = options.PublicUrl ?? builder.Configuration["Telegram:PublicUrl"];
        if (!string.IsNullOrWhiteSpace(publicUrl))
        {
            kernel.WithEnvironment("DigitalBrain__Telegram__PublicUrl", TelegramHostingExtensions.ValidatePublicOrigin(publicUrl));
            return;
        }
        if (!builder.ExecutionContext.IsRunMode)
        {
            throw new InvalidOperationException("Set Telegram:PublicUrl before publishing, or set Telegram:Enabled=false.");
        }
        TelegramCloudflareHosting.Configure(kernel, builder, options.CloudflaredCommand);
    }
}
