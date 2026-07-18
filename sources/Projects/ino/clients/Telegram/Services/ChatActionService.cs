using Telegram.BotAPI;
using Telegram.BotAPI.AvailableMethods;

namespace Ino.Telegram.Host.Services;

/// <summary>
/// Sends Telegram chat actions (the "typing…" indicator) while the bot is
/// working on a reply. The indicator expires after ~5 seconds, so this
/// re-pings every 4 seconds inside an awaitable scope:
/// <code>
/// await using var typing = chatActions.StartTyping(chatId);
/// // … long-running work …
/// </code>
/// Disposal cancels the loop.
/// </summary>
public sealed class ChatActionService(
    ITelegramBotClient botClient,
    ILogger<ChatActionService> logger)
{
    const int TypingIntervalMs = 4000;

    public TypingScope StartTyping(long chatId, int? topicId = null)
        => new(botClient, logger, chatId, topicId, "typing");

    public sealed class TypingScope : IAsyncDisposable
    {
        readonly ITelegramBotClient _botClient;
        readonly ILogger _logger;
        readonly long _chatId;
        readonly int? _topicId;
        readonly string _action;
        readonly CancellationTokenSource _cts = new();
        readonly Task _loop;
        int _stopped;

        internal TypingScope(ITelegramBotClient botClient, ILogger logger, long chatId, int? topicId, string action)
        {
            _botClient = botClient;
            _logger = logger;
            _chatId = chatId;
            _topicId = topicId;
            _action = action;
            _loop = RunLoopAsync();
        }

        async Task RunLoopAsync()
        {
            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        await _botClient.SendChatActionAsync(_chatId, _action, messageThreadId: _topicId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogDebug(ex, "Failed to send chat action {Action}", _action);
                    }

                    await Task.Delay(TypingIntervalMs, _cts.Token);
                }
            }
            catch (OperationCanceledException) { }
        }

        public void Stop()
        {
            if (Interlocked.Exchange(ref _stopped, 1) == 0)
                _cts.Cancel();
        }

        public async ValueTask DisposeAsync()
        {
            Stop();
            try { await _loop; } catch { }
            _cts.Dispose();
        }
    }
}
