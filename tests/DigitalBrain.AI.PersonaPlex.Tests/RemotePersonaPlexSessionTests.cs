using System.Net;
using System.Net.WebSockets;
using System.Text;
using DigitalBrain.AI.PersonaPlex;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace DigitalBrain.AI.PersonaPlex.Tests;

public sealed class RemotePersonaPlexSessionTests
{
    [Fact]
    public void PcmRoundTripPreservesLittleEndianSamples()
    {
        var pcm = Enumerable.Range(0, 1920).Select(static sample => (short)(sample - 960)).ToArray();
        var bytes = new byte[RemotePersonaPlexSession.FrameByteCount];

        RemotePersonaPlexSession.WritePcm16LittleEndian(pcm, bytes);
        var decoded = new short[1920];
        RemotePersonaPlexSession.ReadPcm16LittleEndian(bytes, decoded);

        Assert.Equal(pcm, decoded);
        Assert.Equal(0x40, bytes[0]);
        Assert.Equal(0xfc, bytes[1]);
    }

    [Fact]
    public async Task FactoryBecomesReadyWhenAdapterReportsReady()
    {
        await using var adapter = await FakeAdapter.StartAsync(readyImmediately: true);
        await using var factory = CreateFactory(adapter);

        await factory.StartAsync(TestContext.Current.CancellationToken);
        await WaitForReadyAsync(factory);

        Assert.Equal(PersonaPlexReadinessState.Ready, factory.Readiness.State);
        await using var session = await factory.CreateAsync(
            new PersonaPlexSessionRequest("connection-1"),
            TestContext.Current.CancellationToken);

        var input = PersonaPlexAudioFrame.Create(7, Enumerable.Repeat((short)42, 1920).ToArray());
        var output = await session.ProcessAsync(input, TestContext.Current.CancellationToken);

        Assert.Equal(7, output.Sequence);
        Assert.All(output.Pcm16.ToArray(), static sample => Assert.Equal((short)-42, sample));
        Assert.Equal(1, adapter.AuthorizedStreamCount);
    }

    [Fact]
    public async Task FactoryCreateAsyncThrowsWhileAdapterIsUnavailable()
    {
        await using var adapter = await FakeAdapter.StartAsync(readyImmediately: false);
        await using var factory = CreateFactory(adapter);

        await factory.StartAsync(TestContext.Current.CancellationToken);
        await Task.Delay(200, TestContext.Current.CancellationToken);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await factory.CreateAsync(
                new PersonaPlexSessionRequest("connection-1"),
                TestContext.Current.CancellationToken));

        Assert.Equal(PersonaPlexReadinessState.Loading, factory.Readiness.State);
        Assert.Contains("loading", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static RemotePersonaPlexSessionFactory CreateFactory(FakeAdapter adapter)
        => new(
            Options.Create(new PersonaPlexOptions
            {
                Enabled = true,
                UseRemoteRuntime = true,
                RuntimeEndpoint = adapter.BaseAddress.ToString().TrimEnd('/'),
                AdapterToken = FakeAdapter.Token,
                MaxSessions = 1,
            }),
            NullLogger<RemotePersonaPlexSessionFactory>.Instance);

    private static async Task WaitForReadyAsync(RemotePersonaPlexSessionFactory factory)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            if (factory.Readiness.State == PersonaPlexReadinessState.Ready)
            {
                return;
            }

            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.Fail($"Factory did not become ready: {factory.Readiness.State} {factory.Readiness.Message}");
    }

    private sealed class FakeAdapter : IAsyncDisposable
    {
        public const string Token = "test-adapter-token";

        private readonly HttpListener _listener = new();
        private readonly CancellationTokenSource _cancellation = new();
        private Task? _loop;

        private FakeAdapter()
        {
        }

        public Uri BaseAddress { get; private set; } = null!;

        public bool ReadyImmediately { get; private set; }

        public int AuthorizedStreamCount { get; private set; }

        public static async Task<FakeAdapter> StartAsync(bool readyImmediately)
        {
            var adapter = new FakeAdapter { ReadyImmediately = readyImmediately };
            for (var port = 18_800; port < 19_000; port++)
            {
                adapter.BaseAddress = new Uri($"http://127.0.0.1:{port}/");
                adapter._listener.Prefixes.Clear();
                adapter._listener.Prefixes.Add(adapter.BaseAddress.ToString());
                try
                {
                    adapter._listener.Start();
                    adapter._loop = adapter.RunAsync(adapter._cancellation.Token);
                    return adapter;
                }
                catch (HttpListenerException)
                {
                }
            }

            throw new InvalidOperationException("Unable to bind FakeAdapter listener.");
        }

        public async ValueTask DisposeAsync()
        {
            _cancellation.Cancel();
            _listener.Stop();
            if (_loop is not null)
            {
                try
                {
                    await _loop.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
            }

            _listener.Close();
            _cancellation.Dispose();
        }

        private async Task RunAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleAsync(context), CancellationToken.None);
            }
        }

        private async Task HandleAsync(HttpListenerContext context)
        {
            if (context.Request.Url?.AbsolutePath == "/readyz")
            {
                var ready = ReadyImmediately;
                var payload = ready
                    ? """{"state":"ready","mode":"cuda","message":"Official PersonaPlex runtime is ready."}"""
                    : """{"state":"loading","mode":"unavailable","message":"Loading official PersonaPlex runtime."}""";
                var bytes = Encoding.UTF8.GetBytes(payload);
                context.Response.StatusCode = ready ? 200 : 503;
                context.Response.ContentType = "application/json";
                await context.Response.OutputStream.WriteAsync(bytes);
                context.Response.Close();
                return;
            }

            if (context.Request.Url?.AbsolutePath == "/stream" && context.Request.IsWebSocketRequest)
            {
                var authorization = context.Request.Headers["Authorization"];
                if (!string.Equals(authorization, $"Bearer {Token}", StringComparison.Ordinal))
                {
                    context.Response.StatusCode = 403;
                    context.Response.Close();
                    return;
                }

                var socketContext = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);
                AuthorizedStreamCount++;
                var socket = socketContext.WebSocket;
                var buffer = new byte[RemotePersonaPlexSession.FrameByteCount];
                while (socket.State == WebSocketState.Open)
                {
                    var result = await socket.ReceiveAsync(buffer, CancellationToken.None).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "closed",
                            CancellationToken.None).ConfigureAwait(false);
                        return;
                    }

                    if (result.MessageType != WebSocketMessageType.Binary || !result.EndOfMessage
                        || result.Count != buffer.Length)
                    {
                        await socket.CloseAsync(
                            WebSocketCloseStatus.InvalidPayloadData,
                            "bad frame",
                            CancellationToken.None).ConfigureAwait(false);
                        return;
                    }

                    var pcm = new short[1920];
                    RemotePersonaPlexSession.ReadPcm16LittleEndian(buffer, pcm);
                    for (var index = 0; index < pcm.Length; index++)
                    {
                        pcm[index] = (short)-pcm[index];
                    }

                    RemotePersonaPlexSession.WritePcm16LittleEndian(pcm, buffer);
                    await socket.SendAsync(
                        buffer,
                        WebSocketMessageType.Binary,
                        endOfMessage: true,
                        CancellationToken.None).ConfigureAwait(false);
                }

                return;
            }

            context.Response.StatusCode = 404;
            context.Response.Close();
        }
    }
}
