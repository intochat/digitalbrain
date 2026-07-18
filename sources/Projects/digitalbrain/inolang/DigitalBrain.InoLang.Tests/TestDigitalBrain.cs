using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using DigitalBrain.Kernel;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Runtime.Introspector;
using DigitalBrain.Runtime.Ui;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using DigitalBrain.InoLang.Tests.Internals;
using DigitalBrain.Runtime.Neurons;
using DigitalBrain.Runtime;

namespace DigitalBrain.InoLang.Tests;

public sealed class TestDigitalBrain : IAsyncDisposable
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    readonly WebApplication _app;
    readonly GrpcChannel _channel;
    readonly DigitalBrainGateway.DigitalBrainGatewayClient _client;
    readonly Metadata _headers;
    bool _isShuttingDown;

    private sealed class CorrelationState
    {
        public readonly List<Synapse> Replies = new();
        public readonly List<Waiter> Waiters = new();
    }

    private abstract class Waiter
    {
        public abstract bool TrySatisfy(Synapse synapse);
        public abstract void Cancel();
    }

    private sealed class TypedWaiter<TSynapse> : Waiter where TSynapse : Synapse
    {
        private readonly TaskCompletionSource<TSynapse> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<TSynapse> Task => _tcs.Task;

        public override bool TrySatisfy(Synapse synapse)
        {
            if (synapse is TSynapse expected)
            {
                return _tcs.TrySetResult(expected);
            }
            return false;
        }

        public override void Cancel()
        {
            _tcs.TrySetCanceled();
        }
    }

    readonly ConcurrentDictionary<Guid, CorrelationState> _correlationStates = new();
    readonly CancellationTokenSource _watcherCts = new();
    readonly Task? _homeFeedWatcherTask;

    TestDigitalBrain(WebApplication app, GrpcChannel channel, string kernelHttpsUrl)
    {
        _app = app;
        _channel = channel;
        _client = new DigitalBrainGateway.DigitalBrainGatewayClient(channel);
        _headers = new Metadata { { "x-active-scope", "user/local" } };
        KernelHttpsUrl = kernelHttpsUrl;

        _homeFeedWatcherTask = StartHomeFeedWatcher();
    }

    public IGrainFactory GrainFactory => _app.Services.GetRequiredService<IGrainFactory>();

    private Task StartHomeFeedWatcher()
    {
        return Task.Run(async () =>
        {
            try
            {
                await foreach (var envelope in WatchHomeFeedAsync(_watcherCts.Token))
                {
                    if (envelope.LibraryName == "synapse-broadcast")
                    {
                        var replyType = ResolveSynapseType(envelope.RootWidget);
                        if (replyType != null)
                        {
                            var parsed = (Synapse?)JsonSerializer.Deserialize(envelope.DataJson, replyType, JsonOptions);
                            if (parsed != null)
                            {
                                BufferSynapse(parsed);
                            }
                        }
                    }
                    else
                    {
                        var card = new RfwCard(
                            LibraryName: envelope.LibraryName,
                            RootWidget: envelope.RootWidget,
                            DataJson: envelope.DataJson) 
                        { 
                            Headers = SynapseMetadata.Create(
                                synapseId: Guid.NewGuid(),
                                correlationId: Guid.TryParse(envelope.CorrelationId, out var cid) ? cid : Guid.Empty,
                                causationId: null,
                                callerNeuronId: Guid.Empty,
                                callerNeuronType: "Gateway",
                                receiverNeuronId: Guid.Empty,
                                receiverNeuronType: "HomeFeed",
                                timestamp: DateTimeOffset.TryParse(envelope.Timestamp, out var ts) ? ts : DateTimeOffset.UtcNow
                            ) 
                        };

                        BufferSynapse(card);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception)
            {
                // Silence watcher exceptions to not crash the test runner
            }
        });
    }

    public string KernelHttpsUrl { get; }

    public string ActiveScope { get; private set; } = "user/local";
    public TestDigitalBrain? GlobalConnection { get; private set; }

    public void SwitchScope(string scope)
    {
        ActiveScope = string.IsNullOrEmpty(scope) ? BrainScopeHelper.GlobalScope : scope;
        for (var i = _headers.Count - 1; i >= 0; i--)
        {
            if (_headers[i].Key.Equals("x-active-scope", StringComparison.OrdinalIgnoreCase))
            {
                _headers.RemoveAt(i);
            }
        }
        _headers.Add("x-active-scope", ActiveScope);
    }

    public void ConnectToGlobal(TestDigitalBrain globalBrain)
    {
        GlobalConnection = globalBrain;
    }

    public Uri GetEndpoint(string resourceName, string endpointName) => new Uri("http://localhost");

    public bool HasResource(string resourceName) => true;

    public static Task<TestDigitalBrain> StartAsync(Action<TestDigitalBrainOptions>? configure = null)
    {
        if (configure is null) return TestDigitalBrainBootstrapper.GetAsync();

        var options = new TestDigitalBrainOptions();
        configure(options);
        return TestDigitalBrainBootstrapper.GetAsync(options);
    }

    public static ValueTask ShutdownIfBootedAsync() =>
        TestDigitalBrainBootstrapper.ShutdownIfBootedAsync();

    internal static async Task<TestDigitalBrain> BootAsync(TestDigitalBrainOptions options)
    {
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        Environment.SetEnvironmentVariable("DIGITALBRAIN_ACCEPT_LICENSE", "true");

        var builder = WebApplication.CreateBuilder();

        // Run as in-process TestServer
        builder.WebHost.UseTestServer();

        // Apply environment / configuration overrides
        foreach (var (key, value) in options.EnvironmentOverrides)
        {
            builder.Configuration[key] = value;
        }

        // Enforce required testing overrides to avoid binding to external systems/silos
        builder.Configuration["DigitalBrain__Mode"] = "Testing";
        
        if (options.ParallelIsolation)
        {
            var uniq = Guid.NewGuid().ToString("N");
            builder.Configuration["ORLEANS_CLUSTER_ID"] = $"cluster-{uniq}";
            builder.Configuration["ORLEANS_SERVICE_ID"] = $"service-{uniq}";
            builder.Configuration["DigitalBrain:Streams:Mode"] = "memory";
            builder.Configuration["ConnectionStrings:orleans-redis"] = "";
        }
        else
        {
            if (!options.EnvironmentOverrides.ContainsKey("ORLEANS_CLUSTER_ID"))
            {
                builder.Configuration["ORLEANS_CLUSTER_ID"] = "";
            }
            if (!options.EnvironmentOverrides.ContainsKey("ConnectionStrings:orleans-redis"))
            {
                builder.Configuration["ConnectionStrings:orleans-redis"] = "";
            }
            if (!options.EnvironmentOverrides.ContainsKey("DigitalBrain:Streams:Mode"))
            {
                builder.Configuration["DigitalBrain:Streams:Mode"] = "memory";
            }
        }

        // Call our shared bootstrapper
        DigitalBrainKernelBootstrapper.ConfigureServices(builder);

        var app = builder.Build();

        DigitalBrainKernelBootstrapper.ConfigurePipeline(app);

        await app.StartAsync();

        // Build zero-NIC unencrypted HTTP/2 gRPC channel
        var testServer = app.GetTestServer();
        var handler = testServer.CreateHandler();
        var channel = GrpcChannel.ForAddress("http://localhost", new GrpcChannelOptions
        {
            HttpHandler = handler
        });

        return new TestDigitalBrain(app, channel, "http://localhost");
    }

    public async Task Emit<TSynapse>(TSynapse synapse, CancellationToken ct = default)
        where TSynapse : Synapse
    {
        var concreteType = synapse.GetType();
        var envelope = new SynapseEnvelope
        {
            CorrelationId = synapse.CorrelationId.ToString(),
            TypeName = concreteType.FullName ?? "",
            Payload = ByteString.CopyFrom(
                JsonSerializer.SerializeToUtf8Bytes(synapse, concreteType, JsonOptions)),
        };

        var reply = await _client.SendAsync(envelope, headers: _headers, cancellationToken: ct);
        if (string.IsNullOrEmpty(reply.TypeName)) return;

        var replyType = ResolveSynapseType(reply.TypeName)
            ?? throw new InvalidOperationException(
                $"Cannot resolve reply synapse type '{reply.TypeName}' in any loaded assembly.");
        var parsed = (Synapse?)JsonSerializer.Deserialize(reply.Payload.Memory.Span, replyType, JsonOptions)
            ?? throw new InvalidOperationException(
                $"Reply payload of type '{reply.TypeName}' deserialized to null.");

        BufferSynapse(parsed);
    }

    private void BufferSynapse(Synapse synapse)
    {
        var state = _correlationStates.GetOrAdd(synapse.CorrelationId, _ => new CorrelationState());
        lock (state)
        {
            for (var i = 0; i < state.Waiters.Count; i++)
            {
                if (state.Waiters[i].TrySatisfy(synapse))
                {
                    state.Waiters.RemoveAt(i);
                    return;
                }
            }
            state.Replies.Add(synapse);
        }
    }

    public async Task<TSynapse> AwaitSynapse<TSynapse>(
        Guid correlationId,
        TimeSpan? timeout = null,
        CancellationToken ct = default)
        where TSynapse : Synapse
    {
        var state = _correlationStates.GetOrAdd(correlationId, _ => new CorrelationState());
        TypedWaiter<TSynapse>? waiter = null;

        lock (state)
        {
            for (var i = 0; i < state.Replies.Count; i++)
            {
                if (state.Replies[i] is TSynapse expected)
                {
                    state.Replies.RemoveAt(i);
                    return expected;
                }
            }

            waiter = new TypedWaiter<TSynapse>();
            state.Waiters.Add(waiter);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct);
        linked.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
        await using var registration = linked.Token.Register(() =>
        {
            lock (state)
            {
                state.Waiters.Remove(waiter);
            }
            waiter.Cancel();
        });

        try
        {
            return await waiter.Task;
        }
        catch (OperationCanceledException)
        {
            throw new TimeoutException(
                $"No reply for correlation {correlationId} matching {typeof(TSynapse).Name} within the configured timeout.");
        }
    }

    private static readonly ConcurrentDictionary<string, Type?> SynapseTypeCache = new();

    static Type? ResolveSynapseType(string typeName)
    {
        return SynapseTypeCache.GetOrAdd(typeName, name =>
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(name, throwOnError: false);
                if (t is not null && typeof(Synapse).IsAssignableFrom(t)) return t;
            }
            return null;
        });
    }

    public IAsyncEnumerable<RfwCardEnvelope> WatchHomeFeedAsync(CancellationToken ct = default)
    {
        var call = _client.WatchHomeFeed(new WatchHomeFeedRequest(), headers: _headers, cancellationToken: ct);
        return call.ResponseStream.ReadAllAsync(ct);
    }

    public IAsyncEnumerable<VisualLoadHintProto> WatchVisualLoadHintAsync(
        string clientId, CancellationToken ct = default)
    {
        var call = _client.WatchVisualLoadHint(
            new WatchVisualLoadHintRequest { ClientId = clientId },
            headers: _headers,
            cancellationToken: ct);
        return call.ResponseStream.ReadAllAsync(ct);
    }

    public async Task PushFlutterPerfAsync(
        IEnumerable<FlutterPerfSampleProto> samples, CancellationToken ct = default)
    {
        using var call = _client.PushFlutterPerf(headers: _headers, cancellationToken: ct);
        foreach (var s in samples)
            await call.RequestStream.WriteAsync(s, ct);
        await call.RequestStream.CompleteAsync();
        await call;
    }

    public async Task<Guid> SubmitPromptAsync(
        string text,
        string userId = "default",
        Guid? correlationId = null,
        CancellationToken ct = default)
    {
        var reply = await _client.SubmitPromptAsync(new SubmitPromptRequest
        {
            Text = text,
            UserId = userId,
            CorrelationId = correlationId?.ToString() ?? string.Empty,
        }, headers: _headers, cancellationToken: ct);
        return Guid.Parse(reply.CorrelationId);
    }

    public async Task<DeveloperSandboxReport> SendPromptAsync(string text, CancellationToken ct = default)
    {
        var correlationId = await SubmitPromptAsync(text, ct: ct);
        return await AwaitSynapse<DeveloperSandboxReport>(correlationId, TimeSpan.FromSeconds(15), ct);
    }

    public async Task<TranscribeResponse> TranscribeAsync(
        byte[] audio,
        string mimeType = "audio/wav",
        string? languageHint = null,
        int chunkSize = 16 * 1024,
        CancellationToken ct = default)
    {
        using var call = _client.Transcribe(headers: _headers, cancellationToken: ct);

        var firstChunk = true;
        for (var offset = 0; offset < audio.Length; offset += chunkSize)
        {
            var slice = audio.AsMemory(offset, Math.Min(chunkSize, audio.Length - offset));
            var msg = new TranscribeRequest
            {
                AudioChunk = ByteString.CopyFrom(slice.Span),
            };
            if (firstChunk)
            {
                msg.MimeType = mimeType;
                if (languageHint is not null) msg.LanguageHint = languageHint;
                firstChunk = false;
            }
            await call.RequestStream.WriteAsync(msg, ct);
        }

        if (firstChunk)
        {
            await call.RequestStream.WriteAsync(new TranscribeRequest
            {
                MimeType = mimeType,
                LanguageHint = languageHint ?? string.Empty,
            }, ct);
        }

        await call.RequestStream.CompleteAsync();
        return await call;
    }

    public async ValueTask DisposeAsync()
    {
        if (_isShuttingDown) return;
        _isShuttingDown = true;
        
        _watcherCts.Cancel();
        if (_homeFeedWatcherTask is not null)
        {
            try { await _homeFeedWatcherTask.ConfigureAwait(false); }
            catch { }
        }
        _watcherCts.Dispose();
        
        _channel.Dispose();
        try
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }
        catch
        {
            // Suppress shutdown exceptions in tests
        }
    }

    internal async ValueTask ShutdownAsync()
    {
        await DisposeAsync();
    }
}
