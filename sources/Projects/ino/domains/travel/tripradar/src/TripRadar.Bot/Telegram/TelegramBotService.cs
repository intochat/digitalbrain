using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableTypes;

namespace TripRadar.Bot.Telegram;

public sealed record InlineButton(string Text, string CallbackData, string? Url = null);

public interface ITelegramBotService
{
    Task SendMessageAsync(long chatId, string text, CancellationToken ct = default);
    Task SendInlineKeyboardAsync(long chatId, string text, InlineButton[][] buttons, CancellationToken ct = default);
    Task SendMiniAppLaunchAsync(long chatId, string text, string url, string buttonLabel, CancellationToken ct = default);
    Task SendWelcomeWithRegistrationAsync(
        long chatId,
        string text,
        string primaryUrl,
        string primaryButtonLabel,
        string secondaryUrl,
        string secondaryButtonLabel,
        CancellationToken ct = default);
    Task AnswerCallbackQueryAsync(string callbackQueryId, CancellationToken ct = default);
}

internal sealed class TelegramBotService(ITelegramBotClient bot, ILogger<TelegramBotService> logger) : ITelegramBotService
{
    public async Task SendMessageAsync(long chatId, string text, CancellationToken ct = default)
    {
        try
        {
            await bot.SendMessageAsync(chatId, text, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send message to chat {ChatId}", chatId);
        }
    }

    public async Task SendInlineKeyboardAsync(long chatId, string text, InlineButton[][] buttons, CancellationToken ct = default)
    {
        try
        {
            var keyboard = BuildInlineKeyboard(buttons);
            await bot.SendMessageAsync(chatId, text, replyMarkup: keyboard, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send inline keyboard to chat {ChatId}", chatId);
        }
    }

    public async Task SendMiniAppLaunchAsync(long chatId, string text, string url, string buttonLabel, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out _))
        {
            logger.LogWarning("Mini App URL '{Url}' is not a valid absolute URL, skipping send to chat {ChatId}", url, chatId);
            return;
        }

        try
        {
            var button = new InlineKeyboardButton(buttonLabel)
            {
                WebApp = new WebAppInfo(url)
            };
            var keyboard = new InlineKeyboardMarkup(new[] { new[] { button } });
            await bot.SendMessageAsync(chatId, text, replyMarkup: keyboard, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send Mini App launch button to chat {ChatId}", chatId);
        }
    }

    public async Task SendWelcomeWithRegistrationAsync(
        long chatId,
        string text,
        string primaryUrl,
        string primaryButtonLabel,
        string secondaryUrl,
        string secondaryButtonLabel,
        CancellationToken ct = default)
    {
        var primaryValid = Uri.TryCreate(primaryUrl, UriKind.Absolute, out _);
        var secondaryValid = Uri.TryCreate(secondaryUrl, UriKind.Absolute, out _);

        if (!primaryValid && !secondaryValid)
        {
            logger.LogWarning("Welcome message has no valid URLs (primary='{Primary}', secondary='{Secondary}'); skipping send to chat {ChatId}",
                primaryUrl, secondaryUrl, chatId);
            return;
        }

        try
        {
            var buttons = new List<InlineKeyboardButton>();
            if (primaryValid)
                buttons.Add(new InlineKeyboardButton(primaryButtonLabel) { Url = primaryUrl });
            if (secondaryValid)
                buttons.Add(new InlineKeyboardButton(secondaryButtonLabel) { Url = secondaryUrl });

            var keyboard = new InlineKeyboardMarkup(new[] { buttons.ToArray() });
            await bot.SendMessageAsync(chatId, text, replyMarkup: keyboard, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send welcome message with registration buttons to chat {ChatId}", chatId);
        }
    }

    public async Task AnswerCallbackQueryAsync(string callbackQueryId, CancellationToken ct = default)
    {
        try
        {
            await bot.AnswerCallbackQueryAsync(callbackQueryId, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to answer callback query {CallbackQueryId}", callbackQueryId);
        }
    }

    private static InlineKeyboardMarkup BuildInlineKeyboard(InlineButton[][] buttons)
    {
        var rows = buttons
            .Select(row => row
                .Select(b =>
                {
                    var btn = new InlineKeyboardButton(b.Text);
                    if (!string.IsNullOrWhiteSpace(b.Url))
                        btn.Url = b.Url;
                    else
                        btn.CallbackData = b.CallbackData;
                    return btn;
                })
                .ToArray())
            .ToArray();
        return new InlineKeyboardMarkup(rows);
    }
}
