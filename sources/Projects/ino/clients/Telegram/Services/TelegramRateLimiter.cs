using System.Collections.Concurrent;

namespace Ino.Telegram.Host.Services;

/// <summary>
/// Per-chat rate limiter respecting Telegram's 30 msg/sec limit. Sliding-window
/// token bucket — each chat gets its own semaphore so a busy group can't starve
/// other chats. Set conservatively at 25/sec for safety margin.
/// </summary>
public sealed class TelegramRateLimiter
{
    const int MaxTokensPerSecond = 25;
    const int WindowMs = 1000;

    readonly ConcurrentDictionary<long, ChatBucket> _buckets = new();

    public async Task AcquireAsync(long chatId, CancellationToken ct = default)
    {
        var bucket = _buckets.GetOrAdd(chatId, _ => new ChatBucket());
        await bucket.WaitAsync(ct);
    }

    sealed class ChatBucket
    {
        readonly SemaphoreSlim _semaphore = new(MaxTokensPerSecond, MaxTokensPerSecond);

        public async Task WaitAsync(CancellationToken ct)
        {
            await _semaphore.WaitAsync(ct);
            _ = Task.Delay(WindowMs, ct).ContinueWith(_ =>
            {
                try { _semaphore.Release(); } catch (ObjectDisposedException) { }
            }, TaskScheduler.Default);
        }
    }
}
