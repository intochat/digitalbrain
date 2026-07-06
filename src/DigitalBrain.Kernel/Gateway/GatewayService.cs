using DigitalBrain.Core;
using DigitalBrain.Core.Config;
using DigitalBrain.Core.Ui;
using DigitalBrain.Demo.Runtime;
using DigitalBrain.Google;
using DigitalBrain.Kernel.Auth;
using DigitalBrain.Kernel.Ui;
using DigitalBrain.Kernel.Voice;
using DigitalBrain.Runtime.Grpc;
using DigitalBrain.Salesforce;
using DigitalBrain.Telegram;
using Grpc.Core;

namespace DigitalBrain.Kernel.Gateway;

public sealed class GatewayService(
    IGrainFactory grains,
    IConfiguration configuration,
    HomeFeedBus homeFeedBus,
    SignalEgressBus signalEgressBus,
    IHostEnvironment environment,
    ILogger<GatewayService> logger,
    IPackConfigStore? packConfigStore = null,
    IVoiceTranscriber? voiceTranscriber = null) : DigitalBrainGateway.DigitalBrainGatewayBase
{
    private const long MaxTranscriptionBytes = 25L * 1024 * 1024;
    private readonly IVoiceTranscriber voiceTranscriber = voiceTranscriber ?? new NoOpVoiceTranscriber();

    public override async Task<SynapseEnvelope> Send(SynapseEnvelope request, ServerCallContext context)
    {
        try
        {
            var sendContext = new GatewaySendContext(
                grains,
                configuration,
                environment,
                logger,
                packConfigStore,
                ResolveSessionByClientIdAsync,
                InstallAndRunSurfaceDemoAsync);

            foreach (var handler in GatewaySendHandlers.Default)
            {
                if (await handler.TryHandleAsync(request, context, sendContext))
                {
                    return request;
                }
            }

            return request;
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Send failed for {TypeName}", request.TypeName);
            throw new RpcException(new Status(StatusCode.Internal, ex.GetBaseException().Message));
        }
    }

    // Server-driven UI: stream RfwCards to the client as neurons broadcast them, until the client disconnects.
    public override async Task WatchHomeFeed(WatchHomeFeedRequest request, IServerStreamWriter<RfwCardEnvelope> responseStream, ServerCallContext context)
    {
        logger.LogInformation("WatchHomeFeed opened for {Peer}", context.Peer);
        var clientId = string.IsNullOrWhiteSpace(request.ClientId) ? null : request.ClientId;

        // The first card a client sees is the login surface — pre-fill it with the dev credentials in
        // Development. clientId rides along on the form's submitAction payload (UiSurfaceRuntime.Login), so
        // the client's own submit button re-sends it with no further Flutter code needed for that leg.
        var initialLogin = DevAuth.Enabled(configuration, environment)
            ? UiSurfaceSamples.Login(clientId: clientId ?? "flutter", defaultUsername: DevAuth.Username, defaultPassword: DevAuth.Password)
            : UiSurfaceSamples.Login(clientId: clientId ?? "flutter");
        await WriteCardAsync(responseStream, UiSurfaceRfwBridge.FromUiSurface(initialLogin, "session-main"));
        logger.LogInformation("WatchHomeFeed sent initial login surface to {Peer}", context.Peer);

        await using var subscription = await homeFeedBus.SubscribeAsync(clientId);
        await foreach (var card in subscription.Reader.ReadAllAsync(context.CancellationToken))
        {
            await WriteCardAsync(responseStream, card);
        }
    }

    // Resolves a client-supplied clientId once; callers must use the result's fields downstream, never trust
    // a raw client-supplied userId/sessionId directly.
    private async Task<UserSessionState?> ResolveSessionByClientIdAsync(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return null;
        var session = grains.GetGrain<IUserSessionNeuron>("session-main");
        return await session.GetSessionByClientIdAsync(clientId);
    }

    // Egress for external transports: stream broadcast Signals whose Name is in the request filter (empty = all),
    // until the client disconnects. Each Signal becomes a SynapseEnvelope carrying its Props as UTF-8 JSON.
    public override async Task WatchSynapses(WatchSynapsesRequest request, IServerStreamWriter<SynapseEnvelope> responseStream, ServerCallContext context)
    {
        logger.LogInformation("WatchSynapses opened for {Peer} (filter: {Filter})", context.Peer, string.Join(",", request.TypeFilter));
        using var subscription = signalEgressBus.Subscribe(request.TypeFilter.ToArray());
        await foreach (var signal in subscription.Reader.ReadAllAsync(context.CancellationToken))
        {
            await responseStream.WriteAsync(new SynapseEnvelope
            {
                TypeName = signal.Name,
                CorrelationId = signal.CorrelationId ?? string.Empty,
                Payload = global::Google.Protobuf.ByteString.CopyFrom(
                    System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(signal.Props))
            });
        }
    }

    // Point-to-point pull of a pack's decrypted config over the internal gRPC channel. Same trust level as the
    // startup env param — the secret travels here, never on the broadcast timeline/egress. Values are NOT logged.
    //
    // SECURITY: this RPC lives on the same DigitalBrainGateway service that the browser/Flutter client reaches on
    // the external ingress (CORS is not a security boundary). It is therefore gated by a service-to-service secret:
    // only callers that present the configured InternalServiceKey (the kernel + internal transports — never the
    // browser, which is not given the key) may pull decrypted secrets.
    public override async Task<PackConfigReply> GetPackConfig(GetPackConfigRequest request, ServerCallContext context)
    {
        GatewayInternalAuth.Enforce(configuration, environment, logger, context, nameof(GetPackConfig));

        if (packConfigStore is null)
            throw new RpcException(new Status(StatusCode.FailedPrecondition, "Pack config store is not configured."));

        var scope = string.IsNullOrWhiteSpace(request.Scope) ? "default" : request.Scope;
        var values = await packConfigStore.GetAsync(scope, request.Pack);

        var reply = new PackConfigReply();
        foreach (var (key, value) in values)
            reply.Values[key] = value;
        return reply;
    }


    private static Task WriteCardAsync(
        IServerStreamWriter<RfwCardEnvelope> responseStream,
        RfwCard card) =>
        responseStream.WriteAsync(new RfwCardEnvelope
        {
            LibraryName = card.LibraryName,
            RootWidget = card.RootWidget,
            DataJson = card.DataJson,
            CorrelationId = card.CorrelationId ?? string.Empty,
            Timestamp = card.Timestamp.ToString("O"),
            CallerNeuronType = card.Sender?.Value ?? string.Empty
        });

    public override Task<HealthReply> Health(HealthRequest request, ServerCallContext context) =>
        Task.FromResult(new HealthReply
        {
            Ok = true,
            LlmMode = configuration["DigitalBrain:Llm:Provider"] ?? "none"
        });

    public override async Task<AskReply> Ask(AskRequest request, ServerCallContext context)
    {
        var neuronId = string.IsNullOrWhiteSpace(request.NeuronId) ? "ino-main" : request.NeuronId;
        if (neuronId != "ino-main")
            throw new RpcException(new Status(StatusCode.InvalidArgument, "Ask currently supports only 'ino-main'."));

        try
        {
            var ino = grains.GetGrain<IInoNeuron>(neuronId);
            return new AskReply { Text = await ino.AskAsync(request.Prompt) };
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Ask failed for {NeuronId}", neuronId);
            throw new RpcException(new Status(StatusCode.Internal, ex.GetBaseException().Message));
        }
    }

    public override async Task<FireReply> Fire(FireRequest request, ServerCallContext context)
    {
        try
        {
            var neuron = NeuronResolver.Resolve(grains, request.NeuronId);
            await neuron.FireAsync(new DemoMessageSynapse(request.Text));
            return new FireReply { Accepted = true };
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fire failed for {NeuronId}", request.NeuronId);
            throw new RpcException(new Status(StatusCode.Internal, ex.GetBaseException().Message));
        }
    }

    public override async Task<TimelineReply> Timeline(TimelineRequest request, ServerCallContext context)
    {
        var max = request.MaxEntries <= 0 ? 10 : request.MaxEntries;
        INeuron neuron;
        try
        {
            neuron = NeuronResolver.Resolve(grains, request.NeuronId);
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
        try
        {
            var timeline = await neuron.GetTimelineAsync();
            var reply = new TimelineReply();
            foreach (var s in timeline.TakeLast(max))
            {
                reply.Entries.Add(new TimelineEntry
                {
                    Type = s.Type,
                    Timestamp = s.Timestamp.ToString("O"),
                    Text = s is DemoMessageSynapse demo ? demo.Text : (s.ToString() ?? string.Empty)
                });
            }
            return reply;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Timeline failed for {NeuronId}", request.NeuronId);
            throw new RpcException(new Status(StatusCode.Internal, ex.GetBaseException().Message));
        }
    }

    public override async Task<TranscribeResponse> Transcribe(IAsyncStreamReader<TranscribeRequest> requestStream, ServerCallContext context)
    {
        var correlationId = Guid.NewGuid().ToString("N");
        await using var audio = new MemoryStream();
        string? mimeType = null;
        string? languageHint = null;

        while (await requestStream.MoveNext(context.CancellationToken))
        {
            var request = requestStream.Current;
            if (!string.IsNullOrWhiteSpace(request.MimeType))
            {
                mimeType = request.MimeType;
            }

            if (!string.IsNullOrWhiteSpace(request.LanguageHint))
            {
                languageHint = request.LanguageHint;
            }

            if (request.AudioChunk.Length == 0)
            {
                continue;
            }

            if (audio.Length + request.AudioChunk.Length > MaxTranscriptionBytes)
            {
                throw new RpcException(new Status(StatusCode.ResourceExhausted, "Transcription audio is too large."));
            }

            await audio.WriteAsync(request.AudioChunk.ToByteArray(), context.CancellationToken);
        }

        if (audio.Length == 0)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "No audio was provided."));
        }

        try
        {
            var result = await voiceTranscriber.TranscribeAsync(
                new VoiceTranscriptionRequest(
                    audio.ToArray(),
                    string.IsNullOrWhiteSpace(mimeType) ? "application/octet-stream" : mimeType,
                    languageHint,
                    correlationId),
                context.CancellationToken);

            return new TranscribeResponse
            {
                Transcript = result.Transcript,
                DetectedLanguage = result.DetectedLanguage ?? string.Empty,
                CorrelationId = string.IsNullOrWhiteSpace(result.CorrelationId)
                    ? correlationId
                    : result.CorrelationId
            };
        }
        catch (RpcException)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Transcribe failed for {CorrelationId}", correlationId);
            throw new RpcException(new Status(StatusCode.Internal, ex.GetBaseException().Message));
        }
    }

    private async Task InstallAndRunSurfaceDemoAsync(string correlationId)
    {
        var requestCorrelationId = string.IsNullOrWhiteSpace(correlationId)
            ? Guid.NewGuid().ToString("N")
            : correlationId;

        var pack = SurfaceDemoRuntime.SignedPack();
        var marketplace = grains.GetGrain<IMarketplaceNeuron>("market-ui-demo");
        var generated = grains.GetGrain<IGeneratedNeuron>(SurfaceDemoRuntime.GeneratedNeuronKey);

        await PublishSurfaceDemoGraphAsync(requestCorrelationId, "request accepted");

        await marketplace.FireAsync(new PublishToMarketplace(
            pack.Name,
            pack.Version,
            pack.Code,
            pack.OwnerId,
            pack.IsPrivate,
            pack.CommissionRate,
            pack.Description,
            pack.AuthorPublicKeyBase64,
            pack.SignatureBase64)
        {
            CorrelationId = requestCorrelationId
        });

        await PublishSurfaceDemoGraphAsync(requestCorrelationId, "signed pack published to marketplace");

        await marketplace.FireAsync(new InstallFromMarketplace(pack.Name, pack.Version, BuyerId: "flutter-demo")
        {
            CorrelationId = requestCorrelationId
        });

        await PublishSurfaceDemoGraphAsync(requestCorrelationId, "pack installed into generated neuron");

        var demoText = string.IsNullOrWhiteSpace(correlationId)
            ? "flutter-live-demo"
            : correlationId;
        await generated.FireAsync(new DemoMessageSynapse(demoText)
        {
            CorrelationId = requestCorrelationId
        });

        var generatedTimeline = await generated.GetOutgoingTimelineAsync();
        await PublishSurfaceDemoGraphAsync(requestCorrelationId, "journaled response and surface update observed", generatedTimeline);
    }

    private async Task PublishSurfaceDemoGraphAsync(
        string correlationId,
        string phase,
        IReadOnlyList<Synapse>? generatedTimeline = null)
    {
        var surface = SurfaceDemoRuntime.ActivityGraphSurface(correlationId, phase, generatedTimeline);
        var observability = grains.GetGrain<IObservabilityNeuron>(SurfaceDemoRuntime.ObservabilityNeuronKey);
        try
        {
            await observability.FireAsync(surface);
            logger.LogInformation("Published journaled surface demo graph phase={Phase} correlation={CorrelationId}", phase, correlationId);
        }
        catch (Exception ex) when (IsObservabilityJournalUnavailable(ex))
        {
            logger.LogWarning(ex, "Observability neuron unavailable; streaming graph surface without blocking phase={Phase} correlation={CorrelationId}", phase, correlationId);
            await homeFeedBus.BroadcastAsync(UiSurfaceRfwBridge.FromUiSurface(surface, "digitalbrain.gateway"));
        }
    }

    private static bool IsObservabilityJournalUnavailable(Exception exception) =>
        exception.GetBaseException().Message.Contains("state journal stream writer is not initialized", StringComparison.OrdinalIgnoreCase);
}


