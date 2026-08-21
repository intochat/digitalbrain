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
        var factory = new FakeSessionFactory();
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

        await factory.SessionDisposed.Task.WaitAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task WebSocketRejectsAnOutOfOrderInputSequence()
    {
        var factory = new FakeSessionFactory();
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
        await factory.SessionDisposed.Task.WaitAsync(TestContext.Current.CancellationToken);
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

    private sealed class FakeSessionFactory : IPersonaPlexSessionFactory
    {
        public TaskCompletionSource SessionDisposed { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<IPersonaPlexSession> CreateAsync(
            PersonaPlexSessionRequest request,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult<IPersonaPlexSession>(new FakeSession(SessionDisposed));
    }

    private sealed class ThrowingSessionFactory : IPersonaPlexSessionFactory
    {
        public ValueTask<IPersonaPlexSession> CreateAsync(
            PersonaPlexSessionRequest request,
            CancellationToken cancellationToken = default)
            => throw new InvalidOperationException("sensitive-model-detail");
    }

    private sealed class FakeSession(TaskCompletionSource sessionDisposed) : IPersonaPlexSession
    {
        public ValueTask<PersonaPlexAudioFrame> ProcessAsync(
            PersonaPlexAudioFrame frame,
            CancellationToken cancellationToken = default)
            => ValueTask.FromResult(
                PersonaPlexAudioFrame.Create(
                    frame.Sequence,
                    frame.Pcm16.Span.ToArray().Select(static sample => (short)-sample).ToArray()));

        public ValueTask ResetAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync()
        {
            sessionDisposed.TrySetResult();
            return ValueTask.CompletedTask;
        }
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class PersonaPlexVoiceCollection
{
    public const string Name = "personaplex-voice";
}
