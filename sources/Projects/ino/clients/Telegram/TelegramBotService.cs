using System.Collections.Concurrent;
using Grpc.Core;
using Ino.Grpc;
using Ino.Telegram.Host.Services;
using Telegram.BotAPI.AvailableTypes;
using Telegram.BotAPI.GettingUpdates;

using InoGrpcClient = global::Ino.Grpc.Ino.InoClient;

namespace Ino.Telegram.Host;

/// <summary>
/// Single update handler for the Telegram webhook. Routes /start to the
/// mini-app launcher (the persistent chat menu button covers the always-on
/// case), transcribes voice messages locally, and forwards plain text +
/// transcribed voice to the system silo's gRPC <c>Chat</c> RPC.
///
/// <para>The legacy bot used Orleans <c>IThread</c> grains for conversation
/// memory — that's gone in the POC. Continuity now rides on <c>correlation_id</c>
/// (the field the system silo's <see cref="ChatRequest"/> uses to pin
/// follow-up turns to the same neuron activation). The bot caches one
/// correlation per <c>(chatId, topicId)</c> in-memory; if the silo restarts
/// the cached id will hash to a fresh activation, which is acceptable for a
/// launcher-focused bot — the mini-app is the primary chat surface.</para>
/// </summary>
public sealed class TelegramBotService(
    InoGrpcClient inoClient,
    TelegramMessageSender messageSender,
    TelegramFileService fileService,
    ChatActionService chatActions,
    TelegramBotState botState,
    ILogger<TelegramBotService> logger)
{
    // (chatId, topicId) → correlation_id. Bounded growth; entries naturally
    // age out when the bot restarts. A future slice can replace this with a
    // distributed cache if we need cross-instance continuity.
    readonly ConcurrentDictionary<(long, int?), string> _correlations = new();

    public async Task HandleUpdateAsync(Update update, CancellationToken ct)
    {
        try
        {
            await HandleUpdateCoreAsync(update, ct);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unhandled error in HandleUpdateAsync");
        }
    }

    async Task HandleUpdateCoreAsync(Update update, CancellationToken ct)
    {
        var message = update.Message;
        if (message is null) return;

        var chatId = message.Chat.Id;
        if (chatId == 0) return;

        var from = message.From;
        if (from is null) return;

        var topicId = message.MessageThreadId;
        var text = message.Text;

        // Acknowledge receipt with an emoji reaction so the user sees the bot
        // accepted the message even before transcription/silo round-trip.
        await messageSender.SetReactionAsync(chatId, message.MessageId, "👀");

        if (message.Voice is not null && string.IsNullOrEmpty(text))
        {
            try
            {
                text = await fileService.TranscribeVoiceAsync(message.Voice.FileId, ct);
                if (!string.IsNullOrEmpty(text))
                    await messageSender.SendTextAsync(chatId, $"🎤 {text}", topicId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Voice transcription failed");
                await messageSender.SendTextAsync(chatId,
                    "Couldn't transcribe that voice message — Whisper may still be initializing.", topicId);
                return;
            }
        }

        if (string.IsNullOrEmpty(text)) return;

        if (text.StartsWith('/'))
        {
            // Strip @botname suffix so commands sent in group/forum chats
            // ("/start@inobot") still match.
            var token = text.Split(' ', 2)[0].ToLowerInvariant();
            var atIndex = token.IndexOf('@');
            var command = atIndex > 0 ? token[..atIndex] : token;
            if (command == "/start")
            {
                await SendMiniAppButtonAsync(chatId, topicId, ct);
                return;
            }
        }

        await ForwardToInoAsync(chatId, topicId, from.Id, text, ct);
    }

    async Task SendMiniAppButtonAsync(long chatId, int? topicId, CancellationToken ct)
    {
        var miniAppUrl = botState.MiniAppUrl;
        if (string.IsNullOrWhiteSpace(miniAppUrl))
        {
            await messageSender.SendTextAsync(chatId,
                "Mini-app URL not available yet — webhook setup may not have completed.",
                topicId);
            return;
        }

        var button = new InlineKeyboardButton("Open ino")
        {
            WebApp = new WebAppInfo(miniAppUrl),
        };
        var keyboard = new InlineKeyboardMarkup([[button]]);
        await messageSender.SendTextAsync(
            chatId,
            "Tap to open ino:",
            topicId,
            keyboard);
    }

    async Task ForwardToInoAsync(long chatId, int? topicId, long telegramUserId, string text, CancellationToken ct)
    {
        // Send a placeholder we'll edit as frames arrive — Telegram's typing
        // indicator covers the latency before the first frame.
        var placeholder = await messageSender.SendTextAsync(chatId, "...", topicId);
        await using var typing = chatActions.StartTyping(chatId, topicId);

        var key = (chatId, topicId);
        var correlationId = _correlations.TryGetValue(key, out var cached) ? cached : string.Empty;

        var request = new ChatRequest
        {
            Message = text,
            UserId = $"tg:{telegramUserId}",
            CorrelationId = correlationId,
        };

        try
        {
            using var call = inoClient.Chat(request, cancellationToken: ct);
            string lastReply = string.Empty;

            await foreach (var frame in call.ResponseStream.ReadAllAsync(ct))
            {
                // Cache the silo-issued correlation id so the next turn from
                // the same chat lands on the same neuron activation.
                if (!string.IsNullOrEmpty(frame.CorrelationId))
                    _correlations[key] = frame.CorrelationId;

                if (frame.IsSkeleton) continue; // skeleton frames are for Flutter shimmer; bot only shows the final reply
                if (string.IsNullOrEmpty(frame.Reply)) continue;

                lastReply = frame.Reply;
                await messageSender.EditTextAsync(chatId, placeholder.MessageId, frame.Reply);
            }

            if (string.IsNullOrEmpty(lastReply))
            {
                await messageSender.EditTextAsync(chatId, placeholder.MessageId,
                    "(no reply — open the mini-app for the full response)");
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "gRPC Chat to system silo failed");
            await messageSender.EditTextAsync(chatId, placeholder.MessageId,
                "[error reaching ino — open the mini-app instead]");
        }
    }
}
