using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace DigitalBrain.UI.Aspire.Hosting;

internal static class FlutterVmService
{
    public static async Task WaitUntilReadyAsync(int ddsPort, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        Exception? last = null;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await using var session = await Session.ConnectAsync(ddsPort, cancellationToken).ConfigureAwait(false);
                _ = await session.GetMainIsolateIdAsync(cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                last = ex;
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new TimeoutException(
            $"Flutter VM service on 127.0.0.1:{ddsPort} was not ready within {timeout.TotalSeconds:0}s.",
            last);
    }

    public static async Task ReloadAsync(int ddsPort, CancellationToken cancellationToken)
    {
        await using var session = await Session.ConnectAsync(ddsPort, cancellationToken).ConfigureAwait(false);
        var isolateId = await session.GetMainIsolateIdAsync(cancellationToken).ConfigureAwait(false);
        var result = await session.CallAsync(
            "reloadSources",
            new Dictionary<string, object?>
            {
                ["isolateId"] = isolateId,
                ["force"] = false,
                ["pause"] = false,
            },
            cancellationToken).ConfigureAwait(false);

        if (result.TryGetProperty("type", out var type)
            && string.Equals(type.GetString(), "Sentinel", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Flutter isolate is not ready for reload.");
        }

        if (result.TryGetProperty("success", out var success) && success.ValueKind is JsonValueKind.False)
        {
            var notices = result.TryGetProperty("notices", out var rawNotices)
                ? rawNotices.ToString()
                : result.ToString();
            throw new InvalidOperationException($"Flutter hot reload was rejected: {notices}");
        }
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly ClientWebSocket _socket = new();
        private int _nextId;

        public static async Task<Session> ConnectAsync(int ddsPort, CancellationToken cancellationToken)
        {
            var session = new Session();
            session._socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(20);
            await session._socket
                .ConnectAsync(new Uri($"ws://127.0.0.1:{ddsPort}/ws"), cancellationToken)
                .ConfigureAwait(false);
            return session;
        }

        public async Task<string> GetMainIsolateIdAsync(CancellationToken cancellationToken)
        {
            var vm = await CallAsync("getVM", null, cancellationToken).ConfigureAwait(false);
            if (!vm.TryGetProperty("isolates", out var isolates) || isolates.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException("Flutter VM reported no isolates.");
            }

            foreach (var isolate in isolates.EnumerateArray())
            {
                var system = isolate.TryGetProperty("isSystemIsolate", out var flag) && flag.GetBoolean();
                if (system)
                {
                    continue;
                }

                var id = isolate.GetProperty("id").GetString();
                if (!string.IsNullOrWhiteSpace(id))
                {
                    return id;
                }
            }

            throw new InvalidOperationException("Flutter VM reported no application isolate.");
        }

        public async Task<JsonElement> CallAsync(
            string method,
            IReadOnlyDictionary<string, object?>? parameters,
            CancellationToken cancellationToken)
        {
            var id = Interlocked.Increment(ref _nextId).ToString();
            var payload = new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["id"] = id,
                ["method"] = method,
            };
            if (parameters is not null)
            {
                payload["params"] = parameters;
            }

            var bytes = JsonSerializer.SerializeToUtf8Bytes(payload);
            await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
                .ConfigureAwait(false);

            while (true)
            {
                using var document = await ReceiveAsync(cancellationToken).ConfigureAwait(false);
                var root = document.RootElement;
                if (!root.TryGetProperty("id", out var replyId)
                    || !string.Equals(replyId.ToString(), id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (root.TryGetProperty("error", out var error))
                {
                    throw new InvalidOperationException($"Flutter VM service {method} failed: {error}");
                }

                return root.TryGetProperty("result", out var result)
                    ? result.Clone()
                    : default;
            }
        }

        private async Task<JsonDocument> ReceiveAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[16 * 1024];
            using var stream = new MemoryStream();
            ValueWebSocketReceiveResult received;
            do
            {
                received = await _socket.ReceiveAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    throw new InvalidOperationException("Flutter VM service closed the connection.");
                }

                stream.Write(buffer, 0, received.Count);
            }
            while (!received.EndOfMessage);

            stream.Position = 0;
            return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        public async ValueTask DisposeAsync()
        {
            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "done", CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch (WebSocketException)
                {
                }
            }

            _socket.Dispose();
        }
    }
}
