using System.Net.WebSockets;
using System.Threading.Channels;
using DigitalBrain.AI.PersonaPlex;

namespace DigitalBrain.Kernel;

internal static class PersonaPlexVoiceHttpMaps
{
    private const int LocalFlutterWebPort = 54723;
    private const int QueueCapacity = 4;

    public static IEndpointRouteBuilder MapPersonaPlexVoice(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            HttpSurfacePaths.PersonaPlexVoicePath,
            static async Task (
                HttpContext http,
                IPersonaPlexSessionFactory sessions,
                CancellationToken cancellationToken) =>
            {
                if (!IsAllowedOrigin(http.Request))
                {
                    http.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }

                if (!http.WebSockets.IsWebSocketRequest)
                {
                    http.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
                    return;
                }

                using var socket = await http.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);
                await RunConnectionAsync(
                    socket,
                    http.TraceIdentifier,
                    sessions,
                    cancellationToken).ConfigureAwait(false);
            });

        return endpoints;
    }

    private static bool IsAllowedOrigin(HttpRequest request)
    {
        var origins = request.Headers.Origin;
        if (origins.Count == 0)
        {
            // Native Flutter/Windows clients do not send the browser Origin header.
            return true;
        }

        if (origins.Count != 1
            || !Uri.TryCreate(origins[0], UriKind.Absolute, out var origin)
            || origin.AbsolutePath != "/"
            || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment))
        {
            return false;
        }

        var requestPort = request.Host.Port
            ?? (string.Equals(request.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ? 443 : 80);
        var isSameOrigin = string.Equals(origin.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(origin.Host, request.Host.Host, StringComparison.OrdinalIgnoreCase)
            && origin.Port == requestPort;
        var isLocalFlutterWeb = string.Equals(origin.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)
            && origin.IsLoopback
            && origin.Port == LocalFlutterWebPort;

        return isSameOrigin || isLocalFlutterWeb;
    }

    internal static async Task RunConnectionAsync(
        WebSocket socket,
        string connectionId,
        IPersonaPlexSessionFactory sessions,
        CancellationToken cancellationToken)
    {
        IPersonaPlexSession? session = null;
        try
        {
            var start = await ReceiveMessageAsync(socket, cancellationToken).ConfigureAwait(false);
            if (start.Type != WebSocketMessageType.Text
                || DecodeControl(start.Payload) != PersonaPlexVoiceControl.Start)
            {
                throw new PersonaPlexVoiceProtocolException(
                    "A PersonaPlex session must begin with a start control message.");
            }

            await SendAsync(
                socket,
                PersonaPlexVoiceProtocol.EncodeStatus(
                    "priming",
                    "PersonaPlex is loading the voice persona (can take ~20–60s)…"),
                WebSocketMessageType.Text,
                cancellationToken).ConfigureAwait(false);

            session = await sessions
                .CreateAsync(new PersonaPlexSessionRequest(connectionId), cancellationToken)
                .ConfigureAwait(false);

            await SendAsync(
                socket,
                PersonaPlexVoiceProtocol.EncodeStatus("ready", "PersonaPlex session is ready."),
                WebSocketMessageType.Text,
                cancellationToken).ConfigureAwait(false);

            await RunPipelinesAsync(socket, session, cancellationToken).ConfigureAwait(false);
        }
        catch (PersonaPlexVoiceProtocolException)
        {
            await TrySendErrorAsync(
                socket,
                "protocol_error",
                "Invalid PersonaPlex voice protocol message.",
                cancellationToken).ConfigureAwait(false);
            await TryCloseOutputAsync(
                socket,
                WebSocketCloseStatus.ProtocolError,
                "Invalid PersonaPlex voice protocol message.",
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (WebSocketException)
        {
        }
        catch (Exception)
        {
            await TrySendErrorAsync(
                socket,
                "unavailable",
                "PersonaPlex voice is unavailable.",
                cancellationToken).ConfigureAwait(false);
            await TryCloseOutputAsync(
                socket,
                WebSocketCloseStatus.InternalServerError,
                "PersonaPlex voice is unavailable.",
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (session is not null)
            {
                await TryResetAsync(session).ConfigureAwait(false);
                await TryDisposeAsync(session).ConfigureAwait(false);
            }

            await TryCloseOutputAsync(
                socket,
                WebSocketCloseStatus.NormalClosure,
                "PersonaPlex voice session ended.",
                CancellationToken.None).ConfigureAwait(false);
        }
    }

    private static async Task RunPipelinesAsync(
        WebSocket socket,
        IPersonaPlexSession session,
        CancellationToken cancellationToken)
    {
        var inputs = CreateFrameChannel();
        var outputs = CreateFrameChannel();
        using var receiveCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var workCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var receive = ReceiveAudioAsync(socket, inputs.Writer, receiveCancellation.Token);
        var process = ProcessAudioAsync(session, inputs.Reader, outputs.Writer, workCancellation.Token);
        var send = SendAudioAsync(socket, outputs.Reader, workCancellation.Token);

        try
        {
            var first = await Task.WhenAny(receive, process, send).ConfigureAwait(false);
            await first.ConfigureAwait(false);
            await Task.WhenAll(receive, process, send).ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            workCancellation.Cancel();
            inputs.Writer.TryComplete();
            outputs.Writer.TryComplete();
            await ObserveAsync(process, send).ConfigureAwait(false);

            if (!cancellationToken.IsCancellationRequested
                && exception is not (OperationCanceledException or WebSocketException))
            {
                await TrySendPipelineFailureAsync(socket, exception, cancellationToken).ConfigureAwait(false);
            }

            receiveCancellation.Cancel();
            await ObserveAsync(receive).ConfigureAwait(false);
        }
    }

    private static async Task TrySendPipelineFailureAsync(
        WebSocket socket,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var isProtocolError = exception is PersonaPlexVoiceProtocolException;
        await TrySendErrorAsync(
            socket,
            isProtocolError ? "protocol_error" : "unavailable",
            isProtocolError
                ? "Invalid PersonaPlex voice protocol message."
                : "PersonaPlex voice is unavailable.",
            cancellationToken).ConfigureAwait(false);
        await TryCloseOutputAsync(
            socket,
            isProtocolError ? WebSocketCloseStatus.ProtocolError : WebSocketCloseStatus.InternalServerError,
            isProtocolError
                ? "Invalid PersonaPlex voice protocol message."
                : "PersonaPlex voice is unavailable.",
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReceiveAudioAsync(
        WebSocket socket,
        ChannelWriter<PersonaPlexAudioFrame> inputs,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;
        var expectedSequence = 1L;
        try
        {
            while (socket.State == WebSocketState.Open)
            {
                var message = await ReceiveMessageAsync(socket, cancellationToken).ConfigureAwait(false);
                if (message.Type == WebSocketMessageType.Close)
                {
                    return;
                }

                if (message.Type == WebSocketMessageType.Text)
                {
                    if (DecodeControl(message.Payload) == PersonaPlexVoiceControl.Stop)
                    {
                        return;
                    }

                    throw new PersonaPlexVoiceProtocolException(
                        "Only stop is accepted after a PersonaPlex session starts.");
                }

                if (message.Type != WebSocketMessageType.Binary)
                {
                    throw new PersonaPlexVoiceProtocolException(
                        "PersonaPlex audio must use binary WebSocket messages.");
                }

                var frame = DecodeAudio(message.Payload);
                if (frame.Sequence != expectedSequence)
                {
                    throw new PersonaPlexVoiceProtocolException(
                        "PersonaPlex audio sequence is out of order.");
                }

                if (expectedSequence == long.MaxValue)
                {
                    throw new PersonaPlexVoiceProtocolException("PersonaPlex audio sequence is exhausted.");
                }

                expectedSequence++;
                await inputs.WriteAsync(frame, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            inputs.TryComplete(failure);
        }
    }

    private static async Task ProcessAudioAsync(
        IPersonaPlexSession session,
        ChannelReader<PersonaPlexAudioFrame> inputs,
        ChannelWriter<PersonaPlexAudioFrame> outputs,
        CancellationToken cancellationToken)
    {
        Exception? failure = null;
        try
        {
            await foreach (var input in inputs.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                var output = await session.ProcessAsync(input, cancellationToken).ConfigureAwait(false);
                if (output.Sequence != input.Sequence)
                {
                    throw new InvalidOperationException("PersonaPlex produced an invalid output sequence.");
                }

                await outputs.WriteAsync(output, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            failure = exception;
            throw;
        }
        finally
        {
            outputs.TryComplete(failure);
        }
    }

    private static async Task SendAudioAsync(
        WebSocket socket,
        ChannelReader<PersonaPlexAudioFrame> outputs,
        CancellationToken cancellationToken)
    {
        await foreach (var output in outputs.ReadAllAsync(cancellationToken).ConfigureAwait(false))
        {
            if (socket.State != WebSocketState.Open)
            {
                return;
            }

            await SendAsync(
                socket,
                PersonaPlexVoiceProtocol.EncodeAudio(output),
                WebSocketMessageType.Binary,
                cancellationToken).ConfigureAwait(false);
        }

        if (socket.State == WebSocketState.Open)
        {
            await SendAsync(
                socket,
                PersonaPlexVoiceProtocol.EncodeStop(),
                WebSocketMessageType.Text,
                cancellationToken).ConfigureAwait(false);
        }
    }

    private static Channel<PersonaPlexAudioFrame> CreateFrameChannel()
        => Channel.CreateBounded<PersonaPlexAudioFrame>(new BoundedChannelOptions(QueueCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = true,
        });

    private static PersonaPlexVoiceControl DecodeControl(ReadOnlySpan<byte> payload)
    {
        try
        {
            return PersonaPlexVoiceProtocol.DecodeControl(payload);
        }
        catch (InvalidDataException exception)
        {
            throw new PersonaPlexVoiceProtocolException(exception.Message, exception);
        }
    }

    private static PersonaPlexAudioFrame DecodeAudio(ReadOnlySpan<byte> payload)
    {
        try
        {
            return PersonaPlexVoiceProtocol.DecodeAudio(payload);
        }
        catch (InvalidDataException exception)
        {
            throw new PersonaPlexVoiceProtocolException(exception.Message, exception);
        }
    }

    private static async Task<ReceivedMessage> ReceiveMessageAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[PersonaPlexVoiceProtocol.PacketByteCount + 1];
        var count = 0;
        WebSocketMessageType? messageType = null;

        while (true)
        {
            var received = await socket
                .ReceiveAsync(buffer.AsMemory(count), cancellationToken)
                .ConfigureAwait(false);
            messageType ??= received.MessageType;
            if (messageType != received.MessageType)
            {
                throw new PersonaPlexVoiceProtocolException(
                    "PersonaPlex WebSocket message type changed between fragments.");
            }

            count += received.Count;
            if (count > PersonaPlexVoiceProtocol.PacketByteCount)
            {
                throw new PersonaPlexVoiceProtocolException("PersonaPlex WebSocket message is too large.");
            }

            if (received.EndOfMessage)
            {
                return new ReceivedMessage(received.MessageType, buffer.AsSpan(0, count).ToArray());
            }
        }
    }

    private static async Task SendAsync(
        WebSocket socket,
        byte[] payload,
        WebSocketMessageType messageType,
        CancellationToken cancellationToken)
        => await socket
            .SendAsync(payload.AsMemory(), messageType, endOfMessage: true, cancellationToken)
            .ConfigureAwait(false);

    private static async Task TrySendErrorAsync(
        WebSocket socket,
        string code,
        string message,
        CancellationToken cancellationToken)
    {
        if (socket.State != WebSocketState.Open)
        {
            return;
        }

        try
        {
            await SendAsync(
                socket,
                PersonaPlexVoiceProtocol.EncodeError(code, message),
                WebSocketMessageType.Text,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (socket.State != WebSocketState.Open)
        {
        }
    }

    private static async Task TryCloseOutputAsync(
        WebSocket socket,
        WebSocketCloseStatus status,
        string description,
        CancellationToken cancellationToken)
    {
        if (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
            return;
        }

        try
        {
            await socket.CloseOutputAsync(status, description, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception) when (socket.State is not (WebSocketState.Open or WebSocketState.CloseReceived))
        {
        }
    }

    private static async Task TryResetAsync(IPersonaPlexSession session)
    {
        try
        {
            await session.ResetAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private static async Task TryDisposeAsync(IPersonaPlexSession session)
    {
        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private static async Task ObserveAsync(params Task[] tasks)
    {
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception)
        {
        }
    }

    private sealed record ReceivedMessage(WebSocketMessageType Type, byte[] Payload);
}
