using System.Buffers.Binary;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using DigitalBrain.AI.PersonaPlex;
using DigitalBrain.Kernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DigitalBrain.E2E.Tests;

[Collection(PersonaPlexVoiceCollection.Name)]
public sealed class PersonaPlexVoiceTests
{
    [Fact]
    public void DecodeAudioRejectsWrongPayloadLength()
        => Assert.Throws<InvalidDataException>(() => PersonaPlexVoiceProtocol.DecodeAudio(new byte[12]));

    [Fact]
    public void AudioPacketRoundTripPreservesSequenceAndPcm()
    {
        var pcm = Enumerable.Range(0, 1920).Select(static sample => (short)(sample - 960)).ToArray();

        var packet = PersonaPlexVoiceProtocol.EncodeAudio(PersonaPlexAudioFrame.Create(7, pcm));
        var decoded = PersonaPlexVoiceProtocol.DecodeAudio(packet);

        Assert.Equal(3856, packet.Length);
        Assert.Equal(7, decoded.Sequence);
        Assert.Equal(pcm, decoded.Pcm16.ToArray());
    }

    [Fact]
    public void EncodeAudioWritesDocumentedLittleEndianHeaderOffsets()
    {
        var pcm = new short[1920];
        pcm[0] = 0x1234;
        pcm[1] = unchecked((short)0xfedc);

        var packet = PersonaPlexVoiceProtocol.EncodeAudio(
            PersonaPlexAudioFrame.Create(0x0102030405060708, pcm));

        Assert.Equal(
            new byte[]
            {
                0x01, 0x00, 0x00, 0x00,
                0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01,
                0x80, 0x07, 0x00, 0x00,
                0x34, 0x12, 0xdc, 0xfe,
            },
            packet.AsSpan(0, 20).ToArray());
    }

    [Fact]
    public void DecodeAudioRejectsUnsupportedVersion()
    {
        var packet = new byte[3856];
        BinaryPrimitives.WriteInt32LittleEndian(packet, 2);
        BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(12), 1920);

