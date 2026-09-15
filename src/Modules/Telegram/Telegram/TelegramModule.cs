using DigitalBrain.Abstractions.Behaviors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Configuration;

namespace DigitalBrain.Telegram;

public sealed class TelegramModule : Core.IModule
{
    public const string MessageReceivedSignal = "MessageReceived";
    public void Configure(ISiloBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.TryAddSingleton(services => services.GetService<IConfiguration>()?.GetSection("DigitalBrain:Telegram").Get<TelegramOptions>() ?? new());
        builder.Services.TryAddSingleton<TelegramReminderBehavior>();
        builder.Services.TryAddSingleton<ITelegramBehaviorSetup, TelegramBehaviorSetup>();
        builder.Services.AddSingleton<Core.IHttpSurface, TelegramHttpSurface>();
        builder.Services.AddSingleton(new BehaviorSourceContract("telegram", [new(MessageReceivedSignal,
            """{"type":"object","properties":{"eventId":{"type":"string"},"userId":{"type":"integer"},"text":{"type":"string"},"sentUnixSeconds":{"type":"integer"},"timeZone":{"type":"string"}},"required":["eventId","userId","text","sentUnixSeconds","timeZone"],"additionalProperties":false}""")]));
    }
}

public sealed class TelegramOptions
{
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
