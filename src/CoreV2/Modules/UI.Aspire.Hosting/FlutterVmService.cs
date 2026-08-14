using System.Net.WebSockets;
using System.Text.Json;

namespace Brain.Modules.UI.Aspire.Hosting;

internal static class FlutterVmService
{
    internal static async Task ReloadAsync(int port, CancellationToken cancellationToken)
    {
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{port}/ws"), cancellationToken).ConfigureAwait(false);
        var vm = await CallAsync(socket, 1, "getVM", null, cancellationToken).ConfigureAwait(false);
        var isolate = vm.GetProperty("isolates")
            .EnumerateArray()
            .FirstOrDefault(candidate =>
                !candidate.TryGetProperty("isSystemIsolate", out var system) || !system.GetBoolean());
        if (!isolate.TryGetProperty("id", out var isolateId))
        {
            throw new InvalidOperationException("Flutter VM reported no application isolate.");
        }

        var result = await CallAsync(
            socket,
            2,
            "reloadSources",
            new Dictionary<string, object?>
            {
                ["isolateId"] = isolateId.GetString(),
                ["force"] = false,
                ["pause"] = false,
            },
            cancellationToken).ConfigureAwait(false);
        if (result.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
        {
            throw new InvalidOperationException($"Flutter hot reload was rejected: {result}");
        }
    }

    internal static async Task WaitUntilReadyAsync(
        int port,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        Exception? last = null;
        while (!timeoutSource.IsCancellationRequested)
        {
            try
            {
                await ReloadAsync(port, timeoutSource.Token).ConfigureAwait(false);
                return;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                last = exception;
                await Task.Delay(TimeSpan.FromMilliseconds(500), timeoutSource.Token).ConfigureAwait(false);
            }
        }

        throw new TimeoutException($"Flutter VM service on port {port} was not ready within {timeout}.", last);
    }

    private static async Task<JsonElement> CallAsync(
        ClientWebSocket socket,
        int id,
        string method,
        IReadOnlyDictionary<string, object?>? parameters,
        CancellationToken cancellationToken)
    {
        var message = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
        };
        if (parameters is not null)
        {
            message["params"] = parameters;
        }

        await socket.SendAsync(
            JsonSerializer.SerializeToUtf8Bytes(message),
            WebSocketMessageType.Text,
            true,
            cancellationToken).ConfigureAwait(false);

        var buffer = new byte[16 * 1024];
        using var stream = new MemoryStream();
        while (true)
        {
            var received = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (received.MessageType == WebSocketMessageType.Close)
            {
                throw new InvalidOperationException("Flutter VM service closed its connection.");
            }

            stream.Write(buffer, 0, received.Count);
            if (!received.EndOfMessage)
            {
                continue;
            }

            using var document = JsonDocument.Parse(stream.ToArray());
            var root = document.RootElement;
            if (!root.TryGetProperty("id", out var replyId) || replyId.GetInt32() != id)
            {
                stream.SetLength(0);
                continue;
            }

            if (root.TryGetProperty("error", out var error))
            {
                throw new InvalidOperationException($"Flutter VM service call '{method}' failed: {error}");
            }

            return root.GetProperty("result").Clone();
        }
    }
}