        Assert.Throws<InvalidDataException>(() => PersonaPlexVoiceProtocol.DecodeAudio(packet));
    }

    [Fact]
    public void DecodeAudioRejectsWrongSampleCount()
    {
        var packet = new byte[3856];
        BinaryPrimitives.WriteInt32LittleEndian(packet, 1);
        BinaryPrimitives.WriteInt64LittleEndian(packet.AsSpan(4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(packet.AsSpan(12), 1919);

        Assert.Throws<InvalidDataException>(() => PersonaPlexVoiceProtocol.DecodeAudio(packet));
    }

    [Fact]
    public void JsonControlsRepresentStartStatusErrorAndStop()
    {
        Assert.Equal(
            PersonaPlexVoiceControl.Start,
            PersonaPlexVoiceProtocol.DecodeControl("{\"type\":\"start\"}"u8));
        Assert.Equal(
            PersonaPlexVoiceControl.Stop,
            PersonaPlexVoiceProtocol.DecodeControl("{\"type\":\"stop\"}"u8));

        using var status = JsonDocument.Parse(
            PersonaPlexVoiceProtocol.EncodeStatus("ready", "PersonaPlex session is ready."));
        Assert.Equal("status", status.RootElement.GetProperty("type").GetString());
        Assert.Equal("ready", status.RootElement.GetProperty("state").GetString());
        Assert.Equal("PersonaPlex session is ready.", status.RootElement.GetProperty("message").GetString());

        using var error = JsonDocument.Parse(
            PersonaPlexVoiceProtocol.EncodeError("protocol_error", "Invalid voice protocol message."));
        Assert.Equal("error", error.RootElement.GetProperty("type").GetString());
        Assert.Equal("protocol_error", error.RootElement.GetProperty("code").GetString());
        Assert.Equal("Invalid voice protocol message.", error.RootElement.GetProperty("message").GetString());

        using var stop = JsonDocument.Parse(PersonaPlexVoiceProtocol.EncodeStop());
        Assert.Equal("stop", stop.RootElement.GetProperty("type").GetString());
    }

    [Fact]
    public async Task WebSocketProcessesOneFrameThroughAnOwnedSession()
    {
        var factory = new RecordingSessionFactory();
        await using var app = await StartHostAsync(factory);
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(VoiceUri(app), TestContext.Current.CancellationToken);

        await socket.SendAsync(
            "{\"type\":\"start\"}"u8.ToArray(),
            WebSocketMessageType.Text,
            endOfMessage: true,
            TestContext.Current.CancellationToken);

        var (statusType, statusPayload) = await ReceiveAsync(socket);
        Assert.Equal(WebSocketMessageType.Text, statusType);
        using (var status = JsonDocument.Parse(statusPayload))
        {
            Assert.Equal("status", status.RootElement.GetProperty("type").GetString());
            Assert.Equal("ready", status.RootElement.GetProperty("state").GetString());
        }

        var input = PersonaPlexAudioFrame.Create(1, Enumerable.Repeat((short)42, 1920).ToArray());
        await socket.SendAsync(
            PersonaPlexVoiceProtocol.EncodeAudio(input),
            WebSocketMessageType.Binary,
            endOfMessage: true,
            TestContext.Current.CancellationToken);

        var (audioType, audioPayload) = await ReceiveAsync(socket);
        Assert.Equal(WebSocketMessageType.Binary, audioType);
        var output = PersonaPlexVoiceProtocol.DecodeAudio(audioPayload);
        Assert.Equal(1, output.Sequence);
        Assert.All(output.Pcm16.ToArray(), static sample => Assert.Equal((short)-42, sample));

        await socket.SendAsync(
            "{\"type\":\"stop\"}"u8.ToArray(),
            WebSocketMessageType.Text,
            endOfMessage: true,
            TestContext.Current.CancellationToken);

        var (stopType, stopPayload) = await ReceiveAsync(socket);
        Assert.Equal(WebSocketMessageType.Text, stopType);
        using (var stop = JsonDocument.Parse(stopPayload))
        {
            Assert.Equal("stop", stop.RootElement.GetProperty("type").GetString());
        }

        var (closeType, _) = await ReceiveAsync(socket);
        Assert.Equal(WebSocketMessageType.Close, closeType);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, socket.CloseStatus);
        await socket.CloseOutputAsync(
            WebSocketCloseStatus.NormalClosure,
            "Test complete.",
            TestContext.Current.CancellationToken);

        await AssertCleanedUpOnceAsync(factory);
    }

    [Fact]
    public async Task WebSocketRejectsAnOutOfOrderInputSequence()
    {
        var factory = new RecordingSessionFactory();
        await using var app = await StartHostAsync(factory);
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(VoiceUri(app), TestContext.Current.CancellationToken);

        await socket.SendAsync(
            "{\"type\":\"start\"}"u8.ToArray(),
            WebSocketMessageType.Text,
            endOfMessage: true,
            TestContext.Current.CancellationToken);
        _ = await ReceiveAsync(socket);

        var outOfOrder = PersonaPlexAudioFrame.Create(2, new short[1920]);
        await socket.SendAsync(
            PersonaPlexVoiceProtocol.EncodeAudio(outOfOrder),
            WebSocketMessageType.Binary,
            endOfMessage: true,
            TestContext.Current.CancellationToken);

        var (errorType, errorPayload) = await ReceiveAsync(socket);
        Assert.Equal(WebSocketMessageType.Text, errorType);
        using (var error = JsonDocument.Parse(errorPayload))
        {
            Assert.Equal("error", error.RootElement.GetProperty("type").GetString());
            Assert.Equal("protocol_error", error.RootElement.GetProperty("code").GetString());
        }

        var (closeType, _) = await ReceiveAsync(socket);
        Assert.Equal(WebSocketMessageType.Close, closeType);
        Assert.Equal(WebSocketCloseStatus.ProtocolError, socket.CloseStatus);
        await socket.CloseOutputAsync(
            WebSocketCloseStatus.NormalClosure,
            "Test complete.",
            TestContext.Current.CancellationToken);
        await AssertCleanedUpOnceAsync(factory);
    }

    [Fact]
    public async Task ProtocolErrorCleansUpExactlyOnceWithoutPeerCloseAcknowledgement()
    {
        var factory = new RecordingSessionFactory();
        await using var app = await StartHostAsync(factory);
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(VoiceUri(app), TestContext.Current.CancellationToken);

        await socket.SendAsync(
            "{\"type\":\"start\"}"u8.ToArray(),
            WebSocketMessageType.Text,
            endOfMessage: true,
            TestContext.Current.CancellationToken);
        _ = await ReceiveAsync(socket);

        await socket.SendAsync(
            PersonaPlexVoiceProtocol.EncodeAudio(PersonaPlexAudioFrame.Create(2, new short[1920])),
            WebSocketMessageType.Binary,
            endOfMessage: true,
            TestContext.Current.CancellationToken);

        var (errorType, _) = await ReceiveAsync(socket);
        Assert.Equal(WebSocketMessageType.Text, errorType);
        var (closeType, _) = await ReceiveAsync(socket);
        Assert.Equal(WebSocketMessageType.Close, closeType);
        Assert.Equal(WebSocketCloseStatus.ProtocolError, socket.CloseStatus);

        await AssertCleanedUpOnceAsync(factory, TimeSpan.FromSeconds(1));
        socket.Abort();
    }

    [Fact]
    public async Task WebSocketDoesNotExposeSessionCreationFailures()
    {
        await using var app = await StartHostAsync(new ThrowingSessionFactory());
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(VoiceUri(app), TestContext.Current.CancellationToken);

        await socket.SendAsync(
            "{\"type\":\"start\"}"u8.ToArray(),
            WebSocketMessageType.Text,
            endOfMessage: true,
            TestContext.Current.CancellationToken);

        var (errorType, errorPayload) = await ReceiveAsync(socket);
        Assert.Equal(WebSocketMessageType.Text, errorType);
        using (var error = JsonDocument.Parse(errorPayload))
        {
            Assert.Equal("error", error.RootElement.GetProperty("type").GetString());
            Assert.Equal("unavailable", error.RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain("sensitive-model-detail", Encoding.UTF8.GetString(errorPayload));
        }

        var (closeType, _) = await ReceiveAsync(socket);
        Assert.Equal(WebSocketMessageType.Close, closeType);
        Assert.Equal(WebSocketCloseStatus.InternalServerError, socket.CloseStatus);
        await socket.CloseOutputAsync(
            WebSocketCloseStatus.NormalClosure,
            "Test complete.",
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task FactoryInvalidDataFailureIsUnavailableNotProtocolError()
    {
        await using var app = await StartHostAsync(
            new ThrowingSessionFactory(new InvalidDataException("sensitive-runtime-detail")));
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(VoiceUri(app), TestContext.Current.CancellationToken);

        await socket.SendAsync(
            "{\"type\":\"start\"}"u8.ToArray(),
            WebSocketMessageType.Text,
            endOfMessage: true,
            TestContext.Current.CancellationToken);

        var (errorType, errorPayload) = await ReceiveAsync(socket);
        Assert.Equal(WebSocketMessageType.Text, errorType);
        using (var error = JsonDocument.Parse(errorPayload))
        {
            Assert.Equal("unavailable", error.RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain("sensitive-runtime-detail", Encoding.UTF8.GetString(errorPayload));
        }

        var (closeType, _) = await ReceiveAsync(socket);
        Assert.Equal(WebSocketMessageType.Close, closeType);
        Assert.Equal(WebSocketCloseStatus.InternalServerError, socket.CloseStatus);
        await socket.CloseOutputAsync(
            WebSocketCloseStatus.NormalClosure,
            "Test complete.",
            TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task RuntimeInvalidDataFailureIsUnavailableNotProtocolError()
    {
        var factory = new RecordingSessionFactory(
            static (_, _) => ValueTask.FromException<PersonaPlexAudioFrame>(
                new InvalidDataException("sensitive-runtime-detail")));
        await using var app = await StartHostAsync(factory);
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(VoiceUri(app), TestContext.Current.CancellationToken);
        await StartSessionAsync(socket);
        await socket.SendAsync(
            PersonaPlexVoiceProtocol.EncodeAudio(PersonaPlexAudioFrame.Create(1, new short[1920])),
            WebSocketMessageType.Binary,
            endOfMessage: true,
            TestContext.Current.CancellationToken);

        var (errorType, errorPayload) = await ReceiveAsync(socket);
        Assert.Equal(WebSocketMessageType.Text, errorType);
        using (var error = JsonDocument.Parse(errorPayload))
        {
            Assert.Equal("unavailable", error.RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain("sensitive-runtime-detail", Encoding.UTF8.GetString(errorPayload));
        }

        var (closeType, _) = await ReceiveAsync(socket);
        Assert.Equal(WebSocketMessageType.Close, closeType);
        Assert.Equal(WebSocketCloseStatus.InternalServerError, socket.CloseStatus);
        await AssertCleanedUpOnceAsync(factory, TimeSpan.FromSeconds(1));
        socket.Abort();
    }

    [Fact]
    public async Task WebSocketRejectsForeignBrowserOriginBeforeCreatingSession()
    {
        var factory = new RecordingSessionFactory();
        await using var app = await StartHostAsync(factory);
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Origin", "https://foreign.example");

        _ = await Assert.ThrowsAsync<WebSocketException>(
            () => socket.ConnectAsync(VoiceUri(app), TestContext.Current.CancellationToken));

        Assert.Equal(0, factory.CreateCount);
    }

    [Fact]
    public async Task WebSocketAllowsCurrentLocalFlutterWebOrigin()
    {
        var factory = new RecordingSessionFactory();
        await using var app = await StartHostAsync(factory);
        using var socket = new ClientWebSocket();
        socket.Options.SetRequestHeader("Origin", "http://127.0.0.1:54723");

        await socket.ConnectAsync(VoiceUri(app), TestContext.Current.CancellationToken);
        await StartSessionAsync(socket);
        socket.Abort();

        await AssertCleanedUpOnceAsync(factory);
    }

    [Fact]
    public async Task WebSocketAllowsSameOriginBrowser()
    {
        var factory = new RecordingSessionFactory();
        await using var app = await StartHostAsync(factory);
        using var socket = new ClientWebSocket();
        var voiceUri = VoiceUri(app);
        socket.Options.SetRequestHeader(
            "Origin",
            new UriBuilder(voiceUri) { Scheme = "http", Path = string.Empty }.Uri.GetLeftPart(UriPartial.Authority));

        await socket.ConnectAsync(voiceUri, TestContext.Current.CancellationToken);
        await StartSessionAsync(socket);
        socket.Abort();

        await AssertCleanedUpOnceAsync(factory);
    }

    [Fact]
    public async Task ClientAbortResetsAndDisposesExactlyOnce()
    {
        var factory = new RecordingSessionFactory();
        await using var app = await StartHostAsync(factory);
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(VoiceUri(app), TestContext.Current.CancellationToken);
        await StartSessionAsync(socket);

        socket.Abort();

        await AssertCleanedUpOnceAsync(factory, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ClientAbortCancelsBlockedProcessingBeforeCleanup()
    {
        var processor = new BlockingProcessor();
        var factory = new RecordingSessionFactory(processor.ProcessAsync);
        await using var app = await StartHostAsync(factory);
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(VoiceUri(app), TestContext.Current.CancellationToken);
        await StartSessionAsync(socket);
        await socket.SendAsync(
            PersonaPlexVoiceProtocol.EncodeAudio(PersonaPlexAudioFrame.Create(1, new short[1920])),
            WebSocketMessageType.Binary,
            endOfMessage: true,
            TestContext.Current.CancellationToken);
        await processor.Entered.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        socket.Abort();

        await processor.Canceled.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        await AssertCleanedUpOnceAsync(factory, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task CancellationUnwindsSaturatedOutputQueueAndStalledSender()
    {
        var factory = new RecordingSessionFactory();
        using var socket = new StalledSenderWebSocket(frameCount: 10);
        using var cancellation = new CancellationTokenSource();

        var run = PersonaPlexVoiceHttpMaps.RunConnectionAsync(
            socket,
            "saturated-test",
            factory,
            cancellation.Token);
        await socket.BinarySendBlocked.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        var session = factory.Session ?? throw new InvalidOperationException("The test session was not created.");
        await session.SixthProcessStarted.Task.WaitAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        Assert.Equal(6, session.ProcessCount);

        await cancellation.CancelAsync();

        await run.WaitAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        Assert.True(socket.BlockedSendCanceled.Task.IsCompleted);
        await AssertCleanedUpOnceAsync(factory, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task ExactSizeAudioMayEndWithAnEmptyFinalFragment()
    {
        var factory = new RecordingSessionFactory();
        await using var app = await StartHostAsync(factory);
        using var socket = new ClientWebSocket();
        await socket.ConnectAsync(VoiceUri(app), TestContext.Current.CancellationToken);

        await socket.SendAsync(
            "{\"type\":\"start\"}"u8.ToArray(),
            WebSocketMessageType.Text,
            endOfMessage: true,
            TestContext.Current.CancellationToken);
        _ = await ReceiveAsync(socket);

        var packet = PersonaPlexVoiceProtocol.EncodeAudio(
            PersonaPlexAudioFrame.Create(1, Enumerable.Repeat((short)17, 1920).ToArray()));
        await socket.SendAsync(
            packet,
            WebSocketMessageType.Binary,
            endOfMessage: false,
            TestContext.Current.CancellationToken);
        await socket.SendAsync(
            ReadOnlyMemory<byte>.Empty,
            WebSocketMessageType.Binary,
            endOfMessage: true,
            TestContext.Current.CancellationToken);

        var (audioType, audioPayload) = await ReceiveAsync(socket);
        Assert.Equal(WebSocketMessageType.Binary, audioType);
        var output = PersonaPlexVoiceProtocol.DecodeAudio(audioPayload);
        Assert.Equal(1, output.Sequence);
        Assert.All(output.Pcm16.ToArray(), static sample => Assert.Equal((short)-17, sample));

        socket.Abort();
        await AssertCleanedUpOnceAsync(factory);
    }

    private static async Task<WebApplication> StartHostAsync(IPersonaPlexSessionFactory factory)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(static options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddSingleton(factory);

        var app = builder.Build();
        app.UseWebSockets();
        app.MapPersonaPlexVoice();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }

    private static async Task StartSessionAsync(ClientWebSocket socket)
    {
        await socket.SendAsync(
            "{\"type\":\"start\"}"u8.ToArray(),
            WebSocketMessageType.Text,
            endOfMessage: true,
            TestContext.Current.CancellationToken);
        var (statusType, _) = await ReceiveAsync(socket);
        Assert.Equal(WebSocketMessageType.Text, statusType);
    }

    private static Uri VoiceUri(WebApplication app)
    {
        var server = app.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new UriBuilder(address) { Scheme = "ws", Path = "/voice/personaplex" }.Uri;
    }

    private static async Task<(WebSocketMessageType Type, byte[] Payload)> ReceiveAsync(ClientWebSocket socket)
    {
        var buffer = new byte[PersonaPlexVoiceProtocol.PacketByteCount];
        var received = await socket.ReceiveAsync(buffer.AsMemory(), TestContext.Current.CancellationToken);
        Assert.True(received.EndOfMessage);
        return (received.MessageType, buffer.AsSpan(0, received.Count).ToArray());
    }

    private static async Task AssertCleanedUpOnceAsync(
        RecordingSessionFactory factory,
        TimeSpan? timeout = null)
    {
        var session = factory.Session ?? throw new InvalidOperationException("The test session was not created.");
        await session.CleanupCompleted.Task.WaitAsync(
            timeout ?? TimeSpan.FromSeconds(5),
            TestContext.Current.CancellationToken);
        Assert.Equal(1, session.ResetCount);
        Assert.Equal(1, session.DisposeCount);
    }

    private sealed class RecordingSessionFactory(
        Func<PersonaPlexAudioFrame, CancellationToken, ValueTask<PersonaPlexAudioFrame>>? process = null)
        : IPersonaPlexSessionFactory
    {
        private int _createCount;

        public int CreateCount => Volatile.Read(ref _createCount);

        public RecordingSession? Session { get; private set; }

        public ValueTask<IPersonaPlexSession> CreateAsync(
            PersonaPlexSessionRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _createCount);
            Session = new RecordingSession(process);
            return ValueTask.FromResult<IPersonaPlexSession>(Session);
        }
    }

    private sealed class ThrowingSessionFactory(Exception? failure = null) : IPersonaPlexSessionFactory
    {
        public ValueTask<IPersonaPlexSession> CreateAsync(
            PersonaPlexSessionRequest request,
            CancellationToken cancellationToken = default)
            => throw failure ?? new InvalidOperationException("sensitive-model-detail");
    }

    private sealed class RecordingSession(
        Func<PersonaPlexAudioFrame, CancellationToken, ValueTask<PersonaPlexAudioFrame>>? process)
        : IPersonaPlexSession
    {
        private int _disposeCount;
        private int _processCount;
        private int _resetCount;

        public TaskCompletionSource CleanupCompleted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public int DisposeCount => Volatile.Read(ref _disposeCount);

        public int ProcessCount => Volatile.Read(ref _processCount);

        public int ResetCount => Volatile.Read(ref _resetCount);

        public TaskCompletionSource SixthProcessStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<PersonaPlexAudioFrame> ProcessAsync(
            PersonaPlexAudioFrame frame,
            CancellationToken cancellationToken = default)
        {
            var count = Interlocked.Increment(ref _processCount);
            if (count == 6)
            {
                SixthProcessStarted.TrySetResult();
            }

            return process?.Invoke(frame, cancellationToken) ?? ValueTask.FromResult(
                PersonaPlexAudioFrame.Create(
                    frame.Sequence,
                    frame.Pcm16.Span.ToArray().Select(static sample => (short)-sample).ToArray()));
        }

        public ValueTask ResetAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _resetCount);
            return ValueTask.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            Interlocked.Increment(ref _disposeCount);
            CleanupCompleted.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class BlockingProcessor
    {
        public TaskCompletionSource Canceled { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Entered { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<PersonaPlexAudioFrame> ProcessAsync(
            PersonaPlexAudioFrame frame,
            CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(frame);
            Entered.TrySetResult();
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The blocking processor unexpectedly resumed.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Canceled.TrySetResult();
                throw;
            }
        }
    }

    private sealed class StalledSenderWebSocket : WebSocket
    {
        private readonly Queue<(byte[] Payload, WebSocketMessageType Type)> _inbound;
        private WebSocketState _state = WebSocketState.Open;
        private int _sendCount;

        public StalledSenderWebSocket(int frameCount)
        {
            _inbound = new Queue<(byte[], WebSocketMessageType)>();
            _inbound.Enqueue(("{\"type\":\"start\"}"u8.ToArray(), WebSocketMessageType.Text));
            for (var sequence = 1; sequence <= frameCount; sequence++)
            {
                _inbound.Enqueue((
                    PersonaPlexVoiceProtocol.EncodeAudio(
                        PersonaPlexAudioFrame.Create(sequence, new short[1920])),
                    WebSocketMessageType.Binary));
            }
        }

        public TaskCompletionSource BinarySendBlocked { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource BlockedSendCanceled { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public override WebSocketCloseStatus? CloseStatus => null;

        public override string? CloseStatusDescription => null;

        public override WebSocketState State => _state;

        public override string? SubProtocol => null;

        public override void Abort() => _state = WebSocketState.Aborted;

        public override Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _state = WebSocketState.Closed;
            return Task.CompletedTask;
        }

        public override Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string? statusDescription,
            CancellationToken cancellationToken)
        {
            _state = WebSocketState.CloseSent;
            return Task.CompletedTask;
        }

        public override void Dispose() => _state = WebSocketState.Closed;

        public override Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken)
        {
            if (_inbound.TryDequeue(out var message))
            {
                message.Payload.AsSpan().CopyTo(buffer.AsSpan());
                return Task.FromResult(
                    new WebSocketReceiveResult(message.Payload.Length, message.Type, endOfMessage: true));
            }

            return WaitForReceiveCancellationAsync(cancellationToken);
        }

        public override Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(buffer.Count);
            if (Interlocked.Increment(ref _sendCount) == 1)
            {
                Assert.Equal(WebSocketMessageType.Text, messageType);
                Assert.True(endOfMessage);
                return Task.CompletedTask;
            }

            Assert.Equal(WebSocketMessageType.Binary, messageType);
            Assert.True(endOfMessage);
            BinarySendBlocked.TrySetResult();
            return WaitForSendCancellationAsync(cancellationToken);
        }

        private async Task<WebSocketReceiveResult> WaitForReceiveCancellationAsync(
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The scripted receive unexpectedly resumed.");
        }

        private async Task WaitForSendCancellationAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("The stalled send unexpectedly resumed.");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                BlockedSendCanceled.TrySetResult();
                throw;
            }
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PersonaPlexVoiceCollection
{
    public const string Name = "personaplex-voice";
}
