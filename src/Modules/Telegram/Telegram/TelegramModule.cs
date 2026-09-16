using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using DigitalBrain.Abstractions.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;
using DigitalBrain.Identity;

namespace DigitalBrain.Telegram;

public sealed class TelegramModule : Core.IModule
{
    public void Configure(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.ServiceProvider.GetRequiredService<TelegramHttpSurface>().Map(endpoints);
        endpoints.MapGet("/agent/connections/telegram", (TelegramConnectionStatus status) => Results.Ok(status.Read()));
    }

    public const string MessageReceivedSignal = "MessageReceived";
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton(services => services.GetService<IConfiguration>()?.GetSection("DigitalBrain:Telegram").Get<TelegramOptions>() ?? new());
        builder.Services.TryAddSingleton<TelegramReminderBehavior>();
        builder.Services.TryAddSingleton<TelegramConnectionStatus>();
        builder.Services.TryAddSingleton(_ => new TelegramBotApi(new HttpClient(new SocketsHttpHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(15), MaxResponseContentBufferSize = 131072
        }));
        builder.Services.TryAddSingleton<TelegramBotConnection>();
        builder.Services.AddHostedService(services => services.GetRequiredService<TelegramBotConnection>());
        builder.Services.TryAddSingleton<ITelegramBehaviorSetup, TelegramBehaviorSetup>();
        builder.Services.TryAddSingleton<TelegramHttpSurface>();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IExternalIdentityVerifier, TelegramIdentityVerifier>());
        var port = builder.Configuration.GetValue<int>("DigitalBrain:Telegram:PublicPort");
        if (port != 0)
        {
            builder.Services.AddSingleton(new Core.ModuleEndpointListener(typeof(TelegramModule), port)
            {
                AdditionalModuleTypes = [typeof(IdentityModule), typeof(DigitalBrain.UI.UIModule)],
            });
        }
        builder.Services.AddSingleton(new BehaviorSourceContract("telegram", [new(MessageReceivedSignal,
            """{"type":"object","properties":{"eventId":{"type":"string"},"userId":{"type":"integer"},"text":{"type":"string"},"sentUnixSeconds":{"type":"integer"},"timeZone":{"type":"string"}},"required":["eventId","userId","text","sentUnixSeconds","timeZone"],"additionalProperties":false}""")]));
    }
}

public sealed class TelegramOptions
{
    public int PublicPort { get; set; }
    public bool AutoConfigure { get; set; }
    public string PublicUrl { get; set; } = "";
    public string BotToken { get; set; } = "";
    public string WebhookSecret { get; set; } = "";
    public string MiniAppUrl { get; set; } = "";
    public string MiniAppRoot { get; set; } = Path.Combine(AppContext.BaseDirectory, "telegram-app");
    public string TimeZone { get; set; } = "UTC";
    public string? DecisionProvider { get; set; }
    public string? DecisionModel { get; set; }
    public bool Enabled => !string.IsNullOrWhiteSpace(BotToken) && BotToken.Length <= 256 &&
        WebhookSecret.Length is >= 1 and <= 256 && WebhookSecret.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-') &&
        IsTimeZoneValid(TimeZone);

    public static bool IsTimeZoneValid(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128) { return false; }
        try { _ = TimeZoneInfo.FindSystemTimeZoneById(value); return true; }
        catch (Exception error) when (error is TimeZoneNotFoundException or InvalidTimeZoneException) { return false; }
    }
}

public interface ITelegramBehaviorSetup
{
    Task EnsureAsync(long userId, CancellationToken cancellationToken);
}
