using Microsoft.Extensions.Options;
using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;
using Telegram.BotAPI.AvailableTypes;
using Telegram.BotAPI.UpdatingMessages;

namespace Ino.Telegram.Host.Services;

/// <summary>
/// Thin wrapper over <see cref="ITelegramBotClient"/> that funnels every send
/// through <see cref="TelegramRateLimiter"/> so the per-chat 30 msg/sec ceiling
/// is enforced uniformly.
///
/// <para>Methods kept slim on purpose — the bot's job is to launch the mini-app
/// and stream chat replies back, so we need plain text + edits + reactions and
/// not much else. Forum topics, photo/document upload, media groups all lived
/// in the legacy bot but aren't used by the launcher-focused POC; bringing
/// them back is a one-liner if we need them.</para>
/// </summary>
public sealed class TelegramMessageSender(
    ITelegramBotClient botClient,
    TelegramRateLimiter rateLimiter,
    IOptions<TelegramBotOptions> options,
    ILogger<TelegramMessageSender> logger)
{
    public ITelegramBotClient BotClient => botClient;
    public string BotToken => options.Value.BotToken;
    public string ServerAddress => botClient.Options.ServerAddress;

    public async Task<Message> SendTextAsync(long chatId, string text, int? topicId = null,
        InlineKeyboardMarkup? replyMarkup = null, string? parseMode = null)
    {
        await rateLimiter.AcquireAsync(chatId);
        return await botClient.SendMessageAsync(chatId, text,
            messageThreadId: topicId, replyMarkup: replyMarkup, parseMode: parseMode);
    }

    public async Task EditTextAsync(long chatId, int messageId, string text,
        InlineKeyboardMarkup? replyMarkup = null, string? parseMode = null)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        await rateLimiter.AcquireAsync(chatId);

        if (parseMode is not null)
        {
            try
            {
                await botClient.EditMessageTextAsync(chatId, messageId, text,
                    parseMode: parseMode, replyMarkup: replyMarkup);
                return;
            }
            catch (BotRequestException)
            {
                // formatted parse failed — fall through to plain text edit
            }
        }

        try
        {
            await botClient.EditMessageTextAsync(chatId, messageId, text, replyMarkup: replyMarkup);
        }
        catch (BotRequestException ex) when (
            ex.Message.Contains("message is not modified", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("message text is empty", StringComparison.OrdinalIgnoreCase))
        {
            // No-op edits are common when the streamed reply hasn't changed —
            // ignoring these specific errors keeps the log clean.
        }
    }

    public async Task SetReactionAsync(long chatId, int messageId, string emoji)
    {
        try
        {
            await rateLimiter.AcquireAsync(chatId);
            await botClient.SetMessageReactionAsync(chatId, messageId, [new ReactionTypeEmoji(emoji)]);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to set reaction {Emoji}", emoji);
        }
    }

    public async Task AnswerCallbackAsync(string callbackQueryId, string? text = null)
    {
        try
        {
            await botClient.AnswerCallbackQueryAsync(callbackQueryId, text: text);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Failed to answer callback query");
        }
    }
}
